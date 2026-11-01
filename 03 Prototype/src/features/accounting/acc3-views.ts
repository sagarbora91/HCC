// ACC-3 · acc3-ui — STUBS against the ACC-3 reports contract (02 Planning/agents/ACC3-REPORTS-CONTRACT.md §2–§6). The report lanes (acc3-pl, acc3-ageing,
// acc3-tally, acc3-exports) own the real selectors `accPl`, `accBalanceSheet`, `accCashRatio`, `accAgeing`, `accGstSummary`, `accTallyMasters`,
// `accTallyVouchers` and the export builders; the integrator deletes this file's bodies and re-exports the real ones from services/mock.
// The stubs only reshape the existing trial balance so the screens draw sensible figures; no accounting rule lives here. Money = integer paise.
import {accTrialBalance6,accTallyXml,type State} from '../../services/mock';
import {csvMinimal} from '../../domain/csv';
import {buildXlsx,zipStored} from '../../app/xlsx';

// ── contract §2 shapes (copied) ──
export type ACCPlLine={code:string;name:string;paise:number;group:string};
export type ACCPlGroup={name:string;lines:ACCPlLine[];total:number};
export type ACCPl={from:string;to:string;provisional:boolean;
  revenue:{lines:ACCPlLine[];total:number};otherIncome:{lines:ACCPlLine[];total:number};
  expenses:{consumption:ACCPlGroup;employee:ACCPlGroup;finance:ACCPlGroup;depreciation:ACCPlGroup;other:ACCPlGroup;total:number};
  profitBeforeTax:number;byTallyGroup:{group:string;name:string;paise:number}[]};
export type ACCBsBlock={name:string;lines:{code:string;name:string;paise:number}[];total:number};
export type ACCBalanceSheet={asOf:string;provisional:boolean;
  liabilities:{ownersFunds:ACCBsBlock;nonCurrent:ACCBsBlock;current:ACCBsBlock;total:number};
  assets:{nonCurrent:ACCBsBlock;current:ACCBsBlock;total:number};balanced:boolean;suspense:number;openingDifference:number};
export type ACCCashRatio={fy:string;cashReceipts:number;totalReceipts:number;cashPayments:number;totalPayments:number;receiptsPct:number;paymentsPct:number;flagged:boolean};
// ── contract §3 shapes (copied) ──
export type ACCAgeingBill={ref:string;date:string;due:string;amount:number;paid:number;open:number;days:number};
export type ACCAgeingRow={account:string;party:string;name:string;control:string;bills:ACCAgeingBill[];open:number;b0_30:number;b31_60:number;b61_90:number;b90:number};
export type ACCAgeing={asOf:string;side:'receivable'|'payable';rows:ACCAgeingRow[];totals:{open:number;b0_30:number;b31_60:number;b61_90:number;b90:number}};
export type ACCGstSummary={month:string;
  output:{rate5:number;rate18:number;b2b:number;b2cs:number;advances11A:number;advances11B:number;creditNotes:number};
  itc:{cgst:number;sgst:number;igst:number};rcm:number;setoff:number;cashPaid:number;tcsCredit:number;cashLedgerBalance:number;
  ties:{gstr1:boolean;gstr3b:boolean;differences:string[]}};

type Row=ReturnType<typeof accTrialBalance6>['rows'][number];
const BUCKET=(code:string)=>code==='5000'||code==='5010'||code==='5015'?'consumption':code==='5100'||code==='5110'||code==='5120'?'employee':code==='5080'||code==='5090'?'finance':code==='5200'?'depreciation':'other';
const sum=(l:{paise:number}[])=>l.reduce((a,x)=>a+x.paise,0);
const grp=(name:string,lines:ACCPlLine[]):ACCPlGroup=>({name,lines,total:sum(lines)});

