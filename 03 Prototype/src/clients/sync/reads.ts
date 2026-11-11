// Read gates (D5, W4 E2E): which server GETs the signed-in user may send, so a loader never fires a read the role does not hold
// (no 403 noise, no "N areas could not load"). Each rule repeats the server's check (Hcc.Application queries + AccessEvaluator):
//   • a VIEW permission (or an action's ":VIEW") is held in any mode (A/L/R/V/M); an action permission only in A/L/R;
//   • a check without a record ("wide") needs a property-scoped grant (P); "any" also accepts a scoped grant (S own shift,
//     A assigned, O own submissions) because the server filters the rows to what the user may see.
// tests/sync-roles.mjs checks every rule against the real server's answers per seeded role (tests/_role-reads.json, written by
// Hcc.Integration.Tests RoleReadMatrixTests) and boots each role against a fake server that answers 403 where the real one does.
// No grant is changed here: a read a role does not hold is simply not sent. Role matrix v2 (DEC-079, D1–D7): each module reads with its
// own ids (PERM-<MOD>-001 view, -004 money), the same check the server's module query makes.
import type {SyncMe} from './types';

type Me=Pick<SyncMe,'grants'>;
type Hold={perm:string;view?:boolean;scope?:'wide'|'any'};

/** The server's allow test for one permission (see the header). */
export function holdsGrant(me:Me|null|undefined,h:Hold):boolean{
  if(!me)return false;
  return me.grants.some(g=>g.permissionId===h.perm&&(h.view||'ALR'.includes(String(g.mode||'A').toUpperCase()))&&
    ((h.scope??'wide')==='any'||String(g.scope||'P').toUpperCase()==='P'));
}
const wide=(perm:string,view=false):Hold=>({perm,view,scope:'wide'});
const anyScope=(perm:string,view=false):Hold=>({perm,view,scope:'any'});
const one=(...hs:Hold[])=>(me:Me)=>hs.some(h=>holdsGrant(me,h));
const always=()=>true;

// Server permission per read (module *Permissions classes): RES-001 reservation.view, GST-001 guest.view, HK-001 board, FOL-001 folio.view,
// INV-001 invoice view, SHF-001 shift, RPT-002 finance reports (the Cashier's own-shift cell reaches only the daily collections report),
// ADM-* admin, ACC-001 accounting, CMP-001 compliance, GRP-001/004 groups; role matrix v2 (DEC-079): POS-001 view / 004 day sales / 005 settle,
// MNT-001 view / 004 money / 005 AMC, STK-001 view / 004 money, GSV-001 view / 003 approve, HND-001 view / 003 manager, MSG-001 view /
// 003 templates, OTA-001, WEB-001, YLD-001, CFG-001 view / 002 non-money / 003 money.
const RES=one(wide('PERM-RES-001',true));
const BOARD=one(anyScope('PERM-HK-001',true));
const FINANCE=one(wide('PERM-RPT-002',true));
const STOCK=one(wide('PERM-STK-004',true),anyScope('PERM-STK-001',true));
const POS=one(wide('PERM-POS-001',true));
const POS_LISTS=one(wide('PERM-POS-001',true),wide('PERM-MST-001')); // MA-4: outlets / menu / categories also for masters.manage (Tech admin); role matrix v2 POS read
const MNT=one(anyScope('PERM-MNT-001',true));
const GSV=one(wide('PERM-GSV-001',true));
const GSV_OWN=one(anyScope('PERM-GSV-001',true));
const HND=one(anyScope('PERM-HND-001',true));
const MSG=one(wide('PERM-MSG-001',true));
const MSG_TEMPLATES=one(anyScope('PERM-MSG-003'),wide('PERM-MSG-001',true));
const ACCOUNTING=one(wide('PERM-ACC-001',true));
const COMPLIANCE=one(wide('PERM-CMP-001'));
const ADMIN=one(wide('PERM-ADM-001'));
const CONFIG=one(wide('PERM-CFG-001',true));
const OTA=one(wide('PERM-OTA-001',true));
const WEB=one(wide('PERM-WEB-001',true));
const YIELD=one(wide('PERM-YLD-001',true));

