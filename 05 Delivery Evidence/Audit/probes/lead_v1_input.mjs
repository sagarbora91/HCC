// Lead: builds probes/v1-input.md for the blind re-verifier: findings with Evidence/Fix/Hours/Conf stripped.
import fs from 'node:fs';
const files=process.argv.slice(2);const sonnet=new Set(['A01','A02','A05','A10','A11']);
const out=['# V1 input — findings with evidence stripped (lead-generated '+new Date().toISOString()+')','',
'| ID | Sev | Area | File:line | Claim (impact as stated by the agent) |','|---|---|---|---|---|'];
let n=0,low=0;const counts={};
for(const f of files){const lines=fs.readFileSync(f,'utf8').split(/\r?\n/);let inTable=false,cols=[];
 for(const l of lines){ if(/^\|\s*ID\s*\|\s*Sev\s*\|\s*Area\s*\|/.test(l)){inTable=true;cols=l.split('|').map(s=>s.trim());continue;}
  if(inTable){ if(!l.startsWith('|')){inTable=false;continue;} const c=l.split('|').map(s=>s.trim()); if(!/^A\d{2}[ab]?-\d+/.test(c[1]))continue;
   const id=c[1],sev=c[2],area=c[3],file=c[4],impact=c[cols.indexOf('Impact')]||'';const agent=id.split('-')[0];
   const s=sev.toLowerCase();counts[s]=(counts[s]||0)+1; if(s.startsWith('info'))continue;
   if(s.startsWith('low')){ if(!sonnet.has(agent))continue; low++; if(low%3!==1)continue; }
   out.push(`| ${id} | ${sev} | ${area} | ${file} | ${impact.replace(/\|/g,'/')} |`);n++;}}}
fs.writeFileSync('probes/v1-input.md',out.join('\n')+'\n');console.log('selected',n,'counts',JSON.stringify(counts));
