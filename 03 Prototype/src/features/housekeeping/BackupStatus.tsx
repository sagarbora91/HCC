// DEC-063 G9: "last backup" warning card on the Overview for the Owner and the Manager. The Manager runs the hotel when the Owner
// is away and must know if backups stop, but has no Backup page (Administration stays Owner + Tech admin). Simulation only.
import React from 'react';
import {DatabaseBackup,TriangleAlert,ArrowRight} from 'lucide-react';
import {hkBackupStatus,rbacSeesBackupStatus} from '../../services/mock';
import {useHcc} from '../../app/context';
import {dateLabel,clockTime} from '../../components/ui';

export function backupStatusText(s:Parameters<typeof hkBackupStatus>[0]){
  const b=hkBackupStatus(s);
  const ago=b.hoursAgo===undefined?'No successful backup on record':`Last backup ${b.hoursAgo} hour${b.hoursAgo===1?'':'s'} ago`;
  const title=b.failed?'Backup failed — check the office NAS':b.stale?'Backup is overdue':ago;
  const sub=`${b.failed||b.stale?ago+' · ':''}${b.last?`${dateLabel(b.last.at.slice(0,10))} ${clockTime(b.last.at)} IST · `:''}${b.recentFailures?`${b.recentFailures} failed run${b.recentFailures===1?'':'s'} in the last 3 nights · `:''}simulated`;
  return {b,title,sub};
}
export function BackupStatusCard(){
  const {s,uid,go,permission}=useHcc();
  if(!rbacSeesBackupStatus(s,uid))return null;
  const {b,title,sub}=backupStatusText(s);
  return <section className={'card attention backup-status'+(b.warn?' warn':'')} aria-label="Backup status">
    <div className="attention-item"><span className={'attention-icon '+(b.warn?'orange':'blue')}>{b.warn?<TriangleAlert size={19}/>:<DatabaseBackup size={19}/>}</span>
      <div><strong>{title}</strong><p>{sub}</p>
        {permission('admin')?<button className="text-btn" onClick={()=>go('backup')}>Backup & recovery <ArrowRight size={14}/></button>
          :b.warn?<p className="muted">Tell the Owner — backups are managed on the Backup page (Owner / Tech admin).</p>:null}</div></div>
  </section>;
}
