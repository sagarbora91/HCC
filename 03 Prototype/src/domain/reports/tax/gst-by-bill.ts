// F7-R5 · rpt.tax.gst-by-bill — GST by bill (catalogue A18–A20). Each tax invoice with taxable value by rate and CGST / SGST, read from the stored GST of the
// document (gstr1Documents — never recomputed). Credit notes of the month are listed as negative rows so the totals tie to the GSTR-1 working summary.
import type {RptColumn,RptRow,RptBoundParams} from '../types';
import {registerReport} from '../catalogue';
import {taxEntries,entryFigures,SERIES_ORDER,TAX_RATE_BPS} from './shared';

const T=(key:string,label:string,mask?:RptColumn['mask']):RptColumn=>({key,label,type:'text',money:false,...(mask?{mask}:{})});
const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});
export const GST_BILL_COLS:RptColumn[]=[T('billNo','Bill no'),{key:'billDate',label:'Bill date',type:'date',money:false},T('party','Guest or company','guestName'),T('gstin','GSTIN'),T('room','Room'),
  M('net','Net amount'),M('tax0','Taxable at 0 %'),M('tax5','Taxable at 5 %'),M('tax12','Taxable at 12 %'),M('tax18','Taxable at 18 %'),M('tax28','Taxable at 28 %'),M('cgst','CGST'),M('sgst','SGST')];
const LINE_COLS:RptColumn[]=[GST_BILL_COLS[0]!,GST_BILL_COLS[1]!,GST_BILL_COLS[2]!,GST_BILL_COLS[3]!,GST_BILL_COLS[4]!,T('item','Item'),{key:'qty',label:'Qty',type:'int',money:false},
  {key:'rate',label:'Rate',type:'pct',money:false},...GST_BILL_COLS.slice(5)];
const rateKey=(bps:number)=>`tax${bps/100}`;

export const gstByBill=registerReport({
  id:'rpt.tax.gst-by-bill',title:'GST by bill',area:'tax',priority:'P1',status:'built',
  moneyFlag:true,moneyGate:'PERM-ACC-013',viewPerms:['PERM-ACC-013'],dateClass:'D-M',
  params:[{key:'month',label:'Month',type:'month',dateClass:'D-M'},{key:'level',label:'Level',type:'enum',options:['bill','line'],default:'bill'},
    {key:'zeroTax',label:'Zero-tax bills',type:'enum',options:['include','exclude'],default:'include'}],
  columns:GST_BILL_COLS,
  dateBasis:'Invoices by invoice date (a stay invoice is dated its departure or its correction), with the month\'s credit notes as negative rows. The bill\'s stored GST — never recomputed. Rates other than 0, 5, 12, 18 and 28 % count in Net amount and the tax columns only.',
  formats:['screen','xlsx','pdf','csv'],phone:'no',legacyIds:['A18','A19','A20'],
  select:(s,_uid,p:RptBoundParams)=>{
    const line=p.level==='line',cols=line?LINE_COLS:GST_BILL_COLS;
    const entries=taxEntries(s,p.from!,p.to!).map(e=>({e,f:entryFigures(e)}))
      .filter(x=>!(p.zeroTax==='exclude'&&x.f.gst===0&&x.f.lines.every(l=>l.bps===0)))
      .sort((a,b)=>a.e.date.localeCompare(b.e.date)||SERIES_ORDER[a.e.series]!-SERIES_ORDER[b.e.series]!||a.e.no.localeCompare(b.e.no));
    const rows:RptRow[]=[];
    const acc:Record<string,number>={net:0,cgst:0,sgst:0,...Object.fromEntries(TAX_RATE_BPS.map(b=>[rateKey(b),0]))};
    const rateCells=(ls:{taxable:number;bps:number}[])=>Object.fromEntries(TAX_RATE_BPS.map(b=>[rateKey(b),ls.filter(l=>l.bps===b).reduce((a,l)=>a+l.taxable,0)]));
    const head=(e:typeof entries[number]['e'])=>({billNo:e.no,billDate:e.date,party:e.party,gstin:e.gstin,room:e.room});
    for(const {e,f} of entries){
      const parts=line?f.lines.map(l=>({ls:[l],extra:{item:l.label??'',qty:l.qty??1,rate:l.bps/100}})):[{ls:f.lines,extra:{}}];
      for(const part of parts){
        const net=part.ls.reduce((a,l)=>a+l.taxable,0),cgst=part.ls.reduce((a,l)=>a+l.cgst,0),sgst=part.ls.reduce((a,l)=>a+l.sgst,0),rc=rateCells(part.ls);
        rows.push({kind:'row',cells:{...head(e),...part.extra,net,...rc,cgst,sgst}});
        acc.net+=net;acc.cgst+=cgst;acc.sgst+=sgst;for(const b of TAX_RATE_BPS)acc[rateKey(b)]+=rc[rateKey(b)] as number;
      }
    }
    rows.push({kind:'total',cells:{billNo:'Total',...acc}});
    return {params:p,columns:cols,rows};
  },
});
