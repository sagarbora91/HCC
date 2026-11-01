// ACC-3 lane acc3-pl · Profit & loss (ICAI vertical + Tally-group view), the optional balance sheet and the cash-ratio report.
// Contract: 02 Planning/agents/ACC3-REPORTS-CONTRACT.md §1 (one posting read), §2 (shapes copied exactly), §9, §10.
// Rules: 02 Planning/HCC_Accounting_Rulebook_v1.md §7.4, §7.5, §7.9; golden §8.4 (profit ₹1,40,400), §8.5 (₹79,27,900 both sides).
// Pure selectors over accRptVouchers / accVouchersUpTo (the postings only); ledger codes and groups from the effective chart; money = integer paise.
// Server twin: 08 Production/src/Hcc.Application/Accounting/PlQueries.cs (PlReports) — keep the two in step.
import type {State} from '../types';
import {accSlice,accVouchersUpTo,type ACCVoucher} from './accounting';
import {accGroupRoot,accLedger} from './accounting-chart';
import {accRptControl,accRptLedgerGroup,accRptLedgerName,accRptOpeningPending,accRptVouchers} from './accounting-reports';

// ── Contract §2 shapes (copied exactly; `warnings` is an optional addition, see ACC3-PL-DONE.md) ──
export type ACCPlLine={code:string;name:string;paise:number;group:string};          // paise signed as the ledger's balance movement (income positive)
export type ACCPlGroup={name:string;lines:ACCPlLine[];total:number};
export type ACCPl={
  from:string;to:string;provisional:boolean;
  revenue:{lines:ACCPlLine[];total:number};
  otherIncome:{lines:ACCPlLine[];total:number};
  expenses:{consumption:ACCPlGroup;employee:ACCPlGroup;finance:ACCPlGroup;depreciation:ACCPlGroup;other:ACCPlGroup;total:number};
  profitBeforeTax:number;
  byTallyGroup:{group:string;name:string;paise:number}[];
  warnings?:string[];
};
export type ACCBsBlock={name:string;lines:{code:string;name:string;paise:number}[];total:number};
export type ACCBalanceSheet={asOf:string;provisional:boolean;
  liabilities:{ownersFunds:ACCBsBlock;nonCurrent:ACCBsBlock;current:ACCBsBlock;total:number};
  assets:{nonCurrent:ACCBsBlock;current:ACCBsBlock;total:number};
  balanced:boolean;suspense:number;openingDifference:number};
export type ACCCashRatio={fy:string;cashReceipts:number;totalReceipts:number;cashPayments:number;totalPayments:number;receiptsPct:number;paymentsPct:number;flagged:boolean};

// ── Buckets by code (contract §2) ──
export const ACC_PL_REVENUE=['4000','4100','4200','4300','4900','4910'];
export const ACC_PL_OTHER_INCOME=['4950','4960'];
export type ACCPlBucket='consumption'|'employee'|'finance'|'depreciation'|'other';
const EXPENSE_BUCKET:Record<string,ACCPlBucket>={'5000':'consumption','5010':'consumption','5015':'consumption','5100':'employee','5110':'employee','5120':'employee',
  '5080':'finance','5090':'finance','5200':'depreciation','5300':'other','5400':'other','5900':'other'};
export const ACC_PL_BUCKET_NAMES:Record<ACCPlBucket,string>={consumption:'Cost of materials consumed',employee:'Employee benefits expense',finance:'Finance costs',
  depreciation:'Depreciation',other:'Other expenses'};
/** Cash-ratio threshold (rulebook §7.9): 5 % = 500 basis points. */
export const ACC_CASH_RATIO_LIMIT_BPS=500;

