// Stream E · Access & configuration (owns USER, GRANT, SETTINGS).
// Register new or replacement command handlers here. A handler with the same name as a built-in command replaces it.
// New: USER_CREATE, USER_UPDATE, POLICY_SET, USER_EXCEPTION, POLICY_DECIDE, SETTING_SET, TARIFF_SCHEDULE, TAX_SCHEDULE.
// W05-A Master configuration: SETTING_SET (registry-driven, every group), CFG_DECIDE, CFG_WITHDRAW, CFG_REVERT, CFG_IMPORT.
import type {Handler,Kit} from '../commands';
import type {Role,Permission,State,User,Command} from '../../../domain/types';
import {roles,permissions} from '../../../domain/types';
import {money,addMinutes} from '../../../domain/money';
import {registry} from './index';
import {cfgDef,cfgCanChange,cfgValidate,cfgClean,cfgEffectiveFrom,cfgValueOn,cfgSame,cfgShow,cfgEnsure,cfgWriteDirect,cfgPendingRequests,cfgDecideBlock,
  cfgRevertBlock,cfgChange,cfgImportPlan,type CfgSettingDef} from '../../../domain/streams/config';
import {authorize,adminPolicy,adminPerm,adminOtherActiveOwners,adminDescribeCell,adminDefaultPolicy,adminTaxScheduled,adminRoomTypes,ADMIN_FINANCIAL_MODULES,
  type PolicyCell,type PolicyEffect,type PolicyScope,type HccPolicy} from '../../../domain/streams/admin';

const DATE=/^\d{4}-\d{2}-\d{2}$/;
const first=(u:User|undefined)=>u?u.name.split(' · ')[0]:'unknown';
function policyOf(s:State):HccPolicy{if(!s.policy)s.policy=adminDefaultPolicy(s.clock);return s.policy;}
function bump(k:Kit,summary:string,before:string,after:string,reason:string){
  const p=policyOf(k.s);p.version++;
  p.history.unshift({version:p.version,at:addMinutes(k.s.clock,1),actor:k.user.name,summary,before,after,reason:reason||'—'});
  return p.version;
}

