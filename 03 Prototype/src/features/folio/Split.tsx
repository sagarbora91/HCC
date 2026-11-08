// F8 SF-UI · split folio screens (SF-CONTRACT §2, §4, §5): folio switcher, Split bill dialog ('sf-split'), the one check-out sheet, the check-in
// routing editor and the invoice list per folio. UI only: figures are totals(sfView(b,f)) (SplitLogic.ts), actions are sf-domain's commands.
// Desktop = button strip + tables-free summary lists; phone = chips (ChipFilter) and cards, 48 px targets. A single-folio stay renders nothing here.
import React,{useState} from 'react';
import {Split as SplitIcon,Building2,AlertTriangle,Check,Info} from 'lucide-react';
import {money,totals,grpActiveCompanies,grpCredit,sfIsSplit,sfFolios,sfView,SF_PRIMARY,SF_ROUTE_CATEGORIES} from '../../services/mock';
import type {Booking,SFRouteCategory,SFPayerKind} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Empty,Pill} from '../../components/ui';
import {ChipFilter} from '../../app/mobile/components';
import {sfSwitcher,sfFolioLabel,sfSettleSheet,sfInvoiceRows,sfFreeRoutes,sfOpenCommand,sfRouteCommand,sfRemoveCommand,sfOpen,SF_ROUTE_LABEL,SF_PAYER_LABEL} from './SplitLogic';

/** Folio switcher. Renders nothing for a single-folio stay. Desktop: pressed-button strip; phone: chips. */
export function SfSwitcher({b,value,onChange,mobile}:{b:Booking;value:string;onChange:(id:string)=>void;mobile?:boolean}){
  const tabs=sfSwitcher(b);
  if(!tabs.length)return null;
  if(mobile)return <ChipFilter label="Folio" value={value} onChange={onChange} options={tabs.map(t=>({id:t.id,label:t.label}))}/>;
  return <div className="filters sf-switcher" role="group" aria-label="Folio">{tabs.map(t=><button key={t.id} type="button" className={value===t.id?'active':''} aria-pressed={value===t.id} onClick={()=>onChange(t.id)}>{t.label}<small> · {money(t.balance)}</small></button>)}</div>;
}

/** Payer + routing line of the shown folio and the Split bill entry point (billing permission). */
export function SfFolioBar({b,folio,mobile}:{b:Booking;folio:string;mobile?:boolean}){
  const {act,open}=useHcc();
  const active=['Confirmed','In house'].includes(b.status);
  const f=sfFolios(b).find(x=>x.id===folio);
  return <div className={'sf-bar'+(mobile?' mm-meta':'')}>
    {sfIsSplit(b)&&f&&<p className="folio-company"><Building2 size={14}/>Folio {f.id} is paid by {f.id===SF_PRIMARY?b.guest:f.payer.name}{f.payer.gstin?` · GSTIN ${f.payer.gstin}`:''} · {f.routes.length?f.routes.map(c=>c==='room'?'room':'extras').join(' + ')+' post here':'no new postings routed here'}</p>}
    {active&&act('billing',()=>open({type:'sf-split',id:b.id}),sfIsSplit(b)?'Split bill / routing':'Split bill',mobile?'btn':'text-btn')}
  </div>;
}

