// AF-PAY08 (PAY-08, AF-DEC-1): an equal payment / deposit on the same folio within 10 minutes needs an explicit confirmation.
// Mock mirror of the server rule (RecordReceipt, details.reason DUPLICATE_SUSPECTED) + the pure helper recentEqualReceipt + source checks.
import {readFile} from 'node:fs/promises';

export function register({test,M,assert}){
  const inhouse=()=>{let s=M.seed();s=M.execute(s,'owner',{type:'BOOK',guest:'Dup Test',room:'105',arrival:s.date,departure:'2026-10-26',people:2});
    const id=s.bookings.at(-1).id;s=M.execute(s,'owner',{type:'REGISTER',id});s=M.execute(s,'owner',{type:'CHECKIN',id});return [s,id];};
  const pay=(s,id,over={})=>M.execute(s,'cashier',{type:'PAYMENT',id,amount:100000,kind:'payment',method:'UPI',reference:'DEMO-UPI-1',key:'k'+Math.random().toString(36).slice(2),...over});
  const later=(s,minutes)=>({...s,clock:M.addMinutes(s.clock,minutes)});
  const receipts=(s,id)=>s.bookings.find(b=>b.id===id).receipts;

  test('[AF-PAY08] mock: an equal payment within 10 minutes is refused with DUPLICATE_SUSPECTED and writes nothing',()=>{
    let [s,id]=inhouse();s=pay(s,id);const first=receipts(s,id).at(-1);s=later(s,4);const before=JSON.stringify(s);
    let err;try{pay(s,id);}catch(e){err=e;}
    assert.ok(err,'refused');assert.equal(err.code,'VALIDATION');
    assert.equal(err.details.reason,'DUPLICATE_SUSPECTED');assert.equal(err.details.receiptId,first.id);
    assert.match(err.message,/^A UPI payment of .* was recorded on this folio at \d\d:\d\d IST \((\d+) minutes ago\), receipt .*\. If this is a second, separate payment, confirm it\.$/);
    assert.equal(JSON.stringify(s),before,'the refused command changed nothing');assert.equal(receipts(s,id).length,1);
    assert.equal(M.folioDupDetails(err).receiptId,first.id);
  });
  test('[AF-PAY08] mock: confirmDuplicate posts the second receipt',()=>{
    let [s,id]=inhouse();s=pay(s,id);s=later(s,3);s=pay(s,id,{confirmDuplicate:true});
    assert.equal(receipts(s,id).length,2);assert.deepEqual(receipts(s,id).map(r=>r.amount),[100000,100000]);
  });
  test('[AF-PAY08] mock: a different amount or tender within the window is not flagged; a deposit is not a payment',()=>{
    let [s,id]=inhouse();s=pay(s,id);s=later(s,2);
    s=pay(s,id,{amount:100001});s=pay(s,id,{method:'Card',reference:'DEMO-CARD-1'});s=pay(s,id,{kind:'deposit'});
    assert.equal(receipts(s,id).length,4);
    assert.throws(()=>pay(s,id,{kind:'deposit'}),/second, separate payment/,'an equal deposit is flagged');
  });
  test('[AF-PAY08] mock: an equal payment after the 10-minute window is not flagged',()=>{
    let [s,id]=inhouse();s=pay(s,id);s=later(s,11);s=pay(s,id);
    assert.equal(receipts(s,id).length,2);
  });
  test('[AF-PAY08] mock: the same key replays the stored state and never reaches the guard',()=>{
    let [s,id]=inhouse();const cmd={type:'PAYMENT',id,amount:100000,kind:'payment',method:'UPI',reference:'DEMO-UPI-1',key:'same-key'};
    s=M.execute(s,'cashier',cmd);const again=M.execute(s,'cashier',cmd);
    assert.equal(again,s,'idempotent: the identical state comes back');assert.equal(receipts(again,id).length,1);
  });
  test('[AF-PAY08] mock: refunds are not affected (two equal refunds back to back)',()=>{
    let [s,id]=inhouse();s=pay(s,id,{amount:200000});const rid=receipts(s,id).at(-1).id;
    for(let i=0;i<2;i++){
      s=M.execute(s,'reception',{type:'REFUND_REQUEST',id,receiptId:rid,amount:30000,reason:'Early departure',key:'rr'+i});
      const a=s.approvals.at(-1);s=M.execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true});
      s=M.execute(s,'cashier',{type:'REFUND_EXECUTE',approvalId:a.id,method:'UPI',reference:'DEMO-R'+i,key:'re'+i});
    }
    assert.equal(receipts(s,id).filter(r=>r.kind==='refund').length,2);
  });
  test('[AF-PAY08] recentEqualReceipt: kind, method, exact paise and the 10-minute window',()=>{
    const b={receipts:[{id:'R1',amount:50000,method:'UPI',kind:'payment',at:'2026-10-24T10:00'},{id:'R2',amount:50000,method:'Card',kind:'payment',at:'2026-10-24T10:05'},
      {id:'R3',amount:70000,method:'UPI',kind:'deposit',at:'2026-10-24T10:06'},{id:'R4',amount:90000,method:'UPI',kind:'payment'}]};
    const rows=[{method:'UPI',amount:50000}];
    assert.equal(M.recentEqualReceipt(b,rows,'2026-10-24T10:04').receipt.id,'R1');
    assert.equal(M.recentEqualReceipt(b,rows,'2026-10-24T10:04').minutes,4);
    assert.equal(M.recentEqualReceipt(b,rows,'2026-10-24T10:10').receipt.id,'R1','exactly 10 minutes is inside');
    assert.equal(M.recentEqualReceipt(b,rows,'2026-10-24T10:11'),undefined,'11 minutes is outside');
    assert.equal(M.recentEqualReceipt(b,[{method:'UPI',amount:50001}],'2026-10-24T10:04'),undefined,'paise are exact');
    assert.equal(M.recentEqualReceipt(b,[{method:'UPI',amount:70000}],'2026-10-24T10:07'),undefined,'a deposit is not a payment');
    assert.equal(M.recentEqualReceipt(b,[{method:'UPI',amount:70000}],'2026-10-24T10:07','deposit').receipt.id,'R3');
    assert.equal(M.recentEqualReceipt(b,[{method:'upi',amount:50000}],'2026-10-24T10:04').receipt.id,'R1','method compared case-insensitively');
    assert.equal(M.recentEqualReceipt(b,[{method:'UPI',amount:90000}],'2026-10-24T10:04'),undefined,'a receipt without a time (loaded from the server) is never matched here');
    assert.equal(M.recentEqualReceipt(b,rows,'2026-10-24T10:04','refund'),undefined);
    assert.equal(M.recentEqualReceipt(b,rows,'2026-10-24T09:00'),undefined,'a receipt in the future of the clock is ignored');
  });
  test('[AF-V22 D21-1] a refused duplicate is shown once: one amber confirmation, one prominent tick, no "Not posted" panel, no early note, a short toast',async()=>{
    const ph=await readFile('src/mobile/money/Payment.tsx','utf8'),mod=await readFile('src/features/folio/modals.tsx','utf8'),app=await readFile('src/app/App.tsx','utf8');
    for(const src of [ph,mod]){assert.match(src,/early&&!dup&&/);assert.match(src,/Needs your confirmation/);assert.match(src,/<label className="folio-dup-tick">/);assert.match(src,/This is a second, separate payment/);}
    assert.match(ph,/failed&&!dup&&/);assert.match(app,/folioDupDetails\(r\.error\)\?DUP_TOAST/);assert.match(app,/if\(!folioDupDetails\(r\.error\)\)setError\(r\.text\)/);
  });
  test('[AF-PAY08] shell and dialog wiring: App.tsx announces a refused duplicate and refreshes after RESULT_UNKNOWN; the payment dialog offers the tick',async()=>{
    const app=await readFile('src/app/App.tsx','utf8'),mod=await readFile('src/features/folio/modals.tsx','utf8');
    assert.match(app,/\/\/ AF-PAY08 \(PAY-08\)[\s\S]*announceFolioDuplicate\(cmd\.key,r\.error\)[\s\S]*RESULT_UNKNOWN[\s\S]*getSyncRuntime\(\)\.refresh\(\)[\s\S]*\/\/ AF-PAY08 end/);
    assert.match(app,/setError\(''\);dirty\.current=false;/,'the a11y / phonefix pinned line is untouched');
    assert.match(app,/closeModal=\(\)=>\{if\(dirty\.current\)\{setDiscardAsk\(true\)/,'the discard guard is untouched');
    assert.match(mod,/This is a second, separate payment/);assert.match(mod,/confirmDuplicate:true/);assert.match(mod,/recentEqualReceipt\(bv?!?,/); // F8-SF-UI: the dialog checks the folio view (bv) on a split stay
  });
}
