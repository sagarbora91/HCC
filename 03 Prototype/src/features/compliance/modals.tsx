// Compliance calendar dialogs (SCC tax module in full, DEC-116): Action Center (L1938–1964), firm profile (L1187–1268), Manage compliance
// (categories / custom / built-in archive, L1508–1568), custom obligation form (L1376–1452), reopen (HCC Owner safeguard), month print
// (L59–136) and the simulated CA pack. Desktop dialogs; on the phone the same dialogs open as sheets (48 px targets, no sideways scroll).
import React,{useState} from 'react';
import {RotateCcw,Send,Plus,Pencil,Archive,Trash2,Printer,Save,AlertTriangle} from 'lucide-react';
import {useHcc} from '../../app/context';
import type {ModalProps} from '../../app/registry';
import {cmpSlice,cmpActionCenter,cmpCanManage,cmpHasAccess,cmpRecord,cmpFindCard,cmpRenderFYText,cmpStripHtml,cmpFyLabel,cmpPackLines,cmpPayableAmount,cmpTypeLabel,
  cmpCatLabel,cmpBuiltInObligations,cmpLicenceObligations,cmpLicenceCard,cmpPrintModel,cmpWhen,CMP_CAT_ORDER,CMP_LIMITS,CMP_MONTHS,CMP_GST_TYPES,CMP_ENTITIES,CMP_FREQS,CMP_MAX_PACKS,CMP_ILLUSTRATIVE,
  type CMPCustom,type CMPMonth,type CMPCat} from '../../services/mock';
import {Empty,Pill,dateLabel} from '../../components/ui';
import {printDocument} from '../../app/native';
import {cmpActionKey,cmpName,cmpUiSet,useCmpFeed,CmpText} from './shared';

