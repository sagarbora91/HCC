// Room keys — page 'keys' (nav "Room keys"): tabs Key log (report with filters and totals) · Lock mapping (Manager / Owner) · Encoder (Manager / Owner).
import React,{useState} from 'react';
import {Printer,Save,FlaskConical,Play,Clock} from 'lucide-react';
import {useHcc} from '../../app/context';
import {printDocument} from '../../app/native';
import {keyCanRead,keyCanConfigure,keyLog,keySlice,keyLockOf,keyBisDoor,keyEncoderOnline,KEY_EVENTS,KEY_EVENT_LABEL,KEY_REASON_LABEL,KEY_RESULT_HINT} from '../../services/mock';
import {Empty,Pill} from '../../components/ui';
import {FitPage,FitTabs,ScrollPanel,useFitTab} from '../../components/fit';
import {EncoderBadge,KeyStatePill,keyKey,keyUserName} from './KeyPanel';

type Tab='log'|'locks'|'encoder';
const when=(at:string)=>at.slice(0,10)+' '+at.slice(11,16);

function LogTab(){
  const {s,uid}=useHcc();
  const [f,setF]=useState({from:'',to:'',room:'',booking:'',user:'',event:''});
  const set=(k:keyof typeof f,v:string)=>setF({...f,[k]:v});
  const r=keyLog(s,{from:f.from||undefined,to:f.to||undefined,room:f.room||undefined,booking:f.booking||undefined,user:f.user||undefined,event:f.event||undefined});
  const users=[...new Set(keySlice(s).jobs.map(j=>j.by))];
  const t=r.totals;
  return <><section className="card key-log-card">
    <div className="key-filters" role="search" aria-label="Key log filters">
      <label>From<input type="date" value={f.from} onChange={e=>set('from',e.target.value)}/></label>
      <label>To<input type="date" value={f.to} onChange={e=>set('to',e.target.value)}/></label>
      <label>Room<select value={f.room} onChange={e=>set('room',e.target.value)}><option value="">All</option>{s.rooms.map(x=><option key={x.id} value={x.id}>{x.id}</option>)}</select></label>
      <label>Booking<select value={f.booking} onChange={e=>set('booking',e.target.value)}><option value="">All</option>{s.bookings.filter(b=>keySlice(s).jobs.some(j=>j.booking===b.id)).map(b=>(<option key={b.id} value={b.id}>{b.id} · {b.guest}</option>))}</select></label>
      <label>User<select value={f.user} onChange={e=>set('user',e.target.value)}><option value="">All</option>{users.map(u=><option key={u} value={u}>{keyUserName(s,u)}</option>)}</select></label>
      <label>Event<select value={f.event} onChange={e=>set('event',e.target.value)}><option value="">All</option>{KEY_EVENTS.map(e=><option key={e} value={e}>{KEY_EVENT_LABEL[e]}</option>)}</select></label>
      <button className="btn" onClick={()=>{void printDocument();}}><Printer size={15}/>Print</button>
    </div>
    <div className="small-stats key-totals" aria-label="Totals">
      <div><span>Issued</span><strong className="number">{t.issued}</strong></div>
      <div><span>Re-issued</span><strong className="number">{t.reissued}</strong></div>
      <div><span>Extended</span><strong className="number">{t.extended}</strong></div>
      <div><span>Reactivated</span><strong className="number">{t.reactivated}</strong></div>
      <div><span>Cancelled</span><strong className="number">{t.cancelled}</strong></div>
      <div><span>Read</span><strong className="number">{t.read}</strong></div>
      <div><span>Rows shown</span><strong className="number">{r.total}</strong></div>
    </div></section>
    <ScrollPanel label="Key log">{r.rows.length?<div className="table-wrap key-log-table"><table><thead><tr><th>When</th><th>Event</th><th>Reason</th><th>Room</th><th>Booking</th><th>By</th><th>State</th><th>Card</th></tr></thead>
      <tbody>{r.rows.map(j=><tr key={j.id}><td>{when(j.at)}</td><td>{KEY_EVENT_LABEL[j.event]}{j.kind==='manual'?' (manual)':''}</td><td>{j.reason?KEY_REASON_LABEL[j.reason]:'—'}</td><td>{j.room??'—'}</td><td>{j.booking??'—'}</td><td>{keyUserName(s,j.by)}</td><td><KeyStatePill j={j}/></td><td>{j.cardNo??'—'}</td></tr>)}</tbody></table></div>
      :<Empty title="No key events match" body="Change the filters, or issue a key from a booking."/>}
  </ScrollPanel></>;}

