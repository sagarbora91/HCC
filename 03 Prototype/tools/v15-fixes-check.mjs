// V15-2 / V15-3 · Browser check (Playwright, by hand; not part of npm test). Needs `npm run build` and `PORT=4184 node serve.mjs`.
//   node tools/v15-fixes-check.mjs [--out <dir>]
// Phone 360x780 (V15-3): Appearance & display — the app-bar title is fully visible on two lines, the business-date line stays, no sideways scroll.
// Tablet 800x1280 (V15-2): Master configuration > Room types > first row, type into Name, browser Back → "Discard changes?"; Keep editing keeps the
// text and the page; Back again → Discard leaves the page. Also: a clean form goes back without a prompt.
import {createRequire} from 'node:module';
import {mkdirSync} from 'node:fs';
import {join} from 'node:path';
const require=createRequire(import.meta.url);
const {chromium}=require(process.env.PLAYWRIGHT_DIR||'E:/ClaudeDev/cache/npm/_npx/9833c18b2d85bc59/node_modules/playwright');
const args=process.argv.slice(2);const oi=args.indexOf('--out');
const OUT=oi>=0?args[oi+1]:'../05 Delivery Evidence/Acceptance/v15-fixes';
const BASE=`http://127.0.0.1:${process.env.PORT||4184}/`;
mkdirSync(OUT,{recursive:true});
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const PREF={textScale:1,honourSystemFont:true,reduceMotion:'system',density:'comfortable',startPage:'',listPageSize:100,toastSeconds:6.5};
const browser=await chromium.launch({channel:process.env.PW_CHANNEL||'chrome'});
let bad=0;const log=[];
const check=(label,ok,detail)=>{if(!ok)bad++;const line=`${label}: ${JSON.stringify(detail)} ${ok?'OK':'FAIL'}`;log.push(line);console.log(line);};

// ── V15-3 phone ──
{
  const ctx=await browser.newContext({viewport:{width:360,height:780},deviceScaleFactor:2,isMobile:true,hasTouch:true});
  await ctx.addInitScript(p=>{try{localStorage.setItem('hcc-session','owner');localStorage.setItem('hcc-display-mode','phone');localStorage.setItem('hcc-prefs-1',JSON.stringify({v:1,p}));}catch{}},PREF);
  const page=await ctx.newPage();const errs=[];page.on('pageerror',e=>errs.push(String(e.message)));
  await page.goto(BASE);await page.waitForSelector('.m-appbar',{timeout:20000});await sleep(400);
  const measureTitle=async(name)=>{
    const m=await page.evaluate(()=>{
      const t=document.querySelector('.m-title');const d=document.querySelector('.m-date');const r=t.getBoundingClientRect();
      const range=document.createRange();range.selectNodeContents(t);const rects=[...range.getClientRects()];
      const lastRight=Math.max(...rects.map(x=>x.right)),lastBottom=Math.max(...rects.map(x=>x.bottom));
      const lines=new Set(rects.map(x=>Math.round(x.top))).size;
      // the first line must end at a word boundary: the text up to the first space after the midpoint sits on one line
      const txt=t.textContent;const cut=txt.indexOf(' ',Math.floor(txt.length/2)-3);let firstLineWords=true;
      if(cut>0){const r1=document.createRange();r1.setStart(t.firstChild,0);r1.setEnd(t.firstChild,cut);firstLineWords=new Set([...r1.getClientRects()].map(x=>Math.round(x.top))).size<=1;}
      const pill=document.querySelector('.m-demo-pill');
      return {title:txt,longClass:t.closest('.m-shell').classList.contains('m-longtitle'),fontSize:getComputedStyle(t).fontSize,lines,firstLineWords,titleW:Math.round(r.width),pillW:pill?Math.round(pill.getBoundingClientRect().width):null,
        clipped:lastRight>r.right+0.5||lastBottom>r.bottom+0.5||t.scrollHeight>t.clientHeight+1,box:{right:Math.round(r.right),bottom:Math.round(r.bottom),lastRight:Math.round(lastRight),lastBottom:Math.round(lastBottom),scrollH:t.scrollHeight,clientH:t.clientHeight,lineH:getComputedStyle(t).lineHeight},dateShown:!!d&&getComputedStyle(d).display!=='none'&&d.getBoundingClientRect().height>0,
        appbarH:Math.round(document.querySelector('.m-appbar').getBoundingClientRect().height),scrollW:document.documentElement.scrollWidth,vw:document.documentElement.clientWidth};
    });
    return m;
  };
  // Appearance & display from the user menu (Owner)
  await page.getByRole('button',{name:/User menu/}).click();await sleep(300);
  await page.getByRole('button',{name:/Appearance & display/}).first().click().catch(async()=>{await page.getByText('Appearance & display').first().click();});
  await sleep(500);
  let m=await measureTitle('prefs');
  check('V15-3 phone 360 · Appearance & display title',m.longClass&&!m.clipped&&m.lines===2&&m.firstLineWords&&!m.dateShown&&m.scrollW===m.vw&&/^18px$/.test(m.fontSize)&&(m.pillW===null||m.pillW<=48),m);
  await page.screenshot({path:join(OUT,'p-01-prefs-title-two-lines.png')});
  // a short title keeps one 18 px line
  await page.goBack();await sleep(400);
  m=await measureTitle('home');
  check('V15-3 phone 360 · short title stays one line',!m.longClass&&!m.clipped&&m.lines===1&&/^18px$/.test(m.fontSize),m);
  check('V15-3 phone · no page errors',errs.length===0,errs);
  await ctx.close();
}

