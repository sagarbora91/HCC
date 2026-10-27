// Module 17 · HR & payroll — dialogs (render as full-screen sheets on phones). Modal IDs start with 'hr-'.
// W05-B: SCC statutory payslip (PF / ESIC / MH PT breakdown, amount in words, PDF via the native share/print hook, simulated WhatsApp — DEC-056),
// dated statutory rates, leave policy, half-day leave, pay-run unlock and full-and-final settlement.
import {printDocument} from '../../app/native';
import React,{useState} from 'react';
import {Printer,Info,Hotel,MessageCircle,Plus,Trash2,AlertTriangle} from 'lucide-react';
import {useHcc} from '../../app/context';
import {dateLabel,shortDate,Empty} from '../../components/ui';
import {MstLink} from '../masters/MasterList';
import {
  money,hrSlice,hrRateVersions,hrCanPay,hrCanManage,hrStaffById,hrStaffOfUser,hrShiftFor,hrShiftInfo,hrDeptName,hrLeaveBalance,hrLeaveDays,hrMonthLabel,hrRatesFor,
  hrLastLockedMonth,hrLeaveOverCount,hrNextMonth,hrRatesDefaultFrom,hrRatesPastMonths,hrCanLink,hrLockedPayslipCount,hrLeavePolicy,hrFnfCompute,hrElEncashment,hrAmountWords,hrPayslipMessage,hrWaNumber,hrFy,hrPaidLeave,
  hrShiftChoices,hrDesignationChoices,hrDesignationOf,hrPayRuleFields,hrLeaveChoices,hrLeaveName,mstByCode,HR_SHIFTS,HR_WEEKDAYS,HR_FNF_ADJ_MAX,HR_FNF_OWNER_ABOVE,HR_ATT_LABEL,HR_LEAVE_LABEL,HR_LEAVE_TYPES,HR_DISCLAIMER,HR_NOT_CALCULATED,hrKey,
  hrCanRates,hrCorrectionWindow,hrDayEntry,hrEntryLabel,hrParseEntry,hrPlanCorrection,hrCorrectionDateRefusal,hrPendingRequests,hrPaidHolidayOn,hrRunFor,hrMaskAccount,
  HR_ATT_MARKS,type HRShiftCode,type HRAttStatus,type HRLeaveType,type HRRates,type HRFnfAdj,type HRRun,type HRLine,type HRBank,
} from '../../services/mock';

