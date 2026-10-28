// ACC-2 · TDS deducted by the hotel (C9, DEC-146) and the TDS a company deducts from the hotel's bill (A9) — the dated TDS-rate table.
// Contract: 02 Planning/agents/ACC2-POSTING-CONTRACT.md §4 (A9), §5. Rulebook: HCC_Accounting_Rulebook_v1.md §2.9, §5 A9 / C9 / E5 / E6.
// Mirrors domain/gst.ts: every rate / threshold is a dated value under a `tds.*` key; tdsValueOn() reads the value in force on a business
// date from the Master configuration change log (s.configX.changes — the Owner edits it in Master configuration › Money and tax, DEC-058;
// the provider is registered by domain/streams/accounting-tds.ts) and falls back to the seed versions below. The server twin is
// Hcc.Domain/Accounting/TdsRates.cs + TdsCalculator.cs (same keys, same figures, same rounding).
//
// SEED — every row "CA to confirm" (rulebook §9 OP-10: the Income-tax Act 2025 section rows are VERIFY items; stored as text, not logic):
//   rent          s.393(1) Sl. 2(ii) (old 194-I): ₹50,000 per month or part of a month; 10 % land / building / furniture, 2 % plant / machinery.
//   contractor    s.393(1) (old 194C): ₹30,000 a single bill or ₹1,00,000 in the financial year; 1 % individual / HUF, 2 % others.
//   professional  s.393(1) (old 194J): ₹50,000 in the financial year; 10 % professional fees, 2 % fees for technical services.
//   commission    s.393(1) (old 194H): ₹20,000 in the financial year; 2 %.
// Sources: LAW S-13 (Income-tax Act 2025 s.393 table; Finance Act 2025 thresholds), S-17 (Circular 23/2017: TDS on the amount
// excluding GST). Rates and thresholds are money settings, Owner-only (DEC-058).
//
// Pure functions over the state; imports only the GST helpers (FY, rounding) and the vendor master — never accounting.ts, so the
// purchase lane's expense posting may import tdsForBill without a cycle.
import type {State,Expense} from './types';
import type {CMPProfile} from './streams/compliance';
import {gstFy,gstLineTax} from './gst';
import {mnyVendor} from './streams/money-master';

export const TDS_CA_NOTE='CA to confirm';
export const TDS_ILLUSTRATIVE='Illustrative — CA to confirm';
/** Mapping keys (contract §5 / §4 A9): the hotel's TDS payable (Cr on a bill) and the TDS a company deducted from the hotel (Dr on its payment). */
export const ACC_TDS_PAYABLE_KEY='tax:tds-payable';
export const ACC_TDS_RECEIVABLE_KEY='tax:tds-receivable';
/** Default ledgers of the two keys (rulebook §3.2: 2150 TDS payable, 1360 TDS receivable). */
export const TDS_LEDGER={payable:'2150',receivable:'1360'} as const;

export type TdsSectionId='rent'|'contractor'|'professional'|'commission';
/** A deductee's rate column (contract §4: the firm profile's `tdsDeductorType`). */
export type TdsColumn='individual'|'other';
/** How the threshold is tested: per calendar month (rent), per single bill and per financial year (contractor), per financial year. */
export type TdsBasis='month'|'single+year'|'year';
export type TdsSectionDef={id:TdsSectionId;label:string;section:string;old:string;basis:TdsBasis;source:string;note:string;
  /** Variant rate fields (vendor tdsSection `<id>:<variant>`). */ variants?:{id:string;label:string;field:string}[]};
