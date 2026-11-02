// Compliance calendar = SCC's tax module in full (DEC-116, SCC port 27 Oct 2026). Earlier (W05-C) HCC took only 7 due-date rules from SCC;
// this module now carries SCC's whole "Compliance Operating System" (scratchpad/scc/tax.js; index.html line = tax.js line + 861):
//   • the catalogue of 113 dated cards / 30 obligation keys per FY page April–March with SCC's due-date engine (compliance-catalogue.ts);
//   • the firm profile (one per property — the hotel is one firm; Owner sets it) and SCC's applicability gating (GST type, QRMP advisory,
//     has employees); archiving per obligation key and per category; custom obligations (due day 0–31 in the ticked month, as SCC);
//   • recording per FY: done (filed-on date, notes, evidence ref, reviewer — all optional) and not applicable (no reason), as SCC;
//   • the control layer (risk, cash flow, responsible, reviewer, internal T-5 / T-3 deadline, escalation, checklist, workflow, evidence);
//   • views: month page (7 stats, control note, overdue dots), quarterly dashboard, 30-day Action Center, CSV register (13 columns), month
//     print, evidence files + completeness + ZIP, and the "≈₹ payable" chips / Statutory Payable panel computed from HCC's own data.
// HCC platform safeguards kept (brief): Owner + Accountant only (DEC-055), Owner manages (DEC-058), audit text per command, and an Owner-only
// reopen that keeps the reopened record (SCC deletes it). HCC-only business rules removed (brief): the paid-holiday / Sunday due-date shift,
// the "1–28 of the next month" custom rule and dated rule versions, the mandatory 4–40 reference and 5–120 not-applicable reason, the 30-obligation
// cap and unique names. Kept as HCC extras (owner questions): the "Due this week" dashboard card (dueSoonDays) and the simulated CA pack.
// SCC decisions wave (27 Oct 2026): DEC-125 — the FY follows the business date on 1 April (a pick holds until the next 1 April; cmpSlice
// returns the effective FY); DEC-133 — FSSAI and lodging house licence renewal cards from the firm profile's expiry dates (compliance-licences.ts).
// Exported names start with 'cmp' / 'CMP' (shared mock namespace).
import type {State} from '../types';
import {can} from '../selectors';
import {nights,addDays} from '../money';
import {gstr1Summary} from '../gstr1';
import {hrSlice} from './staff';
import {
  CMP_MONTHS,CMP_QUARTERS,CMP_FY_MIN,CMP_AC_SOON_DAYS,type CMPMonth,type CMPCat,type CMPCard,type CMPGstType,type CMPEntity,type CMPControl,type CMPDaysTag,
  type CMPQuarterKey,cmpBuiltInCards,cmpParseDue,cmpDaysLeft,cmpDaysTag,cmpRenderFYText,cmpObligationKey,cmpIsPayrollItem,cmpControlProfile,cmpQrmpAdvisory,
  cmpTaxPayableField,cmpPayableYm,cmpCatLabel,cmpTypeLabel,cmpFyLabel,cmpFyShortLabel,cmpStripHtml,cmpCsvCell,cmpFileBase,cmpCalendarYearForMonth,
  cmpFyOfDate,cmpClampFy,cmpBuiltInMonth,cmpYmLabel,cmpBusinessFy,cmpEffectiveFy,cmpFyInList,cmpFyPickEnds,
} from './compliance-catalogue';
import {CMP_SCC_D} from './compliance-catalogue-data';
import {CMP_LICENCE_DEFS,type CMPLicenceKind} from './compliance-licences';

export * from './compliance-catalogue';
export * from './compliance-licences';

// ── State ──
export type CMPProfile={name:string;pan:string;gstin:string;entity:CMPEntity;gstType:CMPGstType;hasEmployees:boolean;qrmp:boolean;notes:string;
  /** DEC-133 (optional, Owner): licence numbers (free text) and expiry dates (YYYY-MM-DD). An expiry date puts the renewal card on its month. */
  fssaiNo?:string;fssaiExpiry?:string;lodgingNo?:string;lodgingExpiry?:string;
  /** ACC-2 (contract §4, §5; Owner-only money settings): GST on booking advances (DEC-142, default off); the hotel deducts TDS (DEC-146, default on) and its deductor type (rate column). */
  gstOnAdvances?:boolean;/** ACC-3 C10: the switch applies to advances dated on or after this date (absent = every advance). */gstOnAdvancesFrom?:string;hotelDeductsTds?:boolean;tdsDeductorType?:'individual'|'other';
  updatedBy?:string;updatedAt?:string};
/** SCC record (L2421, L2190) + HCC audit fields (who / when). Done and NA are independent flags; NA wins on screen (SCC). */
export type CMPRecord={done?:boolean;na?:boolean;filedOn?:string;notes?:string;evidenceRef?:string;reviewerNote?:string;dueDate?:string;
  /** HCC: who recorded the filing and when (simulated clock). */ by?:string;at?:string;
  /** HCC: who marked it not applicable and when. */ naBy?:string;naAt?:string;
  /** Set when the record came from the HCC calendar before the SCC port ("obligation:period"). */ legacy?:string};
/** SCC custom obligation (saveCustomObligation L1382–1404). One property = one firm, so SCC's firm scope does not apply. */
export type CMPCustom={id:string;cat:CMPCat;name:string;freq:string;dueDay:number;months:string[];form:string;who:string;what:string;why:string;penalty:string;
  responsible:string;reviewer:string;risk:''|'High'|'Medium'|'Low';buffer:number|null;checklist:string[];workflow:string[];evidence:string[];urgent:false;
  updatedBy?:string;updatedAt?:string};
/** An evidence file (SCC SaagarEvidence, keyed firm|FY|item). The content lives in s.docObjectsX (prototype, LF) or on the server. */
export type CMPFile={id:string;fy:number;itemId:string;name:string;size:number;type:string;addedBy:string;addedAt:string;
  /** LF §1.10 (SCHEMA 13): the content lives in s.docObjectsX under this ref (v12 saves kept it in s.complianceFilesX). */ objectRef?:string};
/** HCC safeguard: what an Owner reopen removed (SCC deletes it without a trace). */
export type CMPReopened={fy:number;itemId:string;part:'done'|'na';record:CMPRecord;reopenedBy:string;reopenedAt:string;reopenReason:string};
export type CMPPack={id:string;at:string;by:string;to:string;toName:string;counts:{overdue:number;due:number;filed:number;na:number};lines:string[]};
export type CMPSettings={caName:string;caEmail:string;/** HCC extra: the "Due this week" dashboard card window. */dueSoonDays:number};
/** State slice, stored at s.complianceX (optional; absent → empty). Records are bucketed per FY start year, as SCC (L9, L241–246). */
export type CMPState={profile?:CMPProfile;fyStart:number;records:Record<string,Record<string,CMPRecord>>;custom:CMPCustom[];archived:string[];
  archivedCats:CMPCat[];files:CMPFile[];packs:CMPPack[];settings:CMPSettings;reopened?:CMPReopened[];
  /** DEC-125: the FY of the business date (cmpFyOfDate) when the FY was picked; null / absent = no pick in force → the business date's FY.
   *  cmpSlice returns fyStart = the EFFECTIVE FY (cmpEffectiveFy) and keeps fyChosenIn only while the pick holds. */
  fyChosenIn?:number|null};