const rs=(v:string)=>Math.round(Number(v)*100);
const toRs=(p:number)=>p?String(p/100):'';
export const hrUserName=(s:ReturnType<typeof useHcc>['s'],id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
/** Bank details for screens, account number masked (production loads it masked already). */
export const hrBankText=(b?:HRBank)=>b&&(b.bankName||b.accountNo||b.ifsc)
  ?`${b.bankName||'(none)'} ${b.accountNo?(b.accountNo.startsWith('••••')?b.accountNo:hrMaskAccount(b.accountNo)):'(none)'} ${b.ifsc}`.trim():'(none)';
const bankText=hrBankText;

/** 'hr-staff': id = staff ID (edit) or undefined (new). Salary, PF / ESIC and bank fields only for payroll roles. */
export function HRStaffModal({id}:{id?:string}){
  const {s,uid,run,closeModal,busy,open}=useHcc();const x=hrSlice(s),st=id?hrStaffById(s,id):undefined,pay=hrCanPay(s,uid);
  const [f,setF]=useState({name:st?.name??'',dept:st?.dept??x.depts[0]?.id??'',designation:st?.designation??'',designationCode:(st?hrDesignationOf(s,st)?.code:undefined)??st?.designationCode??'',userId:st?.userId??'',
    defaultShift:st?.defaultShift??'G',weeklyOff:String(st?.weeklyOff??0),phone:st?.phone??'',gender:st?.gender??'M',payType:st?.payType??'monthly',monthly:toRs(st?.monthly??0),daily:toRs(st?.daily??0),
    joined:s.date,structure:st?.structure??'',pf:st?.pfApplicable===undefined?'':st.pfApplicable?'yes':'no',esic:st?.esicApplicable===false?'no':'yes',bankName:st?.bankName??'',accountNo:st?.accountNo??'',ifsc:st?.ifsc??''});
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement>)=>setF({...f,[k]:e.target.value});
  const linked=new Set(x.staff.filter(o=>o.id!==id&&o.userId).map(o=>o.userId));
  // F-03: only the Owner links logins, and never on their own record.
  const canLink=hrCanLink(s,uid)&&st?.userId!==uid;
  const desigs=hrDesignationChoices(s,f.dept,f.designationCode);
  /** Choosing a designation fills its name and (for payroll roles, new records) the default pay rule of the list. */
  const pickDesig=(e:React.ChangeEvent<HTMLSelectElement>)=>{const row=mstByCode(s,'designations',e.target.value) as {name:string;defaultPayRule?:'monthly-structured'|'monthly-basic'|'daily'}|undefined;
    const rule=!st&&pay&&row?.defaultPayRule?hrPayRuleFields(row.defaultPayRule):undefined;
    setF({...f,designationCode:e.target.value,designation:row?.name??'',...(rule?{payType:rule.payType,structure:rule.structure}:{})});};
  // DEC-084: a bank-detail change by anyone but the Owner waits for the Owner's approval; payments keep the current details meanwhile.
  const owner=hrCanRates(s,uid),bankWaiting=st?hrPendingRequests(s,'bank').find(r=>r.staffId===st.id):undefined;
  const [bankReason,setBankReason]=useState('');
  const bankEdited=f.bankName.trim()!==(st?.bankName??'')||f.accountNo.trim()!==(st?.accountNo??'')||f.ifsc.trim().toUpperCase()!==(st?.ifsc??'');
  function save(e:React.FormEvent){e.preventDefault();
    const cmd:Record<string,unknown>={type:'HR_STAFF_SAVE',id,name:f.name,dept:f.dept,designation:f.designation,...(f.designationCode?{designationCode:f.designationCode}:{}),userId:f.userId||undefined,defaultShift:f.defaultShift,weeklyOff:Number(f.weeklyOff),phone:f.phone,gender:f.gender};
    // N3 (production): a new staff member's joining date (server SaveStaff `joined`, not after the business date) — back-dated staff join that month's pay run.
    if(process.env.HCC_BUILD==='production'&&!st&&f.joined)cmd.joined=f.joined;
    const slips=st?hrLockedPayslipCount(s,st.id):0;
    if(st&&f.userId&&f.userId!==(st.userId??'')&&slips){if(!window.confirm(`${st.name} has payslips in ${slips} locked pay-run${slips===1?'':'s'}. The newly linked login will see them. Relink?`))return;cmd.confirmRelink=true;}
    if(pay)Object.assign(cmd,{payType:f.payType,monthly:f.payType==='monthly'?rs(f.monthly):0,daily:f.payType==='daily'?rs(f.daily):0,
      ...(f.structure?{structure:f.structure}:{}),...(f.pf?{pfApplicable:f.pf==='yes'}:{}),esicApplicable:f.esic==='yes',
      ...(bankWaiting?{}:{bankName:f.bankName,accountNo:f.accountNo,ifsc:f.ifsc.toUpperCase(),...(bankReason.trim()?{bankReason:bankReason.trim()}:{})})});
    const waits=pay&&!owner&&!bankWaiting&&bankEdited;
    run(cmd as {type:string},waits?`${id?'Staff record updated':'Staff member added'} · bank details sent to the Owner for approval`:id?'Staff record updated':'Staff member added',{close:true});}
  return <><div className="modal-heading"><h2>{st?'Edit staff record':'Add staff member'}</h2><p>Fictional staff only. A staff record may link to a demo login; cooks, security and helpers usually have none.</p></div>
    <form className="form" onSubmit={save}>
      <label>Full name<input required minLength={2} maxLength={60} value={f.name} onChange={set('name')} enterKeyHint="next"/></label>
      {process.env.HCC_BUILD==='production'&&!x.depts.length&&<div className="info-note hr-warn" role="alert"><Info size={16}/><span>No departments yet. Add one first, then add staff.{' '}
        <button type="button" className="text-btn" onClick={()=>open({type:'hr-dept'})}>Add a department</button></span></div>}
      <div className="form-row"><label>Department<select required={process.env.HCC_BUILD==='production'} value={f.dept} onChange={e=>setF({...f,dept:e.target.value,...(hrDesignationChoices(s,e.target.value).some(d=>d.code===f.designationCode)?{}:{designationCode:'',designation:f.designationCode?'':f.designation})})}>{x.depts.map(d=><option key={d.id} value={d.id}>{d.name}</option>)}</select></label>
        <label>Designation{desigs.length?<select required value={f.designationCode} onChange={pickDesig}><option value="">Choose…</option>{!f.designationCode&&f.designation&&<option value="">{f.designation} (not in the list)</option>}{desigs.map(d=><option key={d.code} value={d.code}>{d.name}{d.grade?` · ${d.grade}`:''}{d.active?'':' (retired)'}</option>)}</select>
          :<input required maxLength={60} value={f.designation} onChange={set('designation')}/>}<small><MstLink spec="designations"/></small></label></div>
      {process.env.HCC_BUILD==='production'&&!st&&<label>Joining date<input type="date" required max={s.date} value={f.joined} onChange={set('joined')}/><small>On or before the business date. Pay counts from this date.</small></label>}
      <div className="form-row"><label>Default shift<select value={f.defaultShift} onChange={set('defaultShift')}>{hrShiftChoices(s,f.defaultShift).map(h=><option key={h.code} value={h.code}>{h.name} {h.start}–{h.end}{h.active?'':' (retired)'}</option>)}</select><small><MstLink spec="shiftPatterns"/></small></label>
        <label>Weekly off<select value={f.weeklyOff} onChange={set('weeklyOff')}>{HR_WEEKDAYS.map((d,i)=><option key={d} value={i}>{d}</option>)}</select></label></div>
      <div className="form-row"><label>Demo login{!canLink&&<small> (Owner only)</small>}<select value={f.userId} disabled={!canLink} onChange={set('userId')}><option value="">No login (non-login staff)</option>{s.users.filter(u=>!linked.has(u.id)).map(u=><option key={u.id} value={u.id}>{u.name}</option>)}</select></label>
        <label>Mobile (fictional, for payslips){st&&!pay&&<small> (payroll only)</small>}<input inputMode="tel" maxLength={20} value={f.phone} disabled={!!st&&!pay} onChange={set('phone')}/></label></div>
      <label>Professional-tax slab{st&&!pay&&<small> (payroll only)</small>}<select value={f.gender} disabled={!!st&&!pay} onChange={set('gender')}><option value="M">Men’s slab</option><option value="F">Women’s slab (exempt up to ₹25,000)</option></select></label>
      {pay&&st?.userId===uid&&<div className="info-note"><Info size={16}/>This is your own record: salary, PF / ESIC and bank details are changed by the other payroll user (no self-approval).</div>}
      {pay?<fieldset className="hr-fieldset" disabled={st?.userId===uid}><legend>Salary and statutory (payroll roles only)</legend>
        <div className="form-row"><label>Pay type<select value={f.payType} onChange={set('payType')}><option value="monthly">Monthly salary</option><option value="daily">Daily wage</option></select></label>
          {f.payType==='monthly'?<label>Monthly salary (₹)<input required type="number" inputMode="decimal" min="0" max="1000000" step="0.01" value={f.monthly} onChange={set('monthly')}/></label>
            :<label>Daily wage (₹)<input required type="number" inputMode="decimal" min="0" max="100000" step="0.01" value={f.daily} onChange={set('daily')}/></label>}</div>
        <div className="form-row"><label>Structure<select value={f.structure} onChange={set('structure')}><option value="">Default ({f.payType==='daily'?'basic only':'structured'})</option><option value="S">Structured (basic / HRA / washing / special)</option><option value="B">Basic only</option></select></label>
          <label>PF<select value={f.pf} onChange={set('pf')}><option value="">Default (structured pay only)</option><option value="yes">Applies</option><option value="no">Not applicable</option></select></label></div>
        <label>ESIC<select value={f.esic} onChange={set('esic')}><option value="yes">Applies while earned ≤ ceiling</option><option value="no">Not applicable</option></select></label>
        {bankWaiting?<div className="info-note hr-warn" role="status"><AlertTriangle size={16}/><span>A bank-detail change ({bankText(bankWaiting.bankBefore)} → {bankText(bankWaiting.bankAfter)}) is waiting for the Owner. Payments keep the current details until it is decided.</span></div>
          :!owner&&<div className="info-note"><Info size={16}/><span>A change of bank details goes to the Owner for approval (DEC-084). Payments and bank files keep the current details until then.</span></div>}
        <div className="form-row"><label>Bank (fictional)<input maxLength={60} value={f.bankName} disabled={!!bankWaiting} onChange={set('bankName')}/></label>
          <label>Account number<input inputMode="numeric" maxLength={18} value={f.accountNo} disabled={!!bankWaiting} onChange={set('accountNo')}/></label></div>
        <label>IFSC<input maxLength={11} value={f.ifsc} disabled={!!bankWaiting} onChange={set('ifsc')} placeholder="DEMO0000101"/></label>
        {!owner&&!bankWaiting&&bankEdited&&<label>Reason for the bank change (for the Owner)<input maxLength={200} value={bankReason} onChange={e=>setBankReason(e.target.value)} placeholder="Optional"/></label>}</fieldset>
        :<div className="info-note"><Info size={16}/>Salary, PF / ESIC and bank details are set by the Owner or Accountant.</div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>{st?'Save changes':'Add staff member'}</button></div>
    </form></>;
}

/** 'hr-dept' (production build only, N3): add or rename departments (server SaveDepartment). The leave limit is kept as it is — it is
 *  part of the leave policy (Owner). Never opened in the prototype, which ships its departments as fixtures. */
export function HRDeptModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const depts=hrSlice(s).depts;
  const [edit,setEdit]=useState<string|undefined>(id);const cur=depts.find(d=>d.id===edit);
  const [f,setF]=useState({code:cur?.id??'',name:cur?.name??''});
  const pick=(code?:string)=>{const d=depts.find(x=>x.id===code);setEdit(code);setF({code:d?.id??'',name:d?.name??''});};
  function save(e:React.FormEvent){e.preventDefault();
    run({type:'HR_DEPT_SAVE',code:f.code.trim().toUpperCase(),name:f.name.trim(),...(cur?{id:cur.id}:{})},cur?'Department renamed':'Department added',{after:()=>pick(undefined)});}
  return <><div className="modal-heading"><h2>Departments</h2><p>Every staff member belongs to one department. The code cannot change once staff use it; rename freely.</p></div>
    {depts.length?<ul className="hr-dept-list">{depts.map(d=><li key={d.id}><span><strong>{d.name}</strong> <small>{d.id}</small></span><button type="button" className="text-btn" onClick={()=>pick(d.id)}>Rename</button></li>)}</ul>
      :<Empty title="No departments yet" body="Add the first department below (for example FO · Front office)."/>}
    <form className="form" onSubmit={save}>
      <div className="form-row"><label>Code<input required minLength={2} maxLength={10} pattern="[A-Za-z0-9]{2,10}" disabled={!!cur} value={f.code} onChange={e=>setF({...f,code:e.target.value.toUpperCase()})} placeholder="FO"/><small>2–10 letters or digits</small></label>
        <label>Name<input required minLength={2} maxLength={60} value={f.name} onChange={e=>setF({...f,name:e.target.value})} placeholder="Front office"/></label></div>
      <div className="modal-actions"><button type="button" className="btn" onClick={cur?()=>pick(undefined):closeModal}>{cur?'Cancel rename':'Close'}</button><button className="btn primary" disabled={busy}>{cur?'Save name':'Add department'}</button></div>
    </form></>;
}

