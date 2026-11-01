// ACC-2 · lane acc2-purchase — the fixed-asset register (02 Planning/agents/ACC2-POSTING-CONTRACT.md §3.4; rulebook §5 C10, §F F1; DEC-138 /
// DEC-140: HCC keeps the register, the CA passes depreciation in Tally — no depreciation journal here). Exported names start with 'acc' / 'ACC'.
//
// A row is created when a capital purchase (category flag `capital`, ledger in the Fixed Assets group 15x0) is approved:
// `s.accountingX.assets[]` {id, no FA-<yyyy>-<nnn>, ledger, name, cost, date, vendor, expense, itc}. Cost = the bill less the ITC taken
// (the voucher debits the block with exactly that). A reversal of the purchase marks the row `disposed` with amount 0 (never deleted).
import type {State,Expense} from '../types';
import {csvMinimal} from '../csv';
import {accSlice,accLedgerBalanceMap,type ACCAsset} from './accounting';
import {accLedger,accGroupRoot} from './accounting-chart';
import {expPurchaseLines,expIsReturn,expReturnOf} from './expenses-vendor';

/** ACC-3 C2 (contract §7): Σ cost reduction of the posted returns against a capital purchase (each return net of the ITC it reverses; ≥ 0). */
export function accAssetReturned(s:State,expenseId:string):number{
  let t=0;for(const e of s.expenses)if(expIsReturn(e)&&expReturnOf(e)===expenseId&&e.status==='Posted'&&!e.reversalOf)t+=-(e.amount-expPurchaseLines(e).itcTotal);
  return Math.max(0,t);
}
/** The register rows; `cost` is net of the posted returns against the purchase (ACC-3 C2: a return reduces the asset's cost; `returned` says by how much). */
export const accAssets=(s:State):ACCAsset[]=>(accSlice(s).assets??[]).map(a=>{const r=accAssetReturned(s,a.expense);return r?{...a,cost:Math.max(0,a.cost-r),returned:r}:a;});
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

// ── ACC-3 C1 (contract §7; rulebook F2, F4): put to use, disposal, the depreciation factor and the block WDV ──
export const ACC_ASSET_ROLES=['Owner','Accountant']; // PERM-ACC-004 (contract §9)
/** Last day of the Indian financial year (31 March) that contains `date`. */
export const accFyEnd=(date:string)=>{const y=+date.slice(0,4),m=+date.slice(5,7);return `${m>=4?y+1:y}-03-31`;};
const dayNo=(d:string)=>Math.round(Date.parse(d+'T00:00:00Z')/86400000);
/** Rulebook F2: the share of the year's rate an asset earns in the FY ending `fyEnd` — 1 (put to use ≥ 180 days in that year, or in an earlier
 *  year), ½ (fewer than 180 days), 0 (not yet put to use by then, or disposed before the year began). Depreciation counts from the put-to-use date. */
export function accAssetDepreciationFactor(a:Pick<ACCAsset,'putToUse'|'disposed'>,fyEnd:string):0|0.5|1{
  if(!a.putToUse||a.putToUse>fyEnd)return 0;
  const fyStart=`${+fyEnd.slice(0,4)-1}-04-01`;
  if(a.disposed&&a.disposed.date<fyStart)return 0;
  if(a.putToUse<fyStart)return 1;
  return dayNo(fyEnd)-dayNo(a.putToUse)+1>=180?1:0.5;
}
/** Why `ACC_ASSET_PUT_TO_USE` cannot record `date` on the asset ('' = it can). */
export function accAssetPutToUseRefusal(s:State,a:ACCAsset|undefined,date:string):string{
  if(!a)return 'Asset not found.';
  if(a.disposed)return `${a.no} is disposed.`;
  if(!/^\d{4}-\d{2}-\d{2}$/.test(date))return 'Enter the date the asset was first used (YYYY-MM-DD).';
  if(date<a.date)return `The put-to-use date cannot be before the purchase (${a.date}).`;
  if(date>s.date)return 'The put-to-use date cannot be after the business date.';
  return '';
}
/** Block value (paise) per fixed-asset ledger on `asOf` from the books (opening entry + purchases − returns − disposal proceeds; depreciation
 *  is passed by the CA in Tally at year end, DEC-138 / DEC-140, so the book value IS the written-down value HCC knows). Blocks with no balance are left out. */
export function accAssetWdv(s:State,asOf=s.date):{ledger:string;name:string;wdv:number;count:number}[]{
  const m=accLedgerBalanceMap(s,asOf),out:{ledger:string;name:string;wdv:number;count:number}[]=[];
  for(const [code,bal] of m){if(code.includes('/')||!bal||!accIsFixedAssetLedger(s,code))continue;
    out.push({ledger:code,name:accLedger(s,code)?.name??code,wdv:bal,count:accAssets(s).filter(a=>a.ledger===code&&a.date<=asOf&&!(a.disposed&&a.disposed.date<=asOf)).length});}
  return out.sort((a,b)=>a.ledger<b.ledger?-1:1);
}
export const accAssetWdvTotal=(s:State,asOf=s.date)=>accAssetWdv(s,asOf).reduce((a,b)=>a+b.wdv,0);
/** Rulebook F4 (rules the contract's "profit / loss to 4960 / 5400" — see ACC3-CARRY-DONE.md): a sale credits the block with the sale value,
 *  Receipt Dr tender · Cr block; a scrap with nothing received posts nothing. When the block goes negative or holds no other asset, a flag
 *  "short-term capital gain / loss — CA" is raised instead of an automatic entry (block-of-assets rule, IT Act 2025 s.33 / 34 — VERIFY). */
export function accAssetDisposalFlag(s:State,a:ACCAsset,proceeds:number,date:string):string{
  const bal=accLedgerBalanceMap(s,date).get(a.ledger)??0,after=bal-proceeds,others=accAssets(s).filter(x=>x.ledger===a.ledger&&x.id!==a.id&&!x.disposed).length;
  if(after<0)return `Block ${a.ledger} goes negative (${(after/100).toFixed(2)}) — short-term capital gain — CA (rulebook F4).`;
  if(!others)return `Block ${a.ledger} has no asset left — short-term capital loss / gain — CA (rulebook F4).`;
  return '';
}