const SPLIT_KINDS:SFPayerKind[]=['guest','company','udhaar'];
/** 'sf-split' dialog (id = booking): open a folio (payer kind, company with ₹ left, routes, reason), change routes, remove an empty folio. */
export function SplitBillModal({id}:{id?:string}){
  const {s,run,busy,modal,booking}=useHcc();
  const b=s.bookings.find(x=>x.id===id);
  const [kind,setKind]=useState<SFPayerKind>('company'),[ref,setRef]=useState(''),[name,setName]=useState(''),[gstin,setGstin]=useState('');
  const free=b?sfFreeRoutes(b):[];
  const [routes,setRoutes]=useState<SFRouteCategory[]>(['room']),[reason,setReason]=useState('');
  const [edit,setEdit]=useState<Record<string,{routes:SFRouteCategory[];reason:string}>>({});
  if(!b)return <Empty title="This record is unavailable"/>;
  const companies=grpActiveCompanies(s),udhaar=(s.guests??[]).filter(g=>g.creditAccount?.active);
  const toggle=(list:SFRouteCategory[],c:SFRouteCategory)=>list.includes(c)?list.filter(x=>x!==c):[...list,c];
  const folios=sfFolios(b).filter(f=>f.id!==SF_PRIMARY);
  const key=(p:string)=>`${modal?.key??'sf'}:${p}:${b.version}`;
  return <><div className="modal-heading"><div className="eyebrow">SPLIT BILL · {b.id}</div><h2>{b.guest}</h2>
      <p>Choose who pays which part. Later postings follow the routing; what is already posted stays where it is. At most four folios (A–D).</p></div>
    {folios.length>0&&<div className="summary-list sf-folios">{folios.map(f=>{const e=edit[f.id]??{routes:f.routes,reason:''},empty=!b.lines.some(l=>l.folio===f.id)&&!b.receipts.some(r=>r.folio===f.id);
      return <div key={f.id} className="sf-folio-edit"><span><strong>{sfFolioLabel(f,b.guest)}</strong><small>{SF_PAYER_LABEL[f.payer.kind]} · {money(totals(sfView(b,f.id)).balance)} balance{f.invoiceNo?` · invoice ${f.invoiceNo}`:''}</small></span>
        <div className="inline">{SF_ROUTE_CATEGORIES.map(c=><label key={c} className="checkbox-line"><input type="checkbox" checked={e.routes.includes(c)} disabled={!!f.invoiceNo} onChange={()=>setEdit({...edit,[f.id]:{...e,routes:toggle(e.routes,c)}})}/> {c==='room'?'Room':'Extras'}</label>)}</div>
        <label>Reason for the change<input value={e.reason} maxLength={200} onChange={ev=>setEdit({...edit,[f.id]:{...e,reason:ev.target.value}})}/></label>
        <div className="inline"><button type="button" className="btn small-btn" disabled={busy||!!f.invoiceNo} onClick={()=>void run(sfRouteCommand(b,f.id,e.routes,e.reason,key('route'+f.id)),`Folio ${f.id} routing saved`)}>Save routing</button>
          {empty&&<button type="button" className="text-btn" disabled={busy} onClick={()=>void run(sfRemoveCommand(b,f.id,key('rm'+f.id)),`Folio ${f.id} removed`)}>Remove empty folio</button>}</div></div>;})}</div>}
    {folios.length<3?<form className="form" onSubmit={e=>{e.preventDefault();void run(sfOpenCommand(b,{kind,ref,name,gstin,routes,reason},key('open')),'Folio opened',{after:()=>booking(b.id)});}}>
      <h3 className="gs-sub">Open a folio</h3>
      <label>Who pays<select value={kind} onChange={e=>{setKind(e.target.value as SFPayerKind);setRef('');}}>{SPLIT_KINDS.map(k=><option key={k} value={k}>{SF_PAYER_LABEL[k]}</option>)}</select></label>
      {kind==='guest'&&<><label>Payer name<input value={name} maxLength={120} onChange={e=>setName(e.target.value)} required/></label><label>GSTIN (optional)<input value={gstin} maxLength={15} onChange={e=>setGstin(e.target.value.toUpperCase())}/></label></>}
      {kind==='company'&&<label>Company account<select value={ref} onChange={e=>setRef(e.target.value)} required><option value="">Choose an account…</option>{companies.map(c=><option key={c.id} value={c.id}>{c.name} · {money(grpCredit(s,c).available)} left</option>)}</select></label>}
      {kind==='udhaar'&&<label>Credit guest<select value={ref} onChange={e=>setRef(e.target.value)} required><option value="">Choose a guest…</option>{udhaar.map(g=><option key={g.id} value={g.id}>{g.name}</option>)}</select></label>}
      <fieldset><legend>Route to this folio</legend>{SF_ROUTE_CATEGORIES.map(c=><label key={c} className="checkbox-line"><input type="checkbox" checked={routes.includes(c)} disabled={!free.includes(c)} onChange={()=>setRoutes(toggle(routes,c))}/> {SF_ROUTE_LABEL[c]}{free.includes(c)?'':' (already on another folio)'}</label>)}</fieldset>
      <label>Reason (optional)<input value={reason} maxLength={200} onChange={e=>setReason(e.target.value)}/></label>
      <div className="info-note"><Info size={16}/>A company folio is bound by the company’s credit limit and is not paid in cash. Folio A stays with the guest.</div>
      <div className="modal-actions"><button type="button" className="btn" onClick={()=>booking(b.id)}>Back to folio</button><button className="btn primary" disabled={busy}><SplitIcon size={16}/> Open folio</button></div></form>
      :<div className="modal-actions"><span className="muted">A stay has at most four folios.</span><button type="button" className="btn" onClick={()=>booking(b.id)}>Back to folio</button></div>}</>;
}

