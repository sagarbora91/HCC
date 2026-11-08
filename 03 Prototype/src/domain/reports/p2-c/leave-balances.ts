// F8-RPT-P2-C · rpt.people.leave-balances — entitlement, used, pending and balance per employee per tracked leave type (CL, SL, EL) in the FY of the date.
// Adapter over hrEntFor / the leave records (SCC leave in full, DEC-116/119). Used = approved days up to the date; pending = requested days of the FY not yet decided.
import type {State} from '../../types';
import type {RptColumn} from '../types';
import {registerReport} from '../catalogue';
import {hrSlice,hrEntFor,hrFy,HR_TRACKED,HR_SCC_CATEGORY} from '../../streams/staff';
import {col,rowOf} from '../money/common';

export const LEAVE_COLS:RptColumn[]=[col('employee','Employee','text'),col('type','Leave type','text'),col('entitlement','Entitlement','int'),col('used','Used','int'),col('pending','Pending','int'),col('balance','Balance','int')];
export type LeaveLine={staff:string;type:string;ent:number;used:number;pending:number;balance:number};
export function leaveRows(s:State,asOf:string):LeaveLine[]{
  const fy=hrFy(asOf),H=hrSlice(s),out:LeaveLine[]=[];
  const staff=[...H.staff].filter(st=>st.joined<=asOf&&(st.active||(st.exitedOn&&st.exitedOn>asOf))).sort((a,b)=>a.name.localeCompare(b.name)||a.id.localeCompare(b.id));
  for(const st of staff){
    const ent=hrEntFor(s,st.id);
    for(const t of HR_TRACKED){
      let used=0,pending=0;
      for(const l of H.leaves)if(l.staffId===st.id&&l.type===t&&l.date>=fy.from&&l.date<=fy.to){
        if(l.status==='Approved'&&l.date<=asOf)used+=l.days;else if(l.status==='Pending')pending+=l.days;}
      out.push({staff:st.name,type:HR_SCC_CATEGORY[t],ent:ent[t],used,pending,balance:ent[t]-used});}}
  return out;
}
export const leaveBalances=registerReport({
  id:'rpt.people.leave-balances',title:'Leave balances',area:'people',priority:'P2',status:'built',moneyFlag:false,viewPerms:['PERM-HR-001'],dateClass:'D-ASOF',
  params:[{key:'asOf',label:'As on',type:'date',dateClass:'D-ASOF'}],columns:LEAVE_COLS,
  dateBasis:'The financial year (April to March) that contains the date. Entitlement = the policy quota with any per-employee override; used = approved leave days dated up to the date; pending = leave days of the year still waiting for a decision; balance = entitlement less used (no carry-forward). Half days count 0.5.',
  formats:['screen','xlsx','pdf'],phone:'yes',
  select:(s,_u,p)=>({params:p,columns:LEAVE_COLS,rows:leaveRows(s,String(p.asOf)).map(r=>rowOf('row',{employee:r.staff,type:r.type,entitlement:r.ent,used:r.used,pending:r.pending,balance:r.balance},r.staff))}),
});
