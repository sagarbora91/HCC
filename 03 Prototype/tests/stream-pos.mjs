// Module 23 · Restaurant POS & room service — domain/command tests. Names prefixed [POS].
export function register({test,M,assert}){
  const {execute,seed,loadScenario,drawer,totals,metrics,HccError}=M;
  const code=(fn)=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const P=s=>M.posSlice(s);
  const last=s=>P(s).orders.at(-1);
  const ord=(s,id)=>P(s).orders.find(o=>o.id===id);
  /** Open a fresh table check with items sent to the kitchen. */
  const table=(s=seed(),u='cashier',tbl='T3',items=[['m-dal',1],['m-roti',2]])=>{
    s=execute(s,u,{type:'POS_ORDER_OPEN',outlet:'restaurant',table:tbl,covers:2});const id=last(s).id;
    for(const [item,qty] of items)s=execute(s,u,{type:'POS_ITEM_ADD',id,item,qty});
    s=execute(s,u,{type:'POS_KOT_SEND',id});return [s,id];
  };
  const inHouse201=s=>s.bookings.find(b=>b.room==='201'&&b.status==='In house');

  test('[POS] fixture: outlets, menu, checks; core figures unchanged',()=>{
    const s=seed();
    assert.equal(P(s).outlets.length,2);assert.ok(P(s).menu.length>=20);assert.equal(P(s).orders.length,4);
    assert.equal(metrics(s).occupied,8);assert.equal(metrics(s).collections,2150000);assert.equal(drawer(s),1072500); // DEC-069 GST: drawer 1075000 → 1072500 (POS-F01 cash ₹500+₹50 → ₹500+₹25)
    const sum=M.posDaySummary(s);
    // DEC-069 GST: restaurant 5 % instead of the synthetic 10 % — tax 11800 → 5900, gross 129800 → 123900, Cash 55000 → 52500, UPI 74800 → 71400.
    assert.equal(sum.checks,2);assert.equal(sum.net,118000);assert.equal(sum.tax,5900);assert.equal(sum.gross,123900);
    assert.equal(sum.byMode.Cash,52500);assert.equal(sum.byMode.UPI,71400);assert.equal(sum.outletCash,52500);
    assert.equal(sum.open.count,2);
    // Fixture checks never touch bookings.
    assert.ok(s.bookings.every(b=>b.lines.every(l=>!String(l.id).startsWith('posline:'))));
    assert.equal(P(loadScenario('S1')).orders.length,0);
    assert.deepEqual(M.posSlice({}),M.posEmpty());
  });

  test('[POS] open → add (merges unsent) → qty/remove → KOT → kitchen flow',()=>{
    let s=seed();
    s=execute(s,'reception',{type:'POS_ORDER_OPEN',outlet:'restaurant',table:'T6',covers:2});const id=last(s).id;
    assert.equal(code(()=>execute(s,'reception',{type:'POS_ORDER_OPEN',outlet:'restaurant',table:'T6'})),'NOT_AVAILABLE');
    assert.equal(code(()=>execute(s,'reception',{type:'POS_KOT_SEND',id})),'VALIDATION');
    s=execute(s,'reception',{type:'POS_ITEM_ADD',id,item:'m-paneer'});s=execute(s,'reception',{type:'POS_ITEM_ADD',id,item:'m-paneer',qty:2});
    s=execute(s,'reception',{type:'POS_ITEM_ADD',id,item:'m-naan',qty:3});
    assert.equal(ord(s,id).lines.length,2);assert.equal(ord(s,id).lines[0].qty,3);
    const naan=ord(s,id).lines[1].id;
    s=execute(s,'reception',{type:'POS_ITEM_QTY',id,line:naan,qty:0});assert.equal(ord(s,id).lines.length,1);
    assert.equal(code(()=>execute(s,'reception',{type:'POS_ITEM_ADD',id,item:'m-biryani'})),'NOT_AVAILABLE'); // 86'd in fixture
    assert.equal(code(()=>execute(s,'reception',{type:'POS_ITEM_ADD',id,item:'nope'})),'NOT_FOUND');
    s=execute(s,'reception',{type:'POS_KOT_SEND',id});
    const o=ord(s,id),k=o.kots[0];assert.equal(k.no,4);assert.equal(k.status,'Preparing');assert.equal(M.posLineStatus(o,o.lines[0]),'Preparing');
    assert.equal(code(()=>execute(s,'reception',{type:'POS_ITEM_QTY',id,line:o.lines[0].id,qty:1})),'INVALID_TRANSITION');
    assert.equal(code(()=>execute(s,'reception',{type:'POS_KOT_STATUS',id,kot:k.id,status:'Served'})),'INVALID_TRANSITION');
    s=execute(s,'reception',{type:'POS_KOT_STATUS',id,kot:k.id,status:'Ready'});s=execute(s,'reception',{type:'POS_KOT_STATUS',id,kot:k.id,status:'Served'});
    assert.equal(ord(s,id).kots[0].status,'Served');
    assert.equal(code(()=>execute(s,'reception',{type:'POS_KOT_STATUS',id,kot:k.id,status:'Served'})),'INVALID_TRANSITION');
    assert.ok(M.posKitchen(s).some(x=>x.kot.id===k.id));
  });

  test('[POS] settle at outlet: UPI reference, cash needs open shift, idempotent, cash counted once in the drawer',()=>{
    let [s,id]=table();const before=drawer(s);
    assert.equal(M.posOrderTotals(s,ord(s,id)).net,36000); // 280 + 2×40
    assert.throws(()=>execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'UPI'}),/reference/);
    assert.equal(code(()=>execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Cheque',reference:'x'})),'VALIDATION');
    const closed=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)});
    assert.throws(()=>execute(closed,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Cash'}),/shift is closed/);
    s=execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Cash',key:'pos-settle-1'});
    const o=ord(s,id);assert.equal(o.status,'Settled');assert.deepEqual([o.settlement.net,o.settlement.tax,o.settlement.total,o.settlement.taxBps],[36000,1800,37800,500]); // DEC-069 GST: [36000,3600,39600,1000] → [36000,1800,37800,500]
    assert.equal(o.settlement.billNo,'POS/2610/0003');
    assert.deepEqual(execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Cash',key:'pos-settle-1'}),s);
    assert.equal(code(()=>execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Cash'})),'INVALID_TRANSITION');
    assert.equal(drawer(s),before+37800); // shared till (DEC-052): the ₹378 cash bill enters the front-desk drawer once (DEC-069 GST: was ₹396)
    assert.equal(M.posDaySummary(s).outletCash,52500+37800); // DEC-069 GST: 55000+39600 → 52500+37800
    assert.equal(code(()=>execute(s,'cashier',{type:'POS_ITEM_ADD',id,item:'m-tea'})),'INVALID_TRANSITION');
  });

  test('[POS] settle blocked with unsent items; empty check cannot settle',()=>{
    let [s,id]=table();s=execute(s,'cashier',{type:'POS_ITEM_ADD',id,item:'m-tea'});
    assert.throws(()=>execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Cash'}),/Send the remaining items/);
    s=execute(s,'cashier',{type:'POS_ORDER_OPEN',outlet:'restaurant',table:'T7'});
    assert.throws(()=>execute(s,'cashier',{type:'POS_SETTLE',id:last(s).id,mode:'outlet',method:'Cash'}),/no items/);
  });

  test('[POS] post to room folio: CHARGE line shape, tax snapshot, once only',()=>{
    let s=seed();const b=inHouse201(s);
    s=execute(s,'reception',{type:'POS_ITEM_ADD',id:'POS-F04',item:'m-lassi'});s=execute(s,'reception',{type:'POS_KOT_SEND',id:'POS-F04'});
    const t0=totals(s.bookings.find(x=>x.id===b.id)),v0=s.bookings.find(x=>x.id===b.id).version;
    s=execute(s,'reception',{type:'POS_SETTLE',id:'POS-F04',mode:'folio',key:'pos-folio-F04'});
    const bk=s.bookings.find(x=>x.id===b.id),l=bk.lines.at(-1);
    assert.deepEqual(Object.keys(l).sort(),['amount','date','id','kind','label','taxBps']);
    assert.equal(l.kind,'service');assert.equal(l.amount,24000+6000+14000);assert.equal(l.taxBps,500); // DEC-069 GST: 1000 → 500 (restaurant / room service 5 %)assert.equal(l.date,s.date);assert.equal(l.id,'posline:POS-F04');
    assert.equal(totals(bk).total-t0.total,44000+2200);assert.equal(bk.version,v0+1); // DEC-069 GST: tax 4400 → 2200
    assert.equal(ord(s,'POS-F04').settlement.total,46200); // DEC-069 GST: 48400 → 46200
    assert.deepEqual(execute(s,'reception',{type:'POS_SETTLE',id:'POS-F04',mode:'folio',key:'pos-folio-F04'}),s);
    assert.equal(code(()=>execute(s,'reception',{type:'POS_SETTLE',id:'POS-F04',mode:'folio'})),'INVALID_TRANSITION');
    assert.equal(bk.lines.filter(x=>x.id==='posline:POS-F04').length,1);
    // Tax snapshot survives a later rate change (DEC-069: through the dated GST master instead of SETTINGS taxBps).
    const after=execute(s,'owner',{type:'SETTING_SET',setting:'gst.food.bps',value:1200,effectiveFrom:s.date,reason:'CA advice'});
    assert.equal(after.bookings.find(x=>x.id===b.id).lines.at(-1).taxBps,500);assert.equal(M.posOrderTotals(after,ord(after,'POS-F04')).taxBps,500);
    assert.equal(M.posDaySummary(s).byMode['Room folio'],46200); // DEC-069 GST: 48400 → 46200
  });

  test('[POS] folio post rules: in-house only, day closed, restaurant check charged to a room',()=>{
    let [s,id]=table();
    const confirmed=s.bookings.find(b=>b.status==='Confirmed');
    assert.equal(code(()=>execute(s,'cashier',{type:'POS_SETTLE',id,mode:'folio',booking:confirmed.id})),'INVALID_TRANSITION');
    assert.equal(code(()=>execute(s,'cashier',{type:'POS_SETTLE',id,mode:'folio'})),'NOT_FOUND');
    const b=s.bookings.find(b=>b.status==='In house'&&b.room==='102');
    const c=structuredClone(s);c.closedDates.push(c.date);
    assert.equal(code(()=>execute(c,'cashier',{type:'POS_SETTLE',id,mode:'folio',booking:b.id})),'DAY_CLOSED');
    assert.equal(code(()=>execute(c,'cashier',{type:'POS_ORDER_OPEN',outlet:'restaurant',table:'T8'})),'DAY_CLOSED');
    s=execute(s,'cashier',{type:'POS_SETTLE',id,mode:'folio',booking:b.id});
    assert.equal(ord(s,id).booking,b.id);assert.equal(s.bookings.find(x=>x.id===b.id).lines.at(-1).amount,36000);
    assert.equal(code(()=>execute(s,'cashier',{type:'POS_VOID_BILL',id,reason:'Wrong room'})),'INVALID_TRANSITION');
  });

  test('[POS] room service: only for in-house rooms; one open check per room',()=>{
    const s=seed();
    assert.equal(code(()=>execute(s,'reception',{type:'POS_ORDER_OPEN',outlet:'room-service',room:'105'})),'INVALID_TRANSITION');
    assert.equal(code(()=>execute(s,'reception',{type:'POS_ORDER_OPEN',outlet:'room-service',room:'201'})),'NOT_AVAILABLE');
    const n=execute(s,'reception',{type:'POS_ORDER_OPEN',outlet:'room-service',room:'102'});
    assert.equal(last(n).booking,s.bookings.find(b=>b.room==='102'&&b.status==='In house').id);
    assert.equal(code(()=>execute(n,'reception',{type:'POS_ITEM_ADD',id:last(n).id,item:'m-kebab'})),'VALIDATION'); // restaurant only
  });

  test('[POS] void item ≤ ₹500 immediate; unsent item cannot be voided; reason required',()=>{
    let [s,id]=table();const l=ord(s,id).lines[0];
    assert.throws(()=>execute(s,'cashier',{type:'POS_VOID_ITEM',id,line:l.id,reason:''}),/reason/);
    s=execute(s,'cashier',{type:'POS_VOID_ITEM',id,line:l.id,reason:'Guest changed mind'});
    assert.equal(ord(s,id).lines[0].voided,true);assert.equal(M.posOrderTotals(s,ord(s,id)).net,8000);
    assert.equal(M.posDaySummary(s).voids.count,1);assert.equal(M.posDaySummary(s).voids.value,28000);
    s=execute(s,'cashier',{type:'POS_ITEM_ADD',id,item:'m-tea'});
    assert.equal(code(()=>execute(s,'cashier',{type:'POS_VOID_ITEM',id,line:ord(s,id).lines.at(-1).id,reason:'Wrong item'})),'INVALID_TRANSITION');
  });

  test('[POS] void above ₹500 needs approval: lock, no self-approval, inbox routing, executed',()=>{
    let [s,id]=table(seed(),'cashier','T3',[['m-curry',2]]); // ₹920
    const l=ord(s,id).lines[0];
    s=execute(s,'cashier',{type:'POS_VOID_ITEM',id,line:l.id,reason:'Dish returned cold'});
    const a=s.approvals.at(-1);assert.equal(a.kind,'pos-void');assert.equal(a.status,'Pending');assert.equal(a.amount,92000);assert.equal(a.target,id);
    assert.equal(ord(s,id).lines[0].voided,undefined);assert.equal(ord(s,id).pendingVoid,a.id);
    assert.equal(code(()=>execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Cash'})),'INVALID_TRANSITION');
    assert.equal(code(()=>execute(s,'cashier',{type:'POS_VOID_DECIDE',id:a.id,approve:true})),'FORBIDDEN'); // no approve permission
    const own=execute(s,'manager',{type:'POS_VOID_ITEM',id:'POS-F03',line:'POSL-F07',reason:'Kitchen error'}); // 2 thalis ₹840
    assert.throws(()=>execute(own,'manager',{type:'POS_VOID_DECIDE',id:own.approvals.at(-1).id,approve:true}),/own request/);
    const row=M.adminApprovalInbox(s).find(r=>r.a.id===a.id);assert.equal(row.command,'POS_VOID_DECIDE');assert.equal(row.status,'Pending');
    assert.throws(()=>execute(s,'owner',{type:'DAY_CLOSE'}),/pending/i);
    s=execute(s,'manager',{type:row.command,id:a.id,approve:true,key:'approval:'+a.id});
    assert.equal(s.approvals.find(x=>x.id===a.id).status,'Executed');assert.equal(ord(s,id).lines[0].voided,true);assert.equal(ord(s,id).lines[0].voidApproval,a.id);
    assert.equal(ord(s,id).pendingVoid,undefined);
    assert.equal(code(()=>execute(s,'manager',{type:'POS_VOID_DECIDE',id:a.id,approve:true})),'INVALID_TRANSITION');
  });

  test('[POS] void reject unlocks; housekeeper has no POS access',()=>{
    let s=execute(seed(),'reception',{type:'POS_VOID_BILL',id:'POS-F03',reason:'Party left'});
    const a=s.approvals.at(-1);assert.equal(a.amount,(84000+38000+28000+24000));
    s=execute(s,'owner',{type:'POS_VOID_DECIDE',id:a.id,approve:false,reason:'Food already served'});
    assert.equal(s.approvals.at(-1).status,'Rejected');assert.equal(ord(s,'POS-F03').status,'Open');assert.equal(ord(s,'POS-F03').pendingVoid,undefined);
    for(const cmd of [{type:'POS_ORDER_OPEN',outlet:'restaurant',table:'T8'},{type:'POS_ITEM_ADD',id:'POS-F03',item:'m-tea'},{type:'POS_SETTLE',id:'POS-F03',mode:'outlet',method:'Cash'},{type:'POS_MENU_TOGGLE',item:'m-tea'}])
      assert.equal(code(()=>execute(seed(),'housekeeper',cmd)),'FORBIDDEN');
  });

  test('[POS] void settled outlet bill reverses outlet receipt; menu 86 toggle',()=>{
    let s=seed();
    s=execute(s,'cashier',{type:'POS_VOID_BILL',id:'POS-F02',reason:'Charged twice'}); // ₹714 > limit (DEC-069 GST: was ₹748)
    const a=s.approvals.at(-1);assert.equal(a.amount,71400); // DEC-069 GST: 74800 → 71400
    s=execute(s,'manager',{type:'POS_VOID_DECIDE',id:a.id,approve:true});
    const o=ord(s,'POS-F02');assert.equal(o.status,'Void');assert.equal(P(s).receipts.at(-1).amount,-71400); // DEC-069 GST: -74800 → -71400
    const sum=M.posDaySummary(s);assert.equal(sum.checks,1);assert.equal(sum.byMode.UPI,0);assert.equal(sum.outletCash,52500); // DEC-069 GST: 55000 → 52500
    s=execute(s,'cashier',{type:'POS_MENU_TOGGLE',item:'m-biryani'});assert.equal(P(s).menu.find(m=>m.id==='m-biryani').available,true);
  });

  test('[POS] stale approval: check changed after request',()=>{
    let s=execute(seed(),'reception',{type:'POS_VOID_BILL',id:'POS-F03',reason:'Party left'});
    const a=s.approvals.at(-1);
    const changed=structuredClone(s);changed.posX.orders.find(o=>o.id==='POS-F03').version++;
    assert.equal(code(()=>execute(changed,'manager',{type:'POS_VOID_DECIDE',id:a.id,approve:true})),'STALE_VERSION');
    const late=structuredClone(s);late.clock='2026-10-25T09:00';
    assert.equal(code(()=>execute(late,'manager',{type:'POS_VOID_DECIDE',id:a.id,approve:true})),'INVALID_TRANSITION');
  });
}
