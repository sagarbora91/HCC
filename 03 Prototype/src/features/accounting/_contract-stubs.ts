// ACC-1 · acc1-ui — TEMPORARY stand-ins for the selectors and commands the other ACC-1 lanes build (posting contract §2, §3, §5, §6, §7).
// Every screen of this folder imports the contract names from HERE ONLY. The integrator deletes this file and re-points the imports in one
// step (`from './_contract-stubs'` → `from '../../services/mock'`; the command types below are the shapes the screens send).
// The functions return plausible shapes derived from the existing ACC_CHART / ACC_GROUPS / vouchers so the screens render; they are not
// the accounting rules (chart, parties, opening, statements lanes own those).
import {accSlice,accDates,accVouchers,accVouchersUpTo,accLedgerName,accAccount,accLegacyGroup,ACC_CHART,ACC_GROUPS,ACC_SUSPENSE,mnyAccounts,mnyVendors,grpSlice,chnSlice,hrSlice,addDays,
  type State,type ACCLedger,type ACCGroupDef,type ACCOpening,type ACCOpeningLine,type ACCVoucher,type ACCVoucherType,type ACCChartChange} from '../../services/mock';
import {csvMinimal} from '../../domain/csv';

// ── Contract §2 / §1: chart and groups ──
const GROUP_OF_LEGACY:Record<string,string>={Asset:'current-assets',Liability:'current-liabilities',Income:'sales',Expense:'indirect-expenses',Suspense:'suspense'};
const CONTROLS=new Set(['1000','1030','1110','1115','1120','1200','2200','2400']);
/** Contract name `accChart(s)`: built-ins + Owner overrides + Owner-added ledgers. */
export function accChart(s:State):ACCLedger[]{
  const stored=accSlice(s).chart?.ledgers??[];const over=new Map(stored.map(l=>[l.code,l]));
  const base:ACCLedger[]=ACC_CHART.map(a=>over.get(a.code)??{code:a.code,name:a.name,group:GROUP_OF_LEGACY[a.group],system:true,control:CONTROLS.has(a.code)||undefined,active:true,note:a.note});
  return [...base,...stored.filter(l=>!ACC_CHART.some(a=>a.code===l.code))].sort((a,b)=>a.code<b.code?-1:a.code>b.code?1:0);
}
/** Contract name `accGroups(s)`: the 28 Tally groups + the Owner's custom sub-groups. */
export const accGroups=(s:State):ACCGroupDef[]=>[...ACC_GROUPS,...(accSlice(s).chart?.groups??[])];
/** The history rows of the chart (newest first). Contract §8: `chart.history`. */
export const accChartHistory=(s:State):ACCChartChange[]=>[...(accSlice(s).chart?.history??[])].reverse();
export const accLegacyOfGroup=(g:ACCGroupDef)=>accLegacyGroup(g);

// ── Contract §3: party sub-ledgers ──
export type ACCPartyLedger={id:string;control:string;partyId:string;name:string;active:boolean};
/** Contract name `accPartyLedgers(s)`: `{id:'<control>/<party>', control, partyId, name, active}[]`, derived from the masters. */
export function accPartyLedgers(s:State):ACCPartyLedger[]{
  const out:ACCPartyLedger[]=[];const add=(control:string,partyId:string,name:string,active=true)=>out.push({id:control+'/'+partyId,control,partyId,name,active});
  mnyAccounts(s).forEach(a=>add(a.kind==='till'?'1000':'1030',a.code,a.name,a.active));
  grpSlice(s).companies.forEach(c=>add('1110',c.id,c.name));
  chnSlice(s).channels.forEach(c=>add('1115',c.id,c.name));
  hrSlice(s).staff.forEach(p=>add('1200',p.id,p.name));
  mnyVendors(s).forEach(v=>add('2200',v.code,v.name,v.active));
  return out;
}

