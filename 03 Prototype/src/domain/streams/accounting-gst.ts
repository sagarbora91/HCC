// ACC-2 · GST month end, the month lock and GST on booking advances (02 Planning/agents/ACC2-POSTING-CONTRACT.md §4 advances switch, §6, §9;
// rulebook §4.6, §4.12, §4.15, §5 A6 / A6a / A6b, D5, E1–E5, §G1, §8.6; DEC-131, DEC-142). Exported names start with 'acc' / 'ACC'.
//
//   • accGstSetoff(output, credit, cash) — the s.49 / 49A / 49B / Rule 88A order: IGST credit first (IGST, then CGST / SGST — "any order", HCC
//     puts it where the head's own credit falls short, then exhausts it), CGST credit → CGST then IGST, SGST credit → SGST then IGST (only after
//     the CGST credit, s.49(5)(d) proviso), CGST and SGST never cross. Whatever output is left is paid from the electronic cash ledger 1340
//     (E1: Dr output · Cr 1340); the challan is that less the cash already in 1340 (OTA TCS, earlier deposits). Same function on the server
//     (Hcc.Domain.Accounting.GstSetOff), same six hand-worked cases.
//   • accGstMonth(s, 'YYYY-MM') — the month's output by head (2110 / 2120 / 2130), input by head (1310 / 1320 / 1330), RCM 2140, TCS and the
//     1340 balance, from the books (every voucher, the set-off journals left out); credit and cash carried from earlier months net of what the
//     earlier posted set-offs used.
//   • The set-off journal (source {kind:'tax', sub:'gst-setoff'}) is DERIVED like every other voucher: accountingX.gstSetoffs[month] holds its
//     id `gst-setoff:<month>@<date>` (date = the business date it was posted on, after the month ended — §8.6 posts April's in May), and
//     accLiveVouchers derives its lines from accGstMonth on that date (locked days keep their snapshot).
//   • GST on booking advances (A6 switch, DEC-142 default OFF): with the firm profile's gstOnAdvances on, a folio deposit receipt also posts
//     {kind:'tax', sub:'advance-gst'} Dr 1350 · Cr 2110 · Cr 2120, cum-tax on the amount at the booked room's rate (GST master, per unit per
//     night on the booking's rate, arrival date); a deposit applied (receipt kind 'deposit-apply', acc2-flows) or returned posts the reversal
//     {sub:'advance-gst-reversal'} Dr 2110 · Dr 2120 · Cr 1350, never more than the booking's advance GST still held.
//   • The month lock (G1): accMonthLockBlocker / accMonthLocked; the commands are in handlers/accounting-gst.ts.
import type {State} from '../types';
import {money} from '../money';
import {gstForRoomNight,gstSplit} from '../gst';
import {accSlice,accVouchersUpTo,accDayStatus,accLockOf,accLockBlocker,accLiveVouchers,type ACCEntry,type ACCVoucher,type ACCMonthLock} from './accounting';
import {accControlOf} from './accounting-parties';
import {cmpSlice} from './compliance';

// ── Ledgers (rulebook §3.2) ──
export const ACC_GST_LEDGER={outCgst:'2110',outSgst:'2120',outIgst:'2130',inCgst:'1310',inSgst:'1320',inIgst:'1330',cash:'1340',advance:'1350',rcm:'2140'} as const;
export const ACC_GST_SETOFF_SUB='gst-setoff';
export const ACC_ADVANCE_GST_SUB='advance-gst';
export const ACC_ADVANCE_GST_REVERSAL_SUB='advance-gst-reversal';

// ── s.49 set-off (pure) ──
export type ACCGstHeads={igst:number;cgst:number;sgst:number};
/** The set-off of one month (paise). `use.<credit head>.<output head>` = credit of that head used against that output head. */
export type ACCGstSetoff={output:ACCGstHeads;credit:ACCGstHeads;cashAvailable:number;
  use:{igst:ACCGstHeads;cgst:ACCGstHeads;sgst:ACCGstHeads};
  /** Output left after the credit, paid from the electronic cash ledger (E1: Dr output · Cr 1340) = the net payable per head. */cash:ACCGstHeads;
  /** Credit carried to the next month. */creditLeft:ACCGstHeads;
  /** Σ cash heads. */payable:number;
  /** What the challan must deposit (E2): payable less the cash already in 1340 (never below zero). */challan:number};
