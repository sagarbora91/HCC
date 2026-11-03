// FIT-ADMIN-B (wave W2a FIT, DEC-194…196, lane fit-admin-b): syshealth, prefs, access, backup, roadmap, legacy-import render a fit-page root,
// their useFitTab ids equal the SUB_SCREENS lines of the FIT-ADMIN block, defaults as set, every tab / section renders, no content is lost.
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {readFileSync} from 'node:fs';

const ENTRY=`
import React from 'react';
import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics,roomStatus} from '../../src/services/mock';
import {navItems} from '../../src/app/nav';
import {extraPages} from '../../src/app/registry';
import {SUB_SCREENS} from '../../src/app/subscreens';
void navItems;
export {SUB_SCREENS};
export function render(s:any,uid:string,page:string){
  const user=s.users.find((u:any)=>u.id===uid)??s.users[0];const noop=()=>{};
  const ctx:any={s,uid,user,m:metrics(s),page,go:noop,modal:null,open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),act:()=>null,search:'',setSearch:noop,filter:'All',setFilter:noop,tab:'',setTab:noop,
    arrivals:[],departures:[],pending:0,taskList:[],roomStatus:(r:any)=>roomStatus(s,r),switchUser:noop,fault:'none',setFault:noop,loadScenario:noop,resetScenario:noop,
    display:{isMobile:false,mode:'desktop',setMode:noop,framed:false},setDirty:noop,openSearch:noop,openMenu:noop,signOut:noop};
  const P=(extraPages as any)[page];
  return renderToStaticMarkup(<HccContext.Provider value={ctx}><P/></HccContext.Provider>);
}
`;
const read=f=>readFileSync(f,'utf8');
const PAGES=['syshealth','prefs','access','backup','roadmap','legacy-import'];
const withTab=(page,tab,fn)=>{const g=globalThis,had='sessionStorage' in g,prev=g.sessionStorage;
  g.sessionStorage={getItem:k=>k==='hcc-fit-tab:'+page?tab:null,setItem(){},removeItem(){}};
  try{return fn();}finally{if(had)g.sessionStorage=prev;else delete g.sessionStorage;}};

