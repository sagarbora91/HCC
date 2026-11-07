// F12-RPT-GUEST · rpt.guests.upsell — Upsell and upgrade (folio lines UPG / LCO1 / LCO2, booked type kept by meas-cap-desk). Switch meas.guest.upsell.
// Revenue per arrival = upgrade + late check-out + breakfast add-on + extra bed lines (before GST) / arrivals, rounded half up. Money = paise.
import type {State,Booking} from '../../types';
import {addMinutes} from '../../money';
import {registerReport} from '../catalogue';
import {measTarget} from '../../streams/measure';
import {rangeGuard,userLabel} from '../guests/shared';
import {T,I,M,roundHalf,capNote} from './shared';

export const UPSELL_COLS=[I('arrivals','Arrivals'),I('upgradesPaid','Upgrades paid'),I('upgradesFree','Upgrades free'),T('approver','Approver'),M('lateCheckout','Late check-out charged'),I('dayUse','Day-use sold'),M('breakfast','Breakfast add-on'),M('extraBed','Extra bed'),M('revenuePerArrival','Revenue per arrival')];
const BASIS='Stays checked in within the range (up to 92 days), one summary row. Upgrades by the booked type kept at booking; late check-out = lines LCO1 / LCO2; day-use = arrival and departure on the same day; revenue per arrival = upsell lines before GST / arrivals.';
const NEED='UPG / LCO items, booked type kept (meas-cap-desk)';
/** Default upsell revenue per arrival when no target is stored: Rs 50 (paise). The contract lists no upsell target; the Owner can set meas.target.upsellPerArrival later. */
export const UPSELL_TARGET_DEFAULT=5000;
const sum=(b:Booking,test:(l:Booking['lines'][number])=>boolean)=>b.lines.filter(l=>test(l)&&!l.reversalOf).reduce((a,l)=>a+l.amount,0);
const isUpg=(l:{label:string})=>l.label.startsWith('UPG ·');
const isLco=(l:{label:string})=>l.label.startsWith('LCO1 ·')||l.label.startsWith('LCO2 ·');
const isBf=(l:{label:string})=>/breakfast/i.test(l.label);
const isBed=(l:{label:string})=>/extra bed/i.test(l.label);

export function arrivalsOf(s:State,from:string,to:string,user?:string):Booking[]{
  const name=user?s.users.find(u=>u.id===user)?.name:undefined;
  return s.bookings.filter(b=>(b.status==='In house'||b.status==='Departed')&&b.checkedInAt&&b.checkedInAt.slice(0,10)>=from&&b.checkedInAt.slice(0,10)<=to
    &&(!name||s.audit.some(a=>a.action==='CHECKIN'&&a.at===addMinutes(b.checkedInAt!,1)&&a.actor===name)));
}
export type UpsellFigures={arrivals:number;paid:number;free:number;freeNoApprover:number;approvers:string[];lco:number;dayUse:number;bf:number;bed:number;upg:number;total:number;perArrival:number|null};
export function upsellFigures(s:State,list:Booking[]):UpsellFigures{
  const ex=(b:Booking)=>s.measX?.bookingExtra?.[b.id];
  const free=list.filter(b=>ex(b)?.upgrade==='free');
  const f={arrivals:list.length,paid:list.filter(b=>ex(b)?.upgrade==='paid').length,free:free.length,freeNoApprover:free.filter(b=>!ex(b)!.upgradeApprover).length,
    approvers:[...new Set(free.map(b=>ex(b)!.upgradeApprover).filter((x):x is string=>!!x).map(u=>userLabel(s,u)))].sort(),
    lco:list.reduce((a,b)=>a+sum(b,isLco),0),dayUse:list.filter(b=>b.arrival===b.departure).length,bf:list.reduce((a,b)=>a+sum(b,isBf),0),bed:list.reduce((a,b)=>a+sum(b,isBed),0),
    upg:list.reduce((a,b)=>a+sum(b,isUpg),0)};
  const total=f.lco+f.bf+f.bed+f.upg;
  return {...f,total,perArrival:f.arrivals?roundHalf(total/f.arrivals):null};
}
/** Exception rule (red row): a free upgrade without an approver; upsell revenue per arrival below target. */
export function upsellRed(s:State,f:Pick<UpsellFigures,'freeNoApprover'|'perArrival'>){
  const target=measTarget(s,'meas.target.upsellPerArrival')??UPSELL_TARGET_DEFAULT;
  const low=f.perArrival!==null&&f.perArrival<target;
  return {freeNoApprover:f.freeNoApprover>0,belowTarget:low,red:f.freeNoApprover>0||low};
}
export const upsell=registerReport({
  id:'rpt.guests.upsell',title:'Upsell and upgrade',area:'guests',priority:'P2',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'deskUser',label:'Desk user',type:'user'}],
  columns:UPSELL_COLS,dateBasis:BASIS,formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,92,s.date);
    const list=arrivalsOf(s,p.from!,p.to!,p.deskUser as string|undefined);
    if(!list.length)return {params:p,columns:UPSELL_COLS,rows:[],about:{dateBasis:capNote(BASIS,NEED)}};
    const f=upsellFigures(s,list);
    return {params:p,columns:UPSELL_COLS,rows:[{kind:'row',cells:{arrivals:f.arrivals,upgradesPaid:f.paid,upgradesFree:f.free,approver:f.approvers.join(', '),lateCheckout:f.lco,dayUse:f.dayUse,
      breakfast:f.bf,extraBed:f.bed,revenuePerArrival:f.perArrival}}]};
  },
});
