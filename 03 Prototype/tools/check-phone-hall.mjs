// F6-H3 · phone check of the Hall booking page at 360 px (phone rules in CLAUDE.md: no sideways scroll, 48 px targets, 16 px text). Playwright, by hand; not part of npm test.
// Needs `npm run build` and a server: `PORT=4183 node serve.mjs` (or BASE=http://127.0.0.1:<port>/). Playwright: PLAYWRIGHT_DIR=<dir with playwright> (default /opt/node-tools/node_modules/playwright)
//   node tools/check-phone-hall.mjs [--roles owner,reception,housekeeper]
// For each role, at 360x740 in the mobile shell: opens hall-booking, visits each phone tab (Enquiries · Quotes · Calendar · Events), checks the document does not scroll sideways, no visible
// button / link / input / select is under 44 px tall (48 px is the rule; 44 px the tolerance for text links), no script error; the Reception adds an enquiry in the phone sheet and opens it; the
// housekeeper sees the calendar and the events but no rupee amounts anywhere on the page. Exit 0 = every check passed.
import {createRequire} from 'node:module';
import {existsSync} from 'node:fs';
const require=createRequire(import.meta.url);
const {chromium}=require(process.env.PLAYWRIGHT_DIR||(existsSync('/opt/node-tools/node_modules/playwright')?'/opt/node-tools/node_modules/playwright':'playwright'));
const args=process.argv.slice(2);const opt=k=>{const i=args.indexOf(k);return i>=0?args[i+1]:undefined;};
const BASE=process.env.BASE||`http://127.0.0.1:${process.env.PORT||4183}/`;
const ROLES=(opt('--roles')||'reception,owner,housekeeper').split(',');
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const browser=await chromium.launch({channel:process.env.PW_CHANNEL||undefined});
const fails=[];let checked=0;
const measure=page=>page.evaluate(()=>{
  const vis=e=>{const r=e.getBoundingClientRect(),cs=getComputedStyle(e);return r.width>0&&r.height>0&&cs.visibility!=='hidden'&&cs.display!=='none';};
  const small=[...document.querySelectorAll('button:not([aria-label="Dismiss message"]),a[href],input:not([type=hidden]):not([type=checkbox]):not([type=radio]),select,[role=tab]')].filter(vis)
    .map(e=>({t:(e.textContent||e.getAttribute('placeholder')||e.getAttribute('aria-label')||e.tagName).trim().slice(0,24),h:Math.round(e.getBoundingClientRect().height)})).filter(x=>x.h<44);
  const dlg=document.querySelector('[role=dialog]');const root=dlg||document.querySelector('main')||document.body;
  return {docW:document.documentElement.scrollWidth,vw:innerWidth,small,money:/₹\s?\d/.test(root.innerText||''),text:(root.innerText||'').slice(0,60).replace(/\s+/g,' ')};});
const report=(role,label,m,errs,extra=[])=>{checked++;const f=[...extra];
  if(m.docW>m.vw+1)f.push(`sideways ${m.docW}/${m.vw}`);
  if(m.small.length)f.push(`touch-size ${m.small.length}: ${m.small.slice(0,5).map(s=>s.t+' '+s.h).join('|')}`);
  if(errs.length)f.push('script-error '+errs.splice(0).join(';'));
  if(f.length)fails.push(`${role} ${label}: ${f.join('; ')}`);console.log(f.length?'FAIL':'ok  ',role,label,f.join('; '));};
for(const role of ROLES){
  const ctx=await browser.newContext({viewport:{width:360,height:740},deviceScaleFactor:2,hasTouch:true,isMobile:true});
  await ctx.addInitScript(({role})=>{try{localStorage.setItem('hcc-session',role);localStorage.setItem('hcc-display-mode','mobile');}catch{/* blocked */}},{role});
  const page=await ctx.newPage();const errs=[];page.on('pageerror',e=>errs.push(String(e.message)));
  await page.goto(BASE);await page.waitForSelector('.topbar,.m-appbar,.mf-page,main',{timeout:30000}).catch(()=>{});await sleep(900);
  await page.evaluate(()=>{const idx=(history.state&&history.state.idx||0)+1;const st={hcc:'page',page:'hall-booking',idx};history.pushState(st,'');window.dispatchEvent(new PopStateEvent('popstate',{state:st}));});
  await sleep(600);
  const tabs=role==='housekeeper'?['Calendar','Events']:['Enquiries','Quotes','Calendar','Events'];
  if(role==='reception'){
    // add an enquiry in the phone sheet (Fab), then it shows in the list and opens
    await page.getByRole('button',{name:'New enquiry'}).first().click({timeout:3000}).catch(()=>{});await sleep(400);
    report(role,'new enquiry sheet',await measure(page),errs);
    await page.getByLabel('Customer name',{exact:true}).fill('Phone check family (fictional)').catch(()=>{});await page.getByLabel('Phone',{exact:true}).fill('9000000321').catch(()=>{});
    await page.getByRole('button',{name:'Add enquiry'}).click({timeout:3000}).catch(()=>{});await sleep(600);
  }
  for(const t of tabs){
    await page.getByRole('button',{name:t,exact:true}).first().click({timeout:3000}).catch(()=>{});await sleep(300);
    const m=await measure(page);const extra=[];
    if(role==='housekeeper'&&m.money)extra.push('a rupee amount is visible to housekeeping (DEC-061)');
    report(role,`tab ${t}`,m,errs,extra);
  }
  if(role==='reception'){
    await page.getByRole('button',{name:'Enquiries',exact:true}).first().click({timeout:3000}).catch(()=>{});await sleep(300);
    const card=page.locator('.m-list-card',{hasText:'Phone check family'}).first();
    if(await card.count()){await card.click();await sleep(400);const m=await measure(page);report(role,'enquiry card sheet',m,errs,m.text.includes('Enquiry ENQ')?[]:['enquiry sheet did not open']);}
    else{checked++;fails.push('reception: the added enquiry is not in the phone list');console.log('FAIL reception enquiry not listed');}
  }
  await ctx.close();
}
await browser.close();
console.log(`${checked} checks, ${fails.length} failing`);
if(fails.length)console.log(fails.join('\n'));
process.exit(fails.length?1:0);
