// F7-R5 · rpt.tax.sac-summary — SAC summary (GSTR-1 Table 12; catalogue A2). Adapter over gstr1Summary / gstr1Table12 — no new figures.
// Legacy Lodge bills are not in this table (their GST was reported by the CA from the old software).
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {gstr1Summary,gstr1Table12,GSTR1_TABLE12_NOTE} from '../../gstr1';

const T=(key:string,label:string):RptColumn=>({key,label,type:'text',money:false});
const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});
export const SAC_COLS:RptColumn[]=[T('sac','SAC'),T('description','Description'),T('uqc','UQC'),{key:'quantity',label:'Total quantity',type:'int',money:false},M('totalValue','Total value'),
  M('taxable','Taxable value'),M('cgst','CGST'),M('sgst','SGST'),{key:'rate',label:'Rate',type:'pct',money:false}];

export const sacSummary=registerReport({
  id:'rpt.tax.sac-summary',title:'SAC summary (Table 12)',area:'tax',priority:'P1',status:'built',
  moneyFlag:true,moneyGate:'PERM-ACC-013',viewPerms:['PERM-ACC-013'],dateClass:'D-M',
  params:[{key:'month',label:'Month',type:'month',dateClass:'D-M'}],
  columns:SAC_COLS,
  dateBasis:'Documents by invoice date (a stay invoice is dated its departure or its correction); net of credit notes. Quantity = invoices carrying the SAC. Legacy Lodge bills are not in this table.',
  formats:['screen','xlsx','pdf','csv'],phone:'no',legacyIds:['A2'],
  select:(s,_uid,p)=>{
    const g=gstr1Summary(s,p.from!,p.to!);
    const rows:RptRow[]=gstr1Table12(g).map(r=>({kind:'row',cells:{sac:r.sac,description:r.description,uqc:r.uqc,quantity:r.quantity,totalValue:r.taxable+r.cgst+r.sgst+r.igst,
      taxable:r.taxable,cgst:r.cgst,sgst:r.sgst,rate:r.bps/100}}));
    const sum=(k:'taxable'|'cgst'|'sgst'|'totalValue')=>rows.reduce((a,x)=>a+(x.cells[k] as number),0);
    rows.push({kind:'total',cells:{sac:'Total',description:GSTR1_TABLE12_NOTE,quantity:null,totalValue:sum('totalValue'),taxable:sum('taxable'),cgst:sum('cgst'),sgst:sum('sgst')}});
    return {params:p,columns:SAC_COLS,rows};
  },
});
