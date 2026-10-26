// POS area (W4-8): restaurant & room service — outlets, menu, checks, KOT flow, bills, tenders, voids with limits/approval, room posting
// to the folio, cash into the shared drawer. Server: PosEndpoints.cs (commands + GET /pos/*); tax comes ONLY from the server (the check's
// taxBps / taxPaise / totalPaise are what the screens show; nothing is recomputed here).
//
//   loaders   posX       ← GET /pos/outlets + /pos/menu + /pos/checks (open) + one GET /pos/checks/{id} per open check
//                          + GET /pos/checks/closed (today's settled / void checks, at most 50).
//                          Outlet receipts (posX.receipts) are derived from today's closed outlet bills (a sale; plus its reversal for a
//                          voided bill) — the cash ones are what drawerOutletCash() adds to the shared drawer figure.
//             approvals  ← pending void requests (GET /pos/void-requests) as kind 'pos-void' approvals (id "posv-<id>"). Registered as a
//                          TEMPORARY owner (nobody else owns `approvals` yet): once admin/money own it, add kind:'patch' (one word).
//   commands  POS_ORDER_OPEN → OpenCheck · POS_ITEM_ADD → AddCheckItem · POS_ITEM_QTY → SetCheckItemQty · POS_KOT_SEND → SendKot ·
//             POS_KOT_STATUS → AdvanceKot · POS_SETTLE → SettleCheck (outlet: tender + till for cash; folio: stay of the booking) ·
//             POS_VOID_ITEM → VoidCheckItem · POS_VOID_BILL → VoidBill · POS_VOID_DECIDE → DecideVoid · POS_MENU_TOGGLE → SetMenuItemAvailability.
//   ids       outlet / menu item / check / line / KOT = String(server id); void approval = "posv-<id>"; users = String(user id).
//   versions  checks: ids kind 'check' (rowversion) — sent for the state-sensitive commands (KOT, settle, void); menu items: 'menuItem'.
import type {POSState,POSOrder,POSLine,POSKot,POSReceipt,POSMenuItem,POSOutlet,POSKotStatus,Approval,Command} from '../../services/mock';
import {HccClientError} from '../errors';
import type {SyncArea,LoaderCtx,CommandCtx} from './types';
import {protoUserId,serverId,utcToClock,utcToDate} from './map';

// ── Server DTOs (08 Production/src/Hcc.Contracts/Pos/PosContracts.cs) ──
type SrvOutlet={outletId:number;code:string;name:string;kind:'table'|'room';tables:string[]};
type SrvMenuItem={itemId:number;code:string;name:string;category:string;pricePaise:number;veg:boolean;available:boolean;outletIds:number[];version:string};
type SrvOpenCheck={orderId:number};
type SrvLine={lineId:number;itemId:number;name:string;pricePaise:number;qty:number;kotId:number|null;status:string;voided:boolean;voidReason:string|null};
type SrvKot={kotId:number;kotNo:number;status:POSKotStatus;createdUtc:string;readyUtc:string|null;servedUtc:string|null;createdBy?:number|null};
type SrvBill={billId:number;billNo:string;mode:'outlet'|'folio';tender:string|null;reference:string|null;netPaise:number;taxBps:number;taxPaise:number;totalPaise:number;
  businessDate:string;status:string;settledUtc?:string|null;settledBy?:number|null;reservationId?:number|null};
type SrvCheck={orderId:number;outletId:number;outlet:string;spot:string;guest:string|null;covers:number;businessDate:string;status:'Open'|'Settled'|'Void';version:string;
  pendingVoidId:number|null;lines:SrvLine[];kots:SrvKot[];netPaise:number;taxBps:number;taxPaise:number;totalPaise:number;items:number;bill:SrvBill|null;
  openedUtc?:string|null;openedBy?:number|null;voidReason?:string|null;voidedUtc?:string|null;reservationId?:number|null};
type SrvVoidRequest={voidRequestId:number;orderId:number;spot:string;scope:'item'|'bill';lineId:number|null;amountPaise:number;reason:string;requestedBy:number;expiresUtc:string;orderVersion:string};
type SrvStay={stayId:number;folioId:number;reservationId:number;roomNo:string;guest:string};
type SrvTill={tillId:number|null;code:string|null;shiftId:number|null;shiftOpen:boolean};

const VOID_PREFIX='posv-';
const approvalId=(id:number)=>VOID_PREFIX+id;
/** "posv-12" → 12 (VALIDATION for an id the server cannot have issued). */
export function voidRequestIdOf(id:unknown):number{
  const s=String(id??'');return serverId(s.startsWith(VOID_PREFIX)?s.slice(VOID_PREFIX.length):s,'Void request');
}

