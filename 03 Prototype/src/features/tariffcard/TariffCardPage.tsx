// F4-T1 · Page tariff-card (REVENUE & SALES; F4-CONTRACT §1.7): version strip, editor grid with "Enter as incl. GST" and the rounding
// difference, tabs Rooms · Meal plans & extras · Seasons & weekends · Halls (placeholder) · History · Print. Buttons by permission
// (PERM-TRF-001…003). Publishing writes the existing masters (never a second price list, DEC-MA-102).
import React,{useState} from 'react';
import {FilePlus2,Send,Upload,XCircle,Trash2,Printer,Download,FileUp} from 'lucide-react';
import {useHcc} from '../../app/context';
import {saveCsv,printDocument} from '../../app/native';
import {Pill,Empty} from '../../components/ui';
import {FitPage,FitTabs,FitNote,ScrollPanel,StatStrip,useFitTab} from '../../components/fit';
import {money,addDays} from '../../domain/money';
import {gstPct} from '../../domain/gst';
import type {TRCVersion,TRCStatus} from '../../domain/types';
import {trcVersions,trcVersion,trcSummary,trcDraftFromMasters,trcPublishPlan,TRC_DAY_NAMES} from '../../domain/streams/tariffcard';
import {TRC_TABS,TRC_TAB_IDS,trcTabLines,trcUiState,trcDefaultVersion,trcEditorRows,trcEditLine,trcParseRupees,type TrcTab} from './logic';
import {RateSheetDoc,rateSheetModel,rateSheetCsv,rateSheetHistoryCsv} from '../reports/rate-sheet';

export const TRC_STATUS_TONE:Record<TRCStatus,string>={draft:'neutral',proposed:'orange',published:'green',superseded:'neutral',rejected:'red'};
export function useTrcVersion():[TRCVersion|undefined,(id:string)=>void]{
  const {s,uid}=useHcc();const [id,setId]=useState<string>('');
  return [(id&&trcVersion(s,id))||trcDefaultVersion(s,uid),setId];
}
export function VersionStrip({value,onChange}:{value?:TRCVersion;onChange:(id:string)=>void}){
  const {s}=useHcc();const list=trcVersions(s);
  if(!list.length)return null;
  return <div className="trc-strip" role="group" aria-label="Card versions">{list.map(v=><button key={v.id} type="button" className={'trc-chip'+(v.id===value?.id?' on':'')} aria-pressed={v.id===value?.id} onClick={()=>onChange(v.id)}>
    <strong>{v.id}</strong><span>from {v.effectiveFrom}</span><Pill tone={TRC_STATUS_TONE[v.status]}>{v.status}</Pill></button>)}</div>;
}

