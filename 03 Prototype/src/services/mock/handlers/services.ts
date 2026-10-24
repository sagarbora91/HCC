// Module 18 · Guest services, complaints, lost & found — command handlers. Command types MUST start with 'GSV_'. Return value = audit/summary text.
import type {Handler,Kit} from '../commands';
import {execute} from '../commands';
import type {State} from '../../../domain/types';
import {money,addDays} from '../../../domain/money';
import {can} from '../../../domain/selectors';
import {
  GSV_REQUEST_TYPES,GSV_DEPTS,GSV_COMPLAINT_CATEGORIES,GSV_SEVERITIES,GSV_ITEM_CATEGORIES,GSV_STORAGE,GSV_SAFE,GSV_ID_TYPES,GSV_DISPOSAL,
  gsvEnsure,gsvSettings,gsvDefaultDept,gsvDeptRoles,gsvSlaMinutes,gsvAddMinutes,gsvCanWork,gsvIsOpen,gsvRecoveryStatus,gsvIsValuable,
  gsvDisposable,gsvDisposeOn,gsvIdNoteOk,
  type GSVRequest,type GSVComplaint,type GSVItem,type GSVRequestType,type GSVDept,type GSVLog,
} from '../../../domain/streams/services';

/** Remove undefined fields so state survives a JSON round trip unchanged. */
function clean<T extends object>(o:T):T{for(const k of Object.keys(o))if((o as Record<string,unknown>)[k]===undefined)delete (o as Record<string,unknown>)[k];return o;}
const txt=(v:unknown)=>String(v??'').trim();
const log=(k:Kit,what:string):GSVLog=>({at:k.s.clock,by:k.uid,what});
const name=(s:State,uid:string)=>s.users.find(u=>u.id===uid)?.name.split(' · ')[0]??uid;

function findRequest(k:Kit){const r=gsvEnsure(k.s).requests.find(r=>r.id===k.cmd.id);k.assert(r,'Service request not found.','NOT_FOUND');return r!;}
function findComplaint(k:Kit){const c=gsvEnsure(k.s).complaints.find(c=>c.id===k.cmd.id);k.assert(c,'Complaint not found.','NOT_FOUND');return c!;}
function findItem(k:Kit){const i=gsvEnsure(k.s).items.find(i=>i.id===k.cmd.id);k.assert(i,'Lost & found item not found.','NOT_FOUND');return i!;}
/** reservations, or inspect (supervisor) for Housekeeping / Maintenance requests. */
function reqManage(k:Kit,dept:GSVDept){if(can(k.s,k.uid,'reservations'))return;if(dept!=='Front desk'&&can(k.s,k.uid,'inspect'))return;k.req('reservations');}
function checkAssignee(k:Kit,dept:GSVDept,uid:string){
  const u=k.s.users.find(u=>u.id===uid);
  k.assert(u&&u.enabled&&gsvDeptRoles[dept].includes(u.role),`Choose an enabled ${dept.toLowerCase()} team member (${gsvDeptRoles[dept].join(' / ')}).`);
}

