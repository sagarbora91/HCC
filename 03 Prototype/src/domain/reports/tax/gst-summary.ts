// F7-R5 · rpt.tax.gst-summary — GST summary, ties to GSTR-1 and GSTR-3B (catalogue; ACC-3 wired onto the engine). Adapter over accGstSummary — the ACC-3
// figures are unchanged. Output tax by rate and section, advances 11A / 11B, ITC, RCM, the s.49 set-off, cash ledger; tie flags and the differences in words.
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {accGstSummary} from '../../streams/accounting-gst-summary';

export const GST_SUM_COLS:RptColumn[]=[{key:'line',label:'Line',type:'text',money:false},{key:'amount',label:'Amount',type:'money',money:true},
  {key:'tiesGstr1',label:'Ties to GSTR-1',type:'bool',money:false},{key:'tiesGstr3b',label:'Ties to GSTR-3B',type:'bool',money:false}];

export const gstSummary=registerReport({
  id:'rpt.tax.gst-summary',title:'GST summary (ties to GSTR-1 and GSTR-3B)',area:'tax',priority:'P1',status:'ACC-3',
  moneyFlag:true,moneyGate:'PERM-ACC-013',viewPerms:['PERM-ACC-013'],dateClass:'D-M',
  params:[{key:'month',label:'Month',type:'month',dateClass:'D-M'}],
  columns:GST_SUM_COLS,
  dateBasis:'Output tax by invoice date from the GSTR-1 working summary; advances, ITC, RCM, set-off and the cash ledger from the books for the month. Amounts are GST (tax) paise.',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{
    const g=accGstSummary(s,p.month as string);
    const r1=g.ties.gstr1,r3=g.ties.gstr3b,F=(b:boolean|null)=>b===null?null:b?1:0; // bool cells are 1 / 0
    const L=(line:string,amount:number|null,t1:boolean|null=null,t3:boolean|null=null):RptRow=>({kind:'row',cells:{line,amount,tiesGstr1:F(t1),tiesGstr3b:F(t3)}});
    const sec=(line:string):RptRow=>({kind:'subtotal',cells:{line,amount:null,tiesGstr1:null,tiesGstr3b:null}});
    const rows:RptRow[]=[
      sec('Output GST by rate'),L('Rate 5 %',g.output.rate5,r1),L('Rate 18 %',g.output.rate18,r1),
      sec('Output GST by section'),L('B2B',g.output.b2b,r1),L('B2CS',g.output.b2cs,r1),L('Credit notes (CDNR)',g.output.creditNotes,r1),
      sec('Advances'),L('Advance GST raised (11A)',g.output.advances11A),L('Advance GST adjusted (11B)',g.output.advances11B),
      sec('Input tax credit'),L('ITC CGST',g.itc.cgst,null,r3),L('ITC SGST',g.itc.sgst,null,r3),L('ITC IGST',g.itc.igst,null,r3),L('Reverse charge (RCM)',g.rcm,null,r3),
      sec('Book output (vouchers)'),L('CGST',g.bookOutput.cgst,r1,r3),L('SGST',g.bookOutput.sgst,r1,r3),L('IGST',g.bookOutput.igst,r1,r3),
      sec('Set-off (s.49)'),L('Output CGST',g.setoff.output.cgst,null,r3),L('Output SGST',g.setoff.output.sgst,null,r3),L('Output IGST',g.setoff.output.igst,null,r3),
      L('Payable in cash',g.setoff.payable,null,r3),L('Challan to deposit',g.setoff.challan,null,r3),
      sec('Cash ledger'),L('Cash paid in the month',g.cashPaid),L('TCS credit',g.tcsCredit),L('Cash ledger balance',g.cashLedgerBalance),
      ...g.ties.differences.map(d=>L('Difference: '+d,null)),
      {kind:'total',cells:{line:r1&&r3?'Ties to GSTR-1 and GSTR-3B':'Does not tie — see the differences',amount:null,tiesGstr1:F(r1),tiesGstr3b:F(r3)}},
    ];
    return {params:p,columns:GST_SUM_COLS,rows};
  },
});
