// Module 14 dialogs. Types: 'grp-company' (id = company, omitted = new), 'grp-limit' (id = company), 'grp-block' (new block),
// 'grp-allot' / 'grp-release' / 'grp-cancel' / 'grp-rooming' (id = block), 'grp-print' (id = 'pickup' | 'folio:<block>').
import {printDocument} from '../../app/native';
import React,{useState} from 'react';
import {Check,Plus,Trash2,AlertTriangle,Info,Printer} from 'lucide-react';
import {money,nights,grpSlice,grpCompany,grpBlock,grpRoomTypes,grpLines,grpRemaining,grpFreeRooms,grpCredit,grpStayValue,grpHeldElsewhere,grpPickupReport,grpCreditLevel,GRP_CREDIT_LABEL} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,field,submit,shortDate,dateLabel} from '../../components/ui';
import {NumberStepper} from '../../app/mobile/components';
import {GrpCreditPill,grpRupees,grpPaise,grpSimNote,grpUserName} from './parts';
import {MasterFolio,PickupTable} from './Groups';
import {RestrictionNotice} from '../reservations/RestrictionNotice';
import {rstState} from '../reservations/restriction';

const Head=({eyebrow,title,sub}:{eyebrow:string;title:string;sub:string})=><div className="modal-heading"><div className="eyebrow">{eyebrow}</div><h2>{title}</h2><p>{sub}</p></div>;

