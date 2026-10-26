// Module 17 · HR & payroll — dialogs (render as full-screen sheets on phones). Modal IDs start with 'hr-'.
// W05-B: SCC statutory payslip (PF / ESIC / MH PT breakdown, amount in words, PDF via the native share/print hook, simulated WhatsApp — DEC-056),
// dated statutory rates, leave policy, half-day leave, pay-run unlock and full-and-final settlement.
import {printDocument} from '../../app/native';
import React,{useState} from 'react';
import {Printer,Info,Hotel,MessageCircle,Plus,Trash2,AlertTriangle} from 'lucide-react';
import {useHcc} from '../../app/context';
import {dateLabel,shortDate,Empty} from '../../components/ui';
import {
  money,hrSlice,hrRateVersions,hrCanPay,hrCanManage,hrStaffById,hrStaffOfUser,hrShiftFor,hrShiftInfo,hrDeptName,hrLeaveBalance,hrLeaveDays,hrMonthLabel,hrRatesFor,
  hrLastLockedMonth,hrLeaveOverCount,hrNextMonth,hrRatesDefaultFrom,hrRatesPastMonths,hrCanLink,hrLockedPayslipCount,hrLeavePolicy,hrFnfCompute,hrElEncashment,hrAmountWords,hrPayslipMessage,hrWaNumber,hrFy,hrPaidLeave,
  HR_SHIFTS,HR_WEEKDAYS,HR_FNF_ADJ_MAX,HR_FNF_OWNER_ABOVE,HR_ATT_LABEL,HR_LEAVE_LABEL,HR_LEAVE_TYPES,HR_DISCLAIMER,hrKey,
  type HRShiftCode,type HRAttStatus,type HRLeaveType,type HRRates,type HRFnfAdj,type HRRun,type HRLine,
} from '../../services/mock';

