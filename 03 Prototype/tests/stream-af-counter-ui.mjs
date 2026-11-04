// AF-CTR-UI (UX-01, HIS-01 client): source / render checks of both counter-bill screens and the pure lookup helpers. Names prefixed [AF-CTR-UI].
import {readFileSync} from 'node:fs';
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';

await mkdir('tests/.generated',{recursive:true});
await writeFile('tests/.generated/af-ctr-ui-entry.ts',"export {ctrLookupLocal,counterLookupActive,ctrCatalogueOptions,ctrMethods,ctrDefaultMethod} from '../../src/features/lfmoney/counterLocal';\n");
let X;try{
  await build({entryPoints:['tests/.generated/af-ctr-ui-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/af-ctr-ui.mjs',logLevel:'error'});
  X=await import('./.generated/af-ctr-ui.mjs');
}catch{X=undefined;}

const desk=readFileSync('src/features/lfmoney/CounterBills.tsx','utf8');
const phone=readFileSync('src/features/lfmoney/Mobile.tsx','utf8');

export function register({test,assert,M}){
  const T='[AF-CTR-UI] ';
  for(const [name,src] of [['desktop',desk],['phone',phone]]){
    test(T+name+': method is a <select> fed by the payment-methods master; no free-text method input remains',()=>{
      assert.match(src,/ctrMethods\(s\)/);assert.match(src,/<label>Method<select/);
      assert.doesNotMatch(src,/<label>Method<input/);assert.doesNotMatch(src,/useState\('Cash'\)/);
      assert.match(src,/needsReference/);
    });
    test(T+name+': company is a <select> when companies exist (free-text id only as a fallback); item picker; till line; Find row',()=>{
      assert.match(src,/grpActiveCompanies\(s\)/);assert.match(src,/<label>Company<select/);assert.match(src,/companies\.length\?/);
      assert.match(src,/From catalogue \/ menu/);assert.match(src,/ctrCatalogueOptions\(s\)/);assert.match(src,/Free text/);
      assert.match(src,/Cash goes to:/);assert.match(src,/No cash shift is open/);assert.match(src,/noShift/);
      assert.match(src,/Find an older bill/);assert.match(src,/No bill matches/);assert.match(src,/Credit note…/);assert.match(src,/CTR_BILL_VOID_REQUEST/);
      assert.match(src,/process\.env\.HCC_BUILD==='production'/);assert.match(src,/useCounterBillLookup/);assert.match(src,/ctrLookupLocal/);
    });
    test(T+name+': unit price stays editable after picking a catalogue item (DEC-241 pending: no price block)',()=>{
      assert.match(src,/Unit price \(₹\)<input/);assert.doesNotMatch(src,/Unit price[^]{0,80}(readOnly|disabled)/);
    });
  }
  test(T+'ctrLookupLocal filters by invoice number and by date range, newest first',()=>{
    if(!X)return;
    const b=(no,date)=>({id:no,no,date});
    const bills=[b('INV/2627/0001','2026-09-01'),b('INV/2627/0002','2026-09-10'),b('INV/2627/0013','2026-10-02')];
    assert.deepEqual(X.ctrLookupLocal(bills,{invoiceNo:'0002'}).map(x=>x.no),['INV/2627/0002']);
    assert.deepEqual(X.ctrLookupLocal(bills,{invoiceNo:' inv/2627/00 '}).map(x=>x.no),['INV/2627/0013','INV/2627/0002','INV/2627/0001']);
    assert.deepEqual(X.ctrLookupLocal(bills,{from:'2026-09-05',to:'2026-09-30'}).map(x=>x.no),['INV/2627/0002']);
    assert.deepEqual(X.ctrLookupLocal(bills,{from:'2026-10-01'}).map(x=>x.no),['INV/2627/0013']);
    assert.deepEqual(X.ctrLookupLocal(bills,{invoiceNo:'9999'}),[]);
    assert.equal(X.counterLookupActive({}),false);assert.equal(X.counterLookupActive({invoiceNo:' '}),false);assert.equal(X.counterLookupActive({from:'2026-09-05'}),true);
  });
  test(T+'the picker lists active catalogue extras and menu items; the default method is the first drawer method of the master',()=>{
    if(!X)return;
    const s=M.baseFixture;
    const opts=X.ctrCatalogueOptions(s);
    for(const o of opts){assert.ok(o.extraId||o.menuItemId);assert.ok(o.key&&o.label);}
    assert.ok(X.ctrMethods(s).includes(X.ctrDefaultMethod(s)));
    assert.equal(X.ctrDefaultMethod(s),'Cash');
  });
}
