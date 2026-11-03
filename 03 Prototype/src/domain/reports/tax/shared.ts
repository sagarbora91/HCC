// F7-R5 · helpers shared by the tax reports (pure; read-only over State). Every figure comes from gstr1Documents / gstr1Summary (the stored GST of each
// document, never recomputed). Server twin: Hcc.Reporting/Reports/Tax/TaxSql.cs.
import type {State,Booking} from '../../types';
import {gstr1Documents,GSTR1_SAC,type Gstr1Document,type Gstr1Note} from '../../gstr1';
import {blcState} from '../../streams/bill-correction';
import {getLegacyBills} from '../../../features/customers/legacy-source';

export const TAX_RATE_BPS=[0,500,1200,1800,2800] as const;
const lineGst=(l:{cgst:number;sgst:number})=>l.cgst+l.sgst;
export type TaxPay={cash:number;cardUpi:number;credit:number};
export type TaxEntry={no:string;kind:'invoice'|'note';series:'INV'|'RST'|'CRN';date:string;party:string;gstin:string;room:string;lines:Gstr1Document['lines'];against?:string;
  status:'issued'|'cancelled'|'corrected';pay:TaxPay;sign:1|-1};
const noPay=():TaxPay=>({cash:0,cardUpi:0,credit:0});
const isCash=(m?:string)=>String(m??'').trim().toLowerCase()==='cash';
/** Payment split of an invoice: the stay's receipts on its CURRENT invoice (folio), the settlement of a restaurant bill or a counter bill. Credit notes carry none. */
function payOf(s:State,d:Gstr1Document):TaxPay{
  const p=noPay();
  if(d.meta.source==='folio'){
    const b=s.bookings.find(x=>x.id===d.meta.booking);if(!b||b.invoiceNo!==d.no)return p;
    for(const r of b.receipts){
      if(r.kind==='payment'||r.kind==='deposit-apply'){(isCash(r.method)?(p.cash+=r.amount):(p.cardUpi+=r.amount));}
      else if(r.kind==='refund'){(isCash(r.method)?(p.cash-=r.amount):(p.cardUpi-=r.amount));}
      else if(r.kind==='company-credit'||r.kind==='ota-credit'||r.kind==='guest-credit')p.credit+=r.amount;
    }
  }else if(d.meta.source==='restaurant'){
    const o=s.posX?.orders.find(x=>x.settlement?.invoiceNo===d.no);const st=o?.settlement;
    if(st){(isCash(st.method)?(p.cash+=st.total):(p.cardUpi+=st.total));}
  }else{
    const cb=s.counterX?.bills.find(x=>x.no===d.no);
    if(cb){if(cb.settlement.mode==='paid'){(isCash(cb.settlement.method)?(p.cash+=cb.net+cb.tax):(p.cardUpi+=cb.net+cb.tax));}else p.credit+=cb.net+cb.tax;}
  }
  return p;
}
/** Every invoice and credit note dated from…to as one list (invoices then notes), with status and payment split. */
export function taxEntries(s:State,from:string,to:string):TaxEntry[]{
  const {docs,notes}=gstr1Documents(s,from,to);
  const corrected=new Set(blcState(s).corrections.filter(c=>c.status==='Issued').map(c=>c.oldInvoiceNo));
  const voidedCounter=new Set((s.counterX?.bills??[]).filter(b=>b.status==='Credited').map(b=>b.no));
  const inv:TaxEntry[]=docs.map(d=>({no:d.no,kind:'invoice',series:d.series,date:d.meta.date,party:d.meta.party,gstin:d.meta.gstin,room:d.meta.room,lines:d.lines,
    status:d.cancelled||voidedCounter.has(d.no)?'cancelled':corrected.has(d.no)?'corrected':'issued',pay:payOf(s,d),sign:1}));
  const crn:TaxEntry[]=notes.map((n:Gstr1Note)=>({no:n.no,kind:'note',series:'CRN',date:n.meta.date,party:n.meta.party,gstin:n.meta.gstin,room:n.meta.room,lines:n.lines,against:n.against,
    status:'issued',pay:noPay(),sign:-1}));
  return [...inv,...crn];
}
/** Taxable, GST and total of an entry as signed by its kind: every credit note's lines are the note's positive value (gstr1Documents), shown negative here. */
export function entryFigures(e:TaxEntry){
  const k=e.kind==='note'?-1:1;
  const lines=e.lines.map(l=>({...l,taxable:k*l.taxable,cgst:k*l.cgst,sgst:k*l.sgst}));
  const taxable=lines.reduce((a,l)=>a+l.taxable,0),gst=lines.reduce((a,l)=>a+lineGst(l),0);
  return {lines,taxable,gst,cgst:lines.reduce((a,l)=>a+l.cgst,0),sgst:lines.reduce((a,l)=>a+l.sgst,0),total:taxable+gst};
}
export const SERIES_ORDER:Record<string,number>={LGC:0,INV:1,RST:2,BOS:3,CRN:4};
export const isRoomSac=(sac:string)=>sac===GSTR1_SAC.room;
/** Legacy Lodge bills of a period (Table 13 'LGC' row); the prototype reads its fictional legacy sample. */
export const taxLegacyBills=getLegacyBills;
/** First and last day of a bound report period (D-M reports bind `month`; from / to are set by the engine). */
export const periodOf=(p:{from?:string;to?:string})=>({from:p.from!,to:p.to!});
export const guestOf=(b:Booking)=>b.company?.name||b.guest;
