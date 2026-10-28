// Module 23 · Restaurant POS & room service — command handlers. Command types MUST start with 'POS_'. Return value = audit/summary text.
// Rules: 02 Planning/modules/HCC_Module_23_POS_Spec.md §4. Money = integer paise; GST via the GST master (domain/gst.ts); simulated clock only.
// DEC-072: a settled OUTLET bill is a GST tax invoice in the RST series (RST/<FY>/<7 digits>, gap-free per FY), B2B with a buyer GSTIN; voiding
// it issues a credit note in the credit-note series (the folio's CN counter); a room-folio bill gets none (it is on the room invoice).
import type {Handler,Kit} from '../commands';
import type {Approval} from '../../../domain/types';
import {money,addMinutes} from '../../../domain/money';
import {gstLineTax,gstSplit} from '../../../domain/gst';
import {rbacIsRestaurant} from '../../../domain/streams/rbac';
import {cfgValueOn} from '../../../domain/streams/config';
import {fdmAllocate} from '../../../domain/streams/frontdesk-master';
import {posEmpty,posOrderTotals,posLineValue,posActiveLines,posUnsent,posSpot,posNextKotStatus,posBuyerOf,
  POS_VOID_LIMIT,POS_APPROVAL_TTL_MINUTES,POS_RST_SAC} from '../../../domain/streams/pos';
import {mnyMethod,mnyMethodFor,mnyIsDrawer,mnyNames} from '../../../domain/streams/money-master';
import {mstFail} from '../../../domain/streams/masters';
/** "Cash, UPI or Card" — the active POS methods (money masters, MA-3). */
const orList=(xs:string[])=>xs.length>1?xs.slice(0,-1).join(', ')+' or '+xs[xs.length-1]:xs[0]??'a payment method';
import type {POSOrder,POSState,POSKotStatus,POSKot,POSSettlement} from '../../../domain/streams/pos';
// MA-4 POS masters (outlets, tables, categories, items): registering them here makes them part of every bundle that has the POS handlers.
import {posOutletActive,posTablesOf,posItemBlock,posItemPrice,posItemClass,posCheckClass,posCheckRate,posStationOf,posSoldOut,POS_GST_CLASSES,POS_DEFAULT_STATION} from '../../../domain/streams/pos-master';

const slice=(k:Kit):POSState=>{k.s.posX??=posEmpty();return k.s.posX;};
const order=(k:Kit):POSOrder=>{const o=slice(k).orders.find(o=>o.id===k.cmd.id);k.assert(o,'Check not found.','NOT_FOUND');return o!;};
/** An open, unlocked check. */
const editable=(k:Kit,o:POSOrder)=>{
  k.assert(o.status==='Open','This check is already '+o.status.toLowerCase()+'.','INVALID_TRANSITION');
  k.assert(!o.pendingVoid,'A void request is pending for this check. Ask a manager to decide it first.','INVALID_TRANSITION');
};
const reasonOf=(k:Kit)=>{const r=String(k.cmd.reason??'').trim();k.assert(r.length>=3,'Enter a reason for the void (at least 3 characters).');return r;};
const outletName=(k:Kit,o:POSOrder)=>slice(k).outlets.find(x=>x.id===o.outlet)?.name??o.outlet;
/** DEC-072: numbers the bill's tax invoice in the RST series of the business date's FY (the next number, never reused) with the bill's own
 *  CGST / SGST, SAC 996331 and the hotel GSTIN on documents (Master configuration brand.gstin; fictional in the demo). */
function issueRst(k:Kit,st:POSSettlement,supply:'B2C'|'B2B',buyer?:POSSettlement['buyer']){
  slice(k); // posX exists before the series counts in posX.rstSeq
  // MA-11: the RST series is a row of the numbering-series master (MA-5) — the seeded row continues posX.rstSeq (RST/2627/0000001…).
  const invoiceNo=fdmAllocate(k.s,'RST');
  const x=gstSplit(st.tax),gstin=String(cfgValueOn(k.s,'brand.gstin')??'').trim();
  Object.assign(st,{invoiceNo,supply,...(buyer?{buyer}:{}),cgst:x.cgst,sgst:x.sgst,sac:POS_RST_SAC,...(gstin?{hotelGstin:gstin}:{})});
  return invoiceNo;
}
const hasBuyer=(cmd:Record<string,unknown>)=>['buyerGstin','buyerName','buyerAddress'].some(f=>String(cmd[f]??'').trim()!=='');

