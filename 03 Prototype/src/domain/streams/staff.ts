// Module 17 · HR & payroll — pure helpers, selectors and the state slice type. Exported names MUST start with 'hr' / 'HR' (shared mock namespace).
// Wave 0.5 (W05-B): payroll and leave RULES ported from SCC (read-only sources, rules re-written in HCC patterns; integer paise):
//   SCC payroll — E:\Projects\HCC\Migration data on 25 oct\HCC-Migration-2026-10-25\reference-inputs\SCC-modules\payroll\index.html
//     calcAttendance (3 late marks = ½ day, half-day = ½ day loss of pay) · calcGM (structured pay: basic 50% of earned, HRA 80% of basic,
//     washing 10% of basic, special = rest; PF 12% employee / 13% employer on min(basic, ₹15,000); ESIC 0.75% / 3.25% on earned − washing
//     when earned ≤ ₹21,000; overtime at the ordinary rate) · ptFor (Maharashtra PT slabs by gender, ₹300 in February) · advScheduled
//     (monthly instalment, 0 = full balance) · lockRun / unlockRun · reportBankCsv / reportStatutoryCsv / statutoryMemberRows ·
//     waEmpMessage (WhatsApp text) · fnfAssemble / fnfCompose (full and final) · slipWords (amount in words).
//   SCC leave — ...\reference-inputs\SCC-modules\leave\index.html — IN FULL since DEC-116 / DEC-119 (27 Oct 2026; report
//     05 Delivery Evidence\SCC Port\SCC_Leave_Port_2026-10-27.md): one leave record per DATE (full / AM / PM, decided one by one), getCapacity /
//     canAddLeave (MAX_DAY 3 records per date for the whole hotel, MAX_SHIFT 4), ENT_DEFAULTS CL 12 · SL 7 · EL 15 per FY with per-staff
//     overrides (entFor), fyUsage (approved only, by date), requestedLeaveDays + isWeekOffDate (one hotel-wide week-off set), blackouts and
//     store holidays, per-store advisory caps (per department here). HCC safeguards kept (DEC-119): nobody approves their own leave, a
//     cancelled leave stays as an audit record, role permissions (DEC-058), staff keyed by ID, past days through DEC-075 corrections.
// DEC-115 (owner, 27 Oct 2026: "import the entire payroll rules from SCC"; replaces DEC-099): pay days follow SCC calcAttendance exactly
//   (hrSccDays) — total days = calendar days (or the rate version's override) − paid holidays of the Holiday calendar (SCC "Mgmt holidays");
//   absent = total − present − approved paid leave − half days (SCC's DSR/Leave feed; a day worked on the weekly off is present), so a weekly
//   off taken is an absent day; deduction =
//   absent + ½ per half day + ½ per 3 late marks; +4 extra days (2 when the deduction is 6–6.5, none from 7); salary days = min(total,
//   total − deduction + leave + extra), the surplus = OT days paid at gross ÷ total days (no per-minute overtime). ESIC month by month on
//   earned pay. Daily-wage staff: gross = daily rate × total days. Leaves above 5.5 days or a deduction ≥ total days block the lock (SCC risk).
// Statutory values are DATED settings (effective-from) and marked "Illustrative — CA to confirm for a hotel". Fictional staff only.
import type {State,Role} from '../types';
import {addDays} from '../money';
import {can} from '../selectors';
import {hrShiftPattern,hrWeeklyOffAuto,hrHolidayPaid,hrHolidayOn,hrHolidays,hrLeavePaid,hrLeaveQuota,HR_SHIFT_SEED} from './people-masters';
import {mstStored} from './masters';

/** Shift pattern code (master list Shift patterns, MA-6; M E N G are the seed) or 'OFF' for the weekly off. */
export type HRShiftCode=string;
/** NT = present without a thumb punch (SCC "No Thumb": ½ day deduction, DEC-115). */
export type HRAttStatus='P'|'L'|'H'|'A'|'OFF'|'NT';
/** Leave type code (master list Leave types, MA-6; CL SL EL CO ML LOP are the seed). */
export type HRLeaveType=string;
export type HRHalf='AM'|'PM';
export type HRGender='M'|'F';
/** S = structured (basic / HRA / washing / special, SCC 'S'); B = basic only (SCC 'B'). */
export type HRStructure='S'|'B';
export type HRDept={id:string;name:string};
export type HRStaff={id:string;name:string;dept:string;designation:string;
  /** Linked demo user (login); undefined = non-login staff (cooks, security…). */
  userId?:string;
  /** MA-6: code of the designation master row chosen on the staff form; `designation` keeps the printed name (older records: matched by name). */designationCode?:string;
  payType:'monthly'|'daily';
  /** Monthly salary (paise) — monthly pay type. */ monthly:number;
  /** Daily wage (paise) — daily pay type. */ daily:number;
  defaultShift:Exclude<HRShiftCode,'OFF'>;
  /** Weekly off: 0 = Sunday … 6 = Saturday. */ weeklyOff:number;
  joined:string;phone:string;active:boolean;exitedOn?:string;
  /** Professional tax slab (MH). Default 'M' as in SCC. */ gender?:HRGender;
  /** Default: monthly → 'S', daily → 'B'. */ structure?:HRStructure;
  /** Default (SCC migration rule): PF only for structured pay. */ pfApplicable?:boolean;
  /** Default true; ESIC still applies only when earned ≤ ceiling. */ esicApplicable?:boolean;
  /** Fictional bank details for the bank-transfer file. */ bankName?:string;accountNo?:string;ifsc?:string;
  /** Production build only (W4 G2): the server says the stored mobile is a usable WhatsApp number while `phone` stays masked.
   *  Never set by the prototype (its `phone` is the real fictional number, checked with hrWaNumber). */ phoneUsable?:boolean};
export type HRAtt={status:HRAttStatus;at:string;otMin:number;by:string;note?:string};
/** SCC leave record — ONE PER DATE (SCC data.leaves[date][]), decided on its own. `type` = the category (SCC's six + HCC's LOP). */
export type HRLeave={id:string;staffId:string;
  /** The leave date (SCC data.leaves key). */ date:string;
  type:HRLeaveType;
  /** SCC leaveFrom / leaveTo: the date range of the request this day came from. */ from:string;to:string;
  /** SCC leaveDaysOf: 1 = full day, 0.5 = half day. */ days:number;
  /** SCC: optional, trimmed, up to 500 characters ('' = none). */ reason:string;
  /** Half day (SCC half_day_am "Morning (till 2 PM)" / half_day_pm "Afternoon (after 5 PM)"); absent = full day. */ half?:HRHalf;
  /** SCC approvedBy: the nominated approver — user ID of the Owner or a Manager; `approverName` = an imported SCC name that matches no user. */
  approver?:string;approverName?:string;
  status:'Pending'|'Approved'|'Rejected'|'Cancelled';requestedBy:string;requestedAt:string;decidedBy?:string;decidedAt?:string;note?:string;
  /** SCC remove (×) = cancel with an audit record (DEC-119): who and when; the record is hidden from the calendar. */ cancelledBy?:string;cancelledAt?:string;
  /** DEC-075: a back-dated one-day leave created by this attendance correction (request id). */ correctionId?:string;
  /** Imported SCC record without a status: SCC counts it as approved ("legacy record"). */ legacy?:boolean;
  /** Imported SCC record: its id in the file (an import skips a record whose id is already here) and SCC's decidedBy text. */ sccId?:string;decidedByName?:string};
/** SCC blackout / store-holiday entry (W12 P1-29). `dept` '' = every department (SCC "All stores"), else one department (SCC per-store scope). */
export type HRBlackout={id:string;from:string;to:string;dept:string;kind:'blackout'|'holiday';
  /** Refuses the leave (blackouts only; store holidays never block). */ hard:boolean;label:string};
/** SCC agenda item (meeting / agenda / reminder) on a date. */
export type HRAgenda={id:string;date:string;title:string;
  /** HH:MM or '' (SCC sorts blank times last). */ time:string;type:'meeting'|'agenda'|'reminder';description:string;by:string;at:string};
export type HRAdvance={id:string;staffId:string;amount:number;
  /** Monthly recovery; 0 = recover the full balance in the first month (SCC emi 0). */ installment:number;
  recovered:number;date:string;reason:string;by:string;recoveries:{runId:string;amount:number}[];
  /** First month to recover from (YYYY-MM). Default: month of `date`. */ fromMonth?:string;
  /** ACC-2 (coordinator ruling 1): the payment method it was paid by (Cash = the drawer → 1000/<till>; a bank method → 1030/<bank>). Absent on
   *  advances given before ACC-2 — those were simulated bank transfers (ACC_PAYROLL_FUNDING). */ funding?:string};
export type HRRounding='paise'|'rupee';
/** Dated statutory rates (one version per effective-from date). bps = basis points (1% = 100); money in paise. */
export type HRRates={from:string;
  basicBps:number;hraBps:number;washBps:number;
  pfEeBps:number;pfErBps:number;pfWageCap:number;
  esicEeBps:number;esicErBps:number;esicCeiling:number;
  ptMaleExempt:number;ptMaleMid:number;ptMaleMidAmt:number;ptStd:number;ptFeb:number;ptFemaleExempt:number;
  /** OT-day rate as bps of a day's pay (SCC: 10000 = gross ÷ total days). */ otBps:number;
  /** Late marks per ½-day deduction (SCC: 3; 0 = off). */ lateBlock:number;
  /** paise = SCC (round to 0.01); rupee = PF nearest rupee, ESIC next higher rupee. */ rounding:HRRounding;
  /** F-07 ESIC contribution-period rule (Apr–Sep / Oct–Mar): coverage is decided once per period from the contractual wage and then
   *  continues to the period end even if pay crosses the ceiling. Absent/false = SCC month-by-month test on earned pay (the default, DEC-115). */
  esicPeriod?:boolean;
  /** SCC "Days Override": total days of every month from this version on (half days allowed); absent/0 = calendar days. */ daysOverride?:number};
/** SCC leave settings. `ent` = ENT_DEFAULTS as saved (any number ≥ 0, fractions allowed, no maximum). */
export type HRLeavePolicy={ent:{CL:number;SL:number;EL:number};
  /** SCC perEmployee entitlement overrides, keyed by staff ID (only the categories set; the rest use `ent`). */ perStaff?:Record<string,HREnt>;
  /** SCC week-off set (0 = Sunday … 6 = Saturday): dates skipped when leave is recorded and counted. Absent = HR_LEAVE_WEEKOFF_DEFAULT. */ weekOff?:number[];
  /** SCC per-store caps, per department here (whole number ≥ 0; absent = HR_MAX_DAY). ADVISORY: shown as over capacity, never blocks. */ deptCap:Record<string,number>;
  /** Before the SCC port: the blocking per-department default. No longer read (kept on old saves). */ defaultCap?:number;
  /** SCC blackouts and store holidays. */ blackouts?:HRBlackout[]};
export type HREnt={CL?:number;SL?:number;EL?:number};
export type HRCounts={ym:string;days:number;P:number;L:number;H:number;
  /** Half-day worked with the other half on approved half-day leave. */ HL:number;
  A:number;OFFW:number;off:number;paidLeave:number;lopLeave:number;unmarked:number;na:number;otMin:number;
  /** DEC-083 / MA-6: paid holidays (Holiday calendar) not worked — on days with no attendance, leave or weekly off; present only when > 0. */hol?:number;
  /** DEC-115: paid holidays of the Holiday calendar in the month = SCC "Mgmt holidays" (they shorten the total days); present only when > 0. */mgmtHol?:number;
  /** Days counted when the month was read only up to a date (SCC caps the in-progress month by elapsed days); absent = the whole month. */elapsed?:number;
  /** DEC-115: present days without a thumb punch (SCC No Thumb); present only when > 0. */NT?:number};
/** DEC-115: SCC calcAttendance for one staff member's month (days; halves allowed). */
export type HRSccDays={total:number;present:number;half:number;leave:number;absent:number;late:number;
  lateDed:number;absentDed:number;halfDed:number;deduction:number;extra:number;finalPresent:number;salaryDays:number;otDays:number;
  /** No-thumb marks and their deduction (½ each); absent on lines saved before they were counted. */noThumb?:number;noThumbDed?:number};
/** DEC-083 paid holiday in the Wave B shape (date + name) — a view of an active "paid" row of MA-6's Holiday calendar master (one holidays concept). */
export type HRPaidHoliday={date:string;name:string};
/** DEC-075: what a past day holds for pay — nothing (unmarked = absent), an attendance mark, or a one-day approved leave. */
export type HRDayEntry={kind:'none'}|{kind:'mark';status:HRAttStatus;otMin:number}|{kind:'leave';type:HRLeaveType};
/** DEC-075 attendance correction / DEC-084 bank-detail change (server hr.change_request). Pending until the Owner decides; `direct` = applied at
 *  once (a correction that lowers or keeps pay, or the Owner's own). `payEffect` = gross pay change of the month (paise; payroll roles only). */
