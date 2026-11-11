// Module 18 · Guest services, complaints, lost & found — pure helpers, selectors and the state slice type. Exported names MUST start with 'gsv' / 'GSV' (shared mock namespace).
import type {State,Role,User} from '../types';
import {addMinutes,addDays,nights} from '../money';
import {can} from '../selectors';
import {mstRows,mstByCode,type MasterRow} from './masters';
import {SEED_COMPLAINT_CATEGORIES,SEED_LF_CATEGORIES} from '../../fixtures/ext/masters-seed';

export type GSVLog={at:string;by:string;what:string};
export const GSV_REQUEST_TYPES=['Extra towels','Drinking water','Iron & board','Wake-up call','Taxi','Toiletries & amenities','Minor repair','Other'] as const;
export type GSVRequestType=typeof GSV_REQUEST_TYPES[number];
export const GSV_DEPTS=['Housekeeping','Maintenance','Front desk'] as const;
export type GSVDept=typeof GSV_DEPTS[number];
export type GSVRequestStatus='Open'|'Assigned'|'In progress'|'Done'|'Cancelled';
export type GSVRequest={id:string;booking:string;room:string;guest:string;type:GSVRequestType;detail:string;dept:GSVDept;assignee?:string;
  priority:'Normal'|'Urgent';status:GSVRequestStatus;createdAt:string;dueAt:string;scheduledFor?:string;startedAt?:string;doneAt?:string;
  /** Guest-facing completion note (shown to the guest; simulated message). */
  note?:string;cancelReason?:string;by:string;log:GSVLog[];
  /** LF §9.1 B3: wake-up time YYYY-MM-DDTHH:mm (type 'Wake-up call' only). */ wakeAt?:string};

export const GSV_COMPLAINT_CATEGORIES=['Room cleanliness','Maintenance','Noise','Staff behaviour','Food & beverage','Billing','Amenities','Other'] as const;
export type GSVComplaintCategory=typeof GSV_COMPLAINT_CATEGORIES[number];
export const GSV_SEVERITIES=['Low','Medium','High','Critical'] as const;
export type GSVSeverity=typeof GSV_SEVERITIES[number];
export type GSVComplaintStatus='Open'|'Investigating'|'Resolved'|'Closed';
export type GSVRecovery={amount:number;reason:string;approvalId?:string;lineId?:string;at:string;by:string};
/** MA-7: category = the category name when logged (kept as shown then); categoryCode = the master row's code (complaintCategories), absent on older records. */
export type GSVComplaint={id:string;booking?:string;room?:string;guest:string;category:string;categoryCode?:string;severity:GSVSeverity;description:string;
  owner:string;status:GSVComplaintStatus;rootCause?:string;recoveryAction?:string;recovery?:GSVRecovery;followUpOn?:string;
  followUp?:{outcome:string;satisfied:boolean;at:string;by:string};createdAt:string;resolvedAt?:string;closedAt?:string;by:string;log:GSVLog[]};

export const GSV_ITEM_CATEGORIES=['Jewellery','Electronics','Cash / wallet','Documents','Clothing','Personal care','Other'] as const;
export type GSVItemCategory=typeof GSV_ITEM_CATEGORIES[number];
export const GSV_VALUABLE:GSVItemCategory[]=['Jewellery','Electronics','Cash / wallet','Documents'];
export const GSV_STORAGE=['Front office safe','Housekeeping store','Back office cupboard'] as const;
export const GSV_SAFE='Front office safe';
export const GSV_ID_TYPES=['Aadhaar (fictional)','Passport (fictional)','Driving licence (fictional)','Voter ID (fictional)'] as const;
export const GSV_DISPOSAL=['Donated','Destroyed','Handed to police'] as const;
export type GSVItemStatus='Stored'|'Claimed'|'Handed over'|'Disposed';
export type GSVItem={id:string;description:string;category:string;categoryCode?:string;valuable:boolean;foundWhere:string;room?:string;foundAt:string;foundBy:string;
  storage:string;booking?:string;status:GSVItemStatus;
  claim?:{name:string;phone:string;idType:string;idNote:string;matchNote:string;at:string;by:string};
  handover?:{at:string;by:string;to:string;note:string};
  disposal?:{at:string;by:string;method:string;note:string};log:GSVLog[]};

