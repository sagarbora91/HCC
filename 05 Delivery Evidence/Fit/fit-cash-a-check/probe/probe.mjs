// fit-cash-a probe: interactive states the check tool does not reach (folio open in the record pane; cash book day view).
import {createRequire} from 'node:module';
const require=createRequire(import.meta.url);
const {chromium}=require('E:/ClaudeDev/cache/npm/_npx/9833c18b2d85bc59/node_modules/playwright');
const OUT='E:/Projects/HCC/worktrees/fit-cash-a/.scratch/probe/';
const VPS=[{id:'tab-land',w:1138,h:640,touch:true,screen:[1138,711]},{id:'tab-port',w:711,h:1066,touch:true,screen:[711,1138]},{id:'lap-1366',w:1366,h:657}];
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const browser=await chromium.launch({channel:'chrome'});const res=[];
const measure=page=>page.evaluate(()=>{const d=document.documentElement,c=document.querySelector('.content');
  const panels=[...document.querySelectorAll('[data-fit-scroll]')].filter(e=>e.offsetParent).map(e=>({l:e.getAttribute('aria-label'),h:Math.round(e.getBoundingClientRect().height)}));
  const acts=[...document.querySelectorAll('.fol-record .modal-actions .btn, .fit-panel-actions .btn')].map(e=>{const r=e.getBoundingClientRect();return {t:e.textContent.trim().slice(0,24),in:r.top>=0&&r.bottom<=innerHeight&&r.right<=innerWidth}});
  return {doc:[d.scrollHeight,innerHeight,d.scrollWidth,innerWidth],content:c?[c.scrollHeight,c.clientHeight]:null,panels,acts};});
for(const vp of VPS)for(const role of ['owner','cashier']){
  const ctx=await browser.newContext({viewport:{width:vp.w,height:vp.h},hasTouch:!!vp.touch});
  await ctx.addInitScript(({role,vp})=>{localStorage.setItem('hcc-session',role);localStorage.setItem('hcc-display-mode','auto');
    if(vp.touch){Object.defineProperty(screen,'width',{get:()=>vp.screen[0]});Object.defineProperty(screen,'height',{get:()=>vp.screen[1]});
      window.Capacitor={isNativePlatform:()=>true,registerPlugin:()=>({addListener:async()=>({remove(){}}),setOverlaysWebView:async()=>{},setBackgroundColor:async()=>{},setStyle:async()=>{}})};}},{role,vp});
  const page=await ctx.newPage();const errs=[];page.on('pageerror',e=>errs.push(String(e.message)));
  await page.goto('http://127.0.0.1:4193/');await page.waitForSelector('.topbar',{timeout:30000});await sleep(800);
  const go=id=>page.evaluate(id=>{const st={hcc:'page',page:id,idx:(history.state?.idx||0)+1};history.pushState(st,'');window.dispatchEvent(new PopStateEvent('popstate',{state:st}));},id);
  await go('folios');await sleep(600);
  // pick an in-house folio (more actions) through the filter, then open it in the pane
  await page.locator('.filters button',{hasText:'In house'}).click();await sleep(300);
  await page.locator('.fit-split-list .guest-link').first().click();await sleep(500);
  res.push({vp:vp.id,role,state:'folio-open',errs:[...errs],...await measure(page)});
  await page.screenshot({path:`${OUT}${vp.id}-${role}-folio-open.png`});
  await page.locator('.fol-record').evaluate(e=>e.closest('[data-fit-scroll]').scrollTop=99999);await sleep(200);
  await page.screenshot({path:`${OUT}${vp.id}-${role}-folio-open-bottom.png`});
  await page.locator('.filters button',{hasText:'All'}).click();
  await go('cashbook');await sleep(600);
  await page.locator('button',{hasText:'Open day'}).nth(1).click();await sleep(500);
  res.push({vp:vp.id,role,state:'cashbook-day',errs:[...errs],...await measure(page)});
  await page.screenshot({path:`${OUT}${vp.id}-${role}-cashbook-day.png`});
  await ctx.close();
}
await browser.close();
console.log(JSON.stringify(res,null,1));
