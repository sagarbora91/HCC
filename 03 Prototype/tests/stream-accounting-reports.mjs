// ACC-1 lane acc1-statements — ledger / party statement, six-column trial balance, day book (contract §6; rulebook §7.1–7.3, §G2).
// Names prefixed [ACC1-STATEMENTS]. Party ledgers and the opening entry are injected as a locked snapshot (the reports read locked
// snapshots as locked) so these tests do not depend on the other ACC-1 lanes; the last test uses the acc1-opening commands once merged.
export function register({test,M,assert}){
  const Y='2026-10-23',D='2026-10-24';
  const sumNet=(vs,acc)=>vs.flatMap(v=>v.entries).filter(e=>e.account===acc||e.account.startsWith(acc+'/')).reduce((a,e)=>a+e.debit-e.credit,0);
  const V=(no,date,type,entries,sub='test',kind='closing')=>({no,date,type,narration:'Fictional '+no,source:{kind,id:'T/'+no,sub},entries:entries.map(([account,debit,credit])=>({account,debit,credit})),unmapped:[]});
  /** Seed + a locked 1 Oct holding party-ledger vouchers (two companies under control 1110 and one no-party entry on 1110 itself). */
  function withParties(s=M.seed()){
    const d='2026-10-01';
    const vouchers=[
      V('SAL/20261001/001',d,'Sales',[['1110/CMP-A',1000000,0],['4000',0,1000000]]),
      V('RCT/20261001/001',d,'Receipt',[['1000',400000,0],['1110/CMP-B',0,400000]]),
      V('JRN/20261001/001',d,'Journal',[['1110',250000,0],['4100',0,250000]]),
    ];
    const t=M.accTotals(vouchers);const sl=M.accSlice(s);
    return {...s,accountingX:{...sl,locks:{...(sl.locks??{}),[d]:{date:d,lockedAt:'2026-10-02T10:00',lockedBy:'owner',vouchers,debit:t.debit,credit:t.credit,count:vouchers.length}}}};
  }
  const amountsNeverNegative=csv=>csv.split('\r\n').every(l=>!/(^|,)-\d/.test(l));

  test('[ACC1-STATEMENTS] statement: opening = every voucher before `from`, running balance per voucher, closing = opening + period',()=>{
    const s=M.seed(),st=M.accStatement(s,'1100',D,D);
    assert.equal(st.opening,sumNet(M.accVouchersUpTo(s,Y),'1100'));assert.ok(st.opening>0,'23 Oct sales leave a Dr guest-ledger balance');
    let run=st.opening;for(const r of st.rows){run+=r.debit-r.credit;assert.equal(r.running,run,r.no);assert.ok(r.debit>=0&&r.credit>=0);}
    assert.equal(st.debit,st.rows.reduce((a,r)=>a+r.debit,0));assert.equal(st.credit,st.rows.reduce((a,r)=>a+r.credit,0));
    assert.equal(st.closing,st.opening+st.debit-st.credit);assert.equal(st.closing,sumNet(M.accVouchersUpTo(s,D),'1100'));
    assert.ok(st.rows.length>0&&st.rows.every(r=>r.date===D));assert.equal(st.name,'Guest ledger');
    // the same closing whatever the start: the opening absorbs the earlier vouchers
    const wide=M.accStatement(s,'1100',Y,D);assert.equal(wide.closing,st.closing);assert.equal(wide.opening,sumNet(M.accVouchersUpTo(s,'2026-10-22'),'1100'));
  });
  test('[ACC1-STATEMENTS] statement rows carry the counter ledger name(s) and the voucher identity',()=>{
    const s=M.seed(),st=M.accStatement(s,'4000',Y,Y);assert.ok(st.rows.length>0);
    for(const r of st.rows){assert.equal(r.type,'Sales');assert.ok(r.no.startsWith('SAL/20261023/'));assert.match(r.counter,/Guest ledger/);assert.ok(!/Room revenue/.test(r.counter),'own ledger is not its counter');}
    assert.ok(st.closing<0,'income is a Cr balance (negative signed)');
  });
  test('[ACC1-STATEMENTS] Dr / Cr display is never negative (accDrCr, statement CSV)',()=>{
    for(const n of [123456,-98700,0]){const x=M.accDrCr(n);assert.ok(x.amount>=0);assert.equal(x.side,n>0?'Dr':n<0?'Cr':'');}
    assert.equal(M.accDrCrText(-98700),'987.00 Cr');assert.equal(M.accDrCrText(123456),'1234.56 Dr');assert.equal(M.accDrCrText(0),'0.00');
    const s=M.seed(),cr=M.accStatement(s,'4000',Y,D),csv=M.accStatementCsv(cr);
    assert.ok(amountsNeverNegative(csv),csv);assert.match(csv,/Closing balance,,,,\d+\.\d{2} Cr/);
    assert.ok(csv.split('\r\n').includes(M.ACC_STATEMENT_CSV_HEADER.join(',')));
    const dr=M.accStatementCsv(M.accStatement(s,'1100',D,D));assert.ok(amountsNeverNegative(dr));assert.match(dr,/Opening balance,,,,\d+\.\d{2} Dr/);
  });
  test('[ACC1-STATEMENTS] six-column TB: opening before `from`, period gross, closing netted; balanced; ties to the cumulative TB',()=>{
    const s=M.seed(),tb=M.accTrialBalance6(s,D,D,false);
    assert.equal(tb.detail,false);assert.ok(tb.balanced);
    const t=tb.totals;assert.equal(t.openingDebit,t.openingCredit);assert.equal(t.debit,t.credit);assert.equal(t.closingDebit,t.closingCredit);
    const per=M.accTotals(M.accVouchers(s,D));assert.equal(t.debit,per.debit);assert.equal(t.credit,per.credit);
    for(const r of tb.rows){
      assert.ok(!(r.openingDebit&&r.openingCredit)&&!(r.closingDebit&&r.closingCredit),r.account);
      assert.equal(r.closingDebit-r.closingCredit,r.openingDebit-r.openingCredit+r.debit-r.credit,r.account);
      for(const k of ['openingDebit','openingCredit','debit','credit','closingDebit','closingCredit'])assert.ok(r[k]>=0,r.account+' '+k);
    }
    const cum=Object.fromEntries(M.accTrialBalance(M.accVouchersUpTo(s,D)).map(r=>[r.account,r.balance]));
    for(const r of tb.rows)assert.equal(r.closingDebit-r.closingCredit,cum[r.account]??0,r.account);
    const prev=Object.fromEntries(M.accTrialBalance(M.accVouchersUpTo(s,Y)).map(r=>[r.account,r.balance]));
    for(const r of tb.rows)assert.equal(r.openingDebit-r.openingCredit,prev[r.account]??0,r.account);
    // a wider window has the same closing column
    const wide=M.accTrialBalance6(s,Y,D,false);assert.deepEqual(wide.rows.map(r=>[r.account,r.closingDebit,r.closingCredit]),tb.rows.map(r=>[r.account,r.closingDebit,r.closingCredit]));
    assert.equal(tb.rows.find(r=>r.account==='1100').name,'Guest ledger');assert.ok(tb.rows.find(r=>r.account==='1100').group);
  });
  test('[ACC1-STATEMENTS] six-column TB: control summary vs party detail (locked snapshot read as locked)',()=>{
    const s=withParties(),from='2026-10-01',to='2026-10-01';
    const sum=M.accTrialBalance6(s,from,to,false),det=M.accTrialBalance6(s,from,to,true);
    assert.ok(sum.balanced&&det.balanced);
    const c=sum.rows.find(r=>r.account==='1110');assert.deepEqual([c.debit,c.credit,c.closingDebit,c.closingCredit],[1250000,400000,850000,0]);
    assert.ok(!sum.rows.some(r=>r.account.includes('/')),'summary has no party rows');assert.equal(c.control,undefined);
    const rows=det.rows.filter(r=>r.account==='1110'||r.control==='1110');
    assert.deepEqual(rows.map(r=>r.account),['1110','1110/CMP-A','1110/CMP-B'],'control row (direct postings) then its parties');
    assert.deepEqual(rows.map(r=>[r.closingDebit,r.closingCredit]),[[250000,0],[1000000,0],[0,400000]]);
    assert.equal(rows[0].control,undefined);assert.equal(rows[1].control,'1110');assert.match(rows[1].name,/^Company receivables \(city ledger\) · /);
    assert.equal(rows[1].group,rows[0].group,'a party takes its control group');
    assert.equal(rows.reduce((a,r)=>a+r.closingDebit-r.closingCredit,0),c.closingDebit-c.closingCredit,'detail ties to the control');
    assert.equal(det.totals.debit,sum.totals.debit);assert.equal(det.totals.credit,sum.totals.credit);
    // the locked month is the opening of a later window
    const later=M.accTrialBalance6(s,'2026-10-02',D,false).rows.find(r=>r.account==='1110');assert.deepEqual([later.openingDebit,later.debit,later.credit],[850000,0,0]);
    const csv=M.accTrialBalance6Csv(det);assert.ok(csv.includes('1110/CMP-B,'));assert.ok(csv.split('\r\n').includes(M.ACC_TB6_CSV_HEADER.join(',')));assert.match(csv,/\r\nTotal,,,,/);assert.ok(amountsNeverNegative(csv));
  });
  test('[ACC1-STATEMENTS] party statement: a control takes its parties, a party ledger only itself',()=>{
    const s=withParties(),f='2026-10-01';
    const ctl=M.accStatement(s,'1110',f,f);assert.deepEqual(ctl.rows.map(r=>r.no),['JRN/20261001/001','RCT/20261001/001','SAL/20261001/001']);
    assert.deepEqual(ctl.rows.map(r=>r.running),[250000,-150000,850000]);assert.equal(ctl.closing,850000);
    const a=M.accStatement(s,'1110/CMP-A',f,D);assert.equal(a.rows.length,1);assert.equal(a.rows[0].counter,'Room revenue');assert.equal(a.closing,1000000);assert.match(a.name,/Company receivables/);
    const b=M.accStatement(s,'1110/CMP-B',f,D);assert.equal(b.closing,-400000);assert.equal(b.rows[0].counter,'Cash in hand');assert.equal(M.accDrCr(b.closing).side,'Cr');
    const later=M.accStatement(s,'1110/CMP-A','2026-10-02',D);assert.equal(later.opening,1000000);assert.equal(later.rows.length,0);assert.equal(later.closing,1000000);
  });
  test('[ACC1-STATEMENTS] an opening voucher dated on `from` is the opening, not a period movement; openingPending follows §G2',()=>{
    const asOf='2026-04-01',sl0=M.accSlice(M.seed());
    const ob=V('JRN/20260401/000',asOf,'Journal',[['1000',500000,0],['1030/BA-HDFC',2000000,0],['3100',0,2500000]],'opening-entry','opening');
    let s=M.seed();s={...s,accountingX:{...sl0,locks:{[asOf]:{date:asOf,lockedAt:'x',lockedBy:'owner',vouchers:[ob],debit:2500000,credit:2500000,count:1}}}};
    const tb=M.accTrialBalance6(s,asOf,D,false),r=Object.fromEntries(tb.rows.map(x=>[x.account,x]));
    assert.equal(r['3100'].openingCredit,2500000);assert.equal(r['3100'].credit,0);assert.equal(r['1030'].openingDebit,2000000);
    assert.equal(tb.totals.openingDebit,2500000);assert.ok(tb.balanced);
    assert.ok(!M.accDayBook(s,asOf,D).rows.some(x=>x.no==='JRN/20260401/000'),'not in the day book from its own date');
    assert.ok(M.accDayBook(s,'2026-03-31',D).rows.some(x=>x.no==='JRN/20260401/000'),'a period movement when the range starts earlier');
    assert.equal(M.accStatement(s,'3100',asOf,D).opening,-2500000);
    // §G2: pending until an approved opening entry with nothing left on 1400
    assert.equal(tb.openingPending,true,'no opening entry');
    const op=(status,lines)=>({...s,accountingX:{...M.accSlice(s),opening:{asOf,lines,status,by:'manager',at:'2026-10-24T10:00'}}});
    const bal=[{account:'1000',debit:500000,credit:0},{account:'3100',debit:0,credit:500000}];
    assert.equal(M.accTrialBalance6(op('draft',bal),asOf,D,false).openingPending,true,'draft');
    assert.equal(M.accTrialBalance6(op('approved',bal),asOf,D,false).openingPending,false,'approved and balanced');
    assert.equal(M.accRptOpeningPending(op('approved',[...bal,{account:'1000',debit:1000,credit:0}])),true,'gap parked in 1400');
    assert.equal(M.accRptOpeningPending(op('approved',[...bal,{account:'1000',debit:1000,credit:0},{account:'1400',debit:0,credit:1000}])),true,'1400 carries the gap');
  });
  test('[ACC1-STATEMENTS] day book: every voucher in the range in `date, no` order, one row per entry, balanced; CSV',()=>{
    const s=withParties(),db=M.accDayBook(s,'2026-10-01',D);
    const vs=M.accDates(s).filter(d=>d>='2026-10-01'&&d<=D).flatMap(d=>M.accVouchers(s,d));
    assert.equal(db.rows.length,vs.reduce((a,v)=>a+v.entries.length,0));assert.equal(db.vouchers,vs.length);assert.ok(db.balanced);assert.equal(db.debit,db.credit);
    for(let i=1;i<db.rows.length;i++){const a=db.rows[i-1],b=db.rows[i];assert.ok(a.date<b.date||(a.date===b.date&&a.no<=b.no),a.no+' then '+b.no);}
    assert.deepEqual([...new Set(db.rows.filter(r=>r.date==='2026-10-01').map(r=>r.no))],['JRN/20261001/001','RCT/20261001/001','SAL/20261001/001']);
    const one=M.accDayBook(s,D,D);assert.ok(one.rows.every(r=>r.date===D));assert.equal(one.debit,M.accTotals(M.accVouchers(s,D)).debit);
    assert.ok(one.rows.every(r=>r.ledger&&r.source));
    const csv=M.accDayBookCsv(one),lines=csv.split('\r\n');assert.equal(lines[0],M.ACC_DAYBOOK_CSV_HEADER.join(','));assert.equal(lines.length,one.rows.length+2);assert.match(lines.at(-1),/^Total,/);
    assert.equal(M.accDayBook(s,'2026-09-01','2026-09-30').rows.length,0);
  });
  test('[ACC1-STATEMENTS] contract §6 field sets are exact',()=>{
    const s=withParties(),st=M.accStatement(s,'1110','2026-10-01',D),tb=M.accTrialBalance6(s,'2026-10-01',D,true);
    assert.deepEqual(Object.keys(st).sort(),['account','closing','credit','debit','from','name','opening','rows','to']);
    assert.deepEqual(Object.keys(st.rows[0]).sort(),['counter','credit','date','debit','narration','no','running','type']);
    assert.deepEqual(Object.keys(tb).sort(),['balanced','detail','from','openingPending','rows','to','totals']);
    assert.deepEqual(Object.keys(tb.totals).sort(),['closingCredit','closingDebit','credit','debit','openingCredit','openingDebit']);
    assert.deepEqual(Object.keys(tb.rows.find(r=>r.control)).sort(),['account','closingCredit','closingDebit','control','credit','debit','group','name','openingCredit','openingDebit']);
  });
  test('[ACC1-STATEMENTS] with the acc1-opening lane merged: the approved opening entry is the opening of a report from its date',()=>{
    if(typeof M.accOpeningVoucher!=='function')return; // runs on claude/acc1 after the opening lane lands (contract §5 commands)
    let s=M.seed();
    s=M.execute(s,'manager',{type:'ACC_OPENING_SAVE',asOf:'2026-04-01',lines:[{account:'1000',debit:500000,credit:0},{account:'3100',debit:0,credit:500000}]});
    const asOf=M.accSlice(s).opening.asOf;assert.equal(M.accTrialBalance6(s,asOf,D,false).openingPending,true);
    s=M.execute(s,'owner',{type:'ACC_OPENING_APPROVE'});
    const tb=M.accTrialBalance6(s,asOf,D,false);assert.equal(tb.openingPending,false);
    assert.equal(tb.rows.find(r=>r.account==='3100').openingCredit,500000);assert.ok(tb.balanced);
    assert.equal(M.accStatement(s,'3100',asOf,D).opening,-500000);
  });
}
