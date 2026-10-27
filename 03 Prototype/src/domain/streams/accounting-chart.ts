// ACC-1 · The chart of accounts as an Owner-only master (rulebook §3.1, §3.4; posting contract 02 Planning/agents/ACC1-POSTING-CONTRACT.md §1, §2, §7).
// Exported names start with 'acc' / 'ACC' (shared mock namespace). Pure selectors and validation; commands: services/mock/handlers/accounting-chart.ts.
//
//   groups   Tally's 28 groups (ACC_GROUPS, fixed: never renamed, deleted or re-parented) + the Owner's custom sub-groups `g-<slug>`
//            (s.accountingX.chart.groups; nature and side inherited from the parent).
//   ledgers  the built-ins (ACC_LEDGERS, `system`) with the Owner's overrides (name, group within the same Tally nature, GST flag, Tally
//            name, note, active) + Owner-added ledgers (4-digit unused code, any group). Stored in s.accountingX.chart.ledgers: one full row
//            per overridden built-in or Owner-added code; a built-in edited back to its defaults drops its row.
//   retire   refused while the ledger or any of its parties `<code>/<party>` has a non-zero balance (every date up to the business date,
//            locked snapshots as locked), while a mapping key points at it (defaults, master ledgers and Mapping overrides), and always for
//            9999 Suspense and 1400 Opening-balance difference. Reactivate = ACC_LEDGER_UPSERT {active:true}.
//   history  one ACCChartChange row per changed field (kind ledger | group), newest last.
//
// "Same nature" (rulebook §3.4.1) = Tally's nature of a group: Assets / Liabilities / Income / Expenses (+ Suspense) = the legacy ACCGroup of
// the group's primary ancestor (contract §1: BS+Dr Asset, BS+Cr Liability, PL+Cr Income, PL+Dr Expense, suspense Suspense).
//
// Import-cycle safety: accounting.ts may import this file (integration of accLedger into accLiveVouchers), so NOTHING at the top level here
// may read a value imported from './accounting' — functions only.
import type {State} from '../types';
import {money} from '../money';
import {mstCanEditMoney,type MstErrorCode} from './masters';
import {ACC_GROUPS,ACC_LEDGERS,ACC_SUSPENSE,accLegacyGroup,accSlice,accMapping,accVouchersUpTo,
  type ACCGroup,type ACCGroupDef,type ACCLedger,type ACCChartChange} from './accounting';

export type ACCChartSlice={ledgers:ACCLedger[];groups:ACCGroupDef[];history:ACCChartChange[]};
/** Ledger codes that never retire (contract §2 / §5: Suspense and the opening-balance difference are structural). */
export const ACC_CHART_FIXED=['9999','1400'];
export const ACC_LEDGER_CODE=/^\d{4}$/;
export const ACC_GROUP_ID=/^g-[a-z0-9]+(-[a-z0-9]+)*$/;
export const ACC_CHART_OWNER_ONLY='only the Owner changes the chart of accounts (money master, DEC-058; PERM-ACC-007).';

export const accChartSlice=(s:State):ACCChartSlice=>{const c=accSlice(s).chart;return {ledgers:c?.ledgers??[],groups:c?.groups??[],history:c?.history??[]};};
/** May this user edit the chart (ledgers, custom sub-groups)? Contract §7: Owner (admin) only. */
export const accCanEditChart=(s:State,uid:string)=>mstCanEditMoney(s,uid);

// ── Groups ──
/** Tally's 28 groups, then the Owner's custom sub-groups (custom: true). */
export function accGroups(s:State):ACCGroupDef[]{return [...ACC_GROUPS,...accChartSlice(s).groups.map(g=>({...g,custom:true}))];}
export const accGroupOf=(s:State,id:string)=>accGroups(s).find(g=>g.id===id);
/** The primary (top-level) group above `id` (itself when primary); undefined for an unknown id or a broken parent chain. */
export function accGroupRoot(s:State,id:string):ACCGroupDef|undefined{
  const all=accGroups(s);let g=all.find(x=>x.id===id);
  for(let i=0;g&&g.parent&&i<32;i++)g=all.find(x=>x.id===g!.parent);
  return g&&!g.parent?g:undefined;
}
/** Legacy group (Tally nature) of a group: from its primary ancestor (contract §1). Unknown → Suspense. */
export function accGroupLegacy(s:State,id:string):ACCGroup{const r=accGroupRoot(s,id);return r?accLegacyGroup(r):'Suspense';}
/** Group names from the primary group down to `id` (e.g. Current Assets › Bank Accounts). */
export function accGroupPath(s:State,id:string):string[]{
  const all=accGroups(s),out:string[]=[];let g=all.find(x=>x.id===id);
  for(let i=0;g&&i<32;i++){out.unshift(g.name);g=g.parent?all.find(x=>x.id===g!.parent):undefined;}
  return out;
}
/** True when `id` is `anc` or below it. */
function under(s:State,id:string,anc:string){const all=accGroups(s);let g=all.find(x=>x.id===id);
  for(let i=0;g&&i<32;i++){if(g.id===anc)return true;g=g.parent?all.find(x=>x.id===g!.parent):undefined;}return false;}
