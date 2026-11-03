// F6-H3 · Hall booking — pure UI state (no React): enquiry board, quotation / event rows, calendar month + day grid with clash display, quotation draft
// helpers, function sheet (BEO) and print models. Money is computed only by hall-billing.ts (one truth table); the screen and the print read the same
// functions, so printed totals = screen totals. Nothing here mutates state; commands go through execute (handlers/hall.ts and H2's QUO_* handlers).
import type {State} from '../../domain/types';
import {addDays} from '../../domain/money';
import {hallSlice,hallCalendar,hallClashes,hallSoftHoldsOn,hallCapacity,hallEnquiriesDue,hallQuotesExpiring,hallLatest,hallHasPlaceholder,hallVenueFootprint,
  HALL_SLOTS,type HallState,type HallSlot,type HallSetup,type HallEventType,type Enquiry,type Quotation,type QuoteVersion,type QuoteLine,type QuoteHeader,type AdvanceRow,
  type HallEvent,type Venue,type TncSnapshot,type TncTemplate,type HallReceipt} from '../../domain/streams/hall';
import {hallPrice,hallAdvancePlan,hallPlanAmounts,hallPaid,hallBillLines,hallBilledPax,hallDiscountBps,hallDiscountLimit,hallDiscountWithin,type HallTotals} from '../../domain/streams/hall-billing';
import {hallRoleOf} from './access';

export const SLOT_LABEL:Record<HallSlot,string>={MORNING:'Morning',AFTERNOON:'Afternoon',EVENING:'Evening',FULL_DAY:'Full day'};
export const SETUP_LABEL:Record<HallSetup,string>={THEATRE:'Theatre',ROUND:'Round tables',USHAPE:'U-shape',FLOATING:'Floating',CLASSROOM:'Classroom'};
export const EVENT_LABEL:Record<HallEventType,string>={WEDDING:'Wedding',RECEPTION:'Reception',BIRTHDAY:'Birthday',ENGAGEMENT:'Engagement',CORPORATE:'Corporate',CONFERENCE:'Conference',RELIGIOUS:'Religious',OTHER:'Other'};
export const SOURCE_LABEL:Record<Enquiry['source'],string>={WALKIN:'Walk-in',PHONE:'Phone',REFERENCE:'Reference',WEBSITE:'Website',REPEAT:'Repeat guest'};
export const QUOTE_TONE:Record<string,string>={DRAFT:'neutral',SENT:'blue',ACCEPTED:'green',SUPERSEDED:'neutral',EXPIRED:'orange',LOST:'red',CANCELLED:'red'};
export const ENQ_TONE:Record<Enquiry['status'],string>={OPEN:'blue',QUOTED:'orange',WON:'green',LOST:'red'};
export const EVENT_TONE:Record<HallEvent['status'],string>={CONFIRMED:'blue',BILLED:'green',CANCELLED:'red'};
export const venueName=(x:HallState,id?:string)=>x.venues.find(v=>v.id===id)?.name??id??'—';
export const dayLabel=(d:string)=>d?`${d.slice(8,10)}/${d.slice(5,7)}/${d.slice(0,4)}`:'';

// ── Enquiries ──
export const ENQ_COLUMNS:Enquiry['status'][]=['OPEN','QUOTED','WON','LOST'];
export function hallBoard(s:State){
  const x=hallSlice(s),due=hallEnquiriesDue(s),today=new Set(due.today.map(q=>q.id)),late=new Set(x.enquiries.filter(q=>(q.status==='OPEN'||q.status==='QUOTED')&&q.followUp<s.date).map(q=>q.id));
  const by=(st:Enquiry['status'])=>x.enquiries.filter(q=>q.status===st).sort((a,b)=>{
    const fa=today.has(a.id)||late.has(a.id)?0:1,fb=today.has(b.id)||late.has(b.id)?0:1;return fa-fb||a.followUp.localeCompare(b.followUp)||a.id.localeCompare(b.id);});
  return {columns:ENQ_COLUMNS.map(st=>({status:st,items:by(st)})),followUpsToday:due.today,overdue:x.enquiries.filter(q=>late.has(q.id)),warn:due.warn,
    isDue:(q:Enquiry)=>today.has(q.id)||late.has(q.id)};
}