export type HRRequest={id:string;kind:'attendance'|'bank';staffId:string;
  /** Attendance: the corrected day, and the entry before → after. */ date?:string;before?:HRDayEntry;after?:HRDayEntry;
  /** Bank: the staff master's details at request time → the requested ones (production: account numbers masked). */ bankBefore?:HRBank;bankAfter?:HRBank;
  payEffect?:number;reason:string;status:'Pending'|'Approved'|'Rejected'|'Withdrawn';direct?:boolean;
  requestedBy:string;requestedAt:string;decidedBy?:string;decidedAt?:string;note?:string};
export type HRLine={staffId:string;name:string;dept:string;designation:string;payType:'monthly'|'daily';structure:HRStructure;gender:HRGender;base:number;
  counts:HRCounts;latePenaltyHalf:number;payableDays:number;
  /** DEC-115: the SCC day sheet behind payableDays (= salaryDays) and the OT days; absent on lines saved before the SCC port. */ days?:HRSccDays;
  /** Earned (pro-rated) pay = SCC grossPayable. */ earned:number;basic:number;hra:number;washing:number;special:number;ot:number;
  /** earned + overtime. */ gross:number;
  pfWages:number;pf:number;pfEr:number;esicWages:number;esic:number;esicEr:number;pt:number;advance:number;deductions:number;net:number;
  /** F-07: ESIC coverage for this month under the contribution-period rule (absent when the month-by-month rule applied). */ esicCovered?:boolean;
  /** Employer cost = gross + PF employer + ESIC employer. */ ctc:number};
export type HRSend={staffId:string;to:string;at:string;by:string};
export type HRBankFile={at:string;by:string;net:number;
  /** ACC-2 (contract §8): the business date the file was generated (the net-pay payment voucher's date; absent on older files → `at`). */ on?:string;
  /** ACC-2 integration (ruling 6): the date its net-pay voucher is booked on, fixed when the file is made — the pay period's last day when
   *  that day is not locked yet, else the business date of the file. Absent on older files → the period's last day rule (accPayrollDate). */ bookOn?:string};
export type HRUnlock={at:string;by:string;reason:string;
  /** Payslips sent before this unlock — superseded. */ sent?:HRSend[];
  /** Bank files generated before this unlock (already given to the bank). */ bankFiles?:HRBankFile[];
  /** Net pay per staff ID in the lock that was undone. */ nets?:Record<string,number>;
  /** ACC-2 (contract §8): business date of the unlock (the pay-run reversal voucher's date), the business date the undone lock was posted, and
   *  the figures that lock booked (its PayRun voucher, reversed on `on`). Absent on unlocks recorded before ACC-2. */ on?:string;lockedOn?:string;book?:HRRunBook;
  /** ACC-2 integration (ruling 6): the date the undone lock's PayRun voucher was booked on (HRRun.lockedBookOn of that lock). */ lockedBookOn?:string};
/** Bank details as frozen in a locked run (F-02: the bank file pays where the checker approved, not the live staff master). */
export type HRBank={bankName:string;accountNo:string;ifsc:string};
export type HRRun={id:string;month:string;status:'Draft'|'Locked';rates:HRRates;lines:HRLine[];warnings:string[];
  preparedBy:string;preparedAt:string;lockedBy?:string;lockedAt?:string;
  /** ACC-2 (contract §8): business date of the current lock (the PayRun voucher's date; absent on older runs → `lockedAt`). */ lockedOn?:string;
  /** ACC-2 integration (ruling 6): the date the current lock's PayRun voucher is booked on, fixed at the lock — the pay period's last day when
   *  that day is not locked yet, else the business date of the lock. Absent on older runs → the period's last day rule (accPayrollDate). */ lockedBookOn?:string;
  /** Bank details per staff ID, copied at lock (F-02). Absent on runs locked before the snapshot existed. */ bank?:Record<string,HRBank>;
  /** Simulated WhatsApp payslip sends. */ sent?:HRSend[];
  /** Owner unlocks (SCC unlockRun): recoveries reversed, back to Draft. F-09: the payslip sends, bank files and net figures of the
   *  lock that was undone are kept here (superseded), so a relock shows who must get a new payslip and that a bank file was already issued. */
  unlocks?:HRUnlock[];
  /** F-09: bank transfer files generated for the current lock (simulated). */ bankFiles?:HRBankFile[];
  /** F-04: run saved before W05-B (illustrative rules), migrated on restore; employer contributions and the pay split were not recorded. */ legacy?:boolean};
export type HRFnfAdj={label:string;kind:'add'|'ded';amount:number};
export type HRFnf={id:string;staffId:string;name:string;lastDay:string;month:string;
  /** False when the exit month was already paid in a locked run. */ salaryIncluded:boolean;line?:HRLine;rates:HRRates;
  advanceOutstanding:number;adjustments:HRFnfAdj[];earnings:number;recoveries:number;
  /** May be negative: recoverable from the employee. */ net:number;by:string;at:string;
  /** F-12 maker-checker: Draft until another payroll user confirms (absent = settled before the rule existed). */ status?:'Draft'|'Settled';
  confirmedBy?:string;confirmedAt?:string;
  /** ACC-2 (contract §8): business date of the confirmation (the F&F vouchers' date; absent on older settlements → `confirmedAt` / `at`). */ confirmedOn?:string};
/** F-12 limits (illustrative): each adjustment ≤ ₹2,00,000; additions above ₹1,00,000 in total need the Owner as checker. */
export const HR_FNF_ADJ_MAX=20000000,HR_FNF_OWNER_ABOVE=10000000;
export const hrFnfSettled=(f:HRFnf)=>f.status!=='Draft';
/** State slice for this module, stored at s.staffX (optional; absent in old saves → treat as empty). */
export type HRState={depts:HRDept[];staff:HRStaff[];
  /** Roster overrides keyed "YYYY-MM-DD|staffId". */ roster:Record<string,HRShiftCode>;
  /** Attendance keyed "YYYY-MM-DD|staffId". */ att:Record<string,HRAtt>;
  leaves:HRLeave[];advances:HRAdvance[];
  /** Dated statutory rate versions, sorted by `from`. */ rateVersions:HRRates[];
  leavePolicy?:HRLeavePolicy;runs:HRRun[];fnf?:HRFnf[];
  /** LEGACY (Wave B, before the masters merge): its paid-holiday list. A SCHEMA 8 save is migrated into MA-6's Holiday calendar master on load
   *  (services/mock/persistence.ts); nothing reads it otherwise. */ holidays?:HRPaidHoliday[];
  /** DEC-075 / DEC-084 requests, oldest first (absent = none). */ requests?:HRRequest[];
  /** SCC agenda items (meetings, agenda, reminders) by date (absent = none). */ agendas?:HRAgenda[];
  /** Production only: the server's approver choices (Owner + Manager users); the prototype reads its demo users (hrApproverChoices). */
  approvers?:{id:string;name:string;role:string}[];
  /** Production only: the server's open correction window (hrCorrectionWindow computes it in the prototype). */ correctWindow?:{from:string;to:string}};

/** DEC-115: shown on the leave-policy and statutory-rates screens (the SCC pay-day rule, in place of F-08's "not ported" note). */
export const HR_SCC_DAYS='Pay days as in SCC: total days (calendar days, or the override, less paid holidays) − deductions (absent days, ½ per half day, ½ per no-thumb mark, ½ per 3 late marks) + approved leave + 4 extra days (2 when the deduction is 6–6.5 days, none from 7). Salary days stop at the total days; the rest are OT days paid at gross ÷ total days. A weekly off taken counts as an absent day — the extra days pay for it. More than 5.5 leave days in a month, or a deduction of all the days, blocks the lock.';
export const HR_DISCLAIMER='Illustrative — CA to confirm for a hotel. PF, ESIC and Maharashtra PT rules ported from SCC as dated rates; not statutory advice. Fictional staff.';
/** A04-11: shown on payslips and full-and-final documents (server PayrollRates.NotCalculated). */
export const HR_NOT_CALCULATED='Not calculated: gratuity, notice pay, bonus, TDS on salary.';
export const hrDefaultRates=():HRRates=>({from:'2025-04-01',basicBps:5000,hraBps:8000,washBps:1000,pfEeBps:1200,pfErBps:1300,pfWageCap:1500000,
  esicEeBps:75,esicErBps:325,esicCeiling:2100000,ptMaleExempt:750000,ptMaleMid:1000000,ptMaleMidAmt:17500,ptStd:20000,ptFeb:30000,ptFemaleExempt:2500000,
  otBps:10000,lateBlock:3,rounding:'paise',esicPeriod:false});
export const hrDefaultLeavePolicy=():HRLeavePolicy=>({ent:{...HR_ENT_DEFAULTS},deptCap:{}});
// ── SCC leave constants (leave\index.html) ──
/** SCC MAX_DAY: at most 3 leave records (pending + approved) on a date for the WHOLE hotel (SCC: the business); the binding cap. */
export const HR_MAX_DAY=3;
/** SCC MAX_SHIFT: at most 4 per shift (AM / PM). Ported as SCC has it, but MAX_DAY (3) always binds first, so it never decides (owner question). */
export const HR_MAX_SHIFT=4;
/** SCC ENT_DEFAULTS: Casual 12 · Sick 7 · Earned 15 days per April–March financial year. */
export const HR_ENT_DEFAULTS={CL:12,SL:7,EL:15};
/** SCC WEEKOFF_DEFAULT = [0, 6] (Sunday + Saturday). HCC's default is NO day (a 7-day hotel; SCC's own help says to untick both) — owner question. */
export const HR_SCC_WEEKOFF_DEFAULT=[0,6];
export const HR_LEAVE_WEEKOFF_DEFAULT:number[]=[];
/** SCC VALID_CATEGORIES (+ HCC's LOP, leave without pay, which payroll treats as absent — DEC-115). */
export const HR_SCC_CATEGORY:Record<string,string>={CL:'Casual Leave',SL:'Sick Leave',EL:'Earned Leave',ML:'Maternity / Paternity',CO:'Compensatory Off',OTH:'Other',LOP:'Leave without pay'};
export const hrEmpty=():HRState=>({depts:[],staff:[],roster:{},att:{},leaves:[],advances:[],rateVersions:[hrDefaultRates()],runs:[]});
export const hrSlice=(s:State):HRState=>s.staffX??hrEmpty();
/** For handlers: returns the slice, creating it on the (cloned) state if absent. */
export function hrEnsure(s:State):HRState{if(!s.staffX)s.staffX=hrEmpty();if(!s.staffX.rateVersions?.length)s.staffX.rateVersions=[hrDefaultRates()];return s.staffX;}

