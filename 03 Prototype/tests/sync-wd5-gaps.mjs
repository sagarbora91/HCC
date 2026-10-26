// Wave D5 — leftovers of Wave B8 (role matrix v2 screens) and B9 (DEC-080 group blocks), sync layer side, against a fake .NET API.
//   DEC-080   the client's group-block switch follows the server option Hcc:GroupBlocks:Enabled, read from GET /configuration/values
//             (groupBlocksEnabled) by the admin area's configX loader — one switch, on the server. An older server without the field: off.
//   DEC-079 D7 the OTA channel list's commission rate is null for a reader without PERM-OTA-004 → commissionHidden (rate 0); channel
//             terms cannot be sent from such a reader.
//   DEC-079 D1 the server's Restaurant (RST) template lands in the policy and in the per-role module gates (Access control shows it).
// Screens: tests/stream-wd5-gaps.mjs.
import {loadSync,fakeServer,startRuntime,ownErrors} from './_sync-harness.mjs';

const values=extra=>({businessDate:'2026-10-24',values:{hotel:'Sand Residency',checkin:'13:00',checkout:'11:00',discountLimit:25000,'module.groups':true},scheduled:[],...extra});
const channel=(id,commissionBps)=>({channelId:id,code:'C'+id,name:'Channel '+id,status:'Connected',version:'V'+id,commissionBps,markupBps:500,lastSync:null,statusNote:'ok',pushDue:false,pendingMessages:0,
  mapping:[{roomTypeId:2,roomType:'Deluxe',channelCode:'DLX'+id,stopSell:false}]});
const grant=(permissionId,mode='A',scope='P')=>({permissionId,mode,scope,limit:null,limitUnit:null});
const roles={roles:[
  {roleCode:'OWN',name:'Owner',credentialKind:'PASSWORD',ownerOnlyAssign:true,denies:[],grants:[grant('PERM-RES-001'),grant('PERM-FOL-002'),grant('PERM-ADM-001'),grant('PERM-ADM-002'),grant('PERM-POS-004')]},
  {roleCode:'RST',name:'Restaurant',credentialKind:'PIN',ownerOnlyAssign:true,denies:[],grants:[grant('PERM-POS-001'),grant('PERM-POS-002'),grant('PERM-POS-003','R'),grant('PERM-HR-004','A','O')]},
],permissions:[]};

export async function register({test,assert}){
  const S=await loadSync();
  const admin={areas:S.AREAS.filter(a=>['core','admin'].includes(a.id))};

  // ── DEC-080: the flag follows the values read ──
  const start=S.grpBlocksOnServer();
  const boot=async body=>{const srv=fakeServer({'GET /configuration/values':()=>body,'GET /configuration/history':()=>({page:1,pageSize:100,total:0,items:[]})});
    const rt=await startRuntime(S,srv,admin);return {rt,error:rt.get().errors.configX,flag:S.grpBlocksOnServer()};};
  const on=await boot(values({groupBlocksEnabled:true}));
  const off=await boot(values({groupBlocksEnabled:false}));
  S.grpSetBlocksOnServer(true);
  const older=await boot(values({}));   // a server without the field leaves the flag where it is (default off)
  S.grpSetBlocksOnServer(false);
  const olderOff=await boot(values({}));
  const end=S.grpBlocksOnServer();
  test('[DEC-080 D5] grpBlocksOnServer follows GET /configuration/values groupBlocksEnabled (the server option Hcc:GroupBlocks:Enabled); default off',()=>{
    assert.equal(start,false,'off before any read');
    assert.deepEqual([on.error,off.error],[undefined,undefined],'the configX slice loaded');
    assert.equal(on.flag,true,'server option on → the client sends group blocks');
    assert.equal(off.flag,false,'server option off → refused on the client with the written reason');
    assert.equal(older.flag,true);assert.equal(olderOff.flag,false,'no field (older server): the flag is not changed, so it stays off by default');
    assert.equal(end,false);
    assert.equal(on.rt.get().state.configX.values.hotel,'Sand Residency','the values read is mapped as before');
  });

  // ── DEC-079 D7: commission rate withheld ──
  const chSrv=fakeServer({'GET /ota/channels':()=>[channel(1,null),channel(2,null)],'GET /ota/messages':()=>[],'GET /ota/pushes':()=>[],'GET /web-booking/requests':()=>({requests:[],pending:0})});
  chSrv.on('POST /commands/SaveChannel',()=>({ok:true}));
  const chRt=await startRuntime(S,chSrv);
  const masked=chRt.get().state.channelsX.channels;
  chSrv.reset();
  const save=await chRt.run({type:'CHN_CHANNEL_SAVE',channel:'1',commissionBps:0,markupBps:500,mapping:{Deluxe:'DLX1'}});
  test('[DEC-079 D7 D5] OTA channels: a null commission rate (reader without PERM-OTA-004) maps to commissionHidden with rate 0; the rest of the card is unchanged',()=>{
    assert.equal(chRt.get().errors.channelsX,undefined,'the OTA slice loaded (yield has no fake routes here)');
    assert.deepEqual(masked.map(c=>[c.id,c.commissionBps,c.commissionHidden,c.markupBps,c.mapping.Deluxe]),[['1',0,true,500,'DLX1'],['2',0,true,500,'DLX2']]);
  });
  test('[DEC-079 D7 D5] OTA channels: channel terms are not sent from a reader who cannot see the commission (no SaveChannel with a made-up rate)',()=>{
    assert.equal(save.ok,false);assert.equal(save.reason,'precheck');assert.match(save.text,/Manager or the Owner/);
    assert.equal(chSrv.commands('SaveChannel').length,0);
  });

  // ── DEC-079 D1: the Restaurant template from the roles read ──
  const bootRoles=async body=>{const srv=fakeServer({'GET /admin/roles':()=>body,'GET /admin/exceptions':()=>({items:[]}),'GET /admin/users':()=>({items:[],page:1,pageSize:100,total:0}),
    'GET /admin/audit':()=>({items:[],page:1,pageSize:100,total:0}),'GET /configuration/values':()=>values({}),'GET /configuration/history':()=>({page:1,pageSize:100,total:0,items:[]}),
    'GET /configuration/requests':()=>({items:[]})});return (await startRuntime(S,srv,admin)).get();};
  const withRst=await bootRoles(roles),withoutRst=await bootRoles({roles:[roles.roles[0]],permissions:[]});
  test('[DEC-079 D1 D5] The RST template of the roles read becomes the Restaurant policy template and module gate ("billing" through PERM-POS-002); without it, nine roles as before',()=>{
    assert.deepEqual([withRst.errors.policy,withRst.errors.grants],[undefined,undefined]);
    assert.deepEqual(withRst.state.policy.templates.Restaurant,{'PERM-POS-001':{effect:'allow',scope:'P'},'PERM-POS-002':{effect:'allow',scope:'P'},'PERM-POS-003':{effect:'request',scope:'P'},'PERM-HR-004':{effect:'allow',scope:'O'}});
    assert.deepEqual(withRst.state.grants.Restaurant,['billing']);
    assert.equal(withoutRst.state.policy.templates.Restaurant,undefined);
    assert.equal(withoutRst.state.grants.Restaurant,undefined);
    assert.ok(withRst.state.grants.Owner.includes('admin'));
  });
}
