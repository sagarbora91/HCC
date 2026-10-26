// DEC-069 · GST master (domain/gst.ts + Master configuration "Money and tax"). Names prefixed [GST].
// Seeded rates are illustrative (public sources, 25 Oct 2026) — CA to confirm. Money in paise.
export function register({test,M,assert}){
  const {seed,execute,totals,loadScenario,gstForRoomNight,gstForFood,gstForService,gstSpecifiedPremises,gstLineTax,gstSplit,gstLineSplit,gstStayTax,
    gstValueOn,gstCategoryOfLabel,gstFy,cfgDef,cfgCanChange,cfgGroupAccess,HccError}=M;
  const code=fn=>{try{fn();return 'OK';}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}};
  const set=(s,uid,setting,value,effectiveFrom)=>execute(s,uid,{type:'SETTING_SET',setting,value,...(effectiveFrom?{effectiveFrom}:{}),reason:'CA advice'});
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  const inhouse=(s,room,dep='2026-10-27')=>{s=execute(s,'reception',{type:'BOOK',guest:'GST Test (fictional)',room,arrival:s.date,departure:dep,people:1});const id=s.bookings.at(-1).id;
    s=execute(s,'reception',{type:'REGISTER',id});s=execute(s,'reception',{type:'CHECKIN',id});return [s,id];};

  // ── Room slab, per unit per night ──
  test('[GST] slab boundary: ₹7,500.00 → 5 % without ITC; ₹7,500.01 → 18 % with ITC',()=>{
    const s=seed(),d=s.date;
    assert.deepEqual([gstForRoomNight(s,d,750000).bps,gstForRoomNight(s,d,750000).itc],[500,false]);
    assert.deepEqual([gstForRoomNight(s,d,750001).bps,gstForRoomNight(s,d,750001).itc],[1800,true]);
    assert.equal(gstForRoomNight(s,d,1).bps,500);assert.equal(gstForRoomNight(s,d,10000000).bps,1800);
    assert.equal(gstLineTax(750000,500),37500);assert.equal(gstLineTax(750001,1800),135000); // 1,35,000.18 paise → 1,35,000
  });
  test('[GST] before / after 22 Sep 2025: lower slab 12 % with ITC → 5 % without ITC; upper slab 18 % throughout',()=>{
    const s=seed();
    assert.deepEqual([gstForRoomNight(s,'2025-09-21',300000).bps,gstForRoomNight(s,'2025-09-21',300000).itc],[1200,true]);
    assert.deepEqual([gstForRoomNight(s,'2025-09-22',300000).bps,gstForRoomNight(s,'2025-09-22',300000).itc],[500,false]);
    assert.equal(gstForRoomNight(s,'2025-09-21',800000).bps,1800);assert.equal(gstForRoomNight(s,'2025-09-22',800000).bps,1800);
    // A stay across the change is taxed per night: 20, 21 Sep at 12 %, 22, 23 Sep at 5 %.
    assert.equal(gstStayTax(s,300000,'2025-09-20','2025-09-24'),2*36000+2*15000);
  });
  test('[GST] multi-night stay: each night tested on its own price (mixed slabs on one folio)',()=>{
    let [s,id]=inhouse(seed(),'106');                         // Suite ₹4,500
    s=execute(s,'owner',{type:'NIGHT_POST'});                 // 24 Oct at ₹4,500
    // The agreed nightly price changes during the stay (e.g. upgrade): later nights are tested on the new price.
    const raised=structuredClone(s);bk(raised,id).rate=800000;
    let t=execute(raised,'owner',{type:'POST_NIGHTS',id});      // 25, 26 Oct at ₹8,000
    const rooms=bk(t,id).lines.filter(l=>l.kind==='room');
    assert.deepEqual(rooms.map(l=>[l.date,l.amount,l.taxBps]),[['2026-10-24',450000,500],['2026-10-25',800000,1800],['2026-10-26',800000,1800]]);
    assert.equal(totals(bk(t,id)).tax,22500+144000+144000);
    // Average (₹7,000) would be the lower slab — the rule never averages.
    assert.equal(gstForRoomNight(t,t.date,Math.round((450000+800000*2)/3)).bps,500);
    // Same stay priced at exactly ₹7,500 a night stays at 5 % for every night.
    const at=structuredClone(s);bk(at,id).rate=750000;t=execute(at,'owner',{type:'POST_NIGHTS',id});
    assert.deepEqual(bk(t,id).lines.filter(l=>l.kind==='room').map(l=>l.taxBps),[500,500,500]);
  });
  test('[GST] quotes and estimates add GST per night (website quote, group stay value)',()=>{
    const s=seed();
    const q=M.chnWebQuote(s,'Suite','2026-10-25','2026-10-28',2,0);
    assert.deepEqual([q.net,q.tax,q.taxBps,q.total],[1350000,67500,500,1417500]);
    assert.equal(M.grpStayValue(s,800000,'2026-10-25','2026-10-27'),2*800000+2*144000);
  });

  // ── Restaurant / room service and specified premises ──
  test('[GST] restaurant: 5 % without ITC when no unit was above ₹7,500 last FY (derived from tariffs)',()=>{
    const s=seed(),sp=gstSpecifiedPremises(s,s.date);
    assert.deepEqual([sp.fy,sp.prevFy,sp.specified,sp.derived,sp.optIn,sp.maxPrevTariff],['2026-27','2025-26',false,false,false,450000]);
    assert.deepEqual([gstForFood(s,s.date).bps,gstForFood(s,s.date).itc],[500,false]);
    assert.deepEqual(gstFy('2026-03-31'),{label:'2025-26',from:'2025-04-01',to:'2026-03-31'});assert.equal(gstFy('2026-04-01').label,'2026-27');
  });
  test('[GST] A03a-02: a night above ₹7,500 posted by mistake and reversed never makes next FY specified premises',()=>{
    // The prototype derives the status from tariffs only (G5; the server also counts nights actually charged, minus reversed ones).
    let [s,id]=inhouse(seed(),'106');bk(s,id).rate=900000;s=execute(s,'owner',{type:'POST_NIGHTS',id});
    const night=bk(s,id).lines.find(l=>l.kind==='room');assert.deepEqual([night.amount,night.taxBps],[900000,1800]);
    s=execute(s,'reception',{type:'CORRECTION_REQUEST',id,lineId:night.id,reason:'Posted at the wrong rate'});
    s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    assert.ok(bk(s,id).lines.some(l=>l.reversalOf===night.id));
    const sp=gstSpecifiedPremises(s,'2027-04-01');
    assert.deepEqual([sp.prevFy,sp.specified,sp.derived,sp.maxPrevTariff],['2026-27',false,false,450000]);
    assert.equal(gstForFood(s,'2027-04-01').bps,500);
  });
  test('[GST] specified premises derived from last FY: a unit above ₹7,500 in FY 2025-26 → 18 % with ITC in FY 2026-27',()=>{
    const s=seed();s.tariffs=[{type:'Suite',rate:800000,effectiveFrom:'2025-06-01'},{type:'Suite',rate:450000,effectiveFrom:'2025-07-01'}];
    const sp=gstSpecifiedPremises(s,s.date);assert.equal(sp.specified,true);assert.equal(sp.derived,true);assert.equal(sp.maxPrevTariff,800000);
    assert.deepEqual([gstForFood(s,s.date).bps,gstForFood(s,s.date).itc],[1800,true]);
    // FY 2025-26 itself looks at FY 2024-25 (only the ₹4,500 base rate then) → 5 %.
    assert.equal(gstForFood(s,'2026-03-31').bps,500);
    // Exactly ₹7,500 is not above the threshold.
    const t=seed();t.tariffs=[{type:'Suite',rate:750000,effectiveFrom:'2025-06-01'}];assert.equal(gstForFood(t,t.date).bps,500);
    // A tariff above ₹7,500 from 1 Nov 2026 changes FY 2027-28 only.
    let u=seed();u=execute(u,'owner',{type:'TARIFF_SCHEDULE',roomType:'Suite',rate:900000,effectiveFrom:'2026-11-01'});
    assert.equal(gstForFood(u,'2027-03-31').bps,500);assert.equal(gstForFood(u,'2027-04-01').bps,1800);
  });
  test('[GST] specified premises opt-in (Owner declaration) applies from its effective date; POS bills follow it',()=>{
    let s=seed();
    s=set(s,'owner','gst.food.optIn',true,'2026-10-25');
    assert.equal(gstForFood(s,'2026-10-24').bps,500);assert.equal(gstForFood(s,'2026-10-25').bps,1800);
    assert.equal(gstSpecifiedPremises(s,'2026-10-25').optIn,true);assert.match(gstSpecifiedPremises(s,'2026-10-25').reason,/opt-in/);
    let t=set(seed(),'owner','gst.food.optIn',true);                       // effective today
    t=execute(t,'reception',{type:'POS_SETTLE',id:'POS-F04',mode:'folio'});   // paratha ₹240 + chai ₹60
    const o=M.posSlice(t).orders.find(x=>x.id==='POS-F04');assert.deepEqual([o.settlement.taxBps,o.settlement.tax],[1800,5400]);
    assert.equal(bk(t,o.booking).lines.at(-1).taxBps,1800);
  });

  // ── Other services and charge categories ──
  test('[GST] other services: laundry / banquet / other 18 % (dated); food words → restaurant rate; explicit category wins',()=>{
    let [s,id]=inhouse(seed(),'105');
    for(const k of ['laundry','banquet','other'])assert.equal(gstForService(s,s.date,k).bps,1800,k);
    assert.deepEqual(['Laundry service','Dry cleaning','Banquet hall','Conference room','Room service dinner','Minibar','Masala chai','Airport taxi'].map(gstCategoryOfLabel),
      ['laundry','laundry','banquet','banquet','food','food','food','other']);
    s=execute(s,'reception',{type:'CHARGE',id,label:'Laundry',amount:50000});assert.equal(bk(s,id).lines.at(-1).taxBps,1800);
    s=execute(s,'reception',{type:'CHARGE',id,label:'Breakfast',amount:20000});assert.equal(bk(s,id).lines.at(-1).taxBps,500);
    s=execute(s,'reception',{type:'CHARGE',id,label:'Hall hire',amount:20000,gst:'food'});assert.equal(bk(s,id).lines.at(-1).taxBps,500);
    assert.equal(code(()=>execute(s,'reception',{type:'CHARGE',id,label:'X',amount:100,gst:'liquor'})),'VALIDATION');
    s=set(s,'owner','gst.svc.laundryBps',1200);
    s=execute(s,'reception',{type:'CHARGE',id,label:'Laundry',amount:50000});assert.equal(bk(s,id).lines.at(-1).taxBps,1200);
    assert.equal(gstForService(s,s.date,'banquet').bps,1800);
  });
  test('[GST] discounts and credit notes follow the room-night rate of the folio',()=>{
    let [s,id]=inhouse(seed(),'105');s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'DISCOUNT',id,amount:20000,reason:'Goodwill'});
    const d=bk(s,id).lines.at(-1);assert.deepEqual([d.kind,d.amount,d.taxBps],['discount',-20000,500]);
    assert.equal(totals(bk(s,id)).tax,3*10000-1000); // 3 nights × ₹100 − ₹10
  });

  // ── Rounding and CGST / SGST ──
  test('[GST] per-line rounding half away from zero; CGST + SGST always sum to the line tax',()=>{
    assert.equal(gstLineTax(10,500),1);assert.equal(gstLineTax(-10,500),-1);   // ±0.5 paisa → ±1
    assert.equal(gstLineTax(9,500),0);assert.equal(gstLineTax(-9,500),0);assert.equal(gstLineTax(30,500),2); // 1.5 → 2
    assert.equal(gstLineTax(-30,500),-2);assert.equal(gstLineTax(12345,1800),2222);assert.equal(gstLineTax(-12345,1800),-2222);
    assert.deepEqual(gstSplit(2223),{cgst:1112,sgst:1111});assert.deepEqual(gstSplit(1),{cgst:1,sgst:0});assert.deepEqual(gstSplit(-1),{cgst:-1,sgst:0});assert.deepEqual(gstSplit(-2223),{cgst:-1112,sgst:-1111});assert.deepEqual(gstSplit(0),{cgst:0,sgst:0});
    for(const bps of [0,500,1200,1800,2800])for(let amount=-2003;amount<=2003;amount+=7){
      const x=gstLineSplit({amount,taxBps:bps});
      assert.equal(x.cgst+x.sgst,x.tax,`${amount}@${bps}`);assert.ok(Math.abs(x.cgst-x.sgst)<=1);assert.equal(x.tax,gstLineTax(amount,bps));
      assert.ok(Math.abs(x.tax*10000-amount*bps)<=5000,`${amount}@${bps}`);}
  });
  test('[GST] vouchers split Output CGST / SGST and balance; Tally XML carries both ledgers; ledgers equal report tax',()=>{
    let [s,id]=inhouse(seed(),'105');s=execute(s,'owner',{type:'NIGHT_POST'});
    s=execute(s,'reception',{type:'CHARGE',id,label:'Laundry',amount:12345});        // 18 % → 2222 → CGST 1111 + SGST 1111
    s=execute(s,'cashier',{type:'POS_KOT_SEND',id:'POS-F03'});s=execute(s,'cashier',{type:'POS_SETTLE',id:'POS-F03',mode:'outlet',method:'Cash'}); // ₹1,740 → ₹87 → ₹43.50 + ₹43.50
    const vs=M.accLiveVouchers(s,s.date);assert.ok(vs.every(M.accBalanced));
    const lv=vs.find(v=>v.source.id===`${id}/${bk(s,id).lines.at(-1).id}`);
    assert.deepEqual(lv.entries.map(e=>[e.account,e.debit,e.credit]),[['1100',14567,0],['4100',0,12345],['2110',0,1111],['2120',0,1111]]);
    const pv=vs.find(v=>v.source.kind==='pos'&&v.narration.includes('POS-F03'));
    assert.deepEqual(pv.entries.map(e=>[e.account,e.debit,e.credit]),[['1000',182700,0],['4100',0,174000],['2110',0,4350],['2120',0,4350]]);
    const c=M.accReconcile(s,s.date).find(x=>x.id==='tax');assert.ok(c.ok,JSON.stringify(c));
    const x=M.accTallyXml(s,s.date,s.date).xml;assert.match(x,/<LEDGERNAME>Output CGST<\/LEDGERNAME>/);assert.match(x,/<LEDGERNAME>Output SGST<\/LEDGERNAME>/);
    assert.doesNotMatch(x,/Synthetic tax payable/);
    for(const ch of M.accReconcile(s,s.date))assert.ok(ch.ok,ch.id);
  });

  // ── Snapshots, permissions, dated changes ──
  test('[GST] posted lines keep their stored rate after master changes; new postings use the master',()=>{
    let [s,id]=inhouse(seed(),'105');s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'reception',{type:'CHARGE',id,label:'Laundry',amount:50000});
    const before=JSON.stringify(bk(s,id).lines),t0=totals(bk(s,id));
    for(const k of ['gst.room.lowBps','gst.svc.laundryBps','gst.food.bps'])s=set(s,'owner',k,1200);
    s=set(s,'owner','gst.room.threshold',100000);
    assert.equal(JSON.stringify(bk(s,id).lines),before);assert.deepEqual(totals(bk(s,id)),t0);
    assert.ok(bk(s,'HCC-1041').lines.every(l=>l.taxBps===500));
    s=execute(s,'reception',{type:'CHARGE',id,label:'Laundry',amount:50000});assert.equal(bk(s,id).lines.at(-1).taxBps,1200);
    assert.equal(gstForRoomNight(s,s.date,200000).bps,1800); // threshold now ₹1,000 → ₹2,000 is the upper slab
  });
  test('[GST] Owner-only edit: Tech admin read-only (DEC-058), other roles denied; validation',()=>{
    const s=seed();
    for(const k of M.GST_KEYS){const d=cfgDef(s,k);assert.equal(cfgCanChange(s,'owner',d).mode,'allow',k);
      for(const u of ['techadmin','manager','accountant','reception','cashier'])assert.equal(cfgCanChange(s,u,d).mode,'deny',u+' '+k);}
    assert.equal(cfgGroupAccess(s,'techadmin','money').readOnly,true);
    for(const u of ['techadmin','manager','accountant'])assert.equal(code(()=>set(s,u,'gst.room.lowBps',1200)),'FORBIDDEN',u);
    assert.equal(code(()=>set(s,'owner','gst.room.lowBps',2900)),'VALIDATION');assert.equal(code(()=>set(s,'owner','gst.room.lowBps',12.5)),'VALIDATION');
    assert.equal(code(()=>set(s,'owner','gst.room.lowBps',500)),'VALIDATION');           // no change
    assert.equal(code(()=>set(s,'owner','gst.specified.status','x')),'VALIDATION');      // derived, not editable
    assert.equal(code(()=>execute(s,'owner',{type:'TAX_SCHEDULE',bps:1200,effectiveFrom:s.date})),'VALIDATION');
    assert.equal(code(()=>execute(s,'techadmin',{type:'TAX_SCHEDULE',bps:1200,effectiveFrom:s.date})),'FORBIDDEN');
    assert.match(cfgDef(s,'gst.room.lowBps').help,/Illustrative — CA to confirm/);
  });
  test('[GST] dated change applies from its effective date (night audit), can be withdrawn, and history keeps each date',()=>{
    let [s,id]=inhouse(seed(),'105');
    s=set(s,'owner','gst.room.lowBps',1200,'2026-10-25');
    assert.equal(code(()=>set(s,'owner','gst.room.lowBps',1200,'2026-10-23')),'VALIDATION');  // never backdated
    assert.equal(gstValueOn(s,'gst.room.lowBps','2026-10-24'),500);assert.equal(gstValueOn(s,'gst.room.lowBps','2026-10-25'),1200);
    s=execute(s,'owner',{type:'POST_NIGHTS',id});
    assert.deepEqual(bk(s,id).lines.map(l=>[l.date,l.taxBps]),[['2026-10-24',500],['2026-10-25',1200],['2026-10-26',1200]]);
    assert.equal(s.configHistory[0].key,'gst.room.lowBps');assert.equal(s.configHistory[0].effectiveFrom,'2026-10-25');
    // Withdraw before it takes effect → master back to the seed for that date.
    const sched=s.configX.scheduled.find(x=>x.key==='gst.room.lowBps');
    let w=execute(s,'owner',{type:'CFG_WITHDRAW',id:sched.id,reason:'CA withdrew the advice'});assert.equal(gstValueOn(w,'gst.room.lowBps','2026-10-25'),500);
    // Past dates keep the statutory seed (history), whatever is scheduled later.
    assert.equal(gstValueOn(s,'gst.room.lowBps','2025-09-21'),1200);assert.equal(gstValueOn(s,'gst.room.lowBps','2026-04-01'),500);
    // A same-day change applies at once and can be reverted (history/revert).
    let r=set(seed(),'owner','gst.svc.otherBps',1200);const hid=r.configHistory[0].id;
    r=execute(r,'owner',{type:'CFG_REVERT',id:hid});assert.equal(gstForService(r,r.date,'other').bps,1800);
  });
  test('[GST] A02-02: a GST change and a GST withdrawal need a reason (≥ 3 characters, as the server\'s SetGstSetting / WithdrawGstSetting); other settings do not',()=>{
    const s=seed();const msg=fn=>{try{fn();return 'OK';}catch(e){return `${e.code}: ${e.message}`;}};
    const refused='VALIDATION: Give a reason for the GST change (at least 3 characters).';
    for(const reason of [undefined,'','  ','ab'])
      assert.equal(msg(()=>execute(s,'owner',{type:'SETTING_SET',setting:'gst.room.lowBps',value:1200,effectiveFrom:'2026-10-25',...(reason===undefined?{}:{reason})})),refused,String(reason));
    assert.equal(code(()=>execute(s,'techadmin',{type:'SETTING_SET',setting:'gst.room.lowBps',value:1200})),'FORBIDDEN','permission first: Owner only');
    const t=set(s,'owner','gst.room.lowBps',1200,'2026-10-25');const id=t.configX.scheduled.find(x=>x.key==='gst.room.lowBps').id;
    assert.equal(t.configHistory[0].reason,'CA advice');
    assert.equal(msg(()=>execute(t,'owner',{type:'CFG_WITHDRAW',id})),refused);
    assert.equal(execute(t,'owner',{type:'CFG_WITHDRAW',id,reason:'CA withdrew it'}).configX.scheduled.length,0);
    // A revert carries its own reason ("Revert to previous value") unless the sheet gives one.
    const r=set(s,'owner','gst.svc.otherBps',1200);assert.equal(execute(r,'owner',{type:'CFG_REVERT',id:r.configHistory[0].id}).configHistory[0].reason,'Revert to previous value');
    assert.equal(code(()=>execute(s,'owner',{type:'SETTING_SET',setting:'checkin',value:'13:00'})),'OK','non-GST settings keep the optional reason');
  });

  // ── Demo figures and FY26 history ──
  test('[GST] base figures: room nights 5 %, POS 5 %, Room 105 journey ₹4,265 (was ₹4,400)',()=>{
    const s=seed();assert.ok(s.bookings.flatMap(b=>b.lines).every(l=>l.taxBps===500));
    assert.ok(M.posSlice(s).receipts.every(r=>r.taxBps===500));assert.equal(M.drawer(s),1072500);
    let [t,id]=inhouse(s,'105','2026-10-26');t=execute(t,'owner',{type:'POST_NIGHTS',id});
    t=execute(t,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000});t=execute(t,'owner',{type:'DISCOUNT',id,amount:50000,reason:'Goodwill'});
    assert.deepEqual(totals(bk(t,id)),{net:400000,tax:26500,total:426500,paid:0,deposit:0,balance:426500});
  });
  test('[GST] FY26 history: every line taxed by the master for its date; deterministic; vouchers balance on sampled days',()=>{
    const a=loadScenario('FY26');
    const hist=a.bookings.filter(b=>b.id.startsWith('HCC-H'));
    for(const b of hist)for(const l of b.lines){
      const want=l.kind==='room'?gstForRoomNight(a,l.date,l.amount).bps:l.id.startsWith('posline:')?gstForFood(a,l.date).bps:l.kind==='discount'?b.lines.find(x=>x.kind==='room').taxBps
        :l.id.startsWith('noshow:')?gstForRoomNight(a,l.date,l.amount).bps:M.gstForCharge(a,l.date,gstCategoryOfLabel(l.label)).bps;
      assert.equal(l.taxBps,want,b.id+' '+l.id);}
    assert.ok(hist.some(b=>b.lines.some(l=>l.taxBps===1800)),'laundry lines at 18 %');
    for(const r of M.posSlice(a).receipts)assert.equal(r.taxBps,500,r.id);
    for(const w of M.chnSlice(a).webRequests.filter(w=>w.id.startsWith('WEB-H'))){const b=bk(a,w.booking);assert.equal(w.tax,gstStayTax(a,b.rate,b.arrival,b.departure),w.id);}
    const days=Object.keys(a.closings).sort(),pick=[days[0],days[60],days[150],days.at(-1)];
    for(const d of pick){const vs=M.accVouchers(a,d);assert.ok(vs.every(M.accBalanced),d);
      const p=M.reportPack(a,d,d),tax=p.tabs.find(t=>t.id==='revenue').metrics.find(m=>m.id==='tax').value;assert.equal(tax,a.closings[d].revenue.tax,d);}
    assert.equal(JSON.stringify(loadScenario('FY26').bookings.slice(0,50)),JSON.stringify(a.bookings.slice(0,50)));
  });
}
