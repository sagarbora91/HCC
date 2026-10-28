// MA-3 · Money masters (plan Part 2 §8 M15–M18): payment methods, bank accounts and tills, expense categories, expense vendors.
// Pure LEAF module (no runtime imports): the selectors here are read by core money code (drawer, closing, POS, folio, accounting),
// so it must not import the masters registry (that would close an import cycle). The specs (fields, in-use, validation) are
// registered in money-master-specs.ts; the rows live in the masters slice s.mastersX.rows[<id>] like every master (MA-0), and
// until the first saved change the seeds below are the rows — they are exactly the old hard-coded lists, so no figure moves.
//
// Records keep storing the method / category / vendor NAME they always stored ('Cash', 'Supplies', 'Morning Supplies'); a name
// resolves to its row by name, by code, or by an earlier name of the row (rename history), so old records keep resolving.
// Drawer effect = the method "settles to" a till (bank account row of kind 'till'); seed: only Cash → TILL (as before).
// DEC-MA-6: expense vendors are their own list; maintenance vendors (s.maintenanceX) are untouched.
import type {State,Expense} from '../types';
import type {MasterRow,MSTHistory} from './masters';
import {SEED_TILL,SEED_BANK,SEED_ACCOUNTS,SEED_METHODS,SEED_CATEGORIES,seedVendorCode,seedVendorType,seedVendors} from '../../fixtures/ext/masters-seed';

export const MNY_PM='paymentMethods';
export const MNY_BA='bankAccounts';
export const MNY_EC='expenseCategories';
export const MNY_EV='expenseVendors';
export const MNY_MASTERS=[MNY_PM,MNY_BA,MNY_EC,MNY_EV] as const;

export type MnyKind='cash'|'upi'|'card'|'bank'|'wallet'|'credit';
export type MnyPlace='frontdesk'|'pos'|'expenses';
export const MNY_KINDS:{value:MnyKind;label:string}[]=[{value:'cash',label:'Cash'},{value:'upi',label:'UPI'},{value:'card',label:'Card'},
  {value:'bank',label:'Bank transfer'},{value:'wallet',label:'Wallet'},{value:'credit',label:'Credit (pay later / city ledger)'}];
export const MNY_PLACES:{value:MnyPlace;label:string}[]=[{value:'frontdesk',label:'Front desk (folio)'},{value:'pos',label:'Restaurant / POS'},{value:'expenses',label:'Expenses'}];
export const MNY_VENDOR_TYPES=[{value:'supplier',label:'Supplier'},{value:'utility',label:'Utility'},{value:'laundry',label:'Laundry'},{value:'other',label:'Other'}];

/** M15 · one payment method (settlesTo = bankAccounts code; none = credit: vendor payable / city ledger). */
export type MnyMethod=MasterRow&{kind:MnyKind;settlesTo?:string;
  /** A transaction reference is required when a guest pays with it (folio, POS). Expenses use the bill number instead. */needsReference:boolean;
  usableAt:MnyPlace[];/** Basis points; shown as a hint, never posted automatically. */surchargePct?:number;/** Ledger code (accounting default). */ledger?:string};
/** M16 · bank account or till. Only the last four digits of an account number are kept (masked everywhere). */
export type MnyAccount=MasterRow&{kind:'till'|'bank';bank?:string;accountLast4?:string;ifsc?:string;/** Paise, setup only. */openingBalance?:number;
  /** Method kinds this account is the default "settles to" for (new methods). */defaultFor?:string[]};
/** M17 · expense category (ledger = accounting default for "expense:<name>"; approvalLimit = Manager limit override, paise). */
export type MnyCategory=MasterRow&{ledger?:string;needsBill:boolean;approvalLimit?:number;
  /** ACC-2 (contract §3): reverse charge (DEC-145) and its rate; capital = fixed-asset purchases (ledger must be a fixed-assets ledger); itcBlocked = s.17(5) list, the ITC switch stays off (DEC-143). */
  rcm?:boolean;rcmBps?:number;capital?:boolean;itcBlocked?:boolean};
