// Module 14 · Group & corporate — desktop page: Blocks, Companies, Pickup report, Master folio, Limit requests.
import React,{useState} from 'react';
import {Plus,Building,Printer,ListPlus,Info,AlertTriangle,Check,X,Undo2,SlidersHorizontal,CreditCard,Pencil,Ban,Banknote} from 'lucide-react';
import {money,grpSlice,grpKpis,grpLines,grpBlockTotals,grpStatus,grpCompany,grpCredit,grpMasterFolio,grpPickupReport,grpMembers,rbacMoneyAdmin} from '../../services/mock';
import type {GRPBlock} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,shortDate,dateLabel} from '../../components/ui';
import {FitPage,FitTabs,FitNote,ScrollPanel,SplitView,StatStrip,useFitTab} from '../../components/fit';
import {usePaged,ShowMore,DESKTOP_PAGE} from '../../components/ShowMore';
import {GrpStatusPill,GrpCreditPill,GrpCreditBar,grpCutoffText,grpUserName,grpSimNote,GrpRetiredPill} from './parts';

const TAB_DEFS=[{id:'blocks',label:'Blocks'},{id:'companies',label:'Companies'},{id:'pickup-report',label:'Pickup report'},{id:'master-folio',label:'Master folio'},{id:'limit-requests',label:'Limit requests'}] as const;
const TAB_IDS=['blocks','companies','pickup-report','master-folio','limit-requests'] as const;
type Tab=typeof TAB_IDS[number];

export function GroupsPage(){
  const {s,open,permission}=useHcc();
  const [tab,setTab]=useFitTab<Tab>('groups',TAB_IDS,'blocks');
  const [folio,setFolio]=useState('');
  const k=grpKpis(s),x=grpSlice(s);
  const counts:Record<Tab,number>={blocks:x.blocks.length,companies:x.companies.length,'pickup-report':grpPickupReport(s).length,'master-folio':x.blocks.filter(b=>b.members.length).length,'limit-requests':k.pendingLimits};
  const showFolio=(id:string)=>{setFolio(id);setTab('master-folio');};
  const creditNote=<FitNote lines={3} text={<><b>Credit limits are enforced (DEC-076).</b> Used = what the company owes on company credit + the stays billed to it (estimated: agreed nights + GST − what the guest paid).
      A booking, company credit at checkout or a rooming list beyond the remaining credit is refused; only the Owner sets a limit (others send a request). A payment from the company frees credit.</>}/>;
  return <FitPage title="Groups & corporate" sub={`Company accounts, group blocks and pickup · business date ${dateLabel(s.date)}`}
      actions={permission('reservations')&&<><button className="btn primary" onClick={()=>open({type:'grp-block'})}><Plus size={16}/> New group block</button><button className="btn" onClick={()=>open({type:'grp-company'})}><Building size={16}/> New company</button></>}
      toolbar={<StatStrip money items={[
        {title:'Open blocks',value:String(k.open),sub:`${k.dueSoon} with cut-off within 2 days`},
        {title:'Rooms picked up',value:`${k.picked} / ${k.allot}`,sub:'Open blocks · picked / held'},
        {title:'Unpicked rooms',value:String(k.remaining),sub:k.atRisk?`${k.atRisk} block${k.atRisk>1?'s':''} at risk (not enough free rooms)`:'All covered by free inventory'},
        {title:'Credit alerts',value:String(k.creditAlerts),sub:`${k.pendingLimits} limit request${k.pendingLimits===1?'':'s'} pending`}]} compact collapseOnTablet/>}
      tabs={<FitTabs page="groups" value={tab} onChange={setTab} tabs={TAB_DEFS.map(d=>({id:d.id as Tab,label:d.label,count:counts[d.id]}))}/>}>
    {tab==='blocks'&&<BlocksTab showFolio={showFolio}/>}
    {tab==='companies'&&<ScrollPanel label="Companies" footer={creditNote}><CompaniesTab bare/></ScrollPanel>}
    {tab==='pickup-report'&&<PickupTab/>}
    {tab==='master-folio'&&<FolioTab id={folio||x.blocks.find(b=>b.members.length)?.id||''} onPick={setFolio}/>}
    {tab==='limit-requests'&&<ScrollPanel label="Limit requests"><RequestsTab/></ScrollPanel>}
  </FitPage>;
}

