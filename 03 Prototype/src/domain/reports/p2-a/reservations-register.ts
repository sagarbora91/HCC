// F8-RPT-P2-A · rpt.guests.reservations-register — Reservations register (catalogue A37 purpose). D-FWD range by arrival date, search and export of all bookings.
// Money: advance (deposits received on the stay) and quoted total (rate x nights + room GST), gate PERM-RPT-002. Server twin: Hcc.Reporting/Reports/P2A/ReservationsRegisterReport.cs.
import type {Booking} from '../../types';
import {registerReport} from '../catalogue';
import {gstStayTax} from '../../gst';
import {nights} from '../../money';
import {guestById} from '../../streams/guests';
import {bookingNo,guestNameOf} from '../guests/shared';
import {T,I,D,M,row,plural,rangeGuard,natural} from './common';

export const RESERVATION_STATUS=['all','held','confirmed','checked in','checked out','cancelled','no-show','expired'];
export const RESERVATION_COLS=[T('booking','Booking'),D('created','Created'),T('guest','Guest','guestName'),D('arrival','Arrival'),D('departure','Departure'),T('roomType','Room type'),I('rooms','Rooms'),
  T('plan','Plan'),T('source','Source'),T('status','Status'),M('advance','Advance'),M('quoted','Quoted total')];
const STATUS:Record<Booking['status'],string>={Held:'held',Confirmed:'confirmed','In house':'checked in',Departed:'checked out',Cancelled:'cancelled','No show':'no-show',Expired:'expired'};
const cap=(t:string)=>t.charAt(0).toUpperCase()+t.slice(1);

export const reservationsRegister=registerReport({
  id:'rpt.guests.reservations-register',title:'Reservations register',area:'guests',priority:'P2',status:'built',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-001'],dateClass:'D-FWD',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-FWD'},{key:'to',label:'To',type:'date',dateClass:'D-FWD'},{key:'status',label:'Status',type:'enum',options:RESERVATION_STATUS,default:'all'},
    {key:'source',label:'Source',type:'text'},{key:'search',label:'Search',type:'text'}],
  columns:RESERVATION_COLS,
  dateBasis:'Bookings by arrival date (range up to 92 days, up to 90 days ahead). Advance = deposits received on the booking. Quoted total = nightly rate x nights plus room GST at the rates in force on each night. Created = the date the booking was entered (blank when not recorded).',
  formats:['screen','xlsx','pdf','csv'],phone:'yes',legacyIds:['A37'],
  select:(s,_uid,p)=>{
    const from=p.from!,to=p.to!;rangeGuard(from,to,92,s.date,90);
    const st=p.status??'all',src=String(p.source??'').trim().toLowerCase(),q=String(p.search??'').trim().toLowerCase();
    const made=new Map<string,string>();for(const a of s.audit)if(a.action==='BOOK'){const id=a.detail.split(' · ')[0];if(!made.has(id)||a.at<made.get(id)!)made.set(id,a.at);}
    const list=s.bookings.filter(b=>b.arrival>=from&&b.arrival<=to&&(st==='all'||STATUS[b.status]===st)&&(!src||b.source.toLowerCase()===src)&&(!q||[b.id,bookingNo(b),guestNameOf(s,b),b.phone??'',guestById(s,b.guestId)?.phone??''].some(x=>x.toLowerCase().includes(q))))
      .sort((a,b)=>a.arrival.localeCompare(b.arrival)||natural(a.room,b.room)||a.id.localeCompare(b.id));
    const rows=list.map(b=>row('row',{booking:bookingNo(b),created:made.get(b.id)?.slice(0,10)??null,guest:guestNameOf(s,b),arrival:b.arrival,departure:b.departure,roomType:s.rooms.find(r=>r.id===b.room)?.type??'',rooms:1,
      plan:b.plan??'',source:b.source,status:cap(STATUS[b.status]),advance:b.receipts.filter(r=>r.kind==='deposit').reduce((a,r)=>a+r.amount,0),
      quoted:b.rate*nights(b.arrival,b.departure)+gstStayTax(s,b.rate,b.arrival,b.departure)}));
    const count=(x:string)=>list.filter(b=>STATUS[b.status]===x).length;
    const mix=['held','confirmed','checked in','checked out','cancelled','no-show','expired'].filter(x=>count(x)).map(x=>`${cap(x)} ${count(x)}`).join(' · ');
    rows.push(row('total',{booking:`Total · ${plural(list.length,'booking')}`,rooms:list.length,status:mix,advance:rows.reduce((a,r)=>a+(r.cells.advance as number),0),quoted:rows.reduce((a,r)=>a+(r.cells.quoted as number),0)}));
    return {params:p,columns:RESERVATION_COLS,rows};
  },
});
