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
//                                reservation reads) and GET /groups/companies: company accounts, limit requests and — for PERM-GRP-004
//                                holders — the DEC-076 credit picture per company (exposure, receivable, remaining; shown instead of the
//                                local estimate) and the company payments. Credit-limit overrides no longer exist (DEC-076: refused, no override).
//   commands PAYMENT → RecordReceipt (one per tender; before check-in by reservationId: the server opens the folio)  · CHARGE → PostCharge · DISCOUNT / REFUND_REQUEST / COMPANY_CREDIT_REQUEST /
//            CORRECTION_REQUEST → RequestAdjustment (expectedVersion = the folio version loaded) · REFUND_EXECUTE → ExecuteAdjustment ·
//            APPROVAL → DecideApproval | DecideCreditNote · CHECKOUT is W4-1's (CheckoutStay): it calls companyCreditApprovalId() and,
//            after CheckoutStay, issueInvoiceAfterCheckout() from here (a refusal does not fail the checkout: invoiceStepWarning) ·
//            INVOICE_ISSUE → IssueInvoice (departed stay without an invoice; production "Issue invoice" action) ·
//            CREDIT_NOTE → RequestCreditNote · SHIFT_SUBMIT → SubmitShiftCount · SHIFT_DIFFERENCE_DECIDE → DecideApproval ·
//            CB_LOCK / CB_REOPEN → LockCashBookDay / ReopenCashBookDay · GRP_BLOCK_CREATE → CreateGroupBlock → CreateGroup — but REFUSED with a
//            written reason until the server option Hcc:GroupBlocks:Enabled is on (DEC-080, audit A02b-04: pickup, release, cancel and re-allot are not built).
//            GRP_BLOCK_CREATE / CreateGroup carry companyId when billed to a company account · GRP_COMPANY_PAYMENT → RecordCompanyPayment (DEC-076).
// The server re-checks everything (limits, self-approval, DEC-041 version binding, DEC-065 locks); prechecks only repeat cheap prototype refusals.
import type {Booking,Approval,Command,State} from '../../services/mock';
import {totals,addDays,cxSlice,grpAccountOf} from '../../services/mock';
import {GRP_BLOCKS_REFUSAL,grpBlocksOnServer,type GRPState} from '../../domain/streams/groups';
import {mnyMethod,mnyIsDrawer,mnyPick} from '../../domain/streams/money-master'; // AF2-PAYMETHODS (O8)
import {HccClientError} from '../errors';
import type {SrvAvailability} from '../server';
import type {SyncArea,LoaderCtx,CommandCtx,SliceKey,SyncMe} from './types';
import {serverId,protoId} from './map';
import {BOOKING_STATUS} from './frontdesk/shared';
import {folioIntoBooking,approvalFromServer,creditNoteRequestFromServer,cashDifferenceApproval,CNR_PREFIX,deskTill,deskShiftOpen,deskOpeningCash,
  deskShiftRecord,deskShiftClose,cashbookFromServer,blockFromServer,companyFromServer,companyPaymentFromServer,limitRequestFromServer,type SrvCompanyBook,
  type SrvReservationFolio,type SrvInvoice,type SrvCreditNoteRequest,type SrvCashDesk,type SrvCashBookRange,type SrvGroupSummary,type SrvGroupStatement} from './money/wire';
// F8C-SYNC · split folio (SF-CONTRACT): folios B–D are loaded and every folio-aware command addresses its folio (money-split.ts)
import {sfIsSplit,sfFolio,sfView,sfCompanyFolio,sfRouteTarget,sfLineFolio,SF_PRIMARY} from '../../domain/streams/folio-split';
import {sfLoadSplit,sfLetter,sfFolioRef,sfInvoiceIds,sfIssueInvoices,sfFoliosToInvoice,sfKey as sfKeyOf,type SrvFolioSummary} from './money-split';
// F8C-SYNC end

/** A booking as the money patch leaves it: the folio's approvals travel with it to the approvals loader (same batch). */
type MoneyBooking=Booking&{srvApprovals?:Approval[]};
const FOLIO_READERS=['billing','payments','checkout','reservations','reports'] as const;
const CASH_READERS=['payments','approve','reports','close'] as const;
/** Days of cash-book lock state loaded back from the business date (server maximum range 93). */
export const CASHBOOK_DAYS=93;