/** Primary Tally group of a ledger code (a party ledger takes its control's); legacy group names of server-only accounts mapped to a primary. */
export function accPlRoot(s:State,code:string):{id:string;name:string;side:'BS'|'PL';nature:'Dr'|'Cr'}{
  const gid=accLedger(s,code)?.group??accRptLedgerGroup(s,code);
  const r=accGroupRoot(s,gid);if(r)return {id:r.id,name:r.name,side:r.side,nature:r.nature};
  const legacy:Record<string,string>={Income:'indirect-income',Expense:'indirect-expenses',Asset:'current-assets',Liability:'current-liabilities'};
  const g=accGroupRoot(s,legacy[gid]??'suspense')!;return {id:g.id,name:g.name,side:g.side,nature:g.nature};
}
const byCode=(a:{code:string},b:{code:string})=>a.code<b.code?-1:a.code>b.code?1:0;
const inRange=(c:string,lo:string,hi:string)=>c>=lo&&c<=hi;

/** Provisional (contract §1): the dated vouchers of the range mix locked and open days. */
function mixedLocks(s:State,vs:ACCVoucher[]):boolean{
  const locks=accSlice(s).locks??{};let locked=false,open=false;
  for(const d of new Set(vs.map(v=>v.date))){if(locks[d])locked=true;else open=true;}
  return locked&&open;
}

// ── 7.4 Profit & loss ──
/** P&L for [from, to] from the period vouchers (the opening entry is never P&L). Income lines Cr − Dr, expense lines Dr − Cr.
 *  Built-in codes by contract §2; an Owner-added ledger by its Tally group (Direct Expenses / Purchases → consumption, Indirect Expenses →
 *  other, Sales / Direct Incomes → revenue, Indirect Incomes → other income); anything else → other expenses with a warning. */
export function accPl(s:State,from:string,to:string):ACCPl{
  const {period}=accRptVouchers(s,from,to);
  const mv=new Map<string,number>();   // control code → Dr − Cr
  for(const v of period)for(const e of v.entries){const c=accRptControl(e.account);mv.set(c,(mv.get(c)??0)+e.debit-e.credit);}
  const warnings:string[]=[];
  const rev:ACCPlLine[]=[],oth:ACCPlLine[]=[];
  const ex:Record<ACCPlBucket,ACCPlLine[]>={consumption:[],employee:[],finance:[],depreciation:[],other:[]};
  const tally=new Map<string,{group:string;name:string;paise:number}>();
  for(const [code,dr] of mv){
    if(!dr)continue;
    const root=accPlRoot(s,code);
    const isRev=ACC_PL_REVENUE.includes(code),isOth=ACC_PL_OTHER_INCOME.includes(code);
    let bucket:ACCPlBucket|undefined=EXPENSE_BUCKET[code]??(inRange(code,'5020','5070')?'other':undefined);
    const known=isRev||isOth||bucket!==undefined;
    if(!known&&root.side!=='PL')continue;                       // a balance-sheet ledger
    const group=accRptLedgerGroup(s,code),name=accRptLedgerName(s,code);
    const income=isRev||isOth||(!known&&root.nature==='Cr');
    const line:ACCPlLine={code,name,paise:income?-dr:dr,group};
    if(isRev)rev.push(line);else if(isOth)oth.push(line);
    else if(income)(root.id==='indirect-income'?oth:rev).push(line);
    else{
      if(!bucket){
        if(root.id==='direct-expenses'||root.id==='purchases')bucket='consumption';
        else{bucket='other';if(root.id!=='indirect-expenses')warnings.push(`${code} ${name}: no P&L bucket — shown under other expenses`);}
      }
      ex[bucket].push(line);
    }
    const t=tally.get(root.id)??{group:root.id,name:root.name,paise:0};t.paise+=root.nature==='Cr'?-dr:dr;tally.set(root.id,t);
  }
  const sum=(ls:ACCPlLine[])=>ls.reduce((a,l)=>a+l.paise,0);
  const grp=(b:ACCPlBucket):ACCPlGroup=>{const lines=ex[b].sort(byCode);return {name:ACC_PL_BUCKET_NAMES[b],lines,total:sum(lines)};};
  const expenses={consumption:grp('consumption'),employee:grp('employee'),finance:grp('finance'),depreciation:grp('depreciation'),other:grp('other'),total:0};
  expenses.total=expenses.consumption.total+expenses.employee.total+expenses.finance.total+expenses.depreciation.total+expenses.other.total;
  const revenue={lines:rev.sort(byCode),total:sum(rev)},otherIncome={lines:oth.sort(byCode),total:sum(oth)};
  const byTallyGroup=[...tally.values()].sort((a,b)=>{const ia=TALLY_ORDER.indexOf(a.group),ib=TALLY_ORDER.indexOf(b.group);
    return (ia<0?99:ia)-(ib<0?99:ib)||(a.group<b.group?-1:a.group>b.group?1:0);});
  const out:ACCPl={from,to,provisional:accRptOpeningPending(s)||mixedLocks(s,period),revenue,otherIncome,expenses,
    profitBeforeTax:revenue.total+otherIncome.total-expenses.total,byTallyGroup};
  if(warnings.length)out.warnings=warnings;
  return out;
}
const TALLY_ORDER=['sales','direct-income','indirect-income','purchases','direct-expenses','indirect-expenses'];