export function CompanyModal({id}:{id?:string}){
  const {s,run,modal,closeModal,busy,permission}=useHcc();const c=grpCompany(s,id);
  if(id&&!c)return <Empty title="This company is unavailable"/>;
  const types=grpRoomTypes(s);
  return <><Head eyebrow={c?c.id:'NEW COMPANY ACCOUNT'} title={c?'Edit '+c.name:'New company account'} sub="Fictional companies only. The negotiated rate becomes the default block rate for this company."/>
    <form className="form" onSubmit={e=>submit(e,f=>{
      const rates:Record<string,number>={};types.forEach(t=>{const v=grpPaise(f.get('rate-'+t));if(v!==undefined)rates[t]=v;});
      const cmd:Record<string,unknown>={type:'GRP_COMPANY_SAVE',id:c?.id,name:field(f,'name'),gstin:field(f,'gstin'),contact:field(f,'contact'),phone:field(f,'phone'),email:field(f,'email'),city:field(f,'city'),rates,key:modal!.key};
      if(!c)cmd.creditLimit=grpPaise(f.get('limit'));
      run(cmd as {type:string},c?'Company updated':'Company created',{close:true});})}>
      <label>Company name<input name="name" required minLength={2} maxLength={80} defaultValue={c?.name} placeholder="e.g. Manjra Textiles (fictional)" enterKeyHint="next"/></label>
      <div className="form-row"><label>GSTIN placeholder (fictional)<input name="gstin" maxLength={15} defaultValue={c?.gstin} placeholder="27ZZFIC0009F1Z5" autoCapitalize="characters"/></label>
        <label>City<input name="city" maxLength={40} defaultValue={c?.city} placeholder="Latur"/></label></div>
      <div className="form-row"><label>Contact person<input name="contact" maxLength={60} defaultValue={c?.contact}/></label>
        <label>Phone<input name="phone" type="tel" inputMode="tel" maxLength={20} defaultValue={c?.phone} placeholder="+91 90000 00000"/></label></div>
      <label>E-mail<input name="email" type="email" inputMode="email" maxLength={80} defaultValue={c?.email} placeholder="travel@company.example.test"/></label>
      <fieldset className="grp-fieldset"><legend>Negotiated nightly rate (₹, blank = rack rate)</legend><div className="form-row">{types.map(t=><label key={t}>{t}<input name={'rate-'+t} inputMode="decimal" defaultValue={grpRupees(c?.rates[t]||0)} placeholder={String(s.rooms.find(r=>r.type===t)!.rate/100)}/></label>)}</div></fieldset>
      {!c&&<label>Opening credit limit (₹, optional)<input name="limit" inputMode="decimal" placeholder="0"/><small>{permission('admin')?'You can set this directly.':'Without administrator access this is sent as a request for approval.'}</small></label>}
      {c&&<div className="info-note"><Info size={16}/>Change the credit limit with “Credit limit” (administrator, or a request that an approver decides).</div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>{c?'Save changes':'Create account'} <Check size={16}/></button></div>
    </form></>;
}

export function LimitModal({id}:{id?:string}){
  const {s,uid,run,modal,closeModal,busy,permission}=useHcc();const c=grpCompany(s,id);
  if(!c)return <Empty title="This company is unavailable"/>;
  const k=grpCredit(s,c),admin=permission('admin'),pending=grpSlice(s).requests.find(r=>r.company===c.id&&r.status==='Pending');
  return <><Head eyebrow={'CREDIT · '+c.id} title={c.name} sub={admin?'As administrator you set the limit directly; the change is audited.':'Your change is sent as a request; an Owner or Manager (not you) decides it.'}/>
    <div className="summary-list grp-summary"><div><span>Current limit</span><strong>{money(c.creditLimit)}</strong></div><div><span>Exposure (estimate)</span><strong>{money(k.exposure)}</strong></div>
      <div><span>Opening outstanding (simulated AR)</span><strong>{money(c.openingOutstanding)}</strong></div><div><span>Status</span><strong><GrpCreditPill level={k.level}/> {k.limit?k.pct+'% used':''}</strong></div></div>
    {pending&&<div className="info-note"><Info size={16}/>{pending.id} pending: {money(pending.from)} → {money(pending.to)} by {grpUserName(s,pending.actor)}{pending.actor===uid?' (yours)':''}.</div>}
    {(c.limitHistory||[]).length>0&&<details className="grp-hist"><summary>Limit history ({c.limitHistory!.length})</summary><ul>{c.limitHistory!.map((h,i)=><li key={i}>{money(h.from)} → {money(h.to)} · {grpUserName(s,h.actor)} · {shortDate(h.at.slice(0,10))} · {h.reason}</li>)}</ul></details>}
    {(admin||(permission('reservations')&&!pending))?<form className="form" onSubmit={e=>submit(e,f=>run({type:'GRP_LIMIT_SET',company:c.id,limit:grpPaise(f.get('limit'))??-1,reason:field(f,'reason'),key:modal!.key},admin?'Credit limit updated':'Credit-limit request sent',{close:true}))}>
      <label>New credit limit (₹)<input name="limit" required inputMode="decimal" defaultValue={grpRupees(c.creditLimit)} enterKeyHint="next"/></label>
      <label>Reason{admin?' (optional)':''}<input name="reason" required={!admin} minLength={admin?0:3} maxLength={200} placeholder="e.g. Annual contract renewal" enterKeyHint="send"/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>{admin?'Set limit':'Send request'} <Check size={16}/></button></div>
    </form>:<div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div>}
  </>;
}

export function BlockModal(){
  const {s,run,modal,closeModal,busy}=useHcc();const x=grpSlice(s),types=grpRoomTypes(s);
  const [a,setA]=useState(s.date>'2026-10-30'?s.date:'2026-11-12'),[d,setD]=useState(''),[co,setCo]=useState(''),[bill,setBill]=useState<'company'|'guest'>('guest');
  const [allot,setAllot]=useState<Record<string,number>>({});
  const dep=d||(a?new Date(Date.parse(a+'T12:00:00Z')+86400000).toISOString().slice(0,10):'');
  const company=grpCompany(s,co);
  const valid=a>=s.date&&dep>a&&nights(a,dep)<=30;
  return <><Head eyebrow="GROUP BLOCK" title="New group block" sub="Hold rooms by type for a group. Unpicked rooms are released automatically when the business date passes the cut-off."/>
    <form className="form" onSubmit={e=>submit(e,f=>{
      const rates:Record<string,number>={};types.forEach(t=>{const v=grpPaise(f.get('rate-'+t));if(v!==undefined&&allot[t])rates[t]=v;});
      run({type:'GRP_BLOCK_CREATE',name:field(f,'name'),company:co||undefined,billTo:bill,arrival:a,departure:dep,cutoff:field(f,'cutoff'),allot,rates,contact:field(f,'contact'),notes:field(f,'notes'),key:modal!.key},'Group block created',{close:true});})}>
      <label>Group name<input name="name" required minLength={3} maxLength={80} placeholder="e.g. Rotary district meet" enterKeyHint="next"/></label>
      <div className="form-row"><label>Company account<select value={co} onChange={e=>{setCo(e.target.value);if(!e.target.value)setBill('guest');}}><option value="">None (private group)</option>{x.companies.map(c=><option key={c.id} value={c.id}>{c.name}</option>)}</select></label>
        <label>Billing<select value={bill} onChange={e=>setBill(e.target.value as 'company'|'guest')}><option value="guest">Guests pay own folios</option><option value="company" disabled={!co}>Bill to company</option></select></label></div>
      <div className="form-row"><label>Arrival<input type="date" required min={s.date} value={a} onChange={e=>setA(e.target.value)}/></label><label>Departure<input type="date" required min={a} value={dep} onChange={e=>setD(e.target.value)}/></label>
        <label>Cut-off (pickup until)<input name="cutoff" type="date" required min={s.date} max={a} defaultValue={a&&a>s.date?new Date(Date.parse(a+'T12:00:00Z')-3*86400000).toISOString().slice(0,10)<s.date?s.date:new Date(Date.parse(a+'T12:00:00Z')-3*86400000).toISOString().slice(0,10):s.date}/></label></div>
      <fieldset className="grp-fieldset"><legend>Rooms to hold · block rate (₹)</legend>
        {types.map(t=>{const free=valid?grpFreeRooms(s,t,a,dep).length:0,held=valid?grpHeldElsewhere(s,t,a,dep):0,cap=Math.max(0,free-held);
          return <div key={t} className="grp-allot-row"><NumberStepper label={t} value={allot[t]||0} min={0} max={Math.min(50,cap)} onChange={v=>setAllot({...allot,[t]:v})}/>
            <label className="grp-rate-input">Rate<input name={'rate-'+t} inputMode="decimal" key={co+t} defaultValue={grpRupees(company?.rates[t]||s.rooms.find(r=>r.type===t)!.rate)}/></label>
            <small>{valid?`${cap} can be held (${free} free${held?`, ${held} held by other blocks`:''})`:'Choose valid dates'}</small></div>;})}
      </fieldset>
      <div className="form-row"><label>Group contact<input name="contact" maxLength={60}/></label><label>Notes<input name="notes" maxLength={200}/></label></div>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!valid||!Object.values(allot).some(n=>n>0)}>Create block <Check size={16}/></button></div>
    </form></>;
}

