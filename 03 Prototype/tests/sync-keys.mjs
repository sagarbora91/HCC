// AF-SYNC-OPS (OPS-01): KEY_* commands and the keysX loaders in the production sync layer (clients/sync/keys.ts).
import {loadSync,fakeServer,startRuntime,capabilities} from './_sync-harness.mjs';

const logRow=(jobId,over={})=>({jobId,requestedUtc:'2026-10-24T05:30:00.000Z',event:'issued',reason:null,roomNo:'105',bookingNo:'HPE-0001',reservationId:1201,requestedBy:3,requestedByName:'Desk (fictional)',
  state:'done',errorCode:null,cardNo:'846716913',...over});
const lock=(over={})=>({roomId:7,roomNo:'105',hasLock:true,lockNo:5,doorId:null,doorName:'105',note:null,version:'AAAAAAAAB9E=',...over});
const card={cardId:55,cardNo:'846716913',roomId:7,roomNo:'105',event:'issued',reason:null,beginAt:'2026-10-24T12:00',endAt:'2026-10-26T12:00',status:'active',issuedBy:3,issuedUtc:'2026-10-24T05:30:00.000Z'};

export async function register({test,assert}){
  const S=await loadSync();
  const routes=()=>({'GET /keys/log':()=>({rows:[logRow('J2',{event:'cancelled',requestedUtc:'2026-10-25T05:30:00.000Z',cardNo:null}),logRow('J1')],totals:{},total:2}),
    'GET /keys/locks':()=>({locks:[lock(),lock({roomId:8,roomNo:'106',hasLock:false,lockNo:null,doorName:null,note:'Owner ruling: manual key',version:'AAAAAAAAB9F='})],encoders:[]}),
    'GET /keys/encoder':()=>({encoderId:'FRONTDESK-1',pcName:'PC',online:true,adapter:'mock',detail:null,lastSeenUtc:'2026-10-25T05:31:00.000Z'}),
    'GET /keys/panel/:id':()=>({reservationId:1201,activeCards:[card]}),
    'POST /commands/RequestKey':()=>({}),'POST /commands/RecordManualKey':()=>({}),'POST /commands/CancelKey':()=>({}),'POST /commands/ReadKey':()=>({}),
    'POST /commands/SaveKeyLock':()=>({})});
  const srv=fakeServer(routes());
  const rt=await startRuntime(S,srv);
  const st=rt.get().state;
  const k=st.keysX;
  const req=await rt.run({type:'KEY_REQUEST',booking:'1201',event:'reissued',reason:'lost',validTo:'2026-10-26',key:'k-req'});
  const man=await rt.run({type:'KEY_MANUAL',booking:'1201',event:'issued',note:'Manual key given',key:'k-man'});
  const can=await rt.run({type:'KEY_CANCEL',booking:'1201',key:'k-can'});
  const rd=await rt.run({type:'KEY_READ',key:'k-rd'});
  const sv=await rt.run({type:'KEY_LOCK_SAVE',room:'105',hasLock:true,doorName:'105',lockNo:'6',note:'Re-mapped',key:'k-lock'});
  const unknownRoom=await rt.run({type:'KEY_LOCK_SAVE',room:'999',hasLock:false,doorName:'',lockNo:'',note:'',key:'k-lock2'});
  const step=await rt.run({type:'KEY_AGENT_STEP',key:'k-step'});
  const lowSrv=fakeServer({...routes(),'GET /me/capabilities':()=>capabilities({userId:7,displayName:'Housekeeper',roles:['HKP'],grants:[['PERM-HK-001','A']]})});
  const low=await startRuntime(S,lowSrv);
  const lowGets=['/keys/log','/keys/locks','/keys/encoder','/keys/panel/1201'].map(p=>lowSrv.gets(p).length);

  test('[AF-KEYS] KEY_REQUEST → RequestKey with the server reservation id, event and reason; KEY_MANUAL / CANCEL / READ map the same way',()=>{
    assert.equal(req.ok,true);assert.deepEqual(srv.commands('RequestKey')[0].body,{reservationId:1201,event:'reissued',reason:'lost',validTo:'2026-10-26',cards:1});
    assert.equal(man.ok,true);assert.deepEqual(srv.commands('RecordManualKey')[0].body,{reservationId:1201,event:'issued',note:'Manual key given'});
    assert.equal(can.ok,true);assert.deepEqual(srv.commands('CancelKey')[0].body,{reservationId:1201});
    assert.equal(rd.ok,true);assert.deepEqual(srv.commands('ReadKey')[0].body,{});
    assert.equal(srv.commands('RequestKey')[0].key,'k-req');
  });
  test('[AF-KEYS] KEY_LOCK_SAVE sends the lock version learnt from GET /keys/locks and the server room id',()=>{
    assert.equal(sv.ok,true);
    assert.deepEqual(srv.commands('SaveKeyLock')[0].body,{roomId:7,hasLock:true,lockNo:6,doorName:'105',note:'Re-mapped',expectedVersion:'AAAAAAAAB9E='});
    assert.equal(unknownRoom.ok,false);assert.equal(srv.commands('SaveKeyLock').length,1);
  });
  test('[AF-KEYS] loaders fill keysX from the GETs (jobs oldest first, locks by room, encoder, active cards from the panel); a role without the key permission sends none',()=>{
    assert.deepEqual(k.jobs.map(j=>j.id),['J1','J2']);
    assert.equal(k.jobs[0].booking,'1201');assert.equal(k.jobs[0].room,'105');assert.equal(k.jobs[0].state,'done');assert.equal(k.jobs[0].kind,'issue_guest');assert.equal(k.jobs[1].kind,'cancel');
    assert.equal(k.locks['105'].lockNo,5);assert.equal(k.locks['106'].hasLock,false);assert.equal(k.locks['106'].note,'Owner ruling: manual key');
    assert.equal(k.encoderOnline,true);assert.equal(k.encoderSeen,'2026-10-25T11:01');
    assert.equal(k.cards.length,1);assert.equal(k.cards[0].room,'105');assert.equal(k.cards[0].status,'active');assert.equal(k.cards[0].booking,'1201');
    assert.deepEqual(lowGets,[0,0,0,0]);assert.equal((low.get().state.keysX?.jobs??[]).length,0);
  });
  test('[AF-KEYS] KEY_AGENT_STEP stays unmapped (demo encoder), nothing is sent',()=>{
    assert.equal(step.ok,false);assert.equal(step.reason,'unmapped');
  });
}
