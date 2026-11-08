// F8 SF-UI · pure helpers behind the split-folio screens (SF-CONTRACT §2.1, §4, §5). UI only: nothing here computes money of its own —
// every figure is totals(sfView(b,folio)) and every action is a command sf-domain defines (SF_FOLIO_OPEN / SF_ROUTE_SET / SF_FOLIO_REMOVE and the
// folio-aware PAYMENT / CHARGE / DISCOUNT / COMPANY_CREDIT_REQUEST / GUEST_CREDIT_REQUEST / CREDIT_NOTE / CHECKOUT). A single-folio stay returns
// exactly today's answers (no switcher, no `folio` on a command, the checkout sheet is not used).
import type {Booking,Command,State} from '../../domain/types';
import {totals} from '../../domain/selectors';
import {money} from '../../domain/money';
import {sfIsSplit,sfFolios,sfFolio,sfView,sfInvoiceDocs,SF_PRIMARY,SF_ROUTE_CATEGORIES,type SFFolio,type SFPayerKind,type SFRouteCategory} from '../../domain/streams/folio-split';
import {folioApprovalExpired,folioCheckoutBlockers,folioKind} from '../../domain/streams/folio';

export const SF_ROUTE_LABEL:Record<SFRouteCategory,string>={room:'Room nights, meal plans and discounts',extras:'Extras (laundry, food, services)'};
export const SF_PAYER_LABEL:Record<SFPayerKind,string>={guest:'Another guest in the party',company:'Company account',udhaar:'Udhaar (credit guest)',group:'Group'};

/** Short label of a folio: "A · Guest", "B · Deccan Agro Ltd (company)". */
export function sfFolioLabel(f:SFFolio,guest:string):string{
  const who=f.id===SF_PRIMARY?guest:f.payer.name;
  return `${f.id} · ${who}${f.payer.kind==='company'?' (company)':f.payer.kind==='udhaar'?' (Udhaar)':''}`;
}
/** The switcher strip: empty for a single-folio stay (the stay looks exactly as today), else one tab per live folio with its balance. */
export function sfSwitcher(b:Booking):{id:string;label:string;balance:number;text:string}[]{
  if(!sfIsSplit(b))return [];
  return sfFolios(b).map(f=>{const balance=totals(sfView(b,f.id)).balance;return {id:f.id,label:sfFolioLabel(f,b.guest),balance,text:`${sfFolioLabel(f,b.guest)} · ${money(balance)}`};});
}
/** The folio a screen should show: the chosen letter when it is a live folio of a split stay, else A. */
export const sfCurrent=(b:Booking,chosen:string|undefined):string=>sfIsSplit(b)&&chosen&&sfFolio(b,chosen)?chosen:SF_PRIMARY;
/** Folio letter carried by a dialog opened for a folio (`open({type,id,room:<letter>})`): only a live folio letter of a split stay counts. */
export const sfFolioOfModal=(b:Booking|undefined,room:string|undefined):string|undefined=>b&&sfIsSplit(b)&&room&&/^[A-D]$/.test(room)&&sfFolio(b,room)?room:undefined;
/** `open()` argument for a folio action: on a split stay the folio letter rides in `room`; otherwise exactly today's. */
export const sfOpen=(b:Booking,type:string,folio:string):{type:string;id:string;room?:string}=>sfIsSplit(b)?{type,id:b.id,room:folio}:{type,id:b.id};
/** Adds `folio` to a folio command, only on a split stay (every payment, discount, charge, company credit and credit note passes its folio). */
export function sfWithFolio<C extends Command>(b:Booking,folio:string|undefined,cmd:C):C{
  return sfIsSplit(b)&&folio?{...cmd,folio}:cmd;
}

