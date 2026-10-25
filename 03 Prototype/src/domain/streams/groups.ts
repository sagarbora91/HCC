// Module 14 · Group & corporate — pure helpers, selectors and the state slice type. Exported names MUST start with 'grp' / 'GRP' (shared mock namespace).
// Company accounts (negotiated rate, credit limit), group blocks (allotment per room type, cut-off, auto-release),
// rooming-list pickup, pickup report and a read-only master folio. See 02 Planning/modules/HCC_Module_14_Group_Corporate_Spec.md.
import type {State,Booking} from '../types';
import {available,availableRaw,registerRoomHold,totals} from '../selectors';
import {gstForRoomNight,gstLineTax,gstStayTax} from '../gst';
import {addDays,nights} from '../money';

export type GRPCompany={id:string;name:string;
  /** GSTIN-format placeholder — always fictional in this prototype. */
  gstin:string;contact:string;phone:string;email:string;city:string;
  /** Negotiated nightly rate per room type (paise). Missing type = rack rate. */
  rates:Record<string,number>;
  /** Credit limit in paise (0 = no credit; any bill-to-company pickup needs an override). */
  creditLimit:number;
  /** Receivable carried in from before this demo (paise, simulated AR). */
  openingOutstanding:number;
  limitHistory?:{from:number;to:number;at:string;actor:string;reason:string;request?:string}[];
};
export type GRPBlockStatus='Open'|'Released'|'Cancelled';
export type GRPBlock={id:string;name:string;
  /** Company account ID (optional — e.g. a wedding party has none). */
  company?:string;billTo:'company'|'guest';
  arrival:string;departure:string;
  /** Last business date on which rooms may be picked up. Unpicked rooms are released when the business date passes it. */
  cutoff:string;
  /** Rooms held per room type. */
  allot:Record<string,number>;
  /** Agreed block rate per room type (paise). */
  rates:Record<string,number>;
  /** Rooms released per room type (auto at cut-off or manual). */
  released:Record<string,number>;
  members:{booking:string;type:string}[];
  status:GRPBlockStatus;releasedBy?:string;releasedAt?:string;cancelReason?:string;
  contact?:string;notes?:string;createdBy:string;createdAt:string;
};
export type GRPLimitRequest={id:string;company:string;from:number;to:number;reason:string;actor:string;at:string;status:'Pending'|'Approved'|'Rejected';approver?:string;decidedAt?:string;note?:string};
export type GRPOverride={id:string;block:string;company:string;exposure:number;limit:number;reason:string;actor:string;at:string};
/** State slice for this module, stored at s.groupsX (optional; absent in old saves → treat as empty). */
export type GRPState={companies:GRPCompany[];blocks:GRPBlock[];requests:GRPLimitRequest[];overrides:GRPOverride[]};
export const grpEmpty=():GRPState=>({companies:[],blocks:[],requests:[],overrides:[]});
export const grpSlice=(s:State):GRPState=>{const x=s.groupsX as Partial<GRPState>|undefined;return {companies:x?.companies??[],blocks:x?.blocks??[],requests:x?.requests??[],overrides:x?.overrides??[]};};
/** Ensure the slice exists on a (cloned) state and return it for mutation. */
export function grpEnsure(s:State):GRPState{const x=grpSlice(s);s.groupsX=x;return x;}

