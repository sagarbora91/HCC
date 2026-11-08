// F8E-R8 · the company-credit half of SF-CONTRACT §8 R8 / §7 D7 on rpt.money.company-credit (P2-B), prototype side.
// R8: the report attributes to a company the lines of its company folio on a split stay, the whole stay otherwise. On this report that means:
//  - "Used" = the D7 exposure (§7.2): receivable + for a live split stay only the company folio (posted lines + agreed unposted nights + GST
//    − receipts on that folio); the guest folio never counts;
//  - the statement lists the company-credit receipt of the company folio (I5) with that folio's invoice number (F8-INT L1);
//  - a night posting is never refused by the limit (§7.3, SFQ-6 default): the report then shows the company over its limit.
// Golden stay of §5.7 / tests/sf-golden.fixture.json (Deccan Agro, 3 nights at ₹3,000 on folio B = ₹9,450.00 = 945000 paise). Integer paise only.
// Owner-question defaults relied on: SFQ-6 (night posted, company credit refused until the limit is raised). Fictional data.
import {readFileSync} from 'node:fs';
const FX=JSON.parse(readFileSync(new URL('./sf-golden.fixture.json',import.meta.url),'utf8'));

export function register({test,M,assert}){
  const {seed,execute,totals}=M;
  const T='[F8E-R8] ';
  const CR='rpt.money.company-credit';
  const addDays=(d,n)=>{const x=new Date(d+'T00:00:00Z');x.setUTCDate(x.getUTCDate()+n);return x.toISOString().slice(0,10);};
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  const run=(s,p={})=>M.runReport(s,'owner',CR,p,{all:true});
  const rowFor=(s,name)=>run(s).rows.find(r=>r.kind==='row'&&r.cells.company===name);
  const statement=(s,co)=>run(s,{company:co}).sections[1].rows;
  const refusal=fn=>{try{fn();}catch(e){return e;}assert.fail('expected a refusal');};
  const B_TOTAL=FX.folioB.totalPaise,LIMIT=FX.company.creditLimitPaise;

  /** The fixture stay up to the postings. split=false: the same stay billed whole to the company (booking linked to the account), no split. */
  function golden({split=true,nights=true,limitBeforeNights}={}){
    let s=seed();
    s=execute(s,'owner',{type:'GRP_COMPANY_SAVE',name:FX.company.name,gstin:FX.company.gstin,city:'Latur',creditLimit:LIMIT});
    const co=s.groupsX.companies.at(-1).id;
    const dep=addDays(s.date,FX.room.nights);
    const room=s.rooms.find(r=>r.type===FX.room.type&&r.rate===FX.room.ratePaise&&!r.blocked&&r.hk==='Ready'&&M.available(s,r.id,s.date,dep));
    assert.ok(room,'a free '+FX.room.type+' room at the fixture rate');
    s=execute(s,'owner',{type:'BOOK',guest:'Sf Golden Guest',room:room.id,arrival:s.date,departure:dep,people:1,...(split?{}:{company:{account:co}})});
    const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    if(split)s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'company',ref:co},routes:['room'],reason:'Company pays the room'});
    s=execute(s,'owner',{type:'PAYMENT',id,amount:FX.payments.advanceUpiPaise,method:'UPI',reference:'UPI-R8-1'});
    if(limitBeforeNights!==undefined)s=execute(s,'owner',{type:'GRP_LIMIT_SET',company:co,limit:limitBeforeNights,reason:'R8 test: limit lowered'});
    if(nights)s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:FX.postings.laundryPaise,gst:'laundry'});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Room service food',amount:FX.postings.foodPaise,gst:'food'});
    return {s,id,co};
  }
  function settle({s,id}){
    const balA=totals(M.sfView(bk(s,id),'A')).balance;
    if(balA>0)s=execute(s,'owner',{type:'PAYMENT',id,amount:balA,method:'Cash',folio:'A'});
    s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id});
    s=execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    return execute(s,'owner',{type:'CHECKOUT',id});
  }

  test(T+'in house: Used = folio B only (945000 paise = ₹9,450.00), the guest folio A never counts; the same before and after the nights post',()=>{
    assert.equal(B_TOTAL,945000);assert.equal(FX.room.ratePaise*FX.room.nights+FX.folioB.cgstPaise+FX.folioB.sgstPaise,B_TOTAL);
    // before the night posting: the agreed unposted nights on folio B (room routed there) are the estimate
    const pre=golden({nights:false});
    assert.equal(totals(M.sfView(bk(pre.s,pre.id),'B')).total,0,'nothing posted on B yet');
    let r=rowFor(pre.s,FX.company.name);
    assert.ok(Number.isSafeInteger(r.cells.used));
    assert.equal(r.cells.used,B_TOTAL,'estimate = 3 nights + GST');
    // after the night posting
    const g=golden(),b=bk(g.s,g.id);
    const tA=totals(M.sfView(b,'A')),tB=totals(M.sfView(b,'B'));
    assert.equal(tB.total,B_TOTAL);assert.equal(tB.paid,0);
    assert.equal(tA.total,FX.folioA.totalPaise);assert.ok(tA.balance>0,'folio A still owes, yet it is not the company’s');
    r=rowFor(g.s,FX.company.name);
    assert.equal(r.cells.limit,LIMIT);
    assert.equal(r.cells.used,B_TOTAL,'Used = folio B, not the stay');
    assert.notEqual(r.cells.used,totals(b).balance,'not the whole stay balance');
    assert.equal(r.cells.remaining,LIMIT-B_TOTAL);
    assert.equal(r.cells.status,'Within limit');
    // the figure is the D7 exposure helper and the receivable is still 0
    const c=M.grpSlice(g.s).companies.find(x=>x.id===g.co);
    assert.equal(M.grpExposure(g.s,c),B_TOTAL);assert.equal(M.sfFolioExposure(g.s,b,'B'),B_TOTAL);assert.equal(M.grpReceivable(g.s,c),0);
    // the statement has no credit yet (opening 0, closing 0)
    const st=statement(g.s,g.co);
    assert.equal(st.length,2);assert.equal(st[0].cells.balance,0);assert.equal(st.at(-1).cells.balance,0);
  });

  test(T+'R8 otherwise-branch: the same stay billed whole to the company (no split) counts the whole stay, not 945000',()=>{
    const g=golden({split:false}),b=bk(g.s,g.id);
    assert.ok(!b.folios?.length,'single-folio stay');
    const whole=totals(b).total-FX.payments.advanceUpiPaise;
    assert.equal(whole,FX.stay.totalPaise-FX.payments.advanceUpiPaise);
    const r=rowFor(g.s,FX.company.name);
    assert.equal(r.cells.used,whole,'whole stay less what the guest paid');
    assert.equal(r.cells.remaining,LIMIT-whole);
  });

  test(T+'after check-out: Used = the receivable 945000; the statement shows one company credit of 945000 under folio B’s invoice number',()=>{
    const g=golden(),s=settle(g),b=bk(s,g.id);
    assert.equal(b.status,'Departed');
    const [fa,fb]=b.folios;
    const cr=b.receipts.filter(x=>x.kind==='company-credit');
    assert.equal(cr.length,1);assert.equal(cr[0].folio,'B');assert.equal(cr[0].amount,B_TOTAL);
    const r=rowFor(s,FX.company.name);
    assert.equal(r.cells.used,B_TOTAL);assert.equal(r.cells.remaining,LIMIT-B_TOTAL);
    const c=M.grpSlice(s).companies.find(x=>x.id===g.co);
    assert.equal(M.grpReceivable(s,c),B_TOTAL,'receivable = folio B');
    const st=statement(s,g.co),body=st.filter(x=>x.kind==='row');
    assert.equal(st[0].kind,'subtotal');assert.equal(st[0].cells.balance,0,'opening outstanding');
    assert.equal(body.length,1,'one company credit, the guest folio adds nothing');
    assert.equal(body[0].cells.ref,fb.invoiceNo,'folio B’s own invoice');
    assert.notEqual(body[0].cells.ref,fa.invoiceNo);assert.notEqual(body[0].cells.ref,b.invoiceNo,'not the stay’s first (folio A) number');
    assert.equal(body[0].cells.credit,B_TOTAL);assert.equal(body[0].cells.payment,null);assert.equal(body[0].cells.balance,B_TOTAL);
    const close=st.at(-1);
    assert.equal(close.kind,'total');assert.deepEqual([close.cells.credit,close.cells.payment,close.cells.balance],[B_TOTAL,0,B_TOTAL]);
    assert.ok([r.cells.used,close.cells.balance,close.cells.credit].every(Number.isSafeInteger),'integer paise');
  });

  test(T+'night posting is never refused by the limit (POST_NIGHTS): nights land on B, the report shows Over limit; extras onto B and company credit are refused until the Owner raises the limit',()=>{
    const low=B_TOTAL-45000; // ₹9,000: one night's GST short
    const g=golden({limitBeforeNights:low});
    let b=bk(g.s,g.id);
    const nights=b.lines.filter(l=>l.kind==='room');
    assert.equal(nights.length,FX.room.nights,'all three nights posted');
    assert.ok(nights.every(l=>l.folio==='B'),'on the company folio');
    let r=rowFor(g.s,FX.company.name);
    assert.equal(r.cells.limit,low);assert.equal(r.cells.used,B_TOTAL);assert.equal(r.cells.status,'Over limit');assert.equal(r.cells.remaining,0);
    // any other posting to the company folio is refused (§7.3), the same onto the guest folio passes
    const e=refusal(()=>execute(g.s,'owner',{type:'CHARGE',id:g.id,label:'Laundry extra',amount:60000,gst:'laundry',folio:'B'}));
    assert.equal(e.code,'INVALID_TRANSITION');assert.match(e.message,/^Credit limit exceeded for /);
    const okA=execute(g.s,'owner',{type:'CHARGE',id:g.id,label:'Laundry extra',amount:60000,gst:'laundry',folio:'A'});
    assert.equal(rowFor(okA,FX.company.name).cells.used,B_TOTAL,'folio A postings never move Used');
    // company credit at check-out refused (SFQ-6) with the D7 text
    let s=g.s;const balA=totals(M.sfView(b,'A')).balance;
    s=execute(s,'owner',{type:'PAYMENT',id:g.id,amount:balA,method:'Cash',folio:'A'});
    const e2=refusal(()=>execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id:g.id}));
    assert.equal(e2.code,'INVALID_TRANSITION');assert.match(e2.message,/^Credit limit exceeded for /);assert.match(e2.message,/split folio/);
    // the Owner raises the limit: the request goes through
    s=execute(s,'owner',{type:'GRP_LIMIT_SET',company:g.co,limit:LIMIT,reason:'R8 test: limit raised'});
    s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id:g.id});
    const ap=s.approvals.at(-1);assert.equal(ap.kind,'company-credit');assert.equal(ap.amount,B_TOTAL);assert.equal(ap.meta.folio,'B');
    r=rowFor(s,FX.company.name);assert.equal(r.cells.used,B_TOTAL);assert.equal(r.cells.status,'Within limit');
  });

  test(T+'night posting is never refused by the limit (NIGHT_POST, the day-close path): tonight’s night lands on B with the company already over',()=>{
    const g=golden({nights:false,limitBeforeNights:100000}); // ₹1,000: below one night
    let s=execute(g.s,'owner',{type:'NIGHT_POST'});
    const b=bk(s,g.id),nights=b.lines.filter(l=>l.kind==='room');
    assert.equal(nights.length,1,'tonight only');assert.equal(nights[0].folio,'B');assert.equal(nights[0].date,s.date);
    const r=rowFor(s,FX.company.name);
    assert.equal(r.cells.used,B_TOTAL,'one night posted + two agreed nights = still the folio estimate');
    assert.equal(r.cells.status,'Over limit');
  });
}
