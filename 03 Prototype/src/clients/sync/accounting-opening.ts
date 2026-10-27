// ACC-1 · Opening balances entered by hand (contract §5) — production sync (server: Hcc.Api/Endpoints/AccountingOpeningEndpoints.cs).
//
//   commands  ACC_OPENING_SAVE {asOf, lines, note?} → SaveOpeningBalances {asOf, lines:[{account, debitPaise, creditPaise}], note}
//             ACC_OPENING_APPROVE {}               → ApproveOpeningBalances {expectedVersion?}
//             ACC_OPENING_REOPEN {reason}          → ReopenOpeningBalances {reason, expectedVersion?}
//             each reloads accountingX (the server's cumulative trial balance then carries the approved entry as the opening that
//             supersedes an imported one: `server.opening`, sub 'opening-import').
//   loader    accountingOpeningLoader (patch on accountingX ← GET /accounting/opening, PERM-ACC-001) fills `accountingX.opening` for the
//             entry grid and the pending banner. It is NOT in `loaders` yet: a new GET must first be listed in sync/reads.ts READS and in
//             tests/_role-reads.json (regenerated from the real server), which this lane does not own — see
//             05 Delivery Evidence/Accounting/ACC1-OPENING-DONE.md "Integration needed". Until then the production screen shows no draft.
import type {ACCState,ACCOpening,ACCOpeningLine} from '../../domain/streams/accounting';
import {HccClientError} from '../errors';
import type {SyncArea,LoaderCtx,CommandCtx,SliceLoader} from './types';
import {protoUserId,utcToClock,paise,isoDate} from './map';

/** GET /accounting/opening (Hcc.Contracts.Accounting.OpeningBalancesStateView). */
export type SrvOpeningLine={account:string;name:string;debitPaise:number;creditPaise:number};
export type SrvOpeningEntry={asOf:string;status:string;lines:SrvOpeningLine[];debitPaise:number;creditPaise:number;differencePaise:number;
  enteredBy:number;enteredUtc:string;approvedBy?:number|null;approvedUtc?:string|null;note?:string|null;version:string};
export type SrvOpeningState={fyStart:string;pending:boolean;entry?:SrvOpeningEntry|null;lockedFrom?:string|null};

const utc=(v:string|null|undefined)=>!v?'':/(Z|[+-]\d\d:\d\d)$/i.test(v)?v:v+'Z';

/** Server entry → the prototype's ACCOpening (paise, YYYY-MM-DD, prototype user ids, IST clock). */
export function openingFromServer(e:SrvOpeningEntry):ACCOpening{
  const o:ACCOpening={asOf:isoDate(e.asOf,'asOf'),status:e.status==='approved'?'approved':'draft',
    lines:e.lines.map(l=>({account:l.account,debit:paise(l.debitPaise,'debitPaise'),credit:paise(l.creditPaise,'creditPaise')})),
    by:protoUserId(e.enteredBy),at:utcToClock(utc(e.enteredUtc))};
  if(e.approvedBy!=null)o.approvedBy=protoUserId(e.approvedBy);
  if(e.approvedUtc)o.approvedAt=utcToClock(utc(e.approvedUtc));
  if(e.note)o.note=e.note;
  return o;
}
/** SaveOpeningBalances body from the prototype command (the 1400 line is recomputed by the server and never sent). */
export function openingSaveBody(cmd:Record<string,unknown>):{asOf:string|null;lines:{account:string;debitPaise:number;creditPaise:number}[];note:string|null}{
  if(!Array.isArray(cmd.lines)||!cmd.lines.length)throw new HccClientError('VALIDATION','Enter at least one opening line.');
  const lines=(cmd.lines as Partial<ACCOpeningLine>[]).map(l=>({account:String(l?.account??'').trim(),debitPaise:Number(l?.debit??0),creditPaise:Number(l?.credit??0)}))
    .filter(l=>l.account!=='1400');
  if(!lines.length)throw new HccClientError('VALIDATION','Enter at least one opening line.');
  const asOf=typeof cmd.asOf==='string'&&cmd.asOf?cmd.asOf:null,note=cmd.note==null?'':String(cmd.note).trim();
  return {asOf,lines,note:note||null};
}
/** The version of the entry loaded last (approve / reopen send it; undefined before the loader is registered → the server skips the check). */
const version=(c:CommandCtx)=>c.ids.version('accOpening','opening');

async function loadOpening(c:LoaderCtx):Promise<ACCState>{
  const cur:ACCState={...(c.current('accountingX')??{})};
  const r=await c.api.get<SrvOpeningState>('/accounting/opening');
  if(r.entry){c.ids.setVersion('accOpening','opening',r.entry.version);cur.opening=openingFromServer(r.entry);}else delete cur.opening;
  return cur;
}
/** Patch loader for accountingX (register it in `accountingOpening.loaders` once GET /accounting/opening is in READS). */
export const accountingOpeningLoader:SliceLoader={slice:'accountingX',kind:'patch',requires:'reports',needs:['accountingX'],load:loadOpening};

export const accountingOpening:SyncArea={
  id:'accounting-opening',
  loaders:[],
  commands:[
    {type:'ACC_OPENING_SAVE',reload:['accountingX'],
      async send(c){await c.api.command('SaveOpeningBalances',openingSaveBody(c.cmd));}},
    {type:'ACC_OPENING_APPROVE',reload:['accountingX'],
      async send(c){await c.api.command('ApproveOpeningBalances',{expectedVersion:version(c)??null});}},
    {type:'ACC_OPENING_REOPEN',reload:['accountingX'],precheck:cmd=>String(cmd.reason??'').trim().length>=3?undefined:'A reason is required to reopen the opening balances.',
      async send(c){await c.api.command('ReopenOpeningBalances',{reason:String(c.cmd.reason??'').trim(),expectedVersion:version(c)??null});}},
  ],
};
