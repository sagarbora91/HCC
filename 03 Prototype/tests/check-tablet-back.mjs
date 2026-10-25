// Local Chromium/touch QA with a mocked native App bridge. No emulator or device is started.
import {createServer} from 'node:http';
import {readFile,mkdir,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {createRequire} from 'node:module';
import assert from 'node:assert/strict';
const {chromium}=createRequire(import.meta.url)(process.env.HCC_PLAYWRIGHT_PATH||'playwright');
const html=await readFile('dist/index.html');
const server=createServer((req,res)=>{res.setHeader('Content-Type','text/html');res.end(html);});
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const out=resolve('../05 Delivery Evidence/Acceptance/tablet-local');await mkdir(out,{recursive:true});
const browser=await chromium.launch({headless:true,executablePath:process.env.HCC_CHROME_PATH});
const results=[];
try{
  const context=await browser.newContext({viewport:{width:673,height:1006},hasTouch:true});
  await context.addInitScript(()=>{
    localStorage.setItem('hcc-session','owner');
    window.Capacitor={isNativePlatform:()=>true,registerPlugin:name=>({
      addListener:async(event,callback)=>{if(name==='App'&&event==='backButton')window.hccTestBack=callback;return {remove(){}};},
      setOverlaysWebView:async()=>{},setBackgroundColor:async()=>{},setStyle:async()=>{}
    })};
  });
  const page=await context.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
  await page.goto(`http://127.0.0.1:${server.address().port}`);
  if(await page.locator('.signin-user').first().isVisible())await page.locator('.signin-user').filter({hasText:'Sagar'}).click();
  await page.locator('.topbar').waitFor();
  for(const [name,width,height] of [['portrait',673,1006],['landscape',1078,601]]){
    await page.setViewportSize({width,height});
    const top=await page.locator('.topbar').boundingBox();assert.ok(top.height<=150,JSON.stringify(top));
    assert.equal(await page.locator('.demo-toolbar').isVisible(),false);
    const controls=await page.locator('.topbar button:visible').evaluateAll(xs=>xs.map(x=>({label:x.textContent||x.getAttribute('aria-label'),height:x.getBoundingClientRect().height})));
    assert.ok(controls.every(x=>x.height>=48),JSON.stringify(controls));
    await page.screenshot({path:resolve(out,name+'-home.png')});
    await page.getByRole('button',{name:'Open menu',exact:true}).click();
    const nav=await page.locator('.drawer-nav').boundingBox();assert.ok(nav.height>height*.55,JSON.stringify(nav));
    await page.locator('.drawer-cat-btn').filter({hasText:'Front Desk'}).click();
    await page.locator('.drawer-sub[data-page="reservations"]').scrollIntoViewIfNeeded();
    await page.screenshot({path:resolve(out,name+'-drawer.png')});
    await page.evaluate(()=>window.hccTestBack());assert.equal(await page.locator('.drawer').count(),0);
    results.push({name,headerHeight:top.height,navHeight:nav.height,controls});
  }
  const navigate=async id=>{
    await page.getByRole('button',{name:'Open menu',exact:true}).click();
    const target=page.locator(`.drawer-sub[data-page="${id}"]`);
    if(!await target.count())await page.locator('.drawer-cat-btn').filter({hasText:'Front Desk'}).click();
    await target.click();
  };
  await navigate('reservations');
  const pageControls=await page.locator('.content button:visible,.content select:visible,.content input:visible').evaluateAll(xs=>xs.map(x=>({tag:x.tagName,height:x.getBoundingClientRect().height})));
  assert.ok(pageControls.every(x=>x.height>=48),JSON.stringify(pageControls));
  results.push({reservationControls:pageControls});
  await page.screenshot({path:resolve(out,'landscape-reservations.png')});
  await navigate('calendar');
  await page.evaluate(()=>window.hccTestBack());await page.waitForTimeout(100);
  assert.equal(await page.evaluate(()=>history.state.page),'reservations');
  await page.evaluate(()=>window.hccTestBack());await page.waitForTimeout(100);
  assert.equal(await page.evaluate(()=>history.state.idx),0);
  await page.evaluate(()=>window.hccTestBack());await page.getByRole('alertdialog').waitFor();
  await page.getByRole('button',{name:'Stay',exact:true}).click();
  // Simulate the observed exhaustion: stale positive history depth but no popstate.
  await navigate('reservations');
  await page.evaluate(()=>{window.hccOriginalBack=history.back.bind(history);history.back=()=>{};window.hccTestBack();});
  await page.waitForTimeout(550);
  await page.getByRole('heading',{name:'Modules',exact:true}).waitFor();
  assert.equal(await page.evaluate(()=>history.state.idx),0);
  await page.evaluate(()=>window.hccTestBack());await page.getByRole('alertdialog').waitFor();
  await page.getByRole('button',{name:'Stay',exact:true}).click();
  // A new navigation must invalidate the pending fallback instead of being overwritten.
  await navigate('reservations');await page.evaluate(()=>window.hccTestBack());
  await navigate('calendar');await page.waitForTimeout(550);
  assert.equal(await page.evaluate(()=>history.state.page),'calendar');
  assert.equal(await page.getByRole('alertdialog').count(),0);
  await page.evaluate(()=>{history.back=window.hccOriginalBack;});
  assert.deepEqual(errors,[]);
  results.push({back:'menu closes; normal history unwinds; exhausted history recovers; new navigation cancels fallback',errors});
}finally{await browser.close();await new Promise(r=>server.close(r));}
await writeFile(resolve(out,'results.json'),JSON.stringify(results,null,2));console.log(JSON.stringify(results,null,2));
