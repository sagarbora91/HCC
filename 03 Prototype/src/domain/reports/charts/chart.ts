// F12 MEAS · meas-study-pack · ChartBlock (MEAS-CONTRACT §9.4, brief Phase B2 item 1).
// A chart is ONE result block: a section whose first column key is `chart.<kind>` (the category labels) and whose other numeric columns are the
// series (money in paise, counts, percent with one decimal). Because it is an ordinary section it passes the engine untouched: money removal
// (rule M / DEC-061) drops a money series with its column, masks never apply (labels are periods / categories, never names), and the table
// itself prints in every format — the chart is drawn FROM those same cells, so screen = Excel = PDF numbers by construction.
// Renderers: screen = chartSvg (inline SVG, colour tokens), print / PDF = chartPrintSvg through the print.ts hook, Excel = native OpenXML chart
// parts (chartXlsxExt through the xlsx.ts `ext` hook). Server twin: 08 Production/src/Hcc.Reporting/Charts/ChartBlock.cs (same encoding).
import type {RptColumn,RptRow,RptPrintTable} from '../types';
import type {XSheet,XSheetExt} from '../../../app/xlsx';
import {colRef} from '../../../app/xlsx';
import {RPT_RENDER_HOOKS} from '../print';
import {money} from '../../money';

export type ChartKind='bar'|'line'|'stacked'|'heat';
export const CHART_KINDS:ChartKind[]=['bar','line','stacked','heat'];
export type ChartSeriesType='money'|'int'|'pct';
export type ChartSeries={key:string;label:string;type:ChartSeriesType;values:(number|null)[]};
/** What a selector hands over (study-a / study-b `series` block): kind, labels, values in paise or counts. `legacy[i]` marks a legacy point. */
export type ChartBlock={id:string;title:string;kind:ChartKind;labelTitle?:string;labels:string[];series:ChartSeries[];legacy?:boolean[]};
export type ChartSection={key:string;title?:string;columns:RptColumn[];rows:RptRow[]};

export const chartLabelKey=(k:ChartKind)=>`chart.${k}`;
/** The block → an engine section (key `chart:<id>`). Legacy points carry `group:'legacy'` (rule 1.10 marker). */
export function chartSection(c:ChartBlock):ChartSection{
  if(!CHART_KINDS.includes(c.kind))throw new Error(`ChartBlock ${c.id}: unknown kind ${c.kind}`);
  for(const s of c.series)if(s.values.length!==c.labels.length)throw new Error(`ChartBlock ${c.id}: series ${s.key} has ${s.values.length} values for ${c.labels.length} labels`);
  const lk=chartLabelKey(c.kind);
  const columns:RptColumn[]=[{key:lk,label:c.labelTitle??'Period',type:'text',money:false},...c.series.map(s=>({key:s.key,label:s.label,type:s.type,money:s.type==='money'}))];
  const rows:RptRow[]=c.labels.map((l,i)=>({kind:'row',cells:{[lk]:l,...Object.fromEntries(c.series.map(s=>[s.key,s.values[i]??null]))},...(c.legacy?.[i]?{group:'legacy'}:{})}));
  return {key:'chart:'+c.id,title:c.title,columns,rows};
}
/** Decodes a table (section, print table) back into its chart, or null when the table is not a chart. Only `row` rows are points. */
export function chartOf(t:{columns:RptColumn[];rows:RptRow[];title?:string;key?:string}):ChartBlock|null{
  const lc=t.columns[0];if(!lc||!lc.key.startsWith('chart.'))return null;
  const kind=lc.key.slice(6) as ChartKind;if(!CHART_KINDS.includes(kind))return null;
  const pts=t.rows.filter(r=>r.kind==='row');
  const series=t.columns.slice(1).filter(c=>c.type==='money'||c.type==='int'||c.type==='pct').map(c=>({key:c.key,label:c.label,type:c.type as ChartSeriesType,
    values:pts.map(r=>{const v=r.cells[c.key];return typeof v==='number'?v:null;})}));
  return {id:(t.key??'').replace(/^chart:/,'')||kind,title:t.title??'',kind,labelTitle:lc.label,labels:pts.map(r=>String(r.cells[lc.key]??'')),series,legacy:pts.map(r=>r.group==='legacy')};
}
export const isChart=(t:{columns:RptColumn[]})=>!!t.columns[0]?.key.startsWith('chart.');

