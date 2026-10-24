// MB-08 phone sheets: 'ma-reject' (id = approval ID), 'ma-audit-filter', 'ma-range', 'ma-drill' (id = metric ID, room = report tab),
// 'ma-dictionary'. Rendered full-screen by ModalHost; primary action sits in the sticky .modal-actions.
import React,{useState} from 'react';
import {X,Check,Download} from 'lucide-react';
import {money,adminApprovalInbox,reportDrillCsv,reportDictionary,reportRangeLimit,reportRangePresets,reportMonthRange,historyMonths} from '../../services/mock';
import {usePaged,ShowMore,PHONE_PAGE} from '../../components/ShowMore';
import {useHcc} from '../../app/context';
import {Empty,shortDate,dateLabel} from '../../components/ui';
import {downloadCsv} from '../../features/reports/Reports';
import {maApprovalActions,maAuditOptions,maRangeCheck,maReportPack,maDrillTotal,maMaskReports,MA_REPORT_CHIPS} from './logic';
import {useMaUi,maSetUi,maRange} from './store';
import {MaRows} from './parts';

export function MaRejectSheet({id}:{id?:string}){
  const {s,uid,run,modal,closeModal,busy}=useHcc();const [reason,setReason]=useState('');
  const r=adminApprovalInbox(s).find(x=>x.a.id===id);
  if(!r)return <Empty title="This request is unavailable"/>;
  const act=maApprovalActions(s,uid,r);
  return <><div className="modal-heading"><div className="eyebrow">{r.a.id} · {r.kind.toUpperCase()}</div><h2>Reject request</h2><p>{r.requester} · {r.a.amount?money(r.a.amount)+' · ':''}{r.a.reason}</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(!reason.trim()||act.rejectBlock)return;
      run({type:r.command,id:r.a.id,approve:false,reason:reason.trim(),key:modal!.key},'Request rejected · '+reason.trim(),{close:true});}}>
      <label>Reason for rejecting (required)<textarea required maxLength={200} rows={4} enterKeyHint="done" value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. Discount not justified — offer late checkout instead"/></label>
      {act.rejectBlock&&<p className="ma-why" role="note">{act.rejectBlock}</p>}
      {!reason.trim()&&<p className="ma-why">Enter a reason to continue.</p>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Back</button><button className="btn danger" disabled={busy||!reason.trim()||!!act.rejectBlock}><X size={18}/>Reject request</button></div>
    </form></>;
}

export function MaAuditFilterSheet(){
  const {s,closeModal}=useHcc();const {audit}=useMaUi();
  const [f,setF]=useState(audit);const {actors,actions}=maAuditOptions(s);
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement>)=>setF({...f,[k]:e.target.value});
  return <><div className="modal-heading"><h2>Filter audit log</h2><p>Narrow by who, what, text or business date.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();maSetUi({audit:f});closeModal();}}>
      <label>Actor<select value={f.actor||''} onChange={set('actor')}><option value="">All actors</option>{actors.map(a=><option key={a}>{a}</option>)}</select></label>
      <label>Action<select value={f.action||''} onChange={set('action')}><option value="">All actions</option>{actions.map(a=><option key={a}>{a}</option>)}</select></label>
      <label>Contains text<input value={f.text||''} onChange={set('text')} enterKeyHint="search" placeholder="booking, room, setting…"/></label>
      <label>From<input type="date" value={f.from||''} onChange={set('from')}/></label>
      <label>To<input type="date" value={f.to||''} onChange={set('to')}/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>{maSetUi({audit:{}});closeModal();}}>Clear all</button><button className="btn primary"><Check size={18}/>Show results</button></div>
    </form></>;
}

