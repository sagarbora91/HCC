// Module 16 · Linen, laundry & stock — fixture extensions (fictional data only).
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import {addDays} from '../../domain/money';
import type {INVItem,INVMove,INVState,INVLoc} from '../../domain/streams/inventory';

type Seed=[id:string,name:string,kind:INVItem['kind'],unit:string,costRs:number,min:number,reorder:number,stdX100:number,stock:Partial<Record<INVLoc,number>>,extra?:Partial<INVItem>];
const P=(c:number,d:number,s:number)=>({Classic:c,Deluxe:d,Suite:s});
// Rooms: 12 Classic, 9 Deluxe, 3 Suite. Linen stock = store + 3 pantries + in rooms (one set) + soiled + at laundry.
const seeds:Seed[]=[
  ['LIN-BS','Bed sheet (king)','linen','pc',650,50,40,150,{store:40,floor1:6,floor2:6,floor3:6,rooms:48,soiled:10,laundry:24},{par:P(2,2,2),laundryRate:1800}],
  ['LIN-PC','Pillow cover','linen','pc',120,90,60,300,{store:82,floor1:12,floor2:10,floor3:12,rooms:102,soiled:18,laundry:40},{par:P(4,4,6),laundryRate:600}],
  ['LIN-DC','Duvet cover','linen','pc',1400,24,12,40,{store:22,floor1:3,floor2:3,floor3:3,rooms:24,soiled:4,laundry:8},{par:P(1,1,1),laundryRate:4500}],
  ['LIN-BT','Bath towel','linen','pc',450,55,60,180,{store:30,floor1:5,floor2:4,floor3:6,rooms:54,soiled:14,laundry:30},{par:P(2,2,4),laundryRate:1400}],
  ['LIN-HT','Hand towel','linen','pc',180,55,48,170,{store:52,floor1:8,floor2:7,floor3:8,rooms:54,soiled:12,laundry:26},{par:P(2,2,4),laundryRate:700}],
  ['LIN-BM','Bath mat','linen','pc',220,28,24,80,{store:28,floor1:4,floor2:3,floor3:4,rooms:27,soiled:6,laundry:10},{par:P(1,1,2),laundryRate:1000}],
  ['LIN-BR','Bathrobe','linen','pc',1600,6,4,20,{store:6,floor3:2,rooms:6,soiled:1,laundry:2},{par:P(0,0,2),laundryRate:4000}],
  ['AMN-SP','Soap bar 30 g','amenity','pc',14,120,300,200,{store:260,floor1:30,floor2:28,floor3:26},{supplier:'Morning Supplies'}],
  ['AMN-SH','Shampoo 30 ml','amenity','pc',12,120,300,200,{store:70,floor1:14,floor2:10,floor3:12},{supplier:'Morning Supplies'}],
  ['AMN-DK','Dental kit','amenity','pc',18,80,200,130,{store:150,floor1:20,floor2:18,floor3:16},{supplier:'Morning Supplies'}],
  ['AMN-WB','Water bottle 500 ml','amenity','pc',9,150,480,200,{store:310,floor1:36,floor2:40,floor3:30},{supplier:'Morning Supplies'}],
  ['AMN-TK','Tea & coffee kit','amenity','pc',15,100,240,200,{store:180,floor1:24,floor2:20,floor3:22},{supplier:'Morning Supplies'}],
  ['AMN-SL','Slippers (pair)','amenity','pair',35,40,100,50,{store:64,floor1:8,floor2:6,floor3:6},{supplier:'Morning Supplies'}],
  ['CLN-FC','Floor cleaner 5 L','cleaning','can',450,4,6,5,{store:5,floor1:1,floor2:1,floor3:1},{supplier:'Deccan Hygiene Traders'}],
  ['CLN-GC','Glass cleaner 500 ml','cleaning','bottle',120,6,12,8,{store:8,floor1:2,floor2:1,floor3:2},{supplier:'Deccan Hygiene Traders'}],
  ['CLN-TC','Toilet cleaner 1 L','cleaning','bottle',95,10,24,15,{store:14,floor1:3,floor2:2,floor3:3},{supplier:'Deccan Hygiene Traders'}],
  ['CLN-GB','Garbage bags (roll of 30)','cleaning','roll',60,10,20,10,{store:4,floor1:1,floor2:1,floor3:0},{supplier:'Deccan Hygiene Traders'}],
];
/** Fictional occupied room-nights before go-live (17–22 Oct); 23 Oct onward comes from bookings. */
const history:Record<string,number>={'2026-10-17':12,'2026-10-18':14,'2026-10-19':15,'2026-10-20':11,'2026-10-21':9,'2026-10-22':10};

