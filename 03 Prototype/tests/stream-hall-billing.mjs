// F6 H1 · hall billing (contract §5, spec §21). HB-GOLDEN: enquiry → quotation v2 (5 % Manager discount) → accept → ₹20,000 UPI advance →
// event day 160 actual plates → HPE invoice with the advance taken off → balance by company credit → ledgers balance. Same figures as
// 08 Production/tests/Hcc.Hall.Tests/BillingTests.cs (one truth table). Names prefixed [HB].
import {hallFixtures} from './_hall.mjs';
export function register({test,M,assert}){
  const {goldenLines,quote,at,ledger}=hallFixtures(M);
  const golden=()=>{
    let [s,qid]=quote(M.seed());
    s=M.execute(s,'manager',{type:'QUO_ACCEPT',id:qid,version:1,advance:{amountPaise:2000000,method:'UPI',reference:'UPI-HB-1'},key:'hb-accept'});
    const e0=s.hallX.events.at(-1);
    const d1=s.date;s=at(s,'2026-12-05');
    s=M.execute(s,'reception',{type:'HALL_EVENT_CLOSE',eventId:e0.id,actualPax:160,settle:{mode:'COMPANY',company:'CO-1'}});
    return {s,e:s.hallX.events.find(e=>e.id===e0.id),d1};
  };
  test('[HB] HB-GOLDEN: v2 quote total ₹1,37,322.50; 160 plates billed; HPE invoice ₹1,44,609; advance ₹20,000 applied; balance to 1110/CO-1; every voucher balances',()=>{
    const q=M.hallPrice(M.seed(),goldenLines(5),'2026-10-24').totals;
    assert.deepEqual([q.taxablePaise,q.cgstPaise,q.sgstPaise,q.totalPaise],[11637500,1047375,1047375,13732250]);
    const {s,e,d1}=golden(),inv=e.invoice;
    assert.equal(e.status,'BILLED');assert.match(inv.no,/^(HPE\/2627|DEMO\/2026-27)\/\d{4}$/,"the tax-invoice (INV) series, as room invoices — HPE on the server (DEC-197), DEMO in the prototype seed");assert.equal(inv.billedPax,160);
    assert.deepEqual(inv.lines.map(l=>[l.kind,l.qty,l.taxablePaise,l.taxBps,l.cgstPaise,l.sgstPaise]),[['HALL',1,1425000,1800,128250,128250],['MENU',160,9880000,1800,889200,889200],['EXTRA',1,950000,1800,85500,85500]]);
    assert.deepEqual([inv.taxablePaise,inv.cgstPaise,inv.sgstPaise,inv.totalPaise,inv.advanceAppliedPaise,inv.balancePaise],[12255000,1102950,1102950,14460900,2000000,12460900]);
    const all=[d1,'2026-12-05'].flatMap(d=>M.accLiveVouchers(s,d).filter(v=>v.source.kind==='hall'));
    assert.deepEqual(all.map(v=>v.source.sub).sort(),['hall-advance','hall-advance-apply','hall-balance','hall-invoice']);
    for(const v of all){const t=M.accVoucherTotals(v);assert.equal(t.debit,t.credit,v.narration);assert.deepEqual(v.unmapped,[]);}
    const L=ledger(s,[d1,'2026-12-05']);
    assert.equal(L['1010'],2000000,'UPI Dr ₹20,000');assert.equal(L['2000'],0,'2000: Cr ₹20,000 advance, Dr ₹20,000 applied');
    assert.equal(L['4400'],-2375000,'4400 Cr hall + decor');assert.equal(L['4200'],-9880000,'4200 Cr food');
    assert.equal(L['2110'],-1102950);assert.equal(L['2120'],-1102950);assert.equal(L['1100'],0,'1100 cleared');assert.equal(L['1110/CO-1'],12460900,'company Dr balance');
    const apply=all.find(v=>v.source.sub==='hall-advance-apply');assert.deepEqual(apply.entries.find(x=>x.account==='2000'),{account:'2000',debit:2000000,credit:0});
  });
  test('[HB] DEC-142: the advance voucher is Dr tender · Cr 2000 only — no GST lines',()=>{
    const {s,d1}=golden(),v=M.accLiveVouchers(s,d1).find(v=>v.source.sub==='hall-advance');
    assert.deepEqual(v.entries,[{account:'1010',debit:2000000,credit:0},{account:'2000',debit:0,credit:2000000}]);assert.equal(v.type,'Receipt');
  });
  test('[HB] advance plan 25 % / 50 % at 30 days / balance; the balance row takes the rounding; receipts fill rows in order',()=>{
    const st=M.hallSeed().settings,p=M.hallAdvancePlan(13732250,'2026-12-05','2026-10-24',st);
    assert.deepEqual(p.map(r=>[r.amountPaise,r.due]),[[3433063,'ON_ACCEPT'],[6866125,'2026-11-05'],[3433062,'EVENT_DAY']]);
    assert.equal(p.reduce((a,r)=>a+r.amountPaise,0),13732250);
    assert.equal(M.hallAdvancePlan(100000,'2026-11-01','2026-10-24',st)[1].due,'2026-10-24','second advance at once when the event is nearer than 30 days');
    const a=M.hallAllocate(p,5000000,'R1');assert.deepEqual(a.map(r=>r.paidPaise),[3433063,1566937,0]);
    const {e}=golden();assert.equal(e.advance[0].paidPaise,2000000);assert.equal(e.advance[0].receiptIds.length,1);
  });
  test('[HB] discount limits: Reception 0 %, Manager 10 % passes and 11 % does not, Owner any; a bill extra below list by Reception → HCC_DISCOUNT_LIMIT',()=>{
    const st=M.hallSeed().settings,l=p=>goldenLines(p);
    assert.equal(M.hallDiscountWithin(l(0),M.hallDiscountLimit(st,'Reception')),true);assert.equal(M.hallDiscountWithin(l(1),M.hallDiscountLimit(st,'Reception')),false);
    assert.equal(M.hallDiscountWithin(l(10),M.hallDiscountLimit(st,'Manager')),true);assert.equal(M.hallDiscountWithin(l(11),M.hallDiscountLimit(st,'Manager')),false);
    assert.equal(M.hallDiscountWithin(l(60),M.hallDiscountLimit(st,'Owner')),true);assert.equal(M.hallDiscountBps(l(5)),500);
    let [s,qid]=quote(M.seed());s=M.execute(s,'manager',{type:'QUO_ACCEPT',id:qid});const id=s.hallX.events.at(-1).id;s=at(s,'2026-12-05');
    assert.throws(()=>M.execute(s,'reception',{type:'HALL_EVENT_CLOSE',eventId:id,actualPax:150,extras:[{desc:'DJ',qty:1,ratePaise:400000,listRatePaise:500000}]}),e=>e.code==='HCC_DISCOUNT_LIMIT');
    const ok=M.execute(s,'manager',{type:'HALL_EVENT_CLOSE',eventId:id,actualPax:120,extras:[{desc:'DJ',qty:1,ratePaise:450000,listRatePaise:500000}]}).hallX.events.find(e=>e.id===id);
    assert.equal(ok.invoice.billedPax,150,'guaranteed 150 > actual 120');assert.equal(ok.invoice.settle.mode,'DUE');assert.equal(ok.invoice.balancePaise,ok.invoice.totalPaise);
  });
  test('[HB] cancellation 40 days before keeps 25 % of the quotation total (capped at advances) → 4300 with GST; the rest refunded on Manager approval',()=>{
    let [s,qid]=quote(M.seed(),{dates:['2026-12-03']});
    s=M.execute(s,'reception',{type:'QUO_ACCEPT',id:qid,advance:{amountPaise:5000000,method:'Bank transfer',reference:'NEFT-HB-2'}});const id=s.hallX.events.at(-1).id;
    s=M.execute(s,'reception',{type:'HALL_EVENT_CANCEL',eventId:id,reason:'Family emergency'});
    const c=s.hallX.events.find(e=>e.id===id).cancellation;
    assert.deepEqual([c.daysBefore,c.keptBps,c.keptPaise,c.refundPaise,c.refund],[40,2500,3433063,1566937,'PENDING']);
    assert.equal(M.hallClashes(s.hallX,['V-SAGAR'],['2026-12-03'],'EVENING').length,0,'hall released');
    assert.throws(()=>M.execute(s,'reception',{type:'HALL_REFUND',eventId:id,method:'Bank transfer',reference:'NEFT-R1'}),/Access denied/);
    s=M.execute(s,'manager',{type:'HALL_REFUND',eventId:id,method:'Bank transfer',reference:'NEFT-R1'});
    const vs=M.accLiveVouchers(s,s.date).filter(v=>v.source.kind==='hall'),cv=vs.find(v=>v.source.sub==='hall-cancel'),rv=vs.find(v=>v.source.sub==='hall-refund');
    assert.deepEqual(cv.entries,[{account:'2000',debit:3433063,credit:0},{account:'4300',debit:0,credit:2909375},{account:'2110',debit:0,credit:261844},{account:'2120',debit:0,credit:261844}]);
    assert.deepEqual(rv.entries.map(e=>[e.account.split('/')[0],e.debit,e.credit]),[['2000',1566937,0],['1030',0,1566937]]);
    const L=ledger(s,[s.date]);assert.equal(L['2000'],0,'advance deposit fully used: kept + refunded');
  });
  test('[HB] refunds: no self-approval; Manager up to ₹50,000, Owner above; slabs >60 days keep 0 and <7 days keep all advances',()=>{
    let [s,qid]=quote(M.seed(),{dates:['2026-12-03']});
    s=M.execute(s,'manager',{type:'QUO_ACCEPT',id:qid,advance:{amountPaise:8000000,method:'UPI',reference:'UPI-HB-3'}});const id=s.hallX.events.at(-1).id;
    s=M.execute(s,'manager',{type:'HALL_EVENT_CANCEL',eventId:id,reason:'Date moved'});
    assert.throws(()=>M.execute(s,'manager',{type:'HALL_REFUND',eventId:id,method:'UPI',reference:'UPI-R'}),/own cancellation/);
    assert.equal(s.hallX.events.find(e=>e.id===id).cancellation.refundPaise,8000000-3433063);
    assert.throws(()=>M.execute({...s,users:s.users.map(u=>u.id==='owner'?u:u)},'owner',{type:'HALL_REFUND',eventId:id,method:'UPI'}),/reference/);
    const ok=M.execute(s,'owner',{type:'HALL_REFUND',eventId:id,method:'UPI',reference:'UPI-R'});assert.equal(ok.hallX.events.find(e=>e.id===id).cancellation.refund,'PAID');
    const sl=M.hallSeed().settings.cancelSlabs;assert.equal(M.hallCancelSlab(sl,61).keptBps,0);assert.equal(M.hallCancelSlab(sl,60).keptBps,2500);
    assert.equal(M.hallCancelSlab(sl,29).keptBps,5000);assert.equal(M.hallCancelSlab(sl,6).keptBps,10000);
    assert.deepEqual(M.hallInclusiveSplit(118000,1800),{taxablePaise:100000,taxPaise:18000,cgstPaise:9000,sgstPaise:9000});
  });
  test('[HB] permissions: Cashier takes advances but cannot accept; HK / maintenance cannot touch hall money; idempotent advance key',()=>{
    let [s,qid]=quote(M.seed());
    assert.throws(()=>M.execute(s,'cashier',{type:'QUO_ACCEPT',id:qid}),/Access denied/);
    s=M.execute(s,'reception',{type:'QUO_ACCEPT',id:qid});const id=s.hallX.events.at(-1).id;
    const a=M.execute(s,'cashier',{type:'HALL_ADVANCE_RECEIVE',eventId:id,amountPaise:100000,method:'Cash',key:'hb-adv-1'});
    assert.deepEqual(M.execute(a,'cashier',{type:'HALL_ADVANCE_RECEIVE',eventId:id,amountPaise:100000,method:'Cash',key:'hb-adv-1'}),a);
    for(const u of ['housekeeper','maint'])assert.throws(()=>M.execute(s,u,{type:'HALL_ADVANCE_RECEIVE',eventId:id,amountPaise:100,method:'Cash'}),/Access denied/);
    assert.equal(M.hallSeesMoney('Housekeeper'),false);assert.equal(M.hallSeesMoney('Maintenance'),false);assert.equal(M.hallSeesMoney('Cashier'),true);
    assert.throws(()=>M.execute(s,'reception',{type:'HALL_EVENT_CLOSE',eventId:id,actualPax:150}),/on or after the event day/);
  });
  test('[HB] mapping switch Q6: hall.foodLedger 4400 puts the food in 4400; ROOM lines go to 4000 at the room rule',()=>{
    let {s}=golden();s={...s,hallX:{...s.hallX,settings:{...s.hallX.settings,foodLedger:'4400'}}};
    const L=ledger(s,['2026-12-05']);assert.equal(L['4400'],-(2375000+9880000));assert.equal(L['4200'],undefined);
    assert.equal(M.hallTaxBps(M.seed(),'ROOM','2026-12-05',400000),500);assert.equal(M.hallTaxBps(M.seed(),'ROOM','2026-12-05',800000),1800);
  });
}
