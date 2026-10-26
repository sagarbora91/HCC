// MA-11 · Review of the masters wave (MA-1…MA-7 + Wave B): fixes found by the review, each with its test. Names prefixed [MA11].
// Server counterparts: 08 Production tests (PaymentMethodMasterTests, MealPlanNightCloseTests, RstSeriesMasterTests, BookingSourceRetiredTests,
// HolidayLockTests, MastersMigrationUpgradeTests). Report: 05 Delivery Evidence/Masters/MA_Review.md.
export function register({test,M,assert}){
  const {seed,execute,mstRows,mstByCode,mstSpec,masterSpecs,mstVisibleFields,mstVisibleRow,mstSeesMoney,mstMoneyFields,fdmSeriesFor,fdmIssued,fdmPreview,MstError,mstReasonOf}=M;
  const reason=r=>e=>e instanceof MstError&&e.reason===r&&mstReasonOf(e)===r;
  const P=s=>M.posSlice(s);
  const ord=(s,id)=>P(s).orders.find(o=>o.id===id);
  const table=(s,tbl,items,u='cashier')=>{
    s=execute(s,u,{type:'POS_ORDER_OPEN',outlet:'restaurant',table:tbl,covers:2,guest:'Walk-in · MA-11'});const id=P(s).orders.at(-1).id;
    for(const [item,qty] of items)s=execute(s,u,{type:'POS_ITEM_ADD',id,item,qty});
    return [execute(s,u,{type:'POS_KOT_SEND',id}),id];
  };
  const upi=(s,id)=>execute(s,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'UPI',reference:'DEMO-UPI-'+id});
  const save=(s,u,master,row)=>execute(s,u,{type:'MST_MASTER_SAVE',master,row});

  // ── Numbering (MA-5 vs Wave B): RST is a row of the numbering-series master ──
  test('[MA11] RST restaurant invoices are a row of the numbering series (seeded RST, continues posX.rstSeq: RST/2627/0000001…)',()=>{
    const s0=seed(),r=mstByCode(s0,'numberSeries','RST');
    assert.deepEqual([r.document,r.prefix,r.padding,r.reset],['RST','RST',7,'FY']);assert.equal(fdmSeriesFor(s0,'RST').id,'nsr-rst');
    assert.equal(fdmPreview(s0,'RST'),'RST/2627/0000001');
    let [s,id]=table(s0,'T3',[['m-tea',1]]);s=upi(s,id);
    assert.equal(ord(s,id).settlement.invoiceNo,'RST/2627/0000001');assert.deepEqual(P(s).rstSeq,{'2627':1});
    assert.equal(fdmIssued(s,mstByCode(s,'numberSeries','RST')).count,1);assert.equal(fdmPreview(s,'RST'),'RST/2627/0000002');
    // In use: its numbering fields cannot change any more (sequential, no gaps).
    assert.throws(()=>save(s,'owner','numberSeries',{...mstByCode(s,'numberSeries','RST'),padding:6}),e=>/has issued 1 number/.test(e.message));});
  test('[MA11] an Owner RST series from today numbers the next outlet bill (read the same way as the tax-invoice series)',()=>{
    let s=save(seed(),'owner','numberSeries',{code:'RST-OUT',name:'Restaurant (outlet)',document:'RST',prefix:'RS',start:1,padding:5,reset:'FY',activeFrom:seed().date});
    let id;[s,id]=table(s,'T4',[['m-tea',1]]);s=upi(s,id);
    assert.equal(ord(s,id).settlement.invoiceNo,'RS/2627/00001');assert.equal(P(s).rstSeq?.['2627'],undefined);});
  test('[MA11] receipt / KOT / registration-card series are marked reserved in the list help',()=>{
    assert.deepEqual([...M.FDM_RESERVED_DOCS],['RCP','KOT','REG']);assert.equal(M.fdmDocReserved('KOT'),true);assert.equal(M.fdmDocReserved('RST'),false);
    assert.match(mstSpec('numberSeries').help,/reserved: nothing is numbered from them yet/);});

  // ── Booking sources (MA-5): BOOK refuses a retired source ──
  test('[MA11] BOOK refuses a retired booking source (MASTER_RETIRED); other sources and unknown channels still book',()=>{
    let s=seed();const w=mstByCode(s,'bookingSources','WALKIN');s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'bookingSources',id:w.id});
    const book=(t,source,room)=>execute(t,'reception',{type:'BOOK',guest:'Source Test',room,arrival:t.date,departure:'2026-10-26',people:1,source});
    assert.throws(()=>book(s,'Walk-in','105'),e=>reason('MASTER_RETIRED')(e)&&/Walk-in is retired — choose an active booking source\./.test(e.message));
    assert.throws(()=>book(s,'walk-in','105'),reason('MASTER_RETIRED'));
    assert.equal(book(s,'Phone','105').bookings.at(-1).source,'Phone');
    assert.equal(book(s,'Newspaper (fictional)','105').bookings.at(-1).source,'Newspaper (fictional)');});

  // ── RBAC (DEC-058 / DEC-061): money fields never reach housekeeping or maintenance, in ANY registered master form ──
  const MONEYISH=/price|rate|amount|charge|commission|surcharge|deposit|balance|limit|paise|addOn|pay|pct|bps|extraAdult|extraChild|extraBed/i;
  test('[MA11] every registered master: housekeeping and maintenance see no money field (form fields and row values)',()=>{
    const s=seed();
    for(const uid of ['housekeeper','maint'])assert.equal(mstSeesMoney(s,uid),false,uid);
    assert.equal(mstSeesMoney(s,'owner'),true);
    const specs=masterSpecs();assert.ok(specs.length>=28,'all masters registered');
    for(const spec of specs){
      const money=new Set(mstMoneyFields(spec).map(f=>f.key));
      // A field that holds money or a money rate must be a money field (type money / datedMoney, or flagged money:true).
      for(const f of spec.fields)if(f.type==='money'||f.type==='datedMoney'||f.type==='percent'||(MONEYISH.test(f.key)&&!['soldOutToday'].includes(f.key)&&f.type!=='toggle'&&f.type!=='text'))
        assert.ok(money.has(f.key),`${spec.id}.${f.key} (${f.type}) must be a money field`);
      for(const uid of ['housekeeper','maint']){
        const shown=mstVisibleFields(s,uid,spec).map(f=>f.key);
        for(const k of money)assert.ok(!shown.includes(k),`${spec.id}.${k} visible to ${uid}`);
        for(const row of mstRows(s,spec.id))for(const k of money)assert.equal(k in mstVisibleRow(s,uid,spec,row),false,`${spec.id} row ${row.code}.${k} sent to ${uid}`);
      }
    }});
}