export const CMP_MAX_REOPENED=200;
export const CMP_MAX_PACKS=20;
/** HCC safeguard (SCC has no limit): an evidence file up to 5 MB. */
export const CMP_FILE_MAX_BYTES=5*1024*1024;
/** HCC column limits for SCC's free texts (SCC has none). */
export const CMP_LIMITS={name:200,pan:20,gstin:20,firmName:120,notes:500,evidenceRef:200,reviewer:200,text:2000,short:200,list:40,fileName:200,/** DEC-133 */licenceNo:60};
export const CMP_DEFAULT_SETTINGS:CMPSettings={caName:'Rao & Associates (fictional)',caEmail:'ca.desk@example.test',dueSoonDays:7};
export const CMP_ILLUSTRATIVE='SCC compliance catalogue — due dates and texts as SCC ships them (Maharashtra); CA to confirm for a hotel.';
export const cmpEmpty=():CMPState=>({fyStart:CMP_FY_MIN,records:{},custom:[],archived:[],archivedCats:[],files:[],packs:[],settings:{...CMP_DEFAULT_SETTINGS}});

// ── Before the SCC port (W05-C shape): obligations × periods with filings. Mapped onto SCC cards so nothing recorded is lost. ──
type LegacyRule={type:'day';day:number;special?:{month:number;day:number}}|{type:'month-end'}|{type:'fixed';month:number;day:number};
export type CMPLegacyObligation={id:string;kind:string;name:string;form:string;freq:'Monthly'|'Annual';rules:{from:string;rule:LegacyRule}[];owner:string;from:string;active:boolean;note?:string};
export type CMPLegacyFiling={obligation:string;period:string;status:'Filed'|'Not applicable';by:string;at:string;reference?:string;reason?:string};
type LegacyState={obligations?:CMPLegacyObligation[];filings?:CMPLegacyFiling[];packs?:CMPPack[];settings?:Partial<CMPSettings>;
  reopened?:(CMPLegacyFiling&{reopenedBy:string;reopenedAt:string;reopenReason:string})[]};
/** HCC obligation kind → its SCC card per page April…March (gap report §7 step 2). GSTR-9 for FY y → dc5 in FY y+1. */
export const CMP_LEGACY_CARDS:Record<string,string[]>={
  'TDS':['ap1','my1','jn1','jl1','ag1','sp1','oc1','nv1','dc1','jn_1','fb1','mr1'],
  'GSTR-1':['ap2','my3','jn5','jl4','ag3','sp4','oc4','nv3','dc3','jn_3','fb4','mr5'],
  'GSTR-3B':['ap3','my4','jn6','jl6','ag4','sp5','oc5','nv4','dc4','jn_4','fb5','mr6'],
  'EPF':['ap5','my5','jn8','jl9','ag5','sp6','oc9','nv5','dc7','jn_8','fb6','mr8'],
  'ESIC':['ap6','my6','jn9','jl10','ag6','sp7','oc10','nv6','dc8','jn_9','fb7','mr9'],
  'PT':['ap7','my8','jn10','jl12','ag7','sp8','oc11','nv8','dc9','jn_12','fb8','mr10'],
};
/** The SCC archive keys of an HCC obligation kind (a switched-off obligation becomes archived keys). */
export const CMP_LEGACY_KEYS:Record<string,string[]>={'TDS':['TDS Deposit'],'GSTR-1':['GSTR-1'],'GSTR-3B':['GSTR-3B'],'GSTR-9':['GSTR-9'],
  'EPF':['EPF Contribution Deposit','EPF Contribution'],'ESIC':['ESIC Contribution Deposit','ESIC Contribution'],'PT':['Professional Tax']};
const LEGACY_CAT:Record<string,CMPCat>={'TDS':'it','GSTR-1':'gst','GSTR-3B':'gst','GSTR-9':'gst','EPF':'lab','ESIC':'lab','PT':'lab'};
const MONTH_NAMES=['January','February','March','April','May','June','July','August','September','October','November','December'];
const ymAdd=(ym:string,n:number)=>{const y=+ym.slice(0,4),m=+ym.slice(5,7)-1+n;const yy=y+Math.floor(m/12),mm=((m%12)+12)%12;return `${yy}-${String(mm+1).padStart(2,'0')}`;};
/** Where an HCC filing (obligation kind, period) lands in SCC: the card on page month(P + 1) of the FY containing that page. */
export function cmpLegacyCard(kind:string,period:string):{fy:number;itemId:string}|undefined{
  if(kind==='GSTR-9'){const y=+period.slice(0,4);return Number.isInteger(y)&&/^\d{4}-\d{2}$/.test(period)?{fy:y+1,itemId:'dc5'}:undefined;}
  const cards=CMP_LEGACY_CARDS[kind];if(!cards||!/^\d{4}-\d{2}$/.test(period))return undefined;
  const page=ymAdd(period,1),month=MONTH_NAMES[+page.slice(5,7)-1]!;
  return {fy:cmpFyOfDate(page+'-01'),itemId:cards[CMP_MONTHS.indexOf(month as CMPMonth)]!};
}
export const cmpIsLegacy=(x:unknown):boolean=>!!x&&typeof x==='object'&&Array.isArray((x as LegacyState).obligations)&&!('records' in (x as object));
/** Converts a W05-C slice: the first obligation of each kind maps onto SCC's built-in cards; any further obligation of a kind becomes an SCC
 *  custom obligation (its latest due rule; SCC custom edits are retroactive). Filings → done / NA records, switched-off → archived keys,
 *  reopened filings → the reopened log. Idempotent: an SCC-shaped slice is returned unchanged by cmpSlice. */