export const TDS_SECTIONS:TdsSectionDef[]=[
  {id:'rent',label:'Rent of premises',section:'s.393(1) Sl. 2(ii)',old:'194-I',basis:'month',
    source:'LAW S-13 (Income-tax Act 2025 s.393 table, old 194-I; Finance Act 2025: ₹50,000 per month or part of a month) — VERIFY',note:TDS_CA_NOTE,
    variants:[{id:'plant',label:'Rent of plant, machinery or equipment',field:'bpsPlant'}]},
  {id:'contractor',label:'Payments to contractors',section:'s.393(1)',old:'194C',basis:'single+year',
    source:'LAW S-13 (Income-tax Act 2025 s.393 table, old 194C: ₹30,000 single / ₹1,00,000 a year) — VERIFY',note:TDS_CA_NOTE},
  {id:'professional',label:'Professional or technical fees',section:'s.393(1)',old:'194J',basis:'year',
    source:'LAW S-13 (Income-tax Act 2025 s.393 table, old 194J; Finance Act 2025: ₹50,000 a year) — VERIFY',note:TDS_CA_NOTE,
    variants:[{id:'technical',label:'Fees for technical services',field:'bpsTechnical'}]},
  {id:'commission',label:'Commission or brokerage',section:'s.393(1)',old:'194H',basis:'year',
    source:'LAW S-13 (Income-tax Act 2025 s.393 table, old 194H; Finance Act 2025: ₹20,000 a year) — VERIFY',note:TDS_CA_NOTE},
];
export const tdsSectionDef=(id:string)=>TDS_SECTIONS.find(d=>d.id===id);
/** The vendor master's choices for `tdsSection` (contract §3): a row, or `<row>:<variant>` for its second rate. */
export const TDS_SECTION_OPTIONS:{id:string;label:string}[]=TDS_SECTIONS.flatMap(d=>[{id:d.id,label:`${d.label} (${d.section}, old ${d.old})`},
  ...(d.variants??[]).map(v=>({id:`${d.id}:${v.id}`,label:`${v.label} (${d.section}, old ${d.old})`}))]);
/** Base row of a vendor's tdsSection ('rent:plant' → 'rent'); undefined for an unknown value. */
export function tdsBaseSection(code:string|undefined):TdsSectionId|undefined{
  const [id,variant]=String(code??'').split(':');const d=tdsSectionDef(id);
  if(!d||(variant!==undefined&&!(d.variants??[]).some(v=>v.id===variant)))return undefined;return d.id;
}

export type TdsKey=`tds.${TdsSectionId}.${'threshold'|'thresholdYear'|'bpsIndividual'|'bpsOther'|'bpsPlant'|'bpsTechnical'}`;
type SeedVersion={from:string;value:number};
export type TdsSeedDef={key:TdsKey;section:TdsSectionId;label:string;type:'percent'|'money';unit:string;help:string;versions:SeedVersion[]};
const FROM='2026-04-01'; // Income-tax Act 2025 in force; the first version also covers earlier dates (same figures under the 1961 Act from 1 Apr 2025)
const R=(key:TdsKey,section:TdsSectionId,label:string,type:'percent'|'money',unit:string,help:string,value:number):TdsSeedDef=>({key,section,label,type,unit,help,versions:[{from:FROM,value}]});
/** Seed versions (oldest first; the first version applies to every earlier date). Money in paise, rates in basis points. */
export const TDS_SEED:TdsSeedDef[]=[
  R('tds.rent.threshold','rent','TDS · rent · threshold per month','money','₹ per payee per month','Rent to one payee above this in a calendar month (or part of it) is subject to TDS. s.393(1) Sl. 2(ii), old 194-I.',5000000),
  R('tds.rent.bpsIndividual','rent','TDS · rent of land / building · individual / HUF payee','percent','% of the rent (GST excluded)','10 % on rent of land, building or furniture.',1000),
  R('tds.rent.bpsOther','rent','TDS · rent of land / building · other payee','percent','% of the rent (GST excluded)','10 % on rent of land, building or furniture.',1000),
  R('tds.rent.bpsPlant','rent','TDS · rent of plant / machinery','percent','% of the rent (GST excluded)','2 % on rent of plant, machinery or equipment.',200),
  R('tds.contractor.threshold','contractor','TDS · contractor · single bill threshold','money','₹ per bill','A single contract bill above this is subject to TDS. s.393(1), old 194C.',3000000),
  R('tds.contractor.thresholdYear','contractor','TDS · contractor · financial-year threshold','money','₹ per payee per financial year','Bills to one contractor above this in the financial year are subject to TDS (the earlier bills of the year included).',10000000),
  R('tds.contractor.bpsIndividual','contractor','TDS · contractor · individual / HUF column','percent','% of the bill (GST excluded)','1 % for an individual or HUF.',100),
  R('tds.contractor.bpsOther','contractor','TDS · contractor · other column','percent','% of the bill (GST excluded)','2 % for others (firm, company, LLP …).',200),
  R('tds.professional.threshold','professional','TDS · professional fees · financial-year threshold','money','₹ per payee per financial year','Fees to one payee above this in the financial year are subject to TDS. s.393(1), old 194J.',5000000),
  R('tds.professional.bpsIndividual','professional','TDS · professional fees · individual / HUF payee','percent','% of the fees (GST excluded)','10 % on professional fees.',1000),
  R('tds.professional.bpsOther','professional','TDS · professional fees · other payee','percent','% of the fees (GST excluded)','10 % on professional fees.',1000),
  R('tds.professional.bpsTechnical','professional','TDS · fees for technical services','percent','% of the fees (GST excluded)','2 % on fees for technical services.',200),
  R('tds.commission.threshold','commission','TDS · commission · financial-year threshold','money','₹ per payee per financial year','Commission to one payee above this in the financial year is subject to TDS. s.393(1), old 194H.',2000000),
  R('tds.commission.bpsIndividual','commission','TDS · commission · individual / HUF payee','percent','% of the commission (GST excluded)','2 % on commission or brokerage.',200),
  R('tds.commission.bpsOther','commission','TDS · commission · other payee','percent','% of the commission (GST excluded)','2 % on commission or brokerage.',200),
];
export const TDS_KEYS=TDS_SEED.map(d=>d.key);
export const isTdsKey=(key:unknown)=>typeof key==='string'&&key.startsWith('tds.');
export const tdsSeedDef=(key:string)=>TDS_SEED.find(d=>d.key===key);
/** Seed value in force on a date (statutory figures built into the prototype). */
export function tdsSeedOn(key:TdsKey,date:string):number{
  const d=tdsSeedDef(key);if(!d)throw new Error('Unknown TDS key '+key);
  let v=d.versions[0].value;for(const x of d.versions)if(x.from<=date)v=x.value;return v;
}
/** TDS table value in force on a business date: the latest non-withdrawn Master configuration change with effectiveFrom ≤ date, else the seed
 *  (same walk as gstValueOn). */
