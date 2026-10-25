// W05-A · Master configuration hub — one settings registry for every module (DEC-057, DEC-058, APR-006).
// Design: 02 Planning/HCC_Master_Configuration_Design_v1.md (§2 rules from the ETP UI audit and SCC C1 settings redesign,
// §4 behaviour of every setting). Pure domain: registry, permissions, dated values, modules on/off, export/import plan.
// Commands live in services/mock/handlers/admin.ts (SETTING_SET extended, CFG_DECIDE, CFG_WITHDRAW, CFG_REVERT, CFG_IMPORT).
//
// ── Provider API (same idea as registerRoomHold) ────────────────────────────────────────────────────────────────
// Any module contributes settings to one of the 12 fixed groups with ONE call:
//     registerSettingsGroup({id:'hr-statutory', group:'people', defs:hrSettingDefs});
// `defs` is an array or a function (s:State)=>defs (for state-dependent lists such as menu items). Each def needs only
// {key,label}; everything else has a safe default (see CfgProviderDef). Storage:
//   · no read/command  → the hub stores the value itself (s.configX.values[key]); read it with cfgValueOn(s,key,date).
//                        Money/tax/statutory values should set dated:true (effective-from date; posted records never move).
//   · read + command   → thin wrapper: the hub reads the module's own state and saves by running the module's own command
//                        (its validation applies; the hub's DEC-058 rule replaces the module's permission check).
// A later provider with an existing key replaces that key; unregisterSettingsGroup(id) removes a provider.
//
// ── Wired providers (merge of integration/w05) ───────────────────────────────────────────────────────────────────
//     registerSettingsGroup({id:'hr-statutory',group:'people',defs:hrStatutoryDefs});      // W05-B hrSettingDefs() → per-field wrappers
//     registerSettingsGroup({id:'compliance',group:'compliance',defs:cmpDefs});            // W05-C cmpSettingDefs(s) → wrappers
// See the adapters hrStatutoryDefs / cmpDefs at the bottom: each composite W05-B rate (e.g. PF = employee %, employer %, wage cap)
// becomes one editable hub setting per field, saved through HR_STAT_RATES_SET (dated from the 1st of a month by the payroll module).
import type {State,Command,Role} from '../types';
import {SCHEMA} from '../types';
import {money,addDays} from '../money';
import {can} from '../selectors';
import {ADMIN_SETTINGS,adminSettingValue,adminUserActive,authorize,type AdminSettingDef} from './admin';
import {yldSlice,yldPct,YLD_ROUND_OPTIONS} from './yield';
import {aiDirtyHours} from './assistant';
import {gsvSettings,GSV_REQUEST_TYPES} from './services';
import {posSlice} from './pos';
import {hrRatesFor,hrRatesDefaultFrom,hrMonthLabel,hrSettingDefs,hrSettingValue,hrLeavePolicy,type HRRates,type HRSettingDef} from './staff';
import {cmpSettingDefs,type CMPSettingDef} from './compliance';
import {GST_SEED,gstSeedOn,gstSpecifiedPremises,gstPct,GST_ILLUSTRATIVE,type GstKey} from '../gst';

// ───────────────────────────── Groups (fixed names — a test pins them) ─────────────────────────────
export type CfgGroupId='property'|'rooms'|'frontdesk'|'money'|'pos'|'hkm'|'people'|'compliance'|'modules'|'users'|'messages'|'data';
export const CFG_SECTIONS=['Business setup','Operations and people','Software and security'] as const;
export type CfgSection=typeof CFG_SECTIONS[number];
export type CfgGroup={id:CfgGroupId;label:string;section:CfgSection;money:boolean;blurb:string;link?:{page:string;label:string}};
export const CFG_GROUPS:CfgGroup[]=[
  {id:'property',label:'Property and branding',section:'Business setup',money:false,blurb:'Name, address, GSTIN, invoice header and footer, print format, language'},
  {id:'rooms',label:'Rooms and rate plans',section:'Business setup',money:true,blurb:'Room types, dated tariffs, rate guardrails and restrictions',link:{page:'yield',label:'Open Tariff & yield'}},
  {id:'frontdesk',label:'Front-desk policies',section:'Business setup',money:false,blurb:'Check-in and checkout times, hold expiry, deposit, ID rules, no-show, guest-service SLAs'},
  {id:'money',label:'Money and tax',section:'Business setup',money:true,blurb:'Dated GST master (rooms, restaurant, services), discount and approval limits, payment methods, document numbering, till model'},
  {id:'pos',label:'Restaurant and POS',section:'Business setup',money:true,blurb:'Outlets, menu availability and KOT',link:{page:'pos',label:'Open Restaurant & POS'}},
  {id:'hkm',label:'Housekeeping and maintenance',section:'Operations and people',money:false,blurb:'Inspection, dirty-room alert, attendant load, preventive maintenance'},
  {id:'people',label:'People, payroll and leave',section:'Operations and people',money:true,blurb:'Departments, dated PF / ESIC / PT rates, leave policy, weekly offs',link:{page:'staff',label:'Open Staff & payroll'}},
  {id:'compliance',label:'Compliance obligations',section:'Operations and people',money:true,blurb:'GST, TDS, PF, ESIC and PT list, due rules, CA contact'},
  {id:'modules',label:'Modules on/off',section:'Software and security',money:false,blurb:'Switch optional modules off; their data is kept'},
  {id:'users',label:'Users, roles and PINs',section:'Software and security',money:false,blurb:'Users and roles live in Access control; PIN lock options',link:{page:'access',label:'Open Access control'}},
  {id:'messages',label:'Messages and documents',section:'Software and security',money:false,blurb:'Sender name, consent, gateway key, document retention',link:{page:'messages',label:'Open Messages'}},
  {id:'data',label:'Data and system',section:'Software and security',money:false,blurb:'Backup schedule, restore drill, storage, diagnostics, about'},
];
export const cfgGroup=(id:string)=>CFG_GROUPS.find(g=>g.id===id);