// ─────────────── Policy changes (GRANT, POLICY_SET, USER_EXCEPTION) ───────────────
type Planned={summary:string;before:string;after:string;target:string;apply:()=>void};
/** Validates a policy change for `actor` (requester, or the approver on execution) and returns how to apply it. */
function plan(k:Kit,actor:User,c:Command):Planned{
  const {s,assert,fail}=k;const pol=policyOf(s);
  if(c.type==='GRANT'){
    assert(roles.includes(c.role)&&permissions.includes(c.permission),'Invalid role or permission.');
    assert(c.role!=='Owner','Owner template is protected in this prototype.','FORBIDDEN');
    assert(c.role!==actor.role,'You cannot change the permissions of your own role.','FORBIDDEN');
    assert(c.permission!=='admin','Administrator delegation is outside this prototype.','FORBIDDEN');
    const arr=s.grants[c.role as Role],has=arr.includes(c.permission);
    if(!has)assert(!(c.role==='Tech admin'&&ADMIN_FINANCIAL_MODULES.includes(c.permission)),'Tech admin cannot hold financial actions (technical and financial power are kept separate).');
    return {summary:`Module access · ${c.role} · ${c.permission} ${has?'removed':'granted'}`,before:has?'on':'off',after:has?'off':'on',target:c.role,
      apply:()=>{s.grants[c.role as Role]=has?arr.filter(p=>p!==c.permission):[...arr,c.permission as Permission];}};
  }
  if(c.type==='POLICY_SET'){
    assert(roles.includes(c.role),'Choose a role.');
    const p=adminPerm(c.perm);assert(p,'Choose a permission from the access matrix.');
    assert(c.role!=='Owner','Owner template is protected in this prototype.','FORBIDDEN');
    assert(c.role!==actor.role,'You cannot change the permissions of your own role.','FORBIDDEN');
    const role=c.role as Role,tpl=pol.templates[role]||(pol.templates[role]={});
    const before=tpl[c.perm];
    if(c.limit!==undefined){
      assert(p!.limit,'This permission has no configurable limit.');
      assert(Number.isSafeInteger(c.limit)&&c.limit>=0&&(p!.limit!.unit!=='percent'||c.limit<=100),p!.limit!.unit==='percent'?'Enter a percentage between 0 and 100.':'Enter a limit of ₹0 or more.');
      const old=pol.limits[role]?.[c.perm];const fmt=(n:number|undefined)=>n===undefined?'not set':p!.limit!.unit==='percent'?n+'%':money(n);
      return {summary:`Limit · ${role} · ${c.perm} ${p!.limit!.label}`,before:fmt(old),after:fmt(c.limit),target:role,
        apply:()=>{(pol.limits[role]||(pol.limits[role]={}))[c.perm]=c.limit;}};
    }
    const effect=c.effect as PolicyEffect|'none';
    assert(['allow','deny','limit','request','none'].includes(effect),'Choose Allow, Limit, Request, Deny or Not granted.');
    const scope=(c.scope||before?.scope||'P') as PolicyScope;
    assert(['P','S','A','O'].includes(scope),'Choose a scope: property, own shift, assigned or own.');
    if(effect==='limit')assert(p!.limit,'Only permissions with a limit (discount, rate, cash difference, credit, expense, approvals) can use Limit.');
    if(['allow','limit','request'].includes(effect)){
      assert(!(role==='Tech admin'&&p!.financial),'Tech admin cannot hold financial actions (technical and financial power are kept separate).');
      assert(!['PERM-ADM-001','PERM-ADM-002'].includes(c.perm),'Administrator delegation is outside this prototype.','FORBIDDEN');
    }
    const next:PolicyCell|undefined=effect==='none'?undefined:{effect,scope,...(c.mode&&effect==='allow'?{mode:c.mode}:{})};
    assert(JSON.stringify(before)!==JSON.stringify(next),'No change: the template already has this value.');
    return {summary:`Role template · ${role} · ${c.perm} ${p!.action}`,before:adminDescribeCell(before),after:adminDescribeCell(next),target:role,
      apply:()=>{if(next)tpl[c.perm]=next;else delete tpl[c.perm];}};
  }
  if(c.type==='USER_EXCEPTION'){
    if(c.remove){
      const e=pol.exceptions.find(e=>e.id===c.remove);assert(e,'Exception not found.','NOT_FOUND');
      assert(e!.user!==actor.id,'You cannot change exceptions on your own access.','FORBIDDEN');
      return {summary:`Exception removed · ${e!.id} · ${first(s.users.find(u=>u.id===e!.user))} · ${e!.perm}`,before:`${e!.effect} until ${e!.expires} (${e!.reason})`,after:'removed',target:e!.user,
        apply:()=>{pol.exceptions=pol.exceptions.filter(x=>x.id!==c.remove);}};
    }
    const target=s.users.find(u=>u.id===c.user);assert(target,'Choose a user.');
    const p=adminPerm(c.perm);assert(p,'Choose a permission from the access matrix.');
    assert(['allow','deny'].includes(c.effect),'Choose Allow or Deny.');
    assert(c.reason?.trim().length>=3,'Give a reason for the exception.');
    assert(typeof c.expires==='string'&&DATE.test(c.expires),'Choose an expiry date.');
    assert(c.expires>=s.date,'Expiry cannot be before the current business date.');
    assert(target!.id!==actor.id,'You cannot add exceptions to your own access (no self-escalation).','FORBIDDEN');
    if(target!.role==='Owner')assert(actor.role==='Owner','Only an Owner can change another Owner’s access.','FORBIDDEN');
    if(target!.role==='Owner'&&c.effect==='deny')assert(!['PERM-ADM-001','PERM-ADM-002'].includes(c.perm),'Owner administration cannot be removed by exception (PERM-SOD-003).','FORBIDDEN');
    if(c.effect==='allow'){
      assert(!(target!.role==='Tech admin'&&p!.financial),'Tech admin cannot hold financial actions (technical and financial power are kept separate).');
      assert(!['PERM-ADM-001','PERM-ADM-002','PERM-ADM-003'].includes(c.perm),'Administrator delegation is outside this prototype.','FORBIDDEN');
      assert(target!.role!==actor.role,'You cannot grant extra access to users of your own role.','FORBIDDEN');
    }
    const eid=k.id('EXC');
    return {summary:`Exception · ${first(target)} · ${c.perm} ${p!.action}`,before:'inherit (role template)',after:`${c.effect} until ${c.expires} — ${c.reason.trim()}`,target:target!.id,
      apply:()=>{pol.exceptions.push({id:eid,user:target!.id,perm:c.perm,effect:c.effect,reason:c.reason.trim(),expires:c.expires,by:actor.id,at:s.clock});}};
  }
  return fail('VALIDATION','Unknown policy change.');
}
/** Run a policy change for the current user: apply (Owner) or raise an Owner-approval request (Tech admin, PERM-ADM-002 = R). */
function policyCommand(k:Kit){
  const {s,uid,user,cmd,req,fail}=k;req('admin');
  const d=authorize(s,uid,'PERM-ADM-002');
  const pl=plan(k,user,cmd);
  if(d.decision==='deny')fail('FORBIDDEN',d.reason);
  if(d.decision==='approval'){
    const pol=policyOf(s);
    const change={...cmd};delete change.key;
    const hours=s.settings.approvalExpiryHours??24;
    const aid=k.id('APR');
    s.approvals.push({id:aid,booking:'',amount:0,reason:String(cmd.reason||pl.summary),status:'Pending',actor:uid,version:pol.version,kind:'policy',target:pl.target,
      expiresAt:addMinutes(s.clock,hours*60),meta:{command:'POLICY_DECIDE',change,summary:pl.summary,before:pl.before,after:pl.after}});
    return `${aid} · policy change requested (Owner approval): ${pl.summary} · ${pl.before} → ${pl.after}`;
  }
  pl.apply();const v=bump(k,pl.summary,pl.before,pl.after,cmd.reason||'');
  return `${pl.summary} · ${pl.before} → ${pl.after} · policy v${v}`;
}

