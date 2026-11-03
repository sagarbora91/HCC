// F7-R8 · stock reports on the engine (inventory slice): stock by location, reorder alerts, laundry batches, stock counts, losses, consumption per
// occupied room-night, par and minimums, stock movements. Adapters over invSlice / invAlerts / invCountVariance / invConsumption.
// View PERM-STK-001 (quantities, incl. Supervisor / Housekeeper / Maintenance); value and cost columns need PERM-STK-004 and are removed without it (rule M).
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {invSlice,invAlerts,invCountVariance,invCountValue,invConsumption,invLocLabel,invItem,invTotal,invValue,invClean,invParRequired,invParPerSet,invParCoverage,invFmt,INV_LOCS,INV_KINDS,invMissing} from '../../streams/inventory';
import {col,rowOf,rangeParams,userName} from '../money/common';

const V='PERM-STK-001',G='PERM-STK-004';
const kindLabel=(k:string)=>INV_KINDS.find(x=>x.id===k)?.label??k;
const base={area:'stock' as const,viewPerms:[V],phone:'yes' as const};

export const STOCK_SHEET_COLS:RptColumn[]=[col('item','Item','text'),col('kind','Kind','text'),col('unit','Unit','text'),...INV_LOCS.map(l=>col(l.id,l.short,'int')),col('total','Total','int'),col('cost','Unit cost','money',true),col('value','Value','money',true)];
export const stockSheet=registerReport({...base,id:'rpt.stock.stock-sheet',title:'Stock by location',priority:'P1',status:'built',moneyFlag:true,moneyGate:G,params:[],columns:STOCK_SHEET_COLS,formats:['screen','xlsx','pdf'],
  dateBasis:'The stock held now: pieces per location, total, unit cost and value (total × standard cost).',
  select:(s,_u,p)=>{
    const items=invSlice(s).items;
    const rows:RptRow[]=items.map(i=>rowOf('row',{item:i.name,kind:kindLabel(i.kind),unit:i.unit,...Object.fromEntries(INV_LOCS.map(l=>[l.id,i.stock[l.id]??0])),total:invTotal(i),cost:i.cost,value:invValue(i)}));
    rows.push(rowOf('total',{item:'Total value',kind:null,unit:null,...Object.fromEntries(INV_LOCS.map(l=>[l.id,null])),total:null,cost:null,value:items.reduce((a,i)=>a+invValue(i),0)}));
    return {params:p,columns:STOCK_SHEET_COLS,rows};
  }});

export const REORDER_COLS:RptColumn[]=[col('item','Item','text'),col('kind','Kind','text'),col('clean','Clean stock','int'),col('min','Minimum','int'),col('suggest','Suggested order','int'),col('cost','Unit cost','money',true)];
export const stockReorder=registerReport({...base,id:'rpt.stock.reorder',title:'Reorder alerts',priority:'P2',status:'built',moneyFlag:true,moneyGate:G,params:[],columns:REORDER_COLS,formats:['screen','xlsx','pdf'],
  dateBasis:'Items whose clean stock (store + floor pantries) is below the minimum, now. Suggested = the larger of the reorder quantity and twice the minimum less clean stock.',
  select:(s,_u,p)=>({params:p,columns:REORDER_COLS,rows:invAlerts(s).sort((a,b)=>(a.clean-a.item.min)-(b.clean-b.item.min)||a.item.name.localeCompare(b.item.name))
    .map(a=>rowOf('row',{item:a.item.name,kind:kindLabel(a.item.kind),clean:a.clean,min:a.item.min,suggest:a.suggest,cost:a.item.cost}))}) });

export const LAUNDRY_COLS:RptColumn[]=[col('batch','Batch','text'),col('vendor','Vendor','text'),col('sent','Sent','date'),col('status','Status','text'),col('pieces','Pieces sent','int'),
  col('received','Received','int'),col('damaged','Damaged','int'),col('missing','Missing','int'),col('charge','Laundry charge','money',true)];
