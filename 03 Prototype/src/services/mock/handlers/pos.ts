// Module 23 · Restaurant POS & room service — command handlers. Command types MUST start with 'POS_'. Return value = audit/summary text.
// Rules: 02 Planning/modules/HCC_Module_23_POS_Spec.md §4. Money = integer paise; synthetic tax via taxRateFor; simulated clock only.
import type {Handler,Kit} from '../commands';
import type {Approval} from '../../../domain/types';
import {money,addMinutes} from '../../../domain/money';
import {taxRateFor} from '../../../domain/selectors';
import {posEmpty,posOrderTotals,posLineValue,posActiveLines,posUnsent,posSpot,posNextKotStatus,
  POS_VOID_LIMIT,POS_TENDERS,POS_APPROVAL_TTL_MINUTES} from '../../../domain/streams/pos';
import type {POSOrder,POSState,POSKotStatus} from '../../../domain/streams/pos';

const slice=(k:Kit):POSState=>{k.s.posX??=posEmpty();return k.s.posX;};
const order=(k:Kit):POSOrder=>{const o=slice(k).orders.find(o=>o.id===k.cmd.id);k.assert(o,'Check not found.','NOT_FOUND');return o!;};
/** An open, unlocked check. */
const editable=(k:Kit,o:POSOrder)=>{
  k.assert(o.status==='Open','This check is already '+o.status.toLowerCase()+'.','INVALID_TRANSITION');
  k.assert(!o.pendingVoid,'A void request is pending for this check. Ask a manager to decide it first.','INVALID_TRANSITION');
};
const reasonOf=(k:Kit)=>{const r=String(k.cmd.reason??'').trim();k.assert(r.length>=3,'Enter a reason for the void (at least 3 characters).');return r;};
const outletName=(k:Kit,o:POSOrder)=>slice(k).outlets.find(x=>x.id===o.outlet)?.name??o.outlet;

