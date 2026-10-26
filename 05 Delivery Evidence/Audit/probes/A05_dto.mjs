// A05 sweep helper: for routes reachable by Housekeeping supervisor / Housekeeper / Maintenance (and Tech admin), walk the response DTO
// types and list money-like fields (and whether they are nullable = maskable). Also lists phone / e-mail / ID-document fields per DTO.
// Output: evidence/A05/A05_dto_money_fields.json, A05_dto_pii_fields.json
import fs from 'node:fs';
import { ROOT, SRC, walk, blank, match, splitArgs, indexSources } from './A05_lib.mjs';
const OUT = ROOT + '/05 Delivery Evidence/Audit/evidence/A05';
const rows = JSON.parse(fs.readFileSync(OUT + '/A05_route_access.json', 'utf8'));
const ix = indexSources();

// ---- type table: records (positional) and classes/records with init/set properties ----
const types = {};   // name -> [{type,name,file}]
for (const f of ix.files) {
  const o = fs.readFileSync(f, 'utf8'); const b = blank(o);
  for (const m of b.matchAll(/\b(?:record|class)\s+(?:struct\s+)?(\w+)\s*(<[^<>]*>)?\s*\(/g)) {
    const open = m.index + m[0].length - 1; const close = match(b, open); if (close < 0) continue;
    const parts = splitArgs(b, open + 1, close); const fields = [];
    for (const [s, e] of parts) {
      const t = o.slice(s, e).replace(/\[[^\]]*\]/g, '').trim(); const mm = /^(.*\S)\s+(\w+)(?:\s*=.*)?$/s.exec(t); if (mm) fields.push({ type: mm[1].replace(/\s+/g, ' '), name: mm[2] });
    }
    if (fields.length && !types[m[1]]) types[m[1]] = fields.map(x => ({ ...x, file: f.replace(SRC + '/', '') }));
  }
  for (const m of b.matchAll(/\b(?:record|class)\s+(\w+)\s*(?:<[^<>]*>)?[^{;(]*\{/g)) {
    const open = m.index + m[0].length - 1; const close = match(b, open); if (close < 0) continue;
    const body = o.slice(open, close); const fields = [];
    for (const p of body.matchAll(/public\s+(?:required\s+)?([\w<>\[\],\.\?\s]+?)\s+(\w+)\s*\{\s*get;/g)) fields.push({ type: p[1].trim(), name: p[2] });
    if (fields.length) types[m[1]] = [...(types[m[1]] ?? []), ...fields.map(x => ({ ...x, file: f.replace(SRC + '/', '') }))];
  }
}
const MONEY = /(Paise|Amount|Rate\b|Rates\b|Cost|Price|Fee|Charge|Total|Balance|Salary|Wage|\bPay\b|Pay[A-Z]|Commission|Deposit|Tax|Float|Expected|Counted|Difference|Revenue|Sales|Spend|Credit|Discount|Refund|Net\b|Gross|Value)/;
const NOTMONEY = /(RatePlan|RateCode|PayrollRun|Payee$|Taxable$)/;
const PII = /(Phone|Mobile|Email|WhatsApp|IdNumber|IdType|IdDoc|Aadhaar|Aadhar|Passport|\bPan\b|Pan[A-Z]|Dob|DateOfBirth|Address|Ifsc|IFSC|AccountNo|AccountNumber|BankAccount|Gstin|GstIn|Nationality|ImageRef|OcrText|ExtractedNumber|LastFour|Last4)/;

function unwrap(t) {
  let s = t.replace(/\?/g, '');
  for (let i = 0; i < 5; i++) { const m = /^(?:IReadOnlyList|IReadOnlyCollection|IEnumerable|List|IList|ICollection|Task|ValueTask|CommandResult|IReadOnlyDictionary|Dictionary|PagedResult|Page)<(.*)>$/.exec(s); if (!m) break; s = m[1]; const parts = splitTop(s); s = parts[parts.length - 1]; }
  return s.trim();
}
function splitTop(s) { const out = []; let d = 0, st = 0; for (let i = 0; i < s.length; i++) { if (s[i] === '<') d++; else if (s[i] === '>') d--; else if (s[i] === ',' && d === 0) { out.push(s.slice(st, i).trim()); st = i + 1; } } out.push(s.slice(st).trim()); return out; }
function walkType(name, path, depth, seen, out, rx, anyReg) {
  if (depth > 5 || seen.has(name)) return; const fs_ = types[name]; if (!fs_) return; seen.add(name);
  for (const f of fs_) {
    const nullable = /\?\s*$/.test(f.type);
    if (rx.test(f.name) && !NOTMONEY.test(f.name) && /(long|int|decimal|double|string|Paise|Money)/i.test(f.type)) out.push({ path: path + '.' + f.name, type: f.type, nullable, declared: name });
    const inner = unwrap(f.type); const base = inner.replace(/<.*/, '');
    if (types[base]) walkType(base, path + '.' + f.name, depth + 1, new Set(seen), out, rx, anyReg);
  }
}
function returnTypeOf(m) {
  const sig = m.sig; const rt = /(?:public|internal)\s+(?:static\s+)?(?:async\s+)?(.+?)\s+\w+\s*(?:<[^<>]*>)?\s*\($/s.exec(sig.slice(0, sig.indexOf('(') + 1));
  return rt ? unwrap(rt[1].trim()) : null;
}

const byRoute = {}; const pii = {};
const route2methods = JSON.parse(fs.readFileSync(OUT + '/A05_routes.json', 'utf8'));
for (const r of route2methods) {
  const key = r.method + ' ' + r.route.replace('/api/v1', '');
  const rts = new Set(), money = [], pi = [];
  for (const c of r.calls) {
    for (const m of ix.byClassName.get(c) ?? []) { const rt = returnTypeOf(m); if (rt) rts.add(rt); }
  }
  for (const rt of rts) { walkType(rt, rt, 0, new Set(), money, MONEY); walkType(rt, rt, 0, new Set(), pi, PII); }
  byRoute[key] = { returnTypes: [...rts], money, };
  pii[key] = { returnTypes: [...rts], pii: pi };
}
fs.writeFileSync(OUT + '/A05_dto_money_fields.json', JSON.stringify(byRoute, null, 1));
fs.writeFileSync(OUT + '/A05_dto_pii_fields.json', JSON.stringify(pii, null, 1));
fs.writeFileSync(OUT + '/A05_dto_types_index.json', JSON.stringify({ typeCount: Object.keys(types).length }, null, 1));
console.log('types indexed', Object.keys(types).length, '| routes with money-like fields in response:', Object.values(byRoute).filter(x => x.money.length).length, '| routes with PII-like fields:', Object.values(pii).filter(x => x.pii.length).length);