// ── Action Center ──
export function CmpActionModal(){
  const {s,closeModal}=useHcc();const x=cmpSlice(s),feed=useCmpFeed();
  if(!x.profile)return <div className="form"><div className="modal-heading"><h2>Action Center</h2></div><p className="cmp-ac-empty">Set the firm profile first (Owner: Firm profile) to see its compliance actions.</p></div>;
  const rows=cmpActionCenter(s),overdue=rows.filter(r=>r.days<0),soon=rows.filter(r=>r.days>=0);
  const go=(month:CMPMonth,itemId:string)=>{closeModal();cmpUiSet({view:'calendar',month,focus:itemId});};
  const row=(r:typeof rows[number])=>{const amt=cmpPayableAmount(feed,r.name,r.month,x.fyStart);
    return <li key={r.itemId}><button type="button" className={'cmp-ac-row'+(r.days<0?' is-overdue':' is-soon')} onClick={()=>go(r.month,r.itemId)}>
      <span className="cmp-ac-when">{r.days<0?'⚠ ':''}{cmpWhen(r.days)}</span>
      <span className="cmp-ac-main"><strong><CmpText text={cmpRenderFYText(r.name,x.fyStart)} custom={r.card._custom}/></strong>
        <small>{r.month} · Due {r.due}{r.form?' · '+cmpStripHtml(cmpRenderFYText(r.form,x.fyStart)):''}{amt!==null?` · ≈₹${amt.toLocaleString('en-IN')} payable (estimate — verify with CA)`:''}</small></span>
      <span className="cmp-ac-firm">{x.profile!.name}</span></button></li>;};
  return <div className="form cmp-ac">
    <div className="modal-heading"><h2>Action Center</h2><p>{cmpFyLabel(x.fyStart)} · outstanding obligations of {x.profile.name}, soonest first. Open the month to mark items filed.</p></div>
    <div className="cmp-ac-sum"><div className="overdue"><strong>{overdue.length}</strong><span>Overdue</span></div><div className="soon"><strong>{soon.length}</strong><span>Due ≤ 30d</span></div><div><strong>1</strong><span>Firm</span></div></div>
    {!rows.length?<p className="cmp-ac-empty">✓ Nothing overdue or due within 30 days — the firm is on track.</p>:<>
      {overdue.length>0&&<><h3 className="cmp-ac-grp">Overdue ({overdue.length})</h3><ul className="cmp-ac-list">{overdue.map(row)}</ul></>}
      {soon.length>0&&<><h3 className="cmp-ac-grp">Due soon ({soon.length})</h3><ul className="cmp-ac-list">{soon.map(row)}</ul></>}</>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button></div></div>;
}

// ── Firm profile (one per property; Owner edits, Accountant sees it) ──
export function CmpProfileModal(){
  const {s,uid,run,busy,closeModal}=useHcc();const x=cmpSlice(s),f=x.profile,owner=cmpCanManage(s,uid);
  const [name,setName]=useState(f?.name??s.settings.hotel),[pan,setPan]=useState(f?.pan??''),[gstin,setGstin]=useState(f?.gstin??''),[entity,setEntity]=useState<string>(f?.entity??'proprietorship');
  const [type,setType]=useState<string>(f?.gstType??'regular'),[emp,setEmp]=useState(f?.hasEmployees===false?'no':'yes'),[qrmp,setQrmp]=useState(f?.qrmp===true),[notes,setNotes]=useState(f?.notes??'');
  const [fssaiNo,setFssaiNo]=useState(f?.fssaiNo??''),[fssaiExpiry,setFssaiExpiry]=useState(f?.fssaiExpiry??''),[lodgingNo,setLodgingNo]=useState(f?.lodgingNo??''),[lodgingExpiry,setLodgingExpiry]=useState(f?.lodgingExpiry??'');
  // ACC-2 (contract §4, §5): three money switches of the firm profile — Owner only (DEC-058), shown to the Accountant read-only.
  const [gstAdv,setGstAdv]=useState(f?.gstOnAdvances===true),[deducts,setDeducts]=useState(f?.hotelDeductsTds!==false),[deductor,setDeductor]=useState<'individual'|'other'>(f?.tdsDeductorType==='other'?'other':'individual');
  const qrmpShown=type==='regular'||type==='both';
  /** DEC-133: where the renewal card will sit (the FY page of the expiry month). */
  const where=(kind:'fssai'|'lodging',expiry:string)=>{const l=cmpLicenceCard({profile:{...(f??{name,pan,gstin,entity:'proprietorship',gstType:'regular',hasEmployees:true,qrmp:false,notes:''}),
    fssaiExpiry:kind==='fssai'?expiry:'',lodgingExpiry:kind==='lodging'?expiry:''}},kind);
    return <small className="cmp-lic-note">{l?`Renewal card: ${l.month} page of ${cmpFyLabel(l.fy)}, due ${dateLabel(l.expiry)} (risk High).`:'No expiry date — no renewal card.'}</small>;};
  return <form className="form cmp-profile" onSubmit={e=>{e.preventDefault();void run({type:'CMP_PROFILE_SET',name,pan,gstin,entity,gstType:type,hasEmployees:emp!=='no',qrmp,notes,
      fssaiNo,fssaiExpiry,lodgingNo,lodgingExpiry,...(owner?{gstOnAdvances:gstAdv,hotelDeductsTds:deducts,tdsDeductorType:deductor}:{}),key:cmpActionKey(s,'profile')},'Firm profile saved',{close:true});}}>
    <div className="modal-heading"><h2>Firm profile</h2><p>The hotel is one firm: one profile for this property{owner?'':' — only the Owner changes it'}. The GST type and “has employees” decide which SCC cards apply.</p></div>
    <fieldset disabled={!owner} className="cmp-fieldset">
      <label>Firm name<input required maxLength={120} value={name} onChange={e=>setName(e.target.value)}/></label>
      <div className="form-row"><label>PAN<input maxLength={20} value={pan} autoComplete="off" onChange={e=>setPan(e.target.value.toUpperCase())}/></label>
        <label>GSTIN<input maxLength={20} value={gstin} autoComplete="off" onChange={e=>setGstin(e.target.value.toUpperCase())}/></label></div>
      <label>Entity<select value={entity} onChange={e=>setEntity(e.target.value)}>{CMP_ENTITIES.map(o=><option key={o.id} value={o.id}>{o.label}</option>)}</select></label>
      <label>GST registration type<select value={type} onChange={e=>setType(e.target.value)}>{CMP_GST_TYPES.map(t=><option key={t} value={t}>{cmpTypeLabel(t)}</option>)}</select></label>
      {qrmpShown&&<label className="cmp-check"><input type="checkbox" checked={qrmp} onChange={e=>setQrmp(e.target.checked)}/>QRMP scheme (quarterly GSTR-1 / GSTR-3B, monthly payment) — advisory only</label>}
      <label>Has employees<select value={emp} onChange={e=>setEmp(e.target.value)}><option value="yes">Yes — payroll filings apply</option><option value="no">No — hide EPF, ESIC, PT deduction and LWF</option></select></label>
      <label>Notes<textarea rows={2} maxLength={500} value={notes} onChange={e=>setNotes(e.target.value)}/></label></fieldset>
    <fieldset disabled={!owner} className="cmp-fieldset"><legend>Licences (optional) — each expiry date adds its renewal card</legend>
      <div className="form-row"><label>FSSAI licence number<input maxLength={CMP_LIMITS.licenceNo} value={fssaiNo} autoComplete="off" onChange={e=>setFssaiNo(e.target.value)}/></label>
        <label>FSSAI licence expiry<input type="date" value={fssaiExpiry} onChange={e=>setFssaiExpiry(e.target.value)}/>{where('fssai',fssaiExpiry)}</label></div>
      <div className="form-row"><label>Lodging house licence number<input maxLength={CMP_LIMITS.licenceNo} value={lodgingNo} autoComplete="off" onChange={e=>setLodgingNo(e.target.value)}/></label>
        <label>Lodging licence expiry<input type="date" value={lodgingExpiry} onChange={e=>setLodgingExpiry(e.target.value)}/>{where('lodging',lodgingExpiry)}</label></div>
      <p className="cmp-meta">After a renewal, enter the new expiry date: the card moves to that month and financial year. Lead time and penalties: CA to confirm.</p></fieldset>
    <fieldset disabled={!owner} className="cmp-fieldset cmp-money-switches" aria-label="Accounting switches (Owner only)"><legend>Accounting switches <span className="pill orange">Owner only</span></legend>
      <label className="cmp-check"><input type="checkbox" checked={gstAdv} onChange={e=>setGstAdv(e.target.checked)}/>Charge GST on booking advances (DEC-142) — off: GST is booked when the stay is billed; on: the advance carries GST that is reversed when the deposit is applied or refunded</label>
      <label className="cmp-check"><input type="checkbox" checked={deducts} onChange={e=>setDeducts(e.target.checked)}/>The hotel deducts TDS from vendor bills (DEC-146) — on: vendors with TDS are paid net and the hotel deposits the TDS; off: no TDS is held back</label>
      <label>Deductor type (picks the TDS rate column)<select value={deductor} disabled={!deducts} onChange={e=>setDeductor(e.target.value==='other'?'other':'individual')}><option value="individual">Individual / HUF (proprietorship)</option><option value="other">Other than individual / HUF (firm, company)</option></select></label>
      {!owner&&<p className="cmp-meta">Only the Owner changes these switches.</p>}
      <p className="cmp-meta">Statutory rates and thresholds: CA to confirm.</p></fieldset>
    {f?.updatedAt&&<small>Last saved {f.updatedAt.replace('T',' ')} by {cmpName(s,f.updatedBy)}.</small>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button>{owner&&<button className="btn primary" disabled={busy||!name.trim()}><Save size={16}/>Save profile</button>}</div>
  </form>;
}

// ── Reopen (HCC: Owner only, reason, the removed record is kept) ──
export function CmpReopenModal({id,room}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();const x=cmpSlice(s),fy=Number(room)||x.fyStart,itemId=id??'',rec=cmpRecord(x,fy,itemId),card=cmpFindCard(x,itemId);const [reason,setReason]=useState('');
  if(!rec||!(rec.na===true||rec.done)||!card)return <Empty title="This item is not filed or marked not applicable"/>;
  const na=rec.na===true;
  return <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'CMP_REOPEN',fy,itemId,reason,key:cmpActionKey(s,'reopen',fy,itemId)},na?'Not applicable removed':'Reopened',{close:true});}}>
    <div className="modal-heading"><h2>{na?'Remove “Not applicable”':'Reopen filing'}</h2>
      <p><CmpText text={cmpRenderFYText(card.name,fy)} custom={card._custom}/> · {cmpFyLabel(fy)} · {na?'not applicable':`filed${rec.filedOn?' on '+dateLabel(rec.filedOn):''}${rec.evidenceRef?' · evidence '+rec.evidenceRef:''}`}{rec.by||rec.naBy?` · by ${cmpName(s,na?rec.naBy:rec.by)}`:''}. The record is kept and the reason goes to the audit trail.</p></div>
    <label>Reason (required)<textarea required minLength={5} maxLength={120} rows={3} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. Wrong filing date entered"/></label>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||reason.trim().length<5}><RotateCcw size={16}/>{na?'Remove NA':'Reopen'}</button></div>
  </form>;
}

