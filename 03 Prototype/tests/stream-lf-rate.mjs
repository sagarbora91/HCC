// LF lane lf-money-rate · contract §2 (A1 rate / guest-count change) and §3 (B1 agreed rate).
export function register({test,M,assert}){
  const {seed,execute,totals}=M;
  const T='[LF-RATE] ';
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  function inhouse(s=seed(),extra={}){
    s=execute(s,'owner',{type:'BOOK',guest:'Rate Test',room:'105',arrival:s.date,departure:'2026-10-27',people:2,...extra});const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});return [s,id];
  }
  const tonight=s=>execute(s,'owner',{type:'NIGHT_POST'});

  test(T+'Owner applies directly; tonight re-posts with a reversal and a new room line at the new rate',()=>{
    let [s,id]=inhouse();s=tonight(s);const before=totals(bk(s,id));
    s=execute(s,'owner',{type:'STAY_RATE_CHANGE',booking:id,rate:150000,scope:'today-next',reason:'Long stay'});
    const x=bk(s,id);assert.equal(x.rate,150000);
    const rev=x.lines.find(l=>l.reversalOf);assert.ok(rev);assert.equal(rev.amount,-200000);assert.equal(rev.kind,'adjustment');assert.equal(rev.date,s.date);
    const nl=x.lines.filter(l=>l.kind==='room'&&l.date===s.date&&!x.lines.some(r=>r.reversalOf===l.id));assert.equal(nl.length,1);assert.equal(nl[0].amount,150000);
    assert.equal(totals(x).net,before.net-50000);
    const c=s.stayRateX.changes.at(-1);assert.equal(c.oldRate,200000);assert.equal(c.newRate,150000);assert.equal(c.reposted.length,1);assert.equal(c.effectiveFrom,s.date);
    assert.match(M.strHeaderLine(s,id),/Rate changed/);
  });
  test(T+'scope next: tonight keeps the old rate (posted now), later nights take the new rate',()=>{
    let [s,id]=inhouse();
    s=execute(s,'owner',{type:'STAY_RATE_CHANGE',booking:id,rate:180000,scope:'next',reason:'From tomorrow'});
    const x=bk(s,id);assert.equal(x.lines.find(l=>l.kind==='room'&&l.date===s.date).amount,200000);assert.equal(x.rate,180000);
    assert.equal(s.stayRateX.changes.at(-1).effectiveFrom,'2026-10-25');
  });
  test(T+'scope all never touches a night on a closed business date',()=>{
    let [s,id]=inhouse();s=tonight(s);
    const x=bk(s,id);x.arrival='2026-10-23';x.lines.push({id:'night:old',label:'Room night · 2026-10-23',amount:200000,date:'2026-10-23',kind:'room',taxBps:500});
    assert.deepEqual(M.strPlan(s,x,'all').map(l=>l.date),[s.date]);
    s=execute(s,'owner',{type:'STAY_RATE_CHANGE',booking:id,rate:190000,scope:'all',reason:'Corporate'});
    assert.ok(!bk(s,id).lines.some(l=>l.reversalOf==='night:old'));
  });
  test(T+'re-posted night takes the GST slab of the new price (DEC-069)',()=>{
    let [s,id]=inhouse();s=tonight(s);
    s=execute(s,'owner',{type:'STAY_RATE_CHANGE',booking:id,rate:900000,scope:'today-next',reason:'Upsell'});
    const l=bk(s,id).lines.filter(l=>l.kind==='room').at(-1);assert.equal(l.amount,900000);assert.equal(l.taxBps,M.gstForRoomNight(s,s.date,900000).bps);assert.notEqual(l.taxBps,500);
  });
  test(T+'Manager within 15 % applies directly; beyond needs an Owner',()=>{
    let [s,id]=inhouse();
    s=execute(s,'manager',{type:'STAY_RATE_CHANGE',booking:id,rate:170000,scope:'today-next',reason:'Within limit'});assert.equal(bk(s,id).rate,170000);
    s=execute(s,'manager',{type:'STAY_RATE_CHANGE',booking:id,rate:160000,scope:'today-next',reason:'Beyond limit'});
    assert.equal(bk(s,id).rate,170000);const a=s.approvals.at(-1);assert.equal(a.kind,'rate-change');assert.equal(a.meta.oldRate,170000);
    assert.throws(()=>execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true}),/own request/);
    s=execute(s,'owner',{type:'APPROVAL',id:a.id,approve:true});assert.equal(bk(s,id).rate,160000);assert.equal(s.approvals.find(x=>x.id===a.id).status,'Executed');
    assert.equal(s.stayRateX.changes.at(-1).approvalId,a.id);
  });
  test(T+'Reception always requests; a Manager approves within the limit, not beyond',()=>{
    let [s,id]=inhouse();
    s=execute(s,'reception',{type:'STAY_RATE_CHANGE',booking:id,rate:150000,scope:'today-next',reason:'Guest asked'});
    const a=s.approvals.at(-1);assert.equal(bk(s,id).rate,200000);
    assert.throws(()=>execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>execute(s,'reception',{type:'STAY_RATE_CHANGE',booking:id,rate:190000,scope:'today-next',reason:'Second'}),/already pending/);
    s=execute(s,'manager',{type:'APPROVAL',id:a.id,approve:false});
    s=execute(s,'reception',{type:'STAY_RATE_CHANGE',booking:id,rate:180000,scope:'today-next',reason:'Ok'});
    s=execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});assert.equal(bk(s,id).rate,180000);
  });
  test(T+'a raise above the tariff: Manager direct, Reception still requests',()=>{
    let [s,id]=inhouse();
    s=execute(s,'manager',{type:'STAY_RATE_CHANGE',booking:id,rate:250000,scope:'today-next',reason:'Peak'});assert.equal(bk(s,id).rate,250000);
    s=execute(s,'reception',{type:'STAY_RATE_CHANGE',booking:id,rate:260000,scope:'today-next',reason:'Peak'});assert.equal(bk(s,id).rate,250000);assert.equal(s.approvals.at(-1).kind,'rate-change');
  });
  test(T+'people change: capacity checked; a Manager raise with an extra-person charge needs approval',()=>{
    let [s,id]=inhouse(seed(),{people:1});
    assert.throws(()=>execute(s,'owner',{type:'STAY_RATE_CHANGE',booking:id,people:3,scope:'today-next',reason:'Family'}),/accommodates at most/);
    const extra=M.strExtraChargeApplies(s,bk(s,id));
    s=execute(s,'manager',{type:'STAY_RATE_CHANGE',booking:id,people:2,scope:'today-next',reason:'Friend joined'});
    if(extra){assert.equal(bk(s,id).people,1);assert.equal(s.approvals.at(-1).kind,'rate-change');}else assert.equal(bk(s,id).people,2);
    s=execute(s,'owner',{type:'STAY_RATE_CHANGE',booking:id,people:2,rate:210000,scope:'today-next',reason:'Owner'});assert.equal(bk(s,id).people,2);
  });
  test(T+'guards: not in house, nothing changes, bad scope, no reason, roles',()=>{
    let s=seed();s=execute(s,'owner',{type:'BOOK',guest:'Future',room:'105',arrival:s.date,departure:'2026-10-26',people:1});const fid=s.bookings.at(-1).id;
    assert.throws(()=>execute(s,'owner',{type:'STAY_RATE_CHANGE',booking:fid,rate:1,scope:'all',reason:'x'}),/in-house stay only/);
    let [t,id]=inhouse();
    assert.throws(()=>execute(t,'owner',{type:'STAY_RATE_CHANGE',booking:id,rate:200000,scope:'all',reason:'x'}),/Nothing changes/);
    assert.throws(()=>execute(t,'owner',{type:'STAY_RATE_CHANGE',booking:id,rate:1000,scope:'later',reason:'x'}),/Choose when/);
    assert.throws(()=>execute(t,'owner',{type:'STAY_RATE_CHANGE',booking:id,rate:1000,scope:'all',reason:' '}),/reason/);
    assert.throws(()=>execute(t,'owner',{type:'STAY_RATE_CHANGE',booking:id,rate:-1,scope:'all',reason:'x'}),/whole paise/);
    for(const u of ['cashier','accountant','housekeeper'])assert.throws(()=>execute(t,u,{type:'STAY_RATE_CHANGE',booking:id,rate:1000,scope:'all',reason:'x'}),e=>e.code==='FORBIDDEN');
  });
  test(T+'approval goes stale when the folio moves',()=>{
    let [s,id]=inhouse();
    s=execute(s,'reception',{type:'STAY_RATE_CHANGE',booking:id,rate:190000,scope:'today-next',reason:'x'});const a=s.approvals.at(-1);
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:10000});
    assert.throws(()=>execute(s,'owner',{type:'APPROVAL',id:a.id,approve:true}),/folio changed/);
  });

  // ── B1 agreed rate ──
  const G='G-0001';
  test(T+'agreed rate: Owner only; set, applied at BOOK with rate:"agreed", rateSource recorded',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'manager',{type:'GUEST_RATE_SET',guestId:G,roomType:'Classic',rate:170000}),e=>e.code==='FORBIDDEN');
    s=execute(s,'owner',{type:'GUEST_RATE_SET',guestId:G,roomType:'Classic',rate:170000,validTo:'2026-12-31',note:'Regular'});
    assert.equal(s.guests.find(g=>g.id===G).agreedRates[0].rate,170000);
    s=execute(s,'reception',{type:'BOOK',guest:'Ananya Rao',guestId:G,room:'105',arrival:s.date,departure:'2026-10-26',people:1,rate:'agreed'});
    const x=s.bookings.at(-1);assert.equal(x.rate,170000);assert.equal(x.rateSource,'agreed');
    s=execute(s,'reception',{type:'BOOK',guest:'Ananya Rao',phone:'+91 98200 01041',room:'105',arrival:'2026-10-26',departure:'2026-10-27',people:1,rate:'agreed'});
    assert.equal(s.bookings.at(-1).rate,170000,'phone resolves to the one guest');
  });
  test(T+'agreed rate: expired or other room type refused; clear removes it',()=>{
    let s=seed();
    s=execute(s,'owner',{type:'GUEST_RATE_SET',guestId:G,roomType:'Classic',rate:170000,validTo:s.date});
    s.date='2026-10-25';
    assert.throws(()=>execute(s,'owner',{type:'BOOK',guest:'Ananya',guestId:G,room:'105',arrival:'2026-10-25',departure:'2026-10-26',people:1,rate:'agreed'}),/no agreed rate/);
    assert.throws(()=>execute(s,'owner',{type:'GUEST_RATE_SET',guestId:G,roomType:'Nope',rate:1}),/room type/);
    s=execute(s,'owner',{type:'GUEST_RATE_CLEAR',guestId:G,roomType:'Classic'});assert.equal(s.guests.find(g=>g.id===G).agreedRates,undefined);
  });
  test(T+'agreed rate applied at CHECKIN and hidden from non-money roles',()=>{
    let s=seed();s=execute(s,'owner',{type:'GUEST_RATE_SET',guestId:G,roomType:'Classic',rate:165000});
    s=execute(s,'owner',{type:'BOOK',guest:'Ananya Rao',room:'105',arrival:s.date,departure:'2026-10-26',people:1});const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id,guestId:G,guest:{...s.guests.find(g=>g.id===G)}});
    s=execute(s,'owner',{type:'CHECKIN',id,rate:'agreed'});assert.equal(bk(s,id).rate,165000);assert.equal(bk(s,id).rateSource,'agreed');
    const g=s.guests.find(g=>g.id===G);
    assert.ok(M.guestMoneyFilter(s,'reception',g).agreedRates);assert.equal(M.guestMoneyFilter(s,'housekeeper',g).agreedRates,undefined);
    assert.equal(M.guestAgreedRatesView(s,'maint',g),undefined);
  });
}
