// LF lane lf-money-bill · contract §4 (A2) BILL_CORRECTION_REQUEST; the `bill-correction` approval branch (Owner only, RBAC_OWNER_DECIDES)
// lives in the LF-BILL block of handlers/folio.ts and calls `blcExecute`: (a) a credit note for the whole current invoice (CRN), (b) the
// folio changes, (c) a new tax invoice (INV, next number, revision + 1), (d) the correction record — one command, atomic.
import type {Handler,Kit} from '../commands';
import type {Approval,BLCCorrection,Booking} from '../../../domain/types';
import {money} from '../../../domain/money';
import {totals} from '../../../domain/selectors';
import {rbacCanRequestCorrection} from '../../../domain/streams/rbac';
import {folioExpiresAt} from '../../../domain/streams/folio';
import {fdmAllocate} from '../../../domain/streams/frontdesk-master';
import {sfIsSplit} from '../../../domain/streams/folio-split'; // F8-SF-DOMAIN
import {sfFail} from './folio-split'; // F8-SF-DOMAIN
import {blcPreview,blcRefusal,blcLive,blcInvoiceDate,blcState} from '../../../domain/streams/bill-correction';

const BILL_CORRECTION_REQUEST:Handler=k=>{
  // PERM-FOL-010 folio.billCorrection.request (PERM-FOL-005 holders); approve = Owner.
  const {s,cmd,open,assert,fail,uid}=k;
  if(!rbacCanRequestCorrection(s,uid))fail('FORBIDDEN','Access denied: billing permission or the Accountant role is required.');
  open();
  const x=s.bookings.find(b=>b.id===cmd.booking||b.id===cmd.id);assert(x,'Booking not found.','NOT_FOUND');
  // F8-SF-DOMAIN · a split stay is corrected by a credit note per folio (SF-CONTRACT §3, SFQ-11)
  if(sfIsSplit(x!))sfFail('INVALID_TRANSITION','SF_SPLIT_BILL_CORRECTION','This stay has a split bill (one invoice per folio): correct it with a credit note on the folio concerned.');
  // F8-SF-DOMAIN end
  const refusal=blcRefusal(s,x);if(refusal)fail(/s\.34/.test(refusal)?'DAY_CLOSED':'INVALID_TRANSITION',refusal);
  const reason=String(cmd.reason??'').trim();assert(reason,'A reason is required.');
  const p=blcPreview(s,x!,cmd.changes);if('error' in p)fail('VALIDATION',p.error);
  const pv=p as Exclude<typeof p,{error:string}>;
  const a:Approval={id:k.id('APR'),booking:x!.id,actor:uid,version:x!.version,status:'Pending',expiresAt:folioExpiresAt(s.clock),amount:Math.abs(pv.delta),
    reason:reason.slice(0,200),kind:'bill-correction',target:x!.invoiceNo,meta:{command:'APPROVAL',oldTotal:pv.oldTotal,newTotal:pv.newTotal,delta:pv.delta}};
  s.approvals.push(a);
  const c:BLCCorrection={id:k.id('BLC'),booking:x!.id,oldInvoiceNo:x!.invoiceNo!,changes:pv.changes,reason:a.reason,requestedBy:uid,at:s.clock,approvalId:a.id,
    status:'Pending',oldTotal:pv.oldTotal,newTotal:pv.newTotal,delta:pv.delta};
  (s.billCorrectionX??(s.billCorrectionX={corrections:[]})).corrections.push(c);
  a.meta={...a.meta,correction:c.id};
  return `${x!.id} · correction of ${x!.invoiceNo} requested · ${money(pv.oldTotal)} → ${money(pv.newTotal)} (${pv.delta>=0?'+':''}${money(pv.delta)}) · ${a.id}`;
};

/** Execute an approved correction (called from the APPROVAL handler, Owner only). Returns the detail suffix. */
export function blcExecute(k:Kit,x:Booking,a:Approval):string{
  const {s,assert,fail,uid}=k;
  const c=blcState(s).corrections.find(c=>c.approvalId===a.id);assert(c,'The correction record no longer exists.','NOT_FOUND');
  const refusal=blcRefusal(s,{...x,id:'\u0000'} as Booking);if(refusal&&/s\.34/.test(refusal))fail('DAY_CLOSED',refusal); // window re-checked at approval
  assert(x.status==='Departed'&&x.invoiceNo===c!.oldInvoiceNo,'The invoice changed after this request. Reject it and request again.','STALE_VERSION');
  const p=blcPreview(s,x,c!.changes);if('error' in p)fail('INVALID_TRANSITION',p.error);
  const pv=p as Exclude<typeof p,{error:string}>;
  const live=blcLive(x),oldNo=x.invoiceNo!,oldDate=blcInvoiceDate(s,x);
  // (a) credit note for the whole current invoice: a reversal of every live line, at its own GST rate (exact tax), on the business date.
  const crn=fdmAllocate(s,'CRN');
  live.forEach((l,i)=>x.lines.push({id:`${c!.id}/cn${i+1}`,label:`Credit note ${crn} · ${l.label}`,amount:-l.amount,date:s.date,kind:'adjustment',taxBps:l.taxBps,reversalOf:l.id,approvalId:a.id}));
  const net=live.reduce((t,l)=>t+l.amount,0);
  (x.creditNotes??=[]).push({id:k.id('CN'),no:crn,amount:net,reason:`Bill correction ${c!.id} · ${a.reason}`,date:s.date,approvalId:a.id,lineId:`${c!.id}/cn1`});
  // (b) the corrected folio as new lines (ids `<correction>/n<i>` tie them to the new invoice for GSTR-1).
  pv.lines.forEach((l,i)=>x.lines.push({...l,id:`${c!.id}/n${i+1}`}));
  // (c) the new tax invoice, revision + 1.
  const inv=fdmAllocate(s,'INV');
  const prev=x.invoiceRevisions?.at(-1)?.no;
  (x.invoiceRevisions??=[]).push({no:oldNo,date:oldDate,supersedes:prev&&prev!==oldNo?prev:''});
  x.invoiceNo=inv;x.version++;
  // (d) the record.
  const delta=totals(x).balance;
  Object.assign(c!,{creditNoteNo:crn,newInvoiceNo:inv,approvedBy:uid,decidedAt:s.clock,status:'Issued',oldTotal:pv.oldTotal,newTotal:pv.newTotal,delta:pv.delta,
    settlement:delta<0?'refund-due':delta>0?'balance-due':'none'} satisfies Partial<BLCCorrection>);
  a.status='Executed';a.meta={...a.meta,creditNote:crn,invoice:inv};
  return ` · ${crn} credits ${oldNo}; new invoice ${inv}`+(delta>0?` · balance due ${money(delta)}`:delta<0?` · refund due ${money(-delta)}`:'');
}

export const handlers:Record<string,Handler>={BILL_CORRECTION_REQUEST};
