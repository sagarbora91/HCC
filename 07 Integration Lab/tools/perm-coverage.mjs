// A2.2 · Static permission coverage of every mock command handler (03 Prototype is only read).
// For each command: coarse permission(s) passed to req(), fine-grained authorize() PERM IDs, intended PERM IDs from comments,
// day-closed guard, and amount validation. Writes evidence/permission-coverage.json and prints a markdown table.
// Usage: node "07 Integration Lab/tools/perm-coverage.mjs"
import fs from 'node:fs';import path from 'node:path';import {fileURLToPath} from 'node:url';
const LAB=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const SRC=path.resolve(LAB,'../03 Prototype/src/services/mock');
const files=[...fs.readdirSync(path.join(SRC,'handlers')).filter(f=>f.endsWith('.ts')&&f!=='index.ts').map(f=>path.join(SRC,'handlers',f)),path.join(SRC,'commands.ts')];
const START=/^\s*(?:const\s+([A-Z][A-Z0-9_]{2,})\s*:\s*Handler\s*=|([A-Z][A-Z0-9_]{2,})\s*:\s*(?:async\s*)?(?:k\b|\(\{|\(k)|case\s+'([A-Z][A-Z0-9_]{2,})'\s*:)/;
const rows=[];const registry=new Set();
for(const f of files){
  const lines=fs.readFileSync(f,'utf8').split(/\r?\n/);const rel=path.relative(path.resolve(SRC,'../../..'),f).replaceAll('\\','/');
  // file-level helpers that call req()/authorize(): name → perms
  const helpers={};
  lines.forEach((l,i)=>{const m=l.match(/^\s*(?:const|function)\s+([a-zA-Z]\w*)\s*[=(]/);if(!m||START.test(l))return;
    // single-line helper, or a multi-line function body up to the next line that is exactly '}'
    let body=l;if(/^function\s/.test(l)&&!/}\s*$/.test(l))for(let j=i+1;j<lines.length;j++){body+='\n'+lines[j];if(lines[j]==='}')break;}
    if(/req\(|authorize\(|FORBIDDEN|\bcan\(/.test(body))helpers[m[1]]={...perms(body),gate:/FORBIDDEN|\bcan\(/.test(body)};});
  const starts=[];lines.forEach((l,i)=>{const m=l.match(START);if(m)starts.push({i,name:m[1]||m[2]||m[3],builtin:!!m[3]});});
  starts.forEach((st,j)=>{
    const body=lines.slice(st.i,j+1<starts.length?starts[j+1].i:lines.length).join('\n');
    const p=perms(body);let gate=/FORBIDDEN|\bcan\(|hr[A-Z]\w*Can|Can[A-Z]\w*\(/.test(body);
    for(const [h,hp] of Object.entries(helpers))if(new RegExp('\\b'+h+'\\(').test(body)){p.req.push(...hp.req);p.authorize.push(...hp.authorize);gate||=hp.gate;}
    const intended=[...new Set((body.match(/PERM-[A-Z]+-\d{3}/g)||[]))];
    rows.push({command:st.name,file:rel,builtin:st.builtin,req:[...new Set(p.req)],authorize:[...new Set(p.authorize)],customGate:gate,intended,
      dayClosedGuard:/\bopen\(\)|financial\(/.test(body),amountCheck:/positive\(|isSafeInteger|Number\.isInteger/.test(body)});
  });
}
function perms(t){return {req:[...t.matchAll(/\breq\(([^)]*)\)/g)].flatMap(m=>[...m[1].matchAll(/'([a-z]+)'/g)].map(x=>x[1])),authorize:[...t.matchAll(/authorize\([^)]*?'(PERM-[A-Z]+-\d{3})'/g)].map(m=>m[1])};}
// Built-in switch cases that a stream handler replaces are dead code: mark them.
const handlerNames=new Set(rows.filter(r=>!r.builtin).map(r=>r.command));
for(const r of rows)r.shadowed=r.builtin&&handlerNames.has(r.command);
const live=rows.filter(r=>!r.shadowed);
// AI_ASK / AI_CLEAR are deliberately open to every enabled user (answers are filtered by the caller's permissions; history is per user).
const OPEN_BY_DESIGN=new Set(['AI_ASK','AI_CLEAR']);
for(const r of live)if(OPEN_BY_DESIGN.has(r.command))r.openByDesign=true;
const noPerm=live.filter(r=>!r.req.length&&!r.authorize.length&&!r.customGate&&!r.openByDesign);
fs.mkdirSync(path.join(LAB,'evidence'),{recursive:true});
fs.writeFileSync(path.join(LAB,'evidence/permission-coverage.json'),JSON.stringify({generated:new Date().toISOString(),commands:live.length,shadowedBuiltins:rows.filter(r=>r.shadowed).map(r=>r.command),withoutPermissionCheck:noPerm.map(r=>r.command),rows:live},null,1));
console.log('| Command | File | Coarse req() | authorize() | Custom gate | Intended PERM | Day-closed guard | Amount check |\n|---|---|---|---|---|---|---|---|');
for(const r of live.sort((a,b)=>a.file.localeCompare(b.file)||a.command.localeCompare(b.command)))
  console.log(`| ${r.command} | ${r.file.replace('src/services/mock/','')} | ${r.req.join(', ')||'**none**'} | ${r.authorize.join(', ')||'—'} | ${r.customGate?'yes':r.openByDesign?'any enabled user (by design)':'—'} | ${r.intended.join(', ')||'—'} | ${r.dayClosedGuard?'yes':'no'} | ${r.amountCheck?'yes':'—'} |`);
console.error(`\n${live.length} live commands; ${rows.filter(r=>r.shadowed).length} shadowed built-ins; without any permission check: ${noPerm.map(r=>r.command).join(', ')||'none'}`);
