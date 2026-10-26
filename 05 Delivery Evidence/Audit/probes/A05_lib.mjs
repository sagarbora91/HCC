// A05 shared helpers: C# lightweight scanner (strings/comments blanked), class/method index, route extraction, PERM derivation.
// Read-only. Pure Node built-ins. Run from anywhere; paths are absolute below.
import fs from 'node:fs';
import path from 'node:path';

export const ROOT = 'E:/Projects/HCC/Hotel Control Centre';
export const SRC = ROOT + '/08 Production/src';
export const MIG = ROOT + '/08 Production/database/migrations';

export function walk(dir, pred, out = []) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name === 'bin' || e.name === 'obj' || e.name === 'node_modules') continue;
    const p = dir + '/' + e.name;
    if (e.isDirectory()) walk(p, pred, out); else if (pred(p)) out.push(p);
  }
  return out;
}

/** Same length as src; string contents and comments replaced by spaces (newlines kept), quotes kept. */
export function blank(src) {
  const o = src.split(''); const n = src.length; let i = 0;
  const wipe = (a, b) => { for (let k = a; k < b; k++) if (o[k] !== '\n' && o[k] !== '\r') o[k] = ' '; };
  while (i < n) {
    const c = src[i], d = src[i + 1];
    if (c === '/' && d === '/') { let j = i; while (j < n && src[j] !== '\n') j++; wipe(i, j); i = j; continue; }
    if (c === '/' && d === '*') { let j = src.indexOf('*/', i + 2); if (j < 0) j = n; else j += 2; wipe(i, j); i = j; continue; }
    if (c === '"' && src.startsWith('"""', i)) { let j = src.indexOf('"""', i + 3); j = j < 0 ? n : j + 3; wipe(i + 3, j - 3); i = j; continue; }
    if ((c === '@' && d === '"') || (c === '$' && d === '@' && src[i + 2] === '"') || (c === '@' && d === '$' && src[i + 2] === '"')) {
      let j = src.indexOf('"', i) + 1; for (; j < n; j++) { if (src[j] === '"') { if (src[j + 1] === '"') { j++; continue; } break; } }
      wipe(src.indexOf('"', i) + 1, j); i = j + 1; continue;
    }
    if (c === '$' && d === '"') { let j = i + 2; for (; j < n; j++) { if (src[j] === '\\') { j++; continue; } if (src[j] === '"') break; } wipe(i + 2, j); i = j + 1; continue; }
    if (c === '"') { let j = i + 1; for (; j < n; j++) { if (src[j] === '\\') { j++; continue; } if (src[j] === '"') break; } wipe(i + 1, j); i = j + 1; continue; }
    if (c === "'") {
      const m = /^'(\\.|[^'\\])'/.exec(src.slice(i, i + 6)); if (m) { wipe(i + 1, i + m[0].length - 1); i += m[0].length; continue; }
    }
    i++;
  }
  return o.join('');
}

/** index of the matching close for the open bracket at position i in blanked text */
export function match(b, i) {
  const open = b[i], close = { '(': ')', '{': '}', '[': ']', '<': '>' }[open]; let d = 0;
  for (let k = i; k < b.length; k++) { if (b[k] === open) d++; else if (b[k] === close) { d--; if (d === 0) return k; } }
  return -1;
}

/** split at top-level commas; returns [start,end) pairs */
export function splitArgs(b, from, to) {
  const out = []; let d = 0, s = from;
  for (let k = from; k < to; k++) {
    const c = b[k];
    if (c === '(' || c === '{' || c === '[') d++; else if (c === ')' || c === '}' || c === ']') d--;
    else if (c === ',' && d === 0) { out.push([s, k]); s = k + 1; }
  }
  if (s < to) out.push([s, to]);
  return out;
}

export const CLASS_RE = /\b(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|file)\s+)*(class|record|interface|struct)\s+(?:struct\s+)?(\w+)/g;