/** First day of the Indian FY (1 April) holding `date`. */
export const accPlFyStart=(date:string)=>{const y=+date.slice(0,4),m=+date.slice(5,7);return `${m>=4?y:y-1}-04-01`;};
/** FY label `2026-27` of a date. */
export const accPlFyOf=(date:string)=>{const y=+accPlFyStart(date).slice(0,4);return `${y}-${String((y+1)%100).padStart(2,'0')}`;};

// ── 7.5 Balance sheet (an HCC view, not the statutory statement) ──
const DUTIES=new Set(['2100','2110','2120','2130','2140','2150','2310','2320','2330','1300','1310','1320','1330','1350']);
type Place='owners'|'ncl'|'cl'|'nca'|'ca';
/** Where a balance-sheet ledger sits (contract §2, rulebook §7.5): codes first, then the ledger's primary Tally group. */
function placeOf(s:State,code:string):Place{
  if(code==='3000'||code==='3100')return 'owners';
  if(code==='2400'||code==='2410')return 'ncl';
  if(inRange(code,'1500','1599'))return 'nca';
  if(['2000','2200','2300','2340'].includes(code))return 'cl';
  switch(accPlRoot(s,code).id){case 'capital':return 'owners';case 'loans':return 'ncl';case 'current-liabilities':return 'cl';
    case 'fixed-assets':case 'investments':return 'nca';default:return 'ca';}
}
/** Balance sheet as of `asOf`: every voucher up to `asOf` (opening entry and locked snapshots included). Owner's funds = 3100 + current-year
 *  profit (accPl from 1 April to asOf) − 3000; Duties & Taxes (2100–2150, 2310–2330 net of 1300–1330, 1350) netted — net Dr → current asset
 *  "GST input credit"; 1340 GST electronic cash ledger stays a current asset (rulebook §8.5); fixed assets at their ledger balance = WDV
 *  (depreciation is credited to the blocks, rulebook F2). A ledger whose balance runs against its side (a bank overdraft, a vendor advance)
 *  moves to the other side's current block. 9999 / 1400 not zero → provisional. */
