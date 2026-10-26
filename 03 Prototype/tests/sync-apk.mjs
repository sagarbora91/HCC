// W4-12 APK ↔ server: token sign-in (returnToken → Bearer), 401 → sign-in, sign-out revokes the server session, idle/absolute
// expiry, device registration (RegisterDevice + staff list) and PIN sign-in with X-HCC-Device, server address setup
// (discovery + health), the CapacitorHttp fetch adapter, and the production sign-in / setup / Settings screens (static render).
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {loadSync,fakeServer,reply,netDown,capabilities,settle} from './_sync-harness.mjs';

const kv=()=>{const m=new Map();return {m,getItem:k=>m.has(k)?m.get(k):null,setItem:(k,v)=>{m.set(k,String(v));},removeItem:k=>{m.delete(k);}};};
const T0=Date.parse('2026-10-24T04:30:00.000Z');
function signInResult(token,{userId=1,displayName='Test Owner',kind='PASSWORD'}={}){
  return {userId,displayName,credentialKind:kind,session:{idleExpiresUtc:'2026-10-24T05:00:00.000Z',absoluteExpiresUtc:'2026-10-24T16:30:00.000Z',idleTimeoutSeconds:1800},sessionToken:token};
}
/** Fake server whose session is the bearer token (no cookie): valid tokens in `live`. */
function tokenServer(){
  const live=new Set();let n=0;
  const srv=fakeServer({
    'POST /auth/sign-in':({body,headers})=>{
      if(body.pin!==undefined&&headers['X-HCC-Device']!=='dev-tok')return reply(403,{code:'FORBIDDEN',message:'PIN sign-in works only on a registered hotel device.'});
      if(body.password!==undefined&&body.password!=='right'||body.pin!==undefined&&body.pin!=='1234')return reply(401,{code:'UNAUTHENTICATED',message:'Wrong login name or password.'});
      const t='tok-'+(++n);live.add(t);return signInResult(body.returnToken?t:null,body.pin?{userId:5,displayName:'Rina Reception',kind:'PIN'}:{});},
    'POST /auth/sign-out':({headers})=>{live.delete(String(headers.Authorization??'').replace(/^Bearer /,''));return undefined;},
    'GET /me/capabilities':({headers})=>live.has(String(headers.Authorization??'').replace(/^Bearer /,''))?capabilities():reply(401,{code:'UNAUTHENTICATED',message:'Sign in again.'}),
  });
  return {srv,live};
}

