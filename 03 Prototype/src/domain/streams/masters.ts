// MA-0 · Masters framework (plan HCC_Customisation_Features_Plan_v1.md Part 2 §9). One registry of configurable lists
// ("masters": floors, room types, payment methods, …) with ONE row shape, ONE command pattern and ONE UI (features/masters).
// Pure domain: registry, row rules, validation, in-use, history, permissions. Commands: services/mock/handlers/masters.ts.
//
// ── Provider API (used by MA-1 … MA-7) ──────────────────────────────────────────────────────────────────────────────
//   registerMaster({id:'roomTypes',label:'Room types',singular:'room type',group:'rooms',prefix:'RTY',money:false,permission:'masters',
//     fields:[{key:'code',label:'Code',type:'text',required:true,maxLength:20},{key:'name',label:'Name',type:'text',required:true,maxLength:60},
//             {key:'maxAdults',label:'Max adults',type:'number',min:1,max:8},{key:'rackRate',label:'Rack rate',type:'datedMoney',money:true}],
//     inUse:(s,row)=>({count:s.rooms.filter(r=>r.type===row.code).length,where:['Rooms']}),
//     seed:s=>[...]});                       // rows used until the first change is saved (old saves / fresh fixture)
// Optional hooks: validate(s,row,rows) → message (MASTER_INVALID), canRetire(s,row) → message (MASTER_IN_USE),
// store {read,write} = the rows live elsewhere (MA-1: s.rooms itself) instead of s.mastersX.rows[id].
// Commands are generic (MST_MASTER_SAVE / _RETIRE / _REACTIVATE / _REORDER / _DELETE {master,…}); a module may also expose
// prefixed aliases with mstHandlersFor('<PFX>') (handlers/masters.ts) in its own handler file.
//
// ── Row rules (§9.1) ──────────────────────────────────────────────────────────────────────────────────────────────────
// id, code (unique incl. retired rows, ^[A-Z0-9-]{1,20}$, immutable once in use), name (1–60), sortOrder, active, retiredOn.
// Retire, never hard-delete, when in use; delete only when unused. Money fields are Owner-only (DEC-058) and hidden from roles
// without money visibility (DEC-061). Audit line "Master <label>: <field> old → new"; history per field in s.mastersX.history.
import type {State,Room,Role} from '../types';
import {assignedScopeRoles} from '../types';
import type {CfgGroupId} from './config';
import {HccError,type ErrorCode} from '../errors';
import {can} from '../selectors';
import {authorize} from './admin';
import {mastersSeed} from '../../fixtures/ext/masters-seed';

// ───────────────────────────── Types ─────────────────────────────
/** One dated amount (paise) of a datedMoney field; the latest effectiveFrom ≤ date is in force (tariff pattern). */
export type MstDatedAmount={amount:number;effectiveFrom:string};
/** Base row of every master. Spec fields add their own keys. */
export type MasterRow={id:string;code:string;name:string;sortOrder:number;active:boolean;retiredOn?:string;[field:string]:unknown};
export type MstFieldType='text'|'number'|'money'|'percent'|'select'|'toggle'|'datedMoney'|'multiselect';
/** Field spec: drives validation (mstValidate) and the form (features/masters/MasterForm). money = integer paise; percent = basis points. */
export type FieldSpec={key:string;label:string;type:MstFieldType;required?:boolean;maxLength?:number;min?:number;max?:number;
  /** Fixed choices for select / multiselect. */options?:{value:string;label:string}[];
  /** Choices from another master (its codes; active rows offered, a retired value already on the row stays valid). */master?:string;
  /** MA-7: choices computed from the state (e.g. enabled housekeepers, rooms); a value already on the row stays valid when it drops out. */optionsFrom?:(s:State)=>{value:string;label:string}[];
  /** MA-7: text field edited in a text area (one item per line). */multiline?:boolean;
  /** Money field: Owner-only change (DEC-058), hidden from roles without money visibility (DEC-061). money/datedMoney imply it. */money?:boolean;
  help?:string;placeholder?:string};
