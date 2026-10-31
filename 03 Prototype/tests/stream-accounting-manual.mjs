// ACC-2 · Manual vouchers and loan party ledgers (ACC2-POSTING-CONTRACT §2, §7, §9; rulebook §1 principle 2, B5–B7, B11, A18, §6).
// Every guard of contract §2 with its message, the roles (Owner approved at once, Accountant pending, Owner decides / voids), void on an
// unlocked day (posts nothing) and on a locked day (reversing voucher dated the business date), the templates, Add loan. Names [ACC2-MANUAL].
import {tillClear} from './_till.mjs';
export function register({test,M,assert}){
  const D='2026-10-24',R=r=>r*100,BANK='1030/BANK-1',TILL='1000/TILL';
  // The command's `type` is its name: the voucher type travels as `voucherType` (contract §2 `type`).
  const jv=(lines,extra={})=>({type:'ACC_MANUAL_SAVE',date:D,voucherType:'Journal',narration:'Test journal',lines,...extra});
  const tpl=(t,x,extra={})=>{const g=M.accManualTemplateLines(t,x);return {type:'ACC_MANUAL_SAVE',date:D,voucherType:g.type,narration:g.narration,lines:g.lines,template:t,...extra};};
  const charges=(extra={})=>({type:'ACC_MANUAL_SAVE',date:D,voucherType:'Payment',narration:'Bank charges October',reference:'STMT-OCT',template:'bank-charges',
    lines:[{account:'5080',debit:R(590),credit:0},{account:BANK,debit:0,credit:R(590)}],...extra});
  const run=(s,uid,cmd)=>M.execute(s,uid,cmd);
  const refused=(fn,code,re)=>assert.throws(fn,e=>e.code===code&&re.test(e.message),String(re));
  const last=s=>M.accManualList(s).at(-1);
  const manualOn=(s,d)=>M.accLiveVouchers(s,d).filter(v=>v.source.kind==='manual');
  function closeDay(s=M.seed()){
    for(const b of s.bookings.filter(b=>b.status==='Confirmed')){s=run(s,'owner',{type:'REGISTER',id:b.id});s=run(s,'owner',{type:'CHECKIN',id:b.id});}
    s=run(s,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});
    s=run(s,'owner',{type:'NIGHT_POST'});s=run(s,'owner',{type:'SHIFT_CLOSE',counted:M.drawer(s)});
    s=tillClear(M,s);return run(s,'owner',{type:'DAY_CLOSE'});
  }

  test('[ACC2-MANUAL] Owner saves → approved at once, posts on its date as source {manual, id, template}; numbered with the day',()=>{
    const s=run(M.seed(),'owner',charges()),v=last(s);
    assert.deepEqual([v.status,v.by,v.decidedBy,v.bookedOn,v.template,v.type],['approved','owner','owner',D,'bank-charges','Payment']);
    const vs=manualOn(s,D);assert.equal(vs.length,1);
    assert.deepEqual(vs[0].source,{kind:'manual',id:v.id,sub:'bank-charges'});
    assert.deepEqual(vs[0].entries,[{account:'5080',debit:R(590),credit:0},{account:BANK,debit:0,credit:R(590)}]);
    assert.match(vs[0].no,/^PMT\/20261024\/\d{3}$/);assert.match(vs[0].narration,/Bank charges October · STMT-OCT · manual MV-/);
    assert.ok(M.accDates(s).includes(D));assert.match(s.audit[0].detail,/approved \(Owner\)/);
    // The day still balances; the bank party ledger carries the charge.
    const t=M.accTotals(M.accLiveVouchers(s,D));assert.equal(t.debit,t.credit);
    assert.equal(M.accPartyBalance(s,BANK)-M.accPartyBalance(M.seed(),BANK),-R(590));
  });
  test('[ACC2-MANUAL] Accountant saves → pending (posts nothing); Owner approves (re-runs the guards) or rejects with a reason',()=>{
    let s=run(M.seed(),'accountant',charges());const v=last(s);
    assert.deepEqual([v.status,v.by,v.decidedBy],['pending','accountant',undefined]);
    assert.equal(manualOn(s,D).length,0);assert.equal(M.accManualPending(s).length,1);
    for(const u of ['accountant','manager','cashier'])refused(()=>run(s,u,{type:'ACC_MANUAL_DECIDE',id:v.id,approve:true}),'FORBIDDEN',/Access denied/);
    refused(()=>run(s,'owner',{type:'ACC_MANUAL_DECIDE',id:v.id,approve:false,reason:'x'}),'VALIDATION',/Enter a reason/);
    const rej=run(s,'owner',{type:'ACC_MANUAL_DECIDE',id:v.id,approve:false,reason:'Wrong bank'});
    assert.deepEqual([last(rej).status,last(rej).reason,last(rej).decidedBy],['rejected','Wrong bank','owner']);
    // The maker corrects a rejected voucher (same id) → pending again; then the Owner approves.
    let c=run(rej,'accountant',{...charges(),id:v.id,narration:'Bank charges October (corrected)'});
    assert.deepEqual([M.accManualList(c).length,last(c).status,last(c).reason],[1,'pending',undefined]);
    c=run(c,'owner',{type:'ACC_MANUAL_DECIDE',id:v.id,approve:true});
    assert.deepEqual([last(c).status,last(c).decidedBy,last(c).bookedOn],['approved','owner',D]);
    assert.equal(manualOn(c,D).length,1);
    refused(()=>run(c,'owner',{type:'ACC_MANUAL_DECIDE',id:v.id,approve:true}),'INVALID_TRANSITION',/no longer pending/);
    refused(()=>run(c,'accountant',{...charges(),id:v.id}),'INVALID_TRANSITION',/void an approved one/);
    // Approval re-runs the guards: a voucher entered for a day locked meanwhile is refused.
    s=run(closeDay(),'accountant',charges());const p=last(s);
    s=run(s,'accountant',{type:'ACC_LOCK',date:D});
    refused(()=>run(s,'owner',{type:'ACC_MANUAL_DECIDE',id:p.id,approve:true}),'INVALID_TRANSITION',/2026-10-24 is locked — unlock the day first/);
  });
  test('[ACC2-MANUAL] roles: enter = Owner / Accountant (PERM-ACC-010); Manager, Cashier, Reception, Tech admin refused; bad debt Owner only (A18)',()=>{
    const s=M.seed();
    for(const u of ['manager','cashier','reception','techadmin','housekeeper'])refused(()=>run(s,u,charges()),'FORBIDDEN',/Access denied/);
    const bad={template:'bad-debt',voucherType:'Journal',narration:'Write-off of an old company balance',lines:[{account:'5300',debit:R(1000),credit:0},{account:'1110/CO-1',debit:0,credit:R(1000)}]};
    refused(()=>run(s,'accountant',{...charges(),...bad}),'FORBIDDEN',/Only the Owner writes off a bad debt \(rulebook A18\)/);
    refused(()=>run(s,'accountant',{...charges(),...bad,template:'journal'}),'FORBIDDEN',/rulebook A18/); // a 5300 debit is a write-off whatever the template
    const ok=run(s,'owner',{...charges(),...bad});
    assert.deepEqual(manualOn(ok,D)[0].entries,[{account:'5300',debit:R(1000),credit:0},{account:'1110/CO-1',debit:0,credit:R(1000)}]);
    assert.match(manualOn(ok,D)[0].no,/^JRN\//);
  });
  test('[ACC2-MANUAL] guards: type, template, narration, reference, date (shape, future, locked), line count',()=>{
    const s=M.seed(),M_=M.ACC_MANUAL_MSG,esc=t=>new RegExp(t.replace(/[.*+?^${}()|[\]\\]/g,'\\$&'));
    refused(()=>run(s,'owner',charges({voucherType:'Sales'})),'VALIDATION',esc(M_.type));
    refused(()=>run(s,'owner',charges({template:'gift'})),'VALIDATION',esc(M_.template));
    refused(()=>run(s,'owner',charges({narration:'  x '})),'VALIDATION',esc(M_.narration));
    refused(()=>run(s,'owner',charges({reference:'R'.repeat(61)})),'VALIDATION',esc(M_.reference));
    refused(()=>run(s,'owner',charges({date:'2026-02-30'})),'VALIDATION',esc(M_.date));
    refused(()=>run(s,'owner',charges({date:'2026-10-25'})),'VALIDATION',esc(M_.future));
    refused(()=>run(s,'owner',charges({lines:[{account:'5080',debit:100,credit:0}]})),'VALIDATION',esc(M_.lines));
    refused(()=>run(s,'owner',charges({lines:'x'})),'VALIDATION',esc(M_.lines));
    // Every problem is listed in order; the first is the refusal.
    assert.deepEqual(M.accManualProblems(s,{type:'X',narration:'',date:'bad',lines:[]}),[M_.type,M_.narration,M_.date,M_.lines]);
    // Locked day: "unlock the day first".
    let l=closeDay();l=run(l,'accountant',{type:'ACC_LOCK',date:D});l=run(l,'owner',{type:'NEXT_DAY'});
    refused(()=>run(l,'owner',charges()),'INVALID_TRANSITION',/^2026-10-24 is locked — unlock the day first \(the Owner reopens a locked day with a reason, DEC-077\)\.$/);
    assert.equal(last(run(l,'owner',charges({date:'2026-10-25'}))).date,'2026-10-25');
  });
  test('[ACC2-MANUAL] guards: each line (shape, one side, too large, 9999 / 1400 never, control without party, unknown / retired ledger, unknown party), balance',()=>{
    const s=M.seed(),M_=M.ACC_MANUAL_MSG,L=(a,b)=>[a,b],ok={account:BANK,debit:0,credit:R(590)};
    const p=lines=>M.accManualProblems(s,{type:'Journal',narration:'Guard test',date:D,lines});
    assert.deepEqual(p(L({account:'50x',debit:1,credit:0},ok)),[M_.lineAccount(1)]);
    assert.deepEqual(p(L({account:'5080',debit:5,credit:5},{account:BANK,debit:0,credit:0})),[M_.lineAmount(1),M_.lineAmount(2)]);
    assert.deepEqual(p(L({account:'5080',debit:1.5,credit:0},ok)),[M_.lineAmount(1)]);
    assert.deepEqual(p(L({account:'5080',debit:-5,credit:0},ok)),[M_.lineAmount(1)]);
    assert.deepEqual(p(L({account:'5080',debit:20_000_000_000,credit:0},{account:BANK,debit:0,credit:20_000_000_000})),[M_.lineLarge(1),M_.lineLarge(2)]);
    assert.deepEqual(p(L({account:'9999',debit:R(1),credit:0},ok)),[M_.lineNever(1,'9999')]);
    assert.deepEqual(p(L({account:'1400',debit:R(1),credit:0},ok)),[M_.lineNever(1,'1400')]);
    for(const c of ['1000','1030','1110','1115','1120','1200','1210','2200','2400'])assert.deepEqual(p(L({account:c,debit:R(1),credit:0},ok)),[M_.lineControl(1,c)],c);
    assert.deepEqual(p(L({account:'5081',debit:R(1),credit:0},ok)),[M_.lineUnknown(1,'5081')]);
    assert.deepEqual(p(L({account:'1110/CO-404',debit:R(1),credit:0},ok)),[M_.lineUnknown(1,'1110/CO-404')]);
    assert.deepEqual(p(L({account:'5080/X',debit:R(1),credit:0},ok)),[M_.lineUnknown(1,'5080/X')]);
    assert.deepEqual(p(L({account:'2400/LN-NONE',debit:R(1),credit:0},ok)),[M_.lineUnknown(1,'2400/LN-NONE')]);
    assert.deepEqual(p(L({account:'5080',debit:R(590),credit:0},{account:BANK,debit:0,credit:R(500)})),[M_.balance]);
    assert.deepEqual(M_.balance,'The voucher does not balance: total debits must equal total credits.');
    // A retired Owner ledger is refused; an active one is accepted.
    let x=run(s,'owner',{type:'ACC_LEDGER_UPSERT',create:true,code:'5130',name:'Software subscriptions',group:'indirect-expenses'});
    assert.deepEqual(M.accManualProblems(x,{type:'Journal',narration:'Guard test',date:D,lines:L({account:'5130',debit:R(590),credit:0},ok)}),[]);
    x=run(x,'owner',{type:'ACC_LEDGER_RETIRE',code:'5130'});
    assert.deepEqual(M.accManualProblems(x,{type:'Journal',narration:'Guard test',date:D,lines:L({account:'5130',debit:R(590),credit:0},ok)}),[M_.lineUnknown(1,'5130')]);
    refused(()=>run(s,'owner',jv(L({account:'9999',debit:R(1),credit:0},ok))),'VALIDATION',/9999 is never posted by hand/);
  });
  test('[ACC2-MANUAL] guards: Contra only between cash / bank; Payment / Receipt need exactly one cash-or-bank side',()=>{
    const s=M.seed(),M_=M.ACC_MANUAL_MSG;
    const p=(type,lines)=>M.accManualProblems(s,{type,narration:'Guard test',date:D,lines});
    // Contra: cash ↔ bank, UPI / card clearing (bank group) ↔ bank — accepted; anything else refused.
    assert.deepEqual(p('Contra',[{account:BANK,debit:R(5000),credit:0},{account:TILL,debit:0,credit:R(5000)}]),[]);
    assert.deepEqual(p('Contra',[{account:BANK,debit:R(5000),credit:0},{account:'1020',debit:0,credit:R(5000)}]),[]);
    assert.deepEqual(p('Contra',[{account:'1005',debit:R(500),credit:0},{account:TILL,debit:0,credit:R(500)}]),[]);
    assert.deepEqual(p('Contra',[{account:BANK,debit:R(4900),credit:0},{account:'5080',debit:R(100),credit:0},{account:'1020',debit:0,credit:R(5000)}]),[M_.contra]);
    // Payment: cash / bank on the credit side only.
    assert.deepEqual(p('Payment',[{account:'5080',debit:R(1),credit:0},{account:BANK,debit:0,credit:R(1)}]),[]);
    assert.deepEqual(p('Payment',[{account:'5080',debit:R(1),credit:0},{account:'3100',debit:0,credit:R(1)}]),[M_.payment]);
    assert.deepEqual(p('Payment',[{account:BANK,debit:R(1),credit:0},{account:TILL,debit:0,credit:R(1)}]),[M_.payment]);
    // Receipt: cash / bank on the debit side only.
    assert.deepEqual(p('Receipt',[{account:BANK,debit:R(1),credit:0},{account:'4960',debit:0,credit:R(1)}]),[]);
    assert.deepEqual(p('Receipt',[{account:'5080',debit:R(1),credit:0},{account:'4960',debit:0,credit:R(1)}]),[M_.receipt]);
    assert.deepEqual(p('Receipt',[{account:BANK,debit:R(1),credit:0},{account:TILL,debit:0,credit:R(1)}]),[M_.receipt]);
    // A Journal may touch anything (no cash / bank rule).
    assert.deepEqual(p('Journal',[{account:BANK,debit:R(1),credit:0},{account:TILL,debit:0,credit:R(1)}]),[]);
    const c=run(s,'owner',{type:'ACC_MANUAL_SAVE',date:D,voucherType:'Contra',template:'contra',narration:'Petty cash float topped up',lines:[{account:'1005',debit:R(500),credit:0},{account:TILL,debit:0,credit:R(500)}]});
    assert.match(manualOn(c,D)[0].no,/^CTR\/20261024\/001$/);assert.equal(manualOn(c,D)[0].type,'Contra');
  });
  test('[ACC2-MANUAL] void on an unlocked day: void, posts nothing; Owner only, reason needed, approved vouchers only',()=>{
    let s=run(M.seed(),'owner',charges());const v=last(s);
    for(const u of ['accountant','manager'])refused(()=>run(s,u,{type:'ACC_MANUAL_VOID',id:v.id,reason:'Duplicate'}),'FORBIDDEN',/Access denied/);
    refused(()=>run(s,'owner',{type:'ACC_MANUAL_VOID',id:v.id,reason:''}),'VALIDATION',/Enter a reason/);
    refused(()=>run(s,'owner',{type:'ACC_MANUAL_VOID',id:'MV-404',reason:'Duplicate'}),'NOT_FOUND',/not found/);
    s=run(s,'owner',{type:'ACC_MANUAL_VOID',id:v.id,reason:'Duplicate entry'});
    assert.deepEqual([last(s).status,last(s).reason,last(s).voidedBy,M.accManualList(s).length],['void','Duplicate entry','owner',1]);
    assert.equal(manualOn(s,D).length,0);assert.match(s.audit[0].detail,/unlocked day: posts nothing/);
    refused(()=>run(s,'owner',{type:'ACC_MANUAL_VOID',id:v.id,reason:'Again'}),'INVALID_TRANSITION',/Only an approved/);
    const p=run(M.seed(),'accountant',charges());
    refused(()=>run(p,'owner',{type:'ACC_MANUAL_VOID',id:last(p).id,reason:'Pending'}),'INVALID_TRANSITION',/Only an approved/);
  });
  test('[ACC2-MANUAL] void on a locked day: a reversing voucher dated the business date (lines swapped, voidOf); the locked snapshot keeps the original, no drift',()=>{
    let s=closeDay();s=run(s,'owner',charges());const v=last(s);
    s=run(s,'accountant',{type:'ACC_LOCK',date:D});
    const snap=M.accLockOf(s,D).vouchers.filter(x=>x.source.kind==='manual');assert.equal(snap.length,1);assert.equal(snap[0].source.id,v.id);
    s=run(s,'owner',{type:'NEXT_DAY'});const B=s.date;assert.equal(B,'2026-10-25');
    s=run(s,'owner',{type:'ACC_MANUAL_VOID',id:v.id,reason:'Bank refunded the charge'});
    const orig=M.accManualById(s,v.id),rev=last(s);
    assert.deepEqual([orig.status,orig.reason,orig.voidedBy],['void','Bank refunded the charge','owner']);
    assert.deepEqual([rev.status,rev.date,rev.voidOf,rev.bookedOn,rev.type,rev.template,rev.by],['approved',B,v.id,B,'Payment','bank-charges','owner']);
    assert.deepEqual(rev.lines,[{account:'5080',debit:0,credit:R(590)},{account:BANK,debit:R(590),credit:0}]);
    // The reversal posts on the business date; the original still derives on its locked date (snapshot = live → no drift).
    const r=manualOn(s,B);assert.equal(r.length,1);assert.match(r[0].narration,/reversal of MV-/);
    assert.ok(!M.accExceptions(s,D).some(x=>x.kind==='drift'));assert.equal(manualOn(s,D).length,1);
    // Net effect on the bank party over both days: nothing.
    assert.equal(M.accPartyBalance(s,BANK,B),M.accPartyBalance(closeDay(),BANK,D));
    refused(()=>run(s,'owner',{type:'ACC_MANUAL_VOID',id:rev.id,reason:'Undo the undo'}),'INVALID_TRANSITION',/reversing voucher cannot be voided/);
    assert.match(s.audit[0].detail,/is locked: reversing voucher MV-\d+ dated 2026-10-25/);
  });
  test('[ACC2-MANUAL] Add loan (Owner): 2400/<CODE> party row in the chart (kind loan, lender, last 4); loan received and EMI templates post to it',()=>{
    const s0=M.seed();
    for(const u of ['accountant','manager','techadmin'])refused(()=>run(s0,u,{type:'ACC_LOAN_ADD',name:'HDFC term loan',lender:'HDFC Bank (fictional)'}),'FORBIDDEN',/only the Owner adds a loan/);
    refused(()=>run(s0,'owner',{type:'ACC_LOAN_ADD',name:'HDFC term loan',lender:'HDFC Bank (fictional)',accountLast4:'12'}),'VALIDATION',/ends in 4 digits/);
    // Coordinator ruling (28 Oct): ACC_LOAN_ADD {code, name, lender, accountLast4, kind:'secured'|'unsecured'} → party row 2400/<CODE>.
    let s=run(s0,'owner',{type:'ACC_LOAN_ADD',code:'ln-hdfc',name:'HDFC term loan',lender:'HDFC Bank (fictional)',accountLast4:'7788',kind:'secured'});
    const loan=M.accLoans(s)[0];
    assert.deepEqual([loan.code,loan.name,loan.group,loan.kind,loan.lender,loan.accountLast4,loan.active,loan.system],
      ['2400/LN-HDFC','HDFC term loan','secured-loans','loan','HDFC Bank (fictional)','7788',true,false]);
    const u=run(s,'owner',{type:'ACC_LOAN_ADD',code:'LN-FAMILY',name:'Loan from a relative',lender:'A relative (fictional)',kind:'unsecured'});
    assert.deepEqual(M.accLoans(u).map(l=>[l.code,l.group]),[['2400/LN-FAMILY','unsecured-loans'],['2400/LN-HDFC','secured-loans']]);
    refused(()=>run(s,'owner',{type:'ACC_LOAN_ADD',code:'LN-X',name:'Odd loan',lender:'HDFC',kind:'overdraft'}),'VALIDATION',/secured or unsecured/);
    assert.equal(M.accChartHistory(s)[0].field,'created');
    refused(()=>run(s,'owner',{type:'ACC_LOAN_ADD',code:'LN-HDFC',name:'Another loan',lender:'HDFC'}),'INVALID_TRANSITION',/already exists/);
    const L='2400/LN-HDFC';
    s=run(s,'owner',tpl('loan-received',{amount:R(500000),bank:BANK,loan:L},{narration:'Term loan disbursed'}));
    const emi=M.accManualTemplateLines('loan-emi',{principal:R(15000),interest:R(5000),bank:BANK,loan:L});
    assert.deepEqual(emi.lines,[{account:L,debit:R(15000),credit:0},{account:'5090',debit:R(5000),credit:0},{account:BANK,debit:0,credit:R(20000)}]);
    s=run(s,'owner',tpl('loan-emi',{principal:R(15000),interest:R(5000),bank:BANK,loan:L}));
    assert.equal(M.accPartyBalance(s,L),-R(485000));
    assert.deepEqual(manualOn(s,D).map(v=>v.type),['Receipt','Payment']);
  });
  test('[ACC2-MANUAL] templates build the contract §2 lines (capital, drawing, bank charges, interest, bad debt, statutory, GST, TDS, contra)',()=>{
    const T=(t,x)=>M.accManualTemplateLines(t,{amount:R(100),bank:BANK,...x});
    const e=(t,x)=>T(t,x).lines.map(l=>[l.account,l.debit?'Dr':'Cr']);
    assert.deepEqual(e('capital-introduced'),[[BANK,'Dr'],['3100','Cr']]);assert.equal(T('capital-introduced').type,'Receipt');
    assert.deepEqual(e('owner-drawing-bank'),[['3000','Dr'],[BANK,'Cr']]);assert.equal(T('owner-drawing-bank').type,'Payment');
    assert.deepEqual(e('bank-charges'),[['5080','Dr'],[BANK,'Cr']]);
    assert.deepEqual(e('interest-income'),[[BANK,'Dr'],['4960','Cr']]);
    assert.deepEqual(e('bad-debt',{party:'1120/UD-9876543210'}),[['5300','Dr'],['1120/UD-9876543210','Cr']]);assert.equal(T('bad-debt').type,'Journal');
    assert.deepEqual([e('statutory-remittance',{kind:'pf'})[0][0],e('statutory-remittance',{kind:'esic'})[0][0],e('statutory-remittance',{kind:'pt'})[0][0]],['2310','2320','2330']);
    assert.deepEqual(e('gst-payment'),[['1340','Dr'],[BANK,'Cr']]);assert.deepEqual(e('gst-payment',{rcm:true}),[['2140','Dr'],['1340','Cr']]);
    assert.deepEqual(e('tds-remittance'),[['2150','Dr'],[BANK,'Cr']]);
    assert.deepEqual(e('contra',{from:TILL,to:BANK}),[[BANK,'Dr'],[TILL,'Cr']]);assert.equal(T('contra').type,'Contra');
    // Each built voucher passes the guards (bank party of the seed, Owner).
    const s=M.seed();
    for(const t of ['capital-introduced','owner-drawing-bank','bank-charges','interest-income','tds-remittance'])
      assert.deepEqual(M.accManualProblems(s,{...T(t),date:D,narration:T(t).narration}),[],t);
  });
  test('[ACC2-MANUAL] cash guard (contract §7, ACC-2 integration: the accCashGuard of the flows lane): a ₹15,000 cash Payment to one person is warned and marked deduction lost; a ₹2,00,000 cash Receipt from one party is refused',()=>{
    const s=M.seed();
    const pay=M.accManualCashCheck(s,{type:'Payment',date:D,lines:[{account:'1200/HR-S01',debit:R(15000),credit:0},{account:TILL,debit:0,credit:R(15000)}]});
    assert.equal(pay.deductionLost,true);assert.match(pay.warning,/more than ₹10,000/);assert.equal(pay.refused,undefined);
    assert.deepEqual(M.accManualCashCheck(s,{type:'Payment',date:D,lines:[{account:'1200/HR-S01',debit:R(10000),credit:0},{account:TILL,debit:0,credit:R(10000)}]}),{});
    const co=s.groupsX.companies[0].id;
    const rec={type:'ACC_MANUAL_SAVE',voucherType:'Receipt',date:D,narration:'Cash from a company (fictional)',lines:[{account:TILL,debit:R(200000),credit:0},{account:'1110/'+co,debit:0,credit:R(200000)}]};
    assert.throws(()=>M.execute(s,'owner',rec),e=>e.code==='VALIDATION'&&/s.186/.test(e.message));
    const ok=M.execute(s,'owner',{type:'ACC_MANUAL_SAVE',voucherType:'Payment',date:D,narration:'Advance in cash (fictional)',lines:[{account:'1200/HR-S01',debit:R(15000),credit:0},{account:TILL,debit:0,credit:R(15000)}]});
    assert.equal(ok.accountingX.manual.at(-1).cashDeductionLost,true,'the voucher carries the B10 flag');
  });
}
