// Maintenance + linen & stock area (W4-9). Slots: 'maintenanceX' (assets, vendors, AMCs, spares, PM plans, work orders) and 'inventoryX'
// (stock items, laundry batches, counts, losses, moves, GRNs). Server: Maintenance module (W2-7, /maintenance/*, 16 commands) and Stock
// module (W2-8, /stock/*, 12 commands). Wire contracts: 08 Production/src/Hcc.Contracts/{Maintenance,Stock}.
//
//   ids        work orders: prototype id = server `No` ("WO-0901", what the screens print) and ('wo', No) → numeric id is learned;
//              assets / vendors / spares / plans / AMCs: String(server id); rooms: room number (the rooms area learns room ids,
//              this area also learns them from assets and work orders); users (assignees): String(user id); stock items: the item
//              code (LIN-BS); laundry batches / counts / losses / GRNs: "LB-n" / "CNT-n" / "LOSS-n" / "GRN-n" (the server's own refs).
//   versions   every record's opaque version goes to ids.setVersion(kind, protoId, v). The prototype work-order / register / batch /
//              count commands carry no `version`, so mappers read ids.version(...) themselves and send it as expectedVersion.
//   money      DEC-063 G12: the server nulls part prices, spare costs, AMC values, stock costs and values for readers without finance
//              rights; masked values stay masked here (shown as 0 / left out: the screens already hide them for those roles).
//   masks      vendors, plans and AMCs come back empty for non-managers; the loaders never reconstruct them.
import type {State} from '../../services/mock';
import type {MNTState,MNTWorkOrder,MNTAsset,MNTAssetKind,MNTHistory,MNTStatus} from '../../domain/streams/maintenance';
import type {INVState,INVItem,INVLoc,INVKind,INVMove,INVMoveKind,INVBatch,INVCount,INVLoss,INVGrn} from '../../domain/streams/inventory';
import {HccClientError} from '../errors';
import type {SyncArea,LoaderCtx,CommandCtx} from './types';
import type {IdDirectory} from './map';
import {serverId,utcToClock,protoUserId} from './map';

// ── server DTOs (shapes of Hcc.Contracts.Maintenance / Hcc.Contracts.Stock; camelCase on the wire) ──
type Page<T>={page:number;pageSize:number;total:number;rows:T[]};
type SrvPart={spareId:number;spareName:string;qty:number;unitCostPaise:number|null};
type SrvOrder={workOrderId:number;no:string;kind:string;title:string;priority:string;status:string;roomId:number|null;roomNo:string|null;assetId:number|null;area:string|null;
  assigneeId:number|null;vendorId:number|null;createdUtc:string;createdBy:number;startedUtc:string|null;doneUtc:string|null;verifiedUtc:string|null;holdReason:string|null;
  awaitingSpareId:number|null;note:string|null;returnReason:string|null;cancelReason:string|null;repairTaskId:number|null;planId:number|null;dueDate:string|null;
  downtimeFromUtc:string|null;downtimeToUtc:string|null;parts:SrvPart[];version:string;doneBy?:number|null;verifiedBy?:number|null};
type SrvHistory={at:string;by:number;action:string;note:string|null};
type SrvOrderDetail={order:SrvOrder;history:SrvHistory[]};
type SrvAsset={assetId:number;name:string;kind:string;roomId:number|null;roomNo:string|null;area:string|null;make:string;model:string;installedOn:string|null;vendorId:number|null;status:string;version:string};
type SrvVendor={vendorId:number;name:string;trade:string;phone:string;version:string};
type SrvSpare={spareId:number;name:string;unit:string;stock:number;reorderAt:number;costPaise:number|null;version:string};
type SrvPlan={planId:number;assetId:number;title:string;everyDays:number;nextDue:string;active:boolean;lastDone:string|null;version:string};
type SrvAmc={amcId:number;vendorId:number;scope:string;from:string;to:string;visits:number;valuePaise:number|null;version:string};
type SrvRegisters={assets:SrvAsset[];vendors:SrvVendor[];spares:SrvSpare[];plans:SrvPlan[]};
type SrvAmcPage={amcs:SrvAmc[]};