export function cmpFromLegacy(old:LegacyState,s:Pick<State,'date'|'settings'>):CMPState{
  const x=cmpEmpty();x.fyStart=cmpClampFy(cmpFyOfDate(s.date));
  x.profile={name:s.settings?.hotel||'Hotel',pan:'',gstin:'',entity:'proprietorship',gstType:'regular',hasEmployees:true,qrmp:false,
    notes:'Set from the HCC calendar before the SCC port (it tracked GSTR-1 / GSTR-3B monthly, EPF, ESIC and PT): regular dealer, monthly filer, has employees. Owner to confirm.'};
  x.settings={...CMP_DEFAULT_SETTINGS,...(old.settings??{})};x.packs=old.packs??[];
  const obs=[...(old.obligations??[])],first=new Map<string,string>();
  for(const o of obs)if(!first.has(o.kind))first.set(o.kind,o.id);
  const where=new Map<string,(p:string)=>{fy:number;itemId:string}|undefined>();
  for(const o of obs){
    if(first.get(o.kind)===o.id){
      where.set(o.id,p=>cmpLegacyCard(o.kind,p));
      if(!o.active)for(const k of CMP_LEGACY_KEYS[o.kind]??[])if(!x.archived.includes(k))x.archived.push(k);
      continue;
    }
    const sorted=[...(o.rules??[])].sort((a,b)=>a.from.localeCompare(b.from)),rule=sorted[sorted.length-1]?.rule;
    const id='cust-'+cmpFileBase(o.id);
    const annual=o.freq==='Annual'||rule?.type==='fixed';
    const dueDay=rule?.type==='month-end'?0:rule?.type==='day'||rule?.type==='fixed'?rule.day:0;
    const months=annual&&rule?.type==='fixed'?[MONTH_NAMES[rule.month-1]!]:[...CMP_MONTHS];
    x.custom.push({id,cat:LEGACY_CAT[o.kind]??'it',name:o.name,freq:annual?'Annual':'Monthly',dueDay,months,form:o.form??'',who:'',what:o.note??'',why:'',penalty:'',
      responsible:o.owner??'',reviewer:'',risk:'',buffer:null,checklist:[],workflow:[],evidence:[],urgent:false});
    if(!o.active)x.archived.push(id);
    where.set(o.id,p=>{
      if(annual&&rule?.type==='fixed'){const y=+p.slice(0,4)+1;return {fy:y,itemId:`${id}-${MONTH_NAMES[rule.month-1]!.slice(0,3)}`};}
      const page=ymAdd(p,1);return {fy:cmpFyOfDate(page+'-01'),itemId:`${id}-${MONTH_NAMES[+page.slice(5,7)-1]!.slice(0,3)}`};});
  }
  const toRecord=(f:CMPLegacyFiling,fy:number,itemId:string):CMPRecord=>{
    const card=cmpFindCard(x,itemId);const due=card?cmpParseDue(card.due,fy):null;
    return f.status==='Filed'
      ?{done:true,filedOn:f.at.slice(0,10),notes:'',evidenceRef:f.reference??'',reviewerNote:'',...(due?{dueDate:due}:{}),by:f.by,at:f.at,legacy:`${f.obligation}:${f.period}`}
      :{na:true,...(f.reason?{notes:f.reason}:{}),naBy:f.by,naAt:f.at,legacy:`${f.obligation}:${f.period}`};};
  for(const f of old.filings??[]){
    const w=where.get(f.obligation)?.(f.period);if(!w)continue;
    const b=(x.records[String(w.fy)]??={});b[w.itemId]={...(b[w.itemId]??{}),...toRecord(f,w.fy,w.itemId)};
  }
  const reopened:CMPReopened[]=[];
  for(const r of old.reopened??[]){const w=where.get(r.obligation)?.(r.period);if(!w)continue;
    reopened.push({fy:w.fy,itemId:w.itemId,part:r.status==='Filed'?'done':'na',record:toRecord(r,w.fy,w.itemId),reopenedBy:r.reopenedBy,reopenedAt:r.reopenedAt,reopenReason:r.reopenReason});}
  if(reopened.length)x.reopened=reopened;
  return x;
}

/** The slice as every view and command reads it. DEC-125: fyStart is the EFFECTIVE FY (the pick while it holds, else the business date's FY,
 *  clamped to SCC's list); fyChosenIn stays only while the pick holds (null otherwise), so writing the slice back changes nothing. */
export function cmpSlice(s:State):CMPState{
  const raw=s.complianceX as unknown;
  if(!raw)return {...cmpEmpty(),fyStart:cmpBusinessFy(s.date),fyChosenIn:null};
  const x=(cmpIsLegacy(raw)?cmpFromLegacy(raw as LegacyState,s):raw) as Partial<CMPState>;
  const chosen=typeof x.fyChosenIn==='number'&&x.fyChosenIn===cmpFyOfDate(s.date)?x.fyChosenIn:null;
  return {...(x.profile?{profile:x.profile}:{}),fyStart:cmpEffectiveFy(cmpClampFy(x.fyStart??CMP_FY_MIN),chosen,s.date),fyChosenIn:chosen,records:x.records??{},custom:x.custom??[],
    archived:x.archived??[],archivedCats:x.archivedCats??[],files:x.files??[],packs:x.packs??[],settings:{...CMP_DEFAULT_SETTINGS,...(x.settings??{})},...(x.reopened?{reopened:x.reopened}:{})};
}
/** DEC-125 restore of a save from before DEC-125 (shape-detected, idempotent; SCHEMA unchanged): the slice has no fyChosenIn. When its audit
 *  shows an FY was picked (CMP FY SET), the pick counts as made in the current business-date FY — no jump on restore; it holds until the next
 *  1 April. Without a recorded pick the calendar simply follows the business date (nothing to store). Same rule as server migration 4180. */
export function cmpStampFyChoice(s:State):boolean{
  const x=s.complianceX as unknown;
  if(!x||typeof x!=='object'||cmpIsLegacy(x)||'fyChosenIn' in (x as object))return false;
  if(!(s.audit??[]).some(a=>a.action==='CMP FY SET'))return false;
  s.complianceX={...(x as CMPState),fyChosenIn:cmpFyOfDate(s.date)};return true;
}
/** DEC-125, for the FY selector: the effective FY, the business date's FY and — while a pick of another FY holds — its last day (31 March)
 *  and the FY the calendar moves to on the next 1 April. */
export function cmpFyStatus(s:State):{fy:number;businessFy:number;pickEnds:string|null;next:number|null}{
  const x=cmpSlice(s),businessFy=cmpBusinessFy(s.date),held=x.fyChosenIn!=null&&x.fyStart!==businessFy;
  return {fy:x.fyStart,businessFy,pickEnds:held?cmpFyPickEnds(x.fyChosenIn!):null,next:held?cmpFyInList(x.fyChosenIn!+1):null};
}

// ── Access: Owner and Accountant only (DEC-055). Managing (profile, archive, custom, reopen, settings) needs 'approve' (Owner, DEC-058). ──
const roleOf=(s:State,uid:string)=>s.users.find(u=>u.id===uid)?.role;
export const cmpHasAccess=(s:State,uid:string)=>{const r=roleOf(s,uid);return (r==='Owner'||r==='Accountant')&&can(s,uid,'reports');};
export const cmpCanManage=(s:State,uid:string)=>cmpHasAccess(s,uid)&&can(s,uid,'approve');

