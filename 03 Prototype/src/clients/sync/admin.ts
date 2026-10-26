// Admin area (W4-5): users, role templates / fine-grained policy, user exceptions, approvals & audit, Master configuration.
// Screens: features/admin (Access control, Approvals & audit, Master configuration + its dialogs) and mobile/admin.
// Server (08 Production): Access — GET /admin/users, /admin/roles, /admin/exceptions, /admin/audit; commands CreateUser,
// SetUserEnabled, AssignRole, RevokeRole, SetCredential, GrantException, RevokeException, RegisterDevice, RevokeDevice.
// Configuration — GET /configuration/values (every user), /configuration/history, /configuration/requests; commands SetSetting,
// DecideSettingRequest, WithdrawScheduledSetting, RevertSetting, ImportConfiguration.
//
//   slices   users ← /admin/users · policy ← /admin/roles + /admin/exceptions (admins) or the signed-in user's own capabilities
//            (everyone else: their role's template = what /me/capabilities says they hold, so authorize() follows the server) ·
//            grants ← the loaded role templates through PERMISSION_MAP · approvals ← configuration requests (kind 'config') ·
//            audit ← /admin/audit (PERM-ADM-004) · configX (+ history/changes) ← /configuration/values + /configuration/history ·
//            configHistory ← /configuration/history · settings ← the legacy keys of configX.values.
//   ids      users / exceptions / setting changes = String(server id); configuration requests "CFG-REQ-<id>" (the approvals slice
//            mixes kinds from several server tables). Versions: 'user' (rowversion, sent as expectedVersion — the server requires it
//            for user changes), 'cfgRequest'.
import type {State,Command,Role,Approval,Audit,User,Permission} from '../../services/mock';
import {ADMIN_ROLE_ORDER,addDays} from '../../services/mock';
import type {HccPolicy,PolicyCell,PolicyException,CFGState,CFGChange,CFGScheduled} from '../../services/mock';
import {HccClientError} from '../errors';
import type {SyncArea,LoaderCtx,CommandCtx,SyncMe} from './types';
import {serverId,protoId,utcToClock,clockToUtc,utcToDate} from './map';
import {ROLE_MAP,permissionsFromGrants} from './capabilities';

// ───────────── Server DTOs (Hcc.Contracts.Access / Configuration) ─────────────
export type SrvGrant={permissionId:string;mode:string;scope:string;limit?:number|null;limitUnit?:string|null};
export type SrvUserRole={roleCode:string;validFromUtc:string;validUntilUtc?:string|null};
export type SrvUser={userId:number;loginName:string;displayName:string;enabled:boolean;credentialKind:string;lockedOut:boolean;roles:SrvUserRole[];version:string};
export type SrvPage<T>={items:T[];page:number;pageSize:number;total:number};
export type SrvRole={roleCode:string;name:string;credentialKind:string;ownerOnlyAssign:boolean;grants:SrvGrant[];denies:{permissionId:string;reason:string}[]};
export type SrvRoles={roles:SrvRole[];permissions:{permissionId:string;resourceAction:string;kind:string;isMoney:boolean}[]};
export type SrvException={exceptionId:number;userId:number;permissionId:string;effect:string;mode:string;scope:string;limit?:number|null;limitUnit?:string|null;
  reason:string;validUntilUtc:string;grantedBy:number;grantedUtc:string;revokedUtc?:string|null};
export type SrvAudit={auditId:number;atUtc:string;principalId:number;actor:string;action:string;target:string;detail:string;operation?:string|null};
export type SrvValues={businessDate:string;values:Record<string,unknown>;scheduled:{key:string;value:unknown;effectiveFrom:string}[]};
export type SrvHistoryEntry={changeId:number;key:string;label:string;before:string;after:string;effectiveFrom:string;status:string;changedBy:string;approvedBy?:string|null;
  reason?:string|null;source:string;revertOf?:number|null;at:string;canRevert:boolean;revertBlock?:string|null};
