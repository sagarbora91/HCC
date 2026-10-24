// Module 17 · HR & payroll — dialogs (render as full-screen sheets on phones). Modal IDs start with 'hr-'.
import {printDocument} from '../../app/native';
import React,{useState} from 'react';
import {Printer,Info,Hotel} from 'lucide-react';
import {useHcc} from '../../app/context';
import {dateLabel,shortDate,Empty} from '../../components/ui';
import {
  money,hrSlice,hrCanPay,hrCanManage,hrStaffById,hrStaffOfUser,hrShiftFor,hrShiftInfo,hrDeptName,hrLeaveBalance,hrLeaveDays,hrMonthLabel,
  HR_SHIFTS,HR_WEEKDAYS,HR_ATT_LABEL,HR_LEAVE_LABEL,HR_DISCLAIMER,hrKey,
  type HRShiftCode,type HRAttStatus,type HRLeaveType,type HRRates,
} from '../../services/mock';

const rs=(v:string)=>Math.round(Number(v)*100);
const toRs=(p:number)=>p?String(p/100):'';
export const hrUserName=(s:ReturnType<typeof useHcc>['s'],id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';

/** 'hr-staff': id = staff ID (edit) or undefined (new). Salary fields only for payroll roles. */
export function HRStaffModal({id}:{id?:string}){
  const {s,uid,run,closeModal,busy}=useHcc();const x=hrSlice(s),st=id?hrStaffById(s,id):undefined,pay=hrCanPay(s,uid);
  const [f,setF]=useState({name:st?.name??'',dept:st?.dept??x.depts[0]?.id??'',designation:st?.designation??'',userId:st?.userId??'',
    defaultShift:st?.defaultShift??'G',weeklyOff:String(st?.weeklyOff??0),phone:st?.phone??'',payType:st?.payType??'monthly',monthly:toRs(st?.monthly??0),daily:toRs(st?.daily??0)});
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement>)=>setF({...f,[k]:e.target.value});
  const linked=new Set(x.staff.filter(o=>o.id!==id&&o.userId).map(o=>o.userId));
  function save(e:React.FormEvent){e.preventDefault();
    const cmd:Record<string,unknown>={type:'HR_STAFF_SAVE',id,name:f.name,dept:f.dept,designation:f.designation,userId:f.userId||undefined,defaultShift:f.defaultShift,weeklyOff:Number(f.weeklyOff),phone:f.phone};
    if(pay)Object.assign(cmd,{payType:f.payType,monthly:f.payType==='monthly'?rs(f.monthly):0,daily:f.payType==='daily'?rs(f.daily):0});
    run(cmd as {type:string},id?'Staff record updated':'Staff member added',{close:true});}
  return <><div className="modal-heading"><h2>{st?'Edit staff record':'Add staff member'}</h2><p>Fictional staff only. A staff record may link to a demo login; cooks, security and helpers usually have none.</p></div>
    <form className="form" onSubmit={save}>
      <label>Full name<input required minLength={2} maxLength={60} value={f.name} onChange={set('name')} enterKeyHint="next"/></label>
      <div className="form-row"><label>Department<select value={f.dept} onChange={set('dept')}>{x.depts.map(d=><option key={d.id} value={d.id}>{d.name}</option>)}</select></label>
        <label>Designation<input required maxLength={60} value={f.designation} onChange={set('designation')}/></label></div>
      <div className="form-row"><label>Default shift<select value={f.defaultShift} onChange={set('defaultShift')}>{HR_SHIFTS.filter(h=>h.code!=='OFF').map(h=><option key={h.code} value={h.code}>{h.label} {h.start}–{h.end}</option>)}</select></label>
        <label>Weekly off<select value={f.weeklyOff} onChange={set('weeklyOff')}>{HR_WEEKDAYS.map((d,i)=><option key={d} value={i}>{d}</option>)}</select></label></div>
      <div className="form-row"><label>Demo login<select value={f.userId} onChange={set('userId')}><option value="">No login (non-login staff)</option>{s.users.filter(u=>!linked.has(u.id)).map(u=><option key={u.id} value={u.id}>{u.name}</option>)}</select></label>
        <label>Phone (fictional)<input inputMode="tel" maxLength={20} value={f.phone} onChange={set('phone')}/></label></div>
      {pay?<fieldset className="hr-fieldset"><legend>Salary (payroll roles only)</legend>
        <div className="form-row"><label>Pay type<select value={f.payType} onChange={set('payType')}><option value="monthly">Monthly salary</option><option value="daily">Daily wage</option></select></label>
          {f.payType==='monthly'?<label>Monthly salary (₹)<input required type="number" inputMode="decimal" min="0" max="1000000" step="0.01" value={f.monthly} onChange={set('monthly')}/></label>
            :<label>Daily wage (₹)<input required type="number" inputMode="decimal" min="0" max="100000" step="0.01" value={f.daily} onChange={set('daily')}/></label>}</div></fieldset>
        :<div className="info-note"><Info size={16}/>Salary details are set by the Owner or Accountant.</div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>{st?'Save changes':'Add staff member'}</button></div>
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

/** 'hr-leave': id = staff ID (optional for managers). Own request for everyone else. */
export function HRLeaveModal({id}:{id?:string}){
  const {s,uid,run,closeModal,busy}=useHcc();const x=hrSlice(s),mgr=hrCanManage(s,uid),me=hrStaffOfUser(s,uid);
  const [sid,setSid]=useState(id??me?.id??(mgr?x.staff.find(t=>t.active)?.id:'')??'');
  const st=hrStaffById(s,sid);
  const [f,setF]=useState({type:(st?.payType==='daily'?'LOP':'CL') as HRLeaveType,from:s.date,to:s.date,reason:''});
  if(!st)return <Empty title="No staff record linked" body="Your demo user is not linked to a staff record."/>;
  const types:HRLeaveType[]=st.payType==='daily'?['LOP']:['CL','SL','EL','LOP'];
  const type=types.includes(f.type)?f.type:types[0];
  const days=f.to>=f.from?hrLeaveDays(s,st,f.from,f.to):0;const bal=hrLeaveBalance(s,st.id,f.from.slice(0,4));
  return <><div className="modal-heading"><h2>Request leave</h2><p>Another manager approves — nobody can approve their own request. Weekly offs inside the dates are not counted.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_LEAVE_REQUEST',staffId:st.id,leaveType:type,from:f.from,to:f.to,reason:f.reason},'Leave request sent for approval',{close:true});}}>
      {mgr&&!id&&<label>Staff member<select value={sid} onChange={e=>setSid(e.target.value)}>{x.staff.filter(t=>t.active).map(t=><option key={t.id} value={t.id}>{t.name}</option>)}</select></label>}
      {(!mgr||id)&&<p className="hr-who"><strong>{st.name}</strong> · {st.designation}</p>}
      <label>Leave type<select value={type} onChange={e=>setF({...f,type:e.target.value as HRLeaveType})}>{types.map(t=><option key={t} value={t}>{HR_LEAVE_LABEL[t]} ({t})</option>)}</select></label>
      <div className="form-row"><label>From<input type="date" required min={s.date} value={f.from} onChange={e=>setF({...f,from:e.target.value,to:e.target.value>f.to?e.target.value:f.to})}/></label>
        <label>To<input type="date" required min={f.from} value={f.to} onChange={e=>setF({...f,to:e.target.value})}/></label></div>
      <label>Reason<input required minLength={3} maxLength={120} value={f.reason} onChange={e=>setF({...f,reason:e.target.value})} enterKeyHint="send"/></label>
      <div className="info-note"><Info size={16}/><span>{days} working day{days===1?'':'s'} · {st.payType==='daily'?'Daily-wage staff: leave without pay only (illustrative policy).':'Balance this year (illustrative): '+bal.map(b=>`${b.type} ${b.left}/${b.entitled}`).join(' · ')}</span></div>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||days<1}>Send request</button></div>
    </form></>;
}

/** 'hr-leave-decide': id = leave ID, room = 'approve' | 'reject'. */
export function HRLeaveDecideModal({id,room}:{id?:string;room?:string}){
  const {s,run,closeModal,busy}=useHcc();const l=hrSlice(s).leaves.find(x=>x.id===id);const [note,setNote]=useState('');
  if(!l)return <Empty title="Leave request unavailable"/>;
  const approve=room!=='reject',st=hrStaffById(s,l.staffId);
  return <><div className="modal-heading"><h2>{approve?'Approve leave':'Reject leave'}</h2><p>{l.id} · {st?.name} · {HR_LEAVE_LABEL[l.type]} · {shortDate(l.from)}{l.to!==l.from?` – ${shortDate(l.to)}`:''} · {l.days} day{l.days===1?'':'s'} · “{l.reason}” · requested by {hrUserName(s,l.requestedBy)}</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_LEAVE_DECIDE',id:l.id,approve,note},approve?'Leave approved':'Leave rejected',{close:true});}}>
      <label>{approve?'Note (optional)':'Reason for rejecting'}<input required={!approve} maxLength={120} value={note} onChange={e=>setNote(e.target.value)} enterKeyHint="done"/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className={'btn '+(approve?'primary':'danger')} disabled={busy}>{approve?'Approve':'Reject'}</button></div>
    </form></>;
}

/** 'hr-advance': salary advance with monthly recovery. */
export function HRAdvanceModal(){
  const {s,run,closeModal,busy}=useHcc();const x=hrSlice(s);
  const [f,setF]=useState({staffId:x.staff.find(t=>t.active)?.id??'',amount:'',installment:'',reason:''});
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement|HTMLSelectElement>)=>setF({...f,[k]:e.target.value});
  return <><div className="modal-heading"><h2>Salary advance</h2><p>Recovered from net pay each month when payroll is locked. Paid by simulated bank transfer — no effect on the cash drawer.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();run({type:'HR_ADVANCE_ADD',staffId:f.staffId,amount:rs(f.amount),installment:rs(f.installment),reason:f.reason},'Advance recorded',{close:true});}}>
      <label>Staff member<select value={f.staffId} onChange={set('staffId')}>{x.staff.filter(t=>t.active).map(t=><option key={t.id} value={t.id}>{t.name}</option>)}</select></label>
      <div className="form-row"><label>Amount (₹)<input required type="number" inputMode="decimal" min="1" max="1000000" step="0.01" value={f.amount} onChange={set('amount')}/></label>
        <label>Monthly recovery (₹)<input required type="number" inputMode="decimal" min="1" step="0.01" value={f.installment} onChange={set('installment')}/></label></div>
      <label>Reason<input required maxLength={120} value={f.reason} onChange={set('reason')} enterKeyHint="done"/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Record advance</button></div>
    </form></>;
}