/** The value a renderer draws: the number in the cell's unit (paise → rupees, percent stays percent). Excel caches the same value (pct as fraction). */
export const chartValue=(type:ChartSeriesType,v:number|null)=>v===null?null:type==='money'?v/100:v;
const textOf=(type:ChartSeriesType,v:number|null)=>v===null?'':type==='money'?money(v):type==='pct'?v.toFixed(1)+'%':String(v);
const esc=(t:string)=>t.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
const r2=(n:number)=>Math.round(n*100)/100;

/** Bar / line / stacked geometry shared by the screen SVG, the print SVG and the parity tests. Heights are value / max × plot height. */
export function chartGeometry(c:ChartBlock,w=640,h=240){
  const pad={l:56,r:12,t:12,b:40};const pw=w-pad.l-pad.r,ph=h-pad.t-pad.b;const n=Math.max(1,c.labels.length);
  const vals=c.series.map(s=>s.values.map(v=>chartValue(s.type,v)??0));
  const max=c.kind==='stacked'?Math.max(0,...c.labels.map((_,i)=>vals.reduce((a,v)=>a+Math.max(0,v[i]),0))):Math.max(0,...vals.flat());
  const scale=max>0?ph/max:0;const band=pw/n;
  const bars:{series:string;index:number;value:number;x:number;y:number;w:number;h:number}[]=[];
  const lines:{series:string;points:{index:number;value:number;x:number;y:number}[]}[]=[];
  if(c.kind==='line')c.series.forEach((s,si)=>lines.push({series:s.key,points:vals[si].map((v,i)=>({index:i,value:v,x:r2(pad.l+band*(i+0.5)),y:r2(pad.t+ph-v*scale)}))}));
  else if(c.kind==='stacked')c.labels.forEach((_,i)=>{let base=0;c.series.forEach((s,si)=>{const v=Math.max(0,vals[si][i]);const bh=v*scale;
    bars.push({series:s.key,index:i,value:vals[si][i],x:r2(pad.l+band*i+band*0.15),y:r2(pad.t+ph-base-bh),w:r2(band*0.7),h:r2(bh)});base+=bh;});});
  else if(c.kind==='bar'){const k=Math.max(1,c.series.length),bw=band*0.8/k;
    c.series.forEach((s,si)=>vals[si].forEach((v,i)=>{const bh=Math.max(0,v)*scale;bars.push({series:s.key,index:i,value:v,x:r2(pad.l+band*i+band*0.1+bw*si),y:r2(pad.t+ph-bh),w:r2(bw),h:r2(bh)});}));}
  return {w,h,pad,max,scale,band,bars,lines};
}
/** Heat map = shaded table: share 0…1 of the cell within the whole grid (min → 0, max → 1). */
export function heatShare(c:ChartBlock){
  const all=c.series.flatMap(s=>s.values.filter((v):v is number=>v!==null));const lo=Math.min(...all),hi=Math.max(...all);
  return c.series.map(s=>s.values.map(v=>v===null||!all.length?null:hi===lo?1:(v-lo)/(hi-lo)));
}
// Colours: screen uses the theme tokens (CP-3/CP-5); print is always light (CLAUDE.md) so it uses the Maroon & Sand values directly.
const SCREEN=['var(--primary)','var(--ink-muted, var(--muted))','var(--success-fill, var(--primary))','var(--line)'];
const PRINT=['#7A1F2B','#6B5547','#B08D57','#2B1D16'];
function svg(c:ChartBlock,pal:string[],ink:string,line:string){
  if(c.kind==='heat'){
    const sh=heatShare(c);const cell=(si:number,i:number)=>sh[si][i];
    return `<table class="rpt-heat" data-chart="${esc(c.id)}"><thead><tr><th>${esc(c.labelTitle??'')}</th>${c.series.map(s=>`<th>${esc(s.label)}</th>`).join('')}</tr></thead><tbody>${
      c.labels.map((l,i)=>`<tr><th>${esc(l)}</th>${c.series.map((s,si)=>{const a=cell(si,i);return `<td data-value="${s.values[i]??''}" style="background:${a===null?'transparent':`color-mix(in srgb, ${pal[0]} ${Math.round(a*70)}%, transparent)`}">${esc(textOf(s.type,s.values[i]))}</td>`;}).join('')}</tr>`).join('')}</tbody></table>`;
  }
  const g=chartGeometry(c);const base=g.pad.t+(g.h-g.pad.t-g.pad.b);
  const ser=(k:string)=>c.series.findIndex(s=>s.key===k);
  const parts:string[]=[`<line x1="${g.pad.l}" y1="${base}" x2="${g.w-g.pad.r}" y2="${base}" stroke="${line}"/>`];
  for(const b of g.bars){const s=c.series[ser(b.series)];parts.push(`<rect x="${b.x}" y="${b.y}" width="${b.w}" height="${b.h}" fill="${pal[ser(b.series)%pal.length]}" data-series="${esc(b.series)}" data-value="${s.values[b.index]??''}"><title>${esc(c.labels[b.index])} · ${esc(s.label)}: ${esc(textOf(s.type,s.values[b.index]))}</title></rect>`);}
  for(const l of g.lines){const si=ser(l.series),s=c.series[si];parts.push(`<polyline fill="none" stroke="${pal[si%pal.length]}" stroke-width="2" points="${l.points.map(p=>`${p.x},${p.y}`).join(' ')}"/>`);
    for(const p of l.points)parts.push(`<circle cx="${p.x}" cy="${p.y}" r="3" fill="${pal[si%pal.length]}" data-series="${esc(l.series)}" data-value="${s.values[p.index]??''}"><title>${esc(c.labels[p.index])} · ${esc(s.label)}: ${esc(textOf(s.type,s.values[p.index]))}</title></circle>`);}
  const every=Math.max(1,Math.ceil(c.labels.length/12));
  c.labels.forEach((l,i)=>{if(i%every===0)parts.push(`<text x="${r2(g.pad.l+g.band*(i+0.5))}" y="${base+14}" font-size="10" text-anchor="middle" fill="${ink}">${esc(l)}</text>`);});
  const top=c.series[0]?textOf(c.series[0].type,c.series[0].type==='money'?Math.round(g.max*100):g.max):'';
  parts.push(`<text x="${g.pad.l-4}" y="${g.pad.t+8}" font-size="10" text-anchor="end" fill="${ink}">${esc(top)}</text>`);
  c.series.forEach((s,si)=>parts.push(`<rect x="${g.pad.l+si*150}" y="${g.h-14}" width="10" height="10" fill="${pal[si%pal.length]}"/><text x="${g.pad.l+si*150+14}" y="${g.h-5}" font-size="10" fill="${ink}">${esc(s.label)}</text>`));
  return `<svg class="rpt-chart" data-chart="${esc(c.id)}" data-kind="${c.kind}" viewBox="0 0 ${g.w} ${g.h}" width="100%" role="img" aria-label="${esc(c.title||c.id)}" xmlns="http://www.w3.org/2000/svg">${parts.join('')}</svg>`;
}
/** Screen: inline SVG (theme tokens). The report view puts it above the chart's table (see DONE, integration). */
export const chartSvg=(c:ChartBlock)=>svg(c,SCREEN,'var(--ink)','var(--line)');
/** Print / PDF (always light). */
export const chartPrintSvg=(c:ChartBlock)=>svg(c,PRINT,'#2B1D16','#999999');

