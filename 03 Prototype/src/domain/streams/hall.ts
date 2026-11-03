// Module 27 · Hall booking (F6, spec 02 Planning/modules/HCC_Module_27_Hall_Booking_Spec.md, contract 02 Planning/agents/F6-CONTRACT.md §2).
// Types for every F6 lane (declared by the H1 setup commit; H2 / H3 import, never redeclare) and the pure H1 rules: slice access with the
// lazy seed, slot overlap, calendar clash (combined halls expand to their parts), soft holds, enquiry auto-lost, quotation expiry reminders.
// Money lives in hall-billing.ts. State slice s.hallX (optional; absent = the seed from fixtures/ext/hall.ts, SCHEMA 14).
import type {State,Role} from '../types';
import {addDays} from '../money';
import {hallSeed} from '../../fixtures/ext/hall';

export type HallSlot='MORNING'|'AFTERNOON'|'EVENING'|'FULL_DAY';
export type HallSetup='THEATRE'|'ROUND'|'USHAPE'|'FLOATING'|'CLASSROOM';
export type HallEventType='WEDDING'|'RECEPTION'|'BIRTHDAY'|'ENGAGEMENT'|'CORPORATE'|'CONFERENCE'|'RELIGIOUS'|'OTHER';
/** GST class names of the hall lines (a class, never a rate: the rate comes from the dated GST master at the invoice date, hall-billing.ts). */
export type HallGstClass='HALL_RENT'|'BANQUET_FOOD'|'SERVICE'|'ROOM';
export type VenueSlot={slot:HallSlot;from:string;to:string;ratePaise:number;minPlates?:number};
export type Venue={id:string;name:string;kind:'HALL'|'LAWN'|'TERRACE';capacity:Partial<Record<HallSetup,number>>;slots:VenueSlot[];minPlates?:number;
  gstClass:string;combinedOf?:string[];active:boolean;version:number};
export type HallSoftHold={venueId:string;until:string;by:string};
export type Enquiry={id:string;customerId?:string;name:string;phone:string;partyId?:string;eventType:HallEventType;dates:string[];slot:HallSlot;
  pax:number;venueId?:string;source:'WALKIN'|'PHONE'|'REFERENCE'|'WEBSITE'|'REPEAT';followUp:string;note?:string;
  status:'OPEN'|'QUOTED'|'WON'|'LOST';lostReasonId?:string;lostNote?:string;softHold?:HallSoftHold;createdAt?:string;by?:string;version?:number};
export type QuoteLine={id:string;kind:'HALL'|'MENU'|'EXTRA'|'ROOM'|'OTHER';refId?:string;desc:string;qty:number;unit:string;
  ratePaise:number;listRatePaise:number;discountPaise:number;gstClass:string;/** set at invoice */taxBps?:number};
export type AdvanceRow={id:string;label:string;pctBps?:number;amountPaise?:number;due:string|'ON_ACCEPT'|'EVENT_DAY';paidPaise:number;receiptIds:string[]};
export type TncClause={n:number;heading:string;text:string};
export type TncTemplate={id:string;eventType:HallEventType|'GENERAL';versions:{v:number;effectiveFrom:string;clauses:TncClause[]}[]};
export type TncSnapshot={templateId:string;v:number;clauses:TncClause[];hasPlaceholder:boolean};
export type QuoteVersionStatus='DRAFT'|'SENT'|'SUPERSEDED'|'EXPIRED'|'ACCEPTED'|'LOST'|'CANCELLED';
export type QuoteHeader={eventType:HallEventType;dates:string[];slot:HallSlot;venueId:string;setup:HallSetup;pax:number;guaranteedPax:number};
export type QuoteVersion={v:number;status:QuoteVersionStatus;header:QuoteHeader;lines:QuoteLine[];advance:AdvanceRow[];tnc:TncSnapshot;validUntil:string;
  createdBy:string;createdAt:string;sentAt?:string;placeholderOverride?:{by:string;reason:string;at:string};approvalId?:string};
export type Quotation={id:string;number:string;/* QT/2627/0001 */fy:string;enquiryId:string;versions:QuoteVersion[];currentVersion:number;
  /** customer of the enquiry, copied for prints / invoices */customer?:{name:string;phone:string;company?:string}};
