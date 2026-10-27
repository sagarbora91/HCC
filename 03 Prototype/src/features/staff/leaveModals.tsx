// Module 17 · leave dialogs — SCC LeaveDesk overlays (DEC-116 / DEC-119): Add Leave, day view, leave detail, Approvals, Add Agenda, settings
// (defaults / week-off / advisory caps), per-staff ⚙ override, blackout, print, JSON import and Clear All. Sheets on phones. Modal IDs start with 'hr-'.
// The SCC texts (toasts, confirms, warnings) come from domain/streams/staff-leave.ts so the handler, this dialog and the production client agree.
import React,{useState} from 'react';
import {Info,AlertTriangle,Printer,Plus,Check,X,Trash2,Download} from 'lucide-react';
import {useHcc} from '../../app/context';
import {Empty,shortDate} from '../../components/ui';
import {printDocument,saveTextFile} from '../../app/native';
import {
  hrSlice,hrCanManage,hrCanRates,hrStaffById,hrStaffOfUser,hrCapacity,hrLeavesOn,hrAgendasOn,hrDeptTally,hrLeaveDayNote,hrLeaveDateLong,hrLeaveDateShort,
  hrLeaveApproverName,hrLeaveName,hrLeaveChoices,hrApproverChoices,hrLeaveRequestPlan,hrLeaveRequestToast,hrLeaveOverBalance,hrBlackoutHits,
  hrLeaveBlackoutWarning,hrLeaveSoftBlackoutText,hrLeaveBalanceHint,hrLeaveCapacityWarning,hrLeaveCategory,hrLeavePrintRows,hrLeaveImportParse,
  hrLeaveImportPreview,hrLeaveExportJson,hrLeaveClearList,hrLeavePolicy,hrLeaveWeekOff,hrDeptCap,hrKindLabel,hrDecidable,hrLeaveOverCount,
  HR_HALF_LABEL,HR_MAX_SHIFT,HR_MAX_DAY,HR_TRACKED,HR_WEEKDAYS,HR_ENT_DEFAULTS,HR_SCC_WEEKOFF_DEFAULT,type HRLeave,type HRHalf,type HRLeaveType,
} from '../../services/mock';
import {LeaveApprovalsView,hrLeaveReadOnly,useLeaveDecide} from './Leave';

