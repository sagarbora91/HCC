// A02b-05 · "Change my password / PIN" in the demo build: the mock command OWN_CREDENTIAL_CHANGE applies the server's rules
// (Hcc.Domain.Access: DEC-062 kind per role, PinRules, PasswordRules, OwnCredentialRules, last-3 PIN history) for every signed-in user;
// only a digest is kept, never the secret. Screens: the sheet (desktop dialog + phone sheet), the profile menu and the menu drawer entry.
// Names prefixed [OWN-CRED].
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  const DEMO_PIN=M.demoPin(),DEMO_PASSWORD=M.demoPassword();
  const cmd=(current,next,kind,key)=>kind==='PIN'?{type:'OWN_CREDENTIAL_CHANGE',current,newPin:next,...(key?{key}:{})}:{type:'OWN_CREDENTIAL_CHANGE',current,newPassword:next,...(key?{key}:{})};
  const refused=(s,uid,c)=>{try{M.execute(s,uid,c);return null;}catch(e){return {code:e.code,message:e.message};}};

  test('[OWN-CRED] kind follows the role (DEC-062): Owner, Accountant, Tech admin → password; everyone else → PIN; same as the server role codes',()=>{
    assert.deepEqual(['Owner','Accountant','Tech admin','Manager','Reception','Cashier','Supervisor','Housekeeper','Maintenance','Restaurant'].map(M.kindOfRole),
      ['PASSWORD','PASSWORD','PASSWORD','PIN','PIN','PIN','PIN','PIN','PIN','PIN']);
    assert.deepEqual([M.requiredKind(['OWN']),M.requiredKind(['ACC']),M.requiredKind(['TAD']),M.requiredKind(['MGR','REC']),M.requiredKind(['HKP','ACC']),M.requiredKind([])],
      ['PASSWORD','PASSWORD','PASSWORD','PIN','PASSWORD',null]);
  });

  test('[OWN-CRED] PIN user (Reception): current PIN required and checked, server PIN rules, not the current one, then a digest only',()=>{
    const s=M.seed();
    assert.deepEqual(refused(s,'reception',cmd('000000','305718','PIN')),{code:'FORBIDDEN',message:'The current password or PIN is wrong.'});
    assert.deepEqual(refused(s,'reception',cmd(DEMO_PIN,'30571','PIN')),{code:'VALIDATION',message:'The PIN must be exactly 6 digits.'});
    assert.deepEqual(refused(s,'reception',cmd(DEMO_PIN,'30a718','PIN')),{code:'VALIDATION',message:'The PIN must be exactly 6 digits.'});
    for(const weak of ['123456','111111','121212','123123','123321','147258','110011'])
      assert.deepEqual(refused(s,'reception',cmd(DEMO_PIN,weak,'PIN')),{code:'VALIDATION',message:'This PIN is too easy to guess. Choose one without runs or repeats.'},weak);
    assert.deepEqual(refused(s,'reception',cmd(DEMO_PIN,DEMO_PIN,'PIN')),{code:'VALIDATION',message:'Choose a new PIN: it must be different from the current one.'});
    assert.deepEqual(refused(s,'reception',cmd(DEMO_PIN,'A-long-enough-pass-1','PASSWORD')),{code:'VALIDATION',message:'Your account does not use a PASSWORD.'});
    assert.deepEqual(refused(s,'reception',{type:'OWN_CREDENTIAL_CHANGE',current:DEMO_PIN}),{code:'VALIDATION',message:'Give either a password or a PIN.'});
    const next=M.execute(s,'reception',cmd(DEMO_PIN,'305718','PIN','own-credential:reception:n1:1'));
    const json=JSON.stringify(next);
    assert.ok(!json.includes('305718')&&!json.includes(DEMO_PIN),'no secret in the state');
    assert.deepEqual(Object.keys(next.credentialsX),['reception']);assert.equal(next.credentialsX.reception.kind,'PIN');
    assert.equal(next.audit[0].detail,'Own PIN changed.');assert.equal(next.audit[0].action,'OWN CREDENTIAL CHANGE');
    assert.deepEqual(M.execute(next,'reception',cmd(DEMO_PIN,'305718','PIN','own-credential:reception:n1:1')),next,'same key = already recorded');
    // The old PIN no longer opens it; the new one does; untouched users keep the starting PIN.
    assert.equal(refused(next,'reception',cmd(DEMO_PIN,'920461','PIN')).message,'The current password or PIN is wrong.');
    assert.equal(refused(next,'cashier',cmd(DEMO_PIN,'920461','PIN')),null);
    assert.deepEqual([next.bookings,next.users,next.grants],[s.bookings,s.users,s.grants],'nothing else changes');
  });

  test('[OWN-CRED] PIN history: none of the last 3 PINs (the current included), as PinHistory on the server',()=>{
    let s=M.seed();const pins=['305718','920461','581734'];let cur=DEMO_PIN;
    for(const p of pins){s=M.execute(s,'housekeeper',cmd(cur,p,'PIN'));cur=p;}
    // history now: 581734, 920461, 305718 → those three are refused; the starting PIN (4th back) is allowed again.
    for(const p of ['920461','305718'])assert.equal(refused(s,'housekeeper',cmd(cur,p,'PIN')).message,'Choose a PIN you have not used for your last 3 PINs.',p);
    s=M.execute(s,'housekeeper',cmd(cur,DEMO_PIN,'PIN'));
    assert.equal(s.credentialsX.housekeeper.history.length,3);
  });

  test('[OWN-CRED] password user (Owner, Tech admin): 12–128 characters, not the sign-in name, ≥ 4 different characters, not the current one; no password history',()=>{
    const s=M.seed();
    assert.equal(refused(s,'owner',cmd('wrong-password-1','Sand-Maroon-Ledger-9','PASSWORD')).message,'The current password or PIN is wrong.');
    assert.equal(refused(s,'owner',cmd(DEMO_PASSWORD,'short-pass','PASSWORD')).message,'The password must be 12–128 characters.');
    assert.equal(refused(s,'owner',cmd(DEMO_PASSWORD,'the-owner-password','PASSWORD')).message,'The password must not contain the sign-in name.');
    assert.equal(refused(s,'owner',cmd(DEMO_PASSWORD,'abababababab','PASSWORD')).message,'The password is too simple.');
    assert.equal(refused(s,'owner',cmd(DEMO_PASSWORD,DEMO_PASSWORD,'PASSWORD')).message,'Choose a new password: it must be different from the current one.');
    assert.equal(refused(s,'owner',cmd(DEMO_PASSWORD,'305718','PIN')).message,'Your account does not use a PIN.');
    const a=M.execute(s,'techadmin',cmd(DEMO_PASSWORD,'Sand-Maroon-Ledger-9','PASSWORD'));
    const b=M.execute(a,'techadmin',cmd('Sand-Maroon-Ledger-9',DEMO_PASSWORD,'PASSWORD'));
    assert.deepEqual([a.credentialsX.techadmin.history,b.credentialsX.techadmin.kind],[[],'PASSWORD']);
    assert.ok(!JSON.stringify(b).includes('Sand-Maroon-Ledger-9'));
  });

  test('[OWN-CRED] every signed-in role may change its own; a disabled or expired user may not',()=>{
    for(const u of M.seed().users){const pw=M.kindOfRole(u.role)==='PASSWORD';
      assert.ok(M.execute(M.seed(),u.id,cmd(pw?DEMO_PASSWORD:DEMO_PIN,pw?'Sand-Maroon-Ledger-9':'305718',pw?'PASSWORD':'PIN')).credentialsX[u.id],u.id);}
    const off=M.execute(M.seed(),'owner',{type:'USER',id:'reception'});
    assert.match(refused(off,'reception',cmd(DEMO_PIN,'305718','PIN')).message,/disabled/);
  });

  test('[OWN-CRED] the client check gives the same refusals before anything is sent (shared rules, one more: the two new entries must match)',()=>{
    assert.equal(M.ownCredentialProblem('PIN','','305718','305718'),'Type your current PIN.');
    assert.equal(M.ownCredentialProblem('PASSWORD','','x','x'),'Type your current password.');
    assert.equal(M.ownCredentialProblem('PIN',DEMO_PIN,'123456','123456'),'This PIN is too easy to guess. Choose one without runs or repeats.');
    assert.equal(M.ownCredentialProblem('PIN',DEMO_PIN,'305718','305719'),'The two PINs do not match.');
    assert.equal(M.ownCredentialProblem('PIN',DEMO_PIN,DEMO_PIN,DEMO_PIN),'Choose a new PIN: it must be different from the current one.');
    assert.equal(M.ownCredentialProblem('PASSWORD','x','the-owner-password','the-owner-password','owner'),'The password must not contain the sign-in name.');
    assert.equal(M.ownCredentialProblem('PIN',DEMO_PIN,'305718','305718'),undefined);
    assert.deepEqual(M.ownCredentialCommand('PIN','1','2','k'),{type:'OWN_CREDENTIAL_CHANGE',current:'1',newPin:'2',key:'k'});
    assert.deepEqual(M.ownCredentialCommand('PASSWORD','1','2','k'),{type:'OWN_CREDENTIAL_CHANGE',current:'1',newPassword:'2',key:'k'});
    assert.equal(M.ownCredentialKey('reception','abc123',2),'own-credential:reception:abc123:2');
    assert.match(M.mustChangeText('PIN'),/^Change your PIN first\./);assert.match(M.mustChangeText('PASSWORD'),/^Change your password first\./);
  });

  // ── Screens (demo build) ──
  await writeFile('tests/.generated/own-cred-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
import {OwnCredentialSheet} from '../../src/app/OwnCredential';
import {UserMenuSheet} from '../../src/app/mobile/sheets';
import {NavDrawer} from '../../src/app/NavDrawer';
const C:any={OwnCredentialSheet,UserMenuSheet,NavDrawer};
export function render(name:string,s:any,uid:string,mobile=false){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);
  const ctx:any={s,uid,user,m:metrics(s),page:'home',go:noop,modal:{type:'own-credential',key:'k1'},open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop,switchUser:noop,signOut:noop,openSearch:noop,taskList:[]};
  return renderToStaticMarkup(<HccContext.Provider value={ctx}>{React.createElement(C[name],{onClose:noop})}</HccContext.Provider>);
}`);
  await build({entryPoints:['tests/.generated/own-cred-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/own-cred.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"demo"'},loader:{'.css':'empty'},
    banner:{js:"import {createRequire as __ocReq} from 'node:module';const require=__ocReq(import.meta.url);"}});
  const {render}=await import('./.generated/own-cred.mjs');
  const text=html=>html.replace(/<[^>]+>/g,' ').replace(/&#x27;|&#39;/g,"'").replace(/&amp;/g,'&').replace(/\s+/g,' ');
  test('[OWN-CRED] sheet (desktop dialog + phone sheet): current / new / again, masked and empty, rules, demo starting secret; PIN fields numeric',()=>{
    const s=M.seed();
    for(const mobile of [false,true]){
      const h=render('OwnCredentialSheet',s,'reception',mobile),t=text(h);
      assert.match(t,/Change my PIN/);assert.match(t,/Current PIN/);assert.match(t,/New PIN/);assert.match(t,/Type the new PIN again/);
      assert.match(t,/6 digits, no runs or repeats/);assert.match(t,/starts with PIN 246810/);
      assert.equal((h.match(/type="password"/g)||[]).length,3);assert.equal((h.match(/inputMode="numeric"|inputmode="numeric"/g)||[]).length,3);
      assert.doesNotMatch(h,/ value="/);assert.match(h,/<button type="submit" class="btn primary[^"]*"[^>]*>.*Change PIN<\/button>/);
      assert.equal(/m-sheet-form/.test(h),mobile,'phone layout only on the phone');
    }
    const pw=text(render('OwnCredentialSheet',s,'owner'));
    assert.match(pw,/Change my password/);assert.match(pw,/12–128 characters/);assert.match(pw,/password Demo-Hotel-2026/);
  });
  test('[OWN-CRED] reachable for every user: profile menu (phone sheet / desktop dialog) and the menu drawer',()=>{
    const s=M.seed();
    assert.match(text(render('UserMenuSheet',s,'housekeeper',true)),/Change my PIN/);
    assert.match(text(render('UserMenuSheet',s,'accountant')),/Change my password/);
    assert.match(text(render('NavDrawer',s,'maint')),/Change my PIN/);
    assert.match(text(render('NavDrawer',s,'owner')),/Change my password/);
  });
}