// ─────────────── Users ───────────────
/** DEC-033: an Owner may be disabled or demoted only while another active (enabled, unexpired) Owner remains. */
function ownerGuard(k:Kit,target:User,losing:boolean){
  if(target.role!=='Owner')return;
  if(losing)k.assert(adminOtherActiveOwners(k.s,target.id).length>0,'You cannot disable or demote the last active owner (DEC-033). Enable another owner first.','FORBIDDEN');
}
const usersGate=(k:Kit)=>{k.req('admin');const d=authorize(k.s,k.uid,'PERM-ADM-001');if(d.decision!=='allow')k.fail('FORBIDDEN',d.reason);};

const USER:Handler=k=>{
  const {s,uid,cmd,assert}=k;usersGate(k);
  const target=s.users.find(x=>x.id===cmd.id);assert(target,'User not found.','NOT_FOUND');
  assert(target!.id!==uid,'Use another owner account to change your own access.','FORBIDDEN');
  ownerGuard(k,target!,target!.enabled);
  target!.enabled=!target!.enabled;
  return target!.name+' · '+(target!.enabled?'enabled':'disabled')+' · before → after: '+(target!.enabled?'disabled → enabled':'enabled → disabled');
};
const USER_CREATE:Handler=k=>{
  const {s,cmd,assert,user}=k;usersGate(k);
  const name=String(cmd.name||'').trim();
  assert(name.length>=2&&name.length<=40,'Enter the person’s name (2–40 characters).');
  assert(roles.includes(cmd.role),'Choose a role.');
  assert(cmd.role!=='Owner'||user.role==='Owner','Only an Owner can create another Owner account.','FORBIDDEN');
  if(cmd.expiresOn)assert(DATE.test(cmd.expiresOn)&&cmd.expiresOn>=s.date,'Expiry must be today or a later business date.');
  const full=`${name} · ${cmd.role}`;
  assert(!s.users.some(u=>u.name.toLowerCase()===full.toLowerCase()),'A user with this name and role already exists.');
  const id=k.id('user');
  s.users.push({id,name:full,role:cmd.role,enabled:true,...(cmd.expiresOn?{expiresOn:cmd.expiresOn}:{}),assignedBy:user.id,assignedOn:s.date});
  return `${id} · ${full} created${cmd.expiresOn?' · access until '+cmd.expiresOn:''}`;
};
const USER_UPDATE:Handler=k=>{
  const {s,uid,cmd,assert,user}=k;usersGate(k);
  const t=s.users.find(x=>x.id===cmd.id);assert(t,'User not found.','NOT_FOUND');
  const changes:string[]=[];
  const self=t!.id===uid;
  if(cmd.name!==undefined){const n=String(cmd.name).trim();assert(n.length>=2&&n.length<=40,'Enter the person’s name (2–40 characters).');const full=n+' · '+(cmd.role??t!.role);if(full!==t!.name){changes.push(`name ${t!.name} → ${full}`);}}
  if(cmd.role!==undefined&&cmd.role!==t!.role){
    assert(!self,'Use another owner account to change your own access.','FORBIDDEN');
    assert(roles.includes(cmd.role),'Choose a role.');
    ownerGuard(k,t!,true);
    assert(cmd.role!=='Owner'||user.role==='Owner','Only an Owner can assign the Owner role (no escalation).','FORBIDDEN');
    changes.push(`role ${t!.role} → ${cmd.role}`);
  }
  if(cmd.enabled!==undefined&&!!cmd.enabled!==t!.enabled){
    assert(!self,'Use another owner account to change your own access.','FORBIDDEN');
    ownerGuard(k,t!,t!.enabled);changes.push(cmd.enabled?'disabled → enabled':'enabled → disabled');
  }
  if(cmd.expiresOn!==undefined&&(cmd.expiresOn||undefined)!==t!.expiresOn){
    assert(!self,'Use another owner account to change your own access.','FORBIDDEN');
    if(cmd.expiresOn){
      assert(DATE.test(cmd.expiresOn)&&cmd.expiresOn>=s.date,'Expiry must be today or a later business date.');
      if(t!.role==='Owner'){ownerGuard(k,t!,false);assert(adminOtherActiveOwners(s,t!.id).some(o=>!o.expiresOn),'The last active owner without an expiry cannot be given one (DEC-033).','FORBIDDEN');}
    }
    changes.push(`expiry ${t!.expiresOn||'none'} → ${cmd.expiresOn||'none'}`);
  }
  assert(changes.length,'Nothing changed.');
  const role=(cmd.role??t!.role) as Role;
  if(cmd.name!==undefined)t!.name=String(cmd.name).trim()+' · '+role;else if(role!==t!.role)t!.name=t!.name.split(' · ')[0]+' · '+role;
  if(role!==t!.role){t!.role=role;t!.assignedBy=uid;t!.assignedOn=s.date;}
  if(cmd.enabled!==undefined)t!.enabled=!!cmd.enabled;
  if(cmd.expiresOn!==undefined){if(cmd.expiresOn)t!.expiresOn=cmd.expiresOn;else delete t!.expiresOn;}
  return `${t!.id} · ${changes.join('; ')}`;
};

