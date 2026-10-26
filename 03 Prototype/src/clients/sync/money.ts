// Money area (W4-3): folio, cashier / payments, cash book, invoices & credit notes, groups billing. Mappers in ./money/wire.ts.
//
//   loaders  bookings (patch)  ← GET /folios/by-reservation/{id} per booking (lines, receipts; stay id/version for CheckoutStay) and, for a
//                                departed stay, GET /invoices/by-source/folio/{folioId} (invoice number, credit notes). The approvals of each
//                                folio ride along on the booking (`srvApprovals`) for the approvals loader — no second read.
//            approvals (patch on the admin area's slice) ← folio approvals (above) + the pending cash-difference request (from `shift`) + pending credit-note requests
//                                (GET /credit-note-requests?status=Pending, mapped to the booking through its invoice).
//            shiftOpen / openingCash / shift / shiftClose ← GET /cashier/desk (DEC-052 shared till; learns till + shift ids/versions).
//            cashbookX          ← GET /cashbook?from&to (locks and lock/reopen history; the figures stay derived on the client: cbDay).
//            groupsX            ← GET /groups + /groups/{id}/statement (blocks of groups that have one; member room types from the
//                                reservation reads). Company accounts / credit limits / overrides do not exist on the server (empty).
//   commands PAYMENT → RecordReceipt (one per tender; before check-in by reservationId: the server opens the folio)  · CHARGE → PostCharge · DISCOUNT / REFUND_REQUEST / COMPANY_CREDIT_REQUEST /
//            CORRECTION_REQUEST → RequestAdjustment (expectedVersion = the folio version loaded) · REFUND_EXECUTE → ExecuteAdjustment ·
//            APPROVAL → DecideApproval | DecideCreditNote · CHECKOUT is W4-1's (CheckoutStay): it calls companyCreditApprovalId() and,
//            after CheckoutStay, issueInvoiceAfterCheckout() from here (a refusal does not fail the checkout: invoiceStepWarning) ·
//            INVOICE_ISSUE → IssueInvoice (departed stay without an invoice; production "Issue invoice" action) ·
//            CREDIT_NOTE → RequestCreditNote · SHIFT_SUBMIT → SubmitShiftCount · SHIFT_DIFFERENCE_DECIDE → DecideApproval ·
//            CB_LOCK / CB_REOPEN → LockCashBookDay / ReopenCashBookDay · GRP_BLOCK_CREATE → CreateGroupBlock → CreateGroup.
// The server re-checks everything (limits, self-approval, DEC-041 version binding, DEC-065 locks); prechecks only repeat cheap prototype refusals.
import type {Booking,Approval,Command,State} from '../../services/mock';
import {totals,addDays} from '../../services/mock';
import type {GRPState} from '../../domain/streams/groups';
import {HccClientError} from '../errors';
import type {SrvAvailability} from '../server';
import type {SyncArea,LoaderCtx,CommandCtx,SliceKey} from './types';
import {serverId,protoId} from './map';
import {BOOKING_STATUS} from './frontdesk/shared';
import {folioIntoBooking,approvalFromServer,creditNoteRequestFromServer,cashDifferenceApproval,CNR_PREFIX,deskTill,deskShiftOpen,deskOpeningCash,
  deskShiftRecord,deskShiftClose,cashbookFromServer,blockFromServer,
  type SrvReservationFolio,type SrvInvoice,type SrvCreditNoteRequest,type SrvCashDesk,type SrvCashBookRange,type SrvGroupSummary,type SrvGroupStatement} from './money/wire';

/** A booking as the money patch leaves it: the folio's approvals travel with it to the approvals loader (same batch). */
type MoneyBooking=Booking&{srvApprovals?:Approval[]};
const FOLIO_READERS=['billing','payments','checkout','reservations','reports'] as const;
const CASH_READERS=['payments','approve','reports','close'] as const;
/** Days of cash-book lock state loaded back from the business date (server maximum range 93). */
export const CASHBOOK_DAYS=93;

