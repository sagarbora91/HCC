// F7-R1 · demo report on the engine: rpt.money.revenue-by-category-tender (F4-T2). Adapter selector over rctReport — no new figures (RPT-ARCHITECTURE §7).
// Block A = revenue by room category, B = collections by tender, C = the bridge. Occ % is the OP-13 computed-column demo: pct(nightsSold,rooms).
import type {RptColumn,RptRow} from '../types';
import type {RCTBlockARow,RCTBlockBRow,RCTBasis} from '../../types';
import {registerReport} from '../catalogue';
import {rctReport} from '../../streams/rpt-revcat';

const M=(key:string,label:string):RptColumn=>({key,label,type:'money',money:true});
export const RCT_A_COLS:RptColumn[]=[{key:'category',label:'Category',type:'text',money:false},{key:'rooms',label:'Rooms (room-nights sellable)',type:'int',money:false},
  {key:'nightsSold',label:'Room-nights sold',type:'int',money:false},{key:'occPct',label:'Occ %',type:'pct',money:false,computed:{expr:'pct(nightsSold,rooms)',deps:['nightsSold','rooms']}},
  M('roomTariff','Room tariff'),M('extraPerson','Extra person'),M('mealPlans','Meal plans'),M('otherRoomCharges','Other charges'),M('discounts','Discounts'),M('corrections','Corrections'),
  M('net','Net (excl. GST)'),M('cgst','CGST'),M('sgst','SGST'),M('gross','Gross (incl. GST)'),M('adr','ADR')];
export const RCT_B_COLS:RptColumn[]=[{key:'category',label:'Category',type:'text',money:false},M('cash','Cash'),M('upi','UPI'),M('card','Card'),M('bank','Bank transfer'),
  M('companyCredit','Company credit'),M('udhaar','Udhaar'),M('ota','OTA'),M('otherTenders','Other tenders'),M('advancesReceived','Advances received'),
  M('advancesAdjusted','Advances adjusted'),M('advancesRefunded','Advances refunded'),M('refunds','Refunds (in tenders)'),M('netReceived','Net received')];
export const RCT_C_COLS:RptColumn[]=[{key:'item',label:'Item',type:'text',money:false},M('amount','Amount')];
const aRow=(x:RCTBlockARow,kind:RptRow['kind']):RptRow=>({kind,cells:{category:x.label,rooms:x.rooms,nightsSold:x.nightsSold,roomTariff:x.roomTariff,extraPerson:x.extraPerson,
  mealPlans:x.mealPlans,otherRoomCharges:x.otherRoomCharges,discounts:x.discounts,corrections:x.corrections,net:x.net,cgst:x.cgst,sgst:x.sgst,gross:x.gross,adr:x.adr}});
const bRow=(x:RCTBlockBRow,kind:RptRow['kind']):RptRow=>({kind,cells:{category:x.label,cash:x.cash,upi:x.upi,card:x.card,bank:x.bank,companyCredit:x.companyCredit,udhaar:x.udhaar,
  ota:x.ota,otherTenders:Object.values(x.otherTenders).reduce((a,b)=>a+b,0),advancesReceived:x.advancesReceived,advancesAdjusted:x.advancesAdjusted,
  advancesRefunded:x.advancesRefunded,refunds:x.refunds,netReceived:x.netReceived}});

export const revenueByCategoryTender=registerReport({
  id:'rpt.money.revenue-by-category-tender',title:'Revenue by room category and tender',area:'money',priority:'P1',status:'built',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-001'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},
    {key:'basis',label:'Basis',type:'enum',options:['business-date','stay'],default:'business-date'}],
  columns:[...RCT_A_COLS],
  dateBasis:'Charges by service (business) date, receipts by receipt date; stay basis = Block A by arrival date. Bridge always on business dates.',
  formats:['screen','xlsx','pdf','csv'],phone:'summary',
  select:(s,_uid,p)=>{
    const r=rctReport(s,p.from!,p.to!,(p.basis as RCTBasis)??'business-date');
    const c=r.c;
    const sections=[
      {key:'a',title:'A Revenue',columns:RCT_A_COLS,rows:[...r.a.map(x=>aRow(x,'row')),aRow(r.aTotal,'total')]},
      {key:'b',title:'B Collections',columns:RCT_B_COLS,rows:[...r.b.map(x=>bRow(x,'row')),bRow(r.bTotal,'total')]},
      {key:'c',title:'C Bridge',columns:RCT_C_COLS,rows:([['Opening due',c.openingDue],['Gross charges',c.gross],['Net received',c.netReceived],['Advance movement',c.advanceMovement],
        ['Closing due',c.closingDue],['Opening advances held',c.openingAdvances],['Closing advances held',c.closingAdvances]] as [string,number][]).map(([item,amount])=>({kind:'row',cells:{item,amount}} as RptRow))
        .concat([{kind:'total',cells:{item:'Difference (must be 0)',amount:c.difference}}])},
    ];
    return {params:p,columns:sections[0].columns,rows:sections[0].rows,sections,about:{legacy:r.legacy}};
  },
});
