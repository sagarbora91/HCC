// Module 21 · OTA channel management — desktop page (inbox, channels & mapping, rate/availability push, commission report). All SIMULATED.
import React,{useState} from 'react';
import {RefreshCw,Send,Printer,PlugZap,Pause,Unplug,Settings2,Check,X} from 'lucide-react';
import {money,chnSlice,chnPending,chnAcceptCheck,chnPushPreview,chnCommissionReport,chnRoomTypes,chnChannelName,chnPct,CHN_PUSH_DAYS,
  type CHNMessage,type CHNChannel} from '../../services/mock';
import {useHcc} from '../../app/context';
import {PageTitle,Empty,shortDate,clockTime} from '../../components/ui';
import {ChnPill,ChnSimulated,stay,party,firstName} from './shared';

export type ChnTab='inbox'|'channels'|'push'|'commission';
export const CHN_TABS:{id:ChnTab;label:string}[]=[{id:'inbox',label:'Inbox'},{id:'channels',label:'Channels & mapping'},{id:'push',label:'Rate & availability push'},{id:'commission',label:'Commission report'}];
export const chnTabOf=(t:string):ChnTab=>(CHN_TABS.some(x=>x.id===t)?t:'inbox') as ChnTab;

export function ChannelsPage(){
  const {s,tab,setTab,run,busy}=useHcc();const t=chnTabOf(tab);
  const st=chnSlice(s),pend=chnPending(s),rep=chnCommissionReport(s);
  return <><PageTitle title="OTA channels" sub="Module 21 · channel manager preview — connections, room mapping, rate & availability push, inbound reservations and commission. Simulated."
    action={<button className="btn primary" disabled={busy} onClick={()=>run({type:'CHN_PULL'},'Checked channels (simulated)')}><RefreshCw size={15}/> Check for new messages</button>}/>
    <ChnSimulated/>
    <div className="small-stats">
      <div><span>Pending messages</span><strong>{pend.length}</strong></div>
      <div><span>Connected channels</span><strong>{st.channels.filter(c=>c.status==='Connected').length}/{st.channels.length}</strong></div>
      <div><span>OTA bookings (active)</span><strong>{rep.total.bookings}</strong></div>
      <div><span>Commission due</span><strong>{money(rep.total.commission)}</strong></div>
    </div>
    <section className="card">
      <div className="tabs chn-tabs" role="tablist">{CHN_TABS.map(x=><button key={x.id} role="tab" aria-selected={t===x.id} className={t===x.id?'active':''} onClick={()=>setTab(x.id)}>{x.label}{x.id==='inbox'&&pend.length>0&&<span>{pend.length}</span>}</button>)}</div>
      {t==='inbox'&&<InboxTab/>}{t==='channels'&&<ChannelsTab/>}{t==='push'&&<PushTab/>}{t==='commission'&&<CommissionTab/>}
    </section></>;
}

export function PrecheckCell({m}:{m:CHNMessage}){
  const {s}=useHcc();const c=chnAcceptCheck(s,m);
  return <div className="chn-check"><ChnPill status={c.ok?'Accepted':'Rejected'}>{c.ok?'Can accept':'Cannot accept'}</ChnPill><small>{c.reason}</small>{c.warning&&<small className="chn-warn-text">{c.warning}</small>}</div>;
}

