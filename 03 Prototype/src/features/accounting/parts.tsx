// Module 20 · Accounting automation — shared UI pieces (desktop + phone) and dialogs.
import React,{useState} from 'react';
import {Lock,LockOpen,Download,AlertTriangle,CheckCircle2,Info,Link2} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money,accVouchers,accVoucherTotals,accLedgerName,accSourceOf,accLockBlocker,accLockOf,accDaySummary,accCsv,rbacCanExportVouchers,accMapRows,accMapping,
  ACC_CHART,ACC_SUSPENSE,ACC_LOCK_ROLES,type ACCVoucher,type ACCDayStatus,type ACCException} from '../../services/mock';
import {Empty,dateLabel,shortDate,clockTime} from '../../components/ui';
import {downloadCsv} from '../reports/Reports';
import {accChart,accGroups,accGroupLegacy,accOpening} from '../../services/mock';
import type {AccLedgerUpsert,AccLedgerRetire,AccGroupUpsert,AccOpeningSave} from './commands';
import {LedgerPicker,ledgerOptions,toPaise,fromPaise,fyStart} from './shared';

export const accMoney=(n:number)=>n?money(n):'—';
export function AccStatusPill({status}:{status:ACCDayStatus}){
  return <span className={'pill acc-st-'+status.toLowerCase()}>{status==='Locked'?<Lock size={12} aria-hidden/>:null}{status==='Open'?'Open · provisional':status==='Closed'?'Closed · not locked':'Locked'}</span>;
}
export function AccSeverity({x}:{x:ACCException}){
  const Icon=x.severity==='error'?AlertTriangle:x.severity==='warning'?AlertTriangle:Info;
  return <span className={'pill acc-sev-'+x.severity}><Icon size={12} aria-hidden/>{x.severity==='error'?'Blocks lock':x.severity==='warning'?'Warning':'Info'}</span>;
}
export const accUserName=(s:ReturnType<typeof useHcc>['s'],id:string)=>s.users.find(u=>u.id===id)?.name??id;
/** Permission helpers for the page (coarse permissions + lock roles). */
export function useAccPerms(){
  const {s,uid,user,permission}=useHcc();
  return {canLock:permission('reports')&&ACC_LOCK_ROLES.includes(user.role),canUnlock:permission('reports')&&permission('admin'),canMap:permission('admin'),s,uid,
    canChart:permission('admin')&&user.role==='Owner', // contract §7 PERM-ACC-007: chart of accounts = Owner
    canOpeningEnter:permission('reports')&&ACC_LOCK_ROLES.includes(user.role), // PERM-ACC-008: Owner, Manager, Accountant
    canOpeningApprove:user.role==='Owner'}; // PERM-ACC-009
}
export function accExport(s:ReturnType<typeof useHcc>['s'],date:string,toast:(t:string)=>void,uid:string){
  if(!rbacCanExportVouchers(s,uid)){toast('Your role cannot export vouchers.');return;} // DEC-063 G13
  const vs=accVouchers(s,date);
  downloadCsv(`HCC-Demo-vouchers-${date}_${date}.csv`,accCsv(vs));
  toast(`Generic voucher CSV downloaded · ${vs.length} voucher${vs.length===1?'':'s'} · Tally/other mapping later`);
}
/** Lock / unlock action button for the selected date (desktop header and phone footer). */
export function AccLockButton({date,primary=true}:{date:string;primary?:boolean}){
  const {s,open}=useHcc();const {canLock,canUnlock}=useAccPerms();
  const locked=!!accLockOf(s,date),why=accLockBlocker(s,date);
  if(locked)return canUnlock?<button className="btn" onClick={()=>open({type:'acc-unlock',id:date})}><LockOpen size={16}/> Unlock period</button>:null;
  if(!canLock)return null;
  return <button className={'btn'+(primary?' primary':'')} disabled={!!why} title={why||undefined} aria-describedby={why?'acc-lock-why':undefined} onClick={()=>open({type:'acc-lock',id:date})}><Lock size={16}/> Lock period</button>;
}
export function AccExportButton({date,primary=false}:{date:string;primary?:boolean}){
  const {s,uid,toast}=useHcc();const n=accVouchers(s,date).length;
  if(!rbacCanExportVouchers(s,uid))return null;
  return <button className={'btn'+(primary?' primary':'')} disabled={!n} onClick={()=>accExport(s,date,toast,uid)}><Download size={16}/> Export CSV</button>;
}

