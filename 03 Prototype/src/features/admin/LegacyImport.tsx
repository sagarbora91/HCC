// LG-UI · Legacy import (Owner, PERM-LGC-001), desktop. Master configuration › Data and system links here. Folder path, parts, dry run, Run;
// the server's LegacyImportResult is rendered as tables (status = colour AND text); the runs list is GET /legacy/runs.
// Domain logic stays on the server: this page only sends the contract's request and shows the answer.
import React,{useCallback,useEffect,useState} from 'react';
import {Lock,Play,CheckCircle2,TriangleAlert,FileUp} from 'lucide-react';
import {money} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty} from '../../components/ui';
import {LEGACY_PARTS,legacyImportClient,legacyRunBlock,legacyFigureIsMoney,legacyAllMatch,type LegacyPart,type LegacyImportResult,type LegacyRunRow} from './legacyImportModel';

/** PERM-LGC-001 is Owner only. */
export const useLegacyOwner=()=>{const {s,uid}=useHcc();return s.users.find(u=>u.id===uid)?.role==='Owner';};
export const legacyWhen=(iso:string)=>iso?iso.replace('T',' ').slice(0,16)+' UTC':'—';
export const legacyFigure=(figure:string,v:number)=>legacyFigureIsMoney(figure)?money(v):v.toLocaleString('en-IN');
export function MatchPill({ok}:{ok:boolean}){return ok?<Pill tone="green"><CheckCircle2 size={11}/> Match</Pill>:<Pill tone="acc-sev-error"><TriangleAlert size={11}/> Mismatch</Pill>;}

export function RunsTable({runs}:{runs:LegacyRunRow[]}){
  if(!runs.length)return <Empty title="No runs yet" body="Runs appear here after the first dry run or import."/>;
  return <div className="table-wrap"><table className="adm-table"><thead><tr><th>Run</th><th>When</th><th>Parts</th><th>Mode</th><th>Result</th></tr></thead><tbody>
    {runs.map(r=><tr key={r.runId}><td>{r.runId}{r.by&&<small>{r.by}</small>}</td><td>{legacyWhen(r.at)}</td><td>{r.parts.join(', ')||'—'}</td>
      <td>{r.dryRun?'Dry run':'Import'}</td><td>{r.committed?<Pill tone="green">Committed</Pill>:<Pill>Nothing written</Pill>}</td></tr>)}</tbody></table></div>;
}

export function LegacyResultView({r}:{r:LegacyImportResult}){
  const g=r.guests,c=r.cutover;
  const kv=(rows:[string,number][])=><div className="table-wrap"><table className="adm-table"><tbody>{rows.map(([k,v])=><tr key={k}><th scope="row">{k}</th><td className="number">{v.toLocaleString('en-IN')}</td></tr>)}</tbody></table></div>;
  const errs=[...g.errors,...r.history.errors];
  return <section className="card" aria-label="Import result">
    <div className="section-title"><div><h2>Result · run {r.runId}</h2><p>{r.dryRun?'Dry run: every check ran, nothing was written.':r.committed?'Committed.':'Not committed.'} Extractor {r.extractorVersion}.</p></div>
      <div className="inline"><Pill tone={r.dryRun?'blue':r.committed?'green':'orange'}>{r.dryRun?'Dry run':r.committed?'Committed':'Not committed'}</Pill>{r.history.perFy.length>0&&<MatchPill ok={legacyAllMatch(r)}/>}</div></div>
    {errs.length>0&&<div className="adm-lock padded" role="alert"><TriangleAlert size={14}/><span>{errs.join(' · ')}</span></div>}
    {r.parts.includes('guests')&&<><h3 className="gs-sub">Guests</h3>{kv([['Rows in the file',g.csvRows],['Matched to existing guests',g.matchedExisting],['Created',g.created],['Skipped (invalid phone)',g.skippedInvalidPhone],['Merged',g.merged],['ID numbers sealed',g.secretsSealed],['Dates of birth set',g.dobSet],['Photos copied',g.photosCopied],['ID proofs copied',g.idProofsCopied],['Tags added',g.tagsAdded]])}</>}
    {r.parts.includes('history')&&<><h3 className="gs-sub">History and reconciliation by financial year</h3>
      {r.history.perFy.length?<div className="table-wrap"><table className="adm-table"><thead><tr><th>FY</th><th>Stays</th><th>Rooms</th><th>Bills</th><th>Receipts</th><th>Duplicates dropped</th><th>Outside FY</th><th>Net</th><th>Receipts total</th><th>Check</th></tr></thead><tbody>
        {r.history.perFy.map(f=><tr key={f.fy}><td><strong>{f.fy}</strong></td><td className="number">{f.stays}</td><td className="number">{f.segments}</td><td className="number">{f.bills}</td><td className="number">{f.receipts}</td><td className="number">{f.duplicatesDropped}</td><td className="number">{f.outOfFy}</td>
          <td className="number">{money(f.reconciliation.netPaise)}</td><td className="number">{money(f.reconciliation.receiptsPaise)}</td><td><MatchPill ok={f.reconciliation.match}/></td></tr>)}</tbody></table></div>:<p className="muted padded">No history rows.</p>}
      {r.history.perFy.filter(f=>!f.reconciliation.match).map(f=><div key={f.fy}><h3 className="gs-sub">Differences · {f.fy} (this year was rolled back)</h3>
        <div className="table-wrap"><table className="adm-table"><thead><tr><th>Figure</th><th>Expected</th><th>Actual</th></tr></thead><tbody>
          {f.reconciliation.differences.map(d=><tr key={d.figure}><td>{d.figure}</td><td className="number">{legacyFigure(d.figure,d.expected)}</td><td className="number">{legacyFigure(d.figure,d.actual)}</td></tr>)}</tbody></table></div></div>)}</>}
    {r.parts.includes('cutover')&&<><h3 className="gs-sub">Cut-over</h3>{kv([['Customer opening rows',c.customerOpeningRows],['Receivables',c.receivablesRows],['Advances',c.advancesRows],['In-house guests (hand re-entry)',c.inhouseRows]])}{c.note&&<p className="muted padded">{c.note}</p>}</>}
    {r.warnings.length>0&&<><h3 className="gs-sub">Warnings ({r.warnings.length})</h3><ul className="padded">{r.warnings.map((w,i)=><li key={i}>{w}</li>)}</ul></>}
  </section>;
}