const rs=(v:string)=>Math.round(Number(v)*100);
const toRs=(p:number)=>p?String(p/100):'';
export const hrUserName=(s:ReturnType<typeof useHcc>['s'],id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';

/** 'hr-staff': id = staff ID (edit) or undefined (new). Salary, PF / ESIC and bank fields only for payroll roles. */
export function HRStaffModal({id}:{id?:string}){
  const {s,uid,run,closeModal,busy,open}=useHcc();const x=hrSlice(s),st=id?hrStaffById(s,id):undefined,pay=hrCanPay(s,uid);
  const [f,setF]=useState({name:st?.name??'',dept:st?.dept??x.depts[0]?.id??'',designation:st?.designation??'',userId:st?.userId??'',
    defaultShift:st?.defaultShift??'G',weeklyOff:String(st?.weeklyOff??0),phone:st?.phone??'',gender:st?.gender??'M',payType:st?.payType??'monthly',monthly:toRs(st?.monthly??0),daily:toRs(st?.daily??0),
    joined:s.date,structure:st?.structure??'',pf:st?.pfApplicable===undefined?'':st.pfApplicable?'yes':'no',esic:st?.esicApplicable===false?'no':'yes',bankName:st?.bankName??'',accountNo:st?.accountNo??'',ifsc:st?.ifsc??''});
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement>)=>setF({...f,[k]:e.target.value});
  const linked=new Set(x.staff.filter(o=>o.id!==id&&o.userId).map(o=>o.userId));
  // F-03: only the Owner links logins, and never on their own record.
  const canLink=hrCanLink(s,uid)&&st?.userId!==uid;
  function save(e:React.FormEvent){e.preventDefault();
    const cmd:Record<string,unknown>={type:'HR_STAFF_SAVE',id,name:f.name,dept:f.dept,designation:f.designation,userId:f.userId||undefined,defaultShift:f.defaultShift,weeklyOff:Number(f.weeklyOff),phone:f.phone,gender:f.gender};
    // N3 (production): a new staff member's joining date (server SaveStaff `joined`, not after the business date) — back-dated staff join that month's pay run.
    if(process.env.HCC_BUILD==='production'&&!st&&f.joined)cmd.joined=f.joined;
    const slips=st?hrLockedPayslipCount(s,st.id):0;
    if(st&&f.userId&&f.userId!==(st.userId??'')&&slips){if(!window.confirm(`${st.name} has payslips in ${slips} locked pay-run${slips===1?'':'s'}. The newly linked login will see them. Relink?`))return;cmd.confirmRelink=true;}
    if(pay)Object.assign(cmd,{payType:f.payType,monthly:f.payType==='monthly'?rs(f.monthly):0,daily:f.payType==='daily'?rs(f.daily):0,
      ...(f.structure?{structure:f.structure}:{}),...(f.pf?{pfApplicable:f.pf==='yes'}:{}),esicApplicable:f.esic==='yes',bankName:f.bankName,accountNo:f.accountNo,ifsc:f.ifsc.toUpperCase()});
    run(cmd as {type:string},id?'Staff record updated':'Staff member added',{close:true});}
  return <><div className="modal-heading"><h2>{st?'Edit staff record':'Add staff member'}</h2><p>Fictional staff only. A staff record may link to a demo login; cooks, security and helpers usually have none.</p></div>
    <form className="form" onSubmit={save}>
      <label>Full name<input required minLength={2} maxLength={60} value={f.name} onChange={set('name')} enterKeyHint="next"/></label>
      {process.env.HCC_BUILD==='production'&&!x.depts.length&&<div className="info-note hr-warn" role="alert"><Info size={16}/><span>No departments yet. Add one first, then add staff.{' '}
        <button type="button" className="text-btn" onClick={()=>open({type:'hr-dept'})}>Add a department</button></span></div>}
      <div className="form-row"><label>Department<select required={process.env.HCC_BUILD==='production'} value={f.dept} onChange={set('dept')}>{x.depts.map(d=><option key={d.id} value={d.id}>{d.name}</option>)}</select></label>
        <label>Designation<input required maxLength={60} value={f.designation} onChange={set('designation')}/></label></div>
      {process.env.HCC_BUILD==='production'&&!st&&<label>Joining date<input type="date" required max={s.date} value={f.joined} onChange={set('joined')}/><small>On or before the business date. Pay counts from this date.</small></label>}
      <div className="form-row"><label>Default shift<select value={f.defaultShift} onChange={set('defaultShift')}>{HR_SHIFTS.filter(h=>h.code!=='OFF').map(h=><option key={h.code} value={h.code}>{h.label} {h.start}–{h.end}</option>)}</select></label>
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
        <div className="form-row"><label>Bank (fictional)<input maxLength={60} value={f.bankName} onChange={set('bankName')}/></label>
          <label>Account number<input inputMode="numeric" maxLength={18} value={f.accountNo} onChange={set('accountNo')}/></label></div>
        <label>IFSC<input maxLength={11} value={f.ifsc} onChange={set('ifsc')} placeholder="DEMO0000101"/></label></fieldset>
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
  const pick=(shift:HRShiftCode)=>run({type:'HR_ROSTER_SET',staffId:st.id,date:room,shift},`${st.name} · ${shortDate(room)} → ${hrShiftInfo(shift).label}`,{close:true});
  return <><div className="modal-heading"><h2>Set shift</h2><p>{st.name} · {HR_WEEKDAYS[new Date(room+'T12:00:00Z').getUTCDay()]} {dateLabel(room)}. Currently <strong>{hrShiftInfo(cur).label}</strong>.</p></div>
    <div className="hr-shift-pick">{HR_SHIFTS.map(h=><button key={h.code} type="button" className={'btn'+(h.code===cur?' primary':'')} aria-pressed={h.code===cur} disabled={busy} onClick={()=>pick(h.code)}>
      <strong>{h.label}</strong><span>{h.start?`${h.start}–${h.end}`:'No duty'}</span></button>)}</div>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button></div></>;
}

