// Dev helper (not part of npm test): run only the given test files, e.g. node tests/_one.mjs tests/stream-ops-master.mjs
import {build} from 'esbuild';
import assert from 'node:assert/strict';
import {mkdir} from 'node:fs/promises';
import {pathToFileURL} from 'node:url';
import {resolve} from 'node:path';
await mkdir('tests/.generated',{recursive:true});
await build({entryPoints:['src/services/mock/index.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/mock.mjs',logLevel:'error'});
const M=await import(pathToFileURL(resolve('tests/.generated/mock.mjs')).href);
let passed=0,failed=0;
function test(name,fn){try{fn();passed++;console.log('PASS',name);}catch(e){failed++;console.log('FAIL',name,'\n   ',String(e.message).split('\n').slice(0,6).join('\n    '));}}
for(const f of process.argv.slice(2)){const x=await import(pathToFileURL(resolve(f)).href);await x.register({test,M,assert});}
console.log(`${passed} passed, ${failed} failed`);
