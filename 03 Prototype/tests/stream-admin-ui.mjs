// ADM-3 lane U · the Administration page (page id `access`): four tabs (Modules & features · Roles · Users · Audit) on desktop and phone,
// Owner-only Roles tab, the role grid, New role dialog, Users role picker with custom roles, merged Audit, search. Names prefixed [ADM3].
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';

const ENTRY=`
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {FitNavContext} from '../../src/components/fit';
import {can,metrics,roomStatus} from '../../src/services/mock';
import {navItems} from '../../src/app/nav';
import {extraPages,mobilePages,extraModals as modals} from '../../src/app/registry';
import {SUB_SCREENS} from '../../src/app/subscreens';
import {buildSearchEntries,searchEntries,PAGE_CATEGORY} from '../../src/app/navmap';
void navItems;
export {SUB_SCREENS,buildSearchEntries,searchEntries,PAGE_CATEGORY,navItems};
export function render(s:any,uid:string,tab:string,mobile=false,modal=''){
  const user=s.users.find((u:any)=>u.id===uid)??s.users[0];const noop=()=>{};
  const ctx:any={s,uid,user,m:metrics(s),page:'access',go:noop,modal:modal?{type:modal,key:'k1'}:null,open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),act:()=>null,search:'',setSearch:noop,filter:'All',setFilter:noop,tab:'',setTab:noop,
    arrivals:[],departures:[],pending:0,taskList:[],roomStatus:(r:any)=>roomStatus(s,r),switchUser:noop,fault:'none',setFault:noop,loadScenario:noop,resetScenario:noop,
    display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop,framed:false},setDirty:noop,openSearch:noop,openMenu:noop,signOut:noop};
  const P=modal?((modals as any)[modal]):mobile?(mobilePages as any).access:(extraPages as any).access;
  const el=<HccContext.Provider value={ctx}><FitNavContext.Provider value={{desktop:!mobile,page:'access',tabKey:tab,seq:1,setTabCrumb:noop}}><P/></FitNavContext.Provider></HccContext.Provider>;
  return renderToStaticMarkup(el);
}
`;
const withTab=(tab,fn)=>{const g=globalThis,had='sessionStorage' in g,prev=g.sessionStorage;
  g.sessionStorage={getItem:k=>k==='hcc-fit-tab:access'?tab:null,setItem(){},removeItem(){}};
  try{return fn();}finally{if(had)g.sessionStorage=prev;else delete g.sessionStorage;}};

