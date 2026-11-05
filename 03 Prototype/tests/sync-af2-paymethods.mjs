// AF2-PAYMETHODS (verifier note O8): the folio PAYMENT precheck, the refund / company-payment prechecks and the phone + desktop payment checks take the
// methods from the payment-methods master (mst.payment_method / s.mastersX.rows.paymentMethods), not a fixed Cash / UPI / Card / Bank transfer list:
// retired methods refused (server words), a row that needs a reference asks for one, a row that settles to a till needs the open shift and sends the
// till, renamed methods (earlier name or code) are sent under the master's current name. PAY-08 (split replay keys) unchanged. Names prefixed [AF2-PM].
import {build} from 'esbuild';
import {mkdir,writeFile,readFile} from 'node:fs/promises';

await mkdir('tests/.generated',{recursive:true});
await writeFile('tests/.generated/af2-pm-entry.ts',"export {money,folioTender,folioIsDrawer} from '../../src/clients/sync/money';\nexport {HccClientError} from '../../src/clients/errors';\n"
  +"export {mmPaymentProblem} from '../../src/mobile/money/logic';\nexport {mnyPick,mnyChooseText,mnyNames} from '../../src/domain/streams/money-master';\n");
await build({entryPoints:['tests/.generated/af2-pm-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/af2-pm.mjs',logLevel:'error'});
const X=await import('./.generated/af2-pm.mjs');

export async function register({test,assert,M}){
  const mapper=type=>X.money.commands.find(c=>c.type===type);
  // The Owner's master: Cash renamed "Front till" (still settles to the till), UPI renamed "PhonePe UPI", Card retired, two Owner-added methods
  // (a wallet that needs a reference, a room voucher that does not), Bank transfer unchanged.
  const rows=[
    {id:'pm-cash',code:'CASH',name:'Front till',sortOrder:10,active:true,kind:'cash',settlesTo:M.MNY_TILL,needsReference:false,usableAt:['frontdesk','pos','expenses'],ledger:'1000'},
    {id:'pm-upi',code:'UPI',name:'PhonePe UPI',sortOrder:20,active:true,kind:'upi',settlesTo:M.MNY_BANK,needsReference:true,usableAt:['frontdesk','pos','expenses'],ledger:'1010'},
    {id:'pm-card',code:'CARD',name:'Card',sortOrder:30,active:false,retiredOn:'2026-10-20',kind:'card',settlesTo:M.MNY_BANK,needsReference:true,usableAt:['frontdesk','pos'],ledger:'1020'},
    {id:'pm-bank',code:'BANK',name:'Bank transfer',sortOrder:40,active:true,kind:'bank',settlesTo:M.MNY_BANK,needsReference:true,usableAt:['frontdesk','expenses'],ledger:'1030'},
    {id:'pm-wallet',code:'WALLET',name:'Paytm wallet',sortOrder:45,active:true,kind:'wallet',settlesTo:M.MNY_BANK,needsReference:true,usableAt:['frontdesk'],ledger:'1010'},
    {id:'pm-voucher',code:'VOUCHER',name:'Room voucher',sortOrder:46,active:true,kind:'wallet',settlesTo:M.MNY_BANK,needsReference:false,usableAt:['frontdesk']},
    {id:'pm-pos-only',code:'ZOMATO',name:'Zomato',sortOrder:47,active:true,kind:'wallet',settlesTo:M.MNY_BANK,needsReference:false,usableAt:['pos']},
    {id:'pm-company-credit',code:'COMPANY',name:'Company credit',sortOrder:60,active:true,kind:'credit',needsReference:false,usableAt:[]},
  ];
  const history=[{id:'h1',master:'paymentMethods',rowId:'pm-cash',code:'CASH',field:'name',old:'Cash',new:'Front till',by:'owner',at:'2026-10-21T10:00'},
    {id:'h2',master:'paymentMethods',rowId:'pm-upi',code:'UPI',field:'name',old:'UPI',new:'PhonePe UPI',by:'owner',at:'2026-10-21T10:01'}];
  const base=()=>{let s=M.seed();s=M.execute(s,'owner',{type:'BOOK',guest:'Method Test',room:'105',arrival:s.date,departure:'2026-10-26',people:2});const id=s.bookings.at(-1).id;
    s=M.execute(s,'owner',{type:'REGISTER',id});s=M.execute(s,'owner',{type:'CHECKIN',id});
    s={...s,shiftOpen:true,mastersX:{...(s.mastersX??{}),rows:{...(s.mastersX?.rows??{}),paymentMethods:rows.map(r=>({...r,usableAt:[...r.usableAt]}))},history:[...history]}};
    return [s,id];};
  const [s,id]=base();
  const pre=(cmd,st=s)=>mapper('PAYMENT').precheck({type:'PAYMENT',id,kind:'payment',...cmd},st,undefined);
  const ctx=(cmd,calls,fail)=>({cmd:{type:'PAYMENT',id,kind:'payment',...cmd},state:s,
    ids:{server:(k,x)=>k==='folio'?5001:k==='till'&&x==='desk'?3:undefined,version:()=>'FOLv1'},
    api:{command:async(name,body,opt)=>{calls.push({name,body,step:opt?.step});const f=fail?.(body,opt);if(f)throw f;return {receiptId:calls.length};}}});
  const dupErr=()=>new X.HccClientError('VALIDATION','A Paytm wallet payment of ₹500.00 was recorded on this folio at 10:00 IST (2 minutes ago), receipt 77. If this is a second, separate payment, confirm it.',
    {reason:'DUPLICATE_SUSPECTED',receiptId:77},400);

  test('[AF2-PM] the precheck offers and accepts the master methods: an Owner-added wallet with its reference, a voucher without one',()=>{
    assert.equal(pre({amount:50000,method:'Paytm wallet',reference:'PTM-1'}),undefined);
    assert.equal(pre({amount:50000,method:'Room voucher',reference:''}),undefined,'a row without "needs reference" posts without one');
    assert.deepEqual(X.mnyNames(s,'frontdesk'),['Front till','PhonePe UPI','Bank transfer','Paytm wallet','Room voucher']);
  });
  test('[AF2-PM] a reference-required method asks for its reference (server words)',()=>{
    assert.equal(pre({amount:50000,method:'Paytm wallet',reference:' '}),'Enter the transaction reference for a non-cash payment.');
    assert.equal(pre({amount:50000,method:'Bank transfer'}),'Enter the transaction reference for a non-cash payment.');
  });
  test('[AF2-PM] a retired method is refused before any request; an unknown or POS-only method lists the active front-desk methods',()=>{
    assert.equal(pre({amount:50000,method:'Card',reference:'C-1'}),'Card is retired — choose another payment method.');
    const list='Select a payment method (Front till, PhonePe UPI, Bank transfer, Paytm wallet or Room voucher).';
    assert.equal(pre({amount:50000,method:'Cheque',reference:'x'}),list);
    assert.equal(pre({amount:50000,method:'Zomato',reference:'x'}),list,'usable at the outlet only');
    assert.equal(pre({amount:50000,method:'Company credit'}),list,'company credit is applied through an approved request only');
    assert.equal(pre({amount:50000,method:''}),list);
  });
  test('[AF2-PM] a renamed method still works: earlier name, code and other case resolve, and the request carries the current name',async()=>{
    for(const m of ['UPI','upi','PhonePe UPI',' phonepe upi ']) assert.equal(pre({amount:50000,method:m,reference:'U-1'}),undefined,m);
    assert.equal(X.folioTender(s,'UPI'),'PhonePe UPI');assert.equal(X.folioTender(s,'WALLET'),'Paytm wallet');assert.equal(X.folioTender(s,'Cash'),'Front till');
    const calls=[];await mapper('PAYMENT').send(ctx({amount:50000,method:'UPI',reference:'U-1'},calls));
    assert.deepEqual(calls.map(c=>[c.name,c.body.tender,c.body.tillId,c.body.reference,c.body.confirmDuplicate]),[['RecordReceipt','PhonePe UPI',null,'U-1',false]]);
  });
  test('[AF2-PM] a till-settling method (renamed from Cash) needs the open shift and sends the desk till; the old name "Cash" does too',async()=>{
    const closed={...s,shiftOpen:false};
    assert.match(pre({amount:50000,method:'Front till'},closed),/cash shift is closed/i);
    assert.match(pre({amount:50000,method:'Cash'},closed),/cash shift is closed/i,'the earlier name resolves to the drawer row');
    assert.equal(pre({amount:50000,method:'Paytm wallet',reference:'P'},closed),undefined,'a non-drawer method needs no shift');
    assert.equal(X.folioIsDrawer(s,'Front till'),true);assert.equal(X.folioIsDrawer(s,'Cash'),true);assert.equal(X.folioIsDrawer(s,'Paytm wallet'),false);
    assert.equal(X.folioIsDrawer(M.seed(),'Cash'),true);assert.equal(X.folioIsDrawer(s,'Legacy cash'),false,'an unknown value is drawer cash only when it is literally "Cash"');
    const calls=[];await mapper('PAYMENT').send(ctx({amount:50000,method:'Cash',reference:''},calls));
    assert.deepEqual(calls.map(c=>[c.body.tender,c.body.tillId]),[['Front till',3]]);
  });
  test('[AF2-PM] split payment with mixed methods: each row checked against the master, canonical names, till only on the drawer row, keys t1…t3',async()=>{
    const tenders=[{method:'Cash',amount:20000,reference:''},{method:'WALLET',amount:20000,reference:'PTM-2'},{method:'Room voucher',amount:10000,reference:''}];
    assert.equal(pre({amount:50000,tenders}),undefined);
    assert.equal(pre({amount:50000,tenders:[tenders[0],{...tenders[1],reference:''},tenders[2]]}),'Enter the transaction reference for a non-cash payment.');
    assert.equal(pre({amount:50000,tenders:[tenders[0],{method:'Card',amount:20000,reference:'C'},tenders[2]]}),'Card is retired — choose another payment method.');
    assert.match(pre({amount:50000,tenders},{...s,shiftOpen:false}),/cash shift is closed/i);
    const calls=[];await mapper('PAYMENT').send(ctx({amount:50000,tenders},calls));
    assert.deepEqual(calls.map(c=>[c.step,c.body.tender,c.body.tillId,c.body.amountPaise,c.body.confirmDuplicate]),
      [['t1','Front till',3,20000,false],['t2','Paytm wallet',null,20000,false],['t3','Room voucher',null,10000,false]]);
  });
  test('[AF2-PM] PAY-08 unchanged on a mixed split: the confirmed resend replays each row under tN, sends only the refused row as confirm-tN',async()=>{
    const tenders=[{method:'Cash',amount:30000,reference:''},{method:'Paytm wallet',amount:50000,reference:'PTM-3'}];
    const calls=[];await mapper('PAYMENT').send(ctx({amount:80000,tenders,confirmDuplicate:true},calls,(b,o)=>o?.step==='t2'&&b.confirmDuplicate===false?dupErr():null));
    assert.deepEqual(calls.map(c=>[c.step,c.body.tender,c.body.confirmDuplicate]),[['t1','Front till',false],['t2','Paytm wallet',false],['confirm-t2','Paytm wallet',true]]);
    const calls2=[];const e=await mapper('PAYMENT').send(ctx({amount:80000,tenders},calls2,(b,o)=>o?.step==='t2'?dupErr():null)).then(()=>null,x=>x);
    assert.ok(e,'refused');assert.equal(e.details.reason,'DUPLICATE_SUSPECTED');assert.match(e.message,/Tender rows 1 to 1 were already recorded; check the folio before confirming\.$/);
  });
  test('[AF2-PM] refund execution: back by the original (retired) method is allowed, another retired or unknown method is refused, a drawer method needs the shift',()=>{
    let st=s;st={...st,bookings:st.bookings.map(b=>b.id===id?{...b,receipts:[...b.receipts,{id:'R-CARD',amount:40000,method:'Card',kind:'payment',date:st.date,reference:'C-9'},
      {id:'R-OLD',amount:40000,method:'UPI',kind:'payment',date:st.date,reference:'U-9'}]}:b),
      approvals:[...st.approvals,{id:'A-1',kind:'refund',booking:id,target:'R-CARD',amount:10000,status:'Approved'},{id:'A-2',kind:'refund',booking:id,target:'R-OLD',amount:10000,status:'Approved'}]};
    const r=cmd=>mapper('REFUND_EXECUTE').precheck({type:'REFUND_EXECUTE',...cmd},st,undefined);
    assert.equal(r({approvalId:'A-1'}),undefined,'the original retired method, by default');
    assert.equal(r({approvalId:'A-1',method:'Card',reference:'C-R'}),undefined,'the original retired method, named');
    assert.equal(r({approvalId:'A-2',method:'Card',reference:'C-R'}),'Card is retired — choose another payment method.');
    assert.equal(r({approvalId:'A-2',method:'PhonePe UPI'}),'Enter the transaction reference for a non-cash payment.');
    assert.equal(r({approvalId:'A-2',method:'PhonePe UPI',reference:'U-R'}),undefined);
    assert.match(r({approvalId:'A-2',method:'Cheque'}),/^Select a payment method \(/);
    assert.equal(r({approvalId:'A-2',method:'Front till'}),undefined,'a drawer method with the shift open');
    assert.match(mapper('REFUND_EXECUTE').precheck({type:'REFUND_EXECUTE',approvalId:'A-2',method:'Cash'},{...st,shiftOpen:false},undefined),/cash shift is closed/i);
  });
  test('[AF2-PM] company payment precheck: a till-settling method is refused, a wallet from the master is taken with its reference',async()=>{
    const p=cmd=>mapper('GRP_COMPANY_PAYMENT').precheck({type:'GRP_COMPANY_PAYMENT',amount:10000,company:'5',...cmd},s,undefined);
    assert.match(p({method:'Front till'}),/A company pays by bank transfer, UPI or card/);
    assert.match(p({method:'Cash'}),/A company pays by bank transfer, UPI or card/);
    assert.equal(p({method:'Card',reference:'x'}),'Card is retired — choose another payment method.');
    assert.equal(p({method:'Paytm wallet'}),'Enter the transaction reference for a non-cash payment.');
    assert.equal(p({method:'Paytm wallet',reference:'PTM-9'}),undefined);
  });
  test('[AF2-PM] mnyPick mirrors PaymentMethodBook.Pick / Choose (texts per place; seed list = the old fixed words)',()=>{
    const seed=M.seed();
    assert.equal(X.mnyChooseText(seed,'frontdesk'),'Select a payment method (Cash, UPI, Card or Bank transfer).');
    assert.equal(X.mnyChooseText(seed,'pos'),'Select Cash, UPI or Card.');
    assert.equal(X.mnyChooseText(seed,'expenses'),'Choose a funding source.');
    assert.equal(X.mnyPick(seed,'cash','frontdesk','').method.name,'Cash');
    assert.equal(X.mnyPick(s,'Card','frontdesk','x',{allowRetired:true}).method.name,'Card');
    assert.equal(X.mnyPick(s,'Card','frontdesk','x').retired,true);
    assert.equal(X.mnyPick(s,'Paytm wallet','frontdesk','',{requireReference:false}).method.code,'WALLET');
  });
  test('[AF2-PM] phone check (mmPaymentProblem) refuses a retired / unknown method with the server words; the dialogs use the master rows',async()=>{
    assert.equal(X.mmPaymentProblem('payment','500',[{method:'Card',amount:'',reference:'C'}],0,s),'Card is retired — choose another payment method.');
    assert.equal(X.mmPaymentProblem('payment','500',[{method:'Paytm wallet',amount:'',reference:''}],0,s),'Enter a demonstration transaction reference for each non-cash tender.');
    assert.equal(X.mmPaymentProblem('payment','500',[{method:'Room voucher',amount:'',reference:''}],0,s),'');
    assert.equal(X.mmPaymentProblem('payment','500',[{method:'Cash',amount:'',reference:''}],0),'','without state: the old rule');
    const src=await readFile('src/clients/sync/money.ts','utf8'),ph=await readFile('src/mobile/money/Payment.tsx','utf8'),mod=await readFile('src/features/folio/modals.tsx','utf8');
    assert.doesNotMatch(src,/\['Cash','UPI','Card','Bank transfer'\]/,'no fixed method list in the folio precheck');
    assert.doesNotMatch(src,/method!=='Cash'/,'no literal Cash test for the till');
    assert.match(ph,/mnyMethod\(s,r\.method\)\?\.needsReference/,'the phone sheet asks for a reference by the master row');
    assert.match(mod,/mnyPick\(s,r\.method,'frontdesk'/,'the desktop dialog checks the master row');
  });
}
