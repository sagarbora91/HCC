// Production only: today's headline figures from the server's report (see useMetrics.ts).
import {can,type State} from '../services/mock';
import {useServerReport} from '../features/reports/serverBits';
import {todayFigures} from '../clients/sync/reports';
import type {Metrics} from './useMetrics';

export function useLiveMetrics(s:State,uid:string,base:Metrics):Metrics{
  const enabled=can(s,uid,'reports')&&/^d{4}-d{2}-d{2}$/.test(s.date);
  const f=useServerReport(s.date,s.date,enabled);
  if(!enabled)return base;
  if(!f.data)return {...base,live:{ready:false,loading:f.status!=='error',error:f.error}};
  const t=todayFigures(f.data);
  return {...base,occupied:t.occupied,sellable:t.sellable,occupancy:t.occupancy,collections:t.collections,expenses:t.expenses,
    live:{ready:true,loading:f.status==='loading',error:f.status==='error'?f.error??'unreachable':undefined,asOfUtc:t.asOfUtc,drawer:t.drawer}};
}
