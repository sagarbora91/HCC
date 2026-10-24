// FY history (scenario FY26): compact daily housekeeping rows and month-by-month summary derived from the stored closing records.
// Pure selectors; no commands. Closing records are the source for money and occupancy so the card equals the Reports totals.
import type {State} from '../types';
import {reportPct,reportRatioMoney} from './closing';

export type HistoryDay={date:string;departures:number;cleans:number;stayovers:number;audits:number;
  /** Checklist runs expected that day (older than 30 days only this count is kept). */checklists?:number};
export type HistoryX={from:string;to:string;days:HistoryDay[]};
export type HistoryMonth={month:string;label:string;days:number;sellable:number;occupied:number;occupancy:string;roomRevenue:number;adr:string;revpar:string;
  netRevenue:number;receipts:number;expenses:number;stays:number;cancellations:number;noShows:number;cleans:number;stayovers:number};

const MONTHS=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
export const historyMonthLabel=(month:string)=>`${MONTHS[Number(month.slice(5,7))-1]} ${month.slice(0,4)}`;
const blank=(month:string,label:string):HistoryMonth=>({month,label,days:0,sellable:0,occupied:0,occupancy:'—',roomRevenue:0,adr:'—',revpar:'—',netRevenue:0,receipts:0,expenses:0,stays:0,cancellations:0,noShows:0,cleans:0,stayovers:0});
const finish=(m:HistoryMonth)=>{m.occupancy=reportPct(m.occupied,m.sellable);m.adr=reportRatioMoney(m.roomRevenue,m.occupied);m.revpar=reportRatioMoney(m.roomRevenue,m.sellable);return m;};

/** Month rows plus an "FY to date" total, for the history window only. Empty when the scenario has no history. */
export function historyMonths(s:State):{months:HistoryMonth[];total:HistoryMonth}{
  const h=s.historyX,total=blank('FY','FY to date');
  if(!h)return {months:[],total:finish(total)};
  const by=new Map<string,HistoryMonth>();
  const row=(date:string)=>{const k=date.slice(0,7);let m=by.get(k);if(!m){m=blank(k,historyMonthLabel(k));by.set(k,m);}return m;};
  const inWindow=(d:string)=>d>=h.from&&d<=h.to;
  for(const [date,c] of Object.entries(s.closings??{})){
    if(!inWindow(date))continue;
    for(const m of [row(date),total]){m.days++;m.sellable+=c.occupancy.sellable;m.occupied+=c.occupancy.occupied;m.roomRevenue+=c.occupancy.roomRevenue;
      m.netRevenue+=c.revenue.net;m.receipts+=c.receipts.net;m.expenses+=c.expenses.total;}
  }
  for(const b of s.bookings){
    if(b.status==='Departed'&&inWindow(b.departure)){row(b.departure).stays++;total.stays++;}
    else if(b.status==='Cancelled'&&inWindow(b.arrival)){row(b.arrival).cancellations++;total.cancellations++;}
    else if(b.status==='No show'&&inWindow(b.arrival)){row(b.arrival).noShows++;total.noShows++;}
  }
  for(const d of h.days)for(const m of [row(d.date),total]){m.cleans+=d.cleans;m.stayovers+=d.stayovers;}
  const months=[...by.values()].sort((a,b)=>a.month<b.month?-1:1).map(finish);
  return {months,total:finish(total)};
}

// ── Report date-range rules shared by the desktop and phone Reports pages ──
/** Longest range the Reports pages accept: 62 days normally, a whole financial year when the scenario carries history. */
export const reportRangeLimit=(s:State)=>s.historyX?366:62;
export type RangePreset={id:string;label:string;from:string;to:string};
const shift=(d:string,n:number)=>new Date(Date.parse(d+'T12:00:00Z')+n*86400000).toISOString().slice(0,10);
export function reportRangePresets(s:State):RangePreset[]{
  const t=s.date,out:RangePreset[]=[{id:'today',label:'Today',from:t,to:t},{id:'3d',label:'Last 3 days',from:shift(t,-2),to:t},{id:'7d',label:'Last 7 days',from:shift(t,-6),to:t},
    {id:'30d',label:'Last 30 days',from:shift(t,-29),to:t},{id:'month',label:'This month',from:t.slice(0,8)+'01',to:t}];
  if(s.historyX)out.push({id:'fy',label:'FY history',from:s.historyX.from,to:s.historyX.to});
  return out;
}
/** First and last day of a 'YYYY-MM' month, clipped to the business date. */
export function reportMonthRange(s:State,month:string):{from:string;to:string}{
  const from=month+'-01',next=shift(from,32).slice(0,8)+'01',last=shift(next,-1);
  return {from,to:last>s.date?s.date:last};
}
