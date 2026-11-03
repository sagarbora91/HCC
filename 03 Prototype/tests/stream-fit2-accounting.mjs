// FIT-2 accounting lane: the Accounting page keeps all 24 views as FitTabs (More menu comes from the shell), and its 4-figure StatStrip is
// compact + collapseOnTablet (remembered per page). Layout only: no font size, no money logic touched.
// Integrator: rewritten as register({test,assert}) — the lane's version ran at import and called process.exit(), which ended `npm test`
// part-way (every stream file sorted after it never ran, and the totals line was never printed).
import {readFileSync} from 'node:fs';
export function register({test,assert}){
  const src=readFileSync('src/features/accounting/Accounting.tsx','utf8');
  test('[FIT2-ACCOUNTING] StatStrip compact + collapseOnTablet + page key; FitTabs used',()=>{
    assert.ok(/<StatStrip money compact collapseOnTablet page="accounting"/.test(src));
    assert.ok(/<FitTabs<Tab> page="accounting"/.test(src));
  });
  test('[FIT2-ACCOUNTING] 24 distinct views, each rendered',()=>{
    const ids=(src.match(/ACC_TAB_IDS=\[([^\]]+)\]/)||[])[1]?.split(',').map(x=>x.trim().replace(/'/g,''))||[];
    assert.equal(ids.length,24);assert.equal(new Set(ids).size,24);
    for(const id of ids)assert.ok(src.includes(`tab==='${id}'`),'view rendered: '+id);
  });
  test('[FIT2-ACCOUNTING] no own sideways tab bar, no font-size in the page',()=>{
    assert.ok(!/overflow-x/.test(src));assert.ok(!/font-size/.test(src));
  });
}
