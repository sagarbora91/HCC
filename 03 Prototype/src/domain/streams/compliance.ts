// Wave 0.5 · W05-C Compliance calendar — pure helpers, selectors and the state slice type. Exported names MUST start with 'cmp' / 'CMP' (shared mock namespace).
// Due-date rules ported (rules only, not code) from SCC www/modules/tax/index.html (compliance templates): TDS deposit = 7th of the next month
// (March deductions: 30 April), GSTR-1 = 11th, GSTR-3B = 20th, EPF = 15th, ESIC = 15th, Professional Tax (Maharashtra) = last day of the next month,
// GSTR-9 annual = 31 December after the financial year. Illustrative — CA to confirm for a hotel. Rules are DATED settings (effective-from), never constants.
import type {State} from '../types';
import {can} from '../selectors';
import {nights} from '../money';

export type CMPKind='TDS'|'GSTR-1'|'GSTR-3B'|'GSTR-9'|'EPF'|'ESIC'|'PT';
export const CMP_KINDS:CMPKind[]=['TDS','GSTR-1','GSTR-3B','GSTR-9','EPF','ESIC','PT'];
export type CMPFreq='Monthly'|'Annual';
/** Due-date rule. 'day': that day of the month after the period (optional special-month override); 'month-end': last day of the month after the period; 'fixed': the first such month/day after the financial year ends (annual). */
export type CMPRule={type:'day';day:number;special?:{month:number;day:number}}|{type:'month-end'}|{type:'fixed';month:number;day:number};
export type CMPDatedRule={from:string;rule:CMPRule};
export type CMPObligation={id:string;kind:CMPKind;name:string;form:string;freq:CMPFreq;rules:CMPDatedRule[];
  /** Role responsible (shown on the row). */owner:string;
  /** First period that is tracked: 'YYYY-MM' (monthly) or 'YYYY-YY' (annual FY, e.g. 2025-26). */from:string;active:boolean;note?:string;updatedBy?:string;updatedAt?:string};
export type CMPFiling={obligation:string;period:string;status:'Filed'|'Not applicable';by:string;at:string;reference?:string;reason?:string};
export type CMPPack={id:string;at:string;by:string;to:string;toName:string;counts:{overdue:number;due:number;filed:number;na:number};lines:string[]};
export type CMPSettings={caName:string;caEmail:string;/** "Due this week" window in days. */dueSoonDays:number};
/** State slice, stored at s.complianceX (optional; absent in old saves → treated as empty). */
/** A filing that the Owner reopened: the original record is kept (W05 review F14), with who reopened it, when and why. */
export type CMPReopened=CMPFiling&{reopenedBy:string;reopenedAt:string;reopenReason:string};
export type CMPState={obligations:CMPObligation[];filings:CMPFiling[];packs:CMPPack[];settings:CMPSettings;/** Absent until the first reopen. */reopened?:CMPReopened[]};
export const CMP_MAX_REOPENED=200;
export const CMP_DEFAULT_SETTINGS:CMPSettings={caName:'Rao & Associates (fictional)',caEmail:'ca.desk@example.test',dueSoonDays:7};
export const CMP_MAX_PACKS=20;
export const CMP_ILLUSTRATIVE='Illustrative due dates — CA to confirm for a hotel.';
export const cmpEmpty=():CMPState=>({obligations:[],filings:[],packs:[],settings:{...CMP_DEFAULT_SETTINGS}});
export const cmpSlice=(s:State):CMPState=>{const x=s.complianceX as Partial<CMPState>|undefined;
  return {obligations:x?.obligations??[],filings:x?.filings??[],packs:x?.packs??[],settings:{...CMP_DEFAULT_SETTINGS,...(x?.settings??{})},...(x?.reopened?{reopened:x.reopened}:{})};};

// ── Access: Owner and Accountant only (DEC-055). Managing obligations needs 'approve' (Owner). ──
const roleOf=(s:State,uid:string)=>s.users.find(u=>u.id===uid)?.role;
export const cmpHasAccess=(s:State,uid:string)=>{const r=roleOf(s,uid);return (r==='Owner'||r==='Accountant')&&can(s,uid,'reports');};
export const cmpCanManage=(s:State,uid:string)=>cmpHasAccess(s,uid)&&can(s,uid,'approve');

