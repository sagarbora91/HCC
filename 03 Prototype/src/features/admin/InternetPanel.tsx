// F5-U1 · Internet access (EGRESS-CONTRACT.md §5, §6): Master configuration › Internet group (Owner edits, Manager reads), the System health
// Egress block, the "Internet off" banners and the held-queue badge. Screens only: the gate is E1's (domain/streams/egress.ts, CFG_NET_SET).
import React,{useState} from 'react';
import {Globe,Power,Clock,ShieldAlert,CheckCircle2,Lock} from 'lucide-react';
import {useHcc} from '../../app/context';
import {netState,netHeldCounts,netSlice,netOn,NET_SETTING_DEFS,NET_MASTER,NET_WINDOW,NET_QUEUES,NET_INTEGRATIONS,netKeyOf,chnSlice,
  type NetReason,type NetWindow} from '../../services/mock';
import {Pill,Empty,clockTime,dateLabel} from '../../components/ui';

/** Who sees the Internet group and the Egress block: Owner (edit), Manager (read). Everyone else nothing (contract §5, §6). */
export const netRoleSees=(role?:string)=>role==='Owner'||role==='Manager';
export const netRoleEdits=(role?:string)=>role==='Owner';
export const NET_REASON_TEXT:Record<NetReason,string>={on:'On','master-off':'Off — master switch is off','integration-off':'Off — this switch is off','outside-window':'Off — outside the allowed time window','settings-unreadable':'Off — settings unreadable (fails closed)'};
const DAYS=['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
export const netWindowText=(w:NetWindow|null)=>w?`${w.from}–${w.to} on ${w.days.map(d=>DAYS[d]).join(', ')}`:'No window (always, when switched on)';

/** Master configuration › Internet access. The Owner toggles each switch through a reason prompt (CFG_NET_SET {setting, value, reason}). */
export function InternetPanel(){
  const {s,uid,user,open,busy}=useHcc();const st=netState(s,uid);const edit=netRoleEdits(user.role);
  if(!netRoleSees(user.role))return <Empty title="Not available for your role" body="The internet switches are for the Owner (change) and the Manager (read)."/>;
  const switches=NET_SETTING_DEFS.filter(d=>d.key!==NET_WINDOW&&d.key!==NET_MASTER);
  const master=NET_SETTING_DEFS.find(d=>d.key===NET_MASTER)!;
  const toggle=(key:string,on:boolean)=>edit&&<button type="button" className={'btn small-btn'+(on?'':' primary')} disabled={busy} aria-label={`${on?'Switch off':'Switch on'} ${NET_SETTING_DEFS.find(d=>d.key===key)?.label}`} onClick={()=>open({type:'net-set',id:key,room:on?'off':'on'})}><Power size={14}/> {on?'Switch off':'Switch on'}</button>;
  return <div className="net-panel">
    <p className="muted">HCC makes no internet call unless the master switch and the switch of that service are both on (and the time window, if one is set). Everything starts OFF. Work meant for the internet waits in a held queue and is sent in order when its switch is turned on.</p>
    {!edit&&<p className="muted"><Lock size={13}/> Read only — only the Owner changes the internet switches.</p>}
    <div className="net-master"><Globe size={18} aria-hidden/><div><strong>{master.label}</strong><small className="muted"> {master.help}</small></div>
      <Pill tone={st.masterOn?'green':'orange'}>{st.masterOn?'On':'Off'}</Pill>{toggle(NET_MASTER,st.masterOn)}</div>
    <ul className="net-rows" aria-label="Internet switches">{switches.map(d=>{const i=d.key.slice(4) as typeof NET_INTEGRATIONS[number];const on=st.integrations[i];const dec=st.decisions[i];
      return <li key={d.key} className="net-row"><div><strong>{d.label}</strong><small>{d.help}</small><small>Effective now: {dec.allowed?'allowed':NET_REASON_TEXT[dec.reason]}</small></div>
        <Pill tone={on?'green':'neutral'}>{on?'On':'Off'}</Pill>{toggle(d.key,on)}</li>;})}</ul>
    <div className="net-row"><div><strong><Clock size={14} aria-hidden/> Allowed time window</strong><small>{NET_SETTING_DEFS.find(d=>d.key===NET_WINDOW)!.help}</small><small>{netWindowText(st.window)}</small></div>
      {edit&&<button type="button" className="btn small-btn" disabled={busy} onClick={()=>open({type:'net-set',id:NET_WINDOW,room:'window'})}>Change window…</button>}</div>
    <LastChange/>
  </div>;
}
function LastChange(){
  const {s,uid}=useHcc();const ch=[...netSlice(s).changes].reverse().slice(0,6);const who=(id:string)=>s.users.find(u=>u.id===id)?.name??id;
  if(!ch.length)return <p className="muted">No change has been made yet — every switch is at its default (off).</p>;
  return <section aria-label="Recent changes"><h3 className="acc-h3">Recent changes</h3><ul className="acc-hist">{ch.map(c=><li key={c.id}><strong>{c.key}</strong>: {String(c.before===true?'ON':c.before===false?'OFF':c.before?JSON.stringify(c.before):'none')} → {String(c.after===true?'ON':c.after===false?'OFF':c.after?JSON.stringify(c.after):'none')} · {s.users.find(u=>u.id===uid)?.role==='Owner'?who(c.by)+' · ':''}{dateLabel(c.at.slice(0,10))} {clockTime(c.at)} — {c.reason}</li>)}</ul></section>;
}

/** 'net-set' · id = setting key, room = 'on' | 'off' | 'window'. Owner only; the reason (3–120 characters) is kept with the change. */
export function NetSetModal({id,room}:{id?:string;room?:string}){
  const {s,user,run,closeModal,busy}=useHcc();const key=id||NET_MASTER;const def=NET_SETTING_DEFS.find(d=>d.key===key);const edit=netRoleEdits(user.role);
  const cur=netState(s).window;const [reason,setReason]=useState('');
  const [from,setFrom]=useState(cur?.from??'08:00');const [to,setTo]=useState(cur?.to??'20:00');const [days,setDays]=useState<number[]>(cur?.days??[0,1,2,3,4,5,6]);const [clear,setClear]=useState(false);
  if(!def)return <Empty title="Unknown internet setting"/>;
  const isWin=key===NET_WINDOW,value:unknown=isWin?(clear?'':{from,to,days:[...days].sort()}):room==='on';
  const ok=reason.trim().length>=3&&reason.trim().length<=120&&(!isWin||clear||(days.length>0&&from!==to));
  return <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'CFG_NET_SET',setting:key,value,reason:reason.trim(),key:`net:${key}:${s.sequence}`},isWin?'Internet window saved':`${def.label} switched ${room==='on'?'on':'off'}`,{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">OWNER ACTION</p><h2>{isWin?'Allowed time window':`${room==='on'?'Switch on':'Switch off'} · ${def.label}`}</h2>
      <p>{def.help} The change takes effect at once; held work is sent in order when a switch goes on, and waits when it goes off.</p></div>
    {isWin&&<><label className="check"><input type="checkbox" checked={clear} onChange={e=>setClear(e.target.checked)}/> No window (allowed at any time)</label>
      {!clear&&<><label>From (hotel time)<input type="time" value={from} onChange={e=>setFrom(e.target.value)} required/></label><label>To (hotel time, exclusive)<input type="time" value={to} onChange={e=>setTo(e.target.value)} required/></label>
        <fieldset className="net-days"><legend>Days</legend>{DAYS.map((d,i)=><label key={d} className="check"><input type="checkbox" checked={days.includes(i)} onChange={e=>setDays(e.target.checked?[...days,i]:days.filter(x=>x!==i))}/> {d}</label>)}</fieldset>
        <p className="muted">A window that ends before it starts crosses midnight (the late part belongs to the start day).</p></>}</>}
    <label>Reason (3–120 characters, required)<input value={reason} maxLength={120} required minLength={3} onChange={e=>setReason(e.target.value)} enterKeyHint="done" placeholder="e.g. Test OTA sync with the channel manager"/></label>
    {!edit&&<p className="f-why">Only the Owner changes the internet switches.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!edit||!ok}>Save</button></div></form>;
}

/** System health › Egress block: master, effective now (with reason), per-integration state, window, last change, held counts per queue, firewall rule. */
export function EgressBlock({phone}:{phone?:boolean}){
  const {s,uid,user}=useHcc();const st=netState(s,uid);const held=netHeldCounts(s);
  if(!netRoleSees(user.role))return null;
  const QN:Record<string,string>={messaging:'Messages','ota-push':'OTA push',email:'E-mail','scheduled-report':'Scheduled reports'};
  const rows:[string,React.ReactNode][]=[['Master switch',st.masterOn?'On':'Off'],
    ...NET_INTEGRATIONS.map(i=>[NET_SETTING_DEFS.find(d=>d.key===netKeyOf(i))?.label??i,`${st.integrations[i]?'On':'Off'} · ${st.decisions[i].allowed?'allowed now':NET_REASON_TEXT[st.decisions[i].reason]}`] as [string,React.ReactNode]),
    ['Time window',netWindowText(st.window)],
    ['Last change',st.changedAt?`${dateLabel(st.changedAt.slice(0,10))} ${clockTime(st.changedAt)}${st.changedBy?' · '+(s.users.find(u=>u.id===st.changedBy)?.name??st.changedBy):''} — ${st.reason}`:'None yet (all off)'],
    ['Windows firewall rule',process.env.HCC_BUILD==='production'?'Read from the server (applied / not applied / unknown)':'Not applicable in the demo']];
  return <section className="net-block" aria-label="Internet egress">
    <div className="adm-row-head"><h3 className="acc-h3"><Globe size={15} aria-hidden/> Internet (egress)</h3>
      <Pill tone={st.effectiveNow?'green':'orange'}>{st.effectiveNow?'Some services allowed now':'No internet call allowed now'}</Pill></div>
    {phone?<dl className="net-dl" aria-label="Egress state">{rows.map(([k,v])=><div key={k}><dt>{k}</dt><dd>{v}</dd></div>)}
      {held.map(h=><div key={h.queue}><dt>{QN[h.queue]??h.queue} (held / waiting)</dt><dd>{h.held} / {h.pending}</dd></div>)}</dl>
    :<><div className="table-wrap"><table className="adm-table" aria-label="Egress state"><tbody>{rows.map(([k,v])=><tr key={k}><th scope="row">{k}</th><td>{v}</td></tr>)}</tbody></table></div>
    <div className="table-wrap"><table className="adm-table" aria-label="Held queues"><thead><tr><th>Queue</th><th className="number">Held</th><th className="number">Waiting to send</th></tr></thead>
      <tbody>{held.map(h=><tr key={h.queue}><td>{QN[h.queue]??h.queue}</td><td className="number">{h.held}</td><td className="number">{h.pending}</td></tr>)}</tbody></table></div></>}
  </section>;
}

/** Held count of one queue (all queues when omitted). */
export const netHeldOf=(s:Parameters<typeof netHeldCounts>[0],q?:typeof NET_QUEUES[number])=>netHeldCounts(s).filter(h=>!q||h.queue===q).reduce((a,h)=>a+h.held,0);

/** Compact banner / badge for a page header: 'messaging' → "Internet off — messages queue · N held"; 'ota' → "OTA bookings are not being received — last sync hh:mm". */
export function NetBanner({kind}:{kind:'messaging'|'ota'}){
  const {s,user}=useHcc();
  if(kind==='messaging'){
    const on=netOn(s,'messaging'),n=netHeldOf(s,'messaging');
    if(on&&!n)return null;
    return <span className="net-banner" role="status">{!on&&<Pill tone="orange"><ShieldAlert size={12} aria-hidden/> Internet off — messages queue</Pill>}
      {n>0&&<Pill tone="blue" ><span aria-label={`${n} held message${n===1?'':'s'}`}>{n} held</span></Pill>}{on&&n>0&&<Pill tone="green"><CheckCircle2 size={12} aria-hidden/> sending in order</Pill>}</span>;
  }
  if(netOn(s,'ota'))return null;
  const last=chnSlice(s).channels.map(c=>c.lastSync).filter(Boolean).sort().at(-1);
  void user;
  return <span className="net-banner" role="status"><Pill tone="orange"><ShieldAlert size={12} aria-hidden/> OTA bookings are not being received — last sync {last?clockTime(last):'never'}</Pill></span>;
}

/** One-line banner text for a page sub-heading (desktop header, one line): '' when nothing to say, else ends with ' · '. */
export function netBannerText(s:Parameters<typeof netOn>[0],kind:'messaging'|'ota'):string{
  if(kind==='messaging'){const n=netHeldOf(s,'messaging');return (netOn(s,'messaging')?'':'Internet off — messages queue · ')+(n>0?`${n} held · `:'');}
  if(netOn(s,'ota'))return '';
  const last=chnSlice(s).channels.map(c=>c.lastSync).filter(Boolean).sort().at(-1);
  return `OTA bookings are not being received — last sync ${last?clockTime(last):'never'} · `;
}
