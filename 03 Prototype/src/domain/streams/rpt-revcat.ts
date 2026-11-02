// F4-T2 · Revenue by room category and tender (rpt.money.revenue-by-category-tender) — a view over the existing report selectors
// (reportLines / reportReceipts / reportPack), no new money logic. Contract 02 Planning/agents/F4-CONTRACT.md §2.
import type {State,Booking,Line,RCTBasis,RCTBlockARow,RCTBlockBRow,RCTBridge,RCTReport} from '../types';
import {HccError} from '../errors';
import {gstLineSplit} from '../gst';
import {csvMinimal} from '../csv';
import {money} from '../money';
import {reportDates,reportLines,reportReceipts,reportSellableOn,reportStaysOn} from './closing';
import {posSlice} from './pos';
import {accTrialBalance6} from './accounting-reports';

/** Roles that see the report and export it (DEC-202 #1, PERM-RPT-002 / RPT-003). Reception, Cashier, Restaurant, Housekeeping, Maintenance: FORBIDDEN. */
export const RCT_ROLES=['Owner','Accountant','Manager'];
export const rctCanView=(s:State,uid:string)=>{const u=s.users.find(x=>x.id===uid);return !!u&&u.enabled&&RCT_ROLES.includes(u.role);};
export function rctRequire(s:State,uid:string){if(!rctCanView(s,uid))throw new HccError('FORBIDDEN','Revenue by category and tender is for the Owner, Accountant and Manager.');}

const typeOf=(s:State,room:string)=>s.rooms.find(r=>r.id===room)?.type;
const keyOf=(s:State,b:Booking)=>typeOf(s,b.room)??'unassigned';
const labelOf=(k:string)=>k==='unassigned'?'Unassigned room':k;
const zeroA=(key:string,label:string):RCTBlockARow=>({key,label,rooms:0,nightsSold:0,occPct:0,roomTariff:0,extraPerson:0,mealPlans:0,otherRoomCharges:0,discounts:0,corrections:0,net:0,cgst:0,sgst:0,gross:0,adr:0});
const zeroB=(key:string,label:string):RCTBlockBRow=>({key,label,cash:0,upi:0,card:0,bank:0,companyCredit:0,udhaar:0,ota:0,otherTenders:{},advancesReceived:0,advancesAdjusted:0,advancesRefunded:0,refunds:0,netReceived:0});
export function rctLineColumn(l:Pick<Line,'kind'|'label'>):'roomTariff'|'extraPerson'|'mealPlans'|'otherRoomCharges'|'discounts'|'corrections'{
  if(l.kind==='room')return 'roomTariff';if(l.kind==='discount')return 'discounts';if(l.kind==='adjustment')return 'corrections';
  if(/extra\s+(person|bed|guest|adult)/i.test(l.label))return 'extraPerson';
  if(/meal|breakfast|\b(CP|MAP|AP)\b/i.test(l.label))return 'mealPlans';
  return 'otherRoomCharges';
}
const pct1=(n:number,d:number)=>d?Math.floor((n*1000+Math.floor(d/2))/d)/10:0;

/** The report for [from, to]. Tender column of a receipt method (seed names; any other method goes to otherTenders). */
export function rctTender(method:string):'cash'|'upi'|'card'|'bank'|undefined{
  const m=method.trim().toLowerCase();return m==='cash'?'cash':m==='upi'?'upi':m==='card'?'card':m==='bank transfer'||m==='bank'?'bank':undefined;
}
function addTender(row:RCTBlockBRow,method:string,amount:number){
  const t=rctTender(method);if(t)row[t]+=amount;else row.otherTenders[method]=(row.otherTenders[method]??0)+amount;
  row.netReceived+=amount;
}
/** Guest-folio due and advances held at the end of date d. Due = Σ lines + tax − (payments + deposit-apply + credits to company / OTA / Udhaar − refunds), i.e. what the
 *  guest ledger (1100) holds; advances = deposits − returns − applies, plus an advance carried in the opening balance that is still to be applied after d. */
