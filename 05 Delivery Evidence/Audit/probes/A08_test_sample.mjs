// A08 test-quality sample: prints the bodies of 40 chosen tests (10 projects x 4) to evidence/A08/test_sample.txt.
// Run from repo root with Node 25: node "05 Delivery Evidence/Audit/probes/A08_test_sample.mjs"
import fs from 'node:fs';
import path from 'node:path';
const T = path.join(process.cwd(), '08 Production', 'tests');
const pick = {
  'Hcc.Billing.Tests': [['Domain/BillingDomainTests.cs', 13], ['Domain/BillingDomainTests.cs', 120], ['Sql/BillingCommandTests.cs', 26], ['Sql/BillingCommandTests.cs', 277]],
  'Hcc.Invoices.Tests': [['Domain/InvoiceRulesTests.cs', 29], ['Domain/InvoiceRulesTests.cs', 73], ['Domain/InvoiceRulesTests.cs', 161], ['Sql/InvoiceSqlTests.cs', 160]],
  'Hcc.Payroll.Tests': [['Domain/PayrollRulesTests.cs', 59], ['Domain/PayrollRulesTests.cs', 109], ['Domain/PayrollRulesTests.cs', 204], ['Sql/PayrollSqlTests.cs', 91]],
  'Hcc.Closing.Tests': [['Sql/CloseJourneyTests.cs', 166], ['Sql/ConcurrencySnapshotCashBookTests.cs', 81], ['Sql/ResumeAndBarrierTests.cs', 114], ['Sql/CashBookViewTests.cs', 64]],
  'Hcc.Accounting.Tests': [['Domain/AccountingRulesTests.cs', 73], ['Domain/AccountingRulesTests.cs', 105], ['Golden/GoldenAccountingTests.cs', 135], ['Sql/AccountingFlowTests.cs', 27]],
  'Hcc.Groups.Tests': [['Kernel/GroupRulesGoldenTests.cs', 63], ['Kernel/GroupRulesGoldenTests.cs', 122], ['Kernel/GroupRulesGoldenTests.cs', 160], ['Sql/GroupsSqlTests.cs', 55]],
  'Hcc.GstIds.Tests': [['Gst/GstRulesGoldenTests.cs', 20], ['Gst/GstRulesGoldenTests.cs', 120], ['Gst/GstRulesGoldenTests.cs', 181], ['Gst/GstSqlTests.cs', 159]],
  'Hcc.Pos.Tests': [['Domain/PosDomainTests.cs', 12], ['Domain/PosDomainTests.cs', 62], ['Sql/PosCommandTests.cs', 95], ['Sql/PosCommandTests.cs', 217]],
  'Hcc.Yield.Tests': [['Domain/YieldRulesGoldenTests.cs', 52], ['Domain/YieldRulesGoldenTests.cs', 88], ['Domain/YieldRulesGoldenTests.cs', 183], ['Sql/YieldSqlTests.cs', 105]],
  'Hcc.Stock.Tests': [['Kernel/StockRulesTests.cs', 16], ['Kernel/StockRulesTests.cs', 89], ['Sql/StockSqlTests.cs', 73], ['Sql/StockSqlTests.cs', 185]],
};
const out = [];
let n = 0;
for (const [proj, list] of Object.entries(pick)) {
  for (const [file, line] of list) {
    const lines = fs.readFileSync(path.join(T, proj, file), 'utf8').split('\n');
    let end = line + 1;
    while (end < lines.length && end < line + 60 && !/^\s*\[(Fact|Theory)/.test(lines[end])) end++;
    out.push(`\n##### ${++n}. ${proj}/${file}:${line}`);
    out.push(lines.slice(line - 1, end).join('\n'));
  }
}
const dst = path.join(process.cwd(), '05 Delivery Evidence', 'Audit', 'evidence', 'A08', 'test_sample.txt');
fs.writeFileSync(dst, out.join('\n'));
console.log(`${n} tests written to ${dst}`);
