// M23 phone: Backup & recovery (simulation) and Roadmap — simple read-only cards from the same selectors as desktop.
import React from 'react';
import {TriangleAlert,Info,DatabaseBackup} from 'lucide-react';
import {hkBackupHistory,hkLastBackup,HK_BACKUP_DESTINATION,HK_BACKUP_RETENTION,HK_RESTORE_STEPS,HK_ROADMAP} from '../../services/mock';
import {useHcc} from '../../app/context';
import {dateLabel,clockTime} from '../../components/ui';
import {MaTag,MaIntro,MaRows} from './parts';

const when=(at:string)=>`${dateLabel(at.slice(0,10))} · ${clockTime(at)} IST`;
export function MaBackup(){
  const {s,run,busy,permission}=useHcc();
  const rows=hkBackupHistory(s),last=hkLastBackup(s);
  return <div className="ma-page">
    <div className="ma-warn" role="note"><TriangleAlert size={18}/><span><strong>Simulation — no real backup is taken.</strong> Real backup, off-device copies and restore drills are production scope.</span></div>
    <article className="ma-card"><strong>Status</strong><MaRows rows={[['Last successful backup',last?`${when(last.at)} · ${last.kind}`:'None'],['Destination',HK_BACKUP_DESTINATION],['Retention',HK_BACKUP_RETENTION]]}/>
      {permission('admin')&&<button type="button" className="btn" disabled={busy} onClick={()=>run({type:'BACKUP_SIMULATE',key:'backup:'+s.sequence},'Simulated backup recorded in the audit trail. No data was copied.')}><DatabaseBackup size={18}/>Run simulated backup</button>}</article>
    <h2 className="ma-h2">Backup history</h2>
    {rows.map(r=><article className="ma-card" key={r.at+r.kind}><div className="ma-card-top"><div><strong>{when(r.at)}</strong><span>{r.kind}</span></div><MaTag tone={r.result==='Failed'?'warn':'ok'}>{r.result==='Failed'?'Failed':'Succeeded'}</MaTag></div>{r.note&&<p className="ma-sub">{r.note}</p>}</article>)}
    <h2 className="ma-h2">Restore procedure</h2>
    <ol className="ma-card ma-ol">{HK_RESTORE_STEPS.map(x=><li key={x}>{x}</li>)}</ol>
    <div className="ma-note"><Info size={18}/><span>Restores always go to a separate environment first. Nothing on this page copies or restores data.</span></div>
  </div>;
}
export function MaRoadmap(){
  return <div className="ma-page">
    <MaIntro>Areas 13–26 are built in this prototype; anything that talks to an outside service is simulated. Production rollout follows the phases shown (PRD §3, DEC-02). Read only.</MaIntro>
    {HK_ROADMAP.map(r=><article className="ma-card" key={r.no} aria-label={`Area ${r.no}: ${r.area}`}>
      <div className="ma-card-top"><div><strong>{r.no} · {r.area}</strong></div><MaTag tone={r.phase==='Phase 2'?'info':'neutral'}>{r.phase}</MaTag></div>
      <MaRows rows={[['Prototype disposition',r.disposition],['Current build',r.current]]}/></article>)}
  </div>;
}
