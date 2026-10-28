// ACC-2 acc2-gst · production sync (src/clients/sync/accounting-gst.ts) against a fake server shaped like the .NET DTOs
// (08 Production/src/Hcc.Contracts/Accounting/GstContracts.cs). Names prefixed [ACC2-GST-SYNC].
import {loadSync,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

export async function register({test,assert}){
  const S=await loadSync();
  const mine={areas:S.AREAS.filter(a=>['core','accounting','accounting-gst'].includes(a.id))};
  // An Accountant without the accounting view: the accounting loader sends no read, so only the commands talk to the server.
  const acc=capabilities({roles:['ACC'],grants:[['PERM-ACC-013','A'],['PERM-ACC-012','R']]});
  const ok=()=>({month:'2026-04',postedOn:'2026-05-01',sourceKey:'gst-setoff:2026-04@2026-05-01',payablePaise:2900000,challanPaise:2890000,unchanged:false,replaced:null});
  const remit=()=>({month:'2026-04',template:'statutory-remittance',voucherId:'MV-1',voucherStatus:'pending',complianceCard:'my5',complianceNote:'compliance card my5 marked filed'});
  const lock=()=>({month:'2026-04',status:'Locked',daysLocked:30,setoffKey:'gst-setoff:2026-04@2026-05-01',expenseMonthLocked:true});
  const srv=fakeServer({'GET /me/capabilities':()=>acc,'POST /commands/PostGstSetoff':ok,'POST /commands/RecordGstPayment':remit,'POST /commands/RecordStatutoryRemittance':remit,
    'POST /commands/LockAccountingMonth':lock,'POST /commands/RequestAccountingMonthLock':lock,'POST /commands/UnlockAccountingMonth':lock,
    'POST /commands/SetGstOnAdvances':()=>({on:true})});
  const rt=await startRuntime(S,srv,mine);

  srv.reset();
  const setoff=await rt.run({type:'ACC_GST_SETOFF',month:'2026-04',key:'setoff-1'});
  const bad=await rt.run({type:'ACC_GST_SETOFF',month:'2026-4'});
  const pay=await rt.run({type:'ACC_GST_PAY',month:'2026-04',amount:2890000,method:'Bank transfer',challan:' CIN-DEMO-0520 ',date:'2026-05-20',rcm:40000});
  const noChallan=await rt.run({type:'ACC_GST_PAY',month:'2026-04',amount:100,method:'Bank transfer',challan:''});
  const stat=await rt.run({type:'ACC_STAT_REMIT',kind:'pf',month:'2026-04',amount:3600000,method:'Bank transfer',challan:'EPF-TRRN-0515',date:'2026-05-15'});
  const badKind=await rt.run({type:'ACC_STAT_REMIT',kind:'tds',month:'2026-04',amount:1,method:'Cash',challan:'XYZ'});
  test('[ACC2-GST-SYNC] ACC_GST_SETOFF → PostGstSetoff {month} with the key; GST_PAY / STAT_REMIT → paise bodies; bad month, challan and kind refused on the device',()=>{
    assert.ok(setoff.ok,JSON.stringify(setoff));
    assert.deepEqual(srv.commands('PostGstSetoff').map(c=>[c.body,c.key]),[[{month:'2026-04'},'setoff-1']]);
    assert.equal(bad.ok,false);assert.match(bad.text,/Choose a month/);
    assert.ok(pay.ok,JSON.stringify(pay));
    assert.deepEqual(srv.commands('RecordGstPayment').map(c=>c.body),[{month:'2026-04',amountPaise:2890000,method:'Bank transfer',challan:'CIN-DEMO-0520',date:'2026-05-20',rcmPaise:40000}]);
    assert.equal(noChallan.ok,false);assert.match(noChallan.text,/challan/);
    assert.ok(stat.ok,JSON.stringify(stat));
    assert.deepEqual(srv.commands('RecordStatutoryRemittance').map(c=>c.body),[{kind:'pf',month:'2026-04',amountPaise:3600000,method:'Bank transfer',challan:'EPF-TRRN-0515',date:'2026-05-15'}]);
    assert.equal(badKind.ok,false);assert.match(badKind.text,/PF, ESIC or professional tax/);
  });

  srv.reset();
  const req=await rt.run({type:'ACC_MONTH_LOCK_REQUEST',month:'2026-04'});
  srv.on('POST /commands/LockAccountingMonth',()=>reply(403,{code:'FORBIDDEN',message:'Not permitted: Request only: another user approves.'}));
  const refused=await rt.run({type:'ACC_MONTH_LOCK',month:'2026-04'});
  const noReason=await rt.run({type:'ACC_MONTH_UNLOCK',month:'2026-04',reason:'x'});
  const unlocked=await rt.run({type:'ACC_MONTH_UNLOCK',month:'2026-04',reason:'  Late vendor bill '});
  const sw=await rt.run({type:'ACC_GST_ADVANCES_SET',on:true});
  test('[ACC2-GST-SYNC] month lock request / lock (server decides, 403 kept) / unlock {reason}; SetGstOnAdvances {on}; the area maps exactly its seven commands',()=>{
    assert.ok(req.ok,JSON.stringify(req));assert.deepEqual(srv.commands('RequestAccountingMonthLock').map(c=>c.body),[{month:'2026-04'}]);
    assert.equal(refused.ok,false);
    assert.equal(noReason.ok,false);assert.match(noReason.text,/reason is required/);
    assert.ok(unlocked.ok,JSON.stringify(unlocked));assert.deepEqual(srv.commands('UnlockAccountingMonth').map(c=>c.body),[{month:'2026-04',reason:'Late vendor bill'}]);
    assert.ok(sw.ok,JSON.stringify(sw));assert.deepEqual(srv.commands('SetGstOnAdvances').map(c=>c.body),[{on:true}]);
    const area=S.AREAS.find(a=>a.id==='accounting-gst');
    assert.deepEqual(area.commands.map(c=>c.type),['ACC_GST_SETOFF','ACC_GST_PAY','ACC_STAT_REMIT','ACC_MONTH_LOCK','ACC_MONTH_LOCK_REQUEST','ACC_MONTH_UNLOCK','ACC_GST_ADVANCES_SET']);
    assert.equal(area.loaders.length,0,'the GET /accounting/gst/locks loader is registered at integration (READS + _role-reads.json)');
  });
}
