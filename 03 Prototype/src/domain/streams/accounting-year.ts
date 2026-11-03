// ACC-4 · Year lock, carry-forward, reopen (02 Planning/agents/ACC4-CONTRACT.md §1; rulebook G3 step 5–6, G4; DEC-077 extended to a year).
// Pure helpers and selectors. Exported names start with 'acc' / 'ACC'. Money = integer paise. Slice s.accYearX (optional; SCHEMA stays 13).
// The carry-forward is GENERATED from the books, never typed: balance-sheet ledgers (party sub-ledgers kept) → opening of fy+1; P&L ledgers
// closed to 3100; 3000 Drawings closed to 3100. Approved at the Owner's lock, it posts as the year-close opening voucher dated 1 April of fy+1
// (source kind 'opening', sub 'year-close'): it zeroes the P&L ledgers and 3000 into 3100, so fy+1 opens exactly on the carry-forward lines.
import type {State} from '../types';
import {accSlice,accVouchersUpTo,accDates,type ACCEntry,type ACCVoucher} from './accounting';
import {accTrialBalance6,accRptControl} from './accounting-reports';
import {accPlRoot} from './accounting-pl';
import {accMonthLockOf} from './accounting-gst';
import {accOpening} from './accounting-opening';

export type AccFy=string; // '2026-27'
export type AccYearStatus='open'|'closing'|'locked'|'reopened';
export interface AccYearState{fy:AccFy;status:AccYearStatus;lockedAt?:string;lockedBy?:string;reopenedAt?:string;reopenedBy?:string;reopenReason?:string;
  provisionalExports?:boolean;carryForwardId?:string;history?:{at:string;by:string;action:'lock'|'reopen'|'relock';reason?:string}[]}
export interface AccCarryForwardLine{ledger:string;party?:string;dr:number;cr:number}
export interface AccCarryForward{id:string;fromFy:AccFy;toFy:AccFy;status:'draft'|'approved'|'superseded';lines:AccCarryForwardLine[];
  profitToCapital:number;drawingsToCapital:number;generatedAt:string;generatedBy?:string;approvedBy?:string;approvedAt?:string;supersededBy?:string;
  /** The year-close entries (P&L ledgers and 3000 to 3100) that turn the continuous books into the carry-forward; paise. */ close?:ACCEntry[]}
export type AccYearSlice={years:AccYearState[];carryForwards:AccCarryForward[]};

export const ACC_YEAR_FIRST:AccFy='2026-27';          // §1.3: FY 2026-27 has the hand-entered opening; no 2025-26 book
export const ACC_CAPITAL='3100',ACC_DRAWINGS='3000';
export const ACC_YEAR_REASON_MIN=10;
export const ACC_PROVISIONAL_TEXT='PROVISIONAL — year reopened';
export const accYearSlice=(s:State):AccYearSlice=>s.accYearX??{years:[],carryForwards:[]};

// ── FY arithmetic ──
const FY=/^(\d{4})-(\d{2})$/;
export const accFyValid=(fy:string)=>{const m=FY.exec(fy);return !!m&&(+m[1]+1)%100===+m[2];};
export const accFyOfDate=(date:string):AccFy=>{const y=+date.slice(0,4),m=+date.slice(5,7),a=m>=4?y:y-1;return `${a}-${String((a+1)%100).padStart(2,'0')}`;};
export const accFyBounds=(fy:AccFy)=>{const y=+fy.slice(0,4);return {from:`${y}-04-01`,to:`${y+1}-03-31`};};
export const accFyNext=(fy:AccFy):AccFy=>{const y=+fy.slice(0,4)+1;return `${y}-${String((y+1)%100).padStart(2,'0')}`;};
export const accFyPrev=(fy:AccFy):AccFy=>{const y=+fy.slice(0,4)-1;return `${y}-${String((y+1)%100).padStart(2,'0')}`;};
export const accFyMonths=(fy:AccFy)=>{const y=+fy.slice(0,4);return Array.from({length:12},(_,i)=>{const m=(i+3)%12+1,yy=i<9?y:y+1;return `${yy}-${String(m).padStart(2,'0')}`;});};

