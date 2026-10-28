// ACC-1 · party sub-ledgers (02 Planning/agents/ACC1-POSTING-CONTRACT.md §3; rulebook §3.3, DEC-132, DEC-144). Exported names start with
// 'acc' / 'ACC' (shared mock namespace).
//
// A party ledger id is `<control code>/<party id>` (e.g. 1110/CMP-0007, 2200/V-MORNING-SUPPLIES, 1030/BANK-1, 1000/TILL, 1120/UD-9876543210).
// Parties are DERIVED from the masters, never stored twice: tills and bank accounts (money masters), companies (Groups & corporate), OTA
// channels, Udhaar customers (the expense register), staff and expense vendors. A voucher entry posts to the party ledger when its source
// names the party; when it cannot (an old record without the party, a method with no settles-to account) it posts to the control code
// itself and accExceptions raises a `no-party` warning (never blocks a lock).
//
// LEAF module: only type imports plus the money-master leaf, so accounting.ts (and anything else) can import it without a cycle.
import type {State} from '../types';
import type {ExpUdReceivable} from './expenses-scc';
import {mnyAccounts,mnyMethod,mnyMethodIndex,mnyVendor,mnyVendors,type MnyAccount} from './money-master';

/** Control ledgers (contract §2 / §3): they hold party sub-ledgers and are never meant to be posted to directly. */
export const ACC_PARTY_CONTROLS=['1000','1030','1110','1115','1120','1200','1210','2200','2400'] as const; // ACC-2 integration (ruling 7): 1210 recoverable from leavers, one party per staff member
export type ACCPartyControl=typeof ACC_PARTY_CONTROLS[number];
/** One party sub-ledger (contract §3: `accPartyLedgers(s)` returns {id, control, partyId, name, active}[]). */
export type ACCPartyLedger={id:string;control:string;partyId:string;name:string;active:boolean};
/** What a source knows about its parties; each control picks the field it needs (contract §3 "party resolution per source"). */
export type ACCPartyCtx={
  /** Payment method / funding name: cash → the till it settles to (1000), bank → the bank account it settles to (1030). */method?:string;
  /** Till code the record names (cash leaving a drawer); wins over the method's settles-to for 1000 when it is a till row. */till?:string;
  /** Bank text a record names (bank deposit slip: code, name or bank); wins over the method's settles-to for 1030. */bank?:string;
  /** Company account id (1110). */company?:string;
  /** OTA channel id (1115). */channel?:string;
  /** Udhaar customer party id (1120), see accUdhaarPartyId. */udhaar?:string;
  /** Staff id (1200 staff advances; 1210 recoverable from a leaver, ruling 7). */staff?:string;
  /** Expense vendor name or code as stored on the expense (2200). */vendor?:string;
};

export const accIsControl=(code:string)=>(ACC_PARTY_CONTROLS as readonly string[]).includes(code);
/** True for a party ledger id `<4-digit control>/<party>`. */
export const accIsParty=(id:string)=>/^\d{4}\/.+$/.test(String(id??''));
/** Control code of a ledger id (the id itself for a plain ledger code). */
export const accControlOf=(id:string)=>{const i=String(id??'').indexOf('/');return i<0?String(id??''):String(id).slice(0,i);};
/** Party id of a party ledger id ('' for a plain ledger code). */
export const accPartyIdOf=(id:string)=>{const i=String(id??'').indexOf('/');return i<0?'':String(id).slice(i+1);};
export const accPartyLedgerId=(control:string,partyId:string)=>`${control}/${partyId}`;

// ── Udhaar customers (contract §3: distinct (name, mobile); id UD-<10-digit mobile>, or UD-<slug of name> without a mobile) ──
/** Slug of a name for a party id: capitals, digits and '-', at most 36 characters. */
export const accPartySlug=(name:string)=>String(name??'').toUpperCase().replace(/[^A-Z0-9]+/g,'-').replace(/^-+|-+$/g,'').slice(0,36).replace(/-+$/,'')||'X';
export function accUdhaarPartyId(r:Pick<ExpUdReceivable,'name'|'mobile'>):string{
  const m=String(r.mobile??'').replace(/\D/g,'');return m.length>=10?'UD-'+m.slice(-10):'UD-'+accPartySlug(r.name);
}

// ── Master rows by control ──
const tills=(s:State)=>mnyAccounts(s).filter(a=>a.kind==='till');
const banks=(s:State)=>mnyAccounts(s).filter(a=>a.kind==='bank');
const companies=(s:State)=>(s.groupsX?.companies??[]) as {id:string;name:string}[];
const channels=(s:State)=>(s.channelsX?.channels??[]) as {id:string;name:string}[];
const staff=(s:State)=>(s.staffX?.staff??[]) as {id:string;name:string;active:boolean}[];
const udhaar=(s:State)=>(s.expensesX?.udhaar??[]) as ExpUdReceivable[];