export type GSVSettings={sla:Record<GSVRequestType,number>;retentionDays:number;valuableRetentionDays:number};
export const GSV_DEFAULT_SETTINGS:GSVSettings={sla:{'Extra towels':20,'Drinking water':10,'Iron & board':15,'Wake-up call':5,'Taxi':30,'Toiletries & amenities':20,'Minor repair':45,'Other':30},retentionDays:90,valuableRetentionDays:180};

/** State slice for this module, stored at s.servicesX (optional; absent in old saves → treat as empty). */
export type GSVState={requests:GSVRequest[];complaints:GSVComplaint[];items:GSVItem[];settings?:GSVSettings};
export const gsvEmpty=():GSVState=>({requests:[],complaints:[],items:[]});
export const gsvSlice=(s:State):GSVState=>{const x=s.servicesX as Partial<GSVState>|undefined;const y:GSVState={requests:x?.requests??[],complaints:x?.complaints??[],items:x?.items??[]};if(x?.settings)y.settings=x.settings;return y;};
/** Ensure a mutable, complete slice on a (cloned) state. */
export function gsvEnsure(s:State):GSVState{const x=gsvSlice(s);s.servicesX=x;return x;}
export const gsvSettings=(s:State):GSVSettings=>{const x=gsvSlice(s).settings;return {...GSV_DEFAULT_SETTINGS,...x,sla:{...GSV_DEFAULT_SETTINGS.sla,...x?.sla}};};

// ── Requests ──
export const gsvDefaultDept=(t:GSVRequestType):GSVDept=>t==='Minor repair'?'Maintenance':['Wake-up call','Taxi','Other'].includes(t)?'Front desk':'Housekeeping';
export const gsvDeptRoles:Record<GSVDept,Role[]>={Housekeeping:['Housekeeper','Supervisor'],Maintenance:['Maintenance'],'Front desk':['Reception','Manager','Owner']};
export const gsvAssignable=(s:State,dept:GSVDept):User[]=>s.users.filter(u=>u.enabled&&gsvDeptRoles[dept].includes(u.role));
/** SLA minutes for a type; Urgent halves it (minimum 5). */
export function gsvSlaMinutes(s:State,type:GSVRequestType,priority:'Normal'|'Urgent'='Normal'){const m=gsvSettings(s).sla[type]??30;return priority==='Urgent'?Math.max(5,Math.round(m/2)):m;}
export const gsvMinutesBetween=(a:string,b:string)=>Math.round((Date.parse(b+':00Z')-Date.parse(a+':00Z'))/60000);
export const gsvAddMinutes=addMinutes;
export const gsvIsOpen=(r:GSVRequest)=>r.status!=='Done'&&r.status!=='Cancelled';
export const gsvOverdue=(s:State,r:GSVRequest)=>gsvIsOpen(r)&&s.clock>r.dueAt;
export const gsvLate=(r:GSVRequest)=>r.status==='Done'&&!!r.doneAt&&r.doneAt>r.dueAt;
/** Minutes left (negative = overdue) for an open request; undefined when finished. */
export const gsvMinutesLeft=(s:State,r:GSVRequest)=>gsvIsOpen(r)?gsvMinutesBetween(s.clock,r.dueAt):undefined;
export type GSVSla={tone:'green'|'orange'|'red'|'neutral'|'blue';label:string};
export function gsvSlaLabel(s:State,r:GSVRequest):GSVSla{
  if(r.status==='Cancelled')return {tone:'neutral',label:'Cancelled'};
  if(r.status==='Done'){const m=gsvMinutesBetween(r.createdAt,r.doneAt!);return gsvLate(r)?{tone:'orange',label:`Done late · ${m} min`}:{tone:'green',label:`Within SLA · ${m} min`};}
  if(r.scheduledFor&&s.clock<r.scheduledFor&&gsvMinutesBetween(s.clock,r.scheduledFor)>60)return {tone:'blue',label:'Scheduled '+r.scheduledFor.slice(11,16)};
  const left=gsvMinutesLeft(s,r)!;
  if(left<0)return {tone:'red',label:`Overdue · ${-left} min`};
  if(left<=5)return {tone:'orange',label:`Due in ${left} min`};
  return {tone:'blue',label:`Due in ${left} min`};
}
/** Whether a user may start/complete a request (UI and handler share this rule). */
export function gsvCanWork(s:State,uid:string,r:GSVRequest){
  const u=s.users.find(x=>x.id===uid);if(!u)return false;
  if(u.role==='Housekeeper'||u.role==='Maintenance')return can(s,uid,'housekeeping')&&r.assignee===uid;
  if(can(s,uid,'reservations'))return true;
  return can(s,uid,'inspect')&&r.dept!=='Front desk';
}
export const gsvMyRequests=(s:State,uid:string)=>gsvSlice(s).requests.filter(r=>r.assignee===uid);
/** Requests a user sees on "My guest requests": own assignments; supervisors (inspect) see all HK/maintenance requests. */
export function gsvVisibleOps(s:State,uid:string){const u=s.users.find(x=>x.id===uid);const all=gsvSlice(s).requests;
  if(!u)return [];if(u.role==='Housekeeper'||u.role==='Maintenance')return all.filter(r=>r.assignee===uid);
  if(can(s,uid,'reservations'))return all;return all.filter(r=>r.dept!=='Front desk');}

