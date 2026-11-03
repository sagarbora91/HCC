// Module 27 · Hall booking command handlers (F6, contract §3). H1 block first; H2 / H3 append their own blocks.
// H1: HALL_VENUE_UPSERT, HALL_MENU_UPSERT, HALL_ENQUIRY_ADD / _UPDATE / _LOST / _SOFT_HOLD, HALL_NIGHT_RUN (enquiry auto-lost), QUO_ACCEPT,
// HALL_ADVANCE_RECEIVE, HALL_EVENT_CLOSE, HALL_EVENT_CANCEL, HALL_REFUND. Roles per contract §4 (HALL_ROLES); money rules in hall-billing.ts.
import type {Handler,Kit} from '../commands';
import type {Role} from '../../../domain/types';
import {hallEnsure,hallNextId,hallClashes,hallSoftHoldsOn,hallCapacity,hallEnquiriesDue,hallLatest,HALL_ROLES,HALL_SLOTS,HALL_EVENT_TYPES,HALL_NO_RESPONSE,
  type Venue,type MenuPackage,type Enquiry,type HallEvent,type QuoteLine,type HallReceipt,type HallInvoice} from '../../../domain/streams/hall';
import {hallPrice,hallAdvancePlan,hallPlanAmounts,hallAllocate,hallPaid,hallBillLines,hallBilledPax,hallDiscountLimit,hallDiscountWithin,hallCancelQuote,
  hallRefundApprover} from '../../../domain/streams/hall-billing';
import {posSlice} from '../../../domain/streams/pos';
import {POS_FIXTURE_MENU} from '../../../fixtures/ext/pos';
import {grpSlice} from '../../../domain/streams/groups';
import {fdmAllocate} from '../../../domain/streams/frontdesk-master';
import {addDays} from '../../../domain/money';

const DATE=/^\d{4}-\d{2}-\d{2}$/;
const TENDERS=['Cash','UPI','Card','Bank transfer'];
const rs=(p:number)=>'₹'+(p/100).toFixed(2);
const role=(k:Kit,list:Role[],what:string)=>{if(!list.includes(k.user.role))k.fail('FORBIDDEN',`Access denied: ${what}.`);};
const txt=(v:unknown)=>String(v??'').trim();
const paise=(k:Kit,v:unknown,what:string,allowZero=false)=>{const n=Number(v);k.assert(Number.isSafeInteger(n)&&(allowZero?n>=0:n>0)&&n<=10_000_000_000,`${what}: enter whole paise ${allowZero?'(0 or more)':'greater than zero'}.`);return n;};
const tender=(k:Kit,m:unknown,ref:unknown)=>{const method=txt(m);k.assert(TENDERS.includes(method),'Choose a tender: Cash, UPI, Card or Bank transfer.');
  if(method!=='Cash')k.assert(txt(ref).length>=3,'Enter the payment reference (UPI / card / bank).');return method;};
const event=(k:Kit)=>{const e=hallEnsure(k.s).events.find(e=>e.id===txt(k.cmd.eventId??k.cmd.id));k.assert(e,'Hall event not found.','NOT_FOUND');return e!;};
const receipt=(k:Kit,e:HallEvent,kind:HallReceipt['kind'],amountPaise:number,extra:Partial<HallReceipt>):HallReceipt=>{
  const x=hallEnsure(k.s),r:HallReceipt={id:hallNextId(x,'HRC'),eventId:e.id,date:k.s.date,kind,amountPaise,by:k.uid,at:k.s.clock,...extra};
  e.receipts=[...(e.receipts??[]),r];return r;};
const ref=(v:unknown)=>txt(v)?{reference:txt(v)}:{};

