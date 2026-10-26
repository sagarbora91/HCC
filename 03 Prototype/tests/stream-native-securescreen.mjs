// Audit A07-04: FLAG_SECURE while the production "Show ID" dialog shows a full guest ID number. native.ts wrappers (no-ops outside the APK,
// call shape inside a fake Capacitor, hold counting), the Java plugin and its registration, and the reveal dialog wiring.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['src/app/native.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/native-securescreen.mjs',logLevel:'error',external:['react']});
const N=await import('./.generated/native-securescreen.mjs');
const JAVA='../04 Android/android/app/src/main/java/in/hcc/prototype/';
const read=f=>{try{return readFileSync(JAVA+f,'utf8');}catch{return '';}};
const tick=()=>new Promise(r=>setTimeout(r,0));

function fakeBridge({plugins=['SecureScreen'],fail=false,syncThrow=false}={}){
  const calls=[];
  const plugin=name=>new Proxy({},{get:(_,m)=>m==='then'?undefined:(a)=>{calls.push([name,m,a]);
    if(syncThrow)throw new Error('bridge gone');
    return fail?Promise.reject(new Error('"SecureScreen" plugin is not implemented on android')):Promise.resolve({secure:!!a?.secure,pending:false});}});
  globalThis.Capacitor={isNativePlatform:()=>true,registerPlugin:plugin,isPluginAvailable:n=>plugins.includes(n)};
  N.resetNativeCache();return calls;
}
const clear=()=>{delete globalThis.Capacitor;N.resetNativeCache();};
const sets=calls=>calls.filter(c=>c[1]==='setSecure').map(c=>c[2].secure);

const R={};
{
  // Browser: nothing native, holds still counted and released.
  R.browserPlugin=N.secureScreenPlugin();const rb=N.holdSecureScreen();R.browserHolds=N.secureScreenHolds();rb();R.browserAfter=N.secureScreenHolds();
  // APK with the plugin: first hold sets, nested holds do not repeat, last release clears, a second release is a no-op.
  let calls=fakeBridge();R.plugin=N.secureScreenPlugin();
  const a=N.holdSecureScreen();R.afterA=sets(calls);const b=N.holdSecureScreen();R.afterB=sets(calls);R.holds2=N.secureScreenHolds();
  a();R.afterReleaseA=sets(calls);a();R.afterDoubleA=sets(calls);b();R.afterReleaseB=sets(calls);R.holds0=N.secureScreenHolds();
  const c=N.holdSecureScreen();c();R.second=sets(calls);R.callShape=calls[0];clear();
  // An older APK without the plugin: no native call at all.
  calls=fakeBridge({plugins:[]});R.oldPlugin=N.secureScreenPlugin();const o=N.holdSecureScreen();o();R.oldCalls=calls;clear();
  // Plugin errors (rejected promise or a synchronous throw) never escape.
  R.unhandled=0;const onUnhandled=()=>{R.unhandled++;};process.on('unhandledRejection',onUnhandled);
  fakeBridge({fail:true});try{const f=N.holdSecureScreen();f();R.failOk=true;}catch{R.failOk=false;}await tick();await tick();clear();
  fakeBridge({syncThrow:true});try{const f=N.holdSecureScreen();f();R.throwOk=true;}catch{R.throwOk=false;}clear();
  process.off('unhandledRejection',onUnhandled);R.holdsEnd=N.secureScreenHolds();
}
export function register({test,assert}){
  test('[SECURE] outside the APK there is no plugin and no native call; holds still balance',()=>{
    assert.equal(R.browserPlugin,undefined);assert.equal(R.browserHolds,1);assert.equal(R.browserAfter,0);
  });
  test('[SECURE] inside the APK: the first hold sends setSecure({secure:true}), the last release sends {secure:false}',()=>{
    assert.ok(R.plugin);assert.deepEqual(R.callShape,['SecureScreen','setSecure',{secure:true}]);
    assert.deepEqual(R.afterA,[true]);assert.deepEqual(R.afterB,[true]);assert.equal(R.holds2,2);
    assert.deepEqual(R.afterReleaseA,[true],'one holder left: still secure');assert.deepEqual(R.afterDoubleA,[true],'a second release of the same hold is ignored');
    assert.deepEqual(R.afterReleaseB,[true,false]);assert.equal(R.holds0,0);assert.deepEqual(R.second,[true,false,true,false]);
  });
  test('[SECURE] an APK without the SecureScreen plugin (older build) makes no native call',()=>{
    assert.equal(R.oldPlugin,undefined);assert.deepEqual(R.oldCalls,[]);
  });
  test('[SECURE] plugin errors never throw or leave an unhandled rejection; the count returns to zero',()=>{
    assert.equal(R.failOk,true);assert.equal(R.throwOk,true);assert.equal(R.unhandled,0);assert.equal(R.holdsEnd,0);
  });
  test('[SECURE] Java plugin: name, setSecure, FLAG_SECURE set at once, cleared only in the foreground and only if it set it',()=>{
    const j=read('SecureScreenPlugin.java');
    assert.ok(j.length>0,'SecureScreenPlugin.java exists');
    assert.match(j,/@CapacitorPlugin\(name\s*=\s*"SecureScreen"\)/);
    assert.match(j,/@PluginMethod\s+public void setSecure/);
    assert.match(j,/addFlags\(WindowManager\.LayoutParams\.FLAG_SECURE\)/);assert.match(j,/clearFlags\(WindowManager\.LayoutParams\.FLAG_SECURE\)/);
    assert.match(j,/!wanted && applied && resumed/);assert.match(j,/protected void handleOnResume\(\)/);assert.match(j,/protected void handleOnPause\(\)/);
    assert.match(j,/executeOnMainThread/);
  });
  test('[SECURE] MainActivity registers the plugin before super.onCreate',()=>{
    const m=read('MainActivity.java');
    const reg=m.indexOf('registerPlugin(SecureScreenPlugin.class)'),sup=m.indexOf('super.onCreate(savedInstanceState)');
    assert.ok(reg>=0&&sup>=0&&reg<sup);
  });
  test('[SECURE] the Show ID dialog holds FLAG_SECURE from the request until it closes',()=>{
    const ui=readFileSync('src/clients/sync/guestid-ui.tsx','utf8');
    assert.match(ui,/import \{useSecureScreen\} from '\.\.\/\.\.\/app\/native';/);
    assert.match(ui,/useSecureScreen\(loading\|\|shown\);/);
    // the hook runs on every render (never behind a condition or after an early return)
    assert.ok(ui.indexOf('useSecureScreen(loading||shown)')<ui.indexOf('return <>'));
  });
  test('[SECURE] the inline Show ID of the demo build holds FLAG_SECURE too while the number is shown (same behaviour as production)',()=>{
    const demo=readFileSync('src/features/guests/GuestId.tsx','utf8');
    const fn=demo.slice(demo.indexOf('function GuestIdValueDemo'));
    assert.match(fn,/useSecureScreen\(shown&&!v\.masked\);/);
    assert.ok(fn.indexOf('useSecureScreen(')<fn.indexOf('if(v.masked)return'),'hook before the early return');
  });
}