/** 'hr-roster': id = staff ID, room = date. */
export function HRRosterModal({id,room}:{id?:string;room?:string}){
  const {s,run,closeModal,busy}=useHcc();const st=id?hrStaffById(s,id):undefined;
  if(!st||!room)return <Empty title="This roster cell is unavailable"/>;
  const cur=hrShiftFor(s,st,room);
  const pick=(shift:HRShiftCode)=>run({type:'HR_ROSTER_SET',staffId:st.id,date:room,shift},`${st.name} · ${shortDate(room)} → ${hrShiftInfo(shift,s).label}`,{close:true});
  return <><div className="modal-heading"><h2>Set shift</h2><p>{st.name} · {HR_WEEKDAYS[new Date(room+'T12:00:00Z').getUTCDay()]} {dateLabel(room)}. Currently <strong>{hrShiftInfo(cur,s).label}</strong>.</p></div>
    <div className="hr-shift-pick">{[...hrShiftChoices(s,cur).map(p=>({code:p.code,label:p.name,start:p.start,end:p.end})),HR_SHIFTS.find(h=>h.code==='OFF')!].map(h=><button key={h.code} type="button" className={'btn'+(h.code===cur?' primary':'')} aria-pressed={h.code===cur} disabled={busy} onClick={()=>pick(h.code)}>
      <strong>{h.label}</strong><span>{h.start?`${h.start}–${h.end}`:'No duty'}</span></button>)}</div>
    <p className="footnote"><MstLink spec="shiftPatterns"/></p>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button></div></>;
}

/** 'hr-att': id = staff ID. Manager marks/corrects today's attendance. */
export function HRAttModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const st=id?hrStaffById(s,id):undefined;
  const prev=st?hrSlice(s).att[hrKey(s.date,st.id)]:undefined;
  const [status,setStatus]=useState<HRAttStatus>(prev?.status??'P');const [ot,setOt]=useState(String(prev?.otMin??0));const [note,setNote]=useState(prev?.note??'');
  if(!st)return <Empty title="Staff member unavailable"/>;
  const shift=hrShiftInfo(hrShiftFor(s,st,s.date),s);
  return <><div className="modal-heading"><h2>Attendance · {st.name}</h2><p>Business date {dateLabel(s.date)} · rostered {shift.label}{shift.start?` ${shift.start}–${shift.end}`:''}.{prev?` Already recorded as ${HR_ATT_LABEL[prev.status]} — saving corrects it.`:''}</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_ATT_MARK',staffId:st.id,status,otMin:Number(ot)||0,note},`${st.name} · ${HR_ATT_LABEL[status]}`,{close:true});}}>
      <div className="hr-status-pick" role="radiogroup" aria-label="Attendance status">{HR_ATT_MARKS.map(k=>
        <label key={k} className={status===k?'on':''}><input type="radio" name="hr-status" checked={status===k} onChange={()=>setStatus(k)}/>{HR_ATT_LABEL[k]}</label>)}</div>
      <label>Overtime (minutes, 0–480)<input type="number" inputMode="numeric" min="0" max="480" step="15" value={ot} disabled={status==='A'} onChange={e=>setOt(e.target.value)}/></label>
      <label>Note<input maxLength={120} value={note} onChange={e=>setNote(e.target.value)} placeholder="Optional" enterKeyHint="done"/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Save attendance</button></div>
    </form></>;
}

const OT_MARKS=['P','L','H','OFF'];
/** 'hr-att-correct': id = staff ID (optional: choose), room = date (optional: the last open day). DEC-075 correction of a PAST day of the open
 *  pay month: a mark, a full-day back-dated leave or "not marked". Raising pay → a request the Owner approves; lowering or keeping it → applies now. */
export function HRAttCorrectModal({id,room}:{id?:string;room?:string}){
  const {s,uid,run,closeModal,busy}=useHcc();const x=hrSlice(s),w=hrCorrectionWindow(s),pay=hrCanPay(s,uid),owner=hrCanRates(s,uid);
  const choices=x.staff.filter(t=>t.userId!==uid);// F-10: never your own record
  const [sid,setSid]=useState(id??choices.find(t=>t.active)?.id??'');
  const [date,setDate]=useState(room&&room>=w.from&&room<=w.to?room:w.to);
  const [mark,setMark]=useState('P');const [ot,setOt]=useState('0');const [reason,setReason]=useState('');
  const st=hrStaffById(s,sid);
  if(w.to<w.from)return <Empty title="No day to correct" body="The open pay month has no past day yet."/>;
  if(!st)return <Empty title="Staff member unavailable"/>;
  const otMin=OT_MARKS.includes(mark)?Number(ot)||0:0,target=hrParseEntry(mark,otMin);
  const refusal=DATE_OK(date)?hrCorrectionDateRefusal(s,date)?.msg:'Choose the day to correct.';
  const plan=typeof target==='string'||refusal?undefined:hrPlanCorrection(s,st,date,target);
  const why=typeof target==='string'?target:refusal??(plan&&'refusal' in plan?plan.refusal.msg:undefined);
  const effect=plan&&!('refusal' in plan)?plan.effect:0,sends=effect>0&&!owner;
  const cur=refusal?undefined:hrDayEntry(s,st,date),hol=refusal?undefined:hrPaidHolidayOn(s,date);
  const outcome=effect>0?(owner?'Raises pay — as the Owner your correction applies now (audited).':`Raises pay${pay?` by ${money(effect)}`:''} — sent to the Owner for approval; nothing changes until then.`)
    :effect<0?`Lowers pay${pay?` by ${money(-effect)}`:''} — applies now (audited).`:'Pay unchanged — applies now (audited).';
  const types:HRLeaveType[]=st.payType==='daily'?['LOP']:HR_LEAVE_TYPES;
  return <><div className="modal-heading"><h2>Correct a past day</h2><p>Open pay month: {dateLabel(w.from)} – {dateLabel(w.to)}. A locked month never changes. A correction that raises pay needs the Owner’s approval; one that lowers or keeps pay applies at once. Every correction is audited with its reason.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_ATT_CORRECT',staffId:st.id,date,mark,otMin,reason:reason.trim()},sends?'Correction sent to the Owner for approval':'Attendance corrected',{close:true});}}>
      {id?<p className="hr-who"><strong>{st.name}</strong> · {st.designation}</p>
        :<label>Staff member<select value={sid} onChange={e=>setSid(e.target.value)}>{choices.map(t=><option key={t.id} value={t.id}>{t.name}{t.active?'':' (exited)'}</option>)}</select></label>}
      <label>Day<input type="date" required min={w.from} max={w.to} value={date} onChange={e=>setDate(e.target.value)}/></label>
      {cur&&<div className="info-note"><Info size={16}/><span>Now: <strong>{hrEntryLabel(cur.entry)}</strong> · rostered {hrShiftInfo(hrShiftFor(s,st,date)).label}{hol?` · paid holiday (${hol.name})`:''}{cur.halfDayLeave?' · half-day leave':''}</span></div>}
      <label>Correct to<select value={mark} onChange={e=>setMark(e.target.value)}>
        <optgroup label="Attendance">{HR_ATT_MARKS.map(k=><option key={k} value={k}>{HR_ATT_LABEL[k]}</option>)}</optgroup>
        <optgroup label="Leave (back-dated, full day)">{types.map(t=><option key={t} value={t}>{HR_LEAVE_LABEL[t]} ({t})</option>)}</optgroup>
        <option value="none">Not marked (clear the day)</option></select></label>
      {OT_MARKS.includes(mark)&&<label>Overtime (minutes, 0–480)<input type="number" inputMode="numeric" min="0" max="480" step="15" value={ot} onChange={e=>setOt(e.target.value)}/></label>}
      <label>Reason<input required minLength={3} maxLength={200} value={reason} onChange={e=>setReason(e.target.value)} placeholder="e.g. duty register checked" enterKeyHint="done"/></label>
      {why?<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/><span>{why}</span></div>:<div className="info-note"><Info size={16}/><span>{outcome}</span></div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!!why}>{sends?'Send to the Owner':'Save correction'}</button></div>
    </form></>;
}
const DATE_OK=(d:string)=>/^\d{4}-\d{2}-\d{2}$/.test(d);