/** M18 · expense vendor (DEC-MA-6, separate from maintenance vendors). */
export type MnyVendor=MasterRow&{type:string;gstin?:string;pan?:string;tds:boolean;contact?:string;terms?:string;
  /** ACC-2 (contract §3, §5): two-letter GST state code (MH = intra-state) and the TDS rate-table row when `tds` is on. */ state?:string;tdsSection?:string};

// ───────────────────────────── Seeds = the old constants (folio FOLIO_TENDERS, POS_TENDERS, closing EXPENSE_METHODS / TENDERS /
// EXPENSE_CATEGORIES, accounting ACC_DEFAULT_MAP) + "Company credit" (receipts of authorised company credit) ─────────────────────────────
// The rows are in fixtures/ext/masters-seed.ts (one place for every master's seed); the names below are kept for the existing imports.
export const MNY_TILL=SEED_TILL;
export const MNY_BANK=SEED_BANK;
export const MNY_SEED_ACCOUNTS:MnyAccount[]=SEED_ACCOUNTS;
export const MNY_SEED_METHODS:MnyMethod[]=SEED_METHODS;
export const MNY_SEED_CATEGORIES:MnyCategory[]=SEED_CATEGORIES;
/** Code of a vendor seeded from free text: V-<WORDS> (capitals, digits, “-”; ≤ 20), unique among `taken`. */
export const mnyVendorCode=seedVendorCode;
/** Vendor type guessed from a free-text name (seed only; the Owner corrects it in the master). */
export const mnyVendorTypeOf=seedVendorType;
/** Seed of the vendor list: every distinct free-text vendor on the expenses (name order), so every historical expense resolves. */
export const mnySeedVendors=(s:Pick<State,'expenses'>):MnyVendor[]=>seedVendors(s);

// ───────────────────────────── Rows (stored list, else the seed) ─────────────────────────────
const stored=(s:State,id:string)=>{const r=s.mastersX?.rows?.[id];return Array.isArray(r)?r:undefined;};
const bySort=(a:MasterRow,b:MasterRow)=>a.sortOrder-b.sortOrder||(a.code<b.code?-1:a.code>b.code?1:0);
export const mnyMethods=(s:State)=>((stored(s,MNY_PM)??MNY_SEED_METHODS) as MnyMethod[]).slice().sort(bySort);
export const mnyAccounts=(s:State)=>((stored(s,MNY_BA)??MNY_SEED_ACCOUNTS) as MnyAccount[]).slice().sort(bySort);
export const mnyCategories=(s:State)=>((stored(s,MNY_EC)??MNY_SEED_CATEGORIES) as MnyCategory[]).slice().sort(bySort);
export const mnyVendors=(s:State)=>((stored(s,MNY_EV)??mnySeedVendors(s)) as MnyVendor[]).slice().sort(bySort);