type SrvItem={code:string;name:string;kind:string;unit:string;minQty:number;reorderQty:number;stdX100:number|null;par:Record<string,number>;levels:{loc:string;qty:number}[];
  supplier:string|null;unitCostPaise:number|null;laundryRatePaise:number|null;version:string};
type SrvItemsPage=Page<SrvItem>&{parSets:number};
type SrvBatch={batchId:number;vendor:string;sentDate:string;status:string;returnedDate:string|null;note:string|null;chargePaise:number|null;version:string;
  lines:{itemCode:string;sent:number;received:number|null;damaged:number|null}[]};
type SrvCount={countId:number;businessDate:string;loc:string;status:string;actorId:number;reviewerId:number|null;reason:string|null;note:string|null;version:string;
  lines:{itemCode:string;system:number;counted:number}[]};
type SrvLoss={lossId:number;businessDate:string;itemCode:string;loc:string;qty:number;reason:string;note:string|null;actorId:number;valuePaise:number|null;ref:string|null};
type SrvMove={moveId:number;businessDate:string;atUtc:string;kind:string;itemCode:string;qty:number;from:string|null;to:string|null;ref:string|null;reason:string|null;actorId:number};
type SrvSettings={parSets:number;vendorName:string|null;turnaroundDays:number};

// ── helpers ──
const PAGE=100;
/** Reads every page (newest first on the server), up to `maxPages`. */
async function allPages<T>(get:(page:number)=>Promise<Page<T>>,maxPages=10):Promise<T[]>{
  const out:T[]=[];
  for(let p=1;p<=maxPages;p++){const r=await get(p);out.push(...r.rows);if(out.length>=r.total||!r.rows.length)break;}
  return out;
}
const num=(v:unknown)=>(Number.isFinite(Number(v))?Number(v):0);
const opt=(v:number|null|undefined)=>v===null||v===undefined?undefined:String(v);
const STATUS:Record<string,MNTStatus>={Open:'Open',InProgress:'In progress',OnHold:'On hold',Done:'Done',Verified:'Verified',Cancelled:'Cancelled'};
const mntStatus=(s:string):MNTStatus=>STATUS[s]??(s as MNTStatus);
const userName=(users:State['users']|undefined,id:number|null|undefined)=>id==null?'':(users??[]).find(u=>u.id===protoUserId(id))?.name.split(' · ')[0]??`User ${id}`;

// ── maintenance mapping ──
export function assetFromServer(a:SrvAsset):MNTAsset{
  return {id:String(a.assetId),name:a.name,kind:a.kind as MNTAssetKind,room:a.roomNo??undefined,area:a.roomNo?undefined:(a.area??undefined),make:a.make??'',model:a.model??'',
    installedOn:a.installedOn??'',vendorId:opt(a.vendorId),status:a.status as MNTAsset['status']};
}
export function orderFromServer(w:SrvOrder,history:MNTHistory[],users?:State['users']):MNTWorkOrder{
  const x:MNTWorkOrder={id:w.no,kind:w.kind as MNTWorkOrder['kind'],title:w.title,priority:w.priority as MNTWorkOrder['priority'],status:mntStatus(w.status),
    room:w.roomNo??undefined,area:w.area??undefined,assetId:opt(w.assetId),assignee:opt(w.assigneeId),vendorId:opt(w.vendorId),
    createdAt:utcToClock(w.createdUtc),createdBy:protoUserId(w.createdBy),
    parts:(w.parts??[]).map(p=>({spareId:String(p.spareId),qty:p.qty,...(p.unitCostPaise===null||p.unitCostPaise===undefined?{}:{unitCost:p.unitCostPaise})})) as MNTWorkOrder['parts'],
    history:history.length?history:[]};
  if(w.startedUtc)x.startedAt=utcToClock(w.startedUtc);
  if(w.doneUtc)x.doneAt=utcToClock(w.doneUtc);
  if(w.doneBy!=null)x.doneBy=protoUserId(w.doneBy);
  if(w.verifiedUtc)x.verifiedAt=utcToClock(w.verifiedUtc);
  if(w.verifiedBy!=null)x.verifiedBy=protoUserId(w.verifiedBy);
  if(w.holdReason)x.holdReason=w.holdReason;
  if(w.awaitingSpareId!=null)x.awaitingSpare=String(w.awaitingSpareId);
  if(w.note)x.note=w.note;
  if(w.returnReason)x.returnReason=w.returnReason;
  if(w.cancelReason)x.cancelReason=w.cancelReason;
  if(w.repairTaskId!=null)x.repairTaskId=String(w.repairTaskId);
  if(w.planId!=null)x.planId=String(w.planId);
  if(w.dueDate)x.dueDate=w.dueDate;
  if(w.downtimeFromUtc)x.downtimeFrom=utcToClock(w.downtimeFromUtc);
  if(w.downtimeToUtc)x.downtimeTo=utcToClock(w.downtimeToUtc);
  void users;
  return x;
}
const historyFromServer=(h:SrvHistory[],users?:State['users']):MNTHistory[]=>h.map(e=>({at:utcToClock(e.at),by:userName(users,e.by),action:e.action,...(e.note?{note:e.note}:{})}));

