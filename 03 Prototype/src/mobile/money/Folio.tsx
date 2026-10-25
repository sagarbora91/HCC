// M09 Folio (phone): folio list page + folio sheet (balance hero, Charges · Receipts · Requests, sticky footer) + "More" sheet.
import {usePaged,ShowMore,PHONE_PAGE} from '../../components/ShowMore';
import React,{useState} from 'react';
import {Receipt,Plus,MoreHorizontal,RotateCcw,Printer,LogOut,Percent,CalendarRange,XCircle,FileMinus,PencilLine,Building2} from 'lucide-react';
import {money,totals,lineTax,resStatus,nights,folioNights,folioApprovalStatus,folioKind,folioApprovalsFor,folioRefundable,folioRefunded,folioLineReversed,folioLineCorrectable,folioReceiptLabel,FOLIO_APPROVAL_KINDS,plural,rbacCanViewFolios,rbacCanDiscount,rbacCanRequestCorrection} from '../../services/mock';
import {useHcc} from '../../app/context';
import {ListCard,KpiCard,ChipFilter} from '../../app/mobile/components';
import {Pill,Empty,shortDate} from '../../components/ui';
import {FolioApprovalRow} from '../../features/folio/modals';
import {bookingActionSlots} from '../../app/registry';
import {mmBalanceHero} from './logic';

const FILTERS=['All','In house','Due out','Balance due','Departed','Confirmed'] as const;
type F=typeof FILTERS[number]|'Requests';
const statusTone=(st:string)=>st==='In house'?'green':st==='Confirmed'?'blue':st==='Held'?'orange':'neutral';

/** Phone page 'folios': search + status chips, folio cards with right-aligned balances, folio requests. */
export function MmFolios(){
  const {s,booking,search,setSearch}=useHcc();
  const [f,setF]=useState<F>('All');
  const q=search.toLowerCase();
  const requests=s.approvals.filter(a=>FOLIO_APPROVAL_KINDS.includes(folioKind(a))).filter(a=>{const st=folioApprovalStatus(s,a);return st==='Pending'||(st==='Approved'&&a.kind==='refund')||(st==='Expired'&&a.status==='Pending');}).slice().reverse();
  const list=s.bookings.filter(b=>{
    const t=totals(b);
    const ok=f==='All'||(f==='Due out'?b.status==='In house'&&b.departure<=s.date:f==='Balance due'?t.balance>0:b.status===f);
    return ok&&`${b.guest} ${b.id} ${b.room} ${b.company?.name??''} ${b.invoiceNo??''}`.toLowerCase().includes(q);
  });
  const pg=usePaged(list,PHONE_PAGE);
  const outstanding=s.bookings.reduce((a,b)=>a+Math.max(0,totals(b).balance),0);
  const count=(x:F)=>x==='Requests'?requests.length:s.bookings.filter(b=>{const t=totals(b);return x==='All'||(x==='Due out'?b.status==='In house'&&b.departure<=s.date:x==='Balance due'?t.balance>0:b.status===x);}).length;
  return <div className="mm-page">
    <div className="mm-kpis"><KpiCard label="Outstanding" value={money(outstanding)} sub="All open balances" onClick={()=>setF('Balance due')}/><KpiCard label="Requests" value={requests.length} sub="Folio approvals / refunds" onClick={()=>setF('Requests')}/></div>
    <label className="mm-search"><span className="sr-only">Search folios</span><input type="search" enterKeyHint="search" value={search} onChange={e=>setSearch(e.target.value)} placeholder="Search guest, room, company, invoice"/></label>
    <ChipFilter label="Folio filter" value={f} onChange={setF} options={[...FILTERS,'Requests' as const].map(x=>({id:x as F,label:x,count:count(x as F)}))}/>
    {f==='Requests'?(!requests.length?<Empty title="Nothing waiting" body="Requests that need a decision or an approved refund awaiting execution appear here."/>
      :<div className="mm-requests">{requests.map(a=><FolioApprovalRow key={a.id} a={a} showGuest/>)}</div>)
    :<>{pg.shown.map(b=>{const t=totals(b),n=folioNights(b);return <ListCard key={b.id} onClick={()=>booking(b.id)}
        title={b.guest} sub={`Room ${b.room} · ${b.id}${b.company?' · '+b.company.name:''}`}
        meta={<><Pill tone={statusTone(b.status)}>{b.status}</Pill> {shortDate(b.arrival)}–{shortDate(b.departure)} · nights {n.posted}/{n.agreed}</>}
        right={<span className="mm-amt"><strong className="number">{money(Math.abs(t.balance))}</strong><small className={t.balance>0?'mm-due':t.balance<0?'mm-credit':'mm-ok'}>{t.balance>0?'Due':t.balance<0?'Credit':'Settled'}</small></span>}/>;})}
      {!list.length&&<Empty title="No folios match" body="Try another filter or search."/>}<ShowMore p={pg}/></>}
    <p className="footnote">All names and records are fictional. GST illustrative — CA to confirm.</p>
  </div>;
}

