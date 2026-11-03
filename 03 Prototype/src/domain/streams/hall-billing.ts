// Module 27 · Hall booking money (F6 H1, contract §5; spec §6.4, §8, §9, §13–§15). Pure: integer paise, GST from the dated master, CGST + SGST
// per line (half away from zero, src/domain/gst.ts). ONE truth table with the server: 08 Production/src/Hcc.Domain/Hall/HallBilling.cs.
//   line taxable = qty × rate − discount; discount % = Σ(list − charged) / Σ list (pre-GST)
//   advance plan = % rows of the grand total (half away from zero), the last row = balance (takes the rounding)
//   event bill   = MENU lines at max(guaranteed, actual) plates + extras; HPE invoice; advances applied; balance by tender / company / Udhaar / due
//   cancellation = slab by days before the event → kept = min(advances paid, total × slab %); kept is GST-inclusive at the hall-rent rate
//                  (Rulebook §4.9) → 4300 + CGST + SGST; the rest is refunded (Manager ≤ ₹50,000, Owner above; no self-approval)
// Accounting (accounting.ts F6-H1 block reads hallAccSources): hall:advance Dr tender Cr 2000 (no GST, DEC-142) · hall:invoice Dr 1100 Cr 4400 /
// 4200 (MENU) / 4000 (ROOM) Cr 2110 Cr 2120 · hall:advance-apply Dr 2000 Cr 1100 · hall:balance Dr tender | 1110/<company> | 1120 Cr 1100 ·
// hall:cancel Dr 2000 Cr 4300 Cr 2110 Cr 2120 · hall:refund Dr 2000 Cr tender.
import type {State,Role} from '../types';
import {gstLineTax,gstSplit,gstForService,gstForRoomNight} from '../gst';
import {addDays} from '../money';
import {hallSlice,hallDaysBefore,type QuoteLine,type AdvanceRow,type HallSettings,type HallEvent,type HallState,type HallCancelSlab} from './hall';

/** a × bps / 10000 rounded half away from zero (the GST rounding, reused for percentages). */
export const hallPct=(amount:number,bps:number)=>gstLineTax(amount,bps);
export const hallLineTaxable=(l:Pick<QuoteLine,'qty'|'ratePaise'|'discountPaise'>)=>l.qty*l.ratePaise-(l.discountPaise||0);
export const hallLineList=(l:Pick<QuoteLine,'qty'|'listRatePaise'>)=>l.qty*l.listRatePaise;

/** GST rate (bps) of a hall line class on a date from the dated GST master: hall rent and banquet food → banquet rate; ROOM → room-night rule
 *  at the line rate (DEC-198); everything else → other services. Illustrative — CA to confirm. */
export function hallTaxBps(s:State,gstClass:string,date:string,ratePaise=0):number{
  if(gstClass==='ROOM')return gstForRoomNight(s,date,ratePaise).bps;
  if(gstClass==='HALL_RENT'||gstClass==='BANQUET_FOOD')return gstForService(s,date,'banquet').bps;
  return gstForService(s,date,'other').bps;
}
export type HallPricedLine=QuoteLine&{taxBps:number;taxablePaise:number;cgstPaise:number;sgstPaise:number};
export type HallTotals={taxablePaise:number;cgstPaise:number;sgstPaise:number;taxPaise:number;totalPaise:number;listPaise:number;discountPaise:number};
/** Lines priced on a date (a line that already carries taxBps keeps it — posted lines keep their snapshot). */
export function hallPrice(s:State,lines:readonly QuoteLine[],date:string):{lines:HallPricedLine[];totals:HallTotals}{
  const out=lines.map(l=>{const taxBps=l.taxBps??hallTaxBps(s,l.gstClass,date,l.ratePaise),taxable=hallLineTaxable(l),g=gstSplit(gstLineTax(taxable,taxBps));
    return {...l,taxBps,taxablePaise:taxable,cgstPaise:g.cgst,sgstPaise:g.sgst};});
  const t=out.reduce((a,l)=>({taxablePaise:a.taxablePaise+l.taxablePaise,cgstPaise:a.cgstPaise+l.cgstPaise,sgstPaise:a.sgstPaise+l.sgstPaise,listPaise:a.listPaise+hallLineList(l)}),
    {taxablePaise:0,cgstPaise:0,sgstPaise:0,listPaise:0});
  return {lines:out,totals:{...t,taxPaise:t.cgstPaise+t.sgstPaise,totalPaise:t.taxablePaise+t.cgstPaise+t.sgstPaise,discountPaise:t.listPaise-t.taxablePaise}};
}

