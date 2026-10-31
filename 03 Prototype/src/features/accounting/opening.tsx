// ACC-1 · acc1-ui — Opening balances (contract §5; DEC-139 / DEC-148): entry grid of ledger / party, Dr, Cr; the automatic 1400 difference line;
// Save for Owner / Manager / Accountant, Approve and Reopen for the Owner; status pill draft / approved / pending. Desktop = inline grid,
// phone = line cards + the 'acc-opening-line' sheet.
import React,{useMemo,useState} from 'react';
import {Plus,Trash2,CheckCircle2,AlertTriangle,Info,Save,Lock,LockOpen} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,type ACCOpening,type ACCOpeningLine} from '../../services/mock';
import {accOpening,accOpeningPending,accChart,accPartyLedgers} from '../../services/mock';
import {accOpeningOverHandCap,accOpeningPageOf,ACC_OPENING_MAX_LINES} from '../../services/mock';
import type {AccOpeningSave,AccOpeningApprove,AccOpeningReopen} from './commands';
import {ListCard,KpiCard,StickyFooter} from '../../app/mobile/components';
import {Empty,dateLabel,shortDate,clockTime} from '../../components/ui';
import {useAccPerms,accUserName} from './parts';
import {ledgerOptions,toPaise,fromPaise,fyStart,accAmt} from './shared';

export const OPENING_DIFF='1400';
/** The automatic balancing line on 1400 for what is entered (display only; the handler adds the same line when it saves). */
export function openingGap(lines:ACCOpeningLine[]):ACCOpeningLine|undefined{
  const net=lines.filter(l=>l.account!==OPENING_DIFF).reduce((a,l)=>a+l.debit-l.credit,0);
  return net===0?undefined:net>0?{account:OPENING_DIFF,debit:0,credit:net}:{account:OPENING_DIFF,debit:-net,credit:0};
}
export const openingTotals=(lines:ACCOpeningLine[])=>{const g=openingGap(lines),all=g?[...lines.filter(l=>l.account!==OPENING_DIFF),g]:lines.filter(l=>l.account!==OPENING_DIFF);
  return {debit:all.reduce((a,l)=>a+l.debit,0),credit:all.reduce((a,l)=>a+l.credit,0),gap:g};};
export function useOpeningNames(){
  const {s}=useHcc();
  return useMemo(()=>{const m=new Map<string,string>();accChart(s).forEach(l=>m.set(l.code,l.name));accPartyLedgers(s).forEach(p=>m.set(p.id,p.name));return (id:string)=>m.get(id)??id;},[s]);
}
export function OpeningStatus({o,pending}:{o?:ACCOpening;pending:boolean}){
  return <span className="acc-open-pills">
    {!o?<span className="pill acc-sev-warning">Not entered</span>:o.status==='approved'?<span className="pill green"><Lock size={12} aria-hidden/>Approved</span>:<span className="pill blue">Draft</span>}
    {pending&&(o?<span className="pill acc-sev-error"><AlertTriangle size={12} aria-hidden/>Pending — period locks from {shortDate(o.asOf)} refused</span>
      :<span className="pill acc-sev-warning"><AlertTriangle size={12} aria-hidden/>Pending — locks not gated until an entry exists</span>)}</span>;
}

type Row={k:number;account:string;debit:string;credit:string};
let rowKey=0;
const toRows=(o?:ACCOpening):Row[]=>(o?.lines.filter(l=>l.account!==OPENING_DIFF)??[]).map(l=>({k:rowKey++,account:l.account,debit:fromPaise(l.debit),credit:fromPaise(l.credit)}));

