// F7-R3 · rpt.guests.rate-changes — Rate change detail (catalogue A36; LF A1 data gst.stay_rate_change). Money gate PERM-RPT-002 (the report's own permission).
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {strState} from '../../streams/stay-rate';
import {bookingNo,rangeGuard,userLabel} from './shared';

const T=(key:string,label:string,mask?:RptColumn['mask']):RptColumn=>({key,label,type:'text',money:false,...(mask?{mask}:{})});
const I=(key:string,label:string):RptColumn=>({key,label,type:'int',money:false});
const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});
export const RATE_CHANGE_COLS:RptColumn[]=[T('changedAt','Changed at'),T('room','Room'),T('booking','Booking'),T('guest','Guest','guestName'),M('oldRate','Old rate'),M('newRate','New rate'),
  I('oldGuests','Old guests'),I('newGuests','New guests'),T('reason','Reason'),T('authorisedBy','Authorised by'),T('changedBy','Changed by')];

export const rateChanges=registerReport({
  id:'rpt.guests.rate-changes',title:'Rate change detail',area:'guests',priority:'P1',status:'built',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'room',label:'Room',type:'room'},{key:'changedBy',label:'Changed by',type:'user'}],
  columns:RATE_CHANGE_COLS,
  dateBasis:'Changes by the business date they were made on (range up to 92 days). Rates are per night; guests = adults + children. Footer: count and net change in rate (new − old).',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',legacyIds:['A36'],
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,92,s.date);
    const list=strState(s).changes.filter(c=>c.date>=p.from!&&c.date<=p.to!&&(!p.room||c.room===p.room)&&(!p.changedBy||c.by===p.changedBy)).sort((a,c)=>a.at.localeCompare(c.at)||a.id.localeCompare(c.id));
    const rows:RptRow[]=list.map(c=>{const b=s.bookings.find(x=>x.id===c.booking);
      return {kind:'row',cells:{changedAt:c.at.replace('T',' ').slice(0,16),room:c.room,booking:b?bookingNo(b):c.booking,guest:b?.guest??'',oldRate:c.oldRate,newRate:c.newRate,oldGuests:c.oldPeople+c.oldChildren,
        newGuests:c.newPeople+c.newChildren,reason:c.reason,authorisedBy:userLabel(s,c.approvedBy??c.by),changedBy:userLabel(s,c.by)}};});
    const net=list.reduce((a,c)=>a+c.newRate-c.oldRate,0);
    rows.push({kind:'total',cells:{changedAt:`Total · ${list.length} change${list.length===1?'':'s'}`,newRate:net,reason:'Net change in rate (new − old)'}});
    return {params:p,columns:RATE_CHANGE_COLS,rows};
  },
});