export type MasterUsage={count:number;where:string[]};
export type MasterSpec<T extends MasterRow=MasterRow>={
  id:string;label:string;singular:string;group:CfgGroupId;
  /** Upper-case prefix for new row ids (and a module's optional command alias prefix, see mstHandlersFor). */prefix:string;
  fields:FieldSpec[];
  /** Money master (payment methods, extras…): every change is Owner-only (DEC-058). */money:boolean;
  /** 'masters' = operational list (Owner and Tech admin by default, DEC-MA-1); 'settings' = Owner-only like money settings. */permission:'masters'|'settings';
  /** Where the row is used (pure selector). count > 0 → cannot delete, code cannot change. */inUse:(s:State,row:T)=>MasterUsage;
  /** Extra rule → message (MASTER_INVALID). */validate?:(s:State,row:T,rows:T[])=>string|undefined;
  /** Refuse retiring (MASTER_IN_USE), e.g. an outlet with open checks. */canRetire?:(s:State,row:T)=>string|undefined;
  /** Rows before the first saved change (fixture values; old saves). */seed?:(s:State)=>T[];
  /** Rows stored outside s.mastersX (MA-1 rooms read/write s.rooms). */store?:{read:(s:State)=>T[];write:(s:State,rows:T[])=>void};
  /** One line under the title in the list. */help?:string;
  /** V14-7: the name follows from the code (rooms: "Room 401"); a pasted or imported row may leave the name out. */autoName?:(code:string)=>string};
export type MSTHistory={id:string;master:string;rowId:string;code:string;field:string;old:string;new:string;by:string;at:string};
export type MSTState={rows:Record<string,MasterRow[]>;history:MSTHistory[]};

// ───────────────────────────── Errors ─────────────────────────────
export const MST_ERROR_CODES=['MASTER_CODE_TAKEN','MASTER_IN_USE','MASTER_RETIRED','MASTER_LOCKED_BY_BOOKINGS','MASTER_INVALID'] as const;
export type MstErrorCode=typeof MST_ERROR_CODES[number];
/** Wire code of each master error (the shared HccError union stays unchanged; the server sends the same pair as code + details.reason). */
export const MST_ERROR_BASE:Record<MstErrorCode,ErrorCode>={MASTER_CODE_TAKEN:'VALIDATION',MASTER_INVALID:'VALIDATION',MASTER_IN_USE:'INVALID_TRANSITION',
  MASTER_RETIRED:'INVALID_TRANSITION',MASTER_LOCKED_BY_BOOKINGS:'INVALID_TRANSITION'};
/** Typed master error: an HccError (code VALIDATION / INVALID_TRANSITION) carrying the master reason. */
export class MstError extends HccError{
  constructor(public readonly reason:MstErrorCode,message:string,public readonly field?:string){super(MST_ERROR_BASE[reason],message);}
}
export const mstFail=(reason:MstErrorCode,message:string,field?:string):never=>{throw new MstError(reason,message,field);};
/** Master reason of an error (prototype MstError or a server error with details.reason), else undefined. */
export function mstReasonOf(e:unknown):MstErrorCode|undefined{
  const r=(e as {reason?:unknown;details?:{reason?:unknown}}|null)?.reason??(e as {details?:{reason?:unknown}}|null)?.details?.reason;
  return MST_ERROR_CODES.includes(r as MstErrorCode)?r as MstErrorCode:undefined;
}

// ───────────────────────────── Registry ─────────────────────────────
const specs:MasterSpec[]=[];
let builtinsDone=false;
function builtins(){if(builtinsDone)return;builtinsDone=true;registerMaster(FLOOR_SPEC as MasterSpec);}
/** Register (or replace, same id) a master. Call at module load of the providing module. */
export function registerMaster<T extends MasterRow>(spec:MasterSpec<T>){
  builtins();
  const i=specs.findIndex(x=>x.id===spec.id);
  if(i>=0)specs[i]=spec as unknown as MasterSpec;else specs.push(spec as unknown as MasterSpec);
}
/** Remove a registered master (tests, or a provider switched off). The built-in Floors master cannot be removed. */
export function unregisterMaster(id:string){const i=specs.findIndex(x=>x.id===id);if(i>=0&&id!==MST_FLOORS)specs.splice(i,1);}
export const masterSpecs=():MasterSpec[]=>{builtins();return [...specs];};
export const mstSpec=(id:string):MasterSpec|undefined=>masterSpecs().find(x=>x.id===id);
export const mstSpecsOfGroup=(g:CfgGroupId|string)=>masterSpecs().filter(x=>x.group===g);

