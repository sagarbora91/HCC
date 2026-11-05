// LF lane lf-money-rate · contract §2 (A1) STAY_RATE_CHANGE and §3 (B1) GUEST_RATE_SET / GUEST_RATE_CLEAR.
// The `rate-change` approval branch lives in the LF-RATE block of handlers/folio.ts and calls `strApply` below.
import type {Handler,Kit} from '../commands';
import type {Approval,Booking,GuestAgreedRate,STRRateChange} from '../../../domain/types';
import {money} from '../../../domain/money';
import {gstForRoomNight} from '../../../domain/gst';
import {folioExpiresAt,folioKind,folioApprovalExpired} from '../../../domain/streams/folio';
import {strRefusal,strCanDirect,strPlan,strEffectiveFrom,strNightClosed,strState,strAgreedRate,strThresholdBps,strTariff,strOverThreshold,strReductionBps,strJudgedText,type STRScope} from '../../../domain/streams/stay-rate';

export type STRInput={rate:number;people:number;children:number;scope:STRScope;reason:string};

/** Apply a validated change to an in-house stay (direct or from an approved request). Mutates the booking and `s.stayRateX`. */
export function strApply(k:Kit,x:Booking,c:STRInput,approval?:Approval):STRRateChange{
  const {s,id,uid}=k;
  const old={rate:x.rate,people:x.people,children:x.children??0};
  // CTL-01: what the change is judged against, read BEFORE the booking moves. An Owner-set (or Owner-approved) rate becomes the new agreed rate.
  const moves=c.rate!==old.rate,agreed=strAgreedRate(s,x),bps=strThresholdBps(s),tariff=strTariff(s,x);
  const over=moves&&strOverThreshold(s,x,c.rate),ownerSet=s.users.find(u=>u.id===uid)?.role==='Owner';
  // `next`: tonight keeps the old rate — post tonight's night now (as the night audit would) before the rate changes.
  if(c.scope==='next'&&x.arrival<=s.date&&s.date<x.departure&&!strNightClosed(s,s.date))k.addNight(x,s.date);
  const reposted:STRRateChange['reposted']=[];
  if(c.rate!==old.rate){
    for(const l of strPlan(s,x,c.scope)){
      const rev=id('reversal'),nl=id('line');
      x.lines.push({id:rev,label:`Reversal · ${l.label} · rate change`,amount:-l.amount,date:s.date,kind:'adjustment',taxBps:l.taxBps,reversalOf:l.id,...(approval?{approvalId:approval.id}:{})});
      // DEC-069: GST slab of that night's actual (new) price, on that night's date.
      x.lines.push({id:nl,label:`Room night · ${l.date} · rate ${money(c.rate)}`,amount:c.rate,date:l.date,kind:'room',taxBps:gstForRoomNight(s,l.date,c.rate).bps});
      reposted.push({reversal:rev,line:nl,night:l.date});
    }
  }
  x.rate=c.rate;x.people=c.people;
  if(c.children)x.children=c.children;else delete x.children;
  x.version++;
  const st=s.stayRateX??(s.stayRateX={changes:[]});
  const ch:STRRateChange={id:id('STR'),booking:x.id,room:x.room,date:s.date,at:s.clock,by:approval?approval.actor:uid,scope:c.scope,reason:c.reason,
    oldRate:old.rate,newRate:c.rate,oldPeople:old.people,newPeople:c.people,oldChildren:old.children,newChildren:c.children,
    effectiveFrom:strEffectiveFrom(s,{...x,lines:[]},c.scope),reposted,
    tariff,agreedRate:agreed,agreedAfter:ownerSet&&moves?c.rate:agreed,thresholdBps:bps,overThreshold:over};
  if(approval){ch.approvalId=approval.id;ch.approvedBy=uid;}
  st.changes.push(ch);void strState;
  // CTL-01: a separate audit line the owner review can find (who changed, who approved, how far below the agreed rate).
  if(over){const name=(id_:string)=>s.users.find(u=>u.id===id_)?.name??id_;
    s.audit.unshift({id:id('audit'),at:s.clock,actor:k.user.name,action:'RATE REDUCTION OVER THRESHOLD',
      detail:`${x.id} · ${ch.id} · agreed ${money(agreed)} → ${money(c.rate)} · ${strReductionBps(agreed,c.rate)} bps below the agreed rate (threshold ${bps} bps) · changed by ${name(ch.by)}`
        +(approval?` · approved by ${name(uid)}`:' · applied directly')+` · ${c.reason}`});}
  return ch;
}

