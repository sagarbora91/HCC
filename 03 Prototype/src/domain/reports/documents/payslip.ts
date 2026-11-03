// F7-R7 · payslip document (catalogue rpt.people.payslip, legal layout). Same release rule as the payslip dialog: payroll roles (Owner, Accountant with the
// reports right) see any line of any run; a staff member sees only their own line of a Locked run. All money is the stored HRLine; nothing recomputed.
import type {State} from '../../types';
import {HccError} from '../../errors';
import {hrSlice,hrCanPay,hrStaffOfUser,hrDeptName,hrMonthLabel,hrAmountWords} from '../../streams/staff';
import {type DocModel,T,R,userName,docMoneyRole} from './model';

export function payslipDocument(s:State,uid:string,runId:string,staffId:string):DocModel{
  const run=hrSlice(s).runs.find(r=>r.id===runId),line=run?.lines.find(l=>l.staffId===staffId);
  if(!run||!line)throw new HccError('NOT_FOUND','No such payslip.');
  const me=hrStaffOfUser(s,uid),own=me?.id===staffId&&run.status==='Locked';
  if(!docMoneyRole(s,uid)&&!own||!(hrCanPay(s,uid)||own))throw new HccError('FORBIDDEN','You can print only your own payslip.');
  const d=line.days,c=line.counts;
  const earn=[['Basic',line.basic],...(line.structure==='S'?[['HRA',line.hra],['Washing allowance',line.washing],['Special allowance',line.special]] as [string,number][]:[]),[d?`OT days (${d.otDays})`:'Overtime',line.ot]] as [string,number][];
  const ded:[string,number][]=[[`Provident Fund (${(run.rates?.pfEeBps??1200)/100}%)`,line.pf],[`ESIC (${(run.rates?.esicEeBps??75)/100}%)`,line.esic],['Professional tax',line.pt],['Advance recovery',line.advance]];
  const rows=[...earn.map(([k,v])=>R({item:k,earn:v,ded:null})),R({item:'Gross',earn:line.gross,ded:null},'subtotal'),...ded.map(([k,v])=>R({item:k,earn:null,ded:v})),R({item:'Total deductions',earn:null,ded:line.deductions},'subtotal'),R({item:'Net pay',earn:line.net,ded:null},'total')];
  return {id:'rpt.people.payslip',title:'Payslip',number:`${hrMonthLabel(run.month)} · ${line.staffId}`,property:s.settings.hotel,money:true,
    pairs:[['Employee',line.name],['Staff ID',line.staffId],['Designation',line.designation],['Department',hrDeptName(s,line.dept)],['Month',hrMonthLabel(run.month)],['Run',run.id],['Pay basis',line.payType==='monthly'?`${(line.base/100).toFixed(2)} / month`:`${(line.base/100).toFixed(2)} / day`],
      ['Payable days',String(line.payableDays)],['Attendance',d?`Present ${d.present} · Half ${d.half} · Absent ${d.absent} · Late ${d.late}`:`Present ${c.P} · Late ${c.L} · Absent ${c.A+c.unmarked}`],['Status',run.status==='Locked'?'Final':'Draft - not final']],
    tables:[{key:'pay',title:'Earnings and deductions',columns:[T('item','Item'),T('earn','Earnings','money'),T('ded','Deductions','money')],rows},
      {key:'er',title:'Employer contributions (not deducted)',columns:[T('item','Item'),T('amount','Amount','money')],rows:[R({item:'PF employer',amount:line.pfEr}),R({item:'ESIC employer',amount:line.esicEr})]}],
    notes:[hrAmountWords(line.net),`Prepared by ${userName(s,run.preparedBy)}${run.lockedBy?` · locked by ${userName(s,run.lockedBy)}`:''}. Statutory rates are illustrative - CA to confirm for a hotel.`],
    sign:['Employee','Authorised signatory'],basis:'Pay month.'};
}