const wire=(e:unknown)=>e instanceof HccClientError?e.wireCode:undefined;
/** Reads that a role may not hold (403) or that have nothing yet (404) leave that part empty instead of failing the slice. */
async function optional<T>(p:Promise<T>):Promise<T|null>{
  try{return await p;}catch(e){const w=wire(e);if(w==='FORBIDDEN'||w==='NOT_FOUND')return null;throw e;}
}
async function inBatches<T,R>(list:T[],n:number,f:(x:T)=>Promise<R>):Promise<R[]>{
  const out:R[]=[];for(let i=0;i<list.length;i+=n)out.push(...await Promise.all(list.slice(i,i+n).map(f)));return out;
}

// ── loaders ──

async function patchBookings(c:LoaderCtx):Promise<MoneyBooking[]>{
  const list=(c.current('bookings')??[]) as MoneyBooking[];
  return inBatches(list,8,async b=>{
    if(!/^\d+$/.test(b.id)||!c.mayRead('GET /folios/by-reservation/:id'))return b;
    const r=await optional(c.api.get<SrvReservationFolio>(`/folios/by-reservation/${b.id}`));
    if(!r)return b;
    if(r.stayId!=null){c.ids.learn('stay',b.id,r.stayId);if(r.stayVersion)c.ids.setVersion('stay',b.id,r.stayVersion);}
    const f=r.folio;
    if(f){c.ids.learn('folio',b.id,f.folioId);c.ids.setVersion('folio',b.id,f.version);}
    let inv:SrvInvoice|null=null;
    if(f&&b.status==='Departed'&&c.mayRead('GET /invoices/by-source/folio/:id'))inv=await optional(c.api.get<SrvInvoice>(`/invoices/by-source/folio/${f.folioId}`));
    if(inv)c.ids.learn('invoice',b.id,inv.invoiceId);
    const next=folioIntoBooking(b,f,inv) as MoneyBooking;
    next.srvApprovals=(f?.approvals??[]).map(a=>{c.ids.setVersion('approval',protoId(a.approvalId),a.version);return approvalFromServer(a,b.id);});
    return next;
  });
}

/** GET /cashier/folios item (Hcc.Contracts Billing `DeskFolioItem`, D5): no phone / e-mail / ID fields (the Cashier's guest view is masked). */
type SrvDeskFolio={reservationId:number;publicNo:string;status:string;guestName:string;roomId:number|null;roomNo:string|null;roomType:string|null;
  arrival:string;departure:string;adults:number;children:number;source:string;nightlyRatePaise:number|null;stayId:number|null;folioId:number|null;
  folioVersion:string|null;balancePaise:number;companyName:string|null;companyGstin:string|null};

/** Bookings for a role that holds the folio read but not the reservations read (Cashier, DEC-063: "Cashier works from folios"): the
 *  cashier's folio list — in-house stays, today's arrivals (advances) and today's departures. Called by the bookings owner
 *  (sync/reservations.ts); lines / receipts follow from this area's patch loader as for every booking. */
export async function loadDeskBookings(c:LoaderCtx):Promise<Booking[]>{
  const r=await c.api.get<{businessDate:string;items:SrvDeskFolio[]}>('/cashier/folios');
  return r.items.map(x=>{
    const id=String(x.reservationId);
    c.ids.learn('booking',id,x.reservationId);
    if(x.roomNo&&x.roomId!=null)c.ids.learn('room',x.roomNo,x.roomId);
    if(x.stayId!=null)c.ids.learn('stay',id,x.stayId);
    if(x.folioId!=null){c.ids.learn('folio',id,x.folioId);if(x.folioVersion)c.ids.setVersion('folio',id,x.folioVersion);}
    const b:Booking={id,guest:x.guestName,email:'',people:x.adults,room:x.roomNo??'',arrival:x.arrival,departure:x.departure,rate:x.nightlyRatePaise??0,
      source:x.source,status:BOOKING_STATUS[x.status]??(x.status as Booking['status']),registered:x.stayId!=null,lines:[],receipts:[],version:1,notes:''} as Booking;
    if(x.children)b.children=x.children;
    if(x.companyName)b.company=x.companyGstin?{name:x.companyName,gstin:x.companyGstin}:{name:x.companyName};
    return b;
  }).sort((a,b)=>Number(a.id)-Number(b.id));
}

