// A05 step 5: prototype side. (1) PERMISSION_MAP completeness vs the 82 seeded server permissions; (2) coarse permissions per role derived from the
// server seed (via PERMISSION_MAP) vs the prototype's coarse fixture grants; (3) the prototype's MATRIX (admin.ts, fine grained) vs the server seed;
// (4) the CAPABILITY_GAPS statements re-checked; (5) ROLE_MAP. Parses the TypeScript as text (no imports) and recomputes permissionsFromGrants.
import fs from 'node:fs';
import { ROOT } from './A05_lib.mjs';
import { ROLES, grants, perms } from './A05_access.mjs';
const OUT = ROOT + '/05 Delivery Evidence/Audit/evidence/A05';
const P = ROOT + '/03 Prototype/src';
const cap = fs.readFileSync(P + '/clients/sync/capabilities.ts', 'utf8');

// PERMISSION_MAP
const map = {};
for (const m of cap.matchAll(/^\s*(\w+):\{any:\[([^\]]*)\],modes:'([A-Z]+)'\}/gm)) map[m[1]] = { any: [...m[2].matchAll(/'(PERM-[A-Z]+-\d{3})'/g)].map(x => x[1]), modes: m[3] };
const roleMap = Object.fromEntries([...cap.matchAll(/(\w+):'([A-Za-z ]+)'/g)].filter(x => ROLES.includes(x[1])).map(x => [x[1], x[2]]));
const gaps = [...cap.matchAll(/^\s*'((?:[^'\\]|\\.)*)',?\s*$/gm)].map(x => x[1]).filter(s => s.length > 60);
const types = fs.readFileSync(P + '/domain/types.ts', 'utf8');
const protoPerms = /export type Permission=([^;]+);/.exec(types)[1].split('|').map(s => s.replace(/'/g, '').trim());
const protoRoles = /export type Role=([^;]+);/.exec(types)[1].split('|').map(s => s.replace(/'/g, '').trim());

const mapped = new Set(Object.values(map).flatMap(r => r.any));
const serverIds = Object.keys(perms).sort();
const unmapped = serverIds.filter(id => !mapped.has(id));
const mapNotOnServer = [...mapped].filter(id => !perms[id]);
const protoPermsUnmapped = protoPerms.filter(p => !map[p]);
const byModule = {};
for (const id of unmapped) (byModule[id.split('-')[1]] ??= []).push(id);

// coarse permissions per role from the seed (what meFromCapabilities would build; GET /me/capabilities lists every grant of the role)
const roleName = roleMap;
const coarseFromSeed = {};
for (const r of ROLES) {
  const gs = Object.entries(grants[r] ?? {}).map(([permissionId, g]) => ({ permissionId, mode: g.mode }));
  coarseFromSeed[r] = Object.keys(map).filter(p => gs.some(g => map[p].any.includes(g.permissionId) && map[p].modes.includes(g.mode)));
}
// prototype coarse fixture grants (fixtures/base.ts)
const base = fs.readFileSync(P + '/fixtures/base.ts', 'utf8');
const allPerms = protoPerms;
const protoCoarse = {
  OWN: allPerms, MGR: allPerms.filter(p => p !== 'admin'), REC: ['reservations', 'checkin', 'checkout', 'billing', 'payments', 'expenses'], CSH: ['billing', 'payments', 'expenses', 'reports'],
  SUP: ['housekeeping', 'inspect'], HKP: ['housekeeping'], ACC: ['reports'], MNT: ['housekeeping'], TAD: ['admin'],
};
// guard: confirm those literals are still what base.ts says
const baseLine = /grants:\{Owner:\[\.\.\.permissions\],Manager:permissions\.filter\(p=>p!=='admin'\),\s*Reception:\[([^\]]*)\],\s*Cashier:\[([^\]]*)\],Supervisor:\[([^\]]*)\],Housekeeper:\[([^\]]*)\],\s*(?:\/\/[^\n]*\n\s*)?Accountant:\[([^\]]*)\],Maintenance:\[([^\]]*)\],'Tech admin':\[([^\]]*)\]/s.exec(base);
const lit = s => s.split(',').map(x => x.replace(/'/g, '').trim()).filter(Boolean);
const baseOk = baseLine && JSON.stringify([lit(baseLine[1]), lit(baseLine[2]), lit(baseLine[3]), lit(baseLine[4]), lit(baseLine[5]), lit(baseLine[6]), lit(baseLine[7])]) ===
  JSON.stringify([protoCoarse.REC, protoCoarse.CSH, protoCoarse.SUP, protoCoarse.HKP, protoCoarse.ACC, protoCoarse.MNT, protoCoarse.TAD]);
const coarseDiff = [];
for (const r of ROLES) {
  const s = new Set(coarseFromSeed[r]), p = new Set(protoCoarse[r]);
  const serverOnly = [...s].filter(x => !p.has(x)), protoOnly = [...p].filter(x => !s.has(x));
  if (serverOnly.length || protoOnly.length) coarseDiff.push({ role: r, serverSeedGives: [...s].sort(), prototypeFixtureGives: [...p].sort(), serverOnly, prototypeOnly: protoOnly });
}

// prototype fine-grained MATRIX (admin.ts) vs seed 0101
const adm = fs.readFileSync(P + '/domain/streams/admin.ts', 'utf8');
const protoMatrix = {};
for (const m of adm.matchAll(/^\s*\['(PERM-[A-Z]+-\d{3})','[^']*','[^']*',(?:true|false),(?:'\w+'|undefined),'([^']+)'\]/gm)) {
  const cells = m[2].split(' '); protoMatrix[m[1]] = {}; ROLES.forEach((r, i) => { const c = cells[i] ?? '—'; protoMatrix[m[1]][r] = c === '—' ? '-' : c; });
}
const matrixDiff = [];
for (const id of Object.keys(protoMatrix)) for (const r of ROLES) {
  const g = grants[r]?.[id]; const s = g ? g.mode + (g.scope !== 'P' ? '·' + g.scope : '') : '-';
  if (s !== protoMatrix[id][r]) matrixDiff.push({ perm: id, role: r, prototype: protoMatrix[id][r], seed: s });
}
const seedCore = serverIds.filter(id => /^PERM-(RES|RATE|GST|STY|FOL|PAY|SHF|HK|ROOM|EXP|CLS|APR|RPT|ADM)-/.test(id));
const protoMatrixMissing = seedCore.filter(id => !protoMatrix[id]);

// CAPABILITY_GAPS claim checks
const has = (role, id, modes = 'AL') => { const g = grants[role]?.[id]; return !!g && modes.includes(g.mode); };
const claims = [
  { n: 1, text: 'Accountant gets "expenses" (server PERM-EXP-001:A); the prototype Accountant has reports only.', check: has('ACC', 'PERM-EXP-001') && coarseFromSeed.ACC.includes('expenses') && !protoCoarse.ACC.includes('expenses'), note: 'seed ACC EXP-001 A·O; PERMISSION_MAP expenses accepts modes AL' },
  { n: 2, text: 'Cashier gets "reports" through PERM-RPT-002:V; Accountant/Manager/Owner through PERM-RPT-001.', check: has('CSH', 'PERM-RPT-002', 'V') && coarseFromSeed.CSH.includes('reports') && has('ACC', 'PERM-RPT-001') && has('MGR', 'PERM-RPT-001') && has('OWN', 'PERM-RPT-001'), note: 'true; RPT-002 is also held A by OWN, MGR, ACC' },
  { n: 3, text: '"approve" follows PERM-APR-001 only (Owner A, Manager L).', check: JSON.stringify(map.approve.any) === '["PERM-APR-001"]' && has('OWN', 'PERM-APR-001') && has('MGR', 'PERM-APR-001', 'L'), note: '' },
  { n: 6, text: 'Module PERM ids without a coarse prototype permission (PERM-ACC/CMP/CRM/DOC/GRP/HR/INV/OTA/SOD/WEB, PERM-HOV-*, PERM-MNT-*, PERM-POS*) do not change can().', check: null, note: 'PERM-HOV-*, PERM-MNT-*, PERM-POS-* and PERM-SOD-* (as grants) do not exist on the server; module routes borrow core ids (v2 §1.2). PERM-IMP-* (W4-13) and PERM-RATE are not named.' },
];
const out = {
  protoPermissions: protoPerms, protoRoles, roleMap, permissionMap: map, serverPermissionsSeeded: serverIds.length, mappedServerIds: [...mapped].sort(), unmappedServerIds: unmapped, unmappedByModule: byModule,
  mapIdsNotOnServer: mapNotOnServer, protoPermissionsWithoutRule: protoPermsUnmapped, rolesOnServerNotInRoleMap: ROLES.filter(r => !roleMap[r]), protoRolesNotOnServer: protoRoles.filter(r => !Object.values(roleMap).includes(r)),
  coarseFromSeed, protoCoarse, fixtureLiteralStillMatches: !!baseOk, coarseDiff, protoMatrixRows: Object.keys(protoMatrix).length, matrixCellsCompared: Object.keys(protoMatrix).length * 9, matrixDiff, seedCoreIdsMissingInPrototypeMatrix: protoMatrixMissing,
  gapsDeclared: gaps, gapChecks: claims,
};
fs.writeFileSync(OUT + '/A05_prototype_permission_map.json', JSON.stringify(out, null, 1));
console.log(JSON.stringify({ ...out, permissionMap: undefined, mappedServerIds: undefined, gapsDeclared: gaps.length, unmappedServerIds: unmapped.length }, null, 1));
