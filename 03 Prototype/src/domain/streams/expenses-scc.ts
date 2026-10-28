// SCC expense features in HCC (DEC-120, 27 Oct 2026). Pure helpers (exported names start with 'exp' / 'EXP') for the expense register's
// description, chosen date, GST on the bill, bill photo, edit log and void; the petty-cash float; monthly budgets; recurring templates;
// the Udhaar credit register; the month lock (SCC "Generate & lock tax feed") and SCC's ledger / month CSV.
// Source: SCC expense\index.html (scratchpad scc\expense.js; line numbers below are expense.js lines, index.html = +384).
// HCC's own approval rules stay (DEC-120, DEC-063 G5, REQ-EXP-003): an expense counts only once approved, so every SCC total that reads
// "non-void ledger rows" reads HCC's counted records here (Posted / Reversed originals and their negative reversal records).
// Money is integer paise (SCC: rupees as floats).
import type {State,Expense} from '../types';
import {money} from '../money';
import {csvMinimal} from '../csv';
import {expenseCounts,closingReopened} from './closing';
import {cbIsLocked,cbLockedMessage} from './cashbook';
import {mnyIsDrawer} from './money-master';
import {mstCanEditMoney} from './masters';

// ── Constants (SCC expense.js) ──
/** SCC REASONS (line 33): the fixed void / correction reasons. */
export const EXP_VOID_REASONS=['Cash shortage correction','Duplicate entry','Missed receipt','Wrong payment mode','Voucher correction','Other'] as const;
/** Largest expense: ₹10 crore (the server's Paise maximum; was ₹10 lakh in the prototype). SCC has no maximum. */
export const EXP_MAX_PAISE=10_000_000_000;
/** SCC readPhoto (458-466): a picked file above 4 MB is refused; the photo is scaled to at most 1000 px wide and saved as JPEG quality 0.7. */
export const EXP_PHOTO_MAX_BYTES=4*1024*1024;
export const EXP_PHOTO_WIDTH=1000;
export const EXP_PHOTO_QUALITY=0.7;
/** SCC budgets (1287, 1306-1308): tab "near limit" at ≥ 85 %, "over" above 100 %; entry-time alert warns at ≥ 90 %, error at ≥ 100 %. */
export const EXP_BUDGET_NEAR=85;
export const EXP_ALERT_WARN=90;
export const EXP_ALERT_ERROR=100;
/** SCC petty cash (1213): "Replenish soon" when the balance is below 15 % of the float. */
export const EXP_PETTY_LOW_PCT=15;
/** SCC Udhaar payment (1587): up to ₹0.50 more than outstanding is accepted as typed; more is capped to the outstanding. */
export const EXP_UD_TOLERANCE=50;
/** SCC Udhaar (1618): default due date = credit date + 30 days. */
export const EXP_UD_DUE_DAYS=30;
export const EXP_MONTHS=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
/** Description / note / reason lengths (HCC; SCC has none). */
export const EXP_DESC_MAX=300;

// ── State (s.expensesX, optional: absent in old saves = nothing set) ──
export type ExpEditLog={at:string;by:string;old:Record<string,string|number>;new:Record<string,string|number>;reason:string};
export type ExpRecTpl={id:string;name:string;amount:number;dueDay:number;category:string;method:string;vendor:string;active:boolean;createdAt:string;createdBy:string};
export type ExpPettyEntry={id:string;amount:number;date:string;by:string;at:string;status:'Pending'|'Approved'|'Rejected';note?:string;decidedBy?:string;decidedAt?:string;reason?:string};
/** `desk` (DEC-130): taken as a desk receipt — cash into the open shift's drawer, other modes to their own ledgers — and booked in Accounting
 *  (Dr the tender · Cr Udhaar receivables). Settlements recorded before DEC-130 have no `desk`: they never reached the drawer or the books. */
export type ExpUdSettlement={id:string;amount:number;date:string;mode:string;by:string;at:string;desk?:true;reference?:string};
/** ACC-1 (contract §4, rulebook A17, DEC-144): `kind` = what was sold on credit (restaurant bill, room folio or other — the screen asks) and
 *  `gstBps` = its GST rate. Raising the credit posts a Sales voucher the same day (Dr 1120/<customer> gross · Cr F&B / room / other revenue ·
 *  Cr CGST · Cr SGST). Rows raised before ACC-1 have neither: kind 'other' (4100), no GST lines (CA note in ACC1-PARTIES-DONE.md). */