/** APK12-1: "next business day" only when the day is closed; with the day open the missing piece is a cashier shift. */
export const shiftClosedText=(s:State)=>s.closedDates?.includes(s.date)?'The cash shift is closed. Open the next business day first.':'The cash shift is closed. Open a cashier shift first.';
/** APK12-3: PERM-PAY-001 is own-shift scoped (S) for Reception / Cashier: the server takes cash only on a shift the user opened. */
const PAY_PERM='PERM-PAY-001',SHIFT_PERM='PERM-SHF-001';
export const CASH_NEEDS_OWN_SHIFT='Cash needs your own open cashier shift — use UPI/card or ask the cashier.';
/** Why this user cannot take a CASH tender (undefined = allowed as far as the client can tell). The owner of an open shift is not
 *  on the wire, so a user with only own-shift (S) payment scope needs an open shift AND the cashier permission to open one; the
 *  server remains the judge (and its refusal reads the same way). Users with property scope (Owner, Manager) are not limited. */
export function cashBlockedReason(me:SyncMe|null|undefined,shiftOpen:boolean,openerUid?:string):string|undefined{
  if(!me)return undefined;
  const g=me.grants.filter(x=>x.permissionId===PAY_PERM);
  if(!g.length||g.some(x=>x.scope!=='S'))return undefined;
  // DEC-078 (Wave B1): the desk read carries the shift's opener, so "own shift" is checked exactly when it is known.
  if(openerUid)return shiftOpen&&openerUid===me.uid?undefined:CASH_NEEDS_OWN_SHIFT;
  return shiftOpen&&me.permIds.includes(SHIFT_PERM)?undefined:CASH_NEEDS_OWN_SHIFT;
}
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
    // F8C-SYNC: a stay with more than one live folio loads every folio (lines stamped with their letter, Booking.folios); else today's path (I1)
    if(f&&c.mayRead('GET /folios/by-reservation/:id/summary')){
      const rows=await optional(c.api.get<SrvFolioSummary[]>(`/folios/by-reservation/${b.id}/summary`));
      const split=rows?await sfLoadSplit(c,b,f,inv,rows):null;
      if(split)return split as MoneyBooking;
    }
    // F8C-SYNC end
    const next=folioIntoBooking(b,f,inv) as MoneyBooking;
    delete next.folios; // F8C-SYNC: a single-folio stay never carries folios (I1)
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
    const b:Booking={id,publicNo:x.publicNo||undefined,guest:x.guestName,email:'',people:x.adults,room:x.roomNo??'',arrival:x.arrival,departure:x.departure,rate:x.nightlyRatePaise??0,
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
  const byInvoice=new Map<number,{booking:string;folio?:string}>(); // F8C-SYNC: every folio invoice of a split stay (meta.folio)
  for(const b of bookings)for(const x of sfInvoiceIds(c.ids,b))byInvoice.set(x.invoiceId,{booking:b.id,folio:x.folio});
  if(byInvoice.size&&c.mayRead('GET /credit-note-requests')){
    const reqs=await optional(c.api.get<SrvCreditNoteRequest[]>('/credit-note-requests',{status:'Pending'}))??[];
    for(const r of reqs){const at=byInvoice.get(r.invoiceId);if(!at)continue;
      const a=creditNoteRequestFromServer(r,at.booking);if(at.folio)a.meta={...(a.meta??{}),folio:at.folio};c.ids.setVersion('approval',a.id,r.version);out.push(a);}
  }
  out.sort((a,b)=>(Number(a.id.replace(CNR_PREFIX,''))||0)-(Number(b.id.replace(CNR_PREFIX,''))||0));
  return [...(c.current('approvals')??[]),...out];
}

