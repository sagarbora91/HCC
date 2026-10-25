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
}
