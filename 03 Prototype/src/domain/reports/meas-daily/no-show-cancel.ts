// F12-RPT-DAILY · rpt.guests.no-show-cancel (catalogue 1.7, meas.daily.no-show). Built on bookings + folio lines directly (F8 qw-noshow is not on main):
// a retention charge is a positive folio line whose label names retention / no-show / cancellation; a waiver is a negative line naming a waiver.
// Exception: a waiver without an approver; no-show rate above NOSHOW_TARGET_PCT (5 %: no meas.target key exists) marks every no-show row of the range.
// Early departures are not recorded in the prototype (no planned-departure snapshot) and are listed in the DONE file. Server twin: NoShowCancelReport.cs.
import type {State} from '../../types';
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {registerMeasReport} from '../../streams/measure';
import {bookingNo,rangeGuard,userLabel} from '../guests/shared';
import {T,I,M,EXC,inRange} from './shared';

export const NOSHOW_COLS:RptColumn[]=[T('booking','Booking'),T('channel','Channel'),I('leadDays','Lead days'),T('kind','Kind'),M('retention','Retention charge applied'),M('waived','Charge waived'),T('waivedBy','Waived by'),T('approver','Approver'),EXC()];
export const NOSHOW_KEY='meas.daily.no-show';
export const NOSHOW_TARGET_PCT=5;
const RETENTION=/retention|no[- ]?show|cancellation/i,WAIVER=/waive/i;
/** No-show rate (percent, integer maths via the cross-multiply in the caller) over bookings arriving in the range that were not just holds. */
export function noShowCounts(s:State,from?:string,to?:string){
  const inR=s.bookings.filter(b=>inRange(b.arrival,from,to)&&b.status!=='Held'&&b.status!=='Expired');
  return {noShows:inR.filter(b=>b.status==='No show').length,total:inR.length};
}
export const noShowRateRed=(noShows:number,total:number)=>total>0&&noShows*100>NOSHOW_TARGET_PCT*total;

export function noShowRows(s:State,p:{from?:string;to?:string;channel?:string;kind?:string}):RptRow[]{
  const {noShows,total}=noShowCounts(s,p.from,p.to),rateRed=noShowRateRed(noShows,total);
  return s.bookings.filter(b=>(b.status==='No show'||b.status==='Cancelled')&&inRange(b.arrival,p.from,p.to)&&(!p.channel||b.source.toLowerCase()===p.channel.toLowerCase())
      &&(!p.kind||p.kind==='all'||(p.kind==='no-show')===(b.status==='No show')))
    .sort((a,b)=>a.arrival.localeCompare(b.arrival)||a.id.localeCompare(b.id)).map(b=>{
      const ret=b.lines.filter(l=>l.amount>0&&RETENTION.test(l.label)).reduce((a,l)=>a+l.amount,0);
      const wl=b.lines.filter(l=>l.amount<0&&WAIVER.test(l.label)),waived=wl.reduce((a,l)=>a-l.amount,0);
      const ap=wl.map(l=>l.approvalId?s.approvals.find(a=>a.id===l.approvalId):undefined).find(Boolean);
      const waivedBy=ap?.actor??'',approver=ap?.approver??'';
      const why:string[]=[];
      if(waived>0&&!approver)why.push('Retention waived without an approver');
      if(b.status==='No show'&&rateRed)why.push(`No-show rate above ${NOSHOW_TARGET_PCT} %`);
      return {kind:'row' as const,cells:{booking:bookingNo(b),channel:b.source,leadDays:null,kind:b.status==='No show'?'No-show':'Cancelled',retention:ret,waived,waivedBy:userLabel(s,waivedBy),approver:userLabel(s,approver),exception:why.join('; ')}};
    });
}
registerMeasReport('rpt.guests.no-show-cancel',NOSHOW_KEY);
export const noShowCancel=registerReport({
  id:'rpt.guests.no-show-cancel',title:'No-show, cancellation and early departure',area:'guests',priority:'P2',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'channel',label:'Channel',type:'text'},{key:'kind',label:'Kind',type:'enum',options:['all','no-show','cancelled'],default:'all'}],
  columns:NOSHOW_COLS,
  dateBasis:'No-shows and cancellations by arrival date (range up to 92 days). Retention charge = positive folio lines naming retention / no-show / cancellation; waived = negative lines naming a waiver. Lead days are blank until the booking date is captured. Early departures are not recorded yet. Exception: a waiver without an approver; no-show rate above 5 %.',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{rangeGuard(p.from,p.to,92,s.date);return {params:p,columns:NOSHOW_COLS,rows:noShowRows(s,p)};},
});