export function MaRangeSheet(){
  const {s,closeModal}=useHcc();const u=useMaUi();const cur=maRange(u,s.date);
  const [from,setFrom]=useState(cur.from),[to,setTo]=useState(cur.to);
  const chk=maRangeCheck(from,to,s.date,reportRangeLimit(s));
  const apply=(a:string,b:string)=>{maSetUi({reportFrom:a,reportTo:b});closeModal();};
  return <><div className="modal-heading"><h2>Report date range</h2><p>Business dates up to today ({dateLabel(s.date)}). At most {reportRangeLimit(s)} days.</p></div>
    <div className="ma-presets">{reportRangePresets(s).map(p=><button type="button" key={p.id} className="btn" onClick={()=>apply(p.from,p.to)}>{p.label}</button>)}
      {(s.historyX?historyMonths(s).months:[]).map(m=>{const r=reportMonthRange(s,m.month);return <button type="button" key={m.month} className="btn" onClick={()=>apply(r.from,r.to)}>{m.label}</button>;})}</div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(chk.ok)apply(from,to);}}>
      <label>From (business date)<input type="date" value={from} max={s.date} onChange={e=>setFrom(e.target.value)}/></label>
      <label>To<input type="date" value={to} max={s.date} onChange={e=>setTo(e.target.value)}/></label>
      {!chk.ok&&<p className="ma-why" role="alert">{chk.error}</p>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={!chk.ok}><Check size={18}/>Apply range</button></div>
    </form></>;
}

export function MaDrillSheet({id,room}:{id?:string;room?:string}){
  const {s,uid,booking,toast,closeModal}=useHcc();const u=useMaUi();const {from,to}=maRange(u,s.date);
  const pack=maReportPack(s,uid,from,to);const m=pack?.tabs.find(t=>t.id===room)?.metrics.find(x=>x.id===id);
  const pg=usePaged(m?.rows??[],PHONE_PAGE);
  if(!pack||!m)return <Empty title="No report for this range"/>;
  const mask=maMaskReports(s,uid);const fmt=(v:number)=>m.unit==='money'?money(v):String(v);
  return <><div className="modal-heading"><div className="eyebrow">{MA_REPORT_CHIPS.find(c=>c.id===room)?.label.toUpperCase()} · {shortDate(from)} – {shortDate(to)}</div><h2>{m.label}</h2><p>{m.rows.length} source row{m.rows.length===1?'':'s'}{m.note?' · '+m.note:''}</p></div>
    <div className="ma-total" role="status"><span>Total — equals the report figure</span><strong className="number">{fmt(maDrillTotal(m))}</strong></div>
    {m.rows.length?<><ul className="ma-drill">{pg.shown.map(r=><li key={r.ref}>
      {r.booking&&!mask?<button type="button" onClick={()=>booking(r.booking!)}><DrillBody r={r} v={fmt(r.amount)}/></button>:<div><DrillBody r={r} v={fmt(r.amount)}/></div>}</li>)}</ul><ShowMore p={pg}/></>
      :<Empty title="No source records" body="Nothing matches this figure in the selected range."/>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button>
      <button type="button" className="btn primary" onClick={()=>{downloadCsv(`HCC-Demo-rows-${m.id}-${pack.from}_${pack.to}.csv`,reportDrillCsv(s,pack,m));toast('Source rows downloaded');}}><Download size={18}/>Rows CSV</button></div></>;
}
function DrillBody({r,v}:{r:{date:string;ref:string;booking?:string;guest?:string;room?:string;detail:string;method?:string};v:string}){
  return <><span className="ma-drill-main"><strong>{r.booking?`${r.guest??''} · ${r.booking}`:r.room?`Room ${r.room}`:r.detail}</strong><small>{r.detail}{r.method?' · '+r.method:''}</small><small>{shortDate(r.date)} · {r.ref}</small></span><strong className="number">{v}</strong></>;
}

export function MaDictionarySheet(){
  const {closeModal}=useHcc();
  return <><div className="modal-heading"><div className="eyebrow">FINANCIAL RULEBOOK §6</div><h2>Report dictionary</h2><p>How each figure is defined and what happens when a divisor is zero.</p></div>
    {reportDictionary.map(([a,b,c])=><div className="ma-card" key={a}><strong>{a}</strong><MaRows rows={[['Definition',b],['Zero handling',c]]}/></div>)}
    <div className="modal-actions"><button type="button" className="btn primary" onClick={closeModal}>Done</button></div></>;
}