async function loadMaintenance(c:LoaderCtx):Promise<MNTState>{
  const [reg,orders,amcs]=await Promise.all([
    c.api.get<SrvRegisters>('/maintenance/registers'),
    allPages<SrvOrder>(p=>c.api.get<Page<SrvOrder>>('/maintenance/work-orders',{page:p,pageSize:PAGE})),
    c.can('inspect')&&c.mayRead('GET /maintenance/amcs')?c.api.get<SrvAmcPage>('/maintenance/amcs'):Promise.resolve<SrvAmcPage>({amcs:[]}),
  ]);
  const prev=c.current('maintenanceX')?.orders??[];const users=c.current('users');
  for(const a of reg.assets){c.ids.setVersion('asset',String(a.assetId),a.version);if(a.roomNo&&a.roomId)c.ids.learn('room',a.roomNo,a.roomId);}
  reg.vendors.forEach(v=>c.ids.setVersion('vendor',String(v.vendorId),v.version));
  reg.spares.forEach(s=>c.ids.setVersion('spare',String(s.spareId),s.version));
  reg.plans.forEach(p=>c.ids.setVersion('plan',String(p.planId),p.version));
  amcs.amcs.forEach(a=>c.ids.setVersion('amc',String(a.amcId),a.version));
  // History is only on the detail read: fetched when a work order is new or its version changed, otherwise kept.
  const out:MNTWorkOrder[]=new Array(orders.length);
  const todo:number[]=[];
  orders.forEach((w,i)=>{
    const before=prev.find(p=>p.id===w.no),same=before&&c.ids.version('wo',w.no)===w.version&&before.history.length>0;
    if(w.roomNo&&w.roomId)c.ids.learn('room',w.roomNo,w.roomId);
    if(same){out[i]={...orderFromServer(w,before!.history,users)};}else todo.push(i);
  });
  for(let i=0;i<todo.length;i+=8){
    await Promise.all(todo.slice(i,i+8).map(async j=>{
      const w=orders[j];
      let hist:MNTHistory[]=[];
      try{const d=await c.api.get<SrvOrderDetail>(`/maintenance/work-orders/${w.workOrderId}`);hist=historyFromServer(d.history??[],users);}
      catch(e){if(!(e instanceof HccClientError)||e.wireCode!=='NOT_FOUND')throw e;}
      out[j]=orderFromServer(w,hist,users);
    }));
  }
  orders.forEach(w=>{c.ids.learn('wo',w.no,w.workOrderId);c.ids.setVersion('wo',w.no,w.version);});
  return {assets:reg.assets.map(assetFromServer),
    vendors:reg.vendors.map(v=>({id:String(v.vendorId),name:v.name,trade:v.trade,phone:v.phone})),
    amcs:amcs.amcs.map(a=>({id:String(a.amcId),vendorId:String(a.vendorId),scope:a.scope,from:a.from,to:a.to,visits:a.visits,value:num(a.valuePaise)})),
    spares:reg.spares.map(s=>({id:String(s.spareId),name:s.name,unit:s.unit,stock:s.stock,reorderAt:s.reorderAt,cost:num(s.costPaise)})),
    plans:reg.plans.map(p=>({id:String(p.planId),assetId:String(p.assetId),title:p.title,everyDays:p.everyDays,nextDue:p.nextDue,active:p.active,...(p.lastDone?{lastDone:p.lastDone}:{})})),
    orders:out.sort((a,b)=>a.id<b.id?-1:a.id>b.id?1:0)};
}