export function indexSources() {
  const files = walk(SRC, p => p.endsWith('.cs') && !p.includes('/Hcc.Api/'));
  const consts = new Map();
  const constsBare = new Map();
  const methods = [];
  for (const f of files) {
    const orig = fs.readFileSync(f, 'utf8'); const b = blank(orig);
    const classes = []; let m; CLASS_RE.lastIndex = 0;
    while ((m = CLASS_RE.exec(b))) {
      let k = m.index + m[0].length; let depth = 0;
      for (; k < b.length; k++) { const c = b[k]; if (c === '(') depth++; else if (c === ')') depth--; else if (depth === 0 && (c === '{' || c === ';')) break; }
      if (b[k] === '{') { const e = match(b, k); classes.push({ kind: m[1], name: m[2], start: k, end: e }); }
    }
    const innermost = pos => { let best = null; for (const c of classes) if (pos > c.start && pos < c.end && (!best || c.start > best.start)) best = c; return best; };
    const cre = /\bconst\s+string\s+(\w+)\s*=\s*"([^"\n]*)"/g;
    while ((m = cre.exec(orig))) { const c = innermost(m.index); const cn = c ? c.name : '?'; consts.set(cn + '.' + m[1], m[2]); if (!constsBare.has(cn)) constsBare.set(cn, new Map()); constsBare.get(cn).set(m[1], m[2]); }
    const cc = /\bconst\s+string\s+(\w+)\s*=\s*(\w+)\s*\+\s*"([^"\n]*)"/g;
    while ((m = cc.exec(orig))) { const c = innermost(m.index); const cn = c ? c.name : '?'; const base = consts.get(cn + '.' + m[2]); if (base !== undefined) { consts.set(cn + '.' + m[1], base + m[3]); if (!constsBare.has(cn)) constsBare.set(cn, new Map()); constsBare.get(cn).set(m[1], base + m[3]); } }
    const lre = /\bstatic\s+readonly\s+[\w<>\[\]]+\s+(\w+)\s*=\s*\[([^\]]*)\]/g;
    while ((m = lre.exec(orig))) { const c = innermost(m.index); const cn = c ? c.name : '?'; const vals = m[2].split(',').map(s => s.trim()).map(n => consts.get(cn + '.' + n)).filter(v => v && v.startsWith('PERM-')); if (vals.length) { consts.set(cn + '.' + m[1], vals.join('|')); if (!constsBare.has(cn)) constsBare.set(cn, new Map()); constsBare.get(cn).set(m[1], vals.join('|')); } }
    const mre = /\b(?:public|internal|private|protected)\s+(?:(?:static|async|override|virtual|sealed|abstract|new|unsafe|extern|partial)\s+)*[\w<>\[\],\.\?\s\(\)]*?\b(\w+)\s*(?:<[^<>()]*>)?\s*\(/g;
    while ((m = mre.exec(b))) {
      const name = m[1]; if (['if', 'for', 'while', 'switch', 'foreach', 'using', 'lock', 'catch', 'return', 'nameof', 'typeof', 'new'].includes(name)) continue;
      const open = m.index + m[0].length - 1; const close = match(b, open); if (close < 0) continue;
      let k = close + 1; while (/\s/.test(b[k] ?? '')) k++;
      if (b.startsWith('where', k)) { while (k < b.length && b[k] !== '{' && !(b[k] === '=' && b[k + 1] === '>') && b[k] !== ';') k++; }
      let bodyStart, bodyEnd;
      if (b[k] === '{') { bodyStart = k; bodyEnd = match(b, k); }
      else if (b[k] === '=' && b[k + 1] === '>') { bodyStart = k + 2; let d = 0, e = bodyStart; for (; e < b.length; e++) { const c = b[e]; if (c === '(' || c === '{' || c === '[') d++; else if (c === ')' || c === '}' || c === ']') d--; else if (c === ';' && d === 0) break; } bodyEnd = e; }
      else continue;
      const cls = innermost(m.index); if (!cls || cls.kind === 'interface') continue;
      methods.push({ cls: cls.name, name, file: f, sig: orig.slice(m.index, close + 1), body: orig.slice(bodyStart, bodyEnd + 1), bodyB: b.slice(bodyStart, bodyEnd + 1), line: orig.slice(0, m.index).split('\n').length });
    }
  }
  const byName = new Map(), byClassName = new Map();
  for (const mt of methods) { (byName.get(mt.name) ?? byName.set(mt.name, []).get(mt.name)).push(mt); const k = mt.cls + '.' + mt.name; (byClassName.get(k) ?? byClassName.set(k, []).get(k)).push(mt); }
  const classNames = new Set(methods.map(m => m.cls));
  return { files, consts, constsBare, methods, byName, byClassName, classNames };
}