type S=ReturnType<typeof useHcc>['s'];
const userName=(s:S,id?:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id??'—';
const staffName=(s:S,id:string)=>hrSlice(s).staff.find(t=>t.id===id)?.name??id;
const asOfOf=(s:S,room?:string)=>room&&/^\d{4}-\d{2}-\d{2}$/.test(room)?room:s.date;

/** 'hr-leave': SCC Add Leave. id = staff ID (fixed staff member), room = the prefilled date (SCC prefillDate || day view || as-of). */
export function HRLeaveModal({id,room}:{id?:string;room?:string}){
  const {s,uid,run,closeModal,busy}=useHcc();const x=hrSlice(s),mgr=hrCanManage(s,uid),me=hrStaffOfUser(s,uid);
  const first=asOfOf(s,room)<s.date?s.date:asOfOf(s,room);
  const [sid,setSid]=useState(id??(mgr?'':me?.id??''));
  const [f,setF]=useState({approver:'',kind:'full' as 'full'|'AM'|'PM',type:'CL' as HRLeaveType,from:first,to:first,reason:''});
  const st=hrStaffById(s,sid),fixed=!!id||!mgr;
  if(fixed&&!st)return <Empty title="No staff record linked" body="Your demo user is not linked to a staff record."/>;
  const half:HRHalf|undefined=f.kind==='full'?undefined:f.kind,to=f.to||f.from;
  const approvers=hrApproverChoices(s).filter(a=>!st?.userId||a.id!==st.userId);
  const cap=hrLeaveCapacityWarning(s,f.from,half),bk=st?hrBlackoutHits(s,f.from,to,st.dept):undefined,hint=st?hrLeaveBalanceHint(s,st.id,f.type,f.from,to,half):undefined;
  function submit(e:React.FormEvent){e.preventDefault();if(!st)return;
    const over=hrLeaveOverBalance(s,st,f.type,f.from,to,half);
    if(over&&!window.confirm(over))return;
    const b=hrBlackoutHits(s,f.from,to,st.dept);
    if(!b.hard&&b.hits.length&&!window.confirm(hrLeaveSoftBlackoutText(b.labels,hrLeaveCategory(f.type))))return;
    const t=hrLeaveRequestToast(hrLeaveRequestPlan(s,st.id,f.from,to,half));
    void run({type:'HR_LEAVE_REQUEST',staffId:st.id,approver:f.approver,leaveType:f.type,from:f.from,to,...(half?{half}:{}),reason:f.reason.trim(),
      ...(over?{confirmOverBalance:true}:{}),...(b.hits.length?{confirmBlackout:true}:{})},t.text,{close:true});}
  return <><div className="modal-heading"><h2>Add Leave</h2><p>One record per date (SCC). Dates on the hotel’s leave week-off days ({hrLeaveWeekOff(s).length?hrLeaveWeekOff(s).map(d=>HR_WEEKDAYS[d]).join(' + '):'none'}) are skipped; a date that already has {HR_MAX_DAY} leaves, or the same person, is skipped and the other dates are still recorded. Every date then waits for approval — nobody approves their own leave.</p></div>
    <form className="form" onSubmit={submit}>
      {!fixed?<label>Staff member<select required value={sid} onChange={e=>setSid(e.target.value)}><option value="">— Select Staff Member —</option>{x.staff.filter(t=>t.active).map(t=><option key={t.id} value={t.id}>{t.name} ({x.depts.find(d=>d.id===t.dept)?.name??t.dept})</option>)}</select></label>
        :<p className="hr-who"><strong>{st!.name}</strong> · {st!.designation}</p>}
      <label>Approver (Manager / Owner) *<select required value={f.approver} onChange={e=>setF({...f,approver:e.target.value})}><option value="">— Select Approver —</option>{approvers.map(a=><option key={a.id} value={a.id}>{a.name} ({a.role})</option>)}</select></label>
      <div className="hr-status-pick lv-kind" role="radiogroup" aria-label="Leave type">{([['full','Full Day'],['AM',HR_HALF_LABEL.AM],['PM',HR_HALF_LABEL.PM]] as const).map(([k,l])=>
        <label key={k} className={f.kind===k?'on':''}><input type="radio" name="lv-kind" checked={f.kind===k} onChange={()=>setF({...f,kind:k})}/>{l}</label>)}</div>
      <label>Category<select value={f.type} onChange={e=>setF({...f,type:e.target.value as HRLeaveType})}>{hrLeaveChoices(s).map(c=><option key={c.code} value={c.code}>{c.name}{c.code==='LOP'?' — unpaid':''}</option>)}</select></label>
      <div className="form-row"><label>Leave From<input type="date" required min={s.date} value={f.from} onChange={e=>setF({...f,from:e.target.value,to:e.target.value>f.to?e.target.value:f.to})}/></label>
        <label>Leave To<input type="date" required min={f.from} value={f.to} onChange={e=>setF({...f,to:e.target.value})}/></label></div>
      <label>Reason / notes (optional)<textarea maxLength={500} rows={2} value={f.reason} onChange={e=>setF({...f,reason:e.target.value})}/></label>
      {cap&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/><span>{cap}</span></div>}
      {bk&&bk.hits.length>0&&<div className={'info-note '+(bk.hard?'lv-hard':'hr-warn')} role="alert"><AlertTriangle size={16}/><span>{hrLeaveBlackoutWarning(bk)}</span></div>}
      {hint&&<div className={'info-note '+(hint.exceed?'lv-exceed':hint.tracked?'lv-ok':'')}><Info size={16}/><span>{hint.text}</span></div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy||!st}>Add Leave</button></div>
    </form></>;
}

/** 'hr-leave-day': SCC day view. id = date, room = as-of date (past view = read-only). */
export function HRLeaveDayModal({id,room}:{id?:string;room?:string}){
  const {s,uid,open,run,busy,closeModal}=useHcc();const decide=useLeaveDecide();const date=id&&/^\d{4}-\d{2}-\d{2}$/.test(id)?id:s.date,asOf=asOfOf(s,room),ro=hrLeaveReadOnly(s,asOf);
  const mgr=hrCanManage(s,uid),all=hrLeavesOn(s,date),leaves=all.filter(l=>l.status!=='Cancelled'),removed=all.filter(l=>l.status==='Cancelled'),agendas=hrAgendasOn(s,date);
  const cap=hrCapacity(s,date),tally=hrDeptTally(s,date),ok=new Set(hrDecidable(s,uid).map(l=>l.id)),me=hrStaffOfUser(s,uid);
  const canAdd=mgr&&!ro&&!cap.dayFull&&date>=s.date;
  const canRemove=(l:HRLeave)=>!ro&&(l.status==='Pending'?(mgr||l.requestedBy===uid||l.staffId===me?.id):mgr&&(l.status!=='Approved'||l.date>=s.date));
  const remove=(l:HRLeave)=>{if(window.confirm('Remove this leave record?'))run({type:'HR_LEAVE_CANCEL',id:l.id},'Leave removed');};
  const bar=(label:string,used:number)=><div className="lv-slot" key={label}><div className="lv-slot-label">{label}</div><div className={'lv-slot-count '+(used>=HR_MAX_SHIFT?'full':used>=3?'warn':'ok')}>{used} / {HR_MAX_SHIFT}</div>
    <div className="lv-bar"><div className={'lv-fill'+(used>=HR_MAX_SHIFT?' full':'')} style={{width:Math.min(used/HR_MAX_SHIFT*100,100)+'%'}}/></div>
    <div className="lv-slot-note">{used>=HR_MAX_SHIFT?'⚠ Shift full — no more allowed':`${HR_MAX_SHIFT-used} slot(s) remaining`}</div></div>;
  return <><div className="modal-heading"><h2>{hrLeaveDateLong(date)}</h2><p>{leaves.length} leave(s) · {agendas.length} agenda item(s){ro?' · read-only (past view)':''}</p></div>
    <div className="lv-day">
      <div className="inline lv-day-actions">{canAdd&&<button className="btn primary" onClick={()=>open({type:'hr-leave',room:date})}><Plus size={16}/> Add Leave</button>}
        {mgr&&!ro&&<button className="btn" onClick={()=>open({type:'hr-agenda',room:date})}><Plus size={16}/> Add Agenda</button>}</div>
      <section><h4>Capacity</h4><div className="lv-capgrid">{bar('AM Shift (Morning, till 2 PM)',cap.amUsed)}{bar('PM Shift (Afternoon, after 5 PM)',cap.pmUsed)}
        <div className={'lv-slot-note lv-daynote'+(cap.dayFull?' lv-red':'')}>{hrLeaveDayNote(cap)}</div></div></section>
      <section><h4>Leave load by department (advisory)</h4><div className="lv-capgrid">{tally.depts.map(t=><div className="lv-slot" key={t.dept}><div className="lv-slot-label">{t.name}</div>
        <div className={'lv-slot-count '+(t.over?'full':'ok')}>{t.used} / {t.cap}</div><div className="lv-bar"><div className={'lv-fill'+(t.over?' full':'')} style={{width:Math.min(t.cap>0?t.used/t.cap*100:t.used>0?100:0,100)+'%'}}/></div>
        <div className={'lv-slot-note'+(t.over?' lv-red':'')}>{t.over?'⚠ Over capacity':`${Math.max(0,t.cap-t.used)} slot(s) free`}</div></div>)}
        {tally.unassigned>0&&<div className="lv-slot"><div className="lv-slot-label">Unassigned</div><div className="lv-slot-count">{tally.unassigned}</div><div className="lv-slot-note">{tally.unassigned} staff — department not set</div></div>}</div>
        <p className="footnote">Does not block adding leave (SCC per-store caps, per department here).</p></section>
      <section><h4>Leave</h4>{leaves.length?<div className="lv-rows">{leaves.map(l=><div key={l.id} className={'lv-row'+(l.status==='Rejected'?' lv-row-rejected':'')}>
        <button type="button" className="lv-row-main" onClick={()=>open({type:'hr-leave-detail',id:l.id,room:asOf})}><strong>{staffName(s,l.staffId)}</strong>
          <small>{hrLeaveName(s,l.type)}{hrLeaveApproverName(s,l)?' · Approver: '+hrLeaveApproverName(s,l):''}{l.decidedAt?' · Decided '+hrLeaveDateShort(l.decidedAt.slice(0,10)):''}</small></button>
        <span className={'lv-badge-kind '+(l.half==='AM'?'half-am':l.half==='PM'?'half-pm':'full')}>{hrKindLabel(l)}</span>
        {l.status!=='Approved'&&<span className={'lv-badge-st '+(l.status==='Pending'?'pending':'rejected')}>{l.status.toLowerCase()}</span>}
        {!ro&&l.status==='Pending'&&ok.has(l.id)&&<><button type="button" className="lv-icon ok" title="Approve" aria-label="Approve" disabled={busy} onClick={()=>decide(l,true)}><Check size={16}/></button>
          <button type="button" className="lv-icon no" title="Reject" aria-label="Reject" disabled={busy} onClick={()=>decide(l,false)}>⊘</button></>}
        {canRemove(l)&&<button type="button" className="lv-icon" title="Remove leave" aria-label="Remove leave" disabled={busy} onClick={()=>remove(l)}>×</button>}</div>)}</div>
        :<p className="footnote">No leaves recorded for this date</p>}
        {removed.length>0&&<p className="footnote">{removed.length} removed record(s) kept for audit: {removed.map(l=>`${staffName(s,l.staffId)} (${hrKindLabel(l)}, by ${userName(s,l.cancelledBy)})`).join(' · ')}</p>}</section>
      <section><h4>Agenda</h4>{agendas.length?<div className="lv-rows">{agendas.map(a=><div key={a.id} className="lv-row"><span className="lv-agenda-time">{a.time||'—'}</span>
        <div className="lv-row-main lv-row-static"><strong>{a.title} <span className={'lv-agenda-type '+a.type}>{a.type}</span></strong>{a.description&&<small>{a.description}</small>}</div>
        {mgr&&!ro&&<button type="button" className="lv-icon" title="Remove" aria-label="Remove agenda item" disabled={busy} onClick={()=>{if(window.confirm('Remove this agenda item?'))run({type:'HR_AGENDA_REMOVE',id:a.id},'Agenda removed');}}>✕</button>}</div>)}</div>
        :<p className="footnote">No agenda or meetings scheduled</p>}</section>
    </div>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button></div></>;
}

/** 'hr-leave-detail': SCC staff detail. id = leave record ID, room = as-of date. */
export function HRLeaveDetailModal({id,room}:{id?:string;room?:string}){
  const {s,uid,busy,closeModal}=useHcc();const decideLeave=useLeaveDecide();const l=hrSlice(s).leaves.find(x=>x.id===id);
  if(!l)return <Empty title="Leave record unavailable"/>;
  const ro=hrLeaveReadOnly(s,asOfOf(s,room)),ok=hrDecidable(s,uid).some(x=>x.id===l.id);
  const typeLabel=l.half==='AM'?'Half Day — Morning':l.half==='PM'?'Half Day — Afternoon':'Full Day';
  const rows:[string,string][]=[['Leave Type',typeLabel],['Category',hrLeaveName(s,l.type)],['Leave From',hrLeaveDateShort(l.from)],['Leave To',hrLeaveDateShort(l.to)],
    ['Approver',hrLeaveApproverName(s,l)||'Not recorded'],['Status',l.status.toUpperCase()+(l.legacy?' (legacy record)':'')],
    ['Decided By',l.decidedBy?userName(s,l.decidedBy):l.decidedByName??'—'],['Decided At',l.decidedAt?hrLeaveDateShort(l.decidedAt.slice(0,10)):'—'],['Record ID',l.id],
    ...(l.status==='Cancelled'?[['Removed by',`${userName(s,l.cancelledBy)} · ${l.cancelledAt??''}${l.note?' · '+l.note:''}`] as [string,string]]:[]),
    ['Requested by',`${userName(s,l.requestedBy)} · ${l.requestedAt}`]];
  const decide=(approve:boolean)=>decideLeave(l,approve,{close:true});
  return <><div className="modal-heading"><h2>{staffName(s,l.staffId)}</h2><p>Leave on {hrLeaveDateShort(l.date)}</p></div>
    <div className="summary-list padded lv-detail">{rows.map(([k,v])=><div key={k}><span>{k}</span><strong>{v}</strong></div>)}{l.reason&&<div><span>Reason / Notes</span><strong className="lv-reason">{l.reason}</strong></div>}</div>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button>
      {l.status==='Pending'&&!ro&&ok&&<><button className="btn danger" disabled={busy} onClick={()=>decide(false)}><X size={16}/> Reject</button><button className="btn primary" disabled={busy} onClick={()=>decide(true)}><Check size={16}/> Approve</button></>}</div></>;
}

/** 'hr-leave-approvals': SCC Approvals overlay (room = as-of date). */
export function HRLeaveApprovalsModal({room}:{room?:string}){
  const {s,closeModal,display}=useHcc();
  return <><div className="modal-heading"><h2>Leave approvals</h2><p>Approve within 7 days of the leave date (SCC note); approved leave counts in payroll at once.</p></div>
    <LeaveApprovalsView phone={!!display?.isMobile} asOf={asOfOf(s,room)}/>
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Close</button></div></>;
}

/** 'hr-agenda': SCC Add Agenda (room = prefilled date). */
export function HRAgendaModal({room}:{room?:string}){
  const {s,run,closeModal,busy}=useHcc();const [f,setF]=useState({date:asOfOf(s,room),title:'',time:'',type:'meeting',description:''});
  return <><div className="modal-heading"><h2>Add Agenda</h2><p>A meeting, agenda or reminder on a date (SCC). Shown on the calendar and in the day view.</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'HR_AGENDA_ADD',date:f.date,title:f.title.trim(),time:f.time,agendaType:f.type,description:f.description.trim()},'Agenda item added',{close:true});}}>
      <div className="form-row"><label>Date<input type="date" required value={f.date} onChange={e=>setF({...f,date:e.target.value})}/></label><label>Time (optional)<input type="time" value={f.time} onChange={e=>setF({...f,time:e.target.value})}/></label></div>
      <label>Title<input required maxLength={200} value={f.title} onChange={e=>setF({...f,title:e.target.value})}/></label>
      <label>Type<select value={f.type} onChange={e=>setF({...f,type:e.target.value})}><option value="meeting">Meeting</option><option value="agenda">Agenda</option><option value="reminder">Reminder</option></select></label>
      <label>Description (optional)<textarea maxLength={1000} rows={2} value={f.description} onChange={e=>setF({...f,description:e.target.value})}/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Add Agenda</button></div>
    </form></>;
}