export type ExpUdKind='restaurant'|'room'|'other';
export type ExpUdReceivable={id:string;name:string;mobile:string;amount:number;date:string;dueDate:string;note:string;settlements:ExpUdSettlement[];by:string;at:string;
  void?:boolean;voidReason?:string;voidAt?:string;voidBy?:string;kind?:ExpUdKind;gstBps?:number};
/** SCC gm_tax_feed[month] (1508): the month's figures when the Owner generated the feed and locked the month. HCC keeps the expense side
 *  (HCC has no manual income ledger) and SCC's "GST paid on purchases". `stale` is set by a write into the locked month (Owner override). */
export type ExpMonthFeed={month:string;expense:number;count:number;byCategory:Record<string,number>;gstPaid:number;generatedAt:string;by:string;stale?:boolean;staleAt?:string;
  /** DEC-135: the part of gstPaid marked ITC allowed (absent on feeds generated before). */
  gstItc?:number;
  /** DEC-131: the Owner unlocked the month (with a reason); writes no longer need the override and the figures are stale until locked again. */
  unlockedAt?:string;unlockedBy?:string;unlockReason?:string;
  /** DEC-131: every Owner unlock of this month, oldest first (kept when the month is locked again). */
  unlocks?:ExpMonthUnlock[]};
export type ExpMonthUnlock={at:string;by:string;reason:string};
/** ACC-2 (contract §3.2): a payment to a vendor, allocated to open bills oldest-first by default; the unallocated rest is an advance (the party goes Dr). */
export type ExpVendorPayment={id:string;vendor:string;date:string;amount:number;method:string;reference:string;by:string;at:string;allocations:{expense:string;amount:number}[];
  void?:boolean;voidReason?:string;voidBy?:string;voidAt?:string};
export type EXPState={
  /** ACC-2 */ vendorPayments?:ExpVendorPayment[];
  /** SCC gm_budgets: month (YYYY-MM) → category → cap in paise (0 / absent = no budget). */
  budgets?:Record<string,Record<string,number>>;
  recurring?:ExpRecTpl[];
  /** Petty-cash float top-ups (replenishments), each approved by a second person; float = Σ approved. */
  petty?:ExpPettyEntry[];
  udhaar?:ExpUdReceivable[];
  months?:Record<string,ExpMonthFeed>;
};
export const expSlice=(s:State):EXPState=>(s.expensesX??{}) as EXPState;

// ── Small helpers ──
export const expYm=(date:string)=>String(date??'').slice(0,7);
/** SCC prevYM (1321). */
export function expPrevMonth(m:string):string{let y=+m.slice(0,4),mo=+m.slice(5,7)-1;if(mo<1){y--;mo=12;}return y+'-'+(mo<10?'0':'')+mo;}
/** SCC recDaysInMonth (705). */
export const expDaysInMonth=(m:string)=>new Date(+m.slice(0,4),+m.slice(5,7),0).getDate();
/** What the entry is for; records saved before DEC-120 have no description and show the vendor. */
export const expDescription=(e:Expense)=>e.description??e.vendor;
/** Counted reversal records carry their original's source (petty / recurring), so totals net to zero. */
const counts=(e:Expense)=>expenseCounts(e);
/** Rupee text for CSV cells: SCC writes the JS number (1180, 1180.5), HCC the same value from paise. */
export const expRupees=(p:number)=>String(p/100);
/** Percent text of a rate in basis points (1800 → "18", 1250 → "12.5"). */
export const expPct=(bps:number)=>String(bps/100);

// ── Date of an entry (SCC: any day whose cash statement is open; HCC: the business date or the Owner-reopened day, DEC-077) ──
/**
 * Why an expense may not be dated `date` (null = it may). Never after the business date (SCC has no future check — bug B5; HCC refuses as
 * the owner asked); the business date while it is open; an earlier date only while the Owner has it reopened (DEC-077, like folio
 * corrections) — SCC: "That day's cash statement is locked. Owner must reopen it first."; never a locked cash-book day (DEC-065). Drawer
 * cash leaves today's drawer, so a drawer-cash expense is always dated the business date.
 */
