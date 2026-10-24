// Module 23 · POS shared UI parts (desktop page, phone page and sheets).
import React,{useState} from 'react';
import {Send,Minus,Plus,Ban,Printer,Wallet,ShieldCheck,Check,X,Leaf,Drumstick} from 'lucide-react';
import {money,posOrderTotals,posLineStatus,posLineValue,posUnsent,posActiveLines,posSpot,posMenuFor,posCategories,posOpenOrderAt,posOutlet,posInHouse,
  posPendingVoid,posSlice,POS_VOID_LIMIT} from '../../services/mock';
import type {POSOrder,POSKotStatus} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill,clockTime} from '../../components/ui';

export const posUserName=(s:ReturnType<typeof useHcc>['s'],id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
const tone:Record<string,string>={New:'pos-st-new',Preparing:'pos-st-prep',Ready:'pos-st-ready',Served:'pos-st-served',Void:'pos-st-void',Open:'pos-st-prep',Settled:'pos-st-ready'};
export function PosStatus({st}:{st:string}){return <span className={'pos-status '+(tone[st]||'pos-st-new')}>{st==='New'?'Not sent':st}</span>;}

/** Table / room tiles for one outlet. */
export function PosSpots({outlet,selected,onSelect}:{outlet:string;selected?:string;onSelect:(spot:string)=>void}){
  const {s}=useHcc();const out=posOutlet(s,outlet);if(!out)return null;
  const spots=out.kind==='table'?(out.tables||[]).map(t=>({id:t,label:'Table '+t,sub:''})):posInHouse(s).map(b=>({id:b.room,label:'Room '+b.room,sub:b.guest}));
  if(!spots.length)return <p className="muted pos-pad">No in-house rooms right now.</p>;
  return <div className="pos-spots" role="list">{spots.map(x=>{const o=posOpenOrderAt(s,outlet,x.id),t=o&&posOrderTotals(s,o);
    const live=o?o.kots.filter(k=>o.lines.some(l=>l.kot===k.id&&!l.voided)):[];
    const kitchen=live.some(k=>k.status==='Ready')?'Ready':live.some(k=>k.status==='Preparing')?'Preparing':undefined;
    return <button type="button" role="listitem" key={x.id} className={'pos-spot'+(o?' busy':'')+(selected===x.id?' on':'')} aria-pressed={selected===x.id} onClick={()=>onSelect(x.id)}>
      <strong>{x.label}</strong><span>{x.sub||(o?.guest??(o?`${o.covers} cover${o.covers===1?'':'s'}`:'Free'))}</span>
      <small>{o?<>{money(t!.total)} · {t!.items} item{t!.items===1?'':'s'}</>:'Tap to open a check'}</small>
      {kitchen&&<PosStatus st={kitchen}/>}{o?.pendingVoid&&<span className="pos-status pos-st-void">Void pending</span>}
    </button>;})}</div>;
}

/** Menu grid with category filter. Tapping an item adds 1 to the check. */
export function PosMenu({outlet,order}:{outlet:string;order?:POSOrder}){
  const {s,run,busy,permission}=useHcc();const cats=posCategories(s,outlet);const [cat,setCat]=useState('All');
  const items=posMenuFor(s,outlet).filter(m=>cat==='All'||m.category===cat);
  const locked=!order||order.status!=='Open'||!!order.pendingVoid;
  return <div className="pos-menu">
    <div className="pos-cats" role="group" aria-label="Menu category">{['All',...cats].map(c=><button type="button" key={c} aria-pressed={cat===c} className={cat===c?'on':''} onClick={()=>setCat(c)}>{c}</button>)}</div>
    {!order&&<p className="pos-hint">Choose a table or room first, then tap dishes to add them.</p>}
    <div className="pos-items">{items.map(m=><button type="button" key={m.id} className={'pos-item'+(m.available?'':' off')} disabled={busy||locked||!m.available||!permission('billing')}
      aria-label={`Add ${m.name}, ${money(m.price)}${m.available?'':', unavailable'}`}
      onClick={()=>order&&run({type:'POS_ITEM_ADD',id:order.id,item:m.id},`${m.name} added`)}>
      <span className={'pos-veg '+(m.veg?'veg':'nonveg')} aria-label={m.veg?'Vegetarian':'Non-vegetarian'}>{m.veg?<Leaf size={12}/>:<Drumstick size={12}/>}</span>
      <strong>{m.name}</strong><span className="pos-price">{money(m.price)}</span>{!m.available&&<small>Unavailable (86)</small>}
      {order&&<b className="pos-inc">{order.lines.filter(l=>l.item===m.id&&!l.voided).reduce((a,l)=>a+l.qty,0)||''}</b>}
    </button>)}</div>
  </div>;
}

/** The current check: lines, totals and actions. Used on desktop (side panel) and phone (cart sheet). */
export function PosCheck({o,compact}:{o:POSOrder;compact?:boolean}){
  const {s,uid,run,busy,open,permission}=useHcc();
  const t=posOrderTotals(s,o),unsent=posUnsent(o),apr=posPendingVoid(s,o),out=posOutlet(s,o.outlet);
  const isOpen=o.status==='Open',lockedVoid=!!o.pendingVoid;
  const canDecide=apr&&permission('approve')&&apr.actor!==uid;
  const voidValue=(v:number)=>v>POS_VOID_LIMIT?' · needs approval':'';
  return <div className={'pos-check'+(compact?' compact':'')}>
    <div className="pos-check-head"><div><div className="eyebrow">{out?.name.toUpperCase()} · {o.id}</div><h2>{posSpot(o)}</h2>
      <p>{o.guest?o.guest+' · ':''}{o.covers} cover{o.covers===1?'':'s'} · opened {clockTime(o.openedAt)} by {posUserName(s,o.openedBy)}{o.booking?' · folio '+o.booking:''}</p></div>
      <PosStatus st={o.status}/></div>
    {apr&&<div className="info-note pos-note"><ShieldCheck size={16}/><div><strong>Void request {apr.id} · {money(apr.amount)}</strong> — “{apr.reason}” by {posUserName(s,apr.actor)}. The check is locked until another authorised user decides.
      {canDecide&&<div className="pos-row-actions"><button className="btn" disabled={busy} onClick={()=>run({type:'POS_VOID_DECIDE',id:apr.id,approve:false,key:'approval:'+apr.id},'Void request rejected')}><X size={15}/> Reject</button>
        <button className="btn primary" disabled={busy} onClick={()=>run({type:'POS_VOID_DECIDE',id:apr.id,approve:true,key:'approval:'+apr.id},'Void approved')}><Check size={15}/> Approve void</button></div>}
      {!canDecide&&apr.actor===uid&&<small> You raised it — no self-approval.</small>}</div></div>}
    <ul className="pos-lines" aria-label="Items on this check">
      {o.lines.map(l=>{const st=posLineStatus(o,l);return <li key={l.id} className={l.voided?'void':''}>
        <div className="pos-line-main"><strong>{l.name}</strong><span>{money(l.price)} × {l.qty} = <b>{money(posLineValue(l))}</b></span>
          <span className="pos-line-meta"><PosStatus st={st}/>{l.voided&&<small>{l.voidReason}{l.voidApproval?' · approved '+l.voidApproval:''}</small>}</span></div>
        {isOpen&&!lockedVoid&&permission('billing')&&(st==='New'?<div className="pos-qty" role="group" aria-label={'Quantity of '+l.name}>
            <button type="button" aria-label={'Decrease '+l.name} disabled={busy} onClick={()=>run({type:'POS_ITEM_QTY',id:o.id,line:l.id,qty:l.qty-1},l.qty>1?'Quantity updated':'Item removed')}><Minus size={16}/></button>
            <span className="number">{l.qty}</span>
            <button type="button" aria-label={'Increase '+l.name} disabled={busy||l.qty>=50} onClick={()=>run({type:'POS_ITEM_QTY',id:o.id,line:l.id,qty:l.qty+1},'Quantity updated')}><Plus size={16}/></button></div>
          :!l.voided&&<button type="button" className="btn small-btn danger" disabled={busy} onClick={()=>open({type:'pos-void',id:o.id,room:l.id})} aria-label={'Void '+l.name}><Ban size={14}/> Void{voidValue(posLineValue(l))}</button>)}
      </li>;})}
      {!o.lines.length&&<li className="pos-empty-line">No items yet — tap dishes on the menu.</li>}
    </ul>
    <div className="pos-totals"><div><span>Items</span><strong>{t.items}</strong></div><div><span>Net</span><strong>{money(t.net)}</strong></div>
      <div><span>Synthetic tax {t.taxBps/100}%{o.settlement?' (snapshot)':''}</span><strong>{money(t.tax)}</strong></div><div className="pos-grand"><span>Bill total</span><strong>{money(t.total)}</strong></div></div>
    {o.settlement&&<p className="pos-settled">Bill {o.settlement.billNo} · {o.settlement.mode==='folio'?`posted to room folio ${o.settlement.booking}`:`${o.settlement.method}${o.settlement.reference?' · '+o.settlement.reference:''} at outlet`} · {clockTime(o.settlement.at)} by {posUserName(s,o.settlement.by)}</p>}
    {o.status==='Void'&&<p className="pos-settled">Voided · {o.voidReason} · {o.voidedAt&&clockTime(o.voidedAt)}</p>}
    <div className="pos-actions">
      {isOpen&&permission('billing')&&<button className="btn primary" disabled={busy||!unsent.length||lockedVoid} onClick={()=>run({type:'POS_KOT_SEND',id:o.id},'KOT sent to the kitchen')}><Send size={15}/> Send KOT{unsent.length?` (${unsent.reduce((a,l)=>a+l.qty,0)})`:''}</button>}
      {isOpen&&(permission('payments')||permission('billing'))&&<button className="btn primary" disabled={busy||!posActiveLines(o).length||!!unsent.length||lockedVoid} onClick={()=>open({type:'pos-settle',id:o.id})}><Wallet size={15}/> Settle</button>}
      {o.kots.length>0&&<button className="btn" onClick={()=>open({type:'pos-print',id:o.id,room:o.kots.slice(-1)[0]!.id})}><Printer size={15}/> Print KOT</button>}
      {posActiveLines(o).length>0||o.settlement?<button className="btn" onClick={()=>open({type:'pos-print',id:o.id,room:'bill'})}><Printer size={15}/> {o.settlement?'Print bill':'Print pro-forma'}</button>:null}
      {permission('billing')&&!lockedVoid&&(isOpen&&o.lines.length>0||o.status==='Settled'&&o.settlement?.mode==='outlet'&&o.settlement.date===s.date&&permission('payments'))&&
        <button className="btn danger" disabled={busy} onClick={()=>open({type:'pos-void',id:o.id,room:'bill'})}><Ban size={15}/> Void bill</button>}
    </div>
    {isOpen&&unsent.length>0&&posActiveLines(o).length>unsent.length&&<p className="pos-hint">Send the new items to the kitchen before settling.</p>}
  </div>;
}

/** KOT card for the kitchen board. */
export function PosKotCard({o,kotId}:{o:POSOrder;kotId:string}){
  const {s,run,busy,permission,open}=useHcc();const k=o.kots.find(k=>k.id===kotId)!;const lines=o.lines.filter(l=>l.kot===k.id);
  const next:Record<POSKotStatus,POSKotStatus|undefined>={Preparing:'Ready',Ready:'Served',Served:undefined};const n=next[k.status];
  const out=posOutlet(s,o.outlet);
  return <article className={'pos-kot '+tone[k.status]}>
    <header><strong>KOT {k.no} · {posSpot(o)}</strong><PosStatus st={k.status}/></header>
    <small>{out?.name} · sent {clockTime(k.at)} by {posUserName(s,k.by)}{k.readyAt?` · ready ${clockTime(k.readyAt)}`:''}{k.servedAt?` · served ${clockTime(k.servedAt)}`:''}</small>
    <ul>{lines.map(l=><li key={l.id} className={l.voided?'void':''}><b>{l.qty}×</b> {l.name}{l.voided&&<em> — void</em>}</li>)}</ul>
    <div className="pos-row-actions">{n&&permission('billing')&&<button className="btn primary" disabled={busy} onClick={()=>run({type:'POS_KOT_STATUS',id:o.id,kot:k.id,status:n},`KOT ${k.no} → ${n}`)}>Mark {n.toLowerCase()}</button>}
      <button className="btn" onClick={()=>open({type:'pos-print',id:o.id,room:k.id})}><Printer size={15}/> Print</button></div>
  </article>;
}
export const posKitchenColumns:POSKotStatus[]=['Preparing','Ready','Served'];
export const posHasSlice=(s:ReturnType<typeof useHcc>['s'])=>posSlice(s).outlets.length>0;
export {Pill};