// ── Cards ──
/** SCC expandCustom (L1658–1704): a custom obligation on each ticked month; due day 0 = "Last day of <Mon>", 1–31 = "<d> <Mon>" in that month
 *  (no clamping: 31 in a 30-day month rolls over into the next month, as SCC — owner question 3). Never urgent. */
export function cmpExpandCustom(x:Pick<CMPState,'custom'>,month:string):CMPCard[]{
  const abbr=String(month||'').slice(0,3),out:CMPCard[]=[];
  for(const c of x.custom){
    if(!c||!c.id)continue;
    const months=Array.isArray(c.months)?c.months:[];
    if(!months.includes(month))continue;
    const dueDay=parseInt(String(c.dueDay),10);
    const due=(dueDay===0||isNaN(dueDay))?('Last day of '+abbr):(dueDay+' '+abbr);
    out.push({id:c.id+'-'+abbr,_gid:c.id,_custom:true,cat:c.cat||'it',name:c.name||'Custom Obligation',due,freq:c.freq||'Monthly',form:c.form||'',who:c.who||'',
      what:c.what||'',why:c.why||'',penalty:c.penalty||'',responsible:c.responsible||'',reviewer:c.reviewer||'',risk:c.risk||'',
      buffer:(typeof c.buffer==='number')?c.buffer:null,checklist:[...(c.checklist??[])],workflow:[...(c.workflow??[])],evidence:[...(c.evidence??[])],urgent:false});
  }
  return out;
}
// ── DEC-133: licence-renewal cards (FSSAI, lodging house) from the firm profile's expiry dates ──
const LICENCE_FIELDS:Record<CMPLicenceKind,{no:'fssaiNo'|'lodgingNo';expiry:'fssaiExpiry'|'lodgingExpiry'}>={
  fssai:{no:'fssaiNo',expiry:'fssaiExpiry'},lodging:{no:'lodgingNo',expiry:'lodgingExpiry'}};
const SHORT_MON=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
const isIso=(d:unknown):d is string=>typeof d==='string'&&/^\d{4}-\d{2}-\d{2}$/.test(d)&&new Date(d+'T00:00:00Z').toISOString().slice(0,10)===d;
/** The profile fields of a licence kind. */
export const cmpLicenceFields=(kind:CMPLicenceKind)=>LICENCE_FIELDS[kind];
/** DEC-133: the licence obligations (archive keys = the name up to the en dash, as SCC's keys), in the Labour Law category like SCC's
 *  Shops & Establishment renewal. */
export const cmpLicenceObligations=():{key:string;cat:CMPCat;name:string}[]=>CMP_LICENCE_DEFS.map(d=>({key:cmpObligationKey(d),cat:d.cat,name:d.name}));
/** DEC-133: a licence card — only while its expiry date is set; it belongs to the FY and month of the expiry date and is due on it
 *  ("D Mon" as SCC's due strings, so SCC's engine gives back the expiry date). Risk High; the SCC control layer does the rest. */
export function cmpLicenceCard(x:Pick<CMPState,'profile'>,kind:CMPLicenceKind):{card:CMPCard;month:CMPMonth;fy:number;expiry:string}|undefined{
  const d=CMP_LICENCE_DEFS.find(l=>l.kind===kind),f=LICENCE_FIELDS[kind],exp=x.profile?.[f.expiry];
  if(!d||!isIso(exp))return undefined;
  const mi=+exp.slice(5,7)-1,no=String(x.profile?.[f.no]??'').trim();
  const card:CMPCard={id:d.id,cat:d.cat,name:d.name,due:`${+exp.slice(8,10)} ${SHORT_MON[mi]}`,urgent:false,freq:d.freq,form:d.form+(no?` · Licence no. ${no}`:''),
    who:d.who,what:d.what,why:d.why,penalty:d.penalty,risk:d.risk,responsible:d.responsible,reviewer:d.reviewer,buffer:null,
    checklist:[...d.checklist],workflow:[...d.workflow],evidence:[...d.evidence],_licence:kind};
  return {card,month:MONTH_NAMES[mi] as CMPMonth,fy:cmpFyOfDate(exp),expiry:exp};
}
/** DEC-133: the licence cards on one page of FY `fy`. */
export function cmpLicenceCards(x:Pick<CMPState,'profile'>,month:string,fy:number):CMPCard[]{
  const out:CMPCard[]=[];
  for(const d of CMP_LICENCE_DEFS){const l=cmpLicenceCard(x,d.kind);if(l&&l.fy===fy&&l.month===month)out.push(l.card);}
  return out;
}
const licenceById=(x:Pick<CMPState,'profile'>,itemId:string)=>{const d=CMP_LICENCE_DEFS.find(l=>l.id===itemId);return d?cmpLicenceCard(x,d.kind):undefined;};
/** SCC getMonthItems (L1710): built-in cards of the page + expanded custom obligations; with the FY, also the DEC-133 licence cards of that FY. */
export const cmpMonthItems=(x:Pick<CMPState,'custom'|'profile'>,month:string,fy?:number):CMPCard[]=>
  [...cmpBuiltInCards(month),...cmpExpandCustom(x,month),...(fy===undefined?[]:cmpLicenceCards(x,month,fy))];
/** SCC getVisibleItems (L1613–1628): archived categories and keys vanish; no employees hides payroll cards; GST cards only for regular / both,
 *  composition cards only for composition / both; no profile = SCC "no firm selected" = 'both' (everything). */
export function cmpVisibleItems(x:Pick<CMPState,'profile'|'archived'|'archivedCats'>,items:CMPCard[]):CMPCard[]{
  const f=x.profile;const type=f?.gstType||'both';const noEmployees=!!f&&f.hasEmployees===false;
  return items.filter(item=>{
    if(!item)return false;
    if(x.archivedCats.includes(item.cat))return false;
    if(x.archived.includes(cmpObligationKey(item)))return false;
    if(noEmployees&&cmpIsPayrollItem(item))return false;
    if(item.cat==='it'||item.cat==='lab')return true;
    if(item.cat==='gst')return type==='regular'||type==='both';
    if(item.cat==='comp')return type==='composition'||type==='both';
    return true;
  });
}
export const cmpMonthCards=(x:CMPState,month:string,fy?:number)=>cmpVisibleItems(x,cmpMonthItems(x,month,fy));
/** The card of an item id (built-in, or a custom obligation's month instance), visible or not. DEC-133: a licence card while its expiry is set
 *  — with `fy`, only in the FY of its expiry date. */
export function cmpFindCard(x:Pick<CMPState,'custom'|'profile'>,itemId:string,fy?:number):CMPCard|undefined{
  const l=licenceById(x,itemId);if(l)return fy===undefined||l.fy===fy?l.card:undefined;
  const m=cmpItemMonth(x,itemId);return m?cmpMonthItems(x,m).find(c=>c.id===itemId):undefined;}
