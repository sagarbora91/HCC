// Lead: collects every finding row (ID, Sev, Area, File:line, Impact, Fix, Hours) from all agent files into evidence/lead/findings_all.tsv and prints severity totals.
import fs from 'node:fs';
const files=fs.readdirSync('agents').filter(f=>/^A\d/.test(f)).map(f=>'agents/'+f);
const rows=[];const tot={};
for(const f of files){const lines=fs.readFileSync(f,'utf8').split(/\r?\n/);let cols=null;
 for(const l of lines){ if(/^\|\s*ID\s*\|\s*Sev\s*\|\s*Area\s*\|/.test(l)){cols=l.split('|').map(s=>s.trim());continue;}
  if(cols){ if(!l.startsWith('|')){cols=null;continue;} const c=l.split('|').map(s=>s.trim()); if(!/^A\d{2}[ab]?-\d+/.test(c[1]))continue;
   const g=n=>c[cols.indexOf(n)]||''; const sev=c[2].replace(/\*/g,'').split(/[ (]/)[0]; tot[sev]=(tot[sev]||0)+1;
   rows.push([c[1],sev,c[3],c[4],g('Impact'),g('Fix'),g('Hours'),g('Conf')].map(s=>s.replace(/\t/g,' ')).join('\t'));}}}
fs.writeFileSync('evidence/lead/findings_all.tsv','ID\tSev\tArea\tFile\tImpact\tFix\tHours\tConf\n'+rows.join('\n')+'\n');
console.log(rows.length,'findings',JSON.stringify(tot));
