// Overview / Home headline figures. Prototype: metrics(s) over the state. Production (Wave 4 gap G3): occupancy, receipts, posted expenses and
// expected drawer cash come from the server's report for the business date (users with the reports permission); until it arrives the tiles
// show a loading state (`live.ready` false), never a client sum over partly loaded slices.
import {metrics,type State} from '../services/mock';
import {useLiveMetrics} from './liveMetrics';

export type LiveMeta={ready:boolean;loading:boolean;error?:string;asOfUtc?:string;drawer?:number};
export type Metrics=ReturnType<typeof metrics>&{live?:LiveMeta};

export function useMetrics(s:State,uid:string):Metrics{
  const base=metrics(s);
  // A constant branch (folded at build time), so the prototype bundle never references the sync layer.
  return process.env.HCC_BUILD==='production'?useLiveMetrics(s,uid,base):base; // eslint-disable-line react-hooks/rules-of-hooks
}