/** Route pattern (as in tests/_role-reads.json) → may the user send it. */
export const READS={
  'GET /me/capabilities':always,
  'GET /me/directory':always,
  'GET /meas/captures':always, // F12 f12-int-cap: any signed-in user; the server masks money / HR rows per caller (MeasCaptureService.Mask)
  'GET /closing/business-date':always,
  'GET /closing/records':FINANCE,
  // DEC-079: the daily collections report — the Cashier's finance-report cell (V·S) counts here (any scope); every other finance report is property-wide.
  'GET /cashier/collections':one(anyScope('PERM-RPT-002',true)),
  'GET /reports/daily':FINANCE,
  'GET /reservations':RES,
  'GET /reservations/:id':RES,
  'GET /guests':one(wide('PERM-GST-001',true)),
  'GET /availability':RES,
  'GET /rooms/board':BOARD,
  'GET /rooms/status':RES,
  'GET /housekeeping/tasks':BOARD,
  'GET /folios/by-reservation/:id':one(wide('PERM-FOL-001',true)),
  'GET /folios/by-reservation/:id/summary':one(wide('PERM-FOL-001',true)), // F8C-SYNC: split folio summary (SplitFolioEndpoints, FolioView)
  'GET /folios/:id':one(wide('PERM-FOL-001',true)), // F8C-SYNC: folios B–D of a split stay (BillingQueries.GetFolioAsync, FolioView)
  'GET /invoices/by-source/folio/:id':one(wide('PERM-INV-001',true)),
  'GET /credit-note-requests':one(wide('PERM-INV-001',true)),
  'GET /cashier/desk':one(anyScope('PERM-SHF-001')),
  'GET /cashier/folios':one(wide('PERM-FOL-001',true)),
  'GET /cashier/transfers':one(anyScope('PERM-SHF-001'),wide('PERM-DRW-003',true)), // Wave B1 (DEC-073): cash-shift holders + slip viewers (Accountant)
  'GET /cashbook':FINANCE,
  'GET /groups':one(wide('PERM-GRP-001',true)),
  'GET /groups/companies':one(wide('PERM-GRP-001',true)),
  'GET /groups/:id/statement':one(wide('PERM-GRP-004',true)),
  'GET /admin/users':ADMIN,
  'GET /admin/roles':ADMIN,
  'GET /admin/exceptions':ADMIN,
  'GET /admin/policy-history':one(wide('PERM-ADM-001'),wide('PERM-ADM-002',true),wide('PERM-ADM-004',true)), // ADM-3: role / policy change log (Audit tab); server: PERM-ADM-001/002/004 at any mode
  'GET /admin/audit':one(wide('PERM-ADM-004',true)),
  // Wave C3 (A09-05) System health: PERM-SYS-001 (Owner, Tech admin), an action permission held in A.
  'GET /admin/system-health':one(wide('PERM-SYS-001')),
  // F6-H3: the hall snapshot is for every staff role (the calendar and the function sheet carry no prices); the server returns enquiries / quotations / money only to the holders of PERM-HAL-001 / 002 (DEC-061).
  'GET /hall/snapshot':always,
  // F6-int: menu packages for every staff role (prices stripped without PERM-HAL-002 view); T&C templates for the holders of PERM-HAL-002 (view)
  'GET /hall/menus':always,
  'GET /hall/templates':one(anyScope('PERM-HAL-002',true)),
  // AF-SYNC-OPS (integration): room keys, tariff card, report favourites / schedules / runs (server permission checks in KeysEndpoints, TariffCardEndpoints, ReportService).
  'GET /keys/log':one(wide('PERM-KEY-001')),
  'GET /keys/encoder':one(wide('PERM-KEY-001')),
  'GET /keys/panel/:id':one(wide('PERM-KEY-001')),
  'GET /keys/locks':one(wide('PERM-KEY-004')),
  'GET /tariff-card':one(wide('PERM-TRF-001')),
  'GET /reports/favourites':always, // server answers every signed-in role (ReportService.FavouritesAsync has no permission gate)
  'GET /reports/schedules':one(wide('PERM-RPT-004')),
  'GET /reports/runs':one(wide('PERM-RPT-004')),
  // LF-ALERTS B4: PERM-RMD-001 reminders (an action permission).
  'GET /reminders':one(wide('PERM-RMD-001')),
  // LF-BILL A2: the bill-correction register = InvoicePermissions.View (PERM-INV-001), as the invoice reads.
  'GET /invoices/corrections':one(wide('PERM-INV-001',true)),
  // LF-finish: counter bills = PERM-CTR-001 (Owner, Manager, Reception, Cashier); credit guests = PERM-FOL-010 (money roles); applied rate changes = PERM-STY-010 (Owner, Manager, Reception).
  'GET /counter-bills':one(wide('PERM-CTR-001')),
  'GET /guests/credit':one(wide('PERM-FOL-010')),
  'GET /stays/rate-changes':one(wide('PERM-STY-010')),
  // PERM-CFG-001 is a VIEW permission (Owner A, Tech admin V), so /me/capabilities lists it for both; requests also answer CFG-002/003 holders.
  'GET /configuration/requests':one(wide('PERM-CFG-001',true),wide('PERM-CFG-002'),wide('PERM-CFG-003')),
  'GET /configuration/history':CONFIG,
  'GET /configuration/values':always,
  // GST master (DEC-069): TaxPermissions.ConfigurationView = PERM-CFG-001, the same check as /configuration/history.
  'GET /tax/gst':CONFIG,
  'GET /payroll/staff':always,
  'GET /payroll/board':always,
  'GET /accounting/days':ACCOUNTING,
  'GET /accounting/trial-balance':ACCOUNTING,
  'GET /accounting/mapping':ACCOUNTING,
  'GET /accounting/mapping/history':ACCOUNTING,
  'GET /accounting/locks/history':ACCOUNTING,
  // ACC-1 (integration): the chart of accounts master + history and the opening entry — PERM-ACC-001 view, as the trial balance.
  'GET /accounting/chart/master':ACCOUNTING,
  'GET /accounting/chart/history':ACCOUNTING,
  'GET /accounting/opening':ACCOUNTING,
  'GET /accounting/year':ACCOUNTING, // F5-A1
  // ACC-2 (integration): manual vouchers + loans, GST month / month locks, ledger statement and day book — PERM-ACC-001 view; the TDS state
  // (rates, deductions, remittances) — PERM-ACC-013 accounting.tax (Owner, Accountant); vendor payments, ageing and the asset register —
  // PERM-EXP-004 or the accounting view (VendorPaymentQueries).
  'GET /accounting/manual':ACCOUNTING,
  'GET /accounting/gst/month':ACCOUNTING,
  'GET /accounting/gst/locks':ACCOUNTING,
  'GET /accounting/statement':ACCOUNTING,
  'GET /accounting/day-book':ACCOUNTING,
  'GET /accounting/tds':one(wide('PERM-ACC-013')),
  'GET /expenses/vendor-payments':one(wide('PERM-EXP-004'),wide('PERM-ACC-001',true)),
  'GET /expenses/vendor-ageing':one(wide('PERM-EXP-004'),wide('PERM-ACC-001',true)),
  'GET /accounting/assets':one(wide('PERM-EXP-004'),wide('PERM-ACC-001',true)),
  'GET /expenses':one(anyScope('PERM-EXP-001'),anyScope('PERM-EXP-002'),anyScope('PERM-EXP-003')),
  'GET /expenses/registers':one(anyScope('PERM-EXP-001'),anyScope('PERM-EXP-002'),anyScope('PERM-EXP-003')), // DEC-120 SCC registers
  'GET /compliance/state':COMPLIANCE,
  'GET /compliance/ca-packs':COMPLIANCE,
  'GET /pos/outlets':POS_LISTS,
  'GET /pos/menu':POS_LISTS,
  'GET /pos/menu-categories':POS_LISTS,
  'GET /pos/checks':POS,
  // Today's settled bills add up to the day's sales: the money view (Cashier own shift) or the settling desk (Reception), not the Restaurant role.
  'GET /pos/checks/closed':one(anyScope('PERM-POS-004',true),anyScope('PERM-POS-005')),
  'GET /pos/checks/:id':POS,
  'GET /pos/void-requests':POS,
  'GET /pos/in-house':POS,
  'GET /pos/till':POS,
  'GET /maintenance/registers':MNT,
  'GET /maintenance/work-orders':MNT,
  'GET /maintenance/work-orders/:id':MNT,
  // AMC contracts: PERM-MNT-005 (Owner, Manager) or the money view PERM-MNT-004 (the Accountant's V; the action's V cell is not listed).
  'GET /maintenance/amcs':one(wide('PERM-MNT-005'),wide('PERM-MNT-004',true)),
  'GET /stock/items':STOCK,
  'GET /stock/laundry':STOCK,
  'GET /stock/counts':STOCK,
  'GET /stock/losses':STOCK,
  'GET /stock/moves':STOCK,
  'GET /stock/settings':STOCK,
  'GET /guest-services/requests':GSV_OWN,
  'GET /guest-services/complaints':GSV,
  'GET /guest-services/lost-items':GSV_OWN,
  'GET /guest-services/settings':one(wide('PERM-GSV-001',true),wide('PERM-GSV-003')),
  'GET /guest-services/stays':GSV,
  'GET /guest-services/feedback':one(wide('PERM-FBK-001',true)), // F4-T3
  'GET /guest-services/feedback/follow-ups':one(wide('PERM-FBK-002',true)),
  'GET /guest-services/histories/:kind':GSV,
  'GET /handover/checklists':HND,
  'GET /handover/templates':one(wide('PERM-HND-003')),
  'GET /handover/notes':HND,
  'GET /messaging/messages':MSG,
  'GET /messaging/templates':MSG_TEMPLATES,
  'GET /messaging/rules':MSG_TEMPLATES,
  'GET /messaging/consents':MSG,
  'GET /ota/channels':OTA,
  'GET /ota/messages':OTA,
  'GET /ota/pushes':OTA,
  // ACC-2 flows: OTA payouts are channel money (PERM-OTA-004 channels.money.view: Owner, Manager A, Accountant V).
  'GET /ota/payouts':one(wide('PERM-OTA-004',true)),
  'GET /web-booking/requests':WEB,
  'GET /web-booking/requests/:id':WEB,
  'GET /yield/calendar':YIELD,
  'GET /yield/config':YIELD,
  'GET /yield/publishes':YIELD,
  'GET /yield/proposals':YIELD,
  // MA-0 masters: floor names label every board (any signed-in user); the change history needs masters.manage (PERM-MST-001).
  'GET /masters/floors':always,
  'GET /masters/floors/history':one(wide('PERM-MST-001')),
  // MA-1 rooms masters: lists for every signed-in user (amounts empty without money visibility); history = masters.manage, money list = Owner.
  'GET /masters/room-types':always,
  'GET /masters/room-types/history':one(wide('PERM-MST-001')),
  'GET /masters/amenities':always,
  'GET /masters/amenities/history':one(wide('PERM-MST-001')),
  'GET /masters/occupancy-charges':always,
  'GET /masters/occupancy-charges/history':one(wide('PERM-MST-002')),
  'GET /masters/rooms':always,
  // MA-3 money masters: pick lists for every signed-in user (money / PAN fields null without PERM-MST-002); history = PERM-MST-002 (Owner).
  'GET /masters/payment-methods':always,'GET /masters/payment-methods/history':one(wide('PERM-MST-002')),
  'GET /masters/bank-accounts':always,'GET /masters/bank-accounts/history':one(wide('PERM-MST-002')),
  'GET /masters/expense-categories':always,'GET /masters/expense-categories/history':one(wide('PERM-MST-002')),
  'GET /masters/expense-vendors':always,'GET /masters/expense-vendors/history':one(wide('PERM-MST-002')),
  // MA-2 catalogue: prices are hotel money → folio view (PERM-FOL-001; never housekeeping / maintenance, DEC-061); history → PERM-MST-002 (Owner).
  'GET /catalogue/meal-plans':one(wide('PERM-FOL-001',true)),
  'GET /catalogue/extras':one(wide('PERM-FOL-001',true)),
  'GET /catalogue/discount-reasons':one(wide('PERM-FOL-001',true)),
  'GET /catalogue/meal-plans/history':one(wide('PERM-MST-002')),
  'GET /catalogue/extras/history':one(wide('PERM-MST-002')),
  'GET /catalogue/discount-reasons/history':one(wide('PERM-MST-002')),
  'GET /catalogue/links':one(wide('PERM-FOL-001',true)),
  // MA-5 front-desk lists: booking forms, registration, documents and policies read them (any signed-in user; commission masked by the
  // server for roles without money visibility); the history needs masters.manage (sources, ID types) or masters.manage-money (numbering, policies).
  'GET /masters/booking-sources':always,
  'GET /masters/booking-sources/history':one(wide('PERM-MST-001')),
  'GET /masters/id-types':always,
  'GET /masters/id-types/history':one(wide('PERM-MST-001')),
  'GET /masters/number-series':always,
  'GET /masters/number-series/history':one(wide('PERM-MST-002')),
  'GET /masters/policies':always,
  'GET /masters/policies/history':one(wide('PERM-MST-002')),
  // MA-6 people lists: designations, shift patterns, leave types and holidays label staff, roster and leave screens (any signed-in user); history needs PERM-MST-001.
  'GET /hr/designations':always,
  'GET /hr/designations/history':one(wide('PERM-MST-001')),
  'GET /hr/shift-patterns':always,
  'GET /hr/shift-patterns/history':one(wide('PERM-MST-001')),
  'GET /hr/leave-types':always,
  'GET /hr/leave-types/history':one(wide('PERM-MST-001')),
  'GET /hr/holidays':always,
  'GET /hr/holidays/history':one(wide('PERM-MST-001')),
  // MA-7 operations masters: lists readable by any signed-in user (housekeepers need the checklist, reception the categories; no money); histories need masters.manage.
  'GET /housekeeping/task-types':always,
  'GET /housekeeping/task-types/history':one(wide('PERM-MST-001')),
  'GET /housekeeping/zones':always,
  'GET /housekeeping/zones/history':one(wide('PERM-MST-001')),
  'GET /guest-services/complaint-categories':always,
  'GET /guest-services/complaint-categories/history':one(wide('PERM-MST-001')),
  'GET /guest-services/lost-found-categories':always,
  'GET /guest-services/lost-found-categories/history':one(wide('PERM-MST-001')),
  // FX-MERGE-UI: customer merge lists = PERM-CRM-003 (CustomerQueries.MergeAccessAsync: MergeDecide, else MergePropose; both are PERM-CRM-003, Owner and Manager)
  'GET /customers/duplicates':one(wide('PERM-CRM-003')),'GET /customers/merges':one(wide('PERM-CRM-003')),
} satisfies Record<string,(me:Me)=>boolean>;
export type ReadRoute=keyof typeof READS;

/** True when the user may send every given read (a missing user may send none). */
export function mayRead(me:Me|null|undefined,...routes:ReadRoute[]):boolean{
  return !!me&&routes.every(r=>READS[r](me));
}
