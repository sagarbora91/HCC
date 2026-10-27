// Module 17 · self-service "My shifts & pay" (page 'my-hr'): my roster, check-in, my month, my leave, my payslips.
// Shows only the signed-in demo user's own record — never other people's pay or hotel money.
// W05-B: latest payslip card with the PF / ESIC / PT deduction breakdown (PDF through the native share/print hook) and SCC leave balances per financial year.
// SCC leave port (DEC-116): own leave = one record per date (pending ones can be removed — cancelled, kept for audit); balances = approved days only.
import React from 'react';
import {LogIn,CalendarPlus,FileText,Clock,Printer} from 'lucide-react';
import {useHcc} from '../../app/context';
import {PageTitle,Pill,Empty,dateLabel,shortDate} from '../../components/ui';
import {ListCard,KpiCard,StickyFooter} from '../../app/mobile/components';
import {
  money,isDayClosed,hrSlice,hrStaffOfUser,hrShiftFor,hrShiftInfo,hrMonday,hrWeek,hrKey,hrDayStatus,hrCounts,hrLeaveBalance,hrVisiblePayslips,
  hrMonthLabel,hrApprovedLeaveOn,hrCheckinStatus,hrFy,HR_ATT_LABEL,hrLeaveName,hrHolidayOn,HR_WEEKDAYS,hrLeaveApproverName,type HRStaff,type HRRun,type HRLine,type HRLeave,
} from '../../services/mock';

export const hrLeaveTone=(st:string)=>st==='Approved'?'green':st==='Pending'?'orange':st==='Rejected'?'hr-red':'neutral';
export const hrAttTone=(st:string)=>st==='P'?'green':st==='L'||st==='H'||st==='NT'?'orange':st==='A'?'hr-red':'blue';

const kind=(l:HRLeave,s:ReturnType<typeof useHcc>['s'])=>hrLeaveName(s,l.type)+(l.half?` · ½ (${l.half==='AM'?'morning':'afternoon'})`:'');
const statusText=(l:HRLeave)=>l.status==='Cancelled'?'Removed':l.status;
/** Latest payslip with the deduction breakdown (own payslip only). */
function PayslipCard({run,line,phone,onOpen}:{run:HRRun;line:HRLine;phone:boolean;onOpen:()=>void}){
  const rows:[string,number][]=[['Gross earnings',line.gross],['Provident Fund',-line.pf],['ESIC',-line.esic],['Professional tax',-line.pt],...(line.advance?[['Advance recovery',-line.advance] as [string,number]]:[])];
  return <section className={'card hr-slip-card'+(phone?' padded':'')}><div className="section-title"><div><h2>Payslip · {hrMonthLabel(run.month)}</h2><p>{line.payableDays} payable days · locked pay-run</p></div><FileText size={18}/></div>
    <div className="summary-list padded">{rows.map(([k,v])=><div key={k}><span>{k}</span><strong className="number">{v<0?'− '+money(-v):money(v)}</strong></div>)}
      <div className="hr-slip-net"><span>Net pay</span><strong className="number">{money(line.net)}</strong></div></div>
    <div className="padded"><button className="btn primary hr-slip-btn" onClick={onOpen}><Printer size={16}/> Payslip PDF</button></div></section>;
}

function useMine(){
  const ctx=useHcc();const {s,uid}=ctx;const me=hrStaffOfUser(s,uid);
  if(!me)return {ctx,me};
  const att=hrSlice(s).att[hrKey(s.date,me.id)],shift=hrShiftFor(s,me,s.date),leave=hrApprovedLeaveOn(s,me.id,s.date);
  const canCheckin=me.active&&!att&&shift!=='OFF'&&!leave&&!isDayClosed(s);
  return {ctx,me,att,shift,leave,canCheckin};
}
function todayText(me:HRStaff,s:ReturnType<typeof useHcc>['s']){
  const sh=hrShiftInfo(hrShiftFor(s,me,s.date),s);return sh.code==='OFF'?'Weekly off today':`${sh.label} shift ${sh.start}–${sh.end}`;
}

