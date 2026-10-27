// SCC tax port (DEC-116) — one-time generator. Reads SCC's own compliance catalogue `D` and SCC's own rule functions straight from
// the SCC tax module (reference input, not in this repo) and writes:
//   • src/domain/streams/compliance-catalogue-data.ts   — the catalogue verbatim (12 month pages, 113 cards, every text field)
//   • ../08 Production/src/Hcc.Domain/Compliance/SccTaxCatalogue.json — the same catalogue for the server (embedded resource)
//   • tests/_scc-tax-golden.json — what SCC's OWN functions compute on SCC's OWN D (the oracle of the prototype and server tests):
//       fy[2026…2032]  every card's due date (parseDueDate)
//       cards          per card: obligation key, payroll item, risk, cash flow, responsible, reviewer, T-buffer, ≈₹ field, GST filing mode
//       control        business date 2026-10-24, FY 2026–27: days left, the days tag, internal due date and escalation
//       visible        getVisibleItems for each GST type × has employees (and no firm selected)
//       qrmp           the QRMP advisory per card for regular / both × QRMP on / off
//       text           sha256 of every rendered text (renderFYText + Income-tax Act 2025 renames) for FY 2026–27 … 2028–29, plus samples
// Usage (from 03 Prototype): node tools/scc-tax-catalogue.mjs [path to SCC tax index.html]
// Never edit the generated files by hand: re-run this tool.
import {readFileSync,writeFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {resolve} from 'node:path';

const SRC=process.argv[2]??'E:/Projects/HCC/Migration data on 25 oct/HCC-Migration-2026-10-25/reference-inputs/SCC-modules/tax/index.html';
const html=readFileSync(SRC,'utf8');
const cut=(from,to)=>{const a=html.indexOf(from);const b=html.indexOf(to,a);if(a<0||b<0)throw new Error('not found: '+from);return html.slice(a,b+to.length);};
/** A top-level declaration of SCC's script, up to the next top-level statement. */
const top=start=>{const a=html.indexOf('\n'+start);if(a<0)throw new Error('not found: '+start);const rest=html.slice(a+1);
  const m=/\n(function |const |let |\/\*|\/\/)/.exec(rest.slice(1));return rest.slice(0,m?m.index+1:rest.length);};
const dSrc=cut('const D={','}; // end DATA');
const D=new Function(dSrc.replace('const D=','return ').replace('}; // end DATA','};'))();
const MONTHS=['April','May','June','July','August','September','October','November','December','January','February','March'];
if(JSON.stringify(Object.keys(D))!==JSON.stringify(MONTHS))throw new Error('unexpected month order');
const cards=MONTHS.flatMap(m=>D[m].items.map(it=>({month:m,...it})));
const sha=s=>createHash('sha256').update(s).digest('hex');

// SCC's own functions, verbatim, over a STATE shim and a Date whose "now" is the given day (the oracle).
const fnSrc=['function getActiveFirm(','function getCalendarYearForMonth(','const FY_BASELINE_START','const NEW_ACT_FORMS','function renderNewActForms(',
  'function renderFYText(','function parseDueDate(','function getDaysLeft(','function daysLeftTag(','const BUSINESS_LAB_IDS','function isPayrollItem(',
  'function getVisibleItems(','function obligationKey(','function isArchived(','function taxPayableField(','function gstFilingMode(','function qrmpAdvisory(',
  'function stripHtml(','function subtractDays(','function getControlProfile('].map(top).join('\n');
function scc(state,today){
  const [y,m,d]=(today??'2026-10-24').split('-').map(Number);
  class FixedDate extends Date{constructor(...a){if(a.length)super(...a);else super(y,m-1,d);}static now(){return new Date(y,m-1,d).getTime();}}
  return new Function('STATE','Date','D','MONTHS',fnSrc+'\nreturn {parseDueDate,getDaysLeft,daysLeftTag,renderFYText,obligationKey,isPayrollItem,getVisibleItems,taxPayableField,gstFilingMode,qrmpAdvisory,stripHtml,getControlProfile};')
    ({firms:[],activeFirmId:null,archived:[],archivedCats:[],custom:[],...state},FixedDate,D,MONTHS);
}
const iso=d=>d?d.getFullYear()+'-'+String(d.getMonth()+1).padStart(2,'0')+'-'+String(d.getDate()).padStart(2,'0'):null;
const golden={source:'SCC tax/index.html — const D (line '+(html.slice(0,html.indexOf('const D={')).split('\n').length)+') and SCC\'s own rule functions',
  sha256:sha(JSON.stringify(D)),cards:cards.length,keys:0,fy:{}};
const base=scc({fyStartYear:2026});
golden.keys=new Set(cards.map(c=>base.obligationKey(c))).size;
for(let fy=2026;fy<=2032;fy++){const f=scc({fyStartYear:fy});golden.fy[fy]=cards.map(c=>[c.month,c.id,c.due,iso(f.parseDueDate(c.due,c.month))]);}
golden.cardRules=cards.map(c=>{const p=base.getControlProfile(c,c.month);
  return [c.id,base.obligationKey(c),base.isPayrollItem(c)?1:0,p.risk,p.cashFlow,p.responsible,p.reviewer,p.buffer,base.taxPayableField(c.name),base.gstFilingMode(c)];});
golden.control={today:'2026-10-24',fy:2026,rows:cards.map(c=>{const p=base.getControlProfile(c,c.month),days=base.getDaysLeft(c.due,c.month);
  return [c.id,days,base.stripHtml(base.daysLeftTag(days,false)),iso(p.internalDate),p.escalation.label];})};
golden.visible={none:scc({fyStartYear:2026}).getVisibleItems(cards).map(c=>c.id).join(' ')};
for(const type of ['regular','composition','both','unregistered'])for(const emp of [true,false]){
  const f=scc({fyStartYear:2026,firms:[{id:'f',name:'F',type,hasEmployees:emp}],activeFirmId:'f'});golden.visible[`${type}|${emp?'emp':'noemp'}`]=f.getVisibleItems(cards).map(c=>c.id).join(' ');}
golden.qrmp={};
for(const type of ['regular','both'])for(const qrmp of [true,false]){
  const f=scc({fyStartYear:2026,firms:[{id:'f',name:'F',type,qrmp}],activeFirmId:'f'});
  golden.qrmp[`${type}|${qrmp?'qrmp':'monthly'}`]=cards.map(c=>[c.id,f.stripHtml(f.qrmpAdvisory(c,c.month))]).filter(r=>r[1]);}
golden.text={};
for(const fy of [2026,2027,2028]){const f=scc({fyStartYear:fy});
  const rendered=MONTHS.map(m=>[f.renderFYText(D[m].note),...D[m].items.map(i=>['name','form','who','what','why','penalty'].map(k=>f.renderFYText(i[k])))]);
  // sha256 over the rendered texts joined with U+0001 (note, then name / form / who / what / why / penalty of each card, page by page) — the server hashes the same.
  golden.text[fy]={sha256:sha(rendered.flat(2).join('')),aprilNote:f.renderFYText(D.April.note),ap4:f.renderFYText(cards.find(c=>c.id==='ap4').name),
    my2:f.renderFYText(cards.find(c=>c.id==='my2').name),jn3:f.renderFYText(cards.find(c=>c.id==='jn3').name),dc5:f.renderFYText(cards.find(c=>c.id==='dc5').name),
    ap1Penalty:f.renderFYText(cards.find(c=>c.id==='ap1').penalty),q4:f.renderFYText('January – March 2025')};}
const json=JSON.stringify(D);
writeFileSync(resolve('src/domain/streams/compliance-catalogue-data.ts'),
`// GENERATED by tools/scc-tax-catalogue.mjs from SCC tax/index.html (const D, ${cards.length} cards, ${golden.keys} obligation keys) — do not edit.
// SCC's compliance catalogue verbatim (DEC-116): every field of every card and each month's control note, as SCC ships it.
// sha256(JSON) = ${golden.sha256}
export type CMPSccCard={id:string;cat:'it'|'gst'|'comp'|'lab';name:string;due:string;urgent?:boolean;freq:string;form:string;who:string;what:string;why:string;penalty:string};
export type CMPSccMonth={note:string;items:CMPSccCard[]};
export const CMP_SCC_D:Record<string,CMPSccMonth>=${json};
`);
writeFileSync(resolve('../08 Production/src/Hcc.Domain/Compliance/SccTaxCatalogue.json'),json+'\n');
const rows=(k,v)=>`  "${k}": [\n`+v.map(r=>'   '+JSON.stringify(r)).join(',\n')+'\n  ]';
const body=JSON.stringify({source:golden.source,sha256:golden.sha256,cards:golden.cards,keys:golden.keys},null,1).replace(/\n}$/,'')
  +',\n "fy": {\n'+Object.entries(golden.fy).map(([k,v])=>rows(k,v)).join(',\n')+'\n },\n'
  +' "cardRules": [\n'+golden.cardRules.map(r=>'  '+JSON.stringify(r)).join(',\n')+'\n ],\n'
  +' "control": {"today": "2026-10-24", "fy": 2026, "rows": [\n'+golden.control.rows.map(r=>'  '+JSON.stringify(r)).join(',\n')+'\n ]},\n'
  +' "visible": '+JSON.stringify(golden.visible,null,1).replace(/\n/g,'\n ')+',\n'
  +' "qrmp": {\n'+Object.entries(golden.qrmp).map(([k,v])=>rows(k,v)).join(',\n')+'\n },\n'
  +' "text": '+JSON.stringify(golden.text,null,1).replace(/\n/g,'\n ')+'\n}\n';
