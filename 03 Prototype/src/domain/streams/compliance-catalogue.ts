// SCC tax port (DEC-116) — SCC's compliance rule functions, ported line by line from the SCC tax module
// (reference input SCC-modules/tax/index.html; extracted script scratchpad/scc/tax.js, index.html line = tax.js line + 861).
// SCC's catalogue `D` lives in compliance-catalogue-data.ts (generated verbatim by tools/scc-tax-catalogue.mjs). Every function below keeps
// SCC's logic, including what looks like a bug (listed as owner questions in 05 Delivery Evidence/SCC Port/SCC_Tax_Port_2026-10-27.md).
// HCC difference (architecture rule, owner question 5): "today" is the business date, never the device clock. Dates are ISO strings.
// Exported names start with 'cmp' / 'CMP' (shared mock namespace).
import {CMP_SCC_D,type CMPSccCard,type CMPSccMonth} from './compliance-catalogue-data';
import {addDays,nights} from '../money';

export {CMP_SCC_D};
export type {CMPSccCard,CMPSccMonth};

/** SCC MONTHS (L342): the FY pages, April → March. */
export const CMP_MONTHS=['April','May','June','July','August','September','October','November','December','January','February','March'] as const;
export type CMPMonth=typeof CMP_MONTHS[number];
const MONTH_NAMES=['January','February','March','April','May','June','July','August','September','October','November','December'];
const SHORT_MONTHS:Record<string,number>={Jan:0,Feb:1,Mar:2,Apr:3,May:4,Jun:5,Jul:6,Aug:7,Sep:8,Oct:9,Nov:10,Dec:11};
/** SCC FY_BASELINE_START (L155): the catalogue texts are authored for FY 2024–25. */
export const CMP_FY_BASELINE_START=2024;
/** SCC NEW_ACT_FORMS (L165): the Income-tax Act, 2025 renames are on. */
export const CMP_NEW_ACT_FORMS=true;
/** SCC FY selector (L224, L235): FY 2026–27 … FY 2032–33; never below 2026. */
export const CMP_FY_MIN=2026,CMP_FY_MAX=2032;
/** SCC AC_SOON_DAYS (L1897): the Action Center window. */
export const CMP_AC_SOON_DAYS=30;
/** SCC BUSINESS_LAB_IDS (L1608): labour cards that stay when the firm has no employees. */
export const CMP_BUSINESS_LAB_IDS=['dc_shops','jn_11','fb9','mr11'];
/** SCC QUARTERS (L1136–1141), period texts verbatim (rebased by cmpRenderFYText). */
export const CMP_QUARTERS=[
  {key:'Q1',label:'Q1 – Apr to Jun',months:['April','May','June'],period:'April – June 2024'},
  {key:'Q2',label:'Q2 – Jul to Sep',months:['July','August','September'],period:'July – September 2024'},
  {key:'Q3',label:'Q3 – Oct to Dec',months:['October','November','December'],period:'October – December 2024'},
  {key:'Q4',label:'Q4 – Jan to Mar',months:['January','February','March'],period:'January – March 2025'},
] as const;
export type CMPQuarterKey='Q1'|'Q2'|'Q3'|'Q4';

export type CMPCat='it'|'gst'|'comp'|'lab';
export const CMP_CATS:CMPCat[]=['it','gst','comp','lab'];
/** SCC renderMasterList catOrder (L1513). */
export const CMP_CAT_ORDER:CMPCat[]=['gst','comp','it','lab'];
/** SCC catLabel (L2140). */
export function cmpCatLabel(c:string){if(c==='it')return 'Income Tax';if(c==='gst')return 'GST';if(c==='comp')return 'Composition GST';return 'Labour Law';}
export type CMPGstType='regular'|'composition'|'both'|'unregistered';
export const CMP_GST_TYPES:CMPGstType[]=['regular','composition','both','unregistered'];
/** SCC typeLabel (L1179). */
export function cmpTypeLabel(t:string){if(t==='regular')return 'Regular Dealer';if(t==='composition')return 'Composition Dealer';if(t==='both')return 'Both / All';if(t==='unregistered')return 'Unregistered';return t||'';}
export type CMPEntity='proprietorship'|'partnership'|'llp'|'company';
/** SCC firm form entity options (index.html L690–695). */
export const CMP_ENTITIES:{id:CMPEntity;label:string}[]=[{id:'proprietorship',label:'Proprietorship'},{id:'partnership',label:'Partnership Firm'},{id:'llp',label:'LLP'},{id:'company',label:'Private Limited Company'}];
/** SCC custom-obligation frequency labels (index.html L792–797): a label only. */
export const CMP_FREQS=['Monthly','Quarterly','Annual','One-time'];

