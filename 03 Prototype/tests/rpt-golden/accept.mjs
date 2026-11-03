// F7-R1 · rewrites tests/rpt-golden/goldens/*.json from the current code. Run from 03 Prototype after a reviewed change: node tests/rpt-golden/accept.mjs
import {build} from 'esbuild';import assert from 'node:assert/strict';
await build({entryPoints:['src/services/mock/index.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/mock-golden.mjs',logLevel:'error'});
const M=await import('../.generated/mock-golden.mjs');const {goldenCheck}=await import('./harness.mjs');
goldenCheck(M,assert,{accept:true});console.log('Goldens written for',M.allReports().length,'report(s).');
