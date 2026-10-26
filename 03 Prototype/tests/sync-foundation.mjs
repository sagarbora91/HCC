// W4-0 sync foundation: registry rules, production run() routing (mapped / unmapped / precheck / read-only), stable
// Idempotency-Keys + expectedVersion, loader scheduling (needs, patches, permissions, FORBIDDEN, focus throttle), the
// IndexedDB-style read cache with "as of", sign-in / session end, and the capabilities → can() mapping per server role.
// The bundle check (sync layer only in the production bundle) lives with the other bundle checks in stream-clients.mjs.
import {loadSync,loadPages,fakeServer,startRuntime,reply,netDown,capabilities,settle} from './_sync-harness.mjs';

/** Server role grants as seeded by 0101_access_rbac_seed.sql (role → [PERM id, mode]). */
const SEED={
  OWN:'RES-001 RES-002 RES-003 RES-004 RATE-001 GST-001 GST-002 GST-003 STY-001 STY-002 STY-003 FOL-001 FOL-002 FOL-003 FOL-004 FOL-005 PAY-001 PAY-002 PAY-003 PAY-004 PAY-005 SHF-001 SHF-002 HK-001 HK-002 HK-003 HK-004 ROOM-001 ROOM-002 EXP-001 EXP-002 EXP-003 CLS-001 CLS-002 CLS-003 CLS-004 APR-001 RPT-001 RPT-002 RPT-003 ADM-001 ADM-002 ADM-003 ADM-004 ADM-005',
  MGR:'RES-001 RES-002 RES-003 RES-004 RATE-001:L GST-001 GST-002 GST-003 STY-001 STY-002 STY-003 FOL-001 FOL-002 FOL-003:L FOL-004 FOL-005:R PAY-001 PAY-002 PAY-003 PAY-004 PAY-005:L SHF-001 SHF-002:L HK-001 HK-002 HK-003 HK-004 ROOM-001 ROOM-002 EXP-001 EXP-002:L EXP-003 CLS-001 CLS-002 CLS-003 APR-001:L RPT-001 RPT-002 RPT-003 ADM-004 ADM-005:V',
  REC:'RES-001 RES-002 RES-003 RES-004 RATE-001:R GST-001 GST-002 GST-003 STY-001 STY-002 STY-003 FOL-001 FOL-002 FOL-003:L FOL-004 FOL-005:R PAY-001 PAY-002 PAY-003:R PAY-004 SHF-001 ROOM-001 EXP-001',
  CSH:'FOL-001 FOL-002 FOL-004 FOL-005:R PAY-001 PAY-002 PAY-003:R PAY-004 SHF-001 EXP-001 GST-001:M RPT-002:V',
  SUP:'HK-001 HK-002 HK-003 HK-004 ROOM-001 ROOM-002',HKP:'HK-001 HK-003 ROOM-001',MNT:'HK-001 ROOM-001',
  ACC:'ADM-004 CLS-001:V CLS-003 EXP-001 EXP-002 EXP-003 FOL-001:V FOL-004:V FOL-005:R RPT-001 RPT-002 RPT-003 SHF-002',
  TAD:'ADM-001 ADM-002:R ADM-003:V ADM-005',
};
const seedGrants=code=>SEED[code].split(' ').map(x=>{const [p,m='A']=x.split(':');return ['PERM-'+p,m];});