/** A card as the calendar shows it: an SCC built-in, or a custom obligation expanded for one month (SCC expandCustom). */
export type CMPCard=CMPSccCard&{_custom?:boolean;_gid?:string;responsible?:string;reviewer?:string;risk?:string;buffer?:number|null;
  checklist?:string[];workflow?:string[];evidence?:string[]};

// ── FY (SCC L138–146) ──
export const cmpFyLabel=(fy:number)=>`FY ${fy}–${String(fy+1).slice(-2)}`;
export const cmpFyShortLabel=(fy:number)=>`${fy}–${String(fy+1).slice(-2)}`;
export const cmpCalendarYearForMonth=(month:string,fy:number)=>['January','February','March'].includes(month)?fy+1:fy;
/** The FY whose pages contain this business date (Apr–Dec: that year; Jan–Mar: the year before). */
export const cmpFyOfDate=(date:string)=>{const y=+date.slice(0,4),m=+date.slice(5,7);return m>=4?y:y-1;};
/** SCC switchFY clamp (L235): max(2026, year). */
export const cmpClampFy=(fy:number)=>Math.max(CMP_FY_MIN,Number.isInteger(fy)?fy:CMP_FY_MIN);

/** `new Date(y, mi, day)` as an ISO date — the same roll-over as JavaScript (day 0 = last day of the month before; 31 Apr = 1 May). */
const isoOf=(y:number,mi:number,day:number)=>new Date(Date.UTC(y,mi,day)).toISOString().slice(0,10);

/** SCC parseDueDate (L345–369): "D Mon" (Jan–Mar = FY start + 1) or "Last day of / By end of Mon". The due string decides the date,
 *  not the page month (GSTR-4 on the April page is due 30 Jun). Null when SCC returns null. */
export function cmpParseDue(due:string|undefined|null,fy:number):string|null{
  if(!due)return null;
  const lastEnd=/^(?:last day of|by end of)\s+(.+)$/i.exec(due);
  if(lastEnd){
    const mi=SHORT_MONTHS[lastEnd[1]!.trim().substring(0,3)];
    if(mi===undefined)return null;
    const y=cmpCalendarYearForMonth(MONTH_NAMES[mi]!,fy);
    return isoOf(y,mi+1,0);
  }
  const m=due.match(/^(\d{1,2})\s+([A-Za-z]+)$/);
  if(m){
    const day=parseInt(m[1]!);
    const mi=SHORT_MONTHS[m[2]!.substring(0,3)];
    if(mi===undefined)return null;
    const y=mi<=2?(fy+1):fy;
    return isoOf(y,mi,day);
  }
  return null;
}
/** SCC getDaysLeft (L371–376): days from today to the due date (today = the business date in HCC). */
export function cmpDaysLeft(due:string|undefined,fy:number,today:string):number|null{const d=cmpParseDue(due,fy);return d===null?null:nights(today,d);}
export type CMPDaysTag={text:string;cls:'done-days'|'overdue'|'urgent-days'|'soon'|'ok'};
/** SCC daysLeftTag (L378–386): bands overdue / due today / ≤ 7 days / ≤ 15 days / later. */
export function cmpDaysTag(days:number|null,isDone:boolean):CMPDaysTag|null{
  if(isDone)return {text:'✓ Filed',cls:'done-days'};
  if(days===null)return null;
  if(days<0)return {text:`⚠ ${Math.abs(days)}d overdue`,cls:'overdue'};
  if(days===0)return {text:'🔴 Due today',cls:'urgent-days'};
  if(days<=7)return {text:`⚡ ${days}d left`,cls:'urgent-days'};
  if(days<=15)return {text:`⏰ ${days}d left`,cls:'soon'};
  return {text:`${days}d left`,cls:'ok'};
}