// ── F-04: saves made before W05-B (schema ≤ 7: `rates` instead of `rateVersions`, shorter run lines) ──
/** Pre-W05-B rate shape and its defaults (only values that differ from these are carried over as the owner's custom rates). */
type HRLegacyRates={basicBps:number;pfBps:number;pfCap:number;esicBps:number;esicCeiling:number;pt:number;ptThreshold:number;otBps:number;lateBlock:number};
const HR_LEGACY_DEFAULTS:HRLegacyRates={basicBps:5000,pfBps:1200,pfCap:1500000,esicBps:75,esicCeiling:2100000,pt:20000,ptThreshold:1000000,otBps:15000,lateBlock:3};
const okInt=(v:unknown):v is number=>Number.isSafeInteger(v)&&(v as number)>=0;
/** Old `rates` → one dated version from the first default date: SCC defaults, overlaid with every old value the owner had customised. */
export function hrRatesFromLegacy(old:Partial<HRLegacyRates>,from=hrDefaultRates().from):HRRates{
  const r={...hrDefaultRates(),from},custom=(k:keyof HRLegacyRates)=>okInt(old[k])&&old[k]!==HR_LEGACY_DEFAULTS[k]?old[k] as number:undefined;
  const set=<K extends keyof HRRates>(k:K,v:HRRates[K]|undefined)=>{if(v!==undefined)r[k]=v;};
  set('basicBps',custom('basicBps'));set('pfEeBps',custom('pfBps'));set('pfWageCap',custom('pfCap'));set('esicEeBps',custom('esicBps'));set('esicCeiling',custom('esicCeiling'));
  set('ptStd',custom('pt'));const th=custom('ptThreshold');if(th!==undefined&&th>0){r.ptMaleMid=th-1;r.ptMaleExempt=Math.min(r.ptMaleExempt,r.ptMaleMid-1);}
  set('otBps',custom('otBps'));const lb=old.lateBlock;if(okInt(lb)&&lb<=10&&lb!==HR_LEGACY_DEFAULTS.lateBlock)r.lateBlock=lb;
  return r;
}
/** Fills the fields a pre-W05-B run line lacks from what it did record (old `basic` = earned pay; no employer share or pay split). */
function hrLegacyLine(l:Partial<HRLine>&Record<string,unknown>,ym:string,st:HRStaff|undefined,r:HRRates):HRLine{
  const n=(v:unknown)=>Number.isFinite(v)?v as number:0;
  const gross=n(l.gross),ot=n(l.ot),earned=Number.isFinite(l.earned)?l.earned as number:Math.max(0,gross-ot),pf=n(l.pf),esic=n(l.esic);
  const c=(l.counts??{}) as Partial<HRCounts>;
  return {...l,staffId:String(l.staffId??''),name:String(l.name??''),dept:String(l.dept??''),designation:String(l.designation??''),payType:l.payType==='daily'?'daily':'monthly',
    structure:l.structure??'B',gender:l.gender??(st?hrGenderOf(st):'M'),base:n(l.base),
    counts:{ym,days:n(c.days)||hrDaysInMonth(ym),P:n(c.P),L:n(c.L),H:n(c.H),HL:n(c.HL),A:n(c.A),OFFW:n(c.OFFW),off:n(c.off),paidLeave:n(c.paidLeave),lopLeave:n(c.lopLeave),unmarked:n(c.unmarked),na:n(c.na),otMin:n(c.otMin)},
    latePenaltyHalf:n(l.latePenaltyHalf),payableDays:n(l.payableDays),earned,basic:Number.isFinite(l.basic)?l.basic as number:earned,hra:n(l.hra),washing:n(l.washing),special:n(l.special),ot,gross,
    pfWages:Number.isFinite(l.pfWages)?l.pfWages as number:pf?Math.min(hrMulDiv(earned,r.basicBps,1,10000),r.pfWageCap):0,pf,pfEr:n(l.pfEr),
    esicWages:Number.isFinite(l.esicWages)?l.esicWages as number:esic?gross:0,esic,esicEr:n(l.esicEr),pt:n(l.pt),advance:n(l.advance),deductions:n(l.deductions),net:n(l.net),
    ctc:Number.isFinite(l.ctc)?l.ctc as number:gross+n(l.pfEr)+n(l.esicEr)};
}
/** One-time migration for state saved before W05-B (run from restoreState for schema ≤ 7; a no-op on current-shape state, so it is idempotent).
 *  Old `rates` → `rateVersions` (custom values kept); old runs keep their figures, get a full rate snapshot and the missing line fields, marked legacy. */
export function hrMigrateLegacy(s:State):boolean{
  const x=s.staffX as (HRState&{rates?:Partial<HRLegacyRates>})|undefined;if(!x)return false;
  let changed=false;
  if(!x.rateVersions?.length){x.rateVersions=[x.rates?hrRatesFromLegacy(x.rates):hrDefaultRates()];changed=true;}
  if(x.rates){delete x.rates;changed=true;}
  for(const run of x.runs??[]){
    const rr=run.rates as Partial<HRRates>&Partial<HRLegacyRates>;
    const old=!rr||typeof rr.from!=='string'||run.lines.some(l=>!Number.isFinite((l as Partial<HRLine>).earned)||!Number.isFinite((l as Partial<HRLine>).pfEr)||typeof (l.counts as Partial<HRCounts>)?.ym!=='string');
    if(!old)continue;
    if(!rr||typeof rr.from!=='string')run.rates=hrRatesFromLegacy(rr??{});
    run.lines=run.lines.map(l=>hrLegacyLine(l as HRLine&Record<string,unknown>,run.month,x.staff.find(t=>t.id===l.staffId),run.rates));
    run.legacy=true;changed=true;
  }
  return changed;
}

/** Built-in shift table (the seed of the Shift patterns list; fallback when no state is at hand). */
export const HR_SHIFTS:{code:HRShiftCode;label:string;start:string;end:string}[]=[...HR_SHIFT_SEED.map(x=>({code:x.code,label:x.name,start:x.start,end:x.end})),{code:'OFF',label:'Weekly off',start:'',end:''}];
/** Label and times of a shift code; with the state, the Shift patterns list decides (retired patterns still resolve). */
export function hrShiftInfo(c:HRShiftCode,s?:State){
  const p=s?hrShiftPattern(s,c):undefined;if(p)return {code:c,label:p.name,start:p.start,end:p.end};
  return HR_SHIFTS.find(x=>x.code===c)??{code:c,label:c,start:'',end:''};
}
export const HR_ATT_LABEL:Record<HRAttStatus,string>={P:'Present',L:'Late',H:'Half-day',A:'Absent',OFF:'Worked on off day',NT:'No thumb punch'};
/** Every attendance mark a manager can record, in screen order. */
export const HR_ATT_MARKS:HRAttStatus[]=['P','L','H','A','OFF','NT'];
/** SCC's six leave categories in SCC's order (VALID_CATEGORIES) + HCC's LOP. The list is fixed (SCC: not editable). */
export const HR_LEAVE_TYPES:HRLeaveType[]=['CL','SL','EL','ML','CO','OTH','LOP'];
export const HR_LEAVE_LABEL:Record<HRLeaveType,string>={CL:'Casual leave',SL:'Sick leave',EL:'Earned leave',ML:'Maternity / paternity leave',CO:'Compensatory off',OTH:'Other',LOP:'Leave without pay'};
/** SCC TRACKED_CATS (Casual, Sick, Earned): quota and balance warning. Maternity / Comp-Off / Other (and LOP) are not quota-tracked. */
export const HR_TRACKED:('CL'|'SL'|'EL')[]=['CL','SL','EL'];
/** Every category is a leave day in pay (SCC); only HCC's LOP is unpaid (payroll: absent). */
export const hrPaidLeave=(t:HRLeaveType,s?:State)=>s?hrLeavePaid(s,t):t!=='LOP';
export const HR_GRACE_MIN=15;
export const HR_WEEKDAYS=['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
/** SCC leave-type labels (H695-697). */
export const HR_HALF_LABEL:Record<HRHalf,string>={AM:'Half Day — Morning (till 2 PM)',PM:'Half Day — Afternoon (after 5 PM)'};
/** SCC short labels: Full Day / Half AM / Half PM. */
export const hrKindLabel=(l:{half?:HRHalf})=>l.half==='AM'?'Half Day AM':l.half==='PM'?'Half Day PM':'Full Day';

// ── Exact integer arithmetic (half-up; BigInt so large products never lose precision) ──
const B=(n:number)=>BigInt(n);
/** round(a·b·c / den), half-up, for non-negative integers. */
export function hrMulDiv(a:number,b:number,c:number,den:number):number{const n=B(a)*B(b)*B(c);return Number((n*B(2)+B(den))/(B(2)*B(den)));}
/** ceil(a·b / den) for non-negative integers. */
const ceilDiv=(a:number,b:number,den:number)=>{const n=B(a)*B(b);return Number((n+B(den)-B(1))/B(den));};
/** Percentage (bps) of a paise amount with the statutory rounding of the rate version. */
export function hrPct(paise:number,bps:number,mode:HRRounding,kind:'pf'|'esic'='pf'):number{
  if(mode==='rupee')return (kind==='esic'?ceilDiv(paise,bps,10000*100):hrMulDiv(paise,bps,1,10000*100))*100;
  return hrMulDiv(paise,bps,1,10000);
}

// ── Dates ──
export const hrWeekday=(d:string)=>new Date(d+'T12:00:00Z').getUTCDay();
/** Monday of the week containing d. */
export const hrMonday=(d:string)=>addDays(d,-((hrWeekday(d)+6)%7));
export const hrWeek=(monday:string)=>Array.from({length:7},(_,i)=>addDays(monday,i));
export const hrDaysInMonth=(ym:string)=>new Date(Date.UTC(+ym.slice(0,4),+ym.slice(5,7),0)).getUTCDate();
export const hrMonthDates=(ym:string)=>Array.from({length:hrDaysInMonth(ym)},(_,i)=>`${ym}-${String(i+1).padStart(2,'0')}`);
export const hrMonthEnd=(ym:string)=>`${ym}-${String(hrDaysInMonth(ym)).padStart(2,'0')}`;
export const hrPrevMonth=(ym:string)=>{const y=+ym.slice(0,4),m=+ym.slice(5,7);return m===1?`${y-1}-12`:`${y}-${String(m-1).padStart(2,'0')}`;};
export const hrNextMonth=(ym:string)=>{const y=+ym.slice(0,4),m=+ym.slice(5,7);return m===12?`${y+1}-01`:`${y}-${String(m+1).padStart(2,'0')}`;};
export const hrMonthLabel=(ym:string)=>new Date(ym+'-15T12:00:00Z').toLocaleDateString('en-IN',{month:'long',year:'numeric',timeZone:'UTC'});
/** A month may be paid once it is complete: its last day is before the business date. */
export const hrMonthComplete=(s:State,ym:string)=>hrMonthEnd(ym)<s.date;
/** Indian financial year (April → March) containing a date; a 4-digit year means the FY starting in April of that year (SCC fyRangeOf). */
export function hrFy(dateOrYear:string){
  const y=dateOrYear.length===4?+dateOrYear:(+dateOrYear.slice(5,7)>=4?+dateOrYear.slice(0,4):+dateOrYear.slice(0,4)-1);
  return {from:`${y}-04-01`,to:`${y+1}-03-31`,label:`FY ${y}–${String(y+1).slice(-2)}`};
}
const toMin=(t:string)=>+t.slice(0,2)*60+ +t.slice(3,5);

// ── Roles / visibility ──
const PAYROLL_ROLES:Role[]=['Owner','Accountant'];
/** Roster, attendance for others, leave decisions, non-salary staff edits. */
export const hrCanManage=(s:State,uid:string)=>can(s,uid,'approve');
/** Salaries, payroll, advances, payslips of others. Role-based on purpose (brief: Owner / Accountant only). */
export function hrCanPay(s:State,uid:string){const u=s.users.find(x=>x.id===uid);return !!u&&PAYROLL_ROLES.includes(u.role)&&can(s,uid,'reports');}
/** Statutory rates, leave policy and unlocking a pay-run: payroll role that also holds admin (Owner, DEC-058). */
export const hrCanRates=(s:State,uid:string)=>hrCanPay(s,uid)&&can(s,uid,'admin');
export const hrStaffOfUser=(s:State,uid:string)=>hrSlice(s).staff.find(x=>x.userId===uid);
export const hrStaffById=(s:State,id:string)=>hrSlice(s).staff.find(x=>x.id===id);
/** F-03: linking a staff record to a demo login decides whose payslips "My shifts & pay" shows — Owner only (payroll + admin), never on your own record. */
export const hrCanLink=(s:State,uid:string)=>hrCanRates(s,uid);
/** Locked runs that hold a payslip for this staff member (relinking such a record needs the Owner's explicit confirmation). */
export const hrLockedPayslipCount=(s:State,staffId:string)=>hrSlice(s).runs.filter(r=>r.status==='Locked'&&r.lines.some(l=>l.staffId===staffId)).length;
export const hrDeptName=(s:State,id:string)=>hrSlice(s).depts.find(d=>d.id===id)?.name??id;
export const hrStructureOf=(st:HRStaff):HRStructure=>st.structure??(st.payType==='daily'?'B':'S');
export const hrPfOn=(st:HRStaff)=>st.pfApplicable??hrStructureOf(st)==='S';
export const hrEsicOn=(st:HRStaff)=>st.esicApplicable??true;
export const hrGenderOf=(st:HRStaff):HRGender=>st.gender??'M';

// ── Dated statutory rates ──
export const hrRateVersions=(s:State):HRRates[]=>{const v=hrSlice(s).rateVersions;return v?.length?v:[hrDefaultRates()];};
/** Rate version in force for a month (latest effective-from on or before the 1st). */
export function hrRatesFor(s:State,ym:string):HRRates{
  const v=hrRateVersions(s),d=ym+'-01';let r=v[0];for(const x of v)if(x.from<=d)r=x;return r;
}
/** F-06: default effective date for a new rate version: the month after the last COMPLETED pay period (the current month),
 *  and never before the month after the last locked run. A completed-but-unlocked month changes only when chosen explicitly. */
export function hrRatesDefaultFrom(s:State):string{
  const cur=s.date.slice(0,7),last=hrLastLockedMonth(s),next=last?hrNextMonth(last):cur;return (next>cur?next:cur)+'-01';
}
/** Completed months without a locked run that a version from `from` would change (their payroll must be recalculated). */
export function hrRatesPastMonths(s:State,from:string):string[]{
  const out:string[]=[];for(let m=from.slice(0,7);hrMonthComplete(s,m);m=hrNextMonth(m))if(hrSlice(s).runs.find(r=>r.month===m)?.status!=='Locked')out.push(m);return out;
}
/** Leave policy; once the Leave types list is saved its yearly quotas are the entitlements (MA-6), before that the stored policy. */
export function hrLeavePolicy(s:State):HRLeavePolicy{
  const p=hrSlice(s).leavePolicy??hrDefaultLeavePolicy();
  if(!mstStored(s,'leaveTypes'))return p;
  return {...p,ent:{CL:hrLeaveQuota(s,'CL')??0,SL:hrLeaveQuota(s,'SL')??0,EL:hrLeaveQuota(s,'EL')??0}};
}
/** SCC per-store advisory cap, per department (SCC CAPS_DEFAULT = MAX_DAY). Never blocks a leave. */
export const hrDeptCap=(s:State,dept:string)=>hrLeavePolicy(s).deptCap[dept]??HR_MAX_DAY;
/** Latest locked pay-run month (rates on or before it can no longer change). */
export const hrLastLockedMonth=(s:State)=>hrSlice(s).runs.filter(r=>r.status==='Locked').map(r=>r.month).sort().pop();

// ── Roster / attendance ──
export const hrKey=(date:string,staffId:string)=>date+'|'+staffId;
export function hrShiftFor(s:State,st:HRStaff,date:string):HRShiftCode{
  const o=hrSlice(s).roster[hrKey(date,st.id)];if(o)return o;
  return hrWeekday(date)===st.weeklyOff&&hrWeeklyOffAuto(s,st.defaultShift)?'OFF':st.defaultShift;
}
// ── DEC-083 paid holidays = the active "paid" rows of MA-6's Holiday calendar master (masters integration: one holidays concept) ──
/** The paid holidays (date order) as {date, name}; restricted and retired rows of the calendar are not paid holidays. */
export const hrPaidHolidays=(s:State):HRPaidHoliday[]=>hrHolidays(s,{active:true}).filter(h=>h.kind==='paid').map(h=>({date:h.code,name:h.name}))
  .sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:0);
