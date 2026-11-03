// F7-R3 · rpt.guests.movement-register — Arrivals and departures register (catalogue A30). D-FWD range (may look ahead). Money: late check-out
// charge and balance due (gate PERM-RPT-002). Server twin: Hcc.Reporting/Reports/Guests/MovementRegisterReport.cs (same column keys).
import type {RptColumn,RptRow} from '../types';
import type {Booking} from '../../types';
import {registerReport} from '../catalogue';
import {totals} from '../../selectors';
import {lineTax} from '../../gst';
import {nights} from '../../money';
import {deskBasis,deskLateLineId,deskPurposeLabel} from '../../streams/desk-fields';
import {bookingNo,checkInAtOf,checkOutAtOf,guestNameOf,paxOf,rangeGuard} from './shared';

const T=(key:string,label:string,mask?:RptColumn['mask']):RptColumn=>({key,label,type:'text',money:false,...(mask?{mask}:{})});
export const MOVEMENT_COLS:RptColumn[]=[{key:'date',label:'Date',type:'date',money:false},T('booking','Booking'),T('guest','Guest','guestName'),T('room','Room'),T('roomType','Room type'),
  {key:'pax',label:'Pax',type:'int',money:false},{key:'nights',label:'Nights',type:'int',money:false},T('source','Source'),T('status','Status'),T('checkInTime','Check-in time'),
  T('checkOutTime','Check-out time'),T('basis','Check-out basis'),{key:'lateCharge',label:'Late check-out charge',type:'money',money:true},T('purpose','Purpose of visit'),T('vehicle','Vehicle no'),
  {key:'balanceDue',label:'Balance due',type:'money',money:true}];
export const MOVEMENT_STATUS=['all','booked','checked in','checked out','cancelled','no-show'];
/** Status shown in the register and the group the Status filter uses. */
export const movementStatus=(b:Booking)=>b.status==='In house'?'checked in':b.status==='Departed'?'checked out':b.status==='Cancelled'||b.status==='Expired'?'cancelled':b.status==='No show'?'no-show':'booked';
const cap=(t:string)=>t.charAt(0).toUpperCase()+t.slice(1);

export const movementRegister=registerReport({
  id:'rpt.guests.movement-register',title:'Arrivals and departures register',area:'guests',priority:'P1',status:'built',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-001'],dateClass:'D-FWD',
  params:[{key:'type',label:'Type',type:'enum',options:['both','arrivals','departures'],default:'both'},{key:'from',label:'From',type:'date',dateClass:'D-FWD'},{key:'to',label:'To',type:'date',dateClass:'D-FWD'},
    {key:'status',label:'Status',type:'enum',options:MOVEMENT_STATUS,default:'all'},{key:'roomType',label:'Room type',type:'text'},{key:'source',label:'Source',type:'text'}],
  columns:MOVEMENT_COLS,
  dateBasis:'Arrivals by arrival date, departures by departure date (booked date until the guest leaves); range up to 92 days, up to 90 days ahead. Late check-out charge and balance due include GST.',
  formats:['screen','xlsx','pdf','csv'],phone:'summary',legacyIds:['A30'],
  select:(s,_uid,p)=>{
    const from=p.from!,to=p.to!;rangeGuard(from,to,92,s.date,90);
    const want=p.type??'both',st=p.status??'all',rt=String(p.roomType??'').trim().toLowerCase(),src=String(p.source??'').trim().toLowerCase();
    const items:{date:string;b:Booking;kind:'arrival'|'departure'}[]=[];
    for(const b of s.bookings){
      if(want!=='departures'&&b.arrival>=from&&b.arrival<=to)items.push({date:b.arrival,b,kind:'arrival'});
      if(want!=='arrivals'&&b.departure>=from&&b.departure<=to&&['Confirmed','In house','Departed'].includes(b.status))items.push({date:b.departure,b,kind:'departure'});
    }
    const rows:RptRow[]=[];let day='',dRooms=0,dPax=0,dLate=0,dBal=0,tRooms=0,tPax=0,tLate=0,tBal=0;
    const flush=()=>{if(!day)return;rows.push({kind:'subtotal',group:day,cells:{date:day,booking:`Day total · ${dRooms} room${dRooms===1?'':'s'}`,pax:dPax,lateCharge:dLate,balanceDue:dBal}});};
    const sorted=items.filter(({b})=>(st==='all'||movementStatus(b)===st)&&(!rt||(s.rooms.find(r=>r.id===b.room)?.type??'').toLowerCase()===rt)&&(!src||b.source.toLowerCase()===src))
      .sort((a,c)=>a.date.localeCompare(c.date)||a.b.room.localeCompare(c.b.room)||a.kind.localeCompare(c.kind));
    for(const {date,b} of sorted){
      if(date!==day){flush();day=date;dRooms=dPax=dLate=dBal=0;}
      const late=b.lines.filter(l=>l.id===deskLateLineId(b.id)).reduce((a,l)=>a+l.amount+lineTax(l),0),bal=totals(b).balance,pax=paxOf(b);
      rows.push({kind:'row',group:date,cells:{date,booking:bookingNo(b),guest:guestNameOf(s,b),room:b.room,roomType:s.rooms.find(r=>r.id===b.room)?.type??'',pax,nights:nights(b.arrival,b.departure),
        source:b.source,status:cap(movementStatus(b)),checkInTime:checkInAtOf(s,b),checkOutTime:checkOutAtOf(s,b),basis:deskBasis(s,b)==='24h'?'24-hour':'Fixed time',lateCharge:late,
        purpose:deskPurposeLabel(s,b.purpose),vehicle:b.vehicle?.no??'',balanceDue:bal}});
      dRooms++;dPax+=pax;dLate+=late;dBal+=bal;tRooms++;tPax+=pax;tLate+=late;tBal+=bal;
    }
    flush();
    rows.push({kind:'total',cells:{booking:`Total · ${tRooms} room${tRooms===1?'':'s'}`,pax:tPax,lateCharge:tLate,balanceDue:tBal}});
    return {params:p,columns:MOVEMENT_COLS,rows};
  },
});
