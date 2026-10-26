// A05 step 7: fill probes/A05_report_template.md with the generated tables -> agents/A05_access_table.md
import fs from 'node:fs';
import { ROOT } from './A05_lib.mjs';
const O = ROOT + '/05 Delivery Evidence/Audit/evidence/A05/';
const t = JSON.parse(fs.readFileSync(O + 'A05_route_table.json', 'utf8'));
const diff = JSON.parse(fs.readFileSync(O + 'A05_review_diff.json', 'utf8'));
const sweep = JSON.parse(fs.readFileSync(O + 'A05_sweep_hk_mnt_money.json', 'utf8'));
let tpl = fs.readFileSync(ROOT + '/05 Delivery Evidence/Audit/probes/A05_report_template.md', 'utf8');
const esc = s => String(s ?? '').replace(/\|/g, '/').replace(/\n/g, ' ');
const short = p => p.replace('PERM-', '');
const gateText = x => {
  if (!x.session) return /bootstrap|setup/.test(x.route) ? 'loopback only' : /public\/booking/.test(x.route) ? 'anonymous (public, rate-limited)' : 'anonymous';
  if (!x.gate.length) return 'session-only';
  const ids = (x.semIds && x.semIds.length ? x.semIds : x.gate).map(short);
  const all = [...new Set(x.gate.map(short))];
  let s;
  if (x.semantics === 'primary') { s = ids[0]; const rest = all.filter(p => p !== ids[0]); if (rest.length) s += ' (step-up ' + rest.join('/') + ')'; }
  else { s = [...new Set(ids)].join(x.semantics === 'and' ? ' + ' : ' / '); if (new Set(ids).size > 1) s += x.semantics === 'and' ? ' [all]' : ' [any]'; }
  if (x.reveal.length) s += ' · mask ' + x.reveal.map(short).join(' ');
  return s;
};
const v2Text = x => x.v2Roles ? `${x.v2.map(short).join('/')}: ${x.v2Roles.join(' ') || 'nobody'}` : /^Unchanged/.test(x.v2Verdict) ? 'unchanged' : '—';
const v1Text = x => x.v1Roles !== null && x.v1Roles !== undefined ? (x.v1Roles || 'nobody') : 'no row';
const verdictShort = v => v.replace(' (v2 proposal)', ' (v2)');
const NOTES = [
  [/^POST \/commands\/(SaveCompany|SetCompanyLimit)$/, 'A05-01: Manager sets a credit limit directly up to 10,000 rupees; G11 says a direct change is Owner-only'],
  [/^POST \/commands\/DecideCompanyLimit$/, 'Manager deciding a request matches the prototype (GRP_LIMIT_DECIDE needs approve); see A05-01 for the direct set'],
  [/^GET \/(cashier\/collections|closing\/snapshots|closing\/records|cashbook|reports\/daily)/, 'A05-02: matrix gives the Cashier V·S; every RPT-002 gate refuses the Cashier'],
  [/^GET \/accounting\/(days|trial-balance|mapping|locks|chart)/, 'A05-03: Cashier holds ACC-001 (whole books)'],
  [/^GET \/guest-services\/reports\//, 'A05-04: Reception reaches this through GST-002 although RPT-001 REC is a dash (G8)'],
  [/^(GET \/configuration\/requests|POST \/commands\/DecideSettingRequest)$/, 'A05-05: Manager reaches the one Manager-approver request'],
  [/^POST \/commands\/(SetSetting|ImportConfiguration|RevertSetting|WithdrawScheduledSetting)$/, 'A05-06: TAD changes non-money settings at once (GSTIN, ID retention)'],
  [/^POST \/commands\/(SetCredential|SetUserEnabled)$/, 'A05-07: guard looks at roles, not at money exceptions'],
  [/^POST \/commands\/(GrantException|RevokeException)$/, 'A05-08: TAD R cell = refusal with no request record'],
];
const OVERRIDE = [[/^POST \/commands\/SetCompanyLimit$/, 'Matrix says no but code grants (G11, see A05-01)']];
t.forEach(x => { const key = x.method + ' ' + x.route; for (const [re, n] of NOTES) if (re.test(key)) x.judgement.push(n); for (const [re, v] of OVERRIDE) if (re.test(key)) x.verdict = v; });
const lines = ['| # | Method | Path (under /api/v1) | Session | PERM gate | Roles granted by seed | Matrix v1 says | v2 proposal says | Verdict | Needs judgement |', '|---|---|---|---|---|---|---|---|---|---|'];
t.forEach(x => lines.push(`| ${x.n} | ${x.method} | \`${esc(x.route)}\` | ${x.session ? 'yes' : 'no'} | ${esc(gateText(x))} | ${esc(x.rolesSeed || '—')}${x.rolesSource === 'static' ? '' : ' †'} | ${esc(v1Text(x))} | ${esc(v2Text(x))} | ${esc(verdictShort(x.verdict))} | ${esc(x.judgement.filter(j => !/^roles = real/.test(j)).join('; '))} |`));
const tally = {}; for (const x of t) tally[x.verdict] = (tally[x.verdict] ?? 0) + 1;
const v1t = {}; for (const x of t) v1t[x.v1Verdict] = (v1t[x.v1Verdict] ?? 0) + 1;
const tallyRows = Object.entries(tally).sort((a, b) => b[1] - a[1]).map(([k, v]) => `| ${k} | ${v} |`).join('\n');
// c1 table
const maskBy = { Handover: 'HandoverViews.Note(seesCash): amount null unless PAY-001 or RPT-002 held (HandoverQueries.cs:22-24, HandoverViews.cs:35-39)', Maintenance: 'money = HasAsync(RPT-002, Property) (MaintenanceQueries.cs:30-31); cost fields `money ? x : null` (:56, :61, :102, :119, :139)', Stock: 'money = HasAsync(RPT-002, Property) (StockQueries.cs:22); cost fields `money ? x : null` (:72, :94-95; StockCommands.cs:234, :290)', Payroll: 'own record only (DEC-061): lines/payslips filtered to the caller, LOCKED runs only (PayrollQueries.cs:105-107, :250); not hotel money' };
const c1 = ['| Route | Roles of SUP/HKP/MNT that reach it | Money-like response fields (`?` = nullable, `!` = always present) | Why it is safe / not safe |', '|---|---|---|---|'];
for (const r of sweep.filter(r => r.nf)) c1.push(`| ${esc(r.route)} | ${r.who.join(' ')} | ${esc(r.fields.slice(0, 5).join(' '))}${r.fields.length > 5 ? ` … (${r.fields.length} fields)` : ''} | ${esc(maskBy[r.mod])} |`);
// review diff table
const rd = ['| Review row | Route | Review lists | A05 derives | Reading |', '|---|---|---|---|---|'];
const reading = { 39: 'transitive: shift-difference branch calls SHF-002 (CashierService); review omits it', 166: 'transitive: HK-003 for the "work" transitions; review writes "task work" in words', 174: 'review says "commit permission per master"; A05 names IMP-002 / IMP-003', 242: 'GST-002 is the phone/e-mail mask (GuestContactMask), not a gate', 312: 'same: GST-002 mask', 313: 'same: GST-002 mask', 357: 'same: GST-002 mask', 367: 'REVIEW IS WRONG: ProposeAsync checks RPT-001 + APR-001 only (YieldService.cs:264-266), no ADM-003', 368: 'REVIEW IS WRONG: DecideAsync checks ADM-003 only (YieldService.cs:295), no RPT-001' };
for (const d of diff.permDiff) rd.push(`| #${d.n} | ${d.route.replace('/api/v1', '')} | ${esc(d.review.replace(/PERM-/g, ''))} | ${esc([...d.a05Direct, ...d.a05Via].map(short).join(' '))} | ${esc(reading[d.n] ?? '')} |`);
const nOnlyA05 = diff.onlyInA05.map(s => '`' + s.replace('/api/v1', '') + '`').join(', ');
tpl = tpl.replace('{{ROUTE_TABLE}}', lines.join('\n')).replace('{{TALLY}}', tallyRows).replace('{{C1_TABLE}}', c1.join('\n')).replace('{{REVIEW_DIFF}}', rd.join('\n')).replace('{{ONLY_IN_A05}}', nOnlyA05)
  .replace('{{V1TALLY}}', Object.entries(v1t).map(([k, v]) => `${k}: ${v}`).join(' · '));
fs.writeFileSync(ROOT + '/05 Delivery Evidence/Audit/agents/A05_access_table.md', tpl);
console.log('written', tpl.length, 'chars;', t.length, 'rows');