/** The paid holiday on a date, if any. */
export const hrPaidHolidayOn=(s:State,date:string):HRPaidHoliday|undefined=>{const h=hrHolidayOn(s,date);return h&&h.kind==='paid'?{date:h.code,name:h.name}:undefined;};
/** A day without duty for leave: a rostered weekly off or a paid holiday (leave days skip both; nobody is "off" on them). */
export const hrDayOff=(s:State,st:HRStaff,date:string)=>hrShiftFor(s,st,date)==='OFF'||hrHolidayPaid(s,date);
/** DEC-075: the unmarked-days warning points to the correction action (server PayrollRules.UnmarkedHint). */
export const HR_UNMARKED_HINT='a manager can correct them on Staff & payroll › Attendance › Past days before the lock.';
// ── SCC leave records by date (one record per date; a memo per leaves array for the screens — handlers filter directly) ──
const leaveIdx=new WeakMap<HRLeave[],{n:number;by:Map<string,HRLeave[]>}>();
/** Every leave record (any status) of a date, in insertion order. */
export function hrLeavesOn(s:State,date:string):HRLeave[]{
  const ls=hrSlice(s).leaves;let ix=leaveIdx.get(ls);
  if(!ix||ix.n!==ls.length){const by=new Map<string,HRLeave[]>();for(const l of ls){const a=by.get(l.date);if(a)a.push(l);else by.set(l.date,[l]);}ix={n:ls.length,by};leaveIdx.set(ls,ix);}
  return ix.by.get(date)??[];
}
/** SCC isActiveLeave: pending + approved hold a slot (rejected frees it; a cancelled record — SCC "removed" — is gone). */
export const hrLeaveActive=(l:{status:HRLeave['status']})=>l.status==='Pending'||l.status==='Approved';
export const hrApprovedLeavesOn=(s:State,staffId:string,date:string)=>hrLeavesOn(s,date).filter(l=>l.staffId===staffId&&l.status==='Approved');
/** A full-day approved leave on the date, else the first half-day one. */
export const hrApprovedLeaveOn=(s:State,staffId:string,date:string)=>{const ls=hrApprovedLeavesOn(s,staffId,date);return ls.find(l=>!l.half)??ls[0];};
/** Day view for one staff member: explicit attendance → approved leave (SCC: a leave record of that date) → rostered off → paid holiday → unmarked.
 *  Half-day leave can sit beside attendance. */
export function hrDayStatus(s:State,st:HRStaff,date:string):{kind:'att'|'leave'|'off'|'holiday'|'unmarked'|'na';att?:HRAtt;leave?:HRLeave;holiday?:HRPaidHoliday}{
  if(date<st.joined||(st.exitedOn&&date>st.exitedOn))return {kind:'na'};
  const att=hrSlice(s).att[hrKey(date,st.id)],leave=hrApprovedLeaveOn(s,st.id,date);
  if(att)return leave?.half?{kind:'att',att,leave}:{kind:'att',att};
  if(leave)return {kind:'leave',leave};
  if(hrShiftFor(s,st,date)==='OFF')return {kind:'off'};
  const holiday=hrPaidHolidayOn(s,date);if(holiday)return {kind:'holiday',holiday};// DEC-083 / MA-6: an unmarked paid holiday is paid like a weekly off
  return {kind:'unmarked'};
}
/** Self check-in status from the simulated clock: Late after shift start + grace. */
export function hrCheckinStatus(clock:string,shift:HRShiftCode,s?:State):'P'|'L'{
  const p=s?hrShiftPattern(s,shift):undefined;
  if(shift==='OFF'||(p?p.night:shift==='N'))return 'P';
  return toMin(clock.slice(11,16))>toMin(hrShiftInfo(shift,s).start)+HR_GRACE_MIN?'L':'P';
}
/** Counts for a staff member over a month (optionally up to a date). Leave and absence can be half days (0.5). */
export function hrCounts(s:State,st:HRStaff,ym:string,upTo?:string):HRCounts{
  const c:HRCounts={ym,days:hrDaysInMonth(ym),P:0,L:0,H:0,HL:0,A:0,OFFW:0,off:0,paidLeave:0,lopLeave:0,unmarked:0,na:0,otMin:0};
  const addLeave=(l:HRLeave,n:number)=>{if(hrPaidLeave(l.type,s))c.paidLeave+=n;else c.lopLeave+=n;};
  const mgmt=hrMonthDates(ym).filter(d=>hrHolidayPaid(s,d)).length;if(mgmt)c.mgmtHol=mgmt;
  // The optional counts are set after the loop in one fixed key order (mgmtHol, hol, NT, elapsed), as the production client rebuilds them.
  let seen=0,hol=0,nt=0,elapsed:number|undefined;
  for(const d of hrMonthDates(ym)){
    if(upTo&&d>upTo){elapsed=seen;break;}
    seen++;
    if(d<st.joined||(st.exitedOn&&d>st.exitedOn)){c.na++;continue;}
    const att=hrSlice(s).att[hrKey(d,st.id)],ls=hrApprovedLeavesOn(s,st.id,d),full=ls.find(l=>!l.half),halves=ls.filter(l=>l.half).slice(0,2);
    if(att){
      c.otMin+=att.otMin||0;
      if(!full&&halves.length&&(att.status==='H'||att.status==='A')){
        addLeave(halves[0],0.5);
        if(att.status==='H')c.HL++;else c.A+=0.5;
      }else if(att.status==='OFF')c.OFFW++;else if(att.status==='NT')nt++;else c[att.status]++;
    }
    // SCC leave port (DEC-116): leave is one record per date, created only on dates outside the hotel's week-off set, so an approved record IS a
    // leave day — SCC's payroll feed counts every approved per-day record (paid categories → leave, HCC's LOP → absent), also on a rostered off day.
    // (Before the port, F-01 read a rostered weekly off inside a leave SPAN as an off day; spans became per-day records of the counted days only.)
    // DEC-083 / MA-6: an unmarked paid holiday without leave is a paid day; a weekly off on the same day stays a weekly off.
    else if(full)addLeave(full,1);
    else if(halves.length){halves.forEach(l=>addLeave(l,0.5));c.unmarked+=1-0.5*halves.length;}
    else if(hrShiftFor(s,st,d)==='OFF')c.off++;
    else if(hrHolidayPaid(s,d))hol++;
    else c.unmarked++;
  }
  if(hol)c.hol=hol;if(nt)c.NT=nt;if(elapsed!==undefined)c.elapsed=elapsed;
  return c;
}

// ── Leave (SCC leave\index.html, in full — DEC-116 / DEC-119) ──
/** The hotel's week-off set for leave (SCC loadWeekOff): weekdays 0 = Sunday … 6 = Saturday. Per-staff weekly offs stay for the roster only. */
export const hrLeaveWeekOff=(s:State):number[]=>{const w=hrSlice(s).leavePolicy?.weekOff;return Array.isArray(w)?w:HR_LEAVE_WEEKOFF_DEFAULT;};
/** SCC isWeekOffDate: the single source for recording, import, requested days, the blackout check, the calendar tint and the staffing strip. */
export const hrIsWeekOff=(s:State,date:string)=>hrLeaveWeekOff(s).includes(hrWeekday(date));
/** SCC getCapacity over the active records of a date (`ls` = that date's records; default: the state's). */
export function hrCapacity(s:State,date:string,ls:HRLeave[]=hrLeavesOn(s,date)){
  const act=ls.filter(l=>l.date===date&&hrLeaveActive(l));
  const amUsed=act.filter(l=>!l.half||l.half==='AM').length,pmUsed=act.filter(l=>!l.half||l.half==='PM').length,dayUsed=act.length,dayFull=dayUsed>=HR_MAX_DAY;
  return {amUsed,pmUsed,dayUsed,dayFull,amFull:amUsed>=HR_MAX_SHIFT||dayFull,pmFull:pmUsed>=HR_MAX_SHIFT||dayFull,
    bothFull:(amUsed>=HR_MAX_SHIFT&&pmUsed>=HR_MAX_SHIFT)||dayFull,
    canAddFullDay:amUsed<HR_MAX_SHIFT&&pmUsed<HR_MAX_SHIFT&&!dayFull,canAddAM:amUsed<HR_MAX_SHIFT&&!dayFull,canAddPM:pmUsed<HR_MAX_SHIFT&&!dayFull};
}
/** SCC canAddLeave(key, type): full day needs both shifts, a half day its own shift, all under the 3-per-day cap. */
export function hrCanAddLeave(s:State,date:string,half?:HRHalf,ls?:HRLeave[]){
  const c=hrCapacity(s,date,ls);return half==='AM'?c.canAddAM:half==='PM'?c.canAddPM:c.canAddFullDay;
}
/** SCC duplicate rule (submitLeave): same staff member, a record not rejected (nor cancelled), and the same half, or either is a full day. AM + PM coexist. */
export const hrIsDuplicate=(ls:HRLeave[],staffId:string,date:string,half?:HRHalf)=>
  ls.some(l=>l.date===date&&l.staffId===staffId&&hrLeaveActive(l)&&(l.half===half||!l.half||!half));
/** SCC requestedLeaveDays: non-week-off dates from…to (at most 400 looked at, SCC's guard) × 1 (full day) or 0.5 (half day). Dates later skipped
 *  as duplicate or full still count (SCC; owner question). */
export function hrLeaveDays(s:State,from:string,to:string,half?:HRHalf){
  if(!from||!to||from>to)return 0;
  let n=0,g=0;for(let d=from;d<=to&&g++<400;d=addDays(d,1))if(!hrIsWeekOff(s,d))n++;
  return n*(half?0.5:1);
}
/** SCC entFor: per category, the staff member's override, else the saved default, else ENT_DEFAULTS (never below 0). */
export function hrEntFor(s:State,staffId:string):{CL:number;SL:number;EL:number}{
  const p=hrLeavePolicy(s),ov=p.perStaff?.[staffId]??{},ent=p.ent??HR_ENT_DEFAULTS;
  const one=(c:'CL'|'SL'|'EL')=>{const v=ov[c]!=null?ov[c]:ent[c]!=null?ent[c]:HR_ENT_DEFAULTS[c];return Math.max(0,Number(v)||0);};
  return {CL:one('CL'),SL:one('SL'),EL:one('EL')};
}
/** SCC fyUsage: APPROVED leave days (SCC isCountedLeave; imported legacy records are approved) per category for one staff member, each record by its
 *  own date inside the FY. Pending leave is not counted (owner question). `except` = a record to leave out (DEC-075 correction preview). */