export function expDateCheck(s:State,date:string,method?:string):{code:'VALIDATION'|'DAY_CLOSED';message:string}|null{
  if(!/^\d{4}-\d{2}-\d{2}$/.test(String(date??'')))return {code:'VALIDATION',message:'Choose a valid expense date.'};
  if(date>s.date)return {code:'VALIDATION',message:`An expense cannot be dated after the business date (${s.date}).`};
  if(date===s.date){if(s.closedDates.includes(date))return {code:'DAY_CLOSED',message:'This business day is closed. Open the next business day first.'};}
  else if(closingReopened(s)?.date!==date)return {code:'DAY_CLOSED',message:`That day’s cash statement is locked (${date} is a closed business day). The Owner must reopen it first.`};
  if(cbIsLocked(s,date))return {code:'DAY_CLOSED',message:cbLockedMessage(date,'an expense on that day')};
  if(method&&date!==s.date&&mnyIsDrawer(s,method))return {code:'VALIDATION',message:'Drawer cash leaves today’s drawer: date a cash expense on the business date, or choose another funding source.'};
  return null;
}
/** Dates an expense may carry now: the business date, and the Owner-reopened day when there is one (date pickers). */
export function expOpenDates(s:State):string[]{
  const r=closingReopened(s)?.date;const out:string[]=[];
  if(!s.closedDates.includes(s.date)&&!cbIsLocked(s,s.date))out.push(s.date);
  if(r&&r<s.date&&!cbIsLocked(s,r))out.push(r);
  return out;
}

// ── GST on the bill (SCC P1-17) ──
/**
 * SCC prefillGst (445-448): GST part of a GST-inclusive bill, round(amount × r ÷ (100 + r), 2 dp), computed exactly as SCC does (rupees and
 * percent as JS numbers, Math.round), so an exact half paisa follows SCC's floating-point result; returned in paise. Only a suggestion — the
 * GST box stays editable. 0 when the amount or the rate is not positive. (Server twin: ExpenseScc.GstPrefill, same double arithmetic.)
 */
export function expGstPrefill(amount:number,rateBps:number):number{
  if(!(amount>0)||!(rateBps>0))return 0;
  const a=amount/100,r=rateBps/100;return Math.round(a*r/(100+r)*100);
}
/** SCC: the confirm "GST amount (…) is more than the entry amount (…). Save anyway?" (528, 573) — a warning, never a block. */
export const expGstOverMessage=(gst:number,amount:number)=>`GST amount (${money(gst)}) is more than the entry amount (${money(amount)}). Save anyway?`;
/** SCC "GST paid on purchases (from entries — input-credit hint)" (1430): Σ GST of the month's counted expenses (reversals net it off). */
export function expGstPaid(s:State,month:string):number{
  let t=0;for(const e of s.expenses)if(counts(e)&&expYm(e.date)===month)t+=e.gstPaise??0;return t;
}

// ── Input tax credit on the bill's GST (DEC-135, owner 27 Oct 2026: "an input-credit ledger … with an option ITC allowed or ITC disallowed") ──
/** ITC allowed: the bill's GST goes to the GST input-credit ledger; disallowed (the default) leaves it in the expense cost. */
export const EXP_ITC=['disallowed','allowed'] as const;
export const EXP_ITC_LABEL:Record<'allowed'|'disallowed',string>={allowed:'ITC allowed',disallowed:'ITC disallowed'};
/** The ITC flag an expense keeps: only with GST on the bill; anything but 'allowed' is 'disallowed' (nothing is claimed unless marked). */
export function expItcOf(v:unknown,gstPaise:number|undefined):'allowed'|'disallowed'|undefined{
  if(!gstPaise)return undefined;return v==='allowed'?'allowed':'disallowed';
}
/** Why ITC cannot be allowed on this bill ('' = it can): the credit can never be more than the bill (GST above the amount). */
export function expItcRefusal(itc:'allowed'|'disallowed'|undefined,gstPaise:number,amount:number):string{
  return itc==='allowed'&&Math.abs(gstPaise)>Math.abs(amount)?'ITC cannot be allowed on more GST than the bill amount — correct the GST or mark ITC disallowed.':'';
}
/** GST of a counted record booked to the input-credit ledger (0 when disallowed or none; a reversal record carries −GST). */
export const expItcPaise=(e:Expense)=>e.itc==='allowed'?e.gstPaise??0:0;
/** "GST paid on purchases" of a month split into ITC allowed and disallowed (counted records only; reversals net off). */
export function expGstSplit(s:State,month:string):{total:number;allowed:number;disallowed:number}{
  let total=0,allowed=0;for(const e of s.expenses)if(counts(e)&&expYm(e.date)===month){total+=e.gstPaise??0;allowed+=expItcPaise(e);}
  return {total,allowed,disallowed:total-allowed};
}
/** Restore migration (DEC-135): an expense saved with GST before the flag existed is ITC disallowed. Idempotent; true when it changed. */
export function expMigrateItc(s:State):boolean{
  let changed=false;
  for(const e of s.expenses??[])if(e.gstPaise&&e.itc!=='allowed'&&e.itc!=='disallowed'){e.itc='disallowed';changed=true;}
  return changed;
}

