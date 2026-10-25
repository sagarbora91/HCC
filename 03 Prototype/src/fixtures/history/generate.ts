// FY 2026-27 history generator (plan Phase A): bookings, folios, receipts and room allocations for 1 Apr – 23 Oct 2026.
// Deterministic (seeded PRNG, no real clock). Built as data, not by replaying commands (plan H-04); invariants are proved in
// tests/stream-history-a.mjs. BASE is never edited: the result is BASE plus history.
import {clone} from '../../domain/clone';
import type {State,Booking,Line,Receipt,Approval} from '../../domain/types';
import type {POSOrder} from '../../domain/streams/pos';
import {addDays,nights} from '../../domain/money';
import {gstForRoomNight,gstForCharge,gstCategoryOfLabel,gstForFood,gstForFolioAdjustment,gstLineTax,lineTax} from '../../domain/gst';
import {folioInvoiceNo,FOLIO_MANAGER_CREDIT_LIMIT} from '../../domain/streams/folio';
import {createRng,Rng} from './prng';
import {occupancyTarget,stayRate,isWeekend,STAY_LENGTH_BUCKETS,LEAD_TIME_BUCKETS} from './calendar';
import {addOperations} from './operations';
import {addHistoryGuests} from './guests';
import {addModuleHistory,GROUP_PLANS,DOWNTIME_REASONS} from './modules';
import type {HistoryContext} from './util';
import {POS_FIXTURE_MENU} from '../ext/pos';
import {FIRST_NAMES,LAST_NAMES,COMPANIES,STAY_SERVICES,CANCEL_REASONS} from './names';

export type HistoryOptions={seed:number;from:string;to:string};
export const HISTORY_DEFAULTS:HistoryOptions={seed:20260401,from:'2026-04-01',to:'2026-10-23'};

const TYPE_ORDER:Record<string,string[]>={Classic:['Classic','Deluxe','Suite'],Deluxe:['Deluxe','Classic','Suite'],Suite:['Suite','Deluxe','Classic']};
const GSTIN='27AAAAA0000A1Z5'; // placeholder pattern used by the other fictional company fixtures
const MAX_OPEN_RECEIVABLES=5;
const DOWNTIME_COUNT=16;
const pad=(n:number,w=4)=>String(n).padStart(w,'0');
const roundTo=(n:number,step:number)=>Math.round(n/step)*step;

type Guest={name:string;phone:string;email:string};

