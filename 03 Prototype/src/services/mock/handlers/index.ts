import type {Handler} from '../commands';
import {handlers as reservations} from './reservations';
import {handlers as guests} from './guests';
import {handlers as folio} from './folio';
import {handlers as housekeeping} from './housekeeping';
import {handlers as admin} from './admin';
import {handlers as closing} from './closing';
import {handlers as expenses} from './expenses'; // DEC-120: expenses with the SCC features
import {handlers as yieldMod} from './yield';
import {handlers as groups} from './groups';
import {handlers as channels} from './channels';
import {handlers as maintenance} from './maintenance';
import {handlers as inventory} from './inventory';
import {handlers as handover} from './handover';
import {handlers as services} from './services';
import {handlers as messaging} from './messaging';
import {handlers as pos} from './pos';
import {handlers as staff} from './staff';
import {handlers as accounting} from './accounting';
// ACC-1 lanes: add ONE import line for your handler file below this comment and spread it in the ACC1 object (append only).
import {handlers as accountingChart} from './accounting-chart'; // ACC-1 chart lane
import {handlers as accountingOpening} from './accounting-opening';
// ACC-1 end
// ACC-2 lanes: add ONE import line for your handler file below this comment and spread it in the ACC2 object (append only).
import {handlers as expensesVendor} from './expenses-vendor'; // acc2-purchase (contract §3.2, §3.5)
import {handlers as accountingManual} from './accounting-manual'; // ACC-2 acc2-manual lane
import {handlers as accountingTds} from './accounting-tds'; // ACC-2 tds lane
import {handlers as accountingGst} from './accounting-gst'; // ACC-2 gst lane
import {handlers as accountingFlows} from './accounting-flows'; // ACC-2 flows lane (DEPOSIT_APPLY, EXP_UD_PAY_REVERSE)
// ACC-2 end
// ACC-3 lanes: ONE import line below (append only).
import {handlers as accountingAssets} from './accounting-assets'; // ACC-3 acc3-carry (C1)
// ACC-3 end
import {handlers as assistant} from './assistant';
import {handlers as compliance} from './compliance';
import {handlers as cashbook} from './cashbook';
import {handlers as cashout} from './cashout';
import {handlers as masters} from './masters';
import {handlers as credentials} from './credentials'; // A02b-05 self-service password / PIN change
import {handlers as syshealth} from './syshealth';
import {handlers as feedbackQw} from './feedback'; // F4-T3
import {handlers as roommasters} from './roommasters';
import {handlers as keys} from './keys'; // KEYS room keys
import {handlers as roles} from './roles'; // ADM-2 custom roles (wraps USER_CREATE / USER_UPDATE: after admin)
// LF lanes: ONE import line inside your own block (append only); spread it in the LF object below.
// LF-DOCDB
// LF-DOCDB end
// LF-RATE
import {handlers as stayRate} from './stay-rate';
// LF-RATE end
// LF-BILL
import {handlers as billCorrection} from './bill-correction';
// LF-BILL end
// LF-COUNTER
import {handlers as counter} from './counter';
// LF-COUNTER end
// LF-DESK
import {handlers as desk} from './desk';
// LF-DESK end
// LF-ALERTS
import {handlers as reminders} from './reminders';
// LF-ALERTS end
// LF-IMAGES
import {handlers as guestPhotos} from './guestphotos'; // B5 party photos
// LF-IMAGES end
// F7-R1
// RPT_* commands (./reports) are added to `registry` by services/mock/index.ts F7-R1; the integrator may spread them into registry0 instead.
// F7-R1 end
// F5-E1
import {handlers as egress} from './egress'; // F5-E1 CFG_NET_SET, NET_TICK
// F5-E1 end
// F5-A1
import {handlers as accountingYear} from './accounting-year'; // ACC-4 year lock (contract §1)
import {handlers as accountingBank} from './accounting-bank'; // ACC-4 bank reconciliation (contract §2)
import {accYearLockedOn,accYearLockedMessage,accFyOfDate} from '../../../domain/streams/accounting-year';
// F5-A1 end
// F6-H1
import {handlers as hall} from './hall'; // F6 hall booking: masters, enquiries, accept, advances, bill, cancel, refund
// F6-H1 end
// F6-H2
import {handlers as hallQuote} from './hall-quote'; // F6 quotations + T&C: QUO_* (not QUO_ACCEPT), HALL_TNC_TEMPLATE_SAVE
// F6-H2 end
// F6-H3
// F6-H3 end
// F12-SETTINGS
import {handlers as measSettings} from './meas-settings'; // MEAS_PACK_SET, MEAS_ITEM_SET (MEAS-CONTRACT §1.4)
// F12-SETTINGS end
// F12-CAP-DESK
import {handlers as measDesk} from './meas-desk'; // F12 desk captures: turn-away, walked, allowance reasons, booking extras, preferences
// F12-CAP-DESK end
// F12-CAP-HK
import {handlers as measHk} from './meas-hk'; // F12 meas-cap-hk: offline reasons, meters, readings, asset life, discards, task-time recording (wraps TASK)
// F12-CAP-HK end
// F12-CAP-GUEST
import {handlers as measGuest} from './meas-guest'; // F12 MEAS guest captures
// F12-CAP-GUEST end
// F12-CAP-PEOPLE
import {handlers as measPeople} from './meas-people';
// F12-CAP-PEOPLE end
// F12-FORECAST
import {handlers as measForecast} from './meas-forecast'; // F12 meas-forecast: MEAS_EVENT_*, MEAS_OTB_SNAPSHOT, MEAS_COMPETITOR_SAVE, MEAS_RATESHOP_ADD
// F12-FORECAST end
// F8 lanes (SF-CONTRACT / F8_Briefs): one import line inside your own block, then spread it inside the same block of `const F8` below.
// F8-SF-DOMAIN
// F8-SF-DOMAIN end
// F8-QW-NA
// F8-QW-NA end
// F8-QW-NS
// F8-QW-NS end
// F8-QW-KDS
// F8-QW-KDS end
// F8-QW-ADV
// F8-QW-ADV end
// F8-QW-PM
// F8-QW-PM end
// F4-T1
import {handlers as tariffCard} from './tariffcard';
// F4-T1 end
import {handlers as digest} from './digest'; // F4-T2
import {handlers as catalogue} from './catalogue'; // MA-2 (last: wraps folio APPROVAL)
const ACC1:Record<string,Handler>={...accountingChart,...accountingOpening,}; // ACC-1 lanes: spread your handlers here, e.g. {...accountingChart}
const ACC2:Record<string,Handler>={...expensesVendor,...accountingManual,...accountingTds,...accountingGst,...accountingFlows,...accountingAssets,}; // ACC-2 lanes: spread your handlers here, e.g. {...accountingManual}
const LF:Record<string,Handler>={ // LF lanes: spread your handlers inside your own block, e.g. ...stayRate,
  // LF-DOCDB
  // LF-DOCDB end
  // LF-RATE
  ...stayRate,
  // LF-RATE end
  // LF-BILL
  ...billCorrection,
  // LF-BILL end
  // LF-COUNTER
  ...counter,
  // LF-COUNTER end
  // LF-DESK
  ...desk,
  // LF-DESK end
  // LF-ALERTS
  ...reminders,
  // LF-ALERTS end
  // LF-IMAGES
  ...guestPhotos,
  // LF-IMAGES end
};
const F4:Record<string,Handler>={ // F4 lanes: spread your handlers inside your own block
  // F4-T1
  ...tariffCard,
  // F4-T1 end
  // F4-T2
  ...digest,
  // F4-T2 end
  // F4-T3
  ...feedbackQw,
  // F4-T3 end
};
const F6:Record<string,Handler>={ // F6 lanes: spread your handlers inside your own block
  // F6-H1
  ...hall,
  // F6-H1 end
  // F6-H2
  ...hallQuote,
  // F6-H2 end
  // F6-H3
  // F6-H3 end
};
const F12:Record<string,Handler>={ // F12 lanes (MEAS-CONTRACT §6): spread your handlers inside your own block
  // F12-SETTINGS
  ...measSettings,
  // F12-SETTINGS end
  // F12-CAP-DESK
  ...measDesk,
  // F12-CAP-DESK end
  // F12-CAP-HK
  ...measHk,
  // F12-CAP-HK end
  // F12-CAP-GUEST
  ...measGuest,
  // F12-CAP-GUEST end
  // F12-CAP-PEOPLE
  ...measPeople,
  // F12-CAP-PEOPLE end
  // F12-FORECAST
  ...measForecast,
  // F12-FORECAST end
};
const F8:Record<string,Handler>={ // F8 lanes (F8_Briefs): spread your NEW commands inside your own block. Changes to existing commands
  // (PAYMENT, CHARGE, CHECKOUT, BOOK, CANCEL, DAY_CLOSE …) are made inside an // F8-<LANE> block in the command's own handler file instead.
  // F8-SF-DOMAIN
  // F8-SF-DOMAIN end
  // F8-QW-NA
  // F8-QW-NA end
  // F8-QW-NS
  // F8-QW-NS end
  // F8-QW-KDS
  // F8-QW-KDS end
  // F8-QW-ADV
  // F8-QW-ADV end
  // F8-QW-PM
  // F8-QW-PM end
};
const registry0:Record<string,Handler>={...ACC1,...ACC2,...reservations,...guests,...folio,...housekeeping,...admin,...closing,...expenses,...yieldMod,...groups,...channels,...maintenance,...inventory,...handover,...services,...messaging,...pos,...staff,...accounting,...assistant,...compliance,...cashbook,...cashout,...masters,...credentials,...syshealth,...roommasters,...keys,...roles,...LF,...F4,...egress /* F5-E1 */,...catalogue,...accountingYear,...accountingBank /* F5-A1 */,...F6,...F12,...F8};
// F5-A1 — ACC-4 §1.2: after the year lock every post / edit / void dated in the year is refused ACC_YEAR_LOCKED (day reopen and month unlock
// included); the year / bank commands and exports check for themselves. A correction is a journal in the new year: ACC_MANUAL_SAVE takes
// `refersToFy` (G4), kept on the saved voucher.
const YEAR_FREE=/^(ACC_YEAR_|BANK_|ACC_TALLY_EXPORT$)/;
const yearGuard=(type:string,h:Handler):Handler=>k=>{
  if(!YEAR_FREE.test(type)&&k.s.accYearX){const c=k.cmd;
    for(const d of [c.date,typeof c.month==='string'?c.month+'-01':undefined])if(typeof d==='string'&&accYearLockedOn(k.s,d))k.fail('INVALID_TRANSITION',accYearLockedMessage(accFyOfDate(d.slice(0,10))));}
  const out=h(k);
  if(type==='ACC_MANUAL_SAVE'&&typeof k.cmd.refersToFy==='string'&&/^\d{4}-\d{2}$/.test(k.cmd.refersToFy)&&k.s.accountingX?.manual?.length){
    const list=[...k.s.accountingX.manual],j=k.cmd.id?list.findIndex(v=>v.id===String(k.cmd.id)):-1,i=j>=0?j:list.length-1;list[i]={...list[i],refersToFy:k.cmd.refersToFy} as typeof list[number];k.s.accountingX={...k.s.accountingX,manual:list};}
  return out;
};
export const registry:Record<string,Handler>=Object.fromEntries(Object.entries(registry0).map(([t,h])=>[t,yearGuard(t,h)]));
// F5-A1 end
