// Wave C3 · System health area of the production sync layer (src/clients/sync/syshealth.ts) against a fake server shaped like the .NET DTOs
// (08 Production/src/Hcc.Contracts/Health/SystemHealthContracts.cs): GET /admin/system-health → sysHealthX, SYS_RETRY_PARKED → RetryParkedEvent.
// Names prefixed [SYS-SYNC].
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

export async function register({test,M,assert}){
  const S=await loadSync();
  const mine={areas:S.AREAS.filter(a=>['core','syshealth'].includes(a.id))};
  // An Owner as the server lists one (PERM-SYS-001 A·P; PERM-ADM-001 gives the coarse 'admin' gate of the page and the loader).
  const owner=capabilities({grants:[['PERM-ADM-001','A'],['PERM-ADM-002','A'],['PERM-ADM-003','A'],['PERM-SYS-001','A']]});
  const parked=(id,o={})=>({consumer:'accounting-vouchers',consumerLabel:'Accounting vouchers',outboxId:id,eventType:'ChargePosted',attempts:5,errorType:'JsonException',
    firstFailedUtc:'2026-10-24T03:20:00.000Z',lastFailedUtc:'2026-10-24T03:30:00.000Z',parkedUtc:'2026-10-24T03:30:00.000Z',...o});
  let items=[parked(48213),parked(48250,{consumer:'messaging-triggers',consumerLabel:'Guest messages',eventType:'ReservationConfirmed',attempts:6,errorType:'SqlException 547'})];
  const health=()=>({checkedUtc:'2026-10-24T04:35:00.000Z',parked:items,
    lastBackup:{kind:'Differential',succeeded:false,startedUtc:'2026-10-24T03:00:00.000Z',finishedUtc:'2026-10-24T03:00:05.000Z',fileName:null,sizeBytes:null,reason:'sqlcmd failed (exit 1): Msg 3201, Msg 3013'},
    lastSuccessfulBackupUtc:'2026-10-23T21:01:10.000Z',
    lastRestoreDrill:{kind:'RestoreDrill',succeeded:true,startedUtc:'2026-10-18T22:00:00.000Z',finishedUtc:'2026-10-18T22:04:00.000Z',fileName:'Hcc_FULL_20261018T210000Z.bak.hccenc',sizeBytes:null,reason:null},
    eventLog:'off'});
  const routes={'GET /me/capabilities':()=>owner,'GET /admin/system-health':health,
    'POST /commands/RetryParkedEvent':({body})=>{items=items.filter(p=>!(p.consumer===body.consumer&&p.outboxId===body.outboxId));
      return {consumer:body.consumer,outboxId:body.outboxId,eventType:'ChargePosted',attempts:5,released:true};}};
  const srv=fakeServer(routes);
  const rt=await startRuntime(S,srv,mine);
  const st=rt.get().state,bootErrors=rt.get().errors;

  test('[SYS-SYNC] Loader: sysHealthX from GET /admin/system-health (server source, IST clock times, ids as strings, failed backup with its reason, Event Log off)',()=>{
    assert.deepEqual(srv.unrouted,[]);assert.deepEqual(bootErrors,{});
    const x=st.sysHealthX;
    assert.equal(x.source,'server');assert.equal(x.checkedAt,'2026-10-24T10:05');assert.equal(x.eventLog,'off');
    assert.deepEqual(x.parked[0],{consumer:'accounting-vouchers',label:'Accounting vouchers',outboxId:'48213',eventType:'ChargePosted',attempts:5,errorType:'JsonException',
      firstFailedAt:'2026-10-24T08:50',lastFailedAt:'2026-10-24T09:00',parkedAt:'2026-10-24T09:00'});
    assert.deepEqual([x.parked[1].label,x.parked[1].attempts,x.parked[1].errorType],['Guest messages',6,'SqlException 547']);
    assert.deepEqual(x.lastBackup,{kind:'Differential',ok:false,at:'2026-10-24T08:30',reason:'sqlcmd failed (exit 1): Msg 3201, Msg 3013'});
    assert.equal(x.lastOkBackupAt,'2026-10-24T02:31');
    assert.deepEqual(x.lastDrill,{kind:'RestoreDrill',ok:true,at:'2026-10-19T03:34',file:'Hcc_FULL_20261018T210000Z.bak.hccenc'});
    assert.deepEqual(srv.gets('/admin/system-health').map(c=>c.query),[{}]);
    // The page's view is the server's answer, never the demo event.
    const v=M.sysHealthView(st);assert.equal(v.source,'server');assert.ok(!v.parked.some(p=>p.outboxId==='48213'&&p.consumer==='messaging-triggers'));});

  srv.reset();
  const p0=st.sysHealthX.parked[0];
  const cmd={type:'SYS_RETRY_PARKED',consumer:p0.consumer,outboxId:p0.outboxId,key:`sys-retry:${p0.consumer}/${p0.outboxId}/${p0.attempts}`};
  const r=await rt.run(cmd);
  const posts=srv.calls.filter(c=>c.method==='POST'),gets=srv.gets().map(c=>c.path),after=rt.get().state.sysHealthX;
  test('[SYS-SYNC] SYS_RETRY_PARKED → exactly POST /commands/RetryParkedEvent {consumer, outboxId:number} with the button\'s key; System health reloads',()=>{
    assert.ok(r.ok,JSON.stringify(r));
    assert.equal(posts.length,1);assert.equal(posts[0].path,'/commands/RetryParkedEvent');
    assert.deepEqual(posts[0].body,{consumer:'accounting-vouchers',outboxId:48213});
    assert.equal(posts[0].key,'sys-retry:accounting-vouchers/48213/5');
    assert.ok(gets.includes('/admin/system-health'));
    assert.deepEqual(after.parked.map(p=>p.outboxId),['48250']);});

  srv.reset();
  const bad=await rt.run({type:'SYS_RETRY_PARKED',consumer:'accounting-vouchers',outboxId:'not-a-number'});
  srv.on('POST /commands/RetryParkedEvent',()=>reply(404,{code:'NOT_FOUND',message:'This parked event was not found: it may have been retried or delivered already. Refresh System health.'}));
  const gone=await rt.run({type:'SYS_RETRY_PARKED',consumer:'messaging-triggers',outboxId:'48250'});
  test('[SYS-SYNC] A bad outbox id is refused before any request; the server\'s NOT_FOUND keeps its text',()=>{
    assert.equal(bad.ok,false);assert.match(bad.text,/Choose the parked event/);assert.equal(srv.commands('RetryParkedEvent').length,1,'only the second command reached the server');
    assert.deepEqual([gone.ok,gone.reason],[false,'server']);assert.match(gone.text,/may have been retried or delivered already/);
    const area=S.AREAS.find(a=>a.id==='syshealth');assert.deepEqual(area.loaders.map(l=>[l.slice,l.requires,l.reads]),[['sysHealthX','admin',['GET /admin/system-health']]]);
    assert.deepEqual(area.commands.map(c=>c.type),['SYS_RETRY_PARKED']);});

  // A Manager (no PERM-SYS-001, no 'admin') never sends the read; the page then shows nothing, not the demo values.
  const mgrSrv=fakeServer({...routes,'GET /me/capabilities':()=>capabilities({roles:['MGR'],grants:[['PERM-RES-001','A'],['PERM-ADM-004','A'],['PERM-APR-001','L']]})});
  const mgr=await startRuntime(S,mgrSrv,mine);
  // A Tech admin holds PERM-SYS-001 and ADM-001 (users) only among the admin ids: the read is sent.
  const tadSrv=fakeServer({...routes,'GET /me/capabilities':()=>capabilities({roles:['TAD'],grants:[['PERM-ADM-001','A'],['PERM-ADM-002','R'],['PERM-SYS-001','A']]})});
  const tad=await startRuntime(S,tadSrv,mine);
  test('[SYS-SYNC] Read gate: the Manager never asks for /admin/system-health (no 403 noise) and sees an empty panel; the Tech admin loads it',()=>{
    assert.deepEqual(mgrSrv.gets('/admin/system-health'),[]);assert.equal(mgr.get().state.sysHealthX,undefined);assert.deepEqual(mgr.get().errors,{});
    assert.equal(tadSrv.gets('/admin/system-health').length,1);assert.equal(tad.get().state.sysHealthX.source,'server');
    assert.equal(S.READS['GET /admin/system-health']({grants:[{permissionId:'PERM-SYS-001',mode:'A',scope:'P'}]}),true);
    assert.equal(S.READS['GET /admin/system-health']({grants:[{permissionId:'PERM-SYS-001',mode:'V',scope:'P'}]}),false);});

  const P=await loadPages();
  const shown=[P.renderPages(st,'1',false),P.renderPages(st,'1',true),P.renderPages(rt.get().state,'1',false),P.renderPages(mgr.get().state,'1',true)];
  test('[SYS-SYNC] Every desktop and phone page renders on the loaded System health data and without it',()=>{
    assert.deepEqual(shown.map(x=>x.errors),[{},{},{},{}]);assert.ok(shown.every(x=>x.rendered>5));});
}