export function accPl(s:State,from:string,to:string):ACCPl{
  const tb=accTrialBalance6(s,from,to,false);const L=(r:Row,sign:1|-1):ACCPlLine=>({code:r.account,name:r.name,paise:sign*(r.credit-r.debit),group:r.group});
  const inc=tb.rows.filter(r=>/^4/.test(r.account)).map(r=>L(r,1)),exp=tb.rows.filter(r=>/^5/.test(r.account)).map(r=>L(r,-1));
  const revenue=inc.filter(l=>!['4950','4960'].includes(l.code)),other=inc.filter(l=>['4950','4960'].includes(l.code));
  const by=(k:string)=>exp.filter(l=>BUCKET(l.code)===k);
  const expenses={consumption:grp('Consumption',by('consumption')),employee:grp('Employee cost',by('employee')),finance:grp('Finance cost',by('finance')),depreciation:grp('Depreciation',by('depreciation')),other:grp('Other expenses',by('other')),total:sum(exp)};
  const g=new Map<string,number>();for(const l of [...inc,...exp.map(x=>({...x,paise:-x.paise}))])g.set(l.group,(g.get(l.group)??0)+l.paise);
  return {from,to,provisional:tb.openingPending,revenue:{lines:revenue,total:sum(revenue)},otherIncome:{lines:other,total:sum(other)},expenses,
    profitBeforeTax:sum(revenue)+sum(other)-sum(exp),byTallyGroup:[...g].map(([group,paise])=>({group,name:group,paise}))};
}
export function accBalanceSheet(s:State,asOf:string):ACCBalanceSheet{
  const tb=accTrialBalance6(s,'2000-01-01',asOf,false);const bal=(r:Row)=>r.closingDebit-r.closingCredit;
  const line=(r:Row,sign:1|-1)=>({code:r.account,name:r.name,paise:sign*bal(r)});
  const blk=(name:string,lines:{code:string;name:string;paise:number}[]):ACCBsBlock=>({name,lines,total:sum(lines)});
  const pl=tb.rows.filter(r=>/^[45]/.test(r.account)).reduce((a,r)=>a-bal(r),0);
  const owners=blk('Owner funds',[...tb.rows.filter(r=>/^3/.test(r.account)).map(r=>line(r,-1)),{code:'PL',name:'Current-year profit',paise:pl}]);
  const cur=blk('Current liabilities',tb.rows.filter(r=>/^2[1-3]/.test(r.account)).map(r=>line(r,-1))),non=blk('Non-current liabilities',tb.rows.filter(r=>/^24/.test(r.account)).map(r=>line(r,-1)));
  const aNon=blk('Fixed assets',tb.rows.filter(r=>/^15/.test(r.account)).map(r=>line(r,1))),aCur=blk('Current assets',tb.rows.filter(r=>/^1[0-4]/.test(r.account)).map(r=>line(r,1)));
  const lt=owners.total+non.total+cur.total,at=aNon.total+aCur.total;
  const sus=tb.rows.find(r=>r.account==='9999'),od=tb.rows.find(r=>r.account==='1400');
  return {asOf,provisional:tb.openingPending,liabilities:{ownersFunds:owners,nonCurrent:non,current:cur,total:lt},assets:{nonCurrent:aNon,current:aCur,total:at},
    balanced:lt===at,suspense:sus?bal(sus):0,openingDifference:od?bal(od):0};
}
/** `fy` = the financial year's first day (yyyy-04-01). */
export function accCashRatio(s:State,fy:string):ACCCashRatio{
  const end=`${+fy.slice(0,4)+1}-03-31`;const tb=accTrialBalance6(s,fy,end<s.date?end:s.date,false);
  const cash=tb.rows.filter(r=>r.account==='1000'||r.account==='1005');const cashDr=cash.reduce((a,r)=>a+r.debit,0),cashCr=cash.reduce((a,r)=>a+r.credit,0);
  const pct=(a:number,b:number)=>b?Math.round(a*10000/b)/100:0;
  return {fy,cashReceipts:cashDr,totalReceipts:tb.totals.debit,cashPayments:cashCr,totalPayments:tb.totals.credit,receiptsPct:pct(cashDr,tb.totals.debit),paymentsPct:pct(cashCr,tb.totals.credit),
    flagged:pct(cashDr,tb.totals.debit)>5||pct(cashCr,tb.totals.credit)>5};
}
export function accAgeing(s:State,asOf:string,side:'receivable'|'payable'):ACCAgeing{
  const tb=accTrialBalance6(s,'2000-01-01',asOf,true);const ctl=side==='receivable'?['1100','1110','1115','1120']:['2200'];
  const rows:ACCAgeingRow[]=tb.rows.filter(r=>r.control&&ctl.includes(r.control)).map(r=>{const open=Math.abs(r.closingDebit-r.closingCredit);
    return {account:r.account,party:r.account.split('/')[1]??r.account,name:r.name,control:r.control!,bills:open?[{ref:'opening',date:asOf,due:asOf,amount:open,paid:0,open,days:0}]:[],open,b0_30:open,b31_60:0,b61_90:0,b90:0};}).filter(r=>r.open>0);
  const t=(k:'open'|'b0_30'|'b31_60'|'b61_90'|'b90')=>rows.reduce((a,r)=>a+r[k],0);
  return {asOf,side,rows,totals:{open:t('open'),b0_30:t('b0_30'),b31_60:t('b31_60'),b61_90:t('b61_90'),b90:t('b90')}};
}
export function accGstSummary(_s:State,month:string):ACCGstSummary{
  return {month,output:{rate5:0,rate18:0,b2b:0,b2cs:0,advances11A:0,advances11B:0,creditNotes:0},itc:{cgst:0,sgst:0,igst:0},rcm:0,setoff:0,cashPaid:0,tcsCredit:0,cashLedgerBalance:0,ties:{gstr1:true,gstr3b:true,differences:[]}};
}
// ── contract §4 (the real selectors return the Tally envelope; the screens only need text, a count and the notes) ──
export type Acc3Tally={xml:string;count:number;notes:string[];error?:string};
const esc=(t:string)=>t.replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/"/g,'&quot;');
export function accTallyMasters(s:State,fy:string):Acc3Tally{
  const tb=accTrialBalance6(s,fy,fy,false);
  const xml=['<ENVELOPE>',' <HEADER><TALLYREQUEST>Import Data</TALLYREQUEST></HEADER>',' <BODY><IMPORTDATA>','  <REQUESTDESC><REPORTNAME>All Masters</REPORTNAME></REQUESTDESC>',
    ...tb.rows.map(r=>`  <LEDGER NAME="${esc(r.name)}" ACTION="Create"><PARENT>${esc(r.group)}</PARENT></LEDGER>`),' </IMPORTDATA></BODY>','</ENVELOPE>'].join('\n');
  return {xml,count:tb.rows.length,notes:['Tally’s 28 primary groups are not created; they exist in every company.','Opening balances are as at the start of the financial year.']};
}
export function accTallyVouchers(s:State,from:string,to:string):Acc3Tally{
  const p=accTallyXml(s,from,to);return {xml:p.xml,count:p.count,notes:p.error?[]:['Every voucher carries a fixed REMOTEID, so importing again replaces it.'],error:p.error};
}

