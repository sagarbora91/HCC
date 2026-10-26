// W4-5 admin area (src/clients/sync/admin.ts): users, role templates → fine-grained policy + module gates, user exceptions,
// configuration requests (approvals), audit, Master configuration values / history / modules — loaders and command mappers on a fake
// server shaped like the .NET DTOs (Hcc.Contracts.Access, Hcc.Contracts.Configuration), checked against the prototype's own rules.
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

const roles={roles:[
  {roleCode:'OWN',name:'Owner',credentialKind:'PASSWORD',ownerOnlyAssign:true,denies:[],grants:[
    ...['PERM-RES-001','PERM-RES-002','PERM-STY-001','PERM-STY-003','PERM-FOL-002','PERM-FOL-003','PERM-PAY-001','PERM-HK-001','PERM-HK-004','PERM-EXP-001','PERM-APR-001',
      'PERM-CLS-002','PERM-RPT-001','PERM-ADM-001','PERM-ADM-002','PERM-ADM-003','PERM-ADM-004'].map(permissionId=>({permissionId,mode:'A',scope:'P',limit:null,limitUnit:null}))]},
  {roleCode:'MGR',name:'Manager',credentialKind:'PIN',ownerOnlyAssign:true,denies:[],grants:[
    {permissionId:'PERM-RES-002',mode:'A',scope:'P',limit:null,limitUnit:null},{permissionId:'PERM-APR-001',mode:'L',scope:'P',limit:100000,limitUnit:'PAISE'},
    {permissionId:'PERM-RATE-001',mode:'L',scope:'P',limit:1500,limitUnit:'BPS'},{permissionId:'PERM-FOL-003',mode:'L',scope:'P',limit:100000,limitUnit:'PAISE'}]},
  {roleCode:'REC',name:'Reception',credentialKind:'PIN',ownerOnlyAssign:true,denies:[],grants:[
    {permissionId:'PERM-RES-001',mode:'A',scope:'P',limit:null,limitUnit:null},{permissionId:'PERM-RES-002',mode:'A',scope:'P',limit:null,limitUnit:null},
    {permissionId:'PERM-FOL-003',mode:'L',scope:'P',limit:25000,limitUnit:'PAISE'},{permissionId:'PERM-PAY-001',mode:'A',scope:'S',limit:null,limitUnit:null},
    {permissionId:'PERM-FOL-005',mode:'R',scope:'P',limit:null,limitUnit:null},{permissionId:'PERM-GST-001',mode:'A',scope:'P',limit:null,limitUnit:null}]},
  {roleCode:'HKP',name:'Housekeeper',credentialKind:'PIN',ownerOnlyAssign:false,denies:[],grants:[{permissionId:'PERM-HK-001',mode:'A',scope:'A',limit:null,limitUnit:null}]},
  {roleCode:'TAD',name:'Tech admin',credentialKind:'PASSWORD',ownerOnlyAssign:true,grants:[
    {permissionId:'PERM-ADM-001',mode:'A',scope:'P',limit:null,limitUnit:null},{permissionId:'PERM-ADM-002',mode:'R',scope:'P',limit:null,limitUnit:null},{permissionId:'PERM-ADM-003',mode:'V',scope:'P',limit:null,limitUnit:null}],
    denies:[{permissionId:'PERM-FOL-003',reason:'Technical and financial power are kept separate'}]},
],permissions:[{permissionId:'PERM-FOL-003',resourceAction:'folio.discount',kind:'ACTION',isMoney:true}]};
const users=[
  {userId:1,loginName:'owner',displayName:'Test Owner',enabled:true,credentialKind:'PASSWORD',lockedOut:false,roles:[{roleCode:'OWN',validFromUtc:'2026-01-01T00:00:00.000Z',validUntilUtc:null}],version:'VU1'},
  {userId:2,loginName:'rohan',displayName:'Rohan',enabled:true,credentialKind:'PIN',lockedOut:false,roles:[{roleCode:'REC',validFromUtc:'2026-10-01T00:00:00.000Z',validUntilUtc:'2026-11-30T18:30:00.000Z'}],version:'VU2'},
  {userId:3,loginName:'lata',displayName:'Lata',enabled:true,credentialKind:'PASSWORD',lockedOut:false,roles:[{roleCode:'TAD',validFromUtc:'2026-01-01T00:00:00.000Z',validUntilUtc:null}],version:'VU3'},
  {userId:4,loginName:'asha',displayName:'Asha',enabled:false,credentialKind:'PIN',lockedOut:false,roles:[{roleCode:'HKP',validFromUtc:'2026-01-01T00:00:00.000Z',validUntilUtc:'2026-10-10T18:30:00.000Z'}],version:'VU4'},
];
const exceptions={items:[{exceptionId:7,userId:2,permissionId:'PERM-PAY-003',effect:'ALLOW',mode:'A',scope:'P',limit:null,limitUnit:null,reason:'Covering cashier desk this weekend',
  validUntilUtc:'2026-10-25T18:30:00.000Z',grantedBy:1,grantedUtc:'2026-10-24T04:00:00.000Z',revokedUtc:null}]};
