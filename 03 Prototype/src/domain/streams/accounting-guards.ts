// ACC-2 flows · cash guards (02 Planning/agents/ACC2-POSTING-CONTRACT.md §7; rulebook §5 B9, B10). Exported names start with 'acc' / 'ACC'.
//
// B9  receipts: cash from one party totalling ₹2,00,000 or more on one day, or for one booking / transaction / event, is REFUSED (code
//     CASH_LIMIT). Income-tax Act 2025 s.186 (old s.269ST; penalty old s.271DA → s.451) — VERIFY section numbers (rulebook OP-10, CA).
// B10 payments: cash to one party totalling MORE than ₹10,000 on one day is ALLOWED with a warning; the record carries
//     `cashDeductionLost: true` (the P&L notes list them). ₹35,000 for goods-transport categories (flag `transport`). Old s.40A(3); the
//     2025-Act section is reported as 36 or 41 — VERIFY section number.
//
// The three limits are dated values (`cash.receiptLimit`, `cash.paymentLimit`, `cash.transportLimit`, paise) in Master configuration ›
// Money and tax (Owner-only, DEC-058), read like the GST master (gstValueOn): the latest applied hub change effective on or before the
// date, else the seed below. One guarded call per command: folio / POS receipts, Udhaar settlements, company payments, expenses, staff
// advances (this lane); vendor payments and manual Payment / Receipt vouchers call the same function (their lanes).
import type {State} from '../types';
import {HccError} from '../errors';
import {money} from '../money';
import {mnyIsDrawer,mnyCategory,mnyVendor} from './money-master';
import {accPartyName,accIsParty,accUdhaarPartyId} from './accounting-parties';
import {registerSettingsGroup} from './config';

// ── Dated limits (contract §7; `TDS_SEED`-style keys) ──
export type ACCCashLimitKey='cash.receiptLimit'|'cash.paymentLimit'|'cash.transportLimit';
type ACCSeedDef={key:ACCCashLimitKey;label:string;unit:string;help:string;source:string;versions:{from:string;value:number}[]};
/** "VERIFY section numbers" (rulebook B9 / B10, OP-10): the 2025-Act sections are as reported; the CA confirms. */
export const ACC_CASH_VERIFY='VERIFY section numbers — CA to confirm (rulebook OP-10)';
/** Seed versions (oldest first; the first applies to every earlier date). Amounts in paise. */
export const ACC_CASH_SEED:ACCSeedDef[]=[
  {key:'cash.receiptLimit',label:'Cash receipt limit (refused at or above)',unit:'₹ from one person in a day, or for one booking / transaction / event',
    help:'A cash receipt that takes one party’s cash to this amount or more — on one day, or for one booking or event — is refused; take it by UPI, card or bank transfer.',
    source:'Income-tax Act 2025 s.186 (old s.269ST; penalty old s.271DA → s.451)',versions:[{from:'2017-04-01',value:20000000}]},
  {key:'cash.paymentLimit',label:'Cash payment limit (warned above)',unit:'₹ to one person in a day',
    help:'A cash payment that takes one party’s cash above this amount on one day is allowed with a warning; the expense deduction is lost for income tax and the record is marked “deduction lost”.',
    source:'Old Income-tax Act s.40A(3) (2025 Act: reported as s.36 / s.41)',versions:[{from:'2017-04-01',value:1000000}]},
  {key:'cash.transportLimit',label:'Cash payment limit for goods transport (warned above)',unit:'₹ to one transporter in a day',
    help:'As the cash payment limit, for expense categories flagged “transport” (hire of goods carriages).',
    source:'Old Income-tax Act s.40A(3A) proviso / Rule 6DD (2025 Act: reported as s.36 / s.41)',versions:[{from:'2017-04-01',value:3500000}]},
];
export const ACC_CASH_KEYS=ACC_CASH_SEED.map(d=>d.key);
export const accCashSeedOn=(key:ACCCashLimitKey,date:string)=>{const d=ACC_CASH_SEED.find(x=>x.key===key)!;let v=d.versions[0].value;for(const x of d.versions)if(x.from<=date)v=x.value;return v;};
/** Limit in force on a date (paise): the latest applied hub change effective on or before `date` (newest-first log), else the seed. */
export function accCashLimitOn(s:State,key:ACCCashLimitKey,date:string):number{
  const log=s.configX?.changes;let hit:{effectiveFrom:string;after:unknown}|undefined;
  if(log?.length)for(let i=log.length-1;i>=0;i--){const c=log[i];if(c.key===key&&c.status!=='withdrawn'&&c.effectiveFrom<=date&&(!hit||c.effectiveFrom>=hit.effectiveFrom))hit=c;}
  const v=Number(hit?.after);return hit&&Number.isSafeInteger(v)&&v>0?v:accCashSeedOn(key,date);
}
// Master configuration › Money and tax: the three dated limits (Owner-only money settings, DEC-058).
registerSettingsGroup({id:'acc-cash-guards',group:'money',defs:(s:State)=>ACC_CASH_SEED.map(d=>({key:d.key,label:d.label,type:'money' as const,unit:d.unit,
  help:`${d.help} Source: ${d.source} — ${ACC_CASH_VERIFY}. Illustrative — CA to confirm for a hotel.`,money:true,dated:true,effectiveMode:'date' as const,illustrative:true,
  default:accCashSeedOn(d.key,s.date),min:100,max:100000000,show:(v:unknown)=>money(Number(v)),
  affects:'Cash receipts (refused at the limit) and cash payments (warned above it) dated on or after the effective date. Recorded entries are not changed.'}))});

