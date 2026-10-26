// A05 step 4: validate the static route->role derivation against the REAL server answers recorded in the prototype's
// tests/_role-reads.json (written by Hcc.Integration.Tests RoleReadMatrixTests), and test the prototype's READS gating (reads.ts)
// against the same file. Needs Node >= 22.18 (type stripping) to import reads.ts as-is.
import fs from 'node:fs';
import { ROOT } from './A05_lib.mjs';
import { ROLES } from './A05_access.mjs';
const OUT = ROOT + '/05 Delivery Evidence/Audit/evidence/A05';
const rows = JSON.parse(fs.readFileSync(OUT + '/A05_route_access.json', 'utf8'));
const J = JSON.parse(fs.readFileSync(ROOT + '/03 Prototype/tests/_role-reads.json', 'utf8'));

const segs = s => s.split('/').filter(Boolean);
function findRow(pattern) {
  const [m, p] = pattern.split(' '); const ps = segs(p);
  let best = null, bestScore = -1;
  for (const r of rows) {
    if (r.method !== m) continue; const rs = segs(r.route.replace(/\{(\w+)(:\w+)?\}/g, '{$1}'));
    if (rs.length !== ps.length) continue; let score = 0, ok = true;
    for (let i = 0; i < rs.length; i++) { const wild = rs[i].startsWith('{'); const pw = ps[i].startsWith(':'); if (rs[i] === ps[i]) score += 2; else if (wild && pw) score += 1; else if (wild && !pw) score += 0; else { ok = false; break; } }
    if (ok && score > bestScore) { best = r; bestScore = score; }
  }
  return best;
}
const out = []; let agree = 0, total = 0; const unmatched = [];
for (const pattern of Object.keys(J.routes)) {
  const row = findRow(pattern); if (!row) { unmatched.push(pattern); continue; }
  for (const role of ROLES) {
    const real = J.roles[role].reads[pattern]; const c = row.reachAny[role];
    const predicted = c && !c.blocked ? '200' : '403';
    total++;
    if (real === predicted || (real === '404' && predicted === '200') || (real === '200' && !c && row.gatePerms.length === 0)) agree++;
    else out.push({ pattern, route: row.route, role, real, predicted, gate: row.gatePerms.join(' '), reveal: row.revealPerms.join(' '), cell: c ? c.mode + c.scope + (c.blocked ? ' (scoped, blocked unless scope passed)' : '') : '-', uses: row.uses.join(' ') });
  }
}
// session-only routes: no gate perms -> any role passes
const fixed = out.filter(o => !(o.real === '404' && o.predicted === '200') && !(o.real === '200' && o.gate === ''));
fs.writeFileSync(OUT + '/A05_validate_vs_real_server.json', JSON.stringify({ routesInFile: Object.keys(J.routes).length, unmatched, cellsCompared: total, agree, disagree: fixed.length, disagreements: fixed }, null, 1));
console.log('routes in _role-reads.json:', Object.keys(J.routes).length, '| unmatched:', unmatched.length, unmatched.join(', '));
console.log('role x route cells:', total, '| static derivation agrees with the real server:', total - fixed.length, '| disagrees:', fixed.length);
for (const o of fixed) console.log(o.role.padEnd(4), o.pattern.padEnd(42), 'real', o.real, 'static', o.predicted, '| gate', o.gate, '| reveal', o.reveal, '| cell', o.cell, '|', o.uses);

// ---- prototype READS (reads.ts) vs the real answers ----
const mod = await import('file:///' + ROOT + '/03 Prototype/src/clients/sync/reads.ts');
const READS = mod.READS; const protoDiff = []; let pTotal = 0;
for (const role of ROLES) {
  const me = { grants: J.roles[role].permissions.map(([permissionId, mode, scope]) => ({ permissionId, mode, scope })) };
  for (const pattern of Object.keys(J.routes)) {
    const fn = READS[pattern]; pTotal++;
    if (!fn) { protoDiff.push({ role, pattern, real: J.roles[role].reads[pattern], reads_ts: 'NO RULE' }); continue; }
    const allowed = fn(me) ? '200' : '403'; const real = J.roles[role].reads[pattern];
    if (allowed !== real && !(real === '404' && allowed === '200')) protoDiff.push({ role, pattern, real, reads_ts: allowed });
  }
}
const readsOnlyRules = Object.keys(READS).filter(k => !(k in J.routes));
fs.writeFileSync(OUT + '/A05_reads_ts_vs_role_reads_json.json', JSON.stringify({ rulesInReadsTs: Object.keys(READS).length, routesInJson: Object.keys(J.routes).length, cellsCompared: pTotal, disagreements: protoDiff, rulesWithNoRouteInJson: readsOnlyRules }, null, 1));
console.log('\nreads.ts rules:', Object.keys(READS).length, '| routes in json:', Object.keys(J.routes).length, '| cells:', pTotal, '| reads.ts disagrees with the real server:', protoDiff.length, '| rules with no route in json:', readsOnlyRules.join(', ') || 'none');
for (const d of protoDiff.slice(0, 40)) console.log(' ', d.role, d.pattern, 'real', d.real, 'reads.ts', d.reads_ts);