// ── Complaints ──
export const gsvComplaintOpen=(c:GSVComplaint)=>c.status!=='Closed';
export const gsvFollowUpDue=(s:State,c:GSVComplaint)=>c.status==='Resolved'&&!!c.followUpOn&&c.followUpOn<=s.date;
export type GSVRecoveryStatus='None'|'Posted'|'Pending approval'|'Approved'|'Rejected'|'Other';
export function gsvRecoveryStatus(s:State,c:GSVComplaint):GSVRecoveryStatus{
  if(!c.recovery)return 'None';if(c.recovery.lineId)return 'Posted';
  const a=s.approvals.find(a=>a.id===c.recovery!.approvalId);if(!a)return 'Other';
  return a.status==='Pending'?'Pending approval':a.status==='Approved'||a.status==='Executed'?'Approved':a.status==='Rejected'?'Rejected':'Other';
}

// ── Lost & found ──
export const gsvIsValuable=(c:GSVItemCategory)=>GSV_VALUABLE.includes(c);
export const gsvRetentionDays=(s:State,i:GSVItem)=>i.valuable?gsvSettings(s).valuableRetentionDays:gsvSettings(s).retentionDays;
export const gsvItemAgeDays=(s:State,i:GSVItem)=>nights(i.foundAt.slice(0,10),s.date);
export const gsvDisposeOn=(s:State,i:GSVItem)=>addDays(i.foundAt.slice(0,10),gsvRetentionDays(s,i));
export const gsvDisposable=(s:State,i:GSVItem)=>i.status==='Stored'&&s.date>=gsvDisposeOn(s,i);
/** ID check note keeps only a short reference — rejects anything that looks like a full ID number (≥ 6 digits in a row). */
export const gsvIdNoteOk=(n:string)=>n.trim().length>=3&&!/\d{6,}/.test(n.replace(/[\s-]/g,''));

