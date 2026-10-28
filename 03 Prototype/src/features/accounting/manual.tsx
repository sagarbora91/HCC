// ACC-2 · acc2-ui — Accounting › Manual vouchers (contract §2): list with status pills, the entry dialog with the guided templates, the Owner's
// approval queue, void with a reason. Screens only: the guards, the numbering and the posting are the manual lane's (ACC_MANUAL_*).
import React,{useMemo,useState} from 'react';
import {Plus,Check,X,Ban,Info,AlertTriangle} from 'lucide-react';
import {useHcc} from '../../app/context';
import {money} from '../../services/mock';
import type {State} from '../../services/mock';
import {ListCard,ChipFilter} from '../../app/mobile/components';
import {Empty,dateLabel,shortDate} from '../../components/ui';
import {usePaged,ShowMore,DESKTOP_PAGE,PHONE_PAGE} from '../../components/ShowMore';
import {accManualList,accManualProblems,accCashGuard,type ACCManualVoucher,type ACCManualLine,type ACCManualTemplate,type AccManualSave,type AccManualDecide,type AccManualVoid} from './_contract-stubs-2';
import {MANUAL_TEMPLATES,TEMPLATE_LABEL,tplOf,type Tpl,type TplAnswers} from './manual-templates';
import {useAccPerms,accUserName} from './parts';
import {ledgerOptions,tenderOptions,bankOptions,loanOptions,debtorOptions,toPaise,fromPaise,type LedgerOpt} from './shared';
import {CashGuardNote} from './cashguard';

export const MANUAL_STATUS_LABEL:Record<ACCManualVoucher['status'],string>={pending:'Pending approval',approved:'Approved',rejected:'Rejected',void:'Void'};
const STATUS_TONE:Record<ACCManualVoucher['status'],string>={pending:'orange',approved:'green',rejected:'acc-sev-error',void:'neutral'};
export function ManualStatusPill({v}:{v:ACCManualVoucher}){return <span className={'pill '+STATUS_TONE[v.status]}>{MANUAL_STATUS_LABEL[v.status]}</span>;}
const total=(v:ACCManualVoucher)=>v.lines.reduce((a,l)=>a+l.debit,0);
type Filter='all'|ACCManualVoucher['status'];

