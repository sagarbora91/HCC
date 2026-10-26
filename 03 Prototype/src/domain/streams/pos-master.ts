// MA-4 · POS masters (plan HCC_Customisation_Features_Plan_v1.md Part 2 §8 F: M26 outlets, M27 tables and counters, M28 menu categories
// and items) on the MA-0 masters framework (domain/streams/masters.ts). Exported names start with 'pos' / 'POS' (module 23 namespace).
//
//   outlets         backed by s.posX.outlets itself (store adapter; no second copy): code, name, type restaurant / room service / bar /
//                   banquet (room service = checks on in-house rooms, every other type = tables), timings, GST profile (money field,
//                   Owner-only: "hotel" = the hotel's specified-premises status from the GST master, or 5 % / 18 % fixed), KOT station.
//                   Cannot retire with open checks (MASTER_IN_USE).
//   posTables       backed by s.posX.outlets[].tableRows (+ the legacy `tables` list kept in step = active table numbers): code, name,
//                   outlet, table no., seats, section, counter (takeaway). Cannot retire with an open check on it.
//   menuCategories  stored in s.mastersX (seeded from the fixture menu categories).
//   menuItems       backed by s.posX.menu: category, name, short code, price as dated money (Owner-only, DEC-058), GST class (Owner-only),
//                   veg, outlets, kitchen station, "sold out today" (replaces the plain POS_MENU_TOGGLE flag: it lapses at the next business
//                   date). Availability (active, outlets, sold out) needs `masters`; the in-service "sold out today" switch on the POS page
//                   keeps POS_MENU_TOGGLE (billing).
// KOT: one ticket per kitchen station (item station → outlet KOT station → "Kitchen"). GST of a check: one GST class per check (lines keep
// their class); rate = the outlet profile for food, the GST master's service rate otherwise. Fixture rows carry none of the new fields, so
// every BASE / FY26 figure is unchanged (rates, KOT numbers, prices). GST rates are illustrative — CA to confirm.
import type {State} from '../types';
import type {POSOutlet,POSMenuItem,POSOrder,POSTable,POSState} from './pos';
import {registerMaster,mstRows,mstByCode,mstLabel,mstMoneyOn,type MasterRow,type MasterSpec,type MstDatedAmount} from './masters';
import {mastersSeed,seedPosCode} from '../../fixtures/ext/masters-seed';
import {gstForFood,gstForService,gstValueOn,type GstRate} from '../gst';

// ───────────────────────────── Ids and choices ─────────────────────────────
export const POS_MST_OUTLETS='outlets';
export const POS_MST_TABLES='posTables';
export const POS_MST_CATEGORIES='menuCategories';
export const POS_MST_ITEMS='menuItems';
/** Effective-from of a fixture / legacy price (the item's single price before any dated change). */
export const POS_PRICE_SINCE='2017-07-01';
export const POS_DEFAULT_STATION='Kitchen';
export type POSOutletType='restaurant'|'room-service'|'bar'|'banquet';
export const POS_OUTLET_TYPES:{value:POSOutletType;label:string}[]=[
  {value:'restaurant',label:'Restaurant'},{value:'room-service',label:'Room service'},{value:'bar',label:'Bar'},{value:'banquet',label:'Banquet'}];
export type POSGstProfile='hotel'|'standard'|'specified';
export const POS_GST_PROFILES:{value:POSGstProfile;label:string}[]=[
  {value:'hotel',label:'Hotel status (GST master: specified premises or not)'},
  {value:'standard',label:'5 % — not specified premises'},
  {value:'specified',label:'18 % — specified premises'}];
export type POSGstClass='food'|'laundry'|'banquet'|'other';
export const POS_GST_CLASSES:{value:POSGstClass;label:string}[]=[
  {value:'food',label:'Food & beverage (outlet GST profile)'},{value:'banquet',label:'Banquet / hall'},{value:'laundry',label:'Laundry'},{value:'other',label:'Other service'}];