// ── Reports ──
export type GSVResponseRow={key:string;count:number;done:number;avg:number;withinSla:number;pct:number;open:number;overdue:number};
export function gsvResponseReport(s:State,by:'type'|'dept'='type'):GSVResponseRow[]{
  const all=gsvSlice(s).requests.filter(r=>r.status!=='Cancelled');
  const keys=by==='type'?[...GSV_REQUEST_TYPES]:[...GSV_DEPTS];
  return keys.map(k=>{const rs=all.filter(r=>(by==='type'?r.type:r.dept)===k),done=rs.filter(r=>r.status==='Done');
    const mins=done.map(r=>gsvMinutesBetween(r.createdAt,r.doneAt!)),within=done.filter(r=>!gsvLate(r)).length;
    return {key:k,count:rs.length,done:done.length,avg:mins.length?Math.round(mins.reduce((a,b)=>a+b,0)/mins.length):0,withinSla:within,pct:done.length?Math.round(within*100/done.length):0,
      open:rs.filter(gsvIsOpen).length,overdue:rs.filter(r=>gsvOverdue(s,r)).length};}).filter(r=>r.count>0);
}
export function gsvResponseTotals(s:State){const rows=gsvResponseReport(s,'dept');const done=rows.reduce((a,r)=>a+r.done,0),within=rows.reduce((a,r)=>a+r.withinSla,0);
  const all=gsvSlice(s).requests.filter(r=>r.status==='Done');const mins=all.map(r=>gsvMinutesBetween(r.createdAt,r.doneAt!));
  return {done,within,pct:done?Math.round(within*100/done):0,avg:mins.length?Math.round(mins.reduce((a,b)=>a+b,0)/mins.length):0,
    open:rows.reduce((a,r)=>a+r.open,0),overdue:rows.reduce((a,r)=>a+r.overdue,0)};}
export type GSVCategoryRow={category:string;code:string;dept?:string;slaHours?:number;total:number;open:number;bySeverity:Record<GSVSeverity,number>;recovery:number;overSla:number};
/** Complaints by category (MA-7: the complaintCategories master, retired rows included; records of an unknown category are listed under their own name). */
export function gsvComplaintReport(s:State):GSVCategoryRow[]{
  const cs=gsvSlice(s).complaints;
  const rows:{key:string;label:string;dept?:string;hours?:number}[]=gcatRows(s).map(r=>({key:r.code,label:r.name,dept:r.department,hours:r.slaHours}));
  for(const c of cs){const k=gcatKeyOf(s,c);if(!rows.some(r=>r.key===k))rows.push({key:k,label:c.category});}
  return rows.map(r=>{const xs=cs.filter(c=>gcatKeyOf(s,c)===r.key);
    const bySeverity=Object.fromEntries(GSV_SEVERITIES.map(v=>[v,xs.filter(c=>c.severity===v).length])) as Record<GSVSeverity,number>;
    return {category:r.label,code:r.key,...(r.dept?{dept:r.dept}:{}),...(r.hours?{slaHours:r.hours}:{}),total:xs.length,open:xs.filter(gsvComplaintOpen).length,bySeverity,recovery:xs.filter(c=>c.recovery).length,overSla:xs.filter(c=>gcatOverSla(s,c)).length};}).filter(r=>r.total>0);
}
export function gsvLostFoundSummary(s:State){const xs=gsvSlice(s).items;
  return {stored:xs.filter(i=>i.status==='Stored').length,claimed:xs.filter(i=>i.status==='Claimed').length,handed:xs.filter(i=>i.status==='Handed over').length,
    disposed:xs.filter(i=>i.status==='Disposed').length,dueDisposal:xs.filter(i=>gsvDisposable(s,i)).length,valuables:xs.filter(i=>i.valuable&&(i.status==='Stored'||i.status==='Claimed')).length};}

