// F8C phone · "Who pays" card of the check-in review step: payer for the room folio and for the extras folio (guest / company / Udhaar).
// Uses the existing SF commands through mfRoutingPlan (routing.ts). Needs the billing permission (DEC-061: company credit left is money).
import React,{useState,useRef} from 'react';
import {Split as SplitIcon,Info,AlertCircle,Check} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,grpActiveCompanies,grpCredit,sfFolios,sfIsSplit,SF_ROUTE_CATEGORIES} from '../../services/mock';
import type {Booking} from '../../services/mock';
import {mfCurrentRouting,mfIsGroupMember,mfRoutingPlan,mfRoutingProblem,MF_CATEGORY_LABEL,MF_GROUP_MESSAGE,type MfRouting} from './routing';

export function MfBillRouting({b}:{b:Booking}){
  const c=useHcc();const live=useRef(c);live.current=c;const {s,permission,busy,modal}=c;
  const [editing,setEditing]=useState(false);const [want,setWant]=useState<MfRouting|null>(null);
  const [msg,setMsg]=useState<{ok:boolean;text:string}|null>(null);const [saving,setSaving]=useState(false);
  if(!permission('billing')||!['Confirmed','In house'].includes(b.status))return null;
  const have=mfCurrentRouting(b),cur=want??have;
  const companies=grpActiveCompanies(s),udhaar=(s.guests??[]).filter(g=>g.creditAccount?.active);
  const label=(k:string)=>{
    if(k==='guest')return b.guest;
    const [kind,...r]=k.split(':'),ref=r.join(':');
    if(kind==='company')return sfFolios(b).find(f=>f.payer.ref===ref)?.payer.name??companies.find(x=>x.id===ref)?.name??'Company';
    return sfFolios(b).find(f=>f.payer.ref===ref)?.payer.name??udhaar.find(g=>g.id===ref)?.name??'Udhaar guest';
  };
  const header=<div className="mf-route-head"><SplitIcon size={18}/><strong>Who pays</strong></div>;
  if(mfIsGroupMember(s,b))return <div className="mf-route">{header}<div className="info-note"><Info size={16}/>{MF_GROUP_MESSAGE}</div></div>;
  const problem=mfRoutingProblem(b,cur),cmds=mfRoutingPlan(b,cur,`${modal?.key??'mf-route'}:${b.version}`),dirty=cmds.length>0;
  async function save(){
    setSaving(true);setMsg(null);
    for(const cmd of cmds){
      const ok=await c.run(cmd,'Bill routing saved');
      if(!ok){await new Promise(r=>setTimeout(r,40));setMsg({ok:false,text:(live.current.error||'The routing was not saved.')+' Nothing after this step was changed; check the choices below and save again.'});setSaving(false);setWant(null);return;}
    }
    setSaving(false);setWant(null);setEditing(false);setMsg({ok:true,text:'Bill routing saved. Later postings follow it; nothing already posted moves.'});
  }
  const opts=()=>[<option key="g" value="guest">{b.guest} (guest pays)</option>,
    ...companies.map(x=><option key={x.id} value={'company:'+x.id}>{x.name}</option>),
    ...udhaar.map(g=><option key={g.id} value={'udhaar:'+g.id}>{g.name} · Udhaar</option>)];
  return <div className="mf-route">{header}
    {!editing&&<>
      <div className="mf-route-rows">{SF_ROUTE_CATEGORIES.map(k=><div key={k} className="mf-route-row"><span>{k==='room'?'Room':'Extras'}</span><strong>{label(have[k])}</strong></div>)}</div>
      {!sfIsSplit(b)&&<p className="mf-note">One folio: the guest pays everything. Send the room or the extras to a company or an Udhaar account if someone else pays.</p>}
      <button type="button" className="btn mf-wide" onClick={()=>{setEditing(true);setMsg(null);}}>{sfIsSplit(b)?'Change who pays':'Split the bill…'}</button></>}
    {editing&&<div className="form mf-form">
      {SF_ROUTE_CATEGORIES.map(k=><label key={k}>{MF_CATEGORY_LABEL[k]}<select value={cur[k]} onChange={e=>setWant({...cur,[k]:e.target.value})}>{opts()}</select>{cur[k].startsWith('company:')&&(()=>{const co=companies.find(x=>'company:'+x.id===cur[k]);return co?<small className="mf-route-hint">Credit left {money(grpCredit(s,co).available)}</small>:null;})()}</label>)}
      <div className="info-note"><Info size={16}/>A company folio is bound by the company’s credit limit and is not paid in cash. Folio A stays with the guest. Later postings follow the routing; what is already posted stays where it is.</div>
      {problem&&<div className="mf-deny" role="alert"><AlertCircle size={18}/><div>{problem}</div></div>}
      <div className="mf-inline"><button type="button" className="btn" disabled={saving} onClick={()=>{setEditing(false);setWant(null);}}>Cancel</button>
        <button type="button" className="btn primary" disabled={busy||saving||!dirty||!!problem} onClick={save}><Check size={16}/>Save routing</button></div></div>}
    {msg&&<div className={msg.ok?'mf-ok':'mf-deny'} role="status">{msg.ok?<Check size={18}/>:<AlertCircle size={18}/>}<div>{msg.text}</div></div>}
  </div>;
}