/** Earlier names of each row (field "name" in the master history): old → row id. */
function renames(h:MSTHistory[]|undefined,master:string):Map<string,string>{
  const m=new Map<string,string>();for(const x of h??[])if(x.master===master&&x.field==='name'&&x.old&&x.old!=='—')m.set(x.old,x.rowId);return m;
}
type Index<T extends MasterRow>={rows:T[];byName:Map<string,T>;get:(v:string)=>T|undefined};
function index<T extends MasterRow>(rows:T[],h:MSTHistory[]|undefined,master:string):Index<T>{
  const byName=new Map<string,T>(),byCode=new Map<string,T>(),byLower=new Map<string,T>();
  for(const r of rows){byName.set(r.name,r);byCode.set(r.code,r);if(!byLower.has(r.name.toLowerCase()))byLower.set(r.name.toLowerCase(),r);}
  const old=renames(h,master);
  const get=(v:string)=>{if(v===undefined||v===null)return undefined;const t=String(v);
    return byName.get(t)??byCode.get(t.toUpperCase())??(old.has(t)?rows.find(r=>r.id===old.get(t)):undefined)??byLower.get(t.trim().toLowerCase());};
  return {rows,byName,get};
}
// One cached index per list, rebuilt only when its rows (or the history) change: drawer() and the closing figures call these per record.
type MethodIndex=Index<MnyMethod>&{drawer:Set<string>;credit:Set<string>;names:Record<MnyPlace,string[]>;accounts:Index<MnyAccount>};
let pmCache:{pm:unknown;ba:unknown;h:unknown;idx:MethodIndex}|undefined;
export function mnyMethodIndex(s:State):MethodIndex{
  const pm=stored(s,MNY_PM),ba=stored(s,MNY_BA),h=s.mastersX?.history;
  if(pmCache&&pmCache.pm===pm&&pmCache.ba===ba&&pmCache.h===h)return pmCache.idx;
  const accounts=index(mnyAccounts(s),h,MNY_BA),base=index(mnyMethods(s),h,MNY_PM);
  const drawer=new Set<string>(),credit=new Set<string>();
  const keys=(r:MnyMethod)=>[r.name,r.code,...[...renames(h,MNY_PM)].filter(([,id])=>id===r.id).map(([o])=>o)];
  for(const r of base.rows){
    if(r.settlesTo&&accounts.get(r.settlesTo)?.kind==='till')keys(r).forEach(k=>drawer.add(k));
    if(r.kind==='credit')keys(r).forEach(k=>credit.add(k));
  }
  const names={frontdesk:[],pos:[],expenses:[]} as Record<MnyPlace,string[]>;
  for(const r of base.rows)if(r.active)for(const p of r.usableAt??[])names[p]?.push(r.name);
  const idx={...base,drawer,credit,names,accounts};
  pmCache={pm,ba,h,idx};return idx;
}
/** Row of a stored method value (name, code or an earlier name; retired rows resolve too). */
export const mnyMethod=(s:State,v:string)=>mnyMethodIndex(s).get(v);
/** True when the method settles to a till: the money goes through the front-desk drawer (shared till, DEC-052). Seed: Cash only. */
export const mnyIsDrawer=(s:State,method:string)=>mnyMethodIndex(s).drawer.has(method);
/** True for a credit method (vendor credit, company credit): no money moves now. */
export const mnyIsCredit=(s:State,method:string)=>mnyMethodIndex(s).credit.has(method);
/** Active method names offered at a place, in list order (seed: front desk Cash/UPI/Card/Bank transfer, POS Cash/UPI/Card,
 *  expenses Cash/UPI/Bank transfer/Vendor credit — the three old arrays). */
export const mnyNames=(s:State,place:MnyPlace)=>mnyMethodIndex(s).names[place];
/** Active method usable at a place for a value (name or code); else undefined. */
export function mnyMethodFor(s:State,v:unknown,place:MnyPlace):MnyMethod|undefined{
  const r=typeof v==='string'?mnyMethod(s,v):undefined;return r&&r.active&&(r.usableAt??[]).includes(place)?r:undefined;
}
/** Short label for phone chips / expense funding (old labels kept: Bank transfer → Bank; Cash → Drawer cash for expenses). */
export function mnyShortLabel(s:State,name:string,place?:MnyPlace):string{
  const r=mnyMethod(s,name);if(!r)return name;
  if(place==='expenses'&&mnyIsDrawer(s,name))return 'Drawer cash';
  if(r.kind==='bank'&&r.name==='Bank transfer')return 'Bank';
  return r.name;
}
/** "Settles to" account of a method value (undefined = credit / not settled through an account). */
export function mnySettlesTo(s:State,method:string):MnyAccount|undefined{
  const idx=mnyMethodIndex(s),r=idx.get(method);return r?.settlesTo?idx.accounts.get(r.settlesTo):undefined;
}