const TIME=/^([01]\d|2[0-3]):[0-5]\d$/;

const px=(s:State):POSState|undefined=>s.posX;
/** Code from a fixture id or name: upper-case, other characters → "-", at most 20 ("restaurant" → RESTAURANT, "Breads & rice" → BREADS-RICE). */
export const posCodeOf=seedPosCode;

// ───────────────────────────── Outlets ─────────────────────────────
export const posOutletType=(o:POSOutlet):POSOutletType=>o.type??(o.kind==='room'?'room-service':'restaurant');
export const posOutletCode=(o:POSOutlet)=>o.code??posCodeOf(o.id);
export const posOutletActive=(o:POSOutlet)=>o.active!==false;
function outletRow(o:POSOutlet,i:number):MasterRow{
  const r:MasterRow={id:o.id,code:posOutletCode(o),name:o.name,sortOrder:o.sortOrder??(i+1)*10,active:posOutletActive(o),type:posOutletType(o),gstProfile:o.gstProfile??'hotel'};
  if(o.retiredOn)r.retiredOn=o.retiredOn;if(o.opens)r.opens=o.opens;if(o.closes)r.closes=o.closes;if(o.kotStation)r.kotStation=o.kotStation;
  return r;
}
const optStr=(v:unknown)=>{const t=typeof v==='string'?v.trim():'';return t||undefined;};
function setOpt<T extends object>(o:T,k:string,v:unknown){if(v===undefined||v===null||v==='')delete (o as Record<string,unknown>)[k];else (o as Record<string,unknown>)[k]=v;}
/** Outlet id of an outlet code (retired outlets resolve too). */
export function posOutletIdOf(s:State,code:string){return (px(s)?.outlets??[]).find(o=>posOutletCode(o)===code)?.id;}
/** Outlets in master order (sortOrder); active only by default — the POS screens pick from these. */
export function posOutletList(s:State,opts:{all?:boolean}={}):POSOutlet[]{
  const list=px(s)?.outlets??[];const rank=new Map(list.map((o,i)=>[o.id,o.sortOrder??(i+1)*10]));
  return list.filter(o=>opts.all||posOutletActive(o)).slice().sort((a,b)=>rank.get(a.id)!-rank.get(b.id)!);
}
/** "Open 07:00–23:00" (or '' without timings). */
export const posOutletHours=(o:POSOutlet)=>o.opens&&o.closes?`${o.opens}–${o.closes}`:o.opens?`from ${o.opens}`:o.closes?`until ${o.closes}`:'';

// ───────────────────────────── Tables and counters ─────────────────────────────
/** Table rows of an outlet: the stored master rows, else the legacy `tables` list (fixture: T1…T8 → RESTAURANT-T1 …). */
export function posTableRows(o:POSOutlet):POSTable[]{
  if(o.tableRows)return o.tableRows;
  const code=posOutletCode(o);
  return (o.tables??[]).map((t,i)=>({id:`tbl-${o.id}-${t}`,code:posCodeOf(`${code}-${t}`),name:`${o.name} ${t}`,tableNo:t,sortOrder:(i+1)*10,active:true}));
}
/** Active table / counter numbers of an outlet in master order (POS spot picker, check opening). */
export function posTablesOf(s:State,outletId:string):POSTable[]{
  const o=(px(s)?.outlets??[]).find(x=>x.id===outletId);if(!o||o.kind!=='table')return [];
  return posTableRows(o).filter(t=>t.active).slice().sort((a,b)=>a.sortOrder-b.sortOrder||(a.tableNo<b.tableNo?-1:1));
}
/** "T3 · 4 seats · Garden" / "Counter C1 · takeaway". */
export function posTableLabel(t:POSTable){return [t.counter?'Counter '+t.tableNo:'Table '+t.tableNo,t.seats?`${t.seats} seats`:'',t.section??''].filter(Boolean).join(' · ');}

