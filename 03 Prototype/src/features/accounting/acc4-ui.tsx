// ACC-4 · F5-U1 — Accounting › Year end, Bank reconciliation, Clearing (desktop tabs + phone cards) and their dialogs (ACC4-CONTRACT.md §1, §2, §4).
// Screens only: every figure comes from the A1 selectors (accYearPreview, accBankRecon, accBankClearing …) and every change is an A1 command.
import React,{useMemo,useState} from 'react';
import {Lock,LockOpen,CheckCircle2,AlertTriangle,Upload,Download,Link2,Undo2,Ban,Plus,Wand2} from 'lucide-react';
import {useHcc} from '../../app/context';
import {saveTextFile,saveAndReport} from '../../app/native';
import {money,accYearPreview,accYearOf,accYearSlice,accYearLatestLocked,accFyBounds,accFyNext,accLedgerNameOf,accCfAccount,
  ACC_PROVISIONAL_TEXT,ACC_YEAR_REASON_MIN,accBankSlice,accBankLayouts,accBankLayout,accBankImportPlan,accBankAutoMatch,accBankBookLines,accBankMatchedRefs,
  accBankRecon,accBankReconCsv,accBankClearing,type BankStatementLayout} from '../../services/mock';
import {ListCard,KpiCard,ChipFilter} from '../../app/mobile/components';
import {Empty,dateLabel,shortDate} from '../../components/ui';
import {accUserName} from './parts';
import {acc4Gates,acc4Years,acc4DraftOf,acc4BankLedgers,acc4LineKey,acc4RowAmount,ACC4_BAND_LABEL,ACC4_BAND_PILL,ACC4_CLEARING_NAME} from './acc4-views';

const ledgerLabel=(s:ReturnType<typeof useHcc>['s'],code:string)=>`${code} · ${accLedgerNameOf(s,code.split('/')[0])}${code.includes('/')?' / '+code.split('/')[1]:''}`;
const Sub=({tabs,value,onChange,label}:{tabs:[string,string][];value:string;onChange:(v:string)=>void;label:string}):React.ReactElement=>
  <div className="filters acc4-sub" role="group" aria-label={label}>{tabs.map(([id,l])=><button type="button" key={id} className={value===id?'active':''} aria-pressed={value===id} onClick={()=>onChange(id)}>{l}</button>)}</div>;
const NotForRole=({what}:{what:string})=><Empty title="Not available for your role" body={what}/>;

/** Banner shown on the Accounting page while a year is reopened (contract §1.2): exports carry the provisional mark. */
export function ProvisionalBanner({years}:{years:string[]}){
  if(!years.length)return null;
  return <div className="info-note f-warn acc4-banner" role="status"><AlertTriangle size={16} aria-hidden/><span><strong>{ACC_PROVISIONAL_TEXT}.</strong> Financial year{years.length>1?'s':''} {years.join(', ')} reopened by the Owner — every Tally, CSV and Excel export of {years.length>1?'these years':'this year'} (and the next) is marked PROVISIONAL until the year is locked again.</span></div>;
}