const POS_ORDER_OPEN:Handler=k=>{
  const {s,cmd,req,open,assert,id,uid}=k;req('billing');open();
  const p=slice(k),out=p.outlets.find(o=>o.id===cmd.outlet);assert(out,'Choose an outlet.','NOT_FOUND');
  assert(posOutletActive(out!),`${out!.name} is retired — choose an active outlet.`,'INVALID_TRANSITION');
  const covers=cmd.covers===undefined?1:Number(cmd.covers);
  assert(Number.isInteger(covers)&&covers>=1&&covers<=20,'Covers must be 1–20.');
  const o:POSOrder={id:id('POS'),outlet:out!.id,covers,date:s.date,openedAt:s.clock,openedBy:uid,status:'Open',version:1,lines:[],kots:[]};
  if(out!.kind==='table'){
    assert(posTablesOf(s,out!.id).some(t=>t.tableNo===cmd.table),'Choose a table.');
    assert(!p.orders.some(x=>x.outlet===out!.id&&x.status==='Open'&&x.table===cmd.table),`Table ${cmd.table} already has an open check.`,'NOT_AVAILABLE');
    o.table=cmd.table;if(String(cmd.guest??'').trim())o.guest=String(cmd.guest).trim().slice(0,60);
  }else{
    const b=s.bookings.find(b=>b.room===cmd.room&&b.status==='In house');
    assert(b,'Room service is only for rooms with an in-house guest.','INVALID_TRANSITION');
    assert(!p.orders.some(x=>x.outlet===out!.id&&x.status==='Open'&&x.room===cmd.room),`Room ${cmd.room} already has an open room-service check.`,'NOT_AVAILABLE');
    o.room=b!.room;o.booking=b!.id;o.guest=b!.guest;
  }
  p.orders.push(o);
  return `${o.id} · ${out!.name} · ${posSpot(o)}${o.guest?' · '+o.guest:''} · ${covers} cover${covers===1?'':'s'}`;
};

const POS_ITEM_ADD:Handler=k=>{
  const {cmd,req,open,assert,id}=k;req('billing');open();const o=order(k);editable(k,o);
  const m=slice(k).menu.find(m=>m.id===cmd.item);assert(m,'Menu item not found.','NOT_FOUND');
  const block=posItemBlock(k.s,m!,o.outlet);
  if(block)assert(false,block,posSoldOut(k.s,m!)?'NOT_AVAILABLE':block.startsWith('This item is not')?'VALIDATION':'INVALID_TRANSITION');
  const qty=cmd.qty===undefined?1:Number(cmd.qty);assert(Number.isInteger(qty)&&qty>=1&&qty<=50,'Quantity must be 1–50.');
  // One GST class per check (MA-4): a banquet-hall or other-service item goes on its own check.
  const cls=posItemClass(m!);
  if(posActiveLines(o).length&&posCheckClass(o)!==cls){const name=(c:string)=>POS_GST_CLASSES.find(x=>x.value===c)?.label??c;
    assert(false,`This check is billed as ${name(posCheckClass(o))}; ${m!.name} is ${name(cls)} — open a separate check for it.`,'INVALID_TRANSITION');}
  const price=posItemPrice(k.s,m!)!;
  const same=o.lines.find(l=>l.item===m!.id&&!l.kot&&!l.voided&&l.price===price);
  if(same){assert(same.qty+qty<=50,'Quantity must be 1–50.');same.qty+=qty;}
  else o.lines.push({id:id('POSL'),item:m!.id,name:m!.name,price,qty,...(cls!=='food'?{gst:cls}:{})});
  o.version++;return `${o.id} · +${qty} ${m!.name}`;
};