export type MenuPackage={id:string;name:string;perPlatePaise:number;posItemIds:string[];veg:boolean;active:boolean;version?:number};
/** One money movement of a hall event (H1 billing): advance received, balance settled, refund paid. The accounting source `hall:*` reads these. */
export type HallReceipt={id:string;eventId:string;date:string;kind:'ADVANCE'|'BALANCE'|'REFUND';method?:string;company?:string;udhaar?:boolean;reference?:string;
  amountPaise:number;by:string;at:string};
/** Tax invoice of an event (series HPE, DEC-197): lines with their taxBps snapshot, totals, the advances applied and how the balance was settled. */
export type HallInvoice={no:string;date:string;lines:(QuoteLine&{taxBps:number;taxablePaise:number;cgstPaise:number;sgstPaise:number})[];
  taxablePaise:number;cgstPaise:number;sgstPaise:number;totalPaise:number;advanceAppliedPaise:number;balancePaise:number;
  billedPax:number;settle:{mode:'TENDER'|'COMPANY'|'UDHAAR'|'DUE';method?:string;company?:string;reference?:string};by:string;at:string};
export type HallCancellation={at:string;date:string;keptPaise:number;refundPaise:number;by:string;daysBefore:number;keptBps:number;taxBps:number;
  refund:'NONE'|'PENDING'|'PAID';refundBy?:string;refundAt?:string;refundMethod?:string;reason:string};
export type HallEvent={id:string;quotationId:string;version:number;venueIds:string[];date:string;/** further dates of a multi-day booking (held too) */extraDates?:string[];
  slot:HallSlot;status:'CONFIRMED'|'BILLED'|'CANCELLED';lines:QuoteLine[];actualPax?:number;guaranteedPax?:number;extras:QuoteLine[];blockId?:string;invoiceId?:string;
  cancellation?:HallCancellation;
  // H1 billing additions (contract §5): the accepted advance plan, the receipts, the invoice, the customer for prints.
  advance?:AdvanceRow[];receipts?:HallReceipt[];invoice?:HallInvoice;customer?:{name:string;phone:string;company?:string};eventType?:HallEventType;createdAt?:string;by?:string};
export type HallLostReason={id:string;label:string;needsNote:boolean};
export type HallCancelSlab={minDays:number;keptBps:number};
export type HallSettings={validityDays:number;expiryReminderDays:number;advanceDefault:[number,number,'BALANCE'];secondDueDays:number;
  discountLimitBps:{RECEPTION:number;MANAGER:number;OWNER:number|null};refundManagerLimitPaise:number;
  cancelSlabs:HallCancelSlab[];paxReduceFreeBps:number;enquiryAutoLostDays:number;enquiryWarnDays?:number;softHoldMaxDays?:number;
  /** Q6 mapping switch hall.foodLedger: food in the hall to 4200 F&B (default) or 4400 with the hall income. */foodLedger?:'4200'|'4400'};
export type HallState={venues:Venue[];enquiries:Enquiry[];quotations:Quotation[];templates:TncTemplate[];menus:MenuPackage[];events:HallEvent[];
  lostReasons:HallLostReason[];settings:HallSettings;seq?:number};

export const HALL_SLOTS:HallSlot[]=['MORNING','AFTERNOON','EVENING','FULL_DAY'];
export const HALL_EVENT_TYPES:HallEventType[]=['WEDDING','RECEPTION','BIRTHDAY','ENGAGEMENT','CORPORATE','CONFERENCE','RELIGIOUS','OTHER'];
export const HALL_PLACEHOLDER='OWNER TO REPLACE';
export const hallHasPlaceholder=(clauses:readonly TncClause[])=>clauses.some(c=>c.text.includes(HALL_PLACEHOLDER)||c.heading.includes(HALL_PLACEHOLDER));