/* ────────────────────────── Year end ────────────────────────── */
export function YearEndView({phone}:{phone?:boolean}){
  const {s,user,open,run,busy}=useHcc();const g=acc4Gates(user.role);
  const years=useMemo(()=>acc4Years(s),[s]);const [fy,setFy]=useState(years.at(-2)??years[0]);
  if(!g.year)return <NotForRole what="The year end is prepared by the Owner and the Accountant; the Owner locks it."/>;
  const y=accYearOf(s,fy),p=accYearPreview(s,fy),draft=acc4DraftOf(s,fy),open_=p.checks.filter(c=>!c.ok).length;
  const approved=accYearSlice(s).carryForwards.find(c=>c.id===y.carryForwardId);
  const shown=draft??approved,lines=shown?.lines??p.draft.lines;
  const status=y.status==='locked'?<span className="pill acc-st-locked"><Lock size={12} aria-hidden/>Locked</span>:y.status==='reopened'?<span className="pill orange"><LockOpen size={12} aria-hidden/>Reopened · provisional</span>
    :y.status==='closing'?<span className="pill blue">Closing · draft ready</span>:<span className="pill acc-st-open">Open</span>;
  const latest=accYearLatestLocked(s)===fy;
  const picker=phone?<ChipFilter<string> label="Financial year" value={fy} onChange={setFy} options={years.map(v=>({id:v,label:v}))}/>
    :<label className="acc-date">Financial year<select value={fy} onChange={e=>setFy(e.target.value)}>{years.map(v=><option key={v} value={v}>{v}</option>)}</select></label>;
  const actions=<div className="inline acc4-actions">
    {y.status!=='locked'&&<button type="button" className="btn" disabled={busy||y.status==='reopened'} onClick={()=>void run({type:'ACC_YEAR_CARRY_FORWARD',fy,key:`accy-cf:${fy}:${s.sequence}`},`Carry-forward for ${fy} drafted`)}>
      <Wand2 size={16}/> {draft?'Draft again':'Draft carry-forward'}</button>}
    {y.status==='open'||y.status==='closing'?(g.yearLock&&<button type="button" className="btn primary" disabled={busy||!p.ready||!draft} aria-describedby="acc4-year-why" onClick={()=>open({type:'acc-year-lock',id:fy})}><Lock size={16}/> Lock {fy}</button>):null}
    {y.status==='reopened'&&g.yearLock&&<button type="button" className="btn primary" disabled={busy||!p.ready} aria-describedby="acc4-year-why" onClick={()=>open({type:'acc-year-relock',id:fy})}><Lock size={16}/> Lock again</button>}
    {y.status==='locked'&&g.yearLock&&<button type="button" className="btn" disabled={busy||!latest} onClick={()=>open({type:'acc-year-reopen',id:fy})}><LockOpen size={16}/> Reopen…</button>}
    {!g.yearLock&&<span className="muted">The Owner locks, reopens and relocks the year (PERM-ACC-015).</span>}</div>;
  const why=<p id="acc4-year-why" className="muted">{y.status==='locked'?(latest?`Locked by ${accUserName(s,y.lockedBy??'')} on ${dateLabel((y.lockedAt??'').slice(0,10))}. Only the latest locked year can be reopened, and only while ${accFyNext(fy)} has no locked month.`:'Only the latest locked year can be reopened.')
    :open_?`${open_} check${open_===1?'':'s'} to clear before the year can be locked.`:!draft&&y.status!=='reopened'?'Every check is clear. Draft the carry-forward, then lock.':'Every check is clear.'}</p>;
  const checks=p.checks.map(c=>({...c}));
  const checklist=phone?checks.map(c=><ListCard key={c.id} tone={c.ok?'':'acc-m-ex-error'} title={c.label} sub={c.ok?'Clear':c.detail}
      right={c.ok?<CheckCircle2 size={20} className="acc-ok-icon" aria-hidden/>:<AlertTriangle size={20} className="acc-warn-icon" aria-hidden/>}/>)
    :<ul className="acc4-checks" aria-label="Year-end checks">{checks.map(c=><li key={c.id} className={c.ok?'ok':'bad'}>{c.ok?<CheckCircle2 size={16} className="acc-ok-icon" aria-hidden/>:<AlertTriangle size={16} className="acc-warn-icon" aria-hidden/>}
      <span><strong>{c.label}</strong>{!c.ok&&c.detail&&<small>{c.detail}</small>}</span><span className={'pill '+(c.ok?'green':'acc-sev-error')}>{c.ok?'Clear':'Open'}</span></li>)}</ul>;
  const dr=lines.reduce((a,l)=>a+l.dr,0),cr=lines.reduce((a,l)=>a+l.cr,0);
  const table=phone?<>{lines.map((l,i)=><ListCard key={i} title={accCfAccount(l)} sub={accLedgerNameOf(s,l.ledger)} right={<strong className="number">{money(l.dr||l.cr)} {l.dr?'Dr':'Cr'}</strong>}/>)}</>
    :<div className="table-wrap"><table className="acc-table" aria-label="Carry-forward lines"><thead><tr><th>Ledger</th><th>Name</th><th className="acc-num">Dr</th><th className="acc-num">Cr</th></tr></thead>
      <tbody>{lines.map((l,i)=><tr key={i}><td className="number">{accCfAccount(l)}</td><td>{accLedgerNameOf(s,l.ledger)}</td><td className="acc-num number">{l.dr?money(l.dr):'—'}</td><td className="acc-num number">{l.cr?money(l.cr):'—'}</td></tr>)}</tbody>
      <tfoot><tr><th colSpan={2}>Total</th><td className="acc-num number"><strong>{money(dr)}</strong></td><td className="acc-num number"><strong>{money(cr)}</strong></td></tr></tfoot></table></div>;
  return <div className={phone?'acc4-m':'padded acc4'}>
    {picker}
    <div className="acc4-head">{status}<span className="muted">{fy} · {dateLabel(accFyBounds(fy).from)} → {dateLabel(accFyBounds(fy).to)}</span></div>
    {y.status==='reopened'&&<p className="info-note f-warn" role="status"><AlertTriangle size={16} aria-hidden/><span>{ACC_PROVISIONAL_TEXT}: reopened by {accUserName(s,y.reopenedBy??'')} — “{y.reopenReason}”. Exports are provisional until the year is locked again.</span></p>}
    {actions}{why}
    <h3 className={phone?'m-group':'acc-h3'}>{phone?'CHECKS':'Checks before the lock'}</h3>{checklist}
    <h3 className={phone?'m-group':'acc-h3'}>{phone?'CARRY-FORWARD':`Carry-forward into ${accFyNext(fy)} ${shown?(draft?'(draft)':'(approved)'):'(preview — not saved)'}`}</h3>
    {phone&&<div className="acc-m-kpis"><KpiCard label="Profit to 3100" value={money(shown?.profitToCapital??p.draft.profitToCapital)} sub="P&L result"/><KpiCard label="Drawings to 3100" value={money(shown?.drawingsToCapital??p.draft.drawingsToCapital)} sub="3000 closed"/></div>}
    {!phone&&<p className="muted">Profit {money(shown?.profitToCapital??p.draft.profitToCapital)} and drawings {money(shown?.drawingsToCapital??p.draft.drawingsToCapital)} are closed to 3100 Capital. Income and expense ledgers do not carry forward. {dr===cr?'Debits equal credits.':'Debits do not equal credits.'}</p>}
    {lines.length?table:<Empty title="Nothing to carry forward" body="No balances in this year yet."/>}
    {y.history&&y.history.length>0&&<><h3 className={phone?'m-group':'acc-h3'}>{phone?'HISTORY':'Year history'}</h3><ul className="acc-hist">{y.history.map((h,i)=><li key={i}><strong>{h.action}</strong> · {accUserName(s,h.by)} · {h.at.replace('T',' ')}{h.reason?` — ${h.reason}`:''}</li>)}</ul></>}
  </div>;
}

