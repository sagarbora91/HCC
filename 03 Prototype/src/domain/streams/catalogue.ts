// MA-2 · Meal plans, extras catalogue and discount reasons (plan HCC_Customisation_Features_Plan_v1.md Part 2 §8 M6, M7, M8;
// owner decisions DEC-MA-3 meal plans now, DEC-MA-4 free text stays allowed for Reception). Three money masters on the MA-0 framework
// (domain/streams/masters.ts): every change is Owner-only (DEC-058), housekeeping / maintenance never see the amounts (DEC-061).
//
//   mealPlans        EP / CP / MAP / AP + custom: included meals, add-on per person per night (dated money), GST treatment
//                    "bundled with room" or "separate restaurant line" (Illustrative — CA to confirm), room types allowed.
//                    A booking carries `plan` (BOOK {plan} or CAT_PLAN_SET); each room night posted (commands.ts addNight → catPlanNight)
//                    posts one plan line for the night (kind service, `planCode`), EP / zero add-on posts nothing.
//   extras           code, name, category, default price (dated), GST class (the GST master keys: food / laundry / banquet / other),
//                    unit, department. CAT_CHARGE {id, extraId, qty?, label?, amount?, gst?} posts a catalogue line (`extraId`); name,
//                    price and GST class are prefilled and editable. Free text stays the plain CHARGE ("Other (describe)", no approval).
//   discountReasons  code, name, max % of the charges, approval above %, roles that may use it. CAT_DISCOUNT {id, amount, reasonCode,
//                    note?} feeds the DISCOUNT rules (role limits, approval) and records `discountReason` on the line; a free-text
//                    reason stays the plain DISCOUNT.
// Reports: catChargeSplit (catalogue vs free text), catPlanReport (by plan), catDiscountReport (by reason). Hooks for the booking
// site / OTA mapping: catPlansForSite, catPlanQuote, catPlanFromChannel (RO / BB / HB / FB / AI meal codes).
import type {State,Booking,Line,Role} from '../types';
import {totals} from '../selectors';
import {gstForRoomNight,gstForFood,gstForCharge,GST_CHARGE_CATEGORIES,GST_ILLUSTRATIVE,type GstCategory} from '../gst';
import {registerMaster,mstRows,mstByCode,mstById,mstSpec,mstMoneyOn,mstFail,type MasterRow,type MasterSpec,type FieldSpec,type MstDatedAmount} from './masters';

export const CAT_MEAL_PLANS='mealPlans';
export const CAT_EXTRAS='extras';
export const CAT_DISCOUNT_REASONS='discountReasons';
/** Seeded amounts take effect from the start of FY 2025-26 (before the FY26 history window), so every historical date resolves. */
export const CAT_SEED_FROM='2025-04-01';
type ChargeCat=Exclude<GstCategory,'room'>;

// ───────────────────────────── Row shapes ─────────────────────────────
export type CatMeal='B'|'L'|'D';
export type CatGstTreatment='bundled'|'separate';
export type CatMealPlan=MasterRow&{meals?:CatMeal[];addOn?:MstDatedAmount[];gst?:CatGstTreatment;roomTypes?:string[]};
export type CatExtra=MasterRow&{category?:string;price?:MstDatedAmount[];gst?:ChargeCat;unit?:string;department?:string};
export type CatDiscountReason=MasterRow&{maxPct?:number;approvalAbovePct?:number;roles?:Role[]};

export const CAT_MEALS:{value:CatMeal;label:string}[]=[{value:'B',label:'Breakfast'},{value:'L',label:'Lunch'},{value:'D',label:'Dinner'}];
export const CAT_GST_TREATMENTS:{value:CatGstTreatment;label:string}[]=[
  {value:'bundled',label:'Bundled with the room (room-night rate on room + meals)'},{value:'separate',label:'Separate restaurant line (restaurant rate)'}];
export const CAT_EXTRA_CATEGORIES=[{value:'transport',label:'Transport'},{value:'laundry',label:'Laundry'},{value:'fnb',label:'Food & beverage'},
  {value:'room',label:'Room extras'},{value:'misc',label:'Miscellaneous'}];
