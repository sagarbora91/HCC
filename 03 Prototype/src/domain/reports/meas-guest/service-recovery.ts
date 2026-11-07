// F12-RPT-GUEST · rpt.guests.service-recovery — Service recovery (data GSVComplaint, GSVRecovery, root cause side table). Switch meas.guest.recovery.
// Money: the compensation column only (PERM-RPT-002 gate; removed, not blanked, for other roles; Housekeeping / Maintenance never hold it, DEC-061).
import type {State} from '../../types';
import {registerReport} from '../catalogue';
import {gsvSlice,gcatOverSla,gcatSlaDue,gcatKeyOf,type GSVComplaint} from '../../streams/services';
import {rangeGuard,userLabel} from '../guests/shared';
import {T,I,P,M,pct1,avg1,minutesBetween,capNote} from './shared';

export const RECOVERY_COLS=[T('category','Category'),T('room','Room'),I('complaints','Complaints'),T('hours','Hours to resolve'),P('withinSla','Within SLA %'),T('rootCause','Root cause'),M('compensation','Compensation'),T('approver','Approver'),I('repeat','Repeat in 90 days')];
const BASIS='Complaints by the day they were raised (range up to 92 days), one row per category and room. Hours to resolve = average of resolved complaints; within SLA % over complaints whose category has an SLA; repeat = complaints with an earlier one for the same room and category in the 90 days before. Compensation is the recovery amount posted.';
const NEED='rootCause code list (meas-cap-guest)';
export const REPEAT_DAYS=90;

const days=(a:string,b:string)=>minutesBetween(a,b)/1440;
/** Repeat cause: an earlier complaint for the same room and category no more than 90 days before this one. */
export function isRepeat(s:State,c:GSVComplaint):boolean{
  if(!c.room)return false;const k=gcatKeyOf(s,c);
  return gsvSlice(s).complaints.some(p=>p.id!==c.id&&p.room===c.room&&gcatKeyOf(s,p)===k&&p.createdAt<c.createdAt&&days(p.createdAt,c.createdAt)<=REPEAT_DAYS);
}
/** Compensation without approval: a recovery amount with no approval id on it. */
export const unapproved=(c:GSVComplaint)=>!!c.recovery&&c.recovery.amount>0&&!c.recovery.approvalId;
export const causeLabel=(s:State,c:GSVComplaint)=>{const code=s.measX?.rootCause?.[c.id];return code?(s.measX?.rootCauseList?.find(r=>r.code===code)?.label??code):'';};
export function complaintsOf(s:State,from:string,to:string,category?:string,room?:string){
  return gsvSlice(s).complaints.filter(c=>c.createdAt.slice(0,10)>=from&&c.createdAt.slice(0,10)<=to&&(!category||c.category===category||gcatKeyOf(s,c)===category)&&(!room||c.room===room));
}
/** Exception rule (red row) of one group of complaints. */
export const recoveryRed=(s:State,list:GSVComplaint[])=>{const repeat=list.filter(c=>isRepeat(s,c)).length,noApproval=list.filter(unapproved).length;return {repeat,noApproval,red:repeat>0||noApproval>0};};

export const serviceRecovery=registerReport({
  id:'rpt.guests.service-recovery',title:'Service recovery',area:'guests',priority:'P2',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-001'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'category',label:'Category',type:'text'},{key:'room',label:'Room',type:'room'}],
  columns:RECOVERY_COLS,dateBasis:BASIS,formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,92,s.date);
    const list=complaintsOf(s,p.from!,p.to!,p.category as string|undefined,p.room as string|undefined);
    if(!list.length)return {params:p,columns:RECOVERY_COLS,rows:[],about:{dateBasis:capNote(BASIS,NEED)}};
    const groups=new Map<string,GSVComplaint[]>();
    for(const c of list){const k=c.category+'\u0000'+(c.room??'');(groups.get(k)??groups.set(k,[]).get(k)!).push(c);}
    const rows=[...groups.entries()].sort((a,b)=>a[0].localeCompare(b[0])).map(([,cs])=>{
      const done=cs.filter(c=>c.resolvedAt),sla=cs.filter(c=>gcatSlaDue(s,c)),causes=[...new Set(cs.map(c=>causeLabel(s,c)).filter(Boolean))].sort();
      const approvers=[...new Set(cs.map(c=>c.recovery?.approvalId?userLabel(s,s.approvals.find(a=>a.id===c.recovery!.approvalId)?.approver):'').filter(Boolean))].sort();
      // MEAS2-B (M1): red group = a repeat in 90 days or a compensation without approval.
      return {kind:'row' as const,...(recoveryRed(s,cs).red?{group:'red'}:{}),cells:{category:cs[0].category,room:cs[0].room??'',complaints:cs.length,
        hours:done.length?avg1(done.reduce((a,c)=>a+minutesBetween(c.createdAt,c.resolvedAt!),0)/60,done.length):null,
        withinSla:pct1(sla.filter(c=>!gcatOverSla(s,c)).length,sla.length),rootCause:causes.join(', '),
        compensation:cs.reduce((a,c)=>a+(c.recovery?.amount??0),0),approver:approvers.join(', '),repeat:cs.filter(c=>isRepeat(s,c)).length}};
    });
    return {params:p,columns:RECOVERY_COLS,rows};
  },
});