// ───────────────────────────── Modules on/off (DEC-057) ─────────────────────────────
export type CfgModuleId='pos'|'groups'|'ota'|'website'|'messaging'|'documents'|'assistant'|'inventory'|'maintenance'|'payroll';
export type CfgModule={id:CfgModuleId;label:string;pages:string[];prefixes:string[];except?:string[];note:string};
/** Switchable modules: their nav pages and command prefixes. Everything else (front desk, folios, cashier, closing, access, configuration) is core. */
export const CFG_MODULES:CfgModule[]=[
  {id:'pos',label:'Restaurant & POS',pages:['pos'],prefixes:['POS_'],note:'Restaurant and room-service checks, KOT, menu'},
  {id:'groups',label:'Groups & companies',pages:['groups'],prefixes:['GRP_'],note:'Group blocks, companies and credit limits'},
  {id:'ota',label:'OTA channels',pages:['channels'],prefixes:['CHN_'],except:['CHN_WEB_'],note:'Simulated OTA inbound bookings and rate push'},
  {id:'website',label:'Direct booking site',pages:['booking-site'],prefixes:['CHN_WEB_'],note:'Simulated booking-engine requests'},
  {id:'messaging',label:'Guest messaging',pages:['messages'],prefixes:['MSG_'],except:['MSG_DOC_'],note:'Simulated WhatsApp / SMS / e-mail and templates'},
  {id:'documents',label:'Document capture',pages:['documents'],prefixes:['MSG_DOC_'],note:'Scan and file IDs and bills (simulated OCR)'},
  {id:'assistant',label:'Management assistant',pages:['assistant'],prefixes:['AI_'],note:'Questions about hotel data, anomaly alerts'},
  {id:'inventory',label:'Linen & stock',pages:['inventory'],prefixes:['INV_'],note:'Linen, amenities, laundry and counts'},
  {id:'maintenance',label:'Maintenance',pages:['maintenance'],prefixes:['MNT_'],note:'Work orders, assets, AMC and preventive plans'},
  {id:'payroll',label:'Payroll',pages:[],prefixes:['HR_PAYROLL_','HR_RATES_','HR_ADVANCE_','HR_PAYSLIP_','HR_FNF_','HR_STAT_'],note:'Pay runs, advances, payslips (attendance and leave stay on)'},
];
export const CFG_CORE_MODULES=['Front desk','Guests & folios','Cashier','Day closing','Access control','Master configuration'];
export const cfgModulesOff=(s:State):string[]=>s.configX?.modulesOff??[];
export const cfgModuleOn=(s:State,id:string)=>!cfgModulesOff(s).includes(id);
export function cfgModuleOfCommand(type:string):CfgModule|undefined{
  return CFG_MODULES.find(m=>m.prefixes.some(p=>type.startsWith(p))&&!(m.except||[]).some(p=>type.startsWith(p)));
}
export const cfgModuleOfPage=(page:string)=>CFG_MODULES.find(m=>m.pages.includes(page));
/** False when the page belongs to a module that is switched off (navigation, tiles, search and phone tabs hide it). */
export const cfgPageOn=(s:State,page:string)=>{const m=cfgModuleOfPage(page);return !m||cfgModuleOn(s,m.id);};
export const cfgModuleOffMessage=(m:CfgModule)=>`${m.label} is switched off in Master configuration › Modules on/off. Its data is kept; the Owner or Tech admin can switch it back on.`;

// ───────────────────────────── Setting definitions ─────────────────────────────
export type CfgType='text'|'time'|'money'|'boolean'|'integer'|'percent'|'choice'|'fixed';
/** immediate = now · date = from a business date you choose (dated value; today = now) · nextDay = from the next business day. */
export type CfgEffective='immediate'|'date'|'nextDay';
/** Who must approve a change made by a non-Owner (Tech admin). The Owner's own change never needs approval. */
export type CfgApprover='none'|'Manager'|'Owner';
export type CfgStore=
  |{kind:'settings'}
  |{kind:'config'}
  |{kind:'module';read:(s:State)=>unknown;command:(s:State,v:unknown)=>Command}
  |{kind:'switch';module:CfgModuleId}
  |{kind:'fixed';read:(s:State)=>string};
/** One registry entry. Extends the BL-027 AdminSettingDef (key, category, label, unit, editable, approval, effective, help). */
export type CfgSettingDef=Omit<AdminSettingDef,'type'>&{
  type:CfgType;group:CfgGroupId;source:string;
  effectiveMode:CfgEffective;approver:CfgApprover;
  /** Money/tax/payroll/compliance/approval-limit setting: Owner-only (DEC-058). */
  money:boolean;
  /** all = everyone who opens the hub sees the value; owner = value masked for other roles. */
  visibility:'all'|'owner';
  /** Never shown in full, never exported. */
  secret:boolean;
  /** Value has effective-from dates (history keeps each date). */
  dated:boolean;
  /** "What it affects" — shown before saving. */
  affects:string;
  store:CfgStore;
  default?:unknown;min?:number;max?:number;maxLength?:number;allowEmpty?:boolean;
  options?:readonly(string|number)[];pattern?:RegExp;patternMsg?:string;suffix?:string;
  /** Statutory/tax values: "Illustrative — CA to confirm for a hotel". */
  illustrative?:boolean;
  validate?:(v:unknown,s:State)=>string|undefined;
  show?:(v:unknown)=>string;
  /** Extra impact lines for the preview (state-dependent). */
  impact?:(s:State,v:unknown)=>string[];
  /** Special editor panel shown in the group instead of the generic Change… button (tariff / tax schedules). */
  panel?:'tariffs'|'tax';
};
/** Loose shape accepted from other modules (W05-B hrSettingDefs, W05-C cmpSettingDefs). Only key and label are required. */
export type CfgProviderDef={key:string;label:string;type?:CfgType|AdminSettingDef['type'];unit?:string;help?:string;default?:unknown;
  min?:number;max?:number;maxLength?:number;allowEmpty?:boolean;options?:readonly(string|number)[];pattern?:RegExp;patternMsg?:string;suffix?:string;
  effective?:CfgEffective|string;effectiveMode?:CfgEffective;dated?:boolean;approver?:CfgApprover;money?:boolean;secret?:boolean;visibility?:'all'|'owner';
  affects?:string;illustrative?:boolean;editable?:boolean;fixedReason?:string;
  /** Effective rule shown instead of the generic mode text (e.g. a module command that dates its own versions — W05P-2). */ effectiveText?:string;
  read?:(s:State)=>unknown;command?:(s:State,v:unknown)=>Command;validate?:(v:unknown,s:State)=>string|undefined;show?:(v:unknown)=>string;
  impact?:(s:State,v:unknown)=>string[];panel?:'tariffs'|'tax';store?:CfgStore};
export type CfgProvider={id:string;group:CfgGroupId;defs:readonly CfgProviderDef[]|((s:State)=>readonly CfgProviderDef[]);summary?:(s:State)=>string};

const EFFECTIVE_TEXT:Record<CfgEffective,string>={immediate:'Immediately',date:'From a business date you choose (dated value)',nextDay:'From the next business day'};
function normalize(p:CfgProvider,d:CfgProviderDef):CfgSettingDef{
  const g=cfgGroup(p.group)!;
  const store:CfgStore=d.store??(d.read&&d.command?{kind:'module',read:d.read,command:d.command}:d.read?{kind:'fixed',read:(s:State)=>String(d.read!(s))}:{kind:'config'});
  const fixed=store.kind==='fixed'||d.type==='fixed'||d.editable===false;
  const modes:CfgEffective[]=['immediate','date','nextDay'];
  let mode:CfgEffective=d.effectiveMode??(modes.includes(d.effective as CfgEffective)?d.effective as CfgEffective:d.dated?'date':'immediate');
  if(store.kind==='module')mode='immediate'; // a module command applies now; dated values belong in the hub store
  const money=d.money??g.money,approver=d.approver??'none';
  const type:CfgType=fixed?'fixed':(d.type&&d.type!=='fixed'?d.type:'text') as CfgType;
  return {key:d.key,label:d.label,category:g.label,group:p.group,source:p.id,type,unit:d.unit??'',help:d.help??'',editable:!fixed,
    approval:fixed?(d.fixedReason??'Not configurable here'):(money?'Owner only (DEC-058)':'Owner or Tech admin (DEC-058)')+(approver==='none'?'':` · Tech admin changes need ${approver==='Owner'?'Owner':'Manager or Owner'} approval`),
    effective:fixed?'—':d.effectiveText??EFFECTIVE_TEXT[mode],effectiveMode:mode,approver,money,visibility:d.secret?'owner':(d.visibility??'all'),secret:!!d.secret,dated:d.dated??mode==='date',
    affects:d.affects??'Shown on the related screens; existing records are not changed.',store:fixed&&store.kind!=='fixed'?{kind:'fixed',read:d.read?(s:State)=>String(d.read!(s)):()=>'—'}:store,
    default:d.default,min:d.min,max:d.max,maxLength:d.maxLength,allowEmpty:d.allowEmpty,options:d.options,pattern:d.pattern,patternMsg:d.patternMsg,suffix:d.suffix,
    illustrative:d.illustrative,validate:d.validate,show:d.show,impact:d.impact,panel:d.panel};
}

