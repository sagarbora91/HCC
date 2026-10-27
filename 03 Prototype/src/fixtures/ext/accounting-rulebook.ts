// Module 20 · Accounting — the rulebook golden month (ACC-1 subset), scenario ACC-RULEBOOK. Fictional data only.
// Rulebook: 02 Planning/HCC_Accounting_Rulebook_v1.md §8 (April 2026: opening ₹77,40,000, transactions T1–T19, closing trial balance ₹84,27,500).
// ACC-1 posts only part of the month: this fixture drives the transactions the engine can book through REAL commands — the hand-entered
// opening balances (contract §5: ACC_OPENING_SAVE / ACC_OPENING_APPROVE), room nights with an invoice discount, restaurant on folios, walk-in
// outlet bills, the advance applied, company credit, guest payments by cash / UPI / card, the company's bank payment, two vendor bills on
// vendor credit, electricity by bank, sundry cash expenses, a salary advance, cash banked and a cash shortage — day by day from 1 April,
// closing every business day exactly as the cashier does (night posting, exact cash count, day close, next day). What ACC-1 cannot book
// (T4, T6, T7b, T11, T13b/c, T14, T17–T19 and the Owner's drawing from the bank) is left out; tests/stream-accounting-rulebook.mjs holds
// the expectation table and the list of gaps. The month is the ACC-2 target once the rest is built.
// The command engine is injected (accRulebookBind, bound once in services/mock/rulebook-bind.ts): a fixture must not import the command
// registry, or the scenario worker would carry every handler.
import type {State,Booking,Command} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import {totals,drawer} from '../../domain/selectors';
import {mnyAccounts,mnyMethods} from '../../domain/streams/money-master';

type Exec=(s:State,uid:string,cmd:Command)=>State;
let engine:Exec|undefined;
/** Called once by services/mock/rulebook-bind.ts with the real `execute`. */
export function accRulebookBind(execute:Exec):void{engine=execute;}

export const ACC_RULEBOOK='ACC-RULEBOOK';
/** The fictional parties of the month (rulebook §8.1). Codes are the master row codes; the party ledger id is `<control>/<code or id>` (contract §3). */
export const ACC_RB={
  asOf:'2026-04-01',
  company:{name:'Deccan Agro Ltd',gstin:'27ZZFIC0004F1Z2'},
  vendors:[{code:'EV-MORNING',name:'Morning Supplies'},{code:'EV-KAVERI',name:'Kaveri Textiles'}],
  bank:{code:'BA-HDFC',name:'HDFC'},
  channel:{id:'goibibo',name:'Goibibo'},
  staff:'HR-S07', // "Staff-07" of the rulebook (existing staff row)
  advanceMethod:'Advance adjusted',
};

const OWNER='owner',MANAGER='manager',ACCOUNTANT='accountant';
const P=(rupees:number)=>Math.round(rupees*100);
const addDays=(d:string,n:number)=>{const x=new Date(d+'T00:00:00Z');x.setUTCDate(x.getUTCDate()+n);return x.toISOString().slice(0,10);};
const photo='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';

/** The opening balances of rulebook §8.1 as ledger lines (paise). Party ledgers use the master codes / ids created by the fixture. */
export function accRulebookOpeningLines(till:string,companyId:string):{account:string;debit:number;credit:number}[]{
  const d=(account:string,r:number)=>({account,debit:P(r),credit:0}),c=(account:string,r:number)=>({account,debit:0,credit:P(r)});
  return [d('1000/'+till,20000),d('1030/'+ACC_RB.bank.code,300000),d('1500',6000000),d('1510',800000),d('1520',500000),d('1530',80000),d('1110/'+companyId,40000),
    c('2200/'+ACC_RB.vendors[0].code,25000),c('2000',10000),c('2400',2000000),c('3100',5705000)];
}