// ── Categories and vendors ──
let ecCache:{ec:unknown;h:unknown;idx:Index<MnyCategory>}|undefined;
export function mnyCategoryIndex(s:State):Index<MnyCategory>{
  const ec=stored(s,MNY_EC),h=s.mastersX?.history;if(ecCache&&ecCache.ec===ec&&ecCache.h===h)return ecCache.idx;
  const idx=index(mnyCategories(s),h,MNY_EC);ecCache={ec,h,idx};return idx;
}
export const mnyCategory=(s:State,v:string)=>mnyCategoryIndex(s).get(v);
/** Active category names (pick lists; seed = the old EXPENSE_CATEGORIES). `current` keeps a retired value on an old record. */
export function mnyCategoryNames(s:State,current?:string):string[]{
  const rows=mnyCategoryIndex(s).rows;const cur=current?mnyCategory(s,current):undefined;
  return rows.filter(r=>r.active||r===cur).map(r=>r.name);
}
const evCache=new WeakMap<object,{ev:unknown;h:unknown;idx:Index<MnyVendor>}>();
export function mnyVendorIndex(s:State):Index<MnyVendor>{
  const ev=stored(s,MNY_EV),h=s.mastersX?.history,key=(ev??s.expenses) as unknown as object;
  const c=evCache.get(key);if(c&&c.ev===ev&&c.h===h)return c.idx;
  const idx=index(mnyVendors(s),h,MNY_EV);evCache.set(key,{ev,h,idx});return idx;
}
export const mnyVendor=(s:State,v:string)=>mnyVendorIndex(s).get(v);
export const mnyVendorNames=(s:State)=>mnyVendorIndex(s).rows.filter(r=>r.active).map(r=>r.name);
/** Manager approval limit of an expense (category override, else the hotel limit). */
export const mnyApprovalLimit=(s:State,category:string,hotelLimit:number)=>{const c=mnyCategory(s,category);return c?.approvalLimit!==undefined&&c.approvalLimit!==null?Number(c.approvalLimit):hotelLimit;};

// ── Accounting defaults (ACC_DEFAULT_MAP keys; a mapping set in Accounting still wins) ──
/** "method:<name>" / "funding:<name>" from each method's ledger, "expense:<name>" from each category's ledger. Seed = the old defaults. */
export function mnyLedgerDefaults(s:State):Record<string,string>{
  const out:Record<string,string>={};
  for(const r of mnyMethods(s)){if(!r.ledger)continue;const at=r.usableAt??[];
    if(at.includes('frontdesk')||at.includes('pos'))out['method:'+r.name]=r.ledger;if(at.includes('expenses'))out['funding:'+r.name]=r.ledger;}
  for(const r of mnyCategories(s))if(r.ledger)out['expense:'+r.name]=r.ledger;
  return out;
}

// ── In use (counts per stored value, one pass, cached per record arrays) ──
type Counts={receipts:Map<string,number>;pos:Map<string,number>;expMethod:Map<string,number>;expCategory:Map<string,number>;expVendor:Map<string,number>};
let countCache:{b:unknown;p:unknown;e:unknown;c:Counts}|undefined;
export function mnyCounts(s:State):Counts{
  const b=s.bookings,p=s.posX?.receipts,e=s.expenses;
  if(countCache&&countCache.b===b&&countCache.p===p&&countCache.e===e)return countCache.c;
  const inc=(m:Map<string,number>,k:string)=>m.set(k,(m.get(k)??0)+1);
  const c:Counts={receipts:new Map(),pos:new Map(),expMethod:new Map(),expCategory:new Map(),expVendor:new Map()};
  for(const x of b)for(const r of x.receipts)inc(c.receipts,r.method);
  for(const r of p??[])inc(c.pos,r.method);
  for(const x of e){inc(c.expMethod,x.method);inc(c.expCategory,x.category);inc(c.expVendor,String(x.vendor??'').trim());}
  countCache={b,p,e,c};return c;
}
/** Stored values that resolve to a row (its name, code, earlier names). */
function valuesOf<T extends MasterRow>(idx:Index<T>,row:MasterRow,m:Map<string,number>):number{
  let n=0;for(const [v,k] of m)if(idx.get(v)?.id===row.id)n+=k;return n;
}
export function mnyMethodUsage(s:State,row:MasterRow):{count:number;where:string[]}{
  const c=mnyCounts(s),idx=mnyMethodIndex(s);
  const parts:[string,number][]=[['Folio receipts',valuesOf(idx,row,c.receipts)],['Outlet (POS) receipts',valuesOf(idx,row,c.pos)],['Expenses',valuesOf(idx,row,c.expMethod)]];
  return {count:parts.reduce((a,[,n])=>a+n,0),where:parts.filter(([,n])=>n).map(([w,n])=>`${w} ${n}`)};
}
export function mnyAccountUsage(s:State,row:MasterRow):{count:number;where:string[]}{
  const ms=mnyMethods(s).filter(m=>m.settlesTo===row.code);
  return {count:ms.length,where:ms.length?[`Payment methods ${ms.map(m=>m.name).join(', ')}`]:[]};
}
export function mnyCategoryUsage(s:State,row:MasterRow):{count:number;where:string[]}{
  const n=valuesOf(mnyCategoryIndex(s),row,mnyCounts(s).expCategory);return {count:n,where:n?[`Expenses ${n}`]:[]};
}
export function mnyVendorUsage(s:State,row:MasterRow):{count:number;where:string[]}{
  const n=valuesOf(mnyVendorIndex(s),row,mnyCounts(s).expVendor);return {count:n,where:n?[`Expenses ${n}`]:[]};
}
/** Records that pass through a method row (folio receipts + POS receipts + expenses) — used to lock its kind / settles-to. */
export const mnyMethodRecords=(s:State,row:MasterRow)=>mnyMethodUsage(s,row).count;