/** SCC renderNewActForms (L166–189): Income-tax Act, 2025 form and section renames, applied at render to every text. */
export function cmpRenderNewActForms(text:string):string{
  if(!CMP_NEW_ACT_FORMS)return text;
  return String(text)
    .replace(/Income[‑\- ]?tax Act,?\s*1961/gi,'Income-tax Act, 2025')
    .replace(/\bForm 16A\b/g,'Form 131')
    .replace(/\bForm 16\b/g,'Form 130')
    .replace(/\bForm 24Q\b/g,'Form 138')
    .replace(/\bForm 26Q\b/g,'Form 140')
    .replace(/\bForm 26AS\b/g,'Form 168')
    .replace(/\bSection 200\b/g,'Section 392')
    .replace(/(\bu\/s |\bSec\.? |\bSection )192\b/g,'$1392')
    .replace(/\b194I\b/g,'393').replace(/\b194J\b/g,'393')
    .replace(/(\bu\/s )234B\b/g,'$1424').replace(/(\bu\/s )234C\b/g,'$1425').replace(/(\bu\/s )234E\b/g,'$1427')
    .replace(/\b80C\b/g,'123').replace(/\b80D\b/g,'126')
    .replace(/\bu\/s 201\(1A\)/g,'under the Income-tax Act, 2025')
    .replace(/\bu\/s 271H/g,'under the Income-tax Act, 2025')
    .replace(/\bu\/s 40\(a\)\(ia\)/g,'under the Income-tax Act, 2025')
    .replace(/\bAssessment Year\b/g,'Tax Year').replace(/\bPrevious Year\b/g,'Tax Year');
}
const MONTH_IDX:Record<string,number>={january:0,february:1,march:2,april:3,may:4,june:5,july:6,august:7,september:8,october:9,november:10,december:11};
/** SCC renderFYText (L190–219): rebases the baked FY / AY ranges and the "<Month> YYYY" mentions of the authored window (FY 2023–24 …
 *  2024–25) onto the selected FY, then applies the Income-tax Act, 2025 renames. */
export function cmpRenderFYText(text:string|undefined|null,fy:number):string{
  if(!text)return '';
  const shift=fy-CMP_FY_BASELINE_START;
  const rolled=String(text)
    .replace(/\b(FY |AY )?(\d{4})([–-])(\d{2})\b/g,(m,prefix:string|undefined,y4:string,dash:string,y2:string)=>{
      const start=parseInt(y4,10);
      if(((start+1)%100)!==parseInt(y2,10))return m;
      const ns=start+shift;
      return `${prefix||''}${ns}${dash}${String(ns+1).slice(-2)}`;
    })
    .replace(/\b(January|February|March|April|May|June|July|August|September|October|November|December)\s+(\d{4})\b/g,(m,mon:string,y4:string)=>{
      const cal=parseInt(y4,10);
      const fyOfDate=(MONTH_IDX[mon.toLowerCase()]!<=2)?(cal-1):cal;
      if(fyOfDate<CMP_FY_BASELINE_START-1||fyOfDate>CMP_FY_BASELINE_START)return m;
      return `${mon} ${cal+shift}`;
    });
  return cmpRenderNewActForms(rolled);
}
/** SCC stripHtml (L1970). */
export const cmpStripHtml=(s:unknown)=>String(s==null?'':s).replace(/<[^>]*>/g,'').replace(/&amp;/g,'&').replace(/&lt;/g,'<').replace(/&gt;/g,'>').replace(/&#39;/g,"'").replace(/&quot;/g,'"');

/** SCC obligationKey (L1643–1647): custom = its id; built-in = the name up to the first en-dash or pipe. */
export function cmpObligationKey(item:Pick<CMPCard,'name'|'_custom'|'_gid'>|null|undefined):string{
  if(!item)return '';
  if(item._custom&&item._gid)return item._gid;
  return String(item.name||'').split(/[–|]/)[0]!.trim();
}
/** SCC isPayrollItem (L1609–1611): built-in labour cards except the business-level ones. */
export const cmpIsPayrollItem=(item:Pick<CMPCard,'cat'|'_custom'|'id'>|null|undefined)=>!!item&&item.cat==='lab'&&!item._custom&&!CMP_BUSINESS_LAB_IDS.includes(item.id);
/** SCC gstFilingMode (L1804–1810). */
export function cmpGstFilingMode(item:Pick<CMPCard,'cat'|'name'>|null|undefined):'qrmp'|'monthly'|null{
  if(!item||item.cat!=='gst')return null;
  const n=String(item.name||'').toLowerCase();
  if(n.includes('qrmp'))return 'qrmp';
  if(n.includes('monthly filers'))return 'monthly';
  return null;
}
/** SCC taxPayableField (L1768–1775): which statutory-payable figure a card shows. */
export function cmpTaxPayableField(name:string|undefined):'gstEstimate'|'pf'|'esic'|'pt'|null{
  const n=String(name||'').toLowerCase();
  if(n.includes('gstr-3b')&&n.includes('monthly filers'))return 'gstEstimate';
  if(n.includes('epf')&&n.includes('contribution'))return 'pf';
  if(n.includes('esic')&&n.includes('contribution'))return 'esic';
  if(n.includes('professional tax')&&n.includes('deduction'))return 'pt';
  return null;
}
/** SCC taxPayableAmount key (L1776–1781): the calendar month before the card's page ("YYYY-MM"). */
export function cmpPayableYm(month:string,fy:number):string{
  const mi=MONTH_NAMES.indexOf(month);const y=cmpCalendarYearForMonth(month,fy);
  return isoOf(y,mi-1,1).slice(0,7);
}
/** SCC currentPayableYM (L1847): the month of "today" (the business date in HCC). */
export const cmpCurrentYm=(today:string)=>today.slice(0,7);
/** SCC fmtYMLabel (L1841). */
export function cmpYmLabel(ym:string){const M=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];const m=/^(\d{4})-(\d{2})$/.exec(String(ym||''));
  if(!m)return String(ym||'');return (M[parseInt(m[2]!,10)-1]||m[2])+' '+m[1];}

