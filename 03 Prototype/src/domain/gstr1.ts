// DEC-072 (audit A03a-01) · GSTR-1 working summary — a working aid for the CA, not a return file. Port of the server's Gstr1Summary
// (08 Production Hcc.Domain/Invoices/Gstr1Summary.cs) over the prototype's documents: folio sample invoices (stream C, allocated at checkout)
// with their credit notes, and restaurant tax invoices (RST series, POS) with the credit notes of voided bills. Rate-wise B2B / B2CS
// (unregistered, net of its credit notes) / CDNR (credit notes on B2B invoices); the SAC table (net of credit notes) and the documents
// table (one row per series with the cancelled count). Every supply is intra-state here (CGST + SGST), so B2CL / IGST never occur.
// Exported names start with 'gstr1' (shared mock namespace). Thresholds, SAC codes and sections are illustrative — CA to confirm.
import type {State,Booking,Line} from './types';
import {gstLineTax,gstSplit,gstCategoryOfLabel} from './gst';
import {posSlice,posBillSplit,POS_RST_SAC} from './streams/pos';
import {blcLineInvoice,blcState} from './streams/bill-correction'; // LF-BILL (contract §5)

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
export const GSTR1_SERIES_LABEL:Record<string,string>={LGC:'Legacy Lodge bills (plain numbers, before cut-over)',INV:'Tax invoices · rooms and stays',RST:'Tax invoices · restaurant (RST)',BOS:'Bills of supply',CRN:'Credit notes'};

type DocLine={sac:string;bps:number;taxable:number;cgst:number;sgst:number};
type Doc={no:string;series:'INV'|'RST';supply:'B2C'|'B2B';lines:DocLine[];cancelled?:boolean};
type Note={no:string;supply:'B2C'|'B2B';lines:DocLine[]};

/** SAC of a folio line: room nights, discounts and adjustments are the room supply; service lines by their category (as the server). */
const sacOfLine=(l:Line)=>l.kind==='service'||l.kind==='food'?GSTR1_SAC[gstCategoryOfLabel(l.label)]!:GSTR1_SAC.room!; // ACC-2: a food line is a service line here (same SAC rule as the server)
const docLine=(l:Line):DocLine=>{const tax=gstLineTax(l.amount,l.taxBps),x=gstSplit(tax);return {sac:sacOfLine(l),bps:l.taxBps,taxable:l.amount,cgst:x.cgst,sgst:x.sgst};};
/** Credit-note lines are posted on the folio as negative lines; the note credits their positive value. */
const creditLine=(l:Line):DocLine=>{const d=docLine(l);return {...d,taxable:-d.taxable,cgst:-d.cgst,sgst:-d.sgst};};
const isCreditLine=(b:Booking,l:Line)=>!!l.approvalId&&!!b.creditNotes?.some(c=>c.approvalId===l.approvalId);

