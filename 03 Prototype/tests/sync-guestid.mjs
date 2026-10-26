// W4 gap G4: production "Show ID" (sync/guestid.ts + guestid-ui.tsx): RevealGuestId call, role gating, auto-hide, nothing persisted, page render.
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {loadSync,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

const NUMBER='123412341234',FORMATTED='1234 1234 1234';
const guest=(id)=>({guestId:id,name:'Asha Rao',phone:'+91 98200 01111',email:'a@example.test',address:'x',nationality:'Indian',idType:'Aadhaar (demo)',idLast4:'1234',
  company:null,gstin:null,party:[],verified:true,masked:false,version:'GV'+id,idMasked:'XXXX-XXXX-1234',idOnFile:true,canRevealId:true,preferences:null,notes:null,origin:'manual'});

async function pageRender(){
  const dir='tests/.generated';await mkdir(dir,{recursive:true});
  await writeFile(dir+'/guestid-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {GuestIdValue} from '../../src/features/guests/GuestId';
import {loadScenario} from '../../src/services/mock';
export function render(){const s:any=loadScenario('BASE');const g=s.guests[0];
  const ctx:any={s,uid:'owner',run:async()=>false,busy:false};
  return renderToStaticMarkup(<HccContext.Provider value={ctx}><GuestIdValue g={g}/></HccContext.Provider>);}
`);
  await build({entryPoints:[dir+'/guestid-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:dir+'/guestid-page.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"production"'},
    banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
  return (await import(new URL('../'+dir+'/guestid-page.mjs',import.meta.url).href)).render();
}

export async function register({test,assert}){
  const S=await loadSync();
  const reveals=[];
  const mk=(caps)=>fakeServer({'GET /me/capabilities':()=>caps,'GET /guests':()=>({total:1,page:1,pageSize:500,items:[guest(9)]}),
    'POST /commands/RevealGuestId':({body})=>{reveals.push(body);
      if(body.guestId===404)return reply(404,{code:'NOT_FOUND',message:'The full ID number is not on file for this guest.'});
      return {guestId:body.guestId,idType:'Aadhaar',idNumber:NUMBER,formatted:FORMATTED,idMasked:'XXXX-XXXX-1234'};}});
  const cache=S.createMemoryCache();
  const srv=mk(capabilities());
  const rt=await startRuntime(S,srv,{cache});
  const r1=await S.revealGuestId(rt.transport,'9');
  const r2=await S.revealGuestId(rt.transport,'9');
  const calls=srv.commands('RevealGuestId');
  await rt.refresh();await rt.reload(['guests']);
  const blob=JSON.stringify([rt.get().state,cache.dump()]);
  const notFound=await S.revealGuestId(rt.transport,'404').then(()=>null,e=>String(e.message));
  // gating
  const lim=capabilities({roles:['HKP'],grants:[['PERM-GST-002','A']]});
  const rt2=await startRuntime(S,mk(lim));
  const gate=[S.canRevealGuestId(rt.get().me),S.canRevealGuestId(rt2.get().me),S.canRevealGuestId(null)];
  // controller: auto-hide after ms; hide while in flight drops the number
  const views=[],pending=[];const timers={set:(f,ms)=>{pending.push({f,ms});return pending.length;},clear:()=>{}};
  const c=S.createReveal({reveal:()=>S.revealGuestId(rt.transport,'9'),onChange:v=>views.push(v.phase),timers});
  await c.show();const shownPhase=c.get().phase,timerMs=pending[0]?.ms;pending[0].f();const afterTimer=c.get().phase;
  let release;const slow=S.createReveal({reveal:()=>new Promise(r=>{release=()=>r({idType:'x',formatted:'SECRET'});}),onChange:()=>{},timers});
  const p=slow.show();slow.hide();release();await p;
  const html=await pageRender();

  test('[G4] Show ID: RevealGuestId {guestId} with a fresh Idempotency-Key per click; formatted number returned to the caller only',()=>{
    assert.equal(calls.length,2+0);assert.deepEqual(calls[0].body,{guestId:9});
    assert.ok(calls[0].key&&calls[1].key&&calls[0].key!==calls[1].key);
    assert.deepEqual(r1,{idType:'Aadhaar',formatted:FORMATTED});assert.deepEqual(r2,r1);
    assert.match(notFound,/not on file/);
  });
  test('[G4] Nothing persisted: the number (full or formatted) is absent from the runtime state and the read cache after reveals and reloads',()=>{
    assert.ok(!blob.includes(NUMBER)&&!blob.includes(FORMATTED));
    assert.ok(rt.get().state.guests[0].idNumber===undefined);
  });
  test('[G4] Role gating: only PERM-GST-003 holders may reveal',()=>{assert.deepEqual(gate,[true,false,false]);});
  test('[G4] Controller: shown for 30 s then hidden; hidden or unmounted during the call = number dropped',()=>{
    assert.deepEqual([shownPhase,timerMs,afterTimer],['shown',30000,'idle']);assert.deepEqual(views,['loading','shown','idle']);
    assert.equal(slow.get().phase,'idle');
  });
  test('[G4] Guest page renders in production: masked value only, no full number, no button without a signed-in user',()=>{
    assert.match(html,/XXXX-XXXX-/);assert.ok(!html.includes('Show ID')&&!html.includes(NUMBER));
  });
}
