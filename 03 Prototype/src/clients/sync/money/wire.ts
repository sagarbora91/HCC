// Money area (W4-3) — server DTOs (08 Production/src/Hcc.Contracts/{Billing,Invoices,Closing,Groups}) and the pure mappers to the
// prototype's state shapes. No I/O here (sync/money.ts does the reads and commands). Money stays integer paise; GST is NEVER recomputed:
// every line keeps the server's posted taxBps snapshot, and folioFromServer refuses a folio whose posted tax or totals the prototype's own
// selectors (lineTax / totals) would show differently.
import type {Booking,Line,Receipt,Approval} from '../../../services/mock';
import {totals,lineTax} from '../../../services/mock';
import type {ShiftRecord} from '../../../domain/streams/closing';
import type {CBState,CBLogEntry} from '../../../domain/streams/cashbook';
import type {GRPBlock,GRPCompany,GRPLimitRequest} from '../../../domain/streams/groups';
import {HccClientError} from '../../errors';
import {protoId,protoUserId,utcToClock,paise,isoDate} from '../map';

// ── Billing (Hcc.Contracts.Billing) ──
export type SrvFolioLine={chargeId:number;kind:string;serviceDate:string;label:string;amountPaise:number;taxBps:number;taxPaise:number;reversesChargeId:number|null;reversed:boolean};
export type SrvFolioReceipt={receiptId:number;kind:string;tender:string;amountPaise:number;businessDate:string;reference:string;refundOf:number|null;refundedPaise:number;refundablePaise:number};
export type SrvApproval={approvalId:number;action:string;targetKind:string;targetId:number;amountPaise:number;status:string;reason:string;requestedBy:number;
  decidedBy:number|null;expiresUtc:string;resultId:number|null;version:string};
export type SrvFolioTotals={netPaise:number;taxPaise:number;totalPaise:number;paidPaise:number;depositPaise:number;companyCreditPaise:number;balancePaise:number};
export type SrvFolio={folioId:number;reservationId:number;reservationStatus:string;businessDate:string;version:string;lines:SrvFolioLine[];receipts:SrvFolioReceipt[];
  approvals:SrvApproval[];totals:SrvFolioTotals};
/** GET /folios/by-reservation/{id} (W4-3). */
export type SrvReservationFolio={reservationId:number;stayId:number|null;stayVersion:string|null;folio:SrvFolio|null};
export type SrvShift={shiftId:number;tillId:number;businessDate:string;status:'Open'|'Submitted'|'Closed';openingFloatPaise:number;expectedPaise:number|null;
  countedPaise:number|null;approvalId:number|null;version:string};
export type SrvDeskShift={shift:SrvShift;submittedBy:number|null;differenceReason:string|null;closedBy:number|null;approval:SrvApproval|null};
export type SrvDeskTill={till:{tillId:number;code:string;name:string;version:string};active:SrvDeskShift|null;expected:{expectedPaise:number}|null;lastClosed:SrvDeskShift|null};
/** GET /cashier/desk (W4-3). */
export type SrvCashDesk={businessDate:string;tills:SrvDeskTill[]};

// ── Invoices (Hcc.Contracts.Invoices) ──
export type SrvCreditNoteSummary={creditNoteId:number;creditNoteNo:string;noteDate:string;reason:string;taxablePaise:number;totalPaise:number};
export type SrvInvoice={invoiceId:number;invoiceNo:string;docType:string;invoiceDate:string;sourceKind:string;sourceId:number;totalPaise:number;creditNotes:SrvCreditNoteSummary[]};
export type SrvCreditNoteRequest={requestId:number;invoiceId:number;invoiceNo:string;mode:string;amountPaise:number|null;lines:{invoiceLineNo:number;taxablePaise:number}[];
  reason:string;status:string;direct:boolean;requestedBy:number;requestedUtc:string;expiresUtc:string;decidedBy:number|null;decisionReason:string|null;creditNoteId:number|null;version:string};

// ── Cash book (Hcc.Contracts.Closing) ──
export type SrvCashBookEvent={action:string;principalId:number;atUtc:string;reason:string|null};
export type SrvCashBookDay={businessDate:string;status:string;locked:boolean;lockedBy:number|null;lockedUtc:string|null;history:SrvCashBookEvent[]};
export type SrvCashBookRange={from:string;to:string;businessDate:string;days:SrvCashBookDay[]};

