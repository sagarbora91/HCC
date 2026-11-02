// System health area (Wave C3: audit A09-05 + fix wave A "Left open") of the production sync layer.
//
//   server   GET /admin/system-health (PERM-SYS-001: Owner, Tech admin) → parked outbox events (consumer, event type, attempts, failure
//            times, error TYPE; never the payload or the error text), last backup / last good backup / last restore drill, Event Log state;
//            POST /commands/RetryParkedEvent {consumer, outboxId} (PERM-SYS-001; audited OUTBOX_PARKED_RETRY).
//   loaders  sysHealthX ← the panel (source 'server', times as the IST clock). Without PERM-SYS-001 the read is never sent (reads.ts).
//   commands SYS_RETRY_PARKED {consumer, outboxId} → RetryParkedEvent; reloads sysHealthX (the released event leaves the list at once;
//            the consumer's next poll delivers it, or parks it again with one more attempt).
import type {SYSState,SYSParked,SYSBackupRun} from '../../domain/streams/syshealth';
import type {SYSDatabase} from '../../domain/types';
import {HccClientError} from '../errors';
import type {SyncArea} from './types';
import {utcToClock} from './map';

/** Server wire shapes (08 Production/src/Hcc.Contracts/Health/SystemHealthContracts.cs). */
export type SrvParkedEvent={consumer:string;consumerLabel:string;outboxId:number;eventType:string;attempts:number;errorType:string;firstFailedUtc:string;lastFailedUtc:string;parkedUtc:string};
export type SrvBackupRun={kind:string;succeeded:boolean;startedUtc:string;finishedUtc:string;fileName:string|null;sizeBytes:number|null;reason:string|null};
/** LF-docdb (contract §1.9): one row of the Databases block. */
export type SrvDatabase={name:string;kind:string;schemaVersion:string;reachable:boolean;dataBytes:number;logBytes:number;limitBytes:number;pct:number;
  objectCount:number|null;contentBytes:number|null;danglingRefs:number|null;orphanObjects:number|null;lastBackupUtc:string|null;lastDrillUtc:string|null};
export type SrvSystemHealth={checkedUtc:string;parked:SrvParkedEvent[];lastBackup:SrvBackupRun|null;lastSuccessfulBackupUtc:string|null;lastRestoreDrill:SrvBackupRun|null;eventLog:string;databases?:SrvDatabase[]|null};
export function databaseFromServer(d:SrvDatabase):SYSDatabase{
  return {name:d.name,kind:d.kind==='documents'?'documents':'main',schemaVersion:d.schemaVersion,reachable:d.reachable,dataBytes:d.dataBytes,logBytes:d.logBytes,limitBytes:d.limitBytes,pct:d.pct,
    ...(d.objectCount!=null?{objectCount:d.objectCount}:{}),...(d.contentBytes!=null?{contentBytes:d.contentBytes}:{}),...(d.danglingRefs!=null?{danglingRefs:d.danglingRefs}:{}),
    ...(d.orphanObjects!=null?{orphanObjects:d.orphanObjects}:{}),...(d.lastBackupUtc?{lastBackup:utcToClock(d.lastBackupUtc)}:{}),...(d.lastDrillUtc?{lastDrill:utcToClock(d.lastDrillUtc)}:{})};
}

export function parkedFromServer(p:SrvParkedEvent):SYSParked{
  return {consumer:p.consumer,label:p.consumerLabel||p.consumer,outboxId:String(p.outboxId),eventType:p.eventType,attempts:p.attempts,errorType:p.errorType,
    firstFailedAt:utcToClock(p.firstFailedUtc),lastFailedAt:utcToClock(p.lastFailedUtc),parkedAt:utcToClock(p.parkedUtc)};
}
export function runFromServer(r:SrvBackupRun|null|undefined):SYSBackupRun|undefined{
  if(!r)return undefined;
  const out:SYSBackupRun={kind:r.kind,ok:r.succeeded,at:utcToClock(r.finishedUtc)};
  if(r.fileName)out.file=r.fileName;
  if(r.sizeBytes!==null&&r.sizeBytes!==undefined)out.sizeBytes=r.sizeBytes;
  if(r.reason)out.reason=r.reason;
  return out;
}
export function healthFromServer(h:SrvSystemHealth):SYSState{
  const out:SYSState={source:'server',parked:(h.parked??[]).map(parkedFromServer),checkedAt:utcToClock(h.checkedUtc),
    eventLog:h.eventLog==='on'||h.eventLog==='off'?h.eventLog:'unavailable'};
  const last=runFromServer(h.lastBackup),drill=runFromServer(h.lastRestoreDrill);
  if(last)out.lastBackup=last;
  if(h.lastSuccessfulBackupUtc)out.lastOkBackupAt=utcToClock(h.lastSuccessfulBackupUtc);
  if(drill)out.lastDrill=drill;
  if(h.databases?.length)out.databases=h.databases.map(databaseFromServer);
  return out;
}
/** RetryParkedEvent body from the prototype command (outbox id as the server's number). */
export function retryBody(cmd:Record<string,unknown>):{consumer:string;outboxId:number}{
  const consumer=String(cmd.consumer??'').trim(),id=String(cmd.outboxId??'').trim();
  if(!consumer||consumer.length>40)throw new HccClientError('VALIDATION','Choose the consumer of the parked event (1 to 40 characters).');
  if(!/^\d+$/.test(id)||Number(id)<=0)throw new HccClientError('VALIDATION','Choose the parked event (outbox id).');
  return {consumer,outboxId:Number(id)};
}

export const syshealth:SyncArea={
  id:'syshealth',
  loaders:[{slice:'sysHealthX',requires:'admin',reads:['GET /admin/system-health'],
    async load(c){return healthFromServer(await c.api.get<SrvSystemHealth>('/admin/system-health'));}}],
  commands:[
    {type:'SYS_RETRY_PARKED',reload:['sysHealthX'],
      async send(c){const res=await c.api.command('RetryParkedEvent',retryBody(c.cmd));return {result:res};}},
  ],
};
