// W4 N3 polish: production wording rules, production-only demo controls, staff directory patch, phone footer, unassigned-task hint.
import {readFileSync} from 'node:fs';
import {RULES} from '../build-prod-text.mjs';
import {loadSync} from './_sync-harness.mjs';
const src=p=>readFileSync(new URL('../'+p,import.meta.url),'utf8');
const fix=t=>RULES.reduce((a,[re,to])=>a.replace(re,to),t);
export function register({test,assert}){
  test('[N3-2] production wording rules remove demo / simulated / fictional text but keep code',()=>{
    assert.equal(fix("'Aadhaar (demo)'"),"'Aadhaar'");
    assert.equal(fix("'Generated (simulated clock)'"),"'Generated'");
    assert.equal(fix("Simulated clock {clockTime(s.clock)} IST"),"Hotel clock {clockTime(s.clock)} IST");
    assert.equal(fix('placeholder="Demo transaction reference, e.g. DEMO-UPI-0002"'),'placeholder="Transaction reference"');
    assert.equal(fix("a sample number from DEMO-CN/2026-27/ is issued"),"a credit note number is issued");
    assert.equal(fix("<ChnSimulated text={x}/>"),"<ChnSimulated text={x}/>");
    assert.equal(fix("const o={simulated:true,a:1};"),"const o={simulated:true,a:1};");
    assert.ok(!/fictional/i.test(fix("'Fictional staff only. One role per user.'")));
  });
  test('[N3-2] demo-only controls are compiled out of the production build',()=>{
    assert.match(src('src/features/closing/DayClosing.tsx'),/process\.env\.HCC_BUILD==='production'\?null:<button className="btn subtle"[^\n]*Demo: interrupt after/);
    assert.match(src('src/mobile/admin/Closing.tsx'),/process\.env\.HCC_BUILD==='production'\?null:!md\.closed/);
    assert.match(src('src/features/channels/shared.tsx'),/if\(process\.env\.HCC_BUILD==='production'\)return null/);
    assert.match(src('src/features/messaging/shared.tsx'),/if\(process\.env\.HCC_BUILD==='production'\)return null/);
    assert.match(src('src/features/handover/HandoverPage.tsx'),/useHcc\(\);if\(process\.env\.HCC_BUILD==='production'\|\|!permission\('close'\)\)return null/);
    assert.match(src('build.mjs'),/prodTextPlugin/);
  });
  test('[N3-1] staff directory loader fills names for non-admin roles, leaves the admin list alone',async()=>{
    const S=await loadSync(),area=S.AREAS.find(x=>x.id==='directory'),l=area.loaders[0];
    assert.equal(l.slice,'users');assert.equal(l.kind,'patch');
    const known=[{id:'7',name:'Me',role:'Supervisor',enabled:true}];
    const ctx=admin=>({current:()=>known,can:()=>admin,api:{get:async p=>{assert.equal(p,'/me/directory');return {items:[{userId:7,displayName:'Me X'},
      {userId:3,displayName:'Ramesh Patil',enabled:true,roles:['CSH']},{userId:4,displayName:'Asha',enabled:false,roles:['HKP']}]};}}});
    const out=await l.load(ctx(false));
    assert.deepEqual(out.map(u=>[u.id,u.name,u.role,u.enabled]),[['7','Me','Supervisor',true],['3','Ramesh Patil','Cashier',true],['4','Asha','Housekeeper',false]]);
    assert.equal(await l.load(ctx(true)),known);
  });
  test('[N3-5] 360 px: the main folio action gets its own row',()=>{
    assert.match(src('src/styles/mobile-money.css'),/@media\(max-width:400px\)\{\.modal\.m-sheet \.modal-actions\.mm-foot\{flex-wrap:wrap\}[^}]*mm-foot-main\{flex:1 1 100%\}/);
  });
  test('[N3-6] unassigned checkout tasks show an assign hint and an Unassigned filter',()=>{
    const t=src('src/features/housekeeping/Housekeeping.tsx');
    assert.match(t,/Unassigned\. Choose a housekeeper below/);assert.match(t,/<option value="">Unassigned<\/option>/);
  });
}
