// F6-H3 · Hall booking in the production sync layer (module 27).
//   server    GET /hall/snapshot (venues, enquiries, quotations, events, lost reasons; every money field null for a caller without PERM-HAL-002 view, DEC-061)
//             POST /commands/AddHallEnquiry | UpdateHallEnquiry | LoseHallEnquiry | HoldHallEnquiry | UpsertHallVenue (PERM-HAL-001 / 006)
//                  AcceptQuotation | ReceiveHallAdvance | CloseHallEvent | CancelHallEvent | ApproveHallRefund (PERM-HAL-003 / 004, H1)
//                  RequestCreditNote {invoiceId, mode:'full'} for a hall invoice (the invoice source 'hall', Owner decides)
//   commands  HALL_ENQUIRY_ADD / _UPDATE / _LOST / _SOFT_HOLD, HALL_VENUE_UPSERT, QUO_ACCEPT, HALL_ADVANCE_RECEIVE, HALL_EVENT_CLOSE, HALL_EVENT_CANCEL, HALL_REFUND, HALL_CREDIT_NOTE
//   ids       venue = its code (V-SAGAR), enquiry ENQ-<n>, quotation QUO-<n>, event HEV-<n>, receipt HRC-<n>; the numbers are the server ids (IdDirectory kinds hallVenue / hallEnquiry / hallQuotation / hallEvent).
//             CreateQuotation | EditQuotation | ReviseQuotation | SendQuotation | OverrideTncPlaceholder | ApproveQuotationDiscount | LoseQuotation | CancelQuotation | SaveTncTemplate (H2, mapped by F6-int)
//   not yet   HALL_ROOM_BLOCK (the server blocks rooms inside AcceptQuotation when the request names roomBlock), HALL_NIGHT_RUN (the server night run does it), hall-menu master writes.
import {HccClientError} from '../errors';
import type {SyncArea} from './types';
import type {HallState,TncTemplate,TncSnapshot,Venue,Enquiry,Quotation,QuoteVersion,QuoteLine,AdvanceRow,HallEvent,HallReceipt,HallInvoice} from '../../domain/streams/hall';
import {hallSeed} from '../../fixtures/ext/hall';
import {hallTncSnapshot} from '../../domain/streams/hall-quote'; // FX-SMALL
import {hallHasPlaceholder} from '../../domain/streams/hall';
import {mnyPick,mnySameMethod,mnyIsDrawer} from '../../domain/streams/money-master';
import {shiftClosedText} from './money';
import type {State} from '../../domain/types';
// CU-PAY-B: the hall method is checked against the payment-methods master before any request (the server checks again) and sent under its CURRENT name.
// Refunds pass the event: a retired method is allowed only when it is the same master row as the tender of one of the event's ADVANCE / BALANCE receipts (DEC-MA-11a).
const hallPay=(s:State,v:unknown,reference:unknown,refundOf?:unknown)=>{
  const ev=refundOf===undefined?undefined:s.hallX?.events.find(e=>e.id===String(refundOf));
  const allowRetired=!!ev&&(ev.receipts??[]).some(r=>r.kind!=='REFUND'&&!!r.method&&mnySameMethod(s,String(v??''),r.method));
  const p=mnyPick(s,v,'frontdesk',reference,{allowRetired,requireReference:false});
  if(!p.method)return {problem:p.problem};
  if(p.method.needsReference&&String(reference??'').trim().length<3)return {problem:'Enter the payment reference (UPI / card / bank).'};
  // like the folio prechecks: a till (drawer) method needs the open shift before any request
  if(mnyIsDrawer(s,p.method.name)&&!s.shiftOpen)return {problem:shiftClosedText(s)};
  return {name:p.method.name};};
const hallName=(c:{state:State},v:unknown,reference:unknown,refundOf?:unknown)=>hallPay(c.state,v,reference,refundOf).name??String(v??'');
import {gstLineTax,gstSplit} from '../../domain/gst';

type SrvLine={seq:number;kind:string;refId:string|null;desc:string;qty:number;unit:string;ratePaise:number|null;listRatePaise:number|null;discountPaise:number|null;gstClass:string;taxBps:number|null;extra:boolean};
type SrvAdv={seq:number;label:string;pctBps:number|null;amountPaise:number|null;due:string;paidPaise:number|null};
type SrvVenue={venueId:number;code:string;name:string;kind:string;active:boolean;version:number;capacity:Record<string,number>;combinedOf:number[];minPlates:number|null;gstClass:string;
  slots:{slot:string;from:string;to:string;ratePaise:number|null;minPlates:number|null}[]};