/** Room types in display order (as they occur in the room list). */
export function grpRoomTypes(s:State):string[]{return [...new Set(s.rooms.map(r=>r.type))];}
export const GRP_ACTIVE:Booking['status'][]=['Held','Confirmed','In house','Departed'];
export const grpCompany=(s:State,id?:string)=>id?grpSlice(s).companies.find(c=>c.id===id):undefined;
export const grpBlock=(s:State,id?:string)=>id?grpSlice(s).blocks.find(b=>b.id===id):undefined;
/** Member bookings that still count as picked up (cancelled / no-show / expired members give the room back while the block is open). */
export function grpMembers(s:State,blk:GRPBlock){return blk.members.map(m=>({...m,b:s.bookings.find(b=>b.id===m.booking)})).filter((m):m is {booking:string;type:string;b:Booking}=>!!m.b);}
export function grpPicked(s:State,blk:GRPBlock,type?:string){return grpMembers(s,blk).filter(m=>(!type||m.type===type)&&GRP_ACTIVE.includes(m.b.status)&&!(m.b.status==='Held'&&m.b.holdUntil&&s.clock>=m.b.holdUntil)).length;}
/** Effective status: an Open block whose cut-off has passed is due for auto-release (shown as Released before the sweep persists it). */
export function grpStatus(s:State,blk:GRPBlock):GRPBlockStatus{return blk.status==='Open'&&s.date>blk.cutoff?'Released':blk.status;}
export const grpReleaseDue=(s:State,blk:GRPBlock)=>blk.status==='Open'&&s.date>blk.cutoff;
export function grpRemaining(s:State,blk:GRPBlock,type:string){return grpStatus(s,blk)==='Open'?Math.max(0,(blk.allot[type]||0)-grpPicked(s,blk,type)):0;}
/** Rooms of a type free for every night of [a, d) under the shared availability check. */
export function grpFreeRooms(s:State,type:string,a:string,d:string){return s.rooms.filter(r=>r.type===type&&availableRaw(s,r.id,a,d)).map(r=>r.id);}
/** Rooms free for a block's own pickup: not booked/blocked and not held by ANOTHER block (its own held rooms first). */
export function grpPickupRooms(s:State,blk:GRPBlock,type:string,a:string,d:string){
  const own=grpHolds(s).filter(h=>h.block===blk.id&&h.type===type).map(h=>h.room);
  const free=grpFreeRooms(s,type,a,d).filter(r=>!grpHolds(s).some(h=>h.room===r&&h.block!==blk.id&&a<h.d&&d>h.a));
  return [...own.filter(r=>free.includes(r)),...free.filter(r=>!own.includes(r))];
}
/** Specific rooms held for open blocks' unpicked allotment (deterministic: blocks by arrival, rooms highest number first).
 *  These rooms show as Blocked on the room board/calendar and are not bookable except through the block's rooming list. */
