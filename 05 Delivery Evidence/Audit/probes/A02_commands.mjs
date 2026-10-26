// A02 probe: prototype command types -> sync mappers -> server /commands/<Name> routes; prototype screens -> commands -> slices.
// Read-only over the repository. Run from the repo root:
//   $env:PATH="E:\Tools\nodejs;$env:PATH"; node "05 Delivery Evidence/Audit/probes/A02_commands.mjs"
// Writes (only) 05 Delivery Evidence/Audit/evidence/A02/*.json and a scratch bundle under the OS temp dir.
// Uses esbuild and typescript from 03 Prototype/node_modules (no install, no repo writes).
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {createRequire} from 'node:module';
import {pathToFileURL} from 'node:url';

const ROOT = process.cwd();
const PROTO = path.join(ROOT, '03 Prototype');
const SYNC = path.join(PROTO, 'src', 'clients', 'sync');
const SRV = path.join(ROOT, '08 Production', 'src');
const OUT = path.join(ROOT, '05 Delivery Evidence', 'Audit', 'evidence', 'A02');
fs.mkdirSync(OUT, {recursive: true});
const req = createRequire(path.join(PROTO, 'package.json'));
const esbuild = req('esbuild');
const ts = req('typescript');
const rel = p => path.relative(ROOT, p).split(path.sep).join('/');
const read = p => fs.readFileSync(p, 'utf8');
const walk = (dir, ext, out = []) => {
  for (const e of fs.readdirSync(dir, {withFileTypes: true})) {
    if (e.name === 'node_modules' || e.name === 'bin' || e.name === 'obj') continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, ext, out); else if (ext.some(x => e.name.endsWith(x))) out.push(p);
  }
  return out;
};
const save = (name, obj) => fs.writeFileSync(path.join(OUT, name), JSON.stringify(obj, null, 1));
console.log('node', process.version);

