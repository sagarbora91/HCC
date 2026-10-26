// Wave 4 gap G1 (DEC-062): Access control › Sign-in & devices (production only) — set / reset a password or PIN (SetCredential),
// list / register / revoke hotel devices (GET /admin/devices, RegisterDevice, RevokeDevice) through the admin area's mappers on a
// fake server shaped like Hcc.Contracts.Access. Secrets go only into the SetCredential body: never in the state, cache, keys or logs.
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {loadSync,loadPages,fakeServer,startRuntime,reply,netDown} from './_sync-harness.mjs';

const kv=()=>{const m=new Map();return {m,getItem:k=>m.has(k)?m.get(k):null,setItem:(k,v)=>{m.set(k,String(v));},removeItem:k=>{m.delete(k);}};};
const users=[
  {userId:1,loginName:'owner',displayName:'Test Owner',enabled:true,credentialKind:'PASSWORD',lockedOut:false,roles:[{roleCode:'OWN',validFromUtc:'2026-01-01T00:00:00.000Z',validUntilUtc:null}],version:'VU1'},
  {userId:2,loginName:'rohan',displayName:'Rohan',enabled:true,credentialKind:'NONE',lockedOut:false,roles:[{roleCode:'REC',validFromUtc:'2026-10-01T00:00:00.000Z',validUntilUtc:null}],version:'VU2'},
  {userId:3,loginName:'lata',displayName:'Lata',enabled:true,credentialKind:'PASSWORD',lockedOut:true,roles:[{roleCode:'TAD',validFromUtc:'2026-01-01T00:00:00.000Z',validUntilUtc:null}],version:'VU3'},
  // Expired Accountant role + live Housekeeper: still a password user (any password role, DEC-062).
  {userId:4,loginName:'asha',displayName:'Asha',enabled:true,credentialKind:'PIN',lockedOut:false,roles:[{roleCode:'HKP',validFromUtc:'2026-01-01T00:00:00.000Z',validUntilUtc:null},
    {roleCode:'ACC',validFromUtc:'2025-01-01T00:00:00.000Z',validUntilUtc:'2025-12-31T18:30:00.000Z'}],version:'VU4'},
];
const devices=[
  {deviceId:5,label:'Old tablet · 00000000',registeredUtc:'2026-09-01T04:00:00.000Z',lastSeenUtc:null,revokedUtc:'2026-10-01T04:00:00.000Z'},
  {deviceId:6,label:'Front desk tablet · abcd1234',registeredUtc:'2026-10-20T04:00:00.000Z',lastSeenUtc:'2026-10-24T03:00:00.000Z',revokedUtc:null},
  {deviceId:7,label:'Housekeeping phone · ffff0000',registeredUtc:'2026-10-21T04:00:00.000Z',lastSeenUtc:null,revokedUtc:null},
];
const PIN='482913',PIN2='705318',PASSWORD='Sand-Residency-2026!';