export function hrFyUsage(s:State,staffId:string,fy:{from:string;to:string},except=''):Record<string,number>{
  const used:Record<string,number>={};
  for(const l of hrSlice(s).leaves)if(l.staffId===staffId&&l.status==='Approved'&&l.id!==except&&l.date>=fy.from&&l.date<=fy.to)used[l.type]=(used[l.type]??0)+l.days;
  return used;
}
/** Leave used (approved, by date) in the financial year containing a date (or starting in April of a 4-digit year) for one category. */
export const hrLeaveUsed=(s:State,staffId:string,dateOrYear:string,type:HRLeaveType,except='')=>hrFyUsage(s,staffId,hrFy(dateOrYear),except)[type]??0;
/** Entitlement of a tracked category for a staff member (SCC entFor); 0 for an untracked one. */
export const hrEntitled=(s:State,type:HRLeaveType,staffId='')=>(HR_TRACKED as string[]).includes(type)?hrEntFor(s,staffId)[type as 'CL'|'SL'|'EL']:0;
/** Days left for a tracked category: entitlement − approved use (no carry-forward: SCC starts every FY at the quota). */
export const hrLeaveAvailable=(s:State,staffId:string,type:HRLeaveType,dateOrYear:string,except='')=>hrEntitled(s,type,staffId)-hrLeaveUsed(s,staffId,dateOrYear,type,except);
export function hrLeaveBalance(s:State,staffId:string,dateOrYear:string){
  // F-15: `left` never goes below 0; `over` = approved days beyond the entitlement (SCC allows it after the warning).
  const used=hrFyUsage(s,staffId,hrFy(dateOrYear)),ent=hrEntFor(s,staffId);
  return HR_TRACKED.map(t=>{const u=used[t]??0,e=ent[t];return {type:t,entitled:e,used:u,left:Math.max(0,e-u),over:Math.max(0,u-e)};});
}
/** F-15: staff whose approved leave already exceeds a proposed entitlement (shown before the Owner saves a cut; SCC has no such warning). */
export function hrLeaveOverCount(s:State,ent:{CL:number;SL:number;EL:number},dateOrYear=s.date){
  const fy=hrFy(dateOrYear),ov=hrLeavePolicy(s).perStaff??{};
  return hrSlice(s).staff.filter(st=>st.active&&HR_TRACKED.some(t=>(hrFyUsage(s,st.id,fy)[t]??0)>(ov[st.id]?.[t]??ent[t]))).length;
}
/** SCC blackoutsForKey: entries covering a date; with `dept` (the add-leave gate) only hotel-wide ones and those of that department. */
export function hrBlackoutsFor(s:State,date:string,dept?:string):HRBlackout[]{
  return (hrLeavePolicy(s).blackouts??[]).filter(b=>date>=b.from&&date<=b.to&&(dept===undefined||b.dept===''||b.dept===dept));
}
/** SCC submit gate: blackouts on the non-week-off dates from…to (at most 400 looked at) for a staff member's department; hard = refuse, else confirm. */
export function hrBlackoutHits(s:State,from:string,to:string,dept:string){
  const hits:HRBlackout[]=[];if(!from||!to||from>to)return {hits,hard:false,labels:''};
  let g=0;for(let d=from;d<=to&&g++<400;d=addDays(d,1))if(!hrIsWeekOff(s,d))hits.push(...hrBlackoutsFor(s,d,dept));
  const labels=[...new Set(hits.map(b=>b.label||(b.kind==='holiday'?'Store holiday':'Blackout')))].join(', ');
  return {hits,hard:hits.some(b=>b.hard),labels};
}
/** SCC getApproverChoices: the Owner, then Managers (SCC: Store Managers) — enabled users; none → every enabled user with their role. */
export function hrApproverChoices(s:State):{id:string;name:string;role:string}[]{
  const srv=hrSlice(s).approvers;if(srv)return srv;
  const nm=(u:{name:string})=>u.name.split(' · ')[0];
  const pick=s.users.filter(u=>u.enabled&&u.role==='Owner').concat(s.users.filter(u=>u.enabled&&u.role==='Manager'));
  return (pick.length?pick:s.users.filter(u=>u.enabled)).map(u=>({id:u.id,name:nm(u),role:u.role}));
}
/** Before the SCC port a leave was one SPAN (from…to, `days`): the dates it counted — the staff member's rostered working days that were not paid
 *  holidays (a half day: its one date when that was such a day). Used once to split old spans into per-day records (restore + fixtures). */
export function hrLegacySpanDates(s:State,st:HRStaff|undefined,from:string,to:string,half?:HRHalf):string[]{
  const out:string[]=[];if(!from||!to||to<from)return out;
  for(let d=from;d<=(half?from:to);d=addDays(d,1))if(!st||!hrDayOff(s,st,d))out.push(d);
  return out;
}
/** One-time split of old span-shaped leave (no `date`) into SCC per-day records; idempotent (a no-op when every record has a date).
 *  The first counted date keeps the request id, the next ones get `<id>-2`, `<id>-3` …; every record keeps the span as its request range. */
export function hrMigrateLeaveDays(s:State):boolean{
  const x=s.staffX;if(!x||!Array.isArray(x.leaves)||x.leaves.every(l=>typeof (l as Partial<HRLeave>).date==='string'))return false;
  const out:HRLeave[]=[];
  for(const l of x.leaves){
    if(typeof (l as Partial<HRLeave>).date==='string'){out.push(l);continue;}
    const st=x.staff.find(t=>t.id===l.staffId),counted=hrLegacySpanDates(s,st,l.from,l.to,l.half),dates=counted.length?counted:[l.from];// never drop a record
    dates.forEach((d,i)=>out.push({...l,id:i?`${l.id}-${i+1}`:l.id,date:d,days:l.half?0.5:1,reason:l.reason??''}));
  }
  x.leaves=out;return true;
}

// ── Payroll ──
export const hrAdvanceBalance=(a:HRAdvance)=>a.amount-a.recovered;
export const hrOpenAdvance=(s:State,staffId:string)=>hrSlice(s).advances.find(a=>a.staffId===staffId&&hrAdvanceBalance(a)>0);
const advFrom=(a:HRAdvance)=>a.fromMonth??a.date.slice(0,7);
/** The open advance recovered in a month's run (one open advance per staff member). */
export const hrAdvanceFor=(x:HRState,staffId:string,ym:string)=>x.advances.find(a=>a.staffId===staffId&&hrAdvanceBalance(a)>0&&advFrom(a)<=ym);
/** Whether a staff member belongs in a month's payroll (a full-and-final settlement that paid the exit month removes them). */
export const hrInMonth=(st:HRStaff,ym:string)=>st.joined<=hrMonthEnd(ym)&&(st.active||!!st.exitedOn&&st.exitedOn>=ym+'-01');
/** Maharashtra professional tax for a month (SCC ptFor): women exempt up to the female limit; men nil / mid slab / standard; February top slab. */
export function hrPt(base:number,gender:HRGender,ym:string,r:HRRates):number{
  const top=ym.slice(5,7)==='02'?r.ptFeb:r.ptStd;
  if(gender==='F')return base<=r.ptFemaleExempt?0:top;
  if(base<=r.ptMaleExempt)return 0;
  if(base<=r.ptMaleMid)return r.ptMaleMidAmt;
  return top;
}
// ── F-07: ESIC contribution periods (Apr–Sep, Oct–Mar) ──
/** First month (YYYY-MM) of the ESIC contribution period containing a month. */
export function hrEsicPeriodStart(ym:string){const y=+ym.slice(0,4),m=+ym.slice(5,7);return m>=10?`${y}-10`:m>=4?`${y}-04`:`${y-1}-10`;}
/** Contractual monthly wage for the coverage test: monthly salary, or daily wage × 26 (illustrative — CA to confirm). */
export const hrEsicContractWage=(st:HRStaff)=>st.payType==='monthly'?st.monthly:st.daily*26;
/** Coverage under the contribution-period rule: the earliest LOCKED run of the same period decides; with none yet, the contractual wage
 *  at this month (≤ ceiling = covered). Undefined when the rate version uses the month-by-month rule. */
export function hrEsicCovered(s:State,st:HRStaff,ym:string,r:HRRates):boolean|undefined{
  if(!r.esicPeriod)return undefined;
  const ps=hrEsicPeriodStart(ym);
  const prior=hrSlice(s).runs.filter(x=>x.status==='Locked'&&x.month>=ps&&x.month<ym).sort((a,b)=>a.month<b.month?-1:1)
    .map(x=>x.lines.find(l=>l.staffId===st.id)).find(l=>typeof l?.esicCovered==='boolean');
  return prior?prior.esicCovered:hrEsicContractWage(st)<=r.esicCeiling;
}
/** SCC roundHalf: to the nearest half day (SCC adds EPSILON; the inputs here are exact halves or day counts). */
export const hrRoundHalf=(n:number)=>Math.round(n*2)/2;
/** SCC extra days: 4 when the deduction is at most 5.5 days, 2 below 7, none from 7. */
export const hrSccExtra=(deduction:number)=>deduction<=5.5?4:deduction<7?2:0;
/** SCC total days of a month: calendar days (or the rate version's override) less the paid holidays (SCC "Mgmt holidays"), never below 0. */
export const hrSccTotalDays=(c:HRCounts,r:HRRates)=>Math.max(0,hrRoundHalf((r.daysOverride&&r.daysOverride>0?r.daysOverride:c.days)-(c.mgmtHol??0)));
/** DEC-115 — SCC calcAttendance on HCC's daily marks, read the way SCC's DSR/Leave feed fills the sheet: present = P + L + days worked on the
 *  weekly off (OFF mark) + the worked half of a half-day leave; half days = H; leave = approved paid leave; absent = total − present − leave − half (weekly offs, A, unmarked days,
 *  leave without pay and days out of service). A month read up to a date counts its elapsed share of the total days only (SCC feed). */
export function hrSccDays(c:HRCounts,r:HRRates):HRSccDays{
  const total=hrSccTotalDays(c,r),base=c.elapsed!==undefined&&c.elapsed<c.days?Math.min(total,hrRoundHalf(total*c.elapsed/c.days)):total;
  const noThumb=c.NT??0,present=c.P+c.L+c.OFFW+noThumb+c.HL*0.5,half=c.H,leave=hrRoundHalf(c.paidLeave),late=c.L;
  const absent=Math.max(0,hrRoundHalf(base-present-leave-half));
  const lateDed=r.lateBlock>0?hrRoundHalf(Math.floor(late/r.lateBlock)*0.5):0,absentDed=absent,halfDed=hrRoundHalf(half*0.5),noThumbDed=hrRoundHalf(noThumb*0.5);
  const deduction=hrRoundHalf(lateDed+absentDed+halfDed+noThumbDed),extra=hrSccExtra(deduction);
  const finalPresent=Math.max(0,hrRoundHalf(total-deduction)),raw=hrRoundHalf(finalPresent+leave+extra);
  return {total,present,half,leave,absent,late,lateDed,absentDed,halfDed,deduction,extra,finalPresent,salaryDays:Math.min(total,Math.max(0,raw)),otDays:Math.max(0,hrRoundHalf(raw-total)),
    noThumb,noThumbDed};
}
/** One payroll line (SCC calcGM). Pure: staff + counts + rate version + advance → figures (rounded to paise, half-up, in this order).
 *  `esicCovered` = contribution-period coverage from hrEsicCovered (only when the rate version uses that rule). */
