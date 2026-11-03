// ADM-1 · Feature catalogue and sub-function switches (DEC-214, DEC-215, DEC-218). Names prefixed [ADM1].
// Catalogue invariants (every nav page in exactly one feature, every command prefix matches a real command, requires resolve),
// the golden JSON shared with the server, switching Reservations off (pages hidden, BOOK refused, walk-in for today still passes),
// dependants, core features never switchable, Tech admin may switch, module switch wins over the feature switch.
import {build} from 'esbuild';
import {readFile,writeFile} from 'node:fs/promises';
await writeFile('tests/.generated/features-nav-entry.ts',"export {navFilterModules,PAGE_CATEGORY} from '../../src/app/navmap';export {navItems} from '../../src/app/nav';export {registry} from '../../src/services/mock/handlers';");
await build({entryPoints:['tests/.generated/features-nav-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/features-nav.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'}});
const N=await import('./.generated/features-nav.mjs');
let golden='';try{golden=await readFile('../08 Production/database/feature-catalogue.json','utf8');}catch{golden='';}

export function register({test,M,assert}){
  const {seed,execute,FEATURE_MODULES,featureAll,featureById,featureOfPage,featureOfCommand,featureClosure,featureCatalogueJson,featureKey,
    cfgFeatureOn,cfgFeatureOffReason,cfgPageOn,cfgModuleOn,cfgDef,cfgCanChange,cfgGroupSummary,cfgImpact,CFG_MODULES}=M;
  const set=(s,uid,setting,value)=>execute(s,uid,{type:'SETTING_SET',setting,value});
  const msg=fn=>{try{fn();return '';}catch(e){return e.message;}};
  const code=fn=>{try{fn();return 'OK';}catch(e){return e.code;}};

  test('[ADM1] catalogue: 18 modules, ids unique, every nav page in exactly one feature, requires resolve, no self-requires',()=>{
    assert.equal(FEATURE_MODULES.length,18);
    const ids=featureAll().map(r=>r.id);assert.equal(new Set(ids).size,ids.length);
    const pageOwners={};for(const r of featureAll())for(const p of r.feature.pages){pageOwners[p]=(pageOwners[p]||0)+1;assert.ok(N.PAGE_CATEGORY[p],`page ${p} of ${r.id} is not in PAGE_CATEGORY`);}
    for(const [p,n] of Object.entries(pageOwners))assert.equal(n,1,`page ${p} owned by ${n} features`);
    const missing=Object.keys(N.PAGE_CATEGORY).filter(p=>!featureOfPage(p));
    assert.deepEqual(missing,[],'nav pages without a feature: '+missing.join(', '));
    for(const r of featureAll())for(const q of r.feature.requires||[]){assert.ok(featureById(q),`${r.id} requires unknown ${q}`);assert.notEqual(q,r.id);}
    for(const m of FEATURE_MODULES)if(m.switch)assert.ok(CFG_MODULES.some(x=>x.id===m.switch),`${m.id} points at unknown module switch ${m.switch}`);
  });
  test('[ADM1] catalogue: every command prefix matches at least one registered command; longest prefix wins; excepts respected',()=>{
    const cmds=Object.keys(N.registry);
    for(const r of featureAll())for(const p of r.feature.prefixes)assert.ok(cmds.some(c=>c.startsWith(p)),`${r.id} prefix ${p} matches no command`);
    assert.equal(featureOfCommand('BOOK').id,'frontdesk.reservations');assert.equal(featureOfCommand('BOOKING_COMPANY').id,'frontdesk.reservations');
    assert.equal(featureOfCommand('EXP_VENDOR_PAY').id,'stock.purchases');assert.equal(featureOfCommand('EXP_EDIT').id,'cash.expenses');
    assert.equal(featureOfCommand('CAT_PLAN_SET').id,'reports.plans');assert.equal(featureOfCommand('CAT_CHARGE').id,'cash.folios');
    assert.equal(featureOfCommand('CHN_WEB_CONFIRM').id,'revenue.website');assert.equal(featureOfCommand('CHN_PAYOUT').id,'revenue.payouts');assert.equal(featureOfCommand('CHN_PULL').id,'revenue.ota');
    assert.equal(featureOfCommand('MSG_DOC_EXTRACT').id,'documents.capture');assert.equal(featureOfCommand('MSG_TEMPLATE').id,'messaging.templates');
    assert.equal(featureOfCommand('PAYMENT').id,'cash.cashier');assert.equal(featureOfCommand('NEXT_DAY').id,'closing.dayclose');
    assert.equal(featureOfCommand('OWN_CREDENTIAL_CHANGE'),undefined);
  });
  test('[ADM1] golden feature-catalogue.json matches the TypeScript catalogue (run node tools/feature-catalogue.mjs --write in 03 Prototype)',()=>{
    assert.ok(golden,'08 Production/database/feature-catalogue.json missing');
    assert.deepEqual(JSON.parse(golden).modules,JSON.parse(JSON.stringify(featureCatalogueJson())));
  });
  test('[ADM1] core features are never switchable: no setting, cfgFeatureOn true, always-on list pinned (DEC-215)',()=>{
    const s=seed();
    const core=featureAll().filter(r=>r.feature.core).map(r=>r.id);
    for(const id of core){assert.equal(cfgDef(s,featureKey(id)),undefined,id);assert.equal(cfgFeatureOn(s,id),true);}
    for(const id of ['frontdesk.checkin','cash.folios','cash.cashier','cash.invoices','closing.dayclose','admin.access','admin.config','admin.backup','admin.health','people.myhr'])assert.ok(core.includes(id),id+' must be core');
    assert.equal(code(()=>set(s,'owner','feature.frontdesk.checkin',false)),'VALIDATION');
  });
  test('[ADM1] Reservations off: pages hidden, BOOK refused with the feature sentence, walk-in for today still books, other pages untouched',()=>{
    let s=seed();
    assert.equal(cfgPageOn(s,'reservations'),true);
    s=set(s,'owner','feature.frontdesk.reservations',false);
    assert.deepEqual(s.configX.featuresOff,['frontdesk.reservations']);
    assert.equal(cfgFeatureOn(s,'frontdesk.reservations'),false);assert.equal(cfgFeatureOffReason(s,'frontdesk.reservations'),'Switched off');
    assert.equal(cfgPageOn(s,'reservations'),false);assert.equal(cfgPageOn(s,'calendar'),false);assert.equal(cfgPageOn(s,'arrivals'),true);assert.equal(cfgPageOn(s,'rooms'),true);
    const ids=N.navFilterModules(s,N.navItems,'owner').map(n=>n.id);
    assert.ok(!ids.includes('reservations')&&!ids.includes('calendar')&&ids.includes('arrivals'));
    const m=msg(()=>execute(s,'owner',{type:'BOOK',guest:'Future',room:'105',arrival:'2026-11-20',departure:'2026-11-22',people:1}));
    assert.match(m,/Reservations \(Front desk\) is switched off in Master configuration › Modules & features/);
    assert.equal(code(()=>execute(s,'owner',{type:'BOOK',guest:'Future',room:'105',arrival:'2026-11-20',departure:'2026-11-22',people:1})),'FORBIDDEN');
    const walk=execute(s,'owner',{type:'BOOK',guest:'Walk In',room:'105',arrival:s.date,departure:'2026-10-26',people:1});
    assert.equal(walk.bookings.at(-1).guest,'Walk In');
    assert.notEqual(code(()=>execute(walk,'owner',{type:'REGISTER',id:walk.bookings.at(-1).id})),'FORBIDDEN','REGISTER is core, never gated');
    s=set(s,'owner','feature.frontdesk.reservations',true);
    assert.deepEqual(s.configX.featuresOff,[]);assert.equal(cfgPageOn(s,'reservations'),true);
  });
  test('[ADM1] dependants: Expenses off takes Purchases down and says so; switching Expenses on does not bring Purchases back by itself',()=>{
    let s=seed();
    assert.deepEqual(featureClosure('cash.expenses'),['cash.expenses','stock.purchases']);
    const def=cfgDef(s,'feature.cash.expenses');
    assert.ok(cfgImpact(s,def,false,s.date).some(l=>l.includes('Purchases & vendor payments')));
    s=set(s,'owner','feature.cash.expenses',false);
    assert.deepEqual([...s.configX.featuresOff].sort(),['cash.expenses','stock.purchases']);
    assert.equal(cfgFeatureOn(s,'stock.purchases'),false);
    s=set(s,'owner','feature.cash.expenses',true);
    assert.deepEqual(s.configX.featuresOff,['stock.purchases']);assert.equal(cfgFeatureOn(s,'cash.expenses'),true);assert.equal(cfgFeatureOn(s,'stock.purchases'),false);
    s=set(s,'owner','feature.stock.purchases',true);assert.deepEqual(s.configX.featuresOff,[]);
  });
  test('[ADM1] a required feature off makes the dependant off with a reason even when the dependant is not in featuresOff',()=>{
    let s=seed();s.configX={featuresOff:['cash.expenses']};
    assert.equal(cfgFeatureOn(s,'stock.purchases'),false);
    assert.match(cfgFeatureOffReason(s,'stock.purchases'),/Needs Expenses \(Cash & billing\), which is off/);
    assert.equal(code(()=>execute(s,'owner',{type:'EXP_VENDOR_PAY'})),'FORBIDDEN');
  });
  test('[ADM1] module switch wins: Restaurant & POS module off makes pos.checks off with the module reason; the module sentence is used for POS_ commands',()=>{
    let s=set(seed(),'owner','module.pos',false);
    assert.equal(cfgFeatureOn(s,'pos.checks'),false);assert.equal(cfgFeatureOffReason(s,'pos.checks'),'Restaurant & POS module is switched off');
    assert.match(msg(()=>execute(s,'owner',{type:'POS_ORDER_OPEN'})),/Restaurant & POS is switched off in Master configuration › Modules on\/off/);
    s=set(s,'owner','module.pos',true);assert.equal(cfgFeatureOn(s,'pos.checks'),true);
  });
  test('[ADM1] who may switch: Owner and Tech admin allow, Manager denied (DEC-218 / DEC-058); summary counts sub-functions',()=>{
    const s=seed();const def=cfgDef(s,'feature.cash.cashbook');
    assert.equal(cfgCanChange(s,'owner',def).mode,'allow');
    const tad=s.users.find(u=>u.role==='Tech admin');if(tad)assert.equal(cfgCanChange(s,tad.id,def).mode,'allow');
    assert.equal(cfgCanChange(s,'manager',def).mode,'deny');
    assert.equal(code(()=>set(s,'manager','feature.cash.cashbook',false)),'FORBIDDEN');
    const sw=featureAll().filter(r=>!r.feature.core).length;
    assert.equal(cfgGroupSummary(s,'modules'),`10 of 10 on · 6 core always on · ${sw} of ${sw} sub-functions on`);
    const off=set(s,'owner','feature.cash.cashbook',false);
    assert.equal(cfgGroupSummary(off,'modules'),`10 of 10 on · 6 core always on · ${sw-1} of ${sw} sub-functions on`);
    assert.ok(off.configX.changes.some(c=>c.key==='feature.cash.cashbook'&&c.status==='applied'),'history row');
  });
  test('[ADM1] old saves without featuresOff load with everything on',()=>{
    const s=seed();delete s.configX;
    assert.equal(cfgFeatureOn(s,'cash.cashbook'),true);assert.equal(cfgPageOn(s,'cashbook'),true);
  });
}
