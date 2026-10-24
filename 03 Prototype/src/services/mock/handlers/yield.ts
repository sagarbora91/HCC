// Module 13 · Advanced tariff & yield — command handlers. Command types MUST start with 'YLD_'. Return value = audit/summary text.
// Owner (admin + reports) changes rules and publishes; Manager (approve) may propose a publish → Owner decides (YLD_DECIDE).
import type {Handler,Kit} from '../commands';
import type {YLDState,YLDTarget} from '../../../domain/streams/yield';
import {yldEmpty,yldRoomTypes,yldPct,yldPlanPublish,yldTariffSig,yldNight,yldProposalExpired,yldDates,YLD_WINDOW,YLD_DOW_NAMES,YLD_ROUND_OPTIONS,YLD_APPROVAL_KIND} from '../../../domain/streams/yield';
import {can} from '../../../domain/selectors';
import {money,addDays,addMinutes,nights} from '../../../domain/money';

const DATE=/^\d{4}-\d{2}-\d{2}$/;
const slice=(k:Kit):YLDState=>{const s=k.s;if(!s.yieldX)s.yieldX=yldEmpty();return s.yieldX;};
const ownerGate=(k:Kit)=>{k.req('reports');if(!can(k.s,k.uid,'admin'))k.fail('FORBIDDEN','Only the Owner can change tariffs and yield rules. A Manager can propose a publish for Owner approval.');};
const bpsOk=(k:Kit,v:unknown,min=-5000,max=10000)=>k.assert(Number.isInteger(v)&&(v as number)>=min&&(v as number)<=max,`Use an adjustment between ${yldPct(min)} and ${yldPct(max)}.`);
const dateOk=(k:Kit,v:unknown,label:string)=>k.assert(typeof v==='string'&&DATE.test(v),`Choose a valid ${label}.`);
function log(k:Kit,area:string,before:string,after:string){
  slice(k).history.unshift({id:k.id('YLDH'),at:addMinutes(k.s.clock,1),actor:k.user.name,area,before,after,reason:String(k.cmd.reason??'').trim()});
}
const typesOf=(k:Kit,types:unknown)=>{const all=yldRoomTypes(k.s);const list=Array.isArray(types)?types as string[]:[];
  k.assert(list.every(t=>all.includes(t)),'Unknown room type.');return list;};

