// Stream E · small shared pieces for the admin screens.
import React from 'react';
import {Pill} from '../../components/ui';
import type {AuthDecision} from '../../services/mock';
export const firstName=(name:string)=>name.split(' · ')[0];
export function DecisionPill({d}:{d:AuthDecision['decision']}){
  return d==='allow'?<Pill tone="green">✓ Allowed</Pill>:d==='approval'?<Pill tone="orange">⚑ Approval required</Pill>:<Pill>✕ Denied</Pill>;
}
export function StatusPill({status}:{status:string}){
  const tone=['Enabled','Active','Approved','Executed','Posted','On'].includes(status)?'green':['Pending','Expired'].includes(status)?'orange':['Invalidated','Disabled','Rejected'].includes(status)?'neutral':'blue';
  return <Pill tone={tone}>{status}</Pill>;
}
/** A button that, when blocked, is disabled and shows why (visible text, not only a tooltip). */
export function GuardedButton({block,onClick,children,className='btn small-btn',busy}:{block?:string;onClick:()=>void;children:React.ReactNode;className?:string;busy?:boolean}){
  return <span className="adm-guard"><button className={className} disabled={!!block||busy} title={block} aria-describedby={undefined} onClick={onClick}>{children}</button>{block&&<small className="adm-why">{block}</small>}</span>;
}
export function Tabs({tabs,value,onChange,counts}:{tabs:readonly string[];value:string;onChange:(t:string)=>void;counts?:Record<string,number>}){
  return <div className="adm-tabs" role="tablist">{tabs.map(t=><button key={t} role="tab" aria-selected={t===value} className={t===value?'active':''} onClick={()=>onChange(t)}>{t}{counts&&counts[t]!==undefined&&<span>{counts[t]}</span>}</button>)}</div>;
}