// ── contract §5 export builders (stubs; acc3-exports owns the real ones) ──
export type Acc3Report='pl'|'bs'|'ageing-r'|'ageing-p'|'gst'|'cash';
export type Acc3Table={title:string;head:string[];rows:(string|number)[][]};
export type Acc3Args={from:string;to:string;asOf:string;month:string;fy:string};
const rs=(p:number)=>(p/100).toFixed(2);
export const ACC3_REPORTS:[Acc3Report,string][]=[['pl','Profit and loss'],['bs','Balance sheet'],['ageing-r','Outstanding receivable'],['ageing-p','Outstanding payable'],['gst','GST summary'],['cash','Cash ratio']];
export function accReportTable(s:State,kind:Acc3Report,a:Acc3Args):Acc3Table{
  if(kind==='pl'){const p=accPl(s,a.from,a.to);const ex=p.expenses;
    return {title:`Profit and loss ${a.from} to ${a.to}${p.provisional?' (provisional)':''}`,head:['Code','Ledger','Amount'],
      rows:[...p.revenue.lines,...p.otherIncome.lines,...ex.consumption.lines,...ex.employee.lines,...ex.finance.lines,...ex.depreciation.lines,...ex.other.lines].map(l=>[l.code,l.name,rs(l.paise)])};}
  if(kind==='bs'){const b=accBalanceSheet(s,a.asOf);const L=b.liabilities,A=b.assets;
    return {title:`Balance sheet as at ${a.asOf} (not the statutory statement)`,head:['Side','Code','Ledger','Amount'],
      rows:[...[L.ownersFunds,L.nonCurrent,L.current].flatMap(x=>x.lines.map(l=>['Liabilities',l.code,l.name,rs(l.paise)])),...[A.nonCurrent,A.current].flatMap(x=>x.lines.map(l=>['Assets',l.code,l.name,rs(l.paise)]))]};}
  if(kind==='ageing-r'||kind==='ageing-p'){const g=accAgeing(s,a.asOf,kind==='ageing-r'?'receivable':'payable');
    return {title:`Outstanding ${g.side} as at ${a.asOf}`,head:['Party','Open','0-30','31-60','61-90','Over 90'],rows:g.rows.map(r=>[r.name,rs(r.open),rs(r.b0_30),rs(r.b31_60),rs(r.b61_90),rs(r.b90)])};}
  if(kind==='gst'){const g=accGstSummary(s,a.month);
    return {title:`GST summary ${a.month}`,head:['Line','Amount'],rows:[['Output 5%',rs(g.output.rate5)],['Output 18%',rs(g.output.rate18)],['ITC CGST',rs(g.itc.cgst)],['ITC SGST',rs(g.itc.sgst)],['Set-off',rs(g.setoff)],['Paid in cash',rs(g.cashPaid)]]};}
  const c=accCashRatio(s,a.fy);
  return {title:`Cash ratio FY ${a.fy}`,head:['Measure','Cash','Total','Percent'],rows:[['Receipts',rs(c.cashReceipts),rs(c.totalReceipts),c.receiptsPct],['Payments',rs(c.cashPayments),rs(c.totalPayments),c.paymentsPct]]};
}
export const accReportCsv=(t:Acc3Table)=>[[t.title],t.head,...t.rows].map(r=>r.map(c=>csvMinimal(c)).join(',')).join('\r\n');
export const accReportXlsx=(t:Acc3Table):Uint8Array=>buildXlsx([{name:t.title.slice(0,28),rows:[[t.title],t.head,...t.rows]}]);
export const accReportsZip=(ts:Acc3Table[]):Uint8Array=>zipStored(ts.map((t,i)=>({name:`${String(i+1).padStart(2,'0')}-${t.title.replace(/[^A-Za-z0-9]+/g,'-').slice(0,40)}.csv`,data:new TextEncoder().encode(accReportCsv(t))})));