export function accBalanceSheet(s:State,asOf:string):ACCBalanceSheet{
  const vs=accVouchersUpTo(s,asOf);
  const bal=new Map<string,number>();
  for(const v of vs)for(const e of v.entries){const c=accRptControl(e.account);bal.set(c,(bal.get(c)??0)+e.debit-e.credit);}
  const pl=accPl(s,accPlFyStart(asOf),asOf);
  const blocks:Record<Place,ACCBsBlock>={owners:{name:"Owner's funds",lines:[],total:0},ncl:{name:'Non-current liabilities',lines:[],total:0},
    cl:{name:'Current liabilities',lines:[],total:0},nca:{name:'Non-current assets',lines:[],total:0},ca:{name:'Current assets',lines:[],total:0}};
  const liab=(p:Place)=>p==='owners'||p==='ncl'||p==='cl';
  let plLedgers=0,duties=0;const dutyLines:{code:string;name:string;paise:number}[]=[];
  for(const [code,b] of [...bal].sort((x,y)=>x[0]<y[0]?-1:x[0]>y[0]?1:0)){
    if(!b)continue;
    if(accPlRoot(s,code).side==='PL'||/^[45]/.test(code)){plLedgers+=b;continue;}
    const name=accRptLedgerName(s,code);
    if(DUTIES.has(code)){duties+=b;dutyLines.push({code,name,paise:-b});continue;}
    let p=placeOf(s,code),paise=liab(p)?-b:b;
    if(paise<0&&p!=='owners'){p=liab(p)?'ca':'cl';paise=-paise;}
    blocks[p].lines.push({code,name,paise});
  }
  if(duties<0)blocks.cl.lines.push(...dutyLines);                 // net Cr: payable, each ledger shown
  else if(duties>0)blocks.ca.lines.push({code:'1300',name:'GST input credit',paise:duties});
  blocks.owners.lines.push({code:'PL',name:'Profit for the year to date',paise:pl.profitBeforeTax});
  const earlier=-plLedgers-pl.profitBeforeTax;                     // P&L balances of earlier years not yet closed to capital
  if(earlier)blocks.owners.lines.push({code:'PL-EARLIER',name:'Profit & loss of earlier years (not closed)',paise:earlier});
  for(const k of Object.keys(blocks) as Place[]){blocks[k].lines.sort(byCode);blocks[k].total=blocks[k].lines.reduce((a,l)=>a+l.paise,0);}
  const lt=blocks.owners.total+blocks.ncl.total+blocks.cl.total,at=blocks.nca.total+blocks.ca.total;
  const suspense=bal.get('9999')??0,openingDifference=bal.get('1400')??0;
  return {asOf,provisional:accRptOpeningPending(s)||suspense!==0||openingDifference!==0||mixedLocks(s,vs),
    liabilities:{ownersFunds:blocks.owners,nonCurrent:blocks.ncl,current:blocks.cl,total:lt},
    assets:{nonCurrent:blocks.nca,current:blocks.ca,total:at},balanced:lt===at,suspense,openingDifference};
}

// ── 7.9 Cash-ratio report ──
const CASH=new Set(['1000','1005']);
const MONEY=new Set(['1000','1005','1010','1020','1030']);
const isMoney=(s:State,code:string)=>{if(MONEY.has(code))return true;const g=accLedger(s,code)?.group;return g==='cash'||g==='bank';};
/** Cash receipts ÷ total receipts and cash payments ÷ total payments over the FY `2026-27` (Receipt / Payment vouchers; the money side:
 *  Dr on cash / bank ledgers of a receipt, Cr of a payment; cash = 1000 / 1005). Percentages in basis points; flagged above 500 (5 %). */
export function accCashRatio(s:State,fy:string):ACCCashRatio{
  const y=+fy.slice(0,4),from=`${y}-04-01`,to=`${y+1}-03-31`;
  const {period}=accRptVouchers(s,from,to);
  let cr=0,tr=0,cp=0,tp=0;
  for(const v of period){
    if(v.type!=='Receipt'&&v.type!=='Payment')continue;
    for(const e of v.entries){const c=accRptControl(e.account);if(!isMoney(s,c))continue;
      if(v.type==='Receipt'){tr+=e.debit;if(CASH.has(c))cr+=e.debit;}else{tp+=e.credit;if(CASH.has(c))cp+=e.credit;}}
  }
  const bps=(a:number,b:number)=>b?Math.round(a*10000/b):0;
  const receiptsPct=bps(cr,tr),paymentsPct=bps(cp,tp);
  return {fy,cashReceipts:cr,totalReceipts:tr,cashPayments:cp,totalPayments:tp,receiptsPct,paymentsPct,
    flagged:receiptsPct>ACC_CASH_RATIO_LIMIT_BPS||paymentsPct>ACC_CASH_RATIO_LIMIT_BPS};
}