// ── Cash flows already recorded (the totals the guard adds to) ──
/** One cash movement with a party: `party` = the person (a party ledger id `<control>/<id>`, `booking:<id>`, `pos:<order>`, `expense:<id>`);
 *  `scope` = the booking / transaction / event it belongs to (summed over every date for receipts). */
export type ACCCashFlow={kind:'receipt'|'payment';party:string;scope?:string;date:string;amount:number;transport?:boolean;source:string};
const isCash=(s:State,method:string|undefined)=>!!method&&mnyIsDrawer(s,method);
const vendorParty=(s:State,v:string|undefined,fallback:string)=>{const t=String(v??'').trim();if(!t)return fallback;const row=mnyVendor(s,t);return '2200/'+(row?.code??t);};
const transportOf=(s:State,category:string|undefined)=>!!category&&!!(mnyCategory(s,category) as {transport?:boolean}|undefined)?.transport;
/** Every recorded cash receipt / payment of the state with its party (drawer-cash methods; void / rejected records excluded). */
export function accCashFlows(s:State):ACCCashFlow[]{
  const out:ACCCashFlow[]=[];
  for(const b of s.bookings)for(const r of b.receipts)if((r.kind==='payment'||r.kind==='deposit')&&isCash(s,r.method))
    out.push({kind:'receipt',party:'booking:'+b.id,scope:'booking:'+b.id,date:r.date,amount:r.amount,source:`${b.id}/${r.id}`});
  for(const r of s.posX?.receipts??[])if(r.kind==='sale'&&r.amount>0&&isCash(s,r.method))out.push({kind:'receipt',party:'pos:'+r.order,scope:'pos:'+r.order,date:r.date,amount:r.amount,source:r.id});
  for(const u of s.expensesX?.udhaar??[]){const party='1120/'+udParty(u);
    for(const p of u.settlements??[])if(p.desk&&isCash(s,p.mode))out.push({kind:'receipt',party,scope:'udhaar:'+u.id,date:p.date,amount:p.amount,source:p.id});}
  for(const p of s.groupsX?.payments??[])if(isCash(s,p.method))out.push({kind:'receipt',party:'1110/'+p.company,date:p.date,amount:p.amount,source:p.id});
  for(const e of s.expenses)if(e.status==='Posted'&&isCash(s,e.method))out.push({kind:'payment',party:vendorParty(s,e.vendor,'expense:'+(e.reversalOf??e.id)),date:e.date,amount:e.amount,transport:transportOf(s,e.category),source:e.id});
  for(const p of s.expensesX?.vendorPayments??[])if(!p.void&&isCash(s,p.method))out.push({kind:'payment',party:vendorParty(s,p.vendor,'vendor-payment:'+p.id),date:p.date,amount:p.amount,source:p.id});
  for(const a of s.staffX?.advances??[]){const f=(a as {funding?:string}).funding;if(isCash(s,f))out.push({kind:'payment',party:'1200/'+a.staffId,date:a.date,amount:a.amount,source:a.id});}
  for(const v of s.accountingX?.manual??[]){
    if(v.status!=='approved'||(v.type!=='Payment'&&v.type!=='Receipt'))continue;
    const cash=v.lines.filter(l=>l.account==='1000'||l.account==='1005'||l.account.startsWith('1000/')),other=v.lines.find(l=>!cash.includes(l)&&accIsParty(l.account));
    const amt=cash.reduce((a,l)=>a+(v.type==='Receipt'?l.debit-l.credit:l.credit-l.debit),0);
    if(amt&&other)out.push({kind:v.type==='Receipt'?'receipt':'payment',party:other.account,date:v.bookedOn??v.date,amount:amt,source:v.id});
  }
  return out;
}
const udParty=(r:{name:string;mobile:string})=>accUdhaarPartyId(r);
/** Party id of an Udhaar customer for the guard (`1120/UD-…`, as the books). */
export const accCashUdhaarParty=(r:{name:string;mobile:string})=>'1120/'+udParty(r);

