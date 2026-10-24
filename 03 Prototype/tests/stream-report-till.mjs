export function register({test,M,assert}){
  const tab=(s,from=s.date,to=s.date)=>M.reportPack(s,from,to).tabs.find(t=>t.id==='expenses');
  const value=(t,id)=>t.metrics.find(m=>m.id===id).value;
  const reconcile=s=>{const t=tab(s);assert.equal(s.openingCash+value(t,'cash-in')+value(t,'cash-out')-value(t,'cash'),M.drawer(s));
    for(const m of t.metrics)assert.equal(m.value,m.rows.reduce((a,r)=>a+r.amount,0));return t;};
  test('[REPORT-TILL] BASE shared cash report includes POS once, matching drawer and drill-down',()=>{
    const s=M.seed(),t=reconcile(s),rows=t.metrics.find(m=>m.id==='cash-in').rows;
    assert.equal(value(t,'cash-in'),755000);
    assert.equal(rows.filter(r=>r.ref==='POSR-F01').length,1);
    assert.equal(rows.some(r=>r.ref==='POSR-F02'),false);
    assert.equal(M.drawer(s),1075000);
    const pack=M.reportPack(s,s.date,s.date),metric=t.metrics.find(m=>m.id==='cash-in');
    assert.match(M.reportCsv(s,pack,'expenses'),/"Cash into drawer \(payments \+ deposits \+ POS sales\)","7550.00"/);
    assert.match(M.reportDrillCsv(s,pack,metric),/POSR-F01/);
  });
  test('[REPORT-TILL] cash void is signed outflow; room-folio settlement and UPI do not add cash',()=>{
    let s=M.seed();
    s=M.execute(s,'cashier',{type:'POS_VOID_BILL',id:'POS-F01',reason:'Report regression'});
    assert.equal(value(reconcile(s),'cash-out'),0);
    s=M.execute(s,'manager',{type:'POS_VOID_DECIDE',id:s.approvals.at(-1).id,approve:true});
    let t=reconcile(s);assert.equal(value(t,'cash-out'),-55000);assert.equal(value(t,'cash-in'),755000);
    s=M.execute(s,'cashier',{type:'POS_SETTLE',id:'POS-F04',mode:'folio'});
    s=M.execute(s,'cashier',{type:'POS_KOT_SEND',id:'POS-F03'});
    s=M.execute(s,'cashier',{type:'POS_SETTLE',id:'POS-F03',mode:'outlet',method:'UPI',reference:'TEST-UPI'});
    t=reconcile(s);assert.equal(value(t,'cash-in'),755000);assert.equal(value(t,'cash-out'),-55000);
  });
  test('[REPORT-TILL] inclusive range filters POS by receipt date; historical daily flows match closing cash',()=>{
    const s=M.loadScenario('FY26');
    for(const d of ['2026-04-01','2026-10-23']){
      const t=tab(s,d,d),closing=s.closings[d];
      assert.equal(closing.cash.opening+value(t,'cash-in')+value(t,'cash-out')-value(t,'cash'),closing.cash.expected);
      for(const m of t.metrics)assert.ok(m.rows.every(r=>r.date===d));
    }
    const empty=tab(s,'2027-01-01','2027-01-02');assert.equal(value(empty,'cash-in'),0);assert.equal(value(empty,'cash-out'),0);
    const combined=tab(s,'2026-10-22','2026-10-23');
    for(const id of ['cash-in','cash-out'])assert.equal(value(combined,id),value(tab(s,'2026-10-22','2026-10-22'),id)+value(tab(s,'2026-10-23','2026-10-23'),id));
  });
}