/** Every party sub-ledger derived from the masters, in control order then master order (contract §3). */
export function accPartyLedgers(s:State):ACCPartyLedger[]{
  const out:ACCPartyLedger[]=[];
  const add=(control:string,partyId:string,name:string,active:boolean)=>{if(partyId)out.push({id:accPartyLedgerId(control,partyId),control,partyId,name,active});};
  for(const a of tills(s))add('1000',a.code,a.name,a.active);
  for(const a of banks(s))add('1030',a.code,a.name,a.active);
  for(const c of companies(s))add('1110',c.id,c.name,true);
  for(const c of channels(s))add('1115',c.id,c.name,true);
  const ud=new Map<string,string>();for(const r of udhaar(s)){const id=accUdhaarPartyId(r);if(!ud.has(id))ud.set(id,r.name);}
  for(const [id,name] of ud)add('1120',id,name,true);
  for(const x of staff(s))add('1200',x.id,x.name,x.active!==false);
  for(const x of staff(s))add('1210',x.id,x.name,x.active!==false); // ACC-2 integration (ruling 7): a negative F&F posts to 1210/<leaver>
  for(const v of mnyVendors(s))add('2200',v.code,v.name,v.active);
  // ACC-2 integration (ruling 2, manual DONE item 1): loan parties 2400/<CODE> are Owner-added chart rows (kind 'loan', ACC_LOAN_ADD).
  for(const l of (s.accountingX?.chart?.ledgers??[]) as {code:string;name:string;active:boolean;kind?:string}[])if(l.kind==='loan'&&l.code.startsWith('2400/'))add('2400',l.code.slice(5),l.name,l.active);
  return out;
}
/** Display name of a party ledger id (the master row's name), or undefined when it is not a party id / not in the masters any more. */
export function accPartyName(s:State,id:string):string|undefined{
  if(!accIsParty(id))return undefined;const c=accControlOf(id),p=accPartyIdOf(id);
  switch(c){
    case '1000':case '1030':return mnyAccounts(s).find(a=>a.code===p)?.name;
    case '1110':return companies(s).find(x=>x.id===p)?.name;
    case '1115':return channels(s).find(x=>x.id===p)?.name;
    case '1120':return udhaar(s).find(r=>accUdhaarPartyId(r)===p)?.name;
    case '1200':case '1210':return staff(s).find(x=>x.id===p)?.name; // 1210: ruling 7
    case '2200':return mnyVendors(s).find(v=>v.code===p)?.name;
    case '2400':return ((s.accountingX?.chart?.ledgers??[]) as {code:string;name:string;kind?:string}[]).find(l=>l.kind==='loan'&&l.code===id)?.name; // ACC-2 integration (ruling 2)
    default:return undefined;
  }
}

// ── Per-source resolvers (contract §3) ──
/** Bank account a free-text bank names (deposit slip): code, name or bank (case-insensitive), else the only bank account. */
export function accBankOf(s:State,text?:string):MnyAccount|undefined{
  const bs=banks(s),t=String(text??'').trim().toLowerCase();
  if(t){const hit=bs.find(a=>a.code.toLowerCase()===t||a.name.toLowerCase()===t||String(a.bank??'').toLowerCase()===t)
    ??bs.find(a=>t.includes(a.name.toLowerCase())||t.includes(a.code.toLowerCase()));if(hit)return hit;}
  const act=bs.filter(a=>a.active);return act.length===1?act[0]:bs.length===1?bs[0]:undefined;
}
/** The account a method settles to when it is of `kind` (till / bank); a retired or renamed method still resolves (money master). */
function settles(s:State,method:string|undefined,kind:'till'|'bank'):string|undefined{
  if(!method)return undefined;const m=mnyMethod(s,method);if(!m?.settlesTo)return undefined;
  const a=mnyMethodIndex(s).accounts.get(m.settlesTo);return a&&a.kind===kind?a.code:undefined;
}
/** Party id for a control ledger and a source context; undefined = the source does not name the party (post to the control). */
export function accPartyFor(s:State,control:string,ctx:ACCPartyCtx):string|undefined{
  switch(control){
    case '1000':{const t=ctx.till?tills(s).find(a=>a.code===ctx.till):undefined;return t?.code??settles(s,ctx.method,'till');}
    case '1030':return (ctx.bank!==undefined?accBankOf(s,ctx.bank)?.code:undefined)??settles(s,ctx.method,'bank');
    case '1110':return ctx.company&&companies(s).some(c=>c.id===ctx.company)?ctx.company:undefined;
    case '1115':return ctx.channel&&channels(s).some(c=>c.id===ctx.channel)?ctx.channel:undefined;
    case '1120':return ctx.udhaar||undefined;
    case '1200':case '1210':return ctx.staff&&staff(s).some(x=>x.id===ctx.staff)?ctx.staff:undefined; // 1210: ruling 7
    case '2200':{const v=String(ctx.vendor??'').trim();return v?mnyVendor(s,v)?.code:undefined;}
    default:return undefined; // 2400 loans: Owner-added rows come with ACC-2
  }
}
/** The ledger id an entry posts to: the mapped code, or `<code>/<party>` when the code is a control ledger and the source names the party. */
export function accPost(s:State,code:string,ctx:ACCPartyCtx):string{
  if(!accIsControl(code))return code;const p=accPartyFor(s,code,ctx);return p?accPartyLedgerId(code,p):code;
}
