// F7-R2 · Scheduled runs (my subscriptions + editor + run history) and the export log tabs of the Reports hub.
import React,{useState} from 'react';
import {Plus,Trash2,Play} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Empty,Pill,dateLabel} from '../../components/ui';
import {ScrollPanel} from '../../components/fit';
import type {RptDefinition,RptSchedule,RptFormat} from '../../domain/reports/types';
import {canExportReport} from './hub-engine';
import {visibleSchedules,visibleRuns,visibleExportLog,cadenceText,runTone,runText,recipientChoices,defaultParams,rptState} from './hub-logic';

type SchedFormat=Exclude<RptFormat,'screen'>;
const FORMATS:SchedFormat[]=['xlsx','pdf','csv'];

export function ScheduleEditor({defs,existing,onDone}:{defs:RptDefinition[];existing?:RptSchedule;onDone:()=>void}){
  const {s,uid,run,toast}=useHcc();
  const eligible=defs.filter(d=>canExportReport(s,uid,d)&&d.formats.some(f=>f!=='screen'));
  const [reportId,setReportId]=useState(existing?.reportId??eligible[0]?.id??'');
  const def=eligible.find(d=>d.id===reportId);
  const [format,setFormat]=useState<SchedFormat>(existing?.format??'xlsx');
  const [kind,setKind]=useState<RptSchedule['cadence']['kind']>(existing?.cadence.kind??'daily');
  const [day,setDay]=useState(existing?.cadence.day??1);const [time,setTime]=useState(existing?.cadence.time??'07:00');
  const [rec,setRec]=useState<string[]>(existing?.recipients??[uid]);const [enabled,setEnabled]=useState(existing?.enabled??true);
  const choices=def?recipientChoices(s,def):[];
  const fmts=def?FORMATS.filter(f=>def.formats.includes(f)):[];
  async function save(){
    if(!def||!rec.length)return;
    const ok=await run({type:'RPT_SCHEDULE_SAVE',id:existing?.id,reportId:def.id,params:existing?.params??defaultParams(def.params,s.date),format:fmts.includes(format)?format:fmts[0],
      cadence:{kind,day:kind==='daily'?undefined:day,time},recipients:rec.filter(r=>choices.some(c=>c.id===r)),enabled},'Schedule saved');
    if(ok)onDone();else toast('The schedule was not saved.',true);
  }
  if(!eligible.length)return <Empty title="Nothing to schedule" body="You have no report that can be exported."/>;
  return <form className="rpt-sched-form card" onSubmit={e=>{e.preventDefault();void save();}} aria-label="Schedule editor">
    <label>Report <select value={reportId} onChange={e=>setReportId(e.target.value)} disabled={!!existing}>{eligible.map(d=><option key={d.id} value={d.id}>{d.title}</option>)}</select></label>
    <label>Format <select value={format} onChange={e=>setFormat(e.target.value as SchedFormat)}>{fmts.map(f=><option key={f} value={f}>{f.toUpperCase()}</option>)}</select></label>
    <label>Repeat <select value={kind} onChange={e=>setKind(e.target.value as RptSchedule['cadence']['kind'])}><option value="daily">Every day</option><option value="weekly">Every week</option><option value="monthly">Every month</option></select></label>
    {kind==='weekly'&&<label>Day <select value={day} onChange={e=>setDay(Number(e.target.value))}>{['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'].map((n,i)=><option key={n} value={i}>{n}</option>)}</select></label>}
    {kind==='monthly'&&<label>Day of month <input type="number" min={1} max={28} value={day} onChange={e=>setDay(Math.min(28,Math.max(1,Number(e.target.value)||1)))}/></label>}
    <label>Time (IST) <input type="time" value={time} onChange={e=>setTime(e.target.value)}/></label>
    <fieldset><legend>Send to</legend>{choices.map(c=><label key={c.id} className="rpt-check"><input type="checkbox" checked={rec.includes(c.id)} onChange={e=>setRec(r=>e.target.checked?[...r,c.id]:r.filter(x=>x!==c.id))}/> {c.name} <small className="muted">{c.role}</small></label>)}
      {def?.moneyFlag&&<small className="muted">Money reports go only to the Owner, Manager and Accountant.</small>}</fieldset>
    <label className="rpt-check"><input type="checkbox" checked={enabled} onChange={e=>setEnabled(e.target.checked)}/> Switched on</label>
    <div className="modal-actions"><button type="button" className="btn" onClick={onDone}>Cancel</button><button className="btn primary" disabled={!def||!rec.length}>Save schedule</button></div>
  </form>;
}

