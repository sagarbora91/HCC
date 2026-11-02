// LF lane lf-money-bill · contract §4 (A2 correct a bill after check-out, GST-safe form). Pure helpers: which folio lines make up the
// current invoice, the preview of a correction (new line set, old / new totals, delta), refusals, the s.34(2) window and the
// "supersedes / superseded by" print notes. Export names are prefixed `blc` / `BLC_`.
import type {Booking,BLCChange,BLCCorrection,BLCState,Line,State} from '../types';
import {gstLineTax,gstForCharge,gstForRoomNight,GST_CHARGE_CATEGORIES} from '../gst';
import {money} from '../money';
import {folioKind,folioApprovalExpired} from './folio';

export type {BLCChange,BLCCorrection,BLCState};
export const blcState=(s:State):BLCState=>s.billCorrectionX??{corrections:[]};
/** A line belongs to the current invoice while it is neither a reversal nor reversed (credit-note lines reverse the whole earlier invoice). */
export const blcLive=(b:Booking)=>b.lines.filter(l=>!l.reversalOf&&!b.lines.some(r=>r.reversalOf===l.id));
const gross=(ls:readonly {amount:number;taxBps:number}[])=>ls.reduce((a,l)=>a+l.amount+gstLineTax(l.amount,l.taxBps),0);
/** s.34(2) CGST Act window (OQ-2, CA to confirm): a credit note for an invoice of FY y-(y+1) may issue until 30 November of y+1. */
export function blcDeadline(invoiceDate:string):string{const y=+invoiceDate.slice(0,4),m=+invoiceDate.slice(5,7);return `${(m>=4?y:y-1)+1}-11-30`;}
/** Date of the stay's current invoice: the last issued correction's decision date, else the departure (allocated at check-out). */
export function blcInvoiceDate(s:State,b:Booking):string{
  const c=blcState(s).corrections.filter(c=>c.booking===b.id&&c.status==='Issued').at(-1);
  return c?.decidedAt?.slice(0,10)??b.departure;
}
/** Status as shown: a Pending record whose approval was rejected or expired reads Rejected / Expired. */
export function blcStatus(s:State,c:BLCCorrection):BLCCorrection['status']{
  if(c.status!=='Pending')return c.status;
  const a=s.approvals.find(x=>x.id===c.approvalId);
  if(!a)return 'Pending';
  if(a.status==='Rejected')return 'Rejected';
  if(a.status==='Expired'||folioApprovalExpired(s,a))return 'Expired';
  return 'Pending';
}
export const blcCorrections=(s:State,bookingId?:string)=>blcState(s).corrections.filter(c=>!bookingId||c.booking===bookingId).map(c=>({...c,status:blcStatus(s,c)}));