writeFileSync(resolve('tests/_scc-tax-golden.json'),body);
// tests/_scc-tax-oracle.mjs — SCC's own functions, verbatim, wrapped in a factory over a STATE shim, a fixed "today" and the given catalogue.
const oracleNames=['function getActiveFirm(','function getFinancialYearLabel(','function getFinancialYearShortLabel(','function getCalendarYearForMonth(','const FY_BASELINE_START',
  'const NEW_ACT_FORMS','function renderNewActForms(','function renderFYText(','function getFirmCompliance(','function isItemDone(','function getItemRecord(','function isItemNA(',
  'function findItemMonth(','function dueDateIso(','const MONTHS=','const MONTH_IDX=','function parseDueDate(','function getDaysLeft(','function daysLeftTag(','function typeLabel(',
  'const BUSINESS_LAB_IDS','function isPayrollItem(','function getVisibleItems(','function monthAbbr(','function obligationKey(','function isArchived(','function expandCustom(',
  'function getMonthItems(','function taxPayableField(','function taxPayableAmount(','const MONTH_IDX_CAL','function gstFilingMode(','function qrmpAdvisory(','function fmtYMLabel(',
  'function currentPayableYM(','function getStatutoryPayable(','const AC_SOON_DAYS','function collectOutstanding(','function stripHtml(','function hasOverdueInMonth(',
  'function catLabel(','function escapeHtml(','function subtractDays(','function getControlProfile(','function todayStr('];