// ── Quotations ──
export type QuoteRow={q:Quotation;v:QuoteVersion;enquiry?:Enquiry;customer:string;totalPaise:number;venue:string;date:string};
export function hallQuoteRows(s:State,status='',search=''):QuoteRow[]{
  const x=hallSlice(s),needle=search.trim().toLowerCase();
  return x.quotations.map(q=>{const v=hallLatest(q),enquiry=x.enquiries.find(e=>e.id===q.enquiryId);
    return {q,v,enquiry,customer:q.customer?.name??enquiry?.name??'—',totalPaise:hallPrice(s,v.lines,s.date).totals.totalPaise,venue:venueName(x,v.header.venueId),date:v.header.dates[0]??''};})
    .filter(r=>(!status||r.v.status===status)&&(!needle||`${r.q.number} ${r.customer} ${r.venue}`.toLowerCase().includes(needle)))
    .sort((a,b)=>b.q.number.localeCompare(a.q.number));
}
export const hallQuoteTotals=(s:State,lines:readonly QuoteLine[],date=s.date):HallTotals=>hallPrice(s,lines,date).totals;

/** Editor state of a quotation version (not yet saved). */
export type QuoteDraft={enquiryId:string;header:QuoteHeader;lines:QuoteLine[];advance:AdvanceRow[];validUntil:string;tncTemplateId?:string};
export const hallDraftFromVersion=(q:Quotation,v:QuoteVersion):QuoteDraft=>({enquiryId:q.enquiryId,header:{...v.header,dates:[...v.header.dates]},lines:v.lines.map(l=>({...l})),advance:v.advance.map(r=>({...r})),validUntil:v.validUntil,tncTemplateId:v.tnc.templateId});
export function hallDraftFromEnquiry(s:State,enq:Enquiry):QuoteDraft{
  const x=hallSlice(s),venue=x.venues.find(v=>v.id===enq.venueId&&v.active)??x.venues.find(v=>v.active&&!v.combinedOf);
  const header:QuoteHeader={eventType:enq.eventType,dates:[...enq.dates],slot:enq.slot,venueId:venue?.id??'',setup:'ROUND',pax:enq.pax,guaranteedPax:enq.pax};
  const d:QuoteDraft={enquiryId:enq.id,header,lines:[],advance:[],validUntil:addDays(s.date,x.settings.validityDays)};
  return hallSyncLines(s,d);
}
/** The HALL line follows the venue × slot rate; MENU plate lines follow the guaranteed pax. Other lines are the user's. */
export function hallSyncLines(s:State,d:QuoteDraft):QuoteDraft{
  const x=hallSlice(s),v=x.venues.find(v=>v.id===d.header.venueId),slot=v?.slots.find(o=>o.slot===d.header.slot),days=Math.max(1,d.header.dates.length);
  let lines=d.lines.filter(l=>l.kind!=='HALL');
  if(v){const rate=slot?.ratePaise??0;lines=[{id:'L-HALL',kind:'HALL',refId:v.id,desc:`${v.name} · ${SLOT_LABEL[d.header.slot]}`,qty:days,unit:days>1?'days':'slot',ratePaise:rate,listRatePaise:rate,discountPaise:0,gstClass:v.gstClass||'HALL_RENT'},...lines];}
  const keepHall=d.lines.find(l=>l.kind==='HALL');if(keepHall&&v&&keepHall.refId===v.id&&lines[0]?.kind==='HALL'&&keepHall.ratePaise<lines[0].listRatePaise)lines[0]={...lines[0],ratePaise:keepHall.ratePaise}; // a discount stays
  lines=lines.map(l=>l.kind==='MENU'&&l.unit==='plate'?{...l,qty:d.header.guaranteedPax}:l);
  return {...d,lines};
}
export function hallAddMenu(s:State,d:QuoteDraft,menuId:string):QuoteDraft{
  const m=hallSlice(s).menus.find(m=>m.id===menuId&&m.active);if(!m)return d;
  return {...d,lines:[...d.lines.filter(l=>!(l.kind==='MENU'&&l.refId===m.id)),{id:'L-'+m.id,kind:'MENU',refId:m.id,desc:`${m.name} per plate`,qty:d.header.guaranteedPax,unit:'plate',ratePaise:m.perPlatePaise,listRatePaise:m.perPlatePaise,discountPaise:0,gstClass:'BANQUET_FOOD'}]};
}
let seq=0;
export function hallAddExtra(d:QuoteDraft,desc:string,qty:number,ratePaise:number,gstClass='SERVICE'):QuoteDraft{
  return {...d,lines:[...d.lines,{id:'L-X'+(++seq)+Date.now().toString(36),kind:'EXTRA',desc,qty,unit:'each',ratePaise,listRatePaise:ratePaise,discountPaise:0,gstClass}]};
}
export const hallSetRate=(d:QuoteDraft,id:string,ratePaise:number):QuoteDraft=>({...d,lines:d.lines.map(l=>l.id===id?{...l,ratePaise:Math.max(0,ratePaise)}:l)});
export const hallSetQty=(d:QuoteDraft,id:string,qty:number):QuoteDraft=>({...d,lines:d.lines.map(l=>l.id===id?{...l,qty:Math.max(1,Math.round(qty))}:l)});
export const hallRemoveLine=(d:QuoteDraft,id:string):QuoteDraft=>({...d,lines:d.lines.filter(l=>l.id!==id)});
/** Default advance plan for the draft's total (spec §6.4); the editor shows it and may keep a hand-edited one. */
export function hallDraftPlan(s:State,d:QuoteDraft):AdvanceRow[]{
  const total=hallPrice(s,d.lines,s.date).totals.totalPaise;
  return d.advance.length?hallPlanAmounts(total,d.advance):hallAdvancePlan(total,d.header.dates[0]??s.date,s.date,hallSlice(s).settings);
}
export function hallDiscountState(s:State,uid:string,lines:readonly QuoteLine[]){
  const role=hallRoleOf(s,uid),limit=role?hallDiscountLimit(hallSlice(s).settings,role):0,bps=hallDiscountBps(lines);
  return {bps,limitBps:limit,within:hallDiscountWithin(lines,limit)};
}
/** Template for an event type (its own, else GENERAL) and the version in force on `date`, as a snapshot (the server / H2 stores the snapshot on SEND). */
export function hallTncFor(s:State,eventType:HallEventType,date=s.date,templateId?:string):TncSnapshot|undefined{
  const x=hallSlice(s),t:TncTemplate|undefined=x.templates.find(t=>t.id===templateId)??x.templates.find(t=>t.eventType===eventType)??x.templates.find(t=>t.eventType==='GENERAL');
  if(!t)return undefined;
  const vs=[...t.versions].sort((a,b)=>b.v-a.v),v=vs.find(v=>v.effectiveFrom<=date)??vs[0];if(!v)return undefined;
  return {templateId:t.id,v:v.v,clauses:v.clauses,hasPlaceholder:hallHasPlaceholder(v.clauses)};
}