export function AllotModal({id}:{id?:string}){
  const {s,run,modal,closeModal,busy}=useHcc();const blk=grpBlock(s,id);
  const [allot,setAllot]=useState<Record<string,number>>(()=>({...(blk?.allot||{})}));
  if(!blk)return <Empty title="This block is unavailable"/>;
  const lines=grpLines(s,blk),types=grpRoomTypes(s);
  return <><Head eyebrow={'ALLOTMENT · '+blk.id} title={blk.name} sub="Change the rooms held per type. The allotment cannot go below rooms already picked up; increases are checked against free inventory."/>
    <form className="form" onSubmit={e=>submit(e,()=>run({type:'GRP_BLOCK_ALLOT',id:blk.id,allot,key:modal!.key},'Allotment updated',{close:true}))}>
      {types.map(t=>{const l=lines.find(x=>x.type===t);return <div key={t} className="grp-allot-row"><NumberStepper label={t} value={allot[t]||0} min={l?.picked||0} max={50} onChange={v=>setAllot({...allot,[t]:v})}/><small>{l?`${l.picked} picked up · ${l.remaining} unpicked · ${l.free} free now`:'Not in this block yet'}</small></div>;})}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Save allotment <Check size={16}/></button></div>
    </form></>;
}

export function ReleaseModal({id}:{id?:string}){
  const {s,run,modal,closeModal,busy}=useHcc();const blk=grpBlock(s,id);const [mode,setMode]=useState('all');const [n,setN]=useState(1);
  if(!blk)return <Empty title="This block is unavailable"/>;
  const lines=grpLines(s,blk).filter(l=>l.remaining>0);const sel=lines.find(l=>l.type===mode);
  return <><Head eyebrow={'RELEASE · '+blk.id} title={blk.name} sub="Give unpicked rooms back to general sale. Releasing everything closes pickup for this block."/>
    <form className="form" onSubmit={e=>submit(e,()=>run(mode==='all'?{type:'GRP_RELEASE',id:blk.id,key:modal!.key}:{type:'GRP_RELEASE',id:blk.id,roomType:mode,count:n,key:modal!.key},'Rooms released',{close:true}))}>
      <label>What to release<select value={mode} onChange={e=>{setMode(e.target.value);setN(1);}}><option value="all">All unpicked rooms and close pickup</option>{lines.map(l=><option key={l.type} value={l.type}>{l.type} — {l.remaining} unpicked</option>)}</select></label>
      {sel&&<NumberStepper label={'Rooms to release'} value={n} min={1} max={sel.remaining} onChange={setN}/>}
      {!lines.length&&<div className="info-note"><Info size={16}/>No unpicked rooms remain.</div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||(mode!=='all'&&!sel)}>Release <Check size={16}/></button></div>
    </form></>;
}

