// Yield & tariffs (W4-11) — sync area part. Server: GET /yield/{calendar,config,proposals,publishes}, POST /commands/{SaveYieldPlan,
// SaveYieldSeason, SetYieldDayOfWeek, SaveYieldOccupancyRule, SetYieldGuardrails, SaveYieldRestriction, SetYieldOverride,
// PublishYieldTariffs, ProposeYieldPublish, DecideYieldProposal} (08 Production/src/Hcc.Api/Endpoints/YieldEndpoints.cs).
//   slices   yieldX    ← config (plans, seasons, dow, occupancy rules, guardrails, restrictions, overrides, forecast, history) + publishes
//            approvals ← proposals (kind 'yield', Manager → Owner). NOTE: no other area owns `approvals` yet; when one does, change
//                        this loader to kind:'patch' (it then runs after the owner and appends the yield proposals).
//   ids      room types: type code ↔ server id, learned from the calendar; plan id = plan code (BAR, CORP, WKND, LONG7, DAYUSE);
//            seasons / rules / restrictions / proposals = String(server id). Versions (expectedVersion): kinds yieldPlan, yieldSeason,
//            yieldOcc, yieldRestriction, yieldProposal and yieldSettings/x (day-of-week + guardrails share one settings version).
import type {Approval,Command} from '../../../services/mock';
import type {YLDState,YLDPlan,YLDSeason,YLDOccRule,YLDRestriction,YLDOverride,YLDHistory,YLDPublish} from '../../../domain/streams/yield';
import {HccClientError} from '../../errors';
import type {CommandCtx,CommandMapper,LoaderCtx,SliceLoader} from '../types';
import type {IdDirectory} from '../map';
import {serverId,utcToClock,paise} from '../map';

// ── server DTOs (Hcc.Contracts/Yield) ──
type SrvNight={roomTypeId:number;roomType:string;date:string};
type SrvCalendar={cells:SrvNight[]};
type SrvPlan={planCode:string;name:string;adjBps:number;minNights:number;sellOnDays?:number[]|null;dayUseFrom?:string|null;dayUseTo?:string|null;closeAtOcc?:number|null;active:boolean;note:string;version:string};
type SrvSeason={seasonId:number;name:string;from:string;to:string;adjBps:number;roomTypeIds:number[];priority:number;version:string};
type SrvOcc={ruleId:number;op:string;occ:number;adjBps:number;version:string};
type SrvRestriction={restrictionId:number;roomTypeId?:number|null;from:string;to:string;minStay:number;closedToArrival:boolean;note:string;version:string};
type SrvOverride={roomTypeId:number;night:string;ratePaise:number;reason:string;actorId:number;setUtc:string};
type SrvHistory={historyId:number;atUtc:string;actorId:number;area:string;before:string;after:string;reason:string};
type SrvConfig={plans:SrvPlan[];seasons:SrvSeason[];dow:number[];floorBps:number;ceilBps:number;roundToPaise:number;settingsVersion:string;occupancyRules:SrvOcc[];
  restrictions:SrvRestriction[];overrides:SrvOverride[];forecast:{night:string;roomsSold:number}[];history:SrvHistory[]};
type SrvProposal={proposalId:number;status:string;from:string;to:string;roomTypeIds:number[];changedNights:number;proposedBy:number;proposedUtc:string;expiresUtc:string;
  reason:string;decidedBy?:number|null;decidedUtc?:string|null;decisionNote?:string|null;publishId?:number|null;version:string};
type SrvPublish={publishId:number;from:string;to:string;actorId:number;publishedUtc:string;reason:string;proposalId?:number|null;
  rows:{roomTypeId:number;night:string;beforePaise:number;afterPaise:number;closing:boolean}[]};

const typeCode=(ids:IdDirectory,id:number)=>ids.proto('roomType',id)??String(id);
const userName=(c:LoaderCtx,id:number)=>{const u=(c.current('users')??[]).find(x=>x.id===String(id));return u?u.name:`User ${id}`;};