const providers:CfgProvider[]=[];
let builtinsDone=false;
/** Register (or replace, by id) a provider of settings for one group. */
export function registerSettingsGroup(p:CfgProvider){
  if(!cfgGroup(p.group))throw new Error(`Unknown configuration group ${p.group}`);
  const i=providers.findIndex(x=>x.id===p.id);if(i>=0)providers[i]=p;else providers.push(p);
}
export function unregisterSettingsGroup(id:string){const i=providers.findIndex(x=>x.id===id);if(i>=0)providers.splice(i,1);}
export const cfgProviders=()=>{builtins();return [...providers];};
/** Every setting in the registry for this state, in group order (later providers replace an earlier key). */
export function cfgDefs(s:State):CfgSettingDef[]{
  builtins();
  const byKey=new Map<string,CfgSettingDef>();
  for(const p of providers){const list=typeof p.defs==='function'?p.defs(s):p.defs;for(const d of list){byKey.delete(d.key);byKey.set(d.key,normalize(p,d));}}
  const order=CFG_GROUPS.map(g=>g.id);
  return [...byKey.values()].sort((a,b)=>order.indexOf(a.group)-order.indexOf(b.group));
}
export const cfgDef=(s:State,key:string)=>cfgDefs(s).find(d=>d.key===key);
export const cfgGroupDefs=(s:State,g:string)=>cfgDefs(s).filter(d=>d.group===g);

// ───────────────────────────── Values ─────────────────────────────
export type CFGScheduled={id:string;key:string;value:unknown;effectiveFrom:string;by:string;approvedBy?:string;at:string};
export type CFGChange={id:string;key:string;before:unknown;after:unknown;effectiveFrom:string;at:string;by:string;approvedBy?:string;
  status:'applied'|'scheduled'|'withdrawn';revertOf?:string};
/** State slice s.configX. Absent = defaults (old saves). */
export type CFGState={modulesOff?:string[];values?:Record<string,unknown>;scheduled?:CFGScheduled[];changes?:CFGChange[]};
export const cfgEnsure=(s:State):Required<CFGState>=>{const x=s.configX??(s.configX={});x.modulesOff??=[];x.values??={};x.scheduled??=[];x.changes??=[];return x as Required<CFGState>;};

/** The live value today (before any future-dated change). */
export function cfgRaw(s:State,def:CfgSettingDef):unknown{
  const st=def.store;
  switch(st.kind){
    case 'settings':return (s.settings as unknown as Record<string,unknown>)[def.key]??def.default;
    case 'config':return s.configX?.values?.[def.key]??def.default;
    case 'module':return st.read(s);
    case 'switch':return cfgModuleOn(s,st.module);
    case 'fixed':return st.read(s);
  }
}
/** Future-dated changes waiting for their business date, oldest first. */
export const cfgScheduled=(s:State,key?:string)=>(s.configX?.scheduled??[]).filter(x=>!key||x.key===key).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:a.effectiveFrom>b.effectiveFrom?1:0);
/** Value in force on a business date: the latest scheduled change with effectiveFrom ≤ date, else the live value. */
export function cfgValueOn(s:State,key:string,date=s.date):unknown{
  const def=cfgDef(s,key);if(!def)return undefined;
  const due=cfgScheduled(s,key).filter(x=>x.effectiveFrom<=date);
  return due.length?due[due.length-1].value:cfgRaw(s,def);
}
export function cfgShow(def:CfgSettingDef,v:unknown):string{
  if(def.secret)return v?'Set (hidden)':'Not set';
  if(def.show)return def.show(v);
  if(v===undefined||v===null||v==='')return '—';
  switch(def.type){
    case 'money':return money(Number(v));
    case 'boolean':return v?'On':'Off';
    case 'percent':return (Number(v)/100).toLocaleString('en-IN',{maximumFractionDigits:2})+'%';
    case 'integer':return String(v)+(def.suffix?' '+def.suffix:'');
    default:return String(v);
  }
}
/** Display value for a user: owner-only values are masked for other roles. */
export function cfgDisplay(s:State,uid:string,def:CfgSettingDef,v=cfgValueOn(s,def.key)):string{
  if(def.visibility==='owner'&&s.users.find(u=>u.id===uid)?.role!=='Owner')return def.secret?(v?'Set (hidden)':'Not set'):'Hidden (Owner only)';
  return cfgShow(def,v);
}
const TIME=/^([01]\d|2[0-3]):[0-5]\d$/;
/** Plain validation message for one value (undefined = valid). */
export function cfgValidate(s:State,def:CfgSettingDef,v:unknown):string|undefined{
  if(!def.editable)return 'This setting is not configurable.';
  const range=(lo:number,hi:number)=>`between ${lo} and ${hi}`;
  switch(def.type){
    case 'text':{
      if(typeof v!=='string')return 'Enter text.';
      const t=v.trim(),max=def.maxLength??60;
      if(!t&&!def.allowEmpty)return `${def.label} is required.`;
      if(t.length>max)return `Use at most ${max} characters.`;
      if(t&&def.pattern&&!def.pattern.test(t))return def.patternMsg??'Check the format.';
      break;}
    case 'time':if(typeof v!=='string'||!TIME.test(v))return 'Enter a time as HH:mm.';break;
    case 'boolean':if(typeof v!=='boolean')return 'Choose on or off.';break;
    case 'choice':if(!(def.options||[]).includes(v as string|number))return 'Choose one of the listed options.';break;
    case 'money':{const lo=def.min??0,hi=def.max??10000000;if(!Number.isSafeInteger(v)||(v as number)<lo||(v as number)>hi)return `Enter an amount from ${money(lo)} to ${money(hi)}.`;break;}
    case 'integer':case 'percent':{const lo=def.min??0,hi=def.max??100000;
      if(!Number.isInteger(v)||(v as number)<lo||(v as number)>hi)return def.type==='percent'?`Use a percentage ${range(lo/100,hi/100)}%.`:`Use a whole number ${range(lo,hi)}${def.suffix?' '+def.suffix:''}.`;break;}
    default:return 'This setting is not configurable.';
  }
  return def.validate?.(v,s);
}
/** Normalised value to store (text is trimmed). */
export const cfgClean=(def:CfgSettingDef,v:unknown)=>def.type==='text'&&typeof v==='string'?v.trim():v;
const same=(a:unknown,b:unknown)=>JSON.stringify(a)===JSON.stringify(b);
export const cfgSame=same;

