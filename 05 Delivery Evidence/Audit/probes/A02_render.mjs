// A02 render: turns evidence/A02/*.json (written by A02_commands.mjs) into the markdown tables of agents/A02_coverage_table.md.
// Run from the repo root after A02_commands.mjs:  node "05 Delivery Evidence/Audit/probes/A02_render.mjs"
// Writes 05 Delivery Evidence/Audit/evidence/A02/generated.md (+ mapper_target_routes.md). Read-only otherwise.
import fs from 'node:fs';
import path from 'node:path';

const ROOT = process.cwd();
const EV = path.join(ROOT, '05 Delivery Evidence', 'Audit', 'evidence', 'A02');
const J = n => JSON.parse(fs.readFileSync(path.join(EV, n), 'utf8'));
const C = J('commands.json'), S = J('screens.json'), M = J('mapper-targets.json'), SR = J('server-routes.json'), K = J('state-keys.json');
const out = [];
const w = (...a) => out.push(a.join(''));
const esc = s => String(s ?? '').replace(/\|/g, '\\|');
const short = f => (f ?? '').replace('03 Prototype/src/clients/sync/', '').replace(/\.tsx?$/, '');

// ── group (sync area, split by sub-file for services/ and channels/) ──
const subgroup = (area, files) => {
  const f = (files ?? []).map(short);
  const sub = f.find(x => x.startsWith(area + '/'));
  return sub ?? area;
};
const mapperGroup = m => {
  const f = m.files.map(short);
  const sub = f.find(x => /^(services|channels|closing|money|frontdesk)\//.test(x));
  return sub ?? m.area;
};
const unmappedGroup = (t) => {
  const u = C.unmapped[t][0];
  const fs_ = u.files.map(short);
  const sub = fs_.find(x => x.startsWith(u.area + '/'));
  return sub ?? u.area;
};
// module (mock stream) of each prototype command
const moduleOf = t => C.owner[t];
// majority group of each module among its mapped commands (to place ABSENT commands)
const modVotes = {};
for (const t of C.universe) if (C.status[t] === 'MAPPED') { const g = mapperGroup(C.mappers[t]); const m = moduleOf(t); ((modVotes[m] ||= {})[g] = (modVotes[m][g] || 0) + 1); }
const modGroup = m => { const v = modVotes[m]; if (!v) return null; return Object.entries(v).sort((a, b) => b[1] - a[1])[0][0]; };
const groupOf = t => {
  if (C.status[t] === 'MAPPED') return mapperGroup(C.mappers[t]);
  if (C.status[t] === 'UNMAPPED') return unmappedGroup(t);
  return (modGroup(moduleOf(t)) ?? 'no sync area (' + moduleOf(t) + ')');
};

// ── 1. Summary by sync area ──
const rows = {};
for (const t of C.universe) {
  const g = groupOf(t); const r = (rows[g] ||= {mapped: [], unmapped: [], absent: [], absentAssigned: false});
  if (C.status[t] === 'MAPPED') r.mapped.push(t); else if (C.status[t] === 'UNMAPPED') r.unmapped.push(t); else { r.absent.push(t); r.absentAssigned = true; }
}
const prodOnlyByGroup = {};
for (const t of Object.keys(C.mappers)) if (!C.universe.includes(t)) (prodOnlyByGroup[mapperGroup(C.mappers[t])] ||= []).push(t);
const tot = {mapped: 0, unmapped: 0, absent: 0, all: 0, prod: 0};
w('| Sync area (file) | Prototype commands | Mapped | Unmapped (reason in appendix B) | Absent (silent) | Extra production-only mappers |\n|---|---:|---:|---|---|---|\n');
for (const g of Object.keys(prodOnlyByGroup)) rows[g] ||= {mapped: [], unmapped: [], absent: []};
for (const g of Object.keys(rows).sort()) {
  const r = rows[g]; const n = r.mapped.length + r.unmapped.length + r.absent.length;
  tot.all += n; tot.mapped += r.mapped.length; tot.unmapped += r.unmapped.length; tot.absent += r.absent.length;
  const po = prodOnlyByGroup[g] ?? []; tot.prod += po.length;
  w(`| \`${g}\` | ${n} | ${r.mapped.length} | ${r.unmapped.length ? r.unmapped.length + ': ' + r.unmapped.map(t => '`' + t + '`').join(', ') : '0'} | ${r.absent.length ? r.absent.length + ': ' + r.absent.map(t => '`' + t + '`').join(', ') : '0'} | ${po.length ? po.length + ': ' + po.map(t => '`' + t + '`').join(', ') : '0'} |\n`);
}
w(`| **Total** | **${tot.all}** | **${tot.mapped}** | **${tot.unmapped}** | **${tot.absent}** | **${tot.prod}** |\n`);
const summaryAreaTable = out.splice(0).join('');

// ── 1b. Summary by prototype module (mock stream) ──
const mods = {};
for (const t of C.universe) { const m = moduleOf(t); const r = (mods[m] ||= {mapped: 0, unmapped: 0, absent: 0}); r[C.status[t].toLowerCase()]++; }
w('| Prototype module (`services/mock/handlers/*.ts`) | Commands | Mapped | Unmapped | Absent |\n|---|---:|---:|---:|---:|\n');
for (const m of Object.keys(mods).sort()) { const r = mods[m]; w(`| ${m} | ${r.mapped + r.unmapped + r.absent} | ${r.mapped} | ${r.unmapped} | ${r.absent} |\n`); }
const summaryModuleTable = out.splice(0).join('');

// ── 2. Per-command appendix ──
const screensOf = t => {
  const l = (S.cmdScreens[t] ?? []);
  const d = [], p = []; let bd = false;
  for (const x of l) { if (x.includes('(via booking dialog)')) { bd = true; continue; } (x.startsWith('m:') ? p : d).push(x.replace('m:', '')); }
  const fmt = (a, k) => a.length ? k + ':' + (a.length > 5 ? a.slice(0, 4).join(',') + ',+' + (a.length - 4) : a.join(',')) : '';
  return [fmt(d, 'D'), fmt(p, 'P'), bd ? 'booking dialog' : ''].filter(Boolean).join('; ') || '—';
};
w('| Command | Prototype module | Status | Screens (D = desktop page ids, P = phone) | Sync area file | Server command(s) | Reload slices | Reason / note |\n|---|---|---|---|---|---|---|---|\n');
const order = [...C.universe].sort((a, b) => groupOf(a).localeCompare(groupOf(b)) || a.localeCompare(b));
for (const t of order) {
  const st = C.status[t]; let file = '', srv = '', rel = '', note = '';
  if (st === 'MAPPED') {
    const m = C.mappers[t]; file = (m.files[0] ? short(m.files[0]) : '(factory in ' + m.area + ')') + '.ts';
    srv = m.targets.map(x => '`' + x + '`' + (m.indirectOnly.includes(x) ? '†' : '')).join(' + '); rel = m.reload.join(', ');
    if (m.precheck) note += 'precheck; ';
    if (m.track) note += 'typed-client result tracked; ';
    if (t === 'EXPENSE_APPROVE') note += 'ALSO listed in admin.ts `unmapped` (stale note); ';
  } else if (st === 'UNMAPPED') {
    const u = C.unmapped[t]; file = u.map(x => x.area).join(' / ') + (u.length > 1 ? ' (listed by both)' : '.ts'); note = u.map(x => (u.length > 1 ? '[' + x.area + '] ' : '') + x.reason).join(' || ');
  } else {
    file = '—'; note = '**ABSENT**: no mapper and no `unmapped` entry; the runtime refuses it with the same "Not available on the server yet" text.';
  }
  w(`| \`${t}\` | ${esc(moduleOf(t))} | ${st === 'MAPPED' ? 'MAPPED' : st === 'UNMAPPED' ? 'UNMAPPED' : '**ABSENT**'} | ${esc(screensOf(t))} | ${esc(file)} | ${srv || '—'} | ${esc(rel) || '—'} | ${esc(note)} |\n`);
}
const appendixB = out.splice(0).join('');

// production-only mappers
w('| Mapper type | Sync area file | Server command(s) | Reload | Dispatched from (UI source) |\n|---|---|---|---|---|\n');
for (const t of C.mappedNotInUniverse) {
  const m = C.mappers[t];
  w(`| \`${t}\` | ${esc(short(m.files[0]) + '.ts')} | ${m.targets.map(x => '`' + x + '`').join(' + ')} | ${m.reload.join(', ')} | ${esc([...(S.uiFileList[t] ?? S.unknownLit[t] ?? []).map(x => x.replace('03 Prototype/src/', '')), ...m.files.slice(1).map(x => x.replace('03 Prototype/src/', ''))].join(', ') || '(sync-layer helper)')} |\n`);
}
const prodOnlyTable = out.splice(0).join('');

// ── 3. Screens ──
const fmtSlices = a => a.length ? a.map(x => '`' + x + '`').join(', ') : '—';
const loaded = new Set(K.loaded);
// local bookkeeping keys of the prototype State with no server meaning (idempotency-key suffix counter, scenario label, schema, key log)
const BOOK = new Set(['sequence', 'scenario', 'schema', 'processed']);
// slices every screen touches through can()/date helpers (always loaded); left out of the "via selectors" list to keep the table readable
const BASIC = new Set(['date', 'clock', 'users', 'grants', 'settings', 'policy']);
const screenRows = kind => {
  w('| Screen id | Component (file) | Nav / permission | Own commands | Mapped | Unmapped | Absent | Booking dialog | Slices read that a loader fills | Slices read with NO loader (left as empty skeleton) | Notes |\n|---|---|---|---:|---:|---|---|---|---|---|---|\n');
  for (const sc of S.screens.filter(x => x.kind === kind)) {
    if (!sc.found) { w(`| \`${sc.id}\` | ${esc(sc.comp)} (not resolved by the walk) | ${esc(sc.nav?.p ?? '')} | ? | ? | ? | ? | ? | ? | ? | ? |\n`); continue; }
    const fileS = sc.file.replace('03 Prototype/src/', '');
    const nav = sc.nav ? `${sc.nav.label ?? ''} — ${sc.nav.p ?? ''}`.replace(/'/g, '') : (sc.id === 'home' ? 'home (modules grid)' : '(no nav item)');
    const notes = [];
    if (sc.viaTypedClient) notes.push('BOOK via typed client');
    if (sc.dynamicCmds?.length) notes.push('+' + sc.dynamicCmds.length + ' dispatched dynamically (' + sc.dynamicCmds.join(', ') + ')');
    w(`| \`${sc.id}\`${kind === 'phone' && !sc.usesPhoneVariant ? ' (desktop page)' : ''} | ${esc(sc.comp)} (\`${fileS}\`) | ${esc(nav)} | ${sc.cmds.length} | ${sc.mapped} | ${sc.unmapped ? sc.unmapped + ': ' + sc.cmds.filter(c => C.status[c] === 'UNMAPPED').join(', ') : 0} | ${sc.absent ? sc.absent + ': ' + sc.cmds.filter(c => C.status[c] === 'ABSENT').join(', ') : 0} | ${sc.usesBookingDialog ? 'yes' : 'no'} | ${fmtSlices(sc.slicesLoaded.filter(x => !BOOK.has(x)))}${(() => { const x = (sc.sliceDomLoaded ?? []).filter(k => !sc.slicesLoaded.includes(k) && !BASIC.has(k)); return x.length ? ' + ' + fmtSlices(x) + ' (via selectors)' : ''; })()} | ${fmtSlices([...sc.slicesEmpty.filter(x => !BOOK.has(x)), ...(sc.sliceDomEmpty ?? [])])} | ${esc(notes.join('; '))} |\n`);
  }
  return out.splice(0).join('');
};
const desktopTable = screenRows('desktop');
const phoneTable = screenRows('phone');
const phoneFallbacks = S.phoneFallbackIds;

// booking dialog
w('| Platform | Commands reachable from the booking dialog (and the dialogs it opens) | Mapped | Unmapped | Absent |\n|---|---|---:|---:|---:|\n');
for (const k of ['desktop', 'phone']) {
  const b = S.bookingDialog[k]; if (!b) continue;
  const c = b.cmds; w(`| ${k} | ${c.map(x => '`' + x + '`' + ((C.mappers[x] && !C.universe.includes(x)) ? ' (production-only mapper)' : C.status[x] === 'MAPPED' ? '' : C.status[x] === 'UNMAPPED' ? ' (unmapped)' : ' (ABSENT)')).join(', ')} | ${c.filter(x => C.mappers[x]).length} | ${c.filter(x => !C.mappers[x] && C.status[x] === 'UNMAPPED').length} | ${c.filter(x => !C.mappers[x] && C.status[x] === 'ABSENT').length} |\n`);
}
const bookingDialogTable = out.splice(0).join('');

// ── 4. loaders ──
w('| Sync area | Slice | Kind | Requires (prototype permission) | Required reads (D5) | Needs |\n|---|---|---|---|---|---|\n');
for (const a of C.areas) for (const l of a.loaders) w(`| ${a.id} | \`${l.slice}\` | ${l.kind} | ${l.requires.join(', ') || '—'} | ${l.reads.map(x => '`' + x + '`').join(', ') || '—'} | ${l.needs.join(', ') || '—'} |\n`);
const loadersTable = out.splice(0).join('');

// ── 5. mapper targets vs server routes ──
const distinct = {};
for (const x of M.mapperTargets) (distinct[x.target] ||= {route: x.route, protos: []}).protos.push(x.proto);
for (const m of Object.values(C.mappers)) for (const t of m.targets) if (!distinct[t]) distinct[t] = {route: SR.commands[t]?.routes[0]?.at ?? null, protos: [m.type]};
w(`Distinct server command names targeted by the ${Object.keys(C.mappers).length} mappers: **${Object.keys(distinct).length}**; present as \`POST /api/v1/commands/<Name>\` in \`08 Production/src/Hcc.Api/Endpoints/*.cs\`: **${Object.keys(distinct).filter(k => SR.commands[k]).length}**; missing: **${Object.keys(distinct).filter(k => !SR.commands[k]).length}**.\n\n`);
w('| Server command | Route registered at | Prototype command(s) that send it |\n|---|---|---|\n');
for (const k of Object.keys(distinct).sort()) w(`| \`${k}\` | ${distinct[k].route ?? '**NOT FOUND**'} | ${[...new Set(distinct[k].protos)].map(x => '`' + x + '`').join(', ')} |\n`);
fs.writeFileSync(path.join(EV, 'mapper_target_routes.md'), '# A02 - mapper targets and the server routes that serve them\n\nGenerated by probes/A02_render.mjs from evidence/A02/mapper-targets.json and server-routes.json.\n\n' + out.splice(0).join(''));

// server commands nobody sends
const nt = J('server-not-targeted.json');

// server commands no mapper sends: is any of them used by other client code (sync UI helpers, typed clients, import/guest-id screens)?
const walkTs = (dir, o = []) => { for (const e of fs.readdirSync(dir, {withFileTypes: true})) { const p = path.join(dir, e.name); if (e.isDirectory()) walkTs(p, o); else if (/\.tsx?$/.test(e.name)) o.push(p); } return o; };
const clientFiles = walkTs(path.join(ROOT, '03 Prototype', 'src', 'clients')).concat(walkTs(path.join(ROOT, '03 Prototype', 'src', 'features')), walkTs(path.join(ROOT, '03 Prototype', 'src', 'mobile')), walkTs(path.join(ROOT, '03 Prototype', 'src', 'app')));
const clientText = Object.fromEntries(clientFiles.map(f => [f, fs.readFileSync(f, 'utf8')]));
w('| Server command | Registered at | Used by other client code (outside the mappers) |\n|---|---|---|\n');
for (const n of nt) {
  const hits = clientFiles.filter(f => new RegExp("['\"`/]" + n + "['\"`]").test(clientText[f])).map(f => path.relative(ROOT, f).split(path.sep).join('/').replace('03 Prototype/src/', ''));
  w(`| \`${n}\` | ${SR.commands[n].routes[0].at.replace('08 Production/src/Hcc.Api/Endpoints/', '')} | ${hits.length ? esc(hits.join(', ')) : '**no client code sends it**'} |\n`);
}
const serverNotTargetedTable = out.splice(0).join('');

// state keys
const notLoaded = K.notLoaded.map(k => `\`${k}\` (${K.kinds[k]})`).join(', ');

// ── assemble generated.md ──
const parts = {serverNotTargetedTable, summaryAreaTable, summaryModuleTable, appendixB, prodOnlyTable, desktopTable, phoneTable, bookingDialogTable, loadersTable};
let md = '';
for (const [k, v] of Object.entries(parts)) md += `<!-- BEGIN ${k} -->\n${v}\n<!-- END ${k} -->\n\n`;
fs.writeFileSync(path.join(EV, 'generated.md'), md);
fs.writeFileSync(path.join(EV, 'render-facts.json'), JSON.stringify({totals: tot, serverCommands: SR.commandCount, serverNotTargeted: nt, notLoaded, phoneFallbacks, desktopScreens: S.desktopIds.length, phoneScreens: S.phoneIds.length}, null, 1));
console.log('totals', tot, '| server commands', SR.commandCount, '| not targeted by any mapper', nt.length, '| phone pages falling back to the desktop page:', phoneFallbacks.length);

// ── final file: fill the hand-written template (probes/A02_template.md) with the generated tables ──
const tpl = fs.readFileSync(path.join(ROOT, '05 Delivery Evidence', 'Audit', 'probes', 'A02_template.md'), 'utf8');
const vars = {...parts, notLoaded};
const filled = tpl.replace(/\{\{(\w+)\}\}/g, (m, k) => { if (!(k in vars)) throw new Error('template variable not generated: ' + k); return String(vars[k]).trim(); });
fs.writeFileSync(path.join(ROOT, '05 Delivery Evidence', 'Audit', 'agents', 'A02_coverage_table.md'), filled);
console.log('wrote agents/A02_coverage_table.md', filled.length, 'chars');