/** 'hr-leave-policy': SCC entitlement defaults, week-off days and per-department (SCC per-store) advisory caps. Owner (DEC-058). */
export function HRLeavePolicyModal(){
  const {s,run,closeModal,busy}=useHcc();const p=hrLeavePolicy(s),depts=hrSlice(s).depts,wo=hrLeaveWeekOff(s);
  const [f,setF]=useState({CL:String(p.ent.CL),SL:String(p.ent.SL),EL:String(p.ent.EL),wo:[0,1,2,3,4,5,6].map(d=>wo.includes(d)),dept:Object.fromEntries(depts.map(d=>[d.id,''])) as Record<string,string>});
  const ent={CL:Number(f.CL)||0,SL:Number(f.SL)||0,EL:Number(f.EL)||0},over=hrLeaveOverCount(s,ent);
  function save(e:React.FormEvent){e.preventDefault();
    const deptCap:Record<string,string>={};for(const [d,v] of Object.entries(f.dept))deptCap[d]=v;
    void run({type:'HR_LEAVE_POLICY_SET',ent:{CL:f.CL,SL:f.SL,EL:f.EL},weekOff:f.wo.map((on,i)=>on?i:-1).filter(i=>i>=0),deptCap},'Leave settings saved',{close:true});}
  const n=(k:'CL'|'SL'|'EL',label:string)=><label key={k}>{label}<input type="number" inputMode="decimal" min="0" step="any" value={f[k]} onChange={e=>setF({...f,[k]:e.target.value})}/></label>;
  return <><div className="modal-heading"><h2>Leave settings</h2><p>Annual Leave Entitlements (days per financial year, April–March; SCC defaults {HR_ENT_DEFAULTS.CL} · {HR_ENT_DEFAULTS.SL} · {HR_ENT_DEFAULTS.EL}; a blank field saves 0, as SCC). Applies to new leave.</p></div>
    <form className="form" onSubmit={save}>
      <div className="form-row">{n('CL','Casual Leave')}{n('SL','Sick Leave')}{n('EL','Earned Leave')}</div>
      {over>0&&<div className="info-note hr-warn"><Info size={16}/><span>{over} staff member{over===1?' has':'s have'} already taken more approved leave than these entitlements this year (shown as “over by N”; nothing recorded changes).</span></div>}
      <fieldset className="hr-fieldset"><legend>Leave week-off days (skipped when leave is recorded and counted)</legend><div className="lv-wo">{HR_WEEKDAYS.map((d,i)=>
        <label key={d} className="lv-wo-day"><input type="checkbox" checked={f.wo[i]} onChange={e=>setF({...f,wo:f.wo.map((v,j)=>j===i?e.target.checked:v)})}/>{d}</label>)}</div>
        <p className="footnote">SCC’s default is {HR_SCC_WEEKOFF_DEFAULT.map(d=>HR_WEEKDAYS[d]).join(' + ')}; untick both for a 7-day trading week (HCC’s default: none). Staff weekly offs stay for the roster only.</p></fieldset>
      <fieldset className="hr-fieldset"><legend>Department leave caps (advisory — a blank field keeps the cap)</legend><div className="hr-cap-grid">{depts.map(d=>
        <label key={d.id}>{d.name} (now {hrDeptCap(s,d.id)})<input type="number" inputMode="numeric" min="0" step="1" value={f.dept[d.id]} placeholder={String(hrDeptCap(s,d.id))} onChange={e=>setF({...f,dept:{...f.dept,[d.id]:e.target.value}})}/></label>)}</div>
        <p className="footnote">Flags a department “over capacity” in the day view and the 7-day strip; never blocks (SCC per-store caps). The binding limit is {HR_MAX_DAY} leave records a day for the hotel.</p></fieldset>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Save settings</button></div>
    </form></>;
}

