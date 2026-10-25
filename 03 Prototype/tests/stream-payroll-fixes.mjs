// W05-B code-review fixes (05 Delivery Evidence\Assembly\W05B_Payroll_Code_Review_2026-10-25.md). One regression test per finding, names [PAY-FIX F-nn].
// Money = integer paise; simulated clock only; fictional staff.
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  const {seed,execute}=M;
  // Render harness (payslip dialog, HR sections) — same pattern as stream-payroll-scc.mjs, own bundle.
  await writeFile('tests/.generated/payfix-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can} from '../../src/services/mock';
import {HR_SECTIONS} from '../../src/features/staff/HrDesk';
import {registration} from '../../src/features/staff/register';
function ctx(s:any,uid:string){const user=s.users.find((u:any)=>u.id===uid);
  return {s,uid,user,busy:false,run:async()=>true,open(){},closeModal(){},toast(){},setDirty(){},permission:(p:any)=>can(s,uid,p),display:{isMobile:false}} as any;}
const wrap=(s:any,uid:string,el:any)=>renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid)}>{el}</HccContext.Provider>);
export function renderSection(s:any,uid:string,name:string,phone=false){const C=(HR_SECTIONS as any)[name];return wrap(s,uid,<C phone={phone}/>);}
export function renderModal(s:any,uid:string,type:string,id?:string){const C=(registration.modals as any)[type];return wrap(s,uid,<C id={id}/>);}
`);
  await build({entryPoints:['tests/.generated/payfix-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/payfix-ui.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __pfReq} from 'node:module';const require=__pfReq(import.meta.url);"}});
  const UI=await import('./.generated/payfix-ui.mjs');
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
}
