// F4-T2 · Owner daily digest (F4-CONTRACT §3). dgsCard(s, date) is a pure selector; the day close writes two held drafts per day.
import type {State,DGSDraft,DGSChannel,DGSState} from '../types';
import {HccError} from '../errors';
import {money,addDays} from '../money';
import {reportOccupancyRange,reportPct,reportRatioMoney,reportExpenses,approvalKindLabel} from './closing';
import {rctReport} from './rpt-revcat';
import {qwReorderAlerts,fbkOverdue,fbkSummary} from '../../features/digest/_contract-stubs-f4';

export const DGS_REASON='Delivery waits for the messaging gateway (F9) and the internet switch (F5)';
export const dgsCanView=(s:State,uid:string)=>{const u=s.users.find(x=>x.id===uid);return !!u&&u.enabled&&u.role==='Owner';};
export function dgsRequire(s:State,uid:string){if(!dgsCanView(s,uid))throw new HccError('FORBIDDEN','The owner digest is for the Owner only (PERM-DGS-001).');}
export const dgsSlice=(s:State):DGSState=>s.digestX??{drafts:[]};
export const dgsYesterday=(s:State)=>addDays(s.date,-1);

export type DGSCard={date:string;
  occupancy:{sold:number;sellable:number;pct:string;adr:string;revpar:string};
  revenue:{label:string;net:number;gross:number}[];revenueTotal:{net:number;gross:number};
  collections:{tender:string;amount:number}[];collected:number;
  advances:{received:number;adjusted:number;held:number|undefined};
  expenses:{posted:number;approved:number;pending:number};
  cashAtClose:number|undefined;
  pendingApprovals:{kind:string;count:number}[];
  reorder:number;overdue:{complaints:number;lostFound:number};feedback:{count:number;average:number};
  tomorrow:{arrivals:number;rooms:string[];events:number}};

export function dgsCard(s:State,date:string):DGSCard{
  const occ=reportOccupancyRange(s,date,date),r=rctReport(s,date,date),cl=s.closings?.[date];
  const tenderCols:[string,number][]=[['Cash',r.bTotal.cash],['UPI',r.bTotal.upi],['Card',r.bTotal.card],['Bank transfer',r.bTotal.bank],...Object.entries(r.bTotal.otherTenders)];
  const kinds=new Map<string,number>();
  for(const a of s.approvals)if(a.status==='Pending')kinds.set(approvalKindLabel(a.kind),(kinds.get(approvalKindLabel(a.kind))??0)+1);
  const tom=addDays(date,1),arr=s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival===tom);
  const ex=reportExpenses(s,date,date);
  return {date,
    occupancy:{sold:occ.occupied,sellable:occ.sellable,pct:reportPct(occ.occupied,occ.sellable),adr:reportRatioMoney(occ.roomRevenue,occ.occupied),revpar:reportRatioMoney(occ.roomRevenue,occ.sellable)},
    revenue:r.a.filter(x=>x.net||x.gross).map(x=>({label:x.label,net:x.net,gross:x.gross})),
    revenueTotal:{net:r.a.reduce((t,x)=>t+x.net,0),gross:r.a.reduce((t,x)=>t+x.gross,0)},
    collections:tenderCols.filter(([,v])=>v).map(([tender,amount])=>({tender,amount})),collected:r.bTotal.netReceived,
    advances:{received:r.bTotal.advancesReceived,adjusted:r.bTotal.advancesAdjusted,held:cl?.deposits.heldAtClose??r.c.closingAdvances},
    expenses:{posted:ex.reduce((t,e)=>t+e.amount,0),approved:ex.length,pending:s.expenses.filter(e=>e.status==='Pending'&&e.date===date).length},
    cashAtClose:cl?.cash.counted,
    pendingApprovals:[...kinds].map(([kind,count])=>({kind,count})),
    reorder:qwReorderAlerts(s).length,overdue:fbkOverdue(s,date),feedback:fbkSummary(s,date),
    tomorrow:{arrivals:arr.length,rooms:arr.map(b=>b.room),events:0}};
}
const pendingTotal=(c:DGSCard)=>c.pendingApprovals.reduce((t,x)=>t+x.count,0);
/** E-mail body (money allowed; Owner + Accountant). */
export function dgsEmailBody(c:DGSCard):string{
  return [`Owner digest · ${c.date}`,'',
    `Occupancy ${c.occupancy.pct} (${c.occupancy.sold} of ${c.occupancy.sellable} rooms) · ADR ${c.occupancy.adr} · RevPAR ${c.occupancy.revpar}`,
    'Revenue by category (net / incl. GST):',...c.revenue.map(x=>`  ${x.label}: ${money(x.net)} / ${money(x.gross)}`),`  Total: ${money(c.revenueTotal.net)} / ${money(c.revenueTotal.gross)}`,
    'Collections by tender:',...c.collections.map(x=>`  ${x.tender}: ${money(x.amount)}`),`  Total: ${money(c.collected)}`,
    `Advances received ${money(c.advances.received)} · adjusted ${money(c.advances.adjusted)}${c.advances.held===undefined?'':` · held ${money(c.advances.held)}`}`,
    `Expenses ${money(c.expenses.posted)} (${c.expenses.approved} posted, ${c.expenses.pending} pending)`,
    `Cash in drawer at close: ${c.cashAtClose===undefined?'not closed':money(c.cashAtClose)}`,
    `Pending approvals: ${pendingTotal(c)}${c.pendingApprovals.length?' ('+c.pendingApprovals.map(x=>`${x.kind} ${x.count}`).join(', ')+')':''}`,
    `Reorder alerts: ${c.reorder} · complaints / lost & found over SLA: ${c.overdue.complaints + c.overdue.lostFound} · feedback: ${c.feedback.count}${c.feedback.count?` (average ${c.feedback.average.toFixed(1)} stars)`:''}`,
    `Tomorrow: ${c.tomorrow.arrivals} arrival${c.tomorrow.arrivals===1?'':'s'}, ${c.tomorrow.events} event${c.tomorrow.events===1?'':'s'}`].join('\n');
}
/** WhatsApp body (DEC-202 #7): counts only, no names, no money. */
export function dgsWhatsAppBody(c:DGSCard):string{
  return [`Digest ${c.date}`,`Rooms sold ${c.occupancy.sold} of ${c.occupancy.sellable}`,`Arrivals tomorrow ${c.tomorrow.arrivals}`,`Pending approvals ${pendingTotal(c)}`,
    `Reorder alerts ${c.reorder}`,`Complaints over SLA ${c.overdue.complaints + c.overdue.lostFound}`].join('\n');
}
/** The two held drafts of a closed date (idempotent per date and channel: a re-close replaces them). Never sent. */
export function dgsWriteDrafts(s:State,uid:string,date:string):DGSState{
  const c=dgsCard(s,date),keep=dgsSlice(s).drafts.filter(d=>d.date!==date);
  const mk=(channel:DGSChannel,countsOnly:boolean,body:string):DGSDraft=>({id:`DGS-${date}-${channel}`,date,channel,countsOnly,status:'held',reason:DGS_REASON,body,createdAt:s.clock,by:uid});
  return {drafts:[...keep,mk('Email',false,dgsEmailBody(c)),mk('WhatsApp',true,dgsWhatsAppBody(c))]};
}
export const dgsClosedDates=(s:State)=>Object.keys(s.closings??{}).sort().reverse();
