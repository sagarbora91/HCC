// W0-C · stay-restriction notice (DEC-053): lists the min-stay / closed-to-arrival rules a stay breaks; approvers (Owner, Manager)
// can tick "Override restriction" and give a reason (≥ 3 characters). State comes from rstState (restriction.ts).
import React from 'react';
import {TriangleAlert,ShieldCheck} from 'lucide-react';
import type {Booking} from '../../services/mock';
import {RST_MIN_REASON,RstState,rstOverrideNotes} from './restriction';

export function RestrictionNotice({st,on,reason,onToggle,onReason,subject='this stay'}:{st:RstState;on:boolean;reason:string;
  onToggle:(v:boolean)=>void;onReason:(v:string)=>void;subject?:string}){
  if(!st.violations.length)return null;
  const short=on&&reason.trim().length<RST_MIN_REASON;
  return <div className="rst-notice" role="alert">
    <div className="rst-head"><TriangleAlert size={18} aria-hidden/><span className="rst-label">Restricted</span><strong>Stay restriction applies to {subject}</strong></div>
    <ul className="rst-list">{st.violations.map(v=><li key={v.code+v.restriction+v.message}><span className="rst-code">{v.code==='CTA'?'Closed to arrival':'Minimum stay'}</span>{v.message}</li>)}</ul>
    {st.canOverride?<div className="rst-override">
      <label className="rst-check"><input type="checkbox" checked={on} onChange={e=>onToggle(e.target.checked)}/>Override restriction (recorded on the booking and in the audit log)</label>
      {on&&<label className="rst-reason">Override reason *<input value={reason} maxLength={200} placeholder="e.g. Regular corporate guest" enterKeyHint="done" aria-invalid={short} onChange={e=>onReason(e.target.value)}/>
        {short&&<small className="rst-err">Give a reason of at least {RST_MIN_REASON} characters.</small>}</label>}
    </div>:<p className="rst-ask">Ask a manager to override, or choose other dates.</p>}
  </div>;
}

/** Read-only override note for the booking details (desktop dialog and phone sheet, via the bookingActions slot). */
export function RestrictionOverrideNote({b}:{b:Booking}){
  const notes=rstOverrideNotes(b);if(!notes.length)return null;
  return <div className="rst-booked">{notes.map((n,i)=><p key={i}><ShieldCheck size={15} aria-hidden/><span><strong>Restriction overridden</strong> ({n.rules}): {n.reason}</span></p>)}</div>;
}