// ── Dates ──
const pad=(n:number)=>String(n).padStart(2,'0');
const lastDayOf=(y:number,m:number)=>new Date(Date.UTC(y,m,0)).getUTCDate(); // m = 1..12
const MONTHS=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
export const cmpMonthAdd=(p:string,n:number)=>{const y=+p.slice(0,4),m=+p.slice(5,7)-1+n;const yy=y+Math.floor(m/12),mm=((m%12)+12)%12;return `${yy}-${pad(mm+1)}`;};
export const cmpMonthEnd=(p:string)=>`${p}-${pad(lastDayOf(+p.slice(0,4),+p.slice(5,7)))}`;
export const cmpValidDate=(d:string)=>/^\d{4}-\d{2}-\d{2}$/.test(d)&&new Date(d+'T00:00:00Z').toISOString().slice(0,10)===d;
/** Financial-year label: 'YYYY-YY' from its start year. */
export const cmpFyLabel=(y:number)=>`${y}-${pad((y+1)%100)}`;
export const cmpFyStartYear=(p:string)=>+p.slice(0,4);
export function cmpPeriodLabel(o:Pick<CMPObligation,'freq'>,p:string){return o.freq==='Annual'?`FY ${p}`:`${MONTHS[+p.slice(5,7)-1]} ${p.slice(0,4)}`;}
/** First and last calendar day of a period. */
export const cmpPeriodStart=(o:Pick<CMPObligation,'freq'>,p:string)=>o.freq==='Annual'?`${cmpFyStartYear(p)}-04-01`:`${p}-01`;
export const cmpPeriodEnd=(o:Pick<CMPObligation,'freq'>,p:string)=>o.freq==='Annual'?`${cmpFyStartYear(p)+1}-03-31`:cmpMonthEnd(p);
/** Periods tracked from `o.from` up to the latest period that has ended before `today`. */
export function cmpPeriods(o:Pick<CMPObligation,'freq'|'from'>,today:string):string[]{
  const out:string[]=[];
  if(o.freq==='Annual'){for(let y=cmpFyStartYear(o.from);y<cmpFyStartYear(o.from)+30;y++){const p=cmpFyLabel(y);if(cmpPeriodEnd(o,p)>=today)break;out.push(p);}return out;}
  for(let p=o.from,n=0;n<240&&cmpMonthEnd(p)<today;p=cmpMonthAdd(p,1),n++)out.push(p);
  return out;}
/** The rule valid for a period: the latest dated rule whose effective-from is on or before the period's first day. */
export function cmpRuleFor(o:Pick<CMPObligation,'freq'|'rules'>,p:string):CMPRule{
  const start=cmpPeriodStart(o,p),ok=o.rules.filter(r=>r.from<=start).sort((a,b)=>a.from.localeCompare(b.from));
  return (ok.length?ok[ok.length-1]:[...o.rules].sort((a,b)=>a.from.localeCompare(b.from))[0]).rule;}
export function cmpDueDate(o:Pick<CMPObligation,'freq'|'rules'>,p:string):string{
  const r=cmpRuleFor(o,p);
  // F5: 'after the financial year' — Apr–Dec fall in the calendar year the FY ends; Jan–Mar in the year after (never before the FY has ended).
  if(r.type==='fixed'){const y=cmpFyStartYear(p)+(r.month<=3?2:1);return `${y}-${pad(r.month)}-${pad(Math.min(r.day,lastDayOf(y,r.month)))}`;}
  const nm=cmpMonthAdd(p,1),y=+nm.slice(0,4),m=+nm.slice(5,7);
  if(r.type==='month-end')return `${nm}-${pad(lastDayOf(y,m))}`;
  const day=r.special&&+p.slice(5,7)===r.special.month?r.special.day:r.day;
  return `${nm}-${pad(Math.min(day,lastDayOf(y,m)))}`;}
export function cmpRuleText(r:CMPRule){
  if(r.type==='month-end')return 'Last day of the next month';
  if(r.type==='fixed')return `${r.day} ${MONTHS[r.month-1]} after the financial year`;
  return `${r.day}${r.day%10===1&&r.day!==11?'st':r.day%10===2&&r.day!==12?'nd':r.day%10===3&&r.day!==13?'rd':'th'} of the next month${r.special?` (${MONTHS[r.special.month-1]} period: ${r.special.day} ${MONTHS[r.special.month%12]})`:''}`;}

// ── Items (one row per obligation × period) ──
export type CMPStatus='Overdue'|'Due today'|'Due soon'|'Upcoming'|'Filed'|'Not applicable';
export type CMPItem={key:string;obligation:CMPObligation;period:string;periodLabel:string;due:string;days:number;status:CMPStatus;filing?:CMPFiling};
export const cmpKey=(obligation:string,period:string)=>`${obligation}:${period}`;
/** Every tracked period of every active obligation, soonest due date first. */
export function cmpItems(s:State,includeInactive=false):CMPItem[]{
  const x=cmpSlice(s),byKey=new Map(x.filings.map(f=>[cmpKey(f.obligation,f.period),f]));
  const out:CMPItem[]=[];
  for(const o of x.obligations){
    if(!o.active&&!includeInactive)continue;
    for(const p of cmpPeriods(o,s.date)){
      const key=cmpKey(o.id,p),filing=byKey.get(key),due=cmpDueDate(o,p),days=nights(s.date,due);
      const status:CMPStatus=filing?filing.status:days<0?'Overdue':days===0?'Due today':days<=x.settings.dueSoonDays?'Due soon':'Upcoming';
      out.push({key,obligation:o,period:p,periodLabel:cmpPeriodLabel(o,p),due,days,status,filing});
    }}
  return out.sort((a,b)=>a.due.localeCompare(b.due)||a.obligation.name.localeCompare(b.obligation.name));}