// ── Contract §5: opening balances ──
/** Contract name `accOpening(s)`: the stored opening entry (undefined when none was saved). The UI adds the 1400 difference line itself for display. */
export const accOpening=(s:State):ACCOpening|undefined=>accSlice(s).opening;
/** Contract name `accOpeningPending(s)`: true while there is no approved opening entry or 1400 ≠ 0 (the first lock is refused). */
export function accOpeningPending(s:State):boolean{
  const o=accSlice(s).opening;if(!o||o.status!=='approved')return true;
  const net=o.lines.filter(l=>l.account!=='1400').reduce((a,l)=>a+l.debit-l.credit,0);return net!==0;
}
export type AccOpeningSave={type:'ACC_OPENING_SAVE';asOf:string;lines:ACCOpeningLine[];note?:string};
export type AccOpeningApprove={type:'ACC_OPENING_APPROVE'};
export type AccOpeningReopen={type:'ACC_OPENING_REOPEN';reason:string};

// ── Contract §2 commands (acc1-chart) ──
export type AccLedgerUpsert={type:'ACC_LEDGER_UPSERT';code:string;name:string;group:string;control?:boolean;gst?:'none'|'input'|'output';tallyName?:string;note?:string};
export type AccLedgerRetire={type:'ACC_LEDGER_RETIRE';code:string;active:boolean;reason?:string};
export type AccGroupUpsert={type:'ACC_GROUP_UPSERT';id?:string;name:string;parent:string};

// ── Contract §6: statements, six-column trial balance, day book (acc1-statements) ──
export type ACCStatementRow={date:string;no:string;type:ACCVoucherType;narration:string;counter:string;debit:number;credit:number;running:number};
export type ACCStatement={account:string;name:string;from:string;to:string;opening:number;debit:number;credit:number;closing:number;rows:ACCStatementRow[]};
export type ACCTB6Row={account:string;name:string;group:string;control?:string;openingDebit:number;openingCredit:number;debit:number;credit:number;closingDebit:number;closingCredit:number};
export type ACCTB6={from:string;to:string;detail:boolean;rows:ACCTB6Row[];totals:Omit<ACCTB6Row,'account'|'name'|'group'|'control'>;balanced:boolean;openingPending:boolean};
/** The contract gives no row shape for the day book ("one row per entry"): this is the one the screens read. */
export type ACCDayBookRow={date:string;no:string;type:ACCVoucherType;narration:string;account:string;name:string;debit:number;credit:number};

const controlOf=(id:string)=>id.split('/')[0];
function nameOf(s:State,id:string,parties:Map<string,string>){return parties.get(id)??accLedgerName(controlOf(id))??id;}
const partyNames=(s:State)=>new Map(accPartyLedgers(s).map(p=>[p.id,p.name]));
function vouchersBetween(s:State,from:string,to:string):ACCVoucher[]{return accDates(s).filter(d=>d>=from&&d<=to).sort().flatMap(d=>accVouchers(s,d));}
const before=(s:State,from:string)=>accVouchersUpTo(s,addDays(from,-1)).filter(v=>v.date<from);