/** Server room type id for a prototype type code (learned from the calendar). */
export function roomTypeId(ids:IdDirectory,code:string):number{
  const n=ids.server('roomType',code);
  if(n===undefined)throw new HccClientError('VALIDATION',`Room type ${code} is not known on the server yet. Refresh and try again.`);
  return n;
}
function needVersion(c:CommandCtx,kind:string,id:string):string{
  const v=c.ids.version(kind,id);
  if(v===undefined)throw new HccClientError('VALIDATION','This record has not been loaded from the server yet. Refresh and try again.');
  return v;
}

export function planFromServer(p:SrvPlan):YLDPlan{
  const out:YLDPlan={id:p.planCode,code:p.planCode,name:p.name,adjBps:p.adjBps,minNights:p.minNights,active:p.active,note:p.note};
  if(p.sellOnDays&&p.sellOnDays.length)out.nights=[...p.sellOnDays];
  if(p.dayUseFrom&&p.dayUseTo)out.dayUse={from:p.dayUseFrom,to:p.dayUseTo,closeAtOcc:p.closeAtOcc??90};
  return out;
}

async function learnTypes(c:LoaderCtx){
  const cal=await c.api.get<SrvCalendar>('/yield/calendar',{nights:1});
  for(const x of cal.cells)c.ids.learn('roomType',x.roomType,x.roomTypeId);
}

async function loadYield(c:LoaderCtx):Promise<YLDState>{
  await learnTypes(c);
  const [cfg,pubs]=await Promise.all([c.api.get<SrvConfig>('/yield/config'),c.api.get<SrvPublish[]>('/yield/publishes')]);
  for(const p of cfg.plans)c.ids.setVersion('yieldPlan',p.planCode,p.version);
  for(const x of cfg.seasons)c.ids.setVersion('yieldSeason',String(x.seasonId),x.version);
  for(const x of cfg.occupancyRules)c.ids.setVersion('yieldOcc',String(x.ruleId),x.version);
  for(const x of cfg.restrictions)c.ids.setVersion('yieldRestriction',String(x.restrictionId),x.version);
  c.ids.setVersion('yieldSettings','x',cfg.settingsVersion);
  const seasons:YLDSeason[]=cfg.seasons.map(x=>({id:String(x.seasonId),name:x.name,from:x.from,to:x.to,adjBps:x.adjBps,types:x.roomTypeIds.map(t=>typeCode(c.ids,t)),priority:x.priority}));
  const occ:YLDOccRule[]=cfg.occupancyRules.map(x=>({id:String(x.ruleId),op:x.op as '>='|'<',occ:x.occ,adjBps:x.adjBps}));
  const restrictions:YLDRestriction[]=cfg.restrictions.map(x=>({id:String(x.restrictionId),type:x.roomTypeId==null?'*':typeCode(c.ids,x.roomTypeId),from:x.from,to:x.to,minStay:x.minStay,cta:x.closedToArrival,note:x.note??''}));
  const overrides:YLDOverride[]=cfg.overrides.map(x=>({type:typeCode(c.ids,x.roomTypeId),date:x.night,rate:paise(x.ratePaise,'override'),reason:x.reason,actor:String(x.actorId),at:utcToClock(x.setUtc)}));
  const forecast:Record<string,number>={};for(const f of cfg.forecast)forecast[f.night]=f.roomsSold;
  const history:YLDHistory[]=cfg.history.slice().sort((a,b)=>b.historyId-a.historyId).map(h=>({id:String(h.historyId),at:utcToClock(h.atUtc),actor:userName(c,h.actorId),area:h.area,before:h.before,after:h.after,reason:h.reason??''}));
  const newestFirst=pubs.slice().sort((a,b)=>b.publishId-a.publishId);
  const publishes:YLDPublish[]=newestFirst.map(p=>({id:String(p.publishId),at:utcToClock(p.publishedUtc),actor:userName(c,p.actorId),from:p.from,to:p.to,
    types:[...new Set(p.rows.map(r=>typeCode(c.ids,r.roomTypeId)))],reason:p.reason??'',approval:p.proposalId?String(p.proposalId):undefined,
    rows:p.rows.map(r=>({type:typeCode(c.ids,r.roomTypeId),date:r.night,before:paise(r.beforePaise,'before'),after:paise(r.afterPaise,'after'),...(r.closing?{closing:true}:{})}))}));
  // Tariff rows written by a publish stay "owned" (excluded from the base rate) — oldest first so the latest publish wins.
  const owned:Record<string,number>={};
  for(const p of newestFirst.slice().reverse())for(const r of p.rows)owned[typeCode(c.ids,r.roomTypeId)+'|'+r.night]=r.afterPaise;
  return {plans:cfg.plans.map(planFromServer),seasons,dow:[...cfg.dow],occ,guard:{floorBps:cfg.floorBps,ceilBps:cfg.ceilBps,roundTo:cfg.roundToPaise},
    restrictions,overrides,forecast,owned,history,publishes};
}

