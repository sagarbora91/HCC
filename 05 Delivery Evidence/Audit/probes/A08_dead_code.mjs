// A08 dead-code sweep: public types (record/class/interface/enum) declared in 08 Production/src whose name appears nowhere else in
// src (outside the declaring file). Second column: whether tests reference them. Output: evidence/A08/dead_code.txt
import fs from 'node:fs';
import path from 'node:path';
const SRC = path.join(process.cwd(), '08 Production', 'src');
const TESTS = path.join(process.cwd(), '08 Production', 'tests');
const walk = d => fs.readdirSync(d, { withFileTypes: true }).flatMap(e => e.name === 'bin' || e.name === 'obj' ? [] :
  e.isDirectory() ? walk(path.join(d, e.name)) : e.name.endsWith('.cs') ? [path.join(d, e.name)] : []);
const src = walk(SRC).map(f => ({ f, t: fs.readFileSync(f, 'utf8') }));
const tests = walk(TESTS).map(f => fs.readFileSync(f, 'utf8')).join('\n');
const decls = [];
for (const { f, t } of src)
  for (const m of t.matchAll(/^\s*public\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+)*(record|class|interface|enum|struct)\s+(?:struct\s+)?(\w+)/gm))
    decls.push({ kind: m[1], name: m[2], file: path.relative(process.cwd(), f).replaceAll('\\', '/'), line: t.slice(0, m.index).split('\n').length });
const rows = [];
for (const d of decls) {
  const re = new RegExp(`\\b${d.name}\\b`, 'g');
  let uses = 0;
  for (const { f, t } of src) { const n = (t.match(re) || []).length; uses += path.relative(process.cwd(), f).replaceAll('\\', '/') === d.file ? Math.max(0, n - 1) : n; }
  if (uses === 0) rows.push({ ...d, inTests: re.test(tests) });
}
// Endpoints/handlers: Contracts types whose only use is the declaring file are wire shapes nobody maps
const out = [`declared public types: ${decls.length}; unreferenced elsewhere in src: ${rows.length}`, 'kind\tname\tfile:line\treferenced by tests'];
for (const r of rows) out.push(`${r.kind}\t${r.name}\t${r.file}:${r.line}\t${r.inTests}`);
fs.writeFileSync(path.join(process.cwd(), '05 Delivery Evidence', 'Audit', 'evidence', 'A08', 'dead_code.txt'), out.join('\n'));
console.log(out.join('\n'));