export function hrComputeLine(st:HRStaff,c:HRCounts,r:HRRates,adv?:{installment:number;balance:number},esicCovered?:boolean):HRLine{
  const structure=hrStructureOf(st),gender=hrGenderOf(st),days=hrSccDays(c,r),T2=Math.round(days.total*2);
  // SCC: grossPayable = gross × salary days ÷ total days; OT = gross ÷ total days × OT days. A daily-wage worker's gross = daily rate × total days.
  const dayPay=(d:number,bps:number)=>T2<=0?0:st.payType==='monthly'?hrMulDiv(st.monthly,Math.round(d*2),bps,T2*10000):hrMulDiv(st.daily,Math.round(d*2),bps,2*10000);
  const earned=dayPay(days.salaryDays,10000),ot=dayPay(days.otDays,r.otBps),late=Math.round(days.lateDed*2);
  let basic=earned,hra=0,washing=0;
  if(structure==='S'){basic=hrMulDiv(earned,r.basicBps,1,10000);hra=hrMulDiv(basic,r.hraBps,1,10000);washing=hrMulDiv(basic,r.washBps,1,10000);}
  const special=earned-basic-hra-washing,gross=earned+ot;
  const pfWages=hrPfOn(st)?Math.min(basic,r.pfWageCap):0;
  const pf=pfWages?hrPct(pfWages,r.pfEeBps,r.rounding):0,pfEr=pfWages?hrPct(pfWages,r.pfErBps,r.rounding):0;
  const period=!!r.esicPeriod,covered=period?(esicCovered??hrEsicContractWage(st)<=r.esicCeiling):earned<=r.esicCeiling;
  const esicWages=hrEsicOn(st)&&earned>0&&covered?earned-washing:0;
  const esic=esicWages?hrPct(esicWages,r.esicEeBps,r.rounding,'esic'):0,esicEr=esicWages?hrPct(esicWages,r.esicErBps,r.rounding,'esic'):0;
  const pt=earned>0?hrPt(earned,gender,c.ym,r):0;
  const due=adv?(adv.installment>0?Math.min(adv.installment,adv.balance):adv.balance):0;
  const advance=Math.max(0,Math.min(due,gross-pf-esic-pt));
  const deductions=pf+esic+pt+advance;
  return {staffId:st.id,name:st.name,dept:st.dept,designation:st.designation,payType:st.payType,structure,gender,base:st.payType==='monthly'?st.monthly:st.daily,
    counts:c,latePenaltyHalf:late,payableDays:days.salaryDays,days,earned,basic,hra,washing,special,ot,gross,pfWages,pf,pfEr,esicWages,esic,esicEr,pt,advance,deductions,
    net:gross-deductions,ctc:gross+pfEr+esicEr,...(period?{esicCovered:hrEsicOn(st)&&covered}:{})};
}
/** Staff settled full-and-final with the exit month's salary inside the settlement. */
const settledFor=(x:HRState,staffId:string,ym:string)=>(x.fnf??[]).some(f=>hrFnfSettled(f)&&f.staffId===staffId&&f.salaryIncluded&&f.month===ym);
/** Computes a month's payroll lines and warnings from current state and the rate version in force. */
export function hrComputeRun(s:State,ym:string,rates=hrRatesFor(s,ym)):{lines:HRLine[];warnings:string[]}{
  const x=hrSlice(s),warnings:string[]=[];
  const lines=x.staff.filter(st=>hrInMonth(st,ym)&&!settledFor(x,st.id,ym)).map(st=>{
    const c=hrCounts(s,st,ym);
    if(c.unmarked)warnings.push(`${st.name}: ${c.unmarked} unmarked day${c.unmarked===1?'':'s'} counted as absent — ${HR_UNMARKED_HINT}`);
    if((st.payType==='monthly'?st.monthly:st.daily)<=0)warnings.push(`${st.name}: salary not set — pays ₹0.`);
    const a=hrAdvanceFor(x,st.id,ym);
    const line=hrComputeLine(st,c,rates,a?{installment:a.installment,balance:hrAdvanceBalance(a)}:undefined,hrEsicCovered(s,st,ym,rates));
    warnings.push(...hrSccIssues(st.name,line.days!).map(i=>i.text));
    return line;
  });
  return {lines,warnings};
}
const fmtDays=(n:number)=>String(n);
/** DEC-115 — SCC calcAttendance issues, in SCC's words: 'risk' blocks the lock (SCC runBlockingErrors), 'warn' is shown only. */
export function hrSccIssues(name:string,d:HRSccDays):{type:'risk'|'warn';text:string}[]{
  const out:{type:'risk'|'warn';text:string}[]=[];
  if(d.leave>5.5)out.push({type:'risk',text:`${name}: Leaves applied (${fmtDays(d.leave)}) exceed 5.5-day limit.`});
  if(d.deduction>=d.total)out.push({type:'risk',text:`${name}: Total deduction (${fmtDays(d.deduction)}) ≥ total days (${fmtDays(d.total)}) — zero salary days.`});
  else if(d.deduction>5.5)out.push({type:'warn',text:`${name}: High total deduction — ${fmtDays(d.deduction)} days.`});
  if(d.otDays>0)out.push({type:'warn',text:`${name}: ${fmtDays(d.otDays)} OT day(s) — salary capped at ${fmtDays(d.total)}, OT tracked separately.`});
  if(d.late>=3)out.push({type:'warn',text:`${name}: ${d.late} late mark(s).`});
  if((d.noThumb??0)>0)out.push({type:'warn',text:`${name}: ${d.noThumb} no-thumb entry/entries.`});
  return out;
}
/** Errors that block locking (SCC runBlockingErrors): salary not set, and the SCC 'risk' issues of the day sheet. */
export const hrRunErrors=(run:{lines:HRLine[]})=>run.lines.flatMap(l=>[...(l.base<=0?[`${l.name}: salary not set`]:[]),
  ...(l.days?hrSccIssues(l.name,l.days).filter(i=>i.type==='risk').map(i=>i.text):[])]);
export const hrRunFor=(s:State,ym:string)=>hrSlice(s).runs.find(r=>r.month===ym);
export function hrRunTotals(run:{lines:HRLine[]}){
  const t={staff:run.lines.length,earned:0,basic:0,ot:0,gross:0,pf:0,pfEr:0,esic:0,esicEr:0,pt:0,advance:0,deductions:0,net:0,ctc:0};
  // F-04: a missing field (old save) counts as 0, never NaN.
  for(const l of run.lines)for(const k of ['earned','basic','ot','gross','pf','pfEr','esic','esicEr','pt','advance','deductions','net','ctc'] as const){const v=l[k];if(Number.isFinite(v))t[k]+=v;}
  return t;
}
/** F-02: copy each line's bank details from the staff master into the run (called at lock). */
export function hrBankSnapshot(x:HRState,run:HRRun){
  run.bank=Object.fromEntries(run.lines.map(l=>{const st=x.staff.find(t=>t.id===l.staffId);return [l.staffId,{bankName:st?.bankName??'',accountNo:st?.accountNo??'',ifsc:st?.ifsc??''}];}));
}
/** Masked account number for audit text: last 4 digits only. */
export const hrMaskAccount=(a?:string)=>a?'••••'+a.slice(-4):'(none)';
/** Applies a run's advance recoveries to the advances (used when locking). */
export function hrApplyRecoveries(x:HRState,run:HRRun){
  for(const l of run.lines){if(!l.advance)continue;const a=hrAdvanceFor(x,l.staffId,run.month);
    if(a){a.recovered+=l.advance;a.recoveries.push({runId:run.id,amount:l.advance});}}
}
/** Reverses a run's advance recoveries (Owner unlock). */
export function hrReverseRecoveries(x:HRState,runId:string){
  for(const a of x.advances){const mine=a.recoveries.filter(r=>r.runId===runId);if(!mine.length)continue;
    a.recovered-=mine.reduce((n,r)=>n+r.amount,0);a.recoveries=a.recoveries.filter(r=>r.runId!==runId);}
}
/** F-09: bank files issued for this month so far (current lock and any lock undone since). */
export const hrBankFilesIssued=(run:HRRun)=>[...(run.unlocks??[]).flatMap(u=>u.bankFiles??[]),...(run.bankFiles??[])];
/** ACC-2 (contract §8): what a pay-run lock books (server PayRunFigures, paise): totals of the run lines, other deductions = gross − net − employee
 *  PF − ESIC − PT − advance, and the advance recovered PER STAFF member (`[staffId, paise]`, run-line order; zero recoveries left out). */
export type HRRunBook={gross:number;pfEr:number;esicEr:number;pf:number;esic:number;pt:number;advance:number;other:number;net:number;recoveries:[string,number][]};
export function hrRunBook(run:{lines:HRLine[]}):HRRunBook{
  const t=hrRunTotals(run);
  return {gross:t.gross,pfEr:t.pfEr,esicEr:t.esicEr,pf:t.pf,esic:t.esic,pt:t.pt,advance:t.advance,other:t.gross-t.net-t.pf-t.esic-t.pt-t.advance,net:t.net,
    recoveries:run.lines.filter(l=>Number.isFinite(l.advance)&&l.advance>0).map(l=>[l.staffId,l.advance] as [string,number])};
}
/** ACC-2: the business date of an operation stamped with the simulated clock (`YYYY-MM-DDTHH:MM`), when no business date was recorded. */
export const hrOn=(on:string|undefined,at:string|undefined)=>on??(at?at.slice(0,10):undefined);
/** F-09: payslip state per staff member after an unlock: 'sent' (this lock), 'resend' (sent before an unlock and the net changed or
 *  not yet re-sent — the staff member holds superseded figures), else ''. */
