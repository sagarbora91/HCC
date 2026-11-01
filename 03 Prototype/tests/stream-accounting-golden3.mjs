// ACC-3 acc3-tests · the April 2026 golden month through the ACC-3 reports. Names prefixed [ACC3-TESTS].
// Expectations: src/fixtures/ext/accounting-rulebook.ts `ACC_RB_REPORTS`, worked by hand from rulebook §8.1–§8.6 (never exported from the code under test).
// STATUS AT WRITING: the report lanes (acc3-pl, acc3-ageing, acc3-tally) had not landed, so the engine tests are written against the contract names of
// 02 Planning/agents/ACC3-REPORTS-CONTRACT.md §2–§4 (accPl, accBalanceSheet, accCashRatio, accAgeing, accGstSummary, accTallyMasters, accTallyVouchers) and
// FAIL with "waits for lane ..." until the selector is exported by the mock service. Argument shapes assumed: accPl(s,from,to), accBalanceSheet(s,asOf),
// accCashRatio(s,fy), accAgeing(s,asOf,side), accGstSummary(s,month), accTallyMasters(s,fy), accTallyVouchers(s,from,to) — change `call()` below only.
import {build} from 'esbuild';
await build({entryPoints:['src/fixtures/ext/accounting-rulebook.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/acc3-rulebook-expect.mjs',logLevel:'error'});
const {ACC_RB_REPORTS:X}=await import('./.generated/acc3-rulebook-expect.mjs');

export function register({test,M,assert}){
  const T='[ACC3-TESTS] ';
  const R=r=>Math.round(r*100);
  const waits=(laneName,what)=>new Error(`${T}waits for lane ${laneName}: ${what}`);
  const LANE={accPl:'acc3-pl',accBalanceSheet:'acc3-pl',accCashRatio:'acc3-pl',accAgeing:'acc3-ageing',accGstSummary:'acc3-ageing',accTallyMasters:'acc3-tally',accTallyVouchers:'acc3-tally'};
  const call=(name,...args)=>{if(typeof M[name]!=='function')throw waits(LANE[name]??'?',`${name} is not exported by the mock service yet (merge the lane, then register it in services/mock/index.ts)`);return M[name](...args);};
  let cache;
  const month=()=>{if(cache)return cache;try{return cache=M.loadScenario('ACC-RULEBOOK');}catch(e){throw waits('acc2-*',`scenario ACC-RULEBOOK cannot load — ${String(e.message).split('\n')[0]}`);}};
  const END=X.readTo; // the pay run is locked on 1 May; nothing else posts that day
  const ctl=c=>String(c).split('/')[0];
  const sumLines=(lines,codes)=>codes.reduce((a,c)=>a+lines.filter(l=>ctl(l.code)===c).reduce((b,l)=>b+l.paise,0),0);
  const byCode=lines=>{const m={};for(const l of lines)m[ctl(l.code)]=(m[ctl(l.code)]??0)+l.paise;return m;};
  const has=(lines,expect,what)=>{const got=byCode(lines);for(const [c,v] of Object.entries(expect))assert.equal(got[c]??0,v,`${what} ${c}`);};
  const num=v=>typeof v==='number'?v:(v&&typeof v==='object'?(v.taxable??v.taxableValue??v.total):NaN);
  const near=(got,pct)=>Math.abs(got-pct)<0.01||Math.abs(got-pct*100)<1; // percent, or basis points

  // ───────── self-checks of the encoded expectations (no engine needed) ─────────
  test(T+'expectations tie to the rulebook: profit 1,40,400 = revenue − expenses; balance sheet 79,27,900 on both sides; GST 29,000 − 100 = 28,900',()=>{
    const p=X.pl,sum=o=>Object.values(o).reduce((a,v)=>a+v,0);
    for(const k of ['revenue','otherIncome','consumption','employee','finance','depreciation','other'])assert.equal(sum(p[k].lines),p[k].total,k);
    assert.equal(p.consumption.total+p.employee.total+p.finance.total+p.depreciation.total+p.other.total,p.expensesTotal);
    assert.equal(p.revenue.total+p.otherIncome.total-p.expensesTotal,p.profitBeforeTax);assert.equal(p.profitBeforeTax,R(140400));
    const b=X.balanceSheet;
    assert.equal(b.ownersFunds.lines['3100']+b.ownersFunds.profit+b.ownersFunds.lines['3000'],b.ownersFunds.total);
    for(const k of ['nonCurrentLiabilities','currentLiabilities','nonCurrentAssets','currentAssets'])assert.equal(sum(b[k].lines),b[k].total,k);
    assert.equal(b.ownersFunds.total+b.nonCurrentLiabilities.total+b.currentLiabilities.total,b.liabilitiesTotal);
    assert.equal(b.nonCurrentAssets.total+b.currentAssets.total,b.assetsTotal);assert.equal(b.liabilitiesTotal,b.assetsTotal);assert.equal(b.assetsTotal,R(7927900));
    assert.equal(X.gst.output.cgst+X.gst.output.sgst,X.gst.output.total);assert.equal(X.gst.output.total-X.gst.tcsCredit,X.gst.challan);assert.equal(X.gst.challan,R(28900));
    const a=X.ageing;assert.equal(a.payable30Apr.rows['EV-MORNING'].open+a.payable30Apr.rows['EV-KAVERI'].open,a.payable30Apr.open);
    assert.equal(R(25000)+R(59000)-R(60000),R(24000),'Morning: opening 25,000 + bill 59,000 − paid 60,000');
    assert.equal(R(40000)+R(63000)-R(40000)-R(63000),0,'Deccan Agro is nil at 30 April');
    const c=X.cashRatio;assert.ok(near(c.cashReceipts/c.totalReceipts*100,c.receiptsPct)&&near(c.cashPayments/c.totalPayments*100,c.paymentsPct));
  });

  // ───────── P&L, balance sheet, cash ratio (acc3-pl) ─────────
  test(T+'P&L April 2026 (§8.4): every line, every bucket, profit ₹1,40,400; the Tally-group view is present',()=>{
    const s=month(),p=call('accPl',s,X.from,END);
    assert.equal(p.from,X.from);
    has(p.revenue.lines,X.pl.revenue.lines,'revenue');assert.equal(p.revenue.total,X.pl.revenue.total);
    assert.equal(p.otherIncome.total,0);
    for(const k of ['consumption','employee','finance','depreciation','other']){
      has(p.expenses[k].lines,X.pl[k].lines,k);assert.equal(p.expenses[k].total,X.pl[k].total,k+' total');}
    assert.equal(p.expenses.total,X.pl.expensesTotal);assert.equal(p.profitBeforeTax,X.pl.profitBeforeTax);
    assert.equal(p.profitBeforeTax,R(140400));
    assert.ok(Array.isArray(p.byTallyGroup)&&p.byTallyGroup.length>0,'second view by Tally group');
    assert.equal(typeof p.provisional,'boolean');
  });
  test(T+'P&L: depreciation is nil in April (a year-end entry), and a range inside the month moves only that range',()=>{
    const s=month(),full=call('accPl',s,X.from,END);assert.equal(full.expenses.depreciation.total,0);
    const first=call('accPl',s,'2026-04-01','2026-04-04');
    assert.ok(first.revenue.total<full.revenue.total,'4 days earn less than the month');
  });
  test(T+'Balance sheet 30 April 2026 (§8.5): ₹79,27,900 both sides, balanced, nothing in Suspense or the opening difference, every block',()=>{
    const s=month(),b=call('accBalanceSheet',s,END),E=X.balanceSheet;
    assert.equal(b.liabilities.total,R(7927900));assert.equal(b.assets.total,R(7927900));assert.equal(b.balanced,true);
    assert.equal(b.suspense,0);assert.equal(b.openingDifference,0);
    assert.equal(b.liabilities.ownersFunds.total,E.ownersFunds.total);
    assert.equal(sumLines(b.liabilities.ownersFunds.lines,['3100']),E.ownersFunds.lines['3100']);
    assert.equal(b.liabilities.nonCurrent.total,E.nonCurrentLiabilities.total);has(b.liabilities.nonCurrent.lines,E.nonCurrentLiabilities.lines,'non-current liabilities');
    assert.equal(b.liabilities.current.total,E.currentLiabilities.total);
    // creditors 66,000 (Morning 24,000 + Kaveri 42,000), duties & taxes 75,000, advance deposits 31,500 — by code, whichever sub-lines the report shows
    const cur=byCode(b.liabilities.current.lines);
    assert.equal(cur['2200']??0,E.currentLiabilities.lines['2200'],'sundry creditors');assert.equal(cur['2000']??0,E.currentLiabilities.lines['2000'],'advance deposits');
    assert.equal(['2110','2120','2310','2320','2330'].reduce((a,c)=>a+(cur[c]??0),0),R(75000),'duties & taxes: output GST 29,000 + PF/ESIC/PT 46,000');
    assert.equal(b.assets.nonCurrent.total,E.nonCurrentAssets.total);
    assert.equal(b.assets.current.total,E.currentAssets.total);has(b.assets.current.lines,E.currentAssets.lines,'current assets');
  });
  test(T+'Balance sheet on 1 April 2026 balances (the approved opening entry, ₹77,40,000 both sides)',()=>{
    const s=month(),b=call('accBalanceSheet',s,'2026-04-01');
    assert.equal(b.assets.total,b.liabilities.total);assert.equal(b.balanced,true);
  });
  test(T+'Cash ratio FY 2026-27 (§7.9): cash receipts 1,52,000 of 5,97,860 received; cash payments 13,000 of 4,37,090; receipts flagged above 5 %',()=>{
    const s=month(),c=call('accCashRatio',s,X.fy),E=X.cashRatio;
    assert.equal(c.cashReceipts,E.cashReceipts);assert.equal(c.totalReceipts,E.totalReceipts);assert.equal(c.cashPayments,E.cashPayments);assert.equal(c.totalPayments,E.totalPayments);
    assert.ok(near(c.receiptsPct,E.receiptsPct),'receipts % '+c.receiptsPct);assert.ok(near(c.paymentsPct,E.paymentsPct),'payments % '+c.paymentsPct);
    assert.equal(c.flagged,true);
  });

  // ───────── ageing (acc3-ageing) ─────────
  const rowOf=(a,pred)=>a.rows.find(pred);
  test(T+'Receivable ageing 30 April 2026: Deccan Agro paid in full on 30 April (T7b) → no open party, totals nil',()=>{
    const s=month(),a=call('accAgeing',s,'2026-04-30','receivable');
    assert.equal(a.side,'receivable');assert.equal(a.asOf,'2026-04-30');
    assert.equal(a.rows.filter(r=>r.open!==0).length,0);assert.equal(a.totals.open,0);
  });
  test(T+'Receivable ageing 28 April: Deccan Agro ₹63,000 (₹60,000 + GST) and Goibibo ₹21,000, both in 0–30; the paid opening ₹40,000 is gone; totals tie',()=>{
    const s=month(),a=call('accAgeing',s,'2026-04-28','receivable'),E=X.ageing.receivable28Apr;
    const d=rowOf(a,r=>r.control==='1110'&&/Deccan/.test(r.name)),g=rowOf(a,r=>r.control==='1115'&&/Goibibo/i.test(r.name));
    assert.ok(d&&g,'Deccan Agro and Goibibo rows');
    assert.equal(d.open,E.rows['1110'].open);assert.equal(d.b0_30,E.rows['1110'].b0_30);assert.equal(d.b31_60+d.b61_90+d.b90,0);
    assert.equal(g.open,E.rows['1115'].open);assert.equal(g.b0_30,E.rows['1115'].b0_30);
    const bill=d.bills.filter(b=>b.open>0);assert.equal(bill.length,6,'six company folios (one per delegate room), 10,500 each');assert.equal(bill.reduce((a,b)=>a+b.open,0),R(63000));assert.ok(bill.every(b=>b.days===E.rows['1110'].days));
    assert.ok(!d.bills.some(b=>b.ref==='opening'&&b.open>0),'the opening ₹40,000 was paid on 10 April (oldest first)');
    assert.equal(g.bills.filter(b=>b.open>0)[0].days,E.rows['1115'].days);
    assert.equal(a.totals.open,E.open);assert.equal(a.totals.b0_30,E.b0_30);assert.equal(a.rows.reduce((x,r)=>x+r.open,0),a.totals.open);
  });
  test(T+'Payable ageing 30 April: Morning Supplies ₹24,000 (the 5 April bill, oldest-first after the 25,000 opening), Kaveri Textiles ₹42,000; all 0–30; total ₹66,000',()=>{
    const s=month(),a=call('accAgeing',s,'2026-04-30','payable'),E=X.ageing.payable30Apr;
    assert.equal(a.side,'payable');
    const m=rowOf(a,r=>r.party==='EV-MORNING'),k=rowOf(a,r=>r.party==='EV-KAVERI');assert.ok(m&&k,'both vendors');
    assert.equal(m.open,E.rows['EV-MORNING'].open);assert.equal(m.b0_30,m.open);assert.equal(k.open,E.rows['EV-KAVERI'].open);assert.equal(k.b0_30,k.open);
    const mb=m.bills.filter(b=>b.open>0);assert.equal(mb.length,1);assert.equal(mb[0].amount,R(59000));assert.equal(mb[0].paid,R(35000));assert.equal(mb[0].open,R(24000));assert.equal(mb[0].days,E.rows['EV-MORNING'].days);
    assert.ok(!m.bills.some(b=>b.ref==='opening'&&b.open>0),'the opening ₹25,000 is the oldest item and is cleared first');
    assert.equal(k.bills.filter(b=>b.open>0)[0].days,E.rows['EV-KAVERI'].days);
    assert.deepEqual([a.totals.open,a.totals.b0_30,a.totals.b31_60,a.totals.b61_90,a.totals.b90],[E.open,E.b0_30,0,0,0]);
  });
  test(T+'Payable ageing buckets move with the as-of date: 56/49 days → 31–60, 86/79 → 61–90, 101/94 → 90+ (same open bills)',()=>{
    const s=month();
    for(const w of X.ageing.payableLater){
      const a=call('accAgeing',s,w.asOf,'payable'),m=rowOf(a,r=>r.party==='EV-MORNING'),k=rowOf(a,r=>r.party==='EV-KAVERI');
      assert.equal(m.bills.find(b=>b.open>0).days,w.morning.days,w.asOf);assert.equal(k.bills.find(b=>b.open>0).days,w.kaveri.days,w.asOf);
      assert.equal(m[w.morning.bucket],R(24000),w.asOf+' Morning');assert.equal(k[w.kaveri.bucket],R(42000),w.asOf+' Kaveri');
      assert.equal(a.totals[w.morning.bucket],R(66000),w.asOf+' total');assert.equal(a.totals.open,R(66000));
    }
  });
  test(T+'Ageing rows tie to the party ledger balance (Morning 24,000 Cr, Kaveri 42,000 Cr) and carry no flag',()=>{
    const s=month(),a=call('accAgeing',s,'2026-04-30','payable');
    assert.equal(a.rows.reduce((x,r)=>x+r.open,0),R(66000));assert.ok(a.rows.every(r=>!r.flagged&&!r.untied),'a row that does not tie to its ledger is flagged');
  });

  // ───────── GST summary (acc3-ageing) ─────────
  test(T+'GST summary April 2026 (§8.6): output ₹29,000, ITC nil, RCM nil, TCS ₹100, challan ₹28,900, B2B Deccan Agro ₹60,000, no advances, ties to GSTR-1 and the GST month',()=>{
    const s=month(),g=call('accGstSummary',s,X.gst.month),E=X.gst;
    assert.equal(g.month,E.month);
    assert.equal(num(g.outputTaxable.rate5),E.rate5Taxable,'5 % taxable ₹5,80,000');assert.equal(num(g.outputTaxable.rate18)||0,0);
    assert.equal(num(g.outputTaxable.b2b),E.b2bTaxable);assert.equal(num(g.outputTaxable.b2cs),E.b2csTaxable);
    assert.equal(num(g.outputTaxable.advances11A)||0,0,'DEC-142: no GST on advances');assert.equal(num(g.outputTaxable.advances11B)||0,0);assert.equal(num(g.outputTaxable.creditNotes)||0,0);
    assert.deepEqual([g.itc.cgst,g.itc.sgst,g.itc.igst],[0,0,0],'§4.4: no ITC on any bill');assert.equal(g.rcm||0,0);
    assert.equal(g.tcsCredit,E.tcsCredit);assert.equal(g.cashLedgerBalance,E.cashLedgerBalance);
    assert.equal(g.setoff.challan,E.challan);assert.equal(g.setoff.payable,E.payable);
    assert.equal(g.ties.gstr1,true,'GSTR-1: '+(g.ties.differences||[]).join('; '));assert.equal(g.ties.gstr3b,true,'GSTR-3B: '+(g.ties.differences||[]).join('; '));assert.deepEqual(g.ties.differences,[]);
    const txt=JSON.stringify(g);for(const n of [E.output.cgst,E.output.sgst,E.challan,E.tcsCredit])assert.ok(txt.includes(String(n)),`the summary shows ${n}`);
  });

  // ───────── Tally (acc3-tally): counts, Σ Dr = Σ Cr, REMOTEID stability, byte equality ─────────
  const tallyVouchers=s=>call('accTallyVouchers',s,X.from,X.to).xml;
  const tallyMasters=s=>call('accTallyMasters',s,X.fy).xml;
  const count=(x,re)=>(x.match(re)||[]).length;
  test(T+'Tally vouchers April 2026: well-formed, one VOUCHER per HCC voucher, every voucher Σ Dr = Σ Cr, GUID = REMOTEID',()=>{
    const s=month(),xml=tallyVouchers(s);
    assert.equal(count(xml,/<ENVELOPE>/g),1);
    const vouchers=xml.split(/<VOUCHER /).slice(1);
    let hcc=0;for(let d=1;d<=30;d++)hcc+=M.accVouchers(s,`2026-04-${String(d).padStart(2,'0')}`).length;
    assert.ok(vouchers.length>0);assert.equal(vouchers.length,hcc,'one Tally voucher per HCC voucher');
    for(const v of vouchers){
      const amounts=[...v.matchAll(/<ISDEEMEDPOSITIVE>(Yes|No)<\/ISDEEMEDPOSITIVE>\s*<AMOUNT>(-?[\d.]+)<\/AMOUNT>/g)].map(m=>({dr:m[1]==='Yes',a:Math.round(parseFloat(m[2])*100)}));
      const dr=amounts.filter(x=>x.dr).reduce((t,x)=>t+Math.abs(x.a),0),cr=amounts.filter(x=>!x.dr).reduce((t,x)=>t+Math.abs(x.a),0);
      assert.equal(dr,cr,'Σ Dr = Σ Cr in '+(v.match(/REMOTEID="([^"]*)"/)?.[1]??'?'));
      assert.equal(v.match(/REMOTEID="([^"]*)"/)?.[1],v.match(/<GUID>([^<]*)<\/GUID>/)?.[1],'GUID = REMOTEID');
    }
  });
  test(T+'Tally: REMOTEID is stable (two runs identical), unique, the file is byte-identical on a re-export; masters carry the FY ledgers and the parties',()=>{
    const s=month(),a=tallyVouchers(s),b=tallyVouchers(s);assert.equal(a,b,'byte equality on re-export');
    const ids=[...a.matchAll(/REMOTEID="([^"]*)"/g)].map(m=>m[1]);assert.equal(new Set(ids).size,ids.length,'REMOTEIDs unique');
    const m1=tallyMasters(s),m2=tallyMasters(s);assert.equal(m1,m2);
    assert.ok(count(m1,/<LEDGER /g)>=20,'ledgers of the effective chart + party sub-ledgers');
    for(const n of ['Deccan Agro Ltd','Morning Supplies','Kaveri Textiles'])assert.ok(m1.includes(n),n);
    assert.ok(!/GROUP NAME="Current Assets"/.test(m1),"Tally's primary groups are not created");
  });
  test(T+'Tally: a range over 31 days is refused, and a second scenario load exports the same bytes',()=>{
    const s=month();const over=call('accTallyVouchers',s,'2026-04-01','2026-05-15');assert.ok(/31 days/.test(over.error)&&over.count===0);
    const first=tallyVouchers(s);cache=undefined;assert.equal(tallyVouchers(month()),first,'same scenario → same bytes');
  });
}