// ── The guard ──
export type ACCCashGuardInput={kind:'receipt'|'payment';
  /** The person: a party ledger id (`1110/CO-1`, `1120/UD-…`, `2200/EV-…`, `1200/STF-…`), `booking:<id>`, `pos:<order>`. */party:string;
  /** Paise of cash in this record (the cash tenders only). */amount:number;
  /** Business date (default the business date). */date?:string;
  /** Booking / transaction / event the receipt belongs to (receipts: summed over every date). */scope?:string;
  /** Goods-transport category (₹35,000 instead of ₹10,000). */transport?:boolean;
  /** The tender / funding name: when given and it is not drawer cash, the guard passes without a check. */method?:string;
  /** Display name of the party for the message (default: the party ledger's name). */name?:string};
export type ACCCashGuardResult={ok:boolean;refused:boolean;deductionLost:boolean;code?:'CASH_LIMIT';message:string;warning:string;limit:number;total:number;cash:boolean};
const PASS=(limit=0,total=0,cash=false):ACCCashGuardResult=>({ok:true,refused:false,deductionLost:false,message:'',warning:'',limit,total,cash});
function partyLabel(s:State,party:string,name?:string){
  if(name)return name;
  if(party.startsWith('booking:')){const b=s.bookings.find(x=>x.id===party.slice(8));return b?`${b.guest} (booking ${b.id})`:party.slice(8);}
  if(party.startsWith('pos:'))return 'outlet bill '+party.slice(4);
  if(party.startsWith('expense:'))return 'this payee';
  return (accIsParty(party)&&accPartyName(s,party))||party.slice(party.indexOf('/')+1);
}
/** Refusal text (B9). */
export const accCashReceiptMessage=(who:string,total:number,limit:number,scoped:boolean,date:string)=>
  `Cash of ${money(total)} from ${who} ${scoped?'for this booking / transaction':'on '+date} reaches the ${money(limit)} cash-receipt limit — take it by UPI, card or bank transfer (Income-tax Act 2025 s.186, old s.269ST; ${ACC_CASH_VERIFY}).`;
