// F12-RPT-REVENUE · one entry point for the digest lane and the tests: the pure rules and the red-flag-bearing compute functions of the six reports.
import type {State} from '../../types';
import {measOn} from '../../streams/measure';
import {addDays} from '../../money';
import {mrRefType} from './shared';
import {demandCompute} from './demand-calendar';
import {pickupCompute} from './pickup-pace';
import {washCompute} from './overbooking-wash';
import {restrictionCompute} from './restriction-effect';
import {parityCompute,activeCompetitors} from './rate-parity';
export * from './logic';
export {demandRow,demandCompute,DEMAND_COLS} from './demand-calendar';
export {pickupRow,pickupCompute,pickupAt,pickupNights,snapshotMonth,monthPlus,PICKUP_COLS} from './pickup-pace';
export {dispCompute,dispNights,DISP_COLS} from './displacement';
export {washCompute,overboughtByWeekday,WASH_COLS} from './overbooking-wash';
export {restrictionCompute,RESTR_COLS} from './restriction-effect';
export {parityCompute,activeCompetitors,PARITY_COLS} from './rate-parity';
export type {MrRow} from './shared';

/** Red rows of every revenue report for the Owner digest block "Measurement": [reportId, count]. Reports that are switched off are skipped. */
export function measRevenueRedCounts(s:State):[string,number][]{
  const out:[string,number][]=[];const red=(rows:{red:boolean}[])=>rows.filter(r=>r.red).length;
  if(measOn(s,'meas.rev.demand-calendar'))out.push(['rpt.revenue.demand-calendar',red(demandCompute(s,s.date,90))]);
  if(measOn(s,'meas.rev.pickup'))out.push(['rpt.revenue.pickup-pace',red(pickupCompute(s,6,''))]);
  if(measOn(s,'meas.rev.overbooking'))out.push(['rpt.revenue.overbooking-wash',red(washCompute(s,`${s.date.slice(0,4)}-01-01`,s.date,'',''))]);
  if(measOn(s,'meas.rev.restrictions'))out.push(['rpt.revenue.restriction-effect',red(restrictionCompute(s,addDays(s.date,-30),addDays(s.date,60),''))]);
  if(measOn(s,'meas.rev.rate-shop')&&(activeCompetitors(s).length||s.measX?.rateshop?.length))out.push(['rpt.revenue.rate-parity',red(parityCompute(s,s.date,30,mrRefType(s)))]);
  return out;
}