// ── MA-7 · Complaint and lost-and-found categories (masters complaintCategories / lostFoundCategories; specs in gsvmasters.ts) ──
export const GCC='complaintCategories',LFC='lostFoundCategories';
export type GcatRow=MasterRow&{department:string;slaHours:number};
export type LfcRow=GcatRow&{valuable:boolean};
/** Fixture seed: today's eight complaint categories (code, name, department, SLA hours — illustrative targets). */
export const GCC_SEED:[string,string,GSVDept,number][]=SEED_COMPLAINT_CATEGORIES;
/** Fixture seed: today's seven found-item categories (valuables are held in the safe; SLA = hours to trace and inform the owner). */
export const LFC_SEED:[string,string,GSVDept,number,boolean][]=SEED_LF_CATEGORIES;
export const gcatRows=(s:State,active?:boolean)=>mstRows(s,GCC,active===undefined?{}:{active}) as GcatRow[];
export const lfcRows=(s:State,active?:boolean)=>mstRows(s,LFC,active===undefined?{}:{active}) as LfcRow[];
const norm=(x:unknown)=>String(x??'').trim().toLowerCase();
function resolve<T extends MasterRow>(rows:T[],input:unknown,activeOnly:boolean){
  const k=norm(input);if(!k)return undefined;const ok=(r:T)=>!activeOnly||r.active;
  return rows.find(r=>ok(r)&&norm(r.code)===k)??rows.find(r=>ok(r)&&norm(r.name)===k);
}
/** Category row of a command value: a code, or (older callers) a name. activeOnly = for new records. */
export const gcatResolve=(s:State,input:unknown,activeOnly=true)=>resolve(gcatRows(s),input,activeOnly);
export const lfcResolve=(s:State,input:unknown,activeOnly=true)=>resolve(lfcRows(s),input,activeOnly);
/** Code of a record's category: its stored code, else the seed code of its name, else the name itself. */
export const gcatKeyOf=(_s:State,c:Pick<GSVComplaint,'category'|'categoryCode'>)=>c.categoryCode??GCC_SEED.find(x=>x[1]===c.category)?.[0]??c.category;
export const lfcKeyOf=(i:Pick<GSVItem,'category'|'categoryCode'>)=>i.categoryCode??LFC_SEED.find(x=>x[1]===i.category)?.[0]??i.category;
/** Category as it reads today (a renamed or retired row still resolves; unknown → the name stored on the record). */
export const gcatLabel=(s:State,c:Pick<GSVComplaint,'category'|'categoryCode'>)=>mstByCode(s,GCC,gcatKeyOf(s,c))?.name??c.category;
export const lfcLabel=(s:State,i:Pick<GSVItem,'category'|'categoryCode'>)=>mstByCode(s,LFC,lfcKeyOf(i))?.name??i.category;
export const gcatRowOf=(s:State,c:Pick<GSVComplaint,'category'|'categoryCode'>)=>mstByCode(s,GCC,gcatKeyOf(s,c)) as GcatRow|undefined;
export const lfcRowOf=(s:State,i:Pick<GSVItem,'category'|'categoryCode'>)=>mstByCode(s,LFC,lfcKeyOf(i)) as LfcRow|undefined;
/** Is a found-item category a valuable one (safe storage)? Name or code; unknown → falls back to the built-in valuable list. */
export function gsvItemValuable(s:State,category:string):boolean{
  const r=lfcResolve(s,category,false);return r?!!r.valuable:gsvIsValuable(category as GSVItemCategory);
}
/** Complaint answer-by time (created + the category's SLA hours); undefined when the category has none. */
export function gcatSlaDue(s:State,c:GSVComplaint):string|undefined{
  const h=gcatRowOf(s,c)?.slaHours;return h?addMinutes(c.createdAt,h*60):undefined;
}
/** Over SLA: still open past the due time, or resolved after it. */
export function gcatOverSla(s:State,c:GSVComplaint):boolean{
  const due=gcatSlaDue(s,c);if(!due)return false;
  if(c.status==='Open'||c.status==='Investigating')return s.clock>due;
  return !!c.resolvedAt&&c.resolvedAt>due;
}
export function gcatSlaLabel(s:State,c:GSVComplaint):GSVSla|undefined{
  const due=gcatSlaDue(s,c);if(!due)return undefined;
  if(c.status==='Open'||c.status==='Investigating'){const left=gsvMinutesBetween(s.clock,due);
    return left<0?{tone:'red',label:`Over SLA · ${-left>=120?Math.round(-left/60)+' h':-left+' min'}`}:{tone:left<=60?'orange':'blue',label:`SLA due ${due.slice(11,16)}${due.slice(0,10)===s.date?'':' '+due.slice(5,10)}`};}
  return gcatOverSla(s,c)?{tone:'orange',label:'Resolved after SLA'}:{tone:'green',label:'Within SLA'};
}
/** Found-item contact target (found + the category's SLA hours). */
export function lfcContactBy(s:State,i:GSVItem):string|undefined{const h=lfcRowOf(s,i)?.slaHours;return h?addMinutes(i.foundAt,h*60):undefined;}
export type GSVItemCategoryRow={category:string;code:string;dept?:string;slaHours?:number;valuable:boolean;held:number;total:number};
/** Found items by category (held = Stored or Claimed). */
export function gsvItemReport(s:State):GSVItemCategoryRow[]{
  const xs=gsvSlice(s).items;const rows:{key:string;label:string;dept?:string;hours?:number;valuable:boolean}[]=lfcRows(s).map(r=>({key:r.code,label:r.name,dept:r.department,hours:r.slaHours,valuable:!!r.valuable}));
  for(const i of xs){const k=lfcKeyOf(i);if(!rows.some(r=>r.key===k))rows.push({key:k,label:i.category,valuable:i.valuable});}
  return rows.map(r=>{const m=xs.filter(i=>lfcKeyOf(i)===r.key);return {category:r.label,code:r.key,...(r.dept?{dept:r.dept}:{}),...(r.hours?{slaHours:r.hours}:{}),valuable:r.valuable,held:m.filter(i=>i.status==='Stored'||i.status==='Claimed').length,total:m.length};}).filter(r=>r.total>0);
}