export const stockLaundry=registerReport({...base,id:'rpt.stock.laundry',title:'Laundry batches',priority:'P3',status:'built',moneyFlag:true,moneyGate:G,params:[],columns:LAUNDRY_COLS,formats:['screen','xlsx','pdf'],
  dateBasis:'Every laundry batch, newest first. Received, damaged and missing are blank while the batch is still out; the charge is the one computed when the batch came back.',
  select:(s,_u,p)=>({params:p,columns:LAUNDRY_COLS,rows:[...invSlice(s).batches].sort((a,b)=>b.sent.localeCompare(a.sent)||b.id.localeCompare(a.id)).map(b=>{
    const back=b.status!=='Out';return rowOf('row',{batch:b.id,vendor:b.vendor,sent:b.sent,status:b.status,pieces:b.lines.reduce((a,l)=>a+l.sent,0),received:back?b.lines.reduce((a,l)=>a+(l.received??0),0):null,
      damaged:back?b.lines.reduce((a,l)=>a+(l.damaged??0),0):null,missing:back?b.lines.reduce((a,l)=>a+invMissing(l),0):null,charge:b.charge??null});})}) });

export const COUNTS_COLS:RptColumn[]=[col('count','Count','text'),col('date','Date','date'),col('location','Location','text'),col('status','Status','text'),col('item','Item','text'),
  col('system','System','int'),col('counted','Counted','int'),col('variance','Variance','int'),col('value','Variance value','money',true),col('by','Counted by','text')];
export const stockCounts=registerReport({...base,id:'rpt.stock.counts',title:'Stock counts',priority:'P3',status:'built',moneyFlag:true,moneyGate:G,params:[],columns:COUNTS_COLS,formats:['screen','xlsx','pdf'],
  dateBasis:'Each count with the lines where the count differs from the system quantity (invCountVariance); the variance value is shown on the first line of the count.',
  select:(s,_u,p)=>{
    const rows:RptRow[]=[];
    for(const c of [...invSlice(s).counts].sort((a,b)=>b.date.localeCompare(a.date)||b.id.localeCompare(a.id))){
      const v=invCountVariance(c);
      if(!v.length)rows.push(rowOf('row',{count:c.id,date:c.date,location:invLocLabel(c.loc),status:c.status,item:'No variance',system:null,counted:null,variance:null,value:0,by:userName(s,c.actor)}));
      v.forEach((l,i)=>rows.push(rowOf('row',{count:c.id,date:c.date,location:invLocLabel(c.loc),status:c.status,item:invItem(s,l.item)?.name??l.item,system:l.system,counted:l.counted,variance:l.counted-l.system,
        value:i===0?invCountValue(s,c):null,by:userName(s,c.actor)})));}
    return {params:p,columns:COUNTS_COLS,rows};
  }});

export const LOSSES_COLS:RptColumn[]=[col('date','Date','date'),col('item','Item','text'),col('location','Location','text'),col('qty','Qty','int'),col('reason','Reason','text'),col('value','Value','money',true),col('by','By','text')];
export const stockLosses=registerReport({...base,id:'rpt.stock.losses',title:'Losses',priority:'P3',status:'built',moneyFlag:true,moneyGate:G,dateClass:'D-B',params:rangeParams('D-B'),columns:LOSSES_COLS,formats:['screen','xlsx','pdf'],
  dateBasis:'Recorded losses by date, oldest first; value at the standard cost when recorded.',
  select:(s,_u,p)=>{
    const L=invSlice(s).losses.filter(l=>l.date>=p.from!&&l.date<=p.to!).sort((a,b)=>a.date.localeCompare(b.date)||a.id.localeCompare(b.id));
    const rows:RptRow[]=L.map(l=>rowOf('row',{date:l.date,item:invItem(s,l.item)?.name??l.item,location:invLocLabel(l.loc),qty:l.qty,reason:l.reason,value:l.value,by:userName(s,l.actor)}));
    if(rows.length)rows.push(rowOf('total',{date:'Total',item:null,location:null,qty:L.reduce((a,l)=>a+l.qty,0),reason:null,value:L.reduce((a,l)=>a+l.value,0),by:null}));
    return {params:p,columns:LOSSES_COLS,rows};
  }});

