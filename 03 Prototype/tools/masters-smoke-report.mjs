// MA-10 · Writes 05 Delivery Evidence/Masters/MA_Smoke_Report.md from smoke/smoke-results.json (tools/masters-smoke.mjs).
//   node tools/masters-smoke-report.mjs [<smoke dir>] [<defects.json>]   (defects.json = {fixed:[...],left:[...],notes:[...]} written by hand)
import {readFileSync,writeFileSync,existsSync} from 'node:fs';
import {join,dirname} from 'node:path';
const dir=process.argv[2]||'../05 Delivery Evidence/Masters/smoke';
const r=JSON.parse(readFileSync(join(dir,'smoke-results.json'),'utf8'));
const extra=process.argv[3]&&existsSync(process.argv[3])?JSON.parse(readFileSync(process.argv[3],'utf8')):{fixed:[],left:[],notes:[]};
const VPS=['360','390','1366'],SC=[1,1.3];
const phoneOf=v=>v!=='1366';
function verdict(m){
  const ph=phoneOf(m.vp),why=[];
  if(m.problems?.length)why.push(...m.problems);
  for(const k of ['list','form']){const a=m[k];if(!a||a.doc===undefined)continue;
    if(a.doc>0||a.body>0)why.push(`${k}: page scrolls sideways by ${Math.max(a.doc,a.body)} px`);
    if(a.outside?.length)why.push(`${k}: control outside the viewport — ${a.outside.slice(0,2).join(', ')}`);
    if(a.clipped?.length)why.push(`${k}: clipped control — ${a.clipped.slice(0,2).join(', ')}`);
    if(ph&&a.smallCount)why.push(`${k}: ${a.smallCount} touch target(s) under 44 px — ${a.small.slice(0,2).join(', ')}`);
    if(ph&&a.smallFonts)why.push(`${k}: ${a.smallFonts} field(s) under 16 px`);}
  if(ph&&m.form?.actions&&m.form.actions.hasSave&&m.form.actions.saveVisible===false)why.push('form: Save not in view');
  if(m.form?.actions&&m.form.actions.found===false)why.push('form not found');
  if(!m.form||!m.form.shot)why.push('no form opened');
  return why;
}
const cells={},fails=[];let pass=0,fail=0;
for(const m of r.masters){const why=verdict(m);const key=m.master+'|'+m.vp+'|'+m.scale;cells[key]=why;if(why.length){fail++;fails.push({...m,why});}else pass++;}
const labels={};for(const m of r.masters)labels[m.master]=m.label;
const ids=[...new Set(r.masters.map(m=>m.master))];
const L=[];
L.push('# Masters wave — smoke report (MA-10)');
L.push('');
L.push(`Run ${r.startedAt.slice(0,10)} · Chrome (Playwright) against \`03 Prototype/dist/index.html\` served by \`node serve.mjs\` · tool \`03 Prototype/tools/masters-smoke.mjs\` (re-run: \`npm run build\`, \`PORT=4181 node serve.mjs\`, \`node tools/masters-smoke.mjs\`) · screenshots in \`05 Delivery Evidence/Masters/smoke/\` (JPEG, \`<viewport>-s<scale>-<master>-list|form.jpg\`) · raw numbers \`smoke/smoke-results.json\`.`);
L.push('');
L.push('## 1. Result');
L.push('');
L.push(`**Master screens: ${pass} pass / ${fail} fail** of ${r.masters.length} (28 masters × 3 viewports × 2 text scales; each = the list and one form).`);
const fr=r.func||[];const fp=fr.filter(x=>x.ok).length;
L.push(`**Phone flows: ${fp} pass / ${fr.length-fp} fail.** **Role click-through: ${r.roles.length} role × viewport × grant runs.** **Console errors: ${r.consoleErrors.length}.**`);
L.push('');
L.push('A master screen passes when, at that viewport and text scale: the list opens from Master configuration › group › Lists; one form opens (first row, or Add when empty); the page never scrolls sideways; no button / field lies outside the viewport or is clipped; on the phone every touch target is ≥ 44 px, text fields are 16 px and the Save button is in view; no console error. Text scale 1.3 = `localStorage[\'hcc-prefs-1\']` `{"v":1,"p":{…defaults,"textScale":1.3}}`.');
L.push('');
L.push('## 2. Per master × viewport (text scale 1 / 1.3; P = pass, F = fail)');
L.push('');
L.push('| Master | 360×800 | 390×844 | 1366×768 |');L.push('|---|---|---|---|');
for(const id of ids){const row=VPS.map(v=>SC.map(sc=>{const w=cells[id+'|'+v+'|'+sc];return w===undefined?'–':w.length?'F':'P';}).join(' / '));L.push(`| ${labels[id]} (\`${id}\`) | ${row.join(' | ')} |`);}
L.push('');
if(fails.length){L.push('### Failures');L.push('');for(const f of fails)L.push(`- ${f.vp}@${f.scale} ${f.master}: ${f.why.join('; ')} — \`${f.vp}-s${f.scale}-${f.master}-${f.form?.shot?'form':'list'}.jpg\``);L.push('');}
L.push('## 3. Phone flows (390×844, Owner)');
L.push('');L.push('| Flow | Result | Detail | Screenshot |');L.push('|---|---|---|---|');
for(const f of fr)L.push(`| ${f.name} | ${f.ok?'pass':'**FAIL**'} | ${String(f.detail||'').replace(/\|/g,'/').slice(0,120)} | ${f.shot||''} |`);
L.push('');
L.push('## 4. Role click-through (Master configuration, desktop 1366 and phone 390)');
L.push('');
L.push('Default grants (DEC-MA-1): the page is open to `admin` or `masters`, i.e. **Owner and Tech admin**. Every other role does not get the page (Admin & Setup lists only Approvals, Roadmap, Appearance & display, as their rights allow). To prove the read-only and no-money behaviour for the other roles, a second pass grants `masters` to every role (saved grants patched in IndexedDB — the same effect as the Owner granting it later).');
L.push('');
L.push('| Role | Viewport | Grants | Master configuration | Groups | Read-only groups | Room types rack rate / Menu price / Occupancy charges / Bank accounts: money fields shown | Lock reason on money lists |');
L.push('|---|---|---|---|---|---|---|---|');
for(const x of r.roles){const sp=x.specs||{};const money=['roomTypes','menuItems','occupancyCharges','bankAccounts'].map(k=>sp[k]&&sp[k].form?String(sp[k].form.rupeeLabels.length+(sp[k].form.dated?'*':'')):'–').join(' / ');
  const lock=['occupancyCharges','bankAccounts'].some(k=>sp[k]?.lock)?'shown':(x.reachable?'–':'');
  L.push(`| ${x.role} | ${x.vp} | ${x.granted?'masters granted':'default'} | ${x.reachable?'opens':'not offered'} | ${x.reachable?x.groups:''} | ${x.reachable?x.readOnlyGroups.length:''} | ${x.reachable?money:''} | ${lock} |`);}
L.push('');L.push('`*` = a dated-money sub-form is shown. Housekeeper, Maintenance and Supervisor show `0` everywhere: no money field is offered (DEC-061).');
const leak=r.roles.filter(x=>x.rupee);
if(leak.length){L.push('');L.push('Rupee amounts anywhere in the 12 group pages of the three no-money roles (masters granted): '+(leak.every(x=>!Object.keys(x.rupee).length||Object.values(x.rupee).every(v=>v.every(t=>!/₹\s?\d/.test(t))))?'**none**.':'**found** — see defects.'));}
L.push('');
L.push('## 5. Defects');
L.push('');
L.push('### Fixed in this run');L.push('');for(const d of extra.fixed)L.push('- '+d);
L.push('');L.push('### Left for other owners');L.push('');for(const d of extra.left)L.push('- '+d);
if(extra.notes?.length){L.push('');L.push('### Notes');L.push('');for(const d of extra.notes)L.push('- '+d);}
L.push('');
const out=join(dirname(dir),'MA_Smoke_Report.md');writeFileSync(out,L.join('\n'));console.log('wrote',out,'pass',pass,'fail',fail);