// ── Who changes the expense money settings (DEC-129: budgets and recurring templates are Owner-only, DEC-058) ──
/** Setting / copying budgets and saving / switching / deleting recurring templates: the Owner (the masters money permission, PERM-MST-002). */
export const expCanSetMoney=(s:State,uid:string)=>mstCanEditMoney(s,uid);
export const EXP_OWNER_ONLY_BUDGETS='Only the Owner sets and copies the monthly budgets (money settings, DEC-058).';
export const EXP_OWNER_ONLY_RECURRING='Only the Owner keeps the recurring templates (money settings, DEC-058). Anyone who enters expenses can post a due template.';

// ── Duplicates (SCC addEntry 526): the key lives in closing.ts expenseDuplicates (date + amount + description) ──
export const expDuplicateMessage=(date:string,amount:number,description:string)=>`A similar entry already exists for ${date} (${money(amount)} — ${description}). Save anyway?`;

// ── Month lock (SCC P1-18 isMonthLocked / markFeedStale / genTaxFeed; fitted to HCC's accounting day locks) ──
/** A month is locked while its feed exists and the Owner has not unlocked it (DEC-131). */
export const expMonthLocked=(s:State,date:string)=>{const f=expSlice(s).months?.[expYm(date)];return !!f&&!f.unlockedAt;};
export const expMonthLockedMessage=(month:string)=>`Month ${month} is tax-locked — its tax feed was already generated. Owner override required.`;
/** DEC-131: why the Owner cannot unlock `month` now (null = can; like Accounting's period unlock: a reason of 3–200 characters). The role is
 *  checked separately. */
export function expMonthUnlockRefusal(s:State,month:string,reason:string):{code:'INVALID_TRANSITION'|'VALIDATION';message:string}|null{
  const f=expSlice(s).months?.[String(month??'')];
  if(!f)return {code:'INVALID_TRANSITION',message:`Month ${month} is not locked.`};
  if(f.unlockedAt)return {code:'INVALID_TRANSITION',message:`Month ${month} is already unlocked — lock it again after the changes.`};
  const r=String(reason??'').trim();
  if(r.length<3)return {code:'VALIDATION',message:'A reason is required to unlock a month.'};
  if(r.length>200)return {code:'VALIDATION',message:'Keep the reason to 200 characters.'};
  return null;
}
/** DEC-131: the banner / section text of an unlocked month. */
export const expMonthUnlockedMessage=(f:ExpMonthFeed)=>`Month ${f.month} was unlocked by the Owner ${String(f.unlockedAt??'').replace('T',' ')} (${f.unlockReason??''}). Entries dated in it need no override; its feed is stale until the month is locked again.`;
/** The month's counted figures (SCC genTaxFeed 1492-1504, expense side); DEC-135: gstItc = the GST marked ITC allowed. */
export function expMonthSummary(s:State,month:string):{expense:number;count:number;byCategory:Record<string,number>;gstPaid:number;gstItc:number}{
  let expense=0,count=0,gstPaid=0,gstItc=0;const byCategory:Record<string,number>={};
  for(const e of s.expenses)if(counts(e)&&expYm(e.date)===month){expense+=e.amount;count++;gstPaid+=e.gstPaise??0;gstItc+=expItcPaise(e);byCategory[e.category]=(byCategory[e.category]??0)+e.amount;}
  return {expense,count,byCategory,gstPaid,gstItc};
}
/** SCC: a future month cannot be locked (1491). HCC's "today" is the business date. */
export function expMonthLockRefusal(s:State,month:string):string{
  if(!/^\d{4}-(0[1-9]|1[0-2])$/.test(String(month??''))||month>expYm(s.date))return 'Pick the current or a past month — a future month cannot be locked';
  return '';
}
/** SCC stale banner (1459-1470): the stored flag, or a counted record of the month that was posted (or created) after the feed. */
export function expFeedStale(s:State,month:string):boolean{
  const f=expSlice(s).months?.[month];if(!f)return false;if(f.stale)return true;
  return s.expenses.some(e=>counts(e)&&expYm(e.date)===month&&(e.decidedAt??e.createdAt??'')>f.generatedAt);
}