type SrvEnquiry={enquiryId:number;name:string;phone:string;eventType:string;dates:string[];slot:string;pax:number;venueId:number|null;source:string;followUp:string;note:string|null;status:string;
  lostReasonId:string|null;lostNote:string|null;softVenueId:number|null;softUntil:string|null;version:number;guestId:number|null;companyId:number|null;createdUtc:string};
/** FX-SMALL: GET /hall/templates row (the Owner's saved T&C; code is the template id, e.g. T-WEDDING). */
export type SrvTncTemplate={templateId?:number;code:string;eventType:string;versions:{v:number;effectiveFrom:string;clauses:{n:number;heading:string;text:string}[]}[]};
type SrvVersion={tncTemplateCode?:string|null;tncV?:number|null;v:number;status:string;eventType:string;dates:string[];slot:string;venueId:number;setup:string;pax:number;guaranteedPax:number;validUntil:string;createdUtc:string;sentUtc:string|null;
  overrideReason:string|null;lines:SrvLine[];advance:SrvAdv[]};
type SrvQuotation={quotationId:number;number:string;fy:string;enquiryId:number;currentVersion:number;customerName:string;customerPhone:string;companyId:number|null;versions:SrvVersion[]};
type SrvReceipt={receiptId:number;date:string;kind:string;tender:string|null;companyId:number|null;udhaar:boolean;reference:string|null;amountPaise:number|null;atUtc:string};
type SrvEvent={eventId:number;quotationId:number;version:number;venueId:number;eventDate:string;dates:string[];slot:string;eventType:string;status:string;guaranteedPax:number;actualPax:number|null;
  customerName:string;customerPhone:string;companyId:number|null;blockId:number|null;invoiceNo:string|null;invoiceDate:string|null;totalPaise:number|null;taxablePaise:number|null;cgstPaise:number|null;
  sgstPaise:number|null;advanceAppliedPaise:number|null;balancePaise:number|null;settleMode:string|null;cancelDate:string|null;cancelKeptPaise:number|null;cancelRefundPaise:number|null;
  refundState:string|null;cancelReason:string|null;billedPax:number|null;lines:SrvLine[];advance:SrvAdv[];receipts:SrvReceipt[];createdUtc:string;invoiceId:number|null};
export type SrvHallSnapshot={seesMoney:boolean;venues:SrvVenue[];enquiries:SrvEnquiry[];quotations:SrvQuotation[];events:SrvEvent[];lostReasons:{id:string;label:string;needsNote:boolean}[]};

const n=(v:number|null|undefined)=>v??0;
const clock=(utc:string)=>utc.replace(/Z$/,'').slice(0,16);
const line=(l:SrvLine):QuoteLine=>({id:'L'+l.seq,kind:l.kind as QuoteLine['kind'],...(l.refId?{refId:l.refId}:{}),desc:l.desc,qty:l.qty,unit:l.unit,ratePaise:n(l.ratePaise),listRatePaise:n(l.listRatePaise),
  discountPaise:n(l.discountPaise),gstClass:l.gstClass,...(l.taxBps!==null?{taxBps:l.taxBps}:{})});
const advance=(a:SrvAdv):AdvanceRow=>({id:'ADV-'+a.seq,label:a.label,...(a.pctBps!==null?{pctBps:a.pctBps}:{}),amountPaise:n(a.amountPaise),due:a.due,paidPaise:n(a.paidPaise),receiptIds:[]});

const quoHeader=(c:any,h:any)=>h?{eventType:h.eventType,dates:h.dates,slot:h.slot,venueId:h.venueId?c.ids.server('hallVenue',String(h.venueId)):undefined,setup:h.setup,pax:h.pax,guaranteedPax:h.guaranteedPax}:undefined;
const quoLines=(ls:any)=>Array.isArray(ls)?ls.map((l:any)=>({kind:String(l.kind),desc:String(l.desc),qty:Number(l.qty),ratePaise:Number(l.ratePaise),refId:l.refId?String(l.refId):undefined,unit:l.unit,
  listRatePaise:l.listRatePaise===undefined?undefined:Number(l.listRatePaise),discountPaise:Number(l.discountPaise??0),gstClass:l.gstClass})):undefined;
