// A03a rounding sweep, prototype side: compares 03 Prototype/src/domain/gst.ts gstLineTax / gstSplit / gstCategoryOfLabel with an
// independent BigInt reference (half away from zero; CGST takes the odd paisa). gst.ts has only `import type`, so Node 25 loads it
// directly with type stripping. Run from the repo root (Node 25):
//   node "05 Delivery Evidence/Audit/probes/A03a_rounding_sweep.mjs"
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const ROOT = process.cwd();
const G = await import(pathToFileURL(path.join(ROOT, '03 Prototype/src/domain/gst.ts')).href);
const ref = (amount, bps) => { const p = BigInt(amount) * BigInt(bps), a = p < 0n ? -p : p; let q = a / 10000n; if ((a % 10000n) * 2n >= 10000n) q++; return Number(p < 0n ? -q : q); };
const BPS = [0, 1, 250, 500, 1200, 1800, 2800, 4999, 5000, 9999, 10000];
let n = 0, bad = [], splitBad = [];
const check = (a, b) => {
  n++;
  const mine = ref(a, b), hcc = G.gstLineTax(a, b);
  if (mine !== hcc && bad.length < 20) bad.push({ a, b, mine, hcc });
  const s = G.gstSplit(hcc), ms = Math.trunc(mine / 2);
  if ((s.sgst !== ms || s.cgst !== mine - ms || s.cgst + s.sgst !== hcc) && splitBad.length < 20) splitBad.push({ a, b, s, mine });
};
for (const b of BPS) for (let a = -200000; a <= 200000; a++) check(a, b);
// large values up to ₹10 crore (10^10 paise) incl. edges; Number stays exact while amount × bps < 2^53
let seed = 12345; const rnd = () => (seed = (seed * 1103515245 + 12345) % 2147483648) / 2147483648;
for (let i = 0; i < 200000; i++) { const a = Math.floor((rnd() * 2 - 1) * 1e10), b = BPS[i % BPS.length]; check(a, b); }
for (const a of [1e10, -1e10, 1e10 - 1, 9007199254, 900719925474]) for (const b of BPS) check(a, b);
// float-safety bound of the prototype: amount × bps must stay below 2^53 for exact integer arithmetic
const maxSafeAmountAt2800 = Math.floor(Number.MAX_SAFE_INTEGER / 2800);
// category guesses (G6/G8): label words the regex treats differently than a person would
const labels = ['Express breakfast', 'Express laundry', 'Espresso', 'Press conference hall', 'Airport express transfer', 'Breakfast', 'Hall rent', 'Minibar water bottle', 'Extra bed', 'No-show charge', 'Cancellation fee'];
const cats = Object.fromEntries(labels.map(l => [l, G.gstCategoryOfLabel(l)]));
const lines = [`node ${process.version}`, `checked ${n} (amount, bps) pairs; line-tax mismatches: ${bad.length}; split mismatches: ${splitBad.length}`,
  `prototype exact-integer bound at 28 %: |amount| ≤ ${maxSafeAmountAt2800} paise (₹${(maxSafeAmountAt2800 / 100).toLocaleString('en-IN')}) — above the ₹10 crore field cap, so safe`,
  'gstCategoryOfLabel: ' + JSON.stringify(cats), bad.length ? 'first mismatches: ' + JSON.stringify(bad) : '', splitBad.length ? 'split mismatches: ' + JSON.stringify(splitBad) : ''];
fs.mkdirSync(path.join(ROOT, '05 Delivery Evidence/Audit/evidence/A03a'), { recursive: true });
fs.writeFileSync(path.join(ROOT, '05 Delivery Evidence/Audit/evidence/A03a/rounding_sweep_prototype.txt'), lines.join('\n') + '\n');
console.log(lines.join('\n'));
