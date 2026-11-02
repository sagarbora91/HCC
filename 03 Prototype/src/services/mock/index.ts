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