function dueAt(s:State,d:string){
  let due=0,adv=0;
  for(const b of s.bookings){
    for(const l of b.lines)if(l.date<=d)due+=l.amount+gstLineSplit(l).tax;
    for(const r of b.receipts){
      if(r.kind==='deposit-apply'&&r.depositSource==='opening'&&r.date>d)adv+=r.amount;
      if(r.date>d)continue;
      if(r.kind==='payment'||r.kind==='deposit-apply'||r.kind==='company-credit'||r.kind==='ota-credit'||r.kind==='guest-credit')due-=r.amount;else if(r.kind==='refund')due+=r.amount;
      if(r.kind==='deposit')adv+=r.amount;else if(r.kind==='deposit-return'||(r.kind==='deposit-apply'&&r.depositSource!=='opening'))adv-=r.amount;}
  }
  return {due,adv};
}
/** Ledger-by-ledger comparison of Block C with the trial balance (accounting stream). `ok` = the report figure equals the ledger figure. */
export type RCTLedgerRow={account:string;name:string;what:string;report:number;ledger:number;ok:boolean};
export function rctReport(s:State,from:string,to:string,basis:RCTBasis='business-date'):RCTReport&{tb:{balanced:boolean;receivable:number;payable:number;rows:RCTLedgerRow[]}}{
  const dates=reportDates(from,to);
  // ── Block A ──
  const rows=new Map<string,RCTBlockARow>();
  const rowA=(k:string,label=labelOf(k))=>{let r=rows.get(k);if(!r){r=zeroA(k,label);rows.set(k,r);}return r;};
  for(const r of s.rooms)rowA(r.type);
  const inBasis=basis==='stay'?(b:Booking)=>b.arrival>=from&&b.arrival<=to:()=>true;
  const lines=basis==='stay'?s.bookings.filter(b=>b.arrival>=from&&b.arrival<=to).flatMap(b=>b.lines.map(l=>({b,l}))):reportLines(s,from,to);
  for(const {b,l} of lines){const row=rowA(keyOf(s,b));const {tax,cgst,sgst}=gstLineSplit(l);
    row[rctLineColumn(l)]+=l.amount;row.net+=l.amount;row.cgst+=cgst;row.sgst+=sgst;row.gross+=l.amount+tax;}
  void inBasis;
  for(const d of dates){for(const r of s.rooms)if(reportSellableOn(s,d).includes(r))rowA(r.type).rooms++;
    for(const b of reportStaysOn(s,d)){if(basis==='stay'&&!(b.arrival>=from&&b.arrival<=to))continue;rowA(keyOf(s,b)).nightsSold++;}}
  const room=[...rows.values()].filter(r=>r.key!=='unassigned'||r.net||r.nightsSold).sort((a,b)=>a.key==='unassigned'?1:b.key==='unassigned'?-1:a.label<b.label?-1:1);
  for(const r of room){r.occPct=pct1(r.nightsSold,r.rooms);r.adr=r.nightsSold?Math.round(r.roomTariff/r.nightsSold):0;}
  const aTotal=zeroA('total','Total (folios)');
  const sumInto=(t:RCTBlockARow,r:RCTBlockARow)=>{for(const k of ['rooms','nightsSold','roomTariff','extraPerson','mealPlans','otherRoomCharges','discounts','corrections','net','cgst','sgst','gross'] as const)t[k]+=r[k];};
  for(const r of room)sumInto(aTotal,r);
  aTotal.occPct=pct1(aTotal.nightsSold,aTotal.rooms);aTotal.adr=aTotal.nightsSold?Math.round(aTotal.roomTariff/aTotal.nightsSold):0;
  // Outlet rows (not part of the folio total, which ties to Revenue & tax): restaurant (POS), counter bills, hall (F6, 0 now).
  const rest=zeroA('restaurant','Restaurant (POS)'),ctr=zeroA('counter','Counter bills'),hall=zeroA('hall','Hall');
  for(const r of posSlice(s).receipts)if(r.date>=from&&r.date<=to){rest.otherRoomCharges+=r.net;rest.net+=r.net;rest.cgst+=Math.ceil(r.tax/2);rest.sgst+=Math.floor(r.tax/2);rest.gross+=r.net+r.tax;}
  for(const bl of s.counterX?.bills??[])if(bl.date>=from&&bl.date<=to&&bl.status==='Issued'){ctr.otherRoomCharges+=bl.net;ctr.net+=bl.net;ctr.cgst+=bl.cgst;ctr.sgst+=bl.sgst;ctr.gross+=bl.total;}
  const a=[...room,rest,ctr,hall];
  // ── Block B ──
  const brow=new Map<string,RCTBlockBRow>();
  const rowB=(k:string,label=labelOf(k))=>{let r=brow.get(k);if(!r){r=zeroB(k,label);brow.set(k,r);}return r;};
  for(const r of s.rooms)rowB(r.type);
  const bTotal=zeroB('total','Total');
  for(const {b,r} of reportReceipts(s,from,to)){const row=rowB(keyOf(s,b));
    const feed=(k:RCTBlockBRow,sign:1|-1)=>{
      if(r.kind==='payment'||r.kind==='refund'){const v=r.kind==='refund'?-r.amount:r.amount;addTender(k,r.method,v);if(r.kind==='refund')k.refunds+=r.amount;}
      else if(r.kind==='deposit')k.advancesReceived+=r.amount;
      else if(r.kind==='deposit-apply')k.advancesAdjusted+=r.amount;
      else if(r.kind==='deposit-return')k.advancesRefunded+=r.amount;
      else if(r.kind==='company-credit')k.companyCredit+=r.amount;
      else if(r.kind==='guest-credit')k.udhaar+=r.amount;
      else if(r.kind==='ota-credit')k.ota+=r.amount;
      void sign;};
    feed(row,1);feed(bTotal,1);}
  const brooms=[...brow.values()].filter(r=>r.key!=='unassigned'||r.netReceived||r.advancesReceived||r.companyCredit||r.udhaar||r.ota).sort((x,y)=>x.key==='unassigned'?1:y.key==='unassigned'?-1:x.label<y.label?-1:1);
  const rb=zeroB('restaurant','Restaurant (POS)'),cb=zeroB('counter','Counter bills'),hb=zeroB('hall','Hall');
  for(const r of posSlice(s).receipts)if(r.date>=from&&r.date<=to)addTender(rb,r.method,r.amount);
  for(const bl of s.counterX?.bills??[])if(bl.date>=from&&bl.date<=to&&bl.status==='Issued'){
    if(bl.settlement.mode==='paid')addTender(cb,bl.settlement.method??'Cash',bl.total);else if(bl.settlement.mode==='udhaar')cb.udhaar+=bl.total;else cb.companyCredit+=bl.total;}
  // ── Block C ──
  const o=dueAt(s,addDays1(from,-1)),c=dueAt(s,to);
  const gross=basis==='stay'?reportLines(s,from,to).reduce((t,x)=>t+x.l.amount+gstLineSplit(x.l).tax,0):aTotal.gross, // the bridge is always on business dates (the stay basis is Block A only)
    netReceived=bTotal.netReceived,adjusted=bTotal.advancesAdjusted;
  const movement=bTotal.advancesReceived-adjusted-bTotal.advancesRefunded;
  const credits=bTotal.companyCredit+bTotal.udhaar+bTotal.ota;
  const difference=Math.abs(c.due-(o.due+gross-netReceived-adjusted-credits))+Math.abs((c.adv-o.adv)-movement);
  const bridge:RCTBridge={openingDue:o.due,gross,netReceived,advanceMovement:movement,closingDue:c.due,openingAdvances:o.adv,closingAdvances:c.adv,difference};
  const tb=accTrialBalance6(s,from,to,false);
  const L=(code:string)=>tb.rows.find(x=>x.account===code);
  const open=(code:string)=>{const r=L(code);return r?r.openingDebit-r.openingCredit:0;},close=(code:string)=>{const r=L(code);return r?r.closingDebit-r.closingCredit:0;},dr=(code:string)=>L(code)?.debit??0;
  const ledgerRows:RCTLedgerRow[]=[
    {account:'1100',name:'Guest ledger (in-house folios)',what:'Opening due',report:o.due,ledger:open('1100'),ok:o.due===open('1100')},
    {account:'1100',name:'Guest ledger (in-house folios)',what:'Closing due',report:c.due,ledger:close('1100'),ok:c.due===close('1100')},
    {account:'2000',name:'Advance deposits',what:'Advances held at start',report:o.adv,ledger:-open('2000'),ok:o.adv===-open('2000')},
    {account:'2000',name:'Advance deposits',what:'Advances held at end',report:c.adv,ledger:-close('2000'),ok:c.adv===-close('2000')},
    {account:'1110',name:'Company receivables',what:'Company credit raised',report:bTotal.companyCredit,ledger:dr('1110'),ok:bTotal.companyCredit===dr('1110')},
    {account:'1115',name:'OTA receivables',what:'OTA credit raised',report:bTotal.ota,ledger:dr('1115'),ok:bTotal.ota===dr('1115')},
    {account:'1120',name:'Udhaar receivables',what:'Guest (Udhaar) credit raised',report:bTotal.udhaar,ledger:dr('1120'),ok:bTotal.udhaar===dr('1120')}];
  return {from,to,basis,legacy:false,a,aTotal,b:[...brooms,rb,cb,hb],bTotal,c:bridge,tb:{balanced:tb.balanced,receivable:close('1110')+close('1115')+close('1120'),payable:-close("2200"),rows:ledgerRows}};
}
function addDays1(d:string,n:number){const t=new Date(d+'T12:00:00Z');t.setUTCDate(t.getUTCDate()+n);return t.toISOString().slice(0,10);}