export async function register({test,assert}){
  const S=await loadSync();
  const prevLs=Object.getOwnPropertyDescriptor(globalThis,'localStorage');
  const ls=kv();Object.defineProperty(globalThis,'localStorage',{value:ls,configurable:true,writable:true});
  try{
  // ── Token sign-in → Bearer on every request ──
  let clock=T0;const now=()=>clock;
  const {srv,live}=tokenServer();const store=S.memoryTokenStore();const auth=S.tokenAuth({store,now});
  const mk=(a,f)=>S.createSyncRuntime({areas:S.AREAS,fetch:a.observe(f),cache:S.createMemoryCache(),auth:a,commandRetries:0,cacheIterations:1000});
  await auth.ready();const rt=mk(auth,srv.fetch);
  await rt.boot();const bootStatus=rt.get().status;
  srv.reset();
  const wrong=await rt.signIn({loginName:'owner',password:'wrong'});
  const ok=await rt.signIn({loginName:' owner ',password:'right'});
  const signInCall=srv.calls.filter(c=>c.path==='/auth/sign-in').at(-1);
  const afterGets=srv.calls.filter(c=>c.method==='GET');
  const saved=JSON.parse(await store.load());
  test('[W4-12] Token sign-in: returnToken:true, token kept in the TokenStore, Authorization: Bearer on every later request (no cookie needed)',()=>{
    assert.equal(bootStatus,'signed-out');assert.equal(wrong.ok,false);assert.match(wrong.text,/Wrong login name/);
    assert.equal(ok.ok,true);assert.equal(rt.get().status,'ready');
    assert.deepEqual(signInCall.body,{loginName:'owner',password:'right',returnToken:true});
    assert.equal(signInCall.headers.Authorization,undefined,'no stale bearer on sign-in');
    assert.ok(afterGets.length>=2);for(const g of afterGets)assert.equal(g.headers.Authorization,'Bearer tok-1',g.path);
    assert.equal(saved.token,'tok-1');assert.equal(saved.userId,1);assert.equal(saved.idleTimeoutSeconds,1800);assert.equal(saved.absoluteExpiresUtc,'2026-10-24T16:30:00.000Z');
    assert.equal(auth.hasSession(),true);assert.equal(afterGets.some(g=>g.headers['X-HCC-Device']),false,'unregistered device sends no device header');
  });

  // ── 401 (session revoked / expired on the server) → back to sign-in, token dropped ──
  live.clear();await rt.refresh();
  const after401=rt.get();const storeAfter401=await store.load();
  test('[W4-12] 401 on a request with the token: token dropped from memory and storage, sign-in screen with "session has ended"',()=>{
    assert.equal(after401.status,'signed-out');assert.match(after401.signInMessage,/session has ended/);
    assert.equal(auth.hasSession(),false);assert.equal(storeAfter401,null);
  });

  // ── Sign-out revokes the server session ──
  await rt.signIn({loginName:'owner',password:'right'});const tokBefore=JSON.parse(await store.load()).token;
  srv.reset();await rt.signOut();
  const so=srv.calls.find(c=>c.path==='/auth/sign-out');
  await rt.refresh();const afterSignOut=srv.gets('/me/capabilities').at(-1);
  test('[W4-12] Sign-out: POST /auth/sign-out with the Bearer token (server revokes it), then token gone and no Authorization header',()=>{
    assert.equal(tokBefore,'tok-2');assert.equal(so.method,'POST');assert.equal(so.headers.Authorization,'Bearer tok-2');
    assert.equal(live.has('tok-2'),false);assert.equal(rt.get().status,'signed-out');
    assert.equal(auth.hasSession(),false);assert.equal(afterSignOut?.headers.Authorization,undefined);
  });

  // ── Stored session: restart, idle and absolute expiry ──
  await rt.signIn({loginName:'owner',password:'right'});
  const auth2=S.tokenAuth({store,now});await auth2.ready();const restored=auth2.hasSession();
  const rt2=mk(auth2,srv.fetch);await rt2.boot();const rt2Status=rt2.get().status;
  clock=T0+29*60_000;const idle29=auth2.expired();
  await rt2.refresh();clock=T0+29*60_000+29*60_000;const afterActivity=auth2.expired();
  clock+=2*60_000;const idle31=auth2.expired();
  const auth3=S.tokenAuth({store,now});await auth3.ready();const idleDropped=!auth3.hasSession()&&(await store.load())===null;
  await rt2.signIn({loginName:'owner',password:'right'});clock=Date.parse('2026-10-24T16:30:00.000Z');const abs=auth2.expired();
  const auth4=S.tokenAuth({store,now});await auth4.ready();
  test('[W4-12] Stored session survives a restart; idle timeout counts from this device\'s last answered request; absolute lifetime ends it',()=>{
    assert.equal(restored,true);assert.equal(rt2Status,'ready');
    assert.equal(idle29,false);assert.equal(afterActivity,false,'a request resets the idle clock');assert.equal(idle31,true);
    assert.equal(idleDropped,true,'an idle-expired stored token is dropped on start');
    assert.equal(abs,true);assert.equal(auth4.hasSession(),false);
  });
  clock=T0;

  // ── Token stores ──
  const ss=kv();const st=S.sessionTokenStore(ss);await st.save('x');const stLoad=await st.load();await st.clear();
  const prefsCalls=[];const prefsMap=new Map();
  const prefs={get:async({key})=>{prefsCalls.push('get:'+key);return {value:prefsMap.get(key)??null};},set:async({key,value})=>{prefsCalls.push('set:'+key);prefsMap.set(key,value);},remove:async({key})=>{prefsCalls.push('remove:'+key);prefsMap.delete(key);}};
  const pt=S.preferencesTokenStore(prefs);await pt.save('y');const ptLoad=await pt.load();await pt.clear();
  test('[W4-12] Token stores: Capacitor Preferences through the bridge (kept across restarts), sessionStorage fallback (until the app closes)',()=>{
    assert.equal(st.kind,'session');assert.equal(stLoad,'x');assert.equal(ss.m.size,0);assert.ok(!ss.m.has(S.SESSION_TOKEN_KEY));
    assert.equal(pt.kind,'preferences');assert.equal(ptLoad,'y');assert.deepEqual(prefsCalls,['set:hcc-session-token','get:hcc-session-token','remove:hcc-session-token']);
    assert.equal(S.sessionTokenStore(null).kind,'memory');
  });

  // ── Device registration by an admin, then staff pick + PIN ──
  const users=[{userId:1,loginName:'owner',displayName:'Test Owner',enabled:true,credentialKind:'PASSWORD',lockedOut:false,roles:[],version:'1'},
    {userId:5,loginName:'rina',displayName:'Rina Reception',enabled:true,credentialKind:'PIN',lockedOut:false,roles:[],version:'1'},
    {userId:6,loginName:'arun',displayName:'Arun Cashier',enabled:true,credentialKind:'PIN',lockedOut:false,roles:[],version:'1'},
    {userId:7,loginName:'old',displayName:'Old Staff',enabled:false,credentialKind:'PIN',lockedOut:false,roles:[],version:'1'}];
  let replay=false;
  srv.on('POST /commands/RegisterDevice',({body})=>({deviceId:9,deviceToken:replay?null:'dev-tok',_label:body.label}));
  srv.on('GET /admin/users',({query})=>({items:users.slice((query.page-1)*2,query.page*2),page:Number(query.page),pageSize:Number(query.pageSize),total:users.length}));
  ls.m.clear();
  const id=S.deviceId();const id2=S.deviceId();
  srv.reset();
  const reg=await S.registerThisDevice(rt2.transport,'  Front desk tablet ');
  const regCall=srv.commands('RegisterDevice')[0];const userPages=srv.gets('/admin/users').map(c=>c.query.page);
  const staff=S.deviceStaff();const staffRaw=ls.getItem(S.DEVICE_STAFF_KEY);const staffLogins=[];for(const x of staff)staffLogins.push(await S.resolveStaffLogin(x.id));
  replay=true;const reg2=await S.registerThisDevice(rt2.transport,'Again');replay=false;
  const empty=await S.registerThisDevice(rt2.transport,'  ');
  test('[W4-12] Register this device (admin session): RegisterDevice{label: name · device id}, device token stored once, PIN staff list read from /admin/users',()=>{
    assert.match(id,/^[0-9a-f-]{8,}$/);assert.equal(id2,id,'device id generated once');
    assert.equal(reg.ok,true,reg.text);assert.equal(reg.deviceId,9);assert.equal(reg.staff,2);
    assert.deepEqual(regCall.body,{label:'Front desk tablet · '+id.slice(0,8)});assert.match(regCall.key,/^RegisterDevice:/);
    assert.equal(regCall.headers.Authorization,'Bearer tok-4');
    assert.equal(ls.getItem(S.DEVICE_TOKEN_KEY),'dev-tok');assert.equal(S.deviceRegistered(),true);assert.equal(S.deviceLabel(),'Front desk tablet · '+id.slice(0,8));
    assert.deepEqual(userPages,['1','2']);
    assert.deepEqual(staff.map(x=>x.displayName),['Arun Cashier','Rina Reception'],'enabled PIN users only, sorted by name');
    assert.deepEqual(staffLogins,['arun','rina'],'the opaque id opens to the login name with this device key');
    // F-L09: the stored roster is display name + opaque id only — no login name in device storage.
    for(const x of staff){assert.deepEqual(Object.keys(x).sort(),['displayName','id']);assert.match(x.id,/^hccs1\./);}
    assert.ok(!/arun|rina|loginName/.test(staffRaw.replace(/Arun Cashier|Rina Reception/g,'')),'no login names stored: '+staffRaw);
    assert.equal(reg2.ok,false);assert.match(reg2.text,/did not return its key/);assert.equal(ls.getItem(S.DEVICE_TOKEN_KEY),'dev-tok','a replay keeps the stored token');
    assert.equal(empty.ok,false);
    assert.ok(S.deviceServerLabel('x'.repeat(80),id).length<=60);
  });

  await rt2.signOut();srv.reset();
  const pin=await rt2.signIn({loginName:'rina',pin:'1234'});
  const pinCall=srv.calls.find(c=>c.path==='/auth/sign-in');const pinGet=srv.gets('/me/capabilities').at(-1);
  await S.rememberStaff({loginName:'kiran',displayName:'Kiran Kitchen'});await S.rememberStaff({loginName:'RINA',displayName:'dup'});
  const remembered=[];for(const x of S.deviceStaff())remembered.push(await S.resolveStaffLogin(x.id));
  const oldId=S.deviceStaff()[0].id;
  await S.forgetDevice();const forgotten=[S.deviceRegistered(),S.deviceStaff().length,S.deviceId()===id];
  await S.adoptDeviceRegistration('other-device-token','Other');const foreign=await S.resolveStaffLogin(oldId);await S.forgetDevice();
  await rt2.signOut();const unreg=await rt2.signIn({loginName:'rina',pin:'1234'});
  test('[W4-12] PIN sign-in on the registered device: X-HCC-Device + pin + returnToken; without the device token the server refuses it',()=>{
    assert.equal(pin.ok,true,pin.text);
    assert.deepEqual(pinCall.body,{loginName:'rina',pin:'1234',returnToken:true});assert.equal(pinCall.headers['X-HCC-Device'],'dev-tok');
    assert.equal(pinGet.headers['X-HCC-Device'],'dev-tok');assert.match(pinGet.headers.Authorization,/^Bearer tok-/);
    assert.deepEqual(remembered,['arun','kiran','rina']);
    assert.deepEqual(forgotten,[false,0,true],'forgetting the registration keeps the device id');
    assert.equal(foreign,null,'a roster id cannot be opened with another registration key');
    assert.equal(unreg.ok,false);assert.match(unreg.text,/registered hotel device/);
  });

  // ── Server address setup ──
  const n=S.normaliseServerUrl;
  const disc=fakeServer({'GET /system/discovery':()=>({product:'Hotel Control Centre',serverName:'Hotel PC',version:'0.1.0',apiVersion:'v1',apiBasePath:'/api/v1'})});
  const good=await S.probeServer(' hcc-server.local:8443/api/v1/ ',disc.fetch);const goodUrls=disc.calls.map(c=>c.url);
  const other=fakeServer({'GET /system/discovery':()=>({product:'Something else'})});const notHcc=await S.probeServer('https://10.0.2.2:5443',other.fetch);
  const nf=await S.probeServer('https://10.0.2.2:5443',fakeServer({}).fetch);
  const sick=await S.probeServer('https://10.0.2.2:5443',fakeServer({'GET /system/discovery':()=>({product:'Hotel Control Centre'}),'GET /system/health':()=>({status:'degraded',database:'down'})}).fetch);
  const down=await S.probeServer('https://10.0.2.2:5443',async()=>{throw netDown();});
  const tls=await S.probeServer('https://10.0.2.2:5443',async()=>{throw new Error('java.security.cert.CertPathValidatorException: Trust anchor for certification path not found.');});
  const http=await S.probeServer('http://10.0.2.2:5080',disc.fetch);
  const sv=kv();S.saveServerUrl('https://10.0.2.2:5443',sv);
  test('[W4-12] Server address: https origin only, tested with /system/discovery ("Hotel Control Centre") + /system/health before it is saved as hcc-server-url',()=>{
    assert.deepEqual(n('https://hcc-server.local:8443/'),{ok:true,url:'https://hcc-server.local:8443'});
    assert.deepEqual(n('192.168.1.20:5443'),{ok:true,url:'https://192.168.1.20:5443'});
    assert.deepEqual(n('https://10.0.2.2:5443/api/v1'),{ok:true,url:'https://10.0.2.2:5443'});
    for(const bad of ['','http://192.168.1.20:5080','https://x/other','https://u:p@x','https://x/?a=1','ftp://x'])assert.equal(n(bad).ok,false,bad);
    assert.deepEqual(good,{ok:true,url:'https://hcc-server.local:8443',serverName:'Hotel PC',version:'0.1.0',database:'ok'});
    assert.deepEqual(goodUrls,['https://hcc-server.local:8443/api/v1/system/discovery','https://hcc-server.local:8443/api/v1/system/health']);
    assert.match(notHcc.text,/not a Hotel Control Centre server/);assert.match(nf.text,/not a Hotel Control Centre server/);
    assert.match(sick.text,/not ready \(status: degraded\)/);
    assert.match(down.text,/Cannot reach https:\/\/10\.0\.2\.2:5443/);
    assert.match(tls.text,/certificate is not trusted by this app/);
    assert.match(http.text,/https:\/\//);
    assert.equal(sv.getItem(S.SERVER_URL_KEY),'https://10.0.2.2:5443');assert.equal(S.SERVER_URL_KEY,'hcc-server-url');
  });

  // ── CapacitorHttp fetch adapter ──
  const reqs=[];const answers=[{status:200,data:{a:1}},{status:204,data:''},{status:401,data:{code:'UNAUTHENTICATED',message:'x'}},{status:200,data:'plain'}];
  const cf=S.capacitorFetch({request:async o=>{reqs.push(o);return answers[reqs.length-1];}},9000);
  const r1=await cf('https://h/api/v1/commands/X',{method:'POST',headers:{'Content-Type':'application/json',Authorization:'Bearer t'},body:'{"k":1}'});
  const r2=await cf('https://h/api/v1/auth/sign-out',{method:'POST'});const r3=await cf('https://h/x');const r4=await cf('https://h/y');
  const t1=await r1.text(),t2=await r2.text(),t3=await r3.text(),t4=await r4.text();
  const ctl=new AbortController();const hang=S.capacitorFetch({request:()=>new Promise(()=>{})});const aborted=settle(()=>hang('https://h/z',{signal:ctl.signal}));ctl.abort();const ab=await aborted;
  test('[W4-12] CapacitorHttp adapter: method/headers/raw JSON body/timeouts passed; parsed JSON re-serialised; status kept; abort rejects',()=>{
    assert.deepEqual(reqs[0],{url:'https://h/api/v1/commands/X',method:'POST',headers:{'Content-Type':'application/json',Authorization:'Bearer t'},data:'{"k":1}',responseType:'text',connectTimeout:9000,readTimeout:9000});
    assert.equal(reqs[2].method,'GET');assert.equal(reqs[1].data,undefined);
    assert.deepEqual([r1.status,r1.ok,t1],[200,true,'{"a":1}']);assert.deepEqual([r2.status,t2],[204,'']);assert.deepEqual([r3.status,r3.ok],[401,false]);assert.equal(t3,'{"code":"UNAUTHENTICATED","message":"x"}');assert.equal(t4,'plain');
    assert.equal(ab.ok,false);
  });

  // ── Production screens (static render) ──
  const dir='tests/.generated';await mkdir(dir,{recursive:true});
  await writeFile(dir+'/apk-screens-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {ServerSignIn,ServerSetup,ServerDevicePanel} from '../../src/clients/sync/react';
export const signIn=(m?:string)=>renderToStaticMarkup(<ServerSignIn message={m}/>);
export const setup=()=>renderToStaticMarkup(<ServerSetup onSaved={()=>{}}/>);
export const panel=()=>renderToStaticMarkup(<ServerDevicePanel/>);
`);
  await build({entryPoints:[dir+'/apk-screens-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:dir+'/apk-screens.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"production"'},banner:{js:"import {createRequire as __apReq} from 'node:module';const require=__apReq(import.meta.url);"}});
  const V=await import(new URL('./.generated/apk-screens.mjs',import.meta.url).href);
  ls.m.clear();const unregHtml=V.signIn();const setupHtml=V.setup();const panelUnreg=V.panel();
  await S.adoptDeviceRegistration('dev-tok','Front desk tablet · abcd1234');
  await S.rememberStaff({loginName:'rina',displayName:'Rina Reception'});await S.rememberStaff({loginName:'arun',displayName:'Arun Cashier'});
  const regHtml=V.signIn('Your session has ended. Sign in again.');const panelReg=V.panel();
  test('[W4-12] Screens: unregistered device = password sign-in + register hint; registered = staff names + PIN, password for Owner/Accountant/Tech admin; setup; Settings panel',()=>{
    assert.match(unregHtml,/Password/);assert.doesNotMatch(unregHtml,/aria-label="Sign in with"/);assert.match(unregHtml,/registered hotel device/);
    assert.match(regHtml,/Staff PIN/);assert.match(regHtml,/Rina Reception/);assert.doesNotMatch(regHtml.replace(/Rina Reception|Arun Cashier/g,''),/rina|arun/i,'no login names on the screen');assert.match(regHtml,/Arun Cashier/);assert.match(regHtml,/Someone else/);
    assert.match(regHtml,/Owner \/ Accountant \/ Tech admin \(password\)/);assert.match(regHtml,/Hotel device · Front desk tablet · abcd1234/);assert.match(regHtml,/session has ended/);
    assert.match(setupHtml,/Server address/);assert.match(setupHtml,/Test connection/);assert.match(setupHtml,/Save and continue/);
    assert.match(panelUnreg,/Server and this device/);assert.match(panelUnreg,/not registered/);
    assert.match(panelReg,/Registered device: <strong>Front desk tablet · abcd1234<\/strong> · 2 staff/);
  });
  }finally{
    if(prevLs)Object.defineProperty(globalThis,'localStorage',prevLs);else delete globalThis.localStorage;
  }
}
