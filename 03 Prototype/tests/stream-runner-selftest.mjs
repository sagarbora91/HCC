// REG-07: the runner attributes a delayed (async) failure to its own test and counts a delayed pass only after it resolves.
// Uses its OWN runner instance (never the shared one); setExitCode:false keeps the shared process exit code untouched.
import {createRunner} from './_runner.mjs';
export async function register({test,assert}){
  const r=createRunner({log:{log(){},error(){}},setExitCode:false});
  r.test('sync pass',()=>{});
  r.test('delayed pass',async()=>{await new Promise(res=>setTimeout(res,20));});
  r.test('delayed fail',async()=>{await new Promise(res=>setTimeout(res,20));assert.fail('late');});
  r.test('sync throw',()=>{throw new Error('now');});
  const pendingBefore=r.pending.length,recordedBefore=r.results.length;
  await r.drain();
  test('[RUNNER] delayed failure is attributed to its own test; delayed pass counted after it resolves',()=>{
    assert.equal(r.results.length,4);
    assert.equal(r.results.filter(x=>x.status==='passed').length,2);
    assert.equal(r.results.filter(x=>x.status==='failed').length,2);
    const late=r.results.find(x=>x.name==='delayed fail');
    assert.equal(late.status,'failed');assert.equal(late.error,'late');
    assert.equal(r.pending.length,2);
    assert.equal(pendingBefore,2);
    assert.equal(recordedBefore,2,'async results are not recorded until they settle');
  });
}
