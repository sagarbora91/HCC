// FY history Phase C (part 1): module 13 yield, 14 groups, 15 maintenance, 16 linen & stock history on top of the generated stays.
// Everything is derived from, and consistent with, the Phase A/B data: group members are real bookings, downtime nights have no stay,
// consumption follows occupied room-nights, laundry batches reconcile with losses. The entry point also runs the people and sales parts.
import type {State} from '../../domain/types';
import {addDays,nights} from '../../domain/money';
import type {MNTWorkOrder,MNTHistory,MNTPriority,MNTAsset} from '../../domain/streams/maintenance';
import {MNT_PRIORITY} from '../../domain/streams/maintenance';
import type {INVMove,INVBatch,INVLoss,INVCount,INVGrn} from '../../domain/streams/inventory';
import type {YLDPublish,YLDSeason,YLDHistory} from '../../domain/streams/yield';
import type {Rng} from './prng';
import {pad,roundTo,FIRST_OF,type HistoryContext,type HistoryWindow} from './util';
import {stayRate,festivalOn} from './calendar';
import {addPeopleHistory} from './modules-people';
import {addSalesHistory} from './modules-sales';

export type {HistoryContext,HistoryWindow};

export const GROUP_PLANS:{id:string;name:string;company?:string;arrival:string;departure:string;contact:string;notes:string;lines:[string,number,number][]}[]=[
  {id:'GB-H1',name:'Kulkarni–Joshi wedding party',arrival:'2026-05-15',departure:'2026-05-17',contact:'Mrs. Kulkarni (fictional)',notes:'Each family pays its own folio',lines:[['Classic',6,5],['Deluxe',3,2],['Suite',1,1]]},
  {id:'GB-H2',name:'Godavari Agro dealers meet',company:'CO-2',arrival:'2026-07-10',departure:'2026-07-12',contact:'Sunita Patil',notes:'Dealer meet · breakfast included (simulated)',lines:[['Classic',6,5],['Deluxe',3,3]]},
  {id:'GB-H3',name:'Sahyadri Infra training camp',company:'CO-3',arrival:'2026-08-18',departure:'2026-08-21',contact:'Imran Shaikh',notes:'Corporate training · classroom in the banquet hall (fictional)',lines:[['Deluxe',4,4],['Classic',2,2]]},
  {id:'GB-H4',name:'Ganesh festival group (cultural trust)',arrival:'2026-09-18',departure:'2026-09-21',contact:'Mr. Deshmukh (fictional)',notes:'Festival pilgrims · one folio per family',lines:[['Classic',5,4],['Deluxe',2,2]]},
];
export const DOWNTIME_REASONS=['AC compressor repair','Bathroom plumbing leak','Wall seepage repair and repaint','Electrical rewiring','Geyser replacement','Ceiling fan and light replacement'];

export function addModuleHistory(s:State,rng:Rng,o:HistoryWindow,ctx:HistoryContext):void{
  yieldHistory(s,o);
  groupsHistory(s,ctx);
  maintenanceHistory(s,rng,o,ctx);
  inventoryHistory(s,rng,o);
  addPeopleHistory(s,rng,o,ctx);
  addSalesHistory(s,rng,o,ctx);
}