/** af2-fix finding 1 · a rate set on a room move / stay extension (server StayChangeCommands.RecordRateAsync): call AFTER the room / departure moved and
 *  BEFORE anything else changes the rate. Records the change against the agreed rate (scope today-next, posted nights untouched; only an Owner-set
 *  rate becomes the agreed rate), audits RATE REDUCTION OVER THRESHOLD and sets the booking's rate. undefined when the rate does not move. */
export function strRecordStayChange(k:Kit,x:Booking,rate:number,what:string,reason:string):STRRateChange|undefined{
  const {s,id,uid}=k;
  if(rate===x.rate)return undefined;
  const agreed=strAgreedRate(s,x),bps=strThresholdBps(s),tariff=strTariff(s,x),over=strOverThreshold(s,x,rate);
  const owner=s.users.find(u=>u.id===uid)?.role==='Owner',why=`${what} · ${reason}`.slice(0,200);
  const ch:STRRateChange={id:id('STR'),booking:x.id,room:x.room,date:s.date,at:s.clock,by:uid,scope:'today-next',reason:why,
    oldRate:x.rate,newRate:rate,oldPeople:x.people,newPeople:x.people,oldChildren:x.children??0,newChildren:x.children??0,effectiveFrom:s.date,reposted:[],
    tariff,agreedRate:agreed,agreedAfter:owner?rate:agreed,thresholdBps:bps,overThreshold:over};
  (s.stayRateX??(s.stayRateX={changes:[]})).changes.push(ch);
  if(over)s.audit.unshift({id:id('audit'),at:s.clock,actor:k.user.name,action:'RATE REDUCTION OVER THRESHOLD',
    detail:`${x.id} · ${ch.id} · agreed ${money(agreed)} → ${money(rate)} · ${strReductionBps(agreed,rate)} bps below the agreed rate (threshold ${bps} bps) · changed by ${k.user.name} · applied directly · ${why}`});
  x.rate=rate;
  return ch;
}

/** The CTL-01 audit suffix from the recorded change: what it was judged against. */
export const strJudgedOf=(c:STRRateChange)=>c.oldRate===c.newRate||c.agreedRate===undefined?'':
  `tariff ${money(c.tariff??0)} · agreed ${money(c.agreedRate)} · ${strReductionBps(c.agreedRate,c.newRate)} bps below agreed`+(c.overThreshold?` · over the ${c.thresholdBps} bps threshold (Owner)`:'');
const describe=(c:STRRateChange)=>`rate ${money(c.oldRate)} → ${money(c.newRate)} · guests ${c.oldPeople}+${c.oldChildren} → ${c.newPeople}+${c.newChildren} · ${c.scope} from ${c.effectiveFrom}`+(c.reposted.length?` · ${c.reposted.length} night(s) re-posted`:'')
  +(strJudgedOf(c)?` · ${strJudgedOf(c)}`:'');

