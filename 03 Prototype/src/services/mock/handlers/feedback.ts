// F4-T3 · qw-feedback + qw-alerts command handlers. Command types start with 'FBK_' / 'QW_'.
//  FBK_RECORD {booking,stars,comment?,category?} — Reception, Manager, Owner, Cashier at check-out; optional, one entry per stay; stars <= 2 or a
//    comment with a category opens a complaint in module 18 (existing complaint categories).
//  FBK_FOLLOWUP_SWEEP — system, at day close: every complaint / lost-and-found item unresolved for 7+ days gets one follow-up row and one
//    "Owner follow-up call" task assigned to the Owner (idempotent per source + ref).
//  FBK_URL_SET {url} — Owner; the feedback address printed on bills.  QW_PRINTER_TEST {id} / QW_PRINTER_SET {id,online} — printer test page.
import type {Handler} from '../commands';
import type {FBKEntry} from '../../../domain/types';
import {fbkEnsure,fbkEntryOf,fbkSweep,FBK_LOW_STARS,FBK_FEEDBACK_ROLES} from '../../../domain/streams/feedback';
import {gsvEnsure,gcatResolve} from '../../../domain/streams/services';
import {sysPrinters,sysTestPageText} from '../../../domain/streams/syshealth';

const txt=(v:unknown)=>String(v??'').trim();
const roleOf=(k:Parameters<Handler>[0])=>String((k.user as {role?:string}).role??'');
export const handlers:Record<string,Handler>={
  FBK_RECORD:k=>{
    const {s,cmd,assert,fail,id,uid}=k;
    const role=roleOf(k);if(!(FBK_FEEDBACK_ROLES as readonly string[]).includes(role))fail('FORBIDDEN','Only Reception, Manager, Owner and Cashier record guest feedback.');
    const b=s.bookings.find(x=>x.id===cmd.booking);assert(b,'Booking not found.','NOT_FOUND');
    assert(b!.status==='In house'||b!.status==='Departed','Feedback is recorded for a guest who is staying or has left.','INVALID_TRANSITION');
    const stars=Number(cmd.stars);assert(Number.isInteger(stars)&&stars>=1&&stars<=5,'Choose 1 to 5 stars.');
    const comment=txt(cmd.comment);assert(comment.length<=400,'Keep the comment under 400 characters.');
    const x=fbkEnsure(s);assert(!fbkEntryOf(s,b!.id),'Feedback for this stay is already recorded.','INVALID_TRANSITION');
    const e:FBKEntry={id:id('FBK'),booking:b!.id,stars:stars as FBKEntry['stars'],at:s.clock,by:uid};
    if(comment)e.comment=comment;
    const cat=txt(cmd.category)?gcatResolve(s,cmd.category):undefined;assert(!txt(cmd.category)||cat,'Choose a complaint category from the list.');
    if(stars<=FBK_LOW_STARS||(comment&&cat)){
      const c=cat??gcatResolve(s,'Other');assert(c,'No complaint category is available.');
      const cid=id('CMP');
      gsvEnsure(s).complaints.push({id:cid,booking:b!.id,room:b!.room,guest:b!.guest,category:c!.name,categoryCode:c!.code,severity:stars<=1?'High':stars===2?'Medium':'Low',
        description:(comment||`Guest rated the stay ${stars} of 5 at check-out.`).slice(0,400),owner:uid,status:'Open',createdAt:s.clock,by:uid,log:[{at:s.clock,by:uid,what:'Logged from guest feedback'}]});
      e.complaintId=cid;
    }
    x.entries.push(e);
    return `${e.id} · ${b!.id} · ${stars} of 5${e.complaintId?' · complaint '+e.complaintId:''}`;
  },
  FBK_FOLLOWUP_SWEEP:k=>{
    const {s,id,req}=k;req('close');
    const r=fbkSweep(s,id);
    return `Follow-up sweep · ${r.added} new · ${r.total} unresolved`;
  },
  FBK_FOLLOWUP_DONE:k=>{
    const {s,cmd,req,assert}=k;req('admin');const f=fbkEnsure(s).followUps.find(x=>x.id===cmd.id);assert(f,'Follow-up not found.','NOT_FOUND');
    assert(f!.status==='open','This follow-up is already done.','INVALID_TRANSITION');f!.status='done';return `${f!.id} · ${f!.source} ${f!.ref} · call done`;
  },
  FBK_URL_SET:k=>{
    const {s,cmd,req,assert}=k;req('admin');const u=txt(cmd.url);assert(u.length<=120,'Keep the feedback address under 120 characters.');
    assert(!/[<>"]/.test(u),'The feedback address cannot contain < > or quotes.');
    const x=fbkEnsure(s);if(u)x.url=u;else delete x.url;return u?`Feedback address set: ${u}`:'Feedback address cleared (bill line hidden)';
  },
  QW_PRINTER_TEST:k=>{
    const {s,cmd,req,assert}=k;req('admin');
    const list=sysPrinters(s),p=list.find(x=>x.id===cmd.id);assert(p,'Printer not found.','NOT_FOUND');
    s.sysHealthX={...(s.sysHealthX??{source:'demo',parked:[]}),printers:list.map(x=>x.id===p!.id?{...x,lastTestAt:s.clock,lastTestOk:x.online}:x)} as typeof s.sysHealthX;
    return p!.online?`Test page sent to ${p!.name}: ${sysTestPageText(s,p!).split('\n').length} lines`:`Test page for ${p!.name} failed: printer offline`;
  },
  QW_PRINTER_SET:k=>{
    const {s,cmd,req,assert}=k;req('admin');
    const list=sysPrinters(s),p=list.find(x=>x.id===cmd.id);assert(p,'Printer not found.','NOT_FOUND');
    s.sysHealthX={...(s.sysHealthX??{source:'demo',parked:[]}),printers:list.map(x=>x.id===p!.id?{...x,online:!!cmd.online}:x)} as typeof s.sysHealthX;
    return `${p!.name} marked ${cmd.online?'online':'offline'}`;
  },
};
