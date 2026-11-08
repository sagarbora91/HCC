// F8-RPT-P2-A · rpt.guests.guest-tariff — Guest tariff detail (catalogue A42; LF B1). What a guest or company paid per stay and whether an agreed rate applied.
// D-F range by arrival date; stays that are in house or departed. Money: every rate and amount (gate PERM-RPT-002). Stay-level sums (SF R1): every line of the stay, all folios.
import {registerReport} from '../catalogue';
import {rateFor} from '../../selectors';
import {guestById} from '../../streams/guests';
import {HccError} from '../../errors';
import {bookingNo,guestNameOf} from '../guests/shared';
import {T,M,row,plural,natural} from './common';

export const TARIFF_COLS=[T('stay','Stay'),T('room','Room'),T('roomType','Room type'),T('plan','Plan'),M('standard','Standard rate'),T('rateSource','Rate source'),M('agreed','Agreed rate'),
  M('extras','Extra charges'),M('discount','Discount'),M('netTariff','Net tariff')];

export const guestTariff=registerReport({
  id:'rpt.guests.guest-tariff',title:'Guest tariff detail',area:'guests',priority:'P2',status:'built',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-F',
  params:[{key:'party',label:'Guest or company',type:'text',required:true},{key:'from',label:'From',type:'date',dateClass:'D-F'},{key:'to',label:'To',type:'date',dateClass:'D-F'}],
  columns:TARIFF_COLS,
  dateBasis:'Stays that are in house or departed, by arrival date; the guest or company is matched on the name (part of it is enough). Standard rate = the tariff card rate on arrival; rate source = agreed (the guest\'s agreed rate was used), manual (the rate differs from the standard rate) or standard. Net tariff = room-night charges less discount, before GST; extra charges = every other line, before GST.',
  formats:['screen','xlsx','pdf'],phone:'yes',legacyIds:['A42'],
  select:(s,_uid,p)=>{
    const q=String(p.party??'').trim().toLowerCase();
    if(q.length<2)throw new HccError('VALIDATION','Type at least two letters of the guest or company name.');
    const list=s.bookings.filter(b=>(b.status==='In house'||b.status==='Departed')&&b.arrival>=p.from!&&b.arrival<=p.to!
      &&[guestNameOf(s,b),b.company?.name??'',guestById(s,b.guestId)?.company??''].some(x=>x.toLowerCase().includes(q)))
      .sort((a,b)=>b.arrival.localeCompare(a.arrival)||natural(b.id,a.id));
    let ex=0,di=0,nt=0;
    const rows=list.map(b=>{
      const room=s.rooms.find(r=>r.id===b.room),std=room?rateFor(s,room,b.arrival):b.rate;
      const agreed=guestById(s,b.guestId)?.agreedRates?.find(a=>a.type===room?.type)?.rate??null;
      const tariff=b.lines.filter(l=>l.kind==='room').reduce((a,l)=>a+l.amount,0),discount=-b.lines.filter(l=>l.kind==='discount').reduce((a,l)=>a+l.amount,0);
      const extras=b.lines.filter(l=>l.kind!=='room'&&l.kind!=='discount').reduce((a,l)=>a+l.amount,0),net=tariff-discount;
      ex+=extras;di+=discount;nt+=net;
      return row('row',{stay:bookingNo(b),room:b.room,roomType:room?.type??'',plan:b.plan??'',standard:std,rateSource:b.rateSource==='agreed'?'agreed':b.rate!==std?'manual':'standard',agreed,extras,discount,netTariff:net});
    });
    rows.push(row('total',{stay:`Total · ${plural(list.length,'stay')}`,extras:ex,discount:di,netTariff:nt}));
    return {params:p,columns:TARIFF_COLS,rows};
  },
});