/** 'hr-att': id = staff ID. Manager marks/corrects today's attendance. */
export function HRAttModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const st=id?hrStaffById(s,id):undefined;
  const prev=st?hrSlice(s).att[hrKey(s.date,st.id)]:undefined;
  const [status,setStatus]=useState<HRAttStatus>(prev?.status??'P');const [ot,setOt]=useState(String(prev?.otMin??0));const [note,setNote]=useState(prev?.note??'');
  if(!st)return <Empty title="Staff member unavailable"/>;
  const shift=hrShiftInfo(hrShiftFor(s,st,s.date));
  return <><div className="modal-heading"><h2>Attendance · {st.name}</h2><p>Business date {dateLabel(s.date)} · rostered {shift.label}{shift.start?` ${shift.start}–${shift.end}`:''}.{prev?` Already recorded as ${HR_ATT_LABEL[prev.status]} — saving corrects it.`:''}</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_ATT_MARK',staffId:st.id,status,otMin:Number(ot)||0,note},`${st.name} · ${HR_ATT_LABEL[status]}`,{close:true});}}>
      <div className="hr-status-pick" role="radiogroup" aria-label="Attendance status">{(['P','L','H','A','OFF'] as HRAttStatus[]).map(k=>
        <label key={k} className={status===k?'on':''}><input type="radio" name="hr-status" checked={status===k} onChange={()=>setStatus(k)}/>{HR_ATT_LABEL[k]}</label>)}</div>
      <label>Overtime (minutes, 0–480)<input type="number" inputMode="numeric" min="0" max="480" step="15" value={ot} disabled={status==='A'} onChange={e=>setOt(e.target.value)}/></label>
      <label>Note<input maxLength={120} value={note} onChange={e=>setNote(e.target.value)} placeholder="Optional" enterKeyHint="done"/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Save attendance</button></div>
    </form></>;
}

