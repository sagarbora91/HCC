// MB-06 money (phone) — pure helpers shared by the phone screens and tests/stream-mobile-money.mjs.
// The command builders here produce EXACTLY the commands the phone screens send, using the same command types and
// fields as the desktop dialogs (PaymentModal, CheckoutModal, ShiftCountForm, ExpenseModal). No React imports.
import type {State,Booking,Command} from '../../services/mock';
import {totals,folioNights,folioCheckoutBlockers,folioCompanyCreditState,folioApprovalExpired,shiftDenominationTotal,SHIFT_DENOMINATIONS,addDays} from '../../services/mock';

/** Phone tender chips → command method values (FOLIO_TENDERS). */
export const MM_TENDERS:{id:string;label:string}[]=[{id:'Cash',label:'Cash'},{id:'UPI',label:'UPI'},{id:'Card',label:'Card'},{id:'Bank transfer',label:'Bank'}];
export const MM_PAY_KINDS:{id:'payment'|'deposit'|'deposit-return';label:string;long:string}[]=[
  {id:'payment',label:'Payment',long:'Payment / advance against folio'},
  {id:'deposit',label:'Deposit',long:'Receive refundable deposit'},
  {id:'deposit-return',label:'Return deposit',long:'Return refundable deposit'},
];
export type MmTenderRow={method:string;amount:string;reference:string};

/** Rupee text (keypad input, may contain commas/spaces/₹) → integer paise; NaN when not a valid non-negative amount. */
export function mmPaise(v:string):number{
  const t=String(v??'').replace(/[₹,\s]/g,'');
  if(!t||!/^\d*(\.\d{0,2})?$/.test(t)||t==='.')return NaN;
  return Math.round(Number(t)*100);
}
/** Paise → plain rupee text for an input ("3400", "12.5"). */
export const mmRupeeText=(p:number)=>p>0?String(p/100):'';

/** Effective tender rows: a single row takes the total amount (same rule as the desktop dialog). */
export function mmEffectiveRows(total:string,rows:MmTenderRow[]):MmTenderRow[]{return rows.map(r=>({...r,amount:rows.length===1?total:r.amount}));}

/** Client-side check before the confirm screen; '' when OK. The command re-validates everything. */
export function mmPaymentProblem(kind:string,total:string,rows:MmTenderRow[],deposit:number):string{
  const target=mmPaise(total),eff=mmEffectiveRows(total,rows);
  if(!(target>0))return 'Enter an amount greater than zero.';
  if(eff.some(r=>!(mmPaise(r.amount)>0)))return 'Every tender row needs an amount greater than zero.';
  const sum=eff.reduce((a,r)=>a+mmPaise(r.amount),0);
  if(sum!==target)return `Tender rows total ₹${(sum/100).toLocaleString('en-IN')} but the amount is ₹${(target/100).toLocaleString('en-IN')}. They must be equal.`;
  if(eff.some(r=>r.method!=='Cash'&&!r.reference.trim()))return 'Enter a demonstration transaction reference for each non-cash tender.';
  if(kind==='deposit-return'&&rows.length>1)return 'Return a deposit with a single tender.';
  if(kind==='deposit-return'&&target>deposit)return 'Return cannot exceed the deposit held.';
  return '';
}
/** The PAYMENT command exactly as the desktop PaymentModal builds it (single tender → method/reference; split → tenders[]). */
export function mmPaymentCommand(bookingId:string,kind:string,total:string,rows:MmTenderRow[],key?:string):Command{
  const eff=mmEffectiveRows(total,rows),amount=mmPaise(total);
  const cmd:Command=eff.length===1
    ?{type:'PAYMENT',id:bookingId,amount,kind,method:eff[0].method,reference:eff[0].reference.trim()}
    :{type:'PAYMENT',id:bookingId,amount,kind,tenders:eff.map(r=>({method:r.method,amount:mmPaise(r.amount),reference:r.reference.trim()}))};
  if(key)cmd.key=key;
  return cmd;
}