/** 'hr-request-decide': id = request ID, room = 'approve' | 'reject'. The Owner decides an attendance correction or a bank-detail change. */
export function HRRequestDecideModal({id,room}:{id?:string;room?:string}){
  const {s,uid,run,closeModal,busy}=useHcc();const r=(hrSlice(s).requests??[]).find(q=>q.id===id);const [note,setNote]=useState('');
  if(!r)return <Empty title="Request unavailable"/>;
  const approve=room!=='reject',st=hrStaffById(s,r.staffId),pay=hrCanPay(s,uid),bank=r.kind==='bank';
  const what=bank?`bank details ${bankText(r.bankBefore)} → ${bankText(r.bankAfter)}`:`${r.date?shortDate(r.date):''}: ${r.before?hrEntryLabel(r.before):'—'} → ${r.after?hrEntryLabel(r.after):'—'}`;
  return <><div className="modal-heading"><h2>{approve?'Approve':'Reject'} {bank?'bank-detail change':'attendance correction'}</h2>
    <p>{r.id} · {st?.name} · {what}{!bank&&pay&&r.payEffect!==undefined?` · raises gross pay by ${money(r.payEffect)}`:''} · “{r.reason}” · requested by {hrUserName(s,r.requestedBy)}</p></div>
    {approve&&<div className="info-note"><Info size={16}/><span>{bank?'Approving makes these the details for future pay-runs and bank files; locked runs keep their frozen details.':`The day changes and the ${hrMonthLabel(r.date!.slice(0,7))} draft pay-run (if any) is recalculated.`}</span></div>}
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_REQUEST_DECIDE',id:r.id,approve,note:note.trim()},approve?'Request approved':'Request rejected',{close:true});}}>
      <label>{approve?'Note (optional)':'Reason for rejecting'}<input required={!approve} minLength={approve?undefined:3} maxLength={200} value={note} onChange={e=>setNote(e.target.value)} enterKeyHint="done"/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className={'btn '+(approve?'primary':'danger')} disabled={busy}>{approve?'Approve':'Reject'}</button></div>
    </form></>;
}

/** 'hr-holiday': id = holiday date (rename) or undefined (add). DEC-083 paid-holiday calendar, Owner only. */
export function HRHolidayModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const cur=id?hrPaidHolidayOn(s,id):undefined;
  const [f,setF]=useState({date:cur?.date??s.date,name:cur?.name??''});
  const locked=DATE_OK(f.date)&&hrRunFor(s,f.date.slice(0,7))?.status==='Locked',taken=!cur&&DATE_OK(f.date)?hrPaidHolidayOn(s,f.date):undefined;
  return <><div className="modal-heading"><h2>{cur?'Rename paid holiday':'Add paid holiday'}</h2><p>Paid like a weekly off: monthly staff keep their pay; daily-wage staff as for weekly offs (CA to confirm). Staff who work that day are marked present. Leave dates skip it.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_HOLIDAY_SAVE',date:f.date,name:f.name.trim()},cur?'Holiday renamed':'Paid holiday added',{close:true});}}>
      <label>Date<input type="date" required disabled={!!cur} value={f.date} onChange={e=>setF({...f,date:e.target.value})}/></label>
      <label>Name<input required minLength={2} maxLength={60} value={f.name} onChange={e=>setF({...f,name:e.target.value})} placeholder="e.g. Founders’ Day (fictional)" enterKeyHint="done"/></label>
      {locked&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/><span>{hrMonthLabel(f.date.slice(0,7))} payroll is locked — its holidays cannot change.</span></div>}
      {taken&&<div className="info-note"><Info size={16}/><span>{taken.name} is already on this date — saving renames it.</span></div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
        {cur&&!locked&&<button type="button" className="btn danger" disabled={busy} onClick={()=>{if(window.confirm(`Remove ${cur.name} from the paid-holiday calendar?`))run({type:'HR_HOLIDAY_REMOVE',date:cur.date},'Paid holiday removed',{close:true});}}><Trash2 size={16}/> Remove</button>}
        <button className="btn primary" disabled={busy||locked}>{cur?'Save name':'Add holiday'}</button></div>
    </form></>;
}

