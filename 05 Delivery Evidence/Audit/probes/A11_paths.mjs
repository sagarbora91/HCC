// A11 probe: every backtick-quoted file path named in the audited documents -> does it exist?
// Run from repo root:  $env:PATH="E:\Tools\nodejs;$env:PATH"; node "05 Delivery Evidence/Audit/probes/A11_paths.mjs"
// Read-only. Writes 05 Delivery Evidence/Audit/evidence/A11/paths.json and paths.txt
import fs from 'node:fs';
import path from 'node:path';

const ROOT = process.cwd();
const OUT = path.join(ROOT, '05 Delivery Evidence', 'Audit', 'evidence', 'A11');
fs.mkdirSync(OUT, { recursive: true });

const DOCS = [
  'HANDOFF.md', 'CLAUDE.md', 'MIGRATION.md',
  '08 Production/README.md', '03 Prototype/README.md', '03 Prototype/src/clients/sync/README.md',
  '04 Android/README.md', '08 Production/deployment/windows/INSTALL.md',
  '02 Planning/HCC_Decision_Register.md', '02 Planning/HCC_Implementation_Backlog.md',
  '02 Planning/RBAC_Mismatch_Rulings_Proposed.md',
];

// roots a bare / relative path may be resolved against (docs are written loosely)
const ROOTS = [
  '', '03 Prototype', '08 Production', '04 Android', '02 Planning', '05 Delivery Evidence',
  '03 Prototype/src', '03 Prototype/src/clients/sync', '03 Prototype/tests', '08 Production/src', '08 Production/tests',
  '08 Production/deployment/windows', '08 Production/database/migrations', '04 Android/android/app', '04 Android/scripts',
  '02 Planning/agents', '02 Planning/modules', '02 Planning/options', '05 Delivery Evidence/Acceptance',
  '05 Delivery Evidence/Assembly', '05 Delivery Evidence/APK', '05 Delivery Evidence/Security', '07 Integration Lab',
  '08 Production/src/Hcc.Api', '08 Production/src/Hcc.Application', '08 Production/src/Hcc.Domain',
  '08 Production/src/Hcc.Application/GuestIds', '08 Production/src/Hcc.Application/Tax',
];

function looksLikePath(t) {
  if (/^https?:\/\//i.test(t)) return false;
  if (/[*<>{}$|]/.test(t)) return false;           // wildcards / placeholders / pipes -> handled separately
  if (/^(npm|npx|node|dotnet|git|sqlcmd|cd|powershell|\.\\[A-Za-z-]+\.ps1 )/i.test(t)) return false; // commands
  const hasSep = /[\\/]/.test(t);
  const ext = /\.(ts|tsx|mjs|cs|ps1|md|json|sql|xml|gradle|props|slnx|html|apk|csv|txt|png|jks|pem|cer|pfx|properties|js|css|yml|yaml|tgz|bundle|exe)$/i.test(t);
  const dir = /[\\/]$/.test(t);
  if (/\s/.test(t) && !/^([A-Za-z]:\\|\d\d [A-Z]|\.\.?[\\/]|Hcc\.|src[\\/]|tests?[\\/])/.test(t)) return false;
  const dirLike = hasSep && /^(src|tests?|database|deployment|tools|third-party|scripts|android|dd [A-Z]|Hcc.)[\/ ]?/.test(t) && !/s[a-z]+s/.test(t);
  return (hasSep && (ext || dir || dirLike)) || (!hasSep && ext && !/^[.\d]+$/.test(t));
}

function norm(t) {
  return t.replace(/\\/g, '/').replace(/^\.\//, '').replace(/[.,;:)]+$/, '').replace(/\/$/, '');
}

// file index (git-tracked or not; skips node_modules, .git, worktrees, graphify-out, build outputs)
const SKIP=new Set(['node_modules','.git','worktrees','graphify-out','build','.gradle','bin','obj']);
const INDEX=[];
(function walk(d,rel){for(const e of fs.readdirSync(d,{withFileTypes:true})){if(SKIP.has(e.name))continue;const r=rel?rel+'/'+e.name:e.name;INDEX.push(r+(e.isDirectory()?'/':''));if(e.isDirectory())walk(path.join(d,e.name),r);}})(ROOT,'');
function suffixFind(rel){const n=rel.replace(/\/$/,'');const hits=INDEX.filter(f=>{const g=f.replace(/\/$/,'');return g===n||g.endsWith('/'+n);});return hits;}
function exists(rel) {
  for (const r of ROOTS) {
    const p = path.join(ROOT, r, rel);
    if (fs.existsSync(p)) return r ? r + '/' + rel : rel;
  }
  const h=suffixFind(rel);
  if(h.length) return 'suffix:'+h.slice(0,3).join(' | ')+(h.length>3?' (+'+(h.length-3)+')':'');
  return null;
}

const rows = [];
let total = 0;
for (const d of DOCS) {
  const full = path.join(ROOT, d);
  if (!fs.existsSync(full)) { rows.push({ doc: d, line: 0, token: '(DOC MISSING)', status: 'missing-doc' }); continue; }
  const lines = fs.readFileSync(full, 'utf8').split(/\r?\n/);
  lines.forEach((ln, i) => {
    for (const m of ln.matchAll(/`([^`\n]+)`/g)) {
      let t = m[1].trim();
      if (!looksLikePath(t)) continue;
      total++;
      let status, where = null;
      if (/^[A-Za-z]:[\\/]/.test(t)) {
        // absolute path
        const ok = fs.existsSync(t);
        status = ok ? 'ok-abs' : (/^V:/i.test(t) ? 'old-pc-V' : 'missing-abs');
        where = ok ? t : null;
      } else if (/^~|%USERPROFILE%|^C:\\Program Files\\HCC/i.test(t)) {
        status = 'n/a-installed-path';
      } else {
        const n = norm(t);
        where = exists(n);
        status = where ? (where.startsWith('suffix:') ? 'ok-suffix' : 'ok') : 'MISSING';
      }
      rows.push({ doc: d, line: i + 1, token: t, status, where });
    }
  });
}

const bad = rows.filter(r => !/^ok/.test(r.status) && r.status !== 'n/a-installed-path');
fs.writeFileSync(path.join(OUT, 'paths.json'), JSON.stringify({ total, unresolved: bad.length, rows }, null, 1));
const txt = [`A11 path probe: ${total} path-like tokens, ${bad.length} unresolved`, ''];
for (const r of bad) txt.push(`${r.doc}:${r.line}\t${r.status}\t${r.token}`);
fs.writeFileSync(path.join(OUT, 'paths.txt'), txt.join('\n'));
console.log(txt.slice(0, 1).join('\n'));
const by = {};
for (const r of bad) by[r.status] = (by[r.status] || 0) + 1;
console.log(by);
