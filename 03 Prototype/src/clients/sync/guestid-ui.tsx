// Production "Show ID" button + transient dialog (see guestid.ts). Imported only from `process.env.HCC_BUILD==='production'` branches.
import React,{useEffect,useMemo,useState,useSyncExternalStore} from 'react';
import {Eye,EyeOff,X} from 'lucide-react';
import {getSyncRuntime} from './instance';
import {canRevealGuestId,createReveal,revealGuestId,REVEAL_MS,type RevealView} from './guestid';

/** Current user's right to reveal (PERM-GST-003 from /me/capabilities). */
export function useCanRevealGuestId():boolean{
  const rt=getSyncRuntime();
  const snap=useSyncExternalStore(rt.subscribe,rt.get,rt.get);
  return canRevealGuestId(snap.me);
}

export function GuestIdRevealButton({guestId,uid}:{guestId:string;uid:string}){
  const [view,setView]=useState<RevealView>({phase:'idle'});
  const [left,setLeft]=useState(REVEAL_MS/1000);
  const ctl=useMemo(()=>createReveal({reveal:()=>revealGuestId(getSyncRuntime().transport,guestId),onChange:setView}),[guestId,uid]);
  useEffect(()=>()=>ctl.hide(),[ctl]); // another guest / user / leaving the screen: drop the number
  useEffect(()=>{ // the app goes to the background: hide
    const f=()=>{if(document.visibilityState==='hidden')ctl.hide();};
    document.addEventListener('visibilitychange',f);return ()=>document.removeEventListener('visibilitychange',f);
  },[ctl]);
  const shown=view.phase==='shown';
  useEffect(()=>{
    if(!shown)return;setLeft(REVEAL_MS/1000);
    const t=setInterval(()=>setLeft(n=>Math.max(0,n-1)),1000);return ()=>clearInterval(t);
  },[shown]);
  const loading=view.phase==='loading';
  return <>
    <button type="button" className="text-btn gs-idshow" disabled={loading} onClick={()=>{void ctl.show();}}><Eye size={13}/> {loading?'Checking…':'Show ID'}</button>
    {view.phase==='error'&&<small className="form-error" role="alert"> {view.text}</small>}
    {view.phase==='shown'&&<div className="modal-overlay" onMouseDown={e=>{if(e.target===e.currentTarget)ctl.hide();}}>
      <div className="modal" role="dialog" aria-modal="true" aria-label="Guest ID number" tabIndex={-1} ref={el=>el?.focus()}
        onKeyDown={e=>{if(e.key==='Escape'){e.preventDefault();ctl.hide();}}}>
        <button type="button" className="modal-close icon-btn" aria-label="Hide ID" onClick={ctl.hide}><X size={21}/></button>
        <h2>{view.value.idType||'ID number'}</h2>
        <p className="gs-idfull" style={{fontSize:'1.5rem',letterSpacing:'.05em',fontVariantNumeric:'tabular-nums'}}>{view.value.formatted}</p>
        <p className="muted">This view was logged. It hides itself in {left} s and is not stored on this device.</p>
        <div className="modal-actions"><button type="button" className="btn primary" onClick={ctl.hide}><EyeOff size={14}/> Hide ID</button></div>
      </div></div>}
  </>;
}