/** 'hr-leave': id = staff ID (optional for managers). Own request for everyone else. Full day or half day (SCC). */
export function HRLeaveModal({id}:{id?:string}){
  const {s,uid,run,closeModal,busy}=useHcc();const x=hrSlice(s),mgr=hrCanManage(s,uid),me=hrStaffOfUser(s,uid);
  const [sid,setSid]=useState(id??me?.id??(mgr?x.staff.find(t=>t.active)?.id:'')??'');
  const st=hrStaffById(s,sid);
  const [f,setF]=useState({type:(st?.payType==='daily'?'LOP':'CL') as HRLeaveType,from:s.date,to:s.date,reason:'',part:'full' as 'full'|'AM'|'PM'});
  if(!st)return <Empty title="No staff record linked" body="Your demo user is not linked to a staff record."/>;
  const types:HRLeaveType[]=hrLeaveChoices(s,st.payType).map(r=>r.code);
  const type=types.includes(f.type)?f.type:types[0];
  const half=f.part==='full'?undefined:f.part,to=half?f.from:f.to;
  const days=to>=f.from?hrLeaveDays(s,st,f.from,to,half):0;const bal=hrLeaveBalance(s,st.id,f.from);
  return <><div className="modal-heading"><h2>Request leave</h2><p>Another manager approves — nobody can approve their own request. Weekly offs and paid holidays are not counted. At most {hrLeavePolicy(s).deptCap[st.dept]??hrLeavePolicy(s).defaultCap} {hrDeptName(s,st.dept)} staff can be off on the same day.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_LEAVE_REQUEST',staffId:st.id,leaveType:type,from:f.from,to,...(half?{half}:{}),reason:f.reason},'Leave request sent for approval',{close:true});}}>
      {mgr&&!id&&<label>Staff member<select value={sid} onChange={e=>setSid(e.target.value)}>{x.staff.filter(t=>t.active).map(t=><option key={t.id} value={t.id}>{t.name}</option>)}</select></label>}
      {(!mgr||id)&&<p className="hr-who"><strong>{st.name}</strong> · {st.designation}</p>}
      <label>Leave type<select value={type} onChange={e=>setF({...f,type:e.target.value as HRLeaveType})}>{types.map(t=><option key={t} value={t}>{hrLeaveName(s,t)} ({t}){hrPaidLeave(t,s)?'':' — unpaid'}</option>)}</select></label>
      <div className="hr-status-pick" role="radiogroup" aria-label="Full or half day">{([['full','Full day(s)'],['AM','First half'],['PM','Second half']] as const).map(([k,l])=>
        <label key={k} className={f.part===k?'on':''}><input type="radio" name="hr-part" checked={f.part===k} onChange={()=>setF({...f,part:k})}/>{l}</label>)}</div>
      <div className="form-row"><label>{half?'Date':'From'}<input type="date" required min={s.date} value={f.from} onChange={e=>setF({...f,from:e.target.value,to:e.target.value>f.to?e.target.value:f.to})}/></label>
        {!half&&<label>To<input type="date" required min={f.from} value={f.to} onChange={e=>setF({...f,to:e.target.value})}/></label>}</div>
      <label>Reason<input required minLength={3} maxLength={120} value={f.reason} onChange={e=>setF({...f,reason:e.target.value})} enterKeyHint="send"/></label>
      <div className="info-note"><Info size={16}/><span>{days} working day{days===1?'':'s'} · {st.payType==='daily'?'Daily-wage staff: leave without pay only (illustrative policy).':`Balance ${hrFy(f.from).label}: `+bal.map(b=>`${b.type} ${b.left}/${b.entitled}${b.over?` (over by ${b.over})`:''}`).join(' · ')}</span></div>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||days<=0}>Send request</button></div>
    </form></>;
}

/** 'hr-leave-decide': id = leave ID, room = 'approve' | 'reject'. */
export function HRLeaveDecideModal({id,room}:{id?:string;room?:string}){
  const {s,run,closeModal,busy}=useHcc();const l=hrSlice(s).leaves.find(x=>x.id===id);const [note,setNote]=useState('');
  if(!l)return <Empty title="Leave request unavailable"/>;
  const approve=room!=='reject',st=hrStaffById(s,l.staffId);
  return <><div className="modal-heading"><h2>{approve?'Approve leave':'Reject leave'}</h2><p>{l.id} · {st?.name} · {hrLeaveName(s,l.type)}{l.half?` (${l.half==='AM'?'first':'second'} half)`:''} · {shortDate(l.from)}{l.to!==l.from?` – ${shortDate(l.to)}`:''} · {l.days} day{l.days===1?'':'s'} · “{l.reason}” · requested by {hrUserName(s,l.requestedBy)}</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_LEAVE_DECIDE',id:l.id,approve,note},approve?'Leave approved':'Leave rejected',{close:true});}}>
      <label>{approve?'Note (optional)':'Reason for rejecting'}<input required={!approve} maxLength={120} value={note} onChange={e=>setNote(e.target.value)} enterKeyHint="done"/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className={'btn '+(approve?'primary':'danger')} disabled={busy}>{approve?'Approve':'Reject'}</button></div>
    </form></>;
}

