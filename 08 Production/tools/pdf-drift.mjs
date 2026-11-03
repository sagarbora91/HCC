// F7-R1 · PDF golden drift check (RPT-ARCHITECTURE §9): every tests/Hcc.Reporting.Tests/Goldens/<id>.pdf is read back (text runs + page count —
// MinimalPdfWriter output: uncompressed streams, "Td (…) Tj") and compared with <id>.pdf.txt. Text, not bytes: an ETP / PDFsharp renderer swapped in
// behind IReportRenderer must keep these green. Usage (from "08 Production"): node tools/pdf-drift.mjs [dir]   → exit 1 on drift.
import fs from 'node:fs';import path from 'node:path';import {fileURLToPath} from 'node:url';
const PROD=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const dir=process.argv[2]??path.join(PROD,'tests','Hcc.Reporting.Tests','Goldens');
const WIN={0x95:'•',0x96:'–',0x97:'—'};
export function pdfText(buf){
  const s=Buffer.from(buf).toString('latin1');
  const pages=(s.match(/\/Type \/Page \//g)??[]).length;
  const lines=[...s.matchAll(/Td \(((?:\\.|[^\\)])*)\) Tj/g)].map(m=>m[1].replace(/\\([0-7]{3}|.)/g,(_,e)=>e.length===3?(WIN[parseInt(e,8)]??String.fromCharCode(parseInt(e,8))):e));
  return {pages,lines};
}
let bad=0,n=0;
for(const f of fs.readdirSync(dir).filter(f=>f.endsWith('.pdf'))){
  n++;const {pages,lines}=pdfText(fs.readFileSync(path.join(dir,f)));
  const got=`pages: ${pages}\n`+lines.join('\n')+'\n';
  const txt=path.join(dir,f+'.txt');
  if(!fs.existsSync(txt)){console.error(`${f}: no ${f}.txt golden`);bad++;continue;}
  const want=fs.readFileSync(txt,'utf8').replace(/\r\n/g,'\n');
  if(got!==want){const a=got.split('\n'),b=want.split('\n');const i=a.findIndex((x,k)=>x!==b[k]);console.error(`${f}: drift at line ${i+1}: got "${a[i]}" want "${b[i]}"`);bad++;}
}
console.log(`${n} PDF golden(s) checked, ${bad} drifted.`);process.exit(bad?1:0);
