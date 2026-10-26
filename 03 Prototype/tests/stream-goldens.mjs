// A08-04 / A03a-12 / A03b-07 · server golden files = this prototype. Every golden JSON the server tests read
// (08 Production/tests/**/Golden/*.json) must equal what its exporter produces from the CURRENT prototype; a prototype change that
// moves a figure fails here until the golden is regenerated (node "<exporter>") and the server tests that read it are updated
// (08 Production/tools/golden/check-goldens.mjs, golden-lib.mjs). Names prefixed [GOLDEN].
import fs from 'node:fs';
const PROD=new URL('../../08 Production/',import.meta.url);
export async function register({test,assert}){
  const {GOLDENS,checkGoldens}=await import(new URL('tools/golden/check-goldens.mjs',PROD).href);
  const {goldenDiff}=await import(new URL('tools/golden/golden-lib.mjs',PROD).href);
  const results=await checkGoldens();
  for(const r of results)test(`[GOLDEN] ${r.golden.split('/').at(-1)} equals what ${r.exporter.split('/').at(-1)} produces from the current prototype`,()=>assert.ok(r.ok,'\n'+r.output));

  test('[GOLDEN] every golden exporter on disk is in the drift check',()=>{
    const tests=new URL('tests/',PROD);
    const onDisk=fs.readdirSync(tests).filter(d=>fs.existsSync(new URL(d+'/Golden/',tests)))
      .flatMap(d=>fs.readdirSync(new URL(d+'/Golden/',tests)).filter(f=>/^export-.*\.mjs$/.test(f)).map(f=>`tests/${d}/Golden/${f}`));
    assert.deepEqual([...onDisk,'tools/golden/export-golden.mjs'].sort(),GOLDENS.map(([e])=>e).sort());
  });
  test('[GOLDEN] the check names the paths that drifted (e.g. a 10 % folio against the 5 % GST master)',()=>{
    const committed={journey:{lines:[{taxBps:1000}],expected:{totalPaise:440000}},rooms:[1,2]},current={journey:{lines:[{taxBps:500}],expected:{totalPaise:426500}},rooms:[1,2]};
    assert.deepEqual(goldenDiff(committed,current),['$.journey.lines.0.taxBps: committed=1000 | prototype=500','$.journey.expected.totalPaise: committed=440000 | prototype=426500']);
    assert.deepEqual(goldenDiff(current,structuredClone(current)),[]);
    assert.deepEqual(goldenDiff({a:[1,2]},{a:[1]}),['$.a: array length committed=2 prototype=1','$.a.1: committed=2 | prototype=undefined']);
  });
}
