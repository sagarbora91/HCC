// ACC-2 · Manual vouchers — production sync (src/clients/sync/accounting-manual.ts) against a fake server shaped like the .NET DTOs
// (08 Production/src/Hcc.Contracts/Accounting/ManualVoucherContracts.cs). Names prefixed [ACC2-MANUAL-SYNC].
import {loadSync,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

export async function register({test,assert}){
  const S=await loadSync();
  const mine={areas:S.AREAS.filter(a=>['core','accounting','accounting-manual'].includes(a.id))};
  const acc=capabilities({roles:['ACC'],grants:[['PERM-ACC-010','A'],['PERM-RPT-002','A']]});
  const result=(status,id=41)=>({voucherId:id,status,date:'2026-10-24',reversalId:null,version:'AAAAAAAAB9E='});
  const srv=fakeServer({'GET /me/capabilities':()=>acc,
    'POST /commands/SaveManualVoucher':()=>result('pending'),'POST /commands/DecideManualVoucher':()=>result('approved'),
    'POST /commands/VoidManualVoucher':()=>result('void'),'POST /commands/AddLoan':()=>({account:'2400/LN-HDFC',name:'HDFC term loan'})});
  const rt=await startRuntime(S,srv,mine);

  srv.reset();
  const lines=[{account:'5080',debit:59000,credit:0},{account:'1030/BANK-1',debit:0,credit:59000,note:' October '}];
  const saved=await rt.run({type:'ACC_MANUAL_SAVE',date:'2026-10-24',voucherType:'Payment',narration:' Bank charges ',reference:'STMT-OCT',template:'bank-charges',lines,key:'mv-1'});
  const edited=await rt.run({type:'ACC_MANUAL_SAVE',id:'41',date:'2026-10-24',voucherType:'Payment',narration:'Bank charges',lines});
  const short=await rt.run({type:'ACC_MANUAL_SAVE',date:'2026-10-24',voucherType:'Journal',narration:'x',lines:[lines[0]]});
  test('[ACC2-MANUAL-SYNC] ACC_MANUAL_SAVE → POST /commands/SaveManualVoucher {voucherId, date, voucherType, narration, reference, template, lines in paise}',()=>{
    assert.ok(saved.ok,JSON.stringify(saved));assert.ok(edited.ok,JSON.stringify(edited));
    const sent=srv.commands('SaveManualVoucher');assert.equal(sent.length,2);
    assert.deepEqual(sent[0].body,{voucherId:null,date:'2026-10-24',voucherType:'Payment',narration:'Bank charges',reference:'STMT-OCT',template:'bank-charges',
      lines:[{account:'5080',debitPaise:59000,creditPaise:0,note:null},{account:'1030/BANK-1',debitPaise:0,creditPaise:59000,note:'October'}]});
    assert.equal(sent[0].key,'mv-1');assert.equal(sent[1].body.voucherId,41);
    assert.equal(short.ok,false);assert.match(short.text,/2 to 100 lines/);});

  srv.reset();
  const ok=await rt.run({type:'ACC_MANUAL_DECIDE',id:'41',approve:true});
  const noReason=await rt.run({type:'ACC_MANUAL_DECIDE',id:'41',approve:false,reason:''});
  const rej=await rt.run({type:'ACC_MANUAL_DECIDE',id:'41',approve:false,reason:' Wrong bank '});
  const voided=await rt.run({type:'ACC_MANUAL_VOID',id:'41',reason:'Duplicate entry'});
  const voidNo=await rt.run({type:'ACC_MANUAL_VOID',id:'41'});
  test('[ACC2-MANUAL-SYNC] DECIDE → DecideManualVoucher (a rejection needs a reason, refused on the device); VOID → VoidManualVoucher {voucherId, reason}',()=>{
    assert.ok(ok.ok&&rej.ok&&voided.ok,JSON.stringify([ok,rej,voided]));
    assert.deepEqual(srv.commands('DecideManualVoucher').map(c=>c.body),[{voucherId:41,approve:true,reason:null,expectedVersion:null},{voucherId:41,approve:false,reason:'Wrong bank',expectedVersion:null}]);
    assert.deepEqual(srv.commands('VoidManualVoucher').map(c=>c.body),[{voucherId:41,reason:'Duplicate entry',expectedVersion:null}]);
    assert.equal(noReason.ok,false);assert.equal(voidNo.ok,false);assert.match(voidNo.text,/reason/);});

  srv.reset();
  srv.on('POST /commands/DecideManualVoucher',()=>reply(403,{code:'FORBIDDEN',message:'Not permitted: No permission.'}));
  const refused=await rt.run({type:'ACC_MANUAL_DECIDE',id:'41',approve:true});
  const loan=await rt.run({type:'ACC_LOAN_ADD',name:'HDFC term loan',lender:'HDFC Bank (fictional)'});
  test('[ACC2-MANUAL-SYNC] the server decides permissions (403 kept); ACC_LOAN_ADD needs PERM-ACC-007 on the device; mappers and the loader shape',()=>{
    assert.equal(refused.ok,false);assert.equal(loan.ok,false);assert.match(loan.text,/Only the Owner adds a loan/);assert.equal(srv.commands('AddLoan').length,0);
    const area=S.AREAS.find(a=>a.id==='accounting-manual');
    assert.deepEqual(area.commands.map(c=>c.type),['ACC_MANUAL_SAVE','ACC_MANUAL_DECIDE','ACC_MANUAL_VOID','ACC_LOAN_ADD']);
    assert.equal(area.loaders.length,1);assert.deepEqual([...area.loaders[0].reads],['GET /accounting/manual']);assert.equal(area.loaders[0].kind,'patch'); // ACC-2 integration
  });
}