/** SCC findItemMonth (L269–274); DEC-133: a licence card's page = the month of its expiry date. */
export function cmpItemMonth(x:Pick<CMPState,'custom'|'profile'>,itemId:string):CMPMonth|undefined{
  const b=cmpBuiltInMonth(itemId);if(b)return b;
  const l=licenceById(x,itemId);if(l)return l.month;
  return CMP_MONTHS.find(m=>cmpExpandCustom(x,m).some(c=>c.id===itemId));}

// ── Records ──
export const cmpRecord=(x:Pick<CMPState,'records'>,fy:number,itemId:string):CMPRecord|undefined=>x.records[String(fy)]?.[itemId];
export const cmpIsNA=(x:Pick<CMPState,'records'>,fy:number,itemId:string)=>cmpRecord(x,fy,itemId)?.na===true;
/** SCC isItemDone (L248): the done flag (callers check NA first, as SCC). */
export const cmpIsDone=(x:Pick<CMPState,'records'>,fy:number,itemId:string)=>!!cmpRecord(x,fy,itemId)?.done;
export const cmpFileCount=(x:Pick<CMPState,'files'>,fy:number,itemId:string)=>x.files.filter(f=>f.fy===fy&&f.itemId===itemId).length;

/** One card as a page shows it (SCC renderMonth L2452–2469). */
export type CMPView={card:CMPCard;month:CMPMonth;fy:number;rec:CMPRecord;isNA:boolean;isDone:boolean;due:string|null;days:number|null;isOverdue:boolean;
  tag:CMPDaysTag|null;control:CMPControl;name:string;form:string;whatText:string;whyText:string;whoText:string;penaltyText:string;files:number};
export function cmpView(x:CMPState,card:CMPCard,month:CMPMonth,fy:number,today:string):CMPView{
  const rec=cmpRecord(x,fy,card.id)??{},isNA=rec.na===true,isDone=!isNA&&!!rec.done,due=cmpParseDue(card.due,fy),days=cmpDaysLeft(card.due,fy,today);
  return {card,month,fy,rec,isNA,isDone,due,days,isOverdue:!isNA&&!isDone&&days!==null&&days<0,tag:cmpDaysTag(days,isDone),control:cmpControlProfile(card,fy,today),
    name:cmpRenderFYText(card.name,fy),form:cmpRenderFYText(card.form,fy),whatText:cmpRenderFYText(card.what,fy),whyText:cmpRenderFYText(card.why,fy),
    whoText:cmpRenderFYText(card.who,fy),penaltyText:cmpRenderFYText(card.penalty,fy),files:cmpFileCount(x,fy,card.id)};
}
export type CMPCatFilter='all'|'na'|CMPCat;
/** SCC renderMonth (L2427–2451): the page's cards (filtered) and its 7 stats over the non-NA cards. */
export function cmpMonthPage(s:State,month:CMPMonth,filter:CMPCatFilter='all',fy?:number){
  const x=cmpSlice(s),y=fy??x.fyStart,today=s.date;
  const all=cmpMonthCards(x,month,y).map(c=>cmpView(x,c,month,y,today));
  const na=all.filter(v=>v.isNA),active=all.filter(v=>!v.isNA);
  const list=filter==='all'?all:filter==='na'?na:all.filter(v=>v.card.cat===filter);
  const ev=cmpEvidenceCompleteness(s,month,y);
  return {fy:y,month,calYear:cmpCalendarYearForMonth(month,y),note:cmpRenderFYText(cmpBuiltInNote(month),y),list,all,
    stats:{total:active.length,completed:active.filter(v=>cmpIsDone(x,y,v.card.id)).length,pending:active.filter(v=>!cmpIsDone(x,y,v.card.id)).length,
      highRisk:active.filter(v=>v.control.risk==='High').length,na:na.length,
      overdue:active.filter(v=>!cmpIsDone(x,y,v.card.id)&&(v.days??999)<0).length,evidence:ev.doneTotal?`${ev.doneSatisfied}/${ev.doneTotal}`:'0/0'}};
}
export const cmpBuiltInNote=(month:string)=>CMP_SCC_D[month]?.note??'';
/** SCC hasOverdueInMonth (L2120–2128): the month tab's overdue dot. */
export function cmpHasOverdueInMonth(s:State,month:CMPMonth,fy?:number):boolean{
  const x=cmpSlice(s),y=fy??x.fyStart;
  return cmpMonthCards(x,month,y).some(item=>{
    if(cmpIsNA(x,y,item.id))return false;if(cmpIsDone(x,y,item.id))return false;
    const days=cmpDaysLeft(item.due,y,s.date);return days!==null&&days<0;});
}
/** SCC renderMonth's opening page (L1718–1724): the month of "today" (the business date). */
export const cmpOpeningMonth=(today:string):CMPMonth=>MONTH_NAMES[+today.slice(5,7)-1] as CMPMonth;

// ── Quarterly dashboard (SCC renderDashboard L2478–2511) ──
export type CMPQuarterItem={card:CMPCard;month:CMPMonth;control:CMPControl;days:number|null;name:string};
export function cmpQuarter(s:State,key:CMPQuarterKey,fy?:number){
  const x=cmpSlice(s),y=fy??x.fyStart,today=s.date;
  const data=CMP_QUARTERS.map(q=>{
    const items:CMPQuarterItem[]=[];
    for(const m of q.months)for(const c of cmpMonthCards(x,m,y))if(!cmpIsNA(x,y,c.id))
      items.push({card:c,month:m as CMPMonth,control:cmpControlProfile(c,y,today),days:cmpDaysLeft(c.due,y,today),name:cmpRenderFYText(c.name,y)});
    const done=items.filter(i=>cmpIsDone(x,y,i.card.id)).length;
    const overdue=items.filter(i=>!cmpIsDone(x,y,i.card.id)&&(i.days??999)<0).length;
    const highRisk=items.filter(i=>i.control.risk==='High').length;
    const score=items.length?Math.round((done/items.length)*100):0;
    return {...q,items,total:items.length,done,pending:items.length-done,overdue,highRisk,score};
  });
  const cq=data.find(q=>q.key===key)??data[0]!;
  const ownerLoad:[string,number][]=[],cash={High:0,Medium:0,Low:0};
  for(const i of cq.items){const r=ownerLoad.find(o=>o[0]===i.control.responsible);if(r)r[1]++;else ownerLoad.push([i.control.responsible,1]);cash[i.control.cashFlow]+=1;}
  const monthly=cq.months.map(m=>{const items=cmpMonthCards(x,m,y).filter(i=>!cmpIsNA(x,y,i.id));const total=items.length,done=items.filter(i=>cmpIsDone(x,y,i.id)).length;
    const overdue=items.filter(i=>!cmpIsDone(x,y,i.id)&&(cmpDaysLeft(i.due,y,today)??999)<0).length;return {month:m as CMPMonth,total,done,overdue,score:total?Math.round((done/total)*100):0};});
  const rank={High:0,Medium:1,Low:2};
  const focus=cq.items.filter(i=>!cmpIsDone(x,y,i.card.id)).sort((a,b)=>rank[a.control.risk]-rank[b.control.risk]||(a.days??999)-(b.days??999)).slice(0,8);
  return {fy:y,quarters:data.map(q=>({key:q.key as CMPQuarterKey,label:q.label,score:q.score,total:q.total})),q:cq,period:cmpRenderFYText(cq.period,y),ownerLoad,cash,monthly,focus};
}

