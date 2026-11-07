// F12-RPT-PEOPLE · rpt.people.agent-scorecard — performance by desk user (catalogue §3.10, switch meas.people.scorecard). Owner and Manager only.
// Data: the command audit log (check-ins, check-outs, upsell captures), stay rate changes, discount approvals, day closings (cash counted by), complaints, turn-away log, desk surveys.
// Money columns: discount amount and cash variance (rule M removes them without the money gate). Server twin: AgentScorecardReport.cs.
import type {State} from '../../types';
import type {RptRow} from '../types';
import {registerReport} from '../catalogue';
import {strState} from '../../streams/stay-rate';
import {gsvSlice} from '../../streams/services';
import {T,I,P,M,EXC,pct1,aboveUpperQuartile,ownerManagerOnly,row} from './shared';

export const SCORECARD_ID='rpt.people.agent-scorecard';
export const SCORECARD_COLS=[T('user','User'),I('checkins','Check-ins'),I('checkouts','Check-outs'),I('rateEdits','Rate edits'),I('discountCount','Discounts (count)'),M('discountAmount','Discounts (Rs)'),
  P('discountReasonPct','Discounts with a reason (%)'),I('comps','Comps requested'),T('upsellPerArrival','Upsell per arrival'),I('complaints','Complaints attributed'),M('cashVariance','Cash variances (Rs)'),
  I('regrets','Regrets logged'),T('surveyScore','Survey score'),EXC];

export type ScoreFigures={checkins:number;checkouts:number;rateEdits:number;discountCount:number;discountAmount:number;withReason:number;comps:number;upsells:number;complaints:number;cashVariance:number;regrets:number;surveyTotal:number;surveyCount:number};
const zero=():ScoreFigures=>({checkins:0,checkouts:0,rateEdits:0,discountCount:0,discountAmount:0,withReason:0,comps:0,upsells:0,complaints:0,cashVariance:0,regrets:0,surveyTotal:0,surveyCount:0});

/** Red row: discounts (Rs) or cash variances (Rs) above the team's upper quartile; needs a team of at least 4 users with activity. */
export function scorecardRed(team:ScoreFigures[],f:ScoreFigures):boolean{
  return aboveUpperQuartile(team.map(t=>t.discountAmount),f.discountAmount)||aboveUpperQuartile(team.map(t=>t.cashVariance),f.cashVariance);
}
const active=(f:ScoreFigures)=>Object.values(f).some(v=>v>0);

export function scorecardFigures(s:State,from:string,to:string):Map<string,ScoreFigures>{
  const byUser=new Map<string,ScoreFigures>();
  const get=(uid:string)=>{let f=byUser.get(uid);if(!f){f=zero();byUser.set(uid,f);}return f;};
  const uidOf=(who?:string)=>who?s.users.find(u=>u.id===who||u.name===who||u.name.split(' · ')[0]===who)?.id:undefined;
  const inRange=(d:string)=>d>=from&&d<=to;
  for(const a of s.audit){
    if(!inRange(a.at.slice(0,10)))continue;const u=uidOf(a.actor);if(!u)continue;
    if(a.action==='CHECKIN')get(u).checkins++;
    else if(a.action==='CHECKOUT')get(u).checkouts++;
    else if(a.action==='MEAS BOOKING EXTRA SET')get(u).upsells++;
  }
  for(const c of strState(s).changes){if(!inRange(c.date))continue;const u=uidOf(c.by);if(u)get(u).rateEdits++;}
  for(const a of s.approvals){
    const d=(a.decidedAt??'').slice(0,10);if(!d||!inRange(d))continue;
    if(a.status!=='Approved'&&a.status!=='Executed')continue;const u=uidOf(a.actor);if(!u)continue;
    if(a.kind===undefined){const f=get(u);f.discountCount++;f.discountAmount+=a.amount;if(String(a.reason??'').trim())f.withReason++;}
    else if(a.kind==='comp'||a.kind==='complimentary')get(u).comps++;
  }
  for(const [d,c] of Object.entries(s.closings??{})){
    if(!inRange(d))continue;const u=uidOf(c.cash?.countedBy);if(u)get(u).cashVariance+=Math.abs(c.cash.difference||0);
  }
  for(const c of gsvSlice(s).complaints){if(!inRange(c.createdAt.slice(0,10)))continue;const u=uidOf(c.owner);if(u)get(u).complaints++;}
  for(const t of s.measX?.turnaway??[]){if(!inRange(t.at.slice(0,10)))continue;const u=uidOf(t.by);if(u)get(u).regrets++;}
  for(const v of s.measX?.surveys??[]){if(v.via!=='desk'||!inRange(v.at.slice(0,10)))continue;const u=uidOf(v.enteredBy);if(u){const f=get(u);f.surveyTotal+=v.overall;f.surveyCount++;}}
  return byUser;
}

export function scorecardRows(s:State,from:string,to:string,userFilter?:string):RptRow[]{
  const all=scorecardFigures(s,from,to);
  const team=[...all.values()].filter(active);
  const rows:RptRow[]=[];
  for(const [uid,f] of [...all].sort((a,b)=>a[0].localeCompare(b[0]))){
    if(!active(f)||(userFilter&&uid!==userFilter))continue;
    const u=s.users.find(x=>x.id===uid)!;
    rows.push(row({user:u.name.split(' · ')[0],checkins:f.checkins,checkouts:f.checkouts,rateEdits:f.rateEdits,discountCount:f.discountCount,discountAmount:f.discountAmount,
      discountReasonPct:pct1(f.withReason,f.discountCount),comps:f.comps,upsellPerArrival:f.checkins?(f.upsells/f.checkins).toFixed(2):'0.00',complaints:f.complaints,cashVariance:f.cashVariance,
      regrets:f.regrets,surveyScore:f.surveyCount?(Math.round(f.surveyTotal*10/f.surveyCount)/10).toFixed(1):'',exception:scorecardRed(team,f)?1:0}));
  }
  return rows;
}

const SCORECARD_BASIS='Per signed-in user for the month. Check-ins / check-outs / upsell captures from the command audit log; rate edits from the stay rate-change log; discounts from approved discount requests by the requester (reason % = requests with a reason); comps from approved comp requests (DEC-266 not built yet: 0 until it is); complaints owned; cash variances = absolute cash difference of the day closings the user counted; regrets = turn-aways logged; survey score = average overall score of desk-entered surveys. Upsell per arrival = upsell captures / check-ins. Red: discounts (Rs) or cash variances (Rs) above the team\'s upper quartile (needs at least 4 users with activity). Owner and Manager only.';
export const agentScorecard=registerReport({
  id:SCORECARD_ID,title:'Performance by desk user',area:'people',priority:'P2',status:'MEAS',
  moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],dateClass:'D-M',
  params:[{key:'month',label:'Month',type:'month',dateClass:'D-M'},{key:'user',label:'Desk user',type:'user'}],
  columns:SCORECARD_COLS,dateBasis:SCORECARD_BASIS,formats:['screen','xlsx','pdf','csv'],phone:'no',
  select:(s,uid,p)=>{
    ownerManagerOnly(s,uid,'The performance scorecard');
    return {params:p,columns:SCORECARD_COLS,rows:scorecardRows(s,p.from!,p.to!,typeof p.user==='string'?p.user:undefined)};
  },
});