// ─────────────── Approval decision for policy requests ───────────────
const POLICY_DECIDE:Handler=k=>{
  const {s,uid,user,cmd,assert,fail,req}=k;req('admin');
  const a=s.approvals.find(a=>a.id===cmd.id&&a.kind==='policy');
  assert(a&&a.status==='Pending','This request is no longer pending.','INVALID_TRANSITION');
  assert(a!.actor!==uid,'You cannot approve your own request. Switch to another authorized demo user.','FORBIDDEN');
  const d=authorize(s,uid,'PERM-ADM-002');if(d.decision!=='allow')fail('FORBIDDEN','Only an Owner can decide policy requests. '+d.reason);
  a!.approver=uid;a!.decidedAt=addMinutes(s.clock,1);
  if(!cmd.approve){a!.status='Rejected';if(String(cmd.reason??'').trim())a!.meta={...a!.meta,rejectReason:String(cmd.reason).trim().slice(0,200)} as any;return `${a!.id} · Rejected · ${a!.meta?.summary??''}`;}
  assert(!(a!.expiresAt&&s.clock>a!.expiresAt),'This request has expired. Reject it and ask for a new one.','INVALID_TRANSITION');
  assert(a!.version===policyOf(s).version,'The policy changed after this request. Reject it and request again.','STALE_VERSION');
  const change=a!.meta!.change as Command;
  // DEC-041 step 6: re-check the change for the executor (separation of duty applies to the approver too).
  const pl=plan(k,user,change);pl.apply();
  const requester=first(s.users.find(u=>u.id===a!.actor));
  const v=bump(k,pl.summary,pl.before,pl.after,`Approved request ${a!.id} from ${requester}${a!.reason?' — '+a!.reason:''}`);
  a!.status='Executed';
  return `${a!.id} · approved and applied · ${pl.summary} · ${pl.before} → ${pl.after} · policy v${v}`;
};