// ── Action Center (SCC collectOutstanding L1897–1917): the selected FY's 12 pages; not done / NA, due within 30 days or overdue. ──
export type CMPAction={itemId:string;month:CMPMonth;name:string;form:string;due:string;dueIso:string|null;cat:CMPCat;days:number;card:CMPCard};
export function cmpActionCenter(s:State,fy?:number):CMPAction[]{
  const x=cmpSlice(s);if(!x.profile)return [];// SCC: no firm → nothing (L1941)
  const y=fy??x.fyStart,out:CMPAction[]=[];
  for(const m of CMP_MONTHS)for(const it of cmpMonthCards(x,m,y)){
    if(cmpIsNA(x,y,it.id)||cmpIsDone(x,y,it.id))continue;
    const days=cmpDaysLeft(it.due,y,s.date);
    if(days===null||days>CMP_AC_SOON_DAYS)continue;
    out.push({itemId:it.id,month:m,name:it.name,form:it.form,due:it.due,dueIso:cmpParseDue(it.due,y),cat:it.cat,days,card:it});
  }
  return out.sort((a,b)=>a.days-b.days);
}
/** Badge on the Accounts tile and the phone home: the Action Center count (SCC updateActionBadge), Owner + Accountant only. */
export const cmpBadge=(s:State,uid:string)=>cmpHasAccess(s,uid)?cmpActionCenter(s).length:0;
/** SCC badge text: "99+" above 99. */
export const cmpBadgeText=(n:number)=>n>99?'99+':String(n);
/** HCC extra (kept, owner question 12): overdue + due within the "Due this week" window (settings.dueSoonDays), from the Action Center rows. */
export const cmpDueThisWeek=(s:State)=>{const d=cmpSlice(s).settings.dueSoonDays;return cmpActionCenter(s).filter(r=>r.days<=d);};
/** "⚠ Nd overdue" / "Due today" / "Nd left" as the Action Center rows say it (SCC L1954). */
export const cmpWhen=(days:number)=>days<0?`${Math.abs(days)}d overdue`:days===0?'Due today':`${days}d left`;

// ── Evidence completeness (SCC computeEvidenceCompleteness L2326–2363) ──
export function cmpEvidenceCompleteness(s:State,scope:'fy'|CMPMonth,fy?:number){
  const x=cmpSlice(s),y=fy??x.fyStart;
  if(!x.profile)return {applicableTotal:0,doneTotal:0,doneSatisfied:0,pct:0,missing:[] as {id:string;name:string;month:CMPMonth;cat:CMPCat}[],noFirm:true};
  const months=scope==='fy'?[...CMP_MONTHS]:[scope];
  const rows:{id:string;name:string;month:CMPMonth;cat:CMPCat;done:boolean;ref:string}[]=[];
  for(const m of months)for(const i of cmpMonthCards(x,m,y)){if(cmpIsNA(x,y,i.id))continue;const rec=cmpRecord(x,y,i.id);
    rows.push({id:i.id,name:i.name,month:m,cat:i.cat,done:cmpIsDone(x,y,i.id),ref:String(rec?.evidenceRef||'').trim()});}
  const doneRows=rows.filter(r=>r.done),missing:{id:string;name:string;month:CMPMonth;cat:CMPCat}[]=[];let doneSatisfied=0;
  for(const r of doneRows){if(r.ref||cmpFileCount(x,y,r.id)>0)doneSatisfied++;else missing.push({id:r.id,name:r.name,month:r.month,cat:r.cat});}
  return {applicableTotal:rows.length,doneTotal:doneRows.length,doneSatisfied,pct:doneRows.length?Math.round(doneSatisfied/doneRows.length*100):0,missing,noFirm:false};
}

// ── CSV register (SCC exportComplianceCSV L1971–1999): the full selected FY, visible cards, 13 columns, formula guard. ──
export const CMP_CSV_HEADER=['Firm','FY','Month','Due','Obligation','Form/Portal','Category','Frequency','Responsible','Risk','Status','Filed On','Days Left'];
export function cmpCsvRows(s:State,fy?:number):string[][]{
  const x=cmpSlice(s),y=fy??x.fyStart,fyLabel=cmpFyLabel(y),rows:string[][]=[CMP_CSV_HEADER];
  for(const m of CMP_MONTHS)for(const it of cmpMonthCards(x,m,y)){
    const na=cmpIsNA(x,y,it.id),done=!na&&cmpIsDone(x,y,it.id),rec=cmpRecord(x,y,it.id),prof=cmpControlProfile(it,y,s.date),days=cmpDaysLeft(it.due,y,s.date);
    rows.push([x.profile?.name||'',fyLabel,m,it.due,cmpStripHtml(cmpRenderFYText(it.name,y)),cmpStripHtml(cmpRenderFYText(it.form||'',y)),cmpCatLabel(it.cat),it.freq||'',
      prof.responsible||'',prof.risk||'',na?'N/A':(done?'Done':'Pending'),(rec&&(rec.filedOn||(rec as {filedRef?:string}).filedRef))||'',days===null?'':String(days)]);
  }
  return rows;
}
/** The register as text (SCC joins with CRLF; the byte-order mark is added by saveCsv) and its SCC file name. Null without a profile (SCC: firm first). */
export function cmpCsv(s:State,fy?:number):{fileName:string;csv:string;rows:number}|null{
  const x=cmpSlice(s);if(!x.profile)return null;const y=fy??x.fyStart;
  const rows=cmpCsvRows(s,y);const fileBase=cmpFileBase('Compliance_'+(x.profile.name||'firm')+'_'+cmpFyLabel(y));
  return {fileName:(fileBase||'Compliance')+'.csv',csv:rows.map(r=>r.map(cmpCsvCell).join(',')).join('\r\n'),rows:rows.length-1};
}

