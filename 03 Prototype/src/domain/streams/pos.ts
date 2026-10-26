// Module 23 · Restaurant POS & room service — pure helpers, selectors and the state slice type. Exported names MUST start with 'pos' / 'POS' (shared mock namespace).
// Money = integer paise. Tax = round(net × taxBps / 10000) on the bill net (matches one folio service line). See 02 Planning/modules/HCC_Module_23_POS_Spec.md.
import type {State} from '../types';
import {gstLineTax,gstFy,gstSplit} from '../gst';
import {gstinProblem} from '../gstin';
import {mnyNames,mnyIsDrawer} from './money-master';
import {posCheckRate,posItemActive,posItemCategoryName,posItemCategoryCode,posCategoryRows,posKotStation} from './pos-master';

export type POSOutletKind='table'|'room';
/** MA-4 master fields are optional (fixture rows and old saves have none; see pos-master.ts for the defaults). */
export type POSOutlet={id:string;name:string;kind:POSOutletKind;
  /** Active table numbers in order (kept in step with tableRows by the tables master). */ tables?:string[];
  code?:string;sortOrder?:number;active?:boolean;retiredOn?:string;type?:import('./pos-master').POSOutletType;opens?:string;closes?:string;
  gstProfile?:import('./pos-master').POSGstProfile;kotStation?:string;
  /** Tables and counters master rows (MA-4); absent = derived from `tables`. */ tableRows?:POSTable[]};
export type POSTable={id:string;code:string;name:string;tableNo:string;seats?:number;section?:string;counter?:boolean;sortOrder:number;active:boolean;retiredOn?:string};
export type POSMenuItem={id:string;name:string;category:string;
  /** Price in force when last saved (legacy readers); the dated list `prices` wins when present (posItemPrice). */ price:number;veg:boolean;outlets:string[];
  /** false = sold out ("86"); with soldOutOn it lapses at the next business date (posSoldOut). */ available:boolean;
  code?:string;sortOrder?:number;active?:boolean;retiredOn?:string;shortCode?:string;categoryCode?:string;prices?:{amount:number;effectiveFrom:string}[];
  gstClass?:import('./pos-master').POSGstClass;station?:string;soldOutOn?:string};
export type POSKotStatus='Preparing'|'Ready'|'Served';
export type POSKot={id:string;no:number;at:string;by:string;status:POSKotStatus;readyAt?:string;servedAt?:string;/** Kitchen station (MA-4; absent = Kitchen). */ station?:string};
export type POSLine={id:string;item:string;name:string;price:number;qty:number;
  /** GST class snapshot (MA-4; absent = food). */ gst?:string;
  /** KOT ID once sent to the kitchen; unsent lines have none. */ kot?:string;
  voided?:boolean;voidReason?:string;voidBy?:string;voidApproval?:string;/** Business date of the void. */ voidDate?:string};
export type POSSettlement={mode:'outlet'|'folio';billNo:string;method?:string;reference?:string;net:number;taxBps:number;tax:number;total:number;
  date:string;at:string;by:string;receiptId?:string;booking?:string;folioLineId?:string;
  /** DEC-072: GST tax invoice of an outlet bill (RST/<FY>/<7 digits>); absent for a room-folio bill and for bills settled before the series. */
  invoiceNo?:string;
  /** B2C (walk-in) or B2B (buyer GSTIN given) — with the buyer as entered at settlement. */
  supply?:'B2C'|'B2B';buyer?:{name?:string;gstin?:string;address?:string};
  /** Intra-state CGST / SGST of the bill tax (stored with the bill, never recomputed). */
  cgst?:number;sgst?:number;
  /** Hotel GSTIN and SAC printed on the invoice (snapshot at issue). */
  hotelGstin?:string;sac?:string;
  /** Credit note that reversed the invoice when the bill was voided (CN series). */
  creditNoteNo?:string;creditNoteDate?:string;
  /** Production only: the server bill id (IssueInvoice for a bill settled before the hotel GSTIN was recorded). */
  billId?:string};
