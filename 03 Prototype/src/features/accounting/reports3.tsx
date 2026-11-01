// ACC-3 · acc3-ui — the report tabs of contract §6: P&L, Balance sheet, Outstanding, GST summary, Cash ratio, Tally masters, Exports.
// One component per report; `phone` switches tables to cards (48 px targets, 16 px text, no sideways scroll at 360 px). Figures come from the
// contract selectors (re-exported by acc3-views.ts); no rule lives here. Money is shown only to the books roles (Owner / Manager / Accountant), as the trial balance.
import React,{useMemo,useState} from 'react';
import {Download,Info,AlertTriangle,CheckCircle2} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,rbacSeesBooks,rbacCanExportVouchers,rbacCanTally} from '../../services/mock';
import {saveBinaryFile,saveTextFile,saveAndReport} from '../../app/native';
import {ListCard,KpiCard} from '../../app/mobile/components';
import {Empty} from '../../components/ui';
import {RangeBar,fyStart,monthChoices,monthLabel,type AccRange} from './shared';
import {accPl,accBalanceSheet,accAgeing,accGstSummary,accCashRatio,accTallyMasters,accTallyCompany,accReportFile,accReportsZip,ACC3_REPORTS,
  type ACCPlGroup,type ACCBsBlock,type Acc3Report,type Acc3Args} from './acc3-views';

const fmt=(n:number)=>n<0?`(${money(-n)})`:money(n);
const NOT_FOR_ROLE=<Empty title="Not available for your role" body="The books are for the Owner, Manager and Accountant."/>;
function useBooks(){const {s,uid,permission}=useHcc();return {s,ok:rbacSeesBooks(s,uid)&&permission('reports'),canExport:rbacCanExportVouchers(s,uid),canTally:rbacCanTally(s,uid)};}
function Prov({on,text}:{on:boolean;text?:string}){
  return on?<div className="info-note acc-note" role="status"><Info size={16}/>{text??'Provisional: the range includes a period that is not locked or opening balances are pending, so these figures can still change.'}</div>:null;
}
function Toggle<T extends string>({label,value,onChange,options}:{label:string;value:T;onChange:(v:T)=>void;options:[T,string][]}){
  return <div className="filters" role="group" aria-label={label}>{options.map(([id,l])=><button type="button" key={id} className={value===id?'active':''} aria-pressed={value===id} onClick={()=>onChange(id)}>{l}</button>)}</div>;
}
function Tie({ok,label}:{ok:boolean;label:string}){return ok?<span className="pill green"><CheckCircle2 size={12}/>{label}: ties</span>:<span className="pill acc-sev-error"><AlertTriangle size={12}/>{label}: differs</span>;}

/** CSV + Excel buttons for one report (export permission only). */
export function ExportBar({kind,args}:{kind:Acc3Report;args:Acc3Args}){
  const {s,toast}=useHcc();const {canExport}=useBooks();if(!canExport)return null;
  const save=(fmt:'csv'|'xlsx')=>{const f=accReportFile(s,kind,args,fmt);void saveAndReport(saveBinaryFile(f.name,f.bytes,f.mime),toast,fmt==='csv'?'CSV saved':'Excel workbook saved',fmt==='csv'?'the CSV':'the Excel workbook');};
  return <div className="inline acc-export-bar">
    <button type="button" className="btn" onClick={()=>save('csv')}><Download size={16}/> Export CSV</button>
    <button type="button" className="btn" onClick={()=>save('xlsx')}><Download size={16}/> Export Excel</button></div>;
}
const useArgs=(range?:AccRange,asOf?:string,month?:string,fy?:string):Acc3Args=>{const {s}=useHcc();return {from:range?.from??fyStart(s.date),to:range?.to??s.date,asOf:asOf??s.date,month:month??s.date.slice(0,7),fy:fy??fyStart(s.date)};};

