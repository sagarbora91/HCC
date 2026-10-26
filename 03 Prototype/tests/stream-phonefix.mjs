// W05 phone fixes (W05-1…6, AC-03, AC-04, DEC-060). Names prefixed [PF].
import {build} from 'esbuild';
import {writeFile,readFile} from 'node:fs/promises';
await writeFile('tests/.generated/pf-nav-entry.ts',"export {navFilterModules} from '../../src/app/navmap';export {navItems} from '../../src/app/nav';");
await build({entryPoints:['tests/.generated/pf-nav-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/pf-nav.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'}});
const N=await import('./.generated/pf-nav.mjs');
export function register({test,M,assert}){
  test('[PF] W05-1 compliance calendar is in navigation for Owner and Accountant only',()=>{const s=M.seed();
    for(const u of s.users.filter(u=>u.enabled)){
      const shown=N.navFilterModules(s,N.navItems,u.id).filter(n=>M.can(s,u.id,n.p)).some(n=>n.id==='compliance');
      assert.equal(shown,u.role==='Owner'||u.role==='Accountant',u.role);}});
  test('[PF] AC-04 sheet form submit clears the dirty flag before the child handler runs (capture phase)',async()=>{
    const src=await readFile('src/app/ModalHost.tsx','utf8');
    assert.ok(/onSubmitCapture=\{\(\)=>setDirty\(false\)\}/.test(src));assert.ok(!/ onSubmit=\{\(\)=>setDirty/.test(src));
    const app=await readFile('src/app/App.tsx','utf8');
    assert.ok(/setError\(''\);dirty\.current=false;/.test(app),'run() success clears dirty');});
  test('[PF] AC-03 count-sheet formula: opening + cash in - cash out = expected, restaurant cash included',()=>{
    for(const sc of ['S0','S1']){const s=M.loadScenario(sc);const ex=M.shiftExpected(s);
      assert.equal(ex.opening+M.shiftCashIn(ex)-(ex.refunds+ex.returns+ex.expenses),ex.expected);}});
  const has=async(f,re,msg)=>assert.ok(re.test(await readFile(f,'utf8')),msg||f);
  test('[PF] W05-2..5 phone pages reuse the desktop exports and compliance data',async()=>{
    await has('src/mobile/admin/Reports.tsx',/saveBinaryFile\([^)]*reportXlsx\(/,'W05-2 Excel');
    await has('src/features/accounting/Mobile.tsx',/<TallyExport\/>/,'W05-3 Tally');
    await has('src/mobile/admin/Closing.tsx',/<DayPackButton date=\{s\.date\}\/>/,'W05-4 day pack');
    await has('src/mobile/admin/Closing.tsx',/go\('cashbook'\)/,'W05-4 cash book link');
    await has('src/mobile/frontdesk/Home.tsx',/cmpHasAccess\(s,uid\).*DUE THIS WEEK|DUE THIS WEEK/s,'W05-5 due card');
    const s=M.loadScenario('S1');assert.ok(Array.isArray(M.cmpDueThisWeek(s)));});
  test('[PF] W05-6 phone shell: chips, date inputs and breadcrumb chips are 48 px; labels 16 px',async()=>{
    const css=await readFile('src/styles/z-touch.css','utf8');
    assert.ok(/\.m-shell \.m-chips button[^}]*min-height:var\(--hcc-touch-primary,48px\)/.test(css));
    assert.ok(/\.m-shell \.m-crumbs \.crumbs button\{min-height:var\(--hcc-touch-primary,48px\)/.test(css));
    assert.ok(/input\[type=date\][^}]*min-height:var\(--hcc-touch-primary,48px\)/.test(css));
    assert.ok(/\.cb-sums span[^}]*font-size:16px/.test(css));});
  test('[PF] DEC-060 coarse-pointer density tokens: dense 44, primary 48, fine pointer untouched',async()=>{
    const css=await readFile('src/styles/z-tablet.css','utf8');
    assert.ok(/--hcc-touch-dense:\s*44px/.test(css)&&/--hcc-touch-primary:\s*48px/.test(css));
    const i=css.indexOf('@media screen and (pointer: coarse)');assert.ok(i>0);
    const block=css.slice(i);assert.ok(/min-height: var\(--hcc-touch-dense\)/.test(block)&&/min-height: var\(--hcc-touch-primary\)/.test(block));
  });
  test('[PF] V11 S4 fixes: demo pill and tab labels 16 px, sheet chips 48 px, tile badge room, CGST/SGST span, back after role switch',async()=>{
    const m=await readFile('src/styles/mobile.css','utf8'),z=await readFile('src/styles/z-touch.css','utf8');
    assert.ok(/\.m-demo-pill\{[^}]*font-size:16px/.test(m)&&/\.m-tabbar button\{[^}]*font-size:16px/.test(m));
    assert.ok(!/\.m-demo-pill\{font-size:1[0-5]px/.test(z));
    assert.ok(/\.modal\.m-sheet \.m-chips button\{min-height:var\(--hcc-touch-primary,48px\);font-size:16px\}/.test(z));
    assert.ok(/\.tile:has\(\.tile-badge\)\{padding-right:5\d+px\}/.test(await readFile('src/styles/home.css','utf8')));
    assert.ok(/repeat\(auto-fit,minmax\(172px/.test(await readFile('src/styles/reservations.css','utf8')));
    assert.ok(/data-label="CGST"><span>/.test(await readFile('src/mobile/money/Checkout.tsx','utf8')));
    assert.ok(/catOk/.test(await readFile('src/app/App.tsx','utf8')));});
}
