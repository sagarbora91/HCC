// A05 debugging aid: node A05_trace.mjs <Class.Method> <PERM-id>  -> prints the call chain from the method to the place the PERM id appears.
import { indexSources } from './A05_lib.mjs';
const [, , start, perm] = process.argv;
const ix = indexSources();
const NOISE = new Set(['if', 'for', 'while', 'switch', 'foreach', 'using', 'lock', 'catch', 'return', 'nameof', 'typeof', 'new', 'await']);
function has(m) {
  if (m.body.includes(perm)) return true;
  for (const x of m.bodyB.matchAll(/\b([A-Z]\w+)\.([A-Z]\w*)\b/g)) { const v = ix.consts.get(x[1] + '.' + x[2]); if (v && v.split('|').includes(perm)) return true; }
  return false;
}
const seen = new Set();
function dfs(m, chain, depth) {
  const key = m.file + m.line; if (seen.has(key)) return false; seen.add(key);
  const here = chain.concat([m.cls + '.' + m.name + ' (' + m.file.split('/').slice(-2).join('/') + ':' + m.line + ')']);
  if (has(m)) { console.log(here.join('\n  -> ')); return true; }
  if (depth === 0) return false;
  const calls = new Set();
  for (const x of m.bodyB.matchAll(/(?<![\.\w])([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(/g)) if (!NOISE.has(x[1])) calls.add(m.cls + '.' + x[1]);
  for (const x of m.bodyB.matchAll(/\b([A-Z]\w+)\s*\.\s*([A-Za-z_]\w*)\s*(?:<[^<>()]*>)?\s*\(/g)) if (ix.classNames.has(x[1])) calls.add(x[1] + '.' + x[2]);
  for (const c of calls) for (const h of ix.byClassName.get(c) ?? []) if (h !== m && dfs(h, here, depth - 1)) return true;
  return false;
}
const ms = ix.byClassName.get(start) ?? [];
console.log('candidates', ms.length);
for (const m of ms) { seen.clear(); if (!dfs(m, [], 4)) console.log('not reached from', m.cls + '.' + m.name, m.line); }