// ── P&L ──
export function PlView({range,setRange,phone}:{range:AccRange;setRange:(r:AccRange)=>void;phone?:boolean}){
  const {s,ok}=useBooks();const [byGroup,setByGroup]=useState(false);
  const pl=useMemo(()=>accPl(s,range.from,range.to),[s,range]);const args=useArgs(range);
  if(!ok)return NOT_FOR_ROLE;
  const exp=pl.expenses,groups:ACCPlGroup[]=[exp.consumption,exp.employee,exp.finance,exp.depreciation,exp.other];
  const sections:[string,{code?:string;name:string;paise:number}[],number][]=byGroup?[['By Tally group',pl.byTallyGroup.map(g=>({name:g.name,paise:g.paise})),pl.profitBeforeTax]]
    :[['Revenue',pl.revenue.lines,pl.revenue.total],['Other income',pl.otherIncome.lines,pl.otherIncome.total],...groups.map(g=>[g.name,g.lines,g.total] as [string,typeof g.lines,number])];
  const bar=<div className="acc-subbar"><Toggle label="P&L view" value={byGroup?'g':'l'} onChange={v=>setByGroup(v==='g')} options={[['l','By ledger'],['g','By Tally group']]}/><ExportBar kind="pl" args={args}/></div>;
  const result=<><strong>Profit before tax</strong> <strong className="number">{fmt(pl.profitBeforeTax)}</strong> <span className={'pill '+(pl.profitBeforeTax>=0?'green':'acc-sev-error')}>{pl.profitBeforeTax>=0?'Profit':'Loss'}</span></>;
  return <div className={phone?'':'padded'}><RangeBar s={s} range={range} setRange={setRange} phone={phone}/>{bar}
    <Prov on={pl.provisional}/>
    {phone?<><div className="acc-m-kpis"><KpiCard label="Revenue" value={fmt(pl.revenue.total+pl.otherIncome.total)}/><KpiCard label="Expenses" value={fmt(exp.total)}/><KpiCard label="Profit before tax" value={fmt(pl.profitBeforeTax)} sub={pl.profitBeforeTax>=0?'Profit':'Loss'}/></div>
      {sections.map(([name,lines,total])=><ListCard key={name} title={name} sub={lines.length?`${lines.length} ledger${lines.length===1?'':'s'}`:'Nothing posted'} right={<strong className="number">{fmt(total)}</strong>}/>)}</>
      :<div className="table-wrap"><table className="acc-table"><thead><tr><th>Code</th><th>Ledger</th><th className="acc-num">Amount</th></tr></thead><tbody>
        {sections.map(([name,lines,total])=><React.Fragment key={name}><tr className="acc-group-row"><th colSpan={2}>{name}</th><th className="acc-num number">{fmt(total)}</th></tr>
          {lines.map((l,i)=><tr key={name+i}><td className="number">{l.code??''}</td><td>{l.name}</td><td className="acc-num number">{fmt(l.paise)}</td></tr>)}</React.Fragment>)}
        <tr><th colSpan={2}>Profit before tax (no tax line: proprietor)</th><th className="acc-num number">{fmt(pl.profitBeforeTax)}</th></tr></tbody></table></div>}
    <p className="acc-pl-result">{result}</p></div>;
}

// ── Balance sheet ──
function BsBlock({b,phone}:{b:ACCBsBlock;phone?:boolean}){
  return phone?<><ListCard title={b.name} right={<strong className="number">{fmt(b.total)}</strong>}/>{b.lines.map(l=><ListCard key={b.name+l.code} title={l.name} sub={l.code} right={<span className="number">{fmt(l.paise)}</span>}/>)}</>
    :<><tr className="acc-group-row"><th colSpan={2}>{b.name}</th><th className="acc-num number">{fmt(b.total)}</th></tr>{b.lines.map(l=><tr key={b.name+l.code}><td className="number">{l.code}</td><td>{l.name}</td><td className="acc-num number">{fmt(l.paise)}</td></tr>)}</>;
}
export function BalanceSheetView({asOf,setAsOf,phone}:{asOf:string;setAsOf:(d:string)=>void;phone?:boolean}){
  const {s,ok}=useBooks();const bs=useMemo(()=>accBalanceSheet(s,asOf),[s,asOf]);const args=useArgs(undefined,asOf);
  if(!ok)return NOT_FOR_ROLE;
  const L=bs.liabilities,A=bs.assets;
  const side=(title:string,blocks:ACCBsBlock[],total:number)=>phone?<><h2 className="m-group">{title.toUpperCase()}</h2>{blocks.map(b=><BsBlock key={b.name} b={b} phone/>)}<ListCard title={`Total ${title.toLowerCase()}`} right={<strong className="number">{fmt(total)}</strong>}/></>
    :<div className="table-wrap"><table className="acc-table"><thead><tr><th colSpan={2}>{title}</th><th className="acc-num">Amount</th></tr></thead><tbody>{blocks.map(b=><BsBlock key={b.name} b={b}/>)}
      <tr><th colSpan={2}>Total {title.toLowerCase()}</th><th className="acc-num number">{fmt(total)}</th></tr></tbody></table></div>;
  return <div className={phone?'':'padded'}>
    <div className="acc-subbar"><label className="acc-date">As of<input type="date" value={asOf} max={s.date} onChange={e=>e.target.value&&setAsOf(e.target.value)}/></label>
      {bs.balanced?<span className="pill green"><CheckCircle2 size={12}/>Both sides equal</span>:<span className="pill acc-sev-error"><AlertTriangle size={12}/>Sides differ</span>}<ExportBar kind="bs" args={args}/></div>
    <div className="info-note acc-note" role="note"><Info size={16}/>This is an HCC view for the owner. It is not the statutory statement; the CA prepares that.</div>
    <Prov on={bs.provisional||bs.suspense!==0||bs.openingDifference!==0} text="Provisional: opening balances are pending or suspense (9999) / the opening difference (1400) is not zero."/>
    {side('Liabilities',[L.ownersFunds,L.nonCurrent,L.current],L.total)}{side('Assets',[A.nonCurrent,A.current],A.total)}</div>;
}

