// F7-R5 test entry (bundled by tests/stream-rpt-tax.mjs for node): the mock service plus the legacy-bill sample and the GSTR-1 summary.
export * from '../src/services/mock/index';
export {LF_PARITY_LEGACY_BILLS} from '../src/fixtures/ext/lf-parity';
export {accGstSummary} from '../src/domain/streams/accounting-gst-summary';
export {toCsv} from '../src/domain/reports/engine';
export {totals} from '../src/domain/selectors';