export async function register({test,M,assert}){
  await mkdir('tests/.generated',{recursive:true});
  await writeFile('tests/.generated/fit-admin-b-entry.tsx',ENTRY);
  await build({entryPoints:['tests/.generated/fit-admin-b-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit-admin-b.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"prototype"'},
    banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
  const X=await import(new URL('./.generated/fit-admin-b.mjs',import.meta.url).href);
  const s=M.seed();
  const sub=page=>X.SUB_SCREENS.filter(x=>x.page===page).map(x=>x.id);

  test('[FIT-ADMIN-B] every page renders one fit-page root with its data-fit-page id and the header row',()=>{
    for(const p of PAGES){const h=X.render(s,'owner',p);
      assert.ok(h.startsWith('<section class="fit-page'),p+': '+h.slice(0,60));
      assert.equal((h.match(/class="fit-page /g)||[]).length,1,p+': one page root');assert.ok(h.includes('fit-page-head'),p);}
  });
  test('[FIT-ADMIN-B] a non-owner still gets the lock note inside a fit-page (system health, legacy import)',()=>{
    for(const p of ['legacy-import','syshealth']){const h=X.render(s,'reception',p);assert.ok(h.startsWith('<section class="fit-page'),p);assert.match(h,/is for the (Owner|Owner and the Tech admin)/,p);}
  });
  test('[FIT-ADMIN-B] sub-screens: access / backup / prefs ids in SUB_SCREENS equal the useFitTab ids; defaults users / history / text',()=>{
    assert.deepEqual(sub('access'),['modules','roles','users','audit']); // ADM-3: Administration
    assert.deepEqual(sub('backup'),['history','restore']);assert.deepEqual(sub('prefs'),['text','appearance','display','navigation','alerts']);
    for(const p of ['syshealth','roadmap','legacy-import'])assert.deepEqual(sub(p),[],p+' has no tabs');
    assert.ok(read('src/features/admin/AccessControl.tsx').includes("useFitTab<AccessTab>('access',TAB_IDS,'users')"));
    assert.ok(read('src/features/housekeeping/Backup.tsx').includes("useFitTab<BackupTab>('backup',['history','restore'],'history')"));
    assert.ok(read('src/features/prefs/PrefsPage.tsx').includes("useFitTab<PrefsSection>('prefs',['text','appearance','display','navigation','alerts'],'text')"));
    assert.ok(X.render(s,'owner','access').includes('data-tab="users" aria-selected="true"'));
    assert.ok(X.render(s,'owner','backup').includes('data-tab="history" aria-selected="true"'));
  });
  test('[FIT-ADMIN-B] access (Administration, ADM-3): the demo build shows four tabs (sign-in only in production, inside Users); each tab renders its panels',()=>{
    const h=X.render(s,'owner','access');assert.ok(!h.includes('Sign-in &amp; devices'));
    const want={modules:['Core modules','Sub-functions'],roles:['New role','Built-in role matrix'],users:['New user','Person'],audit:['Policy versions']};
    for(const [tab,texts] of Object.entries(want)){const t=withTab('access',tab,()=>X.render(s,'owner','access'));
      assert.ok(t.includes('data-tab="'+tab+'" aria-selected="true"'),tab);for(const x of texts)assert.ok(t.includes(x),tab+': '+x);
      assert.ok(t.includes('data-fit-scroll=""'),tab+': a scroll panel');}
  });
  test('[FIT-ADMIN-B] prefs: one section at a time (FormSections), Reset in the sticky bar; backup: history and restore are two tabs; roadmap lists areas 13–26 in one panel',()=>{
    const secs={text:'Text size',appearance:'Reduce motion',display:'Device &amp; display',navigation:'Start page',alerts:'Message duration'};
    for(const [id,txt] of Object.entries(secs)){const t=withTab('prefs',id,()=>X.render(s,'owner','prefs'));assert.ok(t.includes(txt),id);assert.ok(t.includes('Reset this device'),id);}
    const r=withTab('backup','restore',()=>X.render(s,'owner','backup'));assert.ok(r.includes('Restore procedure')&&r.includes('hk-steps'));
    const hst=X.render(s,'owner','backup');assert.ok(hst.includes('Backup history')&&hst.includes('Run simulated backup')&&hst.includes('Simulation'));
    const rm=X.render(s,'owner','roadmap');assert.ok(rm.includes('fit-panel')&&(rm.match(/<tr>/g)||[]).length>=14);
  });
  test('[FIT-ADMIN-B] legacy import: form panel + runs panel; the run form, parts and dry-run toggle are all still there',()=>{
    const h=X.render(s,'owner','legacy-import');
    for(const t of ['Run folder on the server PC','Dry run','Run dry run','Expected totals','Runs'])assert.ok(h.includes(t),t);
    assert.ok(h.includes('adm-lg-cols'));
  });
  test('[FIT-ADMIN-B] CSS: src/styles/fit-admin-b.css (admin / housekeeping / prefs sheets are frozen by [CT-C]) is guarded by .app-shell, adds no font-size and no colour',()=>{
    const css=read('src/styles/fit-admin-b.css').replace(/\/\*[^]*?\*\//g,'');
    assert.doesNotMatch(css,/font-size/,'DEC-195');assert.doesNotMatch(css,/#[0-9a-fA-F]{3,8}|rgba?\(/,'tokens only');
    const sels=css.replace(/@media[^{]*\{/g,'').split('}').map(x=>x.split('{')[0].trim()).filter(Boolean);
    assert.ok(sels.length>15);for(const sel of sels)assert.ok(sel.split(',').every(x=>x.trim().startsWith('.app-shell ')),sel);
  });
}
