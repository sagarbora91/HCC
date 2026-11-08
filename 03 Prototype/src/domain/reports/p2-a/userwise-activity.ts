// F8-RPT-P2-A · rpt.guests.userwise-activity — User-wise check-in and check-out (catalogue A33). Source: the audit trail (CHECKIN / CHECKOUT entries).
// The date range binds BOTH actions (the old report's check-out branch ignored it). No money. Permission PERM-ADM-004.
import type {State,Booking} from '../../types';
import {registerReport} from '../catalogue';
import {addMinutes} from '../../money';
import {bookingNo,guestNameOf,userLabel} from '../guests/shared';
import {T,row,plural,rangeGuard} from './common';

export const USERWISE_COLS=[T('user','User'),T('at','Date and time'),T('action','Action'),T('booking','Booking'),T('room','Room'),T('guest','Guest','guestName'),T('billNo','Bill no')];

function bookingOf(s:State,a:{action:string;at:string;detail:string}):Booking|undefined{
  if(a.action==='CHECKOUT')return s.bookings.find(b=>a.detail===b.id||a.detail.startsWith(b.id+' '));
  return s.bookings.find(b=>!!b.checkedInAt&&addMinutes(b.checkedInAt,1)===a.at&&a.detail.startsWith(b.guest+' →'))
    ??s.bookings.find(b=>(b.status==='In house'||b.status==='Departed')&&a.detail===`${b.guest} → room ${b.room}`);
}

export const userwiseActivity=registerReport({
  id:'rpt.guests.userwise-activity',title:'User-wise check-in and check-out',area:'guests',priority:'P2',status:'built',
  moneyFlag:false,viewPerms:['PERM-ADM-004'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'user',label:'User',type:'user'},
    {key:'action',label:'Action',type:'enum',options:['both','check-in','check-out'],default:'both'}],
  columns:USERWISE_COLS,
  dateBasis:'Check-in and check-out entries of the audit trail by the business date they were made on (range up to 92 days); the range applies to both actions.',
  formats:['screen','xlsx','pdf'],phone:'no',legacyIds:['A33'],
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,92,s.date);
    const want=p.action??'both',who=String(p.user??'');
    const items=s.audit.filter(a=>(a.action==='CHECKIN'&&want!=='check-out')||(a.action==='CHECKOUT'&&want!=='check-in'))
      .filter(a=>a.at.slice(0,10)>=p.from!&&a.at.slice(0,10)<=p.to!)
      .filter(a=>!who||s.users.find(u=>u.id===who)?.name===a.actor)
      .sort((a,b)=>userLabel(s,a.actor).localeCompare(userLabel(s,b.actor))||a.at.localeCompare(b.at)||a.id.localeCompare(b.id));
    const name=(a:{actor:string})=>a.actor.split(' · ')[0];
    const rows=[];let cur='',ci=0,co=0,ti=0,to=0;
    const txt=(i:number,o:number)=>`${plural(i,'check-in')} · ${plural(o,'check-out')}`;
    const flush=()=>{if(cur)rows.push(row('subtotal',{user:cur,booking:txt(ci,co)},cur));};
    for(const a of items){
      const u=name(a);if(u!==cur){flush();cur=u;ci=co=0;}
      const b=bookingOf(s,a),isIn=a.action==='CHECKIN';
      rows.push(row('row',{user:u,at:a.at.replace('T',' '),action:isIn?'Check-in':'Check-out',booking:b?bookingNo(b):'',room:b?.room??'',guest:b?guestNameOf(s,b):'',billNo:b?.invoiceNo??''},u));
      if(isIn){ci++;ti++;}else{co++;to++;}
    }
    flush();
    rows.push(row('total',{user:'Total',booking:txt(ti,to)}));
    return {params:p,columns:USERWISE_COLS,rows};
  },
});
