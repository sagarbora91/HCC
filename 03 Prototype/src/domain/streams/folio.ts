// Stream C · Payments & folio (owns PAYMENT, CHARGE, DISCOUNT, APPROVAL, CHECKOUT, REFUND_REQUEST, REFUND_EXECUTE,
// COMPANY_CREDIT_REQUEST, CORRECTION_REQUEST, CREDIT_NOTE) — pure domain helpers and selectors
// (re-exported by services/mock/index.ts for UI and tests). Export names are prefixed `folio`/`FOLIO_` to avoid clashes.
import type {Approval,Booking,Line,Receipt,State} from '../types';
import {addMinutes} from '../money';

/** Payment tenders accepted by PAYMENT (SCR-09). Company credit is applied only via an approved request at checkout. */
/** LEGACY (MA-3): seed of the front-desk payment methods; read mnyNames(s,'frontdesk') (money masters). */
export const FOLIO_TENDERS=['Cash','UPI','Card','Bank transfer'] as const;
/** A Manager may approve company credit up to ₹10,000 per stay; above that an Owner must approve (RBAC §6). */
export const FOLIO_MANAGER_CREDIT_LIMIT=1000000;
/** Approvals expire after 24 simulated hours (RBAC §7). */
export const FOLIO_APPROVAL_TTL_MINUTES=24*60;
/** Approval kinds decided by the APPROVAL command and shown on the folio. `undefined` = discount (M1). */
export const FOLIO_APPROVAL_KINDS=['discount','refund','company-credit','correction','credit-note'];
export const FOLIO_INVOICE_SERIES='DEMO/2026-27/';
export const FOLIO_CREDIT_NOTE_SERIES='DEMO-CN/2026-27/';

export const folioPad=(n:number)=>String(n).padStart(4,'0');
export const folioInvoiceNo=(n:number)=>FOLIO_INVOICE_SERIES+folioPad(n);
export const folioCreditNoteNo=(n:number)=>FOLIO_CREDIT_NOTE_SERIES+folioPad(n);
export const folioExpiresAt=(clock:string)=>addMinutes(clock,FOLIO_APPROVAL_TTL_MINUTES);

/** Approval kind, normalising the M1 discount (no kind). */
export const folioKind=(a:Approval)=>a.kind??'discount';
export const folioKindLabel:Record<string,string>={discount:'Discount','refund':'Refund','company-credit':'Company credit','correction':'Correction','credit-note':'Credit note'};
/** Discounts post at approval; their completed decision never expires. Refund/credit authorizations still expire before execution. */
export function folioApprovalExpired(s:State,a:Approval){return !!a.expiresAt&&s.clock>a.expiresAt&&(a.status==='Pending'||(a.status==='Approved'&&folioKind(a)!=='discount'));}
/** Status as shown to users: a request past its expiry reads "Expired" even before anyone touches it. */
export function folioApprovalStatus(s:State,a:Approval):Approval['status']{return folioApprovalExpired(s,a)?'Expired':a.status;}
/** Folio approvals (stream C kinds) for one booking, newest first. */
export function folioApprovalsFor(s:State,bookingId:string){return s.approvals.filter(a=>a.booking===bookingId&&FOLIO_APPROVAL_KINDS.includes(folioKind(a))).slice().reverse();}

