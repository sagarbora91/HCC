// [ADM2-SYNC] Custom roles on the production client (src/clients/sync/admin.ts, capabilities.ts): the ROL_* commands → CreateRole /
// UpdateRole / SetRoleFeature / SetRoleGrant / RetireRole (bodies as the server wire shapes in 02 Planning/agents/ADM_Briefs.md "Lane S notes"),
// GET /admin/roles → rolesX, GET /admin/policy-history → rolesX.history, /me/capabilities features + roles ["MGR","C01"] → the signed-in
// user's effective pages, and the role-level refusal (FORBIDDEN, details MODULE_OFF + featureId + roleCode + access) shown verbatim.
import {loadSync,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

const grant=(permissionId,mode='A',extra={})=>({permissionId,mode,scope:'P',limit:null,limitUnit:null,...extra});
const role=(roleCode,name,grants,extra={})=>({roleCode,name,credentialKind:'PIN',ownerOnlyAssign:true,denies:[],grants,...extra});
const roles={roles:[
  role('OWN','Owner',['PERM-RES-001','PERM-ADM-001','PERM-ADM-002','PERM-ADM-003','PERM-ADM-004'].map(p=>grant(p)),{credentialKind:'PASSWORD'}),
  role('MGR','Manager',[grant('PERM-RES-002'),grant('PERM-APR-001','L',{limit:100000,limitUnit:'PAISE'})],{isCustom:false,baseRole:null,money:true,features:null,version:1,retired:false}),
  role('C01','Day shift manager',[grant('PERM-RES-002'),grant('PERM-RATE-001','L',{limit:1500,limitUnit:'BPS'})],
    {isCustom:true,baseRole:'MGR',money:false,features:{'accounting.vouchers':'off','compliance.calendar':'view'},version:7,retired:false}),
  role('C02','Old night manager',[grant('PERM-RES-002')],{isCustom:true,baseRole:'MGR',money:true,features:{},version:3,retired:true,retiredUtc:'2026-10-20T04:00:00.000Z'}),
],permissions:[]};
const users=[{userId:1,loginName:'owner',displayName:'Test Owner',enabled:true,credentialKind:'PASSWORD',lockedOut:false,
  roles:[{roleCode:'OWN',validFromUtc:'2026-01-01T00:00:00.000Z',validUntilUtc:null}],version:'VU1'}];
const policyHistory={items:[
  {version:12,roleCode:'C01',action:'ROLE_FEATURE',changedBy:1,changedByName:'Test Owner',changedUtc:'2026-10-24T04:30:00.000Z',summary:'Accounting vouchers set to off',before:'full',after:'off'},
  {version:11,roleCode:'C01',action:'ROLE_CREATE',changedBy:1,changedByName:'Test Owner',changedUtc:'2026-10-24T04:00:00.000Z',summary:'Created Day shift manager (base MGR)',before:null,after:'{"base":"MGR"}'}]};
const EMPTY={page:1,pageSize:100,total:0,items:[]};
const SRV_TEXT='Chart, ledgers & opening balances (Accounting) is not part of your role Day shift manager.';

export async function register({test,assert,M}){
  const S=await loadSync();
  const mine={areas:S.AREAS.filter(a=>['core','reservations','frontdesk','admin'].includes(a.id))};
  let rolesReply=()=>roles,histReply=()=>policyHistory,cmdReply=()=>({roleCode:'C03',version:1,policyVersion:13});
  const base={
    'GET /admin/users':()=>({items:users,page:1,pageSize:100,total:1}),'GET /admin/roles':()=>rolesReply(),'GET /admin/policy-history':()=>histReply(),
    'GET /admin/exceptions':()=>({items:[]}),'GET /admin/audit':()=>EMPTY,
    'GET /configuration/values':()=>({businessDate:'2026-10-24',values:{hotel:'Sand Residency'},scheduled:[]}),'GET /configuration/history':()=>EMPTY,'GET /configuration/requests':()=>({items:[]}),
  };
  const srv=fakeServer({...base,...Object.fromEntries(['CreateRole','UpdateRole','SetRoleFeature','SetRoleGrant','RetireRole'].map(n=>['POST /commands/'+n,()=>cmdReply()]))});
  const rt=await startRuntime(S,srv,mine);const st0=rt.get().state;

  // ── (2) GET /admin/roles ──
  const c01=st0.rolesX?.roles.find(r=>r.id==='C01');
  const histCall=srv.gets('/admin/policy-history')[0]; // recorded at boot, before any command resets the log
  test('[ADM2-SYNC] GET /admin/roles: a custom role lands in rolesX (base, money, features off/view, version, retired); a built-in template is untouched',()=>{
    assert.deepEqual(rt.get().errors,{});
    assert.deepEqual(st0.rolesX.roles.map(r=>r.id).sort(),['C01','C02'],'built-ins OWN / MGR are not custom rows');
    assert.deepEqual([c01.name,c01.base,c01.money,c01.version,!!c01.retired],['Day shift manager','Manager',false,7,false]);
    assert.deepEqual(c01.features,{'accounting.vouchers':'off','compliance.calendar':'view'});
    assert.equal(c01.limits['PERM-RATE-001'],15,'BPS → percent');
    assert.deepEqual([...c01.grants].sort(),['reservations'].sort());
    const c02=st0.rolesX.roles.find(r=>r.id==='C02');
    assert.deepEqual([c02.retired,c02.money,c02.version],[true,true,3]);
    assert.ok(st0.policy.templates.Manager['PERM-RES-002'],'the Manager template is still in the policy');
    assert.equal(st0.policy.templates.Manager['PERM-APR-001'].effect,'limit');
    assert.equal(st0.policy.templates['C01'],undefined,'custom roles do not become policy templates');
    assert.equal(rt.ids.version('role','C01'),'7');
  });

  // ── (4) GET /admin/policy-history ──
  test('[ADM2-SYNC] GET /admin/policy-history: items land in rolesX.history newest first, with the role code and the audit action',()=>{
    const h=st0.rolesX.history;
    assert.deepEqual(h.map(x=>x.version),[12,11]);
    assert.deepEqual([h[0].actor,h[0].summary,h[0].before,h[0].after,h[0].reason],['Test Owner','C01 · Accounting vouchers set to off','full','off','ROLE_FEATURE']);
    assert.deepEqual([h[1].before,h[1].after],['—','{"base":"MGR"}'],'null before → dash');
    assert.deepEqual(histCall.query,{take:'50'});
  });
  const boot=async over=>{try{const r=await startRuntime(S,fakeServer({...base,...over}),mine);return {state:r.get().state,errors:r.get().errors};}catch(error){return {error};}};
  const h403=await boot({'GET /admin/policy-history':()=>reply(403,{code:'FORBIDDEN',message:'No permission.'})});
  const h404=await boot({'GET /admin/policy-history':()=>reply(404,{code:'NOT_FOUND',message:'No such route.'})});
  const h500=await boot({'GET /admin/policy-history':()=>reply(500,{code:'UNAVAILABLE',message:'boom'})});
  test('[ADM2-SYNC] a failed policy-history read (refused, or an older server without the route) leaves the history empty, the roles still load, nothing throws',()=>{
    for(const [n,x] of [['403',h403],['404',h404]]){
      assert.ok(!x.error&&x.state,n+' boot threw: '+(x.error&&x.error.stack));
      assert.deepEqual(x.state.rolesX.history,[],n);assert.deepEqual(x.state.rolesX.roles.map(r=>r.id).sort(),['C01','C02'],n);
      assert.deepEqual(x.errors,{},n+' is not reported as a load error');}
  });
  test('[ADM2-SYNC] a server error on the history read does not throw out of boot and invents no history',()=>{
    assert.ok(!h500.error,'boot threw: '+(h500.error&&h500.error.stack));
    assert.deepEqual(h500.state?.rolesX?.history??[],[]);
  });

  // ── (1) commands ──
  const send=async(cmd,name)=>{srv.reset();const r=await rt.run(cmd);return {r,body:srv.commands(name)[0]?.body,key:srv.commands(name)[0]?.key,n:srv.commands(name).length,calls:srv.calls.map(c=>`${c.method} ${c.path}`)};};
  const created=await send({type:'ROL_CREATE',name:'  Weekend lead  ',base:'Reception',money:false,key:'rol:c1'},'CreateRole');
  const createdNoMoney=await send({type:'ROL_CREATE',name:'Floor lead',base:'Manager',key:'rol:c2'},'CreateRole');
  const upd=await send({type:'ROL_UPDATE',id:'C01',name:' Day manager ',money:true,key:'rol:u1'},'UpdateRole');
  const feat=await send({type:'ROL_FEATURE_SET',id:'C01',feature:'accounting.gst',access:'off',key:'rol:f1'},'SetRoleFeature');
  const gAllow=await send({type:'ROL_GRANT_SET',id:'C01',perm:'PERM-RES-002',effect:'allow',mode:'View',key:'rol:g1'},'SetRoleGrant');
  const gView=await send({type:'ROL_GRANT_SET',id:'C01',perm:'PERM-RES-002',effect:'allow',mode:'view',scope:'S',key:'rol:g2'},'SetRoleGrant');
  const gNone=await send({type:'ROL_GRANT_SET',id:'C01',perm:'PERM-RES-002',effect:'none',key:'rol:g3'},'SetRoleGrant');
  const gLimit=await send({type:'ROL_GRANT_SET',id:'C01',perm:'PERM-RATE-001',limit:12,key:'rol:g4'},'SetRoleGrant');
  const gLimitPaise=await send({type:'ROL_GRANT_SET',id:'C01',perm:'PERM-APR-001',limit:50000,key:'rol:g5'},'SetRoleGrant');
  const gDeny=await send({type:'ROL_GRANT_SET',id:'C01',perm:'PERM-RES-002',effect:'deny',key:'rol:g6'},'SetRoleGrant');
  const retire=await send({type:'ROL_RETIRE',id:'C01',key:'rol:r1'},'RetireRole');
  const noRole=await send({type:'ROL_RETIRE',id:'C99',key:'rol:r2'},'RetireRole');
  const badBase=await send({type:'ROL_CREATE',name:'Boss',base:'Owner',key:'rol:c3'},'CreateRole');
  const badAccess=await send({type:'ROL_FEATURE_SET',id:'C01',feature:'accounting.gst',access:'FULL!',key:'rol:f2'},'SetRoleFeature');

  test('[ADM2-SYNC] ROL_CREATE → POST /commands/CreateRole {name trimmed, baseRole code, money only when given}; the new role code is the result id; roles reload',()=>{
    assert.ok(created.r.ok,created.r.text);
    assert.deepEqual(created.body,{name:'Weekend lead',baseRole:'REC',money:false});assert.equal(created.key,'rol:c1');
    assert.deepEqual(created.r.result,{id:'C03'});
    assert.ok(created.calls.indexOf('GET /admin/roles')>created.calls.indexOf('POST /commands/CreateRole'),created.calls.join(', '));
    assert.deepEqual(createdNoMoney.body,{name:'Floor lead',baseRole:'MGR'},'money omitted → the server default');
    assert.deepEqual([badBase.r.ok,badBase.r.reason,badBase.n],[false,'precheck',0],'Owner is never a base');
  });
  test('[ADM2-SYNC] ROL_UPDATE → UpdateRole {roleCode, trimmed name, money, expectedVersion = the loaded role version as a whole number}',()=>{
    assert.ok(upd.r.ok,upd.r.text);
    assert.deepEqual(upd.body,{roleCode:'C01',name:'Day manager',money:true,expectedVersion:7});
    assert.equal(typeof upd.body.expectedVersion,'number');assert.ok(Number.isInteger(upd.body.expectedVersion));
    assert.deepEqual([noRole.r.ok,noRole.r.reason,noRole.n],[false,'precheck',0]);
  });
  test('[ADM2-SYNC] ROL_FEATURE_SET → SetRoleFeature {roleCode, featureId, access lower-case, expectedVersion}',()=>{
    assert.ok(feat.r.ok,feat.r.text);
    assert.deepEqual(feat.body,{roleCode:'C01',featureId:'accounting.gst',access:'off',expectedVersion:7});
    assert.deepEqual([badAccess.r.reason,badAccess.n],['precheck',0],'only off / view / full');
  });
  test('[ADM2-SYNC] ROL_GRANT_SET → SetRoleGrant: lower-case mode letters A / V, scope from the cell, mode null removes (no scope), limits in BPS (percent × 100) or paise, deny refused on the client',()=>{
    assert.ok(gAllow.r.ok,gAllow.r.text);
    assert.deepEqual(gAllow.body,{roleCode:'C01',expectedVersion:7,permissionId:'PERM-RES-002',mode:'A',scope:'P'},'allow with an unknown mode word → A');
    assert.deepEqual(gView.body,{roleCode:'C01',expectedVersion:7,permissionId:'PERM-RES-002',mode:'V',scope:'S'});
    assert.deepEqual(gNone.body,{roleCode:'C01',expectedVersion:7,permissionId:'PERM-RES-002',mode:null,scope:null},'mode null removes the cell');
    assert.deepEqual(gLimit.body,{roleCode:'C01',expectedVersion:7,permissionId:'PERM-RATE-001',mode:'L',scope:'P',limit:1200,limitUnit:'BPS'});
    assert.deepEqual(gLimitPaise.body,{roleCode:'C01',expectedVersion:7,permissionId:'PERM-APR-001',mode:'L',scope:'P',limit:50000,limitUnit:'PAISE'});
    assert.deepEqual([gDeny.r.ok,gDeny.r.reason,gDeny.n],[false,'precheck',0]);assert.match(gDeny.r.text,/Deny cells are not set on a custom role/);
  });
  test('[ADM2-SYNC] ROL_RETIRE → RetireRole {roleCode, expectedVersion}; every role command reloads rolesX, users, policy',()=>{
    assert.ok(retire.r.ok,retire.r.text);
    assert.deepEqual(retire.body,{roleCode:'C01',expectedVersion:7});
    for(const x of [created,upd,feat,gAllow,retire]){
      assert.ok(x.calls.includes('GET /admin/roles')&&x.calls.includes('GET /admin/users')&&x.calls.includes('GET /admin/policy-history'),x.calls.join(', '));}
  });

  // STALE_VERSION: another admin changed the role since this device loaded it.
  rolesReply=()=>roles;
  srv.on('POST /commands/UpdateRole',()=>reply(409,{code:'STALE_VERSION',message:'Record changed by someone else. Reload and try again.'}));
  srv.reset();const stale=await rt.run({type:'ROL_UPDATE',id:'C01',name:'Day mgr',key:'rol:u2'});
  const protoStale=(()=>{try{throw new M.HccError('STALE_VERSION','Record changed by someone else. Reload and try again.');}catch(e){return `${M.errorTitle[e.code]}: ${e.message}`;}})();
  const staleCalls=srv.commands('UpdateRole').length; // before the next section resets the log
  test('[ADM2-SYNC] STALE_VERSION on a role command shows the prototype stale-version error ("Record changed: …"), once, without retry',()=>{
    assert.deepEqual([stale.ok,stale.reason],[false,'server']);
    assert.equal(stale.error.wireCode,'STALE_VERSION');
    assert.equal(stale.text,protoStale);assert.match(stale.text,/^Record changed: Record changed by someone else/);
    assert.equal(staleCalls,1);
  });

  // ── (5) role-level refusal ──
  srv.on('POST /commands/SetRoleFeature',()=>reply(403,{code:'FORBIDDEN',message:SRV_TEXT,details:{reason:'MODULE_OFF',featureId:'accounting.ledgers',roleCode:'C01',access:'off'}}));
  srv.reset();const refused=await rt.run({type:'ROL_FEATURE_SET',id:'C01',feature:'accounting.gst',access:'view',key:'rol:f3'});
  const refusedCalls=srv.commands('SetRoleFeature').length;
  test('[ADM2-SYNC] a role-level refusal (FORBIDDEN, MODULE_OFF + featureId + roleCode + access) is shown with the server message verbatim and keeps its details',()=>{
    assert.deepEqual([refused.ok,refused.reason],[false,'server']);
    assert.equal(refused.text,`Not permitted: ${SRV_TEXT}`);
    assert.deepEqual([refused.error.wireCode,refused.error.details.reason,refused.error.details.featureId,refused.error.details.roleCode,refused.error.details.access],
      ['FORBIDDEN','MODULE_OFF','accounting.ledgers','C01','off']);
    assert.equal(refusedCalls,1,'not retried');
  });

  // ── (3) /me/capabilities ──
  const meSrv=fakeServer({...base,'GET /me/capabilities':()=>({...capabilities({userId:5,displayName:'Tanvi',roles:['MGR','C01']}),
    features:{'accounting.vouchers':'off','accounting.gst':'off','accounting.tds':'off','accounting.tally':'off','compliance.calendar':'view','x.unknown':'FULL'}})});
  const meRt=await startRuntime(S,meSrv,mine);const ms=meRt.get().state;const me=meRt.get().me??ms.users.find(u=>u.id==='5');
  const pageOn=p=>M.cfgPageOnFor(ms,'5',p);
  test('[ADM2-SYNC] /me/capabilities features + roles ["MGR","C01"]: the signed-in user keeps the Manager base role, holds C01 with the off / view levels (full dropped)',()=>{
    assert.deepEqual(meRt.get().errors,{});
    const u=ms.users.find(x=>x.id==='5');
    assert.deepEqual([u.role,u.customRole],['Manager','C01']);
    const row=ms.rolesX.roles.find(r=>r.id==='C01');
    assert.deepEqual(row.features,{'accounting.vouchers':'off','accounting.gst':'off','accounting.tds':'off','accounting.tally':'off','compliance.calendar':'view'});
    assert.equal(row.name,'Day shift manager','the loaded row keeps its name');assert.equal(row.base,'Manager');
    void me;
  });
  test('[ADM2-SYNC] the user\'s effective page list drops the pages of an off feature (every switchable accounting feature off → Accounting hidden) and keeps the base role\'s landing and other pages',()=>{
    assert.equal(pageOn('accounting'),false,'accounting.* all off → the module (its core page) is off for the role');
    assert.equal(M.rolPageLevel(ms,'5','compliance'),'view');assert.equal(pageOn('compliance'),true,'view keeps the page');
    for(const p of ['overview','rooms','reservations'])assert.equal(pageOn(p),true,p+' stays (base role landing)');
    assert.equal(ms.users.find(x=>x.id==='5').role,'Manager','landing follows the base role');
  });
}