const STAY_RATE_CHANGE:Handler=k=>{
  // PERM-STY-010 stay.rateChange.request (Owner, Manager, Reception); direct for the Owner and a Manager within PERM-RATE-001.
  const {s,cmd,req,open,assert,fail,uid,user}=k;
  req('checkin');open();
  if(!['Owner','Manager','Reception'].includes(user.role))fail('FORBIDDEN','Access denied: a rate or guest-count change is made by Reception, a Manager or the Owner.');
  const x=s.bookings.find(b=>b.id===cmd.booking||b.id===cmd.id);
  assert(x,'Booking not found.','NOT_FOUND');
  const refusal=strRefusal(s,x,cmd as Record<string,unknown>);
  if(refusal)fail(x!.status!=='In house'||/Nothing changes|no later night/.test(refusal)?'INVALID_TRANSITION':'VALIDATION',refusal);
  const c:STRInput={rate:cmd.rate===undefined||cmd.rate===null?x!.rate:Number(cmd.rate),people:cmd.people===undefined||cmd.people===null?x!.people:Number(cmd.people),
    children:cmd.children===undefined||cmd.children===null?(x!.children??0):Number(cmd.children),scope:cmd.scope,reason:String(cmd.reason).trim().slice(0,200)};
  if(strCanDirect(s,uid,x!,c)){const ch=strApply(k,x!,c);return `${x!.id} · ${describe(ch)}`;}
  assert(!s.approvals.some(a=>a.booking===x!.id&&folioKind(a)==='rate-change'&&a.status==='Pending'&&!folioApprovalExpired(s,a)),'A rate-change request is already pending on this stay.','INVALID_TRANSITION');
  const a:Approval={id:k.id('APR'),booking:x!.id,actor:uid,version:x!.version,status:'Pending',expiresAt:folioExpiresAt(s.clock),amount:c.rate,reason:c.reason,kind:'rate-change',
    meta:{command:'APPROVAL',rate:c.rate,people:c.people,children:c.children,scope:c.scope,oldRate:x!.rate,oldPeople:x!.people,oldChildren:x!.children??0,
      agreedRate:strAgreedRate(s,x!),overThreshold:c.rate!==x!.rate&&strOverThreshold(s,x!,c.rate)}}; // CTL-01: shown to the approver
  s.approvals.push(a);
  return `${x!.id} · rate change ${money(x!.rate)} → ${money(c.rate)} (guests ${c.people}+${c.children}, ${c.scope}) requested · ${a.id}`+(c.rate!==x!.rate?` · ${strJudgedText(s,x!,c.rate)}`:'');
};

/** The room types a guest rate may name (room-type keys of the hotel's rooms). */
const typeKnown=(k:Kit,type:string)=>k.s.rooms.some(r=>r.type===type);
const ownerOnly=(k:Kit)=>{k.req('admin');if(k.user.role!=='Owner')k.fail('FORBIDDEN','Access denied: a guest’s agreed rate is set by the Owner (DEC-058).');};

const GUEST_RATE_SET:Handler=k=>{
  // PERM-GST-010 guest.agreedRate (Owner)
  const {s,cmd,assert,uid}=k;ownerOnly(k);
  const g=(s.guests||[]).find(x=>x.id===cmd.guestId);assert(g,'Guest profile not found.','NOT_FOUND');
  const type=String(cmd.roomType??'').trim(); // contract `type` (cmd.type is the command name in the prototype)
  assert(typeKnown(k,type),'Choose a room type.');
  assert(Number.isInteger(cmd.rate)&&cmd.rate>0,'Enter the agreed rate per night in whole paise (more than 0).');
  const validTo=cmd.validTo?String(cmd.validTo):undefined;
  assert(!validTo||/^\d{4}-\d{2}-\d{2}$/.test(validTo),'Choose a valid “valid to” date.');
  assert(!validTo||validTo>=s.date,'The “valid to” date is in the past.');
  const row:GuestAgreedRate={type,rate:cmd.rate,setBy:uid,at:s.clock};
  if(cmd.plan)row.plan=String(cmd.plan);if(validTo)row.validTo=validTo;if(String(cmd.note??'').trim())row.note=String(cmd.note).trim().slice(0,200);
  g!.agreedRates=[...(g!.agreedRates??[]).filter(r=>r.type!==type),row];g!.version=(g!.version||1)+1;
  return `${g!.id} · agreed rate ${type} ${money(cmd.rate)}${validTo?' valid to '+validTo:''}`;
};
const GUEST_RATE_CLEAR:Handler=k=>{
  const {s,cmd,assert}=k;ownerOnly(k);
  const g=(s.guests||[]).find(x=>x.id===cmd.guestId);assert(g,'Guest profile not found.','NOT_FOUND');
  assert(g!.agreedRates?.some(r=>r.type===cmd.roomType),'No agreed rate for that room type.','NOT_FOUND');
  g!.agreedRates=g!.agreedRates!.filter(r=>r.type!==cmd.roomType);if(!g!.agreedRates.length)delete g!.agreedRates;g!.version=(g!.version||1)+1;
  return `${g!.id} · agreed rate ${cmd.roomType} cleared`;
};

export const handlers:Record<string,Handler>={STAY_RATE_CHANGE,GUEST_RATE_SET,GUEST_RATE_CLEAR};
