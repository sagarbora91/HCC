// FIT · static rules of contract §8 (02 Planning/agents/FIT-LAYOUT-CONTRACT.md). Names prefixed [FIT]. No browser.
//  1. Every desktop page component renders a root with class `fit-page` + data-fit-page=<page id>. A page whose markup has no `fit-page`
//     yet is SKIPPED with a message (the wave is in progress); FIT_ENFORCE_ALL=true (the integrator's last step) makes every page mandatory.
//  2. Every useFitTab / FitTabs page id + tab id used in src/ is in src/app/subscreens.ts SUB_SCREENS, and the other way round.
//  3. z-fit.css (when it exists) is in the bundle after z-touch.css and before zz-print.css.
//  4. No file under the phone paths of contract §1 changed since the wave base (git diff; skipped with a message without git).
import {build} from 'esbuild';
import {writeFile,readFile,readdir} from 'node:fs/promises';
import {existsSync,readFileSync,readdirSync,statSync} from 'node:fs';
import {execFileSync} from 'node:child_process';
import {pathToFileURL} from 'node:url';
/** Integrator (2 Nov 2026): every page is converted, so every page is mandatory; FIT_ENFORCE_ALL=false brings back the in-progress skips. */
const ENFORCE=process.env.FIT_ENFORCE_ALL!=='false';
/** The pre-lane head of claude/fit (main + ACC-3 merged + fit-shell + fit-check, before the phase-B lanes). Override with FIT_BASE. */
const FIT_BASE=process.env.FIT_BASE||'3dac5afb';
/** Pages whose tab ids are generated at run time: only these ids are listed in SUB_SCREENS (contract §6). */
const GENERATED_TABS={rooms:['all']};
const walk=(d,o=[])=>{for(const f of readdirSync(d)){const p=d+'/'+f;if(statSync(p).isDirectory())walk(p,o);else if(/\.tsx?$/.test(p))o.push(p);}return o;};