/** 'hr-rates': illustrative payroll configuration (Owner). */
export function HRRatesModal(){
  const {s,run,closeModal,busy}=useHcc();const r=hrSlice(s).rates;
  const [f,setF]=useState({basic:String(r.basicBps/100),pf:String(r.pfBps/100),pfCap:toRs(r.pfCap),esic:String(r.esicBps/100),esicCeiling:toRs(r.esicCeiling),pt:toRs(r.pt),ptThreshold:toRs(r.ptThreshold),ot:String(r.otBps/10000),late:String(r.lateBlock)});
  const set=(k:keyof typeof f)=>(e:React.ChangeEvent<HTMLInputElement>)=>setF({...f,[k]:e.target.value});
  const pct=(v:string)=>Math.round(Number(v)*100);
  function save(e:React.FormEvent){e.preventDefault();
    const next:HRRates={basicBps:pct(f.basic),pfBps:pct(f.pf),pfCap:rs(f.pfCap),esicBps:pct(f.esic),esicCeiling:rs(f.esicCeiling),pt:rs(f.pt),ptThreshold:rs(f.ptThreshold),otBps:Math.round(Number(f.ot)*10000),lateBlock:Number(f.late)};
    run({type:'HR_RATES_SET',...next},'Illustrative rates saved',{close:true});}
  const num=(k:keyof typeof f,label:string,step='0.01')=><label>{label}<input required type="number" inputMode="decimal" min="0" step={step} value={f[k]} onChange={set(k)}/></label>;
  return <><div className="modal-heading"><h2>Illustrative payroll rates</h2><p>{HR_DISCLAIMER} Locked payroll runs keep the rates they were calculated with.</p></div>
    <form className="form" onSubmit={save}>
      <div className="form-row">{num('basic','Basic share of earned pay (%)')}{num('pf','PF placeholder rate (%)')}</div>
      <div className="form-row">{num('pfCap','PF wage cap (₹)')}{num('esic','ESIC placeholder rate (%)')}</div>
      <div className="form-row">{num('esicCeiling','ESIC gross ceiling (₹)')}{num('pt','PT placeholder (₹ / month)')}</div>
      <div className="form-row">{num('ptThreshold','PT applies from gross (₹)')}{num('ot','Overtime multiplier (×)')}</div>
      <label>Late marks per ½-day loss of pay (0 = off)<input required type="number" inputMode="numeric" min="0" max="10" step="1" value={f.late} onChange={set('late')}/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Save rates</button></div>
    </form></>;
}