// ── Service requests ──
const GSV_REQUEST_CREATE:Handler=k=>{
  const {s,cmd,req,open,assert,id}=k;req('reservations');open();
  const x=k.b();assert(x.status==='In house','Service requests can be logged only for in-house guests.','INVALID_TRANSITION');
  const type=cmd.requestType as GSVRequestType;assert(GSV_REQUEST_TYPES.includes(type),'Choose the request type.');
  const dept=(cmd.dept||gsvDefaultDept(type)) as GSVDept;assert(GSV_DEPTS.includes(dept),'Choose a department.');
  const priority=cmd.priority==='Urgent'?'Urgent':'Normal';
  const detail=txt(cmd.detail);assert(detail.length<=160,'Keep the detail under 160 characters.');
  assert(type!=='Other'||detail.length>=3,'Describe the request.');
  let scheduledFor:string|undefined,dueAt:string;
  if(type==='Wake-up call'){
    assert(/^([01]\d|2[0-3]):[0-5]\d$/.test(txt(cmd.time)),'Enter the wake-up time as HH:mm.');
    scheduledFor=s.date+'T'+txt(cmd.time);if(scheduledFor<=s.clock)scheduledFor=addDays(s.date,1)+'T'+txt(cmd.time);
    dueAt=gsvAddMinutes(scheduledFor,gsvSlaMinutes(s,type,priority));
  }else dueAt=gsvAddMinutes(s.clock,gsvSlaMinutes(s,type,priority));
  if(cmd.assignee)checkAssignee(k,dept,cmd.assignee);
  const r:GSVRequest=clean({id:id('SR'),booking:x.id,room:x.room,guest:x.guest,type,detail,dept,assignee:cmd.assignee||undefined,priority,
    status:cmd.assignee?'Assigned':'Open',createdAt:s.clock,dueAt,scheduledFor,by:k.uid,log:[log(k,'Logged'+(cmd.assignee?' · assigned to '+name(s,cmd.assignee):''))]} as GSVRequest);
  gsvEnsure(s).requests.push(r);
  return `${r.id} · room ${r.room} · ${type} · ${dept} · due ${dueAt.slice(11,16)}`;
};
const GSV_REQUEST_ASSIGN:Handler=k=>{
  const {s,cmd,assert}=k;const r=findRequest(k);reqManage(k,r.dept);
  assert(gsvIsOpen(r),'This request is already finished.','INVALID_TRANSITION');
  const dept=(cmd.dept||r.dept) as GSVDept;assert(GSV_DEPTS.includes(dept),'Choose a department.');
  if(dept!==r.dept)reqManage(k,dept);
  checkAssignee(k,dept,cmd.assignee);
  r.dept=dept;r.assignee=cmd.assignee;if(r.status==='Open')r.status='Assigned';
  r.log.push(log(k,`Assigned to ${name(s,cmd.assignee)} (${dept})`));
  return `${r.id} · assigned to ${name(s,cmd.assignee)}`;
};
const GSV_REQUEST_START:Handler=k=>{
  const {s,assert}=k;const r=findRequest(k);
  if(!gsvCanWork(s,k.uid,r)){k.req('housekeeping');k.fail('FORBIDDEN','Only requests assigned to you are accessible.');}
  assert(r.status==='Open'||r.status==='Assigned','This request has already started or finished.','INVALID_TRANSITION');
  if(!r.assignee){r.assignee=k.uid;}
  r.status='In progress';r.startedAt=s.clock;r.log.push(log(k,'Started'));
  return `${r.id} · started`;
};
const GSV_REQUEST_COMPLETE:Handler=k=>{
  const {s,cmd,assert}=k;const r=findRequest(k);
  if(!gsvCanWork(s,k.uid,r)){k.req('housekeeping');k.fail('FORBIDDEN','Only requests assigned to you are accessible.');}
  assert(gsvIsOpen(r),'This request is already finished.','INVALID_TRANSITION');
  const note=txt(cmd.note);assert(note.length>=3,'Add a short note for the guest (e.g. “Two bath towels delivered”).');
  assert(note.length<=200,'Keep the guest note under 200 characters.');
  if(!r.assignee)r.assignee=k.uid;
  r.status='Done';r.doneAt=s.clock;r.note=note;if(!r.startedAt)r.startedAt=s.clock;r.log.push(log(k,'Completed · '+note));
  return `${r.id} · done · ${note}`;
};
const GSV_REQUEST_CANCEL:Handler=k=>{
  const {cmd,assert,req}=k;req('reservations');const r=findRequest(k);
  assert(gsvIsOpen(r),'This request is already finished.','INVALID_TRANSITION');
  const reason=txt(cmd.reason);assert(reason.length>=3,'Enter the reason for cancelling.');
  r.status='Cancelled';r.cancelReason=reason;r.log.push(log(k,'Cancelled · '+reason));
  return `${r.id} · cancelled · ${reason}`;
};