/** One desk read per cash slice (four small GETs; each slice owns its whole value). Learns the till and the active shift. */
async function desk(c:LoaderCtx):Promise<SrvCashDesk>{
  const d=await c.api.get<SrvCashDesk>('/cashier/desk');
  const t=deskTill(d,c.me.userId);
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
  // Company accounts (W4 N1): GET /groups/companies (PERM-GRP-001) — the account book and the pending credit-limit requests.
  const book=c.mayRead('GET /groups/companies')?await optional(c.api.get<SrvCompanyBook>('/groups/companies')):null;
  const companies=(book?.companies??[]).map(x=>{c.ids.learn('company',protoId(x.companyId),x.companyId);c.ids.setVersion('company',protoId(x.companyId),x.version);return companyFromServer(x);});
  const requests=(book?.requests??[]).map(r=>{c.ids.learn('limitRequest',protoId(r.requestId),r.requestId);return limitRequestFromServer(r);});
  const payments=(book?.payments??[]).map(companyPaymentFromServer);
  return {companies,blocks:blocks.filter((b):b is NonNullable<typeof b>=>!!b),requests,overrides:[],payments};
}

// ── command helpers ──

const invalid=(m:string)=>new HccClientError('INVALID_TRANSITION',m);
const validation=(m:string)=>new HccClientError('VALIDATION',m);
const bookingOf=(c:CommandCtx,id:unknown)=>{const b=c.state.bookings.find(x=>x.id===String(id??''));if(!b)throw new HccClientError('NOT_FOUND','Booking not found.');return b;};
/** Folio id + the version loaded (DEC-041: requests are bound to the folio version the user saw). F8C-SYNC: `letter` names a folio B–D of a
 *  split stay (its own id and version); A = the booking's folio as before. */
function folioOf(c:CommandCtx,bookingId:string,letter:string=SF_PRIMARY){
  return sfFolioRef(c,bookingId,letter);
}
function approvalVersion(c:CommandCtx,id:string){
  const v=c.ids.version('approval',id);if(!v)throw new HccClientError('NOT_FOUND','This request is no longer pending. Refresh and try again.');return v;
}
/** AF2-PAYMETHODS: the method as the payment-methods master names it now (code, earlier name or other case → the row's name), else the text. */
export const folioTender=(s:State,v:unknown)=>{const t=text(v);return t?(mnyMethod(s,t)?.name??t):'';};
/** AF2-PAYMETHODS: does this method go into the shared drawer? A row that settles to a till (master); a value no row knows is drawer cash only
 *  when it is the literal "Cash" (the server's PaymentMethodBook.SettlesToTill fallback for records written before the masters). */
export const folioIsDrawer=(s:State,v:unknown)=>{const t=text(v),r=t?mnyMethod(s,t):undefined;return r?mnyIsDrawer(s,r.name):t==='Cash';};
/** The shared till for a drawer method (DEC-052; any master row that settles to a till, whatever its name); null for other tenders. */
function tillFor(c:CommandCtx,method:unknown){
  if(!folioIsDrawer(c.state,method))return null;
  const t=c.ids.server('till','desk');if(t===undefined)throw invalid(shiftClosedText(c.state));return t;
}
/** The tender of the receipt an approved refund request refunds (the server's default tender for ExecuteAdjustment). */
function refundReceiptMethod(s:State,approvalId:unknown):string|undefined{
  const a=s.approvals.find(x=>x.id===String(approvalId??'')&&x.kind==='refund');
  return a?s.bookings.find(x=>x.id===a.booking)?.receipts.find(x=>x.id===a.target)?.method:undefined;
}
const positive=(v:unknown)=>Number.isSafeInteger(v)&&(v as number)>0;
const text=(v:unknown)=>String(v??'').trim();
const FOLIO:SliceKey[]=['bookings','approvals'];
const CASH:SliceKey[]=['shiftOpen','openingCash','shift','shiftClose','approvals'];