export function OpeningView({phone}:{phone?:boolean}){
  const {s}=useHcc();const o=accOpening(s),pending=accOpeningPending(s);
  const {canOpeningEnter,canOpeningApprove}=useAccPerms();
  const name=useOpeningNames();
  const head=<div className="acc-open-head"><OpeningStatus o={o} pending={pending}/>
    <small className="muted">{o?`As of ${dateLabel(o.asOf)} · saved by ${accUserName(s,o.by)} ${shortDate(o.at.slice(0,10))} ${clockTime(o.at)}${o.status==='approved'&&o.approvedBy?` · approved by ${accUserName(s,o.approvedBy)}`:''}`:`Dated ${dateLabel(fyStart(s.date))} (start of the business year) when saved.`}</small></div>;
  const info=<div className="info-note acc-note" role="note"><Info size={16}/>Enter one line per ledger and per party, one side only. A difference is parked on 1400 Opening-balance difference automatically. The Owner approves. Once an entry exists, a period lock on or after its date is refused while it is not approved or 1400 is not zero (with no entry at all, days still lock — DEC-148).</div>;
  return phone?<OpeningPhone o={o} pending={pending} head={head} info={info} name={name} canEnter={canOpeningEnter} canApprove={canOpeningApprove}/>
    :<div className="padded acc-opening">{head}{info}<OpeningGrid key={(o?.at??'new')+(o?.status??'')} o={o} canEnter={canOpeningEnter} canApprove={canOpeningApprove}/></div>;
}

function ReopenForm({onDone}:{onDone:()=>void}){
  const {run,busy}=useHcc();const [reason,setReason]=useState('');
  return <form className="form acc-reopen" onSubmit={e=>{e.preventDefault();void run({type:'ACC_OPENING_REOPEN',reason:reason.trim()} satisfies AccOpeningReopen,'Opening balances reopened as a draft',{after:onDone});}}>
    <label>Reason for reopening (required)<textarea rows={2} required minLength={3} value={reason} onChange={e=>setReason(e.target.value)} enterKeyHint="done"/></label>
    <div className="modal-actions"><button type="button" className="btn" onClick={onDone}>Cancel</button><button className="btn primary" type="submit" disabled={busy||reason.trim().length<3}><LockOpen size={16}/> Reopen</button></div></form>;
}

/** LG-R8: pager for an entry longer than one page (imported drafts may exceed the hand-entry cap). */
function OpeningPager({page,pages,total,set}:{page:number;pages:number;total:number;set:(p:number)=>void}){
  if(pages<2)return null;
  return <div className="acc-open-pager" role="navigation" aria-label="Opening lines pages">
    <button type="button" className="btn" disabled={page===0} onClick={()=>set(page-1)}>Previous</button>
    <span className="muted">Page {page+1} of {pages} · {total} lines</span>
    <button type="button" className="btn" disabled={page>=pages-1} onClick={()=>set(page+1)}>Next</button></div>;
}
const bigNote=(n:number)=><p className="muted" role="note">This draft was written by the legacy importer and has {n} lines — more than the {ACC_OPENING_MAX_LINES} a hand entry holds. It is shown in pages and read-only; the Owner approves it whole, and later corrections go through an Owner-approved journal.</p>;