/** The summary for the documents dated from…to (YYYY-MM-DD, inclusive). A folio sample invoice is dated its departure (it is allocated at checkout). */
/** LF §5 (B7, DEC-197): a bill of the legacy Lodge series (plain numbers, before cut-over). Table 13 only — never Table 12 / rate tables. */
export type Gstr1LegacyBill={no:string;date:string;cancelled:boolean};
export type Gstr1LegacyBills=(from:string,to:string)=>Gstr1LegacyBill[];
export function gstr1Summary(s:State,from:string,to:string,legacyBills?:Gstr1LegacyBills):Gstr1Summary{
  const within=(d?:string)=>!!d&&d>=from&&d<=to;
  const docs:Doc[]=[],notes:Note[]=[];
  for(const b of s.bookings){
    if(!b.invoiceNo)continue;
    const supply=b.company?.gstin?'B2B':'B2C';
    // LF-BILL (§4/§5): a corrected stay has one document per revision — lines added by a correction belong to its new invoice (dated the
    // correction's decision), all other lines to the first invoice (dated the departure). The superseded invoice counts as issued.
    const byNo=new Map<string,Line[]>();
    for(const l of b.lines)if(!isCreditLine(b,l)){const no=blcLineInvoice(s,b,l)!;byNo.set(no,[...(byNo.get(no)??[]),l]);}
    if(!byNo.size)byNo.set(b.invoiceNo,[]);
    for(const [no,ls] of byNo){
      const c=blcState(s).corrections.find(c=>c.status==='Issued'&&c.newInvoiceNo===no);
      if(within(c?.decidedAt?.slice(0,10)??b.departure))docs.push({no,series:'INV',supply,lines:ls.map(docLine)});
    }
    for(const c of b.creditNotes??[])if(within(c.date))notes.push({no:c.no,supply,lines:b.lines.filter(l=>l.approvalId===c.approvalId).map(creditLine)});
  }
  for(const o of posSlice(s).orders){
    const st=o.settlement;if(!st?.invoiceNo)continue;
    const x=posBillSplit(st),line:DocLine={sac:st.sac??POS_RST_SAC,bps:st.taxBps,taxable:st.net,cgst:x.cgst,sgst:x.sgst},supply=st.supply??'B2C';
    if(within(st.date))docs.push({no:st.invoiceNo,series:'RST',supply,lines:[line],cancelled:!!st.creditNoteNo});
    if(st.creditNoteNo&&within(st.creditNoteDate??st.date))notes.push({no:st.creditNoteNo,supply,lines:[line]});
  }
  // LF-COUNTER (§6, A7): counter bills are tax invoices of the INV series; their credit notes (void) are CRN.
  for(const cb of s.counterX?.bills??[]){
    const supply=cb.party.gstin?'B2B':'B2C';
    const ls:DocLine[]=[...cb.lines.map(l=>({amount:l.amount,taxBps:l.taxBps,sac:l.sac})),...(cb.discount?.amount?[{amount:-cb.discount.amount,taxBps:cb.lines[0]?.taxBps??0,sac:cb.lines[0]?.sac??GSTR1_SAC.other!}]:[])]
      .map(l=>{const x=gstSplit(gstLineTax(l.amount,l.taxBps));return {sac:l.sac,bps:l.taxBps,taxable:l.amount,cgst:x.cgst,sgst:x.sgst};});
    if(within(cb.date))docs.push({no:cb.no,series:'INV',supply,lines:ls});
    if(cb.status==='Credited'&&cb.creditNoteNo){const d=s.approvals.find(a=>a.id===cb.voidApprovalId)?.decidedAt?.slice(0,10)??cb.date;if(within(d))notes.push({no:cb.creditNoteNo,supply,lines:ls});}
  }
  // LF-COUNTER end
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
  // LF-BILL (§5): one LGC row for the legacy series in the period (rows appear only when the period holds legacy bills).
  const legacy=(xs:Gstr1LegacyBill[]):Gstr1Doc[]=>{const ok=xs.filter(x=>x.date>=from&&x.date<=to);if(!ok.length)return [];
    const nos=ok.map(x=>x.no).sort((a,b)=>Number(a)-Number(b)||(a<b?-1:a>b?1:0));
    return [{docType:'LGC',first:nos[0]!,last:nos[nos.length-1]!,count:nos.length,cancelled:ok.filter(x=>x.cancelled).length}];};
  const of=(k:'INV'|'RST')=>docs.filter(d=>d.series===k);
  const sacDocs:Record<string,Set<string>>={};for(const d of docs)for(const l of d.lines)(sacDocs[l.sac]??=new Set()).add(d.no);
  const result:Gstr1Summary={from,to,
    rates:[...rates.values()].sort((a,b)=>order.indexOf(a.section)-order.indexOf(b.section)||a.bps-b.bps).map(({docs,...r})=>({...r,documents:docs.size})),
    sac:[...sac.values()].sort((a,b)=>a.sac<b.sac?-1:a.sac>b.sac?1:a.bps-b.bps),
    documents:[...(legacyBills?legacy(legacyBills(from,to)):[]),...series('INV',of('INV').map(d=>d.no),0),...series('RST',of('RST').map(d=>d.no),of('RST').filter(d=>d.cancelled).length),...series('CRN',notes.map(n=>n.no),0)],
    note:GSTR1_NOTE};
  SAC_DOCS.set(result,Object.fromEntries(Object.entries(sacDocs).map(([k,v])=>[k,v.size])));
  return result;
}
const SAC_DOCS=new WeakMap<Gstr1Summary,Record<string,number>>();

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

// ── LF-BILL (§5): Table 12 (HSN / SAC summary) rows and CSV for the GSTR-1 sub-screen tab. Legacy bills contribute nothing here. ──
export const GSTR1_SAC_LABEL:Record<string,string>={'996311':'Room or unit accommodation','996331':'Restaurant / food services','999712':'Laundry services','996334':'Banquet / event services','999799':'Other services'};
export const GSTR1_TABLE12_NOTE='Legacy Lodge bills are not in this table: their GST was reported by the CA from the old software.';
export type Gstr1Table12Row={sac:string;description:string;uqc:'OTH';quantity:number;bps:number;taxable:number;cgst:number;sgst:number;igst:number};
/** Table 12 rows from a summary; quantity = documents carrying that SAC (from `docsPerSac`, else 0). */
export function gstr1Table12(g:Gstr1Summary,docsPerSac:Record<string,number>=SAC_DOCS.get(g)??{}):Gstr1Table12Row[]{
  return g.sac.map(r=>({sac:r.sac,description:GSTR1_SAC_LABEL[r.sac]??'Services',uqc:'OTH',quantity:docsPerSac[r.sac]??0,bps:r.bps,taxable:r.taxable,cgst:r.cgst,sgst:r.sgst,igst:r.igst}));
}
const rupees=(p:number)=>(p/100).toFixed(2);
export function gstr1Table12Csv(rows:readonly Gstr1Table12Row[]):string{
  const head='SAC,Description,UQC,Quantity,Rate %,Taxable value,CGST,SGST,IGST';
  return [head,...rows.map(r=>[r.sac,'"'+r.description.replace(/"/g,'""')+'"',r.uqc,r.quantity,(r.bps/100).toFixed(2),rupees(r.taxable),rupees(r.cgst),rupees(r.sgst),rupees(r.igst)].join(','))].join('\r\n');
}
