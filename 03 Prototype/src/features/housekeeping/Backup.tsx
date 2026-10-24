// SCR-19 Backup & recovery — simulation only (REQ-BKP-001). Every value derives from s.clock and the audit trail.
import React from 'react';
import {DatabaseBackup,TriangleAlert,CheckCircle2,Info} from 'lucide-react';
import {hkBackupHistory,hkLastBackup,HK_BACKUP_DESTINATION,HK_BACKUP_RETENTION,HK_RESTORE_STEPS} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,dateLabel,clockTime} from '../../components/ui';
const when=(at:string)=>`${dateLabel(at.slice(0,10))} · ${clockTime(at)} IST`;
export function Backup(){
  const {s,run,busy,permission}=useHcc();
  const rows=hkBackupHistory(s),last=hkLastBackup(s);
  const admin=permission('admin'); // PERM-ADM-005 backup.view / run (simulated)
  return <><PageTitle title="Backup & recovery" sub="How the hotel's records would be protected. Nothing on this page copies or restores data."
      action={<span className="hk-action"><button className="btn primary" disabled={busy||!admin} onClick={()=>run({type:'BACKUP_SIMULATE',key:'backup:'+s.sequence},'Simulated backup recorded in the audit trail. No data was copied.')}><DatabaseBackup size={16}/> Run simulated backup</button>{!admin&&<small className="hk-reason">Requires administrator access.</small>}</span>}/>
    <div className="hk-sim-banner" role="note"><TriangleAlert size={18}/><strong>Simulation — no real backup is taken.</strong><span>Real backup, off-device copies and restore drills are production scope (NFR-DR-001, AC-027).</span></div>
    <div className="stats hk-backup-stats">
      <div className="stat"><div className="stat-label">Last successful backup</div><strong className="hk-stat-text">{last?when(last.at):'None'}</strong><small>{last?.kind} · simulated</small></div>
      <div className="stat"><div className="stat-label">Destination</div><strong className="hk-stat-text">Office NAS</strong><small>{HK_BACKUP_DESTINATION}</small></div>
      <div className="stat"><div className="stat-label">Retention</div><strong className="hk-stat-text">14 d · 8 w · 12 m</strong><small>{HK_BACKUP_RETENTION}</small></div>
    </div>
    <div className="card"><div className="section-title"><div><h2>Backup history</h2><p>Scheduled runs are derived from the simulated clock; manual runs come from the audit trail.</p></div></div>
      <div className="table-wrap"><table className="hk-table"><thead><tr><th>Time</th><th>Type</th><th>Result</th><th>Note</th></tr></thead>
        <tbody>{rows.map(r=><tr key={r.at+r.kind}><td>{when(r.at)}</td><td>{r.kind}</td><td>{r.result==='Failed'?<Pill tone="orange"><TriangleAlert size={11}/> Failed</Pill>:<Pill tone="green"><CheckCircle2 size={11}/> Succeeded</Pill>}</td><td>{r.note}</td></tr>)}</tbody></table></div></div>
    <div className="card"><div className="section-title"><div><h2>Restore procedure</h2><p>Written procedure for the production system. It cannot be run from this prototype.</p></div></div>
      <ol className="hk-steps">{HK_RESTORE_STEPS.map(x=><li key={x}>{x}</li>)}</ol>
      <div className="padded"><div className="info-note"><Info size={16}/>A failed backup is retried at the next scheduled time and shown to the Owner on this screen. Restores always go to a separate environment first.</div></div></div>
  </>;
}
