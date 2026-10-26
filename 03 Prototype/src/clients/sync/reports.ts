// Server-side reports (Wave 4 gap G3): Reports screen, day pack, history over months and the Overview / Home figures read
// GET /reports/daily (08 Production Reporting: one range of at most 31 business dates, same calculator as the closing snapshot)
// instead of computing from the slices the device happens to have loaded. Figures only: the server report is aggregate, so
// drill-down source rows are NOT available here (screens show SERVER_DETAIL_NOTE). Production build only (no top-level calls).
import type {SyncApi} from './types';
import type {ReportPack} from '../../domain/streams/closing';
import type {ClosingRecord} from '../../domain/streams/closing';
import {money} from '../../domain/money';
import {reportPackFromServer,closingFromServer,type SrvDailyView,type SrvDaily} from './closing/figures';

export const REPORT_MAX_DAYS=31;
export const SERVER_DETAIL_NOTE='Details (source rows) are on the server PC. This figure comes from the server\u2019s report.';

export type ServerReport={pack:ReportPack;view:SrvDailyView;asOfUtc:string};

/** One server report for a range of at most REPORT_MAX_DAYS dates. */
export async function loadServerReport(api:SyncApi,from:string,to:string,now:()=>Date=()=>new Date()):Promise<ServerReport>{
  const view=await api.get<SrvDailyView>('/reports/daily',{from,to});
  return {pack:reportPackFromServer(view),view,asOfUtc:now().toISOString()};
}

/** Day pack / closing-style record of one date from the server report (figures only). */
export function closingFromDailyView(v:SrvDailyView,date:string):ClosingRecord{
  const f:SrvDaily=v.report;
  return closingFromServer({businessDate:date,closedBy:0,closedUtc:date+'T00:00:00.000Z',runId:0,attempts:0,nightAuditResumed:false,nightAuditStays:0,
    depositsHeldAtClosePaise:0,exceptions:[],figures:f});
}

/** The four live tiles of Overview / Home from today's server report. */
export type TodayFigures={occupied:number;sellable:number;occupancy:number;collections:number;expenses:number;drawer:number;revenue:number;tax:number;asOfUtc:string};
export function todayFigures(r:ServerReport):TodayFigures{
  const f=r.view.report,o=f.occupancy;
  return {occupied:o.occupiedRoomNights,sellable:o.sellableRoomNights,occupancy:o.sellableRoomNights?Math.round(o.occupiedRoomNights/o.sellableRoomNights*100):0,
    collections:f.receipts.netPaise,expenses:f.expenses.totalPaise,drawer:f.drawer.expectedPaise,revenue:f.revenue.netPaise,tax:f.revenue.taxPaise,asOfUtc:r.asOfUtc};
}

// ── History over months (financial year to date) ──
export type MonthRange={month:string;label:string;from:string;to:string};
const MONTH_NAMES=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
const lastDay=(y:number,m:number)=>new Date(Date.UTC(y,m,0)).getUTCDate();
/** Months from the start of the Indian financial year (1 April) up to the business date; the last month ends on the business date. Newest last. */
export function fyMonths(date:string):MonthRange[]{
  const y=+date.slice(0,4),mo=+date.slice(5,7),fy=mo>=4?y:y-1,out:MonthRange[]=[];
  let cy=fy,cm=4;
  while(cy<y||(cy===y&&cm<=mo)){
    const p=(n:number)=>String(n).padStart(2,'0'),from=`${cy}-${p(cm)}-01`,end=`${cy}-${p(cm)}-${p(lastDay(cy,cm))}`;
    out.push({month:`${cy}-${p(cm)}`,label:`${MONTH_NAMES[cm-1]} ${cy}`,from,to:end<date?end:date});
    cm++;if(cm>12){cm=1;cy++;}
  }
  return out;
}
export type ServerMonth={month:string;label:string;sellable:number;occupied:number;occupancy:string;adr:string;revpar:string;roomRevenue:number;netRevenue:number};
export type ServerHistory={from:string;to:string;months:ServerMonth[];total:{sellable:number;occupied:number;occupancy:string;adr:string;revpar:string;roomRevenue:number;netRevenue:number};asOfUtc:string};
const ratio=(p:number|null)=>p===null||p===undefined?'\u2014':money(p);
export const pct=(occ:number,sell:number)=>sell?(Math.round(occ/sell*1000)/10).toFixed(1)+'%':'\u2014';
const div=(a:number,b:number)=>b?Math.round(a/b):null;
/** One GET per month; month figures are the server's own strings, the financial-year total is summed from the months. */
export async function loadServerHistory(api:SyncApi,date:string,now:()=>Date=()=>new Date()):Promise<ServerHistory>{
  const ranges=fyMonths(date);
  const views=await Promise.all(ranges.map(r=>api.get<SrvDailyView>('/reports/daily',{from:r.from,to:r.to})));
  const months=ranges.map((r,i)=>{const o=views[i].report.occupancy;
    return {month:r.month,label:r.label,sellable:o.sellableRoomNights,occupied:o.occupiedRoomNights,occupancy:o.occupancyPct,adr:ratio(o.adrPaise),revpar:ratio(o.revParPaise),
      roomRevenue:o.roomRevenuePaise,netRevenue:views[i].report.revenue.netPaise};});
  const sum=(k:'sellable'|'occupied'|'roomRevenue'|'netRevenue')=>months.reduce((a,m)=>a+m[k],0);
  const sellable=sum('sellable'),occupied=sum('occupied'),roomRevenue=sum('roomRevenue');
  return {from:ranges[0]?.from??date,to:date,months,asOfUtc:now().toISOString(),
    total:{sellable,occupied,occupancy:pct(occupied,sellable),adr:ratio(div(roomRevenue,occupied)),revpar:ratio(div(roomRevenue,sellable)),roomRevenue,netRevenue:sum('netRevenue')}};
}