export type POSOrderStatus='Open'|'Settled'|'Void';
export type POSOrder={id:string;outlet:string;table?:string;room?:string;booking?:string;guest?:string;covers:number;
  /** Business date the check was opened. */ date:string;openedAt:string;openedBy:string;status:POSOrderStatus;version:number;
  lines:POSLine[];kots:POSKot[];settlement?:POSSettlement;voidReason?:string;voidedBy?:string;voidedAt?:string;
  /** Pending void approval ID — the check is locked until it is decided. */ pendingVoid?:string};
export type POSReceipt={id:string;order:string;outlet:string;method:string;amount:number;net:number;tax:number;taxBps:number;reference:string;date:string;actor:string;kind:'sale'|'void'};
/** State slice for this module, stored at s.posX (optional; absent in old saves → treat as empty). `rstSeq`: last RST invoice number per
 *  financial-year code ("2627"), gap-free (DEC-072; optional — absent in old saves = none issued yet). */
export type POSState={outlets:POSOutlet[];menu:POSMenuItem[];orders:POSOrder[];receipts:POSReceipt[];kotSeq:Record<string,number>;billSeq:number;rstSeq?:Record<string,number>};
export const posEmpty=():POSState=>({outlets:[],menu:[],orders:[],receipts:[],kotSeq:{},billSeq:0});
export const posSlice=(s:State):POSState=>s.posX??posEmpty();

/** Void value above which a different user with `approve` must decide (₹500). */
export const POS_VOID_LIMIT=50000;
/** LEGACY (MA-3): seed of the POS payment methods; read mnyNames(s,'pos') (money masters). */
export const POS_TENDERS=['Cash','UPI','Card'] as const;
export const POS_KOT_FLOW:POSKotStatus[]=['Preparing','Ready','Served'];
/** Approval TTL (simulated minutes) for void requests. */
export const POS_APPROVAL_TTL_MINUTES=24*60;

// ───────────── DEC-072 · restaurant tax invoices (RST series) — same rules as the server (InvoiceNumbering, PosInvoiceSource) ─────────────
/** Prefix of the restaurant tax-invoice series. Numbers are RST/<FY>/<7 digits>: exactly 16 characters, restarting each 1 April. */
export const POS_RST_PREFIX='RST';
/** SAC of restaurant service (CGST Rules r.46 needs the SAC on each line; illustrative — CA to confirm). */
export const POS_RST_SAC='996331';
/** B2C invoices of ₹50,000 or more need the buyer's name and address (r.46(e) as read — CA to confirm). */
export const POS_B2C_NAME_ADDRESS_LIMIT=5000000;
/** "2627" for any date in FY 2026-27 (1 April 2026 – 31 March 2027). */
export function posFyCode(date:string){const f=gstFy(date).from;const y=+f.slice(0,4);return String(y%100).padStart(2,'0')+String((y+1)%100).padStart(2,'0');}
/** RST/2627/0000001 — the sequence fills the 16 characters; the series is full at 9,999,999 (refused, never wrapped). */
export function posRstNo(fy:string,seq:number){
  const width=16-POS_RST_PREFIX.length-fy.length-2,digits=String(seq);
  if(!Number.isInteger(seq)||seq<1||digits.length>width)throw new RangeError(`The ${POS_RST_PREFIX} series for FY ${fy} is full (${width} digits).`);
  return `${POS_RST_PREFIX}/${fy}/${digits.padStart(width,'0')}`;
}
/** The buyer of an outlet bill as entered (trimmed, GSTIN upper-cased), or the reason it is refused — the server's wording. */
export function posBuyerOf(cmd:Record<string,unknown>,total:number):{buyer?:{name?:string;gstin?:string;address?:string};supply:'B2C'|'B2B'}|{error:string}{
  const gstin=String(cmd.buyerGstin??'').trim().toUpperCase(),name=String(cmd.buyerName??'').trim(),address=String(cmd.buyerAddress??'').trim();
  if(name.length>120)return {error:'Keep it to 120 characters.'};if(address.length>200)return {error:'Keep it to 200 characters.'};
  if(gstin){const bad=gstinProblem(gstin,'buyer');if(bad)return {error:bad};if(!name)return {error:"Enter the buyer's legal name for a B2B tax invoice."};}
  else if(total>=POS_B2C_NAME_ADDRESS_LIMIT&&(!name||!address))return {error:"An invoice of ₹50,000 or more to an unregistered guest needs the recipient's name and address."};
  const buyer={...(name?{name}:{}),...(gstin?{gstin}:{}),...(address?{address}:{})};
  return {supply:gstin?'B2B':'B2C',...(Object.keys(buyer).length?{buyer}:{})};
}
/** The CGST / SGST of a settled bill: as stored with it, else the split of its tax (bills settled before the split was stored). */
export const posBillSplit=(t:POSSettlement)=>t.cgst!==undefined&&t.sgst!==undefined?{cgst:t.cgst,sgst:t.sgst}:gstSplit(t.tax);