// ── Budgets (SCC renderBudget 1272-1290, budgetAlertFor 1297-1309, copyBudgets 1327-1340) ──
export type ExpBudgetRow={category:string;budget:number;actual:number;pct:number;status:'none'|'over'|'near'|'ok'};
/** Σ counted expenses of a category in a month (SCC: Σ non-void expense rows; HCC counts after approval). */
export function expCategorySpend(s:State,category:string,month:string):number{
  let t=0;for(const e of s.expenses)if(counts(e)&&!e.capital&&e.category===category&&expYm(e.date)===month)t+=e.amount;return t; // ACC-2: capital purchases never count against a budget (contract §3)
}
/** SCC's tab status: pct = round(actual ÷ budget × 100); "over" when pct > 100, "near limit" when pct ≥ 85, else ok; no budget → "no budget". */
export function expBudgetRow(category:string,budget:number,actual:number):ExpBudgetRow{
  const pct=budget?Math.round(actual/budget*100):0;
  return {category,budget,actual,pct,status:!budget?'none':pct>100?'over':pct>=EXP_BUDGET_NEAR?'near':'ok'};
}
export function expBudgetRows(s:State,month:string,categories:string[]):ExpBudgetRow[]{
  const bm=expSlice(s).budgets?.[month]??{},spend:Record<string,number>={};
  for(const e of s.expenses)if(counts(e)&&!e.capital&&expYm(e.date)===month)spend[e.category]=(spend[e.category]??0)+e.amount; // ACC-2: not capital purchases
  const cats=[...categories];for(const c of [...Object.keys(bm),...Object.keys(spend)])if(!cats.includes(c))cats.push(c);
  return cats.map(c=>expBudgetRow(c,Number(bm[c])||0,spend[c]??0));
}
/**
 * SCC budgetAlertFor: only for the current month (HCC: the business month) and a budget > 0; pct = round(spend ÷ budget × 100); below 90
 * silent; "<head> now <pct>% of <Mon> budget", error at ≥ 100, else a warning. `extra` = an amount not counted yet (the expense just
 * submitted, counted once approved) — the alert then says "would be … once approved".
 */
export function expBudgetAlert(s:State,category:string,date:string,extra=0):{msg:string;style:'warn'|'err';pct:number}|null{
  const m=expYm(date);if(m!==expYm(s.date))return null;
  const bg=Number(expSlice(s).budgets?.[m]?.[category])||0;if(!(bg>0))return null;
  const ac=expCategorySpend(s,category,m)+extra,pct=Math.round(ac/bg*100);
  if(pct<EXP_ALERT_WARN)return null;
  const mon=EXP_MONTHS[+m.slice(5,7)-1]??m;
  return {msg:extra?`${category} would be ${pct}% of ${mon} budget once approved`:`${category} now ${pct}% of ${mon} budget`,style:pct>=EXP_ALERT_ERROR?'err':'warn',pct};
}
/** SCC copyBudgets plan: heads with a value > 0 last month and none > 0 this month. Returns the heads, or SCC's message when nothing to copy. */
export function expBudgetCopyPlan(s:State,month:string):{prev:string;heads:string[];message?:string}{
  const prev=expPrevMonth(month),b=expSlice(s).budgets??{},src=b[prev]??{},bm=b[month]??{};
  if(!Object.keys(src).some(c=>Number(src[c])>0))return {prev,heads:[],message:`No budgets found for ${prev}`};
  const heads=Object.keys(src).filter(c=>Number(src[c])>0&&!(Number(bm[c])>0));
  if(!heads.length)return {prev,heads,message:`Every head already has a budget for ${month}`};
  return {prev,heads};
}

