// A05 step 1: enumerate every route under 08 Production/src/Hcc.Api, derive PERM ids from the Application code, write raw JSON/CSV.
// Usage: $env:PATH="E:\Tools\nodejs;$env:PATH"; node "05 Delivery Evidence\Audit\probes\A05_routes.mjs"
import fs from 'node:fs';
import { ROOT, SRC, indexSources, extractRoutes, handlerTargets, permsOf, permUses, usesPolicy, walk } from './A05_lib.mjs';

const OUT = ROOT + '/05 Delivery Evidence/Audit/evidence/A05';
fs.mkdirSync(OUT, { recursive: true });
const ix = indexSources();
console.log('node', process.version, '| source files', ix.files.length, '| methods', ix.methods.length, '| string consts', ix.consts.size);

const apiFiles = walk(SRC + '/Hcc.Api', p => p.endsWith('.cs'));
const mapCalls = {};
let routes = [];
for (const f of apiFiles) {
  const t = fs.readFileSync(f, 'utf8');
  const n = (t.match(/\.\s*Map(Get|Post)\s*\(/g) || []).length;
  if (n) mapCalls[f.replace(SRC + '/', '')] = n;
  routes = routes.concat(extractRoutes(ix, f));
}
// drop helper definitions (path built from a bare parameter, e.g. "/commands/" + name)
const helperDefs = routes.filter(r => !r.pathOk);
routes = routes.filter(r => r.pathOk);

const BASE = '/api/v1';
const dedupUses = (u) => { const m = new Map(); for (const x of u) { const k = [x.perm, x.fn, x.scope, x.amount, x.tag].join("|"); if (!m.has(k)) m.set(k, x); } return [...m.values()]; };
const rows = [];
for (const r of routes) {
  const full = BASE + r.prefix + (r.path.startsWith('/') || r.path === '' ? r.path : '/' + r.path);
  const targets = handlerTargets(ix, r);
  const direct = new Set(), via = new Set(); const calls = []; const uses = []; let policyUse = false;
  // permission ids written inline in the handler itself
  for (const x of r.handler.matchAll(/"(PERM-[A-Z]+-\d{3}(?::VIEW)?)"/g)) direct.add(x[1]);
  if (/policy|HasAsync|RequireAsync/.test(r.handlerB)) policyUse = true;
  for (const t of targets) for (const c of t.cands) {
    uses.push(...permUses(ix, c));
    const p = permsOf(ix, c);
    for (const k of p.direct) direct.add(k); for (const k of p.via) via.add(k);
    policyUse ||= usesPolicy(c);
    calls.push(c.cls + '.' + c.name);
  }
  const viaOnly = [...via].filter(k => !direct.has(k));
  rows.push({
    method: r.method, route: full, template: full.replace(/\{(\w+)(:\w+)?\}/g, '{$1}'),
    session: r.secured, file: r.file + ':' + r.line, helper: r.helper ?? '', calls: [...new Set(calls)],
    uses: dedupUses(uses), permsDirect: [...direct].sort(), permsVia: viaOnly.sort(), policyUse,
  });
}
rows.sort((a, b) => a.route.localeCompare(b.route) || a.method.localeCompare(b.method));
const dup = rows.filter((r, i) => i && rows[i - 1].route === r.route && rows[i - 1].method === r.method);

fs.writeFileSync(OUT + '/A05_routes.json', JSON.stringify(rows, null, 1));
const q = s => '"' + String(s).replace(/"/g, '""') + '"';
fs.writeFileSync(OUT + '/A05_routes.csv', ['method,route,session,source,helper,calls,perm_direct,perm_via,policy_use'].concat(rows.map(r =>
  [r.method, r.route, r.session, r.file, r.helper, r.calls.join(' | '), r.permsDirect.join(' '), r.permsVia.join(' '), r.policyUse].map(q).join(','))).join('\n'));
fs.writeFileSync(OUT + '/A05_mapcalls_per_file.json', JSON.stringify({ mapCalls, total: Object.values(mapCalls).reduce((a, b) => a + b, 0), helperDefinitionsSkipped: helperDefs.map(h => h.file + ':' + h.line + ' ' + h.rawPath) }, null, 1));

const summary = {
  node: process.version,
  routes: rows.length, duplicates: dup.map(d => d.method + ' ' + d.route),
  sessionRoutes: rows.filter(r => r.session).length, anonymous: rows.filter(r => !r.session).map(r => r.method + ' ' + r.route),
  withPerm: rows.filter(r => r.permsDirect.length || r.permsVia.length).length,
  noPerm: rows.filter(r => r.session && !r.permsDirect.length && !r.permsVia.length).map(r => r.method + ' ' + r.route + ' [' + r.calls.join(',') + '] policy=' + r.policyUse),
  mapCallsTotal: Object.values(mapCalls).reduce((a, b) => a + b, 0), helperDefsSkipped: helperDefs.length,
  unresolvedTargets: rows.filter(r => !r.calls.length).map(r => r.method + ' ' + r.route),
};
fs.writeFileSync(OUT + '/A05_enumeration_summary.json', JSON.stringify(summary, null, 1));
console.log(JSON.stringify(summary, null, 1));