export const posLineValue=(l:POSLine)=>l.price*l.qty;
export const posActiveLines=(o:POSOrder)=>o.lines.filter(l=>!l.voided);
export const posUnsent=(o:POSOrder)=>o.lines.filter(l=>!l.voided&&!l.kot);
/** Bill totals. Settled checks use their snapshot; open checks preview with today's GST master rate. */
export function posOrderTotals(s:State,o:POSOrder){
  if(o.settlement){const t=o.settlement;return {net:t.net,taxBps:t.taxBps,tax:t.tax,total:t.total,items:posActiveLines(o).reduce((a,l)=>a+l.qty,0)};}
  const net=posActiveLines(o).reduce((a,l)=>a+posLineValue(l),0),taxBps=posCheckRate(s,o,s.date).bps,tax=gstLineTax(net,taxBps);
  return {net,taxBps,tax,total:net+tax,items:posActiveLines(o).reduce((a,l)=>a+l.qty,0)};
}
/** Kitchen status of a line (New = not yet sent). */
export function posLineStatus(o:POSOrder,l:POSLine):'New'|POSKotStatus|'Void'{
  if(l.voided)return 'Void';if(!l.kot)return 'New';return o.kots.find(k=>k.id===l.kot)?.status??'Preparing';
}
export const posOutlet=(s:State,id:string)=>posSlice(s).outlets.find(o=>o.id===id);
export const posOrder=(s:State,id?:string)=>posSlice(s).orders.find(o=>o.id===id);
/** The open check on a table / room of an outlet, if any. */
export function posOpenOrderAt(s:State,outlet:string,spot:string){
  return posSlice(s).orders.find(o=>o.outlet===outlet&&o.status==='Open'&&(o.table===spot||o.room===spot));
}
export const posOpenOrders=(s:State)=>posSlice(s).orders.filter(o=>o.status==='Open');
/** Where the check is: "Table T3" / "Room 201". */
export const posSpot=(o:POSOrder)=>o.table?'Table '+o.table:'Room '+o.room;
/** Rooms with an in-house occupant (room-service targets). */
export function posInHouse(s:State){return s.bookings.filter(b=>b.status==='In house').sort((a,b)=>a.room.localeCompare(b.room));}
/** Menu of an outlet: active items (sold-out ones included, shown as such), ordered by category then item sort order (MA-4 masters). */
export function posMenuFor(s:State,outlet:string){
  const menu=posSlice(s).menu,cats=posCategoryRows(s),cRank=new Map(cats.map((c,i)=>[c.code,i])),iRank=new Map(menu.map((m,i)=>[m.id,m.sortOrder??(i+1)*10]));
  const rank=(m:POSMenuItem)=>cRank.get(posItemCategoryCode(s,m))??cats.length;
  return menu.filter(m=>posItemActive(m)&&m.outlets.includes(outlet)).sort((a,b)=>rank(a)-rank(b)||iRank.get(a.id)!-iRank.get(b.id)!);
}
/** Category names of an outlet's menu in category master order. */
export function posCategories(s:State,outlet:string){return [...new Set(posMenuFor(s,outlet).map(m=>posItemCategoryName(s,m)))];}
/** KOTs of all checks, newest first, with their check and active lines. */
export function posKitchen(s:State){
  return posSlice(s).orders.filter(o=>o.status!=='Void'||o.kots.length).flatMap(o=>o.kots.map(k=>({order:o,kot:k,lines:o.lines.filter(l=>l.kot===k.id)})))
    .filter(x=>x.lines.some(l=>!l.voided)&&x.order.status!=='Void').sort((a,b)=>b.kot.at.localeCompare(a.kot.at)||b.kot.no-a.kot.no);
}
export const posKotLabel=(s:State,o:POSOrder,k:POSKot)=>`${(posOutlet(s,o.outlet)?.name??o.outlet).toUpperCase().slice(0,2)}-KOT ${k.no}${k.station?' · '+k.station:''}`;
/** Kitchen board filtered by station ('' = all); only stations that exist are offered (posStations). */
export const posKitchenAt=(s:State,station='')=>posKitchen(s).filter(x=>!station||posKotStation(x.kot)===station);
export const posNextKotStatus=(st:POSKotStatus):POSKotStatus|undefined=>POS_KOT_FLOW[POS_KOT_FLOW.indexOf(st)+1];
/** Pending void request for a check. */
export function posPendingVoid(s:State,o:POSOrder){return o.pendingVoid?s.approvals.find(a=>a.id===o.pendingVoid&&a.status==='Pending'):undefined;}