const POS_ITEM_QTY:Handler=k=>{
  const {cmd,req,open,assert}=k;req('billing');open();const o=order(k);editable(k,o);
  const l=o.lines.find(l=>l.id===cmd.line);assert(l,'Item not found on this check.','NOT_FOUND');
  assert(!l!.kot&&!l!.voided,'This item was sent to the kitchen. Void it with a reason instead.','INVALID_TRANSITION');
  const q=Number(cmd.qty);assert(Number.isInteger(q)&&q>=0&&q<=50,'Quantity must be 0–50.');
  if(q===0)o.lines=o.lines.filter(x=>x.id!==l!.id);else l!.qty=q;
  o.version++;return `${o.id} · ${l!.name} ${q===0?'removed (not sent)':'qty '+q}`;
};

const POS_KOT_SEND:Handler=k=>{
  const {s,req,open,assert,id,uid}=k;req('billing');open();const o=order(k);editable(k,o);
  const lines=posUnsent(o);assert(lines.length,'Add items before sending a KOT.');
  // MA-4: one KOT per kitchen station (item station → outlet KOT station → Kitchen), in order of first appearance.
  const p=slice(k),seqKey=o.outlet+':'+s.date,menu=p.menu;
  const groups=new Map<string,typeof lines>();
  for(const l of lines){const st=posStationOf(s,o.outlet,menu.find(m=>m.id===l.item));(groups.get(st)??groups.set(st,[]).get(st)!).push(l);}
  const configured=groups.size>1||[...groups.keys()][0]!==POS_DEFAULT_STATION||!!p.outlets.find(x=>x.id===o.outlet)?.kotStation;
  const sent:string[]=[];
  for(const [st,ls] of groups){
    const no=(p.kotSeq[seqKey]??0)+1;p.kotSeq[seqKey]=no;
    const kot:POSKot={id:id('KOT'),no,at:s.clock,by:uid,status:'Preparing' as POSKotStatus,...(configured?{station:st}:{})};
    o.kots.push(kot);ls.forEach(l=>l.kot=kot.id);sent.push(`KOT ${no}${configured?' ('+st+')':''}`);
  }
  o.version++;
  return `${o.id} · ${sent.join(', ')} · ${lines.reduce((a,l)=>a+l.qty,0)} item(s) to kitchen · ${posSpot(o)}`;
};

const POS_KOT_STATUS:Handler=k=>{
  const {s,cmd,req,open,assert}=k;req('billing');open();const o=order(k);
  assert(o.status!=='Void','This check was voided.','INVALID_TRANSITION');
  const kot=o.kots.find(x=>x.id===cmd.kot);assert(kot,'KOT not found.','NOT_FOUND');
  const next=posNextKotStatus(kot!.status);
  assert(next&&cmd.status===next,next?`KOT ${kot!.no} can only move to ${next}.`:`KOT ${kot!.no} is already served.`,'INVALID_TRANSITION');
  kot!.status=next!;if(next==='Ready')kot!.readyAt=s.clock;if(next==='Served')kot!.servedAt=s.clock;
  return `${o.id} · KOT ${kot!.no} → ${next}`;
};