// ── stock mapping ──
const LOCS:INVLoc[]=['store','floor1','floor2','floor3','rooms','soiled','laundry'];
export function itemFromServer(i:SrvItem):INVItem{
  const stock:Partial<Record<INVLoc,number>>={};
  for(const l of i.levels??[])if(LOCS.includes(l.loc as INVLoc)&&l.qty)stock[l.loc as INVLoc]=l.qty;
  const it:INVItem={id:i.code,name:i.name,kind:i.kind as INVKind,unit:i.unit,cost:num(i.unitCostPaise),min:i.minQty,reorderQty:i.reorderQty,stock};
  if(i.stdX100!==null&&i.stdX100!==undefined)it.stdX100=i.stdX100;
  if(i.kind==='linen'&&i.par&&Object.keys(i.par).length)it.par={...i.par};
  if(i.laundryRatePaise!==null&&i.laundryRatePaise!==undefined)it.laundryRate=i.laundryRatePaise;
  if(i.supplier)it.supplier=i.supplier;
  return it;
}
const costOf=(items:INVItem[],code:string)=>items.find(i=>i.id===code)?.cost??0;

async function loadInventory(c:LoaderCtx):Promise<INVState>{
  const [items,batches,counts,losses,moves,settings]=await Promise.all([
    allPages<SrvItem>(p=>c.api.get<SrvItemsPage>('/stock/items',{page:p,pageSize:PAGE})),
    allPages<SrvBatch>(p=>c.api.get<Page<SrvBatch>>('/stock/laundry',{page:p,pageSize:PAGE})),
    allPages<SrvCount>(p=>c.api.get<Page<SrvCount>>('/stock/counts',{page:p,pageSize:PAGE})),
    allPages<SrvLoss>(p=>c.api.get<Page<SrvLoss>>('/stock/losses',{page:p,pageSize:PAGE})),
    allPages<SrvMove>(p=>c.api.get<Page<SrvMove>>('/stock/moves',{page:p,pageSize:PAGE}),5),
    c.api.get<SrvSettings>('/stock/settings').catch(e=>{if(e instanceof HccClientError&&e.wireCode==='FORBIDDEN')return null;throw e;}),
  ]);
  items.forEach(i=>c.ids.setVersion('item',i.code,i.version));
  const invItems=items.map(itemFromServer);
  const fallbackCost=(code:string)=>costOf(invItems,code);
  batches.forEach(b=>{c.ids.learn('batch','LB-'+b.batchId,b.batchId);c.ids.setVersion('batch','LB-'+b.batchId,b.version);});
  counts.forEach(k=>{c.ids.learn('count','CNT-'+k.countId,k.countId);c.ids.setVersion('count','CNT-'+k.countId,k.version);});
  const idN=(s:string)=>Number(s.replace(/^\D+/,''));
  const mapBatch=(b:SrvBatch):INVBatch=>({id:'LB-'+b.batchId,vendor:b.vendor,sent:b.sentDate,sentAt:b.sentDate+'T00:00',status:b.status as INVBatch['status'],
    lines:b.lines.map(l=>({item:l.itemCode,sent:l.sent,...(l.received!==null&&l.received!==undefined?{received:l.received}:{}),...(l.damaged!==null&&l.damaged!==undefined?{damaged:l.damaged}:{})})),
    actor:'',...(b.returnedDate?{returned:b.returnedDate}:{}),...(b.note?{note:b.note}:{}),...(b.chargePaise!=null?{charge:b.chargePaise}:{})});
  const mapCount=(k:SrvCount):INVCount=>({id:'CNT-'+k.countId,date:k.businessDate,at:k.businessDate+'T00:00',loc:k.loc as INVLoc,status:k.status as INVCount['status'],actor:protoUserId(k.actorId),
    lines:k.lines.map(l=>({item:l.itemCode,system:l.system,counted:l.counted})),...(k.reviewerId!=null?{reviewer:protoUserId(k.reviewerId)}:{}),
    ...(k.reason?{reason:k.reason}:{}),...(k.note?{note:k.note}:{})});
  const mapLoss=(l:SrvLoss):INVLoss=>({id:'LOSS-'+l.lossId,date:l.businessDate,item:l.itemCode,loc:l.loc,qty:l.qty,reason:l.reason,actor:protoUserId(l.actorId),
    value:l.valuePaise??l.qty*fallbackCost(l.itemCode),...(l.note?{note:l.note}:{}),...(l.ref?{ref:l.ref}:{})});
  const mapMove=(m:SrvMove):INVMove=>({id:'IMV-'+m.moveId,date:m.businessDate,at:utcToClock(m.atUtc),kind:m.kind as INVMoveKind,item:m.itemCode,qty:m.qty,actor:protoUserId(m.actorId),
    ...(m.from?{from:m.from}:{}),...(m.to?{to:m.to}:{}),...(m.ref?{ref:m.ref}:{}),...(m.reason?{reason:m.reason}:{})});
  const movesAsc=moves.map(mapMove).sort((a,b)=>idN(a.id)-idN(b.id));
  // GRNs have no list endpoint: rebuilt from the "grn" moves (ref GRN-n; the supplier is the move's `from`).
  const grnMap:Record<string,INVGrn>={};
  for(const m of movesAsc.filter(m=>m.kind==='grn'&&m.ref)){
    const g=grnMap[m.ref!]??={id:m.ref!,date:m.date,supplier:m.from??'',ref:m.ref!,lines:[],actor:m.actor};
    g.lines.push({item:m.item,qty:m.qty,cost:costOf(invItems,m.item)});
  }
  const st:INVState={items:invItems,batches:batches.map(mapBatch).sort((a,b)=>idN(a.id)-idN(b.id)),counts:counts.map(mapCount).sort((a,b)=>idN(a.id)-idN(b.id)),
    losses:losses.map(mapLoss).sort((a,b)=>idN(a.id)-idN(b.id)),moves:movesAsc,grns:Object.values(grnMap).sort((a,b)=>idN(a.id)-idN(b.id)),occHistory:{}};
  if(settings){st.parSets=settings.parSets;if(settings.vendorName)st.vendor={name:settings.vendorName,turnaroundDays:settings.turnaroundDays};}
  return st;
}