export const CAT_UNITS=[{value:'each',label:'each'},{value:'piece',label:'per piece'},{value:'bag',label:'per bag'},{value:'night',label:'per night'},
  {value:'trip',label:'per trip'},{value:'hour',label:'per hour'},{value:'person',label:'per person'}];
export const CAT_DEPARTMENTS=[{value:'frontdesk',label:'Front desk'},{value:'housekeeping',label:'Housekeeping'},{value:'laundry',label:'Laundry'},
  {value:'restaurant',label:'Restaurant'},{value:'transport',label:'Transport'},{value:'banquet',label:'Banquet'},{value:'maintenance',label:'Maintenance'}];
/** Roles that may give discounts at all (DEC-063 G1); a reason may narrow them. */
export const CAT_DISCOUNT_ROLES:Role[]=['Owner','Manager','Reception'];
/** Room types of the fixture: the choices of "Room types allowed" until a roomTypes master (MA-1) is registered. */
export const CAT_FALLBACK_ROOM_TYPES=['Classic','Deluxe','Suite'];
const opt=(xs:readonly string[])=>xs.map(x=>({value:x,label:x}));

// ───────────────────────────── Seeds (fixture values; nothing posted, no figure moves) ─────────────────────────────
const dm=(rupees:number):MstDatedAmount[]=>[{amount:rupees*100,effectiveFrom:CAT_SEED_FROM}];
export function catSeedMealPlans():CatMealPlan[]{
  return [
    {id:'mpl-ep',code:'EP',name:'Room only (EP)',sortOrder:10,active:true,meals:[],addOn:dm(0),gst:'separate',roomTypes:[]},
    {id:'mpl-cp',code:'CP',name:'Breakfast included (CP)',sortOrder:20,active:true,meals:['B'],addOn:dm(300),gst:'bundled',roomTypes:[]},
    {id:'mpl-map',code:'MAP',name:'Breakfast and dinner (MAP)',sortOrder:30,active:true,meals:['B','D'],addOn:dm(750),gst:'separate',roomTypes:[]},
    {id:'mpl-ap',code:'AP',name:'All meals (AP)',sortOrder:40,active:true,meals:['B','L','D'],addOn:dm(1200),gst:'separate',roomTypes:[]},
  ];
}
export function catSeedExtras():CatExtra[]{
  const x=(code:string,name:string,category:string,rupees:number,gst:ChargeCat,unit:string,department:string,i:number):CatExtra=>
    ({id:'ext-'+code.toLowerCase(),code,name,sortOrder:(i+1)*10,active:true,category,price:dm(rupees),gst,unit,department});
  // "Laundry" ₹500 at the laundry rate (18 %) is the worked ₹4,265 GST journey's charge (tests/run.mjs, stream-c, stream-f, m1).
  return [
    x('LAUNDRY','Laundry','laundry',500,'laundry','bag','laundry',0),
    x('DRYCLEAN','Dry cleaning','laundry',250,'laundry','piece','laundry',1),
    x('XBED','Extra bed','room',1000,'other','night','housekeeping',2),
    x('PICKUP','Airport pickup','transport',1200,'other','trip','transport',3),
    x('DROP','Airport drop','transport',1200,'other','trip','transport',4),
    x('MINIBAR','Minibar','fnb',250,'food','each','restaurant',5),
    x('LATECO','Late checkout','misc',1000,'other','each','frontdesk',6),
    x('HALL','Meeting hall','misc',2500,'banquet','hour','banquet',7),
  ];
}
export function catSeedDiscountReasons():CatDiscountReason[]{
  const r=(code:string,name:string,maxPct:number,approvalAbovePct:number|undefined,roles:Role[],i:number):CatDiscountReason=>{
    const row:CatDiscountReason={id:'dsr-'+code.toLowerCase(),code,name,sortOrder:(i+1)*10,active:true,maxPct:maxPct*100,roles};
    if(approvalAbovePct!==undefined)row.approvalAbovePct=approvalAbovePct*100;return row;};
  return [
    r('SERVICE','Service recovery',25,10,[],0),
    r('CORP','Corporate rate',20,undefined,[],1),
    r('REPEAT','Repeat guest',10,undefined,[],2),
    r('LONGSTAY','Long stay',15,undefined,['Owner','Manager'],3),
    r('MGMT','Management courtesy',100,undefined,['Owner','Manager'],4),
  ];
}