/** Effective-from date for a change made today (validates a requested date). Returns a message on error. */
export function cfgEffectiveFrom(s:State,def:CfgSettingDef,requested?:string):{date:string}|{error:string}{
  if(def.effectiveMode==='immediate'){if(requested&&requested!==s.date)return {error:`${def.label} takes effect immediately; it cannot be dated.`};return {date:s.date};}
  if(def.effectiveMode==='nextDay'){const nd=addDays(s.date,1);if(requested&&requested!==nd)return {error:`${def.label} takes effect from the next business day (${nd}).`};return {date:nd};}
  const d=requested||s.date;
  if(!/^\d{4}-\d{2}-\d{2}$/.test(d))return {error:'Choose an effective date.'};
  if(d<s.date)return {error:'Effective date cannot be before the current business date.'};
  if(d>addDays(s.date,366))return {error:'Effective date can be at most one year ahead.'};
  return {date:d};
}

/** Writes a value straight into its store (settings / hub store / module switch). Module wrappers go through their command (handler). */
export function cfgWriteDirect(s:State,def:CfgSettingDef,v:unknown){
  const st=def.store;
  if(st.kind==='settings')(s.settings as unknown as Record<string,unknown>)[def.key]=v;
  else if(st.kind==='config')cfgEnsure(s).values[def.key]=v;
  else if(st.kind==='switch'){const x=cfgEnsure(s);x.modulesOff=v?x.modulesOff.filter(m=>m!==st.module):[...new Set([...x.modulesOff,st.module])];}
  else throw new Error('cfgWriteDirect: '+def.key+' is not directly writable');
}
/** Applies scheduled changes whose date has come (called before every command; NEXT_DAY uses the new date). */
export function cfgApplyDue(s:State,date:string){
  const list=s.configX?.scheduled;if(!list?.length)return;
  const due=list.filter(x=>x.effectiveFrom<=date).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1);
  if(!due.length)return;
  const x=cfgEnsure(s);
  for(const e of due){const def=cfgDef(s,e.key);if(def&&def.store.kind!=='module'&&def.store.kind!=='fixed')cfgWriteDirect(s,def,e.value);
    const c=x.changes.find(c=>c.id===e.id);if(c)c.status='applied';}
  x.scheduled=list.filter(e=>!due.includes(e));
}
/**
 * The ONE guarded check in execute() (services/mock/commands.ts): applies due dated values, then refuses commands of a
 * switched-off module (DEC-057). Returns the refusal text, or undefined to continue.
 */
export function cfgBeforeCommand(s:State,cmd:Command):string|undefined{
  cfgApplyDue(s,cmd.type==='NEXT_DAY'?addDays(s.date,1):s.date);
  const m=cfgModuleOfCommand(cmd.type);
  return m&&!cfgModuleOn(s,m.id)?cfgModuleOffMessage(m):undefined;
}

// ───────────────────────────── Permissions (DEC-058) ─────────────────────────────
export type CfgDecision={mode:'allow'|'request'|'deny';reason:string;approver?:'Manager'|'Owner'};
export const CFG_OWNER_ONLY='Owner changes this — money, tax, payroll, compliance and approval limits are Owner-only (DEC-058).';
/** Who may change one setting: Owner all groups; Tech admin non-money settings only (approval when the setting names an approver); others read-only. */
export function cfgCanChange(s:State,uid:string,def:CfgSettingDef):CfgDecision{
  const u=s.users.find(x=>x.id===uid);
  if(!u||!adminUserActive(s,u))return {mode:'deny',reason:'Your account is not active.'};
  if(!def.editable)return {mode:'deny',reason:def.approval};
  if(!can(s,uid,'admin'))return {mode:'deny',reason:'Owner changes this. You can review it here.'};
  if(u.role==='Owner'){const d=authorize(s,uid,'PERM-ADM-003',{act:true});return d.decision==='deny'?{mode:'deny',reason:d.reason}:{mode:'allow',reason:'Owner changes all groups (DEC-058).'};}
  if(u.role==='Tech admin'){
    if(def.money)return {mode:'deny',reason:CFG_OWNER_ONLY};
    if(def.approver!=='none')return {mode:'request',reason:`Your change goes to the ${def.approver==='Owner'?'Owner':'Manager or Owner'} for approval.`,approver:def.approver};
    return {mode:'allow',reason:'Tech admin changes non-money settings (DEC-058).'};
  }
  return {mode:'deny',reason:'Owner changes this. You can review it here.'};
}
/** Group-level access: read-only when the user can change none of its settings; the reason is shown (never hidden or redirected). */
export function cfgGroupAccess(s:State,uid:string,g:CfgGroupId):{readOnly:boolean;reason?:string}{
  const defs=cfgGroupDefs(s,g).filter(d=>d.editable);
  const u=s.users.find(x=>x.id===uid);
  const groupMoney=cfgGroup(g)!.money;
  if(groupMoney&&u?.role!=='Owner'&&can(s,uid,'admin'))return {readOnly:true,reason:CFG_OWNER_ONLY};
  if(!defs.length)return {readOnly:true,reason:g==='users'?'Users and roles are changed in Access control.':'Nothing in this group is configurable here.'};
  const ds=defs.map(d=>cfgCanChange(s,uid,d));
  if(ds.every(d=>d.mode==='deny'))return {readOnly:true,reason:ds[0].reason};
  return {readOnly:false};
}
/** May this user decide a configuration request (approval)? undefined = yes. Never your own request. */
export function cfgDecideBlock(s:State,uid:string,approvalId:string):string|undefined{
  const a=s.approvals.find(x=>x.id===approvalId&&x.kind==='config');
  if(!a)return 'Request not found.';
  const u=s.users.find(x=>x.id===uid);
  if(!u||!adminUserActive(s,u))return 'Your account is not active.';
  if(a.actor===uid)return 'You raised this request — another authorised user must decide it.';
  const ap=String(a.meta?.approver||'Owner');
  if(u.role==='Owner')return undefined;
  if(ap==='Manager'&&u.role==='Manager')return undefined;
  return ap==='Manager'?'Only a Manager or the Owner can decide this request.':'Only the Owner can decide this request.';
}

