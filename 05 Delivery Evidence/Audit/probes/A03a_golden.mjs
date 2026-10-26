// A03a independent recomputation of HCC golden figures (audit probe, 26 Oct 2026).
// Own arithmetic only (BigInt, half away from zero). Fixture VALUES are parsed from the prototype source text
// (03 Prototype/src/fixtures/base.ts, ext/pos.ts, ext/groups.ts) and from the server golden JSON; no HCC money/GST function is called here.
// HCC values for the comparison columns are the literals asserted by the server tests (quoted with file:line in the report).
// Run from the repo root:  $env:PATH="E:\Tools\nodejs;$env:PATH"; node "05 Delivery Evidence/Audit/probes/A03a_golden.mjs"
import fs from 'node:fs';
import path from 'node:path';

const ROOT = process.cwd();
const rd = p => fs.readFileSync(path.join(ROOT, p), 'utf8');
const out = [];
const log = (...a) => { const s = a.join(' '); out.push(s); console.log(s); };
const must = (m, what) => { if (!m) throw new Error('fixture parse failed: ' + what); return m; };

// ── own arithmetic ──
const B = x => BigInt(x);
/** amount × bps / 10000, half away from zero, in BigInt (independent of HCC). */
function tax(amount, bps) {
  const p = B(amount) * B(bps), a = p < 0n ? -p : p;
  let q = a / 10000n; if ((a % 10000n) * 2n >= 10000n) q++;
  return Number(p < 0n ? -q : q);
}
/** CGST gets the odd paisa (magnitude), SGST = |tax|/2 toward zero. */
function split(t) { const s = Math.trunc(t / 2); return { cgst: t - s, sgst: s }; }
const rs = p => (p < 0 ? '-' : '') + '₹' + (Math.abs(p) / 100).toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
const results = [];
function cmp(figure, mine, hccLabel, hcc, note = '') {
  const ok = mine === hcc;
  results.push({ figure, mine, hccLabel, hcc, verdict: ok ? 'Match' : 'Mismatch', note });
  log(`${ok ? 'MATCH   ' : 'MISMATCH'} ${figure}: mine=${mine} (${rs(mine)}) · ${hccLabel}=${hcc}${note ? ' · ' + note : ''}`);
}

// ── GST rules as the brief states them (DEC-069 seed), applied by me ──
const THRESH = 750000;
const roomBps = (date, price) => price <= THRESH ? (date >= '2025-09-22' ? 500 : 1200) : 1800;
const FOOD_STD = 500, FOOD_SPEC = 1800, SVC = 1800;