/** 'hr-leave-ent': SCC ⚙ per-staff entitlement override. id = staff ID. Blank = use the default; all blank = override cleared. Owner. */
export function HRLeaveEntModal({id}:{id?:string}){
  const {s,run,closeModal,busy}=useHcc();const st=id?hrStaffById(s,id):undefined,p=hrLeavePolicy(s),ov=st?p.perStaff?.[st.id]??{}:{};
  const [f,setF]=useState({CL:ov.CL!=null?String(ov.CL):'',SL:ov.SL!=null?String(ov.SL):'',EL:ov.EL!=null?String(ov.EL):''});
  if(!st)return <Empty title="Staff member unavailable"/>;
  const any=HR_TRACKED.some(c=>f[c]!=='');
  return <><div className="modal-heading"><h2>Leave entitlements · {st.name}</h2><p>Blank = use the defaults. Days per financial year (Apr–Mar).</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'HR_LEAVE_ENT_OVERRIDE',staffId:st.id,ent:f},any?`Entitlement override saved for ${st.name}`:'Override cleared — using defaults',{close:true});}}>
      <div className="form-row">{HR_TRACKED.map(c=><label key={c}>{hrLeaveCategory(c)}<input type="number" inputMode="decimal" min="0" step="any" placeholder={String(p.ent[c])} value={f[c]} onChange={e=>setF({...f,[c]:e.target.value})}/></label>)}</div>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Save</button></div>
    </form></>;
}