export function TariffCardPage(){
  const {s,uid,run,busy,toast}=useHcc();
  const [v,setV]=useTrcVersion();const ui=trcUiState(s,uid,v);const sum=trcSummary(s);
  const [tab,setTab]=useFitTab<TrcTab>('tariff-card',TRC_TAB_IDS,'rooms');
  const [reason,setReason]=useState('');
  if(!ui.view)return <FitPage title="Tariff card"><Empty title="Not available" body={ui.readOnlyNote}/></FitPage>;
  const newDraft=async()=>{const from=addDays(s.date,1);if(await run({type:'TRC_DRAFT_SAVE',label:`Tariff card from ${from}`,effectiveFrom:from,lines:trcDraftFromMasters(s,from)},'Draft created from the prices in force'))setV('');};
  const importFiles=async(list:FileList|null)=>{if(!list?.length)return;const files:Record<string,string>={};
    for(const f of Array.from(list)){const n=f.name.toLowerCase(),t=await f.text();
      if(n.includes('room-types'))files.roomTypes=t;else if(n.includes('tariff'))files.tariffs=t;else if(n.includes('occupancy'))files.occupancyCharges=t;else if(n.includes('meal'))files.mealPlans=t;else if(n.includes('rooms'))files.rooms=t;}
    await run({type:'TRC_IMPORT',files,effectiveFrom:s.date>'2026-11-01'?addDays(s.date,1):'2026-11-01'},'Rate card imported as a draft');};
  let preview='';if(v&&(v.status==='draft'||v.status==='proposed')){try{const p=trcPublishPlan(s,v);preview=`${p.writes.length} master rows on publish${p.printOnly.length?` · ${p.printOnly.length} print only`:''}`;}catch(e){preview=(e as Error).message;}}
  const actions=<>
    {ui.canPublish&&<button className="btn primary" disabled={busy} onClick={()=>run({type:'TRC_PUBLISH',id:v!.id},`${v!.id} published`)}><Upload size={16}/> Publish</button>}
    {ui.canPropose&&<button className="btn primary" disabled={busy} onClick={()=>run({type:'TRC_PROPOSE',id:v!.id},'Sent to the Owner for approval')}><Send size={16}/> Propose</button>}
    {ui.canNewDraft&&<button className="btn" disabled={busy} onClick={()=>{void newDraft();}}><FilePlus2 size={16}/> New draft</button>}
    {ui.canImport&&<label className="btn"><FileUp size={16}/> Import CSVs<input type="file" accept=".csv,text/csv" multiple hidden onChange={e=>{void importFiles(e.target.files);e.target.value='';}}/></label>}
    {ui.canDiscard&&<button className="btn" disabled={busy} onClick={()=>run({type:'TRC_DISCARD',id:v!.id},'Draft discarded').then(ok=>ok&&setV(''))}><Trash2 size={16}/> Discard</button>}
    {!ui.canPublish&&!ui.canPropose&&!ui.canNewDraft&&<Pill>View only</Pill>}
  </>;
  const note=<FitNote lines={2} text={<>Publishing writes the tariff schedule, occupancy charges and meal plan prices dated the card’s valid-from date. Existing bookings keep their agreed rates. {ui.readOnlyNote}</>}/>;
  return <FitPage title="Tariff card" sub="The hotel's published prices, versioned by valid-from date. GST is read from the GST master on that date; prices incl. GST split CGST / SGST as on the bill." actions={actions}
    toolbar={<StatStrip page="tariff-card" variant="small" compact collapseOnTablet items={[{title:'Card in force today',value:sum.live??'—',sub:sum.liveFrom?'from '+sum.liveFrom:undefined},
      {title:'Drafts',value:String(sum.drafts)},{title:'Awaiting Owner',value:String(sum.proposed),tone:sum.proposed?'warn':undefined},{title:'Scheduled',value:String(sum.scheduled)}]}/>}
    tabs={<FitTabs page="tariff-card" value={tab} onChange={setTab} tabs={TRC_TABS.map(t=>({id:t.id,label:t.label}))}/>}>
    <VersionStrip value={v} onChange={setV}/>
    {v&&preview&&<p className="trc-preview muted" role="status">{v.id}: {preview}</p>}
    {ui.canReject&&<div className="trc-reject inline"><input aria-label="Reason for rejecting" placeholder="Reason for rejecting" value={reason} onChange={e=>setReason(e.target.value)}/>
      <button className="btn" disabled={busy||reason.trim().length<3} onClick={()=>run({type:'TRC_REJECT',id:v!.id,reason},'Proposal rejected').then(ok=>ok&&setReason(''))}><XCircle size={16}/> Reject</button></div>}
    {(tab==='rooms'||tab==='meals')&&<Editor v={v} tab={tab} note={note} editable={ui.canEdit}/>}
    {tab==='halls'&&<ScrollPanel label="Halls" footer={note}><Empty title="After hall booking" body="Hall prices join the card when hall booking is built (wave F6). A hall line on a draft cannot be published until then."/></ScrollPanel>}
    {tab==='seasons'&&<Seasons v={v} note={note} editable={ui.canEdit} weekend={ui.canWeekend}/>}
    {tab==='history'&&<History note={note}/>}
    {tab==='print'&&<ScrollPanel label="Rate sheet" title="Rate sheet" actions={<><button className="btn" onClick={()=>{void printDocument();}}><Printer size={15}/> Print</button>
      <button className="btn" onClick={()=>{void saveCsv(`tariff-card-${v?.id??'none'}.csv`,rateSheetCsv(s,v?.id)).then(()=>toast('CSV saved'));}}><Download size={15}/> CSV</button></>}>
      <RateSheetDoc sheet={rateSheetModel(s,v?.id)}/></ScrollPanel>}
  </FitPage>;
}

