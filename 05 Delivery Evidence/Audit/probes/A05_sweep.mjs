// A05 sweep c1: routes reachable by Supervisor / Housekeeper / Maintenance and the money-like fields of their response DTOs (from A05_dto.mjs).
// Output: evidence/A05/A05_sweep_hk_mnt_money.json. Run after A05_table.mjs and A05_dto.mjs.
import fs from 'node:fs';
import { ROOT } from './A05_lib.mjs';
const O = ROOT + '/05 Delivery Evidence/Audit/evidence/A05/';
const m = JSON.parse(fs.readFileSync(O + 'A05_dto_money_fields.json', 'utf8'));
const t = JSON.parse(fs.readFileSync(O + 'A05_route_table.json', 'utf8'));
const skip = /\.Total$|Count$|ExpectedVersion|\.Net\b|Counted|Expected\b/;      // page totals / version tokens / cash-count names are not money fields
const rows = [];
for (const x of t) {
  const who = ['SUP', 'HKP', 'MNT'].filter(r => x.codeRoles[r] && !x.codeRoles[r].blocked); if (!who.length) continue;
  const f = (m[x.method + ' ' + x.route]?.money ?? []).filter(f => !skip.test(f.path));
  rows.push({ route: x.method + ' ' + x.route, who, mod: x.module, nf: f.length, nonNull: f.filter(z => !z.nullable).length, fields: f.map(z => z.path.split('.').slice(-2).join('.') + (z.nullable ? '?' : '!')) });
}
fs.writeFileSync(O + 'A05_sweep_hk_mnt_money.json', JSON.stringify(rows, null, 1));
console.log(rows.length, 'routes reachable by SUP/HKP/MNT;', rows.filter(r => r.nf).length, 'with money-like response fields;', rows.filter(r => r.nonNull && r.mod !== 'Payroll').length, 'non-payroll routes with a non-nullable money-like field');