// F12-CAP-GUEST
// MEAS guest captures (MEAS-CONTRACT §2): survey, monthly review figures, root causes, loyalty tag. All data lives in s.measX (side tables); SCHEMA stays 15.
import {HccError} from '../errors';
import type {ErrorCode} from '../errors';
import type {MeasSurvey,MeasReviewMonth,MeasRootCause} from './measure-types';
import {measOn} from './measure';
import {cfgModuleOn} from './config';
import {guestLooksLikeIdNumber} from './guests';

/** Item switches this lane's captures answer to (contract §1.2). */
export const GSV_MEAS_KEYS={survey:'meas.guest.survey',reputation:'meas.guest.reputation',recovery:'meas.guest.recovery',repeat:'meas.guest.repeat'} as const;
export const GSV_MEAS_REASONS=['MEAS_OFF','MEAS_VALIDATION','MEAS_RATE_LIMIT','MEAS_NO_CONSENT','MEAS_ID_DATA'] as const;
export type GsvMeasReason=typeof GSV_MEAS_REASONS[number];
/** Typed refusal: shared code plus the MEAS reason (the server sends the same pair). MEAS_OFF is a VALIDATION-class refusal until errors.ts lists it (integrator). */
export class GsvMeasError extends HccError{constructor(public readonly reason:GsvMeasReason,message:string,code:ErrorCode='VALIDATION'){super(code,message);this.name='GsvMeasError';}}
export const gsvMeasReasonOf=(e:unknown):GsvMeasReason|undefined=>{const r=(e as {reason?:unknown}|null)?.reason;return GSV_MEAS_REASONS.includes(r as GsvMeasReason)?r as GsvMeasReason:undefined;};
export const gsvMeasFail=(reason:GsvMeasReason,message:string,code?:ErrorCode):never=>{throw new GsvMeasError(reason,message,code);};