const inclText=(p:number|null|undefined)=>p===null||p===undefined?'':(p/100).toFixed(2);
function Editor({v,tab,note,editable}:{v?:TRCVersion;tab:TrcTab;note:React.ReactNode;editable:boolean}){
  const {s,run,busy}=useHcc();const [incl,setIncl]=useState(true);
  if(!v)return <ScrollPanel label="Card lines" footer={note}><Empty title="No tariff card yet" body="Create a draft from the prices in force, or import the rate-card CSVs (Owner)."/></ScrollPanel>;
  const lines=trcTabLines(v,tab),rows=trcEditorRows(s,lines);
  const save=(id:string,text:string)=>{const p=trcParseRupees(text);if(p===undefined)return;
    const cur=v.lines.find(l=>l.id===id);if(cur&&(incl?(cur.enteredIncl??cur.displayIncl):cur.rateExcl)===p)return;
    void run({type:'TRC_DRAFT_SAVE',id:v.id,lines:trcEditLine(v.lines,id,p,incl)},'Price saved');};
  return <ScrollPanel label="Card lines" title={`${v.label} · from ${v.effectiveFrom}`} footer={note}
    actions={editable?<label className="inline trc-incl"><input type="checkbox" checked={incl} onChange={e=>setIncl(e.target.checked)}/> Enter as incl. GST</label>:undefined}>
    {rows.length?<table className="table trc-grid"><thead><tr><th>Item</th><th>Nights</th><th className="num">Before GST</th><th className="num">GST</th><th className="num">Incl. GST</th><th className="num">Rounding</th></tr></thead>
      <tbody>{rows.map(r=><tr key={r.id}><td>{r.title}{r.note?<small className="muted"> · {r.note}</small>:null}</td><td>{r.daySet}</td>
        <td className="num">{editable&&!incl?<input key={v.id+r.id+r.excl} className="trc-price" inputMode="decimal" aria-label={`${r.title} before GST`} defaultValue={r.excl===null?'':(r.excl/100).toFixed(2)} disabled={busy} onBlur={e=>save(r.id,e.target.value)}/>:r.excl===null?'—':money(r.excl)}</td>
        <td className="num">{gstPct(r.bps)}</td>
        <td className="num">{editable&&incl?<input key={v.id+r.id+r.incl} className="trc-price" inputMode="decimal" aria-label={`${r.title} incl. GST`} defaultValue={inclText(r.entered??r.incl)} disabled={busy} onBlur={e=>save(r.id,e.target.value)}/>:r.incl===null?'Blank':money(r.incl)}</td>
        <td className="num">{r.diff?<Pill tone="orange">{(r.diff>0?'+':'')+money(r.diff)}</Pill>:'₹0.00'}</td></tr>)}</tbody></table>
      :<Empty title="No lines here" body={tab==='meals'?'Meal plan add-ons and extras appear here.':'Room prices appear here.'}/>}
  </ScrollPanel>;
}

function Seasons({v,note,editable,weekend}:{v?:TRCVersion;note:React.ReactNode;editable:boolean;weekend:boolean}){
  const {s,run,busy}=useHcc();const list=trcVersions(s).filter(x=>x.status!=='rejected');
  const days=v?.weekendDays??[5,6,0];
  const toggle=(d:number)=>{const next=days.includes(d)?days.filter(x=>x!==d):[...days,d];if(v&&editable)void run({type:'TRC_WEEKEND_SET',id:v.id,days:next},'Weekend nights saved');};
  return <ScrollPanel label="Seasons and weekends" title="Seasons & weekends" footer={note}>
    {v&&<div className="trc-season-form">
      <label>Valid from <input type="date" defaultValue={v.effectiveFrom} key={v.id+v.effectiveFrom} min={s.date} disabled={!editable||busy} onBlur={e=>{if(e.target.value&&e.target.value!==v.effectiveFrom)void run({type:'TRC_DRAFT_SAVE',id:v.id,effectiveFrom:e.target.value,lines:trcEditLine(v.lines,'',null,false)},'Valid-from date saved');}}/></label>
      <fieldset className="trc-days"><legend>Weekend nights</legend>{TRC_DAY_NAMES.map((n,d)=><label key={n} className="inline"><input type="checkbox" checked={days.includes(d)} disabled={!(editable&&weekend)||busy} onChange={()=>toggle(d)}/> {n}</label>)}</fieldset>
    </div>}
    <p className="muted">One yearly card plus dated seasonal versions: each season is a version with its own valid-from date; the latest published version on a date is in force.</p>
    <table className="table"><thead><tr><th>Version</th><th>Label</th><th>Valid from</th><th>Weekend</th><th>Status</th></tr></thead>
      <tbody>{list.map(x=><tr key={x.id}><td>{x.id}</td><td>{x.label}</td><td>{x.effectiveFrom}</td><td>{x.weekendDays.map(d=>TRC_DAY_NAMES[d]).join(', ')}</td><td><Pill tone={TRC_STATUS_TONE[x.status]}>{x.status}</Pill></td></tr>)}</tbody></table>
  </ScrollPanel>;
}

function History({note}:{note:React.ReactNode}){
  const {s,toast}=useHcc();const list=trcVersions(s);
  return <ScrollPanel label="Card history" title="History" footer={note} actions={<button className="btn" onClick={()=>{void saveCsv('tariff-card-history.csv',rateSheetHistoryCsv(s)).then(()=>toast('CSV saved'));}}><Download size={15}/> CSV</button>}>
    {list.length?<table className="table"><thead><tr><th>Version</th><th>Valid from</th><th>Status</th><th>By</th><th>Published</th><th className="num">Master rows</th></tr></thead>
      <tbody>{list.map(v=><tr key={v.id}><td>{v.id} · {v.label}{v.notes?<small className="muted"> · {v.notes}</small>:null}</td><td>{v.effectiveFrom}</td><td><Pill tone={TRC_STATUS_TONE[v.status]}>{v.status}{v.supersededBy?' by '+v.supersededBy:''}</Pill></td>
        <td>{v.by}{v.proposedBy?` · proposed ${v.proposedBy}`:''}</td><td>{v.publishedBy?`${v.publishedBy} · ${v.publishedAt}`:'—'}</td><td className="num">{v.publishLog?.length??0}</td></tr>)}</tbody></table>
      :<Empty title="No versions yet"/>}
  </ScrollPanel>;
}