const audit={page:1,pageSize:100,total:2,items:[
  {auditId:902,atUtc:'2026-10-24T04:20:00.000Z',principalId:1,actor:'Test Owner',action:'EXCEPTION_GRANT',target:'user:2',detail:'ALLOW PERM-PAY-003',operation:'GrantException'},
  {auditId:901,atUtc:'2026-10-24T04:00:00.000Z',principalId:1,actor:'Test Owner',action:'USER_CREATE',target:'user:2',detail:"Created 'rohan' with roles REC.",operation:'CreateUser'}]};
const values={businessDate:'2026-10-24',values:{hotel:'Sand Residency',checkin:'13:00',checkout:'11:00',discountLimit:25000,approvalExpiryHours:24,inspection:true,invoicePrefix:'INV',
  'brand.gstin':'27DEMO0000X0Z05','fd.holdHours':6,'module.pos':false,'module.ota':true,'sec.pinLock':false},
  scheduled:[{key:'discountLimit',value:60000,effectiveFrom:'2026-10-30'}]};
const history={page:1,pageSize:100,total:2,items:[
  {changeId:12,key:'discountLimit',label:'Discount threshold',before:'₹250.00',after:'₹600.00',effectiveFrom:'2026-10-30',status:'scheduled',changedBy:'Test Owner',approvedBy:null,reason:'Season',source:'hub',revertOf:null,at:'2026-10-24T04:10:00.000Z',canRevert:false,revertBlock:'Not in effect yet — withdraw it instead.'},
  {changeId:11,key:'checkin',label:'Standard check-in time',before:'14:00',after:'13:00',effectiveFrom:'2026-10-24',status:'applied',changedBy:'Test Owner',approvedBy:null,reason:null,source:'hub',revertOf:null,at:'2026-10-24T04:05:00.000Z',canRevert:true,revertBlock:null}]};
const requests={items:[{requestId:5,key:'sec.pinLock',label:'PIN lock on shared devices',summary:'Master configuration · PIN lock on shared devices · Off → On',reason:'Shared tablet',approver:'Owner',
  requestedBy:'Lata',requestedAt:'2026-10-24T03:30:00.000Z',expiresAt:'2026-10-25T03:30:00.000Z',status:'Pending',decidedBy:null,decisionReason:null,effectiveFrom:'2026-10-24',version:'RQ5',decideBlock:null}]};

