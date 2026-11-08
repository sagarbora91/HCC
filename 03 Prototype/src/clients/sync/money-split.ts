// F8C-SYNC · split folio (DEC-152, SF-CONTRACT §2–§6) in the production sync layer (lane f8c-sync, 8 Nov 2026).
//
//   reads    (called from money.ts patchBookings) GET /folios/by-reservation/{id}/summary = one FolioSummaryDto per folio (live and removed) and,
//            when more than one folio is live, GET /folios/{folioId} for every live folio B–D (lines, receipts, approvals, version) and — on a
//            departed stay — GET /invoices/by-source/folio/{folioId}. A stay with one live folio keeps today's single-folio path exactly (I1).
//   letters  folio letter = the server's letter: seq 1..4 = A..D on both sides, a removed letter is never reused on either side
//            (SplitFolioRules.EnsureCanOpen / prototype SF_FOLIO_OPEN). Ids: IdDirectory kinds 'folio' and 'invoice' are keyed by the booking id
//            for folio A (as before F8) and by '<booking>#<letter>' for B–D; every folio keeps its own version (DEC-041 binding per folio).
//   commands SF_FOLIO_OPEN → OpenFolio · SF_FOLIO_REMOVE → RemoveFolio · SF_ROUTE_SET → SetFolioRouting. On folio A the prototype moves the
//            categories back to the guest (the folio that held them gives them up); the server refuses routing A to a category held by B–D
//            (SF_ROUTE_TAKEN), so the client sends SetFolioRouting to each holder without those categories (same end state, one step per holder).
//            The folio-aware money commands (PAYMENT, CHARGE, DISCOUNT, REFUND_REQUEST, CORRECTION_REQUEST, COMPANY_CREDIT_REQUEST, CREDIT_NOTE,
//            INVOICE_ISSUE in money.ts; CHECKOUT in frontdesk.ts; GUEST_CREDIT_REQUEST in lfmoney.ts) address a folio through sfFolioRef().
// Money is never computed here: per-folio figures are totals(sfView(b,f)) over the server's own lines, reconciled paise-exact per folio.
import type {Booking,Line,Receipt,Approval,State,Command} from '../../services/mock';
import {totals} from '../../services/mock';
import {sfIsSplit,sfFolios,sfView,SF_PRIMARY,SF_FOLIO_IDS,SF_ROUTE_CATEGORIES,
  type SFFolio,type SFFolioId,type SFPayer,type SFRouteCategory} from '../../domain/streams/folio-split';
import {folioApprovalExpired,folioKind} from '../../domain/streams/folio';
import {HccClientError} from '../errors';
import type {SyncArea,LoaderCtx,CommandCtx,SliceKey} from './types';
import type {IdDirectory} from './map';
import {serverId,protoId} from './map';
import {folioIntoBooking,approvalFromServer,type SrvFolio,type SrvInvoice} from './money/wire';

/** GET /folios/by-reservation/{id}/summary row (Hcc.Contracts.Billing FolioSummaryDto). */
export type SrvFolioSummary={folioId:number;seq:number;letter:string;payerKind:string;payerName:string|null;payerGstin:string|null;companyId:number|null;
  customerKey:string|null;routes:string[];netPaise:number;taxPaise:number;totalPaise:number;paidPaise:number;depositPaise:number;balancePaise:number;
  invoiceNo:string|null;removed:boolean};
/** OpenFolio / SetFolioRouting / RemoveFolio result (FolioCommandResult). */
export type SrvFolioCommandResult={reservationId:number;folioId:number;seq:number;letter:string;payerKind:string;routes:string[];removed:boolean};
/** CheckoutStay result fields the split checkout reads (CheckoutStayResult.InvoiceNos: null on a single-folio stay). */
export type SrvCheckoutResult={stayId:number;reservationId:number;invoiceNos?:string[]|null};

/** removedAt of a folio the server reports removed (the summary carries the flag, not the time; only truthiness is read). */
export const SF_REMOVED='removed';
const FOLIO_SLICES:SliceKey[]=['bookings','approvals'];
const text=(v:unknown)=>String(v??'').trim();
const GSTIN=/^[0-9]{2}[A-Z0-9]{13}$/;
const protocol=(m:string)=>new HccClientError('PROTOCOL',m);
const refuse=(code:'NOT_FOUND'|'VALIDATION'|'INVALID_TRANSITION',reason:string,m:string)=>new HccClientError(code,m,{reason});

