// ADM-2 · Custom roles as data (DEC-216…221; 02 Planning/agents/ADM_Briefs.md §3). Pure types + helpers, no React.
// A custom role is a copy of one built-in base role at creation (coarse grants, matrix cells, limits), then adjusted by the Owner on the
// role × sub-function grid (off / view / full per non-core feature) and the money flag. `User.role` stays the base, so every existing
// `u.role==='Manager'` check keeps working; `User.customRole` names the copy. Later changes to the base never flow down.
// Commands: services/mock/handlers/roles.ts (ROL_*). Wiring: can() / authorize() read rolEffectiveGrants / rolEffectiveTemplate,
// cfgPageOnFor + navFilterModules hide off pages, execute() refuses off / view features (rolCommandBlock).
import type {State,Role,User,Permission} from '../types';
import {roles as BUILT_IN} from '../types';
import {FEATURE_MODULES,featureById,featureAll,featureOfPage,featureOfCommand,featureClosure,featureExempt,type FeatureRef} from '../feature-catalogue';
import {adminPolicy,adminPerm,ADMIN_PERMS,ADMIN_FINANCIAL_MODULES,type PolicyCell,type PolicyChange} from './admin';

export type RoleAccess='off'|'view'|'full';
/** The frozen copy of the base taken at creation (what `full` and money=true restore; never re-read from the base). */
export type RoleBaseCopy={grants:Permission[];template:Record<string,PolicyCell>;limits:Record<string,number>};
export type CustomRole={id:string;           // 'C01'…'C99' (= the server role code)
  name:string;base:Role;                    // base ∈ roles minus 'Owner'
  money:boolean;                            // forced false + locked for Housekeeper / Supervisor / Maintenance / Restaurant
  features:Record<string,RoleAccess>;       // non-full entries only, full feature ids ('accounting.vouchers')
  grants:Permission[];                      // coarse module grants (copied from s.grants[base], then adjusted by the grid)
  template:Record<string,PolicyCell>;       // matrix cells (copied from policy.templates[base]); Advanced edits land here
  limits:Record<string,number>;
  retired?:boolean;createdBy:string;createdOn:string;version:number;
  /** Lane P addition: the base as copied at creation (restored by `full` / money on). Absent on rows read from the server. */
  baseCopy?:RoleBaseCopy};
export type ROLState={roles:CustomRole[];history:PolicyChange[]};

export const ROL_ACCESS:RoleAccess[]=['off','view','full'];
const RANK:Record<RoleAccess,number>={off:0,view:1,full:2};
const MONEY_LOCKED:Role[]=['Housekeeper','Supervisor','Maintenance','Restaurant'];
/** Own pay stays visible to every role (DEC-061). */
export const ROL_OWN_PAY='PERM-HR-004';
/** Separation of duty (DEC-219): a custom role may be narrower than its base on these, never wider. */
export const ROL_SOD_PERMS=['PERM-FOL-003','PERM-FOL-005','PERM-APR-001','PERM-CLS-004'];

export const rolState=(s:State):ROLState=>s.rolesX??{roles:[],history:[]};
export const rolList=(s:State)=>rolState(s).roles;
export const rolCustom=(s:State,id:string|undefined)=>id?rolState(s).roles.find(r=>r.id===id):undefined;
/** The custom role a user holds, if any. */
export function rolOfUser(s:State,uid:string):CustomRole|undefined{const u=s.users.find(x=>x.id===uid);return u?.customRole?rolCustom(s,u.customRole):undefined;}
/** Any role but Owner can be a base (DEC-216). */
export const rolCanBeBase=(role:string):role is Role=>role!=='Owner'&&(BUILT_IN.includes(role as Role)||role==='Restaurant');
export const rolMoneyLocked=(base:Role)=>MONEY_LOCKED.includes(base);
/** Sign-in kind follows the base (DEC-062): password for Accountant / Tech admin bases, PIN otherwise. */
export const rolCredentialKind=(base:Role):'PASSWORD'|'PIN'=>base==='Accountant'||base==='Tech admin'||base==='Owner'?'PASSWORD':'PIN';
/** Display name of a user's role: the custom role's name, else the built-in role. */
export function rolRoleName(s:State,u:User):string{return (u.customRole&&rolCustom(s,u.customRole)?.name)||u.role;}