// ───────────────────────────── Usage (in-use selectors; retire, never delete, when used) ─────────────────────────────
const allLines=(s:State)=>s.bookings.flatMap(b=>b.lines.map(l=>({b,l})));
function usage(parts:[string,number][]){const where=parts.filter(p=>p[1]>0).map(([w,n])=>`${w} ${n}`);return {count:parts.reduce((a,p)=>a+p[1],0),where};}
export function catPlanUsage(s:State,code:string){
  return usage([['Bookings',s.bookings.filter(b=>b.plan===code).length],['Plan lines',allLines(s).filter(x=>x.l.planCode===code).length]]);
}
export function catExtraUsage(s:State,id:string){return usage([['Folio lines',allLines(s).filter(x=>x.l.extraId===id).length]]);}
export function catReasonUsage(s:State,code:string){
  return usage([['Discount lines',allLines(s).filter(x=>x.l.discountReason===code).length],
    ['Approval requests',s.approvals.filter(a=>a.meta?.reasonCode===code).length]]);
}

// ───────────────────────────── Specs ─────────────────────────────
const money=(f:Omit<FieldSpec,'money'>):FieldSpec=>({...f,money:true});
/** "Room types allowed": the roomTypes master once MA-1 registers it, else the fixture's types. Empty = every room type. */
const roomTypesField:FieldSpec={key:'roomTypes',label:'Room types allowed',type:'multiselect',
  help:'None ticked = every room type. Bookings in other room types cannot take this plan.',
  get master(){return mstSpec('roomTypes')?'roomTypes':undefined;},
  get options(){return mstSpec('roomTypes')?undefined:opt(CAT_FALLBACK_ROOM_TYPES);}} as FieldSpec;
