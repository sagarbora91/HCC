// LF-ALERTS · lane lf-alerts (LF contract §9): B3 wake-up call alerts, B4 desk reminders, A8 function keys. Names prefixed [LF-ALERTS].
import {readFileSync} from 'node:fs';
import {writeFile} from 'node:fs/promises';
import {build} from 'esbuild';
await writeFile('tests/.generated/lf-alerts-entry.ts',"export {navItems} from '../../src/app/nav';export {PAGE_CATEGORY} from '../../src/app/navmap';export {SUB_SCREENS} from '../../src/app/subscreens';");
await build({entryPoints:['tests/.generated/lf-alerts-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/lf-alerts.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'}});
const X=await import('./.generated/lf-alerts.mjs');
export function register({test,M,assert}){
  const {seed,execute,rmdDue,rmdList,rmdWakeDue,rmdRoll,rmdDueDate,rmdLeadDefault,rmdSlice,rmdAttentionCount,SHORTCUTS,shortcutFor,shortcutAllowed,shortcutKeyOfPage,can,cfgValueOn,gsvSlice,
    }=M;
  const {navItems,PAGE_CATEGORY,SUB_SCREENS}=X;
  const at=(s,clock)=>{const c=structuredClone(s);c.clock=clock;c.date=clock.slice(0,10);return c;};
  const wake=(s,extra={})=>execute(s,'reception',{type:'GSV_REQUEST_CREATE',id:'HCC-1041',requestType:'Wake-up call',time:'06:30',...extra});
  const lastReq=s=>gsvSlice(s).requests.at(-1);
  // ── B3 wake-up ──
  test('[LF-ALERTS] B3 wake-up call: wakeAt must be after the clock, today or tomorrow, YYYY-MM-DDTHH:mm; the SLA clock starts at the wake-up time',()=>{
    const s=seed();
    assert.throws(()=>wake(s,{wakeAt:'2026-10-24T07:59'}),/after the current time/);assert.throws(()=>wake(s,{wakeAt:'2026-10-26T06:00'}),/today or tomorrow/);
    assert.throws(()=>wake(s,{wakeAt:'26-10-25 06:00'}),/YYYY-MM-DDTHH:mm/);
    const t=wake(s,{wakeAt:'2026-10-25T05:45'});const r=lastReq(t);assert.equal(r.wakeAt,'2026-10-25T05:45');assert.equal(r.scheduledFor,'2026-10-25T05:45');assert.equal(r.dueAt,'2026-10-25T05:50','SLA 5 min from the wake-up time');
    const u=wake(s,{time:'09:15'});assert.equal(lastReq(u).wakeAt,'2026-10-24T09:15','a plain time today sets wakeAt too');
    const v=wake(at(s,'2026-10-24T07:00'),{time:'06:30'});assert.equal(lastReq(v).wakeAt,'2026-10-25T06:30','a time already past is tomorrow (existing rule)');
    const o=execute(s,'reception',{type:'GSV_REQUEST_CREATE',id:'HCC-1041',requestType:'Extra towels'});assert.equal(lastReq(o).wakeAt,undefined);});
  test('[LF-ALERTS] B3 desk alert: due when wakeAt ≤ clock and not finished; Done completes it; Snooze moves it 5 minutes (audited); housekeeping never sees it',()=>{
    let s=wake(seed(),{wakeAt:'2026-10-24T09:00'});const id=lastReq(s).id;
    assert.equal(rmdWakeDue(s).length,0);assert.equal(rmdDue(s,'reception').count,0);
    const due=at(s,'2026-10-24T09:00');assert.deepEqual(rmdWakeDue(due).map(r=>r.id),[id]);assert.equal(rmdDue(due,'reception').wake.length,1);
    for(const u of ['owner','manager','cashier'])assert.equal(rmdDue(due,u).wake.length,1,u);
    for(const u of ['housekeeper','supervisor','maint','accountant','techadmin'])assert.equal(rmdDue(due,u).count,0,u);
    const sn=execute(due,'reception',{type:'GSV_WAKE_SNOOZE',id});assert.equal(gsvSlice(sn).requests.find(r=>r.id===id).wakeAt,'2026-10-24T09:05');assert.equal(rmdWakeDue(sn).length,0);
    assert.ok(sn.audit[0].detail.includes('snoozed'));assert.ok(gsvSlice(sn).requests.find(r=>r.id===id).log.some(l=>/Snoozed 5 minutes/.test(l.what)));
    assert.equal(rmdWakeDue(at(sn,'2026-10-24T09:05')).length,1,'alerts again at the new time');
    const done=execute(due,'reception',{type:'GSV_REQUEST_COMPLETE',id,note:'Wake-up call made'});assert.equal(rmdWakeDue(done).length,0);
    assert.throws(()=>execute(due,'housekeeper',{type:'GSV_WAKE_SNOOZE',id}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>execute(done,'reception',{type:'GSV_WAKE_SNOOZE',id}),/already finished/);
    const towels=execute(seed(),'reception',{type:'GSV_REQUEST_CREATE',id:'HCC-1041',requestType:'Extra towels'});assert.throws(()=>execute(towels,'reception',{type:'GSV_WAKE_SNOOZE',id:lastReq(towels).id}),/Only a wake-up call/);});
  // ── B4 reminders ──
  const save=(s,u,patch={})=>execute(s,u,{type:'RMD_SAVE',subject:'Fire extinguisher refill',date:'2026-10-28',...patch});
  const first=s=>rmdSlice(s).reminders.at(-1);
  test('[LF-ALERTS] B4 reminders: setting default 3 lead days; shows from date − leadDays; per role; Housekeeping / Maintenance cannot create',()=>{
    assert.equal(rmdLeadDefault(seed()),3);assert.equal(cfgValueOn(seed(),'reminders.leadDaysDefault'),3);
    let s=save(seed(),'reception');const r=first(s);assert.equal(r.leadDays,3);assert.equal(r.role,'all');assert.equal(r.status,'open');
    assert.equal(rmdList(s,'reception','today').length,0,'28 Oct − 3 days = 25 Oct, not yet');assert.equal(rmdList(s,'reception','upcoming').length,1);
    const d25=at(s,'2026-10-25T09:00');assert.equal(rmdList(d25,'reception','today').length,1);assert.equal(rmdDue(d25,'reception').reminders.length,1);assert.equal(rmdAttentionCount(d25,'reception'),1);
    assert.equal(rmdDue(d25,'housekeeper').count,0);assert.equal(rmdDue(d25,'accountant').count,0,'the banner is the front desk\'s');assert.equal(rmdList(d25,'accountant','today').length,1,'the Accountant holds PERM-RMD-001');assert.equal(rmdList(d25,'cashier','today').length,0,'the Cashier does not');
    s=save(seed(),'manager',{role:'Reception',leadDays:0,subject:'For reception only'});const rr=first(s);assert.equal(rr.leadDays,0);
    assert.equal(rmdList(at(s,'2026-10-28T09:00'),'reception','today').length,1);assert.equal(rmdList(at(s,'2026-10-28T09:00'),'cashier','today').length,0,'addressed to another role');assert.equal(rmdList(at(s,'2026-10-28T09:00'),'owner','today').length,1,'the Owner sees all');
    for(const u of ['housekeeper','maint','supervisor','cashier'])assert.throws(()=>save(seed(),u),e=>e.code==='FORBIDDEN',u);assert.equal(first(save(seed(),'accountant')).by,'accountant');
    assert.throws(()=>save(seed(),'reception',{role:'Owner'}),/everyone or to your own role/);assert.throws(()=>save(seed(),'reception',{subject:'x'}),/Subject/);assert.throws(()=>save(seed(),'reception',{date:'soon'}),/date/);
    assert.throws(()=>save(seed(),'reception',{leadDays:99}),/Lead days/);assert.throws(()=>save(seed(),'reception',{repeat:'weekly'}),/Repeat/);assert.throws(()=>save(seed(),'reception',{note:'Passport 234567890124'}),/ID number/);
    const cfg=structuredClone(seed());cfg.configX={values:{'reminders.leadDaysDefault':5}};assert.equal(first(save(cfg,'reception')).leadDays,5);});
  test('[LF-ALERTS] B4 done / dismiss / repeat: monthly and yearly roll the date (month ends clamp); dismissal needs a reason; edits only by author or a Manager / Owner',()=>{
    assert.equal(rmdRoll('2026-01-31','monthly'),'2026-02-28');assert.equal(rmdRoll('2026-12-15','monthly'),'2027-01-15');assert.equal(rmdRoll('2028-02-29','yearly'),'2029-02-28');assert.equal(rmdRoll('2026-05-05','none'),'2026-05-05');
    let s=save(seed(),'reception',{repeat:'monthly'});const id=first(s).id;
    s=execute(s,'reception',{type:'RMD_DONE',id});let r=first(s);assert.equal(r.status,'open');assert.equal(r.nextDate,'2026-11-28');assert.equal(rmdDueDate(r),'2026-11-28');assert.equal(r.doneBy,'reception');
    s=execute(s,'reception',{type:'RMD_DONE',id});assert.equal(first(s).nextDate,'2026-12-28');
    let o=save(seed(),'reception');const oid=first(o).id;o=execute(o,'accountant',{type:'RMD_DONE',id:oid});assert.equal(first(o).status,'done');assert.equal(rmdList(o,'owner','done').length,1);
    assert.throws(()=>execute(o,'reception',{type:'RMD_DONE',id:oid}),/already finished/);
    let d=save(seed(),'reception');const did=first(d).id;assert.throws(()=>execute(d,'reception',{type:'RMD_DISMISS',id:did,reason:'x'}),/reason/);
    d=execute(d,'reception',{type:'RMD_DISMISS',id:did,reason:'Done by the vendor already'});assert.equal(first(d).status,'dismissed');assert.ok(d.audit[0].detail.includes('dismissed'));
    let e=save(seed(),'reception');const eid=first(e).id;
    assert.throws(()=>execute(e,'accountant',{type:'RMD_SAVE',id:eid,subject:'Changed',date:'2026-10-29'}),/author, a Manager or the Owner/);
    e=execute(e,'manager',{type:'RMD_SAVE',id:eid,subject:'Changed',date:'2026-10-29'});assert.equal(first(e).subject,'Changed');assert.equal(first(e).date,'2026-10-29');
    assert.throws(()=>execute(e,'housekeeper',{type:'RMD_DONE',id:eid}),e2=>e2.code==='FORBIDDEN');
    const forRec=save(seed(),'manager',{role:'Reception'});assert.throws(()=>execute(forRec,'accountant',{type:'RMD_DONE',id:first(forRec).id}),/not addressed to you/);});
  // ── A8 function keys ──
  test('[LF-ALERTS] A8 shortcut table: unique keys, never F1 / F5 / F11 / F12, every permission is a real one, the ten contract rows',()=>{
    const keys=SHORTCUTS.map(x=>x.key);assert.equal(new Set(keys).size,keys.length);
    assert.deepEqual(keys,['F2','F3','F4','F6','F7','F8','F9','F10','Alt+B','Alt+/']);
    for(const bad of ['F1','F5','F11','F12'])assert.ok(!keys.includes(bad),bad);
    const s=seed();for(const x of SHORTCUTS){const perms=[].concat(x.permission);for(const p of perms)assert.ok(M.permissions.includes(p),x.id+' '+p);}
    assert.ok(SHORTCUTS.filter(x=>x.page&&!x.optional).every(x=>navItems.some(n=>n.id===x.page)),'every non-optional target page exists');void s;});
  test('[LF-ALERTS] A8 shortcutFor: F-keys alone, Alt+B / Alt+/ with Alt only; Ctrl / Shift / Alt variants of F-keys and browser keys are ignored',()=>{
    assert.equal(shortcutFor({key:'F2'}).id,'new-booking');assert.equal(shortcutFor({key:'F10'}).id,'cashier');assert.equal(shortcutFor({key:'b',altKey:true}).id,'counter-bill');assert.equal(shortcutFor({key:'B',altKey:true}).id,'counter-bill');
    assert.equal(shortcutFor({key:'/',altKey:true}).id,'shortcut-list');
    for(const e of [{key:'F2',ctrlKey:true},{key:'F2',shiftKey:true},{key:'F2',altKey:true},{key:'F1'},{key:'F5'},{key:'F11'},{key:'F12'},{key:'k',ctrlKey:true},{key:'b'},{key:'b',altKey:true,ctrlKey:true}])assert.equal(shortcutFor(e),undefined,JSON.stringify(e));});
  test('[LF-ALERTS] A8 permission filter: Reception has F2–F8 / F9, the Cashier has no F2 / F3; Housekeeping has none except the shortcut list; the list shows to everyone',()=>{
    const s=seed();const has=u=>p=>can(s,u,p);const allowed=u=>SHORTCUTS.filter(x=>shortcutAllowed(x,has(u))).map(x=>x.key);
    assert.deepEqual(allowed('reception'),['F2','F3','F4','F6','F7','F8','F9','F10','Alt+B','Alt+/']);assert.ok(!allowed('cashier').includes('F2'));assert.ok(allowed('cashier').includes('F10'));
    assert.deepEqual(allowed('housekeeper'),['Alt+/']);assert.deepEqual(allowed('maint'),['Alt+/']);assert.equal(shortcutKeyOfPage('arrivals'),'F3');assert.equal(shortcutKeyOfPage('keys'),undefined);});
  test('[LF-ALERTS] A8 inert with a dialog open, on the phone shell and for a missing page; the banner is mounted in both shells; the user menu lists the shortcuts',()=>{
    const app=readFileSync('src/app/App.tsx','utf8');
    assert.match(app,/LF-ALERTS · A8/);assert.match(app,/if\(!signedIn\|\|display\.isMobile\)return;/);assert.match(app,/if\(modalRef\.current\|\|searchOpen\|\|menuOpen\)return;/);
    assert.match(app,/<DeskAlerts\/>/);assert.match(app,/<DeskAlerts phone\/>/);assert.match(readFileSync('src/app/mobile/sheets.tsx','utf8'),/open\(\{type:'shortcuts'\}\)/);
    assert.match(readFileSync('src/app/SearchPalette.tsx','utf8'),/shortcutKeyOfPage/);});
  test('[LF-ALERTS] page desk-alerts: category front, nav item for the reminders permissions, four sub-screens, phone page, Dashboard attention line',()=>{
    assert.equal(PAGE_CATEGORY['desk-alerts'],'front');const n=navItems.find(x=>x.id==='desk-alerts');assert.ok(n);assert.ok([].concat(n.p).includes('checkin'));
    assert.deepEqual(SUB_SCREENS.filter(x=>x.page==='desk-alerts').map(x=>x.id),['today','upcoming','done','wake-ups']);
    assert.match(readFileSync('src/features/alerts/register.tsx','utf8'),/mobile:\{pages:\{'desk-alerts'/);assert.match(readFileSync('src/features/overview/Overview.tsx','utf8'),/reminders and wake-ups/);});
}
