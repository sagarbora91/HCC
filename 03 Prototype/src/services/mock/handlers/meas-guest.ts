// F12 MEAS · guest captures (MEAS-CONTRACT §2, lane meas-cap-guest). Command types start with 'MEAS_'. Return value = audit text.
//   MEAS_SURVEY_ADD {via:'qr'|'desk', overall,cleanliness,staff,value,breakfast?,recommend, comment?, consent, booking?}
//   MEAS_REVIEW_SAVE {month,platform,count,avgScore,score12m?,responded,themes?}      (Manager, Owner)
//   MEAS_ROOTCAUSE_LIST_SAVE {rows:[{code,label,active}]}   MEAS_ROOTCAUSE_SET {complaint,code}   (Manager, Owner)
//   MEAS_LOYALTY_SET {customer,tag}   (Reception, Manager, Owner)
// Every command first checks measOn (item off -> MEAS_OFF, data kept), is idempotent on its client id (cmd.key / cmd.id) and audited.
import type {Handler,Kit} from '../commands';
import type {MeasSurvey,MeasReviewMonth} from '../../../domain/streams/measure-types';
import {
  GSV_MEAS_KEYS,GSV_MEAS_DESK_ROLES,GSV_MEAS_MANAGER_ROLES,GSV_SURVEY_COMMENT_MAX,GSV_LOYALTY_TAG_MAX,
  gsvMeasGuard,gsvMeasFail,gsvMeasRoleOk,gsvSurveyChannelOn,gsvSurveyProblem,gsvSurveyRateLimited,gsvReviewProblem,gsvReviewId,
  gsvRootCauses,gsvRootCauseListProblem,gsvSlice,
} from '../../../domain/streams/services';
import {guestLooksLikeIdNumber} from '../../../domain/streams/guests';

const txt=(v:unknown)=>typeof v==='string'?v.trim():'';
const slice=(k:Kit)=>k.s.measX??(k.s.measX={});
const forbid=(k:Kit,ok:boolean,what:string)=>{if(!ok)k.fail('FORBIDDEN',`Access denied: ${what}.`);};

const MEAS_SURVEY_ADD:Handler=k=>{
  const c=k.cmd as Record<string,unknown>;
  gsvMeasGuard(k.s,GSV_MEAS_KEYS.survey);
  const via=c.via as 'qr'|'desk'|'link';
  if(via==='desk')forbid(k,gsvMeasRoleOk(k.s,k.uid,GSV_MEAS_DESK_ROLES),'a desk survey needs PERM-MEAS-002 (Owner, Manager or Reception)');
  if(!gsvSurveyChannelOn(k.s,via))gsvMeasFail('MEAS_OFF','The QR survey page belongs to the Direct booking site, which is switched off.');
  const bad=gsvSurveyProblem(c);if(bad)gsvMeasFail(bad.reason,bad.message);
  const m=slice(k);const list=m.surveys??[];
  const cid=txt(c.id)||txt(c.key);
  if(cid&&list.some(x=>x.id===cid))return `${cid} · survey already saved`;
  if(via==='qr'&&gsvSurveyRateLimited(list,k.s.clock))gsvMeasFail('MEAS_RATE_LIMIT','Too many answers from the survey page just now. Please try again in a few minutes.','RESTRICTED');
  let roomType:string|undefined,channel:string|undefined,booking:string|undefined;
  if(via!=='qr'&&txt(c.booking)){
    const b=k.s.bookings.find(x=>x.id===txt(c.booking));k.assert(b,'Booking not found.','NOT_FOUND');
    booking=b!.id;channel=b!.source;roomType=k.s.rooms.find(r=>r.id===b!.room)?.type;
  }
  const rec:MeasSurvey={id:cid||k.id('SRV'),at:k.s.clock,...(booking?{booking}:{}),...(roomType?{roomType}:{}),...(channel?{channel}:{}),
    overall:c.overall as number,cleanliness:c.cleanliness as number,staff:c.staff as number,value:c.value as number,
    ...(c.breakfast!==undefined?{breakfast:c.breakfast as number}:{}),recommend:c.recommend as number,
    ...(txt(c.comment)?{comment:txt(c.comment).slice(0,GSV_SURVEY_COMMENT_MAX)}:{}),consent:true,via,
    ...(via==='desk'?{enteredBy:k.uid}:{})};
  m.surveys=[...list,rec];
  return `${rec.id} · survey via ${via} · overall ${rec.overall}/5 · recommend ${rec.recommend}/10`;
};

