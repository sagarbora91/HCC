// MA-8 · Import / export of masters (plan Part 2 §8 M30). Three pieces, one file:
//   MstIoPanel      — the "Masters export and import" box of Master configuration › Data and system (desktop and phone): export every list
//                     you may change as JSON (no secrets, no ID numbers, no ids), import such a file (modal 'mst-import').
//   MstImportSheet  — modal 'mst-import': choose / paste the JSON, see the dry-run preview (what is added, changed, unchanged, skipped and why),
//                     then Import (command MST_IMPORT: all or nothing).
//   MstPasteSheet   — modal 'mst-paste' (id = master id): "Paste rows" quick-add on a MasterList — CSV text → rows through the master's FieldSpec,
//                     preview with per-row errors, then Add. The same columns as the server import templates; "Copy CSV" gives the list's own rows.
import React,{useMemo,useState} from 'react';
import {Download,Upload,ClipboardPaste,Copy,AlertTriangle,FileText} from 'lucide-react';
import {mstSpec,mstCanEdit,mstToCsv,mstRowsFromCsv,mstCsvColumns,mstExport,mstSetsFromExport,mstImportPreview,mstRows,masterSpecs,type MstImportReport,type MstIoSet,type MstImportItem} from '../../services/mock';
import {useHcc} from '../../app/context';
import {saveTextFile,nativeFileName} from '../../app/native';
import {Pill} from '../../components/ui';

const TONE:Record<MstImportItem['action'],string>={create:'green',update:'blue',unchanged:'neutral',skipped:'orange',error:'red'};
const WORD:Record<MstImportItem['action'],string>={create:'Add',update:'Change',unchanged:'Unchanged',skipped:'Skipped',error:'Refused'};

/** Per-row result list (preview): one line per row with its outcome; unchanged rows are folded into a count. */
function Items({items,lineOf}:{items:MstImportItem[];lineOf?:(i:MstImportItem)=>number|undefined}){
  const shown=items.filter(i=>i.action!=='unchanged'),same=items.length-shown.length;
  return <>
    {shown.length>0&&<ul className="mst-io-rows">{shown.slice(0,200).map((i,n)=><li key={i.master+i.code+n} className={'mst-io-row '+i.action}>
      <Pill tone={TONE[i.action]}>{WORD[i.action]}</Pill>
      <span><strong>{i.code||'(no code)'}</strong>{i.name&&i.name!==i.code?` · ${i.name}`:''}{lineOf?.(i)?<small> · line {lineOf(i)}</small>:null}<small className="mst-io-detail">{i.detail}</small></span></li>)}</ul>}
    {shown.length>200&&<p className="muted">… and {shown.length-200} more.</p>}
    {same>0&&<p className="muted">{same} row{same===1?'':'s'} already match what is in HCC.</p>}
  </>;
}

function Summary({r}:{r:MstImportReport}){
  const c=r.counts;
  return <ul className="mst-io-counts"><li><strong>{c.create}</strong> to add</li><li><strong>{c.update}</strong> to change</li><li><strong>{c.unchanged}</strong> unchanged</li>
    {c.skipped>0&&<li><strong>{c.skipped}</strong> skipped</li>}{c.errors>0&&<li className="bad"><strong>{c.errors}</strong> refused</li>}</ul>;
}

const copyText=async(text:string):Promise<boolean>=>{try{await navigator.clipboard.writeText(text);return true;}catch{return false;}};