export async function register({test,assert,M}){
  const S=await loadSync();
  // Only this area (+ core, reservations, frontdesk: bookings needs guests): other W4 areas load their own routes, which this fake server does not serve.
  const mine={areas:S.AREAS.filter(a=>['core','reservations','frontdesk','admin'].includes(a.id))};
  let userVer=2;
  const srv=fakeServer({
    'GET /admin/users':()=>({items:users,page:1,pageSize:100,total:users.length}),
    'GET /admin/roles':()=>roles,'GET /admin/exceptions':()=>exceptions,'GET /admin/audit':()=>audit,
    'GET /configuration/values':()=>values,'GET /configuration/history':()=>history,'GET /configuration/requests':()=>requests,
    'POST /commands/SetUserEnabled':({body})=>({userId:body.userId,version:'V'+(++userVer)}),
    'POST /commands/CreateUser':()=>({userId:9,version:'VU9'}),
    'POST /commands/AssignRole':({body})=>({userId:body.userId,version:'AFTER-ASSIGN'}),
    'POST /commands/RevokeRole':({body})=>({userId:body.userId,version:'AFTER-REVOKE'}),
    'POST /commands/GrantException':({body})=>({exceptionId:8,userId:body.userId,version:'VX'}),
    'POST /commands/RevokeException':({body})=>({exceptionId:body.exceptionId,userId:body.userId,version:'VX2'}),
    'POST /commands/SetSetting':({body})=>({status:'applied',key:body.key,changeId:13,requestId:null,effectiveFrom:'2026-10-24',before:'13:00',after:'12:30',version:'S1',message:'ok'}),
    'POST /commands/DecideSettingRequest':({body})=>({requestId:body.requestId,status:'Executed',key:'sec.pinLock',changeId:14,effectiveFrom:'2026-10-24',message:'ok'}),
    'POST /commands/WithdrawScheduledSetting':({body})=>({changeId:body.changeId,key:'discountLimit',effectiveFrom:'2026-10-30',message:'ok'}),
    'POST /commands/RevertSetting':({body})=>({status:'applied',key:'checkin',changeId:15,requestId:null,effectiveFrom:'2026-10-24',before:'13:00',after:'14:00',version:'S2',message:'ok'}),
    'POST /commands/ImportConfiguration':()=>({changed:1,unchanged:3,skipped:[],message:'Configuration imported'}),
    'POST /commands/SetCredential':({body})=>({userId:body.userId,version:'VC'}),
    'POST /commands/RegisterDevice':()=>({deviceId:3,deviceToken:'tok-once'}),
  });
  const rt=await startRuntime(S,srv,mine);const st=rt.get().state;const bootErrors=rt.get().errors;
  const proto=M.seed();

  test('[W4-5 admin] Loaders: users (role, expiry, status), role templates → policy cells, limits (BPS → %), denies, exceptions, module gates',()=>{
    assert.deepEqual(bootErrors,{});
    assert.deepEqual([...st.users].sort((a,b)=>a.id-b.id).map(u=>[u.id,u.name,u.role,u.enabled,u.expiresOn]),[['1','Test Owner','Owner',true,undefined],['2','Rohan','Reception',true,'2026-11-30'],
      ['3','Lata','Tech admin',true,undefined],['4','Asha','Housekeeper',false,'2026-10-10']]);
    assert.deepEqual(['1','2','3','4'].map(id=>M.adminUserStatus(st,st.users.find(u=>u.id===id))),['Enabled','Enabled','Enabled','Disabled']);
    assert.equal(rt.ids.version('user','2'),'VU2');
    const p=st.policy;
    // Same limits as the prototype's RBAC matrix v1 (Manager approval ₹1,000, rate override 15 %).
    assert.equal(p.limits.Manager['PERM-APR-001'],proto.policy.limits.Manager['PERM-APR-001']);
    assert.equal(p.limits.Manager['PERM-RATE-001'],proto.policy.limits.Manager['PERM-RATE-001']);
    assert.deepEqual(p.templates.Reception['PERM-PAY-001'],proto.policy.templates.Reception['PERM-PAY-001'],'A·S');
    assert.deepEqual(p.templates['Tech admin']['PERM-ADM-003'],{effect:'allow',scope:'P',mode:'view'});
    assert.deepEqual(p.templates['Tech admin']['PERM-FOL-003'],{effect:'deny',scope:'P'});
    assert.deepEqual(p.exceptions,[{id:'7',user:'2',perm:'PERM-PAY-003',effect:'allow',reason:'Covering cashier desk this weekend',expires:'2026-10-25',by:'1',at:'2026-10-24T09:30'}]);
    assert.deepEqual([...st.grants.Reception].sort(),['payments','reservations'],'PERMISSION_MAP: RES-001/002 → reservations, PAY-001 → payments; FOL-003/005 are not "billing" (FOL-002)');
  });
  test('[W4-5 admin] authorize() on server data gives the prototype decisions (DEC-040: limit → approval, request only, explicit deny, exception allow)',()=>{
    const d=(s,uid,perm,ctx)=>M.authorize(s,uid,perm,ctx).decision;
    assert.deepEqual([d(st,'2','PERM-FOL-003',{amount:30000}),d(st,'2','PERM-FOL-003',{amount:20000}),d(st,'2','PERM-FOL-005'),d(st,'3','PERM-FOL-003'),d(st,'2','PERM-PAY-003'),d(st,'2','PERM-ADM-001')],
      ['approval','allow','approval','deny','allow','deny']);
    assert.deepEqual([d(proto,'reception','PERM-FOL-003',{amount:30000}),d(proto,'reception','PERM-FOL-003',{amount:20000}),d(proto,'reception','PERM-FOL-005'),d(proto,'reception','PERM-ADM-001')],
      ['approval','allow','approval','deny']);
  });
  test('[W4-5 admin] Approvals (configuration requests), audit, Master configuration values, modules, dated values, history and revert state',()=>{
    assert.deepEqual(st.approvals.map(a=>[a.id,a.kind,a.status,a.actor,a.target,a.expiresAt,a.meta.command,a.meta.approver]),
      [['CFG-REQ-5','config','Pending','3','sec.pinLock','2026-10-25T09:00','CFG_DECIDE','Owner']]);
    assert.equal(M.cfgDecideBlock(st,'1','CFG-REQ-5'),undefined,'Owner may decide the Tech admin request');
    assert.match(M.cfgDecideBlock(st,'3','CFG-REQ-5'),/You raised this request/);
    assert.equal(M.cfgPendingRequests(st,'sec.pinLock').length,1);
    assert.deepEqual(st.audit.map(a=>[a.id,a.at,a.actor,a.action]),[['902','2026-10-24T09:50','Test Owner','EXCEPTION_GRANT'],['901','2026-10-24T09:30','Test Owner','USER_CREATE']]);
    assert.deepEqual([st.settings.hotel,st.settings.checkin,st.settings.discountLimit,st.settings.inspection,st.settings.invoicePrefix],['Sand Residency','13:00',25000,true,'INV']);
    assert.deepEqual(st.configX.modulesOff,['pos']);assert.equal(M.cfgModuleOn(st,'pos'),false);assert.equal(M.cfgPageOn(st,'pos'),false);
    assert.equal(M.cfgValueOn(st,'brand.gstin'),'27DEMO0000X0Z05');
    assert.deepEqual(M.cfgScheduled(st,'discountLimit').map(x=>[x.id,x.value,x.effectiveFrom]),[['12',60000,'2026-10-30']]);
    assert.equal(M.cfgValueOn(st,'discountLimit','2026-10-30'),60000);
    assert.equal(M.cfgRevertBlock(st,'11'),undefined,'latest applied change is revertible (server canRevert)');
    assert.ok(M.cfgRevertBlock(st,'12'));
    assert.deepEqual(M.cfgHistory(st,'checkin').map(h=>[h.id,h.before,h.after,h.actor]),[['11','14:00','13:00','Test Owner']]);
    assert.equal(M.cfgDisplay(st,'1',M.cfgDef(st,'discountLimit')),M.cfgShow(M.cfgDef(proto,'discountLimit'),25000));
  });

  // ── Commands ──
  const res={};const calls={};
  const go=async(name,cmd)=>{srv.reset();res[name]=await rt.run(cmd);calls[name]=srv.calls.map(c=>`${c.method} ${c.path}`);};
  await go('disable',{type:'USER',id:'2',key:'user:2:true:0'});
  await go('create',{type:'USER_CREATE',name:'Rohan Patil',role:'Cashier',expiresOn:'2026-11-30',key:'new:u1'});
  await go('role',{type:'USER_UPDATE',id:'2',role:'Cashier',key:'edit:u2'});
  await go('rename',{type:'USER_UPDATE',id:'2',name:'Rohan P',key:'edit:u2b'});
  await go('self',{type:'USER',id:'1',key:'user:1'});
  await go('excAdd',{type:'USER_EXCEPTION',user:'2',perm:'PERM-RPT-003',effect:'deny',reason:'Exports restricted',expires:'2026-11-30',key:'exc:1'});
  await go('excRm',{type:'USER_EXCEPTION',remove:'7',key:'exc-remove:7'});
  await go('set',{type:'SETTING_SET',setting:'checkin',value:'12:30',reason:'Winter',key:'cfg:1'});
  await go('dated',{type:'SETTING_SET',setting:'discountLimit',value:70000,effectiveFrom:'2026-11-01',reason:'',key:'cfg:2'});
  await go('module',{type:'SETTING_SET',setting:'module.pos',value:true,key:'cfg-mod:pos:0'});
  await go('decide',{type:'CFG_DECIDE',id:'CFG-REQ-5',approve:true,key:'approval:CFG-REQ-5'});
  await go('withdraw',{type:'CFG_WITHDRAW',id:'12',key:'cfg-wd:12'});
  await go('revert',{type:'CFG_REVERT',id:'11',key:'cfg-rv:11'});
  await go('import',{type:'CFG_IMPORT',config:{format:'hcc-master-configuration',version:1,values:{hotel:'X'}},key:'imp:1'});
  await go('pin',{type:'USER_CREDENTIAL',id:'2',pin:'482913',key:'cred:2'});
  await go('device',{type:'DEVICE_REGISTER',label:'Front desk tablet',key:'dev:1'});
  await go('grant',{type:'GRANT',role:'Reception',permission:'expenses'});
  await go('policy',{type:'POLICY_SET',role:'Reception',perm:'PERM-FOL-003',limit:30000});
  const cmd=(name,i=0)=>srv.calls.filter(c=>c.method==='POST'&&c.path==='/commands/'+name)[i];
  // (srv.calls was reset per action; re-run lookups use the per-action capture below)
  void cmd;
  test('[W4-5 admin] User commands: SetUserEnabled / CreateUser / AssignRole → RevokeRole with expectedVersion chain; self-change and rename refused on the client',()=>{
    for(const k of ['disable','create','role','excAdd','excRm','set','dated','module','decide','withdraw','revert','import','pin','device'])assert.ok(res[k].ok,k+': '+res[k].text);
    assert.deepEqual(calls.disable,['POST /commands/SetUserEnabled','GET /admin/users','GET /admin/roles','GET /admin/exceptions']);
    assert.deepEqual(calls.role.slice(0,2),['POST /commands/AssignRole','POST /commands/RevokeRole']);
    assert.deepEqual(calls.create[0],'POST /commands/CreateUser');
    assert.deepEqual([res.rename.ok,res.rename.reason],[false,'precheck']);assert.match(res.rename.text,/Renaming/);
    assert.deepEqual([res.self.ok,res.self.reason],[false,'precheck']);
    assert.deepEqual(res.create.result,{userId:'9',loginName:'rohan.patil',credentialNeeded:true});
    assert.deepEqual([res.grant.reason,res.policy.reason],['unmapped','unmapped']);
    assert.match(res.grant.text,/Not available on the server yet: GRANT/);
    assert.deepEqual(res.device.result,{deviceId:'3',deviceToken:'tok-once'});
    assert.deepEqual(res.import.result.changed,1);
  });

  // Bodies and keys (a second runtime records every request without resets).
  const srv2=fakeServer(Object.fromEntries(Object.entries({
    'GET /admin/users':()=>({items:users,page:1,pageSize:100,total:users.length}),'GET /admin/roles':()=>roles,'GET /admin/exceptions':()=>exceptions,'GET /admin/audit':()=>audit,
    'GET /configuration/values':()=>values,'GET /configuration/history':()=>history,'GET /configuration/requests':()=>requests,
    'POST /commands/SetUserEnabled':({body})=>({userId:body.userId,version:'VZ'}),'POST /commands/CreateUser':()=>({userId:9,version:'VU9'}),
    'POST /commands/AssignRole':({body})=>({userId:body.userId,version:'AFTER-ASSIGN'}),'POST /commands/RevokeRole':({body})=>({userId:body.userId,version:'AFTER-REVOKE'}),
    'POST /commands/GrantException':({body})=>({exceptionId:8,userId:body.userId,version:'VX'}),'POST /commands/RevokeException':({body})=>({exceptionId:body.exceptionId,userId:body.userId,version:'VX2'}),
    'POST /commands/SetSetting':()=>({status:'applied'}),'POST /commands/DecideSettingRequest':()=>({status:'Executed'}),'POST /commands/WithdrawScheduledSetting':()=>({}),
    'POST /commands/RevertSetting':()=>({}),'POST /commands/SetCredential':()=>({userId:2,version:'VC'}),
  })));
  const rt2=await startRuntime(S,srv2,mine);
  await rt2.run({type:'USER',id:'2',key:'user:2:true:0'});
  await rt2.run({type:'USER_CREATE',name:'Rohan Patil',role:'Cashier',expiresOn:'2026-11-30',key:'new:u1'});
  await rt2.run({type:'USER_UPDATE',id:'4',role:'Supervisor',enabled:true,key:'edit:u4'});
  await rt2.run({type:'USER_UPDATE',id:'2',expiresOn:'',key:'edit:u2-exp'});
  await rt2.run({type:'USER_EXCEPTION',user:'2',perm:'PERM-RPT-003',effect:'deny',reason:'Exports restricted',expires:'2026-11-30',key:'exc:1'});
  await rt2.run({type:'USER_EXCEPTION',remove:'7',key:'exc-remove:7'});
  await rt2.run({type:'SETTING_SET',setting:'discountLimit',value:70000,effectiveFrom:'2026-11-01',reason:'',key:'cfg:2'});
  await rt2.run({type:'CFG_DECIDE',id:'CFG-REQ-5',approve:false,reason:'Not now',key:'approval:CFG-REQ-5'});
  await rt2.run({type:'CFG_WITHDRAW',id:'12',key:'cfg-wd:12'});
  await rt2.run({type:'CFG_REVERT',id:'11',key:'cfg-rv:11'});
  await rt2.run({type:'USER_CREDENTIAL',id:'2',pin:'482913',key:'cred:2'});
  const c2=(name,i=0)=>srv2.commands(name)[i];
  test('[W4-5 admin] Command bodies, Idempotency-Keys and expectedVersion match the server contracts',()=>{
    assert.deepEqual([c2('SetUserEnabled').body,c2('SetUserEnabled').key],[{userId:2,enabled:false,expectedVersion:'VU2'},'user:2:true:0']);
    assert.deepEqual([c2('CreateUser').body,c2('CreateUser').key],[{loginName:'rohan.patil',displayName:'Rohan Patil',
      roles:[{roleCode:'CSH',validFromUtc:null,validUntilUtc:'2026-11-30T18:30:00.000Z'}]},'new:u1']);
    // Housekeeper (expired, disabled) → Supervisor + enabled: assign (expiry kept), revoke the old role, enable — versions chained.
    assert.deepEqual([c2('AssignRole').body,c2('AssignRole').key],[{userId:4,roleCode:'SUP',validFromUtc:null,validUntilUtc:'2026-10-10T18:30:00.000Z',expectedVersion:'VU4'},'edit:u4:role']);
    assert.deepEqual([c2('RevokeRole').body,c2('RevokeRole').key],[{userId:4,roleCode:'HKP',expectedVersion:'AFTER-ASSIGN'},'edit:u4:revoke']);
    assert.deepEqual([c2('SetUserEnabled',1).body,c2('SetUserEnabled',1).key],[{userId:4,enabled:true,expectedVersion:'AFTER-REVOKE'},'edit:u4:enabled']);
    assert.deepEqual(c2('AssignRole',1).body,{userId:2,roleCode:'REC',validFromUtc:null,validUntilUtc:null,expectedVersion:'VU2'},'clearing the expiry');
    assert.equal(srv2.commands('RevokeRole').length,1);
    assert.deepEqual(c2('GrantException').body,{userId:2,permissionId:'PERM-RPT-003',effect:'DENY',mode:'A',scope:'P',limit:null,limitUnit:null,reason:'Exports restricted',
      validUntilUtc:'2026-11-30T18:30:00.000Z',expectedVersion:'VU2'});
    assert.deepEqual(c2('RevokeException').body,{userId:2,exceptionId:7,expectedVersion:'VU2'});
    assert.deepEqual([c2('SetSetting').body,c2('SetSetting').key],[{key:'discountLimit',value:70000,effectiveFrom:'2026-11-01'},'cfg:2']);
    assert.deepEqual(c2('DecideSettingRequest').body,{requestId:5,approve:false,reason:'Not now',expectedVersion:'RQ5'});
    assert.deepEqual(c2('WithdrawScheduledSetting').body,{changeId:12});
    assert.deepEqual(c2('RevertSetting').body,{changeId:11});
    assert.deepEqual(c2('SetCredential').body,{userId:2,password:null,pin:'482913',expectedVersion:'VU2'});
    const after=srv2.calls.map(c=>`${c.method} ${c.path}`);const i=after.indexOf('POST /commands/SetSetting');
    assert.deepEqual(after.slice(i+1,i+4).sort(),['GET /configuration/history','GET /configuration/requests','GET /configuration/values'].sort(),'configuration reload after SetSetting');
  });

  // A receptionist: no admin reads; policy = own capabilities; settings still from /configuration/values.
  const rec=fakeServer({'GET /me/capabilities':()=>capabilities({userId:2,displayName:'Rohan',roles:['REC'],
    grants:[['PERM-RES-001','A'],['PERM-RES-002','A'],['PERM-FOL-002','A'],['PERM-FOL-003','L'],['PERM-PAY-001','A']]}),'GET /configuration/values':()=>values,
    'GET /configuration/requests':()=>reply(403,{code:'FORBIDDEN',message:'Not permitted'})});
  const rr=await startRuntime(S,rec,mine);const rs=rr.get().state;
  test('[W4-5 admin] Non-admin: no /admin reads, policy from own capabilities (authorize follows the server), settings and module switches still load',()=>{
    assert.equal(rec.calls.filter(c=>c.path.startsWith('/admin')||c.path==='/configuration/history').length,0);
    assert.deepEqual(rr.get().errors,{});
    assert.deepEqual(Object.keys(rs.policy.templates.Reception).sort(),['PERM-FOL-002','PERM-FOL-003','PERM-PAY-001','PERM-RES-001','PERM-RES-002']);
    assert.deepEqual(rs.policy.templates.Manager,{},'other roles are not shown to a non-admin');
    assert.equal(M.authorize(rs,'2','PERM-RES-002').decision,'allow');assert.equal(M.authorize(rs,'2','PERM-ADM-001').decision,'deny');
    assert.equal(M.authorize(rs,'2','PERM-FOL-003',{amount:1}).decision,'allow','L without a role limit → the hotel discount threshold (₹250)');
    assert.equal(M.authorize(rs,'2','PERM-FOL-003',{amount:30000}).decision,'approval');
    assert.deepEqual([rs.settings.hotel,M.cfgModuleOn(rs,'pos'),rs.approvals.length,rs.audit.length],['Sand Residency',false,0,0]);
  });

  const P=await loadPages();
  const shown=[P.renderPages(st,'1',false),P.renderPages(st,'1',true),P.renderPages(rt2.get().state,'1',false),P.renderPages(rs,'2',false),P.renderPages(rs,'2',true)];
  test('[W4-5 admin] Every desktop and phone page renders on the admin server data (Owner and Reception)',()=>{
    assert.deepEqual(shown.map(x=>x.errors),[{},{},{},{},{}]);
  });
  test('[W4-5 admin] Unmapped admin commands are listed with a reason',()=>{
    const a=S.AREAS.find(x=>x.id==='admin');
    assert.deepEqual(Object.keys(a.unmapped).sort(),['EXPENSE_APPROVE','GRANT','POLICY_DECIDE','POLICY_SET','SETTINGS','TARIFF_SCHEDULE','TAX_SCHEDULE']);
    for(const r of Object.values(a.unmapped))assert.ok(r.length>10);
    assert.deepEqual(S.validateAreas(S.AREAS),[]);
  });
}
