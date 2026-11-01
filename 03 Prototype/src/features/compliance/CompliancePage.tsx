// Compliance calendar = SCC tax module in full (DEC-116): desktop page and phone page (same data and pieces, cards on the phone).
// SCC layout: FY selector, firm profile, Action Center, Calendar / Dashboard; the calendar has month tabs April–March with overdue dots
// (L2120–2135), the month's control note, the current-month statutory strip, 7 stats, the category filter with NA (L2450–2451) and the cards.
import React,{useEffect,useRef} from 'react';
import {Bell,Settings2,Building2,FileSpreadsheet,Printer,Download,Archive,Send,LayoutDashboard,CalendarDays} from 'lucide-react';
import {useHcc} from '../../app/context';
import {cmpHasAccess,cmpCanManage,cmpSlice,cmpMonthPage,cmpHasOverdueInMonth,cmpActionCenter,cmpBadgeText,cmpCsv,cmpTypeLabel,cmpFyLabel,cmpPayableStrip,cmpInr,cmpFyStatus,
  CMP_MONTHS,CMP_FY_MIN,CMP_FY_MAX,CMP_ILLUSTRATIVE,type CMPCatFilter} from '../../services/mock';
import {Empty,dateLabel} from '../../components/ui';
import {FitPage,FitTabs,useFitTab,ScrollPanel,FitNote} from '../../components/fit';
import {saveCsv,saveAndReport} from '../../app/native';
import {CmpCard} from './Card';
import {CmpDashboard} from './Dashboard';
import {CmpRich,cmpActionKey,cmpUiSet,useCmpUi,useCmpFeed} from './shared';
import {cmpSaveZip} from './exports';

const FILTERS:{id:CMPCatFilter;label:string}[]=[{id:'all',label:'All'},{id:'it',label:'Income Tax'},{id:'gst',label:'GST'},{id:'comp',label:'Composition'},{id:'lab',label:'Labour'},{id:'na',label:'NA'}];

function useCompliance(){
  const {s,uid}=useHcc();const x=cmpSlice(s),u=useCmpUi(s.date),feed=useCmpFeed();
  const page=cmpMonthPage(s,u.month,u.filter);
  return {s,uid,x,u,feed,page,ac:cmpActionCenter(s),manage:cmpCanManage(s,uid)};
}
type C=ReturnType<typeof useCompliance>;

