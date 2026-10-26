// Wave C · A02b-05 + A06-01 in the production client, on a fake server shaped like Hcc.Contracts.Access:
// • "Change my password / PIN" sends exactly POST /commands/ChangeOwnCredential {current,newPassword,newPin} with the screen's key
//   (never a secret in it), refuses weak / wrong-kind input before sending, shows the server's refusals (wrong PIN, lockout) as they are,
//   then re-keys the offline copy with the NEW secret (the old one no longer opens it) and reloads.
// • A sign-in that reports mustChangeCredential (an administrator set the PIN) shows only the change screen: no reads, every other
//   command refused ("Change your PIN first"), also after a restart; the change releases the app. Names prefixed [OWN-CRED-SYNC].
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

const PIN='482913',NEW_PIN='305718',PASSWORD='Sand-Residency-2026!';
const GATE={code:'FORBIDDEN',message:'Change your PIN first. An administrator set it for you, so choose your own PIN before you continue.',details:{reason:'MUST_CHANGE_CREDENTIAL'}};

export async function register({test,assert}){
  const S=await loadSync();
  const own=S.AREAS.find(a=>a.id==='admin').commands.find(c=>c.type==='OWN_CREDENTIAL_CHANGE');
  const probe={id:'probe',loaders:[{slice:'tasks',async load(c){const r=await c.api.get('/probe/tasks');return r.items;}}],commands:[
    {type:'PROBE',reload:['tasks'],async send(c){await c.api.command('ProbeOne',{});}},own]};
  const areas=[S.core,probe];
  /** Fake server: Rina (Reception, user 5) signs in by PIN on this device; `must` = the PIN was set by an administrator. */
  function server({must:mustAtSignIn=false,refuse}={}){
    let who=0,must=false,pin=PIN;
    const srv=fakeServer({
      'POST /auth/sign-in':({body})=>{
        if(body.loginName!=='rina'||body.pin!==pin)return reply(403,{code:'FORBIDDEN',message:'Sign-in failed. Check the name and the password or PIN.'});
        who=5;must=mustAtSignIn;
        return {userId:5,displayName:'Rina Reception',credentialKind:'PIN',session:{idleExpiresUtc:'2026-10-24T05:00:00.000Z',absoluteExpiresUtc:'2026-10-24T16:30:00.000Z',idleTimeoutSeconds:1800},
          sessionToken:null,mustChangeCredential:must};},
      'POST /auth/sign-out':()=>{who=0;return undefined;},
      'GET /me/capabilities':()=>who?{...capabilities({userId:5,displayName:'Rina Reception',roles:['REC']}),credentialKind:'PIN',mustChangeCredential:must}:reply(401,{code:'UNAUTHENTICATED',message:'Sign in.'}),
      'GET /probe/tasks':()=>!who?reply(401,{code:'UNAUTHENTICATED',message:'Sign in.'}):must?reply(403,GATE):{items:[{id:'7'}]},
      'POST /commands/ProbeOne':()=>must?reply(403,GATE):({}),
      'POST /commands/ChangeOwnCredential':({body})=>{
        if(refuse)return refuse(body);
        if(body.current!==pin)return reply(403,{code:'FORBIDDEN',message:'The current password or PIN is wrong.'});
        pin=body.newPin;must=false;return {userId:5,version:'VU5b'};},
    });
    /** An administrator reset that the client has not heard of yet (the server already answers with the gate). */
    srv.forceMust=()=>{must=true;};
    return srv;
  }
  const start=(srv,cache,keyStore,o={})=>startRuntime(S,srv,{areas,cache,keyStore,...o});
  const paths=srv=>srv.calls.map(c=>`${c.method} ${c.path}`);
  const ownCmd=(current,next,key)=>S.ownCredentialCommand('PIN',current,next,key);

  test('[OWN-CRED-SYNC] the admin area maps OWN_CREDENTIAL_CHANGE (every signed-in user; no reload of its own: the runtime reloads everything)',()=>{
    assert.ok(own,'mapped');assert.deepEqual(own.reload,[]);assert.equal(S.OWN_CREDENTIAL_CHANGE,'OWN_CREDENTIAL_CHANGE');
  });

  // ── 1. Must change at the first sign-in (A06-01) ──
  const srv=server({must:true});const cache=S.createMemoryCache();const keys=S.memoryTokenStore();
  const rt=await start(srv,cache,keys);srv.reset();
  const signed=await rt.signIn({loginName:'rina',pin:PIN});const s1=rt.get();const afterSignIn=paths(srv);
  const blocked=await rt.run({type:'PROBE'});await rt.refresh();await rt.focusRefresh();const afterTries=paths(srv);
  const wrong=await rt.run(ownCmd('000000',NEW_PIN,'own-credential:5:t1:1'));const sWrong=rt.get();
  srv.reset();
  const changed=await rt.run(ownCmd(PIN,NEW_PIN,'own-credential:5:t1:2'));const s2=rt.get();const afterChange=paths(srv);const sent=srv.commands('ChangeOwnCredential');
  test('[OWN-CRED-SYNC] A06-01: a must-change sign-in loads nothing, shows only the change screen and refuses every other command with the server\'s words',()=>{
    assert.equal(signed.ok,true);assert.equal(s1.status,'ready');assert.deepEqual([s1.me.mustChange,s1.me.credentialKind],[true,'PIN']);
    assert.equal(S.appScreen(s1),'must-change');assert.equal(S.appScreen(s1,true),'must-change','not even the device offer');
    assert.deepEqual(afterSignIn,['POST /auth/sign-in','GET /me/capabilities'],'no slice read while the PIN must be changed');
    assert.deepEqual(s1.state.tasks,[]);
    assert.deepEqual([blocked.ok,blocked.reason],[false,'must-change']);assert.equal(blocked.text,GATE.message);
    assert.ok(!afterTries.includes('POST /commands/ProbeOne')&&!afterTries.includes('GET /probe/tasks'),'refresh / focus do not read either');
    assert.deepEqual([wrong.ok,wrong.reason,wrong.text],[false,'server','Not permitted: The current password or PIN is wrong.']);
    assert.equal(S.appScreen(sWrong),'must-change');
  });
  test('[OWN-CRED-SYNC] A02b-05: the change sends exactly {current,newPassword:null,newPin} with the screen key; the same session is released and loads',()=>{
    assert.equal(changed.ok,true);
    assert.deepEqual(sent.map(c=>[c.body,c.key]),[[{current:PIN,newPassword:null,newPin:NEW_PIN},'own-credential:5:t1:2']]);
    assert.deepEqual([...afterChange.slice(0,2),...afterChange.slice(2).sort()],['POST /commands/ChangeOwnCredential','GET /me/capabilities','GET /probe/tasks','GET /system/health'],'then every slice');
    assert.equal(s2.me.mustChange,false);assert.equal(S.appScreen(s2),'app');assert.deepEqual(s2.state.tasks.map(t=>t.id),['7']);
  });

  // ── 2. The offline copy follows the new PIN (the administrator knew the old one) ──
  await new Promise(r=>setTimeout(r,10));
  const dump=JSON.stringify(cache.dump());
  srv.down=true;
  const offOld=await start(srv,cache,S.memoryTokenStore());const oldTry=await offOld.unlock(PIN);
  const offNew=await start(srv,cache,S.memoryTokenStore());const newTry=await offNew.unlock(NEW_PIN);
  srv.down=false;
  test('[OWN-CRED-SYNC] after the change the offline copy opens with the NEW PIN only; no PIN in the cache, key store or any Idempotency-Key',()=>{
    assert.equal(offOld.get().status,'locked');assert.equal(oldTry.ok,false);assert.match(oldTry.text,/Wrong PIN/);
    assert.equal(newTry.ok,true);assert.deepEqual(offNew.get().state.tasks.map(t=>t.id),['7']);
    for(const p of [PIN,NEW_PIN]){assert.ok(!dump.includes(p),'cache holds '+p);assert.ok(!srv.calls.some(c=>String(c.key??'').includes(p)||c.url.includes(p)),'key/url holds '+p);}
  });

  // ── 3. Restart during a must-change session: still only the change screen ──
  const srv3=server({must:true});const c3=S.createMemoryCache();const k3=S.memoryTokenStore();
  const r3=await start(srv3,c3,k3);await r3.signIn({loginName:'rina',pin:PIN});
  srv3.reset();const again=await start(srv3,c3,k3);
  test('[OWN-CRED-SYNC] a restart re-reads the flag from /me/capabilities: the change screen again, no reads',()=>{
    assert.equal(S.appScreen(again.get()),'must-change');assert.deepEqual(paths(srv3),['GET /me/capabilities']);
  });

  // ── 4. Voluntary change: client checks first, server refusals as they are ──
  const srv4=server();const c4=S.createMemoryCache();const r4=await start(srv4,c4,S.memoryTokenStore());await r4.signIn({loginName:'rina',pin:PIN});
  srv4.reset();
  const noKey=await r4.run({type:'OWN_CREDENTIAL_CHANGE',current:PIN,newPin:NEW_PIN});
  const weak=await r4.run(ownCmd(PIN,'123456','own-credential:5:v1:1'));
  const short=await r4.run(ownCmd(PIN,'30571','own-credential:5:v1:1'));
  const same=await r4.run(ownCmd(PIN,PIN,'own-credential:5:v1:1'));
  const wrongKind=await r4.run({type:'OWN_CREDENTIAL_CHANGE',current:PIN,newPassword:PASSWORD,key:'own-credential:5:v1:1'});
  const noCurrent=await r4.run(ownCmd('',NEW_PIN,'own-credential:5:v1:1'));
  const sentBefore=srv4.commands('ChangeOwnCredential').length;
  const ok4=await r4.run(ownCmd(PIN,NEW_PIN,'own-credential:5:v1:2'));const after4=paths(srv4);
  test('[OWN-CRED-SYNC] refused before sending: no screen key, trivial / short PIN, the current PIN again, a password for a PIN account, no current PIN',()=>{
    for(const r of [noKey,weak,short,same,wrongKind,noCurrent])assert.deepEqual([r.ok,r.reason],[false,'precheck']);
    assert.match(noKey.text,/Change my password \/ PIN/);assert.match(weak.text,/too easy to guess/);assert.match(short.text,/exactly 6 digits/);
    assert.match(same.text,/must be different from the current one/);assert.match(wrongKind.text,/signs in with a PIN/);assert.match(noCurrent.text,/Type your current PIN/);
    assert.equal(sentBefore,0);
    assert.equal(ok4.ok,true);assert.deepEqual([...after4.slice(0,2),...after4.slice(2).sort()],['POST /commands/ChangeOwnCredential','GET /me/capabilities','GET /probe/tasks','GET /system/health']);
  });
  const lockSrv=server({refuse:()=>reply(403,{code:'FORBIDDEN',message:'Too many wrong attempts. Try again in 15 minutes.'})});
  const r5=await start(lockSrv,S.createMemoryCache(),S.memoryTokenStore());await r5.signIn({loginName:'rina',pin:PIN});
  const locked=await r5.run(ownCmd(PIN,NEW_PIN,'own-credential:5:l1:1'));
  const ruleSrv=server({refuse:()=>reply(422,{code:'VALIDATION',message:'Choose a PIN you have not used for your last 3 PINs.'})});
  const r6=await start(ruleSrv,S.createMemoryCache(),S.memoryTokenStore());await r6.signIn({loginName:'rina',pin:PIN});
  const reused=await r6.run(ownCmd(PIN,NEW_PIN,'own-credential:5:h1:1'));
  test('[OWN-CRED-SYNC] the server\'s lockout and PIN-history refusals are shown as the server words them',()=>{
    assert.deepEqual([locked.ok,locked.reason,locked.text],[false,'server','Not permitted: Too many wrong attempts. Try again in 15 minutes.']);
    assert.deepEqual([reused.ok,reused.text],[false,'Check the details: Choose a PIN you have not used for your last 3 PINs.']);
  });

  // ── 4b. The server's gate reaches a client that did not know: it switches to the change screen ──
  const srv7=server();const r7=await start(srv7,S.createMemoryCache(),S.memoryTokenStore());await r7.signIn({loginName:'rina',pin:PIN});
  const before7=S.appScreen(r7.get());srv7.forceMust();const gated=await r7.run({type:'PROBE'});const s7=r7.get();
  test('[OWN-CRED-SYNC] a 403 MUST_CHANGE_CREDENTIAL from the server switches the app to the change screen (message shown as the server words it)',()=>{
    assert.equal(before7,'app');assert.deepEqual([gated.ok,gated.reason],[false,'server']);assert.equal(gated.text,'Not permitted: '+GATE.message);
    assert.equal(s7.me.mustChange,true);assert.equal(S.appScreen(s7),'must-change');
  });

  // ── 5. Screens (production build) ──
  const gen='tests/.generated';await mkdir(gen,{recursive:true});
  await writeFile(gen+'/own-cred-sync-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {ForcedCredentialChange} from '../../src/clients/sync/react';
import {OwnCredentialSheet} from '../../src/app/OwnCredential';
import {UserMenuSheet} from '../../src/app/mobile/sheets';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
export const forced=(me:any)=>renderToStaticMarkup(<ForcedCredentialChange me={me}/>);
export function sheet(name:string,s:any,uid:string,mobile=false){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);const C:any={OwnCredentialSheet,UserMenuSheet}[name];
  const ctx:any={s,uid,user,m:metrics(s),page:'home',go:noop,modal:{type:'own-credential',key:'k'},open:noop,closeModal:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop,switchUser:noop,signOut:noop};
  return renderToStaticMarkup(<HccContext.Provider value={ctx}><C/></HccContext.Provider>);
}`);
  await build({entryPoints:[gen+'/own-cred-sync-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:gen+'/own-cred-sync.mjs',logLevel:'error',jsx:'automatic',loader:{'.css':'empty'},
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"production"'},banner:{js:"import {createRequire as __ocsReq} from 'node:module';const require=__ocsReq(import.meta.url);"}});
  const V=await import(new URL('./.generated/own-cred-sync.mjs',import.meta.url).href);
  const text=html=>html.replace(/<[^>]+>/g,' ').replace(/&#x27;|&#39;/g,"'").replace(/&amp;/g,'&').replace(/\s+/g,' ');
  const f=V.forced(s1.me),ft=text(f);
  const st=s2.state,uid=s2.me.uid;
  const prodSheet=text(V.sheet('OwnCredentialSheet',st,uid)),prodMenu=text(V.sheet('UserMenuSheet',st,uid,true));
  const P=await loadPages();const pages=[P.renderPages(st,uid,false),P.renderPages(st,uid,true)];
  test('[OWN-CRED-SYNC] forced screen: the server\'s reason, current / new / again (masked, empty), Sign out — and nothing else (no menu, no pages)',()=>{
    assert.match(ft,/Choose your own PIN/);assert.match(ft,/Change your PIN first\. An administrator set it for you/);assert.match(ft,/Nothing else opens until you do/);
    assert.match(ft,/Current PIN/);assert.match(ft,/Type the new PIN again/);assert.match(ft,/It must not be the PIN you were given/);assert.match(ft,/Sign out/);
    assert.equal((f.match(/type="password"/g)||[]).length,3);assert.doesNotMatch(f,/ value="/);
    assert.doesNotMatch(ft,/Modules Home|Search pages|Business date|Demo:/);
  });
  test('[OWN-CRED-SYNC] production sheet and profile menu: other sessions end, no demo starting PIN, no "Preview as"; every page still renders',()=>{
    assert.match(prodSheet,/Change my PIN/);assert.match(prodSheet,/Your sessions on other devices end; this one continues/);assert.doesNotMatch(prodSheet,/246810|Demo:/);
    assert.match(prodMenu,/Change my PIN/);assert.doesNotMatch(prodMenu,/Preview as/);assert.match(prodMenu,/Sign out returns to the sign-in screen/);
    assert.deepEqual(pages.map(x=>x.errors),[{},{}]);
  });
}
