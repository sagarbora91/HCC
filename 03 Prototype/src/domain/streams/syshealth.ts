// Wave C3 · System health (audit A09-05 + fix wave A "Left open"): parked outbox events (a server consumer failed an event 5 times and
// set it aside), the retry, and the last backup / restore-drill results. Server: GET /admin/system-health + RetryParkedEvent
// (PERM-SYS-001 = Owner + Tech admin; the prototype gate is the coarse 'admin' permission, which the same two roles hold).
// The demo build has no outbox and no backups: it shows one fictional parked event derived from the simulated clock and the simulated
// backup history (housekeeping.ts); SYS_RETRY_PARKED removes it there (as the server's next poll delivers a fixed event). The production
// build never shows demo values: without the loaded server slice the panel is empty.
// No guest data on this page: consumer, event type, attempts, failure times and the error TYPE only (F-L07).
import type {State} from '../types';
import {isProductionBuild} from '../build';
import {addDays,addMinutes} from '../money';
import {hkBackupStatus} from './housekeeping';

/** One parked event. Times are in the prototype clock format (IST, YYYY-MM-DDTHH:mm). */
export type SYSParked={consumer:string;label:string;outboxId:string;eventType:string;attempts:number;errorType:string;firstFailedAt:string;lastFailedAt:string;parkedAt:string};
/** One recorded backup or restore drill. kind = Full | Differential | PreMigration | RestoreDrill (demo: Scheduled | Manual). */
export type SYSBackupRun={kind:string;ok:boolean;at:string;file?:string;sizeBytes?:number;reason?:string};
/** Stored slice: the server's answer (source 'server'), or the demo's remaining parked events after a retry (source 'demo'). */
export type SYSState={source:'server'|'demo';parked:SYSParked[];lastBackup?:SYSBackupRun;lastOkBackupAt?:string;lastDrill?:SYSBackupRun;
  /** Windows Event Log source HCC on the server PC: on | off | unavailable (not Windows / demo). */
  eventLog?:'on'|'off'|'unavailable';checkedAt?:string};
export type SYSView=Required<Pick<SYSState,'source'|'parked'>>&Omit<SYSState,'source'|'parked'>&{eventLog:'on'|'off'|'unavailable'};

/** The shipped server consumers (migration 2900) as people call them (same table as SystemHealthService.ConsumerLabels). */
export const SYS_CONSUMER_LABELS:Record<string,string>={
  'accounting-vouchers':'Accounting vouchers','housekeeping-tasks':'Check-out cleaning tasks','housekeeping-stayover':'Stay-over cleaning tasks',
  'messaging-triggers':'Guest messages','ota-channel-manager':'OTA channel manager',
};
export const sysConsumerLabel=(consumer:string)=>SYS_CONSUMER_LABELS[consumer]??consumer;
/** What a parked event of each consumer holds up (shown under the label). */
export const SYS_CONSUMER_EFFECT:Record<string,string>={
  'accounting-vouchers':'A voucher is missing; the accounting date cannot be locked until it is booked.',
  'housekeeping-tasks':'A check-out cleaning task was not created.','housekeeping-stayover':'A stay-over cleaning task was not created.',
  'messaging-triggers':'A guest message was not prepared.','ota-channel-manager':'A channel update was not sent.',
};
/** Failed attempts after which the server parks an event (OutboxConsumer.ParkAfterFailures). */
export const SYS_PARK_AFTER=5;
/** The accounting exception for parked accounting events (server VoucherBuilder.StuckTitle / Exceptions, word for word). */
export const SYS_STUCK_TITLE='Accounting events stuck — see System health';
export const sysStuckDetail=(n:number)=>`${n} posting event(s) for this property could not be booked after repeated attempts and were set aside. The Owner or the Tech admin can retry them in Admin & Setup › System health; this date cannot be locked until they are booked.`;
export const sysParkedKey=(p:Pick<SYSParked,'consumer'|'outboxId'>)=>p.consumer+'/'+p.outboxId;

/** The demo's one parked event: a guest-message trigger that failed five times this morning (fictional; SQL error type only). */
export function sysDemoParked(s:State):SYSParked[]{
  const last=addMinutes(s.clock,-35);
  return [{consumer:'messaging-triggers',label:sysConsumerLabel('messaging-triggers'),outboxId:'48213',eventType:'ReservationConfirmed',attempts:SYS_PARK_AFTER,
    errorType:'SqlException 547',firstFailedAt:addMinutes(s.clock,-43),lastFailedAt:last,parkedAt:last}];
}
/** The demo's backup part: the simulated backup history (Backup & recovery page) and last Sunday's simulated restore drill. */
function demoBackups(s:State):Pick<SYSView,'lastBackup'|'lastOkBackupAt'|'lastDrill'>{
  const b=hkBackupStatus(s);
  const lastBackup=b.latest?{kind:b.latest.kind,ok:b.latest.result==='Succeeded',at:b.latest.at,...(b.latest.result==='Failed'?{reason:b.latest.note}:{})}:undefined;
  const dow=new Date(s.date+'T12:00:00Z').getUTCDay(),sunday=addDays(s.date,-(dow===0&&s.clock<s.date+'T03:30'?7:dow));
  return {lastBackup,lastOkBackupAt:b.last?.at,lastDrill:{kind:'RestoreDrill',ok:true,at:sunday+'T03:30'}};
}
const EMPTY_SERVER:SYSView={source:'server',parked:[],eventLog:'unavailable'};
/** What the System health page shows. Production: the server's answer only (empty until loaded). Demo: the stored slice, else the demo event. */
export function sysHealthView(s:State):SYSView{
  const x=s.sysHealthX;
  if(isProductionBuild()||x?.source==='server')return x?{...x,eventLog:x.eventLog??'unavailable'}:EMPTY_SERVER;
  return {source:'demo',parked:x?.parked??sysDemoParked(s),...demoBackups(s),eventLog:'unavailable'};
}
/** Parked events of the accounting consumer (block the accounting lock; the demo's accounting page shows them as "stuck"). */
export const sysParkedAccounting=(s:State)=>sysHealthView(s).parked.filter(p=>p.consumer==='accounting-vouchers').length;
/** Hours since the last successful backup on the simulated or server clock (undefined = none recorded). */
export function sysHoursSince(s:State,at?:string):number|undefined{
  if(!at)return undefined;
  return Math.max(0,Math.floor((Date.parse(s.clock+':00Z')-Date.parse(at+':00Z'))/3600000));
}
/** Text for one backup run: "Full · 26 Oct 02:30 · succeeded". */
export const sysRunResult=(r:SYSBackupRun)=>r.ok?(r.kind==='RestoreDrill'?'Passed':'Succeeded'):'Failed';
export const SYS_KIND_LABEL:Record<string,string>={Full:'Full backup',Differential:'Changes backup (differential)',PreMigration:'Safety backup before an upgrade',RestoreDrill:'Restore drill',Scheduled:'Scheduled backup (simulated)',Manual:'Manual backup (simulated)'};
export const sysKindLabel=(k:string)=>SYS_KIND_LABEL[k]??k;
export const SYS_EVENT_LOG_TEXT:Record<SYSView['eventLog'],string>={
  on:'On: the server and the backups write their alerts to the Windows Event Log (Application, source HCC).',
  off:'Off: the Event Log source HCC is not registered on the server PC. Run Install-HccService.ps1 -EventSourceOnly as administrator (INSTALL.md 7b).',
  unavailable:'Not available here (the server is not running on Windows, or this is the demo).',
};
