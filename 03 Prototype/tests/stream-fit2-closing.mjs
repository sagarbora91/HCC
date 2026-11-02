// FIT2-CLOSING (wave FIT-2, lane closing, owner rule 2 Nov 2026, contract §12): Day closing, Cashier shift and the Handover checklist run are tasks -
// every sub-screen fits and a fixed rail shows each step's REAL result with no click. Checks the rail rows against the same state, the sub-screen
// registry, the header action, and that the lane CSS is desktop-only and font-free.
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
await build({entryPoints:['tests/fit2-closing-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fit2-closing.mjs',
  external:['react','react-dom','lucide-react'],logLevel:'error',loader:{'.css':'empty'},define:{'process.env.HCC_BUILD':'"prototype"','process.env.NODE_ENV':'"production"'}});
const F=await import('./.generated/fit2-closing.mjs');
const {renderToStaticMarkup}=await import('react-dom/server');
const React=(await import('react')).default;
const h=React.createElement;
function ctxFor(s,uid){
  const user=s.users.find(u=>u.id===uid);
  return {s,uid,user,m:F.metrics(s),page:'home',go(){},modal:null,open(){},closeModal(){},booking(){},error:'',busy:false,run:async()=>true,toast(){},
    permission:p=>F.can(s,uid,p),act:()=>null,search:'',setSearch(){},filter:'All',setFilter(){},tab:'',setTab(){},
    arrivals:[],departures:[],pending:0,taskList:s.tasks,roomStatus:()=>'Vacant',switchUser(){},fault:'none',setFault(){},loadScenario:async()=>{},resetScenario(){},
    display:{isMobile:false,framed:false,mode:'desktop',setMode(){}},setDirty(){},openSearch(){},openMenu(){},signOut(){},prefs:{prefs:{textScale:1},save(){},reset(){}}};
}
const forget=page=>{try{globalThis.sessionStorage?.removeItem('hcc-fit-tab:'+page);}catch{/* no storage */}};
const desk=(el,s,uid,page,tab='')=>(tab||forget(page),renderToStaticMarkup(h(F.HccContext.Provider,{value:ctxFor(s,uid)},h(F.FitNavContext.Provider,{value:{desktop:true,page,tabKey:tab,seq:1,setTabCrumb(){}}},el))));
const text=t=>t.replace(/<!-- -->/g,'').replace(/<[^>]+>/g,'').replace(/&#x27;|&#39;/g,"'").replace(/&amp;/g,'&');
const subs=page=>F.SUB_SCREENS.filter(x=>x.page===page).map(x=>x.id);
/** Rail rows: [label, summary] from the markup (the rail is the <ol class="fit-step-rail">). */
const rail=html=>{const ol=html.slice(html.indexOf('<ol class="fit-step-rail"'),html.indexOf('</ol>'));
  return ol.split('<li>').slice(1).map(li=>[text((li.match(/<strong>(.*?)<\/strong>/)||[])[1]||'').replace(/^\d+\. /,''),text((li.match(/<span class="fit-rail-sum"[^>]*>(.*?)<\/span>/)||[])[1]||'')]);};
const noComments=t=>t.replace(/\/\*[^]*?\*\//g,'');
const rules=css=>[...noComments(css).replace(/@media[^{]*\{/g,'').matchAll(/([^{}]+)\{([^{}]*)\}/g)].map(m=>({sel:m[1].trim(),body:m[2]})).filter(r=>r.sel);

export function register({test,assert}){
  const s=F.seed();
  test('[FIT2-CLOSING] Day closing: every one of the seven steps carries a real result in the rail (no click needed)',()=>{
    const html=desk(h(F.DayClosing),s,'owner','closing');
    assert.ok(html.includes('class="fit-steps has-summary"'));
    const r=rail(html);assert.equal(r.length,7);
    for(const [label,sum] of r)assert.ok(sum.length>0,label+' has a summary');
    const open=F.closingPreflight(s).filter(x=>x.id!=='nights'&&x.id!=='shift'&&x.items.length).reduce((n,c)=>n+c.items.length,0);
    const targets=F.closingNightTargets(s);const posted=targets.filter(b=>F.closingHasNight(b,s.date)).length;
    assert.match(r[0][1],open?new RegExp(`^${open} to resolve`):/All clear/,'preflight: count to resolve');
    assert.ok(r[1][1].startsWith(`${posted} of ${targets.length}`),'rooms posted n of n: '+r[1][1]);
    assert.ok(/^(Counted|Not counted)/.test(r[2][1]),'cash: '+r[2][1]);
    assert.ok(/^(Blocked by|Ready to close)/.test(r[3][1]),'close: blocked-by / ready: '+r[3][1]);
    assert.ok(r[4][1].startsWith('Net receipts ₹')&&r[4][1].includes('preview'),'summary: net receipts: '+r[4][1]);
    assert.ok(/^Next day /.test(r[5][1]),'next day: '+r[5][1]);
    assert.ok(/^(Nothing reopened|\d+ correction)/.test(r[6][1]),'reopen: '+r[6][1]);
  });
  test('[FIT2-CLOSING] Day closing: a counted shift shows counted cash and the difference; the primary action stays in the header',()=>{
    const base=F.seed();const t=F.execute(base,'owner',{type:'SHIFT_CLOSE',counted:F.shiftExpected(base).expected});
    const html=desk(h(F.DayClosing),t,'owner','closing');const r=rail(html);
    assert.ok(/^Counted ₹[\d,.]+ · exact$/.test(r[2][1]),r[2][1]);
    assert.ok(html.slice(0,html.indexOf('</header>')).includes('Close '),'Close action in the header');
  });
  test('[FIT2-CLOSING] Cashier: sub-screen ids = SUB_SCREENS lines (FIT2-CLOSING block), four steps, each with a summary',()=>{
    assert.deepEqual(subs('cashier'),[...F.CASHIER_STEPS]);
    const src=readFileSync('src/app/subscreens.ts','utf8');const block=src.slice(src.indexOf('// FIT2-CLOSING'),src.indexOf('// FIT-PEOPLE-B'));
    assert.equal((block.match(/page:'cashier'/g)||[]).length,4);
    const html=desk(h(F.Cashier),s,'owner','cashier');const r=rail(html);
    assert.deepEqual(r.map(x=>x[0]),['Expected cash','Count & result','Cash movements','Cash out']);
    assert.ok(r[0][1].includes('expected ₹')&&r[1][1].startsWith('Not counted yet')&&/^\d+ movements? · net [+−]₹/.test(r[2][1]),JSON.stringify(r));
    const base=F.seed();const done=F.execute(base,'owner',{type:'SHIFT_CLOSE',counted:F.shiftExpected(base).expected});
    assert.match(rail(desk(h(F.Cashier),done,'owner','cashier'))[1][1],/^Counted ₹[\d,.]+ · exact$/);
  });
  test('[FIT2-CLOSING] Handover: each checklist run in the rail shows its real progress',()=>{
    const html=desk(h(F.HandoverPage),s,'owner','handover');
    const runs=F.hovMyRuns(s,'owner');assert.ok(runs.length>0);
    const r=rail(html);assert.equal(r.length,runs.length);
    for(const [,sum] of r)assert.match(sum,/^(Submitted|\d+% · \d+\/\d+ done · mandatory \d+\/\d+)/);
    const p=F.hovProgress(s,runs.find(x=>x.status==='Open')??runs[0]);
    assert.ok(r.some(([,sum])=>sum.includes(`${p.done}/${p.total}`)));
  });
  test('[FIT2-CLOSING] CSS: desktop rules only, no font size, no hex colour',()=>{
    const css=readFileSync('src/styles/fit2-closing.css','utf8');const rs=rules(css);assert.ok(rs.length>3);
    for(const r of rs){for(const sel of r.sel.split(','))assert.match(sel.trim(),/^\.app-shell /,sel);
      assert.ok(!/font-size|font:/.test(r.body),'no font change: '+r.sel);assert.ok(!/#[0-9a-f]{3,8}\b/i.test(r.body),'tokens only: '+r.sel);}
  });
}