/** Receipt amount still refundable: original − executed refunds − outstanding (Pending/Approved, unexpired) requests. */
export function folioRefundable(s:State,b:Booking,receiptId:string,exceptApproval=''){
  const r=b.receipts.find(r=>r.id===receiptId);
  if(!r||r.kind!=='payment')return 0;
  const refunded=b.receipts.filter(x=>x.kind==='refund'&&x.refundOf===receiptId).reduce((a,x)=>a+x.amount,0);
  const outstanding=s.approvals.filter(a=>a.id!==exceptApproval&&a.kind==='refund'&&a.target===receiptId&&(a.status==='Pending'||a.status==='Approved')&&!folioApprovalExpired(s,a)).reduce((a,x)=>a+x.amount,0);
  return Math.max(0,r.amount-refunded-outstanding);
}
/** Amount already refunded against a receipt. */
export function folioRefunded(b:Booking,receiptId:string){return b.receipts.filter(x=>x.kind==='refund'&&x.refundOf===receiptId).reduce((a,x)=>a+x.amount,0);}
/** Outstanding (Pending/Approved) refund requests on a booking, excluding one. Used for the departed-stay credit check. */
export function folioOutstandingRefunds(s:State,bookingId:string,exceptApproval=''){
  return s.approvals.filter(a=>a.id!==exceptApproval&&a.booking===bookingId&&a.kind==='refund'&&(a.status==='Pending'||a.status==='Approved')&&!folioApprovalExpired(s,a)).reduce((a,x)=>a+x.amount,0);
}
/** Agreed nights vs posted room-night lines (BL-017 header). */
export function folioNights(b:Booking){
  const agreed=Math.max(0,Math.round((Date.parse(b.departure)-Date.parse(b.arrival))/86400000));
  const dates=new Set(b.lines.filter(l=>l.kind==='room'&&!l.reversalOf).map(l=>l.date));
  return {agreed,posted:dates.size};
}
/** Whether a posted line already has a reversal. */
export const folioLineReversed=(b:Booking,lineId:string)=>b.lines.some(l=>l.reversalOf===lineId);
/** Whether a posted line can be corrected: not itself a reversal, not reversed, no open correction request. */
export function folioLineCorrectable(s:State,b:Booking,l:Line){
  return !l.reversalOf&&!folioLineReversed(b,l.id)&&!s.approvals.some(a=>a.kind==='correction'&&a.target===l.id&&a.status==='Pending'&&!folioApprovalExpired(s,a));
}
/** Company credit applied to a folio (receivable). */
export function folioCompanyCredit(b:Booking){return b.receipts.filter(r=>r.kind==='company-credit').reduce((a,r)=>a+r.amount,0);}
/** Receipt kind labels for display. */
export const folioReceiptLabel:Record<string,string>={payment:'Payment / advance',deposit:'Refundable deposit','deposit-return':'Deposit returned',refund:'Payment refund','company-credit':'Company credit (receivable)',
  'deposit-apply':'Deposit applied at check-out','ota-credit':'OTA credit (receivable)'}; // ACC-2 flows (contract §4)

/** Company-credit request state for a folio (SCR-10). 'approved' = will be applied at checkout; 'stale' = approved but the folio changed. */
export function folioCompanyCreditState(s:State,b:Booking,balance:number):{state:'none'|'pending'|'approved'|'stale';approval?:Approval}{
  const list=s.approvals.filter(a=>a.booking===b.id&&a.kind==='company-credit'&&!folioApprovalExpired(s,a));
  const pending=list.find(a=>a.status==='Pending');if(pending)return {state:'pending',approval:pending};
  const approved=list.filter(a=>a.status==='Approved').slice(-1)[0];
  if(approved)return {state:approved.amount===balance&&approved.version===b.version?'approved':'stale',approval:approved};
  return {state:'none'};
}
/** Reasons a checkout would be refused right now, in the order CHECKOUT checks them (preview only; the command decides). */
export function folioCheckoutBlockers(s:State,b:Booking,t:{balance:number;deposit:number}){
  const out:string[]=[];
  if(b.status!=='In house')return ['This stay is not in house.'];
  const n=folioNights(b);
  const missing:string[]=[];for(let d=b.arrival;d<b.departure;d=new Date(Date.parse(d+'T12:00:00Z')+86400000).toISOString().slice(0,10))if(!b.lines.some(l=>l.kind==='room'&&l.date===d))missing.push(d);
  if(missing.length)out.push(`Room nights not yet posted: ${missing.join(', ')} (${n.posted} of ${n.agreed} posted by the night audit).`);
  if(t.balance>0&&folioCompanyCreditState(s,b,t.balance).state!=='approved')out.push('Balance due — settle it, or use an approved company credit for exactly the balance.');
  if(t.balance<0)out.push('Credit balance — refund the excess receipt first.');
  if(t.deposit>0)out.push('Refundable deposit still held — return it first.');
  if(s.approvals.some(a=>a.booking===b.id&&a.status==='Pending'&&!folioApprovalExpired(s,a)))out.push('A request on this folio is pending approval.');
  return out;
}