const NOISE = new Set(['if', 'for', 'while', 'switch', 'foreach', 'using', 'lock', 'catch', 'return', 'nameof', 'typeof', 'new', 'await', 'ConfigureAwait', 'ToString', 'Select', 'Where', 'Any', 'All', 'ToList', 'ToArray', 'Count', 'First', 'FirstOrDefault', 'Add', 'Contains', 'Equals', 'Max', 'Min', 'Sum', 'Length', 'ArgumentNullException', 'ThrowIfNull', 'Validate', 'Ok', 'Task', 'ValueTask', 'default', 'Create', 'From', 'Parse', 'TryParse', 'Format', 'Join', 'Concat', 'Skip', 'Take', 'OrderBy', 'ThenBy', 'OrderByDescending', 'GroupBy', 'ToDictionary', 'ToHashSet', 'Distinct', 'Single', 'SingleOrDefault', 'Last', 'LastOrDefault', 'Cast', 'Of', 'Run', 'Get', 'Set', 'Value']);

/** PERM ids reachable from method m (direct = in its own body; via = in called helpers). */
export function permsOf(ix, m, depth = 3, seen = new Set()) {
  const key = m.file + ':' + m.line; if (seen.has(key)) return { direct: new Set(), via: new Set() }; seen.add(key);
  const direct = new Set(), viaS = new Set();
  for (const x of m.body.matchAll(/"(PERM-[A-Z]+-\d{3}(?::VIEW)?)"/g)) direct.add(x[1]);
  for (const x of m.bodyB.matchAll(/\b([A-Z]\w+)\.([A-Z]\w*)\b/g)) { const v = ix.consts.get(x[1] + '.' + x[2]); if (v && v.startsWith('PERM-')) for (const p of v.split('|')) direct.add(p); }
  const bare = ix.constsBare.get(m.cls);
  if (bare) for (const x of m.bodyB.matchAll(/(?<![\.\w])([A-Z]\w*)\b/g)) { const v = bare.get(x[1]); if (v && v.startsWith('PERM-')) for (const p of v.split('|')) direct.add(p); }
  if (depth > 0) {
    const calls = new Set();
    for (const x of m.bodyB.matchAll(/(?<![\.\w])([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(/g)) if (!NOISE.has(x[1])) calls.add(m.cls + '.' + x[1]);
    for (const x of m.bodyB.matchAll(/\bthis\s*\.\s*([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(/g)) calls.add(m.cls + '.' + x[1]);
    for (const x of m.bodyB.matchAll(/\b([A-Z]\w+)\s*\.\s*([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(/g)) if (ix.classNames.has(x[1]) && !NOISE.has(x[2])) calls.add(x[1] + '.' + x[2]);
    for (const c of calls) for (const h of ix.byClassName.get(c) ?? []) {
      if (h === m) continue;
      const r = permsOf(ix, h, depth - 1, seen);
      for (const p of r.direct) viaS.add(p); for (const p of r.via) viaS.add(p);
    }
  }
  return { direct, via: viaS };
}

/** Every PERM occurrence in method m (and helpers up to depth) with the enclosing call and its scope argument. */
export function permUses(ix, m, depth = 3, seen = new Set(), tag = 'direct') {
  const key = m.file + ':' + m.line; if (seen.has(key)) return []; seen.add(key);
  const out = [];
  const toks = [];
  for (const x of m.body.matchAll(/"(PERM-[A-Z]+-\d{3}(?::VIEW)?)"/g)) toks.push({ pos: x.index, perms: [x[1]] });
  for (const x of m.bodyB.matchAll(/\b([A-Z]\w+)\.([A-Z]\w*)\b/g)) { const v = ix.consts.get(x[1] + '.' + x[2]); if (v && v.startsWith('PERM-')) toks.push({ pos: x.index, perms: v.split('|') }); }
  const bare = ix.constsBare.get(m.cls);
  if (bare) for (const x of m.bodyB.matchAll(/(?<![\.\w])([A-Z]\w*)\b/g)) { const v = bare.get(x[1]); if (v && v.startsWith('PERM-')) toks.push({ pos: x.index, perms: v.split('|') }); }
  for (const t of toks) {
    // enclosing calls, innermost first
    let d = 0, fn = 'expr', args = '', found = false; const b = m.bodyB;
    for (let k = t.pos - 1; k >= 0; k--) {
      const c = b[k];
      if (c === ')' || c === ']' || c === '}') d++;
      else if (c === '(' || c === '[' || c === '{') {
        if (d > 0) { d--; continue; }
        if (c !== '(') continue;
        const id = /([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*$/.exec(b.slice(Math.max(0, k - 60), k));
        const name = id ? id[1] : '?';
        const end = match(b, k); const text = end > 0 ? b.slice(k, end + 1) : '';
        if (/(Require|Has|Evaluate|Gate|Authori[sz]e|Ensure|Can|Check|Guard|Allowed)/.test(name) || !found) { fn = name; args = text; found = true; if (/(Require|Has|Evaluate|Gate|Authori[sz]e|Ensure|Can|Check|Guard|Allowed)/.test(name)) break; }
      }
    }
    const scope = (/AccessScopes\.Any/.test(args) || /^(AnyAsync|foreach)$/.test(fn)) ? 'any' : (/AccessScopes\.Property/.test(args) || /Wide/.test(fn)) ? 'property' : /new\s+AccessScope\b|\bscope\b|AccessScopes\.(?!Any\b|Property\b)\w+\(/.test(args) ? 'record' : 'none';
    const amount = /amountPaise\s*:|\bamount\b/i.test(args);
    for (const p of t.perms) out.push({ perm: p, fn, scope, amount, at: m.cls + '.' + m.name, line: m.line, tag });
  }
  if (depth > 0) {
    const calls = new Set();
    for (const x of m.bodyB.matchAll(/(?<![\.\w])([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(/g)) if (!NOISE.has(x[1])) calls.add(m.cls + '.' + x[1]);
    for (const x of m.bodyB.matchAll(/\bthis\s*\.\s*([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(/g)) calls.add(m.cls + '.' + x[1]);
    for (const x of m.bodyB.matchAll(/\b([A-Z]\w+)\s*\.\s*([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(/g)) if (ix.classNames.has(x[1]) && !NOISE.has(x[2])) calls.add(x[1] + '.' + x[2]);
    for (const c of calls) for (const h of ix.byClassName.get(c) ?? []) { if (h !== m) out.push(...permUses(ix, h, depth - 1, seen, 'via')); }
  }
  return out;
}

export function usesPolicy(m) { return /\bpolicy\b|RequireAsync|HasAsync|IAccessEvaluator|EvaluateAsync|GateAsync|Require\w*\(/.test(m.bodyB); }

/** Extract routes from one Api file. */
export function extractRoutes(ix, file) {
  const orig = fs.readFileSync(file, 'utf8'); const b = blank(orig); const out = [];
  const lineOf = pos => orig.slice(0, pos).split('\n').length;
  const groupDefs = [];
  const gre = /\bvar\s+(\w+)\s*=\s*(\w+)\s*\.\s*(MapGroup|MapHccSecured)\s*\(/g; let m;
  while ((m = gre.exec(b))) {
    const open = m.index + m[0].length - 1; const close = match(b, open); const argsText = orig.slice(open + 1, close);
    const tail = b.slice(close + 1, close + 40);
    const lit = /^\s*"([^"]*)"/.exec(argsText);
    const prefix = m[3] === 'MapHccSecured' ? '' : (lit ? lit[1] : '');
    const secured = m[3] === 'MapHccSecured' || /^\s*\.\s*RequireHccSession\s*\(\s*\)/.test(tail);
    groupDefs.push({ pos: m.index, name: m[1], parent: m[2], prefix, secured });
  }
  const stateAt = (name, pos) => {
    let d = null; for (const g of groupDefs) if (g.name === name && g.pos < pos) d = g;
    if (!d) return (name === 'api' || name === 'app') ? { prefix: '', secured: false } : null;
    const par = stateAt(d.parent, d.pos) ?? { prefix: '', secured: false };
    return { prefix: par.prefix + d.prefix, secured: par.secured || d.secured };
  };
  const evalPath = (text) => {
    let res = '', ok = true;
    for (const part of text.split('+').map(s => s.trim())) {
      const l = /^"([^"]*)"$/.exec(part);
      if (l) { res += l[1]; continue; }
      const q = /^([A-Z]\w+)\.(\w+)$/.exec(part);
      if (q && ix.consts.has(q[1] + '.' + q[2])) { res += ix.consts.get(q[1] + '.' + q[2]); continue; }
      ok = false; res += '{' + part + '}';
    }
    return { path: res, ok };
  };
  const helperTypes = {}; const localFns = {};
  for (const x of orig.matchAll(/static\s+([A-Z]\w+)\s+(\w+)\s*\(/g)) localFns[x[2]] = x[1];
  for (const x of orig.matchAll(/static\s+void\s+(Command|Cmd)\s*<[^()]*>\s*\(\s*RouteGroupBuilder\s+\w+\s*,[^;{]*?Func<\s*(\w+)\s*,/gs)) helperTypes[x[1]] = x[2];
  const mre = /\b(\w+)\s*\.\s*Map(Get|Post)\s*\(/g;
  while ((m = mre.exec(b))) {
    const open = m.index + m[0].length - 1; const close = match(b, open);
    const args = splitArgs(b, open + 1, close); if (!args.length) continue;
    const pathText = orig.slice(args[0][0], args[0][1]).trim();
    const { path: p, ok } = evalPath(pathText);
    const handler = orig.slice(args[0][1] + 1, close); const handlerB = b.slice(args[0][1] + 1, close);
    const st = stateAt(m[1], m.index);
    out.push({ method: m[2].toUpperCase(), rawPath: pathText, path: p, pathOk: ok, group: m[1], prefix: st?.prefix ?? '?', secured: st?.secured ?? false, file: path.basename(file), line: lineOf(m.index), handler, handlerB, helper: null, ctx: { helperTypes, localFns } });
  }
  const cre = /\b(?:[A-Za-z_]\w*\.)?(Command|Cmd)\s*<([^()]*?)>\s*\(/g;
  while ((m = cre.exec(b))) {
    const open = m.index + m[0].length - 1; const close = match(b, open);
    const args = splitArgs(b, open + 1, close); if (args.length < 3) continue;
    const a0 = orig.slice(args[0][0], args[0][1]).trim(); if (/\s/.test(a0)) continue;
    const nameText = orig.slice(args[1][0], args[1][1]).trim();
    const { path: nm, ok } = evalPath(nameText);
    const st = stateAt(a0, m.index);
    const handler = orig.slice(args[2][0], close); const handlerB = b.slice(args[2][0], close);
    const isAccess = path.basename(file) === 'AccessEndpoints.cs';
    out.push({ method: 'POST', rawPath: nameText, path: isAccess ? '/' + nm : '/commands/' + nm, pathOk: ok, group: a0, prefix: st?.prefix ?? '?', secured: st?.secured ?? false, file: path.basename(file), line: lineOf(m.index), handler, handlerB, helper: m[1], ctx: { helperTypes, localFns } });
  }
  return out;
}

const NOCALL = new Set(['HccActor', 'HccCommand', 'HccReply', 'HccCaller', 'PosCaller', 'GetRequiredService', 'Ok', 'ConfigureAwait', 'ThrowIfNull', 'IdempotencyKey', 'GetHccSession', 'Json', 'File', 'NoContent', 'Select', 'ToArray', 'ToList', 'Validation']);

/** Which indexed methods does this route handler call? */
export function handlerTargets(ix, r) {
  const hints = {};
  const head = /^\s*(?:async\s*)?\(([^)]*)\)\s*=>/.exec(r.handlerB) ?? /^\s*(?:async\s*)?(\w+)\s*=>/.exec(r.handlerB);
  if (head) for (const x of head[1].matchAll(/\b([A-Z][\w<>\?]*)\s+(\w+)/g)) hints[x[2]] = x[1];
  const found = [];
  for (const x of r.handlerB.matchAll(/\b([A-Za-z_]\w*)\s*\.\s*([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(/g)) {
    if (NOCALL.has(x[2]) || /^(Results|Task|Math|Convert|string|String|Guid|DateOnly|DateTime|TimeSpan|Enumerable|Array|Encoding)$/.test(x[1])) continue;
    found.push({ recv: x[1], type: hints[x[1]] ?? null, name: x[2] });
  }
  for (const x of r.handlerB.matchAll(/\b([A-Za-z_]\w*)\s*(?:<\s*(\w+)\s*>)?\s*\(([^()]*)\)\s*\.\s*([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(/g)) {
    if (x[1] === 'GetRequiredService') { found.push({ recv: 'sp', type: x[2], name: x[4] }); continue; }
    if (NOCALL.has(x[4]) || x[4] === 'ConfigureAwait') continue;
    if (r.ctx.localFns && x[1] in r.ctx.localFns) found.push({ recv: '()', type: r.ctx.localFns[x[1]], name: x[4] });
  }
  const ht = r.helper ? r.ctx.helperTypes[r.helper] : null;
  const targets = [];
  for (const f of found) {
    let cands = ix.byName.get(f.name) ?? [];
    const type = f.type ?? (f.recv.length <= 2 ? ht : null);
    if (type) { const core = type.replace(/^I(?=[A-Z])/, '').replace(/\?$/, ''); const flt = cands.filter(c => c.cls === type || c.cls.includes(core)); if (flt.length) cands = flt; }
    if (cands.length) targets.push({ call: f, cands, typed: !!type });
  }
  return targets;
}