// ───────────── Module 13 · Tariff & yield ─────────────
function yieldHistory(s:State,o:HistoryWindow){
  const y=s.yieldX;if(!y)return;
  const seasons:YLDSeason[]=[
    {id:'YLDS-H1',name:'Summer lull',from:'2026-06-01',to:'2026-06-30',adjBps:-800,types:[],priority:1},
    {id:'YLDS-H2',name:'Monsoon low season',from:'2026-07-01',to:'2026-07-31',adjBps:-1000,types:[],priority:1},
    {id:'YLDS-H3',name:'Late monsoon',from:'2026-08-01',to:'2026-08-31',adjBps:-800,types:[],priority:1},
    {id:'YLDS-H4',name:'Ganesh festival',from:'2026-09-14',to:'2026-09-24',adjBps:1000,types:[],priority:2},
    {id:'YLDS-H5',name:'Navratri / Dussehra',from:'2026-10-11',to:'2026-10-20',adjBps:1000,types:[],priority:2},
  ];
  y.seasons=[...seasons,...y.seasons];
  const hist:YLDHistory[]=[{id:'YLDH-H01',at:o.from+'T09:00',actor:'Sagar · Owner',area:'Weekend rates',before:'—',after:'Fri/Sat +8 %, Sunday −5 %',reason:'Rates for FY 2026-27 (fictional)'}];
  seasons.forEach((x,i)=>hist.push({id:'YLDH-H'+pad(i+2,2),at:addDays(x.from,-14)+'T10:00',actor:'Sagar · Owner',area:'Seasons',before:'—',after:`${x.name} ${x.from} – ${x.to} (${x.adjBps>0?'+':''}${x.adjBps/100} %)`,reason:'Season plan'}));
  y.history=[...y.history,...hist].sort((a,b)=>a.at<b.at?-1:1);
  // One publish every Monday for the following 7 nights: the nightly rate each room type was sold at (same rule the stays use).
  const types=[...new Set(s.rooms.map(r=>r.type))],racks=Object.fromEntries(types.map(t=>[t,s.rooms.find(r=>r.type===t)!.rate]));
  const pubs:YLDPublish[]=[];let n=0;
  const first=addDays(o.from,(8-new Date(o.from+'T12:00:00Z').getUTCDay())%7);
  const start=addDays(first,-7);
  for(let from=start;from<=o.to;from=addDays(from,7)){
    const to=addDays(from,6)>o.to?o.to:addDays(from,6);
    const rows=types.flatMap(type=>{const out:YLDPublish['rows']=[];for(let d=from;d<=to;d=addDays(d,1))out.push({type,date:d,before:racks[type],after:stayRate(racks[type],d)});return out;});
    pubs.push({id:'YLDP-H'+pad(++n,3),at:addDays(from,-2)+'T09:00',actor:'manager',from,to,types,rows,reason:festivalOn(from)?festivalOn(from)!.name+' week':'Weekly rate review'});
  }
  y.publishes=[...pubs,...y.publishes];
}

// ───────────── Module 14 · Groups & corporate ─────────────
function groupsHistory(s:State,ctx:HistoryContext){
  const g=s.groupsX;if(!g)return;
  g.blocks=[...ctx.groups,...g.blocks];
  const co=g.companies.find(c=>c.id==='CO-3');
  if(co){
    co.limitHistory=[...(co.limitHistory??[]),{from:15000000,to:co.creditLimit,at:'2026-06-12T11:30',actor:'manager',reason:'Quarterly training programme volume',request:'GLR-H1'}];
    g.requests=[{id:'GLR-H1',company:'CO-3',from:15000000,to:co.creditLimit,reason:'Quarterly training programme volume',actor:'reception',at:'2026-06-12T10:00',status:'Approved',approver:'owner',decidedAt:'2026-06-12T11:30'},...g.requests];
  }
}

// ───────────── Module 15 · Maintenance ─────────────
const TITLES:Record<string,string[]>={
  AC:['AC not cooling','AC water leaking from indoor unit','AC remote not responding','AC making a rattling noise'],Geyser:['Geyser not heating','Geyser tripping the switch'],TV:['TV shows “No signal”','TV remote not working'],
  Lift:['Lift door sensor intermittent','Lift stopped between floors (cleared)'],Generator:['DG set slow to start','DG set load-test fault'],Pump:['Fire pump pressure low'],'RO plant':['RO output low'],Other:['General repair']};
