// Module 26 · AI management assistant — pure helpers, selectors and the state slice type. Exported names MUST start with 'ai' / 'AI' (shared mock namespace).
// SIMULATED: no language model and no network. A keyword matcher picks an intent; pure selectors compute the answer from State.
import type {State,Booking,Permission} from '../types';
import {assignedScopeRoles} from '../types';
import {money,addDays} from '../money';
import {can,totals,drawer,metrics,blockedDuring,blockedTonight,occupant,roomStatus} from '../selectors';
import {cmpHasAccess,cmpDueThisWeek,cmpCounts,cmpItems,cmpStatusText} from './compliance';
import {closingPreflight,shiftStatus,shiftExpected,approvalKindLabel,expenseCounts,SHIFT_MANAGER_DIFF_LIMIT} from './closing';

export const AI_LABEL='Simulated assistant · rule-based · answers computed from this demo’s data';
export const AI_HISTORY_MAX=50;
export const AI_QUESTION_MAX=300;
export const AI_DEFAULT_DIRTY_HOURS=4;

export type AIIntent='occupancy-today'|'occupancy-tomorrow'|'occupancy-7d'|'arrivals'|'departures'|'adr'|'revpar'|'collections'|'cash'|'approvals'|'rooms'|'expenses-week'|'balances'|'hk-backlog'|'day-close'|'briefing'|'anomalies'|'compliance-due'|'unknown';
export type AIFigure={label:string;value:string};
export type AILink={page:string;label:string};
export type AIAnswer={intent:AIIntent;title:string;text:string;figures:AIFigure[];items:string[];how:string;links:AILink[];restricted?:boolean};
export type AIMessage={id:string;at:string;date:string;question:string;answer:AIAnswer};
export type AISeverity='High'|'Medium'|'Low';
export type AIFlag={id:string;severity:AISeverity;title:string;detail:string;page:string;money:boolean};
export type AIBriefLine={id:string;label:string;value:string;note?:string;tone:'ok'|'warn'|'alert'|'info';page:string};
export type AIBriefing={date:string;at:string;headline:string;lines:AIBriefLine[];flags:number};
/** State slice for this module, stored at s.assistantX (optional; absent in old saves → treat as empty). */
export type AIState={
  /** Conversation history per user ID (oldest first, max AI_HISTORY_MAX). */
  history?:Record<string,AIMessage[]>;
  /** Anomaly thresholds. */
  settings?:{dirtyHours:number};
};
export const aiEmpty=():AIState=>({history:{},settings:{dirtyHours:AI_DEFAULT_DIRTY_HOURS}});
export const aiSlice=(s:State):AIState=>s.assistantX??aiEmpty();
export const aiHistory=(s:State,uid:string):AIMessage[]=>aiSlice(s).history?.[uid]??[];
export const aiDirtyHours=(s:State)=>aiSlice(s).settings?.dirtyHours??AI_DEFAULT_DIRTY_HOURS;

// ── Access (coarse permissions only; Housekeeper/Maintenance never see money) ──
export type AIAccess={money:boolean;frontdesk:boolean;rooms:boolean;approvals:boolean;close:boolean;cash:boolean;expenses:boolean;compliance:boolean};
export function aiAccess(s:State,uid:string):AIAccess{
  const u=s.users.find(x=>x.id===uid);
  const any=(...p:Permission[])=>p.some(x=>can(s,uid,x));
  const money=!!u&&!assignedScopeRoles.includes(u.role)&&any('reports','billing','payments','close','approve');
  return {money,
    frontdesk:any('reservations','checkin','billing','reports'),
    rooms:any('housekeeping','inspect','reservations','reports'),
    approvals:any('approve','reports'),
    close:any('close','reports'),
    cash:money&&any('payments','close','reports'),
    expenses:money&&any('expenses','approve','reports'),
    compliance:cmpHasAccess(s,uid)};
}
/** Which access flag each intent needs ('' = always). */
const need:Record<AIIntent,keyof AIAccess|''>={'occupancy-today':'frontdesk','occupancy-tomorrow':'frontdesk','occupancy-7d':'frontdesk',arrivals:'frontdesk',departures:'frontdesk',
  adr:'money',revpar:'money',collections:'money',cash:'cash',approvals:'approvals',rooms:'rooms','expenses-week':'expenses',balances:'money','hk-backlog':'rooms','day-close':'close',briefing:'',anomalies:'','compliance-due':'compliance',unknown:''};
export const aiIntentAllowed=(a:AIAccess,i:AIIntent)=>!need[i]||a[need[i] as keyof AIAccess];