// ── Outstanding ──
export function OutstandingView({phone}:{phone?:boolean}){
  const {s,ok}=useBooks();const [side,setSide]=useState<'receivable'|'payable'>('receivable');const [asOf,setAsOf]=useState(s.date);const [openId,setOpenId]=useState('');
  const ag=useMemo(()=>accAgeing(s,asOf,side),[s,asOf,side]);const args=useArgs(undefined,asOf);
  if(!ok)return NOT_FOR_ROLE;
  const t=ag.totals,kind:Acc3Report=side==='receivable'?'ageing-r':'ageing-p';
  const bills=(r:typeof ag.rows[number])=>r.bills.map(b=><ListCard key={r.account+b.ref} title={b.ref==='opening'?'Opening balance':`Bill ${b.ref}`} sub={`${b.date} · due ${b.due} · ${b.days} days`} right={<span className="number">{money(b.open)}</span>}/>);
  return <div className={phone?'':'padded'}>
    <div className="acc-subbar"><Toggle label="Outstanding side" value={side} onChange={setSide} options={[['receivable','Receivable'],['payable','Payable']]}/>
      <label className="acc-date">As of<input type="date" value={asOf} max={s.date} onChange={e=>e.target.value&&setAsOf(e.target.value)}/></label><ExportBar kind={kind} args={args}/></div>
    {phone?<div className="acc-m-kpis"><KpiCard label="Open" value={money(t.open)}/><KpiCard label="0–30 days" value={money(t.b0_30)}/><KpiCard label="31–60 days" value={money(t.b31_60)}/><KpiCard label="61–90 days" value={money(t.b61_90)}/><KpiCard label="Over 90 days" value={money(t.b90)}/></div>:null}
    {!ag.rows.length?<Empty title="Nothing outstanding" body={`No ${side} balance as of ${asOf}.`}/>
      :phone?ag.rows.map(r=><React.Fragment key={r.account}><ListCard title={r.name} sub={`0–30 ${money(r.b0_30)} · 31–60 ${money(r.b31_60)} · 61–90 ${money(r.b61_90)} · over 90 ${money(r.b90)}`} right={<strong className="number">{money(r.open)}</strong>} onClick={()=>setOpenId(openId===r.account?'':r.account)}/>{openId===r.account&&bills(r)}</React.Fragment>)
      :<div className="table-wrap"><table className="acc-table"><thead><tr><th>Party</th><th className="acc-num">Open</th><th className="acc-num">0–30</th><th className="acc-num">31–60</th><th className="acc-num">61–90</th><th className="acc-num">Over 90</th></tr></thead><tbody>
        {ag.rows.map(r=><React.Fragment key={r.account}><tr><td><button type="button" className="text-btn acc-link" aria-expanded={openId===r.account} onClick={()=>setOpenId(openId===r.account?'':r.account)}>{r.name}</button> <small className="muted">{r.account}</small></td>
          <td className="acc-num number"><strong>{money(r.open)}</strong></td><td className="acc-num number">{money(r.b0_30)}</td><td className="acc-num number">{money(r.b31_60)}</td><td className="acc-num number">{money(r.b61_90)}</td><td className="acc-num number">{money(r.b90)}</td></tr>
          {openId===r.account&&r.bills.map(b=><tr key={r.account+b.ref} className="acc-bill-row"><td>{b.ref==='opening'?'Opening balance':`Bill ${b.ref}`} · {b.date} · due {b.due}</td><td className="acc-num number">{money(b.open)}</td><td colSpan={4}>{b.days} days old · billed {money(b.amount)} · paid {money(b.paid)}</td></tr>)}</React.Fragment>)}
        <tr><th>Total</th><th className="acc-num number">{money(t.open)}</th><th className="acc-num number">{money(t.b0_30)}</th><th className="acc-num number">{money(t.b31_60)}</th><th className="acc-num number">{money(t.b61_90)}</th><th className="acc-num number">{money(t.b90)}</th></tr></tbody></table></div>}</div>;
}

