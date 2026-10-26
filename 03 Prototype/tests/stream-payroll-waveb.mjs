// Wave B item B4 — owner decisions DEC-075 (correcting past attendance, audit A04-02), DEC-083 (paid-holiday calendar, A04-05) and DEC-084
// (bank-detail change with the Owner's approval, O-16). Same rules and figures as the server tests (08 Production/tests/Hcc.Payroll.Tests:
// CorrectionRulesTests, PayrollCorrectionSqlTests). BASE fixture: business date 24 Oct 2026, August 2026 locked. Money in paise, worked by hand.
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  const {seed,execute}=M;
  await writeFile('tests/.generated/payroll-wb-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can} from '../../src/services/mock';
import {HR_SECTIONS} from '../../src/features/staff/HrDesk';
import {registration} from '../../src/features/staff/register';
function ctx(s:any,uid:string){const user=s.users.find((u:any)=>u.id===uid);
  return {s,uid,user,busy:false,run:async()=>true,open(){},closeModal(){},toast(){},setDirty(){},permission:(p:any)=>can(s,uid,p),display:{isMobile:false}} as any;}
const wrap=(s:any,uid:string,el:any)=>renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid)}>{el}</HccContext.Provider>);
export function section(s:any,uid:string,name:string,props:any={}){const C=(HR_SECTIONS as any)[name];return wrap(s,uid,<C phone={false} {...props}/>);}
export function modal(s:any,uid:string,type:string,id?:string,room?:string){const C=(registration.modals as any)[type];return wrap(s,uid,<C id={id} room={room}/>);}
`);
  await build({entryPoints:['tests/.generated/payroll-wb-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/payroll-wb-ui.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __wbReq} from 'node:module';const require=__wbReq(import.meta.url);"}});
  const UI=await import('./.generated/payroll-wb-ui.mjs');
  const x=s=>M.hrSlice(s),code=c=>e=>e.code===c,st=(s,id)=>x(s).staff.find(t=>t.id===id);
  const fix=(uid,staffId,date,mark,extra={})=>s=>execute(s,uid,{type:'HR_ATT_CORRECT',staffId,date,mark,reason:'Register checked (fictional)',...extra});
  const line=(s,ym,id)=>M.hrRunFor(s,ym).lines.find(l=>l.staffId===id);
  const sepDraft=()=>execute(seed(),'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});

  test('[PAY-WB DEC-075] the open pay month: from the month after the latest locked run (never earlier than last month) to yesterday',()=>{
    const s=seed();assert.deepEqual(M.hrCorrectionWindow(s),{from:'2026-09-01',to:'2026-10-23'});   // August locked
    const sep=execute(sepDraft(),'owner',{type:'HR_PAYROLL_LOCK',id:M.hrRunFor(sepDraft(),'2026-09').id});
    assert.deepEqual(M.hrCorrectionWindow(sep),{from:'2026-10-01',to:'2026-10-23'});   // September locked: October only
    assert.deepEqual(M.hrCorrectionWindow({...s,staffX:{...x(s),correctWindow:{from:'2026-10-01',to:'2026-10-23'}}}),{from:'2026-10-01',to:'2026-10-23'},'production: the server window');
    assert.deepEqual(M.hrParseEntry('A',30),'An absent day cannot carry overtime.');assert.deepEqual(M.hrParseEntry('CL',0),{kind:'leave',type:'CL'});
    assert.deepEqual(M.hrParseEntry('none',0),{kind:'none'});assert.equal(typeof M.hrParseEntry('p',0),'string');
  });

  test('[PAY-WB DEC-075] a correction that lowers pay applies at once (audited) and the September draft is recalculated; the lock then goes ahead',()=>{
    let s=sepDraft();assert.equal(line(s,'2026-09','HR-S02').earned,1680000);   // Aarav ₹18,000, payable 28 of 30 days
    // Thu 3 Sep marked present, register says absent: ₹18,000 × 54 / 60 = ₹16,200 → −₹600, applies at once.
    s=fix('manager','HR-S02','2026-09-03','A')(s);
    assert.equal(x(s).att['2026-09-03|HR-S02'].status,'A');assert.equal(x(s).att['2026-09-03|HR-S02'].by,'manager');
    const r=x(s).requests.at(-1);assert.deepEqual([r.status,r.direct,r.payEffect,r.before,r.after],['Approved',true,-60000,{kind:'mark',status:'P',otMin:0},{kind:'mark',status:'A',otMin:0}]);
    assert.deepEqual([line(s,'2026-09','HR-S02').payableDays,line(s,'2026-09','HR-S02').earned],[27,1620000]);
    assert.match(s.audit[0].detail,/Aarav Joshi · 2026-09-03 · Present → Absent · lowers gross pay by ₹600 · reason: Register checked \(fictional\) · September 2026 draft pay-run recalculated/);
    assert.equal(M.hrRunFor(s,'2026-09').preparedBy,'accountant','the preparer stays the maker');
    assert.throws(()=>execute(s,'accountant',{type:'HR_PAYROLL_LOCK',id:M.hrRunFor(s,'2026-09').id}),code('FORBIDDEN'));
    s=execute(s,'owner',{type:'HR_PAYROLL_LOCK',id:M.hrRunFor(s,'2026-09').id});assert.equal(M.hrRunFor(s,'2026-09').status,'Locked');
    assert.throws(()=>fix('manager','HR-S02','2026-09-04','A')(s),e=>e.code==='INVALID_TRANSITION'&&/locked/.test(e.message));
  });

  test('[PAY-WB DEC-075] a correction that raises pay waits for the Owner, blocks the lock, and the approval recalculates (Ravi by hand)',()=>{
    let s=sepDraft();const before=line(s,'2026-09','HR-S05');assert.deepEqual([before.payableDays,before.net],[28.5,1087662]);
    s=fix('manager','HR-S05','2026-09-22','P')(s);
    const r=x(s).requests.at(-1);assert.deepEqual([r.status,r.payEffect,!!r.direct],['Pending',45000,false]);   // 13,500 × 1 day = ₹450 more gross
    assert.equal(x(s).att['2026-09-22|HR-S05'].status,'A','nothing changes before the Owner decides');assert.deepEqual(line(s,'2026-09','HR-S05'),before);
    assert.match(s.audit[0].detail,/raises gross pay by ₹450 — waiting for the Owner/);
    assert.throws(()=>fix('manager','HR-S05','2026-09-22','L')(s),code('NOT_AVAILABLE'));
    assert.throws(()=>execute(s,'owner',{type:'HR_PAYROLL_LOCK',id:M.hrRunFor(s,'2026-09').id}),e=>e.code==='INVALID_TRANSITION'&&/1 attendance correction for September 2026 waits for the Owner/.test(e.message));
    for(const u of ['manager','accountant'])assert.throws(()=>execute(s,u,{type:'HR_REQUEST_DECIDE',id:r.id,approve:true}),code('FORBIDDEN'),u);
    assert.throws(()=>execute(s,'owner',{type:'HR_REQUEST_DECIDE',id:r.id,approve:false}),code('VALIDATION'));
    s=execute(s,'owner',{type:'HR_REQUEST_DECIDE',id:r.id,approve:true,note:'Checked with the supervisor'});
    assert.equal(x(s).att['2026-09-22|HR-S05'].status,'P');assert.equal(x(s).requests.at(-1).decidedBy,'owner');
    // ₹13,500: 29.5 days → earned 13,500 × 59 / 60 = ₹13,275.00; OT 120 min ₹112.50 → gross ₹13,387.50; PF 12 % of basic ₹6,637.50 = ₹796.50;
    // ESIC 0.75 % of (13,275 − washing 663.75) = ₹94.58; PT ₹200; advance ₹1,000 → net ₹11,296.42 (server PayrollCorrectionSqlTests: same).
    const a=line(s,'2026-09','HR-S05');
    assert.deepEqual([a.payableDays,a.earned,a.gross,a.pf,a.esic,a.pt,a.advance,a.net],[29.5,1327500,1338750,79650,9458,20000,100000,1129642]);
    s=execute(s,'owner',{type:'HR_PAYROLL_LOCK',id:M.hrRunFor(s,'2026-09').id});assert.equal(M.hrRunFor(s,'2026-09').status,'Locked');
    assert.throws(()=>execute(s,'owner',{type:'HR_REQUEST_DECIDE',id:r.id,approve:true}),code('INVALID_TRANSITION'));
  });

  test('[PAY-WB DEC-075] refused: locked month, future, today, before the open month, own record, no right, no reason, absent with overtime, no change, inside a multi-day leave',()=>{
    const s=seed();
    for(const [u,id,d,m,c,extra] of [['manager','HR-S02','2026-08-12','P','INVALID_TRANSITION'],['manager','HR-S02','2026-10-25','P','INVALID_TRANSITION'],
      ['manager','HR-S02','2026-10-24','P','INVALID_TRANSITION'],['manager','HR-S02','2026-07-15','P','INVALID_TRANSITION'],['manager','HR-S01','2026-09-03','A','FORBIDDEN'],
      ['accountant','HR-S02','2026-09-03','A','FORBIDDEN'],['housekeeper','HR-S02','2026-09-03','A','FORBIDDEN'],['manager','HR-S02','2026-09-03','A','VALIDATION',{reason:'x'}],
      ['manager','HR-S02','2026-09-03','A','VALIDATION',{otMin:30}],['manager','HR-S02','2026-09-03','P','VALIDATION'],['manager','HR-S05','2026-09-14','P','INVALID_TRANSITION']])
      assert.throws(()=>fix(u,id,d,m,extra)(s),code(c),`${u} ${id} ${d} ${m}`);
  });

  test('[PAY-WB DEC-075] back-dated leave: paid leave waits for the Owner, leave without pay applies, a one-day leave can be replaced, leave rules hold',()=>{
    let s=seed();
    s=fix('manager','HR-S06','2026-09-16','CL',{reason:'Leave form found (fictional)'})(s);   // absent → CL: +₹450 (13,500 × 2 / 60)
    const cl=x(s).requests.at(-1);assert.deepEqual([cl.status,cl.payEffect],['Pending',45000]);
    s=execute(s,'owner',{type:'HR_REQUEST_DECIDE',id:cl.id,approve:true});
    assert.equal(x(s).att['2026-09-16|HR-S06'],undefined,'the absent mark is replaced by the leave');
    const lv=x(s).leaves.find(l=>l.correctionId===cl.id);assert.deepEqual([lv.type,lv.status,lv.from,lv.to,lv.days,lv.requestedBy,lv.decidedBy],['CL','Approved','2026-09-16','2026-09-16',1,'manager','owner']);
    assert.equal(M.hrLeaveBalance(s,'HR-S06','2026-10-24').find(b=>b.type==='CL').left,9);   // 12 − 2 pending (28–29 Oct) − 1
    s=fix('manager','HR-S04','2026-09-28','LOP')(s);   // absent → leave without pay: no pay change → applies at once
    const lop=x(s).requests.at(-1);assert.deepEqual([lop.status,lop.direct,lop.payEffect],['Approved',true,0]);
    assert.equal(x(s).leaves.find(l=>l.correctionId===lop.id).decidedBy,'manager');
    s=fix('manager','HR-S04','2026-09-28','A')(s);   // back to a mark: the one-day leave is cancelled
    assert.equal(x(s).leaves.find(l=>l.correctionId===lop.id).status,'Cancelled');assert.equal(x(s).att['2026-09-28|HR-S04'].status,'A');
    assert.throws(()=>fix('manager','HR-S10','2026-09-14','CL')(s),code('VALIDATION'));   // daily wage: leave without pay only
    assert.throws(()=>fix('manager','HR-S06','2026-09-21','SL')(s),code('VALIDATION'));   // Monday = Kavya's weekly off
    assert.throws(()=>execute(s,'manager',{type:'HR_LEAVE_REQUEST',staffId:'HR-S06',leaveType:'CL',from:'2026-09-30',to:'2026-09-30',reason:'Back-dated'}),e=>e.code==='VALIDATION'&&/Past days/.test(e.message));
    const w=fix('manager','HR-S06','2026-09-30','OFF',{otMin:60})(s),wr=x(w).requests.at(-1);assert.equal(wr.status,'Pending');   // + overtime raises pay
    assert.throws(()=>execute(w,'owner',{type:'HR_REQUEST_WITHDRAW',id:wr.id}),code('FORBIDDEN'));
    assert.equal(x(execute(w,'manager',{type:'HR_REQUEST_WITHDRAW',id:wr.id})).requests.at(-1).status,'Withdrawn');
  });

  test('[PAY-WB DEC-083] paid holiday: paid like a weekly off for monthly staff, like weekly offs for daily wage (figures by hand); leave skips it; locked month keeps it',()=>{
    let s=seed();const sx=x(s);
    // Two fictional staff (weekly off Sunday): present on every September working day except Thu 17 Sep (6, 13, 20, 27 Sep are Sundays).
    for(const [id,payType,monthly,daily] of [['HR-T1','monthly',3000000,0],['HR-T2','daily',0,80000]]){
      sx.staff.push({id,name:id==='HR-T1'?'Holiday Monthly':'Holiday Daily',dept:'KIT',designation:'Test',payType,monthly,daily,defaultShift:'M',weeklyOff:0,joined:'2025-04-01',phone:'',active:true,gender:'M'});
      for(const d of M.hrMonthDates('2026-09'))if(M.hrWeekday(d)!==0&&d!=='2026-09-17')sx.att[d+'|'+id]={status:'P',at:d+'T07:00',otMin:0,by:'manager'};}
    for(const u of ['manager','accountant'])assert.throws(()=>execute(s,u,{type:'HR_HOLIDAY_SAVE',date:'2026-09-17',name:'Founders’ Day (fictional)'}),code('FORBIDDEN'),u);
    s=execute(s,'owner',{type:'HR_HOLIDAY_SAVE',date:'2026-09-17',name:'Founders’ Day (fictional)'});
    assert.match(s.audit[0].detail,/Paid holiday 2026-09-17 · Founders’ Day \(fictional\) added · paid like a weekly off/);
    const run=M.hrComputeRun(s,'2026-09'),m=run.lines.find(l=>l.staffId==='HR-T1'),d=run.lines.find(l=>l.staffId==='HR-T2');
    assert.ok(!run.warnings.some(w=>w.startsWith('Holiday')),'the holiday is not an unmarked day');
    // Monthly ₹30,000: 25 present + 4 weekly offs + 1 holiday = 30 / 30 → ₹30,000; PF 12 % of basic ₹15,000 = ₹1,800; ESIC none (₹30,000 > ₹21,000); PT ₹200 → net ₹28,000.
    assert.deepEqual([m.counts.P,m.counts.off,m.counts.hol,m.payableDays,m.earned,m.pf,m.esic,m.pt,m.net],[25,4,1,30,3000000,180000,0,20000,2800000]);
    // Daily ₹800: 25 days worked = ₹20,000 (the holiday, like a weekly off, unpaid — CA A04-04 open); ESIC 0.75 % = ₹150; PT ₹200 → net ₹19,650.
    assert.deepEqual([d.counts.hol,d.payableDays,d.earned,d.esic,d.pt,d.net],[1,25,2000000,15000,20000,1965000]);
    assert.equal(M.hrDayStatus(s,st(s,'HR-T1'),'2026-09-17').kind,'holiday');
    const off=execute(s,'owner',{type:'HR_HOLIDAY_REMOVE',date:'2026-09-17'}),noHol=M.hrComputeRun(off,'2026-09');
    assert.equal(noHol.lines.find(l=>l.staffId==='HR-T1').earned,2900000);   // unmarked = absent: 30,000 × 58 / 60
    assert.ok(noHol.warnings.includes(`Holiday Monthly: 1 unmarked day counted as absent — ${M.HR_UNMARKED_HINT}`));
    assert.equal(M.hrComputeRun(seed(),'2026-09').lines.every(l=>l.counts.hol===undefined),true,'no holiday: counts keep their old shape');
    // Leave skips a holiday: Ravi CL Tue 3 – Thu 5 Nov with a holiday on Wed 4 Nov = 2 days.
    s=execute(s,'owner',{type:'HR_HOLIDAY_SAVE',date:'2026-11-04',name:'Test holiday (fictional)'});
    assert.equal(M.hrLeaveDays(s,st(s,'HR-S05'),'2026-11-03','2026-11-05'),2);
    // Masters integration: the paid holidays are MA-6's Holiday calendar, whose demo fixture seeds an illustrative calendar (6 paid in FY 2026–27),
    // so the count is the seeded ones + the 2 added here (old: "2 in FY 2026–27" and "None yet" for the plain fixture).
    const seededPaid=M.hrPaidHolidays(seed()).filter(h=>h.date>='2026-04-01'&&h.date<='2027-03-31').length;
    assert.equal(M.hrSettingValue(s,'hr.leave.holidays'),`${seededPaid+2} in FY 2026–27`);
    assert.equal(M.hrSettingValue(seed(),'hr.leave.holidays'),seededPaid?`${seededPaid} in FY 2026–27`:'None yet — the Owner adds them');
    // Locked month: August's holidays cannot change.
    assert.throws(()=>execute(s,'owner',{type:'HR_HOLIDAY_SAVE',date:'2026-08-15',name:'Test (fictional)'}),code('INVALID_TRANSITION'));
    // Masters integration: 2 Oct is a seeded paid holiday of MA-6's demo calendar, so a date without one stands in (old: 2026-10-02).
    assert.throws(()=>execute(s,'owner',{type:'HR_HOLIDAY_REMOVE',date:'2026-10-03'}),code('NOT_FOUND'));
  });

  test('[PAY-WB DEC-084] bank change waits for the Owner; the bank file uses the old details meanwhile; the Owner’s own change applies at once',()=>{
    let s=sepDraft();const old=st(s,'HR-S05').accountNo,ravi=st(s,'HR-S05');
    const save=(u,staff,extra)=>execute(s,u,{type:'HR_STAFF_SAVE',id:staff.id,name:staff.name,dept:staff.dept,designation:staff.designation,defaultShift:staff.defaultShift,
      weeklyOff:staff.weeklyOff,phone:staff.phone,...(staff.userId?{userId:staff.userId}:{}),...extra});
    s=save('accountant',ravi,{accountNo:'999999999999',ifsc:'DEMO0000999',bankReason:'New account (fictional)'});
    assert.equal(st(s,'HR-S05').accountNo,old,'not applied');
    const r=M.hrPendingRequests(s,'bank')[0];assert.deepEqual([r.staffId,r.bankBefore.accountNo,r.bankAfter.accountNo,r.bankAfter.ifsc,r.reason],['HR-S05',old,'999999999999','DEMO0000999','New account (fictional)']);
    assert.match(s.audit[0].detail,new RegExp('••••'+old.slice(-4)+'.*→.*••••9999.*sent to the Owner'));assert.ok(!s.audit[0].detail.includes('999999999999'));
    assert.throws(()=>save('accountant',st(s,'HR-S05'),{accountNo:'888888888888'}),code('INVALID_TRANSITION'));   // one pending per staff member
    s=execute(s,'owner',{type:'HR_PAYROLL_LOCK',id:M.hrRunFor(s,'2026-09').id});
    assert.ok(M.hrBankCsv(s,M.hrRunFor(s,'2026-09')).includes("'"+old),'the locked run pays the old account');
    assert.throws(()=>execute(s,'accountant',{type:'HR_REQUEST_DECIDE',id:r.id,approve:true}),code('FORBIDDEN'));
    s=execute(s,'owner',{type:'HR_REQUEST_DECIDE',id:r.id,approve:true});
    assert.deepEqual([st(s,'HR-S05').accountNo,st(s,'HR-S05').ifsc],['999999999999','DEMO0000999']);
    assert.ok(M.hrBankCsv(s,M.hrRunFor(s,'2026-09')).includes("'"+old),'the locked run keeps its frozen details');
    // Withdrawn by the requester; rejected by the Owner with a reason.
    s=save('accountant',st(s,'HR-S05'),{accountNo:'777777777777'});const w=M.hrPendingRequests(s,'bank')[0];
    s=execute(s,'accountant',{type:'HR_REQUEST_WITHDRAW',id:w.id});assert.equal(x(s).requests.find(q=>q.id===w.id).status,'Withdrawn');
    s=save('accountant',st(s,'HR-S05'),{accountNo:'666666666666'});const j=M.hrPendingRequests(s,'bank')[0];
    assert.throws(()=>execute(s,'owner',{type:'HR_REQUEST_DECIDE',id:j.id,approve:false}),code('VALIDATION'));
    s=execute(s,'owner',{type:'HR_REQUEST_DECIDE',id:j.id,approve:false,note:'Not the staff member’s account'});assert.equal(st(s,'HR-S05').accountNo,'999999999999');
    // The Owner's own change applies at once (audited, masked); a new record's details from the Accountant wait.
    s=save('owner',st(s,'HR-S09'),{accountNo:'123412341234'});assert.equal(st(s,'HR-S09').accountNo,'123412341234');assert.match(s.audit[0].detail,/••••1234 .*\(Owner;/);
    assert.equal(M.hrPendingRequests(s,'bank').length,0);
    s=execute(s,'accountant',{type:'HR_STAFF_SAVE',name:'New Cook',dept:'KIT',designation:'Cook',defaultShift:'M',weeklyOff:2,phone:'',payType:'monthly',monthly:1500000,
      bankName:'Demo Bank (fictional)',accountNo:'555566667777',ifsc:'DEMO0000102'});
    const nc=x(s).staff.at(-1);assert.equal(nc.accountNo,undefined);assert.equal(M.hrPendingRequests(s,'bank')[0].staffId,nc.id);
  });

  test('[PAY-WB UI] past days, requests, holidays and the correction / decision / holiday dialogs render on desktop and phone; payroll points to the correction',()=>{
    let s=sepDraft();s=fix('manager','HR-S05','2026-09-22','P')(s);
    s=execute(s,'accountant',{type:'HR_STAFF_SAVE',id:'HR-S05',name:'Ravi Shinde',dept:'HK',designation:'Room attendant',defaultShift:'M',weeklyOff:5,phone:st(s,'HR-S05').phone,userId:'housekeeper',accountNo:'999999999999'});
    s=execute(s,'owner',{type:'HR_HOLIDAY_SAVE',date:'2026-10-02',name:'Founders’ Day (fictional)'});
    const req=M.hrPendingRequests(s,'attendance')[0];
    for(const phone of [false,true]){
      const own=UI.section(s,'owner','PastDays',{phone}),mgr=UI.section(s,'manager','PastDays',{phone});
      assert.match(own,/Open pay month: 1 Sept? 2026/);assert.match(own,/Approve/);assert.match(own,/\+₹450 gross pay/);
      assert.match(mgr,/Withdraw/);assert.doesNotMatch(mgr,/₹450/,'the Manager sees no money');assert.doesNotMatch(mgr+own,/NaN|undefined/);
      assert.equal(/<table/.test(own),!phone,'phone: cards, not tables');
      const hol=UI.section(s,'owner','Holidays',{phone});assert.match(hol,/Founders’ Day \(fictional\)/);assert.match(hol,/Add holiday/);
      assert.doesNotMatch(UI.section(s,'manager','Holidays',{phone}),/Add holiday/);
      const bank=UI.section(s,'owner','People',{phone});assert.match(bank,/••••9999/);assert.doesNotMatch(bank,/999999999999/);
    }
    assert.match(UI.modal(s,'manager','hr-att-correct','HR-S06','2026-09-16'),/sent to the Owner for approval/);   // Kavya absent → present raises pay
    assert.doesNotMatch(UI.modal(s,'manager','hr-att-correct','HR-S06','2026-09-16'),/₹450/);
    assert.match(UI.modal(s,'owner','hr-att-correct','HR-S06','2026-09-16'),/as the Owner your correction applies now/);
    assert.match(UI.modal(s,'manager','hr-att-correct','HR-S05','2026-08-10'),/Open pay month/);   // outside the window: opens on the last open day
    assert.match(UI.modal(s,'owner','hr-request-decide',req.id,'approve'),/raises gross pay by ₹450/);assert.match(UI.modal(s,'owner','hr-request-decide',req.id,'reject'),/Reason for rejecting/);
    assert.match(UI.modal(s,'owner','hr-holiday'),/Add paid holiday/);assert.match(UI.modal(s,'owner','hr-holiday','2026-10-02'),/Remove/);
    assert.match(UI.modal(s,'accountant','hr-staff','HR-S05'),/waiting for the Owner/);assert.match(UI.modal(seed(),'accountant','hr-staff','HR-S05'),/goes to the Owner for approval/);
    // Payroll: the pending correction blocks the lock; an unmarked day offers the correction.
    const pay=UI.section(s,'owner','Payroll');assert.match(pay,/1 attendance correction for September 2026 waits for the Owner/);
    let u=seed();delete x(u).att['2026-09-21|HR-S02'];u=execute(u,'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});
    assert.ok(M.hrRunFor(u,'2026-09').warnings.some(w=>w.startsWith('Aarav Joshi: 1 unmarked day counted as absent — a manager can correct them')));
    assert.match(UI.section(u,'owner','Payroll'),/Correct unmarked days/);assert.doesNotMatch(UI.section(u,'accountant','Payroll'),/Correct unmarked days/);
    // Payslip: paid holidays are listed when there are any.
    let h=execute(seed(),'owner',{type:'HR_HOLIDAY_SAVE',date:'2026-09-17',name:'Test (fictional)'});delete x(h).att['2026-09-17|HR-S02'];
    h=execute(h,'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});assert.match(UI.modal(h,'accountant','hr-payslip',M.hrRunFor(h,'2026-09').id+'|HR-S02'),/Paid holidays/);
  });
}