const H0=():ACCGstHeads=>({igst:0,cgst:0,sgst:0});
const pos=(n:number)=>Math.max(0,Math.trunc(Number(n)||0));
/** The s.49 / 49A / 49B order (rulebook §4.12, E1). Inputs are clamped to ≥ 0 (a head with a debit balance has nothing to set off). */
export function accGstSetoff(output:ACCGstHeads,credit:ACCGstHeads,cashAvailable=0):ACCGstSetoff{
  const o={igst:pos(output.igst),cgst:pos(output.cgst),sgst:pos(output.sgst)},c={igst:pos(credit.igst),cgst:pos(credit.cgst),sgst:pos(credit.sgst)};
  const left={...o},cr={...c},use={igst:H0(),cgst:H0(),sgst:H0()};
  const take=(from:keyof ACCGstHeads,to:keyof ACCGstHeads,max=Infinity)=>{const n=Math.min(cr[from],left[to],max);if(n>0){cr[from]-=n;left[to]-=n;use[from][to]+=n;}};
  // 1. IGST credit: IGST first; then CGST / SGST where the head's own credit falls short; then (s.49A) the rest, until it is exhausted.
  take('igst','igst');
  take('igst','cgst',Math.max(0,left.cgst-cr.cgst));
  take('igst','sgst',Math.max(0,left.sgst-cr.sgst));
  take('igst','cgst');take('igst','sgst');
  // 2. CGST credit: CGST, then IGST.  3. SGST credit: SGST, then IGST (only what the CGST credit could not pay). Never CGST ↔ SGST.
  take('cgst','cgst');take('cgst','igst');
  take('sgst','sgst');take('sgst','igst');
  const payable=left.igst+left.cgst+left.sgst,cashIn=pos(cashAvailable);
  return {output:o,credit:c,cashAvailable:cashIn,use,cash:left,creditLeft:cr,payable,challan:Math.max(0,payable-cashIn)};
}
/** The set-off journal's lines (E1), merged per ledger: Dr each output head (credit used + cash) · Cr each input head used · Cr 1340 (cash). */
export function accGstSetoffEntries(x:ACCGstSetoff):ACCEntry[]{
  const L=ACC_GST_LEDGER,u=x.use;
  const dr=(account:string,n:number):ACCEntry=>({account,debit:n,credit:0}),cr=(account:string,n:number):ACCEntry=>({account,debit:0,credit:n});
  return [
    dr(L.outIgst,u.igst.igst+u.cgst.igst+u.sgst.igst+x.cash.igst),
    dr(L.outCgst,u.igst.cgst+u.cgst.cgst+x.cash.cgst),
    dr(L.outSgst,u.igst.sgst+u.sgst.sgst+x.cash.sgst),
    cr(L.inIgst,u.igst.igst+u.igst.cgst+u.igst.sgst),
    cr(L.inCgst,u.cgst.cgst+u.cgst.igst),
    cr(L.inSgst,u.sgst.sgst+u.sgst.igst),
    cr(L.cash,x.payable),
  ].filter(e=>e.debit||e.credit);
}

// ── Months ──
export const ACC_MONTH=/^\d{4}-(0[1-9]|1[0-2])$/;
export const accMonthOf=(date:string)=>String(date??'').slice(0,7);
/** First and last date of a month 'YYYY-MM'. */
export function accMonthRange(month:string):{from:string;to:string}{
  const y=+month.slice(0,4),m=+month.slice(5,7),last=new Date(Date.UTC(y,m,0)).getUTCDate();
  return {from:`${month}-01`,to:`${month}-${String(last).padStart(2,'0')}`};
}
/** Every date of a month, ascending. */
export function accMonthDates(month:string):string[]{
  const {to}=accMonthRange(month),n=+to.slice(8,10);
  return Array.from({length:n},(_,i)=>`${month}-${String(i+1).padStart(2,'0')}`);
}
const nextMonth=(m:string)=>{const y=+m.slice(0,4),mo=+m.slice(5,7);return mo===12?`${y+1}-01`:`${y}-${String(mo+1).padStart(2,'0')}`;};