function InboxTab(){
  const {s,run,busy,open}=useHcc();const pend=chnPending(s);
  const decided=chnSlice(s).messages.filter(m=>m.status!=='Pending').sort((a,b)=>(b.decidedAt||'').localeCompare(a.decidedAt||'')).slice(0,8);
  return <div>
    <div className="table-wrap"><table className="chn-table"><thead><tr><th>Received</th><th>Channel · ref</th><th>Guest</th><th>Room type · stay</th><th className="number">Channel amount</th><th>Pre-check</th><th>Action</th></tr></thead><tbody>
      {pend.map(m=>{const ok=chnAcceptCheck(s,m).ok;return <tr key={m.id}>
        <td>{clockTime(m.receivedAt)}<small>{shortDate(m.receivedAt.slice(0,10))}</small></td>
        <td><button className="text-btn" onClick={()=>open({type:'chn-message',id:m.id})}><strong>{chnChannelName(s,m.channel)}</strong></button><small>{m.ref} · <ChnPill status={m.kind}/></small></td>
        <td>{m.guest}<small>{party(m.adults,m.children)}</small></td>
        <td>{m.roomType}<small>{stay(m.arrival,m.departure)}</small></td>
        <td className="number">{m.amount?money(m.amount):'—'}</td>
        <td><PrecheckCell m={m}/></td>
        <td><div className="chn-actions">
          <button className="btn primary small-btn" disabled={busy||!ok} onClick={()=>run({type:'CHN_ACCEPT',id:m.id},m.kind==='New'?`${m.ref} accepted — booking created`:`${m.ref} ${m.kind.toLowerCase()} applied`)}><Check size={14}/> Accept</button>
          <button className="btn small-btn" disabled={busy} onClick={()=>open({type:'chn-reject',id:m.id})}><X size={14}/> Reject</button></div></td>
      </tr>;})}
    </tbody></table>{!pend.length&&<Empty title="Inbox clear" body="No pending OTA messages. Use “Check for new messages” to simulate the next delivery."/>}</div>
    {decided.length>0&&<><div className="section-title"><h2>Recently decided</h2><span className="muted">Last {decided.length}</span></div>
      <div className="table-wrap"><table className="chn-table"><thead><tr><th>Channel · ref</th><th>Kind</th><th>Guest</th><th>Result</th><th>By</th></tr></thead><tbody>
        {decided.map(m=><tr key={m.id}><td><strong>{chnChannelName(s,m.channel)}</strong><small>{m.ref}</small></td><td>{m.kind}</td><td>{m.guest}</td>
          <td><ChnPill status={m.status}/> <small>{m.booking?`${m.booking} · room ${m.room}`:m.reason}</small></td>
          <td>{firstName(s.users.find(u=>u.id===m.decidedBy)?.name)}<small>{m.decidedAt?clockTime(m.decidedAt):''}</small></td></tr>)}
      </tbody></table></div></>}
  </div>;
}

export function ConnectionButtons({c}:{c:CHNChannel}){
  const {run,busy,permission}=useHcc();if(!permission('approve'))return null;
  const go=(action:string,msg:string)=>run({type:'CHN_CHANNEL_STATUS',channel:c.id,action},msg);
  return <>{c.status!=='Connected'&&<button className="btn small-btn" disabled={busy} onClick={()=>go('connect',`${c.name} connected (simulated)`)}><PlugZap size={14}/> {c.status==='Error'?'Reconnect':'Connect'}</button>}
    {c.status==='Connected'&&<button className="btn small-btn" disabled={busy} onClick={()=>go('pause',`${c.name} paused`)}><Pause size={14}/> Pause</button>}
    {c.status!=='Disconnected'&&<button className="btn small-btn subtle" disabled={busy} onClick={()=>go('disconnect',`${c.name} disconnected`)}><Unplug size={14}/> Disconnect</button>}</>;
}
export function StopSellToggle({c,type}:{c:CHNChannel;type:string}){
  const {run,busy,permission}=useHcc();const on=c.stopSell.includes(type);
  return <button type="button" className={'chn-switch'+(on?' on':'')} aria-pressed={on} disabled={busy||!permission('reservations')}
    aria-label={`Stop-sell ${type} on ${c.name}`} onClick={()=>run({type:'CHN_STOP_SELL',channel:c.id,roomType:type,on:!on},`${type} stop-sell ${on?'lifted':'on'} · ${c.name}`)}>
    <span aria-hidden/>{on?'Stop-sell on':'Selling'}</button>;
}

