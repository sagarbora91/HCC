// Module 23 · Restaurant POS & room service — fixture extensions (fictional data only).
// Outlet sales live in s.posX: no money is posted into existing bookings. Cash bills count in the front-desk drawer (shared till, DEC-052):
// POS-F01 adds ₹525 to the BASE drawer (₹10,200 → ₹10,725; DEC-069 GST 5 %, was ₹550); POS-F02 is UPI (non-cash). POS-F03/F04 are open checks that block day close.
import {clone} from '../../domain/clone';
import type {State} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import {gstForFood,gstLineTax} from '../../domain/gst';
import type {POSMenuItem,POSOrder,POSState} from '../../domain/streams/pos';

const R='restaurant',RS='room-service',BOTH=[R,RS];
const item=(id:string,name:string,category:string,rupees:number,veg:boolean,outlets=BOTH,available=true):POSMenuItem=>({id,name,category,price:rupees*100,veg,outlets,available});
export const POS_FIXTURE_MENU:POSMenuItem[]=[
  item('m-tea','Masala chai','Beverages',60,true),item('m-coffee','Filter coffee','Beverages',80,true),item('m-lime','Fresh lime soda','Beverages',120,true),item('m-lassi','Sweet lassi','Beverages',140,true),
  item('m-poha','Kanda poha','Breakfast',150,true),item('m-idli','Idli sambar (3)','Breakfast',160,true),item('m-dosa','Masala dosa','Breakfast',220,true),item('m-paratha','Aloo paratha with curd','Breakfast',240,true),
  item('m-soup','Tomato dhaniya shorba','Starters',180,true),item('m-tikka','Paneer tikka','Starters',360,true),item('m-kebab','Chicken seekh kebab','Starters',420,false,[R]),
  item('m-dal','Dal tadka','Mains',280,true),item('m-paneer','Paneer butter masala','Mains',380,true),item('m-curry','Kolhapuri chicken curry','Mains',460,false),item('m-thali','Maharashtrian veg thali','Mains',420,true,[R]),item('m-biryani','Veg dum biryani','Mains',340,true,BOTH,false),
  item('m-roti','Tandoori roti','Breads & rice',40,true),item('m-naan','Butter naan','Breads & rice',70,true),item('m-rice','Jeera rice','Breads & rice',180,true),
  item('m-gulab','Gulab jamun (2)','Desserts',120,true),item('m-kulfi','Malai kulfi','Desserts',150,true),
];
const line=(id:string,m:string,qty:number,kot?:string)=>{const x=POS_FIXTURE_MENU.find(i=>i.id===m)!;return {id,item:m,name:x.name,price:x.price,qty,...(kot?{kot}:{})};};

export function extendBase(s:State):void{
  const d=s.date,t=(hm:string)=>d+'T'+hm,tax=gstForFood(s,d).bps; // DEC-069 GST master: restaurant 5 % (not specified premises)
  const settle=(ls:{price:number;qty:number}[],no:number,method:string,reference:string|undefined,at:string,rid:string)=>{
    const net=ls.reduce((a,l)=>a+l.price*l.qty,0),x=gstLineTax(net,tax);return {mode:'outlet' as const,billNo:`POS/${d.slice(2,4)}${d.slice(5,7)}/${String(no).padStart(4,'0')}`,method,...(reference?{reference}:{}),net,taxBps:tax,tax:x,total:net+x,date:d,at,by:'cashier',receiptId:rid};};
  const L1=[line('POSL-F01','m-poha',2,'KOT-F01'),line('POSL-F02','m-tea',2,'KOT-F01'),line('POSL-F03','m-coffee',1,'KOT-F01')];
  const L2=[line('POSL-F04','m-dosa',2,'KOT-F02'),line('POSL-F05','m-idli',1,'KOT-F02'),line('POSL-F06','m-coffee',1,'KOT-F02')];
  const orders:POSOrder[]=[
    // Breakfast at table T1, paid cash (₹500 + ₹25 GST = ₹525 outlet cash; was ₹550 with the synthetic 10 %).
    {id:'POS-F01',outlet:R,table:'T1',guest:'Walk-in · Mr. Kulkarni',covers:2,date:d,openedAt:t('07:40'),openedBy:'cashier',status:'Settled',version:4,
      lines:L1,
      kots:[{id:'KOT-F01',no:1,at:t('07:41'),by:'cashier',status:'Served',readyAt:t('07:52'),servedAt:t('07:55')}],settlement:settle(L1,1,'Cash',undefined,t('07:58'),'POSR-F01')},
    // Breakfast at table T4, paid UPI (₹680 + ₹34 GST = ₹714; was ₹748).
    {id:'POS-F02',outlet:R,table:'T4',covers:3,date:d,openedAt:t('07:45'),openedBy:'cashier',status:'Settled',version:4,
      lines:L2,
      kots:[{id:'KOT-F02',no:2,at:t('07:46'),by:'cashier',status:'Served',readyAt:t('07:58'),servedAt:t('08:00')}],settlement:settle(L2,2,'UPI','DEMO-UPI-POS-7731',t('07:59'),'POSR-F02')},
    // Lunch at table T2: KOT 3 in the kitchen.
    {id:'POS-F03',outlet:R,table:'T2',guest:'Walk-in · Shinde family',covers:4,date:d,openedAt:t('07:50'),openedBy:'reception',status:'Open',version:3,
      lines:[line('POSL-F07','m-thali',2,'KOT-F03'),line('POSL-F08','m-paneer',1,'KOT-F03'),line('POSL-F09','m-naan',4,'KOT-F03'),line('POSL-F10','m-lime',2)],
      kots:[{id:'KOT-F03',no:3,at:t('07:52'),by:'reception',status:'Preparing'}]},
  ];
  const rs=s.bookings.find(b=>b.status==='In house'&&b.room==='201');
  if(rs)orders.push({id:'POS-F04',outlet:RS,room:rs.room,booking:rs.id,guest:rs.guest,covers:1,date:d,openedAt:t('07:35'),openedBy:'reception',status:'Open',version:3,
    lines:[line('POSL-F11','m-paratha',1,'KOT-F04'),line('POSL-F12','m-tea',1,'KOT-F04')],
    kots:[{id:'KOT-F04',no:1,at:t('07:36'),by:'reception',status:'Ready',readyAt:t('07:54')}]});
  const st=(o:POSOrder)=>o.settlement!;
  const p:POSState={
    outlets:[{id:R,name:'Restaurant',kind:'table',tables:['T1','T2','T3','T4','T5','T6','T7','T8']},{id:RS,name:'Room service',kind:'room'}],
    menu:clone(POS_FIXTURE_MENU),orders,
    receipts:orders.filter(o=>o.settlement).map(o=>({id:st(o).receiptId!,order:o.id,outlet:o.outlet,method:st(o).method!,amount:st(o).total,net:st(o).net,tax:st(o).tax,taxBps:tax,
      reference:st(o).reference??'Outlet cash',date:d,actor:'cashier',kind:'sale' as const})),
    kotSeq:{[R+':'+d]:3,[RS+':'+d]:1},billSeq:2,
  };
  s.posX=p;
}
/** S1 is the empty-hotel scenario: no checks or outlet sales (menu and outlets stay). */
export const scenarioPatches:Record<string,(s:State)=>void>={S1:s=>{if(s.posX){s.posX.orders=[];s.posX.receipts=[];s.posX.kotSeq={};s.posX.billSeq=0;}}};
export const scenarioInfo:ScenarioInfo[]=[];