// ───────────────────────────── Paste rows ─────────────────────────────
export function MstPasteSheet({id}:{id?:string}){
  const {s,uid,run,modal,closeModal,busy,toast}=useHcc();
  const spec=mstSpec(String(id??''));
  const [text,setText]=useState('');
  const can=spec?mstCanEdit(s,uid,spec):{ok:false,reason:'List not found.'};
  const parsed=useMemo(()=>spec&&text.trim()?mstRowsFromCsv(s,spec,text):undefined,[s,spec,text]);
  const sets:MstIoSet[]|undefined=spec&&parsed&&!parsed.errors.length?[{master:spec.id,source:'csv',rows:parsed.rows.map(x=>x.row)}]:undefined;
  const report=useMemo(()=>sets&&can.ok?mstImportPreview(s,uid,sets):undefined,[s,uid,sets,can.ok]);
  if(!spec)return <p>List not found.</p>;
  const cols=mstCsvColumns(spec);
  const lineOf=(i:MstImportItem)=>i.line?parsed?.rows[i.line-1]?.line:undefined;
  const blocked=!can.ok||!report||report.counts.errors>0||report.counts.create+report.counts.update===0;
  return <><div className="modal-heading"><div className="eyebrow">{spec.label.toUpperCase()}</div><h2>Paste rows</h2>
    <p>Paste rows from a spreadsheet or a CSV file. The first line is the column names — the same columns as the server import files. Each row is checked like the {spec.singular} form; a known code changes that row, a new code adds one. Nothing changes until you press the button.</p></div>
    <form className="form mst-io" onSubmit={e=>{e.preventDefault();if(!blocked&&sets)void run({type:'MST_IMPORT',sets,key:modal?.key??'mst-paste:'+s.sequence},`${report!.counts.create} added · ${report!.counts.update} changed`,{close:true});}}>
      {!can.ok&&<p className="mst-lock" role="note">{can.reason}</p>}
      <p className="muted mst-io-cols">Columns: <code>{cols.join(', ')}</code></p>
      <div className="cfg-actions">
        <button type="button" className="btn" onClick={()=>setText(cols.join(',')+'\n')}><FileText size={15}/> Start from the column names</button>
        <button type="button" className="btn" onClick={async()=>{const csv=mstToCsv(s,spec);const ok=await copyText(csv);if(!ok){const r=await saveTextFile(nativeFileName(spec.id,'csv',s.date),csv,'text/csv',true);toast(r==='failed'?'Could not copy or save':'CSV saved',r==='failed');}else toast(`${mstRows(s,spec.id).length} rows copied as CSV`);}}><Copy size={15}/> Copy this list as CSV</button>
      </div>
      <label>Rows (CSV text)<textarea rows={8} value={text} onChange={e=>setText(e.target.value)} spellCheck={false} placeholder={cols.join(',')} aria-label="Rows as CSV text"/></label>
      {parsed&&parsed.errors.length>0&&<div className="adm-impact" role="alert"><h3><AlertTriangle size={14}/> {parsed.errors.length} problem{parsed.errors.length===1?'':'s'} — fix the text first</h3>
        <ul>{parsed.errors.slice(0,30).map((e,i)=><li key={i}>{e.line?`Line ${e.line}`:'Text'}{e.column?` · ${e.column}`:''}: {e.message}</li>)}</ul></div>}
      {parsed&&parsed.ignored.length>0&&<p className="adm-warn"><AlertTriangle size={14}/> Ignored column{parsed.ignored.length===1?'':'s'}: {parsed.ignored.join(', ')}.</p>}
      {report&&<div className="adm-impact" aria-live="polite"><h3>Preview</h3><Summary r={report}/><Items items={report.items} lineOf={lineOf}/>
        {report.skippedMasters.map(x=><p key={x.master} className="adm-warn"><AlertTriangle size={14}/> {x.reason}</p>)}</div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
        <button className="btn primary" disabled={busy||blocked}><ClipboardPaste size={16}/> {report?`Add ${report.counts.create} · change ${report.counts.update}`:'Add rows'}</button></div>
    </form></>;
}

// ───────────────────────────── Import masters (JSON) ─────────────────────────────
export function MstImportSheet(){
  const {s,uid,run,modal,closeModal,busy}=useHcc();
  const [text,setText]=useState('');const [name,setName]=useState('');
  const parsed=useMemo(()=>{if(!text.trim())return undefined;try{return {data:JSON.parse(text) as unknown};}catch{return {error:'This file is not valid JSON.'};}},[text]);
  const read=useMemo(()=>parsed&&'data' in parsed?mstSetsFromExport(parsed.data):undefined,[parsed]);
  const report=useMemo(()=>read&&!read.errors.length&&read.sets.length?mstImportPreview(s,uid,read.sets):undefined,[s,uid,read]);
  const onFile=(f:File|undefined)=>{if(!f)return;setName(f.name);f.text().then(setText).catch(()=>setText(''));};
  const blocked=!report||report.counts.errors>0||report.counts.create+report.counts.update===0;
  return <><div className="modal-heading"><div className="eyebrow">DATA AND SYSTEM</div><h2>Import masters</h2>
    <p>Choose a masters export (JSON). Every row is checked like the list's own form and matched by code: a new code adds a row, a known code changes it. A file never retires a row. Lists you may not change (money lists need the Owner) are skipped with the reason. All or nothing: one refused row stops the import.</p></div>
    <form className="form mst-io" onSubmit={e=>{e.preventDefault();if(!blocked&&read)void run({type:'MST_IMPORT',sets:read.sets,key:modal?.key??'mst-import:'+s.sequence},`${report!.counts.create} added · ${report!.counts.update} changed`,{close:true});}}>
      <label>Export file<input type="file" accept="application/json,.json" onChange={e=>onFile(e.target.files?.[0])}/></label>
      <label>…or paste the JSON<textarea rows={4} value={text} onChange={e=>{setText(e.target.value);setName('');}} spellCheck={false}/></label>
      {name&&<p className="muted">{name}</p>}
      {parsed&&'error' in parsed&&<p className="adm-err" role="alert">{parsed.error}</p>}
      {read?.errors.map((e,i)=><p key={i} className="adm-err" role="alert">{e}</p>)}
      {read&&read.skipped.length>0&&<p className="adm-warn"><AlertTriangle size={14}/> Not in this version: {read.skipped.map(x=>x.master).join(', ')}.</p>}
      {report&&<div className="adm-impact" aria-live="polite"><h3>Preview</h3><Summary r={report}/>
        {report.skippedMasters.length>0&&<details><summary>{report.skippedMasters.length} list{report.skippedMasters.length===1?'':'s'} skipped</summary><ul>{report.skippedMasters.map(x=><li key={x.master}>{mstSpec(x.master)?.label??x.master} — {x.reason}</li>)}</ul></details>}
        <details open={report.counts.errors>0||report.counts.create+report.counts.update<=12}><summary>Rows</summary>
          {masterSpecs().filter(sp=>report.items.some(i=>i.master===sp.id&&i.action!=='unchanged')).map(sp=><div key={sp.id}><h4 className="mst-io-h">{sp.label}</h4><Items items={report.items.filter(i=>i.master===sp.id)}/></div>)}</details></div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
        <button className="btn primary" disabled={busy||blocked}><Upload size={16}/> Import</button></div>
    </form></>;
}

// ───────────────────────────── Data and system panel ─────────────────────────────
export function MstIoPanel(){
  const {s,uid,open,toast,permission}=useHcc();
  const e=useMemo(()=>mstExport(s,uid),[s,uid]);
  const count=Object.keys(e.masters).length;
  const canImport=permission(['masters','admin']);
  const doExport=async()=>{const r=await saveTextFile(nativeFileName('Masters-export','json',s.date),JSON.stringify(mstExport(s,uid),null,2),'application/json');
    toast(r==='failed'?'Export failed — try again':`${count} lists exported (no secrets, no ID numbers)`,r==='failed');};
  return <section className="cfg-box" aria-label="Masters export and import"><h3>Masters export and import</h3>
    <p className="muted">All the lists of Master configuration (floors, room types, meal plans, payment methods, booking sources, shift patterns, …) as one JSON file — for a backup or to set up a second property. Rows carry codes, not ids; ID numbers and secrets are never included.{e.excluded.some(x=>!x.includes('never'))?' Lists you may not change are left out (money lists are the Owner’s).':''} Single lists also take pasted rows (“Paste rows” on each list).</p>
    <div className="cfg-actions"><button type="button" className="btn" disabled={!count} onClick={()=>void doExport()}><Download size={16}/> Export masters ({count} lists)</button>
      <button type="button" className="btn" disabled={!canImport} onClick={()=>open({type:'mst-import'})}><Upload size={16}/> Import masters…</button></div>
    {!canImport&&<small className="adm-why">Importing needs the masters permission (Owner and Tech admin).</small>}
    {count===0&&<small className="adm-why">You may not change any list.</small>}
  </section>;
}

