// F7-R7 · document layouts (layout:'document', RPT-ARCHITECTURE §4.3 / F7-CONTRACT §4 batch 3): one DocModel per document, built once from State by a
// pure builder; screen, print (Lodge look + HCC letterhead, A16) and PDF all read that model, so the figures are identical by construction.
// docResult() turns a model into an engine RptResult (sections) so Excel / CSV / the PDF-or-print text come from the same renderers as every report.
// Server twin: Hcc.Reporting/Documents/DocumentModel.cs (same labels, same order, same CSS).
import type {State} from '../../types';
import type {RptResult,RptColumn,RptRow,RptCell} from '../types';
import {HccError} from '../../errors';
import {rptHolds,RPT_NO_MONEY_ROLES} from '../mask';
import {can} from '../../selectors';
import type {Permission} from '../../types';
import {LODGE_CSS,LODGE_LETTERHEAD,type LodgeLayout} from '../guests/layouts';
import {cellText} from '../print';

export type DocTable={key:string;title?:string;columns:RptColumn[];rows:RptRow[]};
export type DocModel={
  /** `doc.<slug>` (never renamed); payslip keeps its catalogue id `rpt.people.payslip`. */ id:string;
  title:string;/** Document number shown in the head (invoice no, receipt no, KOT no …). */ number:string;
  /** Property name under the HCC letterhead. */ property:string;/** Label / value facts in print order (the boxed grid). */ pairs:[string,string][];
  tables:DocTable[];notes:string[];/** Signature captions at the foot. */ sign:string[];
  /** The document carries hotel money (every amount is shown only to a caller holding the money right). */ money:boolean;
  /** Date basis text for the About sheet / print footer. */ basis:string;
  /** Pre-built Lodge layout (R3 registration card / day end): printed verbatim. */ lodge?:LodgeLayout;
  /** Legal documents say so (invoice, credit note); the QR text is a plain string, never an IRN. */ qrText?:string};

export const T=(key:string,label:string,type:RptColumn['type']='text'):RptColumn=>({key,label,type,money:type==='money'});
export const R=(cells:Record<string,RptCell>,kind:RptRow['kind']='row'):RptRow=>({kind,cells});
/** Money documents never reach the roles that must not see hotel money (DEC-061), whatever else they hold. */
export function docMoneyRole(s:State,uid:string):boolean{const u=s.users.find(x=>x.id===uid);return !!u&&!RPT_NO_MONEY_ROLES.includes(u.role);}
export function docRequire(s:State,uid:string,ok:boolean,what:string):void{
  if(!ok||!docMoneyRole(s,uid))throw new HccError('FORBIDDEN',`You cannot print the ${what}.`);
}
export const canAny=(s:State,uid:string,p:Permission[])=>docMoneyRole(s,uid)&&can(s,uid,p);
export const holds=rptHolds;
export const userName=(s:State,uid:string)=>s.users.find(u=>u.id===uid)?.name.split(' · ')[0]??uid;

const esc=(t:unknown)=>String(t??'').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
export const DOC_CSS=LODGE_CSS+'.lg-tab{width:100%;border-collapse:collapse;margin:4px 0 8px;font-size:10pt}.lg-tab th,.lg-tab td{border:1px solid black;padding:3px 5px;text-align:left}.lg-tab th{font-size:8.5pt;text-transform:uppercase;letter-spacing:.04em}'+
  '.lg-tab .n{text-align:right}.lg-tab tr.total td,.lg-tab tr.subtotal td{font-weight:bold}.lg-qr{font-size:8.5pt;margin-top:8px;word-break:break-all}';
const head=(property:string,title:string,number:string)=>`<div class="lg-head"><div class="brand">${esc(LODGE_LETTERHEAD)}</div><h1>${esc(property)}</h1><div class="sub">${esc(title)}${number?' · '+esc(number):''}</div></div>`;
const num=(c:RptColumn)=>c.type==='money'||c.type==='int'||c.type==='pct';
const tableHtml=(t:DocTable)=>`${t.title?`<div class="lg-sec">${esc(t.title)}</div>`:''}<table class="lg-tab"><thead><tr>${t.columns.map(c=>`<th${num(c)?' class="n"':''}>${esc(c.label)}</th>`).join('')}</tr></thead><tbody>${
  t.rows.map(r=>`<tr${r.kind!=='row'?` class="${r.kind}"`:''}>${t.columns.map(c=>`<td${num(c)?' class="n"':''}>${esc(cellText(c,r.cells[c.key]))}</td>`).join('')}</tr>`).join('')}</tbody></table>`;
/** The printable page (Lodge look, HCC letterhead, A4): {title, body, css} for native.ts printablePage → printPage. */
export function docHtml(m:DocModel):{title:string;body:string;css:string}{
  if(m.lodge)return {title:m.lodge.title,body:m.lodge.body,css:m.lodge.css};
  const grid=m.pairs.length?`<div class="lg-grid">${m.pairs.map(([k,v])=>`<div class="lg-cell"><b>${esc(k)}</b>${esc(v)||'&nbsp;'}</div>`).join('')}</div>`:'';
  const body=head(m.property,m.title,m.number)+grid+m.tables.map(tableHtml).join('')+m.notes.map(n=>`<p class="lg-note">${esc(n)}</p>`).join('')+
    (m.qrText?`<div class="lg-qr">QR text: ${esc(m.qrText)}</div>`:'')+(m.sign.length?`<div class="lg-sign">${m.sign.map(x=>`<div>${esc(x)}</div>`).join('')}</div>`:'');
  return {title:`${m.title} ${m.number}`.trim(),body,css:DOC_CSS};
}
/** The engine result of a document: "Details" (label / value) then every table, then notes. */
export function docResult(s:State,uid:string,m:DocModel):RptResult{
  const pairs:[string,string][]=m.lodge?m.lodge.pairs:m.pairs;
  const det:DocTable={key:'details',title:'Details',columns:[T('item','Item'),T('value','Value')],rows:pairs.map(([k,v])=>R({item:k,value:v}))};
  const tables=[det,...(m.lodge?[]:m.tables),...(m.notes.length&&!m.lodge?[{key:'notes',title:'Notes',columns:[T('note','Note')],rows:m.notes.map(n=>R({note:n}))}]:[])];
  return {id:m.id,title:m.lodge?m.lodge.title:`${m.title} ${m.number}`.trim(),params:{},columns:tables[0].columns,rows:tables[0].rows,sections:tables,
    about:{runAt:s.clock,user:userName(s,uid),dateBasis:m.basis,provisional:false,legacy:false}};
}
