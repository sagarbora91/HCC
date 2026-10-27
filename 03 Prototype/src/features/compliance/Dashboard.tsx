// Compliance calendar — SCC's quarterly "Compliance Operating System Dashboard" (renderDashboard L2478–2511) with the Statutory Payable panel
// (L1862–1883) and the Evidence Pack Completeness meter (L2364–2393). Desktop and phone (cards stack on the phone).
import React from 'react';
import {Send,ArrowRight} from 'lucide-react';
import {useHcc} from '../../app/context';
import {cmpQuarter,cmpSlice,cmpTypeLabel,cmpFyLabel,cmpFyShortLabel,cmpEvidenceCompleteness,cmpPayablePanel,cmpInr,cmpRenderFYText,cmpStripHtml,cmpShortDate,
  CMP_PAYABLE_BASIS,type CMPPayableFeed} from '../../services/mock';
import {dateLabel} from '../../components/ui';
import {CmpText,cmpUiSet,useCmpUi} from './shared';

export function CmpPayablePanel({feed}:{feed?:CMPPayableFeed}){
  const {s}=useHcc();const p=cmpPayablePanel(feed,s.date);
  if(p.empty)return <section className="cmp-panel"><h3>Statutory Payable</h3><p className="cmp-meta">No statutory payable computed yet. Lock this month’s payroll run (Staff &amp; payroll) or issue invoices — the figures come from HCC’s own data.</p></section>;
  const row=(name:string,val:number)=><div className={'cmp-ops-row'+(val>0?'':' dim')}><span>{name}</span><strong>{cmpInr(val)}</strong></div>;
  return <section className="cmp-panel"><h3><span>Statutory Payable</span><small>{p.chip}</small></h3>
    <div className="cmp-ops">{row('EPF',p.pf)}{row('ESIC',p.esic)}{row('Professional Tax',p.pt)}{row('GST (estimate)',p.gst)}
      <div className="cmp-ops-row total"><span>Total statutory payable</span><strong>{cmpInr(p.total)}</strong></div></div>
    <p className="cmp-meta">Sources: {p.sp!.rec.sources.join(' · ')||'—'}</p>
    <p className="cmp-meta">Computed {dateLabel(p.sp!.rec.at)} from HCC data. {CMP_PAYABLE_BASIS}</p>
    <p className="cmp-meta">PF / ESIC / PT are business-wide (across payroll); GST is an estimate. {p.sp!.note}</p></section>;
}

export function CmpEvidenceMeter(){
  const {s}=useHcc();const x=cmpSlice(s),r=cmpEvidenceCompleteness(s,'fy');
  if(r.noFirm)return <section className="cmp-panel cmp-meter"><h3>Evidence Pack Completeness</h3><div className="cmp-bar"><span style={{width:'0%'}}/></div><p className="cmp-meta">Set the firm profile to track evidence completeness.</p></section>;
  return <section className="cmp-panel cmp-meter"><h3><span>Evidence Pack Completeness</span><strong>{r.doneTotal?r.pct+'%':'—'}</strong></h3>
    <div className="cmp-bar" role="progressbar" aria-valuenow={r.pct} aria-valuemin={0} aria-valuemax={100} aria-label="Evidence pack completeness"><span style={{width:r.pct+'%'}}/></div>
    <p className="cmp-meta">{r.doneTotal?`${r.doneSatisfied} of ${r.doneTotal} completed filing${r.doneTotal===1?'':'s'} have evidence attached`:'No completed filings yet — evidence appears here once filings are marked done'}</p>
    <p className="cmp-meta">FY {cmpFyShortLabel(x.fyStart)}: {r.applicableTotal} applicable obligation{r.applicableTotal===1?'':'s'} this FY</p>
    {r.missing.length?<div className="cmp-missing"><b>Missing evidence ({r.missing.length}):</b> {r.missing.map(m=><button key={m.id} type="button" className="btn small-btn"
        onClick={()=>cmpUiSet({view:'calendar',month:m.month,focus:m.id})}>{m.month}: {cmpStripHtml(cmpRenderFYText(m.name,x.fyStart))} Open →</button>)}</div>
      :r.doneTotal?<p className="cmp-meta">All completed filings have evidence attached.</p>:null}</section>;
}