/** Phone override of the 'booking' dialog: the folio sheet (M09). */
export function MmFolioSheet({id}:{id?:string}){
  const {s,uid,permission,open,busy}=useHcc();
  const [tab,setTab]=useState<'charges'|'receipts'|'requests'>('charges');
  const b=s.bookings.find(x=>x.id===id);
  if(!b)return <Empty title="This record is unavailable" body="The booking may have been removed when a scenario was reloaded."/>;
  if(!rbacCanViewFolios(s,uid))return <Empty title="Access denied" body="Your role cannot open guest folios."/>;
  const h=mmBalanceHero(b),n=folioNights(b),st=resStatus(s,b),active=['Confirmed','In house'].includes(b.status);
  const approvals=folioApprovalsFor(s,b.id);
  const canPay=permission('payments')&&!['Held','Expired','Cancelled','No show'].includes(st);
  const canCharge=active&&permission('billing');
  const userName=(uid?:string)=>s.users.find(u=>u.id===uid)?.name.split(' · ')[0]??uid??'—';
  return <>
    <div className="modal-heading mm-folio-head"><div className="eyebrow">{b.id} · ROOM {b.room}{b.invoiceNo?` · ${b.invoiceNo}`:''}</div><h2>{b.guest}</h2>
      <p className="mm-meta"><Pill tone={statusTone(st)}>{st}</Pill> {shortDate(b.arrival)} – {shortDate(b.departure)} · {plural(nights(b.arrival,b.departure),'night')} · {plural(b.people,'guest')}</p>
      <p className={'mm-meta '+(n.posted<n.agreed?'mm-warn-text':'')}>Nights posted {n.posted} of {n.agreed} agreed</p>
      {b.company&&<p className="mm-meta"><Building2 size={14}/> {b.company.name}</p>}</div>
    <section className={'mm-hero mm-hero-'+h.state} aria-label="Folio balance">
      <div className="mm-hero-main"><span>{h.label}</span><strong className="number">{money(Math.abs(h.balance))}</strong></div>
      <div className="mm-hero-sub"><div><span>Deposit held</span><strong className="number">{money(h.deposit)}</strong></div><div><span>Charges + tax</span><strong className="number">{money(h.total)}</strong></div><div><span>Net paid</span><strong className="number">{money(h.paid)}</strong></div></div>
    </section>
    <div className="mm-seg" role="tablist" aria-label="Folio sections">
      {([['charges','Charges',b.lines.length],['receipts','Receipts',b.receipts.length],['requests','Requests',approvals.length]] as const).map(([k,l,c])=>
        <button key={k} role="tab" type="button" aria-selected={tab===k} className={tab===k?'on':''} onClick={()=>setTab(k)}>{l}<b>{c}</b></button>)}
    </div>
    <div role="tabpanel" className="mm-tabpanel">
      {tab==='charges'&&(b.lines.length?b.lines.map(l=>{const reversed=folioLineReversed(b,l.id),tax=lineTax(l);
        return <div key={l.id} className={'mm-row'+(reversed||l.reversalOf?' mm-muted':'')}>
          <div className="mm-row-main"><strong>{l.label}</strong><small>{shortDate(l.date)} · {l.kind}{l.reversalOf?' · reversal':''}{reversed?' · reversed':''}{s.closedDates.includes(l.date)?' · closed date':''}</small>
            {active&&rbacCanRequestCorrection(s,uid)&&!reversed&&folioLineCorrectable(s,b,l)&&<button type="button" className="text-btn mm-inline-btn" disabled={busy} onClick={()=>open({type:'correction',id:b.id,room:l.id})}><PencilLine size={14}/> Request correction</button>}</div>
          <div className="mm-row-amt"><strong className="number">{money(l.amount)}</strong><small className="number">tax {money(tax)}</small></div></div>;})
        :<Empty title="No charges yet" body="Room nights are posted by the nightly audit."/>)}
      {tab==='receipts'&&(b.receipts.length?b.receipts.map(r=>{const refundable=r.kind==='payment'?folioRefundable(s,b,r.id):0,refunded=r.kind==='payment'?folioRefunded(b,r.id):0,out=['refund','deposit-return'].includes(r.kind);
        return <div key={r.id} className="mm-row">
          <div className="mm-row-main"><strong>{folioReceiptLabel[r.kind]??r.kind} · {r.method}</strong><small>{shortDate(r.date)} · {r.reference}{r.tenderGroup?' · split '+r.tenderGroup:''}{refunded?' · refunded '+money(refunded):''} · {userName(r.actor)}</small><small>{r.id} · not verified</small>
            {refundable>0&&permission('payments')&&<button type="button" className="text-btn mm-inline-btn" disabled={busy} onClick={()=>open({type:'refund',id:b.id,room:r.id})}><RotateCcw size={14}/> Request refund</button>}</div>
          <div className="mm-row-amt"><strong className={'number'+(out?' mm-out':'')}>{out?'−':''}{money(r.amount)}</strong></div></div>;})
        :<Empty title="No receipts recorded" body="Payments, deposits and returns appear here."/>)}
      {tab==='requests'&&(approvals.length?<div className="mm-requests">{approvals.map(a=><FolioApprovalRow key={a.id} a={a}/>)}</div>
        :<Empty title="No folio requests" body="Discounts above the limit, refunds and corrections appear here. No one approves their own request."/>)}
    </div>
    <div className="modal-actions mm-foot">
      {canPay&&<button className="btn primary mm-foot-main" disabled={busy} onClick={()=>open({type:'payment',id:b.id})}><Receipt size={18}/>Record payment</button>}
      {canCharge&&<button className="btn" disabled={busy} onClick={()=>open({type:'charge',id:b.id})}><Plus size={18}/>Add charge</button>}
      <button className="btn" onClick={()=>open({type:'mm-folio-more',id:b.id})} aria-label="More folio actions"><MoreHorizontal size={18}/>More</button>
    </div></>;
}

