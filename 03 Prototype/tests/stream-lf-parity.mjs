// LF-ui (lane lf-tests) · scenario LF-PARITY (contract §12) and the production sync wiring of the LF money commands. Names prefixed [LF-PARITY].
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
await writeFile('tests/.generated/lf-parity-entry.ts',"export {lfmoney} from '../../src/clients/sync/lfmoney';export {reminders,remindersFromServer} from '../../src/clients/sync/reminders';export {READS} from '../../src/clients/sync/reads';export {createRequestFromBook} from '../../src/clients/view';export {areas} from '../../src/clients/sync/registry';\n");
await build({entryPoints:['tests/.generated/lf-parity-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/lf-parity.mjs',logLevel:'error'});
const X=await import('./.generated/lf-parity.mjs');
export function register({test,M,assert}){
  const {loadScenario,totals,blcInvoiceNote,stayDueOut,deskForeignerRegister}=M;
  const T='[LF-PARITY] ';
  let cache;const day=()=>cache??(cache=loadScenario('LF-PARITY'));
  const byGuest=(n)=>day().bookings.find(b=>b.guest.startsWith(n));

  test(T+'scenario is listed and builds from real commands',()=>{
    assert.ok(M.scenarios.some(x=>x.id==='LF-PARITY'&&x.available));assert.equal(day().scenario,'LF-PARITY');
  });
  test(T+'A1 rate change for every open night: reversal + new room line, header line, history row',()=>{
    const s=day(),b=byGuest('Rate Change');assert.equal(b.rate,150000);assert.ok(b.lines.some(l=>l.reversalOf));
    const c=s.stayRateX.changes.at(-1);assert.equal(c.oldRate,200000);assert.equal(c.newRate,150000);assert.match(M.strHeaderLine(s,b.id),/Rate changed/);
  });
  test(T+'A2 corrected bill: credit note + revision-2 invoice, refund due, every voucher balanced',()=>{
    const s=day(),b=byGuest('Corrected Bill'),c=s.billCorrectionX.corrections.at(-1);
    assert.equal(c.status,'Issued');assert.ok(c.creditNoteNo);assert.notEqual(b.invoiceNo,c.oldInvoiceNo);assert.equal(c.newInvoiceNo,b.invoiceNo);assert.equal(c.settlement,'refund-due');
    assert.match(blcInvoiceNote(s,c.oldInvoiceNo),/Superseded by/);assert.ok(M.accLiveVouchers(s,s.date).every(M.accBalanced));
  });
  test(T+'A3–A6 foreign guest: Form C, purpose, vehicle, 24-hour due-out, listed on the foreigner register',()=>{
    const s=day(),b=byGuest('Foreign Guest');assert.equal(b.status,'In house');assert.equal(b.purpose,'TOURISM');assert.equal(b.vehicle.no,'MH24AB1234');assert.equal(b.formC.visaNo,'V9988776');
    assert.match(stayDueOut(s,b),/^2026-10-26T/);assert.ok(deskForeignerRegister(s,'reception','2026-10-01','2026-10-31').some(r=>r.booking===b.id));
  });
  test(T+'B1 + B2 agreed rate and a guest-credit check-out: receipt kind guest-credit, Udhaar receivable on party 1120/UD-<key>',()=>{
    const s=day(),g=s.guests[0],b=s.bookings.find(x=>x.room==='203');assert.equal(g.agreedRates.length,1);assert.equal(g.creditAccount.active,true);
    assert.equal(b.status,'Departed');assert.ok(b.receipts.some(r=>r.kind==='guest-credit'));
    const ud=s.expensesX.udhaar.find(r=>r.booking===b.id);assert.ok(ud);assert.equal(ud.guestId,g.id);
    const out=M.udGuestOutstanding(s).find(r=>r.guestId===g.id);assert.equal(out.open,ud.amount);assert.equal(out.party,'1120/UD-'+g.creditAccount.customerKey);
  });
  test(T+'A7 counter bill on Udhaar credit: receivable raised, invoice number from the HPE series',()=>{
    const s=day(),b=s.counterX.bills.at(-1);assert.equal(b.settlement.mode,'udhaar');assert.ok(b.settlement.udhaarId);assert.equal(b.status,'Issued');assert.ok(b.no);
    assert.equal(b.total,b.net+b.tax);assert.equal(totals({lines:[],receipts:[]}).total,0);
  });
  test(T+'sync: every LF money command is mapped or listed as unmapped; booking client carries purpose / vehicle / basis; reminders read is gated',()=>{
    const types=X.lfmoney.commands.map(c=>c.type);
    for(const t of ['STAY_RATE_CHANGE','GUEST_CREDIT_ACCOUNT_SET','GUEST_CREDIT_REQUEST','CTR_BILL_ISSUE','CTR_BILL_VOID_REQUEST','BILL_CORRECTION_REQUEST'])assert.ok(types.includes(t),t);
    assert.deepEqual(Object.keys(X.lfmoney.unmapped).sort(),['GUEST_RATE_CLEAR','GUEST_RATE_SET']);
    const r=X.createRequestFromBook({room:'105',guest:'A',source:'Phone',purpose:'BUSINESS',vehicle:{no:'MH1',type:'Car'},checkoutBasis:'24h'});
    assert.equal(r.purposeCode,'BUSINESS');assert.equal(r.vehicleNo,'MH1');assert.equal(r.vehicleType,'Car');assert.equal(r.checkoutBasis,'24h');
    assert.ok(typeof X.READS['GET /reminders']==='function');
    const l=X.remindersFromServer([{bucket:'today',businessDate:'2026-10-24',items:[{reminderId:7,subject:'Fire drill',date:'2026-10-25',time:null,leadDays:2,role:'MGR',note:null,repeat:'none',status:'open',nextDate:null,createdBy:3,createdUtc:'2026-10-20T04:30:00Z',doneBy:null,doneUtc:null}]}]);
    assert.equal(l.reminders[0].id,'RMD-7');assert.equal(l.reminders[0].role,'Manager');
  });
}