export function buildHistory(base:State,opts:Partial<HistoryOptions>={}):State{
  const o={...HISTORY_DEFAULTS,...opts};
  const s=clone(base);
  const rng=createRng(o.seed);
  const days=nights(o.from,o.to)+1;
  const sellable=s.rooms.filter(r=>!r.blocked);
  const byType=(t:string)=>sellable.filter(r=>r.type===t);

  // Occupancy grid per room, one cell per night starting at `from`. Existing (BASE) live bookings are reserved first so the
  // generator can never overlap them (plan §3, last paragraph).
  const grid:Record<string,Uint8Array>={};
  s.rooms.forEach(r=>{grid[r.id]=new Uint8Array(days+14);});
  const mark=(room:string,a:string,d:string)=>{const g=grid[room];if(!g)return;for(let i=Math.max(0,nights(o.from,a));i<Math.min(g.length,nights(o.from,d));i++)g[i]=1;};
  s.bookings.filter(b=>b.status==='Confirmed'||b.status==='In house'||b.status==='Held').forEach(b=>mark(b.room,b.arrival,b.departure));
  const isFree=(room:string,i:number,len:number)=>{const g=grid[room];for(let k=i;k<i+len;k++)if(g[k])return false;return true;};
  const occupiedOn=(i:number)=>sellable.reduce((n,r)=>n+grid[r.id][i],0);

  const guestPool:Guest[]=[];
  const newGuest=():Guest=>{
    if(guestPool.length>20&&rng.chance(0.1))return rng.pick(guestPool);
    const first=rng.pick(FIRST_NAMES),last=rng.pick(LAST_NAMES);
    const g={name:`${first} ${last}`,phone:`+91 9${rng.int(1000,9999)} ${rng.int(10000,99999)}`,email:`${first}.${last}${rng.int(1,99)}@example.test`.toLowerCase()};
    guestPool.push(g);return g;
  };

  const bookings:Booking[]=[],approvals:Approval[]=[];
  const ctx:HistoryContext={groups:[],downtimes:[],roomOrders:[]};
  let seq=0,rcpt=0,apr=0,openReceivables=0;
  const rid=()=>'H-RC-'+pad(++rcpt,5);
  const pay=(amount:number,method:string,date:string,kind:Receipt['kind'],actor:string,extra:Partial<Receipt>={}):Receipt=>({
    id:rid(),amount,method,date,kind,actor,reference:method==='Cash'?'CASH-'+pad(rcpt,5):method==='Bank transfer'?'NEFT-DEMO-'+pad(rcpt,5):method==='Card'?'CARD-DEMO-'+pad(rcpt,5):'UPI-DEMO-'+pad(rcpt,5),...extra});
  const tenderMethod=()=>rng.weighted([['Cash',35],['UPI',45],['Card',15],['Bank transfer',5]] as const);
  /** `days` before a date, never earlier than the start of the history window. */
  const before=(date:string,n:number)=>{const x=addDays(date,-n);return x<o.from?o.from:x;};
  const sample=(buckets:readonly (readonly [[number,number],number])[])=>{const [lo,hi]=rng.weighted(buckets);return rng.int(lo,hi);};

  function newBooking(room:{id:string;rate:number},arrival:string,departure:string,source:string,status:Booking['status'],g:Guest,rateOverride?:number):Booking{
    const corporate=source==='Corporate';
    let adj=corporate?-12:0;
    let discounted=false;
    if(status==='Departed'&&rng.chance(0.04)){adj-=rng.int(5,10);discounted=true;}
    const people=rng.weighted([[1,35],[2,45],[3,15],[4,5]] as const);
    const b:Booking={id:'HCC-H'+pad(++seq),guest:g.name,email:g.email,phone:g.phone,people,room:room.id,arrival,departure,
      rate:rateOverride??stayRate(room.rate,arrival,adj),source,status,registered:status==='Departed',lines:[],receipts:[],version:1,
      notes:'Fictional guest · FY 2026-27 history'+(discounted?' · rate discount agreed at booking':'')};
    if(source==='OTA')b.otaChannel=rng.weighted([['BookNest',45],['StayHub',35],['TripKart',20]] as const);
    if(corporate)b.company={name:rng.pick(COMPANIES),gstin:GSTIN};
    if(people>2&&rng.chance(0.4))b.children=1;
    return b;
  }
  const folioTotal=(b:Booking)=>b.lines.reduce((n,l)=>n+l.amount+lineTax(l),0);

  /** A completed stay: one room-night line per night, optional service lines, advance / deposit / settlement receipts. */
  function completeStay(b:Booking){
    for(let d=b.arrival;d<b.departure;d=addDays(d,1))b.lines.push({id:`night:${b.id}:${d}`,label:'Room night · '+d,amount:b.rate,date:d,kind:'room',taxBps:gstForRoomNight(s,d,b.rate).bps});
    const len=nights(b.arrival,b.departure);
    if(rng.chance(0.15)){
      const n=rng.chance(0.3)?2:1;
      for(let k=0;k<n;k++){const [label,lo,hi]=rng.pick(STAY_SERVICES);const amount=roundTo(rng.int(lo,hi),500),date=addDays(b.arrival,rng.int(0,len-1));
        b.lines.push({id:`svc:${b.id}:${k}`,label,amount,date,kind:'service',taxBps:gstForCharge(s,date,gstCategoryOfLabel(label)).bps});}
    }
    if(rng.chance(0.12)){ // room service ordered through the POS and posted to the folio (module 23)
      const menu=POS_FIXTURE_MENU.filter(m=>m.outlets.includes('room-service')),oid='POS-H'+pad(ctx.roomOrders.length+1,5);
      const picks=[...new Set([rng.pick(menu),rng.pick(menu),rng.pick(menu)].slice(0,rng.int(1,3)))];
      const date=addDays(b.arrival,rng.int(0,len-1)),hh=rng.pick(['08:10','13:20','20:30','21:40']);
      const lines=picks.map((m,i)=>({id:oid+'-L'+(i+1),item:m.id,name:m.name,price:m.price,qty:rng.int(1,2),kot:oid+'-K1'}));
      const net=lines.reduce((a,l)=>a+l.price*l.qty,0),taxBps=gstForFood(s,date).bps,tax=gstLineTax(net,taxBps);
      const line:Line={id:'posline:'+oid,label:'Room service',amount:net,date,kind:'service',taxBps};b.lines.push(line);
      const order:POSOrder={id:oid,outlet:'room-service',room:b.room,booking:b.id,guest:b.guest,covers:1,date,openedAt:date+'T'+hh,openedBy:'reception',status:'Settled',version:4,lines,
        kots:[{id:oid+'-K1',no:1,at:date+'T'+hh,by:'reception',status:'Served',readyAt:date+'T'+hh,servedAt:date+'T'+hh}],
        settlement:{mode:'folio',billNo:'',net,taxBps,tax,total:net+tax,date,at:date+'T'+hh,by:'reception',booking:b.id,folioLineId:line.id}};
      ctx.roomOrders.push({order,line});
    }
    if(rng.chance(0.03)){ // a discount; above the manager limit it carries an approval record
      const net=b.lines.reduce((n,l)=>n+l.amount,0),amt=Math.max(5000,roundTo(net*(0.05+rng.next()*0.1),5000));
      const reason=rng.pick(['Service recovery','Loyalty guest','Late check-in goodwill','Corporate rate match']);
      const line:Line={id:`disc:${b.id}`,label:'Discount · '+reason,amount:-amt,date:addDays(b.departure,-1),kind:'discount',taxBps:gstForFolioAdjustment(s,b,addDays(b.departure,-1)).bps};
      if(amt>s.settings.discountLimit){
        const aid='APR-H'+pad(++apr,3);line.label='Approved discount · '+reason;line.approvalId=aid;
        approvals.push({id:aid,booking:b.id,amount:amt,reason,status:'Approved',actor:'reception',approver:'manager',decidedAt:b.departure+'T09:00',version:b.version});
      }
      b.lines.push(line);
    }
    const total=folioTotal(b),actor=()=>rng.chance(0.8)?'cashier':'reception';
    const stayDeposit=rng.chance(0.25);
    if(stayDeposit){const m=rng.chance(0.6)?'Cash':'UPI',amt=rng.chance(0.7)?100000:200000;
      b.receipts.push(pay(amt,m,b.arrival,'deposit',actor()));b.receipts.push(pay(amt,m,b.departure,'deposit-return',actor()));}
    let remaining=total;
    if(rng.chance(0.4)){
      const adv=Math.min(total,Math.max(10000,roundTo(total*(0.25+rng.next()*0.25),10000)));
      b.receipts.push(pay(adv,rng.weighted([['UPI',55],['Card',25],['Cash',20]] as const),before(b.arrival,sample(LEAD_TIME_BUCKETS)),'payment',actor()));
      remaining-=adv;
    }
    if(remaining>0){
      const credit=b.company&&b.departure>='2026-10-05'&&total<=FOLIO_MANAGER_CREDIT_LIMIT&&openReceivables<MAX_OPEN_RECEIVABLES&&rng.chance(0.6);
      if(credit){
        openReceivables++;const aid='APR-H'+pad(++apr,3);
        approvals.push({id:aid,booking:b.id,amount:remaining,reason:'Company credit · '+b.company!.name,status:'Executed',actor:'reception',approver:'manager',decidedAt:b.departure+'T09:30',version:b.version,kind:'company-credit',
          meta:{command:'APPROVAL',company:b.company!.name,gstin:b.company!.gstin,executedBy:'reception',executedAt:b.departure+'T10:30'}});
        b.receipts.push(pay(remaining,'Company credit',b.departure,'company-credit','reception',{reference:`${b.company!.name} · ${aid}`,approvalId:aid}));
      }else if(b.company&&rng.chance(0.5)){
        // Billed corporate stay settled later by bank transfer.
        const due=addDays(b.departure,rng.int(3,12));
        b.receipts.push(pay(remaining,'Bank transfer',due>o.to?b.departure:due,'payment','cashier'));
      }else if(rng.chance(0.12)&&remaining>=20000){
        const first=roundTo(remaining*(0.3+rng.next()*0.4),10000)||10000,tg='H-TG-'+pad(rcpt+1,5);
        b.receipts.push(pay(first,tenderMethod(),b.departure,'payment',actor(),{tenderGroup:tg}));
        b.receipts.push(pay(remaining-first,tenderMethod(),b.departure,'payment',actor(),{tenderGroup:tg}));
      }else b.receipts.push(pay(remaining,tenderMethod(),b.departure,'payment',actor()));
    }
    b.status='Departed';b.registered=true;b.version=4;
    if(len>=2&&rng.chance(0.02))b.notes+=' · left a night early; unused night not charged';
    else if(rng.chance(0.03))b.notes+=' · stay extended by one night at the desk';
  }

  /** Cancellation (60 % with a refunded advance) or no-show (charged one night when an advance exists). */
  function lapsed(b:Booking,noShow:boolean){
    b.status=noShow?'No show':'Cancelled';b.version=2;b.registered=false;
    const hasAdvance=rng.chance(noShow?0.6:0.35);
    if(noShow){
      b.notes='No-show · FY 2026-27 history';
      if(hasAdvance){
        const l:Line={id:`noshow:${b.id}`,label:'No-show charge · 1 night',amount:b.rate,date:b.arrival,kind:'service',taxBps:gstForRoomNight(s,b.arrival,b.rate).bps};
        b.lines.push(l);b.receipts.push(pay(folioTotal(b),rng.weighted([['UPI',60],['Card',25],['Cash',15]] as const),before(b.arrival,rng.int(1,5)),'payment','cashier'));
      }
    }else{
      b.cancelReason=rng.pick(CANCEL_REASONS);b.notes='Cancelled · '+b.cancelReason;
      if(hasAdvance){
        const amt=roundTo(b.rate*(0.5+rng.next()*0.5),10000),m=rng.weighted([['UPI',60],['Card',25],['Cash',15]] as const);
        const payDate=before(b.arrival,rng.int(3,14));
        const p=pay(amt,m,payDate,'payment','cashier');b.receipts.push(p);
        const rd=addDays(b.arrival,-rng.int(0,2));
        b.receipts.push(pay(amt,m,rd<payDate?payDate:rd,'refund','cashier',{refundOf:p.id}));
      }
    }
  }

  // ── Pre-pass: reserve rooms for events that must happen on specific nights (module 14 group blocks, module 15 downtime) ──
  for(const g of GROUP_PLANS){
    const members:{booking:string;type:string}[]=[],allot:Record<string,number>={},released:Record<string,number>={};
    const co=g.company?s.groupsX?.companies.find(c=>c.id===g.company):undefined;
    for(const [type,want,picked] of g.lines){
      const rack=s.rooms.find(r=>r.type===type)!.rate,rate=stayRate(rack,g.arrival,-5);let got=0;
      const i=nights(o.from,g.arrival),len=nights(g.arrival,g.departure);
      for(const room of byType(type)){
        if(got>=picked)break;if(!isFree(room.id,i,len))continue;
        mark(room.id,g.arrival,g.departure);
        const b=newBooking(room,g.arrival,g.departure,co?'Corporate':rng.pick(['Direct','Phone'] as const),'Departed',newGuest(),rate);
        if(co){b.company={name:co.name,gstin:co.gstin};}
        b.notes+=' · Group block '+g.id;completeStay(b);bookings.push(b);members.push({booking:b.id,type});got++;
      }
      allot[type]=got;released[type]=want-got;
    }
    const rates:Record<string,number>={};for(const [type] of g.lines)rates[type]=stayRate(s.rooms.find(r=>r.type===type)!.rate,g.arrival,-5);
    ctx.groups.push({id:g.id,name:g.name,company:g.company,billTo:co?'company':'guest',arrival:g.arrival,departure:g.departure,cutoff:addDays(g.arrival,-7),allot,rates,released,members,
      status:'Released',releasedBy:'auto',releasedAt:addDays(g.arrival,-6)+'T08:00',contact:g.contact,notes:g.notes,createdBy:'manager',createdAt:addDays(g.arrival,-30)+'T11:00'});
  }
  for(let k=0;k<DOWNTIME_COUNT;k++){
    const date=addDays(o.from,rng.int(5,days-10)),len=rng.int(1,3),room=rng.pick(sellable),i=nights(o.from,date);
    if(!isFree(room.id,i,len))continue;
    mark(room.id,date,addDays(date,len));
    ctx.downtimes.push({room:room.id,from:date,to:addDays(date,len),reason:rng.pick(DOWNTIME_REASONS)});
  }

  const lastArrival=addDays(o.to,-1);
  for(let d=o.from,i=0;d<=lastArrival;d=addDays(d,1),i++){
    const weekend=isWeekend(d);
    const target=Math.max(0,Math.min(sellable.length,Math.round(sellable.length*(occupancyTarget(d)+(rng.next()-0.5)*0.1))));
    const need=target-occupiedOn(i);
    const maxLen=nights(d,o.to);
    for(let k=0;k<need;k++){
      let len=Math.min(sample(STAY_LENGTH_BUCKETS),maxLen);
      const source=rng.weighted([['Direct',20],['Phone',15],['OTA',30],['Corporate',weekend?8:28],['Walk-in',10],['Website',5]] as const);
      const wanted=rng.weighted([['Classic',46],['Deluxe',40],['Suite',14]] as const);
      let room:typeof sellable[number]|undefined;
      for(;len>=1&&!room;len--){
        for(const t of TYPE_ORDER[wanted]){
          const free=byType(t).filter(r=>isFree(r.id,i,len));
          if(free.length){room=rng.pick(free);break;}
        }
        if(room)break;
      }
      if(!room)continue;
      mark(room.id,d,addDays(d,len));
      const b=newBooking(room,d,addDays(d,len),source,'Departed',newGuest());
      completeStay(b);bookings.push(b);
      if(len>=2&&rng.chance(0.012)){ // room move: second room is reserved for the whole stay, the first only until the move
        const others=sellable.filter(r=>r.id!==room!.id&&isFree(r.id,i,len));
        if(others.length){
          const second=rng.pick(others),k=rng.int(1,len-1),mid=addDays(d,k);
          mark(second.id,d,addDays(d,len));for(let x=i+k;x<i+len;x++)grid[room.id][x]=0;
          b.segments=[{room:room.id,from:d,to:mid},{room:second.id,from:mid,to:b.departure}];
          b.room=second.id;b.version++;
          ctx.roomOrders.filter(r=>r.order.booking===b.id).forEach(r=>{r.order.room=r.order.date<mid?room.id:second.id;});b.notes+=` · moved from room ${room.id} to ${second.id} on ${mid} (guest request)`;
        }
      }
      if(rng.chance(0.065)){ // a cancelled booking for a nearby date; cancelled stays hold no room
        const ca=addDays(d,rng.int(0,3)),cl=Math.min(sample(STAY_LENGTH_BUCKETS),Math.max(1,nights(ca,o.to)));
        if(ca<=lastArrival){const cr=rng.pick(sellable);const c=newBooking(cr,ca,addDays(ca,cl),rng.weighted([['Direct',30],['Phone',20],['OTA',40],['Website',10]] as const),'Confirmed',newGuest());lapsed(c,false);bookings.push(c);}
      }
      if(rng.chance(0.02)){ // a no-show on a room that stayed empty that night
        const empty=sellable.filter(r=>isFree(r.id,i,1));
        if(empty.length){const c=newBooking(rng.pick(empty),d,addDays(d,1),rng.weighted([['Direct',30],['OTA',50],['Phone',20]] as const),'Confirmed',newGuest());lapsed(c,true);bookings.push(c);}
      }
    }
  }

  // Sample invoice numbers allocated in departure order, as the CHECKOUT command would (Financial Rulebook §5).
  const departed=bookings.filter(b=>b.status==='Departed').sort((a,b)=>a.departure<b.departure?-1:a.departure>b.departure?1:a.id<b.id?-1:1);
  departed.forEach((b,n)=>{b.invoiceNo=folioInvoiceNo((s.invoiceSeqC??0)+n+1);});
  s.invoiceSeqC=(s.invoiceSeqC??0)+departed.length;
  addHistoryGuests(s,bookings,o.seed);
  s.bookings=[...bookings,...s.bookings];
  s.approvals=[...approvals,...s.approvals];
  addOperations(s,rng,o);
  addModuleHistory(s,rng,o,ctx);
  return s;
}