// ── Groups (Hcc.Contracts.Groups) ──
export type SrvGroupSummary={groupId:number;code:string;name:string;status:string;groupBlockId:number|null;blockCode:string|null;arrival:string|null;departure:string|null;members:number;version:string};
export type SrvGroupStatement={groupId:number;code:string;name:string;status:string;organiser:string;billingName:string;billingContact:string;gstin:string|null;
  groupBlockId:number|null;blockCode:string|null;blockStatus:string|null;cutoff:string|null;businessDate:string;
  rules:{reservationId:number|null;category:string;target:string}[];
  pickup:{roomType:string;allotted:number;picked:number;released:number;remaining:number;ratePaise:number;pickedPercent:number}[];
  members:{reservationId:number;publicNo:string;guest:string;room:string|null;status:string;arrival:string;departure:string;folioId:number|null}[];version:string};

const LINE_KIND:Record<string,Line['kind']>={room:'room',service:'service',discount:'discount',correction:'adjustment'};
const RECEIPT_KIND=['payment','deposit','deposit-return','refund','company-credit'];

export function lineFromServer(l:SrvFolioLine):Line{
  const kind=LINE_KIND[l.kind];if(!kind)throw new HccClientError('PROTOCOL',`Unknown folio line kind ${l.kind}.`);
  const out:Line={id:protoId(l.chargeId),label:l.label,amount:paise(l.amountPaise,'amountPaise'),date:isoDate(l.serviceDate,'serviceDate'),kind,taxBps:l.taxBps};
  if(l.reversesChargeId!=null)out.reversalOf=protoId(l.reversesChargeId);
  // Never recompute GST: the screens derive a line's tax from (amount, taxBps); it must equal what the server posted.
  if(lineTax(out)!==l.taxPaise)throw new HccClientError('PROTOCOL',`Folio line ${l.chargeId}: posted GST ${l.taxPaise} paise differs from its rate snapshot.`);
  return out;
}
export function receiptFromServer(r:SrvFolioReceipt):Receipt{
  if(!RECEIPT_KIND.includes(r.kind))throw new HccClientError('PROTOCOL',`Unknown receipt kind ${r.kind}.`);
  const out:Receipt={id:protoId(r.receiptId),amount:paise(r.amountPaise,'amountPaise'),method:r.tender,date:isoDate(r.businessDate,'businessDate'),
    kind:r.kind as Receipt['kind'],reference:r.reference,actor:''};
  if(r.refundOf!=null)out.refundOf=protoId(r.refundOf);
  return out;
}
const APPROVAL_STATUS=['Pending','Approved','Rejected','Expired','Invalidated','Executed'];
const status=(s:string):Approval['status']=>(APPROVAL_STATUS.includes(s)?s:s==='Issued'?'Executed':'Invalidated') as Approval['status'];

/** Billing approval (folio adjustment) → prototype Approval. Discounts keep `kind` undefined (prototype folioKind default). */
export function approvalFromServer(a:SrvApproval,bookingId:string):Approval{
  const out:Approval={id:protoId(a.approvalId),booking:bookingId,amount:paise(a.amountPaise,'amountPaise'),reason:a.reason,status:status(a.status),
    actor:protoUserId(a.requestedBy),version:1,expiresAt:utcToClock(a.expiresUtc)||undefined,meta:{command:'APPROVAL'}};
  if(a.action==='refund'){out.kind='refund';out.target=protoId(a.targetId);}
  else if(a.action==='credit')out.kind='company-credit';
  else if(a.action==='correction'){out.kind='correction';out.target=protoId(a.targetId);}
  else if(a.action==='cash-difference')out.kind='cash-difference';
  if(a.decidedBy!=null)out.approver=protoUserId(a.decidedBy);
  if(out.expiresAt===undefined)delete out.expiresAt;
  return out;
}
/** Credit-note requests use their own id space on the server: prototype id `cnr-<requestId>` (APPROVAL routes it to DecideCreditNote). */
export const CNR_PREFIX='cnr-';
export function creditNoteRequestFromServer(r:SrvCreditNoteRequest,bookingId:string):Approval{
  const amount=r.amountPaise??r.lines.reduce((a,l)=>a+l.taxablePaise,0);
  const out:Approval={id:CNR_PREFIX+r.requestId,booking:bookingId,amount:paise(amount,'amountPaise'),reason:r.reason,status:status(r.status),actor:protoUserId(r.requestedBy),
    version:1,kind:'credit-note',target:r.invoiceNo,meta:{command:'APPROVAL'}};
  const exp=utcToClock(r.expiresUtc);if(exp)out.expiresAt=exp;
  if(r.decidedBy!=null)out.approver=protoUserId(r.decidedBy);
  return out;
}

