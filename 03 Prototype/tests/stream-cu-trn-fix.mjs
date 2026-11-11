// CU lane trn-fix · four defects found while making the training course (06 Training/TRAINING_NOTES.md F14, F17, F8, F19). Names prefixed [CU-TRN].
import {build} from 'esbuild';
import {pathToFileURL} from 'node:url';
import {resolve} from 'node:path';
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

  // ── F8 · Reception and Manager mark a wake-up call Done ──
  const wake=s=>{const b=s.bookings.find(b=>b.status==='In house');
    s=execute(s,'reception',{type:'GSV_REQUEST_CREATE',id:b.id,requestType:'Wake-up call',time:'06:30'});return [s,M.gsvSlice(s).requests.at(-1).id];};
  for(const u of ['reception','manager','owner'])
    test(`[CU-TRN] F8: ${u} can mark a wake-up call Done, also when the coarse reservations grant is removed`,()=>{
      let [s,id]=wake(fy());s.grants=JSON.parse(JSON.stringify(s.grants));
      for(const r of ['Reception','Manager','Owner'])s.grants[r]=(s.grants[r]??[]).filter(p=>p!=='reservations');
      s=execute(s,u,{type:'GSV_REQUEST_COMPLETE',id,note:'Wake-up call made'});
      assert.equal(M.gsvSlice(s).requests.find(r=>r.id===id).status,'Done');
    });
  test('[CU-TRN] F8: the rule is for the desk only - a Housekeeper is still refused',()=>{
    const [s,id]=wake(fy());assert.equal(code(()=>execute(s,'housekeeper',{type:'GSV_REQUEST_COMPLETE',id,note:'Wake-up call made'})),'FORBIDDEN');
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