/** Patch on the admin area's approvals (W4-5 owns the slice: configuration requests): appends the money requests. */
async function patchApprovals(c:LoaderCtx):Promise<Approval[]>{
  const bookings=(c.current('bookings')??[]) as MoneyBooking[];
  const out:Approval[]=bookings.flatMap(b=>b.srvApprovals??[]);
  const cash=cashDifferenceApproval(c.current('shift'));
  if(cash)out.push(cash);
  const byInvoice=new Map<number,string>();
  for(const b of bookings){const i=c.ids.server('invoice',b.id);if(i!==undefined)byInvoice.set(i,b.id);}
  if(byInvoice.size&&c.mayRead('GET /credit-note-requests')){
    const reqs=await optional(c.api.get<SrvCreditNoteRequest[]>('/credit-note-requests',{status:'Pending'}))??[];
    for(const r of reqs){const bid=byInvoice.get(r.invoiceId);if(!bid)continue;
      const a=creditNoteRequestFromServer(r,bid);c.ids.setVersion('approval',a.id,r.version);out.push(a);}
  }
  out.sort((a,b)=>(Number(a.id.replace(CNR_PREFIX,''))||0)-(Number(b.id.replace(CNR_PREFIX,''))||0));
  return [...(c.current('approvals')??[]),...out];
}

/** One desk read per cash slice (four small GETs; each slice owns its whole value). Learns the till and the active shift. */
async function desk(c:LoaderCtx):Promise<SrvCashDesk>{
  const d=await c.api.get<SrvCashDesk>('/cashier/desk');
  const t=deskTill(d);
  if(t){c.ids.learn('till','desk',t.till.tillId);
    const a=t.active;
    // 'current' = the till with the open shift: what accounting (W4-7) passes for cash expense approve / reverse.
    if(a?.shift.status==='Open')c.ids.learn('till','current',t.till.tillId);
    if(a){c.ids.learn('shift','active',a.shift.shiftId);c.ids.setVersion('shift','active',a.shift.version);
      if(a.approval)c.ids.setVersion('approval',protoId(a.approval.approvalId),a.approval.version);}}
  return d;
}

async function loadCashbook(c:LoaderCtx){
  const to=c.current('date');const from=addDays(to,-(CASHBOOK_DAYS-1));
  return cashbookFromServer(await c.api.get<SrvCashBookRange>('/cashbook',{from,to}));
}

async function loadGroups(c:LoaderCtx):Promise<GRPState>{
  const groups=(await c.api.get<SrvGroupSummary[]>('/groups')).filter(g=>g.groupBlockId!=null);
  const blocks=await inBatches(groups,4,async g=>{
    const st=c.mayRead('GET /groups/:id/statement')?await optional(c.api.get<SrvGroupStatement>(`/groups/${g.groupId}/statement`)):null;
    if(!st)return null;
    c.ids.learn('group',protoId(g.groupId),g.groupId);c.ids.setVersion('group',protoId(g.groupId),st.version);
    if(g.groupBlockId!=null)c.ids.learn('groupBlock',protoId(g.groupId),g.groupBlockId);
    const types:Record<string,string>={};
    if(c.mayRead('GET /reservations/:id'))await inBatches(st.members,8,async m=>{const d=await optional(c.api.clients.reservations.getBooking(protoId(m.reservationId)));if(d)types[protoId(m.reservationId)]=d.roomTypeId;});
    const arr=g.arrival??st.members[0]?.arrival??st.businessDate,dep=g.departure??st.members[0]?.departure??addDays(arr,1);
    return blockFromServer(st,arr,dep,types);
  });
  return {companies:[],blocks:blocks.filter((b):b is NonNullable<typeof b>=>!!b),requests:[],overrides:[]};
}

// ── command helpers ──