/** Contract name `accStatement(s, account, from, to)`. A control code sums its parties; a party id (`1110/CMP-0007`) is exact. */
export function accStatement(s:State,account:string,from:string,to:string):ACCStatement{
  const parties=partyNames(s),hit=(a:string)=>a===account||(!account.includes('/')&&controlOf(a)===account);
  const opening=before(s,from).flatMap(v=>v.entries).filter(e=>hit(e.account)).reduce((a,e)=>a+e.debit-e.credit,0);
  let running=opening,debit=0,credit=0;const rows:ACCStatementRow[]=[];
  vouchersBetween(s,from,to).sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:a.no<b.no?-1:1).forEach(v=>{
    const mine=v.entries.filter(e=>hit(e.account));if(!mine.length)return;
    const counter=[...new Set(v.entries.filter(e=>!hit(e.account)).map(e=>nameOf(s,e.account,parties)))].join(', ');
    mine.forEach(e=>{running+=e.debit-e.credit;debit+=e.debit;credit+=e.credit;rows.push({date:v.date,no:v.no,type:v.type,narration:v.narration,counter,debit:e.debit,credit:e.credit,running});});});
  return {account,name:nameOf(s,account,parties),from,to,opening,debit,credit,closing:running,rows};
}
/** Contract name `accTrialBalance6(s, from, to, detail)`. */
export function accTrialBalance6(s:State,from:string,to:string,detail:boolean):ACCTB6{
  const parties=partyNames(s),chart=new Map(accChart(s).map(l=>[l.code,l]));const m=new Map<string,{o:number;d:number;c:number}>();
  const key=(a:string)=>detail?a:controlOf(a),get=(a:string)=>{const k=key(a);let r=m.get(k);if(!r){r={o:0,d:0,c:0};m.set(k,r);}return r;};
  before(s,from).forEach(v=>v.entries.forEach(e=>{get(e.account).o+=e.debit-e.credit;}));
  vouchersBetween(s,from,to).forEach(v=>v.entries.forEach(e=>{const r=get(e.account);r.d+=e.debit;r.c+=e.credit;}));
  const rows:ACCTB6Row[]=[...m.entries()].sort((a,b)=>a[0]<b[0]?-1:1).map(([account,r])=>{const close=r.o+r.d-r.c;
    return {account,name:nameOf(s,account,parties),group:chart.get(controlOf(account))?.group??GROUP_OF_LEGACY[accAccount(controlOf(account))?.group??'Suspense']??'suspense',control:account.includes('/')?controlOf(account):undefined,
      openingDebit:r.o>0?r.o:0,openingCredit:r.o<0?-r.o:0,debit:r.d,credit:r.c,closingDebit:close>0?close:0,closingCredit:close<0?-close:0};});
  const sum=(k:keyof Omit<ACCTB6Row,'account'|'name'|'group'|'control'>)=>rows.reduce((a,r)=>a+r[k],0);
  const totals={openingDebit:sum('openingDebit'),openingCredit:sum('openingCredit'),debit:sum('debit'),credit:sum('credit'),closingDebit:sum('closingDebit'),closingCredit:sum('closingCredit')};
  return {from,to,detail,rows,totals,balanced:totals.openingDebit===totals.openingCredit&&totals.debit===totals.credit&&totals.closingDebit===totals.closingCredit,openingPending:accOpeningPending(s)};
}
/** Contract name `accDayBook(s, from, to)`: every voucher of the range in `date, no` order, one row per entry. */
export function accDayBook(s:State,from:string,to:string):ACCDayBookRow[]{
  const parties=partyNames(s);
  return vouchersBetween(s,from,to).sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:a.no<b.no?-1:1)
    .flatMap(v=>v.entries.map(e=>({date:v.date,no:v.no,type:v.type,narration:v.narration,account:e.account,name:nameOf(s,e.account,parties),debit:e.debit,credit:e.credit})));
}

// CSV builders (the statements lane owns the real ones; names are the screens' assumption)
const rup=(p:number)=>(p/100).toFixed(2),drcr=(n:number)=>n===0?'':rup(Math.abs(n))+(n>0?' Dr':' Cr');
const toCsv=(rows:(string|number)[][])=>rows.map(r=>r.map(csvMinimal).join(',')).join('\r\n');
export const accStatementCsv=(st:ACCStatement)=>toCsv([['Date','Voucher No','Type','Narration','Counter ledger','Debit','Credit','Balance'],
  ['','','','Opening balance','','','',drcr(st.opening)],...st.rows.map(r=>[r.date,r.no,r.type,r.narration,r.counter,r.debit?rup(r.debit):'',r.credit?rup(r.credit):'',drcr(r.running)]),
  ['','','','Closing balance','',rup(st.debit),rup(st.credit),drcr(st.closing)]]);
export const accTrialBalance6Csv=(tb:ACCTB6)=>toCsv([['Code','Ledger','Group','Opening Dr','Opening Cr','Period Dr','Period Cr','Closing Dr','Closing Cr'],
  ...tb.rows.map(r=>[r.account,r.name,r.group,rup(r.openingDebit),rup(r.openingCredit),rup(r.debit),rup(r.credit),rup(r.closingDebit),rup(r.closingCredit)]),
  ['','Total','',rup(tb.totals.openingDebit),rup(tb.totals.openingCredit),rup(tb.totals.debit),rup(tb.totals.credit),rup(tb.totals.closingDebit),rup(tb.totals.closingCredit)]]);
export const accDayBookCsv=(rows:ACCDayBookRow[])=>toCsv([['Date','Voucher No','Type','Narration','Code','Ledger','Debit','Credit'],
  ...rows.map(r=>[r.date,r.no,r.type,r.narration,r.account,r.name,r.debit?rup(r.debit):'',r.credit?rup(r.credit):''])]);

export {ACC_SUSPENSE};
