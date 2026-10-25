// Reports → Excel: one workbook per pack (all tabs), numbers as numbers, money as ₹ cells (ETP OpenXmlReportExporter.cs rule).
import {buildXlsx,XSheet,XCell} from '../../app/xlsx';
import type {ReportPack,ReportTab,ReportRatioValue} from '../../domain/streams/closing';

/** Ratio as a number with the same rounding as the screen (reportPct: 1 dp; reportRatioMoney: whole paise); '—' when the denominator is 0. */
const ratioCell=(r:ReportRatioValue|undefined,display:string):XCell=>!r||!r.den?display:r.kind==='pct'?{pct:Math.floor((r.num*1000+Math.floor(r.den/2))/r.den)/1000}:{paise:Math.round(r.num/r.den)};
const val=(unit:string,v:number):XCell=>unit==='money'?{paise:v}:{int:v};
export function reportSheets(meta:{hotel:string;scenario:string;clock:string},pack:ReportPack):XSheet[]{
  const sheets:XSheet[]=[{name:'About',widths:[30,60],rows:[['Item','Value'],['Report','Hotel Control Centre — DEMONSTRATION · fictional data · not a tax document'],['Property',meta.hotel],['Scenario',meta.scenario],
    ['From (business date)',{date:pack.from}],['To (business date)',{date:pack.to}],['Generated (simulated clock)',meta.clock],['Note','Synthetic 10% test tax; no GST compliance claim. Every total is the sum of its source rows.']]}];
  const drill:XCell[][]=[['Tab','Metric','Date','Reference','Booking','Guest','Room','Detail','Method','Amount / count']];
  for(const t of pack.tabs){
    sheets.push({name:t.label,widths:[34,18,12,40],rows:[['Metric','Value','Source rows','Note'],...t.metrics.map(m=>[m.label,val(m.unit,m.value),{int:m.rows.length},m.note??'']),
      ...t.ratios.map(r=>[r.label+' ('+r.formula+')',ratioCell(r.value,r.display),null,''])]});
    if(t.table)sheets.push(tableSheet(t));
    for(const m of t.metrics)for(const r of m.rows)drill.push([t.label,m.label,{date:r.date},r.ref,r.booking??'',r.guest??'',r.room??'',r.detail,r.method??'',val(m.unit,r.amount)]);
  }
  sheets.push({name:'Source rows',widths:[20,28,12,18,14,18,8,44,10,14],rows:drill});
  return sheets;
}
function tableSheet(t:ReportTab):XSheet{
  const tb=t.table!;
  const der=new Map((tb.derived??[]).map(x=>[x.col,x]));
  return {name:t.label+' by date',rows:[tb.columns,...tb.rows.map(r=>r.map((v,i):XCell=>{const d=der.get(i);if(d)return ratioCell({kind:d.kind,num:Number(r[d.num]),den:Number(r[d.den])},String(v));
    return tb.units[i]==='money'&&typeof v==='number'?{paise:v}:tb.units[i]==='count'&&typeof v==='number'?{int:v}:i===0&&/^\d{4}-\d{2}-\d{2}$/.test(String(v))?{date:String(v)}:v===''?'':v;}))]};
}
export const reportXlsx=(meta:{hotel:string;scenario:string;clock:string},pack:ReportPack)=>buildXlsx(reportSheets(meta,pack));