/** Proposals as prototype approvals (kind 'yield'). meta.types is [] and meta.sig '[]' (= yldTariffSig(s,[])) so the screen shows
 *  Pending; the server decides staleness: a Stale proposal gets meta.sig 'stale' (the screen shows "Stale"). */
export function proposalToApproval(c:LoaderCtx,p:SrvProposal):Approval{
  const types=p.roomTypeIds.map(t=>typeCode(c.ids,t));
  const st=p.status;
  const status:Approval['status']=st==='Pending'||st==='Stale'?'Pending':st==='Executed'?'Executed':st==='Rejected'?'Rejected':st==='Expired'?'Expired':'Invalidated';
  c.ids.setVersion('yieldProposal',String(p.proposalId),p.version);
  const a:Approval={id:String(p.proposalId),booking:'',amount:0,reason:p.reason,status,actor:String(p.proposedBy),version:1,kind:'yield',target:`${p.from}→${p.to}`,
    expiresAt:utcToClock(p.expiresUtc),meta:{command:'YLD_DECIDE',from:p.from,to:p.to,types:[],rooms:types,sig:st==='Stale'?'stale':'[]',changed:p.changedNights,
      summary:`Tariff publish ${p.from} to ${p.to} · ${types.join(', ')} · ${p.changedNights} night rate${p.changedNights===1?'':'s'}`,...(p.publishId?{publish:String(p.publishId)}:{})}};
  if(p.decidedBy!=null)a.approver=String(p.decidedBy);
  if(p.decidedUtc)a.decidedAt=utcToClock(p.decidedUtc);
  return a;
}
async function loadProposals(c:LoaderCtx):Promise<Approval[]>{
  if(!c.ids.list('roomType').length)await learnTypes(c);
  const list=await c.api.get<SrvProposal[]>('/yield/proposals');
  return list.slice().sort((a,b)=>a.proposalId-b.proposalId).map(p=>proposalToApproval(c,p));
}

export const yieldLoaders:SliceLoader[]=[
  {slice:'yieldX',requires:'reports',reads:['GET /yield/calendar','GET /yield/config','GET /yield/publishes'],load:loadYield},
  // approvals is owned by the admin area (W4-5); yield proposals are patched in (kind 'yield'), other kinds kept.
  {slice:'approvals',kind:'patch',requires:'reports',reads:['GET /yield/proposals'],async load(c){const mine=await loadProposals(c);return [...((c.current('approvals') as Approval[]|undefined)??[]).filter(a=>a.kind!=='yield'),...mine];}},
];

const reason=(cmd:Command)=>(cmd.reason===undefined||cmd.reason===null)?undefined:String(cmd.reason).trim()||undefined;
const typesPrecheck=(cmd:Command)=>Array.isArray(cmd.types)&&cmd.types.length?undefined:'Choose at least one room type.';
type Body=Record<string,unknown>;

