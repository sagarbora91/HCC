// Module 14 · phone page (M-groups): chips Blocks / Companies / Pickup / Requests, cards instead of tables, one primary action (Fab).
import React,{useState} from 'react';
import {Plus,ListPlus,SlidersHorizontal,Undo2,Printer,CreditCard,Pencil,Ban,AlertTriangle,Check,X} from 'lucide-react';
import {money,grpSlice,grpKpis,grpBlockTotals,grpLines,grpStatus,grpCompany,grpCredit,grpMasterFolio,grpPickupReport,GRP_CREDIT_LABEL} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,shortDate} from '../../components/ui';
import {ListCard,KpiCard,ChipFilter,Fab} from '../../app/mobile/components';
import {GrpStatusPill,GrpCreditPill,GrpCreditBar,grpCutoffText,grpUserName} from './parts';

type Chip='blocks'|'companies'|'pickup'|'requests';
export function GroupsMobile(){
  const {s,open,permission}=useHcc();const [chip,setChip]=useState<Chip>('blocks');
  const x=grpSlice(s),k=grpKpis(s);
  return <div className="grp-m">
    <div className="grp-m-kpis"><KpiCard label="Open blocks" value={k.open} sub={`${k.dueSoon} cut-off ≤ 2 days`}/><KpiCard label="Picked / held" value={`${k.picked}/${k.allot}`} sub={`${k.remaining} unpicked`}/>
      <KpiCard label="Credit alerts" value={k.creditAlerts} sub={`${k.pendingLimits} limit request${k.pendingLimits===1?'':'s'}`} onClick={()=>setChip('companies')}/></div>
    <ChipFilter label="Groups sections" value={chip} onChange={setChip} options={[{id:'blocks',label:'Blocks',count:x.blocks.length},{id:'companies',label:'Companies',count:x.companies.length},{id:'pickup',label:'Pickup'},{id:'requests',label:'Requests',count:k.pendingLimits}]}/>
    <p className="grp-m-note">Unpicked block rooms are held and shown as Blocked (Group hold) until picked up or released. Simulated data; fictional GSTINs.</p>
    {chip==='blocks'&&(x.blocks.length?x.blocks.map(b=>{const t=grpBlockTotals(s,b),c=grpCompany(s,b.company);
      return <ListCard key={b.id} title={b.name} sub={`${c?c.name:'Private group'} · ${shortDate(b.arrival)}→${shortDate(b.departure)}`} meta={`${t.picked}/${t.allot} picked · ${grpCutoffText(s,b)}`}
        right={<span className="grp-m-right"><GrpStatusPill s={s} blk={b}/>{t.atRisk&&<span className="pill grp-red">At risk</span>}</span>} onClick={()=>open({type:'grp-detail',id:b.id})}/>;}):<Empty title="No group blocks"/>)}
    {chip==='companies'&&x.companies.map(c=>{const cr=grpCredit(s,c);
      return <ListCard key={c.id} title={c.name} sub={`Exposure ${money(cr.exposure)} of ${money(cr.limit)}`} meta={<><GrpCreditBar s={s} c={c}/>{c.gstin} · fictional</>} right={<GrpCreditPill level={cr.level}/>} onClick={()=>open({type:'grp-limit',id:c.id})}/>;})}
    {chip==='pickup'&&<>{grpPickupReport(s).map(r=><ListCard key={r.block.id+r.type} title={`${r.block.name} · ${r.type}`} sub={`${r.picked} of ${r.allot} picked (${r.pct}%) · ${r.released} released · ${r.remaining} left`} meta={`${shortDate(r.block.arrival)}→${shortDate(r.block.departure)} · cut-off ${shortDate(r.block.cutoff)}`} right={<span className="pill neutral">{r.status}</span>}/>)}
      <button className="btn full grp-m-btn" onClick={()=>open({type:'grp-print',id:'pickup'})}><Printer size={18}/> Print pickup report</button></>}
    {chip==='requests'&&<Requests/>}
    {permission('reservations')&&(chip==='companies'?<Fab label="New company" icon={Plus} onClick={()=>open({type:'grp-company'})}/>:<Fab label="New block" icon={Plus} onClick={()=>open({type:'grp-block'})}/>)}
  </div>;
}
function Requests(){
  const {s,uid,run,busy,permission}=useHcc();const list=grpSlice(s).requests.slice().reverse();
  if(!list.length)return <Empty title="No credit-limit requests"/>;
  return <>{list.map(r=>{const own=r.actor===uid;return <div key={r.id} className="m-list-card grp-m-req"><div className="m-lc-main"><strong>{grpCompany(s,r.company)?.name}</strong><span>{money(r.from)} → {money(r.to)} · {r.status}</span><small>{r.reason} · by {grpUserName(s,r.actor)}</small>
    {r.status==='Pending'&&(permission('approve')&&!own?<div className="grp-m-actions"><button className="btn" disabled={busy} onClick={()=>run({type:'GRP_LIMIT_DECIDE',id:r.id,approve:false,key:'grplr:'+r.id},'Request rejected')}><X size={16}/> Reject</button><button className="btn primary" disabled={busy} onClick={()=>run({type:'GRP_LIMIT_DECIDE',id:r.id,approve:true,key:'grplr:'+r.id},'Credit limit approved')}><Check size={16}/> Approve</button></div>
      :<small>{own?'Your request — another approver decides.':'An Owner or Manager decides.'}</small>)}</div></div>;})}</>;
}
/** Block detail sheet (phone; also usable on desktop). */
export function BlockDetail({id}:{id?:string}){
  const {s,open,permission,booking,closeModal}=useHcc();const blk=grpSlice(s).blocks.find(b=>b.id===id);
  if(!blk)return <Empty title="This block is unavailable"/>;
  const st=grpStatus(s,blk),c=grpCompany(s,blk.company),t=grpBlockTotals(s,blk),f=grpMasterFolio(s,blk),canEdit=permission('reservations')&&st==='Open'&&blk.status==='Open';
  const cr=c&&blk.billTo==='company'?grpCredit(s,c):undefined;
  return <><div className="modal-heading"><div className="eyebrow">{blk.id} · {blk.billTo==='company'?'BILL TO COMPANY':'GUESTS PAY OWN FOLIOS'}</div><h2>{blk.name}</h2><p>{c?c.name:'Private group'} · {shortDate(blk.arrival)} → {shortDate(blk.departure)} · {grpCutoffText(s,blk)}</p></div>
    <div className="grp-m-status"><GrpStatusPill s={s} blk={blk}/>{t.atRisk&&<span className="pill grp-red"><AlertTriangle size={12}/> At risk</span>}<span>{t.picked}/{t.allot} picked · {t.remaining} left</span></div>
    <h3 className="grp-m-h">Allotment</h3>
    {grpLines(s,blk).map(l=><ListCard key={l.type} title={`${l.type} · ${money(l.rate)}`} sub={`${l.picked} picked of ${l.allot} · ${l.released} released · ${l.remaining} left`} meta={st==='Open'?`${l.free} free now${l.atRisk?` — short ${l.remaining-l.free}`:''}`:undefined}/>)}
    {cr&&<><h3 className="grp-m-h">Credit</h3><ListCard title={c!.name} sub={`${money(cr.exposure)} of ${money(cr.limit)} · ${GRP_CREDIT_LABEL[cr.level]}`} meta={<GrpCreditBar s={s} c={c!}/>}/></>}
    <h3 className="grp-m-h">Master folio ({f.rows.length})</h3>
    {f.rows.length?<>{f.rows.map(r=><ListCard key={r.id} title={`${r.guest} · ${r.room}`} sub={`${r.id} · ${r.status}`} right={<span className="number">{money(r.balance)}</span>} meta={`Paid ${money(r.paid)} · est. unpaid ${money(r.estimate)}`} onClick={()=>booking(r.id)}/>)}
      <div className="summary-list grp-summary"><div><span>Group posted total</span><strong>{money(f.totals.total)}</strong></div><div><span>Paid</span><strong>{money(f.totals.paid)}</strong></div><div><span>Balance</span><strong>{money(f.totals.balance)}</strong></div><div><span>Est. unpaid stays</span><strong>{money(f.totals.estimate)}</strong></div></div></>
      :<p className="grp-m-empty">No member bookings yet.</p>}
    <div className="grp-m-tools">
      {canEdit&&<button className="btn" onClick={()=>open({type:'grp-allot',id:blk.id})}><SlidersHorizontal size={18}/> Allotment</button>}
      {canEdit&&<button className="btn" onClick={()=>open({type:'grp-release',id:blk.id})}><Undo2 size={18}/> Release</button>}
      {f.rows.length>0&&<button className="btn" onClick={()=>open({type:'grp-print',id:'folio:'+blk.id})}><Printer size={18}/> Print folio</button>}
      {c&&permission('reservations')&&<button className="btn" onClick={()=>open({type:'grp-limit',id:c.id})}><CreditCard size={18}/> Credit</button>}
      {c&&permission('reservations')&&<button className="btn" onClick={()=>open({type:'grp-company',id:c.id})}><Pencil size={18}/> Company</button>}
      {permission('reservations')&&blk.status!=='Cancelled'&&<button className="btn" onClick={()=>open({type:'grp-cancel',id:blk.id})}><Ban size={18}/> Cancel block</button>}
    </div>
    <div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button>{canEdit&&<button className="btn primary" onClick={()=>open({type:'grp-rooming',id:blk.id})}><ListPlus size={18}/> Rooming list</button>}</div></>;
}
