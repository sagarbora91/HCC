// A3b independent recomputation (audit probe, read-only). Run from the repo root with Node 25:
//   $env:PATH="E:\Tools\nodejs;$env:PATH"; node "05 Delivery Evidence/Audit/probes/A3b_recompute.mjs"
// Own arithmetic only: no HCC function is imported. Inputs are transcribed from
//   03 Prototype/src/fixtures/base.ts (bookings, rates, receipts, expenses, openingCash)
//   03 Prototype/src/fixtures/ext/pos.ts (POS-F01 cash, POS-F02 UPI; restaurant 5 %)
//   08 Production/deployment/import-templates/samples/{ledgers,opening-balances}.csv
// and compared with the golden JSON files and server test expectations, which are READ (never executed).
import {readFileSync} from 'node:fs';

const root = process.cwd();
const J = p => JSON.parse(readFileSync(root + '/' + p, 'utf8'));
const out = [];
const log = (...a) => { const s = a.join(' '); out.push(s); console.log(s); };
const rs = p => (p < 0 ? '-' : '') + '₹' + (Math.abs(p) / 100).toFixed(2);
const verdict = (label, mine, theirs) => log(`${mine === theirs ? 'MATCH   ' : 'MISMATCH'} ${label}: mine ${typeof mine === 'number' ? rs(mine) + ' (' + mine + ' p)' : mine} vs ${typeof theirs === 'number' ? rs(theirs) + ' (' + theirs + ' p)' : theirs}`);

// ── GST helpers (own implementation of the stated rules) ──
// line tax = amount × bps / 10000, half away from zero, in paise
const lineTax = (amt, bps) => { const x = amt * bps; const q = Math.trunc(x / 10000), r = x - q * 10000; return Math.abs(r) * 2 >= 10000 ? q + Math.sign(x) : q; };
// intra-state split: SGST = half toward zero, CGST = rest (takes the odd paisa)
const split = t => { const s = Math.trunc(t / 2); return {cgst: t - s, sgst: s}; };
// DEC-069 room rate: ≤ ₹7,500 per unit per night → 5 %, else 18 %
const roomBps = ratePaise => ratePaise <= 750000 ? 500 : 1800;

// ── 1. BASE fixture, transcribed ──
const roomType = i => ['Classic', 'Classic', 'Deluxe', 'Deluxe', 'Classic', 'Suite', 'Deluxe', 'Classic'][i % 8];
const rate = id => { const n = Number(id), i = (Math.floor(n / 100) - 1) * 8 + (n % 100) - 1; const t = roomType(i); return t === 'Suite' ? 450000 : t === 'Deluxe' ? 300000 : 200000; };
const data = [['101', 'In house'], ['102', 'In house'], ['104', 'In house'], ['201', 'In house'], ['202', 'In house'], ['206', 'In house'], ['301', 'In house'], ['302', 'In house'], ['103', 'Confirmed'], ['207', 'Confirmed']];
const bookings = data.map(([room, status], i) => ({id: 'HCC-' + (1041 + i), room, status, rate: rate(room),
  receipt: {amount: status === 'In house' ? rate(room) : 100000, method: i % 3 === 0 ? 'Cash' : 'UPI', kind: 'payment'}}));
const openingCash = 500000;
const pos = [{id: 'POS-F01', method: 'Cash', net: 2 * 15000 + 2 * 6000 + 8000}, {id: 'POS-F02', method: 'UPI', net: 2 * 22000 + 16000 + 8000}]
  .map(b => { const tax = lineTax(b.net, 500); return {...b, tax, total: b.net + tax}; });
const expenses = [{id: 'EXP-101', method: 'Cash', amount: 180000, status: 'Posted', cat: 'Laundry'}, {id: 'EXP-102', method: 'UPI', amount: 85000, status: 'Pending', cat: 'Supplies'}];

