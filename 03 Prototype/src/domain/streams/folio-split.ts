// F8 SF · split folio (02 Planning/agents/SF-CONTRACT.md, DEC-152). Setup commit: types and the pure view helpers that every F8 lane may call.
// Exported names start with 'sf' / 'SF' (shared mock namespace). Lane sf-domain owns this file after the setup commit: it may ADD helpers and
// fill the commands, but never changes these signatures or the single-folio behaviour (a stay without `folios` is exactly today's stay).
// Money = integer paise; nothing here posts, rounds or changes money: a view only filters the stay's own lines and receipts.
import type {Booking,Line,Receipt,State} from '../types';
import type {GRPState} from './groups'; // F8-SF-DOMAIN (type only: no runtime import cycle)

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

// F8-SF-DOMAIN · helpers added by lane sf-domain (SF-CONTRACT §3, §7, §8 R8). Pure; single-folio stays keep today's answers.
/** Refusal reasons (details.reason) of the SF commands and of the folio rules in existing commands (SF-CONTRACT §4). */
export const SF_REASONS=['SF_FOLIO_LIMIT','SF_ROUTE_TAKEN','SF_COMPANY_FOLIO','SF_COMPANY_MISMATCH','SF_GROUP_MEMBER','SF_PAYER_KIND','SF_FOLIO_NOT_EMPTY',
  'SF_FOLIO_INVOICED','SF_SPLIT_BILL_CORRECTION','SF_DEPOSIT_FOLIO','SF_FOLIO_UNKNOWN'] as const;
export type SFReason=typeof SF_REASONS[number];
/** Commands that post room nights: never refused by D7 (SF-CONTRACT §7.3 — it would stop the day close). */
export const SF_NIGHT_COMMANDS:readonly string[]=['POST_NIGHTS','NIGHT_POST','CLOSE_RUN','DAY_CLOSE','NEXT_DAY'];
/** The live company folio of a split stay (at most one), optionally only when it names company account `companyId`. */
export function sfCompanyFolio(b:Booking,companyId?:string):SFFolio|undefined{
  if(!sfIsSplit(b))return undefined;
  return live(b).find(f=>f.payer.kind==='company'&&(companyId===undefined||f.payer.ref===companyId));
}
/** True when a non-split stay is billed to company account `companyId` (its own link or a bill-to-company block it belongs to; as grpAccountOf). */
function billedTo(s:State,b:Booking,companyId:string):boolean{
  if(b.company?.account)return b.company.account===companyId;
  const blocks=((s.groupsX as Partial<GRPState>|undefined)?.blocks)??[];
  return blocks.some(x=>x.billTo==='company'&&x.company===companyId&&x.members.some(m=>m.booking===b.id));
}
/**
 * SF-CONTRACT §8 R8: the lines of a stay that a company report attributes to company `companyId`: on a split stay with a company folio,
 * only that folio's lines (and none when the folio names another company); on any other stay, every line when the stay is billed to the
 * company, else none.
 */
export function sfCompanyLines(s:State,b:Booking,companyId:string):Line[]{
  if(sfIsSplit(b)){
    const cf=sfCompanyFolio(b);
    if(cf)return cf.payer.ref===companyId?sfView(b,cf.id).lines:[];
  }
  return billedTo(s,b,companyId)?b.lines:[];
}
/** Folio a NEW line (one without `folio`) belongs to (SF-CONTRACT §3): reversal → its original's folio; closed-day correction → the folio of
 *  the line it corrects; any other adjustment → A; meal-plan line → the folio of that night's room line; late check-out half day → route(extras);
 *  else the route of its category. On a stay without folios: 'A'. */
export function sfNewLineFolio(b:Booking,l:Line):SFFolioId{
  if(l.folio)return l.folio as SFFolioId;
  if(!b.folios?.length)return SF_PRIMARY;
  if(l.reversalOf)return sfLineFolio(b,l) as SFFolioId;
  if(l.correctionOf){const o=b.lines.find(x=>x.id===l.correctionOf);return (o?sfLineFolio(b,o):SF_PRIMARY) as SFFolioId;}
  if(l.kind==='adjustment')return SF_PRIMARY;
  if(l.planCode){const n=b.lines.find(x=>x.kind==='room'&&x.date===l.date&&!x.reversalOf&&x.folio);if(n)return n.folio as SFFolioId;return sfRouteTarget(b,'room');}
  if(l.id===`late-half:${b.id}`)return sfRouteTarget(b,'extras');
  return sfRouteTarget(b,sfRouteCategory(l));
}
/** Folio a NEW receipt (one without `folio`) belongs to: a refund → the folio of the receipt it refunds; anything else → A. */
export function sfNewReceiptFolio(b:Booking,r:Receipt):SFFolioId{
  if(r.folio)return r.folio as SFFolioId;
  if(r.refundOf){const o=b.receipts.find(x=>x.id===r.refundOf);if(o&&o!==r)return sfReceiptFolio(b,o) as SFFolioId;}
  return SF_PRIMARY;
}
/** The folio letter a command names (`cmd.folio`), validated against the stay's live folios; undefined when absent. Throws nothing:
 *  callers refuse with SF_FOLIO_UNKNOWN when the result is `false`. */
export function sfCmdFolio(b:Booking,folio:unknown):SFFolioId|undefined|false{
  if(folio===undefined||folio===null||folio==='')return undefined;
  const f=String(folio);
  if(!sfIsSplit(b))return f===SF_PRIMARY?SF_PRIMARY:false;
  return live(b).some(x=>x.id===f)?f as SFFolioId:false;
}
// F8-SF-DOMAIN end