export type CMPEscalation={label:'Monitor'|'Reminder'|'Escalate'|'Critical';cls:string;note:string};
export type CMPControl={risk:'High'|'Medium'|'Low';cashFlow:'High'|'Medium'|'Low';responsible:string;reviewer:string;buffer:number;
  internalDate:string|null;escalation:CMPEscalation;checklist:string[];workflow:string[];evidence:string[]};
/** SCC getControlProfile (L2244–2276): risk and cash-flow class by name, responsible / reviewer defaults, the internal deadline
 *  (T-5 for High risk, else T-3, or the custom buffer), the escalation on the internal days left, checklist / workflow / evidence. */
export function cmpControlProfile(item:CMPCard,fy:number,today:string):CMPControl{
  const name=String(item.name||'').toLowerCase();
  const isGstPayment=name.includes('gstr-3b')||name.includes('cmp-08');
  const isTds=name.includes('tds');
  const isPayroll=name.includes('epf')||name.includes('esic')||name.includes('professional tax');
  const isAdvanceTax=name.includes('advance tax');
  let risk:CMPControl['risk']=(isGstPayment||isTds||isPayroll||isAdvanceTax)?'High':(item.cat==='lab'||item.cat==='gst'?'Medium':'Low');
  if(item.risk==='High'||item.risk==='Medium'||item.risk==='Low')risk=item.risk;
  const cashFlow:CMPControl['cashFlow']=(isGstPayment||isAdvanceTax||isTds)?'High':(isPayroll||item.cat==='gst'?'Medium':'Low');
  const responsible=item.responsible?item.responsible:(isPayroll?'Payroll / HR Executive':(item.cat==='gst'||item.cat==='comp'?'GST Executive':'Tax Executive'));
  const reviewer=item.reviewer?item.reviewer:(isAdvanceTax?'Partner / CFO':(isPayroll?'HR & Finance Manager':'Compliance Manager'));
  const buffer=(typeof item.buffer==='number'&&item.buffer>=0)?item.buffer:(risk==='High'?5:3);
  const dueDate=cmpParseDue(item.due,fy);
  const internalDate=dueDate?addDays(dueDate,-buffer):null;
  const internalDaysLeft=internalDate?nights(today,internalDate):null;
  let escalation:CMPEscalation={label:'Monitor',cls:'status-watch',note:'Track preparation and keep the working papers current.'};
  if(internalDaysLeft!==null&&internalDaysLeft<=1)escalation={label:'Critical',cls:'status-critical',note:'Escalate immediately to leadership and secure filing evidence today.'};
  else if(internalDaysLeft!==null&&internalDaysLeft<=3)escalation={label:'Escalate',cls:'status-escalate',note:'Reviewer intervention required. Resolve blockers before portal risk increases.'};
  else if(internalDaysLeft!==null&&internalDaysLeft<=7)escalation={label:'Reminder',cls:'status-reminder',note:'Executor reminder due. Validate data pack and challan readiness.'};
  let checklist=isGstPayment
    ?['Sales register and purchase register closed','ITC matched with GSTR-2B','Net GST payable and challan amount approved']
    :isTds
    ?['Deduction working with section-wise mapping','PAN and deductee details validated','Challan or return control total checked']
    :isPayroll
    ?['Payroll register frozen','Employee contribution workings validated','Portal upload file and payment reference prepared']
    :['Books closed for the period','Working papers reviewed','Final computation approved for filing'];
  if(Array.isArray(item.checklist)&&item.checklist.length)checklist=item.checklist;
  let workflow=['Prepare data pack','Reviewer check and approval','File and store evidence'];
  if(Array.isArray(item.workflow)&&item.workflow.length)workflow=item.workflow;
  let evidence=isGstPayment?['GST challan','Filed return PDF','Working reconciliation']:isTds?['OLTAS challan','Filing acknowledgement','Deduction working']:['Portal challan','Return / ECR copy','Payroll or support working'];
  if(Array.isArray(item.evidence)&&item.evidence.length)evidence=item.evidence;
  return {risk,cashFlow,responsible,reviewer,buffer,internalDate,escalation,checklist,workflow,evidence};
}

