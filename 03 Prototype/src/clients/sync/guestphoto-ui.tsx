// Production party photos (see guestphoto.ts). Imported only from `process.env.HCC_BUILD==='production'` branches. A photo is shown only after a deliberate
// click (an audited server read, like Show ID) and lives in this component's state until the screen closes; nothing is written to the runtime state or the device.
import React,{useCallback,useEffect,useState} from 'react';
import {Camera,Eye,EyeOff} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Guest,guestAccess} from '../../services/mock';
import {compressDocPhoto} from '../../app/slipPhoto';
import {getSyncRuntime} from './instance';
import {listPartyPhotos,viewPartyPhoto} from './guestphoto';

/** Which people (seq 0 = registered guest) have a photo on the server. Reloads after `tick` changes. */
export function usePhotoSeqs(guestId:string,enabled:boolean,tick=0):number[]|undefined{
  const [seqs,setSeqs]=useState<number[]|undefined>();
  useEffect(()=>{
    if(!enabled){setSeqs(undefined);return;}
    let live=true;
    listPartyPhotos(getSyncRuntime().transport,guestId).then(m=>{if(live)setSeqs([...m.keys()]);},()=>{if(live)setSeqs(undefined);});
    return ()=>{live=false;};
  },[guestId,enabled,tick]);
  return seqs;
}

export function PartyPhotosServer({g}:{g:Guest}){
  const {s,uid,run,busy,toast}=useHcc();const a=guestAccess(s,uid);
  const [tick,setTick]=useState(0);const [err,setErr]=useState('');
  const [shown,setShown]=useState<Record<number,string>>({});
  const seqs=usePhotoSeqs(g.id,a.idDetails,tick);
  useEffect(()=>{setShown({});},[g.id,uid]);
  useEffect(()=>{const f=()=>{if(document.visibilityState==='hidden')setShown({});};document.addEventListener('visibilitychange',f);return ()=>document.removeEventListener('visibilitychange',f);},[]);
  const people=[{seq:0,name:g.name,label:'Registered guest'},...g.party.map((p,i)=>({seq:i+1,name:p.name,label:p.relation||p.ageBand}))];
  const show=useCallback(async(seq:number)=>{
    setErr('');
    try{const m=await listPartyPhotos(getSyncRuntime().transport,g.id);const c=m.get(seq);if(!c)throw new Error('There is no photo on file.');
      const url=await viewPartyPhoto(getSyncRuntime().transport,c.captureId);setShown(x=>({...x,[seq]:url}));}
    catch(x){const m=x instanceof Error?x.message:'The photo could not be opened.';setErr(m);toast(m,true);}
  },[g.id,toast]);
  async function pick(seq:number,f?:File){
    if(!f)return;setErr('');
    try{const r=await compressDocPhoto(f);
      if(await run({type:'GUEST_PHOTO',id:g.id,seq,dataUrl:r.dataUrl,key:'gphoto:'+g.id+':'+seq+':'+Date.now()},'Photo saved')){setShown(x=>{const n={...x};delete n[seq];return n;});setTick(t=>t+1);}}
    catch(x){const m=x instanceof Error?x.message:'The photo could not be prepared.';setErr(m);toast(m,true);}
  }
  return <section className="gs-photos" aria-label="Photos of the guests in the room">
    <h3 className="gs-sub">Photos <small className="muted">(optional · compressed to about 150 KB · opening one is logged)</small></h3>
    <ul className="gs-photo-list">{people.map(p=>{const url=shown[p.seq];const has=!!seqs?.includes(p.seq);
      return <li key={p.seq} className="gs-photo-card">
        {url?<img src={url} alt={'Photo of '+p.name} className="gs-photo-img"/>:<span className="gs-photo-none muted">{!a.idDetails?'Restricted':has?'On file':'No photo'}</span>}
        <strong>{p.name}</strong><small>{p.label}</small>
        <span className="gs-photo-actions">
          {a.idDetails&&has&&(url?<button type="button" className="btn" style={{minHeight:48}} onClick={()=>setShown(x=>{const n={...x};delete n[p.seq];return n;})}><EyeOff size={14}/> Hide</button>
            :<button type="button" className="btn" style={{minHeight:48}} disabled={busy} onClick={()=>{void show(p.seq);}}><Eye size={14}/> Show photo</button>)}
          {a.edit&&<label className="btn" style={{minHeight:48}}><Camera size={14}/> {has?'Replace':'Add photo'}<input type="file" accept="image/*" capture="user" className="cmp-sr" disabled={busy} onChange={e=>{const f=e.target.files?.[0];e.target.value='';void pick(p.seq,f);}}/></label>}</span>
      </li>;})}</ul>
    {err&&<p className="adm-err" role="alert">{err}</p>}
  </section>;
}