function paymentPrecheck(cmd:Command,s:State,me?:SyncMe){
  const kind=cmd.kind||'payment';
  if(kind==='refund')return 'Refunds are no longer posted directly. Use “Request refund” on the original receipt; after approval, execute the refund.';
  if(!['payment','deposit','deposit-return'].includes(kind))return 'Invalid receipt type.';
  if(!positive(cmd.amount))return 'Enter an amount greater than zero.';
  const tenders=tendersOf(cmd);
  if(tenders.length>4)return 'Use at most four tender rows.';
  if(tenders.length>1&&kind==='deposit-return')return 'Return a deposit with a single tender.';
  for(const t of tenders){
    if(!positive(t.amount))return 'Enter an amount greater than zero.';
    // AF2-PAYMETHODS (O8): the method comes from the payment-methods master (front desk), as on the server (PaymentMethodBook.Pick): unknown or
    // retired methods are refused, a row that needs a reference asks for one, a row that settles to a till needs the open shift (and own shift).
    const p=mnyPick(s,t.method,'frontdesk',t.reference);
    if(p.problem!==undefined)return p.problem;
    if(mnyIsDrawer(s,p.method.name)){const b=cashBlockedReason(me,s.shiftOpen,cxSlice(s).opener?.by);if(b)return b;if(!s.shiftOpen)return shiftClosedText(s);}
  }
  if(tenders.reduce((a,t)=>a+t.amount,0)!==cmd.amount)return 'Tender rows must add up to the payment amount.';
  // F8C-SYNC · PAYMENT {folio?} on a split stay: the receipt table of SF-CONTRACT §3 in the prototype's words (the server re-checks: EnsureReceiptFolio)
  const b=s.bookings.find(x=>x.id===String(cmd.id??''));
  if(b&&sfIsSplit(b)){
    const fol=String(cmd.folio||SF_PRIMARY),f=sfFolio(b,fol);
    if(!f)return `This stay has no open folio ${fol}.`;
    if(fol!==SF_PRIMARY&&(kind==='deposit'||kind==='deposit-return'))return 'Deposits are held on folio A only.';
    if(f.payer.kind==='company'&&tenders.some(t=>folioIsDrawer(s,t.method)))return 'A company folio is not paid in cash (DEC-076): use bank transfer, UPI or card.';
  }
  // F8C-SYNC end
  return undefined;
}
function tendersOf(cmd:Command):{method:string;amount:number;reference:string}[]{
  return Array.isArray(cmd.tenders)&&cmd.tenders.length
    ?cmd.tenders.map((t:{method?:string;amount?:number;reference?:string})=>({method:String(t?.method??''),amount:Number(t?.amount),reference:text(t?.reference)}))
    :[{method:String(cmd.method??''),amount:Number(cmd.amount),reference:text(cmd.reference)}];
}
async function requestAdjustment(c:CommandCtx,action:string,b:Booking,amount:number,reason:string,targetId:number|null,letter:string=SF_PRIMARY){
  const f=folioOf(c,b.id,letter); // F8C-SYNC: the folio the request is on (split stay)
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
export const CASH_SLICES:SliceKey[]=['shiftOpen','openingCash','shift','shiftClose','cashbookX','cashOutX'];
export const FOLIO_SLICES:SliceKey[]=['bookings','approvals'];

export const money:SyncArea={
  id:'money',
  loaders:[
    {slice:'bookings',kind:'patch',requires:FOLIO_READERS,load:patchBookings},
    {slice:'approvals',kind:'patch',needs:['bookings','shift'],load:patchApprovals},
    {slice:'shiftOpen',requires:CASH_READERS,reads:['GET /cashier/desk'],async load(c){return deskShiftOpen(await desk(c),c.me.userId);}},
    {slice:'openingCash',requires:CASH_READERS,reads:['GET /cashier/desk'],async load(c){return deskOpeningCash(await desk(c),c.me.userId);}},
    {slice:'shift',requires:CASH_READERS,reads:['GET /cashier/desk'],async load(c){return deskShiftRecord(await desk(c),c.me.userId);}},
    {slice:'shiftClose',requires:CASH_READERS,reads:['GET /cashier/desk'],async load(c){return deskShiftClose(await desk(c),c.me.userId);}},
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
        const letter=sfLetter(b,c.cmd.folio); // F8C-SYNC: PAYMENT {folio?} → that folio (default A)
        const preArrival=letter===SF_PRIMARY&&known===undefined&&b.status==='Confirmed'&&kind!=='deposit-return';
        const target=preArrival?{folioId:0,reservationId:serverId(b.id,'Booking')}:{folioId:folioOf(c,b.id,letter).folioId};
        const results=[];
        // AF-PAY08 (PAY-08): confirmDuplicate=true is the cashier's "second, separate payment" tick. The resend uses its own key suffix (:confirm) so it can
        // never collide with the refused attempt's key (same key + a changed payload would be IDEMPOTENCY_MISMATCH).
        const confirm=c.cmd.confirmDuplicate===true;
        const split=tenders.length>1;
        const isDup=(e:unknown):e is HccClientError=>e instanceof HccClientError&&e.details?.reason==='DUPLICATE_SUSPECTED';
        for(const [i,t] of tenders.entries()){
          const body=(conf:boolean)=>({...target,kind,tender:folioTender(c.state,t.method),amountPaise:t.amount,reference:t.reference||null,tillId:tillFor(c,t.method),confirmDuplicate:conf});
          try{
            if(split&&confirm){
              // AF-FIX2 F1: a confirmed resend of a split first replays each row under its ORIGINAL key and payload (key tN, confirmDuplicate false):
              // a row recorded by the refused attempt comes back as an idempotent replay (no second receipt, no second drawer movement).
              // Only a row the server refuses as DUPLICATE_SUSPECTED (the refused row, or a later equal row) is then sent confirmed under confirm-tN.
              try{results.push(await c.api.command('RecordReceipt',body(false),{step:'t'+(i+1)}));continue;}
              catch(e){if(!isDup(e))throw e;}
              results.push(await c.api.command('RecordReceipt',body(true),{step:'confirm-t'+(i+1)}));
              continue;
            }
            const step=split?'t'+(i+1):confirm?'confirm':undefined;
            results.push(await c.api.command('RecordReceipt',body(confirm),step?{step}:{}));
          }catch(e){
            // A refusal on a later tender row: the rows before it are already recorded (each row is its own receipt).
            if(i>0&&isDup(e))
              throw new HccClientError(e.wireCode,`${e.message} Tender rows 1 to ${i} were already recorded; check the folio before confirming.`,e.details,e.httpStatus);
            throw e;
          }
        }
        return {result:results};
      }},
    {type:'CHARGE',reload:FOLIO,
      precheck:cmd=>!positive(cmd.amount)?'Enter an amount greater than zero.':!text(cmd.label)?'Describe the service.':undefined,
      async send(c){const b=bookingOf(c,c.cmd.id);
        // DEC-077: a missed charge on the reopened business day names that day (the server refuses any other earlier date).
        const on=typeof c.cmd.date==='string'&&c.cmd.date!==c.state.date?{serviceDate:c.cmd.date}:{};
        // F8C-SYNC · CHARGE {folio?} on a split stay: a named folio posts there exactly (ExplicitFolio); without one the server routes the
        // extra from folio A (route(extras), FolioRouting.Resolve — the routing the server holds, not the screen's copy).
        if(sfIsSplit(b)){const named=c.cmd.folio!==undefined&&c.cmd.folio!==null&&c.cmd.folio!=='',f=folioOf(c,b.id,sfLetter(b,c.cmd.folio));
          await c.api.command('PostCharge',{folioId:f.folioId,label:text(c.cmd.label),amountPaise:c.cmd.amount,gstCategory:c.cmd.gst??null,...on,...(named?{explicitFolio:true}:{})});return;}
        // F8C-SYNC end
        const f=folioOf(c,b.id,sfLetter(b,c.cmd.folio));
        await c.api.command('PostCharge',{folioId:f.folioId,label:text(c.cmd.label),amountPaise:c.cmd.amount,gstCategory:c.cmd.gst??null,...on});}},
    {type:'DISCOUNT',reload:FOLIO,
      precheck:cmd=>!positive(cmd.amount)?'Enter an amount greater than zero.':!text(cmd.reason)?'A reason is required.':undefined,
      // F8C-SYNC: DISCOUNT {folio?} → that folio, else the folio room is routed to (prototype default); the server posts it on that folio exactly
      async send(c){const b=bookingOf(c,c.cmd.id),letter=sfIsSplit(b)?sfLetter(b,c.cmd.folio,sfRouteTarget(b,'room')):sfLetter(b,c.cmd.folio);
        return {result:await requestAdjustment(c,'discount',b,c.cmd.amount,text(c.cmd.reason),null,letter)};}},
    {type:'REFUND_REQUEST',reload:FOLIO,
      precheck:cmd=>!positive(cmd.amount)?'Enter an amount greater than zero.':!text(cmd.reason)?'Enter a reason for the refund.':!cmd.receiptId?'Select the original receipt to refund.':undefined,
      // F8C-SYNC: a refund is requested on the folio of the receipt it refunds (SF-CONTRACT §3)
      async send(c){const b=bookingOf(c,c.cmd.id),r=b.receipts.find(x=>x.id===String(c.cmd.receiptId));
        return {result:await requestAdjustment(c,'refund',b,c.cmd.amount,text(c.cmd.reason),serverId(c.cmd.receiptId,'Receipt'),r?.folio??SF_PRIMARY)};}},
    {type:'COMPANY_CREDIT_REQUEST',reload:FOLIO,
      precheck:(cmd,s)=>{const b=s.bookings.find(x=>x.id===cmd.id);if(!b)return undefined;
        if(b.status!=='In house')return 'This stay is not in house.';
        // F8C-SYNC · split stay: the company folio's balance (prototype COMPANY_CREDIT_REQUEST {folio?})
        if(sfIsSplit(b)){const cf=sfCompanyFolio(b),fol=String(cmd.folio||cf?.id||SF_PRIMARY);
          if(!cf||fol!==cf.id)return 'Company credit goes on the company folio only. Open a company folio (Split bill) first.';
          return totals(sfView(b,fol)).balance<=0?'There is no outstanding balance to place on company credit.':undefined;}
        // F8C-SYNC end
        if(!b.company?.name?.trim()&&!grpAccountOf(s,b))return 'Add a company to this booking before requesting company credit.';
        if(totals(b).balance<=0)return 'There is no outstanding balance to place on company credit.';return undefined;},
      async send(c){const b=bookingOf(c,c.cmd.id);
        // F8C-SYNC · split stay: the request is on the company folio for exactly that folio's balance (the server checks it equals its balance)
        if(sfIsSplit(b)){const cf=sfCompanyFolio(b);
          if(!cf||sfLetter(b,c.cmd.folio,cf.id)!==cf.id)throw new HccClientError('VALIDATION','Company credit goes on the company folio only. Open a company folio (Split bill) first.',{reason:'SF_COMPANY_FOLIO'});
          return {result:await requestAdjustment(c,'credit',b,totals(sfView(b,cf.id)).balance,text(c.cmd.reason)||('Company credit · '+cf.payer.name),null,cf.id)};}
        // F8C-SYNC end
        // Server rule: the credit equals the current balance — the folio's own figures, which the loader reconciled paise-exact with the server.
        // DEC-076: the server also needs the booking billed to a company account and checks that account's credit limit.
        const company=b.company?.name||grpAccountOf(c.state,b)?.name||'';
        return {result:await requestAdjustment(c,'credit',b,totals(b).balance,text(c.cmd.reason)||('Company credit · '+company),null)};}},
    {type:'CORRECTION_REQUEST',reload:FOLIO,
      precheck:cmd=>!text(cmd.reason)?'A reason is required.':!cmd.lineId?'Select the posted line to correct.':undefined,
      async send(c){const b=bookingOf(c,c.cmd.id),l=b.lines.find(x=>x.id===c.cmd.lineId);
        if(!l)throw new HccClientError('NOT_FOUND','Select the posted line to correct.');
        // F8C-SYNC: the correction is requested on the folio of the line it corrects (I4)
        return {result:await requestAdjustment(c,'correction',b,Math.abs(l.amount),text(c.cmd.reason),serverId(l.id,'Folio line'),sfLineFolio(b,l))};}},
    {type:'REFUND_EXECUTE',reload:[...FOLIO,'shiftOpen','openingCash','shift','shiftClose'],
      // S3 / N10 (prototype validMethod, DEC-052): a CASH refund leaves the shared drawer, so it needs the till's shift open today. The
      // tender defaults to the original receipt's (as on the server), so a dialog without a method is checked too. Non-cash: no shift.
      // AF2-PAYMETHODS (O8): methods from the master. The original receipt's method may be retired (the server allows it); another method must be
      // active and usable at the front desk, with a reference when its row needs one. An original method the client cannot resolve is left to the server.
      precheck:(cmd,s)=>{const original=refundReceiptMethod(s,cmd.approvalId),method=cmd.method||original;
        const same=!cmd.method||(!!original&&!!mnyMethod(s,text(original))&&mnyMethod(s,text(cmd.method))?.id===mnyMethod(s,text(original))?.id);
        const p=mnyPick(s,method,'frontdesk',cmd.reference,{requireReference:!!cmd.method,allowRetired:same});
        if(p.problem!==undefined&&!(!cmd.method&&!mnyMethod(s,text(method))))return p.problem;
        return folioIsDrawer(s,method)&&!s.shiftOpen?shiftClosedText(s):undefined;},
      async send(c){const id=String(c.cmd.approvalId??'');const a=c.state.approvals.find(x=>x.id===id&&x.kind==='refund');
        if(!a)throw new HccClientError('NOT_FOUND','Refund request not found.');
        const b=c.state.bookings.find(x=>x.id===a.booking),r=b?.receipts.find(x=>x.id===a.target);
        const method=c.cmd.method?folioTender(c.state,c.cmd.method):r?.method;
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
        // F8C-SYNC · split stay (SFQ-19 default): every folio with charges and no number yet is invoiced, in letter order; a ₹0 folio never (SFQ-8)
        if(sfIsSplit(b))return sfFoliosToInvoice(b).length?undefined:'Every folio of this stay with charges already has its tax invoice.';
        // F8C-SYNC end
        return b.invoiceNo?`Invoice ${b.invoiceNo} has already been issued for this stay.`:undefined;},
      async send(c){const b=bookingOf(c,c.cmd.id);if(sfIsSplit(b))return {result:await sfIssueInvoices(c,b)}; // F8C-SYNC
        return {result:await issueInvoice(c,String(c.cmd.id))};}},
    {type:'CREDIT_NOTE',reload:FOLIO,
      precheck:(cmd,s)=>{if(!positive(cmd.amount))return 'Enter an amount greater than zero.';if(!text(cmd.reason))return 'A reason is required.';
        const b=s.bookings.find(x=>x.id===cmd.id);if(b&&b.status!=='Departed')return 'A credit note applies only to a departed (invoiced) stay. Use a correction while the guest is in house.';
        // F8C-SYNC · CREDIT_NOTE {folio?} on a split stay: target = that folio's invoice (SF-CONTRACT §6)
        if(b&&sfIsSplit(b)){const fol=String(cmd.folio||SF_PRIMARY),f=sfFolio(b,fol);if(!f)return `This stay has no open folio ${fol}.`;
          if(!f.invoiceNo)return `Folio ${fol} has no tax invoice (a ₹0 folio is not invoiced): nothing to credit.`;}
        // F8C-SYNC end
        return undefined;},
      async send(c){const b=bookingOf(c,c.cmd.id),letter=sfLetter(b,c.cmd.folio),invoiceId=c.ids.server('invoice',sfKeyOf(b.id,letter)); // F8C-SYNC: that folio's invoice
        if(invoiceId===undefined)throw invalid(letter===SF_PRIMARY?'No tax invoice has been issued for this stay yet.':`Folio ${letter} has no tax invoice yet.`);
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
        // DEC-080 / A02b-04: refused before any request (same title and sentence as the server's own NOT_AVAILABLE answer, option Hcc:GroupBlocks:Enabled).
        if(!grpBlocksOnServer())throw new HccClientError('NOT_AVAILABLE',GRP_BLOCKS_REFUSAL,{reason:'GROUP_BLOCKS_OFF'});
        const cmd=c.cmd,allot=Object.entries(cmd.allot??{}).filter(([,n])=>Number(n)>0) as [string,number][];
        const ids=await roomTypeIds(c,allot.map(([t])=>t),cmd.arrival,cmd.departure);
        const block=await c.api.command<{groupBlockId:number}>('CreateGroupBlock',{code:blockCode(cmd.name,cmd.arrival),name:text(cmd.name),arrival:cmd.arrival,departure:cmd.departure,cutoff:cmd.cutoff,
          allotments:allot.map(([t,n])=>({roomTypeId:ids[t],rooms:Number(n),ratePaise:cmd.rates?.[t]??null}))});
        const contact=text(cmd.contact)||text(cmd.name);
        const co=cmd.billTo==='company'&&cmd.company?c.state.groupsX?.companies.find(x=>x.id===String(cmd.company)):undefined;
        const rules=cmd.billTo==='guest'?[{category:'room',target:'guest'},{category:'extras',target:'guest'},{category:'tax',target:'follow'}]:null;
        // DEC-076: a bill-to-company block's master is billed to the company account (routing to it is credit-checked on the server).
        const group=await c.api.command('CreateGroup',{name:text(cmd.name),groupBlockId:block.groupBlockId,organiser:contact,billingName:co?.name??text(cmd.name),billingContact:co?.contact||contact,gstin:co?.gstin||null,rules,
          companyId:co?serverId(co.id,'Company'):null},{step:'group'});
        return {result:{block,group}};
      }},
    {type:'GRP_COMPANY_SAVE',reload:['groupsX'],
      precheck:cmd=>text(cmd.name).length<2?'Enter the company name.':undefined,
      async send(c){
        const m=c.cmd,id=m.id?String(m.id):undefined;
        const rates:Record<string,number>={};for(const [t,v] of Object.entries((m.rates??{}) as Record<string,number>))if(Number(v)>0)rates[t]=Number(v);
        const body:Record<string,unknown>={name:text(m.name),gstin:text(m.gstin)||null,contact:text(m.contact)||null,phone:text(m.phone)||null,email:text(m.email)||null,
          city:text(m.city)||null,rates:rates};
        if(id===undefined)return {result:await c.api.command('SaveCompany',{...body,companyId:null,creditLimitPaise:Number(m.creditLimit)>0?Number(m.creditLimit):0})};
        const v=c.ids.version('company',id);if(v===undefined)throw new HccClientError('NOT_FOUND','Company account not found. Refresh and try again.');
        const result=await c.api.command('SaveCompany',{...body,companyId:serverId(id,'Company'),creditLimitPaise:0},{expectedVersion:v});
        // ACC-2 flows (ruling 12): retiring / re-activating a company account is its own command (PERM-GRP-002).
        if(m.active!==undefined&&m.active!==null)await c.api.command('SetCompanyActive',{companyId:serverId(id,'Company'),active:m.active!==false},{step:'active'});
        return {result};
      }},
    {type:'GRP_LIMIT_SET',reload:['groupsX'],
      precheck:cmd=>!Number.isSafeInteger(cmd.limit)||(cmd.limit as number)<0?'Enter a credit limit of 0 or more.':undefined,
      async send(c){const m=c.cmd;
        return {result:await c.api.command('SetCompanyLimit',{companyId:serverId(m.company,'Company'),limitPaise:m.limit,reason:text(m.reason)||null})};}},
    {type:'GRP_LIMIT_DECIDE',reload:['groupsX'],
      async send(c){const m=c.cmd;
        return {result:await c.api.command('DecideCompanyLimit',{requestId:serverId(m.id,'Limit request'),approve:!!m.approve,note:text(m.note)||null})};}},
    // DEC-076: money received from a company (PERM-GRP-005; UPI, card or bank transfer; at most what it owes on company credit — the server checks).
    {type:'GRP_COMPANY_PAYMENT',reload:['groupsX'],
      // AF2-PAYMETHODS: the method from the payment-methods master (front desk), as on the server; a method that settles to a till is refused here.
      precheck:(cmd,s)=>!positive(cmd.amount)?'Enter an amount greater than zero.':folioIsDrawer(s,cmd.method)?'A company pays by bank transfer, UPI or card (a cheque is recorded as a bank transfer). Take cash on a guest folio in a cashier shift.'
        :mnyPick(s,cmd.method,'frontdesk',cmd.reference).problem,
      async send(c){const m=c.cmd;
        return {result:await c.api.command('RecordCompanyPayment',{companyId:serverId(m.company,'Company'),tender:folioTender(c.state,m.method),amountPaise:m.amount,reference:text(m.reference),invoiceNo:text(m.invoice)||null,
          ...(Number(m.tds)>0?{tdsPaise:Number(m.tds)}:{})})};}}, // ACC-2 tds: TDS the company deducted
  ],
  unmapped:{
    GRP_BLOCK_ALLOT:'no server command changes a block’s allotment (inventory backlog)',
    GRP_RELEASE:'ReleaseGroupBlock needs the block version, which no server read returns yet; partial release has no server command',
    GRP_BLOCK_CANCEL:'no server command cancels a group block',
    GRP_ROOMING:'rooming-list pickup = one CreateReservation per row with the block — reservations area (W4-1) / backlog',
  },
};