// ── Mapping (exported for tests) ──
export const outletFromServer=(o:SrvOutlet):POSOutlet=>({id:String(o.outletId),name:o.name,kind:o.kind,...(o.kind==='table'?{tables:[...o.tables]}:{})});
export const menuFromServer=(m:SrvMenuItem):POSMenuItem=>({id:String(m.itemId),name:m.name,category:m.category,price:m.pricePaise,veg:m.veg,outlets:m.outletIds.map(String),available:m.available});

/** Server check → prototype POSOrder. `fallbackDate`: business date used as the void date of an item void (the server keeps no per-line void date). */
export function orderFromServer(c:SrvCheck,fallbackDate?:string):POSOrder{
  const room=c.spot.startsWith('Room ')?c.spot.slice(5):undefined,table=c.spot.startsWith('Table ')?c.spot.slice(6):undefined;
  const voidDate=c.status==='Void'&&c.voidedUtc?utcToDate(c.voidedUtc):fallbackDate??c.businessDate;
  const lines:POSLine[]=c.lines.map(l=>({id:String(l.lineId),item:String(l.itemId),name:l.name,price:l.pricePaise,qty:l.qty,
    ...(l.kotId!==null?{kot:String(l.kotId)}:{}),...(l.voided?{voided:true,voidReason:l.voidReason??'',voidDate}:{})}));
  const kots:POSKot[]=c.kots.map(k=>({id:String(k.kotId),no:k.kotNo,at:utcToClock(k.createdUtc),by:protoUserId(k.createdBy??0),status:k.status,
    ...(k.readyUtc?{readyAt:utcToClock(k.readyUtc)}:{}),...(k.servedUtc?{servedAt:utcToClock(k.servedUtc)}:{})}));
  const o:POSOrder={id:String(c.orderId),outlet:String(c.outletId),...(table!==undefined?{table}:{}),...(room!==undefined?{room}:{}),
    ...(c.reservationId?{booking:String(c.reservationId)}:{}),...(c.guest?{guest:c.guest}:{}),covers:c.covers,date:c.businessDate,openedAt:utcToClock(c.openedUtc),
    openedBy:protoUserId(c.openedBy??0),status:c.status,version:1,lines,kots,
    ...(c.pendingVoidId?{pendingVoid:approvalId(c.pendingVoidId)}:{})};
  if(c.bill){const b=c.bill;
    o.settlement={mode:b.mode,billNo:b.billNo,...(b.tender?{method:b.tender}:{}),...(b.reference?{reference:b.reference}:{}),net:b.netPaise,taxBps:b.taxBps,tax:b.taxPaise,total:b.totalPaise,
      date:b.businessDate,at:utcToClock(b.settledUtc),by:protoUserId(b.settledBy??0),
      ...(b.mode==='outlet'?{receiptId:'posr-'+b.billId}:{...(b.reservationId?{booking:String(b.reservationId)}:{}),folioLineId:'posline:'+c.orderId})};
    if(b.mode==='folio'&&b.reservationId&&!o.booking)o.booking=String(b.reservationId);
  }
  if(c.status==='Void'){o.voidReason=c.voidReason??'';if(c.voidedUtc)o.voidedAt=utcToClock(c.voidedUtc);}
  return o;
}
/** Outlet receipts of a loaded check: a sale per outlet bill and, for a voided bill, the negative reversal (as the prototype keeps them). */
export function receiptsFromServer(c:SrvCheck):POSReceipt[]{
  const b=c.bill;if(!b||b.mode!=='outlet'||!b.tender)return [];
  const base={order:String(c.orderId),outlet:String(c.outletId),method:b.tender,taxBps:b.taxBps,date:b.businessDate,actor:protoUserId(b.settledBy??0)};
  const sale:POSReceipt={...base,id:'posr-'+b.billId,amount:b.totalPaise,net:b.netPaise,tax:b.taxPaise,reference:b.reference??'Outlet cash',kind:'sale'};
  if(b.status!=='Void')return [sale];
  return [sale,{...base,id:'posr-void-'+b.billId,amount:-b.totalPaise,net:-b.netPaise,tax:-b.taxPaise,reference:'Void of '+b.billNo,kind:'void'}];
}
export const approvalFromServer=(v:SrvVoidRequest):Approval=>({id:approvalId(v.voidRequestId),booking:'',amount:v.amountPaise,reason:v.reason,status:'Pending',
  actor:protoUserId(v.requestedBy),version:1,kind:'pos-void',target:String(v.orderId),expiresAt:utcToClock(v.expiresUtc),
  meta:{command:'POS_VOID_DECIDE',scope:v.scope,summary:`${v.scope==='item'?'Void item':'Void bill'} · ${v.spot}`,...(v.lineId!==null?{line:String(v.lineId)}:{})}});

