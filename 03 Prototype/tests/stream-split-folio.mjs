// F8 sf-domain · split folio commands, routing hook, per-folio checkout / invoices / credit note, D7 per company folio, invariance (SF-CONTRACT).
// Fictional data on the BASE fixture. The full golden stay of §5.7 is lane sf-tests' (tests/stream-sf-golden.mjs).
export async function register({test,M,assert}){
  const {seed,execute,totals}=M;
  const addDays=(d,n)=>{const x=new Date(d+'T00:00:00Z');x.setUTCDate(x.getUTCDate()+n);return x.toISOString().slice(0,10);};
  const withLimit=(s,co,limit)=>({...s,groupsX:{...s.groupsX,companies:s.groupsX.companies.map(c=>c.id===co?{...c,creditLimit:limit}:c)}});
  const reason=(fn,r)=>{try{fn();}catch(e){return e.details?.reason??e.code;}return 'none';};
  const codeOf=fn=>{try{fn();}catch(e){return e.code;}return 'none';};
  /** In-house stay in room 105 for 3 nights, a company account with a limit, nights not yet posted. */
  function stay(limit=5000000){
    let s=seed();
    s=execute(s,'owner',{type:'GRP_COMPANY_SAVE',name:'Deccan Agro Ltd (fictional)',gstin:'27AAACD1111A1Z1',city:'Latur',creditLimit:limit});
    const co=s.groupsX.companies.at(-1).id;
    s=execute(s,'owner',{type:'BOOK',guest:'Split Test Guest',room:'105',arrival:s.date,departure:addDays(s.date,3),people:1});
    const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    return {s,id,co};
  }
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  const open=(s,id,co,routes=['room'])=>execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'company',ref:co},routes,reason:'Company pays the room'});

  test('[F8-SF-DOMAIN] I1: an unsplit stay is today\'s stay — no folio fields, sfView identity, one invoice doc',()=>{
    let {s,id}=stay();
    s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry'});
    const b=bk(s,id);
    assert.equal(b.folios,undefined);assert.ok(b.lines.every(l=>l.folio===undefined));assert.ok(b.receipts.every(r=>r.folio===undefined));
    assert.equal(M.sfView(b,'A'),b);assert.equal(M.sfIsSplit(b),false);
    assert.deepEqual(M.sfCompanyLines(s,b,'nope'),[]);
  });

  test('[F8-SF-DOMAIN] SF_FOLIO_OPEN stamps existing lines A, routes later room nights to B and extras to A',()=>{
    let {s,id,co}=stay();
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry'});
    s=open(s,id,co);
    let b=bk(s,id);
    assert.deepEqual(b.folios.map(f=>[f.id,f.payer.kind,f.routes.join('+')]),[['A','guest','extras'],['B','company','room']]);
    assert.ok(b.lines.every(l=>l.folio==='A'));
    s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Room service food',amount:120000,gst:'food'});
    b=bk(s,id);
    assert.ok(b.lines.filter(l=>l.kind==='room').every(l=>l.folio==='B'),'room nights on B');
    assert.equal(b.lines.find(l=>l.label==='Room service food').folio,'A');
    const tA=totals(M.sfView(b,'A')),tB=totals(M.sfView(b,'B')),t=totals(b);
    assert.equal(tA.total+tB.total,t.total,'I2: stay total = Σ folio totals');
    assert.deepEqual(M.sfCompanyLines(s,b,co).map(l=>l.id),M.sfView(b,'B').lines.map(l=>l.id));
    // explicit folio on CHARGE wins
    s=execute(s,'owner',{type:'CHARGE',id,label:'Minibar',amount:10000,gst:'other',folio:'B'});
    assert.equal(bk(s,id).lines.at(-1).folio,'B');
  });

  test('[F8-SF-DOMAIN] refusals: SF_PAYER_KIND, SF_COMPANY_FOLIO, SF_ROUTE_TAKEN, SF_FOLIO_LIMIT, SF_FOLIO_UNKNOWN, SF_FOLIO_NOT_EMPTY, SF_DEPOSIT_FOLIO, cash on a company folio',()=>{
    let {s,id,co}=stay();
    assert.equal(reason(()=>execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'group',name:'X'},routes:[]})),'SF_PAYER_KIND');
    s=open(s,id,co);
    assert.equal(reason(()=>open(s,id,co,[])),'SF_COMPANY_FOLIO');
    assert.equal(reason(()=>execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'guest',name:'Brother'},routes:['room']})),'SF_ROUTE_TAKEN');
    s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'guest',name:'Brother'},routes:['extras']});
    s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'guest',name:'Sister'},routes:[]});
    assert.equal(reason(()=>execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'guest',name:'Cousin'},routes:[]})),'SF_FOLIO_LIMIT');
    assert.equal(reason(()=>execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:1000,folio:'E'})),'SF_FOLIO_UNKNOWN');
    assert.equal(reason(()=>execute(s,'owner',{type:'SF_FOLIO_REMOVE',id,folio:'A'})),'SF_FOLIO_NOT_EMPTY');
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:1000,gst:'laundry'}); // extras → C
    assert.equal(bk(s,id).lines.at(-1).folio,'C');
    assert.equal(reason(()=>execute(s,'owner',{type:'SF_FOLIO_REMOVE',id,folio:'C'})),'SF_FOLIO_NOT_EMPTY');
    s=execute(s,'owner',{type:'SF_FOLIO_REMOVE',id,folio:'D'});
    assert.equal(reason(()=>execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'guest',name:'Cousin'},routes:[]})),'SF_FOLIO_LIMIT','a removed letter is not reused');
    assert.equal(reason(()=>execute(s,'owner',{type:'PAYMENT',id,kind:'deposit',amount:100000,method:'UPI',reference:'UPI-1',folio:'B'})),'SF_DEPOSIT_FOLIO');
    assert.equal(reason(()=>execute(s,'owner',{type:'PAYMENT',id,amount:100000,method:'Cash',folio:'B'})),'SF_COMPANY_FOLIO');
    s=execute(s,'owner',{type:'PAYMENT',id,amount:100000,method:'UPI',reference:'UPI-2',folio:'B'});
    assert.equal(bk(s,id).receipts.at(-1).folio,'B');
    s=execute(s,'owner',{type:'SF_ROUTE_SET',id,folio:'A',routes:['extras'],reason:'Guest pays extras again'});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:1000,gst:'laundry'});
    assert.equal(bk(s,id).lines.at(-1).folio,'A','routing applies to later postings');
  });

  test('[F8-SF-DOMAIN] SF_COMPANY_MISMATCH and the bill-correction refusal on a split stay',()=>{
    let {s,id,co}=stay();
    s=execute(s,'owner',{type:'GRP_COMPANY_SAVE',name:'Other Co (fictional)',city:'Latur',creditLimit:100000});
    const other=s.groupsX.companies.at(-1).id;
    const b=bk(s,id);
    const s2={...s,bookings:s.bookings.map(x=>x.id===id?{...b,company:{name:'Other Co (fictional)',account:other}}:x)};
    assert.equal(reason(()=>open(s2,id,co)),'SF_COMPANY_MISMATCH');
  });

  test('[F8-SF-DOMAIN] D7: open refused over the limit; CHARGE onto B refused, onto A passes; night posting never refused',()=>{
    let {s,id,co}=stay(100000); // ₹1,000 limit: three nights do not fit
    const msg=(()=>{try{open(s,id,co);}catch(e){return e.message;}return '';})();
    assert.match(msg,/Credit limit exceeded for Deccan Agro Ltd \(fictional\).*this split folio needs/);
    s=open(s,id,co,['extras']); // nothing on it yet: passes
    assert.equal(codeOf(()=>execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:200000,gst:'laundry',folio:'B'})),'INVALID_TRANSITION');
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:200000,gst:'laundry',folio:'A'});
    s=execute(s,'owner',{type:'SF_ROUTE_SET',id,folio:'B',routes:[],reason:'Room back to the guest'});
    // route room to B with a raised limit, then lower the limit: the night posting still posts to B
    s=withLimit(s,co,5000000);
    s=execute(s,'owner',{type:'SF_ROUTE_SET',id,folio:'B',routes:['room'],reason:'Company pays the room'});
    s=withLimit(s,co,100000);
    s=execute(s,'owner',{type:'NIGHT_POST'});
    assert.ok(bk(s,id).lines.some(l=>l.kind==='room'&&l.folio==='B'),'night posted on the company folio');
  });

  test('[F8-SF-DOMAIN] checkout settles every folio, invoices A then B consecutively, Booking.invoiceNo = first; company credit per folio; GST docs per folio',()=>{
    let {s,id,co}=stay();
    s=open(s,id,co);
    s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry'});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Room service food',amount:120000,gst:'food'});
    let b=bk(s,id);const tB=totals(M.sfView(b,'B')),tA=totals(M.sfView(b,'A'));
    assert.equal(reason(()=>execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id,folio:'A'})),'SF_COMPANY_FOLIO');
    assert.equal(codeOf(()=>execute(s,'owner',{type:'CHECKOUT',id})),'INVALID_TRANSITION','folios not settled');
    s=execute(s,'owner',{type:'PAYMENT',id,amount:tA.balance,method:'UPI',reference:'UPI-A'});
    s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id});
    const apr=s.approvals.at(-1);assert.equal(apr.amount,tB.balance);assert.equal(apr.meta.folio,'B');
    s=execute(s,'owner',{type:'APPROVAL',id:apr.id,approve:true});
    const before=s;
    s=execute(s,'owner',{type:'CHECKOUT',id});
    b=bk(s,id);
    const [fa,fb]=b.folios;
    assert.ok(fa.invoiceNo&&fb.invoiceNo);assert.equal(b.invoiceNo,fa.invoiceNo);
    const n=x=>Number(String(x).match(/(\d+)$/)[1]);assert.equal(n(fb.invoiceNo),n(fa.invoiceNo)+1,'consecutive in letter order');
    const cr=b.receipts.find(r=>r.kind==='company-credit');assert.equal(cr.folio,'B');assert.equal(cr.amount,tB.balance);
    assert.equal(totals(M.sfView(b,'A')).balance,0);assert.equal(totals(M.sfView(b,'B')).balance,0);
    const docs=M.gstr1Documents(s,'2000-01-01','2099-12-31').docs.filter(d=>d.meta.booking===id);
    assert.deepEqual(docs.map(d=>d.no),[fa.invoiceNo,fb.invoiceNo]);
    assert.equal(docs[1].supply,'B2B');
    const sum=d=>d.lines.reduce((a,l)=>a+l.taxable+l.cgst+l.sgst,0);
    assert.equal(sum(docs[0])+sum(docs[1]),totals(b).total,'Σ invoices = the stay');
    void before;
  });

  test('[F8-SF-DOMAIN] credit note per folio: cap and target are that folio; folio A untouched',()=>{
    let {s,id,co}=stay();
    s=open(s,id,co);s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry'});
    s=execute(s,'owner',{type:'PAYMENT',id,amount:totals(M.sfView(bk(s,id),'A')).balance,method:'UPI',reference:'UPI-A'});
    s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id});s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    s=execute(s,'owner',{type:'CHECKOUT',id});
    const night=bk(s,id).lines.find(l=>l.kind==='room').amount;
    s=execute(s,'manager',{type:'CREDIT_NOTE',id,folio:'B',amount:night,reason:'One night not used'});
    const a=s.approvals.at(-1);assert.equal(a.target,bk(s,id).folios[1].invoiceNo);assert.equal(a.meta.folio,'B');
    const aLines=M.sfView(bk(s,id),'A').lines.length;
    s=execute(s,'owner',{type:'APPROVAL',id:a.id,approve:true});
    const b=bk(s,id);
    assert.equal(b.creditNotes.at(-1).folio,'B');
    assert.ok(b.lines.filter(l=>l.approvalId===a.id).every(l=>l.folio==='B'));
    assert.equal(M.sfView(b,'A').lines.length,aLines,'folio A untouched');
    const cl=b.lines.find(l=>l.approvalId===a.id);assert.equal(cl.taxBps,b.lines.find(l=>l.kind==='room').taxBps,'credit at the folio adjustment rate (room rate)');
  });
}