/** Accounting › Manual vouchers (desktop table, phone cards). */
export function ManualView({phone}:{phone?:boolean}){
  const {s,open,run,busy}=useHcc();const {canManualEnter,canManualApprove}=useAccPerms();
  const [f,setF]=useState<Filter>('all');
  const all=useMemo(()=>accManualList(s),[s]);const shown=all.filter(v=>f==='all'||v.status===f);
  const pending=all.filter(v=>v.status==='pending');const pg=usePaged(shown,phone?PHONE_PAGE:DESKTOP_PAGE);
  const counts=(st:Filter)=>st==='all'?all.length:all.filter(v=>v.status===st).length;
  const filters=(['all','pending','approved','rejected','void'] as Filter[]).map(id=>({id,label:id==='all'?'All':MANUAL_STATUS_LABEL[id],count:counts(id)}));
  const tools=<div className="acc-man-tools">
    {canManualEnter&&<button type="button" className="btn primary" onClick={()=>open({type:'acc-manual',id:'new'})}><Plus size={16}/> New voucher</button>}
    {canManualEnter&&MANUAL_TEMPLATES.filter(t=>t.id!=='journal'&&(!t.ownerOnly||canManualApprove)).map(t=>
      <button type="button" key={t.id} className="btn small-btn acc-man-tpl" onClick={()=>open({type:'acc-manual',id:'new:'+t.id})}>{t.label}</button>)}</div>;
  const queue=canManualApprove&&pending.length>0&&<div className="acc-man-queue" role="region" aria-label="Approval queue">
    <h3 className="acc-h3">Waiting for your approval ({pending.length})</h3>
    {pending.map(v=><div key={v.id} className="acc-man-q">
      <div><strong>{v.id} · {TEMPLATE_LABEL[v.template??'journal']} · {money(total(v))}</strong>
        <small>{dateLabel(v.date)} · {v.narration} · entered by {accUserName(s,v.by)}</small></div>
      <div className="inline"><button type="button" className="btn small-btn" onClick={()=>open({type:'acc-manual',id:v.id})}>View</button>
        <button type="button" className="btn small-btn primary" disabled={busy} onClick={()=>void run({type:'ACC_MANUAL_DECIDE',id:v.id,approve:true,key:'accman-ok:'+v.id} satisfies AccManualDecide,'Voucher approved and posted')}><Check size={14}/> Approve</button>
        <button type="button" className="btn small-btn" disabled={busy} onClick={()=>open({type:'acc-manual-decide',id:v.id})}><X size={14}/> Reject…</button></div></div>)}</div>;
  const note=<p className="muted acc-man-note">{canManualEnter?(canManualApprove?'Your own vouchers are approved at once — you are the checker.':'Your vouchers wait for the Owner’s approval and post only once approved.'):'Manual vouchers are entered by the Owner or the Accountant. You can read them here.'}
    {' '}A voucher on a locked day is reversed, never edited.</p>;
  if(phone)return <>
    {note}{queue}
    <ChipFilter<Filter> label="Manual voucher status" value={f} onChange={setF} options={filters}/>
    {pg.shown.map(v=><ListCard key={v.id} title={`${v.id} · ${TEMPLATE_LABEL[v.template??'journal']}`} sub={v.narration} meta={`${shortDate(v.date)} · ${v.type} · ${accUserName(s,v.by)}`}
      right={<><strong className="number">{money(total(v))}</strong><br/><ManualStatusPill v={v}/></>} onClick={()=>open({type:'acc-manual',id:v.id})}/>)}
    {!shown.length&&<Empty title="No manual vouchers" body={f==='all'?'Capital, loans, bank charges and other entries that no record in the hotel makes by itself are entered here.':'None with this status.'}/>}
    <ShowMore p={pg}/>
    {canManualEnter&&<div className="acc-m-tpls"><h3 className="m-group">GUIDED ENTRIES</h3>{MANUAL_TEMPLATES.filter(t=>t.id!=='journal'&&(!t.ownerOnly||canManualApprove)).map(t=>
      <ListCard key={t.id} title={t.label} sub={t.blurb} onClick={()=>open({type:'acc-manual',id:'new:'+t.id})}/>)}</div>}
  </>;
  return <div className="padded">
    {tools}{note}{queue}
    <div className="filters acc-man-filters" role="group" aria-label="Manual voucher status">{filters.map(c=><button type="button" key={c.id} className={f===c.id?'active':''} aria-pressed={f===c.id} onClick={()=>setF(c.id)}>{c.label} <span>{c.count}</span></button>)}</div>
    {shown.length?<div className="table-wrap"><table className="acc-table"><thead><tr><th>Voucher</th><th>Date</th><th>Entry</th><th>Type</th><th>Narration</th><th className="acc-num">Amount</th><th>Entered by</th><th>Status</th><th/></tr></thead>
      <tbody>{pg.shown.map(v=><tr key={v.id}><td><button type="button" className="text-btn acc-link number" onClick={()=>open({type:'acc-manual',id:v.id})}>{v.id}</button></td>
        <td>{shortDate(v.date)}</td><td>{TEMPLATE_LABEL[v.template??'journal']}</td><td>{v.type}</td><td className="acc-wrap">{v.narration}{v.voidOf&&<small>Reverses {v.voidOf}</small>}{v.reason&&<small>Reason: {v.reason}</small>}</td>
        <td className="acc-num number"><strong>{money(total(v))}</strong></td><td>{accUserName(s,v.by)}</td><td><ManualStatusPill v={v}/></td>
        <td>{canManualApprove&&v.status==='approved'&&!v.voidOf&&<button type="button" className="text-btn danger-text" onClick={()=>open({type:'acc-manual-void',id:v.id})}>Void…</button>}</td></tr>)}</tbody></table></div>
      :<Empty title="No manual vouchers" body={f==='all'?'Capital, loans, bank charges and other entries that no record in the hotel makes by itself are entered here.':'None with this status.'}/>}
    <ShowMore p={pg}/>
  </div>;
}

// ── entry dialog ──
const emptyLine=():ACCManualLine=>({account:'',debit:0,credit:0});
function optsFor(s:State,kind:string):LedgerOpt[]{return kind==='bank'?bankOptions(s):kind==='tender'?tenderOptions(s):kind==='loan'?loanOptions(s):debtorOptions(s);}

