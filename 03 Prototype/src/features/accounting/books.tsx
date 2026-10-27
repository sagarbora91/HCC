// ACC-1 · acc1-ui — the books: six-column trial balance (contract §6), ledger / party statement, day book. One component per report;
// `phone` switches the table to cards (phone rules: cards not tables, 48 px targets, no sideways scroll at 360 px).
import React,{useMemo,useState} from 'react';
import {CheckCircle2,AlertTriangle,Download,Info} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,rbacCanExportVouchers} from '../../services/mock';
import {accTrialBalance6,accStatement,accDayBook,accTrialBalance6Csv,accStatementCsv,accDayBookCsv,accGroups,type ACCDayBookRow} from '../../services/mock';
import {ListCard,KpiCard} from '../../app/mobile/components';
import {Empty,Stat,shortDate} from '../../components/ui';
import {usePaged,ShowMore,DESKTOP_PAGE,PHONE_PAGE} from '../../components/ShowMore';
import {downloadCsv} from '../reports/Reports';
import {RangeBar,LedgerPicker,ledgerOptions,drcr,accAmt,type AccRange} from './shared';

function CsvButton({name,csv,disabled}:{name:string;csv:()=>string;disabled?:boolean}){
  const {s,uid,toast}=useHcc();if(!rbacCanExportVouchers(s,uid))return null;
  return <button type="button" className="btn" disabled={disabled} onClick={()=>{downloadCsv(name,csv());toast('CSV downloaded');}}><Download size={16}/> Export CSV</button>;
}

// ── Trial balance (six columns) ──
export function TrialBalanceView({range,setRange,phone,onStatement}:{range:AccRange;setRange:(r:AccRange)=>void;phone?:boolean;onStatement:(account:string)=>void}){
  const {s}=useHcc();const [detail,setDetail]=useState(false);
  const tb=useMemo(()=>accTrialBalance6(s,range.from,range.to,detail),[s,range,detail]);
  const gname=useMemo(()=>new Map(accGroups(s).map(g=>[g.id,g.name])),[s]);
  const pg=usePaged(tb.rows,phone?PHONE_PAGE:DESKTOP_PAGE),t=tb.totals;
  const bar=<div className="acc-subbar"><div className="filters" role="group" aria-label="Trial balance detail">
      <button type="button" className={!detail?'active':''} aria-pressed={!detail} onClick={()=>setDetail(false)}>Control ledgers</button>
      <button type="button" className={detail?'active':''} aria-pressed={detail} onClick={()=>setDetail(true)}>With parties</button></div>
    {tb.balanced?<span className="pill green"><CheckCircle2 size={12}/>Debits = credits in every column</span>:<span className="pill acc-sev-error"><AlertTriangle size={12}/>Out of balance</span>}
    {!phone&&<CsvButton name={`HCC-Demo-trial-balance-${range.from}_${range.to}.csv`} csv={()=>accTrialBalance6Csv(tb)} disabled={!tb.rows.length}/>}</div>;
  const pending=tb.openingPending&&<div className="info-note acc-note" role="status"><Info size={16}/>Opening balances pending: no approved opening entry yet, or the 1400 difference is not zero. Opening columns are incomplete until it is approved.</div>;
  if(phone)return <>
    <RangeBar s={s} range={range} setRange={setRange} phone/>{bar}{pending}
    <div className="acc-m-kpis"><KpiCard label="Opening" value={drcr(t.openingDebit-t.openingCredit)} sub={tb.balanced?'Dr = Cr':'Dr ≠ Cr'}/><KpiCard label="Closing" value={drcr(t.closingDebit-t.closingCredit)} sub={tb.balanced?'Dr = Cr':'Dr ≠ Cr'}/>
      <KpiCard label="Period Dr" value={money(t.debit)}/><KpiCard label="Period Cr" value={money(t.credit)}/></div>
    {pg.shown.length?pg.shown.map(r=><ListCard key={r.account} title={`${r.account} · ${r.name}`} sub={`Opening ${drcr(r.openingDebit-r.openingCredit)} · Dr ${accAmt(r.debit)} · Cr ${accAmt(r.credit)}`}
      meta={gname.get(r.group)??r.group} right={<strong className="number">{drcr(r.closingDebit-r.closingCredit)}</strong>} onClick={()=>onStatement(r.account)}/>)
      :<Empty title="No ledger movement" body="No vouchers or balances in this range."/>}
    <ShowMore p={pg}/>
    <CsvButton name={`HCC-Demo-trial-balance-${range.from}_${range.to}.csv`} csv={()=>accTrialBalance6Csv(tb)} disabled={!tb.rows.length}/></>;
  return <>
    <RangeBar s={s} range={range} setRange={setRange}/>{bar}{pending}
    {tb.rows.length?<><div className="table-wrap"><table className="acc-table acc-tb6"><thead><tr><th rowSpan={2}>Code</th><th rowSpan={2}>Ledger</th><th rowSpan={2}>Group</th><th colSpan={2} className="acc-num acc-colgroup">Opening</th><th colSpan={2} className="acc-num acc-colgroup">Period</th><th colSpan={2} className="acc-num acc-colgroup">Closing</th></tr>
        <tr><th className="acc-num">Dr</th><th className="acc-num">Cr</th><th className="acc-num">Dr</th><th className="acc-num">Cr</th><th className="acc-num">Dr</th><th className="acc-num">Cr</th></tr></thead>
      <tbody>{pg.shown.map(r=><tr key={r.account} className={r.control?'acc-party-row':''}><td className="number">{r.account}</td>
          <td><button type="button" className="text-btn acc-link" onClick={()=>onStatement(r.account)}><strong>{r.name}</strong></button></td><td>{gname.get(r.group)??r.group}</td>
          <td className="acc-num number">{accAmt(r.openingDebit)}</td><td className="acc-num number">{accAmt(r.openingCredit)}</td><td className="acc-num number">{accAmt(r.debit)}</td><td className="acc-num number">{accAmt(r.credit)}</td>
          <td className="acc-num number">{accAmt(r.closingDebit)}</td><td className="acc-num number">{accAmt(r.closingCredit)}</td></tr>)}
        <tr className="acc-total"><td/><td><strong>Total (all {tb.rows.length} rows)</strong></td><td/>
          {[t.openingDebit,t.openingCredit,t.debit,t.credit,t.closingDebit,t.closingCredit].map((n,i)=><td key={i} className="acc-num number"><strong>{money(n)}</strong></td>)}</tr></tbody></table></div><ShowMore p={pg}/></>
      :<Empty title="No ledger movement" body="No vouchers or balances in this range."/>}</>;
}

