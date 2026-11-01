// Wave B1 · Cash leaving the drawer (DEC-073) and opening your own shift with a handover count (DEC-078). Shared by the desktop Cashier page
// (SCR-11) and the phone Cashier page (M15): open-shift form, the "Bank deposit / Till transfer / Owner drawing" actions, the list of records
// with confirm / accept / reject, and their dialogs. The rules live in domain/streams/cashout.ts and the mock handlers (the server re-checks).
import React,{useState} from 'react';
import {Landmark,ArrowLeftRight,HandCoins,Camera,Check,X,Wallet,ImageOff,Eye} from 'lucide-react';
import {money,drawer,cxSlice,cxCanBank,cxCanConfirm,cxCanTransfer,cxCanDraw,cxCanViewSlip,cxOwnsDesk,cxTillName,cxDecideBlocked,cxPending,
  shiftStatus,isDayClosed,type CXRecord} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill,Empty,dateLabel} from '../../components/ui';
import {compressSlipPhoto} from '../../app/slipPhoto';
import {clearCapturedPhotos} from '../../app/native';
import {SlipPhotoProd} from '../../clients/sync/cashier-ui';
import {ScrollPanel} from '../../components/fit';

/** "1,234.50" / "₹1,234" → paise; NaN when not a positive amount with at most 2 decimals. */
export function cxRupees(v:string):number{
  const t=String(v??'').replace(/[₹,\s]/g,'');
  if(!t||!/^\d*(\.\d{0,2})?$/.test(t)||t==='.')return NaN;
  return Math.round(Number(t)*100);
}
const name=(s:{users:{id:string;name:string}[]},id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
const KIND:Record<CXRecord['kind'],string>={banking:'Bank deposit',till:'Till transfer',drawing:'Owner drawing'};
const tone=(r:CXRecord)=>r.status==='Pending'?'orange':r.status==='Rejected'?'red':'green';
const statusText=(r:CXRecord)=>r.status==='Pending'?(r.kind==='banking'?'Awaiting confirmation':'Awaiting the receiver'):r.status;

/** DEC-078: open your own shift. Taking over a drawer someone else counted: enter your handover count; a different count needs a reason. */
export function OpenShiftForm({compact}:{compact?:boolean}){
  const {s,run,busy,permission}=useHcc();
  const [count,setCount]=useState('');const [reason,setReason]=useState('');
  if(!permission('payments'))return <p className="muted padded">Opening the cash shift needs the payments permission (cashier or reception).</p>;
  const float=s.openingCash,h=cxRupees(count),entered=count.trim()!=='';
  const bad=entered&&!Number.isFinite(h),diff=entered&&!bad?h-float:0;
  const blocked=!entered?'Count the drawer and enter what you found.':bad?'Enter the amount in rupees.':diff!==0&&!reason.trim()?'Your count differs from the carried float: give a reason.':'';
  return <form className={compact?'form mm-form':'form padded'} aria-label="Open my cash shift" onSubmit={e=>{e.preventDefault();if(blocked)return;
    void run({type:'SHIFT_OPEN',handover:h,reason:reason.trim()||undefined},'Your cashier shift is open — cash receipts are accepted');}}>
    <p>No cash shift is open, so cash receipts are closed. Open <strong>your own</strong> shift: count the drawer you are taking over. The opening float stays
      the cash counted at the last close ({money(float)}); a different count is flagged for the manager.</p>
    <label>Handover count — cash in the drawer now (₹)<input inputMode="decimal" enterKeyHint="done" value={count} onChange={e=>setCount(e.target.value)} placeholder={String(float/100)}/></label>
    {entered&&!bad&&<div className={'f-diff'} role="status"><div><span>Carried float</span><strong>{money(float)}</strong></div><div><span>You counted</span><strong>{money(h)}</strong></div>
      <div className={diff===0?'ok':'bad'}><span>Difference</span><strong>{diff===0?'None · matches':(diff>0?'Over ':'Short ')+money(Math.abs(diff))}</strong></div></div>}
    {entered&&diff!==0&&<label>Reason (required)<textarea required maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. One ₹100 note short at handover"/></label>}
    {blocked&&<p className="f-why" aria-live="polite">{blocked}</p>}
    <div className="modal-actions"><button className={compact?'btn primary full':'btn primary'} disabled={busy||!!blocked}><Wallet size={16}/> Open my cashier shift</button></div>
  </form>;
}

/** The "Bank deposit / Till transfer / Owner drawing" buttons the user may use, and why they are disabled (shared by the panel and, on the
 *  desktop fit page, the page header — FIT §11.9: the page's actions sit in its header, always visible). */
function useCashOutActs(phone?:boolean){
  const {s,uid,open}=useHcc();
  const st=shiftStatus(s),closed=isDayClosed(s);
  const shiftOk=st==='Open'&&!closed,own=cxOwnsDesk(s,uid);
  const why=!shiftOk?'Needs an open cash shift.':!own?'Only from your own open shift.':'';
  const acts=[
    cxCanBank(s,uid)&&<button key="b" type="button" className={phone?'btn full':'btn'} disabled={!!why} title={why||undefined} onClick={()=>open({type:'cash-bank'})}><Landmark size={16}/> Bank deposit</button>,
    cxCanTransfer(s,uid)&&<button key="t" type="button" className={phone?'btn full':'btn'} disabled={!!why} title={why||undefined} onClick={()=>open({type:'till-transfer'})}><ArrowLeftRight size={16}/> Till transfer</button>,
    cxCanDraw(s,uid)&&<button key="d" type="button" className={phone?'btn full':'btn'} disabled={!shiftOk} title={shiftOk?undefined:'Needs an open cash shift.'} onClick={()=>open({type:'owner-drawing'})}><HandCoins size={16}/> Owner drawing</button>,
  ].filter(Boolean);
  return {acts,why};
}
/** FIT desktop page header: the cash-out buttons (the Cashier page passes them to FitPage "actions"). */
export function CashOutActions(){const {acts}=useCashOutActs();return <>{acts}</>;}

/** Bank deposits, till transfers and Owner drawings of the desk drawer: actions + today's / pending records.
 *  fit (desktop FIT page): a ScrollPanel without the buttons (they are in the page header, CashOutActions); the reason they are disabled shows
 *  at the top of the panel. The phone and the old desktop markup are unchanged. */
export function CashOutPanel({phone,fit}:{phone?:boolean;fit?:boolean}){
  const {s,open}=useHcc();
  const x=cxSlice(s);
  const list=x.records.filter(r=>r.status==='Pending'||r.date===s.date||r.decidedDate===s.date).slice().reverse();
  const pending=cxPending(s).length;
  const {acts,why}=useCashOutActs(phone);
  const row=(r:CXRecord)=>{const detail=r.kind==='banking'?`${r.bank??''} · slip ${r.slip??''}`:r.kind==='till'?`${cxTillName(s,r.fromTill)} → ${cxTillName(s,r.toTill)}`:r.reason??'';
    return {detail,who:`${name(s,r.actor)}${r.decidedBy?` · ${r.status.toLowerCase()} by ${name(s,r.decidedBy)}`:''}`};};
  const body=!list.length?<Empty title="Nothing banked, transferred or drawn today" body="Bank deposits, till transfers and Owner drawings appear here and in the cash book."/>
    :phone?<div className="cb-cards">{list.map(r=>{const v=row(r);return <button type="button" key={r.id} className="cb-card cb-tap" onClick={()=>open({type:'cash-decide',id:r.id})}>
        <div><strong>{KIND[r.kind]} · {money(r.amount)}</strong><small>{v.detail}</small><small>{r.id} · {v.who}</small><Pill tone={tone(r)}>{statusText(r)}</Pill></div></button>;})}</div>
    :<div className="table-wrap"><table><thead><tr><th>Record</th><th>Detail</th><th>By</th><th>Status</th><th className="number">Amount</th><th/></tr></thead><tbody>
      {list.map(r=>{const v=row(r);return <tr key={r.id}><td><strong>{KIND[r.kind]}</strong><small>{r.id} · {dateLabel(r.date)}</small></td><td>{v.detail}{r.slipNote&&<small>{r.slipNote}</small>}</td>
        <td>{v.who}</td><td><Pill tone={tone(r)}>{statusText(r)}</Pill></td><td className="number">{money(r.amount)}</td>
        <td><button type="button" className="btn small-btn" onClick={()=>open({type:'cash-decide',id:r.id})}>{r.status==='Pending'?'Open':'View'}</button></td></tr>;})}
    </tbody></table></div>;
  const note=<p className="muted">Expected cash drops when the cash leaves the drawer. A bank deposit is confirmed by a different Manager or Owner
      (a rejected one puts the cash back); a till transfer is accepted by the receiver on their own shift. A pending transfer holds up the day close.</p>;
  if(fit&&!phone)return <ScrollPanel title="Banking, transfers and drawings" label="Cash leaving the drawer" actions={<span className="muted">{pending?`${pending} awaiting a second person`:'DEC-073'}</span>}>
    <div className="padded cx-fit-note">{acts.length>0&&why&&<p className="f-why">{why}</p>}{note}</div>
    {body}
  </ScrollPanel>;
  return <section className={phone?'mm-card':'card'} aria-label="Cash leaving the drawer">
    <div className={phone?undefined:'section-title'}><h2 className={phone?'mm-h':undefined}>Banking, transfers and drawings</h2>{!phone&&<span className="muted">{pending?`${pending} awaiting a second person`:'DEC-073'}</span>}</div>
    {acts.length>0&&<div className={phone?'mm-form':'cb-actions padded'}>{acts}{!phone&&why&&<span className="f-why">{why}</span>}</div>}
    <div className={phone?undefined:'padded'}><p className="muted">Expected cash drops when the cash leaves the drawer. A bank deposit is confirmed by a different Manager or Owner
      (a rejected one puts the cash back); a till transfer is accepted by the receiver on their own shift. A pending transfer holds up the day close.</p></div>
    {body}
  </section>;
}

/** 'cash-bank': amount, bank / account, deposit-slip number, slip photo taken in the app (compressed here to under 100 KB). */
export function CashBankModal(){
  const {s,run,busy,closeModal,modal,display}=useHcc();
  const [amount,setAmount]=useState('');const [bank,setBank]=useState('');const [slip,setSlip]=useState('');const [note,setNote]=useState('');
  const [photo,setPhoto]=useState<{dataUrl:string;bytes:number}|null>(null);const [photoErr,setPhotoErr]=useState('');const [working,setWorking]=useState(false);
  const paise=cxRupees(amount),available=drawer(s);
  const blocked=!Number.isFinite(paise)||paise<=0?'Enter the amount deposited.':paise>available?`That would take the drawer below zero (expected ${money(available)}).`
    :!bank.trim()?'Enter the bank and account.':!slip.trim()?'Enter the deposit-slip number.':!photo?'Take a photo of the deposit slip.':'';
  async function pick(e:React.ChangeEvent<HTMLInputElement>){
    const f=e.target.files?.[0];e.target.value='';if(!f)return;setWorking(true);setPhotoErr('');
    try{setPhoto(await compressSlipPhoto(f));}catch(err){setPhoto(null);setPhotoErr(err instanceof Error?err.message:'The photo could not be prepared. Take it again.');}
    finally{setWorking(false);void clearCapturedPhotos();} // APK: delete the camera's full-size original (only the compressed copy is kept)
  }
  return <form className={display.isMobile?'form mm-form':'form'} onSubmit={e=>{e.preventDefault();if(blocked)return;
    void run({type:'CASH_BANK',amount:paise,bank:bank.trim(),slip:slip.trim(),photo:photo!.dataUrl,note:note.trim()||undefined,key:'cash-bank:'+modal!.key},'Bank deposit recorded — a different Manager or Owner confirms it',{close:true});}}>
    <div className="modal-heading"><h2>Bank deposit</h2><p>From your shift’s drawer · expected cash {money(available)} · {dateLabel(s.date)}</p></div>
    <label>Amount deposited (₹)<input inputMode="decimal" value={amount} onChange={e=>setAmount(e.target.value)} placeholder="e.g. 6000"/></label>
    <label>Bank and account<input maxLength={100} value={bank} onChange={e=>setBank(e.target.value)} placeholder="e.g. State Bank of India · current a/c ••4321"/></label>
    <label>Deposit-slip number<input maxLength={40} value={slip} onChange={e=>setSlip(e.target.value)} placeholder="e.g. DS-1001"/></label>
    <label className="cx-photo">Photo of the deposit slip (required)
      <input type="file" accept="image/*" capture="environment" onChange={e=>{void pick(e);}} aria-describedby="cx-photo-help"/></label>
    <small id="cx-photo-help" className="muted">Taken with the camera and made smaller on this device (under 100 KB). Kept 600 days, then deleted automatically.</small>
    {working&&<p className="muted" role="status">Preparing the photo…</p>}
    {photoErr&&<p className="form-error" role="alert">{photoErr}</p>}
    {photo&&<div className="cx-preview"><img src={photo.dataUrl} alt="Deposit slip photo" style={{maxWidth:'100%',maxHeight:220,borderRadius:8}}/><small>{Math.ceil(photo.bytes/1024)} KB</small></div>}
    <label>Note (optional)<input maxLength={200} value={note} onChange={e=>setNote(e.target.value)}/></label>
    {blocked&&<p className="f-why" aria-live="polite">{blocked}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
      <button className="btn primary" disabled={busy||working||!!blocked}><Camera size={16}/> Record bank deposit</button></div>
  </form>;
}

/** 'till-transfer': to another till; waits for the receiver to accept it on their own open shift. */
export function TillTransferModal(){
  const {s,run,busy,closeModal,modal,display}=useHcc();
  const x=cxSlice(s);const [to,setTo]=useState(x.tills[0]?.code??'');const [amount,setAmount]=useState('');const [reason,setReason]=useState('');
  const paise=cxRupees(amount),available=drawer(s);
  const blocked=!x.tills.length?'There is no other till to transfer to.':!to?'Choose the till that receives the cash.':!Number.isFinite(paise)||paise<=0?'Enter the amount.'
    :paise>available?`That would take the drawer below zero (expected ${money(available)}).`:'';
  return <form className={display.isMobile?'form mm-form':'form'} onSubmit={e=>{e.preventDefault();if(blocked)return;
    void run({type:'TILL_TRANSFER',from:x.desk,to,amount:paise,reason:reason.trim()||undefined,key:'till-transfer:'+modal!.key},'Till transfer recorded — the receiver accepts it on their own shift',{close:true});}}>
    <div className="modal-heading"><h2>Till transfer</h2><p>From {cxTillName(s,x.desk)} · expected cash {money(available)}</p></div>
    <label>To till<select value={to} onChange={e=>setTo(e.target.value)}>{x.tills.map(t=><option key={t.code} value={t.code}>{t.name}{t.shiftOpen?` · shift open (${name(s,t.openedBy)})`:' · no shift open'}</option>)}</select></label>
    <label>Amount (₹)<input inputMode="decimal" value={amount} onChange={e=>setAmount(e.target.value)} placeholder="e.g. 2000"/></label>
    <label>Reason (optional)<input maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. Change for the back office"/></label>
    {blocked&&<p className="f-why" aria-live="polite">{blocked}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
      <button className="btn primary" disabled={busy||!!blocked}><ArrowLeftRight size={16}/> Send to till</button></div>
  </form>;
}

/** 'owner-drawing': Owner only, reason required. */
export function OwnerDrawingModal(){
  const {s,run,busy,closeModal,modal,display}=useHcc();
  const [amount,setAmount]=useState('');const [reason,setReason]=useState('');
  const paise=cxRupees(amount),available=drawer(s);
  const blocked=!Number.isFinite(paise)||paise<=0?'Enter the amount.':paise>available?`That would take the drawer below zero (expected ${money(available)}).`:!reason.trim()?'Give the reason for the drawing.':'';
  return <form className={display.isMobile?'form mm-form':'form'} onSubmit={e=>{e.preventDefault();if(blocked)return;
    void run({type:'OWNER_DRAWING',amount:paise,reason:reason.trim(),key:'owner-drawing:'+modal!.key},'Owner drawing recorded',{close:true});}}>
    <div className="modal-heading"><h2>Owner drawing</h2><p>Cash taken from the drawer by the Owner · expected cash {money(available)}</p></div>
    <label>Amount (₹)<input inputMode="decimal" value={amount} onChange={e=>setAmount(e.target.value)}/></label>
    <label>Reason (required)<input maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. Household expenses"/></label>
    {blocked&&<p className="f-why" aria-live="polite">{blocked}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
      <button className="btn primary" disabled={busy||!!blocked}><HandCoins size={16}/> Record drawing</button></div>
  </form>;
}

/** The slip photo: demo build from the record; production fetched on demand (GET slip, Manager / Owner / Accountant). */
function SlipPhoto({r}:{r:CXRecord}){
  const {s,uid}=useHcc();const [show,setShow]=useState(false);
  if(r.kind!=='banking')return null;
  if(r.slipStored===false||r.slipNote)return <p className="muted"><ImageOff size={15}/> {r.slipNote??'Slip photo purged after 600 days.'}</p>;
  if(!cxCanViewSlip(s,uid))return <p className="muted">The slip photo is visible to the Manager, Owner and Accountant.</p>;
  if(!show)return <button type="button" className="btn" onClick={()=>setShow(true)}><Eye size={16}/> View slip photo</button>;
  return process.env.HCC_BUILD==='production'?<SlipPhotoProd id={r.id}/>
    :r.photo?<img src={r.photo} alt={`Deposit slip ${r.slip??''}`} style={{maxWidth:'100%',maxHeight:360,borderRadius:8}}/>:<p className="muted">No photo stored.</p>;
}

/** 'cash-decide' (id = record): details, slip photo, and confirm / accept or reject (with a reason) when this user may decide it. */
export function CashDecideModal({id}:{id?:string}){
  const {s,uid,run,busy,closeModal,modal,display}=useHcc();const [reason,setReason]=useState('');
  const r=cxSlice(s).records.find(x=>x.id===id);
  if(!r)return <><Empty title="Record not found" body="It may have been decided already. Refresh and try again."/><div className="modal-actions"><button className="btn" onClick={closeModal}>Close</button></div></>;
  const banking=r.kind==='banking',okYes=r.kind!=='drawing'?cxDecideBlocked(s,uid,r,true):'Nothing to decide.',okNo=r.kind!=='drawing'?cxDecideBlocked(s,uid,r,false):'Nothing to decide.';
  const decide=(approve:boolean)=>void run(banking?{type:'CASH_BANK_DECIDE',id:r.id,confirm:approve,reason:reason.trim()||undefined,key:'cash-decide:'+r.id+':'+modal!.key}
    :{type:'TILL_TRANSFER_DECIDE',id:r.id,accept:approve,reason:reason.trim()||undefined,key:'cash-decide:'+r.id+':'+modal!.key},
    approve?(banking?'Bank deposit confirmed':'Till transfer accepted — the cash is in your drawer'):(banking?'Bank deposit rejected — the cash is back in the drawer':'Till transfer sent back'),{close:true});
  const canAct=r.status==='Pending'&&(!okYes||!okNo);
  return <div className={display.isMobile?'mm-form':undefined}>
    <div className="modal-heading"><h2>{KIND[r.kind]} · {money(r.amount)}</h2><p>{r.id} · {dateLabel(r.date)} · <Pill tone={tone(r)}>{statusText(r)}</Pill></p></div>
    <div className="summary-list">
      {banking&&<><div><span>Bank and account</span><strong>{r.bank}</strong></div><div><span>Deposit slip</span><strong>{r.slip}</strong></div></>}
      {r.kind==='till'&&<div><span>From → to</span><strong>{cxTillName(s,r.fromTill)} → {cxTillName(s,r.toTill)}</strong></div>}
      {r.reason&&<div><span>{r.kind==='drawing'?'Reason':'Note'}</span><strong>{r.reason}</strong></div>}
      <div><span>Recorded by</span><strong>{name(s,r.actor)}</strong></div>
      {r.decidedBy&&<div><span>{r.status} by</span><strong>{name(s,r.decidedBy)}{r.decisionReason?` · ${r.decisionReason}`:''}</strong></div>}
    </div>
    <SlipPhoto r={r}/>
    {canAct&&<label className="form">Reason (required to reject)<textarea maxLength={200} value={reason} onChange={e=>setReason(e.target.value)}
      placeholder={banking?'e.g. Slip amount does not match':'e.g. Not needed after all'}/></label>}
    {r.status==='Pending'&&okYes&&okNo&&<p className="f-why">{okYes}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button>
      {canAct&&<button type="button" className="btn" disabled={busy||!!okNo||!reason.trim()} title={okNo||(!reason.trim()?'A reason is required to reject.':undefined)} onClick={()=>decide(false)}><X size={16}/> {banking?'Reject':'Send back'}</button>}
      {canAct&&<button type="button" className="btn primary" disabled={busy||!!okYes} title={okYes||undefined} onClick={()=>decide(true)}><Check size={16}/> {banking?'Confirm deposit':'Accept into my drawer'}</button>}</div>
  </div>;
}
