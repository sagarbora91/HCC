// F7-R7 test entry (bundled by tests/stream-rpt-docs.mjs for node): the mock service plus the document layouts.
export * from '../src/services/mock/index';
export {DOCUMENTS,docDef,docCatalogue,buildDocument,docResult,docHtml,folioDocument,creditNoteDocument,receiptDocument,quotationDocument,payslipDocument,kotDocument,outletBillDocument,rateCardDocument,handoverPackDocument} from '../src/domain/reports/documents';
export {totals} from '../src/domain/selectors';
