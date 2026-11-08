// F8C guard support: stylesheets that gained NEW token-only blocks after the colour-token conversion (CP-3) was pinned.
// The CP-3 guards ([CT-A] / [CT-C]) prove the converted region equals the original; the F8C-added lines are cut out
// of the comparison here (pure insertions between F8C_BASE and HEAD) and checked separately for raw colour literals.
import {execFileSync} from 'node:child_process';
/** last commit before wave F8C (claude/f8c) touched these sheets */
export const F8C_BASE='007e345e^';
export const F8C_FILES=new Set(['src/styles/folio.css','src/styles/mobile-frontdesk.css']);
/** 1-based line numbers of lines inserted since F8C_BASE in `rel` (relative to 03 Prototype); throws if a hunk removes lines */
export function addedLines(rel){
  if(!F8C_FILES.has(rel))return new Set();
  const out=execFileSync('git',['diff','-U0',F8C_BASE,'HEAD','--',rel],{encoding:'utf8',maxBuffer:1<<26});
  const add=new Set();
  for(const m of out.matchAll(/^@@ -\d+(?:,(\d+))? \+(\d+)(?:,(\d+))? @@/gm)){
    if(m[1]!==undefined&&m[1]!=='0')throw new Error(rel+': F8C changed existing lines, only insertions are exempt');
    const start=+m[2],n=m[3]===undefined?1:+m[3];for(let i=0;i<n;i++)add.add(start+i);
  }
  return add;
}
/** the file text without the lines F8C inserted (identical text for every other file) */
export function withoutF8c(rel,text){
  const add=addedLines(rel);if(!add.size)return text;
  return text.split('\n').filter((_,i)=>!add.has(i+1)).join('\n');
}
/** just the inserted lines */
export function onlyF8c(rel,text){const add=addedLines(rel);return text.split('\n').filter((_,i)=>add.has(i+1)).join('\n');}
