// Room keys — the key panel of a booking (modal 'key-panel', id = booking ID): encoder badge, counts, active cards, Issue key / Re-issue / Extend / Reactivate /
// Lost card / Cancel / Read, the card-expiry warning, the manual-key fallback and the booking's key jobs.
import React,{useState} from 'react';
import {KeyRound,RefreshCw,CalendarPlus,Power,AlertTriangle,XCircle,ScanLine,HandHelping,Cpu} from 'lucide-react';
import {useHcc} from '../../app/context';
import type {ModalProps} from '../../app/registry';
import type {Booking,State} from '../../services/mock';
import {keyCanRead,keyCanIssue,keyCanCancel,keyCounts,keyCountLine,keyActiveCards,keyJobsOf,keyJobState,keyEncoderOnline,keyLockOf,keyCardExpiry,keyFolioCheckout,keyMismatchWarning,
  KEY_EVENT_LABEL,KEY_REASON_LABEL,KEY_STATE_LABEL,KEY_REASONS,KEY_RESULT_HINT} from '../../services/mock';
import type {KEYReason,KEYJob} from '../../services/mock';
import {Empty,Pill} from '../../components/ui';

export const keyKey=(s:State,...parts:(string|number|boolean|undefined)[])=>['key',...parts,s.clock,s.audit.length].join(':');
export const keyUserName=(s:State,id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
const when=(at:string)=>at.slice(0,10)+' '+at.slice(11,16);

export function EncoderBadge(){
  const {s}=useHcc();const on=keyEncoderOnline(s);
  return <span className={'pill key-badge '+(on?'key-on':'key-off')} role="status" title="Chabi encoder / Key encoder"><Cpu size={13}/>{on?'Encoder online':'Encoder offline'}</span>;}

export function KeyStatePill({j}:{j:KEYJob}){
  const {s}=useHcc();const st=keyJobState(s,j);
  return <span className={'pill key-st key-st-'+st}>{st==='failed'&&j.code?j.code:KEY_STATE_LABEL[st].split(' (')[0]}</span>;}

/** Button in the booking / folio dialog action row (registered as bookingActions). */
export function KeyBookingAction({b}:{b:Booking}){
  const {s,uid,open}=useHcc();
  if(b.status!=='In house'||!keyCanRead(s,uid))return null;
  return <button className="btn subtle" onClick={()=>open({type:'key-panel',id:b.id})}><KeyRound size={15}/>Room key</button>;}

export function KeyPanelModal({id}:ModalProps){
  const {s,uid,run,busy,closeModal}=useHcc();
  const b=s.bookings.find(b=>b.id===id);
  const [reason,setReason]=useState<KEYReason|''>('');const [validTo,setValidTo]=useState('');const [note,setNote]=useState('');
  if(!b||!keyCanRead(s,uid))return <Empty title="Room key unavailable" body="Keys are for front-desk staff and managers."/>;
  const issue=keyCanIssue(s,uid),cancel=keyCanCancel(s,uid),online=keyEncoderOnline(s),lock=keyLockOf(s,b.room);
  const bc=keyCounts(s,{booking:b.id}),rc=keyCounts(s,{room:b.room}),cards=keyActiveCards(s,b.id),jobs=keyJobsOf(s,b.id);
  const lastDay=validTo||b.departure;
  const warn=keyMismatchWarning(lastDay,keyFolioCheckout(s));
  const open1=jobs.find(j=>{const st=keyJobState(s,j);return st==='queued'||st==='picked'||st==='waiting_card';});
  const failed=jobs.find(j=>keyJobState(s,j)==='failed'||keyJobState(s,j)==='expired');
  const needReason=(ev:string)=>ev!=='issued';
  const send=(event:string,r?:KEYReason)=>run({type:'KEY_REQUEST',booking:b.id,event,reason:r??(reason||undefined),validTo:validTo||undefined,key:keyKey(s,'req',b.id,event)},'Key request sent',{});
  const manual=(event:string,r?:KEYReason)=>run({type:'KEY_MANUAL',booking:b.id,event,reason:r??(reason||undefined),note,key:keyKey(s,'man',b.id,event)},'Recorded: key issued manually',{});
  const blocked=!lock.hasLock;
  const noReason=!reason;
  return <div className="form key-panel">
    <div className="modal-heading"><h2>Room key · room {b.room}</h2><p>{b.guest} · {b.id} · stay {b.arrival} to {b.departure}</p></div>
    <div className="key-top"><EncoderBadge/><span className="key-counts" aria-label="Key counts for this booking">{keyCountLine(bc)}</span>
      <small>Room {b.room} so far: {keyCountLine(rc)}{bc.cancelled||bc.read?` · Cancelled ${bc.cancelled}, Read ${bc.read}`:''}</small></div>
    {blocked&&<div className="key-note" role="status"><AlertTriangle size={16}/><span>{KEY_RESULT_HINT.NoLockRoom} {lock.note?`(${lock.note})`:''}</span></div>}
    {!online&&!blocked&&<div className="key-note" role="status"><AlertTriangle size={16}/><span>{KEY_RESULT_HINT.EncoderOffline}</span></div>}
    {warn&&<div className="key-note key-warn" role="alert"><AlertTriangle size={16}/><span>{warn}</span></div>}
    <div className="key-grid">
      <section className="key-box"><h3>Active cards</h3>
        {cards.length?<ul className="key-cards">{cards.map(c=><li key={c.id}><strong>{c.cardNo??'manual'}</strong> · room {c.room} · valid until {c.to.replace('T',' ')} <Pill tone={c.status==='manual'?'orange':'green'}>{c.status==='manual'?'issued manually':'active'}</Pill></li>)}</ul>:<p className="muted">No active card yet.</p>}
        {open1&&<p className="key-live" role="status"><strong>{KEY_STATE_LABEL[keyJobState(s,open1)]}</strong> · {KEY_EVENT_LABEL[open1.event]}</p>}
      </section>
      <section className="key-box"><h3>Action</h3>
        <div className="key-fields">
          <label>Valid until<input type="date" value={validTo} min={s.date} onChange={e=>setValidTo(e.target.value)}/><small>Default: stay departure {b.departure} (card ends {keyCardExpiry(lastDay).replace('T',' ')})</small></label>
          <label>Reason (re-issue, extend, reactivate)<select value={reason} onChange={e=>setReason(e.target.value as KEYReason|'')}><option value="">Choose…</option>{KEY_REASONS.map(r=><option key={r} value={r}>{KEY_REASON_LABEL[r]}</option>)}</select></label>
        </div>
        <div className="key-actions">
          <button className="btn primary" disabled={busy||!issue||blocked||!!open1||!online} title="Chabi do · Issue key" onClick={()=>send('issued')}><KeyRound size={15}/>Issue key</button>
          <button className="btn" disabled={busy||!issue||blocked||!!open1||!online||noReason} title="Naya card · Re-issue" onClick={()=>send('reissued')}><RefreshCw size={15}/>Re-issue</button>
          <button className="btn" disabled={busy||!issue||blocked||!!open1||!online||noReason} title="Stay badhi · Extend validity" onClick={()=>send('extended')}><CalendarPlus size={15}/>Extend</button>
          <button className="btn" disabled={busy||!issue||blocked||!!open1||!online||noReason} title="Card dobara chalu · Reactivate" onClick={()=>send('reactivated')}><Power size={15}/>Reactivate</button>
          <button className="btn" disabled={busy||!issue||blocked||!!open1||!online} title="Card kho gaya · Lost card" onClick={()=>send('reissued','lost')}><AlertTriangle size={15}/>Lost card</button>
          <button className="btn" disabled={busy||!cancel||blocked||!!open1||!online||!cards.length} title="Chabi band · Cancel key" onClick={()=>run({type:'KEY_CANCEL',booking:b.id,key:keyKey(s,'cancel',b.id)},'Cancel request sent')}><XCircle size={15}/>Cancel key</button>
          <button className="btn" disabled={busy||!online||!!open1} title="Card padho · Read" onClick={()=>run({type:'KEY_READ',key:keyKey(s,'read')},'Read request sent')}><ScanLine size={15}/>Read card</button>
        </div>
        <div className="key-manual"><label>Note for the manual record<input value={note} maxLength={200} onChange={e=>setNote(e.target.value)} placeholder="e.g. made in BIS by hand"/></label>
          <button className="btn" disabled={busy||!issue} title="Haath se banaya · Key issued manually" onClick={()=>manual(cards.length?'reissued':'issued',cards.length?(reason||'other') as KEYReason:undefined)}><HandHelping size={15}/>Key issued manually</button>
          <small>Use when the key service is offline or the room has no electronic lock: record the key you made by hand. Check-in and check-out never wait for keys.</small></div>
        {failed&&<p className="key-fail" role="status">{failed.message??KEY_RESULT_HINT[failed.code??'']??''}</p>}
      </section>
    </div>
    <section className="key-box key-jobs"><h3>Key log for this booking</h3>
      {jobs.length?<div className="table-wrap"><table><thead><tr><th>When</th><th>Event</th><th>Reason</th><th>State</th><th>By</th></tr></thead>
        <tbody>{jobs.map(j=><tr key={j.id}><td>{when(j.at)}</td><td>{KEY_EVENT_LABEL[j.event]}{j.kind==='manual'?' (manual)':''}</td><td>{j.reason?KEY_REASON_LABEL[j.reason]:'—'}</td><td><KeyStatePill j={j}/></td><td>{keyUserName(s,j.by)}</td></tr>)}</tbody></table></div>:<p className="muted">No key events yet.</p>}
    </section>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button></div>
  </div>;}