// ───────────────────────────── Menu categories and items ─────────────────────────────
export const posSeedCategories=(s:State):MasterRow[]=>mastersSeed(POS_MST_CATEGORIES,s); // fixtures/ext/masters-seed.ts
/** Category code of an item: stored code, else the category row with that name, else the code derived from the name. */
export function posItemCategoryCode(s:State,m:POSMenuItem):string{
  if(m.categoryCode)return m.categoryCode;
  return mstRows(s,POS_MST_CATEGORIES).find(r=>r.name===m.category)?.code??posCodeOf(m.category);
}
export const posItemCategoryName=(s:State,m:POSMenuItem)=>mstByCode(s,POS_MST_CATEGORIES,posItemCategoryCode(s,m))?.name??m.category;
export const posItemCode=(m:POSMenuItem)=>m.code??posCodeOf(m.id.replace(/^m-/,''));
export const posItemActive=(m:POSMenuItem)=>m.active!==false;
/** Dated prices of an item (legacy single price → one entry from POS_PRICE_SINCE). */
export const posItemPrices=(m:POSMenuItem):MstDatedAmount[]=>m.prices??[{amount:m.price,effectiveFrom:POS_PRICE_SINCE}];
/** Price in force on a date (undefined = no price yet: the Owner sets it; the item cannot be sold). */
export const posItemPrice=(s:State,m:POSMenuItem,date=s.date)=>m.prices?mstMoneyOn(m.prices,date):m.price;
/** "Sold out today": the switch lapses at the next business date (a legacy "86" without a date stays until switched back). */
export const posSoldOut=(s:State,m:POSMenuItem)=>m.available===false&&(!m.soldOutOn||m.soldOutOn>=s.date);
/** Why an item cannot be added to a check now (undefined = it can). */
export function posItemBlock(s:State,m:POSMenuItem,outletId:string):string|undefined{
  if(!posItemActive(m))return `${m.name} is retired from the menu.`;
  if(!m.outlets.includes(outletId))return 'This item is not on this outlet’s menu.';
  if(posSoldOut(s,m))return `${m.name} is sold out today (86).`;
  if(posItemPrice(s,m)===undefined)return `${m.name} has no price yet — the Owner sets it in Master configuration.`;
  return undefined;
}
export const posItemClass=(m:POSMenuItem):POSGstClass=>m.gstClass??'food';
/** Kitchen station of an item at an outlet (item station → outlet KOT station → Kitchen). */
export function posStationOf(s:State,outletId:string,m:Pick<POSMenuItem,'station'>|undefined):string{
  const o=(px(s)?.outlets??[]).find(x=>x.id===outletId);
  return m?.station||o?.kotStation||POS_DEFAULT_STATION;
}
/** Stations that exist (configured on items or outlets) — the kitchen board's station filter. */
export function posStations(s:State):string[]{
  const set=new Set<string>();for(const o of px(s)?.outlets??[])if(o.kotStation)set.add(o.kotStation);for(const m of px(s)?.menu??[])if(m.station)set.add(m.station);
  return set.size?[POS_DEFAULT_STATION,...[...set].filter(x=>x!==POS_DEFAULT_STATION).sort()]:[];
}
/** Station of a KOT (old KOTs have none → Kitchen). */
export const posKotStation=(k:{station?:string})=>k.station??POS_DEFAULT_STATION;

