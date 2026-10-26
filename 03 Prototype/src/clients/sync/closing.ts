// Closing area (W4-4): day closing, closing records, the real business date, closed dates.
// Server: GET /closing/business-date (any session; added in W4-4), GET /closing/records?from&to (bulk closing snapshots; added in W4-4),
// POST /commands/PostRoomNights (added in W4-4), POST /commands/RunBusinessDayClose (resumable: preflight → nights → cash → close + snapshot → next day).
//
//   loaders   date        PATCH on core's provisional date: the server's business date (DEC-042).
//             closings    the stored closing records of the last 62 business dates (Reports → history, Day closing → "Earlier closing records", closing report, day pack).
//             closedDates the dates of those records, plus the current date when it is closed and the next day is not opened yet (isDayClosed).
//   commands  CLOSE_RUN / NIGHT_POST → PostRoomNights (no interrupt demo)     DAY_CLOSE / NEXT_DAY → RunBusinessDayClose (one resumable run; the server also
//             opens the next day at the end of a successful run, so after DAY_CLOSE the reload shows the next open date; NEXT_DAY resumes a run that stopped after the close).
//             DEC-077: DAY_REOPEN → ReopenBusinessDay {businessDate, reason} (Owner); DAY_RECLOSE → RunBusinessDayClose for the reopened date
//             (re-runs its checks, replaces its closing record; the business date does not move). The records view carries the reopens and the
//             replaced records (attached to their date); the open reopen's date comes from GET /closing/business-date (reopenedDate) and is
//             read on its own when it lies outside the 62-day window.
//             DEC-078 (Wave B1, APK12-3): the close no longer opens the next day's cash shift (it used to open it in the CLOSER's name, so Reception could
//             not take cash next morning without a Manager). Whoever takes the desk opens their own shift with a handover count (SHIFT_OPEN, sync/cashier.ts).
//   not loaded here: closeProgress (the server posts the night step per stay and records it as the charges themselves; the demo interruption is not available),
//             preflight (the screen derives it from the loaded bookings/approvals/expenses/POS/shift slices; the server re-checks every blocker on close and refuses with the list),
//             reports/exports/overview (computed on the client from slices; see reportPackFromServer in closing/figures.ts and the W4-4 report).
import type {SyncArea,CommandMapper,LoaderCtx} from './types';
import {addDays} from '../../domain/money';
import {isIsoDate} from '../wire';
import {closingsFromServer,type SrvBusinessDate,type SrvRecords} from './closing/figures';
import {CASH_SLICES} from './money';

export {reportPackFromServer,closingFromServer,closingsFromServer,reopenFromServer} from './closing/figures';

const HISTORY_DAYS=62;
const bizDate=(c:LoaderCtx)=>c.api.get<SrvBusinessDate>('/closing/business-date');

const nightsMapper=(type:string):CommandMapper=>({type,reload:['bookings'],
  precheck:(cmd,s)=>cmd.interruptAfter!==undefined?'The interrupted night-audit demo is not available on the server: it posts every night once.':!isIsoDate(s.date)?'The business date is not loaded yet. Refresh and try again.':undefined,
  async send(c){const r=await c.api.command<{postedNow:number;stays:number}>('PostRoomNights',{businessDate:c.state.date});return {result:r};}});

const closeMapper=(type:string):CommandMapper=>({type,reload:['date','closedDates','closings','bookings','clock'],
  precheck:(cmd,s)=>!isIsoDate(s.date)?'The business date is not loaded yet. Refresh and try again.':undefined,
  async send(c){const r=await c.api.command<{status:string}>('RunBusinessDayClose',{businessDate:c.state.date,runId:'close-'+c.state.date});
    // DEC-078: no OpenShift here (APK12-3). A Completed run opens the next business date with every till's shift closed; the cash desk reloads
    // so the screens show "Open my cashier shift" to whoever takes the desk next.
    return r?.status==='Completed'?{result:r,reload:CASH_SLICES}:{result:r};}});

export const closing:SyncArea={
  id:'closing',
  loaders:[
    {slice:'date',kind:'patch',async load(c){return (await bizDate(c)).businessDate;}},
    {slice:'closings',needs:['date'],requires:'reports',reads:['GET /closing/records'],async load(c){
      const to=c.current('date'),from=addDays(to,-(HISTORY_DAYS-1));const r=await c.api.get<SrvRecords>('/closing/records',{from,to});
      const out=closingsFromServer(r);
      // DEC-077: a reopened day older than the window still needs its record on the Day closing screen (close it again).
      const open=r.reopens?.find(x=>!x.reclosedUtc);
      if(open&&!out[open.businessDate])Object.assign(out,closingsFromServer(await c.api.get<SrvRecords>('/closing/records',{from:open.businessDate,to:open.businessDate})));
      return out;}},
    {slice:'closedDates',needs:['closings'],async load(c){
      const set=new Set(Object.keys(c.current('closings')??{}));
      const b=await bizDate(c);if(b.closed)set.add(b.businessDate);
      return [...set].sort();}},
  ],
  commands:[nightsMapper('CLOSE_RUN'),nightsMapper('NIGHT_POST'),closeMapper('DAY_CLOSE'),closeMapper('NEXT_DAY'),
    // DEC-077: the Owner reopens a closed day (PERM-CLS-004 on the server; the precheck repeats the prototype's wording).
    {type:'DAY_REOPEN',reload:['closings','closedDates','accountingX'],
      precheck:cmd=>!isIsoDate(String(cmd.date??''))?'Choose a closed business date.':!String(cmd.reason??'').trim()?'A reason is required to reopen a closed business day.':undefined,
      async send(c){const r=await c.api.command<{reopen:unknown;message:string}>('ReopenBusinessDay',{businessDate:c.cmd.date,reason:String(c.cmd.reason).trim()});return {result:r};}},
    // Closing the reopened day again is RunBusinessDayClose for that date (its own run id; the business date does not move).
    {type:'DAY_RECLOSE',reload:['closings','closedDates','bookings','accountingX'],
      precheck:cmd=>isIsoDate(String(cmd.date??''))?undefined:'Choose the reopened business date.',
      async send(c){const r=await c.api.command<{status:string}>('RunBusinessDayClose',{businessDate:c.cmd.date,runId:'reclose-'+c.cmd.date});return {result:r};}}],
  unmapped:{
    CORRECTION_ADJUST:'Server corrections (RequestAdjustment, action correction) reverse one whole posted charge line by id; the prototype posts a free-form signed amount for a closed date. Needs a charge picker (UI) and folio/charge ids from the money area (W4-3).',
    CORRECTION_ADJUST_DECIDE:'DecideApproval needs the approval id + version from the approvals slice, which the money area (W4-3) owns; map it there together with SHIFT_DIFFERENCE_DECIDE.',
    POST_NIGHTS:'M1 compatibility command with no UI button; CLOSE_RUN covers it.',
  },
};
