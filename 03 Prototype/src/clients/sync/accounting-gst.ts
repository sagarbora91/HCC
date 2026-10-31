// ACC-2 acc2-gst · GST month end, statutory remittances, the month lock and the GST-on-advances switch — production sync
// (server: Hcc.Api/Endpoints/AccountingGstEndpoints.cs; contracts Hcc.Contracts/Accounting/GstContracts.cs).
//
//   commands  ACC_GST_SETOFF {month}                                → PostGstSetoff {month}
//             ACC_GST_PAY {month, amount, method, challan, date, rcm?} → RecordGstPayment {month, amountPaise, method, challan, date, rcmPaise}
//             ACC_STAT_REMIT {kind, month, amount, method, challan, date} → RecordStatutoryRemittance {kind, month, amountPaise, method, challan, date}
//             ACC_MONTH_LOCK {month} / ACC_MONTH_LOCK_REQUEST {month} → LockAccountingMonth / RequestAccountingMonthLock {month}
//             ACC_MONTH_UNLOCK {month, reason}                       → UnlockAccountingMonth {month, reason}
//             ACC_GST_ADVANCES_SET {on}                              → SetGstOnAdvances {on}
//             each reloads accountingX (the server's day evaluation then carries the set-off journal and the day locks) and the remittance
//             also complianceX (the card it marks filed) and expensesX (the expense month lock).
//   loader    accountingGstLoader (patch on accountingX ← GET /accounting/gst/locks, PERM-ACC-001) fills monthLocks and gstSetoffs; registered
//             at the ACC-2 integration (READS line, tests/_role-reads.json).
import type {ACCState,ACCMonthLock} from '../../domain/streams/accounting';
import type {SyncArea,LoaderCtx,SliceLoader} from './types';
import {protoUserId,utcToClock} from './map';

/** GET /accounting/gst/locks (Hcc.Contracts.Accounting.AccountingMonthLocksView). */
export type SrvMonthLock={month:string;lockedUtc:string;lockedBy:number;setoffKey?:string|null;unlockedUtc?:string|null;unlockedBy?:number|null;unlockReason?:string|null};
export type SrvGstSetoff={month:string;postedOn:string;sourceKey:string;payablePaise:number;challanPaise:number};
export type SrvMonthLocks={locks:SrvMonthLock[];requested:string[];setoffs:SrvGstSetoff[];gstOnAdvances:boolean};

const utc=(v:string|null|undefined)=>!v?'':/(Z|[+-]\d\d:\d\d)$/i.test(v)?v:v+'Z';
const MONTH=/^\d{4}-(0[1-9]|1[0-2])$/;
const month=(cmd:Record<string,unknown>)=>MONTH.test(String(cmd.month??''))?undefined:'Choose a month (YYYY-MM).';

/** Server month-lock rows (oldest first per month) → the prototype's monthLocks (the last lock, its unlocks after it). */
export function monthLocksFromServer(rows:SrvMonthLock[]):Record<string,ACCMonthLock>{
  const out:Record<string,ACCMonthLock>={};
  for(const r of rows){
    const prev=out[r.month],unlocks=[...(prev?.unlocks??[])];
    const at=utcToClock(utc(r.lockedUtc));
    out[r.month]={lockedAt:at,lockedBy:protoUserId(r.lockedBy),...(r.setoffKey?{setoff:r.setoffKey}:{}),...(unlocks.length?{unlocks}:{})};
    if(r.unlockedUtc)out[r.month].unlocks=[...unlocks,{at:utcToClock(utc(r.unlockedUtc)),by:protoUserId(r.unlockedBy??0),reason:r.unlockReason??''}];
  }
  return out;
}
/** Server set-offs → gstSetoffs (month → the set-off journal's source key, 'gst-setoff:<month>@<date>' as in the prototype). */
export const setoffsFromServer=(rows:SrvGstSetoff[]):Record<string,string>=>Object.fromEntries(rows.map(r=>[r.month,r.sourceKey]));

async function loadGst(c:LoaderCtx):Promise<ACCState>{
  const cur:ACCState={...(c.current('accountingX')??{})};
  const r=await c.api.get<SrvMonthLocks>('/accounting/gst/locks');
  cur.monthLocks=monthLocksFromServer(r.locks);cur.gstSetoffs=setoffsFromServer(r.setoffs);
  return cur;
}
/** Patch loader for accountingX — registered by the integrator (READS 'GET /accounting/gst/locks' + tests/_role-reads.json), see the header. */
export const accountingGstLoader:SliceLoader={slice:'accountingX',kind:'patch',requires:'reports',reads:['GET /accounting/gst/locks'],load:loadGst};

const paise=(v:unknown)=>Math.round(Number(v??0));
const txt=(v:unknown)=>String(v??'').trim();
const date=(v:unknown)=>{const d=txt(v);return d||null;};

export const accountingGst:SyncArea={
  id:'accounting-gst',
  loaders:[accountingGstLoader], // ACC-2 integration: READS 'GET /accounting/gst/locks'
  commands:[
    {type:'ACC_GST_SETOFF',reload:['accountingX'],precheck:month,
      async send(c){await c.api.command('PostGstSetoff',{month:txt(c.cmd.month)});}},
    {type:'ACC_GST_PAY',reload:['accountingX'],precheck:cmd=>month(cmd)??(txt(cmd.challan).length>=3?undefined:'Enter the challan / CIN reference (3–40 characters).'),
      async send(c){await c.api.command('RecordGstPayment',{month:txt(c.cmd.month),amountPaise:paise(c.cmd.amount),method:txt(c.cmd.method),challan:txt(c.cmd.challan),
        date:date(c.cmd.date),rcmPaise:c.cmd.rcm==null?null:paise(c.cmd.rcm)});}},
    {type:'ACC_STAT_REMIT',reload:['accountingX','complianceX'],precheck:cmd=>month(cmd)??(['pf','esic','pt'].includes(txt(cmd.kind))?undefined:'Choose PF, ESIC or professional tax.'),
      async send(c){await c.api.command('RecordStatutoryRemittance',{kind:txt(c.cmd.kind),month:txt(c.cmd.month),amountPaise:paise(c.cmd.amount),method:txt(c.cmd.method),
        challan:txt(c.cmd.challan),date:date(c.cmd.date)});}},
    {type:'ACC_MONTH_LOCK',reload:['accountingX','expensesX'],precheck:month,
      async send(c){await c.api.command('LockAccountingMonth',{month:txt(c.cmd.month)});}},
    {type:'ACC_MONTH_LOCK_REQUEST',reload:['accountingX'],precheck:month,
      async send(c){await c.api.command('RequestAccountingMonthLock',{month:txt(c.cmd.month)});}},
    {type:'ACC_MONTH_UNLOCK',reload:['accountingX','expensesX'],precheck:cmd=>month(cmd)??(txt(cmd.reason).length>=3?undefined:'A reason is required to unlock a month.'),
      async send(c){await c.api.command('UnlockAccountingMonth',{month:txt(c.cmd.month),reason:txt(c.cmd.reason)});}},
    {type:'ACC_GST_ADVANCES_SET',reload:['complianceX','accountingX'],
      async send(c){await c.api.command('SetGstOnAdvances',{on:c.cmd.on===true});}},
  ],
};
