// F12-RPT-REVENUE · pure rules of the six revenue-management reports (catalogue §3.10, MEAS-CONTRACT §4). No State here: the report files
// build the inputs, the server twin (08 Production/src/Hcc.Reporting/Reports/MeasRevenue/MeasRevenueLogic.cs) has the same functions and numbers.
// Money = integer paise. Percentages are whole numbers (tenths where a boundary needs them). Medians and rounding are integer.

/** Demand calendar: forecast 85 % or more with the BAR unchanged, or forecast 25 % or less with no offer open. */
export const mrDemandRed=(forecastPct:number,barUnchanged:boolean,offerOpen:boolean)=>(forecastPct>=85&&barUnchanged)||(forecastPct<=25&&!offerOpen);
/** Pickup and pace: pace in tenths of a percent against last year's same snapshot; null when last year had nothing. */
export const mrPaceTenths=(cur:number,ly:number):number|null=>ly>0?Math.round((cur-ly)*1000/ly):null;
/** Worse than minus 10 % (exactly -10.0 % is not red). */
export const mrPaceRed=(tenths:number|null)=>tenths!==null&&tenths<-100;
/** Displacement: a net loss (the offered rate is below the counter-rate). */
export const mrDisplacementRed=(netPaise:number)=>netPaise<0;
/** Overbooking: rooms oversold above the allowed number. */
export const mrOversoldRed=(over:number,allowed:number)=>over>allowed;
/** Restrictions: the night stayed under 40 % occupancy (rooms ÷ sellable, integer compare). */
export const mrRestrictionRed=(rooms:number,sellable:number)=>sellable>0&&rooms*100<40*sellable;
/** Median of whole paise; even count = floor of the middle pair's mean. Empty = null. */
export function mrMedian(xs:number[]):number|null{
  if(!xs.length)return null;const a=[...xs].sort((x,y)=>x-y),m=a.length>>1;
  return a.length%2?a[m]:Math.floor((a[m-1]+a[m])/2);
}
/** Rate parity: OTA rate below direct; or our rate more than 15 % above the set median with occupancy under 40 %. */
export function mrParityRed(direct:number|null,ota:number|null,median:number|null,rooms:number,sellable:number):boolean{
  if(direct!==null&&ota!==null&&ota<direct)return true;
  return direct!==null&&median!==null&&direct*100>median*115&&mrRestrictionRed(rooms,sellable);
}
/** Our position against the set median in whole percent (+ above); null when no median. */
export const mrPositionPct=(ours:number|null,median:number|null)=>ours!==null&&median!==null&&median>0?Math.round((ours-median)*100/median):null;
/** Lead-time buckets of a booking (days between booking and arrival). */
export const MR_LEAD_BUCKETS=['0-7 days','8-30 days','31-90 days','Over 90 days'] as const;
export const mrLeadBucket=(days:number)=>days<=7?MR_LEAD_BUCKETS[0]:days<=30?MR_LEAD_BUCKETS[1]:days<=90?MR_LEAD_BUCKETS[2]:MR_LEAD_BUCKETS[3];
/** Displacement recommendation for one night: accept when the net is not a loss; counter when the break-even rate is within 150 % of the offer; else decline. */
export function mrRecommendation(netPaise:number,offeredPaise:number,counterPaise:number):'accept'|'counter-rate'|'decline'{
  if(netPaise>=0)return 'accept';
  return counterPaise*100<=offeredPaise*150?'counter-rate':'decline';
}
/** Per-night break-even counter rate: ceil((displaced revenue ÷ rooms) to ₹50). */
export const mrCounterRate=(displacedPaise:number,rooms:number)=>rooms>0?Math.ceil(Math.ceil(displacedPaise/rooms)/5000)*5000:0;
export const MR_WEEKDAYS=['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'];
export const mrDow=(d:string)=>new Date(d+'T12:00:00Z').getUTCDay();
export const mrWeekday=(d:string)=>MR_WEEKDAYS[mrDow(d)];
/** Whole-percent helper (Math.round of a ratio). */
export const mrPct=(a:number,b:number)=>b>0?Math.round(a*100/b):0;