// ── Posted set-offs (accountingX.gstSetoffs[month] = 'gst-setoff:<month>@<date>') ──
export const accGstSetoffId=(month:string,date:string)=>`gst-setoff:${month}@${date}`;
/** {month, date} of a set-off id, or undefined. */
export function accGstSetoffRef(id:string|undefined):{month:string;date:string}|undefined{
  const m=/^gst-setoff:(\d{4}-\d{2})@(\d{4}-\d{2}-\d{2})$/.exec(String(id??''));return m?{month:m[1],date:m[2]}:undefined;
}
/** The posted set-off of a month: its id and the business date it posted on. */
export function accGstSetoffPosted(s:State,month:string):{id:string;month:string;date:string}|undefined{
  const id=accSlice(s).gstSetoffs?.[month],r=accGstSetoffRef(id);return r&&id?{id,...r}:undefined;
}
const isSetoff=(v:ACCVoucher)=>v.source.kind==='tax'&&v.source.sub===ACC_GST_SETOFF_SUB;

/** One month's GST position (paise). Flows are Cr − Dr on output / RCM ledgers and Dr − Cr on input / cash ledgers, the set-off journals left out. */
export type ACCGstMonth={month:string;from:string;to:string;
  /** Output GST of the month by head (2130 / 2110 / 2120). */output:ACCGstHeads;
  /** ITC booked in the month by head (1330 / 1310 / 1320). */input:ACCGstHeads;
  /** ITC brought forward from earlier months (after their posted set-offs). */broughtForward:ACCGstHeads;
  /** Credit available for this month's set-off = brought forward + input. */credit:ACCGstHeads;
  /** RCM booked in the month (Cr 2140) and the 2140 balance at month end — settled only by a gst-payment challan (E3), never by set-off. */rcm:number;rcmBalance:number;
  /** TCS credited by OTAs in the month (Dr 1340 from OTA payouts). */tcs:number;
  /** 1340 at month end net of what earlier posted set-offs used (signed; negative = an earlier challan still unpaid). */cashBalance:number;
  /** GST paid on advances still held (1350 at month end, A6 switch). */advanceGst:number;
  setoff:ACCGstSetoff;
  /** The posted set-off journal (id, the date it posted on) and whether the month is locked. */posted?:{id:string;date:string};locked:boolean};

/** Re-entrancy guard: while the month flows are read, live derivation leaves the set-off journals out (the flows exclude them anyway). */
let flowsDepth=0;
export const accGstReadingFlows=()=>flowsDepth>0;
type Flow={out:ACCGstHeads;inp:ACCGstHeads;cash:number;rcm:number;tcs:number;adv:number};
const F0=():Flow=>({out:H0(),inp:H0(),cash:0,rcm:0,tcs:0,adv:0});
function monthFlows(s:State,toDate:string):Map<string,Flow>{
  flowsDepth++;
  try{
    const m=new Map<string,Flow>(),L=ACC_GST_LEDGER;
    for(const v of accVouchersUpTo(s,toDate)){
      if(isSetoff(v))continue;
      const f=m.get(accMonthOf(v.date))??F0();m.set(accMonthOf(v.date),f);
      for(const e of v.entries){const a=accControlOf(e.account),cr=e.credit-e.debit,dr=e.debit-e.credit;
        if(a===L.outIgst)f.out.igst+=cr;else if(a===L.outCgst)f.out.cgst+=cr;else if(a===L.outSgst)f.out.sgst+=cr;
        else if(a===L.inIgst)f.inp.igst+=dr;else if(a===L.inCgst)f.inp.cgst+=dr;else if(a===L.inSgst)f.inp.sgst+=dr;
        else if(a===L.cash){f.cash+=dr;if(v.source.kind==='ota')f.tcs+=dr;}
        else if(a===L.rcm)f.rcm+=cr;else if(a===L.advance)f.adv+=dr;}
    }
    return m;
  }finally{flowsDepth--;}
}
const add=(a:ACCGstHeads,b:ACCGstHeads):ACCGstHeads=>({igst:a.igst+b.igst,cgst:a.cgst+b.cgst,sgst:a.sgst+b.sgst});
const usedOf=(x:ACCGstSetoff):ACCGstHeads=>({igst:x.credit.igst-x.creditLeft.igst,cgst:x.credit.cgst-x.creditLeft.cgst,sgst:x.credit.sgst-x.creditLeft.sgst});
const sub=(a:ACCGstHeads,b:ACCGstHeads):ACCGstHeads=>({igst:a.igst-b.igst,cgst:a.cgst-b.cgst,sgst:a.sgst-b.sgst});

