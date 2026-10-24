// Module 17 · HR & payroll — domain/command tests. Names prefixed [HR].
export function register({test,M,assert}){
  const {seed,execute}=M;
  const code=c=>e=>e.code===c;
  const x=s=>M.hrSlice(s);
  const st=(s,id)=>x(s).staff.find(t=>t.id===id);

  test('[HR] fixture: 12 fictional staff, 8 linked logins, locked August run, advance partly recovered',()=>{
    const s=seed();assert.equal(x(s).staff.length,12);assert.equal(x(s).staff.filter(t=>t.userId).length,8);
    const aug=M.hrRunFor(s,'2026-08');assert.equal(aug.status,'Locked');assert.equal(aug.lines.length,12);
    assert.equal(x(s).advances[0].recovered,100000);assert.equal(M.hrRunFor(s,'2026-09'),undefined);
    assert.ok(Object.keys(x(s).att).every(k=>!k.startsWith('2026-08')),'raw August marks dropped');
    for(const l of aug.lines){assert.ok(Number.isSafeInteger(l.net));assert.equal(l.net,l.gross-l.pf-l.esic-l.pt-l.advance);}
  });
  test('[HR] slice absent (old save) reads as empty',()=>{const s=seed();delete s.staffX;assert.deepEqual(x(s).staff,[]);assert.equal(M.hrRunFor(s,'2026-08'),undefined);});
  test('[HR] role visibility: salaries for Owner/Accountant only; managing for approve holders',()=>{
    const s=seed();
    assert.deepEqual(['owner','manager','cashier','accountant','housekeeper','techadmin'].map(u=>M.hrCanPay(s,u)),[true,false,false,true,false,false]);
    assert.deepEqual(['owner','manager','accountant','supervisor'].map(u=>M.hrCanManage(s,u)),[true,true,false,false]);
    assert.equal(M.hrCanRates(s,'owner'),true);assert.equal(M.hrCanRates(s,'accountant'),false);
  });
  test('[HR] payroll line formula (monthly): LOP, late rule, OT, illustrative PF/ESIC/PT, advance capped by balance',()=>{
    const r=M.hrDefaultRates();
    const staff={id:'T',name:'Test',dept:'FO',designation:'x',payType:'monthly',monthly:3000000,daily:0};
    const c={days:30,P:22,L:3,H:1,A:1,OFFW:0,off:3,paidLeave:0,lopLeave:0,unmarked:0,na:0,otMin:120};
    const l=M.hrComputeLine(staff,c,r,{installment:100000,balance:50000});
    assert.equal(l.latePenaltyHalf,1);assert.equal(l.payableDays,28);assert.equal(l.basic,2800000);assert.equal(l.ot,37500);
    assert.equal(l.gross,2837500);assert.equal(l.pf,168000);assert.equal(l.esic,0);assert.equal(l.pt,20000);assert.equal(l.advance,50000);assert.equal(l.net,2599500);
  });
  test('[HR] payroll line formula (daily wage) and ESIC under ceiling; advance never makes net negative',()=>{
    const r=M.hrDefaultRates();
    const d=M.hrComputeLine({id:'D',name:'D',dept:'KIT',designation:'x',payType:'daily',monthly:0,daily:60000},{days:30,P:20,L:2,H:2,A:0,OFFW:1,off:4,paidLeave:0,lopLeave:1,unmarked:0,na:0,otMin:0},r);
    assert.equal(d.payableDays,24);assert.equal(d.basic,1440000);assert.equal(d.esic,Math.round(1440000*75/10000));assert.equal(d.pt,20000);
    const tiny=M.hrComputeLine({id:'Y',name:'Y',dept:'KIT',designation:'x',payType:'daily',monthly:0,daily:60000},{days:30,P:1,L:0,H:0,A:0,OFFW:0,off:0,paidLeave:0,lopLeave:0,unmarked:29,na:0,otMin:0},r,{installment:100000,balance:300000});
    assert.equal(tiny.net,0);assert.ok(tiny.advance<100000);
  });
  test('[HR] payroll permissions and completed-month rule',()=>{
    const s=seed();
    assert.throws(()=>execute(s,'manager',{type:'HR_PAYROLL_RUN',month:'2026-09'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'housekeeper',{type:'HR_PAYROLL_RUN',month:'2026-09'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'accountant',{type:'HR_PAYROLL_RUN',month:'2026-10'}),code('INVALID_TRANSITION'));
    const n=execute(s,'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});const r=M.hrRunFor(n,'2026-09');
    assert.equal(r.status,'Draft');assert.equal(r.lines.length,12);assert.deepEqual(r.warnings,[]);
    const ravi=r.lines.find(l=>l.staffId==='HR-S05');assert.equal(ravi.counts.paidLeave,2);assert.equal(ravi.advance,100000);
  });
  test('[HR] maker-checker lock, advance recovery posted once, locked run immutable',()=>{
    let s=execute(seed(),'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});const id=M.hrRunFor(s,'2026-09').id;
    assert.throws(()=>execute(s,'accountant',{type:'HR_PAYROLL_LOCK',id}),code('FORBIDDEN'));
    s=execute(s,'owner',{type:'HR_PAYROLL_LOCK',id,key:'lock-sep'});
    assert.equal(M.hrRunFor(s,'2026-09').status,'Locked');assert.equal(x(s).advances[0].recovered,200000);
    assert.deepEqual(execute(s,'owner',{type:'HR_PAYROLL_LOCK',id,key:'lock-sep'}),s);
    assert.throws(()=>execute(s,'owner',{type:'HR_PAYROLL_LOCK',id}),code('INVALID_TRANSITION'));
    assert.throws(()=>execute(s,'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'}),code('INVALID_TRANSITION'));
    assert.equal(M.hrVisiblePayslips(s,'housekeeper').length,2);assert.ok(M.hrVisiblePayslips(s,'housekeeper').every(p=>p.line.staffId==='HR-S05'));
    assert.equal(M.hrVisiblePayslips(s,'accountant').length,24);assert.equal(M.hrVisiblePayslips(s,'techadmin').length,0);
  });
  test('[HR] stale draft cannot be locked after rates change; recalculation fixes it; locked runs keep their rate snapshot',()=>{
    let s=execute(seed(),'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});const id=M.hrRunFor(s,'2026-09').id;
    const rates={...x(s).rates,ptThreshold:1500000};
    assert.throws(()=>execute(s,'accountant',{type:'HR_RATES_SET',...rates}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'HR_RATES_SET',...rates,otBps:5000}),code('VALIDATION'));
    s=execute(s,'owner',{type:'HR_RATES_SET',...rates});
    assert.equal(M.hrRunFor(s,'2026-08').rates.ptThreshold,1000000);
    assert.throws(()=>execute(s,'owner',{type:'HR_PAYROLL_LOCK',id}),code('STALE_VERSION'));
    s=execute(s,'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});
    assert.ok(M.hrRunFor(s,'2026-09').lines.find(l=>l.staffId==='HR-S05').pt===0);
    s=execute(s,'owner',{type:'HR_PAYROLL_LOCK',id});assert.equal(M.hrRunFor(s,'2026-09').status,'Locked');
  });
  test('[HR] self check-in: late after shift start + grace, once per day, own record only, not on weekly off',()=>{
    let s=seed();// clock 08:00 · Neha morning 07:00 → late; Kavya evening → present
    s=execute(s,'supervisor',{type:'HR_ATT_MARK',staffId:'HR-S04'});assert.equal(x(s).att['2026-10-24|HR-S04'].status,'L');
    s=execute(s,'housekeeper2',{type:'HR_ATT_MARK',staffId:'HR-S06'});assert.equal(x(s).att['2026-10-24|HR-S06'].status,'P');
    assert.throws(()=>execute(s,'housekeeper2',{type:'HR_ATT_MARK',staffId:'HR-S06'}),code('INVALID_TRANSITION'));
    assert.throws(()=>execute(s,'housekeeper',{type:'HR_ATT_MARK',staffId:'HR-S06'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'maint',{type:'HR_ATT_MARK',staffId:'HR-S08'}),/rostered off/);
    assert.throws(()=>execute(s,'housekeeper',{type:'HR_ATT_MARK',staffId:'HR-S05',status:'A'}),code('FORBIDDEN'));
  });
  test('[HR] manager attendance: overtime bounds, correction, day-closed block, no money moved',()=>{
    const s0=seed();let s=execute(s0,'manager',{type:'HR_ATT_MARK',staffId:'HR-S11',status:'P',otMin:90});
    assert.equal(x(s).att['2026-10-24|HR-S11'].otMin,90);
    assert.throws(()=>execute(s,'manager',{type:'HR_ATT_MARK',staffId:'HR-S11',status:'A',otMin:30}),code('VALIDATION'));
    assert.throws(()=>execute(s,'manager',{type:'HR_ATT_MARK',staffId:'HR-S11',status:'P',otMin:600}),code('VALIDATION'));
    s=execute(s,'manager',{type:'HR_ATT_MARK',staffId:'HR-S11',status:'H'});assert.equal(x(s).att['2026-10-24|HR-S11'].status,'H');
    assert.equal(M.drawer(s),M.drawer(s0));assert.deepEqual(M.metrics(s).collections,M.metrics(s0).collections);
    const c=structuredClone(s0);c.closedDates.push(c.date);
    assert.throws(()=>execute(c,'manager',{type:'HR_ATT_MARK',staffId:'HR-S11',status:'P'}),code('DAY_CLOSED'));
  });
  test('[HR] roster: past dates are history, future overrides apply, copy week',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'manager',{type:'HR_ROSTER_SET',staffId:'HR-S05',date:'2026-10-23',shift:'E'}),code('INVALID_TRANSITION'));
    assert.throws(()=>execute(s,'housekeeper',{type:'HR_ROSTER_SET',staffId:'HR-S05',date:'2026-10-26',shift:'E'}),code('FORBIDDEN'));
    s=execute(s,'manager',{type:'HR_ROSTER_SET',staffId:'HR-S05',date:'2026-10-26',shift:'N'});assert.equal(M.hrShiftFor(s,st(s,'HR-S05'),'2026-10-26'),'N');
    assert.equal(M.hrShiftFor(s,st(s,'HR-S05'),'2026-10-30'),'OFF');// Friday weekly off
    s=execute(s,'manager',{type:'HR_ROSTER_COPY',from:'2026-10-26'});assert.equal(M.hrShiftFor(s,st(s,'HR-S05'),'2026-11-02'),'N');
  });
  test('[HR] leave: balance, overlap, daily-wage LOP only, no backdating, weekly offs excluded',()=>{
    const s=seed();
    assert.equal(M.hrLeaveBalance(s,'HR-S05','2026').find(b=>b.type==='CL').left,6);
    assert.throws(()=>execute(s,'housekeeper',{type:'HR_LEAVE_REQUEST',staffId:'HR-S05',leaveType:'CL',from:'2026-11-02',to:'2026-11-09',reason:'Long trip'}),/balance/);
    assert.throws(()=>execute(s,'manager',{type:'HR_LEAVE_REQUEST',staffId:'HR-S10',leaveType:'CL',from:'2026-11-02',to:'2026-11-02',reason:'Trip'}),/without pay/);
    assert.throws(()=>execute(s,'housekeeper2',{type:'HR_LEAVE_REQUEST',staffId:'HR-S06',leaveType:'SL',from:'2026-10-29',to:'2026-10-30',reason:'Unwell'}),code('NOT_AVAILABLE'));
    assert.throws(()=>execute(s,'housekeeper',{type:'HR_LEAVE_REQUEST',staffId:'HR-S05',leaveType:'CL',from:'2026-10-20',to:'2026-10-20',reason:'Late'}),code('VALIDATION'));
    assert.throws(()=>execute(s,'housekeeper',{type:'HR_LEAVE_REQUEST',staffId:'HR-S06',leaveType:'CL',from:'2026-11-02',to:'2026-11-02',reason:'Not mine'}),code('FORBIDDEN'));
    const n=execute(s,'housekeeper',{type:'HR_LEAVE_REQUEST',staffId:'HR-S05',leaveType:'CL',from:'2026-10-29',to:'2026-10-31',reason:'Family visit',key:'lv-1'});
    assert.equal(x(n).leaves.at(-1).days,2);// Fri 30 Oct is Ravi's weekly off
    assert.deepEqual(execute(n,'housekeeper',{type:'HR_LEAVE_REQUEST',staffId:'HR-S05',leaveType:'CL',from:'2026-10-29',to:'2026-10-31',reason:'Family visit',key:'lv-1'}),n);
  });
  test('[HR] leave approval: no self-approval (requester or person on leave), rejection needs a reason',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'manager',{type:'HR_LEAVE_DECIDE',id:'HR-LV-4',approve:true}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'housekeeper2',{type:'HR_LEAVE_DECIDE',id:'HR-LV-3',approve:true}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'HR_LEAVE_DECIDE',id:'HR-LV-4',approve:false}),code('VALIDATION'));
    s=execute(s,'owner',{type:'HR_LEAVE_DECIDE',id:'HR-LV-4',approve:true});
    s=execute(s,'manager',{type:'HR_LEAVE_DECIDE',id:'HR-LV-3',approve:false,note:'Short-staffed that week'});
    assert.deepEqual(['HR-LV-3','HR-LV-4'].map(id=>x(s).leaves.find(l=>l.id===id).status),['Rejected','Approved']);
    s=execute(s,'manager',{type:'HR_LEAVE_REQUEST',staffId:'HR-S01',leaveType:'CL',from:'2026-11-05',to:'2026-11-05',reason:'Own leave'});
    const own=x(s).leaves.at(-1).id;
    assert.equal(M.hrDecidable(s,'manager').some(l=>l.id===own),false);
    assert.throws(()=>execute(s,'manager',{type:'HR_LEAVE_DECIDE',id:own,approve:true}),code('FORBIDDEN'));
    s=execute(s,'owner',{type:'HR_LEAVE_DECIDE',id:own,approve:true});
    assert.equal(M.hrDayStatus(s,st(s,'HR-S01'),'2026-11-05').kind,'leave');
    assert.throws(()=>execute(s,'housekeeper',{type:'HR_LEAVE_DECIDE',id:'HR-LV-2',approve:true}),code('FORBIDDEN'));
  });
  test('[HR] staff master: managers cannot set salary; payroll roles can; user link unique',()=>{
    let s=seed();
    const base={type:'HR_STAFF_SAVE',name:'Lakshmi Pillai',dept:'KIT',designation:'Cook (trainee)',defaultShift:'M',weeklyOff:2};
    assert.throws(()=>execute(s,'manager',{...base,payType:'monthly',monthly:1500000}),code('FORBIDDEN'));
    s=execute(s,'manager',base);assert.equal(x(s).staff.at(-1).monthly,0);
    s=execute(s,'manager',{type:'HR_STAFF_SAVE',id:'HR-S05',name:'Ravi Shinde',dept:'HK',designation:'Senior room attendant',defaultShift:'M',weeklyOff:5,userId:'housekeeper'});
    assert.equal(st(s,'HR-S05').monthly,1350000);assert.equal(st(s,'HR-S05').designation,'Senior room attendant');
    s=execute(s,'accountant',{type:'HR_STAFF_SAVE',id:'HR-S05',name:'Ravi Shinde',dept:'HK',designation:'Senior room attendant',defaultShift:'M',weeklyOff:5,userId:'housekeeper',payType:'monthly',monthly:1450000});
    assert.equal(st(s,'HR-S05').monthly,1450000);
    assert.throws(()=>execute(s,'owner',{...base,userId:'housekeeper'}),/already linked/);
    assert.throws(()=>execute(s,'cashier',base),code('FORBIDDEN'));
  });
  test('[HR] advances: payroll only, one open per staff, installment ≤ amount',()=>{
    const s=seed();
    assert.throws(()=>execute(s,'manager',{type:'HR_ADVANCE_ADD',staffId:'HR-S09',amount:200000,installment:50000,reason:'x'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'HR_ADVANCE_ADD',staffId:'HR-S05',amount:200000,installment:50000,reason:'x'}),code('INVALID_TRANSITION'));
    assert.throws(()=>execute(s,'owner',{type:'HR_ADVANCE_ADD',staffId:'HR-S09',amount:200000,installment:250000,reason:'x'}),code('VALIDATION'));
    const n=execute(s,'accountant',{type:'HR_ADVANCE_ADD',staffId:'HR-S09',amount:200000,installment:50000,reason:'Medical (fictional)'});
    assert.equal(M.hrOpenAdvance(n,'HR-S09').amount,200000);assert.equal(M.drawer(n),M.drawer(s));
  });
  test('[HR] payroll register CSV: header, one row per staff, totals, disclaimer',()=>{
    const s=seed(),r=M.hrRunFor(s,'2026-08'),csv=M.hrRegisterCsv(s,r).trim().split('\n');
    assert.ok(csv[0].startsWith('Month,Staff ID,Name'));assert.equal(csv.length,1+12+1+1);
    const t=M.hrRunTotals(r);assert.ok(csv[13].includes('TOTAL')&&csv[13].endsWith((t.net/100).toFixed(2)));
    assert.match(csv[14],/not statutory advice/);
  });
  test('[HR] exited staff leave the roster and later payroll; unmarked days are warned and counted absent',()=>{
    let s=execute(seed(),'manager',{type:'HR_STAFF_ACTIVE',id:'HR-S12',active:false});
    assert.equal(st(s,'HR-S12').exitedOn,'2026-10-24');assert.equal(M.hrInMonth(st(s,'HR-S12'),'2026-11'),false);assert.equal(M.hrInMonth(st(s,'HR-S12'),'2026-10'),true);
    assert.throws(()=>execute(s,'manager',{type:'HR_STAFF_ACTIVE',id:'HR-S01',active:false}),code('FORBIDDEN'));
    const c=structuredClone(seed());delete c.staffX.att['2026-09-10|HR-S09'];
    const r=M.hrComputeRun(c,'2026-09');assert.ok(r.warnings.some(w=>/Suresh Pawar: 1 unmarked/.test(w)));
  });
}
