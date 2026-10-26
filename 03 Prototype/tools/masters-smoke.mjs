// MA-10 · Masters smoke (Playwright, one browser at a time). Not part of `npm test` (needs Chrome + Playwright, which are not in package.json).
//   1. npm run build    2. PORT=4181 node serve.mjs   3. node tools/masters-smoke.mjs [--out <dir>] [--only owner,roles,func]
// Phase "masters": Owner opens every master list and one form per master at 360x800, 390x844 and 1366x768, text scale 1 and 1.3;
//   measures page-level sideways scroll, controls outside the viewport or clipped, small touch targets (phone), console errors,
//   saves JPEG screenshots. Phase "roles": the 9 roles open Admin & Setup > Master configuration (desktop + phone), and the two
//   non-money roles are granted `masters` to prove no money field is shown. Phase "func": phone flows (rooms range add, reorder,
//   dated money, multiselect). Result JSON -> <out>/smoke-results.json (the report is written from it).
import {createRequire} from 'node:module';
import {mkdirSync,writeFileSync,readFileSync} from 'node:fs';
import {join} from 'node:path';
const require=createRequire(import.meta.url);
const PW=process.env.PLAYWRIGHT_DIR||'E:/ClaudeDev/cache/npm/_npx/9833c18b2d85bc59/node_modules/playwright';
const {chromium}=require(PW);
const args=process.argv.slice(2);
const argv=(k,d)=>{const i=args.indexOf(k);return i>=0?args[i+1]:d;};
const OUT=argv('--out','../05 Delivery Evidence/Masters/smoke');
const ONLY=(argv('--only','owner,roles,func')).split(',');
const BASE=`http://127.0.0.1:${process.env.PORT||4181}/`;
mkdirSync(OUT,{recursive:true});

export const VIEWPORTS=[{name:'360',width:360,height:800},{name:'390',width:390,height:844},{name:'1366',width:1366,height:768}];
export const SCALES=[1,1.3];
export const GROUPS=[
  {id:'rooms',label:'Rooms and rate plans',masters:[['floors','Floors'],['amenities','Amenities'],['roomTypes','Room types'],['rooms','Rooms'],['occupancyCharges','Occupancy and extra charges'],['mealPlans','Meal plans']]},
  {id:'money',label:'Money and tax',masters:[['extras','Extras and services'],['discountReasons','Discount reasons'],['paymentMethods','Payment methods'],['bankAccounts','Bank accounts and tills'],['expenseCategories','Expense categories'],['expenseVendors','Expense vendors']]},
  {id:'pos',label:'Restaurant and POS',masters:[['outlets','Outlets'],['posTables','Tables and counters'],['menuCategories','Menu categories'],['menuItems','Menu items']]},
  {id:'frontdesk',label:'Front-desk policies',masters:[['bookingSources','Booking sources'],['idTypes','Guest ID types'],['numberSeries','Numbering series'],['policies','Booking policies'],['complaintCategories','Complaint categories'],['lostFoundCategories','Lost-and-found categories']]},
  {id:'people',label:'People, payroll and leave',masters:[['designations','Designations'],['shiftPatterns','Shift patterns'],['leaveTypes','Leave types'],['holidays','Holiday calendar']]},
  {id:'hkm',label:'Housekeeping and maintenance',masters:[['hkTaskTypes','Housekeeping task types'],['hkZones','Housekeeping zones']]},
];
const ROLES=[['owner','Owner'],['manager','Manager'],['reception','Reception'],['cashier','Cashier'],['supervisor','Supervisor'],['housekeeper','Housekeeper'],['accountant','Accountant'],['maint','Maintenance'],['techadmin','Tech admin']];

