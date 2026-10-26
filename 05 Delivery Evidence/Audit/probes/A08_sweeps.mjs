// A08 mechanical sweeps (read-only). Run from repo root:
//   $env:PATH="E:\Tools\nodejs;$env:PATH"; node "05 Delivery Evidence/Audit/probes/A08_sweeps.mjs"
// Outputs sweeps.json / sweeps.txt / routes.json into 05 Delivery Evidence/Audit/evidence/A08/.
import fs from 'node:fs';
import path from 'node:path';

const ROOT = process.cwd();
const PROD = path.join(ROOT, '08 Production');
const OUT = path.join(ROOT, '05 Delivery Evidence', 'Audit', 'evidence', 'A08');
fs.mkdirSync(OUT, { recursive: true });

const walk = (dir, filter) => {
  const out = [];
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name === 'bin' || e.name === 'obj' || e.name === 'node_modules') continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) out.push(...walk(p, filter)); else if (filter(p)) out.push(p);
  }
  return out;
};
const rel = p => path.relative(ROOT, p).replaceAll('\\', '/');
const lineOf = (text, idx) => text.slice(0, idx).split('\n').length;

// ── 1. Project references and layering by using-directives ──
const layers = ['Hcc.Contracts', 'Hcc.Domain', 'Hcc.Application', 'Hcc.Infrastructure.SqlServer', 'Hcc.Operations', 'Hcc.Api'];
const projRefs = {};
for (const l of layers) {
  const csproj = fs.readFileSync(path.join(PROD, 'src', l, `${l}.csproj`), 'utf8');
  projRefs[l] = {
    projectRefs: [...csproj.matchAll(/ProjectReference Include="[^"]*\\([^\\"]+)\.csproj"/g)].map(m => m[1]),
    packageRefs: [...csproj.matchAll(/PackageReference Include="([^"]+)"/g)].map(m => m[1]),
    frameworkRefs: [...csproj.matchAll(/FrameworkReference Include="([^"]+)"/g)].map(m => m[1]),
  };
}
const usingViolations = [];
const forbiddenNs = {
  'Hcc.Contracts': [/^Hcc\.(Domain|Application|Infrastructure|Api|Operations)\b/, /^Microsoft\.(Data|AspNetCore)/, /^System\.Data\b/],
  'Hcc.Domain': [/^Hcc\.(Application|Infrastructure|Api|Operations)\b/, /^Microsoft\.(Data|AspNetCore|Extensions\.DependencyInjection)/, /^System\.Data\b/, /^System\.Net\.Http\b/, /^System\.IO\b/],
  'Hcc.Application': [/^Hcc\.(Infrastructure|Api|Operations)\b/, /^Microsoft\.(Data|AspNetCore)/, /^System\.Data\b/],
  'Hcc.Infrastructure.SqlServer': [/^Hcc\.(Api|Operations)\b/, /^Microsoft\.AspNetCore/],
  'Hcc.Operations': [/^Hcc\.Api\b/],
};
for (const l of layers) {
  for (const f of walk(path.join(PROD, 'src', l), p => p.endsWith('.cs'))) {
    const t = fs.readFileSync(f, 'utf8');
    for (const m of t.matchAll(/^\s*(?:global\s+)?using\s+(?:static\s+)?([A-Za-z0-9_.]+)\s*;/gm)) {
      for (const rx of forbiddenNs[l] ?? []) if (rx.test(m[1])) usingViolations.push({ layer: l, file: rel(f), line: lineOf(t, m.index), using: m[1] });
    }
    if (l === 'Hcc.Domain' || l === 'Hcc.Application' || l === 'Hcc.Contracts') {
      for (const m of t.matchAll(/\b(Microsoft\.Data\.SqlClient|System\.Data\.SqlClient|System\.IO\.File|System\.IO\.Directory|System\.Net\.Http\.HttpClient|DateTime\.(?:Now|UtcNow|Today)|DateTimeOffset\.(?:Now|UtcNow)|Environment\.GetEnvironmentVariable)\b/g))
        usingViolations.push({ layer: l, file: rel(f), line: lineOf(t, m.index), using: `(qualified) ${m[1]}` });
    }
  }
}

// ── 2. Migrations ──
const migDir = path.join(PROD, 'database', 'migrations');
const migs = fs.readdirSync(migDir).filter(f => /^\d{4}_.*\.sql$/.test(f)).sort();
const blockOwner = v => {
  if (v <= 2) return 'Platform/P0';
  const b = Math.floor(v / 100);
  return ({ 1: 'Access', 2: 'Inventory+Reservations', 3: 'Stays+Housekeeping', 4: 'Billing+Payments', 5: 'Closing+Reporting', 6: 'Tax+GuestIds',
    7: 'Configuration', 8: 'Expenses', 9: 'Payroll', 10: 'Accounting', 11: 'Compliance', 12: 'Pos', 13: 'Maintenance', 14: 'Stock', 15: 'GuestServices',
    16: 'Messaging', 17: 'Handover', 18: 'Yield', 19: 'Setup', 20: 'WebBooking', 21: 'Documents', 22: 'Groups', 23: 'Customers', 24: 'Invoices',
    25: 'Ota', 26: 'RbacV2', 27: 'Import' })[b] ?? `block${b}`;
};
const p0Owner = t => {
  if (t.startsWith('plat.')) return 'Platform/P0';
  if (t.startsWith('inv.')) return 'Inventory+Reservations';
  if (t.startsWith('bill.') || t.startsWith('pay.')) return 'Billing+Payments';
  return 'Platform/P0';
};
const tableCreator = {};
const migInfo = [];
const stripComments = s => s.replace(/--[^\n]*/g, '').replace(/\/\*[\s\S]*?\*\//g, '');
for (const f of migs) {
  const raw = fs.readFileSync(path.join(migDir, f), 'utf8');
  const sql = stripComments(raw);
  const v = parseInt(f.slice(0, 4), 10);
  for (const m of sql.matchAll(/CREATE\s+TABLE\s+([a-z]+)\.([a-z0-9_]+)/gi)) {
    const t = `${m[1]}.${m[2]}`.toLowerCase();
    tableCreator[t] = { mig: f, owner: v <= 2 ? p0Owner(t) : blockOwner(v) };
  }
  migInfo.push({ f, v, raw, sql });
}
const ownerOf = t => tableCreator[t]?.owner ?? '(unknown)';
const crossDdl = [];
const fks = [];
const migStats = [];
const migDml = [];
for (const { f, v, raw, sql } of migInfo) {
  const owner = v <= 2 ? 'Platform/P0' : blockOwner(v);
  const goCount = (raw.match(/^\s*GO\s*(?:--.*)?$/gim) || []).length;
  const explicitTx = /\bBEGIN\s+TRAN(SACTION)?\b|\bCOMMIT\b|\bROLLBACK\b/i.test(sql);
  const nonTxable = [...sql.matchAll(/\b(ALTER\s+DATABASE|CREATE\s+FULLTEXT|BACKUP\s|RESTORE\s|DBCC\s)/gi)].map(m => m[1]);
  const createProc = (sql.match(/CREATE\s+(OR\s+ALTER\s+)?(PROCEDURE|PROC|TRIGGER|FUNCTION|VIEW)\b/gi) || []).length;
  migStats.push({ f, owner, goCount, explicitTx, nonTxable, createProcTrigFnView: createProc, crlf: raw.includes('\r\n') });
  for (const m of sql.matchAll(/ALTER\s+TABLE\s+([a-z]+)\.([a-z0-9_]+)\s+(ADD|DROP|ALTER|WITH|NOCHECK|CHECK)/gi)) {
    const t = `${m[1]}.${m[2]}`.toLowerCase();
    const o = v <= 2 ? p0Owner(t) : owner;
    const tOwner = ownerOf(t);
    if (tOwner !== o) crossDdl.push({ mig: f, migOwner: o, table: t, tableOwner: tOwner, tableCreatedIn: tableCreator[t]?.mig, verb: m[3].toUpperCase(), line: lineOf(raw, Math.max(0, raw.toLowerCase().indexOf(`alter table ${t}`))) });
  }
  for (const m of sql.matchAll(/CREATE\s+(?:UNIQUE\s+)?(?:NONCLUSTERED\s+|CLUSTERED\s+)?(INDEX|TRIGGER)\s+[A-Za-z0-9_.\[\]]+\s+ON\s+([a-z]+)\.([a-z0-9_]+)/gi)) {
    const t = `${m[2]}.${m[3]}`.toLowerCase();
    const o = v <= 2 ? p0Owner(t) : owner;
    if (ownerOf(t) !== o) crossDdl.push({ mig: f, migOwner: o, table: t, tableOwner: ownerOf(t), tableCreatedIn: tableCreator[t]?.mig, verb: m[1].toUpperCase(), line: 0 });
  }
  for (const m of sql.matchAll(/\b(INSERT(?:\s+INTO)?|UPDATE|DELETE(?:\s+FROM)?|MERGE(?:\s+INTO)?)\s+([a-z]+\.[a-z0-9_]+)/gi)) {
    const t = m[2].toLowerCase();
    const o = v <= 2 ? p0Owner(t) : owner;
    if (ownerOf(t) !== o) migDml.push({ mig: f, migOwner: o, verb: m[1].toUpperCase().split(/\s+/)[0], table: t, tableOwner: ownerOf(t) });
  }
  const re = /(CREATE\s+TABLE|ALTER\s+TABLE)\s+([a-z]+\.[a-z0-9_]+)|REFERENCES\s+([a-z]+\.[a-z0-9_]+)/gi;
  let current = null;
  for (const m of sql.matchAll(re)) {
    if (m[2]) { current = m[2].toLowerCase(); continue; }
    const ref = m[3].toLowerCase();
    if (!current) continue;
    const parentOwner = ownerOf(current), refOwner = ownerOf(ref);
    const pMig = tableCreator[current]?.mig ?? f, rMig = tableCreator[ref]?.mig ?? '?';
    fks.push({ mig: f, parent: current, parentOwner, referenced: ref, refOwner, cross: parentOwner !== refOwner,
      direction: rMig > pMig ? 'EARLIER->LATER (older table points at newer module)' : 'later->earlier' });
  }
}

// ── 3. SQL writes per Infrastructure/Api/Operations file ──
const moduleOfFile = f => {
  const r = rel(f);
  const m = r.match(/src\/Hcc\.Infrastructure\.SqlServer\/([^/]+)\//);
  if (!m) return r.includes('Hcc.Api') ? 'Api' : r.includes('Hcc.Operations') ? 'Operations' : '?';
  const folder = m[1];
  return ({ Inventory: 'Inventory+Reservations', Reservations: 'Inventory+Reservations', Stays: 'Stays+Housekeeping', Housekeeping: 'Stays+Housekeeping',
    Billing: 'Billing+Payments', Payments: 'Billing+Payments', Closing: 'Closing+Reporting', Reporting: 'Closing+Reporting', Tax: 'Tax+GuestIds', GuestIds: 'Tax+GuestIds',
    Commands: 'Platform/P0', Outbox: 'Platform/P0', Migrations: 'Platform/P0', Sql: 'Platform/P0', Connection: 'Platform/P0' })[folder] ?? folder;
};
const writes = [];
const codeFiles = [
  ...walk(path.join(PROD, 'src', 'Hcc.Infrastructure.SqlServer'), p => p.endsWith('.cs')),
  ...walk(path.join(PROD, 'src', 'Hcc.Api'), p => p.endsWith('.cs')),
  ...walk(path.join(PROD, 'src', 'Hcc.Operations'), p => p.endsWith('.cs')),
];
const writeRe = /\b(INSERT(?:\s+INTO)?|UPDATE(?:\s+TOP\s*\(\s*\d+\s*\))?|DELETE(?:\s+TOP\s*\(\s*\d+\s*\))?(?:\s+FROM)?|MERGE(?:\s+INTO)?)\s+([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?|\{[^}]+\}|@\w+)/g;
const methodRe = /^\s*(?:public|private|internal|protected)\b[^;=]*?\s(\w+)\s*(?:<[^>]*>)?\s*\(/;
for (const f of codeFiles) {
  const t = fs.readFileSync(f, 'utf8');
  const lines = t.split('\n');
  for (const m of t.matchAll(writeRe)) {
    const verb = m[1].toUpperCase().replace(/\s+/g, ' ');
    let target = m[2];
    if (target.startsWith('@')) continue;
    if (!/^[A-Z ()0-9]+$/.test(m[1])) continue; // SQL keywords are upper-case in this codebase
    const ln = lineOf(t, m.index);
    const lineText = lines[ln - 1];
    if (/^\s*\/\//.test(lineText)) continue;
    if (/^(SET|STATISTICS)$/i.test(target)) continue;
    if (verb.startsWith('UPDATE') && !target.includes('.') && !target.startsWith('{')) {
      const after = t.slice(m.index, m.index + 1200);
      const am = after.match(new RegExp(`(?:FROM|JOIN)\\s+([a-z]+\\.[a-z0-9_]+)\\s+(?:AS\\s+)?${target}\\b`, 'i'));
      if (am) target = am[1]; else target = `alias:${target}`;
    }
    if (verb.startsWith('DELETE') && !target.includes('.') && !target.startsWith('{')) {
      const after = t.slice(m.index, m.index + 600);
      const am = after.match(new RegExp(`FROM\\s+([a-z]+\\.[a-z0-9_]+)\\s+(?:AS\\s+)?${target}\\b`, 'i'));
      if (am) target = am[1]; else target = `alias:${target}`;
    }
    if (!target.includes('.') && !target.startsWith('{') && !target.startsWith('alias:')) continue;
    if (target.startsWith('alias:') && /^alias:(the|a|an|it|this|is|of|to|and|or|in|on|for)$/i.test(target)) continue;
    target = target.toLowerCase();
    let method = '?', sig = '', start = 0;
    for (let i = ln - 1; i >= 0; i--) {
      if (methodRe.test(lines[i]) && !/\bclass\b|\brecord\b|\binterface\b/.test(lines[i])) { method = lines[i].match(methodRe)[1]; sig = lines.slice(i, i + 3).join(' '); start = i; break; }
    }
    const body = lines.slice(start, ln + 1).join('\n');
    let txKind = 'unknown';
    if (/ICommandTransaction|SqlCommandTransaction/.test(sig)) txKind = 'runner tx (param)';
    else if (/BeginTransactionAsync/.test(body)) txKind = 'own transaction';
    else if (/OpenAsync\(/.test(body)) txKind = 'own connection, autocommit';
    else if (/Cmd\(tx|Sql\(tx\)|\btx\.Command\(/.test(body)) txKind = 'runner tx (local)';
    const fileModule = moduleOfFile(f);
    const tOwner = target.startsWith('{') || target.startsWith('alias:') ? '(dynamic)' : ownerOf(target);
    writes.push({ file: rel(f), line: ln, fileModule, verb: verb.split(' ')[0], table: target, tableOwner: tOwner, cross: tOwner !== fileModule && tOwner !== '(dynamic)', method, txKind });
  }
}

// ── 4. Routes and runner usage ──
const endpointFiles = walk(path.join(PROD, 'src', 'Hcc.Api'), p => p.endsWith('.cs'));
const routes = [];
for (const f of endpointFiles) {
  const t = fs.readFileSync(f, 'utf8');
  for (const m of t.matchAll(/\.Map(Get|Post|Put|Delete|Patch)\(\s*"([^"]+)"/g)) routes.push({ file: rel(f), line: lineOf(t, m.index), verb: m[1].toUpperCase(), route: m[2] });
}
const appFiles = walk(path.join(PROD, 'src', 'Hcc.Application'), p => p.endsWith('.cs'));
const runnerUse = [];
for (const f of appFiles) {
  const t = fs.readFileSync(f, 'utf8');
  runnerUse.push({ file: rel(f), runAsync: (t.match(/\.RunAsync\s*[<(]/g) || []).length, injectsRunner: /ICommandRunner/.test(t) });
}

// ── 5. expectedVersion coverage ──
const contractFiles = walk(path.join(PROD, 'src', 'Hcc.Contracts'), p => p.endsWith('.cs'));
const commandRecords = [];
for (const f of contractFiles) {
  const t = fs.readFileSync(f, 'utf8');
  for (const m of t.matchAll(/public\s+sealed\s+record\s+(\w+)\s*\(([^;{]*?)\)\s*[;{]/gs)) {
    const name = m[1], params = m[2];
    if (!/(Request|Command)$/.test(name) && !/^(Request|Decide|Execute|Record|Post|Create|Amend|Cancel|Mark|Register|Check|Open|Close|Submit|Approve|Reject|Update|Set|Add|Remove|Move|Extend|Shorten|Issue|Void|Settle|Lock|Unlock|Transition|Assign|Complete|Start|Reopen|Publish|Save|Change|Disable|Enable|Reset|Rotate|Reveal|Import|Merge|Link|Book|Confirm|Send|Receive|Transfer|Adjust|Approve|Release|Pick|Run|Generate)/.test(name)) continue;
    if (/(Response|Result|Row|View|Summary|Line|Snapshot|Status|Info|Report|Page|Detail|Details)$/.test(name)) continue;
    commandRecords.push({ file: rel(f), line: lineOf(t, m.index), name, hasExpectedVersion: /ExpectedVersion/i.test(params) });
  }
}
const rowverTables = [];
for (const { f, sql } of migInfo) {
  const parts = sql.split(/(?=CREATE\s+TABLE)/i);
  for (const b of parts) {
    const cm = b.match(/^CREATE\s+TABLE\s+([a-z]+\.[a-z0-9_]+)/i);
    if (!cm) continue;
    const end = b.search(/\)\s*;/);
    const body = end > 0 ? b.slice(0, end) : b.slice(0, 4000);
    if (/\bROWVERSION\b/i.test(body)) rowverTables.push({ table: cm[1].toLowerCase(), mig: f });
  }
  for (const m of sql.matchAll(/ALTER\s+TABLE\s+([a-z]+\.[a-z0-9_]+)\s+ADD\b[^;]*?\bROWVERSION\b/gi)) rowverTables.push({ table: m[1].toLowerCase(), mig: f, via: 'ALTER' });
}
const rvSet = new Set(rowverTables.map(r => r.table));
const rvUpdates = [];
const fileText = {};
for (const f of codeFiles) fileText[rel(f)] = fs.readFileSync(f, 'utf8').split('\n');
for (const w of writes.filter(x => x.verb === 'UPDATE' && rvSet.has(x.table))) {
  const lines = fileText[w.file];
  const chunk = lines.slice(w.line - 1, w.line + 10).join('\n');
  const stmt = chunk.split(/;\s*\r?\n/)[0];
  rvUpdates.push({ ...w, versionChecked: /row_ver\s*(=|<>)\s*@/i.test(stmt) });
}

const summary = {
  generatedUtc: new Date().toISOString(), node: process.version,
  projRefs, usingViolations, migStats, crossDdl, migDml, fksCross: fks.filter(x => x.cross), fkCountAll: fks.length,
  writes, routes: { total: routes.length, post: routes.filter(r => r.verb === 'POST').length, commandRoutes: routes.filter(r => r.route.includes('commands')).length },
  runnerUse: runnerUse.filter(r => r.runAsync > 0 || r.injectsRunner),
  commandRecords, rowverTables, rvUpdates,
};
fs.writeFileSync(path.join(OUT, 'sweeps.json'), JSON.stringify(summary, null, 1));
fs.writeFileSync(path.join(OUT, 'routes.json'), JSON.stringify(routes, null, 1));

const tx = [];
tx.push(`A08 sweeps ${summary.generatedUtc} node ${process.version}`);
tx.push('\n== Project references ==');
for (const [k, v] of Object.entries(projRefs)) tx.push(`${k} -> [${v.projectRefs.join(', ')}] packages [${v.packageRefs.join(', ')}] frameworks [${v.frameworkRefs.join(', ')}]`);
tx.push('\n== Layering (using / qualified) hits ==');
for (const u of usingViolations) tx.push(`${u.layer}\t${u.file}:${u.line}\t${u.using}`);
tx.push('\n== Write table (file -> table -> owner) ==');
const agg = {};
for (const w of writes) {
  agg[w.file] ??= { module: w.fileModule, tables: {} };
  const e = (agg[w.file].tables[w.table] ??= { owner: w.tableOwner, n: 0, lines: [], tx: new Set() });
  e.n++; e.lines.push(w.line); e.tx.add(w.txKind);
}
for (const [file, a] of Object.entries(agg))
  for (const [table, x] of Object.entries(a.tables))
    tx.push(`${x.owner !== a.module && x.owner !== '(dynamic)' ? 'CROSS' : 'own  '}\t${file}\t[${a.module}]\t${table}\t[${x.owner}]\tn=${x.n}\tlines=${x.lines.join(',')}\ttx=${[...x.tx].join('|')}`);
tx.push('\n== Writes outside a runner transaction ==');
for (const w of writes.filter(w => !w.txKind.startsWith('runner'))) tx.push(`${w.txKind}\t${w.file}:${w.line}\t${w.method}\t${w.verb} ${w.table}`);
tx.push('\n== Cross-module DDL in migrations ==');
for (const c of crossDdl) tx.push(`${c.mig}:${c.line}\t[${c.migOwner}]\t${c.verb}\t${c.table}\t(owned by ${c.tableOwner}, created in ${c.tableCreatedIn})`);
tx.push('\n== Cross-module DML in migrations ==');
for (const c of migDml) tx.push(`${c.mig}\t[${c.migOwner}]\t${c.verb}\t${c.table}\t(owned by ${c.tableOwner})`);
tx.push('\n== Cross-module FKs ==');
for (const k of fks.filter(x => x.cross)) tx.push(`${k.mig}\t${k.parent} [${k.parentOwner}] -> ${k.referenced} [${k.refOwner}]\t${k.direction}`);
tx.push(`\n(all FK references found: ${fks.length})`);
tx.push('\n== Migration GO / transaction notes ==');
for (const s of migStats) tx.push(`${s.f}\t[${s.owner}]\tGO=${s.goCount}\texplicitTx=${s.explicitTx}\tproc/trig/fn/view=${s.createProcTrigFnView}\tnonTransactional=${s.nonTxable.join('|') || '-'}\tCRLF=${s.crlf}`);
tx.push('\n== Command-like records in Contracts: ExpectedVersion? ==');
for (const c of commandRecords) tx.push(`${c.hasExpectedVersion ? 'has ' : 'NONE'}\t${c.name}\t${c.file}:${c.line}`);
tx.push('\n== Tables with ROWVERSION ==');
tx.push(rowverTables.map(r => `${r.table} (${r.mig}${r.via ? ' ALTER' : ''})`).join('\n'));
tx.push('\n== UPDATEs on rowversion tables: row_ver compared in the statement? ==');
for (const r of rvUpdates) tx.push(`${r.versionChecked ? 'checked ' : 'NO-CHECK'}\t${r.file}:${r.line}\t${r.method}\t${r.table}\t${r.txKind}`);
tx.push(`\nRoutes: ${routes.length} total, ${summary.routes.post} POST, ${summary.routes.commandRoutes} contain 'commands'`);
fs.writeFileSync(path.join(OUT, 'sweeps.txt'), tx.join('\n'));
console.log(`writes=${writes.length} cross=${writes.filter(w => w.cross).length} usingHits=${usingViolations.length} crossDdl=${crossDdl.length} migDml=${migDml.length} crossFk=${fks.filter(x => x.cross).length}/${fks.length} cmdRecords=${commandRecords.length} rvTables=${rowverTables.length} rvUpdates=${rvUpdates.length} routes=${routes.length}`);