export function rolEffectiveGrants(s:State,u:User):Permission[]{const c=u.customRole?rolCustom(s,u.customRole):undefined;return c?c.grants:(s.grants[u.role]||[]);}
export function rolEffectiveTemplate(s:State,u:User):Record<string,PolicyCell>{const c=u.customRole?rolCustom(s,u.customRole):undefined;return c?c.template:(adminPolicy(s).templates[u.role]??{});}
/** The custom role's own limit for a limit cell (Reception-style discount threshold when the copy has none). Undefined for built-ins. */
export function rolCustomLimit(s:State,c:CustomRole,perm:string):number|undefined{const v=c.limits[perm];if(v!==undefined)return v;return perm==='PERM-FOL-003'?s.settings.discountLimit:undefined;}

/** Every switchable (non-core) feature of a module is off for the role → the whole module (core pages too) is off for it. */
function moduleOffFor(c:CustomRole,r:FeatureRef):boolean{
  const sw=r.module.features.filter(f=>!f.core);
  return sw.length>0&&sw.every(f=>c.features[r.module.id+'.'+f.id]==='off');
}
/** A role's level for one feature (no requirement walk). */
function ownLevel(c:CustomRole,r:FeatureRef):RoleAccess{
  if(r.feature.core)return moduleOffFor(c,r)?'off':'full';
  return c.features[r.id]??'full';
}
/** Level of a feature for a custom role, capped by the features it requires (same closure as ADM-1). */
export function rolRoleFeatureLevel(c:CustomRole,featureId:string):RoleAccess{
  const r=featureById(featureId);if(!r)return 'full';
  let lvl=ownLevel(c,r);
  for(const q of r.feature.requires||[]){const l=rolRoleFeatureLevel(c,q);if(RANK[l]<RANK[lvl])lvl=l;}
  return lvl;
}
/** `full` for core features (unless the role has the whole module off), for unknown ids and for users without a custom role. */
export function rolFeatureLevel(s:State,uid:string,featureId:string):RoleAccess{const c=rolOfUser(s,uid);return c?rolRoleFeatureLevel(c,featureId):'full';}
/** Level of the feature that owns a nav page (`full` when no feature owns it). */
export function rolPageLevel(s:State,uid:string,page:string):RoleAccess{const f=featureOfPage(page);return f?rolFeatureLevel(s,uid,f.id):'full';}

/** Why execute() refuses a command for this actor's role (feature off or view only), or undefined. Core features never (unless the
 *  whole module is off for the role); the ADM-1 walk-in exemption applies to `off` too. */
export function rolCommandBlock(s:State,uid:string,cmd:{type:string;[k:string]:unknown}):string|undefined{
  const c=rolOfUser(s,uid);if(!c)return undefined;
  const f=featureOfCommand(cmd.type);if(!f)return undefined;
  const lvl=rolRoleFeatureLevel(c,f.id);
  if(lvl==='full')return undefined;
  if(lvl==='off'&&featureExempt(f.id,cmd,s.date))return undefined;
  return lvl==='view'?`${f.feature.label} (${f.module.label}) is view only for your role ${c.name}.`:`${f.feature.label} (${f.module.label}) is not part of your role ${c.name}.`;
}

/** Coarse permissions (the module's `coarse`) and the matrix permission ids (by `permPrefixes`) of a feature. */
export function rolFeaturePerms(featureId:string):{coarse:Permission[];ids:string[]}{
  const r=featureById(featureId);if(!r)return {coarse:[],ids:[]};
  const pre=r.feature.permPrefixes;
  return {coarse:[...r.module.coarse],ids:ADMIN_PERMS.filter(p=>pre.some(x=>p.id.startsWith(x))).map(p=>p.id)};
}
/** Money-flagged matrix permission (own pay excepted). */
export const rolIsMoneyPerm=(permId:string)=>permId!==ROL_OWN_PAY&&!!adminPerm(permId)?.financial;

/** Effective access preview of a custom role: pages visible (by level), every non-core feature with its level, money. */
export function rolPreview(s:State,roleId:string){
  const c=rolCustom(s,roleId);if(!c)return undefined;
  const features=featureAll().map(r=>({id:r.id,label:r.feature.label,module:r.module.label,core:!!r.feature.core,level:rolRoleFeatureLevel(c,r.id)}));
  const pages=featureAll().filter(r=>rolRoleFeatureLevel(c,r.id)!=='off').flatMap(r=>r.feature.pages);
  const hiddenPages=featureAll().filter(r=>rolRoleFeatureLevel(c,r.id)==='off').flatMap(r=>r.feature.pages);
  return {role:c,base:c.base,money:c.money,moneyLocked:rolMoneyLocked(c.base),grants:[...c.grants],pages,hiddenPages,features,
    users:s.users.filter(u=>u.customRole===c.id).map(u=>u.id)};
}