/** IdDirectory key of a folio of booking `bookingId`: the booking id for A (unchanged since W4-3), '<booking>#<letter>' for B–D. */
export const sfKey=(bookingId:string,letter:string)=>letter===SF_PRIMARY?bookingId:`${bookingId}#${letter}`;
/** Server folio_seq of a letter (A = 1 … D = 4). */
export function sfSeq(letter:string):number{
  const i=(SF_FOLIO_IDS as readonly string[]).indexOf(letter);
  if(i<0)throw refuse('NOT_FOUND','SF_FOLIO_UNKNOWN',`This stay has no folio ${letter||'(none chosen)'}.`);
  return i+1;
}
/** The folio letter a command names (`cmd.folio`), validated like the prototype's sfCmdFolio / SF_FOLIO_UNKNOWN; absent → `fallback`. */
export function sfLetter(b:Booking,folio:unknown,fallback:string=SF_PRIMARY):string{
  if(folio===undefined||folio===null||folio==='')return fallback;
  const f=String(folio);
  const ok=sfIsSplit(b)?sfFolios(b).some(x=>x.id===f):f===SF_PRIMARY;
  if(!ok)throw refuse('NOT_FOUND','SF_FOLIO_UNKNOWN',`This stay has no open folio ${f}.`);
  return f;
}
/** Server id + loaded version of one folio of a booking (DEC-041: a request is bound to the version of the folio the user saw). */
export function sfFolioRef(c:{ids:IdDirectory},bookingId:string,letter:string=SF_PRIMARY){
  const key=sfKey(bookingId,letter);
  const folioId=c.ids.server('folio',key),version=c.ids.version('folio',key);
  if(folioId===undefined||!version)throw new HccClientError('INVALID_TRANSITION',letter===SF_PRIMARY
    ?'This booking has no folio on the server yet — it opens at check-in. Refresh and try again.'
    :`Folio ${letter} of this stay is not loaded from the server. Refresh and try again.`);
  return {folioId,version};
}

// ── read side ──

/** The prototype payer of a server folio row. Folio A is the guest (name = the booking's guest). Udhaar: ref = the LF B2 customer key. */
function payerOf(r:SrvFolioSummary,b:Booking):SFPayer{
  const gstin=r.payerGstin?.trim()||undefined;
  const kind=(['guest','company','udhaar','group'].includes(r.payerKind)?r.payerKind:'guest') as SFPayer['kind'];
  if(r.seq===1)return {kind:'guest',name:b.guest,...(gstin?{gstin}:{})};
  const p:SFPayer={kind,name:r.payerName?.trim()||(kind==='udhaar'?text(r.customerKey):'')};
  if(kind==='company'&&r.companyId!=null)p.ref=protoId(r.companyId);
  if(kind==='udhaar'&&r.customerKey)p.ref=r.customerKey;
  if(gstin)p.gstin=gstin;
  return p;
}
async function optional<T>(p:Promise<T>):Promise<T|null>{
  try{return await p;}catch(e){if(e instanceof HccClientError&&(e.wireCode==='FORBIDDEN'||e.wireCode==='NOT_FOUND'))return null;throw e;}
}

/**
 * A split stay from the server: every live folio's lines and receipts (each stamped with its folio letter), `Booking.folios` from the summary
 * (payer, effective routes, invoice number, removed), the credit notes of every folio invoice and the approvals of every folio (meta.folio).
 * Returns null when at most one folio is live: the caller keeps today's single-folio booking (I1). Each folio is reconciled on its own
 * (folioIntoBooking: the prototype's totals of that folio's mapped lines = the server's totals of that folio, to the paisa), so stay
 * totals = Σ folio totals (I2) without any figure computed here.
 */
