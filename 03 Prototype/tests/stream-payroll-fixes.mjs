// W05-B code-review fixes (05 Delivery Evidence\Assembly\W05B_Payroll_Code_Review_2026-10-25.md). One regression test per finding, names [PAY-FIX F-nn].
// Money = integer paise; simulated clock only; fictional staff.
import {build,buildSync} from 'esbuild';
import {writeFile} from 'node:fs/promises';
import {readFileSync} from 'node:fs';
import vm from 'node:vm';
export async function register({test,M,assert}){
  const {seed,execute}=M;
  // Render harness (payslip dialog, HR sections) — same pattern as stream-payroll-scc.mjs, own bundle.
  await writeFile('tests/.generated/payfix-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can} from '../../src/services/mock';
import {HR_SECTIONS,HrDesk} from '../../src/features/staff/HrDesk';
import {registration} from '../../src/features/staff/register';
function ctx(s:any,uid:string){const user=s.users.find((u:any)=>u.id===uid);
  return {s,uid,user,busy:false,run:async()=>true,open(){},closeModal(){},toast(){},setDirty(){},permission:(p:any)=>can(s,uid,p),display:{isMobile:false}} as any;}
const wrap=(s:any,uid:string,el:any)=>renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid)}>{el}</HccContext.Provider>);
export function renderSection(s:any,uid:string,name:string,phone=false){const C=(HR_SECTIONS as any)[name];return wrap(s,uid,<C phone={phone}/>);}
export function renderDesk(s:any,uid:string,phone=false){return wrap(s,uid,<HrDesk phone={phone}/>);}
export function renderModal(s:any,uid:string,type:string,id?:string){const C=(registration.modals as any)[type];return wrap(s,uid,<C id={id}/>);}
`);
  await build({entryPoints:['tests/.generated/payfix-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/payfix-ui.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __pfReq} from 'node:module';const require=__pfReq(import.meta.url);"}});
  const UI=await import('./.generated/payfix-ui.mjs');
  const x=s=>M.hrSlice(s);
  const code=c=>e=>e.code===c;

  // SCC leave port (DEC-116): leave is one record per date outside the HOTEL's week-off set (none by default) — a staff member's roster off inside
  // the dates is a leave day too, and payroll counts every approved record by its date (F-01's span rule gave way; old spans were split on restore).
  test('[PAY-FIX F-01 → SCC] LOP over a roster weekly off: one record per date; payroll counts the approved records (lopLeave = the records, absent in the SCC sheet)',()=>{
    let s=seed();// Aarav (HR-S02), ₹18,000/month, weekly off Tuesday; LOP Mon 9 – Wed 11 Nov 2026
    s=execute(s,'reception',{type:'HR_LEAVE_REQUEST',approver:'manager',staffId:'HR-S02',leaveType:'LOP',from:'2026-11-09',to:'2026-11-11',reason:'Personal'});
    const lv=x(s).leaves.filter(l=>l.staffId==='HR-S02'&&l.status==='Pending');assert.deepEqual(lv.map(l=>[l.date,l.days]),[['2026-11-09',1],['2026-11-10',1],['2026-11-11',1]]);
    for(const l of lv)s=execute(s,'manager',{type:'HR_LEAVE_DECIDE',id:l.id,approve:true});
    const st=x(s).staff.find(t=>t.id==='HR-S02'),c=M.hrCounts(s,st,'2026-11');
    assert.equal(c.lopLeave,3);
    // DEC-115 (SCC sheet): leave without pay is not "Leaves" — its days fall into absent with every other day not worked or on paid leave.
    const d=M.hrSccDays(c,M.hrRatesFor(s,'2026-11'));assert.equal(d.leave,c.paidLeave);assert.equal(d.absent,d.total-d.present-d.leave-d.half);
    assert.equal(M.hrDayStatus(s,st,'2026-11-10').kind,'leave');assert.equal(M.hrDayStatus(s,st,'2026-11-17').kind,'off');
    // paid leave over a roster off day: HR-LV-2 = EL Mon 2 + Tue 3 Nov (Tuesday off), two approved records → 2 paid leave days
    assert.deepEqual(x(s).leaves.filter(l=>l.id.startsWith('HR-LV-2')).map(l=>[l.date,l.days]),[['2026-11-02',1],['2026-11-03',1]]);assert.equal(c.paidLeave,2);
  });
  const staffCmd=(st,extra={})=>({type:'HR_STAFF_SAVE',id:st.id,name:st.name,dept:st.dept,designation:st.designation,defaultShift:st.defaultShift,weeklyOff:st.weeklyOff,phone:st.phone,...(st.userId?{userId:st.userId}:{}),...(st.gender?{gender:st.gender}:{}),...extra});
  const S=(s,id)=>x(s).staff.find(t=>t.id===id);

  test('[PAY-FIX F-02] bank file uses the bank details frozen at lock; later edits apply to future runs; audit shows masked old → new',()=>{
    let s=execute(seed(),'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});const id=M.hrRunFor(s,'2026-09').id;
    s=execute(s,'owner',{type:'HR_PAYROLL_LOCK',id});
    const old=S(s,'HR-S05').accountNo;assert.equal(M.hrRunFor(s,'2026-09').bank['HR-S05'].accountNo,old);
    s=execute(s,'accountant',staffCmd(S(s,'HR-S05'),{accountNo:'999999999999',ifsc:'EVIL0000001'}));
    // DEC-084: the Accountant's change waits for the Owner; once approved the staff master changes (future runs).
    assert.equal(S(s,'HR-S05').accountNo,old,'not applied before the Owner approves');
    s=execute(s,'owner',{type:'HR_REQUEST_DECIDE',id:M.hrPendingRequests(s,'bank')[0].id,approve:true});
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
    assert.match(UI.renderModal(s,'manager','hr-staff','HR-S05'),/Owner only/);
  });

  test('[PAY-FIX F-04] a v7 save with old `rates` is migrated on restore: custom rates kept, old runs readable, no NaN / undefined in files or payslips',()=>{
    const v7=structuredClone(seed());const sx=v7.staffX;
    delete sx.rateVersions;sx.rates={basicBps:5000,pfBps:1000,pfCap:1500000,esicBps:75,esicCeiling:2100000,pt:20000,ptThreshold:1000000,otBps:15000,lateBlock:2};
    for(const st of sx.staff){delete st.gender;delete st.bankName;delete st.accountNo;delete st.ifsc;}
    const run=sx.runs[0];delete run.bank;run.rates={...sx.rates};
    run.lines=run.lines.map(l=>{const {staffId,name,dept,designation,payType,base,latePenaltyHalf,payableDays,ot,gross,pf,esic,pt,advance,deductions,net}=l;
      const {ym,HL,...counts}=l.counts;return {staffId,name,dept,designation,payType,base,counts,latePenaltyHalf,payableDays,basic:gross-ot,ot,gross,pf,esic,pt,advance,deductions,net};});
    v7.schema=7;
    const r=M.restoreState(JSON.stringify(v7));assert.equal(r.reset,false);assert.equal(r.state.schema,M.SCHEMA);
    const s=r.state,v=M.hrRateVersions(s);
    assert.equal(s.staffX.rates,undefined);assert.equal(v.length,1);assert.equal(v[0].from,'2025-04-01');
    assert.equal(v[0].pfEeBps,1000,'custom PF rate kept (was silently reset to 12%)');assert.equal(v[0].lateBlock,2,'custom late rule kept');
    assert.equal(v[0].otBps,10000,'old default (1.5×) is not a customisation: SCC default applies');assert.equal(v[0].pfErBps,1300);
    const old=M.hrRunFor(s,'2026-08');assert.equal(old.legacy,true);assert.equal(typeof old.rates.from,'string');
    const t=M.hrRunTotals(old);for(const [k,val] of Object.entries(t))assert.ok(Number.isFinite(val),k);
    for(const csv of [M.hrStatutoryCsv(s,old),M.hrRegisterCsv(s,old),M.hrBankCsv(s,old)])assert.doesNotMatch(csv,/NaN|undefined/);
    assert.match(M.hrStatutoryCsv(s,old),/legacy run/);
    const slip=UI.renderModal(s,'accountant','hr-payslip',old.id+'|HR-S05');assert.doesNotMatch(slip,/NaN|undefined/);assert.match(slip,/Rates from 2025-04-01/);
    assert.doesNotMatch(UI.renderSection(s,'accountant','Payroll'),/NaN|undefined/);
    // the migrated state computes new runs with the kept custom rate, and old net figures are unchanged
    const n=execute(s,'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});assert.equal(M.hrRunFor(n,'2026-09').rates.pfEeBps,1000);
    assert.deepEqual(old.lines.map(l=>l.net),seed().staffX.runs[0].lines.map(l=>l.net));
    // idempotent: a current-shape save round-trips unchanged; older schemas still reset
    const cur=seed();assert.deepEqual(M.restoreState(JSON.stringify(cur)).state,cur);
    assert.equal(M.restoreState(JSON.stringify({...v7,schema:6})).reset,true);
  });

  test('[PAY-FIX F-05] a dated rate version starts from the version in force on its date; later versions never silently revert a change',()=>{
    // reviewer's t3: Jan PF 10%, then Apr changes only ESIC → April keeps PF 10%
    let s=execute(seed(),'owner',{type:'HR_STAT_RATES_SET',from:'2027-01-01',pfEeBps:1000});
    s=execute(s,'owner',{type:'HR_STAT_RATES_SET',from:'2027-04-01',esicEeBps:100});
    assert.equal(M.hrRatesFor(s,'2027-04').pfEeBps,1000);assert.equal(M.hrRatesFor(s,'2027-04').esicEeBps,100);
    assert.equal(M.hrRatesFor(s,'2026-12').pfEeBps,1200);
    // reverse order: a later version already exists; an earlier change is carried into it (it had inherited the old value)
    let r=execute(seed(),'owner',{type:'HR_STAT_RATES_SET',from:'2027-04-01',esicEeBps:100});
    r=execute(r,'owner',{type:'HR_STAT_RATES_SET',from:'2027-01-01',pfEeBps:1000});
    assert.equal(M.hrRatesFor(r,'2027-04').pfEeBps,1000,'PF change does not stop after March');assert.equal(M.hrRatesFor(r,'2027-04').esicEeBps,100);
    assert.equal(M.hrRatesFor(r,'2027-01').esicEeBps,75,'the earlier version does not pick up the later ESIC change');
    assert.match(r.audit[0].detail,/carried forward \(pfEeBps → 2027-04-01\)/);
    // a later version that set its own value keeps it
    let k=execute(seed(),'owner',{type:'HR_STAT_RATES_SET',from:'2027-04-01',pfEeBps:1100});
    k=execute(k,'owner',{type:'HR_STAT_RATES_SET',from:'2027-01-01',pfEeBps:1000});
    assert.equal(M.hrRatesFor(k,'2027-04').pfEeBps,1100);assert.equal(M.hrRatesFor(k,'2027-02').pfEeBps,1000);assert.match(k.audit[0].detail,/2027-04-01 keeps its own pfEeBps/);
    // the dialog sends only changed fields: it renders from the version in force on its first month
    assert.match(UI.renderModal(s,'owner','hr-rates'),/only the fields you change are saved/);
  });

  test('[PAY-FIX F-06] hub and default rate edits date from the month after the last completed pay period; a completed unlocked month changes only when chosen, and says so',()=>{
    const s=seed();// business date 24 Oct 2026, last locked August, September complete but not locked
    assert.equal(M.hrRatesDefaultFrom(s),'2026-10-01');
    const b=execute(s,'owner',{type:'HR_STAT_RATES_SET',pfEeBps:1100});// reviewer's t3 (hub-style edit, no from)
    assert.equal(M.hrRatesFor(b,'2026-09').pfEeBps,1200,'completed September unchanged');assert.equal(M.hrRatesFor(b,'2026-10').pfEeBps,1100);
    // the hub wrapper sends the date explicitly and shows it before saving
    const def=M.cfgDef(s,'hr.pf.pfEeBps');assert.equal(def.store.command(s,1100).from,'2026-10-01');
    assert.ok(def.impact(s,1100).some(l=>/Effective from 1 October 2026/.test(l)));
    const h=execute(s,'owner',{type:'SETTING_SET',setting:'hr.pf.pfEeBps',value:1100});assert.equal(M.hrRatesFor(h,'2026-09').pfEeBps,1200);assert.equal(M.hrRatesFor(h,'2026-10').pfEeBps,1100);
    // explicitly choosing September is allowed but the audit says the completed month changes
    const sep=execute(s,'owner',{type:'HR_STAT_RATES_SET',from:'2026-09-01',pfEeBps:1100});assert.match(sep.audit[0].detail,/completed month not yet locked: September 2026/);
    assert.match(UI.renderSection(s,'owner','Rates'),/applies from 1 October 2026/);
    // FY26 (September locked): the default is October as before
    assert.equal(M.hrRatesDefaultFrom(M.loadScenario('FY26')),'2026-10-01');
  });

  test('[PAY-FIX F-07] ESIC contribution-period rule (optional since DEC-115; SCC month by month is the default): ₹22,000 with 7 days absent stays uncovered; covered at period start stays covered after a raise',()=>{
    assert.equal(M.hrDefaultRates().esicPeriod,false,'DEC-115: SCC month by month');const R={...M.hrDefaultRates(),esicPeriod:true};
    const full=(ym,days,extra={})=>({ym,days,P:days,L:0,H:0,HL:0,A:0,OFFW:0,off:0,paidLeave:0,lopLeave:0,unmarked:0,na:0,otMin:0,...extra});
    const mon=(monthly,extra={})=>({id:'T',name:'T',dept:'FO',designation:'x',payType:'monthly',monthly,daily:0,...extra});
    // ₹22,000, 7 days absent → SCC: deduction 7, no extra days → 23 salary days → earned 22,000 × 23 / 30 = ₹16,866.67; the period rule keeps it
    // uncovered (contract ₹22,000 > ₹21,000); month by month charges ESIC on 16,866.67 − washing 843.33 = 16,023.34 → ₹120.18 / ₹520.76
    const lop=M.hrComputeLine(mon(2200000),full('2026-09',30,{P:23,A:7}),R);
    assert.equal(lop.earned,1686667);assert.equal(lop.esic,0);assert.equal(lop.esicEr,0);assert.equal(lop.esicCovered,false);
    const scc=M.hrComputeLine(mon(2200000),full('2026-09',30,{P:23,A:7}),{...R,esicPeriod:false});assert.equal(scc.esicWages,1602334);assert.equal(scc.esic,12018);assert.equal(scc.esicEr,52076);
    // covered at the start of Oct–Mar (₹20,000), raised to ₹23,000 in December: still covered until March, not from April
    let s=execute(seed(),'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});s=execute(s,'owner',{type:'HR_PAYROLL_LOCK',id:M.hrRunFor(s,'2026-09').id});
    const st={...x(s).staff.find(t=>t.id==='HR-S02'),monthly:2000000};
    const oct=structuredClone(s);oct.staffX.runs.push({id:'T-OCT',month:'2026-10',status:'Locked',rates:R,lines:[M.hrComputeLine(st,full('2026-10',31),R,undefined,M.hrEsicCovered(s,st,'2026-10',R))],warnings:[],preparedBy:'accountant',preparedAt:'x'});
    assert.equal(oct.staffX.runs.at(-1).lines[0].esicCovered,true);
    const raised={...st,monthly:2300000};
    assert.equal(M.hrEsicCovered(oct,raised,'2026-12',R),true);assert.equal(M.hrEsicCovered(oct,raised,'2027-03',R),true);assert.equal(M.hrEsicCovered(oct,raised,'2027-04',R),false);
    const dec=M.hrComputeLine(raised,full('2026-12',31),R,undefined,M.hrEsicCovered(oct,raised,'2026-12',R));assert.ok(dec.esic>0,'contributes to the period end above the ceiling');
    assert.deepEqual([M.hrEsicPeriodStart('2026-04'),M.hrEsicPeriodStart('2026-09'),M.hrEsicPeriodStart('2026-10'),M.hrEsicPeriodStart('2027-03')],['2026-04','2026-04','2026-10','2026-10']);
    // dated setting, Owner-only, shown with the CA flag; versions without the field keep the SCC rule (locked snapshots recompute unchanged)
    const v=execute(seed(),'owner',{type:'HR_STAT_RATES_SET',from:'2026-11-01',esicPeriod:true});assert.equal(M.hrRatesFor(v,'2026-11').esicPeriod,true);assert.equal(M.hrRatesFor(v,'2026-10').esicPeriod,false);
    assert.throws(()=>execute(seed(),'accountant',{type:'HR_STAT_RATES_SET',from:'2026-11-01',esicPeriod:true}),code('FORBIDDEN'));
    assert.match(M.hrSettingValue(seed(),'hr.esic'),/month by month/);assert.match(M.hrSettingValue(v,'hr.esic','2026-11'),/contribution period/);assert.ok(M.cfgDef(seed(),'hr.esic.esicPeriod').illustrative);
  });

  test('[PAY-FIX F-08 → DEC-115] the SCC pay-day rule (extra days, OT days) is stated on the policy and rates screens; nothing says "not ported"',()=>{
    const s=seed();assert.match(M.HR_SCC_DAYS,/4 extra days/);assert.match(M.HR_SCC_DAYS,/OT days/);
    for(const phone of [false,true])for(const html of [UI.renderSection(s,'manager','LeavePolicy',phone),UI.renderSection(s,'owner','Rates',phone)]){assert.match(html,/4 extra days/);assert.doesNotMatch(html,/[Nn]ot ported/);}
  });

  test('[PAY-FIX F-09] unlock supersedes "sent" payslip marks and records an issued bank file (confirm to unlock; warn and confirm on re-generate)',()=>{
    let s=execute(seed(),'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});const id=M.hrRunFor(s,'2026-09').id;
    s=execute(s,'owner',{type:'HR_PAYROLL_LOCK',id});
    s=execute(s,'accountant',{type:'HR_PAYSLIP_SEND',runId:id,staffId:'HR-S05'});
    s=execute(s,'accountant',{type:'HR_PAYROLL_BANK_FILE',id});assert.equal(M.hrRunFor(s,'2026-09').bankFiles.length,1);
    assert.throws(()=>execute(s,'accountant',{type:'HR_PAYROLL_BANK_FILE',id}),code('INVALID_TRANSITION'),'second full file needs confirmation');
    // unlock: refused without confirming the issued bank file
    assert.throws(()=>execute(s,'owner',{type:'HR_PAYROLL_UNLOCK',id,reason:'Attendance correction'}),e=>e.code==='INVALID_TRANSITION'&&/pay(ing)? twice/.test(e.message));
    let u=execute(s,'owner',{type:'HR_PAYROLL_UNLOCK',id,reason:'Attendance correction',confirmBankFile:true});
    let r=M.hrRunFor(u,'2026-09');assert.equal(r.sent,undefined,'sent marks cleared');assert.equal(r.unlocks[0].sent[0].staffId,'HR-S05');assert.equal(r.unlocks[0].bankFiles.length,1);
    assert.equal(r.unlocks[0].nets['HR-S05'],r.lines.find(l=>l.staffId==='HR-S05').net);
    assert.match(u.audit[0].detail,/marked superseded/);
    // correct Ravi's attendance (an absent day), recalculate and relock: his payslip is flagged for a re-send
    const key=Object.keys(u.staffX.att).find(k=>k.startsWith('2026-09')&&k.endsWith('|HR-S05')&&u.staffX.att[k].status==='P');
    u=structuredClone(u);u.staffX.att[key].status='A';
    u=execute(u,'owner',{type:'HR_PAYROLL_RUN',month:'2026-09'});u=execute(u,'accountant',{type:'HR_PAYROLL_LOCK',id});
    r=M.hrRunFor(u,'2026-09');assert.equal(M.hrPayslipMark(r,'HR-S05'),'resend');assert.equal(M.hrPayslipMark(r,'HR-S02'),'');
    assert.equal(M.hrBankFilesIssued(r).length,1,'the issued file survives the unlock');
    // the next bank file after the relock is a recorded reissue
    assert.throws(()=>execute(u,'accountant',{type:'HR_PAYROLL_BANK_FILE',id}),code('INVALID_TRANSITION'));
    const again=execute(u,'accountant',{type:'HR_PAYROLL_BANK_FILE',id,confirmReissue:true});assert.match(again.audit[0].detail,/REISSUE/);
    assert.match(UI.renderModal(s,'owner','hr-unlock',id),/already generated/);
  });

  test('[PAY-FIX F-10] no self-approval of money: own salary, own advance and own attendance/overtime are refused (FORBIDDEN)',()=>{
    const s=seed(),self=e=>e.code==='FORBIDDEN'&&/Self-approval/.test(e.message);
    // reviewer's t4: Accountant sets own salary to ₹90,000 and gives self a ₹50,000 advance; Manager records own attendance with 480 OT minutes
    assert.throws(()=>execute(s,'accountant',staffCmd(S(s,'HR-S07'),{payType:'monthly',monthly:9000000})),self);
    assert.throws(()=>execute(s,'accountant',staffCmd(S(s,'HR-S07'),{esicApplicable:false})),self);
    assert.throws(()=>execute(s,'accountant',{type:'HR_ADVANCE_ADD',staffId:'HR-S07',amount:5000000,installment:0,reason:'Self'}),self);
    assert.throws(()=>execute(s,'manager',{type:'HR_ATT_MARK',staffId:'HR-S01',status:'P',otMin:480}),self);
    // the other person can: Owner changes the Accountant's salary and gives the advance; Supervisor-free path: another manager-level user (Owner) records the Manager's attendance
    assert.equal(S(execute(s,'owner',staffCmd(S(s,'HR-S07'),{payType:'monthly',monthly:2900000})),'HR-S07').monthly,2900000);
    assert.ok(execute(s,'owner',{type:'HR_ADVANCE_ADD',staffId:'HR-S07',amount:500000,installment:100000,reason:'Medical (fictional)'}));
    assert.equal(x(execute(s,'owner',{type:'HR_ATT_MARK',staffId:'HR-S01',status:'P',otMin:60})).att[s.date+'|HR-S01'].otMin,60);
    // unchanged salary on your own record (editing your phone etc.) is not a salary change
    assert.ok(execute(s,'accountant',staffCmd(S(s,'HR-S07'),{payType:'monthly',monthly:S(s,'HR-S07').monthly})));
    assert.doesNotMatch(UI.renderModal(s,'accountant','hr-advance'),/Farhan/);
  });

  test('[PAY-FIX F-11] PT slab (gender) and payslip WhatsApp number changes need payroll permission (Owner / Accountant), audited',()=>{
    const s=seed();// reviewer's t4: Manager moves Ravi to the women's slab and changes his mobile
    assert.throws(()=>execute(s,'manager',staffCmd(S(s,'HR-S05'),{gender:'F'})),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'manager',staffCmd(S(s,'HR-S05'),{phone:'98888 77777'})),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'supervisor',staffCmd(S(s,'HR-S05'),{phone:'98888 77777'})),code('FORBIDDEN'));
    // omitting the phone keeps it (it used to blank the payslip number)
    const k=staffCmd(S(s,'HR-S05'),{designation:'Senior room attendant'});delete k.phone;assert.equal(S(execute(s,'manager',k),'HR-S05').phone,S(s,'HR-S05').phone);
    const a=execute(s,'accountant',staffCmd(S(s,'HR-S05'),{gender:'F',phone:'98888 77777'}));
    assert.equal(S(a,'HR-S05').gender,'F');assert.match(a.audit[0].detail,/PT slab men → women/);assert.match(a.audit[0].detail,/payslip mobile ••••0029 → ••••7777/);
    assert.equal(execute(a,'accountant',{type:'HR_PAYSLIP_SEND',runId:'HR-PAY-1',staffId:'HR-S05'}).staffX.runs[0].sent[0].to,'919888877777');
    // the send button names the destination; managers see the fields as payroll-only
    assert.match(UI.renderModal(s,'accountant','hr-payslip','HR-PAY-1|HR-S05'),/Send on WhatsApp to \+919000010029/);
    assert.match(UI.renderModal(s,'manager','hr-staff','HR-S05'),/payroll only/);
  });

  test('[PAY-FIX F-12] full and final is maker-checker (never the maker) with a ₹2,00,000 adjustment cap and Owner approval above ₹1,00,000 additions',()=>{
    const s=execute(seed(),'manager',{type:'HR_STAFF_ACTIVE',id:'HR-S05',active:false});
    // reviewer's t4: 4 × ₹10,00,000 additions by the Accountant alone → now refused by the cap
    const big=[1,2,3,4].map(i=>({label:'Bonus '+i,kind:'add',amount:100000000}));
    assert.throws(()=>execute(s,'accountant',{type:'HR_FNF_SETTLE',staffId:'HR-S05',adjustments:big}),code('VALIDATION'));
    const d=execute(s,'accountant',{type:'HR_FNF_SETTLE',staffId:'HR-S05',adjustments:[{label:'EL encashment',kind:'add',amount:600000}]});
    const f=x(d).fnf[0];assert.equal(f.status,'Draft');assert.equal(x(d).advances[0].recovered,100000,'no write-off before confirmation');
    assert.equal(M.hrComputeRun(d,'2026-10').lines.some(l=>l.staffId==='HR-S05'),true,'a draft does not remove the staff member from payroll');
    assert.throws(()=>execute(d,'accountant',{type:'HR_FNF_CONFIRM',id:f.id}),code('FORBIDDEN'),'maker cannot confirm');
    assert.throws(()=>execute(d,'manager',{type:'HR_FNF_CONFIRM',id:f.id}),code('FORBIDDEN'));
    assert.throws(()=>execute(d,'accountant',{type:'HR_FNF_SETTLE',staffId:'HR-S05'}),code('INVALID_TRANSITION'),'one draft at a time');
    // stale: an advance recovered elsewhere changes the figures → re-prepare
    const st=structuredClone(d);st.staffX.advances[0].recovered=300000;
    assert.throws(()=>execute(st,'owner',{type:'HR_FNF_CONFIRM',id:f.id}),code('STALE_VERSION'));
    const c=execute(d,'owner',{type:'HR_FNF_CONFIRM',id:f.id});assert.equal(x(c).fnf[0].status,'Settled');assert.equal(x(c).advances[0].recovered,300000);
    assert.equal(M.hrComputeRun(c,'2026-10').lines.some(l=>l.staffId==='HR-S05'),false);
    // additions above ₹1,00,000: the Accountant cannot be the checker; the Owner can
    const o=execute(s,'owner',{type:'HR_FNF_SETTLE',staffId:'HR-S05',adjustments:[{label:'Ex-gratia',kind:'add',amount:15000000}]});
    assert.throws(()=>execute(o,'accountant',{type:'HR_FNF_CONFIRM',id:x(o).fnf[0].id}),e=>e.code==='FORBIDDEN'&&/only the Owner/.test(e.message));
    const a=execute(s,'accountant',{type:'HR_FNF_SETTLE',staffId:'HR-S05',adjustments:[{label:'Ex-gratia',kind:'add',amount:15000000}]});
    assert.equal(x(execute(a,'owner',{type:'HR_FNF_CONFIRM',id:x(a).fnf[0].id})).fnf[0].status,'Settled');
    // discard a draft; the screen shows the draft with the checker's actions
    assert.equal((x(execute(d,'accountant',{type:'HR_FNF_DISCARD',id:f.id})).fnf??[]).length,0);
    assert.match(UI.renderSection(d,'owner','Fnf'),/Confirm/);assert.match(UI.renderSection(d,'accountant','Fnf'),/the other payroll user confirms/);
  });

  test('[PAY-FIX F-14] payslip deduction labels come from the run\'s own rate version',()=>{
    let s=execute(seed(),'owner',{type:'HR_STAT_RATES_SET',from:'2026-09-01',pfEeBps:1000,esicEeBps:100});
    s=execute(s,'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});const id=M.hrRunFor(s,'2026-09').id;
    const slip=UI.renderModal(s,'accountant','hr-payslip',id+'|HR-S05');assert.match(slip,/Provident Fund \(10%\)/);assert.match(slip,/ESIC \(1%\)/);assert.doesNotMatch(slip,/\(12%\)|\(0\.75%\)/);
    assert.match(UI.renderModal(seed(),'accountant','hr-payslip','HR-PAY-1|HR-S05'),/Provident Fund \(12%\)/);
  });
  test('[PAY-FIX F-15] cutting an entitlement never shows a negative balance: 0 left and "over by N" (approved days, SCC); the settings dialog warns; approving over the quota needs a confirmation (DEC-122)',()=>{
    let s=execute(seed(),'reception',{type:'HR_LEAVE_REQUEST',approver:'manager',staffId:'HR-S02',leaveType:'CL',from:'2026-11-16',to:'2026-11-20',reason:'Trip'});
    const ids=x(s).leaves.filter(l=>l.staffId==='HR-S02'&&l.status==='Pending').map(l=>l.id);
    for(const id of ids.slice(0,4))s=execute(s,'manager',{type:'HR_LEAVE_DECIDE',id,approve:true});
    s=execute(s,'owner',{type:'HR_LEAVE_POLICY_SET',ent:{CL:2,SL:7,EL:15}});
    const cl=M.hrLeaveBalance(s,'HR-S02','2026-11-16').find(b=>b.type==='CL');assert.equal(cl.left,0);assert.equal(cl.over,2);
    assert.equal(M.hrLeaveOverCount(seed(),{CL:2,SL:7,EL:15}),0);assert.equal(M.hrLeaveOverCount(s,{CL:2,SL:7,EL:15}),1);
    // DEC-122 (was: SCC re-checks nothing at approval): the 5th date is over the cut quota → SCC's warning until confirmed, then "over by 3"
    assert.throws(()=>execute(s,'manager',{type:'HR_LEAVE_DECIDE',id:ids[4],approve:true}),e=>e.code==='INVALID_TRANSITION'&&/has used 4 of 2 Casual Leave day\(s\)[^]*Approve it anyway\?/.test(e.message));
    const a=execute(s,'manager',{type:'HR_LEAVE_DECIDE',id:ids[4],approve:true,confirmOverBalance:true});assert.equal(M.hrLeaveBalance(a,'HR-S02','2026-11-16').find(b=>b.type==='CL').over,3);
  });
  test('[PAY-FIX F-16] FY26 history gives daily-wage staff leave without pay only (no paid leave on their payslips)',()=>{
    const fy=M.loadScenario('FY26'),daily=x(fy).staff.filter(t=>t.payType==='daily').map(t=>t.id);
    const theirs=x(fy).leaves.filter(l=>l.id.startsWith('HR-LV-H')&&daily.includes(l.staffId));assert.ok(theirs.length>0);
    assert.ok(theirs.every(l=>l.type==='LOP'),theirs.map(l=>l.id+':'+l.type).join(' '));
    for(const r of x(fy).runs)for(const l of r.lines.filter(l=>l.payType==='daily'))assert.equal(l.counts.paidLeave,0,r.month);
  });
  test('[PAY-FIX F-17] statutory CSV says Draft for a draft run; CSV text cells cannot start a formula',()=>{
    const s=execute(seed(),'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'}),r=M.hrRunFor(s,'2026-09');
    assert.match(M.hrStatutoryCsv(s,r),/DRAFT — NOT FINAL/);assert.match(M.hrStatutoryCsv(seed(),M.hrRunFor(seed(),'2026-08')),/Locked run/);
    const t=structuredClone(seed());t.staffX.runs[0].lines[0].name='=HYPERLINK("x")';t.staffX.runs[0].lines[1].name='@SUM(A1)';t.staffX.runs[0].lines[2].name='-cmd';
    const csv=M.hrRegisterCsv(t,t.staffX.runs[0]);assert.ok(csv.includes(`"'=HYPERLINK(""x"")"`));assert.ok(csv.includes(",'@SUM(A1),"));assert.ok(csv.includes(",'-cmd,"));
    assert.doesNotMatch(M.hrBankCsv(t,t.staffX.runs[0]),/,=|,@/);
    assert.match(M.hrStatutoryCsv(seed(),M.hrRunFor(seed(),'2026-08')),/^Component,Employee,Employer,Total\nProvident Fund,\d+\.\d\d,/,'numbers are untouched');
  });

  // ── Lead requests after the browser check (W05P-1/2, AC-01 compatibility class) ──
  test('[PAY-FIX W05P-1] Payroll switched off hides the Payroll / Advances / Full and final / Statutory rates tabs (desktop and phone); attendance and leave stay',()=>{
    const off=execute(seed(),'owner',{type:'SETTING_SET',setting:'module.payroll',value:false});assert.equal(M.cfgModuleOn(off,'payroll'),false);
    for(const phone of [false,true]){const on=UI.renderDesk(seed(),'owner',phone),h=UI.renderDesk(off,'owner',phone);
      for(const t of ['Payroll','Advances','Full and final','Statutory rates'])assert.ok(on.includes('>'+t+'<'),t),assert.ok(!h.includes('>'+t+'<'),'hidden: '+t);
      for(const t of ['Staff','Roster','Attendance','Leave'])assert.ok(h.includes('>'+t+'<'),t);}
    assert.match(UI.renderDesk(off,'owner',false),/Payroll is switched off in Master configuration/);
  });
  test('[PAY-FIX W05P-2] hub rate rows show the real effective rule (not "Immediately") and the long SCC help only once per setting',()=>{
    const s=seed(),rows=M.cfgDefs(s).filter(d=>d.key.startsWith('hr.')&&d.editable&&d.store.kind==='module'&&/^hr\.(structure|pf|esic|pt|ot|late|rounding)\./.test(d.key));
    assert.ok(rows.length>=18);
    for(const d of rows){assert.doesNotMatch(d.effective,/Immediately/,d.key);assert.match(d.effective,/1st of the month after the last completed pay period/);}
    const short='Part of the same dated rate version. Illustrative — CA to confirm for a hotel.',others=rows.filter(d=>d.help!==short).map(d=>d.help);
    assert.equal(new Set(others).size,others.length,'each long SCC paragraph appears once');assert.equal(others.length,new Set(rows.map(d=>d.key.split('.').slice(0,2).join('.'))).size);
  });
  test('[PAY-FIX A04-01] a leaver settled F&F with the exit month salary is in that month’s PF / ESIC / PT statement (not in the run); others unchanged',()=>{
    const s0=seed();let s=execute(s0,'manager',{type:'HR_STAFF_ACTIVE',id:'HR-S05',active:false});// Ravi exits on the business date, 24 Oct 2026
    s=execute(s,'accountant',{type:'HR_FNF_SETTLE',staffId:'HR-S05'});s=execute(s,'owner',{type:'HR_FNF_CONFIRM',id:x(s).fnf[0].id});
    const f=x(s).fnf[0];assert.equal(f.salaryIncluded,true);assert.equal(f.month,'2026-10');
    // Ravi's October F&F line by hand (SCC sheet, DEC-115): ₹13,500/month; 31 days − 2 paid holidays (2 and 20 Oct) = 29 total days; present 18 + 1 late
    // = 19 (both holidays worked); absent 29 − 19 = 10 (4 weekly offs, 1 absent, 7 days after the exit, less the 2 worked holidays) → deduction 10,
    // no extra days → 19 salary days → earned 13,500 × 19 / 29 = 8,844.83; basic 50 % = 4,422.42 (PF wages) → PF 12 % 530.69, employer 13 % 574.91;
    // washing 10 % of basic = 442.24 → ESIC wages 8,844.83 − 442.24 = 8,402.59 (month by month: earned ≤ ₹21,000) → 0.75 % 63.02, 3.25 % 273.08;
    // PT Maharashtra, man, earned ₹7,500.01–10,000 → ₹175.
    assert.deepEqual([f.line.days.total,f.line.days.absent,f.line.days.extra,f.line.payableDays],[29,10,0,19]);
    assert.deepEqual([f.line.earned,f.line.pfWages,f.line.pf,f.line.pfEr,f.line.esicWages,f.line.esic,f.line.esicEr,f.line.pt],[884483,442242,53069,57491,840259,6302,27308,17500]);
    const rates=M.hrRatesFor(s,'2026-10'),run={id:'HR-PAY-T',month:'2026-10',status:'Draft',rates,lines:M.hrComputeRun(s,'2026-10').lines};
    assert.equal(run.lines.some(l=>l.staffId==='HR-S05'),false);assert.equal(run.lines.length,11);
    const before={...run,lines:M.hrComputeRun(s0,'2026-10').lines.filter(l=>l.staffId!=='HR-S05')};
    assert.deepEqual(run.lines,before.lines,'non-leavers unchanged');
    const st=M.hrStatutoryCsv(s,run).split('\n'),t=M.hrRunTotals({lines:[...run.lines,f.line]}),rp=p=>(p/100).toFixed(2);
    assert.ok(st.includes('12,HR-S05,Ravi Shinde,8844.83,4422.42,530.69,574.91,8402.59,63.02,273.08,175.00'));
    assert.equal(st[1],`Provident Fund,${rp(t.pf)},${rp(t.pfEr)},${rp(t.pf+t.pfEr)}`);assert.equal(st[2],`ESIC,${rp(t.esic)},${rp(t.esicEr)},${rp(t.esic+t.esicEr)}`);
    assert.equal(st[3],`Professional Tax (Maharashtra),${rp(t.pt)},0.00,${rp(t.pt)}`);
    assert.equal(st[5],'Sr,Staff ID,Name,Earned,PF wages,PF EE,PF ER,ESIC wages,ESIC EE,ESIC ER,PT','member columns unchanged');
    assert.ok(st.includes("F&F — exit month's salary settled in full and final (not in the pay run): Sr 12 Ravi Shinde (last day 2026-10-24)"));
    // no settled leaver (none, or only a draft F&F): the statement is the run-only one; a leaver already in the run's lines (stale draft) is never added twice
    const draftOnly=execute(execute(s0,'manager',{type:'HR_STAFF_ACTIVE',id:'HR-S05',active:false}),'accountant',{type:'HR_FNF_SETTLE',staffId:'HR-S05'});
    assert.equal(M.hrStatutoryCsv(draftOnly,before),M.hrStatutoryCsv(s0,before));
    assert.doesNotMatch(M.hrStatutoryCsv(s0,before),/F&F/);assert.equal(M.hrStatutoryCsv(s0,before).split('\n').filter(l=>/^\d+,HR-S/.test(l)).length,11);
    const stale={...run,lines:M.hrComputeRun(s0,'2026-10').lines};assert.doesNotMatch(M.hrStatutoryCsv(s,stale),/F&F/);
    assert.equal(M.hrStatutoryCsv(s,stale).split('\n').filter(l=>/,HR-S05,/.test(l)).length,1);
  });
  test('[PAY-FIX A04-11] payslip and full-and-final show "Not calculated: gratuity, notice pay, bonus, TDS on salary" (replaces the gratuity-only line)',()=>{
    assert.equal(M.HR_NOT_CALCULATED,'Not calculated: gratuity, notice pay, bonus, TDS on salary.');
    const s=execute(seed(),'manager',{type:'HR_STAFF_ACTIVE',id:'HR-S05',active:false});
    for(const html of [UI.renderModal(s,'accountant','hr-payslip','HR-PAY-1|HR-S05'),UI.renderModal(s,'accountant','hr-fnf','HR-S05'),UI.renderSection(s,'accountant','Fnf')]){
      assert.ok(html.includes(M.HR_NOT_CALCULATED));assert.doesNotMatch(html,/Gratuity is not calculated/);}
  });
  test('[PAY-FIX COMPAT] staff/payroll code runs without Array.at / replaceAll / structuredClone (Chrome 83 WebView) and the sources do not use them',()=>{
    const bundle=buildSync({entryPoints:['src/services/mock/index.ts'],bundle:true,write:false,platform:'browser',format:'iife',globalName:'HCC',target:'chrome83',logLevel:'error'});
    const ctx=vm.createContext({console});
    vm.runInContext('globalThis.structuredClone=undefined;Array.prototype.at=undefined;String.prototype.replaceAll=undefined;',ctx);
    vm.runInContext(bundle.outputFiles[0].text,ctx);
    const out=vm.runInContext(`(()=>{let s=HCC.seed();
      s=HCC.execute(s,'owner',{type:'HR_STAT_RATES_SET',from:'2026-11-01',pfEeBps:1100});
      s=HCC.execute(s,'owner',{type:'HR_LEAVE_POLICY_SET',ent:{CL:11,SL:7,EL:15}});
      s=HCC.execute(s,'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});const id=HCC.hrRunFor(s,'2026-09').id;
      s=HCC.execute(s,'owner',{type:'HR_PAYROLL_LOCK',id});s=HCC.execute(s,'accountant',{type:'HR_PAYSLIP_SEND',runId:id,staffId:'HR-S05'});
      s=HCC.execute(s,'accountant',{type:'HR_PAYROLL_BANK_FILE',id});s=HCC.execute(s,'owner',{type:'HR_PAYROLL_UNLOCK',id,reason:'Correction',confirmBankFile:true});
      s=HCC.execute(s,'manager',{type:'HR_STAFF_ACTIVE',id:'HR-S09',active:false});s=HCC.execute(s,'accountant',{type:'HR_FNF_SETTLE',staffId:'HR-S09'});
      s=HCC.execute(s,'owner',{type:'HR_FNF_CONFIRM',id:HCC.hrSlice(s).fnf[0].id});
      const fy=HCC.loadScenario('FY26');
      return {last:HCC.hrLastLockedMonth(s),cl:HCC.hrLeavePolicy(s).ent.CL,fnf:HCC.hrSlice(s).fnf[0].status,fyLast:HCC.hrLastLockedMonth(fy),csv:HCC.hrBankCsv(fy,HCC.hrRunFor(fy,'2026-09')).length>100};})()`,ctx);
    assert.deepEqual({...out},{last:'2026-08',cl:11,fnf:'Settled',fyLast:'2026-09',csv:true});
    for(const f of ['src/domain/streams/staff.ts','src/services/mock/handlers/staff.ts','src/fixtures/ext/staff.ts','src/fixtures/history/modules-people.ts','src/features/staff/HrDesk.tsx','src/features/staff/MyHr.tsx','src/features/staff/modals.tsx','src/features/staff/register.tsx']){
      const src=readFileSync(f,'utf8');assert.doesNotMatch(src,/\.at\(|\.replaceAll\(|structuredClone\(/,f);}
  });
  test('[PAY-FIX W05P-3] phone shell: HR chips/tabs/actions and Master configuration search, links and Switch off/on are 48 px with 16 px text',()=>{
    const staff=readFileSync('src/styles/m-staff.css','utf8'),cfg=readFileSync('src/styles/m-config.css','utf8');
    const rule=(css,sel)=>{const i=css.lastIndexOf(sel+'{');assert.ok(i>=0,sel);return css.slice(i,css.indexOf('}',i));};
    for(const sel of ['.m-shell .hr-m .m-chips button','.m-shell .hr-m .btn,.m-shell .hr-m .text-btn,.m-shell .hr-m .small-btn'])assert.match(rule(staff,sel),/min-height:48px;font-size:16px/);
    assert.match(rule(cfg,'.m-shell .cfg-search'),/min-height:48px/);assert.match(rule(cfg,'.m-shell .cfg-search input'),/font-size:16px/);
    assert.match(rule(cfg,'.m-shell .cfg-mod .btn,.m-shell .cfg-mod-act .btn'),/min-height:48px;font-size:16px/);assert.match(rule(cfg,'.m-shell .cfg-link,.m-shell .cfg-hublink'),/min-height:48px/);
  });
  test('[PAY-FIX DEC-074 A04-06] full and final recovers only the part of the advance it covers; the rest stays an open advance (screens say so)',()=>{
    // Ravi (HR-S05): ₹3,000 advance, ₹1,000 recovered in August's locked run → ₹2,000 outstanding; exit today with October's salary in the settlement.
    const s=execute(seed(),'manager',{type:'HR_STAFF_ACTIVE',id:'HR-S05',active:false});
    const st=x(s).staff.find(t=>t.id==='HR-S05'),full=M.hrFnfCompute(s,st,[]),line=full.line,stat=line.pf+line.esic+line.pt;
    assert.deepEqual([full.advanceOutstanding,full.advanceRecovered,full.advanceLeftOpen],[200000,200000,0],'enough to cover: the whole advance, as before');
    // A deduction leaving exactly ₹1,000 before the advance: ₹1,000 recovered, ₹1,000 still owed, net 0
    // (before DEC-074: net −₹1,000 and all ₹2,000 marked recovered, so what the leaver owes vanished from the advance register).
    const ded=line.gross-stat-100000,adj=[{label:'Damage recovery',kind:'ded',amount:ded}];
    const c=M.hrFnfCompute(s,st,adj);
    assert.deepEqual([c.advanceRecovered,c.advanceLeftOpen,c.net,c.recoveries],[100000,100000,0,stat+100000+ded]);
    const d=execute(s,'accountant',{type:'HR_FNF_SETTLE',staffId:'HR-S05',adjustments:adj});
    const n=execute(d,'owner',{type:'HR_FNF_CONFIRM',id:x(d).fnf[0].id});
    assert.equal(x(n).advances[0].recovered,200000);// ₹1,000 (August) + ₹1,000 (this settlement)
    assert.equal(M.hrAdvanceBalance(x(n).advances[0]),100000);
    assert.deepEqual([M.hrFnfAdvanceRecovered(x(n).fnf[0]),M.hrFnfAdvanceLeftOpen(x(n).fnf[0])],[100000,100000]);
    assert.match(UI.renderSection(n,'accountant','Fnf'),/advance ₹1,000 left open/);
    assert.match(UI.renderSection(n,'accountant','Fnf',true),/advance ₹1,000 left open/);
    assert.match(UI.renderSection(n,'accountant','Advances'),/up to what the settlement covers/);
    assert.doesNotMatch(UI.renderModal(s,'accountant','hr-fnf','HR-S05'),/full outstanding advance is recovered/);
    // Deductions ₹500 beyond the settlement: no advance recovered, the ₹2,000 stays open, net −₹500 recoverable.
    const negAdj=[{label:'Damage recovery',kind:'ded',amount:line.gross-stat+50000}],neg=M.hrFnfCompute(s,st,negAdj);
    assert.deepEqual([neg.advanceRecovered,neg.advanceLeftOpen,neg.net],[0,200000,-50000]);
    const nd=execute(s,'owner',{type:'HR_FNF_SETTLE',staffId:'HR-S05',adjustments:negAdj}),nn=execute(nd,'accountant',{type:'HR_FNF_CONFIRM',id:x(nd).fnf[0].id});
    assert.equal(x(nn).advances[0].recovered,100000,'nothing more recovered');assert.equal(M.hrFnfAdvanceLeftOpen(x(nn).fnf[0]),200000);
  });
}
