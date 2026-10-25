// Actual local app geometry/click checks. Chromium is NOT an Android WebView 83 certification.
import {createServer} from 'node:http';
import {readFile,mkdir,writeFile} from 'node:fs/promises';
import {resolve} from 'node:path';
import {createRequire} from 'node:module';
import assert from 'node:assert/strict';
const {chromium}=createRequire(import.meta.url)(process.env.HCC_PLAYWRIGHT_PATH||'playwright');
const html=await readFile('dist/index.html','utf8');assert.ok(!/\binset\s*:/.test(html));
const server=createServer((req,res)=>{res.setHeader('Content-Type','text/html');res.end(html);});
await new Promise(r=>server.listen(0,'127.0.0.1',r));
const browser=await chromium.launch({headless:true,executablePath:process.env.HCC_CHROME_PATH});
const out=resolve('../05 Delivery Evidence/Acceptance/AC02-local');await mkdir(out,{recursive:true});
const results=[];
try{
  for(const [name,width,height] of [['phone',390,844],['tablet',673,1006]]){
    const context=await browser.newContext({viewport:{width,height},hasTouch:true});
    await context.addInitScript(()=>{
      localStorage.setItem('hcc-session','owner');
      window.Capacitor={isNativePlatform:()=>true,registerPlugin:name=>({addListener:async(event,fn)=>{if(name==='App'&&event==='backButton')window.testBack=fn;return {remove(){}};},setStyle:async()=>{},setBackgroundColor:async()=>{},setOverlaysWebView:async()=>{}})};
    });
    const page=await context.newPage();const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.goto(`http://127.0.0.1:${server.address().port}`);await page.locator(name==='phone'?'.m-shell':'.topbar').waitFor();
    const check=async selector=>{
      const box=await page.locator(selector).boundingBox();assert.ok(box,selector);
      assert.ok(Math.abs(box.x)<1&&Math.abs(box.y)<1&&Math.abs(box.width-width)<1&&Math.abs(box.height-height)<1,JSON.stringify({selector,box}));
      results.push({name,selector,box});
    };
    await page.evaluate(()=>scrollTo(0,500));
    await page.getByRole('button',{name:name==='phone'?'Menu':'Open menu',exact:true}).click();
    await check('.drawer-overlay');await page.locator('.drawer-tools summary').click();
    await page.getByRole('button',{name:'Demo controls',exact:true}).click();await check('.modal-overlay');
    await page.locator('.modal-close').click();await page.locator('.modal-overlay').waitFor({state:'hidden'});
    await page.getByRole('button',{name:/^Search pages and actions/}).click();await check('.search-overlay');
    await page.getByRole('combobox').fill('New reservation');await page.getByRole('combobox').press('Enter');
    await page.locator('.modal-overlay').waitFor();await check('.modal-overlay');
    await page.screenshot({path:resolve(out,name+'-booking.png')});
    // Negative control: emulate an engine discarding inset with no longhands.
    await page.locator('.modal-overlay').evaluate(el=>{for(const edge of ['top','right','bottom','left'])el.style[edge]='auto';});
    const broken=await page.locator('.modal-overlay').boundingBox();
    assert.ok(broken.y!==0||broken.height!==height||broken.width!==width,'Negative control must differ');
    results.push({name,negativeControl:broken});
    await page.locator('.modal-overlay').evaluate(el=>el.removeAttribute('style'));await check('.modal-overlay');
    await page.locator('.modal-close').click();await page.locator('.modal-overlay').waitFor({state:'hidden'});
    // Reload establishes home/root while keeping the native bridge simulation.
    await page.reload();await page.locator(name==='phone'?'.m-shell':'.topbar').waitFor();
    await page.evaluate(()=>window.testBack());await page.locator('.exit-ask').waitFor();await check('.exit-ask');
    await page.screenshot({path:resolve(out,name+'-exit.png')});await page.getByRole('button',{name:'Stay',exact:true}).click();
    assert.deepEqual(errors,[]);await context.close();
  }
}finally{await browser.close();await new Promise(r=>server.close(r));}
await writeFile(resolve(out,'results.json'),JSON.stringify(results,null,2));console.log('PASS: phone/tablet menu, demo sheet, search, booking modal and exit prompt; negative controls reproduce missing-edge geometry.');
