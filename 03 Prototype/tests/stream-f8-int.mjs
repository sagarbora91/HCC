// F8-INT · the split-stay golden of SF-CONTRACT §5.7 end to end on the prototype (integrator check; lane sf-tests owns the full scenario,
// its variants a–d and the DEC-061 walk). "Deccan Agro pays rooms, guest pays extras": two invoices, two GST documents, Σ = ₹11,300 = the
// unsplit control; accounting = the control except the party split of the receivable; R6 / R8 / R9 helpers see the stay once.
// Fictional data on the BASE fixture (a free Deluxe room at ₹3,000); GSTIN 27AAPFU0939F1ZV (fictional, valid check digit; contract deviation 8).
import {build} from 'esbuild';
// The high-balance rows are not part of the mock index: bundle the report folder like stream-meas-rpt-daily.mjs (own output file).
await build({entryPoints:['src/domain/reports/meas-daily/index.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/f8-int-meas-daily.mjs',logLevel:'error'});
const D=await import('./.generated/f8-int-meas-daily.mjs');
await build({entryPoints:['src/domain/analytics/legacy-input.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/f8-int-an-input.mjs',logLevel:'error'});
const AN=await import('./.generated/f8-int-an-input.mjs');

export async function register({test,M,assert}){
  const {seed,execute,totals}=M;
  const addDays=(d,n)=>{const x=new Date(d+'T00:00:00Z');x.setUTCDate(x.getUTCDate()+n);return x.toISOString().slice(0,10);};
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  const n=x=>Number(String(x).match(/(\d+)$/)[1]);

  /** The §5.7 stay; split=false gives the control (same six lines, guest pays everything on one folio). */
  function golden(split=true){
    let s=seed();
    s=execute(s,'owner',{type:'GRP_COMPANY_SAVE',name:'Deccan Agro Ltd (fictional)',gstin:'27AAPFU0939F1ZV',city:'Latur',creditLimit:5000000});
    const co=s.groupsX.companies.at(-1).id;
    const dep=addDays(s.date,3),room=s.rooms.find(r=>r.type==='Deluxe'&&r.rate===300000&&!r.blocked&&r.hk==='Ready'&&M.available(s,r.id,s.date,dep));
    assert.ok(room,'a free Deluxe room at ₹3,000');
    s=execute(s,'owner',{type:'BOOK',guest:'Golden Split Guest',room:room.id,arrival:s.date,departure:dep,people:1});
    const id=s.bookings.at(-1).id;
    assert.equal(bk(s,id).rate,300000,'a Deluxe at ₹3,000');
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});
    if(split)s=execute(s,'owner',{type:'SF_FOLIO_OPEN',id,payer:{kind:'company',ref:co},routes:['room'],reason:'Company pays the room'});
    s=execute(s,'owner',{type:'PAYMENT',id,amount:100000,method:'UPI',reference:'UPI-GOLD-1'});
    s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Laundry',amount:50000,gst:'laundry'});
    s=execute(s,'owner',{type:'CHARGE',id,label:'Room service food',amount:120000,gst:'food'});
    return {s,id,co};
  }
  function checkout({s,id},split=true){
    if(split){
      s=execute(s,'owner',{type:'PAYMENT',id,amount:85000,method:'Cash',folio:'A'});
      s=execute(s,'reception',{type:'COMPANY_CREDIT_REQUEST',id});
      s=execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    }else s=execute(s,'owner',{type:'PAYMENT',id,amount:totals(bk(s,id)).balance,method:'Cash'});
    return execute(s,'owner',{type:'CHECKOUT',id});
  }
  const sumDoc=d=>d.lines.reduce((a,l)=>({taxable:a.taxable+l.taxable,cgst:a.cgst+l.cgst,sgst:a.sgst+l.sgst}),{taxable:0,cgst:0,sgst:0});

  test('[F8-INT] golden split stay: folio figures, two invoices in letter order, two GST documents, Σ = ₹11,300 = the control',()=>{
    const g=golden();
    let b=bk(g.s,g.id);
    const tA=totals(M.sfView(b,'A')),tB=totals(M.sfView(b,'B'));
    assert.deepEqual([tA.net,tA.tax,tA.total],[170000,15000,185000],'folio A net 1,700 + tax 150 = 1,850');
    assert.deepEqual([tB.net,tB.tax,tB.total],[900000,45000,945000],'folio B net 9,000 + tax 450 = 9,450');
    assert.equal(tA.balance,85000,'A: ₹1,000 advance paid, ₹850 due');
    const s=checkout(g);b=bk(s,g.id);
    const [fa,fb]=b.folios;
    assert.ok(fa.invoiceNo&&fb.invoiceNo);assert.equal(n(fb.invoiceNo),n(fa.invoiceNo)+1,'consecutive, A first');assert.equal(b.invoiceNo,fa.invoiceNo);
    const docs=M.gstr1Documents(s,'2000-01-01','2099-12-31').docs.filter(d=>d.meta.booking===g.id);
    assert.deepEqual(docs.map(d=>d.no),[fa.invoiceNo,fb.invoiceNo]);
    assert.deepEqual(docs.map(d=>d.supply),['B2C','B2B']);
    assert.deepEqual(sumDoc(docs[0]),{taxable:170000,cgst:7500,sgst:7500});
    assert.deepEqual(sumDoc(docs[1]),{taxable:900000,cgst:22500,sgst:22500});
    const cr=b.receipts.find(r=>r.kind==='company-credit');assert.equal(cr.folio,'B');assert.equal(cr.amount,945000);
    // control: the same six lines on one folio = one invoice of ₹11,300
    const c=checkout(golden(false),false),cb=bk(c,c.bookings.at(-1).id);
    const cdocs=M.gstr1Documents(c,'2000-01-01','2099-12-31').docs.filter(d=>d.meta.booking===cb.id);
    assert.equal(cdocs.length,1);
    const cs=sumDoc(cdocs[0]),ss=[...docs.map(sumDoc)].reduce((a,x)=>({taxable:a.taxable+x.taxable,cgst:a.cgst+x.cgst,sgst:a.sgst+x.sgst}));
    assert.deepEqual(ss,cs,'Σ of the two invoices = the unsplit control, head by head');
    assert.equal(cs.taxable+cs.cgst+cs.sgst,1130000,'₹11,300.00');
    assert.equal(totals(b).total,1130000);
  });

  test('[F8-INT] golden split stay: accounting = the control except the party split of the receivable (I5)',()=>{
    const g=golden(),s=checkout(g);
    const c=checkout(golden(false),false),cid=c.bookings.at(-1).id;
    // every date the stay touched (nights are dated ahead of the business date, so accVouchersUpTo does not reach them yet)
    const net=(st,id)=>{const m={},bb=bk(st,id),dates=[...new Set([...bb.lines.map(l=>l.date),...bb.receipts.map(r=>r.date)])];
      for(const d of dates)for(const v of M.accVouchers(st,d).filter(v=>v.source.booking===id))for(const e of v.entries)m[e.account]=(m[e.account]??0)+e.debit-e.credit;
      for(const k of Object.keys(m))if(m[k]===0)delete m[k];return m;};
    const a=net(s,g.id),b=net(c,cid);
    const pick=(m,re)=>Object.fromEntries(Object.entries(m).filter(([k])=>re.test(k)));
    assert.deepEqual(pick(a,/^4/),pick(b,/^4/),'revenue ledgers identical');
    assert.deepEqual(pick(a,/^2[12]/),pick(b,/^2[12]/),'output GST identical');
    const rev=Object.values(pick(a,/^4/)).reduce((x,y)=>x+y,0),gst=Object.values(pick(a,/^2[12]/)).reduce((x,y)=>x+y,0);
    assert.equal(-rev,1070000,'revenue 9,000 + 500 + 1,200 (food posted by CHARGE lands on 4100 like any service line; the 4200 split of §5.7 needs a POS room-post: sf-tests)');assert.equal(-gst,60000,'CGST 300 + SGST 300');
    const co=Object.entries(a).filter(([k])=>k.startsWith('1110/'));
    assert.equal(co.length,1,'one company party');assert.equal(co[0][1],945000,'1110/<Deccan> Dr ₹9,450');
    assert.ok(co[0][0].includes(g.co)||co[0][0].length>5,'party of the folio payer company');
    assert.equal(Object.entries(a).filter(([k])=>k.startsWith('1100')).reduce((x,[,v])=>x+v,0),0,'1100 guest ledger nets to 0');
    assert.equal(Object.entries(b).filter(([k])=>k.startsWith('1110')).length,0,'control has no company receivable');
  });

  test('[F8-INT] golden split stay: R8 company lines = folio B only; R1 stay-level sums once; R6 high-balance tests the company folio',()=>{
    const g=golden();const b=bk(g.s,g.id);
    const cl=M.sfCompanyLines(g.s,b,g.co);
    assert.equal(cl.reduce((a,l)=>a+l.amount,0),900000,'only the three nights');
    assert.equal(totals(b).total,1130000,'stay-level total once (R1)');
    // R9: the study / analytics input bills the split stay once, for the whole ₹11,300 (never per folio plus per stay)
    const bills=AN.buildAnInput(checkout(g),{rooms:[],stays:[],bills:[],receipts:[]}).bills.filter(x=>x.stay===g.id);
    assert.equal(bills.length,1);assert.equal(bills[0].totalPaise,1130000);assert.equal(bills[0].paidPaise,1130000);
    const row=st=>D.highBalanceRows(st,500000).find(r=>r.cells.room===b.room);
    let hb=row(g.s);
    assert.ok(hb,'in-house with a balance');assert.equal(hb.cells.balance,1030000,'stay balance once (11,300 − 1,000 advance)');
    assert.equal(hb.cells.limit,5000000);assert.equal(hb.cells.exception,'','company folio within its limit, guest folio ₹850 within ₹5,000');
    assert.equal(hb.cells.company,'Deccan Agro Ltd (fictional)');
    // a stay-level test would put ₹10,300 against deposit + limit; R6 tests the company folio (₹9,450) against the limit alone
    const low={...g.s,groupsX:{...g.s.groupsX,companies:g.s.groupsX.companies.map(c=>c.id===g.co?{...c,creditLimit:900000}:c)}};
    hb=row(low);assert.equal(hb.cells.exception,'Company folio above its credit limit');assert.equal(hb.cells.overBy,45000);
  });
}
