// LG-UI · Legacy import — phone: read-only runs list (cards, 48 px targets). Running an import is done on the desktop.
import React,{useEffect,useState} from 'react';
import {Info} from 'lucide-react';
import {legacyImportClient,type LegacyRunRow} from '../../features/admin/legacyImportModel';
import {useLegacyOwner,legacyWhen} from '../../features/admin/LegacyImport';
import {MaTag,MaIntro,MaRows,MaH} from './parts';

export function MaLegacyImport(){
  const owner=useLegacyOwner();
  const [runs,setRuns]=useState<LegacyRunRow[]>([]);
  useEffect(()=>{if(owner)legacyImportClient().runs().then(setRuns).catch(()=>setRuns([]));},[owner]);
  if(!owner)return <div className="ma-page"><MaIntro>Legacy import is for the Owner.</MaIntro></div>;
  return <div className="ma-page">
    <MaIntro>Runs of the old-software import. This list is read only.</MaIntro>
    <MaH aside={<MaTag>{runs.length}</MaTag>}>Runs</MaH>
    {runs.length?runs.map(r=><article className="ma-card" key={r.runId} aria-label={'Run '+r.runId}>
      <div className="ma-card-top"><div><strong>{r.runId}</strong><span>{legacyWhen(r.at)}</span></div><MaTag tone={r.committed?'ok':'neutral'}>{r.committed?'Committed':'Nothing written'}</MaTag></div>
      <MaRows rows={[['Mode',r.dryRun?'Dry run':'Import'],['Parts',r.parts.join(', ')||'—']]}/></article>)
      :<article className="ma-card"><strong>No runs yet</strong></article>}
    <div className="ma-note"><Info size={18}/><span>To run a dry run or an import, open Legacy import on the desktop.</span></div>
  </div>;
}