// ───────────────────────── 1. server command routes (C#) ─────────────────────────
const csFiles = walk(SRV, ['.cs']);
const consts = {};          // "Class.Const" -> value, plus bare "Const" -> [values]
const bare = {};
for (const f of csFiles) {
  const lines = read(f).split(/\r?\n/);
  let cls = '';
  lines.forEach((ln, i) => {
    const c = ln.match(/\b(?:class|record|struct|interface)\s+(\w+)/);
    if (c) cls = c[1];
    for (const m of ln.matchAll(/\bconst\s+string\s+(\w+)\s*=\s*"([^"]*)"/g)) {
      consts[cls + '.' + m[1]] = {value: m[2], at: rel(f) + ':' + (i + 1)};
      (bare[m[1]] ||= []).push(m[2]);
    }
  });
}
const apiFiles = csFiles.filter(f => f.includes(path.join('Hcc.Api', '')) && !f.includes(path.sep + 'bin' + path.sep));
const serverCmds = {};      // name -> {routes:[{file,line,how}]}
const otherPosts = [];      // POST routes not under /commands
const unresolved = [];
let mapPostCalls = 0;
const addCmd = (name, f, line, how) => { (serverCmds[name] ||= {routes: []}).routes.push({at: rel(f) + ':' + line, how}); };
const resolveConst = (expr, f, line) => {
  const k = expr.trim();
  if (consts[k]) return consts[k].value;
  const last = k.split('.').pop();
  if (bare[last] && new Set(bare[last]).size === 1) return bare[last][0];
  unresolved.push({at: rel(f) + ':' + line, expr: k});
  return null;
};
for (const f of apiFiles) {
  const text = read(f), lines = text.split(/\r?\n/);
  lines.forEach((ln, i) => {
    const L = i + 1;
    for (const m of ln.matchAll(/\.MapPost\(/g)) mapPostCalls++;
    let m;
    if ((m = ln.match(/MapPost\("\/commands\/([A-Za-z0-9]+)"/))) addCmd(m[1], f, L, 'MapPost literal');
    else if ((m = ln.match(/MapPost\("\/commands\/"\s*\+\s*([\w.]+)/))) {
      if (/^(name|op)$/.test(m[1])) { /* generic helper; callers below */ }
      else { const v = resolveConst(m[1], f, L); if (v) addCmd(v, f, L, 'MapPost "/commands/" + ' + m[1]); }
    } else if (/MapPost\(/.test(ln) && !/MapPost\("\/" \+ name/.test(ln)) {
      const mm = ln.match(/MapPost\("([^"]*)"/); if (mm) otherPosts.push({route: mm[1], at: rel(f) + ':' + L});
    }
    // helper calls  Command<..>(g, "Name"|Class.OpX, ...)  /  Cmd<..>(g, ...)
    const h = ln.match(/\b(?:Command|Cmd)<[^()]*>\(\s*(\w+)\s*,\s*("([A-Za-z0-9]+)"|[\w.]+)\s*,/);
    if (h && !/static void|static IResult/.test(ln)) {
      const grp = h[1];
      const name = h[3] ?? resolveConst(h[2], f, L);
      const adminGroup = path.basename(f) === 'AccessEndpoints.cs' && grp !== 'commands';
      if (name) {
        if (adminGroup) otherPosts.push({route: '/admin/' + name + ' (group ' + grp + ')', at: rel(f) + ':' + L});
        else addCmd(name, f, L, 'helper ' + (h[3] ? 'literal' : h[2]));
      }
    }
  });
}
// Setup etc.
const serverNames = new Set(Object.keys(serverCmds));
save('server-routes.json', {commandCount: serverNames.size, mapPostCalls, commands: serverCmds, otherPosts, unresolved});
console.log('server /commands names:', serverNames.size, '| MapPost calls:', mapPostCalls, '| other POST routes:', otherPosts.length, '| unresolved:', unresolved.length);

// ───────────────────────── 2. bundle prototype mock + sync registry ─────────────────────────
const handlerDir = path.join(PROTO, 'src', 'services', 'mock', 'handlers');
const handlerFiles = fs.readdirSync(handlerDir).filter(f => f.endsWith('.ts') && f !== 'index.ts').map(f => f.replace(/\.ts$/, ''));
const stdin = `
export {AREAS} from './src/clients/sync/registry';
export {registry} from './src/services/mock/handlers';
export {emptyState} from './src/clients/sync/empty';
export {PERMISSION_MAP,CAPABILITY_GAPS} from './src/clients/sync/capabilities';
export * as HTTP from './src/clients/http';
export * as VIEW from './src/clients/view';
${handlerFiles.map((f, i) => `import {handlers as H${i}} from './src/services/mock/handlers/${f}';`).join('\n')}
export const HANDLER_FILES={${handlerFiles.map((f, i) => `${JSON.stringify(f)}:Object.keys(H${i})`).join(',')}};
`;
const tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'a02-'));
const bundlePath = path.join(tmp, 'bundle.mjs');
await esbuild.build({stdin: {contents: stdin, resolveDir: PROTO, loader: 'ts', sourcefile: 'a02-entry.ts'}, bundle: true, platform: 'node', format: 'esm',
  outfile: bundlePath, logLevel: 'error', absWorkingDir: PROTO, define: {'process.env.HCC_BUILD': '"production"', 'process.env.NODE_ENV': '"production"'},
  banner: {js: "import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
const B = await import(pathToFileURL(bundlePath).href);
const bundleText = read(bundlePath);

// ───────────────────────── 3. prototype command universe ─────────────────────────
const cmdsSrc = read(path.join(PROTO, 'src', 'services', 'mock', 'commands.ts'));
const swStart = cmdsSrc.indexOf('else switch(cmd.type){');
const swEnd = cmdsSrc.indexOf("default:fail('VALIDATION','Unknown action.')");
const switchCases = [...cmdsSrc.slice(swStart, swEnd).matchAll(/^  case '([A-Z0-9_]+)':/gm)].map(m => m[1]);
const handlerOwner = {};       // type -> file
for (const [f, keys] of Object.entries(B.HANDLER_FILES)) for (const k of keys) (handlerOwner[k] ||= []).push(f);
const handlerKeys = Object.keys(B.registry);
const universe = [...new Set([...switchCases, ...handlerKeys])].sort();
const overridden = switchCases.filter(t => handlerKeys.includes(t));
const dupHandlers = Object.entries(handlerOwner).filter(([, v]) => v.length > 1);
console.log('prototype command types:', universe.length, '(switch', switchCases.length, '+ handlers', handlerKeys.length, ', overridden', overridden.length, ')');

// ───────────────────────── 4. sync areas ─────────────────────────
const syncFiles = walk(SYNC, ['.ts', '.tsx']);
const syncText = Object.fromEntries(syncFiles.map(f => [f, read(f)]));
const findFile = (type, kind) => {   // kind 'mapper' -> type:'X' ; 'unmapped' -> X: key
  const re = kind === 'mapper' ? new RegExp(`type\\s*:\\s*'${type}'`) : new RegExp(`(^|[\\s{,])'?${type}'?\\s*:`);
  const hits = syncFiles.filter(f => re.test(syncText[f]));
  return hits.map(rel);
};
// function table from the bundle (for tracing helpers called by a mapper's send())
const sf = ts.createSourceFile('bundle.js', bundleText, ts.ScriptTarget.ES2022, true);
const fnTable = new Map();   // name -> [node]
const topLevelNames = new Set();
const isFn = n => n && (ts.isArrowFunction(n) || ts.isFunctionExpression(n));
(function collect(node, depth) {
  if (ts.isFunctionDeclaration(node) && node.name) { (fnTable.get(node.name.text) ?? fnTable.set(node.name.text, []).get(node.name.text)).push(node); if (node.parent === sf) topLevelNames.add(node.name.text); }
  else if (ts.isVariableDeclaration(node) && ts.isIdentifier(node.name) && isFn(node.initializer)) { (fnTable.get(node.name.text) ?? fnTable.set(node.name.text, []).get(node.name.text)).push(node.initializer); if (node.parent.parent.parent === sf) topLevelNames.add(node.name.text); }
  else if (ts.isPropertyAssignment(node) && (ts.isIdentifier(node.name) || ts.isStringLiteral(node.name)) && isFn(node.initializer)) { const n = node.name.text; (fnTable.get(n) ?? fnTable.set(n, []).get(n)).push(node.initializer); }
  else if (ts.isMethodDeclaration(node) && node.name && (ts.isIdentifier(node.name))) { const n = node.name.text; (fnTable.get(n) ?? fnTable.set(n, []).get(n)).push(node); }
  ts.forEachChild(node, c => collect(c, depth + 1));
})(sf, 0);
const STOP = new Set(['send', 'load', 'get', 'set', 'run', 'has', 'map', 'filter', 'push', 'find', 'some', 'every', 'then', 'catch', 'forEach', 'reduce', 'slice', 'join', 'keys', 'values', 'entries',
  'command', 'precheck', 'track', 'expectedVersion', 'learn', 'server', 'setVersion', 'current', 'can', 'mayRead', 'String', 'Number', 'Boolean', 'Object', 'Array', 'JSON', 'Math', 'Date', 'Promise', 'Error', 'trim', 'toLowerCase', 'toUpperCase']);
const SERVER_LIKE = /^[A-Z][A-Za-z0-9]{3,}$/;
function analyse(fnSrcNode) {
  const direct = new Set(), literals = new Set(), literalsOwn = new Set(), dynamic = [], via = new Set(), visited = new Set();
  const visit = (node, depth, chain) => {
    if (visited.has(node)) return; visited.add(node);
    const walkN = n => {
      if (ts.isStringLiteralLike(n) && SERVER_LIKE.test(n.text)) { literals.add(n.text); if (depth === 0) literalsOwn.add(n.text); }
      if (ts.isCallExpression(n)) {
        const callee = n.expression;
        const name = ts.isIdentifier(callee) ? callee.text : ts.isPropertyAccessExpression(callee) ? callee.name.text : null;
        if (name === 'command' && n.arguments.length) {
          const a0 = n.arguments[0]; let found = false;
          const grab = x => { if (ts.isStringLiteralLike(x)) { direct.add(x.text); found = true; } ts.forEachChild(x, grab); };
          grab(a0);
          if (!found) dynamic.push(n.getText().slice(0, 80));
        } else if (name && !STOP.has(name) && fnTable.has(name) && depth < 7) {
          for (const t of fnTable.get(name)) { if (t === node) continue; via.add(name); visit(t, depth + 1, chain.concat(name)); }
        }
      } else if (ts.isIdentifier(n) && topLevelNames.has(n.text) && !STOP.has(n.text) && depth < 7 && !(n.parent && ts.isCallExpression(n.parent) && n.parent.expression === n)) {
        // function passed by reference (e.g. .map(send1))
        for (const t of fnTable.get(n.text) ?? []) { if (t === node) continue; via.add(n.text); visit(t, depth + 1, chain.concat(n.text)); }
      }
      ts.forEachChild(n, walkN);
    };
    walkN(node);
  };
  visit(fnSrcNode, 0, []);
  return {direct: [...direct], literals: [...literals], literalsOwn: [...literalsOwn], dynamic, via: [...via].filter(x => topLevelNames.has(x))};
}
function parseFnSource(src) {   // Function.prototype.toString of a method / arrow -> a node
  let code = src.trim();
  if (/^(async\s+)?send\s*\(/.test(code)) code = code.replace(/^(async\s+)?send\s*/, '$1function ');
  const s = ts.createSourceFile('x.js', '(' + code + ')', ts.ScriptTarget.ES2022, true);
  return s;
}

const areas = [];
const mappers = {};      // type -> {...}
const unmappedAll = {};  // type -> [{area,reason}]
const sliceOwners = {};  // slice -> [{area,kind}]
const dupMappers = [];
for (const a of B.AREAS) {
  const A = {id: a.id, loaders: [], commands: [], unmappedCount: Object.keys(a.unmapped ?? {}).length};
  for (const l of a.loaders) {
    A.loaders.push({slice: l.slice, kind: l.kind ?? 'own', requires: l.requires ? [].concat(l.requires) : [], reads: l.reads ?? [], needs: l.needs ?? []});
    (sliceOwners[l.slice] ||= []).push({area: a.id, kind: l.kind ?? 'own'});
  }
  for (const c of a.commands) {
    const srcSf = parseFnSource(String(c.send));
    const an = analyse(srcSf);
    const entry = {type: c.type, area: a.id, files: findFile(c.type, 'mapper').sort((x, y) => Number(path.basename(y).replace(/\.tsx?$/, '') === a.id) - Number(path.basename(x).replace(/\.tsx?$/, '') === a.id)), reload: c.reload, precheck: !!c.precheck, track: !!c.track,
      direct: an.direct, literals: an.literals, dynamic: an.dynamic, via: an.via, sendLen: String(c.send).length};
    // direct = literal first argument of a `.command(` call reached from send() (incl. helpers); literalsOwn = server command names written
    // in the mapper's own source (covers helpers such as post(c,'SaveStaff',…) where the name is a parameter).
    entry.literalsOwn = an.literalsOwn.filter(x => serverNames.has(x));
    entry.targets = [...new Set([...an.direct, ...entry.literalsOwn])];
    entry.indirectOnly = entry.targets.filter(x => !an.direct.includes(x));
    // drop huge/noisy fields from the saved evidence
    delete entry.literals;
    A.commands.push(entry);
    if (mappers[c.type]) dupMappers.push(c.type);
    mappers[c.type] = entry;
  }
  for (const [t, why] of Object.entries(a.unmapped ?? {})) (unmappedAll[t] ||= []).push({area: a.id, reason: why, files: findFile(t, 'unmapped')});
  areas.push(A);
}
const mappedTypes = Object.keys(mappers);
const unmappedTypes = Object.keys(unmappedAll);
const mappedNotInUniverse = mappedTypes.filter(t => !universe.includes(t));
const unmappedNotInUniverse = unmappedTypes.filter(t => !universe.includes(t));
const both = unmappedTypes.filter(t => mappers[t]);
console.log('areas', B.AREAS.length, '| mapped types', mappedTypes.length, '| unmapped keys', unmappedTypes.length, '| mapped-but-not-in-prototype', mappedNotInUniverse.length, '| unmapped-not-in-prototype', unmappedNotInUniverse.length, '| both', both.length);

// loader reads vs tests/_role-reads.json routes
const roleReads = JSON.parse(read(path.join(PROTO, 'tests', '_role-reads.json')));
const readRoutes = Object.keys(roleReads.routes ?? {});
const loaderReadsMissing = [];
for (const A of areas) for (const l of A.loaders) for (const r of l.reads) if (!readRoutes.includes(r)) loaderReadsMissing.push({area: A.id, slice: l.slice, route: r});

// status per prototype command
const status = {};
for (const t of universe) {
  if (mappers[t]) status[t] = 'MAPPED';
  else if (unmappedAll[t]) status[t] = 'UNMAPPED';
  else status[t] = 'ABSENT';
}
// area attribution for unmapped/absent: owning handler file (mock stream) -> sync area, via the module that handles it
const owner = t => (handlerOwner[t] ? handlerOwner[t].join('+') : (switchCases.includes(t) ? 'commands.ts (core switch)' : '?'));

save('commands.json', {universe, switchCases, handlerKeys, overridden, dupHandlers, status, owner: Object.fromEntries(universe.map(t => [t, owner(t)])),
  mappers, unmapped: unmappedAll, mappedNotInUniverse, unmappedNotInUniverse, both, dupMappers, areas, sliceOwners, loaderReadsMissing,
  capabilityGaps: B.CAPABILITY_GAPS, handlerFiles: B.HANDLER_FILES});

// ───────────────────────── 5. mapper targets vs server routes ─────────────────────────
const mapperTargets = [];
for (const m of Object.values(mappers)) {
  for (const t of m.direct) mapperTargets.push({proto: m.type, area: m.area, file: m.files[0], target: t, exists: serverNames.has(t), route: serverCmds[t]?.routes[0]?.at ?? null});
}
const missingTargets = mapperTargets.filter(x => !x.exists);
const noTarget = Object.values(mappers).filter(m => !m.targets.length);
save('mapper-targets.json', {total: mapperTargets.length, missingTargets, mappersWithNoServerCommand: noTarget.map(m => ({type: m.type, area: m.area, dynamic: m.dynamic, via: m.via})), mapperTargets});
console.log('mapper->server targets:', mapperTargets.length, '| missing on server:', missingTargets.length, '| mappers with no detected server command:', noTarget.length);

// server commands never targeted by any mapper (info)
const targeted = new Set(mapperTargets.map(x => x.target)); for (const m of Object.values(mappers)) m.targets.forEach(t => targeted.add(t));
const serverNotTargeted = [...serverNames].filter(n => !targeted.has(n)).sort();
save('server-not-targeted.json', serverNotTargeted);

// ───────────────────────── 6. screens ─────────────────────────
// 6a. TS program-less symbol graph over src/features, src/mobile, src/app (relative imports only)
const SRC = path.join(PROTO, 'src');
const uiFiles = [...walk(path.join(SRC, 'features'), ['.ts', '.tsx']), ...walk(path.join(SRC, 'mobile'), ['.ts', '.tsx']), ...walk(path.join(SRC, 'app'), ['.ts', '.tsx']), ...walk(path.join(SRC, 'components'), ['.ts', '.tsx'])];
const fileInfo = new Map();
const resolveImport = (from, spec) => {
  if (!spec.startsWith('.')) return null;
  const base = path.resolve(path.dirname(from), spec);
  for (const c of [base + '.tsx', base + '.ts', path.join(base, 'index.tsx'), path.join(base, 'index.ts')]) if (fs.existsSync(c)) return c;
  return null;
};
const stateKeys = Object.keys(B.emptyState());
const stateKeySet = new Set(stateKeys);
const prodOnly = [...new Set(Object.keys(mappers).filter(t => !universe.includes(t)))];
const cmdSet = new Set([...universe, ...prodOnly]);
for (const t of prodOnly) status[t] = "MAPPED"; // production-only mappers (no prototype handler): counted as mapped on screens
// Files the symbol walk never enters: sync layer, mock handlers/commands/persistence (they are the other side of the contract),
// fixtures, shared components, and src/app (except app/mobile and the barrel src/services/mock/index.ts that re-exports domain code).
function pruned(target) {
  const p = target.split(path.sep).join('/');
  if (p.endsWith('/src/services/mock/index.ts')) return false;
  if (/\/src\/(clients|services|fixtures|components)\//.test(p)) return true;
  if (/\/src\/app\//.test(p) && !/\/app\/mobile\//.test(p)) return true;
  return false;
}
function getInfo(f) {
  if (fileInfo.has(f)) return fileInfo.get(f);
  const info = {decls: new Map(), imports: new Map(), reexports: new Map(), stars: [], sf: null};
  fileInfo.set(f, info);
  const text = read(f);
  const s = ts.createSourceFile(f, text, ts.ScriptTarget.ES2022, true, f.endsWith('x') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
  info.sf = s;
  for (const st of s.statements) {
    if (ts.isImportDeclaration(st) && ts.isStringLiteral(st.moduleSpecifier)) {
      const target = resolveImport(f, st.moduleSpecifier.text);
      const ic = st.importClause; if (!ic || !target || pruned(target)) continue;
      if (ic.name) info.imports.set(ic.name.text, {file: target, name: 'default'});
      if (ic.namedBindings && ts.isNamedImports(ic.namedBindings)) for (const e of ic.namedBindings.elements) info.imports.set(e.name.text, {file: target, name: (e.propertyName ?? e.name).text});
    } else if (ts.isExportDeclaration(st) && st.moduleSpecifier && ts.isStringLiteral(st.moduleSpecifier)) {
      const target = resolveImport(f, st.moduleSpecifier.text);
      if (target && !pruned(target) && st.exportClause && ts.isNamedExports(st.exportClause)) for (const e of st.exportClause.elements) info.reexports.set(e.name.text, {file: target, name: (e.propertyName ?? e.name).text});
      else if (target && !pruned(target) && !st.exportClause) info.stars.push(target);
    } else if (ts.isFunctionDeclaration(st) && st.name) info.decls.set(st.name.text, st);
    else if (ts.isClassDeclaration(st) && st.name) info.decls.set(st.name.text, st);
    else if (ts.isVariableStatement(st)) for (const d of st.declarationList.declarations) if (ts.isIdentifier(d.name)) info.decls.set(d.name.text, d);
  }
  return info;
}
function resolveSym(f, name, depth = 0) {   // -> {file, name, node} | null
  if (depth > 6) return null;
  const info = getInfo(f);
  if (info.decls.has(name)) return {file: f, name, node: info.decls.get(name)};
  if (info.reexports.has(name)) { const r = info.reexports.get(name); return resolveSym(r.file, r.name, depth + 1); }
  if (info.imports.has(name)) { const r = info.imports.get(name); return resolveSym(r.file, r.name === 'default' ? 'default' : r.name, depth + 1); }
  for (const st of info.stars) { const r = resolveSym(st, name, depth + 1); if (r) return r; }
  return null;
}
const nodeFacts = new Map();   // key file#name -> {cmds:Set, modals:Set, slices:Set, refs:[{file,name}]}
const symKey = (f, n) => f + '#' + n;
function factsOf(f, name) {
  const key = symKey(f, name);
  if (nodeFacts.has(key)) return nodeFacts.get(key);
  const facts = {file: f, name, cmds: new Set(), modals: new Set(), slices: new Set(), sliceDom: new Set(), refs: new Map()};
  const isDom = /[\\/]src[\\/]domain[\\/]/.test(f);
  const addSlice = k => (isDom ? facts.sliceDom : facts.slices).add(k);
  nodeFacts.set(key, facts);
  const info = getInfo(f);
  const node = info.decls.get(name);
  if (!node) return facts;
  const bodyIds = new Set();
  const w = n => {
    if (ts.isPropertyAssignment(n) && ts.isIdentifier(n.name) && n.name.text === 'type') {
      const g = x => { if (ts.isStringLiteralLike(x) && /^[A-Z][A-Z0-9_]+$/.test(x.text) && cmdSet.has(x.text)) facts.cmds.add(x.text); ts.forEachChild(x, g); };
      g(n.initializer);
    }
    if (ts.isShorthandPropertyAssignment(n) && n.name.text === 'type') { /* dynamic type var; cannot resolve */ }
    if (ts.isCallExpression(n)) {
      const callee = n.expression; const nm = ts.isIdentifier(callee) ? callee.text : ts.isPropertyAccessExpression(callee) ? callee.name.text : '';
      if (/^open(Modal)?$/.test(nm) && n.arguments[0] && ts.isObjectLiteralExpression(n.arguments[0])) {
        for (const p of n.arguments[0].properties) if (ts.isPropertyAssignment(p) && ts.isIdentifier(p.name) && p.name.text === 'type') {
          const g = x => { if (ts.isStringLiteralLike(x) && /^[a-z][a-z0-9-]*$/.test(x.text)) facts.modals.add(x.text); ts.forEachChild(x, g); }; g(p.initializer);
        }
      }
      // exec-style helpers:  run('TYPE', ...)
      if (/^(run|exec|dispatch|send)$/.test(nm) && n.arguments[0] && ts.isStringLiteralLike(n.arguments[0]) && cmdSet.has(n.arguments[0].text)) facts.cmds.add(n.arguments[0].text);
    }
    if (ts.isPropertyAccessExpression(n) && ts.isIdentifier(n.expression) && /^(s|state|st|S)$/.test(n.expression.text) && stateKeySet.has(n.name.text)) addSlice(n.name.text);
    if (ts.isPropertyAccessExpression(n) && ts.isPropertyAccessExpression(n.expression) && n.expression.name.text === 's' && stateKeySet.has(n.name.text)) addSlice(n.name.text);
    // ctx.booking(id) opens the 'booking' modal: a call, or the function handed to a handler / argument
    if (ts.isIdentifier(n) && n.text === 'booking' && n.parent && ((ts.isCallExpression(n.parent) && n.parent.expression === n) || ts.isJsxExpression(n.parent) || (ts.isCallExpression(n.parent) && n.parent.arguments.includes(n)))) facts.modals.add('booking');
    if (ts.isPropertyAccessExpression(n) && n.name.text === 'booking' && n.parent && ts.isCallExpression(n.parent) && n.parent.expression === n) facts.modals.add('booking');
    if (ts.isIdentifier(n)) bodyIds.add(n.text);
    if (ts.isObjectBindingPattern(n)) { /* const {a,b}=s */
      const p = n.parent;
      if (ts.isVariableDeclaration(p) && p.initializer && ts.isIdentifier(p.initializer) && /^(s|state|st)$/.test(p.initializer.text))
        for (const e of n.elements) { const nm = (e.propertyName ?? e.name); if (ts.isIdentifier(nm) && stateKeySet.has(nm.text)) addSlice(nm.text); }
    }
    ts.forEachChild(n, w);
  };
  w(node);
  for (const id of bodyIds) {
    if (id === name) continue;
    const r = resolveSym(f, id);
    if (r && r.file !== undefined && (r.file !== f || r.name !== name)) facts.refs.set(symKey(r.file, r.name), r);
  }
  return facts;
}
function closure(f, name, modalResolver, skip = new Set(['booking']), extra = []) {
  const cmds = new Set(), slices = new Set(), sliceDom = new Set(), modalsSeen = new Set(), seen = new Set(), viaModal = new Set();
  const modalQueue = [];
  const go = (ff, nn, inModal, dd = 0) => {
    const isDom = /[\\/]src[\\/]domain[\\/]/.test(ff);
    const d2 = isDom ? dd + 1 : 0;
    if (isDom && d2 > 2) return;
    const k = symKey(ff, nn); if (seen.has(k)) return; seen.add(k);
    const fa = factsOf(ff, nn);
    if (!isDom) for (const c of fa.cmds) { cmds.add(c); if (inModal) viaModal.add(c); }
    for (const s of fa.slices) slices.add(s);
    for (const s of fa.sliceDom) sliceDom.add(s);
    if (!isDom) for (const m of fa.modals) if (!modalsSeen.has(m)) { modalsSeen.add(m); modalQueue.push(m); }
    for (const r of fa.refs.values()) go(r.file, r.name, inModal, d2);
  };
  go(f, name, false);
  for (const r of extra) go(r.file, r.name, false);
  while (modalQueue.length) {
    const m = modalQueue.shift(); if (skip.has(m)) continue; const rs = modalResolver(m);
    for (const r of rs) go(r.file, r.name, true);
  }
  // cmds seen first in modal only: viaModal contains commands from modal closures; direct ones are the rest
  return {cmds: [...cmds].sort(), slices: [...slices].sort(), sliceDom: [...sliceDom].filter(x => !slices.has(x)).sort(), modals: [...modalsSeen].sort(), viaModalOnly: [...viaModal].sort()};
}
// registrations: parse app/registry.ts imports -> register.tsx files
const regFile = path.join(SRC, 'app', 'registry.ts');
const regInfo = getInfo(regFile);
const regFiles = [];
for (const [local, imp] of regInfo.imports) if (/[\\/]register\.tsx?$/.test(imp.file)) regFiles.push({local, file: imp.file});
const desktopPages = {}, desktopModals = {}, mobilePagesMap = {}, mobileModalsMap = {}, navEntries = [], bookingActions = [];
const objProps = (obj, cb) => { for (const p of obj.properties) { let key = null; if (ts.isPropertyAssignment(p) || ts.isShorthandPropertyAssignment(p)) { key = ts.isIdentifier(p.name) || ts.isStringLiteral(p.name) ? p.name.text : null; } if (key) cb(key, p); } };
for (const rf of regFiles) {
  const info = getInfo(rf.file);
  const regDecl = info.decls.get('registration');
  const obj = regDecl?.initializer; if (!obj || !ts.isObjectLiteralExpression(obj)) continue;
  const compOf = (f, p) => {
    let id = ts.isShorthandPropertyAssignment(p) ? p.name.text : (ts.isIdentifier(p.initializer) ? p.initializer.text : null);
    if (!id && p.initializer) {   // inline arrow such as ()=><MyHr/>: use the first capitalised JSX tag
      const find = n => { if ((ts.isJsxSelfClosingElement(n) || ts.isJsxOpeningElement(n)) && ts.isIdentifier(n.tagName) && /^[A-Z]/.test(n.tagName.text)) return n.tagName.text; let r; ts.forEachChild(n, c => { r ??= find(c); }); return r; };
      id = find(p.initializer) ?? null;
    }
    return id ? {id, sym: resolveSym(f, id)} : {id: p.initializer.getText().slice(0, 40), sym: null};
  };
  const readMaps = (o, pages, modals) => {
    objProps(o, (k, p) => {
      if (k === 'pages' && ts.isPropertyAssignment(p) && ts.isObjectLiteralExpression(p.initializer)) objProps(p.initializer, (pk, pp) => { pages[pk] = {reg: rel(rf.file), ...compOf(rf.file, pp)}; });
      if (k === 'modals' && ts.isPropertyAssignment(p) && ts.isObjectLiteralExpression(p.initializer)) objProps(p.initializer, (pk, pp) => { modals[pk] = {reg: rel(rf.file), ...compOf(rf.file, pp)}; });
    });
  };
  readMaps(obj, desktopPages, desktopModals);
  objProps(obj, (k, p) => {
    if (k === 'mobile' && ts.isPropertyAssignment(p) && ts.isObjectLiteralExpression(p.initializer)) readMaps(p.initializer, mobilePagesMap, mobileModalsMap);
    if (k === 'bookingActions') bookingActions.push({reg: rel(rf.file), ...compOf(rf.file, p)});
    if (k === 'nav' && ts.isPropertyAssignment(p) && ts.isArrayLiteralExpression(p.initializer)) for (const el of p.initializer.elements) {
      const t = el.getText(); const id = t.match(/\bid\s*:\s*'([^']+)'/)?.[1], label = t.match(/\blabel\s*:\s*'([^']+)'/)?.[1], perm = t.match(/\bp\s*:\s*(\[[^\]]*\]|'[^']*')/)?.[1], grp = t.match(/group\s*:\s*'([^']+)'/)?.[1];
      navEntries.push({id, label, group: grp, p: perm, reg: rel(rf.file)});
    }
  });
}
// core pages / modals (App.tsx and ModalHost.tsx)
const appFile = path.join(SRC, 'app', 'App.tsx'), mhFile = path.join(SRC, 'app', 'ModalHost.tsx');
const corePageSyms = {home: 'ModulesHome', overview: 'Overview', rooms: 'RoomBoard', reservations: 'Reservations', folios: 'Reservations', housekeeping: 'Housekeeping', expenses: 'Expenses', closing: 'DayClosing', reports: 'Reports', access: 'AccessControl', settings: 'HotelSettings'};
const coreModalSyms = {room: 'RoomModal', new: 'NewReservation', cancel: 'CancelReservation', move: 'MoveStay', booking: 'BookingModal', payment: 'PaymentModal', charge: 'ChargeModal', discount: 'ChargeModal', checkout: 'CheckoutModal', invoice: 'InvoiceModal', expense: 'ExpenseModal', cash: 'CashModal'};
const corePages = {}, coreModals = {};
for (const [id, sym] of Object.entries(corePageSyms)) { const f = id === 'home' ? path.join(SRC, 'app', 'ModulesHome.tsx') : appFile; const r = id === 'home' ? resolveSym(f, 'ModulesHome') : resolveSym(appFile, sym); corePages[id] = {reg: 'App.tsx (core)', id: sym, sym: r}; }
for (const [id, sym] of Object.entries(coreModalSyms)) coreModals[id] = {reg: 'ModalHost.tsx (core)', id: sym, sym: resolveSym(mhFile, sym)};
// navigation: base nav groups from nav.ts text
const navTs = read(path.join(SRC, 'app', 'nav.ts'));
const baseNav = [...navTs.matchAll(/\{id:'([^']+)',label:'([^']+)',icon:\w+,p:(\[[^\]]*\]|'[^']*')\}/g)].map(m => ({id: m[1], label: m[2], p: m[3], reg: 'nav.ts (base)'}));
const navAll = [...baseNav, ...navEntries.filter(n => n.id)];
const navById = Object.fromEntries(navAll.map(n => [n.id, n]));

const modalResolverFor = mobile => id => {
  const out = [];
  const pick = (mobile && mobileModalsMap[id]) || desktopModals[id] || coreModals[id];
  if (pick?.sym) out.push({file: pick.sym.file, name: pick.sym.name});
  if (id === 'booking') for (const ba of bookingActions) if (ba.sym) out.push({file: ba.sym.file, name: ba.sym.name});
  return out;
};
function screenReport(id, mobile) {
  const m = mobile ? mobilePagesMap[id] : null;
  const d = desktopPages[id] ?? corePages[id];
  const pick = m ?? d;
  if (!pick?.sym) return {id, found: false, kind: mobile ? 'phone' : 'desktop', comp: pick?.id ?? null, reg: pick?.reg ?? null};
  const cl = closure(pick.sym.file, pick.sym.name, modalResolverFor(mobile));
  cl.usesBookingDialog = cl.modals.includes('booking');
  // The 'new' reservation dialog (desktop and phone wizard) books through the typed client (useReservations().commit -> QuoteStay +
  // CreateReservation), not through ctx.run; it is the BOOK command (sync/reservations.ts maps it and `track`s the result).
  if (cl.modals.includes('new') && !cl.cmds.includes('BOOK')) { cl.cmds = [...cl.cmds, 'BOOK'].sort(); cl.viaTypedClient = ['BOOK']; }
  return {id, kind: mobile ? 'phone' : 'desktop', found: true, comp: pick.sym.name, file: rel(pick.sym.file), reg: pick.reg, usesPhoneVariant: !!m, ...cl};
}
const desktopIds = [...new Set([...Object.keys(corePages), ...Object.keys(desktopPages)])].sort();
const phoneIds = Object.keys(mobilePagesMap).sort();
const screens = [];
for (const id of desktopIds) screens.push({...screenReport(id, false), nav: navById[id] ?? null});
for (const id of phoneIds) screens.push({...screenReport(id, true), nav: navById[id] ?? null});
// phone shell: pages with no phone variant fall back to the desktop page
const phoneFallbackIds = desktopIds.filter(id => !mobilePagesMap[id]);
// booking-dialog extras (bookingActions) -> commands attached to the 'booking' modal
const bookingDialog = {};
for (const mobile of [false, true]) {
  const rs = modalResolverFor(mobile)('booking');
  bookingDialog[mobile ? 'phone' : 'desktop'] = rs.length ? closure(rs[0].file, rs[0].name, modalResolverFor(mobile), new Set(), rs.slice(1)) : null;
}
const bookingActionCmds = {};
for (const ba of bookingActions) if (ba.sym) bookingActionCmds[ba.id] = closure(ba.sym.file, ba.sym.name, modalResolverFor(false)).cmds;
// slice classification
const loadedSlices = new Set(Object.keys(sliceOwners));
const emptyDump = B.emptyState();
const sliceKind = k => { const v = emptyDump[k]; return Array.isArray(v) ? 'list' : (v && typeof v === 'object') ? (/X$/.test(k) ? 'module' : 'object') : typeof v; };
// Dynamic dispatch the static walk cannot see: the Master configuration registry builds commands in domain/streams/config.ts
// (command:(s,v)=>({type:'X',…})) and the Approvals page runs type:r.command for the kinds the handlers queue (meta.command).
const cfgText = read(path.join(SRC, 'domain', 'streams', 'config.ts'));
const cfgDyn = [...new Set([...cfgText.matchAll(/\btype\s*:\s*["']([A-Z][A-Z0-9_]+)["']/g)].map(m => m[1]))].filter(x => cmdSet.has(x));
const appDynSrc = [...walk(path.join(SRC, 'services', 'mock', 'handlers'), ['.ts']), ...walk(path.join(SRC, 'domain', 'streams'), ['.ts'])];
const appDyn = [...new Set(appDynSrc.flatMap(f => [...read(f).matchAll(/\bcommand\s*:\s*["']([A-Z][A-Z0-9_]+)["']/g)].map(m => m[1])))].filter(x => cmdSet.has(x));
const dynamicSources = {settings: cfgDyn, approvals: appDyn};
for (const sc of screens) {
  if (!sc.found || !dynamicSources[sc.id]) continue;
  sc.dynamicCmds = dynamicSources[sc.id].filter(c => !sc.cmds.includes(c));
  sc.cmds = [...new Set([...sc.cmds, ...sc.dynamicCmds])].sort();
}
for (const sc of screens) {
  if (!sc.found) continue;
  sc.cmdStatus = Object.fromEntries(sc.cmds.map(c => [c, status[c]]));
  sc.mapped = sc.cmds.filter(c => status[c] === 'MAPPED').length;
  sc.unmapped = sc.cmds.filter(c => status[c] === 'UNMAPPED').length;
  sc.absent = sc.cmds.filter(c => status[c] === 'ABSENT').length;
  sc.slicesLoaded = sc.slices.filter(s => loadedSlices.has(s));
  sc.slicesEmpty = sc.slices.filter(s => !loadedSlices.has(s));
  sc.sliceDomLoaded = sc.sliceDom.filter(s => loadedSlices.has(s));
  sc.sliceDomEmpty = sc.sliceDom.filter(s => !loadedSlices.has(s));
  sc.sliceKinds = Object.fromEntries(sc.slices.map(s => [s, sliceKind(s)]));
}
// which screens use each command
const cmdScreens = {};
for (const sc of screens) if (sc.found) {
  const pre = sc.kind === 'phone' ? 'm:' : '';
  for (const c of sc.cmds) (cmdScreens[c] ||= []).push(pre + sc.id);
  if (sc.usesBookingDialog) for (const c of bookingDialog[sc.kind === 'phone' ? 'phone' : 'desktop']?.cmds ?? []) if (!sc.cmds.includes(c)) (cmdScreens[c] ||= []).push(pre + sc.id + ' (via booking dialog)');
}
// literal command types anywhere in UI sources that are not in the universe (dispatch of unknown commands)
const literalHits = {}; const unknownLiterals = {};
for (const f of uiFiles) {
  const t = read(f);
  for (const m of t.matchAll(/\btype\s*:\s*([^,}\n]{1,80})/g)) for (const q of m[1].matchAll(/'([A-Z][A-Z0-9_]{2,})'/g)) {
    if (cmdSet.has(q[1])) (literalHits[q[1]] ||= new Set()).add(rel(f)); else (unknownLiterals[q[1]] ||= new Set()).add(rel(f));
  }
}
const uiFileList = Object.fromEntries(Object.entries(literalHits).map(([k, v]) => [k, [...v].sort()]));
const unknownLit = Object.fromEntries(Object.entries(unknownLiterals).map(([k, v]) => [k, [...v].sort()]));
const notOnAnyScreen = universe.filter(c => !cmdScreens[c]);
// where else do the screen-less commands appear in prototype source (dynamic builders: config registry, approvals, closing, tests)?
const allSrc = walk(SRC, ['.ts', '.tsx']).filter(f => !/[\\/]clients[\\/]sync[\\/]/.test(f));
const notOnScreenRefs = {};
for (const c of notOnAnyScreen) {
  notOnScreenRefs[c] = allSrc.filter(f => read(f).includes("'" + c + "'")).map(rel).filter(x => !x.endsWith('src/services/mock/commands.ts'));
}
const testFiles = fs.readdirSync(path.join(PROTO, 'tests')).filter(f => f.endsWith('.mjs'));
const notOnScreenTests = {};
for (const c of notOnAnyScreen) notOnScreenTests[c] = testFiles.filter(f => read(path.join(PROTO, 'tests', f)).includes(c)).length;
save('screens.json', {dynamicSources, bookingDialog, notOnScreenRefs, notOnScreenTests, desktopIds, phoneIds, phoneFallbackIds, screens, bookingActionCmds, cmdScreens, uiFileList, unknownLit, notOnAnyScreen,
  desktopModals: Object.fromEntries(Object.entries({...coreModals, ...desktopModals}).map(([k, v]) => [k, v.sym ? rel(v.sym.file) + '#' + v.sym.name : null])),
  mobileModals: Object.fromEntries(Object.entries(mobileModalsMap).map(([k, v]) => [k, v.sym ? rel(v.sym.file) + '#' + v.sym.name : null])), nav: navAll});
console.log('screens: desktop', desktopIds.length, 'phone', phoneIds.length, '| commands on no screen:', notOnAnyScreen.length, '| unknown literal types in UI:', Object.keys(unknownLit).length);

// summary by status
const counts = {MAPPED: 0, UNMAPPED: 0, ABSENT: 0};
for (const t of universe) counts[status[t]]++;
console.log('status counts', counts);
save("state-keys.json", {stateKeys, kinds: Object.fromEntries(stateKeys.map(k => [k, sliceKind(k)])), loaded: [...loadedSlices], notLoaded: stateKeys.filter(k => !loadedSlices.has(k))});
save("summary.json", {nodeVersion: process.version, universe: universe.length, counts, servercommands: serverNames.size});
if (process.env.A02_DEBUG) { console.log("quoteStay in table:", fnTable.has("quoteStay"), "createHttp:", fnTable.has("createHttpReservationsClient")); const q = fnTable.get("quoteAndCreate"); console.log(q && q[0].getText().slice(0, 400)); fs.copyFileSync(bundlePath, process.env.A02_DEBUG); }
fs.rmSync(tmp, {recursive: true, force: true});
