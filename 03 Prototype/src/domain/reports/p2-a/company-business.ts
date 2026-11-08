// F8-RPT-P2-A · rpt.guests.company-business — Business from company (catalogue A12). Room-nights and billed amount per company, for stays billed to the company.
// SF-CONTRACT R8: on a split stay only the lines of the company's own folio count; on a single-folio stay the whole stay when it is billed to the company account.
import type {Booking,Line,State} from '../../types';
import {registerReport} from '../catalogue';
import {grpSlice} from '../../streams/groups';
import {sfIsSplit,sfFolios,sfView} from '../../streams/folio-split';
import {guestNameOf} from '../guests/shared';
import {T,I,M,row,plural,isLiveNight} from './common';

export const COMPANY_COLS=[T('company','Company'),I('guests','Guests'),I('roomNights','Room-nights'),M('tariff','Tariff'),M('other','Other charges'),M('total','Total business')];

/** Lines of a stay that belong to a company (R8). Until sf-domain merges sfCompanyLines this calls sfView / sfFolios directly. */
export function companyLinesOf(b:Booking,companyId:string):Line[]{
  if(sfIsSplit(b)){
    return sfFolios(b).filter(f=>f.payer.kind==='company'&&f.payer.ref===companyId).flatMap(f=>sfView(b,f.id).lines);
  }
  return b.company?.account===companyId?b.lines:[];
}
const guestKey=(s:State,b:Booking)=>b.guestId??guestNameOf(s,b);

export const companyBusiness=registerReport({
  id:'rpt.guests.company-business',title:'Business from company',area:'guests',priority:'P2',status:'built',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-F',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-F'},{key:'to',label:'To',type:'date',dateClass:'D-F'},{key:'company',label:'Company',type:'text'}],
  columns:COMPANY_COLS,
  dateBasis:'Charges posted in the range (business date of the line) on stays billed to a company account; on a split stay only the company\'s own folio. Tariff = room-night charges, other charges = every other line (discounts and corrections included), both before GST; total business = tariff + other charges. Room-nights = room nights charged and not reversed.',
  formats:['screen','xlsx','pdf'],phone:'summary',legacyIds:['A12'],
  select:(s,_uid,p)=>{
    const q=String(p.company??'').trim().toLowerCase();
    const rows=[];let tg=0,tn=0,tt=0,to=0;
    for(const c of [...grpSlice(s).companies].sort((a,b)=>a.name.localeCompare(b.name))){
      if(q&&!c.name.toLowerCase().includes(q)&&c.id.toLowerCase()!==q)continue;
      const guests=new Set<string>();let nights=0,tariff=0,other=0;
      for(const b of s.bookings){
        const lines=companyLinesOf(b,c.id).filter(l=>l.date>=p.from!&&l.date<=p.to!);
        if(!lines.length)continue;
        guests.add(guestKey(s,b));
        for(const l of lines){
          if(l.kind==='room')tariff+=l.amount;else other+=l.amount;
          if(isLiveNight(b,l))nights++;
        }
      }
      if(!guests.size)continue;
      rows.push(row('row',{company:c.name,guests:guests.size,roomNights:nights,tariff,other,total:tariff+other},c.id));
      tg+=guests.size;tn+=nights;tt+=tariff;to+=other;
    }
    rows.push(row('total',{company:`Total · ${plural(rows.length,'company','companies')}`,guests:tg,roomNights:tn,tariff:tt,other:to,total:tt+to}));
    return {params:p,columns:COMPANY_COLS,rows};
  },
});