/** The slice (read-only view): the stored one, else the seed. */
export const hallSlice=(s:State):HallState=>s.hallX??hallSeed();
/** The slice for a handler (materialises the seed on first write). */
export function hallEnsure(s:State):HallState{
  if(!s.hallX)s.hallX=hallSeed();const x=s.hallX;
  x.venues??=[];x.enquiries??=[];x.quotations??=[];x.templates??=[];x.menus??=[];x.events??=[];x.lostReasons??=[];x.seq??=0;return x;
}
export const hallNextId=(x:HallState,prefix:string)=>`${prefix}-${String(x.seq=(x.seq??0)+1).padStart(4,'0')}`;

// ── Roles (contract §4; the prototype checks roles, the server PERM-HAL-001…006) ──
export const HALL_ROLES={
  enquiry:['Owner','Manager','Reception'] as Role[], // PERM-HAL-001
  quote:['Owner','Manager','Reception'] as Role[], // PERM-HAL-002
  accept:['Owner','Manager','Reception'] as Role[], // PERM-HAL-003
  advance:['Owner','Manager','Reception','Cashier'] as Role[], // PERM-HAL-003 (Cashier advances only)
  bill:['Owner','Manager','Reception','Cashier'] as Role[], // PERM-HAL-004 (Cashier settles)
  cancel:['Owner','Manager','Reception'] as Role[], // PERM-HAL-004 (Reception = request)
  refund:['Owner','Manager'] as Role[], // approve
  softHold:['Owner','Manager'] as Role[],
  venues:['Owner'] as Role[], // PERM-HAL-006
  menus:['Owner','Manager'] as Role[],
  money:['Owner','Manager','Reception','Cashier','Accountant'] as Role[], // may see *Paise (DEC-061: never HK / maintenance)
};
export const hallSeesMoney=(role:Role)=>HALL_ROLES.money.includes(role);

// ── Calendar ──
/** Two slots of the same day overlap: FULL_DAY overlaps every slot; the others only themselves. */
export const hallSlotsOverlap=(a:HallSlot,b:HallSlot)=>a==='FULL_DAY'||b==='FULL_DAY'||a===b;
/** A venue with its parts (a combined venue blocks every hall in it) and every combination that contains it. */
export function hallVenueFootprint(x:HallState,venueId:string):string[]{
  const v=x.venues.find(v=>v.id===venueId);const parts=v?.combinedOf?.length?v.combinedOf:[venueId];
  const out=new Set<string>([venueId,...parts]);
  for(const c of x.venues)if(c.combinedOf?.some(p=>parts.includes(p)))out.add(c.id);
  return [...out];
}
const eventDates=(e:HallEvent)=>[e.date,...(e.extraDates??[])];
export type HallClash={eventId:string;venueId:string;date:string;slot:HallSlot};
/** Hard holds (events not cancelled) that clash with venue × dates × slot. */
export function hallClashes(x:HallState,venueIds:readonly string[],dates:readonly string[],slot:HallSlot,exceptEvent=''):HallClash[]{
  const foot=new Set(venueIds.flatMap(v=>hallVenueFootprint(x,v)));const out:HallClash[]=[];
  for(const e of x.events){
    if(e.id===exceptEvent||e.status==='CANCELLED'||!hallSlotsOverlap(e.slot,slot))continue;
    const hit=e.venueIds.find(v=>hallVenueFootprint(x,v).some(p=>foot.has(p)));if(!hit)continue;
    for(const d of eventDates(e))if(dates.includes(d))out.push({eventId:e.id,venueId:hit,date:d,slot:e.slot});
  }
  return out;
}
/** Soft holds of open enquiries on an overlapping venue / date / slot (warn only, never block). */
export function hallSoftHoldsOn(x:HallState,venueIds:readonly string[],dates:readonly string[],slot:HallSlot,clock:string,exceptEnquiry=''):Enquiry[]{
  const foot=new Set(venueIds.flatMap(v=>hallVenueFootprint(x,v)));
  return x.enquiries.filter(q=>q.id!==exceptEnquiry&&q.softHold&&q.softHold.until>clock&&(q.status==='OPEN'||q.status==='QUOTED')&&hallSlotsOverlap(q.slot,slot)
    &&hallVenueFootprint(x,q.softHold.venueId).some(p=>foot.has(p))&&q.dates.some(d=>dates.includes(d)));
}
/** Calendar rows for a date range (both ends inclusive): one row per venue × date × slot held. No money (safe for every role). */
export type HallCalendarRow={date:string;venueId:string;slot:HallSlot;kind:'EVENT'|'SOFT_HOLD';ref:string;label:string;status:string};
export function hallCalendar(s:State,from:string,to:string):HallCalendarRow[]{
  const x=hallSlice(s),out:HallCalendarRow[]=[];
  for(const e of x.events){if(e.status==='CANCELLED')continue;
    for(const d of eventDates(e))if(d>=from&&d<=to)for(const v of e.venueIds)out.push({date:d,venueId:v,slot:e.slot,kind:'EVENT',ref:e.id,label:`${e.customer?.name??e.id} · ${e.eventType??''}`.trim(),status:e.status==='BILLED'?'Event day':'Confirmed'});}
  for(const q of x.enquiries){if(!q.softHold||q.softHold.until<=s.clock||(q.status!=='OPEN'&&q.status!=='QUOTED'))continue;
    for(const d of q.dates)if(d>=from&&d<=to)out.push({date:d,venueId:q.softHold.venueId,slot:q.slot,kind:'SOFT_HOLD',ref:q.id,label:`${q.name} · ${q.eventType}`,status:'Enquiry hold'});}
  return out.sort((a,b)=>a.date.localeCompare(b.date)||a.venueId.localeCompare(b.venueId)||HALL_SLOTS.indexOf(a.slot)-HALL_SLOTS.indexOf(b.slot));
}
/** Capacity of a venue for a setup (combined: the sum of the parts when the combination has no own figure). */
export function hallCapacity(x:HallState,venueId:string,setup:HallSetup):number|undefined{
  const v=x.venues.find(v=>v.id===venueId);if(!v)return undefined;const own=v.capacity[setup];if(own!==undefined)return own;
  if(!v.combinedOf?.length)return undefined;let t=0;for(const p of v.combinedOf){const c=hallCapacity(x,p,setup);if(c===undefined)return undefined;t+=c;}return t;
}

