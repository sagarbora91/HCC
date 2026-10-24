// Module 16 · Linen, laundry & stock — pure helpers, selectors and the state slice type. Exported names MUST start with 'inv' / 'INV' (shared mock namespace).
import type {State} from '../types';
import {addDays} from '../money';
import {can} from '../selectors';

export type INVLoc='store'|'floor1'|'floor2'|'floor3'|'rooms'|'soiled'|'laundry';
export type INVKind='linen'|'amenity'|'cleaning';
export type INVItem={id:string;name:string;kind:INVKind;unit:string;
  /** Standard (weighted-average) cost per unit, paise. Shown only to money roles. */
  cost:number;
  /** Minimum clean stock (store + floor pantries) before a reorder alert. */
  min:number;reorderQty:number;
  /** Linen pieces per room, by room type. */
  par?:Record<string,number>;
  /** Standard use per occupied room-night × 100 (e.g. 150 = 1.5). */
  stdX100?:number;
  /** Laundry vendor rate per piece, paise (linen only). */
  laundryRate?:number;
  stock:Partial<Record<INVLoc,number>>;supplier?:string};
export type INVMoveKind='grn'|'issue'|'use'|'room-change'|'laundry-out'|'laundry-in'|'loss'|'count';
export type INVMove={id:string;date:string;at:string;kind:INVMoveKind;item:string;qty:number;from?:string;to?:string;ref?:string;reason?:string;actor:string};
export type INVBatchLine={item:string;sent:number;received?:number;damaged?:number};
export type INVBatch={id:string;vendor:string;sent:string;sentAt:string;lines:INVBatchLine[];status:'Out'|'Returned'|'Returned short';returned?:string;actor:string;receivedBy?:string;note?:string;
  /** Laundry charge (paise) computed at return. */ charge?:number};
export type INVLoss={id:string;date:string;item:string;loc:string;qty:number;reason:string;note?:string;actor:string;value:number;ref?:string};
export type INVCountLine={item:string;system:number;counted:number};
export type INVCount={id:string;date:string;at:string;loc:INVLoc;lines:INVCountLine[];status:'Submitted'|'Posted'|'Rejected';actor:string;reviewer?:string;reason?:string;note?:string};
export type INVGrn={id:string;date:string;supplier:string;ref:string;lines:{item:string;qty:number;cost:number}[];actor:string};
/** State slice for this module, stored at s.inventoryX (optional; absent in old saves → treat as empty). */
export type INVState={items:INVItem[];moves:INVMove[];batches:INVBatch[];losses:INVLoss[];counts:INVCount[];grns:INVGrn[];
  /** Linen par sets in circulation (in room, in laundry, on shelf). */ parSets?:number;
  vendor?:{name:string;turnaroundDays:number};
  /** Fictional occupied room-nights before go-live (dates without bookings in the fixture). */ occHistory?:Record<string,number>};
export const invEmpty=():INVState=>({items:[],moves:[],batches:[],losses:[],counts:[],grns:[]});
export const invSlice=(s:State):INVState=>({...invEmpty(),...(s.inventoryX??{})});

export const INV_LOCS:{id:INVLoc;label:string;short:string}[]=[
  {id:'store',label:'Main store',short:'Store'},{id:'floor1',label:'Floor 1 pantry',short:'F1'},{id:'floor2',label:'Floor 2 pantry',short:'F2'},{id:'floor3',label:'Floor 3 pantry',short:'F3'},
  {id:'rooms',label:'In rooms',short:'Rooms'},{id:'soiled',label:'Soiled (to send)',short:'Soiled'},{id:'laundry',label:'At laundry',short:'Laundry'}];
export const INV_FLOORS:INVLoc[]=['floor1','floor2','floor3'];
/** Locations where stock sits on a shelf and can be physically counted. */
export const INV_COUNTABLE:INVLoc[]=['store','floor1','floor2','floor3'];
export const INV_KINDS:{id:INVKind;label:string}[]=[{id:'linen',label:'Linen'},{id:'amenity',label:'Amenities'},{id:'cleaning',label:'Cleaning supplies'}];
export const INV_LOSS_REASONS=['Damaged','Stained beyond use','Missing','Taken by guest','Expired','Other'] as const;
export const INV_SUPPLIERS=['Morning Supplies','Deccan Hygiene Traders','Kaveri Textiles (fictional)'];
export const INV_MAX_QTY=10000;
/** Consumption variance beyond this (percent) is flagged. */
export const INV_VARIANCE_FLAG=15;

