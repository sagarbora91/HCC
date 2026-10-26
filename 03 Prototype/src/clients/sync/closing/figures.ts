// Closing area helpers (W4-4): server DTOs (08 Production/src/Hcc.Contracts/Closing + Reporting) and their mapping to the
// prototype's ClosingRecord (state.closings[date]) and, for the Reports screen, to a figure-level ReportPack.
import type {ClosingRecord} from '../../../domain/streams/closing';
import type {ReportPack,ReportTab,ReportMetric} from '../../../domain/streams/closing';
import {money} from '../../../domain/money';
import {utcToClock,protoUserId} from '../map';

export type SrvTender={tender:string;paymentsPaise:number;refundsPaise:number;netPaise:number};
export type SrvTill={tillCode:string;shifts:number;status:string;openingPaise:number;cashInPaise:number;cashOutPaise:number;expensesPaise:number;restaurantPaise:number;
  expectedPaise:number;countedPaise:number|null;differencePaise:number;countedBy:number|null;approvedBy:number|null;reason:string|null};
export type SrvDay={date:string;arrivals:number;departures:number;sellable:number;occupied:number;netRevenuePaise:number;taxPaise:number;netReceiptsPaise:number;cashExpensesPaise:number};
export type SrvDaily={from:string;to:string;
  occupancy:{sellableRoomNights:number;occupiedRoomNights:number;occupancyPct:string;roomRevenuePaise:number;adrPaise:number|null;revParPaise:number|null};
  revenue:{roomPaise:number;servicePaise:number;discountsPaise:number;correctionsPaise:number;netPaise:number;taxPaise:number;cgstPaise:number;sgstPaise:number;grossPaise:number};
  gst:unknown[];receipts:{byTender:SrvTender[];netPaise:number;depositsInPaise:number;depositsOutPaise:number;companyCreditPaise:number};
  expenses:{cashPaise:number;reversalsPaise:number;totalPaise:number;note:string};
  drawer:{tills:SrvTill[];openingPaise:number;cashInPaise:number;cashOutPaise:number;expectedPaise:number;countedPaise:number|null;differencePaise:number};
  days:SrvDay[]};
export type SrvClosingRecord={businessDate:string;closedBy:number;closedUtc:string;runId:number;attempts:number;nightAuditResumed:boolean;nightAuditStays:number;
  depositsHeldAtClosePaise:number;exceptions:string[];figures:SrvDaily};
export type SrvSnapshot={snapshotId:number;sha256:string;record:SrvClosingRecord};
export type SrvRecords={from:string;to:string;items:SrvSnapshot[]};
export type SrvBusinessDate={businessDate:string;closed:boolean};
export type SrvDailyView={report:SrvDaily;dayStatus:{date:string;status:string;matchesSnapshot:boolean|null}[];businessDate:string;note:string};

const ratio=(p:number|null)=>p===null||p===undefined?'—':money(p);

/** Server closing record → prototype ClosingRecord. Server-only: attempts, run id. Prototype-only and absent on the server: the restaurant outlet
 *  split (`outlet`), UPI/bank and vendor-credit expenses (the server counts drawer-cash expenses until the expense module feeds the pack). */
export function closingFromServer(r:SrvClosingRecord):ClosingRecord{
  const f=r.figures,tills=f.drawer.tills,byTender:Record<string,number>={};
  for(const t of f.receipts.byTender)byTender[t.tender]=t.netPaise;
  const counted=f.drawer.countedPaise??f.drawer.expectedPaise;
  const diffTill=tills.find(t=>t.differencePaise!==0),countedBy=tills.find(t=>t.countedBy!==null)?.countedBy;
  return {date:r.businessDate,closedAt:utcToClock(r.closedUtc),closedBy:protoUserId(r.closedBy),
    revenue:{room:f.revenue.roomPaise,service:f.revenue.servicePaise,discounts:f.revenue.discountsPaise,adjustments:f.revenue.correctionsPaise,net:f.revenue.netPaise,tax:f.revenue.taxPaise},
    receipts:{byTender,net:f.receipts.netPaise,companyCredit:f.receipts.companyCreditPaise},
    deposits:{received:f.receipts.depositsInPaise,returned:f.receipts.depositsOutPaise,heldAtClose:r.depositsHeldAtClosePaise},
    expenses:{cash:f.expenses.cashPaise,nonCash:Math.max(0,f.expenses.totalPaise-f.expenses.cashPaise),vendorCredit:0,total:f.expenses.totalPaise},
    occupancy:{sellable:f.occupancy.sellableRoomNights,occupied:f.occupancy.occupiedRoomNights,occupancy:f.occupancy.occupancyPct,roomRevenue:f.occupancy.roomRevenuePaise,
      adr:ratio(f.occupancy.adrPaise),revpar:ratio(f.occupancy.revParPaise)},
    cash:{opening:f.drawer.openingPaise,expected:f.drawer.expectedPaise,counted,difference:f.drawer.differencePaise,
      reason:diffTill?.reason??undefined,approvedBy:diffTill?.approvedBy!=null?protoUserId(diffTill.approvedBy):undefined,
      ...(countedBy!=null?{countedBy:protoUserId(countedBy)}:{}),restaurant:tills.reduce((a,t)=>a+t.restaurantPaise,0)},
    nightAudit:{stays:r.nightAuditStays,resumed:r.nightAuditResumed},exceptions:[...r.exceptions]};
}