/** Role-checked entry for the UI and the tests. */
export function rctReportFor(s:State,uid:string,from:string,to:string,basis:RCTBasis='business-date'){rctRequire(s,uid);return rctReport(s,from,to,basis);}

// ── CSV (the Excel workbook uses the same rows; screen = export) ──
export const RCT_A_HEAD=['Category','Rooms (room-nights sellable)','Room-nights sold','Occ %','Room tariff','Extra person','Meal plans','Other charges','Discounts','Corrections','Net (excl. GST)','CGST','SGST','Gross (incl. GST)','ADR'];
export const rctARow=(r:RCTBlockARow):(string|number)[]=>[r.label,r.rooms,r.nightsSold,r.occPct,r.roomTariff,r.extraPerson,r.mealPlans,r.otherRoomCharges,r.discounts,r.corrections,r.net,r.cgst,r.sgst,r.gross,r.adr];
export const RCT_B_HEAD=['Category','Cash','UPI','Card','Bank transfer','Company credit','Udhaar','OTA','Other tenders','Advances received','Advances adjusted','Advances refunded','Refunds (in tenders)','Net received'];
export const rctBRow=(r:RCTBlockBRow):(string|number)[]=>[r.label,r.cash,r.upi,r.card,r.bank,r.companyCredit,r.udhaar,r.ota,Object.values(r.otherTenders).reduce((x,y)=>x+y,0),r.advancesReceived,r.advancesAdjusted,r.advancesRefunded,r.refunds,r.netReceived];
const rs=(p:number|string)=>typeof p==='number'?(p/100).toFixed(2):p;
export function rctCsv(r:RCTReport):string{
  const line=(a:(string|number)[],money_=true)=>a.map((v,i)=>csvMinimal(i&&money_&&typeof v==='number'?rs(v):v)).join(',');
  const c=r.c;
  return [line(['Revenue by room category and tender'],false),line(['From',r.from,'To',r.to,'Basis',r.basis==='stay'?'Stay (arrival date)':'Business date'],false),
    ...(r.legacy?[line([RCT_LEGACY_HEADER],false)]:[]),'',
    line(['A. Revenue by category'],false),line(RCT_A_HEAD,false),...r.a.map(x=>line(rctARow(x))),line(rctARow(r.aTotal)),'',
    line(['B. Collections by tender'],false),line(RCT_B_HEAD,false),...r.b.map(x=>line(rctBRow(x))),line(rctBRow(r.bTotal)),'',
    line(['C. Bridge'],false),line(['Opening due',rs(c.openingDue),'Gross',rs(c.gross),'Net received',rs(c.netReceived),'Advance movement',rs(c.advanceMovement),'Closing due',rs(c.closingDue),'Difference',rs(c.difference)],false)].join('\r\n');
}
export const rctMoney=money;

