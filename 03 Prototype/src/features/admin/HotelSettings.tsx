// W05-A · Master configuration (page id `settings`; renamed from "Hotel configuration", DEC-057). Desktop and tablet: the 12 groups
// with search and live summaries on the left, the selected group's settings beside it. Everyone who opens the page sees every group;
// groups they cannot change are read-only with the reason (never hidden or redirected — ETP lesson). Tariff schedules (SCR-18,
// BL-027) live in Rooms and rate plans; the dated GST master (DEC-069) in Money and tax. Registry: domain/streams/config.ts.
import React,{useState,useEffect} from 'react';
import {Lock,CalendarClock,Search,ChevronRight} from 'lucide-react';
import {money,rateFor,adminRoomTypes,adminBaseRate,gstMasterSummary,GST_ILLUSTRATIVE,authorize,mstSeesMoney,cfgGroup,cfgDefs,cfgCanChange,cfgDisplay,cfgScheduled,cfgShow,cfgPendingRequests,cfgHistory,CFG_SECTIONS,
  type CfgGroupId,type CfgSettingDef,rmsTypeName} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill,Empty,shortDate} from '../../components/ui';
import {FitPage,ScrollPanel,SplitView,useFitTab,useWideSplit} from '../../components/fit';
import {GuardedButton} from './parts';
import {cfgListRows,cfgDetailDefs,cfgNav,cfgMoneyMask} from './config/logic';
import {CfgModulesPanel,CfgDataPanel,CfgGroupLink} from './config/panels';
import {ServerDevicePanel} from '../../clients/sync/react'; // production build only (W4-12: server address, device registration)
import {MstListsBlock} from '../masters/MasterList'; // MA-0: "Lists" block (masters of the group)

/** Owner-only block text for the legacy tariff/tax schedules (PERM-ADM-003; Tech admin view-only for money — DEC-058). */
export function scheduleBlock(s:ReturnType<typeof useHcc>['s'],uid:string){
  const d=authorize(s,uid,'PERM-ADM-003',{act:true});
  return d.decision==='allow'?undefined:d.decision==='approval'?'Request only — ask the Owner':s.users.find(u=>u.id===uid)?.role==='Tech admin'?'Owner changes this — money and tax are Owner-only (DEC-058)':d.reason;
}
/** FIT-2: 2-3 headline current values per group for the rail (read-only, from the live registry; secrets and Owner-only values are never shown, money is masked like the summary). */
const HEAD_KEYS:Partial<Record<CfgGroupId,string[]>>={
  property:['hotel','brand.printFormat','brand.language'],rooms:['yld.floorBps','yld.ceilBps'],frontdesk:['checkin','checkout','fd.deposit'],
  money:['currency','invoicePrefix','discountLimit'],pos:['pos.outlets'],hkm:['ai.dirtyHours','hk.maxRoomsPerAttendant','mnt.pmLeadDays'],
  users:['users.active','sec.pinLock'],messages:['msg.senderName','msg.marketing'],data:['data.backupTime','data.backupKeep','data.restoreDrillDays'],
};
export function cfgHeads(s:ReturnType<typeof useHcc>['s'],uid:string,g:CfgGroupId):string{
  if(g==='modules')return '';
  const defs=cfgDefs(s).filter(d=>d.group===g&&!d.panel&&!d.secret&&d.visibility!=='owner');
  const want=HEAD_KEYS[g];
  const pick=(want?want.map(k=>defs.find(d=>d.key===k)).filter((d):d is CfgSettingDef=>!!d):[]);
  const list=(pick.length?pick:defs).slice(0,3);
  return list.map(d=>`${d.label.replace(/ \(.*\)$/,'')}: ${cfgMoneyMask(s,uid,cfgDisplay(s,uid,d))}`).join(' · ');
}
const GROUP_IDS=['property','rooms','frontdesk','money','pos','hkm','people','compliance','modules','users','messages','data'] as const;
/** True below 900 px (the SplitView slide-over breakpoint): the group list is the first thing seen, a group opens over it. */
// FIT (DEC-194, contract §6 'settings' = split; FitPage keeps the default layout because .fit-page.fit-split would collide with SplitView's own .fit-split grid class): SplitView — the 12 groups with live summaries (ScrollPanel) on the left, the selected group's
// settings and Lists (masters) in the scrolling record pane on the right. The group selection is a sub-screen (useFitTab 'settings').
// Below 900 px the group opens over the list with Back. Search sits in the toolbar. Master lists keep their own list/form (MasterList).
export function HotelSettings(){
  const {s,uid}=useHcc();
  const [q,setQ]=useState('');
  const [boot]=useState(()=>{const g=cfgNav.group;cfgNav.group=undefined;return g;}); // "Manage list…" / "also in Master configuration" links
  const [sel,setSel]=useFitTab<CfgGroupId>('settings',GROUP_IDS,'property');
  const narrow=!useWideSplit();
  const [picked,setPicked]=useState(!!boot);
  // FIT-2 integrator: on a wide screen the chosen group opens beside the list at once; "Back to list" (SplitView shows it whenever a record is
  // open) now really closes it there too, giving the list the whole width, until a group is picked again.
  const [closedWide,setClosedWide]=useState(false);
  useEffect(()=>{if(boot)setSel(boot);},[boot]); // eslint-disable-line react-hooks/exhaustive-deps
  const rows=cfgListRows(s,uid,q);
  const heads=Object.fromEntries(rows.map(r=>[r.id,cfgHeads(s,uid,r.id)])) as Record<string,string>;
  const current=rows.some(r=>r.id===sel)?sel:rows[0]?.id;
  const list=<ScrollPanel label="Configuration groups">
    {!rows.length&&<p className="muted padded">No setting matches “{q}”.</p>}
    {CFG_SECTIONS.map(sec=>{const l=rows.filter(r=>r.section===sec);if(!l.length)return null;
      return <div key={sec} className="cfg-sec"><h2>{sec}</h2><ul>{l.map(r=><li key={r.id}>
        <button type="button" className={'cfg-item'+(r.id===current?' on':'')} aria-current={r.id===current?'true':undefined} onClick={()=>{setSel(r.id);setPicked(true);setClosedWide(false);}}>
          <span><strong>{r.label}</strong><small>{r.summary}{r.hits?` · ${r.hits} match${r.hits>1?'es':''}`:''}</small>{heads[r.id]&&<small className="cfg-heads" title={heads[r.id]}>{heads[r.id]}</small>}</span>
          {r.readOnly&&<span className="cfg-ro"><Lock size={12}/> Read only</span>}<ChevronRight size={16} aria-hidden="true"/></button></li>)}</ul></div>;})}
  </ScrollPanel>;
  const detail=current&&(narrow?picked:!closedWide)?<div className="cfg-detail"><CfgGroupDetail g={current} q={q}/></div>:null;
  return <FitPage title="Master configuration" sub="Every setting in one place — 12 groups, dated values, approvals, history and modules on/off."
      actions={<Pill tone="orange">Fictional property · GST illustrative</Pill>}
      toolbar={<label className="cfg-search"><Search size={17}/><input type="search" aria-label="Search settings" placeholder="Search settings (e.g. GSTIN, deposit, PF)" value={q} onChange={e=>setQ(e.target.value)}/></label>}>
    <SplitView label="Master configuration" ratio="1fr 3fr" list={list} detail={detail} onCloseDetail={()=>{setPicked(false);setClosedWide(true);}}
      empty={<Empty title={rows.length?'Pick a group':'Nothing found'} body={rows.length?'Its settings and lists open here.':'Try another word.'}/>}/>
  </FitPage>;
}

