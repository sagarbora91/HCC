// CU-ADMIN-UI · phone drawer: Owner and Tech admin get ONE "Admin & Setup" entry (the top shortcut, which opens the Admin & Setup page listing every admin
// page); the old category entry is dropped for them only. Every other role keeps exactly what it had. Names prefixed [CU-ADMIN-UI].
// Same no-DOM-library approach as stream-adm-ux.mjs: render the real NavDrawer / CategoryPage to static markup with a fake context.
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
import {readFileSync} from 'node:fs';

const ENTRY=`
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
import {NavDrawer} from '../../src/app/NavDrawer';
import {CategoryPage} from '../../src/app/ModulesHome';
import {navItems} from '../../src/app/nav';
import {NAV_CATEGORIES,PAGE_CATEGORY,pagesOfCategory,navFilterModules,catPage} from '../../src/app/navmap';
export {navItems,NAV_CATEGORIES,PAGE_CATEGORY,pagesOfCategory,navFilterModules,catPage};
export function render(name:string,s:any,uid:string,page='home'){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);
  const ctx:any={s,uid,user,m:metrics(s),page,go:noop,modal:null,open:noop,closeModal:noop,booking:noop,error:'',busy:false,
    run:async()=>true,toast:noop,permission:(p:any)=>can(s,uid,p),
    display:{isMobile:true,mode:'mobile',setMode:noop},setDirty:noop,openSearch:noop,signOut:noop,taskList:[]};
  return renderToStaticMarkup(<HccContext.Provider value={ctx}>{name==='CategoryPage'?<CategoryPage page={page}/>:<NavDrawer onClose={noop}/>}</HccContext.Provider>);
}`;

