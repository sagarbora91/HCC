// F12 MEAS · cap-desk (turn-away, walked, allowance reasons, booking extras, preferences). Names prefixed [F12-CAP-DESK].
export function register({test,M,assert}){
  const {seed,execute,totals}=M;
  const TRN={type:'MEAS_TURNAWAY_LOG',reason:'full',arrival:'2026-10-31',nights:2,roomType:'Classic',rooms:1,quotedRate:200000,channel:'Walk-in'};
  const off=(s,key)=>{const c=structuredClone(s);c.configX={...(c.configX??{}),values:{...(c.configX?.values??{}),[key]:false}};return c;};
  const inhouse=s=>s.bookings.find(b=>b.status==='In house');
  /** In-house booking whose assigned room type differs from the booked type (booked type recorded first). */
  function upgraded(){
    let s=seed();const b=inhouse(s);const bookedType=s.rooms.find(x=>x.id===b.room).type;const other=s.rooms.find(r=>r.type!==bookedType);
    s=execute(s,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id:b.id,bookedType});
    s=structuredClone(s);s.bookings.find(x=>x.id===b.id).room=other.id;
    return [s,b.id,bookedType,other.type];
  }

  test('[F12-CAP-DESK] turn-away: logged with lead days, audit text and idempotent on the key',()=>{
    const s=execute(seed(),'reception',{...TRN,key:'trn-1'});const t=s.measX.turnaway;
    assert.equal(t.length,1);assert.equal(t[0].leadDays,M.nights(s.date,'2026-10-31'));assert.equal(t[0].by,'reception');assert.equal(t[0].roomType,'Classic');assert.equal(t[0].quotedRate,200000);
    assert.deepEqual(execute(s,'reception',{...TRN,key:'trn-1'}),s);
    assert.equal(execute(s,'manager',TRN).measX.turnaway.length,2);
  });
  test('[F12-CAP-DESK] turn-away: validation (reason, dates, nights, other needs a note)',()=>{
    const s=seed();
    assert.throws(()=>execute(s,'reception',{...TRN,reason:'nope'}),/why the guest/);
    assert.throws(()=>execute(s,'reception',{...TRN,arrival:'31-10'}),/arrival date/);
    assert.throws(()=>execute(s,'reception',{...TRN,nights:0}),/Nights/);
    assert.throws(()=>execute(s,'reception',{...TRN,quotedRate:1.5}),/paise/);
    assert.throws(()=>execute(s,'reception',{...TRN,reason:'other'}),/note/);
    assert.equal(execute(s,'reception',{...TRN,reason:'other',note:'Wanted a pool'}).measX.turnaway[0].note,'Wanted a pool');
  });
  test('[F12-CAP-DESK] turn-away: Cashier, Accountant, Housekeeper refused; Reception, Manager, Owner allowed',()=>{
    const s=seed();
    for(const u of ['cashier','accountant','housekeeper','maintenance'])assert.throws(()=>execute(s,u,TRN),e=>e.code==='FORBIDDEN',u);
    for(const u of ['reception','manager','owner'])assert.equal(execute(s,u,TRN).measX.turnaway.length,1);
  });
  test('[F12-CAP-DESK] every desk command refuses MEAS_OFF while its switch is off, and keeps the data',()=>{
    const s0=seed();const b=inhouse(s0);
    const cases=[['meas.daily.regrets',TRN,'reception'],['meas.rev.overbooking',{type:'MEAS_WALKED_LOG',booking:b.id,night:s0.date,reason:'Overbooked'},'manager'],
      ['meas.daily.adjustments',{type:'MEAS_ALLOWANCE_REASON_SAVE',code:'XYZ',label:'Test'},'owner'],['meas.guest.upsell',{type:'MEAS_BOOKING_EXTRA_SET',id:b.id,bookedType:'Classic'},'reception'],
      ['meas.guest.preferences',{type:'MEAS_PREFERENCE_SET',customer:M.customerIndex(s0)[0].key,floor:'Top'},'reception']];
    for(const [key,cmd,u] of cases){
      assert.throws(()=>execute(off(s0,key),u,cmd),e=>e.code==='FORBIDDEN'&&/MEAS_OFF/.test(e.message)&&e.details?.reason==='MEAS_OFF'||/MEAS_OFF/.test(e.message),key);
      assert.ok(execute(s0,u,cmd),'on: '+key);
    }
    const s=execute(s0,'reception',TRN);const c=off(s,'meas.daily.regrets');assert.equal(c.measX.turnaway.length,1);
    assert.throws(()=>execute(c,'reception',TRN),/MEAS_OFF/);assert.equal(c.measX.turnaway.length,1);
  });
  test('[F12-CAP-DESK] walked guest: Manager / Owner only, booking must exist, reason and paise cost validated',()=>{
    const s=seed();const b=inhouse(s);const cmd={type:'MEAS_WALKED_LOG',booking:b.id,night:s.date,reason:'Overbooked, sent to partner hotel',costPaise:150000};
    for(const u of ['reception','cashier','accountant'])assert.throws(()=>execute(s,u,cmd),e=>e.code==='FORBIDDEN',u);
    const r=execute(s,'manager',cmd);assert.equal(r.measX.walked[0].costPaise,150000);assert.equal(r.measX.walked[0].approvedBy,'manager');
    assert.throws(()=>execute(s,'manager',{...cmd,booking:'HCC-NOPE'}),e=>e.code==='NOT_FOUND');
    assert.throws(()=>execute(s,'manager',{...cmd,reason:'x'}),/why the guest was walked/);
    assert.throws(()=>execute(s,'manager',{...cmd,costPaise:-5}),/paise/);
  });
  test('[F12-CAP-DESK] allowance reasons: defaults, Owner adds / retires, others refused, code normalised, no-op refused',()=>{
    const s=seed();assert.ok(M.mdAllowanceReasons(s).length>=4);
    assert.throws(()=>execute(s,'manager',{type:'MEAS_ALLOWANCE_REASON_SAVE',code:'abc',label:'Test'}),e=>e.code==='FORBIDDEN');
    let r=execute(s,'owner',{type:'MEAS_ALLOWANCE_REASON_SAVE',code:'vip guest',label:'VIP guest'});assert.ok(r.measX.allowanceReasons.some(x=>x.code==='VIP-GUEST'&&x.active));
    r=execute(r,'owner',{type:'MEAS_ALLOWANCE_REASON_SAVE',code:'VIP-GUEST',label:'VIP guest',active:false});assert.equal(r.measX.allowanceReasons.find(x=>x.code==='VIP-GUEST').active,false);
    assert.throws(()=>execute(r,'owner',{type:'MEAS_ALLOWANCE_REASON_SAVE',code:'VIP-GUEST',label:'VIP guest',active:false}),/No change/);
    assert.throws(()=>execute(s,'owner',{type:'MEAS_ALLOWANCE_REASON_SAVE',code:'A',label:'x'}),/short code/);
  });
  test('[F12-CAP-DESK] booked type is kept when the assigned type differs; the difference is visible',()=>{
    const [s,id,booked,now]=upgraded();const b=s.bookings.find(x=>x.id===id);
    assert.equal(M.mdBookedType(s,b),booked);assert.equal(M.mdTypeDiffers(s,b),true);assert.notEqual(booked,now);
    assert.throws(()=>execute(s,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id,bookedType:now}),/already recorded/);
  });
  test('[F12-CAP-DESK] paid upgrade posts UPG through the folio charge (GST from the master); free upgrade needs a Manager / Owner approver',()=>{
    const [s,id]=upgraded();const before=totals(s.bookings.find(x=>x.id===id));
    const r=execute(s,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id,item:'UPG',upgrade:'paid',amount:50000});
    const line=r.bookings.find(x=>x.id===id).lines.at(-1);assert.match(line.label,/^UPG/);assert.equal(line.amount,50000);assert.equal(line.taxBps,1800);assert.equal(r.measX.bookingExtra[id].upgrade,'paid');
    assert.equal(totals(r.bookings.find(x=>x.id===id)).net,before.net+50000);
    assert.throws(()=>execute(s,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id,item:'UPG',upgrade:'paid'}),/paise/);
    assert.throws(()=>execute(s,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id,item:'UPG',upgrade:'free'}),/approver/);
    assert.throws(()=>execute(s,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id,item:'UPG',upgrade:'free',approver:'cashier'}),/approver/);
    const f=execute(s,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id,item:'UPG',upgrade:'free',approver:'manager'});assert.equal(f.measX.bookingExtra[id].upgradeApprover,'manager');
    assert.deepEqual(totals(f.bookings.find(x=>x.id===id)),before);
    assert.equal(execute(s,'manager',{type:'MEAS_BOOKING_EXTRA_SET',id,item:'UPG',upgrade:'free'}).measX.bookingExtra[id].upgradeApprover,'manager');
  });
  test('[F12-CAP-DESK] UPG refused when the assigned type equals the booked type',()=>{
    const s=seed();const b=inhouse(s);
    assert.throws(()=>execute(s,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id:b.id,item:'UPG',upgrade:'free',approver:'manager'}),/nothing to upgrade/);
  });
  test('[F12-CAP-DESK] late check-out: LCO1 Rs 300 from 14:00, LCO2 Rs 600 from 18:00 (pre-tax paise, GST 18 %), wrong band refused',()=>{
    assert.equal(M.mdLateCheckoutItem('13:59'),undefined);assert.equal(M.mdLateCheckoutItem('14:00'),'LCO1');assert.equal(M.mdLateCheckoutItem('17:59'),'LCO1');assert.equal(M.mdLateCheckoutItem('18:00'),'LCO2');
    const s=seed();const id=inhouse(s).id;
    const a=execute(s,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id,item:'LCO1',time:'15:30'});const l1=a.bookings.find(x=>x.id===id).lines.at(-1);assert.equal(l1.amount,30000);assert.match(l1.label,/^LCO1/);assert.equal(l1.taxBps,1800);
    const b=execute(s,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id,item:'LCO2',time:'19:00'});assert.equal(b.bookings.find(x=>x.id===id).lines.at(-1).amount,60000);
    assert.throws(()=>execute(s,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id,item:'LCO2',time:'15:00'}),/LCO2 applies from 18:00/);
    assert.throws(()=>execute(s,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id,item:'LCO1',time:'12:00'}),/LCO1 applies/);
    assert.throws(()=>execute(s,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id,item:'XXX'}),/Unknown upsell/);
  });
  test('[F12-CAP-DESK] late check-out waiver needs a Manager and a reason; nothing is posted',()=>{
    const s=seed();const id=inhouse(s).id;const cmd={type:'MEAS_BOOKING_EXTRA_SET',id,item:'LCO1',time:'15:00',waive:true,reason:'Guest is a VIP'};
    assert.throws(()=>execute(s,'reception',cmd),/Manager/);
    assert.throws(()=>execute(s,'manager',{...cmd,reason:''}),/reason/);
    const r=execute(s,'manager',cmd);assert.equal(r.bookings.find(x=>x.id===id).lines.length,s.bookings.find(x=>x.id===id).lines.length);
  });
  test('[F12-CAP-DESK] extras are refused for Cashier and on a departed booking; unknown booking not found',()=>{
    const s=seed();const id=inhouse(s).id;
    assert.throws(()=>execute(s,'cashier',{type:'MEAS_BOOKING_EXTRA_SET',id,item:'LCO1',time:'15:00'}),e=>e.code==='FORBIDDEN');
    const d=structuredClone(s);d.bookings.find(x=>x.id===id).status='Departed';
    assert.throws(()=>execute(d,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id,item:'LCO1',time:'15:00'}),/no longer open/);
    assert.throws(()=>execute(s,'reception',{type:'MEAS_BOOKING_EXTRA_SET',id:'HCC-NOPE',item:'LCO1',time:'15:00'}),e=>e.code==='NOT_FOUND');
  });
  test('[F12-CAP-DESK] preferences: saved by customer key, trimmed, cleared by empty text, unknown customer refused',()=>{
    const s=seed();const c=M.customerIndex(s)[0];
    let r=execute(s,'reception',{type:'MEAS_PREFERENCE_SET',customer:c.key,floor:' Top ',pillow:'Firm',doNotDisturb:true});
    assert.deepEqual(r.measX.preference[c.key],{floor:'Top',pillow:'Firm',doNotDisturb:true});
    r=execute(r,'reception',{type:'MEAS_PREFERENCE_SET',customer:c.key,pillow:'',doNotDisturb:false});assert.deepEqual(r.measX.preference[c.key],{floor:'Top'});
    assert.throws(()=>execute(s,'reception',{type:'MEAS_PREFERENCE_SET',customer:'zz'}),e=>e.code==='NOT_FOUND');
    assert.throws(()=>execute(s,'reception',{type:'MEAS_PREFERENCE_SET',customer:c.key}),/Nothing to record/);
    assert.throws(()=>execute(s,'reception',{type:'MEAS_PREFERENCE_SET',customer:c.key,floor:'x'.repeat(81)}),/80 letters/);
    assert.throws(()=>execute(s,'cashier',{type:'MEAS_PREFERENCE_SET',customer:c.key,floor:'Top'}),e=>e.code==='FORBIDDEN');
  });
  test('[F12-CAP-DESK] blacklist: reason + Owner approval; Reception refused; Manager needs the Owner as approver; clear needs a reason',()=>{
    const s=seed();const c=M.customerIndex(s)[0];const cmd={type:'MEAS_PREFERENCE_SET',customer:c.key,blacklist:'Damaged room, refused to pay'};
    assert.throws(()=>execute(s,'reception',cmd),e=>e.code==='FORBIDDEN');
    assert.throws(()=>execute(s,'manager',cmd),/Owner/);
    assert.throws(()=>execute(s,'manager',{...cmd,approvedBy:'manager'}),/Owner/);
    assert.throws(()=>execute(s,'manager',{...cmd,blacklist:'x'}),/reason/);
    let r=execute(s,'manager',{...cmd,approvedBy:'owner'});assert.equal(r.measX.preference[c.key].blacklist.approvedBy,'owner');assert.equal(r.measX.preference[c.key].blacklist.at,s.clock);
    assert.equal(execute(s,'owner',cmd).measX.preference[c.key].blacklist.approvedBy,'owner');
    assert.throws(()=>execute(r,'manager',{type:'MEAS_PREFERENCE_SET',customer:c.key,blacklist:null}),/why the blacklist is cleared/);
    r=execute(r,'manager',{type:'MEAS_PREFERENCE_SET',customer:c.key,blacklist:null,reason:'Settled the bill'});assert.equal(r.measX.preference[c.key].blacklist,undefined);
    assert.throws(()=>execute(r,'owner',{type:'MEAS_PREFERENCE_SET',customer:c.key,blacklist:null,reason:'again'}),/not blacklisted/);
  });
  test('[F12-CAP-DESK] known-guest badge: first-time guest has none; blacklist flag shows',()=>{
    const s=seed();
    const nb=execute(s,'owner',{type:'BOOK',guest:'Brand New Visitor',room:'105',arrival:s.date,departure:M.addDays(s.date,1),people:1});
    assert.equal(M.mdKnownGuest(nb,nb.bookings.at(-1)),undefined);
    const ck=M.customerIndex(nb).find(x=>x.name==='Brand New Visitor').key;
    const bl=execute(nb,'owner',{type:'MEAS_PREFERENCE_SET',customer:ck,blacklist:'Test'});assert.equal(M.mdKnownGuest(bl,bl.bookings.at(-1)).blacklisted,true);
    const rep=s.bookings.find(b=>M.customerIndex(s).some(c=>c.stays.length>=2&&c.completed.length>=1&&c.stays.includes(b)));
    if(rep){const k=M.mdKnownGuest(s,rep);assert.ok(k===undefined||(k.stays>=1&&typeof k.key==='string'));}
  });
  test('[F12-CAP-DESK] slice absent on the base seed (SCHEMA unchanged) and old saves read as empty',()=>{
    const s=seed();assert.equal(s.measX,undefined);assert.deepEqual(M.mdTurnaways(s),[]);assert.deepEqual(M.mdWalked(s),[]);assert.equal(M.mdExtra(s,'x'),undefined);assert.equal(M.mdPreference(s,'x'),undefined);
  });
}
