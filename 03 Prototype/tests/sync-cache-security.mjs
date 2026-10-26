// Security review 2026-10-26 — client fixes. F-H01: the offline read cache is encrypted at rest (AES-256-GCM, data key never
// stored next to it) and shown only to a signed-in user: a session the server confirms, a still-valid local session (resume key
// kept outside the cache), or offline re-authentication with the PIN / password; wiped on sign-out, 401, user change, idle and
// absolute expiry, too many wrong offline attempts, and an old unencrypted cache. F-M07: tokens sealed before they reach
// Preferences / Web Storage; the device token moves out of plain localStorage and is sent as X-HCC-Device on every request.
import {loadSync,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

const kv=()=>{const m=new Map();return {m,getItem:k=>m.has(k)?m.get(k):null,setItem:(k,v)=>{m.set(k,String(v));},removeItem:k=>{m.delete(k);}};};
const PIN='482913',PASSWORD='Sand-Residency-2026!';

export async function register({test,assert}){
  const S=await loadSync();
  const probe={id:'probe',loaders:[{slice:'tasks',async load(c){const r=await c.api.get('/probe/tasks');return r.items;}}],commands:[
    {type:'PROBE',reload:['tasks'],async send(c){await c.api.command('ProbeOne',{});}}]};
  const areas=[S.core,probe];
  let clock=Date.parse('2026-10-24T04:30:00.000Z');const now=()=>new Date(clock);
  /** Fake server with a cookie-like session: sign-in by password (owner, user 1) or PIN (rina, user 5). */
  function server(){
    let who=0,signedAt=0;
    const iso=t=>new Date(t).toISOString();
    const srv=fakeServer({
      'POST /auth/sign-in':({body})=>{
        const pin=body.pin!==undefined;const ok=pin?body.loginName==='rina'&&body.pin===PIN:body.loginName==='owner'&&body.password===PASSWORD;
        if(!ok)return reply(401,{code:'UNAUTHENTICATED',message:'Wrong login name or password.'});
        who=pin?5:1;signedAt=clock;
        return {userId:who,displayName:pin?'Rina Reception':'Test Owner',credentialKind:pin?'PIN':'PASSWORD',
          session:{idleExpiresUtc:'2026-10-24T05:00:00.000Z',absoluteExpiresUtc:'2026-10-24T16:30:00.000Z',idleTimeoutSeconds:1800},sessionToken:null};},
      'POST /auth/sign-out':()=>{who=0;return undefined;},
      // Session times as the server reports them (sliding idle end, fixed absolute end 12 h after sign-in).
      'GET /me/capabilities':()=>who?{...capabilities({userId:who,displayName:who===5?'Rina Reception':'Test Owner',roles:who===5?['REC']:['OWN']}),
        session:{idleExpiresUtc:iso(clock+1800_000),absoluteExpiresUtc:iso(signedAt+12*3600_000),idleTimeoutSeconds:1800}}:reply(401,{code:'UNAUTHENTICATED',message:'Sign in.'}),
      'GET /probe/tasks':()=>who?{items:[{id:'7',guest:'Meera Kulkarni (fictional)',phone:'98220 11111'}]}:reply(401,{code:'UNAUTHENTICATED',message:'Sign in.'}),
    });
    srv.signOutHere=()=>{who=0;};
    return srv;
  }
  const start=(srv,cache,keyStore,o={})=>startRuntime(S,srv,{areas,cache,keyStore,now,...o});

  // ── 1. Sign in → encrypted cache; the data key is not next to it ──
  const srv=server();const cache=S.createMemoryCache();const keys=S.memoryTokenStore();
  const rt=await start(srv,cache,keys);await rt.signIn({loginName:'owner',password:PASSWORD});
  const dump1=cache.dump();const lock1=JSON.parse(dump1.lock);const resume1=JSON.parse(await keys.load()).resume;
  test('[SEC F-H01] Cache at rest: every slice and the meta record sealed (AES-GCM); no plaintext, no data key, no secret in the cache database',()=>{
    assert.equal(rt.get().status,'ready');assert.deepEqual(rt.get().state.tasks.map(t=>t.id),['7']);
    assert.deepEqual(Object.keys(dump1).sort(),['lock','meta','s:clock','s:date','s:tasks']);
    for(const [k,v] of Object.entries(dump1))if(k!=='lock')assert.match(v,/^hccs1\.[A-Za-z0-9+/=]+\.[A-Za-z0-9+/=]+$/,k);
    const all=JSON.stringify(dump1),{lock:_l,...records}=dump1,data=Object.values(records).join('|');
    for(const plain of ['Meera','98220',PASSWORD,resume1])assert.ok(!all.includes(plain),'cache holds '+plain);
    for(const plain of ['Test Owner','"id"','tasks'])assert.ok(!data.includes(plain),'sealed records hold '+plain);
    assert.deepEqual(Object.keys(lock1).sort(),['absoluteExpiresUtc','byResume','bySecret','credentialKind','displayName','failures','format','idleTimeoutSeconds','iterations','lastActivity','salt','userId'],
      'the clear lock record holds only what the unlock screen and the expiry check need');
    assert.equal(lock1.format,'hcc-server-cache-2');assert.equal(lock1.userId,1);assert.equal(lock1.credentialKind,'password');
    assert.match(lock1.bySecret,/^hccs1\./);assert.match(lock1.byResume,/^hccs1\./);assert.ok(lock1.iterations>=1000);
    assert.equal(lock1.absoluteExpiresUtc,'2026-10-24T16:30:00.000Z','server remaining lifetime mapped onto this clock');
  });

  // ── 2. No cache before authentication (server says 401) ──
  srv.signOutHere();
  const rt401=await start(srv,cache,keys);const s401=rt401.get();
  test('[SEC F-H01] No cached data before authentication: a 401 at start shows the sign-in screen, nothing decrypted, cache and resume key deleted',()=>{
    assert.equal(s401.status,'signed-out');assert.equal(s401.me,null);assert.deepEqual(s401.state.tasks,[]);assert.equal(s401.fromCache,false);
    assert.equal(cache.dump(),null);
  });

  // ── 3. Offline restart with a valid local session (same device, resume key kept) → read-only copy ──
  const srv3=server();const c3=S.createMemoryCache();const k3=S.memoryTokenStore();
  const r3=await start(srv3,c3,k3);await r3.signIn({loginName:'owner',password:PASSWORD});
  srv3.down=true;clock+=10*60_000;
  const off3=await start(srv3,c3,k3);const s3=off3.get();
  test('[SEC F-H01] Offline restart within the session (resume key present): cached slices shown read-only with their "as of"',()=>{
    assert.equal(s3.status,'ready');assert.equal(s3.readOnly,true);assert.equal(s3.fromCache,true);
    assert.deepEqual(s3.state.tasks.map(t=>t.guest),['Meera Kulkarni (fictional)']);assert.equal(s3.me.userId,1);
  });

  // ── 4. Offline restart WITHOUT the resume key (browser / app closed) → locked until re-authentication ──
  const off4=await start(srv3,c3,S.memoryTokenStore());const s4=off4.get();
  const empty4=await off4.unlock('');const wrong4=await off4.unlock('nope-nope-nope');const s4w=off4.get();
  const ok4=await off4.unlock(PASSWORD);const s4ok=off4.get();const refused4=await off4.run({type:'PROBE'});
  test('[SEC F-H01] Offline restart without a local session: status "locked", NOTHING shown; wrong secret refused; the right password opens it read-only',()=>{
    assert.equal(s4.status,'locked');assert.equal(s4.me,null);assert.deepEqual(s4.state.tasks,[]);assert.deepEqual(s4.locked,{displayName:'Test Owner',credentialKind:'password'});
    assert.equal(empty4.ok,false);assert.equal(wrong4.ok,false);assert.match(wrong4.text,/Wrong password\. 4 more wrong attempts delete the offline copy/);
    assert.equal(s4w.status,'locked');assert.deepEqual(s4w.state.tasks,[]);
    assert.equal(ok4.ok,true);assert.equal(s4ok.status,'ready');assert.equal(s4ok.readOnly,true);assert.deepEqual(s4ok.state.tasks.map(t=>t.id),['7']);
    assert.deepEqual([refused4.ok,refused4.reason],[false,'read-only']);
    assert.equal(JSON.parse(c3.dump().lock).failures,0,'a successful unlock resets the attempt counter');
  });

  // ── 5. Too many wrong offline attempts → wiped ──
  const off5=await start(srv3,c3,S.memoryTokenStore());const tries=[];
  for(let i=0;i<5;i++)tries.push(await off5.unlock('wrong-'+i));
  const s5=off5.get();
  test('[SEC F-H01] Five wrong offline attempts delete the offline copy and show the sign-in screen',()=>{
    assert.deepEqual(tries.map(t=>t.ok),[false,false,false,false,false]);assert.match(tries[4].text,/deleted/);
    assert.equal(s5.status,'signed-out');assert.equal(c3.dump(),null);assert.deepEqual(s5.state.tasks,[]);
  });

  // ── 6. PIN user: offline unlock by PIN ──
  const srv6=server();const c6=S.createMemoryCache();
  const r6=await start(srv6,c6,S.memoryTokenStore());await r6.signIn({loginName:'rina',pin:PIN});
  srv6.down=true;const off6=await start(srv6,c6,S.memoryTokenStore());const l6=off6.get().locked;const ok6=await off6.unlock(PIN);
  test('[SEC F-H01] A PIN user re-authenticates offline with the PIN (the PIN itself is never stored)',()=>{
    assert.deepEqual(l6,{displayName:'Rina Reception',credentialKind:'pin'});assert.equal(ok6.ok,true);assert.equal(off6.get().me.userId,5);
    assert.ok(!JSON.stringify(c6.dump()).includes(PIN));
  });

  // ── 7. Idle and absolute expiry wipe the cache (offline restart and while running) ──
  const srv7=server();const c7=S.createMemoryCache();const k7=S.memoryTokenStore();
  const r7=await start(srv7,c7,k7);await r7.signIn({loginName:'owner',password:PASSWORD});
  srv7.down=true;clock+=31*60_000;
  const off7=await start(srv7,c7,k7);const s7=off7.get();const dump7=c7.dump();const keys7=await k7.load();
  const srv8=server();const c8=S.createMemoryCache();const k8=S.memoryTokenStore();
  const r8=await start(srv8,c8,k8);await r8.signIn({loginName:'owner',password:PASSWORD});
  clock+=29*60_000;const early=r8.checkExpiry();clock+=2*60_000;const late=r8.checkExpiry();const s8=r8.get();
  const srv9=server();const c9=S.createMemoryCache();const k9=S.memoryTokenStore();
  const r9=await start(srv9,c9,k9);await r9.signIn({loginName:'owner',password:PASSWORD});
  const t9=clock;let idleNever=true;
  while(clock<t9+12*3600_000-20*60_000){clock+=20*60_000;await r9.refresh();if(r9.checkExpiry())idleNever=false;} // active all day
  const before9=r9.get().status;clock=t9+12*3600_000+1000;const abs9hit=r9.checkExpiry();const abs9=r9.get().status;const absDump=c9.dump();
  test('[SEC F-H01] Idle (30 min without a server answer) and absolute (12 h) expiry: signed out, cache and resume key deleted — no data visible offline',()=>{
    assert.equal(s7.status,'signed-out');assert.deepEqual(s7.state.tasks,[]);assert.equal(dump7,null);assert.equal(keys7,null);
    assert.equal(early,false);assert.equal(late,true);assert.equal(s8.status,'signed-out');assert.match(s8.signInMessage,/period without activity/);assert.equal(c8.dump(),null);
    assert.equal(idleNever,true,'activity keeps the copy alive');assert.equal(before9,'ready');
    assert.equal(abs9hit,true);assert.equal(abs9,'signed-out');assert.equal(absDump,null);
  });

  // ── 8. Sign-out, another user, old unencrypted cache, tampering ──
  const srv10=server();const c10=S.createMemoryCache();const k10=S.memoryTokenStore();
  const r10=await start(srv10,c10,k10);await r10.signIn({loginName:'owner',password:PASSWORD});
  await r10.signOut();const out10=[c10.dump(),await k10.load()];
  const legacy=S.createMemoryCache();await legacy.write({meta:JSON.stringify({format:'hcc-server-cache-1',me:{userId:1}}),tasks:JSON.stringify({value:[{id:'old'}],asOf:'x'})});
  const srvL=server();srvL.down=true;const offL=await start(srvL,legacy,S.memoryTokenStore());const sL=offL.get();
  const srv11=server();const c11=S.createMemoryCache();const k11=S.memoryTokenStore();
  const r11=await start(srv11,c11,k11);await r11.signIn({loginName:'owner',password:PASSWORD});
  const d11=c11.dump();const flipped=d11['s:tasks'].slice(0,-6)+(d11['s:tasks'].slice(-6,-5)==='A'?'B':'A')+d11['s:tasks'].slice(-5);
  await c11.write({'s:tasks':flipped,'s:date':d11['s:tasks']});
  srv11.down=true;const off11=await start(srv11,c11,k11);const s11=off11.get();
  test('[SEC F-H01] Sign-out deletes cache + resume key; a format-1 (unencrypted) cache is deleted unread; tampered or swapped records are not shown',()=>{
    assert.deepEqual(out10,[null,null]);
    assert.equal(sL.status,'signed-out');assert.deepEqual(sL.state.tasks,[]);assert.equal(legacy.dump(),null);
    assert.equal(s11.status,'ready');assert.deepEqual(s11.state.tasks,[],'tampered slice dropped');assert.notDeepEqual(s11.state.date,[{id:'7'}],'a record moved to another name does not open');
  });
  const srv12=server();const c12=S.createMemoryCache();const k12=S.memoryTokenStore();
  const r12=await start(srv12,c12,k12);await r12.signIn({loginName:'owner',password:PASSWORD});
  await r12.signIn({loginName:'rina',pin:PIN});const lock12=JSON.parse(c12.dump().lock);
  test('[SEC F-H01] Another user signing in on the device: the previous copy is deleted and a new one keyed for the new user',()=>{
    assert.equal(lock12.userId,5);assert.equal(r12.get().me.userId,5);
  });

  // ── 9. A session confirmed online after a restart (no secret at hand): resume-only copy, no offline unlock ──
  const srv13=server();await srv13.fetch('http://hcc.test/api/v1/auth/sign-in',{method:'POST',body:JSON.stringify({loginName:'owner',password:PASSWORD})});
  const c13=S.createMemoryCache();const k13=S.memoryTokenStore();const r13=await start(srv13,c13,k13);const lock13=JSON.parse(c13.dump().lock);
  srv13.down=true;const off13=await start(srv13,c13,S.memoryTokenStore());const s13=off13.get();
  test('[SEC F-H01] Session confirmed by the server without a sign-in here: cached only under the session resume key; without it nothing is shown',()=>{
    assert.equal(r13.get().status,'ready');assert.equal(lock13.bySecret,'');assert.match(lock13.byResume,/^hccs1\./);
    assert.equal(s13.status,'signed-out');assert.deepEqual(s13.state.tasks,[]);assert.equal(c13.dump(),null);
  });

  // ── F-M07: sealed token stores, device token out of plain localStorage, X-HCC-Device on every request ──
  const wk=await crypto.subtle.generateKey({name:'AES-GCM',length:256},false,['encrypt','decrypt']);
  const other=await crypto.subtle.generateKey({name:'AES-GCM',length:256},false,['encrypt','decrypt']);
  const raw=kv();const inner=S.sessionTokenStore(raw,'k');const sealed=S.sealedTokenStore(inner,async()=>wk,'session-token');
  await sealed.save('tok-secret-1');const rawVal=raw.getItem('k');const back=await sealed.load();
  raw.setItem('k','plain-old');const migrated=await sealed.load();const migratedRaw=raw.getItem('k');
  const wrongKey=await S.sealedTokenStore(inner,async()=>other,'session-token').load();const afterWrong=raw.getItem('k');
  test('[SEC F-M07] Sealed token store: Preferences / Web Storage hold only sealed text; an old plain value is re-sealed; a foreign key opens nothing',()=>{
    assert.match(rawVal,/^hccs1\./);assert.ok(!rawVal.includes('tok-secret'));assert.equal(back,'tok-secret-1');
    assert.equal(migrated,'plain-old');assert.match(migratedRaw,/^hccs1\./);
    assert.equal(wrongKey,null);assert.equal(afterWrong,null,'an unreadable value is dropped');
  });

  const prevLs=Object.getOwnPropertyDescriptor(globalThis,'localStorage');const ls=kv();
  Object.defineProperty(globalThis,'localStorage',{value:ls,configurable:true,writable:true});
  try{
    ls.setItem(S.DEVICE_TOKEN_KEY,'dev-tok-legacy');
    const dstoreRaw=kv();const dstore=S.sealedTokenStore(S.sessionTokenStore(dstoreRaw,'hcc-device-token-sealed'),async()=>wk,'device-token');
    await S.useDeviceTokenStore(dstore);
    const afterMigrate=[ls.getItem(S.DEVICE_TOKEN_KEY),dstoreRaw.getItem('hcc-device-token-sealed'),S.deviceToken(),S.deviceRegistered()];
    const ds=fakeServer({'POST /auth/sign-in':()=>({userId:5,displayName:'Rina',credentialKind:'PIN',session:{idleExpiresUtc:'2026-10-24T05:00:00.000Z',absoluteExpiresUtc:'2026-10-24T16:30:00.000Z',idleTimeoutSeconds:1800}}),
      'GET /probe/tasks':()=>({items:[]})});
    const drt=await startRuntime(S,ds,{areas,now});await drt.signIn({loginName:'rina',pin:PIN});await drt.refresh();
    const devHeaders=ds.calls.map(c=>c.headers['X-HCC-Device']);
    await S.forgetDevice();const gone=[S.deviceToken(),dstoreRaw.getItem('hcc-device-token-sealed')];
    test('[SEC F-M07] Device token: moved from plain localStorage into the sealed store; X-HCC-Device on every request (sign-in and session); forget drops it',()=>{
      assert.equal(afterMigrate[0],null,'no plain copy left in localStorage');assert.match(afterMigrate[1],/^hccs1\./);assert.ok(!afterMigrate[1].includes('dev-tok'));
      assert.deepEqual(afterMigrate.slice(2),['dev-tok-legacy',true]);
      assert.equal(S.DEVICE_HEADER,'X-HCC-Device');assert.ok(devHeaders.length>=3);assert.ok(devHeaders.every(h=>h==='dev-tok-legacy'),JSON.stringify(devHeaders));
      assert.deepEqual(gone,['',null]);
    });
  }finally{
    S.resetDeviceTokenStore();
    if(prevLs)Object.defineProperty(globalThis,'localStorage',prevLs);else delete globalThis.localStorage;
  }
}
