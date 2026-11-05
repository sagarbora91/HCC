// AF2-CTL01 · audit CTL-01: a rate reduction is judged against the stay's AGREED rate as well as the tariff (stricter wins), with the Owner's
// threshold `rate.agreedReductionBps` (Master configuration › Rooms and rate plans, default 15 %); the owner digest lists the changes beyond it.
// Same cases as the server's StayRateTests (Hcc.Stays.Tests) and RateThresholdTests (Hcc.Counter.Tests).
export function register({test,M,assert}){
  const {seed,execute}=M;
  const T='[AF2-CTL01] ';
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  function inhouse(s=seed(),room='105'){
    s=execute(s,'owner',{type:'BOOK',guest:'Threshold Test',room,arrival:s.date,departure:'2026-10-27',people:2});const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});return [s,id];
  }
  const rate=(s,who,id,r,reason='Corporate')=>execute(s,who,{type:'STAY_RATE_CHANGE',booking:id,rate:r,scope:'today-next',reason});
  /** A stay agreed at ₹2,500 (the Owner raised it: an Owner-set rate is the agreed rate) on a ₹2,000 tariff. */
  function agreed2500(room){let [s,id]=inhouse(seed(),room);s=rate(s,'owner',id,250000,'Agreed corporate rate');return [s,id];}

  test(T+'the setting: Owner-only, percent, default 15 %, in Rooms and rate plans',()=>{
    const s=seed();const d=M.cfgDef(s,'rate.agreedReductionBps');
    assert.ok(d);assert.equal(d.group,'rooms');assert.equal(d.type,'percent');assert.equal(d.money,true);assert.equal(d.default,1500);
    assert.equal(M.strThresholdBps(s),1500);assert.equal(M.STR_THRESHOLD_KEY,'rate.agreedReductionBps');
    assert.throws(()=>execute(s,'manager',{type:'SETTING_SET',setting:'rate.agreedReductionBps',value:2500,reason:'test'}),e=>e.code==='FORBIDDEN');
    const s2=execute(s,'owner',{type:'SETTING_SET',setting:'rate.agreedReductionBps',value:2500,reason:'CTL-01'});
    assert.equal(M.strThresholdBps(s2),2500);
  });
  test(T+'at the threshold below the agreed rate the Manager applies; one paisa more needs the Owner',()=>{
    let [s,id]=agreed2500('105');
    assert.equal(M.strAgreedRate(s,bk(s,id)),250000);assert.equal(M.strAgreedFloor(250000,1500),212500);
    s=rate(s,'manager',id,212500);assert.equal(bk(s,id).rate,212500);
    const c=s.stayRateX.changes.at(-1);assert.equal(c.overThreshold,false);assert.equal(c.agreedRate,250000);assert.equal(c.agreedAfter,250000);assert.equal(c.thresholdBps,1500);assert.equal(c.tariff,200000);
    let [t,id2]=agreed2500('106');
    t=rate(t,'manager',id2,212499);assert.equal(bk(t,id2).rate,250000);const a=t.approvals.at(-1);assert.equal(a.kind,'rate-change');assert.equal(a.meta.overThreshold,true);assert.equal(a.meta.agreedRate,250000);
    assert.equal(M.strCanApprove(t,'manager',bk(t,id2),212499),false);
    t=execute(t,'owner',{type:'APPROVAL',id:a.id,approve:true});assert.equal(bk(t,id2).rate,212499);
    const c2=t.stayRateX.changes.at(-1);assert.equal(c2.overThreshold,true);assert.equal(c2.approvedBy,'owner');assert.equal(c2.agreedAfter,212499);
    assert.ok(t.audit.some(x=>x.action==='RATE REDUCTION OVER THRESHOLD'&&x.detail.includes(id2)&&/approved by/.test(x.detail)));
  });
  test(T+'the audit case: ₹2,500 → ₹1,900 on a ₹2,000 tariff (within 15 % of the tariff) needs the Owner; a Manager cannot approve it',()=>{
    let [s,id]=agreed2500('105');
    s=execute(s,'reception',{type:'STAY_RATE_CHANGE',booking:id,rate:190000,scope:'today-next',reason:'Guest asked'});
    const a=s.approvals.at(-1);assert.equal(bk(s,id).rate,250000);
    assert.throws(()=>execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true}),e=>e.code==='FORBIDDEN'&&/below the agreed rate/.test(e.message));
    s=execute(s,'owner',{type:'APPROVAL',id:a.id,approve:true});assert.equal(bk(s,id).rate,190000);assert.equal(s.stayRateX.changes.at(-1).overThreshold,true);
  });
  test(T+'an agreed rate lower than the master tariff keeps the tariff limit (the stricter floor)',()=>{
    let [s,id]=inhouse();s=rate(s,'owner',id,180000,'Regular guest agreed rate');
    assert.equal(M.strAgreedRate(s,bk(s,id)),180000);
    s=rate(s,'manager',id,170000);assert.equal(bk(s,id).rate,170000);assert.equal(s.stayRateX.changes.at(-1).overThreshold,false);
    let [t,id2]=inhouse(seed(),'106');t=rate(t,'owner',id2,180000,'Regular guest agreed rate');
    t=rate(t,'manager',id2,169999);assert.equal(bk(t,id2).rate,180000,'below the ₹1,700 tariff floor: an approval');
    assert.equal(t.approvals.at(-1).meta.overThreshold,false);
  });
  test(T+'rate changed mid-stay: Manager steps are measured against the agreed rate until the Owner moves it',()=>{
    let [s,id]=agreed2500('105');
    s=rate(s,'manager',id,220000,'Step one');assert.equal(bk(s,id).rate,220000);
    assert.equal(M.strAgreedRate(s,bk(s,id)),250000,'a Manager change does not move the agreed rate');
    s=rate(s,'manager',id,210000,'Step two');assert.equal(bk(s,id).rate,220000,'16 % below the agreed ₹2,500: approval');
    s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:false});
    s=rate(s,'owner',id,200000,'Owner rate');const c=s.stayRateX.changes.at(-1);
    assert.equal(c.overThreshold,true);assert.equal(c.agreedAfter,200000);assert.equal(M.strAgreedRate(s,bk(s,id)),200000);
    s=rate(s,'manager',id,170000,'Step three');assert.equal(bk(s,id).rate,170000,'15 % below the new agreed ₹2,000');
  });
  test(T+'the Owner raises the threshold: the Manager may then go 24 % below the agreed rate',()=>{
    let [s,id]=agreed2500('105');
    s=execute(s,'owner',{type:'SETTING_SET',setting:'rate.agreedReductionBps',value:2500,reason:'CTL-01'});
    s=rate(s,'manager',id,190000);assert.equal(bk(s,id).rate,190000);const c=s.stayRateX.changes.at(-1);assert.equal(c.thresholdBps,2500);assert.equal(c.overThreshold,false);
  });
  test(T+'the owner digest lists the changes beyond the threshold with who / what / how much; WhatsApp only counts them',()=>{
    let [s,id]=agreed2500('105');
    s=execute(s,'manager',{type:'STAY_RATE_CHANGE',booking:id,rate:190000,scope:'today-next',reason:'Corporate rate'});
    s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    let [t]=[s];const [t2,id2]=inhouse(t,'106');t=rate(t2,'manager',id2,190000,'Small cut'); // 5 % below the agreed ₹2,000: not listed
    const cuts=M.dgsRateCuts(t,t.date);
    // the Owner's raise to ₹2,500 is not a cut; the approved ₹1,900 is
    assert.equal(cuts.length,1);const r=cuts[0];
    assert.deepEqual([r.room,r.booking,r.agreed,r.rate,r.tariff,r.reductionBps,r.thresholdBps,r.reason],['105',id,250000,190000,200000,2400,1500,'Corporate rate']);
    const name=u=>t.users.find(x=>x.id===u).name;
    assert.equal(r.changedBy,name('manager'));assert.equal(r.approvedBy,name('owner'));
    const card=M.dgsCard(t,t.date);assert.equal(card.rateCuts.length,1);
    const email=M.dgsEmailBody(card);
    assert.match(email,/Rate reductions beyond the agreed-rate threshold: 1/);
    assert.ok(email.includes(M.dgsRateLine(r)));assert.match(email,/24% below; threshold 15%/);assert.ok(!email.includes('Room 106'));
    assert.ok(email.includes(`by ${name('manager')} · approved by ${name('owner')}`));
    const wa=M.dgsWhatsAppBody(card);assert.match(wa,/Rate cuts beyond threshold 1/);assert.ok(!wa.includes('₹'));assert.ok(!wa.includes(name('manager')));
  });
}