// ── GST summary ──
export function GstSummaryView({phone}:{phone?:boolean}){
  const {s,ok}=useBooks();const months=useMemo(()=>monthChoices(s.date),[s.date]);const [month,setMonth]=useState(months[0]);
  const g=useMemo(()=>accGstSummary(s,month),[s,month]);const args=useArgs(undefined,undefined,month);
  if(!ok)return NOT_FOR_ROLE;
  const rows:[string,number][]=[['Output tax at 5 %',g.output.rate5],['Output tax at 18 %',g.output.rate18],['Business to business (B2B)',g.output.b2b],['Business to consumer, small (B2CS)',g.output.b2cs],['Advances received (11A)',g.output.advances11A],['Advances adjusted (11B)',g.output.advances11B],['Credit notes',g.output.creditNotes],
    ['Input credit: CGST',g.itc.cgst],['Input credit: SGST',g.itc.sgst],['Input credit: IGST',g.itc.igst],['Reverse charge',g.rcm],['Set-off: payable in cash',g.setoff.payable],['Set-off: challan to deposit',g.setoff.challan],['Paid in cash (challan)',g.cashPaid],['TCS credit',g.tcsCredit],['Cash ledger balance (1340)',g.cashLedgerBalance]];
  return <div className={phone?'':'padded'}>
    <div className="acc-subbar"><label className="acc-date">Month<select value={month} onChange={e=>setMonth(e.target.value)}>{months.map(m=><option key={m} value={m}>{monthLabel(m)}</option>)}</select></label>
      <Tie ok={g.ties.gstr1} label="GSTR-1"/><Tie ok={g.ties.gstr3b} label="GSTR-3B"/><ExportBar kind="gst" args={args}/></div>
    {g.ties.differences.length>0&&<div className="info-note f-warn" role="status"><AlertTriangle size={16}/><span>{g.ties.differences.join(' · ')}</span></div>}
    {phone?rows.map(([l,v])=><ListCard key={l} title={l} right={<strong className="number">{fmt(v)}</strong>}/>)
      :<div className="table-wrap"><table className="acc-table"><thead><tr><th>Line</th><th className="acc-num">Amount</th></tr></thead><tbody>{rows.map(([l,v])=><tr key={l}><td>{l}</td><td className="acc-num number">{fmt(v)}</td></tr>)}</tbody></table></div>}</div>;
}

// ── Cash ratio ──
export function CashRatioView({phone}:{phone?:boolean}){
  const {s,ok}=useBooks();const cur=+fyStart(s.date).slice(0,4);const [fy,setFy]=useState(fyStart(s.date));
  const c=useMemo(()=>accCashRatio(s,fy),[s,fy]);const args=useArgs(undefined,undefined,undefined,fy);
  if(!ok)return NOT_FOR_ROLE;
  const rows:[string,number,number,number][]=[['Receipts',c.cashReceipts,c.totalReceipts,c.receiptsPct],['Payments',c.cashPayments,c.totalPayments,c.paymentsPct]];
  return <div className={phone?'':'padded'}>
    <div className="acc-subbar"><label className="acc-date">Financial year<select value={fy} onChange={e=>setFy(e.target.value)}>{[cur,cur-1,cur-2].map(y=><option key={y} value={`${y}-04-01`}>{y}-{String(y+1).slice(2)}</option>)}</select></label>
      {c.flagged?<span className="pill acc-sev-error"><AlertTriangle size={12}/>Flagged: cash is above 5 %</span>:<span className="pill green"><CheckCircle2 size={12}/>Within 5 %</span>}<ExportBar kind="cash" args={args}/></div>
    {phone?rows.map(([l,cash,tot,pct])=><ListCard key={l} title={l} sub={`Cash ${money(cash)} of ${money(tot)}`} right={<strong className="number">{(pct/100).toFixed(2)} %</strong>}/>)
      :<div className="table-wrap"><table className="acc-table"><thead><tr><th>Measure</th><th className="acc-num">Cash</th><th className="acc-num">All</th><th className="acc-num">Cash share</th></tr></thead><tbody>
        {rows.map(([l,cash,tot,pct])=><tr key={l}><td>{l}</td><td className="acc-num number">{money(cash)}</td><td className="acc-num number">{money(tot)}</td><td className="acc-num number">{(pct/100).toFixed(2)} %</td></tr>)}</tbody></table></div>}
    <p className="muted">Cash means entries on the cash ledgers 1000 and 1005. The flag shows when either share is above 5 %.</p></div>;
}