function OpeningGrid({o,canEnter,canApprove}:{o?:ACCOpening;canEnter:boolean;canApprove:boolean}){
  const {s,run,busy}=useHcc();const opts=useMemo(()=>ledgerOptions(s,true),[s]);
  const [rows,setRows]=useState<Row[]>(()=>toRows(o));const [note,setNote]=useState(o?.note??'');const [reopen,setReopen]=useState(false);
  const big=accOpeningOverHandCap(o),[page,setPage]=useState(0);
  const approved=o?.status==='approved',edit=canEnter&&!approved&&!big;
  const shown=accOpeningPageOf(rows,page);
  const lines:ACCOpeningLine[]=rows.filter(r=>r.account).map(r=>({account:r.account,debit:toPaise(r.debit),credit:toPaise(r.credit)}));
  const both=lines.some(l=>l.debit&&l.credit),blank=lines.some(l=>!l.debit&&!l.credit),dup=new Set(lines.map(l=>l.account)).size!==lines.length;
  const tot=openingTotals(lines.filter(l=>l.debit||l.credit));
  const dirty=JSON.stringify(lines)!==JSON.stringify((o?.lines??[]).filter(l=>l.account!==OPENING_DIFF))||note!==(o?.note??'');
  const name=useOpeningNames();
  const problem=both?'A line has both Dr and Cr — enter one side only.':dup?'A ledger or party appears twice.':blank?'A line has no amount.':'';
  const set=(k:number,p:Partial<Row>)=>setRows(r=>r.map(x=>x.k===k?{...x,...p}:x));
  const save=()=>void run({type:'ACC_OPENING_SAVE',asOf:o?.asOf??fyStart(s.date),lines:lines.filter(l=>l.debit||l.credit),note:note.trim()||undefined} satisfies AccOpeningSave,'Opening balances saved as a draft');
  return <>
    <div className="table-wrap"><table className="acc-table acc-open-grid"><thead><tr><th>Ledger / party</th><th className="acc-num">Debit (₹)</th><th className="acc-num">Credit (₹)</th><th/></tr></thead>
      <tbody>{shown.items.map(r=><tr key={r.k}><td>{edit?<select aria-label="Ledger or party" value={r.account} onChange={e=>set(r.k,{account:e.target.value})}><option value="">Choose…</option>
            {[...opts,...(r.account&&!opts.some(x=>x.id===r.account)?[{id:r.account,label:`${r.account} · ${name(r.account)}`}]:[])].map(x=><option key={x.id} value={x.id}>{x.label}</option>)}</select>
          :<><strong>{name(r.account)}</strong> <small className="number">{r.account}</small></>}</td>
        <td className="acc-num">{edit?<input inputMode="decimal" aria-label="Debit in rupees" value={r.debit} onChange={e=>set(r.k,{debit:e.target.value,credit:e.target.value?'':r.credit})}/>:<span className="number">{accAmt(toPaise(r.debit))}</span>}</td>
        <td className="acc-num">{edit?<input inputMode="decimal" aria-label="Credit in rupees" value={r.credit} onChange={e=>set(r.k,{credit:e.target.value,debit:e.target.value?'':r.debit})}/>:<span className="number">{accAmt(toPaise(r.credit))}</span>}</td>
        <td>{edit&&<button type="button" className="icon-btn" aria-label="Remove line" onClick={()=>setRows(x=>x.filter(y=>y.k!==r.k))}><Trash2 size={16}/></button>}</td></tr>)}
        {!rows.length&&<tr><td colSpan={4} className="muted">No lines yet.{edit?' Add the first line below.':''}</td></tr>}
        {tot.gap&&<tr className="acc-gap-row" id="acc-open-gap"><td><strong>{name(OPENING_DIFF)}</strong> <small className="number">{OPENING_DIFF}</small><small className="acc-sub">Added automatically so the entry balances</small></td>
          <td className="acc-num number">{accAmt(tot.gap.debit)}</td><td className="acc-num number">{accAmt(tot.gap.credit)}</td><td/></tr>}
        <tr className="acc-total"><td><strong>Total</strong></td><td className="acc-num number"><strong>{money(tot.debit)}</strong></td><td className="acc-num number"><strong>{money(tot.credit)}</strong></td><td/></tr></tbody></table></div>
    <OpeningPager page={shown.page} pages={shown.pages} total={rows.length} set={setPage}/>
    {big&&bigNote(rows.length)}
    <p className="acc-balance-line">{tot.gap?<span className="pill acc-sev-warning"><AlertTriangle size={12}/>{money(tot.gap.debit||tot.gap.credit)} parked on 1400 — approve only when this is understood</span>:lines.length?<span className="pill green"><CheckCircle2 size={12}/>Balanced — 1400 is zero</span>:null}</p>
    {edit&&<label className="acc-open-note">Note (optional)<input value={note} maxLength={200} onChange={e=>setNote(e.target.value)} placeholder="e.g. Closing trial balance of the old books, 31 March"/></label>}
    {problem&&<p className="f-why" role="alert">{problem}</p>}
    {!canEnter&&<p className="muted" role="note">Your role can read the opening balances. The Manager or Accountant enters them; the Owner approves.</p>}
    {reopen&&<ReopenForm onDone={()=>setReopen(false)}/>}
    {!reopen&&<div className="modal-actions acc-open-actions">
      {edit&&<button type="button" className="btn" onClick={()=>setRows(r=>[...r,{k:rowKey++,account:'',debit:'',credit:''}])}><Plus size={16}/> Add line</button>}
      {edit&&<button type="button" className="btn primary" disabled={busy||!!problem||!lines.length||!dirty} onClick={save}><Save size={16}/> Save draft</button>}
      {canApprove&&o&&!approved&&<button type="button" className="btn primary" disabled={busy||dirty||!o.lines.length} title={dirty?'Save the draft first':undefined} onClick={()=>void run({type:'ACC_OPENING_APPROVE'} satisfies AccOpeningApprove,'Opening balances approved')}><CheckCircle2 size={16}/> Approve</button>}
      {canApprove&&approved&&<button type="button" className="btn" onClick={()=>setReopen(true)}><LockOpen size={16}/> Reopen</button>}</div>}
  </>;
}