/** Blocks sub-screen (FIT split): block list on the left, the selected block's record (card, members, rooming list / allotment actions, billing) on the right. */
function BlocksTab({showFolio}:{showFolio:(id:string)=>void}){
  const {s}=useHcc();const blocks=grpSlice(s).blocks.slice().sort((a,b)=>(grpStatus(s,a)==='Open'?0:1)-(grpStatus(s,b)==='Open'?0:1)||a.arrival.localeCompare(b.arrival));
  const pg=usePaged(blocks,DESKTOP_PAGE);
  // FIT-2: no block open = the block list takes the whole width (every block visible, scrolling in its own panel); a click opens the record beside it.
  const [sel,setSel]=useState<string|null>(null);
  const id=sel;const blk=blocks.find(b=>b.id===id);
  const note=<FitNote lines={2} text={<><b>Rooms held for groups are blocked.</b> Unpicked block rooms are assigned to specific rooms, shown as “Group hold” (Blocked) on the room board and calendar, and can only be booked through the block’s rooming list. Releasing or cancelling a block frees them.</>}/>;
  if(!blocks.length)return <ScrollPanel label="Group blocks" footer={note}><Empty title="No group blocks" body="Create a block to hold rooms for a group."/></ScrollPanel>;
  // FIT-2 integrator: no panel head — the "Blocks" tab right above already shows the title and the count; the rows get that line.
  const list=<ScrollPanel label="Group blocks" footer={<>{note}<ShowMore p={pg}/></>}>
    <ul className="grp-list">{pg.shown.map(b=>{const t=grpBlockTotals(s,b),c=grpCompany(s,b.company);
      return <li key={b.id}><button type="button" className={'grp-list-row'+(b.id===id?' active':'')} aria-current={b.id===id?'true':undefined} onClick={()=>setSel(b.id)}>
        <span className="grp-list-row-main"><strong>{b.name}</strong><small>{b.id}{c?' · '+c.name:''}</small><small>{shortDate(b.arrival)} → {shortDate(b.departure)} · {t.picked} of {t.allot} picked up ({t.pct}%)</small></span>
        <GrpStatusPill s={s} blk={b}/></button></li>;})}</ul></ScrollPanel>;
  return <SplitView label="Group blocks" ratio="2fr 3fr" list={list} onCloseDetail={()=>setSel(null)}
    detail={blk?<BlockRecord key={blk.id} blk={blk} showFolio={showFolio}/>:null}
    empty={<Empty title="Pick a group block" body="Its rooms, members, rooming list and billing open here."/>}/>;
}
function BlockRecord({blk,showFolio}:{blk:GRPBlock;showFolio:(id:string)=>void}){
  const {s}=useHcc();const members=grpMembers(s,blk);
  return <div className="grp-record"><BlockCard blk={blk} showFolio={showFolio}/>
    {members.length>0&&<section className="grp-record-billing" aria-label="Billing"><div className="section-title compact"><h2>Billing · master folio</h2></div><MasterFolio blk={blk}/></section>}</div>;
}
export function BlockCard({blk,showFolio}:{blk:GRPBlock;showFolio?:(id:string)=>void}){
  const {s,open,permission,booking}=useHcc();
  const lines=grpLines(s,blk),t=grpBlockTotals(s,blk),st=grpStatus(s,blk),c=grpCompany(s,blk.company),members=grpMembers(s,blk);
  const canEdit=permission('reservations')&&st==='Open'&&blk.status==='Open';
  return <article className="grp-block">
    <header><div><div className="eyebrow">{blk.id} · {blk.billTo==='company'?'BILL TO COMPANY':'GUESTS PAY OWN FOLIOS'}</div><h3>{blk.name}</h3>
      <p>{c?c.name:'No company account'} · {shortDate(blk.arrival)} → {shortDate(blk.departure)} · {t.nights} night{t.nights>1?'s':''}{blk.contact?' · contact '+blk.contact:''}</p></div>
      <div className="grp-block-status"><GrpStatusPill s={s} blk={blk}/>{t.atRisk&&<span className="pill grp-red"><AlertTriangle size={12}/> At risk</span>}<small>{grpCutoffText(s,blk)}</small></div></header>
    <div className="grp-pickup"><div className="grp-bar" role="img" aria-label={`Pickup ${t.pct}%`}><span className="grp-bar-fill ok" style={{width:t.pct+'%'}}/></div><span>{t.picked} of {t.allot} picked up ({t.pct}%) · {t.remaining} unpicked · {t.released} released</span></div>
    <div className="table-wrap"><table className="grp-table"><thead><tr><th>Room type</th><th className="number">Block rate</th><th className="number">Held</th><th className="number">Picked up</th><th className="number">Released</th><th className="number">Remaining</th><th className="number">Free now</th></tr></thead>
      <tbody>{lines.map(l=><tr key={l.type}><td><strong>{l.type}</strong></td><td className="number">{money(l.rate)}</td><td className="number">{l.allot}</td><td className="number">{l.picked}</td><td className="number">{l.released}</td><td className="number">{l.remaining}</td>
        <td className="number">{st==='Open'?<>{l.free}{l.atRisk&&<span className="grp-risk"> · short {l.remaining-l.free}</span>}</>:'—'}</td></tr>)}</tbody></table></div>
    {members.length>0&&<div className="grp-members"><span>Members:</span>{members.map(m=><button key={m.booking} className="text-btn" onClick={()=>booking(m.booking)}>{m.b.guest} · {m.b.room} · {m.b.status}</button>)}</div>}
    {blk.notes&&<p className="footnote">{blk.notes}</p>}
    <footer className="grp-block-actions">
      {canEdit&&<button className="btn primary" onClick={()=>open({type:'grp-rooming',id:blk.id})}><ListPlus size={15}/> Rooming list</button>}
      {canEdit&&<button className="btn" onClick={()=>open({type:'grp-allot',id:blk.id})}><SlidersHorizontal size={15}/> Allotment</button>}
      {canEdit&&<button className="btn" onClick={()=>open({type:'grp-release',id:blk.id})}><Undo2 size={15}/> Release</button>}
      {showFolio&&members.length>0&&<button className="btn" onClick={()=>showFolio(blk.id)}><CreditCard size={15}/> Master folio</button>}
      {permission('reservations')&&blk.status!=='Cancelled'&&<button className="btn subtle" onClick={()=>open({type:'grp-cancel',id:blk.id})}><Ban size={15}/> Cancel block</button>}
    </footer>
  </article>;
}