// ── 1. BASE fixture values ──
const base = rd('03 Prototype/src/fixtures/base.ts');
const types = JSON.parse(must(base.match(/const roomTypes=(\[[^\]]+\])/), 'roomTypes')[1].replace(/'/g, '"'));
const rateM = must(base.match(/rate:roomTypes\[i%8\]==='Suite'\?(\d+):roomTypes\[i%8\]==='Deluxe'\?(\d+):(\d+)/), 'room rate rule');
const rate = t => t === 'Suite' ? +rateM[1] : t === 'Deluxe' ? +rateM[2] : +rateM[3];
const rooms = Array.from({ length: 24 }, (_, i) => ({ id: String((Math.floor(i / 8) + 1) * 100 + (i % 8) + 1), type: types[i % 8] }));
const data = JSON.parse(must(base.match(/const data=(\[\[.*?\]\]);/), 'booking data')[1].replace(/'/g, '"'));
must(base.includes("amount:status==='In house'?rate:100000,method:i%3===0?'Cash':'UPI'"), 'receipt rule');
const bookings = data.map(([room, guest, status], i) => {
  const r = rate(rooms.find(x => x.id === room).type);
  return { id: `HCC-${1041 + i}`, room, status, rate: r, pay: status === 'In house' ? r : 100000, method: i % 3 === 0 ? 'Cash' : 'UPI' };
});
const opening = +must(base.match(/openingCash:(\d+)/), 'openingCash')[1];
const exp = [...base.matchAll(/\{id:'(EXP-\d+)'[^}]*amount:(\d+),method:'(\w+)'[^}]*status:'(\w+)'/g)].map(m => ({ id: m[1], amount: +m[2], method: m[3], status: m[4] }));
must(exp.length === 2, 'expenses');
const collections = bookings.reduce((a, b) => a + b.pay, 0);
const cashPay = bookings.filter(b => b.method === 'Cash').reduce((a, b) => a + b.pay, 0);
// specified premises: previous FY (2025-26) highest unit tariff = highest rack rate in the fixture (no dated tariffs in BASE)
const maxTariff = Math.max(...rooms.map(r => rate(r.type)));
const specified = maxTariff > THRESH;
const foodBps = specified ? FOOD_SPEC : FOOD_STD;
// POS-F01 (cash) and POS-F02 (UPI) lines from ext/pos.ts
const pos = rd('03 Prototype/src/fixtures/ext/pos.ts');
const menu = Object.fromEntries([...pos.matchAll(/item\('(m-\w+)','[^']*','[^']*',(\d+)/g)].map(m => [m[1], +m[2] * 100]));
const linesOf = name => [...must(pos.match(new RegExp(`const ${name}=\\[(.*?)\\];`)), name)[1].matchAll(/line\('[^']+','(m-\w+)',(\d+)/g)].map(m => ({ price: menu[m[1]], qty: +m[2] }));
const net = ls => ls.reduce((a, l) => a + l.price * l.qty, 0);
const f01 = net(linesOf('L1')), f02 = net(linesOf('L2'));
const f01Total = f01 + tax(f01, foodBps), f02Total = f02 + tax(f02, foodBps);
const postedCashExp = exp.filter(e => e.method === 'Cash' && e.status === 'Posted').reduce((a, e) => a + e.amount, 0);
const drawer = opening + cashPay + f01Total - postedCashExp;
log(`BASE: ${bookings.length} bookings, opening ${rs(opening)}, cash payments ${rs(cashPay)}, POS-F01 net ${rs(f01)} + GST ${foodBps}bps = ${rs(f01Total)}, posted cash expense ${rs(postedCashExp)}; max FY25-26 tariff ${rs(maxTariff)} → specified=${specified}`);
const golden = JSON.parse(rd('08 Production/tests/Hcc.Platform.Tests/Golden/base-fixture.golden.json'));
cmp('BASE collections', collections, 'golden JSON expected.collectionsPaise', golden.base.expected.collectionsPaise);
cmp('BASE collections', collections, 'server GoldenFigureTests.cs:13 CollectionsPaise', 2150000);
cmp('BASE drawer', drawer, 'golden JSON expected.drawerPaise', golden.base.expected.drawerPaise, 'golden JSON is pre-DEC-069 (restaurant 10 %)');
cmp('BASE drawer', drawer, 'server GstSqlTests.cs:254', 1072500);
cmp('BASE drawer', drawer, 'server GoldenBillingTests.cs:47 / GoldenFigureTests.cs:12', 1075000, 'stale 10 % figure');
cmp('BASE outlet cash (POS-F01)', f01Total, 'golden JSON expected.outletCashPaise', golden.base.expected.outletCashPaise, 'golden JSON is pre-DEC-069');
cmp('POS-F02 UPI bill', f02Total, 'prototype stream-a2-till.mjs:83', 71400);
// golden JSON raw receipts agree with the fixture?
const gj = golden.base.folioReceipts.map(r => `${r.bookingId}|${r.method}|${r.amountPaise}`).join(',');
const mineR = bookings.map(b => `${b.id}|${b.method}|${b.pay}`).join(',');
log(`golden JSON folio receipts equal the fixture's: ${gj === mineR}`);

// ── 2. Room 105 journey (₹2,000 Classic, 24→26 Oct 2026; laundry ₹500; ₹500 discount following the room-night rate) ──
const r105 = rate(rooms.find(r => r.id === '105').type);
const jl = [
  { k: 'room', d: '2026-10-24', a: r105, bps: roomBps('2026-10-24', r105) },
  { k: 'room', d: '2026-10-25', a: r105, bps: roomBps('2026-10-25', r105) },
  { k: 'service', d: '2026-10-24', a: 50000, bps: SVC },
];
jl.push({ k: 'discount', d: '2026-10-24', a: -50000, bps: jl[0].bps });
let jn = 0, jt = 0, jc = 0, js = 0;
for (const l of jl) { const t = tax(l.a, l.bps), s = split(t); l.t = t; l.c = s.cgst; l.s = s.sgst; jn += l.a; jt += t; jc += s.cgst; js += s.sgst; }
log('Room 105 lines: ' + jl.map(l => `${l.k} ${rs(l.a)}@${l.bps} tax ${l.t} (C ${l.c} / S ${l.s})`).join(' · '));
cmp('Room 105 net', jn, 'server GstRulesGoldenTests.cs:187', 400000);
cmp('Room 105 GST', jt, 'server GstRulesGoldenTests.cs:187 / GstSqlTests.cs:49', 26500);
cmp('Room 105 total', jn + jt, 'server InvoiceSqlTests.cs:27 (INV/2627/0000001)', 426500);
cmp('Room 105 CGST', jc, 'server InvoiceSqlTests.cs:37', 13250);
cmp('Room 105 SGST', js, 'server InvoiceSqlTests.cs:37', 13250);
cmp('Room 105 total', jn + jt, 'golden JSON journey.expected.totalPaise', golden.journey.expected.totalPaise, 'golden JSON lines carry taxBps 1000');
cmp('Room 105 total', jn + jt, 'server GoldenBillingTests.cs:156 / GoldenFigureTests.cs:14', 440000, 'stale 10 % figure (FixedGstRateResolver(1000))');
cmp('Room 105 balance after ₹1,000 advance (deposit separate)', jn + jt - 100000, 'prototype run.mjs:24 / m1.mjs:53', 326500);

// ── 3. Groups (ext/groups.ts + BASE HCC-1044/1048) ──
const grp = rd('03 Prototype/src/fixtures/ext/groups.ts');
const co = id => { const m = must(grp.match(new RegExp(`\\{id:'${id}'.*?creditLimit:(\\d+),openingOutstanding:(\\d+)`, 's')), id); return { limit: +m[1], opening: +m[2] }; };
const blockSrc = id => must(grp.match(new RegExp(`\\{id:'${id}'.*?createdAt`, 's')), id)[0];
const objNum = (src, key) => Object.fromEntries([...must(src.match(new RegExp(`${key}:\\{([^}]*)\\}`)), key)[1].matchAll(/(\w+):(\d+)/g)].map(m => [m[1], +m[2]]));
const g1 = blockSrc('GB-1'), g2 = blockSrc('GB-2');
const g1allot = objNum(g1, 'allot'), g1rel = objNum(g1, 'released'), g1rates = objNum(g1, 'rates');
const g1members = [...g1.matchAll(/booking:'(HCC-\d+)'/g)].map(m => m[1]);
const contracted = g1allot.Classic + g1rel.Classic, picked = g1members.length;
log(`GB-1: allot ${JSON.stringify(g1allot)} released ${JSON.stringify(g1rel)} members ${g1members} → contracted ${contracted}, picked ${picked}`);
cmp('GB-1 allotted (incl. released)', contracted, 'server GroupRulesGoldenTests.cs:34', 4);
cmp('GB-1 picked %', Math.round(picked * 100 / contracted), 'server GroupRulesGoldenTests.cs:35', 50);
// member exposure: 3 nights (23–26 Oct) × (rate + GST per night) − paid; 23 Oct posted at the fixture's rate
const exposure = id => { const b = bookings.find(x => x.id === id); const nights = ['2026-10-23', '2026-10-24', '2026-10-25'];
  const owed = nights.reduce((a, d) => a + b.rate + tax(b.rate, roomBps(d, b.rate)), 0) - b.pay; return Math.max(0, owed); };
cmp('HCC-1044 exposure', exposure('HCC-1044'), 'server GroupRulesGoldenTests.cs:66', 430000);
const g1est = g1members.reduce((a, id) => a + exposure(id), 0);
cmp('GB-1 master estimate', g1est, 'server GroupRulesGoldenTests.cs:79', 860000);
cmp('GB-1 members paid', g1members.reduce((a, id) => a + bookings.find(x => x.id === id).pay, 0), 'server GroupRulesGoldenTests.cs:78', 400000);
const co1 = co('CO-1'), co2 = co('CO-2');
cmp('CO-1 exposure (opening + GB-1 stays)', co1.opening + g1est, 'server GroupRulesGoldenTests.cs:80 (6200000 constant + estimates)', 7060000);
const g2rates = objNum(g2, 'rates'), g2dates = [must(g2.match(/arrival:'([\d-]+)'/), 'arr')[1], must(g2.match(/departure:'([\d-]+)'/), 'dep')[1]];
const stay = (r, a, d) => { let v = 0; for (let x = new Date(a + 'T12:00Z'); x < new Date(d + 'T12:00Z'); x = new Date(+x + 864e5)) { const day = x.toISOString().slice(0, 10); v += r + tax(r, roomBps(day, r)); } return v; };
const sc = stay(g2rates.Classic, ...g2dates), sd = stay(g2rates.Deluxe, ...g2dates);
cmp('GB-2 Classic stay value', sc, 'server GroupRulesGoldenTests.cs:88', 378000);
cmp('GB-2 Deluxe stay value', sd, 'server GroupRulesGoldenTests.cs:88', 567000);
cmp('CO-2 exposure (opening + 1 Classic + 1 Deluxe)', co2.opening + sc + sd, 'server GroupRulesGoldenTests.cs:89', 9745000);
log(`CO-2 limit ${rs(co2.limit)} vs exposure ${rs(co2.opening + sc + sd)} → ${co2.opening + sc + sd > co2.limit ? 'over' : 'within'} limit`);
// server-only attrition rule on GB-1: 50 %, no allowance, 3 nights
const per = tax(g1rates.Classic, 5000), unpicked = contracted - picked;
let attNet = 0, attTax = 0;
for (const d of ['2026-10-23', '2026-10-24', '2026-10-25']) { const amt = per * unpicked; attNet += amt; attTax += tax(amt, roomBps(d, per)); }
cmp('GB-1 attrition net (server rule, 50 %)', attNet, 'server GroupRulesGoldenTests.cs:166', 525000);
cmp('GB-1 attrition GST (slab on ₹875)', attTax, 'server GroupRulesGoldenTests.cs:184 (26250)', 26250);
log(`  note: had the slab been tested on the contracted ₹${g1rates.Classic / 100} night the rate would still be 5 % here; for a ₹9,000 block rate the code would tax a 50 % retention (₹4,500) at 5 % instead of 18 % (G7).`);

// ── 4. Mixed folio (constructed): ≤ ₹7,500 night, > ₹7,500 night, restaurant, laundry, room-rate discount ──
const mixed = [
  { k: 'room', label: 'Room night 2026-10-24 (₹7,500.00, at threshold)', a: 750000, bps: roomBps('2026-10-24', 750000) },
  { k: 'room', label: 'Room night 2026-10-25 (₹7,500.01, above)', a: 750001, bps: roomBps('2026-10-25', 750001) },
  { k: 'service', label: 'Restaurant dinner ₹1,234.50', a: 123450, bps: foodBps },
  { k: 'service', label: 'Room service tea ₹10.10', a: 1010, bps: foodBps },
  { k: 'service', label: 'Laundry ₹333.33', a: 33333, bps: SVC },
  { k: 'discount', label: 'Discount ₹123.45 on 25 Oct (follows 25 Oct room-night rate)', a: -12345, bps: roomBps('2026-10-25', 750001) },
];
let mN = 0, mT = 0, mC = 0, mS = 0;
for (const l of mixed) { l.t = tax(l.a, l.bps); Object.assign(l, split(l.t)); mN += l.a; mT += l.t; mC += l.cgst; mS += l.sgst; log(`  mixed: ${l.label} @${l.bps}bps → tax ${l.t} = CGST ${l.cgst} + SGST ${l.sgst}`); }
log(`Mixed folio: net ${mN} tax ${mT} (CGST ${mC} / SGST ${mS}) total ${mN + mT}`);
const mixedOut = { lines: mixed.map(l => ({ a: l.a, bps: l.bps, tax: l.t, cgst: l.cgst, sgst: l.sgst })), net: mN, tax: mT, cgst: mC, sgst: mS, total: mN + mT };

// ── 5. Refund and credit notes (InvoiceSqlTests / CreditNoteSqlTests worlds) ──
// (a) room-only stay 2 × ₹2,000 @5 %: invoice ₹4,200; credit note ₹100 on line 1 → ₹105; folio −₹105; refund ₹105 → 0.
const inv2 = 2 * (200000 + tax(200000, 500));
cmp('Room-only invoice (2 nights)', inv2, 'server CreditNoteSqlTests.cs:21', 420000);
const cn1 = 10000 + tax(10000, 500);
cmp('Credit note ₹100 + GST', cn1, 'server CreditNoteSqlTests.cs:34', 10500);
cmp('Folio balance after the credit note', -cn1, 'server CreditNoteSqlTests.cs:44', -10500);
cmp('Folio balance after refund of the credit', -cn1 + cn1, 'server CreditNoteSqlTests.cs:57', 0);
// (b) amount-mode ₹100 spread over AC006 positive lines by largest remainder (ties → earlier line)
const pool = [200000, 200000, 50000], pb = [500, 500, 1800], W = pool.reduce((a, b) => a + b, 0), A = 10000;
const sh = pool.map((w, i) => ({ i, f: Math.floor(A * w / W), r: (A * w) % W }));
let left = A - sh.reduce((a, s) => a + s.f, 0);
[...sh].sort((x, y) => y.r - x.r || x.i - y.i).slice(0, left).forEach(s => sh[s.i].f++);
const cnA = sh.reduce((a, s) => a + s.f + tax(s.f, pb[s.i]), 0);
log(`Amount-mode spread: ${sh.map(s => s.f).join(' / ')}`);
cmp('Amount-mode credit note ₹100 on AC006', cnA, 'server InvoiceRulesTests.cs:124 / CreditNoteSqlTests.cs:85', 10644);
cmp('Full credit note after it', 426500 - cnA, 'server CreditNoteSqlTests.cs:92', 415856);
// (c) the full-after-partial refusal (A03a finding): partial credit whose tax is an exact half paisa
const lineTaxable = 200000, lineTax = tax(lineTaxable, 500), part = 10010;
const partTax = tax(part, 500), restTax = tax(lineTaxable - part, 500);
log(`Full-after-partial: line ₹2,000 @5 % tax ${lineTax}; partial ₹100.10 → tax ${partTax}; remaining ${lineTaxable - part} → tax ${restTax}; cumulative ${partTax + restTax} > ${lineTax} → CreditNoteRules.Plan refuses (CreditNoteRules.cs:78-80)`);

// ── 6. Invoice numbering and FY ──
const fy = d => { const y = +d.slice(0, 4), m = +d.slice(5, 7), s = m >= 4 ? y : y - 1; return String(s % 100).padStart(2, '0') + String((s + 1) % 100).padStart(2, '0'); };
const num = (p, d, n) => { const f = fy(d), w = 16 - p.length - f.length - 2; return `${p}/${f}/${String(n).padStart(w, '0')}`; };
const n1 = num('INV', '2026-10-24', 1);
log(`numbers: ${n1} (${n1.length}) · ${num('CN', '2026-10-24', 1)} · FY 2027-03-31 → ${fy('2027-03-31')} · 2027-04-01 → ${fy('2027-04-01')}`);
results.push({ figure: 'Invoice no. format', mine: n1, hccLabel: 'InvoiceSqlTests.cs:27', hcc: 'INV/2627/0000001', verdict: n1 === 'INV/2627/0000001' ? 'Match' : 'Mismatch' });
results.push({ figure: 'Credit note no. format', mine: num('CN', '2026-10-24', 1), hccLabel: 'CreditNoteSqlTests.cs:34', hcc: 'CN/2627/00000001', verdict: num('CN', '2026-10-24', 1) === 'CN/2627/00000001' ? 'Match' : 'Mismatch' });

// ── 7. GSTIN checksum (own implementation of the GSTN mod-36 rule) ──
const AL = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ';
const chk = s => { let sum = 0; for (let i = 0; i < 14; i++) { const p = AL.indexOf(s[i]) * (i % 2 ? 2 : 1); sum += Math.floor(p / 36) + p % 36; } return AL[(36 - sum % 36) % 36]; };
const gstins = { '27AAPFU0939F1ZV': 'test valid', '29AAGCB7383J1Z4': 'test valid', '27AAAAA0000A1Z2': 'test hotel (valid)', '27AAAAA0000A1Z5': 'prototype hotel placeholder',
  ...Object.fromEntries([...grp.matchAll(/id:'(CO-\d)'[^}]*?gstin:'(\w+)'/g)].map(m => [m[2], 'fixture company ' + m[1]])), '27ZZFIC0009F1Z5': 'GroupText.Gstin example message' };
const gstinOut = {};
for (const [g, what] of Object.entries(gstins)) { const ok = chk(g.slice(0, 14)) === g[14]; gstinOut[g] = ok; log(`GSTIN ${g} (${what}): check char expected ${chk(g.slice(0, 14))} → ${ok ? 'valid' : 'INVALID'}`); }

fs.mkdirSync(path.join(ROOT, '05 Delivery Evidence/Audit/evidence/A03a'), { recursive: true });
fs.writeFileSync(path.join(ROOT, '05 Delivery Evidence/Audit/evidence/A03a/golden_recompute.json'), JSON.stringify({ node: process.version, results, mixed: mixedOut, gstin: gstinOut }, null, 1));
fs.writeFileSync(path.join(ROOT, '05 Delivery Evidence/Audit/evidence/A03a/golden_recompute.txt'), `node ${process.version}\n` + out.join('\n') + '\n');
log(`\n${results.filter(r => r.verdict === 'Match').length} match, ${results.filter(r => r.verdict !== 'Match').length} mismatch`);
