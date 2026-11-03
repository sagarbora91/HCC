// ACC-4 · F5-A1 · bank reconciliation and clearing (02 Planning/agents/ACC4-CONTRACT.md §2, §6). Names prefixed [ACC4-BANK].
// Fixture tests/fixtures/bank-april-2026.csv (fictional, generic layout) against the rulebook April 2026 month (scenario ACC-RULEBOOK).
import {readFileSync} from 'node:fs';
export function register({test,M,assert}){
  const T='[ACC4-BANK] ',R=r=>Math.round(r*100),HDFC='1030/BA-HDFC';
  const CSV=readFileSync(new URL('./fixtures/bank-april-2026.csv',import.meta.url),'utf8');
  const run=(s,uid,cmd)=>M.execute(s,uid,cmd);
  let base;const month=()=>base??=M.loadScenario('ACC-RULEBOOK');
  const imported=()=>run(month(),'accountant',{type:'BANK_IMPORT',bankLedger:HDFC,layoutId:'generic',fileName:'bank-april-2026.csv',text:CSV});
  const rowsOf=(s,re)=>s.accBankX.rows.filter(r=>re.test(r.narration));

  test(T+'golden: import → auto-match → one manual (3 UPI rows ↔ one contra) → two created vouchers → difference 0; 1010 / 1020 zero',()=>{
    let s=imported();assert.equal(s.accBankX.rows.length,15);assert.equal(s.accBankX.imports[0].closingBalance,R(546561));assert.equal(s.accBankX.imports[0].openingBalance,R(300000));
    s=run(s,'accountant',{type:'BANK_AUTO_MATCH',bankLedger:HDFC});
    const rules=s.accBankX.matches.map(m=>m.rule);assert.equal(rules.length,10,'10 auto matches');assert.ok(rules.includes('auto-ref')&&rules.includes('auto-amount-date'));
    const auto=M.accBankAutoMatch(s,HDFC);assert.equal(auto.settlements.length,3,'UPI settlement rows proposed as B8');
    const upi=rowsOf(s,/^UPI SETTL/),contra=M.accBankBookLines(s,HDFC,'2026-04-30').find(l=>/UPI-SETTLE-APR/.test(l.narration));
    s=run(s,'accountant',{type:'BANK_MATCH',rowIds:upi.map(r=>r.id),refs:[contra.ref]});
    s=run(s,'accountant',{type:'BANK_CREATE_VOUCHER',rowId:rowsOf(s,/^INTEREST/)[0].id,ledger:'4960'});
    s=run(s,'accountant',{type:'BANK_CREATE_VOUCHER',rowId:rowsOf(s,/^SMS/)[0].id,ledger:'5080'});
    const r=M.accBankRecon(s,HDFC,'2026-04-30');
    assert.equal(r.booksBalance,R(501561));assert.equal(r.issuedTotal,R(45000),'electricity cheque not presented');assert.equal(r.depositsTotal,0);
    assert.equal(r.bankOnly.length,0);assert.equal(r.statementBalance,R(546561));assert.equal(r.difference,0);
    assert.equal(M.accBankClearing(s,'1010','2026-04-30').balance,0);assert.equal(M.accBankClearing(s,'1020','2026-04-30').balance,0);
    const csv=M.accBankReconCsv(r);assert.ok(csv.includes('Difference')&&csv.includes('45000.00'));
    assert.equal(M.accBankClearedDate(s,contra.ref),'2026-04-27');
    const tb=M.accTrialBalance6(s,'2026-04-01','2026-05-01',false);assert.ok(tb.balanced,'every posting balances');
  });
  test(T+'duplicate file refused BANK_DUP_FILE; balance break refused with the row; overlapping rows skipped unless kept',()=>{
    const s=imported();
    assert.throws(()=>run(s,'accountant',{type:'BANK_IMPORT',bankLedger:HDFC,layoutId:'generic',text:CSV}),/BANK_DUP_FILE/);
    const broken=CSV.replace('245000.00','245001.00');
    assert.throws(()=>run(month(),'accountant',{type:'BANK_IMPORT',bankLedger:HDFC,layoutId:'generic',text:broken}),/BANK_BALANCE_BREAK.*row 3/);
    const head=CSV.split('\n')[0],two=CSV.split('\n').slice(1,3).join('\n');
    const again=run(s,'accountant',{type:'BANK_IMPORT',bankLedger:HDFC,layoutId:'generic',text:head+'\n'+two+'\n'});
    assert.equal(again.accBankX.imports.at(-1).rows,0);
    const kept=run(s,'accountant',{type:'BANK_IMPORT',bankLedger:HDFC,layoutId:'generic',text:head+'\n'+two+'\n',keepDuplicates:true});
    assert.equal(kept.accBankX.imports.at(-1).rows,2);
    assert.throws(()=>run(month(),'reception',{type:'BANK_IMPORT',bankLedger:HDFC,layoutId:'generic',text:CSV}),/Access denied/);
  });
  test(T+'ambiguous amount left unmatched; manual sums must agree (BANK_MATCH_SUM); undo a match; 9999 refused for an unknown credit',()=>{
    const text='date,narration,ref,debit,credit,balance\n11/04/2026,UPI CR A,,,31500.00,331500.00\n';
    let s=run(month(),'accountant',{type:'BANK_IMPORT',bankLedger:'1030/BA-TEST',layoutId:'generic',text});
    // two book lines of the same amount and date on a test ledger → two candidates → left for manual
    s=structuredClone(s);s.accountingX.manual=[...s.accountingX.manual,...[1,2].map(i=>({id:'MV-T'+i,date:'2026-04-10',type:'Receipt',narration:'test '+i,lines:[{account:'1030/BA-TEST',debit:R(31500),credit:0},{account:'4960',debit:0,credit:R(31500)}],status:'approved',by:'owner',at:s.clock}))];
    const a=M.accBankAutoMatch(s,'1030/BA-TEST');assert.equal(a.proposals.length,0);assert.equal(a.ambiguous.length,1);
    const row=s.accBankX.rows.find(r=>r.bankLedger==='1030/BA-TEST'),book=M.accBankBookLines(s,'1030/BA-TEST','2026-04-30');
    assert.throws(()=>run(s,'accountant',{type:'BANK_MATCH',rowIds:[row.id],refs:book.map(b=>b.ref)}),/BANK_MATCH_SUM/);
    s=run(s,'accountant',{type:'BANK_MATCH',rowIds:[row.id],refs:[book[0].ref]});
    const m=s.accBankX.matches.at(-1);s=run(s,'accountant',{type:'BANK_UNMATCH',matchId:m.id});
    assert.equal(s.accBankX.rows.find(r=>r.id===row.id).status,'unmatched');
    assert.throws(()=>run(s,'accountant',{type:'BANK_CREATE_VOUCHER',rowId:row.id,ledger:'9999'}),/Suspense/);
  });
  test(T+'B8 settlement (T14b): Dr bank 1,48,230 · Dr 5080 1,770 · Cr 1020 1,50,000; sum refused; UPI no charge (T14a)',()=>{
    const l=M.accBankSettlementLines({bankLedger:HDFC,net:R(148230),clearing:'1020',gross:R(150000),mdr:R(1500),mdrGst:R(270),itc:false}).lines;
    assert.deepEqual(l,[{account:HDFC,debit:R(148230),credit:0},{account:'5080',debit:R(1770),credit:0},{account:'1020',debit:0,credit:R(150000)}]);
    const itc=M.accBankSettlementLines({bankLedger:HDFC,net:R(148230),clearing:'1020',gross:R(150000),mdr:R(1500),mdrGst:R(270),itc:true}).lines;
    assert.equal(itc.reduce((a,x)=>a+x.debit-x.credit,0),0);assert.ok(itc.some(x=>x.account==='1310'));
    assert.match(M.accBankSettlementLines({bankLedger:HDFC,net:R(148000),clearing:'1020',gross:R(150000),mdr:R(1500),mdrGst:R(270),itc:false}).error,/BANK_SETTLEMENT_SUM/);
    let s=imported();const row=rowsOf(s,/^UPI SETTL 1/)[0];
    s=run(s,'accountant',{type:'BANK_SETTLEMENT_POST',rowId:row.id,clearing:'1010'});
    const v=s.accountingX.manual.at(-1);assert.deepEqual(v.lines,[{account:HDFC,debit:R(31500),credit:0},{account:'1010',debit:0,credit:R(31500)}]);
    assert.equal(s.accBankX.rows.find(r=>r.id===row.id).status,'matched');
  });
  test(T+'layout master: single amount + Dr/Cr column file parses; Owner-only; Generic seed present',()=>{
    assert.equal(M.accBankLayouts(month())[0].name,'Generic');
    const lay={id:'amt-flag',name:'Amount + Dr/Cr',bankLedger:'1030/BA-HDFC',delimiter:';',headerRows:1,dateFormat:'DD-MMM-YYYY',columns:{date:'Txn Date',narration:'Details',amount:'Amount',drCrFlag:'Type'},active:true};
    assert.throws(()=>run(month(),'accountant',{type:'BANK_LAYOUT_UPSERT',layout:lay}),/Owner/);
    const s=run(month(),'owner',{type:'BANK_LAYOUT_UPSERT',layout:lay});
    const p=M.accBankParse('Txn Date;Details;Amount;Type\n05-Apr-2026;EMI;"55,000.00";Dr\n10-Apr-2026;=HYPERLINK(x);40000;Cr\n',M.accBankLayout(s,'amt-flag'));
    assert.equal(p.error,undefined);assert.deepEqual(p.rows.map(r=>[r.date,r.debit,r.credit]),[['2026-04-05',R(55000),0],['2026-04-10',0,R(40000)]]);
    assert.ok(p.rows[1].narration.startsWith("'="),'CSV formula guard');
  });
}