// ───────────────────────────── Rows ─────────────────────────────
export const MST_CODE_PATTERN=/^[A-Z0-9-]{1,20}$/;
export const MST_NAME_MAX=60;
const bySort=(a:MasterRow,b:MasterRow)=>a.sortOrder-b.sortOrder||(a.code<b.code?-1:a.code>b.code?1:0);
/** True once the list has been saved (s.mastersX.rows[id] or the spec's own store). */
export const mstStored=(s:State,specId:string)=>{const sp=mstSpec(specId);return !!sp?.store||Array.isArray(s.mastersX?.rows?.[specId]);};
/** All rows of a master in sort order (stored rows, else the seed). active:true = active only, false = retired only. */
export function mstRows(s:State,specId:string,opts:{active?:boolean}={}):MasterRow[]{
  const sp=mstSpec(specId);if(!sp)return [];
  const raw=sp.store?sp.store.read(s):Array.isArray(s.mastersX?.rows?.[specId])?s.mastersX!.rows[specId]:(sp.seed?.(s)??[]);
  return raw.filter(r=>opts.active===undefined||r.active===opts.active).sort(bySort);
}
/** Row by code — retired rows still resolve (old records keep their value). */
export const mstByCode=(s:State,specId:string,code:string)=>mstRows(s,specId).find(r=>r.code===code);
export const mstById=(s:State,specId:string,id:string)=>mstRows(s,specId).find(r=>r.id===id);
/** Display name of a code (retired rows resolve too; unknown code → the code itself). */
export const mstLabel=(s:State,specId:string,code:string)=>mstByCode(s,specId,code)?.name??code;
/** Pick-list options: active rows, plus the current value when it is retired ("(retired)"). */
export function mstOptions(s:State,specId:string,current?:string|string[]):{value:string;label:string;retired?:boolean}[]{
  const cur=new Set(Array.isArray(current)?current:current?[current]:[]);
  return mstRows(s,specId).filter(r=>r.active||cur.has(r.code)).map(r=>({value:r.code,label:r.active?r.name:`${r.name} (retired)`,...(r.active?{}:{retired:true})}));
}
/** Next sortOrder (steps of 10 after the last row). */
export const mstNextSort=(rows:MasterRow[])=>rows.reduce((m,r)=>Math.max(m,Number(r.sortOrder)||0),0)+10;
/** Ids after moving one row up/down among the given (visible) order; other rows keep their relative order. */
export function mstMove(rows:MasterRow[],id:string,dir:-1|1):string[]{
  const ids=[...rows].sort(bySort).map(r=>r.id);const i=ids.indexOf(id),j=i+dir;
  if(i<0||j<0||j>=ids.length)return ids;[ids[i],ids[j]]=[ids[j],ids[i]];return ids;
}
/** Amount of a datedMoney value in force on a date (undefined before the first entry). */
export function mstMoneyOn(v:unknown,date:string):number|undefined{
  const list=Array.isArray(v)?(v as MstDatedAmount[]).filter(x=>x&&x.effectiveFrom<=date).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1):[];
  return list.length?list[list.length-1].amount:undefined;
}
export const mstHistory=(s:State,specId:string,id?:string)=>(s.mastersX?.history??[]).filter(h=>h.master===specId&&(!id||h.rowId===id));
export const mstUsage=(s:State,spec:MasterSpec,row:MasterRow):MasterUsage=>{try{return spec.inUse(s,row);}catch{return {count:0,where:[]};}};

