// Stream E dialogs. Types: 'admin-user' (id = user ID, omitted = new user), 'admin-setting' (id = setting key),
// 'admin-tariff' (room = room type). The synthetic 'admin-tax' dialog was removed by DEC-069 (GST master rows use 'cfg-setting').
import React,{useState} from 'react';
import {Check,AlertTriangle,Info} from 'lucide-react';
import {roles,authorize,adminImpactPreview,adminSettingHistory,adminSettingValue,adminTariffOn,adminBaseRate,adminRoomTypes,ADMIN_SETTINGS} from '../../services/mock';
import type {AdminImpact,Role} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,field,submit,shortDate,clockTime} from '../../components/ui';
import {firstName} from './parts';

export function UserModal({id}:{id?:string}){
  const {s,uid,user,run,modal,closeModal,busy}=useHcc();
  const u=id?s.users.find(x=>x.id===id):undefined;
  if(id&&!u)return <Empty title="This user is unavailable"/>;
  const self=u?.id===uid;
  return <><div className="modal-heading"><div className="eyebrow">{u?u.id:'NEW USER'}</div><h2>{u?'Edit '+firstName(u.name):'Create a demo user'}</h2><p>Fictional staff only. One role per user; an expiry ends access after that business day.</p></div>
    <form className="form" onSubmit={e=>submit(e,f=>{
      const role=field(f,'role') as Role,expiresOn=field(f,'expiresOn');
      if(!u)run({type:'USER_CREATE',name:field(f,'name'),role,expiresOn:expiresOn||undefined,key:modal!.key},'User created',{close:true});
      else{const cmd:Record<string,unknown>={type:'USER_UPDATE',id:u.id,key:modal!.key};
        if(field(f,'name')!==firstName(u.name))cmd.name=field(f,'name');
        if(!self){if(role!==u.role)cmd.role=role;if((expiresOn||undefined)!==u.expiresOn)cmd.expiresOn=expiresOn;const en=f.get('enabled')==='on';if(en!==u.enabled)cmd.enabled=en;}
        run(cmd as {type:string},'User updated',{close:true});}
    })}>
      <label>Name<input name="name" required minLength={2} maxLength={40} defaultValue={u?firstName(u.name):''} placeholder="e.g. Rohan"/></label>
      <div className="form-row"><label>Role<select name="role" defaultValue={u?.role||'Reception'} disabled={self}>{roles.map(r=><option key={r} value={r} disabled={r==='Owner'&&user.role!=='Owner'}>{r}{r==='Owner'&&user.role!=='Owner'?' (Owner only)':''}</option>)}</select></label>
        <label>Access until (optional)<input type="date" name="expiresOn" min={s.date} defaultValue={u?.expiresOn||''} disabled={self}/></label></div>
      {u&&<label className="checkbox-line"><input type="checkbox" name="enabled" defaultChecked={u.enabled} disabled={self}/>Account enabled</label>}
      {self&&<div className="info-note"><Info size={16}/>This is your own account: you can rename it, but another administrator must change your role, status or expiry.</div>}
      {u?.role==='Owner'&&!self&&<div className="info-note"><Info size={16}/>The last active owner cannot be disabled, demoted or given an expiry (DEC-033).</div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>{u?'Save changes':'Create user'} <Check size={16}/></button></div>
    </form></>;
}

export function Impact({p}:{p:AdminImpact}){
  return <div className="adm-impact" aria-live="polite"><h3>Impact preview</h3>
    {p.warning&&<p className="adm-warn" role="alert"><AlertTriangle size={15}/> {p.warning}</p>}
    <ul>{p.notes.map((n,i)=><li key={i}>{n}</li>)}</ul>
    {p.stays.length>0&&<details><summary>{p.stays.length} record{p.stays.length>1?'s':''}</summary><ul>{p.stays.map(x=><li key={x.id}>{x.id}{x.guest&&' · '+x.guest}{x.room&&' · room '+x.room} — {x.note}</li>)}</ul></details>}
  </div>;
}
function HistoryList({k}:{k:string}){
  const {s}=useHcc();const h=adminSettingHistory(s,k);
  return <div className="adm-hist"><h3>History</h3>{h.length?<ul>{h.map(x=><li key={x.id}><strong>{x.before} → {x.after}</strong> · effective {shortDate(x.effectiveFrom)} · {firstName(x.actor)} at {shortDate(x.at.slice(0,10))} {clockTime(x.at)}{x.reason?' · '+x.reason:''}</li>)}</ul>:<p className="muted">No changes since the scenario was loaded.</p>}</div>;
}
const cfgBlock=(s:ReturnType<typeof useHcc>['s'],uid:string)=>{const d=authorize(s,uid,'PERM-ADM-003',{act:true});return d.decision==='allow'?undefined:d.decision==='approval'?'Your role may only request configuration changes — ask the Owner.':d.reason;};

export function SettingModal({id}:{id?:string}){
  const {s,uid,run,modal,closeModal,busy}=useHcc();
  const def=ADMIN_SETTINGS.find(d=>d.key===id&&d.editable&&d.key!=='taxBps');
  const st=s.settings as unknown as Record<string,unknown>;
  const initial=id==='approvalExpiryHours'?String(st.approvalExpiryHours??24):id==='discountLimit'?String(s.settings.discountLimit/100):id==='inspection'?(s.settings.inspection?'on':'off'):String(st[id||'']??'');
  const [v,setV]=useState(initial);
  if(!def)return <Empty title="This setting is not configurable"/>;
  const block=cfgBlock(s,uid);
  const value=def.type==='money'?Math.round(Number(v)*100):def.type==='integer'?Number(v):def.type==='boolean'?v==='on':v;
  const impact=def.key==='checkin'||def.key==='checkout'?adminImpactPreview(s,{kind:'times',checkin:def.key==='checkin'?String(value):s.settings.checkin,checkout:def.key==='checkout'?String(value):s.settings.checkout})
    :def.key==='discountLimit'?adminImpactPreview(s,{kind:'discount',limit:Number(value)||0}):def.key==='inspection'?adminImpactPreview(s,{kind:'inspection',on:value===true}):undefined;
  return <><div className="modal-heading"><div className="eyebrow">{def.category.toUpperCase()} · {def.key}</div><h2>{def.label}</h2><p>{def.help}</p></div>
    <form className="form" onSubmit={e=>submit(e,f=>run({type:'SETTING_SET',setting:def.key,value,reason:field(f,'reason'),key:modal!.key},'Setting saved · history recorded',{close:true}))}>
      <div className="summary-list"><div><span>Type · unit</span><strong>{def.type} · {def.unit}</strong></div><div><span>Current value</span><strong>{adminSettingValue(s,def.key)}</strong></div><div><span>Effective</span><strong>{def.effective}</strong></div><div><span>Approval need</span><strong>{def.approval}</strong></div></div>
      {def.type==='boolean'?<label>New value<select value={v} onChange={e=>setV(e.target.value)}><option value="on">On</option><option value="off">Off</option></select></label>
        :<label>New value{def.type==='money'?' (₹)':''}<input required value={v} onChange={e=>setV(e.target.value)} type={def.type==='time'?'time':def.type==='money'||def.type==='integer'?'number':'text'} min={def.type==='integer'?1:0} max={def.type==='integer'?168:undefined} step={def.type==='money'?0.01:1} maxLength={def.type==='text'?60:undefined}/></label>}
      <label>Reason (optional, kept in history)<input name="reason" maxLength={120}/></label>
      {impact&&<Impact p={impact}/>}
      <HistoryList k={def.key}/>
      {block&&<p className="adm-lock" role="note">{block}</p>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!!block}>Save setting <Check size={16}/></button></div>
    </form></>;
}

export function TariffModal({room}:{room?:string}){
  const {s,uid,run,modal,closeModal,busy}=useHcc();
  const types=adminRoomTypes(s);
  const [type,setType]=useState(room&&types.includes(room)?room:types[0]);
  const cur=adminTariffOn(s,type,s.date)?.rate??adminBaseRate(s,type);
  const [rate,setRate]=useState(String(cur/100));const [from,setFrom]=useState(s.date);
  const block=cfgBlock(s,uid);const paise=Math.round(Number(rate)*100);
  const p=adminImpactPreview(s,{kind:'tariff',type,rate:paise,effectiveFrom:from});
  return <><div className="modal-heading"><div className="eyebrow">ROOMS & TARIFFS</div><h2>Schedule a tariff change</h2><p>New bookings arriving on or after the effective date are priced at the new rate. Existing bookings keep their agreed rate.</p></div>
    <form className="form" onSubmit={e=>submit(e,f=>run({type:'TARIFF_SCHEDULE',roomType:type,rate:paise,effectiveFrom:from,reason:field(f,'reason'),key:modal!.key},'Tariff scheduled',{close:true}))}>
      <div className="form-row"><label>Room type<select value={type} onChange={e=>{setType(e.target.value);setRate(String((adminTariffOn(s,e.target.value,s.date)?.rate??adminBaseRate(s,e.target.value))/100));}}>{types.map(t=><option key={t}>{t}</option>)}</select></label>
        <label>Nightly rate before tax (₹)<input type="number" min="1" step="0.01" required value={rate} onChange={e=>setRate(e.target.value)}/></label></div>
      <label>Effective from (business date)<input type="date" min={s.date} required value={from} onChange={e=>setFrom(e.target.value)}/>{from<s.date&&<small className="adm-err">Effective date cannot be before the current business date.</small>}</label>
      <label>Reason (optional)<input name="reason" maxLength={120}/></label>
      <Impact p={p}/>
      <HistoryList k={'tariff:'+type}/>
      {block&&<p className="adm-lock" role="note">{block}</p>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!!block}>Schedule tariff <Check size={16}/></button></div>
    </form></>;
}