export type SrvRequest={requestId:number;key:string;label:string;summary:string;reason?:string|null;approver:string;requestedBy:string;requestedAt:string;expiresAt:string;
  status:string;decidedBy?:string|null;decisionReason?:string|null;effectiveFrom:string;version:string;decideBlock?:string|null};

// ───────────── Mapping helpers ─────────────
/** Prototype role → server role code (inverse of ROLE_MAP). */
export function roleCode(role:string):string{
  const hit=Object.entries(ROLE_MAP).find(([,r])=>r===role);
  if(!hit)throw new HccClientError('VALIDATION',`Choose a role (${role} is not a server role).`);
  return hit[0];
}
/** Last valid business date (inclusive) ↔ server "valid until" instant = the next day 00:00 hotel time. */
export const untilUtcOf=(expiresOn:string)=>clockToUtc(addDays(expiresOn,1)+'T00:00');
export function expiresOnOf(untilUtc:string|null|undefined):string|undefined{
  const t=untilUtc?Date.parse(untilUtc):NaN;return Number.isFinite(t)?utcToDate(new Date(t-1).toISOString()):undefined;
}
const ROLE_RANK=Object.keys(ROLE_MAP);
/** Server user → prototype User (one role per user in the prototype: the highest-ranked role still valid, else the latest). */
export function userFromServer(u:SrvUser,date:string):User{
  const rs=[...u.roles].sort((a,b)=>ROLE_RANK.indexOf(a.roleCode)-ROLE_RANK.indexOf(b.roleCode));
  const live=rs.filter(r=>{const e=expiresOnOf(r.validUntilUtc);return !e||e>=date;});
  const r=live[0]??[...rs].sort((a,b)=>String(b.validUntilUtc).localeCompare(String(a.validUntilUtc)))[0];
  const role=(r&&ROLE_MAP[r.roleCode])||'Reception';
  const out:User={id:protoId(u.userId),name:u.displayName,role,enabled:u.enabled};
  const exp=expiresOnOf(r?.validUntilUtc);if(exp)out.expiresOn=exp;
  if(r?.validFromUtc)out.assignedOn=utcToDate(r.validFromUtc);
  return out;
}
/** Server grant (mode A/L/R/V/M, scope P/S/A/O) → prototype policy cell. */
export function cellOf(g:SrvGrant):PolicyCell{
  const scope=(['P','S','A','O'].includes(g.scope)?g.scope:'P') as PolicyCell['scope'];
  switch(String(g.mode).toUpperCase()){
    case 'L':return {effect:'limit',scope};
    case 'R':return {effect:'request',scope};
    case 'V':return {effect:'allow',scope,mode:'view'};
    case 'M':return {effect:'allow',scope,mode:'masked'};
    default:return {effect:'allow',scope};
  }
}
/** Prototype cell → server mode letter (for the coarse module gate). */
const modeOf=(c:PolicyCell)=>c.effect==='limit'?'L':c.effect==='request'?'R':c.mode==='view'?'V':c.mode==='masked'?'M':'A';
/** Limit in the prototype unit: paise stay paise; basis points become whole percent (rate override). */
const limitOf=(g:SrvGrant)=>g.limit==null?undefined:String(g.limitUnit).toUpperCase()==='BPS'?Math.round(g.limit/100):g.limit;