export async function sfLoadSplit(c:LoaderCtx,b:Booking,primary:SrvFolio,primaryInvoice:SrvInvoice|null,rows:SrvFolioSummary[])
  :Promise<(Booking&{srvApprovals?:Approval[]})|null>{
  const sorted=[...rows].sort((x,y)=>x.seq-y.seq);
  const live=sorted.filter(r=>!r.removed);
  if(live.length<=1)return null;
  for(const r of sorted)if(SF_FOLIO_IDS[r.seq-1]!==r.letter)throw protocol(`Folio ${r.folioId}: seq ${r.seq} is not letter ${r.letter}.`);
  const a=sorted.find(r=>r.seq===1);
  if(!a||a.removed||a.folioId!==primary.folioId)throw protocol(`Reservation ${b.id}: the folio summary does not name folio ${primary.folioId} as folio A.`);
  if(!c.mayRead('GET /folios/:id'))return null;
  const invoices=b.status==='Departed'&&c.mayRead('GET /invoices/by-source/folio/:id');
  const parts=await Promise.all(live.map(async r=>{
    const view=r.seq===1?primary:await c.api.get<SrvFolio>(`/folios/${r.folioId}`);
    if(view.folioId!==r.folioId)throw protocol(`Folio ${r.folioId}: the server returned folio ${view.folioId}.`);
    const inv=r.seq===1?primaryInvoice:invoices?await optional(c.api.get<SrvInvoice>(`/invoices/by-source/folio/${r.folioId}`)):null;
    return {r,view,inv};
  }));
  const lines:Line[]=[],receipts:Receipt[]=[],creditNotes:NonNullable<Booking['creditNotes']>=[],approvals:Approval[]=[],folios:SFFolio[]=[];
  for(const r of sorted){
    const letter=r.letter as SFFolioId,key=sfKey(b.id,letter),p=parts.find(x=>x.r===r);
    let invoiceNo=r.invoiceNo??undefined;
    if(p){
      c.ids.learn('folio',key,r.folioId);c.ids.setVersion('folio',key,p.view.version);
      const one=folioIntoBooking(b,p.view,p.inv); // PROTOCOL when this folio's totals do not reconcile
      for(const l of one.lines)lines.push({...l,folio:letter});
      for(const x of one.receipts)receipts.push({...x,folio:letter});
      if(p.inv){c.ids.learn('invoice',key,p.inv.invoiceId);invoiceNo=p.inv.invoiceNo;}
      for(const n of one.creditNotes??[])creditNotes.push({...n,folio:letter});
      for(const ap of p.view.approvals??[]){
        c.ids.setVersion('approval',protoId(ap.approvalId),ap.version);
        const x=approvalFromServer(ap,b.id);x.meta={...(x.meta??{}),folio:letter};approvals.push(x);
      }
    }
    folios.push({id:letter,payer:payerOf(r,b),routes:r.removed?[]:r.routes.filter((x):x is SFRouteCategory=>SF_ROUTE_CATEGORIES.includes(x as SFRouteCategory)),
      openedAt:'',openedBy:'',...(invoiceNo?{invoiceNo}:{}),...(r.removed?{removedAt:SF_REMOVED}:{})});
  }
  const next:Booking&{srvApprovals?:Approval[]}={...b,lines,receipts,folios};
  // SF-CONTRACT §2.1: Booking.invoiceNo = the first folio invoice (letter order = the order of allocation at checkout).
  const first=folios.find(f=>!f.removedAt&&f.invoiceNo)?.invoiceNo;
  if(first)next.invoiceNo=first;else delete next.invoiceNo;
  if(creditNotes.length)next.creditNotes=creditNotes;else delete next.creditNotes;
  next.srvApprovals=approvals;
  return next;
}
/** Every issued folio invoice of a booking for the credit-note request mapping: server invoice id → booking + folio letter. */
export function sfInvoiceIds(ids:IdDirectory,b:Booking):{invoiceId:number;folio?:string}[]{
  if(!sfIsSplit(b)){const i=ids.server('invoice',b.id);return i===undefined?[]:[{invoiceId:i}];}
  return sfFolios(b).flatMap(f=>{const i=ids.server('invoice',sfKey(b.id,f.id));return i===undefined?[]:[{invoiceId:i,folio:f.id}];});
}

// ── command helpers used by money.ts / frontdesk.ts / lfmoney.ts ──

/** CheckoutStay.creditApprovalIds of a split stay: per live folio with a balance, the approved company credit (company folio) or guest credit
 *  (folio A / an Udhaar folio) for exactly that folio's balance, requested on that folio — the approvals the prototype's CHECKOUT applies. */
