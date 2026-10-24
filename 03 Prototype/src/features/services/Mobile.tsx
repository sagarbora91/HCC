// Module 18 · Guest services — phone pages (cards instead of tables, one FAB per section).
import React,{useState} from 'react';
import {Plus,MessageSquareWarning,PackageSearch,Printer} from 'lucide-react';
import {gsvSlice,gsvSlaLabel,gsvIsOpen,gsvRecoveryStatus,gsvFollowUpDue,gsvDisposable,gsvItemAgeDays,gsvResponseTotals,gsvComplaintOpen,gsvVisibleOps} from '../../services/mock';
import {useHcc} from '../../app/context';
import {ListCard,KpiCard,ChipFilter,Fab} from '../../app/mobile/components';
import {Pill,Empty} from '../../components/ui';
import {gsvWhen,gsvUserName,gsvStatusTone,gsvSevTone,GsvReportBody} from './modals';
import {gsvTabs,gsvFilterRequests,GsvWorkButtons,type GsvTab,type GsvReqFilter} from './GuestServices';

export function GuestServicesMobile(){
  const {s,open}=useHcc();
  const [tab,setTab]=useState<GsvTab>('Requests');
  const [f,setF]=useState<GsvReqFilter>('Open');
  const x=gsvSlice(s),t=gsvResponseTotals(s);
  const counts:Record<GsvTab,number|undefined>={Requests:x.requests.filter(gsvIsOpen).length,Complaints:x.complaints.filter(gsvComplaintOpen).length,'Lost & found':x.items.filter(i=>i.status==='Stored'||i.status==='Claimed').length,Reports:undefined};
  return <div className="gsv-m">
    <ChipFilter label="Guest services section" value={tab} onChange={setTab} options={gsvTabs.map(id=>({id,label:id,count:counts[id]}))}/>
    {tab==='Requests'&&<>
      <div className="gsv-m-kpis"><KpiCard label="Open" value={counts.Requests} sub={`${t.overdue} overdue`} onClick={()=>setF('Open')}/><KpiCard label="Within SLA" value={t.done?t.pct+'%':'—'} sub={`avg ${t.avg} min`} onClick={()=>setF('Done')}/></div>
      <ChipFilter label="Request filter" value={f} onChange={setF} options={(['Open','Overdue','Done','All'] as GsvReqFilter[]).map(id=>({id,label:id,count:gsvFilterRequests(s,id).length}))}/>
      {gsvFilterRequests(s,f).map(r=>{const sla=gsvSlaLabel(s,r);return <ListCard key={r.id} onClick={()=>open({type:'gsv-request',id:r.id})}
        title={<>Room {r.room} · {r.type}</>} sub={`${r.guest} · ${r.dept} · ${gsvUserName(s,r.assignee)}`} meta={r.detail||r.id}
        right={<span className="gsv-m-right"><Pill tone={sla.tone}>{sla.label}</Pill><Pill tone={gsvStatusTone(r.status)}>{r.status}</Pill></span>}/>;})}
      {!gsvFilterRequests(s,f).length&&<Empty title="No requests here"/>}
      <Fab label="New request" icon={Plus} onClick={()=>open({type:'gsv-request-new'})}/></>}
    {tab==='Complaints'&&<>
      {x.complaints.slice().sort((a,b)=>(gsvComplaintOpen(a)===gsvComplaintOpen(b)?(a.createdAt<b.createdAt?1:-1):gsvComplaintOpen(a)?-1:1)).map(c=>{const rec=gsvRecoveryStatus(s,c);return <ListCard key={c.id} onClick={()=>open({type:'gsv-complaint',id:c.id})}
        title={<>{c.guest}{c.room?' · Room '+c.room:''}</>} sub={`${c.category} · owner ${gsvUserName(s,c.owner)}`} meta={gsvFollowUpDue(s,c)?'Follow-up due today':rec!=='None'?'Recovery discount · '+rec:c.id}
        right={<span className="gsv-m-right"><Pill tone={gsvSevTone(c.severity)}>{c.severity}</Pill><Pill tone={gsvStatusTone(c.status)}>{c.status}</Pill></span>}/>;})}
      {!x.complaints.length&&<Empty title="No complaints recorded"/>}
      <Fab label="Log complaint" icon={MessageSquareWarning} onClick={()=>open({type:'gsv-complaint-new'})}/></>}
    {tab==='Lost & found'&&<>
      {x.items.slice().sort((a,b)=>a.foundAt<b.foundAt?1:-1).map(i=><ListCard key={i.id} onClick={()=>open({type:'gsv-item',id:i.id})}
        title={i.description} sub={`${i.foundWhere} · ${gsvWhen(i.foundAt)}`} meta={`${i.storage} · ${gsvItemAgeDays(s,i)} days`}
        right={<span className="gsv-m-right"><Pill tone={gsvStatusTone(i.status)}>{i.status}</Pill>{gsvDisposable(s,i)&&<Pill tone="orange">Retention over</Pill>}</span>}/>)}
      {!x.items.length&&<Empty title="No items logged"/>}
      <Fab label="Log found item" icon={PackageSearch} onClick={()=>open({type:'gsv-item-new'})}/></>}
    {tab==='Reports'&&<>
      <div className="card padded gsv-m-report"><GsvReportBody/></div>
      <Fab label="Print report" icon={Printer} onClick={()=>open({type:'gsv-print'})}/></>}
  </div>;
}

export function GsvMyRequestsMobile(){
  const {s,uid,open}=useHcc();
  const [f,setF]=useState<GsvReqFilter>('Open');
  const mine=gsvVisibleOps(s,uid),list=gsvFilterRequests(s,f,mine);
  return <div className="gsv-m">
    <ChipFilter label="Request filter" value={f} onChange={setF} options={(['Open','Overdue','Done','All'] as GsvReqFilter[]).map(id=>({id,label:id,count:gsvFilterRequests(s,id,mine).length}))}/>
    {list.map(r=>{const sla=gsvSlaLabel(s,r);return <div key={r.id} className="gsv-m-task">
      <ListCard onClick={()=>open({type:'gsv-request',id:r.id})} title={<>Room {r.room} · {r.type}</>} sub={`${r.detail||'—'} · ${r.guest}`} meta={r.note?`Guest note: “${r.note}”`:`${r.dept} · ${gsvUserName(s,r.assignee)}`}
        right={<span className="gsv-m-right"><Pill tone={sla.tone}>{sla.label}</Pill><Pill tone={gsvStatusTone(r.status)}>{r.status}</Pill></span>}/>
      <GsvWorkButtons r={r} big/></div>;})}
    {!list.length&&<Empty title="No guest requests" body="Nothing is assigned to you in this view."/>}
    <Fab label="Found item" icon={PackageSearch} onClick={()=>open({type:'gsv-item-new'})}/>
  </div>;
}
