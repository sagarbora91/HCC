// A01: check that file/dir paths named in backticks in HANDOFF.md (Sessions 10-12b) and CLAUDE.md exist.
// Run from the repo root:  node "05 Delivery Evidence/Audit/probes/A01_paths.mjs"
import { readFileSync, existsSync } from 'node:fs';
import { join } from 'node:path';
import { execSync } from 'node:child_process';

const root = process.cwd();
const sources = [
  ['HANDOFF.md', 211, 344],
  ['CLAUDE.md', 1, 9999],
];
const BRANCHES = new Set(execSync('git branch --format=%(refname:short)',{cwd:root}).toString().split(String.fromCharCode(10)).map((x)=>x.trim()).filter(Boolean));
const rows = [];
for (const [file, from, to] of sources) {
  const lines = readFileSync(join(root, file), 'utf8').split(/\r?\n/);
  lines.forEach((line, i) => {
    const n = i + 1;
    if (n < from || n > to) return;
    for (const m of line.matchAll(/`([^`]+)`/g)) {
      let t = m[1].trim();
      // only things that look like paths with an extension or a known folder
      if (!/[\\/]/.test(t) && !/\.(md|ts|tsx|mjs|cs|sql|ps1|json|apk|html|csproj|slnx|gradle|png)$/i.test(t)) continue;
      if (/\s{2,}|^\w+:\/\/|^http|^npm |^dotnet |^node |^git |^bash |^sqlcmd|^\$env|^-/.test(t)) continue;
      if (/^[A-Za-z]:\\/.test(t) && !/^E:\\Projects\\HCC\\Hotel Control Centre/i.test(t)) { rows.push([file, n, t, 'EXTERNAL (not checked)']); continue; }
      t = t.replace(/^E:\\Projects\\HCC\\Hotel Control Centre\\/i, '').replace(/^V:\\Codex\\Hotel Control Centre\\/i, '');
      t = t.replace(/\\/g, '/').replace(/[,.;:)]+$/, '');
      if (/[*…<>{}|]/.test(t) || /\.\.\./.test(t)) { rows.push([file, n, m[1], 'PATTERN (not checked)']); continue; }
      const br = BRANCHES;
      if (br.has(t)) { rows.push([file, n, m[1], 'EXISTS (git branch)']); continue; }
      const candidates = [t, t.replace(/^src\//, '03 Prototype/src/'), '03 Prototype/' + t, '08 Production/' + t, '05 Delivery Evidence/' + t, '02 Planning/' + t, '04 Android/' + t,
        '03 Prototype/src/' + t, '03 Prototype/src/clients/' + t, '03 Prototype/src/app/' + t, '03 Prototype/src/fixtures/history/' + t, '03 Prototype/src/features/' + t, '08 Production/deployment/windows/' + t, '05 Delivery Evidence/Acceptance/' + t, '05 Delivery Evidence/Assembly/' + t, '02 Planning/agents/' + t];
      const hit = candidates.find((c) => existsSync(join(root, c)));
      rows.push([file, n, m[1], hit ? 'EXISTS ' + (hit === t ? '' : '(as ' + hit + ')') : 'MISSING']);
    }
  });
}
const missing = rows.filter((r) => r[3].startsWith('MISSING'));
console.log(`checked=${rows.length} exists=${rows.filter((r) => r[3].startsWith('EXISTS')).length} missing=${missing.length} other=${rows.length - rows.filter((r) => r[3].startsWith('EXISTS')).length - missing.length}`);
for (const r of rows) console.log(r.join(' | '));