type NewLine=Omit<Line,'id'>;
/** The night a room line bills: the date in its label ("Room night · YYYY-MM-DD", kept when a correction re-dates the line), else its date. */
export const blcNightOf=(l:Line)=>/Room night · (\d{4}-\d{2}-\d{2})/.exec(l.label)?.[1]??l.date;
/** The corrected invoice's lines (without ids), totals and delta; or `error`. Pure. */
export function blcPreview(s:State,b:Booking,changes:readonly BLCChange[]):{lines:NewLine[];oldTotal:number;newTotal:number;delta:number;changes:BLCChange[]}|{error:string}{
  if(!Array.isArray(changes)||!changes.length)return {error:'Add at least one change to the bill.'};
  const live=blcLive(b),out:NewLine[]=[],norm:BLCChange[]=[];
  const rate=new Map<string,number>(),remove=new Set<string>();
  for(const c of changes as BLCChange[]){
    if(c?.kind==='room-rate'){
      if(!/^\d{4}-\d{2}-\d{2}$/.test(String(c.night))||c.night<b.arrival||c.night>=b.departure)return {error:'Choose a night of the stay.'};
      if(!Number.isInteger(c.newRate)||c.newRate<0)return {error:'Enter the corrected room rent in whole paise (0 or more).'};
      const l=live.find(l=>l.kind==='room'&&l.amount>0&&blcNightOf(l)===c.night);
      if(!l)return {error:`No room rent is billed for the night of ${c.night}.`};
      if(rate.has(c.night))return {error:`The night of ${c.night} is changed twice.`};
      if(c.newRate===l.amount)return {error:`The room rent of ${c.night} is already ${money(l.amount)}.`};
      rate.set(c.night,c.newRate);norm.push({kind:'room-rate',night:c.night,oldRate:l.amount,newRate:c.newRate});
    }else if(c?.kind==='remove-line'){
      const l=b.lines.find(l=>l.id===c.lineId);
      if(!l)return {error:'Select a billed line to remove.'};
      if(l.reversalOf)return {error:'A reversal line cannot itself be removed.'};
      if(!live.includes(l))return {error:'This line is already reversed.'};
      if(l.kind==='room')return {error:'Change a night’s room rent instead of removing it.'};
      if(remove.has(l.id))return {error:'The same line is removed twice.'};
      remove.add(l.id);norm.push({kind:'remove-line',lineId:l.id});
    }else if(c?.kind==='add-line'){
      const label=String(c.label??'').trim();
      if(!label)return {error:'Describe the added charge.'};
      if(!Number.isInteger(c.amount)||c.amount<=0)return {error:'Enter the added amount in whole paise (more than 0).'};
      if(!GST_CHARGE_CATEGORIES.some(x=>x.id===c.gstCategory))return {error:'Choose a GST category: food, laundry, banquet or other.'};
      if(!/^\d{4}-\d{2}-\d{2}$/.test(String(c.date))||c.date<b.arrival||c.date>b.departure)return {error:'The service date is outside the stay.'};
      norm.push({kind:'add-line',label,amount:c.amount,gstCategory:c.gstCategory,date:c.date,...(c.extraId?{extraId:c.extraId}:{})});
    }else return {error:'Unknown change.'};
  }
  for(const l of live){
    if(remove.has(l.id))continue;
    const night=l.kind==='room'&&l.amount>0&&rate.has(blcNightOf(l))?blcNightOf(l):undefined;
    const {id:_id,reversalOf:_r,approvalId:_a,...rest}=l;void _id;void _r;void _a;
    if(night!==undefined){const nr=rate.get(night)!;out.push({...rest,label:`Room night · ${night}`,amount:nr,date:s.date,taxBps:gstForRoomNight(s,night,nr).bps});}
    else out.push({...rest,date:s.date});
  }
  for(const c of norm)if(c.kind==='add-line')out.push({label:c.label,amount:c.amount,date:s.date,kind:'service',taxBps:gstForCharge(s,c.date,c.gstCategory as never).bps});
  const oldTotal=gross(live),newTotal=gross(out);
  return {lines:out,oldTotal,newTotal,delta:newTotal-oldTotal,changes:norm};
}
/** Refusal of a BILL_CORRECTION_REQUEST for this stay (before the changes are checked), or ''. */
export function blcRefusal(s:State,b:Booking|undefined):string{
  if(!b)return 'Booking not found.';
  if(b.status!=='Departed'||!b.invoiceNo)return 'Only a departed, invoiced stay can be corrected. Use a correction while the guest is in house.';
  const pending=s.approvals.some(a=>a.booking===b.id&&a.status==='Pending'&&!folioApprovalExpired(s,a)&&['bill-correction','credit-note'].includes(folioKind(a)));
  if(pending)return 'A bill correction or credit note is already pending on this stay.';
  const dl=blcDeadline(blcInvoiceDate(s,b));
  if(s.date>dl)return `A credit note for invoice ${b.invoiceNo} could be issued only until ${dl} (s.34(2) CGST Act — CA to confirm).`;
  return '';
}
/** Print note of an invoice number: "Supersedes …" on a revision, "Superseded by … (credit note …)" on an earlier one. */
export function blcInvoiceNote(s:State,no:string):string{
  const cs=blcState(s).corrections.filter(c=>c.status==='Issued');
  const by=cs.find(c=>c.oldInvoiceNo===no),sup=cs.find(c=>c.newInvoiceNo===no);
  return [sup?`Supersedes ${sup.oldInvoiceNo}`:'',by?`Superseded by ${by.newInvoiceNo} (credit note ${by.creditNoteNo})`:''].filter(Boolean).join(' · ');
}
/** Which invoice a folio line belongs to (GSTR-1 documents): a line added by a correction → its new invoice; else the stay's first invoice. */
export function blcLineInvoice(s:State,b:Booking,l:Line):string|undefined{
  const c=blcState(s).corrections.find(c=>c.booking===b.id&&c.status==='Issued'&&l.id.startsWith(c.id+'/'));
  return c?c.newInvoiceNo:(b.invoiceRevisions?.[0]?.no??b.invoiceNo);
}
