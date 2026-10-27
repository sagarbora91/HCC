// ACC-1 · The chart of accounts as an Owner-only master — command handlers (posting contract §1, §2, §7; rules in domain/streams/accounting-chart.ts).
//   ACC_LEDGER_UPSERT {create?, code, name?, group?, gst?, tallyName?, note?, active?}   create:true adds an Owner ledger (unused 4-digit code); else edits one (a built-in:
//                                                                              name, group within the same Tally nature, GST flag, Tally name, note);
//                                                                              active:true reactivates a retired ledger
//   ACC_LEDGER_RETIRE {code, reason?}                                          refused (MASTER_IN_USE) while mapped or with a non-zero balance (own or party)
//   ACC_GROUP_UPSERT  {id?, name, parent}                                      add a custom sub-group g-<slug> or rename / re-parent one
// Owner only (DEC-058; server PERM-ACC-007). Every change writes one history row per field. Return value = audit / summary text.
import type {Handler,Kit} from '../commands';
import {mstFail} from '../../../domain/streams/masters';
import {accSlice,type ACCState,type ACCLedger,type ACCGroupDef,type ACCChartChange} from '../../../domain/streams/accounting';
import {accCanEditChart,accChartSlice,accBuiltinLedger,accLedgerUpsertPlan,accGroupUpsertPlan,accLedgerRetireBlocker,accChart,ACC_CHART_OWNER_ONLY,
  type ACCChartPlan,type ACCLedgerInput} from '../../../domain/streams/accounting-chart';

/** Writable chart on the cloned state (creates the slice and the chart for old saves). */
function chart(k:Kit){const x:ACCState={...accSlice(k.s)};const c=accChartSlice(k.s);
  x.chart={ledgers:[...c.ledgers],groups:[...c.groups],history:[...c.history]};k.s.accountingX=x;return x.chart;}
const owner=(k:Kit)=>{if(!accCanEditChart(k.s,k.uid))k.fail('FORBIDDEN','Access denied: '+ACC_CHART_OWNER_ONLY);};
const apply=<T,>(p:ACCChartPlan<T>):Extract<ACCChartPlan<T>,{ok:true}>=>{if(!p.ok)mstFail(p.refusal.reason,p.refusal.message,p.refusal.field);return p as Extract<ACCChartPlan<T>,{ok:true}>;};
const hist=(k:Kit,kind:ACCChartChange['kind'],code:string,changes:[string,string,string][]):ACCChartChange[]=>
  changes.map(([field,before,after])=>({id:k.id('ACCC'),at:k.s.clock,actor:k.uid,kind,code,field,before,after}));
/** A built-in whose stored row equals its defaults is not stored (the chart reads the built-in). */
function isDefault(row:ACCLedger){const b=accBuiltinLedger(row.code);
  return !!b&&b.name===row.name&&b.group===row.group&&(b.gst??'none')===(row.gst??'none')&&!row.tallyName&&row.active&&(row.note===undefined||row.note===(b.note??''));}

export const handlers:Record<string,Handler>={
  ACC_LEDGER_UPSERT:k=>{
    owner(k);const p=apply(accLedgerUpsertPlan(k.s,k.cmd as ACCLedgerInput));
    const c=chart(k),rest=c.ledgers.filter(l=>l.code!==p.row.code);
    c.ledgers=isDefault(p.row)?rest:[...rest,p.row].sort((a,b)=>a.code<b.code?-1:1);
    c.history=[...c.history,...hist(k,'ledger',p.row.code,p.changes)];
    return p.created?`Ledger ${p.row.code} ${p.row.name} added to the chart of accounts`
      :`Ledger ${p.row.code} ${p.row.name} · ${p.changes.map(([f,a,b])=>`${f}: ${a||'—'} → ${b||'—'}`).join(' · ')}`;
  },
  ACC_LEDGER_RETIRE:k=>{
    owner(k);const code=String(k.cmd.code??'').trim();
    const l=accChart(k.s).find(x=>x.code===code);if(!l)k.fail('NOT_FOUND','This ledger was not found in the chart of accounts.');
    const why=accLedgerRetireBlocker(k.s,code);
    if(why)mstFail(l!.active?'MASTER_IN_USE':'MASTER_RETIRED',why);
    const reason=String(k.cmd.reason??'').trim().slice(0,200);
    const row:ACCLedger={...l!,active:false};
    const c=chart(k);c.ledgers=[...c.ledgers.filter(x=>x.code!==code),row].sort((a,b)=>a.code<b.code?-1:1);
    c.history=[...c.history,...hist(k,'ledger',code,[['active','active',reason?'retired · '+reason:'retired']])];
    return `Ledger ${code} ${l!.name} retired${reason?' · '+reason:''}`;
  },
  ACC_GROUP_UPSERT:k=>{
    owner(k);const p=apply(accGroupUpsertPlan(k.s,k.cmd as {id?:unknown;name?:unknown;parent?:unknown}));
    const c=chart(k),row:ACCGroupDef={...p.row};
    c.groups=[...c.groups.filter(g=>g.id!==row.id),row];
    c.history=[...c.history,...hist(k,'group',row.id,p.changes)];
    return p.created?`Ledger group ${row.name} (${row.id}) added`:`Ledger group ${row.name} · ${p.changes.map(([f,a,b])=>`${f}: ${a||'—'} → ${b||'—'}`).join(' · ')}`;
  },
};
