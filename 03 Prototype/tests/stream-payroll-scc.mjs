// W05-B · payroll, leave and payslips ported from SCC (payroll\index.html, leave\index.html). Names prefixed [PAY-SCC].
// Every statutory rule is tested at its ceiling / threshold and with its rounding. Money = integer paise.
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  const {seed,execute}=M;
  // Render harness (server-side render of the HR desk sections, My shifts & pay and the dialogs; desktop and phone).
  await writeFile('tests/.generated/payroll-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can} from '../../src/services/mock';
import {HR_SECTIONS,HrDesk} from '../../src/features/staff/HrDesk';
import {MyHr} from '../../src/features/staff/MyHr';
import {registration} from '../../src/features/staff/register';
function ctx(s:any,uid:string,mobile:boolean){const user=s.users.find((u:any)=>u.id===uid);
  return {s,uid,user,busy:false,run:async()=>true,open(){},closeModal(){},toast(){},setDirty(){},permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile}} as any;}
const wrap=(s:any,uid:string,mobile:boolean,el:any)=>renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid,mobile)}>{el}</HccContext.Provider>);
export const sections=Object.keys(HR_SECTIONS);
export function renderSection(s:any,uid:string,name:string,phone:boolean){const C=(HR_SECTIONS as any)[name];return wrap(s,uid,phone,<C phone={phone}/>);}
export function renderDesk(s:any,uid:string,phone:boolean){return wrap(s,uid,phone,<HrDesk phone={phone}/>);}
export function renderMine(s:any,uid:string,phone:boolean){return wrap(s,uid,phone,<MyHr phone={phone}/>);}
export function renderModal(s:any,uid:string,type:string,id?:string){const C=(registration.modals as any)[type];return wrap(s,uid,false,<C id={id}/>);}
`);
  await build({entryPoints:['tests/.generated/payroll-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/payroll-ui.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __pyReq} from 'node:module';const require=__pyReq(import.meta.url);"}});
  const UI=await import('./.generated/payroll-ui.mjs');
  const code=c=>e=>e.code===c;
  const x=s=>M.hrSlice(s);
  const R=M.hrDefaultRates();
  const full=(ym='2026-09',days=30,extra={})=>({ym,days,P:days,L:0,H:0,HL:0,A:0,OFFW:0,off:0,paidLeave:0,lopLeave:0,unmarked:0,na:0,otMin:0,...extra});
  const mon=(monthly,extra={})=>({id:'T',name:'Test Person',dept:'FO',designation:'x',payType:'monthly',monthly,daily:0,...extra});

  test('[PAY-SCC] default rate version = SCC rules, dated from 1 Apr 2025, marked illustrative',()=>{
    assert.deepEqual([R.from,R.basicBps,R.hraBps,R.washBps,R.pfEeBps,R.pfErBps,R.pfWageCap,R.esicEeBps,R.esicErBps,R.esicCeiling],['2025-04-01',5000,8000,1000,1200,1300,1500000,75,325,2100000]);
    assert.deepEqual([R.ptMaleExempt,R.ptMaleMid,R.ptMaleMidAmt,R.ptStd,R.ptFeb,R.ptFemaleExempt,R.otBps,R.lateBlock,R.rounding],[750000,1000000,17500,20000,30000,2500000,10000,3,'paise']);
    assert.match(M.HR_DISCLAIMER,/CA to confirm for a hotel/);
  });
  test('[PAY-SCC] PF: 12% / 13% of basic, basic capped at ₹15,000 (at, below and above the cap)',()=>{
    const at=M.hrComputeLine(mon(3000000),full(),R);assert.equal(at.basic,1500000);assert.equal(at.pfWages,1500000);assert.equal(at.pf,180000);assert.equal(at.pfEr,195000);
    const over=M.hrComputeLine(mon(4000000),full(),R);assert.equal(over.basic,2000000);assert.equal(over.pfWages,1500000);assert.equal(over.pf,180000);assert.equal(over.pfEr,195000);
    const under=M.hrComputeLine(mon(2000000),full(),R);assert.equal(under.pfWages,1000000);assert.equal(under.pf,120000);assert.equal(under.pfEr,130000);
    const off=M.hrComputeLine(mon(2000000,{pfApplicable:false}),full(),R);assert.equal(off.pf+off.pfEr+off.pfWages,0);
    const basicOnly=M.hrComputeLine(mon(1200000,{structure:'B'}),full(),R);assert.equal(basicOnly.basic,1200000);assert.equal(basicOnly.pf,0,'SCC: PF only for structured pay by default');
    const basicPf=M.hrComputeLine(mon(1200000,{structure:'B',pfApplicable:true}),full(),R);assert.equal(basicPf.pf,144000);
  });
  test('[PAY-SCC] ESIC: 0.75% / 3.25% of earned − washing, only while earned ≤ ₹21,000; half-up to paise',()=>{
    const at=M.hrComputeLine(mon(2100000),full(),R);
    assert.deepEqual([at.basic,at.washing,at.esicWages],[1050000,105000,1995000]);assert.equal(at.esic,14963);assert.equal(at.esicEr,64838);// 14,962.5 → 14,963 · 64,837.5 → 64,838
    const above=M.hrComputeLine(mon(2100001),full(),R);assert.equal(above.esic,0);assert.equal(above.esicEr,0);assert.equal(above.esicWages,0);
    const off=M.hrComputeLine(mon(1500000,{esicApplicable:false}),full(),R);assert.equal(off.esic,0);
    // SCC month-by-month rule (esicPeriod off): earned (not contractual) pay decides — ₹25,000 salary with 6 days LOP earns ₹20,000 → covered.
    // The default version uses the contribution-period rule instead (review F-07, tests/stream-payroll-fixes.mjs).
    const lop=M.hrComputeLine(mon(2500000),full('2026-09',30,{P:24,A:6}),{...R,esicPeriod:false});assert.equal(lop.earned,2000000);assert.ok(lop.esic>0);
  });
  test('[PAY-SCC] rupee rounding option: PF to the nearest rupee, ESIC up to the next rupee',()=>{
    const rr={...R,rounding:'rupee'};
    const a=M.hrComputeLine(mon(2100000),full(),rr);assert.equal(a.esic,15000);assert.equal(a.esicEr,64900);assert.equal(a.pf,126000);
    const b=M.hrComputeLine(mon(2100900),full(),rr),bp=M.hrComputeLine(mon(2100900),full(),R);
    assert.equal(bp.pf,126054);assert.equal(b.pf,126100);// 1,260.54 → 1,261
    const c=M.hrComputeLine(mon(2100100),full(),rr),cp=M.hrComputeLine(mon(2100100),full(),R);
    assert.equal(cp.pf,126006);assert.equal(c.pf,126000);// 1,260.06 → 1,260
    assert.equal(M.hrPct(1995000,75,'paise'),14963);assert.equal(M.hrPct(1995000,75,'rupee','esic'),15000);assert.equal(M.hrPct(100,75,'rupee','esic'),100);
  });
  test('[PAY-SCC] Maharashtra PT slabs: men nil / ₹175 / ₹200, women nil to ₹25,000, ₹300 in February (mid slab never bumped)',()=>{
    const pt=(base,g,ym='2026-09')=>M.hrPt(base,g,ym,R);
    assert.deepEqual([pt(750000,'M'),pt(750001,'M'),pt(1000000,'M'),pt(1000001,'M')],[0,17500,17500,20000]);
    assert.deepEqual([pt(750000,'M','2027-02'),pt(1000000,'M','2027-02'),pt(1000001,'M','2027-02')],[0,17500,30000]);
    assert.deepEqual([pt(2500000,'F'),pt(2500001,'F'),pt(2500001,'F','2027-02'),pt(1000001,'F')],[0,20000,30000,0]);
    const feb=M.hrComputeLine(mon(2000000),full('2027-02',28),R);assert.equal(feb.pt,30000);
    const woman=M.hrComputeLine(mon(2200000,{gender:'F'}),full(),R);assert.equal(woman.pt,0);
    const zero=M.hrComputeLine(mon(2000000),full('2026-09',30,{P:0,A:30}),R);assert.equal(zero.earned,0);assert.equal(zero.pt,0);
  });
  test('[PAY-SCC] overtime at the ordinary rate (SCC) is outside the PF / ESIC / PT base; exact figures',()=>{
    const l=M.hrComputeLine(mon(1000000),full('2026-09',30,{otMin:600}),R);
    assert.equal(l.ot,41667);// 1,000,000 × 600 / (30 × 8 × 60) = 41,666.67 → 41,667
    assert.equal(l.pt,17500,'PT on earned ₹10,000, not ₹10,416.67');assert.equal(l.pf,60000);assert.equal(l.esicWages,950000);assert.equal(l.esic,7125);assert.equal(l.esicEr,30875);
    assert.equal(l.gross,1041667);assert.equal(l.deductions,84625);assert.equal(l.net,957042);
    const two=M.hrComputeLine(mon(1000000),full('2026-09',30,{otMin:600}),{...R,otBps:20000});assert.equal(two.ot,83333);
    const big=M.hrMulDiv(100000000,14880,30000,8*60*10000*31);
    assert.equal(big,Number((100000000n*14880n*30000n*2n+BigInt(8*60*10000*31))/(2n*BigInt(8*60*10000*31))));
  });
  test('[PAY-SCC] attendance: 3 late marks = ½ day, half-day = ½ day, absent / unmarked = 1 day, structured split sums to earned',()=>{
    const pd=c=>M.hrComputeLine(mon(3000000),full('2026-09',30,c),R).payableDays;
    assert.deepEqual([pd({L:2}),pd({L:3}),pd({L:5}),pd({L:6}),pd({H:1}),pd({A:1}),pd({unmarked:1}),pd({lopLeave:0.5})],[30,29.5,29.5,29,29.5,29,29,29.5]);
    for(const m of [1234567,1350000,999999]){const l=M.hrComputeLine(mon(m),full('2026-09',30,{H:3,L:4}),R);assert.equal(l.basic+l.hra+l.washing+l.special,l.earned);assert.ok(l.special>=0);}
    assert.equal(M.hrComputeLine(mon(3000000),full('2026-09',30,{L:3}),{...R,lateBlock:0}).payableDays,30);
  });
  test('[PAY-SCC] half-day leave: request rules, 0.5 day, AM + PM coexist, same half conflicts, weekly off refused',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'housekeeper',{type:'HR_LEAVE_REQUEST',staffId:'HR-S05',leaveType:'CL',from:'2026-10-30',half:'AM',reason:'Bank'}),/weekly off/);
    assert.throws(()=>execute(s,'housekeeper',{type:'HR_LEAVE_REQUEST',staffId:'HR-S05',leaveType:'CL',from:'2026-11-02',half:'X',reason:'Bank'}),code('VALIDATION'));
    s=execute(s,'housekeeper',{type:'HR_LEAVE_REQUEST',staffId:'HR-S05',leaveType:'CL',from:'2026-11-02',to:'2026-11-05',half:'AM',reason:'Bank work'});
    const l=x(s).leaves.at(-1);assert.deepEqual([l.from,l.to,l.days,l.half],['2026-11-02','2026-11-02',0.5,'AM']);
    assert.equal(M.hrLeaveBalance(s,'HR-S05','2026-11-02').find(b=>b.type==='CL').left,9.5);
    assert.throws(()=>execute(s,'housekeeper',{type:'HR_LEAVE_REQUEST',staffId:'HR-S05',leaveType:'CL',from:'2026-11-02',half:'AM',reason:'Again'}),code('NOT_AVAILABLE'));
    assert.throws(()=>execute(s,'housekeeper',{type:'HR_LEAVE_REQUEST',staffId:'HR-S05',leaveType:'SL',from:'2026-11-02',to:'2026-11-02',reason:'Full'}),code('NOT_AVAILABLE'));
    s=execute(s,'housekeeper',{type:'HR_LEAVE_REQUEST',staffId:'HR-S05',leaveType:'SL',from:'2026-11-02',half:'PM',reason:'Doctor'});
    assert.equal(x(s).leaves.at(-1).days,0.5);
    assert.ok(x(seed()).leaves.some(l=>l.id==='HR-LV-5'&&l.half==='PM'&&l.days===0.5),'fixture has a pending half-day');
  });
  test('[PAY-SCC] half-day leave in payroll: worked half + paid half = full day; LOP half or no other half = ½ day lost',()=>{
    const base=structuredClone(seed()),st=x(base).staff.find(t=>t.id==='HR-S09'),d='2026-09-10',k=d+'|HR-S09';
    base.staffX.att[k]={status:'P',at:d+'T07:00',otMin:0,by:'manager'};
    const fullDay=M.hrComputeRun(base,'2026-09').lines.find(l=>l.staffId==='HR-S09');
    const leave=(type)=>({id:'T-LV',staffId:'HR-S09',type,from:d,to:d,days:0.5,half:'AM',reason:'x',status:'Approved',requestedBy:'manager',requestedAt:d+'T09:00'});
    const run=(att,type)=>{const c=structuredClone(base);if(att)c.staffX.att[k]={status:att,at:d+'T07:00',otMin:0,by:'manager'};else delete c.staffX.att[k];if(type)c.staffX.leaves.push(leave(type));
      return {line:M.hrComputeRun(c,'2026-09').lines.find(l=>l.staffId==='HR-S09'),c:M.hrCounts(c,st,'2026-09')};};
    const half=M.hrMulDiv(st.monthly,1,1,60);
    const paid=run('H','CL');assert.equal(paid.c.HL,1);assert.equal(paid.c.paidLeave,fullDay.counts.paidLeave+0.5);assert.equal(paid.line.earned,fullDay.earned);
    const lop=run('H','LOP');assert.equal(lop.c.lopLeave,0.5);assert.equal(lop.line.payableDays,fullDay.payableDays-0.5);
    const noAtt=run(undefined,'CL');assert.equal(noAtt.c.unmarked,0.5);assert.equal(noAtt.line.payableDays,fullDay.payableDays-0.5);
    const plainH=run('H');assert.equal(plainH.line.payableDays,fullDay.payableDays-0.5);
    assert.equal(plainH.line.earned,M.hrMulDiv(st.monthly,2*fullDay.payableDays-1,1,60));assert.ok(half>0);
    // self check-in on a half-day leave day records a half-day
    const t=structuredClone(seed());t.staffX.leaves.push({id:'T-LV2',staffId:'HR-S06',type:'CL',from:t.date,to:t.date,days:0.5,half:'PM',reason:'x',status:'Approved',requestedBy:'housekeeper2',requestedAt:t.date+'T07:00'});
    const n=execute(t,'housekeeper2',{type:'HR_ATT_MARK',staffId:'HR-S06'});assert.equal(x(n).att[t.date+'|HR-S06'].status,'H');
  });
  test('[PAY-SCC] max staff off per department per day: pending and approved hold a slot, rejected frees it, policy is Owner-only',()=>{
    let s=seed();// Housekeeping: Kavya pending 28 Oct; cap 2
    s=execute(s,'housekeeper',{type:'HR_LEAVE_REQUEST',staffId:'HR-S05',leaveType:'CL',from:'2026-10-28',to:'2026-10-28',reason:'Family'});
    const ravi=x(s).leaves.at(-1).id;
    assert.throws(()=>execute(s,'supervisor',{type:'HR_LEAVE_REQUEST',staffId:'HR-S04',leaveType:'CL',from:'2026-10-28',to:'2026-10-28',reason:'Travel'}),e=>e.code==='NOT_AVAILABLE'&&/Housekeeping already has 2 staff off/.test(e.message));
    const cal=M.hrLeaveCalendar(s,'2026-10').find(d=>d.date==='2026-10-28');assert.equal(cal.depts.find(d=>d.dept==='HK').count,2);assert.equal(cal.full,true);
    const r=execute(s,'manager',{type:'HR_LEAVE_DECIDE',id:ravi,approve:false,note:'Short-staffed'});
    assert.ok(execute(r,'supervisor',{type:'HR_LEAVE_REQUEST',staffId:'HR-S04',leaveType:'CL',from:'2026-10-28',to:'2026-10-28',reason:'Travel'}));
    assert.throws(()=>execute(s,'manager',{type:'HR_LEAVE_POLICY_SET',deptCap:{HK:3}}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'HR_LEAVE_POLICY_SET',defaultCap:0}),code('VALIDATION'));
    const p=execute(s,'owner',{type:'HR_LEAVE_POLICY_SET',deptCap:{HK:3},ent:{CL:12,SL:7,EL:15}});assert.equal(M.hrDeptCap(p,'HK'),3);assert.equal(M.hrDeptCap(p,'FO'),2);
    assert.ok(execute(p,'supervisor',{type:'HR_LEAVE_REQUEST',staffId:'HR-S04',leaveType:'CL',from:'2026-10-28',to:'2026-10-28',reason:'Travel'}));
    const e=execute(s,'owner',{type:'HR_LEAVE_POLICY_SET',ent:{CL:1,SL:7,EL:15}});
    assert.throws(()=>execute(e,'reception',{type:'HR_LEAVE_REQUEST',staffId:'HR-S02',leaveType:'CL',from:'2026-11-11',to:'2026-11-12',reason:'Trip'}),/balance/);
  });
  test('[PAY-SCC] SCC entitlements per Indian FY (CL 12 · SL 7 · EL 15); a request cannot cross 31 March',()=>{
    const s=seed();assert.deepEqual(M.hrLeaveBalance(s,'HR-S02','2026-10-24').map(b=>[b.type,b.entitled]),[['CL',12],['SL',7],['EL',15]]);
    assert.equal(M.hrFy('2026-03-31').label,'FY 2025–26');assert.equal(M.hrFy('2026-04-01').from,'2026-04-01');assert.equal(M.hrFy('2026').to,'2027-03-31');
    assert.throws(()=>execute(s,'reception',{type:'HR_LEAVE_REQUEST',staffId:'HR-S02',leaveType:'CL',from:'2027-03-30',to:'2027-04-02',reason:'Trip'}),/31 March/);
    const n=execute(s,'reception',{type:'HR_LEAVE_REQUEST',staffId:'HR-S02',leaveType:'CO',from:'2026-11-11',to:'2026-11-11',reason:'Worked Diwali'});
    assert.equal(x(n).leaves.at(-1).type,'CO');assert.equal(M.hrPaidLeave('CO'),true);assert.equal(M.hrPaidLeave('LOP'),false);
  });
  test('[PAY-SCC] lock blocks on unset salary; Owner unlock reverses recoveries, needs a reason, latest month only, not for archived attendance',()=>{
    let s=execute(seed(),'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});const id=M.hrRunFor(s,'2026-09').id;
    s=execute(s,'owner',{type:'HR_PAYROLL_LOCK',id});assert.equal(x(s).advances[0].recovered,200000);
    assert.throws(()=>execute(s,'owner',{type:'HR_PAYROLL_UNLOCK',id:'HR-PAY-1',reason:'Correction'}),code('INVALID_TRANSITION'));// a later month is locked
    assert.throws(()=>execute(s,'accountant',{type:'HR_PAYROLL_UNLOCK',id,reason:'Correction'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'HR_PAYROLL_UNLOCK',id,reason:''}),code('VALIDATION'));
    let u=execute(s,'owner',{type:'HR_PAYROLL_UNLOCK',id,reason:'Attendance correction'});
    const r=M.hrRunFor(u,'2026-09');assert.equal(r.status,'Draft');assert.equal(r.lockedBy,undefined);assert.equal(r.unlocks.length,1);
    assert.equal(x(u).advances[0].recovered,100000);assert.ok(x(u).advances[0].recoveries.every(q=>q.runId!==id));
    assert.equal(M.hrVisiblePayslips(u,'housekeeper').length,1);
    u=execute(u,'owner',{type:'HR_PAYROLL_LOCK',id});assert.equal(x(u).advances[0].recovered,200000);
    // the BASE August run: its raw attendance is archived
    assert.throws(()=>execute(seed(),'owner',{type:'HR_PAYROLL_UNLOCK',id:'HR-PAY-1',reason:'Correction'}),/archived/);
    // unset salary blocks the lock
    const z=structuredClone(seed());z.staffX.staff.find(t=>t.id==='HR-S11').monthly=0;
    const d=execute(z,'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});
    assert.throws(()=>execute(d,'owner',{type:'HR_PAYROLL_LOCK',id:M.hrRunFor(d,'2026-09').id}),/salary not set/);
  });
  test('[PAY-SCC] dated rates: version in force per month, legacy HR_RATES_SET defaults to the current month (review F-06), explicit from may be earlier',()=>{
    let s=execute(seed(),'owner',{type:'HR_RATES_SET',esicCeiling:2500000});
    assert.deepEqual(M.hrRateVersions(s).map(v=>v.from),['2025-04-01','2026-10-01']);
    assert.equal(M.hrRatesFor(s,'2026-09').esicCeiling,2100000);assert.equal(M.hrRatesFor(s,'2026-10').esicCeiling,2500000);
    s=execute(s,'owner',{type:'HR_STAT_RATES_SET',from:'2026-10-01',esicCeiling:2400000});assert.equal(M.hrRateVersions(s).length,2);assert.equal(M.hrRatesFor(s,'2026-12').esicCeiling,2400000);
    s=execute(s,'owner',{type:'HR_STAT_RATES_SET',from:'2026-09-01',esicCeiling:2400000});assert.equal(M.hrRateVersions(s).length,3);
    assert.throws(()=>execute(s,'owner',{type:'HR_STAT_RATES_SET',from:'2026-10-15'}),code('VALIDATION'));
    assert.throws(()=>execute(s,'owner',{type:'HR_STAT_RATES_SET',from:'2026-11-01',basicBps:9000,hraBps:8000}),/cannot exceed/);
    s=execute(s,'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});assert.equal(M.hrRunFor(s,'2026-09').rates.esicCeiling,2400000);
  });
  test('[PAY-SCC] bank transfer CSV and statutory CSV (SCC reportBankCsv / reportStatutoryCsv)',()=>{
    const s=seed(),r=M.hrRunFor(s,'2026-08'),bank=M.hrBankCsv(s,r).trim().split('\n');
    assert.equal(bank[0],'Sr,Employee Name,Employee ID,Bank Name,Account No,IFSC,Net Amount');assert.equal(bank.length,1+12+1+1);
    const ravi=r.lines.find(l=>l.staffId==='HR-S05'),row=bank.find(l=>l.includes(',HR-S05,'));
    assert.ok(row.includes(",'000012"),'account number keeps the leading apostrophe');assert.ok(row.endsWith((ravi.net/100).toFixed(2)));assert.ok(row.includes('DEMO0000'));
    const t=M.hrRunTotals(r);assert.ok(bank[13].includes('TOTAL')&&bank[13].endsWith((t.net/100).toFixed(2)));assert.match(bank[14],/simulated transfer/);
    const st=M.hrStatutoryCsv(s,r).split('\n');
    assert.equal(st[1],`Provident Fund,${(t.pf/100).toFixed(2)},${(t.pfEr/100).toFixed(2)},${((t.pf+t.pfEr)/100).toFixed(2)}`);
    assert.equal(st[2],`ESIC,${(t.esic/100).toFixed(2)},${(t.esicEr/100).toFixed(2)},${((t.esic+t.esicEr)/100).toFixed(2)}`);
    assert.ok(st[3].startsWith('Professional Tax (Maharashtra),'+(t.pt/100).toFixed(2)));assert.equal(st.filter(l=>/^\d+,HR-S/.test(l)).length,12);
    for(const l of r.lines)assert.equal(l.net,l.gross-l.pf-l.esic-l.pt-l.advance);
  });
  test('[PAY-SCC] payslip delivery: simulated WhatsApp after lock only, payroll roles only, Indian mobile number; amount in words',()=>{
    const s=seed();
    assert.throws(()=>execute(s,'housekeeper',{type:'HR_PAYSLIP_SEND',runId:'HR-PAY-1',staffId:'HR-S05'}),code('FORBIDDEN'));
    const n=execute(s,'accountant',{type:'HR_PAYSLIP_SEND',runId:'HR-PAY-1',staffId:'HR-S05'});
    assert.deepEqual(M.hrRunFor(n,'2026-08').sent.map(x=>[x.staffId,x.to]),[['HR-S05','919000010029']]);
    const d=execute(s,'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});
    assert.throws(()=>execute(d,'accountant',{type:'HR_PAYSLIP_SEND',runId:M.hrRunFor(d,'2026-09').id,staffId:'HR-S05'}),code('INVALID_TRANSITION'));
    const l=M.hrRunFor(s,'2026-08').lines.find(l=>l.staffId==='HR-S05'),msg=M.hrPayslipMessage(s,M.hrRunFor(s,'2026-08'),l);
    assert.match(msg,/^Namaste Ravi/);assert.match(msg,/Net pay: Rs\./);assert.match(msg,/simulated WhatsApp/);
    assert.deepEqual([M.hrWaNumber('90000 10029'),M.hrWaNumber('+91 90000 10029'),M.hrWaNumber('12345')],['919000010029','919000010029','']);
    assert.equal(M.hrAmountWords(1234550),'Rupees Twelve Thousand Three Hundred Forty Five and Fifty Paise Only');
    assert.equal(M.hrAmountWords(1000000000),'Rupees One Crore Only');assert.equal(M.hrAmountWords(0),'Rupees Zero Only');
  });
  test('[PAY-SCC] full and final: exit month pay − statutory − full outstanding advance ± adjustments; once; removes the staff from that month’s run',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'accountant',{type:'HR_FNF_SETTLE',staffId:'HR-S05'}),code('INVALID_TRANSITION'));// still active
    s=execute(s,'manager',{type:'HR_STAFF_ACTIVE',id:'HR-S05',active:false});
    const st=x(s).staff.find(t=>t.id==='HR-S05');
    assert.equal(M.hrElEncashment(s,st),M.hrMulDiv(1350000,30,1,62));
    assert.throws(()=>execute(s,'manager',{type:'HR_FNF_SETTLE',staffId:'HR-S05'}),code('FORBIDDEN'));
    const adj=[{label:'Leave encashment (EL 15 days)',kind:'add',amount:653226},{label:'Uniform not returned',kind:'ded',amount:50000}];
    const n=execute(s,'accountant',{type:'HR_FNF_SETTLE',staffId:'HR-S05',adjustments:adj});
    const f=x(n).fnf[0];assert.equal(f.month,'2026-10');assert.equal(f.salaryIncluded,true);assert.equal(f.advanceOutstanding,200000);
    assert.equal(f.line.counts.na,7);// 25–31 Oct after the exit
    assert.equal(f.earnings,f.line.gross+653226);assert.equal(f.recoveries,f.line.pf+f.line.esic+f.line.pt+200000+50000);assert.equal(f.net,f.earnings-f.recoveries);
    assert.equal(x(n).advances[0].recovered,300000);
    assert.equal(M.hrComputeRun(n,'2026-10').lines.some(l=>l.staffId==='HR-S05'),false);assert.equal(M.hrComputeRun(s,'2026-10').lines.some(l=>l.staffId==='HR-S05'),true);
    assert.throws(()=>execute(n,'accountant',{type:'HR_FNF_SETTLE',staffId:'HR-S05'}),code('INVALID_TRANSITION'));
    assert.throws(()=>execute(n,'manager',{type:'HR_STAFF_ACTIVE',id:'HR-S05',active:true}),/Full and final/);
    const neg=execute(s,'owner',{type:'HR_FNF_SETTLE',staffId:'HR-S05',adjustments:[{label:'Damage recovery',kind:'ded',amount:5000000}]});assert.ok(x(neg).fnf[0].net<0);
    const me=execute(seed(),'manager',{type:'HR_STAFF_ACTIVE',id:'HR-S07',active:false});
    assert.throws(()=>execute(me,'accountant',{type:'HR_FNF_SETTLE',staffId:'HR-S07'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'HR_FNF_SETTLE',staffId:'HR-S05',adjustments:[{label:'x',kind:'add',amount:-5}]}),code('VALIDATION'));
  });
  test('[PAY-SCC] staff master: gender for PT by managers; PF / ESIC flags and bank details only by payroll roles',()=>{
    const s=seed(),b={type:'HR_STAFF_SAVE',id:'HR-S09',name:'Suresh Pawar',dept:'KIT',designation:'Cook',defaultShift:'M',weeklyOff:3};
    assert.equal(x(execute(s,'manager',{...b,gender:'M'})).staff.find(t=>t.id==='HR-S09').gender,'M');
    assert.throws(()=>execute(s,'manager',{...b,esicApplicable:false}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'accountant',{...b,ifsc:'BAD'}),code('VALIDATION'));
    const n=execute(s,'accountant',{...b,esicApplicable:false,ifsc:'DEMO0000777',accountNo:'00001234567'});const t=x(n).staff.find(t=>t.id==='HR-S09');
    assert.deepEqual([t.esicApplicable,t.ifsc,t.accountNo],[false,'DEMO0000777','00001234567']);
  });
  test('[PAY-SCC] hrSettingDefs for the Master configuration hub: one group, dated statutory rates, values from the state',()=>{
    const s=seed(),defs=M.hrSettingDefs();
    assert.equal(new Set(defs.map(d=>d.key)).size,defs.length);assert.ok(defs.every(d=>d.group==='People, payroll and leave'));
    const stat=defs.filter(d=>d.category==='Statutory rates');assert.ok(stat.length>=6&&stat.every(d=>d.dated&&d.approver==='Owner'&&/CA to confirm/.test(d.help)&&d.command==='HR_STAT_RATES_SET'));
    assert.ok(defs.some(d=>d.key==='hr.leave.cap'&&d.command==='HR_LEAVE_POLICY_SET'));
    for(const d of defs)assert.notEqual(M.hrSettingValue(s,d.key),'—',d.key);
    assert.match(M.hrSettingValue(s,'hr.pf'),/12% \+ 13%/);assert.match(M.hrSettingValue(s,'hr.leave.ent'),/CL 12 · SL 7 · EL 15/);
  });
  test('[PAY-SCC] UI renders: every HR section (desktop + phone), My shifts & pay payslip card, dialogs; phone has no tables',()=>{
    const fy=M.loadScenario('FY26');
    for(const s of [seed(),fy])for(const name of UI.sections)for(const phone of [false,true]){
      const html=UI.renderSection(s,'owner',name,phone);assert.ok(html.length>50,name);
      if(phone&&['Payroll','Rates','Fnf','LeaveCalendar','LeavePolicy','Advances'].includes(name))assert.ok(!html.includes('<table'),'phone '+name+' uses cards');}
    const pay=UI.renderSection(fy,'accountant','Payroll',false);assert.match(pay,/Bank transfer file/);assert.match(pay,/PF \/ ESIC \/ PT statement/);assert.match(pay,/Register Excel/);
    assert.match(UI.renderSection(fy,'owner','Payroll',false),/Unlock/);assert.doesNotMatch(UI.renderSection(fy,'accountant','Payroll',false),/Unlock/);
    assert.match(UI.renderSection(seed(),'owner','Rates',false),/Illustrative — CA to confirm for a hotel/);assert.match(UI.renderSection(seed(),'owner','Rates',true),/tablet or desktop/);
    assert.match(UI.renderSection(seed(),'owner','LeaveCalendar',false),/Kavya · CL \(pending\)/);
    const mine=UI.renderMine(seed(),'housekeeper',true);assert.match(mine,/LATEST PAYSLIP/);assert.match(mine,/Payslip PDF/);assert.match(mine,/Provident Fund/);assert.match(mine,/Casual leave/);
    assert.doesNotMatch(UI.renderMine(seed(),'housekeeper',false),/Meera/);
    assert.match(UI.renderDesk(seed(),'housekeeper',true),/LATEST PAYSLIP/,'non-HR roles fall back to My shifts & pay');
    const slip=UI.renderModal(seed(),'accountant','hr-payslip','HR-PAY-1|HR-S05');assert.match(slip,/Send on WhatsApp/);assert.match(slip,/Rupees /);assert.match(slip,/Payslip PDF/);
    assert.doesNotMatch(UI.renderModal(seed(),'housekeeper','hr-payslip','HR-PAY-1|HR-S05'),/Send on WhatsApp/);assert.match(UI.renderModal(seed(),'housekeeper','hr-payslip','HR-PAY-1|HR-S04'),/Payslip unavailable/);
    const ex=execute(seed(),'manager',{type:'HR_STAFF_ACTIVE',id:'HR-S05',active:false});assert.match(UI.renderModal(ex,'accountant','hr-fnf','HR-S05'),/Net payable|Recoverable/);
    for(const m of ['hr-rates','hr-leave-policy','hr-leave','hr-advance','hr-staff'])assert.ok(UI.renderModal(seed(),'owner',m).length>50,m);
    assert.match(UI.renderModal(seed(),'owner','hr-unlock','HR-PAY-1'),/Unlock/);
  });
}
