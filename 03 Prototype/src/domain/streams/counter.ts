// LF lane lf-money-counter · contract §6 (A7 counter bill) and §7 (B2 individual credit guests). Pure helpers: a counter line from the
// catalogue / POS menu / free text, bill totals (GST per line, half away from zero), discount limits (DEC-063), refusals, drawer cash,
// the accounting source {kind:'counter'} and the credit-guest selectors. Export names are prefixed `ctr` / `CTR_` / `gcr`.
import type {Booking,CTRBill,CTRLine,CTRState,GuestCreditAccount,State} from '../types';
import {gstForCharge,gstLineTax,gstSplit,GST_CHARGE_CATEGORIES} from '../gst';
import {money} from '../money';
import {catExtra,catExtraPrice} from './catalogue';
import {posItemPrice,posItemClass,posRateFor} from './pos-master';
import {rbacCanDiscount,rbacDiscountLimit} from './rbac';
import {mnyIsDrawer} from './money-master';
import {GSTR1_SAC} from '../gstr1';
import {expUdOut,expUdDaysSince,type ExpUdReceivable} from './expenses-scc';
import {grpSlice,grpExposure,grpCreditRefusal} from './groups';
import type {Guest} from './guests';
import {customerKeyForCredit} from './customers';

export type {CTRBill,CTRLine,CTRState,GuestCreditAccount};
export const ctrState=(s:State):CTRState=>s.counterX??{bills:[]};
export const CTR_QTY_MAX=99;
/** AF2 counter caps (parity with the server's CounterBillRules.MaxUnitPaise / MaxBillPaise, AF-FIX2 F2): a unit price at most ₹1 crore, one bill
 *  (before discount) at most ₹10 crore. Integer paise; 30 lines × 99 × the unit cap is far below Number.MAX_SAFE_INTEGER, and sums are guarded anyway. */
export const CTR_UNIT_MAX_PAISE=1_000_000_000,CTR_BILL_MAX_PAISE=10_000_000_000;
export const CTR_UNIT_CAP_TEXT='The unit price can be at most ₹1,00,00,000.00. Check the amount (it is in paise).';
export const CTR_BILL_CAP_TEXT='One counter bill can be at most ₹10,00,00,000.00. Check the quantities and prices, or split the bill.';
/** Refusal ('' = fine) when the bill before discount is above the cap (or not a safe integer). Same wording as the server's BillRefusal. */
export function ctrBillRefusal(lines:readonly Pick<CTRLine,'qty'|'unit'>[]):string{
  let gross=0;
  for(const l of lines){const a=l.unit*l.qty;if(!Number.isSafeInteger(a))return CTR_BILL_CAP_TEXT;gross+=a;if(!Number.isSafeInteger(gross)||gross>CTR_BILL_MAX_PAISE)return CTR_BILL_CAP_TEXT;}
  return '';
}
export const CTR_SETTLEMENTS=['paid','udhaar','company'] as const;
export type CTRLineInput={extraId?:string;menuItemId?:string;label?:string;qty?:number;unit?:number;gstCategory?:string};