export type SfSettleRow={id:string;label:string;kind:SFPayerKind;total:number;paid:number;balance:number;status:'settled'|'covered'|'company-credit'|'guest-credit'|'pay'|'refund';note:string};
const metaFolio=(a:{meta?:Record<string,unknown>}):string=>String(a.meta?.folio??SF_PRIMARY);
/** The approved company-credit / guest-credit request that exactly covers folio `id`'s balance (what CHECKOUT applies), if any. */
function coveringApproval(s:State,b:Booking,f:SFFolio,balance:number){
  const kind=f.payer.kind==='company'?'company-credit':f.payer.kind==='udhaar'?'guest-credit':'';
  if(!kind)return undefined;
  return s.approvals.find(a=>a.booking===b.id&&folioKind(a)===kind&&a.status==='Approved'&&!folioApprovalExpired(s,a)&&a.amount===balance&&a.version===b.version&&metaFolio(a as {meta?:Record<string,unknown>})===f.id);
}
/** One row per folio for the single check-out sheet. `sum` of balances = the figure the sheet shows as the stay's outstanding. */
export function sfSettleSheet(s:State,b:Booking):{rows:SfSettleRow[];balance:number;total:number;paid:number;deposit:number;blockers:string[];clear:boolean;invoices:string[]}{
  const rows:SfSettleRow[]=sfFolios(b).map(f=>{
    const t=totals(sfView(b,f.id)),cover=t.balance>0?coveringApproval(s,b,f,t.balance):undefined;
    const status:SfSettleRow['status']=t.balance===0?'settled':t.balance<0?'refund':cover?(f.payer.kind==='company'?'company-credit':'guest-credit'):'pay';
    const note=status==='settled'?'Settled':status==='refund'?'Credit balance — refund the excess receipt first'
      :status==='company-credit'?`Approved company credit (${cover!.id}) covers ${money(t.balance)} — applied when you confirm`
      :status==='guest-credit'?`Approved guest credit (${cover!.id}) covers ${money(t.balance)} — applied when you confirm`
      :f.payer.kind==='company'?'Balance due — request company credit for exactly this balance, or take a bank / UPI / card payment'
      :f.payer.kind==='udhaar'?'Balance due — place it on the credit guest’s account, or take a payment':'Balance due — record a payment';
    return {id:f.id,label:sfFolioLabel(f,b.guest),kind:f.payer.kind,total:t.total,paid:t.paid,balance:t.balance,status,note};
  });
  const sum=(k:'total'|'paid'|'balance')=>rows.reduce((a,r)=>a+r[k],0);
  const deposit=totals(b).deposit;
  // Nights, deposit and pending approvals come from today's blocker list (given zero balance); each folio's balance is judged on its own.
  const base=folioCheckoutBlockers(s,b,{balance:0,deposit});
  const blockers=[...base,...rows.filter(r=>r.status==='pay').map(r=>`Folio ${r.id}: ${money(r.balance)} is due — ${r.kind==='company'?'request company credit or take a payment':'settle it'}.`),
    ...rows.filter(r=>r.status==='refund').map(r=>`Folio ${r.id}: credit balance ${money(-r.balance)} — refund the excess receipt first.`)];
  return {rows,balance:sum('balance'),total:sum('total'),paid:sum('paid'),deposit,blockers,clear:b.status==='In house'&&!blockers.length,invoices:sfInvoiceDocs(b).map(d=>d.no)};
}
/** Invoice numbers per folio of a departed (or part-invoiced) stay. A folio with no number and a zero total shows "no invoice (₹0 folio)" (SFQ-8). */
export function sfInvoiceRows(b:Booking):{folio:string;label:string;no:string;total:number}[]{
  const split=sfIsSplit(b);
  return sfFolios(b).map(f=>{const v=split?sfView(b,f.id):b,t=totals(v);
    return {folio:f.id,label:sfFolioLabel(f,b.guest),no:f.invoiceNo??(!split?b.invoiceNo:undefined)??(b.status==='Departed'&&t.total===0?'No invoice (₹0 folio)':'Not yet issued'),total:t.total};});
}

/** Routes still free for a new folio (categories no other live folio holds, folio A not counted: A takes what is left). */
export function sfFreeRoutes(b:Booking):SFRouteCategory[]{
  const held=new Set(sfFolios(b).filter(f=>f.id!==SF_PRIMARY).flatMap(f=>f.routes));
  return SF_ROUTE_CATEGORIES.filter(c=>!held.has(c));
}
/** SF_FOLIO_OPEN from the Split bill form. The command validates; this only shapes the payload. */
export function sfOpenCommand(b:Booking,f:{kind:SFPayerKind;ref?:string;name?:string;gstin?:string;routes:SFRouteCategory[];reason?:string},key:string):Command{
  const payer=f.kind==='guest'?{kind:'guest' as const,name:f.name??'',...(f.gstin?{gstin:f.gstin}:{})}:{kind:f.kind,ref:f.ref??''};
  return {type:'SF_FOLIO_OPEN',id:b.id,payer,routes:f.routes,...(f.reason?{reason:f.reason}:{}),key};
}
export const sfRouteCommand=(b:Booking,folio:string,routes:SFRouteCategory[],reason:string,key:string):Command=>({type:'SF_ROUTE_SET',id:b.id,folio,routes,reason,key});
export const sfRemoveCommand=(b:Booking,folio:string,key:string):Command=>({type:'SF_FOLIO_REMOVE',id:b.id,folio,key});

/** Who the shown folio's document is addressed to (folio A = the guest). */
export const sfFolioPayer=(b:Booking,folio:string):string=>sfFolio(b,folio)?.payer.name&&folio!==SF_PRIMARY?sfFolio(b,folio)!.payer.name:b.guest;
/** `booking` parameter of doc.folio / doc.invoice for one folio: "<id>#<letter>" on a split stay (folioDocument reads the suffix), else the id. */
export const sfDocBooking=(b:Booking,folio:string):string=>sfIsSplit(b)?`${b.id}#${folio}`:b.id;
