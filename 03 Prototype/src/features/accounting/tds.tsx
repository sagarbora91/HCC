// ACC-2 · acc2-ui — Accounting › TDS (contract §5) and Accounting › Assets (contract §3.4), plus the Add loan dialog of the chart screen (§2).
// TDS: the dated rate table, the deductee report with CSV, TDS deducted by companies (Form 168 reconciliation), the remittance form.
// Assets: the fixed-asset register with CSV. Screens only: the rates, the deduction, the register rows and the postings are other lanes'.
import React,{useMemo,useState} from 'react';
import {Download,Info,Plus} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,rbacCanExportVouchers,cmpSlice} from '../../services/mock';
import {ListCard} from '../../app/mobile/components';
import {Empty,dateLabel} from '../../components/ui';
import {downloadCsv} from '../reports/Reports';
import {tdsRateRows,tdsDeducteeReport,tdsDeducteeCsv,tdsReceivableReport,accAssetsList,accAssetsCsv,accLoans,type AccTdsRemit,type AccLoanAdd} from './_contract-stubs-2';
import {useAccPerms} from './parts';
import {monthChoices,monthLabel,toPaise,fromPaise,bankMethods,fyStart} from './shared';

const pct=(bps:number)=>(bps/100).toLocaleString('en-IN',{maximumFractionDigits:2})+' %';
function Csv({name,csv,disabled}:{name:string;csv:()=>string;disabled?:boolean}){
  const {s,uid,toast}=useHcc();if(!rbacCanExportVouchers(s,uid))return null;
  return <button type="button" className="btn" disabled={disabled} onClick={()=>{downloadCsv(name,csv());toast('CSV downloaded');}}><Download size={16}/> Export CSV</button>;
}

export function TdsView({phone}:{phone?:boolean}){
  const {s,open}=useHcc();const {canTax}=useAccPerms();
  const months=useMemo(()=>monthChoices(s.date),[s.date]);const fy='FY'+fyStart(s.date).slice(0,4);
  const [period,setPeriod]=useState(months[0]);
  const prof=cmpSlice(s).profile,deducts=prof?.hotelDeductsTds!==false,other=prof?.tdsDeductorType==='other';
  const rates=tdsRateRows(s,s.date),rows=useMemo(()=>tdsDeducteeReport(s,period),[s,period]),recv=tdsReceivableReport(s,fy);
  const tot=rows.reduce((a,r)=>({taxable:a.taxable+r.taxable,tds:a.tds+r.tds,remitted:a.remitted+r.remitted}),{taxable:0,tds:0,remitted:0});
  const label=(p:string)=>p.length===7?monthLabel(p):p;
  const picker=<label className="acc-date">Period<select value={period} onChange={e=>setPeriod(e.target.value)}><option value={fy}>Financial year {fy.slice(2)}–{String(+fy.slice(2)+1).slice(2)}</option>{months.map(m=><option key={m} value={m}>{monthLabel(m)}</option>)}</select></label>;
  const head=<>
    {!deducts&&<div className="info-note f-warn" role="status"><Info size={16}/>The firm profile says the hotel does <strong>not</strong> deduct TDS from vendors; the Owner can switch this on in Compliance › Firm profile.</div>}
    <p className="muted">Deductor type: <strong>{other?'Other than individual / HUF':'Individual / HUF'}</strong> (sets the rate column). Rates are dated, Owner-editable in Master configuration › Money and tax, and marked <em>CA to confirm</em>.</p></>;
  const remitBtn=canTax?<button type="button" className="btn primary" onClick={()=>open({type:'acc-tds-remit',id:period.length===7?period:s.date.slice(0,7)})}>Remit TDS…</button>:null;
  const rateList=phone?rates.map(r=><ListCard key={r.section} title={`${r.label}`} sub={`Old section ${r.oldSection} · threshold ${r.threshold}`} meta={`From ${dateLabel(r.from)} · ${r.source}`}
      right={<><strong className="number">{pct(other?r.rateOther:r.rateIndividual)}</strong><br/><span className="pill orange">CA to confirm</span></>}/>)
    :<div className="table-wrap"><table className="acc-table"><thead><tr><th>Section</th><th>Threshold</th><th className="acc-num">Individual / HUF</th><th className="acc-num">Other</th><th>From</th><th>Source</th></tr></thead><tbody>
      {rates.map(r=><tr key={r.section}><td><strong>{r.label}</strong><small>Old section {r.oldSection}</small></td><td>{r.threshold}</td><td className={'acc-num number'+(!other?' acc-hl':'')}>{pct(r.rateIndividual)}</td><td className={'acc-num number'+(other?' acc-hl':'')}>{pct(r.rateOther)}</td>
        <td>{dateLabel(r.from)}</td><td><small>{r.source}</small>{r.caConfirm&&<span className="pill orange">CA to confirm</span>}</td></tr>)}</tbody></table></div>;
  const deductee=phone?<>{rows.map(r=><ListCard key={r.vendor+r.section} title={r.vendor||'—'} sub={`${r.pan||'PAN not on file'} · section ${r.section}`} meta={`Taxable ${money(r.taxable)} · remitted ${money(r.remitted)}`} right={<strong className="number">{money(r.tds)}</strong>}/>)}
      {!rows.length&&<Empty title="No TDS deducted" body={`Nothing was deducted from vendors in ${label(period)}.`}/>}</>
    :rows.length?<div className="table-wrap"><table className="acc-table"><thead><tr><th>Vendor</th><th>PAN</th><th>Section</th><th className="acc-num">Taxable value</th><th className="acc-num">TDS deducted</th><th className="acc-num">Remitted</th></tr></thead><tbody>
      {rows.map(r=><tr key={r.vendor+r.section}><td>{r.vendor||'—'}</td><td className="number">{r.pan||'—'}</td><td>{r.section}</td><td className="acc-num number">{money(r.taxable)}</td><td className="acc-num number"><strong>{money(r.tds)}</strong></td><td className="acc-num number">{money(r.remitted)}</td></tr>)}
      <tr className="acc-total"><td><strong>Total</strong></td><td/><td/><td className="acc-num number">{money(tot.taxable)}</td><td className="acc-num number"><strong>{money(tot.tds)}</strong></td><td className="acc-num number">{money(tot.remitted)}</td></tr></tbody></table></div>
      :<Empty title="No TDS deducted" body={`Nothing was deducted from vendors in ${label(period)}.`}/>;
  const form168=recv.length>0&&<><h3 className="acc-h3">TDS deducted by companies (Form 168 reconciliation)</h3>
    {recv.map(r=><p key={r.company+r.month} className="muted">{r.company} · {r.month} · {money(r.tds)}</p>)}</>;
  return <div className={phone?undefined:'padded'}>
    {head}
    <h3 className="acc-h3">Rate table</h3>{rateList}
    <div className="acc-tds-bar"><h3 className="acc-h3">Deductee report</h3>{picker}{!phone&&<Csv name={`HCC-Demo-tds-deductees-${period}.csv`} csv={()=>tdsDeducteeCsv(rows,period)} disabled={!rows.length}/>}{!phone&&remitBtn}</div>
    {deductee}
    {tot.tds>tot.remitted&&<p className="muted">{money(tot.tds-tot.remitted)} deducted and not yet remitted (Dr TDS payable 2150 · Cr bank when you remit).</p>}
    {form168}
    {phone&&<div className="acc-tds-phone-actions"><Csv name={`HCC-Demo-tds-deductees-${period}.csv`} csv={()=>tdsDeducteeCsv(rows,period)} disabled={!rows.length}/>{remitBtn}</div>}
  </div>;
}

