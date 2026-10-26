// Guest-services area (W4-10): service requests with SLAs, complaints (recovery discount via Billing), lost & found with retention.
// Server: 08 Production/src/Hcc.Api/Endpoints/GuestServicesEndpoints.cs (module 18) + the W4-10 additive reads GET /guest-services/stays
// and GET /guest-services/histories/{kind}.
//
//   loaders   servicesX ← GET /guest-services/{requests,complaints,lost-items,settings,stays,histories/*} assembled into the prototype's
//             GSVState (requests, complaints, items, settings). Booking ids are reservation ids; the stay lookup (reservation ↔ stay) is
//             learned in `ids` ('stay'). Each sub-read tolerates 403 (housekeeping/maintenance see only their own requests and their
//             own lost items; complaints, history and settings need front-office rights) — the slice then holds what the user may see.
//   commands  every GSV_* command the screens send → the server command of the same meaning (versions from the loaded records).
//   gaps      gsvRecoveryStatus() reads s.approvals for a pending/approved recovery discount: that slice belongs to the money area.
import type {GSVState,GSVRequest,GSVComplaint,GSVItem,GSVSettings,GSVLog,GSVRequestType,GSVDept,GSVRecovery} from '../../../domain/streams/services';
import {HccClientError} from '../../errors';
import type {SyncArea,LoaderCtx,CommandCtx,SyncApi} from '../types';
import {utcToClock,serverId} from '../map';

// ── Server DTOs (08 Production/src/Hcc.Contracts/GuestServices) ──
type SrvPage<T>={page:number;pageSize:number;total:number;items:T[];ownOnly:boolean};
type SrvSla={tone:string;label:string;minutesLeft:number|null};
type SrvRequest={requestId:number;stayId:number;roomNo:string;guestName:string;requestType:string;detail:string;dept:string;assigneeId:number|null;priority:string;status:string;
  createdUtc:string;dueUtc:string;scheduledUtc:string|null;startedUtc:string|null;doneUtc:string|null;guestNote:string|null;cancelReason:string|null;sla:SrvSla;version:string};
type SrvRecovery={amountPaise:number;reason:string;approvalId:number|null;chargeId:number|null;atUtc:string;by:number};
type SrvComplaint={complaintId:number;stayId:number|null;roomNo:string|null;guestName:string;category:string;severity:string;description:string;ownerId:number;status:string;
  rootCause:string|null;recoveryAction:string|null;followUpOn:string|null;followUpDue:boolean;followUp:{outcome:string;satisfied:boolean;atUtc:string;by:number}|null;
  createdUtc:string;resolvedUtc:string|null;closedUtc:string|null;recovery:SrvRecovery|null;recoveryStatus:string;version:string};
type SrvClaim={name:string;phone:string;idType:string;idNote:string;matchNote:string;atUtc:string;by:number};
type SrvItem={itemId:number;description:string;category:string;valuable:boolean;foundWhere:string;roomNo:string|null;stayId:number|null;foundUtc:string;foundDate:string;
  foundBy:number;storage:string;status:string;disposeOn:string;disposable:boolean;claim:SrvClaim|null;handedOverTo:string|null;disposalMethod:string|null;version:string};
type SrvSettings={slaMinutes:Record<string,number>;retentionDays:number;valuableRetentionDays:number;version:string|null};
type SrvStay={stayId:number;reservationId:number;guestId:number|null;roomNo:string;guestName:string;status:string};
type SrvHistory={recordId:number;entries:{atUtc:string;by:number;what:string}[]};

