// REG-07 self-test: spawns node on a temporary script that uses tests/_runner.mjs; a delayed failure must give exit 1 and name the
// test in the evidence file, a delayed pass must give exit 0. Run: node tools/runner-selftest.mjs (from 03 Prototype).
import {spawnSync} from 'node:child_process';
import {mkdirSync,writeFileSync,readFileSync,rmSync} from 'node:fs';
import {resolve} from 'node:path';
import assert from 'node:assert/strict';
const dir=resolve('tests/.generated');mkdirSync(dir,{recursive:true});
const runnerUrl=new URL('../tests/_runner.mjs',import.meta.url).href;
const evidence=resolve(dir,'selftest-evidence.json');
function run(kind){
  const script=resolve(dir,'selftest-'+kind+'.mjs');
  writeFileSync(script,`import {createRunner} from ${JSON.stringify(runnerUrl)};
const r=createRunner({log:{log(){},error(){}}});
r.test('sync ok',()=>{});
r.test('delayed ${kind}',async()=>{await new Promise(res=>setTimeout(res,30));${kind==='fail'?"throw new Error('late boom');":''}});
await r.finish({evidencePath:${JSON.stringify(evidence)}});
`);
  try{rmSync(evidence,{force:true});}catch{}
  const p=spawnSync(process.execPath,[script],{encoding:'utf8'});
  rmSync(script,{force:true});
  return {code:p.status,ev:JSON.parse(readFileSync(evidence,'utf8')),err:p.stderr};
}
const f=run('fail');
assert.equal(f.code,1,'delayed failure must give exit code 1');
assert.ok(f.ev.results.some(x=>x.name==='delayed fail'&&x.status==='failed'&&x.error==='late boom'),'evidence names the failing test');
assert.equal(f.ev.failed,1);assert.equal(f.ev.asyncTests,1);
const ok=run('pass');
assert.equal(ok.code,0,'delayed pass must give exit code 0');
assert.equal(ok.ev.failed,0);assert.equal(ok.ev.passed,2);
rmSync(evidence,{force:true});
console.log('runner self-test PASS');
