// MA-4 · POS masters on the server (08 Production PosMastersService / PosEndpoints): the MST_SERVER entries of the masters area
// (sync/masters.ts spreads POS_MST_SERVER into its table, so MST_MASTER_SAVE / _RETIRE / _REACTIVATE / _REORDER / _DELETE for these four
// lists go to the commands below). Outlets, tables and menu items are store-backed in posX: the POS area loader (sync/pos.ts) reads them
// (GET /pos/outlets with tableRows, GET /pos/menu) and records their versions; menu categories load into mastersX through the masters
// loader (GET /pos/menu-categories, paged). No change history is read on production yet (the server writes it to mst.master_history).
//
//   outlets         SaveOutlet / RetireOutlet / ReactivateOutlet / ReorderOutlets / DeleteOutlet          (ids kind 'outlet')
//   posTables       SaveOutletTable / RetireOutletTable / ReactivateOutletTable / ReorderOutletTables / DeleteOutletTable ('outletTable')
//   menuCategories  SaveMenuCategory / RetireMenuCategory / ReactivateMenuCategory / ReorderMenuCategories / DeleteMenuCategory ('menuCategory')
//   menuItems       SaveMenuItem / RetireMenuItem / ReactivateMenuItem / ReorderMenuItems / DeleteMenuItem ('menuItem')
// Codes in the prototype rows (outlet codes, category codes) are turned into server ids here; money stays integer paise.
import type {MasterRow,MstDatedAmount} from '../../domain/streams/masters';
import type {MstServerRoute} from './masters';
import type {CommandCtx} from './types';
import {HccClientError} from '../errors';
import {protoId,serverId} from './map';

/** GET /pos/menu-categories item (PosMastersContracts.cs MenuCategoryView). */
export type SrvMenuCategory={categoryId:number;code:string;name:string;sortOrder:number;active:boolean;retiredOn:string|null;items:number;version:string};
export function categoryFromServer(x:Record<string,unknown>):MasterRow{
  const c=x as unknown as SrvMenuCategory;
  const r:MasterRow={id:protoId(c.categoryId),code:c.code,name:c.name,sortOrder:c.sortOrder,active:c.active};
  if(c.retiredOn)r.retiredOn=c.retiredOn;
  return r;
}
const str=(v:unknown)=>{const t=typeof v==='string'?v.trim():'';return t||null;};
function outletIdOf(c:CommandCtx,code:unknown):number{
  const o=(c.state.posX?.outlets??[]).find(x=>(x.code??'')===String(code));
  if(!o)throw new HccClientError('VALIDATION',`Outlet ${String(code??'')} is not on the server. Refresh and try again.`);
  return serverId(o.id,'Outlet');
}
function categoryIdOf(c:CommandCtx,code:unknown):number{
  const r=(c.state.mastersX?.rows?.menuCategories??[]).find(x=>x.code===String(code));
  if(!r)throw new HccClientError('VALIDATION',`Menu category ${String(code??'')} is not on the server. Refresh and try again.`);
  return serverId(r.id,'Menu category');
}
export function outletBody(row:Record<string,unknown>,_c:CommandCtx){
  return {type:String(row.type??'restaurant'),opens:str(row.opens),closes:str(row.closes),gstProfile:String(row.gstProfile??'hotel'),kotStation:str(row.kotStation)};
}
export function tableBody(row:Record<string,unknown>,c:CommandCtx){
  const seats=row.seats===undefined||row.seats===null||row.seats===''?null:Number(row.seats);
  return {outletId:outletIdOf(c,row.outlet),tableNo:String(row.tableNo??'').trim(),seats,section:str(row.section),counter:!!row.counter};
}
export function itemBody(row:Record<string,unknown>,c:CommandCtx){
  const prices=(Array.isArray(row.price)?row.price as MstDatedAmount[]:[]).map(p=>({effectiveFrom:p.effectiveFrom,amountPaise:Number(p.amount)}));
  const today=c.state.date,inForce=[...prices].filter(p=>p.effectiveFrom<=today).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1).pop();
  const categoryId=categoryIdOf(c,row.category);
  const category=(c.state.mastersX?.rows?.menuCategories??[]).find(x=>x.id===String(categoryId))?.name??String(row.category??'');
  return {category,categoryId,pricePaise:inForce?.amountPaise??prices[0]?.amountPaise??0,prices,veg:!!row.veg,
    outletIds:(Array.isArray(row.outlets)?row.outlets as string[]:[]).map(x=>outletIdOf(c,x)),
    shortCode:str(row.shortCode),gstClass:String(row.gstClass??'food'),station:str(row.station),soldOutToday:!!row.soldOutToday};
}
export const POS_MST_SERVER:Record<string,MstServerRoute>={
  outlets:{list:'GET /pos/outlets',listPath:'/pos/outlets',kind:'outlet',idField:'outletId',idsField:'outletIds',loadedElsewhere:true,reload:['posX'],toBody:outletBody,
    save:'SaveOutlet',retire:'RetireOutlet',reactivate:'ReactivateOutlet',reorder:'ReorderOutlets',remove:'DeleteOutlet'},
  posTables:{list:'GET /pos/outlets',listPath:'/pos/outlets',kind:'outletTable',idField:'tableId',idsField:'tableIds',loadedElsewhere:true,reload:['posX'],toBody:tableBody,
    save:'SaveOutletTable',retire:'RetireOutletTable',reactivate:'ReactivateOutletTable',reorder:'ReorderOutletTables',remove:'DeleteOutletTable'},
  menuCategories:{list:'GET /pos/menu-categories',listPath:'/pos/menu-categories',kind:'menuCategory',idField:'categoryId',idsField:'categoryIds',fromServer:categoryFromServer,reload:['posX'],
    save:'SaveMenuCategory',retire:'RetireMenuCategory',reactivate:'ReactivateMenuCategory',reorder:'ReorderMenuCategories',remove:'DeleteMenuCategory'},
  menuItems:{list:'GET /pos/menu',listPath:'/pos/menu',kind:'menuItem',idField:'itemId',idsField:'itemIds',loadedElsewhere:true,reload:['posX'],toBody:itemBody,
    save:'SaveMenuItem',retire:'RetireMenuItem',reactivate:'ReactivateMenuItem',reorder:'ReorderMenuItems',remove:'DeleteMenuItem'},
};
