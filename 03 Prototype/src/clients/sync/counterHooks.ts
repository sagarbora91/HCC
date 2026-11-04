// AF-CTR-UI (HIS-01): older counter bills by invoice number / date range. Production reads GET /counter-bills?invoiceNo=&from=&to= on demand
// (the list loader keeps only the newest 500); the prototype filters its local list. Imported by the counter-bill screens, used only from
// `process.env.HCC_BUILD==='production'` branches (reportsHooks.ts pattern).
import {useServerFetch,type Fetched} from './reportsHooks';
import {counterBillLookup} from './lfmoney';
import type {CTRBill,State} from '../../domain/types';
import {catExtras,catExtraPrice} from '../../domain/streams/catalogue';
import {posItemActive,posItemPrice} from '../../domain/streams/pos-master';
import {mnyNames,mnyIsDrawer} from '../../domain/streams/money-master';

export type CounterLookupQuery={invoiceNo?:string;from?:string;to?:string};
/** True when the query has at least one criterion (an empty Find does nothing). */
export const counterLookupActive=(q:CounterLookupQuery)=>!!(q.invoiceNo?.trim()||q.from||q.to);

/** Production lookup: idle until `enabled` and the query has a criterion; refetches with the runtime's data stamp (useServerFetch). */
export function useCounterBillLookup(query:CounterLookupQuery,enabled:boolean):Fetched<CTRBill[]>{
  const on=enabled&&counterLookupActive(query);
  return useServerFetch<CTRBill[]>(on?`ctr|${query.invoiceNo?.trim()??''}|${query.from??''}|${query.to??''}`:null,api=>counterBillLookup(api,query));
}

/** Prototype lookup: a local filter over the bills (invoice number contains, business date within from..to). Newest first. */
export function ctrLookupLocal(bills:CTRBill[],q:CounterLookupQuery):CTRBill[]{
  const inv=(q.invoiceNo??'').trim().toLowerCase();
  return bills.filter(b=>(!inv||b.no.toLowerCase().includes(inv))&&(!q.from||b.date>=q.from)&&(!q.to||b.date<=q.to)).slice().reverse();
}

/** UX-01: one entry of the "From catalogue / menu" picker of a counter-bill line (the data the lines already read: ctrLineFrom). The price is only a
 *  suggestion — the line's unit price stays editable (DEC-241 is pending: no approval, no block). */
export type CtrPick={key:string;label:string;unit?:number;gst:string;extraId?:string;menuItemId?:string};
export function ctrCatalogueOptions(s:State):CtrPick[]{
  const out:CtrPick[]=[];
  for(const x of catExtras(s,true))out.push({key:'e:'+x.id,label:x.name,unit:catExtraPrice(x,s.date),gst:x.gst??'other',extraId:x.id});
  for(const m of s.posX?.menu??[])if(posItemActive(m))out.push({key:'m:'+m.id,label:m.name+' (menu)',unit:posItemPrice(s,m,s.date),gst:'food',menuItemId:m.id});
  return out;
}
/** Front-desk payment methods of the master (AF-DEC-2) and the default one: the first drawer method (cash), else the first. */
export const ctrMethods=(s:State)=>mnyNames(s,'frontdesk');
export const ctrDefaultMethod=(s:State)=>{const l=ctrMethods(s);return l.find(m=>mnyIsDrawer(s,m))??l[0]??'';};
