// W4 fix F3: (D3) a 503 NOT_CONFIGURED (guest-ID key missing) is not "server unreachable"; (D4) the Cashier screen's SHIFT_OPEN → OpenShift after the
// till-cash import left the first day's shift closed. Fake routes are shaped like the .NET DTOs (Hcc.Contracts).
import {loadSync,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

const closedDesk=(over={})=>({businessDate:'2026-10-26',tills:[{till:{tillId:3,code:'FD-1',name:'Front desk drawer',version:'T1'},active:null,expected:null,
  lastClosed:{shift:{shiftId:40,tillId:3,businessDate:'2026-10-26',status:'Closed',openingFloatPaise:2500000,expectedPaise:2500000,countedPaise:2500000,approvalId:null,version:'S1'},
    submittedBy:2,differenceReason:'Opening cash (import)',closedBy:2,approval:null},...over}]});

export async function register({test,assert}){
  const S=await loadSync();

  // D3 ── key missing: the server answers 503 UNAVAILABLE with details.reason NOT_CONFIGURED.
  let mode='key';
  const srv=fakeServer({
    'POST /commands/RegisterGuest':()=>mode==='key'
      ?reply(503,{code:'UNAVAILABLE',message:'Full ID numbers cannot be stored: guest ID encryption is not configured on this server.',details:{reason:'NOT_CONFIGURED',feature:'guest-id'}})
      :reply(503,{code:'UNAVAILABLE',message:'The database is not reachable.'}),
  });
  const rt=await startRuntime(S,srv);
  const key=await rt.transport.command('RegisterGuest',{}).then(()=>null,e=>e);
  const keyCalls=srv.commands('RegisterGuest').length;
  const roAfterKey=rt.get().readOnly;
  const next=await rt.transport.get('/system/health').then(()=>'ok',()=>'fail'); // the server is reachable: other calls still work
  mode='db';
  const db=await rt.transport.command('RegisterGuest',{}).then(()=>null,e=>e);
  const roAfterDb=rt.get().readOnly;

  test('[F3 D3] A 503 NOT_CONFIGURED is a setup problem: clear message, not retried, the app stays online (no read-only banner)',()=>{
    assert.ok(key,'command refused');
    assert.equal(key.wireCode,'NOT_AVAILABLE');assert.equal(key.httpStatus,503);assert.equal(key.resultUnknown,false);
    assert.match(key.message,/ID storage is not set up on the server/);
    assert.equal(keyCalls,1);
    assert.equal(roAfterKey,false);assert.equal(next,'ok');
  });
  test('[F3 D3] A real 503 UNAVAILABLE (database not reachable) still switches the app to read-only',()=>{
    assert.equal(db.wireCode,'UNAVAILABLE');assert.equal(roAfterDb,true);
  });

  // D4 ── first day after the till-cash import: no open shift, SHIFT_OPEN opens it with the carried float.
  let desk=closedDesk();
  const cs=fakeServer({
    'GET /cashier/desk':()=>desk,
    'POST /commands/OpenShift':({body})=>{desk={businessDate:'2026-10-26',tills:[{till:desk.tills[0].till,active:{shift:{shiftId:41,tillId:body.tillId,businessDate:'2026-10-26',status:'Open',openingFloatPaise:2500000,
      expectedPaise:null,countedPaise:null,approvalId:null,version:'S2'},submittedBy:null,differenceReason:null,closedBy:null,approval:null},expected:{expectedPaise:2500000},lastClosed:null}]};
      return {shiftId:41,tillId:body.tillId,businessDate:'2026-10-26',status:'Open',openingFloatPaise:2500000,version:'S2'};},
  });
  const crt=await startRuntime(S,cs,{areas:S.AREAS.filter(a=>a.id!=='channels')});
  const before=crt.get().state;
  const opened=await crt.run({type:'SHIFT_OPEN'});
  const open=cs.commands('OpenShift');
  const after=crt.get().state;

  // a user without the cashier permission, and an already open shift, are refused before anything is sent
  const lim=fakeServer({'GET /me/capabilities':()=>capabilities({roles:['HKP'],grants:[['PERM-HK-001','A']]})});
  const lrt=await startRuntime(S,lim);lim.reset();
  const denied=await lrt.run({type:'SHIFT_OPEN'});
  const again=await crt.run({type:'SHIFT_OPEN'});

  test('[F3 D4] First day after the till-cash import: shift closed with no open control state → SHIFT_OPEN sends OpenShift {tillId, openingFloatPaise:null}; the desk reloads and the shift is open',()=>{
    assert.equal(before.shiftOpen,false);assert.equal(before.openingCash,2500000);
    assert.ok(opened.ok,opened.text);
    assert.equal(open.length,1);assert.deepEqual(open[0].body,{tillId:3,openingFloatPaise:null});assert.ok(open[0].key);
    assert.equal(after.shiftOpen,true);assert.equal(after.openingCash,2500000);
  });
  test('[F3 D4] SHIFT_OPEN is refused without PERM-SHF-001 and while a shift is already open (nothing sent)',()=>{
    assert.equal(denied.ok,false);assert.equal(denied.reason,'precheck');assert.equal(lim.commands('OpenShift').length,0);
    assert.equal(again.ok,false);assert.equal(again.reason,'precheck');assert.equal(cs.commands('OpenShift').length,1);
  });
}
