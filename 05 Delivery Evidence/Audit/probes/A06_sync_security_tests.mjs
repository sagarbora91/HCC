// A6 probe: runs ONLY the two client-side security test files (tests/sync-cache-security.mjs, tests/sync-apk.mjs) through the
// same test() shape as 03 Prototype/tests/run.mjs, without the full 1008-test suite (A1 owns that). Needs tests/.generated/mock.mjs
// (built by run.mjs or by this probe when absent). Run from the repo root:
//   $env:PATH="E:\Tools\nodejs;$env:PATH"; node "05 Delivery Evidence/Audit/probes/A06_sync_security_tests.mjs"
import assert from 'node:assert/strict';
import {existsSync} from 'node:fs';
import {pathToFileURL} from 'node:url';
import path from 'node:path';
const proto=path.resolve('03 Prototype');
process.chdir(proto);
const gen=path.join(proto,'tests/.generated/mock.mjs');
if(!existsSync(gen)){const {build}=await import(pathToFileURL(path.join(proto,'node_modules/esbuild/lib/main.js')).href);
  await build({entryPoints:['src/services/mock/index.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/mock.mjs',logLevel:'error'});}
const M=await import(pathToFileURL(gen).href);
let passed=0,total=0;
function test(name,fn){total++;try{fn();passed++;console.log('PASS',name);}catch(e){console.error('FAIL',name,e);process.exitCode=1;}}
for(const f of ['sync-cache-security.mjs','sync-apk.mjs']){
  console.log('--- '+f);
  await import(pathToFileURL(path.join(proto,'tests',f)).href).then(x=>x.register({test,M,assert}));
}
console.log(`${passed}/${total} tests passed (sync-cache-security + sync-apk)`);