export function ScheduledTab({defs}:{defs:RptDefinition[]}){
  const {s,uid,run}=useHcc();
  const [edit,setEdit]=useState<RptSchedule|'new'|null>(null);
  const scheds=visibleSchedules(s,uid),runs=visibleRuns(s,uid);
  const title=(id:string)=>defs.find(d=>d.id===id)?.title??id;
  return <ScrollPanel title="Scheduled runs" label="Scheduled runs" actions={<><button className="btn" onClick={()=>void run({type:'RPT_RUN_DUE'})}><Play size={15}/> Run due now</button>
      <button className="btn primary" onClick={()=>setEdit('new')}><Plus size={15}/> New schedule</button></>}>
    {edit&&<ScheduleEditor defs={defs} existing={edit==='new'?undefined:edit} onDone={()=>setEdit(null)}/>}
    <h3>My subscriptions</h3>
    {scheds.length===0?<Empty title="No schedules yet" body="Schedule a report to have it prepared every day, week or month."/>:
      <div className="table-wrap"><table><thead><tr><th>Report</th><th>When</th><th>Format</th><th>Status</th><th><span className="sr-only">Actions</span></th></tr></thead><tbody>
        {scheds.map(x=><tr key={x.id}><td>{title(x.reportId)}</td><td>{cadenceText(x.cadence)}</td><td>{x.format.toUpperCase()}</td><td><Pill tone={x.enabled?'green':'neutral'}>{x.enabled?'On':'Off'}</Pill></td>
          <td className="rpt-row-actions"><button className="btn" onClick={()=>setEdit(x)}>Edit</button><button className="btn" aria-label={'Delete schedule '+title(x.reportId)} onClick={()=>void run({type:'RPT_SCHEDULE_DELETE',id:x.id},'Schedule deleted')}><Trash2 size={15}/></button></td></tr>)}</tbody></table></div>}
    <h3>Run history</h3>
    {runs.length===0?<p className="muted">No runs yet.</p>:<div className="table-wrap"><table><thead><tr><th>Started</th><th>Report</th><th>Result</th><th className="number">Rows</th></tr></thead><tbody>
      {runs.slice(0,100).map(r=><tr key={r.id}><td>{dateLabel(r.startedAt.slice(0,10))} {r.startedAt.slice(11,16)}</td><td>{title(r.reportId)}</td><td><Pill tone={runTone(r)}>{runText(r)}</Pill>{r.error&&<small> {r.error}</small>}</td><td className="number">{r.rows??''}</td></tr>)}</tbody></table></div>}
  </ScrollPanel>;
}

export function ExportLogTab({defs}:{defs:RptDefinition[]}){
  const {s,uid}=useHcc();const log=visibleExportLog(s,uid);
  const title=(id:string)=>defs.find(d=>d.id===id)?.title??id;
  void rptState;
  return <ScrollPanel title="Export log" label="Export and print log" actions={<span className="muted">{log.length} entries</span>}>
    {log.length===0?<Empty title="Nothing exported yet" body="Every print and export is recorded here."/>:<div className="table-wrap"><table><thead><tr><th>When</th><th>Report</th><th>By</th><th>Kind</th><th className="number">Rows</th><th>File</th></tr></thead><tbody>
      {log.slice(0,200).map(e=><tr key={e.id}><td>{dateLabel(e.at.slice(0,10))} {e.at.slice(11,16)}</td><td>{title(e.reportId)}{e.provisional&&<small> provisional</small>}</td><td>{s.users.find(u=>u.id===e.by)?.name??e.by}</td><td>{e.format}</td><td className="number">{e.rows}</td><td>{e.fileName??''}</td></tr>)}</tbody></table></div>}
  </ScrollPanel>;
}
