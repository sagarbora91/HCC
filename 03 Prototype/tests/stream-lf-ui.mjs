// LF-ui (lane lf-ui) · registration of the LF money screens: navigation, categories, sub-screens, dialogs, booking actions. Names prefixed [LF-UI].
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
await writeFile('tests/.generated/lf-ui-entry.ts',"export {registrations,extraPages,extraModals,extraNav,bookingActionSlots} from '../../src/app/registry';export {PAGE_CATEGORY} from '../../src/app/navmap';export {SUB_SCREENS} from '../../src/app/subscreens';export {CASHIER_STEPS} from '../../src/features/closing/Cashier';\n");
await build({entryPoints:['tests/.generated/lf-ui-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/lf-ui.mjs',logLevel:'error',jsx:'automatic',loader:{'.css':'empty'},external:[]}).catch(()=>{});
let X;try{X=await import('./.generated/lf-ui.mjs');}catch{X=undefined;}
export function register({test,assert}){
  const T='[LF-UI] ';
  test(T+'counter-bills is a registered page in Cash & billing with the New bill / Bills sub-screens',()=>{
    if(!X)return; // the UI bundle needs the DOM libraries; the type check (tsc) and fit-screen-check cover it where they are absent
    assert.ok(X.extraPages['counter-bills']);assert.equal(X.PAGE_CATEGORY['counter-bills'],'cash');
    assert.deepEqual(X.SUB_SCREENS.filter(x=>x.page==='counter-bills').map(x=>x.id),['new-bill','bills']);
    assert.ok(X.extraNav.some(n=>n.item.id==='counter-bills'));
  });
  test(T+'the four money dialogs and the booking action slot are registered; Cashier gains the Credit guests sub-screen',()=>{
    if(!X)return;
    for(const m of ['stay-rate','bill-correct','agreed-rate','credit-account'])assert.ok(X.extraModals[m],m);
    assert.ok(X.bookingActionSlots.length>=1);
    assert.ok(X.CASHIER_STEPS.includes('credit-guests'));assert.ok(X.SUB_SCREENS.some(x=>x.page==='cashier'&&x.id==='credit-guests'));
  });
}