// ── Manage compliance (SCC Compliance Master) ──
export function CmpManageModal(){
  const {s,run,busy,open,closeModal}=useHcc();const x=cmpSlice(s);const [del,setDel]=useState('');
  const [caName,setCaName]=useState(x.settings.caName),[caEmail,setCaEmail]=useState(x.settings.caEmail),[days,setDays]=useState(String(x.settings.dueSoonDays));
  const builtIn=[...cmpBuiltInObligations(),...cmpLicenceObligations()];// DEC-133: the licence cards are archived like SCC's built-in keys
  // The obligation key travels as `obligationKey` (`key` is the command's idempotency key).
  const arch=(obligationKey:string,label:string)=>{void run({type:'CMP_ARCHIVE_KEY',obligationKey,key:cmpActionKey(s,'arch',obligationKey)},label);};
  const monthsTxt=(c:CMPCustom)=>(c.months??[]).map(m=>m.slice(0,3)).join(', ')||'—';
  return <div className="form cmp-manage">
    <div className="modal-heading"><h2>Manage compliance</h2><p>{CMP_ILLUSTRATIVE} Built-in obligations cannot be edited (SCC): archive them, or add your own.</p></div>
    <h3 className="cmp-mgr-title">Categories</h3><p className="cmp-meta">Archive a whole category to hide all of its obligations (built-in + custom) at once.</p>
    <ul className="cmp-mgr-list">{CMP_CAT_ORDER.map(cat=>{const a=x.archivedCats.includes(cat);return <li key={cat} className={a?'is-archived':''}><span>{cmpCatLabel(cat)}{a&&<Pill tone="neutral">ARCHIVED</Pill>}</span>
      <button type="button" className="btn small-btn" disabled={busy} onClick={()=>{void run({type:'CMP_ARCHIVE_CAT',cat,key:cmpActionKey(s,'cat',cat)},a?'Category restored':'Category archived');}}><Archive size={14}/>{a?'Restore':'Archive'}</button></li>;})}</ul>
    <h3 className="cmp-mgr-title">Your Custom Obligations</h3>
    {!x.custom.length?<p className="cmp-meta">No custom obligations yet. Add one below.</p>:CMP_CAT_ORDER.map(cat=>{const rows=x.custom.filter(c=>(c.cat||'gst')===cat);if(!rows.length)return null;
      return <React.Fragment key={cat}><h4 className="cmp-mgr-cat">{cmpCatLabel(cat)}</h4><ul className="cmp-mgr-list">{rows.map(c=>{const a=x.archived.includes(c.id),d=parseInt(String(c.dueDay),10);
        return <li key={c.id} className={a?'is-archived':''}><span>{c.name} <Pill tone="blue">CUSTOM</Pill>{a&&<Pill tone="neutral">ARCHIVED</Pill>}<small>{c.freq} · {d===0||isNaN(d)?'Last day':'Day '+c.dueDay} · {monthsTxt(c)}</small></span>
          <span className="cmp-mgr-btns">{del===c.id?<><button type="button" className="btn small-btn danger" disabled={busy} onClick={()=>{setDel('');void run({type:'CMP_CUSTOM_DELETE',id:c.id,key:cmpActionKey(s,'cdel',c.id)},'Custom obligation deleted');}}><Trash2 size={14}/>Delete — sure?</button>
              <button type="button" className="btn small-btn" onClick={()=>setDel('')}>Keep</button></>
            :<><button type="button" className="btn small-btn" onClick={()=>open({type:'cmp-custom',id:c.id})}><Pencil size={14}/>Edit</button>
              <button type="button" className="btn small-btn" onClick={()=>setDel(c.id)}><Trash2 size={14}/>Delete</button>
              <button type="button" className="btn small-btn" disabled={busy} onClick={()=>arch(c.id,a?'Restored':'Archived')}><Archive size={14}/>{a?'Restore':'Archive'}</button></>}</span></li>;})}</ul></React.Fragment>;})}
    <button type="button" className="btn" onClick={()=>open({type:'cmp-custom'})}><Plus size={15}/>Add compliance obligation</button>
    <h3 className="cmp-mgr-title">Built-in Obligations</h3>
    {CMP_CAT_ORDER.map(cat=>{const rows=builtIn.filter(o=>o.cat===cat);if(!rows.length)return null;
      return <React.Fragment key={cat}><h4 className="cmp-mgr-cat">{cmpCatLabel(cat)}</h4><ul className="cmp-mgr-list">{rows.map(o=>{const a=x.archived.includes(o.key);
        return <li key={o.key} className={a?'is-archived':''}><span>{o.key}{a&&<Pill tone="neutral">ARCHIVED</Pill>}</span>
          <button type="button" className="btn small-btn" disabled={busy} onClick={()=>arch(o.key,a?'Restored':'Archived')}><Archive size={14}/>{a?'Restore':'Archive'}</button></li>;})}</ul></React.Fragment>;})}
    <fieldset className="cmp-fieldset"><legend>CA pack and “Due this week” (HCC extras)</legend>
      <label>CA / consultant<input value={caName} maxLength={60} onChange={e=>setCaName(e.target.value)}/></label>
      <label>CA e-mail (simulated send)<input type="email" inputMode="email" value={caEmail} maxLength={80} onChange={e=>setCaEmail(e.target.value)}/></label>
      <label>“Due this week” window on the Dashboard card (days)<input inputMode="numeric" value={days} onChange={e=>setDays(e.target.value.replace(/\D/g,''))}/></label>
      <button type="button" className="btn" disabled={busy} onClick={()=>{void run({type:'CMP_SETTINGS_SET',caName,caEmail,dueSoonDays:Number(days),key:cmpActionKey(s,'set')},'Settings saved');}}><Save size={15}/>Save CA and reminder settings</button></fieldset>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button></div>
  </div>;
}