// ───────────────────────────── History, summaries, search ─────────────────────────────
export const cfgHistory=(s:State,key:string)=>(s.configHistory||[]).filter(h=>h.key===key);
export const cfgChange=(s:State,id:string)=>s.configX?.changes?.find(c=>c.id===id);
/** Why a history entry cannot be reverted (undefined = it can). Only the latest applied change of a key, with its raw values kept. */
export function cfgRevertBlock(s:State,id:string):string|undefined{
  const c=cfgChange(s,id);
  if(!c)return 'This change was made before the configuration hub kept values; change it again instead.';
  if(c.status!=='applied')return c.status==='scheduled'?'Not in effect yet — withdraw it instead.':'This change was withdrawn.';
  const later=(s.configX?.changes??[]).find(x=>x.key===c.key&&x.status!=='withdrawn'&&x.at>c.at);
  if(later)return 'A newer change exists for this setting.';
  const def=cfgDef(s,c.key);if(!def)return 'This setting is no longer in the registry.';
  if(!same(cfgRaw(s,def),c.after))return 'The value was changed elsewhere since; change it again instead.';
  return undefined;
}
export const cfgPendingRequests=(s:State,key?:string)=>s.approvals.filter(a=>a.kind==='config'&&a.status==='Pending'&&(!key||a.target===key));
/** One-line live summary per group, e.g. "8 of 10 on" or "12 settings · 2 changes pending". */
export function cfgGroupSummary(s:State,g:CfgGroupId):string{
  for(const p of providers)if(p.group===g&&p.summary)return p.summary(s);
  const defs=cfgGroupDefs(s,g);
  if(g==='modules'){const on=CFG_MODULES.filter(m=>cfgModuleOn(s,m.id)).length;return `${on} of ${CFG_MODULES.length} on · ${CFG_CORE_MODULES.length} core always on`;}
  const keys=new Set(defs.map(d=>d.key));
  const pend=cfgPendingRequests(s).filter(a=>keys.has(String(a.target))).length+(s.configX?.scheduled??[]).filter(x=>keys.has(x.key)).length;
  const editable=defs.filter(d=>d.editable).length;
  const parts=[`${editable} setting${editable===1?'':'s'}`];
  if(g==='rooms')parts.push(`${new Set(s.rooms.map(r=>r.type)).size} room types`);
  if(g==='money'){const fut=(s.configX?.scheduled??[]).filter(x=>x.key.startsWith('gst.')).length+(s.tariffs||[]).filter(t=>t.effectiveFrom>s.date).length;if(fut)parts.push(`${fut} dated rate${fut===1?'':'s'} ahead`);}
  if(pend)parts.push(`${pend} change${pend===1?'':'s'} pending`);
  return parts.join(' · ');
}
const norm=(t:string)=>t.toLowerCase().replace(/[^a-z0-9%₹ ]+/g,' ').replace(/\s+/g,' ').trim();
export type CfgSearchHit={group:CfgGroup;defs:CfgSettingDef[];groupMatch:boolean};
/** Search groups and settings: every word must match the group (name, blurb) or a setting (label, key, help, unit). */
export function cfgSearch(s:State,q:string):CfgSearchHit[]{
  const terms=norm(q).split(' ').filter(Boolean);
  if(!terms.length)return CFG_GROUPS.map(group=>({group,defs:[],groupMatch:true}));
  const all=cfgDefs(s);
  const hits:CfgSearchHit[]=[];
  for(const group of CFG_GROUPS){
    const gt=norm(group.label+' '+group.blurb+' '+group.section);
    const groupMatch=terms.every(t=>gt.includes(t));
    const defs=all.filter(d=>{if(d.group!==group.id)return false;const dt=norm(`${d.label} ${d.key} ${d.help} ${d.unit}`);
      return terms.some(t=>dt.includes(t))&&terms.every(t=>dt.includes(t)||gt.includes(t));});
    if(groupMatch||defs.length)hits.push({group,defs,groupMatch});
  }
  return hits;
}
/** Impact lines shown before saving ("what it affects"). */
export function cfgImpact(s:State,def:CfgSettingDef,v:unknown,effectiveFrom:string):string[]{
  const lines=[def.affects];
  if(effectiveFrom>s.date)lines.push(`Takes effect on ${effectiveFrom}; until then ${cfgShow(def,cfgValueOn(s,def.key))} stays in force.`);
  if(def.money&&def.dated)lines.push('Posted records keep the value that applied on their date.');
  const st=def.store;
  if(st.kind==='switch'){const m=CFG_MODULES.find(x=>x.id===st.module);
    if(m)lines.push(v?`${m.label} appears again in menus, tiles, search and phone tabs; its commands work again.`:`${m.label} is hidden from menus, tiles, search, reminders and phone tabs, and its commands are refused. Its data and history are kept.`);}
  return [...lines,...(def.impact?.(s,v)??[])];
}

// ───────────────────────────── Export / import (JSON, no secrets) ─────────────────────────────
export const CFG_EXPORT_FORMAT='hcc-master-configuration';
export type CfgExport={format:string;version:number;schema:number;exportedAt:string;businessDate:string;property:string;
  values:Record<string,unknown>;scheduled:{key:string;value:unknown;effectiveFrom:string}[];excluded:string[]};
/** Configuration export for backup / a second property. Secrets and fixed values are excluded; nothing personal is included. */
export function cfgExport(s:State):CfgExport{
  const defs=cfgDefs(s);const values:Record<string,unknown>={};const excluded:string[]=[];
  for(const d of defs){if(!d.editable)continue;if(d.secret){excluded.push(d.key);continue;}values[d.key]=cfgRaw(s,d);}
  return {format:CFG_EXPORT_FORMAT,version:1,schema:SCHEMA,exportedAt:s.clock,businessDate:s.date,property:s.settings.hotel,values,
    scheduled:cfgScheduled(s).filter(x=>!defs.find(d=>d.key===x.key)?.secret).map(x=>({key:x.key,value:x.value,effectiveFrom:x.effectiveFrom})),excluded};
}
export type CfgImportPlan={apply:{def:CfgSettingDef;value:unknown;effectiveFrom:string}[];skipped:{key:string;reason:string}[];errors:string[];unchanged:number};
/** Dry run of an import for this user: what would change, what is skipped (and why), and blocking errors. Pure. */
export function cfgImportPlan(s:State,uid:string,data:unknown):CfgImportPlan{
  const plan:CfgImportPlan={apply:[],skipped:[],errors:[],unchanged:0};
  const d=data as Partial<CfgExport>|null;
  if(!d||typeof d!=='object'||d.format!==CFG_EXPORT_FORMAT||typeof d.values!=='object'||!d.values){plan.errors.push('This is not a Master configuration export file.');return plan;}
  if(d.version!==1){plan.errors.push(`Unsupported export version ${String(d.version)}.`);return plan;}
  for(const [key,raw] of Object.entries(d.values)){
    const def=cfgDef(s,key);
    if(!def){plan.skipped.push({key,reason:'Not in this registry'});continue;}
    if(def.secret){plan.skipped.push({key,reason:'Secrets are never imported'});continue;}
    if(!def.editable){plan.skipped.push({key,reason:'Not configurable'});continue;}
    const v=cfgClean(def,raw);
    const err=cfgValidate(s,def,v);if(err){plan.errors.push(`${def.label}: ${err}`);continue;}
    if(same(cfgValueOn(s,key,def.effectiveMode==='nextDay'?addDays(s.date,1):s.date),v)&&!cfgScheduled(s,key).length){plan.unchanged++;continue;}
    const dec=cfgCanChange(s,uid,def);
    if(dec.mode==='deny'){plan.skipped.push({key,reason:dec.reason});continue;}
    if(dec.mode==='request'){plan.skipped.push({key,reason:'Needs approval — change it on its own'});continue;}
    const eff=cfgEffectiveFrom(s,def);
    plan.apply.push({def,value:v,effectiveFrom:'date' in eff?eff.date:s.date});
  }
  return plan;
}

// ───────────────────────────── Built-in providers ─────────────────────────────
const GROUP_OF_LEGACY:Record<string,CfgGroupId>={hotel:'property',rooms:'rooms',checkin:'frontdesk',checkout:'frontdesk',currency:'money',
  discountLimit:'money',approvalExpiryHours:'money',managerLimits:'money',inspection:'hkm',openingCash:'money',shiftModel:'money',invoicePrefix:'money',watermark:'property'};
