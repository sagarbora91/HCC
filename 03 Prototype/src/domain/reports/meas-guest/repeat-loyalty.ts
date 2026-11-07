// F12-RPT-GUEST · rpt.guests.repeat-loyalty — Repeat guests and loyalty base (customers, stays, messaging outbox; loyalty tag from meas-cap-guest). Switch meas.guest.repeat.
// Money: the revenue-share column (share of the month's stay revenue from guests seen before) is flagged y and sits behind PERM-RPT-002.
// The 3+ stay guest list shows the name (masked for roles without PERM-RES-001, rule V); last stay and mobile are not shown (the entry's column is one text column).
import type {State} from '../../types';
import {registerReport} from '../catalogue';
import {customerIndex,type Customer} from '../../streams/customers';
import {totals} from '../../selectors';
import {msgSlice} from '../../streams/messaging';
import {T,I,P,B,pct1,prevMonth,capNote} from './shared';

export const REPEAT_COLS=[T('month','Month'),I('seenBefore','Customers seen before'),P('repeatShare','Repeat share'),I('stays','Stays'),P('revenueShare','Revenue share',true),T('guests3','3+ stay guests','guestName'),T('loyaltyTag','Loyalty tag'),B('offerSent','Offer sent')];
const BASIS='Customers (by mobile) with a stay that started in the month. Seen before = a completed stay earlier than their first one in the month. Repeat share = seen before / customers in the month; revenue share = folio totals of those customers’ stays / all stays of the month. The list shows guests with three or more completed stays.';
const NEED='cust.loyaltyTag (meas-cap-guest)';

const stayIn=(c:Customer,month:string)=>c.completed.filter(b=>b.arrival.slice(0,7)===month);
export type RepeatFigures={customers:number;seenBefore:number;share:number|null;stays:number;revenueShare:number|null};
export function repeatFigures(s:State,month:string):RepeatFigures{
  const seen=customerIndex(s).filter(c=>stayIn(c,month).length);
  let before=0,stays=0,rev=0,revRepeat=0;
  for(const c of seen){
    const inMonth=stayIn(c,month),first=inMonth.map(b=>b.arrival).sort()[0];
    const repeat=c.completed.some(b=>b.arrival<first);
    const r=inMonth.reduce((a,b)=>a+totals(b).total,0);
    stays+=inMonth.length;rev+=r;if(repeat){before++;revRepeat+=r;}
  }
  return {customers:seen.length,seenBefore:before,share:pct1(before,seen.length),stays,revenueShare:pct1(revRepeat,rev)};
}
/** Exception rule (red row): repeat share falls two months running (month < previous < the one before). */
export function repeatFalling(s:State,month:string){
  const m1=prevMonth(month),m2=prevMonth(m1),a=repeatFigures(s,month).share,b=repeatFigures(s,m1).share,c=repeatFigures(s,m2).share;
  return {share:a,prev:b,prev2:c,red:a!==null&&b!==null&&c!==null&&a<b&&b<c};
}
export const offerSentTo=(s:State,key:string)=>(msgSlice(s).outbox??[]).some(m=>m.guestKey===key&&m.template==='manual'&&!m.blocked);

export const repeatLoyalty=registerReport({
  id:'rpt.guests.repeat-loyalty',title:'Repeat guests and loyalty base',area:'guests',priority:'P2',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-001'],dateClass:'D-M',
  params:[{key:'month',label:'Month',type:'month',dateClass:'D-M'}],
  columns:REPEAT_COLS,dateBasis:BASIS,formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{
    const month=p.month as string,f=repeatFigures(s,month);
    if(!f.customers)return {params:p,columns:REPEAT_COLS,rows:[],about:{dateBasis:capNote(BASIS,NEED)}};
    // MEAS2-B (M1): the summary row is red when the repeat share falls two months running.
    const rows=[{kind:'row' as const,...(repeatFalling(s,month).red?{group:'red'}:{}),cells:{month,seenBefore:f.seenBefore,repeatShare:f.share,stays:f.stays,revenueShare:f.revenueShare,guests3:'',loyaltyTag:'',offerSent:null}}];
    const loyal=customerIndex(s).filter(c=>stayIn(c,month).length&&c.completed.length>=3).sort((a,b)=>a.name.localeCompare(b.name)||a.key.localeCompare(b.key));
    for(const c of loyal)rows.push({kind:'row',cells:{month:'',seenBefore:null,repeatShare:null,stays:c.completed.length,revenueShare:null,guests3:c.name,loyaltyTag:s.measX?.preference?.[c.key]?.loyaltyTag??'',offerSent:offerSentTo(s,c.key)}} as never);
    return {params:p,columns:REPEAT_COLS,rows};
  },
});