// ── command helpers ──
const text=(v:unknown)=>{const t=String(v??'').trim();return t||undefined;};
/** Server id of a learned record, or a VALIDATION refusal before any request. */
function known(ids:IdDirectory,kind:string,id:unknown,what:string):number{
  const n=ids.server(kind,String(id??''));
  if(n===undefined)throw new HccClientError('VALIDATION',`${what} ${id??''} is not known on the server yet. Refresh and try again.`.replace('  ',' '));
  return n;
}
function version(ids:IdDirectory,kind:string,id:unknown,what:string):string{
  const v=ids.version(kind,String(id??''));
  if(!v)throw new HccClientError('VALIDATION',`${what} ${id??''} has no server version yet. Refresh and try again.`.replace('  ',' '));
  return v;
}
const roomId=(c:CommandCtx,room:unknown)=>room?known(c.ids,'room',room,'Room'):undefined;
const wo=(c:CommandCtx)=>({workOrderId:known(c.ids,'wo',c.cmd.id,'Work order')});
const woVer=(c:CommandCtx)=>({expectedVersion:version(c.ids,'wo',c.cmd.id,'Work order')});
export const maintenance:SyncArea={
  id:'maintenance',
  loaders:[
    {slice:'maintenanceX',requires:'housekeeping',reads:['GET /maintenance/registers','GET /maintenance/work-orders','GET /maintenance/work-orders/:id'],load:loadMaintenance},
    {slice:'inventoryX',requires:['housekeeping','reports','expenses'],reads:['GET /stock/items','GET /stock/laundry','GET /stock/counts','GET /stock/losses','GET /stock/moves','GET /stock/settings'],load:loadInventory},
  ],
  commands:[
    // ── work orders ──
    {type:'MNT_WO_CREATE',reload:['maintenanceX'],
      precheck:cmd=>String(cmd.title??'').trim().length<3?'Describe the fault (at least 3 characters).':!cmd.priority?'Choose a priority (P1–P4).':(!cmd.room&&!cmd.area&&!cmd.assetId)?'Choose a room, an area or an asset.':undefined,
      async send(c){const m=c.cmd;
        await c.api.command('CreateWorkOrder',{title:String(m.title).trim(),priority:m.priority,roomId:roomId(c,m.room),assetId:m.assetId?serverId(m.assetId,'Asset'):undefined,
          area:m.room?undefined:text(m.area),notes:text(m.notes),assigneeId:m.assignee?serverId(m.assignee,'User'):undefined});}},
    {type:'MNT_WO_FROM_REPAIR',reload:['maintenanceX'],
      async send(c){const m=c.cmd;
        await c.api.command('ConvertRepairToWorkOrder',{taskId:serverId(m.taskId,'Repair request'),priority:m.priority||undefined,assetId:m.assetId?serverId(m.assetId,'Asset'):undefined});return {reload:['tasks']};}},
    {type:'MNT_WO_ASSIGN',reload:['maintenanceX'],
      async send(c){const m=c.cmd;
        await c.api.command('AssignWorkOrder',{...wo(c),technicianId:m.vendorId?undefined:serverId(m.assignee,'Technician'),vendorId:m.vendorId?serverId(m.vendorId,'Vendor'):undefined},woVer(c));return {reload:['tasks']};}},
    {type:'MNT_WO_START',reload:['maintenanceX'],async send(c){await c.api.command('StartWorkOrder',{...wo(c)},woVer(c));return {reload:['tasks']};}},
    {type:'MNT_WO_HOLD',reload:['maintenanceX'],
      precheck:cmd=>String(cmd.reason??'').trim().length<3?'Enter why the work is on hold.':undefined,
      async send(c){await c.api.command('HoldWorkOrder',{...wo(c),reason:String(c.cmd.reason).trim(),spareId:c.cmd.spareId?serverId(c.cmd.spareId,'Spare'):undefined},woVer(c));}},
    {type:'MNT_WO_PART',reload:['maintenanceX'],
      precheck:cmd=>!(Number.isInteger(cmd.qty)&&cmd.qty>=1&&cmd.qty<=99)?'Enter a quantity between 1 and 99.':undefined,
      async send(c){await c.api.command('AddWorkOrderPart',{...wo(c),spareId:serverId(c.cmd.spareId,'Spare'),qty:Number(c.cmd.qty)},woVer(c));}},
    {type:'MNT_WO_COMPLETE',reload:['maintenanceX'],
      precheck:cmd=>String(cmd.note??'').trim().length<3?'Describe the work done (at least 3 characters).':undefined,
      async send(c){await c.api.command('CompleteWorkOrder',{...wo(c),note:String(c.cmd.note).trim()},woVer(c));return {reload:['tasks']};}},
    {type:'MNT_WO_VERIFY',reload:['maintenanceX'],
      precheck:cmd=>!cmd.approve&&String(cmd.reason??'').trim().length<3?'Enter why the work is returned.':undefined,
      async send(c){const m=c.cmd;
        await c.api.command('VerifyWorkOrder',{...wo(c),approve:!!m.approve,reason:m.approve?undefined:String(m.reason).trim(),unblock:!!m.unblock},woVer(c));return {reload:['rooms']};}},
    {type:'MNT_WO_CANCEL',reload:['maintenanceX'],
      precheck:cmd=>String(cmd.reason??'').trim().length<3?'Enter a cancellation reason.':undefined,
      async send(c){await c.api.command('CancelWorkOrder',{...wo(c),reason:String(c.cmd.reason).trim()},woVer(c));}},
    {type:'MNT_WO_BLOCK',reload:['maintenanceX'],
      async send(c){await c.api.command('BlockWorkOrderRoom',{...wo(c),to:c.cmd.to||undefined},woVer(c));return {reload:['rooms']};}},
    {type:'MNT_PM_GENERATE',reload:['maintenanceX'],async send(c){await c.api.command('GeneratePmWorkOrders',{});}},
    // ── registers ──
    {type:'MNT_PLAN_SAVE',reload:['maintenanceX'],
      precheck:cmd=>String(cmd.title??'').trim().length<3?'Enter the PM task.':!(Number.isInteger(cmd.everyDays)&&cmd.everyDays>=1&&cmd.everyDays<=365)?'Repeat every 1–365 days.':undefined,
      async send(c){const m=c.cmd;
        await c.api.command('SavePmPlan',{planId:m.id?serverId(m.id,'Plan'):undefined,assetId:serverId(m.assetId,'Asset'),title:String(m.title).trim(),everyDays:Number(m.everyDays),nextDue:m.nextDue,active:m.active!==false},
          m.id?{expectedVersion:version(c.ids,'plan',m.id,'Plan')}:undefined);}},
    {type:'MNT_ASSET_SAVE',reload:['maintenanceX'],
      precheck:cmd=>String(cmd.name??'').trim().length<2?'Enter the asset name.':!cmd.room&&!String(cmd.area??'').trim()?'Choose a room or enter an area.':undefined,
      async send(c){const m=c.cmd;
        await c.api.command('SaveMaintenanceAsset',{assetId:m.id?serverId(m.id,'Asset'):undefined,name:String(m.name).trim(),kind:m.kind,roomId:roomId(c,m.room),area:m.room?undefined:text(m.area),
          make:text(m.make),model:text(m.model),installedOn:text(m.installedOn),vendorId:m.vendorId?serverId(m.vendorId,'Vendor'):undefined,status:m.status??'Working'},
          m.id?{expectedVersion:version(c.ids,'asset',m.id,'Asset')}:undefined);}},
    {type:'MNT_VENDOR_SAVE',reload:['maintenanceX'],
      precheck:cmd=>String(cmd.name??'').trim().length<3?'Enter the vendor name.':!/^[+\d][\d\s-]{6,}$/.test(String(cmd.phone??'').trim())?'Enter a phone number.':undefined,
      async send(c){const m=c.cmd;
        await c.api.command('SaveMaintenanceVendor',{vendorId:m.id?serverId(m.id,'Vendor'):undefined,name:String(m.name).trim(),trade:text(m.trade),phone:String(m.phone).trim()},
          m.id?{expectedVersion:version(c.ids,'vendor',m.id,'Vendor')}:undefined);}},
    {type:'MNT_AMC_SAVE',reload:['maintenanceX'],
      precheck:cmd=>String(cmd.scope??'').trim().length<3?'Describe what the AMC covers.':!(cmd.to>cmd.from)?'The AMC must end after it starts.':!(Number.isSafeInteger(cmd.value)&&cmd.value>0)?'Enter the contract value.':undefined,
      async send(c){const m=c.cmd;
        await c.api.command('SaveAmc',{amcId:m.id?serverId(m.id,'AMC'):undefined,vendorId:serverId(m.vendorId,'Vendor'),scope:String(m.scope).trim(),from:m.from,to:m.to,visits:Number(m.visits),valuePaise:m.value},
          m.id?{expectedVersion:version(c.ids,'amc',m.id,'AMC')}:undefined);}},
    {type:'MNT_SPARE_RECEIVE',reload:['maintenanceX'],
      precheck:cmd=>!(Number.isInteger(cmd.qty)&&cmd.qty>=1&&cmd.qty<=999)?'Enter a quantity between 1 and 999.':undefined,
      async send(c){await c.api.command('ReceiveSpare',{spareId:serverId(c.cmd.spareId,'Spare'),qty:Number(c.cmd.qty)},{expectedVersion:version(c.ids,'spare',c.cmd.spareId,'Spare')});}},

    // ── linen & stock ──
    {type:'INV_GRN',reload:['inventoryX'],
      precheck:cmd=>String(cmd.supplier??'').trim().length<2?'Enter the supplier.':!Array.isArray(cmd.lines)||!cmd.lines.length?'Add at least one item line.':undefined,
      async send(c){const m=c.cmd;
        await c.api.command('StockReceiveGrn',{supplier:String(m.supplier).trim(),ref:text(m.ref),
          lines:(m.lines as {item:string;qty:number;cost?:number}[]).map(l=>({itemCode:l.item,qty:l.qty,unitCostPaise:l.cost??undefined}))});}},
    {type:'INV_ISSUE',reload:['inventoryX'],
      async send(c){const m=c.cmd;await c.api.command('StockIssue',{itemCode:m.item,qty:m.qty,from:m.from,to:m.to});}},
    {type:'INV_USE',reload:['inventoryX'],
      async send(c){const m=c.cmd;await c.api.command('StockUse',{itemCode:m.item,qty:m.qty,floor:m.floor,room:m.room?String(m.room):undefined});}},
    {type:'INV_ROOM_CHANGE',reload:['inventoryX'],
      async send(c){const m=c.cmd;await c.api.command('StockRoomChange',{itemCode:m.item,qty:m.qty,floor:m.floor,room:m.room?String(m.room):undefined});}},
    {type:'INV_LAUNDRY_SEND',reload:['inventoryX'],
      precheck:cmd=>!Array.isArray(cmd.lines)||!cmd.lines.length?'Add at least one item line.':undefined,
      async send(c){const m=c.cmd;
        await c.api.command('StockLaundrySend',{lines:(m.lines as {item:string;qty:number}[]).map(l=>({itemCode:l.item,qty:l.qty})),note:text(m.note)});}},
    {type:'INV_LAUNDRY_RECEIVE',reload:['inventoryX'],
      async send(c){const m=c.cmd;
        await c.api.command('StockLaundryReceive',{batchId:known(c.ids,'batch',m.id,'Laundry batch'),
          lines:(m.lines as {item:string;received:number;damaged?:number}[]).map(l=>({itemCode:l.item,received:l.received,damaged:l.damaged??0})),note:text(m.note)},
          {expectedVersion:version(c.ids,'batch',m.id,'Laundry batch')});}},
    {type:'INV_LOSS',reload:['inventoryX'],
      precheck:cmd=>cmd.reason==='Other'&&String(cmd.note??'').trim().length<3?'Describe the reason.':undefined,
      async send(c){const m=c.cmd;await c.api.command('StockRecordLoss',{itemCode:m.item,qty:m.qty,loc:m.loc,reason:m.reason,note:text(m.note)});}},
    {type:'INV_COUNT_SUBMIT',reload:['inventoryX'],
      async send(c){const m=c.cmd;await c.api.command('StockCountSubmit',{loc:m.loc,counts:m.counts,note:text(m.note)});}},
    {type:'INV_COUNT_POST',reload:['inventoryX'],
      precheck:cmd=>String(cmd.reason??'').trim().length<3?'A reason is required.':undefined,
      async send(c){const m=c.cmd;
        await c.api.command('StockCountDecide',{countId:known(c.ids,'count',m.id,'Count'),approve:!!m.approve,reason:String(m.reason).trim()},{expectedVersion:version(c.ids,'count',m.id,'Count')});}},
    {type:'INV_ITEM_SET',reload:['inventoryX'],
      async send(c){const m=c.cmd;
        await c.api.command('StockItemSet',{code:m.id,minQty:m.min,reorderQty:m.reorderQty,stdX100:m.stdX100,par:m.par},{expectedVersion:version(c.ids,'item',m.id,'Stock item')});}},
  ],
  unmapped:{},
};