// ───────────────────────────── GST (DEC-069; illustrative — CA to confirm) ─────────────────────────────
/** GST class of a check (its first active line's class; one class per check). */
export const posCheckClass=(o:POSOrder):POSGstClass=>(o.lines.find(l=>!l.voided)?.gst as POSGstClass|undefined)??'food';
/** Rate of a supply at an outlet: food follows the outlet's GST profile, other classes the GST master's service rate. */
export function posRateFor(s:State,outletId:string,cls:POSGstClass,date:string):GstRate{
  if(cls!=='food')return gstForService(s,date,cls);
  const prof=(px(s)?.outlets??[]).find(o=>o.id===outletId)?.gstProfile??'hotel';
  if(prof==='standard')return {bps:Number(gstValueOn(s,'gst.food.bps',date)),itc:false,category:'food',rule:'Restaurant · outlet fixed at the standard rate'};
  if(prof==='specified')return {bps:Number(gstValueOn(s,'gst.food.specifiedBps',date)),itc:true,category:'food',rule:'Restaurant · outlet fixed at the specified-premises rate'};
  return gstForFood(s,date);
}
export const posCheckRate=(s:State,o:POSOrder,date:string)=>posRateFor(s,o.outlet,posCheckClass(o),date);

// ───────────────────────────── Store adapters (rows live in s.posX) ─────────────────────────────
const slice=(s:State):POSState=>{s.posX??={outlets:[],menu:[],orders:[],receipts:[],kotSeq:{},billSeq:0};return s.posX;};
const outletStore={
  read:(s:State)=>(px(s)?.outlets??[]).map(outletRow),
  write(s:State,rows:MasterRow[]){
    const p=slice(s),old=new Map(p.outlets.map(o=>[o.id,o]));
    p.outlets=rows.map(r=>{
      const prev=old.get(r.id),type=(String(r.type??'restaurant') as POSOutletType),kind=type==='room-service'?'room':'table';
      const o:POSOutlet={...(prev??{id:r.id,name:r.name,kind}),id:r.id,name:r.name,kind,code:r.code,sortOrder:r.sortOrder,active:r.active,type,gstProfile:(r.gstProfile as POSGstProfile|undefined)??'hotel'};
      setOpt(o,'retiredOn',r.retiredOn);setOpt(o,'opens',optStr(r.opens));setOpt(o,'closes',optStr(r.closes));setOpt(o,'kotStation',optStr(r.kotStation));
      if(kind==='table'){o.tables??=[];}else{delete o.tables;delete o.tableRows;}
      return o;});
  },
};
const tableStore={
  read:(s:State)=>(px(s)?.outlets??[]).filter(o=>o.kind==='table').flatMap(o=>posTableRows(o).map(t=>({...t,outlet:posOutletCode(o)}) as MasterRow)),
  write(s:State,rows:MasterRow[]){
    const p=slice(s);
    p.outlets=p.outlets.map(o=>{
      if(o.kind!=='table')return o;
      const code=posOutletCode(o),mine=rows.filter(r=>r.outlet===code);
      const tableRows:POSTable[]=mine.map(r=>{const t:POSTable={id:r.id,code:r.code,name:r.name,tableNo:String(r.tableNo??'').trim(),sortOrder:r.sortOrder,active:r.active};
        if(r.seats!==undefined&&r.seats!==null&&r.seats!=='')t.seats=Number(r.seats);setOpt(t,'section',optStr(r.section));if(r.counter)t.counter=true;if(r.retiredOn)t.retiredOn=r.retiredOn;return t;});
      if(!o.tableRows&&!mine.length&&!(o.tables??[]).length)return o;
      return {...o,tableRows,tables:tableRows.filter(t=>t.active).sort((a,b)=>a.sortOrder-b.sortOrder).map(t=>t.tableNo)};});
  },
};
function itemRow(s:State,m:POSMenuItem,i:number):MasterRow{
  const outlets=px(s)?.outlets??[];
  const r:MasterRow={id:m.id,code:posItemCode(m),name:m.name,sortOrder:m.sortOrder??(i+1)*10,active:posItemActive(m),category:posItemCategoryCode(s,m),
    price:posItemPrices(m),gstClass:posItemClass(m),veg:!!m.veg,outlets:m.outlets.map(id=>{const o=outlets.find(x=>x.id===id);return o?posOutletCode(o):posCodeOf(id);}),
    soldOutToday:posSoldOut(s,m)};
  if(m.retiredOn)r.retiredOn=m.retiredOn;if(m.shortCode)r.shortCode=m.shortCode;if(m.station)r.station=m.station;
  return r;
}
const itemStore={
  read:(s:State)=>(px(s)?.menu??[]).map((m,i)=>itemRow(s,m,i)),
  write(s:State,rows:MasterRow[]){
    const p=slice(s),old=new Map(p.menu.map(m=>[m.id,m]));
    p.menu=rows.map(r=>{
      const prev=old.get(r.id),prices=Array.isArray(r.price)?(r.price as MstDatedAmount[]):[];
      const cat=String(r.category??'');
      const m:POSMenuItem={...(prev??{id:r.id,name:r.name,category:'',price:0,veg:false,outlets:[],available:true}),id:r.id,code:r.code,name:r.name,sortOrder:r.sortOrder,active:r.active,
        categoryCode:cat,category:cat?mstLabel(s,POS_MST_CATEGORIES,cat):'',prices,price:mstMoneyOn(prices,s.date)??prices[0]?.amount??0,
        gstClass:(r.gstClass as POSGstClass|undefined)??'food',veg:!!r.veg,
        outlets:(Array.isArray(r.outlets)?r.outlets as string[]:[]).map(c=>posOutletIdOf(s,c)??c)};
      setOpt(m,'retiredOn',r.retiredOn);setOpt(m,'shortCode',optStr(r.shortCode));setOpt(m,'station',optStr(r.station));
      const sold=!!r.soldOutToday,was=prev?posSoldOut(s,prev):false;
      if(sold&&!was){m.available=false;m.soldOutOn=s.date;}else if(!sold){m.available=true;delete m.soldOutOn;}
      return m;});
  },
};