// ───────────────────────────── Validation ─────────────────────────────
export type MstProblem={reason:MstErrorCode;field?:string;message:string};
const isMoneyField=(f:FieldSpec)=>!!f.money||f.type==='money'||f.type==='datedMoney';
export const mstMoneyFields=(spec:MasterSpec)=>spec.fields.filter(isMoneyField);
const ISO=/^\d{4}-\d{2}-\d{2}$/;
/** Normalise editor input into a row (code upper-case, trimmed text, numbers as numbers). Unknown keys are dropped. */
export function mstClean(spec:MasterSpec,input:Record<string,unknown>,base?:MasterRow):MasterRow{
  const out:Record<string,unknown>={...(base??{})};
  const keys=new Set(['id','code','name','sortOrder',...spec.fields.map(f=>f.key)]);
  for(const [k,v] of Object.entries(input??{})){
    if(!keys.has(k)||k==='active'||k==='retiredOn'||k==='id')continue;
    const f=spec.fields.find(x=>x.key===k);
    if(k==='code')out.code=String(v??'').trim().toUpperCase();
    else if(k==='name')out.name=String(v??'').trim();
    else if(k==='sortOrder')out.sortOrder=v===''||v===undefined||v===null?undefined:Number(v);
    else if(f?.type==='text'||f?.type==='select')out[k]=typeof v==='string'?v.trim():v;
    else if(f?.type==='toggle')out[k]=!!v;
    else if(f?.type==='number'||f?.type==='money'||f?.type==='percent')out[k]=v===''||v===undefined||v===null?undefined:Number(v);
    else if(f?.type==='multiselect')out[k]=Array.isArray(v)?[...new Set(v.map(String))]:[];
    else if(f?.type==='datedMoney')out[k]=Array.isArray(v)?(v as MstDatedAmount[]).map(x=>({amount:Number(x?.amount),effectiveFrom:String(x?.effectiveFrom??'')})).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1):[];
    else out[k]=v;
  }
  return out as MasterRow;
}
/** Every problem of a row (inline form errors). `rows` = the master's current rows; `s` enables master-select checks and spec.validate. */
export function mstProblems(spec:MasterSpec,row:MasterRow,rows:MasterRow[],s?:State,old?:MasterRow):MstProblem[]{
  const out:MstProblem[]=[];const add=(reason:MstErrorCode,field:string|undefined,message:string)=>{if(!out.some(p=>p.field===field&&field))out.push({reason,field,message});};
  const code=String(row.code??''),name=String(row.name??'');
  if(!code)add('MASTER_INVALID','code','Enter a code.');
  else if(!MST_CODE_PATTERN.test(code))add('MASTER_INVALID','code','Code: 1–20 capital letters, digits or “-”.');
  else if(rows.some(r=>r.id!==row.id&&r.code.toUpperCase()===code))add('MASTER_CODE_TAKEN','code',`Code ${code} is already used in ${spec.label} (retired rows keep their code).`);
  if(!name)add('MASTER_INVALID','name','Enter a name.');
  else if(name.length>MST_NAME_MAX)add('MASTER_INVALID','name',`Name: at most ${MST_NAME_MAX} characters.`);
  else if(rows.some(r=>r.id!==row.id&&r.active&&r.name.toLowerCase()===name.toLowerCase()))add('MASTER_INVALID','name',`Another active ${spec.singular} is already called “${name}”.`);
  if(!Number.isInteger(row.sortOrder)||row.sortOrder<0||row.sortOrder>1_000_000)add('MASTER_INVALID','sortOrder','Sort order: a whole number from 0 to 1,000,000.');
  for(const f of spec.fields){
    if(['code','name','sortOrder','active'].includes(f.key))continue;
    const v=row[f.key],empty=v===undefined||v===null||v===''||(Array.isArray(v)&&!v.length);
    if(empty){if(f.required)add('MASTER_INVALID',f.key,`Enter ${f.label.toLowerCase()}.`);continue;}
    const lim=(n:number,unit='')=>{if(f.min!==undefined&&n<f.min)add('MASTER_INVALID',f.key,`${f.label}: at least ${f.min}${unit}.`);if(f.max!==undefined&&n>f.max)add('MASTER_INVALID',f.key,`${f.label}: at most ${f.max}${unit}.`);};
    switch(f.type){
      case 'text':{const t=String(v);if(t.length>(f.maxLength??200))add('MASTER_INVALID',f.key,`${f.label}: at most ${f.maxLength??200} characters.`);break;}
      case 'number':{const n=Number(v);if(!Number.isFinite(n))add('MASTER_INVALID',f.key,`${f.label}: enter a number.`);else lim(n);break;}
      case 'money':{const n=Number(v);if(!Number.isSafeInteger(n)||n<0)add('MASTER_INVALID',f.key,`${f.label}: enter an amount in rupees (0 or more).`);else lim(n);break;}
      case 'percent':{const n=Number(v);if(!Number.isInteger(n)||n<(f.min??0)||n>(f.max??10000))add('MASTER_INVALID',f.key,`${f.label}: enter a percentage from ${(f.min??0)/100} to ${(f.max??10000)/100}.`);break;}
      case 'toggle':if(typeof v!=='boolean')add('MASTER_INVALID',f.key,`${f.label}: on or off.`);break;
      case 'select':case 'multiselect':{
        const vals=(f.type==='select'?[v]:Array.isArray(v)?v:[v]).map(String);
        const oldVals=new Set((Array.isArray(old?.[f.key])?old![f.key] as string[]:old?.[f.key]!==undefined?[String(old[f.key])]:[]).map(String));
        for(const x of vals){
          if(f.options){if(!f.options.some(o=>o.value===x))add('MASTER_INVALID',f.key,`${f.label}: choose from the list.`);}
          else if(f.optionsFrom&&s){if(!f.optionsFrom(s).some(o=>o.value===x)&&!oldVals.has(x))add('MASTER_INVALID',f.key,`${f.label}: choose from the list.`);}
          else if(f.master&&s){const ref=mstByCode(s,f.master,x);
            if(!ref)add('MASTER_INVALID',f.key,`${f.label}: ${x} is not in ${mstSpec(f.master)?.label??f.master}.`);
            else if(!ref.active&&!oldVals.has(x))add('MASTER_RETIRED',f.key,`${f.label}: ${ref.name} is retired — choose an active one.`);}
        }
        break;}
      case 'datedMoney':{
        const list=Array.isArray(v)?v as MstDatedAmount[]:[];
        if(!Array.isArray(v))add('MASTER_INVALID',f.key,`${f.label}: dated amounts expected.`);
        for(const x of list){if(!Number.isSafeInteger(x.amount)||x.amount<0)add('MASTER_INVALID',f.key,`${f.label}: each amount must be 0 or more.`);if(!ISO.test(x.effectiveFrom))add('MASTER_INVALID',f.key,`${f.label}: each amount needs an effective-from date.`);}
        if(new Set(list.map(x=>x.effectiveFrom)).size!==list.length)add('MASTER_INVALID',f.key,`${f.label}: one amount per effective date.`);
        if(s){const before=(old?.[f.key] as MstDatedAmount[]|undefined)??[];const key=(x:MstDatedAmount)=>x.effectiveFrom+':'+x.amount;
          const past=(l:MstDatedAmount[])=>l.filter(x=>x.effectiveFrom<s.date).map(key).sort().join('|');
          if(past(before)!==past(list))add('MASTER_INVALID',f.key,`${f.label}: amounts already in effect cannot change — add a new amount from ${s.date} or later.`);}
        break;}
    }
  }
  if(s&&spec.validate&&!out.length){const m=spec.validate(s,row,rows);if(m)add('MASTER_INVALID',undefined,m);}
  return out;
}
/** First problem of a row, or undefined. */
export const mstValidate=(spec:MasterSpec,row:MasterRow,rows:MasterRow[],s?:State,old?:MasterRow):MstProblem|undefined=>mstProblems(spec,row,rows,s,old)[0];