export function hrPayslipMark(run:HRRun,staffId:string):'sent'|'resend'|'superseded'|''{
  if((run.sent??[]).some(x=>x.staffId===staffId))return 'sent';
  const u=[...(run.unlocks??[])].reverse().find(q=>(q.sent??[]).some(x=>x.staffId===staffId));if(!u)return '';
  const now=run.lines.find(l=>l.staffId===staffId)?.net;
  return now!==undefined&&u.nets?.[staffId]!==undefined&&u.nets[staffId]!==now?'resend':'superseded';
}
/** Payslips visible to a user: payroll roles see all; others only their own, from locked runs. */
export function hrVisiblePayslips(s:State,uid:string):{run:HRRun;line:HRLine}[]{
  const pay=hrCanPay(s,uid),me=hrStaffOfUser(s,uid);
  return hrSlice(s).runs.filter(r=>r.status==='Locked').flatMap(run=>run.lines.filter(l=>pay||l.staffId===me?.id).map(line=>({run,line})))
    .sort((a,b)=>b.run.month.localeCompare(a.run.month));
}
const rupees=(p:number)=>(Number.isFinite(p)?p/100:0).toFixed(2);
/** CSV cell; F-17: text starting with = + - @ (or tab / CR) is prefixed with ' so spreadsheets never run it as a formula (plain numbers stay numbers). */
// A06-08: a CR inside a cell is quoted too — unquoted, Excel starts a new row there and would read the text after it as a cell of its own.
const csvCell=(v:string|number)=>{let t=String(v);if(typeof v==='string'&&/^[=+\-@\t\r]/.test(t)&&!/^-?\d+(\.\d+)?$/.test(t))t="'"+t;return /[",\r\n]/.test(t)?'"'+t.split('"').join('""')+'"':t;};
const csv=(rows:(string|number)[][])=>rows.map(r=>r.map(csvCell).join(',')).join('\n')+'\n';
/** Payroll register CSV (rupees with 2 decimals). */
export function hrRegisterCsv(s:State,run:HRRun):string{
  const head=['Month','Staff ID','Name','Department','Designation','Pay type','Payable days','Earned','Basic','HRA','Washing','Special','Overtime','Gross','PF wages','PF (employee)','ESIC (employee)','PT','Advance recovery','Total deductions','PF (employer)','ESIC (employer)','Net pay'];
  const row=(l:HRLine)=>[run.month,l.staffId,l.name,hrDeptName(s,l.dept),l.designation,l.payType,l.payableDays,...[l.earned,l.basic,l.hra,l.washing,l.special,l.ot,l.gross,l.pfWages,l.pf,l.esic,l.pt,l.advance,l.deductions,l.pfEr,l.esicEr,l.net].map(rupees)];
  const t=hrRunTotals(run),pfw=run.lines.reduce((n,l)=>n+l.pfWages,0),hra=run.lines.reduce((n,l)=>n+l.hra,0),wash=run.lines.reduce((n,l)=>n+l.washing,0),spec=run.lines.reduce((n,l)=>n+l.special,0);
  const total=[run.month,'','TOTAL','','','','',...[t.earned,t.basic,hra,wash,spec,t.ot,t.gross,pfw,t.pf,t.esic,t.pt,t.advance,t.deductions,t.pfEr,t.esicEr,t.net].map(rupees)];
  return csv([head,...run.lines.map(row),total])+csvCell(`${run.status} run · fictional data · ${HR_DISCLAIMER}`)+'\n';
}
/** Bank transfer file (SCC reportBankCsv): one row per staff member with net pay; account numbers keep a leading apostrophe for Excel.
 *  F-02: a locked run pays to the bank details frozen at lock; later staff-master edits apply to future runs only. */
export function hrBankCsv(s:State,run:HRRun):string{
  const rows:(string|number)[][]=[['Sr','Employee Name','Employee ID','Bank Name','Account No','IFSC','Net Amount']];
  run.lines.forEach((l,i)=>{const st=hrStaffById(s,l.staffId),b=run.bank?.[l.staffId]??{bankName:st?.bankName??'',accountNo:st?.accountNo??'',ifsc:st?.ifsc??''};
    rows.push([i+1,l.name,l.staffId,b.bankName,"'"+b.accountNo,b.ifsc,rupees(l.net)]);});
  rows.push(['','TOTAL','','','','',rupees(hrRunTotals(run).net)]);
  const src=run.bank?'bank details as locked':'bank details from the staff master (run locked before bank snapshots)';
  return csv(rows)+csvCell(`${hrMonthLabel(run.month)} · ${run.status} · ${src} · fictional bank details · simulated transfer — no money moves`)+'\n';
}
/** A04-01: leavers settled full-and-final with the run month's salary inside the settlement are not in the run (settledFor), so their PF / ESIC / PT —
 *  the deductions on the settled F&F line — join that month's statutory statement. A staff member already in the run's lines is never added twice. */
export function hrStatutoryFnf(s:State,run:HRRun):HRFnf[]{
  const inRun=new Set(run.lines.map(l=>l.staffId));
  return (hrSlice(s).fnf??[]).filter(f=>hrFnfSettled(f)&&f.salaryIncluded&&f.month===run.month&&!!f.line&&!inRun.has(f.staffId))
    .sort((a,b)=>a.staffId.localeCompare(b.staffId));
}
/** Statutory summary + member-wise register (SCC reportStatutoryCsv + statutoryMemberRows). A04-01: settled F&F rows follow the run's rows in the same
 *  columns, count in the totals, and a note line after the table names them (the member columns stay as they are). */
export function hrStatutoryCsv(s:State,run:HRRun):string{
  const fnf=hrStatutoryFnf(s,run),members=[...run.lines,...fnf.map(f=>f.line!)],t=hrRunTotals({lines:members});
  const rows:(string|number)[][]=[['Component','Employee','Employer','Total'],
    ['Provident Fund',rupees(t.pf),rupees(t.pfEr),rupees(t.pf+t.pfEr)],['ESIC',rupees(t.esic),rupees(t.esicEr),rupees(t.esic+t.esicEr)],['Professional Tax (Maharashtra)',rupees(t.pt),'0.00',rupees(t.pt)],[],
    ['Sr','Staff ID','Name','Earned','PF wages','PF EE','PF ER','ESIC wages','ESIC EE','ESIC ER','PT']];
  members.forEach((l,i)=>rows.push([i+1,l.staffId,l.name,...[l.earned,l.pfWages,l.pf,l.pfEr,l.esicWages,l.esic,l.esicEr,l.pt].map(rupees)]));
  const note=fnf.length?csvCell(`F&F — exit month's salary settled in full and final (not in the pay run): ${fnf.map((f,i)=>`Sr ${run.lines.length+i+1} ${f.line!.name} (last day ${f.lastDay})`).join('; ')}`)+'\n':'';
  return csv(rows)+note+csvCell(`${hrMonthLabel(run.month)} · ${run.status==='Locked'?'Locked run':'DRAFT — NOT FINAL, do not file'} · rates from ${run.rates?.from??'—'}${run.legacy?' · legacy run (before SCC rules; employer share not recorded)':''} · ${HR_DISCLAIMER}`)+'\n';
}
// ── DEC-075 corrections of past attendance (server Hcc.Domain/Payroll/Corrections.cs + PayrollService.Corrections.cs) ──
/** The open pay month: from the 1st of the month after the latest LOCKED pay-run — at the earliest the 1st of the month before the business
 *  date's month — to yesterday (today is marked on the Today view). Empty when to < from. Production: the server's window. */
export function hrCorrectionWindow(s:State):{from:string;to:string}{
  const w=hrSlice(s).correctWindow;if(w)return w;
  const floor=hrPrevMonth(s.date.slice(0,7))+'-01',last=hrLastLockedMonth(s),next=last?hrNextMonth(last)+'-01':floor;
  return {from:next>floor?next:floor,to:addDays(s.date,-1)};
}
export const hrEntryLabel=(e:HRDayEntry)=>e.kind==='mark'?HR_ATT_LABEL[e.status]+(e.otMin?` · OT ${e.otMin} min`:''):e.kind==='leave'?HR_LEAVE_LABEL[e.type]:'Not marked';
export const hrEntryCode=(e:HRDayEntry)=>e.kind==='mark'?e.status:e.kind==='leave'?e.type:'none';
export const hrSameEntry=(a:HRDayEntry,b:HRDayEntry)=>hrEntryCode(a)===hrEntryCode(b)&&(a.kind!=='mark'||b.kind!=='mark'||a.otMin===b.otMin);
/** Correction targets the dialog offers (server DayEntry.Parse): attendance marks, full-day leave types, or 'none' (clear the day). */
export const HR_CORRECTION_MARKS=['P','L','H','A','OFF','NT','CL','SL','EL','ML','CO','OTH','LOP','none'] as const;
/** Parses a correction target (mark code + overtime minutes); a string = the refusal (VALIDATION). */
export function hrParseEntry(mark:unknown,ot:unknown):HRDayEntry|string{
  const m=Number(ot??0);
  if(!Number.isInteger(m))return 'Overtime must be 0–480 minutes.';
  if(mark==='none')return m?'A cleared day carries no overtime.':{kind:'none'};
  if((HR_ATT_MARKS as string[]).includes(String(mark))){if(m<0||m>480)return 'Overtime must be 0–480 minutes.';if(mark==='A'&&m>0)return 'An absent day cannot carry overtime.';
    return {kind:'mark',status:mark as HRAttStatus,otMin:m};}
  if((HR_LEAVE_TYPES as string[]).includes(String(mark)))return m?'A leave day carries no overtime.':{kind:'leave',type:mark as HRLeaveType};
  return 'Choose what the day should be: an attendance mark, a leave type or "none".';
}
/** What a past day holds, in the order the month counts read it (attendance mark first, then a full-day approved leave). Leave is one record per
 *  date (SCC), so an approved full-day record is always the day's own record (`oneDayLeave`), whatever range it was requested with. */
export function hrDayEntry(s:State,st:HRStaff,date:string):{entry:HRDayEntry;oneDayLeave?:HRLeave;halfDayLeave:boolean;pendingLeave:boolean}{
  const x=hrSlice(s),att=x.att[hrKey(date,st.id)],mine=x.leaves.filter(l=>l.staffId===st.id&&l.date===date);
  const approved=mine.filter(l=>l.status==='Approved'),full=approved.find(l=>!l.half),halfDayLeave=approved.some(l=>!!l.half),pendingLeave=mine.some(l=>l.status==='Pending');
  if(att)return {entry:{kind:'mark',status:att.status,otMin:att.otMin||0},halfDayLeave,pendingLeave};
  if(!full)return {entry:{kind:'none'},halfDayLeave,pendingLeave};
  return {entry:{kind:'leave',type:full.type},oneDayLeave:full,halfDayLeave:false,pendingLeave};
}
/** Gross pay (earned + overtime) of a staff member's month line on a state's days. */
const hrMonthGross=(s:State,st:HRStaff,ym:string)=>hrComputeLine(st,hrCounts(s,st,ym),hrRatesFor(s,ym)).gross;
/** A read-only view of the state with the day holding `entry` (the day's mark replaced or removed, a replaced one-day leave cancelled, a leave
 *  entry a new one-day approved leave). Pure: builds new objects and reads only the staff slice (safe on a command's copy-on-write draft). */
function hrWithEntry(s:State,st:HRStaff,date:string,e:HRDayEntry,replaced?:string):State{
  const x=hrSlice(s),key=hrKey(date,st.id),att={...x.att};delete att[key];
  const leaves=x.leaves.map(l=>l.id===replaced?{...l,status:'Cancelled' as const}:l);
  if(e.kind==='mark')att[key]={status:e.status,at:'',otMin:e.otMin,by:''};
  else if(e.kind==='leave')leaves.push({id:'(correction)',staffId:st.id,date,type:e.type,from:date,to:date,days:1,reason:'',status:'Approved',requestedBy:'',requestedAt:''});
  return {date:s.date,staffX:{...x,att,leaves}} as unknown as State;
}
type HRCorrectionRefusal={code:'VALIDATION'|'INVALID_TRANSITION';msg:string};
const dmy=(d:string)=>new Date(d+'T12:00:00Z').toLocaleDateString('en-IN',{day:'numeric',month:'short',year:'numeric',timeZone:'UTC'});
/** The date must lie in the open pay month: never a locked month, today or the future. Undefined = allowed. */
export function hrCorrectionDateRefusal(s:State,date:string):HRCorrectionRefusal|undefined{
  const ym=date.slice(0,7),w=hrCorrectionWindow(s);
  if(hrRunFor(s,ym)?.status==='Locked')return {code:'INVALID_TRANSITION',msg:`${hrMonthLabel(ym)} payroll is locked — a locked month is never changed (DEC-075).`};
  if(date>s.date)return {code:'INVALID_TRANSITION',msg:'A future day cannot be corrected.'};
  if(date===s.date)return {code:'INVALID_TRANSITION',msg:'Today’s attendance is marked on the Today view; corrections are for past days.'};
  if(date<w.from)return {code:'INVALID_TRANSITION',msg:`Only the open pay month can be corrected: ${dmy(w.from)} – ${dmy(w.to)}.`};
  return undefined;
}
/** Validates a correction of `date` to `entry` and works out its effect on the month's gross pay (> 0 = raises pay = the Owner approves).
 *  Shared by the HR_ATT_CORRECT / HR_REQUEST_DECIDE handlers and the correction dialog's preview. */
export function hrPlanCorrection(s:State,st:HRStaff,date:string,entry:HRDayEntry):{refusal:HRCorrectionRefusal}|{day:ReturnType<typeof hrDayEntry>;effect:number}{
  const no=(code:HRCorrectionRefusal['code'],msg:string)=>({refusal:{code,msg}});
  if(date<st.joined||(st.exitedOn&&date>st.exitedOn))return no('INVALID_TRANSITION',`${st.name} was not in service on ${dmy(date)}.`);
  const day=hrDayEntry(s,st,date);
  if(day.pendingLeave)return no('INVALID_TRANSITION','A pending leave request covers this day — decide it first.');
  if(hrSameEntry(day.entry,entry))return no('VALIDATION',`${dmy(date)} already holds ${hrEntryLabel(entry).toLowerCase()} — nothing to correct.`);
  if(entry.kind==='leave'){
    if(day.halfDayLeave)return no('INVALID_TRANSITION','A half-day leave covers this day; correct the attendance mark beside it instead.');
    // SCC leave port: the hotel's week-off set decides where leave can be (SCC isWeekOffDate); every category, any pay type (SCC has no unpaid-only rule).
    if(hrIsWeekOff(s,date))return no('VALIDATION','That day is in the hotel’s leave week-off set — no leave is recorded on it.');
    if((HR_TRACKED as string[]).includes(entry.type)){const t=entry.type as 'CL'|'SL'|'EL',left=hrEntitled(s,t,st.id)-hrLeaveUsed(s,st.id,date,t,day.oneDayLeave?.id??'');
      if(left<1)return no('VALIDATION',`Not enough ${t} balance: ${Math.max(0,left)} day${left===1?'':'s'} left in ${hrFy(date).label}.`);}
  }
  const ym=date.slice(0,7);
  return {day,effect:hrMonthGross(hrWithEntry(s,st,date,entry,day.oneDayLeave?.id),st,ym)-hrMonthGross(s,st,ym)};
}
/** Requests waiting for the Owner (optionally one kind). */
export const hrPendingRequests=(s:State,kind?:HRRequest['kind'])=>(hrSlice(s).requests??[]).filter(r=>r.status==='Pending'&&(!kind||r.kind===kind));
/** The unmarked past days of a month for one staff member (what the pay-run warning counts as absent) — the correction dialog opens on the first. */
export const hrUnmarkedDates=(s:State,st:HRStaff,ym:string)=>hrMonthDates(ym).filter(d=>d<s.date&&hrDayStatus(s,st,d).kind==='unmarked');

/** Pending leave records (one per date) a user may decide (excludes own requests and own leave — DEC-119). */
export function hrDecidable(s:State,uid:string){
  if(!hrCanManage(s,uid))return [];
  const me=hrStaffOfUser(s,uid);
  return hrSlice(s).leaves.filter(l=>l.status==='Pending'&&l.requestedBy!==uid&&l.staffId!==me?.id);
}
/** SCC allPendingLeaves: every pending date record, by date (the Approvals list and its badge). */
export const hrPendingLeaves=(s:State)=>hrSlice(s).leaves.filter(l=>l.status==='Pending').sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:0);

// ── Payslip delivery (DEC-056): PDF via the share sheet + simulated WhatsApp ──
/** WhatsApp number: digits only, 10 digits → +91 (SCC waEmpMessage). Empty when unusable. */
export function hrWaNumber(phone:string){let d=String(phone??'').replace(/[^0-9]/g,'');if(d.length===10)d='91'+d;return /^91\d{10}$/.test(d)?d:'';}
const fmtRs=(p:number)=>'Rs.'+(p/100).toLocaleString('en-IN',{minimumFractionDigits:2,maximumFractionDigits:2});
export function hrPayslipMessage(s:State,run:HRRun,l:HRLine){
  return `Namaste ${l.name.split(' ')[0]}, your salary for ${hrMonthLabel(run.month)} (${s.settings.hotel}):\nPayable days: ${l.payableDays}\nGross: ${fmtRs(l.gross)}\nDeductions: ${fmtRs(l.deductions)}\nNet pay: ${fmtRs(l.net)}\nPayslip PDF attached. — ${s.settings.hotel} (simulated WhatsApp · fictional data)`;
}
/** Amount in words, Indian system (SCC slipWords), with paise. */
export function hrAmountWords(paise:number){
  const a=['','One','Two','Three','Four','Five','Six','Seven','Eight','Nine','Ten','Eleven','Twelve','Thirteen','Fourteen','Fifteen','Sixteen','Seventeen','Eighteen','Nineteen'];
  const b=['','','Twenty','Thirty','Forty','Fifty','Sixty','Seventy','Eighty','Ninety'];
  const t2=(n:number)=>n<20?a[n]:b[Math.floor(n/10)]+(n%10?' '+a[n%10]:'');
  const t3=(n:number)=>{const h=Math.floor(n/100),r=n%100;return (h?a[h]+' Hundred'+(r?' ':''):'')+(r?t2(r):'');};
  const words=(num:number)=>{if(num===0)return 'Zero';const p:string[]=[];const cr=Math.floor(num/1e7);num%=1e7;const lk=Math.floor(num/1e5);num%=1e5;const th=Math.floor(num/1e3);num%=1e3;
    if(cr)p.push(t3(cr)+' Crore');if(lk)p.push(t3(lk)+' Lakh');if(th)p.push(t3(th)+' Thousand');if(num)p.push(t3(num));return p.join(' ');};
  const neg=paise<0,v=Math.abs(paise),r=Math.floor(v/100),ps=v%100;
  return (neg?'Minus ':'')+'Rupees '+words(r)+(ps?' and '+t2(ps)+' Paise':'')+' Only';
}

// ── Full and final settlement (SCC fnfAssemble / fnfCompose) ──
/** Final-month salary (unless already paid in a locked run) + additions − statutory deductions − deductions − the outstanding advance, the
 *  advance only up to what the settlement covers (DEC-074 / A04-06, server FnfRules.Compute): recovered = earnings − statutory − deductions,
 *  clamped to 0…outstanding; the rest stays an open advance owed by the leaver. A negative net (deductions beyond the earnings) recovers no advance. */
export function hrFnfCompute(s:State,st:HRStaff,adjustments:HRFnfAdj[]){
  const x=hrSlice(s),lastDay=st.exitedOn??s.date,month=lastDay.slice(0,7),rates=hrRatesFor(s,month);
  const paid=x.runs.some(r=>r.month===month&&r.status==='Locked'&&r.lines.some(l=>l.staffId===st.id));
  const line=paid?undefined:hrComputeLine(st,hrCounts(s,st,month),rates,undefined,hrEsicCovered(s,st,month,rates));
  const advanceOutstanding=x.advances.filter(a=>a.staffId===st.id).reduce((n,a)=>n+hrAdvanceBalance(a),0);
  const add=adjustments.filter(a=>a.kind==='add').reduce((n,a)=>n+a.amount,0),ded=adjustments.filter(a=>a.kind==='ded').reduce((n,a)=>n+a.amount,0);
  const earnings=(line?line.gross:0)+add,statutory=line?line.pf+line.esic+line.pt:0;
  const advanceRecovered=Math.min(Math.max(0,earnings-statutory-ded),Math.max(0,advanceOutstanding)),recoveries=statutory+advanceRecovered+ded;
  return {lastDay,month,salaryIncluded:!paid,line,rates,advanceOutstanding,advanceRecovered,advanceLeftOpen:advanceOutstanding-advanceRecovered,earnings,recoveries,net:earnings-recoveries};
}
/** DEC-074: the part of the advance a stored settlement recovered (recoveries − statutory − deductions; settlements made before the rule recovered all of it). */
export function hrFnfAdvanceRecovered(f:HRFnf){
  const ded=f.adjustments.filter(a=>a.kind==='ded').reduce((n,a)=>n+a.amount,0),statutory=f.line?f.line.pf+f.line.esic+f.line.pt:0;
  return f.recoveries-statutory-ded;
}
/** DEC-074: the advance a settlement did not cover — still open and owed by the leaver. */
export const hrFnfAdvanceLeftOpen=(f:HRFnf)=>Math.max(0,f.advanceOutstanding-hrFnfAdvanceRecovered(f));

// ── Settings for the Master configuration hub (W05-A wires them; group "People, payroll and leave") ──
export type HRSettingDef={key:string;group:'People, payroll and leave';category:'Statutory rates'|'Leave policy';label:string;
  type:'percent'|'money'|'integer'|'text'|'fixed';unit:string;editable:boolean;approval:string;effective:string;help:string;
  visibility:string;approver:'none'|'Owner';dated:boolean;affects:string;command?:string;fields?:(keyof HRRates|string)[]};
const OWNER_PAY='Owner only (payroll and statutory, DEC-058); never self-approved';
const DATED='From a month (effective-from 1st); locked pay-runs keep their snapshot';
export function hrSettingDefs():HRSettingDef[]{
  const rate=(key:string,label:string,type:HRSettingDef['type'],unit:string,fields:(keyof HRRates)[],help:string):HRSettingDef=>({key,group:'People, payroll and leave',category:'Statutory rates',label,type,unit,editable:true,
    approval:OWNER_PAY,effective:DATED,help:help+' Illustrative — CA to confirm for a hotel.',visibility:'Owner, Accountant',approver:'Owner',dated:true,
    affects:'Draft and future payroll runs, payslips, bank and statutory files; locked runs never change',command:'HR_STAT_RATES_SET',fields});
  const leave=(key:string,label:string,type:HRSettingDef['type'],unit:string,fields:string[],help:string,editable=true):HRSettingDef=>({key,group:'People, payroll and leave',category:'Leave policy',label,type,unit,editable,
    approval:editable?OWNER_PAY:'Set per staff member in Staff and payroll',effective:editable?'Immediately; new leave requests only':'—',help,visibility:'Owner, Manager, Accountant',approver:editable?'Owner':'none',dated:false,
    affects:'Leave requests, balances and the leave calendar',...(editable?{command:'HR_LEAVE_POLICY_SET'}:{}),fields});
  return [
    rate('hr.structure','Salary structure (structured pay)','percent','% basic of earned · HRA and washing % of basic',['basicBps','hraBps','washBps'],'SCC: basic 50%, HRA 80% of basic, washing 10% of basic, special = rest.'),
    rate('hr.pf','Provident Fund','percent','% of PF wages · wage cap ₹',['pfEeBps','pfErBps','pfWageCap'],'Employee 12%, employer 13% on basic capped at ₹15,000 (SCC).'),
    rate('hr.esic','ESIC','percent','% of earned − washing · ceiling ₹',['esicEeBps','esicErBps','esicCeiling','esicPeriod'],'Employee 0.75%, employer 3.25% on earned − washing. Coverage: SCC month by month (earned ≤ ₹21,000) by default (DEC-115); optional contribution-period rule (decided at the start of Apr–Sep / Oct–Mar from the contractual wage ≤ ₹21,000, kept to the period end); off = SCC month-by-month test on earned pay.'),
    rate('hr.pt','Professional tax (Maharashtra)','money','₹ per month by slab',['ptMaleExempt','ptMaleMid','ptMaleMidAmt','ptStd','ptFeb','ptFemaleExempt'],'Men nil ≤ ₹7,500, ₹175 to ₹10,000, then ₹200 (₹300 in February); women nil ≤ ₹25,000 (SCC ptFor).'),
    rate('hr.days','Total days of a month','integer','days (0 = calendar days)',['daysOverride'],'SCC "Days Override": the month counts this many total days instead of its calendar days; paid holidays of the Holiday calendar are taken off either way (SCC "Mgmt holidays").'),
    rate('hr.ot','OT day rate','percent','× a day’s pay (gross ÷ total days)',['otBps'],'SCC pays each OT day — salary days beyond the total days — at gross ÷ total days (1×). No per-minute overtime (DEC-115).'),
    rate('hr.late','Late marks rule','integer','late marks per ½-day loss of pay',['lateBlock'],'SCC: 3 late marks = ½ day.'),
    rate('hr.rounding','Statutory rounding','text','paise / rupee',['rounding'],'SCC rounds to paise; "rupee" rounds PF to the nearest rupee and ESIC up to the next rupee.'),
    leave('hr.leave.ent','Yearly leave entitlement','integer','days per financial year (CL / SL / EL)',['ent'],'SCC ENT_DEFAULTS: casual 12, sick 7, earned 15 per April–March year; any number of days ≥ 0 (halves allowed, no maximum). A staff member can have an override (Staff and payroll › Leave › Settings). Going over the quota warns; the person recording can still record it (SCC).'),
    leave('hr.leave.cap','Leave records per day','fixed','3 per date for the whole hotel (pending + approved)',[],`SCC MAX_DAY = ${HR_MAX_DAY} (fixed): a full date is skipped, the other dates of the request are still recorded. SCC MAX_SHIFT = ${HR_MAX_SHIFT} per shift is ported too (MAX_DAY binds first). Department caps are advisory only (SCC per-store caps; Staff and payroll › Leave › Settings).`,false),
    leave('hr.leave.half','Half-day leave','fixed','morning (till 2 PM) / afternoon (after 5 PM) = 0.5 day',[],'Always on (SCC leaveDaysOf); a half-day request may cover several dates (one half per date).',false),
    leave('hr.leave.weeklyoff','Leave week-off days','fixed','one set for the hotel',[],'Dates on these weekdays are skipped when leave is recorded and counted (SCC isWeekOffDate). HCC default: none (a 7-day hotel); SCC’s default is Saturday + Sunday. Staff weekly offs stay for the roster only. Edited in Staff and payroll › Leave › Settings.',false),
    // DEC-083: the dated paid-holiday calendar is kept in Staff & payroll › Leave › Holidays (Owner); shown read-only here.
    {...leave('hr.leave.holidays','Paid holidays','fixed','dated list per property',[],'Each paid holiday shortens its month’s total days (SCC "Mgmt holidays"); nobody loses pay for it. Not skipped inside leave dates (SCC: store holidays are blackout markers). The hotel has none (DEC-114).',false),
      approval:'Owner — Staff & payroll › Leave › Holidays',effective:'From the holiday’s date; a locked pay month keeps its holidays',dated:true},
  ];
}
const pctOf=(bps:number)=>`${bps/100}%`;
const rs=(p:number)=>'₹'+(p/100).toLocaleString('en-IN');
/** One-line current value of an HR setting (for hub summaries). */
export function hrSettingValue(s:State,key:string,ym=s.date.slice(0,7)):string{
  const r=hrRatesFor(s,ym),p=hrLeavePolicy(s);
  switch(key){
    case 'hr.structure':return `Basic ${pctOf(r.basicBps)} · HRA ${pctOf(r.hraBps)} · washing ${pctOf(r.washBps)} (from ${r.from})`;
    case 'hr.pf':return `${pctOf(r.pfEeBps)} + ${pctOf(r.pfErBps)} on wages up to ${rs(r.pfWageCap)}`;
    case 'hr.esic':return `${pctOf(r.esicEeBps)} + ${pctOf(r.esicErBps)} · ${r.esicPeriod?`covered for the contribution period when the contractual wage ≤ ${rs(r.esicCeiling)}`:`while earned ≤ ${rs(r.esicCeiling)} (month by month)`}`;
    case 'hr.pt':return `Men ${rs(r.ptMaleMidAmt)} / ${rs(r.ptStd)} (Feb ${rs(r.ptFeb)}) · women exempt ≤ ${rs(r.ptFemaleExempt)}`;
    case 'hr.days':return r.daysOverride?`${r.daysOverride} days a month`:'Calendar days (SCC auto)';
    case 'hr.ot':return `${r.otBps/10000}× a day’s pay per OT day`;
    case 'hr.late':return r.lateBlock?`${r.lateBlock} late = ½ day`:'Off';
    case 'hr.rounding':return r.rounding==='paise'?'To paise (SCC)':'PF nearest ₹ · ESIC next ₹';
    case 'hr.leave.ent':return `CL ${p.ent.CL} · SL ${p.ent.SL} · EL ${p.ent.EL} days / FY`;
    case 'hr.leave.cap':return `${HR_MAX_DAY} per day for the hotel (SCC)${Object.keys(p.deptCap).length?` · ${Object.keys(p.deptCap).length} advisory department cap${Object.keys(p.deptCap).length===1?'':'s'}`:''}`;
    case 'hr.leave.half':return 'On';
    case 'hr.leave.weeklyoff':{const w=hrLeaveWeekOff(s);return w.length?w.map(d=>HR_WEEKDAYS[d]).join(' + '):'None (every day can be leave)';}
    case 'hr.leave.holidays':{const fy=hrFy(s.date),n=hrPaidHolidays(s).filter(h=>h.date>=fy.from&&h.date<=fy.to).length;return n?`${n} in ${fy.label}`:'None yet — the Owner adds them';}
    default:return '—';
  }
}
