// Module 27 · Hall booking quotation + T&C handlers (F6 H2, contract §3; spec §6–§8). QUO_ACCEPT is H1's (handlers/hall.ts).
// QUO_CREATE · QUO_EDIT (draft only) · QUO_REVISE (copy to v+1, old → SUPERSEDED) · QUO_SEND (placeholder guard, discount limit, T&C snapshot,
// locks the version, outbox held until F9) · QUO_PLACEHOLDER_OVERRIDE (Owner, reason, logged) · QUO_DISCOUNT_APPROVE (Owner, never the author)
// · QUO_LOST (listed reason) · QUO_CANCEL (draft) · QUO_EXPIRE (night run: SENT past validity → EXPIRED) · HALL_TNC_TEMPLATE_SAVE (Owner only).
import type {Handler,Kit} from '../commands';
import {hallEnsure,hallLatest,HALL_ROLES,HALL_SLOTS,HALL_EVENT_TYPES,type Quotation,type QuoteVersion,type QuoteLine,type QuoteHeader,type AdvanceRow,
  type HallSetup,type Enquiry,type TncTemplate} from '../../../domain/streams/hall';
import {hallDiscountBps,hallDiscountLimit,hallDiscountWithin} from '../../../domain/streams/hall-billing';
import {hallTemplates,hallTncSnapshot,hallTncClausesProblem,hallTncClauses,hallQuoNext,hallQuoteDefaultLines,hallMasterRate,hallQuoteFinal} from '../../../domain/streams/hall-quote';
import {addDays} from '../../../domain/money';

const DATE=/^\d{4}-\d{2}-\d{2}$/;
const SETUPS:HallSetup[]=['THEATRE','ROUND','USHAPE','FLOATING','CLASSROOM'];
const KINDS=['HALL','MENU','EXTRA','ROOM','OTHER'];
const CLASSES=['HALL_RENT','BANQUET_FOOD','SERVICE','ROOM'];
const txt=(v:unknown)=>String(v??'').trim();
const role=(k:Kit,list:readonly string[],what:string)=>{if(!list.includes(k.user.role))k.fail('FORBIDDEN',`Access denied: ${what}.`);};
const pct=(bps:number)=>(bps/100).toFixed(2).replace(/\.?0+$/,'')+' %';
const quo=(k:Kit)=>{const q=hallEnsure(k.s).quotations.find(q=>q.id===txt(k.cmd.id));k.assert(q,'Quotation not found.','NOT_FOUND');return q!;};
const put=(k:Kit,q:Quotation)=>{const x=hallEnsure(k.s);x.quotations=x.quotations.map(o=>o.id===q.id?q:o);};
const setEnq=(k:Kit,id:string,f:(e:Enquiry)=>Enquiry)=>{const x=hallEnsure(k.s);x.enquiries=x.enquiries.map(e=>e.id===id?f({...e}):e);};
/** The command may name the version it acts on; it must be the latest (HCC_QUO_NOT_LATEST). */
const latest=(k:Kit,q:Quotation)=>{if(k.cmd.version!==undefined&&Number(k.cmd.version)!==q.currentVersion)k.fail('HCC_QUO_NOT_LATEST',`Only the latest version (v${q.currentVersion}) can be changed.`);return hallLatest(q);};

