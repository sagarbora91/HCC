// F8-RPT-P2-A · rpt.rooms.room-wise-revenue — Room-wise and room-type revenue (catalogue A11 / A44). Nights sold and tariff earned per room, room type or floor.
// Stay-level (SF R1): every room line of the stay, all folios. Lines are attributed to the room the guest was in on the night. Money: all but nights (gate PERM-RPT-002).
import {registerReport} from '../catalogue';
import {mstFloorLabel} from '../../streams/masters';
import {T,I,M,row,plural,divRound,roomOnNight,roomColumn,isLiveNight,natural} from './common';

export const ROOMWISE_COLS=[T('name','Room or type'),I('nights','Nights sold'),M('tariff','Tariff'),M('extra','Extra person'),M('other','Other room charges'),M('total','Total'),M('avg','Average rate')];

export const roomWiseRevenue=registerReport({
  id:'rpt.rooms.room-wise-revenue',title:'Room-wise and room-type revenue',area:'rooms',priority:'P2',status:'built',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-F',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-F'},{key:'to',label:'To',type:'date',dateClass:'D-F'},{key:'groupBy',label:'Group by',type:'enum',options:['room','room type','floor'],default:'room'}],
  columns:ROOMWISE_COLS,
  dateBasis:'Room charges by the business date of the line, before GST. Nights sold = room nights charged and not reversed; tariff = room-night charges; extra person = extra person / bed lines; other room charges = meal plan lines, discounts and corrections (food, laundry and other service lines are in the service-lines report). Average rate = tariff / nights sold.',
  formats:['screen','xlsx','pdf'],phone:'summary',legacyIds:['A11','A44'],
  select:(s,_uid,p)=>{
    const by=p.groupBy??'room';
    const acc=new Map<string,{n:number;t:number;e:number;o:number}>();
    const keyOf=(room:string)=>{const r=s.rooms.find(x=>x.id===room);return by==='room'?room:by==='room type'?(r?.type??'Unassigned'):(r?mstFloorLabel(s,r):'Unassigned');};
    for(const b of s.bookings)for(const l of b.lines){
      if(l.date<p.from!||l.date>p.to!)continue;
      const c=roomColumn(l);if(!c)continue;
      const k=keyOf(roomOnNight(b,l.date)),a=acc.get(k)??{n:0,t:0,e:0,o:0};
      if(c==='tariff')a.t+=l.amount;else if(c==='extra')a.e+=l.amount;else a.o+=l.amount;
      if(isLiveNight(b,l))a.n++;
      acc.set(k,a);
    }
    const cells=(name:string,a:{n:number;t:number;e:number;o:number})=>({name,nights:a.n,tariff:a.t,extra:a.e,other:a.o,total:a.t+a.e+a.o,avg:divRound(a.t,a.n)});
    const keys=[...acc.keys()].sort(by==='floor'?(x,y)=>x.localeCompare(y):natural);
    const rows=keys.map(k=>row('row',cells(k,acc.get(k)!),k));
    const tot={n:0,t:0,e:0,o:0};for(const a of acc.values()){tot.n+=a.n;tot.t+=a.t;tot.e+=a.e;tot.o+=a.o;}
    rows.push(row('total',cells(`Total · ${plural(keys.length,by==='room'?'room':by==='floor'?'floor':'room type')}`,tot)));
    return {params:p,columns:ROOMWISE_COLS,rows};
  },
});