// ───────────────────────────── In use ─────────────────────────────
const orders=(s:State)=>px(s)?.orders??[];
export function posOutletUsage(s:State,row:MasterRow){
  const id=posOutletIdOf(s,row.code)??row.id;
  const checks=orders(s).filter(o=>o.outlet===id).length,items=(px(s)?.menu??[]).filter(m=>m.outlets.includes(id)).length;
  const o=(px(s)?.outlets??[]).find(x=>x.id===id),tables=o?posTableRows(o).length:0;
  const where=[checks?`Checks ${checks}`:'',items?`Menu items ${items}`:'',tables?`Tables ${tables}`:''].filter(Boolean);
  return {count:checks+items+tables,where};
}
export const posOpenChecksAt=(s:State,outletId:string,table?:string)=>orders(s).filter(o=>o.outlet===outletId&&o.status==='Open'&&(table===undefined||o.table===table));

// ───────────────────────────── Specs ─────────────────────────────
const CODE_F={key:'code',label:'Code',type:'text' as const,required:true,maxLength:20};
const SORT_F={key:'sortOrder',label:'Sort order',type:'number' as const,min:0,max:1_000_000};
const ACTIVE_F={key:'active',label:'Active',type:'toggle' as const};

export const POS_OUTLET_SPEC:MasterSpec={
  id:POS_MST_OUTLETS,label:'Outlets',singular:'outlet',group:'pos',prefix:'OUT',money:false,permission:'masters',
  help:'Restaurant, room service, bar and banquet outlets. Room service takes checks on in-house rooms; the others on tables. Retire an outlet only when it has no open checks.',
  fields:[{...CODE_F,placeholder:'BAR',help:'Short code, e.g. REST, RS, BAR. Cannot change once checks or menu items use it.'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Rooftop bar'},
    {key:'type',label:'Type',type:'select',required:true,options:POS_OUTLET_TYPES},
    {key:'opens',label:'Opens at (HH:MM)',type:'text',maxLength:5,placeholder:'07:00'},
    {key:'closes',label:'Closes at (HH:MM)',type:'text',maxLength:5,placeholder:'23:00'},
    {key:'gstProfile',label:'GST profile',type:'select',options:POS_GST_PROFILES,money:true,help:'Restaurant GST: the hotel’s status from the GST master (default), or fixed 5 % / 18 % (specified premises). Illustrative — CA to confirm.'},
    {key:'kotStation',label:'KOT station',type:'text',maxLength:20,placeholder:'Kitchen',help:'Where this outlet’s KOTs print unless an item has its own station.'},
    SORT_F,ACTIVE_F],
  inUse:(s,row)=>posOutletUsage(s,row),
  canRetire:(s,row)=>{const id=posOutletIdOf(s,row.code)??row.id;const n=posOpenChecksAt(s,id).length;
    return n?`${row.name} has ${n} open check${n===1?'':'s'} — settle or void ${n===1?'it':'them'} before retiring the outlet.`:undefined;},
  validate:(s,row,rows)=>{
    for(const k of ['opens','closes']){const v=optStr(row[k]);if(v&&!TIME.test(v))return `${k==='opens'?'Opens':'Closes'} at: use HH:MM (24-hour), e.g. 07:00.`;}
    const old=rows.find(r=>r.id===row.id);
    if(old&&(old.type==='room-service')!==(row.type==='room-service')){
      const id=posOutletIdOf(s,old.code)??old.id,o=(px(s)?.outlets??[]).find(x=>x.id===id);
      if(orders(s).some(x=>x.outlet===id)||(o&&posTableRows(o).length))return 'This outlet already has checks or tables — room service and table outlets cannot be switched. Add a new outlet instead.';}
    return undefined;},
  store:outletStore,
};
export const POS_TABLE_SPEC:MasterSpec={
  id:POS_MST_TABLES,label:'Tables and counters',singular:'table',group:'pos',prefix:'TBL',money:false,permission:'masters',
  help:'Tables of each table outlet, plus takeaway counters. The POS shows the active ones in this order.',
  fields:[{...CODE_F,placeholder:'REST-T9',help:'Unique code, e.g. outlet code + table, REST-T9.'},
    {key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Restaurant T9'},
    {key:'outlet',label:'Outlet',type:'select',required:true,master:POS_MST_OUTLETS},
    {key:'tableNo',label:'Table / counter no.',type:'text',required:true,maxLength:10,placeholder:'T9',help:'What the POS and the bills show. Cannot change once a check used it.'},
    {key:'seats',label:'Seats',type:'number',min:1,max:60},
    {key:'section',label:'Section',type:'text',maxLength:40,placeholder:'Garden'},
    {key:'counter',label:'Takeaway counter',type:'toggle'},
    SORT_F,ACTIVE_F],
  inUse:(s,row)=>{const id=posOutletIdOf(s,String(row.outlet??''));const n=id?orders(s).filter(o=>o.outlet===id&&o.table===row.tableNo).length:0;return {count:n,where:n?[`Checks ${n}`]:[]};},
  canRetire:(s,row)=>{const id=posOutletIdOf(s,String(row.outlet??''));return id&&posOpenChecksAt(s,id,String(row.tableNo)).length?`${row.tableNo} has an open check — settle or void it first.`:undefined;},
  validate:(s,row,rows)=>{
    const outlet=(px(s)?.outlets??[]).find(o=>posOutletCode(o)===row.outlet);
    if(!outlet)return 'Choose an outlet.';
    if(outlet.kind!=='table')return `${outlet.name} is room service — it takes checks on rooms, not tables.`;
    const no=String(row.tableNo??'').trim();if(!/^[A-Za-z0-9 -]{1,10}$/.test(no))return 'Table / counter no.: 1–10 letters, digits, spaces or “-”.';
    if(rows.some(r=>r.id!==row.id&&r.active&&r.outlet===row.outlet&&String(r.tableNo).toLowerCase()===no.toLowerCase()))return `${outlet.name} already has an active ${no}.`;
    const old=rows.find(r=>r.id===row.id);
    if(old&&(old.tableNo!==no||old.outlet!==row.outlet)){const id=posOutletIdOf(s,String(old.outlet));if(id&&orders(s).some(o=>o.outlet===id&&o.table===old.tableNo))return `${old.tableNo} has checks — its number and outlet cannot change. Retire it and add a new one.`;}
    return undefined;},
  store:tableStore,
};
export const POS_CATEGORY_SPEC:MasterSpec={
  id:POS_MST_CATEGORIES,label:'Menu categories',singular:'menu category',group:'pos',prefix:'MCAT',money:false,permission:'masters',
  help:'Menu sections in this order on the POS (Beverages, Breakfast, Mains…).',
  fields:[{...CODE_F,placeholder:'SOUPS'},{key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Soups'},SORT_F,ACTIVE_F],
  inUse:(s,row)=>{const n=(px(s)?.menu??[]).filter(m=>posItemCategoryCode(s,m)===row.code).length;return {count:n,where:n?[`Menu items ${n}`]:[]};},
  seed:posSeedCategories,
};
export const POS_ITEM_SPEC:MasterSpec={
  id:POS_MST_ITEMS,label:'Menu items',singular:'menu item',group:'pos',prefix:'MNU',money:false,permission:'masters',
  help:'The menu: price changes are dated and Owner-only; “sold out today” lapses at the next business date.',
  fields:[{...CODE_F,placeholder:'PANEER-TIKKA'},{key:'name',label:'Name',type:'text',required:true,maxLength:60,placeholder:'Paneer tikka'},
    {key:'category',label:'Category',type:'select',required:true,master:POS_MST_CATEGORIES},
    {key:'shortCode',label:'Short code',type:'text',maxLength:10,placeholder:'PT',help:'Quick key for the POS search.'},
    {key:'price',label:'Price',type:'datedMoney',help:'Before GST. A new price takes effect from its date; prices already in effect stay as they were.'},
    {key:'gstClass',label:'GST class',type:'select',options:POS_GST_CLASSES,money:true,help:'Food follows the outlet’s GST profile; a check holds one GST class. Illustrative — CA to confirm.'},
    {key:'veg',label:'Vegetarian',type:'toggle'},
    {key:'outlets',label:'Sold at',type:'multiselect',required:true,master:POS_MST_OUTLETS},
    {key:'station',label:'Kitchen station',type:'text',maxLength:20,placeholder:'Tandoor',help:'KOTs are split by station; empty = the outlet’s KOT station.'},
    {key:'soldOutToday',label:'Sold out today',type:'toggle',help:'Hidden from ordering until the next business date (the POS page has the same switch).'},
    SORT_F,ACTIVE_F],
  inUse:(s,row)=>{const n=orders(s).reduce((a,o)=>a+o.lines.filter(l=>l.item===row.id).length,0);return {count:n,where:n?[`Check lines ${n}`]:[]};},
  validate:(_s,row)=>{const sc=optStr(row.shortCode);if(sc&&!/^[A-Za-z0-9-]{1,10}$/.test(sc))return 'Short code: 1–10 letters, digits or “-”.';
    const st=optStr(row.station);if(st&&st.length>20)return 'Kitchen station: at most 20 characters.';return undefined;},
  store:itemStore,
};
/** Registered at module load (the POS handlers import this module). */
export const POS_MASTER_SPECS=[POS_OUTLET_SPEC,POS_TABLE_SPEC,POS_CATEGORY_SPEC,POS_ITEM_SPEC];
for(const sp of POS_MASTER_SPECS)registerMaster(sp);
/** Category rows in master order (POS category chips). */
export const posCategoryRows=(s:State)=>mstRows(s,POS_MST_CATEGORIES);
export const posCategoryByCode=(s:State,code:string)=>mstByCode(s,POS_MST_CATEGORIES,code);
