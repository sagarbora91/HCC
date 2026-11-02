// F4-T3 qw-feedback · guest feedback sheet at check-out: 1-5 stars + comment, optional, never blocks check-out. Stars <= 2 or a comment with
// a category opens a complaint (module 18). Tablet-friendly: 48 px star buttons.
import React,{useState} from 'react';
import {GSV_COMPLAINT_CATEGORIES,fbkEntryOf,fbkBillLine} from '../../services/mock';
import {useHcc} from '../../app/context';

const ROLES=['Reception','Manager','Owner','Cashier'];
export function FeedbackSheet({booking}:{booking:string}){
  const {s,user,run,busy}=useHcc();
  const [stars,setStars]=useState(0),[comment,setComment]=useState(''),[category,setCategory]=useState('');
  if(!ROLES.includes(user.role))return null;
  const done=fbkEntryOf(s,booking);
  if(done)return <div className="fbk-sheet" role="status">Guest feedback recorded: {done.stars} of 5{done.complaintId?` · complaint ${done.complaintId} opened`:''}.</div>;
  return <div className="fbk-sheet"><strong>Guest feedback (optional)</strong>
    <div className="fbk-stars" role="group" aria-label="Stars">{[1,2,3,4,5].map(n=><button key={n} type="button" aria-pressed={stars===n} aria-label={`${n} star${n===1?'':'s'}`} onClick={()=>setStars(n)}>{n}★</button>)}</div>
    <label>Comment<textarea value={comment} maxLength={400} onChange={e=>setComment(e.target.value)} rows={2}/></label>
    <label>Complaint category (only if the comment is a complaint)<select value={category} onChange={e=>setCategory(e.target.value)}><option value="">None</option>{GSV_COMPLAINT_CATEGORIES.map(c=><option key={c}>{c}</option>)}</select></label>
    <button className="btn" type="button" disabled={busy||!stars} onClick={()=>run({type:'FBK_RECORD',booking,stars,comment,category,key:'fbk:'+booking},'Feedback recorded.')}>Save feedback</button>
    <small className="muted"> Skipping this never blocks check-out.</small>
    {fbkBillLine(s)&&<p className="footnote">{fbkBillLine(s)}</p>}
  </div>;
}
export function FeedbackBillLine(){
  const {s}=useHcc();const t=fbkBillLine(s);
  return t?<p className="footnote fbk-bill-line">{t}</p>:null;
}
