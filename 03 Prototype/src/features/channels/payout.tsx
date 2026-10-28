// ACC-2 · acc2-ui — OTA payout entry from a channel statement (contract §4, DEC-150): gross / commission / commission GST / TCS / net with the equality check
// (gross = net + commission + GST + TCS), the bookings it settles, the bank it landed in. Screens only: the posting (Dr bank net · Dr 5070 commission + GST ·
// Dr 1340 TCS · Cr 1115/<channel> gross) and the guard "gross ≤ the channel's open receivable" belong to the flows lane (CHN_PAYOUT).
import React,{useState} from 'react';
import {Plus,CheckCircle2,AlertTriangle} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,chnSlice,chnChannelName} from '../../services/mock';
import {ListCard} from '../../app/mobile/components';
import {Empty,dateLabel,shortDate} from '../../components/ui';
import {chnPayouts,chnOpenReceivable,type ChnPayoutCmd} from '../accounting/_contract-stubs-2';
import {useAccPerms,accUserName} from '../accounting/parts';
import {toPaise,fromPaise,bankOptions} from '../accounting/shared';

/** Payouts tab of the OTA channels page (desktop table, phone cards). */
export function PayoutsTab({phone}:{phone?:boolean}){
  const {s,open}=useHcc();const {canVendorPay}=useAccPerms();const rows=chnPayouts(s);
  const add=canVendorPay?<button type="button" className={'btn primary'+(phone?' full':'')} onClick={()=>open({type:'chn-payout'})}><Plus size={16}/> Record a payout</button>:<p className="muted">Payouts are recorded by the Owner, Manager or Accountant.</p>;
  if(phone)return <>{add}
    {rows.map(p=><ListCard key={p.id} title={`${chnChannelName(s,p.channel)} · ${money(p.net)} received`} sub={`Statement ${p.statementRef} · ${shortDate(p.date)}`}
      meta={`Gross ${money(p.gross)} · commission ${money(p.commission+p.commissionGst)} · TCS ${money(p.tcs)}`} right={<span className="pill green">Posted</span>}/>)}
    {!rows.length&&<Empty title="No payouts yet" body="OTAs pay net of commission. Record each payment from the channel’s statement."/>}</>;
  return <div className="padded"><div className="chn-toolbar"><p className="chn-muted">OTAs pay the hotel net of commission, its GST and TCS, with a statement. Each payout settles the channel’s receivable.</p>{add}</div>
    {rows.length?<div className="table-wrap"><table className="chn-table"><thead><tr><th>Payout</th><th>Channel · statement</th><th className="number">Gross</th><th className="number">Commission</th><th className="number">GST on it</th><th className="number">TCS</th><th className="number">Net received</th><th>Bookings</th></tr></thead><tbody>
      {rows.map(p=><tr key={p.id}><td><strong>{p.id}</strong><small>{dateLabel(p.date)} · {accUserName(s,p.by)}</small></td><td>{chnChannelName(s,p.channel)}<small>{p.statementRef}</small></td><td className="number">{money(p.gross)}</td><td className="number">{money(p.commission)}</td>
        <td className="number">{money(p.commissionGst)}</td><td className="number">{money(p.tcs)}</td><td className="number"><strong>{money(p.net)}</strong></td><td>{p.bookings.length?p.bookings.join(', '):'—'}</td></tr>)}</tbody></table></div>
      :<Empty title="No payouts yet" body="OTAs pay net of commission. Record each payment from the channel’s statement."/>}</div>;
}

