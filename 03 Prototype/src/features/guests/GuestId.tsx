// Guest ID display (DEC-068): always masked (last 4); the full number appears only after a deliberate, audited "Show ID" click
// by a role with the ID-view permission. The audit entry records who/when — never the number.
import React,{useState,useEffect} from 'react';
import {Eye,EyeOff} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Guest,guestView,guestRevealId,guestIdFormat} from '../../services/mock';

export function GuestIdValue({g}:{g:Guest}){
  const {s,uid,run,busy}=useHcc();const v=guestView(s,uid,g);
  const [shown,setShown]=useState(false);
  useEffect(()=>{setShown(false);},[g.id,uid]);
  if(v.masked)return <>Restricted</>;
  const full=shown?guestRevealId(s,uid,g):undefined;
  const head=`${v.idType} · `;
  return <><span className="gs-idval">{head}{full?guestIdFormat(g.idType,full):(v.idMasked||'—')}</span>
    {v.idOnFile?<button type="button" className="text-btn gs-idshow" disabled={busy} aria-pressed={shown}
        onClick={()=>{if(shown){setShown(false);return;}void run({type:'GUEST_ID_VIEW',id:g.id,key:'idview:'+g.id+':'+Date.now()},'ID shown — this view is logged').then(ok=>{if(ok)setShown(true);});}}>
      {shown?<><EyeOff size={13}/> Hide ID</>:<><Eye size={13}/> Show ID</>}</button>
      :<small className="muted gs-idnote"> · full number not on file</small>}</>;
}