export function LegacyImport(){
  const {toast}=useHcc();
  const owner=useLegacyOwner();
  const client=legacyImportClient();
  const [folder,setFolder]=useState('');const [parts,setParts]=useState<LegacyPart[]>(['guests','history','cutover']);const [dry,setDry]=useState(true);
  const [totals,setTotals]=useState('');const [totalsName,setTotalsName]=useState('');
  const [result,setResult]=useState<LegacyImportResult>();const [runs,setRuns]=useState<LegacyRunRow[]>([]);const [work,setWork]=useState(false);const [err,setErr]=useState('');
  const reload=useCallback(()=>{client.runs().then(setRuns).catch(()=>setRuns([]));},[client]);
  useEffect(()=>{if(owner)reload();},[owner,reload]);
  if(!owner)return <><PageTitle title="Legacy import" sub="Load the old Lodge software's history."/>
    <section className="card"><div className="section-title"><h2>Legacy import</h2><Lock size={18}/></div><p className="adm-lock padded">Legacy import is for the Owner.</p></section></>;
  const block=legacyRunBlock(folder,parts,totals);
  const toggle=(p:LegacyPart)=>setParts(x=>x.includes(p)?x.filter(y=>y!==p):[...x,p]);
  const pick=async(f?:File)=>{if(!f)return;setTotals(await f.text());setTotalsName(f.name);};
  const go=async()=>{setWork(true);setErr('');try{const r=await client.run({folder:folder.trim(),parts,dryRun:dry,expectedTotals:totals});setResult(r);reload();toast(r.dryRun?'Dry run finished — nothing was written':'Import finished');}
    catch(e){setErr(e instanceof Error?e.message:'The run failed.');}finally{setWork(false);}};
  return <><PageTitle title="Legacy import" sub="Load guests, history and opening balances extracted from the old Lodge software. Start with a dry run." action={client.demo?<Pill tone="blue">Demo data</Pill>:undefined}/>
    <section className="card" aria-label="Run an import"><div className="section-title"><h2>Run</h2></div>
      <div className="padded">
        <label className="field"><span>Run folder on the server PC</span><input value={folder} onChange={e=>setFolder(e.target.value)} placeholder="E:\\Projects\\HCC\\Existing Software\\LG-EXTRACT\\20261105-2130" autoComplete="off"/></label>
        <fieldset className="field"><legend>Parts</legend>{LEGACY_PARTS.map(p=><label key={p.id} className="inline"><input type="checkbox" checked={parts.includes(p.id)} onChange={()=>toggle(p.id)}/><span><strong>{p.label}</strong> <small className="muted">{p.note}</small></span></label>)}</fieldset>
        <label className="field"><span><FileUp size={14}/> Expected totals (totals.json of the run){totalsName?': '+totalsName:''}</span><input type="file" accept=".json,application/json" onChange={e=>void pick(e.target.files?.[0])}/></label>
        <label className="inline"><input type="checkbox" checked={dry} onChange={e=>setDry(e.target.checked)}/><span><strong>Dry run</strong> <small className="muted">Check everything, write nothing</small></span></label>
        <div className="inline"><button type="button" className="btn primary" disabled={!!block||work} onClick={()=>void go()}><Play size={16}/>{work?'Running…':dry?'Run dry run':'Run import'}</button>{block&&<small className="adm-why">{block}</small>}</div>
        {err&&<p role="alert" className="adm-why">{err}</p>}
      </div></section>
    {result&&<LegacyResultView r={result}/>}
    <section className="card" aria-label="Runs"><div className="section-title"><h2>Runs</h2><span className="muted">{runs.length}</span></div><RunsTable runs={runs}/></section></>;
}