/** 'hr-leave': id = staff ID (optional for managers). Own request for everyone else. Full day or half day (SCC). */
export function HRLeaveModal({id}:{id?:string}){
  const {s,uid,run,closeModal,busy}=useHcc();const x=hrSlice(s),mgr=hrCanManage(s,uid),me=hrStaffOfUser(s,uid);
  const [sid,setSid]=useState(id??me?.id??(mgr?x.staff.find(t=>t.active)?.id:'')??'');
  const st=hrStaffById(s,sid);
  const [f,setF]=useState({type:(st?.payType==='daily'?'LOP':'CL') as HRLeaveType,from:s.date,to:s.date,reason:'',part:'full' as 'full'|'AM'|'PM'});
  if(!st)return <Empty title="No staff record linked" body="Your demo user is not linked to a staff record."/>;
  const types:HRLeaveType[]=st.payType==='daily'?['LOP']:HR_LEAVE_TYPES;
  const type=types.includes(f.type)?f.type:types[0];
  const half=f.part==='full'?undefined:f.part,to=half?f.from:f.to;
  const days=to>=f.from?hrLeaveDays(s,st,f.from,to,half):0;const bal=hrLeaveBalance(s,st.id,f.from);
  return <><div className="modal-heading"><h2>Request leave</h2><p>Another manager approves — nobody can approve their own request. Weekly offs are not counted. At most {hrLeavePolicy(s).deptCap[st.dept]??hrLeavePolicy(s).defaultCap} {hrDeptName(s,st.dept)} staff can be off on the same day.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_LEAVE_REQUEST',staffId:st.id,leaveType:type,from:f.from,to,...(half?{half}:{}),reason:f.reason},'Leave request sent for approval',{close:true});}}>
      {mgr&&!id&&<label>Staff member<select value={sid} onChange={e=>setSid(e.target.value)}>{x.staff.filter(t=>t.active).map(t=><option key={t.id} value={t.id}>{t.name}</option>)}</select></label>}
      {(!mgr||id)&&<p className="hr-who"><strong>{st.name}</strong> · {st.designation}</p>}
      <label>Leave type<select value={type} onChange={e=>setF({...f,type:e.target.value as HRLeaveType})}>{types.map(t=><option key={t} value={t}>{HR_LEAVE_LABEL[t]} ({t}){hrPaidLeave(t)?'':' — unpaid'}</option>)}</select></label>
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
  return <><div className="modal-heading"><h2>{approve?'Approve leave':'Reject leave'}</h2><p>{l.id} · {st?.name} · {HR_LEAVE_LABEL[l.type]}{l.half?` (${l.half==='AM'?'first':'second'} half)`:''} · {shortDate(l.from)}{l.to!==l.from?` – ${shortDate(l.to)}`:''} · {l.days} day{l.days===1?'':'s'} · “{l.reason}” · requested by {hrUserName(s,l.requestedBy)}</p></div>
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
    ptMaleExempt:toRs(r.ptMaleExempt),ptMaleMid:toRs(r.ptMaleMid),ptMaleMidAmt:toRs(r.ptMaleMidAmt),ptStd:toRs(r.ptStd),ptFeb:toRs(r.ptFeb),ptFemaleExempt:toRs(r.ptFemaleExempt),ot:String(r.otBps/10000),late:String(r.lateBlock),rounding:r.rounding as string,esicPeriod:r.esicPeriod?'yes':'no'};};
  const [f,setF]=useState(()=>formFor(hrRatesDefaultFrom(s).slice(0,7)));
  const past=hrRatesPastMonths(s,f.from+'-01');
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement>)=>setF(k==='from'?formFor(e.target.value):{...f,[k]:e.target.value});
  const bps=(v:string)=>Math.round(Number(v)*100);
  const laterVersions=hrRateVersions(s).filter(v=>v.from>f.from+'-01').map(v=>v.from);
  function save(e:React.FormEvent){e.preventDefault();
    const next:Omit<HRRates,'from'>={basicBps:bps(f.basic),hraBps:bps(f.hra),washBps:bps(f.wash),pfEeBps:bps(f.pfEe),pfErBps:bps(f.pfEr),pfWageCap:rs(f.pfCap),esicEeBps:bps(f.esicEe),esicErBps:bps(f.esicEr),esicCeiling:rs(f.esicCeiling),
      ptMaleExempt:rs(f.ptMaleExempt),ptMaleMid:rs(f.ptMaleMid),ptMaleMidAmt:rs(f.ptMaleMidAmt),ptStd:rs(f.ptStd),ptFeb:rs(f.ptFeb),ptFemaleExempt:rs(f.ptFemaleExempt),otBps:Math.round(Number(f.ot)*10000),lateBlock:Number(f.late),rounding:f.rounding as HRRates['rounding'],esicPeriod:f.esicPeriod==='yes'};
    const cur=hrRatesFor(s,f.from),diff=Object.fromEntries(Object.entries(next).filter(([k,v])=>cur[k as keyof HRRates]!==v));
    run({type:'HR_STAT_RATES_SET',from:f.from+'-01',...diff},'Statutory rates saved',{close:true});}
  const num=(k:keyof typeof f,label:string,step='0.01')=><label>{label}<input required type="number" inputMode="decimal" min="0" step={step} value={f[k]} onChange={set(k)}/></label>;
  return <><div className="modal-heading"><h2>Statutory rates — new version</h2><p>Illustrative — CA to confirm for a hotel. A version takes effect from the 1st of a month after the last locked pay-run ({last?hrMonthLabel(last):'none yet'}); locked runs keep the rates they were calculated with.</p></div>
    <form className="form" onSubmit={save}>
      <label>Effective from<select value={f.from} onChange={set('from')}>{months.map(m=><option key={m} value={m}>1 {hrMonthLabel(m)}</option>)}</select></label>
      <div className="info-note"><Info size={16}/><span>Values below are the version in force for {hrMonthLabel(f.from)}; only the fields you change are saved.{past.length?` Warning: ${past.map(hrMonthLabel).join(', ')} ${past.length===1?'is':'are'} complete but not locked — ${past.length===1?'its':'their'} payroll changes and must be recalculated.`:''}{laterVersions.length?` A later version exists (from ${laterVersions.join(', ')}): your change is carried into it unless it set its own value for that field.`:''}</span></div>
      <fieldset className="hr-fieldset"><legend>Salary structure</legend><div className="form-row">{num('basic','Basic (% of earned)')}{num('hra','HRA (% of basic)')}</div>{num('wash','Washing allowance (% of basic)')}</fieldset>
      <fieldset className="hr-fieldset"><legend>Provident Fund</legend><div className="form-row">{num('pfEe','Employee (%)')}{num('pfEr','Employer (%)')}</div>{num('pfCap','PF wage cap (₹)')}</fieldset>
      <fieldset className="hr-fieldset"><legend>ESIC</legend><div className="form-row">{num('esicEe','Employee (%)')}{num('esicEr','Employer (%)')}</div>{num('esicCeiling','Wage ceiling (₹)')}
        <label>Coverage rule <small>(illustrative — CA to confirm)</small><select value={f.esicPeriod} onChange={set('esicPeriod')}><option value="yes">Contribution period (Apr–Sep / Oct–Mar): decided at the period start, kept to its end</option><option value="no">Month by month on earned pay (SCC)</option></select></label></fieldset>
      <fieldset className="hr-fieldset"><legend>Professional tax (Maharashtra)</legend>
        <div className="form-row">{num('ptMaleExempt','Men: nil up to (₹)')}{num('ptMaleMid','Men: middle slab up to (₹)')}</div>
        <div className="form-row">{num('ptMaleMidAmt','Middle slab (₹ / month)')}{num('ptStd','Standard (₹ / month)')}</div>
        <div className="form-row">{num('ptFeb','February top slab (₹)')}{num('ptFemaleExempt','Women: nil up to (₹)')}</div></fieldset>
      <div className="form-row">{num('ot','Overtime (× ordinary rate)')}<label>Late marks per ½-day loss (0 = off)<input required type="number" inputMode="numeric" min="0" max="10" step="1" value={f.late} onChange={set('late')}/></label></div>
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
  return <><div className="modal-heading"><h2>Full and final · {st.name}</h2><p>Last day {dateLabel(c.lastDay)}. {c.salaryIncluded?`Includes ${hrMonthLabel(c.month)} salary to the last day.`:`${hrMonthLabel(c.month)} salary was already paid in the locked run.`} The full outstanding advance is recovered. Simulated bank transfer.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_FNF_SETTLE',staffId:st.id,adjustments:clean},'Draft settlement prepared — the other payroll user confirms',{close:true});}}>
      <div className="summary-list hr-fnf-sum">
        {c.line&&row(`Salary ${hrMonthLabel(c.month)} (${c.line.payableDays} days)`,c.line.earned,'+ ')}{c.line&&!!c.line.ot&&row('Overtime',c.line.ot,'+ ')}
        {clean.filter(a=>a.kind==='add').map(a=>row(a.label,a.amount,'+ '))}
        {c.line&&!!c.line.pf&&row('PF (employee)',c.line.pf,'− ')}{c.line&&!!c.line.esic&&row('ESIC (employee)',c.line.esic,'− ')}{c.line&&!!c.line.pt&&row('Professional tax',c.line.pt,'− ')}
        {!!c.advanceOutstanding&&row('Advance outstanding (full recovery)',c.advanceOutstanding,'− ')}
        {clean.filter(a=>a.kind==='ded').map(a=>row(a.label,a.amount,'− '))}
      </div>
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
      <p className="footnote">{HR_DISCLAIMER} Gratuity is not calculated in this prototype.</p>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||clean.some(a=>a.amount>HR_FNF_ADJ_MAX)}>Prepare draft settlement</button></div>
    </form></>;
}

