// A07 — ID-shaped value sweep over the repository (tracked text files) and the whole 05 Delivery Evidence folder (incl. untracked).
// Looks for Aadhaar-shaped 12-digit numbers (classified with the Verhoeff checksum and the first digit), PAN, passport and driving
// licence patterns. Never prints a full value: every sample is masked to its last 4. Run from the repo root with Node 25:
//   $env:PATH="E:\Tools\nodejs;$env:PATH"; node "05 Delivery Evidence/Audit/probes/A07_id_sweep.mjs"
// Output: 05 Delivery Evidence/Audit/evidence/A07/id_sweep.txt and id_sweep.json
import {execSync} from 'node:child_process';
import {readFileSync, writeFileSync, statSync, readdirSync, mkdirSync} from 'node:fs';
import {join, extname, relative} from 'node:path';

const root = process.cwd();
const out = join(root, '05 Delivery Evidence', 'Audit', 'evidence', 'A07');
mkdirSync(out, {recursive: true});
const TEXT = new Set(['.md', '.txt', '.ts', '.tsx', '.mjs', '.cjs', '.js', '.cs', '.sql', '.json', '.ps1', '.html', '.csv', '.xml', '.yml', '.yaml', '.css', '.gradle', '.properties', '.kt', '.java', '.props', '.targets', '.csproj', '.slnx']);
const SKIP = /(^|[\\/])(node_modules|bin|obj|\.git|\.gradle|build)([\\/]|$)/;
const MAX = 60 * 1024 * 1024;

// Verhoeff (same tables as GuestIdRules.cs / guests.ts)
const D = [[0,1,2,3,4,5,6,7,8,9],[1,2,3,4,0,6,7,8,9,5],[2,3,4,0,1,7,8,9,5,6],[3,4,0,1,2,8,9,5,6,7],[4,0,1,2,3,9,5,6,7,8],[5,9,8,7,6,0,4,3,2,1],[6,5,9,8,7,1,0,4,3,2],[7,6,5,9,8,2,1,0,4,3],[8,7,6,5,9,3,2,1,0,4],[9,8,7,6,5,4,3,2,1,0]];
const P = [[0,1,2,3,4,5,6,7,8,9],[1,5,7,6,2,8,3,0,9,4],[5,8,0,3,7,9,6,1,4,2],[8,9,1,6,0,4,3,5,2,7],[9,4,5,3,1,2,6,8,7,0],[4,2,8,6,5,7,3,9,0,1],[2,7,9,3,8,0,6,4,1,5],[7,0,4,6,9,1,3,2,5,8]];
const verhoeff = s => { let c = 0; for (let i = 0; i < s.length; i++) c = D[c][P[i % 8][+s[s.length - 1 - i]]]; return c === 0; };
const mask = v => '*'.repeat(Math.max(0, v.length - 4)) + v.slice(-4);

const patterns = {
  aadhaar12: /(?<![0-9A-Za-z])(\d{4})[ -]?(\d{4})[ -]?(\d{4})(?![0-9A-Za-z])/g,
  pan: /(?<![A-Z0-9])[A-Z]{5}[0-9]{4}[A-Z](?![A-Z0-9])/g,
  passport: /(?<![A-Z0-9])[A-Z][0-9]{7}(?![A-Z0-9])/g,
  licence: /(?<![A-Z0-9])[A-Z]{2}[0-9]{13}(?![A-Z0-9])/g,
};

