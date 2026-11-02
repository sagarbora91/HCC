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
import {dobSize,dobRefCheck} from './docobjects';
import type {SYSDatabase} from '../types';

/** One parked event. Times are in the prototype clock format (IST, YYYY-MM-DDTHH:mm). */
export type SYSParked={consumer:string;label:string;outboxId:string;eventType:string;attempts:number;errorType:string;firstFailedAt:string;lastFailedAt:string;parkedAt:string};
/** One recorded backup or restore drill. kind = Full | Differential | PreMigration | RestoreDrill (demo: Scheduled | Manual). */
export type SYSBackupRun={kind:string;ok:boolean;at:string;file?:string;sizeBytes?:number;reason?:string};
/** Stored slice: the server's answer (source 'server'), or the demo's remaining parked events after a retry (source 'demo'). */
export type SYSState={source:'server'|'demo';parked:SYSParked[];lastBackup?:SYSBackupRun;lastOkBackupAt?:string;lastDrill?:SYSBackupRun;
  /** Windows Event Log source HCC on the server PC: on | off | unavailable (not Windows / demo). */
  eventLog?:'on'|'off'|'unavailable';checkedAt?:string;
  /** LF §1.9: main + documents database rows. */ databases?:import('../types').SYSDatabase[];
  /** F4-T3: printers (test page, offline banner). Absent = the two demo printers. */ printers?:SYSPrinter[]};
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
/** SQL Server Express allows 50 GB of data per database (LF-CONTRACT §1.9). */
export const SYS_DB_LIMIT_BYTES=50*1024*1024*1024;
export const sysDbPct=(dataBytes:number)=>Math.min(100,Math.floor(dataBytes*100/SYS_DB_LIMIT_BYTES));
/** Bar colour thresholds of the Databases panel: amber at 70 %, red at 85 % of the limit. */
export const sysDbTone=(pct:number):'ok'|'warn'|'bad'=>pct>=85?'bad':pct>=70?'warn':'ok';
/** The demo's two fictional database rows: the main database (fictional size) and the documents database sized from the demo's docObjectsX slice. */
export function sysDemoDatabases(s:State):SYSDatabase[]{
  const z=dobSize(s),chk=dobRefCheck(s),back=demoBackups(s);
  const mainData=1_740_000_000,docData=64*1024*1024+Math.round(z.total*1.15);
  return [
    {name:'Hcc',kind:'main',schemaVersion:'4674',reachable:true,dataBytes:mainData,logBytes:128*1024*1024,limitBytes:SYS_DB_LIMIT_BYTES,pct:sysDbPct(mainData),
      ...(back.lastOkBackupAt?{lastBackup:back.lastOkBackupAt}:{}),...(back.lastDrill?{lastDrill:back.lastDrill.at}:{})},
    {name:'HccDocuments',kind:'documents',schemaVersion:'0002',reachable:true,dataBytes:docData,logBytes:72*1024*1024,limitBytes:SYS_DB_LIMIT_BYTES,pct:sysDbPct(docData),
      objectCount:z.count,contentBytes:z.total,danglingRefs:chk.dangling,orphanObjects:chk.orphans,
      ...(back.lastOkBackupAt?{lastBackup:back.lastOkBackupAt}:{}),...(back.lastDrill?{lastDrill:back.lastDrill.at}:{})},
  ];
}
/** "1.7 GB" / "64 MB" for the Databases panel. */
export function sysBytes(n:number):string{
  if(n>=1024**3)return (n/1024**3).toFixed(n>=10*1024**3?0:1)+' GB';
  if(n>=1024**2)return Math.round(n/1024**2)+' MB';
  return Math.max(0,Math.round(n/1024))+' KB';
}
const EMPTY_SERVER:SYSView={source:'server',parked:[],eventLog:'unavailable'};
/** What the System health page shows. Production: the server's answer only (empty until loaded). Demo: the stored slice, else the demo event. */
export function sysHealthView(s:State):SYSView{
  const x=s.sysHealthX;
  if(isProductionBuild()||x?.source==='server')return x?{...x,eventLog:x.eventLog??'unavailable'}:EMPTY_SERVER;
  return {source:'demo',parked:x?.parked??sysDemoParked(s),...demoBackups(s),eventLog:'unavailable',databases:sysDemoDatabases(s)};
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

// F4-T3 · printers block (F4-CONTRACT §4)
export type SYSPrinter={id:string;name:string;kind:'A4'|'thermal';online:boolean;lastTestAt?:string;lastTestOk?:boolean};
export const SYS_DEMO_PRINTERS:SYSPrinter[]=[{id:'prt-front',name:'Front desk (A4 laser)',kind:'A4',online:true},{id:'prt-kitchen',name:'Kitchen order printer (thermal)',kind:'thermal',online:true}];
export const sysPrinters=(s:State):SYSPrinter[]=>s.sysHealthX?.printers??SYS_DEMO_PRINTERS.map(p=>({...p}));
export const sysPrintersOffline=(s:State):SYSPrinter[]=>sysPrinters(s).filter(p=>!p.online);
/** The test page the "Print test page" button prints (also read by the server printer-test endpoint). */
export const sysTestPageText=(s:State,p:SYSPrinter)=>`${s.settings.hotel}\nPrinter test page\n${p.name}\n${s.clock.replace('T',' ')} IST\nIf you can read this, the printer works.`;
// F4-T3 end
