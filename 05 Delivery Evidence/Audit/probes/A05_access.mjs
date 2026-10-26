// A05 step 3: join routes with the RBAC seeds, matrix v1 (parsed from the markdown) and the v2 proposal (parsed from the markdown).
// Output: evidence/A05/A05_seed_grants.csv, A05_seed_vs_v1.csv, A05_route_access.json/csv
// Usage: $env:PATH="E:\Tools\nodejs;$env:PATH"; node "05 Delivery Evidence\Audit\probes\A05_access.mjs"
import fs from 'node:fs';
import { ROOT, MIG } from './A05_lib.mjs';
const OUT = ROOT + '/05 Delivery Evidence/Audit/evidence/A05';
export const ROLES = ['OWN', 'MGR', 'REC', 'CSH', 'ACC', 'SUP', 'HKP', 'MNT', 'TAD'];

// ---------- seeds ----------
const seedFiles = fs.readdirSync(MIG).filter(f => /rbac_seed\.sql$/.test(f)).sort();
export const perms = {};          // id -> {kind,isMoney,what,file}
export const grants = {};         // role -> perm -> {mode,scope,limit,unit}
const roleRows = [];
for (const f of seedFiles) {
  const sql = fs.readFileSync(MIG + '/' + f, 'utf8');
  for (const m of sql.matchAll(/\(\s*'(PERM-[A-Z]+-\d{3})'\s*,\s*'([^']*)'\s*,\s*'(VIEW|ACTION)'\s*,\s*([01])\s*\)/g)) perms[m[1]] = { what: m[2], kind: m[3], isMoney: +m[4], file: f };
  for (const m of sql.matchAll(/\(\s*'([A-Z]{3})'\s*,\s*'(PERM-[A-Z]+-\d{3})'\s*,\s*'([ALRVM])'\s*,\s*'([PSAO])'\s*,\s*(NULL|\d+)\s*,\s*(NULL|'[A-Z]+')\s*\)/g)) {
    (grants[m[1]] ??= {})[m[2]] = { mode: m[3], scope: m[4], limit: m[5] === 'NULL' ? null : +m[5], unit: m[6] === 'NULL' ? null : m[6].replace(/'/g, ''), file: f };
  }
  for (const m of sql.matchAll(/\(\s*'([A-Z]{3})'\s*,\s*N'([^']*)'\s*,\s*'(PIN|PASSWORD)'\s*,\s*([01])\s*,\s*(\d+)\s*\)/g)) roleRows.push({ role: m[1], name: m[2], cred: m[3], ownerOnlyAssign: +m[4] });
}
export { roleRows };
const permIds = Object.keys(perms).sort();
const nGrants = Object.values(grants).reduce((a, g) => a + Object.keys(g).length, 0);

// ---------- matrix v1 ----------
const v1md = fs.readFileSync(ROOT + '/02 Planning/HCC_RBAC_Matrix_v1.md', 'utf8').split('\n');
export const v1 = {};            // perm -> role -> {mode,scope}
for (const line of v1md) {
  if (!/^\|\s*PERM-[A-Z]+-\d{3}\s*\|/.test(line)) continue;
  const c = line.split('|').slice(1, -1).map(s => s.trim());
  if (c.length < 11 || !/^PERM-[A-Z]+-\d{3}$/.test(c[0])) continue;
  if (/^PERM-SOD/.test(c[0])) continue;
  const row = {};
  ROLES.forEach((r, i) => row[r] = cell(c[2 + i]));
  v1[c[0]] = row;
}
function cell(t) {
  const s = t.replace(/[¹²]/g, '').replace(/\(M4.*\)/, '').trim();
  if (/^status card/.test(s)) return { mode: '-', scope: '-', note: 'status card (dashboard only, no page)' };
  const m = /^([ALRVM])(?:·([PSAO]))?/.exec(s);
  return m ? { mode: m[1], scope: m[2] ?? 'P' } : { mode: '-', scope: '-' };
}

// ---------- matrix v2 proposal ----------
const v2md = fs.readFileSync(ROOT + '/02 Planning/HCC_RBAC_Matrix_v2_Proposal.md', 'utf8').split('\n');
export const v2 = {};            // perm -> {what, row:{role->{mode,scope}}}
let hdr = null;
for (const line of v2md) {
  if (/^\|\s*Perm\s*\|/.test(line)) { hdr = line.split('|').slice(1, -1).map(s => s.trim()).slice(2); continue; }
  if (!hdr || !/^\|\s*PERM-[A-Z]+-\d{3}\s*\|/.test(line)) continue;
  const c = line.split('|').slice(1, -1).map(s => s.trim());
  if (c.length < 2 + hdr.length) continue;
  const row = {}; hdr.forEach((r, i) => row[r] = cell(c[2 + i]));
  v2[c[0]] = { what: c[1], row };
}

// ---------- seed vs v1 cell diff ----------
const diffRows = [];
for (const p of Object.keys(v1)) for (const r of ROLES) {
  const s = grants[r]?.[p] ?? null; const x = v1[p][r];
  const sCell = s ? s.mode + (s.scope !== 'P' ? '·' + s.scope : '') : '-';
  const xCell = x.mode === '-' ? '-' : x.mode + (x.scope !== 'P' ? '·' + x.scope : '');
  if (sCell !== xCell) diffRows.push({ perm: p, role: r, seed: sCell, v1: xCell, note: x.note ?? '' });
}
const v1Missing = permIds.filter(p => !v1[p] && /^PERM-(RES|RATE|GST|STY|FOL|PAY|SHF|HK|ROOM|EXP|CLS|APR|RPT|ADM)-/.test(p));
const v1NotSeeded = Object.keys(v1).filter(p => !perms[p]);

// ---------- helpers ----------
const ord = { A: 0, L: 1, R: 2, V: 3, M: 4 };
/** How a grant counts for a use: returns null or {mode,scope} */
export function passes(role, permId, use) {
  const base = permId.replace(':VIEW', ''); const g = grants[role]?.[base]; if (!g) return null;
  const kind = perms[base]?.kind; const viewReq = permId.endsWith(':VIEW') || kind === 'VIEW';
  if (!viewReq && !['A', 'L', 'R'].includes(g.mode)) return null;
  const hasFn = use && /^(Has|Can)/.test(use.fn);
  const scopeAny = use && (use.scope === 'any' || use.scope === 'record' || use.fn === 'expr' || (hasFn && use.scope === 'none'));
  if (g.scope !== 'P' && !scopeAny) return { mode: g.mode, scope: g.scope, blocked: true };
  return { mode: g.mode, scope: g.scope, blocked: false };
}
export const cellStr = c => c ? c.mode + (c.scope && c.scope !== 'P' ? '·' + c.scope : '') + (c.blocked ? '✗' : '') : '';
export function fmtRoles(map) { // role -> cell|null  => "OWN MGR(L) REC(R) CSH·S"
  return ROLES.filter(r => map[r]).map(r => { const c = map[r]; return r + (c.mode !== 'A' ? '(' + c.mode + ')' : '') + (c.scope !== 'P' ? '·' + c.scope : '') + (c.blocked ? '*' : ''); }).join(' ');
}

// ---------- route access ----------
const routes = JSON.parse(fs.readFileSync(OUT + '/A05_routes.json', 'utf8'));
const MOD = f => f.split(':')[0].replace(/Endpoints\.cs$/, '').replace(/\.cs$/, '');
const CORE = new Set(['Inventory', 'Reservations', 'Stays', 'Housekeeping', 'Billing', 'Payments', 'Closing', 'Expenses', 'GuestId', 'Tax', 'Access']);
const DEDICATED = new Set(['Payroll', 'Accounting', 'Compliance', 'WebBooking', 'Documents', 'Groups', 'Customers', 'Invoices', 'Ota', 'Import']);
const BORROWED = new Set(['Pos', 'Stock', 'Maintenance', 'GuestServices', 'Messaging', 'Handover', 'Yield', 'Configuration']);

// v2 permission(s) that describe each route's action (ordered rules, first match wins). Judgement calls are marked '?'.
const V2RULES = [
  // POS
  [/^POST \/commands\/(CreateOutlet|SaveMenuItem)$/, 'PERM-POS-006'], [/^POST \/commands\/DecideVoid$/, 'PERM-POS-003'], [/^POST \/commands\/(VoidBill|VoidCheckItem)$/, 'PERM-POS-003?'],
  [/^POST \/commands\/SettleCheck$/, 'PERM-POS-005'], [/^(POST \/commands\/(OpenCheck|AddCheckItem|SetCheckItemQty|SendKot|AdvanceKot|SetMenuItemAvailability))$/, 'PERM-POS-002'],
  [/^GET \/pos\/(till|close-blockers)$/, 'PERM-POS-004?'], [/^GET \/pos\//, 'PERM-POS-001'],
  // Stock
  [/^POST \/commands\/StockCountDecide$|^POST \/commands\/StockItemCreate$/, 'PERM-STK-003'], [/^POST \/commands\/(StockItemSet|StockSettingsSet)$/, 'PERM-STK-005'], [/^POST \/commands\/Stock/, 'PERM-STK-002'], [/^GET \/stock\//, 'PERM-STK-001 (+STK-004 for money fields)'],
  // Maintenance
  [/^POST \/commands\/SaveAmc$/, 'PERM-MNT-005'], [/^GET \/maintenance\/amcs$/, 'PERM-MNT-005'], [/^POST \/commands\/(AssignWorkOrder|BlockWorkOrderRoom|VerifyWorkOrder|CancelWorkOrder|GeneratePmWorkOrders|ReceiveSpare|ConvertRepairToWorkOrder)$/, 'PERM-MNT-003'],
  [/^POST \/commands\/(CreateWorkOrder|CompleteWorkOrder|HoldWorkOrder|StartWorkOrder|AddWorkOrderPart|ResumeWorkOrder|ReturnWorkOrder)/, 'PERM-MNT-002'], [/^GET \/maintenance\/report$/, 'PERM-MNT-004'], [/^GET \/maintenance\//, 'PERM-MNT-001 (+MNT-004 for money fields)'], [/^POST \/commands\/.*(WorkOrder|Amc|Spare|Pm)/, 'PERM-MNT-002?'],
  // Guest services
  [/^POST \/commands\/(AssignServiceRequest)$/, 'PERM-GSV-005'], [/^POST \/commands\/(ResolveComplaint|OfferRecoveryDiscount|RecordComplaintFollowUp|SaveGuestServiceSettings|DisposeLostItem)$/, 'PERM-GSV-003?'],
  [/^POST \/commands\/(LogServiceRequest|StartServiceRequest|CompleteServiceRequest|CancelServiceRequest|LogComplaint|UpdateComplaint|LogLostItem|ClaimLostItem|HandOverLostItem)$/, 'PERM-GSV-002'], [/^GET \/guest-services\/reports\//, 'PERM-GSV-001 (+GSV-004?)'], [/^GET \/guest-services\//, 'PERM-GSV-001'],
  // Messaging
  [/^POST \/commands\/(UpdateMessageTemplate|UpdateMessageRule)$/, 'PERM-MSG-003'], [/^POST \/commands\/SetMessagingConsent$/, 'PERM-MSG-005?'], [/^POST \/commands\/(SendMessage|RetryMessage|RunMessagingTriggers|CancelMessage)$/, 'PERM-MSG-002'], [/^GET \/messaging\//, 'PERM-MSG-001'],
  // Handover
  [/^POST \/commands\/(ReopenChecklistRun|SaveHandoverTemplate|ToggleHandoverTemplate|AcknowledgeHandoverNote)$/, 'PERM-HND-003'], [/^POST \/commands\/(SubmitHandoverNote|SubmitChecklistRun|TickChecklistItem|CompleteCarryItem)$/, 'PERM-HND-002'], [/^GET \/handover\//, 'PERM-HND-001 (+HND-004 for cash amounts)'],
  // Yield
  [/^POST \/commands\/ProposeYieldPublish$/, 'PERM-YLD-002'], [/^POST \/commands\/(DecideYieldProposal|PublishYieldTariffs)$/, 'PERM-YLD-003'], [/^POST \/commands\/.*Yield/, 'PERM-YLD-002?'], [/^GET \/yield\//, 'PERM-YLD-001'],
  // Configuration
  [/^GET \/configuration\/(modules|values)$/, 'v2: not specified (session-only)'], [/^GET \/configuration\//, 'PERM-CFG-001'], [/^POST \/commands\/(SetSetting|RevertSetting|WithdrawScheduledSetting|ImportConfiguration|DecideSettingRequest)$|^POST \/configuration\/import-plan$/, 'PERM-CFG-002 (non-money) / CFG-003 (money groups)'],
  // OTA
  [/^GET \/ota\/(commission|reconciliation)$/, 'PERM-OTA-004'], [/^GET \/ota\//, 'PERM-OTA-001'], [/^POST \/commands\/(SaveChannel|SetChannelStatus|QueueSimulatedMessage)$/, 'PERM-OTA-005? (connect/credentials)'], [/^POST \/commands\/(PushChannel|SetStopSell|PushRates|StopSell).*/, 'PERM-OTA-003?'], [/^POST \/commands\/.*Channel/, 'PERM-OTA-002'],
  // Web booking
  [/^GET \/web-booking\//, 'PERM-WEB-001'], [/^POST \/commands\/(ConfirmWebRequest|DeclineWebRequest|CreateWebDepositLink)$/, 'PERM-WEB-002'], [/^POST \/commands\/MarkWebDepositPaid$/, 'PERM-WEB-002 (+PAY-002 core)'],
  // Documents
  [/^GET \/documents\//, 'PERM-DOC-001'], [/^POST \/commands\/CaptureIdDocument$/, 'PERM-DOC-002'], [/^POST \/commands\/(ConfirmIdCapture|RejectIdCapture)$/, 'PERM-DOC-003'], [/^POST \/commands\/(ViewIdCaptureImage|RevealIdCaptureNumber)$/, 'PERM-DOC-004'], [/^POST \/commands\/PurgeIdCaptures$/, 'PERM-DOC-005'],
  // Groups
  [/^GET \/groups\/\{groupId\}\/statement$/, 'PERM-GRP-004'], [/^GET \/groups/, 'PERM-GRP-001'], [/^POST \/commands\/RecordGroupReceipt$/, 'PERM-GRP-005'], [/^POST \/commands\/(DecideGroupAttrition|DecideCompanyLimit|SetCompanyLimit)$/, 'PERM-GRP-003'], [/^POST \/commands\/(CreateGroup|UpdateGroup|SetGroupRouting|AttachGroupMember|DetachGroupMember|ApplyGroupRouting|RerouteGroupCharge|TransferGroupMember|SettleGroup|SaveCompany|RequestGroupAttrition)$/, 'PERM-GRP-002'],
  // CRM
  [/^POST \/commands\/(ProposeCustomerMerge|DecideCustomerMerge)$/, 'PERM-CRM-003'], [/^POST \/commands\/(AddCustomerNote|AddCustomerTag|RemoveCustomerTag|SaveCustomerPreferences|SetCustomerConsent|RevealCustomerContact)/, 'PERM-CRM-002'], [/^GET \/customers\/(merges|duplicates)$/, 'PERM-CRM-003?'], [/^GET \/customers/, 'PERM-CRM-001 (+CRM-004 for lifetime spend)'],
  // Invoices
  [/^POST \/commands\/IssueInvoice$/, 'PERM-INV-002'], [/^POST \/commands\/DecideCreditNote$/, 'PERM-INV-003'], [/^POST \/commands\/RequestCreditNote$/, 'PERM-INV-005'], [/^GET \/(invoices|credit-notes)\/.*document$/, 'PERM-INV-002'], [/^GET \/(invoices|credit-notes|credit-note-requests)/, 'PERM-INV-001'],
];
function v2Perms(key) { for (const [re, p] of RULES()) if (re.test(key)) return p; return null; }
function RULES() { return V2RULES; }

const rows = [];
for (const r of routes) {
  const key = r.method + ' ' + r.route.replace('/api/v1', '').replace(/\{(\w+)(:\w+)?\}/g, '{$1}');
  const keyV2 = r.method + ' ' + r.route.replace('/api/v1', '');
  const mod = MOD(r.file);
  const permIdsAll = [...new Set([...r.permsDirect, ...r.permsVia])];
  // uses by perm
  const byPerm = {};
  for (const u of r.uses) (byPerm[u.perm] ??= []).push(u);
  const gate = [], reveal = [];
  for (const p of Object.keys(byPerm)) {
    const us = byPerm[p];
    const g = us.filter(u => !/^(Has|Can)/.test(u.fn)); const h = us.filter(u => /^(Has|Can)/.test(u.fn));
    if (g.length) gate.push({ perm: p, uses: g }); if (h.length && !g.length) reveal.push({ perm: p, uses: h });
  }
  const gatePerms = gate.length ? gate : reveal;             // wrappers that turn HasAsync into a refusal (stock/maintenance) show up as reveal-only
  const roleCells = {}, roleCellsAll = {};
  for (const role of ROLES) {
    let best = null; const perPerm = [];
    for (const gp of gatePerms) {
      let pbest = null;
      for (const u of gp.uses) { const c = passes(role, gp.perm, u); if (c && (!pbest || (pbest.blocked && !c.blocked) || (pbest.blocked === c.blocked && ord[c.mode] < ord[pbest.mode]))) pbest = c; }
      perPerm.push(pbest);
      if (pbest && (!best || (best.blocked && !pbest.blocked) || (best.blocked === pbest.blocked && ord[pbest.mode] < ord[best.mode]))) best = pbest;
    }
    if (best) roleCells[role] = best;
    if (gatePerms.length && perPerm.every(x => x)) {
      const worst = perPerm.reduce((a, c) => (a.blocked && !c.blocked) ? a : (!a.blocked && c.blocked) ? c : (ord[c.mode] > ord[a.mode] ? c : a));
      roleCellsAll[role] = worst;
    }
  }
  const revealPerms = Object.keys(byPerm).filter(p => !gatePerms.some(g => g.perm === p));
  const hasMoneyPerm = gatePerms.some(g => perms[g.perm.replace(':VIEW', '')]?.isMoney);
  rows.push({
    method: r.method, route: r.route.replace('/api/v1', ''), key, mod, session: r.session, src: r.file,
    gatePerms: gatePerms.map(g => g.perm), revealPerms, permsAll: permIdsAll,
    reachAny: roleCells, reachAll: roleCellsAll, moneyGate: hasMoneyPerm,
    v2: V2RULES.find(([re]) => re.test(keyV2))?.[1] ?? null,
    uses: r.uses.map(u => `${u.perm.replace('PERM-', '')}:${u.fn}/${u.scope}${u.amount ? '/$' : ''}`), usesObj: r.uses,
    calls: r.calls,
  });
}

// ---------- write ----------
const q = s => '"' + String(s ?? '').replace(/"/g, '""') + '"';
fs.writeFileSync(OUT + '/A05_seed_grants.csv', ['role,permission,mode,scope,limit,unit,permission_kind,is_money,seed_file'].concat(
  ROLES.flatMap(r => Object.entries(grants[r] ?? {}).map(([p, g]) => [r, p, g.mode, g.scope, g.limit ?? '', g.unit ?? '', perms[p]?.kind ?? '?', perms[p]?.isMoney ?? '?', g.file].join(',')))).join('\n'));
fs.writeFileSync(OUT + '/A05_seed_vs_v1.csv', ['permission,role,seed,matrix_v1,note'].concat(diffRows.map(d => [d.perm, d.role, d.seed, d.v1, q(d.note)].join(','))).join('\n'));
fs.writeFileSync(OUT + '/A05_route_access.json', JSON.stringify(rows, null, 1));
fs.writeFileSync(OUT + '/A05_route_access.csv', ['method,route,module,session,gate_perms,reveal_perms,reach_any_of_gate_perms,reach_all_gate_perms,money_gate,v2_perm,calls'].concat(rows.map(x =>
  [x.method, x.route, x.mod, x.session, x.gatePerms.join(' '), x.revealPerms.join(' '), fmtRoles(x.reachAny), fmtRoles(x.reachAll), x.moneyGate, x.v2 ?? '', x.calls.join(' | ')].map(q).join(','))).join('\n'));
const summary = {
  seedFiles, permissionsSeeded: permIds.length, grantsSeeded: nGrants, rolesSeeded: roleRows,
  v1Rows: Object.keys(v1).length, v1CellsCompared: Object.keys(v1).length * 9, v1CellDiffs: diffRows, v1PermsMissingFromSeed: v1NotSeeded, seededCoreIdsNotInV1: v1Missing,
  v2Rows: Object.keys(v2).length, v2Ids: Object.keys(v2),
  codePermsNotSeeded: [...new Set(rows.flatMap(r => r.permsAll.map(p => p.replace(':VIEW', ''))))].filter(p => !perms[p]),
  seededPermsNeverChecked: permIds.filter(p => !rows.some(r => r.permsAll.some(x => x.replace(':VIEW', '') === p))),
};
fs.writeFileSync(OUT + '/A05_access_summary.json', JSON.stringify(summary, null, 1));
console.log(JSON.stringify({ ...summary, rolesSeeded: summary.rolesSeeded.length }, null, 1));
