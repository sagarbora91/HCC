// Module 14 · Group & corporate — domain/command tests. Names prefixed [GRP].
import {tillClear} from './_till.mjs';
export function register({test,M,assert}){
  const {seed,execute}=M;
  const blk=(s,id)=>M.grpSlice(s).blocks.find(b=>b.id===id);
  const co=(s,id)=>M.grpSlice(s).companies.find(c=>c.id===id);
  const rows=(n,type='Classic')=>Array.from({length:n},(_,i)=>({guest:'Dealer Guest '+(i+1),type,people:1,phone:'+91 90000 1000'+i}));
  /** Close 24 Oct and open 25 Oct (same path as the core end-to-end test). */
  function nextDay(s){
    for(const b of s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival<=s.date)){s=execute(s,'owner',{type:'REGISTER',id:b.id});s=execute(s,'owner',{type:'CHECKIN',id:b.id});}
    for(const e of s.expenses.filter(e=>e.status==='Pending'))s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:e.id,approve:true});
    s=execute(s,'owner',{type:'NIGHT_POST'});s=execute(s,'owner',{type:'SHIFT_CLOSE',counted:M.drawer(s)});s=tillClear(M,s);s=execute(s,'owner',{type:'DAY_CLOSE'});
    return execute(s,'owner',{type:'NEXT_DAY'});
  }

  test('[GRP] module slice defaults to empty',()=>{const s=seed();delete s.groupsX;assert.deepEqual(M.grpSlice(s),{companies:[],blocks:[],requests:[],overrides:[]});});
  test('[GRP] fixture: fictional companies, 4 blocks, core figures unchanged',()=>{
    const s=seed(),x=M.grpSlice(s);
    assert.equal(x.companies.length,3);assert.equal(x.blocks.length,4);
    for(const c of x.companies){assert.match(c.name,/fictional/);assert.match(c.gstin,/^\d{2}ZZFIC\d{4}F/);assert.ok(M.GRP_GSTIN.test(c.gstin));}
    assert.equal(M.metrics(s).occupied,8);assert.equal(M.metrics(s).collections,2150000);assert.equal(M.drawer(s),1075000);
  });
  test('[GRP] pickup lines and company exposure figures',()=>{
    const s=seed(),g1=M.grpBlockTotals(s,blk(s,'GB-1'));
    assert.deepEqual([g1.allot,g1.picked,g1.released,g1.remaining],[4,2,2,0]);
    assert.equal(M.grpBookingExposure(s,s.bookings.find(b=>b.id==='HCC-1044')),460000);
    assert.equal(M.grpCredit(s,co(s,'CO-1')).exposure,7120000);
    const c2=M.grpCredit(s,co(s,'CO-2'));assert.equal(c2.exposure,8800000);assert.equal(c2.level,'near');
    const g2=M.grpBlockTotals(s,blk(s,'GB-2'));assert.deepEqual([g2.allot,g2.picked,g2.remaining],[9,0,9]);
  });
  test('[GRP] block creation validates dates, cut-off and inventory (incl. other blocks)',()=>{
    const s=seed();
    const base={type:'GRP_BLOCK_CREATE',name:'Rotary district meet',billTo:'guest',arrival:'2026-10-30',departure:'2026-11-01',cutoff:'2026-10-28'};
    assert.throws(()=>execute(s,'owner',{...base,cutoff:'2026-10-31',allot:{Classic:1}}),/cut-off/);
    assert.throws(()=>execute(s,'owner',{...base,arrival:'2026-10-20',allot:{Classic:1}}),/before the current business date/);
    assert.throws(()=>execute(s,'owner',{...base,allot:{}}),/at least one room/);
    // 11 sellable Classic rooms (308 blocked); GB-2 already holds 6 for these nights.
    assert.throws(()=>execute(s,'owner',{...base,allot:{Classic:6}}),e=>e.code==='NOT_AVAILABLE'&&/Only 5 Classic/.test(e.message));
    const n=execute(s,'reception',{...base,allot:{Classic:5}});
    const b=M.grpSlice(n).blocks.at(-1);assert.equal(b.status,'Open');assert.equal(b.rates.Classic,200000);
    assert.throws(()=>execute(s,'owner',{...base,billTo:'company',allot:{Classic:1}}),/company account/);
    const c=execute(s,'owner',{...base,billTo:'company',company:'CO-3',allot:{Suite:1}});assert.equal(M.grpSlice(c).blocks.at(-1).rates.Suite,380000);
  });
  test('[GRP] rooming list books real rooms at the block rate, billed to the company',()=>{
    const s0=seed(),before=s0.bookings.length;
    const s=execute(s0,'reception',{type:'GRP_ROOMING',block:'GB-2',rows:[...rows(1),{guest:'Deluxe Guest',type:'Deluxe',people:2,room:'303'}],key:'rl-1'});
    assert.equal(s.bookings.length,before+2);
    const made=s.bookings.slice(-2);
    assert.ok(made.every(b=>b.status==='Confirmed'&&b.arrival==='2026-10-30'&&b.departure==='2026-11-01'&&b.company?.name==='Godavari Agro Inputs (fictional)'));
    assert.deepEqual(made.map(b=>b.rate),[180000,270000]);assert.equal(made[1].room,'303');
    assert.equal(new Set(made.map(b=>b.room)).size,2);
    const t=M.grpBlockTotals(s,blk(s,'GB-2'));assert.deepEqual([t.picked,t.remaining],[2,7]);
    assert.equal(M.grpCredit(s,co(s,'CO-2')).exposure,8800000+M.grpStayValue(s,180000,'2026-10-30','2026-11-01')+M.grpStayValue(s,270000,'2026-10-30','2026-11-01'));
    assert.deepEqual(execute(s,'reception',{type:'GRP_ROOMING',block:'GB-2',rows:rows(2),key:'rl-1'}),s,'idempotent key');
    assert.equal(M.grpMasterFolio(s,blk(s,'GB-2')).rows.length,2);
  });
  test('[GRP] rooming list is atomic and respects allotment, type and availability',()=>{
    const s=seed(),copy=JSON.stringify(s);
    assert.throws(()=>execute(s,'owner',{type:'GRP_ROOMING',block:'GB-3',rows:[{guest:'Kulkarni Family',type:'Classic',people:2},{guest:'Joshi Family',type:'Classic',people:9}]}),/Row 2/);
    assert.equal(JSON.stringify(s),copy);
    assert.throws(()=>execute(s,'owner',{type:'GRP_ROOMING',block:'GB-3',rows:[{guest:'Suite One',type:'Suite',people:2},{guest:'Suite Two',type:'Suite',people:2}]}),e=>e.code==='NOT_AVAILABLE'&&/no Suite rooms left/.test(e.message));
    assert.throws(()=>execute(s,'owner',{type:'GRP_ROOMING',block:'GB-3',rows:[{guest:'Deluxe Person',type:'Deluxe',people:1}]}),/not part of this block/);
    assert.throws(()=>execute(s,'owner',{type:'GRP_ROOMING',block:'GB-3',rows:[{guest:'Wrong Room',type:'Classic',people:1,room:'306'}]}),/not a Classic room/);
    // GB-4 starts tomorrow; 104 (Deluxe) is occupied until 26 Oct → BOOK path rejects.
    assert.throws(()=>execute(s,'owner',{type:'GRP_ROOMING',block:'GB-4',rows:[{guest:'Site Engineer',type:'Deluxe',people:1,room:'104'}]}),e=>e.code==='NOT_AVAILABLE');
    // Guest-billed block: no company on bookings.
    const n=execute(s,'owner',{type:'GRP_ROOMING',block:'GB-3',rows:[{guest:'Kulkarni Family',type:'Suite',people:2}]});
    assert.equal(n.bookings.at(-1).company,undefined);assert.equal(n.bookings.at(-1).rate,420000);
  });
  test('[GRP] credit limit: refusal, approver override with reason, reception cannot override',()=>{
    const s=seed();
    assert.ok(execute(s,'reception',{type:'GRP_ROOMING',block:'GB-2',rows:rows(3)}));
    assert.throws(()=>execute(s,'reception',{type:'GRP_ROOMING',block:'GB-2',rows:rows(4)}),/Credit limit exceeded/);
    assert.throws(()=>execute(s,'reception',{type:'GRP_ROOMING',block:'GB-2',rows:rows(4),override:true,overrideReason:'Known client'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>execute(s,'manager',{type:'GRP_ROOMING',block:'GB-2',rows:rows(4),override:true}),/reason/);
    const n=execute(s,'manager',{type:'GRP_ROOMING',block:'GB-2',rows:rows(4),override:true,overrideReason:'Advance cheque received (simulated)'});
    assert.equal(M.grpSlice(n).overrides.length,1);assert.equal(M.grpCredit(n,co(n,'CO-2')).level,'over');assert.match(n.audit[0].detail,/overridden/);
  });
  test('[GRP] credit-limit change: admin sets, reception requests, no self-approval',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'manager',{type:'GRP_LIMIT_SET',company:'CO-3',limit:25000000}),/reason/);
    s=execute(s,'manager',{type:'GRP_LIMIT_SET',company:'CO-3',limit:25000000,reason:'Annual contract renewal'});
    const r=M.grpSlice(s).requests.at(-1);assert.equal(r.status,'Pending');assert.equal(co(s,'CO-3').creditLimit,20000000);
    assert.throws(()=>execute(s,'manager',{type:'GRP_LIMIT_DECIDE',id:r.id,approve:true}),/own request/);
    assert.throws(()=>execute(s,'reception',{type:'GRP_LIMIT_DECIDE',id:r.id,approve:true}),e=>e.code==='FORBIDDEN');
    s=execute(s,'owner',{type:'GRP_LIMIT_DECIDE',id:r.id,approve:true});assert.equal(co(s,'CO-3').creditLimit,25000000);
    assert.throws(()=>execute(s,'owner',{type:'GRP_LIMIT_DECIDE',id:r.id,approve:false}),/no longer pending/);
    s=execute(s,'owner',{type:'GRP_LIMIT_SET',company:'CO-2',limit:12000000});
    assert.equal(co(s,'CO-2').creditLimit,12000000);assert.equal(M.grpSlice(s).requests.find(x=>x.id==='GLR-1').status,'Rejected');
    assert.throws(()=>execute(s,'reception',{type:'GRP_LIMIT_SET',company:'CO-2',limit:-1,reason:'x'}),/credit limit between/);
  });
  test('[GRP] housekeeping and accountant roles cannot use group commands',()=>{
    const s=seed();
    for(const u of ['housekeeper','maint','accountant'])assert.throws(()=>execute(s,u,{type:'GRP_ROOMING',block:'GB-2',rows:rows(1)}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>execute(s,'housekeeper',{type:'GRP_COMPANY_SAVE',name:'Test Co'}),e=>e.code==='FORBIDDEN');
  });
  test('[GRP] company save validates GSTIN shape and duplicate names; new limit via request',()=>{
    const s=seed();
    assert.throws(()=>execute(s,'reception',{type:'GRP_COMPANY_SAVE',name:'Manjra Textiles (fictional)',gstin:'12345'}),/GSTIN/);
    assert.throws(()=>execute(s,'reception',{type:'GRP_COMPANY_SAVE',name:'deccan pharma distributors (fictional)'}),/already exists/);
    const n=execute(s,'reception',{type:'GRP_COMPANY_SAVE',name:'Manjra Textiles (fictional)',gstin:'27zzfic0009f1z5',rates:{Classic:185000},creditLimit:5000000});
    const c=M.grpSlice(n).companies.at(-1);assert.equal(c.gstin,'27ZZFIC0009F1Z5');assert.equal(c.creditLimit,0);assert.equal(M.grpSlice(n).requests.at(-1).to,5000000);
    const o=execute(s,'owner',{type:'GRP_COMPANY_SAVE',name:'Manjra Textiles (fictional)',creditLimit:5000000});assert.equal(M.grpSlice(o).companies.at(-1).creditLimit,5000000);
  });
  test('[GRP] allotment change cannot go below pickup; manual release closes pickup',()=>{
    let s=execute(seed(),'owner',{type:'GRP_ROOMING',block:'GB-2',rows:rows(2)});
    assert.throws(()=>execute(s,'owner',{type:'GRP_BLOCK_ALLOT',id:'GB-2',allot:{Classic:1}}),/already picked up/);
    assert.throws(()=>execute(s,'owner',{type:'GRP_BLOCK_ALLOT',id:'GB-2',allot:{Classic:12}}),e=>e.code==='NOT_AVAILABLE');
    s=execute(s,'owner',{type:'GRP_BLOCK_ALLOT',id:'GB-2',allot:{Classic:4,Suite:1}});assert.equal(blk(s,'GB-2').rates.Suite,450000);
    s=execute(s,'owner',{type:'GRP_RELEASE',id:'GB-2',roomType:'Classic',count:1});
    assert.deepEqual(M.grpLines(s,blk(s,'GB-2')).find(l=>l.type==='Classic'),{...M.grpLines(s,blk(s,'GB-2')).find(l=>l.type==='Classic'),allot:4,picked:2,released:1,remaining:1});
    s=execute(s,'owner',{type:'GRP_RELEASE',id:'GB-2'});assert.equal(blk(s,'GB-2').status,'Released');
    assert.throws(()=>execute(s,'owner',{type:'GRP_ROOMING',block:'GB-2',rows:rows(1)}),/Pickup is closed/);
    assert.throws(()=>execute(s,'owner',{type:'GRP_BLOCK_CANCEL',id:'GB-2',reason:'Event postponed'}),/member booking/);
  });
  test('[GRP] auto-release after the business date passes the cut-off (NEXT_DAY)',()=>{
    let s=seed();
    assert.equal(M.grpStatus(s,blk(s,'GB-3')),'Open');
    s=nextDay(s);assert.equal(s.date,'2026-10-25');
    // Derived before the sweep persists it.
    assert.equal(M.grpStatus(s,blk(s,'GB-3')),'Released');assert.equal(M.grpBlockTotals(s,blk(s,'GB-3')).released,6);
    assert.throws(()=>execute(s,'owner',{type:'GRP_ROOMING',block:'GB-3',rows:[{guest:'Late Guest',type:'Classic',people:1}]}),/released at the cut-off/);
    s=execute(s,'reception',{type:'GRP_SWEEP'});
    assert.equal(blk(s,'GB-3').status,'Released');assert.equal(blk(s,'GB-3').releasedBy,'auto');assert.equal(blk(s,'GB-2').status,'Open');assert.equal(blk(s,'GB-4').status,'Open');
    const again=execute(s,'reception',{type:'GRP_SWEEP'});assert.deepEqual(M.grpSlice(again).blocks,M.grpSlice(s).blocks);
  });
  test('[GRP] cancelled member gives the room back; master folio totals aggregate members',()=>{
    let s=execute(seed(),'owner',{type:'GRP_ROOMING',block:'GB-2',rows:rows(2)});
    const id=s.bookings.at(-1).id;s=execute(s,'owner',{type:'CANCEL',id,reason:'Dealer not travelling'});
    assert.equal(M.grpBlockTotals(s,blk(s,'GB-2')).picked,1);
    const f=M.grpMasterFolio(seed(),blk(seed(),'GB-1'));
    assert.equal(f.rows.length,2);assert.equal(f.totals.paid,400000);assert.equal(f.totals.estimate,920000);
    assert.equal(f.totals.total,f.rows.reduce((a,r)=>a+r.total,0));
  });
  test('[GRP] day-closed rule blocks group writes',()=>{
    const s=seed();s.closedDates.push(s.date);
    assert.throws(()=>execute(s,'owner',{type:'GRP_BLOCK_CREATE',name:'Late group',billTo:'guest',arrival:'2026-11-10',departure:'2026-11-11',cutoff:'2026-11-01',allot:{Classic:1}}),e=>e.code==='DAY_CLOSED');
  });
}