/** `g-<slug>` id of a custom sub-group name. */
export const accGroupSlug=(name:string)=>'g-'+(name.toLowerCase().normalize('NFKD').replace(/[^a-z0-9]+/g,'-').replace(/^-+|-+$/g,'').slice(0,40).replace(/-+$/,'')||'group');

// ── Ledgers ──
/** The effective chart: built-ins with the Owner's overrides, then the Owner-added ledgers; code order. */
export function accChart(s:State):ACCLedger[]{
  const stored=new Map(accChartSlice(s).ledgers.map(l=>[l.code,l]));
  const out=ACC_LEDGERS.map(b=>{const o=stored.get(b.code);return o?mergeBuiltin(b,o):b;});
  for(const l of stored.values())if(!ACC_LEDGERS.some(b=>b.code===l.code))out.push({...l,system:false,control:undefined});
  return out.map(l=>{const x={...l};if(!x.control)delete x.control;return x;}).sort((a,b)=>a.code<b.code?-1:a.code>b.code?1:0);
}
/** A built-in with an override row: code, system and control never change. */
function mergeBuiltin(b:ACCLedger,o:ACCLedger):ACCLedger{
  const r:ACCLedger={...b,name:o.name,group:o.group,gst:o.gst??b.gst,active:o.active};
  if(o.tallyName)r.tallyName=o.tallyName;else delete r.tallyName;
  if(o.note!==undefined){if(o.note)r.note=o.note;else delete r.note;}
  return r;
}
export const accBuiltinLedger=(code:string)=>ACC_LEDGERS.find(l=>l.code===code);
/** A ledger of the effective chart; a party ledger id `<control>/<party>` resolves to its control (contract §3). */
export function accLedger(s:State,code:string):ACCLedger|undefined{
  const all=accChart(s),l=all.find(x=>x.code===code);if(l)return l;
  const i=code.indexOf('/');if(i<1)return undefined;const c=all.find(x=>x.code===code.slice(0,i));return c?.control?c:undefined;
}
export const accLedgerNameOf=(s:State,code:string)=>accLedger(s,code)?.name??code;
/** Name used in the Tally XML masters (contract §2: default = name). */
export const accLedgerTallyName=(s:State,code:string)=>{const l=accLedger(s,code);return l?(l.tallyName||l.name):code;};
/** Normal balance (Dr / Cr) of a ledger = its group's nature; undefined for an unknown code. */
export function accLedgerNature(s:State,code:string):'Dr'|'Cr'|undefined{const l=accLedger(s,code);return l?accGroupOf(s,l.group)?.nature:undefined;}
export function accLedgerLegacyGroup(s:State,code:string):ACCGroup{const l=accLedger(s,code);return l?accGroupLegacy(s,l.group):'Suspense';}
/** Mapping keys (effective: defaults, master ledgers, Mapping overrides) that point at `code`. */
export const accLedgerMappedBy=(s:State,code:string)=>Object.entries(accMapping(s)).filter(([,v])=>v===code).map(([k])=>k).sort();
/** Balance (Dr − Cr, paise) of a ledger and of each of its parties over every voucher up to the business date (locked snapshots as locked). */
export function accLedgerBalances(s:State,code:string):{balance:number;own:number;parties:Record<string,number>}{
  let own=0;const parties:Record<string,number>={};const pre=code+'/';
  for(const v of accVouchersUpTo(s,s.date))for(const e of v.entries){
    if(e.account===code)own+=e.debit-e.credit;
    else if(e.account.startsWith(pre)){const p=e.account.slice(pre.length);parties[p]=(parties[p]??0)+e.debit-e.credit;}
  }
  return {balance:own+Object.values(parties).reduce((a,b)=>a+b,0),own,parties};
}
const drcr=(p:number)=>`${money(Math.abs(p))} ${p>0?'Dr':'Cr'}`;
/** Why `code` cannot be retired now ('' = it can). */
export function accLedgerRetireBlocker(s:State,code:string):string{
  const l=accChart(s).find(x=>x.code===code);
  if(!l)return 'This ledger was not found in the chart of accounts.';
  if(!l.active)return `${l.code} ${l.name} is already retired.`;
  if(ACC_CHART_FIXED.includes(code))return `${l.code} ${l.name} is structural and never retires.`;
  const keys=accLedgerMappedBy(s,code);
  if(keys.length)return `${l.code} ${l.name} is the ledger of ${keys.length} mapping key${keys.length===1?'':'s'} (${keys.slice(0,3).join(', ')}${keys.length>3?', …':''}) — map ${keys.length===1?'it':'them'} to another ledger first.`;
  const b=accLedgerBalances(s,code);
  if(b.own)return `${l.code} ${l.name} has a balance of ${drcr(b.own)} — retire it only once the balance is zero.`;
  const p=Object.entries(b.parties).filter(([,v])=>v);
  if(p.length)return `${p.length} part${p.length===1?'y':'ies'} under ${l.code} ${l.name} still ha${p.length===1?'s':'ve'} a balance (${p.slice(0,3).map(([k,v])=>`${k} ${drcr(v)}`).join(', ')}) — retire it only once every party is zero.`;
  return '';
}