// ── Complaints ──
const GSV_COMPLAINT_LOG:Handler=k=>{
  const {s,cmd,req,open,assert,id}=k;req('reservations');open();
  let booking:string|undefined,room:string|undefined,guest=txt(cmd.guest);
  if(cmd.booking){const x=s.bookings.find(b=>b.id===cmd.booking);assert(x,'Booking not found.','NOT_FOUND');booking=x!.id;room=x!.room;guest=guest||x!.guest;}
  assert(guest.length>=2,'Enter the guest’s name.');
  assert(GSV_COMPLAINT_CATEGORIES.includes(cmd.category),'Choose a category.');
  assert(GSV_SEVERITIES.includes(cmd.severity),'Choose the severity.');
  const description=txt(cmd.description);assert(description.length>=5,'Describe the complaint.');assert(description.length<=400,'Keep the description under 400 characters.');
  const owner=cmd.owner||k.uid;assert(s.users.some(u=>u.id===owner&&u.enabled)&&can(s,owner,'reservations'),'The owner must be an enabled front-office user.');
  const c:GSVComplaint=clean({id:id('CMP'),booking,room,guest,category:cmd.category,severity:cmd.severity,description,owner,status:'Open',createdAt:s.clock,by:k.uid,log:[log(k,'Logged · owner '+name(s,owner))]} as GSVComplaint);
  gsvEnsure(s).complaints.push(c);
  return `${c.id} · ${c.category} · ${c.severity} · ${guest}`;
};
const GSV_COMPLAINT_UPDATE:Handler=k=>{
  const {s,cmd,req,assert}=k;req('reservations');const c=findComplaint(k);
  assert(c.status==='Open'||c.status==='Investigating','Only an open complaint can be updated.','INVALID_TRANSITION');
  const changes:string[]=[];
  if(cmd.owner&&cmd.owner!==c.owner){assert(s.users.some(u=>u.id===cmd.owner&&u.enabled)&&can(s,cmd.owner,'reservations'),'The owner must be an enabled front-office user.');c.owner=cmd.owner;changes.push('owner '+name(s,cmd.owner));}
  if(cmd.severity&&cmd.severity!==c.severity){assert(GSV_SEVERITIES.includes(cmd.severity),'Choose the severity.');c.severity=cmd.severity;changes.push('severity '+cmd.severity);}
  if(cmd.rootCause!==undefined&&txt(cmd.rootCause)!==(c.rootCause??'')){c.rootCause=txt(cmd.rootCause)||undefined;changes.push('root cause');}
  if(cmd.recoveryAction!==undefined&&txt(cmd.recoveryAction)!==(c.recoveryAction??'')){c.recoveryAction=txt(cmd.recoveryAction)||undefined;changes.push('recovery action');}
  if(c.status==='Open'){c.status='Investigating';changes.push('investigating');}
  clean(c);assert(changes.length,'Nothing to update.');
  c.log.push(log(k,'Updated · '+changes.join(', ')));
  return `${c.id} · ${changes.join(', ')}`;
};
const GSV_COMPLAINT_RESOLVE:Handler=k=>{
  const {s,cmd,req,assert}=k;req('reservations');const c=findComplaint(k);
  assert(c.status==='Open'||c.status==='Investigating','Only an open complaint can be resolved.','INVALID_TRANSITION');
  const rootCause=txt(cmd.rootCause??c.rootCause),action=txt(cmd.recoveryAction??c.recoveryAction);
  assert(rootCause.length>=3,'Record the root cause before resolving.');assert(action.length>=3,'Record the recovery action taken.');
  const followUpOn=txt(cmd.followUpOn)||addDays(s.date,1);
  assert(/^\d{4}-\d{2}-\d{2}$/.test(followUpOn)&&followUpOn>=s.date,'The follow-up date cannot be in the past.');
  c.rootCause=rootCause;c.recoveryAction=action;c.status='Resolved';c.resolvedAt=s.clock;c.followUpOn=followUpOn;
  c.log.push(log(k,'Resolved · follow-up '+followUpOn));
  return `${c.id} · resolved · follow-up ${followUpOn}`;
};
/** Service-recovery discount: runs the EXISTING DISCOUNT command unchanged (permission, limits, approval threshold). */
const GSV_COMPLAINT_RECOVERY:Handler=k=>{
  const {s,uid,cmd,assert}=k;const c=findComplaint(k);
  assert(c.status!=='Closed','This complaint is closed.','INVALID_TRANSITION');
  assert(c.booking,'Link the complaint to a stay before offering a folio discount.','INVALID_TRANSITION');
  const st=gsvRecoveryStatus(s,c);
  assert(st==='None'||st==='Rejected','A recovery discount is already '+(st==='Posted'||st==='Approved'?'posted':'awaiting approval')+' for this complaint.','INVALID_TRANSITION');
  const reason='Service recovery · '+c.id+(txt(cmd.reason)?' · '+txt(cmd.reason):'');
  const beforeApprovals=new Set(s.approvals.map(a=>a.id));
  const beforeLines=new Set((s.bookings.find(b=>b.id===c.booking)?.lines??[]).map(l=>l.id));
  // Nested call through the single command path; throws its own typed errors (FORBIDDEN, DAY_CLOSED, VALIDATION…).
  const next=execute(s,uid,{type:'DISCOUNT',id:c.booking,amount:cmd.amount,reason});
  for(const key of Object.keys(s) as (keyof State)[])if(!(key in next))delete (s as Record<string,unknown>)[key];
  Object.assign(s,next);
  const c2=gsvEnsure(s).complaints.find(x=>x.id===c.id)!;
  const apr=s.approvals.find(a=>!beforeApprovals.has(a.id));
  const line=s.bookings.find(b=>b.id===c2.booking)!.lines.find(l=>!beforeLines.has(l.id)&&l.kind==='discount');
  c2.recovery=clean({amount:cmd.amount,reason,approvalId:apr?.id,lineId:line?.id,at:s.clock,by:uid});
  c2.log.push(log(k,`Recovery discount ${money(cmd.amount)} · ${apr?'approval requested '+apr.id:'posted to folio'}`));
  return `${c2.id} · recovery ${money(cmd.amount)} on ${c2.booking} · ${apr?'approval requested '+apr.id:'discount posted'}`;
};
const GSV_COMPLAINT_FOLLOWUP:Handler=k=>{
  const {s,cmd,req,assert}=k;req('reservations');const c=findComplaint(k);
  assert(c.status==='Resolved','Follow-up is recorded after the complaint is resolved.','INVALID_TRANSITION');
  const outcome=txt(cmd.outcome);assert(outcome.length>=3,'Record what the guest said.');
  const satisfied=!!cmd.satisfied;
  if(satisfied&&(c.severity==='High'||c.severity==='Critical'))k.req('approve');
  c.followUp={outcome,satisfied,at:s.clock,by:k.uid};
  if(satisfied){c.status='Closed';c.closedAt=s.clock;}else{c.status='Investigating';delete c.resolvedAt;}
  c.log.push(log(k,`Follow-up · ${satisfied?'satisfied · closed':'not satisfied · re-opened'} · ${outcome}`));
  return `${c.id} · follow-up · ${satisfied?'closed':'re-opened'}`;
};

