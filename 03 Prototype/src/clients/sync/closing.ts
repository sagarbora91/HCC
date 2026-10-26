// Closing area (W4-4): day closing, closing records, the real business date, closed dates.
// Server: GET /closing/business-date (any session; added in W4-4), GET /closing/records?from&to (bulk closing snapshots; added in W4-4),
// POST /commands/PostRoomNights (added in W4-4), POST /commands/RunBusinessDayClose (resumable: preflight → nights → cash → close + snapshot → next day).
//
//   loaders   date        PATCH on core's provisional date: the server's business date (DEC-042).
//             closings    the stored closing records of the last 62 business dates (Reports → history, Day closing → "Earlier closing records", closing report, day pack).
//             closedDates the dates of those records, plus the current date when it is closed and the next day is not opened yet (isDayClosed).
//   commands  CLOSE_RUN / NIGHT_POST → PostRoomNights (no interrupt demo)     DAY_CLOSE / NEXT_DAY → RunBusinessDayClose (one resumable run; the server also
//             opens the next day at the end of a successful run, so after DAY_CLOSE the reload shows the next open date; NEXT_DAY resumes a run that stopped after the close).
//             After a Completed run: OpenShift on the desk till (key <action>:shift, float carried by the server), when the till is known and PERM-SHF-001 is held.
//   not loaded here: closeProgress (the server posts the night step per stay and records it as the charges themselves; the demo interruption is not available),
//             preflight (the screen derives it from the loaded bookings/approvals/expenses/POS/shift slices; the server re-checks every blocker on close and refuses with the list),
//             reports/exports/overview (computed on the client from slices; see reportPackFromServer in closing/figures.ts and the W4-4 report).
import type {SyncArea,CommandMapper,LoaderCtx} from './types';
import {addDays} from '../../domain/money';
import {isIsoDate} from '../wire';
import {closingFromServer,type SrvBusinessDate,type SrvRecords} from './closing/figures';
import {CASH_SLICES} from './money';

export {reportPackFromServer,closingFromServer} from './closing/figures';

const HISTORY_DAYS=62;
const bizDate=(c:LoaderCtx)=>c.api.get<SrvBusinessDate>('/closing/business-date');

const nightsMapper=(type:string):CommandMapper=>({type,reload:['bookings'],
  precheck:(cmd,s)=>cmd.interruptAfter!==undefined?'The interrupted night-audit demo is not available on the server: it posts every night once.':!isIsoDate(s.date)?'The business date is not loaded yet. Refresh and try again.':undefined,
  async send(c){const r=await c.api.command<{postedNow:number;stays:number}>('PostRoomNights',{businessDate:c.state.date});return {result:r};}});

/** PERM id the server's OpenShift requires (BillingPermissions.ShiftCount). */
const SHIFT_PERM='PERM-SHF-001';
const closeMapper=(type:string):CommandMapper=>({type,reload:['date','closedDates','closings','bookings','clock'],
  precheck:(cmd,s)=>!isIsoDate(s.date)?'The business date is not loaded yet. Refresh and try again.':undefined,
  async send(c){const r=await c.api.command<{status:string}>('RunBusinessDayClose',{businessDate:c.state.date,runId:'close-'+c.state.date});
    // The prototype's next day opens the cashier shift with the counted cash (DEC-035); the server's close does not, so once the run is
    // Completed (next day open) open the shared desk till's shift (money area learns ids 'till','desk'); the server carries the float.
    const till=c.ids.server('till','desk');
    if(r?.status==='Completed'&&till!==undefined&&c.me.permIds.includes(SHIFT_PERM)){
      await c.api.command('OpenShift',{tillId:till,openingFloatPaise:null},{step:'shift'});
      return {result:r,reload:CASH_SLICES};}
    return {result:r};}});

export const closing:SyncArea={
  id:'closing',
  loaders:[
    {slice:'date',kind:'patch',async load(c){return (await bizDate(c)).businessDate;}},
    {slice:'closings',needs:['date'],requires:'reports',async load(c){
      const to=c.current('date');const r=await c.api.get<SrvRecords>('/closing/records',{from:addDays(to,-(HISTORY_DAYS-1)),to});
      const out:Record<string,ReturnType<typeof closingFromServer>>={};
      for(const x of r.items)out[x.record.businessDate]=closingFromServer(x.record);
      return out;}},
    {slice:'closedDates',needs:['closings'],async load(c){
      const set=new Set(Object.keys(c.current('closings')??{}));
      const b=await bizDate(c);if(b.closed)set.add(b.businessDate);
      return [...set].sort();}},
  ],
  commands:[nightsMapper('CLOSE_RUN'),nightsMapper('NIGHT_POST'),closeMapper('DAY_CLOSE'),closeMapper('NEXT_DAY')],
  unmapped:{
    CORRECTION_ADJUST:'Server corrections (RequestAdjustment, action correction) reverse one whole posted charge line by id; the prototype posts a free-form signed amount for a closed date. Needs a charge picker (UI) and folio/charge ids from the money area (W4-3).',
    CORRECTION_ADJUST_DECIDE:'DecideApproval needs the approval id + version from the approvals slice, which the money area (W4-3) owns; map it there together with SHIFT_DIFFERENCE_DECIDE.',
    POST_NIGHTS:'M1 compatibility command with no UI button; CLOSE_RUN covers it.',
  },
};