function venueIn(k:Kit,v:any,old?:Venue):Venue{
  const x=hallEnsure(k.s),name=txt(v?.name);k.assert(name.length>=2&&name.length<=60,'Hall name: 2–60 characters.');
  k.assert(['HALL','LAWN','TERRACE'].includes(v?.kind),'Kind: Hall, Lawn or Terrace.');
  k.assert(!x.venues.some(o=>o.id!==old?.id&&o.name.toLowerCase()===name.toLowerCase()),'Another hall already has this name.');
  const slots=Array.isArray(v?.slots)?v.slots:[];k.assert(slots.length>=1,'Give at least one slot with its time and rate.');
  const seen=new Set<string>();
  for(const s of slots){k.assert(HALL_SLOTS.includes(s?.slot)&&!seen.has(s.slot),'Each slot once: Morning, Afternoon, Evening or Full day.');seen.add(s.slot);
    k.assert(/^\d{2}:\d{2}$/.test(s.from)&&/^\d{2}:\d{2}$/.test(s.to)&&s.from<s.to,'Slot times: HH:mm, from before to.');paise(k,s.ratePaise,'Slot rate',true);}
  const cap:Record<string,number>={};
  for(const [key,n] of Object.entries(v?.capacity??{})){k.assert(['THEATRE','ROUND','USHAPE','FLOATING','CLASSROOM'].includes(key)&&Number.isInteger(n)&&(n as number)>0&&(n as number)<=10000,'Capacity: a whole number per setup.');cap[key]=n as number;}
  const combinedOf:string[]|undefined=Array.isArray(v?.combinedOf)&&v.combinedOf.length?v.combinedOf.map(String):undefined;
  if(combinedOf)k.assert(combinedOf.length>=2&&combinedOf.every(id=>x.venues.some(o=>o.id===id&&!o.combinedOf&&o.id!==old?.id)),'A combined hall joins two or more existing single halls.');
  return {id:old?.id??hallNextId(x,'V'),name,kind:v.kind,capacity:cap,
    slots:slots.map((s:any)=>({slot:s.slot,from:s.from,to:s.to,ratePaise:Number(s.ratePaise),...(s.minPlates?{minPlates:Number(s.minPlates)}:{})})),
    ...(v.minPlates?{minPlates:Number(v.minPlates)}:{}),gstClass:txt(v.gstClass)||'HALL_RENT',...(combinedOf?{combinedOf}:{}),active:v.active!==false,version:(old?.version??0)+1};
}