/** Pure: page ids → tab ids used by useFitTab(...) / <FitTabs page=... /> calls in the given {file:text} sources. */
export function usedFitTabs(sources){
  const used={};const add=(page,ids)=>{(used[page]??=new Set());for(const i of ids)used[page].add(i);};
  for(const [file,src] of Object.entries(sources)){
    const arr=txt=>[...txt.matchAll(/'([^']+)'|"([^"]+)"/g)].map(m=>m[1]??m[2]);
    const resolve=name=>{const m=src.match(new RegExp('(?:const|let)\\s+'+name+'\\s*(?::[^=]+)?=\\s*(\\[[^\\]]*\\])'));return m?arr(m[1]):null;};
    for(const m of src.matchAll(/useFitTab(?:<[^>]*>)?\(\s*['"]([^'"]+)['"]\s*,\s*(\[[^\]]*\]|[A-Za-z_$][\w$]*)/g)){
      const ids=m[2].startsWith('[')?arr(m[2]):resolve(m[2]);add(m[1],ids||[]);
    }
    for(const m of src.matchAll(/<FitTabs[^>]*\bpage=(?:\{)?['"]([^'"]+)['"]/g))add(m[1],[]);
  }
  return used;
}
/** Pure: SUB_SCREENS entries from the text of subscreens.ts. */
export function subScreens(text){return [...text.matchAll(/^\s*\{\s*page:\s*'([^']+)'\s*,\s*id:\s*'([^']+)'/gm)].map(m=>({page:m[1],id:m[2]}));}
/** Pure: compare. Returns problems (strings). */
export function compareTabs(used,subs,generated=GENERATED_TABS){
  const problems=[];
  for(const [page,ids] of Object.entries(used)){
    for(const id of ids)if(!subs.some(s=>s.page===page&&s.id===id)&&!(generated[page]&&!generated[page].includes(id)))problems.push(`useFitTab ${page}/${id} missing from SUB_SCREENS`);
    if(!ids.size&&!subs.some(s=>s.page===page))problems.push(`FitTabs page ${page} has no SUB_SCREENS entry`);
  }
  for(const s of subs){
    if(!used[s.page])problems.push(`SUB_SCREENS ${s.page}/${s.id}: no useFitTab('${s.page}',…) in src/`);
    else if(used[s.page].size&&!used[s.page].has(s.id)&&!(generated[s.page]))problems.push(`SUB_SCREENS ${s.page}/${s.id} not among that page's useFitTab ids`);
  }
  return problems;
}

export async function register({test,M,assert}){
  // ---- 2. registry ↔ useFitTab (pure parts first, so the rules themselves are tested) ----
  test('[FIT] tab parser: useFitTab ids, const arrays, FitTabs page',()=>{
    const u=usedFitTabs({a:`const TABS=['x','y'] as const;const [t]=useFitTab('pg',TABS,'x');`,b:`useFitTab<Tab>("q",['p','r'],'p');<FitTabs page="q" tabs={[]}/>`});
    assert.deepEqual([...u.pg].sort(),['x','y']);assert.deepEqual([...u.q].sort(),['p','r']);
  });
  test('[FIT] tab comparer flags a missing and a stale registry entry, and honours generated tabs',()=>{
    const subs=[{page:'pg',id:'x'},{page:'old',id:'z'}];
    const p=compareTabs({pg:new Set(['x','y'])},subs);
    assert.ok(p.some(x=>/pg\/y missing/.test(x)));assert.ok(p.some(x=>/old\/z/.test(x)));
    assert.deepEqual(compareTabs({rooms:new Set(['all','f1','f2'])},[{page:'rooms',id:'all'}]),[]);
  });
  test('[FIT] every useFitTab page and tab id is in SUB_SCREENS and vice versa',()=>{
    const p='src/app/subscreens.ts';
    if(!existsSync(p)){console.log('SKIP [FIT] src/app/subscreens.ts not created yet (lane fit-shell)');return;}
    const subs=subScreens(readFileSync(p,'utf8'));
    const sources=Object.fromEntries(walk('src').filter(f=>!f.replace(/\\/g,'/').endsWith('src/app/subscreens.ts')).map(f=>[f,readFileSync(f,'utf8')]));
    const used=usedFitTabs(sources);
    // fit.tsx itself defines useFitTab/FitTabs: its signature text has no string-literal page, so it adds nothing.
    assert.deepEqual(compareTabs(used,subs),[]);
  });

  // ---- 3. stylesheet order ----
  {
    const names=readdirSync('src/styles').filter(f=>f.endsWith('.css'));
    if(!names.includes('z-fit.css'))console.log('SKIP [FIT] z-fit.css not created yet (lane fit-shell)');
    else{
      const {readStyleSources}=await import(pathToFileURL(process.cwd()+'/tools/cssTextScale.mjs').href);
      const bundle=(await readStyleSources('.')).join('\n');
      const pos=n=>bundle.indexOf(readFileSync('src/styles/'+n,'utf8'));
      const [fit,touch,print]=['z-fit.css','z-touch.css','zz-print.css'].map(pos);
      test('[FIT] z-fit.css is in the bundle after z-touch.css and before zz-print.css (bundle order)',()=>{
        assert.ok(fit>=0&&touch>=0&&print>=0,'all three sheets are in the bundle');
        assert.ok(touch<fit,'z-touch.css before z-fit.css');assert.ok(fit<print,'z-fit.css before zz-print.css');
      });
    }
  }

  // ---- 4. phone layout untouched ----
  test('[FIT] no phone-layout file changed since the wave base',()=>{
    let out;
    try{
      execFileSync('git',['rev-parse','--verify',FIT_BASE+'^{commit}'],{stdio:'pipe'});
      out=execFileSync('git',['diff','--name-only',FIT_BASE,'HEAD'],{stdio:'pipe',maxBuffer:64*1024*1024}).toString();
    }catch(e){console.log(`SKIP [FIT] git or base ${FIT_BASE} unavailable (${String(e.message).split('\n')[0]})`);return;}
    const phone=f=>/(^|\/)03 Prototype\/src\/(mobile|app\/mobile)\//.test(f)||/Mobile\.tsx$/.test(f)||/(^|\/)03 Prototype\/src\/styles\/(mobile[^/]*|m-[^/]*)\.css$/.test(f);
    // Phone files that arrived from main by a merge (not a FIT change): KEYS module's own new phone sheet (F1 merge of main 8a377f32).
    // F2 (APK v19 defect fixes V17-4 / V15-4, intended): MobileShell.tsx. F3 LF lanes add their features to phone screens (not FIT layout changes):
    // desk purpose / vehicle / reminders (sheets.tsx, alerts Mobile.tsx, Arrivals.tsx), docdb databases panel (SystemHealth.tsx).
    const FROM_MAIN=new Set(['03 Prototype/src/styles/m-keys.css','03 Prototype/src/app/mobile/MobileShell.tsx',
      '03 Prototype/src/app/mobile/sheets.tsx','03 Prototype/src/features/alerts/Mobile.tsx','03 Prototype/src/mobile/frontdesk/Arrivals.tsx','03 Prototype/src/mobile/admin/SystemHealth.tsx','03 Prototype/src/features/lfmoney/Mobile.tsx',
      '03 Prototype/src/features/accounting/Mobile.tsx','03 Prototype/src/mobile/admin/Settings.tsx',
      '03 Prototype/src/features/tariffcard/TariffCardMobile.tsx','03 Prototype/src/styles/m-f4.css']);   // F4-T1: tariff card phone cards + its sheet (new feature, not FIT)
    const bad=out.split('\n').map(s=>s.trim()).filter(Boolean).filter(phone).filter(f=>!FROM_MAIN.has(f));
    assert.deepEqual(bad,[],'phone-layout files changed since '+FIT_BASE);
  });

  // ---- 1. page roots ----
  await writeFile('tests/.generated/fit-entry.tsx',`
import '../../src/app/App';
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {FitNavContext} from '../../src/components/fit';
import {can,metrics} from '../../src/services/mock';
import {extraPages} from '../../src/app/registry';
import {Overview} from '../../src/features/overview/Overview';
import {RoomBoard} from '../../src/features/rooms/RoomBoard';
import {Reservations} from '../../src/features/reservations/Reservations';
import {Housekeeping} from '../../src/features/housekeeping/Housekeeping';
import {Expenses} from '../../src/features/expenses/Expenses';
import {DayClosing} from '../../src/features/closing/DayClosing';
import {Reports} from '../../src/features/reports/Reports';
import {AccessControl} from '../../src/features/admin/AccessControl';
import {HotelSettings} from '../../src/features/admin/HotelSettings';
const pages:Record<string,()=>React.ReactNode>={overview:()=><Overview/>,rooms:()=><RoomBoard/>,reservations:()=><Reservations/>,folios:()=><Reservations folio/>,housekeeping:()=><Housekeeping/>,expenses:()=><Expenses/>,closing:()=><DayClosing/>,reports:()=><Reports/>,access:()=><AccessControl/>,settings:()=><HotelSettings/>};
for(const [id,P] of Object.entries(extraPages))pages[id]=()=><P/>;
export const PAGE_IDS=Object.keys(pages);
export function render(id:string,s:any,uid:string){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);
  const arrivals=s.bookings.filter((b:any)=>b.status==='Confirmed'&&b.arrival<=s.date),departures=s.bookings.filter((b:any)=>b.status==='In house'&&b.departure<=s.date);
  const ctx:any={s,uid,user,m:metrics(s),page:id,go:noop,modal:{type:'x',key:'k1'},open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,search:'',setSearch:noop,
    permission:(p:any)=>can(s,uid,p),act:(p:any,fn:any,label:string,style='btn')=>can(s,uid,p)?<button className={style}>{label}</button>:null,
    filter:'All',setFilter:noop,tab:'',setTab:noop,arrivals,departures,pending:0,taskList:s.tasks,roomStatus:()=>'',
    display:{isMobile:false,mode:'desktop',setMode:noop},setDirty:noop,openSearch:noop,openMenu:noop,signOut:noop};
  // the desktop shell's nav (App.tsx FitContent): FitPage writes data-fit-page from it
  return renderToStaticMarkup(<HccContext.Provider value={ctx}><FitNavContext.Provider value={{desktop:true,page:id,tabKey:'',seq:1,setTabCrumb:noop}}>{pages[id]()}</FitNavContext.Provider></HccContext.Provider>);
}`);
  let X=null,buildError='';
  try{
    await build({entryPoints:['tests/.generated/fit-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit.mjs',logLevel:'silent',jsx:'automatic',
      define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"demo"'},loader:{'.css':'empty'},
      banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
    X=await import('./.generated/fit.mjs');
  }catch(e){buildError=String(e.message).split('\n')[0];}
  test('[FIT] page harness builds',()=>{assert.ok(X,'could not build the page harness: '+buildError);});
  if(!X)return;
  const s=M.loadScenario('FY26');
  const rootOf=html=>{ // the single top-level element's opening tag
    const m=html.match(/^<([a-z0-9]+)([^>]*)>/);if(!m)return null;
    const tag=m[1];let depth=0,i=0;const re=new RegExp('<(/?)'+tag+'\\b[^>]*?(/?)>','g');let end=-1,mm;
    while((mm=re.exec(html))){if(mm[2])continue;depth+=mm[1]?-1:1;if(depth===0){end=re.lastIndex;break;}}
    return {open:m[0],attrs:m[2],single:end===html.length};
  };
  for(const id of X.PAGE_IDS){
    test(`[FIT] page ${id} renders a fit-page root`,()=>{
      let html;
      try{html=X.render(id,s,'owner');}catch(e){
        if(ENFORCE)assert.fail(`page ${id} did not render for the check: ${String(e.message).split('\n')[0]}`);
        console.log(`SKIP [FIT] page ${id}: render needs more context than the harness gives (${String(e.message).split('\n')[0].slice(0,80)})`);return;
      }
      if(!/fit-page/.test(html)&&!ENFORCE){console.log(`SKIP [FIT] page ${id}: not converted yet (no fit-page root)`);return;}
      const r=rootOf(html);
      assert.ok(r,`page ${id}: markup does not start with an element`);
      assert.ok(/class="[^"]*\bfit-page\b/.test(r.attrs),`page ${id}: the root element has no class fit-page (${r.open.slice(0,80)})`);
      assert.ok(r.single,`page ${id}: more than one top-level element; the fit-page root must be the only child of .content`);
      assert.ok(new RegExp(`data-fit-page="${id}"`).test(r.attrs),`page ${id}: root needs data-fit-page="${id}"`);
    });
  }
}
