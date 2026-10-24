// Shared guest profile form (SCR-05): labelled fields, inline validation, duplicate suggestion, party members.
import React,{useState} from 'react';
import {AlertTriangle,Plus,Trash2,Info} from 'lucide-react';
import {useHcc} from '../../app/context';
import {GuestDraft,Guest,guestValidate,guestDuplicates,guestIdTypes,guestAgeBands,guestView} from '../../services/mock';

export const emptyDraft=(over:GuestDraft={}):GuestDraft=>({name:'',phone:'',email:'',address:'',nationality:'Indian',idType:'',idLast4:'',company:'',gstin:'',preferences:'',notes:'',party:[],...over});
export const draftOf=(g:Guest):GuestDraft=>emptyDraft({name:g.name,phone:g.phone,email:g.email,address:g.address,nationality:g.nationality,idType:g.idType,idLast4:g.idLast4,company:g.company||'',gstin:g.gstin||'',preferences:g.preferences,notes:g.notes,party:g.party.map(p=>({...p}))});
/** Small stable hash for idempotency keys derived from form content. */
export const hashOf=(v:unknown)=>{const t=JSON.stringify(v);let h=0;for(let i=0;i<t.length;i++)h=(h*31+t.charCodeAt(i))|0;return (h>>>0).toString(36);};

type Props={initial:GuestDraft;guestId?:string;registration:boolean;submitLabel:string;onSubmit:(d:GuestDraft)=>void;
  onUseExisting?:(g:Guest)=>void;secondary?:React.ReactNode;disabled?:boolean;lockedFields?:string[]};
export function GuestForm({initial,guestId,registration,submitLabel,onSubmit,onUseExisting,secondary,disabled,lockedFields=[]}:Props){
  const {s,uid,busy,open}=useHcc();
  const [d,setD]=useState<GuestDraft>(initial);const [tried,setTried]=useState(false);
  const errors=guestValidate(d,registration),shown=tried?errors:{};
  const dups=guestDuplicates(s,{id:guestId,name:d.name,phone:d.phone});
  const set=(k:keyof GuestDraft)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement|HTMLTextAreaElement>)=>setD({...d,[k]:e.target.value});
  const party=d.party||[];
  const setParty=(i:number,k:string,v:string)=>setD({...d,party:party.map((p,j)=>j===i?{...p,[k]:v}:p)});
  const f=(k:keyof GuestDraft,label:string,props:React.InputHTMLAttributes<HTMLInputElement>={},req=false)=>{const err=shown[k as string];const fid='gf-'+String(k);
    return <label><span>{label}{req&&<span className="gs-req" aria-hidden> *</span>}</span><input name={String(k)} value={String(d[k]??'')} onChange={set(k)} aria-invalid={!!err} aria-describedby={err?fid+'-e':undefined} disabled={disabled||lockedFields.includes(String(k))} {...props}/>{err&&<span className="gs-field-error" id={fid+'-e'}>{err}</span>}</label>;};
  const errCount=Object.keys(errors).length;
  return <form className="form" noValidate onSubmit={e=>{e.preventDefault();setTried(true);if(!errCount)onSubmit(d);}}>
    {dups.length>0&&<div className="gs-dup" role="status"><AlertTriangle size={17}/><div><strong>Possible match{dups.length>1?'es':''}</strong>
      {dups.map(m=>{const v=guestView(s,uid,m.guest);return <div key={m.guest.id} className="gs-dup-row"><span>{m.guest.id} · {v.name} · {m.reason==='phone'?'same phone':'same name'} ({v.phone})</span>
        <span className="inline">{onUseExisting&&<button type="button" className="btn small-btn" disabled={busy||disabled} onClick={()=>onUseExisting(m.guest)}>Use this profile</button>}<button type="button" className="text-btn" onClick={()=>open({type:'guest-profile',id:m.guest.id})}>View</button></span></div>;})}
      <small>This is a suggestion only. Saving creates a separate profile unless you choose an existing one.</small></div></div>}
    {f('name','Guest name',{maxLength:80,autoComplete:'off',placeholder:'e.g. Demo Guest'},true)}
    <div className="form-row">{f('phone','Phone (fictional)',{type:'tel',maxLength:20,placeholder:'+91 98200 0xxxx'},true)}{f('email','Email (optional)',{type:'email',maxLength:80,placeholder:'guest@example.test'})}</div>
    {f('address','Address',{maxLength:160,placeholder:'Street, city (fictional)'},registration)}
    <div className="form-row">
      <label><span>ID type{registration&&<span className="gs-req" aria-hidden> *</span>}</span><select name="idType" value={d.idType} onChange={set('idType')} aria-invalid={!!shown.idType} disabled={disabled}><option value="">Choose…</option>{guestIdTypes.map(x=><option key={x}>{x}</option>)}{d.idType&&!guestIdTypes.includes(d.idType)&&<option>{d.idType}</option>}</select>{shown.idType&&<span className="gs-field-error">{shown.idType}</span>}</label>
      {f('idLast4','ID — last 4 characters only',{maxLength:4,placeholder:'e.g. 4K21',autoComplete:'off'},registration)}
    </div>
    <div className="form-row">{f('nationality','Nationality',{maxLength:40})}{f('company','Company (optional)',{maxLength:80})}</div>
    {f('gstin','Company GSTIN (optional, fictional format)',{maxLength:15,placeholder:'27ABCDE1234F1Z5'})}
    <div className="form-row">{f('preferences','Preferences',{maxLength:160})}{f('notes','Notes',{maxLength:200})}</div>
    <fieldset className="gs-party"><legend>Accompanying guests ({party.length})</legend>
      {party.map((p,i)=><div className="gs-party-row" key={i}>
        <label>Name<input value={p.name} maxLength={60} onChange={e=>setParty(i,'name',e.target.value)} disabled={disabled}/></label>
        <label>Age band<select value={p.ageBand} onChange={e=>setParty(i,'ageBand',e.target.value)} disabled={disabled}>{guestAgeBands.map(x=><option key={x}>{x}</option>)}</select></label>
        <label>Relation<input value={p.relation} maxLength={30} onChange={e=>setParty(i,'relation',e.target.value)} disabled={disabled}/></label>
        <button type="button" className="icon-btn" aria-label={`Remove party member ${i+1}`} disabled={disabled} onClick={()=>setD({...d,party:party.filter((_,j)=>j!==i)})}><Trash2 size={16}/></button></div>)}
      {shown.party&&<span className="gs-field-error">{shown.party}</span>}
      <button type="button" className="text-btn" disabled={disabled||party.length>=6} onClick={()=>setD({...d,party:[...party,{name:'',ageBand:'Adult',relation:''}]})}><Plus size={14}/>Add accompanying guest</button>
    </fieldset>
    <div className="info-note"><Info size={16}/>Fictional data only. Record the ID type and the last 4 characters — never a full ID number or an image.</div>
    {tried&&errCount>0&&<div className="gs-form-summary" role="alert">{errCount} field{errCount>1?'s need':' needs'} attention before saving.</div>}
    <div className="modal-actions">{secondary}<button className="btn primary" disabled={busy||disabled}>{submitLabel}</button></div>
  </form>;
}
