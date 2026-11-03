// F7-R2 · Reports hub on the phone: search, category chips, one card per report, the viewer as cards (no sideways scroll), scheduled runs and export log as cards.
import React,{useState} from 'react';
import {ArrowLeft,Search} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Empty,Pill,dateLabel} from '../../components/ui';
import {ListCard,ChipFilter} from '../../app/mobile/components';
import type {RptArea} from '../../domain/reports/types';
import {filterHub,groupByArea,favsOf,AREA_LABEL,AREA_ORDER,HUB_TABS,visibleSchedules,visibleRuns,visibleExportLog,cadenceText,runTone,runText,type HubTab} from './hub-logic';
import {ReportView} from './ReportView';
import {useHubDefs} from './HubPage';

export function ReportsHubMobile(){
  const {s,uid}=useHcc();const defs=useHubDefs();
  const [tab,setTab]=useState<HubTab>('reports');const [query,setQuery]=useState(''),[area,setArea]=useState<RptArea|'all'>('all'),[open,setOpen]=useState<string|null>(null);
  const favs=favsOf(s,uid);
  const title=(id:string)=>defs.find(d=>d.id===id)?.title??id;
  const def=defs.find(d=>d.id===open);
  if(def)return <div className="rpt-m"><button type="button" className="btn rpt-m-back" onClick={()=>setOpen(null)}><ArrowLeft size={18}/> All reports</button><ReportView key={def.id} def={def} phone/></div>;
  const shown=filterHub(defs,{query,area,favOnly:false,favs,phone:true});
  // Favourites first inside the list, then the categories.
  const groups=groupByArea(shown);const favDefs=shown.filter(d=>favs.includes(d.id));
  return <div className="rpt-m">
    <ChipFilter label="Section" options={HUB_TABS.map(t=>({id:t.id,label:t.label}))} value={tab} onChange={setTab}/>
    {tab==='reports'&&<>
      <label className="rpt-search"><Search size={18} aria-hidden/><span className="sr-only">Search reports</span><input type="search" value={query} placeholder="Search reports" onChange={e=>setQuery(e.target.value)}/></label>
      <ChipFilter label="Category" options={[{id:'all' as const,label:'All'},...AREA_ORDER.filter(a=>defs.some(d=>d.area===a)).map(a=>({id:a,label:AREA_LABEL[a]}))]} value={area} onChange={setArea}/>
      {shown.length===0?<Empty title={defs.length?'No report matches':'No reports available'} body={defs.length?'Try a different search.':'No report is open to your role yet.'}/>:<>
        {favDefs.length>0&&<section><h3 className="rpt-m-h">Favourites</h3>{favDefs.map(d=><ListCard key={'f'+d.id} title={d.title} sub={AREA_LABEL[d.area]} right={d.phone==='summary'?<Pill>Summary</Pill>:undefined} onClick={()=>setOpen(d.id)}/>)}</section>}
        {groups.map(g=><section key={g.area}><h3 className="rpt-m-h">{g.label}</h3>{g.defs.map(d=><ListCard key={d.id} title={d.title} sub={d.dateBasis} right={d.phone==='summary'?<Pill>Summary</Pill>:undefined} onClick={()=>setOpen(d.id)}/>)}</section>)}</>}</>}
    {tab==='scheduled'&&<>
      {visibleSchedules(s,uid).length===0?<Empty title="No schedules yet" body="Schedules are set up on the desktop."/>:visibleSchedules(s,uid).map(x=><ListCard key={x.id} title={title(x.reportId)} sub={cadenceText(x.cadence)+' · '+x.format.toUpperCase()} right={<Pill tone={x.enabled?'green':'neutral'}>{x.enabled?'On':'Off'}</Pill>}/>)}
      <h3 className="rpt-m-h">Run history</h3>
      {visibleRuns(s,uid).slice(0,50).map(r=><ListCard key={r.id} title={title(r.reportId)} sub={dateLabel(r.startedAt.slice(0,10))+' '+r.startedAt.slice(11,16)} right={<Pill tone={runTone(r)}>{runText(r)}</Pill>}/>)}</>}
    {tab==='log'&&(visibleExportLog(s,uid).length===0?<Empty title="Nothing exported yet" body="Every print and export is recorded here."/>:
      visibleExportLog(s,uid).slice(0,50).map(e=><ListCard key={e.id} title={title(e.reportId)} sub={`${e.format} · ${e.rows} rows`} meta={dateLabel(e.at.slice(0,10))+' '+e.at.slice(11,16)}/>))}
  </div>;
}