export function CancelBlockModal({id}:{id?:string}){
  const {s,run,modal,closeModal,busy}=useHcc();const blk=grpBlock(s,id);
  if(!blk)return <Empty title="This block is unavailable"/>;
  return <><Head eyebrow={'CANCEL · '+blk.id} title={blk.name} sub="Allowed only when no member booking is held, confirmed or in house. Unpicked rooms are released."/>
    <form className="form" onSubmit={e=>submit(e,f=>run({type:'GRP_BLOCK_CANCEL',id:blk.id,reason:field(f,'reason'),key:modal!.key},'Block cancelled',{close:true}))}>
      <label>Reason<input name="reason" required minLength={3} maxLength={200} placeholder="e.g. Event postponed" enterKeyHint="send"/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Keep block</button><button className="btn danger" disabled={busy}>Cancel block</button></div>
    </form></>;
}

type Row={guest:string;phone:string;type:string;room:string;people:number};
/** Rooming list → bookings. Desktop: grid rows; phone (mobile=true): one card per guest, larger controls. */
export function RoomingForm({id,mobile}:{id?:string;mobile?:boolean}){
  const {s,uid,run,modal,closeModal,busy,permission}=useHcc();const blk=grpBlock(s,id);
  const types=blk?Object.keys(blk.allot).filter(t=>grpRemaining(s,blk,t)>0):[];
  const [rows,setRows]=useState<Row[]>(()=>[{guest:'',phone:'',type:types[0]||'',room:'',people:1}]);
  const [override,setOverride]=useState(false),[reason,setReason]=useState('');
  const [rstOn,setRstOn]=useState(false),[rstReason,setRstReason]=useState('');
  if(!blk)return <Empty title="This block is unavailable"/>;
  const a=blk.arrival<s.date?s.date:blk.arrival;
  const set=(i:number,p:Partial<Row>)=>setRows(rows.map((r,j)=>j===i?{...r,...p}:r));
  const used=(t:string)=>rows.filter(r=>r.type===t).length;
  const over=types.filter(t=>used(t)>grpRemaining(s,blk,t));
  const c=grpCompany(s,blk.company),bill=blk.billTo==='company'&&c;
  const added=rows.reduce((n,r)=>n+(r.type?grpStayValue(s,blk.rates[r.type]||0,a,blk.departure):0),0);
  const k=bill?grpCredit(s,c!):undefined,after=k?k.exposure+added:0,level=k?grpCreditLevel(c!.creditLimit,after):'ok';
  const needOverride=!!bill&&after>c!.creditLimit,canOverride=permission('approve');
  // Stay restrictions (DEC-053) for the block dates and the room types on this list; one override covers the whole list.
  const rst=rstState(s,uid,rows.map(r=>r.type),a,blk.departure,rstOn?rstReason:null);
  const ok=rows.every(r=>r.guest.trim().length>=2&&r.type)&&!over.length&&(!needOverride||(canOverride&&override&&reason.trim().length>=3))&&rst.canSubmit;
  const send=()=>run({type:'GRP_ROOMING',block:blk.id,rows:rows.map(r=>({guest:r.guest.trim(),phone:r.phone.trim()||undefined,type:r.type,room:r.room||undefined,people:r.people})),override:needOverride&&override||undefined,overrideReason:needOverride?reason.trim():undefined,restrictionOverride:rst.restrictionOverride,key:modal!.key},`${rows.length} booking${rows.length>1?'s':''} created`,{close:true});
  return <><Head eyebrow={'ROOMING LIST · '+blk.id} title={blk.name} sub={`${dateLabel(a)} → ${dateLabel(blk.departure)} · each guest becomes a confirmed booking at the block rate${bill?', billed to '+c!.name:''}.`}/>
    <div className={mobile?"grp-remaining grp-remaining-m":"grp-remaining"}>{Object.keys(blk.allot).map(t=><span key={t} className={'pill '+(used(t)>grpRemaining(s,blk,t)?'grp-red':'neutral')}>{t}: {used(t)} / {grpRemaining(s,blk,t)} left</span>)}</div>
    <form className="form" onSubmit={e=>{e.preventDefault();if(ok)send();}}>
      <div className={mobile?'grp-rows grp-rows-m':'grp-rows'}>
        {rows.map((r,i)=>{const taken=rows.filter((x,j)=>j!==i&&x.room).map(x=>x.room);const free=r.type?grpFreeRooms(s,r.type,a,blk.departure).filter(x=>!taken.includes(x)):[];
          return <div key={i} className="grp-row"><div className="grp-row-head"><b>Guest {i+1}</b>{rows.length>1&&<button type="button" className="btn subtle small-btn" aria-label={`Remove guest ${i+1}`} onClick={()=>setRows(rows.filter((_,j)=>j!==i))}><Trash2 size={15}/>{mobile&&' Remove'}</button>}</div>
            <label>Guest name<input value={r.guest} required minLength={2} maxLength={60} onChange={e=>set(i,{guest:e.target.value})} placeholder="Full name" enterKeyHint="next" autoComplete="off"/></label>
            <label>Phone<input value={r.phone} type="tel" inputMode="tel" maxLength={20} onChange={e=>set(i,{phone:e.target.value})} placeholder="+91 90000 00000" enterKeyHint="next"/></label>
            <label>Room type<select value={r.type} onChange={e=>set(i,{type:e.target.value,room:''})}>{Object.keys(blk.allot).filter(t=>blk.allot[t]>0).map(t=><option key={t} value={t}>{t} · {money(blk.rates[t])}</option>)}</select></label>
            <label>Room<select value={r.room} onChange={e=>set(i,{room:e.target.value})}><option value="">Auto-assign</option>{free.map(x=><option key={x} value={x}>{x}</option>)}</select></label>
            {mobile?<NumberStepper label="Guests" value={r.people} min={1} max={4} onChange={v=>set(i,{people:v})}/>:<label>Guests<input type="number" inputMode="numeric" min={1} max={4} value={r.people} onChange={e=>set(i,{people:Math.max(1,Math.min(4,Number(e.target.value)||1))})}/></label>}
          </div>;})}
      </div>
      <button type="button" className="btn grp-add" disabled={rows.length>=50} onClick={()=>setRows([...rows,{guest:'',phone:'',type:rows.at(-1)?.type||types[0]||'',room:'',people:1}])}><Plus size={16}/> Add guest</button>
      {over.length>0&&<div className="info-note grp-warn" role="alert"><AlertTriangle size={16}/>More {over.join(', ')} guests than unpicked rooms. Increase the allotment first.</div>}
      <RestrictionNotice st={rst} on={rstOn} reason={rstReason} onToggle={setRstOn} onReason={setRstReason} subject={`this block (${dateLabel(a)} → ${dateLabel(blk.departure)})`}/>
      {bill&&<div className={'grp-credit-box '+level} aria-live="polite"><div><b>Credit check · {c!.name}</b><span>Exposure {money(k!.exposure)} + this list {money(added)} = <b>{money(after)}</b> of {money(c!.creditLimit)} limit</span></div><GrpCreditPill level={level}/></div>}
      {needOverride&&(canOverride?<><label className="checkbox-line"><input type="checkbox" checked={override} onChange={e=>setOverride(e.target.checked)}/>Override the credit limit (audited)</label>
        {override&&<label>Override reason<input value={reason} minLength={3} maxLength={200} onChange={e=>setReason(e.target.value)} placeholder="e.g. Advance cheque received" enterKeyHint="send"/></label>}</>
        :<div className="info-note grp-warn" role="alert"><AlertTriangle size={16}/>{GRP_CREDIT_LABEL.over}: only an Owner or Manager can override. Reduce the list, ask for a higher limit, or switch user.</div>)}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!ok}>Create {rows.length} booking{rows.length>1?'s':''} <Check size={16}/></button></div>
    </form></>;
}
export function RoomingModal({id}:{id?:string}){return <RoomingForm id={id}/>;}
export function RoomingMobile({id}:{id?:string}){return <RoomingForm id={id} mobile/>;}

export function PrintModal({id}:{id?:string}){
  const {s,closeModal}=useHcc();const blk=id?.startsWith('folio:')?grpBlock(s,id.slice(6)):undefined;
  return <><div className="grp-print-doc invoice">
    <div className="grp-print-head"><div><div className="eyebrow">{s.settings.hotel} · DEMONSTRATION DOCUMENT</div><h2>{blk?'Master folio · '+blk.name:'Group pickup report'}</h2>
      <p>{blk?`${blk.id} · ${shortDate(blk.arrival)} → ${shortDate(blk.departure)}`:`All blocks`} · printed {dateLabel(s.date)} {s.clock.slice(11,16)} (simulated clock)</p></div><span className="grp-stamp">SAMPLE · NOT A TAX INVOICE</span></div>
    {blk?<MasterFolio blk={blk} print/>:<PickupTable rows={grpPickupReport(s)}/>}
    <p className="footnote">{grpSimNote}</p></div>
    <div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print / save PDF</button></div></>;
}
