// Module 21 · OTA channels — phone page: chips Inbox / Channels / Push / Commission; cards instead of tables; one sticky primary action.
import React from 'react';
import {RefreshCw,Send,Printer} from 'lucide-react';
import {money,chnSlice,chnPending,chnAcceptCheck,chnPushPreview,chnCommissionReport,chnRoomTypes,chnChannelName,chnPct,CHN_PUSH_DAYS} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,shortDate,clockTime} from '../../components/ui';
import {ListCard,KpiCard,ChipFilter,StickyFooter} from '../../app/mobile/components';
import {ChnPill,ChnSimulated,stay,party} from './shared';
import {chnTabOf,usePushChannel,StopSellToggle,type ChnTab} from './Channels';

const pl=(n:number,w:string)=>`${n} ${w}${n===1?'':'s'}`;
export function ChannelsMobile(){
  const {s,tab,setTab,run,busy,open}=useHcc();const t=chnTabOf(tab);
  const st=chnSlice(s),pend=chnPending(s),rep=chnCommissionReport(s);
  const [ch,setCh]=usePushChannel();const pc=st.channels.find(c=>c.id===ch);
  return <div className="chn-m">
    <ChnSimulated text="Simulated channel manager — fictional OTAs, nothing is sent."/>
    <ChipFilter<ChnTab> label="Channels view" value={t} onChange={v=>setTab(v)} options={[{id:'inbox',label:'Inbox',count:pend.length},{id:'channels',label:'Channels'},{id:'push',label:'Push'},{id:'commission',label:'Commission'}]}/>
    {t==='inbox'&&<>
      {pend.map(m=>{const c=chnAcceptCheck(s,m);return <ListCard key={m.id} onClick={()=>open({type:'chn-message',id:m.id})}
        title={<>{m.guest} <ChnPill status={m.kind}/></>} sub={`${chnChannelName(s,m.channel)} · ${m.ref} · ${m.roomType}`}
        meta={<>{stay(m.arrival,m.departure)} · {party(m.adults,m.children)}<br/><span className={c.ok?'chn-ok-text':'chn-err-text'}>{c.ok?'Can accept':'Cannot accept'}: {c.reason}</span></>}/>;})}
      {!pend.length&&<Empty title="Inbox clear" body="No pending OTA messages."/>}
      <StickyFooter><button className="btn primary" disabled={busy} onClick={()=>run({type:'CHN_PULL'},'Checked channels (simulated)')}><RefreshCw size={18}/> Check for new messages</button></StickyFooter></>}
    {t==='channels'&&st.channels.map(c=><section key={c.id} className="chn-mcard">
      <ListCard title={c.name} sub={c.statusNote} meta={`Commission ${chnPct(c.commissionBps)} · markup ${chnPct(c.markupBps)} · last sync ${c.lastSync?clockTime(c.lastSync):'never'}`} right={<ChnPill status={c.status}/>} onClick={()=>open({type:'chn-channel',id:c.id})}/>
      <div className="chn-mstop">{chnRoomTypes(s).map(type=><div key={type}><span>{type}<small>{c.mapping[type]||'Not mapped'}</small></span>{c.mapping[type]?<StopSellToggle c={c} type={type}/>:<span className="chn-muted">—</span>}</div>)}</div>
    </section>)}
    {t==='push'&&<>
      <label className="m-field-label chn-mselect">Channel<select value={ch} onChange={e=>setCh(e.target.value)}>{st.channels.map(c=><option key={c.id} value={c.id}>{c.name} · {c.status}</option>)}</select></label>
      <p className="chn-muted">Next {CHN_PUSH_DAYS} nights · rooms free and rate incl. {pc?chnPct(pc.markupBps):'0%'} markup.</p>
      {chnPushPreview(s,ch).unmapped.length>0&&<p className="chn-muted">Not mapped: {chnPushPreview(s,ch).unmapped.join(', ')}</p>}
      {[...new Set(chnPushPreview(s,ch).rows.map(r=>r.type))].map(type=>{const rows=chnPushPreview(s,ch).rows.filter(r=>r.type===type);
        return <section key={type} className="chn-mpush"><h3>{type} <small>{rows[0].code}</small></h3>
          <ul>{rows.map(r=><li key={r.date} className={r.closed?'closed':''}><span>{shortDate(r.date)}</span><b>{r.closed?'Closed':`${r.avail} left`}</b><small>{r.closed?r.why:money(r.rate)}</small></li>)}</ul></section>;})}
      {!chnPushPreview(s,ch).rows.length&&<Empty title="Nothing to push" body="Map a room type on desktop or in the channel sheet."/>}
      <StickyFooter><button className="btn primary" disabled={busy||pc?.status!=='Connected'||!chnPushPreview(s,ch).rows.length} onClick={()=>run({type:'CHN_PUSH',channel:ch},`Pushed to ${pc?.name} (simulated)`)}><Send size={18}/> {pc?.status==='Connected'?`Push to ${pc.name}`:`${pc?.name??'Channel'} not connected`}</button></StickyFooter></>}
    {t==='commission'&&<>
      <div className="chn-mkpis"><KpiCard label="Commission due" value={money(rep.total.commission)} sub={pl(rep.total.bookings,'booking')}/><KpiCard label="Net to hotel" value={money(rep.total.net)} sub={pl(rep.total.nights,'room night')}/></div>
      {rep.byChannel.map(x=><ListCard key={x.channel.id} title={x.channel.name} sub={`${pl(x.bookings,'booking')} · ${pl(x.nights,'night')} · ${chnPct(x.channel.commissionBps)}`} meta={`Revenue ${money(x.base)} · net ${money(x.net)}`} right={<strong>{money(x.commission)}</strong>}/>)}
      <h3 className="m-group">BOOKINGS</h3>
      {rep.rows.map(x=><ListCard key={x.booking.id} title={x.booking.guest} sub={`${chnChannelName(s,x.channel)} · ${x.booking.id} · ${x.booking.status}`} meta={`${stay(x.booking.arrival,x.booking.departure)} · ${x.nights} × ${money(x.booking.rate)}`} right={<strong>{money(x.commission)}</strong>}/>)}
      {!rep.rows.length&&<Empty title="No OTA bookings yet" body="Accept a reservation from the inbox."/>}
      <StickyFooter><button className="btn primary" onClick={()=>open({type:'chn-commission-print'})}><Printer size={18}/> Print report</button></StickyFooter></>}
  </div>;
}