/** DEC-125: the calendar follows the business date on 1 April; a picked FY holds until the next 1 April (shown while it differs). */
const FY_RULE='On 1 April (business date) the calendar moves to the new financial year by itself. A year you pick here holds until the next 1 April.';
function FySelect({c,phone}:{c:C;phone?:boolean}){
  const {run,busy}=useHcc();const years:number[]=[];for(let y=CMP_FY_MIN;y<=CMP_FY_MAX;y++)years.push(y);const st=cmpFyStatus(c.s);
  return <label className={'cmp-fy'+(phone?' cmp-fy-m':'')} title={FY_RULE}>Financial year<select value={c.x.fyStart} disabled={busy}
    onChange={e=>{void run({type:'CMP_FY_SET',fy:Number(e.target.value),key:cmpActionKey(c.s,'fy',e.target.value)},'Financial year changed');}}>
    {years.map(y=><option key={y} value={y}>{cmpFyLabel(y)}</option>)}</select>
    {st.pickEnds&&st.next!==null&&<small className="cmp-fy-hint">Your pick · holds until {dateLabel(st.pickEnds)}{st.next!==st.fy?`, then ${cmpFyLabel(st.next)}`:''}</small>}</label>;
}
function FirmLine({c}:{c:C}){
  const f=c.x.profile;
  return <p className="cmp-firm-line"><Building2 size={15}/>{f?<><b>{f.name}</b> · <span className={'cmp-type-badge cmp-type-'+f.gstType}>{cmpTypeLabel(f.gstType)}</span>{f.qrmp&&<span className="cmp-qrmp-badge">QRMP</span>}
    {f.hasEmployees===false&&<span className="cmp-meta"> · No employees</span>}</>:<span>No firm profile yet — showing every card (SCC “All Firms View”). The Owner sets the profile to track filings.</span>}</p>;
}
/** SCC month nav (L2518–2527): the 12 pages with the overdue dot. */
function MonthTabs({c,phone}:{c:C;phone?:boolean}){
  return <div className={'cmp-mtabs'+(phone?' cmp-mtabs-m':'')} role="tablist" aria-label="Month">{CMP_MONTHS.map(m=>{const dot=cmpHasOverdueInMonth(c.s,m);
    return <button key={m} type="button" role="tab" aria-selected={m===c.u.month} className={m===c.u.month?'active':''} onClick={()=>cmpUiSet({view:'calendar',month:m})}>
      {phone?m.slice(0,3):m}{dot&&<span className="cmp-dot" aria-label="overdue"/>}</button>;})}</div>;
}
function Stats({c,phone}:{c:C;phone?:boolean}){
  const st=c.page.stats;const cell=(l:string,v:React.ReactNode,cls='')=><div className={cls}><span>{l}</span><strong>{v}</strong></div>;
  return <div className={'cmp-stats'+(phone?' cmp-stats-m':'')} aria-label="Month counters">{cell('Total',st.total)}{cell('Completed',st.completed,'done')}{cell('Pending',st.pending,'pend')}
    {cell('High Risk',st.highRisk,'risk')}{cell('Not Applicable',st.na,'na')}{cell('Overdue',st.overdue,st.overdue?'bad':'')}{cell('Evidence',st.evidence)}</div>;
}
function Filters({c}:{c:C}){
  const arch=c.x.archivedCats;const n=c.page.all.filter(v=>v.isNA).length;
  return <div className="cmp-cat-filter" role="group" aria-label="Category">{FILTERS.filter(f=>f.id==='all'||f.id==='na'||!arch.includes(f.id as never)).map(f=>
    <button key={f.id} type="button" aria-pressed={c.u.filter===f.id} className={c.u.filter===f.id?'active':''} onClick={()=>cmpUiSet({filter:f.id})}>{f.label}{f.id==='na'&&n?` (${n})`:''}</button>)}</div>;
}
function Strip({c}:{c:C}){
  const p=cmpPayableStrip(c.feed,c.s.date,c.u.month);if(!p)return null;
  return <p className="cmp-strip">{cmpInr(p.total)} statutory payable this month · PF {cmpInr(p.pf)} · ESIC {cmpInr(p.esic)} · PT {cmpInr(p.pt)} · GST {cmpInr(p.gst)} <span>· estimate — verify with CA</span></p>;
}
function Calendar({c,phone,fit,tools}:{c:C;phone?:boolean;fit?:boolean;tools?:React.ReactNode}){
  const p=c.page;
  useEffect(()=>{if(c.u.focus){const el=document.getElementById('cmp-card-'+c.u.focus);el?.scrollIntoView?.({behavior:'smooth',block:'center'});cmpUiSet({focus:undefined});}},[c.u.focus,c.u.month]);
  // An archived category cannot stay the active filter (SCC toggleCatArchive L1482).
  useEffect(()=>{if(c.u.filter!=='all'&&c.u.filter!=='na'&&c.x.archivedCats.includes(c.u.filter))cmpUiSet({filter:'all'});},[c.x.archivedCats,c.u.filter]);
  // FIT (desktop page): the month tabs stay one fixed row; the month's heading, control note, strip, counters, filter and cards scroll in one panel.
  if(fit)return <>
    <MonthTabs c={c}/>
    <ScrollPanel label={`Compliance cards · ${p.month} ${p.calYear}`} actions={tools} footer={<FitNote text={`${CMP_ILLUSTRATIVE} Nothing is filed from here: this is your tracker. Days left count from the business date.`}/>}>
      <div className="padded cmp-fit-body">
        <div className="cmp-mhd"><h2>{p.month} {p.calYear}</h2><span>{cmpFyLabel(p.fy)}</span></div>
        <details className="cmp-note" open><summary>⚡ Control Note</summary><p><CmpRich text={p.note}/></p></details>
        <Strip c={c}/>
        <Stats c={c}/>
        <Filters c={c}/>
        {!p.list.length?<Empty title="Nothing here" body={c.u.filter==='na'?'No item of this month is marked not applicable.':'No compliance obligations for this month.'}/>
          :<div className="cmp-cards">{p.list.map(v=><CmpCard key={v.card.id} v={v} feed={c.feed}/>)}</div>}
      </div>
    </ScrollPanel></>;
  return <>
    <MonthTabs c={c} phone={phone}/>
    <div className="cmp-mhd"><h2>{p.month} {p.calYear}</h2><span>{cmpFyLabel(p.fy)}</span></div>
    <details className="cmp-note" open={!phone}><summary>⚡ Control Note</summary><p><CmpRich text={p.note}/></p></details>
    <Strip c={c}/>
    <Stats c={c} phone={phone}/>
    <Filters c={c}/>
    {!p.list.length?<Empty title="Nothing here" body={c.u.filter==='na'?'No item of this month is marked not applicable.':'No compliance obligations for this month.'}/>
      :<div className="cmp-cards">{p.list.map(v=><CmpCard key={v.card.id} v={v} feed={c.feed} phone={phone}/>)}</div>}
  </>;
}
function useTools(c:C){
  const {open,toast,s}=useHcc();
  return {
    csv:()=>{const r=cmpCsv(s);if(!r){toast('Set the firm profile first to export its compliance register.',true);return;}void saveAndReport(saveCsv(r.fileName,r.csv),toast,`${r.fileName} saved · ${r.rows} rows`,'the compliance register');},
    zip:()=>{void cmpSaveZip(s,toast);},
    print:()=>open({type:'cmp-print',id:c.u.month}),
  };
}

