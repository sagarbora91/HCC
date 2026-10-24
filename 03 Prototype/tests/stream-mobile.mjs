// MB-01/02 mobile foundation: display-mode resolution and role tabs (pure logic; UI checked with Playwright in MB-11).
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/mobile-entry.ts',"export {resolveMobile,readDisplayMode,DISPLAY_KEY} from '../../src/app/display';export {tabsFor,roleTabs} from '../../src/app/mobile/tabs';export {navItems,landing} from '../../src/app/nav';");
  await build({entryPoints:['tests/.generated/mobile-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/mobile.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'}});
  const X=await import('./.generated/mobile.mjs');
  test('[MB] Display mode: Auto follows width/APK, Desktop and Mobile are forced',()=>{
    assert.equal(X.resolveMobile('auto',false,false),false);assert.equal(X.resolveMobile('auto',true,false),true);assert.equal(X.resolveMobile('auto',false,true),true);
    assert.equal(X.resolveMobile('desktop',true,true),false);assert.equal(X.resolveMobile('mobile',false,false),true);
    assert.equal(X.DISPLAY_KEY,'hcc-display-mode');assert.equal(X.readDisplayMode(),'auto'); // no storage in node → safe default
  });
  test('[MB] Every role gets 1–4 permitted bottom tabs; landing page reachable',()=>{
    const s=M.seed();
    for(const u of s.users.filter(u=>u.enabled)){
      const allowed=X.navItems.filter(n=>M.can(s,u.id,n.p)).map(n=>n.id);
      const tabs=X.tabsFor(u.role,allowed);
      assert.ok(tabs.length>=1&&tabs.length<=4,`${u.role}: ${tabs.length} tabs`);
      for(const t of tabs)assert.ok(allowed.includes(t.page),`${u.role} tab ${t.key} not permitted`);
      assert.ok(allowed.includes(X.landing[u.role]),`${u.role} landing`);
    }
  });
  test('[MB] Tabs hide pages a role loses (access edit applies on phone too)',()=>{
    const s=M.execute(M.seed(),'owner',{type:'GRANT',role:'Reception',permission:'reservations'});
    const allowed=X.navItems.filter(n=>M.can(s,'reception',n.p)).map(n=>n.id);
    assert.ok(!X.tabsFor('Reception',allowed).some(t=>t.page==='rooms'||t.page==='reservations'));
  });
}