// ── Custom obligation (add / edit) ──
const SHORT_MONTHS=(m:string)=>m.slice(0,3);
const MONTH_DAYS:Record<string,number>={April:30,May:31,June:30,July:31,August:31,September:30,October:31,November:30,December:31,January:31,February:28,March:31};
export function CmpCustomModal({id}:ModalProps){
  const {s,run,busy,closeModal}=useHcc();const x=cmpSlice(s),c=x.custom.find(o=>o.id===id);
  const [name,setName]=useState(c?.name??''),[cat,setCat]=useState<CMPCat>(c?.cat??'gst'),[freq,setFreq]=useState(c?.freq??'Monthly'),[dueDay,setDueDay]=useState(String(c?.dueDay??1));
  const [months,setMonths]=useState<string[]>(c?.months??[]),[form,setForm]=useState(c?.form??'');
  const [who,setWho]=useState(c?.who??''),[what,setWhat]=useState(c?.what??''),[why,setWhy]=useState(c?.why??''),[penalty,setPenalty]=useState(c?.penalty??'');
  const [resp,setResp]=useState(c?.responsible??''),[rev,setRev]=useState(c?.reviewer??''),[risk,setRisk]=useState<string>(c?.risk??''),[buffer,setBuffer]=useState(typeof c?.buffer==='number'?String(c.buffer):'');
  const [checklist,setChecklist]=useState((c?.checklist??[]).join('\n')),[workflow,setWorkflow]=useState((c?.workflow??[]).join('\n')),[evidence,setEvidence]=useState((c?.evidence??[]).join('\n'));
  if(id&&!c)return <Empty title="This custom obligation is unavailable"/>;
  const day=Number(dueDay),rolls=day>=29?months.filter(m=>MONTH_DAYS[m]!<day):[];
  const toggle=(m:string)=>setMonths(ms=>ms.includes(m)?ms.filter(z=>z!==m):[...ms,m]);
  return <form className="form cmp-custom" onSubmit={e=>{e.preventDefault();void run({type:'CMP_CUSTOM_SAVE',id:c?.id,name,cat,freq,dueDay:day,months,form,who,what,why,penalty,responsible:resp,reviewer:rev,risk,
      buffer:buffer===''?null:Number(buffer),checklist,workflow,evidence,key:cmpActionKey(s,'cust',c?.id??name)},c?'Obligation saved':'Obligation added',{close:true});}}>
    <div className="modal-heading"><h2>{c?'✏ Edit Obligation':'+ Add Compliance Obligation'}</h2><p>An edit applies to every month, past and future (SCC: no effective date).</p></div>
    <label>Obligation name<input required maxLength={200} value={name} onChange={e=>setName(e.target.value)}/></label>
    <div className="form-row"><label>Category<select value={cat} onChange={e=>setCat(e.target.value as CMPCat)}>{CMP_CAT_ORDER.map(k=><option key={k} value={k}>{cmpCatLabel(k)}</option>)}</select></label>
      <label>Frequency (label)<select value={freq} onChange={e=>setFreq(e.target.value)}>{CMP_FREQS.map(f=><option key={f}>{f}</option>)}</select></label>
      <label>Due day<select value={dueDay} onChange={e=>setDueDay(e.target.value)}><option value="0">Last day</option>{Array.from({length:31},(_,n)=><option key={n+1} value={n+1}>{n+1}</option>)}</select></label></div>
    <fieldset className="cmp-fieldset"><legend>Applicable months (the due day falls in the ticked month itself)</legend>
      <label className="cmp-check"><input type="checkbox" checked={months.length===12} onChange={e=>setMonths(e.target.checked?[...CMP_MONTHS]:[])}/>Every month</label>
      <div className="cmp-month-checks">{CMP_MONTHS.map(m=><label key={m} className="cmp-check"><input type="checkbox" checked={months.includes(m)} onChange={()=>toggle(m)}/>{SHORT_MONTHS(m)}</label>)}</div></fieldset>
    {rolls.length>0&&<p className="cmp-warn" role="status"><AlertTriangle size={15}/> Day {day} does not exist in {rolls.map(SHORT_MONTHS).join(', ')}: as SCC computes it, the due date rolls over into the next month there (e.g. 31 Apr = 1 May).</p>}
    <label>Form / Portal<input maxLength={200} value={form} onChange={e=>setForm(e.target.value)}/></label>
    <details><summary>Who / What / Why / Penalty (optional)</summary>
      <label>Who it applies to<textarea rows={2} value={who} onChange={e=>setWho(e.target.value)}/></label><label>What it is<textarea rows={2} value={what} onChange={e=>setWhat(e.target.value)}/></label>
      <label>Why it must be done<textarea rows={2} value={why} onChange={e=>setWhy(e.target.value)}/></label><label>Penalty<textarea rows={2} value={penalty} onChange={e=>setPenalty(e.target.value)}/></label></details>
    <details><summary>Ownership / Risk / Checklist / Workflow / Evidence (optional)</summary>
      <div className="form-row"><label>Responsible<input maxLength={200} value={resp} onChange={e=>setResp(e.target.value)} placeholder="automatic"/></label><label>Reviewer<input maxLength={200} value={rev} onChange={e=>setRev(e.target.value)} placeholder="automatic"/></label></div>
      <div className="form-row"><label>Risk<select value={risk} onChange={e=>setRisk(e.target.value)}><option value="">Automatic</option><option>High</option><option>Medium</option><option>Low</option></select></label>
        <label>Internal buffer (days before due, 0–30)<input inputMode="numeric" value={buffer} onChange={e=>setBuffer(e.target.value.replace(/\D/g,''))} placeholder="automatic (5 high risk, else 3)"/></label></div>
      <label>Input checklist (one per line)<textarea rows={3} value={checklist} onChange={e=>setChecklist(e.target.value)}/></label>
      <label>Workflow (one per line)<textarea rows={3} value={workflow} onChange={e=>setWorkflow(e.target.value)}/></label>
      <label>Evidence pack (one per line)<textarea rows={3} value={evidence} onChange={e=>setEvidence(e.target.value)}/></label></details>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!name.trim()||!months.length}>{c?'Save':'Add'}</button></div>
  </form>;
}