/** The booking with the server folio's lines and receipts (+ invoice number and credit notes when an invoice exists). Refuses a folio
 *  whose totals, as the prototype screens compute them from the mapped lines/receipts, would differ from the server's (paise-exact). */
export function folioIntoBooking(b:Booking,f:SrvFolio|null,inv:SrvInvoice|null):Booking{
  const next:Booking={...b,lines:f?f.lines.map(lineFromServer):[],receipts:f?f.receipts.map(receiptFromServer):[]};
  if(f){
    const t=totals(next),s=f.totals;
    if(t.net!==s.netPaise||t.tax!==s.taxPaise||t.total!==s.totalPaise||t.paid!==s.paidPaise||t.deposit!==s.depositPaise||t.balance!==s.balancePaise)
      throw new HccClientError('PROTOCOL',`Folio ${f.folioId}: totals do not reconcile with the posted lines and receipts.`);
  }
  if(inv){
    next.invoiceNo=inv.invoiceNo;
    next.creditNotes=inv.creditNotes.map(c=>({id:protoId(c.creditNoteId),no:c.creditNoteNo,amount:paise(c.taxablePaise,'taxablePaise'),reason:c.reason,
      date:isoDate(c.noteDate,'noteDate'),approvalId:'',lineId:''}));
  }else{delete next.invoiceNo;delete next.creditNotes;}
  return next;
}

/** Cash-desk till the cashier screens show (DEC-052 one shared till): the till with an active shift, else the first till. */
export const deskTill=(d:SrvCashDesk):SrvDeskTill|undefined=>d.tills.find(t=>t.active)??d.tills[0];
/** shiftOpen: an Open shift of the current business date on the desk till (a Submitted count pauses cash, as in the prototype). */
export function deskShiftOpen(d:SrvCashDesk){const a=deskTill(d)?.active?.shift;return !!a&&a.status==='Open'&&a.businessDate===d.businessDate;}
/** openingCash: the active shift's float; after a close on this business date, that shift's float; otherwise the next float (last count). */
export function deskOpeningCash(d:SrvCashDesk){
  const t=deskTill(d);if(!t)return 0;
  if(t.active)return paise(t.active.shift.openingFloatPaise,'openingFloatPaise');
  const c=t.lastClosed?.shift;if(!c)return 0;
  return c.businessDate===d.businessDate?paise(c.openingFloatPaise,'openingFloatPaise'):paise(c.countedPaise??0,'countedPaise');
}
/** shift: the submitted count awaiting a difference decision, or the count that closed today's shift; undefined while counting is open. */
export function deskShiftRecord(d:SrvCashDesk):ShiftRecord|undefined{
  const t=deskTill(d);if(!t)return undefined;
  const a=t.active;
  if(a&&a.shift.status==='Submitted'){
    const r:ShiftRecord={date:a.shift.businessDate,status:'Submitted',expected:paise(a.shift.expectedPaise??0,'expectedPaise'),counted:paise(a.shift.countedPaise??0,'countedPaise'),
      submittedBy:a.submittedBy!=null?protoUserId(a.submittedBy):''};
    if(a.differenceReason)r.reason=a.differenceReason;
    if(a.shift.approvalId!=null)r.approval=protoId(a.shift.approvalId);
    return r;
  }
  const c=t.lastClosed;
  if(!a&&c&&c.shift.businessDate===d.businessDate){
    const r:ShiftRecord={date:c.shift.businessDate,status:'Closed',expected:paise(c.shift.expectedPaise??0,'expectedPaise'),counted:paise(c.shift.countedPaise??0,'countedPaise'),
      submittedBy:c.submittedBy!=null?protoUserId(c.submittedBy):''};
    if(c.differenceReason)r.reason=c.differenceReason;
    if(c.shift.approvalId!=null){r.approval=protoId(c.shift.approvalId);if(c.approval?.decidedBy!=null)r.decidedBy=protoUserId(c.approval.decidedBy);}
    return r;
  }
  return undefined;
}
/** shiftClose: the count that closed today's shift (next float = counted). */
export function deskShiftClose(d:SrvCashDesk){
  const t=deskTill(d),c=t?.lastClosed;
  if(!t||t.active||!c||c.shift.businessDate!==d.businessDate)return undefined;
  return {expected:paise(c.shift.expectedPaise??0,'expectedPaise'),counted:paise(c.shift.countedPaise??0,'countedPaise'),countedBy:c.submittedBy!=null?protoUserId(c.submittedBy):undefined};
}
/** The cash-difference request of a submitted count as the prototype's approval (kind cash-difference, meta.command SHIFT_DIFFERENCE_DECIDE). */
export function cashDifferenceApproval(sh:ShiftRecord|undefined,approver?:string):Approval|undefined{
  if(!sh||sh.status!=='Submitted'||!sh.approval)return undefined;
  const a:Approval={id:sh.approval,booking:'',amount:Math.abs(sh.counted-sh.expected),reason:sh.reason??'',status:'Pending',actor:sh.submittedBy,version:1,
    kind:'cash-difference',target:sh.date,meta:{command:'SHIFT_DIFFERENCE_DECIDE',expected:sh.expected,counted:sh.counted}};
  if(approver)a.approver=approver;
  return a;
}