log('== 1. Closing summary, BASE 24 Oct 2026 (own arithmetic) ==');
const byTender = {};
for (const b of bookings) byTender[b.receipt.method] = (byTender[b.receipt.method] ?? 0) + b.receipt.amount;
const collections = Object.values(byTender).reduce((a, x) => a + x, 0);
log('Folio receipts by tender:', Object.entries(byTender).map(([k, v]) => `${k} ${rs(v)}`).join(' · '), '· total', rs(collections));
log('Refunds', rs(0), '· deposits in/out', rs(0), '/', rs(0), '· company credit', rs(0));
for (const p of pos) log(`Outlet ${p.id} ${p.method}: net ${rs(p.net)} + GST 5 % ${rs(p.tax)} = ${rs(p.total)} (CGST ${rs(split(p.tax).cgst)}, SGST ${rs(split(p.tax).sgst)})`);
const cashExp = expenses.filter(e => e.method === 'Cash' && e.status === 'Posted').reduce((a, e) => a + e.amount, 0);
log('Expenses posted cash', rs(cashExp), '· pending (excluded, not drawer)', rs(85000));
const outletCash = pos.filter(p => p.method === 'Cash').reduce((a, p) => a + p.total, 0);
const drawer = openingCash + (byTender.Cash ?? 0) + outletCash - cashExp;
log(`Drawer expected = opening ${rs(openingCash)} + folio cash ${rs(byTender.Cash)} + outlet cash ${rs(outletCash)} − cash expenses ${rs(cashExp)} = ${rs(drawer)}`);
log('Counted (BASE has no count) = expected → difference', rs(0));
const g = J('08 Production/tests/Hcc.Platform.Tests/Golden/base-fixture.golden.json');
verdict('collections vs base-fixture.golden.json', collections, g.base.expected.collectionsPaise);
verdict('outlet cash vs base-fixture.golden.json', outletCash, g.base.expected.outletCashPaise);
verdict('drawer vs base-fixture.golden.json (GoldenFigureTests.BASE_expected_drawer_is_10750_rupees, GoldenBillingTests)', drawer, g.base.expected.drawerPaise);
verdict('drawer vs Hcc.Closing.Tests DailyPackTests.Base_drawer_10725… (1072500)', drawer, 1072500);
verdict('drawer vs HANDOFF Session 11b "BASE drawer ₹10,725"', drawer, 1072500);

// ── 2. Vouchers + trial balance (23 + 24 Oct) ──
log('\n== 2. Vouchers and trial balance (own rules: Dr guest a+t / Cr revenue / Cr 2110 / Cr 2120; receipts Dr tender / Cr 1100) ==');
const tender = {Cash: '1000', UPI: '1010', Card: '1020', 'Bank transfer': '1030'};
const v23 = [], v24 = [];
for (const b of bookings.filter(b => b.status === 'In house')) {
  const t = lineTax(b.rate, roomBps(b.rate)), s = split(t);
  v23.push([['1100', b.rate + t, 0], ['4000', 0, b.rate], ['2110', 0, s.cgst], ['2120', 0, s.sgst]]);
}
for (const b of bookings) v24.push([[tender[b.receipt.method], b.receipt.amount, 0], ['1100', 0, b.receipt.amount]]);
for (const p of pos) { const s = split(p.tax); v24.push([[tender[p.method], p.total, 0], ['4100', 0, p.net], ['2110', 0, s.cgst], ['2120', 0, s.sgst]]); }
v24.push([['5010', 180000, 0], ['1000', 0, 180000]]);
const tb = vs => { const m = {}; for (const v of vs) for (const [a, d, c] of v) { m[a] ??= [0, 0]; m[a][0] += d; m[a][1] += c; } return m; };
const tot = m => Object.values(m).reduce((a, [d, c]) => [a[0] + d, a[1] + c], [0, 0]);
const ag = J('08 Production/tests/Hcc.Accounting.Tests/Golden/accounting-golden.json');
for (const [name, vs] of [['base-23oct', v23], ['base-24oct', v24]]) {
  const m = tb(vs), [d, c] = tot(m);
  log(`${name}: ${vs.length} vouchers · Dr ${rs(d)} = Cr ${rs(c)} → ${d === c ? 'balanced' : 'UNBALANCED'}`);
  const gold = Object.fromEntries(ag.days.find(x => x.name === name).trialBalance.map(r => [r.account, [r.debitPaise, r.creditPaise]]));
  for (const a of Object.keys({...m, ...gold}).sort()) verdict(`  ${name} TB ${a}`, JSON.stringify(m[a] ?? null), JSON.stringify(gold[a] ?? null));
}
const all = [...v23, ...v24], [td, tc] = tot(tb(all));
verdict('Tally range 23–24 Oct debit total vs accounting-golden tally.debitPaise', td, ag.tally.debitPaise);
verdict('Tally range voucher count vs golden', String(all.length), String(ag.tally.count));