/** Stays of the month: [room, nights, group]. Deccan Agro: 6 classic rooms × 5 nights = 30 nights (₹60,000). Guests: 55 classic + 50 deluxe + 20 suite nights. */
const DECCAN=['101','102','105','108','201','202'];
const GUESTS:{room:string;nights:number}[]=[
  {room:'205',nights:10},{room:'208',nights:10},{room:'301',nights:10},{room:'302',nights:10},{room:'305',nights:10},{room:'308',nights:5},
  {room:'103',nights:10},{room:'104',nights:10},{room:'107',nights:10},{room:'203',nights:10},{room:'204',nights:10},
  {room:'106',nights:10},{room:'206',nights:10},
];
/** Room-service check charged to a folio: net ₹5,000 = chicken curry ×6 + paneer butter masala ×4 + paneer tikka ×2 (460×6 + 380×4 + 360×2). */
const FOLIO_CHECK=[['m-curry',6],['m-paneer',4],['m-tikka',2]] as const;
/** Walk-in outlet check: net ₹6,000 = thali ×7 + curry ×5 + paneer butter masala ×2. */
const OUTLET_CHECK=[['m-thali',7],['m-curry',5],['m-paneer',2]] as const;

/** What happens on which April day (T-numbers are the rulebook's). Every day also runs the night audit, the exact cash count, the day close and the next day. */
export const ACC_RB_PLAN:Record<string,string>={
  '2026-04-01':'Opening entry saved by the Manager and approved by the Owner; Deccan Agro (6 rooms × 5 nights) and 13 guest stays check in',
  '2026-04-02…10':'T2 room-service checks charged to folios (20 × ₹5,000 over days 1–9), T3 walk-in outlet checks (10 × ₹6,000 net, every second day, cash and UPI alternately)',
  '2026-04-05':'T8 Morning Supplies bill ₹59,000 on vendor credit (ITC disallowed)',
  '2026-04-06':'T5b Deccan Agro checks out on company credit ₹63,000; T5a/T5c room 308 checks out (advance applied first)',
  '2026-04-08':'T13a salary advance to HR-S07 ₹5,000 (a bank transfer in HCC; no voucher in ACC-1)',
  '2026-04-10':'T7a Deccan Agro pays ₹40,000 by bank transfer',
  '2026-04-11':'T1 discount ₹10,000 (Owner); T5a/T5c the other 12 guests check out and pay: advance, cash, UPI, card',
  '2026-04-12':'T9 Kaveri Textiles bill ₹42,000 on vendor credit (ITC disallowed)',
  '2026-04-14,18':'T12 sundry cash expenses 2 × ₹4,000',
  '2026-04-15':'T10 electricity ₹45,000 paid by bank',
  '2026-04-25':'T15a cash banked ₹1,50,000 (slip, photo, confirmed by the Owner)',
  '2026-04-26':'T15b cash short ₹200 accepted by the Owner',
};

function need(f:Exec|undefined):Exec{if(!f)throw new Error('ACC-RULEBOOK needs the command engine (services/mock/rulebook-bind.ts binds it).');return f;}

/**
 * Drives April 2026 on `s0` (any state: it is cleared of operations, dated 1 April and run forward). Returns the state after 30 April is
 * closed and 1 May is open. Every change goes through `exec` (the real command engine); only master-data setup that has no command is
 * written directly (the OTA channel row, the company's opening outstanding).
 */
