// ACC-2 · lane acc2-tds — TDS reports, remittances and the Master configuration rows (contract §4 A9, §5; rulebook §2.9, §5 A9 / C9 / E5 / E6).
// The rate table and the bill calculation live in domain/tds.ts (re-exported here, so the mock barrel needs one line).
//   tdsDeducteeReport(s, fy|month)  deductee-wise: vendor, PAN, section, taxable, TDS held back (Expense.tdsPaise), remitted (allocated from the
//                                   'tds-remittance' manual vouchers of the same section and month), pending; tdsDeducteeCsv for the CA (DEC-146).
//   tdsRemittances(s)               the ACC_TDS_REMIT entries (manual vouchers, template tds-remittance; section and month in the narration).
//   tdsReceivableReport(s, fy)      Form 168 reconciliation (A9 / E6): TDS the companies deducted from the hotel (GRP_COMPANY_PAYMENT tds → Dr 1360)
//                                   by company and month, the 1360 ledger movement beside it, and the Form 168 figure the CA keys in.
// Exported names start with 'tds' / 'TDS' / 'acc' (shared mock namespace).
import type {State,Expense} from '../types';
import {csvMinimal} from '../csv';
import {gstFy} from '../gst';
import {money} from '../money';
import {mnyVendor} from './money-master';
import {grpSlice} from './groups';
import {accSlice,accMapping,accVouchers,type ACCManualVoucher} from './accounting';
import {accControlOf} from './accounting-parties';
import {registerSettingsGroup,type CfgProviderDef} from './config';
import {TDS_SEED,TDS_SECTIONS,TDS_ILLUSTRATIVE,TDS_LEDGER,ACC_TDS_PAYABLE_KEY,tdsSeedOn,tdsPct,tdsBaseSection,tdsSectionDef,type TdsKey,type TdsSectionId} from '../tds';

export * from '../tds';

// ───────────────────────────── Master configuration › Money and tax (Owner-only, dated; DEC-058) ─────────────────────────────
/** The TDS table as dated hub settings (like the GST master): SETTING_SET on a tds.* key stores a dated change that tdsValueOn reads. */
export function tdsSettingDefs(s:State):CfgProviderDef[]{
  return TDS_SEED.map(d=>{const sec=tdsSectionDef(d.section)!;
    return {key:d.key,label:d.label,type:d.type,unit:d.unit,help:`${d.help} ${sec.source}. ${TDS_ILLUSTRATIVE} for a hotel.`,money:true,dated:true,effectiveMode:'date' as const,
      illustrative:true,default:tdsSeedOn(d.key,s.date),...(d.type==='percent'?{min:0,max:3000}:{min:0,max:100000000}),
      show:(v:unknown)=>d.type==='percent'?tdsPct(Number(v)):money(Number(v)),
      affects:'Bills posted on or after the effective date (TDS held back from the vendor). Bills already posted keep their TDS.'};});
}
registerSettingsGroup({id:'tds',group:'money',defs:tdsSettingDefs});

// ───────────────────────────── Periods ─────────────────────────────
export type TdsPeriod={label:string;from:string;to:string;kind:'fy'|'month'};
/** '2026-27' (financial year) or '2026-04' (month); {fy} / {month} also accepted. Default: the FY of the business date. */
export function tdsPeriod(s:State,p?:string|{fy?:string;month?:string}):TdsPeriod{
  const fy=(y:number):TdsPeriod=>{const f=gstFy(`${y}-04-01`);return {label:'FY '+f.label,from:f.from,to:f.to,kind:'fy'};};
  const def=()=>fy(+gstFy(s.date).from.slice(0,4));
  const want=typeof p==='object'&&p?(p.month?'month':p.fy?'fy':undefined):undefined;
  const v=typeof p==='string'?p:typeof p==='object'&&p?p.month??p.fy:undefined;
  const m=/^(\d{4})-(\d{2})$/.exec(String(v??''));if(!m)return def();
  const y=+m[1],two=+m[2],isFy=two===(y+1)%100;
  if(want==='fy'||(!want&&isFy))return isFy?fy(y):def();
  if(two<1||two>12)return def();
  return {label:String(v),from:`${v}-01`,to:`${v}-31`,kind:'month'};
}
const inP=(p:TdsPeriod,d:string)=>d>=p.from&&d<=p.to;