function headerIn(k:Kit,base:QuoteHeader,h:any):QuoteHeader{
  const x=hallEnsure(k.s),n:QuoteHeader={...base,...(h??{})};
  k.assert(HALL_EVENT_TYPES.includes(n.eventType),'Choose the event type.');
  k.assert(Array.isArray(n.dates)&&n.dates.length>=1&&n.dates.length<=7&&n.dates.every(d=>DATE.test(d)&&d>=k.s.date),'Event date(s) from today on (up to 7).');
  k.assert(HALL_SLOTS.includes(n.slot),'Choose the slot.');
  const v=x.venues.find(v=>v.id===n.venueId&&v.active);k.assert(v,'Choose an active hall.');k.assert(v!.slots.some(s=>s.slot===n.slot),`${v!.name} has no ${n.slot} slot.`);
  k.assert(SETUPS.includes(n.setup),'Choose the setup.');
  k.assert(Number.isInteger(n.pax)&&n.pax>=1&&n.pax<=5000,'Expected guests: 1–5000.');
  k.assert(Number.isInteger(n.guaranteedPax)&&n.guaranteedPax>=0&&n.guaranteedPax<=n.pax,'Guaranteed guests: 0 up to the expected guests.');
  return {...n,dates:[...new Set(n.dates.map(String))].sort()};
}
function linesIn(k:Kit,raw:unknown,slot:QuoteHeader['slot']):QuoteLine[]{
  const x=hallEnsure(k.s),arr=Array.isArray(raw)?raw:[];k.assert(arr.length>=1&&arr.length<=60,'Give 1–60 lines.');
  return arr.map((l:any,i:number)=>{
    k.assert(KINDS.includes(l?.kind),`Line ${i+1}: kind Hall, Menu, Extra, Room or Other.`);
    const desc=txt(l.desc);k.assert(desc.length>=2&&desc.length<=120,`Line ${i+1}: description 2–120 characters.`);
    const qty=Number(l.qty);k.assert(Number.isInteger(qty)&&qty>=1&&qty<=100000,`Line ${i+1}: quantity a whole number.`);
    const rate=Number(l.ratePaise);k.assert(Number.isSafeInteger(rate)&&rate>=0&&rate<=10_000_000_000,`Line ${i+1}: rate in whole paise.`);
    const master=hallMasterRate(x,{kind:l.kind,refId:txt(l.refId)},slot);
    if(l.kind==='HALL'||l.kind==='MENU')k.assert(master!==undefined,`Line ${i+1}: pick the ${l.kind==='HALL'?'hall slot':'menu package'} from the master.`);
    const list=master??(l.listRatePaise!==undefined?Number(l.listRatePaise):rate);
    k.assert(Number.isSafeInteger(list)&&list>=rate,`Line ${i+1}: the list rate cannot be below the rate.`);
    const disc=Number(l.discountPaise??0);k.assert(Number.isSafeInteger(disc)&&disc>=0&&disc<=qty*rate,`Line ${i+1}: discount 0 up to the line value.`);
    const gstClass=txt(l.gstClass)||(l.kind==='HALL'?'HALL_RENT':l.kind==='MENU'?'BANQUET_FOOD':l.kind==='ROOM'?'ROOM':'SERVICE');
    k.assert(CLASSES.includes(gstClass),`Line ${i+1}: GST class Hall rent, Banquet food, Service or Room.`);
    return {id:`L${i+1}`,kind:l.kind,...(txt(l.refId)?{refId:txt(l.refId)}:{}),desc,qty,unit:txt(l.unit)||'each',ratePaise:rate,listRatePaise:list,discountPaise:disc,gstClass};
  });
}
/** Advance plan rows (spec §6.4): Manager / Owner may edit; Reception keeps the default ([] = default at acceptance). */
function advanceIn(k:Kit,raw:unknown):AdvanceRow[]{
  if(raw===undefined||raw===null||(Array.isArray(raw)&&!raw.length))return [];
  role(k,['Owner','Manager'],'only the Manager or Owner changes the advance plan (Reception quotes the default)');
  const arr=Array.isArray(raw)?raw:[];k.assert(arr.length>=1&&arr.length<=6,'The advance plan has 1–6 rows.');
  let pctSum=0;
  const rows=arr.map((r:any,i:number):AdvanceRow=>{const last=i===arr.length-1,label=txt(r?.label)||(last?'Balance':`Advance ${i+1}`);
    const due=txt(r?.due);k.assert(due==='ON_ACCEPT'||due==='EVENT_DAY'||DATE.test(due),`Advance row ${i+1}: due on acceptance, on the event day or a date.`);
    if(last)return {id:`ADV-${i+1}`,label,due,paidPaise:0,receiptIds:[]};
    if(r.pctBps!==undefined){const p=Number(r.pctBps);k.assert(Number.isInteger(p)&&p>0&&p<10000,`Advance row ${i+1}: percentage between 0 and 100.`);pctSum+=p;return {id:`ADV-${i+1}`,label,pctBps:p,due,paidPaise:0,receiptIds:[]};}
    const a=Number(r.amountPaise);k.assert(Number.isSafeInteger(a)&&a>0,`Advance row ${i+1}: a percentage or an amount in whole paise.`);return {id:`ADV-${i+1}`,label,amountPaise:a,due,paidPaise:0,receiptIds:[]};});
  k.assert(pctSum<10000,'The percentage rows must leave a balance (under 100 % in all).');
  return rows;
}
const approvalNote=(k:Kit,lines:QuoteLine[])=>{const x=hallEnsure(k.s),lim=hallDiscountLimit(x.settings,k.user.role);
  return hallDiscountWithin(lines,lim)?'':` · discount ${pct(hallDiscountBps(lines))} is above your limit (${pct(lim??0)}): the Owner approves it before sending`;};