// ── Statement ──
export function StatementView({range,setRange,account,setAccount,phone}:{range:AccRange;setRange:(r:AccRange)=>void;account:string;setAccount:(a:string)=>void;phone?:boolean}){
  const {s,open}=useHcc();const opts=useMemo(()=>ledgerOptions(s,false),[s]);
  const st=useMemo(()=>account?accStatement(s,account,range.from,range.to):null,[s,account,range]);
  const pg=usePaged(st?.rows??[],phone?PHONE_PAGE:DESKTOP_PAGE);
  const voucher=(date:string,no:string)=>open({type:'acc-voucher',id:date+'|'+no});
  return <>
    <div className="acc-subbar acc-stmt-pick"><LedgerPicker options={opts} value={account} onChange={setAccount}/></div>
    <RangeBar s={s} range={range} setRange={setRange} phone={phone}/>
    {!st?<Empty title="Choose a ledger or party" body="A statement shows the opening balance, every entry with a running balance, and the closing balance. Choose a control ledger to see all its parties together."/>:<>
      {phone?<div className="acc-m-kpis"><KpiCard label="Opening" value={drcr(st.opening)}/><KpiCard label="Closing" value={drcr(st.closing)}/><KpiCard label="Debits" value={accAmt(st.debit)}/><KpiCard label="Credits" value={accAmt(st.credit)}/></div>
        :<div className="stats acc-stats"><Stat title="Opening balance" value={drcr(st.opening)} sub={`Before ${shortDate(range.from)}`}/><Stat title="Debits" value={accAmt(st.debit)} sub={`${st.rows.length} entries`}/><Stat title="Credits" value={accAmt(st.credit)} sub={st.name}/><Stat title="Closing balance" value={drcr(st.closing)} sub={`At ${shortDate(range.to)}`}/></div>}
      {!phone&&<div className="acc-subbar"><strong>{st.account} · {st.name}</strong><CsvButton name={`HCC-Demo-statement-${st.account.replace('/','_')}-${range.from}_${range.to}.csv`} csv={()=>accStatementCsv(st)}/></div>}
      {phone?<>{pg.shown.length?pg.shown.map((r,i)=><ListCard key={r.no+i} title={`${r.no} · ${shortDate(r.date)}`} sub={r.narration} meta={`${r.type} · ${r.counter||'—'}`}
          right={<><strong className="number">{r.debit?'Dr '+money(r.debit):'Cr '+money(r.credit)}</strong><br/><small className="number">{drcr(r.running)}</small></>} onClick={()=>voucher(r.date,r.no)}/>)
          :<Empty title="No entries in this range" body="Only the opening and closing balances apply."/>}<ShowMore p={pg}/>
        <CsvButton name={`HCC-Demo-statement-${st.account.replace('/','_')}-${range.from}_${range.to}.csv`} csv={()=>accStatementCsv(st)}/></>
      :<><div className="table-wrap"><table className="acc-table"><thead><tr><th>Date</th><th>Voucher</th><th>Type</th><th>Narration</th><th>Counter ledger</th><th className="acc-num">Debit</th><th className="acc-num">Credit</th><th className="acc-num">Balance</th></tr></thead>
          <tbody><tr className="acc-total"><td colSpan={7}><strong>Opening balance</strong></td><td className="acc-num number"><strong>{drcr(st.opening)}</strong></td></tr>
            {pg.shown.map((r,i)=><tr key={r.no+i}><td>{shortDate(r.date)}</td><td><button type="button" className="text-btn acc-link number" onClick={()=>voucher(r.date,r.no)}>{r.no}</button></td><td>{r.type}</td><td className="acc-wrap">{r.narration}</td><td>{r.counter}</td>
              <td className="acc-num number">{accAmt(r.debit)}</td><td className="acc-num number">{accAmt(r.credit)}</td><td className="acc-num number">{drcr(r.running)}</td></tr>)}
            <tr className="acc-total"><td colSpan={5}><strong>Closing balance</strong></td><td className="acc-num number"><strong>{money(st.debit)}</strong></td><td className="acc-num number"><strong>{money(st.credit)}</strong></td><td className="acc-num number"><strong>{drcr(st.closing)}</strong></td></tr></tbody></table></div><ShowMore p={pg}/>
        {!st.rows.length&&<Empty title="No entries in this range" body="Only the opening and closing balances apply."/>}</>}</>}</>;
}