// ───────────────────────────── Remittances (E5; ACC_TDS_REMIT → manual template tds-remittance) ─────────────────────────────
export type TdsRemit={id:string;date:string;month:string;section:TdsSectionId;amount:number;challan:string;status:ACCManualVoucher['status'];voucher:ACCManualVoucher};
const REMIT_RE=/^TDS remittance · ([a-z]+) \(.*\) · (\d{4}-\d{2}) · challan (.+)$/;
/** The narration ACC_TDS_REMIT writes (parsed back by tdsRemittances; the server keeps the same fields in acc.tds_remit). */
export function tdsRemitNarration(section:TdsSectionId,month:string,challan:string){
  const d=tdsSectionDef(section)!;return `TDS remittance · ${section} (${d.section}, old ${d.old}) · ${month} · challan ${challan}`;
}
/** Every TDS remittance entry (any status), oldest first. */
export function tdsRemittances(s:State):TdsRemit[]{
  const out:TdsRemit[]=[];
  for(const v of accSlice(s).manual??[]){
    if(v.template!=='tds-remittance'||v.voidOf)continue;const m=REMIT_RE.exec(v.narration);const sec=m?tdsBaseSection(m[1]):undefined;if(!m||!sec)continue;
    const amount=v.lines.filter(l=>accControlOf(l.account)===(accMapping(s)[ACC_TDS_PAYABLE_KEY]??TDS_LEDGER.payable)).reduce((a,l)=>a+l.debit-l.credit,0);
    out.push({id:v.id,date:v.date,month:m[2],section:sec,amount,challan:m[3],status:v.status,voucher:v});
  }
  return out.sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:a.id<b.id?-1:1);
}
const live=(r:TdsRemit)=>r.status==='approved'||r.status==='pending';

// ───────────────────────────── Bills with TDS ─────────────────────────────
export type TdsBillRow={expense:string;date:string;month:string;vendor:string;code:string;pan:string;section:string;base:TdsSectionId;taxable:number;tds:number;remitted:number};
const counts=(e:Expense)=>e.status==='Posted'||e.status==='Reversed';
/** Every posted bill that carries TDS (reversal records carry their negative TDS), with the remitted share allocated per section and month
 *  (oldest bill first) from the approved remittances. */
export function tdsBills(s:State):TdsBillRow[]{
  const rows:TdsBillRow[]=[];
  for(const e of s.expenses){
    if(!counts(e)||!e.tdsPaise)continue;
    const v=e.vendor?mnyVendor(s,e.vendor):undefined,code=e.tdsSection??v?.tdsSection??'',base=tdsBaseSection(code);if(!base)continue;
    rows.push({expense:e.id,date:e.date,month:e.date.slice(0,7),vendor:v?.name??e.vendor??'—',code:v?.code??'',pan:v?.pan??'',section:code,base,
      taxable:e.amount-(e.gstPaise??0),tds:e.tdsPaise,remitted:0});
  }
  rows.sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:a.expense<b.expense?-1:1);
  const pool=new Map<string,number>();
  for(const r of tdsRemittances(s))if(r.status==='approved'){const k=r.section+'|'+r.month;pool.set(k,(pool.get(k)??0)+r.amount);}
  for(const b of rows){const k=b.base+'|'+b.month,left=pool.get(k)??0;if(left<=0||b.tds<=0)continue;const take=Math.min(left,b.tds);b.remitted=take;pool.set(k,left-take);}
  return rows;
}
/** TDS held back for a section and month less what is remitted (approved and pending entries) — what ACC_TDS_REMIT may still pay. */
export function tdsPayableOutstanding(s:State,section:TdsSectionId,month:string):number{
  const held=tdsBills(s).filter(b=>b.base===section&&b.month===month).reduce((a,b)=>a+b.tds,0);
  const paid=tdsRemittances(s).filter(r=>live(r)&&r.section===section&&r.month===month).reduce((a,r)=>a+r.amount,0);
  return held-paid;
}