const quoAdvance=(a:any)=>Array.isArray(a)&&a.length?a.map((r:any)=>({due:String(r.due),pctBps:r.pctBps,amountPaise:r.amountPaise,label:r.label})):undefined;
/** GET /hall/snapshot → s.hallX (pure; the prototype screens and rules read this shape). */
export function hallFromServer(r:SrvHallSnapshot,srvTemplates:readonly SrvTncTemplate[]=[]):HallState{
  const seed=hallSeed(),code=new Map(r.venues.map(v=>[v.venueId,v.code]));
  const vc=(id:number|null|undefined)=>(id===null||id===undefined?undefined:code.get(id)??'V-'+id);
  const venues:Venue[]=r.venues.map(v=>({id:v.code,name:v.name,kind:v.kind as Venue['kind'],capacity:v.capacity as Venue['capacity'],active:v.active,version:v.version,gstClass:v.gstClass,
    slots:v.slots.map(s=>({slot:s.slot as Venue['slots'][number]['slot'],from:s.from,to:s.to,ratePaise:n(s.ratePaise),...(s.minPlates?{minPlates:s.minPlates}:{})})),
    ...(v.minPlates?{minPlates:v.minPlates}:{}),...(v.combinedOf.length?{combinedOf:v.combinedOf.map(i=>vc(i)!)}:{})}));
  const enquiries:Enquiry[]=r.enquiries.map(q=>({id:'ENQ-'+q.enquiryId,name:q.name,phone:q.phone,eventType:q.eventType as Enquiry['eventType'],dates:q.dates,slot:q.slot as Enquiry['slot'],pax:q.pax,
    ...(q.venueId!==null?{venueId:vc(q.venueId)}:{}),source:q.source as Enquiry['source'],followUp:q.followUp,...(q.note?{note:q.note}:{}),status:q.status as Enquiry['status'],
    ...(q.lostReasonId?{lostReasonId:q.lostReasonId}:{}),...(q.lostNote?{lostNote:q.lostNote}:{}),
    ...(q.softVenueId!==null&&q.softUntil?{softHold:{venueId:vc(q.softVenueId)!,until:q.softUntil+'T23:59',by:''}}:{}),createdAt:clock(q.createdUtc),version:q.version}));
  // FX-SMALL: the Owner's saved terms (empty list = nothing saved yet, so the seeded placeholders stay in force, same as the demo).
  const templates:TncTemplate[]=srvTemplates.map(t=>({id:t.code,eventType:t.eventType as TncTemplate['eventType'],
    versions:t.versions.map(v=>({v:v.v,effectiveFrom:v.effectiveFrom,clauses:v.clauses.map(c=>({n:c.n,heading:c.heading,text:c.text}))}))}));
  const tncOf=(v:SrvVersion):TncSnapshot=>{
    const locked=v.tncTemplateCode&&v.tncV?templates.find(t=>t.id===v.tncTemplateCode)?.versions.find(x=>x.v===v.tncV):undefined;
    if(locked&&v.tncTemplateCode){const clauses=locked.clauses.map(c=>({...c}));return {templateId:v.tncTemplateCode,v:locked.v,clauses,hasPlaceholder:!clauses.length||hallHasPlaceholder(clauses)};}
    return hallTncSnapshot({...seed,templates} as HallState,v.eventType as Quotation['versions'][number]['header']['eventType'],(v.sentUtc??v.createdUtc).slice(0,10)); // a draft shows the version in force that day
  };
  const quotations:Quotation[]=r.quotations.map(q=>({id:'QUO-'+q.quotationId,number:q.number,fy:q.fy,enquiryId:'ENQ-'+q.enquiryId,currentVersion:q.currentVersion,
    customer:{name:q.customerName,phone:q.customerPhone},
    versions:q.versions.map((v):QuoteVersion=>({v:v.v,status:v.status as QuoteVersion['status'],header:{eventType:v.eventType as QuoteVersion['header']['eventType'],dates:v.dates,slot:v.slot as QuoteVersion['header']['slot'],
      venueId:vc(v.venueId)!,setup:v.setup as QuoteVersion['header']['setup'],pax:v.pax,guaranteedPax:v.guaranteedPax},lines:v.lines.map(line),advance:v.advance.map(advance),
      tnc:tncOf(v),validUntil:v.validUntil,createdBy:'',createdAt:clock(v.createdUtc),...(v.sentUtc?{sentAt:clock(v.sentUtc)}:{}),
      ...(v.overrideReason?{placeholderOverride:{by:'',reason:v.overrideReason,at:clock(v.createdUtc)}}:{})}))}));
  const events:HallEvent[]=r.events.map(e=>{
    const lines=e.lines.filter(l=>!l.extra).map(line),extras=e.lines.filter(l=>l.extra).map(line);
    const receipts:HallReceipt[]=e.receipts.map(x=>({id:'HRC-'+x.receiptId,eventId:'HEV-'+e.eventId,date:x.date,kind:x.kind as HallReceipt['kind'],...(x.tender?{method:x.tender}:{}),
      ...(x.companyId!==null?{company:'CO-'+x.companyId}:{}),...(x.udhaar?{udhaar:true}:{}),...(x.reference?{reference:x.reference}:{}),amountPaise:n(x.amountPaise),by:'',at:clock(x.atUtc)}));
    const priced=e.lines.map(l=>{const taxBps=l.taxBps??0,taxable=n(l.qty*n(l.ratePaise)-n(l.discountPaise)),g=gstSplit(gstLineTax(taxable,taxBps));
      return {...line(l),taxBps,taxablePaise:taxable,cgstPaise:g.cgst,sgstPaise:g.sgst};});
    const invoice:HallInvoice|undefined=e.invoiceNo&&e.totalPaise!==null?{no:e.invoiceNo,date:e.invoiceDate??e.eventDate,lines:priced,taxablePaise:n(e.taxablePaise),cgstPaise:n(e.cgstPaise),sgstPaise:n(e.sgstPaise),
      totalPaise:n(e.totalPaise),advanceAppliedPaise:n(e.advanceAppliedPaise),balancePaise:n(e.balancePaise),billedPax:n(e.billedPax),
      settle:{mode:(e.settleMode??'DUE') as HallInvoice['settle']['mode']},by:'',at:e.invoiceDate??e.eventDate}:undefined;
    const ev:HallEvent={id:'HEV-'+e.eventId,quotationId:'QUO-'+e.quotationId,version:e.version,venueIds:[vc(e.venueId)!],date:e.eventDate,...(e.dates.length>1?{extraDates:e.dates.filter(d=>d!==e.eventDate)}:{}),
      slot:e.slot as HallEvent['slot'],status:e.status as HallEvent['status'],lines,...(e.actualPax!==null?{actualPax:e.actualPax}:{}),guaranteedPax:e.guaranteedPax,extras,
      ...(e.blockId!==null?{blockId:'GB-'+e.blockId}:{}),...(invoice?{invoiceId:invoice.no,invoice}:{}),advance:e.advance.map(advance),receipts,
      customer:{name:e.customerName,phone:e.customerPhone},eventType:e.eventType as HallEvent['eventType'],createdAt:clock(e.createdUtc),by:''};
    if(e.status==='CANCELLED'&&e.cancelDate)ev.cancellation={at:e.cancelDate+'T00:00',date:e.cancelDate,keptPaise:n(e.cancelKeptPaise),refundPaise:n(e.cancelRefundPaise),by:'',daysBefore:0,keptBps:0,taxBps:0,
      refund:(e.refundState??'NONE') as 'NONE'|'PENDING'|'PAID',reason:e.cancelReason??''};
    return ev;});
  return {...seed,venues,enquiries,quotations,events,templates,lostReasons:r.lostReasons,seq:0};
}

