// A11 probe: INSTALL.md (and 08 Production README) commands -> does each script exist, does it declare every -Flag the document uses,
// do the config keys named exist in code, do the HTTP routes named exist in Endpoints.
import fs from 'node:fs';
import path from 'node:path';

const ROOT = process.cwd();
const OUT = path.join(ROOT, '05 Delivery Evidence', 'Audit', 'evidence', 'A11');
fs.mkdirSync(OUT, { recursive: true });
const WIN = path.join(ROOT, '08 Production', 'deployment', 'windows');
const install = fs.readFileSync(path.join(WIN, 'INSTALL.md'), 'utf8');
const android = fs.readFileSync(path.join(ROOT, '04 Android', 'README.md'), 'utf8');
const prodReadme = fs.readFileSync(path.join(ROOT, '08 Production', 'README.md'), 'utf8');
const res = { scripts: [], flags: [], configKeys: [], routes: [], other: [] };

// --- script invocations in code lines: .\Name.ps1 -Flag value ...
const scripts = fs.readdirSync(WIN).filter(f => f.endsWith('.ps1'));
const text = {};
for (const s of scripts) text[s] = fs.readFileSync(path.join(WIN, s), 'utf8');
const seenFlags = new Set();
for (const [i, ln] of install.split(/\r?\n/).entries()) {
  const m = ln.match(/^\s*(?:\.\\|")?([A-Za-z-]+\.ps1)"?\s+(.*)$/) || ln.match(/\.\\([A-Za-z-]+\.ps1)\s+(.*?)(?:\s+#.*)?$/);
  if (!m) continue;
  const [, name, rest] = m;
  const exists = fs.existsSync(path.join(WIN, name));
  res.scripts.push({ line: i + 1, script: name, exists });
  if (!exists) continue;
  for (const f of rest.matchAll(/(?:^|\s)-([A-Za-z]+)\b/g)) {
    const flag = f[1];
    if (['WhatIf', 'Verbose', 'Confirm'].includes(flag)) { // common parameters need SupportsShouldProcess / CmdletBinding
      const ok = /SupportsShouldProcess|CmdletBinding/i.test(text[name]);
      res.flags.push({ line: i + 1, script: name, flag, declared: ok, note: 'common parameter (CmdletBinding/ShouldProcess)' });
      continue;
    }
    const declared = new RegExp('\\$' + flag + '\\b', 'i').test(text[name]);
    res.flags.push({ line: i + 1, script: name, flag, declared });
  }
}
// scripts named in prose with backticks but not in code lines
for (const m of install.matchAll(/`([A-Za-z-]+\.ps1)`/g)) res.scripts.push({ line: 0, script: m[1], exists: fs.existsSync(path.join(WIN, m[1])), prose: true });

// functions named
for (const fn of ['ConvertTo-HccWrappedGuestIdKey']) {
  const hit = Object.entries(text).filter(([, t]) => t.includes(fn)).map(([n]) => n);
  res.other.push({ what: 'function ' + fn, foundIn: hit });
}
res.other.push({ what: 'installer', Hcc_iss: fs.existsSync(path.join(WIN, 'installer', 'Hcc.iss')), Build_Installer: fs.existsSync(path.join(WIN, 'installer', 'Build-Installer.ps1')), builtExeInRepo: false });

// --- config keys
const SRC = path.join(ROOT, '08 Production', 'src');
const allCs = [];
(function walk(d) { for (const e of fs.readdirSync(d, { withFileTypes: true })) { if (['bin', 'obj'].includes(e.name)) continue; const p = path.join(d, e.name); if (e.isDirectory()) walk(p); else if (e.name.endsWith('.cs')) allCs.push(p); } })(SRC);
const blob = allCs.map(f => fs.readFileSync(f, 'utf8')).join('\n');
const sampleCfg = fs.readFileSync(path.join(WIN, 'appsettings.Production.sample.json'), 'utf8');
const keys = ['WebRoot', 'Enabled', 'PublicPort', 'PublicBindAddress', 'KnownProxies', 'KnownNetworks', 'AllowedHosts', 'LoopbackHttpPort', 'HttpsPort', 'CertificateThumbprint', 'BackupDirectory', 'ServerName',
  'GuestIdKeys', 'AllowMachineScope', 'AllowPlainKeys', 'Required', 'IdempotencyRetention', 'SessionRetention', 'SignInFailuresPerAddress', 'WebBooking', 'documents.retentionDays'];
for (const k of keys) res.configKeys.push({ key: k, inCs: blob.includes('"' + k + '"') || new RegExp('\\b' + k + '\\b').test(blob), inSample: sampleCfg.includes(k) });
res.other.push({ what: 'sample config keys', value: Object.keys(JSON.parse(sampleCfg)) });

// --- HTTP routes named in READMEs/INSTALL
const EP = path.join(SRC, 'Hcc.Api');
const epBlob = [];
(function walk(d) { for (const e of fs.readdirSync(d, { withFileTypes: true })) { if (['bin', 'obj'].includes(e.name)) continue; const p = path.join(d, e.name); if (e.isDirectory()) walk(p); else if (e.name.endsWith('.cs')) epBlob.push(fs.readFileSync(p, 'utf8')); } })(EP);
const eb = epBlob.join('\n');
const routes = ['/system/health', '/system/discovery', '/setup/SetupProperty', '/setup/status', '/auth/bootstrap-owner', '/public/booking', '/me/capabilities', '/rooms/board', '/rooms/status', '/cashier/folios', '/admin/users', '/cashbook', '/commands/RevealGuestId', '/commands/RegisterDevice', '/commands/QuoteStay', '/commands/CreateReservation', '/commands/SetGstSetting', '/compliance/', '/commands/ApplyRoomBlock'];
for (const r of routes) {
  const core = r.replace(/^\/commands\//, '');
  const inEp = eb.includes(r) || eb.includes('"' + core + '"') || eb.includes(core);
  res.routes.push({ route: r, foundInApiOrContracts: inEp });
}
// Program.cs migrate verb
const prog = fs.readFileSync(path.join(SRC, 'Hcc.Api', 'Program.cs'), 'utf8');
res.other.push({ what: 'Program.cs handles "migrate"', value: /["']migrate["']/.test(prog) });

fs.writeFileSync(path.join(OUT, 'install_check.json'), JSON.stringify(res, null, 1));
const bad = [...res.scripts.filter(s => !s.exists), ...res.flags.filter(f => !f.declared), ...res.configKeys.filter(k => !k.inCs), ...res.routes.filter(r => !r.foundInApiOrContracts)];
console.log('scripts referenced:', res.scripts.length, ' flags checked:', res.flags.length, ' unresolved/undeclared:', bad.length);
console.log(JSON.stringify(bad, null, 1));
console.log(JSON.stringify(res.other, null, 1));