export function tdsValueOn(s:State,key:TdsKey,date:string):number{
  const log=s.configX?.changes;
  if(log?.length){
    let hit:{effectiveFrom:string;after:unknown}|undefined;
    for(let i=log.length-1;i>=0;i--){const c=log[i];
      if(c.key===key&&c.status!=='withdrawn'&&c.effectiveFrom<=date&&(!hit||c.effectiveFrom>=hit.effectiveFrom))hit=c;}
    if(hit&&Number.isFinite(Number(hit.after)))return Number(hit.after);
  }
  return tdsSeedOn(key,date);
}
/** "10 %" style label for a rate in basis points. */
export const tdsPct=(bps:number)=>(bps/100).toLocaleString('en-IN',{maximumFractionDigits:2})+' %';
const rs=(p:number)=>'₹'+(p/100).toLocaleString('en-IN',{maximumFractionDigits:2});

// ───────────────────────────── Firm-profile switches (contract §4; Owner-only) ─────────────────────────────
export type TdsSettings={hotelDeductsTds:boolean;tdsDeductorType:TdsColumn};
/** DEC-146: the hotel deducts TDS (default on); the rate column (default 'other' — the higher contractor rate, CA to confirm). */
export function tdsSettings(s:State):TdsSettings{
  const p=(s.complianceX as {profile?:CMPProfile}|undefined)?.profile;
  return {hotelDeductsTds:p?.hotelDeductsTds!==false,tdsDeductorType:p?.tdsDeductorType==='individual'?'individual':'other'};
}

