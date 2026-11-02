// F4-T3 qw-feedback + qw-alerts · guest feedback at check-out, 7-day unresolved follow-ups, stock reorder alerts for the digest.
// Slice `s.feedbackX` (FBKState in types.ts). Selectors `fbkSummary`, `fbkOverdue`, `qwReorderAlerts` are read by the owner digest (T2).
import type {State,FBKEntry,FBKFollowUp,FBKState} from '../types';
import {gsvSlice} from './services';
import {invAlerts,type INVAlert} from './inventory';

export const FBK_OVERDUE_DAYS=7;
/** Stars at or below this open a complaint automatically. */
export const FBK_LOW_STARS=2;
export const FBK_FEEDBACK_ROLES=['Reception','Manager','Owner','Cashier'] as const;
export const fbkSlice=(s:State):FBKState=>({entries:s.feedbackX?.entries??[],followUps:s.feedbackX?.followUps??[],...(s.feedbackX?.url?{url:s.feedbackX.url}:{})});
export function fbkEnsure(s:State):FBKState{const x=fbkSlice(s);s.feedbackX=x;return x;}
export const fbkEntryOf=(s:State,booking:string):FBKEntry|undefined=>fbkSlice(s).entries.find(e=>e.booking===booking);
/** Count and average stars of the feedback recorded on one business date (`avg` null when none). */
export function fbkSummary(s:State,date:string):{date:string;count:number;avg:number|null;low:number}{
  const e=fbkSlice(s).entries.filter(x=>x.at.slice(0,10)===date);
  return {date,count:e.length,avg:e.length?Math.round(e.reduce((a,x)=>a+x.stars,0)/e.length*10)/10:null,low:e.filter(x=>x.stars<=FBK_LOW_STARS).length};
}
export type FBKOverdueRow={source:'complaint'|'lostfound';ref:string;since:string;days:number;label:string};
const daysBetween=(from:string,to:string)=>Math.round((Date.parse(to+'T00:00:00Z')-Date.parse(from+'T00:00:00Z'))/86400000);
/** Complaints still Open / Investigating and lost-and-found items still Stored for 7 or more days on `date`. */
export function fbkOverdue(s:State,date:string):{complaints:number;lostFound:number;total:number;rows:FBKOverdueRow[]}{
  const g=gsvSlice(s),rows:FBKOverdueRow[]=[];
  for(const c of g.complaints)if(c.status==='Open'||c.status==='Investigating'){const d=daysBetween(c.createdAt.slice(0,10),date);if(d>=FBK_OVERDUE_DAYS)rows.push({source:'complaint',ref:c.id,since:c.createdAt.slice(0,10),days:d,label:`${c.category} · ${c.severity}`});}
  for(const i of g.items)if(i.status==='Stored'){const d=daysBetween(i.foundAt.slice(0,10),date);if(d>=FBK_OVERDUE_DAYS)rows.push({source:'lostfound',ref:i.id,since:i.foundAt.slice(0,10),days:d,label:i.category});}
  rows.sort((a,b)=>b.days-a.days||a.ref.localeCompare(b.ref));
  return {complaints:rows.filter(r=>r.source==='complaint').length,lostFound:rows.filter(r=>r.source==='lostfound').length,total:rows.length,rows};
}
/** Digest line, e.g. "3 unresolved for 7+ days (2 complaints, 1 lost-and-found item)". */
export const fbkOverdueLine=(o:ReturnType<typeof fbkOverdue>)=>o.total?`${o.total} unresolved for ${FBK_OVERDUE_DAYS}+ days (${o.complaints} complaint${o.complaints===1?'':'s'}, ${o.lostFound} lost-and-found item${o.lostFound===1?'':'s'})`:'Nothing unresolved for 7+ days';
export const fbkSummaryLine=(x:ReturnType<typeof fbkSummary>)=>x.count?`${x.count} feedback, average ${x.avg} of 5${x.low?` (${x.low} low)`:''}`:'No feedback recorded';
/** Text line printed on bills and invoices; empty (line hidden) until the owner sets the address (Q12). */
export const fbkBillLine=(s:State):string=>{const u=(fbkSlice(s).url||'').trim();return u?`Tell us how we did: ${u}`:'';};
export const fbkOpenFollowUps=(s:State):FBKFollowUp[]=>fbkSlice(s).followUps.filter(f=>f.status==='open');
export const fbkFollowKey=(source:string,ref:string)=>`${source}:${ref}`;

// ── qw-alerts ──
/** Items below their reorder level (the existing inventory selector); the digest line and the Stock banner read this. */
export const qwReorderAlerts=(s:State):INVAlert[]=>invAlerts(s);
export const qwReorderLine=(a:INVAlert[])=>a.length?`${a.length} item${a.length===1?'':'s'} below reorder level: ${a.slice(0,3).map(x=>x.item.name).join(', ')}${a.length>3?` and ${a.length-3} more`:''}`:'Stock is above reorder levels';