function emptyTemplates<T>():Record<Role,Record<string,T>>{return Object.fromEntries(ADMIN_ROLE_ORDER.map(r=>[r,{}])) as unknown as Record<Role,Record<string,T>>;}
/** Policy from the server role templates (+ exceptions). Server role templates have no version history: version stays 1. */
export function policyFromServer(roles:SrvRoles|null,exceptions:SrvException[],me:SyncMe):HccPolicy{
  const templates=emptyTemplates<PolicyCell>(),limits=emptyTemplates<number>();
  const put=(role:Role,grants:SrvGrant[],denies:{permissionId:string}[])=>{
    templates[role]={};limits[role]={};
    for(const g of grants){templates[role][g.permissionId]=cellOf(g);const l=limitOf(g);if(l!==undefined&&String(g.mode).toUpperCase()==='L')limits[role][g.permissionId]=l;}
    for(const d of denies)templates[role][d.permissionId]={effect:'deny',scope:'P'};
  };
  if(roles)for(const r of roles.roles){const role=ROLE_MAP[r.roleCode];if(role)put(role,r.grants,r.denies);}
  // Not an administrator (or the roles read failed): the signed-in user's own role follows what the server says they hold.
  if(!roles)put(me.role,me.grants.map(g=>({...g})),[]);
  const exc:PolicyException[]=exceptions.filter(e=>!e.revokedUtc).map(e=>({id:protoId(e.exceptionId),user:protoId(e.userId),perm:e.permissionId,
    effect:String(e.effect).toUpperCase()==='DENY'?'deny':'allow',reason:e.reason,expires:expiresOnOf(e.validUntilUtc)??'',by:protoId(e.grantedBy),at:utcToClock(e.grantedUtc)}));
  return {version:1,templates,limits,exceptions:exc,history:[]};
}
/** Coarse module gate per role, from the policy cells through PERMISSION_MAP (same rule as can() for the signed-in user). */
export function grantsFromPolicy(p:HccPolicy):Record<Role,Permission[]>{
  return Object.fromEntries(ADMIN_ROLE_ORDER.map(r=>[r,permissionsFromGrants(Object.entries(p.templates[r]??{}).filter(([,c])=>c.effect!=='deny')
    .map(([permissionId,c])=>({permissionId,mode:modeOf(c)})))])) as unknown as Record<Role,Permission[]>;
}
export const cfgRequestId=(requestId:number)=>'CFG-REQ-'+requestId;
const REQUEST_STATUS:Record<string,Approval['status']>={Pending:'Pending',Approved:'Approved',Rejected:'Rejected',Expired:'Expired',Executed:'Executed',Invalidated:'Invalidated',Withdrawn:'Invalidated'};
export function approvalFromRequest(r:SrvRequest,me:SyncMe,users:User[]):Approval{
  const actor=r.requestedBy===me.displayName?me.uid:users.find(u=>u.name===r.requestedBy)?.id??r.requestedBy;
  const approver=r.decidedBy?(users.find(u=>u.name===r.decidedBy)?.id??r.decidedBy):undefined;
  return {id:cfgRequestId(r.requestId),booking:'',amount:0,reason:r.reason?`${r.summary} — ${r.reason}`:r.summary,status:REQUEST_STATUS[r.status]??'Pending',actor,version:0,
    kind:'config',target:r.key,expiresAt:utcToClock(r.expiresAt)||undefined,...(approver?{approver}:{}),
    meta:{command:'CFG_DECIDE',key:r.key,approver:r.approver,summary:r.summary,effectiveFrom:r.effectiveFrom,serverDecideBlock:r.decideBlock??undefined}};
}
export const auditFromServer=(a:SrvAudit):Audit=>({id:protoId(a.auditId),at:utcToClock(a.atUtc),actor:a.actor,action:a.action,detail:a.target?`${a.target} · ${a.detail}`:a.detail});