const m=(id:string,label:string,value:number,unit:'money'|'count'='money',note?:string):ReportMetric=>({id,label,unit,value,rows:[],note});
/** GET /reports/daily → a ReportPack with the Reports screen's tabs and metric ids, figures only (no source rows: the server report is
 *  aggregate; drill-down needs the folio/receipt reads). Day tables carry the columns the server returns per day. The Reports screen must be
 *  pointed at this (it computes from client slices today): see the W4-4 report. */
export function reportPackFromServer(v:SrvDailyView):ReportPack{
  const r=v.report,o=r.occupancy,rv=r.revenue,dates=r.days.map(d=>d.date);
  const stat=new Map(v.dayStatus.map(x=>[x.date,x.status]));
  const dayLabel=(d:string)=>stat.get(d)==='Closed'?'Closed':stat.get(d)==='Open'?'Open':stat.get(d)==='Future'?'Future':'—';
  const tabs:ReportTab[]=[
    {id:'daily',label:'Daily operations',ratios:[{id:'occupancy',label:'Occupancy %',display:o.occupancyPct,formula:`${o.occupiedRoomNights} occupied ÷ ${o.sellableRoomNights} sellable × 100, 1 dp`}],
      metrics:[m('arrivals','Arrivals checked in',r.days.reduce((a,d)=>a+d.arrivals,0),'count'),m('departures','Departures checked out',r.days.reduce((a,d)=>a+d.departures,0),'count'),
        m('occupied','Occupied room-nights',o.occupiedRoomNights,'count'),m('net-revenue','Net revenue (excl. tax)',rv.netPaise),m('net-receipts','Net receipts (payments − refunds)',r.receipts.netPaise),
        m('expenses','Posted expenses (net of reversals)',r.expenses.totalPaise)],
      table:{columns:['Business date','Day status','Arrivals','Departures','Occupied','Net revenue','Net receipts','Expenses'],units:['text','text','count','count','count','money','money','money'],
        rows:r.days.map(d=>[d.date,dayLabel(d.date),d.arrivals,d.departures,d.occupied,d.netRevenuePaise,d.netReceiptsPaise,d.cashExpensesPaise])}},
    {id:'revenue',label:'Revenue & tax',ratios:[],metrics:[m('room','Room revenue',rv.roomPaise),m('service','Service revenue',rv.servicePaise),m('discount','Discounts',rv.discountsPaise),
      m('adjustment','Adjustments & corrections',rv.correctionsPaise),m('net','Net revenue (excl. tax)',rv.netPaise),m('tax','GST (CGST + SGST)',rv.taxPaise,'money','Tax is a liability, not revenue · GST master, illustrative — CA to confirm'),
      m('gross','Charges incl. tax',rv.grossPaise)]},
    {id:'receipts',label:'Receipts & deposits',ratios:[],metrics:[...r.receipts.byTender.map(t=>m('tender:'+t.tender,t.tender+' (payments − refunds)',t.netPaise)),m('net','Net receipts (payments − refunds)',r.receipts.netPaise),
      m('deposit-in','Deposits received',r.receipts.depositsInPaise),m('deposit-out','Deposits returned',-r.receipts.depositsOutPaise),m('company-credit','Company credit (city ledger)',r.receipts.companyCreditPaise)]},
    {id:'expenses',label:'Expenses & cash',ratios:[],metrics:[m('cash','Drawer-cash expenses',r.expenses.cashPaise),m('reversals','Reversals included above',r.expenses.reversalsPaise),m('total','Posted expenses (net of reversals)',r.expenses.totalPaise),
      m('cash-in','Cash into drawer (payments + deposits + POS sales)',r.drawer.cashInPaise),m('cash-out','Cash out of drawer (refunds + returns + POS voids)',r.drawer.cashOutPaise)]},
    {id:'occupancy',label:'Occupancy',ratios:[{id:'occupancy',label:'Occupancy %',display:o.occupancyPct,formula:`${o.occupiedRoomNights} occupied ÷ ${o.sellableRoomNights} sellable × 100, 1 dp`},
      {id:'adr',label:'ADR',display:ratio(o.adrPaise),formula:`${money(o.roomRevenuePaise)} room revenue ÷ ${o.occupiedRoomNights} occupied room-nights`},
      {id:'revpar',label:'RevPAR',display:ratio(o.revParPaise),formula:`${money(o.roomRevenuePaise)} room revenue ÷ ${o.sellableRoomNights} sellable room-nights`}],
      metrics:[m('sellable','Sellable room-nights',o.sellableRoomNights,'count'),m('occupied','Occupied room-nights',o.occupiedRoomNights,'count'),m('room-revenue','Room revenue',o.roomRevenuePaise)]},
  ];
  return {from:r.from,to:r.to,dates,tabs};
}