export const handlers:Record<string,Handler>={
  // F6-H1
  HALL_VENUE_UPSERT:k=>{
    role(k,HALL_ROLES.venues,'only the Owner edits the halls master (PERM-HAL-006)');
    const x=hallEnsure(k.s),id=txt(k.cmd.venue?.id),old=id?x.venues.find(v=>v.id===id):undefined;if(id)k.assert(old,'Hall not found.','NOT_FOUND');
    if(old&&k.cmd.version!==undefined)k.assert(Number(k.cmd.version)===old.version,'This hall was changed by someone else. Reload and try again.','STALE_VERSION');
    const v=venueIn(k,k.cmd.venue,old);
    if(old&&!v.active)k.assert(!x.events.some(e=>e.status==='CONFIRMED'&&e.venueIds.includes(old.id)&&e.date>=k.s.date),'This hall has confirmed events ahead; it stays active until they are billed or cancelled.','INVALID_TRANSITION');
    x.venues=old?x.venues.map(o=>o.id===old.id?v:o):[...x.venues,v];
    return `${old?'Hall updated':'Hall added'} · ${v.name} v${v.version}`;
  },
  HALL_MENU_UPSERT:k=>{
    role(k,HALL_ROLES.menus,'menus are kept by the Owner or Manager (PERM-HAL-006)');
    const x=hallEnsure(k.s),m=k.cmd.menu??{},id=txt(m.id),old=id?x.menus.find(o=>o.id===id):undefined;if(id)k.assert(old,'Menu not found.','NOT_FOUND');
    const name=txt(m.name);k.assert(name.length>=2&&name.length<=60,'Menu name: 2–60 characters.');
    const per=paise(k,m.perPlatePaise,'Price per plate');
    const items:string[]=Array.isArray(m.posItemIds)?m.posItemIds.map(String):[];k.assert(items.length>=1,'Pick at least one item from the POS menu.');
    const menu=posSlice(k.s).menu.length?posSlice(k.s).menu:POS_FIXTURE_MENU;
    const missing=items.filter(i=>!menu.some(p=>p.id===i));k.assert(!missing.length,`Not on the POS menu master: ${missing.join(', ')}.`);
    const row:MenuPackage={id:old?.id??hallNextId(x,'MP'),name,perPlatePaise:per,posItemIds:items,veg:!!m.veg,active:m.active!==false,version:(old?.version??0)+1};
    x.menus=old?x.menus.map(o=>o.id===old.id?row:o):[...x.menus,row];
    return `${old?'Menu updated':'Menu added'} · ${name} · ${rs(per)} / plate`;
  },
  HALL_ENQUIRY_ADD:k=>{
    role(k,HALL_ROLES.enquiry,'enquiries (PERM-HAL-001)');
    const x=hallEnsure(k.s),c=k.cmd,name=txt(c.name),phone=txt(c.phone).replace(/[\s-]+/g,'');
    k.assert(name.length>=2&&name.length<=80,'Enter the customer’s name.');k.assert(/^\+?\d{10,13}$/.test(phone),'Enter a 10-digit phone number.');
    k.assert(HALL_EVENT_TYPES.includes(c.eventType),'Choose the event type.');
    const dates:string[]=Array.isArray(c.dates)?c.dates.map(String):[];
    k.assert(dates.length>=1&&dates.length<=7&&dates.every(d=>DATE.test(d)&&d>=k.s.date),'Event date(s) from today on (up to 7).');
    k.assert(HALL_SLOTS.includes(c.slot),'Choose the slot.');k.assert(Number.isInteger(c.pax)&&c.pax>=1&&c.pax<=5000,'Expected guests: 1–5000.');
    if(c.venueId)k.assert(x.venues.some(v=>v.id===c.venueId&&v.active),'Choose an active hall.');
    k.assert(['WALKIN','PHONE','REFERENCE','WEBSITE','REPEAT'].includes(c.source),'Choose the source.');
    k.assert(DATE.test(c.followUp)&&c.followUp>=k.s.date,'Set a follow-up date from today on.');
    const q:Enquiry={id:hallNextId(x,'ENQ'),name,phone,eventType:c.eventType,dates:[...new Set(dates)].sort(),slot:c.slot,pax:c.pax,source:c.source,followUp:c.followUp,status:'OPEN',
      ...(c.venueId?{venueId:c.venueId}:{}),...(c.customerId?{customerId:txt(c.customerId)}:{}),...(c.partyId?{partyId:txt(c.partyId)}:{}),...(txt(c.note)?{note:txt(c.note).slice(0,500)}:{}),
      createdAt:k.s.clock,by:k.uid,version:1};
    x.enquiries=[...x.enquiries,q];
    return `Enquiry ${q.id} · ${name} · ${q.eventType} · ${q.dates.join(', ')} ${q.slot} · ${q.pax} pax`;
  },
  HALL_ENQUIRY_UPDATE:k=>{
    role(k,HALL_ROLES.enquiry,'enquiries (PERM-HAL-001)');
    const x=hallEnsure(k.s),q=x.enquiries.find(q=>q.id===txt(k.cmd.id));k.assert(q,'Enquiry not found.','NOT_FOUND');
    k.assert(q!.status==='OPEN'||q!.status==='QUOTED'||(q!.status==='LOST'&&k.cmd.status==='OPEN'),'A won or lost enquiry is read-only (reopen a lost one first).','INVALID_TRANSITION');
    const c=k.cmd,n:Enquiry={...q!};
    if(c.followUp!==undefined){k.assert(DATE.test(c.followUp)&&c.followUp>=k.s.date,'Set a follow-up date from today on.');n.followUp=c.followUp;}
    if(c.note!==undefined)n.note=txt(c.note).slice(0,500);
    if(c.pax!==undefined){k.assert(Number.isInteger(c.pax)&&c.pax>=1&&c.pax<=5000,'Expected guests: 1–5000.');n.pax=c.pax;}
    if(c.venueId!==undefined){k.assert(!c.venueId||x.venues.some(v=>v.id===c.venueId&&v.active),'Choose an active hall.');if(c.venueId)n.venueId=c.venueId;else delete n.venueId;}
    if(c.dates!==undefined){const d:string[]=Array.isArray(c.dates)?c.dates.map(String):[];k.assert(d.length>=1&&d.every(v=>DATE.test(v)&&v>=k.s.date),'Event date(s) from today on.');n.dates=[...new Set(d)].sort();}
    if(c.slot!==undefined){k.assert(HALL_SLOTS.includes(c.slot),'Choose the slot.');n.slot=c.slot;}
    if(c.status!==undefined){k.assert(c.status==='OPEN'||c.status==='QUOTED','Use Mark lost (or accept the quotation) to close an enquiry.');
      if(q!.status==='LOST'){delete n.lostReasonId;delete n.lostNote;}n.status=c.status;}
    n.version=(q!.version??1)+1;x.enquiries=x.enquiries.map(o=>o.id===q!.id?n:o);
    return `Enquiry ${q!.id} updated · ${n.status} · follow-up ${n.followUp}`;
  },
  HALL_ENQUIRY_LOST:k=>{
    role(k,HALL_ROLES.enquiry,'enquiries (PERM-HAL-001)');
    const x=hallEnsure(k.s),q=x.enquiries.find(q=>q.id===txt(k.cmd.id));k.assert(q,'Enquiry not found.','NOT_FOUND');
    k.assert(q!.status==='OPEN'||q!.status==='QUOTED','Only an open or quoted enquiry can be marked lost.','INVALID_TRANSITION');
    const r=x.lostReasons.find(r=>r.id===txt(k.cmd.reasonId));k.assert(r,'Choose a lost reason from the list.');
    const note=txt(k.cmd.note);if(r!.needsNote)k.assert(note.length>=3,`“${r!.label}” needs a note (at least 3 characters).`);
    x.enquiries=x.enquiries.map(o=>{if(o.id!==q!.id)return o;const n:Enquiry={...o,status:'LOST',lostReasonId:r!.id,version:(o.version??1)+1};if(note)n.lostNote=note.slice(0,300);delete n.softHold;return n;});
    return `Enquiry ${q!.id} lost · ${r!.label}${note?' · '+note:''}`;
  },
  HALL_ENQUIRY_SOFT_HOLD:k=>{
    role(k,HALL_ROLES.softHold,'a soft hold needs the Manager or Owner');
    const x=hallEnsure(k.s),q=x.enquiries.find(q=>q.id===txt(k.cmd.id));k.assert(q,'Enquiry not found.','NOT_FOUND');
    k.assert(q!.status==='OPEN'||q!.status==='QUOTED','Only an open or quoted enquiry can hold a hall.','INVALID_TRANSITION');
    if(k.cmd.release){x.enquiries=x.enquiries.map(o=>{if(o.id!==q!.id)return o;const n={...o};delete n.softHold;return n;});return `Soft hold released · ${q!.id}`;}
    const v=x.venues.find(v=>v.id===txt(k.cmd.venueId??q!.venueId)&&v.active);k.assert(v,'Choose an active hall.');
    const days=Number(k.cmd.days??3),max=x.settings.softHoldMaxDays??3;k.assert(Number.isInteger(days)&&days>=1&&days<=max,`A soft hold lasts 1–${max} days.`);
    const clash=hallClashes(x,[v!.id],q!.dates,q!.slot);if(clash.length)k.fail('HCC_HALL_CLASH',`${v!.name} is already booked on ${clash[0].date} (${clash[0].slot}, event ${clash[0].eventId}).`);
    const others=hallSoftHoldsOn(x,[v!.id],q!.dates,q!.slot,k.s.clock,q!.id);
    const until=addDays(k.s.date,days)+k.s.clock.slice(10);
    x.enquiries=x.enquiries.map(o=>o.id===q!.id?{...o,softHold:{venueId:v!.id,until,by:k.uid}}:o);
    return `Soft hold · ${v!.name} · ${q!.dates.join(', ')} ${q!.slot} until ${until}${others.length?` · warning: also soft-held by ${others.map(o=>o.id).join(', ')}`:''}`;
  },
  HALL_NIGHT_RUN:k=>{
    role(k,['Owner','Manager'],'the hall night run (Manager or Owner)');
    const x=hallEnsure(k.s),due=hallEnquiriesDue(k.s),ids=new Set(due.lost.map(q=>q.id));
    x.enquiries=x.enquiries.map(o=>{if(!ids.has(o.id))return o;const n:Enquiry={...o,status:'LOST',lostReasonId:HALL_NO_RESPONSE,lostNote:`No reply ${x.settings.enquiryAutoLostDays} days after the follow-up date (auto)`,version:(o.version??1)+1};delete n.softHold;return n;});
    return `Hall night run · ${ids.size} enquiry(ies) moved to Lost / No response · ${due.warn.length} warned`;
  },
  QUO_ACCEPT:k=>{
    role(k,HALL_ROLES.accept,'accepting a quotation (PERM-HAL-003)');k.open();
    const x=hallEnsure(k.s),q=x.quotations.find(q=>q.id===txt(k.cmd.id));k.assert(q,'Quotation not found.','NOT_FOUND');
    if(k.cmd.version!==undefined&&Number(k.cmd.version)!==q!.currentVersion)k.fail('HCC_QUO_NOT_LATEST',`Only the latest version (v${q!.currentVersion}) can be accepted.`);
    const v=hallLatest(q!);k.assert(v.status==='SENT'||v.status==='EXPIRED','Only a sent quotation can be accepted.','INVALID_TRANSITION');
    const reason=txt(k.cmd.reason),boss=['Owner','Manager'].includes(k.user.role);
    if((v.status==='EXPIRED'||v.validUntil<k.s.date)&&!(boss&&reason.length>=3))k.fail('HCC_QUO_EXPIRED',`This quotation expired on ${v.validUntil}. Revise it, or a Manager accepts it with a reason.`);
    const h=v.header,venue=x.venues.find(o=>o.id===h.venueId);k.assert(venue?.active,'The quoted hall is not active.');
    const clash=hallClashes(x,[h.venueId],h.dates,h.slot);
    if(clash.length)k.fail('HCC_HALL_CLASH',`${venue!.name} is already booked on ${clash[0].date} (${clash[0].slot}, event ${clash[0].eventId}).`);
    const cap=hallCapacity(x,h.venueId,h.setup);
    if(cap!==undefined&&h.pax>cap&&!(boss&&txt(k.cmd.capacityReason).length>=3))k.fail('HCC_HALL_CAPACITY',`${h.pax} guests is above ${venue!.name}’s ${h.setup} capacity of ${cap}. A Manager can override with a reason.`);
    const total=hallPrice(k.s,v.lines,k.s.date).totals.totalPaise;
    const plan=v.advance?.length?hallPlanAmounts(total,v.advance.map(r=>({...r,paidPaise:0,receiptIds:[]}))):hallAdvancePlan(total,h.dates[0],k.s.date,x.settings);
    const enq=x.enquiries.find(o=>o.id===q!.enquiryId),customer=q!.customer??(enq?{name:enq.name,phone:enq.phone}:undefined);
    const e:HallEvent={id:hallNextId(x,'HEV'),quotationId:q!.id,version:v.v,venueIds:[h.venueId],date:h.dates[0],...(h.dates.length>1?{extraDates:h.dates.slice(1)}:{}),slot:h.slot,
      status:'CONFIRMED',lines:v.lines.map(l=>({...l})),guaranteedPax:h.guaranteedPax,extras:[],advance:plan,receipts:[],eventType:h.eventType,
      ...(customer?{customer}:{}),createdAt:k.s.clock,by:k.uid};
    x.events=[...x.events,e];
    x.quotations=x.quotations.map(o=>o.id!==q!.id?o:{...o,versions:o.versions.map(w=>w.v===v.v?{...w,status:'ACCEPTED'}:w)});
    if(enq)x.enquiries=x.enquiries.map(o=>{if(o.id!==enq.id)return o;const n:Enquiry={...o,status:'WON',version:(o.version??1)+1};delete n.softHold;return n;});
    let paid='';
    if(k.cmd.advance&&Number(k.cmd.advance.amountPaise)>0){const a=k.cmd.advance,amt=paise(k,a.amountPaise,'Advance'),m=tender(k,a.method,a.reference);
      const ev=x.events[x.events.length-1],r=receipt(k,ev,'ADVANCE',amt,{method:m,...ref(a.reference)});ev.advance=hallAllocate(ev.advance!,amt,r.id);
      paid=` · advance ${rs(amt)} ${m} (receipt ${r.id}, no GST)`;}
    return `Quotation ${q!.number} v${v.v} accepted → event ${e.id} · ${venue!.name} ${h.dates.join(', ')} ${h.slot} · total ${rs(total)}${reason?' · '+reason:''}${paid}`;
  },
  HALL_ADVANCE_RECEIVE:k=>{
    role(k,HALL_ROLES.advance,'taking a hall advance (PERM-HAL-003)');k.open();
    const e=event(k);k.assert(e.status==='CONFIRMED','Advances are taken on a confirmed event only.','INVALID_TRANSITION');
    const amt=paise(k,k.cmd.amountPaise,'Advance'),m=tender(k,k.cmd.method,k.cmd.reference);
    const r=receipt(k,e,'ADVANCE',amt,{method:m,...ref(k.cmd.reference)});
    e.advance=hallAllocate(e.advance??[],amt,r.id);
    return `Hall advance ${rs(amt)} · ${m} · event ${e.id} · receipt ${r.id} (Dr tender, Cr 2000 advance deposits; no GST, DEC-142)`;
  },
  HALL_EVENT_CLOSE:k=>{
    role(k,HALL_ROLES.bill,'the event-day bill (PERM-HAL-004)');k.open();
    const x=hallEnsure(k.s),e=event(k);k.assert(e.status==='CONFIRMED','This event is already billed or cancelled.','INVALID_TRANSITION');
    k.assert(k.s.date>=e.date,`The bill is made on or after the event day (${e.date}).`,'INVALID_TRANSITION');
    const actual=Number(k.cmd.actualPax);k.assert(Number.isInteger(actual)&&actual>=0&&actual<=5000,'Enter the actual plates (0–5000).');
    const extras:QuoteLine[]=(Array.isArray(k.cmd.extras)?k.cmd.extras:[]).map((l:any,i:number)=>{
      const desc=txt(l?.desc);k.assert(desc.length>=2,'Each extra needs a description.');k.assert(Number.isInteger(l.qty)&&l.qty>0,'Extra quantity: a whole number.');
      const rate=paise(k,l.ratePaise,'Extra rate'),kind=['EXTRA','MENU','OTHER','ROOM'].includes(l.kind)?l.kind:'EXTRA';
      return {id:`X${i+1}`,kind,desc,qty:l.qty,unit:txt(l.unit)||'each',ratePaise:rate,listRatePaise:Number(l.listRatePaise??rate),discountPaise:Number(l.discountPaise??0),
        gstClass:txt(l.gstClass)||(kind==='MENU'?'BANQUET_FOOD':'SERVICE')};});
    // Spec §8: the discount accepted on the quotation stands; a new discount on the bill (extras below list) is held to the role's limit.
    if(!hallDiscountWithin(extras,hallDiscountLimit(x.settings,k.user.role)))k.fail('HCC_DISCOUNT_LIMIT','A discount on the bill above your limit needs the Owner’s approval.');
    const lines=hallBillLines({...e,actualPax:actual,extras}),priced=hallPrice(k.s,lines,k.s.date),t=priced.totals;
    const set=k.cmd.settle??{},mode=txt(set.mode)||'DUE';k.assert(['TENDER','COMPANY','UDHAAR','DUE'].includes(mode),'Settle by tender, company credit, Udhaar, or leave the balance due.');
    const paid=hallPaid(e),applied=Math.min(paid,t.totalPaise),balance=t.totalPaise-applied;
    let settle:HallInvoice['settle']={mode:'DUE'};
    if(balance>0&&mode==='TENDER')settle={mode:'TENDER',method:tender(k,set.method,set.reference),...ref(set.reference)};
    if(balance>0&&mode==='COMPANY'){role(k,['Owner','Manager','Reception'],'company credit needs the Reception, Manager or Owner');
      const co=grpSlice(k.s).companies.find(c=>c.id===txt(set.company));k.assert(co,'Choose the company account.');settle={mode:'COMPANY',company:co!.id};}
    if(balance>0&&mode==='UDHAAR'){role(k,['Owner','Manager'],'Udhaar (DEC-144) needs the Manager or Owner');settle={mode:'UDHAAR'};}
    const no=fdmAllocate(k.s,'INV'); // DEC-197 series HPE/2627/…
    e.actualPax=actual;e.extras=extras;e.status='BILLED';e.invoiceId=no;
    e.invoice={no,date:k.s.date,lines:priced.lines,taxablePaise:t.taxablePaise,cgstPaise:t.cgstPaise,sgstPaise:t.sgstPaise,totalPaise:t.totalPaise,
      advanceAppliedPaise:applied,balancePaise:balance,billedPax:hallBilledPax(e),settle,by:k.uid,at:k.s.clock};
    if(balance>0&&settle.mode!=='DUE')receipt(k,e,'BALANCE',balance,{...(settle.method?{method:settle.method}:{}),...(settle.company?{company:settle.company}:{}),...(settle.mode==='UDHAAR'?{udhaar:true}:{}),...ref(settle.reference)});
    const over=paid-applied;
    return `Hall invoice ${no} · event ${e.id} · ${hallBilledPax(e)} plates billed (guaranteed ${e.guaranteedPax??0}, actual ${actual}) · total ${rs(t.totalPaise)} · advances applied ${rs(applied)} · balance ${rs(balance)} ${settle.mode}${over>0?` · ${rs(over)} advance left to refund`:''}`;
  },
  HALL_EVENT_CANCEL:k=>{
    role(k,HALL_ROLES.cancel,'cancelling a hall event (PERM-HAL-004)');k.open();
    const e=event(k);k.assert(e.status==='CONFIRMED','Only a confirmed event can be cancelled (a billed one takes a credit note).','INVALID_TRANSITION');
    const reason=txt(k.cmd.reason);k.assert(reason.length>=3,'Give the reason for the cancellation.');
    const c=hallCancelQuote(k.s,e,k.s.date);
    e.status='CANCELLED';
    e.cancellation={at:k.s.clock,date:k.s.date,keptPaise:c.keptPaise,refundPaise:c.refundPaise,by:k.uid,daysBefore:c.daysBefore,keptBps:c.keptBps,taxBps:c.taxBps,
      refund:c.refundPaise>0?'PENDING':'NONE',reason};
    return `Event ${e.id} cancelled ${c.daysBefore} days before · kept ${rs(c.keptPaise)} (${c.keptBps/100} % slab, 4300 with GST) · refund ${rs(c.refundPaise)}${c.refundPaise?' awaiting approval':''} · hall released`;
  },
  HALL_REFUND:k=>{
    role(k,HALL_ROLES.refund,'a hall refund is approved by the Manager (up to ₹50,000) or the Owner');k.open();
    const x=hallEnsure(k.s),e=event(k),c=e.cancellation;k.assert(c&&c.refund==='PENDING','No refund is waiting on this event.','INVALID_TRANSITION');
    k.assert(c!.by!==k.uid,'You cannot approve a refund on your own cancellation.','FORBIDDEN');
    if(!hallRefundApprover(x.settings,k.user.role,c!.refundPaise))k.fail('FORBIDDEN',`A refund above ₹${(x.settings.refundManagerLimitPaise/100).toLocaleString('en-IN')} needs the Owner.`);
    const m=tender(k,k.cmd.method,k.cmd.reference);
    const r=receipt(k,e,'REFUND',c!.refundPaise,{method:m,...ref(k.cmd.reference)});
    e.cancellation={...c!,refund:'PAID',refundBy:k.uid,refundAt:k.s.clock,refundMethod:m};
    return `Hall refund ${rs(c!.refundPaise)} · ${m} · event ${e.id} · receipt ${r.id} (Dr 2000, Cr tender)`;
  },
  // F6-H1 end
  // F6-H2
  // F6-H2 end
  // F6-H3
  // F6-H3 end
};