// ── Dialogs ──
const parseRef=(id?:string)=>{const [date,no]=(id??'').split('|');return {date,no};};
/** 'acc-voucher' · id = "<date>|<voucher no>" */
export function AccVoucherModal({id}:{id?:string}){
  const {s,closeModal,booking,display}=useHcc();const {date,no}=parseRef(id);
  const v=accVouchers(s,date).find(x=>x.no===no);
  if(!v)return <Empty title="Voucher not found" body="It may have changed since the list was opened (vouchers are re-derived until the day is locked)."/>;
  const t=accVoucherTotals(v),src=accSourceOf(s,v),locked=!!accLockOf(s,date);
  return <><div className="modal-heading"><p className="eyebrow">{v.type.toUpperCase()} VOUCHER · {locked?'LOCKED SNAPSHOT':'PROVISIONAL'}</p><h2 className="number">{v.no}</h2><p>{dateLabel(v.date)} · {v.narration}</p></div>
    {display.isMobile?<div className="acc-m-entries">{v.entries.map((e,i)=><div key={i}><span><strong>{accLedgerName(e.account)}</strong><small>{e.account}{e.account===ACC_SUSPENSE?' · unmapped':''}</small></span>
        <b className="number">{e.debit?'Dr '+money(e.debit):'Cr '+money(e.credit)}</b></div>)}
      <div className="acc-total"><span><strong>Total</strong></span><b className="number">Dr {money(t.debit)} · Cr {money(t.credit)}</b></div></div>
    :<div className="table-wrap acc-entries"><table><thead><tr><th>Ledger</th><th className="acc-num">Debit</th><th className="acc-num">Credit</th></tr></thead>
      <tbody>{v.entries.map((e,i)=><tr key={i}><td><strong>{accLedgerName(e.account)}</strong><small>{e.account}{e.account===ACC_SUSPENSE?' · unmapped':''}</small></td><td className="acc-num number">{accMoney(e.debit)}</td><td className="acc-num number">{accMoney(e.credit)}</td></tr>)}
        <tr className="acc-total"><td><strong>Total</strong></td><td className="acc-num number"><strong>{money(t.debit)}</strong></td><td className="acc-num number"><strong>{money(t.credit)}</strong></td></tr></tbody></table></div>}
    <p className="acc-balance-line">{t.debit===t.credit?<span className="pill green"><CheckCircle2 size={12}/>Balanced</span>:<span className="pill acc-sev-error"><AlertTriangle size={12}/>Unbalanced</span>}
      {v.unmapped.length>0&&<span className="pill acc-sev-error">Unmapped: {v.unmapped.join(', ')}</span>}</p>
    <h3 className="acc-h3">Source record</h3>
    <div className="summary-list acc-source">
      <div><span>Source ID</span><strong className="number">{v.source.id}</strong></div>
      <div><span>Kind</span><strong>{v.source.kind} · {v.source.sub}</strong></div>
      {src.line&&<div><span>Folio line</span><strong>{src.line.label} · {money(src.line.amount)} · tax {src.line.taxBps/100}%</strong></div>}
      {src.receipt&&<div><span>Receipt</span><strong>{src.receipt.method} · {money(src.receipt.amount)} · {src.receipt.reference}</strong></div>}
      {src.expense&&<div><span>Expense</span><strong>{src.expense.vendor||src.expense.description||'—'} · {src.expense.category} · {src.expense.status}</strong></div>}
      {src.posReceipt&&<div><span>Outlet receipt</span><strong>{src.posReceipt.order} · {src.posReceipt.method} · {money(src.posReceipt.amount)} · {src.posReceipt.reference}</strong></div>}
      {v.source.kind==='closing'&&<div><span>Closing record</span><strong>Cash difference accepted for {shortDate(date)}</strong></div>}
      {(src.line||src.receipt)===undefined&&v.source.kind!=='closing'&&!src.expense&&!src.posReceipt&&<div><span>Status</span><strong>Source record no longer present (snapshot kept)</strong></div>}
    </div>
    <div className="modal-actions">{src.booking&&<button className="btn" onClick={()=>booking(src.booking!.id)}><Link2 size={16}/> Open folio {src.booking.id}</button>}<button className="btn primary" onClick={closeModal}>Done</button></div></>;
}
/** 'acc-lock' · id = business date */
export function AccLockModal({id}:{id?:string}){
  const {s,run,closeModal}=useHcc();const date=id??s.date;const sum=accDaySummary(s,date),why=accLockBlocker(s,date);
  const failing=sum.checks.filter(c=>!c.ok);
  return <><div className="modal-heading"><p className="eyebrow">ACCOUNTING PERIOD</p><h2>Lock {dateLabel(date)}?</h2>
    <p>The {sum.count} derived voucher{sum.count===1?'':'s'} for this closed business day are frozen as a snapshot. Later mapping changes will not alter them; only the owner can unlock (with a reason).</p></div>
    <div className="summary-list"><div><span>Vouchers</span><strong>{sum.count}</strong></div><div><span>Total debits</span><strong className="number">{money(sum.debit)}</strong></div>
      <div><span>Total credits</span><strong className="number">{money(sum.credit)}</strong></div><div><span>Reconciliation</span><strong>{failing.length?`${failing.length} check(s) differ`:`All ${sum.checks.length} checks agree`}</strong></div></div>
    {why&&<p className="f-why" role="alert" id="acc-lock-why">{why}</p>}
    {!why&&failing.length>0&&<div className="info-note"><AlertTriangle size={16}/>Some figures differ from Reports: {failing.map(c=>c.label).join(', ')}. You can still lock; review the Reconciliation tab first.</div>}
    <div className="modal-actions"><button className="btn" onClick={closeModal}>Cancel</button>
      <button className="btn primary" disabled={!!why} onClick={()=>run({type:'ACC_LOCK',date},`Accounting period ${shortDate(date)} locked`,{close:true})}><Lock size={16}/> Lock period</button></div></>;
}
/** 'acc-unlock' · id = business date (owner) */
export function AccUnlockModal({id}:{id?:string}){
  const {s,run,closeModal}=useHcc();const date=id??s.date;const lock=accLockOf(s,date);const [reason,setReason]=useState('');
  if(!lock)return <Empty title="This date is not locked"/>;
  return <form className="form" onSubmit={e=>{e.preventDefault();run({type:'ACC_UNLOCK',date,reason:reason.trim()},`Accounting period ${shortDate(date)} unlocked`,{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">OWNER ACTION</p><h2>Unlock {dateLabel(date)}</h2><p>Locked by {accUserName(s,lock.lockedBy)} at {clockTime(lock.lockedAt)} on {shortDate(lock.lockedAt.slice(0,10))}. Vouchers return to live derivation with the current mapping.</p></div>
    <label>Reason (required)<textarea name="reason" rows={3} required minLength={3} value={reason} onChange={e=>setReason(e.target.value)} enterKeyHint="done" placeholder="e.g. Reclassify laundry expense"/></label>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" type="submit" disabled={reason.trim().length<3}><LockOpen size={16}/> Unlock</button></div></form>;
}
/** 'acc-map' · id = mapping key (admin) */
export function AccMapModal({id}:{id?:string}){
  const {s,run,closeModal}=useHcc();const {canMap}=useAccPerms();
  const row=accMapRows(s).find(r=>r.key===id);const [account,setAccount]=useState(row?.account&&row.account!==ACC_SUSPENSE?row.account:'');
  if(!row)return <Empty title="Classification not found"/>;
  return <form className="form" onSubmit={e=>{e.preventDefault();run({type:'ACC_MAP_SET',source:row.key,account},'Mapping updated · locked periods unchanged',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">{row.group.toUpperCase()}</p><h2>Map “{row.value}”</h2>
      <p>Currently {row.mapped?<strong>{row.account} {accLedgerName(row.account)}</strong>:<strong>unmapped (posts to Suspense)</strong>}{row.defaultAccount?` · default ${row.defaultAccount} ${accLedgerName(row.defaultAccount)}`:''}. Used by {row.uses} source record{row.uses===1?'':'s'}. Changes apply to unlocked dates only.</p></div>
    <label>Ledger<select name="account" value={account} disabled={!canMap} onChange={e=>setAccount(e.target.value)}>
      <option value="">Choose a ledger…</option>
      {ACC_CHART.filter(a=>a.code!==ACC_SUSPENSE).map(a=><option key={a.code} value={a.code}>{a.code} · {a.name} ({a.group})</option>)}</select></label>
    {!canMap&&<p className="f-why">Only an administrator (Owner) can change mappings.</p>}
    <div className="modal-actions">
      {row.changed&&canMap&&<button type="button" className="btn" onClick={()=>run({type:'ACC_MAP_RESET',source:row.key},'Mapping reset to default',{close:true})}>Reset to default</button>}
      <button type="button" className="btn" onClick={closeModal}>Cancel</button>
      <button className="btn primary" type="submit" disabled={!canMap||!account||account===accMapping(s)[row.key]}>Save mapping</button></div></form>;
}
export function accVoucherDebit(v:ACCVoucher){return accVoucherTotals(v).debit;}

// ── ACC-1 dialogs: chart of accounts (Owner) and opening-balance lines (contract §2, §5, §7) ──
const isNewId=(id?:string)=>!id||id==='new'||id.startsWith('new:');
/** 'acc-ledger' · id = ledger code, 'new' or 'new:<group id>' (Owner edits; everyone else reads) */
export function AccLedgerModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const {canChart}=useAccPerms();
  const chart=accChart(s),groups=accGroups(s),isNew=isNewId(id),cur=isNew?undefined:chart.find(l=>l.code===id);
  const [code,setCode]=useState(cur?.code??'');const [name,setName]=useState(cur?.name??'');
  const [group,setGroup]=useState(cur?.group??(id?.startsWith('new:')?id.slice(4):''));
  const [gst,setGst]=useState<'none'|'input'|'output'>(cur?.gst??'none');const [tally,setTally]=useState(cur?.tallyName??'');const [note,setNote]=useState(cur?.note??'');
  if(!isNew&&!cur)return <Empty title="Ledger not found"/>;
  // Same Tally nature (Asset / Liability / Income / Expense / Suspense of the primary group, coordinator ruling 2) for a built-in.
  const curNature=cur?accGroupLegacy(s,cur.group):undefined;
  const choices=cur?.system?groups.filter(g=>accGroupLegacy(s,g.id)===curNature):groups.filter(g=>g.id!=='suspense');
  const codeBad=isNew?(!/^\d{4}$/.test(code)?'Use a 4-digit code.':chart.some(l=>l.code===code)?'This code is already used.':''):'';
  const ok=canChart&&!codeBad&&name.trim().length>=2&&!!group;
  return <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'ACC_LEDGER_UPSERT',...(isNew?{create:true as const}:{}),code,name:name.trim(),group,gst,tallyName:tally.trim(),note:note.trim()} satisfies AccLedgerUpsert,'Ledger saved',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">CHART OF ACCOUNTS{cur?.system?' · BUILT-IN':''}</p><h2>{cur?`${cur.code} · ${cur.name}`:'Add ledger'}</h2>
      {cur?.control&&<p>Control ledger: it holds party sub-ledgers and is never posted to directly.</p>}{cur&&!cur.active&&<p><span className="pill acc-st-closed">Retired</span></p>}</div>
    <label>Code<input value={code} disabled={!isNew||!canChart} inputMode="numeric" maxLength={4} onChange={e=>setCode(e.target.value.replace(/\D/g,''))} placeholder="4 digits, unused"/></label>
    {codeBad&&code&&<p className="f-why" role="alert">{codeBad}</p>}
    <label>Name<input value={name} disabled={!canChart} onChange={e=>setName(e.target.value)}/></label>
    <label>Group<select value={group} disabled={!canChart} onChange={e=>setGroup(e.target.value)}><option value="">Choose a group…</option>{choices.map(g=><option key={g.id} value={g.id}>{g.name} ({g.nature})</option>)}</select></label>
    {cur?.system&&<p className="muted">A built-in ledger keeps its nature ({curNature}); only groups of the same nature are offered.</p>}
    <label>GST role<select value={gst} disabled={!canChart} onChange={e=>setGst(e.target.value as typeof gst)}><option value="none">None</option><option value="input">Input credit</option><option value="output">Output tax</option></select></label>
    <label>Name in Tally (optional)<input value={tally} disabled={!canChart} onChange={e=>setTally(e.target.value)} placeholder={name||'Same as the name'}/></label>
    <label>Note (optional)<input value={note} disabled={!canChart} onChange={e=>setNote(e.target.value)}/></label>
    {!canChart&&<p className="f-why">Only the Owner can change the chart of accounts.</p>}
    <div className="modal-actions">
      {cur&&canChart&&(cur.active?<button type="button" className="btn" disabled={busy} onClick={()=>void run({type:'ACC_LEDGER_RETIRE',code:cur.code} satisfies AccLedgerRetire,'Ledger retired',{close:true})}>Retire ledger</button>
        :<button type="button" className="btn" disabled={busy} onClick={()=>void run({type:'ACC_LEDGER_UPSERT',code:cur.code,active:true} satisfies AccLedgerUpsert,'Ledger reactivated',{close:true})}>Reactivate</button>)}
      <button type="button" className="btn" onClick={closeModal}>{canChart?'Cancel':'Close'}</button>
      {canChart&&<button className="btn primary" type="submit" disabled={busy||!ok}>Save ledger</button>}</div>
    {cur&&canChart&&cur.active&&<p className="muted">Retiring is refused while the ledger (or any of its parties) has a balance, or a mapping points at it.</p>}</form>;
}
/** 'acc-group' · id = custom group id, 'new' or 'new:<parent group id>' (Owner) */
export function AccGroupModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const {canChart}=useAccPerms();
  const groups=accGroups(s),isNew=isNewId(id),cur=isNew?undefined:groups.find(g=>g.id===id);
  const [name,setName]=useState(cur?.name??'');const [parent,setParent]=useState(cur?.parent??(id?.startsWith('new:')?id.slice(4):''));
  if(!isNew&&!cur)return <Empty title="Group not found"/>;
  const locked=!!cur&&!cur.custom,par=groups.find(g=>g.id===parent);
  return <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'ACC_GROUP_UPSERT',id:cur?.id,name:name.trim(),parent} satisfies AccGroupUpsert,'Sub-group saved',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">CHART OF ACCOUNTS</p><h2>{cur?cur.name:'Add sub-group'}</h2>
      <p>{locked?'This is one of Tally’s 28 primary groups: it is never deleted or moved.':'A custom sub-group sits under any group and takes its nature.'}</p></div>
    <label>Name<input value={name} disabled={!canChart||locked} onChange={e=>setName(e.target.value)}/></label>
    <label>Under group<select value={parent} disabled={!canChart||locked} onChange={e=>setParent(e.target.value)}><option value="">Choose a group…</option>{groups.filter(g=>g.id!==cur?.id).map(g=><option key={g.id} value={g.id}>{g.name} ({g.nature})</option>)}</select></label>
    {par&&<p className="muted">Nature {par.nature} · {par.side==='BS'?'Balance sheet':'Profit & loss'}</p>}
    {!canChart&&<p className="f-why">Only the Owner can change the chart of accounts.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>{canChart&&!locked?'Cancel':'Close'}</button>
      {canChart&&!locked&&<button className="btn primary" type="submit" disabled={busy||name.trim().length<2||!parent}>Save sub-group</button>}</div></form>;
}
/** 'acc-opening-line' · id = ledger / party id of the line, or 'new' (Owner / Manager / Accountant, while the entry is a draft) */
export function AccOpeningLineModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const {canOpeningEnter}=useAccPerms();
  const o=accOpening(s),lines=(o?.lines??[]).filter(l=>l.account!=='1400'),isNew=isNewId(id),cur=isNew?undefined:lines.find(l=>l.account===id);
  const [account,setAccount]=useState(cur?.account??'');const [dr,setDr]=useState(fromPaise(cur?.debit??0));const [cr,setCr]=useState(fromPaise(cur?.credit??0));
  const opts=ledgerOptions(s,true);
  if(!isNew&&!cur)return <Empty title="Line not found" body="The opening entry changed since the list was opened."/>;
  const locked=o?.status==='approved'||!canOpeningEnter,debit=toPaise(dr),credit=toPaise(cr);
  const dup=!cur&&lines.some(l=>l.account===account);
  const save=(next:typeof lines)=>run({type:'ACC_OPENING_SAVE',asOf:o?.asOf??fyStart(s.date),lines:next,note:o?.note} satisfies AccOpeningSave,'Opening line saved',{close:true});
  return <form className="form" onSubmit={e=>{e.preventDefault();void save([...lines.filter(l=>l.account!==cur?.account),{account,debit,credit}]);}}>
    <div className="modal-heading"><p className="eyebrow">OPENING BALANCES</p><h2>{cur?'Edit opening line':'Add opening line'}</h2><p>One side only. The difference is parked on 1400 automatically.</p></div>
    <LedgerPicker options={opts} value={account} onChange={setAccount} disabled={locked||!!cur}/>
    <label>Debit (₹)<input inputMode="decimal" value={dr} disabled={locked} onChange={e=>{setDr(e.target.value);if(e.target.value)setCr('');}}/></label>
    <label>Credit (₹)<input inputMode="decimal" value={cr} disabled={locked} onChange={e=>{setCr(e.target.value);if(e.target.value)setDr('');}}/></label>
    {dup&&<p className="f-why" role="alert">This ledger or party already has a line — edit that one.</p>}
    {locked&&<p className="f-why">{o?.status==='approved'?'The opening entry is approved. The Owner can reopen it.':'Your role cannot enter opening balances.'}</p>}
    <div className="modal-actions">
      {cur&&!locked&&<button type="button" className="btn" disabled={busy} onClick={()=>void save(lines.filter(l=>l.account!==cur.account))}>Remove line</button>}
      <button type="button" className="btn" onClick={closeModal}>Cancel</button>
      <button className="btn primary" type="submit" disabled={busy||locked||!account||dup||(!debit&&!credit)}>Save line</button></div></form>;
}