const YLD_PLAN_SAVE:Handler=k=>{
  const {cmd,assert}=k;ownerGate(k);const y=slice(k);
  const p=y.plans.find(p=>p.id===cmd.id);assert(p,'Rate plan not found.','NOT_FOUND');
  const before=`${yldPct(p!.adjBps)} · min ${p!.minNights} · ${p!.active?'active':'inactive'}`;
  const adj=cmd.adjBps??p!.adjBps,min=cmd.minNights??p!.minNights,active=cmd.active??p!.active;
  if(p!.id==='BAR'){assert(adj===0,'BAR is the reference rate: its adjustment stays at 0%. Change seasons or rules instead.');assert(active,'BAR cannot be deactivated.');}
  bpsOk(k,adj,-9000,5000);
  if(p!.dayUse)assert(min===0,'Day-use has no overnight (minimum nights 0).');
  else assert(Number.isInteger(min)&&min>=1&&min<=30,'Minimum nights must be 1–30.');
  if(p!.dayUse&&cmd.closeAtOcc!==undefined){assert(Number.isInteger(cmd.closeAtOcc)&&cmd.closeAtOcc>=50&&cmd.closeAtOcc<=101,'Closing occupancy must be 50–101%.');p!.dayUse={...p!.dayUse,closeAtOcc:cmd.closeAtOcc};}
  p!.adjBps=adj;p!.minNights=min;p!.active=!!active;
  const after=`${yldPct(adj)} · min ${min} · ${active?'active':'inactive'}`;
  assert(before!==after||cmd.closeAtOcc!==undefined,'No change.');
  log(k,'Plan · '+p!.name,before,after);
  return `Rate plan ${p!.code} · ${before} → ${after}`;
};
const YLD_SEASON_SAVE:Handler=k=>{
  const {cmd,assert,id}=k;ownerGate(k);const y=slice(k);
  if(cmd.remove){const i=y.seasons.findIndex(x=>x.id===cmd.id);assert(i>=0,'Season not found.','NOT_FOUND');const [old]=y.seasons.splice(i,1);
    log(k,'Season · '+old.name,`${old.from}–${old.to} ${yldPct(old.adjBps)}`,'removed');return `Season ${old.name} removed`;}
  assert(String(cmd.name??'').trim().length>=2,'Name the season.');
  dateOk(k,cmd.from,'start date');dateOk(k,cmd.to,'end date');assert(cmd.to>=cmd.from,'The season must end on or after its start date.');
  assert(nights(cmd.from,cmd.to)<=366,'A season can cover at most one year.');
  bpsOk(k,cmd.adjBps);assert(cmd.adjBps!==0,'A season needs a non-zero adjustment.');
  const types=typesOf(k,cmd.types);const priority=Number.isInteger(cmd.priority)?cmd.priority:1;
  const rec={id:'',name:String(cmd.name).trim(),from:cmd.from,to:cmd.to,adjBps:cmd.adjBps,types,priority};
  const after=`${rec.from}–${rec.to} ${yldPct(rec.adjBps)}${types.length?' · '+types.join(', '):''}`;
  if(cmd.id){const x=y.seasons.find(x=>x.id===cmd.id);assert(x,'Season not found.','NOT_FOUND');
    const before=`${x!.from}–${x!.to} ${yldPct(x!.adjBps)}${x!.types.length?' · '+x!.types.join(', '):''}`;Object.assign(x!,{...rec,id:x!.id});log(k,'Season · '+rec.name,before,after);return `Season ${rec.name} updated · ${after}`;}
  rec.id=id('YLDS');y.seasons.push(rec);log(k,'Season · '+rec.name,'—',after);return `Season ${rec.name} added · ${after}`;
};
const YLD_DOW_SET:Handler=k=>{
  const {cmd,assert}=k;ownerGate(k);const y=slice(k);
  assert(Array.isArray(cmd.dow)&&cmd.dow.length===7,'Give an adjustment for each day of the week.');
  (cmd.dow as unknown[]).forEach(v=>bpsOk(k,v,-3000,5000));
  const show=(xs:number[])=>xs.map((v,i)=>v?`${YLD_DOW_NAMES[i]} ${yldPct(v)}`:'').filter(Boolean).join(', ')||'none';
  const before=show(y.dow),after=show(cmd.dow);assert(before!==after,'No change.');
  y.dow=[...cmd.dow];log(k,'Day-of-week rules',before,after);return `Day-of-week rules · ${after}`;
};
const YLD_OCC_SAVE:Handler=k=>{
  const {cmd,assert,id}=k;ownerGate(k);const y=slice(k);
  const show=(r:{op:string;occ:number;adjBps:number})=>`occupancy ${r.op==='>='?'≥':'<'} ${r.occ}% → ${yldPct(r.adjBps)}`;
  if(cmd.remove){const i=y.occ.findIndex(r=>r.id===cmd.id);assert(i>=0,'Rule not found.','NOT_FOUND');const [old]=y.occ.splice(i,1);log(k,'Occupancy rule',show(old),'removed');return `Occupancy rule removed · ${show(old)}`;}
  assert(cmd.op==='>='||cmd.op==='<','Choose “at or above” or “below”.');
  assert(Number.isInteger(cmd.occ)&&cmd.occ>=1&&cmd.occ<=100,'Occupancy threshold must be 1–100%.');
  bpsOk(k,cmd.adjBps,-5000,5000);assert(cmd.adjBps!==0,'A rule needs a non-zero adjustment.');
  if(cmd.op==='>=')assert(cmd.adjBps>0,'High-occupancy rules should raise the rate.');else assert(cmd.adjBps<0,'Low-occupancy rules should lower the rate.');
  assert(!y.occ.some(r=>r.id!==cmd.id&&r.op===cmd.op&&r.occ===cmd.occ),'A rule with this threshold already exists.');
  const rec={op:cmd.op as '>='|'<',occ:cmd.occ,adjBps:cmd.adjBps};
  if(cmd.id){const r=y.occ.find(r=>r.id===cmd.id);assert(r,'Rule not found.','NOT_FOUND');const before=show(r!);Object.assign(r!,rec);log(k,'Occupancy rule',before,show(rec));return 'Occupancy rule updated · '+show(rec);}
  y.occ.push({id:id('YLDO'),...rec});log(k,'Occupancy rule','—',show(rec));return 'Occupancy rule added · '+show(rec);
};
const YLD_GUARD_SET:Handler=k=>{
  const {cmd,assert}=k;ownerGate(k);const y=slice(k);
  bpsOk(k,cmd.floorBps,-5000,0);bpsOk(k,cmd.ceilBps,0,10000);
  assert(YLD_ROUND_OPTIONS.includes(cmd.roundTo),'Choose a rounding unit.');
  const show=(g:{floorBps:number;ceilBps:number;roundTo:number})=>`floor ${yldPct(g.floorBps)} · ceiling ${yldPct(g.ceilBps)} · round ${money(g.roundTo)}`;
  const before=show(y.guard);y.guard={floorBps:cmd.floorBps,ceilBps:cmd.ceilBps,roundTo:cmd.roundTo};const after=show(y.guard);
  assert(before!==after,'No change.');log(k,'Guardrails',before,after);return 'Guardrails · '+after;
};
const YLD_RESTRICTION_SAVE:Handler=k=>{
  const {cmd,assert,id}=k;ownerGate(k);const y=slice(k);
  const show=(r:{type:string;from:string;to:string;minStay:number;cta:boolean})=>`${r.type==='*'?'All types':r.type} · arrivals ${r.from}–${r.to}${r.minStay>1?' · min '+r.minStay+' nights':''}${r.cta?' · closed to arrival':''}`;
  if(cmd.remove){const i=y.restrictions.findIndex(r=>r.id===cmd.id);assert(i>=0,'Restriction not found.','NOT_FOUND');const [old]=y.restrictions.splice(i,1);log(k,'Restriction',show(old),'removed');return 'Restriction removed · '+show(old);}
  const type=String(cmd.roomType??'*');assert(type==='*'||yldRoomTypes(k.s).includes(type),'Unknown room type.');
  dateOk(k,cmd.from,'first arrival date');dateOk(k,cmd.to,'last arrival date');assert(cmd.to>=cmd.from,'The last arrival date must be on or after the first.');
  assert(nights(cmd.from,cmd.to)<=90,'A restriction can cover at most 90 days.');
  const minStay=cmd.minStay??1;assert(Number.isInteger(minStay)&&minStay>=1&&minStay<=14,'Minimum stay must be 1–14 nights.');
  assert(minStay>1||!!cmd.cta,'Set a minimum stay above 1 night or close to arrival.');
  const rec={type,from:cmd.from as string,to:cmd.to as string,minStay,cta:!!cmd.cta,note:String(cmd.note??'').trim()};
  if(cmd.id){const r=y.restrictions.find(r=>r.id===cmd.id);assert(r,'Restriction not found.','NOT_FOUND');const before=show(r!);Object.assign(r!,rec);log(k,'Restriction',before,show(rec));return 'Restriction updated · '+show(rec);}
  y.restrictions.push({id:id('YLDR'),...rec});log(k,'Restriction','—',show(rec));return 'Restriction added · '+show(rec);
};
const YLD_OVERRIDE:Handler=k=>{
  const {s,cmd,assert}=k;ownerGate(k);const y=slice(k);
  assert(yldRoomTypes(s).includes(cmd.roomType),'Choose a room type.');dateOk(k,cmd.date,'night');
  assert(cmd.date>=s.date,'Overrides apply to tonight or later.');
  assert(String(cmd.reason??'').trim().length>=3,'Give a reason for the override.');
  const i=y.overrides.findIndex(o=>o.type===cmd.roomType&&o.date===cmd.date);
  if(cmd.rate===null||cmd.rate===undefined){assert(i>=0,'There is no override to clear.','NOT_FOUND');const [old]=y.overrides.splice(i,1);
    log(k,`Override · ${cmd.roomType} ${cmd.date}`,money(old.rate),'cleared');return `Override cleared · ${cmd.roomType} ${cmd.date}`;}
  assert(Number.isSafeInteger(cmd.rate)&&cmd.rate>=10000&&cmd.rate<=10000000,'Enter a nightly rate between ₹100 and ₹1,00,000.');
  const before=i>=0?money(y.overrides[i].rate):'computed '+money(yldNight(s,cmd.roomType,cmd.date).computed);
  if(i>=0)y.overrides.splice(i,1);
  y.overrides.push({type:cmd.roomType,date:cmd.date,rate:cmd.rate,reason:String(cmd.reason).trim(),actor:k.uid,at:addMinutes(s.clock,1)});
  log(k,`Override · ${cmd.roomType} ${cmd.date}`,before,money(cmd.rate));
  return `Override · ${cmd.roomType} ${cmd.date} → ${money(cmd.rate)} (not live until published)`;
};