const LEGACY_EXTRA:Record<string,Partial<CfgProviderDef>>={
  rooms:{fixedReason:'Rooms come from the fixture; tariffs are scheduled below (Owner only)'},
  hotel:{maxLength:60,affects:'Header, sidebar, documents and the export file name.',validate:v=>String(v).trim().length?undefined:'Property name is required (up to 60 characters).'},
  checkin:{affects:'Arrivals expected today and printed confirmations. Booking dates and charges never change.'},
  checkout:{affects:'In-house stays departing today and printed folios. Booking dates and charges never change.'},
  discountLimit:{max:10000000,effectiveMode:'date',dated:true,affects:'Discounts above this need another authorised user’s approval (PERM-FOL-003). Pending requests are not re-evaluated.'},
  approvalExpiryHours:{min:1,max:168,default:24,suffix:'h',show:v=>String(v)+' h',affects:'New approval requests expire after this many simulated hours; existing requests keep their expiry.'},
  inspection:{approver:'Manager',affects:'Cleaning completed after the change; tasks already awaiting inspection are unaffected.'},
  invoicePrefix:{default:'DEMO-INV',pattern:/^[A-Z0-9-]{2,8}$/,patternMsg:'Use 2–8 capital letters, digits or hyphens.',maxLength:8,affects:'Demonstration documents generated after the change.'},
};
function legacyDefs():CfgProviderDef[]{
  return ADMIN_SETTINGS.filter(d=>GROUP_OF_LEGACY[d.key]).map(d=>{
    const fixed=!d.editable;
    const base:CfgProviderDef={key:d.key,label:d.label,unit:d.unit,help:d.help,type:fixed?'fixed':d.type==='time'?'time':d.type,
      store:fixed?{kind:'fixed',read:(s:State)=>adminSettingValue(s,d.key)}:{kind:'settings'},editable:!fixed,fixedReason:d.approval,
      panel:d.key==='rooms'?'tariffs':undefined};
    return {...base,...LEGACY_EXTRA[d.key]};
  });
}
const pct=(v:unknown)=>yldPct(Number(v));
function builtins(){
  if(builtinsDone)return;builtinsDone=true;
  // Legacy BL-027 settings (Stream E) — same keys, same stores, so every existing screen and test keeps working.
  for(const g of CFG_GROUPS)registerSettingsGroup({id:'core-'+g.id,group:g.id,defs:()=>legacyDefs().filter(d=>GROUP_OF_LEGACY[d.key]===g.id)});
  registerSettingsGroup({id:'property',group:'property',defs:[
    {key:'brand.address',label:'Address on documents',type:'text',maxLength:120,default:'12 Station Road, Latur 413512 (fictional)',unit:'text, ≤ 120 chars',help:'Printed under the property name.',affects:'Invoices, folios and confirmations generated after the change.'},
    {key:'brand.gstin',label:'GSTIN on documents',type:'text',maxLength:15,allowEmpty:true,default:'',pattern:/^[0-9A-Z]{15}$/,patternMsg:'A GSTIN has 15 capital letters and digits (fictional values only in the prototype).',unit:'15 characters, optional',help:'Fictional only — the prototype never validates a real GSTIN.',affects:'Document headers generated after the change.'},
    {key:'brand.invoiceHeader',label:'Invoice header line',type:'text',maxLength:80,default:'HCC Demo Residency · fictional property',unit:'text, ≤ 80 chars',help:'Second line of the invoice header.'},
    {key:'brand.invoiceFooter',label:'Invoice footer',type:'text',maxLength:120,default:'Thank you for staying with us.',unit:'text, ≤ 120 chars',help:'The demonstration watermark is always printed as well.'},
    {key:'brand.printFormat',label:'Print format',type:'choice',options:['A4','A5','80 mm thermal'],default:'A4',unit:'choice',help:'Default paper for invoices and receipts.'},
    {key:'brand.language',label:'Document language',type:'choice',options:['English','English + Hindi labels'],default:'English',unit:'choice',help:'Labels on printed documents.'},
  ]});
  registerSettingsGroup({id:'yield-guard',group:'rooms',defs:[
    {key:'yld.floorBps',label:'Rate guardrail · floor',type:'percent',min:-5000,max:0,unit:'% below base',help:'Lowest suggested rate versus the base tariff (Tariff & yield).',show:pct,
      read:s=>yldSlice(s).guard.floorBps,command:(s,v)=>({type:'YLD_GUARD_SET',...yldSlice(s).guard,floorBps:v}),affects:'Suggested rates from now on; published and booked rates are unchanged.'},
    {key:'yld.ceilBps',label:'Rate guardrail · ceiling',type:'percent',min:0,max:10000,unit:'% above base',help:'Highest suggested rate versus the base tariff.',show:pct,
      read:s=>yldSlice(s).guard.ceilBps,command:(s,v)=>({type:'YLD_GUARD_SET',...yldSlice(s).guard,ceilBps:v}),affects:'Suggested rates from now on; published and booked rates are unchanged.'},
    {key:'yld.roundTo',label:'Rate rounding',type:'choice',options:YLD_ROUND_OPTIONS,unit:'₹ step',help:'Suggested rates are rounded to this step.',show:v=>money(Number(v)),
      read:s=>yldSlice(s).guard.roundTo,command:(s,v)=>({type:'YLD_GUARD_SET',...yldSlice(s).guard,roundTo:v}),affects:'Suggested rates from now on.'},
  ]});
  registerSettingsGroup({id:'frontdesk',group:'frontdesk',defs:[
    {key:'fd.holdHours',label:'Unconfirmed hold expiry',type:'integer',min:1,max:72,default:6,suffix:'h',unit:'simulated hours',effective:'nextDay',help:'Holds without an advance are released after this long.',affects:'Holds created from the next business day.'},
    {key:'fd.deposit',label:'Refundable deposit at check-in',type:'money',money:true,min:0,max:2000000,default:100000,unit:'₹ per stay',effective:'nextDay',dated:true,help:'Suggested deposit shown at check-in.',affects:'Check-ins from the next business day; deposits already held are unchanged.'},
    {key:'fd.idRequired',label:'ID documents required',type:'choice',options:['Lead guest only','Every adult guest'],default:'Every adult guest',unit:'choice',help:'Fictional ID metadata only in the prototype.',affects:'Registrations from now on.'},
    {key:'fd.noShowTime',label:'No-show cut-off',type:'time',default:'23:00',unit:'HH:mm (IST)',effective:'nextDay',help:'Confirmed arrivals not checked in by this time may be marked no-show.',affects:'Arrivals from the next business day.'},
  ]});
  registerSettingsGroup({id:'gsv-sla',group:'frontdesk',defs:()=>[
    ...GSV_REQUEST_TYPES.map(t=>({key:'gsv.sla.'+t,label:'Guest request SLA · '+t,type:'integer' as const,min:5,max:480,suffix:'min',unit:'minutes',help:'Guest Services marks requests late after this.',
      read:(s:State)=>gsvSettings(s).sla[t],command:(_s:State,v:unknown)=>({type:'GSV_SETTINGS',sla:{[t]:v}}),affects:'Open and new guest requests are timed against the new SLA.'})),
    {key:'gsv.retentionDays',label:'Lost & found retention',type:'integer',min:30,max:365,suffix:'days',unit:'days',help:'Unclaimed items can be disposed after this.',
      read:s=>gsvSettings(s).retentionDays,command:(_s,v)=>({type:'GSV_SETTINGS',retentionDays:v}),affects:'Disposal dates of logged items.'},
    {key:'gsv.valuableRetentionDays',label:'Lost & found retention · valuables',type:'integer',min:30,max:730,suffix:'days',unit:'days',help:'At least the normal retention.',
      read:s=>gsvSettings(s).valuableRetentionDays,command:(_s,v)=>({type:'GSV_SETTINGS',valuableRetentionDays:v}),affects:'Disposal dates of valuable items.'},
  ]});
  registerSettingsGroup({id:'pos-menu',group:'pos',defs:s=>[
    {key:'pos.outlets',label:'Outlets',type:'fixed',read:x=>posSlice(x).outlets.map(o=>o.name).join(', ')||'None',fixedReason:'Outlets are set up in Restaurant & POS',help:'Restaurant and room-service outlets.'},
    ...posSlice(s).menu.map(m=>({key:'pos.menu.'+m.id,label:`Menu · ${m.name} available`,type:'boolean' as const,unit:'on / off',help:`${m.category} · ${money(m.price)}. Off = “86”, not orderable.`,
      read:(x:State)=>posSlice(x).menu.find(i=>i.id===m.id)?.available??false,command:()=>({type:'POS_MENU_TOGGLE',item:m.id}),affects:'New orders; items already on a check are unchanged.'})),
  ]});
  registerSettingsGroup({id:'hkm',group:'hkm',defs:[
    {key:'ai.dirtyHours',label:'Dirty-room alert threshold',type:'integer',min:1,max:48,suffix:'h',unit:'hours',help:'The Management assistant flags rooms dirty for longer than this.',
      read:aiDirtyHours,command:(_s,v)=>({type:'AI_SETTINGS',dirtyHours:v}),affects:'Assistant anomaly alerts from now on.'},
    {key:'hk.maxRoomsPerAttendant',label:'Rooms per attendant (guide)',type:'integer',min:4,max:20,default:12,suffix:'rooms',unit:'rooms per shift',help:'Planning guide shown on the housekeeping board.',affects:'Planning hints only.'},
    {key:'mnt.pmLeadDays',label:'Preventive maintenance lead time',type:'integer',min:1,max:30,default:7,suffix:'days',unit:'days',help:'Preventive work orders are listed this many days ahead.',affects:'Preventive plans generated after the change.'},
  ]});
  // W05-B statutory rates and leave policy (dated rate versions; hrSettingDefs) and W05-C compliance settings (cmpSettingDefs).
  registerSettingsGroup({id:'hr-statutory',group:'people',defs:hrStatutoryDefs});
  // DEC-069 GST master (domain/gst.ts): dated, Owner-only (money group), illustrative.
  registerSettingsGroup({id:'gst',group:'money',defs:gstDefs});
  registerSettingsGroup({id:'compliance',group:'compliance',defs:cmpDefs});
  registerSettingsGroup({id:'modules',group:'modules',defs:CFG_MODULES.map(m=>({key:'module.'+m.id,label:m.label,type:'boolean' as const,unit:'on / off',help:m.note,store:{kind:'switch' as const,module:m.id},
    affects:'Menus, tiles, search, reminders and phone tabs; commands are refused while off. Data is kept.',
    impact:(s:State,v:unknown)=>v===false&&m.id==='pos'?[`${posSlice(s).orders.filter(o=>o.status==='Open').length} open restaurant check(s) stay open and can be settled after switching back on.`]:[]}))});
  registerSettingsGroup({id:'users',group:'users',defs:[
    {key:'users.active',label:'Active users',type:'fixed',read:s=>`${s.users.filter(u=>adminUserActive(s,u)).length} of ${s.users.length}`,fixedReason:'Users and roles are changed in Access control',help:'Create, disable and assign roles in Access control.'},
    {key:'sec.pinLock',label:'PIN lock on shared devices',type:'boolean',default:false,approver:'Owner',unit:'on / off',help:'Off by default; switch on deliberately (SCC rule). Simulated in the prototype.',affects:'Next sign-in on every device.'},
    {key:'sec.pinIdleMinutes',label:'Lock after idle',type:'integer',min:1,max:60,default:5,suffix:'min',approver:'Owner',unit:'minutes',help:'Used when the PIN lock is on.',affects:'Next sign-in on every device.'},
  ]});
  registerSettingsGroup({id:'messages',group:'messages',defs:[
    {key:'msg.senderName',label:'Sender name',type:'text',maxLength:40,default:'HCC Demo Residency',unit:'text, ≤ 40 chars',help:'Shown on simulated WhatsApp, SMS and e-mail messages.'},
    {key:'msg.marketing',label:'Marketing messages',type:'choice',options:['Only with guest consent','Never'],default:'Only with guest consent',unit:'choice',help:'Service messages are not affected.'},
    {key:'msg.gatewayKey',label:'Messaging gateway key (simulated)',type:'text',maxLength:64,allowEmpty:true,default:'',secret:true,approver:'Owner',unit:'secret',help:'Never shown in full and never exported.',affects:'Simulated sends only.'},
    {key:'doc.retentionDays',label:'Captured document retention',type:'integer',min:30,max:3650,default:365,suffix:'days',unit:'days',help:'Captured images are discarded after this (metadata stays).'},
  ]});
  registerSettingsGroup({id:'data',group:'data',defs:[
    {key:'data.backupTime',label:'Daily backup time (simulated)',type:'time',default:'02:00',unit:'HH:mm (IST)',help:'Local backup schedule.'},
    {key:'data.backupKeep',label:'Backups kept',type:'integer',min:1,max:90,default:14,suffix:'days',unit:'days'},
    {key:'data.restoreDrillDays',label:'Restore drill every',type:'integer',min:7,max:180,default:30,suffix:'days',unit:'days',help:'Reminder to test a restore.'},
    {key:'about.version',label:'Version',type:'fixed',read:()=>`Prototype 0.1 · data schema ${SCHEMA}`,fixedReason:'Set by the build'},
  ]});
}
/** Role names allowed to decide a request with this approver (for UI text). */
export const cfgApproverRoles=(a:CfgApprover):Role[]=>a==='Manager'?['Manager','Owner']:['Owner'];