export function CompaniesTab({bare}:{bare?:boolean}={}){
  const {s,open,permission}=useHcc();const x=grpSlice(s);
  if(!x.companies.length)return <Empty title="No company accounts"/>;
  return <>{!bare&&<div className="info-note grp-gap"><Info size={16}/><span><b>Credit limits are enforced (DEC-076).</b> Used = what the company owes on company credit + the stays billed to it (estimated: agreed nights + GST − what the guest paid).
      A booking, company credit at checkout or a rooming list beyond the remaining credit is refused; only the Owner sets a limit (others send a request). A payment from the company frees credit.</span></div>}
    <div className="table-wrap"><table className="grp-table"><thead><tr><th>Company</th><th>Contact</th><th>Negotiated rates</th><th className="number">Credit limit</th><th className="number">Used</th><th className="number">Remaining</th><th>Credit</th><th/></tr></thead>
    <tbody>{x.companies.map(c=>{const k=grpCredit(s,c),req=x.requests.find(r=>r.company===c.id&&r.status==='Pending');
      return <tr key={c.id}><td><strong>{c.name}</strong> <GrpRetiredPill c={c}/><small>{c.gstin?`GSTIN ${c.gstin} · fictional`:'No GSTIN'}{c.city?' · '+c.city:''}</small></td>
        <td>{c.contact||'—'}<small>{c.phone}</small></td>
        <td>{Object.entries(c.rates).map(([t,r])=><div key={t} className="grp-rate"><span>{t}</span> {money(r)}</div>)}{!Object.keys(c.rates).length&&<span className="muted">Rack rates</span>}</td>
        <td className="number">{money(c.creditLimit)}{c.creditLimit===0&&<small>No credit</small>}{req&&<small>Request → {money(req.to)}</small>}</td>
        <td className="number">{money(k.exposure)}<small>owed {money(k.receivable)} · stays {money(k.stays)}</small></td>
        <td className="number">{money(k.available)}</td>
        <td><GrpCreditPill level={k.level}/><GrpCreditBar s={s} c={c}/><small>{k.limit?k.pct+'% used':''}</small></td>
        <td className="grp-row-actions">{permission('reservations')&&<><button className="btn small-btn" onClick={()=>open({type:'grp-company',id:c.id})}><Pencil size={14}/> Edit</button>
          <button className="btn small-btn" onClick={()=>open({type:'grp-limit',id:c.id})}><CreditCard size={14}/> Credit limit</button></>}
          {permission('payments')&&k.receivable>0&&<button className="btn small-btn" onClick={()=>open({type:'grp-payment',id:c.id})}><Banknote size={14}/> Record payment</button>}</td></tr>;})}</tbody></table></div></>;
}

