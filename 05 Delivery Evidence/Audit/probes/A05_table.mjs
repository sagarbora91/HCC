// A05 step 6: final per-route table (JSON/CSV in evidence). Role reach = static derivation of the entry gate (semantics per route below);
// for the 78 GET routes recorded in 03 Prototype/tests/_role-reads.json the REAL server answer per role replaces the static answer.
import fs from 'node:fs';
import { ROOT } from './A05_lib.mjs';
import { ROLES, perms, grants, v1, v2, passes } from './A05_access.mjs';
const OUT = ROOT + '/05 Delivery Evidence/Audit/evidence/A05';
const rows = JSON.parse(fs.readFileSync(OUT + '/A05_route_access.json', 'utf8'));
const J = JSON.parse(fs.readFileSync(ROOT + '/03 Prototype/tests/_role-reads.json', 'utf8'));
const CORE = new Set(['Inventory', 'Reservations', 'Stays', 'Housekeeping', 'Billing', 'Payments', 'Closing', 'Expenses', 'GuestId', 'Tax', 'Access']);
const NOTMATRIX_ROUTE = /^POST \/commands\/(CreateTill|LockCashBookDay|ReopenCashBookDay)$/;   // core module, but no v1 row describes the action (borrowed id)
const V2_UNCHANGED = new Set(['Payroll', 'Accounting', 'Compliance']);
const ord = { A: 0, L: 1, R: 2, V: 3, M: 4 };
const P = id => 'PERM-' + id;