const POS_SETTLE:Handler=k=>{
  const {s,cmd,req,open,assert,id,uid,financial}=k;
  req(cmd.mode==='folio'?'billing':'payments');
  assert(!rbacIsRestaurant(s,uid),'The Restaurant role takes orders; Reception or the Cashier settles the bill (DEC-079).','FORBIDDEN');
  const o=order(k);
  if(o.status==='Settled')assert(false,'This check is already settled.','INVALID_TRANSITION');
  editable(k,o);open();
  assert(posActiveLines(o).length,'This check has no items to settle.');
  assert(!posUnsent(o).length,'Send the remaining items to the kitchen (KOT) or remove them before settling.','INVALID_TRANSITION');
  // DEC-069: restaurant / room service GST from the master (5 %, or 18 % when the hotel is specified premises this FY); one bill = one line.
  const p=slice(k),taxBps=posCheckRate(s,o,s.date).bps,net=posActiveLines(o).reduce((a,l)=>a+posLineValue(l),0),tax=gstLineTax(net,taxBps),total=net+tax;
  assert(net>0,'This check has no value to settle.');
  const billNo=`POS/${s.date.slice(2,4)}${s.date.slice(5,7)}/${String(p.billSeq+1).padStart(4,'0')}`;
  if(cmd.mode==='folio'){
    assert(!hasBuyer(cmd),'A bill charged to a room folio is invoiced to the guest after checkout; give the business GSTIN on the room invoice.');
    const bid=cmd.booking||o.booking;const x=s.bookings.find(b=>b.id===bid);assert(x,'Choose the in-house guest to charge.','NOT_FOUND');
    financial(x!);assert(x!.status==='In house','Only an in-house guest’s folio can take a room charge.','INVALID_TRANSITION');
    const lid='posline:'+o.id;assert(!x!.lines.some(l=>l.id===lid),'This check is already on the folio.','INVALID_TRANSITION');
    const n=posActiveLines(o).reduce((a,l)=>a+l.qty,0);
    // Same line shape as CHARGE (tax snapshot on the line); ACC-2 (contract §8, rulebook A3): kind 'food' → line:food → 4200 F&B revenue.
    x!.lines.push({id:lid,label:`${outletName(k,o)} · bill ${billNo} · ${n} item${n===1?'':'s'}`,amount:net,date:s.date,kind:'food',taxBps});x!.version++;
    o.settlement={mode:'folio',billNo,net,taxBps,tax,total,date:s.date,at:s.clock,by:uid,booking:x!.id,folioLineId:lid};
    if(!o.booking){o.booking=x!.id;o.guest??=x!.guest;}
    p.billSeq++;o.status='Settled';o.version++;
    return `${o.id} · bill ${billNo} · ${money(total)} (net ${money(net)} + tax ${money(tax)}) posted to folio ${x!.id} · room ${x!.room}`;
  }
  assert(cmd.mode==='outlet','Choose how the bill is settled.');
  const bo=posBuyerOf(cmd,total);if('error' in bo)assert(false,bo.error);
  // MA-3: an active payment method usable at the POS (money masters; seed = the old POS_TENDERS Cash / UPI / Card).
  {const row=typeof cmd.method==='string'?mnyMethod(s,cmd.method):undefined;
    if(row&&!row.active&&(row.usableAt??[]).includes('pos'))mstFail('MASTER_RETIRED',`${row.name} is retired — choose an active payment method.`);}
  const pm=mnyMethodFor(s,cmd.method,'pos');
  assert(pm,`Select ${orList(mnyNames(s,'pos'))}.`);
  const method=pm!.name;
  const reference=String(cmd.reference??'').trim();
  const cash=mnyIsDrawer(s,pm!.name);
  // Shared till (DEC-052): outlet cash goes into the front-desk drawer, so it needs the open cash shift like folio cash.
  if(cash)assert(s.shiftOpen,'The cash shift is closed, so no more cash can be taken today. Settle by UPI or card, or charge the room.','INVALID_TRANSITION');
  else if(pm!.needsReference)assert(reference,'Enter a demonstration transaction reference.');
  const rid=id('POSR');
  p.receipts.push({id:rid,order:o.id,outlet:o.outlet,method:method,amount:total,net,tax,taxBps,reference:reference||(cash?'Outlet cash':method),date:s.date,actor:uid,kind:'sale'});
  o.settlement={mode:'outlet',billNo,method:method,reference:reference||undefined,net,taxBps,tax,total,date:s.date,at:s.clock,by:uid,receiptId:rid};
  if(o.settlement.reference===undefined)delete o.settlement.reference;
  const b=bo as Exclude<typeof bo,{error:string}>,invoiceNo=issueRst(k,o.settlement,b.supply,b.buyer);
  p.billSeq++;o.status='Settled';o.version++;
  return `${o.id} · bill ${billNo} · ${money(total)} ${method} at ${outletName(k,o)} (outlet receipt ${rid}${cash?' · counted in the front-desk drawer':''}) · tax invoice ${invoiceNo}${b.supply==='B2B'?' (B2B '+b.buyer!.gstin+')':''}`;
};

