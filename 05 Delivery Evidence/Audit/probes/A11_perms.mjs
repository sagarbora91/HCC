// A11 probe: every PERM-xxx-nnn id named in the audited documents (and ranges like PERM-HR-001…009) -> seeded in database/migrations/*.sql?
import fs from 'node:fs'; import path from 'node:path';
const ROOT=process.cwd(), OUT=path.join(ROOT,'05 Delivery Evidence','Audit','evidence','A11'); fs.mkdirSync(OUT,{recursive:true});
const DOCS=['HANDOFF.md','CLAUDE.md','08 Production/README.md','03 Prototype/src/clients/sync/README.md','04 Android/README.md','08 Production/deployment/windows/INSTALL.md','02 Planning/HCC_Decision_Register.md','02 Planning/HCC_Implementation_Backlog.md','02 Planning/RBAC_Mismatch_Rulings_Proposed.md','05 Delivery Evidence/CA_Question_Pack_2026-10-25.md'];
const mig=path.join(ROOT,'08 Production','database','migrations');
const seeded=new Set(); for(const f of fs.readdirSync(mig)) for(const m of fs.readFileSync(path.join(mig,f),'utf8').matchAll(/PERM-[A-Z]+-\d{3}[a-z]?/g)) seeded.add(m[0]);
const rows=[];
for(const d of DOCS){ const t=fs.readFileSync(path.join(ROOT,d),'utf8').split(/\r?\n/);
  t.forEach((ln,i)=>{
    for(const m of ln.matchAll(/PERM-([A-Z]+)-(\d{3})(?:\s*(?:…|\.\.\.|–|-|\/)\s*(\d{3}))?/g)){
      const pre=m[1], a=+m[2], b=m[3]?+m[3]:a;
      const ids=[]; if(m[3] && /…|\.\.\.|–/.test(m[0])) { for(let n=a;n<=b;n++) ids.push(`PERM-${pre}-${String(n).padStart(3,'0')}`);} else { ids.push(`PERM-${pre}-${m[2]}`); if(m[3]) ids.push(`PERM-${pre}-${m[3]}`); }
      for(const id of ids) rows.push({doc:d,line:i+1,id,seeded:seeded.has(id)});
    } }); }
const bad=rows.filter(r=>!r.seeded);
fs.writeFileSync(path.join(OUT,'perms_in_docs.json'),JSON.stringify({seededCount:seeded.size,checked:rows.length,notSeeded:bad},null,1));
console.log('seeded ids',seeded.size,'checked refs',rows.length,'not seeded',bad.length); for(const b of bad) console.log(`${b.doc}:${b.line} ${b.id}`);
