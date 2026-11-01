// ACC-3 lane acc3-ageing · GST summary (rulebook §7.7, §4.12, §8.6; contract 02 Planning/agents/ACC3-REPORTS-CONTRACT.md §3).
// Output by rate and section from the existing GSTR-1 working summary (gstr1Summary), advances 11A / 11B from the advance-GST vouchers,
// ITC by head, RCM, the s.49 set-off, TCS credit and the 1340 balance from ACC-2's accGstMonth. Ties: `gstr1` = books output (net of the
// advance-GST lines) equals the GSTR-1 tax; `gstr3b` = the month's output read from the vouchers equals accGstMonth and the set-off adds up
// (credit used + cash per head = output; a posted set-off journal matches the computed one). Differences are listed in words.
// Output figures are GST (tax) paise; `outputTaxable` carries the taxable value of the same keys (optional addition, DONE file).
import type {State} from '../types';
import {gstr1Summary} from '../gstr1';
import {accControlOf} from './accounting-parties';
import {accRptVouchers} from './accounting-reports';
import {ACC_ADVANCE_GST_REVERSAL_SUB,ACC_ADVANCE_GST_SUB,ACC_GST_LEDGER,ACC_GST_SETOFF_SUB,accGstMonth,accGstSetoffEntries,accMonthRange,type ACCGstHeads,type ACCGstSetoff} from './accounting-gst';

export type ACCGstSummaryOutput={rate5:number;rate18:number;b2b:number;b2cs:number;advances11A:number;advances11B:number;creditNotes:number};
export type ACCGstSummary={month:string;output:ACCGstSummaryOutput;outputTaxable:ACCGstSummaryOutput;
  itc:{cgst:number;sgst:number;igst:number};rcm:number;setoff:ACCGstSetoff;cashPaid:number;tcsCredit:number;cashLedgerBalance:number;
  /** Book output by head (2130 / 2110 / 2120) for the month. */bookOutput:ACCGstHeads;
  ties:{gstr1:boolean;gstr3b:boolean;differences:string[]}};

const rs=(p:number)=>'₹'+(p/100).toFixed(2);
const tot=(h:ACCGstHeads)=>h.igst+h.cgst+h.sgst;

/** GST summary of `month` (YYYY-MM). */
export function accGstSummary(s:State,month:string):ACCGstSummary{
  const {from,to}=accMonthRange(month),g=accGstMonth(s,month),g1=gstr1Summary(s,from,to),L=ACC_GST_LEDGER;
  const out:ACCGstSummaryOutput={rate5:0,rate18:0,b2b:0,b2cs:0,advances11A:0,advances11B:0,creditNotes:0},tx:ACCGstSummaryOutput={...out};
  let g1Tax=0;
  for(const r of g1.rates){
    const note=r.section==='CDNR'||r.section==='CDNUR',sign=note?-1:1,tax=r.cgst+r.sgst+r.igst;
    g1Tax+=sign*tax;
    if(r.bps===500){out.rate5+=sign*tax;tx.rate5+=sign*r.taxable;}else if(r.bps===1800){out.rate18+=sign*tax;tx.rate18+=sign*r.taxable;}
    if(r.section==='B2B'){out.b2b+=tax;tx.b2b+=r.taxable;}else if(r.section==='B2CS'||r.section==='B2CL'){out.b2cs+=tax;tx.b2cs+=r.taxable;}
    else if(note){out.creditNotes+=tax;tx.creditNotes+=r.taxable;}
  }
  // the month's vouchers: advance GST (11A raised / 11B adjusted), challans paid into 1340, book output read independently
  const book:ACCGstHeads={igst:0,cgst:0,sgst:0};let cashPaid=0;
  for(const v of accRptVouchers(s,from,to).period){
    const setoff=v.source.kind==='tax'&&v.source.sub===ACC_GST_SETOFF_SUB;
    for(const e of v.entries){const a=accControlOf(e.account),cr=e.credit-e.debit;
      if(!setoff){if(a===L.outIgst)book.igst+=cr;else if(a===L.outCgst)book.cgst+=cr;else if(a===L.outSgst)book.sgst+=cr;}
      if(a===L.outCgst||a===L.outSgst||a===L.outIgst){if(v.source.sub===ACC_ADVANCE_GST_SUB)out.advances11A+=cr;else if(v.source.sub===ACC_ADVANCE_GST_REVERSAL_SUB)out.advances11B-=cr;}
      if(a===L.cash&&v.source.sub==='gst-payment')cashPaid+=e.debit;
    }
  }
  const differences:string[]=[];
  const bookNet=tot(book)-out.advances11A+out.advances11B;
  const gstr1=bookNet===g1Tax;
  if(!gstr1)differences.push(`Books output GST ${rs(bookNet)} (net of advances 11A ${rs(out.advances11A)} / 11B ${rs(out.advances11B)}) vs GSTR-1 working summary ${rs(g1Tax)}: differ by ${rs(bookNet-g1Tax)} — a sale booked without an invoice in the period (e.g. an Udhaar credit or a folio not yet invoiced) or an invoice dated in another month.`);
  let gstr3b=true;
  const heads:(keyof ACCGstHeads)[]=['igst','cgst','sgst'];
  for(const h of heads)if(book[h]!==g.output[h]){gstr3b=false;differences.push(`Output ${h.toUpperCase()} from the vouchers ${rs(book[h])} vs the GST month ${rs(g.output[h])}.`);}
  const x=g.setoff;
  for(const h of heads){const used=x.use.igst[h]+x.use.cgst[h]+x.use.sgst[h];
    if(used+x.cash[h]!==Math.max(0,g.output[h])){gstr3b=false;differences.push(`${h.toUpperCase()}: credit used ${rs(used)} + cash ${rs(x.cash[h])} ≠ output ${rs(g.output[h])}.`);}}
  if(g.posted){
    const want=accGstSetoffEntries(x).map(e=>`${e.account}:${e.debit}:${e.credit}`).sort().join('|');
    const pv=accRptVouchers(s,g.posted.date,g.posted.date).period.find(v=>v.source.id===g.posted!.id);
    const got=(pv?.entries??[]).map(e=>`${e.account}:${e.debit}:${e.credit}`).sort().join('|');
    if(pv&&got!==want){gstr3b=false;differences.push(`The posted set-off journal ${g.posted.id} differs from the month's computed set-off (books changed after it was posted).`);}
  }
  return {month,output:out,outputTaxable:tx,itc:{cgst:g.input.cgst,sgst:g.input.sgst,igst:g.input.igst},rcm:g.rcm,setoff:x,cashPaid,tcsCredit:g.tcs,
    cashLedgerBalance:g.cashBalance,bookOutput:book,ties:{gstr1,gstr3b,differences}};
}