// ---- entry-gate semantics for routes with several PERM ids (read from the code; see "Needs judgement") ----
// 'primary' = first id is the gate, the others are step-ups/conditional; 'and' = every id; 'or' = any id (alternatives / by kind / by record)
const SEM = [
  [/^POST \/commands\/(CreateReservation|ConfirmHold)$/, 'primary', ['RES-002'], 'APR-001 only for a restriction override'],
  [/^POST \/commands\/AmendReservation$/, 'primary', ['RES-003'], 'APR-001 only for a restriction override'],
  [/^POST \/commands\/(ExtendStay|MoveRoom)$/, 'primary', ['STY-002'], 'APR-001 only when the agreed rate changes'],
  [/^POST \/commands\/(DisposeLostItem|RecordComplaintFollowUp)$/, 'primary', ['GST-002'], 'APR-001 only for High/Critical complaints or disposal'],
  [/^POST \/commands\/RecordGroupReceipt$/, 'primary', ['GRP-005'], 'GRP-003 step-up for master refunds'],
  [/^POST \/commands\/(SaveCompany|SetCompanyLimit)$/, 'primary', ['GRP-002'], 'GRP-003 decides a credit limit above 0 (Manager within limit)'],
  [/^POST \/commands\/(SaveDepartment|SaveStaff)$/, 'primary', ['HR-002'], 'HR-005 (pay) / HR-009 (leave cap, login link) for those fields'],
  [/^POST \/commands\/MarkAttendance$/, 'or', ['HR-004', 'HR-002'], 'own record (HR-004 scope O) or manager (HR-002)'],
  [/^POST \/commands\/SaveAmc$/, 'and', ['RPT-002', 'HK-004'], 'G12: Owner/Manager only'],
  [/^POST \/commands\/(BlockWorkOrderRoom|VerifyWorkOrder)$/, 'and', ['ROOM-002', 'HK-004'], ''],
  [/^POST \/commands\/(AddWorkOrderPart|CompleteWorkOrder|HoldWorkOrder|StartWorkOrder)$/, 'and', ['HK-001', 'ROOM-001'], 'HK-004 holders (manager) pass without ROOM-001 on a record; ROOM-001 is checked for the work order record'],
  [/^POST \/commands\/ConvertRepairToWorkOrder$/, 'and', ['HK-001', 'ROOM-001'], ''],
  [/^POST \/commands\/MarkWebDepositPaid$/, 'and', ['WEB-001', 'PAY-002'], ''],
  [/^POST \/commands\/DecideExpense$/, 'and', ['EXP-002', 'EXP-003'], 'EXP-002 approve + EXP-003 post (approve above the limit goes to Owner)'],
  [/^POST \/commands\/ConfirmIdCapture$/, 'and', ['DOC-001', 'GST-003'], 'writes the full ID number'],
  [/^POST \/commands\/(SaveYield.*|SetYield.*|PublishYieldTariffs)$/, 'and', ['RPT-001', 'ADM-003'], 'Owner only through ADM-003'],
  [/^POST \/commands\/ProposeYieldPublish$/, 'and', ['RPT-001', 'APR-001'], 'APR-001 checked with amount 0'],
  [/^POST \/commands\/(SettleCheck|VoidBill)$/, 'and', ['FOL-002', 'PAY-001'], 'PAY-001 own-shift scope for cash'],
  [/^POST \/commands\/CommitImportBatch$/, 'and', ['IMP-001', 'IMP-003'], 'IMP-002 suffices for non-money masters; IMP-003 for money masters (both Owner only)'],
  [/^POST \/commands\/RecordReceipt$/, 'or', ['PAY-001', 'PAY-002'], 'payment = PAY-001, deposit = PAY-002; cash needs the open own shift'],
  [/^POST \/commands\/RequestAdjustment$/, 'or', ['FOL-003', 'PAY-003', 'FOL-005'], 'by kind: discount FOL-003 (limits), refund PAY-003, correction FOL-005; R cells create a request'],
  [/^POST \/commands\/ExecuteAdjustment$/, 'or', ['PAY-004'], 'refund execution; never the requester'],
  [/^POST \/commands\/DecideApproval$/, 'or', ['APR-001', 'PAY-005', 'SHF-002'], 'by kind: APR-001 (limit), PAY-005 company credit, SHF-002 cash difference; never the requester'],
  [/^POST \/commands\/TransitionHousekeepingTask$/, 'primary', ['HK-001'], 'plus HK-002/003/004 by transition'],
  [/^(GET|POST) \/(handover|commands\/(AcknowledgeHandoverNote|CompleteCarryItem|SubmitChecklistRun|SubmitHandoverNote|TickChecklistItem))/, 'or', ['HK-001', 'PAY-001', 'APR-001'], 'module access = any of board / payments / approve held at any scope; cash amounts need PAY-001 or RPT-002'],
  [/^(GET|POST) \/(guest-services\/(requests|lost-items)|commands\/(AssignServiceRequest|CancelServiceRequest|CompleteServiceRequest|LogLostItem|LogServiceRequest|StartServiceRequest))/, 'or', ['GST-002', 'HK-002', 'HK-003', 'ROOM-001'], 'front desk (GST-002) / supervisor (HK-002) / assigned housekeeping or maintenance (HK-003, ROOM-001 own records)'],
  [/^GET \/guest-services\/reports\//, 'or', ['GST-002', 'RPT-001'], '"report.operations or front-office rights" (GuestServicesQueries.RequireReportAsync)'],
  [/^GET \/guest-services\/settings$/, 'or', ['GST-002', 'APR-001'], ''],
  [/^GET \/stock\//, 'or', ['RPT-002', 'HK-003'], 'RPT-002 at property scope = money view, HK-003 any scope = work view (no money)'],
  [/^GET \/expenses/, 'or', ['EXP-001', 'EXP-002', 'EXP-003'], 'rows filtered to what the role may see'],
  [/^GET \/admin\/(roles|exceptions)$/, 'or', ['ADM-001', 'ADM-002', 'ADM-003'], 'RequireAnyAsync'],
  [/^GET \/handover\/templates$/, 'or', ['APR-001'], 'recorded real answer: approve only'],
];
function semantics(key, uniqPerms) {
  for (const [re, mode, ids, note] of SEM) if (re.test(key)) return { mode, ids: ids.map(P), note };
  return { mode: 'or', ids: uniqPerms, note: '' };
}

const segs = s => s.split('/').filter(Boolean);
function realFor(row) {
  let best = null, bs = -1;
  for (const pattern of Object.keys(J.routes)) {
    const [m, p] = pattern.split(' '); if (m !== row.method) continue;
    const ps = segs(p), rs = segs(row.route.replace(/\{(\w+)(:\w+)?\}/g, '{$1}')); if (ps.length !== rs.length) continue;
    let ok = true, score = 0; for (let i = 0; i < ps.length; i++) { if (ps[i] === rs[i]) score += 2; else if (rs[i].startsWith('{') && ps[i].startsWith(':')) score += 1; else { ok = false; break; } }
    if (ok && score > bs) { best = pattern; bs = score; }
  }
  return best;
}
const hold1 = (role, id) => { const base = id.replace(':VIEW', ''); const c = v1[base]?.[role]; if (!c || c.mode === '-') return null;
  const viewReq = id.endsWith(':VIEW') || perms[base]?.kind === 'VIEW'; if (!viewReq && !['A', 'L', 'R'].includes(c.mode)) return null; return c; };
const hold2 = (role, id) => { const c = v2[id]?.row?.[role]; return c && c.mode !== '-' ? c : null; };
const better = (a, b) => !a ? b : !b ? a : (a.blocked && !b.blocked) ? b : (!a.blocked && b.blocked) ? a : ord[b.mode] < ord[a.mode] ? b : a;
const worse = (a, b) => (a.blocked && !b.blocked) ? a : (!a.blocked && b.blocked) ? b : ord[b.mode] > ord[a.mode] ? b : a;
const fmt = m => ROLES.filter(r => m[r]).map(r => { const c = m[r]; return r + (c.mode !== 'A' ? '(' + c.mode + ')' : '') + (c.scope !== 'P' ? '·' + c.scope : '') + (c.blocked ? '*' : ''); }).join(' ');

const out = [];
for (const r of rows) {
  const key = r.method + ' ' + r.route;
  const allGate = [...new Set(r.gatePerms)];
  const baseUniq = [...new Set(allGate.map(p => p.replace(':VIEW', '')))];
  const sem = baseUniq.length > 1 ? semantics(key, allGate) : { mode: 'or', ids: allGate, note: '' };
  const idsBase = sem.ids.map(p => p.replace(':VIEW', ''));
  const usesFor = id => r.usesObj.filter(u => u.perm.replace(':VIEW', '') === id.replace(':VIEW', ''));
  const cellOf = (role, id) => { let best = null; for (const u of usesFor(id)) best = better(best, passes(role, id.includes(':VIEW') ? id : u.perm, u)); return best; };
  const stat = {};
  for (const role of ROLES) {
    const cells = sem.ids.map(id => cellOf(role, id));
    let c = null;
    if (sem.mode === 'and') c = cells.every(x => x) ? cells.reduce(worse) : null;
    else if (sem.mode === 'primary') c = cells[0];
    else c = cells.reduce((a, x) => better(a, x), null);
    if (c) stat[role] = c;
  }
  const pat = realFor(r);
  let code = stat, src = 'static';
  if (pat) { src = 'recorded real answer'; code = {}; for (const role of ROLES) { const a = J.roles[role].reads[pat]; if (a === '200' || a === '404') code[role] = stat[role] ? { ...stat[role], blocked: false } : { mode: 'A', scope: 'P', blocked: false, observed: true }; } }
  const codeHas = ROLES.filter(x => code[x] && !code[x].blocked);
  const row = { ...r, sem, code, codeSrc: src, realPattern: pat };
  // v1
  const gateV1 = idsBase.filter(p => perms[p]);
  const v1Covered = CORE.has(r.mod) && sem.ids.length > 0 && gateV1.every(p => v1[p]) && !NOTMATRIX_ROUTE.test(key);
  row.v1Covered = v1Covered;
  if (v1Covered) {
    const has = role => sem.mode === 'and' ? sem.ids.every(id => hold1(role, id)) : sem.mode === 'primary' ? !!hold1(role, sem.ids[0]) : sem.ids.some(id => hold1(role, id));
    row.v1Roles = ROLES.filter(has);
    const extra = codeHas.filter(x => !row.v1Roles.includes(x)), missing = row.v1Roles.filter(x => !codeHas.includes(x));
    row.v1Diff = { extra, missing };
    row.v1Verdict = extra.length ? 'Matrix says no but code grants' : missing.length ? 'Matrix says yes but code refuses' : 'Match';
  } else row.v1Verdict = 'Not in matrix';
  // v2
  const v2ids = r.v2 ? [...r.v2.matchAll(/PERM-[A-Z]+-\d{3}/g)].map(x => x[0]) : [];
  row.v2Ids = v2ids; row.v2Guess = !!(r.v2 && /\?/.test(r.v2));
  if (v2ids.length && v2ids.every(id => v2[id])) {
    row.v2Roles = ROLES.filter(x => hold2(x, v2ids[0]));
    const extra = codeHas.filter(x => !row.v2Roles.includes(x)), missing = row.v2Roles.filter(x => !codeHas.includes(x));
    row.v2Diff = { extra, missing };
    row.v2Verdict = extra.length ? 'Matrix says no but code grants' : missing.length ? 'Matrix says yes but code refuses' : 'Match';
  } else row.v2Verdict = v1Covered ? 'Unchanged (v2 §2.5)' : V2_UNCHANGED.has(r.mod) ? 'Unchanged (v2 §2.3)' : 'Not in matrix';
  row.verdict = v1Covered ? row.v1Verdict : (v2ids.length && v2[v2ids[0]]) ? row.v2Verdict + ' (v2 proposal)' : 'Not in matrix';
  out.push(row);
}
function judge(x) {
  const n = [];
  if (!x.session) n.push('Anonymous: ' + (/bootstrap|setup/.test(x.route) ? 'loopback-only (server PC)' : /public\/booking/.test(x.route) ? 'public booking listener, rate-limited' : 'by design'));
  if (x.session && x.gatePerms.length === 0) n.push('Session-only by design (no PERM)');
  const uniq = [...new Set(x.gatePerms.map(p => p.replace(':VIEW', '')))];
  if (uniq.length > 1) n.push(`${x.sem.mode.toUpperCase()}: ${x.sem.note || 'several ids'}`);
  if (Object.values(x.code).some(c => c.mode === 'L')) n.push('limit mode L: over the limit -> APPROVAL_REQUIRED');
  if (Object.values(x.code).some(c => c.mode === 'R')) n.push('request-only mode R: refused with APPROVAL_REQUIRED unless the command itself creates a request');
  if (ROLES.some(r => x.code[r]?.blocked)) n.push('* scoped-only (' + ROLES.filter(r => x.code[r]?.blocked).join(' ') + '): passes only when the code names an own record/shift');
  if (x.revealPerms.includes('PERM-GST-002')) n.push('GST-002 = phone/e-mail reveal, not a gate');
  if (x.v2Guess) n.push('v2 id is my reading');
  if (!x.v1Covered && x.session && x.gatePerms.length && !/^(Payroll|Accounting|Compliance|WebBooking|Documents|Groups|Customers|Invoices|Ota|Import)$/.test(x.mod)) n.push('borrowed id ' + uniq.map(p => p.replace('PERM-', '')).join('/') + ' (v2 §1.2)');
  if (x.code.TAD && x.gatePerms.length && !/^PERM-(ADM-00[123]|IMP-001|HR-00[14])/.test(x.gatePerms[0])) n.push('TAD reaches this: check DEC-057/058');
  if (x.codeSrc !== 'static') n.push('roles = real-server answers');
  return n;
}
for (const x of out) x.judgement = judge(x);
const q = s => '"' + String(s ?? '').replace(/"/g, '""') + '"';
fs.writeFileSync(OUT + '/A05_route_table.json', JSON.stringify(out.map((x, i) => ({ n: i + 1, method: x.method, route: x.route, module: x.mod, session: x.session, src: x.src, gate: x.gatePerms, reveal: x.revealPerms, semantics: x.sem.mode, semIds: x.sem.ids, rolesSeed: fmt(x.code), rolesSource: x.codeSrc, v1Roles: x.v1Covered ? x.v1Roles.join(' ') : null, v2: x.v2Ids, v2Roles: x.v2Roles ?? null, v1Verdict: x.v1Verdict, v2Verdict: x.v2Verdict, verdict: x.verdict, judgement: x.judgement, v1Diff: x.v1Diff ?? null, v2Diff: x.v2Diff ?? null, codeRoles: Object.fromEntries(ROLES.filter(r => x.code[r]).map(r => [r, x.code[r]])) })), null, 1));
fs.writeFileSync(OUT + '/A05_route_table.csv', ['n,method,route,module,session,gate_perms,reveal_perms,semantics,roles_seed,roles_source,matrix_v1_roles,v2_perm,v2_roles,verdict,judgement'].concat(out.map((x, i) =>
  [i + 1, x.method, x.route, x.mod, x.session, x.gatePerms.join(' '), x.revealPerms.join(' '), x.sem.mode, fmt(x.code), x.codeSrc, x.v1Covered ? x.v1Roles.join(' ') : '', x.v2Ids.join(' '), (x.v2Roles ?? []).join(' '), x.verdict, x.judgement.join('; ')].map(q).join(','))).join('\n'));
const tally = {}; for (const x of out) tally[x.verdict] = (tally[x.verdict] ?? 0) + 1;
console.log('VERDICTS', JSON.stringify(tally));
