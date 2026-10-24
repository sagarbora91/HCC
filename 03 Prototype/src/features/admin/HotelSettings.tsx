// SCR-18 Hotel configuration (BL-027): categories, effective dates, impact preview, history, safety invariants.
import React,{useState} from 'react';
import {DisplaySection} from '../../app/display';
import {Info,RotateCcw,Lock,CalendarClock} from 'lucide-react';
import {money,authorize,rateFor,adminSettingValue,adminSettingHistory,adminRoomTypes,adminBaseRate,adminTaxScheduled,ADMIN_SETTINGS,ADMIN_CONFIG_CATEGORIES} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty,shortDate} from '../../components/ui';
import {GuardedButton,Tabs} from './parts';

const INVARIANTS:[string,string][]=[
  ['Authorisation enforcement','Every command is checked against module access, the access policy and separation of duty. No setting can switch this off.'],
  ['Room overlap protection','A room can never hold two active stays for the same night.'],
  ['Duplicate payment protection','Each action carries an idempotency key; repeating it never posts a second receipt.'],
  ['Audit history','Every change is written to the audit log with actor and simulated time. The log cannot be disabled or edited.'],
  ['Demonstration watermark','Printed documents always say “Demonstration — not a tax invoice / fictional”.'],
];
export function HotelSettings(){
  const {s,uid,open,busy,display}=useHcc();
  const [cat,setCat]=useState<string>('Property');
  const d=authorize(s,uid,'PERM-ADM-003',{act:true});
  const block=d.decision==='allow'?undefined:d.decision==='approval'?'Request only — ask the Owner':s.users.find(u=>u.id===uid)?.role==='Tech admin'?'View only for Tech admin (PERM-ADM-003)':d.reason;
  const defs=ADMIN_SETTINGS.filter(x=>x.category===cat);
  return <><PageTitle title="Hotel configuration" sub="Operating policies by category — with effective dates, impact preview and history." action={<Pill tone="orange">Fictional property · synthetic tax</Pill>}/>
    {block&&<div className="info-note adm-banner"><Lock size={16}/><span>{block}. You can review every setting and its history.</span></div>}
    <section className="card"><Tabs tabs={ADMIN_CONFIG_CATEGORIES} value={cat} onChange={setCat}/>
      {cat==='Rooms & tariffs'&&<Tariffs block={block}/>}
      {cat==='Finance & synthetic tax'&&<TaxPanel block={block}/>}
      {defs.length>0&&<div className="table-wrap"><table className="adm-table"><thead><tr><th>Setting</th><th>Type · unit</th><th>Current value</th><th>Effective</th><th>Approval need</th><th>History</th><th></th></tr></thead><tbody>
        {defs.map(x=>{const h=adminSettingHistory(s,x.key);return <tr key={x.key}><td className="adm-wrap"><strong>{x.label}</strong><small>{x.key} · {x.help}</small></td><td>{x.type}<small>{x.unit}</small></td><td><strong>{adminSettingValue(s,x.key)}</strong></td>
          <td className="adm-wrap">{h[0]?shortDate(h[0].effectiveFrom):'Fixture'}<small>{x.effective}</small></td><td className="adm-wrap"><small>{x.approval}</small></td>
          <td>{h.length?<>{h.length} change{h.length>1?'s':''}<small>last {h[0].before} → {h[0].after}</small></>:<span className="muted">None</span>}</td>
          <td>{x.editable&&x.key!=='taxBps'?<GuardedButton block={block} busy={busy} onClick={()=>open({type:'admin-setting',id:x.key})}>Change…</GuardedButton>:x.key==='taxBps'?<GuardedButton block={block} busy={busy} onClick={()=>open({type:'admin-tax'})}>Schedule…</GuardedButton>:<Pill><Lock size={11}/> Fixed</Pill>}</td></tr>;})}
      </tbody></table></div>}
      {cat==='Stay timings'&&<p className="muted padded">Changing a time shows a warning listing the open stays affected today before you save. Booking dates and charges never change.</p>}
    </section>
    <DisplaySection display={display}/>
    <div className="two-col adm-two">
      <section className="card"><div className="section-title"><h2>Not configurable (safety invariants)</h2><Lock size={18}/></div>
        <div className="summary-list padded adm-inv">{INVARIANTS.map(([k,v])=><div key={k}><span><strong>{k}</strong><br/>{v}</span><strong>Always on</strong></div>)}</div></section>
      <div>
        <section className="card"><div className="section-title"><h2>Prototype boundaries</h2><Info size={18}/></div><div className="prose padded">
          <p><strong>Local and fictional.</strong> Data is stored in this browser when supported. Another browser or device has its own data.</p>
          <p><strong>No production connections.</strong> No SQL Server, bank gateway, real authentication, guest documents or live GST validation.</p>
          <p><strong>Browser storage is not backup.</strong> Clearing browser data removes demo edits. Any scenario can always be reloaded.</p></div></section>
        <section className="card"><div className="section-title"><h2>Start fresh</h2></div><div className="padded"><p>Reload the current scenario ({s.scenario}) or choose another from the demo toolbar.</p><button className="btn danger" onClick={()=>open({type:'reset'})}><RotateCcw size={16}/> Reset demo data</button></div></section>
      </div></div></>;
}