/** 'hr-payslip': id = "runId|staffId". Own payslip or payroll roles; printable on A4 and phone. */
export function HRPayslipModal({id}:{id?:string}){
  const {s,uid,closeModal}=useHcc();const [runId,staffId]=(id??'').split('|');
  const run=hrSlice(s).runs.find(r=>r.id===runId),line=run?.lines.find(l=>l.staffId===staffId);
  const me=hrStaffOfUser(s,uid),allowed=hrCanPay(s,uid)||(me?.id===staffId&&run?.status==='Locked');
  if(!run||!line||!allowed)return <Empty title="Payslip unavailable" body="Payslips are released when payroll is locked. You can see only your own."/>;
  const c=line.counts,R=(label:string,v:React.ReactNode)=><div key={label}><span>{label}</span><strong className="number">{v}</strong></div>;
  return <><div className="hr-payslip invoice">
    <div className="invoice-top"><Hotel size={28}/><div><h2>{s.settings.hotel}</h2><p>Payslip · {hrMonthLabel(run.month)} · {run.id}</p></div></div>
    <div className="invoice-stamp">{run.status==='Locked'?'DEMONSTRATION PAYSLIP':'DRAFT — NOT FINAL'} · FICTIONAL DATA · ILLUSTRATIVE DEDUCTIONS — NOT STATUTORY ADVICE</div>
    <div className="hr-ps-head"><div><small>EMPLOYEE</small><h3>{line.name}</h3><p>{line.designation} · {hrDeptName(s,line.dept)} · {line.staffId}</p></div>
      <div><small>PAY BASIS</small><h3>{line.payType==='monthly'?`${money(line.base)} / month`:`${money(line.base)} / day`}</h3><p>{c.days} calendar days · payable {line.payableDays}</p></div></div>
    <div className="hr-ps-grid">
      <section><h4>Attendance</h4><div className="summary-list">{R('Present',c.P)}{R('Late',c.L)}{R('Half-days',c.H)}{R('Absent',c.A+c.unmarked)}{R('Paid leave',c.paidLeave)}{R('Leave without pay',c.lopLeave)}{R('Weekly offs',c.off)}{!!c.OFFW&&R('Worked on off day',c.OFFW)}{R('Overtime',`${Math.round(c.otMin/6)/10} h`)}{!!line.latePenaltyHalf&&R('Late rule (½-days)',line.latePenaltyHalf)}</div></section>
      <section><h4>Earnings</h4><div className="summary-list">{R('Basic earned',money(line.basic))}{R('Overtime',money(line.ot))}{R('Gross',money(line.gross))}</div>
        <h4>Deductions <em>(illustrative)</em></h4><div className="summary-list">{R('PF placeholder',money(line.pf))}{R('ESIC placeholder',money(line.esic))}{R('PT placeholder',money(line.pt))}{R('Advance recovery',money(line.advance))}{R('Total deductions',money(line.deductions))}</div></section>
    </div>
    <div className="hr-ps-net"><span>Net pay</span><strong className="number">{money(line.net)}</strong></div>
    <p className="footnote">{HR_DISCLAIMER} Prepared by {hrUserName(s,run.preparedBy)}{run.lockedBy?` · locked by ${hrUserName(s,run.lockedBy)} at ${run.lockedAt?.slice(11)} on ${dateLabel(run.lockedAt!.slice(0,10))} (simulated clock)`:''}. Payment by simulated bank transfer.</p>
  </div>
  <div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print / save PDF</button></div></>;
}