// ── Excel: native OpenXML chart (DrawingML) fed from the table cells of the same sheet ──
const C='http://schemas.openxmlformats.org/drawingml/2006/chart',A='http://schemas.openxmlformats.org/drawingml/2006/main',R='http://schemas.openxmlformats.org/officeDocument/2006/relationships';
const XDR='http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing';
const xq=(n:string)=>`'${n.replace(/'/g,"''")}'`;
/** The chart XML for a sheet whose row 1 is the header and rows 2… the points (the layout toXlsx writes). Values cached as Excel holds them. */
export function chartXml(c:ChartBlock,sheet:string):string{
  const n=c.labels.length,last=n+1;
  const cat=`<c:cat><c:strRef><c:f>${esc(xq(sheet))}!$A$2:$A$${last}</c:f><c:strCache><c:ptCount val="${n}"/>${c.labels.map((l,i)=>`<c:pt idx="${i}"><c:v>${esc(l)}</c:v></c:pt>`).join('')}</c:strCache></c:strRef></c:cat>`;
  const ser=c.series.map((s,si)=>{const col=colRef(si+1),fmt=s.type==='money'?'#,##0.00':s.type==='pct'?'0.0%':'0';
    const cached=s.values.map((v,i)=>v===null?'':`<c:pt idx="${i}"><c:v>${s.type==='pct'?+(v/100).toPrecision(15):chartValue(s.type,v)}</c:v></c:pt>`).join('');
    return `<c:ser><c:idx val="${si}"/><c:order val="${si}"/><c:tx><c:strRef><c:f>${esc(xq(sheet))}!$${col}$1</c:f><c:strCache><c:ptCount val="1"/><c:pt idx="0"><c:v>${esc(s.label)}</c:v></c:pt></c:strCache></c:strRef></c:tx>${
      c.kind==='line'?'<c:marker><c:symbol val="circle"/></c:marker>':''}${cat}<c:val><c:numRef><c:f>${esc(xq(sheet))}!$${col}$2:$${col}$${last}</c:f><c:numCache><c:formatCode>${fmt}</c:formatCode><c:ptCount val="${n}"/>${cached}</c:numCache></c:numRef></c:val>${c.kind==='line'?'<c:smooth val="0"/>':''}</c:ser>`;}).join('');
  const axes='<c:axId val="500001"/><c:axId val="500002"/>';
  const plot=c.kind==='line'?`<c:lineChart><c:grouping val="standard"/><c:varyColors val="0"/>${ser}<c:marker val="1"/>${axes}</c:lineChart>`
    :`<c:barChart><c:barDir val="col"/><c:grouping val="${c.kind==='stacked'?'stacked':'clustered'}"/><c:varyColors val="0"/>${ser}<c:gapWidth val="80"/>${c.kind==='stacked'?'<c:overlap val="100"/>':''}${axes}</c:barChart>`;
  return `<?xml version="1.0" encoding="UTF-8" standalone="yes"?><c:chartSpace xmlns:c="${C}" xmlns:a="${A}" xmlns:r="${R}"><c:roundedCorners val="0"/><c:chart><c:title><c:tx><c:rich><a:bodyPr/><a:p><a:r><a:t>${esc(c.title||c.id)}</a:t></a:r></a:p></c:rich></c:tx><c:overlay val="0"/></c:title><c:autoTitleDeleted val="0"/><c:plotArea><c:layout/>${plot}<c:catAx><c:axId val="500001"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:delete val="0"/><c:axPos val="b"/><c:crossAx val="500002"/></c:catAx><c:valAx><c:axId val="500002"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:delete val="0"/><c:axPos val="l"/><c:majorGridlines/><c:crossAx val="500001"/></c:valAx></c:plotArea><c:legend><c:legendPos val="b"/></c:legend><c:plotVisOnly val="1"/></c:chart></c:chartSpace>`;
}
/** xlsx.ts `ext` for a chart sheet: drawing + chart parts and the sheet's `<drawing>` element. Heat maps have no Excel chart (the table is the map). */
export function chartXlsxExt(c:ChartBlock):XSheet['ext']{
  if(c.kind==='heat')return undefined;
  return (index:number,name:string):XSheetExt=>{
    const cols=c.series.length+1,rows=c.labels.length+1;
    const drawing=`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><xdr:wsDr xmlns:xdr="${XDR}" xmlns:a="${A}"><xdr:twoCellAnchor><xdr:from><xdr:col>${cols+1}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>1</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from><xdr:to><xdr:col>${cols+10}</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>${Math.max(rows,18)}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to><xdr:graphicFrame macro=""><xdr:nvGraphicFramePr><xdr:cNvPr id="2" name="${esc(c.title||c.id)}"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr><xdr:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/></xdr:xfrm><a:graphic><a:graphicData uri="${C}"><c:chart xmlns:c="${C}" xmlns:r="${R}" r:id="rId1"/></a:graphicData></a:graphic></xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor></xdr:wsDr>`;
    return {tail:`<drawing xmlns:r="${R}" r:id="rIdChart1"/>`,rels:[{id:'rIdChart1',type:R+'/drawing',target:`../drawings/drawing${index}.xml`}],
      files:[{name:`xl/drawings/drawing${index}.xml`,data:drawing,contentType:'application/vnd.openxmlformats-officedocument.drawing+xml'},
        {name:`xl/drawings/_rels/drawing${index}.xml.rels`,data:`<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="${R}/chart" Target="../charts/chart${index}.xml"/></Relationships>`},
        {name:`xl/charts/chart${index}.xml`,data:chartXml(c,name),contentType:'application/vnd.openxmlformats-officedocument.drawingml.chart+xml'}]};
  };
}

// ── Hooks (print.ts RPT_RENDER_HOOKS): installed once when this module loads ──
let installed=false;
export function installChartHooks(){
  if(installed)return;installed=true;
  RPT_RENDER_HOOKS.print.push((t:RptPrintTable)=>{const c=chartOf(t);return c&&c.kind!=='heat'?`<figure class="rpt-chart">${chartPrintSvg(c)}</figure>`:'';});
  RPT_RENDER_HOOKS.xlsx.push((t,sheet)=>{const c=chartOf(t);const ext=c?chartXlsxExt(c):undefined;return ext?{...sheet,ext}:sheet;});
}
installChartHooks();