/** Check-in routing editor: the current split and the entry to change it (the stay is Confirmed or In house). Nothing for a stay without a split. */
export function SfRoutingEditor({b}:{b:Booking}){
  const {act,open}=useHcc();
  if(!['Confirmed','In house'].includes(b.status))return null;
  const rows=sfIsSplit(b)?sfFolios(b):[];
  return <div className="info-note sf-routing"><SplitIcon size={16}/><div><strong>Bill routing</strong>
    {rows.length?<ul>{rows.map(f=><li key={f.id}>{sfFolioLabel(f,b.guest)} — {f.routes.length?f.routes.join(' + '):'nothing new'}</li>)}</ul>:<p>One folio: the guest pays everything. Split the bill to send room or extras to a company or another payer.</p>}
    {act('billing',()=>open({type:'sf-split',id:b.id}),rows.length?'Change routing':'Split bill…','btn small-btn')}</div></div>;
}

/** The one check-out sheet of a split stay: every folio's balance and how it is settled, the stay total, per-folio actions, blockers. */
export function SfCheckoutSheet({b,mobile}:{b:Booking;mobile?:boolean}){
  const {s,run,open,busy,permission}=useHcc();
  if(!sfIsSplit(b))return null;
  const sh=sfSettleSheet(s,b);
  return <section className={mobile?'mm-card sf-sheet':'sf-sheet'} aria-label="Settle every folio">
    <div className="section-title compact"><h3>Settle every folio</h3><span className="muted">One check-out settles all {sh.rows.length}</span></div>
    <div className="summary-list">{sh.rows.map(r=><div key={r.id} className="sf-row"><span><strong>{r.label}</strong><small>{r.note}</small></span>
      <strong className="number">{money(r.balance)}</strong>
      <span className="inline">{r.status==='pay'&&permission('payments')&&<button type="button" className={mobile?'btn':'btn small-btn'} disabled={busy} onClick={()=>open(sfOpen(b,'payment',r.id))}>Record payment</button>}
        {r.status==='pay'&&r.kind==='company'&&permission('checkout')&&<button type="button" className={mobile?'btn':'btn small-btn'} disabled={busy} onClick={()=>void run({type:'COMPANY_CREDIT_REQUEST',id:b.id,folio:r.id,key:`cc:${b.id}:${r.id}:${b.version}`},'Company credit requested — awaiting approval')}>Request company credit</button>}
        {r.status==='pay'&&r.kind==='udhaar'&&permission('checkout')&&<button type="button" className={mobile?'btn':'btn small-btn'} disabled={busy} onClick={()=>void run({type:'GUEST_CREDIT_REQUEST',id:b.id,folio:r.id,key:`gc:${b.id}:${r.id}:${b.version}`},'Guest credit requested — awaiting approval')}>Place on credit account</button>}
        {r.status==='settled'&&<Pill tone="green"><Check size={11}/> Settled</Pill>}</span></div>)}
      <div><span>Charges including GST (all folios)</span><strong>{money(sh.total)}</strong></div>
      <div><span>Net payments (all folios)</span><strong>{money(sh.paid)}</strong></div>
      <div className="sf-total"><span>Outstanding (sum of folios)</span><strong>{money(sh.balance)}</strong></div>
      <div><span>Deposit to return</span><strong>{money(sh.deposit)}</strong></div></div>
    {sh.blockers.length>0&&<div className="info-note folio-blockers" role="status"><AlertTriangle size={17}/><div><strong>Checkout is blocked until:</strong><ul>{sh.blockers.map(x=><li key={x}>{x}</li>)}</ul></div></div>}
  </section>;
}

/** After check-out: the invoice number of each folio (a ₹0 folio has none). */
export function SfInvoiceList({b}:{b:Booking}){
  if(!sfIsSplit(b))return null;
  const rows=sfInvoiceRows(b);
  return <div className="summary-list sf-invoices" aria-label="Invoices per folio">{rows.map(r=><div key={r.folio}><span>{r.label}</span><strong>{r.no} · {money(r.total)}</strong></div>)}</div>;
}
