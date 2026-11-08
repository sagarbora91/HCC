// F8 QW-KDS · kitchen display: pure card list over the existing POS kitchen board (posKitchenAt). No money, no new command (bump = POS_KOT_STATUS).
import type {State} from '../../services/mock';
import {posKitchenAt,posKotLabel,posSpot,posOutlet,posNextKotStatus} from './pos';
import type {POSKotStatus} from './pos';
import {posKotStation} from './pos-master';

/** QWQ-3 defaults, same for every outlet: amber after 10 minutes, red after 20 minutes (a KOT exactly at the limit has reached it). */
export const KDS_AMBER_MINUTES=10;
export const KDS_RED_MINUTES=20;
export type KdsLevel='ok'|'amber'|'red';
/** Text that always accompanies a level colour. */
export const KDS_LEVEL_TEXT:Record<KdsLevel,string>={ok:'On time',amber:'Slow',red:'Late'};
export type KdsCard={
  kotId:string;orderId:string;label:string;spot:string;outlet:string;outletName:string;station:string;status:POSKotStatus;
  /** Clock time the KOT was sent (simulated clock). */ sentAt:string;
  /** Minutes on the current step: sent to now while Preparing; ready to now while Ready (waiting to be served). Simulated clock. */ minutes:number;
  level:KdsLevel;levelText:string;next?:POSKotStatus;items:{name:string;qty:number}[]};
const mins=(a:string,b:string)=>Math.max(0,Math.round((Date.parse(b.slice(0,10)+'T'+b.slice(11,16)+':00Z')-Date.parse(a.slice(0,10)+'T'+a.slice(11,16)+':00Z'))/60000));
export const kdsLevel=(minutes:number):KdsLevel=>minutes>=KDS_RED_MINUTES?'red':minutes>=KDS_AMBER_MINUTES?'amber':'ok';
/** Open KOTs (Preparing, Ready) of an outlet ('' = all) and station ('' = all), oldest first. Cards carry names and quantities only. */
export function kdsCards(s:State,outlet='',station=''):KdsCard[]{
  return posKitchenAt(s,station).filter(x=>x.kot.status!=='Served'&&(!outlet||x.order.outlet===outlet))
    .map(({order:o,kot:k,lines})=>{
      const from=k.status==='Ready'?(k.readyAt??k.at):k.at,minutes=mins(from,s.clock),level=kdsLevel(minutes);
      return {kotId:k.id,orderId:o.id,label:posKotLabel(s,o,k),spot:posSpot(o),outlet:o.outlet,outletName:posOutlet(s,o.outlet)?.name??o.outlet,station:posKotStation(k),
        status:k.status,sentAt:k.at,minutes,level,levelText:KDS_LEVEL_TEXT[level],next:posNextKotStatus(k.status),
        items:lines.filter(l=>!l.voided).map(l=>({name:l.name,qty:l.qty}))};
    }).sort((a,b)=>a.sentAt.localeCompare(b.sentAt)||a.kotId.localeCompare(b.kotId));
}