/** 'hr-leave-blackout': SCC add blackout / store holiday. Owner. */
export function HRLeaveBlackoutModal(){
  const {s,run,closeModal,busy}=useHcc();const depts=hrSlice(s).depts;
  const [f,setF]=useState({from:s.date,to:s.date,dept:'',kind:'blackout',hard:false,label:''});
  return <><div className="modal-heading"><h2>Blackout / store holiday</h2><p>A blackout warns (or, ticked hard, blocks) leave on its dates; a store holiday is a marker that still asks for confirmation (SCC).</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();void run({type:'HR_LEAVE_BLACKOUT_ADD',...f,hard:f.kind==='blackout'&&f.hard},'Blackout saved',{close:true});}}>
      <div className="form-row"><label>From<input type="date" required value={f.from} onChange={e=>setF({...f,from:e.target.value})}/></label><label>To<input type="date" required value={f.to} onChange={e=>setF({...f,to:e.target.value})}/></label></div>
      <div className="form-row"><label>Scope<select value={f.dept} onChange={e=>setF({...f,dept:e.target.value})}><option value="">All departments</option>{depts.map(d=><option key={d.id} value={d.id}>{d.name}</option>)}</select></label>
        <label>Kind<select value={f.kind} onChange={e=>setF({...f,kind:e.target.value})}><option value="blackout">Blackout</option><option value="holiday">Store Holiday (marker only)</option></select></label></div>
      {f.kind==='blackout'&&<label className="check-row"><input type="checkbox" checked={f.hard} onChange={e=>setF({...f,hard:e.target.checked})}/> Hard block (leave on these dates is refused)</label>}
      <label>Label (optional)<input maxLength={80} value={f.label} onChange={e=>setF({...f,label:e.target.value})} placeholder="e.g. Diwali rush"/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className="btn primary" disabled={busy}>Save</button></div>
    </form></>;
}

