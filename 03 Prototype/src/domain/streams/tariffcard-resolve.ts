// F4-T1 · Tariff card rate resolution (F4-CONTRACT.md §1.3). Kept free of other stream imports: domain/streams/admin.ts (adminTariffOn →
// rateFor) loads it before the masters registry exists.
import type {State,TRCDaySet} from '../types';

/** A25 default: Friday, Saturday and Sunday nights are weekend nights (0 = Sun … 6 = Sat). */
export const TRC_WEEKEND_DEFAULT=[5,6,0];
const normDays=(d?:TRCDaySet)=>d??'all';
/** Weekend nights in force for a tariff row: the writing card version's set, else the slice default (Fri / Sat / Sun). */
export function trcWeekendDaysFor(s:State,cardVersion?:string):number[]{
  const x=s.tariffCardX,v=cardVersion?x?.versions?.find(v=>v.id===cardVersion):undefined;
  return v?.weekendDays?.length?v.weekendDays:x?.weekendDaysDefault?.length?x.weekendDaysDefault:TRC_WEEKEND_DEFAULT;
}
/** weekday / weekend of a night (the date the night starts). */
export function trcNightKind(date:string,weekend:number[]):'weekday'|'weekend'{return weekend.includes(new Date(date+'T12:00:00Z').getUTCDay())?'weekend':'weekday';}
/** Row for a night: the latest row with effectiveFrom ≤ night whose day set matches the night; a specific row beats an 'all' row of
 *  the same date. Rows without `days` behave exactly as before (they are 'all'). */
export function trcResolveRow<T extends {effectiveFrom:string;days?:TRCDaySet;cardVersion?:string}>(s:State,rows:T[],date:string):T|undefined{
  let best:T|undefined;
  for(const t of rows){
    if(t.effectiveFrom>date)continue;
    const d=normDays(t.days);
    if(d!=='all'&&trcNightKind(date,trcWeekendDaysFor(s,t.cardVersion))!==d)continue;
    if(!best||t.effectiveFrom>best.effectiveFrom||(t.effectiveFrom===best.effectiveFrom&&(normDays(best.days)==='all'||d!=='all')))best=t;
  }
  return best;
}