export const MEAL_PLAN_SPEC:MasterSpec<CatMealPlan>={
  id:CAT_MEAL_PLANS,label:'Meal plans',singular:'meal plan',group:'rooms',prefix:'MPL',money:true,permission:'settings',
  help:`EP, CP, MAP, AP and your own packages. The add-on is charged per guest per night with each room night. GST treatment: ${GST_ILLUSTRATIVE}.`,
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:20,placeholder:'CP',help:'Short code, e.g. EP, CP, MAP, AP. OTA meal codes map to it.'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Breakfast included (CP)'},
    {key:'meals',label:'Meals included',type:'multiselect',options:CAT_MEALS},
    money({key:'addOn',label:'Add-on per guest per night',type:'datedMoney',help:'Charged for every guest (adults and children) on each room night. ₹0 = no plan line (room only).'}),
    {key:'gst',label:'GST treatment',type:'select',required:true,options:CAT_GST_TREATMENTS,help:`Bundled: the plan line takes the room-night slab on room + meals (one composite supply). Separate: restaurant rate. ${GST_ILLUSTRATIVE}.`},
    roomTypesField,
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
    {key:'active',label:'Active',type:'toggle'},
  ],
  inUse:(s,row)=>catPlanUsage(s,row.code),
  seed:()=>catSeedMealPlans(),
};
export const EXTRA_SPEC:MasterSpec<CatExtra>={
  id:CAT_EXTRAS,label:'Extras and services',singular:'extra',group:'money',prefix:'EXT',money:true,permission:'settings',
  help:'Catalogue in front of “Add a service charge”: name, price and GST class are prefilled and can still be changed. “Other (describe)” stays free text (DEC-MA-4).',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:20,placeholder:'LAUNDRY'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Laundry',help:'Printed on the folio and the tax invoice.'},
    {key:'category',label:'Category',type:'select',required:true,options:CAT_EXTRA_CATEGORIES},
    money({key:'price',label:'Default price (before GST)',type:'datedMoney',help:'Prefilled in the charge dialog; Reception may change it on a charge.'}),
    {key:'gst',label:'GST class',type:'select',required:true,options:GST_CHARGE_CATEGORIES.map(c=>({value:c.id,label:c.label})),help:'Rate from the dated GST master on the posting date (Master configuration › Money and tax).'},
    {key:'unit',label:'Unit',type:'select',required:true,options:CAT_UNITS},
    {key:'department',label:'Department that fulfils',type:'select',options:CAT_DEPARTMENTS},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
    {key:'active',label:'Active',type:'toggle'},
  ],
  inUse:(s,row)=>catExtraUsage(s,row.id),
  seed:()=>catSeedExtras(),
};
export const DISCOUNT_REASON_SPEC:MasterSpec<CatDiscountReason>={
  id:CAT_DISCOUNT_REASONS,label:'Discount reasons',singular:'discount reason',group:'money',prefix:'DSR',money:true,permission:'settings',
  help:'Reasons offered in “Add a discount”. Role limits and approvals still apply (DEC-063); a reason can cap the discount and ask for approval sooner.',
  fields:[
    {key:'code',label:'Code',type:'text',required:true,maxLength:20,placeholder:'SERVICE'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Service recovery'},
    {key:'maxPct',label:'Maximum discount (% of the folio charges)',type:'percent',money:true,required:true,min:1,max:10000},
    {key:'approvalAbovePct',label:'Needs approval above (% of the charges)',type:'percent',money:true,min:0,max:10000,help:'Optional. Above it, another user approves even within the role limit (the Owner decides directly).'},
    {key:'roles',label:'Shown to roles',type:'multiselect',options:opt(CAT_DISCOUNT_ROLES),help:'None ticked = every role that may give discounts (Owner, Manager, Reception).'},
    {key:'sortOrder',label:'Sort order',type:'number',min:0,max:1_000_000},
    {key:'active',label:'Active',type:'toggle'},
  ],
  validate:(_s,row)=>row.approvalAbovePct!==undefined&&row.maxPct!==undefined&&row.approvalAbovePct>=row.maxPct
    ?'“Needs approval above” must be below the maximum discount.':undefined,
  inUse:(s,row)=>catReasonUsage(s,row.code),
  seed:()=>catSeedDiscountReasons(),
};
registerMaster(MEAL_PLAN_SPEC);
registerMaster(EXTRA_SPEC);
registerMaster(DISCOUNT_REASON_SPEC);

// ───────────────────────────── Lookups ─────────────────────────────
export const catPlans=(s:State,active?:boolean)=>mstRows(s,CAT_MEAL_PLANS,active===undefined?{}:{active}) as CatMealPlan[];
export const catPlan=(s:State,code:string)=>mstByCode(s,CAT_MEAL_PLANS,code) as CatMealPlan|undefined;
export const catExtras=(s:State,active?:boolean)=>mstRows(s,CAT_EXTRAS,active===undefined?{}:{active}) as CatExtra[];
export const catExtra=(s:State,id:string)=>mstById(s,CAT_EXTRAS,id) as CatExtra|undefined;
export const catReasons=(s:State,active?:boolean)=>mstRows(s,CAT_DISCOUNT_REASONS,active===undefined?{}:{active}) as CatDiscountReason[];
export const catReason=(s:State,code:string)=>mstByCode(s,CAT_DISCOUNT_REASONS,code) as CatDiscountReason|undefined;
/** Price of an extra on a date (undefined before its first dated price). */
export const catExtraPrice=(x:CatExtra,date:string)=>mstMoneyOn(x.price,date);
/** Add-on per guest per night of a plan on a date (0 when none is dated yet). */
export const catPlanAddOn=(p:CatMealPlan,date:string)=>mstMoneyOn(p.addOn,date)??0;
export const catMealsLabel=(p:Pick<CatMealPlan,'meals'>)=>p.meals?.length?CAT_MEALS.filter(m=>p.meals!.includes(m.value)).map(m=>m.label.toLowerCase()).join(', '):'room only';
export const catPlanLabel=(s:State,code?:string)=>!code?'No plan':catPlan(s,code)?.name??code;
/** Guests charged by a plan: adults and children of the booking. */
export const catPlanGuests=(b:Pick<Booking,'people'|'children'>)=>Math.max(1,(b.people||0)+(b.children??0));
const isoDate=(d:string)=>/^\d{4}-\d{2}-\d{2}$/.test(d);

/** Is the room type allowed by the plan? (empty list = all; matches the room type, or a roomTypes master row by key (MA-1: the room's stored type), code or name). */
export function catPlanAllowsType(s:State,p:CatMealPlan,roomType:string):boolean{
  const list=p.roomTypes??[];if(!list.length)return true;
  if(list.includes(roomType))return true;
  const rt=mstSpec('roomTypes')?mstRows(s,'roomTypes').find(r=>String(r.key??r.code)===roomType||r.name===roomType||r.code===roomType):undefined;
  return !!rt&&(list.includes(rt.code)||list.includes(rt.name));
}
/** Meal plans a booking may take (active, room type allowed), for pick lists; the booking's current plan stays visible. */
export function catPlanOptions(s:State,roomType?:string,current?:string){
  return catPlans(s).filter(p=>(p.active&&(!roomType||catPlanAllowsType(s,p,roomType)))||p.code===current)
    .map(p=>({value:p.code,label:p.active?p.name:`${p.name} (retired)`}));
}
/** Validates a plan for a booking (BOOK / CAT_PLAN_SET): typed master errors. Returns the plan row (undefined = no plan). */
export function catCheckPlan(s:State,code:unknown,roomType:string,current?:string):CatMealPlan|undefined{
  const c=String(code??'').trim().toUpperCase();if(!c)return undefined;
  const p=catPlan(s,c);
  if(!p)mstFail('MASTER_INVALID',`Meal plan ${c} is not in the Meal plans list.`,'plan');
  if(!p!.active&&p!.code!==current)mstFail('MASTER_RETIRED',`${p!.name} is retired — choose an active meal plan.`,'plan');
  if(!catPlanAllowsType(s,p!,roomType))mstFail('MASTER_INVALID',`${p!.name} is not offered for ${roomType} rooms.`,'plan');
  return p;
}

// ───────────────────────────── Plan lines (posted with each room night) ─────────────────────────────
/** The plan line of a booking for one night, or undefined (no plan, EP, or ₹0 add-on). Pure: does not change the booking. */
export function catPlanNightLine(s:State,b:Booking,date:string):Line|undefined{
  if(!b.plan)return undefined;
  const p=catPlan(s,b.plan);if(!p)return undefined;
  const each=catPlanAddOn(p,date),guests=catPlanGuests(b),amount=each*guests;
  if(amount<=0)return undefined;
  const bps=p.gst==='bundled'?gstForRoomNight(s,date,b.rate+amount).bps:gstForFood(s,date).bps;
  return {id:`plan:${b.id}:${date}`,label:`Meal plan ${p.code} · ${guests} × ₹${(each/100).toLocaleString('en-IN')} · ${date}`,amount,date,kind:'service',taxBps:bps,planCode:p.code};
}
/** Called by the room-night posting (services/mock/commands.ts addNight) right after a NEW room-night line was pushed for `date`:
 *  posts the plan line once (id plan:<booking>:<date>). Bundled plans also give the new room-night line the slab of room + meals
 *  (one composite supply, room the principal supply — Illustrative, CA to confirm); a night posted before the plan was chosen keeps
 *  its snapshot and gets no plan line. */
export function catPlanNight(s:State,b:Booking,date:string):void{
  const line=catPlanNightLine(s,b,date);
  if(!line||b.lines.some(l=>l.id===line.id))return;
  const room=b.lines[b.lines.length-1];
  if(catPlan(s,b.plan!)?.gst==='bundled'&&room?.kind==='room'&&room.date===date&&!room.reversalOf)room.taxBps=line.taxBps;
  b.lines.push(line);
}
/** Add-on total of a stay (quotes, booking site): every night from arrival to departure, per guest. */
export function catPlanQuote(s:State,code:string,guests:number,from:string,to:string):number{
  const p=catPlan(s,code);if(!p||!isoDate(from)||!isoDate(to))return 0;
  let t=0;for(let d=from,i=0;d<to&&i<731;d=addDay(d),i++)t+=catPlanAddOn(p,d)*Math.max(1,guests);return t;
}
function addDay(d:string){return new Date(Date.parse(d+'T12:00:00Z')+86400000).toISOString().slice(0,10);}
/** Booking site: active plans offered for a room type, with the add-on per guest per night on the date. */
export function catPlansForSite(s:State,roomType:string,date:string){
  return catPlans(s,true).filter(p=>catPlanAllowsType(s,p,roomType)).map(p=>({code:p.code,name:p.name,meals:catMealsLabel(p),perGuestNight:catPlanAddOn(p,date)}));
}
/** OTA meal codes (channel manager) → plan codes. */
export const CAT_OTA_MEAL_CODES:Record<string,string>={RO:'EP',EP:'EP',BB:'CP',CP:'CP',HB:'MAP',MAP:'MAP',FB:'AP',AP:'AP',AI:'AP'};
/** Plan for an OTA / booking-site meal code: the mapped code when that plan is active, else undefined (book without a plan). */
export function catPlanFromChannel(s:State,mealCode:string|undefined):string|undefined{
  const c=CAT_OTA_MEAL_CODES[String(mealCode??'').trim().toUpperCase()]??String(mealCode??'').trim().toUpperCase();
  const p=c?catPlan(s,c):undefined;return p?.active?p.code:undefined;
}

// ───────────────────────────── Extras (charge dialog) ─────────────────────────────
export const CAT_QTY_MAX=99;
/** Prefill of the charge dialog for an extra on the business date: label, amount (price × qty), GST class. */
export function catChargePrefill(s:State,x:CatExtra,qty=1){
  const price=catExtraPrice(x,s.date);
  return {label:qty>1?`${x.name} × ${qty}`:x.name,amount:price===undefined?undefined:price*qty,gst:(x.gst??'other') as ChargeCat,price};
}
/** GST rate (bps) a charge of this class would take today. */
export const catChargeBps=(s:State,gst:ChargeCat)=>gstForCharge(s,s.date,gst).bps;

// ───────────────────────────── Discount reasons ─────────────────────────────
/** Reasons this role may pick (active, role allowed). */
export function catReasonsFor(s:State,role:Role,current?:string){
  return catReasons(s).filter(r=>(r.active&&(!r.roles?.length||r.roles.includes(role)))||r.code===current);
}
/** Cap and approval threshold of a reason on a folio (paise; approvalAbove undefined = none). */
export function catReasonLimits(r:CatDiscountReason,b:Booking){
  const net=Math.max(0,totals(b).net);
  return {net,max:Math.floor(net*(r.maxPct??10000)/10000),approvalAbove:r.approvalAbovePct===undefined?undefined:Math.floor(net*r.approvalAbovePct/10000)};
}

// ───────────────────────────── Reports ─────────────────────────────
const inRange=(d:string,from:string,to:string)=>d>=from&&d<=to;
const live=(b:Booking,l:Line)=>!l.reversalOf&&!b.lines.some(r=>r.reversalOf===l.id);
export type CatSplitRow={key:string;label:string;count:number;amount:number};
/** Service charges posted in [from, to]: catalogue (by item) vs free text (by label); plan lines apart. Reversed lines excluded. */
export function catChargeSplit(s:State,from:string,to:string){
  const cat=new Map<string,CatSplitRow>(),free=new Map<string,CatSplitRow>();let plans={count:0,amount:0};
  for(const b of s.bookings)for(const l of b.lines){
    if(l.kind!=='service'||!inRange(l.date,from,to)||!live(b,l))continue;
    if(l.planCode){plans={count:plans.count+1,amount:plans.amount+l.amount};continue;}
    const m=l.extraId?cat:free,key=l.extraId??l.label.trim().toLowerCase();
    const x=catExtra(s,l.extraId??'');
    const row=m.get(key)??{key,label:l.extraId?(x?`${x.code} · ${x.name}`:l.extraId):l.label.trim(),count:0,amount:0};
    row.count++;row.amount+=l.amount;m.set(key,row);
  }
  const sum=(m:Map<string,CatSplitRow>)=>({count:[...m.values()].reduce((a,r)=>a+r.count,0),amount:[...m.values()].reduce((a,r)=>a+r.amount,0),
    rows:[...m.values()].sort((a,b)=>b.amount-a.amount||a.label.localeCompare(b.label))});
  return {catalogue:sum(cat),freeText:sum(free),plans};
}
export type CatPlanRow={code:string;label:string;bookings:number;roomNights:number;roomRevenue:number;planRevenue:number};
/** Stays by meal plan: bookings with a room night in [from, to], room nights and room revenue posted then, plan lines posted then. */
export function catPlanReport(s:State,from:string,to:string):CatPlanRow[]{
  const rows=new Map<string,CatPlanRow>();
  const get=(code:string)=>{let r=rows.get(code);if(!r){r={code,label:code==='—'?'No plan':catPlanLabel(s,code),bookings:0,roomNights:0,roomRevenue:0,planRevenue:0};rows.set(code,r);}return r;};
  for(const b of s.bookings){
    const nights=b.lines.filter(l=>l.kind==='room'&&inRange(l.date,from,to)&&live(b,l));
    const planLines=b.lines.filter(l=>l.planCode&&inRange(l.date,from,to)&&live(b,l));
    if(!nights.length&&!planLines.length)continue;
    const r=get(b.plan??'—');r.bookings++;r.roomNights+=nights.length;r.roomRevenue+=nights.reduce((a,l)=>a+l.amount,0);
    for(const l of planLines)get(l.planCode!).planRevenue+=l.amount;
  }
  const order=new Map(catPlans(s).map((p,i)=>[p.code,i]));
  return [...rows.values()].sort((a,b)=>(order.get(a.code)??999)-(order.get(b.code)??999)||a.code.localeCompare(b.code));
}
/** Discounts posted in [from, to] by reason (free-text reasons in one row). */
export function catDiscountReport(s:State,from:string,to:string):CatSplitRow[]{
  const m=new Map<string,CatSplitRow>();
  for(const b of s.bookings)for(const l of b.lines){
    if(l.kind!=='discount'||!inRange(l.date,from,to)||!live(b,l))continue;
    const key=l.discountReason??'—';
    const row=m.get(key)??{key,label:key==='—'?'Free-text reason':catReason(s,key)?.name??key,count:0,amount:0};
    row.count++;row.amount+=-l.amount;m.set(key,row);
  }
  return [...m.values()].sort((a,b)=>b.amount-a.amount);
}
/** CSV of the three reports (one section each). */
export function catReportCsv(s:State,from:string,to:string):string{
  const q=(v:unknown)=>{const t=String(v??'');return /[",\n]/.test(t)?`"${t.replace(/"/g,'""')}"`:t;};
  const rs=(p:number)=>(p/100).toFixed(2);
  const split=catChargeSplit(s,from,to),plans=catPlanReport(s,from,to),disc=catDiscountReport(s,from,to);
  const lines:string[][]=[['Section','Item','Count','Amount (₹, before GST)']];
  for(const r of split.catalogue.rows)lines.push(['Catalogue charges',r.label,String(r.count),rs(r.amount)]);
  for(const r of split.freeText.rows)lines.push(['Free-text charges',r.label,String(r.count),rs(r.amount)]);
  lines.push(['Meal plan lines','All plans',String(split.plans.count),rs(split.plans.amount)]);
  for(const r of plans)lines.push([`Plan ${r.code}`,`${r.label} · ${r.bookings} bookings · ${r.roomNights} room nights · room ₹${rs(r.roomRevenue)}`,String(r.roomNights),rs(r.planRevenue)]);
  for(const r of disc)lines.push(['Discounts by reason',r.label,String(r.count),rs(r.amount)]);
  return lines.map(l=>l.map(q).join(',')).join('\n')+'\n';
}
