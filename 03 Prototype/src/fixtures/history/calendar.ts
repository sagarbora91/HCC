// Demand model for the FY 2026-27 history (plan §3). Data tables only; pure functions of the date.
/** Base occupancy by month (percentage points). */
export const MONTH_OCCUPANCY:Record<string,number>={'04':62,'05':55,'06':48,'07':45,'08':50,'09':58,'10':66};
/** Day-of-week adjustment in points (0 = Sunday). */
export const DOW_OCCUPANCY:Record<number,number>={5:6,6:10,0:-8};
export type Festival={name:string;from:string;to:string;pts:number};
export const FESTIVALS:Festival[]=[
  {name:'Ganesh festival',from:'2026-09-14',to:'2026-09-24',pts:12},
  {name:'Navratri / Dussehra',from:'2026-10-11',to:'2026-10-20',pts:10},
];
/** 0 = Sunday … 6 = Saturday. */
export const dayOfWeek=(date:string)=>new Date(date+'T12:00:00Z').getUTCDay();
export const isWeekend=(date:string)=>{const d=dayOfWeek(date);return d===5||d===6||d===0;};
export const festivalOn=(date:string)=>FESTIVALS.find(f=>date>=f.from&&date<=f.to);
/** Target share of sellable rooms occupied on the night of `date` (0…1). */
export function occupancyTarget(date:string):number{
  const pts=(MONTH_OCCUPANCY[date.slice(5,7)]??50)+(DOW_OCCUPANCY[dayOfWeek(date)]??0)+(festivalOn(date)?.pts??0);
  return Math.max(0.15,Math.min(0.97,pts/100));
}
/** Rate multiplier in percent for a stay arriving on `date` (season, festival, weekend). */
export function rateFactorPct(date:string):number{
  const month=date.slice(5,7);
  let pct=month==='06'||month==='08'?92:month==='07'?90:100;
  if(festivalOn(date))pct+=10;
  const d=dayOfWeek(date);
  if(d===5||d===6)pct+=8;else if(d===0)pct-=5;
  return pct;
}
/** Stay-length buckets: 1 night 45 %, 2 30 %, 3 12 %, 4–6 10 %, 7+ 3 % (plan §3). */
export const STAY_LENGTH_BUCKETS:readonly (readonly [[number,number],number])[]=[[[1,1],45],[[2,2],30],[[3,3],12],[[4,6],10],[[7,14],3]];
/** Lead time in days before arrival: walk-in 15 %, same week 45 %, 1–4 weeks 30 %, over 4 weeks 10 %. */
export const LEAD_TIME_BUCKETS:readonly (readonly [[number,number],number])[]=[[[0,0],15],[[1,6],45],[[7,28],30],[[29,60],10]];
/** Nightly rate (paise) for a room with rack `rack` for a stay arriving on `date`: season/festival/weekend factor, optional extra adjustment in %, ₹50 steps, never below ₹1,000. */
export function stayRate(rack:number,date:string,adjPct=0):number{
  return Math.max(Math.round(rack*(rateFactorPct(date)+adjPct)/100/5000)*5000,100000);
}
