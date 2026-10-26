// Module 14 · Group & corporate — pure helpers, selectors and the state slice type. Exported names MUST start with 'grp' / 'GRP' (shared mock namespace).
// Company accounts (negotiated rate, credit limit), group blocks (allotment per room type, cut-off, auto-release),
// rooming-list pickup, pickup report and a read-only master folio. See 02 Planning/modules/HCC_Module_14_Group_Corporate_Spec.md.
import type {State,Booking} from '../types';
import {available,availableRaw,registerRoomHold,totals} from '../selectors';
import {gstForRoomNight,gstLineTax,gstSplit,gstStayTax} from '../gst';
import {addDays,nights,money} from '../money';

export type GRPCompany={id:string;name:string;
  /** GSTIN-format placeholder — always fictional in this prototype. */
  gstin:string;contact:string;phone:string;email:string;city:string;
  /** Negotiated nightly rate per room type (paise). Missing type = rack rate. */
  rates:Record<string,number>;
  /** Credit limit in paise. DEC-076: 0 = no credit (nothing may be billed to the account on credit); set by the Owner only. */
  creditLimit:number;
  /** Receivable carried in from before this demo (paise, simulated AR). */
  openingOutstanding:number;
  limitHistory?:{from:number;to:number;at:string;actor:string;reason:string;request?:string}[];
  /** Production only: the server's credit figures (GET /groups/companies); grpCredit() shows these instead of the local estimate. */
  credit?:GRPServerCredit;
};
/** The server's credit picture of a company (DEC-076; paise). Items: receivable | payment | stay | group with a booking / invoice / group reference. */
export type GRPServerCredit={exposure:number;receivable:number;openStays:number;groupMasters:number;remaining:number;items:{kind:string;id:string;reference:string;amount:number}[]};
/** Money received from a company against its company-credit receivable (DEC-076): non-cash, never more than it owes; frees credit. */
export type GRPCompanyPayment={id:string;company:string;amount:number;method:string;reference:string;invoice?:string;date:string;actor:string;at:string};
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
/** State slice for this module, stored at s.groupsX (optional; absent in old saves → treat as empty). `overrides` = credit-limit overrides
 *  recorded before DEC-076 (history only: a refusal can no longer be overridden). `payments` (DEC-076) is absent in old saves. */
export type GRPState={companies:GRPCompany[];blocks:GRPBlock[];requests:GRPLimitRequest[];overrides:GRPOverride[];payments?:GRPCompanyPayment[]};
export const grpEmpty=():GRPState=>({companies:[],blocks:[],requests:[],overrides:[],payments:[]});
export const grpSlice=(s:State):GRPState=>{const x=s.groupsX as Partial<GRPState>|undefined;
  return {companies:x?.companies??[],blocks:x?.blocks??[],requests:x?.requests??[],overrides:x?.overrides??[],payments:x?.payments??[]};};

// ── DEC-080 (audit A02b-04): no group blocks in production until pickup, release, cancel and re-allot exist ──
/** Shown by the production client (and by the server, GroupBlockOptions.RefusalMessage, option Hcc:GroupBlocks:Enabled, default off). The demo build is unchanged. */
export const GRP_BLOCKS_REFUSAL='Group blocks are not available yet: pickup and release are still being built. Book the rooms as separate reservations.';
let blocksOnServer=false;
/** Whether the production client sends GRP_BLOCK_CREATE. Off until the server option is switched on: then change the default here (or call grpSetBlocksOnServer). */
export const grpBlocksOnServer=()=>blocksOnServer;
export function grpSetBlocksOnServer(on:boolean){blocksOnServer=on;}
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
/** Bookings billed to a company account (DEC-076: an explicit link, as on the server): members of its bill-to-company blocks plus
 *  bookings whose company names the account (`company.account`). A company typed by name only is invoice details, not billed to it. */
export function grpCompanyBookings(s:State,c:GRPCompany):Booking[]{
  const ids=new Set<string>();
  for(const blk of grpSlice(s).blocks)if(blk.company===c.id&&blk.billTo==='company')blk.members.forEach(m=>ids.add(m.booking));
  for(const b of s.bookings)if(b.company?.account===c.id)ids.add(b.id);
  return s.bookings.filter(b=>ids.has(b.id));
}
/** The company account a booking is billed to (its own link, else its bill-to-company block), or undefined. */
export function grpAccountOf(s:State,b:Booking):GRPCompany|undefined{
  if(b.company?.account)return grpCompany(s,b.company.account);
  const blk=grpSlice(s).blocks.find(x=>x.billTo==='company'&&x.company&&x.members.some(m=>m.booking===b.id));
  return blk?grpCompany(s,blk.company):undefined;
}
/** Money received from the company (DEC-076). */
export const grpPaid=(s:State,c:GRPCompany)=>(grpSlice(s).payments??[]).filter(p=>p.company===c.id).reduce((a,p)=>a+p.amount,0);
/** Company-credit receivable: opening outstanding + company credit given at checkout on its bookings − payments received (never below 0). */
export function grpReceivable(s:State,c:GRPCompany){
  const credit=grpCompanyBookings(s,c).reduce((a,b)=>a+b.receipts.filter(r=>r.kind==='company-credit').reduce((x,r)=>x+r.amount,0),0);
  return Math.max(0,c.openingOutstanding+credit-grpPaid(s,c));
}
/**
 * DEC-076 exposure (paise), as the server defines it: the receivable (opening outstanding + company credit − payments) plus every live
 * booking billed to the company (Held, Confirmed, In house: posted + agreed unposted nights + GST − what the guest paid; grpBookingExposure).
 * A departed stay counts only through the company credit it left.
 */
