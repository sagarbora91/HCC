// Catalogue area (MA-2): meal plans, extras catalogue, discount reasons on the server module Catalogue (Hcc.Contracts/Catalogue).
//
//   server   GET /catalogue/{meal-plans|extras|discount-reasons} (PERM-FOL-001 — prices are hotel money, DEC-061), …/history (PERM-MST-002),
//            GET /catalogue/links (plans of bookings + catalogue links of charges, PERM-FOL-001);
//            POST /commands/{Save|Retire|Reactivate|Delete}{MealPlan|ExtraItem|DiscountReason}, Reorder{MealPlans|ExtraItems|DiscountReasons},
//            PostExtra, RequestReasonedDiscount, SetReservationMealPlan (PostMealPlanNights is run by the night posting on the server).
//   loaders  mastersX (patch) ← the three lists (a role without the read gets an empty list, never the demo seed) + history (Owner);
//            bookings (patch, after the money area's lines) ← booking.plan, line.extraId / planCode / discountReason from the links.
//   commands CAT_CHARGE → PostExtra · CAT_DISCOUNT → RequestReasonedDiscount (folio version) · CAT_PLAN_SET → SetReservationMealPlan;
//            MST_MASTER_* for the three lists arrive through the masters area (CAT_MST_DELEGATES, masters.ts MST_DELEGATES).
import type {Booking,Role} from '../../services/mock';
import type {MasterRow,MSTHistory,MSTState} from '../../domain/streams/masters';
import {HccClientError} from '../errors';
import type {SyncArea,LoaderCtx,CommandCtx} from './types';
import type {ReadRoute} from './reads';
import type {MstDelegate} from './masters';
import {serverId,protoId,protoUserId,utcToClock} from './map';

type SrvDated={effectiveFrom:string;amountPaise:number};
type SrvBase={code:string;name:string;sortOrder:number;active:boolean;retiredOn:string|null;usageCount:number;updatedUtc:string;version:string};
type SrvMealPlan=SrvBase&{mealPlanId:number;meals:string[];gstTreatment:string;roomTypes:string[];addOn:SrvDated[]};
type SrvExtra=SrvBase&{extraItemId:number;category:string;gstCategory:string;unit:string;department:string|null;prices:SrvDated[]};
type SrvReason=SrvBase&{discountReasonId:number;maxBps:number;approvalAboveBps:number|null;roles:string[]};
type SrvPage<T>={page:number;pageSize:number;total:number;items:T[]};
type SrvHistory={historyId:number;rowId:number;code:string|null;field:string;oldValue:string|null;newValue:string|null;changedBy:number;changedByName:string|null;changedUtc:string};
type SrvLinks={plans:{reservationId:number;mealPlanCode:string}[];charges:{chargeId:number;extraItemId:number|null;mealPlanCode:string|null;discountReasonCode:string|null}[]};

const ROLE_OF:Record<string,Role>={OWN:'Owner',MGR:'Manager',REC:'Reception'};
const CODE_OF:Record<string,string>={Owner:'OWN',Manager:'MGR',Reception:'REC'};
const dated=(l:SrvDated[])=>l.map(x=>({amount:x.amountPaise,effectiveFrom:x.effectiveFrom}));
const toDated=(v:unknown)=>(Array.isArray(v)?v:[]).map((x:{amount?:unknown;effectiveFrom?:unknown})=>({effectiveFrom:String(x?.effectiveFrom??''),amountPaise:Number(x?.amount)}));
function base(id:number,x:SrvBase):MasterRow{const r:MasterRow={id:protoId(id),code:x.code,name:x.name,sortOrder:x.sortOrder,active:x.active};if(x.retiredOn)r.retiredOn=x.retiredOn;return r;}
export function mealPlanFromServer(x:SrvMealPlan):MasterRow{return {...base(x.mealPlanId,x),meals:[...x.meals],addOn:dated(x.addOn),gst:x.gstTreatment,roomTypes:[...x.roomTypes]};}
export function extraFromServer(x:SrvExtra):MasterRow{
  const r:MasterRow={...base(x.extraItemId,x),category:x.category,price:dated(x.prices),gst:x.gstCategory,unit:x.unit};if(x.department)r.department=x.department;return r;}
export function reasonFromServer(x:SrvReason):MasterRow{
  const r:MasterRow={...base(x.discountReasonId,x),maxPct:x.maxBps,roles:x.roles.map(c=>ROLE_OF[c]).filter(Boolean)};if(x.approvalAboveBps!==null)r.approvalAbovePct=x.approvalAboveBps;return r;}

/** One catalogue list on the server: routes, command names, id fields, wire mapping. */
type CatRoute={list:ReadRoute;history:ReadRoute;listPath:string;historyPath:string;kind:string;what:string;idField:string;idsField:string;
  save:string;retire:string;reactivate:string;remove:string;reorder:string;fromServer:(x:never)=>MasterRow;body:(row:Record<string,unknown>)=>Record<string,unknown>};