// ── Validation (pure; the handler applies the plan) ──
export type ACCChartRefusal={reason:MstErrorCode;message:string;field?:string};
export type ACCLedgerInput={create?:unknown;code?:unknown;name?:unknown;group?:unknown;gst?:unknown;tallyName?:unknown;note?:unknown;active?:unknown};
export type ACCChartPlan<T>={ok:true;row:T;created:boolean;changes:[string,string,string][]}|{ok:false;refusal:ACCChartRefusal};
const bad=(reason:MstErrorCode,message:string,field?:string):{ok:false;refusal:ACCChartRefusal}=>({ok:false,refusal:{reason,message,field}});
const txt=(v:unknown)=>v===undefined||v===null?'':String(v).trim();
const same=(a:string,b:string)=>a.toLowerCase()===b.toLowerCase();
const GST=['none','input','output'];

/** Plan of ACC_LEDGER_UPSERT: the new stored row and the changed fields, or the refusal (MASTER_* reason). Permission is checked by the handler. */
export function accLedgerUpsertPlan(s:State,input:ACCLedgerInput):ACCChartPlan<ACCLedger>{
  const code=txt(input.code);
  if(!ACC_LEDGER_CODE.test(code))return bad('MASTER_INVALID','A ledger code is 4 digits, e.g. 5130.','code');
  const chart=accChart(s),old=chart.find(l=>l.code===code),builtin=accBuiltinLedger(code);
  const name=input.name===undefined&&old?old.name:txt(input.name);
  if(!name||name.length>60)return bad('MASTER_INVALID','Enter the ledger name (1–60 characters).','name');
  const group=input.group===undefined&&old?old.group:txt(input.group);
  const g=accGroupOf(s,group);
  if(!g)return bad('MASTER_INVALID','Choose the group from the chart (one of Tally’s groups or a sub-group).','group');
  const gst=input.gst===undefined?(old?.gst??'none'):txt(input.gst);
  if(!GST.includes(gst))return bad('MASTER_INVALID','GST flag is none, input or output.','gst');
  const tallyName=input.tallyName===undefined?(old?.tallyName??''):txt(input.tallyName);
  if(tallyName.length>60)return bad('MASTER_INVALID','The Tally name is at most 60 characters.','tallyName');
  const note=input.note===undefined?(old?.note??''):txt(input.note);
  if(note.length>200)return bad('MASTER_INVALID','The note is at most 200 characters.','note');
  let active=old?old.active:true;
  if(input.active!==undefined){const a=input.active===true||input.active==='true';
    if(!a&&(!old||old.active))return bad('MASTER_INVALID','Retire a ledger with Retire (it checks balances and mappings).','active');
    active=a;}
  // create: true = the Owner adds a ledger: the code must be unused (incl. retired ledgers: reactivate one instead).
  if(input.create===true&&old)return bad('MASTER_CODE_TAKEN',`Code ${code} is already used by ${old.name}${old.active?'':' (retired: reactivate it instead)'}.`,'code');
  if(!old&&input.create!==true)return bad('MASTER_INVALID',`No ledger ${code} in the chart — add it as a new ledger.`,'code');
  // Same Tally nature for a built-in always, for an Owner ledger once it is in use (mapped or with a balance).
  if(old&&old.group!==group){
    const from=accGroupLegacy(s,old.group),to=accGroupLegacy(s,group);
    if(from!==to){
      if(old.system)return bad('MASTER_INVALID',`${code} ${old.name} is a built-in ${from} ledger: move it only to another ${from} group (Tally nature).`,'group');
      if(accLedgerMappedBy(s,code).length||accLedgerBalances(s,code).balance)return bad('MASTER_INVALID',`${code} ${old.name} is in use as a ${from} ledger: move it only to another ${from} group.`,'group');
    }
  }
  // Ledger names and Tally names are unique (Tally needs unique ledger names).
  const tName=tallyName||name;
  for(const l of chart){if(l.code===code)continue;
    if(same(l.name,name))return bad('MASTER_CODE_TAKEN',`${l.code} is already called “${l.name}”.`,'name');
    if(same(l.tallyName||l.name,tName))return bad('MASTER_CODE_TAKEN',`${l.code} already uses the Tally name “${l.tallyName||l.name}”.`,tallyName?'tallyName':'name');}
  const row:ACCLedger={code,name,group,system:!!builtin,gst:gst as ACCLedger['gst'],active,...(tallyName?{tallyName}:{}),...(note||builtin?{note}:{})};
  if(builtin?.control)row.control=true;
  const changes:[string,string,string][]=[];
  if(!old)changes.push(['created','',`${code} ${name} · ${accGroupPath(s,group).join(' › ')}`]);
  else{const f=(k:string,a:string,b:string)=>{if(a!==b)changes.push([k,a,b]);};
    f('name',old.name,name);f('group',old.group,group);f('gst',old.gst??'none',gst);f('tallyName',old.tallyName??'',tallyName);f('note',old.note??'',note);
    f('active',old.active?'active':'retired',active?'active':'retired');}
  if(old&&!changes.length)return bad('MASTER_INVALID','Nothing changed.');
  return {ok:true,row,created:!old,changes};
}

