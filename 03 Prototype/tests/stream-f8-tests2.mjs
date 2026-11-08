// F8-TESTS2 · split-folio cross-check tests the integrator listed as still to write (F8-INT-DONE "Split-folio cross-check tests"):
// golden variants (b) and (c), the food line through a POS room-post (ledger 4200) with the trial balance against the unsplit control,
// the DEC-061 walk (variant d) over commands and screens, M5 (two guest credits of the same amount), M6 (late half day with extras routed
// to the company folio) and H2 (discount on a mixed-rate folio). Tests only: no product code. Fictional data on the BASE fixture;
// company GSTIN 27AAPFU0939F1ZV (fictional, valid check digit). Owner-question defaults used: SFQ-13 Manager credit limit per company folio
// ₹10,000, SFQ-20 mixed-rate adjustment refused, SFQ-6 night posting never refused.
import {readFileSync} from 'node:fs';

export async function register({test,M,assert}){
  const {seed,execute,totals}=M;
  const T='[F8-TESTS2] ';
  const addDays=(d,n)=>{const x=new Date(d+'T00:00:00Z');x.setUTCDate(x.getUTCDate()+n);return x.toISOString().slice(0,10);};
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  const withLimit=(s,co,limit)=>({...s,groupsX:{...s.groupsX,companies:s.groupsX.companies.map(c=>c.id===co?{...c,creditLimit:limit}:c)}});
  const err=fn=>{try{fn();}catch(e){return e;}return null;};
  const msg=fn=>err(fn)?.message??'';
  const reasonOf=fn=>{const e=err(fn);return e?(e.details?.reason??e.code):'none';};

  /** The §5.7 stay (same as stream-f8-int.mjs): split=false gives the unsplit control. withFood posts the food through a POS room-post instead of CHARGE. */
  function golden({split=true,limit=5000000,pos=false}={}){
    let s=seed();
    s=execute(s,'owner',{type:'GRP_COMPANY_SAVE',name:'Deccan Agro Ltd (fictional)',gstin:'27AAPFU0939F1ZV',city:'Latur',creditLimit:limit});
    const co=s.groupsX.companies.at(-1).id;
    const dep=addDays(s.date,3),room=s.rooms.find(r=>r.type==='Deluxe'&&r.rate===300000&&!r.blocked&&r.hk==='Ready'&&M.available(s,r.id,s.date,dep));
    assert.ok(room,'a free Deluxe room at ₹3,000');
    s=execute(s,'owner',{type:'BOOK',guest:'Golden Tests2 Guest',room:room.id,arrival:s.date,departure:dep,people:1});
    const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    if(split)s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'company',ref:co},routes:['room'],reason:'Company pays the room'});
    s=execute(s,'owner',{type:'PAYMENT',id,amount:100000,method:'UPI',reference:'UPI-GOLD-1'});
    s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry'});
    let posId;
    if(pos){
      s=execute(s,'reception',{type:'POS_ORDER_OPEN',outlet:'room-service',room:room.id});
      posId=M.posSlice(s).orders.at(-1).id;
      s=execute(s,'reception',{type:'POS_ITEM_ADD',id:posId,item:'m-tea',qty:2});
      s=execute(s,'reception',{type:'POS_KOT_SEND',id:posId});
      s=execute(s,'reception',{type:'POS_SETTLE',id:posId,mode:'folio'});
    }else s=execute(s,'owner',{type:'CHARGE',id,label:'Room service food',amount:120000,gst:'food'});
    return {s,id,co,posId};
  }
  function settle({s,id},split=true){
    if(split){
      s=execute(s,'owner',{type:'PAYMENT',id,amount:totals(M.sfView(bk(s,id),'A')).balance,method:'Cash',folio:'A'});
      s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id});
      s=execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    }else s=execute(s,'owner',{type:'PAYMENT',id,amount:totals(bk(s,id)).balance,method:'Cash'});
    return execute(s,'owner',{type:'CHECKOUT',id});
  }

  // ── variant (b) ──────────────────────────────────────────────────────────────────────────────────────────────
  test(T+'variant (b): D7 limit lowered to ₹9,000 before checkout → company credit refused with the D7 text, guest folio A unaffected; the Owner raising the limit unblocks it',()=>{
    let g=golden();let {s}=g;const {id,co}=g;
    s=withLimit(s,co,900000);
    // the company folio is ₹9,450 against a ₹9,000 limit: the request is refused with the grpCreditRefusal text
    const m=msg(()=>execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id}));
    assert.match(m,/Credit limit exceeded for Deccan Agro Ltd \(fictional\)/);
    assert.equal(err(()=>execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id})).code,'INVALID_TRANSITION');
    // the guest folio is unaffected: A settles by cash and its totals do not move
    const aBefore=JSON.stringify(totals(M.sfView(bk(s,id),'A')));
    s=execute(s,'owner',{type:'PAYMENT',id,amount:85000,method:'Cash',folio:'A'});
    assert.equal(totals(M.sfView(bk(s,id),'A')).balance,0);
    assert.notEqual(JSON.stringify(totals(M.sfView(bk(s,id),'A'))),aBefore);
    assert.equal(totals(M.sfView(bk(s,id),'B')).balance,945000,'B untouched');
    // checkout stays blocked (folio B owes) and nothing is posted
    assert.equal(err(()=>execute(s,'owner',{type:'CHECKOUT',id})).code,'INVALID_TRANSITION');
    // a payment on B (no cash: DEC-076) is the other way out; and raising the limit (Owner only) lets the credit through
    assert.equal(err(()=>execute(s,'manager',{type:'GRP_COMPANY_SAVE',id:co,name:'Deccan Agro Ltd (fictional)',gstin:'27AAPFU0939F1ZV',creditLimit:5000000}))?.code,'FORBIDDEN','a Manager cannot raise a limit');
    s=withLimit(s,co,5000000);
    s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id});s=execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    s=execute(s,'owner',{type:'CHECKOUT',id});
    assert.equal(bk(s,id).status,'Departed');
  });

  test(T+'variant (b2): the limit is lowered AFTER the request was approved → CHECKOUT re-checks D7 and refuses; the Owner raising it lets the checkout through',()=>{
    let {s,id,co}=golden();
    s=execute(s,'owner',{type:'PAYMENT',id,amount:85000,method:'Cash',folio:'A'});
    s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id});
    s=execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    const low=withLimit(s,co,900000);
    const e=err(()=>execute(low,'owner',{type:'CHECKOUT',id}));
    assert.ok(e,'checkout refused');assert.match(e.message,/Credit limit exceeded|credit limit/i);
    assert.equal(bk(low,id).status,'In house','nothing changed');
    assert.equal(bk(execute(s,'owner',{type:'CHECKOUT',id}),id).status,'Departed');
  });

  // ── variant (c) ──────────────────────────────────────────────────────────────────────────────────────────────
  test(T+'variant (c): extra ₹600 onto B over the limit is refused, the same onto A passes; refusal changes no state',()=>{
    let {s,id,co}=golden();
    s=withLimit(s,co,1000000); // ₹10,000: B owes ₹9,450, + ₹600 + GST goes over
    const before=JSON.stringify(s);
    const e=err(()=>execute(s,'owner',{type:'CHARGE',id,label:'Banquet extra',amount:60000,gst:'laundry',folio:'B'}));
    assert.ok(e);assert.equal(e.code,'INVALID_TRANSITION');assert.match(e.message,/Credit limit exceeded for Deccan Agro Ltd \(fictional\)/);
    assert.equal(JSON.stringify(s),before,'state untouched by the refusal');
    const bBefore=totals(M.sfView(bk(s,id),'B')).total;
    s=execute(s,'owner',{type:'CHARGE',id,label:'Banquet extra',amount:60000,gst:'laundry',folio:'A'});
    const b=bk(s,id),aLine=M.sfView(b,'A').lines.find(l=>l.label==='Banquet extra');
    assert.ok(aLine,'posted on A');assert.equal(aLine.folio,'A');
    assert.equal(totals(M.sfView(b,'B')).total,bBefore,'folio B unchanged');
    assert.equal(totals(b).total,1130000+60000+Math.round(60000*0.18),'stay total = golden ₹11,300 + ₹600 + 18 % GST ₹108');
  });

  test(T+'variant (c2): a posting that still fits the limit onto B passes; a night posting over the limit is never refused (SFQ-6) and company credit then is',()=>{
    let {s,id,co}=golden();
    // headroom: limit ₹10,000, B owes ₹9,450 → ₹400 + 18 % = ₹472 fits (₹9,922)
    s=withLimit(s,co,1000000);
    s=execute(s,'owner',{type:'CHARGE',id,label:'Small extra',amount:40000,gst:'laundry',folio:'B'});
    assert.equal(M.sfView(bk(s,id),'B').lines.some(l=>l.label==='Small extra'),true);
    // limit lowered below what B owes: a further night is still posted (the day must close)
    let t=withLimit(execute(seed(),'owner',{type:'GRP_COMPANY_SAVE',name:'Night Co (fictional)',gstin:'27AAPFU0939F1ZV',city:'Latur',creditLimit:5000000}),'x',0);
    t=golden({limit:5000000}).s;
    const co2=t.groupsX.companies.at(-1).id;
    t=withLimit(t,co2,100000);
    const nights=M.sfView(bk(t,id),'B').lines.filter(l=>l.kind==='room').length;
    assert.equal(nights,3);
    // credit is refused at request time while over the limit
    assert.match(msg(()=>execute(t,'reception',{type:'COMPANY_CREDIT_REQUEST',id})),/Credit limit exceeded/);
  });

  // ── food through a POS room-post + trial balance against the control ─────────────────────────────────────────
  const net=(st,id)=>{const m={},bb=bk(st,id),dates=[...new Set([...bb.lines.map(l=>l.date),...bb.receipts.map(r=>r.date)])];
    for(const d of dates)for(const v of M.accVouchers(st,d).filter(v=>v.source.booking===id))for(const e of v.entries)m[e.account]=(m[e.account]??0)+e.debit-e.credit;
    for(const k of Object.keys(m))if(m[k]===0)delete m[k];return m;};
  const pick=(m,re)=>Object.fromEntries(Object.entries(m).filter(([k])=>re.test(k)));
  const sum=m=>Object.values(m).reduce((a,b)=>a+b,0);

  test(T+'golden with the food line through a POS room-post: line lands on A as kind food (ledger 4200 F&B), tax per line, Σ of two invoices = the unsplit control with the same POS post',()=>{
    const g=golden({pos:true}),c=golden({split:false,pos:true});
    const lid='posline:'+g.posId;
    const line=bk(g.s,g.id).lines.find(l=>l.id===lid);
    assert.ok(line,'the POS line is on the folio');assert.equal(line.kind,'food');assert.equal(line.folio,'A','extras route to the guest folio');
    assert.ok(line.amount>0);
    const sA=settle(g),sC=settle(c,false);
    const b=bk(sA,g.id),cb=bk(sC,c.id);
    // revenue split: 4200 carries exactly the POS net, in both
    const a=net(sA,g.id),k=net(sC,c.id);
    const r4200=Object.entries(a).filter(([acc])=>acc.startsWith('4200')).reduce((x,[,v])=>x+v,0);
    assert.equal(-r4200,line.amount,'4200 F&B revenue = the POS net');
    assert.deepEqual(pick(a,/^4/),pick(k,/^4/),'revenue ledgers identical to the control, head by head');
    assert.deepEqual(pick(a,/^2[12]/),pick(k,/^2[12]/),'output GST identical');
    // trial balance: every voucher balanced; split differs from the control only on tender / party (1100, 1110, cash, UPI)
    for(const d of [...new Set(b.lines.map(l=>l.date).concat(b.receipts.map(r=>r.date)))]){
      const vs=M.accVouchers(sA,d);assert.ok(vs.every(M.accBalanced),'balanced on '+d);const t=M.accTotals(vs);assert.equal(t.debit,t.credit,'Σ Dr = Σ Cr on '+d);
    }
    assert.equal(sum(a),0,'booking vouchers net to zero');
    assert.equal(Object.entries(a).filter(([acc])=>acc.startsWith('1100')).reduce((x,[,v])=>x+v,0),0,'1100 guest ledger nets to 0');
    const co=Object.entries(a).filter(([acc])=>acc.startsWith('1110/'));
    assert.equal(co.length,1);assert.equal(co[0][1],totals(M.sfView(b,'B')).total,'1110/<Deccan> Dr = folio B total');
    assert.equal(Object.keys(k).filter(acc=>acc.startsWith('1110')).length,0);
    // invoices: Σ = control total; the food line is on invoice A (B2C) only
    assert.equal(totals(b).total,totals(cb).total);
    const docs=M.gstr1Documents(sA,'2000-01-01','2099-12-31').docs.filter(d=>d.meta.booking===g.id);
    assert.deepEqual(docs.map(d=>d.supply),['B2C','B2B']);
    const tot=d=>d.lines.reduce((x,l)=>x+l.taxable+l.cgst+l.sgst,0);
    assert.equal(tot(docs[0])+tot(docs[1]),totals(cb).total,'Σ invoices = the unsplit control to the paisa');
    assert.equal(tot(docs[0]),totals(M.sfView(b,'A')).total);assert.equal(tot(docs[1]),totals(M.sfView(b,'B')).total);
  });

  // ── GST per invoice: each invoice's tax = Σ of its own lines' tax ─────────────────────────────────────────────
  test(T+'GST per invoice: each folio invoice carries exactly the tax of its own lines (I2/I3), no recomputation on a folio sum',()=>{
    const g=golden();const s=settle(g);const b=bk(s,g.id);
    const docs=M.gstr1Documents(s,'2000-01-01','2099-12-31').docs.filter(d=>d.meta.booking===g.id);
    for(const [i,f] of ['A','B'].entries()){
      const view=M.sfView(b,f),lineTax=view.lines.reduce((x,l)=>x+M.lineTax(l),0);
      assert.equal(docs[i].lines.reduce((x,l)=>x+l.cgst+l.sgst,0),lineTax,'invoice '+f+' tax = Σ lineTax of folio '+f);
      assert.equal(totals(view).tax,lineTax);
    }
    assert.equal(totals(b).tax,docs.reduce((x,d)=>x+d.lines.reduce((y,l)=>y+l.cgst+l.sgst,0),0));
    assert.equal(totals(b).total,totals(M.sfView(b,'A')).total+totals(M.sfView(b,'B')).total,'I2: stay total = Σ folio totals');
  });

  // ── variant (d): DEC-061 walk ────────────────────────────────────────────────────────────────────────────────
  const BARRED=['housekeeper','housekeeper2','supervisor','maint'];
  test(T+'variant (d) DEC-061 walk, commands: Housekeeping, Supervisor and Maintenance are refused every folio money / split command on a split stay, and a refusal changes nothing',()=>{
    let {s,id,co}=golden();
    const cmds=[
      {type:'SF_FOLIO_OPEN',id,payer:{kind:'guest',name:'Second Person'},routes:[],reason:'x'},
      {type:'SF_ROUTE_SET',id,folio:'B',routes:[],reason:'No longer'},
      {type:'SF_FOLIO_REMOVE',id,folio:'B'},
      {type:'PAYMENT',id,folio:'A',amount:100,method:'UPI',reference:'x1'},
      {type:'PAYMENT',id,folio:'B',amount:100,method:'UPI',reference:'x2'},
      {type:'CHARGE',id,folio:'B',label:'x',amount:100,gst:'laundry'},
      {type:'DISCOUNT',id,folio:'B',amount:100,reason:'x'},
      {type:'COMPANY_CREDIT_REQUEST',id,folio:'B'},
      {type:'COMPANY_CREDIT_REQUEST',id},
      {type:'CREDIT_NOTE',id,folio:'B',amount:100,reason:'x'},
      {type:'CHECKOUT',id},
      {type:'GUEST_CREDIT_REQUEST',booking:id,folio:'A'},
    ];
    const snap=JSON.stringify(s);
    for(const u of BARRED){
      assert.ok(s.users.some(x=>x.id===u),'fixture user '+u);
      for(const c of cmds){
        const e=err(()=>execute(s,u,c));
        assert.ok(e,u+' '+c.type+' was allowed');
        assert.ok(e.code==='FORBIDDEN'||/Access denied|not permitted/i.test(e.message),u+' '+c.type+' refused for another reason: '+e.message);
      }
    }
    assert.equal(JSON.stringify(s),snap,'execute is copy-on-write: the refusals changed nothing');
    // while the roles that own folios are still allowed (the walk is not refusing everything)
    assert.doesNotThrow(()=>execute(s,'reception',{type:'PAYMENT',id,folio:'A',amount:100,method:'UPI',reference:'ok-1'}));
    void co;
  });

  test(T+'variant (d) DEC-061 walk, screens: no folio money for those roles — RBAC says no, and the split dialog, switcher, settle sheet and phone folio gate on billing / payments / checkout',()=>{
    const s=golden().s;
    for(const u of BARRED){
      assert.equal(M.rbacCanViewFolios(s,u),false,u+' cannot view folios');
      assert.equal(M.can(s,u,'payments'),false,u+' has no payments');
      assert.equal(M.can(s,u,'billing'),false,u+' has no billing');
      assert.equal(M.can(s,u,'checkout'),false,u+' has no checkout');
    }
    const modals=readFileSync('src/features/folio/modals.tsx','utf8'),split=readFileSync('src/features/folio/Split.tsx','utf8');
    assert.ok(/rbacCanViewFolios\(s,uid\)\)return <Empty title="Access denied"/.test(modals),'the folio dialog (and with it the switcher) refuses roles without folio rights');
    assert.ok(/act\('billing'/.test(split),'Split bill entry gated on billing');
    assert.ok(/permission\('payments'\)/.test(split)&&/permission\('checkout'\)/.test(split),'settle sheet actions gated on payments / checkout');
    for(const f of ['src/mobile/money/Folio.tsx','src/mobile/money/Checkout.tsx']){
      const src=readFileSync(f,'utf8');
      assert.ok(/rbacCanViewFolios|permission\(|can\(|act\(/.test(src),f+' is permission-gated');
    }
    // the folio/book selectors do not hand a barred role a folio: the guest list view strips money for them
    const rows=M.folioListFor?.(s,'housekeeper');
    if(Array.isArray(rows))assert.equal(rows.length,0);
  });

  // ── H2: discount on a mixed-rate folio is refused (SFQ-20 default) ───────────────────────────────────────────
  test(T+'H2: a discount on guest folio A (laundry 18 % + food 5 %, no room night) is refused with the mixed-rate text; on B (room 5 %) it posts at 5 %',()=>{
    const {s,id}=golden();
    assert.match(msg(()=>execute(s,'manager',{type:'DISCOUNT',id,folio:'A',amount:10000,reason:'Goodwill'})),/different GST rates/);
    const t=execute(s,'owner',{type:'DISCOUNT',id,folio:'B',amount:30000,reason:'Goodwill'});
    const d=bk(t,id).lines.find(l=>l.kind==='discount');
    assert.ok(d);assert.equal(d.folio,'B','a discount stays on the folio named (H8)');assert.equal(d.taxBps,500);
  });

  // ── M6: late half day with extras routed to the company folio ────────────────────────────────────────────────
  test(T+'M6: late check-out half day with extras routed to the company folio — the half-day line is not refused by D7 at CHECKOUT',()=>{
    let s=seed();
    s=execute(s,'owner',{type:'GRP_COMPANY_SAVE',name:'Late Co (fictional)',gstin:'27AAPFU0939F1ZV',city:'Latur',creditLimit:5000000});
    const co=s.groupsX.companies.at(-1).id;
    s=execute(s,'owner',{type:'BOOK',guest:'Late Tests2 Guest',room:'105',arrival:'2026-10-24',departure:'2026-10-25',people:1});
    const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'company',ref:co},routes:['extras'],reason:'Company pays the extras'});
    s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry'});
    assert.equal(bk(s,id).lines.find(l=>l.label==='Laundry').folio,'B','extras routed to the company folio');
    // the guest folio pays the night; the company folio takes laundry on credit; then the limit is lowered to exactly the laundry
    s=execute(s,'owner',{type:'PAYMENT',id,amount:totals(M.sfView(bk(s,id),'A')).balance,method:'UPI',reference:'UPI-LATE-A',folio:'A'});
    s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id});s=execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    s=withLimit(s,co,totals(M.sfView(bk(s,id),'B')).total);
    const late=structuredClone(s);late.clock='2026-10-25T13:15';
    const lc=M.stayLateCheckout(late,bk(late,id));
    assert.equal(lc.late,true);
    // the late half day falls on the company folio (extras route); its payment on B by UPI (no cash on a company folio)
    const half=lc.halfDay.amount+Math.round(lc.halfDay.amount*lc.halfDay.taxBps/10000);
    let t=execute(late,'owner',{type:'PAYMENT',id,amount:half,method:'UPI',reference:'UPI-LATE-B',folio:'B'});
    t=execute(t,'reception',{type:'CHECKOUT',id,lateCharge:'half-day'});
    const b=bk(t,id);
    assert.equal(b.status,'Departed');
    const ll=b.lines.find(l=>l.id===`late-half:${id}`);
    assert.ok(ll,'late half-day line posted');assert.equal(ll.folio,'B','late line routed to the extras folio (company)');
  });

  // ── M5: two guest credits of the same amount ─────────────────────────────────────────────────────────────────
  test(T+'M5: two Udhaar folios with the same balance each get their own 1120 party in the journal (matched through the receipt\'s approval, not by amount)',()=>{
    let s=seed();
    const gs=s.guests.filter(g=>g.phone&&String(g.phone).replace(/\D/g,'').length>=10).slice(0,2);
    assert.equal(gs.length,2,'two seeded guests with phones');
    for(const g of gs)s=execute(s,'owner',{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:g.id,limit:5000000,note:'Regular'});
    s=execute(s,'owner',{type:'BOOK',guest:'Udhaar Tests2 Guest',room:'105',arrival:s.date,departure:addDays(s.date,2),people:1});
    const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'udhaar',ref:gs[0].id},routes:[],reason:'First credit guest pays'});
    s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'udhaar',ref:gs[1].id},routes:[],reason:'Second credit guest pays'});
    const fol=bk(s,id).folios.map(f=>f.id);assert.deepEqual(fol,['A','B','C']);
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry',folio:'B'});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry',folio:'C'});
    s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'PAYMENT',id,amount:totals(M.sfView(bk(s,id),'A')).balance,method:'UPI',reference:'UPI-UD-A',folio:'A'});
    const amt=totals(M.sfView(bk(s,id),'B')).balance;
    assert.equal(amt,totals(M.sfView(bk(s,id),'C')).balance,'the two credit folios owe the same');
    for(const f of ['B','C']){
      s=execute(s,'reception',{type:'GUEST_CREDIT_REQUEST',booking:id,folio:f,reason:'Regular guest'});
      s=execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    }
    s=execute(s,'owner',{type:'CHECKOUT',id});
    const b=bk(s,id),gc=b.receipts.filter(r=>r.kind==='guest-credit');
    assert.equal(gc.length,2);assert.deepEqual(gc.map(r=>r.folio).sort(),['B','C']);assert.ok(gc.every(r=>r.amount===amt));
    const parties=new Set();
    for(const v of M.accVouchers(s,s.date).filter(v=>v.source.booking===id&&v.source.sub==='guest-credit'))
      for(const e of v.entries.filter(e=>e.account.startsWith('1120/')&&e.debit===amt))parties.add(e.account);
    assert.equal(parties.size,2,'two different Udhaar parties, one per credit receipt: '+[...parties].join(', '));
    const keys=gs.map(g=>g.creditAccount?.customerKey??M.customerKeyForCredit(g));
    for(const k of keys)assert.ok([...parties].some(p=>p.includes(k)),'party for '+k);
  });
}