export async function register({test,M,assert}){
  await writeFile('tests/.generated/cu-admin-ui-entry.tsx',ENTRY);
  await build({entryPoints:['tests/.generated/cu-admin-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/cu-admin-ui.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"demo"'},loader:{'.css':'empty'},
    banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
  const X=await import(new URL('./.generated/cu-admin-ui.mjs',import.meta.url).href);
  const base=M.seed();
  // Restaurant (POS only) and a custom role (copy of Manager's grants under its own name) join the seeded roles.
  base.users.push({id:'cu-rest',name:'Rina · Restaurant',role:'Restaurant',enabled:true},{id:'cu-duty',name:'Dev · Duty Manager',role:'Duty Manager',enabled:true});
  base.grants.Restaurant=['billing'];base.grants['Duty Manager']=[...base.grants.Manager];
  const esc=t=>t.replace(/&/g,'&amp;');
  const roles=[...new Map(base.users.filter(u=>u.enabled).map(u=>[u.role,u.id])).values()];
  const adminRoles=['Owner','Tech admin'].map(r=>base.users.find(u=>u.role===r&&u.enabled).id);
  const count=(h,t)=>h.split(t).length-1;
  /** Pages the role may open (what the old drawer + Modules Home could reach). */
  const allowedOf=uid=>new Set(X.navFilterModules(base,X.navItems,uid).filter(n=>M.can(base,uid,n.p)).map(n=>n.id));
  /** Pages reachable from the new drawer: every category opened in turn + the Admin & Setup page behind the shortcut. */
  const reachableOf=uid=>{
    const got=new Set();
    for(const c of X.NAV_CATEGORIES){const h=X.render('NavDrawer',base,uid,X.catPage(c.id));for(const m of h.matchAll(/data-page="([^"]+)"/g))if(m[1]!=='admin-setup')got.add(m[1]);}
    if(X.render('NavDrawer',base,uid).includes('data-page="admin-setup"')){
      const h=X.render('CategoryPage',base,uid,X.catPage('admin'));
      for(const m of h.matchAll(/data-page="([^"]+)"/g))got.add(m[1]);
    }
    return got;};

  test('[CU-ADMIN-UI] Owner and Tech admin: exactly one "Admin & Setup" entry in the phone drawer (the top shortcut), no second category',()=>{
    for(const uid of adminRoles){
      const h=X.render('NavDrawer',base,uid);
      assert.equal(count(h,'Admin &amp; Setup'),1,uid+' entries');
      assert.ok(h.includes('data-page="admin-setup"'),uid+' shortcut');
      const here=X.render('NavDrawer',base,uid,'prefs'); // on an admin page the shortcut is highlighted, still one entry
      assert.equal(count(here,'Admin &amp; Setup'),1);assert.ok(/class="drawer-item on"[^>]*data-page="admin-setup"|data-page="admin-setup" class="drawer-item on"/.test(here));
    }
  });
  test('[CU-ADMIN-UI] every Admin & Setup page stays reachable for Owner and Tech admin (shortcut page lists them all)',()=>{
    for(const uid of adminRoles){
      const adminPages=X.navItems.filter(n=>X.PAGE_CATEGORY[n.id]==='admin'&&allowedOf(uid).has(n.id));
      assert.ok(adminPages.length>=5,uid+' admin pages '+adminPages.length);
      const h=X.render('CategoryPage',base,uid,X.catPage('admin'));
      for(const n of adminPages)assert.ok(h.includes('data-page="'+n.id+'"'),uid+' lacks '+n.id);
    }
  });
  test('[CU-ADMIN-UI] no role loses a page: reachable pages equal the pages the role may open',()=>{
    for(const uid of roles){
      const want=allowedOf(uid),got=reachableOf(uid);
      want.delete('overview');
      assert.deepEqual([...got].sort(),[...want].sort(),uid);
    }
  });
  test('[CU-ADMIN-UI] other roles keep exactly their old drawer: no shortcut, Admin & Setup category only where they have an admin-category page',()=>{
    for(const uid of roles.filter(r=>!adminRoles.includes(r))){
      const h=X.render('NavDrawer',base,uid);
      assert.ok(!h.includes('data-page="admin-setup"'),uid+' has no shortcut');
      const has=[...allowedOf(uid)].some(p=>X.PAGE_CATEGORY[p]==='admin');
      assert.equal(count(h,'Admin &amp; Setup'),has?1:0,uid);
    }
  });
  test('[CU-ADMIN-UI] desktop mode on a 360 dp phone: top-bar rules live in v22-fixes.css only, tokens not hex',()=>{
    const css=readFileSync('src/styles/v22-fixes.css','utf8');
    const m=css.match(/\/\* CU-ADMIN-UI:[\s\S]*?\/\* \/CU-ADMIN-UI \*\//);
    assert.ok(m,'marked block');
    assert.ok(/@media\(max-width:480px\)/.test(m[0])&&m[0].includes('.fit-topbar')&&m[0].includes('.fit-demo-pill')&&m[0].includes('.crumbs'));
    assert.ok(!/#[0-9a-fA-F]{3,8}\b/.test(m[0]),'no hex colours');
    // Business date + Open/Closed stay visible; only the clock chip, the words "Business date" and the chip's icon go; text is never shrunk (except the DEMO pill's word).
    const rules=[...m[0].matchAll(/([^{}@]+)\{([^}]*)\}/g)].map(r=>[r[1].trim(),r[2]]);
    const hiddenSel=rules.filter(([,b])=>/display:none/.test(b)).flatMap(([sel])=>sel.split(',').map(x=>x.trim()));
    assert.ok(!hiddenSel.some(x=>/\.today/.test(x)&&!/fit-date-text|>svg/.test(x)),'business date chip is not hidden');
    assert.ok(rules.some(([sel,b])=>/\.today:not\(\.fit-clock\)/.test(sel)&&/display:flex!important/.test(b)),'date chip forced visible');
    assert.ok(hiddenSel.some(x=>/fit-date-text/.test(x)),'only the words "Business date" are dropped');
    assert.ok(!rules.some(([sel,b])=>/font-size/.test(b)&&!/fit-demo-pill/.test(sel)),'no text shrinking');
    assert.ok(!hiddenSel.some(x=>/crumbs li:last-child$/.test(x)),'current crumb stays');
  });
}