/* ────────────────────────── Year dialogs ────────────────────────── */
/** 'acc-year-lock' · id = fy (Owner). Approves the draft carry-forward as the next year's opening entry. */
export function YearLockModal({id,relock}:{id?:string;relock?:boolean}){
  const {s,user,run,closeModal,busy}=useHcc();const fy=id||acc4Years(s).at(-2)||'';const g=acc4Gates(user.role);
  const p=accYearPreview(s,fy),draft=acc4DraftOf(s,fy);
  return <form className="form" onSubmit={e=>{e.preventDefault();void run(relock?{type:'ACC_YEAR_RELOCK',fy,key:`accy-relock:${fy}:${s.sequence}`}:{type:'ACC_YEAR_LOCK',fy,carryForwardId:draft?.id,key:`accy-lock:${fy}:${s.sequence}`},relock?`Year ${fy} locked again`:`Year ${fy} locked`,{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">OWNER ACTION</p><h2>{relock?'Lock':'Lock'} {fy}</h2>
      <p>{relock?'The carry-forward is regenerated and replaces the earlier one; the next year’s opening is replaced.':`The draft carry-forward becomes the opening entry of ${accFyNext(fy)}. After this every post, edit or void dated in ${fy} is refused; a correction is a journal in ${accFyNext(fy)}.`}</p></div>
    <p className="muted">{money(p.draft.debit)} Dr = {money(p.draft.credit)} Cr · profit {money(p.draft.profitToCapital)} and drawings {money(p.draft.drawingsToCapital)} to 3100 · {p.draft.lines.length} line(s).</p>
    {!p.ready&&<p className="f-why" role="alert">Not ready: {p.checks.filter(c=>!c.ok).map(c=>c.label).join('; ')}.</p>}
    {!relock&&!draft&&<p className="f-why" role="alert">Draft the carry-forward first.</p>}
    {!g.yearLock&&<p className="f-why">Only the Owner locks a financial year.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
      <button className="btn primary" disabled={busy||!g.yearLock||!p.ready||(!relock&&!draft)}><Lock size={16}/> Lock {fy}</button></div></form>;
}
export const YearRelockModal=({id}:{id?:string})=><YearLockModal id={id} relock/>;
/** 'acc-year-reopen' · id = fy (Owner, reason of at least 10 characters). */
export function YearReopenModal({id}:{id?:string}){
  const {s,user,run,closeModal,busy}=useHcc();const fy=id||accYearLatestLocked(s)||'';const [reason,setReason]=useState('');const g=acc4Gates(user.role);
  const ok=reason.trim().length>=ACC_YEAR_REASON_MIN;
  return <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'ACC_YEAR_REOPEN',fy,reason:reason.trim(),key:`accy-reopen:${fy}:${s.sequence}`},`Year ${fy} reopened · exports are provisional`,{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">OWNER ACTION</p><h2>Reopen {fy}</h2>
      <p>Only the latest locked year can be reopened, and only while {accFyNext(fy)} has no locked month. Every export of {fy} and {accFyNext(fy)} is marked “{ACC_PROVISIONAL_TEXT}” until the year is locked again. Issued invoice and credit-note numbers never change.</p></div>
    <label>Reason (at least {ACC_YEAR_REASON_MIN} characters)<textarea rows={3} required minLength={ACC_YEAR_REASON_MIN} maxLength={400} value={reason} onChange={e=>setReason(e.target.value)} enterKeyHint="done" placeholder="e.g. CA found a missed purchase bill of March"/></label>
    {!g.yearLock&&<p className="f-why">Only the Owner reopens a financial year.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!g.yearLock||!ok}><LockOpen size={16}/> Reopen year</button></div></form>;
}

/* ────────────────────────── Bank reconciliation ────────────────────────── */
type BankSub='import'|'match'|'unmatched'|'report'|'layouts';
export function BankView({phone}:{phone?:boolean}){
  const {s,user}=useHcc();const g=acc4Gates(user.role);
  const ledgers=useMemo(()=>acc4BankLedgers(s),[s]);const [ledger,setLedger]=useState(ledgers[0]??'');const [sub,setSub]=useState<BankSub>('match');
  if(!g.bank)return <NotForRole what="The bank reconciliation is for the Owner and the Accountant."/>;
  const picker=ledgers.length>1?(phone?<ChipFilter<string> label="Bank account" value={ledger} onChange={setLedger} options={ledgers.map(l=>({id:l,label:l.split('/')[1]??l}))}/>
    :<label className="acc-date">Bank account<select value={ledger} onChange={e=>setLedger(e.target.value)}>{ledgers.map(l=><option key={l} value={l}>{ledgerLabel(s,l)}</option>)}</select></label>):null;
  const tabs:[string,string][]=[['import','Import'],['match','Match board'],['unmatched','Unmatched'],['report','Report'],...(g.layout?[['layouts','Layouts'] as [string,string]]:[])];
  return <div className={phone?'acc4-m':'padded acc4'}>
    {picker}<Sub tabs={tabs} value={sub} onChange={v=>setSub(v as BankSub)} label="Bank reconciliation section"/>
    {sub==='import'&&<BankImport ledger={ledger} setLedger={setLedger} ledgers={ledgers} phone={phone}/>}
    {sub==='match'&&<BankMatchBoard ledger={ledger} phone={phone}/>}
    {sub==='unmatched'&&<BankUnmatched ledger={ledger} phone={phone}/>}
    {sub==='report'&&<BankReport ledger={ledger} phone={phone}/>}
    {sub==='layouts'&&g.layout&&<BankLayouts phone={phone}/>}
  </div>;
}
function BankImport({ledger,setLedger,ledgers,phone}:{ledger:string;setLedger:(l:string)=>void;ledgers:string[];phone?:boolean}){
  const {s,run,busy}=useHcc();const layouts=accBankLayouts(s).filter(l=>l.active);const [layoutId,setLayoutId]=useState(layouts[0]?.id??'generic');
  const [text,setText]=useState('');const [name,setName]=useState('');const [keep,setKeep]=useState(false);
  const lay=accBankLayout(s,layoutId);const imports=accBankSlice(s).imports.filter(i=>!ledger||i.bankLedger===ledger);
  const plan=useMemo(()=>text&&lay&&ledger?accBankImportPlan(s,ledger,text,lay):undefined,[s,text,lay,ledger]);
  const problem=plan?plan.error?.message??(plan.duplicateFile?'BANK_DUP_FILE: this statement file was already imported.':plan.lockedRow?`ACC_YEAR_LOCKED: row ${plan.lockedRow.line} is in a locked financial year.`:''):'';
  return <div className="acc4-import">
    <p className="muted">Choose the bank’s statement file (CSV). The layout says which column is which; the Owner keeps the layouts. A file that was imported before, a broken running balance or a row in a locked year is refused.</p>
    <div className="inline acc4-form">
      {ledgers.length>0?<label>Bank account<select value={ledger} onChange={e=>setLedger(e.target.value)}>{ledgers.map(l=><option key={l} value={l}>{ledgerLabel(s,l)}</option>)}</select></label>
        :<label>Bank account<input value={ledger} onChange={e=>setLedger(e.target.value.trim())} placeholder="1030/HDFC"/></label>}
      <label>Layout<select value={layoutId} onChange={e=>setLayoutId(e.target.value)}>{layouts.map(l=><option key={l.id} value={l.id}>{l.name}</option>)}</select></label>
      <label>Statement file<input type="file" accept=".csv,text/csv" onChange={async e=>{const f=e.target.files?.[0];if(!f)return;setName(f.name);setText(await f.text());}}/></label></div>
    {plan&&!problem&&<div className="info-note" role="status"><CheckCircle2 size={16} aria-hidden/><span>{plan.rows.length} row(s) {plan.rows[0]?.date} → {plan.rows.at(-1)?.date} · opening {money(plan.openingBalance)} · closing {money(plan.closingBalance)}{plan.overlaps.length?` · ${plan.overlaps.length} row(s) overlap an earlier import and are skipped`:''}</span></div>}
    {problem&&<p className="f-why" role="alert">{problem}</p>}
    {plan&&!problem&&plan.overlaps.length>0&&<label className="check"><input type="checkbox" checked={keep} onChange={e=>setKeep(e.target.checked)}/> Keep the overlapping rows too</label>}
    <button type="button" className="btn primary" disabled={busy||!text||!!problem||!/^1030\//.test(ledger)} onClick={()=>void run({type:'BANK_IMPORT',bankLedger:ledger,layoutId,fileName:name||'statement.csv',text,keepDuplicates:keep,key:`bank-imp:${ledger}:${s.sequence}`},'Statement imported',{after:()=>{setText('');setName('');}})}><Upload size={16}/> Import statement</button>
    <h3 className={phone?'m-group':'acc-h3'}>{phone?'IMPORTED STATEMENTS':'Imported statements'}</h3>
    {imports.length?(phone?imports.map(i=><ListCard key={i.id} title={i.fileName} sub={`${shortDate(i.from)} → ${shortDate(i.to)} · ${i.rows} rows`} right={<strong className="number">{money(i.closingBalance)}</strong>}/>)
      :<div className="table-wrap"><table className="acc-table"><thead><tr><th>File</th><th>From</th><th>To</th><th className="acc-num">Rows</th><th className="acc-num">Opening</th><th className="acc-num">Closing</th></tr></thead>
        <tbody>{imports.map(i=><tr key={i.id}><td>{i.fileName}<small className="number"> {i.id}</small></td><td>{dateLabel(i.from)}</td><td>{dateLabel(i.to)}</td><td className="acc-num number">{i.rows}</td><td className="acc-num number">{money(i.openingBalance)}</td><td className="acc-num number">{money(i.closingBalance)}</td></tr>)}</tbody></table></div>)
      :<Empty title="No statement imported yet" body="Choose a file above."/>}
  </div>;
}
/** Unmatched book lines and statement rows for a ledger, with the matched list (undo). */
function useBankSets(ledger:string){
  const {s}=useHcc();const x=accBankSlice(s);
  return useMemo(()=>{
    const rows=x.rows.filter(r=>r.bankLedger===ledger);const to=rows.map(r=>r.date).sort().at(-1)??s.date;const refs=accBankMatchedRefs(s);
    const book=accBankBookLines(s,ledger,to>s.date?to:s.date).filter(l=>!refs.has(acc4LineKey(l.ref)));
    const matches=x.matches.filter(m=>!m.undoneAt&&m.rowIds.some(id=>rows.some(r=>r.id===id)));
    return {rows,unRows:rows.filter(r=>r.status==='unmatched'),ignored:rows.filter(r=>r.status==='ignored'),book,matches,to};
  },[s,x,ledger]);
}
function BankMatchBoard({ledger,phone}:{ledger:string;phone?:boolean}){
  const {s,run,busy}=useHcc();const sets=useBankSets(ledger);const auto=useMemo(()=>accBankAutoMatch(s,ledger),[s,ledger]);
  const [pickR,setPickR]=useState<string[]>([]);const [pickL,setPickL]=useState<string[]>([]);
  const toggle=(set:string[],id:string)=>set.includes(id)?set.filter(x=>x!==id):[...set,id];
  const rowSum=sets.unRows.filter(r=>pickR.includes(r.id)).reduce((a,r)=>a+acc4RowAmount(r),0);
  const lineSum=sets.book.filter(l=>pickL.includes(acc4LineKey(l.ref))).reduce((a,l)=>a+l.amount,0);
  const canMatch=pickR.length>0&&pickL.length>0&&rowSum===lineSum;
  const doMatch=()=>void run({type:'BANK_MATCH',rowIds:pickR,refs:sets.book.filter(l=>pickL.includes(acc4LineKey(l.ref))).map(l=>l.ref),key:`bank-m:${ledger}:${s.sequence}`},'Matched by hand',{after:()=>{setPickR([]);setPickL([]);}});
  if(!sets.rows.length)return <Empty title="No statement rows" body="Import a statement in the Import section first."/>;
  const rowItem=(r:typeof sets.unRows[number])=>phone?<ListCard key={r.id} title={<><input type="checkbox" aria-label={`Select statement row ${r.narration}`} checked={pickR.includes(r.id)} onChange={()=>setPickR(toggle(pickR,r.id))}/> {r.narration}</>}
      sub={`${shortDate(r.date)}${r.ref?' · '+r.ref:''}`} right={<strong className="number">{money(Math.abs(acc4RowAmount(r)))} {r.credit?'in':'out'}</strong>}/>:null;
  return <>
    <div className="inline acc4-actions"><button type="button" className="btn" disabled={busy||!sets.unRows.length} onClick={()=>void run({type:'BANK_AUTO_MATCH',bankLedger:ledger,key:`bank-a:${ledger}:${s.sequence}`},'Auto-match done')}><Wand2 size={16}/> Auto-match</button>
      <span className="muted">{auto.proposals.length} match{auto.proposals.length===1?'':'es'} found · {auto.settlements.length} UPI / card settlement(s) to post · {auto.ambiguous.length} need your choice</span></div>
    <div className="inline acc4-actions"><button type="button" className="btn primary" disabled={busy||!canMatch} onClick={doMatch}><Link2 size={16}/> Match selected</button>
      <span className={canMatch||(!pickR.length&&!pickL.length)?'muted':'f-why'}>Rows {money(rowSum)} · book lines {money(lineSum)}{pickR.length&&pickL.length&&rowSum!==lineSum?` · differ by ${money(Math.abs(rowSum-lineSum))}`:''}</span></div>
    {phone?<><h3 className="m-group">STATEMENT ROWS NOT MATCHED</h3>{sets.unRows.map(rowItem)}
      <h3 className="m-group">BOOK LINES NOT CLEARED</h3>{sets.book.map(l=><ListCard key={acc4LineKey(l.ref)} title={<><input type="checkbox" aria-label={`Select book line ${l.no}`} checked={pickL.includes(acc4LineKey(l.ref))} onChange={()=>setPickL(toggle(pickL,acc4LineKey(l.ref)))}/> {l.no}</>}
        sub={l.narration} meta={shortDate(l.date)} right={<strong className="number">{money(Math.abs(l.amount))} {l.amount>0?'in':'out'}</strong>}/>)}</>
    :<div className="fit-cols acc4-board"><div><h3 className="acc-h3">Statement rows not matched ({sets.unRows.length})</h3>
        {sets.unRows.length?<div className="table-wrap"><table className="acc-table" aria-label="Statement rows not matched"><thead><tr><th/><th>Date</th><th>Narration</th><th className="acc-num">In / out</th></tr></thead>
          <tbody>{sets.unRows.map(r=><tr key={r.id}><td><input type="checkbox" aria-label={`Select statement row ${r.narration}`} checked={pickR.includes(r.id)} onChange={()=>setPickR(toggle(pickR,r.id))}/></td><td>{shortDate(r.date)}</td><td className="acc-wrap">{r.narration}{r.ref&&<small className="number"> {r.ref}</small>}</td>
            <td className="acc-num number">{r.credit?money(r.credit):'−'+money(r.debit)}</td></tr>)}</tbody></table></div>:<Empty title="Every row is matched or ignored"/>}</div>
      <div><h3 className="acc-h3">Book lines not cleared ({sets.book.length})</h3>
        {sets.book.length?<div className="table-wrap"><table className="acc-table" aria-label="Book lines not cleared"><thead><tr><th/><th>Date</th><th>Voucher</th><th className="acc-num">In / out</th></tr></thead>
          <tbody>{sets.book.map(l=><tr key={acc4LineKey(l.ref)}><td><input type="checkbox" aria-label={`Select book line ${l.no}`} checked={pickL.includes(acc4LineKey(l.ref))} onChange={()=>setPickL(toggle(pickL,acc4LineKey(l.ref)))}/></td><td>{shortDate(l.date)}</td><td className="acc-wrap">{l.no}<small> {l.narration}</small></td>
            <td className="acc-num number">{l.amount>0?money(l.amount):'−'+money(-l.amount)}</td></tr>)}</tbody></table></div>:<Empty title="Every book line is cleared"/>}</div></div>}
    <h3 className={phone?'m-group':'acc-h3'}>{phone?'MATCHED':`Matched (${sets.matches.length})`}</h3>
    {sets.matches.length?(phone?sets.matches.map(m=><ListCard key={m.id} title={m.id} sub={`${m.rule} · cleared ${shortDate(m.clearedDate)}`} meta={`${m.rowIds.length} row(s) · ${m.voucherLineRefs.length} line(s)`}
        right={<button type="button" className="btn small-btn" disabled={busy} onClick={()=>void run({type:'BANK_UNMATCH',matchId:m.id,key:`bank-u:${m.id}`},'Match undone')}><Undo2 size={14}/> Undo</button>}/>)
      :<div className="table-wrap"><table className="acc-table"><thead><tr><th>Match</th><th>Rule</th><th>Cleared</th><th className="acc-num">Rows</th><th className="acc-num">Lines</th><th/></tr></thead>
        <tbody>{sets.matches.map(m=><tr key={m.id}><td className="number">{m.id}</td><td>{m.rule}</td><td>{dateLabel(m.clearedDate)}</td><td className="acc-num number">{m.rowIds.length}</td><td className="acc-num number">{m.voucherLineRefs.length}</td>
          <td><button type="button" className="btn small-btn" disabled={busy} onClick={()=>void run({type:'BANK_UNMATCH',matchId:m.id,key:`bank-u:${m.id}`},'Match undone')}><Undo2 size={14}/> Undo</button></td></tr>)}</tbody></table></div>)
      :<p className="muted">Nothing matched yet.</p>}
  </>;
}
/** Statement rows with no book entry: ignore, create a voucher (charges, interest), post a UPI / card settlement (B8). */
function BankUnmatched({ledger,phone}:{ledger:string;phone?:boolean}){
  const {s,open}=useHcc();const sets=useBankSets(ledger);const auto=useMemo(()=>accBankAutoMatch(s,ledger),[s,ledger]);
  if(!sets.unRows.length&&!sets.ignored.length)return <Empty title="Nothing unmatched" body="Every statement row is matched."/>;
  const acts=(r:typeof sets.unRows[number])=><span className="inline acc4-row-actions">
    {r.credit>0&&<button type="button" className={'btn small-btn'+(auto.settlements.includes(r.id)?' primary':'')} onClick={()=>open({type:'acc-bank-settle',id:r.id})}>UPI / card settlement…</button>}
    <button type="button" className="btn small-btn" onClick={()=>open({type:'acc-bank-create',id:r.id})}><Plus size={14}/> Create voucher…</button>
    <button type="button" className="btn small-btn" onClick={()=>open({type:'acc-bank-ignore',id:r.id})}><Ban size={14}/> Ignore…</button></span>;
  return <>
    <p className="muted">These bank entries have no matching book line. Bank charges and interest are made into vouchers; a UPI or card settlement is posted as a journal against the clearing ledger; an unknown credit never goes to 9999 Suspense — it stays here until its ledger is known.</p>
    {phone?sets.unRows.map(r=><div key={r.id} className="acc4-card"><ListCard title={r.narration} sub={`${shortDate(r.date)}${r.ref?' · '+r.ref:''}`} meta={auto.settlements.includes(r.id)?'Looks like a UPI / card settlement':auto.ambiguous.includes(r.id)?'Two or more candidates — match by hand':undefined}
        right={<strong className="number">{money(Math.abs(acc4RowAmount(r)))} {r.credit?'in':'out'}</strong>}/>{acts(r)}</div>)
    :<div className="table-wrap"><table className="acc-table" aria-label="Unmatched statement rows"><thead><tr><th>Date</th><th>Narration</th><th className="acc-num">In / out</th><th>Note</th><th>Action</th></tr></thead>
      <tbody>{sets.unRows.map(r=><tr key={r.id}><td>{shortDate(r.date)}</td><td className="acc-wrap">{r.narration}{r.ref&&<small className="number"> {r.ref}</small>}</td><td className="acc-num number">{r.credit?money(r.credit):'−'+money(r.debit)}</td>
        <td><small>{auto.settlements.includes(r.id)?'Looks like a UPI / card settlement':auto.ambiguous.includes(r.id)?'Two or more candidates':''}</small></td><td>{acts(r)}</td></tr>)}</tbody></table></div>}
    {sets.ignored.length>0&&<><h3 className={phone?'m-group':'acc-h3'}>{phone?'IGNORED':`Ignored (${sets.ignored.length})`}</h3>
      {sets.ignored.map(r=><p key={r.id} className="muted">{shortDate(r.date)} · {r.narration} · {money(Math.abs(acc4RowAmount(r)))}</p>)}</>}
  </>;
}
function BankReport({ledger,phone}:{ledger:string;phone?:boolean}){
  const {s,toast}=useHcc();const [asOf,setAsOf]=useState(s.date);
  const r=useMemo(()=>accBankRecon(s,ledger,asOf),[s,ledger,asOf]);const ok=r.difference===0;
  const exportCsv=()=>void saveAndReport(saveTextFile(`HCC-bank-reconciliation-${ledger.replace('/','-')}-${asOf}.csv`,accBankReconCsv(r),'text/csv',true),toast,'Reconciliation CSV saved','the reconciliation');
  return <>
    <div className="inline acc4-actions"><label className="acc-date">As of<input type="date" value={asOf} max={s.date} onChange={e=>e.target.value&&setAsOf(e.target.value)}/></label>
      <button type="button" className="btn" onClick={exportCsv}><Download size={16}/> Export CSV</button></div>
    <div className={'info-note acc4-diff '+(ok?'':'f-warn')} role="status" id="acc4-recon-diff">{ok?<CheckCircle2 size={16} aria-hidden/>:<AlertTriangle size={16} aria-hidden/>}
      <span><strong>Difference {money(r.difference)}</strong> {ok?'— the bank statement agrees with the books.':'— must be zero; check the unmatched lists.'}</span></div>
    <div className="table-wrap"><table className="acc-table" aria-label="Bank reconciliation"><tbody>
      <tr><th scope="row">Balance per books ({ledger})</th><td className="acc-num number">{money(r.booksBalance)}</td></tr>
      <tr><th scope="row">Add: issued, not yet debited by the bank ({r.issuedNotPresented.length})</th><td className="acc-num number">{money(r.issuedTotal)}</td></tr>
      <tr><th scope="row">Less: deposited, not yet credited ({r.depositsNotCredited.length})</th><td className="acc-num number">−{money(r.depositsTotal)}</td></tr>
      <tr><th scope="row">Bank-only items not yet booked ({r.bankOnly.length})</th><td className="acc-num number">{money(r.bankOnlyTotal)}</td></tr>
      <tr><th scope="row">Balance per statement (computed)</th><td className="acc-num number">{money(r.computedStatement)}</td></tr>
      <tr><th scope="row">Balance per statement (bank)</th><td className="acc-num number"><strong>{money(r.statementBalance)}</strong></td></tr></tbody></table></div>
    {[['Issued, not presented',r.issuedNotPresented.map(l=>({k:l.no,d:l.date,n:l.narration,a:-l.amount}))],['Deposited, not credited',r.depositsNotCredited.map(l=>({k:l.no,d:l.date,n:l.narration,a:l.amount}))],
      ['Bank only',r.bankOnly.map(b=>({k:b.id,d:b.date,n:b.narration,a:b.credit-b.debit}))]].map(([t,rows])=>(rows as {k:string}[]).length>0&&<React.Fragment key={t as string}>
        <h3 className={phone?'m-group':'acc-h3'}>{phone?(t as string).toUpperCase():t as string}</h3>
        {(rows as {k:string;d:string;n:string;a:number}[]).map(x=><ListCard key={x.k} title={x.n} sub={`${shortDate(x.d)} · ${x.k}`} right={<strong className="number">{money(Math.abs(x.a))}</strong>}/>)}</React.Fragment>)}
  </>;
}
function BankLayouts({phone}:{phone?:boolean}){
  const {s,open}=useHcc();const ls=accBankLayouts(s);
  return <>
    <div className="inline acc4-actions"><button type="button" className="btn primary" onClick={()=>open({type:'acc-bank-layout',id:'new'})}><Plus size={16}/> New layout…</button><span className="muted">Statement layouts map a bank’s columns (Owner, PERM-ACC-017). The owner’s real bank becomes a new row; no code change.</span></div>
    {phone&&ls.map(l=><ListCard key={l.id} title={l.name} sub={`${l.bankLedger} · ${l.dateFormat} · ${l.delimiter==='\t'?'tab':l.delimiter}`} meta={l.active?'Active':'Inactive'} onClick={()=>open({type:'acc-bank-layout',id:l.id})}/>)}
    {!phone&&<div className="table-wrap"><table className="acc-table" aria-label="Statement layouts"><thead><tr><th>Name</th><th>Bank ledger</th><th>Date format</th><th>Columns</th><th>Status</th><th/></tr></thead>
      <tbody>{ls.map(l=><tr key={l.id}><td><strong>{l.name}</strong><small className="number"> {l.id}</small></td><td className="number">{l.bankLedger}</td><td>{l.dateFormat}</td>
        <td className="acc-wrap"><small>{Object.entries(l.columns).filter(([,v])=>v).map(([k,v])=>`${k}: ${v}`).join(' · ')}</small></td><td>{l.active?<span className="pill green">Active</span>:<span className="pill">Inactive</span>}</td>
        <td><button type="button" className="btn small-btn" onClick={()=>open({type:'acc-bank-layout',id:l.id})}>Edit…</button></td></tr>)}</tbody></table></div>}
  </>;
}

/* ────────────────────────── Bank dialogs ────────────────────────── */
const rowOf=(s:ReturnType<typeof useHcc>['s'],id?:string)=>accBankSlice(s).rows.find(r=>r.id===id);
/** 'acc-bank-ignore' · id = statement row id (reason required). */
export function BankIgnoreModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const r=rowOf(s,id);const [reason,setReason]=useState('');
  if(!r)return <Empty title="Statement row not found"/>;
  return <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'BANK_IGNORE',rowId:r.id,reason:reason.trim(),key:`bank-i:${r.id}`},'Statement row ignored',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">BANK RECONCILIATION</p><h2>Ignore this statement row</h2><p>{shortDate(r.date)} · {r.narration} · {money(Math.abs(acc4RowAmount(r)))}. It stays in the list of ignored rows with your reason.</p></div>
    <label>Reason (required)<textarea rows={2} required minLength={3} value={reason} onChange={e=>setReason(e.target.value)} enterKeyHint="done" placeholder="e.g. Duplicate line in the bank’s file"/></label>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||reason.trim().length<3}><Ban size={16}/> Ignore row</button></div></form>;
}
/** 'acc-bank-create' · id = statement row id: bank charge (5080) or interest / other credit (4960), matched at once. */
export function BankCreateModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const r=rowOf(s,id);const out=!!r&&r.debit>0;const [ledger,setLedger]=useState(out?'5080':'4960');const [narration,setNarration]=useState('');
  if(!r)return <Empty title="Statement row not found"/>;
  return <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'BANK_CREATE_VOUCHER',rowId:r.id,ledger,narration:narration.trim()||undefined,key:`bank-c:${r.id}`},'Voucher created and matched',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">BANK RECONCILIATION</p><h2>Create a voucher from this row</h2><p>{shortDate(r.date)} · {r.narration} · {money(Math.abs(acc4RowAmount(r)))} {out?'paid out':'received'}. The voucher posts {out?`Dr ${ledger} · Cr ${r.bankLedger}`:`Dr ${r.bankLedger} · Cr ${ledger}`} and is matched to this row.</p></div>
    <label>Ledger<input value={ledger} onChange={e=>setLedger(e.target.value.trim())} inputMode="numeric" required pattern="\d{4}(/[A-Za-z0-9._-]+)?"/></label>
    <p className="muted">{out?'5080 bank charges':'4960 interest and other income'} is the usual choice. 9999 Suspense is refused.</p>
    <label>Narration (optional)<input value={narration} onChange={e=>setNarration(e.target.value)} maxLength={300} enterKeyHint="done"/></label>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!ledger||ledger==='9999'}><Plus size={16}/> Create voucher</button></div></form>;
}
const toP=(t:string)=>Math.round(Number(t||'0')*100);
/** 'acc-bank-settle' · id = statement row id: B8 UPI / card settlement journal (net = the credit; gross − MDR − GST on MDR = net). */
export function BankSettleModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const r=rowOf(s,id);const [clearing,setClearing]=useState<'1010'|'1020'>('1010');
  const [gross,setGross]=useState(r?String(r.credit/100):'');const [mdr,setMdr]=useState('0');const [gst,setGst]=useState('0');const [itc,setItc]=useState(false);
  if(!r)return <Empty title="Statement row not found"/>;
  const diff=toP(gross)-toP(mdr)-toP(gst)-r.credit;
  return <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'BANK_SETTLEMENT_POST',rowId:r.id,clearing,gross:toP(gross),mdr:toP(mdr),mdrGst:toP(gst),itc,key:`bank-s:${r.id}`},'Settlement journal posted and matched',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">BANK RECONCILIATION · B8</p><h2>Post a UPI / card settlement</h2><p>{shortDate(r.date)} · {r.narration} · bank credit {money(r.credit)}. Dr {r.bankLedger} (net) · Dr 5080 (charge{itc?'; its GST to 1300':' with its GST'}) · Cr {clearing} (gross).</p></div>
    <label>Clearing ledger<select value={clearing} onChange={e=>setClearing(e.target.value as '1010'|'1020')}><option value="1010">1010 · UPI clearing</option><option value="1020">1020 · Card clearing</option></select></label>
    <label>Gross tenders settled (₹)<input inputMode="decimal" value={gross} onChange={e=>setGross(e.target.value)} required/></label>
    <label>Charge, MDR (₹)<input inputMode="decimal" value={mdr} onChange={e=>setMdr(e.target.value)}/></label>
    <label>GST on the charge (₹)<input inputMode="decimal" value={gst} onChange={e=>setGst(e.target.value)}/></label>
    <label className="check"><input type="checkbox" checked={itc} onChange={e=>setItc(e.target.checked)}/> Input credit allowed on the charge’s GST</label>
    {diff!==0&&<p className="f-why" role="alert">BANK_SETTLEMENT_SUM: gross − charge − GST must equal the bank credit {money(r.credit)} (differs by {money(Math.abs(diff))}).</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||diff!==0||toP(gross)<=0}>Post settlement</button></div></form>;
}
/** 'acc-bank-layout' · id = layout id | 'new' (Owner, PERM-ACC-017). */
export function BankLayoutModal({id}:{id?:string}){
  const {s,user,run,closeModal,busy}=useHcc();const g=acc4Gates(user.role);const cur=id&&id!=='new'?accBankLayout(s,id):undefined;
  const [f,setF]=useState<BankStatementLayout>(()=>cur?{...cur,columns:{...cur.columns}}:{id:'',name:'',bankLedger:'1030/',delimiter:',',headerRows:1,dateFormat:'DD/MM/YYYY',columns:{date:'date',narration:'narration',ref:'ref',debit:'debit',credit:'credit',balance:'balance'},active:true});
  const col=(k:keyof BankStatementLayout['columns'],l:string)=><label key={k}>{l}<input value={f.columns[k]??''} onChange={e=>setF({...f,columns:{...f.columns,[k]:e.target.value||undefined}})} placeholder="column header"/></label>;
  return <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'BANK_LAYOUT_UPSERT',layout:{...f,id:f.id.trim(),name:f.name.trim()},key:`bank-l:${f.id}:${s.sequence}`},'Statement layout saved',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">OWNER MASTER</p><h2>{cur?'Edit':'New'} statement layout</h2><p>Map the columns of the bank’s CSV by their header text. Debit and credit columns, or one amount column.</p></div>
    <label>Id (lower-case, dashes)<input value={f.id} disabled={!!cur} onChange={e=>setF({...f,id:e.target.value})} required pattern="[a-z0-9][a-z0-9\-]{0,39}"/></label>
    <label>Name<input value={f.name} onChange={e=>setF({...f,name:e.target.value})} required maxLength={60}/></label>
    <label>Bank ledger<input value={f.bankLedger} onChange={e=>setF({...f,bankLedger:e.target.value.trim()})} required placeholder="1030/HDFC"/></label>
    <label>Date format<select value={f.dateFormat} onChange={e=>setF({...f,dateFormat:e.target.value as BankStatementLayout['dateFormat']})}>{['DD/MM/YYYY','DD-MM-YYYY','YYYY-MM-DD','DD-MMM-YYYY'].map(v=><option key={v}>{v}</option>)}</select></label>
    <label>Delimiter<select value={f.delimiter} onChange={e=>setF({...f,delimiter:e.target.value as BankStatementLayout['delimiter']})}><option value=",">Comma</option><option value=";">Semicolon</option><option value={'\t'}>Tab</option></select></label>
    <label>Header rows<input type="number" min={1} max={10} value={f.headerRows} onChange={e=>setF({...f,headerRows:Number(e.target.value)})}/></label>
    {col('date','Date column')}{col('narration','Narration column')}{col('ref','Reference column')}{col('debit','Debit column')}{col('credit','Credit column')}{col('amount','Single amount column')}{col('drCrFlag','Dr / Cr flag column')}{col('balance','Balance column')}
    <label className="check"><input type="checkbox" checked={f.active} onChange={e=>setF({...f,active:e.target.checked})}/> Active</label>
    {!g.layout&&<p className="f-why">Only the Owner keeps the layouts.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!g.layout}>Save layout</button></div></form>;
}