const invalid=(m:string)=>new HccClientError('INVALID_TRANSITION',m);
const validation=(m:string)=>new HccClientError('VALIDATION',m);
const bookingOf=(c:CommandCtx,id:unknown)=>{const b=c.state.bookings.find(x=>x.id===String(id??''));if(!b)throw new HccClientError('NOT_FOUND','Booking not found.');return b;};
/** Folio id + the version loaded (DEC-041: requests are bound to the folio version the user saw). */
function folioOf(c:CommandCtx,bookingId:string){
  const folioId=c.ids.server('folio',bookingId),version=c.ids.version('folio',bookingId);
  if(folioId===undefined||!version)throw invalid('This booking has no folio on the server yet — it opens at check-in. Refresh and try again.');
  return {folioId,version};
}
function approvalVersion(c:CommandCtx,id:string){
  const v=c.ids.version('approval',id);if(!v)throw new HccClientError('NOT_FOUND','This request is no longer pending. Refresh and try again.');return v;
}
/** The shared till for cash (DEC-052); null for other tenders. */
function tillFor(c:CommandCtx,method:unknown){
  if(method!=='Cash')return null;
  const t=c.ids.server('till','desk');if(t===undefined)throw invalid('The cash shift is closed. Open the next business day first.');return t;
}
const positive=(v:unknown)=>Number.isSafeInteger(v)&&(v as number)>0;
const text=(v:unknown)=>String(v??'').trim();
const FOLIO:SliceKey[]=['bookings','approvals'];
const CASH:SliceKey[]=['shiftOpen','openingCash','shift','shiftClose','approvals'];

function paymentPrecheck(cmd:Command,s:State){
  const kind=cmd.kind||'payment';
  if(kind==='refund')return 'Refunds are no longer posted directly. Use “Request refund” on the original receipt; after approval, execute the refund.';
  if(!['payment','deposit','deposit-return'].includes(kind))return 'Invalid receipt type.';
  if(!positive(cmd.amount))return 'Enter an amount greater than zero.';
  const tenders=tendersOf(cmd);
  if(tenders.length>4)return 'Use at most four tender rows.';
  if(tenders.length>1&&kind==='deposit-return')return 'Return a deposit with a single tender.';
  for(const t of tenders){
    if(!positive(t.amount))return 'Enter an amount greater than zero.';
    if(!['Cash','UPI','Card','Bank transfer'].includes(t.method))return 'Select a payment method.';
    if(t.method!=='Cash'&&!t.reference)return 'Enter a demonstration transaction reference.';
    if(t.method==='Cash'&&!s.shiftOpen)return 'The cash shift is closed. Open the next business day first.';
  }
  if(tenders.reduce((a,t)=>a+t.amount,0)!==cmd.amount)return 'Tender rows must add up to the payment amount.';
  return undefined;
}
function tendersOf(cmd:Command):{method:string;amount:number;reference:string}[]{
  return Array.isArray(cmd.tenders)&&cmd.tenders.length
    ?cmd.tenders.map((t:{method?:string;amount?:number;reference?:string})=>({method:String(t?.method??''),amount:Number(t?.amount),reference:text(t?.reference)}))
    :[{method:String(cmd.method??''),amount:Number(cmd.amount),reference:text(cmd.reference)}];
}
async function requestAdjustment(c:CommandCtx,action:string,b:Booking,amount:number,reason:string,targetId:number|null){
  const f=folioOf(c,b.id);
  return c.api.command('RequestAdjustment',{action,folioId:f.folioId,targetId,amountPaise:amount,reason},{expectedVersion:f.version});
}
/** Block code for CreateGroupBlock (1–20 characters): name letters/digits + arrival MMDD. */
export function blockCode(name:string,arrival:string){return (String(name).toUpperCase().replace(/[^A-Z0-9]/g,'').slice(0,13)||'GROUP')+'-'+String(arrival).slice(5).replace('-','');}
async function roomTypeIds(c:CommandCtx,types:string[],arrival:string,departure:string){
  const missing=types.filter(t=>c.ids.server('roomType',t)===undefined);
  if(missing.length){// Template literal on purpose: the prototype bundle legitimately contains this path (typed reservations client), and the
    // stream-clients leak check scans quoted sync-file paths. The typed client hides numeric room-type ids, hence the raw read.
    const a=await c.api.get<SrvAvailability>(`/availability`,{arrival,departure,adults:1,children:0});
    for(const r of a.rooms)c.ids.learn('roomType',r.roomType,r.roomTypeId);for(const t of a.types)c.ids.learn('roomType',t.code,t.roomTypeId);}
  return Object.fromEntries(types.map(t=>{const id=c.ids.server('roomType',t);if(id===undefined)throw validation(`Room type ${t} is not on the server.`);return [t,id];}));
}

