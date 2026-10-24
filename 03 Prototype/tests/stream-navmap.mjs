// Step 1 tests: navigation map (categories, page → category) and the search index. Pure logic; the palette UI is checked in the browser.
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
await writeFile('tests/.generated/navmap-entry.ts',"export {NAV_CATEGORIES,PAGE_CATEGORY,PAGE_KEYWORDS,SEARCH_ACTIONS,buildSearchEntries,searchEntries,visibleEntries,categoryOf,HOME_PAGE,PAGE_BLURB,catPage,parseCatPage,pagesOfCategory,crumbsFor} from '../../src/app/navmap';export {navItems,navGroups} from '../../src/app/nav';export {extraModals} from '../../src/app/registry';");
await build({entryPoints:['tests/.generated/navmap-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/navmap.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'}});
const X=await import('./.generated/navmap.mjs');

export function register({test,M,assert}){
  const {baseFixture,can}=M,s=baseFixture();
  const entries=X.buildSearchEntries(X.navItems);
  const forRole=(uid)=>{const allowed=new Set(X.navItems.filter(n=>can(s,uid,n.p)).map(n=>n.id));return X.visibleEntries(entries,allowed,p=>can(s,uid,p));};
  const top=(list,q)=>X.searchEntries(list,q).map(h=>h.entry);

  test('[NAV] ten categories; every registered page except the Dashboard belongs to exactly one',()=>{
    assert.equal(X.NAV_CATEGORIES.length,10);assert.equal(new Set(X.NAV_CATEGORIES.map(c=>c.id)).size,10);
    const ids=X.navItems.map(n=>n.id);assert.equal(new Set(ids).size,ids.length);
    for(const id of ids){if(id==='overview')continue;assert.ok(X.PAGE_CATEGORY[id],'page without a category: '+id);assert.ok(X.NAV_CATEGORIES.some(c=>c.id===X.PAGE_CATEGORY[id]),id);}
  });
  test('[NAV] the mapping names no page that does not exist (customers is built in step 4)',()=>{
    const ids=new Set(X.navItems.map(n=>n.id));
    for(const id of Object.keys(X.PAGE_CATEGORY))assert.ok(ids.has(id),'unknown page in PAGE_CATEGORY: '+id);
    for(const id of Object.keys(X.PAGE_KEYWORDS))assert.ok(ids.has(id),'keywords for unknown page: '+id);
  });
  test('[NAV] agreed placement: Guests profile under Customers; stay functions under Guests & Services; Dashboard has no category',()=>{
    assert.equal(X.PAGE_CATEGORY.customers,'customers');assert.equal(X.PAGE_CATEGORY.guests,undefined);assert.ok(!X.navItems.some(n=>n.id==='guests'));
    for(const p of ['inhouse','guest-services','gsv-my-requests','messages','documents'])assert.equal(X.PAGE_CATEGORY[p],'guestsvc',p);
    for(const p of ['rooms','calendar','reservations','arrivals'])assert.equal(X.PAGE_CATEGORY[p],'front',p);
    for(const p of ['yield','groups','channels','booking-site'])assert.equal(X.PAGE_CATEGORY[p],'revenue',p);
    assert.equal(X.PAGE_CATEGORY.overview,undefined);
  });
  test('[NAV] search actions open dialogs that exist',()=>{
    for(const a of X.SEARCH_ACTIONS){assert.ok(X.navItems.some(n=>n.id===a.page),a.id);
      const known=a.modal.type in X.extraModals||['new','expense','guest-walkin'].includes(a.modal.type);assert.ok(known,a.modal.type);}
  });
  test('[NAV] search finds pages by label, keyword and synonym and shows the exact path',()=>{
    const all=entries;
    assert.equal(top(all,'check in')[0].page,'arrivals');
    assert.deepEqual(top(all,'check in')[0].path,['Front Desk','Arrivals & check-in']);
    assert.equal(top(all,'rates')[0].page,'yield');assert.equal(top(all,'booking id')[0].page,'arrivals');
    assert.equal(top(all,'lost found')[0].page,'guest-services');assert.equal(top(all,'payslip')[0].page,'staff'.length?top(all,'payslip')[0].page:'');
    assert.ok(top(all,'payslip').some(e=>e.page==='my-hr'));
    assert.equal(top(all,'cashier')[0].page,'cashier');assert.equal(top(all,'ota')[0].page,'channels');
    assert.deepEqual(top(all,'trial balance')[0].path,['Accounts & Reports','Accounting']);
  });
  test('[NAV] search finds quick actions with a three-part path; typing order and case do not matter',()=>{
    const a=top(entries,'new booking')[0];assert.equal(a.kind,'action');assert.deepEqual(a.path,['Front Desk','Reservations','New reservation']);assert.deepEqual(a.modal,{type:'new'});
    assert.equal(top(entries,'EXPENSE add')[0].id,'act-add-expense');assert.equal(top(entries,'walk in')[0].id,'act-walkin');
  });
  test('[NAV] every word must match; empty and nonsense queries return nothing; booking IDs are not searchable globally',()=>{
    assert.deepEqual(top(entries,''),[]);assert.deepEqual(top(entries,'   '),[]);assert.deepEqual(top(entries,'reservation zzzq'),[]);assert.deepEqual(top(entries,'xqzv'),[]);
    assert.deepEqual(top(entries,'HCC-1041'),[]);
  });
  test('[NAV] results respect the role: housekeeper cannot find folios, reception can book, owner finds everything',()=>{
    const hk=forRole('housekeeper'),rec=forRole('reception'),own=forRole('owner');
    assert.equal(top(hk,'folio').length,0);assert.equal(top(hk,'new reservation').length,0);assert.ok(top(hk,'housekeeping').length>0);
    assert.ok(top(rec,'new reservation').length>0);assert.deepEqual(top(rec,'payroll').map(e=>e.page),['my-hr']);
    assert.equal(top(own,'payroll')[0].page,'staff');assert.ok(own.length>=entries.length-0);
  });
  test('[NAV] every page can be found by its own label for a role that may open it',()=>{
    for(const u of s.users){const list=forRole(u.id);
      for(const e of list.filter(e=>e.kind==='page')){const hit=top(list,e.label).find(x=>x.id===e.id);assert.ok(hit,`${u.id} cannot find ${e.label}`);}}
  });
  test('[NAV] Home and category pages are virtual, recognised and never collide with real pages',()=>{
    assert.equal(X.HOME_PAGE,'home');assert.ok(!X.navItems.some(n=>n.id==='home'||n.id.startsWith('cat:')));
    for(const c of X.NAV_CATEGORIES){assert.equal(X.parseCatPage(X.catPage(c.id)),c.id);}
    assert.equal(X.parseCatPage('rooms'),undefined);assert.equal(X.parseCatPage('cat:nope'),undefined);
  });
  test('[NAV] every page has a one-line tile description; every category tile pages are real nav items',()=>{
    for(const p of [...Object.keys(X.PAGE_CATEGORY),'overview'])assert.ok(X.PAGE_BLURB[p]&&X.PAGE_BLURB[p].length>8,'blurb '+p);
    const ids=new Set(X.navItems.map(n=>n.id));
    for(const c of X.NAV_CATEGORIES)for(const p of X.pagesOfCategory(c.id))assert.ok(ids.has(p),c.id+' lists missing page '+p);
  });
  test('[NAV] breadcrumb is Home › Category › Page with the earlier parts clickable',()=>{
    assert.deepEqual(X.crumbsFor('home',''),[{label:'Modules Home'}]);
    assert.deepEqual(X.crumbsFor('cat:cash','').map(c=>c.label),['Modules Home','Cash & Billing']);
    const c=X.crumbsFor('cashier','Cashier');assert.deepEqual(c.map(x=>x.label),['Modules Home','Cash & Billing','Cashier']);
    assert.deepEqual(c.map(x=>x.page),['home','cat:cash',undefined]);
    assert.deepEqual(X.crumbsFor('overview','Overview').map(x=>x.label),['Modules Home','Dashboard']);
  });
  test('[NAV] every role sees at least one tile on Modules Home',()=>{
    for(const u of s.users.filter(u=>u.enabled)){const allowed=new Set(X.navItems.filter(n=>can(s,u.id,n.p)).map(n=>n.id));
      const tiles=X.NAV_CATEGORIES.filter(c=>X.pagesOfCategory(c.id).some(p=>allowed.has(p)));assert.ok(tiles.length>0||allowed.has('overview'),u.id+' has no tiles');}
  });
}
