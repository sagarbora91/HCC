// LF-images B5 (DEC-201): one photo card per person in the room — the registered guest and each accompanying guest. The photo is optional (DEC-193); a person's full ID number is
// already captured in the guest form. Each picture is compressed on the device to about 150 KB before it is stored (G1). Only users who may see ID details see the picture.
import React,{useState} from 'react';
import {Camera,Trash2} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Guest,guestAccess} from '../../services/mock';
import {dobUrl,dobPhotoRef} from '../../domain/streams/docobjects';
import {compressDocPhoto} from '../../app/slipPhoto';

export function PartyPhotos({g}:{g:Guest}){
  const {s,uid,run,busy,toast}=useHcc();const a=guestAccess(s,uid);
  const [err,setErr]=useState('');
  const people=[{seq:0,name:g.name,label:'Registered guest'},...g.party.map((p,i)=>({seq:i+1,name:p.name,label:p.relation||p.ageBand}))];
  async function pick(seq:number,f?:File){
    if(!f)return;setErr('');
    try{const r=await compressDocPhoto(f);await run({type:'GUEST_PHOTO',id:g.id,seq,dataUrl:r.dataUrl,key:'gphoto:'+g.id+':'+seq+':'+Date.now()},'Photo saved');}
    catch(x){const m=x instanceof Error?x.message:'The photo could not be prepared.';setErr(m);toast(m,true);}
  }
  return <section className="gs-photos" aria-label="Photos of the guests in the room">
    <h3 className="gs-sub">Photos <small className="muted">(optional · compressed to about 150 KB)</small></h3>
    <ul className="gs-photo-list">{people.map(p=>{const url=a.idDetails?dobUrl(s,dobPhotoRef(g.id,p.seq)):undefined;
      return <li key={p.seq} className="gs-photo-card">
        {url?<img src={url} alt={'Photo of '+p.name} className="gs-photo-img"/>:<span className="gs-photo-none muted">{a.idDetails?'No photo':'Restricted'}</span>}
        <strong>{p.name}</strong><small>{p.label}</small>
        {a.edit&&<span className="gs-photo-actions">
          <label className="btn small-btn"><Camera size={14}/> {url?'Replace':'Add photo'}<input type="file" accept="image/*" capture="user" className="cmp-sr" disabled={busy} onChange={e=>{const f=e.target.files?.[0];e.target.value='';void pick(p.seq,f);}}/></label>
          {url&&<button type="button" className="btn small-btn" disabled={busy} aria-label={'Remove photo of '+p.name} onClick={()=>{void run({type:'GUEST_PHOTO_REMOVE',id:g.id,seq:p.seq,key:'gphoto-rm:'+g.id+':'+p.seq+':'+Date.now()},'Photo removed');}}><Trash2 size={14}/></button>}</span>}
      </li>;})}</ul>
    {err&&<p className="adm-err" role="alert">{err}</p>}
  </section>;
}
