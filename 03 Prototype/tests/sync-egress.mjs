// AF-NETSET (audit item 23) · CFG_NET_SET of the production sync layer (src/clients/sync/egress.ts) against a fake server:
// the command goes to POST /commands/SetNetSetting {setting, value, reason, expectedVersion}; the configuration slices reload afterwards. Names prefixed [AF-NET].
import {loadSync,fakeServer,startRuntime,capabilities} from './_sync-harness.mjs';

export async function register({test,assert}){
  const S=await loadSync();
  const mine={areas:S.AREAS.filter(a=>['core','admin','egress'].includes(a.id))};
  const owner=capabilities({grants:[['PERM-NET-001','A'],['PERM-NET-002','A'],['PERM-ADM-001','A']]});
  const srv=fakeServer({'GET /me/capabilities':()=>owner,
    'POST /commands/SetNetSetting':()=>({status:'applied',key:'net.messaging',changeId:7,requestId:null,effectiveFrom:'2026-10-24',before:'Off',after:'On',version:'AQ==',message:'ok'})});
  const rt=await startRuntime(S,srv,mine);
  srv.reset();
  const r=await rt.run({type:'CFG_NET_SET',setting:'net.messaging',value:true,reason:'  Test the gateway  ',key:'net:net.messaging:1'});
  const posts=srv.calls.filter(c=>c.method==='POST');
  test('[AF-NET] CFG_NET_SET → SetNetSetting body',()=>{
    assert.ok(r.ok,JSON.stringify(r));
    assert.equal(posts.length,1);assert.equal(posts[0].path,'/commands/SetNetSetting');
    assert.deepEqual(posts[0].body,{setting:'net.messaging',value:true,reason:'Test the gateway',expectedVersion:null});
    assert.equal(posts[0].key,'net:net.messaging:1');});

  const area=S.AREAS.find(a=>a.id==='egress');
  const m=area.commands.find(c=>c.type==='CFG_NET_SET');
  test('[AF-NET] after success the config slices reload',()=>{
    for(const slice of ['configX','settings'])assert.ok(m.reload.includes(slice),slice);});

  srv.reset();
  const noReason=await rt.run({type:'CFG_NET_SET',setting:'net.ota',value:true,reason:'x',key:'k2'});
  const badKey=await rt.run({type:'CFG_NET_SET',setting:'hotel',value:true,reason:'Because',key:'k3'});
  const win=await rt.run({type:'CFG_NET_SET',setting:'net.window',value:{from:'08:00',to:'20:00',days:[1,2]},reason:'Office hours',key:'k4'});
  test('[AF-NET] bad key / short reason are refused before any request; the window object is sent as is',()=>{
    assert.equal(noReason.ok,false);assert.equal(badKey.ok,false);
    const p=srv.calls.filter(c=>c.method==='POST');assert.equal(p.length,1);
    assert.deepEqual(p[0].body.value,{from:'08:00',to:'20:00',days:[1,2]});assert.ok(win.ok);});
}