import {cbIsLocked,cbLockedMessage} from '../../../domain/streams/cashbook';
/** Executes a void (item or bill). Shared by direct voids and approved requests. */
function applyVoid(k:Kit,o:POSOrder,scope:'item'|'bill',lineId:string|undefined,reason:string,by:string,approval?:string){
  const {s,id,assert}=k;
  const mark=(l:POSOrder['lines'][number])=>{l.voided=true;l.voidReason=reason;l.voidBy=by;l.voidDate=s.date;if(approval)l.voidApproval=approval;};
  if(scope==='item'){const l=o.lines.find(l=>l.id===lineId)!;mark(l);o.version++;return `${o.id} · void ${l.qty}× ${l.name} (${money(posLineValue(l))}) · ${reason}`;}
  if(o.status==='Settled'){
    const st=o.settlement!;assert(!cbIsLocked(s,st.date),cbLockedMessage(st.date,'voiding a bill settled that day'),'DAY_CLOSED'); // DEC-065
    assert(st.mode==='outlet','A bill posted to a room folio is corrected on the folio, not voided here.','INVALID_TRANSITION');
    if(mnyIsDrawer(s,st.method!))assert(s.shiftOpen,'The cash shift is closed. A cash bill cannot be reversed now.','INVALID_TRANSITION');
    slice(k).receipts.push({id:id('POSR'),order:o.id,outlet:o.outlet,method:st.method!,amount:-st.total,net:-st.net,tax:-st.tax,taxBps:st.taxBps,reference:'Void of '+st.billNo,date:s.date,actor:by,kind:'void'});
    // DEC-072: the bill's tax invoice is reversed in full by a credit note dated today (the invoice itself never changes).
    // Masters integration: one credit-note series (MA-5 numbering master; the seeded CRN series continues s.creditNoteSeqC in FY 2026-27).
    if(st.invoiceNo&&!st.creditNoteNo){st.creditNoteNo=fdmAllocate(s,'CRN');st.creditNoteDate=s.date;}
  }
  posActiveLines(o).forEach(mark);
  o.status='Void';o.voidReason=reason;o.voidedBy=by;o.voidedAt=s.clock;o.version++;
  return `${o.id} · bill void${o.settlement?' '+o.settlement.billNo:''}${o.settlement?.creditNoteNo?' · credit note '+o.settlement.creditNoteNo+' against '+o.settlement.invoiceNo:''} · ${reason}`;
}
/** Void value, or a pending approval when above POS_VOID_LIMIT. */
function voidOrRequest(k:Kit,o:POSOrder,scope:'item'|'bill',value:number,lineId?:string){
  const {s,uid,id}=k;const reason=reasonOf(k);
  if(value<=POS_VOID_LIMIT)return applyVoid(k,o,scope,lineId,reason,uid);
  const a:Approval={id:id('APR'),booking:'',amount:value,reason,status:'Pending',actor:uid,version:o.version,kind:'pos-void',target:o.id,
    expiresAt:addMinutes(s.clock,POS_APPROVAL_TTL_MINUTES),meta:{command:'POS_VOID_DECIDE',scope,summary:`${scope==='item'?'Void item':'Void bill'} · ${posSpot(o)}`}};
  if(lineId)a.meta!.line=lineId;
  s.approvals.push(a);o.pendingVoid=a.id;
  return `${o.id} · void ${scope} ${money(value)} above ${money(POS_VOID_LIMIT)} → approval ${a.id} requested`;
}

const POS_VOID_ITEM:Handler=k=>{
  const {cmd,req,open,assert}=k;req('billing');open();const o=order(k);editable(k,o);
  const l=o.lines.find(l=>l.id===cmd.line);assert(l,'Item not found on this check.','NOT_FOUND');
  assert(!l!.voided,'This item is already void.','INVALID_TRANSITION');
  assert(l!.kot,'This item has not been sent to the kitchen — just remove it.','INVALID_TRANSITION');
  return voidOrRequest(k,o,'item',posLineValue(l!),l!.id);
};

