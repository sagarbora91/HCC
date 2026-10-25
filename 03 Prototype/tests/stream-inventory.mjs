// Module 16 · Linen, laundry & stock — domain/command tests. Names prefixed [INV].
export function register({test,M,assert}){
  const {seed,execute,invSlice,invItem,invClean,invTotal,invAlerts,invConsumption,invParRequired,invOccupied,invMoneyRole}=M;
  const it=(s,id)=>invItem(s,id);
  const err=(fn,re,code)=>assert.throws(fn,e=>(!code||e.code===code)&&(!re||re.test(e.message)));
  test('[INV] slice: absent slice reads as empty; fixture seeds 17 items, alerts and history',()=>{
    const s=seed();const e={...s,inventoryX:undefined};assert.equal(invSlice(e).items.length,0);
    assert.equal(invSlice(s).items.length,17);
    assert.deepEqual(invAlerts(s).map(a=>a.item.id).sort(),['AMN-SH','CLN-GB','LIN-BT']);
    assert.equal(invAlerts(s).find(a=>a.item.id==='LIN-BT').suggest,65); // max(60, 2×55−45)
    assert.equal(invParRequired(s,it(s,'LIN-BS')),144); // 24 rooms × 2 × 3 sets
  });
  test('[INV] fixture keeps core figures unchanged',()=>{const s=seed();assert.equal(M.metrics(s).occupied,8);assert.equal(M.metrics(s).collections,2150000);assert.equal(M.drawer(s),1072500);}); // DEC-069 GST: drawer 1075000 → 1072500 (POS-F01 cash ₹500+₹50 → ₹500+₹25)
  test('[INV] issue store → floor moves stock; over-issue rejected without change',()=>{
    let s=seed();s=execute(s,'housekeeper',{type:'INV_ISSUE',item:'LIN-BS',qty:10,from:'store',to:'floor1'});
    assert.equal(it(s,'LIN-BS').stock.store,30);assert.equal(it(s,'LIN-BS').stock.floor1,16);
    const before=JSON.stringify(s);err(()=>execute(s,'housekeeper',{type:'INV_ISSUE',item:'LIN-BS',qty:31,from:'store',to:'floor1'}),/Only 30/,'VALIDATION');
    assert.equal(JSON.stringify(s),before);
    err(()=>execute(s,'housekeeper',{type:'INV_ISSUE',item:'LIN-BS',qty:1.5,from:'store',to:'floor1'}),/whole number/);
    err(()=>execute(s,'housekeeper',{type:'INV_ISSUE',item:'LIN-BS',qty:1,from:'store',to:'store'}),/different/);
    err(()=>execute(s,'housekeeper',{type:'INV_ISSUE',item:'LIN-BS',qty:1,from:'store',to:'rooms'}),/valid location/);
  });
  test('[INV] permissions: reception/accountant denied; housekeeper cannot GRN or post counts; cost entry only for money roles',()=>{
    const s=seed();
    err(()=>execute(s,'reception',{type:'INV_ISSUE',item:'LIN-BS',qty:1,from:'store',to:'floor1'}),/housekeeping/,'FORBIDDEN');
    err(()=>execute(s,'accountant',{type:'INV_USE',item:'AMN-SP',qty:1,floor:'floor1'}),null,'FORBIDDEN');
    err(()=>execute(s,'housekeeper',{type:'INV_GRN',supplier:'Morning Supplies',lines:[{item:'AMN-SH',qty:10}]}),/supervisor or manager/,'FORBIDDEN');
    err(()=>execute(s,'supervisor',{type:'INV_GRN',supplier:'Morning Supplies',lines:[{item:'AMN-SH',qty:10,cost:1500}]}),/unit cost/,'FORBIDDEN');
    err(()=>execute(s,'housekeeper',{type:'INV_COUNT_POST',id:'CNT-S2',approve:true,reason:'test'}),null,'FORBIDDEN');
    err(()=>execute(s,'housekeeper',{type:'INV_ITEM_SET',id:'AMN-SP',min:1,reorderQty:1}),null,'FORBIDDEN');
    assert.equal(invMoneyRole(s,'housekeeper'),false);assert.equal(invMoneyRole(s,'supervisor'),false);assert.equal(invMoneyRole(s,'manager'),true);assert.equal(invMoneyRole(s,'owner'),true);
  });
  test('[INV] GRN: supervisor receives to store and clears the alert; manager cost → weighted average',()=>{
    let s=seed();s=execute(s,'supervisor',{type:'INV_GRN',supplier:'Morning Supplies',ref:'MS/1',lines:[{item:'AMN-SH',qty:300}]});
    assert.equal(it(s,'AMN-SH').stock.store,370);assert.ok(!invAlerts(s).some(a=>a.item.id==='AMN-SH'));
    assert.equal(invSlice(s).grns.at(-1).ref,'MS/1');assert.equal(invSlice(s).moves.at(-1).kind,'grn');
    const total=invTotal(it(s,'CLN-GB'));// 6 rolls at ₹60
    s=execute(s,'manager',{type:'INV_GRN',supplier:'Deccan Hygiene Traders',lines:[{item:'CLN-GB',qty:6,cost:8000}]});
    assert.equal(total,6);assert.equal(it(s,'CLN-GB').cost,7000);
    err(()=>execute(s,'manager',{type:'INV_GRN',supplier:'X',lines:[{item:'CLN-GB',qty:1}]}),/supplier/);
    err(()=>execute(s,'manager',{type:'INV_GRN',supplier:'Deccan',lines:[{item:'CLN-GB',qty:1},{item:'CLN-GB',qty:2}]}),/only once/);
  });
  test('[INV] use: supplies only, from a floor pantry; linen change sends used pieces to soiled',()=>{
    let s=seed();s=execute(s,'housekeeper',{type:'INV_USE',item:'AMN-SP',qty:4,floor:'floor2',room:'204'});
    assert.equal(it(s,'AMN-SP').stock.floor2,24);
    err(()=>execute(s,'housekeeper',{type:'INV_USE',item:'LIN-BS',qty:1,floor:'floor1'}),/linen change/);
    err(()=>execute(s,'housekeeper',{type:'INV_ROOM_CHANGE',item:'AMN-SP',qty:1,floor:'floor1'}),/Only linen/);
    const r0=it(s,'LIN-BT').stock.rooms;s=execute(s,'housekeeper2',{type:'INV_ROOM_CHANGE',item:'LIN-BT',qty:4,floor:'floor3'});
    assert.equal(it(s,'LIN-BT').stock.floor3,2);assert.equal(it(s,'LIN-BT').stock.soiled,18);assert.equal(it(s,'LIN-BT').stock.rooms,r0);
    assert.equal(invTotal(it(s,'LIN-BT')),invTotal(it(seed(),'LIN-BT')));
  });
  test('[INV] laundry send → receive short: clean to store, damaged + missing to loss register, charge computed',()=>{
    let s=seed();s=execute(s,'housekeeper',{type:'INV_LAUNDRY_SEND',lines:[{item:'LIN-BS',qty:10},{item:'LIN-BT',qty:14}]});
    const b=invSlice(s).batches.at(-1);assert.equal(b.status,'Out');assert.equal(it(s,'LIN-BS').stock.soiled,0);assert.equal(it(s,'LIN-BS').stock.laundry,34);
    err(()=>execute(s,'housekeeper',{type:'INV_LAUNDRY_SEND',lines:[{item:'LIN-BS',qty:1}]}),/Only 0/);
    err(()=>execute(s,'housekeeper',{type:'INV_LAUNDRY_SEND',lines:[{item:'AMN-SP',qty:1}]}),/Only linen/);
    err(()=>execute(s,'housekeeper',{type:'INV_LAUNDRY_RECEIVE',id:b.id,lines:[{item:'LIN-BS',received:10,damaged:1},{item:'LIN-BT',received:14}]}),/cannot exceed/);
    err(()=>execute(s,'housekeeper',{type:'INV_LAUNDRY_RECEIVE',id:b.id,lines:[{item:'LIN-BS',received:10}]}),/every item/);
    const store0=it(s,'LIN-BS').stock.store,losses0=invSlice(s).losses.length;
    s=execute(s,'supervisor',{type:'INV_LAUNDRY_RECEIVE',id:b.id,lines:[{item:'LIN-BS',received:8,damaged:1},{item:'LIN-BT',received:14,damaged:0}],key:'lb-recv'});
    const after=invSlice(s).batches.find(x=>x.id===b.id);
    assert.equal(after.status,'Returned short');assert.equal(it(s,'LIN-BS').stock.store,store0+8);assert.equal(it(s,'LIN-BS').stock.laundry,24);
    assert.equal(invSlice(s).losses.length,losses0+2);
    assert.equal(after.charge,9*1800+14*1400);
    assert.deepEqual(execute(s,'supervisor',{type:'INV_LAUNDRY_RECEIVE',id:b.id,lines:[],key:'lb-recv'}),s); // idempotent key
    err(()=>execute(s,'supervisor',{type:'INV_LAUNDRY_RECEIVE',id:b.id,lines:[{item:'LIN-BS',received:9},{item:'LIN-BT',received:14}]}),/already/,'INVALID_TRANSITION');
  });
  test('[INV] fixture batch LB-S2 receives in full',()=>{
    let s=seed();const b=invSlice(s).batches.find(x=>x.id==='LB-S2');
    s=execute(s,'housekeeper',{type:'INV_LAUNDRY_RECEIVE',id:'LB-S2',lines:b.lines.map(l=>({item:l.item,received:l.sent,damaged:0}))});
    assert.equal(invSlice(s).batches.find(x=>x.id==='LB-S2').status,'Returned');
    assert.ok(invSlice(s).items.every(i=>(i.stock.laundry??0)===0));
  });
  test('[INV] loss needs reason (Other needs note), cannot exceed stock, records value',()=>{
    let s=seed();
    err(()=>execute(s,'housekeeper',{type:'INV_LOSS',item:'LIN-BR',loc:'store',qty:1,reason:'Because'}),/reason/);
    err(()=>execute(s,'housekeeper',{type:'INV_LOSS',item:'LIN-BR',loc:'store',qty:1,reason:'Other',note:''}),/Describe/);
    err(()=>execute(s,'housekeeper',{type:'INV_LOSS',item:'LIN-BR',loc:'store',qty:7,reason:'Missing'}),/Only 6/);
    err(()=>execute(s,'housekeeper',{type:'INV_LOSS',item:'LIN-BR',loc:'laundry',qty:1,reason:'Missing'}),/valid location/);
    s=execute(s,'housekeeper',{type:'INV_LOSS',item:'LIN-BR',loc:'rooms',qty:1,reason:'Taken by guest',note:'Room 106'});
    const l=invSlice(s).losses.at(-1);assert.equal(l.value,160000);assert.equal(it(s,'LIN-BR').stock.rooms,5);
  });
  test('[INV] count: match posts at once; variance needs another reviewer; approve adjusts by variance keeping later moves',()=>{
    let s=seed();
    // matching count of floor 1
    const f1=Object.fromEntries(invSlice(s).items.map(i=>[i.id,i.stock.floor1??0]));
    s=execute(s,'housekeeper',{type:'INV_COUNT_SUBMIT',loc:'floor1',counts:f1});assert.equal(invSlice(s).counts.at(-1).status,'Posted');
    // store count with variance: soap −10
    const st=Object.fromEntries(invSlice(s).items.map(i=>[i.id,i.stock.store??0]));st['AMN-SP']-=10;
    s=execute(s,'supervisor',{type:'INV_COUNT_SUBMIT',loc:'store',counts:st});const c=invSlice(s).counts.at(-1);assert.equal(c.status,'Submitted');
    err(()=>execute(s,'housekeeper',{type:'INV_COUNT_SUBMIT',loc:'store',counts:st}),/already awaiting/,'INVALID_TRANSITION');
    err(()=>execute(s,'supervisor',{type:'INV_COUNT_POST',id:c.id,approve:true,reason:'Own'}),/own count/,'FORBIDDEN');
    err(()=>execute(s,'manager',{type:'INV_COUNT_POST',id:c.id,approve:true,reason:''}),/reason/);
    s=execute(s,'housekeeper',{type:'INV_ISSUE',item:'AMN-SP',qty:20,from:'store',to:'floor1'}); // movement after count
    s=execute(s,'manager',{type:'INV_COUNT_POST',id:c.id,approve:true,reason:'Issued without record'});
    assert.equal(it(s,'AMN-SP').stock.store,260-20-10);
    assert.equal(invSlice(s).losses.at(-1).reason,'Count shortage');assert.equal(invSlice(s).losses.at(-1).qty,10);
    err(()=>execute(s,'manager',{type:'INV_COUNT_POST',id:c.id,approve:true,reason:'again'}),/already been decided/,'INVALID_TRANSITION');
    err(()=>execute(s,'housekeeper',{type:'INV_COUNT_SUBMIT',loc:'store',counts:{}}),/whole-number count/);
  });
  test('[INV] fixture count CNT-S2 (by Ravi) can be rejected by the supervisor with no stock change',()=>{
    let s=seed();const before=JSON.stringify(invSlice(s).items);
    s=execute(s,'supervisor',{type:'INV_COUNT_POST',id:'CNT-S2',approve:false,reason:'Recount after rounds'});
    assert.equal(invSlice(s).counts.find(c=>c.id==='CNT-S2').status,'Rejected');assert.equal(JSON.stringify(invSlice(s).items),before);
  });
  test('[INV] consumption per occupied room-night uses history + bookings and flags shampoo',()=>{
    const s=seed();assert.equal(invOccupied(s,'2026-10-23'),8);assert.equal(invOccupied(s,'2026-10-19'),15);assert.equal(invOccupied(s,'2026-10-30'),0);
    const c=invConsumption(s,'2026-10-17','2026-10-23');assert.equal(c.rn,12+14+15+11+9+10+8);
    const sh=c.rows.find(r=>r.item.id==='AMN-SH'),sp=c.rows.find(r=>r.item.id==='AMN-SP');
    assert.ok(sh.flag&&sh.variance>15);assert.ok(!sp.flag);
    let n=execute(s,'housekeeper',{type:'INV_USE',item:'AMN-SP',qty:5,floor:'floor1'});
    assert.equal(invConsumption(n,s.date,s.date).rows.find(r=>r.item.id==='AMN-SP').used,5);
  });
  test('[INV] item settings: par & minimum change alerts; validation',()=>{
    let s=seed();s=execute(s,'supervisor',{type:'INV_ITEM_SET',id:'LIN-BS',min:70,reorderQty:40,par:{Classic:3,Deluxe:3,Suite:3}});
    assert.ok(invAlerts(s).some(a=>a.item.id==='LIN-BS'));assert.equal(invParRequired(s,it(s,'LIN-BS')),216);
    err(()=>execute(s,'supervisor',{type:'INV_ITEM_SET',id:'AMN-SP',min:1,reorderQty:1,par:{Classic:1}}),/linen only/);
    err(()=>execute(s,'supervisor',{type:'INV_ITEM_SET',id:'AMN-SP',min:-1,reorderQty:1}),/whole numbers/);
  });
  test('[INV] closed business day blocks stock movements',()=>{
    const s=seed();s.closedDates.push(s.date);
    err(()=>execute(s,'housekeeper',{type:'INV_ISSUE',item:'LIN-BS',qty:1,from:'store',to:'floor1'}),null,'DAY_CLOSED');
  });
  test('[INV] commands on an old save without the slice start an empty module safely',()=>{
    const s=seed();delete s.inventoryX;err(()=>execute(s,'housekeeper',{type:'INV_ISSUE',item:'LIN-BS',qty:1,from:'store',to:'floor1'}),/Choose a stock item/,'NOT_FOUND');
    assert.equal(invClean({stock:{}}),0);
  });
}