/** The GST position of `month` (contract §6). Reads every voucher up to the month's last day (locked days: their snapshots). */
export function accGstMonth(s:State,month:string):ACCGstMonth{
  const {from,to}=accMonthRange(month),flows=monthFlows(s,to),posted=accSlice(s).gstSetoffs??{};
  const months=[...flows.keys()].filter(m=>m<=month).sort();
  let credit=H0(),cash=0,rcmBal=0,adv=0;
  const empty=F0();
  for(const m of months.length&&months[0]<month?(()=>{const out:string[]=[];for(let x=months[0];x<month;x=nextMonth(x))out.push(x);return out;})():[]){
    const f=flows.get(m)??empty;credit=add(credit,f.inp);cash+=f.cash;rcmBal+=f.rcm;adv+=f.adv;
    if(posted[m]){const x=accGstSetoff(f.out,credit,cash);credit=sub(credit,usedOf(x));cash-=x.payable;}
  }
  const f=flows.get(month)??empty,bf=credit,avail=add(credit,f.inp),cashBal=cash+f.cash;
  const setoff=accGstSetoff(f.out,avail,cashBal);
  const p=accGstSetoffPosted(s,month);
  return {month,from,to,output:f.out,input:f.inp,broughtForward:bf,credit:avail,rcm:f.rcm,rcmBalance:rcmBal+f.rcm,tcs:f.tcs,cashBalance:cashBal,advanceGst:adv+f.adv,
    setoff,...(p?{posted:{id:p.id,date:p.date}}:{}),locked:accMonthLocked(s,from)};
}

/** The set-off journals posted on `date` (derived; contract §6 E1). Left out while the month flows are being read (re-entrancy guard). */
export function accGstSetoffVouchers(s:State,date:string):Omit<ACCVoucher,'no'>[]{
  if(flowsDepth>0)return [];
  const out:Omit<ACCVoucher,'no'>[]=[];
  for(const [month,id] of Object.entries(accSlice(s).gstSetoffs??{}).sort()){
    const r=accGstSetoffRef(id);if(!r||r.date!==date||r.month!==month)continue;
    const g=accGstMonth(s,month),entries=accGstSetoffEntries(g.setoff);
    if(entries.length)out.push({date,type:'Journal',narration:`GST set-off for ${month} (s.49 order) · output ${money(g.setoff.output.cgst+g.setoff.output.sgst+g.setoff.output.igst)} · ITC used ${money(entries.filter(e=>e.credit&&e.account!==ACC_GST_LEDGER.cash).reduce((a,e)=>a+e.credit,0))} · cash ledger ${money(g.setoff.payable)} · challan ${money(g.setoff.challan)}`,
      source:{kind:'tax',id,sub:ACC_GST_SETOFF_SUB},entries,unmapped:[]});
  }
  return out;
}