// ── Legacy financial years (LG contract §6): the same two blocks over the old Lodge bills and receipts (lgc.bill / lgc.receipt, read by the server) ──
export const RCT_LEGACY_HEADER='Legacy data (old Lodge software)';
/** One lgc.bill row (paise). `room` = the legacy room name; the category comes from the rooms master by that name, else `unassigned`. */
export type RCTLegacyBill={fy:string;billNo:number;date:string;room:string;roomCharge:number;extra:number;fb:number;laundry:number;other:number;discount:number;roomGst:number;fbGst:number;laundryGst:number;otherGst:number;advance:number;net:number};
/** One lgc.receipt row: advance and payment rows are collections, `security` is a refundable deposit (an advance), `refund` goes out. */
export type RCTLegacyReceipt={fy:string;date:string;room:string;kind:'advance'|'payment'|'refund'|'security';method:'CASH'|'UPI'|'CARD'|'BANK'|'OTHER';amount:number};
const LEGACY_METHOD:Record<RCTLegacyReceipt['method'],string>={CASH:'Cash',UPI:'UPI',CARD:'Card',BANK:'Bank transfer',OTHER:'Other'};
/** The report for a legacy range. `typeOf(room)` = the room type in the rooms master (undefined = unmapped, kept as a row `unassigned`, never dropped). */
export function rctLegacyReport(bills:RCTLegacyBill[],receipts:RCTLegacyReceipt[],typeOf:(room:string)=>string|undefined,from:string,to:string):RCTReport{
  const ra=new Map<string,RCTBlockARow>(),rb=new Map<string,RCTBlockBRow>();
  const A=(k:string)=>{let r=ra.get(k);if(!r){r=zeroA(k,labelOf(k));ra.set(k,r);}return r;},B=(k:string)=>{let r=rb.get(k);if(!r){r=zeroB(k,labelOf(k));rb.set(k,r);}return r;};
  const aTotal=zeroA('total','Total (legacy bills)'),bTotal=zeroB('total','Total');
  for(const x of bills)if(x.date>=from&&x.date<=to){
    const net=x.roomCharge+x.extra+x.fb+x.laundry+x.other-x.discount,tax=x.roomGst+x.fbGst+x.laundryGst+x.otherGst,sg=Math.trunc(tax/2),cg=tax-sg;
    for(const r of [A(typeOf(x.room)??'unassigned'),aTotal]){r.roomTariff+=x.roomCharge;r.extraPerson+=x.extra;r.otherRoomCharges+=x.fb+x.laundry+x.other;r.discounts-=x.discount;r.net+=net;r.cgst+=cg;r.sgst+=sg;r.gross+=net+tax;}}
  for(const x of receipts)if(x.date>=from&&x.date<=to)for(const r of [B(typeOf(x.room)??'unassigned'),bTotal]){
    if(x.kind==='security')r.advancesReceived+=x.amount;
    else addTender(r,LEGACY_METHOD[x.method],x.kind==='refund'?-x.amount:x.amount);
    if(x.kind==='refund')r.refunds+=x.amount;}
  const fin=(m:Map<string,RCTBlockARow>)=>[...m.values()].sort((p,q)=>p.key==='unassigned'?1:q.key==='unassigned'?-1:p.label<q.label?-1:1);
  for(const r of [...ra.values(),aTotal])r.adr=0;
  const movement=bTotal.advancesReceived;
  return {from,to,basis:'business-date',legacy:true,a:fin(ra),aTotal,b:[...rb.values()].sort((p,q)=>p.key==='unassigned'?1:q.key==='unassigned'?-1:p.label<q.label?-1:1),bTotal,
    c:{openingDue:0,gross:aTotal.gross,netReceived:bTotal.netReceived,advanceMovement:movement,closingDue:0,openingAdvances:0,closingAdvances:movement,difference:0}};
}
