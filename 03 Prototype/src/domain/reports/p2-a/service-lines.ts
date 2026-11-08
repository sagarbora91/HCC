// F8-RPT-P2-A · rpt.guests.service-lines — Room service and extras lines (catalogue A5 / A6 / A43). Food, laundry and other service lines posted to guest folios.
// Stay-level (SF R1): every line of the stay, all folios. Money: rate, amount, tax (gate PERM-RPT-002).
import {registerReport} from '../catalogue';
import {lineTax} from '../../gst';
import {guestNameOf} from '../guests/shared';
import {T,I,D,M,row,plural,rangeGuard,roomOnNight,isServiceLine,serviceGroup,natural,type ServiceGroup} from './common';

export const SERVICE_COLS=[D('date','Date'),T('room','Room'),T('guest','Guest','guestName'),T('item','Item'),T('group','Group'),I('qty','Qty'),M('rate','Rate'),M('amount','Amount'),M('tax','Tax'),
  T('source','Source'),T('billRef','Bill ref')];
const LABEL:Record<ServiceGroup,string>={food:'Food',laundry:'Laundry',other:'Other',extras:'Extras'};

export const serviceLines=registerReport({
  id:'rpt.guests.service-lines',title:'Room service and extras lines',area:'guests',priority:'P2',status:'built',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'group',label:'Group',type:'enum',options:['all','food','laundry','other','extras'],default:'all'},{key:'item',label:'Item',type:'text'}],
  columns:SERVICE_COLS,
  dateBasis:'Service lines by the business date they were posted (range up to 92 days); lines that were later reversed are left out. Group: catalogue extras = extras, restaurant bills = food, otherwise by the words of the item (laundry, food words, the rest = other). Amount and tax are in paise-exact rupees before/of GST.',
  formats:['screen','xlsx','pdf','csv'],phone:'no',legacyIds:['A5','A6','A43'],
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,92,s.date);
    const g=p.group??'all',q=String(p.item??'').trim().toLowerCase();
    const items=s.bookings.flatMap(b=>b.lines.filter(l=>l.date>=p.from!&&l.date<=p.to!&&isServiceLine(b,l)).map(l=>({b,l,grp:serviceGroup(l)})))
      .filter(x=>(g==='all'||x.grp===g)&&(!q||x.l.label.toLowerCase().includes(q)))
      .sort((a,c)=>a.l.date.localeCompare(c.l.date)||natural(roomOnNight(a.b,a.l.date),roomOnNight(c.b,c.l.date))||a.l.id.localeCompare(c.l.id));
    const rows=items.map(({b,l,grp})=>row('row',{date:l.date,room:roomOnNight(b,l.date),guest:guestNameOf(s,b),item:l.label,group:LABEL[grp],qty:1,rate:l.amount,amount:l.amount,tax:lineTax(l),
      source:/ · bill /.test(l.label)?'room-service POS':'folio',billRef:b.invoiceNo??''}));
    for(const k of ['food','laundry','other','extras'] as ServiceGroup[]){
      const xs=items.filter(x=>x.grp===k);if(!xs.length)continue;
      rows.push(row('subtotal',{item:`${LABEL[k]} · ${plural(xs.length,'line')}`,qty:xs.length,amount:xs.reduce((a,x)=>a+x.l.amount,0),tax:xs.reduce((a,x)=>a+lineTax(x.l),0)},LABEL[k]));
    }
    rows.push(row('total',{item:`Total · ${plural(items.length,'line')}`,qty:items.length,amount:items.reduce((a,x)=>a+x.l.amount,0),tax:items.reduce((a,x)=>a+lineTax(x.l),0)}));
    return {params:p,columns:SERVICE_COLS,rows};
  },
});