// ── Lost & found ──
const GSV_LF_LOG:Handler=k=>{
  const {s,cmd,assert,id,open}=k;
  if(!can(s,k.uid,'reservations'))k.req('housekeeping');open();
  const description=txt(cmd.description);assert(description.length>=3,'Describe the item.');assert(description.length<=160,'Keep the description under 160 characters.');
  assert(GSV_ITEM_CATEGORIES.includes(cmd.category),'Choose a category.');
  const foundWhere=txt(cmd.foundWhere);assert(foundWhere.length>=2,'Enter where the item was found.');
  const storage=txt(cmd.storage);assert((GSV_STORAGE as readonly string[]).includes(storage),'Choose the storage location.');
  const valuable=gsvIsValuable(cmd.category);
  assert(!valuable||storage===GSV_SAFE,`Valuables (${cmd.category}) must be stored in the ${GSV_SAFE}.`);
  let room:string|undefined=txt(cmd.room)||undefined;if(room)assert(s.rooms.some(r=>r.id===room),'Room not found.','NOT_FOUND');
  // Link to the last stay in that room (in house or departed today) — for front-office follow-up only.
  const stay=room?s.bookings.filter(b=>b.room===room&&(b.status==='In house'||b.status==='Departed')&&b.arrival<=s.date).sort((a,b)=>a.departure<b.departure?1:-1)[0]:undefined;
  const foundBy=cmd.foundBy&&can(s,k.uid,'reservations')?cmd.foundBy:k.uid;
  assert(s.users.some(u=>u.id===foundBy),'Choose who found the item.');
  const i:GSVItem=clean({id:id('LF'),description,category:cmd.category,valuable,foundWhere,room,foundAt:s.clock,foundBy,storage,booking:stay?.id,status:'Stored',log:[log(k,'Logged · stored in '+storage)]} as GSVItem);
  gsvEnsure(s).items.push(i);
  return `${i.id} · ${description} · ${storage}`;
};
const GSV_LF_CLAIM:Handler=k=>{
  const {s,cmd,req,assert}=k;req('reservations');const i=findItem(k);
  assert(i.status==='Stored','Only a stored item can be claimed.','INVALID_TRANSITION');
  const nm=txt(cmd.name),phone=txt(cmd.phone).replace(/[\s-]/g,'');
  assert(nm.length>=2,'Enter the claimant’s name.');assert(/^\+?\d{10,13}$/.test(phone),'Enter a 10-digit phone number.');
  assert((GSV_ID_TYPES as readonly string[]).includes(cmd.idType),'Choose the ID document checked.');
  assert(gsvIdNoteOk(txt(cmd.idNote)),'Record an ID check note with only the last 4 characters — never the full ID number.');
  const match=txt(cmd.matchNote);assert(match.length>=3,'Note how the claimant described the item.');
  i.claim={name:nm,phone,idType:cmd.idType,idNote:txt(cmd.idNote),matchNote:match,at:s.clock,by:k.uid};i.status='Claimed';
  i.log.push(log(k,'Claimed by '+nm+' · ID checked'));
  return `${i.id} · claimed by ${nm}`;
};
const GSV_LF_HANDOVER:Handler=k=>{
  const {s,cmd,req,assert}=k;req('reservations');const i=findItem(k);
  assert(i.status==='Claimed','Record and verify the claim before handover.','INVALID_TRANSITION');
  const to=txt(cmd.to)||i.claim!.name;assert(to.length>=2,'Enter who received the item.');
  i.handover={at:s.clock,by:k.uid,to,note:txt(cmd.note)};i.status='Handed over';
  i.log.push(log(k,'Handed over to '+to));
  return `${i.id} · handed over to ${to}`;
};
const GSV_LF_DISPOSE:Handler=k=>{
  const {s,cmd,req,assert}=k;req('reservations');const i=findItem(k);
  assert(i.status==='Stored','Only an unclaimed stored item can be disposed.','INVALID_TRANSITION');
  if(i.valuable)req('approve');
  assert(gsvDisposable(s,i),`Retention period not over — this item can be disposed from ${gsvDisposeOn(s,i)}.`,'INVALID_TRANSITION');
  assert((GSV_DISPOSAL as readonly string[]).includes(cmd.method),'Choose the disposal method.');
  assert(!i.valuable||cmd.method!=='Destroyed','Valuables are donated or handed to police, not destroyed.');
  i.disposal={at:s.clock,by:k.uid,method:cmd.method,note:txt(cmd.note)};i.status='Disposed';
  i.log.push(log(k,'Disposed · '+cmd.method));
  return `${i.id} · disposed · ${cmd.method}`;
};

