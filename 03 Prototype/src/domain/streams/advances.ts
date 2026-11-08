// F8 QW-ADV · "Advances held" (Cashier step): one row per booking that holds an advance or a refundable deposit. Pure, read-only.
// Existing commands only: REFUND_REQUEST -> approval -> REFUND_EXECUTE (DEC-031), PAYMENT kind 'deposit-return'. No posting happens here.
import type {Approval,Booking,State} from '../types';
import {totals} from '../selectors';
import {can} from '../selectors';
import {bookingRefOf} from '../bookingRef';
import {folioRefundable,folioApprovalExpired} from './folio';
import {sfReceiptFolio,SF_PRIMARY,sfIsSplit,sfFolios,sfView} from './folio-split';

/** QWQ-5 default: an advance older than this many days is red (strictly older: day 90 is not red, day 91 is). */
export const ADV_RED_DAYS=90;
const RED_STATUSES=['Cancelled','No show','Departed'] as const;

export type AdvRefundable={receiptId:string;method:string;date:string;remaining:number};
export type AdvPending={id:string;amount:number;status:'Pending'|'Approved';requestedBy:string;target:string};
export type AdvRow={
  booking:string;ref:string;guest:string;status:string;arrival:string;departure:string;
  /** Payment money held beyond the posted bill (paise): max(0, paid - total); 0 when the bill is larger. */
  advance:number;
  /** Refundable deposit still held (paise). */
  deposit:number;
  /** Refunds already paid out against this booking's payments (paise). */
  refunded:number;
  held:number;
  /** Whole days from the oldest advance / deposit receipt to the business date. */
  ageDays:number;
  /** Folio letters that hold the money ('A' for deposits; payments carry their own folio on split stays). */
  folios:string;
  red:boolean;redReason:string;
  refundable:AdvRefundable[];
  pending:AdvPending[];
};
export const ADV_COLUMNS=['Booking','Guest','Status','Arrival','Departure','Advance','Deposit','Refunded','Held','Age (days)','Folio','Flag'] as const;

const daysBetween=(a:string,b:string)=>Math.round((Date.parse(b+'T12:00:00Z')-Date.parse(a+'T12:00:00Z'))/86400000);

/** Rows for the Advances held view, red rows first then oldest first. With a `uid`, only a role with the payments permission gets rows (the cashier gate). */
export function advHeld(s:State,uid?:string):AdvRow[]{
  if(uid!==undefined&&!can(s,uid,'payments'))return [];
  const rows:AdvRow[]=[];
  for(const b of s.bookings){
    const t=totals(b);
    // F8-INT (review M7, SF-CONTRACT §13 "per folio on split stays"): an overpaid folio holds an advance even while another folio still owes.
    const advance=sfIsSplit(b)?sfFolios(b).filter(f=>!f.removedAt).reduce((a,f)=>a+Math.max(0,-totals(sfView(b,f.id)).balance),0):Math.max(0,-t.balance),deposit=Math.max(0,t.deposit);
    if(advance<=0&&deposit<=0)continue;
    const refunded=b.receipts.filter(r=>r.kind==='refund').reduce((a,r)=>a+r.amount,0);
    const pays=b.receipts.filter(r=>r.kind==='payment'),deps=b.receipts.filter(r=>r.kind==='deposit');
    const dates=[...(advance>0?pays:[]),...(deposit>0?deps:[])].map(r=>r.date).sort();
    const ageDays=dates.length?Math.max(0,daysBetween(dates[0],s.date)):0;
    const letters=new Set<string>();
    if(advance>0)pays.forEach(r=>letters.add(sfReceiptFolio(b,r)));
    if(deposit>0)deps.forEach(r=>letters.add(sfReceiptFolio(b,r)));
    if(!letters.size)letters.add(SF_PRIMARY);
    const refundable=pays.map(r=>({receiptId:r.id,method:r.method,date:r.date,remaining:folioRefundable(s,b,r.id)})).filter(r=>r.remaining>0);
    const pending=s.approvals.filter(a=>a.booking===b.id&&a.kind==='refund'&&(a.status==='Pending'||a.status==='Approved')&&!folioApprovalExpired(s,a))
      .map((a:Approval)=>({id:a.id,amount:a.amount,status:a.status as 'Pending'|'Approved',requestedBy:a.actor,target:a.target??''}));
    let redReason='';
    if((RED_STATUSES as readonly string[]).includes(b.status))redReason=`${b.status} - money still held, a refund is owed`;
    else if(advance>0&&ageDays>ADV_RED_DAYS)redReason=`Advance older than ${ADV_RED_DAYS} days`;
    rows.push({booking:b.id,ref:bookingRefOf(s.bookings,b.id),guest:b.guest,status:b.status,arrival:b.arrival,departure:b.departure,
      advance,deposit,refunded,held:advance+deposit,ageDays,folios:[...letters].sort().join(', '),red:!!redReason,redReason,refundable,pending});
  }
  return rows.sort((a,b)=>Number(b.red)-Number(a.red)||b.ageDays-a.ageDays||a.ref.localeCompare(b.ref));
}
export const advTotals=(rows:readonly AdvRow[])=>({count:rows.length,held:rows.reduce((a,r)=>a+r.held,0),red:rows.filter(r=>r.red).length,advance:rows.reduce((a,r)=>a+r.advance,0),deposit:rows.reduce((a,r)=>a+r.deposit,0)});
/** CSV for saveCsv (formula-neutralised text; amounts in rupees with two decimals). */
export function advCsv(rows:readonly AdvRow[]):string{
  const q=(v:string|number)=>{let t=String(v);if(/^[=+\-@\t\r]/.test(t))t="'"+t;return /[",\n]/.test(t)?'"'+t.replace(/"/g,'""')+'"':t;};
  const rs=(p:number)=>(p/100).toFixed(2);
  return [ADV_COLUMNS.join(','),...rows.map(r=>[r.ref,r.guest,r.status,r.arrival,r.departure,rs(r.advance),rs(r.deposit),rs(r.refunded),rs(r.held),r.ageDays,r.folios,r.redReason].map(q).join(','))].join('\n');
}