export const CONSUMPTION_COLS:RptColumn[]=[col('item','Item','text'),col('kind','Kind','text'),col('used','Used','int'),col('perRN','Per room-night','text'),col('std','Standard','text'),col('variance','Variance %','int'),col('flag','Flag','text')];
export const stockConsumption=registerReport({...base,id:'rpt.stock.consumption',title:'Consumption per occupied room-night',priority:'P2',status:'built',moneyFlag:false,dateClass:'D-B',params:rangeParams('D-B'),columns:CONSUMPTION_COLS,formats:['screen','xlsx','pdf'],phone:'yes',
  dateBasis:'Amenities and cleaning supplies: use moves; linen: room-change moves. Per room-night = used ÷ occupied room-nights of the range (the first row of the total block).',
  select:(s,_u,p)=>{
    const c=invConsumption(s,p.from!,p.to!);
    const rows:RptRow[]=c.rows.map(r=>rowOf('row',{item:r.item.name,kind:kindLabel(r.item.kind),used:r.used,perRN:invFmt(r.perRN),std:r.std?invFmt(r.std):null,variance:r.variance===null?null:r.variance+0,flag:r.flag?'Outside ±15%':null}));
    rows.push(rowOf('total',{item:`Occupied room-nights ${c.rn}`,kind:null,used:null,perRN:null,std:null,variance:null,flag:null}));
    return {params:p,columns:CONSUMPTION_COLS,rows};
  }});

export const PAR_COLS:RptColumn[]=[col('item','Linen item','text'),col('perSet','Per set','int'),col('required','Required (sets)','int'),col('held','Held','int'),col('coverage','Coverage %','int'),col('min','Minimum','int'),col('clean','Clean stock','int')];
export const stockPar=registerReport({...base,id:'rpt.stock.par',title:'Par and minimums',priority:'P3',status:'built',moneyFlag:false,params:[],columns:PAR_COLS,formats:['screen','xlsx','pdf'],
  dateBasis:'Linen items with a par by room type: pieces per set of rooms, required for the configured number of sets, held now, coverage and the reorder minimum.',
  select:(s,_u,p)=>({params:p,columns:PAR_COLS,rows:invSlice(s).items.filter(i=>i.kind==='linen'&&i.par).map(i=>rowOf('row',{item:i.name,perSet:invParPerSet(s,i),required:invParRequired(s,i),held:invTotal(i),coverage:invParCoverage(s,i),min:i.min,clean:invClean(i)}))}) });

export const MOVES_COLS:RptColumn[]=[col('when','When','text'),col('type','Type','text'),col('item','Item','text'),col('qty','Qty','int'),col('from','From','text'),col('to','To','text'),col('ref','Reference','text'),col('by','By','text')];
export const stockMovements=registerReport({...base,id:'rpt.stock.movements',title:'Stock movements',priority:'P3',status:'built',moneyFlag:false,dateClass:'D-B',
  params:[...rangeParams('D-B'),{key:'item',label:'Item',type:'text'}],columns:MOVES_COLS,formats:['screen','xlsx','pdf','csv'],
  dateBasis:'Every stock move in the range, newest first.',
  select:(s,_u,p)=>{
    const q=String(p.item??'').trim().toLowerCase();
    return {params:p,columns:MOVES_COLS,rows:invSlice(s).moves.filter(m=>m.date>=p.from!&&m.date<=p.to!&&(!q||(invItem(s,m.item)?.name??m.item).toLowerCase().includes(q)))
      .sort((a,b)=>b.at.localeCompare(a.at)||b.id.localeCompare(a.id)).map(m=>rowOf('row',{when:m.at,type:m.kind,item:invItem(s,m.item)?.name??m.item,qty:m.qty,from:m.from?invLocLabel(m.from):null,to:m.to?invLocLabel(m.to):null,ref:m.ref??null,by:userName(s,m.actor)}))};
  }});