const POS_VOID_BILL:Handler=k=>{
  const {s,req,open,assert}=k;req('billing');open();const o=order(k);
  assert(o.status!=='Void','This check is already void.','INVALID_TRANSITION');
  assert(!o.pendingVoid,'A void request is pending for this check. Ask a manager to decide it first.','INVALID_TRANSITION');
  if(o.status==='Settled'){
    req('payments');
    assert(!cbIsLocked(s,o.settlement!.date),cbLockedMessage(o.settlement!.date,'voiding a bill settled that day'),'DAY_CLOSED'); // DEC-065
    assert(o.settlement!.mode==='outlet','A bill posted to a room folio is corrected on the folio, not voided here.','INVALID_TRANSITION');
    assert(o.settlement!.date===s.date,'Only bills settled on the current business date can be voided.','INVALID_TRANSITION');
    return voidOrRequest(k,o,'bill',o.settlement!.total);
  }
  return voidOrRequest(k,o,'bill',posActiveLines(o).reduce((a,l)=>a+posLineValue(l),0));
};

const POS_VOID_DECIDE:Handler=k=>{
  const {s,cmd,req,open,assert,uid}=k;req('approve');open();
  const a=s.approvals.find(a=>a.id===cmd.id&&a.kind==='pos-void');
  assert(a&&a.status==='Pending','This request is no longer pending.','INVALID_TRANSITION');
  assert(a!.actor!==uid,'You cannot approve your own request. Switch to another authorized demo user.','FORBIDDEN');
  const o=slice(k).orders.find(o=>o.id===a!.target);assert(o,'Check not found.','NOT_FOUND');
  a!.approver=uid;a!.decidedAt=s.clock;delete o!.pendingVoid;
  if(!cmd.approve){a!.status='Rejected';if(String(cmd.reason??'').trim())a!.meta={...a!.meta,rejectReason:String(cmd.reason).trim()};return `${a!.id} · void rejected · ${o!.id}`;}
  assert(!(a!.expiresAt&&s.clock>a!.expiresAt),'This request has expired. Reject it and ask for a new one.','INVALID_TRANSITION');
  assert(o!.version===a!.version,'The check changed after this request. Reject it and request again.','STALE_VERSION');
  const scope=a!.meta?.scope==='item'?'item':'bill';
  const detail=applyVoid(k,o!,scope,a!.meta?.line as string|undefined,a!.reason,a!.actor,a!.id);
  a!.status='Executed';
  return `${a!.id} · approved · ${detail}`;
};

/** DEC-072: the tax invoice of a settled outlet bill that has none yet (settled before the RST series, or — on the server — before the hotel
 *  GSTIN was recorded). Idempotent: an issued number is returned, never a second one. */
const POS_INVOICE_ISSUE:Handler=k=>{
  const {req,open,assert}=k;req('payments');open();const o=order(k);const st=o.settlement;
  assert(st&&o.status==='Settled',o.status==='Void'?'This POS bill was voided; there is nothing to invoice.':'Settle the bill first.','INVALID_TRANSITION');
  assert(st!.mode==='outlet',"This bill was charged to a room folio: it is on the guest's room tax invoice after checkout, not in the restaurant series.",'INVALID_TRANSITION');
  if(st!.invoiceNo)return `${o.id} · bill ${st!.billNo} · tax invoice ${st!.invoiceNo} (already issued)`;
  return `${o.id} · bill ${st!.billNo} · tax invoice ${issueRst(k,st!,'B2C')}`;
};

/** "Sold out today" switch on the POS page (MA-4: the menu master's soldOutToday; lapses at the next business date). */
const POS_MENU_TOGGLE:Handler=k=>{
  const {s,cmd,req,open,assert}=k;req('billing');open();
  const m=slice(k).menu.find(m=>m.id===cmd.item);assert(m,'Menu item not found.','NOT_FOUND');
  if(posSoldOut(s,m!)){m!.available=true;delete m!.soldOutOn;return `${m!.name} · available again`;}
  m!.available=false;m!.soldOutOn=s.date;return `${m!.name} · sold out today (86)`;
};

export const handlers:Record<string,Handler>={POS_ORDER_OPEN,POS_ITEM_ADD,POS_ITEM_QTY,POS_KOT_SEND,POS_KOT_STATUS,POS_SETTLE,POS_VOID_ITEM,POS_VOID_BILL,POS_VOID_DECIDE,POS_INVOICE_ISSUE,POS_MENU_TOGGLE};