// ─────────────── Configuration (SCR-18) ───────────────
const cfgGate=(k:Kit)=>{k.req('admin');const d=authorize(k.s,k.uid,'PERM-ADM-003',{act:true});
  if(d.decision==='approval')k.fail('FORBIDDEN','Your role may only request configuration changes; ask the Owner to make this change.');
  if(d.decision!=='allow')k.fail('FORBIDDEN',k.user.role==='Tech admin'?'Tech admin has view-only access to hotel configuration (PERM-ADM-003).':d.reason);};
function history(k:Kit,key:string,before:string,after:string,effectiveFrom:string,reason=''){
  (k.s.configHistory||(k.s.configHistory=[])).unshift({id:k.id('CFG'),key,before,after,at:addMinutes(k.s.clock,1),actor:k.user.name,effectiveFrom,reason});
}
const show=(key:string,v:unknown)=>key==='discountLimit'?money(Number(v)):key==='taxBps'?Number(v)/100+'%':key==='inspection'?(v?'On':'Off'):key==='approvalExpiryHours'?v+' h':String(v??'—');
function setTaxToday(k:Kit,bps:number){
  const {s}=k;
  if(s.taxSchedule?.length){s.taxSchedule=s.taxSchedule.filter(t=>t.effectiveFrom!==s.date);s.taxSchedule.push({bps,effectiveFrom:s.date});}
  s.settings.taxBps=bps;
}
const SETTINGS:Handler=k=>{
  const {s,cmd,assert}=k;cfgGate(k);assert(cmd.hotel?.trim(),'Property name is required.');
  assert(Number.isInteger(cmd.taxBps)&&cmd.taxBps>=0&&cmd.taxBps<=3000,'Use a synthetic tax between 0% and 30%.');
  assert(Number.isSafeInteger(cmd.discountLimit)&&cmd.discountLimit>=0,'Invalid discount threshold.');
  const next={hotel:cmd.hotel.trim(),taxBps:cmd.taxBps,discountLimit:cmd.discountLimit,inspection:!!cmd.inspection,checkin:cmd.checkin,checkout:cmd.checkout};
  const curTax=adminTaxScheduled(s,s.date);
  for(const key of Object.keys(next) as (keyof typeof next)[]){
    const before=key==='taxBps'?curTax:s.settings[key];
    if(before!==next[key])history(k,key,show(key,before),show(key,next[key]),s.date);
  }
  // Merge (other streams' settings fields are preserved). Tax goes through the schedule so it stays consistent.
  const {taxBps,...rest}=next;s.settings={...s.settings,...rest};if(taxBps!==curTax)setTaxToday(k,taxBps);
  return 'Demo policy updated · existing posted line tax snapshots preserved';
};
// ─────────────── W05-A Master configuration (registry-driven; domain/streams/config.ts) ───────────────
// SETTING_SET {setting,value,effectiveFrom?,reason?} saves any registry setting: DEC-058 permission → validation → effective date →
// approval (Tech admin on settings with an approver) → history (configHistory + raw change for revert) → apply now or schedule.
// Module wrappers run the module's own command (its validation; the hub's permission check replaces the module's `req`).
const showOf=(def:CfgSettingDef,v:unknown)=>cfgShow(def,v);
function cfgApply(k:Kit,def:CfgSettingDef,value:unknown,effectiveFrom:string,reason:string,approvedBy?:string,revertOf?:string){
  const {s,assert}=k;const x=cfgEnsure(s);
  const before=cfgValueOn(s,def.key,effectiveFrom);
  assert(!cfgSame(before,value),'No change: the setting already has this value.');
  const hid=k.id('CFG'),at=addMinutes(s.clock,1);
  (s.configHistory||(s.configHistory=[])).unshift({id:hid,key:def.key,before:showOf(def,before),after:showOf(def,value),at,actor:k.user.name,effectiveFrom,reason});
  const now=effectiveFrom<=s.date;
  x.changes.unshift({id:hid,key:def.key,before,after:value,effectiveFrom,at,by:k.uid,...(approvedBy?{approvedBy}:{}),status:now?'applied':'scheduled',...(revertOf?{revertOf}:{})});
  if(now){
    if(def.store.kind==='module'){
      const mc=def.store.command(s,value);const h=registry[mc.type];
      if(!h)k.fail('VALIDATION',`${def.label} cannot be saved: ${mc.type} is not available.`);
      h({...k,cmd:{...mc,reason},req:()=>{}});
    }else cfgWriteDirect(s,def,value);
  }else{
    // One scheduled value per key and date: a new one for the same date replaces it.
    for(const e of x.scheduled.filter(e=>e.key===def.key&&e.effectiveFrom===effectiveFrom)){const c=x.changes.find(c=>c.id===e.id);if(c)c.status='withdrawn';}
    x.scheduled=x.scheduled.filter(e=>!(e.key===def.key&&e.effectiveFrom===effectiveFrom));
    x.scheduled.push({id:hid,key:def.key,value,effectiveFrom,by:k.uid,...(approvedBy?{approvedBy}:{}),at});
  }
  return {hid,before,now};
}
/** Validate and route one change for the current user. Returns the audit text. */
function cfgChangeCommand(k:Kit,def:CfgSettingDef,raw:unknown,requested:string|undefined,reason:string,revertOf?:string):string{
  const {s,uid,fail}=k;
  const dec=cfgCanChange(s,uid,def);
  if(dec.mode==='deny')fail('FORBIDDEN',dec.reason);
  const value=cfgClean(def,raw);
  const err=cfgValidate(s,def,value);if(err)fail('VALIDATION',err);
  const eff=cfgEffectiveFrom(s,def,requested);if('error' in eff)return fail('VALIDATION',eff.error);
  const effectiveFrom=eff.date;
  if(dec.mode==='request'){
    k.assert(!cfgPendingRequests(s,def.key).length,'A change to this setting is already waiting for approval.','INVALID_TRANSITION');
    const before=cfgValueOn(s,def.key,effectiveFrom);k.assert(!cfgSame(before,value),'No change: the setting already has this value.');
    const aid=k.id('APR');const hours=s.settings.approvalExpiryHours??24;
    const summary=`Master configuration · ${def.label} · ${showOf(def,before)} → ${showOf(def,value)}`;
    s.approvals.push({id:aid,booking:'',amount:0,reason:reason?`${summary} — ${reason}`:summary,status:'Pending',actor:uid,version:0,kind:'config',target:def.key,
      expiresAt:addMinutes(s.clock,hours*60),meta:{command:'CFG_DECIDE',key:def.key,value,effectiveFrom,beforeRaw:before,approver:dec.approver,summary,before:showOf(def,before),after:showOf(def,value),...(revertOf?{revertOf}:{})} as any});
    return `${aid} · configuration change requested (${dec.approver} approval): ${def.key} · ${showOf(def,before)} → ${showOf(def,value)}`;
  }
  const r=cfgApply(k,def,value,effectiveFrom,reason,undefined,revertOf);
  return `Setting ${def.key} · ${showOf(def,r.before)} → ${showOf(def,value)} · ${r.now?'effective':'scheduled from'} ${effectiveFrom}${revertOf?' · reverts '+revertOf:''}`;
}
const SETTING_SET:Handler=k=>{
  const {s,cmd,fail}=k;k.req('admin');
  if(cmd.setting==='taxBps')return fail('VALIDATION','Synthetic tax is effective-dated: use the tax schedule.');
  const def=cfgDef(s,String(cmd.setting));
  if(!def||!def.editable)return fail('VALIDATION','This setting is not configurable.');
  return cfgChangeCommand(k,def,cmd.value,cmd.effectiveFrom||undefined,String(cmd.reason||'').trim().slice(0,120));
};
const CFG_DECIDE:Handler=k=>{
  // No 'admin' gate: a Manager decides requests whose approver is Manager (from Approvals & audit). cfgDecideBlock is the gate.
  const {s,uid,cmd,assert,fail}=k;
  const a=s.approvals.find(a=>a.id===cmd.id&&a.kind==='config');
  assert(a&&a.status==='Pending','This request is no longer pending.','INVALID_TRANSITION');
  const block=cfgDecideBlock(s,uid,a!.id);if(block)fail('FORBIDDEN',block);
  const m=a!.meta as Record<string,unknown>;
  if(!cmd.approve){a!.approver=uid;a!.decidedAt=addMinutes(s.clock,1);a!.status='Rejected';
    if(String(cmd.reason??'').trim())a!.meta={...a!.meta,rejectReason:String(cmd.reason).trim().slice(0,200)} as any;return `${a!.id} · Rejected · ${String(m.summary??'')}`;}
  assert(!(a!.expiresAt&&s.clock>a!.expiresAt),'This request has expired. Reject it and ask for a new one.','INVALID_TRANSITION');
  const def=cfgDef(s,String(m.key));assert(def,'This setting is no longer in the registry.','NOT_FOUND');
  let effectiveFrom=String(m.effectiveFrom);
  if(effectiveFrom<s.date){const e=cfgEffectiveFrom(s,def!);effectiveFrom='date' in e?e.date:s.date;}
  assert(cfgSame(cfgValueOn(s,def!.key,effectiveFrom),m.beforeRaw),'The setting changed after this request. Reject it and request again.','STALE_VERSION');
  const err=cfgValidate(s,def!,m.value);if(err)fail('VALIDATION',err);
  // An Owner approver is re-checked like a direct change (e.g. a user exception denying PERM-ADM-003); DEC-041/058.
  if(k.user.role==='Owner'){const dec=cfgCanChange(s,uid,def!);if(dec.mode!=='allow')fail('FORBIDDEN',dec.reason);}
  a!.approver=uid;a!.decidedAt=addMinutes(s.clock,1);
  const requester=first(s.users.find(u=>u.id===a!.actor));
  const r=cfgApply(k,def!,m.value,effectiveFrom,`Approved request ${a!.id} from ${requester}`,uid,m.revertOf as string|undefined);
  a!.status='Executed';
  return `${a!.id} · approved and applied · ${def!.key} · ${showOf(def!,r.before)} → ${showOf(def!,m.value)} · ${r.now?'effective':'scheduled from'} ${effectiveFrom}`;
};
const CFG_WITHDRAW:Handler=k=>{
  const {s,uid,cmd,assert,fail}=k;k.req('admin');const x=cfgEnsure(s);
  const e=x.scheduled.find(e=>e.id===cmd.id);assert(e,'Scheduled change not found (it may already be in effect).','NOT_FOUND');
  assert(e!.effectiveFrom>s.date,'Only future-dated changes can be withdrawn.','INVALID_TRANSITION');
  const def=cfgDef(s,e!.key);assert(def,'This setting is no longer in the registry.','NOT_FOUND');
  const dec=cfgCanChange(s,uid,def!);if(dec.mode!=='allow')fail('FORBIDDEN',dec.mode==='request'?'Ask the approver to withdraw this scheduled change.':dec.reason);
  x.scheduled=x.scheduled.filter(z=>z.id!==e!.id);const c=x.changes.find(c=>c.id===e!.id);if(c)c.status='withdrawn';
  (s.configHistory||(s.configHistory=[])).unshift({id:k.id('CFG'),key:def!.key,before:showOf(def!,e!.value)+' from '+e!.effectiveFrom,after:'withdrawn',at:addMinutes(s.clock,1),actor:k.user.name,effectiveFrom:e!.effectiveFrom,reason:String(cmd.reason||'').trim()});
  return `Scheduled change withdrawn · ${def!.key} · ${showOf(def!,e!.value)} from ${e!.effectiveFrom}`;
};
const CFG_REVERT:Handler=k=>{
  const {s,cmd,fail}=k;k.req('admin');
  const block=cfgRevertBlock(s,String(cmd.id));if(block)fail('INVALID_TRANSITION',block);
  const c=cfgChange(s,String(cmd.id))!;const def=cfgDef(s,c.key)!;
  // A revert is a normal change back to the previous value: same permission, approval and effective-date rules.
  return cfgChangeCommand(k,def,c.before,cmd.effectiveFrom||undefined,String(cmd.reason||'Revert to previous value').trim(),c.id);
};
const CFG_IMPORT:Handler=k=>{
  const {s,uid,cmd,fail}=k;k.req('admin');
  const plan=cfgImportPlan(s,uid,cmd.config);
  if(plan.errors.length)fail('VALIDATION',plan.errors.slice(0,3).join(' · ')+(plan.errors.length>3?` (+${plan.errors.length-3} more)`:''));
  if(!plan.apply.length)fail('VALIDATION',`Nothing to import: ${plan.unchanged} unchanged, ${plan.skipped.length} skipped.`);
  for(const p of plan.apply)cfgApply(k,p.def,p.value,p.effectiveFrom,'Imported configuration');
  return `Configuration imported · ${plan.apply.length} changed · ${plan.unchanged} unchanged · ${plan.skipped.length} skipped`;
};