// ── Events ──
export type EventRow={e:HallEvent;customer:string;venue:string;totalPaise:number;paidPaise:number;balancePaise:number;pax:number};
export function hallEventRows(s:State,filter:'upcoming'|'billed'|'cancelled'|'all'='upcoming',search=''):EventRow[]{
  const x=hallSlice(s),needle=search.trim().toLowerCase();
  return x.events.filter(e=>filter==='all'||(filter==='upcoming'?e.status==='CONFIRMED':filter==='billed'?e.status==='BILLED':e.status==='CANCELLED'))
    .map(e=>{const priced=e.invoice?e.invoice.totalPaise:hallPrice(s,hallBillLines(e),s.date).totals.totalPaise,paid=hallPaid(e);
      return {e,customer:e.customer?.name??e.id,venue:e.venueIds.map(v=>venueName(x,v)).join(' + '),totalPaise:priced,paidPaise:paid,balancePaise:Math.max(0,priced-paid),pax:hallBilledPax(e)};})
    .filter(r=>!needle||`${r.e.id} ${r.customer} ${r.venue}`.toLowerCase().includes(needle))
    .sort((a,b)=>filter==='billed'||filter==='cancelled'?b.e.date.localeCompare(a.e.date):a.e.date.localeCompare(b.e.date)||a.e.id.localeCompare(b.e.id));
}
/** Bill preview for the event-day screen: lines with actual plates and the extras typed so far. */
export function hallBillPreview(s:State,e:HallEvent,actualPax:number,extras:QuoteLine[]){
  const lines=hallBillLines({...e,actualPax,extras}),priced=hallPrice(s,lines,s.date),paid=hallPaid(e),applied=Math.min(paid,priced.totals.totalPaise);
  return {lines:priced.lines,totals:priced.totals,billedPax:hallBilledPax({guaranteedPax:e.guaranteedPax,actualPax}),paidPaise:paid,appliedPaise:applied,balancePaise:priced.totals.totalPaise-applied,leftPaise:paid-applied};
}

