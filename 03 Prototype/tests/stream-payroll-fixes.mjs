// W05-B code-review fixes (05 Delivery Evidence\Assembly\W05B_Payroll_Code_Review_2026-10-25.md). One regression test per finding, names [PAY-FIX F-nn].
// Money = integer paise; simulated clock only; fictional staff.
export async function register({test,M,assert}){
  const {seed,execute}=M;
  const x=s=>M.hrSlice(s);
  const code=c=>e=>e.code===c;

  test('[PAY-FIX F-01] LOP leave spanning a weekly off: the off day is not deducted (lopLeave = stored days)',()=>{
    let s=seed();// Aarav (HR-S02), ₹18,000/month, weekly off Tuesday; LOP Mon 9 – Wed 11 Nov 2026
    s=execute(s,'reception',{type:'HR_LEAVE_REQUEST',staffId:'HR-S02',leaveType:'LOP',from:'2026-11-09',to:'2026-11-11',reason:'Personal'});
    const lv=x(s).leaves.at(-1);assert.equal(lv.days,2);
    s=execute(s,'manager',{type:'HR_LEAVE_DECIDE',id:lv.id,approve:true});
    const st=x(s).staff.find(t=>t.id==='HR-S02'),c=M.hrCounts(s,st,'2026-11');
    assert.equal(c.lopLeave,lv.days);
    const line=M.hrComputeLine(st,{...c,unmarked:0},M.hrRatesFor(s,'2026-11'));
    assert.equal(line.payableDays,28);assert.equal(line.earned,M.hrMulDiv(1800000,56,1,60));
    assert.equal(M.hrDayStatus(s,st,'2026-11-10').kind,'off');assert.equal(M.hrDayStatus(s,st,'2026-11-09').kind,'leave');
    // paid leave over an off day: "Paid leave" equals the stored days too (HR-LV-2: EL Mon 2 – Tue 3 Nov, Tuesday off)
    const el=x(s).leaves.find(l=>l.id==='HR-LV-2');assert.equal(el.days,1);assert.equal(c.paidLeave,el.days);
  });
  const staffCmd=(st,extra={})=>({type:'HR_STAFF_SAVE',id:st.id,name:st.name,dept:st.dept,designation:st.designation,defaultShift:st.defaultShift,weeklyOff:st.weeklyOff,phone:st.phone,...(st.userId?{userId:st.userId}:{}),...(st.gender?{gender:st.gender}:{}),...extra});
  const S=(s,id)=>x(s).staff.find(t=>t.id===id);

  test('[PAY-FIX F-02] bank file uses the bank details frozen at lock; later edits apply to future runs; audit shows masked old → new',()=>{
    let s=execute(seed(),'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});const id=M.hrRunFor(s,'2026-09').id;
    s=execute(s,'owner',{type:'HR_PAYROLL_LOCK',id});
    const old=S(s,'HR-S05').accountNo;assert.equal(M.hrRunFor(s,'2026-09').bank['HR-S05'].accountNo,old);
    s=execute(s,'accountant',staffCmd(S(s,'HR-S05'),{accountNo:'999999999999',ifsc:'EVIL0000001'}));
    assert.equal(S(s,'HR-S05').accountNo,'999999999999','the staff master changes (future runs)');
    const row=M.hrBankCsv(s,M.hrRunFor(s,'2026-09')).split('\n').find(l=>l.includes(',HR-S05,'));
    assert.ok(row.includes("'"+old)&&!row.includes('999999999999')&&!row.includes('EVIL'),row);
    assert.match(M.hrBankCsv(s,M.hrRunFor(s,'2026-09')),/bank details as locked/);
    const a=s.audit[0].detail;assert.match(a,new RegExp('••••'+old.slice(-4)+'.*→.*••••9999'));assert.ok(!a.includes('999999999999'),'account masked in the audit');
    // the fixture's locked August run also carries its snapshot
    assert.equal(M.hrRunFor(seed(),'2026-08').bank['HR-S05'].accountNo,old);
    // own bank details: refused (no self-approval)
    assert.throws(()=>execute(seed(),'accountant',staffCmd(S(seed(),'HR-S07'),{accountNo:'11112222333'})),code('FORBIDDEN'));
  });

  test('[PAY-FIX F-03] login links are Owner-only: a manager cannot redirect "own" payslip access; relink with locked payslips needs confirmation',()=>{
    const s=seed(),noLink=st=>{const c=staffCmd(st);delete c.userId;return c;};
    // reviewer's t6: Manager unlinks own record, then links self to the Accountant's record
    assert.throws(()=>execute(s,'manager',noLink(S(s,'HR-S01'))),code('FORBIDDEN'));
    const u=execute(s,'owner',noLink(S(s,'HR-S07')));// Owner unlinks Farhan (no payslip exposure)
    assert.throws(()=>execute(u,'manager',staffCmd(S(u,'HR-S07'),{userId:'manager'})),code('FORBIDDEN'));
    assert.deepEqual(M.hrVisiblePayslips(u,'manager').map(p=>p.line.staffId).filter(id=>id!=='HR-S01'),[]);
    // Owner relinks: refused without confirmation (Farhan has locked payslips), allowed with it, audited old → new
    const free=execute(u,'owner',noLink(S(u,'HR-S08')));
    assert.throws(()=>execute(free,'accountant',staffCmd(S(free,'HR-S09'),{userId:'maint'})),code('FORBIDDEN'),'Accountant is payroll but not admin');
    assert.throws(()=>execute(free,'owner',staffCmd(S(free,'HR-S07'),{userId:'maint'})),code('INVALID_TRANSITION'));
    const ok=execute(free,'owner',staffCmd(S(free,'HR-S07'),{userId:'maint',confirmRelink:true}));
    assert.equal(S(ok,'HR-S07').userId,'maint');assert.match(ok.audit[0].detail,/login \(none\) → maint/);
    // the Owner cannot link a record to themselves
    assert.throws(()=>execute(u,'owner',staffCmd(S(u,'HR-S07'),{userId:'owner',confirmRelink:true})),code('FORBIDDEN'));
    // a manager may still edit non-link fields of a linked record (link unchanged)
    assert.equal(S(execute(s,'manager',staffCmd(S(s,'HR-S05'),{designation:'Senior room attendant'})),'HR-S05').designation,'Senior room attendant');
    // the dialog shows the link as Owner-only for others
    const html=M.hrCanLink(s,'manager');assert.equal(html,false);assert.equal(M.hrCanLink(s,'owner'),true);
  });
}
