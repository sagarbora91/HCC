// F7-R3 · rpt.guests.guest-register — Guest register (statutory; catalogue C1.GuestRegister, C4.CheckInReport). Money flag n. ID numbers masked in every
// format; no photos. Desk page right (rule V.5): PERM-STY-001 is the matrix row of the check-in page. Server twin: GuestRegisterReport.cs.
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {deskPurposeLabel} from '../../streams/desk-fields';
import {bookingNo,checkInAtOf,checkOutAtOf,cityOfAddress,guestOfBooking,guestNameOf,idLast4Of,rangeGuard} from './shared';

const T=(key:string,label:string,mask?:RptColumn['mask']):RptColumn=>({key,label,type:'text',money:false,...(mask?{mask}:{})});
const I=(key:string,label:string):RptColumn=>({key,label,type:'int',money:false});
export const GUEST_REGISTER_COLS:RptColumn[]=[T('regNo','Registration no'),T('checkIn','Check-in'),T('guest','Guest','guestName'),T('nationality','Nationality'),T('city','City'),T('phone','Phone'),
  T('idType','ID type'),T('idNumber','ID number','idNumber'),T('room','Room'),I('adults','Adults'),I('children','Children'),T('purpose','Purpose of visit'),T('vehicle','Vehicle no'),
  T('comingFrom','Coming from'),T('goingTo','Going to'),T('checkOut','Check-out'),T('billNo','Bill no')];

export const guestRegister=registerReport({
  id:'rpt.guests.guest-register',title:'Guest register (statutory)',area:'guests',priority:'P1',status:'built',
  moneyFlag:false,viewPerms:['PERM-RPT-001'],deskPerms:['PERM-STY-001'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'status',label:'Status',type:'enum',options:['all','in house','checked out'],default:'all'}],
  columns:GUEST_REGISTER_COLS,
  dateBasis:'Stays by check-in business date (range up to 92 days); a stay still in house shows no check-out. ID numbers are masked in every format.',
  formats:['screen','xlsx','pdf'],phone:'no',legacyIds:['C1.GuestRegister','C4.CheckInReport','C4.PhotoAddressProof'],
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,92,s.date);
    const st=p.status??'all';
    const list=s.bookings.filter(b=>(b.status==='In house'||b.status==='Departed')&&b.arrival>=p.from!&&b.arrival<=p.to!&&(st==='all'||(st==='in house'?b.status==='In house':b.status==='Departed')))
      .sort((a,c)=>bookingNo(a).localeCompare(bookingNo(c)));
    const rows:RptRow[]=list.map(b=>{const g=guestOfBooking(s,b);return {kind:'row',cells:{regNo:bookingNo(b),checkIn:checkInAtOf(s,b),guest:guestNameOf(s,b),nationality:g?.nationality??'',city:cityOfAddress(g?.address),
      phone:g?.phone??b.phone??'',idType:g?.idType??'',idNumber:idLast4Of(g),room:b.room,adults:b.people,children:b.children??0,purpose:deskPurposeLabel(s,b.purpose),vehicle:b.vehicle?.no??'',
      comingFrom:b.formC?.arrivedFrom??'',goingTo:b.formC?.nextDestination??'',checkOut:checkOutAtOf(s,b),billNo:b.invoiceNo??''}};});
    rows.push({kind:'total',cells:{regNo:`${list.length} stay${list.length===1?'':'s'}`}});
    return {params:p,columns:GUEST_REGISTER_COLS,rows};
  },
});