// ── "Settles to" view (cash book and bank view, plan §8 M15/M16) ──
export type MnySettleLine={method:string;in:number;out:number;count:number};
export type MnySettleGroup={account:string;label:string;kind:'till'|'bank'|'none';in:number;out:number;net:number;lines:MnySettleLine[]};
/** Money in and out by "settles to" account for an inclusive business-date range: folio receipts (payments, deposits in; refunds,
 *  deposit returns out), outlet bills (sales in, voids out) and posted expenses (out; reversals back in). Credit methods (vendor /
 *  company credit) are listed under "Not settled through an account". Same records as the cash book and reports. */
export function mnySettlement(s:State,from:string,to:string):MnySettleGroup[]{
  const idx=mnyMethodIndex(s),groups=new Map<string,MnySettleGroup>();
  const inR=(d:string)=>d>=from&&d<=to;
  const add=(method:string,amtIn:number,amtOut:number)=>{
    const acc=mnySettlesTo(s,method),key=acc?.code??'—';
    let g=groups.get(key);if(!g){g={account:key,label:acc?.name??'Not settled through an account (credit)',kind:acc?.kind??'none',in:0,out:0,net:0,lines:[]};groups.set(key,g);}
    const name=idx.get(method)?.name??method;let l=g.lines.find(x=>x.method===name);if(!l){l={method:name,in:0,out:0,count:0};g.lines.push(l);}
    l.in+=amtIn;l.out+=amtOut;l.count++;g.in+=amtIn;g.out+=amtOut;g.net=g.in-g.out;
  };
  for(const b of s.bookings)for(const r of b.receipts){if(!inR(r.date))continue;
    if(r.kind==='payment'||r.kind==='deposit'||r.kind==='company-credit')add(r.method,r.amount,0);else add(r.method,0,r.amount);}
  for(const r of s.posX?.receipts??[])if(inR(r.date)){if(r.amount>=0)add(r.method,r.amount,0);else add(r.method,0,-r.amount);}
  for(const e of s.expenses as Expense[])if(inR(e.date)&&(e.status==='Posted'||e.status==='Reversed')){if(e.amount>=0)add(e.method,0,e.amount);else add(e.method,-e.amount,0);}
  const order=(g:MnySettleGroup)=>g.kind==='till'?0:g.kind==='bank'?1:2;
  const accOrder=(g:MnySettleGroup)=>idx.accounts.rows.findIndex(a=>a.code===g.account);
  return [...groups.values()].sort((a,b)=>order(a)-order(b)||accOrder(a)-accOrder(b)).map(g=>({...g,lines:g.lines.sort((a,b)=>{
    const x=idx.get(a.method)?.sortOrder??1e9,y=idx.get(b.method)?.sortOrder??1e9;return x-y||a.method.localeCompare(b.method);})}));
}
/** Masked account number for display: "XXXX 4321". */
export const mnyMasked=(last4?:string)=>last4?'XXXX '+last4:'—';