/** Payslip body (shared by the dialog and My shifts & pay). */
export function HRPayslipDoc({run,line}:{run:HRRun;line:HRLine}){
  const {s}=useHcc();const c=line.counts,R=(label:string,v:React.ReactNode)=><div key={label}><span>{label}</span><strong className="number">{v}</strong></div>;
  return <div className="hr-payslip invoice">
    <div className="invoice-top"><Hotel size={28}/><div><h2>{s.settings.hotel}</h2><p>Payslip · {hrMonthLabel(run.month)} · {run.id}</p></div></div>
    <div className="invoice-stamp">{run.status==='Locked'?'DEMONSTRATION PAYSLIP':'DRAFT — NOT FINAL'} · FICTIONAL DATA · ILLUSTRATIVE — CA TO CONFIRM FOR A HOTEL</div>
    <div className="hr-ps-head"><div><small>EMPLOYEE</small><h3>{line.name}</h3><p>{line.designation} · {hrDeptName(s,line.dept)} · {line.staffId}</p></div>
      <div><small>PAY BASIS</small><h3>{line.payType==='monthly'?`${money(line.base)} / month`:`${money(line.base)} / day`}</h3><p>{c.days} calendar days · payable {line.payableDays}</p></div></div>
    <div className="hr-ps-grid">
      <section><h4>Attendance</h4><div className="summary-list">{R('Present',c.P)}{R('Late',c.L)}{R('Half-days',(c.H||0)+(c.HL||0))}{R('Absent / not marked',c.A+c.unmarked)}{R('Paid leave',c.paidLeave)}{R('Leave without pay',c.lopLeave)}{R('Weekly offs',c.off)}{!!c.OFFW&&R('Worked on off day',c.OFFW)}{R('Overtime',`${Math.round(c.otMin/6)/10} h`)}{!!line.latePenaltyHalf&&R('Late rule (½-days)',line.latePenaltyHalf)}</div></section>
      <section><h4>Earnings</h4><div className="summary-list">{R('Basic',money(line.basic))}{line.structure==='S'&&<>{R('HRA',money(line.hra))}{R('Washing allowance',money(line.washing))}{R('Special allowance',money(line.special))}</>}{R('Overtime',money(line.ot))}{R('Gross earnings',money(line.gross))}</div>
        <h4>Deductions</h4><div className="summary-list">{R(`Provident Fund (${(run.rates?.pfEeBps??1200)/100}%)`,money(line.pf))}{R(`ESIC (${(run.rates?.esicEeBps??75)/100}%)`,money(line.esic))}{R('Professional tax (MH)',money(line.pt))}{R('Advance recovery',money(line.advance))}{R('Total deductions',money(line.deductions))}</div>
        <h4>Employer contributions <em>(not deducted)</em></h4><div className="summary-list">{R('PF employer',money(line.pfEr))}{R('ESIC employer',money(line.esicEr))}</div></section>
    </div>
    <div className="hr-ps-net"><span>Net pay</span><strong className="number">{money(line.net)}</strong></div>
    <p className="hr-ps-words">{hrAmountWords(line.net)}</p>
    <p className="footnote">{HR_DISCLAIMER} Rates from {run.rates?.from??'—'}{run.legacy?' (legacy run saved before the SCC rules — employer share and pay split not recorded)':''}. Prepared by {hrUserName(s,run.preparedBy)}{run.lockedBy?` · locked by ${hrUserName(s,run.lockedBy)} at ${run.lockedAt?.slice(11)} on ${dateLabel(run.lockedAt!.slice(0,10))} (simulated clock)`:''}. Payment by simulated bank transfer.</p>
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
