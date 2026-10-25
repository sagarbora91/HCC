// W05-A · M21 phone Master configuration: grouped list with search and live summaries → group details → Back (button and Android Back,
// via useStepBack). Setting cards open the 'cfg-setting' sheet (value, effective date, what it affects, history + revert). Read-only
// groups show the reason. Tariff schedules and the dated GST master rows (DEC-069) stay editable for the Owner; modules on/off and data panels are shared.
import React,{useState} from 'react';
import {Lock,ArrowLeft,CalendarClock,Search} from 'lucide-react';
import {money,rateFor,adminRoomTypes,adminBaseRate,gstMasterSummary,cfgGroup,cfgCanChange,cfgDisplay,cfgScheduled,cfgShow,cfgPendingRequests,CFG_SECTIONS,
  type CfgGroupId} from '../../services/mock';
import {useHcc} from '../../app/context';
import {useStepBack} from '../../app/native';
import {ListCard} from '../../app/mobile/components';
import {Empty,shortDate} from '../../components/ui';
import {MaTag,MaIntro,MaGuard,MaRows} from './parts';
import {cfgListRows,cfgDetailDefs,cfgNav} from '../../features/admin/config/logic';
import {CfgModulesPanel,CfgDataPanel,CfgGroupLink} from '../../features/admin/config/panels';
import {scheduleBlock} from '../../features/admin/HotelSettings';

export function MaSettings(){
  const {s,uid}=useHcc();
  const [q,setQ]=useState('');
  const [sel,setSel]=useState<CfgGroupId|''>(()=>{const g=cfgNav.group;cfgNav.group=undefined;return g??'';});
  useStepBack(!!sel,()=>setSel(''));
  if(sel)return <MaGroup g={sel} q={q} onBack={()=>setSel('')}/>;
  const rows=cfgListRows(s,uid,q);
  return <div className="ma-page cfg-m">
    <div className="ma-badges"><MaTag tone="warn">Fictional property · GST illustrative</MaTag></div>
    <label className="cfg-search cfg-m-search"><Search size={18}/><input type="search" aria-label="Search settings" placeholder="Search settings" value={q} onChange={e=>setQ(e.target.value)} enterKeyHint="search"/></label>
    {!rows.length&&<Empty title="No setting matches" body="Try another word."/>}
    {CFG_SECTIONS.map(sec=>{const list=rows.filter(r=>r.section===sec);if(!list.length)return null;
      return <section key={sec}><h2 className="ma-h2">{sec}</h2>
        {list.map(r=><ListCard key={r.id} title={r.label} sub={r.summary+(r.hits?` · ${r.hits} match${r.hits>1?'es':''}`:'')} right={r.readOnly?<MaTag><Lock size={13}/> Read only</MaTag>:undefined} onClick={()=>setSel(r.id)}/>)}</section>;})}
  </div>;
}
function MaGroup({g,q,onBack}:{g:CfgGroupId;q:string;onBack:()=>void}){
  const {s,uid,open,busy}=useHcc();
  const grp=cfgGroup(g)!;const row=cfgListRows(s,uid,'').find(r=>r.id===g)!;
  const defs=cfgDetailDefs(s,g,q);const block=scheduleBlock(s,uid);
  return <div className="ma-page cfg-m">
    <button type="button" className="btn ma-back" onClick={onBack}><ArrowLeft size={18}/>All groups</button>
    <h2 className="ma-h2">{grp.label}</h2><MaIntro>{grp.blurb}</MaIntro><CfgGroupLink g={g}/>
    {row.readOnly&&<div className="ma-note"><Lock size={18}/><span>Read only — {row.reason}</span></div>}
    {g==='rooms'&&!q&&<Tariffs block={block}/>}
    {g==='money'&&!q&&<Tax block={block}/>}
    {g==='modules'&&<CfgModulesPanel/>}
    {g!=='modules'&&defs.map(d=>{const dec=cfgCanChange(s,uid,d);const sched=cfgScheduled(s,d.key);const pend=cfgPendingRequests(s,d.key).length>0;
      return <article className="ma-card" key={d.key} aria-label={d.label}>
        <div className="ma-card-top"><div><strong>{d.label}</strong><span>{d.help}</span></div></div>
        {d.illustrative&&<MaTag tone="warn">Illustrative — CA to confirm</MaTag>}
        <MaRows rows={[['Current value',<strong>{cfgDisplay(s,uid,d)}</strong>],...sched.map(x=>[`From ${shortDate(x.effectiveFrom)}`,cfgShow(d,x.value)] as [string,string]),['Effective',d.effective],['Who can change',d.approval]]}/>
        {pend&&<MaTag tone="warn">Approval pending</MaTag>}
        {d.editable?<MaGuard block={dec.mode==='deny'?dec.reason:undefined} busy={busy} onClick={()=>open({type:'cfg-setting',id:d.key})}>{dec.mode==='request'?'Request change…':'Change… (what it affects)'}</MaGuard>
          :<button type="button" className="btn" onClick={()=>open({type:'cfg-setting',id:d.key})}><Lock size={15}/> Details</button>}
      </article>;})}
    {g!=='modules'&&!defs.length&&(q||!['rooms','money','data'].includes(g))&&<Empty title="No matching settings in this group"/>}
    {g==='data'&&!q&&<CfgDataPanel/>}
  </div>;
}
function Tariffs({block}:{block?:string}){
  const {s,open,run,busy}=useHcc();
  return <>
    <MaIntro>A booking takes the tariff valid on its arrival date and keeps it; posted room lines are never re-priced.</MaIntro>
    {adminRoomTypes(s).map(t=>{const room=s.rooms.find(r=>r.type===t)!;const sched=(s.tariffs||[]).filter(x=>x.type===t).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1);
      return <article className="ma-card" key={t} aria-label={t}><div className="ma-card-top"><div><strong>{t}</strong><span>{s.rooms.filter(r=>r.type===t).length} rooms</span></div><strong className="ma-amt number">{money(rateFor(s,room,s.date))}</strong></div>
        <MaRows rows={[['Base (fixture)',money(adminBaseRate(s,t))],['Today',money(rateFor(s,room,s.date))]]}/>
        {sched.map(x=><div className="ma-sched" key={x.effectiveFrom}><span>{money(x.rate)} from {shortDate(x.effectiveFrom)}</span>{x.effectiveFrom<=s.date?<MaTag tone="ok">In effect</MaTag>:<><MaTag tone="info">Future</MaTag>
          <MaGuard className="btn" block={block} busy={busy} onClick={()=>run({type:'TARIFF_SCHEDULE',roomType:t,effectiveFrom:x.effectiveFrom,remove:true,key:`tariff-rm:${t}:${x.effectiveFrom}:${s.sequence}`},'Future tariff withdrawn')}>Withdraw</MaGuard></>}</div>)}
        <MaGuard block={block} busy={busy} onClick={()=>open({type:'admin-tariff',room:t})}><CalendarClock size={18}/>Schedule tariff…</MaGuard>
      </article>;})}
  </>;
}
function Tax(_p:{block?:string}){
  const {s}=useHcc();
  return <>
    <MaIntro>GST master in force today (CGST + SGST halves). Change each rate below with an effective date. Illustrative — CA to confirm.</MaIntro>
    {gstMasterSummary(s,s.date).map(([a,b,c])=><div className="ma-sched ma-card" key={a}><span><strong>{b}</strong> {a}</span><MaTag tone="info">{c.split(' · ')[0]}</MaTag></div>)}
  </>;
}