export function CfgGroupDetail({g,q}:{g:CfgGroupId;q:string}){
  const {s,uid}=useHcc();
  const grp=cfgGroup(g)!;const row=cfgListRows(s,uid,'').find(r=>r.id===g)!;
  const defs=cfgDetailDefs(s,g,q);const block=scheduleBlock(s,uid);
  return <>
    <div className="cfg-head"><div><h2>{grp.label}</h2><p>{grp.blurb}</p></div><CfgGroupLink g={g}/></div>
    {row.readOnly&&<div className="info-note adm-banner cfg-readonly"><Lock size={16}/><span>Read only — {row.reason}</span></div>}
    {process.env.HCC_BUILD==='production'&&g==='data'&&<div className="padded"><ServerDevicePanel/></div>}
    <MstListsBlock key={g} g={g} q={q}/>
    {(g==='rooms'||g==='money')&&!q&&!mstSeesMoney(s,uid)&&<p className="muted padded"><Lock size={13}/> Tariffs and the GST master are hidden — this role does not see hotel money (DEC-061).</p>}
    {g==='rooms'&&!q&&mstSeesMoney(s,uid)&&<Tariffs block={block}/>}
    {g==='money'&&!q&&mstSeesMoney(s,uid)&&<GstPanel/>}
    {g==='modules'&&<CfgModulesPanel/>}
    {g!=='modules'&&(defs.length?<ul className="cfg-rows">{defs.map(d=><CfgRow key={d.key} def={d}/>)}</ul>:(!['rooms','money','data'].includes(g)||q)&&!(q&&row.listHits)?<Empty title="No matching settings in this group"/>:null)}
    {g==='people'&&<p className="muted padded">Dated statutory rates, leave policy and weekly offs from the payroll module join this group through the settings provider API.</p>}
    {g==='data'&&!q&&<CfgDataPanel/>}
  </>;
}
function CfgRow({def}:{def:CfgSettingDef}){
  const {s,uid,open,busy}=useHcc();
  const d=cfgCanChange(s,uid,def);const sched=cfgScheduled(s,def.key);const pend=cfgPendingRequests(s,def.key);const h=cfgHistory(s,def.key);
  return <li className="cfg-row">
    <div className="cfg-row-main"><strong>{def.label}</strong><small>{cfgMoneyMask(s,uid,def.help)}{def.illustrative?' ':''}</small>{def.illustrative&&<Pill tone="orange">Illustrative — CA to confirm</Pill>}</div>
    <div className="cfg-row-val"><strong>{cfgMoneyMask(s,uid,cfgDisplay(s,uid,def))}</strong>{sched.map(x=><small key={x.id}><CalendarClock size={12}/> {cfgMoneyMask(s,uid,cfgShow(def,x.value))} from {shortDate(x.effectiveFrom)}</small>)}
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
        return <tr key={t}><td><strong>{rmsTypeName(s,t)}</strong></td><td>{s.rooms.filter(r=>r.type===t).length}</td><td>{money(adminBaseRate(s,t))}</td><td><strong>{money(rateFor(s,room,s.date))}</strong></td>
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