// AF-PAY08 (AF-DEC-1 · PAY-08): duplicate-receipt guard. A payment / deposit equal in kind, tender and amount to one recorded on the same folio in the
// last 10 minutes needs an explicit confirmation. The server enforces it on its own clock (RecordReceipt, details.reason DUPLICATE_SUSPECTED); the demo
// mock enforces it on s.clock (receipts carry `at`), and the payment dialog shows an early amber note from the same helper.
export const FOLIO_DUP_REASON='DUPLICATE_SUSPECTED';
export const FOLIO_DUP_WINDOW_MINUTES=10;
export type FolioDupRow={method:string;amount:number};
const dupAt=(r:Receipt)=>(r as Receipt&{at?:string}).at;
const dupMinutes=(clock:string,at:string)=>Math.floor((Date.parse(clock+':00Z')-Date.parse(at+':00Z'))/60000);
/** The newest receipt of this booking with the same kind, method and amount (paise) as one of `rows`, recorded within 10 minutes of `clock`. */
export function recentEqualReceipt(b:Booking,rows:readonly FolioDupRow[],clock:string,kind:string='payment'):{receipt:Receipt;row:FolioDupRow;minutes:number}|undefined{
  if(kind!=='payment'&&kind!=='deposit')return undefined; // refunds and deposit returns are never flagged
  let best:{receipt:Receipt;row:FolioDupRow;minutes:number}|undefined;
  for(const r of b.receipts){
    const at=dupAt(r);if(!at||r.kind!==kind)continue;
    const minutes=dupMinutes(clock,at);if(!(minutes>=0&&minutes<=FOLIO_DUP_WINDOW_MINUTES))continue;
    const row=rows.find(x=>x.amount===r.amount&&x.method.toLowerCase()===r.method.toLowerCase());
    if(row&&(!best||minutes<best.minutes||(minutes===best.minutes&&r.id>best.receipt.id)))best={receipt:r,row,minutes};
  }
  return best;
}
/** Same sentence as the server (DuplicateReceiptGuard.Refusal). */
export function folioDupMessage(method:string,kind:string,amountText:string,at:string,minutes:number,receiptId:string){
  return `A ${method} ${kind==='deposit'?'deposit':'payment'} of ${amountText} was recorded on this folio at ${at.slice(11,16)} IST (${minutes} minutes ago), receipt ${receiptId}. If this is a second, separate payment, confirm it.`;
}
/** `details.reason` of a refused RecordReceipt (or the mock's equivalent): the receipt that looks equal, else undefined. */
export function folioDupDetails(e:unknown):{receiptId:string}|undefined{
  const d=(e as {details?:{reason?:unknown;receiptId?:unknown}}|null|undefined)?.details;
  return d&&d.reason===FOLIO_DUP_REASON?{receiptId:String(d.receiptId??'')}:undefined;
}
/** The shell (App.run) announces a refused duplicate for the dialog that owns `key`; the payment dialogs listen (onFolioDuplicate returns the unsubscribe). */
export const FOLIO_DUP_EVENT='hcc:duplicate-suspected';
export function announceFolioDuplicate(key:string|undefined,e:unknown):void{
  const d=folioDupDetails(e);if(!d||typeof window==='undefined'||typeof CustomEvent==='undefined')return;
  window.dispatchEvent(new CustomEvent(FOLIO_DUP_EVENT,{detail:{key,receiptId:d.receiptId,message:(e as Error).message}}));
}
export function onFolioDuplicate(key:string|undefined,cb:(d:{receiptId:string;message:string})=>void):()=>void{
  if(typeof window==='undefined')return ()=>undefined;
  const on=(ev:Event)=>{const d=(ev as CustomEvent).detail as {key?:string;receiptId:string;message:string};if(d.key===key)cb({receiptId:d.receiptId,message:d.message});};
  window.addEventListener(FOLIO_DUP_EVENT,on);return ()=>window.removeEventListener(FOLIO_DUP_EVENT,on);
}
// AF-PAY08 end
