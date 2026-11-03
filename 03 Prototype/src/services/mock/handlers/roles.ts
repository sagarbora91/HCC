// ADM-2 · Custom roles (DEC-216…221; 02 Planning/agents/ADM_Briefs.md §3.3). Commands ROL_CREATE, ROL_UPDATE, ROL_FEATURE_SET,
// ROL_GRANT_SET, ROL_RETIRE (Owner only: PERM-ADM-002 allow + user.role Owner), and USER_CREATE / USER_UPDATE with `customRole`
// (wrapping the stream E handlers: the user's `role` becomes the custom role's base). Every change bumps the policy version and adds a
// rolesX.history row (who / when / before / after).
import type {Handler,Kit} from '../commands';
import type {State,Role} from '../../../domain/types';
import {addMinutes,money} from '../../../domain/money';
import {authorize,adminPerm,adminDescribeCell,adminDefaultPolicy,type PolicyCell,type PolicyEffect,type PolicyScope} from '../../../domain/streams/admin';
import {featureById} from '../../../domain/feature-catalogue';
import {ROL_ACCESS,rolCustom,rolCanBeBase,rolMoneyLocked,rolSodBlock,rolApplyFeature,rolApplyMoney,rolNextId,rolNewRole,rolHolders,rolList,
  type CustomRole,type RoleAccess,type ROLState} from '../../../domain/streams/roles';
import {handlers as admin} from './admin';

const rolesOf=(s:State):ROLState=>s.rolesX??(s.rolesX={roles:[],history:[]});
/** Owner only (DEC-218): PERM-ADM-002 allow and the Owner role. */
function gate(k:Kit){
  k.req('admin');
  const d=authorize(k.s,k.uid,'PERM-ADM-002');if(d.decision!=='allow')k.fail('FORBIDDEN','Only the Owner edits roles. '+d.reason);
  if(k.user.role!=='Owner'||k.user.customRole)k.fail('FORBIDDEN','Only the Owner edits roles (DEC-218).');
}
/** The custom role named by cmd.id: built-ins refused, never the actor's own role, not retired. */
function target(k:Kit,allowRetired=false):CustomRole{
  const id=String(k.cmd.id??'');
  if(rolCanBeBase(id)||id==='Owner')k.fail('FORBIDDEN','Built-in roles are read-only templates (DEC-216). Create a custom role based on it instead.');
  const r=rolesOf(k.s).roles.find(x=>x.id===id);k.assert(r,'Role not found.','NOT_FOUND');
  k.assert(k.user.customRole!==r!.id,'You cannot change your own role (DEC-218).','FORBIDDEN');
  if(!allowRetired)k.assert(!r!.retired,'This role is retired.','INVALID_TRANSITION');
  return r!;
}
function bump(k:Kit,r:CustomRole,summary:string,before:string,after:string){
  const s=k.s;if(!s.policy)s.policy=adminDefaultPolicy(s.clock);
  s.policy.version++;r.version++;
  rolesOf(s).history.unshift({version:s.policy.version,at:addMinutes(s.clock,1),actor:k.user.name,summary:`${r.id} ${r.name} · ${summary}`,before,after,reason:String(k.cmd.reason??'').trim()||'—'});
}
function checkName(k:Kit,name:string,except?:string){
  k.assert(name.length>=2&&name.length<=40,'Enter a role name (2–40 characters).');
  const low=name.toLowerCase();
  k.assert(!rolList(k.s).some(r=>r.id!==except&&r.name.toLowerCase()===low)&&!['owner','manager','reception','cashier','supervisor','housekeeper','accountant','maintenance','tech admin','restaurant'].includes(low),
    'A role with this name already exists.');
}

