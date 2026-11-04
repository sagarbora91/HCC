// Sync registry (W4-0): every area module. Each W4 area agent has its OWN pre-allocated slot below — uncomment exactly your
// two lines (import + AREAS entry) and touch nothing else, so the eleven branches merge without conflicts (README.md).
// Order matters only for `kind:'patch'` loaders on the same slice (earlier entry patches first). validateAreas()
// (runtime.ts) refuses two owners of a slice and two mappers of one command type; tests/sync-foundation.mjs runs it here.
import type {SyncArea} from './types';
import {core} from './core';
import {reservations} from './reservations';

// W4-1 front desk (stays, guests, customers; extend reservations.ts too):
import {frontdesk} from './frontdesk';

// W4-2 rooms, housekeeping, room blocks:
import {rooms} from './rooms';

// W4-3 money (folio, cashier, cash book, invoices, groups billing):
import {money} from './money';

// W4-4 closing, reports, exports, overview:
import {closing} from './closing';

// W4-5 admin (users, roles, devices, configuration, setup):
import {admin} from './admin';

// W4-6 staff, payroll, leave:
import {staff} from './staff';

// W4-7 accounting, expenses, compliance:
import {accounting} from './accounting';
// ACC-1 lanes: add ONE import line for your sync file below (append only).
import {accountingChart} from './accounting-chart'; // ACC-1 chart lane
import {accountingOpening} from './accounting-opening';
// F5-A1
import {accountingYear} from './accounting-year'; // ACC-4 year end + bank reconciliation
// F5-A1 end
// ACC-1 end
// ACC-2 lanes: ONE line below (append only).
import {expensesVendor} from './expenses-vendor'; // acc2-purchase
import {accountingManual} from './accounting-manual'; // ACC-2 acc2-manual lane
import {accountingTds} from './accounting-tds'; // ACC-2 tds lane
import {accountingGst} from './accounting-gst'; // ACC-2 gst lane
import {accountingFlows} from './accounting-flows'; // acc2-flows
// ACC-2 end
// LF lanes: ONE import line for your sync file inside your own block (append only).
// LF-DOCDB
// LF-DOCDB end
// LF-RATE
import {lfmoney} from './lfmoney';
// LF-RATE end
// LF-BILL
// LF-BILL end
// LF-COUNTER
// LF-COUNTER end
// LF-DESK
// LF-DESK end
// LF-ALERTS
import {reminders} from './reminders';
// LF-ALERTS end
// LF-IMAGES
import {guestphoto} from './guestphoto';
// LF-IMAGES end

// W4-8 POS:
import {pos} from './pos';

// W4-9 maintenance, linen & stock:
import {maintenance} from './maintenance';

// W4-10 guest services, messaging, shift handover:
import {services} from './services';

// W4-11 yield, OTA channels, booking website, ID capture:
import {channels} from './channels';

// W4 fix F3 cashier (SHIFT_OPEN: the first-day open-shift control):
import {cashier} from './cashier';

// W4 N3: display names of all staff for every role (patch on users):
import {directory} from './directory';

// MA-0 masters framework (floors; MA-1 … MA-7 add routes in masters.ts MST_SERVER):
import {masters} from './masters';

// Wave C3 System health (A09-05: parked outbox events, retry, last backup):
import {syshealth} from './syshealth';
// MA-1 rooms masters (room types / amenities / occupancy charges routes live in roommasters.ts RMS_SERVER; rooms tools here):
import {roommasters} from './roommasters';
// MA-3 money masters (patch loader on mastersX; commands through masters.ts MST_SERVER):
import {moneyMasters} from './money-master';
// MA-2 meal plans, extras catalogue, discount reasons (after money: patches the folio lines it loaded):
import {catalogue} from './catalogue';

// F6-H3
import {hall} from './hall'; // F6 hall booking: enquiries, halls master, accept, advances, event bill, cancel, refund, credit note
// F6-H3 end
// AF-SYNC-OPS
import {keys} from './keys'; // OPS-01 room keys
import {tariffcard} from './tariffcard'; // OPS-02 tariff card
import {reportsHub} from './reports-hub'; // OPS-03 report favourites / schedules / print log
// AF-SYNC-OPS end
// AF-NETSET
import {egress} from './egress';
// AF-NETSET end
export const AREAS:SyncArea[]=[
  core,
  reservations,

  frontdesk, // W4-1

  rooms, // W4-2

  money, // W4-3

  closing, // W4-4

  admin, // W4-5

  staff, // W4-6

  accounting, // W4-7
  // ACC-1 lanes: add your sync module name on ONE line below (append only).
  accountingChart, // ACC-1 chart lane
  accountingOpening, // ACC-1 opening balances
  // F5-A1
  accountingYear, // ACC-4
  // F5-A1 end
  // ACC-1 end
// ACC-2 lanes: ONE line below (append only).
  expensesVendor, // acc2-purchase (vendor payments, returns)
  accountingManual, // ACC-2 acc2-manual lane
  accountingTds, // ACC-2 tds lane
  accountingGst, // ACC-2 gst lane
  accountingFlows, // acc2-flows
// ACC-2 end

  pos, // W4-8

  maintenance, // W4-9

  services, // W4-10

  channels, // W4-11

  cashier, // W4 fix F3

  directory, // W4 N3

  masters, // MA-0

  syshealth, // Wave C3
  roommasters, // MA-1
  moneyMasters, // MA-3
  catalogue, // MA-2
  // LF lanes: your AREAS entry inside your own block.
  // LF-DOCDB
  // LF-DOCDB end
  // LF-RATE
  lfmoney,
  // LF-RATE end
  // LF-BILL
  // LF-BILL end
  // LF-COUNTER
  // LF-COUNTER end
  // LF-DESK
  // LF-DESK end
  // LF-ALERTS
  reminders,
  // LF-ALERTS end
  // LF-IMAGES
  guestphoto,
  // LF-IMAGES end
  // F6-H3
  hall,
  // F6-H3 end
  // AF-SYNC-OPS
  keys,
  tariffcard,
  reportsHub,
  // AF-SYNC-OPS end
  // AF-NETSET
  egress,
  // AF-NETSET end
];