const quarterSrc=cut('const QUARTERS = [','];');
const dashSrc=cut('  const quarterData = QUARTERS.map(q=>{','.slice(0,8);');
const csvRows=cut("  const rows = [['Firm','FY','Month'",'  });\n  });');
const csvCell=cut("  const cell = s => { s = String(s==null?'':s); if(/^[\\t\\r\\n ]*[=+\\-@]/.test(s))s=\"'\"+s;",'};');
const zipSan=cut('    const sanitize = s => String(s==null?','\'\');');
writeFileSync(resolve('tests/_scc-tax-oracle.mjs'),
`// GENERATED by tools/scc-tax-catalogue.mjs — SCC's OWN tax-module functions copied verbatim from SCC tax/index.html (the test oracle, DEC-116).
// Only the wrapper is HCC's: a STATE shim (firms, activeFirmId, fyStartYear, compliance, custom, archived, archivedCats), a Date whose "now" is the
// business date, window.__stAsOf = that date, the ≈₹ feed passed in (SCC reads it from localStorage), and D = the catalogue under test.
// Do not edit: re-run the tool.
/* eslint-disable */
export function sccTaxOracle({STATE,today,D,feed}){
  const [ty,tm,td]=today.split('-').map(Number);
  const RealDate=globalThis.Date;
  class Date extends RealDate{constructor(...a){if(a.length)super(...a);else super(ty,tm-1,td);}static now(){return new RealDate(ty,tm-1,td).getTime();}}
  const window={__stAsOf:today};
  const getTaxPayableFeed=()=>feed||{};
${oracleNames.map(top).join('\n').replace(/^/gm,'  ')}
  // SCC QUARTERS (verbatim)
  ${quarterSrc}
  // SCC renderDashboard: the quarter figures, verbatim (the curQ quarter and the HTML are left out).
  function dashboard(curQ){
${dashSrc.replace(/^/gm,'  ')}
    return {quarterData,cq,ownerLoad,cashExposure,monthlyRows,focusItems};
  }
  // SCC exportComplianceCSV: the cell function and the rows, verbatim.
  function csv(){
    const f = getActiveFirm();
    const fyLabel = (typeof getFinancialYearLabel==='function') ? getFinancialYearLabel() : '';
${csvCell.replace(/^/gm,'  ')}
${csvRows.replace(/^/gm,'  ')}
    return {rows,cell};
  }
  // SCC exportEvidenceZip: the folder-name sanitiser, verbatim.
${zipSan.replace(/^/gm,'  ')}
  return {parseDueDate,getDaysLeft,daysLeftTag,renderFYText,renderNewActForms,obligationKey,isPayrollItem,getVisibleItems,expandCustom,getMonthItems,taxPayableField,
    taxPayableAmount,gstFilingMode,qrmpAdvisory,getStatutoryPayable,collectOutstanding,stripHtml,hasOverdueInMonth,getControlProfile,isItemDone,isItemNA,getItemRecord,
    dashboard,csv,sanitize,MONTHS,QUARTERS,typeLabel,catLabel,getFinancialYearLabel,getFinancialYearShortLabel,dueDateIso,findItemMonth};
}
`);
JSON.parse(readFileSync(resolve('tests/_scc-tax-golden.json'),'utf8'));
console.log(`${cards.length} cards, ${golden.keys} keys, sha256 ${golden.sha256}`);
