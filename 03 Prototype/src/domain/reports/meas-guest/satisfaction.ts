// F12-RPT-GUEST · rpt.guests.satisfaction — Guest satisfaction and NPS (catalogue 3.10; data gsv.survey / s.measX.surveys). Switch meas.guest.survey.
// NPS = %9-10 minus %0-6 on the recommend score, rounded half up; target meas.target.nps (default 30). A survey row never carries guest identity;
// the verbatim column lists comments of surveys with consent only.
import type {State} from '../../types';
import type {MeasSurvey} from '../../streams/measure-types';
import {registerReport} from '../catalogue';
import {measTarget} from '../../streams/measure';
import {gsvSlice} from '../../streams/services';
import {T,I,P,roundHalf,pct1,avg1,minutesBetween,capNote} from './shared';

export const SATISFACTION_COLS=[T('month','Month'),I('responses','Responses'),P('responseRate','Response rate'),T('overall','Overall'),T('cleanliness','Cleanliness'),T('staff','Staff'),T('value','Value'),T('breakfast','Breakfast'),I('nps','NPS'),T('verbatim','Verbatim')];
const BASIS='One row for the month by the survey date (guest identity is never shown). Scores are averages of 1-5; NPS = % scoring 9-10 minus % scoring 0-6 on the recommend question; response rate = surveys / stays that left in the month. Verbatim lists comments of guests who gave consent.';
const NEED='gsv.survey (meas-cap-guest)';
export const UNANSWERED_MINUTES=48*60;
/** Review H3: fewer answers than this for the chosen filters show counts only (same floor as the staff pulse). */
export const SURVEY_MIN_ANSWERS=5;
const FEW='Fewer than 5 responses: scores hidden';
const FLOOR_NOTE=' Fewer than 5 responses for the filters: scores, NPS and comments are hidden so no single guest can be read.';

export const surveyLow=(v:MeasSurvey)=>Math.min(v.overall,v.cleanliness,v.staff,v.value,v.breakfast??5)<=2;
export function npsOf(list:{recommend:number}[]):number|null{
  const n=list.length;if(!n)return null;
  return roundHalf((list.filter(v=>v.recommend>=9).length-list.filter(v=>v.recommend<=6).length)*100/n);
}
export function surveysOf(s:State,month:string,roomType?:string,channel?:string):MeasSurvey[]{
  return (s.measX?.surveys??[]).filter(v=>v.at.slice(0,7)===month&&(!roomType||v.roomType===roomType)&&(!channel||v.channel===channel));
}
/** A low score is answered once a complaint is raised on the same booking at or after the survey. */
export function lowUnanswered(s:State,list:MeasSurvey[]):MeasSurvey[]{
  const cs=gsvSlice(s).complaints;
  return list.filter(v=>surveyLow(v)&&minutesBetween(v.at,s.clock)>=UNANSWERED_MINUTES&&!(v.booking&&cs.some(c=>c.booking===v.booking&&c.createdAt>=v.at)));
}
/** Exception rule (red row): NPS below target, or any score of 2 or lower unanswered for 48 hours. */
export function satisfactionExceptions(s:State,month:string,roomType?:string,channel?:string){
  const list=surveysOf(s,month,roomType,channel),nps=npsOf(list),target=measTarget(s,'meas.target.nps')??30,un=lowUnanswered(s,list);
  return {responses:list.length,nps,target,npsBelowTarget:nps!==null&&nps<target,unanswered:un.length,red:nps!==null&&nps<target||un.length>0};
}
export const satisfaction=registerReport({
  id:'rpt.guests.satisfaction',title:'Guest satisfaction and NPS',area:'guests',priority:'P1',status:'MEAS',
  moneyFlag:false,viewPerms:['PERM-RPT-001'],dateClass:'D-M',
  params:[{key:'month',label:'Month',type:'month',dateClass:'D-M'},{key:'roomType',label:'Room type',type:'text'},{key:'channel',label:'Channel',type:'text'}],
  columns:SATISFACTION_COLS,dateBasis:BASIS,formats:['screen','xlsx','pdf','csv'],phone:'yes',
  select:(s,_uid,p)=>{
    const month=p.month as string,rt=p.roomType as string|undefined,ch=p.channel as string|undefined,list=surveysOf(s,month,rt,ch);
    if(!list.length)return {params:p,columns:SATISFACTION_COLS,rows:[],about:{dateBasis:capNote(BASIS,NEED)}};
    const left=s.bookings.filter(b=>b.status==='Departed'&&b.departure.slice(0,7)===month&&(!rt||s.rooms.find(r=>r.id===b.room)?.type===rt)&&(!ch||b.source===ch)).length;
    // Review H3: below SURVEY_MIN_ANSWERS a filtered row could name one guest, so it shows counts only (no scores, NPS or comments).
    if(list.length<SURVEY_MIN_ANSWERS)return {params:p,columns:SATISFACTION_COLS,rows:[{kind:'row',cells:{month,responses:list.length,responseRate:pct1(list.length,left),overall:null,
      cleanliness:null,staff:null,value:null,breakfast:null,nps:null,verbatim:FEW}}],about:{dateBasis:BASIS+FLOOR_NOTE}};
    const sum=(f:(v:MeasSurvey)=>number)=>list.reduce((a,v)=>a+f(v),0);
    const bf=list.filter(v=>v.breakfast!==undefined);
    const verbatim=list.filter(v=>v.consent&&v.comment).slice(0,5).map(v=>v.comment!.trim()).join(' | ');
    const nps=npsOf(list),red=nps!==null&&nps<(measTarget(s,'meas.target.nps')??30);
    return {params:p,columns:SATISFACTION_COLS,rows:[{kind:'row',...(red?{group:'red'}:{}),cells:{month,responses:list.length,responseRate:pct1(list.length,left),overall:avg1(sum(v=>v.overall),list.length),
      cleanliness:avg1(sum(v=>v.cleanliness),list.length),staff:avg1(sum(v=>v.staff),list.length),value:avg1(sum(v=>v.value),list.length),
      breakfast:avg1(bf.reduce((a,v)=>a+v.breakfast!,0),bf.length),nps:npsOf(list),verbatim}}]};
  },
});
