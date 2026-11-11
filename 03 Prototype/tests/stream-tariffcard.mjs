// F4-T1 · Tariff card — domain / command tests (F4-CONTRACT.md §1, §5). Names prefixed [TRC].
// Fixture: tests/fixtures/f4/rate-card/*.csv = a fictional copy of the DEC-198 rate-card files (owner's public card ÷ 1.05 — room GST 5 % per the CA, DEC-239; rooms made up).
import {readFileSync} from 'node:fs';
export function register({test,M,assert}){
  const {seed,execute}=M;
  const csv=n=>readFileSync(new URL('./fixtures/f4/rate-card/'+n+'.csv',import.meta.url),'utf8');
  const files=()=>({roomTypes:csv('room-types'),tariffs:csv('tariffs'),occupancyCharges:csv('occupancy-charges'),mealPlans:csv('meal-plans'),rooms:csv('rooms')});
  const room=(s,type)=>s.rooms.find(r=>r.type===type);
  const gst18=s=>execute(s,'owner',{type:'SETTING_SET',setting:'gst.room.lowBps',value:1800,effectiveFrom:'2026-11-01',reason:'DEC-198 owner setup'});
  const draft=(s,lines,from='2026-11-01',uid='owner',extra={})=>execute(s,uid,{type:'TRC_DRAFT_SAVE',label:'Test card',effectiveFrom:from,lines,...extra});
  const last=s=>s.tariffCardX.versions.at(-1);
  const reason=fn=>e=>{assert.equal(M.trcReasonOf(e),fn);return true;};

  test('[TRC] rounding: inclusive splits CGST / SGST per half; round-trip at 5 % and 18 %',()=>{
    assert.equal(M.trcInclusive(169492,1800),200000,'Deluxe golden (DEC-198)');
    assert.equal(M.trcExclusiveFromInclusive(200000,1800),169492);
    for(const bps of [500,1200,1800])for(const e of [1,99,12345,169492,279661,750000]){
      const i=M.trcInclusive(e,bps);assert.equal(M.trcInclusive(M.trcExclusiveFromInclusive(i,bps),bps),i,`${e}@${bps}`);}
    assert.equal(M.trcExclusiveFromInclusive(210000,500),200000);
    const d=M.trcExclusiveFromInclusive(330000,1800);assert.equal(d,279661);assert.equal(M.trcInclusive(d,1800),329999,'unreachable 3,300 → ₹0.01 rounding difference');
    // F4-int rounding ruling: the card's GST-inclusive price is authoritative — exact where an exclusive paise value reproduces it, else nearest + shown difference (CA to confirm)
    for(const [incl,bps,excl,diff] of [[330000,1800,279661,-1],[50000,1800,42373,1],[20000,1800,16949,-1],[330000,500,314286,0],[50000,500,47619,-1],[20000,500,19048,0]]){
      const e=M.trcExclusiveFromInclusive(incl,bps);assert.equal(e,excl,`${incl}@${bps}`);assert.equal(M.trcInclusive(e,bps)-incl,diff,`diff ${incl}@${bps}`);
      for(let x=Math.max(0,e-50);x<=e+50;x++)assert.ok(Math.abs(M.trcInclusive(x,bps)-incl)>=Math.abs(diff),`no closer exclusive ${incl}@${bps}`);}
  });
  test('[TRC] Deluxe ₹2,000 incl at 5 % (CA, DEC-239) → excl ₹1,904.76, display ₹2,000.00, rounding difference 0',()=>{
    let s=seed();s=draft(s,[{kind:'room',roomType:'Deluxe',plan:'EP',daySet:'all',enteredIncl:200000}]);
    const l=last(s).lines[0];assert.equal(l.rateExcl,190476);assert.equal(l.taxBps,500);assert.equal(l.displayIncl,200000);assert.equal(M.trcRoundingDiff(l),0);
  });
  test('[TRC] taxBps comes from the dated GST master at effectiveFrom (12 % → 5 % on 22 Sep 2025)',()=>{
    const s=seed();
    assert.equal(M.trcPriceLine(s,{kind:'room',roomType:'Deluxe',rateExcl:200000},'2025-09-21','L1').taxBps,1200);
    assert.equal(M.trcPriceLine(s,{kind:'room',roomType:'Deluxe',rateExcl:200000},'2025-09-22','L1').taxBps,500);
    assert.equal(M.trcPriceLine(s,{kind:'room',roomType:'Suite',rateExcl:800000},'2026-11-01','L1').taxBps,1800,'above ₹7,500 = 18 %');
    assert.equal(M.trcPriceLine(gst18(s),{kind:'room',roomType:'Deluxe',rateExcl:200000},'2026-11-01','L1').taxBps,1800,'an Owner master change to 18 % is still honoured (dated master)');
    assert.equal(M.trcPriceLine(s,{kind:'room',roomType:'Deluxe',enteredIncl:200000},'2026-11-01','L1').taxBps,500,'room GST 5 % by default (CA, DEC-239)');
  });
  test('[TRC] inclusive entry on the slab boundary is refused TRC_SLAB_AMBIGUOUS',()=>{
    // ₹7,875 incl at 5 % = ₹7,500 excl (low slab) and ₹7,875 incl at 18 % = ₹6,673.73 (low slab too, not 18 %): only one self-consistent → ok
    const s=seed();assert.equal(M.trcPriceLine(s,{kind:'room',roomType:'Suite',enteredIncl:787500},'2026-11-01','L1').rateExcl,750000);
    // ₹8,860 incl: 5 % → 8,438 (above threshold, inconsistent); 18 % → 7,508.47 (above, consistent) → 18 %
    assert.equal(M.trcPriceLine(s,{kind:'room',roomType:'Suite',enteredIncl:886000},'2026-11-01','L1').taxBps,1800);
    // ₹8,000 incl: 5 % → 7,619 (above, inconsistent), 18 % → 6,779.66 (below, inconsistent) → refused
    assert.throws(()=>M.trcPriceLine(s,{kind:'room',roomType:'Suite',enteredIncl:800000},'2026-11-01','L1'),reason('TRC_SLAB_AMBIGUOUS'));
  });
  test('[TRC] publish → new booking takes the new rate; existing bookings and earlier nights unchanged',()=>{
    let s=seed();const before=JSON.stringify(s.bookings);const old=M.rateFor(s,room(s,'Classic'),'2026-10-30');
    s=draft(s,[{kind:'room',roomType:'Classic',plan:'EP',daySet:'all',rateExcl:250000}],'2026-10-28');const id=last(s).id;
    s=execute(s,'owner',{type:'TRC_PUBLISH',id});
    assert.equal(JSON.stringify(s.bookings),before);
    assert.equal(M.rateFor(s,room(s,'Classic'),'2026-10-27'),old);assert.equal(M.rateFor(s,room(s,'Classic'),'2026-10-28'),250000);
    assert.deepEqual(s.tariffs.find(t=>t.cardVersion===id),{type:'Classic',rate:250000,effectiveFrom:'2026-10-28',cardVersion:id});
    assert.equal(last(s).status,'published');assert.equal(last(s).publishLog.length,1);
    assert.equal(s.audit.filter(a=>a.action==='TRC PUBLISH ROW').length,1);assert.equal(s.audit[0].action,'TRC PUBLISH');
    s=execute(s,'owner',{type:'BOOK',guest:'Card Test',room:room(s,'Classic').id,arrival:'2026-10-29',departure:'2026-10-30',people:1});
    assert.equal(s.bookings.at(-1).rate,250000);
  });
  test('[TRC] weekend pair → two tariff rows; resolution by night (Fri / Sat / Sun weekend)',()=>{
    let s=seed();s=draft(s,[{kind:'room',roomType:'Deluxe',daySet:'weekday',rateExcl:300000},{kind:'room',roomType:'Deluxe',daySet:'weekend',rateExcl:360000}],'2026-11-02');
    s=execute(s,'owner',{type:'TRC_PUBLISH',id:last(s).id});
    assert.equal(s.tariffs.filter(t=>t.type==='Deluxe'&&t.effectiveFrom==='2026-11-02').length,2);
    const r=d=>M.rateFor(s,room(s,'Deluxe'),d);
    assert.equal(r('2026-11-02'),300000,'Mon');assert.equal(r('2026-11-05'),300000,'Thu');
    assert.equal(r('2026-11-06'),360000,'Fri');assert.equal(r('2026-11-07'),360000,'Sat');assert.equal(r('2026-11-08'),360000,'Sun');
    assert.equal(M.trcNightKind('2026-11-06',[5,6,0]),'weekend');
  });
  test('[TRC] blank child price publishes nothing; adult extra writes the occupancy master dated',()=>{
    let s=seed();s=draft(s,[{kind:'occupancy',roomType:'Deluxe',occKind:'adult',rateExcl:90000},{kind:'occupancy',roomType:'Deluxe',occKind:'child',rateExcl:null,ageFrom:5,ageTo:12}],'2026-11-01');
    const v=last(s);const plan=M.trcPublishPlan(s,v);
    assert.equal(plan.writes.length,1);assert.equal(plan.writes[0].master,'occupancyCharges');assert.ok(plan.skipped.some(x=>/child/i.test(x.line)));
    s=execute(s,'owner',{type:'TRC_PUBLISH',id:v.id});
    const occ=M.rmsOccFor(s,'Deluxe');assert.equal(M.mstMoneyOn(occ.extraAdult,'2026-11-01'),90000);assert.equal(M.mstMoneyOn(occ.extraAdult,'2026-10-31'),80000);
    assert.equal(M.mstMoneyOn(occ.extraChild,'2026-11-01'),40000,'child unchanged');
  });
  test('[TRC] CP line writes the meal plan add-on per guest = (CP − EP) ÷ 2',()=>{
    let s=seed();s=draft(s,[{kind:'room',roomType:'Deluxe',plan:'EP',rateExcl:300000},{kind:'room',roomType:'Deluxe',plan:'CP',rateExcl:340000}],'2026-11-01');
    s=execute(s,'owner',{type:'TRC_PUBLISH',id:last(s).id});
    const cp=M.mstRows(s,'mealPlans').find(r=>r.code==='CP');assert.equal(M.mstMoneyOn(cp.addOn,'2026-11-01'),20000);assert.equal(M.mstMoneyOn(cp.addOn,'2026-10-31'),30000);
  });
  test('[TRC] Manager proposes → Owner publishes (approval executed); Manager cannot publish',()=>{
    let s=draft(seed(),[{kind:'room',roomType:'Suite',rateExcl:480000}],'2026-11-01','manager');const id=last(s).id;
    assert.throws(()=>execute(s,'manager',{type:'TRC_PUBLISH',id}),e=>e.code==='FORBIDDEN');
    s=execute(s,'manager',{type:'TRC_PROPOSE',id});const a=s.approvals.at(-1);
    assert.equal(a.kind,'tariff-card');assert.equal(a.target,id);assert.equal(last(s).status,'proposed');
    s=execute(s,'owner',{type:'TRC_PUBLISH',id});assert.equal(s.approvals.find(x=>x.id===a.id).status,'Executed');assert.equal(M.rateFor(s,room(s,'Suite'),'2026-11-01'),480000);
  });
  test('[TRC] Owner rejects a proposal with a reason',()=>{
    let s=draft(seed(),[{kind:'room',roomType:'Suite',rateExcl:480000}],'2026-11-01','manager');const id=last(s).id;s=execute(s,'manager',{type:'TRC_PROPOSE',id});
    assert.throws(()=>execute(s,'owner',{type:'TRC_REJECT',id}),/reason/);
    s=execute(s,'owner',{type:'TRC_REJECT',id,reason:'Too high'});assert.equal(last(s).status,'rejected');assert.equal(s.approvals.at(-1).status,'Rejected');
  });
  test('[TRC] Housekeeping / maintenance / reception cannot draft; housekeeping cannot even view',()=>{
    const s=seed();
    for(const u of ['housekeeper','maint','reception','cashier','accountant'])assert.throws(()=>draft(s,[{kind:'room',roomType:'Suite',rateExcl:1}],'2026-11-01',u),e=>e.code==='FORBIDDEN',u);
    assert.equal(M.trcCan(s,'housekeeper','view'),false);assert.equal(M.trcCan(s,'maint','view'),false);assert.equal(M.trcCan(s,'reception','view'),true);
    assert.equal(M.trcCan(s,'manager','publish'),false);assert.equal(M.trcCan(s,'owner','publish'),true);
  });
  test('[TRC] refusals: past effective date, published read-only, duplicate line, hall, empty, hand-entered conflict',()=>{
    let s=seed();
    assert.throws(()=>draft(s,[{kind:'room',roomType:'Suite',rateExcl:1}],'2026-10-23'),reason('TRC_PAST_EFFECTIVE'));
    assert.throws(()=>draft(s,[{kind:'room',roomType:'Suite',rateExcl:1},{kind:'room',roomType:'Suite',plan:'EP',rateExcl:2}]),reason('TRC_DUP_LINE'));
    let h=draft(s,[{kind:'hall',hallId:'H1',rateExcl:500000}]);assert.throws(()=>execute(h,'owner',{type:'TRC_PUBLISH',id:last(h).id}),reason('TRC_HALL_NOT_READY'));
    let e=draft(s,[{kind:'occupancy',roomType:'Suite',occKind:'child',rateExcl:null}]);assert.throws(()=>execute(e,'owner',{type:'TRC_PUBLISH',id:last(e).id}),reason('TRC_EMPTY'));
    let c=execute(s,'owner',{type:'TARIFF_SCHEDULE',roomType:'Classic',rate:220000,effectiveFrom:'2026-11-01'});
    c=draft(c,[{kind:'room',roomType:'Classic',rateExcl:230000}]);assert.throws(()=>execute(c,'owner',{type:'TRC_PUBLISH',id:last(c).id}),reason('TRC_CONFLICT'));
    s=draft(s,[{kind:'room',roomType:'Suite',rateExcl:470000}]);const id=last(s).id;s=execute(s,'owner',{type:'TRC_PUBLISH',id});
    assert.throws(()=>execute(s,'owner',{type:'TRC_DRAFT_SAVE',id,lines:[]}),reason('TRC_PUBLISHED_READONLY'));
    assert.throws(()=>execute(s,'owner',{type:'TRC_DISCARD',id}),reason('TRC_PUBLISHED_READONLY'));
  });
  test('[TRC] a later version on the same date replaces the earlier card rows and supersedes it',()=>{
    let s=draft(seed(),[{kind:'room',roomType:'Suite',rateExcl:470000}]);const a=last(s).id;s=execute(s,'owner',{type:'TRC_PUBLISH',id:a});
    s=draft(s,[{kind:'room',roomType:'Suite',rateExcl:490000}]);const b=last(s).id;s=execute(s,'owner',{type:'TRC_PUBLISH',id:b});
    assert.equal(M.trcVersion(s,a).status,'superseded');assert.equal(M.trcVersion(s,a).supersededBy,b);
    assert.equal(s.tariffs.filter(t=>t.type==='Suite'&&t.effectiveFrom==='2026-11-01').length,1);assert.equal(M.rateFor(s,room(s,'Suite'),'2026-11-01'),490000);
    assert.equal(M.trcVersion(s,b).publishLog[0].before,470000);
  });
  test('[TRC] DEC-198 CSV import reproduces the card at room GST 5 % (DEC-239; display within ₹0.01; CP / 3rd guest / breakfast)',()=>{
    let s=seed();s=execute(s,'owner',{type:'TRC_IMPORT',files:files(),effectiveFrom:'2026-11-01'});
    const v=last(s);assert.equal(v.status,'draft');assert.equal(v.effectiveFrom,'2026-11-01');assert.ok(v.lines.every(l=>l.daySet==='all'));
    const card={LUX:[3300,3500],PRM:[2800,3000],DLX:[2000,2200],TWN:[2200,2400],NAC:[1500,1700]};
    for(const [t,[ep,cp]] of Object.entries(card)){
      const e=v.lines.find(l=>l.kind==='room'&&l.roomType===t&&l.plan==='EP'),c=v.lines.find(l=>l.kind==='room'&&l.roomType===t&&l.plan==='CP');
      assert.ok(Math.abs(e.displayIncl-ep*100)<=1,`${t} EP ${e.displayIncl}`);assert.ok(Math.abs(c.displayIncl-cp*100)<=1,`${t} CP ${c.displayIncl}`);
      assert.equal(e.taxBps,500);assert.equal(c.taxBps,500);assert.equal(e.displayIncl,ep*100,`${t} EP exact`);assert.equal(c.displayIncl,cp*100,`${t} CP exact`);}
    assert.equal(v.lines.find(l=>l.roomType==='DLX'&&l.plan==='EP').rateExcl,190476,'Deluxe ₹2,000 incl at 5 % → ₹1,904.76 (DEC-239)');
    const adult=t=>v.lines.find(l=>l.kind==='occupancy'&&l.roomType===t&&l.occKind==='adult');
    assert.ok(Math.abs(adult('LUX').displayIncl-50000)<=1);assert.ok(Math.abs(adult('TWN').displayIncl-40000)<=1);assert.ok(Math.abs(adult('NAC').displayIncl-40000)<=1);
    const child=v.lines.filter(l=>l.kind==='occupancy'&&l.occKind==='child');assert.equal(child.length,5);assert.ok(child.every(l=>l.rateExcl===null&&l.displayIncl===null));
    const bf=v.lines.find(l=>l.kind==='meal'&&l.mealPlan==='CP');assert.equal(bf.rateExcl,9524);assert.equal(bf.taxBps,500);assert.ok(Math.abs(2*bf.displayIncl-20000)<=2,'breakfast ₹200 a room');
    assert.throws(()=>execute(s,'manager',{type:'TRC_IMPORT',files:files()}),e=>e.code==='FORBIDDEN');
  });
  // CU-GST5: per-line GST split of the committed rate-card files (owner 7 Nov 2026: rooms + breakfast 5 %, halls 18 %; DEC-198 prices incl. GST, DEC-239)
  test('[TRC] CU-GST5 split per line: room + breakfast 5 %, hall 18 %; card CSVs carry exclusive = incl / 1.05, never / 1.18',()=>{
    let s=seed();s=execute(s,'owner',{type:'TRC_IMPORT',files:files(),effectiveFrom:'2026-11-01'});
    const v=last(s);const incl={LUX:[3300,3500],PRM:[2800,3000],DLX:[2000,2200],TWN:[2200,2400],NAC:[1500,1700]};
    for(const [t,[ep,cp]] of Object.entries(incl))for(const [plan,price] of [['EP',ep],['CP',cp]]){
      const l=v.lines.find(x=>x.kind==='room'&&x.roomType===t&&x.plan===plan);
      assert.equal(l.taxBps,500,`${t} ${plan} room GST 5 %`);assert.equal(l.displayIncl,price*100,`${t} ${plan} incl price unchanged`);
      assert.equal(l.rateExcl,M.trcExclusiveFromInclusive(price*100,500),`${t} ${plan} excl = incl / 1.05`);
      assert.notEqual(l.rateExcl,M.trcExclusiveFromInclusive(price*100,1800),`${t} ${plan} not the 18 % split`);
      }
    const bf=v.lines.find(x=>x.kind==='meal'&&x.mealPlan==='CP');assert.equal(bf.taxBps,500,'breakfast 5 %');assert.equal(bf.rateExcl,M.trcExclusiveFromInclusive(10000,500),'breakfast ₹100 a guest incl → ₹95.24');
    assert.equal(M.trcPriceLine(s,{kind:'hall',rateExcl:1000000},'2026-11-01','H1').taxBps,1800,'hall 18 % (banquet)');
    // Open (CU-GST5 default): the 3rd-guest lines stay "other services" 18 % exactly as the occupancy master posts them (unchanged rate rule); see cu-gst5-DONE.md
    {const ad=v.lines.filter(x=>x.kind==='occupancy'&&x.occKind==='adult');assert.equal(ad.length,5,'one 3rd-guest line per room type');assert.ok(ad.every(x=>x.taxBps===1800));}
  });
  test('[TRC] nothing reads the card for a price: deleting the slice leaves rates unchanged',()=>{
    let s=draft(seed(),[{kind:'room',roomType:'Suite',rateExcl:470000}]);s=execute(s,'owner',{type:'TRC_PUBLISH',id:last(s).id});
    const r=M.rateFor(s,room(s,'Suite'),'2026-11-03');const t={...s};delete t.tariffCardX;assert.equal(M.rateFor(t,room(t,'Suite'),'2026-11-03'),r);
  });
  test('[TRC] weekend days: Owner sets the default; Manager refused',()=>{
    let s=execute(seed(),'owner',{type:'TRC_WEEKEND_SET',days:[5,6]});assert.deepEqual(M.trcSlice(s).weekendDaysDefault,[5,6]);
    assert.throws(()=>execute(s,'manager',{type:'TRC_WEEKEND_SET',days:[6]}),e=>e.code==='FORBIDDEN');
    s=draft(s,[{kind:'room',roomType:'Deluxe',daySet:'weekday',rateExcl:300000},{kind:'room',roomType:'Deluxe',daySet:'weekend',rateExcl:360000}],'2026-11-02');
    s=execute(s,'owner',{type:'TRC_PUBLISH',id:last(s).id});assert.equal(M.rateFor(s,room(s,'Deluxe'),'2026-11-08'),300000,'Sunday is a weekday night now');
  });
  test('[TRC] an absent slice restores unchanged (SCHEMA 13, no migration)',()=>{const s=seed();assert.equal(s.tariffCardX,undefined);assert.equal(M.trcSlice(s).versions.length,0);assert.deepEqual(M.trcSlice(s).weekendDaysDefault,[5,6,0]);});
}