/** A counter line from its source, priced and taxed on `date` (GST master by category; menu items at the outlet's GST profile). */
export function ctrLineFrom(s:State,input:CTRLineInput,date:string,id:string):CTRLine|{error:string}{
  const qty=input.qty===undefined||input.qty===null?1:Number(input.qty);
  if(!Number.isInteger(qty)||qty<1||qty>CTR_QTY_MAX)return {error:`Quantity: a whole number from 1 to ${CTR_QTY_MAX}.`};
  const given=input.unit===undefined||input.unit===null||(input.unit as unknown)===''?undefined:Number(input.unit);
  if(given!==undefined&&(!Number.isInteger(given)||given<=0))return {error:'Enter the unit price in whole paise (more than 0).'};
  if(given!==undefined&&given>CTR_UNIT_MAX_PAISE)return {error:CTR_UNIT_CAP_TEXT};
  const cat=(c:unknown)=>GST_CHARGE_CATEGORIES.some(x=>x.id===c)?c as 'food'|'laundry'|'banquet'|'other':undefined;
  if(input.extraId){
    const x=catExtra(s,String(input.extraId));if(!x)return {error:'Choose an item from the catalogue.'};
    if(!x.active)return {error:`${x.name} is retired — choose another item.`};
    const unit=given??catExtraPrice(x,date);if(unit===undefined)return {error:`${x.name} has no price on ${date} — enter the unit price.`};
    if(unit>CTR_UNIT_MAX_PAISE)return {error:CTR_UNIT_CAP_TEXT};
    const c=cat(input.gstCategory)??x.gst??'other';
    const label=String(input.label??'').trim()||x.name;
    return {id,label,extraId:x.id,qty,unit,amount:unit*qty,gstCategory:c,taxBps:gstForCharge(s,date,c).bps,sac:GSTR1_SAC[c]!};
  }
  if(input.menuItemId){
    const m=(s.posX?.menu??[]).find(m=>m.id===input.menuItemId);if(!m)return {error:'Choose an item from the menu.'};
    if(m.active===false)return {error:`${m.name} is retired — choose another item.`};
    const unit=given??posItemPrice(s,m,date);if(unit===undefined)return {error:`${m.name} has no price on ${date}.`};
    if(unit>CTR_UNIT_MAX_PAISE)return {error:CTR_UNIT_CAP_TEXT};
    const bps=m.outlets[0]?posRateFor(s,m.outlets[0],posItemClass(m),date).bps:gstForCharge(s,date,'food').bps;
    return {id,label:String(input.label??'').trim()||m.name,menuItemId:m.id,qty,unit,amount:unit*qty,gstCategory:'food',taxBps:bps,sac:GSTR1_SAC.food!};
  }
  const label=String(input.label??'').trim();if(!label)return {error:'Describe the item.'};
  if(label.length>120)return {error:'Keep the description to 120 characters.'};
  if(given===undefined)return {error:`Enter the unit price of ${label}.`};
  const c=cat(input.gstCategory);if(!c)return {error:'Choose a GST category: food, laundry, banquet or other.'};
  return {id,label,qty,unit:given,amount:given*qty,gstCategory:c,taxBps:gstForCharge(s,date,c).bps,sac:GSTR1_SAC[c]!};
}
/** The line whose rate a discount takes (the largest line: the folio-adjustment rule of the dominant supply). */
export const ctrDominant=(lines:readonly CTRLine[])=>lines.reduce<CTRLine|undefined>((a,l)=>!a||l.amount>a.amount?l:a,undefined);
/** Totals: GST per line (half away from zero), CGST + SGST per line; a discount is a negative line at the dominant line's rate. */
export function ctrTotals(lines:readonly CTRLine[],discount=0){
  const d=ctrDominant(lines),all=[...lines.map(l=>({amount:l.amount,taxBps:l.taxBps})),...(discount&&d?[{amount:-discount,taxBps:d.taxBps}]:[])];
  let tax=0,cgst=0,sgst=0;for(const l of all){const t=gstLineTax(l.amount,l.taxBps),x=gstSplit(t);tax+=t;cgst+=x.cgst;sgst+=x.sgst;}
  const net=all.reduce((a,l)=>a+l.amount,0);return {net,tax,cgst,sgst,total:net+tax};
}
/** DEC-063 role limits for a counter-bill discount (no approval queue: above the limit = refused). '' when allowed. */
export function ctrDiscountRefusal(s:State,uid:string,amount:number):string{
  if(!amount)return '';
  if(!rbacCanDiscount(s,uid))return 'A discount on a counter bill is given by Reception, a Manager or the Owner — not at the cash counter (DEC-063).';
  const lim=rbacDiscountLimit(s,uid);
  if(lim!==undefined&&amount>lim)return `Your discount limit is ${money(lim)}. A counter bill has no approval queue: ask a Manager or the Owner to issue it.`;
  return '';
}
/** Revenue key of a counter line class: food → line:food (4200), a room-type line → line:room (4000), else line:service (4100). */
export const ctrRevenueKey=(l:Pick<CTRLine,'gstCategory'>)=>l.gstCategory==='food'?'line:food':l.gstCategory==='room'?'line:room':'line:service';
/** Udhaar kind of a counter bill by its dominant line class. */
export const ctrUdKind=(b:Pick<CTRBill,'lines'>)=>ctrDominant(b.lines)?.gstCategory==='food'?'restaurant' as const:'other' as const;
/** Decision date of a credited bill (the void approval's decision), else undefined. */
export const ctrCreditDate=(s:State,b:CTRBill)=>b.status==='Credited'?s.approvals.find(a=>a.id===b.voidApprovalId)?.decidedAt?.slice(0,10)??b.date:undefined;
/** Drawer cash of counter bills on a date: paid in drawer cash (+), credited (refunded) in drawer cash on its credit date (−). */
export function ctrDrawerCash(s:State,date:string):number{
  let n=0;for(const b of ctrState(s).bills){if(b.settlement.mode!=='paid'||!mnyIsDrawer(s,b.settlement.method??''))continue;
    if(b.date===date)n+=b.total;if(ctrCreditDate(s,b)===date)n-=b.total;}
  return n;
}
/** Company receivable still open from counter bills on company credit (not credited). */
export const ctrCompanyOpen=(s:State,companyId:string)=>ctrState(s).bills.filter(b=>b.settlement.mode==='company'&&b.settlement.companyId===companyId&&b.status==='Issued').reduce((a,b)=>a+b.total,0);
/** Company-credit refusal for a counter bill of `total` (DEC-076 rule, exposure + counter bills already on credit). */
export function ctrCompanyRefusal(s:State,companyId:string|undefined,total:number):string{
  const c=grpSlice(s).companies.find(x=>x.id===companyId);if(!c)return 'Choose the company account for a counter bill on company credit.';
  if(c.active===false)return `${c.name} is inactive.`;
  return grpCreditRefusal(c,grpExposure(s,c)+ctrCompanyOpen(s,c.id),total,'counter bill on company credit')??'';
}