function PickupTab(){
  const {s,open}=useHcc();const rows=grpPickupReport(s);
  return <ScrollPanel label="Pickup report" title="Pickup report" actions={<button className="btn" onClick={()=>open({type:'grp-print',id:'pickup'})}><Printer size={15}/> Print</button>}><PickupTable rows={rows}/></ScrollPanel>;
}
export function PickupTable({rows}:{rows:ReturnType<typeof grpPickupReport>}){
  const {s}=useHcc();
  if(!rows.length)return <Empty title="No blocks to report"/>;
  return <div className="table-wrap"><table className="grp-table"><thead><tr><th>Block</th><th>Dates · cut-off</th><th>Type</th><th className="number">Held</th><th className="number">Picked</th><th className="number">Pickup %</th><th className="number">Released</th><th className="number">Remaining</th><th>Status</th></tr></thead>
    <tbody>{rows.map(r=><tr key={r.block.id+r.type}><td><strong>{r.block.name}</strong><small>{r.block.id}{grpCompany(s,r.block.company)?' · '+grpCompany(s,r.block.company)!.name:''}</small></td>
      <td>{shortDate(r.block.arrival)}–{shortDate(r.block.departure)}<small>cut-off {shortDate(r.block.cutoff)}</small></td><td>{r.type}</td>
      <td className="number">{r.allot}</td><td className="number">{r.picked}</td><td className="number">{r.pct}%</td><td className="number">{r.released}</td><td className="number">{r.remaining}{r.atRisk&&<small className="grp-risk">at risk</small>}</td><td>{r.status}</td></tr>)}</tbody></table></div>;
}