export function extendBase(s:State):void{
  const items:INVItem[]=seeds.map(([id,name,kind,unit,costRs,min,reorderQty,stdX100,stock,extra])=>({id,name,kind,unit,cost:costRs*100,min,reorderQty,stdX100,stock:{...stock},...extra}));
  const moves:INVMove[]=[];let n=0;const mv=(m:Omit<INVMove,'id'>)=>moves.push({id:'IMV-s'+(++n),...m});
  // Seven days of consumption history (17–23 Oct). Shampoo runs ~25% above standard (a "watch" flag in the report).
  for(let d='2026-10-17',i=0;d<='2026-10-23';d=addDays(d,1),i++){
    const occ=history[d]??8;
    items.forEach((it,j)=>{
      const factor=(1+(((i*7+j*3)%9)-4)/40)*(it.id==='AMN-SH'?1.25:1);
      const q=Math.round(occ*(it.stdX100??0)/100*factor);if(!q)return;
      mv({date:d,at:d+'T11:30',kind:it.kind==='linen'?'room-change':'use',item:it.id,qty:q,from:'floor'+((i+j)%3+1),to:it.kind==='linen'?'rooms':'used',actor:i%2?'housekeeper2':'housekeeper'});
    });
  }
  // Earlier GRN for amenities (20 Oct).
  mv({date:'2026-10-20',at:'2026-10-20T10:15',kind:'grn',item:'AMN-SP',qty:300,from:'Morning Supplies',to:'store',ref:'GRN-S1',actor:'supervisor'});
  mv({date:'2026-10-20',at:'2026-10-20T10:15',kind:'grn',item:'AMN-WB',qty:480,from:'Morning Supplies',to:'store',ref:'GRN-S1',actor:'supervisor'});
  // Laundry: LB-S1 returned short on 22 Oct; LB-S2 out since 23 Oct (still at laundry).
  const linen=items.filter(i=>i.kind==='linen');
  const out=linen.map(i=>({item:i.id,sent:i.stock.laundry??0})).filter(l=>l.sent);
  const ret=[{item:'LIN-BS',sent:22,received:21,damaged:1},{item:'LIN-PC',sent:36,received:35,damaged:0},{item:'LIN-BT',sent:28,received:28,damaged:0},{item:'LIN-HT',sent:24,received:24,damaged:0}];
  const rate=(id:string)=>items.find(i=>i.id===id)!.laundryRate??0;
  const x:INVState={items,moves,
    batches:[
      {id:'LB-S1',vendor:'Fresh Linen Services',sent:'2026-10-21',sentAt:'2026-10-21T16:10',lines:ret,status:'Returned short',returned:'2026-10-22',actor:'housekeeper',receivedBy:'supervisor',
        charge:ret.reduce((a,l)=>a+(l.received+l.damaged)*rate(l.item),0),note:'1 pillow cover missing — vendor informed (fictional)'},
      {id:'LB-S2',vendor:'Fresh Linen Services',sent:'2026-10-23',sentAt:'2026-10-23T16:40',lines:out,status:'Out',actor:'housekeeper2'}],
    losses:[
      {id:'LOSS-S1',date:'2026-10-22',item:'LIN-BS',loc:'laundry',qty:1,reason:'Damaged at laundry',actor:'supervisor',value:65000,ref:'LB-S1'},
      {id:'LOSS-S2',date:'2026-10-22',item:'LIN-PC',loc:'laundry',qty:1,reason:'Missing from laundry batch (vendor claim)',actor:'supervisor',value:12000,ref:'LB-S1'},
      {id:'LOSS-S3',date:'2026-10-23',item:'LIN-BT',loc:'rooms',qty:2,reason:'Stained beyond use',note:'Room 206 · hair dye stains',actor:'housekeeper',value:90000},
      {id:'LOSS-S4',date:'2026-10-20',item:'LIN-PC',loc:'store',qty:2,reason:'Count shortage',note:'Store count 20 Oct',actor:'supervisor',value:24000,ref:'CNT-S1'}],
    counts:[
      {id:'CNT-S1',date:'2026-10-20',at:'2026-10-20T18:00',loc:'store',lines:items.map(i=>({item:i.id,system:i.id==='LIN-PC'?84:i.stock.store??0,counted:i.id==='LIN-PC'?82:i.stock.store??0})),status:'Posted',actor:'supervisor',reviewer:'manager',reason:'Two covers found torn and binned without a record'},
      {id:'CNT-S2',date:'2026-10-24',at:'2026-10-24T07:45',loc:'floor2',lines:items.map(i=>({item:i.id,system:i.stock.floor2??0,counted:i.id==='AMN-WB'?37:i.id==='LIN-HT'?6:i.stock.floor2??0})),status:'Submitted',actor:'housekeeper',note:'Morning count before rounds'}],
    grns:[{id:'GRN-S1',date:'2026-10-20',supplier:'Morning Supplies',ref:'MS/INV/2210 (fictional)',lines:[{item:'AMN-SP',qty:300,cost:1400},{item:'AMN-WB',qty:480,cost:900}],actor:'supervisor'}],
    parSets:3,vendor:{name:'Fresh Linen Services',turnaroundDays:1},occHistory:history};
  s.inventoryX=x;
}
export const scenarioPatches:Record<string,(s:State)=>void>={};
export const scenarioInfo:ScenarioInfo[]=[];