/** 'hr-advance': salary advance with monthly recovery (0 = full balance next payroll, SCC). */
export function HRAdvanceModal(){
  const {s,uid,run,closeModal,busy}=useHcc();const x=hrSlice(s);
  const eligible=x.staff.filter(t=>t.active&&t.userId!==uid);// F-10: never your own advance
  const [f,setF]=useState({staffId:eligible[0]?.id??'',amount:'',installment:'',fromMonth:s.date.slice(0,7),reason:''});
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement>)=>setF({...f,[k]:e.target.value});
  const months=[s.date.slice(0,7),hrNextMonth(s.date.slice(0,7)),hrNextMonth(hrNextMonth(s.date.slice(0,7)))];
  return <><div className="modal-heading"><h2>Salary advance</h2><p>Recovered from net pay when payroll is locked (never more than net pay). Paid by simulated bank transfer — no effect on the cash drawer.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_ADVANCE_ADD',staffId:f.staffId,amount:rs(f.amount),installment:rs(f.installment||'0'),fromMonth:f.fromMonth,reason:f.reason},'Advance recorded',{close:true});}}>
      <label>Staff member<select value={f.staffId} onChange={set('staffId')}>{eligible.map(t=><option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
      <div className="form-row"><label>Amount (₹)<input required type="number" inputMode="decimal" min="1" max="1000000" step="0.01" value={f.amount} onChange={set('amount')}/></label>
        <label>Monthly recovery (₹, blank = all at once)<input type="number" inputMode="decimal" min="0" step="0.01" value={f.installment} onChange={set('installment')}/></label></div>
      <label>Recover from<select value={f.fromMonth} onChange={set('fromMonth')}>{months.map(m=><option key={m} value={m}>{hrMonthLabel(m)} payroll</option>)}</select></label>
      <label>Reason<input required maxLength={120} value={f.reason} onChange={set('reason')} enterKeyHint="done"/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Record advance</button></div>
    </form></>;
}

/** 'hr-rates': new dated statutory rate version (Owner). */
export function HRRatesModal(){
  const {s,run,closeModal,busy}=useHcc();
  const last=hrLastLockedMonth(s),first=last?hrNextMonth(last):s.date.slice(0,7);
  const months=[first];for(let i=0;i<11;i++)months.push(hrNextMonth(months[months.length-1]));
  const pc=(b:number)=>String(b/100);
  // F-05: the form starts from the version in force on the chosen month and sends only the fields that differ from it.
  const formFor=(m:string)=>{const r=hrRatesFor(s,m);return {from:m,basic:pc(r.basicBps),hra:pc(r.hraBps),wash:pc(r.washBps),pfEe:pc(r.pfEeBps),pfEr:pc(r.pfErBps),pfCap:toRs(r.pfWageCap),esicEe:pc(r.esicEeBps),esicEr:pc(r.esicErBps),esicCeiling:toRs(r.esicCeiling),
    ptMaleExempt:toRs(r.ptMaleExempt),ptMaleMid:toRs(r.ptMaleMid),ptMaleMidAmt:toRs(r.ptMaleMidAmt),ptStd:toRs(r.ptStd),ptFeb:toRs(r.ptFeb),ptFemaleExempt:toRs(r.ptFemaleExempt),ot:String(r.otBps/10000),late:String(r.lateBlock),days:String(r.daysOverride??0),rounding:r.rounding as string,esicPeriod:r.esicPeriod?'yes':'no'};};
  const [f,setF]=useState(()=>formFor(hrRatesDefaultFrom(s).slice(0,7)));
  const past=hrRatesPastMonths(s,f.from+'-01');
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement>)=>setF(k==='from'?formFor(e.target.value):{...f,[k]:e.target.value});
  const bps=(v:string)=>Math.round(Number(v)*100);
  const laterVersions=hrRateVersions(s).filter(v=>v.from>f.from+'-01').map(v=>v.from);
  function save(e:React.FormEvent){e.preventDefault();
    const next:Omit<HRRates,'from'>={basicBps:bps(f.basic),hraBps:bps(f.hra),washBps:bps(f.wash),pfEeBps:bps(f.pfEe),pfErBps:bps(f.pfEr),pfWageCap:rs(f.pfCap),esicEeBps:bps(f.esicEe),esicErBps:bps(f.esicEr),esicCeiling:rs(f.esicCeiling),
      ptMaleExempt:rs(f.ptMaleExempt),ptMaleMid:rs(f.ptMaleMid),ptMaleMidAmt:rs(f.ptMaleMidAmt),ptStd:rs(f.ptStd),ptFeb:rs(f.ptFeb),ptFemaleExempt:rs(f.ptFemaleExempt),otBps:Math.round(Number(f.ot)*10000),lateBlock:Number(f.late),daysOverride:Number(f.days),rounding:f.rounding as HRRates['rounding'],esicPeriod:f.esicPeriod==='yes'};
    const cur=hrRatesFor(s,f.from),diff=Object.fromEntries(Object.entries(next).filter(([k,v])=>(cur[k as keyof HRRates]??(k==='daysOverride'?0:undefined))!==v));
    run({type:'HR_STAT_RATES_SET',from:f.from+'-01',...diff},'Statutory rates saved',{close:true});}
  const num=(k:keyof typeof f,label:string,step='0.01')=><label>{label}<input required type="number" inputMode="decimal" min="0" step={step} value={f[k]} onChange={set(k)}/></label>;
  return <><div className="modal-heading"><h2>Statutory rates — new version</h2><p>Illustrative — CA to confirm for a hotel. A version takes effect from the 1st of a month after the last locked pay-run ({last?hrMonthLabel(last):'none yet'}); locked runs keep the rates they were calculated with.</p></div>
    <form className="form" onSubmit={save}>
      <label>Effective from<select value={f.from} onChange={set('from')}>{months.map(m=><option key={m} value={m}>1 {hrMonthLabel(m)}</option>)}</select></label>
      <div className="info-note"><Info size={16}/><span>Values below are the version in force for {hrMonthLabel(f.from)}; only the fields you change are saved.{past.length?` Warning: ${past.map(hrMonthLabel).join(', ')} ${past.length===1?'is':'are'} complete but not locked — ${past.length===1?'its':'their'} payroll changes and must be recalculated.`:''}{laterVersions.length?` A later version exists (from ${laterVersions.join(', ')}): your change is carried into it unless it set its own value for that field.`:''}</span></div>
      <fieldset className="hr-fieldset"><legend>Salary structure</legend><div className="form-row">{num('basic','Basic (% of earned)')}{num('hra','HRA (% of basic)')}</div>{num('wash','Washing allowance (% of basic)')}</fieldset>
      <fieldset className="hr-fieldset"><legend>Provident Fund</legend><div className="form-row">{num('pfEe','Employee (%)')}{num('pfEr','Employer (%)')}</div>{num('pfCap','PF wage cap (₹)')}</fieldset>
      <fieldset className="hr-fieldset"><legend>ESIC</legend><div className="form-row">{num('esicEe','Employee (%)')}{num('esicEr','Employer (%)')}</div>{num('esicCeiling','Wage ceiling (₹)')}
        <label>Coverage rule <small>(illustrative — CA to confirm)</small><select value={f.esicPeriod} onChange={set('esicPeriod')}><option value="no">Month by month on earned pay (SCC)</option><option value="yes">Contribution period (Apr–Sep / Oct–Mar): decided at the period start, kept to its end</option></select></label></fieldset>
      <fieldset className="hr-fieldset"><legend>Professional tax (Maharashtra)</legend>
        <div className="form-row">{num('ptMaleExempt','Men: nil up to (₹)')}{num('ptMaleMid','Men: middle slab up to (₹)')}</div>
        <div className="form-row">{num('ptMaleMidAmt','Middle slab (₹ / month)')}{num('ptStd','Standard (₹ / month)')}</div>
        <div className="form-row">{num('ptFeb','February top slab (₹)')}{num('ptFemaleExempt','Women: nil up to (₹)')}</div></fieldset>
      <div className="form-row">{num('ot','OT day rate (× a day’s pay)')}<label>Total days of a month (0 = calendar days, SCC)<input required type="number" inputMode="numeric" min="0" max="31" step="1" value={f.days} onChange={set('days')}/></label></div>
      <div className="form-row"><label>Late marks per ½-day loss (0 = off)<input required type="number" inputMode="numeric" min="0" max="10" step="1" value={f.late} onChange={set('late')}/></label></div>
      <label>Rounding<select value={f.rounding} onChange={set('rounding')}><option value="paise">To paise (SCC)</option><option value="rupee">PF nearest rupee · ESIC next higher rupee</option></select></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Save version</button></div>
    </form></>;
}

/** 'hr-leave-policy': SCC entitlements and max staff off per department per day (Owner). */
export function HRLeavePolicyModal(){
  const {s,run,closeModal,busy}=useHcc();const p=hrLeavePolicy(s),depts=hrSlice(s).depts;
  const [f,setF]=useState({CL:String(p.ent.CL),SL:String(p.ent.SL),EL:String(p.ent.EL),cap:String(p.defaultCap),dept:Object.fromEntries(depts.map(d=>[d.id,p.deptCap[d.id]!==undefined?String(p.deptCap[d.id]):''])) as Record<string,string>});
  function save(e:React.FormEvent){e.preventDefault();
    const deptCap:Record<string,number>={};for(const [d,v] of Object.entries(f.dept))if(v!=='')deptCap[d]=Number(v);
    run({type:'HR_LEAVE_POLICY_SET',ent:{CL:Number(f.CL),SL:Number(f.SL),EL:Number(f.EL)},defaultCap:Number(f.cap),deptCap},'Leave policy saved',{close:true});}
  const over=hrLeaveOverCount(s,{CL:Number(f.CL),SL:Number(f.SL),EL:Number(f.EL)});// F-15
  const n=(v:string,on:(v:string)=>void,label:string,max=60,req=true)=><label>{label}<input required={req} type="number" inputMode="numeric" min={req?0:1} max={max} step="1" value={v} onChange={e=>on(e.target.value)}/></label>;
  return <><div className="modal-heading"><h2>Leave policy</h2><p>Days per financial year (April–March). Applies to new requests; existing requests keep their dates.</p></div>
    <form className="form" onSubmit={save}>
      <div className="form-row">{n(f.CL,v=>setF({...f,CL:v}),'Casual leave')}{n(f.SL,v=>setF({...f,SL:v}),'Sick leave')}</div>
      <div className="form-row">{n(f.EL,v=>setF({...f,EL:v}),'Earned leave')}<label>Max staff off per department per day<input required type="number" inputMode="numeric" min="1" max="20" step="1" value={f.cap} onChange={e=>setF({...f,cap:e.target.value})}/></label></div>
      {over>0&&<div className="info-note hr-warn"><Info size={16}/><span>{over} staff member{over===1?' has':'s have'} already taken or requested more than these entitlements this year. Their balance shows 0 left and "over by N"; approved leave is not changed.</span></div>}
      <fieldset className="hr-fieldset"><legend>Department limits (blank = {f.cap||'default'})</legend><div className="hr-cap-grid">{depts.map(d=>n(f.dept[d.id],v=>setF({...f,dept:{...f.dept,[d.id]:v}}),d.name,20,false))}</div></fieldset>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Save policy</button></div>
    </form></>;
}

