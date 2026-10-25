// W05-A · Master configuration (page id `settings`; renamed from "Hotel configuration", DEC-057). Desktop and tablet: the 12 groups
// with search and live summaries on the left, the selected group's settings beside it. Everyone who opens the page sees every group;
// groups they cannot change are read-only with the reason (never hidden or redirected — ETP lesson). Tariff schedules (SCR-18,
// BL-027) live in Rooms and rate plans; the dated GST master (DEC-069) in Money and tax. Registry: domain/streams/config.ts.
import React,{useState} from 'react';
import {Lock,CalendarClock,Search,ChevronRight} from 'lucide-react';
import {money,rateFor,adminRoomTypes,adminBaseRate,gstMasterSummary,GST_ILLUSTRATIVE,authorize,cfgGroup,cfgCanChange,cfgDisplay,cfgScheduled,cfgShow,cfgPendingRequests,cfgHistory,CFG_SECTIONS,
  type CfgGroupId,type CfgSettingDef} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty,shortDate} from '../../components/ui';
import {GuardedButton} from './parts';
import {cfgListRows,cfgDetailDefs,cfgNav} from './config/logic';
import {CfgModulesPanel,CfgDataPanel,CfgGroupLink} from './config/panels';

/** Owner-only block text for the legacy tariff/tax schedules (PERM-ADM-003; Tech admin view-only for money — DEC-058). */
export function scheduleBlock(s:ReturnType<typeof useHcc>['s'],uid:string){
  const d=authorize(s,uid,'PERM-ADM-003',{act:true});
  return d.decision==='allow'?undefined:d.decision==='approval'?'Request only — ask the Owner':s.users.find(u=>u.id===uid)?.role==='Tech admin'?'Owner changes this — money and tax are Owner-only (DEC-058)':d.reason;
}
export function HotelSettings(){
  const {s,uid}=useHcc();
  const [q,setQ]=useState('');
  const [sel,setSel]=useState<CfgGroupId>(()=>{const g=cfgNav.group;cfgNav.group=undefined;return g??'property';});
  const rows=cfgListRows(s,uid,q);
  const current=rows.some(r=>r.id===sel)?sel:rows[0]?.id;
  return <><PageTitle title="Master configuration" sub="Every setting in one place — 12 groups, dated values, approvals, history and modules on/off." action={<Pill tone="orange">Fictional property · GST illustrative</Pill>}/>
    <div className="cfg-hub">
      <aside className="card cfg-list" aria-label="Configuration groups">
        <label className="cfg-search"><Search size={17}/><input type="search" aria-label="Search settings" placeholder="Search settings (e.g. GSTIN, deposit, PF)" value={q} onChange={e=>setQ(e.target.value)}/></label>
        {!rows.length&&<p className="muted padded">No setting matches “{q}”.</p>}
        {CFG_SECTIONS.map(sec=>{const list=rows.filter(r=>r.section===sec);if(!list.length)return null;
          return <div key={sec} className="cfg-sec"><h2>{sec}</h2><ul>{list.map(r=><li key={r.id}>
            <button type="button" className={'cfg-item'+(r.id===current?' on':'')} aria-current={r.id===current?'true':undefined} onClick={()=>setSel(r.id)}>
              <span><strong>{r.label}</strong><small>{r.summary}{r.hits?` · ${r.hits} match${r.hits>1?'es':''}`:''}</small></span>
              {r.readOnly&&<span className="cfg-ro"><Lock size={12}/> Read only</span>}<ChevronRight size={16} aria-hidden="true"/></button></li>)}</ul></div>;})}
      </aside>
      <section className="card cfg-detail">{current?<CfgGroupDetail g={current} q={q}/>:<Empty title="Nothing found" body="Try another word."/>}</section>
    </div></>;
}

export function CfgGroupDetail({g,q}:{g:CfgGroupId;q:string}){
  const {s,uid}=useHcc();
  const grp=cfgGroup(g)!;const row=cfgListRows(s,uid,'').find(r=>r.id===g)!;
  const defs=cfgDetailDefs(s,g,q);const block=scheduleBlock(s,uid);
  return <>
    <div className="cfg-head"><div><h2>{grp.label}</h2><p>{grp.blurb}</p></div><CfgGroupLink g={g}/></div>
    {row.readOnly&&<div className="info-note adm-banner cfg-readonly"><Lock size={16}/><span>Read only — {row.reason}</span></div>}
    {g==='rooms'&&!q&&<Tariffs block={block}/>}
    {g==='money'&&!q&&<GstPanel/>}
    {g==='modules'&&<CfgModulesPanel/>}
    {g!=='modules'&&(defs.length?<ul className="cfg-rows">{defs.map(d=><CfgRow key={d.key} def={d}/>)}</ul>:!['rooms','money','data'].includes(g)||q?<Empty title="No matching settings in this group"/>:null)}
    {g==='people'&&<p className="muted padded">Dated statutory rates, leave policy and weekly offs from the payroll module join this group through the settings provider API.</p>}
    {g==='data'&&!q&&<CfgDataPanel/>}
  </>;
}
function CfgRow({def}:{def:CfgSettingDef}){
  const {s,uid,open,busy}=useHcc();
  const d=cfgCanChange(s,uid,def);const sched=cfgScheduled(s,def.key);const pend=cfgPendingRequests(s,def.key);const h=cfgHistory(s,def.key);
  return <li className="cfg-row">
    <div className="cfg-row-main"><strong>{def.label}</strong><small>{def.help}{def.illustrative?' ':''}</small>{def.illustrative&&<Pill tone="orange">Illustrative — CA to confirm</Pill>}</div>
    <div className="cfg-row-val"><strong>{cfgDisplay(s,uid,def)}</strong>{sched.map(x=><small key={x.id}><CalendarClock size={12}/> {cfgShow(def,x.value)} from {shortDate(x.effectiveFrom)}</small>)}
      {pend.length>0&&<Pill tone="orange">Approval pending</Pill>}</div>
    <div className="cfg-row-meta"><small>{def.effective}</small><small>{def.approval}</small>{h.length>0&&<small>{h.length} change{h.length>1?'s':''} · last {h[0].before} → {h[0].after}</small>}</div>
    <div className="cfg-row-act">{def.editable?<GuardedButton block={d.mode==='deny'?d.reason:undefined} busy={busy} onClick={()=>open({type:'cfg-setting',id:def.key})}>{d.mode==='request'?'Request change…':'Change…'}</GuardedButton>
      :<button type="button" className="btn small-btn" onClick={()=>open({type:'cfg-setting',id:def.key})}><Lock size={12}/> Details</button>}</div>
  </li>;
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
/** DEC-069 GST master — the rules in force today. Each rate is a dated row below (Change… = effective date, history, revert). */
function GstPanel(){
  const {s}=useHcc();
  return <div className="padded">
    <div className="adm-row-head"><p className="muted">GST master in force on {shortDate(s.date)} — intra-state, shown as CGST + SGST halves. Each rate below is a dated value: changes apply from their effective date; posted lines keep their tax snapshot. <Pill tone="orange">{GST_ILLUSTRATIVE}</Pill></p></div>
    <div className="table-wrap"><table className="adm-table"><thead><tr><th>Supply</th><th>GST</th><th>Notes</th></tr></thead><tbody>
      {gstMasterSummary(s,s.date).map(([a,b,c])=><tr key={a}><td><strong>{a}</strong></td><td><strong>{b}</strong></td><td className="adm-wrap">{c}</td></tr>)}
    </tbody></table></div></div>;
}