/** Warning text (B10). */
export const accCashPaymentWarning=(who:string,total:number,limit:number,date:string)=>
  `Cash paid to ${who} on ${date} totals ${money(total)}, more than ${money(limit)}: the income-tax deduction is lost (old s.40A(3); ${ACC_CASH_VERIFY}) — pay by bank transfer or UPI to keep it. Marked “deduction lost”.`;
/**
 * Contract §7: `accCashGuard(s, {kind, party, amount, date})`. A receipt is refused (CASH_LIMIT) when the party's cash on that date — or
 * for the scope (booking / transaction / event) on any date — reaches the receipt limit with this amount; a payment above the payment
 * limit (transport limit for a `transport` category) on that date is allowed with a warning and `deductionLost`. Pure; `exclude` the
 * record's own source id when re-checking a record that is already in the state.
 */
export function accCashGuard(s:State,x:ACCCashGuardInput&{exclude?:string}):ACCCashGuardResult{
  if(x.method!==undefined&&!isCash(s,x.method))return PASS();
  const date=x.date??s.date,amount=Math.max(0,Math.round(Number(x.amount)||0));if(!amount)return PASS(0,0,true);
  const flows=accCashFlows(s).filter(f=>f.kind===x.kind&&f.source!==x.exclude),who=partyLabel(s,x.party,x.name);
  const day=flows.filter(f=>f.party===x.party&&f.date===date).reduce((a,f)=>a+f.amount,0)+amount;
  if(x.kind==='receipt'){
    const limit=accCashLimitOn(s,'cash.receiptLimit',date);
    const scoped=x.scope?flows.filter(f=>f.scope===x.scope).reduce((a,f)=>a+f.amount,0)+amount:0;
    if(day>=limit||scoped>=limit){const total=Math.max(day,scoped);
      return {ok:false,refused:true,deductionLost:false,code:'CASH_LIMIT',message:accCashReceiptMessage(who,total,limit,scoped>=limit&&scoped>=day,date),warning:'',limit,total,cash:true};}
    return PASS(limit,Math.max(day,scoped),true);
  }
  const limit=accCashLimitOn(s,x.transport?'cash.transportLimit':'cash.paymentLimit',date);
  if(day>limit){const warning=accCashPaymentWarning(who,day,limit,date);return {ok:true,refused:false,deductionLost:true,message:warning,warning,limit,total:day,cash:true};}
  return PASS(limit,day,true);
}
/** Typed refusal of the guard: an HccError (code VALIDATION, as the server's 422) carrying the reason CASH_LIMIT. */
export class AccCashLimitError extends HccError{
  readonly reason='CASH_LIMIT' as const;
  constructor(message:string,public readonly limit:number,public readonly total:number){super('VALIDATION',message);}
}
/** The handlers' one guarded call: throws AccCashLimitError on a refused receipt; otherwise returns the result (warning / deductionLost). */
export function accCashEnforce(s:State,x:ACCCashGuardInput&{exclude?:string}):ACCCashGuardResult{
  const g=accCashGuard(s,x);if(g.refused)throw new AccCashLimitError(g.message,g.limit,g.total);return g;
}
/** ' · <warning>' for a command's summary text ('' when none). */
export const accCashNote=(g:ACCCashGuardResult)=>g.warning?' · '+g.warning:'';
/** P&L notes (B10): the records marked "deduction lost" (expenses), newest first. */
export function accCashDeductionLost(s:State,from?:string,to?:string){
  return s.expenses.filter(e=>e.cashDeductionLost&&e.status==='Posted'&&(!from||e.date>=from)&&(!to||e.date<=to))
    .map(e=>({id:e.id,date:e.date,vendor:e.vendor,category:e.category,amount:e.amount})).sort((a,b)=>b.date.localeCompare(a.date)||b.id.localeCompare(a.id));
}