function Tariffs({block}:{block?:string}){
  const {s,open,run,busy}=useHcc();
  const types=adminRoomTypes(s);
  return <div className="padded">
    <div className="adm-row-head"><p className="muted">Effective-dated tariffs. A booking takes the tariff valid on its arrival date and keeps it; posted room lines are never re-priced.</p>
      <GuardedButton className="btn primary" block={block} busy={busy} onClick={()=>open({type:'admin-tariff'})}><CalendarClock size={16}/> Schedule tariff</GuardedButton></div>
    <div className="table-wrap"><table className="adm-table"><thead><tr><th>Room type</th><th>Rooms</th><th>Base (fixture)</th><th>Today ({shortDate(s.date)})</th><th>Scheduled</th><th></th></tr></thead><tbody>
      {types.map(t=>{const room=s.rooms.find(r=>r.type===t)!;const sched=(s.tariffs||[]).filter(x=>x.type===t).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1);
        return <tr key={t}><td><strong>{t}</strong></td><td>{s.rooms.filter(r=>r.type===t).length}</td><td>{money(adminBaseRate(s,t))}</td><td><strong>{money(rateFor(s,room,s.date))}</strong></td>
          <td className="adm-wrap">{sched.length?sched.map(x=><div key={x.effectiveFrom} className="adm-sched">{money(x.rate)} from {shortDate(x.effectiveFrom)}{x.effectiveFrom<=s.date?<Pill tone="green">In effect</Pill>:<><Pill tone="blue">Future</Pill><GuardedButton className="text-btn" block={block} busy={busy} onClick={()=>run({type:'TARIFF_SCHEDULE',roomType:t,effectiveFrom:x.effectiveFrom,remove:true,key:`tariff-rm:${t}:${x.effectiveFrom}:${s.sequence}`},'Future tariff withdrawn')}>Withdraw</GuardedButton></>}</div>):<span className="muted">None</span>}</td>
          <td><GuardedButton className="text-btn" block={block} busy={busy} onClick={()=>open({type:'admin-tariff',room:t})}>Schedule…</GuardedButton></td></tr>;})}
    </tbody></table></div></div>;
}
function TaxPanel({block}:{block?:string}){
  const {s,run,busy}=useHcc();
  const list=[...(s.taxSchedule||[])].sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1);
  return <div className="padded">
    <p className="muted">Synthetic demonstration tax for this prototype — not a GST rate and never presented as compliant. In effect today: <strong>{adminTaxScheduled(s,s.date)/100}%</strong>{list.length?'':` (base setting)`}.</p>
    {list.length>0?<div className="table-wrap"><table className="adm-table"><thead><tr><th>Rate</th><th>Effective from</th><th>Status</th><th></th></tr></thead><tbody>
      {list.map(x=><tr key={x.effectiveFrom}><td><strong>{x.bps/100}%</strong></td><td>{shortDate(x.effectiveFrom)}</td><td>{x.effectiveFrom<=s.date?<Pill tone="green">In effect / past</Pill>:<Pill tone="blue">Future</Pill>}</td>
        <td>{x.effectiveFrom>s.date&&<GuardedButton className="text-btn" block={block} busy={busy} onClick={()=>run({type:'TAX_SCHEDULE',effectiveFrom:x.effectiveFrom,remove:true,key:`tax-rm:${x.effectiveFrom}:${s.sequence}`},'Future tax rate withdrawn')}>Withdraw</GuardedButton>}</td></tr>)}
    </tbody></table></div>:<Empty title="No scheduled tax changes" body="Use “Schedule…” on the synthetic tax row to add an effective-dated rate."/>}
  </div>;
}