// ── Enquiry life cycle ──
export const HALL_ENQUIRY_NEXT:Record<Enquiry['status'],Enquiry['status'][]>={OPEN:['QUOTED','WON','LOST'],QUOTED:['WON','LOST','OPEN'],WON:[],LOST:['OPEN']};
/** Enquiries with no reply for `enquiryAutoLostDays` after the follow-up date (auto-lost at the night run) and those `enquiryWarnDays` before it. */
export function hallEnquiriesDue(s:State){
  const x=hallSlice(s),days=x.settings.enquiryAutoLostDays,warn=x.settings.enquiryWarnDays??3;
  const open=x.enquiries.filter(q=>q.status==='OPEN'||q.status==='QUOTED');
  return {lost:open.filter(q=>addDays(q.followUp,days)<=s.date),warn:open.filter(q=>{const d=addDays(q.followUp,days);return d>s.date&&addDays(q.followUp,days-warn)<=s.date;}),
    today:open.filter(q=>q.followUp===s.date)};
}
export const HALL_NO_RESPONSE='LR-NORESPONSE';
/** Quotations whose latest SENT version expires within the reminder window (for the person who sent it) or has expired. */
export function hallQuotesExpiring(s:State){
  const x=hallSlice(s),r=x.settings.expiryReminderDays;const latest=(q:Quotation)=>q.versions.find(v=>v.v===q.currentVersion);
  const sent=x.quotations.map(q=>({q,v:latest(q)})).filter((p):p is {q:Quotation;v:QuoteVersion}=>!!p.v&&p.v.status==='SENT');
  return {expired:sent.filter(p=>p.v.validUntil<s.date),reminder:sent.filter(p=>p.v.validUntil>=s.date&&addDays(p.v.validUntil,-r)<=s.date)};
}
export const hallLatest=(q:Quotation)=>q.versions.find(v=>v.v===q.currentVersion)!;
/** Days from `date` to the event (negative after it). */
export const hallDaysBefore=(date:string,eventDate:string)=>Math.round((Date.parse(eventDate+'T00:00:00Z')-Date.parse(date+'T00:00:00Z'))/86400000);
