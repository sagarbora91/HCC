// F12-RPT-GUEST · rpt.guests.reputation-index — Online reputation (data gsv.review_month / s.measX.reviews). Switch meas.guest.reputation.
import type {State} from '../../types';
import type {MeasReviewMonth} from '../../streams/measure-types';
import {registerReport} from '../catalogue';
import {T,I,P,pct1,prevMonth,capNote} from './shared';

export const REPUTATION_COLS=[T('month','Month'),T('platform','Platform'),I('reviews','Reviews received'),T('avgScore','Average score'),T('score12m','12-month score'),P('responseRate','Response rate'),T('themes','Themes')];
const BASIS='One row per month and platform for the financial year (April to March). Scores 0-5 with two decimals. Response rate = reviews answered / reviews received.';
const NEED='gsv.review_month (meas-cap-guest)';
const PLATFORMS=['google','agoda','makemytrip','booking','other'];
const h=(v:number)=>Math.round(v*100);
export const DROP_HUNDREDTHS=30;export const RESPONSE_FLOOR_PCT=80;
/** Exception rule (red row): score drop of 0.3 or more against the previous month of the same platform; response rate under 80 %. */
export function reputationRed(r:MeasReviewMonth,prev?:MeasReviewMonth){
  const drop=!!prev&&h(prev.avgScore)-h(r.avgScore)>=DROP_HUNDREDTHS;
  const low=r.count>0&&r.responded*100<RESPONSE_FLOOR_PCT*r.count;
  return {drop,lowResponse:low,red:drop||low};
}
export const reviewsOf=(s:State,from:string,to:string,platform?:string)=>(s.measX?.reviews??[]).filter(r=>r.month>=from&&r.month<=to&&(!platform||r.platform===platform))
  .sort((a,b)=>a.month.localeCompare(b.month)||a.platform.localeCompare(b.platform));
export const prevReview=(s:State,r:MeasReviewMonth)=>(s.measX?.reviews??[]).find(x=>x.platform===r.platform&&x.month===prevMonth(r.month));
export const reputation=registerReport({
  id:'rpt.guests.reputation-index',title:'Online reputation',area:'guests',priority:'P2',status:'MEAS',
  moneyFlag:false,viewPerms:['PERM-RPT-001'],dateClass:'D-FY',
  params:[{key:'fy',label:'Financial year',type:'fy',dateClass:'D-FY'},{key:'platform',label:'Platform',type:'enum',options:PLATFORMS}],
  columns:REPUTATION_COLS,dateBasis:BASIS,formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{
    const list=reviewsOf(s,p.from!.slice(0,7),p.to!.slice(0,7),p.platform as string|undefined);
    if(!list.length)return {params:p,columns:REPUTATION_COLS,rows:[],about:{dateBasis:capNote(BASIS,NEED)}};
    // MEAS2-B (M1): red row per reputationRed against the previous month of the same platform.
    return {params:p,columns:REPUTATION_COLS,rows:list.map(r=>({kind:'row' as const,...(reputationRed(r,prevReview(s,r)).red?{group:'red'}:{}),cells:{month:r.month,platform:r.platform,reviews:r.count,avgScore:r.avgScore.toFixed(2),
      score12m:r.score12m===undefined?null:r.score12m.toFixed(2),responseRate:pct1(r.responded,r.count),themes:r.themes??''}}))};
  },
});
