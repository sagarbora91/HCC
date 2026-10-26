// A05 step 2: parse the security review's 373-row route table, diff against the A05 enumeration (method + normalised path), and
// diff the PERM ids derived here against the review's "Permission checked by the handler" column.
import fs from 'node:fs';
import { ROOT } from './A05_lib.mjs';
const OUT = ROOT + '/05 Delivery Evidence/Audit/evidence/A05';
const md = fs.readFileSync(ROOT + '/05 Delivery Evidence/Security/Security_Review_2026-10-26.md', 'utf8').split('\n');
const rev = [];
for (const line of md) {
  const m = /^\|\s*(\d+)\s*\|\s*([^|]+?)\s*\|\s*(GET|POST|PUT|DELETE)\s*\|\s*`([^`]+)`\s*\|\s*([^|]+?)\s*\|\s*([^|]*?)\s*\|\s*([^|]*?)\s*\|\s*$/.exec(line);
  if (!m) continue;
  rev.push({ n: +m[1], module: m[2], method: m[3], route: '/api/v1' + m[4], access: m[5], perm: m[6], note: m[7], perms: [...m[6].matchAll(/PERM-[A-Z]+-\d{3}|(?<=PERM-[A-Z]{3}-\d{3}(?:, |\/))\d{3}/g)].map(x => x[0]) });
}
const norm = (route) => route.replace(/\{(\w+)(:\w+)?\}/g, '{p}').replace(/\/+$/, '');
const mine = JSON.parse(fs.readFileSync(OUT + '/A05_routes.json', 'utf8'));
const key = r => r.method + ' ' + norm(r.route);
const mk = new Map(mine.map(r => [key(r), r])), rk = new Map(rev.map(r => [key(r), r]));
const onlyMine = mine.filter(r => !rk.has(key(r))).map(r => r.method + ' ' + r.route + '  (' + r.file + ')');
const onlyRev = rev.filter(r => !mk.has(key(r))).map(r => '#' + r.n + ' ' + r.method + ' ' + r.route + '  [' + r.perm + ']');
const revKeys = rev.map(key); const dupRev = revKeys.filter((k, i) => revKeys.indexOf(k) !== i);

// expand review perm text like "PERM-ADM-003, PERM-RPT-001" and "any mode of PERM-ADM-001 / 002 / 003"
function permsFromText(t) {
  const out = new Set(); let last = null;
  for (const x of t.matchAll(/PERM-([A-Z]+)-(\d{3})|(?:\/|,)\s*(\d{3})\b/g)) { if (x[1]) { last = x[1]; out.add(`PERM-${x[1]}-${x[2]}`); } else if (last) out.add(`PERM-${last}-${x[3]}`); }
  return out;
}
const permDiff = [];
for (const r of rev) {
  const m = mk.get(key(r)); if (!m) continue;
  const rp = permsFromText(r.perm); const mp = new Set([...m.permsDirect, ...m.permsVia].map(p => p.replace(':VIEW', '')));
  const a = [...rp].filter(p => !mp.has(p)), b = [...mp].filter(p => !rp.has(p));
  if (a.length || b.length) permDiff.push({ n: r.n, route: r.method + ' ' + r.route, review: r.perm, reviewOnly: a, a05Only: b, a05Direct: m.permsDirect, a05Via: m.permsVia });
}
const res = { reviewRows: rev.length, reviewDupKeys: dupRev, a05Rows: mine.length, onlyInA05: onlyMine, onlyInReview: onlyRev, permDiffCount: permDiff.length, permDiff };
fs.writeFileSync(OUT + '/A05_review_diff.json', JSON.stringify(res, null, 1));
fs.writeFileSync(OUT + '/A05_review_rows.json', JSON.stringify(rev, null, 1));
console.log(JSON.stringify({ reviewRows: rev.length, a05Rows: mine.length, onlyInA05: onlyMine, onlyInReview: onlyRev, dupRev, permDiffCount: permDiff.length }, null, 1));
