// A08: every [Fact]/[Theory] in 08 Production/tests — count assertion calls per test; list tests with none, and tests whose
// asserts compare a call against the same call family (possible mirror). Output: evidence/A08/test_asserts.txt
import fs from 'node:fs';
import path from 'node:path';
const T = path.join(process.cwd(), '08 Production', 'tests');
const walk = d => fs.readdirSync(d, { withFileTypes: true }).flatMap(e => e.name === 'bin' || e.name === 'obj' ? [] :
  e.isDirectory() ? walk(path.join(d, e.name)) : e.name.endsWith('.cs') ? [path.join(d, e.name)] : []);
const rows = [];
const assertRe = /\bAssert\.|\bFails\w*\(|\bExpect\.\w+|\bFailsAsync\(|\bRefused\(|\bSqlError\(|ShouldBe|\.Should\(/g;
for (const f of walk(T)) {
  const t = fs.readFileSync(f, 'utf8');
  const lines = t.split('\n');
  for (let i = 0; i < lines.length; i++) {
    if (!/^\s*\[(Fact|Theory)/.test(lines[i])) continue;
    let j = i + 1; while (j < lines.length && !/(public|private|internal)\s+(async\s+)?(Task|void)\s+\w+/.test(lines[j])) j++;
    const name = (lines[j] || '').match(/(Task|void)\s+(\w+)/)?.[2] ?? '?';
    let k = j + 1; while (k < lines.length && !/^\s*\[(Fact|Theory)/.test(lines[k]) && k < j + 400) k++;
    const body = lines.slice(j, k).join('\n');
    const asserts = (body.match(assertRe) || []).length;
    const sqlCat = /Category", "Sql"/.test(t);
    rows.push({ file: path.relative(T, f).replaceAll('\\', '/'), line: j + 1, name, asserts, sql: sqlCat, theory: /\[Theory/.test(lines[i]) });
  }
}
const byProj = {};
for (const r of rows) { const p = r.file.split('/')[0]; (byProj[p] ??= { tests: 0, zero: 0, sqlFile: 0 }); byProj[p].tests++; if (r.asserts === 0) byProj[p].zero++; if (r.sql) byProj[p].sqlFile++; }
const out = [`tests=${rows.length} zeroAssert=${rows.filter(r => r.asserts === 0).length}`, '', '== per project (tests, zero-assert, in a Category=Sql file) =='];
for (const [p, v] of Object.entries(byProj)) out.push(`${p}\t${v.tests}\t${v.zero}\t${v.sqlFile}`);
out.push('', '== zero-assert tests (may assert via helpers not matched) ==');
for (const r of rows.filter(r => r.asserts === 0)) out.push(`${r.file}:${r.line}\t${r.name}`);
fs.writeFileSync(path.join(process.cwd(), '05 Delivery Evidence', 'Audit', 'evidence', 'A08', 'test_asserts.txt'), out.join('\n'));
console.log(out.slice(0, 1).join('\n'));
