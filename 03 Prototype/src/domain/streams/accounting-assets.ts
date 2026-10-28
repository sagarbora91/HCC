// ACC-2 · lane acc2-purchase — the fixed-asset register (02 Planning/agents/ACC2-POSTING-CONTRACT.md §3.4; rulebook §5 C10, §F F1; DEC-138 /
// DEC-140: HCC keeps the register, the CA passes depreciation in Tally — no depreciation journal here). Exported names start with 'acc' / 'ACC'.
//
// A row is created when a capital purchase (category flag `capital`, ledger in the Fixed Assets group 15x0) is approved:
// `s.accountingX.assets[]` {id, no FA-<yyyy>-<nnn>, ledger, name, cost, date, vendor, expense, itc}. Cost = the bill less the ITC taken
// (the voucher debits the block with exactly that). A reversal of the purchase marks the row `disposed` with amount 0 (never deleted).
import type {State,Expense} from '../types';
import {csvMinimal} from '../csv';
import {accSlice,type ACCAsset} from './accounting';
import {accLedger,accGroupRoot} from './accounting-chart';
import {expPurchaseLines} from './expenses-vendor';

export const accAssets=(s:State):ACCAsset[]=>accSlice(s).assets??[];
/** True when the ledger (built-in or Owner-added, a custom sub-group counts) belongs to Tally's Fixed Assets group. */
export function accIsFixedAssetLedger(s:State,code:string|undefined):boolean{
  const l=code?accLedger(s,code):undefined;return !!l&&accGroupRoot(s,l.group)?.id==='fixed-assets';
}
/** Next register number for a purchase dated `date`: FA-<yyyy>-<nnn>, one series per calendar year of the purchase. */
export function accAssetNextNo(s:State,date:string):string{
  const y=String(date).slice(0,4),pre=`FA-${y}-`;
  const n=accAssets(s).filter(a=>a.no.startsWith(pre)).reduce((m,a)=>Math.max(m,Number(a.no.slice(pre.length))||0),0)+1;
  return pre+String(n).padStart(3,'0');
}
/** The register row a posted capital purchase creates (`ledger` = the block its category maps to). */
export function accAssetFromExpense(s:State,e:Expense,id:string,ledger:string):ACCAsset{
  const itc=expPurchaseLines(e).itcTotal;
  return {id,no:accAssetNextNo(s,e.date),ledger,name:e.description||e.category,cost:e.amount-itc,date:e.date,vendor:e.vendor,expense:e.id,itc};
}
export type ACCAssetRow=ACCAsset&{ledgerName:string;status:'In use'|'Not yet put to use'|'Disposed'};
/** The register for the screen: newest first, with the block name and a status. */
export function accAssetRegister(s:State):ACCAssetRow[]{
  return accAssets(s).map(a=>({...a,ledgerName:accLedger(s,a.ledger)?.name??a.ledger,status:(a.disposed?'Disposed':a.putToUse?'In use':'Not yet put to use') as ACCAssetRow['status']}))
    .sort((a,b)=>a.date<b.date?1:a.date>b.date?-1:a.no<b.no?1:-1);
}
/** Σ cost per block (ledger) of the rows not disposed. */
export function accAssetBlocks(s:State):{ledger:string;name:string;cost:number;count:number}[]{
  const m=new Map<string,{ledger:string;name:string;cost:number;count:number}>();
  for(const a of accAssets(s)){if(a.disposed)continue;const x=m.get(a.ledger)??{ledger:a.ledger,name:accLedger(s,a.ledger)?.name??a.ledger,cost:0,count:0};x.cost+=a.cost;x.count++;m.set(a.ledger,x);}
  return [...m.values()].sort((a,b)=>a.ledger<b.ledger?-1:1);
}
export const ACC_ASSET_CSV_HEADER=['Asset No','Block','Ledger','Asset','Cost','ITC','Date','Put to use','Vendor','Bill','Status','Disposed on','Disposal amount','Disposal voucher'];
const r=(p:number)=>(p/100).toFixed(2);
/** The register as CSV (rupees, 2 decimals, CRLF; formula-guarded cells as every HCC CSV). */
export function accAssetsCsv(s:State):string{
  const rows:(string|number)[][]=[ACC_ASSET_CSV_HEADER];
  for(const a of accAssetRegister(s))rows.push([a.no,a.ledgerName,a.ledger,a.name,r(a.cost),r(a.itc),a.date,a.putToUse??'',a.vendor,a.expense,a.status,a.disposed?.date??'',a.disposed?r(a.disposed.amount):'',a.disposed?.voucher??'']);
  return rows.map(x=>x.map(c=>csvMinimal(c)).join(',')).join('\r\n');
}