/** Balance hero for the phone folio: Due / Credit / Settled, plus deposit held separately. */
export function mmBalanceHero(b:Booking){
  const t=totals(b);
  return {...t,state:(t.balance>0?'due':t.balance<0?'credit':'settled') as 'due'|'credit'|'settled',label:t.balance>0?'Balance due':t.balance<0?'Credit balance':'Settled'};
}

export type MmCheckItem={id:'nights'|'balance'|'deposit'|'requests';label:string;ok:boolean;detail:string};
/** Checkout checklist (M11). Each item mirrors one CHECKOUT rule; the command remains the authority. */
export function mmCheckoutChecklist(s:State,b:Booking):{items:MmCheckItem[];clear:boolean;blockers:string[]}{
  const t=totals(b),n=folioNights(b);
  const missing:string[]=[];for(let d=b.arrival;d<b.departure;d=addDays(d,1))if(!b.lines.some(l=>l.kind==='room'&&l.date===d))missing.push(d);
  const cc=folioCompanyCreditState(s,b,t.balance);
  const pending=s.approvals.filter(a=>a.booking===b.id&&a.status==='Pending'&&!folioApprovalExpired(s,a));
  const items:MmCheckItem[]=[
    {id:'nights',label:'Room nights posted',ok:!missing.length,detail:`${n.posted} of ${n.agreed} agreed${missing.length?` · missing ${missing.join(', ')}`:''}`},
    {id:'balance',label:'Balance settled',ok:t.balance===0||(t.balance>0&&cc.state==='approved'),
      detail:t.balance===0?'₹0 outstanding':t.balance>0?(cc.state==='approved'?'Approved company credit covers the balance':'Balance due — record a payment'):'Credit balance — refund the excess receipt first'},
    {id:'deposit',label:'Deposit returned',ok:t.deposit===0,detail:t.deposit===0?'No deposit held':'Deposit still held — return it first'},
    {id:'requests',label:'No pending requests',ok:!pending.length,detail:pending.length?`${pending.length} pending: ${pending.map(a=>a.id).join(', ')}`:'Nothing awaiting approval'},
  ];
  const blockers=folioCheckoutBlockers(s,b,t);
  return {items,clear:b.status==='In house'&&!blockers.length&&items.every(i=>i.ok),blockers};
}
/** CHECKOUT command (same key form as the desktop CheckoutModal, so a retry on either device is not duplicated). */
export const mmCheckoutCommand=(bookingId:string):Command=>({type:'CHECKOUT',id:bookingId,key:'checkout:'+bookingId});

/** Denomination counts (NumberSteppers) + coins (rupee text) → the record SHIFT_SUBMIT validates. */
export function mmDenominations(notes:Record<number,number>,coins:string):Record<string,number>{
  const d:Record<string,number>={};for(const n of SHIFT_DENOMINATIONS)d[n]=Math.max(0,Math.floor(notes[n]||0));
  const c=mmPaise(coins||'0');d.coins=Number.isFinite(c)?c/100:0;return d;
}
export const mmCountTotal=(d:Record<string,number>)=>shiftDenominationTotal(d);
/** SHIFT_SUBMIT command as sent by the phone count screen (denominations mode or total mode). */
export function mmShiftSubmitCommand(counted:number,reason:string,denominations?:Record<string,number>,key?:string):Command{
  const cmd:Command={type:'SHIFT_SUBMIT',counted};
  if(reason.trim())cmd.reason=reason.trim();
  if(denominations)cmd.denominations=denominations;
  if(key)cmd.key=key;
  return cmd;
}
/** Difference label: "None · exact" / "Over ₹x" / "Short ₹x" (sign only, money formatted by caller). */
export const mmDiffKind=(diff:number)=>diff===0?'exact':diff>0?'over':'short';

/** EXPENSE command as sent by the phone Add-expense sheet (same fields as the desktop ExpenseModal). */
export function mmExpenseCommand(f:{vendor:string;category:string;method:string;amount:string;receipt:string;notes:string},draft:boolean,key?:string):Command{
  const cmd:Command={type:'EXPENSE',vendor:f.vendor,category:f.category,method:f.method,amount:mmPaise(f.amount),receipt:f.receipt,notes:f.notes,draft};
  if(key)cmd.key=key+(draft?':draft':':submit');
  return cmd;
}