function classify(kind, raw) {
  if (kind === 'aadhaar12') {
    const d = raw.replace(/[ -]/g, '');
    if (!verhoeff(d)) return 'not-aadhaar (checksum fails: phone/timestamp/other)';
    if (d[0] === '0' || d[0] === '1') return 'fictional-pattern (starts 0/1, valid checksum)';
    return 'REAL-LOOKING (starts 2-9, valid checksum)';
  }
  if (kind === 'pan') return raw.startsWith('ZZZZZ') ? 'fictional-pattern (ZZZZZ…)' : /^[A-Z]{3}[ABCFGHLJPTK][A-Z]/.test(raw) ? 'REAL-LOOKING (4th letter is a PAN holder type)' : 'pattern-only (4th letter not a PAN holder type)';
  if (kind === 'passport') return raw.startsWith('Z') ? 'fictional-pattern (Z…)' : 'pattern-only (1 letter + 7 digits: also matches many codes)';
  if (kind === 'licence') return raw.startsWith('ZZ') ? 'fictional-pattern (ZZ…)' : /^(MH|DL|KA|TN|GJ|UP|MP|RJ|AP|TS|KL|WB|HR|PB|BR|OR|OD|CG|JH|UK|HP|GA|AS)/.test(raw) ? 'REAL-LOOKING (state code + 13 digits)' : 'pattern-only';
  return 'pattern-only';
}

function* files() {
  const tracked = execSync('git ls-files -z', {cwd: root, maxBuffer: 1 << 28}).toString('utf8').split('\0').filter(Boolean);
  const seen = new Set();
  for (const f of tracked) { seen.add(f); yield {path: f, tracked: true}; }
  const walk = dir => { for (const e of readdirSync(dir, {withFileTypes: true})) { const p = join(dir, e.name); if (SKIP.test(p)) continue; if (e.isDirectory()) walk(p); else { const rel = relative(root, p).replace(/\\/g, '/'); if (!seen.has(rel)) { seen.add(rel); yield_.push({path: rel, tracked: false}); } } } };
  const yield_ = [];
  walk(join(root, '05 Delivery Evidence'));
  for (const x of yield_) yield x;
}

const counts = {}, byFile = {}, samples = {};
let scanned = 0, skipped = 0;
for (const {path, tracked} of files()) {
  if (SKIP.test(path) || !TEXT.has(extname(path).toLowerCase()) || /package-lock\.json$|packages\.lock\.json$/.test(path)) { skipped++; continue; }
  let text; try { if (statSync(join(root, path)).size > MAX) { skipped++; continue; } text = readFileSync(join(root, path), 'utf8'); } catch { skipped++; continue; }
  scanned++;
  for (const [kind, re] of Object.entries(patterns)) {
    re.lastIndex = 0; let m;
    while ((m = re.exec(text))) {
      const raw = m[0], cls = classify(kind, raw);
      const key = kind + ' | ' + cls;
      counts[key] = (counts[key] || 0) + 1;
      (byFile[key] ||= {})[path] = ((byFile[key] ||= {})[path] || 0) + 1;
      const s = (samples[key] ||= new Map());
      const masked = mask(raw.replace(/[ -]/g, ''));
      if (s.size < 12 && !s.has(masked)) s.set(masked, path + (tracked ? '' : ' (untracked)'));
    }
  }
}

const lines = [`A07 ID-shaped value sweep — ${new Date().toISOString()} — node ${process.version}`, `Files scanned: ${scanned}; skipped (binary/lock/too large/excluded): ${skipped}`, ''];
for (const key of Object.keys(counts).sort()) {
  lines.push(`== ${key}: ${counts[key]} match(es) in ${Object.keys(byFile[key]).length} file(s)`);
  const top = Object.entries(byFile[key]).sort((a, b) => b[1] - a[1]).slice(0, 15);
  for (const [f, n] of top) lines.push(`   ${String(n).padStart(5)}  ${f}`);
  lines.push('   samples (masked to last 4):');
  for (const [m, f] of samples[key]) lines.push(`      ${m}  ← ${f}`);
  lines.push('');
}
writeFileSync(join(out, 'id_sweep.txt'), lines.join('\n') + '\n');
writeFileSync(join(out, 'id_sweep.json'), JSON.stringify({generated: new Date().toISOString(), node: process.version, scanned, skipped, counts,
  topFiles: Object.fromEntries(Object.entries(byFile).map(([k, v]) => [k, Object.entries(v).sort((a, b) => b[1] - a[1]).slice(0, 25)])),
  samples: Object.fromEntries(Object.entries(samples).map(([k, v]) => [k, [...v.entries()]]))}, null, 2));
console.log(lines.join('\n'));