function FolioTab({id,onPick}:{id:string;onPick:(id:string)=>void}){
  const {s,open}=useHcc();const blocks=grpSlice(s).blocks;const blk=blocks.find(b=>b.id===id);
  return <ScrollPanel label="Master folio" title="Master folio" actions={<>
      <label className="grp-inline">Block <select value={id} onChange={e=>onPick(e.target.value)}>{blocks.map(b=><option key={b.id} value={b.id}>{b.name} ({b.members.length})</option>)}</select></label>
      {blk&&<button className="btn" onClick={()=>open({type:'grp-print',id:'folio:'+blk.id})}><Printer size={15}/> Print</button>}</>}>
    {blk?<MasterFolio blk={blk}/>:<Empty title="Choose a block"/>}</ScrollPanel>;
}
export function MasterFolio({blk,print}:{blk:GRPBlock;print?:boolean}){
  const {s,booking}=useHcc();const f=grpMasterFolio(s,blk),c=grpCompany(s,blk.company);
  return <div className="grp-folio">
    <p className="grp-folio-head">{blk.billTo==='company'&&c?<>Bill to <b>{c.name}</b> · GSTIN {c.gstin||'—'} (fictional) · {c.city}</>:<>Each guest settles their own folio (no bill-to-company).</>} Read-only aggregation of member folios; payments and charges are posted on each member folio.</p>
    {!f.rows.length?<Empty title="No member bookings yet" body="Use the rooming list to pick up block rooms."/>:
    <div className="table-wrap"><table className="grp-table"><thead><tr><th>Booking</th><th>Guest</th><th>Room</th><th>Status</th><th className="number">Rate</th><th className="number">Posted net</th><th className="number">Tax</th><th className="number">Paid</th><th className="number">Balance</th><th className="number">Est. unpaid stay</th></tr></thead>
      <tbody>{f.rows.map(r=><tr key={r.id}><td>{print?r.id:<button className="text-btn" onClick={()=>booking(r.id)}>{r.id}</button>}</td><td>{r.guest}</td><td>{r.room} · {r.type}</td><td>{r.status}</td>
        <td className="number">{money(r.rate)}</td><td className="number">{money(r.net)}</td><td className="number">{money(r.tax)}</td><td className="number">{money(r.paid)}</td><td className="number">{money(r.balance)}</td><td className="number">{money(r.estimate)}</td></tr>)}</tbody>
      <tfoot><tr><td colSpan={5}><strong>Group total (active members)</strong></td><td className="number"><strong>{money(f.totals.net)}</strong></td><td className="number"><strong>{money(f.totals.tax)}</strong></td><td className="number"><strong>{money(f.totals.paid)}</strong></td><td className="number"><strong>{money(f.totals.balance)}</strong></td><td className="number"><strong>{money(f.totals.estimate)}</strong></td></tr></tfoot></table></div>}
    <p className="footnote">Est. unpaid stay = posted charges + agreed nights not yet posted + GST per night − payments. GST illustrative — CA to confirm.</p>
  </div>;
}

function RequestsTab(){
  const {s,uid,run,busy}=useHcc();const x=grpSlice(s);
  const list=x.requests.slice().reverse();
  if(!list.length)return <Empty title="No credit-limit requests"/>;
  return <div className="table-wrap"><table className="grp-table"><thead><tr><th>Request</th><th>Company</th><th className="number">Limit change</th><th>Reason</th><th>Requested by</th><th>Status</th><th/></tr></thead>
    <tbody>{list.map(r=>{const c=grpCompany(s,r.company),own=r.actor===uid;
      return <tr key={r.id}><td><strong>{r.id}</strong><small>{shortDate(r.at.slice(0,10))} {r.at.slice(11,16)}</small></td><td>{c?.name}</td><td className="number">{money(r.from)} → {money(r.to)}</td><td>{r.reason}</td><td>{grpUserName(s,r.actor)}</td>
        <td>{r.status}{r.approver&&<small>by {grpUserName(s,r.approver)}{r.note?' · '+r.note:''}</small>}</td>
        <td className="grp-row-actions">{r.status==='Pending'&&(rbacMoneyAdmin(s,uid)&&!own?<>
          <button className="btn small-btn" disabled={busy} onClick={()=>run({type:'GRP_LIMIT_DECIDE',id:r.id,approve:false,key:'grplr:'+r.id},'Request rejected')}><X size={14}/> Reject</button>
          <button className="btn primary small-btn" disabled={busy} onClick={()=>run({type:'GRP_LIMIT_DECIDE',id:r.id,approve:true,key:'grplr:'+r.id},'Credit limit approved')}><Check size={14}/> Approve</button></>
          :<small className="muted">{own?'Your request — the Owner decides':'The Owner decides (DEC-076)'}</small>)}</td></tr>;})}</tbody></table></div>;
}