function LocksTab(){
  const {s,run,busy}=useHcc();
  const [edit,setEdit]=useState<Record<string,{hasLock:boolean;doorName:string;lockNo:string;note:string}>>({});
  return <ScrollPanel label="Lock mapping"><div className="key-locks-card"><p className="muted padded">Map each HCC room to its BIS door. Room 106 has no working lock (manual key); a room with no BIS door has no lock until you map it here.</p>
    <div className="table-wrap key-locks-table"><table><thead><tr><th>Room</th><th>Lock</th><th>BIS door name</th><th>Lock no.</th><th>Note</th><th/></tr></thead>
      <tbody>{s.rooms.map(r=>{const cur=keyLockOf(s,r.id),e=edit[r.id]??{hasLock:cur.hasLock,doorName:cur.doorName??'',lockNo:cur.lockNo?String(cur.lockNo):'',note:cur.note??''};
        const dirty=!!edit[r.id];const up=(p:Partial<typeof e>)=>setEdit({...edit,[r.id]:{...e,...p}});
        return <tr key={r.id}><td><strong>{r.id}</strong>{!keyBisDoor(r.id)&&<small> · no BIS door</small>}</td>
          <td><label className="key-check"><input type="checkbox" checked={e.hasLock} onChange={x=>up({hasLock:x.target.checked,doorName:x.target.checked&&!e.doorName?r.id:e.doorName})}/>Has lock</label></td>
          <td><input aria-label={`Door name ${r.id}`} value={e.doorName} maxLength={20} onChange={x=>up({doorName:x.target.value})}/></td>
          <td><input aria-label={`Lock number ${r.id}`} inputMode="numeric" value={e.lockNo} maxLength={3} onChange={x=>up({lockNo:x.target.value.replace(/\D/g,'')})}/></td>
          <td><input aria-label={`Note ${r.id}`} value={e.note} maxLength={200} onChange={x=>up({note:x.target.value})}/></td>
          <td><button className="btn" disabled={busy||!dirty} onClick={()=>run({type:'KEY_LOCK_SAVE',room:r.id,hasLock:e.hasLock,doorName:e.doorName,lockNo:e.lockNo,note:e.note,key:keyKey(s,'lock',r.id)},`Room ${r.id} mapping saved`,{}).then(()=>{const n={...edit};delete n[r.id];setEdit(n);})}><Save size={14}/>Save</button></td></tr>;})}</tbody></table></div>
  </div></ScrollPanel>;}

function EncoderTab(){
  const {s,run,busy}=useHcc();const x=keySlice(s),on=keyEncoderOnline(s);
  const queued=x.jobs.filter(j=>j.state==='queued'||j.state==='picked'||j.state==='waiting_card').length;
  return <ScrollPanel label="Encoder"><div className="key-enc-card"><div className="padded key-enc">
    <p><EncoderBadge/> Front-desk agent FRONTDESK-1 · last heartbeat {x.encoderSeen?when(x.encoderSeen):'never'} · {queued} job{queued===1?'':'s'} waiting</p>
    <p className="muted">{on?'The agent is polling the server.':KEY_RESULT_HINT.EncoderOffline}</p>
    <h3><FlaskConical size={15}/> Simulated encoder (demo only, the keyagent mock)</h3>
    <div className="key-actions">
      <button className="btn" disabled={busy} onClick={()=>run({type:'KEY_ENCODER',online:!on,key:keyKey(s,'enc')},on?'Agent offline':'Agent online',{})}>{on?'Take agent offline':'Bring agent online'}</button>
      <button className="btn" disabled={busy} onClick={()=>run({type:'KEY_AGENT_STEP',stage:'pick',key:keyKey(s,'pick')},'Picked',{})}><Play size={14}/>Pick up job</button>
      <button className="btn" disabled={busy} onClick={()=>run({type:'KEY_AGENT_STEP',stage:'wait',key:keyKey(s,'wait')},'Waiting for card',{})}>Wait for card</button>
      <button className="btn primary" disabled={busy} onClick={()=>run({type:'KEY_AGENT_STEP',key:keyKey(s,'step')},'Encoder ran the job',{})}>Finish job</button>
      <button className="btn" disabled={busy} onClick={()=>run({type:'KEY_CLOCK',minutes:3,key:keyKey(s,'clock')},'Clock +3 min',{})}><Clock size={14}/>+3 min</button>
    </div>
    <div className="key-actions">
      <label className="key-check"><input type="checkbox" checked={x.sim.cardPresent} onChange={e=>run({type:'KEY_SIM',cardPresent:e.target.checked,key:keyKey(s,'sim1',e.target.checked)},'Saved',{})}/>Card on the encoder</label>
      <label className="key-check"><input type="checkbox" checked={x.sim.failNext} onChange={e=>run({type:'KEY_SIM',failNext:e.target.checked,key:keyKey(s,'sim2',e.target.checked)},'Saved',{})}/>Fail the next write</label>
      <label className="key-check"><input type="checkbox" checked={x.sim.supportsExtend} onChange={e=>run({type:'KEY_SIM',supportsExtend:e.target.checked,key:keyKey(s,'sim3',e.target.checked)},'Saved',{})}/>Extend is mapped in BIS</label>
    </div>
    <small>Real adapter: extend / reactivate through the BIS screen is not mapped yet and returns NotSupported until it is mapped at the front desk. Everything else works against this simulated encoder.</small>
  </div></div></ScrollPanel>;}

const KEY_TABS=['log','locks','encoder'] as const;
export function KeysPage(){
  const {s,uid}=useHcc();
  const cfg=keyCanConfigure(s,uid);
  const [tab,setTab]=useFitTab<Tab>('keys',KEY_TABS,'log');
  if(!keyCanRead(s,uid))return <FitPage title="Room keys" sub="Key cards, counts and log"><Empty title="No access" body="Room keys are for front-desk staff and managers."/></FitPage>;
  const tabs:{id:Tab;label:string}[]=[{id:'log',label:'Key log'},...(cfg?[{id:'locks' as Tab,label:'Lock mapping'},{id:'encoder' as Tab,label:'Encoder'}]:[])];
  const cur=tabs.some(t=>t.id===tab)?tab:'log';
  return <FitPage title="Room keys" sub={`${s.date} · issue, re-issue, reactivate and cancel from the booking · counts come from this log`} actions={<EncoderBadge/>}
    tabs={<FitTabs page="keys" tabs={tabs} value={cur} onChange={setTab}/>}>
    {cur==='log'&&<LogTab/>}{cur==='locks'&&<LocksTab/>}{cur==='encoder'&&<EncoderTab/>}
  </FitPage>;}
