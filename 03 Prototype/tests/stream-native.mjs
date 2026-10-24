// Phase D1 tests: Android back-button decision, file names, CSV/print saving through a mocked Capacitor bridge.
import {build} from 'esbuild';
await build({entryPoints:['src/app/native.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/native.mjs',logLevel:'error',external:['react']});
const N=await import('./.generated/native.mjs');

function fakeBridge({fail=false,failPrinter=false}={}){
  const calls=[];
  const plugin=name=>new Proxy({},{get:(_,m)=>m==='then'?undefined:async a=>{calls.push([name,m,a]);if(fail&&name==='Share')throw new Error('cancelled');if(failPrinter&&name==='Printer')throw new Error('no print service');
    if(m==='getUri')return {uri:'file:///storage/emulated/0/Documents/'+a.path};if(m==='addListener')return {remove(){calls.push([name,'remove']);}};return {};}});
  globalThis.Capacitor={isNativePlatform:()=>true,registerPlugin:plugin};
  N.resetNativeCache();return calls;
}
const clear=()=>{delete globalThis.Capacitor;N.resetNativeCache();};

const R={};
{ // async scenarios run once, here; the tests below assert on the results synchronously
  let calls=fakeBridge();R.csv=await N.saveCsv('HCC-Demo-test.csv','a,b\r\n1,2');R.csvCalls=calls;clear();
  fakeBridge({fail:true});R.failed=await N.saveCsv('x.csv','a');clear();
  calls=fakeBridge();await N.saveTextFile('r.html','Total ₹1,23,456 · José','text/html');R.utf8=calls;clear();
  calls=fakeBridge();R.print=await N.printPage('Invoice 1','<html>x</html>');R.printCalls=calls;clear();
  calls=fakeBridge({failPrinter:true});R.printFallback=await N.printPage('Invoice 2','<html>y</html>');R.printFallbackCalls=calls;clear();
  calls=fakeBridge();const off=N.listenBackButton(()=>{});await new Promise(r=>setTimeout(r,5));R.listen=[...calls];off();await new Promise(r=>setTimeout(r,5));R.listenAfter=[...calls];clear();
}
export function register({test,assert}){
  test('[NATIVE] back button: sheet → wizard step → previous page → landing → confirm exit',()=>{
    const d=N.decideBack;
    assert.equal(d({sheetOpen:true,stepBack:true,depth:3,atLanding:false}),'close-sheet');
    assert.equal(d({sheetOpen:false,stepBack:true,depth:3,atLanding:false}),'step-back');
    assert.equal(d({sheetOpen:false,stepBack:false,depth:2,atLanding:false}),'history-back');
    assert.equal(d({sheetOpen:false,stepBack:false,depth:0,atLanding:false}),'go-landing');
    assert.equal(d({sheetOpen:false,stepBack:false,depth:0,atLanding:true}),'confirm-exit');
  });
  test('[NATIVE] Android app: phone-sized screens get the phone layout, tablets the desktop layout',()=>{
    assert.equal(N.nativePhoneLayout(true,360),true);assert.equal(N.nativePhoneLayout(true,412),true);assert.equal(N.nativePhoneLayout(true,673),false);assert.equal(N.nativePhoneLayout(true,800),false);
    assert.equal(N.nativePhoneLayout(false,360),false);assert.equal(N.nativePhoneLayout(false,1000),false);
  });
  test('[NATIVE] step-back registry: latest sheet wins and unregisters cleanly',()=>{
    const log=[];const off1=N.pushStepBack(()=>log.push('a')),off2=N.pushStepBack(()=>log.push('b'));
    assert.equal(N.hasStepBack(),true);N.runStepBack();off2();N.runStepBack();off1();
    assert.deepEqual(log,['b','a']);assert.equal(N.hasStepBack(),false);
  });
  test('[NATIVE] file names are safe, prefixed HCC- and dated',()=>{
    assert.equal(N.nativeFileName('HCC-Demo-daily-2026-10-01_2026-10-23.csv','csv'),'HCC-Demo-daily-2026-10-01_2026-10-23.csv');
    assert.equal(N.nativeFileName('Invoice / Room 105: "Ananya"','html','2026-10-24'),'HCC-Invoice-Room-105-Ananya-20261024.html');
    assert.equal(N.nativeFileName('   ','html'),'HCC-document.html');
    assert.ok(N.nativeFileName('x'.repeat(200),'csv').length<80);
  });
  test('[NATIVE] outside the APK nothing is native (browser fallbacks stay in charge)',()=>{
    clear();assert.equal(N.isNative(),false);assert.equal(N.nativePlugin('Share'),undefined);
    N.styleStatusBar();N.exitApp();assert.equal(typeof N.listenBackButton(()=>{}),'function');
  });
  test('[NATIVE] CSV in the APK is written to Documents/HCC with a BOM and offered through the share sheet',()=>{
    assert.equal(R.csv,'shared');
    const w=R.csvCalls.find(c=>c[1]==='writeFile')[2];
    assert.equal(w.path,'HCC/HCC-Demo-test.csv');assert.equal(w.directory,'DOCUMENTS');assert.equal(w.recursive,true);
    assert.equal(Buffer.from(w.data,'base64').toString('utf8'),'﻿a,b\r\n1,2');
    const sh=R.csvCalls.find(c=>c[1]==='share')[2];assert.match(sh.url,/Documents\/HCC\/HCC-Demo-test\.csv$/);
  });
  test('[NATIVE] a cancelled share sheet or a failing plugin reports "failed", never throws',()=>{assert.equal(R.failed,'failed');});
  test('[NATIVE] non-ASCII text (₹, names) survives the base64 round trip',()=>{
    assert.equal(Buffer.from(R.utf8.find(c=>c[1]==='writeFile')[2].data,'base64').toString('utf8'),'Total ₹1,23,456 · José');
  });
  test('[NATIVE] printing uses the Android print service; if it fails the page is shared as an HTML file',()=>{
    assert.equal(R.print,'printed');const p=R.printCalls.find(c=>c[0]==='Printer'&&c[1]==='printHtml')[2];assert.equal(p.name,'Invoice 1');assert.equal(p.html,'<html>x</html>');
    assert.ok(!R.printCalls.some(c=>c[0]==='Share'));
    assert.equal(R.printFallback,'shared');assert.ok(R.printFallbackCalls.some(c=>c[0]==='Filesystem'&&c[1]==='writeFile'&&c[2].path==='HCC/HCC-Invoice-2.html'));assert.ok(R.printFallbackCalls.some(c=>c[0]==='Share'&&c[1]==='share'));
  });
  test('[NATIVE] back-button listener registers on the App plugin and removes itself',()=>{
    assert.ok(R.listen.some(c=>c[0]==='App'&&c[1]==='addListener'&&c[2]==='backButton'));
    assert.ok(R.listenAfter.some(c=>c[0]==='App'&&c[1]==='remove'));
  });
  test('[NATIVE] printable page is standalone, escapes the title and hides action buttons',()=>{
    const h=N.printablePage('A <b> & C','<div class="invoice">x</div>','.invoice{color:#000}');
    assert.match(h,/^<!doctype html>/);assert.match(h,/<title>A &lt;b> &amp; C<\/title>/);assert.match(h,/\.no-print,\.modal-actions\{display:none!important\}/);assert.match(h,/<div class="invoice">x<\/div>/);
  });
}