// ───────────────────────────── Permissions (DEC-058 / DEC-061 / DEC-MA-1: masters = Owner + Tech admin, grantable) ─────────────────────────────
export const MST_OWNER_ONLY='Owner changes this — money lists and money fields are Owner-only (DEC-058).';
/** Roles that never see money fields (DEC-061): housekeeping and maintenance (assigned scope) and roles without any money permission. */
export function mstSeesMoney(s:State,uid:string):boolean{
  const u=s.users.find(x=>x.id===uid);if(!u)return false;
  if(assignedScopeRoles.includes(u.role as Role)||u.role==='Supervisor')return false;
  return can(s,uid,['billing','payments','reports','approve','admin']);
}
/** MA-10 (DEC-061): text for a role without money visibility — every rupee amount becomes "₹ ••" (hub values, help texts, summaries). */
export const mstMaskMoney=(s:State,uid:string,text:string)=>mstSeesMoney(s,uid)?text:text.replace(/₹\s?[\d,]+(\.\d+)?/g,'₹ ••');
const ownerAllowed=(s:State,uid:string)=>{const u=s.users.find(x=>x.id===uid);return u?.role==='Owner'&&can(s,uid,'admin')&&authorize(s,uid,'PERM-ADM-003',{act:true}).decision==='allow';};
/** May this user change the list at all? Operational lists: the `masters` permission; money lists ('settings'/money): the Owner. */
export function mstCanEdit(s:State,uid:string,spec:MasterSpec):{ok:boolean;reason?:string}{
  const u=s.users.find(x=>x.id===uid);
  if(!u||!u.enabled)return {ok:false,reason:'Your account is not active.'};
  if(spec.money||spec.permission==='settings')return ownerAllowed(s,uid)?{ok:true}:{ok:false,reason:MST_OWNER_ONLY};
  return can(s,uid,'masters')?{ok:true}:{ok:false,reason:`View only — changing ${spec.label.toLowerCase()} needs the masters permission (Owner and Tech admin; the Owner may grant it in Access control, DEC-MA-1).`};
}
/** May this user change money fields of an operational list? (Owner only.) */
export const mstCanEditMoney=(s:State,uid:string)=>ownerAllowed(s,uid);
/** Fields shown to this user (money fields hidden without money visibility, DEC-061). */
export const mstVisibleFields=(s:State,uid:string,spec:MasterSpec)=>spec.fields.filter(f=>!isMoneyField(f)||mstSeesMoney(s,uid));
/** A row as this user may see it (money fields removed without money visibility). */
export function mstVisibleRow(s:State,uid:string,spec:MasterSpec,row:MasterRow):MasterRow{
  if(mstSeesMoney(s,uid))return row;
  const out={...row};for(const f of mstMoneyFields(spec))delete out[f.key];return out;
}

