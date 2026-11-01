// V14-3 · Tablet masters layout check (Playwright, by hand; not part of npm test). Needs `npm run build` and `PORT=4183 node serve.mjs`.
//   node tools/tablet-masters-check.mjs [--out <dir>]   Owner, display mode Desktop, Master configuration > Rooms and rate plans.
// At 800x1280 (portrait) and 1280x800 (landscape): opens Room types > first row, Rooms (tools row), Payment methods > first row
// (dated money) and asserts: first form input >= 240 px wide, no sideways page scroll, nothing right of the viewport.
import {createRequire} from 'node:module';
import {mkdirSync} from 'node:fs';
import {join} from 'node:path';
const require=createRequire(import.meta.url);
const {chromium}=require(process.env.PLAYWRIGHT_DIR||'E:/ClaudeDev/cache/npm/_npx/9833c18b2d85bc59/node_modules/playwright');
const args=process.argv.slice(2);const oi=args.indexOf('--out');
const OUT=oi>=0?args[oi+1]:'../05 Delivery Evidence/Acceptance/v14-fixes';
const BASE=`http://127.0.0.1:${process.env.PORT||4183}/`;
mkdirSync(OUT,{recursive:true});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const PREF={textScale:1,honourSystemFont:true,reduceMotion:'system',density:'comfortable',startPage:'',listPageSize:100,toastSeconds:6.5};
const browser=await chromium.launch({channel:process.env.PW_CHANNEL||'chrome'});
let bad=0;const log=[];
for(const [name,w,h] of [['portrait',800,1280],['landscape',1280,800]]){
  const ctx=await browser.newContext({viewport:{width:w,height:h},deviceScaleFactor:1});
  await ctx.addInitScript(p=>{try{localStorage.setItem('hcc-session','owner');localStorage.setItem('hcc-display-mode','desktop');localStorage.setItem('hcc-prefs-1',JSON.stringify({v:1,p}));}catch{}},PREF);
  const page=await ctx.newPage();const errs=[];
  page.on('pageerror',e=>errs.push(String(e.message)));
  await page.goto(BASE);await page.waitForSelector('text=Modules',{timeout:20000}).catch(()=>{});await sleep(500);
  await page.getByRole('button',{name:/Admin & Setup/}).first().click();await sleep(350);
  await page.getByRole('button',{name:/Master configuration/}).first().click();await sleep(450);
  // FIT-ADMIN-A: below 900 px the group opens over the list (SplitView slide-over): go Back to the list before picking another group.
  const group=async n=>{const b=page.locator('.fit-split-back');if(await b.isVisible().catch(()=>false)){await b.click();await sleep(250);}await page.locator('.cfg-item',{hasText:n}).first().click();await sleep(350);};
  const measure=async(label,shotName)=>{
    const m=await page.evaluate(()=>{
      const vw=document.documentElement.clientWidth;
      const inp=document.querySelector('.mst-detail .mst-fields input:not([type=checkbox]),.mst-detail input:not([type=checkbox]),.mst-detail select');
      const r=inp?.getBoundingClientRect();
      const tools=[...document.querySelectorAll('.rms-tool input:not([type=checkbox]):not([type=radio]),.rms-tool select')].map(e=>e.getBoundingClientRect().width).filter(x=>x>0);
      const dm=[...document.querySelectorAll('.mst-dated-add input,.mst-dated-add select')].map(e=>e.getBoundingClientRect().width).filter(x=>x>0);
      const wide=[...document.querySelectorAll('.mst-block *')].filter(e=>{const b=e.getBoundingClientRect();return b.width>0&&b.right>vw+1;}).length;
      const det=document.querySelector('.mst-detail')?.getBoundingClientRect();
      return {vw,scrollW:document.documentElement.scrollWidth,inputW:r?Math.round(r.width):null,inputRight:r?Math.round(r.right):null,detailW:det?Math.round(det.width):null,toolInputMin:tools.length?Math.round(Math.min(...tools)):null,datedInputMin:dm.length?Math.round(Math.min(...dm)):null,wide};
    });
    const ok=m.scrollW===m.vw&&m.wide===0&&(m.inputW===null||m.inputW>=240)&&(name!=='portrait'||((m.toolInputMin===null||m.toolInputMin>=240)&&(m.datedInputMin===null||m.datedInputMin>=240)));
    if(!ok)bad++;
    log.push(`${name} ${w}x${h} | ${label}: ${JSON.stringify(m)} ${ok?'OK':'FAIL'}`);
    if(shotName){await page.evaluate(()=>{const t=document.querySelector('.mst-detail')||document.querySelector('.mst-list');t?.scrollIntoView({block:'start'});});await sleep(150);await page.screenshot({path:join(OUT,`t-${name}-${shotName}.png`),fullPage:false});}
  };
  await group('Rooms and rate plans');
  const spec=async l=>{const b=page.locator('.mst-specs li').filter({has:page.locator('strong',{hasText:new RegExp('^'+l+'$')})}).first().locator('button.mst-spec');await b.click();await sleep(400);};
  await spec('Room types');
  await page.locator('.mst-row-main').first().click();await sleep(300);
  await measure('Room types > row','roomtypes-form');
  await page.locator('.mst-list-head .btn.primary').first().click();await sleep(300);
  await measure('Room types > Add',null);
  await spec('Room types'); // collapse
  await spec('Rooms');
  await measure('Rooms tools row','rooms-tools');
  await page.locator('.mst-row-main').first().click();await sleep(300);
  await measure('Rooms > row',null);
  await spec('Rooms');
  await group('Money and tax');
  await spec('Payment methods');
  await page.locator('.mst-list-head .btn.primary').first().click();await sleep(300);
  await measure('Payment methods > Add','paymethod-add');
  await spec('Payment methods');
  await group('Rooms and rate plans');
  await spec('Meal plans');
  await page.locator('.mst-row-main').first().click();await sleep(300);
  if(!await page.locator('.mst-dated').count()){bad++;log.push('no dated-money field found on Meal plans');}
  await page.locator('.mst-dated').first().scrollIntoViewIfNeeded().catch(()=>{});
  await measure('Meal plans > row (dated money)','mealplan-dated');
  if(errs.length){bad++;log.push('page errors: '+errs.join(' | '));}
  await ctx.close();
}
await browser.close();
console.log(log.join('\n'));
console.log(bad?`${bad} FAIL`:'ALL OK');
process.exit(bad?1:0);