// ── Discounts (spec §8) ──
/** Discount in bps of the list value (pre-GST), floored for display; the limit test itself is exact (hallDiscountWithin). */
export function hallDiscountBps(lines:readonly QuoteLine[]):number{
  const list=lines.reduce((a,l)=>a+hallLineList(l),0),charged=lines.reduce((a,l)=>a+hallLineTaxable(l),0);
  return list>0?Math.floor((list-charged)*10000/list):0;
}
/** The role's limit in bps (null = any). Roles without a quoting limit (Cashier, Accountant …) get 0. */
export function hallDiscountLimit(settings:HallSettings,role:Role):number|null{
  return role==='Owner'?settings.discountLimitBps.OWNER:role==='Manager'?settings.discountLimitBps.MANAGER:settings.discountLimitBps.RECEPTION;
}
/** Exact: (Σ list − Σ charged) × 10000 ≤ limit × Σ list. */
export function hallDiscountWithin(lines:readonly QuoteLine[],limitBps:number|null):boolean{
  if(limitBps===null)return true;
  const list=lines.reduce((a,l)=>a+hallLineList(l),0),charged=lines.reduce((a,l)=>a+hallLineTaxable(l),0);
  return (list-charged)*10000<=limitBps*list;
}

// ── Advance plan (spec §6.4) ──
/** Default plan for a grand total: 25 % on acceptance, 50 % 30 days before the event (at once when nearer), balance on the event day. */
export function hallAdvancePlan(totalPaise:number,eventDate:string,acceptDate:string,settings:HallSettings):AdvanceRow[]{
  const [p1,p2]=settings.advanceDefault,second=addDays(eventDate,-settings.secondDueDays),due2=second<acceptDate?acceptDate:second;
  return hallPlanAmounts(totalPaise,[{id:'ADV-1',label:'Advance on booking',pctBps:p1,due:'ON_ACCEPT',paidPaise:0,receiptIds:[]},
    {id:'ADV-2',label:'Second advance',pctBps:p2,due:due2,paidPaise:0,receiptIds:[]},{id:'ADV-3',label:'Balance',due:'EVENT_DAY',paidPaise:0,receiptIds:[]}]);
}
/** Amounts of a plan: % rows half away from zero, fixed-amount rows as given, the LAST row = total − the others (rounding difference). */
export function hallPlanAmounts(totalPaise:number,rows:readonly AdvanceRow[]):AdvanceRow[]{
  let used=0;return rows.map((r,i)=>{if(i===rows.length-1)return {...r,amountPaise:totalPaise-used};
    const a=r.pctBps!==undefined?hallPct(totalPaise,r.pctBps):(r.amountPaise??0);used+=a;return {...r,amountPaise:a};});
}
/** A receipt filled into the plan in row order (an overpayment stays on the last row). */
export function hallAllocate(rows:readonly AdvanceRow[],amountPaise:number,receiptId:string):AdvanceRow[]{
  let left=amountPaise;return rows.map((r,i)=>{if(left<=0)return r;const room=i===rows.length-1?left:Math.max(0,(r.amountPaise??0)-r.paidPaise);const take=Math.min(room,left);
    if(!take)return r;left-=take;return {...r,paidPaise:r.paidPaise+take,receiptIds:[...r.receiptIds,receiptId]};});
}
/** Rows due (not fully paid) on or before `date + days` (ON_ACCEPT counts as due; EVENT_DAY on the event date). */
export function hallAdvancesDue(rows:readonly AdvanceRow[],eventDate:string,date:string,days=7){
  const dueOf=(r:AdvanceRow)=>r.due==='ON_ACCEPT'?'0000-00-00':r.due==='EVENT_DAY'?eventDate:r.due;
  return rows.filter(r=>(r.amountPaise??0)>r.paidPaise&&dueOf(r)<=addDays(date,days)).map(r=>({...r,overdue:dueOf(r)<date}));
}

