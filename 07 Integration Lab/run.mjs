// HCC Integration Lab runner. Bundles each suite with the esbuild already installed in 03 Prototype (no new packages),
// runs it under Node with the vitest/NestJS shims, and writes evidence/lab-results.json.
// Donor files are only READ (bundled from their pinned checkout); nothing is written inside 06 Reference Repositories.
// Usage (from the workspace root):  node "07 Integration Lab/run.mjs"
import {createRequire} from 'node:module';
import {fileURLToPath,pathToFileURL} from 'node:url';
import path from 'node:path';
import fs from 'node:fs';
import {createHash} from 'node:crypto';

const LAB=path.dirname(fileURLToPath(import.meta.url));
const ROOT=path.resolve(LAB,'..');
const require=createRequire(path.join(ROOT,'03 Prototype','package.json'));
const {build}=require('esbuild');
const DONOR=path.join(ROOT,'06 Reference Repositories');
const OUT=path.join(LAB,'.generated');
fs.mkdirSync(OUT,{recursive:true});fs.mkdirSync(path.join(LAB,'evidence'),{recursive:true});

const suites=[
  // kind 'upstream' = the donor's own spec/test file, unchanged, executed via the shim (not the donor's vitest runner).
  {id:'H01-upstream',candidate:'H01',kind:'upstream',entry:path.join(DONOR,'haip/apps/api/src/modules/reservation/reservation-state-machine.spec.ts')},
  {id:'H02-upstream',candidate:'H02',kind:'upstream',entry:path.join(DONOR,'haip/apps/api/src/modules/rate-plan/rate-pricing.util.spec.ts')},
  {id:'H04-upstream',candidate:'H04',kind:'upstream',entry:path.join(DONOR,'haip/apps/api/src/common/date/property-business-date.spec.ts')},
  {id:'O01-upstream',candidate:'O01',kind:'upstream-script',entry:path.join(DONOR,'open-hotel-pms/src/lib/checkin-room-selection.test.ts')},
  {id:'O02-upstream',candidate:'O02',kind:'upstream-script',entry:path.join(DONOR,'open-hotel-pms/src/lib/checkout-night-release.test.mts')},
  {id:'H03-upstream',candidate:'H03',kind:'not-run',entry:path.join(DONOR,'haip/apps/api/src/modules/payment/payment-ledger.spec.ts'),
    reason:'Requires decimal.js, drizzle-orm and @telivityhaip/database (donor dependencies not installed; installing donor packages is out of scope). Scenarios re-run against HCC in H03-hcc.'},
  {id:'H01-hcc',candidate:'H01',kind:'hcc',entry:path.join(LAB,'qualifications/H01-reservation-transitions/hcc-conformance.test.ts')},
  {id:'H02-hcc',candidate:'H02',kind:'hcc',entry:path.join(LAB,'qualifications/H02-los-tier-pricing/hcc.test.ts')},
  {id:'H03-hcc',candidate:'H03',kind:'hcc',entry:path.join(LAB,'qualifications/H03-refund-remainder/hcc-scenarios.test.ts')},
  {id:'H04-hcc',candidate:'H04',kind:'hcc',entry:path.join(LAB,'qualifications/H04-property-calendar-date/hcc.test.ts')},
  {id:'O01O02-hcc',candidate:'O01/O02',kind:'hcc',entry:path.join(LAB,'qualifications/O01-O02-room-night-helpers/hcc-comparison.test.ts')},
];

const sha=f=>createHash('sha256').update(fs.readFileSync(f)).digest('hex');
globalThis.__labResults=[];
const summary=[];
for(const s of suites){
  const rel=path.relative(ROOT,s.entry).replaceAll('\\','/');
  if(s.kind==='not-run'){summary.push({suite:s.id,candidate:s.candidate,kind:s.kind,entry:rel,sha256:sha(s.entry),status:'not run',reason:s.reason});continue;}
  const outfile=path.join(OUT,s.id+'.mjs');
  try{
    await build({entryPoints:[s.entry],bundle:true,platform:'node',format:'esm',outfile,logLevel:'error',
      tsconfigRaw:{compilerOptions:{}},
      inject:[path.join(LAB,'shims/vitest.mjs')],
      alias:{'vitest':path.join(LAB,'shims/vitest.mjs'),'@nestjs/common':path.join(LAB,'shims/nestjs-common.mjs')}});
  }catch(e){summary.push({suite:s.id,candidate:s.candidate,kind:s.kind,entry:rel,status:'build failed',error:String(e.message).slice(0,500)});continue;}
  globalThis.__labSuite=s.id;
  const before=globalThis.__labResults.length;
  let scriptError=null;
  try{await import(pathToFileURL(outfile).href+'?t='+Date.now());}catch(e){scriptError=String(e&&e.message||e);}
  const mine=globalThis.__labResults.slice(before);
  if(s.kind==='upstream-script')mine.push({suite:s.id,name:'script assertions (node:assert, top level)',status:scriptError?'failed':'passed',error:scriptError??undefined});
  else if(scriptError)mine.push({suite:s.id,name:'module load',status:'failed',error:scriptError});
  const failed=mine.filter(r=>r.status==='failed');
  summary.push({suite:s.id,candidate:s.candidate,kind:s.kind,entry:rel,sha256:sha(s.entry),tests:mine.length,passed:mine.length-failed.length,failed:failed.length,failures:failed});
}

const all=summary.filter(x=>x.tests!==undefined);
const total=all.reduce((a,x)=>a+x.tests,0),failed=all.reduce((a,x)=>a+x.failed,0);
const report={run_at_utc:new Date().toISOString(),node:process.version,runner:'07 Integration Lab/run.mjs (esbuild from 03 Prototype + shims; not vitest)',
  totals:{suites:summary.length,tests:total,passed:total-failed,failed,not_run:summary.filter(x=>x.status==='not run').length},suites:summary,results:globalThis.__labResults};
fs.writeFileSync(path.join(LAB,'evidence/lab-results.json'),JSON.stringify(report,null,1));
for(const x of summary)console.log(`${x.suite.padEnd(14)} ${x.kind.padEnd(16)} ${x.status??`${x.passed}/${x.tests} passed`}${x.failed?'  FAILED: '+x.failures.map(f=>f.name+' — '+f.error).join(' | '):''}${x.error?'  '+x.error:''}`);
console.log(`\nLab total: ${total-failed}/${total} passed, ${report.totals.not_run} suite(s) not run.`);
if(failed||summary.some(x=>x.status==='build failed'))process.exitCode=1;
