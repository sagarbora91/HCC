// F8D-TESTS-A · SF-GOLDEN fixture bind (SF-CONTRACT 5.7), prototype side. Every expected figure comes from tests/sf-golden.fixture.json, the file the
// server twin (Hcc.Integration.Tests/SfGoldenTests.cs) reads as well, so the two suites cannot drift apart:
// "Deccan Agro pays the rooms (9,450), the guest pays the extras (1,850)" = two invoices, two GST documents, one stay of 11,300.
// Tests only. Fictional data; GSTIN 27AAPFU0939F1ZV. Owner-question defaults used: none beyond the contract (SFQ-6 / SFQ-13 are not touched here).
// Known divergence from the contract text: food posted by CHARGE lands on ledger 4100 (not 4200); the 4200 path is the POS room-post, tested below.
import {readFileSync} from 'node:fs';
import {build} from 'esbuild';
await build({entryPoints:['src/domain/reports/meas-daily/index.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/sf-golden-meas-daily.mjs',logLevel:'error'});
const D=await import('./.generated/sf-golden-meas-daily.mjs');
await build({entryPoints:['src/domain/analytics/legacy-input.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/sf-golden-an-input.mjs',logLevel:'error'});
const AN=await import('./.generated/sf-golden-an-input.mjs');
const FX=JSON.parse(readFileSync(new URL('./sf-golden.fixture.json',import.meta.url),'utf8'));

export async function register({test,M,assert}){
  const {seed,execute,totals}=M;
  const T='[SF-GOLDEN] ';
  const addDays=(d,n)=>{const x=new Date(d+'T00:00:00Z');x.setUTCDate(x.getUTCDate()+n);return x.toISOString().slice(0,10);};
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  const n=x=>Number(String(x).match(/(\d+)$/)[1]);
  const sumDoc=d=>d.lines.reduce((a,l)=>({taxable:a.taxable+l.taxable,cgst:a.cgst+l.cgst,sgst:a.sgst+l.sgst}),{taxable:0,cgst:0,sgst:0});
  const docTotal=d=>{const t=sumDoc(d);return t.taxable+t.cgst+t.sgst;};

  /** The fixture stay. split=false is the unsplit control (same lines, one folio). pos=true posts the food through a POS room-post (ledger 4200). */
  function golden({split=true,pos=false}={}){
    let s=seed();
    s=execute(s,'owner',{type:'GRP_COMPANY_SAVE',name:FX.company.name,gstin:FX.company.gstin,city:'Latur',creditLimit:FX.company.creditLimitPaise});
    const co=s.groupsX.companies.at(-1).id;
    const dep=addDays(s.date,FX.room.nights);
    const room=s.rooms.find(r=>r.type===FX.room.type&&r.rate===FX.room.ratePaise&&!r.blocked&&r.hk==='Ready'&&M.available(s,r.id,s.date,dep));
    assert.ok(room,'a free '+FX.room.type+' room at the fixture rate');
    s=execute(s,'owner',{type:'BOOK',guest:'Sf Golden Guest',room:room.id,arrival:s.date,departure:dep,people:1});
    const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    if(split)s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'company',ref:co},routes:['room'],reason:'Company pays the room'});
    s=execute(s,'owner',{type:'PAYMENT',id,amount:FX.payments.advanceUpiPaise,method:'UPI',reference:'UPI-SFG-1'});
    s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:FX.postings.laundryPaise,gst:'laundry'});
    let posId;
    if(pos){
      s=execute(s,'reception',{type:'POS_ORDER_OPEN',outlet:'room-service',room:room.id});
      posId=M.posSlice(s).orders.at(-1).id;
      s=execute(s,'reception',{type:'POS_ITEM_ADD',id:posId,item:'m-tea',qty:50});
      s=execute(s,'reception',{type:'POS_KOT_SEND',id:posId});
      s=execute(s,'reception',{type:'POS_SETTLE',id:posId,mode:'folio'});
    }else s=execute(s,'owner',{type:'CHARGE',id,label:'Room service food',amount:FX.postings.foodPaise,gst:'food'});
    return {s,id,co,posId};
  }
  function settle({s,id},split=true){
    if(split){
      const balA=totals(M.sfView(bk(s,id),'A')).balance;
      if(balA>0)s=execute(s,'owner',{type:'PAYMENT',id,amount:balA,method:'Cash',folio:'A'});
      s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id});
      s=execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    }else s=execute(s,'owner',{type:'PAYMENT',id,amount:totals(bk(s,id)).balance,method:'Cash'});
    return execute(s,'owner',{type:'CHECKOUT',id});
  }
  /** Net debit-credit by account code (first 4 characters of the account key; party suffix dropped) over every date the stay touched. */
  function ledger(st,id){
    const m={},b=bk(st,id),dates=[...new Set([...b.lines.map(l=>l.date),...b.receipts.map(r=>r.date)])];
    for(const d of dates)for(const v of M.accVouchers(st,d).filter(v=>v.source.booking===id))for(const e of v.entries)m[e.account.slice(0,4)]=(m[e.account.slice(0,4)]??0)+e.debit-e.credit;
    return m;
  }
  const party=(st,id)=>{const m={},b=bk(st,id),dates=[...new Set([...b.lines.map(l=>l.date),...b.receipts.map(r=>r.date)])];
    for(const d of dates)for(const v of M.accVouchers(st,d).filter(v=>v.source.booking===id))for(const e of v.entries)if(e.account.startsWith('1110/'))m[e.account]=(m[e.account]??0)+e.debit-e.credit;return m;};

  test(T+'the fixture is arithmetically consistent: A + B = the stay, tax heads add up, 2 invoices / 2 GST documents',()=>{
    const A=FX.folioA,B=FX.folioB,S=FX.stay;
    assert.equal(A.taxablePaise+A.cgstPaise+A.sgstPaise,A.totalPaise);assert.equal(B.taxablePaise+B.cgstPaise+B.sgstPaise,B.totalPaise);
    assert.equal(A.totalPaise+B.totalPaise,S.totalPaise);assert.equal(A.taxablePaise+B.taxablePaise,S.taxablePaise);
    assert.equal(A.cgstPaise+B.cgstPaise,S.cgstPaise);assert.equal(A.sgstPaise+B.sgstPaise,S.sgstPaise);
    assert.equal(S.taxablePaise+S.cgstPaise+S.sgstPaise,S.totalPaise);assert.equal(S.totalPaise,1130000,'₹11,300.00');
    assert.equal(FX.room.ratePaise*FX.room.nights,B.taxablePaise,'the company folio is the three nights');
    assert.equal(FX.postings.laundryPaise+FX.postings.foodPaise,A.taxablePaise,'the guest folio is the extras');
    assert.equal(FX.payments.companyCreditPaise,B.totalPaise);assert.equal(FX.payments.advanceUpiPaise+FX.payments.cashAtCheckoutPaise,A.totalPaise);
    assert.equal(FX.invoices,2);assert.equal(FX.gstDocuments,2);
  });

  test(T+'two folios, two invoices in letter order, two GST documents; every figure from the fixture; Σ = the unsplit control to the paisa',()=>{
    const g=golden();let b=bk(g.s,g.id);
    const tA=totals(M.sfView(b,'A')),tB=totals(M.sfView(b,'B'));
    assert.deepEqual([tA.net,tA.tax,tA.total],[FX.folioA.taxablePaise,FX.folioA.cgstPaise+FX.folioA.sgstPaise,FX.folioA.totalPaise],'folio A');
    assert.deepEqual([tB.net,tB.tax,tB.total],[FX.folioB.taxablePaise,FX.folioB.cgstPaise+FX.folioB.sgstPaise,FX.folioB.totalPaise],'folio B');
    assert.equal(tA.balance,FX.folioA.totalPaise-FX.payments.advanceUpiPaise,'A owes ₹850 after the ₹1,000 advance');
    assert.equal(FX.payments.cashAtCheckoutPaise,tA.balance,'the fixture cash equals the A balance');
    const s=settle(g);b=bk(s,g.id);
    const [fa,fb]=b.folios;
    assert.ok(fa.invoiceNo&&fb.invoiceNo);assert.equal(n(fb.invoiceNo),n(fa.invoiceNo)+1,'consecutive, A first');assert.equal(b.invoiceNo,fa.invoiceNo,'Booking.invoiceNo = the first number');
    const docs=M.gstr1Documents(s,'2000-01-01','2099-12-31').docs.filter(d=>d.meta.booking===g.id);
    assert.equal(docs.length,FX.gstDocuments);
    assert.deepEqual(docs.map(d=>d.no),[fa.invoiceNo,fb.invoiceNo]);
    assert.deepEqual(docs.map(d=>d.supply),[FX.folioA.supply,FX.folioB.supply]);
    assert.deepEqual(sumDoc(docs[0]),{taxable:FX.folioA.taxablePaise,cgst:FX.folioA.cgstPaise,sgst:FX.folioA.sgstPaise});
    assert.deepEqual(sumDoc(docs[1]),{taxable:FX.folioB.taxablePaise,cgst:FX.folioB.cgstPaise,sgst:FX.folioB.sgstPaise});
    const c=settle(golden({split:false}),false),cb=bk(c,c.bookings.at(-1).id);
    const cdocs=M.gstr1Documents(c,'2000-01-01','2099-12-31').docs.filter(d=>d.meta.booking===cb.id);
    assert.equal(cdocs.length,1,'the control issues one invoice');
    assert.deepEqual(sumDoc(cdocs[0]),{taxable:FX.stay.taxablePaise,cgst:FX.stay.cgstPaise,sgst:FX.stay.sgstPaise},'the control = the fixture stay');
    assert.equal(docTotal(docs[0])+docTotal(docs[1]),FX.stay.totalPaise,'Σ of the two invoices = ₹11,300.00');
    assert.equal(docTotal(cdocs[0]),FX.stay.totalPaise);
    assert.equal(totals(b).total,FX.stay.totalPaise);
    const cr=b.receipts.find(r=>r.kind==='company-credit');assert.equal(cr.folio,'B');assert.equal(cr.amount,FX.payments.companyCreditPaise);
  });

  test(T+'accounting = the control except the party split of the receivable; 1110/<Deccan> Dr = folio B; 1100 nets to 0; vouchers balanced',()=>{
    const g=golden(),s=settle(g),cg=golden({split:false}),c=settle(cg,false),cid=c.bookings.at(-1).id;
    const a=ledger(s,g.id),k=ledger(c,cid);
    // revenue by head: the CHARGE path puts the food on 4100 with the laundry (the 4200 path is the POS test below)
    assert.equal(-a['4000'],FX.room.ratePaise*FX.room.nights,'4000 rooms ₹9,000');
    assert.equal(-(a['4100']??0),FX.postings.laundryPaise+FX.postings.foodPaise,'4100 services ₹500 + ₹1,200');
    for(const head of new Set([...Object.keys(a),...Object.keys(k)].filter(x=>/^4|^2[12]/.test(x))))assert.equal(a[head]??0,k[head]??0,'ledger '+head+' identical to the control');
    const gst=Object.entries(a).filter(([x])=>/^2[12]/.test(x)).reduce((x,[,v])=>x+v,0);
    assert.equal(-gst,FX.stay.cgstPaise+FX.stay.sgstPaise,'output CGST 300 + SGST 300');
    const p=party(s,g.id);
    assert.equal(Object.keys(p).length,1,'one company party');assert.equal(Object.values(p)[0],FX.folioB.totalPaise,'1110/<Deccan> Dr ₹9,450');
    assert.equal(a['1100']??0,0,'1100 guest ledger nets to 0');
    assert.equal(Object.keys(party(c,cid)).length,0,'the control has no company receivable');
    const b=bk(s,g.id);
    for(const d of [...new Set(b.lines.map(l=>l.date).concat(b.receipts.map(r=>r.date)))]){
      const vs=M.accVouchers(s,d);assert.ok(vs.every(M.accBalanced),'balanced on '+d);const t=M.accTotals(vs);assert.equal(t.debit,t.credit,'Σ Dr = Σ Cr on '+d);
    }
  });

  test(T+'food through a POS room-post lands on 4200 (kind food, on folio A); the other heads and the total equal the CHARGE-path control',()=>{
    const g=golden({pos:true}),s=settle(g),b=bk(s,g.id);
    const line=b.lines.find(l=>l.id==='posline:'+g.posId);
    assert.ok(line,'the POS line is on the folio');assert.equal(line.kind,'food');assert.equal(line.folio,'A');
    const a=ledger(s,g.id);
    assert.equal(-(a['4200']??0),line.amount,'4200 F&B revenue = the POS net');
    assert.equal(-a['4000'],FX.room.ratePaise*FX.room.nights,'4000 unchanged');
    assert.equal(-(a['4100']??0),FX.postings.laundryPaise,'4100 = the laundry only');
    assert.equal(Object.values(party(s,g.id))[0],FX.folioB.totalPaise,'1110/<Deccan> still the company folio');
    const docs=M.gstr1Documents(s,'2000-01-01','2099-12-31').docs.filter(d=>d.meta.booking===g.id);
    assert.equal(docs.length,FX.gstDocuments);assert.equal(docTotal(docs[0])+docTotal(docs[1]),totals(b).total,'Σ invoices = the stay');
  });

  test(T+'R8 / R9 / R6 on the fixture stay: company lines = folio B (₹9,000), the study input bills the stay once (₹11,300), high-balance tests the company folio',()=>{
    const g=golden(),b=bk(g.s,g.id);
    // R8
    assert.equal(M.sfCompanyLines(g.s,b,g.co).reduce((x,l)=>x+l.amount,0),FX.folioB.taxablePaise,'R8: only the three nights');
    // R9 on the settled stay
    const s=settle(g);
    const bills=AN.buildAnInput(s,{rooms:[],stays:[],bills:[],receipts:[]}).bills.filter(x=>x.stay===g.id);
    assert.equal(bills.length,1,'R9: one bill for the stay');assert.equal(bills[0].totalPaise,FX.stay.totalPaise);assert.equal(bills[0].paidPaise,FX.stay.totalPaise);
    // R6 on the in-house stay
    const row=st=>D.highBalanceRows(st,500000).find(r=>r.cells.room===b.room);
    let hb=row(g.s);
    assert.ok(hb,'listed');assert.equal(hb.cells.balance,FX.stay.totalPaise-FX.payments.advanceUpiPaise,'stay balance once');
    assert.equal(hb.cells.exception,'');
    const low={...g.s,groupsX:{...g.s.groupsX,companies:g.s.groupsX.companies.map(c=>c.id===g.co?{...c,creditLimit:FX.folioB.totalPaise-45000}:c)}};
    hb=row(low);assert.equal(hb.cells.exception,'Company folio above its credit limit');assert.equal(hb.cells.overBy,45000);
  });
}
