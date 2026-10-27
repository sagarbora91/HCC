// DEC-115 · the entire SCC payroll rules (owner, 27 Oct 2026). SCC's own calcAttendance / calcGM / ptFor, copied below from
// E:\Projects\HCC\Migration data on 25 oct\HCC-Migration-2026-10-25\reference-inputs\SCC-modules\payroll\index.html (rupees, floats),
// are the oracle: HCC (integer paise) must give the same days exactly and the same money to the paisa. Names prefixed [PAY-SCC-PORT].
export function register({test,M,assert}){
  // ── SCC, verbatim apart from taking its inputs as arguments instead of reading `state` ──
  const safeN=v=>{const x=parseFloat(v);return Number.isFinite(x)?x:0;};
  const roundHalf=n=>Math.round((n+Number.EPSILON)*2)/2;
  function calcAttendance(row,totalDays){
    const lateDeduction=roundHalf(Math.floor(safeN(row.late)/3)*0.5);
    const absentDeduction=roundHalf(safeN(row.absent));
    const halfDayDeduction=roundHalf(safeN(row.halfDay)*0.5);
    const noThumbDeduction=roundHalf(safeN(row.noThumb)*0.5);
    const totalDeduction=roundHalf(lateDeduction+absentDeduction+halfDayDeduction+noThumbDeduction);
    let extraDays=totalDeduction<=5.5?4:totalDeduction<7?2:0;
    const finalPresent=Math.max(0,roundHalf(totalDays-totalDeduction));
    const raw=roundHalf(finalPresent+safeN(row.leavesApplied)+extraDays);
    const totalSalaryDays=Math.min(totalDays,Math.max(0,raw));
    const otDays=Math.max(0,roundHalf(raw-totalDays));
    return{totalDays,lateDeduction,absentDeduction,halfDayDeduction,noThumbDeduction,totalDeduction,extraDays,finalPresent,totalSalaryDays,otDays};
  }
  const P={maleExempt:7500,maleMid:10000,maleMidAmt:175,stdAmt:200,febAmt:300,femaleExempt:25000};
  function ptFor(gross,gender,monthName){
    const isFeb=String(monthName)==='February',top=isFeb?P.febAmt:P.stdAmt;
    if(gender==='F')return gross<=P.femaleExempt?0:top;
    if(gross<=P.maleExempt)return 0;
    if(gross<=P.maleMid)return P.maleMidAmt;
    return top;
  }
  function calcGM(row,totalDays,monthName){
    const att=calcAttendance(row,totalDays);const td=att.totalDays;
    const grossPayable=td>0?safeN(row.gross)*att.totalSalaryDays/td:0;
    let basic,hra,washing;
    if(row.salaryType==='S'){basic=grossPayable*0.50;hra=basic*0.80;washing=basic*0.10;}else{basic=grossPayable;hra=0;washing=0;}
    const esicBase=grossPayable-washing;
    const pt=ptFor(grossPayable,row.gender,monthName);
    const pfOn=!!row.pfApplicable,pfWages=pfOn?Math.min(basic,15000):0;
    const pfEE=pfOn?Math.round(pfWages*(12/100)*100)/100:0,pfEmpr=pfOn?Math.round(pfWages*(13/100)*100)/100:0;
    const esicOn=!!row.esicApplicable&&grossPayable<=21000;
    const esicEE=esicOn?Math.round(esicBase*(0.75/100)*100)/100:0,esicEmpr=esicOn?Math.round(esicBase*(3.25/100)*100)/100:0;
    const netPayable=grossPayable-pt-pfEE-esicEE;
    const otAmount=td>0?safeN(row.gross)/td*att.otDays:0;
    return{...att,grossPayable,basic,hra,washing,pt,pfWages,pfEE,esicEE,pfEmpr,esicEmpr,netPayable,otAmount,finalPay:netPayable+otAmount};
  }

  const R=M.hrDefaultRates();
  /** HCC month counts for a sheet: `absent` days are weekly offs taken, the rest present / late / half / paid leave. */
  const counts=({ym='2026-09',days=30,mgmtHol=0,absent=0,half=0,late=0,leave=0,noThumb=0})=>{
    const td=days-mgmtHol,P=td-absent-half-late-leave-noThumb;
    return {ym,days,P,L:late,H:half,HL:0,A:0,OFFW:0,off:absent,paidLeave:leave,lopLeave:0,unmarked:0,na:0,otMin:0,...(mgmtHol?{mgmtHol}:{}),...(noThumb?{NT:noThumb}:{})};
  };
  const near=(a,b,tol,msg)=>assert.ok(Math.abs(a-b)<=tol,`${msg}: ${a} vs ${b}`);

  test('[PAY-SCC-PORT] the day sheet equals SCC calcAttendance on every grid point (total days, absent, half days, late marks, leave, paid holidays)',()=>{
    let n=0;
    for(const days of [28,30,31])for(const mgmtHol of [0,1])for(const absent of [0,2,4,5,5.5,6,6.5,7,9])for(const half of [0,1,3])for(const late of [0,2,3,6,7])for(const leave of [0,1,2.5,5.5,6])for(const noThumb of [0,1,3]){
      const c=counts({days,mgmtHol,absent,half,late,leave,noThumb});if(c.P<0)continue;
      const d=M.hrSccDays(c,R),s=calcAttendance({absent,halfDay:half,late,noThumb,leavesApplied:leave},days-mgmtHol);
      assert.deepEqual([d.total,d.lateDed,d.absentDed,d.halfDed,d.noThumbDed,d.deduction,d.extra,d.finalPresent,d.salaryDays,d.otDays],
        [s.totalDays,s.lateDeduction,s.absentDeduction,s.halfDayDeduction,s.noThumbDeduction,s.totalDeduction,s.extraDays,s.finalPresent,s.totalSalaryDays,s.otDays],JSON.stringify({days,mgmtHol,absent,half,late,leave,noThumb}));
      n++;
    }
    assert.ok(n>9000,`grid points ${n}`);
  });
  test('[PAY-SCC-PORT] the extra-day bands and the cap: 4 offs = full pay, 6 days off = 2 extra, 7 = none; working every day = 4 OT days',()=>{
    const d=x=>M.hrSccDays(counts(x),R);
    assert.deepEqual([d({absent:4}).salaryDays,d({absent:4}).otDays],[30,0]);
    assert.deepEqual([d({absent:5.5}).extra,d({absent:5.5}).salaryDays],[4,28.5]);
    assert.deepEqual([d({absent:6}).extra,d({absent:6}).salaryDays,d({absent:6.5}).salaryDays],[2,26,25.5]);
    assert.deepEqual([d({absent:7}).extra,d({absent:7}).salaryDays],[0,23]);
    assert.deepEqual([d({absent:0}).salaryDays,d({absent:0}).otDays],[30,4]);
    assert.deepEqual([d({absent:2}).otDays,d({absent:4,leave:2}).otDays],[2,2]);// SCC: approved leave is added on top of final present
    assert.deepEqual([d({absent:4,late:3}).lateDed,d({absent:4,late:3}).salaryDays,d({absent:4,late:6}).salaryDays],[0.5,29.5,29]);
    assert.deepEqual([d({absent:4,half:1}).halfDed,d({absent:4,half:1}).salaryDays],[0.5,29.5]);
    assert.deepEqual([d({absent:4,noThumb:1}).noThumbDed,d({absent:4,noThumb:1}).salaryDays,d({absent:4,noThumb:4}).salaryDays],[0.5,29.5,26]);// SCC No Thumb: ½ day each (4 marks: deduction 6 → only 2 extra days)
    const none=M.hrSccDays({...counts({}),P:0,off:30},R);assert.deepEqual([none.deduction,none.salaryDays],[30,0]);
  });
  test('[PAY-SCC-PORT] pay equals SCC calcGM to within 2 paise: grossPayable, split, PF, ESIC (month by month), PT, OT days, final pay',()=>{
    let n=0;
    for(const gross of [700000,950000,1350000,1800000,2100000,2600000,4000000])for(const structure of ['S','B'])for(const gender of ['M','F'])
      for(const [ym,days,monthName] of [['2026-09',30,'September'],['2027-02',28,'February'],['2026-10',31,'October']])for(const sheet of [{absent:4},{absent:0,late:3},{absent:6,half:1},{absent:8},{absent:4,leave:2}]){
        const st={id:'T',name:'T',dept:'FO',designation:'x',payType:'monthly',monthly:gross,daily:0,structure,gender};
        const l=M.hrComputeLine(st,counts({ym,days,...sheet}),R);
        const row={gross:gross/100,salaryType:structure,gender,pfApplicable:structure!=='B',esicApplicable:true,absent:sheet.absent,halfDay:sheet.half??0,late:sheet.late??0,noThumb:0,leavesApplied:sheet.leave??0};
        const c=calcGM(row,days,monthName),p=v=>v*100,tag=JSON.stringify({gross,structure,gender,ym,sheet});
        near(l.earned,p(c.grossPayable),0.5,'earned '+tag);near(l.basic,p(c.basic),1,'basic '+tag);near(l.washing,p(c.washing),1,'washing '+tag);
        // HCC rounds to the paisa at each step (earned, then basic, then the percentage); SCC keeps floats until the percentage — at most 2 paise apart.
        near(l.pf,p(c.pfEE),2,'pf '+tag);near(l.pfEr,p(c.pfEmpr),2,'pfEr '+tag);near(l.esic,p(c.esicEE),2,'esic '+tag);near(l.esicEr,p(c.esicEmpr),2,'esicEr '+tag);
        assert.equal(l.pt,p(c.pt),'pt '+tag);near(l.ot,p(c.otAmount),0.5,'ot '+tag);near(l.net,p(c.finalPay),4,'net '+tag);
        assert.equal(l.payableDays,c.totalSalaryDays);assert.equal(l.days.otDays,c.otDays);
        n++;
      }
    assert.equal(n,420);
  });
  test('[PAY-SCC-PORT] daily-wage staff: gross = daily rate × total days, so pay = daily rate × salary days and OT = daily rate × OT days',()=>{
    const st={id:'T',name:'T',dept:'KIT',designation:'x',payType:'daily',monthly:0,daily:80000,structure:'B',gender:'M'};
    const four=M.hrComputeLine(st,counts({absent:4}),R),all=M.hrComputeLine(st,counts({absent:0}),R),seven=M.hrComputeLine(st,counts({absent:7}),R);
    assert.deepEqual([four.earned,four.ot,all.earned,all.ot,seven.earned],[2400000,0,2400000,320000,1840000]);// 30 × ₹800; + 4 OT days ₹3,200; 23 × ₹800
  });
  test('[PAY-SCC-PORT] total days: calendar days less paid holidays (SCC Mgmt holidays), or the rate version\'s Days Override',()=>{
    const c=counts({days:31,mgmtHol:2,absent:4});assert.equal(M.hrSccDays(c,R).total,29);
    assert.equal(M.hrSccDays(counts({days:31,absent:4}),{...R,daysOverride:26}).total,26);
    assert.equal(M.hrSccDays(counts({days:31,mgmtHol:1,absent:4}),{...R,daysOverride:26}).total,25);
    // a month read only up to a date counts its elapsed share of the total days (SCC feed): 10 of 30 days elapsed, 8 present, 2 off
    const part={...counts({absent:0}),P:8,off:2,elapsed:10};assert.deepEqual([M.hrSccDays(part,R).absent,M.hrSccDays(part,R).deduction],[2,2]);
  });
  test('[PAY-SCC-PORT] SCC issues: leave above 5.5 days and a deduction of every day block the lock; high deduction, OT days and 3 late marks warn',()=>{
    const d=x=>M.hrSccDays(counts(x),R);
    assert.deepEqual(M.hrSccIssues('Asha',d({absent:4,leave:6})).map(i=>i.type+': '+i.text),['risk: Asha: Leaves applied (6) exceed 5.5-day limit.','warn: Asha: 6 OT day(s) — salary capped at 30, OT tracked separately.']);
    assert.deepEqual(M.hrSccIssues('Asha',d({absent:6.5})).map(i=>i.type),['warn']);
    assert.deepEqual(M.hrSccIssues('Asha',d({absent:30})).map(i=>i.text),['Asha: Total deduction (30) ≥ total days (30) — zero salary days.']);
    assert.deepEqual(M.hrSccIssues('Asha',d({absent:4,late:3})).map(i=>i.text),['Asha: 3 late mark(s).']);
    assert.deepEqual(M.hrSccIssues('Asha',d({absent:4,noThumb:2})).map(i=>i.text),['Asha: 2 no-thumb entry/entries.']);
    const line={...M.hrComputeLine({id:'T',name:'Asha',dept:'FO',designation:'x',payType:'monthly',monthly:1800000,daily:0},counts({absent:4,leave:6}),R)};
    assert.deepEqual(M.hrRunErrors({lines:[line]}),['Asha: Leaves applied (6) exceed 5.5-day limit.']);
  });
  test('[PAY-SCC-PORT] No Thumb (SCC): a manager marks it; it counts as present with a ½-day deduction and a correction to it lowers pay at once',()=>{
    const {seed,execute}=M,x=s=>M.hrSlice(s);
    const s=execute(seed(),'manager',{type:'HR_ATT_MARK',staffId:'HR-S06',status:'NT',otMin:0});assert.equal(x(s).att[s.date+'|HR-S06'].status,'NT');
    const st=x(s).staff.find(t=>t.id==='HR-S06'),c=M.hrCounts(s,st,s.date.slice(0,7),s.date);assert.equal(c.NT,1);
    // Kavya, 30 Sep present → no thumb: deduction 5.5 → 6, so SCC gives 2 extra days instead of 4 → 26 salary days (₹11,700) instead of 28.5
    // (₹12,825): −₹1,125, applies at once.
    const k=execute(seed(),'manager',{type:'HR_ATT_CORRECT',staffId:'HR-S06',date:'2026-09-30',mark:'NT',reason:'Register checked (fictional)'});
    const r=x(k).requests.at(-1);assert.deepEqual([r.status,r.direct,r.payEffect],['Approved',true,-112500]);
    assert.equal(M.HR_ATT_LABEL.NT,'No thumb punch');assert.ok(M.HR_ATT_MARKS.includes('NT'));
  });
  test('[PAY-SCC-PORT] defaults follow SCC: ESIC month by month, OT day at 1×, 3 late marks = ½ day, calendar days',()=>{
    assert.deepEqual([R.esicPeriod,R.otBps,R.lateBlock,R.daysOverride??0],[false,10000,3,0]);
    assert.match(M.HR_SCC_DAYS,/4 extra days/);assert.match(M.HR_SCC_DAYS,/OT days/);
  });
}