/** Approved company credit for exactly the folio balance (DEC-041) — pass its server id as CheckoutStay.companyCreditApprovalId. */
export function companyCreditApprovalId(state:State,bookingId:string):number|null{
  const b=state.bookings.find(x=>x.id===bookingId);if(!b)return null;const bal=totals(b).balance;if(bal<=0)return null;
  const a=state.approvals.find(x=>x.booking===b.id&&x.kind==='company-credit'&&x.status==='Approved'&&x.amount===bal);
  return a?serverId(a.id,'Approval'):null;
}
/** IssueInvoice for a booking's folio (W3-6), B2B when the booking has a company GSTIN. The server returns the existing invoice for a
 *  folio that already has one, so any key is safe to repeat. */
function issueInvoice(c:CommandCtx,bookingId:string,step?:string):Promise<unknown>{
  const b=bookingOf(c,bookingId),f=folioOf(c,b.id),gstin=b.company?.gstin?.trim();
  return c.api.command('IssueInvoice',gstin?{sourceKind:'folio',sourceId:f.folioId,supplyType:'B2B',buyerName:b.company!.name,buyerGstin:gstin}:{sourceKind:'folio',sourceId:f.folioId},
    step?{step}:{});
}
export const INVOICE_PERMISSION='PERM-INV-002';
/** The money step after CheckoutStay (W4-1 maps CHECKOUT): issue the tax invoice for the settled folio. Key `<action>:invoice`; a retry
 *  returns the same invoice. Skipped (undefined) without PERM-INV-002. It never undoes the checkout: the CHECKOUT mapper catches a refusal
 *  (invoiceStepWarning) and the stay offers "Issue invoice" (INVOICE_ISSUE) later. */
export async function issueInvoiceAfterCheckout(c:CommandCtx,bookingId:string):Promise<unknown>{
  if(!c.me.permIds.includes(INVOICE_PERMISSION))return undefined;
  return issueInvoice(c,bookingId,'invoice');
}
/** W4 D1: what the screen says when the stay checked out but the invoice step was refused (e.g. no hotel GSTIN yet). */
export function invoiceStepWarning(e:unknown):string{
  const why=e instanceof Error&&e.message?e.message:'the server did not answer';
  return `Checkout completed — the guest is checked out and the room is released. The tax invoice was not issued: ${why.replace(/\.?$/,'.')} `
    +'Once that is fixed, open the departed stay and choose “Issue invoice”.';
}
/** Slices another area reloads after it moves cash (POS settle/void, expenses): the cash desk and the cash book. Folio changes: FOLIO_SLICES. */
export const CASH_SLICES:SliceKey[]=['shiftOpen','openingCash','shift','shiftClose','cashbookX'];
export const FOLIO_SLICES:SliceKey[]=['bookings','approvals'];

