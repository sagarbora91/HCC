// F8 SF · split folio (02 Planning/agents/SF-CONTRACT.md, DEC-152). Setup commit: types and the pure view helpers that every F8 lane may call.
// Exported names start with 'sf' / 'SF' (shared mock namespace). Lane sf-domain owns this file after the setup commit: it may ADD helpers and
// fill the commands, but never changes these signatures or the single-folio behaviour (a stay without `folios` is exactly today's stay).
// Money = integer paise; nothing here posts, rounds or changes money: a view only filters the stay's own lines and receipts.
import type {Booking,Line,Receipt} from '../types';

/** Who pays a folio (SF-CONTRACT §2). 'group' is declared for the server mirror only: F8 refuses a split on a group member (§2.4, SFQ-10). */
export type SFPayerKind='guest'|'company'|'group'|'udhaar';
/** Routing categories (SF-CONTRACT §3, the groups module's own vocabulary): room = room nights, meal-plan lines and discounts; extras = the rest. */
export type SFRouteCategory='room'|'extras';
export const SF_ROUTE_CATEGORIES:readonly SFRouteCategory[]=['room','extras'];
/** Folio letters in order; A is the primary (guest) folio and always exists. Maximum four folios per stay (SFQ-1 default). */
export const SF_FOLIO_IDS=['A','B','C','D'] as const;
export type SFFolioId=typeof SF_FOLIO_IDS[number];
export const SF_PRIMARY:SFFolioId='A';
/** The payer of a folio. company: ref = company account id (Groups & corporate › Companies), name and GSTIN copied from it at open.
 *  udhaar: ref = the guest key of an LF B2 credit guest (guest-credit account). guest: optional GSTIN for a guest who is a registered business. */
export type SFPayer={kind:SFPayerKind;name:string;ref?:string;gstin?:string};
/** One folio of a split stay (Booking.folios, SCHEMA 16). Lines and receipts name their folio in Line.folio / Receipt.folio; absent = 'A'. */
export type SFFolio={id:SFFolioId;payer:SFPayer;
  /** Categories routed to this folio. Exclusive across the stay's open folios; folio A takes every category no other folio holds. */
  routes:SFRouteCategory[];
  /** Simulated clock and user of SF_FOLIO_OPEN. */ openedAt:string;openedBy:string;
  /** This folio's tax invoice number, allocated at checkout in the hotel's one INV series (SF-CONTRACT §5). */ invoiceNo?:string;
  /** Simulated clock of SF_FOLIO_REMOVE (only an empty folio is removed; the letter is not reused). */ removedAt?:string};

/** Folios that are not removed, in letter order. */
const live=(b:Booking)=>(b.folios??[]).filter(f=>!f.removedAt).sort((x,y)=>x.id<y.id?-1:x.id>y.id?1:0);
/** True when the stay has more than one live folio. A stay with only folio A (or no `folios`) is a single-folio stay. */
export const sfIsSplit=(b:Booking):boolean=>live(b).length>1;
/** The stay's folios. Without `folios`: one implicit folio A (payer = the guest, every category routed to it). */
export function sfFolios(b:Booking):SFFolio[]{
  const l=live(b);if(l.length)return l;
  return [{id:SF_PRIMARY,payer:{kind:'guest',name:b.guest,...(b.company?.gstin?{gstin:b.company.gstin}:{})},routes:[...SF_ROUTE_CATEGORIES],openedAt:'',openedBy:''}];
}
export const sfFolio=(b:Booking,id:string):SFFolio|undefined=>sfFolios(b).find(f=>f.id===id);
/** Category of a NEW line for routing (SF-CONTRACT §3): room nights, meal-plan lines and discounts → room; any other charge → extras.
 *  An adjustment (reversal, credit note, correction) is never routed: it follows its original (sfLineFolio) or the folio its command names. */
export function sfRouteCategory(l:Pick<Line,'kind'|'planCode'>):SFRouteCategory{return l.kind==='room'||l.kind==='discount'||!!l.planCode?'room':'extras';}
/** The live folio a new line of category `cat` posts to (the folio whose routes include it, else A). */
export function sfRouteTarget(b:Booking,cat:SFRouteCategory):SFFolioId{return live(b).find(f=>f.id!==SF_PRIMARY&&f.routes.includes(cat))?.id??SF_PRIMARY;}
/** The folio a posted line belongs to: its own `folio`; else, for a reversal, the folio of the line it reverses; else A. */
export function sfLineFolio(b:Booking,l:Line):string{
  if(l.folio)return l.folio;
  if(l.reversalOf){const o=b.lines.find(x=>x.id===l.reversalOf);if(o&&o!==l)return sfLineFolio(b,o);}
  return SF_PRIMARY;
}
/** The folio a receipt belongs to (absent = A). */
export const sfReceiptFolio=(_b:Booking,r:Receipt):string=>r.folio??SF_PRIMARY;
/**
 * One folio of a stay as a Booking-shaped view: only that folio's lines, receipts and credit notes, its own invoice number and its payer as
 * the invoice party. Every existing per-booking rule (totals, refundable, credit-note cap, company credit check, invoice document) runs on
 * the view unchanged. A single-folio stay returns the booking itself (identity), so nothing changes for today's stays.
 */
export function sfView(b:Booking,id:string):Booking{
  if(!sfIsSplit(b))return b;
  const f=sfFolio(b,id);
  const lines=b.lines.filter(l=>sfLineFolio(b,l)===id),receipts=b.receipts.filter(r=>sfReceiptFolio(b,r)===id);
  const creditNotes=(b.creditNotes??[]).filter(c=>(c.folio??SF_PRIMARY)===id);
  const v:Booking={...b,lines,receipts};
  if(creditNotes.length)v.creditNotes=creditNotes;else delete v.creditNotes;
  if(f?.invoiceNo)v.invoiceNo=f.invoiceNo;else delete v.invoiceNo;
  delete v.invoiceRevisions;
  const p=f?.payer;
  if(p?.kind==='company')v.company={name:p.name,...(p.gstin?{gstin:p.gstin}:{}),...(p.ref?{account:p.ref}:{})};
  else if(p?.gstin)v.company={name:p.name,gstin:p.gstin};
  else delete v.company;
  return v;
}
/** Every issued tax invoice of a stay, one row per folio that has a number (single-folio stay: its one invoice). The tax reports
 *  (GSTR-1, invoice register, GST by bill, documents issued, SAC summary) enumerate invoices through this (SF-CONTRACT §5.4). */
export function sfInvoiceDocs(b:Booking):{folio:string;no:string;view:Booking}[]{
  if(!sfIsSplit(b))return b.invoiceNo?[{folio:SF_PRIMARY,no:b.invoiceNo,view:b}]:[];
  return sfFolios(b).filter(f=>!!f.invoiceNo).map(f=>({folio:f.id,no:f.invoiceNo!,view:sfView(b,f.id)}));
}
