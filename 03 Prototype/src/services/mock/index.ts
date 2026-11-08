// Public surface of the mock service — used by the UI and by tests/run.mjs.
export * from '../../domain/index';
export {execute,changedSlices} from './commands';
export * from './faults';
export * from './persistence';
export {baseFixture,BUSINESS_DATE} from '../../fixtures/base';
export {scenarios,loadScenario,DEFAULT_SCENARIO} from '../../fixtures/scenarios';
export type {ScenarioInfo} from '../../fixtures/scenarios';
/** Back-compat alias used by earlier tests: the base fixture. */
export {baseFixture as seed} from '../../fixtures/base';
// Stream domain modules (pure helpers/selectors).
export * from '../../domain/streams/reservations';
export * from '../../domain/streams/guests';
export * from '../../domain/streams/folio';
export * from '../../domain/streams/housekeeping';
export * from '../../domain/streams/admin';
export * from '../../domain/streams/config';
export * from '../../domain/feature-catalogue'; // ADM-1
export * from '../../domain/streams/roles'; // ADM-2 custom roles
export {rolSeedDemo} from '../../fixtures/ext/roles'; // ADM-2 demo custom roles
export * from '../../domain/streams/closing';
export * from '../../domain/streams/expenses-scc'; // DEC-120 SCC expense features
export * from '../../domain/streams/udhaar-desk'; // DEC-130 Udhaar payments as desk receipts
export * from '../../domain/streams/yield';
export * from '../../domain/streams/groups';
export * from '../../domain/streams/channels';
export * from '../../domain/streams/maintenance';
export * from '../../domain/streams/inventory';
export * from '../../domain/streams/handover';
export * from '../../domain/streams/keys'; // KEYS room keys
export * from '../../domain/streams/cashbook';
export * from '../../domain/streams/cashout';
export * from '../../domain/memo';
export * from '../../domain/streams/services';
export * from '../../domain/streams/messaging';
export * from '../../domain/streams/pos';
export * from '../../domain/streams/staff';
export * from '../../domain/streams/staff-leave'; // SCC leave port (DEC-116 / DEC-119)
export * from '../../domain/streams/accounting';
// ACC-1 lanes: add ONE `export * from '../../domain/streams/accounting-<lane>'` line below (append only).
export * from '../../domain/streams/accounting-chart'; // ACC-1 chart lane
export * from '../../domain/streams/accounting-parties'; // acc1-parties (contract §3)
export * from '../../domain/streams/accounting-opening';
export * from '../../domain/streams/accounting-reports'; // acc1-statements
export * from './lf-parity-bind'; // LF-ui lf-tests: scenario LF-PARITY driver
export * from './rulebook-bind'; // acc1-tests: scenario ACC-RULEBOOK driver
// ACC-1 end
// ACC-2 lanes: add ONE `export * from '../../domain/streams/accounting-<lane>'` line below (append only).
export * from '../../domain/streams/accounting-assets'; export * from '../../domain/streams/expenses-vendor'; // acc2-purchase (contract §3)
export * from '../../domain/streams/accounting-manual'; // ACC-2 acc2-manual lane
export * from '../../domain/streams/accounting-tds'; // ACC-2 tds lane (also re-exports domain/tds)
export * from '../../domain/streams/accounting-gst'; // ACC-2 gst lane
export * from '../../domain/streams/accounting-guards'; // acc2-flows (contract §7 cash guards)
export * from '../../domain/streams/accounting-payroll'; // acc2-parity (contract §8)
// ACC-2 end
// ACC-3 lanes: add ONE `export * from '../../domain/streams/accounting-<lane>'` line below (append only).
export * from '../../domain/streams/accounting-pl'; // acc3-pl (contract §2)
export * from '../../domain/streams/accounting-ageing'; export * from '../../domain/streams/accounting-gst-summary'; // acc3-ageing (contract §3)
export * from '../../domain/streams/accounting-tally'; // ACC-3 acc3-tally (contract §4)
export * from '../../domain/streams/accounting-exports'; // ACC-3 acc3-exports (contract §5)
// ACC-3 end
export * from '../../domain/streams/assistant';
export * from '../../domain/streams/compliance';
export * from '../../domain/streams/rbac';
export * from '../../domain/gst';
export * from '../../domain/gstr1';
export {buildHistory,HISTORY_DEFAULTS,setHistoryProgress} from '../../fixtures/history/generate';
export * from '../../domain/streams/history';
export * from '../../domain/streams/masters';
export * from '../../domain/credentials';
export * from '../../domain/streams/syshealth';
export * from '../../domain/streams/audit-viewer'; // F4-T3 qw-audit
export * from '../../domain/streams/feedback'; // F4-T3 qw-feedback + qw-alerts
export * from '../../domain/streams/mastersio'; export {mstImportPreview} from './mastersio'; // MA-8 masters import / export
export * from '../../domain/streams/roommasters';
export * from '../../domain/streams/money-master'; export * from '../../domain/streams/money-master-specs'; // MA-3 money masters
export * from '../../domain/streams/catalogue'; // MA-2 meal plans, extras, discount reasons
export * from '../../domain/streams/pos-master';
export * from '../../domain/streams/frontdesk-master';
export * from '../../domain/streams/people-masters'; // MA-6
export * from '../../domain/streams/hkmasters';
export * from '../../domain/streams/gsvmasters';
export * from '../../fixtures/ext/masters-seed'; // MA-9 one masters seed
// LG
export {LEGACY_SAMPLE} from '../../fixtures/ext/legacy-sample'; // W1a LG: fictional legacy stays (stream-legacy-golden.mjs)
// LG end
// LF lanes: export lines for your domain streams inside your own block (append only).
// LF-DOCDB
export * from '../../domain/streams/docobjects'; // LF-docdb: docObjectsX mirror of the documents database (contract §1.10)
// LF-DOCDB end
// LF-RATE
export * from '../../domain/streams/stay-rate';
// LF-RATE end
// LF-BILL
export * from '../../domain/streams/bill-correction';
export {getLegacyBills} from '../../features/customers/legacy-source';
// LF-BILL end
// LF-COUNTER
export * from '../../domain/streams/counter';
export {customerKeyForCredit} from '../../domain/streams/customers';
// LF-COUNTER end
// LF-DESK
export * from '../../domain/streams/desk-fields';
// LF-DESK end
// LF-ALERTS
export * from '../../domain/streams/reminders';
export * from '../../app/shortcuts';
// LF-ALERTS end
// LF-IMAGES
export * from '../../domain/streams/printnotes'; // LF-images B6: which branding text prints on which document
// LF-IMAGES end
// F7-R1
export * from '../../domain/reports/engine'; // report engine: catalogue, params, run, renderers (F7-CONTRACT §2)
export * from '../../domain/reports/money/revenue-by-category-tender';
export {buildXlsx} from '../../app/xlsx'; // golden harness: unzip the workbook the Excel renderer builds
export {rptSlice,rptScheduleDue,rptResolveTokens,RPT_MONEY_RECIPIENT_ROLES} from './handlers/reports';
import {registry as rptRegistry} from './handlers';
import {handlers as rptHandlers} from './handlers/reports';
for(const [t,h] of Object.entries(rptHandlers))if(!(t in rptRegistry))rptRegistry[t]=h; // integration: move into registry0 (handlers/index.ts) at merge
// F7-R1 end
// F5-E1
export * from '../../domain/streams/egress'; // F5-E1 internet egress gate mirror
// F5-E1 end
// F5-A1
export * from '../../domain/streams/accounting-year'; // ACC-4 year lock / carry-forward (contract §1)
export * from '../../domain/streams/accounting-bank'; // ACC-4 bank reconciliation / clearing (contract §2)
export {ACC_BANK_LAYOUT_SEED} from '../../fixtures/ext/bank-layouts';
// F5-A1 end
// F6-H1
export * from '../../domain/streams/hall'; // F6 hall booking types + rules
export * from '../../domain/streams/hall-billing'; // F6 hall money (one truth table with Hcc.Domain/Hall/HallBilling.cs)
export {hallSeed} from '../../fixtures/ext/hall';
// F6-H1 end
// F6-H2
export * from '../../domain/streams/hall-quote'; // F6 quotations + T&C (numbering, snapshot, guard, print model, CSV)
export {hallTncSeed,HALL_TNC_HEADINGS} from '../../fixtures/ext/hall-seed';
// F6-H2 end
// F6-H3
// F6-H3 end
// F12-SETTINGS
export * from '../../domain/streams/measure'; // MEAS_ITEMS, measOn, measTarget, measGuard, registerMeasReport, MEAS_REPORT_KEY
// F12-SETTINGS end
// F12-CAP-DESK
export * from '../../domain/streams/turnaway'; // F12 desk captures rules
export {customerIndex} from '../../domain/streams/customers'; // Customer 360 index (preferences keyed by its key)
export {MEAS_ALLOWANCE_DEFAULTS,MEAS_TURNAWAY_DEMO} from '../../fixtures/ext/meas-desk';
// F12-CAP-DESK end
// F12-CAP-HK
export {measHkDemoReadings,measHkDemoState} from '../../fixtures/ext/meas-hk';
// F12-CAP-HK end
// F12-CAP-GUEST
export {measOn,measTarget} from '../../domain/streams/measure'; // F12 switches (settings lane replaces the bodies)
// F12-CAP-GUEST end
// F12-CAP-PEOPLE
export {measPeopleDemoTraining,measPeopleDemoPulse} from '../../fixtures/ext/meas-people';
// F12-CAP-PEOPLE end
// F12-FORECAST
export * from '../../domain/forecast'; // F12 meas-forecast: real forecast curve, back-test, OTB rows, displacement (fc*)
export {MEAS_FC_EVENT_KINDS,MEAS_FC_OTB_NIGHTS,MEAS_FC_OTB_KEEP} from './handlers/meas-forecast';
export {measFcEventsSeed,measFcCompetitorsSeed,measFcRateShopSeed,measFcLegacySample} from '../../fixtures/ext/meas-forecast';
// F12-FORECAST end
// F12-RPT-DAILY
export * from '../../domain/reports/meas-daily'; // pure exception functions for the digest (flashRows, rateVarianceRows, … cashierRows, regretsRows)
// F12-RPT-DAILY end
// F8-SETUP
export * from '../../domain/streams/folio-split'; // F8 SF split folio: SFFolio types, sfFolios, sfView, sfLineFolio, sfRouteTarget, sfInvoiceDocs (SF-CONTRACT §2)
// F8-SETUP end
// F8-SF-DOMAIN
export {sfFolioExposure,sfStayExposureFor} from '../../domain/streams/groups'; // F8 SF: D7 exposure per company folio (SF-CONTRACT §7)
export {sfFail,sfAfterCommand} from './handlers/folio-split';
// F8-SF-DOMAIN end
// F8-SF-TESTS
// F8-SF-TESTS end
// F8-QW-NA
export * from '../../domain/streams/night-audit'; // naChecklist, naAutoTicks, naTicksToSend, NA_TEMPLATE (night-audit lines, show only)
// F8-QW-NA end
// F8-QW-NS
export * from '../../domain/streams/noshow';
// F8-QW-NS end
// F8-QW-KDS
export * from '../../domain/streams/kds'; // F8 QW-KDS: kdsCards, thresholds
// F8-QW-KDS end
// F8-QW-ADV
// F8-QW-ADV end
// F8-QW-PM
// F8-QW-PM end
// F4-T1
export * from '../../domain/streams/tariffcard';
// F4-T1 end
// F4-T2
export * from '../../domain/streams/rpt-revcat';
export * from '../../domain/streams/digest';
// F4-T2 end