export async function register({test,M,assert}){
  await writeFile('tests/.generated/admin-ui-entry.tsx',ENTRY);
  await build({entryPoints:['tests/.generated/admin-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/admin-ui.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"prototype"'},
    banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
  const X=await import(new URL('./.generated/admin-ui.mjs',import.meta.url).href);
  const s=M.rolSeedDemo(M.seed());
  const page=(uid,tab,mobile=false)=>withTab(tab,()=>X.render(s,uid,tab,mobile));
  const grid=M.rolGridModules().flatMap(g=>g.features);

  test('[ADM3] sub-screens: modules, roles, users, audit (equal the useFitTab ids); label Administration; default tab users',()=>{
    assert.deepEqual(X.SUB_SCREENS.filter(x=>x.page==='access').map(x=>x.id),['modules','roles','users','audit']);
    const src=readFileSyncSafe('src/features/admin/AccessControl.tsx');
    assert.ok(src.includes("useFitTab<AccessTab>('access',TAB_IDS,'users')")&&src.includes("const TAB_IDS=['modules','roles','users','audit'] as const"));
    assert.equal(X.navItems.find(n=>n.id==='access').label,'Administration');
    const h=X.render(s,'owner','');assert.match(h,/<h1[^>]*>Administration<\/h1>/);
    for(const id of ['modules','roles','users','audit'])assert.ok(h.includes(`data-tab="${id}"`),id);
    assert.ok(h.includes('data-tab="users" aria-selected="true"'));
  });
  test('[ADM3] Modules & features tab (Owner, Tech admin): the module switches and the sub-function tree from ADM-1',()=>{
    for(const uid of ['owner','techadmin']){const h=page(uid,'modules');
      assert.ok(h.includes('Core modules')&&h.includes('Sub-functions')&&h.includes('Reservations'),uid);}
  });
  test('[ADM3] Roles tab: Owner sees the role list (built-in read-only, custom C01 / C02) and the grid; every non-core feature once',()=>{
    const h=page('owner','roles');
    assert.ok(h.includes('Built-in · read-only')&&h.includes('Day shift manager')&&h.includes('Night shift manager')&&h.includes('New role'));
    assert.ok(h.includes('Access grid for Day shift manager'),'first custom role open in the editor');
    const ids=grid.map(f=>M.featureById(f).feature.label);
    for(const f of grid){const lbl=M.featureById(f).feature.label.replace(/&/g,'&amp;');
      const n=(h.match(new RegExp(`aria-label="${lbl.replace(/[.*+?^${}()|[\]\\]/g,'\\$&')} access"`,'g'))||[]).length;
      assert.equal(n,1,f+' appears once ('+n+')');}
    assert.ok(ids.length>=20);
    assert.ok(h.includes('role="radio"')&&(h.match(/>Off<\/button>/g)||[]).length>=grid.length&&h.includes('>View</button>')&&h.includes('>Full</button>'));
    assert.ok(h.includes('Advanced: permission ids')&&h.includes('Effective access · Day shift manager')&&h.includes('Retire role'));
  });
  test('[ADM3] Roles tab: Day shift manager shows Accounting sub-functions Off; money switch present; module switched off for everyone is labelled',()=>{
    const h=page('owner','roles');
    const seg=lbl=>{const i=h.indexOf(`aria-label="${lbl} access"`);return h.slice(i,h.indexOf('</div>',i));};
    assert.match(seg('Manual vouchers &amp; loans'),/aria-checked="true"[^>]*>Off</,'accounting.vouchers off');
    assert.match(seg('Reservations'),/aria-checked="true"[^>]*>Full</);
    assert.ok(h.includes('Money permissions'));
    const off=M.execute(structuredClone(s),'owner',{type:'SETTING_SET',setting:'module.pos',value:false});
    const ho=withTab('roles',()=>X.render(off,'owner','roles'));
    assert.ok(ho.includes('module switched off for everyone'));
  });
  test('[ADM3] Roles tab: Tech admin and Manager get the Owner-only explanation and no editor',()=>{
    for(const uid of ['techadmin','manager','reception']){const h=page(uid,'roles');
      assert.ok(h.includes('Owner only'),uid);assert.ok(h.includes('edited by the Owner only'));
      assert.ok(!h.includes('role="radio"')&&!h.includes('New role')&&!h.includes('Retire role'),uid+': no editor');}
  });
  test('[ADM3] Roles tab: money switch is locked for a Housekeeper-based role (DEC-061); DEC-217 and SoD text present in Advanced',()=>{
    const t=M.execute(structuredClone(s),'owner',{type:'ROL_CREATE',name:'Floor lead',base:'Housekeeper'});
    const c=t.rolesX.roles.find(r=>r.name==='Floor lead');
    const h=withTab('roles',()=>X.render(t,'owner','roles'));assert.ok(h.includes('Floor lead'));
    // the first non-retired role is open; render the new one through its own page state by removing the demo roles from the list
    const only=structuredClone(t);only.rolesX.roles=only.rolesX.roles.filter(r=>r.id===c.id);
    const h2=withTab('roles',()=>X.render(only,'owner','roles'));
    assert.match(h2,/<input[^>]*disabled[^>]*aria-label="Money permissions for Floor lead"/);assert.ok(h2.includes('locked off: a role based on Housekeeper never holds money permissions (DEC-061)'));
    const src=readFileSyncSafe('src/features/admin/RolesPanel.tsx');
    assert.ok(src.includes('DEC-217: Accounting approve and money permissions stay with the Owner and the Accountant unless you grant them explicitly'));
    assert.ok(src.includes('window.confirm')&&src.includes("type:'ROL_GRANT_SET'")&&src.includes("type:'ROL_FEATURE_SET'")&&src.includes("type:'ROL_RETIRE'")&&src.includes("type:'ROL_UPDATE'"));
  });
  test('[ADM3] New role dialog: base picker lists the built-in roles without Owner; name field; money switch',()=>{
    const h=X.render(s,'owner','',false,'admin-role-new');
    const sel=h.slice(h.indexOf('aria-label="Base role"'),h.indexOf('</select>',h.indexOf('aria-label="Base role"')));
    for(const r of ['Manager','Reception','Cashier','Supervisor','Housekeeper','Accountant','Maintenance','Tech admin','Restaurant'])assert.ok(sel.includes(`value="${r}"`),r);
    assert.ok(!sel.includes('value="Owner"'),'Owner is never a base');
    assert.ok(h.includes('Role name')&&h.includes('Money permissions')&&h.includes('Create role'));
    const tad=X.render(s,'techadmin','',false,'admin-role-new');assert.ok(tad.includes('Only the Owner creates roles.'));
  });
  test('[ADM3] Users tab: People list shows custom role + base; user dialog role picker lists built-ins and custom roles C01 / C02 (money roles Owner only)',()=>{
    const h=page('owner','users');
    assert.ok(h.includes('Day shift manager')&&h.includes('Custom · base Manager')&&h.includes('Effective access')&&h.includes('Exceptions'));
    const m=X.render(s,'owner','',false,'admin-user');
    const sel=m.slice(m.indexOf('aria-label="Role"'),m.indexOf('</select>',m.indexOf('aria-label="Role"')));
    assert.ok(sel.includes('Built-in roles')&&sel.includes('Custom roles')&&sel.includes('value="C01"')&&sel.includes('value="C02"'));
    assert.ok(sel.includes('Day shift manager · base Manager')&&m.includes('Signs in with a PIN'));
    const t=X.render(s,'techadmin','',false,'admin-user');
    assert.ok(/value="C01"[^>]*disabled/.test(t)&&t.includes('money role: Owner only'),'Tech admin cannot assign a money role');
    const eff=withTab('users',()=>X.render(s,'owner','users')); void eff;
  });
  test('[ADM3] Audit tab: policy.history and rolesX.history merged, newest first',()=>{
    const t=structuredClone(s);
    t.policy.history=[{version:5,at:'2026-10-20T10:00:00',actor:'Owner',summary:'OLDER policy change',before:'a',after:'b',reason:'r'}];
    t.rolesX.history=[{version:6,at:'2026-10-25T10:00:00',actor:'Owner',summary:'NEWER role change',before:'c',after:'d',reason:'r2'}];
    const h=withTab('audit',()=>X.render(t,'owner','audit'));
    assert.ok(h.includes('Policy versions')&&h.includes('OLDER policy change')&&h.includes('NEWER role change'));
    assert.ok(h.indexOf('NEWER role change')<h.indexOf('OLDER policy change'),'newest first');
    assert.ok(h.includes('Before → after')||h.includes('Before'));
  });
  test('[ADM3] phone: four tabs as a segmented bar; Roles shows cards (Owner) or the Owner-only note; grid rows are cards with three buttons',()=>{
    const h=X.render(s,'owner','',true);
    assert.ok(h.includes('aria-label="Administration"'));
    for(const l of ['Modules','Roles','Users','Audit'])assert.ok(h.includes('>'+l),l);
    const src=readFileSyncSafe('src/mobile/admin/Access.tsx');
    assert.ok(src.includes("RoleEditor c={c} phone")&&src.includes('ROLES_OWNER_ONLY')&&src.includes('ChipFilter<Tab>'));
    const css=readFileSyncSafe('src/styles/adm-roles.css');
    assert.match(css,/\.m-shell \.adm-seg button\{min-height:48px;font-size:16px\}/);assert.doesNotMatch(css,/#[0-9a-fA-F]{3,8}\b/,'tokens only');
  });
  test('[ADM3] search: "Administration" and "roles" find the page; category kept; keywords cover admin, audit, modules',()=>{
    const entries=X.buildSearchEntries(X.navItems.map(n=>({id:n.id,label:n.label})));
    for(const q of ['administration','roles','custom role','audit','modules','users']){const hit=X.searchEntries(entries,q).find(h=>h.entry.page==='access');assert.ok(hit,q);}
    assert.equal(X.searchEntries(entries,'administration')[0].entry.page,'access');
    assert.ok(X.PAGE_CATEGORY.access);
  });
}
import {readFileSync} from 'node:fs';
function readFileSyncSafe(f){return readFileSync(f,'utf8');}