const num=(v:unknown,prefix:string,what:string)=>{const m=new RegExp('^(?:'+prefix+'-)?(\\d+)$').exec(String(v??''));if(!m)throw new HccClientError('VALIDATION',`Choose the ${what}.`);return Number(m[1]);};
const day=(v:unknown)=>String(v??'');

export const hall:SyncArea={
  id:'hall',
  loaders:[{slice:'hallX',reads:['GET /hall/snapshot','GET /hall/templates'],async load(c){
    const r=await c.api.get<SrvHallSnapshot>('/hall/snapshot');
    for(const v of r.venues)c.ids.learn('hallVenue',v.code,v.venueId);
    for(const q of r.enquiries)c.ids.learn('hallEnquiry','ENQ-'+q.enquiryId,q.enquiryId);
    for(const q of r.quotations)c.ids.learn('hallQuotation','QUO-'+q.quotationId,q.quotationId);
    for(const e of r.events){c.ids.learn('hallEvent','HEV-'+e.eventId,e.eventId);if(e.invoiceNo&&e.invoiceId!==null)c.ids.learn('hallInvoice',e.invoiceNo,e.invoiceId);}
    // FX-SMALL: terms the Owner saved live in their own table; a role without the view keeps the seeded terms.
    const tp=await c.api.get<SrvTncTemplate[]>('/hall/templates').catch(()=>[] as SrvTncTemplate[]);
    return hallFromServer(r,tp);}}],
  commands:[
    {type:'HALL_ENQUIRY_ADD',reload:['hallX'],async send(c){const k=c.cmd;
      await c.api.command('AddHallEnquiry',{name:String(k.name??''),phone:String(k.phone??''),eventType:String(k.eventType??''),dates:k.dates,slot:String(k.slot??''),pax:Number(k.pax),
        source:String(k.source??''),followUp:day(k.followUp),venueId:k.venueId?c.ids.server('hallVenue',String(k.venueId)):undefined,note:k.note?String(k.note):undefined});}},
    {type:'HALL_ENQUIRY_UPDATE',reload:['hallX'],async send(c){const k=c.cmd;
      await c.api.command('UpdateHallEnquiry',{enquiryId:num(k.id,'ENQ','enquiry'),followUp:k.followUp,note:k.note,pax:k.pax,venueId:k.venueId===undefined?undefined:k.venueId?c.ids.server('hallVenue',String(k.venueId)):0,
        dates:k.dates,slot:k.slot,status:k.status});}},
    {type:'HALL_ENQUIRY_LOST',reload:['hallX'],async send(c){await c.api.command('LoseHallEnquiry',{enquiryId:num(c.cmd.id,'ENQ','enquiry'),reasonId:String(c.cmd.reasonId??''),note:c.cmd.note?String(c.cmd.note):undefined});}},
    {type:'HALL_ENQUIRY_SOFT_HOLD',reload:['hallX'],async send(c){const k=c.cmd;
      await c.api.command('HoldHallEnquiry',{enquiryId:num(k.id,'ENQ','enquiry'),venueId:k.venueId?c.ids.server('hallVenue',String(k.venueId)):undefined,days:k.days===undefined?3:Number(k.days),release:!!k.release});}},
    {type:'HALL_VENUE_UPSERT',reload:['hallX'],async send(c){const v=(c.cmd.venue??{}) as Record<string,any>;
      await c.api.command('UpsertHallVenue',{venueId:v.id?c.ids.server('hallVenue',String(v.id)):undefined,code:String(v.code??v.id??String(v.name??'').toUpperCase().replace(/[^A-Z0-9]+/g,'-').slice(0,20)),name:String(v.name??''),kind:String(v.kind??''),
        capacity:v.capacity,minPlates:v.minPlates,gstClass:v.gstClass,active:v.active!==false,expectedVersion:c.cmd.version===undefined?undefined:Number(c.cmd.version),
        combinedOf:Array.isArray(v.combinedOf)?v.combinedOf.map((id:string)=>c.ids.server('hallVenue',id)):undefined,
        slots:(v.slots??[]).map((s:any)=>({slot:s.slot,from:s.from,to:s.to,ratePaise:Number(s.ratePaise),minPlates:s.minPlates}))});}},
    {type:'QUO_CREATE',reload:['hallX'],async send(c){const k=c.cmd;
      await c.api.command('CreateQuotation',{enquiryId:num(k.enquiryId,'ENQ','enquiry'),header:quoHeader(c,k.header),lines:quoLines(k.lines),menuCode:k.menuId?String(k.menuId):undefined,advance:quoAdvance(k.advance)});}},
    {type:'QUO_EDIT',reload:['hallX'],async send(c){const k=c.cmd;
      await c.api.command('EditQuotation',{quotationId:num(k.id,'QUO','quotation'),version:k.version===undefined?undefined:Number(k.version),header:quoHeader(c,k.header),lines:quoLines(k.lines),advance:quoAdvance(k.advance)});}},
    ...([['QUO_REVISE','ReviseQuotation'],['QUO_SEND','SendQuotation'],['QUO_PLACEHOLDER_OVERRIDE','OverrideTncPlaceholder'],['QUO_DISCOUNT_APPROVE','ApproveQuotationDiscount'],
      ['QUO_LOST','LoseQuotation'],['QUO_CANCEL','CancelQuotation']] as const).map(([type,op])=>({type,reload:['hallX'] as ['hallX'],async send(c:any){const k=c.cmd;
      await c.api.command(op,{quotationId:num(k.id,'QUO','quotation'),version:k.version===undefined?undefined:Number(k.version),reason:k.reason?String(k.reason):undefined,
        reasonId:k.reasonId?String(k.reasonId):undefined,note:k.note?String(k.note):undefined});}})),
    {type:'HALL_TNC_TEMPLATE_SAVE',reload:['hallX'],async send(c){const k=c.cmd;
      await c.api.command('SaveTncTemplate',{eventType:String(k.eventType??''),clauses:(Array.isArray(k.clauses)?k.clauses:[]).map((x:any)=>({heading:String(x.heading??''),text:String(x.text??'')})),effectiveFrom:k.effectiveFrom?day(k.effectiveFrom):undefined});}},
    {type:'QUO_ACCEPT',reload:['hallX'],
      precheck(cmd,s){const a=cmd.advance as Record<string,any>|undefined;return a&&Number(a.amountPaise)>0?hallPay(s,a.method,a.reference).problem:undefined;},
      async send(c){const k=c.cmd,a=k.advance as Record<string,any>|undefined;
      await c.api.command('AcceptQuotation',{quotationId:num(k.id,'QUO','quotation'),version:k.version===undefined?undefined:Number(k.version),reason:k.reason?String(k.reason):undefined,
        capacityReason:k.capacityReason?String(k.capacityReason):undefined,advance:a&&Number(a.amountPaise)>0?{amountPaise:Number(a.amountPaise),method:hallName(c,a.method,a.reference),reference:a.reference}:undefined});}},
    {type:'HALL_ADVANCE_RECEIVE',reload:['hallX'],precheck:(cmd,s)=>hallPay(s,cmd.method,cmd.reference).problem,async send(c){const k=c.cmd;
      await c.api.command('ReceiveHallAdvance',{eventId:num(k.eventId??k.id,'HEV','event'),amountPaise:Number(k.amountPaise),method:hallName(c,k.method,k.reference),reference:k.reference?String(k.reference):undefined});}},
    {type:'HALL_EVENT_CLOSE',reload:['hallX'],
      precheck(cmd,s){const st=(cmd.settle??{}) as Record<string,any>;return String(st.mode??'')==='TENDER'&&String(st.method??'').trim()?hallPay(s,st.method,st.reference).problem:undefined;},
      async send(c){const k=c.cmd,s=(k.settle??{}) as Record<string,any>;
      await c.api.command('CloseHallEvent',{eventId:num(k.eventId??k.id,'HEV','event'),actualPax:Number(k.actualPax),
        extras:(k.extras??[]).map((x:any)=>({desc:x.desc,qty:Number(x.qty),ratePaise:Number(x.ratePaise),listRatePaise:x.listRatePaise,kind:x.kind,unit:x.unit,gstClass:x.gstClass})),
        settle:{mode:String(s.mode??'DUE'),method:String(s.mode??'')==='TENDER'?hallName(c,s.method,s.reference):s.method,reference:s.reference,companyId:s.company?num(s.company,'CO','company'):undefined}});}},
    {type:'HALL_EVENT_CANCEL',reload:['hallX'],async send(c){await c.api.command('CancelHallEvent',{eventId:num(c.cmd.id??c.cmd.eventId,'HEV','event'),reason:String(c.cmd.reason??'')});}},
    {type:'HALL_REFUND',reload:['hallX'],precheck:(cmd,s)=>hallPay(s,cmd.method,cmd.reference,cmd.id??cmd.eventId).problem,async send(c){await c.api.command('ApproveHallRefund',{eventId:num(c.cmd.id??c.cmd.eventId,'HEV','event'),method:hallName(c,c.cmd.method,c.cmd.reference,c.cmd.id??c.cmd.eventId),reference:c.cmd.reference?String(c.cmd.reference):undefined});}},
    {type:'HALL_CREDIT_NOTE',reload:['hallX'],async send(c){
      const e=c.state.hallX?.events.find(x=>x.id===String(c.cmd.id??c.cmd.eventId)),invoiceId=e?.invoice?c.ids.server('hallInvoice',e.invoice.no):undefined;
      if(!invoiceId)throw new HccClientError('VALIDATION','Only a billed event has an invoice to credit.');
      await c.api.command('RequestCreditNote',{invoiceId,mode:'full',reason:String(c.cmd.reason??'')});}},
  ],
  unmapped:{
    HALL_ROOM_BLOCK:'on the server the rooms are blocked inside AcceptQuotation (roomBlock), refused when the rooms are not free; a later block or release is done from Groups',HALL_NIGHT_RUN:'the server night run closes lapsed enquiries',
    HALL_MENU_UPSERT:'menu packages are read from the server (GET /hall/menus); edit them in the menu master on the server',
  },
};
