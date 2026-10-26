// Sync registry (W4-0): every area module. Each W4 area agent has its OWN pre-allocated slot below — uncomment exactly your
// two lines (import + AREAS entry) and touch nothing else, so the eleven branches merge without conflicts (README.md).
// Order matters only for `kind:'patch'` loaders on the same slice (earlier entry patches first). validateAreas()
// (runtime.ts) refuses two owners of a slice and two mappers of one command type; tests/sync-foundation.mjs runs it here.
import type {SyncArea} from './types';
import {core} from './core';
import {reservations} from './reservations';

// W4-1 front desk (stays, guests, customers; extend reservations.ts too):
// import {frontdesk} from './frontdesk';

// W4-2 rooms, housekeeping, room blocks:
import {rooms} from './rooms';

// W4-3 money (folio, cashier, cash book, invoices, groups billing):
// import {money} from './money';

// W4-4 closing, reports, exports, overview:
// import {closing} from './closing';

// W4-5 admin (users, roles, devices, configuration, setup):
// import {admin} from './admin';

// W4-6 staff, payroll, leave:
// import {staff} from './staff';

// W4-7 accounting, expenses, compliance:
// import {accounting} from './accounting';

// W4-8 POS:
// import {pos} from './pos';

// W4-9 maintenance, linen & stock:
// import {maintenance} from './maintenance';

// W4-10 guest services, messaging, shift handover:
// import {services} from './services';

// W4-11 yield, OTA channels, booking website, ID capture:
// import {channels} from './channels';

export const AREAS:SyncArea[]=[
  core,
  reservations,

  // frontdesk, // W4-1

  rooms, // W4-2

  // money, // W4-3

  // closing, // W4-4

  // admin, // W4-5

  // staff, // W4-6

  // accounting, // W4-7

  // pos, // W4-8

  // maintenance, // W4-9

  // services, // W4-10

  // channels, // W4-11
];
