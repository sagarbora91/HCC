// F7-R1 · PDF-or-print renderer (RPT-ARCHITECTURE §4.3): ReportResult → PrintModel → HTML for native.ts printPage / printablePage.
// The server renders the same model to PDF bytes (Hcc.Reporting MinimalPdfWriter). Text only from cellText() — the same text as the screen.
import type {RptResult,RptPrintModel,RptColumn,RptRow,RptCell} from './types';
import {money} from '../money';
import {rptTables} from './tables';

/** Rows per printed A4 page (portrait / landscape body). */
export const RPT_PRINT_ROWS={portrait:40,landscape:28};
/** The one text form of a cell: screen, print and CSV use it (Excel writes the number). */
export function cellText(col:Pick<RptColumn,'type'>,v:RptCell|undefined):string{
  if(v===null||v===undefined)return '';
  if(typeof v==='string')return v;
  switch(col.type){
    case 'money':return money(v);
    case 'pct':return v.toFixed(1)+'%';
    case 'int':return String(v);
    case 'bool':return v?'Yes':'No';
    default:return String(v);
  }
}
const label:Record<string,string>={from:'From',to:'To',month:'Month',fy:'Financial year',asOf:'As of'};
export function toPrintModel(r:RptResult,property:string):RptPrintModel{
  const tables=rptTables(r);const wide=Math.max(...tables.map(t=>t.columns.length))>8;
  const orientation=wide?'landscape':'portrait';const per=RPT_PRINT_ROWS[orientation];
  const pages:RptPrintModel['pages']=[];let cur:RptPrintModel['pages'][number]={tables:[]},used=0;
  for(const t of tables){
    let i=0;
    do{const room=per-used;if(room<=3&&cur.tables.length){pages.push(cur);cur={tables:[]};used=0;continue;}
      const chunk=t.rows.slice(i,i+Math.max(1,per-used-1));cur.tables.push({columns:t.columns,rows:chunk,...(t.title?{title:t.title}:{})});used+=chunk.length+2;i+=chunk.length;
      if(i<t.rows.length){pages.push(cur);cur={tables:[]};used=0;}}while(i<t.rows.length);
  }
  if(cur.tables.length||!pages.length)pages.push(cur);
  const params=Object.entries(r.params).filter(([k,v])=>v!==undefined&&k!=='legacy').map(([k,v])=>[label[k]??k,String(v)] as [string,string]);
  return {orientation,header:{property,title:r.title,params,runAt:r.about.runAt,user:r.about.user},pages,footer:{basis:r.about.dateBasis,provisional:r.about.provisional}};
}
const esc=(t:string)=>t.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
const num=(c:RptColumn)=>c.type==='money'||c.type==='int'||c.type==='pct';
function tableHtml(columns:RptColumn[],rows:RptRow[],title?:string){
  return `${title?`<h3>${esc(title)}</h3>`:''}<table><thead><tr>${columns.map(c=>`<th${num(c)?' class="n"':''}>${esc(c.label)}</th>`).join('')}</tr></thead><tbody>${
    rows.map(r=>`<tr${r.kind!=='row'?` class="${r.kind}"`:''}>${columns.map(c=>`<td${num(c)?' class="n"':''}>${esc(cellText(c,r.cells[c.key]))}</td>`).join('')}</tr>`).join('')}</tbody></table>`;
}
/** Body HTML + CSS for native.ts printablePage(title, body, css) → printPage(). One `.rpt-page` per PrintModel page. */
export function printHtml(m:RptPrintModel):{title:string;body:string;css:string}{
  const n=m.pages.length;
  const head=`<header><div class="p">${esc(m.header.property)}</div><h2>${esc(m.header.title)}</h2><div class="m">${m.header.params.map(([k,v])=>`${esc(k)}: ${esc(v)}`).join(' · ')}</div><div class="m">Run ${esc(m.header.runAt)} by ${esc(m.header.user)}${m.footer.provisional?' · PROVISIONAL (day not closed)':''}</div></header>`;
  const body=m.pages.map((p,i)=>`<section class="rpt-page">${i===0?head:''}${p.tables.map(t=>tableHtml(t.columns,t.rows,t.title)).join('')}<footer>${esc(m.footer.basis)} · Page ${i+1} of ${n}</footer></section>`).join('');
  const css=`@page{size:A4 ${m.orientation}}.rpt-page{break-after:page}.rpt-page:last-child{break-after:auto}table{border-collapse:collapse;width:100%;font-size:10pt}th,td{border-bottom:1px solid #999;padding:2px 4px;text-align:left}.n{text-align:right}tr.total td,tr.subtotal td{font-weight:bold}header .p{font-weight:bold}footer{font-size:9pt;margin-top:6px}`;
  return {title:m.header.title,body,css};
}
/** Plain text of a print model (golden harness): one line per row, cells tab-separated; page breaks as form feeds. */
export function printText(m:RptPrintModel):string{
  return m.pages.map((p,i)=>[`Page ${i+1} of ${m.pages.length}`,...p.tables.flatMap(t=>[t.columns.map(c=>c.label).join('\t'),...t.rows.map(r=>t.columns.map(c=>cellText(c,r.cells[c.key])).join('\t'))])].join('\n')).join('\n\f\n');
}
