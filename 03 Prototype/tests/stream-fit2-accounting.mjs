// FIT-2 accounting lane: the Accounting page keeps all 21 views as FitTabs (More menu comes from the shell), and its 4-figure StatStrip is
// compact + collapseOnTablet (remembered per page). Layout only: no font size, no money logic touched.
import {readFileSync} from 'node:fs';
const src=readFileSync('src/features/accounting/Accounting.tsx','utf8');
let pass=0,fail=0;const ok=(c,m)=>{if(c){pass++;}else{fail++;console.log('FAIL',m);}};
ok(/<StatStrip money compact collapseOnTablet page="accounting"/.test(src),'StatStrip compact + collapseOnTablet + page key');
const ids=(src.match(/ACC_TAB_IDS=\[([^\]]+)\]/)||[])[1]?.split(',').map(x=>x.trim().replace(/'/g,''))||[];
ok(ids.length===21&&new Set(ids).size===21,'21 distinct views');
for(const id of ids)ok(src.includes(`tab==='${id}'`),'view rendered: '+id);
ok(!/overflow-x/.test(src),'no own sideways tab bar');
ok(!/font-size/.test(src),'no font-size in page');
ok(/<FitTabs<Tab> page="accounting"/.test(src),'FitTabs used');
console.log(`stream-fit2-accounting: ${pass} passed, ${fail} failed`);process.exit(fail?1:0);