const PAGE=100,MAX_PAGES=20;
async function allPages<T>(api:SyncApi,path:string,query:Record<string,string|number|boolean>={}):Promise<T[]>{
  const out:T[]=[];
  for(let page=1;page<=MAX_PAGES;page++){
    const r=await api.get<SrvPage<T>>(path,{...query,page,pageSize:PAGE});
    out.push(...r.items);if(out.length>=r.total||!r.items.length)break;
  }
  return out;
}
/** A read the user may not be allowed (403) leaves its part empty instead of failing the slice. */
async function soft<T>(p:Promise<T>,fallback:T):Promise<T>{
  try{return await p;}catch(e){if(e instanceof HccClientError&&e.wireCode==='FORBIDDEN')return fallback;throw e;}
}
const uid=(n:number|null|undefined)=>n==null?'':String(n);
const logs=(h:SrvHistory[])=>new Map(h.map(x=>[x.recordId,x.entries.map(e=>({at:utcToClock(e.atUtc),by:uid(e.by),what:e.what}) as GSVLog)]));
const clean=<T extends object>(o:T):T=>{for(const k of Object.keys(o))if((o as Record<string,unknown>)[k]===undefined)delete (o as Record<string,unknown>)[k];return o;};

export function requestFromServer(r:SrvRequest,booking:string|undefined,log:GSVLog[]):GSVRequest{
  return clean({id:String(r.requestId),booking:booking??'',room:r.roomNo,guest:r.guestName,type:r.requestType as GSVRequestType,detail:r.detail,dept:r.dept as GSVDept,
    assignee:r.assigneeId==null?undefined:String(r.assigneeId),priority:(r.priority==='Urgent'?'Urgent':'Normal') as GSVRequest['priority'],status:r.status as GSVRequest['status'],
    createdAt:utcToClock(r.createdUtc),dueAt:utcToClock(r.dueUtc),scheduledFor:r.scheduledUtc?utcToClock(r.scheduledUtc):undefined,startedAt:r.startedUtc?utcToClock(r.startedUtc):undefined,
    doneAt:r.doneUtc?utcToClock(r.doneUtc):undefined,note:r.guestNote??undefined,cancelReason:r.cancelReason??undefined,by:log[0]?.by??'',log} as GSVRequest);
}
export function complaintFromServer(c:SrvComplaint,booking:string|undefined,log:GSVLog[]):GSVComplaint{
  const recovery:GSVRecovery|undefined=c.recovery?clean({amount:c.recovery.amountPaise,reason:c.recovery.reason,approvalId:c.recovery.approvalId==null?undefined:String(c.recovery.approvalId),
    lineId:c.recovery.chargeId==null?undefined:String(c.recovery.chargeId),at:utcToClock(c.recovery.atUtc),by:uid(c.recovery.by)}):undefined;
  return clean({id:String(c.complaintId),booking,room:c.roomNo??undefined,guest:c.guestName,category:c.category as GSVComplaint['category'],severity:c.severity as GSVComplaint['severity'],
    description:c.description,owner:uid(c.ownerId),status:c.status as GSVComplaint['status'],rootCause:c.rootCause??undefined,recoveryAction:c.recoveryAction??undefined,recovery,
    followUpOn:c.followUpOn??undefined,followUp:c.followUp?{outcome:c.followUp.outcome,satisfied:c.followUp.satisfied,at:utcToClock(c.followUp.atUtc),by:uid(c.followUp.by)}:undefined,
    createdAt:utcToClock(c.createdUtc),resolvedAt:c.resolvedUtc?utcToClock(c.resolvedUtc):undefined,closedAt:c.closedUtc?utcToClock(c.closedUtc):undefined,by:log[0]?.by??'',log} as GSVComplaint);
}
export function itemFromServer(i:SrvItem,booking:string|undefined,log:GSVLog[]):GSVItem{
  const last=(prefix:string)=>[...log].reverse().find(l=>l.what.startsWith(prefix));
  const hand=i.handedOverTo!=null?{at:last('Handed over')?.at??'',by:last('Handed over')?.by??'',to:i.handedOverTo,note:''}:undefined;
  const disp=i.disposalMethod!=null?{at:last('Disposed')?.at??'',by:last('Disposed')?.by??'',method:i.disposalMethod,note:''}:undefined;
  return clean({id:String(i.itemId),description:i.description,category:i.category as GSVItem['category'],valuable:i.valuable,foundWhere:i.foundWhere,room:i.roomNo??undefined,
    foundAt:utcToClock(i.foundUtc),foundBy:uid(i.foundBy),storage:i.storage,booking,status:i.status as GSVItem['status'],
    claim:i.claim?{name:i.claim.name,phone:i.claim.phone,idType:i.claim.idType,idNote:i.claim.idNote,matchNote:i.claim.matchNote,at:utcToClock(i.claim.atUtc),by:uid(i.claim.by)}:undefined,
    handover:hand,disposal:disp,log} as GSVItem);
}

