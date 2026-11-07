// F12 MEAS · Owner digest block "Measurement" (MEAS-CONTRACT §4, lane meas-digest). Lists the red rows (each report's exception rule) as short
// lines with the report id as drill target. Pure: no state is written. Honours measOn / measOffKeys (an off item is absent; the whole block is absent
// when every pack is off) and the Owner's line subscriptions. The report lanes register their exception functions through registerMeasRedRows;
// the stubs below cover the Phase A captures that exist today (turn-aways, high balances). Owner only (PERM-DGS-001): the caller checks dgsCanView.
import type {State} from '../types';
import {money,addDays,nights} from '../money';
import {measOn,measTarget,measPackOn,MEAS_ITEMS} from './measure';
import {reportOccupancyRange} from './closing';
import {totals} from '../selectors';
import {mdTurnaways} from './turnaway';
import {grpCompany} from './groups';

export type MeasRedSeverity='red'|'amber';
/** One exception line. `key` = the item switch (meas.<pack>.<item>), `reportId` = the drill target (rpt.…), `text` = one short line. */
export type MeasRedRow={key:string;reportId:string;text:string;severity:MeasRedSeverity};
/** Pure exception function of one report: the rows that are red on the digest's business date. */
export type MeasRedRowFn=(s:State,date:string)=>MeasRedRow[];

const REG=new Map<string,MeasRedRowFn>();
/** The report lanes' integrator wires each report's pure exception function here (one provider per item key; a second call replaces the first). */
export function registerMeasRedRows(key:string,fn:MeasRedRowFn):void{REG.set(key,fn);}
export const measRedRowKeys=()=>[...REG.keys()];

// ───────────── Stub providers for the Phase A captures that exist today ─────────────
/** rpt.guests.regrets-denials: more than 3 price regrets on a night under 60 % occupancy. Regrets logged on `date`, counted per night they ask for. */
export const MEAS_REGRET_LIMIT=3,MEAS_REGRET_OCC_PCT=60;
export function measRegretRows(s:State,date:string):MeasRedRow[]{
  const per=new Map<string,number>();
  for(const t of mdTurnaways(s).filter(t=>t.reason==='price'&&t.at.slice(0,10)===date))
    for(let i=0;i<Math.max(1,t.nights);i++){const n=addDays(t.arrival,i);per.set(n,(per.get(n)??0)+t.rooms);}
  const out:MeasRedRow[]=[];
  for(const [night,count] of [...per].sort(([a],[b])=>a.localeCompare(b))){
    if(count<=MEAS_REGRET_LIMIT)continue;
    const o=reportOccupancyRange(s,night,night),pct=o.sellable?Math.round(o.occupied*100/o.sellable):0;
    if(pct<MEAS_REGRET_OCC_PCT)out.push({key:'meas.daily.regrets',reportId:'rpt.guests.regrets-denials',severity:'red',text:`${count} price regrets for ${night} at ${pct}% occupancy`});
  }
  return out;
}
/** rpt.money.high-balance: balance above deposit plus company limit; a walk-in without a company above the target (default Rs 5,000); any balance older than 2 nights. */
export const MEAS_BALANCE_NIGHTS=2;
export function measHighBalanceRows(s:State,date:string):MeasRedRow[]{
  const target=measTarget(s,'meas.target.highBalance')??500000,out:MeasRedRow[]=[];
  for(const b of s.bookings.filter(b=>b.status==='In house')){
    const t=totals(b);if(t.balance<=0)continue;
    const acct=b.company?.account?grpCompany(s,b.company.account):undefined,limit=acct?.creditLimit??0,days=Math.max(0,nights(b.arrival,date));
    const why=acct&&t.balance>t.deposit+limit?'above deposit plus limit'
      :!b.company&&t.balance>target?`walk-in above ${money(target)}`
      :days>MEAS_BALANCE_NIGHTS?`unpaid for ${days} nights`:'';
    if(why)out.push({key:'meas.daily.high-balance',reportId:'rpt.money.high-balance',severity:'red',text:`Room ${b.room} owes ${money(t.balance)} (${why})`});
  }
  return out;
}
registerMeasRedRows('meas.daily.regrets',measRegretRows);
registerMeasRedRows('meas.daily.high-balance',measHighBalanceRows);

// ───────────── Subscriptions (the Owner chooses which lines appear) ─────────────
/** Stored beside the other measurement values (configX.values): `meas.digest.line.<item key>` = false hides that item's lines. Default: subscribed. */
export const measDigestLineKey=(itemKey:string)=>`meas.digest.line.${itemKey}`;
export const measDigestSubscribed=(s:State,itemKey:string)=>s.configX?.values?.[measDigestLineKey(itemKey)]!==false;
export type MeasDigestSub={key:string;label:string;reportId?:string;subscribed:boolean};
/** Subscription rows: every measurement item that is on and has a registered exception provider. */
export function measDigestSubscriptions(s:State):MeasDigestSub[]{
  return MEAS_ITEMS.filter(i=>REG.has(i.key)&&measOn(s,i.key)).map(i=>({key:i.key,label:i.label,reportId:i.report,subscribed:measDigestSubscribed(s,i.key)}));
}

// ───────────── The block ─────────────
/** `checked` = subscribed, switched-on items that were evaluated (an empty block says "no exceptions"). */
export type MeasDigestBlock={title:'Measurement';date:string;rows:MeasRedRow[];checked:number};
/** The block for a business date. undefined = no block at all (every pack off, or nothing checkable). Off items and unsubscribed items are absent. */
export function measDigestBlock(s:State,date:string):MeasDigestBlock|undefined{
  if(!(['daily','revenue','guest','hk','people','study'] as const).some(p=>measPackOn(s,p)))return undefined;
  const rows:MeasRedRow[]=[];let checked=0;
  for(const it of MEAS_ITEMS){
    const fn=REG.get(it.key);if(!fn||!measOn(s,it.key)||!measDigestSubscribed(s,it.key))continue;
    checked++;
    for(const r of fn(s,date))if(r.key===it.key)rows.push(r); // a provider reports only on its own switch
  }
  if(!checked)return undefined;
  rows.sort((a,b)=>(a.severity===b.severity?0:a.severity==='red'?-1:1));
  return {title:'Measurement',date,rows,checked};
}
/** E-mail lines (money allowed; Owner and Accountant only): "  [rpt.money.high-balance] Room 105 owes …". */
export function measDigestEmailLines(b:MeasDigestBlock|undefined):string[]{
  if(!b)return [];
  return [`Measurement exceptions: ${b.rows.length}`,...(b.rows.length?b.rows.map(r=>`  [${r.reportId}] ${r.text}`):['  none today'])];
}
/** WhatsApp line (DEC-202 #7): a count only, no names, no money. */
export const measDigestWhatsAppLines=(b:MeasDigestBlock|undefined):string[]=>b?[`Measurement exceptions ${b.rows.length}`]:[];