// ── Year state ──
export const accYearOf=(s:State,fy:AccFy):AccYearState=>accYearSlice(s).years.find(y=>y.fy===fy)??{fy,status:'open'};
/** A year is closed to postings while it is locked (a reopened year accepts corrections until relocked). */
export const accYearLocked=(s:State,fy:AccFy)=>accYearOf(s,fy).status==='locked';
export const accYearLockedOn=(s:State,date:string)=>/^\d{4}-\d{2}-\d{2}/.test(String(date??''))&&accYearLocked(s,accFyOfDate(String(date).slice(0,10)));
export const accYearLockedMessage=(fy:AccFy)=>`ACC_YEAR_LOCKED: financial year ${fy} is locked — post the correction as a journal in the next year that refers to ${fy} (rulebook G4), or the Owner reopens the year.`;
/** Exports of a year (or of the year after a reopened one) carry ACC_PROVISIONAL_TEXT until relocked (§1.2 ACC_YEAR_REOPEN). */
export function accYearProvisional(s:State,date:string):boolean{
  const fy=accFyOfDate(date);return !!accYearOf(s,fy).provisionalExports||!!accYearOf(s,accFyPrev(fy)).provisionalExports;
}
export const accCarryForward=(s:State,id:string)=>accYearSlice(s).carryForwards.find(c=>c.id===id);
/** The approved carry-forward that opens `fy` (the next year's opening entry, source carry-forward), or undefined. */
export const accYearOpening=(s:State,fy:AccFy)=>accYearSlice(s).carryForwards.find(c=>c.toFy===fy&&c.status==='approved');
/** Latest locked year (only that one can be reopened). */
export function accYearLatestLocked(s:State):AccFy|undefined{
  return accYearSlice(s).years.filter(y=>y.status==='locked').map(y=>y.fy).sort().at(-1);
}

// ── Carry-forward (G3 step 5) ──
const isPl=(s:State,account:string)=>{const c=accRptControl(account);return accPlRoot(s,c).side==='PL'||/^[45]/.test(c);};
/** Builds the carry-forward lines from the closing trial balance of [from, to] (party detail kept). Pure: nothing saved. */
export function accYearCarryLines(s:State,from:string,to:string):{lines:AccCarryForwardLine[];profitToCapital:number;drawingsToCapital:number;close:ACCEntry[];debit:number;credit:number;balanced:boolean}{
  const tb=accTrialBalance6(s,from,to,true);
  const bal=new Map<string,number>();
  for(const r of tb.rows){const n=r.closingDebit-r.closingCredit;if(n)bal.set(r.account,(bal.get(r.account)??0)+n);}
  let pl=0,drawings=0;const close:ACCEntry[]=[];
  const carried=new Map<string,number>();
  for(const [acct,n] of bal){
    if(isPl(s,acct)){pl+=n;close.push(n>0?{account:acct,debit:0,credit:n}:{account:acct,debit:-n,credit:0});continue;}
    if(accRptControl(acct)===ACC_DRAWINGS){drawings+=n;close.push(n>0?{account:acct,debit:0,credit:n}:{account:acct,debit:-n,credit:0});continue;}
    carried.set(acct,(carried.get(acct)??0)+n);
  }
  const profit=-pl;                                         // income Cr balances → profit positive
  const capDelta=-profit+drawings;                          // Dr side of 3100 (signed Dr positive)
  if(capDelta)close.push(capDelta>0?{account:ACC_CAPITAL,debit:capDelta,credit:0}:{account:ACC_CAPITAL,debit:0,credit:-capDelta});
  carried.set(ACC_CAPITAL,(carried.get(ACC_CAPITAL)??0)+capDelta);
  const lines:AccCarryForwardLine[]=[];
  for(const [acct,n] of [...carried].sort((a,b)=>a[0]<b[0]?-1:a[0]>b[0]?1:0)){
    if(!n)continue;const i=acct.indexOf('/');
    lines.push({ledger:i<0?acct:acct.slice(0,i),...(i<0?{}:{party:acct.slice(i+1)}),dr:n>0?n:0,cr:n<0?-n:0});
  }
  const debit=lines.reduce((a,l)=>a+l.dr,0),credit=lines.reduce((a,l)=>a+l.cr,0);
  return {lines,profitToCapital:profit,drawingsToCapital:drawings,close,debit,credit,balanced:debit===credit};
}
export const accCfAccount=(l:AccCarryForwardLine)=>l.party?`${l.ledger}/${l.party}`:l.ledger;