function ChannelsTab(){
  const {s,open,permission}=useHcc();const st=chnSlice(s),types=chnRoomTypes(s);
  return <div className="padded">
    {!permission('approve')&&<div className="info-note chn-note">Connection, commission and mapping changes need a Manager or Owner. You can toggle stop-sell and push availability.</div>}
    <div className="chn-grid">{st.channels.map(c=><article key={c.id} className="chn-channel">
      <header><div><h3>{c.name}</h3><small>{c.statusNote}</small></div><ChnPill status={c.status}/></header>
      <div className="summary-list"><div><span>Last sync</span><strong>{c.lastSync?`${shortDate(c.lastSync.slice(0,10))} ${clockTime(c.lastSync)}`:'Never'}</strong></div>
        <div><span>Commission</span><strong>{chnPct(c.commissionBps)}</strong></div><div><span>Rate markup</span><strong>{chnPct(c.markupBps)}</strong></div></div>
      <table className="chn-map"><thead><tr><th>Room type</th><th>Channel code</th><th>Stop-sell</th></tr></thead><tbody>
        {types.map(t=><tr key={t}><td>{t}</td><td>{c.mapping[t]?<code>{c.mapping[t]}</code>:<span className="chn-muted">Not mapped</span>}</td><td>{c.mapping[t]?<StopSellToggle c={c} type={t}/>:'—'}</td></tr>)}
      </tbody></table>
      <footer><ConnectionButtons c={c}/>{permission('approve')&&<button className="btn small-btn" onClick={()=>open({type:'chn-channel',id:c.id})}><Settings2 size={14}/> Terms & mapping</button>}</footer>
    </article>)}</div>
  </div>;
}

export function usePushChannel(){
  const {s}=useHcc();const st=chnSlice(s);
  const [ch,setCh]=useState(st.channels.find(c=>c.status==='Connected')?.id??st.channels[0]?.id??'');
  return [ch,setCh] as const;
}
function PushTab(){
  const {s,run,busy,user}=useHcc();const st=chnSlice(s);const [ch,setCh]=usePushChannel();
  const c=st.channels.find(c=>c.id===ch);const p=chnPushPreview(s,ch);
  const types=[...new Set(p.rows.map(r=>r.type))];
  return <div className="padded">
    <div className="chn-toolbar"><label className="form chn-inline">Channel<select value={ch} onChange={e=>setCh(e.target.value)}>{st.channels.map(c=><option key={c.id} value={c.id}>{c.name} · {c.status}</option>)}</select></label>
      <p className="chn-muted">What would be sent for the next {CHN_PUSH_DAYS} nights from the business date: rooms free per night (blocked, booked and held rooms excluded) and the hotel tariff {c?`+ ${chnPct(c.markupBps)} markup`:''}.</p>
      <button className="btn primary" disabled={busy||c?.status!=='Connected'||!p.rows.length} onClick={()=>run({type:'CHN_PUSH',channel:ch},`Pushed to ${c?.name} (simulated)`)}><Send size={15}/> Push now</button></div>
    {c&&c.status!=='Connected'&&<div className="info-note chn-note">{c.name} is {c.status.toLowerCase()} — the preview is shown but nothing can be pushed until it is connected.</div>}
    {p.unmapped.length>0&&<div className="info-note chn-note">Not mapped on {c?.name} (not sold, not pushed): {p.unmapped.join(', ')}.</div>}
    {types.length>0?<div className="table-wrap"><table className="chn-ari"><thead><tr><th>Room type</th>{p.dates.map(d=><th key={d}>{shortDate(d)}</th>)}</tr></thead><tbody>
      {types.map(t=><tr key={t}><th scope="row">{t}<small>{p.rows.find(r=>r.type===t)!.code}</small></th>{p.rows.filter(r=>r.type===t).map(r=><td key={r.date} className={r.closed?'closed':''}>
        {r.closed?<><b>Closed</b><small>{r.why}</small></>:<><b>{r.avail} left</b><small>{money(r.rate)}</small></>}</td>)}</tr>)}
    </tbody></table></div>:<Empty title="Nothing to push" body="Map at least one room type to this channel."/>}
    <div className="section-title compact"><h3>Push log</h3><span className="chn-muted">Signed in as {firstName(user.name)}</span></div>
    <div className="table-wrap"><table className="chn-table"><thead><tr><th>When</th><th>Channel</th><th>Summary</th><th>By</th></tr></thead><tbody>
      {st.pushes.slice(0,8).map(x=><tr key={x.id}><td>{shortDate(x.at.slice(0,10))} {clockTime(x.at)}</td><td>{chnChannelName(s,x.channel)}</td><td>{x.summary}</td><td>{firstName(s.users.find(u=>u.id===x.actor)?.name)}</td></tr>)}
    </tbody></table>{!st.pushes.length&&<Empty title="No pushes yet"/>}</div>
  </div>;
}