export type GRPHold={room:string;block:string;type:string;a:string;d:string;label:string};
const grpHoldMemo=new WeakMap<State,{fp:string;holds:GRPHold[]}>();
export function grpHolds(s:State):GRPHold[]{
  const blocks=grpSlice(s).blocks;if(!blocks.length)return [];
  // Only bookings that can matter to a hold (live ones, and block members for pickup counts) enter the fingerprint — history stays (Departed, Cancelled) do not.
  const members=new Set(blocks.flatMap(b=>b.members.map(m=>m.booking)));
  const fp=s.date+'|'+s.clock+'|'+s.bookings.filter(b=>b.status==='Held'||b.status==='Confirmed'||b.status==='In house'||members.has(b.id)).map(b=>b.id+b.status+b.room+b.arrival+b.departure+(b.holdUntil||'')).join(',')+'|'+JSON.stringify(blocks)+'|'+s.rooms.map(r=>r.id+(r.blocked?1:0)+(r.blockFrom||'')+(r.blockTo||'')).join(',');
  const m=grpHoldMemo.get(s);if(m&&m.fp===fp)return m.holds;
  const holds:GRPHold[]=[];
  const open=blocks.filter(b=>grpStatus(s,b)==='Open'&&b.departure>s.date).sort((x,y)=>x.arrival.localeCompare(y.arrival)||x.id.localeCompare(y.id));
  for(const blk of open){const a=blk.arrival<s.date?s.date:blk.arrival,d=blk.departure;
    for(const type of Object.keys(blk.allot)){let n=grpRemaining(s,blk,type);if(n<=0)continue;
      const cands=s.rooms.filter(r=>r.type===type).map(r=>r.id).sort((x,y)=>y.localeCompare(x,undefined,{numeric:true}))
        .filter(r=>availableRaw(s,r,a,d)&&!holds.some(h=>h.room===r&&a<h.d&&d>h.a));
      for(const room of cands){if(n--<=0)break;holds.push({room,block:blk.id,type,a,d,label:`Group hold · ${blk.id} ${blk.name}`});}
    }}
  grpHoldMemo.set(s,{fp,holds});return holds;
}
registerRoomHold((s,room,a,d,exempt)=>{const h=grpHolds(s).find(h=>h.room===room&&h.block!==exempt&&a<h.d&&d>h.a);return h?{id:h.block,label:h.label}:undefined;});
/** Unpicked rooms of a type held by OTHER open blocks whose stay overlaps [a, d) — the module's own soft hold. */
export function grpHeldElsewhere(s:State,type:string,a:string,d:string,except=''){return grpSlice(s).blocks.filter(b=>b.id!==except&&a<b.departure&&d>b.arrival).reduce((n,b)=>n+grpRemaining(s,b,type),0);}
export type GRPLine={type:string;allot:number;picked:number;released:number;remaining:number;rate:number;free:number;atRisk:boolean;pct:number};
/** Per-type allotment / pickup / release figures for one block. `atRisk` = shared availability shows fewer free rooms than the block still needs. */
export function grpLines(s:State,blk:GRPBlock):GRPLine[]{
  const st=grpStatus(s,blk);
  return Object.keys(blk.allot).filter(t=>blk.allot[t]>0||(blk.released[t]||0)>0).map(type=>{
    const allot=blk.allot[type]||0,picked=grpPicked(s,blk,type);
    const released=(blk.released[type]||0)+(grpReleaseDue(s,blk)?Math.max(0,allot-picked):0);
    const remaining=st==='Open'?Math.max(0,allot-picked):0;
    const free=st==='Open'&&blk.departure>s.date?grpFreeRooms(s,type,blk.arrival<s.date?s.date:blk.arrival,blk.departure).length:0;
    const base=allot+(blk.released[type]||0);
    return {type,allot:base,picked,released,remaining,rate:blk.rates[type]||0,free,atRisk:st==='Open'&&free<remaining,pct:base?Math.round(picked/base*100):0};
  });
}
export function grpBlockTotals(s:State,blk:GRPBlock){const l=grpLines(s,blk);const sum=(k:keyof GRPLine)=>l.reduce((a,x)=>a+(x[k] as number),0);
  const allot=sum('allot'),picked=sum('picked');return {allot,picked,released:sum('released'),remaining:sum('remaining'),pct:allot?Math.round(picked/allot*100):0,atRisk:l.some(x=>x.atRisk),nights:nights(blk.arrival,blk.departure)};}
/** Days from the business date to the cut-off (negative = passed). */
export function grpDaysToCutoff(s:State,blk:GRPBlock){return nights(s.date,blk.cutoff);}

/** Release every open block whose cut-off has passed (mutates s). Returns the released block IDs. Idempotent. */
export function grpSweep(s:State):string[]{
  const x=s.groupsX as GRPState|undefined;if(!x?.blocks)return [];
  const out:string[]=[];
  for(const blk of x.blocks)if(grpReleaseDue(s,blk)){
    for(const t of Object.keys(blk.allot)){const picked=grpPicked(s,blk,t),free=Math.max(0,blk.allot[t]-picked);blk.released[t]=(blk.released[t]||0)+free;blk.allot[t]=picked;}
    blk.status='Released';blk.releasedBy='auto';blk.releasedAt=s.clock;out.push(blk.id);
  }
  return out;
}