/** Capture gate: every command first checks the switch and refuses MEAS_OFF (data is kept). */
export function gsvMeasGuard(s:State,key:string):void{
  if(!measOn(s,key))gsvMeasFail('MEAS_OFF',`This measurement (${key}) is switched off in Measurement reports settings.`);
}
/** Survey channels: the QR page is a page of the Direct booking site module, so it also follows that module's switch. */
export const gsvSurveyChannelOn=(s:State,via:'qr'|'desk'|'link')=>via==='desk'||cfgModuleOn(s,'website');

/** PERM-MEAS-002 in the prototype = by role: desk entry on behalf of a guest (Owner, Manager, Reception). */
export const GSV_MEAS_DESK_ROLES:Role[]=['Owner','Manager','Reception'];
export const GSV_MEAS_MANAGER_ROLES:Role[]=['Owner','Manager'];
export const gsvMeasRoleOk=(s:State,uid:string,roles:Role[])=>{const u=s.users.find(x=>x.id===uid);return !!u?.enabled&&roles.includes(u.role);};

/** Fields a survey may carry. Anything identifying (idNumber, idType, photo, name, phone …) is refused, never stored (DEC-068; DoD: a survey cannot store ID data). */
export const GSV_SURVEY_FIELDS=['type','id','key','via','booking','overall','cleanliness','staff','value','breakfast','recommend','comment','consent'] as const;
const FORBIDDEN_SURVEY_FIELDS=/^(id(Number|Type|Photo|Image|Last4|Proof)|photo|image|selfie|aadhaar|passport|pan|licen[cs]e|name|guest|phone|email|address)$/i;
export const GSV_SURVEY_COMMENT_MAX=500;
export const GSV_QR_LIMIT={windowMinutes:10,max:10};

const score=(v:unknown,lo:number,hi:number)=>typeof v==='number'&&Number.isInteger(v)&&v>=lo&&v<=hi;
/** First problem of a survey command (undefined = valid). Pure; the handler turns it into the typed refusal. */
export function gsvSurveyProblem(cmd:Record<string,unknown>):{reason:GsvMeasReason;message:string}|undefined{
  for(const k of Object.keys(cmd))if(FORBIDDEN_SURVEY_FIELDS.test(k)&&cmd[k]!==undefined&&cmd[k]!=='')
    return {reason:'MEAS_ID_DATA',message:`A survey never stores ID data or guest identity (${k}). Leave it out.`};
  if(cmd.via!=='qr'&&cmd.via!=='desk'&&cmd.via!=='link')return {reason:'MEAS_VALIDATION',message:'Channel must be qr, desk or link.'};
  if(cmd.consent!==true)return {reason:'MEAS_NO_CONSENT',message:'The guest must agree to the answers being kept (consent) before they are saved.'};
  for(const k of ['overall','cleanliness','staff','value'])if(!score(cmd[k],1,5))return {reason:'MEAS_VALIDATION',message:`${k}: a whole score from 1 to 5.`};
  if(cmd.breakfast!==undefined&&!score(cmd.breakfast,1,5))return {reason:'MEAS_VALIDATION',message:'breakfast: a whole score from 1 to 5.'};
  if(!score(cmd.recommend,0,10))return {reason:'MEAS_VALIDATION',message:'recommend: a whole score from 0 to 10.'};
  if(cmd.comment!==undefined){
    if(typeof cmd.comment!=='string')return {reason:'MEAS_VALIDATION',message:'The comment must be text.'};
    if(cmd.comment.length>GSV_SURVEY_COMMENT_MAX)return {reason:'MEAS_VALIDATION',message:`The comment is at most ${GSV_SURVEY_COMMENT_MAX} characters.`};
    if(guestLooksLikeIdNumber(cmd.comment))return {reason:'MEAS_ID_DATA',message:'The comment looks like an ID number. A survey never stores ID data.'};
  }
  return undefined;
}
/** Public-page rate limit: at most GSV_QR_LIMIT.max QR answers in the last windowMinutes of the simulated clock. */
export function gsvSurveyRateLimited(surveys:MeasSurvey[],clock:string):boolean{
  const from=addMinutes(clock,-GSV_QR_LIMIT.windowMinutes);
  return surveys.filter(x=>x.via==='qr'&&x.at>from).length>=GSV_QR_LIMIT.max;
}
/** Net promoter score of a survey list: % promoters (9-10) minus % detractors (0-6), whole points; undefined for an empty list. */
export function gsvSurveyNps(list:Pick<MeasSurvey,'recommend'>[]):number|undefined{
  if(!list.length)return undefined;
  const p=list.filter(x=>x.recommend>=9).length,d=list.filter(x=>x.recommend<=6).length;
  return Math.round((p-d)*100/list.length);
}