/** Prototype s.settings keys held in Master configuration (store kind 'settings'). */
export const SETTINGS_KEYS=['hotel','discountLimit','inspection','checkin','checkout','invoicePrefix','approvalExpiryHours'] as const;
const STATUS:Record<string,CFGChange['status']>={applied:'applied',scheduled:'scheduled',withdrawn:'withdrawn'};
/** configX from the values in force + history (scheduled change ids and revert state come from the history). */
export function configFromServer(v:SrvValues,history:SrvHistoryEntry[]):CFGState{
  const values={...v.values};
  const modulesOff=Object.entries(values).filter(([k,x])=>k.startsWith('module.')&&x===false).map(([k])=>k.slice('module.'.length));
  const scheduled:CFGScheduled[]=v.scheduled.map(x=>{const h=history.find(e=>e.key===x.key&&e.effectiveFrom===x.effectiveFrom&&e.status==='scheduled');
    return {id:h?protoId(h.changeId):`sched:${x.key}:${x.effectiveFrom}`,key:x.key,value:x.value,effectiveFrom:x.effectiveFrom,by:h?.changedBy??'',at:h?utcToClock(h.at):''};});
  // cfgRevertBlock needs `after` = the live value for the one revertible change; others get a value that never matches.
  const changes:CFGChange[]=history.map(h=>({id:protoId(h.changeId),key:h.key,before:undefined,after:h.canRevert?values[h.key]:{notRevertible:h.revertBlock??true},
    effectiveFrom:h.effectiveFrom,at:utcToClock(h.at),by:h.changedBy,...(h.approvedBy?{approvedBy:h.approvedBy}:{}),status:STATUS[h.status]??'applied',
    ...(h.revertOf!=null?{revertOf:protoId(h.revertOf)}:{})}));
  return {modulesOff,values,scheduled,changes};
}
export const historyFromServer=(h:SrvHistoryEntry[]):NonNullable<State['configHistory']>=>h.map(e=>({id:protoId(e.changeId),key:e.key,before:e.before,after:e.after,
  at:utcToClock(e.at),actor:e.changedBy,effectiveFrom:e.effectiveFrom,reason:e.reason??''}));
export function settingsFromConfig(x:CFGState|undefined,skeleton:State['settings']):State['settings']{
  const v=x?.values??{};
  const str=(k:string,d:string)=>typeof v[k]==='string'?String(v[k]):d,num=(k:string,d:number)=>Number.isSafeInteger(v[k])?Number(v[k]):d;
  return {hotel:str('hotel',''),discountLimit:num('discountLimit',skeleton.discountLimit),inspection:typeof v.inspection==='boolean'?v.inspection:skeleton.inspection,
    checkin:str('checkin',skeleton.checkin),checkout:str('checkout',skeleton.checkout),invoicePrefix:str('invoicePrefix',skeleton.invoicePrefix??''),
    approvalExpiryHours:num('approvalExpiryHours',skeleton.approvalExpiryHours??24)};
}

// ───────────── Loaders ─────────────
const forbidden=(e:unknown)=>e instanceof HccClientError&&(e.wireCode==='FORBIDDEN'||e.wireCode==='NOT_FOUND');
async function optional<T>(f:()=>Promise<T>,fallback:T):Promise<T>{try{return await f();}catch(e){if(forbidden(e))return fallback;throw e;}}
const isAdmin=(c:LoaderCtx)=>c.can('admin');
const PAGE=100; // ApiConventions.MaxPageSize

async function allPages<T>(c:LoaderCtx,path:string,max=10):Promise<T[]>{
  const out:T[]=[];
  for(let page=1;page<=max;page++){const r=await c.api.get<SrvPage<T>>(path,{page,pageSize:PAGE});out.push(...r.items);if(out.length>=r.total||!r.items.length)break;}
  return out;
}
async function loadUsers(c:LoaderCtx):Promise<User[]>{
  const list=await allPages<SrvUser>(c,'/admin/users');const date=c.current('date');
  for(const u of list){c.ids.learn('user',protoId(u.userId),u.userId);c.ids.setVersion('user',protoId(u.userId),u.version);}
  return list.map(u=>userFromServer(u,date)).sort((a,b)=>Number(a.id)-Number(b.id));
}
async function loadPolicy(c:LoaderCtx):Promise<HccPolicy>{
  if(!isAdmin(c))return policyFromServer(null,[],c.me);
  const roles=await optional(()=>c.api.get<SrvRoles>('/admin/roles'),null);
  const exc=await optional(()=>c.api.get<{items:SrvException[]}>('/admin/exceptions'),{items:[]});
  for(const e of exc.items)c.ids.learn('exception',protoId(e.exceptionId),e.exceptionId);
  return policyFromServer(roles,exc.items,c.me);
}
async function loadApprovals(c:LoaderCtx):Promise<Approval[]>{
  const r=await optional(()=>c.api.get<{items:SrvRequest[]}>('/configuration/requests',{status:''}),{items:[]});
  for(const x of r.items){c.ids.learn('cfgRequest',cfgRequestId(x.requestId),x.requestId);c.ids.setVersion('cfgRequest',cfgRequestId(x.requestId),x.version);}
  return r.items.map(x=>approvalFromRequest(x,c.me,c.current('users')??[]));
}
async function loadAudit(c:LoaderCtx):Promise<Audit[]>{
  if(!c.me.permIds.includes('PERM-ADM-004'))return [];
  const r=await c.api.get<SrvPage<SrvAudit>>('/admin/audit',{page:1,pageSize:PAGE});
  return r.items.map(auditFromServer);
}
async function loadHistory(c:LoaderCtx):Promise<SrvHistoryEntry[]>{
  if(!isAdmin(c))return [];
  return optional(()=>c.api.get<SrvPage<SrvHistoryEntry>>('/configuration/history',{page:1,pageSize:PAGE}).then(r=>r.items),[]);
}
async function loadConfig(c:LoaderCtx):Promise<CFGState>{
  const v=await c.api.get<SrvValues>('/configuration/values');
  return configFromServer(v,await loadHistory(c));
}

