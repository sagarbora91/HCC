// F8-FIX · split-folio money fixes, prototype side (server twins: 08 Production/tests/Hcc.Integration.Tests/F8FixTests.cs).
// The prototype already sums every folio in CANCEL (totals(b) reads all receipts of the booking) and already supports D7 at posting and guest
// credit on an Udhaar folio; these tests pin that behaviour so the two twins stay equal. Fictional data on the BASE fixture.
export async function register({test,M,assert}){
  const {seed,execute,totals}=M;
  const addDays=(d,n)=>{const x=new Date(d+'T00:00:00Z');x.setUTCDate(x.getUTCDate()+n);return x.toISOString().slice(0,10);};
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  const reason=fn=>{try{fn();}catch(e){return e.details?.reason??e.message;}assert.fail('expected a refusal');};
  const refusal=fn=>{try{fn();}catch(e){return e;}assert.fail('expected a refusal');};

  function book(s,nights=1,arrival=s.date){
    const dep=addDays(arrival,nights);
    const room=s.rooms.find(r=>r.type==='Deluxe'&&r.rate===300000&&!r.blocked&&r.hk==='Ready'&&M.available(s,r.id,arrival,dep));
    assert.ok(room,'a free Deluxe room at ₹3,000');
    s=execute(s,'owner',{type:'BOOK',guest:'F8 Fix Guest (fictional)',room:room.id,arrival,departure:dep,people:1});
    return {s,id:s.bookings.at(-1).id};
  }

  test('[F8-FIX] cancel guard: an advance paid on folio B before arrival blocks the cancel (all folios are summed)',()=>{
    let {s,id}=book(seed(),1,addDays(seed().date,2));
    s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'guest',name:'Friend Payer (fictional)'},routes:['extras'],reason:'Friend pays the extras'});
    s=execute(s,'owner',{type:'PAYMENT',id,amount:100000,method:'UPI',reference:'UPI-ADV-B',folio:'B'});
    const b=bk(s,id);
    assert.equal(totals(M.sfView(b,'A')).paid,0,'nothing on folio A');
    assert.equal(totals(M.sfView(b,'B')).paid,100000,'₹1,000 on folio B');
    const e=refusal(()=>execute(s,'owner',{type:'CANCEL',id,reason:'Guest cancelled'}));
    assert.match(e.message,/Refund the advance/);
    assert.equal(bk(s,id).status,'Confirmed');
  });

  test('[F8-FIX] M2 twin, variant (c): an extra onto the company folio over the limit is refused, the same onto A passes',()=>{
    let s=execute(seed(),'owner',{type:'GRP_COMPANY_SAVE',name:'Deccan Agro Ltd (fictional)',gstin:'27AAPFU0939F1ZV',city:'Latur',creditLimit:1000000});
    const co=s.groupsX.companies.at(-1).id;
    let id;({s,id}=book(s));
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'company',ref:co},routes:['room'],reason:'Company pays the room'});
    const e=refusal(()=>execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:600000,gst:'laundry',folio:'B'}));
    assert.match(e.message,/Credit limit exceeded for Deccan Agro Ltd \(fictional\)/);
    assert.match(e.message,/split folio/);
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:600000,gst:'laundry',folio:'A'});
    assert.equal(bk(s,id).lines.filter(l=>l.folio==='A'&&l.label==='Laundry').length,1);
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry small',amount:50000,gst:'laundry',folio:'B'});
    assert.equal(bk(s,id).lines.filter(l=>l.folio==='B').length,1,'a line that fits passes');
  });

  test('[F8-FIX] guest credit on an Udhaar folio is requested for that folio; on a plain guest folio it is refused SF_PAYER_KIND',()=>{
    let {s,id}=book(seed());
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    const payer=(s.guests??[]).find(g=>g.id!==bk(s,id).guestId);
    assert.ok(payer,'another guest profile to pay on Udhaar');
    s=execute(s,'owner',{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:payer.id,limit:1000000,active:true});
    s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'udhaar',ref:payer.id},routes:['extras'],reason:'Udhaar customer pays the extras'});
    s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'guest',name:'Other Person (fictional)'},routes:[]});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry'});
    assert.equal(totals(M.sfView(bk(s,id),'B')).balance,59000,'₹500 + 18 % routed to the Udhaar folio B');
    assert.equal(reason(()=>execute(s,'reception',{type:'GUEST_CREDIT_REQUEST',id,folio:'C',reason:'Wrong folio'})),'SF_PAYER_KIND');
    s=execute(s,'reception',{type:'GUEST_CREDIT_REQUEST',id,folio:'B',reason:'Regular, pays monthly'});
    const a=s.approvals.at(-1);
    assert.deepEqual([a.kind,a.amount,a.meta.folio,a.meta.guestId],['guest-credit',59000,'B',payer.id],'the Udhaar payer, not the stay guest');
  });
}