export async function register({test:outerTest,assert}){
  const S=await loadSync();
  const prevLs=Object.getOwnPropertyDescriptor(globalThis,'localStorage');
  const ls=kv();Object.defineProperty(globalThis,'localStorage',{value:ls,configurable:true,writable:true});
  const logs=[];const orig={log:console.log,warn:console.warn,error:console.error,info:console.info,debug:console.debug};
  // Capture what the sync layer logs while it works; the runner's own PASS / FAIL lines (inside test) go through unchanged.
  let inTest=false;
  for(const k of Object.keys(orig))console[k]=(...a)=>{if(inTest)return orig[k](...a);logs.push(a.map(String).join(' '));};
  const test=(name,fn)=>{inTest=true;try{outerTest(name,fn);}finally{inTest=false;}};
  try{
  // ── Rules mirror the server (CredentialPolicy / PinRules / PasswordRules) ──
  test('[W4 G1] DEC-062 kind per roles and the PIN / password rules mirror the server',()=>{
    assert.equal(S.requiredKind(['OWN']),'PASSWORD');assert.equal(S.requiredKind(['REC','MGR']),'PIN');assert.equal(S.requiredKind(['HKP','ACC']),'PASSWORD');assert.equal(S.requiredKind([]),null);
    for(const p of ['123456','654321','111111','121212','123123','123321','112233','110011','147258'])assert.ok(S.pinIsTrivial(p),p);
    for(const p of [PIN,PIN2,'903517'])assert.ok(!S.pinIsTrivial(p),p);
    assert.match(S.credentialProblem('PIN','12345','12345','rohan'),/exactly 6 digits/);
    assert.match(S.credentialProblem('PIN','12a456','12a456','rohan'),/exactly 6 digits/);
    assert.match(S.credentialProblem('PIN','123456','123456','rohan'),/too easy/);
    assert.match(S.credentialProblem('PIN',PIN,PIN2,'rohan'),/do not match/);
    assert.equal(S.credentialProblem('PIN',PIN,PIN,'rohan'),undefined);
    assert.match(S.credentialProblem('PASSWORD','short','short','lata'),/12–128/);
    assert.match(S.credentialProblem('PASSWORD','my-name-is-lata-ok','my-name-is-lata-ok','lata'),/sign-in name/);
    assert.match(S.credentialProblem('PASSWORD','abababababab','abababababab','lata'),/too simple/);
    assert.equal(S.credentialProblem('PASSWORD',PASSWORD,PASSWORD,'lata'),undefined);
  });

  // ── Fake server ──
  let ver=10;const pinHistory=new Set(['903517']);
  const srv=fakeServer({
    'GET /admin/users':()=>({items:users,page:1,pageSize:100,total:users.length}),
    'GET /admin/devices':()=>({items:devices,page:1,pageSize:100,total:devices.length}),
    'GET /admin/roles':()=>({roles:[],permissions:[]}),'GET /admin/exceptions':()=>({items:[]}),'GET /admin/audit':()=>({items:[],page:1,pageSize:100,total:0}),
    'GET /configuration/values':()=>({businessDate:'2026-10-24',values:{},scheduled:[]}),'GET /configuration/history':()=>({items:[],page:1,pageSize:100,total:0}),
    'GET /configuration/requests':()=>({items:[]}),
    'POST /commands/SetCredential':({body})=>body.pin&&pinHistory.has(body.pin)?reply(400,{code:'VALIDATION',message:'Choose a PIN you have not used for your last 5 PINs.'})
      :({userId:body.userId,version:'VC'+(++ver)}),
    'POST /commands/RegisterDevice':({body})=>({deviceId:8,deviceToken:'tok-once-'+body.label.length}),
    'POST /commands/RevokeDevice':({body})=>({deviceId:body.deviceId,sessionsRevoked:1}),
  });
  const cache=S.createMemoryCache();
  const rt=await startRuntime(S,srv,{areas:S.AREAS.filter(a=>['core','reservations','frontdesk','admin'].includes(a.id)),cache});
  const get=(p,q)=>rt.transport.get(p,q);
  const run=cmd=>rt.run(cmd);

  ls.setItem(S.DEVICE_ID_KEY,'abcd1234-0000-4000-8000-000000000000');ls.setItem(S.DEVICE_TOKEN_KEY,'dev-tok');ls.setItem(S.DEVICE_LABEL_KEY,'Front desk tablet · abcd1234');
  const dir=await S.loadCredentialDirectory(get,S.thisDeviceTag(true));
  test('[W4 G1] Directory: users with credential state and DEC-062 kind; devices active first, this device marked',()=>{
    assert.deepEqual(dir.users.map(u=>[u.id,u.has,u.needs,u.lockedOut]),[['1','PASSWORD','PASSWORD',false],['2','NONE','PIN',false],['3','PASSWORD','PASSWORD',true],['4','PIN','PASSWORD',false]]);
    assert.deepEqual(dir.devices.map(d=>[d.id,d.active,d.thisDevice]),[['6',true,true],['7',true,false],['5',false,false]]);
    assert.deepEqual(srv.gets('/admin/devices')[0].query,{page:'1',pageSize:'100'});
  });
  const rohan=dir.users[1],lata=dir.users[2],owner=dir.users[0];

  // ── Credentials ──
  srv.reset();
  const weak=await S.setCredential(run,rohan,'PIN','123456','123456','credential:2:n1:1');
  const mismatch=await S.setCredential(run,rohan,'PIN',PIN,PIN2,'credential:2:n1:1');
  const sentBefore=srv.commands('SetCredential').length;
  const reused=await S.setCredential(run,rohan,'PIN','903517','903517','credential:2:n1:1');
  const pinOk=await S.setCredential(run,rohan,'PIN',PIN,PIN,'credential:2:n1:2');
  const afterPin=srv.calls.map(c=>`${c.method} ${c.path}`);
  const pwOk=await S.setCredential(run,lata,'PASSWORD',PASSWORD,PASSWORD,'credential:3:n2:1');
  const self=await S.setCredential(run,owner,'PASSWORD',PASSWORD,PASSWORD,'credential:1:n3:1');
  srv.down=true;const lost=await S.setCredential(run,rohan,'PIN',PIN2,PIN2,'credential:2:n4:1');srv.down=false;await rt.transport.get('/system/health');
  test('[W4 G1] SetCredential: weak / mismatched refused before sending; server rule surfaced; PIN and password bodies, key without the secret, users reload',()=>{
    assert.deepEqual([weak.ok,mismatch.ok,sentBefore],[false,false,0]);assert.match(weak.text,/too easy/);assert.match(mismatch.text,/do not match/);
    assert.deepEqual([reused.ok,reused.definitive],[false,true]);assert.match(reused.text,/not used for your last 5 PINs/);
    assert.ok(pinOk.ok,pinOk.text);assert.match(pinOk.text,/not shown again/);assert.doesNotMatch(pinOk.text,new RegExp(PIN));
    const c=srv.commands('SetCredential');
    assert.deepEqual([c[1].body,c[1].key],[{userId:2,password:null,pin:PIN,expectedVersion:'VU2'},'credential:2:n1:2']);
    assert.deepEqual(afterPin.slice(afterPin.lastIndexOf('POST /commands/SetCredential')+1),['GET /admin/users'],'only the users slice reloads');
    assert.ok(pwOk.ok,pwOk.text);assert.match(pwOk.text,/lockout is cleared/);
    assert.deepEqual([c[2].body,c[2].key],[{userId:3,password:PASSWORD,pin:null,expectedVersion:'VU3'},'credential:3:n2:1']);
    assert.deepEqual([self.ok,self.definitive],[false,true]);assert.match(self.text,/another administrator/);assert.ok(!c.some(x=>x.body.userId===1),'own account never sent');assert.equal(c.length,4,'3 answered + 1 lost');
    assert.deepEqual([lost.ok,lost.definitive],[false,false],'a lost answer keeps the attempt (same key on retry)');
    for(const x of srv.calls)assert.ok(!String(x.key??'').includes(PIN)&&!String(x.key??'').includes(PASSWORD)&&!x.url.includes(PIN),'secret in key/url');
  });

  // ── Devices ──
  ls.removeItem(S.DEVICE_TOKEN_KEY);ls.removeItem(S.DEVICE_LABEL_KEY);srv.reset();
  const regEmpty=await S.registerDeviceHere(run,rt.transport,'  ','r1');
  const reg=await S.registerDeviceHere(run,rt.transport,'Front desk tablet','r1');
  const regCalls=srv.calls.map(c=>`${c.method} ${c.path}`);const kept=[ls.getItem(S.DEVICE_TOKEN_KEY),ls.getItem(S.DEVICE_LABEL_KEY)];
  const dir2=await S.loadCredentialDirectory(get,S.thisDeviceTag(S.deviceRegistered()));
  srv.reset();
  const rvOther=await S.revokeDevice(run,dir2.devices.find(d=>d.id==='7'));
  const stillRegistered=S.deviceRegistered();
  const rvThis=await S.revokeDevice(run,dir2.devices.find(d=>d.id==='6'));
  test('[W4 G1] Devices: register THIS device (RegisterDevice, token kept only in device storage, staff list read); revoke any device (RevokeDevice); revoking this one drops it here',()=>{
    assert.deepEqual([regEmpty.ok,srv.commands('RegisterDevice').length>=0],[false,true]);
    assert.ok(reg.ok,reg.text);
    assert.deepEqual(regCalls,['POST /commands/RegisterDevice','GET /admin/users']);
    assert.deepEqual(kept,['tok-once-28','Front desk tablet · abcd1234']);
    assert.deepEqual(dir2.devices.filter(d=>d.thisDevice).map(d=>d.id),['6'],'label tag marks this device');
    const r=srv.commands('RevokeDevice');
    assert.deepEqual(r.map(x=>[x.body,x.key]),[[{deviceId:7},'device-revoke:7'],[{deviceId:6},'device-revoke:6']]);
    assert.deepEqual([rvOther.ok,stillRegistered,rvThis.ok,S.deviceRegistered()],[true,true,true,false]);assert.match(rvThis.text,/registration was removed/);
  });
  const regCmd=(await (async()=>{srv.reset();ls.removeItem(S.DEVICE_TOKEN_KEY);await S.registerDeviceHere(run,undefined,'Bar tablet','r2');return srv.commands('RegisterDevice')[0];})());
  test('[W4 G1] RegisterDevice body {label: "<name> · <device id 8>"}, key device-register:<id>:<nonce>; no reload of slices',()=>{
    assert.deepEqual([regCmd.body,regCmd.key],[{label:'Bar tablet · abcd1234'},'device-register:abcd1234:r2']);
    assert.deepEqual(srv.calls.map(c=>`${c.method} ${c.path}`),['POST /commands/RegisterDevice']);
  });

  // ── Nothing secret kept ──
  await new Promise(r=>setTimeout(r,20));
  const stateJson=JSON.stringify(rt.get().state),cacheJson=JSON.stringify(cache.dump()),lsJson=JSON.stringify([...ls.m]);
  test('[W4 G1] PIN / password never kept: not in the assembled state, the read cache, device storage, Idempotency-Keys or logs',()=>{
    for(const secret of [PIN,PASSWORD,'903517']){
      assert.ok(!stateJson.includes(secret),'state holds '+secret);assert.ok(!cacheJson.includes(secret),'cache holds '+secret);
      assert.ok(!lsJson.includes(secret),'storage holds '+secret);assert.ok(!logs.some(l=>l.includes(secret)),'log holds '+secret);
    }
    assert.ok(!cacheJson.includes('/admin/devices')&&!stateJson.includes('Housekeeping phone'),'device list is read on demand, not cached');
  });
  void netDown;

  // ── Screens ──
  const gen='tests/.generated';await mkdir(gen,{recursive:true});
  await writeFile(gen+'/credentials-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {CredentialsView,CredentialsPanel} from '../../src/clients/sync/CredentialsPanel';
const noop=async()=>({ok:true as const,text:''});
export const view=(dir:any,open?:string,admin=true,registered=true)=>renderToStaticMarkup(<CredentialsView admin={admin} uid="1" dir={dir} initialUser={open} registered={registered}
  onReload={()=>{}} onSetCredential={noop} onRegister={noop} onRevoke={noop}/>);
export const panel=()=>renderToStaticMarkup(<CredentialsPanel uid="1"/>);
`);
  await build({entryPoints:[gen+'/credentials-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:gen+'/credentials-screens.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"production"'},banner:{js:"import {createRequire as __crReq} from 'node:module';const require=__crReq(import.meta.url);"}});
  const V=await import(new URL('./.generated/credentials-screens.mjs',import.meta.url).href);
  const pinForm=V.view(dir,'2'),pwForm=V.view(dir,'3'),unreg=V.view(dir,undefined,true,false),denied=V.view(dir,undefined,false),loading=V.view(null),container=V.panel();
  const P=await loadPages();
  const pages=[P.renderPages(rt.get().state,'1',false),P.renderPages(rt.get().state,'1',true)];
  test('[W4 G1] Panel renders: PIN / password forms (masked, typed twice, rules shown, no value), devices with revoke, register offer, non-admin note; Access control renders on desktop and phone',()=>{
    assert.match(pinForm,/Set PIN/);assert.match(pinForm,/6 digits, no runs or repeats/);assert.match(pinForm,/Type it again/);
    assert.equal((pinForm.match(/type="password"/g)||[]).length,2);assert.match(pinForm,/inputMode="numeric"|inputmode="numeric"/);assert.doesNotMatch(pinForm,/ value="/);
    assert.match(pinForm,/Not set — cannot sign in yet/);assert.match(pinForm,/Front desk tablet · abcd1234/);assert.match(pinForm,/Active · this device/);assert.match(pinForm,/Revoke device/);
    assert.match(pinForm,/Revoked devices \(1\)/);assert.match(pinForm,/Your own password/);assert.doesNotMatch(pinForm,/Register this device/);
    assert.match(pwForm,/Reset password/);assert.match(pwForm,/12–128 characters/);assert.match(pwForm,/locked out/);
    assert.match(unreg,/Register this device/);assert.match(denied,/Only the Owner or Tech admin/);assert.match(loading,/Loading users/);
    assert.match(container,/Only the Owner or Tech admin/,'signed-out runtime singleton → no admin');
    assert.deepEqual(pages.map(x=>x.errors),[{},{}]);
  });
  }finally{
    Object.assign(console,orig);
    if(prevLs)Object.defineProperty(globalThis,'localStorage',prevLs);else delete globalThis.localStorage;
  }
}