// ── GST on booking advances (A6 / A6a / A6b; DEC-142 switch, default off) ──
/** The firm profile's switch (Owner-only money setting). Off when no profile exists. */
export const accGstOnAdvances=(s:State)=>cmpSlice(s).profile?.gstOnAdvances===true;
/** GST inside a cum-tax amount at `bps` (half away from zero), split CGST / SGST as output GST (gstSplit). Signed. */
export function accCumTaxGst(amount:number,bps:number):{tax:number;cgst:number;sgst:number}{
  if(!bps||!amount)return {tax:0,cgst:0,sgst:0};
  const a=Math.abs(amount),taxable=Math.floor((a*10000*2+(10000+bps))/(2*(10000+bps))),t=(a-taxable)*Math.sign(amount),g=gstSplit(t);
  return {tax:t,...g};
}
/** The rate of the booked room (GST master, per unit per night on the booking's rate, on the arrival date). */
export const accAdvanceGstBps=(s:State,b:{arrival:string;rate:number})=>gstForRoomNight(s,b.arrival,b.rate).bps;
const ADV_IN=['deposit'],ADV_OUT=['deposit-apply','deposit-return'];
/** Advance-GST vouchers of `date` (switch on only): Dr 1350 · Cr 2110 · Cr 2120 per deposit; the reversal per deposit applied / returned,
 *  capped by the booking's advance GST still held (an advance received before the switch, or the opening advance, has none to reverse). */
export function accAdvanceGstVouchers(s:State,date:string):Omit<ACCVoucher,'no'>[]{
  if(!accGstOnAdvances(s))return [];
  const out:Omit<ACCVoucher,'no'>[]=[],L=ACC_GST_LEDGER;
  for(const b of s.bookings){
    const rs=b.receipts.map((r,i)=>({r,i})).filter(({r})=>r.amount>0&&(ADV_IN.includes(r.kind)||ADV_OUT.includes(r.kind as string))&&r.date<=date);
    if(!rs.some(({r})=>r.date===date))continue;
    rs.sort((x,y)=>x.r.date<y.r.date?-1:x.r.date>y.r.date?1:x.i-y.i);
    const bps=accAdvanceGstBps(s,b);let held=0;
    for(const {r} of rs){
      const g=accCumTaxGst(r.amount,bps);
      if(ADV_IN.includes(r.kind)){held+=g.tax;
        if(r.date===date&&g.tax)out.push({date,type:'Journal',narration:`GST on advance (receipt voucher, GSTR-1 11A) · ${b.guest} · ${b.id} · ${money(r.amount)} at ${bps/100} %`,
          source:{kind:'tax',id:`advance-gst:${b.id}/${r.id}`,sub:ACC_ADVANCE_GST_SUB,booking:b.id},
          entries:[{account:L.advance,debit:g.tax,credit:0},{account:L.outCgst,debit:0,credit:g.cgst},{account:L.outSgst,debit:0,credit:g.sgst}],unmapped:[]});
      }else{
        const t=Math.min(g.tax,held),x=gstSplit(t);held-=t;
        if(r.date===date&&t>0)out.push({date,type:'Journal',narration:`Advance GST reversed (${r.kind==='deposit-return'?'refund voucher':'adjusted against the invoice, GSTR-1 11B'}) · ${b.guest} · ${b.id} · ${money(r.amount)}`,
          source:{kind:'tax',id:`advance-gst:${b.id}/${r.id}`,sub:ACC_ADVANCE_GST_REVERSAL_SUB,booking:b.id},
          entries:[{account:L.outCgst,debit:x.cgst,credit:0},{account:L.outSgst,debit:x.sgst,credit:0},{account:L.advance,debit:0,credit:t}].filter(e=>e.debit||e.credit),unmapped:[]});
      }
    }
  }
  return out;
}
/** Everything this lane adds to accLiveVouchers for `date` (the advance-GST lines, then the set-off journals). */
export function accGstLiveVouchers(s:State,date:string):Omit<ACCVoucher,'no'>[]{
  return [...accAdvanceGstVouchers(s,date),...accGstSetoffVouchers(s,date)];
}

