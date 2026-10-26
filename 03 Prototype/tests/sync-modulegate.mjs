// A11-01 (DEC-057) in the production client: the server refuses a command of a module switched off in Master configuration with
// FORBIDDEN, the prototype's sentence (cfgModuleOffMessage), details.reason MODULE_OFF and details.moduleId. The client shows that
// sentence exactly as the demo build does ("Not permitted: …"), and reloads the module switches so the module's screens disappear
// (the module may have been switched off on another device). Any other refusal leaves the configuration alone.
import {loadSync,fakeServer,startRuntime,reply} from './_sync-harness.mjs';

export async function register({test,assert,M}){
  const S=await loadSync();
  const mine={areas:S.AREAS.filter(a=>['core','reservations','frontdesk','admin','maintenance'].includes(a.id))};
  const mod=M.CFG_MODULES.find(m=>m.id==='maintenance');
  const OFF_TEXT=M.cfgModuleOffMessage(mod);
  const values={businessDate:'2026-10-24',values:{hotel:'Sand Residency','module.maintenance':true,'module.pos':true},scheduled:[]};
  const page=()=>({page:1,pageSize:50,total:0,rows:[]});
  let mode='ok';
  const srv=fakeServer({
    'GET /admin/users':()=>({items:[{userId:1,loginName:'owner',displayName:'Test Owner',enabled:true,credentialKind:'PASSWORD',lockedOut:false,
      roles:[{roleCode:'OWN',validFromUtc:'2026-01-01T00:00:00.000Z',validUntilUtc:null}],version:'VU1'}],page:1,pageSize:100,total:1}),
    'GET /admin/audit':()=>({page:1,pageSize:100,total:0,items:[]}),
    'GET /configuration/values':()=>structuredClone(values),
    'GET /configuration/history':()=>({page:1,pageSize:100,total:0,items:[]}),
    'GET /configuration/requests':()=>({items:[]}),
    'GET /maintenance/registers':()=>({assets:[],vendors:[],spares:[],plans:[]}),'GET /maintenance/amcs':()=>({amcs:[]}),
    'GET /maintenance/work-orders':page,
    'GET /stock/items':()=>({...page(),parSets:3}),'GET /stock/laundry':page,'GET /stock/counts':page,'GET /stock/losses':page,'GET /stock/moves':page,
    'GET /stock/settings':()=>({parSets:3,vendorName:'Fresh Linen Services',turnaroundDays:1}),
    'POST /commands/SaveMaintenanceVendor':()=>{
      if(mode==='off'){values.values['module.maintenance']=false; // switched off on another device a moment ago
        return reply(403,{code:'FORBIDDEN',message:OFF_TEXT,details:{reason:'MODULE_OFF',moduleId:'maintenance'}});}
      if(mode==='denied')return reply(403,{code:'FORBIDDEN',message:'You do not have permission for this action.'});
      return {vendorId:9,version:'vn9'};
    },
  });
  const rt=await startRuntime(S,srv,mine);
  const before=rt.get().state;
  const vendor=key=>({type:'MNT_VENDOR_SAVE',name:'Sheetal Cooling',trade:'AC',phone:'+91 90000 11201',key});
  const run=async(m,key)=>{mode=m;srv.reset();const r=await rt.run(vendor(key));return {r,calls:srv.calls.map(c=>`${c.method} ${c.path}`),state:rt.get().state};};
  const off=await run('off','k-off');
  values.values['module.maintenance']=true;
  const denied=await run('denied','k-denied');
  const ok=await run('ok','k-ok');

  // The demo build's own refusal for the same command with the module off (execute → cfgBeforeCommand) and its toast text (App.tsx).
  const ps=M.execute(M.seed(),'owner',{type:'SETTING_SET',setting:'module.maintenance',value:false});
  let protoText='';
  try{M.execute(ps,'owner',vendor('p-off'));}catch(e){protoText=`${M.errorTitle[e.code]}: ${e.message}`;}

  test('[A11-01] module switched off: the client shows the server refusal word for word, the same text as the demo build',()=>{
    assert.equal(M.cfgModuleOn(before,'maintenance'),true);assert.equal(M.cfgPageOn(before,'maintenance'),true);
    assert.deepEqual([off.r.ok,off.r.reason],[false,'server']);
    assert.equal(off.r.text,`Not permitted: ${OFF_TEXT}`);
    assert.equal(off.r.text,protoText);
    assert.match(off.r.text,/^Not permitted: Maintenance is switched off in Master configuration › Modules on\/off\. Its data is kept;/);
    assert.deepEqual([off.r.error.wireCode,off.r.error.details.reason,off.r.error.details.moduleId],['FORBIDDEN','MODULE_OFF','maintenance']);
    assert.equal(off.calls.filter(c=>c==='POST /commands/SaveMaintenanceVendor').length,1,'refused once, not retried');
  });
  test('[A11-01] module switched off: the client reloads Master configuration, so the module screens disappear here too',()=>{
    assert.ok(off.calls.indexOf('GET /configuration/values')>off.calls.indexOf('POST /commands/SaveMaintenanceVendor'),off.calls.join(', '));
    assert.deepEqual(off.state.configX.modulesOff,['maintenance']);
    assert.equal(M.cfgModuleOn(off.state,'maintenance'),false);assert.equal(M.cfgPageOn(off.state,'maintenance'),false);
    assert.equal(M.cfgPageOn(off.state,'pos'),true,'other modules untouched');
    assert.equal(off.calls.includes('GET /maintenance/registers'),false,'nothing changed, so the module slices are not reloaded');
  });
  test('[A11-01] any other refusal leaves the configuration alone; back on, the command goes through',()=>{
    assert.deepEqual([denied.r.ok,denied.r.text],[false,'Not permitted: You do not have permission for this action.']);
    assert.equal(denied.calls.includes('GET /configuration/values'),false);
    assert.equal(ok.r.ok,true,ok.r.text);
    assert.equal(ok.calls[0],'POST /commands/SaveMaintenanceVendor');assert.ok(ok.calls.includes('GET /maintenance/registers'),ok.calls.join(', '));
  });
}