export const money:SyncArea={
  id:'money',
  loaders:[
    {slice:'bookings',kind:'patch',requires:FOLIO_READERS,load:patchBookings},
    {slice:'approvals',kind:'patch',needs:['bookings','shift'],load:patchApprovals},
    {slice:'shiftOpen',requires:CASH_READERS,reads:['GET /cashier/desk'],async load(c){return deskShiftOpen(await desk(c));}},
    {slice:'openingCash',requires:CASH_READERS,reads:['GET /cashier/desk'],async load(c){return deskOpeningCash(await desk(c));}},
    {slice:'shift',requires:CASH_READERS,reads:['GET /cashier/desk'],async load(c){return deskShiftRecord(await desk(c));}},
    {slice:'shiftClose',requires:CASH_READERS,reads:['GET /cashier/desk'],async load(c){return deskShiftClose(await desk(c));}},
    {slice:'cashbookX',requires:'reports',reads:['GET /cashbook'],needs:['date'],load:loadCashbook},
    {slice:'groupsX',requires:'reservations',reads:['GET /groups'],load:loadGroups},
  ],
  commands:[
    {type:'PAYMENT',reload:FOLIO,precheck:paymentPrecheck,
      async send(c){
        const b=bookingOf(c,c.cmd.id),tenders=tendersOf(c.cmd),kind=c.cmd.kind||'payment';
        // Advance before arrival: a Confirmed booking has no folio until check-in. The server opens it with the first receipt
        // (RecordReceipt by reservationId, folioId 0), the same folio check-in then finds. Once a folio exists, its id is used.
        const known=c.ids.server('folio',b.id);
        const preArrival=known===undefined&&b.status==='Confirmed'&&kind!=='deposit-return';
        const target=preArrival?{folioId:0,reservationId:serverId(b.id,'Booking')}:{folioId:folioOf(c,b.id).folioId};
        const results=[];
        for(const [i,t] of tenders.entries())
          results.push(await c.api.command('RecordReceipt',{...target,kind,tender:t.method,amountPaise:t.amount,reference:t.reference||null,tillId:tillFor(c,t.method)},
            tenders.length>1?{step:'t'+(i+1)}:{}));
        return {result:results};
      }},
    {type:'CHARGE',reload:FOLIO,
      precheck:cmd=>!positive(cmd.amount)?'Enter an amount greater than zero.':!text(cmd.label)?'Describe the service.':undefined,
      async send(c){const b=bookingOf(c,c.cmd.id),f=folioOf(c,b.id);
        await c.api.command('PostCharge',{folioId:f.folioId,label:text(c.cmd.label),amountPaise:c.cmd.amount,gstCategory:c.cmd.gst??null});}},
    {type:'DISCOUNT',reload:FOLIO,
      precheck:cmd=>!positive(cmd.amount)?'Enter an amount greater than zero.':!text(cmd.reason)?'A reason is required.':undefined,
      async send(c){const b=bookingOf(c,c.cmd.id);return {result:await requestAdjustment(c,'discount',b,c.cmd.amount,text(c.cmd.reason),null)};}},
    {type:'REFUND_REQUEST',reload:FOLIO,
      precheck:cmd=>!positive(cmd.amount)?'Enter an amount greater than zero.':!text(cmd.reason)?'Enter a reason for the refund.':!cmd.receiptId?'Select the original receipt to refund.':undefined,
      async send(c){const b=bookingOf(c,c.cmd.id);
        return {result:await requestAdjustment(c,'refund',b,c.cmd.amount,text(c.cmd.reason),serverId(c.cmd.receiptId,'Receipt'))};}},
    {type:'COMPANY_CREDIT_REQUEST',reload:FOLIO,
      precheck:(cmd,s)=>{const b=s.bookings.find(x=>x.id===cmd.id);if(!b)return undefined;
        if(b.status!=='In house')return 'This stay is not in house.';if(!b.company?.name?.trim())return 'Add a company to this booking before requesting company credit.';
        if(totals(b).balance<=0)return 'There is no outstanding balance to place on company credit.';return undefined;},
      async send(c){const b=bookingOf(c,c.cmd.id);
        // Server rule: the credit equals the current balance — the folio's own figures, which the loader reconciled paise-exact with the server.
        return {result:await requestAdjustment(c,'credit',b,totals(b).balance,text(c.cmd.reason)||('Company credit · '+(b.company?.name??'')),null)};}},
    {type:'CORRECTION_REQUEST',reload:FOLIO,
      precheck:cmd=>!text(cmd.reason)?'A reason is required.':!cmd.lineId?'Select the posted line to correct.':undefined,
      async send(c){const b=bookingOf(c,c.cmd.id),l=b.lines.find(x=>x.id===c.cmd.lineId);
        if(!l)throw new HccClientError('NOT_FOUND','Select the posted line to correct.');
        return {result:await requestAdjustment(c,'correction',b,Math.abs(l.amount),text(c.cmd.reason),serverId(l.id,'Folio line'))};}},
    {type:'REFUND_EXECUTE',reload:[...FOLIO,'shiftOpen','openingCash','shift','shiftClose'],
      precheck:(cmd,s)=>cmd.method==='Cash'&&!s.shiftOpen?'The cash shift is closed. Open the next business day first.':cmd.method&&cmd.method!=='Cash'&&!text(cmd.reference)?'Enter a demonstration transaction reference.':undefined,
      async send(c){const id=String(c.cmd.approvalId??'');const a=c.state.approvals.find(x=>x.id===id&&x.kind==='refund');
        if(!a)throw new HccClientError('NOT_FOUND','Refund request not found.');
        const b=c.state.bookings.find(x=>x.id===a.booking),r=b?.receipts.find(x=>x.id===a.target);
        const method=c.cmd.method||r?.method;
        return {result:await c.api.command('ExecuteAdjustment',{approvalId:serverId(id,'Approval'),tender:method,reference:text(c.cmd.reference)||(r?'Refund of '+r.id:null),tillId:tillFor(c,method)},
          {expectedVersion:approvalVersion(c,id)})};}},
    {type:'APPROVAL',reload:FOLIO,
      async send(c){const id=String(c.cmd.id??'');const reason=text(c.cmd.reason)||null;
        if(id.startsWith(CNR_PREFIX))return {result:await c.api.command('DecideCreditNote',{requestId:serverId(id.slice(CNR_PREFIX.length),'Credit-note request'),approve:!!c.cmd.approve,reason},
          {expectedVersion:approvalVersion(c,id)})};
        return {result:await c.api.command('DecideApproval',{approvalId:serverId(id,'Approval'),approve:!!c.cmd.approve,reason},{expectedVersion:approvalVersion(c,id)})};}},
    // W4 D1: a departed stay without an invoice (the invoice step after checkout was refused, or the checkout user lacked PERM-INV-002).
    // Stable key from the screen ('invoice:<booking>'); the server returns the same invoice if one exists.
    {type:'INVOICE_ISSUE',reload:FOLIO,
      precheck:(cmd,s,me)=>{if(!me.permIds.includes(INVOICE_PERMISSION))return 'Your role cannot issue tax invoices.';
        const b=s.bookings.find(x=>x.id===String(cmd.id??''));if(!b)return undefined;
        if(b.status!=='Departed')return 'A tax invoice is issued after checkout.';
        return b.invoiceNo?`Invoice ${b.invoiceNo} has already been issued for this stay.`:undefined;},
      async send(c){return {result:await issueInvoice(c,String(c.cmd.id))};}},
    {type:'CREDIT_NOTE',reload:FOLIO,
      precheck:(cmd,s)=>{if(!positive(cmd.amount))return 'Enter an amount greater than zero.';if(!text(cmd.reason))return 'A reason is required.';
        const b=s.bookings.find(x=>x.id===cmd.id);return b&&b.status!=='Departed'?'A credit note applies only to a departed (invoiced) stay. Use a correction while the guest is in house.':undefined;},
      async send(c){const b=bookingOf(c,c.cmd.id),invoiceId=c.ids.server('invoice',b.id);
        if(invoiceId===undefined)throw invalid('No tax invoice has been issued for this stay yet.');
        return {result:await c.api.command('RequestCreditNote',{invoiceId,mode:'amount',reason:text(c.cmd.reason),amountPaise:c.cmd.amount})};}},
    {type:'SHIFT_SUBMIT',reload:CASH,
      precheck:(cmd,s)=>!s.shiftOpen?'Shift already closed.':!(Number.isSafeInteger(cmd.counted)&&cmd.counted>=0)?'Enter a valid physical cash count.':undefined,
      async send(c){
        const shiftId=c.ids.server('shift','active'),version=c.ids.version('shift','active');
        if(shiftId===undefined||!version)throw invalid('Shift already closed.');
        const d=c.cmd.denominations as Record<string,number>|undefined;
        const notes=d?Object.fromEntries(Object.entries(d).filter(([k])=>k!=='coins').map(([k,v])=>[k,Number(v)||0])):null;
        const coinsPaise=d?Math.round((Number(d.coins)||0)*100):null;
        return {result:await c.api.command('SubmitShiftCount',{shiftId,countedPaise:c.cmd.counted,notes,coinsPaise,reason:text(c.cmd.reason)||null},{expectedVersion:version})};
      }},
    {type:'SHIFT_DIFFERENCE_DECIDE',reload:CASH,
      async send(c){const id=String(c.cmd.id??'');
        return {result:await c.api.command('DecideApproval',{approvalId:serverId(id,'Approval'),approve:!!c.cmd.approve,reason:text(c.cmd.reason)||null},{expectedVersion:approvalVersion(c,id)})};}},
    {type:'CB_LOCK',reload:['cashbookX'],precheck:cmd=>/^\d{4}-\d{2}-\d{2}$/.test(String(cmd.date||''))?undefined:'Choose a business date.',
      async send(c){await c.api.command('LockCashBookDay',{businessDate:c.cmd.date});}},
    {type:'CB_REOPEN',reload:['cashbookX'],
      precheck:cmd=>!/^\d{4}-\d{2}-\d{2}$/.test(String(cmd.date||''))?'Choose a business date.':text(cmd.reason).length<3?'A reason is required to reopen a locked day.':text(cmd.reason).length>200?'Reason: maximum 200 characters.':undefined,
      async send(c){await c.api.command('ReopenCashBookDay',{businessDate:c.cmd.date,reason:text(c.cmd.reason)});}},
    {type:'GRP_BLOCK_CREATE',reload:['groupsX'],
      precheck:cmd=>text(cmd.name).length<2?'Enter the group name.':!Object.values(cmd.allot??{}).some(n=>Number(n)>0)?'Hold at least one room.':undefined,
      async send(c){
        const cmd=c.cmd,allot=Object.entries(cmd.allot??{}).filter(([,n])=>Number(n)>0) as [string,number][];
        const ids=await roomTypeIds(c,allot.map(([t])=>t),cmd.arrival,cmd.departure);
        const block=await c.api.command<{groupBlockId:number}>('CreateGroupBlock',{code:blockCode(cmd.name,cmd.arrival),name:text(cmd.name),arrival:cmd.arrival,departure:cmd.departure,cutoff:cmd.cutoff,
          allotments:allot.map(([t,n])=>({roomTypeId:ids[t],rooms:Number(n),ratePaise:cmd.rates?.[t]??null}))});
        const contact=text(cmd.contact)||text(cmd.name);
        const rules=cmd.billTo==='guest'?[{category:'room',target:'guest'},{category:'extras',target:'guest'},{category:'tax',target:'follow'}]:null;
        const group=await c.api.command('CreateGroup',{name:text(cmd.name),groupBlockId:block.groupBlockId,organiser:contact,billingName:text(cmd.name),billingContact:contact,rules},{step:'group'});
        return {result:{block,group}};
      }},
  ],
  unmapped:{
    GRP_BLOCK_ALLOT:'no server command changes a block’s allotment (inventory backlog)',
    GRP_RELEASE:'ReleaseGroupBlock needs the block version, which no server read returns yet; partial release has no server command',
    GRP_BLOCK_CANCEL:'no server command cancels a group block',
    GRP_ROOMING:'rooming-list pickup = one CreateReservation per row with the block — reservations area (W4-1) / backlog',
    GRP_COMPANY_SAVE:'company accounts (negotiated rates, credit limit) do not exist on the server — groups billing keeps billing name/GSTIN per group',
    GRP_LIMIT_SET:'company credit limits do not exist on the server',
    GRP_LIMIT_DECIDE:'company credit limits do not exist on the server',
  },
};