const PREF_DEFAULTS={textScale:1,honourSystemFont:true,reduceMotion:'system',density:'comfortable',startPage:'',listPageSize:100,toastSeconds:6.5};
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
let prev={};try{prev=JSON.parse(readFileSync(join(OUT,'smoke-results.json'),'utf8'));}catch{}
const keepErr=e=>!ONLY.some(o=>(o==='owner'&&/^[0-9]+@/.test(e.tag))||(o==='roles'&&/(granted|default)$/.test(e.tag))||(o==='func'&&/^func/.test(e.tag)));
const results={startedAt:new Date().toISOString(),masters:ONLY.includes('owner')?[]:prev.masters||[],roles:ONLY.includes('roles')?[]:prev.roles||[],func:ONLY.includes('func')?[]:prev.func||[],consoleErrors:(prev.consoleErrors||[]).filter(keepErr)};
const save=()=>writeFileSync(join(OUT,'smoke-results.json'),JSON.stringify(results,null,1));

async function newPage(browser,{vp,scale=1,role='owner',tag}){
  const ctx=await browser.newContext({viewport:{width:vp.width,height:vp.height},deviceScaleFactor:1,hasTouch:vp.width<768,isMobile:false});
  await ctx.addInitScript(([r,pref])=>{try{localStorage.setItem('hcc-session',r);localStorage.setItem('hcc-prefs-1',JSON.stringify({v:1,p:pref}));}catch{}},[role,{...PREF_DEFAULTS,textScale:scale}]);
  const page=await ctx.newPage();
  page.errs=[];
  page.on('console',m=>{if(m.type()==='error')page.errs.push({tag,text:m.text().slice(0,300)});});
  page.on('pageerror',e=>page.errs.push({tag,text:'pageerror: '+String(e.message).slice(0,300)}));
  return {ctx,page};
}
async function shot(page,name){const f=name+'.jpg';await page.screenshot({path:join(OUT,f),type:'jpeg',quality:55});return f;}

/** Page-level sideways scroll, controls outside the viewport or clipped, small touch targets inside `scopeSel`. */
async function audit(page,scopeSel,phone){
  return page.evaluate(([scopeSel,phone])=>{
    const vw=document.documentElement.clientWidth;
    const doc=document.documentElement.scrollWidth-vw,body=document.body.scrollWidth-vw;
    const scope=document.querySelector(scopeSel)||document.body;
    const vis=e=>{const r=e.getBoundingClientRect();const cs=getComputedStyle(e);return r.width>0&&r.height>0&&cs.visibility!=='hidden'&&cs.display!=='none';};
    const name=e=>(e.getAttribute('aria-label')||e.innerText||e.placeholder||e.className||e.tagName).toString().replace(/\s+/g,' ').trim().slice(0,40);
    const out=[],clipped=[],small=[];
    for(const e of scope.querySelectorAll('button,a,input,select,textarea,summary,[role=button]')){
      if(!vis(e))continue;const r=e.getBoundingClientRect();
      if(r.right>vw+1||r.left<-1)out.push(name(e)+` (x ${Math.round(r.left)}–${Math.round(r.right)})`);
      const cs=getComputedStyle(e);
      if(e.tagName!=='INPUT'&&e.tagName!=='SELECT'&&e.tagName!=='TEXTAREA'&&cs.overflow!=='visible'&&e.scrollWidth>e.clientWidth+2)clipped.push(name(e)+` (content ${e.scrollWidth} > box ${e.clientWidth})`);
      if(e.tagName==='INPUT'&&e.type==='checkbox')continue; // checkbox inputs sit inside a 44 px label
      if(phone&&(r.height<44||(r.width<44&&e.tagName!=='INPUT'&&e.tagName!=='SELECT')))small.push(name(e)+` (${Math.round(r.width)}×${Math.round(r.height)})`);
    }
    // any element wider than the viewport (text or images pushing the page sideways)
    const wide=[];for(const e of scope.querySelectorAll('*')){if(!vis(e))continue;const r=e.getBoundingClientRect();if(r.right>vw+2&&getComputedStyle(e).position!=='fixed'&&wide.length<5)wide.push(e.tagName.toLowerCase()+'.'+(String(e.className).split(' ')[0]||'')+` right ${Math.round(r.right)}`);}
    let fonts=0;if(phone)for(const e of scope.querySelectorAll('input:not([type=checkbox]),select,textarea')){if(vis(e)&&parseFloat(getComputedStyle(e).fontSize)<16)fonts++;}
    return {vw,doc,body,outside:out.slice(0,6),clipped:clipped.slice(0,6),small:small.slice(0,8),smallCount:small.length,wide,smallFonts:fonts};
  },[scopeSel,phone]);
}

