// CU-PAY-B: payment-method master in hall booking (mock handler + sync prechecks) and in CTR_BILL_ISSUE on the device, and the PAY-08 duplicate guard
// matching an equal receipt across a method rename (by master row id, not by label). Names prefixed [CU-PAY-B].
import {build} from 'esbuild';
import {mkdir,writeFile,readFile} from 'node:fs/promises';
import {hallFixtures} from './_hall.mjs';

await mkdir('tests/.generated',{recursive:true});
await writeFile('tests/.generated/cu-pay-b-entry.ts',"export {hall} from '../../src/clients/sync/hall';\nexport {lfmoney} from '../../src/clients/sync/lfmoney';\n"
  +"export {mnySameMethod} from '../../src/domain/streams/money-master';\n");
await build({entryPoints:['tests/.generated/cu-pay-b-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/cu-pay-b.mjs',logLevel:'error'});
const X=await import('./.generated/cu-pay-b.mjs');

export async function register({test,assert,M}){
  const {quote}=hallFixtures(M);
  // Owner's master: Cash -> "Front till" (renamed), UPI -> "PhonePe UPI" (renamed), Card retired, Paytm wallet added (needs a reference), Room voucher added (none).
  const rows=()=>[
    {id:'pm-cash',code:'CASH',name:'Front till',sortOrder:10,active:true,kind:'cash',settlesTo:M.MNY_TILL,needsReference:false,usableAt:['frontdesk','pos','expenses'],ledger:'1000'},
    {id:'pm-upi',code:'UPI',name:'PhonePe UPI',sortOrder:20,active:true,kind:'upi',settlesTo:M.MNY_BANK,needsReference:true,usableAt:['frontdesk','pos','expenses'],ledger:'1010'},
    {id:'pm-card',code:'CARD',name:'Card',sortOrder:30,active:false,retiredOn:'2026-10-20',kind:'card',settlesTo:M.MNY_BANK,needsReference:true,usableAt:['frontdesk','pos'],ledger:'1020'},
    {id:'pm-bank',code:'BANK',name:'Bank transfer',sortOrder:40,active:true,kind:'bank',settlesTo:M.MNY_BANK,needsReference:true,usableAt:['frontdesk','expenses'],ledger:'1030'},
    {id:'pm-wallet',code:'WALLET',name:'Paytm wallet',sortOrder:45,active:true,kind:'wallet',settlesTo:M.MNY_BANK,needsReference:true,usableAt:['frontdesk'],ledger:'1010'},
    {id:'pm-voucher',code:'VOUCHER',name:'Room voucher',sortOrder:46,active:true,kind:'wallet',settlesTo:M.MNY_BANK,needsReference:false,usableAt:['frontdesk']},
    {id:'pm-pos-only',code:'ZOMATO',name:'Zomato',sortOrder:47,active:true,kind:'wallet',settlesTo:M.MNY_BANK,needsReference:false,usableAt:['pos']}];
  const history=[{id:'h1',master:'paymentMethods',rowId:'pm-cash',code:'CASH',field:'name',old:'Cash',new:'Front till',by:'owner',at:'2026-10-21T10:00'},
    {id:'h2',master:'paymentMethods',rowId:'pm-upi',code:'UPI',field:'name',old:'UPI',new:'PhonePe UPI',by:'owner',at:'2026-10-21T10:01'}];
  const withMaster=s=>({...s,shiftOpen:true,mastersX:{...(s.mastersX??{}),rows:{...(s.mastersX?.rows??{}),paymentMethods:rows()},history:[...history]}});
  const confirmed=()=>{let [s,qid]=quote(M.seed());s=M.execute(s,'manager',{type:'QUO_ACCEPT',id:qid,version:1,key:'cpb-acc'});return [withMaster(s),s.hallX.events.at(-1).id];};
  const adv=(s,id,over={})=>M.execute(s,'reception',{type:'HALL_ADVANCE_RECEIVE',eventId:id,amountPaise:100000,method:'PhonePe UPI',reference:'REF-1',...over});
  const lastReceipt=(s,id)=>s.hallX.events.find(e=>e.id===id).receipts.at(-1);

  test('[CU-PAY-B] hall mock: an Owner-added method is accepted; the receipt stores the current master name; a reference-free row needs none',()=>{
    let [s,id]=confirmed();
    s=adv(s,id,{method:'Paytm wallet',reference:'PTM-77'});assert.equal(lastReceipt(s,id).method,'Paytm wallet');
    s=adv(s,id,{method:'Room voucher',reference:undefined});assert.equal(lastReceipt(s,id).method,'Room voucher');
  });
  test('[CU-PAY-B] hall: Cash renamed (Front till) - a cash advance needs no reference and moves the drawer; UPI still needs its reference',()=>{
    const [s,id]=confirmed(),d0=M.drawer(s);
    const s1=adv(s,id,{method:'Front till',amountPaise:100000,reference:undefined});
    assert.equal(lastReceipt(s1,id).method,'Front till');assert.equal(M.drawer(s1),d0+100000,'the renamed cash row moves the drawer');
    const s2=adv(s,id,{method:'PhonePe UPI',amountPaise:100000,reference:'UPI-77'});assert.equal(M.drawer(s2),d0,'a UPI advance does not');
    assert.throws(()=>adv(s,id,{method:'PhonePe UPI',reference:undefined}),/Enter the payment reference/);
    const pre=X.hall.commands.find(c=>c.type==='HALL_ADVANCE_RECEIVE').precheck;
    assert.equal(pre({method:'Front till'},s),undefined,'device: no reference for the renamed cash row');
    assert.match(pre({method:'PhonePe UPI'},s),/Enter the payment reference/);
  });
  test('[CU-PAY-B] hall mock: an earlier name or the code resolves to the row and is stored under the CURRENT name',()=>{
    let [s,id]=confirmed();
    s=adv(s,id,{method:'UPI',reference:'REF-OLD'});assert.equal(lastReceipt(s,id).method,'PhonePe UPI');
    s=adv(s,id,{method:'CASH',reference:undefined});assert.equal(lastReceipt(s,id).method,'Front till','a renamed cash row needs no reference (the master decides, not the literal Cash)');
  });
  test('[CU-PAY-B] hall mock: retired, unknown and POS-only methods are refused; a reference-needing row without one is refused',()=>{
    const [s,id]=confirmed();
    assert.throws(()=>adv(s,id,{method:'Card'}),/retired/);
    assert.throws(()=>adv(s,id,{method:'Bitcoin'}),/Select a payment method \(Front till, PhonePe UPI, Bank transfer, Paytm wallet or Room voucher\)\./);
    assert.throws(()=>adv(s,id,{method:'Zomato'}),/Select a payment method/);
    assert.throws(()=>adv(s,id,{method:'Paytm wallet',reference:'x'}),/Enter the payment reference/);
  });
  const cancelled=(method)=>{ // an event with a 50,000 advance taken by `method`, cancelled (refund pending); the master is applied AFTER the advance (Card retired)
    let [s0,qid]=quote(M.seed(),{dates:['2026-12-03']});
    s0=M.execute(s0,'reception',{type:'QUO_ACCEPT',id:qid,advance:{amountPaise:5000000,method,reference:'REF-OLD'}});
    const id=s0.hallX.events.at(-1).id;let s=withMaster(s0);
    s=M.execute(s,'reception',{type:'HALL_EVENT_CANCEL',eventId:id,reason:'Date moved'});return [s,id];};
  test('[CU-PAY-B] hall mock: a retired method is allowed on a refund only when it is the row of one of the event receipts (DEC-MA-11a)',()=>{
    let [s,id]=cancelled('Card'); // advance by Card, Card retired afterwards -> the original receipt's row
    s=M.execute(s,'manager',{type:'HALL_REFUND',eventId:id,method:'Card',reference:'CARD-R1'});
    const e=s.hallX.events.find(x=>x.id===id);
    assert.equal(e.receipts.at(-1).kind,'REFUND');assert.equal(e.receipts.at(-1).method,'Card');assert.equal(e.receipts[0].method,'Card','the old receipt keeps its stored label');
    const [s2,id2]=cancelled('UPI'); // advance by UPI: retired Card is another row -> refused
    assert.throws(()=>M.execute(s2,'manager',{type:'HALL_REFUND',eventId:id2,method:'Card',reference:'CARD-R2'}),/Card is retired/);
    assert.equal(s2.hallX.events.find(x=>x.id===id2).receipts.length,1,'nothing written');
    s=M.execute(s2,'manager',{type:'HALL_REFUND',eventId:id2,method:'UPI',reference:'UPI-R2'}); // an active method is fine
    assert.equal(s.hallX.events.find(x=>x.id===id2).receipts.at(-1).method,'PhonePe UPI','stored under the current name');
  });
  test('[CU-PAY-B] hall sync: advance / accept / close / refund prechecks read the master and the request carries the current name',async()=>{
    const [s,id]=confirmed(),cmd=t=>X.hall.commands.find(c=>c.type===t);
    assert.equal(cmd('HALL_ADVANCE_RECEIVE').precheck({method:'PhonePe UPI',reference:'R-1'},s),undefined);
    assert.match(cmd('HALL_ADVANCE_RECEIVE').precheck({method:'Card',reference:'R-1'},s),/retired/);
    assert.match(cmd('HALL_ADVANCE_RECEIVE').precheck({method:'Bitcoin'},s),/^Select a payment method/);
    const [sc,idc]=cancelled('Card'),[su,idu]=cancelled('UPI');
    assert.equal(cmd('HALL_REFUND').precheck({id:idc,method:'Card',reference:'R-1'},sc),undefined,'a refund back by the retired method of the original receipt is allowed');
    assert.match(cmd('HALL_REFUND').precheck({id:idu,method:'Card',reference:'R-1'},su),/Card is retired/,'another retired row is refused');
    assert.match(cmd('HALL_ADVANCE_RECEIVE').precheck({method:'Front till'},{...s,shiftOpen:false}),/shift is closed/,'a till method needs the open shift');
    assert.match(cmd('QUO_ACCEPT').precheck({advance:{amountPaise:5000,method:'Bank transfer',reference:''}},s),/Enter the payment reference/);
    assert.equal(cmd('QUO_ACCEPT').precheck({id:'x'},s),undefined);
    assert.match(cmd('HALL_EVENT_CLOSE').precheck({settle:{mode:'TENDER',method:'Zomato',reference:'R'}},s),/Select a payment method/);
    assert.equal(cmd('HALL_EVENT_CLOSE').precheck({settle:{mode:'COMPANY'}},s),undefined);
    const calls=[];
    await cmd('HALL_ADVANCE_RECEIVE').send({cmd:{eventId:id,amountPaise:100000,method:'UPI',reference:'R-2'},state:s,ids:{server:()=>1,learn(){}},api:{command:async(n,b)=>{calls.push([n,b]);return {};}}});
    assert.equal(calls[0][1].method,'PhonePe UPI','an earlier name is sent under the current master name');
  });
  test('[CU-PAY-B] CTR_BILL_ISSUE precheck: a retired method is refused on the device (it used to pass); unknown, reference and shift rules unchanged',()=>{
    const [s]=confirmed(),ctr=X.lfmoney.commands.find(c=>c.type==='CTR_BILL_ISSUE'),pre=(cmd,st=s)=>ctr.precheck({settlement:{mode:'paid',...cmd}},st);
    assert.match(pre({method:'Card',reference:'R'}),/Card is retired — choose another payment method\./);
    assert.match(pre({method:'Bitcoin'}),/^Select a payment method/);
    assert.equal(pre({method:'PhonePe UPI',reference:'R-9'}),undefined);
    assert.equal(pre({method:'UPI',reference:'R-9'}),undefined,'earlier name');
    assert.equal(pre({method:'Room voucher'}),undefined);
    assert.equal(pre({method:'Paytm wallet'}),'Enter the transaction reference for a non-cash payment.');
    assert.ok(pre({method:'Zomato',reference:'R'}).startsWith('Select a payment method'),'POS-only row is not a front-desk method');
    assert.match(pre({method:'Front till'},{...s,shiftOpen:false}),/shift is closed/);
  });
  test('[CU-PAY-B] PAY-08: an equal receipt stored before a rename matches the one entered after it (by method id)',()=>{
    let s=M.seed();s=M.execute(s,'owner',{type:'BOOK',guest:'Rename Dup',room:'105',arrival:s.date,departure:'2026-10-26',people:2});
    const id=s.bookings.at(-1).id;s=M.execute(s,'owner',{type:'REGISTER',id});s=M.execute(s,'owner',{type:'CHECKIN',id});
    const pay=(st,over={})=>M.execute(st,'cashier',{type:'PAYMENT',id,amount:100000,kind:'payment',method:'UPI',reference:'DEMO-1',key:'k'+Math.random().toString(36).slice(2),...over});
    s=pay(s); // stored under "UPI"
    s=withMaster(s); // UPI renamed to "PhonePe UPI" afterwards
    s={...s,clock:M.addMinutes(s.clock,4)};
    assert.equal(s.bookings.find(b=>b.id===id).receipts.at(-1).method,'UPI');
    let err;try{pay(s,{method:'PhonePe UPI'});}catch(e){err=e;}
    assert.ok(err,'refused');assert.equal(err.details.reason,'DUPLICATE_SUSPECTED');
    assert.equal(pay(s,{method:'PhonePe UPI',confirmDuplicate:true}).bookings.find(b=>b.id===id).receipts.length,2,'confirmed second payment goes through');
    assert.equal(pay(s,{method:'Bank transfer',reference:'NEFT-1'}).bookings.find(b=>b.id===id).receipts.length,2,'another row with the same paise is not a duplicate');
    assert.equal(X.mnySameMethod(s,'UPI','PhonePe UPI'),true);assert.equal(X.mnySameMethod(s,'Cash','Front till'),true);assert.equal(X.mnySameMethod(s,'UPI','Bank transfer'),false);
    assert.equal(X.mnySameMethod(s,'Mystery','mystery'),true,'unresolvable values fall back to the label, case-insensitive');
    const b=s.bookings.find(x=>x.id===id);
    assert.ok(M.recentEqualReceipt(b,[{method:'PhonePe UPI',amount:100000}],s.clock,'payment',(p,q)=>X.mnySameMethod(s,p,q)),'matched across the rename');
    assert.equal(M.recentEqualReceipt(b,[{method:'PhonePe UPI',amount:100000}],s.clock,'payment'),undefined,'without the row test the labels differ (the old behaviour)');
  });
  test('[CU-PAY-B] source: hall modals show the reference by the row needsReference, no literal Cash test',async()=>{
    for(const f of ['ModalEvent','ModalQuote']){const t=await readFile(`src/features/hall/${f}.tsx`,'utf8');assert.ok(!/\.method!=='Cash'/.test(t),f+' still tests the literal Cash');assert.match(t,/needsReference/);}
    assert.ok(!/method!=='Cash'/.test(await readFile('src/services/mock/handlers/hall.ts','utf8')));
  });
}