// ───────────────────────────── Deductee report (contract §5) ─────────────────────────────
export type TdsDeducteeRow={vendor:string;code:string;pan:string;section:string;sectionLabel:string;bills:number;taxable:number;tds:number;remitted:number;pending:number};
export type TdsDeducteeReport={period:TdsPeriod;rows:TdsDeducteeRow[];bills:TdsBillRow[];remittances:TdsRemit[];
  totals:{taxable:number;tds:number;remitted:number;pending:number};note:string};
const sectionLabel=(code:string)=>{const base=tdsBaseSection(code),d=base?tdsSectionDef(base)!:undefined;if(!d)return code;
  const v=code.split(':')[1],vl=v?d.variants?.find(x=>x.id===v)?.label:undefined;return `${vl??d.label} · ${d.section} (old ${d.old})`;};
/** Deductee-wise TDS for a financial year ('2026-27') or a month ('2026-04'): one row per vendor and section; the CA deposits and files (DEC-146). */
export function tdsDeducteeReport(s:State,period?:string|{fy?:string;month?:string}):TdsDeducteeReport{
  const p=tdsPeriod(s,period),bills=tdsBills(s).filter(b=>inP(p,b.date)),by=new Map<string,TdsDeducteeRow>();
  for(const b of bills){const k=b.vendor+'|'+b.section;const r=by.get(k)??{vendor:b.vendor,code:b.code,pan:b.pan,section:b.section,sectionLabel:sectionLabel(b.section),bills:0,taxable:0,tds:0,remitted:0,pending:0};
    r.bills++;r.taxable+=b.taxable;r.tds+=b.tds;r.remitted+=b.remitted;r.pending=r.tds-r.remitted;by.set(k,r);}
  const rows=[...by.values()].sort((a,b)=>a.vendor<b.vendor?-1:a.vendor>b.vendor?1:a.section<b.section?-1:1);
  const totals=rows.reduce((a,r)=>({taxable:a.taxable+r.taxable,tds:a.tds+r.tds,remitted:a.remitted+r.remitted,pending:a.pending+r.pending}),{taxable:0,tds:0,remitted:0,pending:0});
  return {period:p,rows,bills,remittances:tdsRemittances(s).filter(r=>r.month>=p.from.slice(0,7)&&r.month<=p.to.slice(0,7)),totals,
    note:'TDS held back by HCC at the dated rate table (CA to confirm); the CA deposits it by the 7th (30 April for March) and files the return (DEC-146).'};
}
export const TDS_DEDUCTEE_CSV_HEADER=['Vendor','Vendor code','PAN','Section','Bills','Taxable (₹)','TDS (₹)','Remitted (₹)','Pending (₹)'];
const rupees=(p:number)=>(p/100).toFixed(2);
/** CSV for the CA (generic, CRLF, amounts in rupees with 2 decimals; formula guard via csvMinimal). */
export function tdsDeducteeCsv(r:TdsDeducteeReport):string{
  const rows:(string|number)[][]=[TDS_DEDUCTEE_CSV_HEADER,...r.rows.map(x=>[x.vendor,x.code,x.pan,x.sectionLabel,x.bills,rupees(x.taxable),rupees(x.tds),rupees(x.remitted),rupees(x.pending)]),
    ['Total','','','',r.rows.reduce((a,x)=>a+x.bills,0),rupees(r.totals.taxable),rupees(r.totals.tds),rupees(r.totals.remitted),rupees(r.totals.pending)]];
  return rows.map(x=>x.map(csvMinimal).join(',')).join('\r\n');
}