// ── V15-2 tablet ──
{
  const ctx=await browser.newContext({viewport:{width:800,height:1280},deviceScaleFactor:1});
  await ctx.addInitScript(p=>{try{localStorage.setItem('hcc-session','owner');localStorage.setItem('hcc-display-mode','desktop');localStorage.setItem('hcc-prefs-1',JSON.stringify({v:1,p}));}catch{}},PREF);
  const page=await ctx.newPage();const errs=[];page.on('pageerror',e=>errs.push(String(e.message)));
  await page.goto(BASE);await page.waitForSelector('text=Modules',{timeout:20000}).catch(()=>{});await sleep(500);
  await page.getByRole('button',{name:/Admin & Setup/}).first().click();await sleep(350);
  await page.getByRole('button',{name:/Master configuration/}).first().click();await sleep(450);
  // Master configuration (hub) > group "Rooms and rate plans" > list "Room types" > first row (inline form in .mst-detail)
  const openRoomTypes=async()=>{
    if(!await page.locator('.cfg-item').count()){await page.getByRole('button',{name:/Admin & Setup/}).first().click();await sleep(350);await page.getByRole('button',{name:/Master configuration/}).first().click();await sleep(450);}
    if(!await page.locator('.mst-specs').count()){await page.locator('.cfg-item',{hasText:'Rooms and rate plans'}).first().click();await sleep(350);}
    if(!await page.locator('.mst-row-main').count()){await page.locator('.mst-specs li').filter({has:page.locator('strong',{hasText:/^Room types$/})}).first().locator('button.mst-spec').click();await sleep(400);}
    await page.locator('.mst-row-main').first().click();await sleep(400);};
  await openRoomTypes();
  const nameInput=()=>page.locator('.mst-detail input').filter({hasNot:page.locator('[type=checkbox]')}).nth(1);
  const dialog=()=>page.getByRole('alertdialog',{name:/Discard changes/});
  // 1. clean form: Back leaves without a prompt
  const before=await page.evaluate(()=>location.href+'|'+(document.querySelector('.mst-detail')?1:0));
  await page.goBack();await sleep(500);
  const cleanLeft=await page.evaluate(()=>!document.querySelector('.mst-detail'));
  check('V15-2 tablet · clean inline form: Back leaves without a prompt',cleanLeft&&!(await dialog().isVisible().catch(()=>false)),{before,cleanLeft});
  await page.goForward().catch(()=>{});await sleep(500);
  if(!(await page.locator('.mst-detail').count()))await openRoomTypes();
  // 2. dirty form: Back asks; Keep editing keeps the text
  const orig=await nameInput().inputValue();
  await nameInput().click();await page.keyboard.type(' v15');await sleep(200);
  await page.goBack();await sleep(500);
  const asked=await dialog().isVisible().catch(()=>false);
  check('V15-2 tablet · dirty inline form: Back opens "Discard changes?"',asked,{orig});
  await page.screenshot({path:join(OUT,'t-01-inline-dirty-back-asks.png')});
  await page.getByRole('button',{name:'Keep editing'}).click();await sleep(400);
  const kept=await nameInput().inputValue().catch(()=>null);
  check('V15-2 tablet · Keep editing stays on the form with the typed text',kept===orig+' v15'&&!(await dialog().isVisible().catch(()=>false)),{kept});
  await page.screenshot({path:join(OUT,'t-02-keep-editing-kept.png')});
  // 3. Back again → Discard leaves the page, the row is unchanged
  await page.goBack();await sleep(500);
  const asked2=await dialog().isVisible().catch(()=>false);
  await page.getByRole('button',{name:'Discard',exact:true}).click();await sleep(600);
  const left=await page.evaluate(()=>!document.querySelector('.mst-detail'));
  check('V15-2 tablet · Discard finishes the Back',asked2&&left,{asked2,left});
  await page.screenshot({path:join(OUT,'t-03-after-discard.png')});
  // the saved row kept its original name
  await page.goForward().catch(()=>{});await sleep(500);
  if(!(await page.locator('.mst-detail').count()))await openRoomTypes();
  const after=await nameInput().inputValue().catch(()=>null);
  check('V15-2 tablet · the row was not changed by Discard',after===orig,{after,orig});
  // 4. dirty form + in-app navigation (Back to Admin & Setup breadcrumb / home) asks too
  await nameInput().click();await page.keyboard.type(' again');await sleep(200);
  const crumb=page.getByRole('button',{name:/^Modules$|Admin & Setup/}).first();
  await crumb.click();await sleep(500);
  const asked3=await dialog().isVisible().catch(()=>false);
  check('V15-2 tablet · in-app navigation with a dirty inline form asks too',asked3,{asked3});
  if(asked3){await page.getByRole('button',{name:'Discard',exact:true}).click();await sleep(500);}
  check('V15-2 tablet · no page errors',errs.length===0,errs);
  await ctx.close();
}
await browser.close();
console.log(log.join('\n'));
console.log(bad?`\n${bad} check(s) FAILED`:'\nall checks OK');
process.exit(bad?1:0);