// ── Month lock (G1, contract §6; DEC-131 unlock) ──
/** The month lock record of `month` while it is in force (a later unlock lifts it until the month is locked again). */
export function accMonthLockOf(s:State,month:string):ACCMonthLock|undefined{
  const r=accSlice(s).monthLocks?.[month];if(!r)return undefined;
  const last=r.unlocks?.at(-1);return last&&last.at>=r.lockedAt?undefined:r;
}
/** True when `date` (YYYY-MM-DD) falls in a locked month. */
export const accMonthLocked=(s:State,date:string)=>!!accMonthLockOf(s,accMonthOf(date));
export const accMonthLockedMessage=(month:string)=>`Month ${month} is locked (GST set-off posted, books closed) — the Owner unlocks the month first (DEC-131).`;
/** Pending manual vouchers dated in the month (the manual lane keeps them in accountingX.manual). */
export const accMonthPendingManual=(s:State,month:string)=>(accSlice(s).manual??[]).filter(v=>v.status==='pending'&&accMonthOf(v.date)===month);
/** Why `month` cannot be locked now ('' = it can; permission is checked separately). Days closed but not locked are fine: the lock locks them. */
export function accMonthLockBlocker(s:State,month:string):string{
  if(!ACC_MONTH.test(String(month??'')))return 'Choose a month (YYYY-MM).';
  const {to}=accMonthRange(month);
  if(to>=s.date)return `The month ${month} is not over: its last day ${to} must be a closed business day.`;
  if(accMonthLockOf(s,month))return `The month ${month} is already locked.`;
  if(!accGstSetoffPosted(s,month))return `Post the GST set-off for ${month} first (rulebook E1).`;
  const pend=accMonthPendingManual(s,month);
  if(pend.length)return `${pend.length} manual voucher${pend.length===1?' is':'s are'} pending approval in ${month} (${pend[0].id}) — the Owner decides ${pend.length===1?'it':'them'} first.`;
  for(const d of accMonthDates(month)){
    if(accLockOf(s,d))continue;
    if(accDayStatus(s,d)==='Open')return `${d} is not closed — close every business day of ${month} first.`;
    const why=accLockBlocker(s,d);if(why)return `${d}: ${why}`;
  }
  return '';
}
/** Days of the month the lock still has to lock (closed, not locked), ascending. */
export const accMonthDaysToLock=(s:State,month:string)=>accMonthDates(month).filter(d=>!accLockOf(s,d));
/** The snapshot a day lock stores (as ACC_LOCK does). */
export function accDayLockSnapshot(s:State,date:string){const vouchers=accLiveVouchers(s,date);
  return {vouchers,debit:vouchers.reduce((a,v)=>a+v.entries.reduce((b,e)=>b+e.debit,0),0),credit:vouchers.reduce((a,v)=>a+v.entries.reduce((b,e)=>b+e.credit,0),0)};}
/** Months with a lock request in the audit trail and no lock in force since (ACC_MONTH_LOCK_REQUEST; the Owner sees them). */
export function accMonthLockRequests(s:State):{month:string;at:string;by:string}[]{
  const seen=new Set<string>(),out:{month:string;at:string;by:string}[]=[];
  for(const a of s.audit){if(a.action!=='ACC MONTH LOCK REQUEST')continue;const m=/^Month lock requested · (\d{4}-\d{2})/.exec(a.detail)?.[1];
    if(!m||seen.has(m))continue;seen.add(m);const r=accSlice(s).monthLocks?.[m];if(accMonthLockOf(s,m)||(r&&r.lockedAt>=a.at))continue;out.push({month:m,at:a.at,by:a.actor});}
  return out;
}

// ── Statutory remittances (D5) and GST payment (E2 / E3) — lines of the manual-voucher templates ──
export type ACCStatKind='pf'|'esic'|'pt';
export const ACC_STAT_LEDGER:Record<ACCStatKind,string>={pf:'2310',esic:'2320',pt:'2330'};
export const ACC_STAT_LABEL:Record<ACCStatKind,string>={pf:'PF',esic:'ESIC',pt:'Professional tax'};
/** The compliance obligation kind (CMP_LEGACY_CARDS) whose card a remittance marks done. */
export const ACC_STAT_CARD_KIND:Record<ACCStatKind,string>={pf:'EPF',esic:'ESIC',pt:'PT'};
