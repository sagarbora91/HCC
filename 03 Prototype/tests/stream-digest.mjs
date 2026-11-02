// F4-T2 · Owner digest — Owner only; two held drafts per closed day (e-mail with money, WhatsApp counts only); idempotent; never sent.
import {tillClear} from './_till.mjs';
export function register({test,M,assert}){
  const T='[F4-T2 DIGEST] ';
  function closed(){
    let s=M.seed();
    for(const b of s.bookings.filter(b=>b.status==='Confirmed')){s=M.execute(s,'owner',{type:'REGISTER',id:b.id});s=M.execute(s,'owner',{type:'CHECKIN',id:b.id});}
    s=M.execute(s,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});s=M.execute(s,'owner',{type:'NIGHT_POST'});
    s=M.execute(s,'owner',{type:'SHIFT_CLOSE',counted:M.drawer(s)});s=tillClear(M,s);return M.execute(s,'owner',{type:'DAY_CLOSE'});
  }
  test(T+'day close writes an Email and a WhatsApp draft, both held, with the reason',()=>{
    const s=closed(),d=s.digestX.drafts.filter(x=>x.date===s.date);
    assert.equal(d.length,2);assert.deepEqual(d.map(x=>x.channel).sort(),['Email','WhatsApp']);
    for(const x of d){assert.equal(x.status,'held');assert.ok(/F9/.test(x.reason)&&/F5/.test(x.reason));}
    assert.equal(d.find(x=>x.channel==='WhatsApp').countsOnly,true);assert.equal(d.find(x=>x.channel==='Email').countsOnly,false);
  });
  test(T+'WhatsApp draft has counts only: no rupee amounts, no guest names; the e-mail carries money',()=>{
    const s=closed(),w=s.digestX.drafts.find(x=>x.channel==='WhatsApp'),e=s.digestX.drafts.find(x=>x.channel==='Email');
    assert.ok(!/₹|Rs|\d,\d\d\d/.test(w.body));for(const b of s.bookings)assert.ok(!w.body.includes(b.guest),b.guest);
    assert.ok(/₹/.test(e.body));
  });
  test(T+'re-writing the drafts for a date replaces them (idempotent per date and channel)',()=>{
    let s=closed();const n=s.digestX.drafts.length;
    s=M.execute(s,'owner',{type:'DIGEST_REDRAFT',date:s.date});assert.equal(s.digestX.drafts.length,n);
  });
  test(T+'digest card figures match the closing record; Owner only',()=>{
    const s=closed(),c=M.dgsCard(s,s.date),cl=s.closings[s.date];
    assert.equal(c.occupancy.pct,cl.occupancy.occupancy);assert.equal(c.cashAtClose,cl.cash.counted);assert.equal(c.advances.held,cl.deposits.heldAtClose);
    assert.equal(M.dgsCanView(s,'owner'),true);
    for(const u of ['manager','accountant','reception','cashier'])assert.equal(M.dgsCanView(s,u),false,u);
    assert.throws(()=>M.execute(s,'manager',{type:'DIGEST_REDRAFT',date:s.date}),e=>e.code==='FORBIDDEN');
  });
  test(T+'no digest drafts before any day is closed; a v13 save without the slice reads as empty',()=>{
    const s=M.seed();assert.equal(M.dgsSlice(s).drafts.length,0);
  });
}