export const cmpIsOpen=(i:CMPItem)=>i.status!=='Filed'&&i.status!=='Not applicable';
export function cmpStatusText(i:CMPItem){
  if(i.status==='Overdue')return `Overdue by ${-i.days} day${-i.days===1?'':'s'}`;
  if(i.status==='Due today')return 'Due today';
  if(i.status==='Filed'||i.status==='Not applicable')return i.status;
  return `Due in ${i.days} day${i.days===1?'':'s'}`;}
export type CMPCounts={overdue:number;due:number;upcoming:number;filed:number;na:number;open:number};
export function cmpCounts(items:CMPItem[]):CMPCounts{
  const c:CMPCounts={overdue:0,due:0,upcoming:0,filed:0,na:0,open:0};
  for(const i of items){if(i.status==='Overdue')c.overdue++;else if(i.status==='Due today'||i.status==='Due soon')c.due++;else if(i.status==='Upcoming')c.upcoming++;else if(i.status==='Filed')c.filed++;else c.na++;if(cmpIsOpen(i))c.open++;}
  return c;}
/** Overdue plus everything due inside the "due this week" window, most urgent first. */
export const cmpDueThisWeek=(s:State)=>cmpItems(s).filter(i=>i.status==='Overdue'||i.status==='Due today'||i.status==='Due soon');
/** Badge on the Accounts tile: only for users who may open the calendar. */
export const cmpBadge=(s:State,uid:string)=>cmpHasAccess(s,uid)?cmpDueThisWeek(s).length:0;

// ── CA pack (simulated e-mail: nothing is sent) ──
export function cmpPackLines(s:State):string[]{
  const items=cmpItems(s),lines:string[]=[];
  const open=items.filter(cmpIsOpen);
  lines.push(`Position as on ${s.date}: ${items.filter(i=>i.status==='Filed').length} filed, ${open.length} open, ${items.filter(i=>i.status==='Not applicable').length} not applicable.`);
  for(const i of open)lines.push(`OPEN · ${i.obligation.name} · ${i.periodLabel} · due ${i.due} · ${cmpStatusText(i)}`);
  const recent=items.filter(i=>i.status==='Filed'&&i.filing&&nights(i.filing.at.slice(0,10),s.date)<=35);
  for(const i of recent)lines.push(`FILED · ${i.obligation.name} · ${i.periodLabel} · ref ${i.filing!.reference} · ${i.filing!.at.slice(0,10)}`);
  return lines.slice(0,60);}

// ── Settings for the Master configuration hub (W05-A wires these in later) ──
export type CMPSettingDef={key:string;category:string;label:string;type:'text'|'integer'|'fixed';unit:string;editable:boolean;approval:string;effective:string;help:string;value:string|number};
export function cmpSettingDefs(s:State):CMPSettingDef[]{
  const x=cmpSlice(s),OWNER='Owner (approve permission)';
  const defs:CMPSettingDef[]=[
    {key:'cmp.caName',category:'Compliance obligations',label:'CA / consultant name',type:'text',unit:'text',editable:true,approval:OWNER,effective:'Immediately',help:'Shown on the CA pack. Fictional in the prototype.',value:x.settings.caName},
    {key:'cmp.caEmail',category:'Compliance obligations',label:'CA e-mail (simulated send)',type:'text',unit:'e-mail',editable:true,approval:OWNER,effective:'Immediately',help:'The CA pack is a simulation: nothing is e-mailed.',value:x.settings.caEmail},
    {key:'cmp.dueSoonDays',category:'Compliance obligations',label:'“Due this week” window',type:'integer',unit:'days',editable:true,approval:OWNER,effective:'Immediately',help:'Obligations due within this many days show on the Dashboard and the Accounts tile.',value:x.settings.dueSoonDays}];
  for(const o of x.obligations)defs.push({key:'cmp.rule.'+o.id,category:'Compliance obligations',label:`${o.name} — due rule`,type:'fixed',unit:'dated rule',editable:true,approval:OWNER,
    effective:'From a chosen date; earlier periods keep their rule',help:`${CMP_ILLUSTRATIVE} Edit in Compliance calendar › Manage obligations.`,value:cmpRuleText(cmpRuleFor(o,cmpMonthAdd(s.date.slice(0,7),-1)))+(o.active?'':' · switched off')});
  return defs;}