export const GSV_REVIEW_PLATFORMS:MeasReviewMonth['platform'][]=['google','agoda','makemytrip','booking','other'];
export const gsvReviewId=(month:string,platform:string)=>`${month}-${platform}`;
export function gsvReviewProblem(c:Record<string,unknown>):string|undefined{
  if(typeof c.month!=='string'||!/^\d{4}-(0[1-9]|1[0-2])$/.test(c.month))return 'Month must be yyyy-MM.';
  if(!GSV_REVIEW_PLATFORMS.includes(c.platform as MeasReviewMonth['platform']))return 'Choose a platform: Google, Agoda, MakeMyTrip, Booking.com or other.';
  if(!Number.isInteger(c.count)||(c.count as number)<0||(c.count as number)>100000)return 'Review count is a whole number.';
  if(typeof c.avgScore!=='number'||c.avgScore<0||c.avgScore>5)return 'Average score is 0 to 5.';
  if(c.score12m!==undefined&&(typeof c.score12m!=='number'||c.score12m<0||c.score12m>5))return '12-month score is 0 to 5.';
  if(!Number.isInteger(c.responded)||(c.responded as number)<0||(c.responded as number)>(c.count as number))return 'Responded is a whole number, not above the review count.';
  if(c.themes!==undefined&&(typeof c.themes!=='string'||c.themes.length>200||guestLooksLikeIdNumber(c.themes)))return 'Themes: short text (200 characters), no ID numbers.';
  return undefined;
}

export const GSV_ROOTCAUSE_SEED:MeasRootCause[]=[
  {code:'STAFF',label:'Staff attitude / training',active:true},{code:'PROCESS',label:'Process gap',active:true},{code:'MAINT',label:'Maintenance / equipment',active:true},
  {code:'SUPPLY',label:'Supplies / stock',active:true},{code:'EXPECT',label:'Guest expectation',active:true},{code:'THIRD',label:'Third party / OTA',active:true}];
export const gsvRootCauses=(s:State):MeasRootCause[]=>s.measX?.rootCauseList??GSV_ROOTCAUSE_SEED;
export function gsvRootCauseListProblem(rows:unknown):string|undefined{
  if(!Array.isArray(rows)||!rows.length||rows.length>30)return 'Give 1 to 30 root causes.';
  const seen=new Set<string>();
  for(const r of rows as Record<string,unknown>[]){
    if(typeof r?.code!=='string'||!/^[A-Z0-9_-]{1,12}$/.test(r.code))return 'A code is 1 to 12 capital letters or digits.';
    if(seen.has(r.code))return `Code ${r.code} is repeated.`;seen.add(r.code);
    if(typeof r.label!=='string'||!r.label.trim()||r.label.length>40)return `${r.code}: a label of up to 40 characters.`;
    if(typeof r.active!=='boolean')return `${r.code}: active is yes or no.`;
  }
  return undefined;
}
export const GSV_LOYALTY_TAG_MAX=20;
// F12-CAP-GUEST end
