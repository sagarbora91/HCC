// AF2-COUNTER-CAPS: the prototype's counter bill carries the server's two caps (unit price at most Rs 1 crore, one bill before discount at most
// Rs 10 crore) with the same refusal wording; integer paise only; the discount is checked after the cap. Names prefixed [AF2-CTR-CAPS].
import {readFileSync} from 'node:fs';
export function register({test,M,assert}){
  const {seed,execute}=M;
  const T='[AF2-CTR-CAPS] ';
  const UNIT_TEXT='The unit price can be at most ₹1,00,00,000.00. Check the amount (it is in paise).';
  const BILL_TEXT='One counter bill can be at most ₹10,00,00,000.00. Check the quantities and prices, or split the bill.';
  const party={name:'Walk-in Guest',phone:'+91 90000 11111'};
  const line=(unit,qty=1)=>({label:'Item',unit,qty,gstCategory:'other'});
  const issue=(s,lines,extra={})=>execute(s,'owner',{type:'CTR_BILL_ISSUE',party,lines,settlement:{mode:'paid',method:'UPI',reference:'UPI-1'},...extra});
  const refused=(s,lines,re,extra={})=>{const before=JSON.stringify(s.counterX??null);
    assert.throws(()=>issue(s,lines,extra),re);assert.equal(JSON.stringify(s.counterX??null),before,'nothing written');};

  test(T+'constants match the server: Rs 1 crore per unit, Rs 10 crore per bill, in paise',()=>{
    assert.equal(M.CTR_UNIT_MAX_PAISE,1_000_000_000);assert.equal(M.CTR_BILL_MAX_PAISE,10_000_000_000);
    assert.equal(M.CTR_UNIT_CAP_TEXT,UNIT_TEXT);assert.equal(M.CTR_BILL_CAP_TEXT,BILL_TEXT);
  });
  test(T+'unit price: exactly the cap passes, one paise over refuses with the server wording',()=>{
    const s=seed();
    const t=issue(s,[line(1_000_000_000)]);assert.equal(t.counterX.bills.at(-1).lines[0].unit,1_000_000_000);
    refused(s,[line(1_000_000_001)],/Line 1: The unit price can be at most ₹1,00,00,000\.00\. Check the amount \(it is in paise\)\./);
    const l=M.ctrLineFrom(s,line(1_000_000_001),s.date,'L1');assert.equal(l.error,UNIT_TEXT);
  });
  test(T+'refusal is a VALIDATION error (same code as the server)',()=>{
    const s=seed();let e;try{issue(s,[line(2_000_000_000)]);}catch(x){e=x;}
    assert.ok(e);assert.equal(e.code??e.error?.code,'VALIDATION');
  });
  test(T+'bill: exactly Rs 10 crore passes, one paise over refuses',()=>{
    const s=seed();
    const ten=Array.from({length:10},()=>line(1_000_000_000));
    const t=issue(s,ten);assert.equal(t.counterX.bills.at(-1).net,10_000_000_000);
    refused(s,[...ten,line(1)],/One counter bill can be at most ₹10,00,00,000\.00\. Check the quantities and prices, or split the bill\./);
    assert.equal(M.ctrBillRefusal(ten),'');assert.equal(M.ctrBillRefusal([...ten.map(l=>({qty:l.qty,unit:l.unit})),{qty:1,unit:1}]),BILL_TEXT);
  });
  test(T+'qty 99 x a big price refuses; qty 99 x a price at the bill cap boundary passes',()=>{
    const s=seed();
    refused(s,[line(1_000_000_000,99)],/at most ₹10,00,00,000\.00/);
    refused(s,[line(101_010_102,99)],/at most ₹10,00,00,000\.00/);          // 99 x 101010102 = 10,000,000,098 > cap
    const t=issue(s,[line(101_010_101,99)]);assert.equal(t.counterX.bills.at(-1).net,9_999_999_999); // just under
  });
  test(T+'many lines: 30 lines each at the unit cap refuse as a bill; a sum of small lines up to the cap passes',()=>{
    const s=seed();
    refused(s,Array.from({length:30},()=>line(1_000_000_000,99)),/at most ₹10,00,00,000\.00/);
    const many=Array.from({length:30},()=>line(333_333_333)); // 9,999,999,990
    assert.equal(issue(s,many).counterX.bills.at(-1).net,9_999_999_990);
    refused(s,[...many.slice(0,29),line(333_333_344)],/at most ₹10,00,00,000\.00/);
  });
  test(T+'discount is applied after the cap: a discount cannot bring an over-cap bill under it, and a capped bill can be discounted',()=>{
    const s=seed();
    const ten=Array.from({length:10},()=>line(1_000_000_000));
    refused(s,[...ten,line(1)],/at most ₹10,00,00,000\.00/,{discount:{amount:5_000_000_000,reason:'x'}});
    const t=issue(s,ten,{discount:{amount:5_000_000_000,reason:'Owner'}});assert.equal(t.counterX.bills.at(-1).net,5_000_000_000);
  });
  test(T+'no overflow with JS numbers: huge, exponent and unsafe values are refused, never written',()=>{
    const s=seed();
    refused(s,[line(Number.MAX_SAFE_INTEGER)],/at most ₹1,00,00,000\.00/);
    refused(s,[line(1e30)],/at most ₹1,00,00,000\.00/);
    refused(s,[line(Infinity)],/whole paise/);
    assert.equal(M.ctrBillRefusal([{qty:99,unit:Number.MAX_SAFE_INTEGER}]),BILL_TEXT);
    assert.equal(M.ctrBillRefusal([{qty:1,unit:Number.MAX_SAFE_INTEGER},{qty:1,unit:Number.MAX_SAFE_INTEGER}]),BILL_TEXT);
    assert.equal(M.ctrBillRefusal([]),'');
  });
  test(T+'catalogue priced lines are capped too (a given unit over the cap on a catalogue item)',()=>{
    const s=seed();
    assert.equal(M.ctrLineFrom(s,{extraId:'ext-laundry',unit:1_000_000_001},s.date,'L1').error,UNIT_TEXT);
    assert.ok(!('error' in M.ctrLineFrom(s,{extraId:'ext-laundry',unit:1_000_000_000},s.date,'L1')));
  });
  for(const [name,file] of [['desktop','src/features/lfmoney/CounterBills.tsx'],['phone','src/features/lfmoney/Mobile.tsx']]){
    test(T+name+': the screen shows the refusal and blocks Issue (ctrBillRefusal on the good lines, err disables the button)',()=>{
      const src=readFileSync(file,'utf8');
      assert.match(src,/ctrBillRefusal\(good\)/);assert.match(src,/const err=lineErr\?\?/);assert.match(src,/disabled=\{busy\|\|!!err\|\|/);
      assert.match(src,/\{err&&<p className="muted" role="status">\{err\.error\}<\/p>\}/);
    });
  }
}