const ROL_CREATE:Handler=k=>{
  gate(k);const {s,cmd,assert}=k;
  const name=String(cmd.name??'').trim();checkName(k,name);
  assert(cmd.base!=='Owner','The Owner role cannot be the base of a custom role (DEC-216).','FORBIDDEN');
  assert(rolCanBeBase(cmd.base),'Choose a base role.');
  const base=cmd.base as Role;
  if(cmd.money===true)assert(!rolMoneyLocked(base),`A role based on ${base} never holds money permissions (DEC-061).`);
  const id=rolNextId(s);assert(id,'All 99 custom role codes are in use.');
  const r=rolNewRole(s,{id:id!,name,base,money:cmd.money,by:k.uid});
  rolesOf(s).roles.push(r);r.version=0;
  bump(k,r,'created',`—`,`copy of ${base} · money ${r.money?'on':'off'}`);
  return `${id} · ${name} created (copy of ${base}${r.money?'':', no money'})`;
};
const ROL_UPDATE:Handler=k=>{
  gate(k);const {s,cmd,assert}=k;const r=target(k);
  const changes:string[]=[];const before:string[]=[];const after:string[]=[];
  if(cmd.name!==undefined){const n=String(cmd.name).trim();if(n!==r.name){checkName(k,n,r.id);before.push(r.name);after.push(n);changes.push(`name ${r.name} → ${n}`);r.name=n;
    for(const u of s.users)if(u.customRole===r.id)u.name=u.name.split(' · ')[0]+' · '+n;}}
  if(cmd.money!==undefined&&!!cmd.money!==r.money){
    assert(!(cmd.money&&rolMoneyLocked(r.base)),`A role based on ${r.base} never holds money permissions (DEC-061).`,'FORBIDDEN');
    before.push('money '+(r.money?'on':'off'));after.push('money '+(cmd.money?'on':'off'));changes.push(`money ${r.money?'on → off':'off → on'}`);
    rolApplyMoney(s,r,!!cmd.money);
  }
  assert(changes.length,'Nothing changed.');
  bump(k,r,changes.join('; '),before.join('; '),after.join('; '));
  return `${r.id} · ${changes.join('; ')}`;
};
const ROL_FEATURE_SET:Handler=k=>{
  gate(k);const {s,cmd,assert}=k;const r=target(k);
  const f=featureById(String(cmd.feature??''));assert(f,'Choose a feature from the catalogue.');
  assert(!f!.feature.core,`${f!.feature.label} is a core feature: it follows the base role (DEC-215).`);
  assert(ROL_ACCESS.includes(cmd.access),'Choose Off, View or Full.');
  const snap=()=>JSON.stringify([r.grants,r.template,r.limits]);const was=snap();
  const changed=rolApplyFeature(s,r,f!.id,cmd.access as RoleAccess);
  if(!changed.length&&snap()!==was)changed.push(`${f!.id} full (permissions added)`);
  assert(changed.length,'No change: the role already has this level.');
  bump(k,r,`${f!.feature.label} (${f!.module.label})`,changed.map(c=>c.replace(/ → .*/,'')).join('; '),changed.join('; '));
  return `${r.id} · ${changed.join('; ')}`;
};
const ROL_GRANT_SET:Handler=k=>{
  gate(k);const {s,cmd,assert,fail}=k;const r=target(k);
  const p=adminPerm(String(cmd.perm??''));assert(p,'Choose a permission from the access matrix.');
  const permId=p!.id;const before=r.template[permId];
  if(cmd.limit!==undefined){
    assert(p!.limit,'This permission has no configurable limit.');
    assert(Number.isSafeInteger(cmd.limit)&&cmd.limit>=0&&(p!.limit!.unit!=='percent'||cmd.limit<=100),p!.limit!.unit==='percent'?'Enter a percentage between 0 and 100.':'Enter a limit of ₹0 or more.');
    const why=rolSodBlock(s,r,permId,before??{effect:'limit',scope:'P'},cmd.limit);if(why)fail('FORBIDDEN',why);
    const fmt=(n:number|undefined)=>n===undefined?'not set':p!.limit!.unit==='percent'?n+'%':money(n);
    const old=r.limits[permId];assert(old!==cmd.limit,'No change: the role already has this limit.');
    r.limits[permId]=cmd.limit;bump(k,r,`limit · ${permId} ${p!.limit!.label}`,fmt(old),fmt(cmd.limit));
    return `${r.id} · ${permId} limit ${fmt(old)} → ${fmt(cmd.limit)}`;
  }
  const effect=cmd.effect as PolicyEffect|'none';
  assert(['allow','deny','limit','request','none'].includes(effect),'Choose Allow, Limit, Request, Deny or Not granted.');
  const scope=(cmd.scope||before?.scope||'P') as PolicyScope;
  assert(['P','S','A','O'].includes(scope),'Choose a scope: property, own shift, assigned or own.');
  if(effect==='limit')assert(p!.limit,'Only permissions with a limit can use Limit.');
  const next:PolicyCell|undefined=effect==='none'?undefined:{effect,scope,...(cmd.mode&&effect==='allow'?{mode:cmd.mode}:{})};
  const why=rolSodBlock(s,r,permId,next);if(why)fail('FORBIDDEN',why);
  assert(JSON.stringify(before)!==JSON.stringify(next),'No change: the role already has this value.');
  if(next)r.template[permId]=next;else delete r.template[permId];
  // DEC-217: approve / money cells in Accounting stay Owner + Accountant unless granted explicitly — the history carries the warning.
  const warn=/^PERM-ACC-00[34]$/.test(permId)&&next&&next.effect!=='deny'?' · warning: Accounting approve / money granted explicitly (DEC-217)':'';
  bump(k,r,`cell · ${permId} ${p!.action}${warn}`,adminDescribeCell(before),adminDescribeCell(next));
  return `${r.id} · ${permId} ${adminDescribeCell(before)} → ${adminDescribeCell(next)}${warn}`;
};
const ROL_RETIRE:Handler=k=>{
  gate(k);const {s,assert}=k;const r=target(k);
  const held=rolHolders(s,r.id);
  assert(!held.length,`${held.length} enabled user${held.length===1?'':'s'} still hold${held.length===1?'s':''} ${r.name}: move them to another role first.`,'INVALID_TRANSITION');
  r.retired=true;bump(k,r,'retired','active','retired');
  return `${r.id} · ${r.name} retired`;
};

