// DEC-072 (audit A03a-01) · GSTR-1 working summary — a working aid for the CA, not a return file. Port of the server's Gstr1Summary
// (08 Production Hcc.Domain/Invoices/Gstr1Summary.cs) over the prototype's documents: folio sample invoices (stream C, allocated at checkout)
// with their credit notes, and restaurant tax invoices (RST series, POS) with the credit notes of voided bills. Rate-wise B2B / B2CS
// (unregistered, net of its credit notes) / CDNR (credit notes on B2B invoices); the SAC table (net of credit notes) and the documents
// table (one row per series with the cancelled count). Every supply is intra-state here (CGST + SGST), so B2CL / IGST never occur.
// Exported names start with 'gstr1' (shared mock namespace). Thresholds, SAC codes and sections are illustrative — CA to confirm.
import type {State,Booking,Line} from './types';
import {gstLineTax,gstSplit,gstCategoryOfLabel} from './gst';
import {posSlice,posBillSplit,POS_RST_SAC} from './streams/pos';

/** The prototype produces B2B / B2CS / CDNR; the server's summary (production build) can also carry B2CL / CDNUR with IGST. */
export type Gstr1Section='B2B'|'B2CL'|'B2CS'|'CDNR'|'CDNUR';
export type Gstr1Rate={section:Gstr1Section;bps:number;taxable:number;cgst:number;sgst:number;igst:number;documents:number};
export type Gstr1Sac={sac:string;bps:number;taxable:number;cgst:number;sgst:number;igst:number};
export type Gstr1Doc={docType:string;first:string;last:string;count:number;cancelled:number};
export type Gstr1Summary={from:string;to:string;rates:Gstr1Rate[];sac:Gstr1Sac[];documents:Gstr1Doc[];note:string};

export const GSTR1_NOTE='Working summary from issued documents — illustrative, CA to confirm before filing (thresholds, SAC codes, sections).';
/** SAC per supply category (the server's SacCodes): room 996311, restaurant 996331, laundry 999712, banquet 996334, other 999799. */
export const GSTR1_SAC:Record<string,string>={room:'996311',food:POS_RST_SAC,laundry:'999712',banquet:'996334',other:'999799'};
/** Table-13 label of each series code. */
export const GSTR1_SERIES_LABEL:Record<string,string>={INV:'Tax invoices · rooms and stays',RST:'Tax invoices · restaurant (RST)',BOS:'Bills of supply',CRN:'Credit notes'};

type DocLine={sac:string;bps:number;taxable:number;cgst:number;sgst:number};
type Doc={no:string;series:'INV'|'RST';supply:'B2C'|'B2B';lines:DocLine[];cancelled?:boolean};
type Note={no:string;supply:'B2C'|'B2B';lines:DocLine[]};

/** SAC of a folio line: room nights, discounts and adjustments are the room supply; service lines by their category (as the server). */
const sacOfLine=(l:Line)=>l.kind==='service'?GSTR1_SAC[gstCategoryOfLabel(l.label)]!:GSTR1_SAC.room!;
const docLine=(l:Line):DocLine=>{const tax=gstLineTax(l.amount,l.taxBps),x=gstSplit(tax);return {sac:sacOfLine(l),bps:l.taxBps,taxable:l.amount,cgst:x.cgst,sgst:x.sgst};};
/** Credit-note lines are posted on the folio as negative lines; the note credits their positive value. */
const creditLine=(l:Line):DocLine=>{const d=docLine(l);return {...d,taxable:-d.taxable,cgst:-d.cgst,sgst:-d.sgst};};
const isCreditLine=(b:Booking,l:Line)=>!!l.approvalId&&!!b.creditNotes?.some(c=>c.approvalId===l.approvalId);

