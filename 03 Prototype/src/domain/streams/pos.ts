// Module 23 · Restaurant POS & room service — pure helpers, selectors and the state slice type. Exported names MUST start with 'pos' / 'POS' (shared mock namespace).
// Money = integer paise. Tax = round(net × taxBps / 10000) on the bill net (matches one folio service line). See 02 Planning/modules/HCC_Module_23_POS_Spec.md.
import type {State} from '../types';
import {taxRateFor} from '../selectors';

export type POSOutletKind='table'|'room';
export type POSOutlet={id:string;name:string;kind:POSOutletKind;tables?:string[]};
export type POSMenuItem={id:string;name:string;category:string;price:number;veg:boolean;outlets:string[];available:boolean};
export type POSKotStatus='Preparing'|'Ready'|'Served';
export type POSKot={id:string;no:number;at:string;by:string;status:POSKotStatus;readyAt?:string;servedAt?:string};
export type POSLine={id:string;item:string;name:string;price:number;qty:number;
  /** KOT ID once sent to the kitchen; unsent lines have none. */ kot?:string;
  voided?:boolean;voidReason?:string;voidBy?:string;voidApproval?:string;/** Business date of the void. */ voidDate?:string};
export type POSSettlement={mode:'outlet'|'folio';billNo:string;method?:string;reference?:string;net:number;taxBps:number;tax:number;total:number;
  date:string;at:string;by:string;receiptId?:string;booking?:string;folioLineId?:string};
export type POSOrderStatus='Open'|'Settled'|'Void';
export type POSOrder={id:string;outlet:string;table?:string;room?:string;booking?:string;guest?:string;covers:number;
  /** Business date the check was opened. */ date:string;openedAt:string;openedBy:string;status:POSOrderStatus;version:number;
  lines:POSLine[];kots:POSKot[];settlement?:POSSettlement;voidReason?:string;voidedBy?:string;voidedAt?:string;
  /** Pending void approval ID — the check is locked until it is decided. */ pendingVoid?:string};
export type POSReceipt={id:string;order:string;outlet:string;method:string;amount:number;net:number;tax:number;taxBps:number;reference:string;date:string;actor:string;kind:'sale'|'void'};
/** State slice for this module, stored at s.posX (optional; absent in old saves → treat as empty). */
export type POSState={outlets:POSOutlet[];menu:POSMenuItem[];orders:POSOrder[];receipts:POSReceipt[];kotSeq:Record<string,number>;billSeq:number};
export const posEmpty=():POSState=>({outlets:[],menu:[],orders:[],receipts:[],kotSeq:{},billSeq:0});
export const posSlice=(s:State):POSState=>s.posX??posEmpty();

/** Void value above which a different user with `approve` must decide (₹500). */
export const POS_VOID_LIMIT=50000;
export const POS_TENDERS=['Cash','UPI','Card'] as const;
export const POS_KOT_FLOW:POSKotStatus[]=['Preparing','Ready','Served'];
/** Approval TTL (simulated minutes) for void requests. */
export const POS_APPROVAL_TTL_MINUTES=24*60;

export const posLineValue=(l:POSLine)=>l.price*l.qty;
export const posActiveLines=(o:POSOrder)=>o.lines.filter(l=>!l.voided);
export const posUnsent=(o:POSOrder)=>o.lines.filter(l=>!l.voided&&!l.kot);
/** Bill totals. Settled checks use their snapshot; open checks preview with today's synthetic rate. */
export function posOrderTotals(s:State,o:POSOrder){
  if(o.settlement){const t=o.settlement;return {net:t.net,taxBps:t.taxBps,tax:t.tax,total:t.total,items:posActiveLines(o).reduce((a,l)=>a+l.qty,0)};}
  const net=posActiveLines(o).reduce((a,l)=>a+posLineValue(l),0),taxBps=taxRateFor(s,s.date),tax=Math.round(net*taxBps/10000);
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
export function posMenuFor(s:State,outlet:string){return posSlice(s).menu.filter(m=>m.outlets.includes(outlet));}
export function posCategories(s:State,outlet:string){return [...new Set(posMenuFor(s,outlet).map(m=>m.category))];}
/** KOTs of all checks, newest first, with their check and active lines. */
export function posKitchen(s:State){
  return posSlice(s).orders.filter(o=>o.status!=='Void'||o.kots.length).flatMap(o=>o.kots.map(k=>({order:o,kot:k,lines:o.lines.filter(l=>l.kot===k.id)})))
    .filter(x=>x.lines.some(l=>!l.voided)&&x.order.status!=='Void').sort((a,b)=>b.kot.at.localeCompare(a.kot.at)||b.kot.no-a.kot.no);
}
export const posKotLabel=(s:State,o:POSOrder,k:POSKot)=>`${(posOutlet(s,o.outlet)?.name??o.outlet).toUpperCase().slice(0,2)}-KOT ${k.no}`;
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
  const byMode:Record<string,number>={Cash:0,UPI:0,Card:0,'Room folio':0};
  for(const o of settled){const m=o.settlement!.mode==='folio'?'Room folio':o.settlement!.method!;byMode[m]=(byMode[m]||0)+o.settlement!.total;}
  // A voided settled bill (same business date only) leaves the settled list; its sale and reversal receipts net to zero in outlet cash.
  const voided=p.orders.flatMap(o=>o.lines.filter(l=>l.voided&&l.voidDate===date));
  const open=p.orders.filter(o=>o.status==='Open');
  const qty:Record<string,{name:string;qty:number;value:number}>={};
  for(const o of settled)for(const l of posActiveLines(o)){const q=qty[l.item]??={name:l.name,qty:0,value:0};q.qty+=l.qty;q.value+=posLineValue(l);}
  return {date,checks:settled.length,covers:settled.reduce((a,o)=>a+o.covers,0),net:sum(settled,'net'),tax:sum(settled,'tax'),gross:sum(settled,'total'),
    byMode,outletCash:p.receipts.filter(r=>r.date===date&&r.method==='Cash').reduce((a,r)=>a+r.amount,0),
    voids:{count:voided.length,value:voided.reduce((a,l)=>a+posLineValue(l),0)},
    open:{count:open.length,value:open.reduce((a,o)=>a+posOrderTotals(s,o).total,0)},
    byOutlet:p.outlets.map(x=>{const os=settled.filter(o=>o.outlet===x.id);return {id:x.id,name:x.name,checks:os.length,net:sum(os,'net'),tax:sum(os,'tax'),gross:sum(os,'total'),open:open.filter(o=>o.outlet===x.id).length};}),
    top:Object.values(qty).sort((a,b)=>b.qty-a.qty||b.value-a.value).slice(0,5),
    pendingVoids:s.approvals.filter(a=>a.kind==='pos-void'&&a.status==='Pending').length};
}