/** 'chn-payout' · id = channel id (optional). */
export function PayoutModal({id}:{id?:string}){
  const {s,run,closeModal,busy,modal}=useHcc();const {canVendorPay}=useAccPerms();const st=chnSlice(s);
  const connected=st.channels.filter(c=>c.status==='Connected'||c.id===id);const channels=connected.length?connected:st.channels;
  const [channel,setChannel]=useState(id&&st.channels.some(c=>c.id===id)?id:channels[0]?.id??'');
  const banks=bankOptions(s);const [bank,setBank]=useState(banks[0]?.id.split('/')[1]??'');
  const [date,setDate]=useState(s.date);const [ref,setRef]=useState('');
  const [gross,setGross]=useState('');const [com,setCom]=useState('');const [gst,setGst]=useState('');const [tcs,setTcs]=useState('');const [net,setNet]=useState('');
  const [picked,setPicked]=useState<string[]>([]);
  const ch=st.channels.find(c=>c.id===channel);
  const taken=new Set((st.payouts??[]).flatMap(p=>p.bookings));
  const bookings=s.bookings.filter(b=>ch&&b.otaChannel===ch.name&&['In house','Departed'].includes(b.status)&&!taken.has(b.id));
  const G=toPaise(gross),C=toPaise(com),T=toPaise(gst),X=toPaise(tcs),N=toPaise(net),sum=N+C+T+X,open$=channel?chnOpenReceivable(s,channel):0;
  const equal=G>0&&G===sum,over=G>open$;
  const problem=!canVendorPay?'Payouts are recorded by the Owner, Manager or Accountant.':!channel?'Choose the channel.':G<=0?'Enter the gross amount from the statement.':!equal?`Gross ${money(G)} must equal net + commission + GST + TCS (${money(sum)}).`
    :over?`Gross ${money(G)} is more than the ${money(open$)} the channel owes the hotel.`:ref.trim().length<2?'Enter the statement reference.':!bank?'Choose the bank the money landed in.':'';
  const field=(label:string,v:string,set:(x:string)=>void,hint?:string)=><label>{label}<input inputMode="decimal" value={v} onChange={e=>set(e.target.value)} placeholder="0.00"/>{hint&&<small className="muted">{hint}</small>}</label>;
  return <form className="form chn-payout" onSubmit={e=>{e.preventDefault();if(problem)return;void run({type:'CHN_PAYOUT',channel,date,statementRef:ref.trim(),gross:G,commission:C,commissionGst:T,tcs:X,net:N,bank,bookings:picked,key:modal?.key} satisfies ChnPayoutCmd,'Payout recorded',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">OTA PAYOUT · FROM THE STATEMENT</p><h2>Record a payout</h2><p>Dr bank (net) · Dr OTA commission (commission + its GST) · Dr GST cash ledger (TCS) · Cr {ch?.name??'the channel'} receivable (gross).</p></div>
    <div className="form-row"><label>Channel<select value={channel} onChange={e=>{setChannel(e.target.value);setPicked([]);}}>{channels.map(c=><option key={c.id} value={c.id}>{c.name}</option>)}</select></label>
      <label>Statement date<input type="date" value={date} max={s.date} onChange={e=>e.target.value&&setDate(e.target.value)}/></label></div>
    <label>Statement reference<input value={ref} maxLength={60} onChange={e=>setRef(e.target.value)} placeholder="e.g. payout / statement number (fictional)"/></label>
    <div className="form-row">{field('Gross bookings (₹)',gross,setGross,'Total of the bookings on the statement')}{field('Commission (₹)',com,setCom)}</div>
    <div className="form-row">{field('GST on commission (₹)',gst,setGst)}{field('TCS withheld (₹)',tcs,setTcs,'Tax collected at source by the OTA')}</div>
    {field('Net paid to the hotel (₹)',net,setNet)}
    <div className="chn-eq" role="status">{G>0&&(equal?<span className="pill green"><CheckCircle2 size={12}/>Gross = net + commission + GST + TCS</span>:<span className="pill acc-sev-error"><AlertTriangle size={12}/>Off by {money(Math.abs(G-sum))} {G>sum?'(gross is more)':'(deductions + net are more)'}</span>)}
      {G>0&&!equal&&<button type="button" className="text-btn" onClick={()=>setNet(fromPaise(Math.max(G-C-T-X,0)))}>Set net = gross − deductions</button>}
      <small className="muted">The channel owes the hotel {money(open$)} in all.</small></div>
    <label>Bank it landed in<select value={bank} onChange={e=>setBank(e.target.value)}>{banks.map(b=><option key={b.id} value={b.id.split('/')[1]}>{b.label}</option>)}</select></label>
    {bookings.length>0&&<fieldset className="chn-bk"><legend>Bookings this statement settles (optional)</legend>
      {bookings.slice(0,40).map(b=><label key={b.id} className="checkbox-line"><input type="checkbox" checked={picked.includes(b.id)} onChange={e=>setPicked(e.target.checked?[...picked,b.id]:picked.filter(x=>x!==b.id))}/> {b.guest} · {b.id} · {shortDate(b.arrival)}–{shortDate(b.departure)}</label>)}</fieldset>}
    {problem&&(G>0||!canVendorPay)&&<p className="f-why" role="alert">{problem}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!!problem}>Record payout</button></div></form>;
}