export function sfCheckoutCreditApprovals(s:State,b:Booking):number[]{
  const out:number[]=[];
  for(const f of sfFolios(b)){
    const bal=totals(sfView(b,f.id)).balance;if(bal<=0)continue;
    const kind=f.payer.kind==='company'?'company-credit':'guest-credit';
    const a=s.approvals.find(x=>x.booking===b.id&&folioKind(x)===kind&&x.status==='Approved'&&!folioApprovalExpired(s,x)&&x.amount===bal
      &&String((x.meta as Record<string,unknown>|undefined)?.folio??SF_PRIMARY)===f.id);
    if(a)out.push(serverId(a.id,'Approval'));
  }
  return out;
}
/** CHECKOUT of a split stay issued no invoice numbers although a folio has a non-zero total: the hotel GSTIN is not set (SFQ-19 default). */
export function sfInvoiceWarning(b:Booking,invoiceNos:readonly string[]|null|undefined):string|undefined{
  if(invoiceNos&&invoiceNos.length)return undefined;
  if(!sfFolios(b).some(f=>totals(sfView(b,f.id)).total!==0))return undefined;
  return 'Checkout completed — the guest is checked out and the room is released. The folio tax invoices were not numbered (the hotel GSTIN is not set on the server). '
    +'Once it is set, open the departed stay and choose “Issue invoice”: each folio with charges is invoiced in letter order.';
}
/** Folios of a departed split stay that still need a tax invoice: non-zero total, no number yet, letter order (SFQ-8, SFQ-19). */
export const sfFoliosToInvoice=(b:Booking)=>sfFolios(b).filter(f=>!f.invoiceNo&&totals(sfView(b,f.id)).total!==0);
/** INVOICE_ISSUE on a split stay: IssueInvoice per folio in letter order (step invoice-<letter>), buyer = the folio payer — B2B with its GSTIN,
 *  else B2C without a buyer (as SplitCheckoutPort.IssueFolioInvoicesAsync). The server returns an existing folio invoice on a repeat. */
export async function sfIssueInvoices(c:CommandCtx,b:Booking):Promise<unknown[]>{
  const out:unknown[]=[];
  for(const f of sfFoliosToInvoice(b)){
    const {folioId}=sfFolioRef(c,b.id,f.id),gstin=f.payer.gstin?.trim();
    out.push(await c.api.command('IssueInvoice',gstin?{sourceKind:'folio',sourceId:folioId,supplyType:'B2B',buyerName:f.payer.name,buyerGstin:gstin}:{sourceKind:'folio',sourceId:folioId},
      {step:'invoice-'+f.id}));
  }
  return out;
}

// ── the three split-folio commands ──

const bookingOf=(s:State,id:unknown)=>s.bookings.find(x=>x.id===String(id??''));
function routesOf(raw:unknown):SFRouteCategory[]|string{
  const list=raw===undefined||raw===null?[]:raw;
  if(!Array.isArray(list))return 'Routes must be a list of categories (room, extras).';
  const out:SFRouteCategory[]=[];
  for(const r of list as unknown[]){if(!SF_ROUTE_CATEGORIES.includes(r as SFRouteCategory))return 'Route a category: room or extras.';if(!out.includes(r as SFRouteCategory))out.push(r as SFRouteCategory);}
  return out;
}
/** Udhaar payer of SF_FOLIO_OPEN: payer.ref is the credit guest's id (Split bill form) or already a customer key. */
function udhaarGuest(s:State,ref:string){return (s.guests??[]).find(g=>g.creditAccount&&(g.id===ref||g.creditAccount.customerKey===ref));}

