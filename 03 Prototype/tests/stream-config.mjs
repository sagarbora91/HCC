// W05-A · Master configuration hub tests. Names prefixed [CFG]. Registry, DEC-058 permissions, effective dates (immediate / from a
// date / next business day), approvals (never self-approve), history + revert, modules on/off (DEC-057), wrappers, export/import, provider API.
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
import {tillClear} from './_till.mjs';
await writeFile('tests/.generated/config-nav-entry.ts',"export {navFilterModules,PAGE_CATEGORY,PAGE_KEYWORDS,buildSearchEntries,searchEntries} from '../../src/app/navmap';export {navItems} from '../../src/app/nav';");
await build({entryPoints:['tests/.generated/config-nav-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/config-nav.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'}});
const N=await import('./.generated/config-nav.mjs');

export function register({test,M,assert}){
  const {seed,execute,drawer,cfgDefs,cfgDef,cfgCanChange,cfgGroupAccess,cfgValueOn,cfgRaw,cfgGroupSummary,cfgSearch,cfgExport,cfgImportPlan,cfgPageOn,cfgModuleOn,
    cfgModuleOfCommand,cfgRevertBlock,cfgScheduled,cfgDecideBlock,cfgDisplay,registerSettingsGroup,unregisterSettingsGroup,adminApprovalInbox,adminDecideBlock,
    CFG_GROUPS,CFG_MODULES,CFG_SECTIONS,yldSlice,aiDirtyHours,gsvSettings,posSlice,hrRatesFor,hrLeavePolicy,cmpSlice}=M;
  const code=fn=>{try{fn();return 'OK';}catch(e){return e.code;}};
  const msg=fn=>{try{fn();return '';}catch(e){return e.message;}};
  const set=(s,uid,setting,value,extra={})=>execute(s,uid,{type:'SETTING_SET',setting,value,...extra});
  const lastApr=s=>s.approvals.at(-1);
  /** Closes the base business day and opens the next (same path as the core end-to-end test). */
  function rollDay(s){
    for(const b of s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival<=s.date)){s=execute(s,'owner',{type:'REGISTER',id:b.id});s=execute(s,'owner',{type:'CHECKIN',id:b.id});}
    for(const e of s.expenses.filter(e=>e.status==='Pending'))s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:e.id,approve:true});
    s=execute(s,'owner',{type:'NIGHT_POST'});s=execute(s,'owner',{type:'SHIFT_CLOSE',counted:drawer(s)});s=tillClear(M,s);
    s=execute(s,'owner',{type:'DAY_CLOSE'});return execute(s,'owner',{type:'NEXT_DAY'});
  }

  // ── Registry and groups ──
  test('[CFG] 12 fixed groups in 3 sections, names pinned (one name: Master configuration)',()=>{
    assert.deepEqual(CFG_GROUPS.map(g=>g.label),['Property and branding','Rooms and rate plans','Front-desk policies','Money and tax','Restaurant and POS',
      'Housekeeping and maintenance','People, payroll and leave','Compliance obligations','Modules on/off','Users, roles and PINs','Messages and documents','Data and system']);
    assert.deepEqual([...CFG_SECTIONS],['Business setup','Operations and people','Software and security']);
    assert.deepEqual(CFG_GROUPS.filter(g=>g.money).map(g=>g.id),['rooms','money','pos','people','compliance']);
    const settings=N.navItems.find(n=>n.id==='settings');assert.equal(settings.label,'Master configuration');assert.equal(N.PAGE_CATEGORY.settings,'admin');
    for(const w of ['settings','admin','masters','setup'])assert.ok(N.PAGE_KEYWORDS.settings.includes(w),w);
    const hits=N.searchEntries(N.buildSearchEntries(N.navItems),'masters');assert.equal(hits[0].entry.page,'settings');});
  test('[CFG] registry: unique keys, every group has settings, legacy BL-027 keys kept, module wrappers are immediate',()=>{
    const s=seed(),defs=cfgDefs(s),keys=defs.map(d=>d.key);
    assert.equal(new Set(keys).size,keys.length);
    for(const g of CFG_GROUPS)assert.ok(defs.some(d=>d.group===g.id),g.id);
    for(const k of ['hotel','checkin','checkout','discountLimit','approvalExpiryHours','inspection','invoicePrefix','taxBps'])assert.ok(keys.includes(k),k);
    for(const d of defs){assert.ok(['immediate','date','nextDay'].includes(d.effectiveMode),d.key);assert.ok(d.affects,d.key);assert.ok(d.category===CFG_GROUPS.find(g=>g.id===d.group).label,d.key);
      if(d.store.kind==='module')assert.equal(d.effectiveMode,'immediate',d.key);
      if(d.illustrative)assert.match(d.help,/Illustrative — CA to confirm for a hotel/,d.key);}
    assert.equal(cfgDef(s,'taxBps').editable,false);assert.equal(cfgDef(s,'taxBps').panel,'tax');
    assert.ok(keys.includes('pos.menu.'+posSlice(s).menu[0].id));});

  // ── Permissions (DEC-058) ──
  test('[CFG] Owner may change every editable setting; Tech admin only non-money ones; other roles read-only',()=>{
    const s=seed(),defs=cfgDefs(s).filter(d=>d.editable);
    for(const d of defs)assert.equal(cfgCanChange(s,'owner',d).mode,'allow',d.key);
    for(const d of defs){const m=cfgCanChange(s,'techadmin',d).mode;
      if(d.money)assert.equal(m,'deny',d.key);else assert.equal(m,d.approver==='none'?'allow':'request',d.key);}
    for(const uid of ['manager','reception','cashier','accountant','supervisor','housekeeper','maint'])for(const d of defs)assert.equal(cfgCanChange(s,uid,d).mode,'deny',uid+' '+d.key);
    assert.match(cfgCanChange(s,'techadmin',cfgDef(s,'discountLimit')).reason,/Owner-only \(DEC-058\)/);
    assert.equal(cfgDef(s,'fd.deposit').money,true);assert.equal(cfgDef(s,'fd.deposit').group,'frontdesk');});
  test('[CFG] read-only groups show the reason (never hidden)',()=>{
    const s=seed();
    for(const g of ['rooms','money','pos','people','compliance'])assert.deepEqual(cfgGroupAccess(s,'techadmin',g),{readOnly:true,reason:'Owner changes this — money, tax, payroll, compliance and approval limits are Owner-only (DEC-058).'});
    for(const g of ['property','frontdesk','hkm','modules','users','messages','data'])assert.equal(cfgGroupAccess(s,'techadmin',g).readOnly,false,g);
    for(const g of CFG_GROUPS)assert.equal(cfgGroupAccess(s,'owner',g.id).readOnly,false,g.id);
    assert.match(cfgGroupAccess(s,'reception','property').reason,/Owner changes this/);});
  test('[CFG] commands enforce DEC-058: Tech admin money → FORBIDDEN, non-money → saved; no admin grant → FORBIDDEN',()=>{
    let s=seed();
    for(const [k,v] of [['discountLimit',60000],['approvalExpiryHours',12],['invoicePrefix','HCC-25'],['fd.deposit',200000],['yld.floorBps',-1000],['hr.pt.ptStd',25000],['cmp.dueSoonDays',5],['hr.leave.cap.default',3],['pos.menu.'+posSlice(s).menu[0].id,false]])
      assert.equal(code(()=>set(s,'techadmin',k,v)),'FORBIDDEN',k);
    s=set(s,'techadmin','hotel','HCC Demo Residency West');assert.equal(s.settings.hotel,'HCC Demo Residency West');
    s=set(s,'techadmin','brand.printFormat','A5');assert.equal(cfgValueOn(s,'brand.printFormat'),'A5');
    assert.equal(code(()=>set(s,'manager','hotel','X')),'FORBIDDEN');assert.equal(code(()=>set(s,'reception','checkin','13:00')),'FORBIDDEN');
    assert.equal(code(()=>set(s,'owner','taxBps',1200)),'VALIDATION');assert.equal(code(()=>set(s,'owner','rooms',3)),'VALIDATION');assert.equal(code(()=>set(s,'owner','nope',1)),'VALIDATION');});
  test('[CFG] validation messages are plain and per field',()=>{
    const s=seed();
    assert.equal(msg(()=>set(s,'owner','checkin','25:00')),'Enter a time as HH:mm.');
    assert.equal(msg(()=>set(s,'owner','invoicePrefix','inv')),'Use 2–8 capital letters, digits or hyphens.');
    assert.match(msg(()=>set(s,'owner','fd.holdHours',0)),/whole number between 1 and 72 h/);
    assert.match(msg(()=>set(s,'owner','yld.ceilBps',20000)),/percentage between 0 and 100%/);
    assert.match(msg(()=>set(s,'owner','discountLimit',-1)),/Enter an amount from/);
    assert.match(msg(()=>set(s,'owner','brand.gstin','12345')),/15 capital letters/);
    assert.equal(code(()=>set(set(s,'owner','brand.gstin','00DEMO0000X0Z00'),'owner','brand.gstin','')),'OK');
    assert.equal(msg(()=>set(s,'owner','hotel',s.settings.hotel)),'No change: the setting already has this value.');});

  // ── Approvals: never self-approve ──
  test('[CFG] Tech admin change with an Owner approver becomes a request; requester cannot decide; Owner approves',()=>{
    let s=seed();
    s=set(s,'techadmin','sec.pinLock',true,{reason:'Shared tablet at reception'});
    const a=lastApr(s);assert.equal(a.kind,'config');assert.equal(a.status,'Pending');assert.equal(a.meta.command,'CFG_DECIDE');assert.equal(cfgValueOn(s,'sec.pinLock'),false);
    assert.match(a.reason,/Master configuration · PIN lock on shared devices · Off → On/);
    assert.equal(code(()=>set(s,'techadmin','sec.pinLock',true)),'INVALID_TRANSITION');
    assert.match(msg(()=>execute(s,'techadmin',{type:'CFG_DECIDE',id:a.id,approve:true})),/You raised this request/);
    assert.match(msg(()=>execute(s,'manager',{type:'CFG_DECIDE',id:a.id,approve:true})),/Only the Owner/);
    const row=adminApprovalInbox(s).find(r=>r.a.id===a.id);assert.equal(row.command,'CFG_DECIDE');
    assert.match(adminDecideBlock(s,'techadmin',row),/raised this request/);assert.match(adminDecideBlock(s,'manager',row),/Only the Owner/);assert.equal(adminDecideBlock(s,'owner',row),undefined);
    s=execute(s,'owner',{type:'CFG_DECIDE',id:a.id,approve:true});
    assert.equal(cfgValueOn(s,'sec.pinLock'),true);assert.equal(s.approvals.find(x=>x.id===a.id).status,'Executed');
    const h=s.configHistory[0];assert.equal(h.key,'sec.pinLock');assert.match(h.reason,/Approved request .* from Lata/);});
  test('[CFG] Manager-approver request: Manager decides (no admin grant needed); rejection keeps the value; stale and expired refused',()=>{
    let s=seed();
    s=set(s,'techadmin','inspection',false);const a=lastApr(s);assert.equal(a.meta.approver,'Manager');assert.equal(s.settings.inspection,true);
    assert.equal(cfgDecideBlock(s,'manager',a.id),undefined);assert.equal(cfgDecideBlock(s,'reception',a.id),'Only a Manager or the Owner can decide this request.');
    const ok=execute(s,'manager',{type:'CFG_DECIDE',id:a.id,approve:true});assert.equal(ok.settings.inspection,false);
    const no=execute(s,'manager',{type:'CFG_DECIDE',id:a.id,approve:false,reason:'Keep inspections'});assert.equal(no.settings.inspection,true);assert.equal(no.approvals.find(x=>x.id===a.id).status,'Rejected');
    const changed=set(s,'owner','inspection',false);
    assert.equal(code(()=>execute(changed,'manager',{type:'CFG_DECIDE',id:a.id,approve:true})),'STALE_VERSION');
    const later={...s,clock:'2026-10-25T23:00'};assert.equal(code(()=>execute(later,'manager',{type:'CFG_DECIDE',id:a.id,approve:true})),'INVALID_TRANSITION');});

  // ── Effective dates ──
  test('[CFG] immediate settings cannot be dated; date settings accept today or a later business date only',()=>{
    let s=seed();
    assert.match(msg(()=>set(s,'owner','hotel','New Name',{effectiveFrom:'2026-10-30'})),/takes effect immediately/);
    assert.match(msg(()=>set(s,'owner','discountLimit',60000,{effectiveFrom:'2026-10-20'})),/cannot be before the current business date/);
    assert.match(msg(()=>set(s,'owner','discountLimit',60000,{effectiveFrom:'2028-01-01'})),/at most one year/);
    const today=set(s,'owner','discountLimit',60000,{effectiveFrom:s.date});assert.equal(today.settings.discountLimit,60000);
    s=set(s,'owner','discountLimit',70000,{effectiveFrom:'2026-10-27'});
    assert.equal(s.settings.discountLimit,seed().settings.discountLimit,'live value unchanged before its date');
    assert.equal(cfgValueOn(s,'discountLimit','2026-10-26'),seed().settings.discountLimit);assert.equal(cfgValueOn(s,'discountLimit','2026-10-27'),70000);
    assert.equal(cfgScheduled(s,'discountLimit').length,1);assert.equal(s.configHistory[0].effectiveFrom,'2026-10-27');
    s=set(s,'owner','discountLimit',75000,{effectiveFrom:'2026-10-27'});assert.equal(cfgScheduled(s,'discountLimit').length,1,'same date replaces');assert.equal(cfgValueOn(s,'discountLimit','2026-10-30'),75000);});
  test('[CFG] next-business-day settings are scheduled for tomorrow and come into force when the next day opens',()=>{
    let s=seed();
    assert.match(msg(()=>set(s,'owner','fd.noShowTime','22:00',{effectiveFrom:'2026-10-30'})),/next business day \(2026-10-25\)/);
    s=set(s,'owner','fd.noShowTime','22:00');s=set(s,'techadmin','fd.holdHours',4);
    assert.equal(cfgRaw(s,cfgDef(s,'fd.noShowTime')),'23:00');assert.equal(cfgValueOn(s,'fd.noShowTime'),'23:00');assert.equal(cfgValueOn(s,'fd.noShowTime','2026-10-25'),'22:00');
    s=set(s,'owner','discountLimit',65000,{effectiveFrom:'2026-10-25'});
    s=rollDay(s);
    assert.equal(s.date,'2026-10-25');assert.equal(s.configX.values['fd.noShowTime'],'22:00');assert.equal(s.configX.values['fd.holdHours'],4);
    assert.equal(s.settings.discountLimit,65000,'dated legacy value applied to s.settings on its date');assert.equal(cfgScheduled(s).length,0);
    assert.ok(s.configX.changes.filter(c=>['fd.noShowTime','fd.holdHours','discountLimit'].includes(c.key)).every(c=>c.status==='applied'));});
  test('[CFG] a scheduled change can be withdrawn before its date (history kept); applied ones cannot',()=>{
    let s=set(seed(),'owner','fd.deposit',150000);const id=cfgScheduled(s,'fd.deposit')[0].id;
    assert.equal(code(()=>execute(s,'techadmin',{type:'CFG_WITHDRAW',id})),'FORBIDDEN');
    s=execute(s,'owner',{type:'CFG_WITHDRAW',id});assert.equal(cfgScheduled(s).length,0);assert.equal(cfgValueOn(s,'fd.deposit','2026-10-25'),100000);
    assert.equal(s.configHistory[0].after,'withdrawn');assert.equal(s.configX.changes.find(c=>c.id===id).status,'withdrawn');
    assert.equal(code(()=>execute(s,'owner',{type:'CFG_WITHDRAW',id})),'NOT_FOUND');});
  test('[CFG] dated money change never alters posted records',()=>{
    let s=seed();const before=JSON.stringify(s.bookings);
    s=set(s,'owner','discountLimit',10000,{effectiveFrom:'2026-10-26'});s=set(s,'owner','fd.deposit',300000);
    assert.equal(JSON.stringify(s.bookings),before);});

  // ── History and revert ──
  test('[CFG] history + revert to previous value (latest change only, same permission rules)',()=>{
    let s=seed();const orig=s.settings.hotel;
    s=set(s,'owner','hotel','Name One');s=set(s,'owner','hotel','Name Two');
    const [h2,h1]=s.configHistory.filter(h=>h.key==='hotel');
    assert.equal(h2.before,'Name One');assert.equal(h2.after,'Name Two');
    assert.equal(cfgRevertBlock(s,h1.id),'A newer change exists for this setting.');
    s=execute(s,'owner',{type:'CFG_REVERT',id:h2.id});assert.equal(s.settings.hotel,'Name One');assert.equal(s.configX.changes[0].revertOf,h2.id);
    s=execute(s,'owner',{type:'CFG_REVERT',id:s.configHistory[0].id});assert.equal(s.settings.hotel,'Name Two');
    s=set(s,'owner','discountLimit',60000);const hd=s.configHistory[0];
    assert.equal(code(()=>execute(s,'techadmin',{type:'CFG_REVERT',id:hd.id})),'FORBIDDEN');
    s=execute(s,'owner',{type:'CFG_REVERT',id:hd.id});assert.equal(s.settings.discountLimit,seed().settings.discountLimit);
    assert.notEqual(orig,'Name Two');});
  test('[CFG] revert blocks: legacy entries without raw values, scheduled changes, values changed elsewhere',()=>{
    let s=execute(seed(),'owner',{type:'TAX_SCHEDULE',bps:1200,effectiveFrom:'2026-10-26'});
    assert.match(cfgRevertBlock(s,s.configHistory[0].id),/before the configuration hub kept values/);
    s=set(s,'owner','fd.deposit',120000);assert.match(cfgRevertBlock(s,s.configHistory[0].id),/withdraw it instead/);
    s=set(s,'owner','yld.ceilBps',3000);const hid=s.configHistory[0].id;
    s=execute(s,'owner',{type:'YLD_GUARD_SET',...yldSlice(s).guard,ceilBps:3500});
    assert.match(cfgRevertBlock(s,hid),/changed elsewhere/);assert.equal(code(()=>execute(s,'owner',{type:'CFG_REVERT',id:hid})),'INVALID_TRANSITION');});

  // ── Thin wrappers over module settings ──
  test('[CFG] wrappers save through the module command; old module commands keep working',()=>{
    let s=seed();
    s=set(s,'owner','yld.floorBps',-1500);assert.equal(yldSlice(s).guard.floorBps,-1500);assert.equal(yldSlice(s).history[0].area,'Guardrails');
    s=set(s,'owner','yld.roundTo',10000);assert.equal(yldSlice(s).guard.roundTo,10000);
    assert.equal(code(()=>set(s,'owner','yld.roundTo',777)),'VALIDATION');
    s=set(s,'techadmin','ai.dirtyHours',6);assert.equal(aiDirtyHours(s),6,'Tech admin may change housekeeping settings (DEC-058) without the approve grant');
    s=set(s,'techadmin','gsv.sla.Taxi',45);assert.equal(gsvSettings(s).sla.Taxi,45);
    assert.equal(code(()=>set(s,'owner','gsv.valuableRetentionDays',40)),'VALIDATION','module rule: valuables ≥ normal retention');
    const item=posSlice(s).menu[0];s=set(s,'owner','pos.menu.'+item.id,!item.available);assert.equal(posSlice(s).menu[0].available,!item.available);
    const ym=s.date.slice(0,7);
    s=set(s,'owner','hr.pf.pfEeBps',1000);assert.equal(hrRatesFor(s,ym).pfEeBps,1000);assert.equal(hrRatesFor(s,ym).esicEeBps,hrRatesFor(seed(),ym).esicEeBps);
    assert.equal(cfgValueOn(s,'hr.pf.pfEeBps'),1000);assert.equal(code(()=>set(s,'owner','hr.ot.otBps',5000)),'VALIDATION');
    s=set(s,'owner','hr.leave.ent.CL',10);assert.equal(hrLeavePolicy(s).ent.CL,10);assert.equal(hrLeavePolicy(s).ent.SL,hrLeavePolicy(seed()).ent.SL);
    s=set(s,'owner','cmp.dueSoonDays',10);assert.equal(cmpSlice(s).settings.dueSoonDays,10);assert.equal(code(()=>set(s,'owner','cmp.caEmail','nope')),'VALIDATION');
    s=execute(s,'owner',{type:'AI_SETTINGS',dirtyHours:8});assert.equal(cfgValueOn(s,'ai.dirtyHours'),8);
    s=execute(s,'manager',{type:'GSV_SETTINGS',sla:{Taxi:30}});assert.equal(cfgValueOn(s,'gsv.sla.Taxi'),30);});

  // ── Modules on/off (DEC-057) ──
  test('[CFG] module switch-off: pages hidden, commands refused, data kept; core never switchable; back on works',()=>{
    let s=seed();const pos=JSON.stringify(s.posX);
    assert.equal(cfgModuleOfCommand('POS_ORDER_OPEN').id,'pos');assert.equal(cfgModuleOfCommand('CHN_WEB_REQUEST').id,'website');assert.equal(cfgModuleOfCommand('CHN_PULL').id,'ota');
    assert.equal(cfgModuleOfCommand('MSG_DOC_EXTRACT').id,'documents');assert.equal(cfgModuleOfCommand('MSG_SEND').id,'messaging');assert.equal(cfgModuleOfCommand('HR_PAYROLL_RUN').id,'payroll');
    for(const t of ['BOOK','CHECKIN','PAYMENT','DAY_CLOSE','SETTING_SET','HR_ATT_MARK','HR_LEAVE_REQUEST','CB_LOCK','GSV_REQUEST_CREATE'])assert.equal(cfgModuleOfCommand(t),undefined,t);
    s=set(s,'techadmin','module.pos',false);
    assert.equal(cfgModuleOn(s,'pos'),false);assert.equal(cfgPageOn(s,'pos'),false);assert.equal(cfgPageOn(s,'folios'),true);
    assert.equal(JSON.stringify(s.posX),pos,'data kept');
    const outlet=posSlice(s).outlets[0].id;
    assert.match(msg(()=>execute(s,'owner',{type:'POS_ORDER_OPEN',outlet,table:'T9',covers:2})),/Restaurant & POS is switched off in Master configuration/);
    assert.equal(code(()=>execute(s,'owner',{type:'POS_ORDER_OPEN',outlet,table:'T9',covers:2})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'owner',{type:'NIGHT_POST'})),'OK');
    assert.equal(cfgGroupSummary(s,'modules'),'9 of 10 on · 6 core always on');
    const ids=N.navFilterModules(s,N.navItems).map(n=>n.id);assert.ok(!ids.includes('pos'));assert.ok(ids.includes('folios')&&ids.includes('settings'));
    s=set(s,'owner','module.pos',true);assert.equal(cfgModuleOn(s,'pos'),true);assert.equal(cfgGroupSummary(s,'modules'),'10 of 10 on · 6 core always on');
    assert.ok(N.navFilterModules(s,N.navItems).some(n=>n.id==='pos'));
    assert.deepEqual(CFG_MODULES.map(m=>m.id),['pos','groups','ota','website','messaging','documents','assistant','inventory','maintenance','payroll']);
    for(const m of CFG_MODULES)for(const p of m.pages)assert.ok(N.PAGE_CATEGORY[p],p);});
  test('[CFG] payroll off refuses pay-run commands only; attendance stays on',()=>{
    let s=set(seed(),'owner','module.payroll',false);
    assert.equal(code(()=>execute(s,'owner',{type:'HR_PAYROLL_RUN',month:'2026-09'})),'FORBIDDEN');
    assert.equal(code(()=>execute(s,'owner',{type:'HR_RATES_SET',otBps:12000})),'FORBIDDEN');
    assert.match(msg(()=>execute(s,'owner',{type:'HR_PAYROLL_RUN',month:'2026-09'})),/^Payroll is switched off/);
    assert.doesNotMatch(msg(()=>execute(s,'owner',{type:'HR_ATT_MARK'})),/switched off/);});

  // ── Summaries, search, display ──
  test('[CFG] live summaries and search',()=>{
    let s=seed();
    assert.match(cfgGroupSummary(s,'property'),/^\d+ settings$/);
    s=set(s,'techadmin','sec.pinLock',true);assert.match(cfgGroupSummary(s,'users'),/1 change pending/);
    s=set(s,'owner','fd.noShowTime','21:00');assert.match(cfgGroupSummary(s,'frontdesk'),/1 change pending/);
    const g=cfgSearch(s,'gstin');assert.equal(g.length,1);assert.equal(g[0].group.id,'property');assert.deepEqual(g[0].defs.map(d=>d.key),['brand.gstin']);
    assert.ok(cfgSearch(s,'pf').some(h=>h.group.id==='people'));assert.ok(cfgSearch(s,'taxi sla').some(h=>h.defs.some(d=>d.key==='gsv.sla.Taxi')));
    assert.equal(cfgSearch(s,'zzzz').length,0);assert.equal(cfgSearch(s,'').length,12);
    s=set(s,'owner','msg.gatewayKey','sk-demo-123');
    assert.equal(cfgDisplay(s,'owner',cfgDef(s,'msg.gatewayKey')),'Set (hidden)');assert.equal(cfgDisplay(s,'techadmin',cfgDef(s,'msg.gatewayKey')),'Set (hidden)');
    assert.ok(!JSON.stringify(s.configHistory[0]).includes('sk-demo-123'),'secret never in history text');});

  // ── Export / import ──
  test('[CFG] export is JSON without secrets; import into another property applies; round trip is a no-op',()=>{
    let s=set(seed(),'owner','msg.gatewayKey','sk-demo-123');s=set(s,'owner','brand.printFormat','A5');s=set(s,'owner','discountLimit',55000);s=set(s,'owner','module.groups',false);
    const x=cfgExport(s);const text=JSON.stringify(x);
    assert.equal(x.format,'hcc-master-configuration');assert.ok(!text.includes('sk-demo-123'));assert.ok(x.excluded.includes('msg.gatewayKey'));assert.ok(!('msg.gatewayKey' in x.values));
    assert.equal(x.values['brand.printFormat'],'A5');assert.equal(x.values.discountLimit,55000);assert.equal(x.values['module.groups'],false);assert.ok(!('taxBps' in x.values)&&!('rooms' in x.values));
    assert.ok(!text.includes('"users"')&&!text.includes('"policy"'));
    let t=execute(seed(),'owner',{type:'CFG_IMPORT',config:JSON.parse(text)});
    assert.equal(cfgValueOn(t,'brand.printFormat'),'A5');assert.equal(t.settings.discountLimit,55000);assert.equal(cfgModuleOn(t,'groups'),false);
    assert.equal(code(()=>execute(t,'owner',{type:'CFG_IMPORT',config:JSON.parse(text)})),'VALIDATION');
    assert.match(t.audit[0].detail,/Configuration imported · 3 changed/);});
  test('[CFG] import: Tech admin skips money settings with the reason; bad files and invalid values are refused whole',()=>{
    const s=seed();const x=cfgExport(s);x.values.discountLimit=1;x.values['brand.printFormat']='A5';x.values['msg.gatewayKey']='leak';x.values['unknown.key']=1;
    const plan=cfgImportPlan(s,'techadmin',x);
    assert.deepEqual(plan.apply.map(p=>p.def.key),['brand.printFormat']);
    assert.match(plan.skipped.find(k=>k.key==='discountLimit').reason,/Owner-only/);assert.equal(plan.skipped.find(k=>k.key==='msg.gatewayKey').reason,'Secrets are never imported');
    assert.equal(plan.skipped.find(k=>k.key==='unknown.key').reason,'Not in this registry');
    const t=execute(s,'techadmin',{type:'CFG_IMPORT',config:x});assert.equal(t.settings.discountLimit,s.settings.discountLimit);assert.equal(cfgValueOn(t,'brand.printFormat'),'A5');
    assert.equal(code(()=>execute(s,'owner',{type:'CFG_IMPORT',config:{format:'other'}})),'VALIDATION');
    const bad=cfgExport(s);bad.values.checkin='9am';bad.values['brand.printFormat']='A5';
    assert.match(msg(()=>execute(s,'owner',{type:'CFG_IMPORT',config:bad})),/Standard check-in time: Enter a time as HH:mm/);
    assert.equal(code(()=>execute(s,'reception',{type:'CFG_IMPORT',config:x})),'FORBIDDEN');});

  // ── Provider API ──
  test('[CFG] registerSettingsGroup: a module contributes dated settings with one call; unregister removes them',()=>{
    registerSettingsGroup({id:'test-statutory',group:'people',defs:()=>[{key:'test.pfCeiling',label:'PF wage ceiling',type:'money',default:1500000,dated:true,illustrative:true,help:'Illustrative — CA to confirm for a hotel.'},
      {key:'test.weeklyOff',label:'Weekly off',type:'choice',options:['Sunday','Monday'],default:'Sunday',money:false}]});
    try{
      let s=seed();const d=cfgDef(s,'test.pfCeiling');
      assert.equal(d.group,'people');assert.equal(d.effectiveMode,'date');assert.equal(d.money,true);assert.equal(d.source,'test-statutory');
      assert.equal(cfgCanChange(s,'techadmin',d).mode,'deny');assert.equal(cfgCanChange(s,'techadmin',cfgDef(s,'test.weeklyOff')).mode,'allow');
      s=set(s,'owner','test.pfCeiling',1800000,{effectiveFrom:'2026-11-01'});
      assert.equal(cfgValueOn(s,'test.pfCeiling','2026-10-31'),1500000);assert.equal(cfgValueOn(s,'test.pfCeiling','2026-11-01'),1800000);
      assert.ok(cfgSearch(s,'wage ceiling').some(h=>h.group.id==='people'));
      assert.throws(()=>registerSettingsGroup({id:'x',group:'nope',defs:[]}),/Unknown configuration group/);
    }finally{unregisterSettingsGroup('test-statutory');}
    assert.equal(cfgDef(seed(),'test.pfCeiling'),undefined);});
  test('[CFG] W05-B / W05-C settings are wired through the provider API',()=>{
    const s=seed(),defs=cfgDefs(s);
    const hr=defs.filter(d=>d.source==='hr-statutory'),cmp=defs.filter(d=>d.source==='compliance');
    assert.ok(hr.length>=20);assert.ok(hr.every(d=>d.group==='people'&&d.money));
    for(const k of ['hr.pf.pfEeBps','hr.pf.pfErBps','hr.pf.pfWageCap','hr.esic.esicEeBps','hr.pt.ptStd','hr.ot.otBps','hr.late.lateBlock','hr.rounding.rounding','hr.leave.ent.EL','hr.leave.cap.default','hr.leave.half'])assert.ok(hr.some(d=>d.key===k),k);
    assert.ok(hr.filter(d=>d.key.startsWith('hr.pf.')).every(d=>d.illustrative&&/Illustrative — CA to confirm for a hotel/.test(d.help)));
    assert.equal(cfgDef(s,'hr.leave.half').editable,false);
    for(const k of ['cmp.caName','cmp.caEmail','cmp.dueSoonDays'])assert.ok(cmp.some(d=>d.key===k&&d.editable),k);
    assert.ok(cmp.some(d=>d.key.startsWith('cmp.rule.')&&!d.editable));
    assert.equal(cfgCanChange(s,'techadmin',cfgDef(s,'cmp.caName')).mode,'deny');assert.equal(cfgCanChange(s,'techadmin',cfgDef(s,'hr.pf.pfEeBps')).mode,'deny');
    assert.equal(new Set(defs.map(d=>d.key)).size,defs.length);});
  test('[CFG] idempotency: a repeated SETTING_SET key returns the earlier state',()=>{
    const s=set(seed(),'owner','hotel','Once',{key:'cfg-once'});assert.deepEqual(set(s,'owner','hotel','Twice',{key:'cfg-once'}),s);});
}