export function MyHr({phone=false}:{phone?:boolean}){
  const {ctx,me,att,shift,leave,canCheckin}=useMine();const {s,uid,run,open,busy}=ctx;
  if(!me)return <>{!phone&&<PageTitle title="My shifts & pay" sub="Self-service for staff with a demo login"/>}
    <Empty title="No staff record linked" body="Your demo user is not linked to a staff record. Switch to a staff member such as Ravi (Housekeeper) or Meera (Manager)."/></>;
  const checkin=()=>run({type:'HR_ATT_MARK',staffId:me.id},`Checked in at ${s.clock.slice(11)} · ${HR_ATT_LABEL[hrCheckinStatus(s.clock,shift!,s)]}`);
  const week=hrWeek(hrMonday(s.date)),ym=s.date.slice(0,7),c=hrCounts(s,me,ym,s.date);
  const myLeaves=hrSlice(s).leaves.filter(l=>l.staffId===me.id).sort((a,b)=>b.date.localeCompare(a.date));
  const slips=hrVisiblePayslips(s,uid).filter(p=>p.line.staffId===me.id);
  const bal=hrLeaveBalance(s,me.id,s.date),fy=hrFy(s.date).label,latest=slips[0];
  const todayStatus=att?<Pill tone={hrAttTone(att.status)}>{HR_ATT_LABEL[att.status]} · {att.at.slice(11)}</Pill>:leave?<Pill tone="blue">On leave</Pill>:shift==='OFF'?<Pill>Off</Pill>:hrHolidayOn(s,s.date)?<Pill tone="green">{hrHolidayOn(s,s.date)!.kind==='paid'?'Paid holiday':'Restricted holiday'} · {hrHolidayOn(s,s.date)!.name}</Pill>:<Pill tone="orange">Not checked in</Pill>;
  const dayCell=(d:string)=>{const x=hrDayStatus(s,me,d),sh=hrShiftInfo(hrShiftFor(s,me,d),s);
    const st=x.kind==='att'?HR_ATT_LABEL[x.att!.status]:x.kind==='leave'?'Leave':x.kind==='off'?'Off':x.kind==='holiday'?'Holiday':d<s.date?'Unmarked':d===s.date?'Today':'';
    return {sh,st};};

  if(phone)return <div className="hr-m">
    <section className="hr-m-today card padded"><small className="eyebrow">TODAY · {dateLabel(s.date)}</small><h2>{todayText(me,s)}</h2><div className="inline">{todayStatus}<span className="muted">{me.name} · {me.designation}</span></div></section>
    <h2 className="m-group">MY WEEK</h2>
    {week.map(d=>{const {sh,st}=dayCell(d);return <ListCard key={d} tone={d===s.date?'hr-today':''} title={`${HR_WEEKDAYS[new Date(d+'T12:00:00Z').getUTCDay()]} ${shortDate(d)}`} sub={sh.code==='OFF'?'Weekly off':`${sh.label} ${sh.start}–${sh.end}`} right={st}/>;})}
    <h2 className="m-group">THIS MONTH SO FAR</h2>
    <div className="hr-kpis"><KpiCard label="Present" value={c.P+c.L} sub={`${c.L} late`}/><KpiCard label="Half / absent" value={`${c.H} / ${c.A}`}/><KpiCard label="Leave" value={c.paidLeave+c.lopLeave}/><KpiCard label="Overtime" value={`${Math.round(c.otMin/6)/10} h`}/></div>
    {latest&&<><h2 className="m-group">LATEST PAYSLIP</h2><PayslipCard phone run={latest.run} line={latest.line} onOpen={()=>open({type:'hr-payslip',id:latest.run.id+'|'+me.id})}/></>}
    <h2 className="m-group">MY LEAVE · {fy}</h2>
    <div className="hr-kpis">{bal.map(b=><KpiCard key={b.type} label={hrLeaveName(s,b.type)} value={`${b.left} left`} sub={`of ${b.entitled} · used ${b.used}${b.over?` · over by ${b.over}`:''}`}/>)}</div>
    {myLeaves.length?myLeaves.map(l=><ListCard key={l.id} title={`${shortDate(l.date)} · ${kind(l,s)}`} sub={`${l.days} day · approver ${hrLeaveApproverName(s,l)||'—'}${l.reason?' · '+l.reason:''}`}
      right={<Pill tone={hrLeaveTone(l.status)}>{statusText(l)}</Pill>} onClick={l.status==='Pending'?()=>{if(window.confirm('Remove this leave record?'))run({type:'HR_LEAVE_CANCEL',id:l.id},'Leave removed');}:undefined}/>)
      :<p className="hr-m-note">No leave requests yet.</p>}
    <h2 className="m-group">MY PAYSLIPS</h2>
    {slips.length?slips.map(p=><ListCard key={p.run.id} title={hrMonthLabel(p.run.month)} sub={`Net pay ${money(p.line.net)}`} meta={`${p.line.payableDays} payable days`} onClick={()=>open({type:'hr-payslip',id:p.run.id+'|'+me.id})}/>)
      :<p className="hr-m-note">Payslips appear after payroll is locked.</p>}
    <StickyFooter>{canCheckin?<button className="btn primary" disabled={busy} onClick={checkin}><LogIn size={18}/> Check in now · {s.clock.slice(11)}</button>
      :<button className="btn primary" onClick={()=>open({type:'hr-leave'})}><CalendarPlus size={18}/> Request leave</button>}</StickyFooter>
  </div>;

  return <><PageTitle title="My shifts & pay" sub={`${me.name} · ${me.designation} · your own roster, attendance, leave and payslips`}
      action={<div className="inline">{canCheckin&&<button className="btn primary" disabled={busy} onClick={checkin}><LogIn size={16}/> Check in now</button>}<button className="btn" onClick={()=>open({type:'hr-leave'})}><CalendarPlus size={16}/> Request leave</button></div>}/>
    <div className="closing-banner"><div><div className="eyebrow">TODAY · {dateLabel(s.date)} · SIMULATED CLOCK {s.clock.slice(11)}</div><h2>{todayText(me,s)}</h2><p>Check-in after shift start + 15 minutes is recorded as late. Corrections are made by a manager.</p></div>{todayStatus}</div>
    <div className="two-col">
      <section className="card"><div className="section-title"><h2>My week</h2><span className="muted">{shortDate(week[0])} – {shortDate(week[6])}</span></div>
        <div className="table-wrap"><table><thead><tr><th>Day</th><th>Shift</th><th>Attendance</th></tr></thead><tbody>{week.map(d=>{const {sh,st}=dayCell(d);
          return <tr key={d} className={d===s.date?'hr-row-today':''}><td><strong>{HR_WEEKDAYS[new Date(d+'T12:00:00Z').getUTCDay()]} {shortDate(d)}</strong></td><td>{sh.code==='OFF'?'Weekly off':`${sh.label} ${sh.start}–${sh.end}`}</td><td>{st}</td></tr>;})}</tbody></table></div></section>
      <section className="card"><div className="section-title"><h2>{hrMonthLabel(ym)} so far</h2><Clock size={18}/></div>
        <div className="summary-list padded">{[['Present',c.P],['Late',c.L],['Half-days',c.H],['Absent',c.A],['Paid leave',c.paidLeave],['Leave without pay',c.lopLeave],['Weekly offs',c.off],['Overtime',`${Math.round(c.otMin/6)/10} h`]].map(([k,v])=><div key={k}><span>{k}</span><strong>{v}</strong></div>)}</div></section>
    </div>
    {latest&&<PayslipCard phone={false} run={latest.run} line={latest.line} onOpen={()=>open({type:'hr-payslip',id:latest.run.id+'|'+me.id})}/>}
    <div className="two-col">
      <section className="card"><div className="section-title"><div><h2>My leave · {fy}</h2><p>Balance: {bal.map(b=>`${b.type} ${b.left} of ${b.entitled}${b.over?` (over by ${b.over})`:''}`).join(' · ')} (SCC entitlements, April–March; approved days only)</p></div></div>
        {myLeaves.length?<div className="table-wrap"><table><thead><tr><th>Date</th><th>Leave</th><th>Status</th><th></th></tr></thead><tbody>{myLeaves.map(l=><tr key={l.id}>
          <td><strong>{shortDate(l.date)}</strong><small>{l.from!==l.to?`request ${shortDate(l.from)} – ${shortDate(l.to)} · `:''}approver {hrLeaveApproverName(s,l)||'—'}{l.reason?' · '+l.reason:''}</small></td><td>{kind(l,s)}</td><td><Pill tone={hrLeaveTone(l.status)}>{statusText(l)}</Pill></td>
          <td>{l.status==='Pending'&&<button className="text-btn" disabled={busy} onClick={()=>{if(window.confirm('Remove this leave record?'))run({type:'HR_LEAVE_CANCEL',id:l.id},'Leave removed');}}>Remove</button>}</td></tr>)}</tbody></table></div>
          :<Empty title="No leave requests" body="Use Request leave to ask for time off."/>}</section>
      <section className="card"><div className="section-title"><h2>My payslips</h2><FileText size={18}/></div>
        {slips.length?<div className="table-wrap"><table><thead><tr><th>Month</th><th className="number">Net pay</th><th></th></tr></thead><tbody>{slips.map(p=><tr key={p.run.id}>
          <td><strong>{hrMonthLabel(p.run.month)}</strong><small>{p.line.payableDays} payable days</small></td><td className="number">{money(p.line.net)}</td>
          <td><button className="text-btn" onClick={()=>open({type:'hr-payslip',id:p.run.id+'|'+me.id})}>View / print</button></td></tr>)}</tbody></table></div>
          :<Empty title="No payslips yet" body="Payslips appear here after payroll is locked."/>}</section>
    </div></>;
}
export const MyHrMobile=()=><MyHr phone/>;