const text=(v:unknown)=>String(v??'').trim();
export const CAT_SERVER:Record<string,CatRoute>={
  mealPlans:{list:'GET /catalogue/meal-plans',history:'GET /catalogue/meal-plans/history',listPath:'/catalogue/meal-plans',historyPath:'/catalogue/meal-plans/history',
    kind:'mealPlan',what:'Meal plan',idField:'mealPlanId',idsField:'mealPlanIds',save:'SaveMealPlan',retire:'RetireMealPlan',reactivate:'ReactivateMealPlan',
    remove:'DeleteMealPlan',reorder:'ReorderMealPlans',fromServer:mealPlanFromServer as (x:never)=>MasterRow,
    body:row=>({meals:Array.isArray(row.meals)?row.meals:[],gstTreatment:text(row.gst),roomTypes:Array.isArray(row.roomTypes)?row.roomTypes:[],addOn:toDated(row.addOn)})},
  extras:{list:'GET /catalogue/extras',history:'GET /catalogue/extras/history',listPath:'/catalogue/extras',historyPath:'/catalogue/extras/history',
    kind:'extraItem',what:'Extra item',idField:'extraItemId',idsField:'extraItemIds',save:'SaveExtraItem',retire:'RetireExtraItem',reactivate:'ReactivateExtraItem',
    remove:'DeleteExtraItem',reorder:'ReorderExtraItems',fromServer:extraFromServer as (x:never)=>MasterRow,
    body:row=>({category:text(row.category),gstCategory:text(row.gst),unit:text(row.unit),department:text(row.department)||null,prices:toDated(row.price)})},
  discountReasons:{list:'GET /catalogue/discount-reasons',history:'GET /catalogue/discount-reasons/history',listPath:'/catalogue/discount-reasons',
    historyPath:'/catalogue/discount-reasons/history',kind:'discountReason',what:'Discount reason',idField:'discountReasonId',idsField:'discountReasonIds',
    save:'SaveDiscountReason',retire:'RetireDiscountReason',reactivate:'ReactivateDiscountReason',remove:'DeleteDiscountReason',reorder:'ReorderDiscountReasons',
    fromServer:reasonFromServer as (x:never)=>MasterRow,
    body:row=>({maxBps:Number(row.maxPct),approvalAboveBps:row.approvalAbovePct===undefined||row.approvalAbovePct===null||row.approvalAbovePct===''?null:Number(row.approvalAbovePct),
      roles:(Array.isArray(row.roles)?row.roles:[]).map(r=>CODE_OF[String(r)]).filter(Boolean)})},
};
const PAGE=200;
async function pages<T>(c:LoaderCtx,path:string):Promise<T[]>{
  let rows:T[]=[];
  for(let page=1;page<=50;page++){const p=await c.api.get<SrvPage<T>>(path,{page,pageSize:PAGE});rows=rows.concat(p.items);if(!p.items.length||rows.length>=p.total)break;}
  return rows;
}
function historyFromServer(master:string,h:SrvHistory):MSTHistory{
  return {id:protoId(h.historyId),master,rowId:protoId(h.rowId),code:h.code??'',field:h.field,old:h.oldValue??'—',new:h.newValue??'—',by:protoUserId(h.changedBy),at:utcToClock(h.changedUtc)};
}
async function patchMasters(c:LoaderCtx):Promise<MSTState>{
  const cur=c.current('mastersX')??{rows:{},history:[]};
  const rows:Record<string,MasterRow[]>={...(cur.rows??{})},history:MSTHistory[]=[...(cur.history??[])];
  for(const [master,r] of Object.entries(CAT_SERVER)){
    if(!c.mayRead(r.list)){rows[master]=[];continue;} // never the demo seed in production
    const items=await pages<Record<string,unknown>>(c,r.listPath);
    for(const x of items)c.ids.setVersion(r.kind,protoId(x[r.idField] as number),String(x.version));
    rows[master]=items.map(x=>r.fromServer(x as never));
    if(c.mayRead(r.history))history.push(...(await pages<SrvHistory>(c,r.historyPath)).map(h=>historyFromServer(master,h)));
  }
  history.sort((a,b)=>a.at<b.at?1:a.at>b.at?-1:Number(b.id)-Number(a.id));
  return {rows,history};
}
async function patchBookings(c:LoaderCtx):Promise<Booking[]>{
  const list=(c.current('bookings')??[]) as Booking[];
  if(!list.length||!c.mayRead('GET /catalogue/links'))return list;
  // The links only label lines (catalogue / plan / reason); a failed read must not cost the folios themselves.
  let l:SrvLinks;try{l=await c.api.get<SrvLinks>('/catalogue/links');}catch{return list;}
  const plan=new Map(l.plans.map(p=>[protoId(p.reservationId),p.mealPlanCode]));
  const charge=new Map(l.charges.map(x=>[protoId(x.chargeId),x]));
  return list.map(b=>{
    const p=plan.get(b.id),hit=b.lines.some(x=>charge.has(x.id));
    if(!p&&!b.plan&&!hit)return b;
    const next:Booking={...b,lines:hit?b.lines.map(x=>{const k=charge.get(x.id);if(!k)return x;const y={...x};
      if(k.extraItemId!==null)y.extraId=protoId(k.extraItemId);if(k.mealPlanCode)y.planCode=k.mealPlanCode;if(k.discountReasonCode)y.discountReason=k.discountReasonCode;return y;}):b.lines};
    if(p)next.plan=p;else delete next.plan;
    return next;
  });
}