// ── Suggested questions and topic names ──
export const aiTopics:{intent:AIIntent;label:string;question:string}[]=[
  {intent:'briefing',label:'Daily briefing',question:'Give me today’s briefing'},
  {intent:'occupancy-today',label:'Occupancy today',question:'What is occupancy today?'},
  {intent:'occupancy-tomorrow',label:'Occupancy tomorrow',question:'How full are we tomorrow?'},
  {intent:'occupancy-7d',label:'Next 7 days',question:'Occupancy for the next 7 days'},
  {intent:'arrivals',label:'Arrivals',question:'Who is arriving today?'},
  {intent:'departures',label:'Departures',question:'Who is checking out today?'},
  {intent:'adr',label:'ADR',question:'What is our ADR tonight?'},
  {intent:'revpar',label:'RevPAR',question:'What is RevPAR tonight?'},
  {intent:'collections',label:'Collections vs outstanding',question:'Collections vs outstanding today'},
  {intent:'cash',label:'Cash drawer',question:'How is the cash drawer and any variance?'},
  {intent:'approvals',label:'Pending approvals',question:'What approvals are pending?'},
  {intent:'rooms',label:'Dirty / blocked rooms',question:'Which rooms are dirty or blocked?'},
  {intent:'expenses-week',label:'Expenses this week',question:'How much did we spend this week?'},
  {intent:'balances',label:'Top balances due',question:'Who has the biggest balances due?'},
  {intent:'hk-backlog',label:'Housekeeping backlog',question:'What is the housekeeping backlog?'},
  {intent:'day-close',label:'Day-close readiness',question:'Are we ready to close the day?'},
  {intent:'compliance-due',label:'Compliance due',question:'What compliance filings are due?'},
  {intent:'anomalies',label:'Anomaly flags',question:'Any anomalies or problems?'},
];
/** Suggestion chips this user may use. */
export const aiSuggestions=(s:State,uid:string)=>{const a=aiAccess(s,uid);return aiTopics.filter(t=>aiIntentAllowed(a,t.intent));};

