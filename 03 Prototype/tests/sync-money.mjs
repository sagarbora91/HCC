// W4-3 money area (sync/money.ts): folio, cashier / payments, cash book, invoices & credit notes, groups billing — against a fake .NET API.
// The folio is the prototype's own worked ₹4,265 journey (execute: nights + laundry + approved discount + cash advance + deposit), served in
// the server's shape; the loaders must give the screens the same lines, receipts and totals, paise-exact, without recomputing GST.
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities,ownErrors} from './_sync-harness.mjs';

const CMDS=['RecordReceipt','PostCharge','RequestAdjustment','DecideApproval','ExecuteAdjustment','CheckoutStay','IssueInvoice','RequestCreditNote','DecideCreditNote',
  'SubmitShiftCount','LockCashBookDay','ReopenCashBookDay','CreateGroupBlock','CreateGroup','SaveCompany','SetCompanyLimit','DecideCompanyLimit'];

export async function register({test,assert,M}){
  const S=await loadSync();
  // ── the prototype's own figures ──
  let p=M.seed();p=M.execute(p,'owner',{type:'BOOK',guest:'Journey Test',room:'105',arrival:p.date,departure:'2026-10-26',people:2});const pid=p.bookings.at(-1).id;
  p=M.execute(p,'owner',{type:'REGISTER',id:pid});p=M.execute(p,'owner',{type:'CHECKIN',id:pid});p=M.execute(p,'owner',{type:'POST_NIGHTS',id:pid});
  p=M.execute(p,'owner',{type:'CHARGE',id:pid,label:'Laundry',amount:50000});p=M.execute(p,'reception',{type:'DISCOUNT',id:pid,amount:50000,reason:'Service recovery'});
  p=M.execute(p,'manager',{type:'APPROVAL',id:p.approvals.at(-1).id,approve:true});
  p=M.execute(p,'cashier',{type:'PAYMENT',id:pid,amount:100000,method:'Cash',kind:'payment'});p=M.execute(p,'cashier',{type:'PAYMENT',id:pid,amount:50000,method:'Cash',kind:'deposit'});
  const pb=p.bookings.find(b=>b.id===pid),pt=M.totals(pb);

  // ── fake server state ──
  const srvLines=pb.lines.map((l,i)=>({chargeId:9001+i,kind:l.kind==='adjustment'?'correction':l.kind,serviceDate:l.date,label:l.label,amountPaise:l.amount,taxBps:l.taxBps,
    taxPaise:M.lineTax(l),reversesChargeId:null,reversed:false}));
  const srvReceipts=pb.receipts.map((r,i)=>({receiptId:7001+i,kind:r.kind,tender:r.method,amountPaise:r.amount,businessDate:r.date,reference:r.reference,refundOf:null,
    refundedPaise:0,refundablePaise:r.kind==='payment'?r.amount:0}));
  const apr=(id,action,over={})=>({approvalId:id,action,targetKind:'folio',targetId:5001,amountPaise:20000,status:'Pending',reason:'Overpaid at check-in',requestedBy:2,decidedBy:null,
    expiresUtc:'2026-10-25T04:30:00.000Z',resultId:null,version:'APR'+id,...over});
  const folio={folioId:5001,reservationId:1201,reservationStatus:'InHouse',businessDate:'2026-10-24',version:'FOLv1',lines:srvLines,receipts:srvReceipts,
    approvals:[apr(301,'refund',{targetKind:'receipt',targetId:7001}),apr(302,'discount',{amountPaise:50000,status:'Executed',decidedBy:3,reason:'Service recovery'}),apr(303,'credit',{amountPaise:326500,status:'Approved',decidedBy:1})],
    totals:{netPaise:pt.net,taxPaise:pt.tax,totalPaise:pt.total,paidPaise:pt.paid,depositPaise:pt.deposit,companyCreditPaise:0,balancePaise:pt.balance}};
  const nights=[{date:'2026-10-24',ratePaise:200000,taxBps:500,taxPaise:10000},{date:'2026-10-25',ratePaise:200000,taxBps:500,taxPaise:10000}];
  const details=(id,over={})=>({reservationId:id,publicNo:'HCC-'+id,version:'RESv1',status:'InHouse',guestName:'Journey Test',phone:'+91 90000 11111',roomId:7,roomNo:'105',
    roomTypeId:2,roomType:'Deluxe',arrival:pb.arrival,departure:pb.departure,adults:2,children:0,source:'Direct',nightlyRatePaise:200000,holdUntil:null,groupBlockId:null,
    restrictionOverride:null,cancelReason:null,notes:null,nights,netPaise:400000,taxPaise:20000,totalPaise:420000,company:{name:'Fictional Travels',gstin:null},...over});
  const shift=(over={})=>({shiftId:41,tillId:3,businessDate:'2026-10-24',status:'Open',openingFloatPaise:1072500,expectedPaise:null,countedPaise:null,approvalId:null,version:'SHFv1',...over});
  const st={res:details(1201),folio,invoice:null,cnr:[],desk:{businessDate:'2026-10-24',tills:[{till:{tillId:3,code:'FRONT',name:'Front desk',version:'T1'},
    active:{shift:shift(),submittedBy:null,differenceReason:null,closedBy:null,approval:null},expected:{expectedPaise:1222500},lastClosed:null}]},
    cashbook:{from:'2026-07-24',to:'2026-10-24',businessDate:'2026-10-24',days:[
      {businessDate:'2026-10-22',status:'Closed',locked:true,lockedBy:3,lockedUtc:'2026-10-23T05:00:00.000Z',history:[{action:'lock',principalId:3,atUtc:'2026-10-23T05:00:00.000Z',reason:null}]},
      {businessDate:'2026-10-23',status:'Closed',locked:false,lockedBy:null,lockedUtc:null,history:[{action:'lock',principalId:3,atUtc:'2026-10-24T04:00:00.000Z',reason:null},{action:'reopen',principalId:1,atUtc:'2026-10-24T04:30:00.000Z',reason:'Late bill'}]},
      {businessDate:'2026-10-24',status:'Open',locked:false,lockedBy:null,lockedUtc:null,history:[]}]},
    companies:{companies:[{companyId:5,name:'Deccan Pharma Distributors',gstin:'27ZZFIC0009F1Z5',contact:'Asha Rao',phone:null,email:null,city:'Pune',rates:{Deluxe:350000},creditLimitPaise:50000000,version:'CO5'}],
      requests:[{requestId:9,companyId:5,fromPaise:50000000,toPaise:80000000,reason:'Annual contract',status:'Pending',requestedBy:2,requestedUtc:'2026-10-24T04:30:00.000Z',decidedBy:null,note:null}]},
    groups:[{groupId:61,code:'GRP-61',name:'Sharma wedding',status:'Open',groupBlockId:71,blockCode:'SHARMA-1220',arrival:'2026-12-20',departure:'2026-12-22',members:1,version:'G1'},
      {groupId:62,code:'GRP-62',name:'Walk-in tour (no block)',status:'Open',groupBlockId:null,blockCode:null,arrival:null,departure:null,members:0,version:'G2'}]};
  const statement={groupId:61,code:'GRP-61',name:'Sharma wedding',status:'Open',organiser:'R. Sharma',billingName:'Sharma family',billingContact:'R. Sharma',gstin:null,
    groupBlockId:71,blockCode:'SHARMA-1220',blockStatus:'Open',cutoff:'2026-12-10',businessDate:'2026-10-24',rules:[{reservationId:null,category:'room',target:'master'}],
    pickup:[{roomType:'Deluxe',allotted:4,picked:1,released:0,remaining:3,ratePaise:180000,pickedPercent:25}],
    members:[{reservationId:1301,publicNo:'HCC-1301',guest:'Wedding guest',room:'106',status:'Confirmed',arrival:'2026-12-20',departure:'2026-12-22',folioId:null}],version:'GS1'};
  const ok=name=>({body})=>({name,ok:true,echo:body});
  const owner=capabilities({grants:[...capabilities().permissions.map(g=>[g.permissionId,'A']),['PERM-INV-001','A'],['PERM-INV-002','A'],['PERM-INV-003','A']]});
  // GET /reservations (W4-1 list) item for a reservation read: the stay is learned from it (stay id + version for CheckoutStay); the company comes
  // back from the BOOK notes ("Company: …"), exactly as the front-desk list mapping reads it.
  const listItem=d=>({...d,guestId:null,registered:true,stayId:501,stayVersion:'STYv1',stayStatus:d.status==='Departed'?'CheckedOut':'InHouse',checkedInDate:'2026-10-24',
    checkedOutDate:null,segments:[],notes:'Company: Fictional Travels'});
  const resList=items=>({businessDate:'2026-10-24',total:items.length,page:1,pageSize:500,items});
  const srv=fakeServer({
    'GET /me/capabilities':()=>owner,
    'GET /reservations':()=>resList([listItem(st.res)]),
    'GET /pos/void-requests':()=>[], // POS's approvals patch: a failing patch would keep the previous approvals
    'GET /reservations/:id':({params})=>params.id==='1201'?st.res:params.id==='1301'?details(1301,{status:'Confirmed',arrival:'2026-12-20',departure:'2026-12-22',roomNo:'106'}):reply(404,{code:'NOT_FOUND',message:'Reservation not found.'}),
    'GET /folios/by-reservation/:id':({params})=>params.id==='1201'?{reservationId:1201,stayId:501,stayVersion:'STYv1',folio:st.folio}:reply(404,{code:'NOT_FOUND',message:'Reservation not found.'}),
    'GET /invoices/by-source/:kind/:id':()=>st.invoice??reply(404,{code:'NOT_FOUND',message:'No invoice has been issued for this record yet.'}),
    'GET /credit-note-requests':()=>st.cnr,
    'GET /cashier/desk':()=>st.desk,
    'GET /cashbook':()=>st.cashbook,
    'GET /groups':()=>st.groups,
    'GET /groups/companies':()=>st.companies,
    'GET /groups/:id/statement':({params})=>params.id==='61'?statement:reply(404,{code:'NOT_FOUND',message:'Group not found.'}),
    'GET /availability':()=>({businessDate:'2026-10-24',arrival:'2026-12-20',departure:'2026-12-22',nights:2,party:1,types:[{roomTypeId:2,code:'Deluxe',maxOccupancy:3,availableRooms:5,violations:[]},
      {roomTypeId:3,code:'Suite',maxOccupancy:4,availableRooms:2,violations:[]}],rooms:[]}),
    ...Object.fromEntries(CMDS.map(n=>['POST /commands/'+n,ok(n)])),
  });
  // The whole registry: W4-1's CHECKOUT mapper (frontdesk.ts) calls the money helpers (company credit, IssueInvoice after CheckoutStay).
  // Without the yield area: its approvals patch (GET /yield/*) is not served here, and a failing patch keeps the previous approvals (runtime rule).
  const noYield={areas:S.AREAS.filter(a=>a.id!=='channels')};
  const rt=await startRuntime(S,srv,noYield);
  const s0=rt.get();const b0=s0.state.bookings.find(b=>b.id==='1201');

  test('[W4-3 money] Folio patch: the server folio becomes the booking’s lines and receipts — the prototype’s own ₹4,265 journey, paise-exact, GST from the posted snapshots',()=>{
    assert.deepEqual(ownErrors(s0.errors,'money'),{});
    assert.ok(b0,'booking loaded');
    assert.deepEqual(b0.lines.map(l=>[l.label,l.amount,l.date,l.kind,l.taxBps,M.lineTax(l)]),pb.lines.map(l=>[l.label,l.amount,l.date,l.kind,l.taxBps,M.lineTax(l)]));
    assert.deepEqual(b0.receipts.map(r=>[r.kind,r.method,r.amount,r.date]),pb.receipts.map(r=>[r.kind,r.method,r.amount,r.date]));
    assert.deepEqual(M.totals(b0),pt);assert.deepEqual(pt,{net:400000,tax:26500,total:426500,paid:100000,deposit:50000,balance:326500});
    assert.deepEqual([b0.lines[0].id,b0.receipts[0].id],['9001','7001'],'prototype id = String(server id)');
    assert.deepEqual([rt.ids.server('folio','1201'),rt.ids.version('folio','1201'),rt.ids.server('stay','1201'),rt.ids.version('stay','1201'),rt.ids.server('till','desk')],[5001,'FOLv1',501,'STYv1',3]);
    assert.equal(b0.invoiceNo,undefined);assert.equal(b0.company?.name,'Fictional Travels');
  });
  test('[W4-3 money] Approvals: folio requests map to the prototype kinds (discount = default kind, credit → company-credit) with requester, decider, expiry (IST clock)',()=>{
    const a=s0.state.approvals.filter(x=>/^(\d+|cnr-\d+)$/.test(x.id));
    assert.deepEqual(a.map(x=>[x.id,x.booking,x.kind,x.status,x.amount,x.target,x.actor,x.approver]),
      [['301','1201','refund','Pending',20000,'7001','2',undefined],['302','1201',undefined,'Executed',50000,undefined,'2','3'],['303','1201','company-credit','Approved',326500,undefined,'2','1']]);
    assert.equal(a[0].expiresAt,'2026-10-25T10:00');assert.equal(M.folioRefundable(s0.state,b0,'7001'),80000,'refundable remainder honours the pending request');
    assert.equal(rt.ids.version('approval','301'),'APR301');
  });
  test('[W4-3 money] Cash desk → shiftOpen / openingCash (shared till DEC-052); cash book locks + lock/reopen history; group blocks from groups billing',()=>{
    assert.deepEqual([s0.state.shiftOpen,s0.state.openingCash,s0.state.shift,s0.state.shiftClose],[true,1072500,undefined,undefined]);
    assert.deepEqual(s0.state.cashbookX.locks,{'2026-10-22':{lockedBy:'3',lockedAt:'2026-10-23T10:30',log:[{action:'lock',by:'3',at:'2026-10-23T10:30'}]}});
    assert.deepEqual(s0.state.cashbookX.history['2026-10-23'].map(e=>[e.action,e.by,e.reason]),[['lock','3',undefined],['reopen','1','Late bill']]);
    assert.equal(M.cbIsLocked(s0.state,'2026-10-22'),true);assert.equal(M.cbIsLocked(s0.state,'2026-10-23'),false);
    assert.deepEqual(srv.gets('/cashbook')[0].query,{from:'2026-07-24',to:'2026-10-24'});
    const g=s0.state.groupsX;assert.equal(g.blocks.length,1,'groups without a block are not block cards');
    assert.deepEqual([g.blocks[0].id,g.blocks[0].name,g.blocks[0].billTo,g.blocks[0].cutoff,g.blocks[0].allot,g.blocks[0].rates,g.blocks[0].members],
      ['61','Sharma wedding','company','2026-12-10',{Deluxe:4},{Deluxe:180000},[{booking:'1301',type:'Deluxe'}]]);
    assert.deepEqual([g.companies.length,g.requests.length,g.overrides],[1,1,[]],'companies and the pending limit request come from GET /groups/companies');
  });
  const P=await loadPages();const shown=[P.renderPages(s0.state,'1',false),P.renderPages(s0.state,'1',true)];
  test('[W4-3 money] Every desktop and phone page renders on the money slices',()=>{assert.deepEqual(shown.map(x=>x.errors),[{},{}]);assert.ok(shown[0].rendered>10);});

  // ── commands ──
  const run=async(cmd)=>{srv.reset();const r=await rt.run(cmd);return {r,calls:srv.calls.map(c=>`${c.method} ${c.path}`)};};
  const pay1=await run({type:'PAYMENT',id:'1201',amount:100000,kind:'payment',method:'Cash',reference:'',key:'pay:1'});pay1.rr=srv.commands('RecordReceipt');
  const pay2=await run({type:'PAYMENT',id:'1201',amount:226500,kind:'payment',tenders:[{method:'UPI',amount:200000,reference:'UPI-9'},{method:'Card',amount:26500,reference:'CARD-1'}],key:'pay:2'});pay2.rr=srv.commands('RecordReceipt');
  test('[W4-3 money] PAYMENT → RecordReceipt per tender (cash on the desk till, key <action> or <action>:t1…), then the folio and approvals reload',()=>{
    assert.ok(pay1.r.ok,pay1.r.text);const c=pay1.rr;
    assert.equal(pay1.calls[0],'POST /commands/RecordReceipt');assert.ok(pay1.calls.includes('GET /folios/by-reservation/1201'));
    assert.deepEqual([c[0].key,c[0].body],['pay:1',{folioId:5001,kind:'payment',tender:'Cash',amountPaise:100000,reference:null,tillId:3}]);
    assert.ok(pay2.r.ok,pay2.r.text);const c2=pay2.rr;
    assert.deepEqual(c2.map(x=>x.key),['pay:2:t1','pay:2:t2']);
    assert.deepEqual(c2.map(x=>x.body),[{folioId:5001,kind:'payment',tender:'UPI',amountPaise:200000,reference:'UPI-9',tillId:null},{folioId:5001,kind:'payment',tender:'Card',amountPaise:26500,reference:'CARD-1',tillId:null}]);
  });
  srv.reset();await rt.run({type:'PAYMENT',id:'1201',amount:100000,kind:'deposit',method:'Cash',key:'pay:3'});const cashBody=srv.commands('RecordReceipt')[0];
  const charge=await run({type:'CHARGE',id:'1201',amount:30000,label:'Dinner',gst:'food',key:'chg:1'});const chargeCmd=srv.commands('PostCharge')[0];
  const disc=await run({type:'DISCOUNT',id:'1201',amount:20000,reason:'Goodwill',key:'disc:1'});const discCmd=srv.commands('RequestAdjustment')[0];
  const rr=await run({type:'REFUND_REQUEST',id:'1201',receiptId:'7001',amount:10000,reason:'Overpaid',key:'rr:1'});const rrCmd=srv.commands('RequestAdjustment')[0];
  const cc=await run({type:'COMPANY_CREDIT_REQUEST',id:'1201',key:'cc:1201:1'});const ccCmd=srv.commands('RequestAdjustment')[0];
  const cor=await run({type:'CORRECTION_REQUEST',id:'1201',lineId:'9002',reason:'Wrong folio',key:'cor:1'});const corCmd=srv.commands('RequestAdjustment')[0];
  test('[W4-3 money] CHARGE → PostCharge (GST category, no client tax); DISCOUNT / REFUND_REQUEST / COMPANY_CREDIT_REQUEST / CORRECTION_REQUEST → RequestAdjustment bound to the loaded folio version',()=>{
    assert.deepEqual(cashBody.body,{folioId:5001,kind:'deposit',tender:'Cash',amountPaise:100000,reference:null,tillId:3});
    for(const x of [charge,disc,rr,cc,cor])assert.ok(x.r.ok,x.r.text);
    assert.deepEqual([chargeCmd.key,chargeCmd.body],['chg:1',{folioId:5001,label:'Dinner',amountPaise:30000,gstCategory:'food'}]);
    assert.deepEqual(discCmd.body,{action:'discount',folioId:5001,targetId:null,amountPaise:20000,reason:'Goodwill',expectedVersion:'FOLv1'});
    assert.deepEqual(rrCmd.body,{action:'refund',folioId:5001,targetId:7001,amountPaise:10000,reason:'Overpaid',expectedVersion:'FOLv1'});
    assert.deepEqual(ccCmd.body,{action:'credit',folioId:5001,targetId:null,amountPaise:326500,reason:'Company credit · Fictional Travels',expectedVersion:'FOLv1'},'credit = the server balance');
    assert.deepEqual(corCmd.body,{action:'correction',folioId:5001,targetId:9002,amountPaise:Math.abs(pb.lines[1].amount),reason:'Wrong folio',expectedVersion:'FOLv1'});
    assert.ok(cor.calls.includes('GET /folios/by-reservation/1201'));
  });
  const dec=await run({type:'APPROVAL',id:'301',approve:false,reason:'Not needed',key:'approval-no:301'});const decCmd=srv.commands('DecideApproval')[0];
  const exe=await run({type:'REFUND_EXECUTE',approvalId:'301',method:'UPI',reference:'UPI-R1',key:'rx:1'});const exeCmd=srv.commands('ExecuteAdjustment')[0];
  const chk=await run({type:'CHECKOUT',id:'1201',key:'checkout:1201'});const chkCmd=srv.commands('CheckoutStay')[0],invCmd=srv.commands('IssueInvoice')[0];
  test('[W4-3 money] APPROVAL → DecideApproval (approval version); REFUND_EXECUTE → ExecuteAdjustment; CHECKOUT helpers: approved company credit for exactly the balance, then IssueInvoice (<action>:invoice)',()=>{
    for(const x of [dec,exe,chk])assert.ok(x.r.ok,x.r.text);
    assert.deepEqual([decCmd.key,decCmd.body],['approval-no:301',{approvalId:301,approve:false,reason:'Not needed',expectedVersion:'APR301'}]);
    assert.deepEqual(exeCmd.body,{approvalId:301,tender:'UPI',reference:'UPI-R1',tillId:null,expectedVersion:'APR301'});
    assert.deepEqual([chkCmd.key,chkCmd.body],['checkout:1201',{stayId:501,companyCreditApprovalId:303,expectedVersion:'STYv1'}]);
    assert.deepEqual([invCmd.key,invCmd.body],['checkout:1201:invoice',{sourceKind:'folio',sourceId:5001}],'B2C when the company has no GSTIN');
    assert.ok(chk.calls.indexOf('GET /folios/by-reservation/1201')>chk.calls.indexOf('POST /commands/IssueInvoice'));
  });

  // Departed + invoiced, a pending credit-note request, a submitted count awaiting its difference decision.
  st.res=details(1201,{status:'Departed'});st.folio={...folio,reservationStatus:'Departed',version:'FOLv2',approvals:[]};
  st.invoice={invoiceId:801,invoiceNo:'INV/2627/0000001',docType:'INV',invoiceDate:'2026-10-26',sourceKind:'folio',sourceId:5001,totalPaise:426500,
    creditNotes:[{creditNoteId:901,creditNoteNo:'CN/2627/0000001',noteDate:'2026-10-26',reason:'Late check-out waived',taxablePaise:10000,totalPaise:10500}]};
  st.cnr=[{requestId:55,invoiceId:801,invoiceNo:'INV/2627/0000001',mode:'amount',amountPaise:20000,lines:[],reason:'Service recovery',status:'Pending',direct:false,requestedBy:2,
    requestedUtc:'2026-10-26T05:00:00.000Z',expiresUtc:'2026-10-27T05:00:00.000Z',decidedBy:null,decisionReason:null,creditNoteId:null,version:'CNRv1'},
    {requestId:56,invoiceId:999,invoiceNo:'INV/2627/0000009',mode:'full',amountPaise:null,lines:[],reason:'Other hotel folio',status:'Pending',direct:false,requestedBy:2,
    requestedUtc:'2026-10-26T05:00:00.000Z',expiresUtc:'2026-10-27T05:00:00.000Z',decidedBy:null,decisionReason:null,creditNoteId:null,version:'CNRv9'}];
  st.desk={businessDate:'2026-10-24',tills:[{till:{tillId:3,code:'FRONT',name:'Front desk',version:'T1'},
    active:{shift:shift({status:'Submitted',expectedPaise:1222500,countedPaise:1222000,approvalId:77,version:'SHFv2'}),submittedBy:4,differenceReason:'₹5 coin short',closedBy:null,
      approval:apr(77,'cash-difference',{targetKind:'shift',targetId:41,amountPaise:500,reason:'₹5 coin short',requestedBy:4,version:'APR77'})},expected:{expectedPaise:1222500},lastClosed:null}]};
  await rt.refresh();const s1=rt.get();const b1=s1.state.bookings.find(b=>b.id==='1201');
  test('[W4-3 money] Departed stay: invoice number and credit notes from the issued invoice; the credit-note request is a folio request (cnr-55); the submitted count is the shift record + its cash-difference request',()=>{
    assert.deepEqual(ownErrors(s1.errors,'money'),{});
    assert.equal(b1.invoiceNo,'INV/2627/0000001');assert.deepEqual(b1.creditNotes.map(c=>[c.id,c.no,c.amount,c.date,c.reason]),[['901','CN/2627/0000001',10000,'2026-10-26','Late check-out waived']]);
    const a=s1.state.approvals.filter(x=>/^(\d+|cnr-\d+)$/.test(x.id));
    assert.deepEqual(a.map(x=>[x.id,x.kind,x.booking,x.amount,x.target,x.status,x.meta?.command]),
      [['cnr-55','credit-note','1201',20000,'INV/2627/0000001','Pending','APPROVAL'],['77','cash-difference','',500,'2026-10-24','Pending','SHIFT_DIFFERENCE_DECIDE']],'request for an unloaded invoice is left out');
    assert.deepEqual([s1.state.shiftOpen,s1.state.openingCash],[false,1072500]);
    assert.deepEqual(s1.state.shift,{date:'2026-10-24',status:'Submitted',expected:1222500,counted:1222000,submittedBy:'4',reason:'₹5 coin short',approval:'77'});
    assert.equal(M.shiftStatus(s1.state),'Submitted');
  });
  const shown1=[P.renderPages(s1.state,'1',false),P.renderPages(s1.state,'1',true)];
  test('[S3 money] REFUND_EXECUTE: a cash refund (the receipt’s own tender when the dialog gives none) needs the till’s shift open today; non-cash does not',()=>{
    const pre=S.AREAS.find(a=>a.id==='money').commands.find(c=>c.type==='REFUND_EXECUTE').precheck;
    const st=(open,method)=>({shiftOpen:open,approvals:[{id:'9',kind:'refund',booking:'B1',target:'R1'}],bookings:[{id:'B1',receipts:[{id:'R1',method}]}]});
    const closed='The cash shift is closed. Open the next business day first.';
    assert.equal(pre({approvalId:'9'},st(false,'Cash')),closed,'tender from the original cash receipt');
    assert.equal(pre({approvalId:'9',method:'Cash'},st(false,'UPI')),closed);
    assert.equal(pre({approvalId:'9'},st(true,'Cash')),undefined,'shift open today');
    assert.equal(pre({approvalId:'9'},st(false,'UPI')),undefined,'non-cash receipt: no shift needed');
    assert.equal(pre({approvalId:'9',method:'UPI',reference:'UPI-R9'},st(false,'Cash')),undefined,'paid back by UPI: no shift needed');
  });
  test('[W4-3 money] Pages render with an invoiced stay, a credit-note request and a pending cash difference',()=>{assert.deepEqual(shown1.map(x=>x.errors),[{},{}]);});
  const cn=await run({type:'CREDIT_NOTE',id:'1201',amount:15000,reason:'Noise complaint',key:'cn:1'});const cnCmd=srv.commands('RequestCreditNote')[0];
  const cnd=await run({type:'APPROVAL',id:'cnr-55',approve:true,key:'approval:cnr-55'});const cndCmd=srv.commands('DecideCreditNote')[0];
  const sdd=await run({type:'SHIFT_DIFFERENCE_DECIDE',id:'77',approve:true,key:'shiftdiff:77'});const sddCmd=srv.commands('DecideApproval')[0];
  test('[W4-3 money] CREDIT_NOTE → RequestCreditNote (taxable amount); APPROVAL of cnr-* → DecideCreditNote; SHIFT_DIFFERENCE_DECIDE → DecideApproval, cash slices reload',()=>{
    for(const x of [cn,cnd,sdd])assert.ok(x.r.ok,x.r.text);
    assert.deepEqual(cnCmd.body,{invoiceId:801,mode:'amount',reason:'Noise complaint',amountPaise:15000});
    assert.deepEqual(cndCmd.body,{requestId:55,approve:true,reason:null,expectedVersion:'CNRv1'});
    assert.deepEqual(sddCmd.body,{approvalId:77,approve:true,reason:null,expectedVersion:'APR77'});
    assert.ok(sdd.calls.filter(c=>c==='GET /cashier/desk').length>=4,sdd.calls.join(','));
  });

  // Counting (open shift) and the cash book lock / reopen.
  st.desk={businessDate:'2026-10-24',tills:[{till:{tillId:3,code:'FRONT',name:'Front desk',version:'T1'},active:{shift:shift({version:'SHFv3'}),submittedBy:null,differenceReason:null,closedBy:null,approval:null},expected:{expectedPaise:1222500},lastClosed:null}]};
  await rt.refresh();
  const sub=await run({type:'SHIFT_SUBMIT',counted:1222000,reason:'₹5 coin short',denominations:{500:2000,200:100,100:220,50:0,20:0,10:0,coins:0},key:'count:1'});const subCmd=srv.commands('SubmitShiftCount')[0];
  const lock=await run({type:'CB_LOCK',date:'2026-10-23',key:'cb:1'});const lockCmd=srv.commands('LockCashBookDay')[0];
  const reo=await run({type:'CB_REOPEN',date:'2026-10-22',reason:'Missed expense',key:'cb:2'});const reoCmd=srv.commands('ReopenCashBookDay')[0];
  test('[W4-3 money] SHIFT_SUBMIT → SubmitShiftCount (notes grid, coins in paise, shift version); CB_LOCK / CB_REOPEN → LockCashBookDay / ReopenCashBookDay, cash book reloads',()=>{
    for(const x of [sub,lock,reo])assert.ok(x.r.ok,x.r.text);
    assert.deepEqual(subCmd.body,{shiftId:41,countedPaise:1222000,notes:{10:0,20:0,50:0,100:220,200:100,500:2000},coinsPaise:0,reason:'₹5 coin short',expectedVersion:'SHFv3'});
    assert.deepEqual([lockCmd.body,reoCmd.body],[{businessDate:'2026-10-23'},{businessDate:'2026-10-22',reason:'Missed expense'}]);
    assert.deepEqual(lock.calls,['POST /commands/LockCashBookDay','GET /cashbook']);
  });
  const grp=await run({type:'GRP_BLOCK_CREATE',name:'Iyer conference',billTo:'guest',arrival:'2026-12-20',departure:'2026-12-22',cutoff:'2026-12-10',allot:{Deluxe:3,Suite:0},rates:{Deluxe:190000},contact:'A. Iyer',key:'grp:1'});
  const gb=srv.commands('CreateGroupBlock')[0],gg=srv.commands('CreateGroup')[0];
  test('[W4-3 money] GRP_BLOCK_CREATE → [GET /availability for room-type ids] → CreateGroupBlock → CreateGroup (guest-paid routing), groups reload',()=>{
    assert.ok(grp.r.ok,grp.r.text);
    assert.deepEqual(grp.calls.slice(0,3),['GET /availability','POST /commands/CreateGroupBlock','POST /commands/CreateGroup']);
    assert.deepEqual([gb.key,gb.body],['grp:1',{code:'IYERCONFERENC-1220',name:'Iyer conference',arrival:'2026-12-20',departure:'2026-12-22',cutoff:'2026-12-10',allotments:[{roomTypeId:2,rooms:3,ratePaise:190000}]}]);
    assert.equal(gg.key,'grp:1:group');
    assert.deepEqual(gg.body.rules,[{category:'room',target:'guest'},{category:'extras',target:'guest'},{category:'tax',target:'follow'}]);
    assert.ok(grp.calls.includes('GET /groups'));
  });

  // ── W4 N1: company accounts ──
  const sc1=await run({type:'GRP_COMPANY_SAVE',name:' Kale Textiles ',gstin:'27ZZFIC0010F1Z5',contact:'S. Kale',phone:'',email:'',city:'Latur',rates:{Deluxe:300000,Suite:0},creditLimit:20000000,key:'co:1'});const c1=srv.commands('SaveCompany')[0];
  const sc2=await run({type:'GRP_COMPANY_SAVE',id:'5',name:'Deccan Pharma Distributors',gstin:'27ZZFIC0009F1Z5',contact:'Asha Rao',city:'Pune',rates:{Deluxe:360000},key:'co:2'});const c2=srv.commands('SaveCompany')[0];
  const ls=await run({type:'GRP_LIMIT_SET',company:'5',limit:60000000,reason:'Review',key:'co:3'});const lsCmd=srv.commands('SetCompanyLimit')[0];
  const ld=await run({type:'GRP_LIMIT_DECIDE',id:'9',approve:true,note:'ok',key:'co:4'});const ldCmd=srv.commands('DecideCompanyLimit')[0];
  test('[W4 N1] groups loader: companies (limit, rates, GSTIN) and the pending limit request; GRP_COMPANY_SAVE / GRP_LIMIT_SET / GRP_LIMIT_DECIDE → SaveCompany / SetCompanyLimit / DecideCompanyLimit with version, groups reload',()=>{
    const g=s0.state.groupsX;
    assert.deepEqual(g.companies.map(c=>[c.id,c.name,c.gstin,c.rates,c.creditLimit,c.contact,c.city]),[['5','Deccan Pharma Distributors','27ZZFIC0009F1Z5',{Deluxe:350000},50000000,'Asha Rao','Pune']]);
    assert.deepEqual(g.requests.map(r=>[r.id,r.company,r.from,r.to,r.status,r.actor]),[['9','5',50000000,80000000,'Pending','2']]);
    assert.equal(rt.ids.version('company','5'),'CO5');
    for(const x of [sc1,sc2,ls,ld])assert.ok(x.r.ok,x.r.text);
    assert.deepEqual(c1.body,{name:'Kale Textiles',gstin:'27ZZFIC0010F1Z5',contact:'S. Kale',phone:null,email:null,city:'Latur',rates:{Deluxe:300000},companyId:null,creditLimitPaise:20000000});
    assert.equal(c1.key,'co:1');
    assert.deepEqual([c2.body.companyId,c2.body.creditLimitPaise,c2.body.expectedVersion,c2.body.rates],[5,0,'CO5',{Deluxe:360000}]);
    assert.deepEqual(lsCmd.body,{companyId:5,limitPaise:60000000,reason:'Review'});
    assert.deepEqual(ldCmd.body,{requestId:9,approve:true,note:'ok'});
    assert.ok(sc1.calls.includes('GET /groups')&&sc1.calls.includes('GET /groups/companies'));
  });

  // Refusals: prechecks (no request), unmapped commands, a folio whose posted GST disagrees with its snapshot.
  st.desk={businessDate:'2026-10-24',tills:[]};await rt.refresh();
  srv.reset();
  const cashClosed=await rt.run({type:'PAYMENT',id:'1201',amount:1000,kind:'payment',method:'Cash',key:'pay:9'});
  const refundDirect=await rt.run({type:'PAYMENT',id:'1201',amount:1000,kind:'refund',method:'UPI',reference:'x',key:'pay:10'});
  const split=await rt.run({type:'PAYMENT',id:'1201',amount:1000,kind:'payment',tenders:[{method:'UPI',amount:600,reference:'a'},{method:'Card',amount:300,reference:'b'}],key:'pay:11'});
  const refusedCalls=srv.calls.length;
  const unmapped=await Promise.all(['GRP_ROOMING','GRP_RELEASE','GRP_BLOCK_CANCEL'].map(type=>rt.run({type})));
  const area=S.AREAS.find(a=>a.id==='money');
  st.folio={...st.folio,lines:[{...srvLines[0],taxPaise:srvLines[0].taxPaise+1}]};await rt.refresh();const s2=rt.get();
  test('[W4-3 money] Prechecks refuse without a request (cash with no open shift, direct refund, tenders not adding up); GRP company/limit/rooming/release stay unmapped with reasons; a folio with a re-rounded GST line is refused, last good data kept',()=>{
    assert.deepEqual([cashClosed.reason,refundDirect.reason,split.reason],['precheck','precheck','precheck']);
    assert.match(cashClosed.text,/cash shift is closed/);assert.match(refundDirect.text,/Request refund/);assert.equal(refusedCalls,0);
    assert.deepEqual(unmapped.map(r=>r.reason),['unmapped','unmapped','unmapped']);
    assert.deepEqual(Object.keys(area.unmapped).sort(),['GRP_BLOCK_ALLOT','GRP_BLOCK_CANCEL','GRP_RELEASE','GRP_ROOMING']);
    assert.match(s2.errors.bookings??'',/posted GST/);
    assert.ok(!s2.state.bookings.find(b=>b.id==='1201').lines.some(l=>l.id==='9001'&&M.lineTax(l)!==srvLines[0].taxPaise),'a re-rounded line never reaches the screens');
    assert.deepEqual([s2.state.shiftOpen,s2.state.openingCash,s2.state.shift],[false,0,undefined],'no till yet → shift closed, nothing to count');
  });

  // Roles: a housekeeper reads no money; a cashier without PERM-INV-002 checks out without issuing the invoice.
  const hk=fakeServer({'GET /me/capabilities':()=>capabilities({roles:['HKP'],grants:[['PERM-HK-001','A']]})});const hkRt=await startRuntime(S,hk);
  const cs=fakeServer({...Object.fromEntries(Object.entries({'GET /reservations':()=>resList([listItem(details(1201))]),'GET /folios/by-reservation/:id':()=>({reservationId:1201,stayId:501,stayVersion:'STYv1',folio}),
    'GET /cashier/desk':()=>({businessDate:'2026-10-24',tills:[]}),'GET /credit-note-requests':()=>[],'POST /commands/CheckoutStay':ok('CheckoutStay')})),
    'GET /me/capabilities':()=>capabilities({roles:['REC'],grants:[['PERM-RES-001','A'],['PERM-STY-003','A'],['PERM-FOL-001','A'],['PERM-FOL-002','A'],['PERM-PAY-001','A']]})});
  const csRt=await startRuntime(S,cs,noYield);cs.reset();
  const csOut=await csRt.run({type:'CHECKOUT',id:'1201',key:'checkout:x'});
  test('[W4-3 money] A housekeeper loads no folio, desk, cash book or groups; reception without PERM-INV-002 checks out without IssueInvoice',()=>{
    assert.equal(hk.calls.filter(c=>/folios|cashier|cashbook|groups|credit-note/.test(c.path)).length,0,hk.calls.map(c=>c.path).join(','));
    assert.deepEqual(hkRt.get().state.approvals,[]);
    assert.ok(csOut.ok,csOut.text);assert.deepEqual(cs.calls.filter(c=>c.method==='POST').map(c=>c.path),['/commands/CheckoutStay']);
    assert.equal(cs.commands('CheckoutStay')[0].body.companyCreditApprovalId,303);
  });

  // W4 D1: CheckoutStay succeeds, then IssueInvoice is refused (no hotel GSTIN yet). The checkout must count as done: the slices reload
  // (stay Departed, no invoice), the screen gets a warning naming the problem, and INVOICE_ISSUE ("Issue invoice") retries with a stable key.
  const d1={res:details(1201),folio:{...folio,approvals:[]},invoice:null,gstin:false};
  const gstinMsg="No tax invoice can be issued: the hotel's GSTIN is not recorded. An Owner records it in Master configuration › Property and branding › GSTIN on documents (brand.gstin); then issue the invoice from the departed stay's folio.";
  const inv={invoiceId:802,invoiceNo:'INV/2627/0000002',docType:'INV',invoiceDate:'2026-10-24',sourceKind:'folio',sourceId:5001,totalPaise:426500,creditNotes:[]};
  const ds=fakeServer({
    'GET /me/capabilities':()=>owner,'GET /reservations':()=>resList([listItem(d1.res)]),'GET /pos/void-requests':()=>[],
    'GET /reservations/:id':()=>d1.res,'GET /folios/by-reservation/:id':()=>({reservationId:1201,stayId:501,stayVersion:'STYv1',folio:d1.folio}),
    'GET /invoices/by-source/folio/:id':()=>d1.invoice??reply(404,{code:'NOT_FOUND',message:'No invoice for this folio.'}),
    'GET /credit-note-requests':()=>[],'GET /cashier/desk':()=>st.desk,'GET /cashbook':()=>st.cashbook,'GET /groups':()=>[],
    'POST /commands/CheckoutStay':()=>{d1.res=details(1201,{status:'Departed'});d1.folio={...d1.folio,reservationStatus:'Departed',version:'FOLv9'};return {stayId:501,status:'CheckedOut'};},
    'POST /commands/IssueInvoice':()=>{if(!d1.gstin)return reply(409,{code:'INVALID_TRANSITION',message:gstinMsg});d1.invoice=inv;
      return {invoiceId:802,invoiceNo:inv.invoiceNo,docType:'INV',invoiceDate:'2026-10-24',totalPaise:426500,created:true};},
  });
  const dRt=await startRuntime(S,ds,noYield);ds.reset();
  const dOut=await dRt.run({type:'CHECKOUT',id:'1201',key:'checkout:1201'});const dCalls=ds.calls.map(c=>`${c.method} ${c.path}`);
  const dState=dRt.get().state.bookings.find(b=>b.id==='1201');
  const notYet=await dRt.run({type:'INVOICE_ISSUE',id:'1201',key:'invoice:1201'});
  d1.gstin=true;ds.reset();
  const issued=await dRt.run({type:'INVOICE_ISSUE',id:'1201',key:'invoice:1201'});const issuedCalls=ds.calls.map(c=>`${c.method} ${c.path}`);
  const after=dRt.get().state.bookings.find(b=>b.id==='1201');
  const again=await dRt.run({type:'INVOICE_ISSUE',id:'1201',key:'invoice:1201:b'});
  const csNo=await csRt.run({type:'INVOICE_ISSUE',id:'1201',key:'invoice:1201'});
  test('[W4 D1] CHECKOUT: an IssueInvoice refusal after CheckoutStay is a warning, not a failure — the stay reloads as Departed without an invoice',()=>{
    assert.ok(dOut.ok,dOut.text);
    assert.match(dOut.warning,/^Checkout completed/);assert.match(dOut.warning,/brand\.gstin/);assert.match(dOut.warning,/Issue invoice/);
    assert.deepEqual(dCalls.filter(c=>c.startsWith('POST')),['POST /commands/CheckoutStay','POST /commands/IssueInvoice']);
    assert.ok(dCalls.indexOf('GET /folios/by-reservation/1201')>dCalls.indexOf('POST /commands/IssueInvoice'),'folio reloaded after the refusal');
    assert.deepEqual([dState.status,dState.invoiceNo],['Departed',undefined]);
  });
  test('[W4 D1] INVOICE_ISSUE → IssueInvoice with the screen key; refused with the server message until the GSTIN is set, then the invoice number shows',()=>{
    assert.deepEqual([notYet.ok,notYet.reason],[false,'server']);assert.match(notYet.text,/GSTIN on documents \(brand\.gstin\)/);
    assert.ok(issued.ok,issued.text);const c=ds.commands('IssueInvoice').at(-1);
    assert.deepEqual([c.key,c.body],['invoice:1201',{sourceKind:'folio',sourceId:5001}]);
    assert.ok(issuedCalls.indexOf('GET /invoices/by-source/folio/5001')>issuedCalls.indexOf('POST /commands/IssueInvoice'));
    assert.equal(after.invoiceNo,'INV/2627/0000002');
    assert.deepEqual([again.ok,again.reason],[false,'precheck']);assert.match(again.text,/already been issued/);
    assert.deepEqual([csNo.ok,csNo.reason],[false,'precheck']);assert.match(csNo.text,/cannot issue tax invoices/);
  });
}
