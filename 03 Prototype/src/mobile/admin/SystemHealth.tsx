// Wave C3 · System health — phone (cards, 48 px targets). Same data and Retry as the desktop page (features/admin/SystemHealth.tsx).
import React from 'react';
import {RotateCcw,Info} from 'lucide-react';
import {sysHealthView,sysHoursSince,sysKindLabel,sysRunResult,sysBytes,SYS_CONSUMER_EFFECT,SYS_EVENT_LOG_TEXT,SYS_PARK_AFTER,type SYSBackupRun} from '../../services/mock';
import {useHcc} from '../../app/context';
import {sysWhen,sysRetryCommand,SYS_RETRY_DONE} from '../../features/admin/SystemHealth';
import {MaTag,MaIntro,MaRows,MaH} from './parts';

const runText=(r?:SYSBackupRun)=>r?`${sysRunResult(r)} · ${sysKindLabel(r.kind)} · ${sysWhen(r.at)}${r.reason?' · '+r.reason:''}`:'None recorded yet';

export function MaSystemHealth(){
  const {s,run,busy,permission}=useHcc();
  if(!permission('admin'))return <div className="ma-page"><MaIntro>System health is for the Owner and the Tech admin.</MaIntro></div>;
  const v=sysHealthView(s),hours=sysHoursSince(s,v.lastOkBackupAt);
  return <div className="ma-page">
    <MaIntro>Background work that is stuck, and the last backups.{v.source==='demo'?' Demo data.':''} No guest data is shown here.</MaIntro>
    <MaH aside={<MaTag tone={v.parked.length?'warn':'ok'}>{v.parked.length} parked</MaTag>}>Parked events</MaH>
    {v.parked.length?v.parked.map(p=><article className="ma-card" key={p.consumer+'/'+p.outboxId} aria-label={`Parked event ${p.outboxId}`}>
      <div className="ma-card-top"><div><strong>{p.label}</strong><span>{p.eventType} · No. {p.outboxId}</span></div><MaTag tone="warn">{p.attempts} attempts</MaTag></div>
      <p className="ma-sub">{SYS_CONSUMER_EFFECT[p.consumer]??p.consumer}</p>
      <MaRows rows={[['Last failure',sysWhen(p.lastFailedAt)],['First failure',sysWhen(p.firstFailedAt)],['Error type',p.errorType]]}/>
      <button type="button" className="btn" disabled={busy} onClick={()=>run(sysRetryCommand(p),SYS_RETRY_DONE)}><RotateCcw size={18}/>Retry</button>
    </article>):<article className="ma-card"><strong>Nothing parked</strong><p className="ma-sub">Every background event was processed.</p></article>}
    <div className="ma-note"><Info size={18}/><span>Each parked event was tried {SYS_PARK_AFTER} times. Fix the cause with your technical person, then Retry: it is tried once more and comes back here if it fails again.</span></div>
    {v.databases&&v.databases.length>0&&<><MaH>Databases</MaH>
      {v.databases.map(d=><article className="ma-card" key={d.name} aria-label={`Database ${d.name}`}>
        <div className="ma-card-top"><div><strong>{d.name}</strong><span>{d.kind==='documents'?'Photos and documents':'Everything else'} · version {d.schemaVersion}</span></div>
          <MaTag tone={!d.reachable||d.pct>=85?'warn':d.pct>=70?'warn':'ok'}>{d.reachable?`${d.pct} %`:'Not reached'}</MaTag></div>
        <MaRows rows={[['Size',`${sysBytes(d.dataBytes)} of 50 GB`],...(d.objectCount!==undefined?[['Files',`${d.objectCount} · ${sysBytes(d.contentBytes??0)}`] as [string,string]]:[]),
          ['Last backup',d.lastBackup?sysWhen(d.lastBackup):'None yet']]}/></article>)}</>}
    <MaH aside={hours!==undefined&&hours>26?<MaTag tone="warn">{hours} h ago</MaTag>:undefined}>Backups</MaH>
    <article className="ma-card"><MaRows rows={[
      ['Last backup',runText(v.lastBackup)],
      ['Last good backup',v.lastOkBackupAt?`${sysWhen(v.lastOkBackupAt)}${hours!==undefined?` · ${hours} h ago`:''}`:'None recorded yet'],
      ['Last restore drill',runText(v.lastDrill)],
      ['Windows Event Log',SYS_EVENT_LOG_TEXT[v.eventLog]]]}/></article>
  </div>;
}