const SPARES:Record<string,string[]>={AC:['SP-01','SP-02','SP-03','SP-08'],Geyser:['SP-04','SP-05'],TV:['SP-06'],Lift:['SP-07'],Generator:['SP-09','SP-07'],Pump:['SP-07'],'RO plant':['SP-07'],Other:['SP-07']};
function maintenanceHistory(s:State,rng:Rng,o:HistoryWindow,ctx:HistoryContext){
  const mx=s.maintenanceX;if(!mx)return;const x=mx;
  const orders:MNTWorkOrder[]=[];let n=0;
  const nm=(u:string)=>FIRST_OF[u]??u;
  const h=(at:string,by:string,action:string,note?:string):MNTHistory=>({at,by:nm(by),action,...(note?{note}:{})});
  const clockAt=(date:string,mins:number)=>{const m=Math.max(0,Math.min(23*60+59,mins));return date+'T'+pad(Math.floor(m/60),2)+':'+pad(m%60,2);};
  const plus=(at:string,mins:number)=>new Date(Date.parse(at+':00Z')+mins*60000).toISOString().slice(0,16);
  const spareCost=(id:string)=>x.spares.find(p=>p.id===id)?.cost??0;
  function wo(kind:'Corrective'|'Preventive',title:string,priority:MNTPriority,createdAt:string,asset:MNTAsset|undefined,extra:Partial<MNTWorkOrder>={}):MNTWorkOrder{
    const sla=MNT_PRIORITY[priority].slaMins,late=rng.chance(0.08);
    const startedAt=plus(createdAt,rng.int(15,120)),planned=plus(createdAt,Math.round(sla*(late?rng.next()*0.8+1.1:rng.next()*0.7+0.1))),doneAt=planned>plus(startedAt,10)?planned:plus(startedAt,10);
    const created='supervisor',verifiedAt=plus(doneAt,rng.int(30,360));
    const parts=kind==='Corrective'&&asset&&rng.chance(0.3)?(()=>{const sp=rng.pick(SPARES[asset.kind]??SPARES.Other);return [{spareId:sp,qty:1,unitCost:spareCost(sp)}];})():[];
    const w:MNTWorkOrder={id:'WO-H'+pad(++n),kind,title,priority,status:'Verified',createdAt,createdBy:created,startedAt,doneAt,doneBy:'maint',verifiedAt,verifiedBy:'supervisor',assignee:'maint',parts,
      ...(asset?{assetId:asset.id,...(asset.room?{room:asset.room}:{area:asset.area})}:{}),...(asset?.vendorId&&rng.chance(0.2)?{vendorId:asset.vendorId}:{}),...extra,
      history:[h(createdAt,created,'Created'),h(plus(createdAt,5),'manager','Assigned','Joseph'),h(startedAt,'maint','Started'),
        ...parts.map(p=>h(plus(startedAt,20),'maint','Part used',`1 × ${x.spares.find(q=>q.id===p.spareId)?.name??p.spareId}`)),h(doneAt,'maint','Completed'),h(verifiedAt,'supervisor','Verified')]};
    return w;
  }
  // Corrective work orders: roughly one every three days.
  const assets=x.assets.filter(a=>a.status!=='Retired');
  for(let d=o.from;d<=addDays(o.to,-1);d=addDays(d,1)){
    if(!rng.chance(1/3))continue;
    const a=rng.pick(assets),p=rng.weighted([['P1',5],['P2',20],['P3',55],['P4',20]] as const);
    orders.push(wo('Corrective',rng.pick(TITLES[a.kind]??TITLES.Other),p,clockAt(d,rng.int(7*60,20*60)),a));
  }
  // Room downtime: a corrective work order whose nights have no stay (the generator reserved them).
  for(const dt of ctx.downtimes){
    const a=x.assets.find(q=>q.room===dt.room);
    orders.push(wo('Corrective',dt.reason,'P2',dt.from+'T08:30',a,{room:dt.room,downtimeFrom:dt.from+'T12:00',downtimeTo:dt.to+'T11:00',note:'Room out of service until the repair is verified'}));
  }
  // Preventive work orders: every plan occurrence back from its last completion.
  for(const p of x.plans){
    const asset=x.assets.find(a=>a.id===p.assetId);
    for(let d=p.lastDone??'';d&&d>=o.from;d=addDays(d,-p.everyDays)){
      if(x.orders.some(w=>w.planId===p.id&&w.dueDate===d))continue;
      orders.push(wo('Preventive',p.title,'P3',addDays(d,-3)+'T08:00',asset,{planId:p.id,dueDate:d}));
    }
  }
  orders.sort((a,b)=>a.createdAt<b.createdAt?-1:a.createdAt>b.createdAt?1:a.id<b.id?-1:1);
  orders.forEach((w,i)=>{w.id='WO-H'+pad(i+1);});
  x.orders=[...orders,...x.orders];
}