const POS_ORDER_OPEN:Handler=k=>{
  const {s,cmd,req,open,assert,id,uid}=k;req('billing');open();
  const p=slice(k),out=p.outlets.find(o=>o.id===cmd.outlet);assert(out,'Choose an outlet.','NOT_FOUND');
  const covers=cmd.covers===undefined?1:Number(cmd.covers);
  assert(Number.isInteger(covers)&&covers>=1&&covers<=20,'Covers must be 1–20.');
  const o:POSOrder={id:id('POS'),outlet:out!.id,covers,date:s.date,openedAt:s.clock,openedBy:uid,status:'Open',version:1,lines:[],kots:[]};
  if(out!.kind==='table'){
    assert(out!.tables?.includes(cmd.table),'Choose a table.');
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
  assert(m!.outlets.includes(o.outlet),'This item is not on this outlet’s menu.');
  assert(m!.available,`${m!.name} is unavailable (86) right now.`,'NOT_AVAILABLE');
  const qty=cmd.qty===undefined?1:Number(cmd.qty);assert(Number.isInteger(qty)&&qty>=1&&qty<=50,'Quantity must be 1–50.');
  const same=o.lines.find(l=>l.item===m!.id&&!l.kot&&!l.voided);
  if(same){assert(same.qty+qty<=50,'Quantity must be 1–50.');same.qty+=qty;}
  else o.lines.push({id:id('POSL'),item:m!.id,name:m!.name,price:m!.price,qty});
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
  const p=slice(k),seqKey=o.outlet+':'+s.date,no=(p.kotSeq[seqKey]??0)+1;p.kotSeq[seqKey]=no;
  const kot={id:id('KOT'),no,at:s.clock,by:uid,status:'Preparing' as POSKotStatus};
  o.kots.push(kot);lines.forEach(l=>l.kot=kot.id);o.version++;
  return `${o.id} · KOT ${no} · ${lines.reduce((a,l)=>a+l.qty,0)} item(s) to kitchen · ${posSpot(o)}`;
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
  const o=order(k);
  if(o.status==='Settled')assert(false,'This check is already settled.','INVALID_TRANSITION');
  editable(k,o);open();
  assert(posActiveLines(o).length,'This check has no items to settle.');
  assert(!posUnsent(o).length,'Send the remaining items to the kitchen (KOT) or remove them before settling.','INVALID_TRANSITION');
  const p=slice(k),taxBps=taxRateFor(s,s.date),net=posActiveLines(o).reduce((a,l)=>a+posLineValue(l),0),tax=Math.round(net*taxBps/10000),total=net+tax;
  assert(net>0,'This check has no value to settle.');
  const billNo=`POS/${s.date.slice(2,4)}${s.date.slice(5,7)}/${String(p.billSeq+1).padStart(4,'0')}`;
  if(cmd.mode==='folio'){
    const bid=cmd.booking||o.booking;const x=s.bookings.find(b=>b.id===bid);assert(x,'Choose the in-house guest to charge.','NOT_FOUND');
    financial(x!);assert(x!.status==='In house','Only an in-house guest’s folio can take a room charge.','INVALID_TRANSITION');
    const lid='posline:'+o.id;assert(!x!.lines.some(l=>l.id===lid),'This check is already on the folio.','INVALID_TRANSITION');
    const n=posActiveLines(o).reduce((a,l)=>a+l.qty,0);
    // Same line shape as CHARGE (kind 'service', tax snapshot on the line).
    x!.lines.push({id:lid,label:`${outletName(k,o)} · bill ${billNo} · ${n} item${n===1?'':'s'}`,amount:net,date:s.date,kind:'service',taxBps});x!.version++;
    o.settlement={mode:'folio',billNo,net,taxBps,tax,total,date:s.date,at:s.clock,by:uid,booking:x!.id,folioLineId:lid};
    if(!o.booking){o.booking=x!.id;o.guest??=x!.guest;}
    p.billSeq++;o.status='Settled';o.version++;
    return `${o.id} · bill ${billNo} · ${money(total)} (net ${money(net)} + tax ${money(tax)}) posted to folio ${x!.id} · room ${x!.room}`;
  }
  assert(cmd.mode==='outlet','Choose how the bill is settled.');
  assert((POS_TENDERS as readonly string[]).includes(cmd.method),'Select Cash, UPI or Card.');
  const reference=String(cmd.reference??'').trim();
  // Shared till (DEC-052): outlet cash goes into the front-desk drawer, so it needs the open cash shift like folio cash.
  if(cmd.method==='Cash')assert(s.shiftOpen,'The cash shift is closed, so no more cash can be taken today. Settle by UPI or card, or charge the room.','INVALID_TRANSITION');
  else assert(reference,'Enter a demonstration transaction reference.');
  const rid=id('POSR');
  p.receipts.push({id:rid,order:o.id,outlet:o.outlet,method:cmd.method,amount:total,net,tax,taxBps,reference:reference||'Outlet cash',date:s.date,actor:uid,kind:'sale'});
  o.settlement={mode:'outlet',billNo,method:cmd.method,reference:reference||undefined,net,taxBps,tax,total,date:s.date,at:s.clock,by:uid,receiptId:rid};
  if(o.settlement.reference===undefined)delete o.settlement.reference;
  p.billSeq++;o.status='Settled';o.version++;
  return `${o.id} · bill ${billNo} · ${money(total)} ${cmd.method} at ${outletName(k,o)} (outlet receipt ${rid}${cmd.method==='Cash'?' · counted in the front-desk drawer':''})`;
};

/** Executes a void (item or bill). Shared by direct voids and approved requests. */
function applyVoid(k:Kit,o:POSOrder,scope:'item'|'bill',lineId:string|undefined,reason:string,by:string,approval?:string){
  const {s,id,assert}=k;
  const mark=(l:POSOrder['lines'][number])=>{l.voided=true;l.voidReason=reason;l.voidBy=by;l.voidDate=s.date;if(approval)l.voidApproval=approval;};
  if(scope==='item'){const l=o.lines.find(l=>l.id===lineId)!;mark(l);o.version++;return `${o.id} · void ${l.qty}× ${l.name} (${money(posLineValue(l))}) · ${reason}`;}
  if(o.status==='Settled'){
    const st=o.settlement!;assert(st.mode==='outlet','A bill posted to a room folio is corrected on the folio, not voided here.','INVALID_TRANSITION');
    if(st.method==='Cash')assert(s.shiftOpen,'The cash shift is closed. A cash bill cannot be reversed now.','INVALID_TRANSITION');
    slice(k).receipts.push({id:id('POSR'),order:o.id,outlet:o.outlet,method:st.method!,amount:-st.total,net:-st.net,tax:-st.tax,taxBps:st.taxBps,reference:'Void of '+st.billNo,date:s.date,actor:by,kind:'void'});
  }
  posActiveLines(o).forEach(mark);
  o.status='Void';o.voidReason=reason;o.voidedBy=by;o.voidedAt=s.clock;o.version++;
  return `${o.id} · bill void${o.settlement?' '+o.settlement.billNo:''} · ${reason}`;
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

const POS_MENU_TOGGLE:Handler=k=>{
  const {cmd,req,open,assert}=k;req('billing');open();
  const m=slice(k).menu.find(m=>m.id===cmd.item);assert(m,'Menu item not found.','NOT_FOUND');
  m!.available=!m!.available;return `${m!.name} · ${m!.available?'available again':'marked unavailable (86)'}`;
};

export const handlers:Record<string,Handler>={POS_ORDER_OPEN,POS_ITEM_ADD,POS_ITEM_QTY,POS_KOT_SEND,POS_KOT_STATUS,POS_SETTLE,POS_VOID_ITEM,POS_VOID_BILL,POS_VOID_DECIDE,POS_MENU_TOGGLE};