// ── Preview checklist (§1.2 ACC_YEAR_PREVIEW) ──
export type AccYearCheck={id:'months-gst-locked'|'days-locked'|'opening-difference'|'suspense'|'clearing'|'pending'|'opening-approved';ok:boolean;label:string;detail:string};
export function accYearChecks(s:State,fy:AccFy):AccYearCheck[]{
  const {from,to}=accFyBounds(fy),x=accSlice(s);
  const dates=accDates(s).filter(d=>d>=from&&d<=to&&d<s.date||(d>=from&&d<=to&&s.closedDates.includes(d)));
  const unlockedDays=dates.filter(d=>!x.locks?.[d]).sort();
  const months=[...new Set(dates.map(d=>d.slice(0,7)))].sort();
  const openMonths=months.filter(m=>!accMonthLockOf(s,m));
  const bal=new Map<string,number>();
  for(const v of accVouchersUpTo(s,to))for(const e of v.entries){const c=accRptControl(e.account);bal.set(c,(bal.get(c)??0)+e.debit-e.credit);}
  const b=(c:string)=>bal.get(c)??0;
  const pend:string[]=[];
  for(const m of x.manual??[])if(m.status==='pending'&&m.date>=from&&m.date<=to)pend.push(`manual voucher ${m.id}`);
  for(const e of s.expenses)if(e.status==='Pending'&&e.date>=from&&e.date<=to)pend.push(`expense ${e.id}`);
  for(const a of s.approvals??[])if((a as {status?:string}).status==='Pending'&&/credit/i.test(String((a as {kind?:string}).kind??(a as {type?:string}).type??''))&&String((a as {at?:string}).at??'').slice(0,10)<=to)pend.push(`credit note ${a.id}`);
  const fyEnded=s.date>to;
  const out:AccYearCheck[]=[
    {id:'opening-approved',ok:fy!==ACC_YEAR_FIRST||accOpening(s)?.status==='approved'||!accOpening(s),label:'Opening entry approved',detail:fy===ACC_YEAR_FIRST&&accOpening(s)?.status!=='approved'&&accOpening(s)?'The hand-entered opening of 1 April is a draft.':''},
    {id:'days-locked',ok:fyEnded&&!unlockedDays.length,label:'Every day of the year locked',detail:!fyEnded?`The year ends on ${to}; the business date is ${s.date}.`:unlockedDays.length?`${unlockedDays.length} day(s) not locked, first ${unlockedDays[0]}.`:''},
    {id:'months-gst-locked',ok:!openMonths.length,label:'Every month GST-locked',detail:openMonths.length?`Not locked: ${openMonths.join(', ')}.`:''},
    {id:'opening-difference',ok:b('1400')===0,label:'1400 Opening-balance difference is zero',detail:b('1400')?`₹${(Math.abs(b('1400'))/100).toFixed(2)} in 1400.`:''},
    {id:'suspense',ok:b('9999')===0,label:'9999 Suspense is zero',detail:b('9999')?`₹${(Math.abs(b('9999'))/100).toFixed(2)} in 9999.`:''},
    {id:'clearing',ok:b('1010')===0&&b('1020')===0,label:'1010 UPI / 1020 card clearing zero at 31 March',detail:b('1010')||b('1020')?`1010 ₹${(b('1010')/100).toFixed(2)} · 1020 ₹${(b('1020')/100).toFixed(2)} not settled to the bank.`:''},
    {id:'pending',ok:!pend.length,label:'Nothing pending dated in the year',detail:pend.length?`${pend.length} pending: ${pend.slice(0,3).join(', ')}${pend.length>3?'…':''}.`:''},
  ];
  return out;
}
export type AccYearPreview={fy:AccFy;from:string;to:string;ready:boolean;checks:AccYearCheck[];draft:ReturnType<typeof accYearCarryLines>};
export function accYearPreview(s:State,fy:AccFy):AccYearPreview{
  const {from,to}=accFyBounds(fy),checks=accYearChecks(s,fy);
  return {fy,from,to,ready:checks.every(c=>c.ok),checks,draft:accYearCarryLines(s,from,to)};
}

// ── The year-close opening voucher (book side of an approved carry-forward) ──
/** Approved carry-forwards as opening vouchers dated 1 April of the next year (counted by accVouchersUpTo). Kind 'opening' keeps them out
 *  of the next year's P&L and day book (accRptIsOpening). */
export function accYearCloseVouchers(s:State):ACCVoucher[]{
  const out:ACCVoucher[]=[];
  for(const c of s.accYearX?.carryForwards??[]){
    if(c.status!=='approved'||!c.close?.length)continue;const d=accFyBounds(c.toFy).from;
    out.push({no:`JRN/${d.split('-').join('')}/YC`,date:d,type:'Journal',narration:`Year close ${c.fromFy}: P&L and drawings to ${ACC_CAPITAL} (carry-forward ${c.id})`,
      source:{kind:'opening',id:c.id,sub:'year-close'},entries:c.close.map(e=>({...e})),unmapped:[]});
  }
  return out;
}
