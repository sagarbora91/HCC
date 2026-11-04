// REG-07 test runner. test(name,fn) runs fn() IMMEDIATELY (suites may read state set by earlier callbacks at registration time).
// If fn returns a thenable, the result is recorded only when it settles (passed on resolve, failed on reject, under its own name).
// run.mjs calls `await drain()` after every suite's register(), so a suite's async tests finish before the next suite registers.
// finish() always writes evidence/domain-tests.json. Unhandled rejections / uncaught exceptions become failed results, never silent passes.
// Option setExitCode:false (self-test only) keeps a private runner from touching the shared process exit code.
// HCC_TEST_ONLY=<regex> (handled in run.mjs) limits the stream|sync suite files for a quick dev loop; the full npm test is still required.
import {mkdir,writeFile} from 'node:fs/promises';
import {dirname} from 'node:path';

export function createRunner({log=console,setExitCode=true,hookProcess=setExitCode}={}){
  let passed=0,asyncTests=0;
  const results=[],pending=[];
  const fail=(name,e,isAsync)=>{results.push({name,status:'failed',...(isAsync?{async:true}:{}),error:e&&e.message!==undefined?String(e.message):String(e)});log.error('FAIL',name,e);if(setExitCode)process.exitCode=1;};
  const pass=(name,isAsync)=>{passed++;results.push({name,status:'passed',...(isAsync?{async:true}:{})});log.log('PASS',name);};
  function test(name,fn){
    let r;
    try{r=fn();}catch(e){fail(name,e);return;}
    if(r&&typeof r.then==='function'){
      asyncTests++;
      const promise=Promise.resolve(r).then(()=>pass(name,true),e=>fail(name,e,true));
      pending.push({name,promise});
    }else pass(name);
  }
  async function drain(){while(true){const batch=pending.filter(p=>!p.done);if(!batch.length)return;await Promise.allSettled(batch.map(p=>p.promise));batch.forEach(p=>{p.done=true;});}}
  const onRej=e=>fail('[runner] unhandled: '+(e&&e.message!==undefined?e.message:String(e)),e);
  if(hookProcess){process.on('unhandledRejection',onRej);process.on('uncaughtException',onRej);}
  async function finish({evidencePath='evidence/domain-tests.json'}={}){
    await drain();
    await mkdir(dirname(evidencePath),{recursive:true});
    const failed=results.filter(r=>r.status==='failed').length;
    await writeFile(evidencePath,JSON.stringify({runAt:new Date().toISOString(),passed,failed,total:results.length,asyncTests,results},null,2));
    log.log(`${passed}/${results.length} tests passed (${asyncTests} async)`);
  }
  return {test,drain,finish,results,pending,get asyncTests(){return asyncTests;}};
}
