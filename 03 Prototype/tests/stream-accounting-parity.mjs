// ACC-2 · lane acc2-parity: the prototype books payroll like the server (contract §8, rulebook D1–D4, §8.2 T13a–c) and F&B folio lines
// go to 4200 (line:food, rulebook A3). Names prefixed [ACC2-PARITY]. Server twins: SourceRules.AdvanceGiven / PayRun / PayRunPaid /
// PayRunReversal / Fnf / FnfPaid (same source keys, same mapping keys, same sides) and Hcc.Accounting.Tests/Domain/PayrollPartyTests.cs.
import {STEP} from './_rulebook-month.mjs';
export function register({test,M,assert}){
  const {seed,execute}=M;
  const T='[ACC2-PARITY] ';
  const D=seed().date;
  const ent=v=>v.entries.map(e=>[e.account,e.debit,e.credit]);
  const set=xs=>xs.map(x=>x.join('|')).sort();
  const payroll=(s,d=D)=>M.accLiveVouchers(s,d).filter(v=>v.source.kind==='payroll');
  const one=(vs,id)=>{const v=vs.filter(x=>x.source.id===id);assert.equal(v.length,1,id+' '+vs.map(x=>x.source.id).join(','));return v[0];};
  const noParty=(s,d=D)=>M.accExceptions(s,d).filter(x=>x.kind==='no-party');
  /** The rulebook's party symbols → the BASE fixture's parties (Staff-07 = HR-S07; the bank HDFC = the seeded bank account BANK-1). */
  const sym=a=>a.replace('Staff-07','HR-S07').replace('HDFC','BANK-1');

  // ── T13a–c: the rulebook month's payroll, figure for figure ──
  /** BASE + an advance of ₹5,000 to HR-S07 and a pay run locked on D whose lines total the rulebook's T13b (gross 2,40,000; PF wages 1,50,000 at
   *  12 % + 12 %; ESIC wages 2,00,000 at 0.75 % + 3.25 %; PT 2,000; advance 5,000 recovered from HR-S07; net 2,13,500), its bank file on D. */
  const t13=()=>{
    const s=structuredClone(seed()),x=s.staffX;
    x.advances.push({id:'HR-ADV-T13',staffId:'HR-S07',amount:500000,installment:500000,recovered:0,date:D,reason:'Salary advance (fictional)',by:'owner',recoveries:[]});
    const line=(staffId,f)=>({staffId,name:staffId,dept:'FO',designation:'—',payType:'monthly',structure:'S',gender:'M',base:0,counts:{},latePenaltyHalf:0,payableDays:30,
      earned:f.gross,basic:0,hra:0,washing:0,special:0,ot:0,pfWages:0,esicWages:0,ctc:f.gross+f.pfEr+f.esicEr,...f,deductions:f.pf+f.esic+f.pt+f.advance,net:f.gross-f.pf-f.esic-f.pt-f.advance});
    const lines=[line('HR-S07',{gross:3000000,pf:180000,pfEr:180000,esic:22500,esicEr:97500,pt:20000,advance:500000}),
      line('HR-S01',{gross:21000000,pf:1620000,pfEr:1620000,esic:127500,esicEr:552500,pt:180000,advance:0})];
    x.runs.push({id:'HR-PAY-T13',month:'2026-09',status:'Locked',rates:{},lines,warnings:[],preparedBy:'accountant',preparedAt:D+'T11:00',lockedBy:'owner',lockedAt:D+'T17:40',lockedOn:D,
      bankFiles:[{at:D+'T18:00',by:'owner',net:lines.reduce((a,l)=>a+l.net,0),on:D}]});
    return s;
  };
  test(T+'T13a–c: advance, pay run and net pay equal the rulebook entries (per-staff recovery Cr 1200/HR-S07; the advance is a bank transfer in HCC)',()=>{
    const s=t13(),vs=payroll(s);
    const b=M.hrRunBook(s.staffX.runs.at(-1));
    assert.deepEqual([b.gross,b.pfEr,b.esicEr,b.pf+b.pfEr,b.esic+b.esicEr,b.pt,b.advance,b.other,b.net],[24000000,1800000,650000,3600000,800000,200000,500000,0,21350000]);
    assert.deepEqual(b.recoveries,[['HR-S07',500000]]);
    const adv=one(vs,'advance:HR-ADV-T13'),run=one(vs,'payroll:HR-PAY-T13'),paid=one(vs,'payroll-paid:HR-PAY-T13');
    assert.deepEqual([adv.type,run.type,paid.type],['Payment','Journal','Payment']);
    assert.deepEqual([adv.source.sub,run.source.sub,paid.source.sub],['advance','payrun','payment']);
    // T13b and T13c exactly as the rulebook (the employer PF and ESIC on two 5110 lines, as the server's two keys)
    assert.deepEqual(set(ent(run)),set(STEP.T13b.map(x=>[sym(x.a),x.dr,x.cr])));
    assert.deepEqual(set(ent(paid)),set(STEP.T13c.map(x=>[sym(x.a),x.dr,x.cr])));
    // T13a: the staff side as the rulebook; the funding is the bank (server PayrollService.AdvanceFunding), not the drawer
    assert.deepEqual(ent(adv),[['1200/HR-S07',500000,0],['1030/BANK-1',0,500000]]);
    assert.ok(vs.every(M.accBalanced));
    // the server's entry order: Dr salaries, employer PF, employer ESIC; Cr net, PF, ESIC, PT, advances per staff, other deductions
    assert.deepEqual(ent(run).map(e=>e[0]),['5100','5110','5110','2300','2310','2320','2330','1200/HR-S07']);
    // the advance is cleared on the staff party by the recovery: 1200/HR-S07 nets to nil on D
    assert.equal(vs.flatMap(v=>v.entries).filter(e=>e.account==='1200/HR-S07').reduce((a,e)=>a+e.debit-e.credit,0),0);
    assert.deepEqual(noParty(s).filter(x=>x.source?.startsWith('payroll')||x.source?.startsWith('advance')),[],'no payroll entry on a control ledger itself');
  });

  // ── the commands drive the vouchers ──
  test(T+'HR_ADVANCE_ADD books Dr 1200/<staff> · Cr the bank the transfer settles to, on the business date (key advance:<id>)',()=>{
    let s=execute(seed(),'owner',{type:'HR_ADVANCE_ADD',staffId:'HR-S02',amount:250000,installment:0,reason:'School fees (fictional)'});
    const a=s.staffX.advances.at(-1),v=one(payroll(s),'advance:'+a.id);
    assert.deepEqual(ent(v),[['1200/HR-S02',250000,0],['1030/BANK-1',0,250000]]);
    assert.match(v.narration,/^Salary advance given · Bank transfer · advance HR-ADV-\S+ · staff HR-S02$/);
    assert.ok(M.accDates(s).includes(D));assert.ok(M.accReconcile(s,D).every(c=>c.ok),JSON.stringify(M.accReconcile(s,D).filter(c=>!c.ok)));
  });
  test(T+'pay run lock → PayRun with the recovery per staff; bank file → net pay paid once per lock; unlock → exact reversal, payment kept; re-lock → /lock2',()=>{
    let s=execute(seed(),'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});const id=M.hrRunFor(s,'2026-09').id;
    s=execute(s,'owner',{type:'HR_PAYROLL_LOCK',id});
    const r=M.hrRunFor(s,'2026-09');assert.equal(r.lockedOn,D);
    const run=one(payroll(s),'payroll:'+id),t=M.hrRunTotals(r);
    assert.ok(M.accBalanced(run));assert.equal(M.accVoucherTotals(run).debit,t.gross+t.pfEr+t.esicEr);
    const rec=r.lines.filter(l=>l.advance>0);assert.ok(rec.length>=1,'the BASE advance of HR-S05 is recovered in September');
    assert.deepEqual(run.entries.filter(e=>e.account.startsWith('1200')).map(e=>[e.account,e.credit]),rec.map(l=>['1200/'+l.staffId,l.advance]));
    assert.equal(run.entries.find(e=>e.account==='2300').credit,t.net);
    // bank file: the first file of the lock is the payment; a re-issue is not a second payment
    s=execute(s,'accountant',{type:'HR_PAYROLL_BANK_FILE',id});
    s=execute(s,'accountant',{type:'HR_PAYROLL_BANK_FILE',id,confirmReissue:true});
    const paid=payroll(s).filter(v=>v.source.sub==='payment');assert.equal(paid.length,1);
    assert.deepEqual(ent(paid[0]),[['2300',t.net,0],['1030/BANK-1',0,t.net]]);assert.equal(paid[0].source.id,'payroll-paid:'+id);
    // unlock: the reversal swaps every entry of the lock's PayRun (same day here), the payment voucher stays
    s=execute(s,'owner',{type:'HR_PAYROLL_UNLOCK',id,reason:'Attendance correction (fictional)',confirmBankFile:true});
    let vs=payroll(s);const rev=one(vs,`payroll-unlock:${id}/lock1`);
    assert.deepEqual(ent(rev),ent(run).map(([a,d,c])=>[a,c,d]));assert.match(rev.narration,/BANK FILE ALREADY ISSUED/);
    one(vs,'payroll:'+id);one(vs,'payroll-paid:'+id);
    assert.equal(s.staffX.runs.find(x=>x.id===id).unlocks[0].lockedOn,D);
    // re-lock (the Accountant prepared it, the Owner locks): lock 2 posts under its own key; its net pay will not be booked twice by the old file
    s=execute(s,'owner',{type:'HR_PAYROLL_LOCK',id});vs=payroll(s);
    const two=one(vs,`payroll:${id}/lock2`);assert.deepEqual(ent(two),ent(run));assert.match(two.narration,/· lock 2$/);
    const sum=M.accTrialBalance(vs);for(const row of sum.filter(r=>['5100','5110','2310','2320','2330'].includes(r.account)))assert.equal(row.balance,ent(run).filter(e=>e[0]===row.account).reduce((a,e)=>a+e[1]-e[2],0),row.account+': one lock counts');
    assert.ok(vs.every(M.accBalanced));
  });
  test(T+'full and final on confirm: Fnf journal (advance recovered on the leaver’s party) + its net pay the same day; nothing before confirmation',()=>{
    const s0=execute(seed(),'manager',{type:'HR_STAFF_ACTIVE',id:'HR-S05',active:false});
    const d=execute(s0,'accountant',{type:'HR_FNF_SETTLE',staffId:'HR-S05',adjustments:[{label:'EL encashment',kind:'add',amount:600000},{label:'Uniform not returned',kind:'ded',amount:50000}]});
    const f=d.staffX.fnf[0];assert.equal(payroll(d).filter(v=>v.source.id.startsWith('fnf')).length,0,'a draft books nothing');
    const c=execute(d,'owner',{type:'HR_FNF_CONFIRM',id:f.id}),g=c.staffX.fnf[0];assert.equal(g.confirmedOn,D);
    const vs=payroll(c),j=one(vs,'fnf:'+f.id);assert.ok(M.accBalanced(j));assert.equal(j.type,'Journal');
    const l=g.line,rec=M.hrFnfAdvanceRecovered(g);assert.ok(rec>0,'the open advance is recovered');
    assert.deepEqual(set(ent(j)),set([['5100',l.gross,0],['5100',600000,0],['5110',l.pfEr,0],['5110',l.esicEr,0],['2310',0,l.pf+l.pfEr],['2320',0,l.esic+l.esicEr],['2330',0,l.pt],
      ['1200/HR-S05',0,rec],['2340',0,50000],['2300',0,g.net]].filter(e=>e[1]||e[2])));
    const p=one(vs,'fnf-paid:'+f.id);assert.deepEqual(ent(p),[['2300',g.net,0],['1030/BANK-1',0,g.net]]);assert.equal(p.source.sub,'fnf-payment');
  });
  test(T+'a negative full and final books Dr recoverable from the leaver (payroll:fnf-recoverable → 1200/<staff>, as the server) and no payment',()=>{
    const s=structuredClone(seed());
    s.staffX.fnf=[{id:'HR-FNF-T',staffId:'HR-S06',name:'—',lastDay:D,month:'2026-10',salaryIncluded:false,rates:{},advanceOutstanding:0,
      adjustments:[{label:'Notice pay shortfall',kind:'ded',amount:400000}],earnings:0,recoveries:400000,net:-400000,by:'accountant',at:D+'T10:00',status:'Settled',confirmedBy:'owner',confirmedAt:D+'T11:00',confirmedOn:D}];
    const vs=payroll(s);assert.equal(vs.length,1);
    assert.deepEqual(ent(vs[0]),[['2340',0,400000],['1200/HR-S06',400000,0]]);assert.match(vs[0].narration,/recoverable from the leaver ₹4000\.00/);
  });
  test(T+'payroll keys are mapped like the server (Payroll group in the mapping table) and the Owner can re-point them',()=>{
    const s=t13(),rows=M.accMapRows(s),get=k=>rows.find(r=>r.key===k);
    for(const [k,a] of Object.entries({'payroll:salaries':'5100','payroll:employer-pf':'5110','payroll:employer-esic':'5110','payroll:net-payable':'2300','payroll:pf-payable':'2310',
      'payroll:esic-payable':'2320','payroll:pt-payable':'2330','payroll:other-deductions':'2340','payroll:advances':'1200','payroll:fnf-additions':'5100','payroll:fnf-deductions':'2340',
      'payroll:fnf-recoverable':'1200','line:food':'4200'})){assert.equal(M.ACC_DEFAULT_MAP[k],a,k);assert.equal(get(k)?.account,a,k);}
    assert.equal(get('payroll:salaries').group,'Payroll');assert.ok(get('payroll:salaries').uses>=1);assert.ok(M.ACC_KEY_PATTERN.test('payroll:salaries'));
    const o=execute(s,'owner',{type:'ACC_MAP_SET',source:'payroll:salaries',account:'5120'});
    assert.equal(one(payroll(o),'payroll:HR-PAY-T13').entries[0].account,'5120');
  });
  test(T+'BASE: the seeded advance (10 Aug) and the August run locked on 2 Sep are vouchers on those dates; FY26 history books its runs on their lock dates',()=>{
    const s=seed();assert.ok(M.accDates(s).includes('2026-08-10')&&M.accDates(s).includes('2026-09-02'));
    assert.deepEqual(payroll(s,'2026-08-10').map(v=>v.source.id),['advance:HR-ADV-1']);
    assert.deepEqual(payroll(s,'2026-09-02').map(v=>v.source.id),['payroll:HR-PAY-1']);
    assert.ok(payroll(s,'2026-09-02').every(M.accBalanced));
  });

  // ── F&B folio lines (contract §8, rulebook A3) ──
  test(T+'POS charge-to-folio and a catalogue extra of GST class food are food lines → Cr 4200; other extras stay service → 4100; reports and reconciliation agree',()=>{
    let s=seed();const b=s.bookings.find(x=>x.room==='201'&&x.status==='In house');
    s=execute(s,'reception',{type:'POS_ITEM_ADD',id:'POS-F04',item:'m-lassi'});s=execute(s,'reception',{type:'POS_KOT_SEND',id:'POS-F04'});
    s=execute(s,'reception',{type:'POS_SETTLE',id:'POS-F04',mode:'folio'});
    const pl=s.bookings.find(x=>x.id===b.id).lines.at(-1);assert.equal(pl.kind,'food');
    const food=M.catExtras(s).find(x=>x.gst==='food'&&x.active);
    s=food?execute(s,'reception',{type:'CAT_CHARGE',id:b.id,extraId:food.id,amount:15000}):execute(s,'reception',{type:'CAT_CHARGE',id:b.id,extraId:'ext-laundry',gst:'food',amount:15000});
    const xl=s.bookings.find(x=>x.id===b.id).lines.at(-1);assert.equal(xl.kind,'food');
    s=execute(s,'reception',{type:'CAT_CHARGE',id:b.id,extraId:'ext-laundry'});assert.equal(s.bookings.find(x=>x.id===b.id).lines.at(-1).kind,'service');
    const vs=M.accLiveVouchers(s,D),of=l=>vs.find(v=>v.source.id===`${b.id}/${l.id}`);
    assert.equal(of(pl).entries[1].account,'4200');assert.equal(of(pl).entries[1].credit,pl.amount);assert.equal(of(pl).source.sub,'food');
    assert.equal(of(xl).entries[1].account,'4200');
    assert.equal(of(s.bookings.find(x=>x.id===b.id).lines.at(-1)).entries[1].account,'4100');
    assert.ok(M.accReconcile(s,D).every(c=>c.ok),JSON.stringify(M.accReconcile(s,D).filter(c=>!c.ok)));
    // the day report keeps F&B on the folio inside service revenue (as the server); GSTR-1 treats it as the service it was
    const pack=M.reportPack(s,D,D),svc=pack.tabs.find(t=>t.id==='revenue').metrics.find(m=>m.id==='service');
    assert.ok(svc.rows.some(r=>r.ref===pl.id&&r.kind==='food'));
    const row=M.accMapRows(s).find(r=>r.key==='line:food');assert.deepEqual([row.group,row.account,row.mapped],['Folio line kind','4200',true]);assert.ok(row.uses>=2);
  });
  test(T+'scenario ACC-RULEBOOK: no line:service override any more — restaurant on folios reaches 4200 by line:food; 4100 untouched',()=>{
    const s=M.loadScenario('ACC-RULEBOOK');
    assert.equal((s.accountingX.mapping??{})['line:service'],undefined);assert.equal(M.accMapping(s)['line:service'],'4100');
    const lines=s.bookings.flatMap(b=>b.lines.filter(l=>l.id.startsWith('posline:')));assert.equal(lines.length,20);assert.ok(lines.every(l=>l.kind==='food'));
    const tb=M.accTrialBalance(M.accVouchersUpTo(s,'2026-04-30'));
    assert.equal(tb.find(r=>r.account==='4200').balance,-16000000);assert.equal(tb.find(r=>r.account==='4100'),undefined);
  });
}