// ── Month print (A4 landscape) ──
export function CmpPrintModal({id}:ModalProps){
  const {s,closeModal}=useHcc();const month=(CMP_MONTHS as readonly string[]).includes(id??'')?id as CMPMonth:'April';const p=cmpPrintModel(s,month);
  return <div className="form"><div className="print-doc cmp-print-doc">
    <div className="cmp-print-head"><div><strong>{s.settings.hotel}</strong><small>Compliance Calendar</small></div><div><strong>Compliance</strong><small>{p.month} {p.calYear} · {p.firmName}</small></div></div>
    <p className="cmp-print-meta"><span><b>Firm:</b> {p.firmName}{p.firmType?` (${p.firmType})`:''}</span><span><b>FY:</b> {p.fyLabel}</span><span><b>Month:</b> {p.month} {p.calYear}</span>
      <span><b>Obligations:</b> {p.obCount}</span><span><b>Done:</b> {p.doneCount} / {p.obCount-p.naCount}</span>{p.naCount>0&&<span><b>NA:</b> {p.naCount}</span>}</p>
    <h3>Compliance Obligations — {p.month} {p.calYear}</h3>
    {!p.rows.length?<p>No compliance obligations for this month.</p>:<table className="cmp-print-table"><thead><tr><th>Due Date</th><th>Obligation</th><th>Form / Portal</th><th>Frequency</th><th>Responsible</th><th>Status</th></tr></thead>
      <tbody>{p.rows.map((r,n)=><tr key={n}><td>{r.due}</td><td>{r.name}</td><td>{r.form}</td><td>{r.freq}</td><td>{r.responsible}</td><td>{r.status}</td></tr>)}</tbody>
      <tfoot><tr><td colSpan={6}>{p.footer}</td></tr></tfoot></table>}
    <p className="cmp-print-foot">{s.settings.hotel} · Compliance Operating System · {p.fyLabel}</p></div>
    <div className="modal-actions no-print"><button type="button" className="btn" onClick={closeModal}>Close</button><button type="button" className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/>Print</button></div></div>;
}