// ───────────────────────────── Rates ─────────────────────────────
/** Rate (bps) of a vendor's tdsSection on a date in a column ('rent:plant' / 'professional:technical' take the variant rate). 0 = unknown. */
export function tdsRateBps(s:State,code:string,date:string,column:TdsColumn=tdsSettings(s).tdsDeductorType):number{
  const base=tdsBaseSection(code);if(!base)return 0;
  const v=String(code).split(':')[1],d=tdsSectionDef(base)!,variant=v?d.variants!.find(x=>x.id===v):undefined;
  const field=variant?variant.field:column==='individual'?'bpsIndividual':'bpsOther';
  return tdsValueOn(s,`tds.${base}.${field}` as TdsKey,date);
}
/** The table in force on a date for the TDS tab / phone view: one row per section (and variant). */
export function tdsTable(s:State,date:string){
  return TDS_SECTIONS.flatMap(d=>{
    const th=tdsValueOn(s,`tds.${d.id}.threshold` as TdsKey,date),year=d.basis==='single+year'?tdsValueOn(s,`tds.${d.id}.thresholdYear` as TdsKey,date):undefined;
    const threshold=d.basis==='month'?`${rs(th)} per month`:d.basis==='year'?`${rs(th)} a financial year`:`${rs(th)} a bill or ${rs(year!)} a financial year`;
    const row=(code:string,label:string,ind:number,oth:number)=>({code,label,section:`${d.section} (old ${d.old})`,threshold,individual:ind,other:oth,source:d.source,note:d.note});
    return [row(d.id,d.label,tdsRateBps(s,d.id,date,'individual'),tdsRateBps(s,d.id,date,'other')),
      ...(d.variants??[]).map(v=>{const b=tdsRateBps(s,`${d.id}:${v.id}`,date);return row(`${d.id}:${v.id}`,v.label,b,b);})];
  });
}

// ───────────────────────────── TDS on a bill (C9) ─────────────────────────────
export type TdsBillInput={vendor:string;amount:number;gstPaise?:number;date:string;
  /** The expense id being posted (excluded from the vendor's earlier bills when it is already stored). */ exclude?:string};
export type TdsBillResult={paise:number;section:string;taxable:number;
  /** What the rate applied to: this bill's taxable value, plus the period's earlier bills without TDS when this bill crosses the period threshold. */ base:number;
  bps:number;reason:string};
const counts=(e:Expense)=>e.status==='Posted'||e.status==='Reversed';
const norm=(v:unknown)=>String(v??'').trim().toLowerCase();
/** Taxable value of a bill: the amount less the GST on it (Circular 23/2017 — TDS is not deducted on the GST shown separately). */
export const tdsTaxable=(amount:number,gstPaise=0)=>Math.max(0,amount-Math.max(0,gstPaise));
/**
 * TDS the hotel holds back on a vendor bill (contract §5, DEC-146): 0 unless the firm profile's "hotel deducts TDS" is on and the vendor is
 * flagged `tds` with a known `tdsSection`. taxable = amount − GST; the threshold is tested on the vendor's total for the period (calendar month
 * for rent, financial year otherwise; contractor also per single bill) with this bill included; when this bill takes the total over the
 * threshold, the period's earlier bills that carried no TDS are added to the base (the deduction then covers the whole aggregate, as the
 * old 194C / 194J / 194H provisos). paise = round(base × rate), half away from zero, never more than the bill. CA to confirm.
 */