/** 'acc-tds-remit' · id = YYYY-MM: remit TDS (template tds-remittance: Dr 2150 · Cr bank). */
export function TdsRemitModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const {canTax}=useAccPerms();const month=id||s.date.slice(0,7);
  const rows=tdsDeducteeReport(s,month),sections=[...new Set(rows.map(r=>r.section).filter(Boolean))],methods=bankMethods(s);
  const [section,setSection]=useState(sections[0]??'');
  const due=rows.filter(r=>!section||r.section===section).reduce((a,r)=>a+r.tds-r.remitted,0);
  const [amount,setAmount]=useState(fromPaise(Math.max(due,0)));const [method,setMethod]=useState(methods[0]?.[0]??'');const [challan,setChallan]=useState('');const [date,setDate]=useState(s.date);
  const ok=toPaise(amount)>0&&!!method&&challan.trim().length>=3&&!!section;
  return <form className="form" onSubmit={e=>{e.preventDefault();if(ok)void run({type:'ACC_TDS_REMIT',month,section,amount:toPaise(amount),method,challan:challan.trim(),date,key:`acctds:${month}:${section}:${challan.trim()}`} satisfies AccTdsRemit,'TDS remittance recorded',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">TDS · {monthLabel(month).toUpperCase()}</p><h2>Remit TDS</h2><p>Dr TDS payable (2150) · Cr bank. Deducted this month and not yet remitted: {money(Math.max(due,0))}.</p></div>
    <label>Section<select value={section} onChange={e=>{setSection(e.target.value);setAmount(fromPaise(Math.max(rows.filter(r=>r.section===e.target.value).reduce((a,r)=>a+r.tds-r.remitted,0),0)));}}><option value="">Choose…</option>
      {(sections.length?sections:tdsRateRows(s,s.date).map(r=>r.section)).map(x=><option key={x} value={x}>{x}</option>)}</select></label>
    <label>Amount (₹)<input inputMode="decimal" value={amount} onChange={e=>setAmount(e.target.value)}/></label>
    <label>Paid from<select value={method} onChange={e=>setMethod(e.target.value)}>{methods.map(([v,l])=><option key={v} value={v}>{l}</option>)}</select></label>
    <label>Challan number<input value={challan} maxLength={40} onChange={e=>setChallan(e.target.value)} placeholder="e.g. BSR / challan serial (fictional)"/></label>
    <label>Date paid<input type="date" value={date} max={s.date} onChange={e=>e.target.value&&setDate(e.target.value)}/></label>
    {!canTax&&<p className="f-why">TDS remittances are entered by the Owner or the Accountant.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!canTax||!ok}>Record remittance</button></div></form>;
}

// ── Assets ──
export function AssetsView({phone}:{phone?:boolean}){
  const {s,open}=useHcc();const rows=accAssetsList(s);const cost=rows.reduce((a,r)=>a+r.cost,0);
  const note=<p className="muted">Created automatically when a capital purchase posts. HCC keeps the register only — depreciation is worked in Tally (no depreciation journal here).</p>;
  if(phone)return <>{note}
    {rows.map(a=><ListCard key={a.id} title={`${a.no} · ${a.name}`} sub={`${a.ledger} · ${a.vendor||'—'} · bought ${dateLabel(a.date)}`}
      meta={`${a.putToUse?'Put to use '+dateLabel(a.putToUse):'Not marked as put to use'}${a.disposed?` · disposed ${dateLabel(a.disposed.date)} for ${money(a.disposed.amount)}`:''}`} right={<strong className="number">{money(a.cost)}</strong>}
      onClick={()=>open({type:'exp-text',id:a.expense})}/>)}
    {!rows.length&&<Empty title="No fixed assets yet" body="A purchase in a capital category creates a register row."/>}
    <Csv name="HCC-Demo-fixed-assets.csv" csv={()=>accAssetsCsv(rows)} disabled={!rows.length}/></>;
  return <div className="padded">{note}
    <div className="inline acc-assets-bar"><strong>{rows.length} asset{rows.length===1?'':'s'} · cost {money(cost)}</strong><Csv name="HCC-Demo-fixed-assets.csv" csv={()=>accAssetsCsv(rows)} disabled={!rows.length}/></div>
    {rows.length?<div className="table-wrap"><table className="acc-table"><thead><tr><th>Asset no</th><th>Name</th><th>Ledger</th><th>Vendor</th><th>Bought</th><th className="acc-num">Cost</th><th className="acc-num">ITC taken</th><th>Put to use</th><th>Disposal</th></tr></thead><tbody>
      {rows.map(a=><tr key={a.id}><td className="number">{a.no}</td><td>{a.name}<small>{a.expense}</small></td><td>{a.ledger}</td><td>{a.vendor||'—'}</td><td>{dateLabel(a.date)}</td><td className="acc-num number">{money(a.cost)}</td><td className="acc-num number">{a.itc?money(a.itc):'—'}</td>
        <td>{a.putToUse?dateLabel(a.putToUse):'—'}</td><td>{a.disposed?`${dateLabel(a.disposed.date)} · ${money(a.disposed.amount)}`:'—'}</td></tr>)}</tbody></table></div>
      :<Empty title="No fixed assets yet" body="A purchase in a capital category creates a register row."/>}</div>;
}

// ── Add loan (chart of accounts) ──
/** 'acc-loan' (Owner): adds a loan party row under the 2400 control ledger (`2400/<LN-code>`). */
export function LoanModal(){
  const {s,run,closeModal,busy}=useHcc();const {canChart}=useAccPerms();const [name,setName]=useState('');const [lender,setLender]=useState('');const [last4,setLast4]=useState('');const [note,setNote]=useState('');
  const loans=accLoans(s),ok=canChart&&name.trim().length>=2&&lender.trim().length>=2&&(!last4||/^\d{4}$/.test(last4));
  return <form className="form" onSubmit={e=>{e.preventDefault();if(ok)void run({type:'ACC_LOAN_ADD',name:name.trim(),lender:lender.trim(),...(last4?{accountLast4:last4}:{}),...(note.trim()?{note:note.trim()}:{}),key:'accloan:'+name.trim()} satisfies AccLoanAdd,'Loan added',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">CHART OF ACCOUNTS</p><h2>Add loan</h2><p>A loan is a party row under Bank loans (2400). Loan received, EMIs and interest are then entered as guided manual vouchers. Fictional names only.</p></div>
    <label>Loan name<input value={name} maxLength={60} onChange={e=>setName(e.target.value)} placeholder="e.g. Term loan — extension (fictional)"/></label>
    <label>Lender<input value={lender} maxLength={60} onChange={e=>setLender(e.target.value)} placeholder="e.g. Sample Bank"/></label>
    <label>Loan account — last 4 digits (optional)<input value={last4} inputMode="numeric" maxLength={4} onChange={e=>setLast4(e.target.value.replace(/\D/g,''))}/><small className="muted">Only the last four digits are kept.</small></label>
    <label>Note (optional)<input value={note} maxLength={120} onChange={e=>setNote(e.target.value)}/></label>
    {loans.length>0&&<p className="muted">Existing: {loans.map(l=>`${l.name} (${l.id})`).join(' · ')}</p>}
    {!canChart&&<p className="f-why">Only the Owner can add a loan.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!ok}><Plus size={16}/> Add loan</button></div></form>;
}
