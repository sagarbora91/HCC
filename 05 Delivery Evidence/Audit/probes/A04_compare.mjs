// A04 — compares my independent figures (evidence/A04/recompute.json) with the server kernel's (evidence/A04/kernel.json).
// Run from the repo root after A04_recompute.mjs and the A04_kernel probe:  node "05 Delivery Evidence/Audit/probes/A04_compare.mjs"
import {readFileSync,writeFileSync} from 'node:fs';
const E='05 Delivery Evidence/Audit/evidence/A04/';
const mine=JSON.parse(readFileSync(E+'recompute.json','utf8')),k=JSON.parse(readFileSync(E+'kernel.json','utf8'));
const F=['payableDays','earned','basic','hra','washing','special','ot','gross','pfWages','pf','pfEr','esicWages','esic','esicEr','pt','advance','deductions','net','ctc','esicCovered'];
const rows=[];let bad=0;
const cmp=(label,a,b)=>{const d=F.filter(f=>a[f]!==b[f]).map(f=>`${f}: mine ${a[f]} kernel ${b[f]}`);if(d.length)bad++;rows.push({label,match:d.length===0,diffs:d,net:{mine:a.net,kernel:b.net}});};
for(const c of mine.cases.filter(c=>c.mine))cmp(c.case,c.mine,k.cases[c.case]);
const fm=mine.cases.find(c=>c.case.includes('F&F'));
cmp('F&F final-month line',fm.line,k.fnf.line);
for(const [f,a,b] of [['advanceOutstanding',fm.outstandingAdvance,k.fnf.advanceOutstanding],['elEncashment',fm.elEncashment,k.fnf.elEncashment],['earnings',fm.earnings,k.fnf.earnings],['recoveries',fm.recoveries,k.fnf.recoveries],['net',fm.net,k.fnf.net]]){
  const ok=a===b;if(!ok)bad++;rows.push({label:'F&F '+f,match:ok,diffs:ok?[]:[`mine ${a} kernel ${b}`]});}
mine.edges.forEach((e,i)=>cmp(e.label,e.mine,k.edges[i].line));
writeFileSync(E+'compare.json',JSON.stringify({mismatches:bad,rows},null,1)+'\n');
for(const r of rows)console.log(r.match?'MATCH   ':'MISMATCH',r.label,r.diffs.join('; '));
console.log(bad?`${bad} mismatch(es)`:'all figures match');