// ── Calendar ──
export type DayCell={date:string;count:number;inMonth:boolean};
export function hallMonthCells(s:State,ym:string):DayCell[]{
  const first=ym+'-01',dow=(new Date(first+'T12:00:00Z').getUTCDay()+6)%7,start=addDays(first,-dow),rows=hallCalendar(s,start,addDays(start,41));
  return Array.from({length:42},(_,i)=>{const date=addDays(start,i);return {date,count:rows.filter(r=>r.date===date).length,inMonth:date.slice(0,7)===ym};});
}
export const hallMonthShift=(ym:string,n:number)=>{const d=new Date(ym+'-01T12:00:00Z');d.setUTCMonth(d.getUTCMonth()+n);return d.toISOString().slice(0,7);};
export type SlotState={venueId:string;slot:HallSlot;state:'FREE'|'CONFIRMED'|'EVENT_DAY'|'HOLD';label:string;ref?:string;via?:string};
const PARTS:HallSlot[]=['MORNING','AFTERNOON','EVENING'];
/** Every active hall × the three parts of the day: Free · Confirmed · Event day · Enquiry hold (colour plus text). A combined hall shows the part that blocks it. */
export function hallDayGrid(s:State,date:string):{venue:{id:string;name:string;combinedOf?:string[]};slots:SlotState[]}[]{
  const x=hallSlice(s);
  return x.venues.filter(v=>v.active).map(full=>{const venue={id:full.id,name:full.name,...(full.combinedOf?{combinedOf:full.combinedOf}:{})};return {venue,slots:PARTS.map(slot=>{
    const hit=hallClashes(x,[venue.id],[date],slot)[0];
    if(hit){const e=x.events.find(e=>e.id===hit.eventId)!;return {venueId:venue.id,slot,state:e.status==='BILLED'||(e.date===s.date&&date===s.date)?'EVENT_DAY' as const:'CONFIRMED' as const,
      label:`${e.customer?.name??e.id}${e.eventType?' · '+EVENT_LABEL[e.eventType]:''}`,ref:e.id,via:hit.venueId!==venue.id?venueName(x,hit.venueId):undefined};}
    const soft=hallSoftHoldsOn(x,[venue.id],[date],slot,s.clock)[0];
    if(soft)return {venueId:venue.id,slot,state:'HOLD' as const,label:`${soft.name} · ${EVENT_LABEL[soft.eventType]}`,ref:soft.id};
    return {venueId:venue.id,slot,state:'FREE' as const,label:'Free'};})};});
}
export const SLOT_STATE_LABEL:Record<SlotState['state'],string>={FREE:'Free',CONFIRMED:'Confirmed',EVENT_DAY:'Event day',HOLD:'Enquiry hold'};
export const SLOT_STATE_TONE:Record<SlotState['state'],string>={FREE:'green',CONFIRMED:'blue',EVENT_DAY:'orange',HOLD:'neutral'};
/** Availability check for venue × dates × slot: hard clashes (refused on accept), soft holds (warn) and the capacity note. */
export function hallAvailability(s:State,venueId:string,dates:string[],slot:HallSlot,pax?:number,setup:HallSetup='ROUND',exceptEvent=''){
  const x=hallSlice(s),clashes=hallClashes(x,[venueId],dates,slot,exceptEvent),soft=hallSoftHoldsOn(x,[venueId],dates,slot,s.clock),cap=hallCapacity(x,venueId,setup);
  return {free:!clashes.length,clashes:clashes.map(c=>({...c,customer:x.events.find(e=>e.id===c.eventId)?.customer?.name??c.eventId,hall:venueName(x,c.venueId)})),soft,
    capacity:cap,overCapacity:cap!==undefined&&pax!==undefined&&pax>cap,
    footprint:hallVenueFootprint(x,venueId).filter(id=>id!==venueId).map(id=>venueName(x,id))};
}