/** Validates a publish window and the room types; returns the type list. */
function windowOk(k:Kit){
  const {s,cmd,assert}=k;
  dateOk(k,cmd.from,'first night');dateOk(k,cmd.to,'last night');
  const closed=s.closedDates.includes(s.date);
  assert(cmd.from>=s.date&&!(closed&&cmd.from===s.date),closed?'Today’s business day is closed: publish from tomorrow onwards.':'Nights before the business date cannot be re-priced.','INVALID_TRANSITION');
  assert(cmd.to>=cmd.from,'The last night must be on or after the first.');
  assert(cmd.to<=addDays(s.date,YLD_WINDOW-1),`Publish within the ${YLD_WINDOW}-night window (to ${addDays(s.date,YLD_WINDOW-1)}).`);
  const types=typesOf(k,cmd.types);assert(types.length>0,'Choose at least one room type.');
  return types;
}
function apply(k:Kit,from:string,to:string,types:string[],target:YLDTarget|undefined,approval?:string){
  const {s,assert}=k;const y=slice(k);
  const plan=yldPlanPublish(s,from,to,types,target);
  assert(plan.rows.length>0,'Nothing to publish: the live tariffs already match the suggested rates.');
  s.tariffs=plan.tariffs;
  for(const r of plan.rows)y.owned[r.type+'|'+r.date]=r.after;
  const pid=k.id('YLDP');
  y.publishes.unshift({id:pid,at:addMinutes(s.clock,1),actor:k.user.name,from,to,types,rows:plan.rows,reason:String(k.cmd.reason??'').trim(),approval});
  const changed=plan.rows.filter(r=>r.before!==r.after).length;
  log(k,'Publish '+pid,`${from} to ${to} · ${types.join(', ')}`,`${changed} night rate${changed===1?'':'s'} written${approval?' · approved '+approval:''}`);
  return {pid,changed};
}
const YLD_PUBLISH:Handler=k=>{
  ownerGate(k);const types=windowOk(k);const {cmd}=k;
  const {pid,changed}=apply(k,cmd.from,cmd.to,types,undefined);
  return `${pid} · ${changed} nightly tariff${changed===1?'':'s'} published ${cmd.from} to ${cmd.to} (${types.join(', ')}) · existing bookings keep agreed rates`;
};
const YLD_PROPOSE:Handler=k=>{
  const {s,cmd,assert,uid,req}=k;req('reports');req('approve');
  const types=windowOk(k);
  assert(!s.approvals.some(a=>a.kind===YLD_APPROVAL_KIND&&a.status==='Pending'&&!yldProposalExpired(s,a)),'A tariff publish request is already pending. Ask the Owner to decide it first.','INVALID_TRANSITION');
  const rates=types.flatMap(type=>yldDates(cmd.from,nights(cmd.from,cmd.to)+1).map(date=>({type,date,rate:yldNight(s,type,date).suggested})));
  const map=new Map(rates.map(r=>[r.type+'|'+r.date,r.rate]));
  const preview=yldPlanPublish(s,cmd.from,cmd.to,types,(t,d)=>map.get(t+'|'+d)!);
  assert(preview.rows.length>0,'Nothing to publish: the live tariffs already match the suggested rates.');
  const aid=k.id('APR');const changed=preview.rows.filter(r=>r.before!==r.after).length;
  const reason=String(cmd.reason??'').trim()||`Publish yield tariffs ${cmd.from} to ${cmd.to}`;
  s.approvals.push({id:aid,booking:'',amount:0,reason,status:'Pending',actor:uid,version:1,kind:YLD_APPROVAL_KIND,target:`${cmd.from}→${cmd.to}`,
    expiresAt:addMinutes(s.clock,(s.settings.approvalExpiryHours??24)*60),
    meta:{command:'YLD_DECIDE',from:cmd.from,to:cmd.to,types,rates,sig:yldTariffSig(s,types),changed,summary:`Tariff publish ${cmd.from} to ${cmd.to} · ${types.join(', ')} · ${changed} night rate${changed===1?'':'s'}`}});
  log(k,'Proposal '+aid,'—',`${cmd.from} to ${cmd.to} · ${types.join(', ')} · ${changed} changes · awaiting Owner`);
  return `${aid} · tariff publish requested (Owner approval) · ${cmd.from} to ${cmd.to} · ${changed} night rate${changed===1?'':'s'}`;
};
const YLD_DECIDE:Handler=k=>{
  const {s,cmd,assert,uid,fail}=k;k.req('reports');
  const a=s.approvals.find(a=>a.id===cmd.id&&a.kind===YLD_APPROVAL_KIND);
  assert(a,'Tariff request not found.','NOT_FOUND');
  assert(a!.status==='Pending','This request is no longer pending.','INVALID_TRANSITION');
  assert(a!.actor!==uid,'You cannot approve your own request. Switch to another authorised demo user.','FORBIDDEN');
  if(!can(s,uid,'admin'))fail('FORBIDDEN','Only the Owner can decide tariff publish requests.');
  a!.approver=uid;a!.decidedAt=addMinutes(s.clock,1);
  if(!cmd.approve){a!.status=yldProposalExpired(s,a!)?'Expired':'Rejected';log(k,'Proposal '+a!.id,'pending',a!.status.toLowerCase());return `${a!.id} · ${a!.status}`;}
  assert(!yldProposalExpired(s,a!),'This request expired. Reject it and ask for a new one.','INVALID_TRANSITION');
  const m=a!.meta as {from:string;to:string;types:string[];rates:{type:string;date:string;rate:number}[];sig:string};
  assert(yldTariffSig(s,m.types)===m.sig,'Tariffs changed after this request was raised. Reject it and propose again.','STALE_VERSION');
  const closed=s.closedDates.includes(s.date);
  assert(m.from>s.date||(m.from===s.date&&!closed),'The first night of this request has passed. Reject it and propose again.','INVALID_TRANSITION');
  const map=new Map(m.rates.map(r=>[r.type+'|'+r.date,r.rate]));
  const {pid,changed}=apply(k,m.from,m.to,m.types,(t,d)=>map.get(t+'|'+d)!,a!.id);
  a!.status='Executed';a!.meta={...a!.meta,publish:pid};
  return `${a!.id} · approved · ${pid} · ${changed} nightly tariff${changed===1?'':'s'} published`;
};

export const handlers:Record<string,Handler>={YLD_PLAN_SAVE,YLD_SEASON_SAVE,YLD_DOW_SET,YLD_OCC_SAVE,YLD_GUARD_SET,YLD_RESTRICTION_SAVE,YLD_OVERRIDE,YLD_PUBLISH,YLD_PROPOSE,YLD_DECIDE};