// ── Month print (SCC printCompliance L59–136): A4 landscape, the page's visible cards. ──
export function cmpPrintModel(s:State,month:CMPMonth,fy?:number){
  const x=cmpSlice(s),y=fy??x.fyStart,f=x.profile,firmName=f?f.name:'All Firms',calYear=cmpCalendarYearForMonth(month,y);
  const items=cmpMonthCards(x,month,y);
  const doneCount=items.filter(i=>!cmpIsNA(x,y,i.id)&&cmpIsDone(x,y,i.id)).length,naCount=items.filter(i=>cmpIsNA(x,y,i.id)).length,obCount=items.length;
  return {title:'Compliance — '+month+' — '+firmName,fileBase:'Compliance-'+month+'-'+firmName.replace(/[\\/:*?"<>|]/g,'-'),month,calYear,firmName,
    firmType:f?cmpTypeLabel(f.gstType):'',fyLabel:cmpFyLabel(y),obCount,doneCount,naCount,
    rows:items.map(item=>{const isNA=cmpIsNA(x,y,item.id),isDone=!isNA&&cmpIsDone(x,y,item.id);
      return {due:item.due,name:cmpStripHtml(cmpRenderFYText(item.name,y)),form:cmpStripHtml(cmpRenderFYText(item.form,y)),freq:item.freq,
        responsible:cmpControlProfile(item,y,s.date).responsible,status:isNA?'N/A':(isDone?'✓ Done':'Pending')};}),
    footer:`${obCount} obligation${obCount===1?'':'s'} · ${doneCount} done · ${(obCount-naCount)-doneCount} pending${naCount?` · ${naCount} N/A`:''}`};
}

// ── Evidence ZIP (SCC exportEvidenceZip L2045–2098): folders <Month>_<Category>_<Name> + __<itemId>, de-duplicated names, index.csv. ──
const zipSan=(v:unknown)=>String(v==null?'':v).replace(/[^\w.-]+/g,'_').replace(/^_+|_+$/g,'');
/** SCC's index.csv cell (L2092): quotes only, no formula guard. */
export const cmpZipCell=(v:unknown)=>{const s=String(v==null?'':v).replace(/"/g,'""');return /[",\r\n]/.test(s)?'"'+s+'"':s;};
export function cmpEvidenceZipPlan(s:State,fy?:number){
  const x=cmpSlice(s),y=fy??x.fyStart;
  const meta:Record<string,{name:string;cat:string;month:string}>={};
  for(const m of CMP_MONTHS)for(const it of cmpMonthItems(x,m,y))if(it&&it.id&&!meta[it.id])meta[it.id]={name:cmpStripHtml(cmpRenderFYText(it.name||'',y)),cat:cmpCatLabel(it.cat),month:m};
  const seen:Record<string,Record<string,true>>={},entries:{fileId:string;path:string}[]=[],manifest:string[][]=[['Obligation','Original filename','Stored path','Added-on']];
  for(const e of x.files.filter(f=>f.fy===y)){
    const m=meta[e.itemId];
    let folder=zipSan(m?(m.month+'_'+m.cat+'_'+m.name):e.itemId).slice(0,60);if(!folder)folder='evidence';
    folder=folder+'__'+(zipSan(e.itemId).slice(0,24)||'x');
    const inFolder=(seen[folder]??={});
    let fname=zipSan(e.name||'evidence')||'evidence';
    if(inFolder[fname]){const dot=fname.lastIndexOf('.');const base=dot>0?fname.slice(0,dot):fname;const ext=dot>0?fname.slice(dot):'';let n=2;
      while(inFolder[base+' ('+n+')'+ext])n++;fname=base+' ('+n+')'+ext;}
    inFolder[fname]=true;entries.push({fileId:e.id,path:folder+'/'+fname});
    manifest.push([m?m.name:e.itemId,e.name||'',folder+'/'+fname,e.addedAt.slice(0,10)]);
  }
  const fileBase=cmpFileBase('Evidence_'+(x.profile?.name||'firm')+'_'+cmpFyShortLabel(y));
  return {fileName:(fileBase||'Evidence')+'.zip',entries,index:'﻿'+manifest.map(r=>r.map(cmpZipCell).join(',')).join('\r\n')};
}

// ── "≈₹ payable" (SCC taxPayableBadge / StatutoryPayable panel L1751–1895), computed from HCC's own data (SCC reads its shell bridge). ──
/** One month's statutory figures in paise. */
export type CMPPayable={pf:number;esic:number;pt:number;gstEstimate:number;sources:string[];at:string};
export type CMPPayableFeed={months:Record<string,CMPPayable>;note:string};
export const CMP_PAYABLE_NOTE='Estimate — verify with CA before filing.';
/** What each figure uses (shown on the panel). */
export const CMP_PAYABLE_BASIS='EPF = employee + employer PF and ESIC = employee + employer ESIC of every line of the month’s LOCKED pay run; Professional Tax = PT deducted in that run; GST (estimate) = CGST + SGST + IGST on the tax invoices issued in the month (rooms and restaurant), less the month’s credit notes — the GSTR-1 working summary’s SAC total; input tax credit is not deducted.';
/** HCC's equivalent of SCC's saagar_tax_payable feed for the months the selected FY's chips and the panel read (March of the FY … the
 *  business month): payroll from the locked run of the month, GST from issued invoices. Months with nothing are left out (SCC: no ₹0 noise). */
export function cmpPayableFeed(s:State,fy?:number):CMPPayableFeed{
  const x=cmpSlice(s),y=fy??x.fyStart,cur=s.date.slice(0,7),months:Record<string,CMPPayable>={};
  const want=new Set<string>([cur]);for(let i=0;i<13;i++){const ym=ymAdd(`${y}-03`,i);if(ym<=cur)want.add(ym);}
  const runs=hrSlice(s).runs;
  for(const ym of [...want].sort()){
    const run=runs.find(r=>r.month===ym&&r.status==='Locked');
    const pf=run?run.lines.reduce((a,l)=>a+l.pf+l.pfEr,0):0,esic=run?run.lines.reduce((a,l)=>a+l.esic+l.esicEr,0):0,pt=run?run.lines.reduce((a,l)=>a+l.pt,0):0;
    const last=new Date(Date.UTC(+ym.slice(0,4),+ym.slice(5,7),0)).toISOString().slice(0,10);
    const g=gstr1Summary(s,ym+'-01',last),gst=g.sac.reduce((a,r)=>a+r.cgst+r.sgst+r.igst,0);
    if(!run&&gst===0)continue;
    const sources:string[]=[];
    if(run)sources.push(`Payroll ${cmpYmLabel(ym)} (locked run)`);
    if(gst!==0)sources.push(`GST on invoices issued ${cmpYmLabel(ym)} (net of credit notes)`);
    months[ym]={pf,esic,pt,gstEstimate:gst,sources,at:s.date};
  }
  return {months,note:CMP_PAYABLE_NOTE};
}
/** SCC taxPayableAmount (L1776–1786): the card's figure in whole rupees (rounded), from the month before its page; null when none or ≤ 0. */
export function cmpPayableAmount(feed:CMPPayableFeed|undefined,name:string,month:string,fy:number):number|null{
  const field=cmpTaxPayableField(name);if(!field||!feed)return null;
  const rec=feed.months[cmpPayableYm(month,fy)];if(!rec)return null;
  const amt=Math.round((Number(rec[field])||0)/100);return amt>0?amt:null;
}
/** SCC getStatutoryPayable (L1852–1861): the business month's entry, else the newest available. */
export function cmpStatutoryPayable(feed:CMPPayableFeed|undefined,today:string):{ym:string;rec:CMPPayable;note:string;isCurrent:boolean}|null{
  if(!feed)return null;const keys=Object.keys(feed.months).filter(k=>/^\d{4}-\d{2}$/.test(k));if(!keys.length)return null;
  const cy=today.slice(0,7),ym=feed.months[cy]?cy:keys.slice().sort().pop()!;const rec=feed.months[ym];if(!rec)return null;
  return {ym,rec,note:feed.note,isCurrent:ym===cy};
}
/** SCC renderStatutoryPayablePanel figures (L1862–1868), whole rupees. */
export function cmpPayablePanel(feed:CMPPayableFeed|undefined,today:string){
  const sp=cmpStatutoryPayable(feed,today);
  const r=(p:number|undefined)=>Math.round((Number(p)||0)/100);
  const pf=sp?r(sp.rec.pf):0,esic=sp?r(sp.rec.esic):0,pt=sp?r(sp.rec.pt):0,gst=sp?r(sp.rec.gstEstimate):0,total=pf+esic+pt+gst;
  return {sp,pf,esic,pt,gst,total,empty:!sp||total<=0,chip:sp?cmpYmLabel(sp.ym)+(sp.isCurrent?'':' (latest available)'):''};
}
/** SCC renderStatutoryPayableCard (L1884–1895): the strip on the business month's page only, when that month has an entry and total > 0. */
export function cmpPayableStrip(feed:CMPPayableFeed|undefined,today:string,month:string){
  const p=cmpPayablePanel(feed,today);if(!p.sp||!p.sp.isCurrent)return null;
  if(month!==MONTH_NAMES[+p.sp.ym.slice(5,7)-1])return null;if(p.total<=0)return null;return p;
}
/** SCC fmtINR (L1840): ₹ with Indian grouping. */
export const cmpInr=(rupees:number)=>'₹'+Math.round(Number(rupees)||0).toLocaleString('en-IN');

// ── CA pack (HCC's simulated pack; SCC sends its own WhatsApp "ca_pack" template, not supplied — owner question 10) ──
export function cmpPackCounts(s:State){
  const x=cmpSlice(s),y=x.fyStart;let done=0,na=0,open=0,overdue=0;
  for(const m of CMP_MONTHS)for(const c of cmpMonthCards(x,m,y)){if(cmpIsNA(x,y,c.id)){na++;continue;}if(cmpIsDone(x,y,c.id)){done++;continue;}open++;
    const d=cmpDaysLeft(c.due,y,s.date);if(d!==null&&d<0)overdue++;}
  const ac=cmpActionCenter(s);return {overdue,due:ac.filter(r=>r.days>=0).length,filed:done,na,open};
}
export function cmpPackLines(s:State):string[]{
  const x=cmpSlice(s),y=x.fyStart,c=cmpPackCounts(s),lines:string[]=[];
  lines.push(`Position as on ${s.date} (${cmpFyLabel(y)}): ${c.filed} done, ${c.open} pending (${c.overdue} overdue), ${c.na} not applicable.`);
  for(const r of cmpActionCenter(s))lines.push(`OPEN · ${cmpStripHtml(cmpRenderFYText(r.name,y))} · ${r.month} · due ${r.dueIso??r.due} · ${cmpWhen(r.days)}`);
  for(const m of CMP_MONTHS)for(const it of cmpMonthCards(x,m,y)){const rec=cmpRecord(x,y,it.id);
    if(rec?.done&&!rec.na&&rec.filedOn&&nights(rec.filedOn,s.date)>=0&&nights(rec.filedOn,s.date)<=35)
      lines.push(`FILED · ${cmpStripHtml(cmpRenderFYText(it.name,y))} · filed ${rec.filedOn}${rec.evidenceRef?' · ref '+rec.evidenceRef:''}`);}
  return lines.slice(0,60);
}

// ── Settings for the Master configuration hub ──
export type CMPSettingDef={key:string;category:string;label:string;type:'text'|'integer'|'fixed';unit:string;editable:boolean;approval:string;effective:string;help:string;value:string|number};
export function cmpSettingDefs(s:State):CMPSettingDef[]{
  const x=cmpSlice(s),OWNER='Owner (approve permission)',f=x.profile,where='Edit in Compliance calendar › Firm profile.';
  return [
    {key:'cmp.caName',category:'Compliance obligations',label:'CA / consultant name',type:'text',unit:'text',editable:true,approval:OWNER,effective:'Immediately',help:'Shown on the CA pack. Fictional in the prototype.',value:x.settings.caName},
    {key:'cmp.caEmail',category:'Compliance obligations',label:'CA e-mail (simulated send)',type:'text',unit:'e-mail',editable:true,approval:OWNER,effective:'Immediately',help:'The CA pack is a simulation: nothing is e-mailed.',value:x.settings.caEmail},
    {key:'cmp.dueSoonDays',category:'Compliance obligations',label:'“Due this week” window',type:'integer',unit:'days',editable:true,approval:OWNER,effective:'Immediately',help:'HCC extra: obligations due within this many days show on the Dashboard card. The Action Center and badges use SCC’s 30 days.',value:x.settings.dueSoonDays},
    {key:'cmp.profile.gstType',category:'Compliance obligations',label:'GST registration type (SCC firm profile)',type:'fixed',unit:'profile',editable:true,approval:OWNER,effective:'Immediately',help:'Decides which GST / composition cards apply (SCC). '+where,value:f?cmpTypeLabel(f.gstType)+(f.qrmp?' · QRMP':''):'Not set'},
    {key:'cmp.profile.hasEmployees',category:'Compliance obligations',label:'Has employees (SCC firm profile)',type:'fixed',unit:'profile',editable:true,approval:OWNER,effective:'Immediately',help:'No employees hides the payroll filings (EPF, ESIC, PT deduction, LWF), as SCC. '+where,value:f?(f.hasEmployees===false?'No':'Yes'):'Not set'},
    {key:'cmp.catalogue',category:'Compliance obligations',label:'SCC compliance catalogue',type:'fixed',unit:'catalogue',editable:true,approval:OWNER,effective:'Immediately',help:'Built-in cards cannot be edited (SCC): archive an obligation or a category, or add your own, in Compliance calendar › Manage compliance.',value:`113 cards · 30 obligations · ${x.custom.length} custom · ${x.archived.length+x.archivedCats.length} archived`},
  ];
}
// Dates helper re-exported for the UI (internal deadline).
export const cmpShortDate=(iso:string|null)=>{if(!iso)return 'NA';const M=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];return `${iso.slice(8,10)} ${M[+iso.slice(5,7)-1]}`;};
export const cmpAddDays=addDays;