/** Plan of ACC_GROUP_UPSERT {id?, name, parent}: a new custom sub-group (no id) or a rename / re-parent of a custom one. */
export function accGroupUpsertPlan(s:State,input:{id?:unknown;name?:unknown;parent?:unknown}):ACCChartPlan<ACCGroupDef>{
  const id=txt(input.id),all=accGroups(s),old=id?all.find(g=>g.id===id):undefined;
  if(id&&!old)return bad('MASTER_INVALID','This group was not found.','id');
  if(old&&!old.custom)return bad('MASTER_INVALID',`${old.name} is one of Tally’s 28 groups: it is fixed. Add a sub-group under it instead.`,'id');
  const name=input.name===undefined&&old?old.name:txt(input.name);
  if(!name||name.length>60)return bad('MASTER_INVALID','Enter the group name (1–60 characters).','name');
  const parent=input.parent===undefined&&old?old.parent??'':txt(input.parent);
  const p=all.find(g=>g.id===parent);
  if(!p)return bad('MASTER_INVALID','Choose the parent group (a custom group is always a sub-group).','parent');
  const gid=old?old.id:accGroupSlug(name);
  if(!old&&all.some(g=>g.id===gid))return bad('MASTER_CODE_TAKEN',`A group with the id ${gid} already exists.`,'name');
  if(all.some(g=>g.id!==gid&&same(g.name,name)))return bad('MASTER_CODE_TAKEN',`A group called “${name}” already exists.`,'name');
  if(old&&old.parent!==parent){
    if(under(s,parent,old.id))return bad('MASTER_INVALID','A group cannot sit under itself or one of its own sub-groups.','parent');
    const from=accGroupLegacy(s,old.id),to=accGroupLegacy(s,parent);
    if(from!==to)return bad('MASTER_INVALID',`${old.name} is a ${from} group: move it only under another ${from} group (Tally nature).`,'parent');
  }
  const row:ACCGroupDef={id:gid,name,parent,nature:p.nature,side:p.side,custom:true};
  const changes:[string,string,string][]=[];
  if(!old)changes.push(['created','',`${name} · under ${accGroupPath(s,parent).join(' › ')}`]);
  else{if(old.name!==name)changes.push(['name',old.name,name]);if(old.parent!==parent)changes.push(['parent',old.parent??'',parent]);}
  if(old&&!changes.length)return bad('MASTER_INVALID','Nothing changed.');
  return {ok:true,row,created:!old,changes};
}

