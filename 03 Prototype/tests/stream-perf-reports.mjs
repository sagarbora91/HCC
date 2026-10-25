// Performance pass on Reports / Cash book: the indexed and memoised selectors must give exactly the figures of the original
// (uncached, per-date rescanning) computation. Golden hashes were taken from the original implementation (main b04f596).
import {createHash} from 'node:crypto';
const sha=o=>createHash('sha256').update(JSON.stringify(o)).digest('hex').slice(0,16);
const GOLDEN=[
  ['BASE','2026-10-24','2026-10-24','38884c821063692d','56abcbfee5b7dfa3',807],
  ['BASE','2026-10-01','2026-10-24','bacebb5b465dabb7','56abcbfee5b7dfa3',1996],
  ['FY26','2026-04-01','2026-10-24','79a51a68d6f7ab82','c41a9c8fd20f68e5',14839],
  ['FY26','2026-07-01','2026-07-31','8682476fa2331354','91efc51d0b73458a',2871],
  ['FY26','2026-10-20','2026-10-24','9e3c6b9107d01ff1','7c5963b5428fb40b',1091],
  ['FY26','FYFROM','FYTO','15c5d3d2e7752d64','520b8e9c1b86b704',14778],
  ['FY26','2026-04-10','2026-04-10','6fced525eba64b9a','975a73db7d9634c6',825],
];
export function register({test,M,assert}){
  const {seed,execute,loadScenario,reportPack,reportCsv,cbRange,cbDay,shiftMovements,shiftMovementsByDate,reportPackMemo,cbRangeMemo,memoByDeps}=M;
  const base=seed(),fy=loadScenario('FY26');
  const st=n=>n==='BASE'?base:fy;
  const fix=(s,d)=>d==='FYFROM'?s.historyX.from:d==='FYTO'?s.historyX.to:d;
  for(const [n,a0,z0,hp,hc,len] of GOLDEN){
    const s=st(n),a=fix(s,a0),z=fix(s,z0);
    test(`[PERF-R] ${n} ${a}..${z}: report pack and cash book range equal the original computation`,()=>{
      const p=reportPack(s,a,z);
      assert.equal(sha(p),hp);assert.equal(sha(cbRange(s,a,z)),hc);assert.equal(reportCsv(s,p,'daily').length,len);
    });
    test(`[PERF-R] ${n} ${a}..${z}: memoised pack / range are identical to the uncached ones and reused`,()=>{
      const p1=reportPackMemo(s,a,z),p2=reportPackMemo(s,a,z);
      assert.equal(p1,p2);assert.deepEqual(p1,reportPack(s,a,z));
      const c1=cbRangeMemo(s,a,z);assert.equal(c1,cbRangeMemo(s,a,z));assert.deepEqual(c1,cbRange(s,a,z));
    });
  }
  test('[PERF-R] every tab CSV of the FY pack is unchanged by the memo layer',()=>{
    const a=fy.historyX.from,z=fy.historyX.to,p=reportPack(fy,a,z),q=reportPackMemo(fy,a,z);
    for(const t of ['daily','revenue','receipts','expenses','occupancy'])assert.equal(reportCsv(fy,q,t),reportCsv(fy,p,t));
  });
  test('[PERF-R] indexed cash movements equal shiftMovements for every FY day',()=>{
    const dates=Object.keys(fy.closings).sort(),by=shiftMovementsByDate(fy,dates[0],dates[dates.length-1]);
    for(const d of dates)assert.deepEqual(by(d),shiftMovements({...fy,date:d}),d);
  });
  test('[PERF-R] the memo is invalidated when a record changes (new state, new figures)',()=>{
    const s=seed(),before=reportPackMemo(s,s.date,s.date);
    const next=execute(s,'owner',{type:'EXPENSE',vendor:'Memo Test',category:'Supplies',amount:12300,method:'Cash'});
    const after=reportPackMemo(next,next.date,next.date);
    assert.notEqual(after,before);assert.deepEqual(after,reportPack(next,next.date,next.date));
    assert.equal(reportPackMemo(s,s.date,s.date),before);
  });
  test('[PERF-R] memoByDeps keeps only the most recent results',()=>{
    const m=memoByDeps(2);let n=0;const f=k=>m([k],'x',()=>++n);
    f(1);f(2);f(1);assert.equal(n,2);f(3);f(2);assert.equal(n,4);
  });
  test('[PERF-R] timing smoke: FY pack and Apr–Oct cash book stay within a generous budget',()=>{
    const a=fy.historyX.from,z=fy.historyX.to,t0=performance.now();
    reportPack(fy,a,z);const t1=performance.now();cbRange(fy,a,fy.date);const t2=performance.now();
    assert.ok(t1-t0<1500,`reportPack FY took ${(t1-t0).toFixed(0)} ms`);
    assert.ok(t2-t1<1000,`cbRange took ${(t2-t1).toFixed(0)} ms`);
  });
}