const TARIFF_SCHEDULE:Handler=k=>{
  const {s,cmd,assert}=k;cfgGate(k);
  assert(adminRoomTypes(s).includes(cmd.roomType),'Choose a room type.');
  assert(typeof cmd.effectiveFrom==='string'&&DATE.test(cmd.effectiveFrom),'Choose an effective date.');
  const list=s.tariffs||(s.tariffs=[]);
  if(cmd.remove){
    assert(cmd.effectiveFrom>s.date,'Only future-dated tariffs can be withdrawn; bookings may already use this one.');
    const at=list.findIndex(t=>t.type===cmd.roomType&&t.effectiveFrom===cmd.effectiveFrom);assert(at>=0,'Scheduled tariff not found.','NOT_FOUND');
    const [old]=list.splice(at,1);history(k,'tariff:'+cmd.roomType,money(old.rate)+' from '+old.effectiveFrom,'withdrawn',old.effectiveFrom,cmd.reason||'');
    return `Tariff ${cmd.roomType} from ${cmd.effectiveFrom} withdrawn`;
  }
  assert(cmd.effectiveFrom>=s.date,'Effective date cannot be before the current business date.');
  assert(Number.isSafeInteger(cmd.rate)&&cmd.rate>0&&cmd.rate<=10000000,'Enter a nightly rate greater than zero (maximum ₹1,00,000).');
  const room=s.rooms.find(r=>r.type===cmd.roomType)!;
  const prev=list.filter(t=>t.type===cmd.roomType&&t.effectiveFrom<=cmd.effectiveFrom).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1).slice(-1)[0]?.rate??room.rate;
  s.tariffs=list.filter(t=>!(t.type===cmd.roomType&&t.effectiveFrom===cmd.effectiveFrom));s.tariffs.push({type:cmd.roomType,rate:cmd.rate,effectiveFrom:cmd.effectiveFrom});
  history(k,'tariff:'+cmd.roomType,money(prev),money(cmd.rate),cmd.effectiveFrom,cmd.reason||'');
  return `Tariff ${cmd.roomType} · ${money(prev)} → ${money(cmd.rate)} from ${cmd.effectiveFrom} · existing bookings keep agreed rates`;
};
const TAX_SCHEDULE:Handler=k=>{
  const {s,cmd,assert}=k;cfgGate(k);
  assert(typeof cmd.effectiveFrom==='string'&&DATE.test(cmd.effectiveFrom),'Choose an effective date.');
  const list=s.taxSchedule||(s.taxSchedule=[]);
  if(cmd.remove){
    assert(cmd.effectiveFrom>s.date,'Only future-dated tax rates can be withdrawn.');
    const at=list.findIndex(t=>t.effectiveFrom===cmd.effectiveFrom);assert(at>=0,'Scheduled tax rate not found.','NOT_FOUND');
    const [old]=list.splice(at,1);history(k,'taxBps',old.bps/100+'% from '+old.effectiveFrom,'withdrawn',old.effectiveFrom,cmd.reason||'');
    return `Synthetic tax from ${cmd.effectiveFrom} withdrawn`;
  }
  assert(cmd.effectiveFrom>=s.date,'Effective date cannot be before the current business date.');
  assert(Number.isInteger(cmd.bps)&&cmd.bps>=0&&cmd.bps<=3000,'Use a synthetic tax between 0% and 30%.');
  const prev=adminTaxScheduled(s,cmd.effectiveFrom);
  s.taxSchedule=list.filter(t=>t.effectiveFrom!==cmd.effectiveFrom);s.taxSchedule.push({bps:cmd.bps,effectiveFrom:cmd.effectiveFrom});
  if(cmd.effectiveFrom===s.date)s.settings.taxBps=cmd.bps;
  history(k,'taxBps',prev/100+'%',cmd.bps/100+'%',cmd.effectiveFrom,cmd.reason||'');
  return `Synthetic tax ${prev/100}% → ${cmd.bps/100}% from ${cmd.effectiveFrom} · posted lines keep their snapshot`;
};

export const handlers:Record<string,Handler>={
  USER,USER_CREATE,USER_UPDATE,
  GRANT:policyCommand,POLICY_SET:policyCommand,USER_EXCEPTION:policyCommand,POLICY_DECIDE,
  SETTINGS,SETTING_SET,TARIFF_SCHEDULE,TAX_SCHEDULE,
  CFG_DECIDE,CFG_WITHDRAW,CFG_REVERT,CFG_IMPORT,
};