/** Cash-book locks and lock/reopen history (s.cashbookX) from the range view. Figures stay derived on the client (cbDay). */
export function cashbookFromServer(r:SrvCashBookRange):CBState{
  const locks:CBState['locks']={},history:CBState['history']={};
  for(const d of r.days){
    const log:CBLogEntry[]=d.history.map(e=>{const x:CBLogEntry={action:e.action==='reopen'?'reopen':'lock',by:protoUserId(e.principalId),at:utcToClock(e.atUtc)};
      if(e.reason)x.reason=e.reason;return x;});
    if(log.length)history[d.businessDate]=log;
    if(d.locked)locks[d.businessDate]={lockedBy:d.lockedBy!=null?protoUserId(d.lockedBy):'',lockedAt:utcToClock(d.lockedUtc),log};
  }
  return {locks,history};
}

/** A server group with a block → the prototype's block card (read-only figures; member room types come from the reservation reads). */
export type SrvCompany={companyId:number;name:string;gstin:string|null;contact:string|null;phone:string|null;email:string|null;city:string|null;rates:Record<string,number>;creditLimitPaise:number;version:string};
export type SrvCompanyRequest={requestId:number;companyId:number;fromPaise:number;toPaise:number;reason:string;status:string;requestedBy:number;requestedUtc:string;decidedBy:number|null;note:string|null};
export type SrvCompanyBook={companies:SrvCompany[];requests:SrvCompanyRequest[]};
export function companyFromServer(c:SrvCompany):GRPCompany{
  const rates:Record<string,number>={};for(const [t,v] of Object.entries(c.rates??{}))rates[t]=paise(v,'rate');
  return {id:protoId(c.companyId),name:c.name,gstin:c.gstin??'',contact:c.contact??'',phone:c.phone??'',email:c.email??'',city:c.city??'',rates,
    creditLimit:paise(c.creditLimitPaise,'creditLimitPaise'),openingOutstanding:0,limitHistory:[]};
}
export function limitRequestFromServer(r:SrvCompanyRequest):GRPLimitRequest{
  const status=r.status==='Approved'||r.status==='Rejected'?r.status:'Pending';
  return {id:protoId(r.requestId),company:protoId(r.companyId),from:paise(r.fromPaise,'fromPaise'),to:paise(r.toPaise,'toPaise'),reason:r.reason,actor:protoUserId(r.requestedBy),
    at:utcToClock(r.requestedUtc),status,...(r.decidedBy!=null?{approver:protoUserId(r.decidedBy)}:{}),...(r.note?{note:r.note}:{})};
}

export function blockFromServer(g:SrvGroupStatement,arrival:string,departure:string,memberTypes:Record<string,string>):GRPBlock{
  const allot:Record<string,number>={},rates:Record<string,number>={},released:Record<string,number>={};
  for(const p of g.pickup){allot[p.roomType]=p.allotted;rates[p.roomType]=paise(p.ratePaise,'ratePaise');if(p.released)released[p.roomType]=p.released;}
  const room=g.rules.find(r=>r.reservationId==null&&r.category==='room');
  const st=g.blockStatus==='Released'||g.blockStatus==='Cancelled'?g.blockStatus:'Open';
  return {id:protoId(g.groupId),name:g.name,billTo:room?.target==='guest'?'guest':'company',arrival,departure,cutoff:g.cutoff??arrival,allot,rates,released,
    members:g.members.map(m=>({booking:protoId(m.reservationId),type:memberTypes[protoId(m.reservationId)]??''})),status:st,
    contact:g.billingContact,createdBy:'',createdAt:''};
}