const MEAS_REVIEW_SAVE:Handler=k=>{
  const c=k.cmd as Record<string,unknown>;
  gsvMeasGuard(k.s,GSV_MEAS_KEYS.reputation);
  forbid(k,gsvMeasRoleOk(k.s,k.uid,GSV_MEAS_MANAGER_ROLES),'review figures are saved by the Manager or Owner');
  const bad=gsvReviewProblem(c);if(bad)k.fail('VALIDATION',bad);
  const m=slice(k);const list=m.reviews??[];
  const id=gsvReviewId(c.month as string,c.platform as string);
  const rec:MeasReviewMonth={id,month:c.month as string,platform:c.platform as MeasReviewMonth['platform'],count:c.count as number,avgScore:c.avgScore as number,
    ...(c.score12m!==undefined?{score12m:c.score12m as number}:{}),responded:c.responded as number,...(txt(c.themes)?{themes:txt(c.themes)}:{})};
  const at=list.findIndex(x=>x.id===id);
  m.reviews=at<0?[...list,rec]:list.map((x,i)=>i===at?rec:x);
  return `${id} · ${rec.count} reviews · average ${rec.avgScore} · responded ${rec.responded}${at<0?'':' (replaced)'}`;
};

const MEAS_ROOTCAUSE_LIST_SAVE:Handler=k=>{
  gsvMeasGuard(k.s,GSV_MEAS_KEYS.recovery);
  forbid(k,gsvMeasRoleOk(k.s,k.uid,GSV_MEAS_MANAGER_ROLES),'the root-cause list is edited by the Manager or Owner');
  const bad=gsvRootCauseListProblem(k.cmd.rows);if(bad)k.fail('VALIDATION',bad);
  const rows=(k.cmd.rows as {code:string;label:string;active:boolean}[]).map(r=>({code:r.code,label:r.label.trim(),active:r.active}));
  // A cause already used by a complaint cannot be removed, only retired (data is kept).
  const used=Object.values(slice(k).rootCause??{});
  for(const code of new Set(used))k.assert(rows.some(r=>r.code===code),`Root cause ${code} is used by a complaint: retire it instead of removing it.`);
  slice(k).rootCauseList=rows;
  return `Root causes saved · ${rows.length} (${rows.filter(r=>r.active).length} active)`;
};

const MEAS_ROOTCAUSE_SET:Handler=k=>{
  gsvMeasGuard(k.s,GSV_MEAS_KEYS.recovery);
  forbid(k,gsvMeasRoleOk(k.s,k.uid,GSV_MEAS_MANAGER_ROLES),'a root cause is set by the Manager or Owner');
  const cid=txt(k.cmd.complaint);
  k.assert(gsvSlice(k.s).complaints.some(x=>x.id===cid),'Complaint not found.','NOT_FOUND');
  const code=txt(k.cmd.code);
  const m=slice(k);
  if(!code){const next={...(m.rootCause??{})};delete next[cid];m.rootCause=next;return `${cid} · root cause cleared`;}
  const row=gsvRootCauses(k.s).find(r=>r.code===code);
  k.assert(row&&(row.active||m.rootCause?.[cid]===code),'Choose an active root cause from the list.');
  m.rootCause={...(m.rootCause??{}),[cid]:code};
  return `${cid} · root cause ${code} (${row!.label})`;
};

const MEAS_LOYALTY_SET:Handler=k=>{
  gsvMeasGuard(k.s,GSV_MEAS_KEYS.repeat);
  forbid(k,gsvMeasRoleOk(k.s,k.uid,GSV_MEAS_DESK_ROLES),'a loyalty tag is set by Reception, the Manager or the Owner');
  const cust=txt(k.cmd.customer);
  k.assert((k.s.guests??[]).some(g=>g.id===cust),'Customer not found.','NOT_FOUND');
  const tag=txt(k.cmd.tag);
  k.assert(tag.length<=GSV_LOYALTY_TAG_MAX,`A tag is at most ${GSV_LOYALTY_TAG_MAX} characters.`);
  k.assert(!guestLooksLikeIdNumber(tag),'A tag cannot hold an ID number.');
  const m=slice(k);const pref={...(m.preference?.[cust]??{})};
  if(tag)pref.loyaltyTag=tag;else delete pref.loyaltyTag;
  m.preference={...(m.preference??{}),[cust]:pref};
  return `${cust} · loyalty tag ${tag?`"${tag}"`:'cleared'}`;
};

export const handlers:Record<string,Handler>={MEAS_SURVEY_ADD,MEAS_REVIEW_SAVE,MEAS_ROOTCAUSE_LIST_SAVE,MEAS_ROOTCAUSE_SET,MEAS_LOYALTY_SET};
