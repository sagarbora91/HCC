// ADM-2 lane P · custom roles in the prototype (DEC-216…221; 02 Planning/agents/ADM_Briefs.md §3). Names prefixed [ADM2].
// Mirrors the server tests (brief §2.4) on the mock: day-shift manager (accounting.* off) refused on Accounting commands and sees no
// accounting page; night-shift manager (accounting view) refused on ACC_MANUAL_SAVE but passes the day-close path; a Housekeeper-based role
// never gains money; last-owner rule unchanged; no self-edit; built-ins read-only; retire refused while held; base copy frozen; old saves.
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
await writeFile('tests/.generated/roles-nav-entry.ts',"export {navFilterModules} from '../../src/app/navmap';export {navItems} from '../../src/app/nav';");
await build({entryPoints:['tests/.generated/roles-nav-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/roles-nav.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'}});
const N=await import('./.generated/roles-nav.mjs');

export function register({test,M,assert}){
  const {seed,execute,can,authorize,rolSeedDemo,rolFeatureLevel,rolPreview,rolCustom,rolCommandBlock,restoreState,SCHEMA}=M;
  const err=fn=>{try{fn();return {code:'OK',msg:''};}catch(e){return {code:e.code,msg:e.message};}};
  const demo=()=>rolSeedDemo(seed());
  const pages=(s,uid)=>N.navFilterModules(s,N.navItems,uid).filter(n=>can(s,uid,n.p)).map(n=>n.id);
  const ACC=['ACC_MANUAL_SAVE','ACC_LEDGER_UPSERT','ACC_GROUP_UPSERT','ACC_LOCK','ACC_GST_SETOFF','ACC_TDS_REMIT','ACC_TALLY_EXPORT','ACC_LOAN_ADD','ACC_OPENING_SAVE','ACC_MONTH_LOCK'];

  test('[ADM2] SCHEMA 14; demo seed adds C01 / C02 (Manager base) and one user each; BASE keeps its ten users',()=>{
    assert.equal(SCHEMA,16); /* F8 setup: 15 → 16 */assert.equal(seed().users.length,10);assert.equal(seed().rolesX,undefined);
    const s=demo();assert.deepEqual(s.rolesX.roles.map(r=>[r.id,r.name,r.base,r.money]),[['C01','Day shift manager','Manager',true],['C02','Night shift manager','Manager',true]]);
    assert.deepEqual(s.users.filter(u=>u.customRole).map(u=>[u.id,u.role,u.customRole]),[['daymgr','Manager','C01'],['nightmgr','Manager','C02']]);
  });
  test('[ADM2] day-shift manager: every Accounting command refused (FORBIDDEN, names the role); no accounting page; rest of Manager kept',()=>{
    const s=demo();
    for(const type of ACC){const r=err(()=>execute(s,'daymgr',{type}));assert.equal(r.code,'FORBIDDEN',type);assert.match(r.msg,/is not part of your role Day shift manager/,type);}
    assert.equal(rolFeatureLevel(s,'daymgr','accounting.ledgers'),'off');   // every switchable Accounting feature off → the module is off
    const p=pages(s,'daymgr');assert.ok(!p.includes('accounting'));assert.ok(p.includes('reports')&&p.includes('folios')&&p.includes('closing'));
    assert.ok(pages(s,'manager').includes('accounting'));
    assert.equal(err(()=>execute(s,'daymgr',{type:'NIGHT_POST'})).code,'OK');
  });
  test('[ADM2] night-shift manager: ACC_MANUAL_SAVE refused (view only); accounting page visible; day-close path passes the role gate',()=>{
    const s=demo();
    const r=err(()=>execute(s,'nightmgr',{type:'ACC_MANUAL_SAVE',date:s.date,voucherType:'Journal',narration:'x',lines:[]}));
    assert.equal(r.code,'FORBIDDEN');assert.match(r.msg,/Manual vouchers & loans \(Accounting\) is view only for your role Night shift manager\./);
    assert.ok(pages(s,'nightmgr').includes('accounting'));
    assert.equal(rolCommandBlock(s,'nightmgr',{type:'DAY_CLOSE'}),undefined);
    assert.match(err(()=>execute(s,'nightmgr',{type:'DAY_CLOSE'})).msg,/pending arrivals/);       // the domain rule, not the role
    assert.equal(err(()=>execute(s,'nightmgr',{type:'NIGHT_POST'})).code,'OK');
  });
  test('[ADM2] ROL_CREATE copies the base; Owner only; Owner never a base; name unique',()=>{
    let s=seed();
    for(const uid of ['manager','techadmin','reception'])assert.equal(err(()=>execute(s,uid,{type:'ROL_CREATE',name:'Desk lead',base:'Reception'})).code,'FORBIDDEN',uid);
    assert.equal(err(()=>execute(s,'owner',{type:'ROL_CREATE',name:'Boss two',base:'Owner'})).code,'FORBIDDEN');
    s=execute(s,'owner',{type:'ROL_CREATE',name:'Desk lead',base:'Reception'});
    const r=s.rolesX.roles[0];assert.equal(r.id,'C01');assert.deepEqual(r.grants,s.grants.Reception);assert.deepEqual(r.template,s.policy.templates.Reception);
    assert.equal(r.version,1);assert.equal(s.policy.version,2);assert.match(s.rolesX.history[0].summary,/C01 Desk lead · created/);
    assert.match(err(()=>execute(s,'owner',{type:'ROL_CREATE',name:'desk LEAD',base:'Cashier'})).msg,/already exists/);
    assert.match(err(()=>execute(s,'owner',{type:'ROL_CREATE',name:'Manager',base:'Cashier'})).msg,/already exists/);
  });
  test('[ADM2] money invariant: a Housekeeper-based role never gains money (create, update, grant, grid)',()=>{
    let s=execute(seed(),'owner',{type:'ROL_CREATE',name:'Floor lead',base:'Housekeeper',money:false});
    assert.match(err(()=>execute(seed(),'owner',{type:'ROL_CREATE',name:'Floor lead',base:'Housekeeper',money:true})).msg,/never holds money/);
    assert.equal(s.rolesX.roles[0].money,false);
    assert.equal(err(()=>execute(s,'owner',{type:'ROL_UPDATE',id:'C01',money:true})).code,'FORBIDDEN');
    const g=err(()=>execute(s,'owner',{type:'ROL_GRANT_SET',id:'C01',perm:'PERM-RPT-002',effect:'allow'}));assert.equal(g.code,'FORBIDDEN');assert.match(g.msg,/never holds money/);
    assert.match(err(()=>execute(s,'owner',{type:'ROL_FEATURE_SET',id:'C01',feature:'cash.expenses',access:'full'})).msg,/No change/); // nothing money comes over
    const r=s.rolesX.roles[0];
    assert.ok(!r.grants.includes('expenses')&&!Object.keys(r.template).some(k=>M.rolIsMoneyPerm(k)));
    s=M.rolSeedDemo(execute(s,'owner',{type:'USER_CREATE',name:'Gita',customRole:'C01'}));const u=s.users.find(x=>x.customRole==='C01');
    assert.equal(u.role,'Housekeeper');assert.equal(can(s,u.id,'payments'),false);assert.equal(authorize(s,u.id,'PERM-RPT-002').decision,'deny');
  });
  test('[ADM2] grid full on a feature the base lacks adds view + act and the module coarse grant, never approve; off removes it',()=>{
    let s=execute(seed(),'owner',{type:'ROL_CREATE',name:'Desk plus',base:'Reception'});
    assert.equal(s.rolesX.roles[0].template['PERM-HK-001'],undefined);
    s=execute(s,'owner',{type:'ROL_FEATURE_SET',id:'C01',feature:'housekeeping.complaints',access:'full'});
    // complaints has PERM-GSV- (no matrix v1 ids) → the module coarse grants come over
    assert.ok(s.rolesX.roles[0].grants.includes('housekeeping'));
    s=execute(s,'owner',{type:'ROL_FEATURE_SET',id:'C01',feature:'frontdesk.reservations',access:'off'});
    const r=s.rolesX.roles[0];assert.ok(!Object.keys(r.template).some(k=>k.startsWith('PERM-RES-')));assert.equal(r.features['frontdesk.reservations'],'off');
    s=execute(s,'owner',{type:'ROL_FEATURE_SET',id:'C01',feature:'frontdesk.reservations',access:'full'});
    assert.deepEqual(Object.keys(s.rolesX.roles[0].template).filter(k=>k.startsWith('PERM-RES-')).sort(),['PERM-RES-001','PERM-RES-002','PERM-RES-003','PERM-RES-004']); // the base copy's own cells
    assert.equal(err(()=>execute(s,'owner',{type:'ROL_FEATURE_SET',id:'C01',feature:'frontdesk.checkin',access:'off'})).code,'VALIDATION'); // core
    // dependants follow off: switching cash.folios is core; revenue.ota off takes revenue.payouts with it
    s=execute(s,'owner',{type:'ROL_FEATURE_SET',id:'C01',feature:'revenue.ota',access:'off'});assert.equal(s.rolesX.roles[0].features['revenue.payouts'],'off');
  });
  test('[ADM2] SoD (DEC-219): discount / approval limits only down; credit-note approve never added; admin delegation refused',()=>{
    let s=execute(seed(),'owner',{type:'ROL_CREATE',name:'Senior manager',base:'Manager'});
    assert.match(err(()=>execute(s,'owner',{type:'ROL_GRANT_SET',id:'C01',perm:'PERM-FOL-003',limit:100001})).msg,/only go down/);
    s=execute(s,'owner',{type:'ROL_GRANT_SET',id:'C01',perm:'PERM-FOL-003',limit:50000});assert.equal(s.rolesX.roles[0].limits['PERM-FOL-003'],50000);
    assert.match(err(()=>execute(s,'owner',{type:'ROL_GRANT_SET',id:'C01',perm:'PERM-FOL-005',effect:'allow'})).msg,/never wider/);
    assert.match(err(()=>execute(s,'owner',{type:'ROL_GRANT_SET',id:'C01',perm:'PERM-ADM-002',effect:'allow'})).msg,/delegation/);
    s=execute(s,'owner',{type:'ROL_GRANT_SET',id:'C01',perm:'PERM-FOL-005',effect:'none'});assert.equal(s.rolesX.roles[0].template['PERM-FOL-005'],undefined);
    s=M.rolSeedDemo(execute(s,'owner',{type:'USER_CREATE',name:'Kiran',customRole:'C01'}));const u=s.users.find(x=>x.customRole==='C01');
    assert.equal(authorize(s,u.id,'PERM-FOL-003',{amount:60000}).decision,'approval');assert.match(authorize(s,u.id,'PERM-FOL-003',{amount:60000}).reason,/Senior manager limit/);
    assert.equal(authorize(s,'manager','PERM-FOL-003',{amount:60000}).decision,'allow');
  });
  test('[ADM2] actor cannot edit own role; built-ins not editable; Tech admin assigns non-money custom roles only',()=>{
    let s=demo();
    for(const id of ['Manager','Owner','Reception'])assert.match(err(()=>execute(s,'owner',{type:'ROL_FEATURE_SET',id,feature:'frontdesk.keys',access:'off'})).msg,/Built-in roles are read-only/);
    const own=structuredClone(s);own.users.find(u=>u.id==='owner').customRole='C01';   // an Owner holding a custom role is refused outright
    assert.equal(err(()=>execute(own,'owner',{type:'ROL_UPDATE',id:'C01',name:'X role'})).code,'FORBIDDEN');
    assert.match(err(()=>execute(s,'daymgr',{type:'ROL_UPDATE',id:'C01',name:'Mine now'})).msg,/Access denied|Only the Owner/);
    assert.match(err(()=>execute(s,'techadmin',{type:'USER_CREATE',name:'Ankit',customRole:'C02'})).msg,/money permissions/);
    s=execute(s,'owner',{type:'ROL_CREATE',name:'Linen runner',base:'Housekeeper'});
    s=execute(s,'techadmin',{type:'USER_CREATE',name:'Ankit',customRole:'C03'});
    assert.deepEqual(s.users.filter(u=>u.customRole==='C03').map(u=>[u.name,u.role]),[['Ankit · Linen runner','Housekeeper']]);
    assert.match(err(()=>execute(s,'daymgr',{type:'USER_UPDATE',id:'daymgr',customRole:'C02'})).msg,/./);
  });
  test('[ADM2] USER_UPDATE moves users between custom and built-in roles; last-owner rule unchanged',()=>{
    let s=demo();
    s=execute(s,'owner',{type:'USER_UPDATE',id:'manager',customRole:'C02'});
    let u=s.users.find(x=>x.id==='manager');assert.deepEqual([u.role,u.customRole,u.name],['Manager','C02','Meera · Night shift manager']);
    s=execute(s,'owner',{type:'USER_UPDATE',id:'manager',role:'Manager'});u=s.users.find(x=>x.id==='manager');assert.deepEqual([u.role,u.customRole,u.name],['Manager',undefined,'Meera · Manager']);
    s=execute(s,'owner',{type:'USER_UPDATE',id:'reception',customRole:'C01'});assert.equal(s.users.find(x=>x.id==='reception').role,'Manager');
    assert.match(err(()=>execute(s,'owner',{type:'USER_UPDATE',id:'owner',customRole:'C01'})).msg,/own access/);
    // last-owner rule unchanged: the only Owner cannot be moved to a custom role by another admin either
    assert.match(err(()=>execute(s,'techadmin',{type:'USER_UPDATE',id:'owner',customRole:'C03'})).msg,/./);
    const t=execute(s,'owner',{type:'ROL_CREATE',name:'Desk runner',base:'Reception',money:false});
    assert.match(err(()=>execute(t,'techadmin',{type:'USER_UPDATE',id:'owner',customRole:'C03'})).msg,/last active owner/);
  });
  test('[ADM2] retire refused while an enabled user holds it; allowed after; retired role cannot be assigned',()=>{
    let s=demo();
    assert.match(err(()=>execute(s,'owner',{type:'ROL_RETIRE',id:'C01'})).msg,/still holds/);
    s=execute(s,'owner',{type:'USER',id:'daymgr'});s=execute(s,'owner',{type:'ROL_RETIRE',id:'C01'});
    assert.equal(rolCustom(s,'C01').retired,true);
    assert.match(err(()=>execute(s,'owner',{type:'USER_UPDATE',id:'reception',customRole:'C01'})).msg,/retired/);
  });
  test('[ADM2] base copy frozen: editing the base template / grants after creation does not reach the custom role',()=>{
    let s=demo();const before=JSON.stringify(rolCustom(s,'C02').template);
    s=execute(s,'owner',{type:'POLICY_SET',role:'Manager',perm:'PERM-RES-001',effect:'none'});
    s=execute(s,'owner',{type:'GRANT',role:'Manager',permission:'reports'});
    assert.equal(JSON.stringify(rolCustom(s,'C02').template),before);assert.ok(rolCustom(s,'C02').grants.includes('reports'));
    assert.equal(authorize(s,'nightmgr','PERM-RES-001').decision,'allow');assert.equal(authorize(s,'manager','PERM-RES-001').decision,'deny');
    // and money back on restores the creation copy, not today's base
    s=execute(s,'owner',{type:'ROL_UPDATE',id:'C02',money:false});assert.ok(!rolCustom(s,'C02').grants.includes('payments'));
    s=execute(s,'owner',{type:'ROL_UPDATE',id:'C02',money:true});assert.ok(rolCustom(s,'C02').grants.includes('payments'));
  });
  test('[ADM2] preview + history; old saves without rolesX load; a v13 save keeps users without customRole',()=>{
    const s=demo();const p=rolPreview(s,'C01');assert.ok(p.hiddenPages.includes('accounting'));assert.deepEqual(p.users,['daymgr']);
    assert.equal(p.features.find(f=>f.id==='accounting.vouchers').level,'off');
    const old=seed();delete old.rolesX;old.schema=14;const back=restoreState(JSON.stringify(old)).state;assert.equal(back.schema,16); /* F8 setup: a v14 save restores to the current SCHEMA 16 */
    assert.equal(back.rolesX,undefined);assert.equal(can(back,'manager','reports'),true);assert.equal(rolFeatureLevel(back,'manager','accounting.vouchers'),'full');
    assert.equal(err(()=>execute(back,'manager',{type:'NIGHT_POST'})).code,'OK');
  });
}