/** 'hr-leave-print': SCC printCalendar (landscape document). id = month YYYY-MM. */
export function HRLeavePrintModal({id}:{id?:string}){
  const {s,closeModal}=useHcc();const ym=id&&/^\d{4}-\d{2}$/.test(id)?id:s.date.slice(0,7),d=hrLeavePrintRows(s,ym);
  return <><div className="print-doc lv-print"><div className="lv-print-head"><div><strong>{s.settings.hotel}</strong><small>Leave &amp; Agenda</small></div><div className="lv-print-title">Leave Calendar<small>{d.title.replace('Leave Calendar — ','')}</small></div></div>
    <p className="lv-print-meta">Leaves: {d.leaves.length} · Agendas: {d.agendas.length}</p>
    {!d.leaves.length&&!d.agendas.length?<p>No leave or agenda entries this month.</p>:<>
      {d.leaves.length>0&&<section><h4>Leaves</h4><table><thead><tr><th>Date</th><th>Day</th><th>Employee</th><th>Leave Type</th><th>Notes</th></tr></thead>
        <tbody>{d.leaves.map((r,i)=><tr key={i}><td>{r.date}</td><td>{r.dow}</td><td>{r.name}</td><td>{r.type}</td><td>{r.notes||'—'}</td></tr>)}</tbody><tfoot><tr><td colSpan={5}>Total leaves: {d.leaves.length}</td></tr></tfoot></table></section>}
      {d.agendas.length>0&&<section><h4>Agendas</h4><table><thead><tr><th>Date</th><th>Day</th><th>Agenda</th></tr></thead><tbody>{d.agendas.map((r,i)=><tr key={i}><td>{r.date}</td><td>{r.dow}</td><td>{r.text}</td></tr>)}</tbody></table></section>}</>}
    <p className="lv-print-meta">Fictional data · printed from Hotel Control Centre</p></div>
    <div className="modal-actions no-print"><button className="btn" onClick={closeModal}>Close</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print / save PDF (landscape)</button></div></>;
}