export function accRulebookMonth(s0:State,exec:Exec=need(engine)):State{
  let s=clearOperations(s0);
  const run=(uid:string,cmd:Command)=>{s=exec(s,uid,cmd);return s;};
  // ── master data: bank account, payment method for the applied advance, vendors, company, OTA channel ──
  const till=mnyAccounts(s).find(a=>a.kind==='till')!.code;
  run(OWNER,{type:'MST_MASTER_SAVE',master:'bankAccounts',row:{code:ACC_RB.bank.code,name:ACC_RB.bank.name,kind:'bank',bank:'HDFC Bank (fictional)',accountLast4:'4321',ifsc:'DEMO0001234'}});
  const bankMethod=mnyMethods(s).find(r=>r.name==='Bank transfer')!;
  run(OWNER,{type:'MST_MASTER_SAVE',master:'paymentMethods',row:{...bankMethod,settlesTo:ACC_RB.bank.code}});
  // T5a: HCC has no command that applies an advance held since before HCC to a folio, so the fixture uses a payment method whose ledger is 2000
  // (the master's ledger feeds the mapping): Dr 2000 · Cr 1100, as the rulebook's entry. It settles to a placeholder account that never moves money.
  run(OWNER,{type:'MST_MASTER_SAVE',master:'bankAccounts',row:{code:'BA-ADV',name:'Advance held since before HCC (placeholder)',kind:'bank',bank:'None (fictional)'}});
  run(OWNER,{type:'MST_MASTER_SAVE',master:'paymentMethods',row:{code:'ADVANCE',name:ACC_RB.advanceMethod,kind:'wallet',settlesTo:'BA-ADV',needsReference:false,usableAt:['frontdesk'],ledger:'2000'}});
  for(const v of ACC_RB.vendors)run(OWNER,{type:'MST_MASTER_SAVE',master:'expenseVendors',row:{code:v.code,name:v.name,type:v.name.startsWith('Kaveri')?'laundry':'supplier',tds:false}});
  run(OWNER,{type:'GRP_COMPANY_SAVE',name:ACC_RB.company.name,gstin:ACC_RB.company.gstin,contact:'Anita Kulkarni (fictional)',phone:'+91 90000 00044',email:'accounts@deccanagro.example.test',city:'Latur',
    rates:{Classic:200000},creditLimit:P(1000000)});
  const company=s.groupsX!.companies.find(c=>c.name===ACC_RB.company.name)!;
  company.openingOutstanding=P(40000); // the ₹40,000 owed before HCC (rulebook §8.1)
  s.channelsX!.channels.push({id:ACC_RB.channel.id,name:ACC_RB.channel.name,status:'Connected',commissionBps:1500,markupBps:0,mapping:{},stopSell:[],statusNote:'Simulated connection'});
  // ── the opening entry (contract §5): entered by the Manager, approved by the Owner ──
  run(MANAGER,{type:'ACC_OPENING_SAVE',asOf:ACC_RB.asOf,lines:accRulebookOpeningLines(till,company.id),note:'Rulebook §8.1 opening balances, 1 April 2026 (fictional)'});
  run(OWNER,{type:'ACC_OPENING_APPROVE'});
  // A folio restaurant / room-service charge is a 'service' line (kind has no class of its own), so rulebook A3 (restaurant on a folio → 4200)
  // needs the Owner's mapping override; this month has no other service line (no laundry, no banquet).
  run(OWNER,{type:'ACC_MAP_SET',source:'line:service',account:'4200'});
  // ── 1 April: arrivals ──
  const stays:{id:string;room:string;nights:number;company?:boolean}[]=[];
  const book=(room:string,nights:number,guest:string,onCompany:boolean)=>{
    run(MANAGER,{type:'BOOK',guest,room,arrival:s.date,departure:addDays(s.date,nights),people:2,phone:'+91 90000 12345',source:'Direct',...(onCompany?{company:{account:company.id}}:{})});
    const b=s.bookings.at(-1)!;
    run(MANAGER,{type:'REGISTER',id:b.id});run(MANAGER,{type:'CHECKIN',id:b.id});
    stays.push({id:b.id,room,nights,...(onCompany?{company:true}:{})});
  };
  DECCAN.forEach((room,i)=>book(room,5,`Deccan Agro delegate ${i+1} (fictional)`,true));
  GUESTS.forEach((g,i)=>book(g.room,g.nights,`Guest ${String.fromCharCode(65+i)} (fictional)`,false));
  const expectRate:Record<string,number>={Classic:200000,Deluxe:300000,Suite:450000};
  for(const b of s.bookings){const type=s.rooms.find(r=>r.id===b.room)!.type;if(b.rate!==expectRate[type])throw new Error(`ACC-RULEBOOK: ${b.room} rate ${b.rate} is not the ${type} base rate ${expectRate[type]}`);}

  // restaurant checks charged to folios (T2): 20 × ₹5,000 over the stay, spread over four rooms
  const folioRooms=['103','104','106','206'],checks:{day:number;room:string}[]=[];
  for(let i=0;i<20;i++)checks.push({day:1+(i%9),room:folioRooms[i%4]});
  // walk-in outlet checks (T3): 5 by cash, 5 by UPI, ₹6,000 net each
  const outlet:{day:number;method:'Cash'|'UPI'}[]=[];
  for(let i=0;i<10;i++)outlet.push({day:2+i*2,method:i%2?'UPI':'Cash'});
  let checkNo=0;
  const pos=(uid:string,open:Record<string,any>,items:readonly (readonly [string,number])[],settle:Record<string,any>)=>{
    run(uid,{type:'POS_ORDER_OPEN',...open});const o=s.posX!.orders.at(-1)!;
    for(const [item,qty] of items)run(uid,{type:'POS_ITEM_ADD',id:o.id,item,qty});
    run(uid,{type:'POS_KOT_SEND',id:o.id});run(uid,{type:'POS_SETTLE',id:o.id,...settle});
  };

  const bank=(amount:number)=>{
    run(MANAGER,{type:'CASH_BANK',amount,bank:ACC_RB.bank.name,slip:'SLIP-'+s.date.replace(/-/g,''),photo});
    run(OWNER,{type:'CASH_BANK_DECIDE',id:s.cashOutX!.records.at(-1)!.id,confirm:true});
  };
  const expense=(e:Record<string,any>)=>{run(MANAGER,{...e,type:'EXPENSE'});run(OWNER,{type:'EXPENSE_APPROVE',id:s.expenses.at(-1)!.id,approve:true});};

  // guest folios are settled at check-out: advance applied first (T5a), then cash, UPI, card up to the month's totals (T5c)
  const pools:{method:string;left:number}[]=[{method:ACC_RB.advanceMethod,left:P(10000)},{method:'Cash',left:P(152000)},{method:'UPI',left:P(150000)},{method:'Card',left:P(150000)}];
  let payNo=0;
  const settleGuest=(b:Booking)=>{
    let owe=totals(s.bookings.find(x=>x.id===b.id)!).balance;const tenders:{method:string;amount:number;reference:string}[]=[];
    for(const p of pools){if(owe<=0)break;const take=Math.min(owe,p.left);if(take<=0)continue;p.left-=take;owe-=take;
      tenders.push({method:p.method,amount:take,reference:p.method==='Cash'||p.method===ACC_RB.advanceMethod?'':`DEMO-${p.method.toUpperCase()}-${++payNo}`});}
    if(owe!==0)throw new Error('ACC-RULEBOOK: guest tenders do not cover the folio of '+b.id);
    const amount=tenders.reduce((a,t)=>a+t.amount,0);
    if(amount>0)run(MANAGER,{type:'PAYMENT',id:b.id,amount,kind:'payment',...(tenders.length>1?{tenders}:{method:tenders[0].method,reference:tenders[0].reference})});
    run(MANAGER,{type:'CHECKOUT',id:b.id});
  };

  for(let day=1;day<=30;day++){
    const date=s.date;
    if(date!==addDays(ACC_RB.asOf,day-1))throw new Error('ACC-RULEBOOK: business date drifted to '+date);
    // restaurant checks on folios and walk-in checks of the day
    for(const c of checks.filter(c=>c.day===day))pos(MANAGER,{outlet:'room-service',room:c.room,covers:2},FOLIO_CHECK,{mode:'folio'});
    for(const o of outlet.filter(o=>o.day===day))pos(MANAGER,{outlet:'restaurant',table:'T'+(1+(checkNo++%8)),covers:2},OUTLET_CHECK,
      {mode:'outlet',method:o.method,reference:o.method==='Cash'?'':'DEMO-UPI-OUTLET-'+checkNo});
    if(day===5)expense({vendor:'Morning Supplies',category:'Supplies',amount:P(59000),method:'Vendor credit',description:'Consumables, invoice MS/26-27/118 (fictional)',gstRateBps:1800,gstPaise:P(9000),itc:'disallowed'});
    if(day===6){
      for(const st of stays.filter(x=>x.company)){
        run(MANAGER,{type:'COMPANY_CREDIT_REQUEST',id:st.id,reason:'Deccan Agro account'});
        run(OWNER,{type:'APPROVAL',id:s.approvals.at(-1)!.id,approve:true});
        run(MANAGER,{type:'CHECKOUT',id:st.id});
      }
      const b308=stays.find(x=>x.room==='308')!;settleGuest(s.bookings.find(b=>b.id===b308.id)!);
    }
    if(day===8)run(OWNER,{type:'HR_ADVANCE_ADD',staffId:ACC_RB.staff,amount:P(5000),installment:P(5000),reason:'Salary advance (fictional)'});
    if(day===10)run(OWNER,{type:'GRP_COMPANY_PAYMENT',company:company.id,amount:P(40000),method:'Bank transfer',reference:'NEFT-DEMO-0410'});
    if(day===11){
      const disc=s.bookings.find(b=>b.room==='206')!;
      run(OWNER,{type:'DISCOUNT',id:disc.id,amount:P(10000),reason:'Service recovery (fictional)'});
      for(const g of GUESTS.filter(g=>g.nights===10))settleGuest(s.bookings.find(b=>b.room===g.room)!);
    }
    if(day===12)expense({vendor:'Kaveri Textiles',category:'Laundry',amount:P(42000),method:'Vendor credit',description:'Linen, invoice KT/118 (fictional)',gstRateBps:500,gstPaise:P(2000),itc:'disallowed'});
    if(day===14)expense({vendor:'',category:'Petty cash',amount:P(4000),method:'Cash',description:'Sundry cash expenses (1 of 2, fictional)'});
    if(day===15)expense({vendor:'State Electricity Board (fictional)',category:'Utilities',amount:P(45000),method:'Bank transfer',description:'Electricity, March bill (exempt supply)'});
    if(day===18)expense({vendor:'',category:'Petty cash',amount:P(4000),method:'Cash',description:'Sundry cash expenses (2 of 2, fictional)'});
    if(day===25)bank(P(150000));
    // day end: night audit, exact cash count (a ₹200 shortage on 26 April, accepted by the Owner), day close, next day
    run(MANAGER,{type:'NIGHT_POST'});
    if(day===26){
      run(MANAGER,{type:'SHIFT_SUBMIT',counted:counted(s)-P(200),reason:'Short by ₹200 (fictional)'});
      run(OWNER,{type:'SHIFT_DIFFERENCE_DECIDE',id:s.approvals.at(-1)!.id,approve:true});
    }else run(MANAGER,{type:'SHIFT_CLOSE',counted:counted(s)});
    if((s.posX?.orders??[]).some(o=>o.status==='Open'))throw new Error('ACC-RULEBOOK: an outlet check is still open at day end');
    run(OWNER,{type:'DAY_CLOSE'});
    run(OWNER,{type:'NEXT_DAY'});
    run(MANAGER,{type:'SHIFT_OPEN',handover:s.openingCash});
  }
  return s;
}