// ── commands ──
const routeOf=(c:CommandCtx)=>{const r=CAT_SERVER[String(c.cmd.master)];if(!r)throw new HccClientError('VALIDATION','Unknown list.');return r;};
const idOf=(r:CatRoute,id:unknown)=>{if(id===undefined||id===null||id==='')throw new HccClientError('VALIDATION','Choose a row.');return serverId(String(id),r.what);};
/** MST_MASTER_* of the three lists (called by the masters area; one mapper per command type stays there). */
const DELEGATE:MstDelegate={
  async save(c:CommandCtx){const r=routeOf(c),row=(c.cmd.row??{}) as Record<string,unknown>;
    const body:Record<string,unknown>={code:text(row.code).toUpperCase(),name:text(row.name),...r.body(row)};
    if(row.sortOrder!==undefined&&row.sortOrder!==null&&row.sortOrder!=='')body.sortOrder=Number(row.sortOrder);
    let expectedVersion:string|undefined;
    if(row.id){body[r.idField]=idOf(r,row.id);expectedVersion=c.ids.version(r.kind,String(row.id));}
    return {result:await c.api.command(r.save,body,{expectedVersion})};},
  async status(c:CommandCtx,op:'retire'|'reactivate'|'remove'){const r=routeOf(c),id=String(c.cmd.id);
    await c.api.command(r[op],{[r.idField]:idOf(r,id)},{expectedVersion:c.ids.version(r.kind,id)});},
  async reorder(c:CommandCtx){const r=routeOf(c);await c.api.command(r.reorder,{[r.idsField]:(c.cmd.ids as unknown[]).map(id=>idOf(r,id))});},
};
export const CAT_MST_DELEGATES:Record<string,MstDelegate>={mealPlans:DELEGATE,extras:DELEGATE,discountReasons:DELEGATE};

const bookingOf=(c:CommandCtx)=>{const b=c.state.bookings.find(x=>x.id===String(c.cmd.id??''));if(!b)throw new HccClientError('NOT_FOUND','Booking not found.');return b;};
function folioOf(c:CommandCtx,b:Booking){
  const folioId=c.ids.server('folio',b.id),version=c.ids.version('folio',b.id);
  if(folioId===undefined||!version)throw new HccClientError('INVALID_TRANSITION','This booking has no folio on the server yet — it opens at check-in. Refresh and try again.');
  return {folioId,version};
}
const positive=(v:unknown)=>Number.isSafeInteger(v)&&(v as number)>0;
const opt=(v:unknown)=>v===undefined||v===null||v===''?null:v;

export const catalogue:SyncArea={
  id:'catalogue',
  loaders:[
    {slice:'mastersX',kind:'patch',load:patchMasters},
    {slice:'bookings',kind:'patch',reads:['GET /catalogue/links'],load:patchBookings},
  ],
  commands:[
    {type:'CAT_CHARGE',reload:['bookings','approvals'],
      precheck:cmd=>!cmd.extraId?'Choose an item from the catalogue.':cmd.amount!==undefined&&!positive(cmd.amount)?'Enter an amount greater than zero.':undefined,
      async send(c){const b=bookingOf(c),f=folioOf(c,b);
        return {result:await c.api.command('PostExtra',{folioId:f.folioId,extraItemId:serverId(String(c.cmd.extraId),'Extra item'),quantity:opt(c.cmd.qty),
          label:opt(text(c.cmd.label)),amountPaise:opt(c.cmd.amount),gstCategory:opt(c.cmd.gst)})};}},
    {type:'CAT_DISCOUNT',reload:['bookings','approvals'],
      precheck:cmd=>!positive(cmd.amount)?'Enter an amount greater than zero.':!text(cmd.reasonCode)?'Choose a discount reason.':undefined,
      async send(c){const b=bookingOf(c),f=folioOf(c,b);
        return {result:await c.api.command('RequestReasonedDiscount',{folioId:f.folioId,amountPaise:c.cmd.amount,reasonCode:text(c.cmd.reasonCode).toUpperCase(),
          note:opt(text(c.cmd.note))},{expectedVersion:f.version})};}},
    {type:'CAT_PLAN_SET',reload:['bookings'],
      async send(c){const b=bookingOf(c);
        return {result:await c.api.command('SetReservationMealPlan',{reservationId:serverId(b.id,'Booking'),mealPlanCode:opt(text(c.cmd.plan).toUpperCase())})};}},
  ],
};