// ── Loaders ──
async function loadPos(c:LoaderCtx):Promise<POSState>{
  const [outlets,menu,open,closed]=await Promise.all([
    c.api.get<SrvOutlet[]>('/pos/outlets'),c.api.get<SrvMenuItem[]>('/pos/menu'),c.api.get<SrvOpenCheck[]>('/pos/checks'),
    c.api.get<SrvCheck[]>('/pos/checks/closed',{date:c.current('date')||undefined})]);
  for(const m of menu)c.ids.setVersion('menuItem',String(m.itemId),m.version);
  const checks:SrvCheck[]=[];
  for(let i=0;i<open.length;i+=8){
    const part=await Promise.all(open.slice(i,i+8).map(o=>c.api.get<SrvCheck>('/pos/checks/'+o.orderId).catch(e=>{
      if(e instanceof HccClientError&&e.wireCode==='NOT_FOUND')return null;throw e;})));
    for(const x of part)if(x)checks.push(x);
  }
  const seen=new Set(checks.map(x=>x.orderId)),all=[...checks,...closed.filter(x=>!seen.has(x.orderId))].sort((a,b)=>a.orderId-b.orderId);
  for(const x of all)c.ids.setVersion('check',String(x.orderId),x.version);
  const date=c.current('date')||undefined;
  return {outlets:outlets.map(outletFromServer),menu:menu.map(menuFromServer),orders:all.map(x=>orderFromServer(x,date)),
    receipts:closed.flatMap(receiptsFromServer),kotSeq:{},billSeq:0};
}
async function patchApprovals(c:LoaderCtx):Promise<Approval[]>{
  const pending=await c.api.get<SrvVoidRequest[]>('/pos/void-requests');
  return [...(c.current('approvals')??[]).filter(a=>a.kind!=='pos-void'),...pending.map(approvalFromServer)];
}

// ── Commands ──
const need=(cmd:Command,k:string,msg:string)=>{if(cmd[k]===undefined||cmd[k]===null||String(cmd[k]).trim()==='')return msg;return undefined;};
const checkId=(cmd:Command)=>serverId(cmd.id,'Check');
/** Version of the check as last loaded (optional: the server only compares it when given). */
const checkVersion=(c:CommandCtx)=>c.ids.version('check',String(c.cmd.id));
const reasonCheck=(cmd:Command)=>String(cmd.reason??'').trim().length>=3?undefined:'Enter a reason for the void (at least 3 characters).';
const posOrderOf=(c:CommandCtx)=>c.state.posX?.orders.find(o=>o.id===String(c.cmd.id));

/** Folio mode: the stay of the chosen in-house guest (booking = reservation id). */
async function stayOf(c:CommandCtx):Promise<number|undefined>{
  const booking=String(c.cmd.booking??posOrderOf(c)?.booking??'');
  if(!booking)throw new HccClientError('NOT_FOUND','Choose the in-house guest to charge.');
  const stays=await c.api.get<SrvStay[]>('/pos/in-house');
  const s=stays.find(x=>String(x.reservationId)===booking);
  if(!s)throw new HccClientError('INVALID_TRANSITION','Only an in-house guest’s folio can take a room charge.');
  return s.stayId;
}