export async function register({test,assert,M}){
  const S=await loadSync();

  // ── Registry ──
  const registryProblems=S.validateAreas(S.AREAS);
  const dupes=S.validateAreas([{id:'a',loaders:[{slice:'rooms',load:async()=>[]}],commands:[{type:'X',reload:['rooms'],send:async()=>{}}]},
    {id:'b',loaders:[{slice:'rooms',load:async()=>[]},{slice:'tasks',kind:'patch',load:async()=>[]}],commands:[{type:'X',reload:['posX'],send:async()=>{}}]}]);
  test('[W4-0] Registry: valid as shipped; two owners of a slice, two mappers of a command, orphan patches and reloads are refused',()=>{
    assert.deepEqual(registryProblems,[]);
    assert.ok(S.AREAS.some(a=>a.id==='core')&&S.AREAS.some(a=>a.id==='reservations'));
    assert.equal(dupes.length,4,dupes.join(' | '));
    assert.ok(dupes.some(p=>/two owners/.test(p))&&dupes.some(p=>/mapped by a and b/.test(p))&&dupes.some(p=>/patch loader on "tasks"/.test(p))&&dupes.some(p=>/reloads "posX"/.test(p)));
    assert.throws(()=>S.createSyncRuntime({areas:[S.core,S.core]}),/Sync registry/);
  });

  // ── Boot: capabilities + loaders over the empty skeleton (no demo data) ──
  const srv=fakeServer();const rt=await startRuntime(S,srv);const snap=rt.get();
  test('[W4-0] Boot: GET /me/capabilities then every loader; production state = empty skeleton + server slices + the signed-in user (no demo data)',()=>{
    assert.equal(snap.status,'ready');assert.equal(snap.readOnly,false);assert.equal(snap.fromCache,false);
    assert.deepEqual(srv.calls.map(c=>`${c.method} ${c.path}`).slice(0,2),['GET /me/capabilities','GET /system/health']);
    const s=snap.state;
    assert.equal(s.clock,'2026-10-24T10:00');assert.equal(s.date,'2026-10-24');assert.equal(s.scenario,'SERVER');
    for(const k of ['rooms','bookings','tasks','expenses','approvals','audit','guests','closedDates','processed'])assert.deepEqual(s[k],[],k);
    assert.deepEqual(s.posX.orders,[]);assert.deepEqual(s.staffX.staff,[]);assert.equal(s.settings.hotel,'');
    assert.deepEqual(s.users.map(u=>[u.id,u.name,u.role,u.enabled]),[['1','Test Owner','Owner',true]]);
    assert.equal(M.can(s,'1','admin'),true);assert.equal(M.can(s,'1',['close','reports']),true);
    assert.ok(snap.asOf&&snap.asOf.endsWith('Z'),'as-of time recorded');
    assert.equal(srv.calls.filter(c=>c.method==='POST').length,0,'loading never sends commands');
  });

  // ── Every screen renders on the server skeleton (production build), as on the prototype data ──
  const P=await loadPages();
  const skDesk=P.renderPages(snap.state,'1',false),skPhone=P.renderPages(snap.state,'1',true);
  const protoDesk=P.renderPages(M.seed(),'owner',false);
  const broken=P.renderPages({...snap.state,bookings:[{id:'x'}]},'1',false);
  test('[W4-0] Screens: every desktop and phone page renders on the empty server skeleton (harness renderPages for area tests)',()=>{
    assert.deepEqual(skDesk.errors,{});assert.deepEqual(skPhone.errors,{});
    assert.ok(skDesk.rendered>=30&&skPhone.rendered>=30,`${skDesk.rendered}/${skPhone.rendered}`);
    assert.deepEqual(protoDesk.errors,{});assert.equal(protoDesk.rendered,skDesk.rendered);
    assert.ok(Object.keys(broken.errors).length>0,'a malformed slice is reported');
  });

  // ── Capabilities mapping per seeded server role ──
  const perRole={};
  for(const code of Object.keys(SEED))perRole[code]=S.meFromCapabilities(capabilities({roles:[code],grants:seedGrants(code)}));
  const base=M.seed();
  test('[W4-0] Capabilities: seeded server role grants → prototype can() permissions equal the prototype role grants (gap: Accountant gets expenses)',()=>{
    const byRole={OWN:'Owner',MGR:'Manager',REC:'Reception',CSH:'Cashier',SUP:'Supervisor',HKP:'Housekeeper',ACC:'Accountant',MNT:'Maintenance',TAD:'Tech admin'};
    for(const [code,role] of Object.entries(byRole)){
      assert.equal(perRole[code].role,role,code);
      const want=[...base.grants[role]].sort();if(code==='ACC')want.push('expenses');
      assert.deepEqual([...perRole[code].permissions].sort(),want.sort(),code);
    }
    assert.ok(S.CAPABILITY_GAPS.length>=4);
    const s=S.overlayMe(S.emptyState(),perRole.HKP);
    assert.equal(M.can(s,perRole.HKP.uid,'housekeeping'),true);assert.equal(M.can(s,perRole.HKP.uid,'payments'),false);
  });

  // ── run(): unmapped / precheck / mapped with stable keys, numbering, expectedVersion, reload ──
  const sent=[];let failNext=false;
  const probe={id:'probe',loaders:[{slice:'tasks',async load(c){const r=await c.api.get('/probe/tasks');return r.items;}}],
    commands:[
      {type:'PROBE',reload:['tasks'],precheck:cmd=>cmd.amount>0?undefined:'Enter an amount.',
        async send(c){sent.push(c.key);await c.api.command('ProbeOne',{taskId:S.serverId(c.cmd.id,'Task'),amountPaise:c.cmd.amount},{expectedVersion:c.expectedVersion('task',c.cmd.id)});
          await c.api.command('ProbeTwo',{taskId:S.serverId(c.cmd.id,'Task')});await c.api.command('ProbeThree',{},{step:'audit'});}},
    ]};
  let version=1;
  const ps=fakeServer({'GET /probe/tasks':()=>({items:[{id:'7',v:version}]}),
    'POST /commands/ProbeOne':()=>{if(failNext){failNext=false;return netDown();}return {ok:true};},'POST /commands/ProbeTwo':()=>({ok:true}),'POST /commands/ProbeThree':()=>({ok:true})});
  const prt=await startRuntime(S,ps,{areas:[S.core,probe]});
  prt.ids.setVersion('task','7','AAAAAAAAB9I=');
  ps.reset();
  const unmapped=await prt.run({type:'CHECKIN',id:'7'});const afterUnmapped=ps.calls.length;
  const pre=await prt.run({type:'PROBE',id:'7',amount:0});const afterPre=ps.calls.length;
  failNext=true;const lost=await prt.run({type:'PROBE',id:'7',amount:100});
  const lostKey=ps.commands('ProbeOne')[0].key;
  const retry=await prt.run({type:'PROBE',id:'7',amount:100});
  const again=await prt.run({type:'PROBE',id:'7',amount:100});
  const versioned=await prt.run({type:'PROBE',id:'7',amount:100,version:3,key:'dlg:abc'});
  const p1=ps.commands('ProbeOne'),p2=ps.commands('ProbeTwo'),p3=ps.commands('ProbeThree');
  test('[W4-0] run(): unmapped → "Not available on the server yet: <type>" and precheck refusals send nothing',()=>{
    assert.deepEqual([unmapped.ok,unmapped.reason,unmapped.text],[false,'unmapped','Not available on the server yet: CHECKIN']);
    assert.equal(afterUnmapped,0);
    assert.deepEqual([pre.ok,pre.reason,pre.text],[false,'precheck','Check the details: Enter an amount.']);assert.equal(afterPre,0);
  });
  test('[W4-0] run(): Idempotency-Key stable per user action — reused after a lost response, new after a success; dialog key used as given',()=>{
    assert.equal(lost.ok,false);assert.equal(lost.reason,'server');
    assert.ok(retry.ok&&again.ok&&versioned.ok);
    assert.equal(p1[1].key,lostKey,'retry after a lost response reuses the key');
    assert.notEqual(p1[2].key,lostKey,'an identical second action after a success gets a new key');
    assert.ok(p1.every(c=>c.key.length<=80&&/^[\x21-\x7e]+$/.test(c.key)));
    assert.equal(p1[3].key,'dlg:abc');assert.deepEqual([sent[0],sent[1]],[lostKey,lostKey]);
  });
  test('[W4-0] run(): later commands of one action get <key>:2 / <key>:<step>; expectedVersion only when the prototype command carries a version',()=>{
    assert.equal(p2[2].key,'dlg:abc:2');assert.equal(p3[2].key,'dlg:abc:audit');assert.equal(p2[0].key,lostKey+':2');
    assert.equal(p1[1].body.expectedVersion,undefined);assert.equal(p1[3].body.expectedVersion,'AAAAAAAAB9I=');
    assert.deepEqual(p1[3].body,{taskId:7,amountPaise:100,expectedVersion:'AAAAAAAAB9I='});
    assert.equal(p1[3].headers['Content-Type'],'application/json');
  });
  test('[W4-0] run(): a committed command reloads its slices (GET after POST); the lost one reloads nothing',()=>{
    const seq=ps.calls.map(c=>`${c.method} ${c.path}`);
    const firstOk=seq.indexOf('POST /commands/ProbeThree');
    assert.equal(seq[firstOk+1],'GET /probe/tasks');
    assert.equal(ps.gets('/probe/tasks').length,3,'one reload per committed action');
  });

  // ── Loader scheduling: needs / patches / permissions / FORBIDDEN / errors ──
  const order=[];
  const sched={id:'sched',loaders:[
    {slice:'tasks',kind:'patch',async load(c){order.push('tasks:patch');return [...c.current('tasks'),{id:'p'}];}},
    {slice:'expenses',needs:['tasks'],async load(c){order.push('expenses');return c.current('tasks').map(t=>({id:'e-'+t.id}));}},
    {slice:'tasks',async load(){order.push('tasks:own');return [{id:'o'}];}},
    {slice:'approvals',requires:'admin',async load(c){await c.api.get('/admin-only');return [{id:'x'}];}},
    {slice:'audit',async load(c){return c.api.get('/forbidden');}},
    {slice:'guests',async load(c){return c.api.get('/broken');}},
  ],commands:[]};
  const ss=fakeServer({'GET /me/capabilities':()=>capabilities({roles:['HKP'],grants:seedGrants('HKP')}),'GET /forbidden':()=>reply(403,{code:'FORBIDDEN',message:'no'}),'GET /broken':()=>'{"guests":1'});
  const srt=await startRuntime(S,ss,{areas:[S.core,sched]});const sst=srt.get();
  test('[W4-0] Loaders: owner before patch, `needs` respected, `requires` skips the GET, FORBIDDEN → empty slice, other errors keep the slice and are reported',()=>{
    assert.deepEqual(order,['tasks:own','tasks:patch','expenses']);
    assert.deepEqual(sst.state.tasks.map(t=>t.id),['o','p']);assert.deepEqual(sst.state.expenses.map(e=>e.id),['e-o','e-p']);
    assert.deepEqual(sst.state.approvals,[]);assert.equal(ss.gets('/admin-only').length,0);
    assert.deepEqual(sst.state.audit,[]);assert.equal(sst.errors.audit,undefined);
    assert.match(sst.errors.guests,/^sched: /);assert.deepEqual(sst.state.guests,[]);
    assert.equal(sst.readOnly,false);assert.equal(sst.state.users[0].role,'Housekeeper');
  });

  // ── Focus refresh throttle and manual refresh ──
  let t=Date.parse('2026-10-24T04:30:00Z');const clock=()=>new Date(t);
  const fs2=fakeServer();const frt=await startRuntime(S,fs2,{areas:[S.core],now:clock,focusRefreshMs:30_000});
  const n0=fs2.gets('/system/health').length;
  t+=10_000;await frt.focusRefresh();const n1=fs2.gets('/system/health').length;
  t+=25_000;await frt.focusRefresh();const n2=fs2.gets('/system/health').length;
  await frt.refresh();const n3=fs2.gets('/system/health').length;
  const listeners={};const win={addEventListener:(e,f)=>{listeners[e]=f;},removeEventListener:e=>{delete listeners[e];},document:{visibilityState:'visible',addEventListener:(e,f)=>{listeners['doc:'+e]=f;},removeEventListener:e=>{delete listeners['doc:'+e];}}};
  const detach=frt.attachWindow(win);const attached=Object.keys(listeners).sort();detach();
  test('[W4-0] Scheduling: focus/visibility refresh at most every 30 s; manual refresh always; window listeners attach and detach',()=>{
    assert.deepEqual([n0,n1,n2,n3],[1,1,2,3]);
    assert.deepEqual(attached,['doc:visibilitychange','focus']);assert.deepEqual(Object.keys(listeners),[]);
    assert.equal(fs2.gets('/me/capabilities').length,3,'every full load re-checks the session and role');
  });

  // ── Read-only mode + read cache ──
  const cache=S.createMemoryCache();
  const rs=fakeServer({'GET /probe/tasks':()=>({items:[{id:'7'},{id:'8'}]}),'POST /commands/ProbeOne':()=>({ok:true}),'POST /commands/ProbeTwo':()=>({ok:true}),'POST /commands/ProbeThree':()=>({ok:true})});
  const r1=await startRuntime(S,rs,{areas:[S.core,probe],cache});
  const cachedAsOf=r1.get().asOf;
  rs.down=true;
  const failedRefresh=await settle(()=>r1.refresh());
  const roSnap=r1.get();rs.reset();
  const refused=await r1.run({type:'PROBE',id:'7',amount:100});
  const roCalls=rs.calls.map(c=>`${c.method} ${c.path}`);
  // A new start on the same device while the server is still unreachable: the cache is shown, read-only.
  const r2=await startRuntime(S,rs,{areas:[S.core,probe],cache});const offline=r2.get();
  rs.down=false;await r2.refresh();const back=r2.get();
  test('[W4-0] Read-only: unreachable server → every command refused without a POST (only a reconnect health check); last slices stay visible',()=>{
    assert.ok(failedRefresh.ok);assert.equal(roSnap.readOnly,true);assert.deepEqual(roSnap.state.tasks.map(x=>x.id),['7','8']);
    assert.deepEqual([refused.ok,refused.reason],[false,'read-only']);assert.match(refused.text,/^Server unavailable — read-only\. Nothing was sent and nothing is saved on this device \(PROBE\)\.$/);
    assert.deepEqual(roCalls,['GET /system/health']);
  });
  test('[W4-0] Read cache: a restart while unreachable shows the cached slices with their "as of" time; nothing is queued; reconnect reloads',()=>{
    const d=cache.dump();assert.equal(d.meta.format,'hcc-server-cache-1');assert.equal(d.meta.me.userId,1);
    assert.deepEqual(Object.keys(d.slices).sort(),['clock','date','tasks']);assert.ok(d.slices.tasks.asOf);
    assert.equal(offline.status,'ready');assert.equal(offline.readOnly,true);assert.equal(offline.fromCache,true);
    assert.deepEqual(offline.state.tasks.map(x=>x.id),['7','8']);assert.equal(offline.asOf,cachedAsOf);assert.equal(offline.state.users[0].id,'1');
    assert.equal(back.readOnly,false);assert.equal(back.fromCache,false);
    assert.equal(rs.calls.filter(c=>c.method==='POST').length,0,'nothing queued or replayed after reconnect');
  });

  // ── Sign-in, session end, user change ──
  let signedIn=false;let who=1;
  const as=fakeServer({'POST /auth/sign-in':({body})=>{if(body.password!=='right')return reply(401,{code:'UNAUTHENTICATED',message:'Wrong login name or password.'});signedIn=true;
      return {userId:who,displayName:'Owner',credentialKind:'PASSWORD',session:{idleExpiresUtc:'2026-10-24T05:00:00.000Z',absoluteExpiresUtc:'2026-10-24T16:00:00.000Z',idleTimeoutSeconds:1800},sessionToken:null};},
    'POST /auth/sign-out':()=>{signedIn=false;return undefined;},
    'GET /me/capabilities':()=>signedIn?capabilities({userId:who}):reply(401,{code:'UNAUTHENTICATED',message:'Sign in.'}),
    'GET /probe/tasks':()=>signedIn?{items:[{id:'u'+who}]}:reply(401,{code:'UNAUTHENTICATED',message:'Sign in.'}),
    'POST /commands/ProbeOne':()=>reply(401,{code:'UNAUTHENTICATED',message:'Session ended.'})});
  const acache=S.createMemoryCache();
  const art=await startRuntime(S,as,{areas:[S.core,probe],cache:acache});const out0=art.get();
  const bad=await art.signIn({loginName:'owner',password:'wrong'});const badMsg=art.get().signInMessage;
  const noSecret=await art.signIn({loginName:'owner'});
  const good=await art.signIn({loginName:' owner ',password:'right'});const in1=art.get();
  const signInCall=as.calls.find(c=>c.path==='/auth/sign-in'&&c.body.password==='right');
  const ended=await art.run({type:'PROBE',id:'7',amount:5});const out1=art.get();const cacheAfterEnd=acache.dump();
  await art.signIn({loginName:'owner',password:'right'});who=2;signedIn=true;await art.refresh();const swapped=art.get();
  await art.signOut();const out2=art.get();const cacheAfterOut=acache.dump();
  test('[W4-0] Sign-in: no session → sign-in screen; POST /auth/sign-in (cookie session, no Idempotency-Key, credentials never cached); errors shown',()=>{
    assert.equal(out0.status,'signed-out');assert.equal(out0.me,null);
    assert.equal(bad.ok,false);assert.equal(badMsg,'Sign in again: Wrong login name or password.');
    assert.equal(noSecret.ok,false);assert.match(noSecret.text,/password or your PIN/);
    assert.ok(good.ok);assert.equal(in1.status,'ready');assert.deepEqual(in1.state.tasks.map(x=>x.id),['u1']);
    assert.deepEqual(signInCall.body,{loginName:'owner',password:'right',returnToken:false});assert.equal(signInCall.key,undefined);
    assert.ok(!JSON.stringify(acache.dump()).includes('right'),'the password is never written to the cache');
  });
  test('[W4-0] Session end (401 on a command) → sign-in screen, cache cleared; another user → previous data dropped; sign-out clears the cache',()=>{
    assert.deepEqual([ended.ok,ended.reason],[false,'server']);assert.equal(out1.status,'signed-out');assert.match(out1.signInMessage,/session has ended/);
    assert.equal(cacheAfterEnd,null);assert.deepEqual(out1.state.tasks,[]);
    assert.equal(swapped.me.userId,2);assert.deepEqual(swapped.state.tasks.map(x=>x.id),['u2']);
    assert.equal(out2.status,'signed-out');assert.equal(cacheAfterOut,null);assert.ok(as.calls.some(c=>c.path==='/auth/sign-out'));
  });

  // ── Mapping helpers ──
  test('[W4-0] Mapping helpers: ids String(server id) ↔ number, IST clock ↔ UTC, paise/date guards, id directory snapshot',()=>{
    assert.equal(S.protoId(1201),'1201');assert.equal(S.serverId('1201'),1201);assert.throws(()=>S.serverId('HCC-1201','Booking'),e=>e.code==='VALIDATION');
    assert.equal(S.utcToClock('2026-10-24T04:30:00.000Z'),'2026-10-24T10:00');assert.equal(S.clockToUtc('2026-10-24T10:00'),'2026-10-24T04:30:00.000Z');
    assert.equal(S.utcToDate('2026-10-24T20:00:00Z'),'2026-10-25');assert.equal(S.paise(12345),12345);assert.throws(()=>S.paise('1'),e=>e.wireCode==='PROTOCOL');
    assert.throws(()=>S.isoDate('2026-02-30'),e=>e.wireCode==='PROTOCOL');
    const d=S.createIdDirectory();d.learn('room','105',7);d.setVersion('booking','1201','v1');const e=S.createIdDirectory(d.snapshot());
    assert.deepEqual([e.server('room','105'),e.proto('room',7),e.version('booking','1201'),e.list('room')],[7,'105','v1',['105']]);
    assert.equal(S.fitKey('x'.repeat(100)).length,80);assert.equal(S.fitKey('a b'),'a_b');
  });
}
