// Wave C3 · System health (audit A09-05 + fix wave A "Left open"), desktop. Owner and Tech admin ('admin'; server PERM-SYS-001):
// parked outbox events with Retry (SYS_RETRY_PARKED → RetryParkedEvent), the last backup, the last good backup and the last restore drill,
// and whether the Windows Event Log source HCC is registered. No guest data: consumer, event type, attempts, times, error TYPE only.
import React from 'react';
import {Activity,RotateCcw,TriangleAlert,CheckCircle2,Lock} from 'lucide-react';
import {sysHealthView,sysHoursSince,sysKindLabel,sysRunResult,SYS_CONSUMER_EFFECT,SYS_EVENT_LOG_TEXT,SYS_PARK_AFTER,type SYSParked,type SYSBackupRun} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty,dateLabel,clockTime} from '../../components/ui';

export const sysWhen=(at:string)=>`${dateLabel(at.slice(0,10))} · ${clockTime(at)} IST`;
/** Idempotency key of one retry click: changes when the event failed again (attempts), so a later retry is a new command. */
export const sysRetryKey=(p:SYSParked)=>`sys-retry:${p.consumer}/${p.outboxId}/${p.attempts}`;
export const sysRetryCommand=(p:SYSParked)=>({type:'SYS_RETRY_PARKED',consumer:p.consumer,outboxId:p.outboxId,key:sysRetryKey(p)});
export const SYS_RETRY_DONE='Released for one more try. It leaves this list; if it fails again it comes back here.';

function RunText({r}:{r?:SYSBackupRun}){
  if(!r)return <span className="muted">None recorded yet</span>;
  return <>{r.ok?<Pill tone="green"><CheckCircle2 size={11}/> {sysRunResult(r)}</Pill>:<Pill tone="orange"><TriangleAlert size={11}/> {sysRunResult(r)}</Pill>} {sysKindLabel(r.kind)} · {sysWhen(r.at)}
    {r.reason&&<small className="adm-wrap">Reason: {r.reason}</small>}{r.file&&<small className="adm-wrap">{r.file}</small>}</>;
}

export function SystemHealth(){
  const {s,run,busy,permission}=useHcc();
  if(!permission('admin'))return <><PageTitle title="System health" sub="Background work and backups of the server."/>
    <section className="card"><div className="section-title"><h2>System health</h2><Lock size={18}/></div><p className="adm-lock padded">System health is for the Owner and the Tech admin.</p></section></>;
  const v=sysHealthView(s),hours=sysHoursSince(s,v.lastOkBackupAt);
  return <><PageTitle title="System health" sub="Background work that is stuck, and the last backups. Nothing here shows guest data."
      action={v.source==='demo'?<Pill tone="blue"><Activity size={12}/> Demo data</Pill>:v.checkedAt?<Pill>Checked {clockTime(v.checkedAt)} IST</Pill>:undefined}/>
    <section className="card" aria-label="Parked events"><div className="section-title"><div><h2>Parked events</h2>
        <p>The server tried each of these {SYS_PARK_AFTER} times and set it aside so that later work keeps flowing. Fix the cause with your technical person, then press Retry: the next run tries it once more (if it fails again it is parked again at once).</p></div>
        <Pill tone={v.parked.length?'orange':'green'}>{v.parked.length} parked</Pill></div>
      {v.parked.length?<div className="table-wrap"><table className="adm-table"><thead><tr><th>Part of HCC</th><th>Event</th><th>Attempts</th><th>Last failure</th><th>Error type</th><th>Action</th></tr></thead><tbody>
        {v.parked.map(p=><tr key={p.consumer+'/'+p.outboxId}><td className="adm-wrap"><strong>{p.label}</strong><small>{SYS_CONSUMER_EFFECT[p.consumer]??p.consumer}</small></td>
          <td>{p.eventType}<small>No. {p.outboxId}</small></td><td className="number">{p.attempts}</td>
          <td>{sysWhen(p.lastFailedAt)}<small>first {sysWhen(p.firstFailedAt)}</small></td><td>{p.errorType}</td>
          <td><button className="btn small-btn" disabled={busy} onClick={()=>run(sysRetryCommand(p),SYS_RETRY_DONE)}><RotateCcw size={14}/> Retry</button></td></tr>)}
      </tbody></table></div>:<Empty title="Nothing parked" body="Every background event was processed."/>}
    </section>
    <section className="card" aria-label="Backups"><div className="section-title"><div><h2>Backups</h2><p>Recorded by the backup scripts on the server PC after every run.</p></div>
        {hours!==undefined&&hours>26?<Pill tone="orange">Last good backup {hours} h ago</Pill>:null}</div>
      <div className="table-wrap"><table className="adm-table"><tbody>
        <tr><th scope="row">Last backup</th><td className="adm-wrap"><RunText r={v.lastBackup}/></td></tr>
        <tr><th scope="row">Last good backup</th><td>{v.lastOkBackupAt?<>{sysWhen(v.lastOkBackupAt)}{hours!==undefined&&<small>{hours} hour{hours===1?'':'s'} ago</small>}</>:<span className="muted">None recorded yet</span>}</td></tr>
        <tr><th scope="row">Last restore drill</th><td className="adm-wrap"><RunText r={v.lastDrill}/></td></tr>
        <tr><th scope="row">Windows Event Log</th><td className="adm-wrap">{SYS_EVENT_LOG_TEXT[v.eventLog]}</td></tr>
      </tbody></table></div>
    </section></>;
}