// ── Tally masters (shown above the voucher export) ──
export function TallyMastersPanel(){
  const {s,toast}=useHcc();const {canTally}=useBooks();const cur=+fyStart(s.date).slice(0,4);const [fy,setFy]=useState(fyStart(s.date));
  const m=useMemo(()=>accTallyMasters(s,fy,accTallyCompany(s)),[s,fy]);
  if(!canTally)return null;
  return <div className="padded exp-tally exp-tally-masters">
    <h3>Masters (groups and ledgers)</h3>
    <div className="inline exp-tally-range"><label className="acc-date">Financial year<select value={fy} onChange={e=>setFy(e.target.value)}>{[cur,cur-1].map(y=><option key={y} value={`${y}-04-01`}>{y}-{String(y+1).slice(2)}</option>)}</select></label>
      <button type="button" className="btn" disabled={!m.ledgers||!!m.error} onClick={()=>{void saveAndReport(saveTextFile(m.fileName,m.xml,'application/xml'),toast,'Tally masters XML saved · not imported into Tally','the Tally masters');}}><Download size={16}/> Download masters XML</button></div>
    {m.error?<p className="f-why" role="alert">{m.error}</p>:<p className="muted">{m.groups} group{m.groups===1?'':'s'} and {m.ledgers} ledger{m.ledgers===1?'':'s'} ({m.partyLedgers} part{m.partyLedgers===1?'y':'ies'}). Import the masters before the vouchers.</p>}
    <p className="muted">Tally’s 28 primary groups are not created; they exist in every company. Opening balances are as at the start of the financial year.</p>
    {m.notes.map(n=><p key={n} className="muted">{n}</p>)}</div>;
}

// ── Exports (bulk) ──
export function ExportsView({phone}:{phone?:boolean}){
  const {s,toast}=useHcc();const {ok,canExport}=useBooks();const [range,setRange]=useState<AccRange>({from:fyStart(s.date),to:s.date});
  const args=useArgs(range);
  if(!ok)return NOT_FOR_ROLE;
  if(!canExport)return <Empty title="Exports are not available for your role" body="Ask the Owner or the Accountant for the files."/>;
  const one=(k:Acc3Report,xlsx:boolean)=>{const f=accReportFile(s,k,{...args,asOf:range.to},xlsx?'xlsx':'csv');void saveAndReport(saveBinaryFile(f.name,f.bytes,f.mime),toast,xlsx?'Excel workbook saved':'CSV saved','the file');};
  return <div className={phone?'':'padded'}>
    <RangeBar s={s} range={range} setRange={setRange} phone={phone}/>
    <p className="muted">One file per report for the range above (as-of reports use the end date).</p>
    {ACC3_REPORTS.map(([k,label])=><div key={k} className="inline acc-export-row"><strong>{label}</strong>
      <button type="button" className="btn" onClick={()=>one(k,false)}><Download size={16}/> CSV</button><button type="button" className="btn" onClick={()=>one(k,true)}><Download size={16}/> Excel</button></div>)}
    <button type="button" className="btn primary full" onClick={()=>{const z=accReportsZip(s,{...args,asOf:range.to});void saveAndReport(saveBinaryFile(z.name,z.bytes,z.mime),toast,'Zip saved · every report as CSV','the zip');}}><Download size={16}/> Download all (zip)</button></div>;
}