// ───────────────────────────── Display ─────────────────────────────
export function mstShow(f:FieldSpec,v:unknown,s?:State):string{
  if(v===undefined||v===null||v==='')return '—';
  const rs=(p:number)=>'₹'+(p/100).toLocaleString('en-IN',{maximumFractionDigits:2});
  switch(f.type){
    case 'toggle':return v?'Yes':'No';
    case 'money':return rs(Number(v));
    case 'percent':return (Number(v)/100).toLocaleString('en-IN',{maximumFractionDigits:2})+' %';
    case 'datedMoney':{const l=v as MstDatedAmount[];if(!l.length)return '—';const now=s?mstMoneyOn(l,s.date):undefined;return now!==undefined?rs(now)+(l.some(x=>s&&x.effectiveFrom>s.date)?' (dated change ahead)':''):`${rs(l[0].amount)} from ${l[0].effectiveFrom}`;}
    case 'select':return f.master&&s?mstLabel(s,f.master,String(v)):(f.options??(s&&f.optionsFrom?f.optionsFrom(s):undefined))?.find(o=>o.value===v)?.label??String(v);
    case 'multiselect':return (v as string[]).map(x=>f.master&&s?mstLabel(s,f.master,x):(f.options??(s&&f.optionsFrom?f.optionsFrom(s):undefined))?.find(o=>o.value===x)?.label??x).join(', ');
    default:return String(v);
  }
}
/** Search masters by label / singular / group (Master configuration search). */
export function mstSearch(q:string):MasterSpec[]{
  const terms=q.toLowerCase().replace(/[^a-z0-9 ]+/g,' ').split(' ').filter(Boolean);
  if(!terms.length)return [];
  return masterSpecs().filter(sp=>{const t=`${sp.label} ${sp.singular} list lists master masters ${sp.fields.map(f=>f.label).join(' ')}`.toLowerCase();return terms.every(x=>t.includes(x));});
}
/** "3 floors · 1 retired" */
export function mstSummary(s:State,specId:string):string{
  const sp=mstSpec(specId);if(!sp)return '';const all=mstRows(s,specId),ret=all.filter(r=>!r.active).length;
  return `${all.length-ret} ${all.length-ret===1?sp.singular:sp.label.toLowerCase()}${ret?` · ${ret} retired`:''}`;
}