/** 'hr-leave-import': SCC importJSON — choose the backup file, see SCC's preview, confirm the merge. Owner. */
export function HRLeaveImportModal(){
  const {s,run,closeModal,busy,toast}=useHcc();const [data,setData]=useState<unknown>();const [msg,setMsg]=useState('');
  const plan=data===undefined?undefined:hrLeaveImportParse(s,data);
  const pick=(file?:File)=>{if(!file)return;void file.text().then(t=>{try{setData(JSON.parse(t));setMsg('');}catch(e){setData(undefined);setMsg('Import failed: '+(e as Error).message);toast('Import failed: '+(e as Error).message,true);}});};
  const ok=plan&&typeof plan!=='string';
  return <><div className="modal-heading"><h2>Import leave (JSON)</h2><p>An SCC LeaveDesk backup (or this screen’s Backup JSON). It MERGES: a record already here is ignored; the same person on a date, or a full date, is skipped; a week-off date is skipped; staff are matched by name. Records in a locked pay month are skipped.</p></div>
    <div className="form"><label>Backup file<input type="file" accept="application/json,.json" onChange={e=>pick(e.target.files?.[0])}/></label>
      {msg&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/><span>{msg}</span></div>}
      {typeof plan==='string'&&<div className="info-note hr-warn" role="alert"><AlertTriangle size={16}/><span>Import failed: {plan}</span></div>}
      {ok&&<div className="info-note"><Info size={16}/><span className="lv-pre">{hrLeaveImportPreview(plan as Exclude<typeof plan,string|undefined>)}</span></div>}
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button>
        <button className="btn primary" disabled={busy||!ok} onClick={()=>{void run({type:'HR_LEAVE_IMPORT',data},'Leave data imported',{close:true});}}>Merge</button></div></div></>;
}