const cellRank=(c:PolicyCell|undefined)=>!c||c.effect==='deny'?0:c.effect==='request'?1:c.effect==='limit'?2:3;
/** Why a matrix cell may not be set on a custom role (money invariant, Tech admin separation, admin delegation, DEC-219 SoD), or
 *  undefined. `limit` is the new limit when the cell is a limit. Compared with the role's frozen base copy. */
export function rolSodBlock(s:State,role:CustomRole,permId:string,cell:PolicyCell|undefined,limit?:number):string|undefined{
  const p=adminPerm(permId);
  const grants=!!cell&&cell.effect!=='deny';
  if(!grants&&limit===undefined)return undefined;
  if(grants&&rolIsMoneyPerm(permId)&&!role.money)
    return rolMoneyLocked(role.base)?`A role based on ${role.base} never holds money permissions (DEC-061).`:`${role.name} has money switched off: switch it on first.`;
  if(grants&&role.base==='Tech admin'&&p?.financial)return 'A role based on Tech admin cannot hold financial actions (technical and financial power are kept separate).';
  if(grants&&['PERM-ADM-001','PERM-ADM-002'].includes(permId))return 'Administrator delegation is outside this prototype.';
  if(ROL_SOD_PERMS.includes(permId)){
    const base=(role.baseCopy?.template??adminPolicy(s).templates[role.base]??{})[permId];
    if(cellRank(cell)>cellRank(base))return `Separation of duty (DEC-219): ${p?.action??permId} may be narrower than on ${role.base}, never wider.`;
    if(limit!==undefined){const bl=role.baseCopy?.limits[permId]??adminPolicy(s).limits[role.base]?.[permId]??(permId==='PERM-FOL-003'?s.settings.discountLimit:undefined);
      if(bl===undefined||limit>bl)return `Separation of duty (DEC-219): the ${p?.action??permId} limit may only go down from the ${role.base} limit.`;}
  }
  return undefined;
}

