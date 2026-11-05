// AF2-CTL01 · production mapper: GET /stays/rate-changes carries the CTL-01 columns (migration 4920) into s.stayRateX, so the digest card can list
// the changes beyond the agreed-rate threshold; rows written before 4920 map without them.
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';

await mkdir('tests/.generated',{recursive:true});
await writeFile('tests/.generated/af2-ctl01-entry.ts',"export {rateChangesFromServer} from '../../src/clients/sync/lfmoney';\n");
await build({entryPoints:['tests/.generated/af2-ctl01-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/af2-ctl01.mjs',logLevel:'error'});
const X=await import('./.generated/af2-ctl01.mjs');

export function register({test,assert}){
  const row=extra=>({changeId:5,stayId:1,reservationId:9,roomNo:'101',businessDate:'2026-10-24',changedUtc:'2026-10-24T05:00:00Z',changedBy:2,approvalId:7,approvedBy:1,
    scope:'all',reason:'Corporate rate',oldRatePaise:250000,newRatePaise:190000,oldPeople:2,newPeople:2,oldChildren:0,newChildren:0,effectiveFrom:'2026-10-24',repostedJson:'[]',...extra});
  test('[AF2-CTL01] rate-change rows map the tariff, agreed rate, threshold and over-threshold flag',()=>{
    const c=X.rateChangesFromServer([row({tariffPaise:200000,agreedRatePaise:250000,agreedAfterPaise:190000,thresholdBps:1500,overThreshold:true})]).changes[0];
    assert.deepEqual([c.tariff,c.agreedRate,c.agreedAfter,c.thresholdBps,c.overThreshold],[200000,250000,190000,1500,true]);
  });
  test('[AF2-CTL01] rows written before 4920 map without the CTL-01 fields',()=>{
    const c=X.rateChangesFromServer([row({tariffPaise:null,agreedRatePaise:null,agreedAfterPaise:null,thresholdBps:null,overThreshold:false})]).changes[0];
    assert.equal(c.agreedRate,undefined);assert.equal(c.overThreshold,undefined);assert.equal(c.newRate,190000);
  });
}
