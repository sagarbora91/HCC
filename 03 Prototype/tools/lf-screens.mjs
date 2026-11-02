// LF · screenshots of the new LF screens for the delivery evidence (at most 8 PNGs, fictional data). Playwright, by hand; not part of npm test.
// Needs `npm run build` and a server: `PORT=4183 node serve.mjs` (or BASE=http://127.0.0.1:<port>/). Playwright: PLAYWRIGHT_DIR=<dir with playwright> (default /opt/node-tools/node_modules/playwright).
//   node tools/lf-screens.mjs [--out <dir>]      default <repo>/05 Delivery Evidence/LF/screens
import {createRequire} from 'node:module';
import {existsSync,mkdirSync} from 'node:fs';
import {join,dirname} from 'node:path';
import {fileURLToPath} from 'node:url';
const require=createRequire(import.meta.url);
const {chromium}=require(process.env.PLAYWRIGHT_DIR||(existsSync('/opt/node-tools/node_modules/playwright')?'/opt/node-tools/node_modules/playwright':'playwright'));
const HERE=dirname(fileURLToPath(import.meta.url)),args=process.argv.slice(2);const opt=k=>{const i=args.indexOf(k);return i>=0?args[i+1]:undefined;};
const OUT=opt('--out')||join(HERE,'..','..','05 Delivery Evidence','LF','screens');mkdirSync(OUT,{recursive:true});
const BASE=process.env.BASE||`http://127.0.0.1:${process.env.PORT||4183}/`;
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
// [file, role, viewport, page, local tab (sessionStorage hcc-fit-tab:<page>) | '']
const SHOTS=[
  ['counter-bills-new.png','reception',{width:1366,height:720},'counter-bills','new-bill'],
  ['counter-bills-list.png','reception',{width:1366,height:720},'counter-bills','bills'],
  ['desk-alerts.png','reception',{width:1366,height:720},'desk-alerts',''],
  ['customers.png','owner',{width:1366,height:720},'customers',''],
  ['cashier-credit-guests.png','owner',{width:1366,height:720},'cashier','credit-guests'],
  ['phone-counter-bills.png','reception',{width:360,height:740,mobile:true},'counter-bills',''],
  ['phone-desk-alerts.png','reception',{width:360,height:740,mobile:true},'desk-alerts',''],
].slice(0,8);
const browser=await chromium.launch({channel:process.env.PW_CHANNEL||undefined});
let n=0;
for(const [file,role,vp,id,tab] of SHOTS){
  const ctx=await browser.newContext({viewport:{width:vp.width,height:vp.height},deviceScaleFactor:vp.mobile?2:1,hasTouch:!!vp.mobile,isMobile:!!vp.mobile});
  await ctx.addInitScript(({role,mobile})=>{try{localStorage.setItem('hcc-session',role);localStorage.setItem('hcc-display-mode',mobile?'mobile':'desktop');}catch{/* blocked */}},{role,mobile:!!vp.mobile});
  const page=await ctx.newPage();await page.goto(BASE);await page.waitForSelector('.topbar,.m-appbar,main',{timeout:30000}).catch(()=>{});await sleep(900);
  await page.evaluate(({id,tab})=>{try{if(tab)sessionStorage.setItem('hcc-fit-tab:'+id,tab);}catch{/* blocked */}
    const idx=(history.state&&history.state.idx||0)+1;const st={hcc:'page',page:id,idx};history.pushState(st,'');window.dispatchEvent(new PopStateEvent('popstate',{state:st}));},{id,tab});
  await sleep(700);await page.screenshot({path:join(OUT,file)});n++;console.log('saved',file);await ctx.close();
}
await browser.close();console.log(n+' screenshots in '+OUT);