export function grpExposure(s:State,c:GRPCompany){
  return grpReceivable(s,c)+grpCompanyBookings(s,c).filter(b=>b.status!=='Departed').reduce((a,b)=>a+grpBookingExposure(s,b),0);
}
export type GRPCreditLevel='ok'|'near'|'over'|'none';
export function grpCreditLevel(limit:number,exposure:number):GRPCreditLevel{if(limit<=0)return exposure>0?'over':'none';return exposure>limit?'over':exposure>=limit*0.8?'near':'ok';}
export const GRP_CREDIT_LABEL:Record<GRPCreditLevel,string>={ok:'Within limit',near:'Near limit (≥ 80%)',over:'Over limit',none:'No credit'};
/** Credit picture of a company: exposure (used), receivable, open stays, limit, % used, level and what remains. Production: the server's figures. */
export function grpCredit(s:State,c:GRPCompany){
  const exposure=c.credit?c.credit.exposure:grpExposure(s,c),receivable=c.credit?c.credit.receivable:grpReceivable(s,c);
  return {exposure,receivable,stays:exposure-receivable,limit:c.creditLimit,
    pct:c.creditLimit?Math.round(exposure/c.creditLimit*100):0,level:grpCreditLevel(c.creditLimit,exposure),available:Math.max(0,c.creditLimit-exposure)};
}
/**
 * DEC-076 refusal text (same wording and figures as the server's CompanyCreditRules): undefined when `used + needed` fits the limit.
 * `used` = everything else billed to the company; `needed` = what this booking / stay / list adds. No override: the Owner raises the limit.
 */
export function grpCreditRefusal(c:GRPCompany,used:number,needed:number,what:string):string|undefined{
  if(!(needed>0&&used+needed>c.creditLimit))return undefined;
  return `Credit limit exceeded for ${c.name}: limit ${money(c.creditLimit)}${c.creditLimit===0?' (no credit)':''}, used ${money(used)}, remaining ${money(Math.max(0,c.creditLimit-used))}; `
    +`this ${what} needs ${money(needed)}. Only the Owner can raise the limit (Groups & corporate › Companies › Credit limit).`;
}
/**
 * DEC-076 preview for a booking (the command decides): its company account, what the rest of the company's business uses (this stay
 * excluded) and the refusal `needed` would get. Production: the server's figures (the stay's own line from the server's breakdown).
 */
export function grpCreditPreview(s:State,b:Booking,needed:number,what='stay on company credit'):{c?:GRPCompany;used:number;refusal?:string}{
  const c=grpAccountOf(s,b);if(!c)return {used:0};
  const own=c.credit?(c.credit.items.find(i=>i.kind==='stay'&&i.id===b.id)?.amount??0):b.status!=='Departed'?grpBookingExposure(s,b):0;
  const used=Math.max(0,grpCredit(s,c).exposure-own);
  return {c,used,refusal:grpCreditRefusal(c,used,needed,what)};
}
/** Tenders a company may pay with (DEC-076: no cash on company credit; a cheque is recorded as a bank transfer). */
export const GRP_PAY_METHODS=['Bank transfer','UPI','Card'] as const;
/** Estimated value of new stays at a block rate: nights × rate + GST. */
export function grpStayValue(s:State,rate:number,a:string,d:string){let v=0;for(let x=a;x<d;x=addDays(x,1))v+=rate;return v+gstStayTax(s,rate,a,d);}

// ── Attrition (server rule GroupAttrition, DEC-074 / A03a-06) ──
// The prototype charges no attrition (it only releases unpicked rooms at the cut-off); the production server does. These pure helpers mirror
// the server rule so the GST basis of an attrition / retention charge has one definition on each side.
export type GRPAttritionLine={type:string;night:string;rooms:number;
  /** Retention price per room-night = block rate × attrition % (half away from zero). */ perRoomNight:number;amount:number;
  /** The contracted per-night block rate the line was taken from. */ blockRate:number};
/** Server GroupAttrition.Lines: per room type (ordinal order), unpicked rooms beyond ⌊contracted × allowance⌋ are charged every night of the block. */
export function grpAttritionLines(types:{type:string;contracted:number;picked:number;rate:number}[],arrival:string,departure:string,allowanceBps:number,rateBps:number):GRPAttritionLine[]{
  const out:GRPAttritionLine[]=[];
  for(const t of [...types].sort((a,b)=>a.type<b.type?-1:a.type>b.type?1:0)){
    const free=Math.floor(t.contracted*allowanceBps/10000),rooms=Math.max(0,Math.max(0,t.contracted-t.picked)-free);
    const per=gstLineTax(t.rate,rateBps); // rate × % with the same half-away-from-zero rounding as the server's MoneyMath.PercentOf
    if(!rooms||per<=0)continue;
    for(let n=arrival;n<departure;n=addDays(n,1))out.push({type:t.type,night:n,rooms,perRoomNight:per,amount:per*rooms,blockRate:t.rate});
  }
  return out;
}
/** DEC-074 / A03a-06: the room slab of an attrition / retention charge comes from the CONTRACTED per-night block rate — a ₹9,000 block at 50 %
 *  charges ₹4,500 but is taxed at the ₹9,000 night's 18 %, not 5 %. CA G7 to confirm: the one place to change the basis (server GroupAttrition.GstSlabBasis). */
export const grpAttritionSlabBasis=(l:GRPAttritionLine)=>l.blockRate;
/** GST of one attrition line: the GST master's room rate for the slab basis on that night; tax on the line, CGST + SGST split as every line. */
export function grpAttritionGst(s:State,l:GRPAttritionLine){const rate=gstForRoomNight(s,l.night,grpAttritionSlabBasis(l)),tax=gstLineTax(l.amount,rate.bps);return {...rate,tax,...gstSplit(tax)};}

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
