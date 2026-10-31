// LG-UI · Customers 360 with legacy (old Lodge app) stays, and the Owner page "Legacy import". Names prefixed [LG-UI].
// Rollups with and without legacy stays, masking per role, the Legacy badge rows (read only, no folio link), the production mapper,
// the page registered + gated (Owner only), the phone page registered, navmap entry.
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/lg-ui-entry.tsx',`
import '../../src/app/App';
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
import {registration as adminReg} from '../../src/features/admin/register';
import {registration as mobileReg} from '../../src/mobile/admin/register';
import {customerIndex,legacyCounts} from '../../src/domain/streams/customers';
import {legacyProfileRead} from '../../src/services/mock/handlers/customers-legacy';
import {LEGACY_SAMPLE} from '../../src/fixtures/ext/legacy-sample';
import {CustomersPage,CustomerPanel} from '../../src/features/customers/Customers';
import {LegacyImport,LegacyResultView} from '../../src/features/admin/LegacyImport';
import {MaLegacyImport} from '../../src/mobile/admin/LegacyImport';
import {demoLegacyResult,legacyRunBlock} from '../../src/features/admin/legacyImportModel';
import {legacyStayFromServer,legacyStaysFromProfile} from '../../src/clients/sync/customers-legacy';
import {legacyRunFromServer} from '../../src/clients/sync/legacy-import';
import {navRoleOk,PAGE_CATEGORY,PAGE_KEYWORDS} from '../../src/app/navmap';
export {customerIndex,legacyCounts,legacyProfileRead,LEGACY_SAMPLE,adminReg,mobileReg,demoLegacyResult,legacyRunBlock,legacyStayFromServer,legacyStaysFromProfile,legacyRunFromServer,navRoleOk,PAGE_CATEGORY,PAGE_KEYWORDS};
const C:any={CustomersPage,CustomerPanel,LegacyImport,LegacyResultView,MaLegacyImport};
export function render(name:string,props:any,s:any,uid:string,mobile=false,search=''){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);
  const ctx:any={s,uid,user,m:metrics(s),page:'x',go:noop,modal:{type:'x',key:'k1'},open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,search,setSearch:noop,
    permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop};
  return renderToStaticMarkup(<HccContext.Provider value={ctx}>{React.createElement(C[name],props)}</HccContext.Provider>);
}`);
  await build({entryPoints:['tests/.generated/lg-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/lg-ui.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"demo"'},loader:{'.css':'empty'},
    banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
  const X=await import('./.generated/lg-ui.mjs');
  const text=h=>h.replace(/<[^>]+>/g,' ').replace(/&#x27;|&#39;/g,"'").replace(/&amp;/g,'&').replace(/\s+/g,' ');
  const s=M.loadScenario('FY26'),lookup=k=>X.legacyProfileRead(k);
  const plain=X.customerIndex(s),withL=X.customerIndex(s,lookup);
  const A='9820001041',V='9820001042';
  const pick=(l,k)=>l.find(c=>c.key===k);

  test('[LG-UI] the fixture has six fictional legacy stays for two seed guests',()=>{
    const all=Object.values(X.LEGACY_SAMPLE).flat();assert.equal(all.length,6);assert.deepEqual(Object.keys(X.LEGACY_SAMPLE).sort(),[A,V]);
    assert.ok(pick(plain,A)&&pick(plain,V),'both guests exist as customers in FY26');
    assert.equal(X.legacyProfileRead(A)[0].arrival>=X.legacyProfileRead(A)[1].arrival,true,'newest first');
  });
  test('[LG-UI] without legacy stays nothing changes (rollups, counts, first stay)',()=>{
    for(const c of plain){assert.equal(c.legacyStays.length,0);assert.equal(c.legacyCount,0);}
    const c=pick(plain,A);assert.equal(c.nights,c.completed.reduce((t,b)=>t+M.nights(b.arrival,b.departure),0));
    assert.equal(c.lifetime,c.completed.reduce((t,b)=>t+M.totals(b).total,0));
  });
  test('[LG-UI] with legacy stays nights, lifetime and first stay include them; stays stays live-only; cancelled legacy does not count',()=>{
    for(const k of [A,V]){
      const p=pick(plain,k),c=pick(withL,k),ok=X.LEGACY_SAMPLE[k].filter(X.legacyCounts);
      assert.equal(c.stays.length,p.stays.length,'live stays unchanged');assert.equal(c.completed.length,p.completed.length);
      assert.equal(c.legacyStays.length,X.LEGACY_SAMPLE[k].length);assert.equal(c.legacyCount,ok.length);
      assert.equal(c.nights,p.nights+ok.reduce((t,l)=>t+l.nights,0));
      assert.equal(c.lifetime,p.lifetime+ok.reduce((t,l)=>t+(l.net??0),0));
      const first=[p.firstStay,...ok.map(l=>l.arrival)].filter(Boolean).sort()[0];assert.equal(c.firstStay,first);
      assert.equal(c.avgRate,p.avgRate,'average tariff stays live-only');
    }
    assert.equal(pick(withL,V).legacyStays.length-pick(withL,V).legacyCount,1,'the cancelled stay is listed but not counted');
    assert.equal(pick(withL,V).firstStay,'2023-12-27');
  });
  test('[LG-UI] customers without legacy stays are untouched when others have some',()=>{
    const other=plain.find(c=>![A,V].includes(c.key));assert.deepEqual(pick(withL,other.key).nights,other.nights);
    assert.equal(withL.length,plain.length);
  });
  test('[LG-UI] the index is cached per lookup (a different lookup rebuilds it)',()=>{
    assert.equal(X.customerIndex(s,lookup),withL);assert.notEqual(X.customerIndex(s,k=>[]),withL);
  });
  test('[LG-UI] production mapper: LegacyStays of the profile → LegacyStay, money absent when masked',()=>{
    const v={stayId:7,legacyFy:'2025-26',legacyBookingId:41877,legacyBillNo:3098,arrival:'2025-06-14T00:00:00',departure:'2025-06-16',nights:2,room:'207',roomCount:1,status:'Departed',netPaise:512000};
    assert.deepEqual(X.legacyStayFromServer(v),{fy:'2025-26',bookingId:41877,billNo:3098,arrival:'2025-06-14',departure:'2025-06-16',nights:2,room:'207',roomCount:1,status:'Departed',net:512000});
    const m=X.legacyStayFromServer({...v,legacyBillNo:null,departure:null,netPaise:null});
    assert.equal('net' in m,false);assert.equal('billNo' in m,false);assert.equal('departure' in m,false);
    const l=X.legacyStaysFromProfile({legacyStays:[{...v,legacyBookingId:1,arrival:'2024-01-01'},v]});assert.equal(l[0].bookingId,41877);
    assert.deepEqual(X.legacyStaysFromProfile({legacyStays:null}),[]);assert.deepEqual(X.legacyStaysFromProfile(undefined),[]);
  });
  test('[LG-UI] Customers 360 desktop: Legacy badge rows with old bill number, money masked by the live-stay rule, no folio link',()=>{
    // render with the real legacy lookup the page uses (mock handler) – search narrows to Ananya Rao
    const html=X.render('CustomersPage',{},s,'owner',false,'Ananya Rao'),t=text(html);
    assert.ok(/Legacy/.test(t),'badge');assert.ok(/Old bill no\. 2210/.test(t));assert.ok(/\(3 legacy\)/.test(t),'list shows legacy count muted');
    assert.ok(/₹6,900/.test(t.replace(/\s/g,'')||t)||/6,900/.test(t),'owner sees the net amount');
    const legacyBlock=html.slice(html.indexOf('aria-label="Legacy stays"'));
    assert.ok(!/<button/.test(legacyBlock.slice(0,legacyBlock.indexOf('</ul>'))),'legacy rows are not buttons (no folio link)');
    const hk=text(X.render('CustomersPage',{},s,'housekeeper',false,'Ananya Rao'));
    assert.ok(!/6,900/.test(hk),'a role without billing never sees the amount');
  });
  test('[LG-UI] Customers 360 phone renders the legacy rows too',()=>{
    const c=pick(withL,V);const t=text(X.render('CustomerPanel',{c,onBack(){},onLegacy(){}},s,'owner',true));
    assert.ok(/Legacy/.test(t));assert.ok(/Old bill no\. 1876/.test(t));assert.ok(/2 rooms/.test(t));
  });
  test('[LG-UI] Legacy import: registered under Admin & Setup, Owner only, nav + keywords + phone page',()=>{
    assert.equal(typeof X.adminReg.pages['legacy-import'],'function');
    const nav=X.adminReg.nav.find(n=>n.item.id==='legacy-import');assert.ok(nav);assert.equal(nav.item.label,'Legacy import');
    assert.equal(typeof X.mobileReg.mobile.pages['legacy-import'],'function');
    assert.equal(X.PAGE_CATEGORY['legacy-import'],'admin');assert.ok(X.PAGE_KEYWORDS['legacy-import'].includes('legacy import'));
    const owner=text(X.render('LegacyImport',{},s,'owner'));assert.ok(/Run folder on the server PC/.test(owner));assert.ok(/Dry run/.test(owner));
    for(const u of ['manager','accountant','reception','cashier']){const t=text(X.render('LegacyImport',{},s,u));assert.ok(/for the Owner/.test(t),u);assert.ok(!/Run folder/.test(t),u);}
    for(const u of ['manager','reception']){assert.ok(/for the Owner/.test(text(X.render('MaLegacyImport',{},s,u,true))),u);}
    assert.ok(/read only/.test(text(X.render('MaLegacyImport',{},s,'owner',true))));
    // LG integrator: the menu entry is hidden from the Tech admin (navRoleOk), kept for the Owner
    assert.equal(X.navRoleOk(s,'techadmin','legacy-import'),false);assert.equal(X.navRoleOk(s,'owner','legacy-import'),true);
  });
  test('[LG-UI] the result is rendered as tables with status colour AND text; mismatch lists its differences',()=>{
    const r=X.demoLegacyResult({folder:'E:\\x',parts:['guests','history','cutover'],dryRun:true,expectedTotals:'{}'});
    const html=X.render('LegacyResultView',{r},s,'owner'),t=text(html);
    assert.ok(/Match/.test(t)&&/Mismatch/.test(t),'text labels');assert.ok(html.includes('pill green')&&html.includes('acc-sev-error'),'status colours');
    assert.ok(/Differences · 2026-27/.test(t));assert.ok(/netPaise/.test(t)&&/receiptsCount/.test(t));
    assert.ok(/Cut-over/.test(t)&&/Guests/.test(t)&&/Warnings/.test(t));assert.ok((html.match(/<table/g)||[]).length>=4);
  });
  test('[LG-UI] Run is blocked until folder, parts and totals are valid; runs rows map tolerantly',()=>{
    assert.ok(X.legacyRunBlock('',['history'],'{}'));assert.ok(X.legacyRunBlock('\\\\srv\\share',['history'],'{}'));assert.ok(X.legacyRunBlock('E:\\r',[],'{}'));assert.ok(X.legacyRunBlock('E:\\r',['history'],' '));
    assert.equal(X.legacyRunBlock('E:\\r',['history'],'{}'),undefined);
    const r=X.legacyRunFromServer({runId:'r1',startedUtc:'2026-11-05T21:30:00Z',dryRun:true,parts:'guests,history',committed:false,extra:1});
    assert.deepEqual(r,{runId:'r1',at:'2026-11-05T21:30:00Z',dryRun:true,parts:['guests','history'],committed:false});
  });
  test('[LG-UI] legacy data has no State slice, no persistence (read only)',()=>{
    const st=M.seed();assert.ok(!('legacyX' in st)&&!('legacy' in st));
  });
}