// ── B2 individual credit guests (§7) ──
/** The Udhaar party key of a guest's credit account: the guest's phone (last 10 digits), or the guest id when phone-less (customers.ts rule). */
export const gcrCustomerKey=(g:Pick<Guest,'id'|'phone'>)=>customerKeyForCredit(g);
/** Open Udhaar receivables of a guest (by guestId, or by the account's customer key = the receivable's mobile). */
export function gcrOpenBills(s:State,g:{id:string;phone:string;creditAccount?:GuestCreditAccount}):ExpUdReceivable[]{
  const key=g.creditAccount?.customerKey??gcrCustomerKey(g);
  return (s.expensesX?.udhaar??[]).filter(r=>!r.void&&expUdOut(r)>0&&(r.guestId===g.id||(!!r.mobile&&r.mobile===key)));
}
export const gcrUsed=(s:State,g:{id:string;phone:string;creditAccount?:GuestCreditAccount})=>gcrOpenBills(s,g).reduce((a,r)=>a+expUdOut(r),0);
/** Refusal of putting `amount` on a guest's credit account ('' = allowed); text mirrors grpCreditRefusal. */
export function gcrRefusal(s:State,g:Guest|undefined,amount:number):string{
  if(!g)return 'Link the stay to a guest profile with a credit account first.';
  const a=g.creditAccount;if(!a||!a.active)return `${g.name} has no active credit account. The Owner opens one on the guest profile (Customers › Profile › Credit).`;
  const used=gcrUsed(s,g);
  if(amount>0&&used+amount>a.limit)return `Credit limit exceeded for ${g.name}: limit ${money(a.limit)}${a.limit===0?' (no credit)':''}, used ${money(used)}, remaining ${money(Math.max(0,a.limit-used))}; `
    +`this stay on guest credit needs ${money(amount)}. Only the Owner can raise the limit (Customers › Profile › Credit).`;
  return '';
}
export type GCRAgeBucket='0–30'|'31–60'|'61–90'|'90+';
export const gcrBucket=(days:number):GCRAgeBucket=>days<=30?'0–30':days<=60?'31–60':days<=90?'61–90':'90+';
/** Bill-wise outstanding per credit guest (Cashier › Credit guests): open bills with their booking / counter bill reference and ageing. */
export function udGuestOutstanding(s:State,asOf=s.date){
  return (s.guests??[]).filter(g=>g.creditAccount).map(g=>{
    const bills=gcrOpenBills(s,g).map(r=>{const days=expUdDaysSince(r.date,asOf);
      const bk:Booking|undefined=r.booking?s.bookings.find(b=>b.id===r.booking):undefined;
      const cb=r.counterBill?ctrState(s).bills.find(b=>b.id===r.counterBill):undefined;
      return {id:r.id,date:r.date,open:expUdOut(r),days,bucket:gcrBucket(days),ref:bk?.invoiceNo??cb?.no??r.booking??r.counterBill??r.note};});
    const buckets:Record<GCRAgeBucket,number>={'0–30':0,'31–60':0,'61–90':0,'90+':0};for(const b of bills)buckets[b.bucket]+=b.open;
    return {guestId:g.id,name:g.name,limit:g.creditAccount!.limit,active:g.creditAccount!.active,customerKey:g.creditAccount!.customerKey,
      party:'1120/UD-'+g.creditAccount!.customerKey,open:bills.reduce((a,b)=>a+b.open,0),buckets,bills};
  });
}