// ───────────── Command mappers ─────────────
const userOf=(s:State,id:unknown)=>s.users.find(u=>u.id===String(id??''));
function userVersion(c:CommandCtx,id:string):string{
  const v=c.ids.version('user',id);if(!v)throw new HccClientError('VALIDATION','This user is not loaded from the server yet. Refresh and try again.');return v;
}
/** Sign-in name for a new user: from the dialog when it has one, else derived from the name (letters/digits, dots; ≥ 3 characters). */
export function loginFor(cmd:Command):string{
  const raw=String(cmd.login??cmd.name??'').normalize('NFKD').toLowerCase().replace(/[^a-z0-9]+/g,'.').replace(/^\.+|\.+$/g,'').slice(0,40);
  return raw.length>=3?raw:(raw+'.user').replace(/^\./,'').slice(0,40);
}
const noSelf=(cmd:Command,me:SyncMe)=>String(cmd.id??'')===me.uid?'Use another administrator account to change your own access.':undefined;

const usersReload=['users','policy','grants'] as const;
const configReload=['configX','settings','configHistory','approvals'] as const;

export const admin:SyncArea={
  id:'admin',
  loaders:[
    {slice:'users',requires:'admin',needs:['date'],load:loadUsers},
    {slice:'policy',load:loadPolicy},
    {slice:'grants',needs:['policy'],requires:'admin',async load(c){return grantsFromPolicy(c.current('policy')!);}},
    {slice:'approvals',needs:['users'],requires:['admin','approve'],load:loadApprovals},
    {slice:'audit',load:loadAudit},
    {slice:'configX',load:loadConfig},
    {slice:'configHistory',requires:'admin',async load(c){return historyFromServer(await loadHistory(c));}},
    {slice:'settings',needs:['configX'],async load(c){return settingsFromConfig(c.current('configX'),c.current('settings'));}},
  ],
  commands:[
    {type:'USER',reload:[...usersReload],
      precheck:(cmd,s,me)=>noSelf(cmd,me)??(userOf(s,cmd.id)?undefined:'User not found.'),
      async send(c){const u=userOf(c.state,c.cmd.id)!;
        await c.api.command('SetUserEnabled',{userId:serverId(u.id,'User'),enabled:!u.enabled,expectedVersion:userVersion(c,u.id)});}},
    {type:'USER_CREATE',reload:[...usersReload],
      precheck:cmd=>{const n=String(cmd.name??'').trim();return n.length<2||n.length>40?'Enter the person’s name (2–40 characters).':!ROLE_RANK.some(k=>ROLE_MAP[k]===cmd.role)?'Choose a role.':undefined;},
      async send(c){const cmd=c.cmd;
        const r=await c.api.command<{userId:number;version:string}>('CreateUser',{loginName:loginFor(cmd),displayName:String(cmd.name).trim(),
          roles:[{roleCode:roleCode(cmd.role),validFromUtc:null,validUntilUtc:cmd.expiresOn?untilUtcOf(cmd.expiresOn):null}]});
        c.ids.learn('user',protoId(r.userId),r.userId);
        return {result:{userId:protoId(r.userId),loginName:loginFor(cmd),credentialNeeded:true}};}},
    {type:'USER_UPDATE',reload:[...usersReload],
      precheck:(cmd,s,me)=>{const u=userOf(s,cmd.id);if(!u)return 'User not found.';
        if(cmd.name!==undefined&&String(cmd.name).trim()!==u.name)return 'Renaming a user is not available on the server yet (no server command) — change role, expiry or status only.';
        if((cmd.role!==undefined||cmd.enabled!==undefined||cmd.expiresOn!==undefined)&&u.id===me.uid)return noSelf(cmd,me);
        return cmd.role===undefined&&cmd.enabled===undefined&&cmd.expiresOn===undefined?'Nothing changed.':undefined;},
      async send(c){const cmd=c.cmd,u=userOf(c.state,cmd.id)!,userId=serverId(u.id,'User');let version=userVersion(c,u.id);
        const role=(cmd.role??u.role) as Role;const expiresOn=cmd.expiresOn!==undefined?(cmd.expiresOn||undefined):u.expiresOn;
        const until=expiresOn?untilUtcOf(expiresOn):null;
        if(role!==u.role||cmd.expiresOn!==undefined){
          version=(await c.api.command<{version:string}>('AssignRole',{userId,roleCode:roleCode(role),validFromUtc:null,validUntilUtc:until,expectedVersion:version},{step:'role'})).version;
          if(role!==u.role)version=(await c.api.command<{version:string}>('RevokeRole',{userId,roleCode:roleCode(u.role),expectedVersion:version},{step:'revoke'})).version;
        }
        if(cmd.enabled!==undefined&&!!cmd.enabled!==u.enabled)await c.api.command('SetUserEnabled',{userId,enabled:!!cmd.enabled,expectedVersion:version},{step:'enabled'});}},
    {type:'USER_EXCEPTION',reload:[...usersReload],
      precheck:(cmd,s)=>{
        if(cmd.remove)return s.policy?.exceptions.some(e=>e.id===cmd.remove)?undefined:'Exception not found.';
        if(!userOf(s,cmd.user))return 'Choose a user.';
        if(String(cmd.reason??'').trim().length<3)return 'Give a reason for the exception.';
        return /^\d{4}-\d{2}-\d{2}$/.test(String(cmd.expires??''))&&cmd.expires>=s.date?undefined:'Expiry cannot be before the current business date.';},
      async send(c){const cmd=c.cmd;
        if(cmd.remove){const e=c.state.policy!.exceptions.find(x=>x.id===cmd.remove)!;
          await c.api.command('RevokeException',{userId:serverId(e.user,'User'),exceptionId:serverId(e.id,'Exception'),expectedVersion:userVersion(c,e.user)});return;}
        await c.api.command('GrantException',{userId:serverId(cmd.user,'User'),permissionId:String(cmd.perm),effect:cmd.effect==='deny'?'DENY':'ALLOW',mode:'A',scope:'P',
          limit:null,limitUnit:null,reason:String(cmd.reason).trim(),validUntilUtc:untilUtcOf(cmd.expires),expectedVersion:userVersion(c,String(cmd.user))});}},
    // No screen yet (DEC-062): credentials and shared-device registration. The PIN / password goes only to the server.
    {type:'USER_CREDENTIAL',reload:['users'],
      // A caller-chosen key is required: the runtime's fallback key is a digest of the command, which would include the PIN.
      precheck:(cmd,s,me)=>noSelf(cmd,me)??(!cmd.key?'Open the credential dialog to set a PIN or password.':!userOf(s,cmd.id)?'User not found.':!cmd.pin===!cmd.password?'Give either a PIN or a password.':undefined),
      async send(c){const u=userOf(c.state,c.cmd.id)!;
        await c.api.command('SetCredential',{userId:serverId(u.id,'User'),password:c.cmd.password?String(c.cmd.password):null,pin:c.cmd.pin?String(c.cmd.pin):null,expectedVersion:userVersion(c,u.id)});}},
    {type:'DEVICE_REGISTER',reload:[],precheck:cmd=>String(cmd.label??'').trim()?undefined:'Name the device (e.g. Front desk tablet).',
      async send(c){const r=await c.api.command<{deviceId:number;deviceToken?:string|null}>('RegisterDevice',{label:String(c.cmd.label).trim()});
        return {result:{deviceId:protoId(r.deviceId),deviceToken:r.deviceToken??null}};}},
    {type:'DEVICE_REVOKE',reload:[],async send(c){await c.api.command('RevokeDevice',{deviceId:serverId(c.cmd.id,'Device')});}},
    // Master configuration.
    {type:'SETTING_SET',reload:[...configReload],
      precheck:cmd=>cmd.setting==='taxBps'?'The synthetic tax is replaced by the dated GST master (DEC-069): change GST rates in Master configuration › Money and tax.':cmd.setting?undefined:'Choose a setting.',
      async send(c){const cmd=c.cmd;const reason=String(cmd.reason??'').trim();
        await c.api.command('SetSetting',{key:String(cmd.setting),value:cmd.value??null,...(cmd.effectiveFrom?{effectiveFrom:cmd.effectiveFrom}:{}),...(reason?{reason}:{})});}},
    {type:'CFG_DECIDE',reload:[...configReload],
      async send(c){const cmd=c.cmd;const id=String(cmd.id);const requestId=c.ids.server('cfgRequest',id)??serverId(id.replace(/^CFG-REQ-/,''),'Request');
        const reason=String(cmd.reason??'').trim();
        await c.api.command('DecideSettingRequest',{requestId,approve:!!cmd.approve,...(reason?{reason}:{}),expectedVersion:c.ids.version('cfgRequest',id)??null});}},
    {type:'CFG_WITHDRAW',reload:[...configReload],
      async send(c){const reason=String(c.cmd.reason??'').trim();await c.api.command('WithdrawScheduledSetting',{changeId:serverId(c.cmd.id,'Scheduled change'),...(reason?{reason}:{})});}},
    {type:'CFG_REVERT',reload:[...configReload],
      async send(c){const reason=String(c.cmd.reason??'').trim();
        await c.api.command('RevertSetting',{changeId:serverId(c.cmd.id,'Change'),...(c.cmd.effectiveFrom?{effectiveFrom:c.cmd.effectiveFrom}:{}),...(reason?{reason}:{})});}},
    {type:'CFG_IMPORT',reload:[...configReload],precheck:cmd=>cmd.config&&typeof cmd.config==='object'?undefined:'This is not a Master configuration export file.',
      async send(c){const r=await c.api.command<{changed:number;unchanged:number;skipped:unknown[];message:string}>('ImportConfiguration',{config:c.cmd.config});return {result:r};}},
  ],
  unmapped:{
    GRANT:'role templates are the server seed (0101 + module seeds); no command edits them yet — role matrix v2 (D1–D7) decides',
    POLICY_SET:'no server command edits role templates or role limits yet (same as GRANT)',
    POLICY_DECIDE:'no policy change requests on the server (Tech admin PERM-ADM-002 R has nothing to request yet)',
    TARIFF_SCHEDULE:'dated tariffs are published by the Yield module (PublishYieldTariffs, W4-11); no plain schedule/withdraw command',
    TAX_SCHEDULE:'replaced by the dated GST master (DEC-069) — SETTING_SET on gst.* keys',
    SETTINGS:'legacy BL-027 command; no screen sends it — SETTING_SET per key instead',
    EXPENSE_APPROVE:'expenses area (W4-7) — shown on Approvals & audit',
  },
};