// ── 3. Cumulative TB with the sample opening trial balance ──
log('\n== 3. Cumulative TB incl. opening balances (samples/opening-balances.csv; SBI01 → 1030) ==');
const opening = [['1000', 2500000, 0], ['1030', 35000000, 0], ['FURN01', 120000000, 0], ['1110', 4500000, 0], ['2200', 0, 6200000], ['LOAN01', 0, 50000000], ['CAP01', 0, 105800000]];
const [od, oc] = opening.reduce((a, [, d, c]) => [a[0] + d, a[1] + c], [0, 0]);
log(`Opening: Dr ${rs(od)} · Cr ${rs(oc)} → ${od === oc ? 'balanced' : 'UNBALANCED'}`);
verdict('opening total vs Hcc.Import.Tests OpeningTrialBalanceTests (162000000)', od, 162000000);
const cum = tb(all);
for (const [a, d, c] of opening) { cum[a] ??= [0, 0]; cum[a][0] += d; cum[a][1] += c; }
const [cd, cc] = tot(cum);
log(`Cumulative to 24 Oct (opening as of 22 Oct + 23–24 Oct vouchers): Dr ${rs(cd)} · Cr ${rs(cc)} → ${cd === cc ? 'balanced' : 'UNBALANCED'}`);
for (const a of Object.keys(cum).sort()) log(`  ${a.padEnd(7)} Dr ${rs(cum[a][0]).padStart(16)}  Cr ${rs(cum[a][1]).padStart(16)}  bal ${rs(cum[a][0] - cum[a][1])}`);
log('  note: 1000 Cash in hand = opening ₹25,000 + 24 Oct ₹5,725 net; the drawer in BASE opened with ₹5,000 (fixture) — HCC never compares the TB cash with the till opening cash.');

// ── 4. Room 105 journey under DEC-069: lines → 2110/2120; one receipt, one refund, one credit note ──
log('\n== 4. Room 105 (DEC-069) — GST ledgers 2110/2120, receipt and refund vouchers ==');
const lines = [['room', 200000, 500], ['room', 200000, 500], ['service · Laundry', 50000, 1800], ['discount', -50000, 500]];
let net = 0, tax = 0, cg = 0, sg = 0;
for (const [k, a, bps] of lines) { const t = lineTax(a, bps), s = split(t); net += a; tax += t; cg += s.cgst; sg += s.sgst;
  log(`  ${k.padEnd(18)} ${rs(a).padStart(10)} @${bps / 100}% tax ${rs(t)} → Dr 1100 ${rs(a + t)} · Cr ${k === 'discount' ? '4900' : k === 'room' ? '4000' : '4100'} ${rs(a)} · Cr 2110 ${rs(s.cgst)} · Cr 2120 ${rs(s.sgst)}`); }
log(`  folio net ${rs(net)} + GST ${rs(tax)} = ${rs(net + tax)} · 2110 ${rs(cg)} · 2120 ${rs(sg)}`);
verdict('room 105 total vs HANDOFF Session 11b (₹4,265)', net + tax, 426500);
verdict('room 105 total vs base-fixture.golden.json journey.expected (GoldenFigureTests Room_105_journey_invoice_is_4400)', net + tax, g.journey.expected.totalPaise);
log('  Receipt (RCT): cash payment ₹1,000 → Dr 1000 ₹1,000.00 · Cr 1100 ₹1,000.00 (2110/2120 untouched: GST is booked on the charge, never on the receipt)');
log('  Refund (PMT): cash refund ₹500 of that receipt → Dr 1100 ₹500.00 · Cr 1000 ₹500.00 (2110/2120 untouched)');
const cnTax = lineTax(-50000, 1800), cn = split(cnTax);
log(`  Credit note on laundry ₹500 @18%: correction line −₹500, tax ${rs(cnTax)} → Dr 4910 ₹500.00 · Dr 2110 ${rs(-cn.cgst)} · Dr 2120 ${rs(-cn.sgst)} · Cr 1100 ${rs(50000 - cnTax)}`);
for (const t of [2501, -2501, 1, -1001]) log(`  split(${t}) = CGST ${split(t).cgst} · SGST ${split(t).sgst}`);
log('  server AccountingRulesTests.Gst_split…: (-1001 → -501/-500), (1 → 1/0) — compare above');

