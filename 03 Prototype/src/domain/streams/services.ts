// Module 18 · Guest services, complaints, lost & found — pure helpers, selectors and the state slice type. Exported names MUST start with 'gsv' / 'GSV' (shared mock namespace).
import type {State,Role,User} from '../types';
import {addMinutes,addDays,nights} from '../money';
import {can} from '../selectors';

export type GSVLog={at:string;by:string;what:string};
export const GSV_REQUEST_TYPES=['Extra towels','Drinking water','Iron & board','Wake-up call','Taxi','Toiletries & amenities','Minor repair','Other'] as const;
export type GSVRequestType=typeof GSV_REQUEST_TYPES[number];
export const GSV_DEPTS=['Housekeeping','Maintenance','Front desk'] as const;
export type GSVDept=typeof GSV_DEPTS[number];
export type GSVRequestStatus='Open'|'Assigned'|'In progress'|'Done'|'Cancelled';
export type GSVRequest={id:string;booking:string;room:string;guest:string;type:GSVRequestType;detail:string;dept:GSVDept;assignee?:string;
  priority:'Normal'|'Urgent';status:GSVRequestStatus;createdAt:string;dueAt:string;scheduledFor?:string;startedAt?:string;doneAt?:string;
  /** Guest-facing completion note (shown to the guest; simulated message). */
  note?:string;cancelReason?:string;by:string;log:GSVLog[]};

export const GSV_COMPLAINT_CATEGORIES=['Room cleanliness','Maintenance','Noise','Staff behaviour','Food & beverage','Billing','Amenities','Other'] as const;
export type GSVComplaintCategory=typeof GSV_COMPLAINT_CATEGORIES[number];
export const GSV_SEVERITIES=['Low','Medium','High','Critical'] as const;
export type GSVSeverity=typeof GSV_SEVERITIES[number];
export type GSVComplaintStatus='Open'|'Investigating'|'Resolved'|'Closed';
export type GSVRecovery={amount:number;reason:string;approvalId?:string;lineId?:string;at:string;by:string};
export type GSVComplaint={id:string;booking?:string;room?:string;guest:string;category:GSVComplaintCategory;severity:GSVSeverity;description:string;
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
export type GSVItem={id:string;description:string;category:GSVItemCategory;valuable:boolean;foundWhere:string;room?:string;foundAt:string;foundBy:string;
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
export type GSVCategoryRow={category:string;total:number;open:number;bySeverity:Record<GSVSeverity,number>;recovery:number};
export function gsvComplaintReport(s:State):GSVCategoryRow[]{
  const cs=gsvSlice(s).complaints;
  return GSV_COMPLAINT_CATEGORIES.map(cat=>{const xs=cs.filter(c=>c.category===cat);
    const bySeverity=Object.fromEntries(GSV_SEVERITIES.map(v=>[v,xs.filter(c=>c.severity===v).length])) as Record<GSVSeverity,number>;
    return {category:cat,total:xs.length,open:xs.filter(gsvComplaintOpen).length,bySeverity,recovery:xs.filter(c=>c.recovery).length};}).filter(r=>r.total>0);
}
export function gsvLostFoundSummary(s:State){const xs=gsvSlice(s).items;
  return {stored:xs.filter(i=>i.status==='Stored').length,claimed:xs.filter(i=>i.status==='Claimed').length,handed:xs.filter(i=>i.status==='Handed over').length,
    disposed:xs.filter(i=>i.status==='Disposed').length,dueDisposal:xs.filter(i=>gsvDisposable(s,i)).length,valuables:xs.filter(i=>i.valuable&&(i.status==='Stored'||i.status==='Claimed')).length};}