export const invLocLabel=(l:string)=>INV_LOCS.find(x=>x.id===l)?.label??l;
export const invItem=(s:State,id:string)=>invSlice(s).items.find(i=>i.id===id);
export const invQty=(it:INVItem,l:INVLoc)=>it.stock[l]??0;
/** Clean usable stock: main store + floor pantries. */
export const invClean=(it:INVItem)=>invQty(it,'store')+INV_FLOORS.reduce((a,f)=>a+invQty(it,f),0);
/** Every piece the hotel owns (all locations, incl. rooms, soiled and laundry). */
export const invTotal=(it:INVItem)=>INV_LOCS.reduce((a,l)=>a+invQty(it,l.id),0);
export const invValue=(it:INVItem,qty=invTotal(it))=>qty*it.cost;
/** Money (stock value, costs, charges) is only shown to roles with reports or expenses. */
export const invMoneyRole=(s:State,uid:string)=>can(s,uid,'reports')||can(s,uid,'expenses');
export function invRoomsByType(s:State){const m:Record<string,number>={};s.rooms.forEach(r=>{m[r.type]=(m[r.type]||0)+1;});return m;}
export const invRoomTypes=(s:State)=>Object.keys(invRoomsByType(s)).sort();
/** Linen required in circulation = Σ rooms of type × par × par sets. */
export function invParRequired(s:State,it:INVItem){if(!it.par)return 0;const rt=invRoomsByType(s),sets=invSlice(s).parSets??3;return Object.entries(rt).reduce((a,[t,n])=>a+n*(it.par![t]||0),0)*sets;}
/** Pieces needed to dress every room once (in-room par). */
export function invParPerSet(s:State,it:INVItem){if(!it.par)return 0;const rt=invRoomsByType(s);return Object.entries(rt).reduce((a,[t,n])=>a+n*(it.par![t]||0),0);}
export function invParCoverage(s:State,it:INVItem){const req=invParRequired(s,it);return req?Math.round(invTotal(it)/req*100):100;}
export type INVAlert={item:INVItem;clean:number;suggest:number};
export function invAlerts(s:State):INVAlert[]{return invSlice(s).items.filter(i=>invClean(i)<i.min).map(i=>({item:i,clean:invClean(i),suggest:Math.max(i.reorderQty,2*i.min-invClean(i))}));}
export const invOpenBatches=(s:State)=>invSlice(s).batches.filter(b=>b.status==='Out');
export const invPiecesOut=(s:State)=>invOpenBatches(s).reduce((a,b)=>a+b.lines.reduce((x,l)=>x+l.sent,0),0);
export const invMissing=(l:INVBatchLine)=>l.received===undefined?0:Math.max(0,l.sent-(l.received||0)-(l.damaged||0));
export const invPendingCounts=(s:State)=>invSlice(s).counts.filter(c=>c.status==='Submitted');
export const invCountVariance=(c:INVCount)=>c.lines.filter(l=>l.counted!==l.system);
export function invCountValue(s:State,c:INVCount){return c.lines.reduce((a,l)=>a+(l.counted-l.system)*(invItem(s,l.item)?.cost??0),0);}
/** Items that live at a location (linen everywhere; supplies never in rooms/soiled/laundry). */
export function invItemsAt(s:State,l:INVLoc){return invSlice(s).items.filter(i=>i.kind==='linen'||INV_COUNTABLE.includes(l));}
/** Occupied room-nights for a business date: stays In house / Departed covering the night; fixture history for earlier dates. */
export function invOccupied(s:State,date:string){
  if(date>s.date)return 0;
  const live=s.bookings.filter(b=>(b.status==='In house'||b.status==='Departed')&&b.arrival<=date&&b.departure>date).length;
  return live||(invSlice(s).occHistory?.[date]??0);
}
export function invDates(from:string,to:string){const out:string[]=[];for(let d=from;d<=to&&out.length<93;d=addDays(d,1))out.push(d);return out;}
export type INVConsumptionRow={item:INVItem;used:number;perRN:number;std:number;variance:number|null;flag:boolean};
/** Consumption per occupied room-night (amenities/cleaning: use moves; linen: room-change moves). */
export function invConsumption(s:State,from:string,to:string){
  const dates=invDates(from,to),rn=dates.reduce((a,d)=>a+invOccupied(s,d),0),x=invSlice(s);
  const rows:INVConsumptionRow[]=x.items.map(item=>{
    const kind=item.kind==='linen'?'room-change':'use';
    const used=x.moves.filter(m=>m.item===item.id&&m.kind===kind&&m.date>=from&&m.date<=to).reduce((a,m)=>a+m.qty,0);
    const perRN=rn?used/rn:0,std=(item.stdX100??0)/100;
    const variance=std&&rn?Math.round((perRN-std)/std*100):null;
    return {item,used,perRN,std,variance,flag:variance!==null&&Math.abs(variance)>INV_VARIANCE_FLAG};
  });
  return {dates,rn,rows};
}
export const invFmt=(n:number,d=2)=>Number.isInteger(n)?String(n):n.toFixed(d);
