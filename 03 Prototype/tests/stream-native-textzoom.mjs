// CP-4: native.ts wrappers for the Android TextZoom plugin: no-ops outside the APK, call shape inside a fake Capacitor, Java source wiring.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['src/app/native.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/native-textzoom.mjs',logLevel:'error',external:['react']});
const N=await import('./.generated/native-textzoom.mjs');
const JAVA='../04 Android/android/app/src/main/java/in/hcc/prototype/';
const read=f=>{try{return readFileSync(JAVA+f,'utf8');}catch{return '';}};

function fakeBridge({plugins=['TextZoom'],scale=1.3,failSet=false,failGet=false,bad=undefined}={}){
  const calls=[];
  const plugin=name=>new Proxy({},{get:(_,m)=>m==='then'?undefined:async a=>{calls.push([name,m,a]);
    if(m==='setHonourSystem'){if(failSet)throw new Error('"TextZoom" plugin is not implemented on android');return {honour:a.honour,zoom:a.honour?Math.round(scale*100):100,systemScale:scale};}
    if(m==='getTextZoom'){if(failGet)throw new Error('boom');return {zoom:Math.round(scale*100),systemScale:bad===undefined?scale:bad,honour:true};}
    return {};}});
  globalThis.Capacitor={isNativePlatform:()=>true,registerPlugin:plugin,isPluginAvailable:n=>plugins.includes(n)};
  N.resetNativeCache();return calls;
}
const clear=()=>{delete globalThis.Capacitor;N.resetNativeCache();};

const R={};
{
  R.browserPlugin=N.textZoomPlugin();R.browserSet=await N.setHonourSystemFont(false);R.browserRead=await N.readSystemFontScale();
  let calls=fakeBridge();R.plugin=N.textZoomPlugin();R.setOff=await N.setHonourSystemFont(false);R.setOn=await N.setHonourSystemFont(true);R.scale=await N.readSystemFontScale();R.calls=calls;clear();
  calls=fakeBridge({plugins:[]});R.oldPlugin=N.textZoomPlugin();R.oldSet=await N.setHonourSystemFont(false);R.oldRead=await N.readSystemFontScale();R.oldCalls=calls;clear();
  fakeBridge({failSet:true,failGet:true});R.failSet=await N.setHonourSystemFont(false);R.failRead=await N.readSystemFontScale();clear();
  fakeBridge({bad:0});R.badZero=await N.readSystemFontScale();clear();
  fakeBridge({bad:'x'});R.badText=await N.readSystemFontScale();clear();
  fakeBridge({scale:1});R.normal=await N.readSystemFontScale();clear();
  const c=fakeBridge();R.p1=N.textZoomPlugin();R.p2=N.textZoomPlugin();clear();void c;
  fakeBridge({scale:0.85});R.small=await N.readSystemFontScale();clear();
}
export function register({test,assert}){
  test('[TEXTZOOM] outside the APK every wrapper is a no-op',()=>{
    assert.equal(R.browserPlugin,undefined);assert.equal(R.browserSet,false);assert.equal(R.browserRead,undefined);
  });
  test('[TEXTZOOM] inside the APK textZoomPlugin() returns the TextZoom proxy and is cached',()=>{
    assert.ok(R.plugin);assert.equal(R.p1,R.p2);
  });
  test('[TEXTZOOM] setHonourSystemFont sends {honour:boolean} to setHonourSystem',()=>{
    assert.equal(R.setOff,true);assert.equal(R.setOn,true);
    const sets=R.calls.filter(c=>c[1]==='setHonourSystem');
    assert.deepEqual(sets,[['TextZoom','setHonourSystem',{honour:false}],['TextZoom','setHonourSystem',{honour:true}]]);
  });
  test('[TEXTZOOM] readSystemFontScale calls getTextZoom and returns the multiplier',()=>{
    assert.equal(R.scale,1.3);assert.equal(R.normal,1);assert.equal(R.small,0.85);
    assert.ok(R.calls.some(c=>c[0]==='TextZoom'&&c[1]==='getTextZoom'));
  });
  test('[TEXTZOOM] an APK without the TextZoom plugin (older build) behaves like a browser, no native call',()=>{
    assert.equal(R.oldPlugin,undefined);assert.equal(R.oldSet,false);assert.equal(R.oldRead,undefined);assert.deepEqual(R.oldCalls,[]);
  });
  test('[TEXTZOOM] plugin errors never throw: set → false, read → undefined',()=>{
    assert.equal(R.failSet,false);assert.equal(R.failRead,undefined);
  });
  test('[TEXTZOOM] unusable systemScale values (0, text) read as undefined',()=>{
    assert.equal(R.badZero,undefined);assert.equal(R.badText,undefined);
  });
  test('[TEXTZOOM] Java plugin: name, both methods, stored flag default true, system scale restore',()=>{
    const j=read('TextZoomPlugin.java');
    assert.ok(j.length>0,'TextZoomPlugin.java exists');
    assert.match(j,/@CapacitorPlugin\(name\s*=\s*"TextZoom"\)/);
    assert.match(j,/@PluginMethod\s+public void getTextZoom/);
    assert.match(j,/@PluginMethod\s+public void setHonourSystem/);
    assert.match(j,/hcc_text_zoom_honour/);assert.match(j,/getBoolean\(KEY_HONOUR,\s*true\)/);
    assert.match(j,/setTextZoom\(/);assert.match(j,/fontScale/);assert.match(j,/: 100;/);
  });
  test('[TEXTZOOM] MainActivity registers the plugin before super.onCreate and applies the stored flag at startup',()=>{
    const m=read('MainActivity.java');
    const reg=m.indexOf('registerPlugin(TextZoomPlugin.class)'),sup=m.indexOf('super.onCreate');
    assert.ok(reg>=0&&sup>=0&&reg<sup);assert.match(m,/TextZoomPlugin\.apply\(/);
  });
}