/** 'hr-unlock': id = run ID. Owner unlock with a reason (SCC unlockRun). */
export function HRUnlockModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const r=hrSlice(s).runs.find(x=>x.id===id);const [reason,setReason]=useState('');const [ack,setAck]=useState(false);
  if(!r)return <Empty title="Payroll run unavailable"/>;
  const files=r.bankFiles??[],sent=r.sent??[];
  return <><div className="modal-heading"><h2>Unlock {hrMonthLabel(r.month)} payroll</h2><p>Payslips are withdrawn, advance recoveries of this run are reversed and the run goes back to Draft. Recalculate and lock again (maker-checker).</p></div>
    {sent.length>0&&<div className="info-note hr-warn"><AlertTriangle size={16}/><span>{sent.length} payslip{sent.length===1?' was':'s were'} already sent on WhatsApp. They are marked superseded; staff whose net changes are flagged for a re-send after the relock.</span></div>}
    {files.length>0&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/><span>A bank transfer file was already generated ({files.length}×, last {files[files.length-1].at}, net {money(files[files.length-1].net)}). If the bank has paid it, a new full file would pay twice.</span></div>}
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_PAYROLL_UNLOCK',id:r.id,reason,...(files.length?{confirmBankFile:ack}:{})},'Payroll unlocked',{close:true});}}>
      <label>Reason<input required minLength={5} maxLength={120} value={reason} onChange={e=>setReason(e.target.value)} enterKeyHint="done"/></label>
      {files.length>0&&<label className="check"><input type="checkbox" checked={ack} onChange={e=>setAck(e.target.checked)}/> I understand the bank file was already issued</label>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn danger" disabled={busy||(files.length>0&&!ack)}>Unlock</button></div>
    </form></>;
}

/** 'hr-fnf': id = staff ID. Full and final settlement preview + confirm (SCC fnfCompose / fnfGenerate). */
export function HRFnfModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const st=id?hrStaffById(s,id):undefined;
  const [adj,setAdj]=useState<{label:string;kind:'add'|'ded';amount:string}[]>([]);
  if(!st||!st.exitedOn)return <Empty title="Record the exit first" body="Full and final is prepared after the staff member is marked as exited."/>;
  const clean:HRFnfAdj[]=adj.filter(a=>a.label.trim()&&Number(a.amount)>0).map(a=>({label:a.label.trim(),kind:a.kind,amount:rs(a.amount)}));
  const c=hrFnfCompute(s,st,clean),enc=hrElEncashment(s,st);
  const row=(l:string,v:number,sign='')=><div key={l}><span>{l}</span><strong className="number">{sign}{money(v)}</strong></div>;
  return <><div className="modal-heading"><h2>Full and final · {st.name}</h2><p>Last day {dateLabel(c.lastDay)}. {c.salaryIncluded?`Includes ${hrMonthLabel(c.month)} salary to the last day.`:`${hrMonthLabel(c.month)} salary was already paid in the locked run.`} The outstanding advance is recovered up to what the settlement covers; the rest stays open. Simulated bank transfer.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_FNF_SETTLE',staffId:st.id,adjustments:clean},'Draft settlement prepared — the other payroll user confirms',{close:true});}}>
      <div className="summary-list hr-fnf-sum">
        {c.line&&row(`Salary ${hrMonthLabel(c.month)} (${c.line.payableDays} days)`,c.line.earned,'+ ')}{c.line&&!!c.line.ot&&row('Overtime',c.line.ot,'+ ')}
        {clean.filter(a=>a.kind==='add').map(a=>row(a.label,a.amount,'+ '))}
        {c.line&&!!c.line.pf&&row('PF (employee)',c.line.pf,'− ')}{c.line&&!!c.line.esic&&row('ESIC (employee)',c.line.esic,'− ')}{c.line&&!!c.line.pt&&row('Professional tax',c.line.pt,'− ')}
        {clean.filter(a=>a.kind==='ded').map(a=>row(a.label,a.amount,'− '))}
        {!!c.advanceOutstanding&&row(c.advanceLeftOpen?`Advance recovered (of ${money(c.advanceOutstanding)} outstanding)`:'Advance outstanding (full recovery)',c.advanceRecovered,'− ')}
      </div>
      {c.advanceLeftOpen>0&&<div className="info-note hr-warn" role="note"><AlertTriangle size={16}/><span>The settlement does not cover the whole advance: {money(c.advanceLeftOpen)} stays open as an advance the employee still owes.</span></div>}
      <div className="hr-ps-net"><span>{c.net<0?'Recoverable from employee':'Net payable'}</span><strong className="number">{money(Math.abs(c.net))}</strong></div>
      <fieldset className="hr-fieldset"><legend>Adjustments (up to 4)</legend>
        {adj.map((a,i)=><div key={i} className="hr-adj-row">
          <input aria-label="Adjustment label" maxLength={80} value={a.label} placeholder="Label" onChange={e=>setAdj(adj.map((q,j)=>j===i?{...q,label:e.target.value}:q))}/>
          <select aria-label="Addition or deduction" value={a.kind} onChange={e=>setAdj(adj.map((q,j)=>j===i?{...q,kind:e.target.value as 'add'|'ded'}:q))}><option value="add">Addition</option><option value="ded">Deduction</option></select>
          <input aria-label="Amount in rupees" type="number" inputMode="decimal" min="0" step="0.01" value={a.amount} placeholder="₹" onChange={e=>setAdj(adj.map((q,j)=>j===i?{...q,amount:e.target.value}:q))}/>
          <button type="button" className="btn" aria-label="Remove adjustment" onClick={()=>setAdj(adj.filter((_,j)=>j!==i))}><Trash2 size={16}/></button></div>)}
        <div className="inline">{adj.length<4&&<button type="button" className="btn" onClick={()=>setAdj([...adj,{label:'',kind:'add',amount:''}])}><Plus size={16}/> Add adjustment</button>}
          {adj.length<4&&enc>0&&!adj.some(a=>/encashment/i.test(a.label))&&<button type="button" className="btn" onClick={()=>setAdj([...adj,{label:'Earned leave encashment',kind:'add',amount:String(enc/100)}])}><Plus size={16}/> EL encashment {money(enc)}</button>}</div></fieldset>
      <div className="info-note"><Info size={16}/><span>Maker-checker: this saves a draft. The other payroll user confirms it{clean.filter(a=>a.kind==='add').reduce((n,a)=>n+a.amount,0)>HR_FNF_OWNER_ABOVE?` — additions above ${money(HR_FNF_OWNER_ABOVE)} need the Owner`:''}. Each adjustment is limited to {money(HR_FNF_ADJ_MAX)}.</span></div>
      <p className="footnote">{HR_DISCLAIMER} {HR_NOT_CALCULATED}</p>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||clean.some(a=>a.amount>HR_FNF_ADJ_MAX)}>Prepare draft settlement</button></div>
    </form></>;
}

