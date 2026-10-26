// A08: semantic diff between committed golden JSON files and copies regenerated (in the scratch folder given as argv[2])
// from the CURRENT prototype by the repo's own exporters (copied, unchanged, to that folder; the repo itself is not written).
// Usage: node A08_golden_diff.mjs "<scratch>/08 Production"   → evidence/A08/golden_regen_diff.txt
import fs from 'node:fs';
import path from 'node:path';
const R = path.join(process.cwd(), '08 Production');
const S = process.argv[2];
const files = ['Accounting/Golden/accounting-golden.json', 'Customers/Golden/customers-golden.json', 'Messaging/Golden/messaging-golden.json',
  'Payroll/Golden/payroll-base.golden.json', 'Stays/Golden/stays-golden.json', 'Platform/Golden/base-fixture.golden.json', 'Inventory/Golden/inventory-base.golden.json']
  .map(f => path.join('tests', 'Hcc.' + f.replace('/', '.Tests/')));
const diffs = (a, b, p, out) => {
  if (out.length > 25) return;
  if (typeof a !== typeof b || Array.isArray(a) !== Array.isArray(b) || a === null || b === null || typeof a !== 'object') {
    if (JSON.stringify(a) !== JSON.stringify(b)) out.push(`${p}: committed=${JSON.stringify(a)?.slice(0, 140)} | regenerated=${JSON.stringify(b)?.slice(0, 140)}`);
    return;
  }
  if (Array.isArray(a) && a.length !== b.length) out.push(`${p}: array length committed=${a.length} regenerated=${b.length}`);
  for (const k of new Set([...Object.keys(a), ...Object.keys(b)])) diffs(a[k], b[k], `${p}.${k}`, out);
};
const lines = [`A08 golden regeneration check ${new Date().toISOString()} node ${process.version}`];
for (const f of files) {
  const regen = path.join(S, f);
  if (!fs.existsSync(regen)) { lines.push(`\n## ${f}: NOT regenerated — the exporter fails on the current prototype`); continue; }
  const out = [];
  diffs(JSON.parse(fs.readFileSync(path.join(R, f), 'utf8')), JSON.parse(fs.readFileSync(regen, 'utf8')), '$', out);
  lines.push(`\n## ${f}: ${out.length === 0 ? 'identical' : out.length + (out.length > 25 ? '+' : '') + ' differing paths (first 25)'}`);
  lines.push(...out.slice(0, 25));
}
fs.writeFileSync(path.join(process.cwd(), '05 Delivery Evidence', 'Audit', 'evidence', 'A08', 'golden_regen_diff.txt'), lines.join('\n'));
console.log(lines.join('\n'));
