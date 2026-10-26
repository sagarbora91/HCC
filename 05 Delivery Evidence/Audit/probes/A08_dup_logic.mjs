// A08: prototype vs server duplicated rules (GST line tax + split, yield occupancy %, yield rounding, OTA markup).
// Prototype side = the real prototype functions where they import cleanly (Node 25 type stripping), else the formula copied
// verbatim with its file:line. Server side = a line-for-line port of the C# kernel (cited). Output: evidence/A08/dup_logic.txt
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
const ROOT = process.cwd();
const out = [`A08 duplicated-logic probe ${new Date().toISOString()} node ${process.version}`];

// ── server kernel ports ──
// 08 Production/src/Hcc.Domain/Money/MoneyMath.cs:14-22
const divHalfAway = (num, den) => { const neg = num < 0n; const abs = neg ? -num : num; let q = abs / den; if ((abs % den) * 2n >= den) q++; return neg ? -q : q; };
const srvLineTax = (amount, bps) => Number(divHalfAway(BigInt(amount) * BigInt(bps), 10000n));        // Tax/GstMaster.cs:230 → MoneyMath.TaxOf
const srvOccPct = (sold, sellable) => sellable === 0 ? 0 : Math.min(100, Number(divHalfAway(BigInt(sold) * 100n, BigInt(sellable)))); // Yield/YieldEngine.cs:54
const srvYieldRound = (p, unit) => Math.max(unit, Number(divHalfAway(BigInt(p), BigInt(unit))) * unit); // Yield/YieldEngine.cs:28
const srvAdjust = (p, bps) => Number(divHalfAway(BigInt(p) * BigInt(10000 + bps), 10000n));            // Yield/YieldEngine.cs:25, Ota/OtaRules.cs:52

// ── prototype ──
let proto = {};
try {
  proto = await import(pathToFileURL(path.join(ROOT, '03 Prototype', 'src', 'domain', 'gst.ts')).href);
  out.push('prototype gst.ts imported directly (Node type stripping)');
} catch (e) {
  out.push('prototype gst.ts could not be imported (' + e.message.split('\n')[0] + '); using the copied formula from gst.ts:74-77');
  proto.gstLineTax = (amount, bps) => { const p = amount * bps, a = Math.abs(p), q = Math.floor(a / 10000), r = a - q * 10000, t = q + (r >= 5000 ? 1 : 0); return p < 0 && t ? -t : t; };
  proto.gstSplit = tax => { const sgst = Math.trunc(tax / 2) || 0; return { cgst: tax - sgst, sgst }; };
}
// 03 Prototype/src/domain/streams/yield.ts:89 and :101 and :119; streams/channels.ts:69 (copied verbatim)
const protoOccPct = (sold, sellable) => sellable ? Math.min(100, Math.round(sold / sellable * 100)) : 0;
const protoYieldRound = (paise, unit) => Math.max(unit, Math.round(paise / unit) * unit);
const protoAdjust = (rate, bps) => Math.round(rate * (10000 + bps) / 10000);

// 1. GST line tax over a wide grid incl. negatives and every slab rate
let gstChecked = 0, gstDiff = [];
for (const bps of [0, 500, 1200, 1800, 2800])
  for (let amount = -200_000; amount <= 200_000; amount += 1) {
    gstChecked++;
    const a = proto.gstLineTax(amount, bps), b = srvLineTax(amount, bps);
    if (a !== b && gstDiff.length < 10) gstDiff.push({ amount, bps, prototype: a, server: b });
  }
out.push(`\n1. GST line tax: ${gstChecked} (amount, bps) pairs, ${gstDiff.length} differences ${JSON.stringify(gstDiff)}`);
// CGST/SGST split: server Tax/GstMaster.cs:233 (CGST takes the odd paisa, as the GstIds golden test asserts)
const srvSplit = t => { const sgst = Number(BigInt(t) / 2n); return { cgst: t - sgst, sgst }; };
let splitDiff = 0; for (let t = -5000; t <= 5000; t++) { const a = proto.gstSplit(t), b = srvSplit(t); if (a.cgst !== b.cgst || a.sgst !== b.sgst) splitDiff++; }
out.push(`   CGST/SGST split over tax -5000..5000: ${splitDiff} differences (server split ported from Tax/GstMaster.cs:233-237)`);

// 2. Yield occupancy % (float divide-then-multiply vs integer half-away)
const occDiff = [];
for (let sellable = 1; sellable <= 200; sellable++) for (let sold = 0; sold <= sellable; sold++)
  if (protoOccPct(sold, sellable) !== srvOccPct(sold, sellable)) occDiff.push(`${sold}/${sellable}: prototype ${protoOccPct(sold, sellable)}% server ${srvOccPct(sold, sellable)}%`);
out.push(`\n2. Yield forecast occupancy %: ${occDiff.length} (sold, sellable≤200) pairs differ; first: ${occDiff.slice(0, 12).join('; ')}`);
const small = occDiff.filter(x => +x.split('/')[1].split(':')[0] <= 40);
out.push(`   with sellable ≤ 40 rooms: ${small.length} → ${small.join('; ')}`);

// 3. Yield rounding to the guardrail unit and rate adjustment
let rDiff = [], aDiff = [];
for (const unit of [100, 1000, 5000, 10000]) for (let p = 0; p <= 2_000_000; p += 50) if (protoYieldRound(p, unit) !== srvYieldRound(p, unit) && rDiff.length < 5) rDiff.push({ p, unit });
for (const bps of [-4500, -1500, -1200, -500, 0, 500, 1000, 1500, 2500]) for (let r = 1000; r <= 2_000_000; r += 7) if (protoAdjust(r, bps) !== srvAdjust(r, bps) && aDiff.length < 5) aDiff.push({ r, bps });
out.push(`\n3. Yield round-to-unit: differences ${JSON.stringify(rDiff)}; rate × (1 + bps) (yield plans, OTA markup): differences ${JSON.stringify(aDiff)}`);
fs.writeFileSync(path.join(ROOT, '05 Delivery Evidence', 'Audit', 'evidence', 'A08', 'dup_logic.txt'), out.join('\n'));
console.log(out.join('\n').slice(0, 4000));