// ── CA pack (simulated; SCC sends its WhatsApp "ca_pack" template — not supplied) ──
export function CmpPackModal(){
  const {s,uid,run,busy,closeModal}=useHcc();const x=cmpSlice(s);const lines=cmpPackLines(s);const last=x.packs[0];
  return <div className="form cmp-pack">
    <div className="modal-heading"><h2>Send CA pack</h2><p><Pill tone="orange">Simulated</Pill> Nothing is sent. The pack is saved here as if sent to {x.settings.caName} ({x.settings.caEmail}). SCC sends a WhatsApp “CA pack” — its template is not in the inputs.</p></div>
    <ul className="cmp-pack-lines" aria-label="Pack contents">{lines.map((l,n)=><li key={n}>{l}</li>)}</ul>
    {last&&<small>Last pack: {last.at.replace('T',' ')} by {cmpName(s,last.by)} · {last.counts.overdue} overdue at the time. Kept: the latest {CMP_MAX_PACKS}.</small>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button>
      <button className="btn primary" disabled={busy||!cmpHasAccess(s,uid)} onClick={()=>{void run({type:'CMP_CA_PACK',key:cmpActionKey(s,'pack')},'CA pack saved as sent (simulated)',{close:true});}}><Send size={16}/>Send (simulated)</button></div>
  </div>;
}