/** FIT sub-screens of the desktop page (SUB_SCREENS block FIT-PEOPLE-A): the SCC Calendar / Dashboard views. */
const CMP_VIEWS=['calendar','dashboard'] as const;
type CmpView=typeof CMP_VIEWS[number];
const CMP_VIEW_LABEL:Record<CmpView,string>={calendar:'Calendar',dashboard:'Dashboard'};

export function CompliancePage(){
  const c=useCompliance();const {open}=useHcc();const t=useTools(c);
  // FIT (DEC-196): the view is a page tab. The shared UI store (cmpUiSet) stays the source the dashboard's and Action Center's "Open →" jumps
  // use; useFitTab adds go('compliance',{tab}) and the remembered tab, and the two are kept in step.
  const [fitView,setFitView]=useFitTab<CmpView>('compliance',CMP_VIEWS,'calendar');
  const lastView=useRef(c.u.view);
  useEffect(()=>{if(fitView!==c.u.view)cmpUiSet({view:fitView});},[fitView]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(()=>{if(c.u.view!==lastView.current){lastView.current=c.u.view;if(c.u.view!==fitView)setFitView(c.u.view);}},[c.u.view]); // eslint-disable-line react-hooks/exhaustive-deps
  const choose=(v:CmpView)=>{setFitView(v);cmpUiSet({view:v});};
  if(!cmpHasAccess(c.s,c.uid))return <FitPage title="Compliance calendar"><Empty title="Owner and Accountant only" body="The compliance calendar is visible to the Owner and the Accountant."/></FitPage>;
  // Header: Action Center. Toolbar: FY, firm line and the firm's settings (Firm profile, Manage). The export tools sit in the panel's fixed head,
  // always visible on both views (one header row holds them only at desktop widths).
  const tools=<div className="cmp-tools"><button className="btn small-btn" onClick={t.csv}><FileSpreadsheet size={14}/>CSV register</button><button className="btn small-btn" onClick={t.print}><Printer size={14}/>Print month</button>
    <button className="btn small-btn" onClick={t.zip}><Archive size={14}/>Evidence ZIP</button><button className="btn small-btn" onClick={()=>open({type:'cmp-gstr1'})}><Download size={14}/>GSTR-1 working summary</button>
    <button className="btn small-btn" onClick={()=>open({type:'cmp-pack'})}><Send size={14}/>CA pack</button></div>;
  return <FitPage title="Compliance calendar" sub="SCC Compliance Operating System — income tax, GST, composition and labour-law filings for the whole financial year, with ownership, internal deadlines, escalation and evidence."
      actions={<button className="btn" onClick={()=>open({type:'cmp-action'})}><Bell size={15}/>Action Center{c.ac.length>0&&<span className="cmp-badge">{cmpBadgeText(c.ac.length)}</span>}</button>}
      toolbar={<div className="cmp-toolbar cmp-fit-toolbar"><FySelect c={c}/><FirmLine c={c}/>
        <span className="cmp-head-actions"><button className="btn" onClick={()=>open({type:'cmp-profile'})}><Building2 size={15}/>Firm profile</button>
          {c.manage&&<button className="btn" onClick={()=>open({type:'cmp-manage'})}><Settings2 size={15}/>Manage compliance</button>}</span></div>}
      tabs={<FitTabs page="compliance" value={c.u.view} onChange={choose} tabs={CMP_VIEWS.map(id=>({id,label:CMP_VIEW_LABEL[id]}))}/>}>
    {c.u.view==='dashboard'
      ?<ScrollPanel label="Compliance dashboard" actions={tools} footer={<FitNote text={`${CMP_ILLUSTRATIVE} Nothing is filed from here: this is your tracker. Days left count from the business date.`}/>}><div className="padded cmp-fit-body"><CmpDashboard feed={c.feed}/></div></ScrollPanel>
      :<Calendar c={c} fit tools={tools}/>}
  </FitPage>;
}

export function ComplianceMobile(){
  const c=useCompliance();const {open}=useHcc();const t=useTools(c);
  if(!cmpHasAccess(c.s,c.uid))return <Empty title="Owner and Accountant only"/>;
  return <div className="cmp-m">
    <FySelect c={c} phone/><FirmLine c={c}/>
    <div className="cmp-m-row"><button type="button" className="btn cmp-m-btn" onClick={()=>open({type:'cmp-action'})}><Bell size={18}/>Action Center{c.ac.length>0&&<span className="cmp-badge">{cmpBadgeText(c.ac.length)}</span>}</button>
      <button type="button" className="btn cmp-m-btn" aria-pressed={c.u.view==='dashboard'} onClick={()=>cmpUiSet({view:c.u.view==='dashboard'?'calendar':'dashboard'})}>
        {c.u.view==='dashboard'?<><CalendarDays size={18}/>Calendar</>:<><LayoutDashboard size={18}/>Dashboard</>}</button></div>
    {c.u.view==='dashboard'?<CmpDashboard feed={c.feed}/>:<Calendar c={c} phone/>}
    <div className="cmp-m-more"><button type="button" className="btn" onClick={()=>open({type:'cmp-profile'})}><Building2 size={18}/>Firm profile</button>
      {c.manage&&<button type="button" className="btn" onClick={()=>open({type:'cmp-manage'})}><Settings2 size={18}/>Manage compliance</button>}
      <button type="button" className="btn" onClick={t.csv}><FileSpreadsheet size={18}/>CSV register</button><button type="button" className="btn" onClick={t.print}><Printer size={18}/>Print month</button>
      <button type="button" className="btn" onClick={t.zip}><Archive size={18}/>Evidence ZIP</button><button type="button" className="btn" onClick={()=>open({type:'cmp-gstr1'})}><Download size={18}/>GSTR-1 working summary</button>
      <button type="button" className="btn" onClick={()=>open({type:'cmp-pack'})}><Send size={18}/>CA pack (simulated)</button></div>
    <p className="cmp-m-note">{CMP_ILLUSTRATIVE}</p>
  </div>;
}