// ── Day book ──
type DbVoucher={date:string;no:string;type:string;narration:string;rows:ACCDayBookRow[];debit:number};
export function DayBookView({range,setRange,phone}:{range:AccRange;setRange:(r:AccRange)=>void;phone?:boolean}){
  const {s,open}=useHcc();const db=useMemo(()=>accDayBook(s,range.from,range.to),[s,range]),rows=db.rows;
  const vs=useMemo(()=>{const m=new Map<string,DbVoucher>();rows.forEach(r=>{const k=r.date+'|'+r.no;let v=m.get(k);if(!v){v={date:r.date,no:r.no,type:r.type,narration:r.narration,rows:[],debit:0};m.set(k,v);}v.rows.push(r);v.debit+=r.debit;});return [...m.values()];},[rows]);
  const pg=usePaged(vs,phone?PHONE_PAGE:DESKTOP_PAGE);
  const tot={debit:db.debit,credit:db.credit};
  const voucher=(v:DbVoucher)=>open({type:'acc-voucher',id:v.date+'|'+v.no});
  const csv=<CsvButton name={`HCC-Demo-day-book-${range.from}_${range.to}.csv`} csv={()=>accDayBookCsv(db)} disabled={!rows.length}/>;
  return <>
    <RangeBar s={s} range={range} setRange={setRange} phone={phone}/>
    <div className="acc-subbar"><span className="muted">{vs.length} voucher{vs.length===1?'':'s'} · debits {money(tot.debit)} · credits {money(tot.credit)}</span>{!phone&&csv}</div>
    {vs.length?phone?<>{pg.shown.map(v=><ListCard key={v.date+v.no} title={v.no} sub={v.narration} meta={`${shortDate(v.date)} · ${v.type} · ${v.rows.length} lines`} right={<strong className="number">{money(v.debit)}</strong>} onClick={()=>voucher(v)}/>)}<ShowMore p={pg}/>{csv}</>
      :<><div className="table-wrap"><table className="acc-table"><thead><tr><th>Date</th><th>Voucher</th><th>Type</th><th>Narration</th><th>Ledger</th><th className="acc-num">Debit</th><th className="acc-num">Credit</th></tr></thead>
        <tbody>{pg.shown.flatMap(v=>v.rows.map((r,i)=><tr key={v.date+v.no+i} className={i===0?'acc-vstart':''}>
          <td>{i===0?shortDate(v.date):''}</td><td>{i===0&&<button type="button" className="text-btn acc-link number" onClick={()=>voucher(v)}>{v.no}</button>}</td><td>{i===0?v.type:''}</td><td className="acc-wrap">{i===0?v.narration:''}</td>
          <td><span className={r.credit&&!r.debit?'acc-indent':''}>{r.ledger}</span> <small className="number">{r.account}</small></td><td className="acc-num number">{accAmt(r.debit)}</td><td className="acc-num number">{accAmt(r.credit)}</td></tr>))}</tbody></table></div><ShowMore p={pg}/></>
      :<Empty title="No vouchers in this range" body="Nothing was posted, received or spent on these business dates."/>}</>;
}