// ── 5. One Tally voucher (own serialisation) vs the golden prototype XML ──
log('\n== 5. Tally XML voucher RCT/20261024/001 (debit = ISDEEMEDPOSITIVE Yes, negative; credit = No, positive) ==');
const r = n => (n / 100).toFixed(2);
const mine = [
  '    <DATE>20261024</DATE><VOUCHERTYPENAME>Receipt</VOUCHERTYPENAME><VOUCHERNUMBER>RCT/20261024/001</VOUCHERNUMBER><NARRATION>Payment received · Cash · DEMO-100 · Ananya Rao</NARRATION><EFFECTIVEDATE>20261024</EFFECTIVEDATE>',
  `    <ALLLEDGERENTRIES.LIST><LEDGERNAME>Cash in hand</LEDGERNAME><ISDEEMEDPOSITIVE>Yes</ISDEEMEDPOSITIVE><AMOUNT>-${r(200000)}</AMOUNT></ALLLEDGERENTRIES.LIST>`,
  `    <ALLLEDGERENTRIES.LIST><LEDGERNAME>Guest ledger</LEDGERNAME><ISDEEMEDPOSITIVE>No</ISDEEMEDPOSITIVE><AMOUNT>${r(200000)}</AMOUNT></ALLLEDGERENTRIES.LIST>`];
const gx = ag.tally.xml.split('\n'), i = gx.findIndex(l => l.includes('<VOUCHERNUMBER>RCT/20261024/001<'));
for (let k = 0; k < 3; k++) verdict(`  xml line ${k + 1}`, mine[k], gx[i + k]);
const amounts = [...ag.tally.xml.matchAll(/<ISDEEMEDPOSITIVE>(Yes|No)<\/ISDEEMEDPOSITIVE><AMOUNT>(-?[\d.]+)<\/AMOUNT>/g)];
const bad = amounts.filter(([, y, a]) => (y === 'Yes') !== a.startsWith('-'));
const sum = amounts.reduce((s, [, , a]) => s + Math.round(Number(a) * 100), 0);
log(`  whole golden XML: ${amounts.length} ledger entries, ${bad.length} with sign ≠ ISDEEMEDPOSITIVE, signed sum ${sum} paise (must be 0 for every voucher set)`);
log(`  XML contains GUID/REMOTEID? ${/REMOTEID|<GUID>/.test(ag.tally.xml)} · ledger masters? ${/<LEDGER /.test(ag.tally.xml)} · ACTION="Create" count ${(ag.tally.xml.match(/ACTION="Create"/g) ?? []).length}`);

// ── 6. Gaps quantified (illustrative, own figures) ──
log('\n== 6. Ledger effect of events Accounting does not consume (illustrative) ==');
// Salary advance ₹5,000 given (AdvanceGiven: not consumed), ₹1,000 recovered in the next pay run (PayrollPosted: Cr 1200 booked)
log('  Staff advance ₹5,000 given (not booked) then ₹1,000 recovered in payroll (booked Cr 1200): 1200 Staff advances balance', rs(0 - 100000), '(asset with a credit balance); bank/cash outflow of ₹5,000 missing');
// Group: member transfer ₹10,500 to master (booked Dr 1110 / Cr 1100), master pays ₹10,500 by bank (GroupReceiptRecorded: not booked),
// attrition 2 room-nights × ₹1,800 @5 % (GroupAttritionPosted: not booked)
const att = 2 * 180000, attTax = lineTax(att, 500);
log(`  Group: transfer ₹10,500 → 1110 Dr ₹10,500 stays open forever (bank receipt not booked); attrition ${rs(att)} + GST ${rs(attTax)} (2110 ${rs(split(attTax).cgst)} / 2120 ${rs(split(attTax).sgst)}) never reaches revenue or output GST`);
// Banking: no server command produces a 'banking' or transfer movement
log('  Cash banked ₹5,000 at day end: no command records it; the count shows a ₹5,000 shortage → accepted as "cash short" → Dr 5900 Cash over/short ₹5,000 (should be Dr 1030 Bank)');

writeOut();
function writeOut() { const p = root + '/05 Delivery Evidence/Audit/evidence/A03b/recompute.txt'; import('node:fs').then(fs => fs.writeFileSync(p, `node ${process.version}\n` + out.join('\n') + '\n')); }