// ── USER_CREATE / USER_UPDATE with a custom role ──
function assignable(k:Kit,id:string):CustomRole{
  const r=rolCustom(k.s,id);k.assert(r,'Choose a role.','NOT_FOUND');
  k.assert(!r!.retired,'This role is retired.','INVALID_TRANSITION');
  // DEC-218: the Tech admin may assign non-money roles only.
  k.assert(!r!.money||k.user.role==='Owner','Only the Owner assigns a role with money permissions (DEC-218).','FORBIDDEN');
  return r!;
}
const USER_CREATE:Handler=k=>{
  if(!k.cmd.customRole)return admin.USER_CREATE(k);
  const r=assignable(k,String(k.cmd.customRole));
  const n=k.s.users.length;
  const detail=admin.USER_CREATE({...k,cmd:{...k.cmd,role:r.base}});
  const u=k.s.users[n];if(u){u.customRole=r.id;u.name=u.name.split(' · ')[0]+' · '+r.name;}
  return `${detail} · role ${r.name} (${r.base} base)`;
};
const USER_UPDATE:Handler=k=>{
  const {s,uid,cmd,assert}=k;
  const t=s.users.find(x=>x.id===cmd.id);
  if(!t||(cmd.customRole===undefined&&!t.customRole))return admin.USER_UPDATE(k);
  const hasKey=Object.prototype.hasOwnProperty.call(cmd,'customRole');
  const nextId:string|undefined=hasKey?(cmd.customRole||undefined):cmd.role!==undefined?undefined:t.customRole;
  const changed=nextId!==t.customRole;
  let next:CustomRole|undefined;
  if(changed){
    k.req('admin');const d=authorize(s,uid,'PERM-ADM-001');if(d.decision!=='allow')k.fail('FORBIDDEN',d.reason);
    assert(t.id!==uid,'Use another owner account to change your own access.','FORBIDDEN');
    if(nextId)next=assignable(k,nextId);
  }else next=rolCustom(s,t.customRole);
  const role=(next?next.base:(cmd.role??t.role)) as Role;
  const sub:Record<string,unknown>={...cmd};delete sub.customRole;
  if(role!==t.role)sub.role=role;else delete sub.role;
  let detail='';
  const other=['name','role','enabled','expiresOn'].some(x=>sub[x]!==undefined);
  if(other){
    try{detail=admin.USER_UPDATE({...k,cmd:sub as Kit['cmd']});}
    catch(e){if(!(changed&&e instanceof Error&&e.message==='Nothing changed.'))throw e;}
  }else assert(changed,'Nothing changed.');
  const label=next?next.name:t.role;
  if(changed){
    const was=t.customRole?rolCustom(s,t.customRole)?.name??t.customRole:t.role;
    if(next)t.customRole=next.id;else delete t.customRole;
    t.assignedBy=uid;t.assignedOn=s.date;
    detail=(detail?detail+'; ':`${t.id} · `)+`role ${was} → ${label}`;
  }
  t.name=t.name.split(' · ')[0]+' · '+label;
  return detail;
};

export const handlers:Record<string,Handler>={ROL_CREATE,ROL_UPDATE,ROL_FEATURE_SET,ROL_GRANT_SET,ROL_RETIRE,USER_CREATE,USER_UPDATE};
