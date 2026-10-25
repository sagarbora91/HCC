// Performance store (audit P-01, P-03, P-04): per-slice IndexedDB saves, copy-on-write execute(), FY26 built in the scenario
// worker. Names prefixed [PERF].
import {build} from 'esbuild';
import {createHash} from 'node:crypto';
const worker=await build({entryPoints:['src/app/scenarioWorker.ts'],bundle:true,write:false,format:'iife',target:['es2020'],logLevel:'error'});
const WORKER_SRC=worker.outputFiles[0].text;
await build({stdin:{contents:"export * from './src/app/storage';export {generateScenario} from './src/app/scenarioGen';",resolveDir:'.',loader:'ts'},
  bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/perf-store.mjs',logLevel:'error',define:{__HCC_SCENARIO_WORKER__:JSON.stringify(WORKER_SRC)}});
const ST=await import('./.generated/perf-store.mjs');

const sha=o=>createHash('sha256').update(JSON.stringify(o)).digest('hex');
/** sha256 of JSON.stringify(loadScenario('FY26')) on main b04f596, before this change. A deliberate fixture change must update it. */
const FY26_SHA_BEFORE='2aadeee553c53d74d4e24c640709887437cdfc6664ffa340fc939a5662c84a63';

// ── fake IndexedDB with real transaction semantics: writes are staged and applied only on commit; `failPut` aborts the
// transaction when a put for that key is issued (nothing of that transaction is applied) ──
function fakeIdb(){
  const data=new Map();const log=[];let failPut=null;
  const api={data,log,set failPut(k){failPut=k;},open(){
    const req={};
    queueMicrotask(()=>{
      const db={createObjectStore(){},transaction(_s,mode='readonly'){
        let pending=0,aborted=false;const staged=new Map(),tx={};const keys=[];
        const finish=()=>queueMicrotask(()=>{if(pending!==0||aborted)return;
          for(const [k,v] of staged){if(v===undefined)data.delete(k);else data.set(k,v);}
          if(mode==='readwrite')log.push(keys);tx.oncomplete?.();});
        const rq=fn=>{pending++;const r={};queueMicrotask(()=>{if(aborted)return;try{r.result=fn();}catch(e){aborted=true;tx.error=e;tx.onabort?.();return;}r.onsuccess?.();pending--;if(!pending)finish();});return r;};
        tx.objectStore=()=>({
          get:k=>rq(()=>data.get(k)),getAllKeys:()=>rq(()=>[...data.keys()]),
          put:(v,k)=>rq(()=>{if(k===failPut)throw new Error('QuotaExceededError (simulated)');keys.push(k);staged.set(k,v);}),
          delete:k=>rq(()=>{keys.push('-'+k);staged.set(k,undefined);}),
        });
        return tx;}};
      req.result=db;req.onupgradeneeded?.();req.onsuccess?.();
    });
    return req;}};
  return api;
}
function fakeLs(){const m=new Map();return {m,getItem:k=>m.has(k)?m.get(k):null,setItem:(k,v)=>{m.set(k,String(v));},removeItem:k=>{m.delete(k);}};}
const now={delay:0,setTimer:fn=>{fn();return 1;},clearTimer(){}};
/** A fake Worker that runs the inlined worker bundle in-process (as a Blob worker would) and copies messages like postMessage. */
class FakeWorker{
  constructor(){this.self={postMessage:m=>queueMicrotask(()=>this.onmessage?.({data:structuredClone(m)}))};new Function('self',WORKER_SRC)(this.self);FakeWorker.started++;}
  postMessage(d){queueMicrotask(()=>this.self.onmessage({data:structuredClone(d)}));}
  terminate(){}
}
FakeWorker.started=0;

async function prepare(M){
  const {loadScenario,execute,seed,changedSlices}=M;
  const out={};
  // Deterministic generation, directly and through the worker path (inlined bundle, per-slice JSON, reassembly).
  const fy=loadScenario('FY26');out.fySha=sha(fy);
  globalThis.Worker=FakeWorker;const progress=[];
  const viaWorker=await ST.generateScenario('FY26',p=>progress.push(p));delete globalThis.Worker;
  out.workerSha=sha(viaWorker);out.workerStarted=FakeWorker.started;out.progress=progress;
  out.mainSha=sha(await ST.generateScenario('FY26'));// no Worker global → main-thread fallback

  // Slice round trip of FY26 + only changed slices written by the next save.
  const idb=fakeIdb(),a=ST.createStateStorage({idb,ls:fakeLs(),...now});
  a.save(viaWorker);await a.flush();
  out.firstSave=a.lastSave();out.firstKeys=[...idb.data.keys()].sort();
  const b=ST.createStateStorage({idb,ls:fakeLs(),...now});
  const loaded=await b.load();out.loaded=loaded;out.fyJson=JSON.stringify(fy);
  const task=loaded.state.tasks.find(t=>t.assignee==='housekeeper'&&t.status==='To do');
  const next=execute(loaded.state,'housekeeper',{type:'TASK',id:task.id,action:'start'});
  out.changed=changedSlices(loaded.state,next);
  const before=new Map(idb.data);
  b.save(next);await b.flush();
  out.secondSave=b.lastSave();out.secondTx=idb.log.at(-1);
  out.untouchedSame=[...before].filter(([k])=>k.startsWith('slice:')&&!out.secondSave.slices.includes(k.slice(6))).every(([k,v])=>idb.data.get(k)===v);
  out.reloaded=JSON.stringify((await ST.createStateStorage({idb,ls:fakeLs()}).load()).state)===JSON.stringify(next);

  // Failed command: input and store untouched. Failed transaction: nothing applied, next save rewrites everything.
  const snap=new Map(idb.data),stateJson=JSON.stringify(next);
  try{execute(next,'housekeeper',{type:'PAYMENT',id:next.bookings[0].id,amount:100,method:'Cash'});out.failThrew=false;}catch{out.failThrew=true;}
  out.failStateSame=JSON.stringify(next)===stateJson;
  const third=execute(next,'housekeeper',{type:'TASK',id:task.id,action:'check',index:0});
  idb.failPut='manifest';const errsBefore=idb.log.length;b.save(third);await b.flush();idb.failPut=null;
  out.abortedApplied=idb.log.length!==errsBefore;
  out.storeAfterAbortSame=snap.size===idb.data.size&&[...snap].every(([k,v])=>idb.data.get(k)===v);
  b.save(third);await b.flush();out.afterAbortSave=b.lastSave();
  out.thirdRoundTrip=JSON.stringify((await ST.createStateStorage({idb,ls:fakeLs()}).load()).state)===JSON.stringify(third);

  // Idempotent replay returns the same object → nothing new to write.
  const k1=execute(seed(),'owner',{type:'NIGHT_POST',key:'perf-k'});out.replaySame=execute(k1,'owner',{type:'NIGHT_POST',key:'perf-k'})===k1;

  // Migration from the old single-blob save (IndexedDB "current", SCHEMA 7), then the first save converts it atomically.
  const v7=structuredClone(seed());delete v7.complianceX;delete v7.cashbookX;delete v7.configX;v7.schema=7;
  const idbOld=fakeIdb();idbOld.data.set('current',JSON.stringify(v7));
  const c=ST.createStateStorage({idb:idbOld,ls:fakeLs(),...now});
  const mig=await c.load();out.mig=mig;
  out.migKeysBefore=[...idbOld.data.keys()];
  c.save(mig.state);await c.flush();out.migSave=c.lastSave();out.migKeysAfter=[...idbOld.data.keys()].sort();
  out.migReload=await ST.createStateStorage({idb:idbOld,ls:fakeLs()}).load();
  // ... and from the localStorage blob `hcc-prototype-v2`.
  const lsOld=fakeLs();lsOld.setItem('hcc-prototype-v2',JSON.stringify(v7));const idbLs=fakeIdb();
  const d=ST.createStateStorage({idb:idbLs,ls:lsOld,...now});const migLs=await d.load();d.save(migLs.state);await d.flush();
  out.migLs=migLs;out.lsCleared=!lsOld.m.has('hcc-prototype-v2');out.migLsReload=await ST.createStateStorage({idb:idbLs,ls:fakeLs()}).load();
  // A manifest that names a missing slice is unreadable → reset with a reason (never a half-assembled state).
  const idbBad=fakeIdb();for(const [k,v] of idbOld.data)idbBad.data.set(k,v);idbBad.data.delete('slice:bookings');
  out.missing=await ST.createStateStorage({idb:idbBad,ls:fakeLs()}).load();
  return out;
}
const P=await prepare(await import('./.generated/mock.mjs'));

export function register({test,M,assert}){
  const {seed,execute,changedSlices,SCHEMA}=M;
  test('[PERF] FY26 generation is deterministic: hash equals the pre-change hash, directly, via the worker and the fallback',()=>{
    assert.equal(P.fySha,FY26_SHA_BEFORE);assert.equal(P.workerSha,FY26_SHA_BEFORE);assert.equal(P.mainSha,FY26_SHA_BEFORE);
    assert.equal(P.workerStarted,1);assert.ok(P.progress.length>3,'progress reported');assert.ok(P.progress.every((p,i,a)=>!i||p.done>=a[i-1].done),'progress never goes back');});
  test('[PERF] slice save/load round-trips FY26 exactly (same JSON, same key order)',()=>{
    assert.equal(P.loaded.reset,false);assert.equal(JSON.stringify(P.loaded.state),P.fyJson);
    assert.equal(P.firstSave.full,true);assert.ok(P.firstKeys.includes('manifest'));assert.ok(!P.firstKeys.includes('current'));
    assert.ok(P.firstKeys.includes('slice:bookings')&&P.firstKeys.includes('slice:accountingX'));});
  test('[PERF] after a command only the changed slices (+ manifest) are written, in one transaction',()=>{
    const s=P.secondSave;assert.equal(s.full,false);
    assert.deepEqual([...s.slices].sort(),P.changed.filter(k=>typeof P.loaded.state[k]==='object').sort());
    assert.ok(!s.slices.includes('bookings')&&!s.slices.includes('accountingX')&&!s.slices.includes('posX'));assert.ok(s.slices.includes('tasks'));
    assert.ok(s.bytes<P.fyJson.length/20,`wrote ${s.bytes} of ${P.fyJson.length} bytes`);
    assert.deepEqual([...P.secondTx].sort(),[...s.slices.map(k=>'slice:'+k),'manifest'].sort());
    assert.equal(P.untouchedSame,true);assert.equal(P.reloaded,true);});
  test('[PERF] a failed command leaves state and store untouched; an aborted save applies nothing and the next save rewrites all',()=>{
    assert.equal(P.failThrew,true);assert.equal(P.failStateSame,true);
    assert.equal(P.abortedApplied,false);assert.equal(P.storeAfterAbortSame,true);
    assert.equal(P.afterAbortSave.full,true);assert.equal(P.thirdRoundTrip,true);});
  test('[PERF] old single-blob save (IndexedDB "current", SCHEMA 7) is migrated, then converted to slices atomically',()=>{
    assert.equal(P.mig.reset,false);assert.equal(P.mig.state.schema,SCHEMA);assert.ok(P.mig.state.complianceX?.obligations?.length>0);
    assert.deepEqual(P.migKeysBefore,['current'],'nothing rewritten before the first save');
    assert.equal(P.migSave.full,true);assert.ok(!P.migKeysAfter.includes('current'));assert.ok(P.migKeysAfter.includes('manifest'));
    assert.equal(P.migReload.reset,false);assert.deepEqual(P.migReload.state,P.mig.state);});
  test('[PERF] old localStorage blob (hcc-prototype-v2) is migrated into slices and the key removed',()=>{
    assert.equal(P.migLs.reset,false);assert.equal(P.migLs.state.schema,SCHEMA);assert.equal(P.lsCleared,true);assert.deepEqual(P.migLsReload.state,P.migLs.state);});
  test('[PERF] a manifest naming a missing slice resets with a reason instead of loading a partial state',()=>{
    assert.equal(P.missing.reset,true);assert.match(P.missing.reason,/could not be read/);});
  test('[PERF] execute shares untouched slices and never mutates its input',()=>{
    const s=seed(),json=JSON.stringify(s),t=s.tasks.find(x=>x.assignee==='housekeeper'&&x.status==='To do');
    const n=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'start'});
    assert.equal(JSON.stringify(s),json);assert.equal(n.bookings,s.bookings);assert.notEqual(n.tasks,s.tasks);
    assert.ok(changedSlices(s,n).includes('tasks')&&!changedSlices(s,n).includes('bookings'));
    assert.equal(P.replaySame,true);});
  test('[PERF] a failed command after a read of a slice does not leak the draft into the input',()=>{
    const s=seed(),json=JSON.stringify(s);
    assert.throws(()=>execute(s,'owner',{type:'BOOK',guest:'X Guest',room:'308',arrival:s.date,departure:'2026-10-26',people:1}),/not available/);
    assert.equal(JSON.stringify(s),json);});
}
