// Wave 0.5 · W05-C Compliance calendar — desktop page and phone page (same data, cards on the phone).
import React,{useState} from 'react';
import {CheckCircle2,Ban,RotateCcw,Send,Settings2,ShieldAlert} from 'lucide-react';
import {useHcc} from '../../app/context';
import {cmpHasAccess,cmpCanManage,cmpItems,cmpCounts,CMP_KINDS,CMP_ILLUSTRATIVE} from '../../services/mock';
import type {CMPItem} from '../../services/mock';
import {PageTitle,Empty,dateLabel} from '../../components/ui';
import {usePaged,ShowMore,DESKTOP_PAGE,PHONE_PAGE} from '../../components/ShowMore';
import {ChipFilter,KpiCard,Fab} from '../../app/mobile/components';
import {CmpStatusPill,useCmpFilingText,CMP_FILTERS,cmpApplyFilter} from './shared';
import type {CmpFilter} from './shared';

function useCalendar(){
  const {s,uid}=useHcc();
  const [filter,setFilter]=useState<CmpFilter>('open');const [kind,setKind]=useState('all');
  const items=cmpItems(s),counts=cmpCounts(items);
  return {s,uid,items,counts,filter,setFilter,kind,setKind,list:cmpApplyFilter(items,filter,kind),manage:cmpCanManage(s,uid)};}

function RowActions({i}:{i:CMPItem}){
  const {open,uid,s}=useHcc();
  if(i.filing)return cmpCanManage(s,uid)?<button className="btn small-btn" onClick={()=>open({type:'cmp-reopen',id:i.obligation.id,room:i.period})}><RotateCcw size={14}/>Reopen</button>:null;
  return <span className="cmp-actions"><button className="btn primary small-btn" onClick={()=>open({type:'cmp-file',id:i.obligation.id,room:i.period})}><CheckCircle2 size={14}/>Mark filed</button>
    <button className="btn small-btn" onClick={()=>open({type:'cmp-na',id:i.obligation.id,room:i.period})}><Ban size={14}/>Not applicable</button></span>;}

export function CompliancePage(){
  const c=useCalendar();const {open}=useHcc();const filingText=useCmpFilingText();
  const pg=usePaged(c.list,DESKTOP_PAGE);
  if(!cmpHasAccess(c.s,c.uid))return <Empty title="Owner and Accountant only" body="The compliance calendar is visible to the Owner and the Accountant."/>;
  return <div className="cmp-page">
    <PageTitle title="Compliance calendar" sub="TDS, GST returns, EPF, ESIC and Professional Tax: due dates, who filed and when."
      action={<span className="cmp-head-actions">
        {c.manage&&<button className="btn" onClick={()=>open({type:'cmp-manage'})}><Settings2 size={15}/>Manage obligations</button>}
        <button className="btn primary" onClick={()=>open({type:'cmp-pack'})}><Send size={15}/>Send CA pack</button></span>}/>
    <div className="small-stats cmp-stats" aria-label="Compliance counters">
      <div className={c.counts.overdue?'cmp-stat-bad':''}><span>Overdue</span><strong>{c.counts.overdue}</strong></div>
      <div><span>Due this week</span><strong>{c.counts.due}</strong></div>
      <div><span>Filed this year</span><strong>{c.counts.filed}</strong></div>
      <div><span>Not applicable</span><strong>{c.counts.na}</strong></div>
    </div>
    <div className="info-note"><ShieldAlert size={16}/><span>{CMP_ILLUSTRATIVE} Nothing is filed from here: this is your tracker. “Send CA pack” is simulated and e-mails nobody.</span></div>
    <section className="card">
      <div className="section-title"><div><h2>Filings</h2><p>Soonest due date first. Business date {dateLabel(c.s.date)}.</p></div>
        <span className="cmp-filters"><label className="cmp-kind">Type<select value={c.kind} onChange={e=>c.setKind(e.target.value)}><option value="all">All types</option>{CMP_KINDS.map(k=><option key={k}>{k}</option>)}</select></label></span></div>
      <div className="tabs" role="tablist" aria-label="Filter filings">{CMP_FILTERS.map(f=><button key={f.id} role="tab" aria-selected={c.filter===f.id} className={c.filter===f.id?'active':''} onClick={()=>c.setFilter(f.id)}>{f.label}
        <span>{cmpApplyFilter(c.items,f.id,c.kind).length}</span></button>)}</div>
      {!pg.total?<Empty title={c.filter==='open'?'Nothing open':'Nothing here'} body={c.filter==='open'?'Every tracked filing is filed or marked not applicable.':'No filings match this filter.'}/>:
      <div className="table-wrap"><table className="cmp-table"><thead><tr><th>Obligation</th><th>Period</th><th>Due date</th><th>Owner</th><th>Status</th><th>Filing record</th><th><span className="cmp-sr">Actions</span></th></tr></thead>
        <tbody>{pg.shown.map(i=><tr key={i.key} className={i.status==='Overdue'?'cmp-row-overdue':''}>
          <td><strong>{i.obligation.name}</strong><small>{i.obligation.form}</small></td><td>{i.periodLabel}</td><td className="number">{dateLabel(i.due)}</td><td>{i.obligation.owner}</td>
          <td><CmpStatusPill item={i}/></td><td className="cmp-record">{filingText(i)||'—'}</td><td><RowActions i={i}/></td></tr>)}</tbody></table></div>}
      <ShowMore p={pg}/>
    </section>
  </div>;}