/* ────────────────────────── Clearing ────────────────────────── */
/** 1010 UPI / 1020 card clearing: tenders taken less settlements, ageing of unsettled tender days (over 3 days amber, over 7 red). Read-only (Manager may read). */
export function ClearingView({phone}:{phone?:boolean}){
  const {s,user}=useHcc();const g=acc4Gates(user.role);
  const views=useMemo(()=>(['1010','1020'] as const).map(c=>accBankClearing(s,c,s.date)),[s]);
  if(!g.clearing)return <NotForRole what="The clearing view is for the Owner, Manager and Accountant."/>;
  return <div className={phone?'acc4-m':'padded acc4'}>
    <p className="muted">Tenders taken by UPI and card sit in a clearing ledger until the bank settles them. Settlements clear the oldest tender day first. Both must be zero at the year lock.</p>
    <div className={phone?'acc-m-kpis':'acc4-kpis'}>{views.map(v=><KpiCard key={v.code} label={`${v.code} · ${ACC4_CLEARING_NAME[v.code]}`} value={money(v.balance)} sub={v.open.length?`${v.open.length} unsettled tender day(s)`:'Settled'}/>)}</div>
    {views.map(v=><section key={v.code} aria-label={ACC4_CLEARING_NAME[v.code]}>
      <h3 className={phone?'m-group':'acc-h3'}>{phone?`${v.code} · ${ACC4_CLEARING_NAME[v.code].toUpperCase()}`:`${v.code} · ${ACC4_CLEARING_NAME[v.code]} — unsettled tender days`}</h3>
      {v.open.length?(phone?v.open.map(o=><ListCard key={o.date} tone={o.band==='red'?'acc-m-ex-error':o.band==='amber'?'acc-m-ex-warning':''} title={dateLabel(o.date)} sub={`${o.age} day${o.age===1?'':'s'} unsettled`}
          right={<><strong className="number">{money(o.open)}</strong><br/><span className={'pill '+ACC4_BAND_PILL[o.band]}>{ACC4_BAND_LABEL[o.band]}</span></>}/>)
        :<div className="table-wrap"><table className="acc-table"><thead><tr><th>Tender day</th><th className="acc-num">Unsettled</th><th className="acc-num">Days</th><th>Ageing</th></tr></thead>
          <tbody>{v.open.map(o=><tr key={o.date}><td>{dateLabel(o.date)}</td><td className="acc-num number">{money(o.open)}</td><td className="acc-num number">{o.age}</td><td><span className={'pill '+ACC4_BAND_PILL[o.band]}>{ACC4_BAND_LABEL[o.band]}</span></td></tr>)}</tbody></table></div>)
        :<p className="muted"><CheckCircle2 size={14} aria-hidden/> Nothing unsettled.</p>}
      {!phone&&v.days.length>0&&<details className="acc4-days"><summary>By day ({v.days.length})</summary><div className="table-wrap"><table className="acc-table"><thead><tr><th>Date</th><th className="acc-num">Tenders</th><th className="acc-num">Settled</th><th className="acc-num">Balance</th></tr></thead>
        <tbody>{v.days.map(d=><tr key={d.date}><td>{dateLabel(d.date)}</td><td className="acc-num number">{money(d.tenders)}</td><td className="acc-num number">{money(d.settled)}</td><td className="acc-num number">{money(d.balance)}</td></tr>)}</tbody></table></div></details>}
    </section>)}
  </div>;
}