export function tdsForBill(s:State,bill:TdsBillInput):TdsBillResult{
  const none=(reason:string,section='',taxable=0):TdsBillResult=>({paise:0,section,taxable,base:0,bps:0,reason});
  const set=tdsSettings(s);
  if(!set.hotelDeductsTds)return none('The hotel does not deduct TDS (firm profile).');
  const v=bill.vendor?mnyVendor(s,bill.vendor):undefined;
  if(!v)return none('No vendor master row.');
  if(!v.tds)return none(`${v.name}: TDS not flagged on the vendor.`);
  const code=String(v.tdsSection??''),base=tdsBaseSection(code);
  if(!base)return none(`${v.name}: choose the TDS section on the vendor.`);
  const amount=Math.round(Number(bill.amount)||0),taxable=tdsTaxable(amount,Math.round(Number(bill.gstPaise)||0));
  if(taxable<=0)return none('Nothing taxable on the bill.',code,taxable);
  const d=tdsSectionDef(base)!,date=bill.date,bps=tdsRateBps(s,code,date,set.tdsDeductorType);
  const th=tdsValueOn(s,`tds.${base}.threshold` as TdsKey,date);
  const fy=gstFy(date),from=d.basis==='month'?date.slice(0,7)+'-01':fy.from,to=d.basis==='month'?date.slice(0,7)+'-31':fy.to;
  const same=(e:Expense)=>{const n=norm(e.vendor);return !!n&&(n===norm(v.name)||n===norm(v.code));};
  const earlier=s.expenses.filter(e=>e.id!==bill.exclude&&counts(e)&&same(e)&&e.date>=from&&e.date<=to&&e.date<=date);
  const earlierTaxable=earlier.reduce((a,e)=>a+tdsTaxable(e.amount,e.gstPaise??0),0);
  const untaxed=earlier.filter(e=>!e.tdsPaise).reduce((a,e)=>a+tdsTaxable(e.amount,e.gstPaise??0),0);
  const total=earlierTaxable+taxable;
  const yearTh=d.basis==='single+year'?tdsValueOn(s,`tds.${base}.thresholdYear` as TdsKey,date):th;
  const period=d.basis==='month'?`in ${date.slice(0,7)}`:`in FY ${fy.label}`;
  let deductBase=0,reason='';
  if(d.basis==='single+year'&&taxable>th){deductBase=taxable;reason=`single bill ${rs(taxable)} above ${rs(th)}`;}
  if(total>yearTh){
    const already=earlierTaxable>yearTh; // the period crossed the threshold before this bill: only this bill
    deductBase=already?taxable:taxable+untaxed;
    reason=`${rs(total)} to ${v.name} ${period} above ${rs(yearTh)}${!already&&untaxed?` (earlier bills without TDS ${rs(untaxed)} included)`:''}`;
  }
  if(!deductBase)return {paise:0,section:code,taxable,base:0,bps,reason:`${rs(total)} to ${v.name} ${period} — within the ${rs(d.basis==='single+year'?th:yearTh)} threshold`};
  const paise=Math.min(amount,gstLineTax(deductBase,bps));
  return {paise,section:code,taxable,base:deductBase,bps,reason:`${d.label} ${tdsPct(bps)} on ${rs(deductBase)} · ${reason}`};
}

// ───────────────────────────── TDS a company deducts from the hotel (A9) ─────────────────────────────
/**
 * A9 (rulebook §5, T7a / T7b): what a company paying the hotel under a regular arrangement is expected to deduct — the rent row (s.393
 * Sl. 2(ii), old 194-I; the hotel is the payee, column 'other' for a firm) at its rate on the taxable value EXCLUDING GST, only when the
 * company's taxable billing in the month exceeds the monthly threshold (`monthTaxable`, default this bill). Catering may be 2 % (old 194C) —
 * the hotel records whatever the company actually deducted; this is only the check figure. Deccan Agro: ₹60,000 → ₹6,000; ₹40,000 → 0.
 */
export function tdsCompanyExpected(s:State,x:{taxable:number;date:string;monthTaxable?:number}):{paise:number;bps:number;threshold:number}{
  const threshold=tdsValueOn(s,'tds.rent.threshold',x.date),bps=tdsValueOn(s,'tds.rent.bpsOther',x.date);
  const month=x.monthTaxable??x.taxable;
  return {paise:month>threshold&&x.taxable>0?gstLineTax(x.taxable,bps):0,bps,threshold};
}
/** Highest share of a settlement a company's TDS may take (20 %: the no-PAN rate is the highest TDS rate; anything above is not TDS). */
export const TDS_RECEIVABLE_MAX_BPS=2000;
/** Why a company payment's TDS figure is refused ('' = fine): whole paise, not negative, at most 20 % of what it settles (amount + tds). */
export function tdsReceivableProblem(amount:number,tds:number):string{
  if(!Number.isInteger(tds)||tds<0)return 'TDS deducted must be whole paise, zero or more.';
  if(tds&&tds*10000>(amount+tds)*TDS_RECEIVABLE_MAX_BPS)return `TDS ${rs(tds)} is more than 20 % of the ${rs(amount+tds)} settled — record what the company actually deducted (Form 168 shows it).`;
  return '';
}