// ───────────── Module 16 · Linen, laundry & stock ─────────────
function occupancyByDate(s:State,o:HistoryWindow){
  const occ=new Map<string,number>();
  for(const b of s.bookings)if(b.status==='Departed'||b.status==='In house')for(let d=b.arrival;d<b.departure&&d<=o.to;d=addDays(d,1))occ.set(d,(occ.get(d)??0)+1);
  return occ;
}
function inventoryHistory(s:State,rng:Rng,o:HistoryWindow){
  const x=s.inventoryX;if(!x)return;
  const occ=occupancyByDate(s,o);
  const items=x.items;
  // The base fixture's consumption for 17–22 Oct was written for fictional occupancy; replace it with moves that follow the real stays.
  x.moves=x.moves.filter(m=>!((m.kind==='use'||m.kind==='room-change')&&m.date<'2026-10-23'));
  const moves:INVMove[]=[];let mn=0;const mv=(m:Omit<INVMove,'id'>)=>moves.push({id:'IMV-H'+pad(++mn,5),...m});
  const lastUse=addDays(o.to,-1);
  const linenUse=new Map<string,Map<string,number>>(); // date → item → qty
  for(let d=o.from,i=0;d<=lastUse;d=addDays(d,1),i++){
    const n=occ.get(d)??0;if(!n)continue;
    items.forEach((it,j)=>{
      if(!it.stdX100)return;
      const factor=(1+(rng.next()-0.5)*0.2)*(it.id==='AMN-SH'?1.25:1);
      const q=Math.round(n*it.stdX100/100*factor);if(!q)return;
      const linen=it.kind==='linen';
      mv({date:d,at:d+'T11:30',kind:linen?'room-change':'use',item:it.id,qty:q,from:'floor'+((i+j)%3+1),to:linen?'rooms':'used',actor:i%2?'housekeeper2':'housekeeper'});
      if(linen){let m=linenUse.get(d);if(!m){m=new Map();linenUse.set(d,m);}m.set(it.id,q);}
    });
  }
  // Weekly goods received (Tuesdays) for amenities and cleaning supplies.
  const grns:INVGrn[]=[];let gn=0;
  const suppliers=[...new Set(items.filter(i=>i.supplier).map(i=>i.supplier!))];
  for(let d=o.from;d<=lastUse;d=addDays(d,1)){
    if(new Date(d+'T12:00:00Z').getUTCDay()!==2)continue;
    for(const sup of suppliers){
      const lines=items.filter(i=>i.supplier===sup&&rng.chance(0.7)).map(i=>({item:i.id,qty:Math.max(1,Math.round(i.reorderQty*(0.8+rng.next()*0.5))),cost:Math.round(i.cost/100)*100}));
      if(!lines.length)continue;
      const id='GRN-H'+pad(++gn,3);
      grns.push({id,date:d,supplier:sup,ref:`${sup.split(' ')[0].toUpperCase()}/${pad(2000+gn)} (fictional)`,lines,actor:'supervisor'});
      lines.forEach(l=>mv({date:d,at:d+'T10:15',kind:'grn',item:l.item,qty:l.qty,from:sup,to:'store',ref:id,actor:'supervisor'}));
    }
  }
  // Laundry: batches go out Monday and Thursday and come back the next day, covering the linen used since the last batch.
  const linen=items.filter(i=>i.kind==='linen'&&i.laundryRate);
  const batches:INVBatch[]=[],losses:INVLoss[]=[];let bn=0,ln=0;
  let lastSent=o.from;
  for(let d=o.from;d<=addDays(o.to,-2);d=addDays(d,1)){
    const dow=new Date(d+'T12:00:00Z').getUTCDay();if(dow!==1&&dow!==4)continue;
    const lines=linen.map(it=>{let q=0;for(let k=lastSent;k<d;k=addDays(k,1))q+=linenUse.get(k)?.get(it.id)??0;return {item:it.id,sent:q};}).filter(l=>l.sent>0);
    lastSent=d;if(!lines.length)continue;
    const id='LB-H'+pad(++bn,3),ret=addDays(d,1);
    const done=lines.map(l=>{
      const missing=rng.chance(0.07)?rng.int(1,2):0,damaged=rng.chance(0.07)?1:0;
      return {item:l.item,sent:l.sent,received:Math.max(0,l.sent-missing-damaged),damaged};
    });
    const rate=(id2:string)=>items.find(i=>i.id===id2)!.laundryRate??0,short=done.some(l=>l.sent!==l.received+l.damaged);
    batches.push({id,vendor:'Fresh Linen Services',sent:d,sentAt:d+'T16:10',lines:done,status:short?'Returned short':'Returned',returned:ret,actor:'housekeeper',receivedBy:'supervisor',
      charge:done.reduce((a,l)=>a+(l.received+l.damaged)*rate(l.item),0),...(short?{note:'Shortfall reported to the vendor (fictional)'}:{})});
    for(const l of done){
      mv({date:d,at:d+'T16:10',kind:'laundry-out',item:l.item,qty:l.sent,from:'soiled',to:'laundry',ref:id,actor:'housekeeper'});
      mv({date:ret,at:ret+'T11:00',kind:'laundry-in',item:l.item,qty:l.received,from:'laundry',to:'store',ref:id,actor:'supervisor'});
      const miss=l.sent-l.received-l.damaged,it=items.find(i=>i.id===l.item)!;
      if(l.damaged)losses.push({id:'LOSS-H'+pad(++ln,3),date:ret,item:l.item,loc:'laundry',qty:l.damaged,reason:'Damaged at laundry',actor:'supervisor',value:l.damaged*it.cost,ref:id});
      if(miss)losses.push({id:'LOSS-H'+pad(++ln,3),date:ret,item:l.item,loc:'laundry',qty:miss,reason:'Missing from laundry batch (vendor claim)',actor:'supervisor',value:miss*it.cost,ref:id});
    }
  }
  // Monthly physical count of the main store (last day of Apr–Sep): a few small shortages, each with its loss entry.
  const counts:INVCount[]=[];let cn=0;
  for(const ym of ['2026-04','2026-05','2026-06','2026-07','2026-08','2026-09']){
    const date=addDays(ym+'-01',31).slice(0,8)+'01',last=addDays(date,-1),id='CNT-H'+pad(++cn,2);
    const short=new Map<string,number>();
    items.filter(i=>(i.stock.store??0)>5).sort(()=>rng.next()-0.5).slice(0,rng.int(1,2)).forEach(i=>short.set(i.id,rng.int(1,3)));
    const lines=items.map(i=>({item:i.id,system:i.stock.store??0,counted:(i.stock.store??0)-(short.get(i.id)??0)}));
    counts.push({id,date:last,at:last+'T18:00',loc:'store',lines,status:'Posted',actor:'supervisor',reviewer:'manager',reason:short.size?'Torn pieces binned without a record':undefined});
    short.forEach((q,item)=>losses.push({id:'LOSS-H'+pad(++ln,3),date:last,item,loc:'store',qty:q,reason:'Count shortage',note:'Store count '+last,actor:'supervisor',value:q*items.find(i=>i.id===item)!.cost,ref:id}));
  }
  counts.forEach(c=>{if(c.reason===undefined)delete c.reason;});
  // A few guest-related linen losses.
  for(let k=0;k<14;k++){const d=addDays(o.from,rng.int(0,nights(o.from,o.to)-2)),it=rng.pick(linen.filter(i=>i.id!=='LIN-BR'));
    losses.push({id:'LOSS-H'+pad(++ln,3),date:d,item:it.id,loc:'rooms',qty:1,reason:rng.pick(['Stained beyond use','Taken by guest','Damaged']),actor:'housekeeper',value:it.cost});}
  const byDate=<T extends {date:string}>(a:T,b:T)=>a.date<b.date?-1:a.date>b.date?1:0;
  x.moves=[...moves.sort(byDate),...x.moves];x.grns=[...grns,...x.grns];x.batches=[...batches,...x.batches];x.losses=[...losses.sort(byDate),...x.losses];x.counts=[...counts,...x.counts];
}