// ── Reminders (page header strip) ──
export function hallAlerts(s:State){
  const d=hallEnquiriesDue(s),q=hallQuotesExpiring(s),x=hallSlice(s);
  const advDue=x.events.filter(e=>e.status==='CONFIRMED').flatMap(e=>(e.advance??[]).filter(r=>(r.amountPaise??0)>r.paidPaise&&r.due!=='ON_ACCEPT'&&r.due!=='EVENT_DAY'&&r.due<=s.date));
  return {followUps:d.today.length,overdue:x.enquiries.filter(e=>(e.status==='OPEN'||e.status==='QUOTED')&&e.followUp<s.date).length,expiring:q.reminder.length,expired:q.expired.length,advancesDue:advDue.length};
}

// ── Function sheet (BEO) and print models ──
export type BeoModel={hotel:string;eventId:string;title:string;date:string;dates:string[];slot:string;time:string;venues:string[];setup?:string;eventType:string;customer:string;phone?:string;
  guaranteedPax:number;actualPax?:number;menu:{desc:string;qty:number;unit:string}[];extras:{desc:string;qty:number;unit:string}[];notes:string[];prices?:{lines:{desc:string;qty:number;rate:number;taxable:number}[];totals:HallTotals;paid:number;balance:number}};
export function hallBeo(s:State,e:HallEvent,withMoney:boolean):BeoModel{
  const x=hallSlice(s),q=x.quotations.find(q=>q.id===e.quotationId),v=q?.versions.find(v=>v.v===e.version),venue=x.venues.find(o=>o.id===e.venueIds[0]);
  const times=venue?.slots.find(o=>o.slot===e.slot),lines=[...e.lines,...e.extras];
  const model:BeoModel={hotel:s.settings.hotel||'Hotel',eventId:e.id,title:`${EVENT_LABEL[e.eventType??'OTHER']} · ${e.customer?.name??e.id}`,date:e.date,dates:[e.date,...(e.extraDates??[])],slot:SLOT_LABEL[e.slot],
    time:times?`${times.from}–${times.to}`:'',venues:e.venueIds.map(id=>venueName(x,id)),setup:v?SETUP_LABEL[v.header.setup]:undefined,eventType:EVENT_LABEL[e.eventType??'OTHER'],
    customer:e.customer?.name??'',phone:e.customer?.phone,guaranteedPax:e.guaranteedPax??0,actualPax:e.actualPax,
    menu:lines.filter(l=>l.kind==='MENU').map(l=>({desc:l.desc,qty:l.unit==='plate'?hallBilledPax(e)||l.qty:l.qty,unit:l.unit})),
    extras:lines.filter(l=>l.kind==='EXTRA'||l.kind==='OTHER'||l.kind==='ROOM').map(l=>({desc:l.desc,qty:l.qty,unit:l.unit})),notes:[]};
  for(const l of lines.filter(l=>l.kind==='MENU')){const m=x.menus.find(m=>m.id===l.refId);if(m)model.notes.push(`${m.name}: ${m.posItemIds.length} items for the kitchen (${m.veg?'veg':'veg and non-veg'})`);}
  if(e.blockId)model.notes.push(`Rooms blocked for guests: group block ${e.blockId}`);
  if(withMoney){const priced=hallPrice(s,hallBillLines(e),s.date),paid=hallPaid(e);
    model.prices={lines:priced.lines.map(l=>({desc:l.desc,qty:l.qty,rate:l.ratePaise,taxable:l.taxablePaise})),totals:priced.totals,paid,balance:Math.max(0,priced.totals.totalPaise-paid)};}
  return model;
}
export type QuoteDocModel={hotel:string;number:string;version:number;status:string;date:string;validUntil:string;customer:{name:string;phone?:string;company?:string};header:QuoteHeader;venue:string;
  lines:{desc:string;qty:number;unit:string;ratePaise:number;taxBps:number;taxablePaise:number;cgstPaise:number;sgstPaise:number}[];totals:HallTotals;advance:AdvanceRow[];tnc?:TncSnapshot};