/** Payslip body (shared by the dialog and My shifts & pay). */
export function HRPayslipDoc({run,line}:{run:HRRun;line:HRLine}){
  const {s}=useHcc();const c=line.counts,d=line.days,R=(label:string,v:React.ReactNode)=><div key={label}><span>{label}</span><strong className="number">{v}</strong></div>;
  return <div className="hr-payslip invoice">
    <div className="invoice-top"><Hotel size={28}/><div><h2>{s.settings.hotel}</h2><p>Payslip · {hrMonthLabel(run.month)} · {run.id}</p></div></div>
    <div className="invoice-stamp">{run.status==='Locked'?'DEMONSTRATION PAYSLIP':'DRAFT — NOT FINAL'} · FICTIONAL DATA · ILLUSTRATIVE — CA TO CONFIRM FOR A HOTEL</div>
    <div className="hr-ps-head"><div><small>EMPLOYEE</small><h3>{line.name}</h3><p>{line.designation} · {hrDeptName(s,line.dept)} · {line.staffId}</p></div>
      <div><small>PAY BASIS</small><h3>{line.payType==='monthly'?`${money(line.base)} / month`:`${money(line.base)} / day`}</h3><p>{d?`${d.total} total days · salary days ${line.payableDays}${d.otDays?` + ${d.otDays} OT`:''}`:`${c.days} calendar days · payable ${line.payableDays}`}</p></div></div>
    <div className="hr-ps-grid">
      {d?<section><h4>Attendance (SCC)</h4><div className="summary-list">{!!c.mgmtHol&&R('Paid holidays (off the total days)',c.mgmtHol)}{R('Total days',d.total)}{R('Present',d.present)}{R('Absent (incl. weekly offs)',d.absent)}{R('Half days',d.half)}{!!d.noThumb&&R('No-thumb marks (½ day each)',d.noThumb)}{R('Late marks',d.late)}{!!d.lateDed&&R('Late deduction',d.lateDed)}{R('Total deduction',d.deduction)}{R('Leaves',d.leave)}{R('Final present',d.finalPresent)}{R('Extra days',d.extra)}{R('Salary days',d.salaryDays)}{R('OT days',d.otDays)}</div></section>:
      <section><h4>Attendance</h4><div className="summary-list">{R('Present',c.P)}{R('Late',c.L)}{R('Half-days',(c.H||0)+(c.HL||0))}{R('Absent / not marked',c.A+c.unmarked)}{R('Paid leave',c.paidLeave)}{R('Leave without pay',c.lopLeave)}{R('Weekly offs',c.off)}{!!c.hol&&R('Paid holidays',c.hol)}{!!c.OFFW&&R('Worked on off day',c.OFFW)}{R('Overtime',`${Math.round(c.otMin/6)/10} h`)}{!!line.latePenaltyHalf&&R('Late rule (½-days)',line.latePenaltyHalf)}</div></section>}
      <section><h4>Earnings</h4><div className="summary-list">{R('Basic',money(line.basic))}{line.structure==='S'&&<>{R('HRA',money(line.hra))}{R('Washing allowance',money(line.washing))}{R('Special allowance',money(line.special))}</>}{R(d?`OT days (${d.otDays})`:'Overtime',money(line.ot))}{R('Gross earnings',money(line.gross))}</div>
        <h4>Deductions</h4><div className="summary-list">{R(`Provident Fund (${(run.rates?.pfEeBps??1200)/100}%)`,money(line.pf))}{R(`ESIC (${(run.rates?.esicEeBps??75)/100}%)`,money(line.esic))}{R('Professional tax (MH)',money(line.pt))}{R('Advance recovery',money(line.advance))}{R('Total deductions',money(line.deductions))}</div>
        <h4>Employer contributions <em>(not deducted)</em></h4><div className="summary-list">{R('PF employer',money(line.pfEr))}{R('ESIC employer',money(line.esicEr))}</div></section>
    </div>
    <div className="hr-ps-net"><span>Net pay</span><strong className="number">{money(line.net)}</strong></div>
    <p className="hr-ps-words">{hrAmountWords(line.net)}</p>
    <p className="footnote">{HR_DISCLAIMER} Rates from {run.rates?.from??'—'}{run.legacy?' (legacy run saved before the SCC rules — employer share and pay split not recorded)':''}. Prepared by {hrUserName(s,run.preparedBy)}{run.lockedBy?` · locked by ${hrUserName(s,run.lockedBy)} at ${run.lockedAt?.slice(11)} on ${dateLabel(run.lockedAt!.slice(0,10))} (simulated clock)`:''}. Payment by simulated bank transfer. {HR_NOT_CALCULATED}</p>
  </div>;
}

/** 'hr-payslip': id = "runId|staffId". Own payslip or payroll roles; PDF via the share/print hook; simulated WhatsApp for payroll roles. */
export function HRPayslipModal({id}:{id?:string}){
  const {s,uid,closeModal,run:exec,busy}=useHcc();const [runId,staffId]=(id??'').split('|');
  const run=hrSlice(s).runs.find(r=>r.id===runId),line=run?.lines.find(l=>l.staffId===staffId);
  const me=hrStaffOfUser(s,uid),pay=hrCanPay(s,uid),allowed=pay||(me?.id===staffId&&run?.status==='Locked');
  if(!run||!line||!allowed)return <Empty title="Payslip unavailable" body="Payslips are released when payroll is locked. You can see only your own."/>;
  const st=hrStaffById(s,staffId),sent=(run.sent??[]).filter(x=>x.staffId===staffId);
  // W4 G2: in production the mobile is masked (••••0001); the server's phoneUsable flag enables the send and the server resolves the number.
  const wa=process.env.HCC_BUILD==='production'?(st?.phoneUsable?st.phone:''):st?hrWaNumber(st.phone):'',to=process.env.HCC_BUILD==='production'?wa:`+${wa}`;
  return <><HRPayslipDoc run={run} line={line}/>
    {pay&&run.status==='Locked'&&<div className="hr-wa no-print"><div><strong><MessageCircle size={15}/> WhatsApp (simulated)</strong><small>{wa?`To ${to}`:'No usable mobile number'}{sent.length?` · sent ${sent.length}× · last ${sent[sent.length-1].at.slice(11)} ${shortDate(sent[sent.length-1].at.slice(0,10))}`:''}</small></div>
      <pre>{hrPayslipMessage(s,run,line)}</pre></div>}
    <div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button>
      {pay&&run.status==='Locked'&&<button className="btn" disabled={busy||!wa} onClick={()=>exec({type:'HR_PAYSLIP_SEND',runId:run.id,staffId},'Payslip sent on WhatsApp (simulated — nothing left this device)')}><MessageCircle size={16}/> Send on WhatsApp{wa?` to ${to}`:''}</button>}
      <button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Payslip PDF</button></div></>;
}