/** 'hr-leave-clear': SCC Clear All — type DELETE (uppercase). HCC: leave records are cancelled and kept for audit (DEC-119). Owner. */
export function HRLeaveClearModal(){
  const {s,run,closeModal,busy,toast}=useHcc();const [v,setV]=useState('');
  const backup=()=>{const f=hrLeaveExportJson(s);void saveTextFile(f.name,f.json,'application/json').then(()=>toast(`${f.name} saved`));};
  return <><div className="modal-heading"><h2>Clear All leave data</h2><p>This clears:</p></div>
    <ul className="lv-clear">{hrLeaveClearList(s).map(t=><li key={t}>{t}</li>)}</ul>
    <p className="footnote padded">Department caps and blackouts are kept (SCC). The staff master is not touched.</p>
    <form className="form" onSubmit={e=>{e.preventDefault();if(v.trim()!=='DELETE'){toast('Type DELETE (uppercase) to confirm',true);return;}void run({type:'HR_LEAVE_CLEAR',confirm:v.trim()},'All data cleared',{close:true});}}>
      <label>Type DELETE to confirm<input value={v} onChange={e=>setV(e.target.value)} autoComplete="off"/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={backup}><Download size={16}/> Backup First</button><button type="button" className="btn" onClick={closeModal}>Cancel</button>
        <button className="btn danger" disabled={busy}><Trash2 size={16}/> Clear All</button></div>
    </form></>;
}

/** 'hr-leave-decide' (kept for older links): one date record, room = 'approve' | 'reject'; the note is optional (SCC asks none). */
export function HRLeaveDecideModal({id,room}:{id?:string;room?:string}){
  const {s,closeModal,busy}=useHcc();const decide=useLeaveDecide();const l=hrSlice(s).leaves.find(x=>x.id===id);const [note,setNote]=useState('');
  if(!l)return <Empty title="Leave record unavailable"/>;
  const approve=room!=='reject';
  return <><div className="modal-heading"><h2>{approve?'Approve leave':'Reject leave'}</h2><p>{l.id} · {staffName(s,l.staffId)} · {hrLeaveName(s,l.type)} · {hrKindLabel(l)} · {shortDate(l.date)}{l.reason?` · “${l.reason}”`:''} · requested by {userName(s,l.requestedBy)}</p></div>
    <form className="form" onSubmit={e=>{e.preventDefault();void decide(l,approve,{note:note.trim()||undefined,close:true});}}>
      <label>Note (optional)<input maxLength={200} value={note} onChange={e=>setNote(e.target.value)} enterKeyHint="done"/></label>
      <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Cancel</button><button className={'btn '+(approve?'primary':'danger')} disabled={busy}>{approve?'Approve':'Reject'}</button></div>
    </form></>;
}