// ───────────────────────────── Adapters for W05-B / W05-C settings ─────────────────────────────
type HRField={label:string;type:CfgType;min?:number;max?:number;options?:string[];show?:(v:unknown)=>string};
const HR_FIELDS:Record<string,HRField>={
  basicBps:{label:'basic share of earned pay',type:'percent',min:1000,max:10000},hraBps:{label:'HRA (% of basic)',type:'percent',min:0,max:10000},washBps:{label:'washing allowance (% of basic)',type:'percent',min:0,max:10000},
  pfEeBps:{label:'employee rate',type:'percent',min:0,max:2500},pfErBps:{label:'employer rate',type:'percent',min:0,max:2500},pfWageCap:{label:'wage cap',type:'money',min:0,max:100000000},
  esicEeBps:{label:'employee rate',type:'percent',min:0,max:1000},esicErBps:{label:'employer rate',type:'percent',min:0,max:1000},esicCeiling:{label:'earned-pay ceiling',type:'money',min:0,max:100000000},
  ptMaleExempt:{label:'men nil slab up to',type:'money',min:0,max:100000000},ptMaleMid:{label:'men middle slab up to',type:'money',min:0,max:100000000},ptMaleMidAmt:{label:'men middle slab amount',type:'money',min:0,max:100000000},
  ptStd:{label:'standard amount',type:'money',min:0,max:100000000},ptFeb:{label:'February amount',type:'money',min:0,max:100000000},ptFemaleExempt:{label:'women nil slab up to',type:'money',min:0,max:100000000},
  otBps:{label:'multiplier (% of ordinary rate)',type:'percent',min:10000,max:30000,show:v=>(Number(v)/10000)+'×'},lateBlock:{label:'late marks per ½ day (0 = off)',type:'integer',min:0,max:10},
  rounding:{label:'rounding',type:'choice',options:['paise','rupee'],show:v=>v==='paise'?'To paise (SCC)':'PF nearest ₹ · ESIC next ₹'},
  esicPeriod:{label:'contribution-period rule',type:'boolean',show:v=>v?'Apr–Sep / Oct–Mar (coverage kept to period end)':'Month by month on earned pay (SCC)'},
};
const curYm=(s:State)=>s.date.slice(0,7);
/** W05-B hrSettingDefs() → hub settings. Rate fields are wrappers over HR_STAT_RATES_SET with an explicit `from` (W05-B review F-06:
 *  the month after the last completed pay period, never a completed month; shown before saving); leave entitlements and the per-department cap
 *  wrap HR_LEAVE_POLICY_SET; the rest is shown read-only. */