export const yieldCommands:CommandMapper[]=[
  {type:'YLD_PLAN_SAVE',reload:['yieldX'],async send(c){const m=c.cmd;
    await c.api.command('SaveYieldPlan',{planCode:m.id,adjBps:m.adjBps,minNights:m.minNights,active:m.active,closeAtOcc:m.closeAtOcc,reason:reason(m)},
      {expectedVersion:needVersion(c,'yieldPlan',String(m.id))});}},
  {type:'YLD_SEASON_SAVE',reload:['yieldX'],async send(c){const m=c.cmd;
    const ver=m.id?needVersion(c,'yieldSeason',String(m.id)):undefined;
    const body:Body=m.remove?{seasonId:serverId(m.id,'Season'),remove:true,reason:reason(m)}
      :{seasonId:m.id?serverId(m.id,'Season'):undefined,name:m.name,from:m.from,to:m.to,adjBps:m.adjBps,
        roomTypeIds:(Array.isArray(m.types)?m.types as string[]:[]).map(t=>roomTypeId(c.ids,t)),priority:m.priority,reason:reason(m)};
    await c.api.command('SaveYieldSeason',body,{expectedVersion:ver});}},
  {type:'YLD_DOW_SET',reload:['yieldX'],async send(c){const m=c.cmd;
    await c.api.command('SetYieldDayOfWeek',{dow:m.dow,reason:reason(m)},{expectedVersion:needVersion(c,'yieldSettings','x')});}},
  {type:'YLD_OCC_SAVE',reload:['yieldX'],async send(c){const m=c.cmd;
    const ver=m.id?needVersion(c,'yieldOcc',String(m.id)):undefined;
    const body:Body=m.remove?{ruleId:serverId(m.id,'Rule'),remove:true,reason:reason(m)}
      :{ruleId:m.id?serverId(m.id,'Rule'):undefined,op:m.op,occ:m.occ,adjBps:m.adjBps,reason:reason(m)};
    await c.api.command('SaveYieldOccupancyRule',body,{expectedVersion:ver});}},
  {type:'YLD_GUARD_SET',reload:['yieldX'],async send(c){const m=c.cmd;
    await c.api.command('SetYieldGuardrails',{floorBps:m.floorBps,ceilBps:m.ceilBps,roundToPaise:m.roundTo,reason:reason(m)},{expectedVersion:needVersion(c,'yieldSettings','x')});}},
  {type:'YLD_RESTRICTION_SAVE',reload:['yieldX'],async send(c){const m=c.cmd;
    const ver=m.id?needVersion(c,'yieldRestriction',String(m.id)):undefined;
    const body:Body=m.remove?{restrictionId:serverId(m.id,'Restriction'),remove:true,reason:reason(m)}
      :{restrictionId:m.id?serverId(m.id,'Restriction'):undefined,roomTypeId:(m.roomType??'*')==='*'?null:roomTypeId(c.ids,String(m.roomType)),from:m.from,to:m.to,
        minStay:m.minStay,closedToArrival:!!m.cta,note:m.note,reason:reason(m)};
    await c.api.command('SaveYieldRestriction',body,{expectedVersion:ver});}},
  {type:'YLD_OVERRIDE',reload:['yieldX'],
    precheck:cmd=>String(cmd.reason??'').trim().length>=3?undefined:'Give a reason for the override.',
    async send(c){const m=c.cmd;
      await c.api.command('SetYieldOverride',{roomTypeId:roomTypeId(c.ids,String(m.roomType)),night:m.date,ratePaise:m.rate===undefined?null:m.rate,reason:String(m.reason).trim()});}},
  {type:'YLD_PUBLISH',reload:['yieldX','approvals'],precheck:typesPrecheck,async send(c){const m=c.cmd;
    await c.api.command('PublishYieldTariffs',{from:m.from,to:m.to,roomTypeIds:(m.types as string[]).map(t=>roomTypeId(c.ids,t)),reason:reason(m)});
    return {reload:['tariffs']};}},
  {type:'YLD_PROPOSE',reload:['yieldX','approvals'],precheck:typesPrecheck,async send(c){const m=c.cmd;
    await c.api.command('ProposeYieldPublish',{from:m.from,to:m.to,roomTypeIds:(m.types as string[]).map(t=>roomTypeId(c.ids,t)),reason:reason(m)});}},
  {type:'YLD_DECIDE',reload:['yieldX','approvals'],async send(c){const m=c.cmd;
    await c.api.command('DecideYieldProposal',{proposalId:serverId(m.id,'Request'),approve:!!m.approve,note:m.note?String(m.note):undefined},
      {expectedVersion:needVersion(c,'yieldProposal',String(m.id))});
    return {reload:['tariffs']};}},
];