/** SCC qrmpAdvisory (L1816–1833) — advisory only, nothing is hidden. Needs a firm (HCC: the compliance profile). */
export function cmpQrmpAdvisory(item:CMPCard,profile:{gstType:string;qrmp:boolean}|undefined):{tone:'amber'|'green'|'grey';text:string}|null{
  if(!profile||!item||item.cat!=='gst')return null;
  const mode=cmpGstFilingMode(item);
  if(!mode)return null;
  if(profile.qrmp){
    if(mode==='monthly'){
      if(profile.gstType==='both')return null;
      return {tone:'amber',text:'QRMP firm: you file this quarterly, not monthly. File the quarterly GSTR-1 / GSTR-3B and pay monthly via PMT-06 challan (GSTR-1 IFF upload is optional).'};
    }
    if(mode==='qrmp')return {tone:'green',text:'Applies to your firm (QRMP quarterly filer).'};
    return null;
  }
  if(mode==='qrmp')return {tone:'grey',text:'Only for QRMP-scheme firms — your firm files monthly (see the GSTR-1 / GSTR-3B Monthly card).'};
  return null;
}
/** SCC exportComplianceCSV cell (L1975): formula guard (a leading = + - @ after whitespace gets a '), quotes. */
export function cmpCsvCell(v:unknown):string{let s=String(v==null?'':v);if(/^[\t\r\n ]*[=+\-@]/.test(s))s="'"+s;s=s.replace(/"/g,'""');return /[",\r\n]/.test(s)?'"'+s+'"':s;}
/** SCC file-name sanitising (L1989, L2095). */
export const cmpFileBase=(s:string)=>s.replace(/[^\w.-]+/g,'_').replace(/^_+|_+$/g,'');
/** The built-in cards of one month page (SCC D[month].items). */
export const cmpBuiltInCards=(month:string):CMPCard[]=>CMP_SCC_D[month]?.items??[];
/** Every built-in card with its page month. */
export const cmpAllBuiltIn=():{month:CMPMonth;card:CMPCard}[]=>CMP_MONTHS.flatMap(m=>cmpBuiltInCards(m).map(card=>({month:m,card})));
/** SCC builtInObligations (L1489–1498): unique built-in keys in catalogue order, with the category and name of the first card. */
export function cmpBuiltInObligations():{key:string;cat:CMPCat;name:string}[]{
  const map=new Map<string,{key:string;cat:CMPCat;name:string}>();
  for(const m of CMP_MONTHS)for(const it of cmpBuiltInCards(m)){const key=cmpObligationKey(it);if(key&&!map.has(key))map.set(key,{key,cat:it.cat,name:it.name});}
  return [...map.values()];
}
/** The page month of a built-in card id (SCC findItemMonth over D). */
export function cmpBuiltInMonth(itemId:string):CMPMonth|undefined{return CMP_MONTHS.find(m=>cmpBuiltInCards(m).some(c=>c.id===itemId));}
/** Text with SCC's only markup (<strong>, <em>) as segments for safe rendering; entities decoded. */
export function cmpRichSegments(text:string):{t:string;b?:boolean;i?:boolean}[]{
  const out:{t:string;b?:boolean;i?:boolean}[]=[];let b=false,i=false;
  for(const part of String(text).split(/(<\/?(?:strong|em)>)/)){
    if(part==='<strong>')b=true;else if(part==='</strong>')b=false;else if(part==='<em>')i=true;else if(part==='</em>')i=false;
    else if(part){const t=cmpStripHtml(part);if(t)out.push({t,...(b?{b}:{}),...(i?{i}:{})});}
  }
  return out;
}