/** Estimated unpaid value of a stay: posted lines + unposted agreed nights, GST per night, less net payments. Company credit stays owed. */
export function grpBookingExposure(s:State,b:Booking){
  if(!GRP_ACTIVE.includes(b.status))return 0;
  const t=totals(b);let net=t.net,tax=t.tax;
  if(b.status!=='Departed')for(let d=b.arrival;d<b.departure;d=addDays(d,1))if(!b.lines.some(l=>l.kind==='room'&&l.date===d)){net+=b.rate;tax+=gstLineTax(b.rate,gstForRoomNight(s,d,b.rate).bps);}
  return Math.max(0,net+tax-t.paid);
}
/** Bookings billed to a company: members of its bill-to-company blocks plus bookings carrying its name. */
export function grpCompanyBookings(s:State,c:GRPCompany):Booking[]{
  const ids=new Set<string>();
  for(const blk of grpSlice(s).blocks)if(blk.company===c.id&&blk.billTo==='company')blk.members.forEach(m=>ids.add(m.booking));
  for(const b of s.bookings)if(b.company?.name&&b.company.name.toLowerCase()===c.name.toLowerCase())ids.add(b.id);
  return s.bookings.filter(b=>ids.has(b.id));
}
export function grpExposure(s:State,c:GRPCompany){return c.openingOutstanding+grpCompanyBookings(s,c).reduce((a,b)=>a+grpBookingExposure(s,b),0);}
export type GRPCreditLevel='ok'|'near'|'over'|'none';
export function grpCreditLevel(limit:number,exposure:number):GRPCreditLevel{if(limit<=0)return exposure>0?'over':'none';return exposure>limit?'over':exposure>=limit*0.8?'near':'ok';}
export const GRP_CREDIT_LABEL:Record<GRPCreditLevel,string>={ok:'Within limit',near:'Near limit (≥ 80%)',over:'Over limit',none:'No credit'};
export function grpCredit(s:State,c:GRPCompany){const exposure=grpExposure(s,c);return {exposure,limit:c.creditLimit,pct:c.creditLimit?Math.round(exposure/c.creditLimit*100):0,level:grpCreditLevel(c.creditLimit,exposure),available:Math.max(0,c.creditLimit-exposure)};}
/** Estimated value of new stays at a block rate: nights × rate + GST. */
export function grpStayValue(s:State,rate:number,a:string,d:string){let v=0;for(let x=a;x<d;x=addDays(x,1))v+=rate;return v+gstStayTax(s,rate,a,d);}

/** Read-only master folio: each member stay with its folio totals, and the group sum. */
export function grpMasterFolio(s:State,blk:GRPBlock){
  const rows=grpMembers(s,blk).map(m=>{const t=totals(m.b);return {id:m.b.id,guest:m.b.guest,room:m.b.room,type:m.type,status:m.b.status,rate:m.b.rate,net:t.net,tax:t.tax,total:t.total,paid:t.paid,balance:t.balance,estimate:grpBookingExposure(s,m.b)};});
  const live=rows.filter(r=>GRP_ACTIVE.includes(r.status));
  const sum=(k:'net'|'tax'|'total'|'paid'|'balance'|'estimate')=>live.reduce((a,r)=>a+r[k],0);
  return {rows,totals:{net:sum('net'),tax:sum('tax'),total:sum('total'),paid:sum('paid'),balance:sum('balance'),estimate:sum('estimate')}};
}
/** Pickup report rows: every block × room type. */
export function grpPickupReport(s:State){return grpSlice(s).blocks.flatMap(blk=>grpLines(s,blk).map(l=>({block:blk,status:grpStatus(s,blk),...l})));}
export function grpKpis(s:State){
  const x=grpSlice(s),open=x.blocks.filter(b=>grpStatus(s,b)==='Open');
  const t=open.map(b=>grpBlockTotals(s,b));
  return {open:open.length,allot:t.reduce((a,b)=>a+b.allot,0),picked:t.reduce((a,b)=>a+b.picked,0),remaining:t.reduce((a,b)=>a+b.remaining,0),
    dueSoon:open.filter(b=>grpDaysToCutoff(s,b)<=2).length,atRisk:t.filter(b=>b.atRisk).length,
    creditAlerts:x.companies.filter(c=>grpCredit(s,c).level==='over'||grpCredit(s,c).level==='near').length,pendingLimits:x.requests.filter(r=>r.status==='Pending').length};
}
/** GSTIN placeholder shape (same as reservations' RES_GSTIN); fixture values use the fictional "ZZFIC" pattern. */
export const GRP_GSTIN=/^\d{2}[A-Z]{5}\d{4}[A-Z][A-Z\d]Z[A-Z\d]$/;
export const GRP_MAX_LIMIT=1000000000; // ₹1 crore in paise
