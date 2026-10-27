// ACC-1 lane acc1-chart · the chart of accounts as an Owner-only master on the server (Hcc.Api AccountingChartEndpoints; posting contract §1,
// §2, §7). Plain object literals and functions only (no top-level calls).
//
//   POST /commands/UpsertLedger {create, code, name, groupId, gst, tallyName, note, active}   ← ACC_LEDGER_UPSERT (PERM-ACC-007, Owner)
//   POST /commands/RetireLedger {code, reason}                                                 ← ACC_LEDGER_RETIRE
//   POST /commands/UpsertLedgerGroup {id, name, parent}                                        ← ACC_GROUP_UPSERT
//   GET  /accounting/chart/master  (PERM-ACC-001) → groups, ledgers (overridden flag), problems, canEdit
//   GET  /accounting/chart/history (PERM-ACC-001) → the field-level history
// accountingX.chart (patch after the W4-7 owner): ACC_CHART_LOADER below, registered at the ACC-1 integration (READS lines in sync/reads.ts,
// routes in tests/_role-reads.json, default fake routes in tests/_sync-harness.mjs).
import type {ACCGroupDef,ACCLedger,ACCChartChange,ACCState} from '../../domain/streams/accounting';
import type {SyncArea,SliceLoader,LoaderCtx} from './types';
import {protoUserId,utcToClock} from './map';

type Wire=Record<string,unknown>;
export type SrvLedgerView={code:string;name:string;groupId:string;group:string;nature:string|null;system:boolean;control:boolean;gst:string;tallyName:string|null;
  active:boolean;note:string|null;overridden:boolean;mappedBy:string[]};
export type SrvLedgerGroupView={id:string;name:string;parent:string|null;nature:'Dr'|'Cr';side:'BS'|'PL';custom:boolean;legacyGroup:string;path:string[]};
export type SrvChartMaster={groups:SrvLedgerGroupView[];ledgers:SrvLedgerView[];problems:Wire[];canEdit:boolean};
export type SrvChartChange={changeId:number;atUtc:string;actor:number;kind:'ledger'|'group';code:string;field:string;before:string;after:string};

const txt=(v:unknown)=>v===undefined||v===null?'':String(v).trim();
const opt=(v:unknown)=>v===undefined?null:v===null?null:String(v);
const GST=['none','input','output'];

/** The server's chart → accountingX.chart: stored rows = Owner-added ledgers and overridden built-ins (the prototype keeps only those),
 *  groups = the custom sub-groups, history in server order. */
export function accChartFromServer(m:SrvChartMaster,history:SrvChartChange[],uidOf:(id:number)=>string=protoUserId):NonNullable<ACCState['chart']>{
  const ledgers:ACCLedger[]=m.ledgers.filter(l=>!l.system||l.overridden).map(l=>{
    const r:ACCLedger={code:l.code,name:l.name,group:l.groupId,system:l.system,gst:(GST.includes(l.gst)?l.gst:'none') as ACCLedger['gst'],active:l.active};
    if(l.control)r.control=true;if(l.tallyName)r.tallyName=l.tallyName;if(l.note!==null||l.system)r.note=l.note??'';return r;});
  const groups:ACCGroupDef[]=m.groups.filter(g=>g.custom).map(g=>({id:g.id,name:g.name,...(g.parent?{parent:g.parent}:{}),nature:g.nature,side:g.side,custom:true}));
  return {ledgers,groups,history:history.map((h):ACCChartChange=>({id:'ACCC-'+h.changeId,at:utcToClock(h.atUtc),actor:uidOf(h.actor),kind:h.kind,code:h.code,field:h.field,before:h.before,after:h.after}))};
}

/** Patch loader for accountingX.chart. */
export const ACC_CHART_LOADER:SliceLoader={slice:'accountingX',kind:'patch',requires:'reports',reads:['GET /accounting/chart/master','GET /accounting/chart/history'],
  async load(c:LoaderCtx){
    const [m,h]=await Promise.all([c.api.get<SrvChartMaster>('/accounting/chart/master'),c.api.get<SrvChartChange[]>('/accounting/chart/history')]);
    const cur=c.current('accountingX')??{};
    return {...cur,chart:accChartFromServer(m,h,protoUserId)};
  }};

const owner=(me:{permIds:string[]})=>me.permIds.includes('PERM-ACC-007')?undefined:'Only the Owner changes the chart of accounts.';

export const accountingChart:SyncArea={id:'accountingChart',
  loaders:[ACC_CHART_LOADER],
  commands:[
    {type:'ACC_LEDGER_UPSERT',reload:['accountingX'],
      precheck:(cmd,_s,me)=>owner(me)??(/^\d{4}$/.test(txt(cmd.code))?undefined:'A ledger code is 4 digits, e.g. 5130.'),
      async send(c){const x=c.cmd as Wire;
        await c.api.command('UpsertLedger',{create:x.create===true,code:txt(x.code),name:opt(x.name),groupId:opt(x.group),gst:opt(x.gst),tallyName:opt(x.tallyName),
          note:opt(x.note),active:x.active===undefined?null:x.active===true||x.active==='true'});}},
    {type:'ACC_LEDGER_RETIRE',reload:['accountingX'],precheck:(cmd,_s,me)=>owner(me)??(/^\d{4}$/.test(txt(cmd.code))?undefined:'Choose the ledger to retire.'),
      async send(c){await c.api.command('RetireLedger',{code:txt(c.cmd.code),reason:txt(c.cmd.reason)||null});}},
    {type:'ACC_GROUP_UPSERT',reload:['accountingX'],precheck:(cmd,_s,me)=>owner(me)??(txt(cmd.name)||txt(cmd.id)?undefined:'Enter the group name (1–60 characters).'),
      async send(c){const x=c.cmd as Wire;await c.api.command('UpsertLedgerGroup',{id:txt(x.id)||null,name:opt(x.name),parent:opt(x.parent)});}},
  ]};