async function loadServices(c:LoaderCtx):Promise<GSVState>{
  const api=c.api;
  const [requests,complaints,items,settings,stays,hReq,hCmp,hItm]=await Promise.all([
    soft(allPages<SrvRequest>(api,'/guest-services/requests'),[]),
    soft(allPages<SrvComplaint>(api,'/guest-services/complaints'),[]),
    soft(allPages<SrvItem>(api,'/guest-services/lost-items'),[]),
    soft(api.get<SrvSettings>('/guest-services/settings'),null),
    soft(api.get<{items:SrvStay[]}>('/guest-services/stays').then(r=>r.items),[] as SrvStay[]),
    soft(api.get<SrvHistory[]>('/guest-services/histories/request'),[] as SrvHistory[]),
    soft(api.get<SrvHistory[]>('/guest-services/histories/complaint'),[] as SrvHistory[]),
    soft(api.get<SrvHistory[]>('/guest-services/histories/item'),[] as SrvHistory[]),
  ]);
  const byStay=new Map<number,string>();
  for(const s of stays){byStay.set(s.stayId,String(s.reservationId));c.ids.learn('stay',String(s.reservationId),s.stayId);}
  const booking=(stayId:number|null)=>stayId==null?undefined:byStay.get(stayId);
  const lr=logs(hReq),lc=logs(hCmp),li=logs(hItm);
  const state:GSVState={
    requests:requests.map(r=>{c.ids.setVersion('gsvRequest',String(r.requestId),r.version);return requestFromServer(r,booking(r.stayId),lr.get(r.requestId)??[]);}),
    complaints:complaints.map(x=>{c.ids.setVersion('gsvComplaint',String(x.complaintId),x.version);return complaintFromServer(x,booking(x.stayId),lc.get(x.complaintId)??[]);}),
    items:items.map(i=>{c.ids.setVersion('gsvItem',String(i.itemId),i.version);return itemFromServer(i,booking(i.stayId),li.get(i.itemId)??[]);}),
  };
  if(settings){
    state.settings={sla:{...settings.slaMinutes} as GSVSettings['sla'],retentionDays:settings.retentionDays,valuableRetentionDays:settings.valuableRetentionDays};
    if(settings.version)c.ids.setVersion('gsvSettings','1',settings.version);
  }
  return state;
}

// ── Commands ──
/** The loaded version of a record (the screens' commands carry none); a missing one means the list was never loaded. */
function ver(c:CommandCtx,kind:string,id:unknown):string{
  const v=c.ids.version(kind,String(id));
  if(!v)throw new HccClientError('VALIDATION','This record is not loaded from the server yet. Refresh and try again.');
  return v;
}
const num=(id:unknown,what:string)=>serverId(id as string,what);
const txt=(v:unknown)=>String(v??'').trim();
const optId=(v:unknown,what:string)=>txt(v)?serverId(txt(v),what):undefined;
const optTxt=(v:unknown)=>txt(v)||undefined;
const stayOf=(c:CommandCtx,booking:unknown)=>c.ids.server('stay',String(booking??''));

type Cmd=CommandCtx['cmd'];
const need=(msg:string,...vals:unknown[])=>vals.every(v=>txt(v))?undefined:msg;