// ── Intent matching (keywords; highest score wins, list order breaks ties → specific first) ──
const rules:[AIIntent,RegExp[]][]=[
  ['compliance-due',[/complian/,/(gst|gstr|tds|epf|esic|pf)/,/professional tax/,/filings?/,/due (dates?|this week)/,/what (is|are) due/,/overdue (returns?|filings?)/]],
  ['balances',[/\btop\b.*\b(balance|due|owe)/,/\b(biggest|largest|highest|most)\b.*\b(balance|due|owe|owing)/,/\bwho\b.*\bowe/,/\bbalances?\s+due\b/,/\bdebtors?\b/]],
  ['revpar',[/\brev\s?-?par\b/,/revenue per available/]],
  ['adr',[/\badr\b/,/average (daily |room )?rate/,/\bavg\.? rate\b/,/average tariff/]],
  ['day-close',[/\bday[\s-]?clos/,/\bclose (the |today|the business )?day/,/night audit/,/ready to close/,/\bclosing\b/,/\bclose\b/]],
  ['cash',[/\bcash\b/,/\bdrawer\b/,/\bvariance\b/,/\btill\b/,/\bfloat\b/,/\bshift\b/]],
  ['collections',[/\bcollect/,/\boutstanding\b/,/\breceivabl/,/\bdues?\b/,/\bpayments? (received|today)/,/\breceipts?\b/]],
  ['expenses-week',[/\bexpens/,/\bspen[dt]/,/\bvendors?\b/,/\bcosts?\b/]],
  ['approvals',[/\bapprov/,/\bpending requests?\b/,/\bsign[\s-]?off\b/]],
  ['hk-backlog',[/\bhousekeep/,/\bbacklog\b/,/\btasks?\b/,/\bcleaning\b/,/\binspection\b/]],
  ['rooms',[/\bdirty\b/,/\bblocked\b/,/out of (order|service)/,/\broom status\b/,/\bnot ready\b/]],
  ['departures',[/\bdepart/,/\bcheck(ing)?[\s-]?outs?\b/,/\bleaving\b/]],
  ['arrivals',[/\barriv/,/\bcheck(ing)?[\s-]?ins?\b/,/\bcoming in\b/,/\bexpected guests?\b/]],
  ['occupancy-today',[/\boccup/,/\bhow full\b/,/\bin[\s-]?house\b/,/\brooms? (sold|occupied)\b/,/\bfull\b/]],
  ['anomalies',[/\banomal/,/\bunusual\b/,/\bflags?\b/,/\bproblems?\b/,/\bissues?\b/,/\brisks?\b/,/\bwrong\b/,/\bred flags?\b/]],
  ['briefing',[/\bbrief/,/\bsummar/,/\bovervie?w\b/,/\bhow (are we|is) .*today\b/,/\bwhat'?s happening\b/,/\bstatus\b/]],
];
export function aiNormalize(q:string){return q.toLowerCase().replace(/[’']/g,"'").replace(/\s+/g,' ').trim();}
export function aiMatchIntent(question:string):AIIntent{
  const q=aiNormalize(question);if(!q)return 'unknown';
  let best:AIIntent='unknown',score=0;
  for(const [intent,res] of rules){const n=res.filter(r=>r.test(q)).length;if(n>score){best=intent;score=n;}}
  if(best==='unknown'&&/\bnext (7|seven) days\b|\bnext week\b/.test(q))return 'occupancy-7d';
  if(best==='occupancy-today'){
    if(/\b(7|seven) ?days?\b|\bweek\b|\bnext few days\b|\bforecast\b|\bupcoming\b/.test(q))return 'occupancy-7d';
    if(/\btomorrow\b/.test(q))return 'occupancy-tomorrow';
  }
  return best;
}

// ── Small helpers ──
const pct=(num:number,den:number)=>den?Math.round(num*100/den)+'%':'—';
const mins=(a:string,b:string)=>Math.round((Date.parse(b+':00Z')-Date.parse(a+':00Z'))/60000);
const hrs=(m:number)=>m>=60?`${Math.floor(m/60)} h ${m%60} min`:`${m} min`;
const plural=(n:number,one:string,many=one+'s')=>`${n} ${n===1?one:many}`;
const userName=(s:State,id:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id;
const dayLabel=(d:string)=>new Date(d+'T12:00:00Z').toLocaleDateString('en-IN',{weekday:'short',day:'numeric',month:'short',timeZone:'UTC'});

/** Stays on the books for the night of d (Confirmed or In house, arrival ≤ d < departure). */
export function aiStaysOn(s:State,d:string):Booking[]{return s.bookings.filter(b=>(b.status==='In house'||b.status==='Confirmed')&&b.arrival<=d&&d<b.departure);}
export function aiSellableOn(s:State,d:string){return s.rooms.filter(r=>!blockedDuring(r,d,addDays(d,1))).length;}
export function aiOccupancyOn(s:State,d:string){const booked=aiStaysOn(s,d).length,sellable=aiSellableOn(s,d);return {date:d,booked,sellable,pct:sellable?Math.round(booked*100/sellable):0};}
export function aiOccupancyRange(s:State,days=7){const out=[];for(let i=0;i<days;i++)out.push(aiOccupancyOn(s,addDays(s.date,i)));return out;}
/** Tonight's room revenue: posted room line for the business date, else the agreed rate (counted separately). */
export function aiRoomRevenueTonight(s:State){
  const stays=s.bookings.filter(b=>b.status==='In house'&&b.arrival<=s.date&&s.date<b.departure);
  let revenue=0,posted=0,agreed=0;
  for(const b of stays){const lines=b.lines.filter(l=>l.kind==='room'&&l.date===s.date);
    if(lines.length){revenue+=lines.reduce((a,l)=>a+l.amount,0);posted++;}else{revenue+=b.rate;agreed++;}}
  const sellable=s.rooms.filter(r=>!blockedTonight(s,r)).length;
  return {revenue,occupied:stays.length,sellable,posted,agreed,
    adr:stays.length?Math.round(revenue/stays.length):0,revpar:sellable?Math.round(revenue/sellable):0};
}
export function aiArrivals(s:State){
  const due=s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival<=s.date);
  const done=s.bookings.filter(b=>b.status==='In house'&&b.arrival===s.date);
  return {due,done};
}
export function aiDepartures(s:State){
  const due=s.bookings.filter(b=>b.status==='In house'&&b.departure<=s.date);
  const done=s.bookings.filter(b=>b.status==='Departed'&&b.departure===s.date);
  return {due,done};
}
export function aiTopBalances(s:State,n=5){
  return s.bookings.filter(b=>['In house','Confirmed','Departed'].includes(b.status)).map(b=>({b,balance:totals(b).balance}))
    .filter(x=>x.balance>0).sort((a,b)=>b.balance-a.balance||(a.b.id<b.b.id?-1:1)).slice(0,n);
}
export function aiExpensesWeek(s:State){
  const from=addDays(s.date,-6);
  const list=s.expenses.filter(e=>expenseCounts(e)&&e.date>=from&&e.date<=s.date);
  const byCategory:Record<string,number>={};list.forEach(e=>{byCategory[e.category]=(byCategory[e.category]||0)+e.amount;});
  return {from,to:s.date,total:list.reduce((a,e)=>a+e.amount,0),count:list.length,byCategory,pending:s.expenses.filter(e=>e.status==='Pending')};
}
/** Age (minutes) of an open task on the simulated clock; tasks without createdAt are aged from 00:00 of the business date. */
export function aiTaskAge(s:State,t:{createdAt?:string}){const from=t.createdAt??s.date+'T00:00';return {minutes:Math.max(0,mins(from,s.clock)),estimated:!t.createdAt};}
export function aiBacklog(s:State){
  const open=s.tasks.filter(t=>t.status!=='Done'&&t.kind!=='repair');
  const byStatus:Record<string,number>={};const byAssignee:Record<string,number>={};
  open.forEach(t=>{byStatus[t.status]=(byStatus[t.status]||0)+1;byAssignee[t.assignee]=(byAssignee[t.assignee]||0)+1;});
  const oldest=open.map(t=>({t,...aiTaskAge(s,t)})).sort((a,b)=>b.minutes-a.minutes)[0];
  return {open,byStatus,byAssignee,oldest};
}

// ── Anomaly flags ──
const sevRank:Record<AISeverity,number>={High:0,Medium:1,Low:2};
export function aiFlags(s:State,uid?:string):AIFlag[]{
  const out:AIFlag[]=[];const limit=s.settings.discountLimit;const N=aiDirtyHours(s);
  // Large discounts in the last 7 days
  const from=addDays(s.date,-6);
  for(const b of s.bookings)for(const l of b.lines)if(l.kind==='discount'&&l.date>=from&&l.date<=s.date&&-l.amount>limit)
    out.push({id:'disc:'+l.id,severity:-l.amount>2*limit?'High':'Medium',title:`Large discount · ${b.guest} (${b.id})`,
      detail:`${money(-l.amount)} discount on ${l.date} is above the ${money(limit)} limit. “${l.label}”.`,page:'folios',money:true});
  // Cash difference / formula mismatch
  if(s.shift?.date===s.date&&s.shift.counted!==s.shift.expected){const diff=s.shift.counted-s.shift.expected;
    out.push({id:'cash-diff',severity:Math.abs(diff)>SHIFT_MANAGER_DIFF_LIMIT?'High':'Medium',title:'Cash difference on today’s count',
      detail:`Counted ${money(s.shift.counted)} vs expected ${money(s.shift.expected)} (${diff<0?'short':'over'} ${money(Math.abs(diff))}).${s.shift.reason?' Reason: '+s.shift.reason+'.':''}`,page:'cashier',money:true});}
  if(!shiftExpected(s).consistent)out.push({id:'cash-formula',severity:'High',title:'Drawer formula mismatch',detail:'The expected-cash parts do not add up to the drawer figure. Review cash movements.',page:'cashier',money:true});
  // Rooms dirty for too long (checkout / move cleans for rooms still Dirty)
  for(const t of s.tasks){const r=s.rooms.find(r=>r.id===t.room);
    if(t.status==='Done'||!r||r.hk!=='Dirty'||t.kind==='stayover'||t.kind==='repair')continue;
    const age=aiTaskAge(s,t);if(age.minutes<=N*60)continue;
    out.push({id:'dirty:'+t.id,severity:age.minutes>2*N*60?'High':'Medium',title:`Room ${r.id} dirty for ${hrs(age.minutes)}`,
      detail:`Over the ${N}-hour threshold on the simulated clock${age.estimated?' (no task timestamp — aged from the start of the business date)':''}. Assigned to ${userName(s,t.assignee)}.`,page:'housekeeping',money:false});}
  // Arrival due into a room that is not ready
  for(const b of s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival===s.date)){const r=s.rooms.find(r=>r.id===b.room);
    if(r&&(r.hk!=='Ready'||blockedTonight(s,r)))out.push({id:'arr-room:'+b.id,severity:'Medium',title:`Arrival into room ${r.id} not ready`,
      detail:`${b.guest} arrives today; room ${r.id} is ${roomStatus(s,r).toLowerCase()}.`,page:'housekeeping',money:false});}
  // Unbalanced folios
  for(const b of s.bookings){const t=totals(b);
    if(b.status==='Departed'&&(t.balance!==0||t.deposit!==0))out.push({id:'unbal:'+b.id,severity:'High',title:`Departed folio not settled · ${b.guest} (${b.id})`,
      detail:`${t.balance!==0?`Balance ${money(t.balance)}`:''}${t.balance!==0&&t.deposit!==0?'; ':''}${t.deposit!==0?`deposit ${money(t.deposit)} still held`:''} after departure.`,page:'folios',money:true});
    else if(b.status==='In house'&&b.departure<=s.date&&t.balance>0)out.push({id:'overdue:'+b.id,severity:'Medium',title:`Overdue departure with balance · ${b.guest}`,
      detail:`Room ${b.room} was due out ${b.departure}; ${money(t.balance)} still due.`,page:'folios',money:true});
    else if(b.status==='In house'&&t.balance<0&&b.lines.length)out.push({id:'credit:'+b.id,severity:'Low',title:`Credit balance · ${b.guest} (${b.id})`,
      detail:`Receipts exceed charges by ${money(-t.balance)} — apply to later nights or refund.`,page:'folios',money:true});}
  // Pending approvals / expenses waiting
  const pa=s.approvals.filter(a=>a.status==='Pending'),pe=s.expenses.filter(e=>e.status==='Pending');
  if(pa.length+pe.length)out.push({id:'pending',severity:'Low',title:`${plural(pa.length+pe.length,'request')} awaiting a decision`,
    detail:[pa.length?plural(pa.length,'approval'):'',pe.length?plural(pe.length,'expense'):''].filter(Boolean).join(' and ')+' pending — the day cannot close until they are decided.',page:'approvals',money:false});
  const a=uid?aiAccess(s,uid):null;
  return out.filter(f=>!a||(f.money?a.money:f.id!=='pending'||a.approvals||a.close)).sort((x,y)=>sevRank[x.severity]-sevRank[y.severity]);
}

// ── Daily briefing ──
const aiCheckName:Record<string,string>={arrivals:'arrivals',departures:'overdue departures',holds:'expired holds',nights:'room-night charges',approvals:'approvals',expenses:'expenses',shift:'cash shift'};
export function aiBriefing(s:State,uid:string):AIBriefing{
  const a=aiAccess(s,uid),m=metrics(s),lines:AIBriefLine[]=[];
  if(a.frontdesk){
    const arr=aiArrivals(s),dep=aiDepartures(s),proj=m.occupied+arr.due.length;
    lines.push({id:'occ',label:'Occupancy',value:`${m.occupancy}%`,note:`${m.occupied} of ${m.sellable} sellable rooms in house; ${pct(proj,m.sellable)} once due arrivals check in`,tone:'info',page:'rooms'});
    lines.push({id:'arr',label:'Arrivals due',value:String(arr.due.length),note:arr.due.length?arr.due.map(b=>`${b.guest} · ${b.room}`).join(', '):'All arrivals checked in',tone:arr.due.length?'warn':'ok',page:'arrivals'});
    lines.push({id:'dep',label:'Departures due',value:String(dep.due.length),note:dep.due.length?dep.due.map(b=>`${b.guest} · ${b.room}`).join(', '):'No departures due',tone:dep.due.length?'warn':'ok',page:'folios'});
  }
  if(a.rooms){
    const notReady=s.rooms.filter(r=>!blockedTonight(s,r)&&!occupant(s,r.id)&&r.hk!=='Ready').length,blocked=s.rooms.filter(r=>blockedTonight(s,r)).length,bl=aiBacklog(s);
    lines.push({id:'rooms',label:'Vacant rooms not ready',value:String(notReady),note:`${blocked} blocked tonight; ${plural(bl.open.length,'open housekeeping task')}`,tone:notReady?'warn':'ok',page:'housekeeping'});
  }
  if(a.approvals){const n=s.approvals.filter(x=>x.status==='Pending').length+s.expenses.filter(e=>e.status==='Pending').length;
    lines.push({id:'appr',label:'Pending decisions',value:String(n),note:n?'Approvals and expenses waiting':'Nothing waiting',tone:n?'warn':'ok',page:'approvals'});}
  if(a.money){
    lines.push({id:'coll',label:'Collections today',value:money(m.collections),note:`Outstanding on folios ${money(m.outstanding)}; deposits held ${money(m.deposits)}`,tone:'info',page:'reports'});
    if(a.cash)lines.push({id:'cash',label:'Expected drawer',value:money(drawer(s)),note:`Shift ${shiftStatus(s).toLowerCase()}`,tone:'info',page:'cashier'});
  }
  if(a.close){const pf=closingPreflight(s),blocking=pf.filter(p=>p.items.length);const closed=s.closedDates.includes(s.date);
    lines.push({id:'close',label:'Day close',value:closed?'Closed':blocking.length?'Not ready':'Ready',note:closed?`Business date ${s.date} is closed`:blocking.length?`${plural(blocking.length,'check')} blocking: `+blocking.map(p=>aiCheckName[p.id]??p.title).join(', '):'All checks clear',tone:closed||!blocking.length?'ok':'warn',page:'closing'});}
  const flags=aiFlags(s,uid);
  const high=flags.filter(f=>f.severity==='High').length;
  const headline=lines.length?`${dayLabel(s.date)} · ${plural(flags.length,'flag')}${high?` (${high} high)`:''}`:'No briefing topics for your role';
  return {date:s.date,at:s.clock,headline,lines,flags:flags.length};
}

// ── Answers ──
const link=(page:string,label:string):AILink=>({page,label});
export function aiAnswer(s:State,uid:string,question:string,intent:AIIntent=aiMatchIntent(question)):AIAnswer{
  const a=aiAccess(s,uid);
  const ans=(title:string,text:string,figures:AIFigure[],how:string,links:AILink[],items:string[]=[]):AIAnswer=>({intent,title,text,figures,items,how,links});
  if(!aiIntentAllowed(a,intent)){
    const topics=aiSuggestions(s,uid).map(t=>t.label.toLowerCase());
    return {intent,title:'Not available for your role',restricted:true,figures:[],items:[],links:[],
      text:`This topic uses information your role does not have access to${need[intent]==='money'||need[intent]==='cash'||need[intent]==='expenses'?' (money figures)':''}. You can ask about: ${topics.join(', ')}.`,
      how:'Answers are filtered by the permissions of the signed-in demo user.'};
  }
  const m=metrics(s);
  switch(intent){
  case 'occupancy-today':{const arr=aiArrivals(s),proj=m.occupied+arr.due.length;
    return ans('Occupancy today',`${m.occupancy}% — ${m.occupied} of ${m.sellable} sellable rooms are in house. With the ${plural(arr.due.length,'arrival')} still due it becomes ${pct(proj,m.sellable)}.`,
      [{label:'In house',value:String(m.occupied)},{label:'Sellable tonight',value:String(m.sellable)},{label:'Occupancy',value:`${m.occupancy}%`},{label:'Projected',value:pct(proj,m.sellable)}],
      'In-house stays ÷ rooms not blocked tonight. Projected adds confirmed arrivals due today. Percentages are rounded to whole numbers.',[link('rooms','Room board'),link('reports','Reports')]);}
  case 'occupancy-tomorrow':{const o=aiOccupancyOn(s,addDays(s.date,1));
    return ans('Occupancy tomorrow',`${o.pct}% on the books for ${dayLabel(o.date)} — ${o.booked} of ${o.sellable} sellable rooms.`,
      [{label:'Booked',value:String(o.booked)},{label:'Sellable',value:String(o.sellable)},{label:'Occupancy',value:`${o.pct}%`}],
      'Confirmed and in-house stays covering that night (arrival ≤ night < departure) ÷ rooms not blocked that night. Holds are not counted.',[link('reservations','Reservations'),link('calendar','Calendar')]);}
  case 'occupancy-7d':{const r=aiOccupancyRange(s,7),avg=Math.round(r.reduce((x,o)=>x+o.pct,0)/r.length),peak=[...r].sort((x,y)=>y.pct-x.pct)[0];
    return ans('Occupancy · next 7 days',`Average ${avg}% from ${dayLabel(r[0].date)} to ${dayLabel(r[6].date)}. Busiest night: ${dayLabel(peak.date)} at ${peak.pct}%.`,
      [{label:'Average',value:`${avg}%`},{label:'Peak',value:`${peak.pct}%`}],
      'For each night: confirmed and in-house stays ÷ rooms not blocked that night; the average is the mean of the seven nightly percentages.',[link('reservations','Reservations'),link('yield','Tariff & yield')],
      r.map(o=>`${dayLabel(o.date)} — ${o.pct}% (${o.booked}/${o.sellable})`));}
  case 'arrivals':{const x=aiArrivals(s);
    return ans('Arrivals today',x.due.length?`${plural(x.due.length,'arrival')} still to check in; ${x.done.length} already checked in today.`:`No arrivals left to check in; ${x.done.length} checked in today.`,
      [{label:'Due',value:String(x.due.length)},{label:'Checked in today',value:String(x.done.length)}],
      'Confirmed bookings with arrival on or before the business date (late arrivals included) are due; in-house stays that arrived today are done.',[link('arrivals','Arrivals & check-in'),link('reservations','Reservations')],
      x.due.map(b=>`${b.guest} · room ${b.room} · ${b.id}${b.arrival<s.date?' · late since '+b.arrival:''}${s.rooms.find(r=>r.id===b.room)?.hk!=='Ready'?' · room not ready':''}`));}
  case 'departures':{const x=aiDepartures(s);
    return ans('Departures today',x.due.length?`${plural(x.due.length,'departure')} due to check out; ${x.done.length} already departed.`:`No departures due; ${x.done.length} departed today.`,
      [{label:'Due',value:String(x.due.length)},{label:'Departed',value:String(x.done.length)}],
      `In-house stays with departure on or before the business date are due${a.money?'; the balance is the folio total (incl. synthetic tax) minus net payments':''}.`,[link('folios','Guests & folios')],
      x.due.map(b=>`${b.guest} · room ${b.room}${b.departure<s.date?' · overdue since '+b.departure:''}${a.money?' · balance '+money(totals(b).balance):''}`));}
  case 'adr':case 'revpar':{const r=aiRoomRevenueTonight(s);const isAdr=intent==='adr';
    return ans(isAdr?'ADR tonight':'RevPAR tonight',isAdr?`ADR is ${r.occupied?money(r.adr):'—'} — ${money(r.revenue)} room revenue over ${plural(r.occupied,'occupied room')}.`:`RevPAR is ${money(r.revpar)} — ${money(r.revenue)} room revenue over ${r.sellable} sellable rooms.`,
      [{label:'Room revenue',value:money(r.revenue)},{label:isAdr?'Occupied rooms':'Sellable rooms',value:String(isAdr?r.occupied:r.sellable)},{label:isAdr?'ADR':'RevPAR',value:isAdr?(r.occupied?money(r.adr):'—'):money(r.revpar)},{label:'Nights posted',value:`${r.posted} of ${r.occupied}`}],
      `${isAdr?'Room revenue ÷ occupied rooms':'Room revenue ÷ sellable (not blocked) rooms'} for tonight, before tax. Uses tonight’s posted room line where it exists; ${plural(r.agreed,'stay')} not yet posted ${r.agreed===1?'uses its':'use their'} agreed rate. Rounded to the paisa.`,[link('reports','Reports')]);}
  case 'collections':
    return ans('Collections vs outstanding',`Collected ${money(m.collections)} today; ${money(m.outstanding)} is still outstanding on folios. Refundable deposits held: ${money(m.deposits)}.`,
      [{label:'Collected today',value:money(m.collections)},{label:'Outstanding',value:money(m.outstanding)},{label:'Deposits held',value:money(m.deposits)}],
      'Collected = payments − refunds with today’s receipt date. Outstanding = sum of positive folio balances (charges + synthetic tax − payments − company credit). Deposits are not revenue and are shown separately.',[link('reports','Reports'),link('folios','Guests & folios')]);
  case 'cash':{const x=shiftExpected(s),st=shiftStatus(s);const sh=s.shift?.date===s.date?s.shift:undefined;const diff=sh?sh.counted-sh.expected:0;
    return ans('Cash drawer',`Expected drawer ${money(x.expected)}; the shift is ${st.toLowerCase()}.${sh?` Counted ${money(sh.counted)} — ${diff===0?'no difference':`${diff<0?'short':'over'} by ${money(Math.abs(diff))}`}.`:' No count submitted yet.'}`,
      [{label:'Opening float',value:money(x.opening)},{label:'Cash payments',value:money(x.payments)},{label:'Cash deposits',value:money(x.deposits)},{label:'Cash out (refunds, returns, expenses)',value:money(x.refunds+x.returns+x.expenses)},{label:'Expected',value:money(x.expected)},...(sh?[{label:'Difference',value:money(diff)}]:[])],
      'Expected = opening float + cash payments + cash deposits − cash refunds − deposit returns − posted cash expenses (Financial Rulebook §4).',[link('cashier','Cashier shift')]);}
  case 'approvals':{const pa=s.approvals.filter(x=>x.status==='Pending'),pe=s.expenses.filter(e=>e.status==='Pending');
    return ans('Pending approvals',pa.length+pe.length?`${plural(pa.length,'approval')} and ${plural(pe.length,'expense')} are waiting for a decision.`:'Nothing is waiting for approval.',
      [{label:'Approvals',value:String(pa.length)},{label:'Expenses',value:String(pe.length)}],
      'Approval requests with status Pending plus expenses submitted and not yet approved or rejected. A request cannot be approved by the person who raised it.',[link('approvals','Approvals & audit'),link('expenses','Expenses')],
      [...pa.map(x=>`${approvalKindLabel(x.kind)}${a.money?' · '+money(x.amount):''} · ${x.reason} · raised by ${userName(s,x.actor)}`),...pe.map(e=>`Expense · ${e.vendor}${a.money?' · '+money(e.amount):''} · raised by ${userName(s,e.actor)}`)]);}
  case 'rooms':{const st=s.rooms.map(r=>({r,st:roomStatus(s,r)}));const pick=(k:string)=>st.filter(x=>x.st===k).map(x=>x.r.id);
    const dirty=pick('Dirty'),cleaning=pick('Cleaning'),insp=pick('Inspection'),blocked=pick('Blocked');
    return ans('Dirty and blocked rooms',`${plural(dirty.length,'room')} dirty, ${cleaning.length} being cleaned, ${insp.length} awaiting inspection and ${blocked.length} blocked tonight.`,
      [{label:'Dirty',value:String(dirty.length)},{label:'Cleaning',value:String(cleaning.length)},{label:'Inspection',value:String(insp.length)},{label:'Blocked',value:String(blocked.length)}],
      'Room status as on the room board: blocked tonight first, then occupied, else the housekeeping status.',[link('rooms','Room board'),link('housekeeping','Housekeeping')],
      [dirty.length?'Dirty: '+dirty.join(', '):'',cleaning.length?'Cleaning: '+cleaning.join(', '):'',insp.length?'Inspection: '+insp.join(', '):'',
        ...blocked.map(id=>{const r=s.rooms.find(x=>x.id===id)!;return `Blocked: ${id}${r.blockReason?' · '+r.blockReason:''}${r.blockTo?' · until '+r.blockTo:''}`;})].filter(Boolean));}
  case 'expenses-week':{const x=aiExpensesWeek(s);const cats=Object.entries(x.byCategory).sort((p,q)=>q[1]-p[1]);
    return ans('Expenses this week',`${money(x.total)} posted across ${plural(x.count,'expense')} from ${dayLabel(x.from)} to ${dayLabel(x.to)}. ${plural(x.pending.length,'expense')} pending approval${x.pending.length?' ('+money(x.pending.reduce((p,e)=>p+e.amount,0))+')':''}.`,
      [{label:'Posted (7 days)',value:money(x.total)},{label:'Count',value:String(x.count)},{label:'Pending',value:String(x.pending.length)}],
      'Posted and later-reversed expenses dated in the last 7 business days (reversal records offset on their own date). Pending expenses are not counted until approved.',[link('expenses','Expenses')],
      cats.map(([c,v])=>`${c} — ${money(v)}`));}
  case 'balances':{const t=aiTopBalances(s);
    return ans('Top balances due',t.length?`The largest balance is ${money(t[0].balance)} (${t[0].b.guest}). ${plural(t.length,'folio')} listed; total outstanding ${money(m.outstanding)}.`:'No folio has a balance due.',
      [{label:'Total outstanding',value:money(m.outstanding)},{label:'Listed',value:String(t.length)}],
      'Balance = charges + synthetic tax − payments (net of refunds) − company credit, for confirmed, in-house and departed stays. Top five shown.',[link('folios','Guests & folios')],
      t.map(x=>`${x.b.guest} · room ${x.b.room} · ${x.b.status} · ${money(x.balance)}`));}
  case 'hk-backlog':{const x=aiBacklog(s);
    return ans('Housekeeping backlog',x.open.length?`${plural(x.open.length,'open task')}. Oldest: room ${x.oldest!.t.room}, ${hrs(x.oldest!.minutes)}${x.oldest!.estimated?' (estimated)':''}.`:'No open housekeeping tasks.',
      [{label:'To do',value:String(x.byStatus['To do']||0)},{label:'In progress',value:String(x.byStatus['In progress']||0)},{label:'Inspection',value:String(x.byStatus['Inspection']||0)}],
      'Housekeeping tasks not Done (repairs excluded — see Maintenance), grouped by status and assignee. Age is measured on the simulated clock from task creation; tasks without a timestamp are aged from 00:00 of the business date.',[link('housekeeping','Housekeeping')],
      Object.entries(x.byAssignee).map(([u,n])=>`${userName(s,u)} — ${plural(n,'task')}`));}
  case 'day-close':{const closed=s.closedDates.includes(s.date),pf=closingPreflight(s),blocking=pf.filter(p=>p.items.length);
    return ans('Day-close readiness',closed?`Business date ${s.date} is already closed. Open the next business day from Day closing.`:blocking.length?`Not ready — ${plural(blocking.length,'check')} of ${pf.length} still blocking.`:'Ready — every preflight check is clear.',
      [{label:'Checks clear',value:`${pf.length-blocking.length} of ${pf.length}`},{label:'Blocking items',value:String(blocking.reduce((n,p)=>n+p.items.length,0))}],
      'The same preflight the Day closing screen runs: arrivals, overdue departures, expired holds, room-night charges, approvals, expenses and the cash shift.',[link('closing','Day closing')],
      closed?[]:blocking.map(p=>`${p.title}: ${plural(p.items.length,'item')} — ${p.hint}`));}
  case 'compliance-due':{const due=cmpDueThisWeek(s),c=cmpCounts(cmpItems(s));
    return ans('Compliance due',due.length?`${plural(due.length,'filing')} overdue or due this week: ${c.overdue} overdue, ${due.length-c.overdue} due soon.`:`Nothing overdue or due this week. ${c.upcoming} later filing${c.upcoming===1?'':'s'} on the calendar.`,
      [{label:'Overdue',value:String(c.overdue)},{label:'Due this week',value:String(due.length-c.overdue)},{label:'Filed',value:String(c.filed)}],
      'Calendar of TDS, GST returns, EPF, ESIC and Professional Tax with due-date rules marked illustrative (CA to confirm). “This week” uses the window set in Compliance calendar.',[link('compliance','Compliance calendar')],
      due.map(i=>`${i.obligation.name} · ${i.periodLabel} · due ${i.due} · ${cmpStatusText(i)}`));}
  case 'briefing':{const b=aiBriefing(s,uid);
    return ans('Daily briefing',b.headline,b.lines.map(l=>({label:l.label,value:l.value})),'Each line is computed live from the demo data with the same rules as the matching question. Lines you cannot see are left out.',[link('overview','Overview')],
      b.lines.map(l=>`${l.label}: ${l.value}${l.note?' — '+l.note:''}`));}
  case 'anomalies':{const f=aiFlags(s,uid);
    return ans('Anomaly flags',f.length?`${plural(f.length,'flag')}: ${f.filter(x=>x.severity==='High').length} high, ${f.filter(x=>x.severity==='Medium').length} medium, ${f.filter(x=>x.severity==='Low').length} low.`:'No anomalies found.',
      [{label:'High',value:String(f.filter(x=>x.severity==='High').length)},{label:'Medium',value:String(f.filter(x=>x.severity==='Medium').length)},{label:'Low',value:String(f.filter(x=>x.severity==='Low').length)}],
      `Rules: discounts above the ${a.money?money(s.settings.discountLimit)+' ':''}limit, cash count differences, rooms dirty more than ${aiDirtyHours(s)} hours, arrivals into rooms not ready, unsettled or overdue folios, credit balances and waiting approvals.`,
      [...new Set(f.map(x=>x.page))].map(p=>link(p,p==='folios'?'Guests & folios':p==='housekeeping'?'Housekeeping':p==='cashier'?'Cashier shift':p==='approvals'?'Approvals & audit':p)),
      f.map(x=>`${x.severity} · ${x.title} — ${x.detail}`));}
  default:{const t=aiSuggestions(s,uid).map(x=>x.label.toLowerCase());
    return ans('I can’t answer that yet',`I only understand a fixed set of questions about this demo. Try one of: ${t.join('; ')}.`,[],
      'Questions are matched by keywords to a list of known topics; nothing is sent anywhere.',[],aiSuggestions(s,uid).slice(0,6).map(x=>'“'+x.question+'”'));}
  }
}
