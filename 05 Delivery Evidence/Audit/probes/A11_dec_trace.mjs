// A11 probe: for each DEC-/APR- id in the Decision Register (+ DEC-044..051 from the mobile plan), list where code/tests/migrations cite it.
// Citation in a comment is only a lead: the lead reads the cited line and judges whether it IMPLEMENTS the decision.
import fs from 'node:fs';
import path from 'node:path';

const ROOT = process.cwd();
const OUT = path.join(ROOT, '05 Delivery Evidence', 'Audit', 'evidence', 'A11');
fs.mkdirSync(OUT, { recursive: true });
const reg = fs.readFileSync(path.join(ROOT, '02 Planning', 'HCC_Decision_Register.md'), 'utf8') +
  fs.readFileSync(path.join(ROOT, '02 Planning', 'HCC_Mobile_Version_Plan_v1.md'), 'utf8');
const ids = [...new Set([...reg.matchAll(/\b(DEC-\d{2,3}|APR-\d{3})\b/g)].map(m => m[1]))];
// normalise DEC-05 / DEC-052 etc: keep both spellings as distinct ids because the register uses both
const SCAN = [
  ['proto-src', '03 Prototype/src'], ['proto-tests', '03 Prototype/tests'], ['proto-build', '03 Prototype/build.mjs'],
  ['srv-src', '08 Production/src'], ['srv-db', '08 Production/database'], ['srv-tests', '08 Production/tests'],
  ['srv-deploy', '08 Production/deployment'], ['android', '04 Android/android/app/src'], ['android-scripts', '04 Android/scripts'], ['android-readme', '04 Android/README.md'],
];
const SKIP = new Set(['node_modules', '.git', 'bin', 'obj', 'build', '.generated', 'dist', 'worktrees']);
const files = [];
function walk(d, tag) {
  const st = fs.statSync(d);
  if (st.isFile()) { files.push([tag, d]); return; }
  for (const e of fs.readdirSync(d, { withFileTypes: true })) {
    if (SKIP.has(e.name)) continue;
    const p = path.join(d, e.name);
    if (e.isDirectory()) walk(p, tag);
    else if (/\.(ts|tsx|mjs|cs|sql|ps1|xml|gradle|json|md|css|js|properties)$/.test(e.name) && !/\.(golden|lock)\.json$/.test(e.name) && !/package-lock/.test(e.name)) files.push([tag, p]);
  }
}
for (const [tag, rel] of SCAN) { const p = path.join(ROOT, rel); if (fs.existsSync(p)) walk(p, tag); }

const hits = Object.fromEntries(ids.map(i => [i, []]));
for (const [tag, f] of files) {
  const lines = fs.readFileSync(f, 'utf8').split(/\r?\n/);
  lines.forEach((ln, i) => {
    if (ln.length > 4000) return;
    for (const m of ln.matchAll(/\b(DEC-\d{2,3}|APR-\d{3})\b/g)) {
      if (hits[m[1]]) hits[m[1]].push({ tag, file: path.relative(ROOT, f).replace(/\\/g, '/'), line: i + 1 });
    }
  });
}
const rows = ids.sort().map(id => {
  const h = hits[id];
  const byTag = {};
  for (const x of h) byTag[x.tag] = (byTag[x.tag] || 0) + 1;
  // prefer non-test first, then tests
  const impl = h.filter(x => !/tests$|android-readme/.test(x.tag));
  const first = (impl.length ? impl : h).slice(0, 4).map(x => `${x.file}:${x.line}`);
  return { id, totalCites: h.length, byTag, sample: first };
});
fs.writeFileSync(path.join(OUT, 'dec_trace.json'), JSON.stringify(rows, null, 1));
const txt = rows.map(r => `${r.id}\t${r.totalCites}\t${JSON.stringify(r.byTag)}\t${r.sample.join(' ; ')}`).join('\n');
fs.writeFileSync(path.join(OUT, 'dec_trace.txt'), txt);
console.log(txt);
