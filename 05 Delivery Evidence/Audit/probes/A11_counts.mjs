// A11 probe: static counts of test registrations, migrations, projects, endpoints, PERM ids, SCHEMA.
// No test is executed. Run from repo root with Node 25 first on PATH.
import fs from 'node:fs';
import path from 'node:path';

const ROOT = process.cwd();
const OUT = path.join(ROOT, '05 Delivery Evidence', 'Audit', 'evidence', 'A11');
fs.mkdirSync(OUT, { recursive: true });
const out = {};
const read = p => fs.readFileSync(path.join(ROOT, p), 'utf8');
const walk = (d, f, acc = []) => { for (const e of fs.readdirSync(d, { withFileTypes: true })) { if (['bin', 'obj', 'node_modules', '.git'].includes(e.name)) continue; const p = path.join(d, e.name); if (e.isDirectory()) walk(p, f, acc); else if (f(p)) acc.push(p); } return acc; };

// ---- prototype tests: test( registrations in files npm test loads ----
const T = path.join(ROOT, '03 Prototype', 'tests');
const protoFiles = ['run.mjs', 'm1.mjs', ...fs.readdirSync(T).filter(f => /^(stream|sync)-.*\.mjs$/.test(f)).sort()];
let protoTotal = 0; const perFile = {}; const dynamic = [];
for (const f of protoFiles) {
  const src = fs.readFileSync(path.join(T, f), 'utf8');
  // literal registrations: test('..' or test(`..` or test("..
  const n = (src.match(/(^|[^\w.$])test\(\s*['"`]/gm) || []).length;
  // registrations whose first arg is not a literal (loops / template names / helper)
  const nDyn = (src.match(/(^|[^\w.$])test\(\s*[^'"`\s]/gm) || []).length;
  perFile[f] = { literal: n, nonLiteralArg: nDyn };
  protoTotal += n;
  if (nDyn) dynamic.push(f + ':' + nDyn);
}
out.prototype = { filesLoadedByRunMjs: protoFiles.length, literalTestCalls: protoTotal, nonLiteralCallsNeedHumanLook: dynamic, perFile };
// last recorded run by npm test (artefact on disk; not re-run here)
try { const j = JSON.parse(read('03 Prototype/evidence/domain-tests.json')); out.prototype.lastRecordedRun = { runAt: j.runAt, passed: j.passed, total: j.total }; } catch { out.prototype.lastRecordedRun = null; }
out.prototype.nonLoadedTestFiles = fs.readdirSync(T).filter(f => !protoFiles.includes(f) && /\.(mjs|txt|tsx)$/.test(f));

// ---- server tests ----
const TS = path.join(ROOT, '08 Production', 'tests');
const projects = fs.readdirSync(TS, { withFileTypes: true }).filter(d => d.isDirectory()).map(d => d.name).sort();
const srv = {}; let facts = 0, theories = 0, inline = 0, memberTheories = 0, skipped = 0, otherAttr = {};
for (const p of projects) {
  const files = walk(path.join(TS, p), f => f.endsWith('.cs'));
  let fa = 0, th = 0, il = 0, mt = 0, sk = 0;
  for (const f of files) {
    const s = fs.readFileSync(f, 'utf8');
    fa += (s.match(/\[\s*Fact\b/g) || []).length;
    th += (s.match(/\[\s*Theory\b/g) || []).length;
    il += (s.match(/\[\s*InlineData\b/g) || []).length;
    mt += (s.match(/\[\s*(MemberData|ClassData)\b/g) || []).length;
    sk += (s.match(/Skip\s*=/g) || []).length;
    for (const m of s.matchAll(/\[\s*([A-Za-z]*(?:Fact|Theory)[A-Za-z]*)\b/g)) if (!['Fact', 'Theory'].includes(m[1])) otherAttr[m[1]] = (otherAttr[m[1]] || 0) + 1;
  }
  srv[p] = { fact: fa, theory: th, inlineData: il, memberData: mt, skip: sk };
  facts += fa; theories += th; inline += il; memberTheories += mt; skipped += sk;
}
out.server = { projects: projects.length, facts, theories, inlineDataRows: inline, memberOrClassDataAttrs: memberTheories, skipAttrs: skipped, otherFactLikeAttrs: otherAttr,
  upperBoundIfEachInlineRowIsOneCase: facts + inline, note: 'Theories w/ MemberData expand at runtime -> exact figure comes from A1 dotnet test.', perProject: srv };

// ---- migrations ----
const mig = fs.readdirSync(path.join(ROOT, '08 Production', 'database', 'migrations')).filter(f => f.endsWith('.sql')).sort();
out.migrations = { count: mig.length, files: mig };

// ---- solution projects ----
const slnx = read('08 Production/Hcc.slnx');
out.solution = { projectEntries: (slnx.match(/<Project\s+Path=/g) || []).length, testProjectsInSln: (slnx.match(/tests\/[^"]+\.csproj/g) || []).length, testDirs: projects.length };

// ---- endpoints ----
const EP = path.join(ROOT, '08 Production', 'src', 'Hcc.Api', 'Endpoints');
const epFiles = fs.readdirSync(EP).filter(f => f.endsWith('.cs'));
let mapGet = 0, mapPost = 0, mapOther = 0;
for (const f of epFiles) { const s = fs.readFileSync(path.join(EP, f), 'utf8'); mapGet += (s.match(/\.MapGet\(/g) || []).length; mapPost += (s.match(/\.MapPost\(/g) || []).length; mapOther += (s.match(/\.Map(Put|Delete|Patch)\(/g) || []).length; }
out.endpoints = { files: epFiles.length, MapGet: mapGet, MapPost: mapPost, MapPutDeletePatch: mapOther, total: mapGet + mapPost + mapOther };

// ---- PERM ids ----
const permSeen = new Set();
for (const f of walk(path.join(ROOT, '08 Production', 'database'), f => f.endsWith('.sql'))) for (const m of fs.readFileSync(f, 'utf8').matchAll(/PERM-[A-Z]+-\d{3}[a-z]?/g)) permSeen.add(m[0]);
const permContracts = new Set();
for (const f of walk(path.join(ROOT, '08 Production', 'src'), f => f.endsWith('.cs'))) for (const m of fs.readFileSync(f, 'utf8').matchAll(/PERM-[A-Z]+-\d{3}[a-z]?/g)) permContracts.add(m[0]);
out.perms = { inSeedSql: permSeen.size, inSrcCs: permContracts.size, inSrcNotInSeed: [...permContracts].filter(x => !permSeen.has(x)).sort(), inSeedNotInSrc: [...permSeen].filter(x => !permContracts.has(x)).sort() };

// ---- SCHEMA const ----
const types = read('03 Prototype/src/domain/types.ts');
out.schema = (types.match(/SCHEMA\s*=\s*(\d+)/) || [])[0] || null;

// ---- sync area modules ----
const syncDir = path.join(ROOT, '03 Prototype', 'src', 'clients', 'sync');
out.syncFiles = fs.readdirSync(syncDir).filter(f => /\.(ts|tsx)$/.test(f)).length;

fs.writeFileSync(path.join(OUT, 'counts.json'), JSON.stringify(out, null, 1));
console.log(JSON.stringify({ proto: { files: out.prototype.filesLoadedByRunMjs, literal: out.prototype.literalTestCalls, dyn: out.prototype.nonLiteralCallsNeedHumanLook, last: out.prototype.lastRecordedRun, notLoaded: out.prototype.nonLoadedTestFiles }, server: { ...out.server, perProject: undefined }, mig: out.migrations.count, sln: out.solution, ep: out.endpoints, perms: out.perms, schema: out.schema, syncFiles: out.syncFiles }, null, 1));