function OpeningPhone({o,pending,head,info,name,canEnter,canApprove}:{o?:ACCOpening;pending:boolean;head:React.ReactNode;info:React.ReactNode;name:(id:string)=>string;canEnter:boolean;canApprove:boolean}){
  const {run,busy,open}=useHcc();const [reopen,setReopen]=useState(false);
  const lines=(o?.lines??[]).filter(l=>l.account!==OPENING_DIFF),tot=openingTotals(lines),approved=o?.status==='approved',big=accOpeningOverHandCap(o),edit=canEnter&&!approved&&!big;
  const [page,setPage]=useState(0),shown=accOpeningPageOf(lines,page);
  const add=()=>open({type:'acc-opening-line',id:'new'});
  return <>
    {head}{info}
    <div className="acc-m-kpis"><KpiCard label="Debits" value={money(tot.debit)}/><KpiCard label="Credits" value={money(tot.credit)}/></div>
    {big&&bigNote(lines.length)}
    {lines.length?shown.items.map(l=><ListCard key={l.account} title={name(l.account)} sub={l.account} right={<strong className="number">{l.debit?'Dr '+money(l.debit):'Cr '+money(l.credit)}</strong>} onClick={edit?()=>open({type:'acc-opening-line',id:l.account}):undefined}/>)
      :<Empty title="No opening lines yet" body={edit?'Tap Add line to enter the first ledger or party balance.':'The Manager or Accountant enters the opening balances.'}/>}
    <OpeningPager page={shown.page} pages={shown.pages} total={lines.length} set={setPage}/>
    {tot.gap&&<ListCard tone="acc-m-ex-warning" title={`${name(OPENING_DIFF)}`} sub={`${OPENING_DIFF} · added automatically so the entry balances`} right={<strong className="number">{tot.gap.debit?'Dr '+money(tot.gap.debit):'Cr '+money(tot.gap.credit)}</strong>}/>}
    {lines.length>0&&!tot.gap&&<p className="acc-balance-line"><span className="pill green"><CheckCircle2 size={12}/>Balanced — 1400 is zero</span></p>}
    {pending&&o?.status==='approved'&&<p className="muted acc-m-note">Approved, but the 1400 difference is not zero — the first period lock stays refused.</p>}
    {reopen&&<ReopenForm onDone={()=>setReopen(false)}/>}
    {!reopen&&(edit||canApprove)&&<StickyFooter>
      {canApprove&&o&&!approved&&lines.length>0?<><button type="button" className="btn" disabled={!edit} onClick={add}><Plus size={16}/> Add line</button><button type="button" className="btn primary" disabled={busy} onClick={()=>void run({type:'ACC_OPENING_APPROVE'} satisfies AccOpeningApprove,'Opening balances approved')}><CheckCircle2 size={16}/> Approve</button></>
        :canApprove&&approved?<button type="button" className="btn primary" onClick={()=>setReopen(true)}><LockOpen size={16}/> Reopen</button>
        :<button type="button" className="btn primary" disabled={!edit} onClick={add}><Plus size={16}/> Add line</button>}</StickyFooter>}
  </>;
}