// ───────────────────────────── Floors (M1, the reference master) ─────────────────────────────
// Room.floor stays a number (no new Room field until MA-1): room floor n ↔ floor code F<n>. Names come from the master,
// ordered by sortOrder; a floor without a master row is shown as "Floor n" (old saves, rooms on a new floor).
export const MST_FLOORS='floors';
export const mstFloorCode=(n:number)=>'F'+n;
/** Floor number of a code F<n> (undefined for other codes, e.g. a named wing until MA-1 links rooms to codes). */
export const mstFloorNumber=(code:string)=>{const m=/^F(\d{1,4})$/.exec(code);return m?Number(m[1]):undefined;};
/** Fixture seed: one row per floor number used by the rooms ("Floor 1" …), sort order 10, 20, … */
export const mstSeedFloors=(s:State):MasterRow[]=>mastersSeed(MST_FLOORS,s); // fixtures/ext/masters-seed.ts
/** Master row of a room's floor (retired rows resolve too). */
export const mstFloorOf=(s:State,room:Pick<Room,'floor'>)=>mstByCode(s,MST_FLOORS,mstFloorCode(room.floor));
/** Floor name of a room, or of a floor number ("Floor n" when the master has no row). */
export function mstFloorLabel(s:State,x:Pick<Room,'floor'>|number):string{
  const n=typeof x==='number'?x:x.floor;return mstByCode(s,MST_FLOORS,mstFloorCode(n))?.name??'Floor '+n;
}
/** Floors that have rooms, in master order (sortOrder; floors without a row after them by number) — board sections and filters. */
export function mstFloorGroups(s:State):{floor:number;code:string;label:string;retired:boolean;rooms:number}[]{
  const nums=[...new Set(s.rooms.map(r=>r.floor))];
  const rank=(n:number)=>{const r=mstByCode(s,MST_FLOORS,mstFloorCode(n));return r?[0,r.sortOrder,n]:[1,0,n];};
  return nums.sort((a,b)=>{const x=rank(a),y=rank(b);return x[0]-y[0]||x[1]-y[1]||x[2]-y[2];}).map(n=>{const r=mstByCode(s,MST_FLOORS,mstFloorCode(n));
    return {floor:n,code:mstFloorCode(n),label:r?.name??'Floor '+n,retired:!!r&&!r.active,rooms:s.rooms.filter(x=>x.floor===n).length};});
}
export const FLOOR_SPEC:MasterSpec={
  id:MST_FLOORS,label:'Floors',singular:'floor',group:'rooms',prefix:'FLR',money:false,permission:'masters',
  help:'Names and order of floors and wings on the room board, housekeeping board and filters. Room n0x is on floor F<n>.',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:20,placeholder:'F1',help:'Short code, e.g. F1, G, ANNEXE. Cannot change once rooms use it.'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Ground floor',help:'Shown on the boards, e.g. “Ground”, “1st floor”, “Annexe”.'},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000,help:'Lower comes first on the boards.'},
    {key:'active',label:'Active',type:'toggle',help:'Retire a floor instead of deleting it; retired floors still label their rooms.'},
  ],
  inUse:(s,row)=>{const n=mstFloorNumber(row.code);const rooms=n===undefined?[]:s.rooms.filter(r=>r.floor===n);
    return {count:rooms.length,where:rooms.length?[`Rooms ${rooms.map(r=>r.id).join(', ')}`]:[]};},
  seed:mstSeedFloors,
};
