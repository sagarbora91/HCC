// ACC-1 · Opening balances — production sync (src/clients/sync/accounting-opening.ts) against a fake server shaped like the .NET DTOs
// (08 Production/src/Hcc.Contracts/Accounting/OpeningBalanceContracts.cs). Names prefixed [ACC1-OPENING-SYNC].
import {loadSync,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

export async function register({test,assert}){
  const S=await loadSync();
  const mine={areas:S.AREAS.filter(a=>['core','accounting','accounting-opening'].includes(a.id))};
  // A Manager without the accounting view: the accounting loader sends no read, so only the commands talk to the server.
  const mgr=capabilities({roles:['MGR'],grants:[['PERM-ACC-008','A'],['PERM-RPT-002','A']]});
  const result=(status)=>({asOf:'2026-04-01',status,lines:10,debitPaise:774000000,creditPaise:774000000,differencePaise:-570500000,version:'AAAAAAAAB9E='});
  const srv=fakeServer({'GET /me/capabilities':()=>mgr,
    'POST /commands/SaveOpeningBalances':()=>result('draft'),'POST /commands/ApproveOpeningBalances':()=>result('approved'),
    'POST /commands/ReopenOpeningBalances':()=>result('draft')});
  const rt=await startRuntime(S,srv,mine);

  srv.reset();
  const lines=[{account:'1000',debit:2000000,credit:0},{account:'1110/CMP-DECCAN',debit:4000000,credit:0},{account:'1400',debit:0,credit:6000000}];
  const saved=await rt.run({type:'ACC_OPENING_SAVE',asOf:'2026-04-01',lines,note:' From the CA ',key:'open-save-1'});
  const save=srv.commands('SaveOpeningBalances');
  test('[ACC1-OPENING-SYNC] ACC_OPENING_SAVE → POST /commands/SaveOpeningBalances {asOf, lines in paise (1400 never sent), note} with the dialog key',()=>{
    assert.ok(saved.ok,JSON.stringify(saved));assert.equal(save.length,1);
    assert.deepEqual(save[0].body,{asOf:'2026-04-01',lines:[{account:'1000',debitPaise:2000000,creditPaise:0},{account:'1110/CMP-DECCAN',debitPaise:4000000,creditPaise:0}],note:'From the CA'});
    assert.equal(save[0].key,'open-save-1');});

  srv.reset();
  const approved=await rt.run({type:'ACC_OPENING_APPROVE'});
  const short=await rt.run({type:'ACC_OPENING_REOPEN',reason:'x'});
  const reopened=await rt.run({type:'ACC_OPENING_REOPEN',reason:'  Bank balance per statement '});
  test('[ACC1-OPENING-SYNC] APPROVE → ApproveOpeningBalances; REOPEN needs a reason (refused before any request) → ReopenOpeningBalances {reason}',()=>{
    assert.ok(approved.ok,JSON.stringify(approved));assert.deepEqual(srv.commands('ApproveOpeningBalances').map(c=>c.body),[{expectedVersion:null}]);
    assert.equal(short.ok,false);assert.match(short.text,/reason is required/);
    assert.ok(reopened.ok,JSON.stringify(reopened));
    assert.deepEqual(srv.commands('ReopenOpeningBalances').map(c=>c.body),[{reason:'Bank balance per statement',expectedVersion:null}]);});

  srv.reset();
  srv.on('POST /commands/ApproveOpeningBalances',()=>reply(403,{code:'FORBIDDEN',message:'Not permitted: No permission.'}));
  const refused=await rt.run({type:'ACC_OPENING_APPROVE'});
  const empty=await rt.run({type:'ACC_OPENING_SAVE',asOf:'2026-04-01',lines:[{account:'1400',debit:5,credit:0}]});
  test('[ACC1-OPENING-SYNC] The server decides permissions (403 kept); a save with only the 1400 line is refused on the device',()=>{
    assert.equal(refused.ok,false);assert.equal(empty.ok,false);assert.match(empty.text,/at least one opening line/);
    assert.equal(srv.commands('SaveOpeningBalances').length,0);
    const area=S.AREAS.find(a=>a.id==='accounting-opening');
    assert.deepEqual(area.commands.map(c=>c.type),['ACC_OPENING_SAVE','ACC_OPENING_APPROVE','ACC_OPENING_REOPEN']);
    // ACC-1 integration: the loader is registered (READS + tests/_role-reads.json).
    assert.equal(area.loaders.length,1);assert.deepEqual([...area.loaders[0].reads],['GET /accounting/opening']);assert.equal(area.loaders[0].kind,'patch');});
}