/** 'acc-manual' · id = a voucher id (view; edit while your own is pending), 'new' (free entry) or 'new:<template>' (a guided entry). */
export function ManualModal({id}:{id?:string}){
  const {s,uid,run,closeModal,busy,open,modal}=useHcc();const {canManualEnter,canManualApprove}=useAccPerms();
  const cur=id&&!id.startsWith('new')?(s.accountingX?.manual??[]).find(v=>v.id===id):undefined;
  const startTpl=cur?.template??(id?.startsWith('new:')?id.slice(4) as ACCManualTemplate:'journal');
  const [tplId,setTplId]=useState<ACCManualTemplate>(startTpl);
  const tpl:Tpl=tplOf(tplId)??MANUAL_TEMPLATES[0];
  const [date,setDate]=useState(cur?.date??s.date);const [narration,setNarration]=useState(cur?.narration??'');const [reference,setReference]=useState(cur?.reference??'');
  const [vtype,setVtype]=useState<ACCManualVoucher['type']>(cur?.type??tpl.type);
  const [ans,setAns]=useState<TplAnswers>({});const [free,setFree]=useState<ACCManualLine[]>(cur?cur.lines.map(l=>({...l})):[emptyLine(),emptyLine()]);
  const editable=canManualEnter&&(!cur||(cur.status==='pending'&&cur.by===uid));
  if(id&&!id.startsWith('new')&&!cur)return <Empty title="Voucher not found" body="It may have been replaced since the list was opened."/>;
  const guided=tpl.fields.length>0&&!cur;
  const n=(k:string)=>toPaise(ans[k]??'');
  const lines:ACCManualLine[]=guided?tpl.build(ans,n):free.filter(l=>l.account||l.debit||l.credit);
  const type=guided?tpl.type:vtype;
  const problems=editable?accManualProblems(s,{date,voucherType:type,lines}):[];
  const dr=lines.reduce((a,l)=>a+l.debit,0),cr=lines.reduce((a,l)=>a+l.credit,0);
  // §7 guard preview for Payment / Receipt lines on cash with a party (one party day-total in the real guard)
  const cashLine=lines.find(l=>/^1000\//.test(l.account)||l.account==='1005');
  const guardParty=(type==='Payment'||type==='Receipt')&&cashLine?(lines.find(l=>l!==cashLine&&/^(1110|1120|2200|1200|2400)\//.test(l.account))?.account??''):'';
  const guard=cashLine&&guardParty?accCashGuard(s,{kind:type==='Receipt'?'receipt':'payment',party:guardParty,amount:Math.max(cashLine.debit,cashLine.credit),date}):undefined;
  const ownerOnlyBlock=tpl.ownerOnly&&!canManualApprove?'Bad debt write-offs are entered by the Owner only.':'';
  const setLine=(i:number,p:Partial<ACCManualLine>)=>setFree(free.map((l,j)=>j===i?{...l,...p}:l));
  const all=useMemo(()=>ledgerOptions(s,true),[s]);
  const save=()=>void run({type:'ACC_MANUAL_SAVE',...(cur?{id:cur.id}:{}),date,voucherType:type,narration:narration.trim(),...(reference.trim()?{reference:reference.trim()}:{}),lines,template:tplId,key:modal?.key} satisfies AccManualSave,
    canManualApprove?'Voucher posted (approved — you are the checker)':'Voucher sent to the Owner for approval',{close:true});
  const ok=editable&&!problems.length&&narration.trim().length>=3&&!ownerOnlyBlock;
  return <form className="form acc-man-form" onSubmit={e=>{e.preventDefault();if(ok)save();}}>
    <div className="modal-heading"><p className="eyebrow">MANUAL VOUCHER{cur?' · '+cur.id:''}</p>
      <h2>{cur?`${TEMPLATE_LABEL[cur.template??'journal']} · ${money(total(cur))}`:tpl.label}</h2>
      <p>{cur?<><ManualStatusPill v={cur}/> {dateLabel(cur.date)} · entered by {accUserName(s,cur.by)}</>:tpl.blurb}</p></div>
    {!cur&&canManualEnter&&<label>Entry type<select value={tplId} onChange={e=>{const t=tplOf(e.target.value)!;setTplId(t.id);setAns({});setVtype(t.type);}}>
      {MANUAL_TEMPLATES.filter(t=>!t.ownerOnly||canManualApprove).map(t=><option key={t.id} value={t.id}>{t.label}</option>)}</select></label>}
    {!canManualEnter&&!cur&&<p className="f-why">Manual vouchers are entered by the Owner or the Accountant.</p>}
    <div className="form-row"><label>Date<input type="date" value={date} max={s.date} disabled={!editable} onChange={e=>e.target.value&&setDate(e.target.value)}/></label>
      {!guided&&<label>Voucher type<select value={type} disabled={!editable||!!cur} onChange={e=>setVtype(e.target.value as ACCManualVoucher['type'])}>{(['Journal','Contra','Payment','Receipt'] as const).map(t=><option key={t}>{t}</option>)}</select></label>}
      {guided&&<label>Voucher type<input value={tpl.type} disabled readOnly/></label>}</div>
    {guided&&tpl.fields.map(fd=>fd.kind==='amount'
      ?<label key={fd.key}>{fd.label}<input inputMode="decimal" value={ans[fd.key]??''} onChange={e=>setAns({...ans,[fd.key]:e.target.value})} placeholder="0.00"/></label>
      :<label key={fd.key}>{fd.label}<select value={ans[fd.key]??''} onChange={e=>setAns({...ans,[fd.key]:e.target.value})}><option value="">Choose…</option>
        {optsFor(s,fd.kind).map(o=><option key={o.id} value={o.id}>{o.label}</option>)}</select>
        {fd.kind==='loan'&&!loanOptions(s).length&&<small className="muted">No loan yet — an Owner adds one under Chart of accounts › Add loan.</small>}</label>)}
    {!guided&&(editable?<div className="acc-lines" role="group" aria-label="Voucher lines">
      {free.map((l,i)=><div key={i} className="acc-line"><label className="acc-line-acct">Ledger<select value={l.account} onChange={e=>setLine(i,{account:e.target.value})}><option value="">Choose…</option>
          {all.map(o=><option key={o.id} value={o.id}>{o.label}</option>)}</select></label>
        <label>Debit (₹)<input inputMode="decimal" value={fromPaise(l.debit)} onChange={e=>setLine(i,{debit:toPaise(e.target.value),credit:toPaise(e.target.value)?0:l.credit})}/></label>
        <label>Credit (₹)<input inputMode="decimal" value={fromPaise(l.credit)} onChange={e=>setLine(i,{credit:toPaise(e.target.value),debit:toPaise(e.target.value)?0:l.debit})}/></label>
        {free.length>2&&<button type="button" className="text-btn" onClick={()=>setFree(free.filter((_,j)=>j!==i))}>Remove</button>}</div>)}
      <button type="button" className="btn small-btn" onClick={()=>setFree([...free,emptyLine()])}><Plus size={14}/> Add line</button></div>:null)}
    {(guided||!editable)&&<div className="acc-man-preview" aria-label="Voucher lines">
      {(cur?cur.lines:lines).map((l,i)=><div key={i}><span><strong>{l.account}</strong>{l.note&&<small>{l.note}</small>}</span><b className="number">{l.debit?'Dr '+money(l.debit):'Cr '+money(l.credit)}</b></div>)}
      {!(cur?cur.lines:lines).length&&<p className="muted">The lines appear as you fill in the form.</p>}</div>}
    {editable&&<p className="acc-man-total" role="status">Debits {money(dr)} · Credits {money(cr)} {dr===cr&&dr>0?<span className="pill green">Balanced</span>:<span className="pill acc-sev-error">Not balanced</span>}</p>}
    <label>Narration<input value={narration} maxLength={200} disabled={!editable} onChange={e=>setNarration(e.target.value)} placeholder="What is this for?"/></label>
    <label>Reference (optional)<input value={reference} maxLength={60} disabled={!editable} onChange={e=>setReference(e.target.value)} placeholder="Cheque, UTR, challan…"/></label>
    {cur&&<div className="summary-list">
      {cur.reference&&<div><span>Reference</span><strong>{cur.reference}</strong></div>}
      {cur.decidedBy&&<div><span>Decided by</span><strong>{accUserName(s,cur.decidedBy)} · {cur.decidedAt?.replace('T',' ')}</strong></div>}
      {cur.reason&&<div><span>Reason</span><strong>{cur.reason}</strong></div>}
      {cur.voidOf&&<div><span>Reverses</span><strong>{cur.voidOf}</strong></div>}
      {cur.bookedOn&&<div><span>Booked on</span><strong>{dateLabel(cur.bookedOn)}</strong></div>}</div>}
    {guard&&<CashGuardNote verdict={guard}/>}
    {ownerOnlyBlock&&<p className="f-why" role="alert">{ownerOnlyBlock}</p>}
    {editable&&(dr>0||dr!==cr)&&problems.length>0&&<ul className="f-why acc-man-problems" role="alert">{problems.map(p=><li key={p}>{p}</li>)}</ul>}
    {editable&&!canManualApprove&&<div className="info-note"><Info size={16}/>The Owner approves this voucher before it posts.</div>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>{editable?'Cancel':'Close'}</button>
      {cur&&cur.status==='pending'&&canManualApprove&&<><button type="button" className="btn" onClick={()=>open({type:'acc-manual-decide',id:cur.id})}><X size={16}/> Reject…</button>
        <button type="button" className="btn primary" disabled={busy} onClick={()=>void run({type:'ACC_MANUAL_DECIDE',id:cur.id,approve:true,key:'accman-ok:'+cur.id} satisfies AccManualDecide,'Voucher approved and posted',{close:true})}><Check size={16}/> Approve</button></>}
      {cur&&cur.status==='approved'&&!cur.voidOf&&canManualApprove&&<button type="button" className="btn" onClick={()=>open({type:'acc-manual-void',id:cur.id})}><Ban size={16}/> Void…</button>}
      {editable&&<button className="btn primary" type="submit" disabled={busy||!ok}>{cur?'Save changes':canManualApprove?'Post voucher':'Send for approval'}</button>}</div></form>;
}

/** 'acc-manual-decide' · id = voucher id: the Owner rejects with a reason. */
export function ManualDecideModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const {canManualApprove}=useAccPerms();const v=(s.accountingX?.manual??[]).find(x=>x.id===id);const [reason,setReason]=useState('');
  if(!v)return <Empty title="Voucher not found"/>;
  return <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'ACC_MANUAL_DECIDE',id:v.id,approve:false,reason:reason.trim(),key:'accman-no:'+v.id} satisfies AccManualDecide,'Voucher rejected',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">OWNER DECISION</p><h2>Reject {v.id}</h2><p>{TEMPLATE_LABEL[v.template??'journal']} · {money(total(v))} · {v.narration}. Nothing is posted; the entry stays on record as rejected.</p></div>
    <label>Reason (required)<textarea rows={3} required minLength={3} value={reason} onChange={e=>setReason(e.target.value)} enterKeyHint="done"/></label>
    {!canManualApprove&&<p className="f-why">Only the Owner can decide a manual voucher.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Back</button><button className="btn danger" disabled={busy||!canManualApprove||reason.trim().length<3}><X size={16}/> Reject voucher</button></div></form>;
}

/** 'acc-manual-void' · id = voucher id: the Owner voids with a reason (an unlocked day: it posts nothing; a locked day: a reversing voucher). */
export function ManualVoidModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const {canManualApprove}=useAccPerms();const v=(s.accountingX?.manual??[]).find(x=>x.id===id);const [reason,setReason]=useState('');
  if(!v)return <Empty title="Voucher not found"/>;
  const locked=!!s.accountingX?.locks?.[v.date];
  return <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'ACC_MANUAL_VOID',id:v.id,reason:reason.trim(),key:'accman-void:'+v.id} satisfies AccManualVoid,locked?'Reversing voucher posted':'Voucher voided',{close:true});}}>
    <div className="modal-heading"><p className="eyebrow">OWNER ACTION</p><h2>Void {v.id}</h2><p>{TEMPLATE_LABEL[v.template??'journal']} · {money(total(v))} · {dateLabel(v.date)} · {v.narration}</p></div>
    <div className={'info-note'+(locked?' f-warn':'')} role="status">{locked?<AlertTriangle size={16}/>:<Info size={16}/>}
      <span>{locked?`${dateLabel(v.date)} is locked, so a reversing voucher (the same lines swapped, approved at once) is posted on today’s business date. The original stays on record.`:`${dateLabel(v.date)} is not locked: the voucher becomes void and posts nothing.`}</span></div>
    <label>Reason (required)<textarea rows={3} required minLength={3} value={reason} onChange={e=>setReason(e.target.value)} enterKeyHint="done"/></label>
    {!canManualApprove&&<p className="f-why">Only the Owner can void a manual voucher.</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Back</button><button className="btn danger" disabled={busy||!canManualApprove||reason.trim().length<3}><Ban size={16}/> {locked?'Post reversal':'Void voucher'}</button></div></form>;
}