// ── Recurring templates (SCC P1-16, 680-884) ──
/** SCC recPostedMap (708): 'rec:<id>:<YYYY-MM>' → entries. HCC: a void, rejected or reversed post (and reversal records) does not count. */
export function expRecPosted(s:State,month:string):Record<string,number>{
  const map:Record<string,number>={};
  for(const e of s.expenses)if(!e.reversalOf&&e.status!=='Void'&&e.status!=='Rejected'&&e.status!=='Reversed'&&typeof e.sourceRef==='string'&&e.sourceRef.startsWith('rec:')&&e.sourceRef.endsWith(':'+month))
    map[e.sourceRef]=(map[e.sourceRef]??0)+1;
  return map;
}
export const expRecRef=(id:string,month:string)=>`rec:${id}:${month}`;
/** SCC recurringDue (718-725): active, not posted this month, and the day of the month ≥ min(due day, days in the month) — rent on day 31 is due on 28/29 Feb. */
export function expRecurringDue(s:State):ExpRecTpl[]{
  const m=expYm(s.date),day=+s.date.slice(8,10),dim=expDaysInMonth(m),posted=expRecPosted(s,m);
  return (expSlice(s).recurring??[]).filter(t=>t.active&&!posted[expRecRef(t.id,m)]&&day>=Math.min(t.dueDay,dim));
}
/** SCC recurringCard status pill (738-742). */
export function expRecStatus(s:State,t:ExpRecTpl):{kind:'inactive'|'posted'|'due'|'upcoming';count:number;day:number}{
  const m=expYm(s.date),day=+s.date.slice(8,10),eff=Math.min(t.dueDay,expDaysInMonth(m)),got=expRecPosted(s,m)[expRecRef(t.id,m)]??0;
  return {kind:!t.active?'inactive':got?'posted':day>=eff?'due':'upcoming',count:got,day:eff};
}
/** SCC normRecTpl due day (682): a whole day 1–31, anything else → 1. */
export function expDueDay(v:unknown):number{const d=Math.floor(Number(v));return d>=1&&d<=31?d:1;}

// ── Petty cash (SCC renderPetty 1203-1224) ──
export type ExpPetty={float:number;disbursed:number;balance:number;pending:number;pendingTopUps:number;status:'Replenish soon'|'Healthy'};
/**
 * SCC: disbursed = Σ petty rows (all time), balance = float − disbursed, "Replenish soon" when balance < 15 % of the float (float > 0).
 * HCC: the float = Σ APPROVED top-ups; a disbursement is a drawer-cash expense that counts once approved (pending ones shown apart).
 */
export function expPetty(s:State):ExpPetty{
  const entries=expSlice(s).petty??[];
  const float=entries.filter(e=>e.status==='Approved').reduce((a,e)=>a+e.amount,0);
  const pendingTopUps=entries.filter(e=>e.status==='Pending').reduce((a,e)=>a+e.amount,0);
  let disbursed=0,pending=0;
  for(const e of s.expenses)if(e.source==='petty'){if(counts(e))disbursed+=e.amount;else if(e.status==='Pending'||e.status==='Draft')pending+=e.amount;}
  const balance=float-disbursed;
  return {float,disbursed,balance,pending,pendingTopUps,status:balance*100<EXP_PETTY_LOW_PCT*float&&float?'Replenish soon':'Healthy'};
}
/** SCC pettyOut description (1236): "Petty: <for, or sundry>". */
export const expPettyDescription=(what?:string)=>'Petty: '+(String(what??'').trim()||'sundry');