const GSV_SETTINGS:Handler=k=>{
  const {s,cmd,req,assert}=k;req('approve');const cur=gsvSettings(s);
  const sla={...cur.sla};
  for(const t of GSV_REQUEST_TYPES)if(cmd.sla?.[t]!==undefined){const m=Number(cmd.sla[t]);assert(Number.isInteger(m)&&m>=5&&m<=480,`SLA for ${t} must be 5–480 minutes.`);sla[t]=m;}
  const retentionDays=cmd.retentionDays===undefined?cur.retentionDays:Number(cmd.retentionDays);
  const valuableRetentionDays=cmd.valuableRetentionDays===undefined?cur.valuableRetentionDays:Number(cmd.valuableRetentionDays);
  assert(Number.isInteger(retentionDays)&&retentionDays>=30&&retentionDays<=365,'Retention must be 30–365 days.');
  assert(Number.isInteger(valuableRetentionDays)&&valuableRetentionDays>=retentionDays&&valuableRetentionDays<=730,'Valuables retention must be at least the normal retention (max 730 days).');
  gsvEnsure(s).settings={sla,retentionDays,valuableRetentionDays};
  return `Guest-service settings · retention ${retentionDays}/${valuableRetentionDays} days`;
};

export const handlers:Record<string,Handler>={GSV_REQUEST_CREATE,GSV_REQUEST_ASSIGN,GSV_REQUEST_START,GSV_REQUEST_COMPLETE,GSV_REQUEST_CANCEL,
  GSV_COMPLAINT_LOG,GSV_COMPLAINT_UPDATE,GSV_COMPLAINT_RESOLVE,GSV_COMPLAINT_RECOVERY,GSV_COMPLAINT_FOLLOWUP,
  GSV_LF_LOG,GSV_LF_CLAIM,GSV_LF_HANDOVER,GSV_LF_DISPOSE,GSV_SETTINGS};
