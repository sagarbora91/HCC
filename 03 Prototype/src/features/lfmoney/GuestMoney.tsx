// LF-UI · booking-dialog actions, agreed-rate + credit sections of the guest profile, and the Cashier "Credit guests" list.
import React from 'react';
import {useHcc} from '../../app/context';
import {money,udGuestOutstanding,gcrUsed,blcCorrections,strChangesFor,type Booking} from '../../services/mock';
import {Pill} from '../../components/ui';

export function LfBookingActions({b}:{b:Booking}){
  const {s,permission,open}=useHcc();
  const rate=b.status==='In house'&&permission('billing'),fix=b.status==='Departed'&&!!b.invoiceNo&&permission('billing');
  if(!rate&&!fix)return null;
  const corr=blcCorrections(s,b.id);const chg=strChangesFor(s,b.id);
  return <>{rate&&<button type="button" className="btn" onClick={()=>open({type:'stay-rate',id:b.id})}>Rate / guests{chg.length?` (${chg.length})`:''}</button>}
    {fix&&<button type="button" className="btn" onClick={()=>open({type:'bill-correct',id:b.id})}>Correct this bill{corr.length?` (${corr.length})`:''}</button>}</>;
}

/** Owner-visible money fields of a guest profile: agreed rates (B1) and the credit account (B2). */
export function GuestMoneySections({guestId}:{guestId:string}){
  const {s,uid,run,busy,open}=useHcc();
  const g=(s.guests??[]).find(x=>x.id===guestId);const owner=s.users.find(u=>u.id===uid)?.role==='Owner';
  if(!g||(!owner&&!g.agreedRates&&!g.creditAccount))return null;
  return <section aria-label="Agreed rate and credit">
    <h3 className="gs-sub">Agreed rate</h3>
    {g.agreedRates?.length?<ul className="gs-party-list">{g.agreedRates.map(r=><li key={r.type}><strong>{r.type} · {money(r.rate)}</strong><span>{r.validTo?'until '+r.validTo:'no end date'}{r.note?' · '+r.note:''}</span>
      {owner&&<button type="button" className="text-btn" disabled={busy} onClick={()=>void run({type:'GUEST_RATE_CLEAR',guestId:g.id,roomType:r.type},'Agreed rate cleared.')}>Clear</button>}</li>)}</ul>:<p className="muted">No agreed rate.</p>}
    {owner&&<button type="button" className="btn" onClick={()=>open({type:'agreed-rate',id:g.id})}>Set agreed rate</button>}
    <h3 className="gs-sub">Credit</h3>
    {g.creditAccount?<p>{g.creditAccount.active?<Pill tone="green">Active</Pill>:<Pill tone="orange">Inactive</Pill>} Limit {money(g.creditAccount.limit)} · used {money(gcrUsed(s,g))}</p>:<p className="muted">No credit account.</p>}
    {owner&&<button type="button" className="btn" onClick={()=>open({type:'credit-account',id:g.id})}>{g.creditAccount?'Change credit account':'Open credit account'}</button>}
  </section>;
}

export function CreditGuests(){
  const {s}=useHcc();const rows=udGuestOutstanding(s);
  if(!rows.length)return <p className="muted padded">No guest has a credit account yet. The Owner opens one on the guest profile.</p>;
  return <div className="table-wrap"><table><thead><tr><th>Guest</th><th>Limit</th><th>Outstanding</th><th>0–30</th><th>31–60</th><th>61–90</th><th>90+</th></tr></thead><tbody>
    {rows.map(r=><tr key={r.guestId}><td><strong>{r.name}</strong><small>{r.active?'':'inactive · '}{r.party}</small></td><td>{money(r.limit)}</td><td>{money(r.open)}</td>
      {(['0–30','31–60','61–90','90+'] as const).map(k=><td key={k}>{r.buckets[k]?money(r.buckets[k]):'—'}</td>)}</tr>)}</tbody></table></div>;
}