// ───────────────────────────── TDS receivable — Form 168 reconciliation (A9, E6) ─────────────────────────────
export type TdsReceivableRow={company:string;name:string;month:string;payments:number;settled:number;received:number;tds:number};
export type TdsReceivableCompany={company:string;name:string;tds:number;form168?:number;difference?:number};
export type TdsReceivableReport={period:TdsPeriod;rows:TdsReceivableRow[];companies:TdsReceivableCompany[];total:number;ledger:number;ledgerMatches:boolean;note:string};
/**
 * Form 168 reconciliation (rulebook A9 / E6): TDS deducted by companies from their payments to the hotel (Dr 1360), by company and month,
 * the 1360 movement of the company-payment vouchers of the period beside it, and per company the Form 168 figure (paise) when the CA keys it in
 * — a shortfall is chased with the company; at year end the credit goes to the proprietor (Dr 3000 · Cr 1360, a manual journal, E6).
 */
export function tdsReceivableReport(s:State,fy?:string,form168:Record<string,number>={}):TdsReceivableReport{
  const p=tdsPeriod(s,fy),g=grpSlice(s),name=(id:string)=>g.companies.find(c=>c.id===id)?.name??id,by=new Map<string,TdsReceivableRow>();
  const pays=(g.payments??[]).filter(x=>inP(p,x.date)&&(x.tds??0)!==0);
  for(const x of pays){const k=x.company+'|'+x.date.slice(0,7);const r=by.get(k)??{company:x.company,name:name(x.company),month:x.date.slice(0,7),payments:0,settled:0,received:0,tds:0};
    r.payments++;r.received+=x.amount;r.tds+=x.tds??0;r.settled=r.received+r.tds;by.set(k,r);}
  const rows=[...by.values()].sort((a,b)=>a.name<b.name?-1:a.name>b.name?1:a.month<b.month?-1:1);
  const per=new Map<string,TdsReceivableCompany>();
  for(const r of rows){const c=per.get(r.company)??{company:r.company,name:r.name,tds:0};c.tds+=r.tds;per.set(r.company,c);}
  for(const [id,f] of Object.entries(form168)){const c=per.get(id)??{company:id,name:name(id),tds:0};c.form168=f;c.difference=f-c.tds;per.set(id,c);}
  const companies=[...per.values()].sort((a,b)=>a.name<b.name?-1:1);
  const total=rows.reduce((a,r)=>a+r.tds,0),recv=accMapping(s)['tax:tds-receivable']??TDS_LEDGER.receivable;
  const ledger=[...new Set(pays.map(x=>x.date))].flatMap(d=>accVouchers(s,d)).filter(v=>v.source.sub==='company-payment')
    .reduce((a,v)=>a+v.entries.filter(e=>accControlOf(e.account)===recv).reduce((b,e)=>b+e.debit-e.credit,0),0);
  return {period:p,rows,companies,total,ledger,ledgerMatches:ledger===total,
    note:'Companies deduct TDS on the hotel’s taxable value excluding GST (s.393, old 194-I; Circular 23/2017); reconcile to Form 168 (earlier 26AS) and chase any shortfall. CA to confirm.'};
}
export const TDS_RECEIVABLE_CSV_HEADER=['Company','Month','Payments','Received (₹)','TDS deducted (₹)','Settled (₹)'];
export function tdsReceivableCsv(r:TdsReceivableReport):string{
  const rows:(string|number)[][]=[TDS_RECEIVABLE_CSV_HEADER,...r.rows.map(x=>[x.name,x.month,x.payments,rupees(x.received),rupees(x.tds),rupees(x.settled)]),
    ['Total','',r.rows.reduce((a,x)=>a+x.payments,0),rupees(r.rows.reduce((a,x)=>a+x.received,0)),rupees(r.total),rupees(r.rows.reduce((a,x)=>a+x.settled,0))]];
  return rows.map(x=>x.map(csvMinimal).join(',')).join('\r\n');
}
/** Section rows for pickers / the TDS tab (id, label with the s.393 row and the old section). */
export const tdsSectionChoices=()=>TDS_SECTIONS.map(d=>({id:d.id,label:`${d.label} · ${d.section} (old ${d.old})`}));