export const pos:SyncArea={id:'pos',
  loaders:[
    {slice:'posX',requires:'billing',needs:['date'],load:loadPos},
    // TEMPORARY OWNER: no other area owns `approvals` on this branch. When the admin / money area registers its own `approvals` loader,
    // add kind:'patch' here (validateAreas refuses two owners; patchApprovals already keeps the other kinds and replaces only 'pos-void').
    {slice:'approvals',requires:'billing',load:patchApprovals},
  ],
  commands:[
    {type:'POS_ORDER_OPEN',reload:['posX'],
      precheck:cmd=>{const n=cmd.covers===undefined?1:Number(cmd.covers);if(!Number.isInteger(n)||n<1||n>20)return 'Covers must be 1–20.';
        return need(cmd,'outlet','Choose an outlet.')??(cmd.table||cmd.room?undefined:'Choose a table.');},
      async send(c){const cmd=c.cmd;
        await c.api.command('OpenCheck',{outletId:serverId(cmd.outlet,'Outlet'),...(cmd.table?{table:String(cmd.table)}:{}),...(cmd.room?{roomNo:String(cmd.room)}:{}),
          covers:cmd.covers===undefined?1:Number(cmd.covers),...(String(cmd.guest??'').trim()?{guest:String(cmd.guest).trim()}:{})});}},
    {type:'POS_ITEM_ADD',reload:['posX'],
      precheck:cmd=>{const q=cmd.qty===undefined?1:Number(cmd.qty);return Number.isInteger(q)&&q>=1&&q<=50?undefined:'Quantity must be 1–50.';},
      async send(c){await c.api.command('AddCheckItem',{orderId:checkId(c.cmd),itemId:serverId(c.cmd.item,'Menu item'),qty:c.cmd.qty===undefined?1:Number(c.cmd.qty)});}},
    {type:'POS_ITEM_QTY',reload:['posX'],
      precheck:cmd=>{const q=Number(cmd.qty);return Number.isInteger(q)&&q>=0&&q<=50?undefined:'Quantity must be 0–50.';},
      async send(c){await c.api.command('SetCheckItemQty',{orderId:checkId(c.cmd),lineId:serverId(c.cmd.line,'Line'),qty:Number(c.cmd.qty)});}},
    {type:'POS_KOT_SEND',reload:['posX'],
      async send(c){const v=checkVersion(c);await c.api.command('SendKot',{orderId:checkId(c.cmd)},v?{expectedVersion:v}:undefined);}},
    {type:'POS_KOT_STATUS',reload:['posX'],
      async send(c){await c.api.command('AdvanceKot',{orderId:checkId(c.cmd),kotId:serverId(c.cmd.kot,'KOT'),status:String(c.cmd.status)});}},
    {type:'POS_SETTLE',reload:['posX'],
      precheck:cmd=>cmd.mode==='folio'?undefined:cmd.mode!=='outlet'?'Choose how the bill is settled.':!['Cash','UPI','Card'].includes(cmd.method)?'Select Cash, UPI or Card.':
        cmd.method!=='Cash'&&!String(cmd.reference??'').trim()?'Enter a demonstration transaction reference.':undefined,
      async send(c){
        const cmd=c.cmd,v=checkVersion(c);
        if(!v)throw new HccClientError('STALE_VERSION','This check was not loaded from the server. Refresh and try again.');
        if(cmd.mode==='folio'){
          const stayId=await stayOf(c);
          await c.api.command('SettleCheck',{orderId:checkId(cmd),mode:'folio',...(stayId?{stayId}:{})},{expectedVersion:v});
          return {reload:['bookings']};}
        let tillId:number|undefined;
        if(cmd.method==='Cash'){
          // Shared till (DEC-052): outlet cash goes into the front-desk drawer, so the till must have an open shift.
          const till=await c.api.get<SrvTill>('/pos/till');
          if(till.tillId===null||!till.shiftOpen)throw new HccClientError('INVALID_TRANSITION','The cash shift is closed, so no more cash can be taken today. Settle by UPI or card, or charge the room.');
          tillId=till.tillId;}
        const reference=String(cmd.reference??'').trim();
        await c.api.command('SettleCheck',{orderId:checkId(cmd),mode:'outlet',tender:String(cmd.method),...(reference?{reference}:{}),...(tillId!==undefined?{tillId}:{})},{expectedVersion:v});
        return undefined;}},
    {type:'POS_VOID_ITEM',reload:['posX','approvals'],precheck:reasonCheck,
      async send(c){const v=checkVersion(c);
        const r=await c.api.command<{outcome:string;voidRequestId:number|null}>('VoidCheckItem',{orderId:checkId(c.cmd),lineId:serverId(c.cmd.line,'Line'),reason:String(c.cmd.reason).trim()},v?{expectedVersion:v}:undefined);
        return {result:r};}},
    {type:'POS_VOID_BILL',reload:['posX','approvals'],precheck:reasonCheck,
      async send(c){const v=checkVersion(c);
        const r=await c.api.command<{outcome:string;voidRequestId:number|null}>('VoidBill',{orderId:checkId(c.cmd),reason:String(c.cmd.reason).trim()},v?{expectedVersion:v}:undefined);
        return {result:r};}},
    {type:'POS_VOID_DECIDE',reload:['posX','approvals'],
      async send(c){const reason=String(c.cmd.reason??'').trim();
        await c.api.command('DecideVoid',{voidRequestId:voidRequestIdOf(c.cmd.id),approve:!!c.cmd.approve,...(reason?{reason}:{})});}},
    {type:'POS_MENU_TOGGLE',reload:['posX'],
      async send(c){const m=c.state.posX?.menu.find(x=>x.id===String(c.cmd.item));
        if(!m)throw new HccClientError('NOT_FOUND','Menu item not found.');
        await c.api.command('SetMenuItemAvailability',{itemId:serverId(c.cmd.item,'Menu item'),available:!m.available});}},
  ],
  unmapped:{},
};