// ── Event money ──
export const hallPaid=(e:HallEvent)=>(e.receipts??[]).reduce((a,r)=>a+(r.kind==='ADVANCE'?r.amountPaise:r.kind==='REFUND'?-r.amountPaise:0),0);
export const hallAdvancesPaid=(e:HallEvent)=>(e.receipts??[]).filter(r=>r.kind==='ADVANCE').reduce((a,r)=>a+r.amountPaise,0);
/** Billed plates: the higher of guaranteed and actual (spec §13, Q11). */
export const hallBilledPax=(e:Pick<HallEvent,'guaranteedPax'|'actualPax'>)=>Math.max(e.guaranteedPax??0,e.actualPax??0);
/** The event-day lines: the accepted lines with MENU (per plate) quantity = billed plates, then the extras. */
export function hallBillLines(e:HallEvent,actualPax?:number):QuoteLine[]{
  const pax=hallBilledPax({guaranteedPax:e.guaranteedPax,actualPax:actualPax??e.actualPax});
  return [...e.lines.map(l=>l.kind==='MENU'&&l.unit==='plate'&&pax>0?{...l,qty:pax,discountPaise:l.discountPaise&&l.qty?Math.round(l.discountPaise*pax/l.qty):l.discountPaise}:l),...e.extras];
}
/** Grand total of the event's accepted lines priced on a date (the cancellation basis = the accepted quotation total). */
export const hallEventQuoteTotal=(s:State,e:HallEvent,date:string)=>hallPrice(s,e.lines,date).totals.totalPaise;

// ── Cancellation (spec §15) ──
export function hallCancelSlab(slabs:readonly HallCancelSlab[],daysBefore:number):HallCancelSlab{
  const sorted=[...slabs].sort((a,b)=>b.minDays-a.minDays);return sorted.find(x=>daysBefore>=x.minDays)??sorted[sorted.length-1];
}
/** Kept / refund for a cancellation on `date`. kept = min(advances paid, quotation total × slab %); the rest is refunded. */
export function hallCancelQuote(s:State,e:HallEvent,date:string){
  const x=hallSlice(s),daysBefore=hallDaysBefore(date,e.date),slab=hallCancelSlab(x.settings.cancelSlabs,daysBefore);
  const total=hallEventQuoteTotal(s,e,date),paid=hallPaid(e),kept=Math.min(paid,hallPct(total,slab.keptBps));
  return {daysBefore,keptBps:slab.keptBps,totalPaise:total,paidPaise:paid,keptPaise:kept,refundPaise:paid-kept,taxBps:hallTaxBps(s,'HALL_RENT',date)};
}
/** A GST-inclusive kept amount split into taxable + CGST + SGST (taxable half away from zero; the tax takes the rest, split per gstSplit). */
export function hallInclusiveSplit(grossPaise:number,bps:number){
  const p=grossPaise*10000,d=10000+bps,q=Math.floor(p/d),r=p-q*d,taxable=q+(r*2>=d?1:0),tax=grossPaise-taxable,g=gstSplit(tax);
  return {taxablePaise:taxable,taxPaise:tax,cgstPaise:g.cgst,sgstPaise:g.sgst};
}
/** Refund approval: Manager up to the limit, Owner any; never the person who cancelled (no self-approval, checked by the handler). */
export function hallRefundApprover(settings:HallSettings,role:Role,refundPaise:number):boolean{
  return role==='Owner'||(role==='Manager'&&refundPaise<=settings.refundManagerLimitPaise);
}

// ── Accounting sources (accounting.ts F6-H1 block) ──
export type HallAccLine={key:string;party?:{method?:string;company?:string};debit:number;credit:number};
export type HallAccSource={type:'Sales'|'Receipt'|'Payment'|'Journal';narration:string;source:{kind:'hall';id:string;sub:string};lines:HallAccLine[]};
/** Source keys: advances → receipt:deposit (2000 Advance deposits), cancellation charge → line:attrition (4300), food → line:food (4200, or 4400
 *  when the hall setting foodLedger = '4400', spec Q6), hall / decor / services → ledger 4400 Banquet & events revenue (no mapping key). */