/** The summary for the documents dated from…to (YYYY-MM-DD, inclusive). A folio sample invoice is dated its departure (it is allocated at checkout). */
export function gstr1Summary(s:State,from:string,to:string):Gstr1Summary{
  const within=(d?:string)=>!!d&&d>=from&&d<=to;
  const docs:Doc[]=[],notes:Note[]=[];
  for(const b of s.bookings){
    if(!b.invoiceNo)continue;
    const supply=b.company?.gstin?'B2B':'B2C';
    if(within(b.departure))docs.push({no:b.invoiceNo,series:'INV',supply,lines:b.lines.filter(l=>!isCreditLine(b,l)).map(docLine)});
    for(const c of b.creditNotes??[])if(within(c.date))notes.push({no:c.no,supply,lines:b.lines.filter(l=>l.approvalId===c.approvalId).map(creditLine)});
  }
  for(const o of posSlice(s).orders){
    const st=o.settlement;if(!st?.invoiceNo)continue;
    const x=posBillSplit(st),line:DocLine={sac:st.sac??POS_RST_SAC,bps:st.taxBps,taxable:st.net,cgst:x.cgst,sgst:x.sgst},supply=st.supply??'B2C';
    if(within(st.date))docs.push({no:st.invoiceNo,series:'RST',supply,lines:[line],cancelled:!!st.creditNoteNo});
    if(st.creditNoteNo&&within(st.creditNoteDate??st.date))notes.push({no:st.creditNoteNo,supply,lines:[line]});
  }
  const rates=new Map<string,Gstr1Rate&{docs:Set<string>}>(),sac=new Map<string,Gstr1Sac>();
  const add=(section:Gstr1Section,l:DocLine,sign:number,no:string)=>{
    const k=section+'|'+l.bps,r=rates.get(k)??{section,bps:l.bps,taxable:0,cgst:0,sgst:0,igst:0,documents:0,docs:new Set<string>()};
    r.taxable+=sign*l.taxable;r.cgst+=sign*l.cgst;r.sgst+=sign*l.sgst;r.docs.add(no);rates.set(k,r);};
  const addSac=(l:DocLine,sign:number)=>{const k=l.sac+'|'+l.bps,r=sac.get(k)??{sac:l.sac,bps:l.bps,taxable:0,cgst:0,sgst:0,igst:0};
    r.taxable+=sign*l.taxable;r.cgst+=sign*l.cgst;r.sgst+=sign*l.sgst;sac.set(k,r);};
  for(const d of docs)for(const l of d.lines){add(d.supply==='B2B'?'B2B':'B2CS',l,1,d.no);addSac(l,1);}
  // B2CS is reported net of its credit notes; credit notes on B2B invoices go to CDNR.
  for(const n of notes)for(const l of n.lines){if(n.supply==='B2B')add('CDNR',l,1,n.no);else add('B2CS',l,-1,n.no);addSac(l,-1);}
  const order:Gstr1Section[]=['B2B','B2CL','B2CS','CDNR','CDNUR'];
  const series=(code:string,nos:string[],cancelled:number):Gstr1Doc[]=>{if(!nos.length)return [];const xs=[...nos].sort();
    return [{docType:code,first:xs[0]!,last:xs[xs.length-1]!,count:xs.length,cancelled}];};
  const of=(k:'INV'|'RST')=>docs.filter(d=>d.series===k);
  return {from,to,
    rates:[...rates.values()].sort((a,b)=>order.indexOf(a.section)-order.indexOf(b.section)||a.bps-b.bps).map(({docs,...r})=>({...r,documents:docs.size})),
    sac:[...sac.values()].sort((a,b)=>a.sac<b.sac?-1:a.sac>b.sac?1:a.bps-b.bps),
    documents:[...series('INV',of('INV').map(d=>d.no),0),...series('RST',of('RST').map(d=>d.no),of('RST').filter(d=>d.cancelled).length),...series('CRN',notes.map(n=>n.no),0)],
    note:GSTR1_NOTE};
}

/** Months of the financial year of `date` up to that month (newest first): the GSTR-1 period picker. */
export function gstr1Months(date:string):{id:string;label:string;from:string;to:string}[]{
  const names=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
  const y=+date.slice(0,4),m=+date.slice(5,7),startY=m>=4?y:y-1,out:{id:string;label:string;from:string;to:string}[]=[];
  for(let i=0;i<12;i++){
    const mm=(3+i)%12+1,yy=mm>=4?startY:startY+1,id=`${yy}-${String(mm).padStart(2,'0')}`;
    if(id>date.slice(0,7))break;
    const last=new Date(Date.UTC(yy,mm,0)).getUTCDate();
    out.push({id,label:`${names[mm-1]} ${yy}`,from:id+'-01',to:`${id}-${String(last).padStart(2,'0')}`});
  }
  return out.reverse();
}