/** 'mm-folio-more': discount, refund, correction, checkout, invoice, stay actions (+ other streams' booking actions). */
export function MmFolioMore({id}:{id?:string}){
  const {s,uid,permission,open,booking}=useHcc();
  const b=s.bookings.find(x=>x.id===id);
  if(!b)return <Empty title="This record is unavailable"/>;
  const active=['Confirmed','In house'].includes(b.status);
  const firstRefundable=b.receipts.find(r=>r.kind==='payment'&&folioRefundable(s,b,r.id)>0);
  const firstCorrectable=b.lines.find(l=>folioLineCorrectable(s,b,l));
  const item=(show:boolean,icon:React.ReactNode,title:string,sub:string,fn:()=>void)=>show?<ListCard key={title} title={<span className="m-lc-title">{icon}{title}</span>} sub={sub} onClick={fn}/>:null;
  return <><div className="modal-heading"><div className="eyebrow">{b.id} · ROOM {b.room}</div><h2>More actions</h2><p>{b.guest} · balance {money(totals(b).balance)}</p></div>
    <div className="mm-more-list">
      {item(active&&rbacCanDiscount(s,uid),<Percent size={20}/>,'Discount','Above the limit, another user approves',()=>open({type:'discount',id:b.id}))}
      {item(!!firstRefundable&&permission('payments'),<RotateCcw size={20}/>,'Request refund',`Against ${firstRefundable?.id} (pick another on the Receipts tab)`,()=>open({type:'refund',id:b.id,room:firstRefundable!.id}))}
      {item(active&&!!firstCorrectable&&rbacCanRequestCorrection(s,uid),<PencilLine size={20}/>,'Request correction',`Reverse “${firstCorrectable?.label}” (pick another on the Charges tab)`,()=>open({type:'correction',id:b.id,room:firstCorrectable!.id}))}
      {item(b.status==='In house'&&permission('checkout'),<LogOut size={20}/>,'Checkout','Checklist, then confirm',()=>open({type:'checkout',id:b.id}))}
      {item(active&&permission('reservations'),<CalendarRange size={20}/>,b.status==='In house'?'Move / extend stay':'Change dates / room','Shows conflicts before saving',()=>open({type:b.status==='In house'?'stay-move':'move',id:b.id}))}
      {item(b.status==='Confirmed'&&permission('reservations'),<XCircle size={20}/>,'Cancel / no-show','Refund advances first',()=>open({type:'cancel',id:b.id}))}
      {item(b.status==='Departed'&&rbacCanRequestCorrection(s,uid),<FileMinus size={20}/>,'Request credit note','Departed stays only',()=>open({type:'credit-note',id:b.id}))}
      {item(true,<Printer size={20}/>,b.invoiceNo?'Sample invoice '+b.invoiceNo:'Folio preview','View and print (wireless printer)',()=>open({type:'invoice',id:b.id}))}
    </div>
    {bookingActionSlots.length>0&&<div className="mm-slot-actions">{bookingActionSlots.map((Slot,i)=><Slot key={i} b={b}/>)}</div>}
    <div className="modal-actions"><button className="btn" onClick={()=>booking(b.id)}>Back to folio</button></div></>;
}
