// AF-CTR-UI (HIS-01): older counter bills by invoice number / date range. Production reads GET /counter-bills?invoiceNo=&from=&to= on demand
// (the list loader keeps only the newest 500); the prototype filters its local list. Imported by the counter-bill screens, used only from
// `process.env.HCC_BUILD==='production'` branches (reportsHooks.ts pattern). The pure helpers (local filter, pickers) live in features/lfmoney/counterLocal.ts
// so the prototype bundle never pulls the sync layer in through this file (tests/stream-clients.mjs W4-0).
import {useServerFetch,type Fetched} from './reportsHooks';
import {counterBillLookup} from './lfmoney';
import type {CTRBill} from '../../domain/types';

import type {CounterLookupQuery} from '../../features/lfmoney/counterLocal';
/** Production lookup: idle until `enabled` and the query has a criterion; refetches with the runtime's data stamp (useServerFetch). */
export function useCounterBillLookup(query:CounterLookupQuery,enabled:boolean):Fetched<CTRBill[]>{
  const on=enabled&&!!(query.invoiceNo?.trim()||query.from||query.to);
  return useServerFetch<CTRBill[]>(on?`ctr|${query.invoiceNo?.trim()??''}|${query.from??''}|${query.to??''}`:null,api=>counterBillLookup(api,query));
}