// ── Udhaar credit register (SCC Wave 3, 1547-1651) ──
export const expUdPaid=(r:ExpUdReceivable)=>r.settlements.reduce((a,x)=>a+x.amount,0);
/** SCC udOut: amount − paid, floored at 0. */
export const expUdOut=(r:ExpUdReceivable)=>{const o=r.amount-expUdPaid(r);return o>0?o:0;};
/** SCC udDaysSince: whole days from the credit date to today (HCC: the business date). */
export const expUdDaysSince=(date:string,today:string)=>Math.round((Date.parse(today+'T00:00:00Z')-Date.parse(date+'T00:00:00Z'))/86400000);
/** SCC udBucket: ≤ 30 days "0–30", ≤ 60 "31–60", else "60+". */
export function expUdBucket(r:ExpUdReceivable,today:string):'0–30'|'31–60'|'60+'{const d=expUdDaysSince(r.date,today);return d<=30?'0–30':d<=60?'31–60':'60+';}
/** SCC udOverdue: a due date and today after it. */
export const expUdOverdue=(r:ExpUdReceivable,today:string)=>!!r.dueDate&&today>r.dueDate;
/** SCC payReceivable (1586-1587): more than outstanding + ₹0.50 is capped to the outstanding; up to ₹0.50 over is taken as typed. */
export function expUdPayment(r:ExpUdReceivable,amount:number):{amount:number;capped:boolean}{
  const out=expUdOut(r);return amount>out+EXP_UD_TOLERANCE?{amount:out,capped:true}:{amount,capped:false};
}
/** SCC addReceivable mobile (1566, 1569): digits only, kept only when ≥ 10 digits (the last 10). */
export function expUdMobile(v:unknown):string{const m=String(v??'').replace(/\D/g,'');return m.length>=10?m.slice(-10):'';}
// ── ACC-1 · Udhaar credit raised (contract §4) ──
export const EXP_UD_KINDS:{value:ExpUdKind;label:string}[]=[{value:'restaurant',label:'Restaurant bill'},{value:'room',label:'Room folio'},{value:'other',label:'Other'}];
/** Kind of a credit (rows raised before ACC-1: 'other'). */
export const expUdKind=(r:Pick<ExpUdReceivable,'kind'>):ExpUdKind=>r.kind==='restaurant'||r.kind==='room'?r.kind:'other';
/** Revenue mapping key of a credit's kind: restaurant → pos:sales (F&B), room → line:room, other → line:service. */
export const expUdRevenueKey=(r:Pick<ExpUdReceivable,'kind'>)=>({restaurant:'pos:sales',room:'line:room',other:'line:service'} as const)[expUdKind(r)];
/** GST inside a credit's gross amount (the amount the customer owes is tax-inclusive): taxable = gross × 10000 ÷ (10000 + bps) rounded half
 *  away from zero (as gstLineTax), tax = gross − taxable, split CGST / SGST as gstSplit (SGST = half toward zero, CGST takes the odd paisa).
 *  bps 0 / absent (rows before ACC-1) = no GST. Server: SourceRules.UdhaarCredit (same arithmetic). */
export function expUdGst(r:Pick<ExpUdReceivable,'amount'|'gstBps'>):{taxable:number;tax:number;cgst:number;sgst:number}{
  const g=r.amount,b=Math.max(0,Math.trunc(Number(r.gstBps)||0));if(!b)return {taxable:g,tax:0,cgst:0,sgst:0};
  const n=Math.abs(g)*10000,d=10000+b,q=Math.floor(n/d),rem=n-q*d,t=q+(rem*2>=d?1:0),taxable=g<0?-t:t,tax=g-taxable,sgst=Math.trunc(tax/2)||0;
  return {taxable,tax,cgst:tax-sgst,sgst};
}
/** Open credits (not written off, outstanding > 0), oldest first; totals by SCC's ageing buckets. */
export function expUdOpen(s:State):{open:ExpUdReceivable[];total:number;buckets:Record<'0–30'|'31–60'|'60+',number>}{
  const open=(expSlice(s).udhaar??[]).filter(r=>!r.void&&expUdOut(r)>0).sort((a,b)=>String(a.date).localeCompare(String(b.date)));
  const buckets={'0–30':0,'31–60':0,'60+':0};let total=0;
  for(const r of open){const o=expUdOut(r);total+=o;buckets[expUdBucket(r,s.date)]+=o;}
  return {open,total,buckets};
}
/** SCC remindReceivable text (1604) with the hotel's name instead of SCC's firm and town. */
export function expUdReminder(hotel:string,r:ExpUdReceivable):string{
  return 'Namaste '+r.name+',\nA gentle reminder from '+hotel+' regarding your pending balance of '+money(expUdOut(r))+(r.dueDate?' (due '+r.dueDate+')':'')+'.\nKindly clear it at your convenience. Thank you.';
}