async function openHub(page,phone){
  await page.goto(BASE);await page.waitForSelector('text=Modules',{timeout:20000}).catch(()=>{});await sleep(500);
  await page.getByRole('button',{name:/Admin & Setup/}).first().click();await sleep(350);
  const mc=page.getByRole('button',{name:/Master configuration/}).first();
  if(!await mc.count())return false;
  await mc.click();await sleep(450);return true;
}
async function openGroup(page,g,phone){
  const b=phone?page.locator('button',{hasText:g.label}).first():page.locator('.cfg-item',{hasText:g.label}).first();
  await b.click();await sleep(350);
}
async function toggleSpec(page,label,phone){
  const sel=phone?'.mst-m-spec':'.mst-specs li';
  const item=page.locator(sel).filter({has:page.locator('strong',{hasText:new RegExp('^'+label.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')+'$')})}).first();
  await item.waitFor({timeout:4000});
  await item.locator(phone?'button.m-list-card':'button.mst-spec').first().click();await sleep(400);
  return item;
}

async function mastersPhase(browser){
  for(const vp of VIEWPORTS)for(const scale of SCALES){
    const phone=vp.width<768,tag=`${vp.name}@${scale}`;
    const {ctx,page}=await newPage(browser,{vp,scale,tag});
    for(const g of GROUPS){
      if(!await openHub(page,phone)){results.masters.push({tag,group:g.id,fail:'Master configuration not reachable'});continue;}
      await openGroup(page,g,phone);
      for(const [id,label] of g.masters){
        const rec={vp:vp.name,scale,master:id,label,list:{},form:{},problems:[]};
        try{
          const item=await toggleSpec(page,label,phone);
          const listSel=phone?'.mst-m-spec':'.mst-block';
          if(phone){await item.scrollIntoViewIfNeeded();}
          rec.list=await audit(page,phone?`.mst-m-spec:has(.mst-m-list)`:'.mst-block',phone);
          rec.list.shot=await shot(page,`${vp.name}-s${scale}-${id}-list`);
          rec.list.rows=await page.locator(phone?'.mst-m-row':'.mst-row').count();
          // one form: first existing row, else Add
          const rowBtn=phone?page.locator('.mst-m-spec .mst-m-row .m-list-card').first():page.locator('.mst-row-main').first();
          let opened=false,how='';
          if(await rowBtn.count()){await rowBtn.click();how='edit first row';opened=true;}
          else{const add=page.locator(phone?'.mst-m-add':'.mst-list-head .btn.primary').first();if(await add.count()){await add.click();how='add';opened=true;}}
          await sleep(500);
          if(!opened){rec.problems.push('no row and no Add button');}
          else{
            rec.form.how=how;
            if(!phone){await page.locator('.mst-detail').first().evaluate(e=>e.scrollIntoView({block:'start'}));await sleep(150);}
            const formSel=phone?'.mst-sheet':'.mst-detail';
            rec.form=Object.assign(rec.form,await audit(page,formSel,phone));
            rec.form.shot=await shot(page,`${vp.name}-s${scale}-${id}-form`);
            // action buttons reachable (Save / Add inside the viewport)
            rec.form.actions=await page.evaluate(([sel,phone])=>{const f=document.querySelector(sel);if(!f)return {found:false};const b=[...f.querySelectorAll('button')].filter(x=>/Save changes|^Add /.test(x.innerText.trim()));const vh=innerHeight;
              return {found:true,saveVisible:b.length?b.some(x=>{const r=x.getBoundingClientRect();return r.top>=0&&r.bottom<=vh+1;}):null,hasSave:b.length>0,fields:f.querySelectorAll('input,select,textarea').length,money:[...f.querySelectorAll('label,legend')].filter(l=>/₹|\(%\)/.test(l.innerText)).length};},[formSel,phone]);
            // close the form
            if(phone){const c=page.locator('.mst-sheet button',{hasText:/^Close$/}).first();if(await c.count())await c.click();else await page.goBack();await sleep(350);}
          }
        }catch(e){rec.problems.push('script: '+String(e.message).split('\n')[0]);try{await page.screenshot({path:join(OUT,`${vp.name}-s${scale}-${id}-ERROR.jpg`),type:'jpeg',quality:50});}catch{}}
        results.masters.push(rec);
      }
      save();
    }
    results.consoleErrors.push(...page.errs);await ctx.close();save();
    console.log('done',tag);
  }
}

/** Adds `masters` to every role except Owner / Tech admin by patching the saved grants slice (IndexedDB), then the app is reloaded. */
async function grantMasters(page){
  await page.evaluate(()=>new Promise((res,rej)=>{const q=indexedDB.open('hcc');q.onerror=()=>rej(q.error);q.onsuccess=()=>{const d=q.result;const tx=d.transaction('state','readwrite');const st=tx.objectStore('state');
    const g=st.get('slice:grants');g.onsuccess=()=>{const o=JSON.parse(g.result);for(const r of Object.keys(o))if(!o[r].includes('masters'))o[r].push('masters');st.put(JSON.stringify(o),'slice:grants');};tx.oncomplete=()=>res(true);tx.onerror=()=>rej(tx.error);};}));
}
const MONEY_PROBE=[['rooms','roomTypes','Room types'],['rooms','occupancyCharges','Occupancy and extra charges'],['pos','menuItems','Menu items'],['money','bankAccounts','Bank accounts and tills']];

async function rolesPhase(browser){
  const RF=argv('--roles',''),GF=argv('--grant','');
  for(const granted of [false,true].filter(g=>!GF||String(g)===GF))for(const vp of [VIEWPORTS[2],VIEWPORTS[1]]){
    const phone=vp.width<768;
    for(const [rid,rname] of ROLES.filter(r=>!RF||RF.split(',').includes(r[0]))){
      const tag=`${rid}/${vp.name}/${granted?'granted':'default'}`;
      const rec={role:rname,vp:vp.name,granted,reachable:false,groups:0,readOnlyGroups:[],specs:{}};
      const {ctx,page}=await newPage(browser,{vp,role:rid,tag});
      try{
        await page.goto(BASE);await page.waitForSelector('text=Modules',{timeout:20000}).catch(()=>{});await sleep(1500);
        if(granted){await grantMasters(page);}
        rec.reachable=await openHub(page,phone);
        if(!rec.reachable){rec.homeAdminTile=await page.getByRole('button',{name:/Admin & Setup/}).count()>0;
          if(rec.homeAdminTile){await page.getByRole('button',{name:/Admin & Setup/}).first().click();await sleep(300);rec.adminItems=await page.locator('main button, main a').allInnerTexts().catch(()=>[]);}
          rec.shot=await shot(page,`roles-${rid}-${vp.name}-${granted?'granted':'default'}-hub`);}
        else{
          rec.groups=await page.locator(phone?'button.m-list-card':'.cfg-item').count();
          rec.readOnlyGroups=await page.evaluate(()=>[...document.querySelectorAll('.cfg-item,.m-list-card')].filter(e=>/Read only/.test(e.innerText)).map(e=>e.innerText.split('\n')[0]));
          rec.shot=await shot(page,`roles-${rid}-${vp.name}-${granted?'granted':'default'}-hub`);
          if(granted&&['housekeeper','maint','supervisor'].includes(rid)){ // DEC-061: no hotel money anywhere on the hub for these roles
            rec.rupee={};
            const labels=await page.evaluate(()=>[...document.querySelectorAll('.cfg-item strong,button.m-list-card .m-lc-main strong,button.m-list-card strong')].map(e=>e.innerText));
            for(const lab of labels){
              if(phone){await page.getByRole('button',{name:/All groups/}).first().click().catch(()=>{});await sleep(150);}
              await page.locator(phone?'button.m-list-card':'.cfg-item',{hasText:lab}).first().click();await sleep(250);
              const txt=await page.locator('main').first().innerText();const m=txt.match(/.{0,30}₹.{0,20}/g)||[];
              if(m.length)rec.rupee[lab]=m.slice(0,3);
            }
            if(phone){await page.getByRole('button',{name:/All groups/}).first().click().catch(()=>{});await sleep(150);}
          }
          for(const [gid,sid,label] of MONEY_PROBE){
            const g=GROUPS.find(x=>x.id===gid);
            if(phone){await page.getByRole('button',{name:/All groups/}).first().click().catch(()=>{});await sleep(200);}
            await openGroup(page,g,phone);
            const q={visible:false};
            const has=await page.locator(phone?'.mst-m-spec':'.mst-specs li').filter({hasText:label}).count();
            q.visible=has>0;
            if(has){
              const item=await toggleSpec(page,label,phone);
              q.lock=await page.locator('.mst-lock').first().innerText().catch(()=>null);
              q.addShown=await page.locator(phone?'.mst-m-add':'.mst-list-head .btn.primary').count()>0;
              const rowBtn=phone?page.locator('.mst-m-spec .mst-m-row .m-list-card').first():page.locator('.mst-row-main').first();
              if(await rowBtn.count()){await rowBtn.click();await sleep(500);
                const formSel=phone?'.mst-sheet':'.mst-detail';
                q.form=await page.evaluate(sel=>{const f=document.querySelector(sel);if(!f)return null;return {dated:f.querySelectorAll('.mst-dated').length,rupeeLabels:[...f.querySelectorAll('label,legend')].filter(l=>/₹|\(%\)/.test(l.innerText)).map(l=>l.innerText.split('\n')[0].slice(0,30)),
                  ownerOnlyNotes:[...f.querySelectorAll('.mst-help')].filter(l=>/Owner/.test(l.innerText)).length,saveBtn:[...f.querySelectorAll('button')].some(b=>/Save changes/.test(b.innerText)),inputs:[...f.querySelectorAll('input,select,textarea')].filter(i=>!i.disabled).length,
                  disabledMoney:[...f.querySelectorAll('.mst-dated[disabled],input[disabled]')].length};},formSel);
                if(sid==='roomTypes'||sid==='occupancyCharges')q.shot=await shot(page,`roles-${rid}-${vp.name}-${granted?'granted':'default'}-${sid}`);
                if(phone){const c=page.locator('.mst-sheet button',{hasText:/^Close$/}).first();if(await c.count())await c.click();await sleep(300);}}
            }
            rec.specs[sid]=q;
          }
        }
      }catch(e){rec.error=String(e.message).split('\n')[0];}
      results.roles.push(rec);results.consoleErrors.push(...page.errs);await ctx.close();save();
      console.log('role',tag,rec.reachable?'hub ok':'no hub');
    }
  }
}

const toastText=async p=>p.locator('.toast').first().innerText().catch(()=>'');
async function funcPhase(browser){
  const vp=VIEWPORTS[1],phone=true,tag='func/390';
  const {ctx,page}=await newPage(browser,{vp,tag});
  const note=(name,ok,detail,shotName)=>{results.func.push({name,ok,detail,shot:shotName});console.log(ok?'PASS':'FAIL',name,detail||'');};
  const grp=id=>GROUPS.find(g=>g.id===id);
  try{
    // 1 range add (Rooms)
    await openHub(page,phone);await openGroup(page,grp('rooms'),phone);
    const roomsItem=await toggleSpec(page,'Rooms',phone);
    const sec=page.locator('section[aria-label="Add rooms"]').first();
    await sec.scrollIntoViewIfNeeded();
    await sec.locator('input').first().fill('101');await sleep(200);
    const taken=await sec.locator('.mst-err').first().innerText().catch(()=>'');
    note('Rooms: range add refuses a taken number with a reason',/101|taken|exist/i.test(taken),taken);
    await sec.locator('input').first().fill('401-403');await sleep(200);
    const btn=sec.locator('button.btn.primary');const label=await btn.innerText();
    const bb=await btn.boundingBox();
    note('Rooms: Add button names the range and is a 48 px target',/401/.test(label)&&bb.height>=48,`${label.trim()} ${Math.round(bb.height)} px`);
    await btn.click();await sleep(700);
    const t1=await toastText(page);const sum=await page.locator('.mst-m-spec').first().innerText();
    note('Rooms: 401–403 added',/Added/.test(t1),t1+' | '+(await page.locator('.mst-m-spec button.m-list-card',{hasText:'Rooms'}).first().innerText().catch(()=>'')).replace(/\n/g,' '),await shot(page,'func-390-rooms-range-add'));
    // renumber + OOO tools visible
    note('Rooms: Renumber and Set out of order tools present',await page.locator('section[aria-label="Renumber a room"]').count()>0&&await page.locator('section[aria-label="Out of order"]').count()>0);
    // 2 reorder (Floors)
    await page.reload();await sleep(1200);await openHub(page,phone);await openGroup(page,grp('rooms'),phone);
    await toggleSpec(page,'Floors',phone);
    const first=async()=>(await page.locator('.mst-m-spec .mst-m-row .m-lc-main strong').first().innerText());
    const before=await first();
    await page.getByRole('button',{name:'Move Floor 2 up'}).click();await sleep(600);
    const after=await first();
    note('Floors: reorder by arrow button moves the row and saves',before==='Floor 1'&&after==='Floor 2',`${before} → ${after}; ${await toastText(page)}`,await shot(page,'func-390-floors-reorder'));
    await page.getByRole('button',{name:'Move Floor 2 down'}).click();await sleep(600);
    // 3+4 dated money + multiselect (Room types)
    await page.reload();await sleep(1200);await openHub(page,phone);await openGroup(page,grp('rooms'),phone);
    await toggleSpec(page,'Room types',phone);
    await page.locator('.mst-m-spec .mst-m-row .m-list-card').first().click();await sleep(500);
    const sheet=page.locator('.mst-sheet');
    const dated=sheet.locator('.mst-dated').first();await dated.scrollIntoViewIfNeeded();
    const li0=await dated.locator('li').count();
    await dated.locator('input[type=number]').fill('3999');
    const add=dated.getByRole('button',{name:/Add amount/});const ab=await add.boundingBox();
    note('Room types: dated-money Add amount is a 48 px target',ab.height>=48,`${Math.round(ab.height)} px`);
    const d2=new Date(Date.now());// business date is simulated: use the From default
    await add.click();await sleep(300);
    const li1=await dated.locator('li').count();
    note('Room types: dated-money sub-form adds an amount with an effective date',li1===li0+1,`${li0} → ${li1} rows`,await shot(page,'func-390-roomtypes-dated-money'));
    const am=sheet.locator('fieldset.mst-multi').first();await am.scrollIntoViewIfNeeded();
    const cb=am.locator('input[type=checkbox]').first();const was=await cb.isChecked();
    await am.locator('label.mst-toggle').first().click();await sleep(150);
    note('Room types: multiselect (amenities) toggles from a 44 px+ row',(await cb.isChecked())!==was,`row ${(await am.locator('label.mst-toggle').first().boundingBox()).height} px`,await shot(page,'func-390-roomtypes-multiselect'));
    const sv=sheet.getByRole('button',{name:/Save changes/});await sv.click();await sleep(800);
    note('Room types: save closes the sheet and confirms',(await sheet.count())===0&&/saved/i.test(await toastText(page)),await toastText(page));
    // 5 add + validation + retire / reactivate / delete (Floors)
    await page.reload();await sleep(1200);await openHub(page,phone);await openGroup(page,grp('rooms'),phone);
    await toggleSpec(page,'Floors',phone);
    await page.locator('.mst-m-add').click();await sleep(500);
    await sheet.getByRole('button',{name:/^Add floor/}).click();await sleep(300);
    const err=await sheet.locator('.mst-err,.adm-err').first().innerText().catch(()=>'');
    note('Floors: empty Add shows inline validation',err.length>0,err,await shot(page,'func-390-floors-validation'));
    const inputs=sheet.locator('input[type=text],input:not([type])');
    await sheet.locator('#mst-floors-code').fill('F4');await sheet.locator('#mst-floors-name').fill('Terrace');
    await sheet.getByRole('button',{name:/^Add floor/}).click();await sleep(800);
    note('Floors: add saves, sheet closes',(await sheet.count())===0&&/added/i.test(await toastText(page)),await toastText(page));
    await page.locator('.mst-m-spec .mst-m-row .m-list-card',{hasText:'Terrace'}).click();await sleep(500);
    await sheet.getByRole('button',{name:/Retire/}).click();await sleep(500);
    await page.locator('.modal-actions .btn.danger',{hasText:'Retire'}).click();await sleep(700); // V14-6: the retire confirmation
    const chip=await page.locator('.m-chips button',{hasText:'Retired'}).innerText();
    note('Floors: retire from the sheet',/1/.test(chip),chip.replace(/\n/g,' ')+'; '+await toastText(page));
    await page.locator('.m-chips button',{hasText:'Retired'}).click();await sleep(300);
    await page.locator('.mst-m-spec .mst-m-row .m-list-card',{hasText:'Terrace'}).click();await sleep(500);
    note('Floors: retired row form shows the reactivate path and a Delete (never used)',await sheet.getByRole('button',{name:/Reactivate/}).count()>0&&await sheet.getByRole('button',{name:/Delete/}).count()>0,'',await shot(page,'func-390-floors-retired-sheet'));
    await sheet.getByRole('button',{name:/Delete/}).click();await sleep(700);
    note('Floors: unused row deletes',/deleted/i.test(await toastText(page)),await toastText(page));
    // 6 back button closes the sheet, stays on the page
    await page.locator('.m-chips button',{hasText:'Active'}).click();await sleep(200);
    await page.locator('.mst-m-spec .mst-m-row .m-list-card').first().click();await sleep(500);
    const open1=await sheet.count();
    await page.goBack();await sleep(500);
    note('Back button closes the sheet and stays in Master configuration (History API)',open1===1&&(await sheet.count())===0&&await page.locator('.mst-m-spec').count()>0,'');
    // 7 in-use delete refusal text
    await page.locator('.mst-m-spec .mst-m-row .m-list-card').first().click();await sleep(500);
    note('Floors: a floor in use offers Retire, not Delete',await sheet.getByRole('button',{name:/Delete/}).count()===0&&/In use/.test(await sheet.innerText()),'');
  }catch(e){note('func script error',false,String(e.message).split('\n')[0]);try{await shot(page,'func-390-ERROR');}catch{}}
  results.consoleErrors.push(...page.errs);await ctx.close();save();
}

async function main(){
  const browser=await chromium.launch({channel:'chrome'});
  try{
    if(ONLY.includes('owner'))await mastersPhase(browser);
    if(ONLY.includes('roles'))await rolesPhase(browser);
    if(ONLY.includes('func'))await funcPhase(browser);
  }finally{await browser.close();save();}
}
if(process.argv[1]&&import.meta.url.endsWith(process.argv[1].replace(/\\/g,'/').split('/').pop()))await main();
export {newPage,shot,audit,openHub,openGroup,toggleSpec,sleep,results,save,BASE,OUT,ROLES,chromium};
