// F4-T1 · Tariff card — phone page: cards, read-only except the Owner (publish / reject). Same figures as the desktop grid.
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import {ListCard,KpiCard,ChipFilter,StickyFooter} from '../../app/mobile/components';
import {Pill,Empty} from '../../components/ui';
import {money} from '../../domain/money';
import {gstPct} from '../../domain/gst';
import {trcSummary,trcVersions} from '../../domain/streams/tariffcard';
import {trcUiState,trcEditorRows} from './logic';
import {useTrcVersion,TRC_STATUS_TONE} from './TariffCardPage';

export function TariffCardMobile(){
  const {s,uid,run,busy}=useHcc();const [v,setV]=useTrcVersion();const ui=trcUiState(s,uid,v,true);const sum=trcSummary(s);
  const [reason,setReason]=useState('');
  if(!ui.view)return <Empty title="Not available" body={ui.readOnlyNote}/>;
  const list=trcVersions(s);
  return <div className="trc-m">
    <div className="trc-m-kpis"><KpiCard label="Card in force" value={sum.live??'—'} sub={sum.liveFrom?'from '+sum.liveFrom:undefined}/><KpiCard label="Awaiting Owner" value={sum.proposed}/></div>
    {list.length>0&&<ChipFilter label="Version" options={list.slice(0,6).map(x=>({id:x.id,label:`${x.id} · ${x.status}`}))} value={v?.id??list[0].id} onChange={setV}/>}
    {ui.readOnlyNote&&<p className="muted trc-m-note">{ui.readOnlyNote}</p>}
    {!v?<Empty title="No tariff card yet"/>:trcEditorRows(s,v.lines).map(r=><ListCard key={r.id} title={r.title} sub={`${r.daySet} · GST ${gstPct(r.bps)}${r.note?' · '+r.note:''}`}
      meta={r.excl===null?'Price blank':`Before GST ${money(r.excl)}`} right={<strong>{r.incl===null?'Blank':money(r.incl)}</strong>}/>)}
    {v&&<p className="muted">{v.label} · valid from {v.effectiveFrom} · <Pill tone={TRC_STATUS_TONE[v.status]}>{v.status}</Pill></p>}
    {(ui.canPublish||ui.canReject)&&<StickyFooter>
      {ui.canReject&&<input aria-label="Reason for rejecting" placeholder="Reason for rejecting" value={reason} onChange={e=>setReason(e.target.value)}/>}
      {ui.canReject&&<button className="btn" disabled={busy||reason.trim().length<3} onClick={()=>run({type:'TRC_REJECT',id:v!.id,reason},'Proposal rejected')}>Reject</button>}
      {ui.canPublish&&<button className="btn primary" disabled={busy} onClick={()=>run({type:'TRC_PUBLISH',id:v!.id},`${v!.id} published`)}>Publish</button>}
    </StickyFooter>}
  </div>;
}