// ───────────── Grid application (used by handlers/roles.ts on its private copy) ─────────────
/** Prefixes of `featureId` not shared with another feature that stays above `lvl` for the role. */
function ownPrefixes(c:CustomRole,featureId:string,lvl:RoleAccess):string[]{
  const r=featureById(featureId)!;
  const others=featureAll().filter(x=>x.id!==featureId&&RANK[rolRoleFeatureLevel(c,x.id)]>RANK[lvl]);
  return r.feature.permPrefixes.filter(p=>!others.some(o=>o.feature.permPrefixes.includes(p)));
}
const suffix=(id:string)=>id.slice(id.lastIndexOf('-')+1);
/** Sets one feature's level (plus its dependants down to the same level for off / view) and adjusts cells / coarse grants (§1). */
export function rolApplyFeature(s:State,c:CustomRole,featureId:string,access:RoleAccess):string[]{
  const changed:string[]=[];
  const ids=access==='full'?[featureId]:featureClosure(featureId);
  for(const id of ids){const r=featureById(id)!;if(r.feature.core)continue;
    const cur=c.features[id]??'full';const next=id===featureId?access:(RANK[cur]>RANK[access]?access:cur);
    if(next===cur)continue;
    if(next==='full')delete c.features[id];else c.features[id]=next;changed.push(`${id} ${cur} → ${next}`);}
  const base=c.baseCopy??{grants:s.grants[c.base]??[],template:adminPolicy(s).templates[c.base]??{},limits:adminPolicy(s).limits[c.base]??{}};
  for(const id of ids){const r=featureById(id);if(!r||r.feature.core)continue;const lvl=rolRoleFeatureLevel(c,id);
    const pre=ownPrefixes(c,id,lvl);const mine=(k:string)=>pre.some(p=>k.startsWith(p));
    if(lvl==='off'){for(const k of Object.keys(c.template))if(mine(k)&&c.template[k].effect!=='deny')delete c.template[k];}
    else if(lvl==='view'){
      for(const k of Object.keys(c.template))if(mine(k)&&suffix(k)!=='001'&&c.template[k].effect!=='deny')delete c.template[k];
      for(const p of rolFeaturePerms(id).ids)if(mine(p)&&suffix(p)==='001'&&!c.template[p]&&!(rolIsMoneyPerm(p)&&!c.money))c.template[p]={effect:'allow',scope:'P',mode:'view'};
    }else{
      // full: the base's own cells come back; a feature the base lacks gets its view (-001) and act (-002) ids, never approve / money.
      for(const p of rolFeaturePerms(id).ids){if(c.template[p]?.effect==='deny')continue;if(rolIsMoneyPerm(p)&&!c.money)continue;
        if(base.template[p]){c.template[p]=JSON.parse(JSON.stringify(base.template[p]));if(base.limits[p]!==undefined&&c.limits[p]===undefined)c.limits[p]=base.limits[p];}
        else if(['001','002'].includes(suffix(p))&&!c.template[p]&&!(c.base==='Tech admin'&&adminPerm(p)?.financial))c.template[p]={effect:'allow',scope:'P'};}
      for(const g of r.module.coarse){if(g==='admin'||c.grants.includes(g))continue;
        if(!c.money&&ADMIN_FINANCIAL_MODULES.includes(g))continue;if(c.base==='Tech admin'&&ADMIN_FINANCIAL_MODULES.includes(g))continue;c.grants.push(g);}
    }
  }
  return changed;
}
/** Money off: drops money cells (own pay kept) and the financial coarse modules; money on: restores the base copy's money cells. */
export function rolApplyMoney(s:State,c:CustomRole,money:boolean){
  c.money=money;
  const base=c.baseCopy??{grants:s.grants[c.base]??[],template:adminPolicy(s).templates[c.base]??{},limits:adminPolicy(s).limits[c.base]??{}};
  if(!money){
    for(const k of Object.keys(c.template))if(rolIsMoneyPerm(k)&&c.template[k].effect!=='deny')delete c.template[k];
    c.grants=c.grants.filter(g=>!ADMIN_FINANCIAL_MODULES.includes(g));
    return;
  }
  for(const [k,cell] of Object.entries(base.template)){if(!rolIsMoneyPerm(k)||c.template[k])continue;
    const owners=featureAll().filter(f=>f.feature.permPrefixes.some(p=>k.startsWith(p)));
    if(owners.length&&owners.every(f=>rolRoleFeatureLevel(c,f.id)==='off'))continue;
    c.template[k]=JSON.parse(JSON.stringify(cell));if(base.limits[k]!==undefined)c.limits[k]=base.limits[k];}
  for(const g of base.grants)if(ADMIN_FINANCIAL_MODULES.includes(g)&&!c.grants.includes(g))c.grants.push(g);
}
/** Next free custom role id C01…C99 (retired ids are never reused). */
export function rolNextId(s:State):string|undefined{
  const used=new Set(rolList(s).map(r=>r.id));
  for(let i=1;i<=99;i++){const id='C'+String(i).padStart(2,'0');if(!used.has(id))return id;}
  return undefined;
}
/** A new custom role: a copy of the base (coarse grants, matrix cells, limits), money applied. Pure (does not add it to the state). */
export function rolNewRole(s:State,o:{id:string;name:string;base:Role;money?:boolean;by:string}):CustomRole{
  const pol=adminPolicy(s);const cp=<T>(x:T):T=>JSON.parse(JSON.stringify(x??{}));
  const baseCopy:RoleBaseCopy={grants:[...(s.grants[o.base]??[])],template:cp(pol.templates[o.base]??{}),limits:cp(pol.limits[o.base]??{})};
  const c:CustomRole={id:o.id,name:o.name,base:o.base,money:true,features:{},grants:[...baseCopy.grants],template:cp(baseCopy.template),limits:cp(baseCopy.limits),
    createdBy:o.by,createdOn:s.date,version:1,baseCopy};
  const money=rolMoneyLocked(o.base)?false:o.money!==false;
  if(!money)rolApplyMoney(s,c,false);
  return c;
}
/** Users (enabled) holding a custom role. */
export const rolHolders=(s:State,id:string)=>s.users.filter(u=>u.customRole===id&&u.enabled);
/** Non-core features grouped by module, for the role grid. */
export const rolGridModules=()=>FEATURE_MODULES.map(m=>({module:m,features:m.features.filter(f=>!f.core).map(f=>m.id+'.'+f.id)})).filter(x=>x.features.length);