export type POSDaySummary={date:string;checks:number;covers:number;net:number;tax:number;gross:number;
  byMode:Record<string,number>;outletCash:number;voids:{count:number;value:number};open:{count:number;value:number};
  byOutlet:{id:string;name:string;checks:number;net:number;tax:number;gross:number;open:number}[];top:{name:string;qty:number;value:number}[];pendingVoids:number};
/** Outlet day summary for a business date (settled checks by settlement date; open checks as of now). */
export function posDaySummary(s:State,date=s.date):POSDaySummary{
  const p=posSlice(s),settled=p.orders.filter(o=>o.status==='Settled'&&o.settlement?.date===date);
  const sum=(xs:POSOrder[],k:'net'|'tax'|'total')=>xs.reduce((a,o)=>a+(o.settlement?.[k]??0),0);
  const byMode:Record<string,number>={...Object.fromEntries(mnyNames(s,'pos').map(m=>[m,0])),'Room folio':0}; // MA-3: POS methods (seed Cash, UPI, Card)
  for(const o of settled){const m=o.settlement!.mode==='folio'?'Room folio':o.settlement!.method!;byMode[m]=(byMode[m]||0)+o.settlement!.total;}
  // A voided settled bill (same business date only) leaves the settled list; its sale and reversal receipts net to zero in outlet cash.
  const voided=p.orders.flatMap(o=>o.lines.filter(l=>l.voided&&l.voidDate===date));
  const open=p.orders.filter(o=>o.status==='Open');
  const qty:Record<string,{name:string;qty:number;value:number}>={};
  for(const o of settled)for(const l of posActiveLines(o)){const q=qty[l.item]??={name:l.name,qty:0,value:0};q.qty+=l.qty;q.value+=posLineValue(l);}
  return {date,checks:settled.length,covers:settled.reduce((a,o)=>a+o.covers,0),net:sum(settled,'net'),tax:sum(settled,'tax'),gross:sum(settled,'total'),
    byMode,outletCash:p.receipts.filter(r=>r.date===date&&mnyIsDrawer(s,r.method)).reduce((a,r)=>a+r.amount,0),
    voids:{count:voided.length,value:voided.reduce((a,l)=>a+posLineValue(l),0)},
    open:{count:open.length,value:open.reduce((a,o)=>a+posOrderTotals(s,o).total,0)},
    byOutlet:p.outlets.map(x=>{const os=settled.filter(o=>o.outlet===x.id);return {id:x.id,name:x.name,checks:os.length,net:sum(os,'net'),tax:sum(os,'tax'),gross:sum(os,'total'),open:open.filter(o=>o.outlet===x.id).length};}),
    top:Object.values(qty).sort((a,b)=>b.qty-a.qty||b.value-a.value).slice(0,5),
    pendingVoids:s.approvals.filter(a=>a.kind==='pos-void'&&a.status==='Pending').length};
}