export const HALL_KEYS={advance:'receipt:deposit',revenue:'ledger:4400',food:'line:food',cancel:'line:attrition'} as const;
const D=(key:string,a:number,party?:HallAccLine['party']):HallAccLine=>({key,debit:a,credit:0,...(party?{party}:{})});
const C=(key:string,a:number,party?:HallAccLine['party']):HallAccLine=>({key,debit:0,credit:a,...(party?{party}:{})});
const lineKey=(l:QuoteLine,food:string)=>l.kind==='MENU'?food:l.kind==='ROOM'?'line:room':HALL_KEYS.revenue;
/** Every hall accounting source dated `date` (pure). */
export function hallAccSources(s:State,date:string,x:HallState|undefined=s.hallX):HallAccSource[]{
  const out:HallAccSource[]=[];if(!x)return out;const food=x.settings.foodLedger==='4400'?HALL_KEYS.revenue:HALL_KEYS.food;
  for(const e of x.events){const who=e.customer?.name??e.id;
    for(const r of e.receipts??[]){if(r.date!==date||!r.amountPaise)continue;
      if(r.kind==='ADVANCE')out.push({type:'Receipt',narration:`Hall advance · ${who} · ${r.method}${r.reference?' · '+r.reference:''} · event ${e.id}`,source:{kind:'hall',id:`hall:advance:${r.id}`,sub:'hall-advance'},
        lines:[D('method:'+r.method,r.amountPaise,{method:r.method}),C(HALL_KEYS.advance,r.amountPaise)]});
      else if(r.kind==='REFUND')out.push({type:'Payment',narration:`Hall refund · ${who} · ${r.method} · event ${e.id}`,source:{kind:'hall',id:`hall:refund:${r.id}`,sub:'hall-refund'},
        lines:[D(HALL_KEYS.advance,r.amountPaise),C('method:'+r.method,r.amountPaise,{method:r.method})]});
      else if(r.kind==='BALANCE'){
        const dr=r.company?D('receipt:company-credit',r.amountPaise,{company:r.company}):r.udhaar?D('receipt:udhaar',r.amountPaise):D('method:'+r.method,r.amountPaise,{method:r.method});
        out.push({type:r.company||r.udhaar?'Journal':'Receipt',narration:`Hall balance · ${who} · ${r.company?'company credit '+r.company:r.udhaar?'Udhaar':r.method} · invoice ${e.invoice?.no??''}`,
          source:{kind:'hall',id:`hall:balance:${r.id}`,sub:'hall-balance'},lines:[dr,C('folio:guest',r.amountPaise)]});}
    }
    const inv=e.invoice;
    if(inv&&inv.date===date){
      const rev=new Map<string,number>();for(const l of inv.lines)rev.set(lineKey(l,food),(rev.get(lineKey(l,food))??0)+l.taxablePaise);
      out.push({type:'Sales',narration:`Hall invoice ${inv.no} · ${who} · ${inv.billedPax} plates · event ${e.id}`,source:{kind:'hall',id:`hall:invoice:${e.id}`,sub:'hall-invoice'},
        lines:[D('folio:guest',inv.totalPaise),...[...rev].map(([k,v])=>C(k,v)),C('folio:cgst',inv.cgstPaise),C('folio:sgst',inv.sgstPaise)]});
      if(inv.advanceAppliedPaise)out.push({type:'Journal',narration:`Hall advance applied · invoice ${inv.no} · ${who}`,source:{kind:'hall',id:`hall:advance-apply:${e.id}`,sub:'hall-advance-apply'},
        lines:[D(HALL_KEYS.advance,inv.advanceAppliedPaise),C('folio:guest',inv.advanceAppliedPaise)]});
    }
    const cn=e.creditNote;
    if(cn&&cn.date===date&&inv){ // F6-H3: credit note = the invoice reversed (Dr revenue + GST, Cr receivable)
      const rev=new Map<string,number>();for(const l of inv.lines)rev.set(lineKey(l,food),(rev.get(lineKey(l,food))??0)+l.taxablePaise);
      out.push({type:'Journal',narration:`Hall credit note ${cn.no} · invoice ${cn.invoiceNo} · ${who} · ${cn.reason}`,source:{kind:'hall',id:`hall:credit-note:${e.id}`,sub:'hall-credit-note'},
        lines:[...[...rev].map(([k,v])=>D(k,v)),D('folio:cgst',cn.cgstPaise),D('folio:sgst',cn.sgstPaise),C('folio:guest',cn.totalPaise)]});}
    const c=e.cancellation;
    if(c&&c.date===date&&c.keptPaise){const g=hallInclusiveSplit(c.keptPaise,c.taxBps);
      out.push({type:'Journal',narration:`Hall cancellation charge · ${who} · ${c.daysBefore} days before · kept ${c.keptBps/100} % · event ${e.id}`,source:{kind:'hall',id:`hall:cancel:${e.id}`,sub:'hall-cancel'},
        lines:[D(HALL_KEYS.advance,c.keptPaise),C(HALL_KEYS.cancel,g.taxablePaise),C('folio:cgst',g.cgstPaise),C('folio:sgst',g.sgstPaise)]});}
  }
  return out;
}