export function hallQuoteDoc(s:State,q:Quotation,v:QuoteVersion):QuoteDocModel{
  const x=hallSlice(s),priced=hallPrice(s,v.lines,v.sentAt?v.sentAt.slice(0,10):s.date),enq=x.enquiries.find(e=>e.id===q.enquiryId);
  const total=priced.totals.totalPaise,plan=v.advance.length?hallPlanAmounts(total,v.advance):hallAdvancePlan(total,v.header.dates[0]??s.date,s.date,x.settings);
  return {hotel:s.settings.hotel||'Hotel',number:q.number,version:v.v,status:v.status,date:(v.sentAt??v.createdAt).slice(0,10),validUntil:v.validUntil,
    customer:q.customer??(enq?{name:enq.name,phone:enq.phone}:{name:'—'}),header:v.header,venue:venueName(x,v.header.venueId),
    lines:priced.lines.map(l=>({desc:l.desc,qty:l.qty,unit:l.unit,ratePaise:l.ratePaise,taxBps:l.taxBps,taxablePaise:l.taxablePaise,cgstPaise:l.cgstPaise,sgstPaise:l.sgstPaise})),
    totals:priced.totals,advance:plan,tnc:v.tnc};
}
export type InvoiceDocModel={hotel:string;no:string;date:string;event:string;customer:{name:string;phone?:string;company?:string};lines:{desc:string;qty:number;unit:string;ratePaise:number;taxBps:number;taxablePaise:number;cgstPaise:number;sgstPaise:number}[];
  taxablePaise:number;cgstPaise:number;sgstPaise:number;totalPaise:number;advanceAppliedPaise:number;balancePaise:number;settle:string;billedPax:number;credit?:{no:string;date:string;reason:string}};
export function hallInvoiceDoc(s:State,e:HallEvent):InvoiceDocModel|undefined{
  const i=e.invoice;if(!i)return undefined;
  return {hotel:s.settings.hotel||'Hotel',no:i.no,date:i.date,event:e.id,customer:e.customer??{name:e.id},
    lines:i.lines.map(l=>({desc:l.desc,qty:l.qty,unit:l.unit,ratePaise:l.ratePaise,taxBps:l.taxBps,taxablePaise:l.taxablePaise,cgstPaise:l.cgstPaise,sgstPaise:l.sgstPaise})),
    taxablePaise:i.taxablePaise,cgstPaise:i.cgstPaise,sgstPaise:i.sgstPaise,totalPaise:i.totalPaise,advanceAppliedPaise:i.advanceAppliedPaise,balancePaise:i.balancePaise,billedPax:i.billedPax,
    settle:i.settle.mode==='TENDER'?`Paid by ${i.settle.method}`:i.settle.mode==='COMPANY'?`Company credit ${i.settle.company}`:i.settle.mode==='UDHAAR'?'Udhaar (customer credit)':'Balance due',
    ...(e.creditNote?{credit:{no:e.creditNote.no,date:e.creditNote.date,reason:e.creditNote.reason}}:{})};
}
export const hallReceiptDoc=(s:State,e:HallEvent,r:HallReceipt)=>({hotel:s.settings.hotel||'Hotel',id:r.id,date:r.date,event:e.id,customer:e.customer?.name??e.id,method:r.method??'',reference:r.reference,amountPaise:r.amountPaise,kind:r.kind});
export {HALL_SLOTS};
