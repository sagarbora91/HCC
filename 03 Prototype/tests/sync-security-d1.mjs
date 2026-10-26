// Wave D item D1 — security hardening Lows. Prefix [D1].
// A06-08 (OWASP CSV injection): every CSV the app writes keeps formula text as text and numbers as numbers; a CR inside a cell is quoted.
// A06-16 / A10-08 (APK12-2): the sign-in screen shows the server's throttle text with the wait time — also through the APK's native
// HTTP adapter (CapacitorHttp: headers as a plain object, JSON body already parsed), which the v12 test (sync-v12fix.mjs) does not cover.
import {readFileSync} from 'node:fs';
import {build} from 'esbuild';
import {loadSync,keyStoreFor} from './_sync-harness.mjs';

export async function register({test,assert,M}){
  // ── A06-08: shared cell helpers (src/domain/csv.ts; server twin Hcc.Domain.Text.CsvFormula) ──
  await build({entryPoints:['src/domain/csv.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/d1-csv.mjs',logLevel:'error'});
  const {csvSafe,csvMinimal,csvQuoted}=await import(new URL('./.generated/d1-csv.mjs',import.meta.url).href);
  test('[D1 A06-08] csvSafe: = + - @ TAB CR text gets an apostrophe; plain numbers (-500, -1,250.50, +15) keep their sign; a CR is always quoted',()=>{
    for(const t of ['=1+2','+cmd',"-2+3",'@SUM(1)','\t=1','\r=1'])assert.equal(csvSafe(t),"'"+t,t);
    for(const t of ['-500','-1,250.50','+15','Rahul',"'=already"])assert.equal(csvSafe(t),t,t);
    assert.equal(csvSafe(-500),'-500');
    assert.equal(csvMinimal('a\rb'),'"a\rb"');assert.equal(csvMinimal('-500.00'),'-500.00');assert.equal(csvQuoted('=x'),`"'=x"`);
  });

  // ── A06-08: plans & extras report (was the one CSV without the guard) ──
  const {seed,execute,catReportCsv}=M;
  let s=seed();
  s=execute(s,'reception',{type:'BOOK',guest:'CSV Guard (fictional)',room:'105',arrival:s.date,departure:'2026-10-26',people:2});
  const id=s.bookings.at(-1).id;
  s=execute(s,'reception',{type:'REGISTER',id});s=execute(s,'reception',{type:'CHECKIN',id});s=execute(s,'owner',{type:'POST_NIGHTS',id});
  s=execute(s,'reception',{type:'CHARGE',id,label:'=HYPERLINK("http://x.test","y")',amount:70000});
  s=execute(s,'reception',{type:'CHARGE',id,label:'@SUM(1)',amount:5000});
  s=execute(s,'reception',{type:'CHARGE',id,label:'-2+3 towels',amount:2500});
  const cat=catReportCsv(s,s.date,'2026-10-31');
  test('[D1 A06-08] plans & extras CSV: typed charge labels that Excel would run stay text; amounts stay plain numbers',()=>{
    assert.ok(cat.includes(`Free-text charges,"'=HYPERLINK(""http://x.test"",""y"")",1,700.00`),cat);
    assert.ok(cat.includes(`Free-text charges,'@SUM(1),1,50.00`),cat);
    assert.ok(cat.includes(`Free-text charges,'-2+3 towels,1,25.00`),cat);
    assert.doesNotMatch(cat,/(^|\n|,)"?[=+@]/);
  });

  // ── A06-08: prototype payroll files (server twin PayrollFiles.Cell) — a CR inside a name cannot start a row of its own ──
  const t=structuredClone(seed());const run=t.staffX.runs[0];
  run.lines[0].name='Asha\r=cmd|\' /C calc\'!A0';run.lines[1].name='=HYPERLINK("x")';run.lines[1].net=-50000;
  const reg=M.hrRegisterCsv(t,run),bank=M.hrBankCsv(t,run);
  test('[D1 A06-08] payroll register and bank file: a CR inside a name is quoted, formula names get an apostrophe, a negative net stays -500.00',()=>{
    for(const csv of [reg,bank]){
      assert.ok(csv.includes(`"Asha\r=cmd|' /C calc'!A0"`),'CR quoted');
      assert.ok(csv.includes(`"'=HYPERLINK(""x"")"`),'formula neutralised');
      assert.doesNotMatch(csv,/(^|\n|,)"?=/);   // "Asha\r=cmd…" sits inside its quotes: no cell starts with =
    }
    assert.match(bank.split('\n').find(l=>l.includes('HYPERLINK')),/,-500\.00$/);
  });

  // ── A06-16 / A10-08: throttle text through the APK transport ──
  const S=await loadSync();
  const health={status:'ok',database:'ok',schemaVersion:2,serverTimeUtc:'2026-10-24T04:30:00.000Z'};
  const throttle={code:'FORBIDDEN',message:'Too many sign-in attempts from this device. Wait a few minutes and try again.',details:{reason:'TOO_MANY_ATTEMPTS'}};
  const bad={code:'FORBIDDEN',message:'Sign-in failed. Check the name and the password or PIN.',details:{reason:'BAD_CREDENTIALS'}};
  /** CapacitorHttp as the APK sees it: sign-in answers `signIn`, health ok, everything else 401 (signed out). */
  const capacitor=signIn=>{const seen=[];return {seen,async request(o){const p=new URL(o.url,'https://10.0.2.2:5443').pathname.replace(/^\/api\/v1/,'');seen.push(o.method+' '+p);
    if(o.method==='POST'&&p==='/auth/sign-in')return signIn(JSON.parse(o.data));
    if(p==='/system/health')return {status:200,data:health,headers:{'content-type':'application/json'}};
    return {status:401,data:{code:'UNAUTHENTICATED',message:'Sign in.'},headers:{}};}};};
  const apkRuntime=async http=>{const c=S.createMemoryCache();
    const rt=S.createSyncRuntime({areas:S.AREAS,fetch:S.capacitorFetch(http),cache:c,keyStore:keyStoreFor(S,c),cacheIterations:1000,
      auth:S.tokenAuth({store:S.memoryTokenStore()}),commandRetries:0});await rt.boot();return rt;};
  const lower=capacitor(()=>({status:429,data:throttle,headers:{'retry-after':'840','content-type':'application/json'}}));
  const rLower=await (await apkRuntime(lower)).signIn({loginName:'rec.anita',pin:'472913'});
  const titled=capacitor(()=>({status:429,data:JSON.stringify(throttle),headers:{'Retry-After':'60'}}));
  const titledRt=await apkRuntime(titled);const rTitled=await titledRt.signIn({loginName:'rec.anita',pin:'472913'});
  let sent;const wrong=capacitor(b=>{sent=b;return {status:403,data:bad,headers:{}};});
  const rWrong=await (await apkRuntime(wrong)).signIn({loginName:'rec.anita',pin:'000000'});
  test('[D1 A06-16] APK transport: a 429 shows the throttle text with the wait in minutes (lower- or title-case Retry-After, parsed or raw JSON body)',()=>{
    assert.deepEqual(rLower,{ok:false,text:'Too many sign-in attempts from this device. Wait 14 minutes, then try again.'});
    assert.deepEqual(rTitled,{ok:false,text:'Too many sign-in attempts from this device. Wait 1 minute, then try again.'});
    assert.equal(titledRt.get().signInMessage,rTitled.text);assert.equal(titledRt.get().status,'signed-out');
    assert.ok(lower.seen.includes('POST /auth/sign-in'));
  });
  test('[D1 A06-16] APK transport: a wrong PIN keeps the server\'s own bad-credentials sentence, without a "Not permitted:" prefix; the token is asked for',()=>{
    assert.deepEqual(rWrong,{ok:false,text:'Sign-in failed. Check the name and the password or PIN.'});
    assert.equal(sent.returnToken,true);assert.equal(sent.pin,'000000');
  });
  test('[D1 A06-16] the sign-in screen shows the runtime\'s text (no generic fallback when the server sent one)',()=>{
    const react=readFileSync(new URL('../src/clients/sync/react.tsx',import.meta.url),'utf8');
    assert.match(react,/const r=await rt\.signIn\(/);assert.match(react,/if\(!r\.ok\)\{setErr\(r\.text\?\?'Sign-in failed\.'\);return;\}/);
    assert.match(react,/\{shown&&<p className="form-error" role="alert">\{shown\}<\/p>\}/);
    assert.match(react,/<ServerSignIn message=\{snap\.signInMessage\?\?note\}\/>/);
  });
}
