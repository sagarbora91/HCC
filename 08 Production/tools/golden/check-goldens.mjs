// Golden drift check (A08-04 / A03a-12 / A03b-07): re-runs every golden exporter with --check against the prototype of this checkout
// and fails when a committed golden file differs from what its exporter produces now (or when an exporter no longer runs).
// Run by 03 Prototype `npm test` (tests/stream-goldens.mjs), so a prototype change that moves a server golden figure fails the
// prototype suite until the golden is regenerated (run its exporter without --check) and the server tests that read it are updated.
// Usage (Node 25): node "08 Production/tools/golden/check-goldens.mjs" [--proto <path to 03 Prototype>]
import {execFile} from 'node:child_process';
import path from 'node:path';import {fileURLToPath} from 'node:url';
const PROD=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..','..');

/** Every exporter and the golden it writes (paths relative to 08 Production). */
export const GOLDENS=[
  ['tools/golden/export-golden.mjs','tests/Hcc.Platform.Tests/Golden/base-fixture.golden.json'],
  ['tests/Hcc.Inventory.Tests/Golden/export-inventory-golden.mjs','tests/Hcc.Inventory.Tests/Golden/inventory-base.golden.json'],
  ['tests/Hcc.Stays.Tests/Golden/export-stays-golden.mjs','tests/Hcc.Stays.Tests/Golden/stays-golden.json'],
  ['tests/Hcc.Messaging.Tests/Golden/export-messaging-golden.mjs','tests/Hcc.Messaging.Tests/Golden/messaging-golden.json'],
  ['tests/Hcc.Customers.Tests/Golden/export-customers-golden.mjs','tests/Hcc.Customers.Tests/Golden/customers-golden.json'],
  ['tests/Hcc.Accounting.Tests/Golden/export-accounting-golden.mjs','tests/Hcc.Accounting.Tests/Golden/accounting-golden.json'],
  ['tests/Hcc.Payroll.Tests/Golden/export-payroll-golden.mjs','tests/Hcc.Payroll.Tests/Golden/payroll-base.golden.json'],
];

/** Runs all exporters with --check (in parallel, each in its own node process); resolves to one result per golden. */
export function checkGoldens(extraArgs=[]){
  return Promise.all(GOLDENS.map(([exporter,golden])=>new Promise(resolve=>{
    execFile(process.execPath,[path.join(PROD,exporter),'--check',...extraArgs],{cwd:PROD,encoding:'utf8',maxBuffer:16*1024*1024,timeout:300000},
      (err,stdout,stderr)=>resolve({exporter,golden,ok:!err,output:(stdout+stderr).trim()}));
  })));
}

if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  const i=process.argv.indexOf('--proto');
  const results=await checkGoldens(i>0?['--proto',process.argv[i+1]]:[]);
  for(const r of results)console.log(`${r.ok?'OK  ':'FAIL'} ${r.golden}${r.ok?'':'\n'+r.output.split('\n').map(l=>'     '+l).join('\n')}`);
  const bad=results.filter(r=>!r.ok).length;
  console.log(bad?`${bad} of ${results.length} golden files are out of date.`:`All ${results.length} golden files match the current prototype.`);
  if(bad)process.exitCode=1;
}