export function CmpDashboard({feed}:{feed?:CMPPayableFeed}){
  const {s,open}=useHcc();const x=cmpSlice(s),f=x.profile,u=useCmpUi(s.date);const d=cmpQuarter(s,u.quarter),cq=d.q;
  return <div className="cmp-dash">
    <div className="cmp-dash-hd"><div><h2>Compliance Operating System Dashboard</h2><p>{cmpFyLabel(x.fyStart)} · {f?`${f.name} · ${cmpTypeLabel(f.gstType)}`:'All Firms'} · Focused on control, accountability, and audit readiness</p></div>
      {f&&<button type="button" className="btn" onClick={()=>open({type:'cmp-pack'})}><Send size={15}/>Send CA pack (simulated)</button>}</div>
    {!f&&<p className="cmp-banner">ℹ️ <b>No firm profile yet.</b> Dashboard is summarised for the full rule set. The Owner sets the profile to use evidence and completion history.</p>}
    <div className="cmp-qtabs" role="tablist" aria-label="Quarter">{d.quarters.map(q=><button key={q.key} type="button" role="tab" aria-selected={q.key===cq.key} className={q.key===cq.key?'active':''}
      onClick={()=>cmpUiSet({quarter:q.key})}>{q.label}<span>{q.total?q.score+'%':'—'}</span></button>)}</div>
    <div className="cmp-dash-stats"><div><strong>{cq.total}</strong><span>Total Compliances</span></div><div><strong>{cq.total?cq.score+'%':'—'}</strong><span>Compliance Score</span></div>
      <div><strong>{cq.pending}</strong><span>Open Items</span></div><div className={cq.overdue?'bad':''}><strong>{cq.overdue}</strong><span>Overdue Alerts</span></div></div>
    <div className="cmp-grid-2"><CmpPayablePanel feed={feed}/><CmpEvidenceMeter/></div>
    <div className="cmp-grid-2">
      <section className="cmp-panel"><h3>Monthly Performance</h3><table className="cmp-ftable"><thead><tr><th>Month</th><th>Score</th><th>Done</th><th>Overdue</th></tr></thead>
        <tbody>{d.monthly.map(r=><tr key={r.month}><td>{r.month}</td><td>{r.score}%</td><td>{r.done}/{r.total}</td><td>{r.overdue||'0'}</td></tr>)}</tbody></table></section>
      <section className="cmp-panel"><h3>Control Summary</h3><div className="cmp-ops">
        <div className="cmp-ops-row"><span>High-risk obligations<small>GSTR-3B, TDS, EPF, advance tax and similar critical items</small></span><strong>{cq.highRisk}</strong></div>
        <div className="cmp-ops-row"><span>Cash flow exposure<small>High / Medium / Low tax outflow planning buckets</small></span><strong>{d.cash.High} / {d.cash.Medium} / {d.cash.Low}</strong></div>
        <div className="cmp-ops-row"><span>Evidence readiness<small>Proofs captured at completion for audit trail</small></span><strong>{cq.done}</strong></div></div></section></div>
    <div className="cmp-grid-2">
      <section className="cmp-panel"><h3>Top Control Focus Items</h3>{d.focus.length?<ul className="cmp-focus">{d.focus.map(i=><li key={i.card.id}>
        <div><strong><CmpText text={i.name} custom={i.card._custom}/></strong><small>{i.month} · Due {i.card.due}</small></div>
        <span className={'cmp-pill cmp-risk-'+i.control.risk.toLowerCase()}>{i.control.risk}</span><span className="cmp-focus-owner">{i.control.responsible}</span>
        <span className={'cmp-pill cmp-'+i.control.escalation.cls}>{i.control.escalation.label}</span></li>)}</ul>
        :<p className="cmp-meta">All items in {d.period} are completed.</p>}</section>
      <section className="cmp-panel"><h3>Ownership Load</h3><div className="cmp-ops">{d.ownerLoad.map(([o,n])=><div key={o} className="cmp-ops-row"><span>{o}<small>Assigned compliances in {d.period}</small></span><strong>{n}</strong></div>)}</div></section></div>
    <section className="cmp-panel"><h3><span>Pending and Escalated Items</span><small>{d.focus.length} items shown</small></h3>
      {d.focus.length?<ul className="cmp-pending">{d.focus.map(i=><li key={i.card.id} className={'cmp-cat-'+i.card.cat}><div>
        <strong><CmpText text={i.name} custom={i.card._custom}/></strong><small>{i.month} · Due {i.card.due} · Internal {cmpShortDate(i.control.internalDate)}</small>
        <small>{i.control.responsible} → {i.control.reviewer} · {i.control.evidence.join(' · ')}</small></div>
        <button type="button" className="btn small-btn" onClick={()=>cmpUiSet({view:'calendar',month:i.month,focus:i.card.id})}>Open <ArrowRight size={14}/></button></li>)}</ul>
        :<p className="cmp-meta">No pending items in this quarter.</p>}</section>
  </div>;
}