// ── Problems and rows for the screens ──
export type ACCChartProblem={id:string;severity:'error'|'warning';code?:string;title:string;detail:string};
/** Chart health (shown on the Chart of accounts tab): broken groups, duplicate names, mappings to unknown or retired ledgers. */
export function accChartProblems(s:State):ACCChartProblem[]{
  const out:ACCChartProblem[]=[],chart=accChart(s);
  for(const g of accChartSlice(s).groups){
    if(!accGroupRoot(s,g.id))out.push({id:'group:'+g.id,severity:'error',title:`Group ${g.name} has no valid parent`,detail:`Its parent “${g.parent??''}” is missing or loops back. Choose another parent.`});
    if(!ACC_GROUP_ID.test(g.id))out.push({id:'group-id:'+g.id,severity:'warning',title:`Group id ${g.id} is not g-<slug>`,detail:'Custom sub-group ids start with g-.'});
  }
  for(const l of chart){
    if(!accGroupOf(s,l.group))out.push({id:'ledger-group:'+l.code,severity:'error',code:l.code,title:`${l.code} ${l.name} is in an unknown group`,detail:`Group “${l.group}” does not exist. Move the ledger to a group of the chart.`});
    const b=accBuiltinLedger(l.code);
    if(b&&accGroupOf(s,l.group)&&accGroupLegacy(s,l.group)!==accGroupLegacy(s,b.group))out.push({id:'ledger-nature:'+l.code,severity:'error',code:l.code,title:`${l.code} ${l.name} changed nature`,detail:`A built-in ${accGroupLegacy(s,b.group)} ledger sits in a ${accGroupLegacy(s,l.group)} group.`});
  }
  const seen=new Map<string,string>();
  for(const l of chart){const k=(l.tallyName||l.name).toLowerCase();const o=seen.get(k);
    if(o)out.push({id:'dup:'+l.code,severity:'error',code:l.code,title:`Duplicate ledger name “${l.tallyName||l.name}”`,detail:`${o} and ${l.code} carry the same name; Tally needs unique ledger names.`});else seen.set(k,l.code);}
  for(const [k,v] of Object.entries(accMapping(s))){
    if(!v||v===ACC_SUSPENSE)continue;const l=chart.find(x=>x.code===v);
    if(!l)out.push({id:'map:'+k,severity:'error',code:v,title:`Mapping ${k} points at ${v}, which is not in the chart`,detail:'Map it to a ledger of the chart (Mapping tab).'});
    else if(!l.active)out.push({id:'map:'+k,severity:'error',code:v,title:`Mapping ${k} points at retired ledger ${v} ${l.name}`,detail:'Map it to an active ledger or reactivate the ledger.'});
  }
  return out;
}
export type ACCChartRow=ACCLedger&{nature?:'Dr'|'Cr';legacy:ACCGroup;groupName:string;path:string[];overridden:boolean;mappedBy:string[]};
/** One row per ledger for the Chart of accounts tab (no balances: those come from the statements / trial balance). */
export function accChartRows(s:State):ACCChartRow[]{
  const stored=new Set(accChartSlice(s).ledgers.map(l=>l.code)),map=accMapping(s);
  return accChart(s).map(l=>({...l,nature:accGroupOf(s,l.group)?.nature,legacy:accGroupLegacy(s,l.group),groupName:accGroupOf(s,l.group)?.name??l.group,
    path:accGroupPath(s,l.group),overridden:l.system&&stored.has(l.code),mappedBy:Object.entries(map).filter(([,v])=>v===l.code).map(([k])=>k).sort()}));
}
/** Chart history, newest first. */
export const accChartHistory=(s:State)=>[...accChartSlice(s).history].reverse();