export function hrStatutoryDefs():CfgProviderDef[]{
  const out:CfgProviderDef[]=[];
  for(const d of hrSettingDefs() as HRSettingDef[]){
    const base={unit:d.unit,help:d.help,illustrative:d.category==='Statutory rates',approver:d.approver,affects:d.affects};
    if(d.command==='HR_STAT_RATES_SET'&&d.fields?.length){
      // W05P-2: the long SCC paragraph is shown once per setting (its first field); the other fields get a short line.
      (d.fields as (keyof HRRates)[]).filter(f=>HR_FIELDS[f as string]).forEach((f,i)=>{const m=HR_FIELDS[f as string];
        out.push({...base,key:`${d.key}.${String(f)}`,label:`${d.label} · ${m.label}`,type:m.type,min:m.min,max:m.max,options:m.options,show:m.show,
          effectiveText:'From the 1st of the month after the last completed pay period (new dated rate version); locked runs keep theirs',
          help:i===0?d.help:'Part of the same dated rate version. Illustrative — CA to confirm for a hotel.',
          read:s=>hrRatesFor(s,hrRatesDefaultFrom(s).slice(0,7))[f],command:(s,v)=>({type:'HR_STAT_RATES_SET',from:hrRatesDefaultFrom(s),[f]:v}),
          impact:s=>[`Effective from 1 ${hrMonthLabel(hrRatesDefaultFrom(s).slice(0,7))} (new rate version); earlier months and locked pay-runs keep their rates.`]});});
    }else if(d.key==='hr.leave.ent'){
      for(const lt of ['CL','SL','EL'] as const)out.push({...base,key:`hr.leave.ent.${lt}`,label:`${d.label} · ${lt==='CL'?'casual':lt==='SL'?'sick':'earned'} leave`,type:'integer',min:0,max:60,unit:'days per financial year',
        read:s=>hrLeavePolicy(s).ent[lt],command:(s,v)=>({type:'HR_LEAVE_POLICY_SET',ent:{...hrLeavePolicy(s).ent,[lt]:v}})});
    }else if(d.key==='hr.leave.cap'){
      out.push({...base,key:'hr.leave.cap.default',label:d.label,type:'integer',min:1,max:20,unit:'staff per department per day',help:d.help+' Department overrides are set in Staff and payroll.',
        read:s=>hrLeavePolicy(s).defaultCap,command:(_s,v)=>({type:'HR_LEAVE_POLICY_SET',defaultCap:v})});
    }else out.push({...base,key:d.key,label:d.label,type:'fixed',read:s=>hrSettingValue(s,d.key),fixedReason:d.editable?'Edited in Staff and payroll':d.approval});
  }
  return out;
}
/** W05-C cmpSettingDefs(s) → hub settings: CA name, CA e-mail and the due-soon window wrap CMP_SETTINGS_SET; due rules are dated and edited in the calendar. */
export function cmpDefs(s:State):CfgProviderDef[]{
  const field:Record<string,'caName'|'caEmail'|'dueSoonDays'>={'cmp.caName':'caName','cmp.caEmail':'caEmail','cmp.dueSoonDays':'dueSoonDays'};
  const valueOf=(x:State,key:string)=>(cmpSettingDefs(x) as CMPSettingDef[]).find(d=>d.key===key)?.value;
  return (cmpSettingDefs(s) as CMPSettingDef[]).map(d=>{
    const f=field[d.key];
    if(!f)return {key:d.key,label:d.label,type:'fixed' as const,unit:d.unit,help:d.help,read:(x:State)=>String(valueOf(x,d.key)??'—'),fixedReason:'Dated rule — edit in Compliance calendar › Manage obligations'};
    return {key:d.key,label:d.label,type:d.type==='integer'?'integer' as const:'text' as const,unit:d.unit,help:d.help,
      ...(f==='caName'?{maxLength:60,validate:(v:unknown)=>String(v).trim().length<3?'CA name must be 3–60 characters.':undefined}:f==='caEmail'?{maxLength:80,pattern:/^[^\s@]+@[^\s@]+\.[^\s@]+$/,patternMsg:'Enter a valid e-mail address.'}:{min:1,max:30,suffix:'days'}),
      affects:f==='dueSoonDays'?'Dashboard “Due this week” card and the Accounts tile badge.':'The simulated CA pack.',
      read:(x:State)=>valueOf(x,d.key),command:(_x:State,v:unknown)=>({type:'CMP_SETTINGS_SET',[f]:v})};
  });
}
/** DEC-069 GST master → hub settings. Values are dated (effective-from), Owner-only (DEC-058), stored by the hub; domain/gst.ts reads them
 *  (gstValueOn) for every new posting. The default shown is the seed version in force on the business date. */
export function gstDefs(s:State):CfgProviderDef[]{
  const out:CfgProviderDef[]=GST_SEED.map(d=>({key:d.key,label:d.label,type:d.type,unit:d.unit,help:`${d.help} ${GST_ILLUSTRATIVE}.`,money:true,dated:true,effectiveMode:'date' as const,illustrative:true,
    default:gstSeedOn(d.key as GstKey,s.date),...(d.type==='percent'?{min:0,max:2800,show:(v:unknown)=>gstPct(Number(v))}:d.type==='money'?{min:100,max:10000000}:{show:(v:unknown)=>d.key==='gst.food.optIn'?(v?'Declared (opt-in)':'Not declared'):(v?'Available':'Not available')}),
    affects:'New room nights, charges and restaurant bills dated on or after the effective date. Posted lines keep their tax snapshot.',
    impact:(x:State)=>d.key==='gst.food.optIn'?[`Specified premises today: ${gstSpecifiedPremises(x,x.date).specified?'yes':'no'} (${gstSpecifiedPremises(x,x.date).reason}).`]:[]}));
  out.push({key:'gst.specified.status',label:'GST · specified premises this financial year',type:'fixed',fixedReason:'Derived from last financial year’s tariffs, or the opt-in above',
    help:'Decides the restaurant / room-service rate. '+GST_ILLUSTRATIVE+'.',read:x=>{const sp=gstSpecifiedPremises(x,x.date);return `FY ${sp.fy}: ${sp.specified?'specified premises':'not specified premises'} — ${sp.reason}`;}});
  return out;
}