/** The cash the cashier counts at close (the expected drawer). */
const counted=(s:State)=>drawer(s);

/** The base fixture cleared of operations (as scenario S1), a 20,000 float in the drawer, dated 1 April 2026. */
function clearOperations(s0:State):State{
  const s=JSON.parse(JSON.stringify(s0)) as State;
  s.bookings=[];s.tasks=[];s.expenses=[];s.approvals=[];s.closedDates=[];s.closings={};s.processed=[];
  s.rooms.forEach(r=>{r.hk='Ready';r.blocked=false;delete r.blockFrom;delete r.blockTo;});
  if(s.posX){s.posX.orders=[];s.posX.receipts=[];}
  s.date=ACC_RB.asOf;s.clock=ACC_RB.asOf+'T08:00';s.openingCash=P(20000);s.shiftOpen=true;s.shiftClose=undefined;s.shift=undefined;s.closeProgress=undefined;
  return s;
}

export function extendBase(_s:State):void{}
export const scenarioPatches:Record<string,(s:State)=>void>={
  [ACC_RULEBOOK]:s=>{Object.assign(s,accRulebookMonth(s));},
};
export const scenarioInfo:ScenarioInfo[]=[
  {id:ACC_RULEBOOK,name:'Accounting rulebook month (April 2026)',purpose:'The rulebook golden month, ACC-1 subset: hand-entered opening balances (₹77,40,000), 19 stays, restaurant and walk-in bills, company credit and payment, vendor bills on credit, electricity by bank, cash banked and a ₹200 shortage — April 2026 closed day by day, opened on 1 May. Fictional.',available:true},
];
