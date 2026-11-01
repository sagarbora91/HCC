// FIT-PEOPLE-A (wave W2a FIT, DEC-194…196, APR-025, contract 02 Planning/agents/FIT-LAYOUT-CONTRACT.md §6): Staff & payroll, My shifts & pay,
// Day closing and the Compliance calendar render one fit-page root on the desktop layout; their useFitTab ids equal their SUB_SCREENS lines;
// the default tabs / steps are as set; go(page,{tab}) opens a sub-screen; the Day-closing "Close <date>" action sits in the page header with
// the old enabling rule; DEC-058 still hides the payroll tabs from managers; the phone versions carry no fit- class.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['tests/fit-people-a-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit-people-a.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',loader:{'.css':'empty'},define:{'process.env.HCC_BUILD':'"prototype"','process.env.NODE_ENV':'"production"'}});
const F=await import('./.generated/fit-people-a.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
const h=React.createElement;

function ctxFor(s,uid,isMobile=false){
  const user=s.users.find(u=>u.id===uid);
  return {s,uid,user,m:F.metrics(s),page:'home',go(){},modal:null,open(){},closeModal(){},booking(){},error:'',busy:false,run:async()=>true,toast(){},
    permission:p=>F.can(s,uid,p),act:()=>null,search:'',setSearch(){},filter:'All',setFilter(){},tab:'',setTab(){},
    arrivals:[],departures:[],pending:0,taskList:s.tasks,roomStatus:()=>'Vacant',switchUser(){},fault:'none',setFault(){},loadScenario:async()=>{},resetScenario(){},
    display:{isMobile,framed:false,mode:isMobile?'mobile':'desktop',setMode(){}},setDirty(){},openSearch(){},openMenu(){},signOut(){},prefs:{prefs:{textScale:1},save(){},reset(){}}};
}
/** Desktop render: HccContext + the desktop shell's FitNavContext (page, asked tab). */
const desk=(el,s,uid,page,tab='')=>renderToStaticMarkup(h(F.HccContext.Provider,{value:ctxFor(s,uid)},h(F.FitNavContext.Provider,{value:{desktop:true,page,tabKey:tab,seq:1,setTabCrumb(){}}},el)));
const phone=(el,s,uid)=>renderToStaticMarkup(h(F.HccContext.Provider,{value:ctxFor(s,uid,true)},el));
const subs=page=>F.SUB_SCREENS.filter(x=>x.page===page).map(x=>x.id);
const root=html=>(html.match(/^<section class="([^"]+)" data-fit-page="([^"]+)"/)||[]).slice(1);
const selectedTab=html=>(html.match(/role="tab" id="fit-tab-[^"]+" data-tab="([^"]+)" aria-selected="true"/)||[])[1];
const currentStep=html=>(html.match(/class="fit-step [a-z]+ on" aria-current="step"[^>]*>.*?<strong>\d+(?:<!-- -->)?\. (?:<!-- -->)?([^<]+)<\/strong>/)||[])[1];
const src=f=>readFileSync(f,'utf8');

export function register({test,assert}){
  const s=F.seed();

  test('[FIT-PEOPLE-A] each converted page renders one fit-page root with data-fit-page (staff, my-hr, closing, compliance) and the right layout',()=>{
    assert.deepEqual(root(desk(h(F.HrDesk),s,'owner','staff')),['fit-page fit-fill','staff']);
    assert.deepEqual(root(desk(h(F.HrDesk),s,'housekeeper','staff')),['fit-page fit-fill','staff'],'non-HR roles get My shifts & pay, also a fit page');
    assert.deepEqual(root(desk(h(F.MyHr),s,'housekeeper','my-hr')),['fit-page fit-fill','my-hr']);
    assert.deepEqual(root(desk(h(F.MyHr),s,'techadmin','my-hr')),['fit-page fit-fill','my-hr'],'no linked staff record: still a fit page');
    assert.deepEqual(root(desk(h(F.DayClosing),s,'owner','closing')),['fit-page fit-steps','closing']);
    assert.deepEqual(root(desk(h(F.CompliancePage),s,'owner','compliance')),['fit-page fit-fill','compliance']);
    assert.deepEqual(root(desk(h(F.CompliancePage),s,'reception','compliance')),['fit-page fit-fill','compliance'],'no access: the notice sits in a fit page');
    for(const [el,uid,page] of [[h(F.HrDesk),'owner','staff'],[h(F.MyHr),'housekeeper','my-hr'],[h(F.DayClosing),'owner','closing'],[h(F.CompliancePage),'owner','compliance']]){
      const html=desk(el,s,uid,page);assert.ok(html.endsWith('</section>'),page+' is a single root');assert.ok(html.includes('data-fit-scroll=""'),page+' has a scroll panel');
      assert.ok(!html.includes('class="page-title"')||html.includes('page-title fit-page-head'),page+': no PageTitle inside the FitPage');
    }
  });

  test('[FIT-PEOPLE-A] useFitTab ids = SUB_SCREENS lines (staff tabs, the seven Day-closing steps, compliance views), inside the FIT-PEOPLE-A block',()=>{
    assert.deepEqual(subs('staff'),['people','roster','attendance','leave','payroll','advances','fnf','settings']);
    assert.ok(src('src/features/staff/HrDesk.tsx').includes("const STAFF_TABS=['people','roster','attendance','leave','payroll','advances','fnf','settings'] as const;"));
    assert.deepEqual(subs('closing'),[...F.CLOSING_STEPS]);assert.equal(F.CLOSING_STEPS.length,7);
    assert.deepEqual(subs('compliance'),['calendar','dashboard']);
    assert.deepEqual(subs('my-hr'),[],'my-hr has no sub-screens (fill)');
    const reg=src('src/app/subscreens.ts'),block=reg.slice(reg.indexOf('// FIT-PEOPLE-A'));
    for(const p of ['staff','closing','compliance'])assert.equal((block.match(new RegExp(`page:'${p}'`,'g'))||[]).length,subs(p).length,p+' lines all in the FIT-PEOPLE-A block');
  });

  test('[FIT-PEOPLE-A] defaults: staff opens on the role\'s first tab (Staff), compliance on Calendar, Day closing on the first step not done',()=>{
    assert.equal(selectedTab(desk(h(F.HrDesk),s,'owner','staff')),'people');
    assert.equal(selectedTab(desk(h(F.CompliancePage),s,'owner','compliance')),'calendar');
    const html=desk(h(F.DayClosing),s,'owner','closing');
    assert.equal(F.isDayClosed(s),false);
    const pf=F.closingPreflight(s).filter(x=>x.id!=='nights'&&x.id!=='shift').some(x=>x.items.length);
    assert.equal(currentStep(html),pf?'Preflight':'Post room nights');
  });

  test('[FIT-PEOPLE-A] go(page,{tab}) opens the sub-screen: Payroll, Leave, Statutory rates, a Day-closing step, the compliance Dashboard',()=>{
    let html=desk(h(F.HrDesk),s,'owner','staff','payroll');assert.equal(selectedTab(html),'payroll');assert.match(html,/Monthly payroll/);
    html=desk(h(F.HrDesk),s,'owner','staff','settings');assert.match(html,/Statutory rates and versions/);assert.match(html,/Versions/);
    html=desk(h(F.HrDesk),s,'owner','staff','leave');assert.match(html,/Leave calendar/);
    html=desk(h(F.DayClosing),s,'owner','closing','closing-summary');assert.equal(currentStep(html),'Closing summary');assert.match(html,/Earlier closing records/);
    html=desk(h(F.DayClosing),s,'owner','closing','reopen-and-corrections');assert.match(html,/Closed-date corrections/);assert.match(html,/Reopen a closed day/,'the Owner sees the reopen form');
    assert.doesNotMatch(desk(h(F.DayClosing),s,'manager','closing','reopen-and-corrections'),/Reopen a closed day/,'others do not (no reopened day)');
    html=desk(h(F.CompliancePage),s,'owner','compliance','dashboard');assert.equal(selectedTab(html),'dashboard');
    assert.equal(selectedTab(desk(h(F.HrDesk),s,'owner','staff','nope')),'people','unknown tab → default');
  });

  test('[FIT-PEOPLE-A] Day closing: "Close <date>" is the first header action on every step, enabled exactly by the old rule (busy, close permission, blockers)',()=>{
    const blockers=F.closingPreflight(s).filter(x=>x.items.length).length;
    for(const uid of ['owner','manager','reception','accountant'])for(const step of F.CLOSING_STEPS){
      const html=desk(h(F.DayClosing),s,uid,'closing',step);
      const m=html.match(/<div class="fit-page-actions"><button class="btn primary"( disabled="")?>Close [^<]+/);
      assert.ok(m,`${uid}/${step}: Close is the first header action`);
      assert.equal(!!m[1],!F.can(s,uid,'close')||blockers>0,`${uid}/${step}: disabled exactly when no close permission or blockers`);
    }
    assert.ok(src('src/features/closing/DayClosing.tsx').includes("disabled={busy||!canClose||!!blockers.length} onClick={()=>run({type:'DAY_CLOSE',key:'dayclose:'+s.date}"),'enabling rule and command unchanged');
    assert.ok(src('src/features/closing/DayClosing.tsx').includes("disabled={busy||!closed||!canClose} onClick={()=>run({type:'NEXT_DAY',key:'nextday:'+s.date}"),'next-day rule unchanged');
    assert.match(src('src/features/closing/DayClosing.tsx'),/process\.env\.HCC_BUILD==='production'\?null:<button className="btn subtle"[^\n]*Demo: interrupt after/,'N3 demo button kept');
  });

  test('[FIT-PEOPLE-A] DEC-058 / role gating unchanged: managers get Staff…Leave tabs without payroll; Owner and Accountant also Payroll…Statutory rates',()=>{
    const tabsOf=html=>[...html.matchAll(/role="tab" id="fit-tab-staff-([a-z]+)"/g)].map(m=>m[1]);
    assert.deepEqual(tabsOf(desk(h(F.HrDesk),s,'owner','staff')),['people','roster','attendance','leave','payroll','advances','fnf','settings']);
    assert.deepEqual(tabsOf(desk(h(F.HrDesk),s,'manager','staff')),['people','roster','attendance','leave']);
    assert.equal(selectedTab(desk(h(F.HrDesk),s,'manager','staff','payroll')),'people','a manager asking for Payroll lands on the first allowed tab');
    assert.doesNotMatch(desk(h(F.HrDesk),s,'manager','staff','payroll'),/Monthly payroll/);
    const off=F.execute(s,'owner',{type:'SETTING_SET',setting:'module.payroll',value:false});
    assert.deepEqual(tabsOf(desk(h(F.HrDesk),off,'owner','staff')),['people','roster','attendance','leave'],'payroll switched off hides its tabs');
  });

  test('[FIT-PEOPLE-A] phone versions untouched: no fit- class or data-fit-scroll in the phone HR desk, My HR and compliance',()=>{
    for(const [el,uid] of [[h(F.HrDesk,{phone:true}),'owner'],[h(F.HrDesk,{phone:true}),'manager'],[h(F.MyHr,{phone:true}),'housekeeper'],[h(F.ComplianceMobile),'owner']]){
      const html=phone(el,s,uid);assert.doesNotMatch(html,/fit-|data-fit-scroll/,uid);
    }
  });

  test('[FIT-PEOPLE-A] desktop stylesheets: only under .app-shell, no font sizes, no hex colours',()=>{
    for(const f of ['src/styles/staff-fit.css','src/styles/closing-fit.css','src/styles/compliance-fit.css']){
      const css=src(f).replace(/\/\*[^]*?\*\//g,'');
      for(const rule of css.match(/[^{}]+\{[^{}]*\}/g)||[]){const sel=rule.slice(0,rule.indexOf('{')).trim();
        if(sel.startsWith('@media'))continue;for(const part of sel.replace(/^@media[^{]*/,'').split(/,(?![^()]*\))/))assert.match(part.trim(),/^\.app-shell\b/,f+': '+part);}
      assert.doesNotMatch(css,/font-size|font:/,f+': no font sizes (DEC-195)');assert.doesNotMatch(css,/#[0-9a-fA-F]{3,8}\b/,f+': tokens only');
    }
  });
}