export function ComplianceMobile(){
  const c=useCalendar();const {open}=useHcc();const filingText=useCmpFilingText();
  const pg=usePaged(c.list,PHONE_PAGE);
  if(!cmpHasAccess(c.s,c.uid))return <Empty title="Owner and Accountant only"/>;
  return <div className="cmp-m">
    <div className="cmp-m-kpis"><KpiCard label="Overdue" value={c.counts.overdue}/><KpiCard label="Due this week" value={c.counts.due}/><KpiCard label="Filed" value={c.counts.filed}/></div>
    <p className="cmp-m-note">{CMP_ILLUSTRATIVE}</p>
    <ChipFilter label="Filter filings" options={CMP_FILTERS.map(f=>({id:f.id,label:f.label==='Filed / not applicable'?'Filed':f.label,count:cmpApplyFilter(c.items,f.id,'all').length}))} value={c.filter} onChange={c.setFilter}/>
    {c.manage&&<button className="btn full cmp-m-manage" onClick={()=>open({type:'cmp-manage'})}><Settings2 size={18}/>Manage obligations</button>}
    {!pg.total&&<Empty title="Nothing here" body="No filings match this filter."/>}
    {pg.shown.map(i=><div key={i.key} className={'card cmp-m-card'+(i.status==='Overdue'?' cmp-m-overdue':'')}>
      <div className="cmp-m-top"><strong>{i.obligation.name}</strong><CmpStatusPill item={i}/></div>
      <span>{i.periodLabel} · due {dateLabel(i.due)}</span><small>{i.obligation.owner} · {i.obligation.form}</small>
      {i.filing&&<small className="cmp-m-record">{filingText(i)}</small>}
      {!i.filing&&<div className="cmp-m-btns"><button className="btn primary" onClick={()=>open({type:'cmp-file',id:i.obligation.id,room:i.period})}><CheckCircle2 size={18}/>Mark filed</button>
        <button className="btn" onClick={()=>open({type:'cmp-na',id:i.obligation.id,room:i.period})}>Not applicable</button></div>}
      {i.filing&&c.manage&&<div className="cmp-m-btns"><button className="btn" onClick={()=>open({type:'cmp-reopen',id:i.obligation.id,room:i.period})}><RotateCcw size={18}/>Reopen</button></div>}
    </div>)}
    <ShowMore p={pg}/>
    <Fab label="Send CA pack" icon={Send} onClick={()=>open({type:'cmp-pack'})}/>
  </div>;}