export function CommissionTables(){
  const {s,booking,permission}=useHcc();const r=chnCommissionReport(s);
  return <>
    <div className="table-wrap"><table className="chn-table"><thead><tr><th>Channel</th><th>Rate</th><th className="number">Bookings</th><th className="number">Room nights</th><th className="number">Room revenue</th><th className="number">Commission</th><th className="number">Net to hotel</th></tr></thead><tbody>
      {r.byChannel.map(x=><tr key={x.channel.id}><td><strong>{x.channel.name}</strong></td><td>{chnPct(x.channel.commissionBps)}</td><td className="number">{x.bookings}</td><td className="number">{x.nights}</td><td className="number">{money(x.base)}</td><td className="number">{money(x.commission)}</td><td className="number">{money(x.net)}</td></tr>)}
      <tr className="chn-total"><td><strong>Total</strong></td><td/><td className="number">{r.total.bookings}</td><td className="number">{r.total.nights}</td><td className="number">{money(r.total.base)}</td><td className="number">{money(r.total.commission)}</td><td className="number">{money(r.total.net)}</td></tr>
    </tbody></table></div>
    <div className="section-title compact"><h3>Bookings</h3></div>
    <div className="table-wrap"><table className="chn-table"><thead><tr><th>Booking</th><th>Channel</th><th>Stay</th><th>Status</th><th className="number">Nights × rate</th><th className="number">Commission</th></tr></thead><tbody>
      {r.rows.map(x=><tr key={x.booking.id}><td>{permission('reservations')?<button className="text-btn" onClick={()=>booking(x.booking.id)}>{x.booking.guest}</button>:x.booking.guest}<small>{x.booking.id} · room {x.booking.room}</small></td>
        <td>{chnChannelName(s,x.channel)}</td><td>{stay(x.booking.arrival,x.booking.departure)}</td><td><ChnPill status={x.booking.status==='Confirmed'?'Accepted':'Info'}>{x.booking.status}</ChnPill></td>
        <td className="number">{x.nights} × {money(x.booking.rate)}</td><td className="number">{money(x.commission)}</td></tr>)}
    </tbody></table>{!r.rows.length&&<Empty title="No OTA bookings yet" body="Accept a reservation from the inbox to see commission here."/>}</div>
    <p className="footnote chn-foot">Base = agreed nightly rate × nights (room revenue, excluding synthetic tax). Cancelled, held and expired bookings are excluded. Bookings entered manually with source “OTA · manual” have no named channel and are not included. Simulated figures.</p>
  </>;
}
function CommissionTab(){
  const {open}=useHcc();
  return <div className="padded chn-print">
    <div className="chn-toolbar"><p className="chn-muted">Commission owed to each channel on active OTA bookings (Confirmed, In house, Departed).</p><button className="btn" onClick={()=>open({type:'chn-commission-print'})}><Printer size={15}/> Print view</button></div>
    <CommissionTables/></div>;
}