// ── CSV (SCC exportLedgerCSV 657-663, exportMonthCSV 1517-1522, dlCSV 664-665) ──
export const EXP_LEDGER_COLUMNS=['Date','Firm','Type','Category','Mode','Vendor','Description','Amount','Source','Void','VoidReason','CreatedBy','Store','GSTAmt','GSTRate%'];
export const EXP_MONTH_COLUMNS=['Date','Firm','Type','Category','Mode','Vendor','Description','Amount','Source','GSTAmt','GSTRate%'];
/** HCC columns after SCC's (the HCC record id and status — SCC has no approval status in its file; DEC-135: ITC allowed / disallowed of the bill's GST). */
export const EXP_HCC_COLUMNS=['Ref','Status','ITC'];
/** The ITC cell: allowed / disallowed for a bill with GST, empty without GST. */
const itcCell=(e:Expense)=>e.gstPaise?(e.itc==='allowed'?'allowed':'disallowed'):'';
const csvLine=(r:(string|number)[])=>r.map(v=>csvMinimal(v)).join(',');
const byDate=(a:Expense,b:Expense)=>a.date<b.date?-1:a.date>b.date?1:0;
/** Firm = the hotel (one legal entity); CreatedBy = the user's name; Store = Unassigned (HCC has one shared drawer, DEC-052). */
function sccRow(s:State,e:Expense,who:(id:string)=>string,full:boolean):(string|number)[]{
  const base:(string|number)[]=[e.date,s.settings.hotel||'Unassigned','expense',e.category,e.method,e.vendor??'',expDescription(e),expRupees(e.amount),e.source??'manual'];
  const tail=[e.gstPaise?expRupees(e.gstPaise):'',e.gstRateBps?expPct(e.gstRateBps):''];
  return full?[...base,e.status==='Void'?'Y':'',e.voidReason??'',who(e.actor),'Unassigned',...tail,e.id,e.status,itcCell(e)]:[...base,...tail,e.id,e.status,itcCell(e)];
}
/** SCC ledger CSV: every record (void ones too), date ascending, file ledger_<business date>.csv. */
export function expLedgerCsv(s:State,who:(id:string)=>string=id=>s.users.find(u=>u.id===id)?.name??id):{name:string;csv:string;rows:number}{
  const rows=s.expenses.slice().sort(byDate).map(e=>sccRow(s,e,who,true));
  return {name:`ledger_${s.date}.csv`,csv:[csvLine([...EXP_LEDGER_COLUMNS,...EXP_HCC_COLUMNS]),...rows.map(csvLine)].join('\n'),rows:rows.length};
}
/** SCC month CSV: the month's counted records (SCC: non-void rows), date ascending, file expense_month_<YYYY-MM>.csv. */
export function expMonthCsv(s:State,month:string):{name:string;csv:string;rows:number}{
  const rows=s.expenses.filter(e=>counts(e)&&expYm(e.date)===month).sort(byDate).map(e=>sccRow(s,e,()=>'',false));
  return {name:`expense_month_${month}.csv`,csv:[csvLine([...EXP_MONTH_COLUMNS,...EXP_HCC_COLUMNS]),...rows.map(csvLine)].join('\n'),rows:rows.length};
}

// ── Ledger filters (SCC applyLF 407-417) ──
export const EXP_LEDGER_CAP=200;
/** SCC filters: mode, month and a search over description + vendor + category; newest first. (SCC's income/expense type does not apply.) */
export function expLedgerFilter(s:State,f:{mode?:string;month?:string;q?:string;status?:string}):Expense[]{
  const q=String(f.q??'').trim().toLowerCase();
  return s.expenses.filter(e=>(!f.mode||e.method===f.mode)&&(!f.month||expYm(e.date)===f.month)&&(!f.status||f.status==='All'||e.status===f.status)
    &&(!q||(expDescription(e)+' '+(e.vendor??'')+' '+e.category).toLowerCase().includes(q)))
    .map((e,i)=>({e,i})).sort((a,b)=>a.e.date!==b.e.date?(a.e.date<b.e.date?1:-1):b.i-a.i).map(x=>x.e);
}
/** SCC shareEntry text (62-72), copied to the clipboard. */
export function expEntryText(e:Expense):string{
  const lines=['Expense · '+e.date,'Category: '+(e.category||'—'),'Amount: '+money(e.amount),'Mode: '+(e.method||'—')];
  if(e.vendor)lines.push('Vendor: '+e.vendor);if(e.description)lines.push('Note: '+e.description);
  if(e.status==='Void')lines.push('(VOID'+(e.voidReason?' — '+e.voidReason:'')+')');
  return lines.join('\n');
}
