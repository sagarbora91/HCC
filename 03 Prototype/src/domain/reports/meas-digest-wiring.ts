// F12 MEAS · f12-int (MEAS-DIGEST item 5, review M2): every measurement report with an exception rule feeds the Owner digest block "Measurement".
// Generic provider, same rule as the server's MeasReportDigestSource: run the item's report for the digest's business date as the Owner and keep
// the rows that are red (`group:'red'`) or carry a truthy `exception` cell (people pack). Study items are not evaluated. Providers registered by
// the digest lane itself (regrets, high balance) are kept. Pure: runReport never writes state. A report that throws is left out of the block.
import type {State} from '../types';
import type {RptRow} from './types';
import {runReport,reportDef} from './engine';
import {MEAS_ITEMS} from '../streams/measure';
import {registerMeasRedRows,measRedRowKeys,type MeasRedRow} from '../streams/measure-digest';

/** At most this many lines per item; the rest is "… and N more". */
export const MEAS_DIGEST_PER_ITEM=5;
const fyOf=(d:string)=>{const y=+d.slice(0,4),m=+d.slice(5,7),a=m>=4?y:y-1;return `${a}-${String((a+1)%100).padStart(2,'0')}`;};
const isRed=(r:RptRow)=>{const x=r.cells.exception as unknown;return r.kind==='row'&&(r.group==='red'||(x!==undefined&&x!==null&&x!==0&&x!==false&&x!==''&&x!=='No'));};

export function measReportRedRows(s:State,date:string,key:string,reportId:string):MeasRedRow[]{
  const owner=s.users.find(u=>u.role==='Owner');if(!owner||!reportDef(reportId))return [];
  const def=reportDef(reportId)!,raw:Record<string,unknown>={};
  for(const p of def.params??[]){
    const t=String(p.type);
    if(t==='month')raw[p.key]=date.slice(0,7);else if(t==='fy')raw[p.key]=fyOf(date);else if(t==='date')raw[p.key]=date;
  }
  let rows:RptRow[]=[];
  try{rows=runReport(s,owner.id,reportId,raw,{all:true}).rows.filter(isRed);}
  catch{return [];}
  const text=def.columns.filter(c=>!c.money).slice(0,3);
  const line=(r:RptRow)=>text.map(c=>r.cells[c.key]).filter(v=>v!==null&&v!==undefined&&v!=='').map(String).join(' · ')||def.title;
  const out:MeasRedRow[]=rows.slice(0,MEAS_DIGEST_PER_ITEM).map(r=>({key,reportId,severity:'red' as const,text:line(r)}));
  if(rows.length>MEAS_DIGEST_PER_ITEM)out.push({key,reportId,severity:'red',text:`… and ${rows.length-MEAS_DIGEST_PER_ITEM} more`});
  return out;
}

const have=new Set(measRedRowKeys());
for(const it of MEAS_ITEMS){
  if(it.pack==='study'||!it.report||have.has(it.key))continue;
  const id=it.report;
  registerMeasRedRows(it.key,(s,date)=>measReportRedRows(s,date,it.key,id));
}
