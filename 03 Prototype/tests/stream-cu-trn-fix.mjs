// CU lane trn-fix · four defects found while making the training course (06 Training/TRAINING_NOTES.md F14, F17, F8, F19). Names prefixed [CU-TRN].
import {build} from 'esbuild';
import {pathToFileURL} from 'node:url';
import {resolve} from 'node:path';
import {hallFixtures} from './_hall.mjs';
export async function register({test,M,assert}){
  const {seed,execute,loadScenario,drawer,shiftExpected,shiftMovements,shiftStatus}=M;
  const fy=()=>loadScenario('FY26');
  const code=f=>{try{f();return 'ok';}catch(e){return e.code??e.message;}};

  // ── F14 · the Cashier page figure (shiftExpected) is the close command's figure (drawer) ──
  // Cash that drawer(s) counts from three sources the page used to leave out: counter bill, hall advance less a refund, vendor payment.
  const withCash=s=>{
    s.counterX={...(s.counterX??{}),bills:[...(s.counterX?.bills??[]),{id:'CB-T1',date:s.date,status:'Issued',total:123457,settlement:{mode:'paid',method:'Cash'}}]};
    s.hallX={...(s.hallX??M.hallSeed()),events:[{id:'EV-T1',receipts:[{date:s.date,method:'Cash',kind:'ADVANCE',amountPaise:500050},{date:s.date,method:'Cash',kind:'REFUND',amountPaise:20003}]}]};
    s.expensesX={...(s.expensesX??{}),vendorPayments:[{id:'VP-T1',vendor:'Test Vendor',date:s.date,method:'Cash',amount:77777,allocations:[]}]};
    return s;};
  for(const [name,mk] of [['BASE',()=>seed()],['FY26',fy]])
    test(`[CU-TRN] F14 ${name}: expected cash on the page equals the close command's drawer figure, to the paise`,()=>{
      const plain=mk(),s=withCash(mk()),ex=shiftExpected(s);
      assert.equal(ex.expected,drawer(s));assert.equal(ex.consistent,true);
      const mv=shiftMovements(s),sum=k=>mv.filter(m=>m.kind===k).reduce((a,m)=>a+m.amount,0);
      assert.equal(sum('counter-bill'),123457);assert.equal(sum('hall-receipt'),480047);assert.equal(sum('vendor-payment'),-77777);
      assert.equal(ex.expected,s.openingCash+mv.reduce((a,m)=>a+m.amount,0));
      assert.equal(ex.expected,drawer(plain)+123457+480047-77777);
    });
  test('[CU-TRN] F14: typing the page figure and submitting closes the shift with no reason (no difference)',()=>{
    let s=withCash(seed());const ex=shiftExpected(s).expected;
    s=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:ex});
    assert.equal(shiftStatus(s),'Closed');assert.equal(s.shift.expected,ex);assert.equal(s.shift.counted,ex);assert.equal(s.shiftClose.counted-s.shiftClose.expected,0);
  });
  test('[CU-TRN] F14: a count one paisa off still needs a reason',()=>{
    const s=withCash(seed());assert.match(String(code(()=>execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:shiftExpected(s).expected+1}))),/reason|VALIDATION/);
  });

  // F14 through the real commands: cash counter bill, hall advance in cash and UPI, vendor payment in cash and by bank, also with Cash renamed in the master.
  for(const rename of [false,true])
    test(`[CU-TRN] F14 real commands${rename?' (Cash renamed in the payment-method master)':''}: page figure = close command figure, to the paise`,()=>{
      const {quote,at}=hallFixtures(M);
      let s=seed(),cash='Cash';
      if(rename){const row=s.mastersX?.paymentMethods?.find(r=>r.code==='CASH')??M.mstRows?.(s,'paymentMethods')?.find(r=>r.code==='CASH');
        s=execute(s,'owner',{type:'MST_MASTER_SAVE',master:'paymentMethods',row:{...row,name:'Till cash'}});cash='Till cash';}
      const d0=drawer(s),vend=M.mstRows(s,'expenseVendors')[0].code;
      s=execute(s,'cashier',{type:'CTR_BILL_ISSUE',party:{name:'Walk-in Guest',phone:'+91 90000 11111'},lines:[{label:'Courier',unit:15000,gstCategory:'other'}],settlement:{mode:'paid',method:cash}});
      const bill=s.counterX.bills.at(-1);assert.ok(bill.total>0);
      let qid;[s,qid]=quote(s);
      s=execute(s,'manager',{type:'QUO_ACCEPT',id:qid,version:1,advance:{amountPaise:2000000,method:'UPI',reference:'UPI-T-1'},key:'t-accept'});
      const ev=s.hallX.events.at(-1).id;
      s=execute(s,'cashier',{type:'HALL_ADVANCE_RECEIVE',eventId:ev,amountPaise:250013,method:cash,reference:'REF-T-CASH',key:'t-adv-cash'});
      s=execute(s,'cashier',{type:'HALL_ADVANCE_RECEIVE',eventId:ev,amountPaise:310007,method:'UPI',reference:'UPI-T-2',key:'t-adv-upi'});
      s=execute(s,'manager',{type:'EXP_VENDOR_PAY',vendor:vend,amount:120003,method:cash});
      s=execute(s,'manager',{type:'EXP_VENDOR_PAY',vendor:vend,amount:90001,method:'Bank transfer',reference:'UTR-T-1'});
      const ex=shiftExpected(s),mv=shiftMovements(s),sum=k=>mv.filter(m=>m.kind===k).reduce((a,m)=>a+m.amount,0);
      assert.equal(ex.expected,drawer(s));assert.equal(ex.consistent,true);
      assert.equal(sum('counter-bill'),bill.total);assert.equal(sum('hall-receipt'),250013);assert.equal(sum('vendor-payment'),-120003);
      assert.equal(drawer(s)-d0,bill.total+250013-120003,'UPI hall advance and the bank vendor payment are not drawer cash');
      s=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:ex.expected});
      assert.equal(shiftStatus(s),'Closed');assert.equal(s.shiftClose.counted-s.shiftClose.expected,0);
    });

  // ── F17 · hall terms are listed and editable in FY26 ──
  const out=resolve('tests/.generated/hall-logic.mjs');
  await build({entryPoints:['src/features/hall/logic.ts'],bundle:true,platform:'node',format:'esm',outfile:out,logLevel:'error'});
  const L=await import(pathToFileURL(out).href);
  test('[CU-TRN] F17 FY26: the quotation terms for an event type exist (seeded templates) before any save',()=>{
    const s=fy();assert.equal(s.hallX,undefined);
    const t=L.hallTncFor(s,'WEDDING');assert.ok(t,'terms found');assert.equal(t.clauses.length,10);assert.equal(t.hasPlaceholder,true);
  });
  test('[CU-TRN] F17 FY26: the Owner replaces the terms; the next quotation uses them; others are refused',()=>{
    let s=fy();const clauses=L.hallTncFor(s,'WEDDING').clauses.map(c=>({n:c.n,heading:c.heading,text:'Fictional clause '+c.n}));
    assert.equal(code(()=>execute(s,'manager',{type:'HALL_TNC_TEMPLATE_SAVE',eventType:'WEDDING',clauses})),'FORBIDDEN');
    s=execute(s,'owner',{type:'HALL_TNC_TEMPLATE_SAVE',eventType:'WEDDING',clauses});
    const t=L.hallTncFor(s,'WEDDING');assert.equal(t.hasPlaceholder,false);assert.equal(t.v,2);assert.equal(t.clauses[0].text,'Fictional clause 1');
    assert.equal(L.hallTncFor(s,'CORPORATE').hasPlaceholder,true,'other templates untouched');
  });

  // ── F8 · the desk banner offers Done only to users the command will accept (same rule: gsvCanWork) ──
  const wake=s=>{const b=s.bookings.find(b=>b.status==='In house');
    s=execute(s,'reception',{type:'GSV_REQUEST_CREATE',id:b.id,requestType:'Wake-up call',time:'06:30'});const r=M.gsvSlice(s).requests.at(-1);return [s,r.id,r];};
  for(const u of ['reception','manager','owner'])
    test(`[CU-TRN] F8: ${u} with stock grants sees Done and the command succeeds`,()=>{
      let [s,id,r]=wake(fy());assert.equal(M.gsvCanWork(s,u,r),true);
      s=execute(s,u,{type:'GSV_REQUEST_COMPLETE',id,note:'Wake-up call made'});
      assert.equal(M.gsvSlice(s).requests.find(x=>x.id===id).status,'Done');
    });
  test('[CU-TRN] F8: the Cashier sees no Done (gsvCanWork false) and the command is refused',()=>{
    const [s,id,r]=wake(fy());assert.equal(M.gsvCanWork(s,'cashier',r),false);
    assert.equal(code(()=>execute(s,'cashier',{type:'GSV_REQUEST_COMPLETE',id,note:'Wake-up call made'})),'FORBIDDEN');
  });
  test('[CU-TRN] F8: a Reception user whose reservations grant is revoked sees no Done and is refused (as the server)',()=>{
    const [s0,id,r]=wake(fy());const s=execute(s0,'owner',{type:'GRANT',role:'Reception',permission:'reservations'});
    assert.equal(M.can(s,'reception','reservations'),false);assert.equal(M.gsvCanWork(s,'reception',r),false);
    assert.equal(code(()=>execute(s,'reception',{type:'GSV_REQUEST_COMPLETE',id,note:'Wake-up call made'})),'FORBIDDEN');
  });
  test('[CU-TRN] F8: the three alert views gate Done / Snooze on gsvCanWork (source check)',async()=>{
    const {readFile}=await import('node:fs/promises');
    for(const f of ['src/app/DeskAlerts.tsx','src/features/alerts/DeskAlertsPage.tsx','src/features/alerts/Mobile.tsx'])assert.match(await readFile(f,'utf8'),/gsvCanWork\(s,uid,/,f);
  });

  // ── F19 · OTA inbox rows can be accepted in FY26, also after the day-close chapters moved the date on ──
  for(const day of ['2026-10-24','2026-10-26'])
    test(`[CU-TRN] F19 FY26 on ${day}: every pending new-booking inbox row passes the accept pre-check and can be accepted`,()=>{
      let s=fy();s.date=day;s.clock=day+'T09:00';
      const rows=s.channelsX.messages.filter(m=>m.status==='Pending'&&m.kind==='New');assert.ok(rows.length>=5);
      for(const m of rows)assert.equal(M.chnAcceptCheck(s,m).ok,true,m.id+' '+M.chnAcceptCheck(s,m).reason);
      for(const m of rows)s=execute(s,'reception',{type:'CHN_ACCEPT',id:m.id});
      assert.equal(s.channelsX.messages.filter(m=>rows.some(r=>r.id===m.id)&&m.booking).length,rows.length);
    });
  test('[CU-TRN] F19: BASE is untouched (its inbox rows keep the original dates and party)',()=>{
    const s=seed();const m=s.channelsX.messages.find(m=>m.id==='CHM-F5');assert.equal(m.arrival,'2026-10-26');assert.equal(m.adults,3);
    assert.equal(s.channelsX.messages.find(m=>m.id==='CHM-F1').arrival,'2026-10-27');
  });
  test('[CU-TRN] F19 FY26: BN-88231 (the training share) is unchanged and a modify of an accepted row still applies',()=>{
    let s=fy();assert.equal(s.channelsX.messages.find(m=>m.id==='CHM-F1').arrival,'2026-10-27');
    s=execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F2'});s=execute(s,'reception',{type:'CHN_ACCEPT',id:'CHM-F6'});
    assert.equal(s.channelsX.messages.find(m=>m.id==='CHM-F6').status,'Applied');
  });
}
