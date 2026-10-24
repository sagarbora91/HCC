// A2.3 · Shared till (DEC-052, W0-B): restaurant / room-service cash counts once in the front-desk drawer. Names prefixed [TILL].
import {tillClear} from './_till.mjs';
export function register({test,M,assert}){
  const {execute,seed,loadScenario,drawer,totals,HccError}=M;
  const code=fn=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const P=s=>M.posSlice(s);
  /** A table check (dal + 2 roti = ₹360 + ₹36 tax = ₹396) sent to the kitchen. */
  const table=(s=seed(),tbl='T3')=>{
    s=execute(s,'cashier',{type:'POS_ORDER_OPEN',outlet:'restaurant',table:tbl,covers:2});const id=P(s).orders.at(-1).id;
    s=execute(s,'cashier',{type:'POS_ITEM_ADD',id,item:'m-dal',qty:1});s=execute(s,'cashier',{type:'POS_ITEM_ADD',id,item:'m-roti',qty:2});
    s=execute(s,'cashier',{type:'POS_KOT_SEND',id});return [s,id];
  };
  const sameFormula=s=>{const x=M.shiftExpected(s);assert.equal(x.expected,drawer(s));assert.equal(x.consistent,true);
    assert.equal(x.expected,s.openingCash+M.shiftMovements(s).reduce((a,m)=>a+m.amount,0));return x;};

  test('[TILL] BASE: drawer ₹10,750 = ₹10,200 folio/expense cash + ₹550 restaurant cash (POS-F01); UPI POS-F02 excluded',()=>{
    const s=seed(),x=sameFormula(s);
    assert.equal(drawer(s),1075000);assert.equal(x.restaurant,55000);
    const pos=M.shiftMovements(s).filter(m=>m.order);
    assert.deepEqual(pos.map(m=>[m.id,m.kind,m.label,m.amount]),[['POSR-F01','pos-sale','Restaurant · POS/2610/0001',55000]]);
  });
  test('[TILL] cash POS sale enters the drawer once',()=>{
    let [s,id]=table();const before=drawer(s);
    s=execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Cash'});
    assert.equal(drawer(s),before+39600);
    const mv=M.shiftMovements(s).filter(m=>m.order===id);assert.equal(mv.length,1);assert.equal(mv[0].amount,39600);assert.match(mv[0].label,/^Restaurant · POS\/2610\/0003$/);
    sameFormula(s);
  });
  test('[TILL] UPI / card POS sale is non-cash: drawer unchanged, shown in outlet receipts by method',()=>{
    let [s,id]=table();const before=drawer(s);
    s=execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'UPI',reference:'DEMO-UPI-T1'});
    let [t,id2]=table(s,'T5');t=execute(t,'cashier',{type:'POS_SETTLE',id:id2,mode:'outlet',method:'Card',reference:'DEMO-CARD-T1'});
    assert.equal(drawer(t),before);assert.equal(M.shiftMovements(t).filter(m=>m.order).length,1);
    const o=M.closingOutlet(t,t.date);assert.deepEqual(o.byMethod,{Cash:55000,UPI:74800+39600,Card:39600});assert.equal(o.net,55000+74800+39600*2);
  });
  test('[TILL] room service charged to a folio adds a folio charge and no cash',()=>{
    let s=seed();const before=drawer(s),o=P(s).orders.find(x=>x.id==='POS-F04'),b0=totals(s.bookings.find(b=>b.id===o.booking));
    s=execute(s,'cashier',{type:'POS_SETTLE',id:'POS-F04',mode:'folio'});
    const b1=totals(s.bookings.find(b=>b.id===o.booking));
    assert.equal(b1.net-b0.net,30000);assert.equal(b1.tax-b0.tax,3000); // paratha ₹240 + chai ₹60, synthetic 10%
    assert.equal(drawer(s),before);assert.equal(P(s).receipts.filter(r=>r.order==='POS-F04').length,0);
    assert.equal(M.shiftMovements(s).some(m=>m.order==='POS-F04'),false);
    assert.equal(M.closingOutlet(s,s.date).roomFolio,33000);
    assert.equal(M.accLiveVouchers(s,s.date).some(v=>v.source.kind==='pos'&&v.narration.includes('POS-F04')),false);
  });
  test('[TILL] same-day void of a cash bill nets to zero in the drawer',()=>{
    let [s,id]=table();const before=drawer(s);
    s=execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Cash'});
    s=execute(s,'cashier',{type:'POS_VOID_BILL',id,reason:'Wrong table billed'});
    assert.equal(drawer(s),before);
    const mv=M.shiftMovements(s).filter(m=>m.order===id);
    assert.deepEqual(mv.map(m=>[m.kind,m.amount]),[['pos-sale',39600],['pos-void',-39600]]);assert.match(mv[1].label,/^Restaurant void · /);
    sameFormula(s);
    const acc=M.accLiveVouchers(s,s.date).filter(v=>v.source.kind==='pos'&&v.narration.includes(id));
    assert.equal(acc.length,2);assert.ok(acc.every(M.accBalanced));
  });
  test('[TILL] POS cash is rejected after the shift closes (sale and cash void); UPI still allowed',()=>{
    let [s,id]=table();
    let c=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)});
    assert.equal(code(()=>execute(c,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Cash'})),'INVALID_TRANSITION');
    c=execute(c,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'UPI',reference:'DEMO-UPI-LATE'});
    assert.equal(drawer(c),c.shiftClose.expected);
    s=execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Cash'});
    s=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)});
    assert.equal(code(()=>execute(s,'cashier',{type:'POS_VOID_BILL',id,reason:'Too late'})),'INVALID_TRANSITION');
  });
  test('[TILL] replay with the same key changes nothing',()=>{
    let [s,id]=table();
    s=execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Cash',key:'till-1'});
    const again=execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Cash',key:'till-1'});
    assert.deepEqual(again,s);assert.equal(M.shiftMovements(again).filter(m=>m.order===id).length,1);
  });
  test('[TILL] an open POS check blocks DAY_CLOSE (preflight item + command); settling it lets the day close',()=>{
    let s=seed();
    for(const b of s.bookings.filter(b=>b.status==='Confirmed')){s=execute(s,'owner',{type:'REGISTER',id:b.id});s=execute(s,'owner',{type:'CHECKIN',id:b.id});}
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});s=execute(s,'owner',{type:'NIGHT_POST'});
    s=execute(s,'owner',{type:'SHIFT_CLOSE',counted:drawer(s)});
    const pf=M.closingPreflight(s).filter(x=>x.items.length);
    assert.deepEqual(pf.map(x=>x.id),['pos']);assert.equal(pf[0].page,'pos');assert.equal(pf[0].items.length,2);
    assert.throws(()=>execute(s,'owner',{type:'DAY_CLOSE'}),e=>e.code==='INVALID_TRANSITION'&&/POS-F03, POS-F04/.test(e.message));
    s=tillClear(M,s);s=execute(s,'owner',{type:'DAY_CLOSE'});
    const rec=s.closings[s.date];
    assert.equal(rec.cash.restaurant,55000);assert.equal(rec.cash.expected,1075000);assert.deepEqual(rec.outlet.byMethod,{Cash:55000,UPI:74800+191400+33000,Card:0});
    for(const c of M.accReconcile(s,s.date))assert.ok(c.ok,`${c.id} report ${c.report} ledger ${c.ledger}`);
  });
  // FY26: built once for the history checks below.
  const fy=loadScenario('FY26');
  const dayState=(d,rec)=>{
    const views=fy.bookings.map(b=>({...b,lines:b.lines.filter(l=>l.date<=d),receipts:b.receipts.filter(r=>r.date<=d)}));
    const a=fy.approvals.find(x=>x.kind==='cash-difference'&&x.target===d&&x.id.startsWith('APR-HC'));
    return {...fy,date:d,clock:d+'T20:00',openingCash:rec.cash.opening,shiftOpen:true,shiftClose:undefined,closeProgress:undefined,closings:undefined,bookings:views,
      shift:a?{date:d,status:'Closed',expected:a.meta.expected,counted:a.meta.counted,reason:a.reason,submittedBy:'cashier',approval:a.id,decidedBy:'manager'}:undefined};
  };
  test('[TILL] drawer === shiftExpected().expected on BASE and on FY26 23 Oct (restaurant cash included)',()=>{
    sameFormula(seed());sameFormula(fy);assert.equal(drawer(fy),1075000);
    const d='2026-10-23',rec=fy.closings[d],s=dayState(d,rec),x=sameFormula(s);
    const cash=P(fy).receipts.filter(r=>r.date===d&&r.method==='Cash').reduce((a,r)=>a+r.amount,0);
    assert.ok(cash>0);assert.equal(x.restaurant,cash);assert.equal(rec.cash.restaurant,cash);assert.equal(x.expected,rec.cash.expected);
  });
  test('[TILL] FY26 closing records equal a full recompute; float restored by banking; every day has restaurant cash counted',()=>{
    const days=Object.keys(fy.closings).filter(d=>d<fy.date).sort();assert.equal(days.length,206);
    const sample=days.filter((d,i)=>i%15===0||i===days.length-1);
    for(const d of sample){
      const rec=fy.closings[d],s=dayState(d,rec),ex=drawer(s);
      const re=M.closingSummary({...s,clock:d+'T23:30',shiftOpen:false,shiftClose:{expected:ex,counted:s.shift?.counted??ex}},'manager');
      re.nightAudit={stays:re.occupancy.occupied,resumed:false};re.cash.banked=rec.cash.banked;
      assert.deepEqual(re,rec,d);
    }
    // Cash above the ₹5,000 float is banked after each count; the next morning opens with what stayed (₹5,000 unless the drawer was lower).
    days.forEach((d,i)=>{const c=fy.closings[d];
      assert.equal(c.cash.banked,Math.max(0,c.cash.counted-500000),d);
      assert.equal(c.cash.opening,i?fy.closings[days[i-1]].cash.counted-fy.closings[days[i-1]].cash.banked:500000,d);assert.ok(c.cash.opening<=500000,d);
      assert.equal(c.cash.restaurant,P(fy).receipts.filter(r=>r.date===d&&r.method==='Cash').reduce((a,r)=>a+r.amount,0),d);});
    assert.ok(days.filter(d=>fy.closings[d].cash.restaurant>0).length>150);
    assert.ok(days.filter(d=>fy.closings[d].cash.opening===500000).length>=190);
    assert.equal(fy.closings[fy.date]?.cash,undefined);assert.equal(fy.openingCash,500000);
  });
}
