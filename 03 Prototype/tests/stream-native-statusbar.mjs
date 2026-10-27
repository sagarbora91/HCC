// V14-2: styleStatusBar() on Android 15+ also calls the APK's StatusBarColor plugin (the StatusBar plugin's colour is ignored under edge-to-edge).
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['src/app/native.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/native-statusbar.mjs',logLevel:'error',external:['react']});
const N=await import('./.generated/native-statusbar.mjs');
const JAVA='../04 Android/android/app/src/main/java/in/hcc/prototype/';
const read=f=>{try{return readFileSync(JAVA+f,'utf8');}catch{return '';}};
const flush=()=>new Promise(r=>setTimeout(r,0));

function fake({withColor=true,failColor=false}={}){
  const calls=[];
  const plugin=name=>new Proxy({},{get:(_,m)=>m==='then'?undefined:async a=>{calls.push([name,m,a]);if(name==='StatusBarColor'&&failColor)throw new Error('boom');return {applied:true};}});
  globalThis.Capacitor={isNativePlatform:()=>true,registerPlugin:plugin,isPluginAvailable:n=>n!=='StatusBarColor'||withColor};
  N.resetNativeCache();return calls;
}
const clear=()=>{delete globalThis.Capacitor;delete globalThis.document;delete globalThis.getComputedStyle;N.resetNativeCache();};

const R={};
{
  N.resetNativeCache();R.browser=(()=>{try{N.styleStatusBar('dark');return 'ok';}catch(e){return String(e);}})();
  let calls=fake();
  for(const t of ['light','dark','hc'])N.styleStatusBar(t);
  await flush();R.calls=calls;clear();
  // theme surface is read from the CSS variable --surface
  calls=fake();
  globalThis.document={documentElement:{getAttribute:()=>'dark'}};
  globalThis.getComputedStyle=()=>({getPropertyValue:k=>k==='--surface'?' #201916 ':''});
  N.styleStatusBar();await flush();R.withSurface=calls.filter(c=>c[0]==='StatusBarColor');clear();
  // old APK without the plugin: no StatusBarColor call at all
  calls=fake({withColor:false});N.styleStatusBar('dark');await flush();R.old=calls;clear();
  // plugin failure never throws or rejects unhandled
  calls=fake({failColor:true});R.fail=(()=>{try{N.styleStatusBar('hc');return 'ok';}catch(e){return String(e);}})();await flush();clear();
  // V15-1: rotation / resize paints the colours again, once per burst, through the real timer; unregister removes the listeners
  const fakeWin=()=>{const ls={};return {ls,addEventListener:(t,fn)=>{(ls[t]??=[]).push(fn);},removeEventListener:(t,fn)=>{ls[t]=(ls[t]??[]).filter(f=>f!==fn);},setTimeout,clearTimeout,fire:t=>{for(const f of ls[t]??[])f();}};};
  calls=fake();globalThis.document={documentElement:{getAttribute:()=>'dark'}};
  let win=fakeWin();const off=N.listenStatusBarRestyle(5,win);
  R.restyleListeners=Object.keys(win.ls).sort();
  win.fire('resize');win.fire('resize');win.fire('orientationchange');
  await new Promise(r=>setTimeout(r,30));
  R.restyleCalls=calls.filter(c=>c[0]==='StatusBarColor');
  off();R.restyleAfterOff=Object.values(win.ls).flat().length;win.fire('resize');await new Promise(r=>setTimeout(r,30));R.restyleCallsAfterOff=calls.filter(c=>c[0]==='StatusBarColor').length;
  clear();
  N.resetNativeCache();win=fakeWin();N.listenStatusBarRestyle(5,win)();R.restyleBrowser=Object.values(win.ls).flat().length;
}
export function register({test,assert}){
  test('[STATUSBAR] outside the APK styleStatusBar is a no-op',()=>{assert.equal(R.browser,'ok');});
  test('[STATUSBAR] inside the APK the StatusBar plugin keeps its three calls and StatusBarColor gets the theme fill (light #7a1f2b, dark #ae4155, hc #5e1620)',()=>{
    const bg=R.calls.filter(c=>c[0]==='StatusBar'&&c[1]==='setBackgroundColor').map(c=>c[2].color);
    assert.deepEqual(bg,['#7a1f2b','#ae4155','#5e1620']);
    assert.ok(R.calls.filter(c=>c[1]==='setStyle').every(c=>c[2].style==='DARK'));
    const sc=R.calls.filter(c=>c[0]==='StatusBarColor');
    assert.deepEqual(sc.map(c=>c[1]),['setColors','setColors','setColors']);
    assert.deepEqual(sc.map(c=>c[2].top),['#7a1f2b','#ae4155','#5e1620']);
    assert.ok(sc.every(c=>c[2].bottom===undefined),'no bottom colour when the surface cannot be read');
  });
  test('[STATUSBAR] the strip behind the gesture bar uses the theme --surface colour',()=>{
    assert.deepEqual(R.withSurface,[['StatusBarColor','setColors',{top:'#ae4155',bottom:'#201916'}]]);
  });
  test('[STATUSBAR] an APK without StatusBarColor skips that call; a failing plugin never throws',()=>{
    assert.equal(R.old.filter(c=>c[0]==='StatusBarColor').length,0);assert.ok(R.old.some(c=>c[1]==='setBackgroundColor'));
    assert.equal(R.fail,'ok');
  });
  test('[V15-1] inside the APK a resize / orientation burst repaints the theme colours once after the delay; unregister stops it; a browser gets no listener',()=>{
    assert.deepEqual(R.restyleListeners,['orientationchange','resize']);
    assert.deepEqual(R.restyleCalls.map(c=>[c[1],c[2].top]),[['setColors','#ae4155']],'three events inside the delay = one repaint, in the theme on screen');
    assert.equal(R.restyleAfterOff,0);assert.equal(R.restyleCallsAfterOff,1);
    assert.equal(R.restyleBrowser,0);
    assert.equal(N.STATUS_BAR_RESTYLE_MS,150);
    const app=readFileSync('src/app/App.tsx','utf8');
    assert.ok(app.includes('const offRestyle=listenStatusBarRestyle();'),'App registers the repaint with the back-button listener');
  });
  test('[STATUSBAR] Java plugin: name, setColors, API 35 gate, draws the decor top padding, registered before super.onCreate',()=>{
    const j=read('StatusBarColorPlugin.java');assert.ok(j.length>0);
    assert.match(j,/@CapacitorPlugin\(name\s*=\s*"StatusBarColor"\)/);
    assert.match(j,/@PluginMethod\s+public void setColors/);
    assert.match(j,/VANILLA_ICE_CREAM/);assert.match(j,/setBackgroundDrawable\(/);assert.match(j,/getPaddingTop\(\)/);
    assert.match(j,/setAppearanceLightStatusBars/);assert.match(j,/setAppearanceLightNavigationBars/);
    const m=read('MainActivity.java');const reg=m.indexOf('registerPlugin(StatusBarColorPlugin.class)'),sup=m.indexOf('super.onCreate(savedInstanceState)');
    assert.ok(reg>=0&&sup>=0&&reg<sup);
  });
  test('[STATUSBAR] the activity theme is not changed (no edge-to-edge opt-out: it is ignored at targetSdk 36), so Android 13/14 layout is untouched',()=>{
    const s=readFileSync('../04 Android/android/app/src/main/res/values/styles.xml','utf8');
    assert.doesNotMatch(s,/windowOptOutEdgeToEdgeEnforcement/);
  });
}
