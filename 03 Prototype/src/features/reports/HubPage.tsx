// F7-R2 · Reports hub (page `report-hub`): the catalogue grouped by category with search, favourites and the viewer beside it; tabs for scheduled runs and the export log.
import React,{useMemo,useState} from 'react';
import {Star,Search} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Empty,Pill} from '../../components/ui';
import {FitPage,ScrollPanel,SplitView,FitTabs,useFitTab} from '../../components/fit';
import type {RptArea} from '../../domain/reports/types';
import {reportCatalogue,canViewReport} from './hub-engine';
import {hubDefs,filterHub,groupByArea,favsOf,AREA_LABEL,AREA_ORDER,HUB_TABS,type HubTab} from './hub-logic';
import {ReportView} from './ReportView';
import {ScheduledTab,ExportLogTab} from './ScheduleTabs';

export function useHubDefs(){
  const {s,uid}=useHcc();
  return useMemo(()=>hubDefs(s,uid,reportCatalogue(s,uid),d=>canViewReport(s,uid,d)),[s,uid]);
}

export function ReportsHub(){
  const {s,uid,run}=useHcc();
  const defs=useHubDefs();
  const [tab,setTab]=useFitTab<HubTab>('report-hub',HUB_TABS.map(t=>t.id),'reports');
  const [query,setQuery]=useState(''),[area,setArea]=useState<RptArea|'all'>('all'),[favOnly,setFavOnly]=useState(false),[open,setOpen]=useState<string|null>(null);
  const favs=favsOf(s,uid);
  const shown=filterHub(defs,{query,area,favOnly,favs});
  const groups=groupByArea(shown);
  const def=defs.find(d=>d.id===open)??null;
  const toolbar=tab==='reports'?<div className="row rpt-toolbar" role="search">
    <label className="rpt-search"><Search size={16} aria-hidden/> <span className="sr-only">Search reports</span><input type="search" value={query} placeholder="Search reports" onChange={e=>setQuery(e.target.value)}/></label>
    <label>Category <select value={area} onChange={e=>setArea(e.target.value as RptArea|'all')}><option value="all">All</option>{AREA_ORDER.filter(a=>defs.some(d=>d.area===a)).map(a=><option key={a} value={a}>{AREA_LABEL[a]}</option>)}</select></label>
    <label className="rpt-check"><input type="checkbox" checked={favOnly} onChange={e=>setFavOnly(e.target.checked)}/> Favourites only</label></div>:undefined;
  const list=<ScrollPanel title="Reports" label="Report list" actions={<span className="muted">{shown.length}</span>}>
    {groups.length===0?<Empty title={defs.length?'No report matches':'No reports available'} body={defs.length?'Try a different search or category.':'No report is open to your role yet.'}/>:
      <nav className="rpt-list" aria-label="Reports">{groups.map(g=><div key={g.area} className="rpt-group"><h3>{g.label}</h3><ul>{g.defs.map(d=><li key={d.id}>
        <button type="button" className={'rpt-item'+(open===d.id?' on':'')} aria-current={open===d.id?'true':undefined} onClick={()=>setOpen(d.id)}>
          <span>{d.title}</span>{d.moneyFlag&&<small className="muted">money</small>}{favs.includes(d.id)&&<Star size={13} aria-label="Favourite" fill="currentColor"/>}</button>
        <button type="button" className="rpt-fav" aria-pressed={favs.includes(d.id)} aria-label={(favs.includes(d.id)?'Remove from favourites: ':'Add to favourites: ')+d.title} onClick={()=>void run({type:'RPT_FAV_TOGGLE',reportId:d.id})}><Star size={16} fill={favs.includes(d.id)?'currentColor':'none'}/></button></li>)}</ul></div>)}</nav>}
  </ScrollPanel>;
  return <FitPage title="Reports" sub="Every report, one place: view, export, print or schedule" layout={tab==='reports'?'split':'fill'} toolbar={toolbar}
    tabs={<FitTabs page="report-hub" tabs={HUB_TABS.map(t=>({...t}))} value={tab} onChange={setTab}/>}>
    {tab==='reports'&&<SplitView label="Reports" list={list} detail={def?<ReportView key={def.id} def={def}/>:null} onCloseDetail={()=>setOpen(null)}
      empty={<Empty title="Choose a report" body="Pick one from the list to set its dates and see it here."/>}/>}
    {tab==='scheduled'&&<ScheduledTab defs={defs}/>}
    {tab==='log'&&<ExportLogTab defs={defs}/>}
  </FitPage>;
}
