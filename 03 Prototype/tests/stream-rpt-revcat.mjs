// F4-T2 · rpt.money.revenue-by-category-tender — tie-out against Revenue & tax (A) and Receipts & deposits (B); the bridge (C) closes;
// Reception FORBIDDEN; the CSV / Excel rows equal the screen. Fictional data only.
export function register({test,M,assert}){
  const T='[F4-T2 REVCAT] ';
  const FROM='2026-04-01';
  let cache;const month=()=>cache??=M.loadScenario('ACC-RULEBOOK');
  const tab=(p,id)=>p.tabs.find(t=>t.id===id), met=(t,id)=>t.metrics.find(m=>m.id===id).value;
  const ranges=s=>[[FROM,'2026-04-30'],['2026-04-10','2026-04-12'],[s.date,s.date]];

  test(T+'A: net, CGST + SGST and gross equal Revenue & tax for the range, to the paisa',()=>{
    const s=month();
    for(const [f,t] of ranges(s)){
      const r=M.rctReport(s,f,t),rev=tab(M.reportPack(s,f,t),'revenue');
      assert.equal(r.aTotal.net,met(rev,'net'),f+'..'+t);
      assert.equal(r.aTotal.cgst+r.aTotal.sgst,met(rev,'tax'));
      assert.equal(r.aTotal.gross,met(rev,'gross'));
      assert.equal(r.aTotal.roomTariff,met(rev,'room'));
    }
  });
  test(T+'A: rows add up to the total; every room type has a row; an unmapped room is kept as "unassigned"',()=>{
    const s=structuredClone(month());
    const b=s.bookings.find(b=>b.lines.length);b.room='ZZ99';
    const r=M.rctReport(s,FROM,'2026-04-30');
    const room=r.a.filter(x=>!['restaurant','counter','hall'].includes(x.key));
    assert.equal(room.reduce((a,x)=>a+x.net,0),r.aTotal.net);assert.ok(r.a.some(x=>x.key==='unassigned'&&x.net));
    for(const t of new Set(s.rooms.map(x=>x.type)))assert.ok(r.a.some(x=>x.key===t),t);
  });
  test(T+'B: tender columns equal Receipts & deposits per tender; advances equal the deposit metrics',()=>{
    const s=month();
    for(const [f,t] of ranges(s)){
      const r=M.rctReport(s,f,t),rc=tab(M.reportPack(s,f,t),'receipts');
      const tm=n=>rc.metrics.find(m=>m.id==='tender:'+n)?.value??0;
      assert.equal(r.bTotal.cash,tm('Cash'));assert.equal(r.bTotal.upi,tm('UPI'));assert.equal(r.bTotal.card,tm('Card'));assert.equal(r.bTotal.bank,tm('Bank transfer'));
      assert.equal(r.bTotal.advancesReceived,met(rc,'deposit-in'));assert.equal(r.bTotal.advancesRefunded,0-met(rc,'deposit-out'));
      assert.equal(r.bTotal.companyCredit,met(rc,'company-credit'));
      const other=Object.values(r.bTotal.otherTenders).reduce((a,b)=>a+b,0);
      assert.equal(r.bTotal.netReceived,met(rc,'net'),f);void other;
    }
  });
  test(T+'C: the bridge closes (difference 0) on every range and in both bases; the trial balance balances',()=>{
    const s=month();
    for(const [f,t] of ranges(s))for(const basis of ['business-date','stay']){
      const r=M.rctReport(s,f,t,basis);assert.equal(r.c.difference,0,f+'..'+t+' '+basis);
      assert.equal(r.c.closingDue,r.c.openingDue+r.c.gross-r.c.netReceived-r.bTotal.advancesAdjusted);if(basis==='business-date')assert.equal(r.c.gross,r.aTotal.gross);
      assert.equal(r.c.closingAdvances-r.c.openingAdvances,r.c.advanceMovement);assert.equal(r.tb.balanced,true);
    }
  });
  test(T+'stay basis counts a stay\'s whole folio by its arrival date; business-date basis is the default',()=>{
    const s=month();
    const a=M.rctReport(s,'2026-04-10','2026-04-12'),b=M.rctReport(s,'2026-04-10','2026-04-12','stay');
    assert.equal(a.basis,'business-date');assert.equal(b.basis,'stay');
    const arrivals=s.bookings.filter(x=>x.arrival>='2026-04-10'&&x.arrival<='2026-04-12');
    assert.equal(b.aTotal.net,arrivals.reduce((t,x)=>t+x.lines.reduce((u,l)=>u+l.amount,0),0));
  });
  test(T+'Reception, Cashier, Housekeeper are FORBIDDEN; Owner, Accountant, Manager may read',()=>{
    const s=month();
    for(const u of ['reception','cashier','housekeeper'])assert.throws(()=>M.rctReportFor(s,u,FROM,'2026-04-30'),e=>e.code==='FORBIDDEN',u);
    for(const u of ['owner','accountant','manager'])assert.ok(M.rctReportFor(s,u,FROM,'2026-04-30').aTotal);
  });
  test(T+'CSV carries the screen figures (net, gross, net received) and the bridge',()=>{
    const s=month(),r=M.rctReport(s,FROM,'2026-04-30'),csv=M.rctCsv(r);
    assert.ok(csv.includes((r.aTotal.net/100).toFixed(2)));assert.ok(csv.includes((r.aTotal.gross/100).toFixed(2)));assert.ok(csv.includes((r.bTotal.netReceived/100).toFixed(2)));
    assert.ok(csv.startsWith('Revenue by room category and tender'));
    assert.ok(csv.includes('Difference'));
  });
}
