// M21 phone Hotel configuration: category list → settings detail (Change… opens the setting sheet with impact preview),
// tariff and synthetic-tax schedules editable, Device & display section (per device), safety invariants.
import React,{useState} from 'react';
import {Lock,ArrowLeft,CalendarClock,ShieldCheck} from 'lucide-react';
import {money,authorize,rateFor,adminSettingValue,adminSettingHistory,adminRoomTypes,adminBaseRate,adminTaxScheduled,ADMIN_SETTINGS,ADMIN_CONFIG_CATEGORIES} from '../../services/mock';
import {useHcc} from '../../app/context';
import {DisplaySection} from '../../app/display';
import {ListCard} from '../../app/mobile/components';
import {Empty,shortDate} from '../../components/ui';
import {MaTag,MaIntro,MaGuard,MaRows} from './parts';

const INVARIANTS=['Authorisation enforcement','Room overlap protection','Duplicate payment protection','Audit history','Demonstration watermark'];
export function MaSettings(){
  const {s,uid,display}=useHcc();
  const [cat,setCat]=useState<string>('');
  const d=authorize(s,uid,'PERM-ADM-003',{act:true});
  const block=d.decision==='allow'?undefined:d.decision==='approval'?'Request only — ask the Owner':s.users.find(u=>u.id===uid)?.role==='Tech admin'?'View only for Tech admin (PERM-ADM-003)':d.reason;
  if(cat)return <div className="ma-page">
    <button type="button" className="btn ma-back" onClick={()=>setCat('')}><ArrowLeft size={18}/>All categories</button>
    <h2 className="ma-h2">{cat}</h2>
    {block&&<div className="ma-note"><Lock size={18}/><span>{block}. You can review every setting and its history.</span></div>}
    {cat==='Rooms & tariffs'&&<Tariffs block={block}/>}
    {cat==='Finance & synthetic tax'&&<Tax block={block}/>}
    <SettingCards cat={cat} block={block}/>
    {cat==='Stay timings'&&<MaIntro>Changing a time shows the open stays affected today before you save. Booking dates and charges never change.</MaIntro>}
  </div>;
  return <div className="ma-page">
    <div className="ma-badges"><MaTag tone="warn">Fictional property · synthetic tax</MaTag></div>
    {block&&<div className="ma-note"><Lock size={18}/><span>{block}. You can review every setting and its history.</span></div>}
    <h2 className="ma-h2">Categories</h2>
    {ADMIN_CONFIG_CATEGORIES.map(c=>{const n=ADMIN_SETTINGS.filter(x=>x.category===c).length;
      const extra=c==='Rooms & tariffs'?`${adminRoomTypes(s).length} room types · tariffs`:c==='Finance & synthetic tax'?`Synthetic tax ${adminTaxScheduled(s,s.date)/100}% today`:'';
      return <ListCard key={c} title={c} sub={[n?`${n} setting${n>1?'s':''}`:'',extra].filter(Boolean).join(' · ')||'Read only'} onClick={()=>setCat(c)}/>;})}
    <div className="ma-display"><DisplaySection display={display}/></div>
    <details className="ma-card ma-inv"><summary><ShieldCheck size={18}/>Not configurable (safety invariants)</summary><ul>{INVARIANTS.map(x=><li key={x}>{x} — <b>always on</b></li>)}</ul></details>
  </div>;
}
function SettingCards({cat,block}:{cat:string;block?:string}){
  const {s,open,busy}=useHcc();
  const defs=ADMIN_SETTINGS.filter(x=>x.category===cat);
  if(!defs.length)return cat==='Rooms & tariffs'?null:<Empty title="No settings in this category"/>;
  return <>{defs.map(x=>{const h=adminSettingHistory(s,x.key);const tax=x.key==='taxBps';return <article className="ma-card" key={x.key} aria-label={x.label}>
    <div className="ma-card-top"><div><strong>{x.label}</strong><span>{x.help}</span></div></div>
    <MaRows rows={[['Current value',<strong>{adminSettingValue(s,x.key)}</strong>],['Type · unit',`${x.type} · ${x.unit}`],['Effective',h[0]?`${shortDate(h[0].effectiveFrom)} · ${x.effective}`:x.effective],['Approval need',x.approval],['History',h.length?`${h.length} change${h.length>1?'s':''} · last ${h[0].before} → ${h[0].after}`:'None']]}/>
    {x.editable||tax?<MaGuard block={block} busy={busy} onClick={()=>open(tax?{type:'admin-tax'}:{type:'admin-setting',id:x.key})}>{tax?'Schedule…':'Change… (impact preview)'}</MaGuard>:<MaTag><Lock size={13}/> Fixed</MaTag>}
  </article>;})}</>;
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
function Tax({block}:{block?:string}){
  const {s,run,busy}=useHcc();
  const list=[...(s.taxSchedule||[])].sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1);
  return <>
    <MaIntro>Synthetic demonstration tax — not a GST rate. In effect today: <strong>{adminTaxScheduled(s,s.date)/100}%</strong>.</MaIntro>
    {list.map(x=><div className="ma-sched ma-card" key={x.effectiveFrom}><span><strong>{x.bps/100}%</strong> from {shortDate(x.effectiveFrom)}</span>{x.effectiveFrom<=s.date?<MaTag tone="ok">In effect / past</MaTag>:<><MaTag tone="info">Future</MaTag>
      <MaGuard className="btn" block={block} busy={busy} onClick={()=>run({type:'TAX_SCHEDULE',effectiveFrom:x.effectiveFrom,remove:true,key:`tax-rm:${x.effectiveFrom}:${s.sequence}`},'Future tax rate withdrawn')}>Withdraw</MaGuard></>}</div>)}
  </>;
}