export const guestServices:SyncArea={
  id:'guest-services',
  loaders:[{slice:'servicesX',requires:['reservations','checkin','housekeeping','inspect'],load:loadServices}],
  commands:[
    {type:'GSV_REQUEST_CREATE',reload:['servicesX'],
      precheck:(cmd:Cmd)=>need('Choose an in-house guest.',cmd.id)??need('Choose the request type.',cmd.requestType),
      async send(c){
        const stayId=stayOf(c,c.cmd.id);
        if(stayId===undefined)throw new HccClientError('VALIDATION','Service requests can be logged only for in-house guests.');
        await c.api.command('LogServiceRequest',{stayId,requestType:c.cmd.requestType,detail:optTxt(c.cmd.detail),dept:optTxt(c.cmd.dept),priority:optTxt(c.cmd.priority),
          time:c.cmd.requestType==='Wake-up call'?optTxt(c.cmd.time):undefined,assigneeId:optId(c.cmd.assignee,'Assignee')});
      }},
    {type:'GSV_REQUEST_ASSIGN',reload:['servicesX'],precheck:(cmd:Cmd)=>need('Choose a team member.',cmd.assignee),
      async send(c){await c.api.command('AssignServiceRequest',{requestId:num(c.cmd.id,'Request'),assigneeId:num(c.cmd.assignee,'Assignee'),dept:optTxt(c.cmd.dept)},
        {expectedVersion:ver(c,'gsvRequest',c.cmd.id)});}},
    {type:'GSV_REQUEST_START',reload:['servicesX'],
      async send(c){await c.api.command('StartServiceRequest',{requestId:num(c.cmd.id,'Request')},{expectedVersion:ver(c,'gsvRequest',c.cmd.id)});}},
    {type:'GSV_REQUEST_COMPLETE',reload:['servicesX'],
      precheck:(cmd:Cmd)=>txt(cmd.note).length>=3?undefined:'Add a short note for the guest (e.g. “Two bath towels delivered”).',
      async send(c){await c.api.command('CompleteServiceRequest',{requestId:num(c.cmd.id,'Request'),note:txt(c.cmd.note)},{expectedVersion:ver(c,'gsvRequest',c.cmd.id)});}},
    {type:'GSV_REQUEST_CANCEL',reload:['servicesX'],
      precheck:(cmd:Cmd)=>txt(cmd.reason).length>=3?undefined:'Enter the reason for cancelling.',
      async send(c){await c.api.command('CancelServiceRequest',{requestId:num(c.cmd.id,'Request'),reason:txt(c.cmd.reason)},{expectedVersion:ver(c,'gsvRequest',c.cmd.id)});}},

    {type:'GSV_COMPLAINT_LOG',reload:['servicesX'],
      precheck:(cmd:Cmd)=>txt(cmd.guest).length>=2||cmd.booking?undefined:'Enter the guest’s name.',
      async send(c){
        const stayId=c.cmd.booking?stayOf(c,c.cmd.booking):undefined;
        await c.api.command('LogComplaint',{stayId,guestName:optTxt(c.cmd.guest),category:c.cmd.category,severity:c.cmd.severity,description:txt(c.cmd.description),
          ownerId:optId(c.cmd.owner,'Owner')});
      }},
    {type:'GSV_COMPLAINT_UPDATE',reload:['servicesX'],
      async send(c){await c.api.command('UpdateComplaint',{complaintId:num(c.cmd.id,'Complaint'),ownerId:optId(c.cmd.owner,'Owner'),severity:optTxt(c.cmd.severity),
        rootCause:c.cmd.rootCause===undefined?undefined:txt(c.cmd.rootCause),recoveryAction:c.cmd.recoveryAction===undefined?undefined:txt(c.cmd.recoveryAction)},
        {expectedVersion:ver(c,'gsvComplaint',c.cmd.id)});}},
    {type:'GSV_COMPLAINT_RESOLVE',reload:['servicesX'],
      precheck:(cmd:Cmd)=>txt(cmd.rootCause).length>=3?undefined:'Record the root cause before resolving.',
      async send(c){await c.api.command('ResolveComplaint',{complaintId:num(c.cmd.id,'Complaint'),rootCause:optTxt(c.cmd.rootCause),recoveryAction:optTxt(c.cmd.recoveryAction),
        followUpOn:optTxt(c.cmd.followUpOn)},{expectedVersion:ver(c,'gsvComplaint',c.cmd.id)});}},
    // The discount itself is posted (or sent for approval) by Billing on the server; this module only links it to the complaint.
    {type:'GSV_COMPLAINT_RECOVERY',reload:['servicesX','bookings'],
      precheck:(cmd:Cmd)=>Number.isSafeInteger(cmd.amount)&&cmd.amount>0?undefined:'Enter the discount amount.',
      async send(c){const r=await c.api.command<{outcome:string;approvalId:number|null}>('OfferRecoveryDiscount',{complaintId:num(c.cmd.id,'Complaint'),amountPaise:c.cmd.amount,
        reason:optTxt(c.cmd.reason)},{expectedVersion:ver(c,'gsvComplaint',c.cmd.id)});return {result:r};}},
    {type:'GSV_COMPLAINT_FOLLOWUP',reload:['servicesX'],
      precheck:(cmd:Cmd)=>txt(cmd.outcome).length>=3?undefined:'Record what the guest said.',
      async send(c){await c.api.command('RecordComplaintFollowUp',{complaintId:num(c.cmd.id,'Complaint'),outcome:txt(c.cmd.outcome),satisfied:!!c.cmd.satisfied},
        {expectedVersion:ver(c,'gsvComplaint',c.cmd.id)});}},

    {type:'GSV_LF_LOG',reload:['servicesX'],
      precheck:(cmd:Cmd)=>need('Describe the item.',cmd.description)??need('Enter where the item was found.',cmd.foundWhere),
      async send(c){await c.api.command('LogLostItem',{description:txt(c.cmd.description),category:c.cmd.category,foundWhere:txt(c.cmd.foundWhere),storage:c.cmd.storage,
        roomNo:optTxt(c.cmd.room),foundBy:optId(c.cmd.foundBy,'Finder')});}},
    {type:'GSV_LF_CLAIM',reload:['servicesX'],
      precheck:(cmd:Cmd)=>need('Enter the claimant’s name.',cmd.name)??need('Record an ID check note with only the last 4 characters — never the full ID number.',cmd.idNote),
      async send(c){await c.api.command('ClaimLostItem',{itemId:num(c.cmd.id,'Item'),name:txt(c.cmd.name),phone:txt(c.cmd.phone),idType:c.cmd.idType,idNote:txt(c.cmd.idNote),
        matchNote:txt(c.cmd.matchNote)},{expectedVersion:ver(c,'gsvItem',c.cmd.id)});}},
    {type:'GSV_LF_HANDOVER',reload:['servicesX'],
      async send(c){await c.api.command('HandOverLostItem',{itemId:num(c.cmd.id,'Item'),to:optTxt(c.cmd.to),note:optTxt(c.cmd.note)},{expectedVersion:ver(c,'gsvItem',c.cmd.id)});}},
    {type:'GSV_LF_DISPOSE',reload:['servicesX'],
      async send(c){await c.api.command('DisposeLostItem',{itemId:num(c.cmd.id,'Item'),method:c.cmd.method,note:optTxt(c.cmd.note)},{expectedVersion:ver(c,'gsvItem',c.cmd.id)});}},

    {type:'GSV_SETTINGS',reload:['servicesX'],
      async send(c){const v=c.ids.version('gsvSettings','1');
        await c.api.command('SaveGuestServiceSettings',{slaMinutes:c.cmd.sla,retentionDays:c.cmd.retentionDays,valuableRetentionDays:c.cmd.valuableRetentionDays},{expectedVersion:v});}},
  ],
  unmapped:{},
};