export const handlers:Record<string,Handler>={
  QUO_CREATE:k=>{
    role(k,HALL_ROLES.quote,'quotations (PERM-HAL-002)');
    const x=hallEnsure(k.s),c=k.cmd,enq=x.enquiries.find(e=>e.id===txt(c.enquiryId));k.assert(enq,'Enquiry not found.','NOT_FOUND');
    k.assert(enq!.status==='OPEN'||enq!.status==='QUOTED','Quote an open enquiry (reopen a lost one first).','INVALID_TRANSITION');
    const header=headerIn(k,{eventType:enq!.eventType,dates:enq!.dates,slot:enq!.slot,venueId:enq!.venueId??'',setup:'ROUND',pax:enq!.pax,guaranteedPax:enq!.pax},c.header);
    const lines=c.lines!==undefined?linesIn(k,c.lines,header.slot)
      :linesIn(k,hallQuoteDefaultLines(x,{venueId:header.venueId,slot:header.slot,menuId:txt(c.menuId)||undefined,plates:header.guaranteedPax||header.pax}),header.slot);
    const advance=advanceIn(k,c.advance);
    const n=hallQuoNext(x,k.s.date);
    const v:QuoteVersion={v:1,status:'DRAFT',header,lines,advance,tnc:hallTncSnapshot(x,header.eventType,k.s.date),validUntil:addDays(k.s.date,x.settings.validityDays),createdBy:k.uid,createdAt:k.s.clock};
    const q:Quotation={id:`QUO-${n.fy}-${String(n.seq).padStart(4,'0')}`,number:n.number,fy:n.fy,enquiryId:enq!.id,versions:[v],currentVersion:1,
      customer:{name:enq!.name,phone:enq!.phone,...(txt(c.company)?{company:txt(c.company)}:{})}};
    x.quotations=[...x.quotations,q];
    return `Quotation ${q.number} v1 drafted · ${enq!.name} · ${header.eventType} ${header.dates.join(', ')} ${header.slot}${approvalNote(k,lines)}`;
  },
  QUO_EDIT:k=>{
    role(k,HALL_ROLES.quote,'quotations (PERM-HAL-002)');
    const x=hallEnsure(k.s),q=quo(k),v=latest(k,q);k.assert(v.status==='DRAFT','Only a draft can be edited — revise a sent quotation into a new version.','INVALID_TRANSITION');
    const header=k.cmd.header!==undefined?headerIn(k,v.header,k.cmd.header):v.header;
    const lines=k.cmd.lines!==undefined?linesIn(k,k.cmd.lines,header.slot):v.lines;
    const advance=k.cmd.advance!==undefined?advanceIn(k,k.cmd.advance):v.advance;
    const n:QuoteVersion={...v,header,lines,advance,tnc:header.eventType!==v.header.eventType?hallTncSnapshot(x,header.eventType,k.s.date):v.tnc};
    if(k.cmd.lines!==undefined||k.cmd.header!==undefined)delete n.approvalId; // an approval covers the lines it saw
    if(header.eventType!==v.header.eventType)delete n.placeholderOverride;
    put(k,{...q,versions:q.versions.map(w=>w.v===v.v?n:w)});
    return `Quotation ${q.number} v${v.v} saved${approvalNote(k,lines)}`;
  },
  QUO_REVISE:k=>{
    role(k,HALL_ROLES.quote,'quotations (PERM-HAL-002)');
    const x=hallEnsure(k.s),q=quo(k),v=latest(k,q);
    k.assert(v.status==='SENT'||v.status==='EXPIRED','Only a sent or expired quotation is revised (edit a draft directly).','INVALID_TRANSITION');
    const nv:QuoteVersion={v:v.v+1,status:'DRAFT',header:{...v.header,dates:[...v.header.dates]},lines:v.lines.map(l=>({...l})),advance:v.advance.map(r=>({...r,receiptIds:[]})),
      tnc:hallTncSnapshot(x,v.header.eventType,k.s.date),validUntil:addDays(k.s.date,x.settings.validityDays),createdBy:k.uid,createdAt:k.s.clock};
    if(v.placeholderOverride&&nv.tnc.hasPlaceholder)nv.placeholderOverride={...v.placeholderOverride}; // once per quotation (spec §7)
    put(k,{...q,currentVersion:nv.v,versions:[...q.versions.map(w=>w.v===v.v&&w.status==='SENT'?{...w,status:'SUPERSEDED' as const}:w),nv]});
    return `Quotation ${q.number} revised → v${nv.v} (draft) · v${v.v} ${v.status==='SENT'?'superseded':'stays expired'}`;
  },
  QUO_SEND:k=>{
    role(k,HALL_ROLES.quote,'quotations (PERM-HAL-002)');
    const x=hallEnsure(k.s),q=quo(k),v=latest(k,q);k.assert(v.status==='DRAFT','This version is already sent (revise it for changes).','INVALID_TRANSITION');
    k.assert(v.header.dates[0]>=k.s.date,'The event date has passed — revise the dates first.','INVALID_TRANSITION');
    const lim=hallDiscountLimit(x.settings,k.user.role);
    if(!hallDiscountWithin(v.lines,lim)&&!v.approvalId)k.fail('HCC_DISCOUNT_LIMIT',`The discount of ${pct(hallDiscountBps(v.lines))} is above your limit (${pct(lim??0)}). Ask the Owner to approve it.`);
    // T&C snapshot taken now (the template in force on the send date) — immutable from here on.
    const tnc=hallTncSnapshot(x,v.header.eventType,k.s.date);
    const n:QuoteVersion={...v,tnc,status:'SENT',sentAt:k.s.clock,validUntil:addDays(k.s.date,x.settings.validityDays)};
    if(!hallQuoteFinal(n))k.fail('HCC_TNC_PLACEHOLDER','The T&C still say “OWNER TO REPLACE”. The Owner replaces the template, or overrides for this quotation with a reason.');
    put(k,{...q,versions:q.versions.map(w=>w.v===v.v?n:w)});
    setEnq(k,q.enquiryId,e=>e.status==='OPEN'?{...e,status:'QUOTED',version:(e.version??1)+1}:e);
    return `Quotation ${q.number} v${v.v} sent · valid until ${n.validUntil} · T&C ${tnc.templateId} v${tnc.v} kept with it · outbox message held until messaging is switched on`;
  },
  QUO_PLACEHOLDER_OVERRIDE:k=>{
    if(k.user.role!=='Owner')k.fail('HCC_TNC_OWNER_ONLY','Only the Owner can override the T&C placeholder (PERM-HAL-005).');
    const q=quo(k),v=latest(k,q);k.assert(v.status==='DRAFT','Override a draft before it is sent.','INVALID_TRANSITION');
    const reason=txt(k.cmd.reason);k.assert(reason.length>=5,'Give the reason for sending with placeholder T&C (at least 5 characters).');
    k.assert(!q.versions.some(w=>w.placeholderOverride&&w.v!==v.v),'This quotation already used its one override (spec §7).','INVALID_TRANSITION');
    put(k,{...q,versions:q.versions.map(w=>w.v===v.v?{...w,placeholderOverride:{by:k.uid,reason:reason.slice(0,300),at:k.s.clock}}:w)});
    return `T&C placeholder override · ${q.number} v${v.v} · ${reason}`;
  },
  QUO_DISCOUNT_APPROVE:k=>{
    role(k,['Owner'],'a discount above the limit is approved by the Owner');
    const q=quo(k),v=latest(k,q);k.assert(v.status==='DRAFT','Approve the discount on a draft.','INVALID_TRANSITION');
    k.assert(v.createdBy!==k.uid,'You cannot approve a discount on your own quotation (no self-approval).','FORBIDDEN');
    const bps=hallDiscountBps(v.lines),id=`QDA|${k.uid}|${k.s.clock}|${bps}`;
    put(k,{...q,versions:q.versions.map(w=>w.v===v.v?{...w,approvalId:id}:w)});
    return `Discount ${pct(bps)} approved · ${q.number} v${v.v}`;
  },
  QUO_LOST:k=>{
    role(k,HALL_ROLES.quote,'quotations (PERM-HAL-002)');
    const x=hallEnsure(k.s),q=quo(k),v=latest(k,q);
    k.assert(['DRAFT','SENT','EXPIRED'].includes(v.status),'Only an open quotation can be marked lost.','INVALID_TRANSITION');
    const r=x.lostReasons.find(r=>r.id===txt(k.cmd.reasonId));k.assert(r,'Choose a lost reason from the list.');
    const note=txt(k.cmd.note);if(r!.needsNote)k.assert(note.length>=3,`“${r!.label}” needs a note (at least 3 characters).`);
    put(k,{...q,versions:q.versions.map(w=>w.v===v.v?{...w,status:'LOST' as const}:w)});
    setEnq(k,q.enquiryId,e=>{if(e.status!=='OPEN'&&e.status!=='QUOTED')return e;const n:Enquiry={...e,status:'LOST',lostReasonId:r!.id,version:(e.version??1)+1};
      if(note)n.lostNote=note.slice(0,300);delete n.softHold;return n;});
    return `Quotation ${q.number} v${v.v} lost · ${r!.label}${note?' · '+note:''} (the number is kept)`;
  },
  QUO_CANCEL:k=>{
    role(k,HALL_ROLES.quote,'quotations (PERM-HAL-002)');
    const q=quo(k),v=latest(k,q);k.assert(v.status==='DRAFT','Only a draft is cancelled (a sent quotation is marked lost).','INVALID_TRANSITION');
    put(k,{...q,versions:q.versions.map(w=>w.v===v.v?{...w,status:'CANCELLED' as const}:w)});
    return `Quotation ${q.number} v${v.v} cancelled (the number is kept, never reused)`;
  },
  QUO_EXPIRE:k=>{
    role(k,['Owner','Manager'],'the quotation expiry run (Manager or Owner)');
    const x=hallEnsure(k.s);let n=0;
    x.quotations=x.quotations.map(q=>{const v=hallLatest(q);if(v.status!=='SENT'||v.validUntil>=k.s.date)return q;n++;
      return {...q,versions:q.versions.map(w=>w.v===v.v?{...w,status:'EXPIRED' as const}:w)};});
    return `Quotation expiry · ${n} quotation(s) expired`;
  },
  HALL_TNC_TEMPLATE_SAVE:k=>{
    if(k.user.role!=='Owner')k.fail('HCC_TNC_OWNER_ONLY','Only the Owner edits T&C templates (PERM-HAL-005).');
    const x=hallEnsure(k.s),c=k.cmd,eventType=txt(c.eventType);
    k.assert(eventType==='GENERAL'||HALL_EVENT_TYPES.includes(eventType as never),'Choose the event type (or General).');
    const p=hallTncClausesProblem(c.clauses);k.assert(!p,p??'');
    const eff=txt(c.effectiveFrom)||k.s.date;k.assert(DATE.test(eff)&&eff>=k.s.date,'Effective from today on.');
    const all:TncTemplate[]=hallTemplates(x).map(t=>({...t,versions:t.versions.map(v=>({...v,clauses:v.clauses.map(c=>({...c}))}))}));
    const old=all.find(t=>t.eventType===eventType),clauses=hallTncClauses(c.clauses);
    let t:TncTemplate,vNo:number;
    if(old){vNo=Math.max(...old.versions.map(v=>v.v))+1;t={...old,versions:[...old.versions,{v:vNo,effectiveFrom:eff,clauses}]};}
    else{vNo=1;t={id:`T-${eventType}`,eventType:eventType as TncTemplate['eventType'],versions:[{v:1,effectiveFrom:eff,clauses}]};}
    x.templates=old?all.map(o=>o.id===old.id?t:o):[...all,t];
    return `T&C template ${t.id} v${vNo} saved · ${clauses.length} clauses · from ${eff} (sent quotations keep their own copy)`;
  },
};