function openPrecheck(cmd:Command,s:State):string|undefined{
  const b=bookingOf(s,cmd.id);if(!b)return undefined;
  if(b.status!=='Confirmed'&&b.status!=='In house')return 'A folio can be opened only on a confirmed or in-house stay.';
  const p=(cmd.payer??{}) as Partial<SFPayer>;
  if(p.kind!=='guest'&&p.kind!=='company'&&p.kind!=='udhaar')return 'A split folio is paid by a guest, a company account or an Udhaar credit guest.';
  const routes=routesOf(cmd.routes);if(typeof routes==='string')return routes;
  if(text(cmd.reason).length>200)return 'Keep the reason to 200 characters.';
  if(p.kind==='company'&&!text(p.ref))return 'Choose the company account that pays this folio.';
  if(p.kind==='udhaar'){const g=udhaarGuest(s,text(p.ref));if(!text(p.ref))return 'Choose a guest with a credit account (Customers › Credit account) to pay this folio on Udhaar.';
    if(g&&!g.creditAccount!.active)return `${g.name}'s credit account is inactive.`;}
  if(p.kind==='guest'){const n=text(p.name);if(n.length<2||n.length>120)return 'Enter the name of the person who pays this folio.';
    const g=text(p.gstin).toUpperCase();if(g&&!GSTIN.test(g))return 'Enter a 15-character GSTIN or leave it blank.';}
  return undefined;
}
function routePrecheck(cmd:Command,s:State):string|undefined{
  const b=bookingOf(s,cmd.id);
  const routes=routesOf(cmd.routes);if(typeof routes==='string')return routes;
  const r=text(cmd.reason);if(r.length<3||r.length>200)return 'Give the reason for changing the routing (3–200 characters).';
  if(b&&(!sfIsSplit(b)||!sfFolios(b).some(f=>f.id===text(cmd.folio))))return `This stay has no open folio ${text(cmd.folio)||'(none chosen)'}.`;
  return undefined;
}

export const moneySplit:SyncArea={
  id:'money-split',
  loaders:[], // the folios load inside money.ts patchBookings (one patch of the bookings slice)
  commands:[
    {type:'SF_FOLIO_OPEN',reload:FOLIO_SLICES,precheck:openPrecheck,
      async send(c){
        const b=bookingOf(c.state,c.cmd.id);if(!b)throw new HccClientError('NOT_FOUND','Booking not found.');
        const p=(c.cmd.payer??{}) as Partial<SFPayer>,kind=p.kind as string,routes=routesOf(c.cmd.routes) as SFRouteCategory[];
        const body:Record<string,unknown>={reservationId:serverId(b.id,'Booking'),payerKind:kind,companyId:null,customerKey:null,payerName:null,payerGstin:null,routes,
          reason:text(c.cmd.reason)||null};
        if(kind==='company')body.companyId=serverId(text(p.ref),'Company account');
        else if(kind==='udhaar'){const g=udhaarGuest(c.state,text(p.ref));body.customerKey=g?.creditAccount?.customerKey??text(p.ref);body.payerName=g?.name??null;}
        else{body.payerName=text(p.name);const g=text(p.gstin).toUpperCase();if(g)body.payerGstin=g;}
        return {result:await c.api.command<SrvFolioCommandResult>('OpenFolio',body)};
      }},
    {type:'SF_ROUTE_SET',reload:FOLIO_SLICES,precheck:routePrecheck,
      async send(c){
        const b=bookingOf(c.state,c.cmd.id);if(!b)throw new HccClientError('NOT_FOUND','Booking not found.');
        const letter=sfLetter(b,c.cmd.folio,''),routes=routesOf(c.cmd.routes) as SFRouteCategory[],reason=text(c.cmd.reason),reservationId=serverId(b.id,'Booking');
        if(letter!==SF_PRIMARY)return {result:await c.api.command<SrvFolioCommandResult>('SetFolioRouting',{reservationId,folioSeq:sfSeq(letter),routes,reason})};
        // Folio A: each folio B–D that holds one of the categories keeps only the others (prototype SF_ROUTE_SET on A); A takes what is left.
        const out:SrvFolioCommandResult[]=[];
        for(const f of sfFolios(b))if(f.id!==SF_PRIMARY&&f.routes.some(r=>routes.includes(r)))
          out.push(await c.api.command<SrvFolioCommandResult>('SetFolioRouting',{reservationId,folioSeq:sfSeq(f.id),routes:f.routes.filter(r=>!routes.includes(r)),reason},{step:'route-'+f.id}));
        return {result:out};
      }},
    {type:'SF_FOLIO_REMOVE',reload:FOLIO_SLICES,
      precheck:cmd=>text(cmd.folio)===SF_PRIMARY?'Folio A (the guest\'s) cannot be removed.':undefined,
      async send(c){
        const b=bookingOf(c.state,c.cmd.id);if(!b)throw new HccClientError('NOT_FOUND','Booking not found.');
        const letter=sfLetter(b,c.cmd.folio,'');
        return {result:await c.api.command<SrvFolioCommandResult>('RemoveFolio',{reservationId:serverId(b.id,'Booking'),folioSeq:sfSeq(letter)})};
      }},
  ],
};
