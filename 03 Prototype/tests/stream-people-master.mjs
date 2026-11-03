// MA-6 · People lists (plan Part 2 §8 M19–M22): designations, shift patterns, leave types, holiday calendar. Names prefixed [PPL].
// Seeds reproduce today's constants, so BASE / FY26 / payroll figures stay identical until a list is saved.
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
export function register({test,M,assert}){
  const {seed,execute,mstRows,mstByCode,mstSpec,mstSpecsOfGroup,mstHistory,mstReasonOf,MstError,mstSearch,
    hrShiftInfo,hrShiftFor,hrCheckinStatus,hrShiftPatterns,hrShiftPattern,hrCounts,hrComputeLine,hrComputeRun,hrLeaveDays,hrLeaveBalance,hrLeaveQuota,hrLeavePolicy,
    hrLeaveName,hrLeavePaid,hrLeaveChoices,hrTrackedTypes,hrHolidayPaid,hrHolidayOn,hrHolidayDates,hrHolidayFy,hrDesignationOf,hrDesignationChoices,hrSeedDesignations,
    hrWeeklyOffAuto,hrShiftMinutes,hrPayRuleFields,cmpSlice,cmpView,cmpFindCard,cmpParseDue,hrDayStatus,HR_SHIFTS}=M;
  const code=c=>e=>e.code===c;
  const reason=r=>e=>e instanceof MstError&&e.reason===r&&mstReasonOf(e)===r;
  const src=f=>readFileSync(f,'utf8');
  const sha=o=>createHash('sha256').update(JSON.stringify(o)).digest('hex');
  const strip=s=>{const {mastersX,grants,...r}=s;return r;};
  const save=(s,u,master,row,key)=>execute(s,u,{type:'MST_MASTER_SAVE',master,row,key});
  const x=s=>s.staffX;
  const staffRow=(s,id)=>x(s).staff.find(t=>t.id===id);

  // ── Registration, seeds, figures unchanged ──
  test('[PPL] four lists registered in "People, payroll and leave"; operational by default, holidays Owner-only; pay fields marked Owner-only',()=>{
    assert.deepEqual(mstSpecsOfGroup('people').map(p=>p.id).sort(),['designations','holidays','leaveTypes','shiftPatterns']);
    for(const id of ['designations','shiftPatterns','leaveTypes'])assert.deepEqual([mstSpec(id).permission,mstSpec(id).money],['masters',false],id);
    assert.deepEqual([mstSpec('holidays').permission,mstSpec('holidays').money],['settings',true]);
    const money=id=>mstSpec(id).fields.filter(f=>f.money).map(f=>f.key);
    assert.deepEqual(money('designations'),['defaultPayRule']);assert.deepEqual(money('leaveTypes'),['paid','yearlyQuota']);assert.deepEqual(money('shiftPatterns'),[]);});
  test('[PPL] seeds: shift patterns M E N G (today\'s times, 30 min break, N is night), leave types = SCC’s six (CL 12 · SL 7 · EL 15, ML, CO, Other) + LOP, 8 holidays, one designation per staff title',()=>{
    const s=seed();
    assert.deepEqual(mstRows(s,'shiftPatterns').map(r=>[r.code,r.start,r.end,!!r.night,r.breakMinutes,r.weeklyOffRule]),
      [['M','07:00','15:00',false,30,'fixed'],['E','15:00','23:00',false,30,'fixed'],['N','23:00','07:00',true,30,'fixed'],['G','09:00','18:00',false,30,'fixed']]);
    assert.deepEqual(mstRows(s,'leaveTypes').map(r=>[r.code,r.paid,r.yearlyQuota]),[['CL',true,12],['SL',true,7],['EL',true,15],['ML',true,undefined],['CO',true,undefined],['OTH',true,undefined],['LOP',false,undefined]]);
    assert.deepEqual(hrTrackedTypes(s),['CL','SL','EL']);assert.equal(mstRows(s,'holidays').length,8);
    const titles=new Set(x(s).staff.map(t=>t.designation));assert.equal(mstRows(s,'designations').length,titles.size);
    for(const t of x(s).staff)assert.equal(hrDesignationOf(s,t)?.name,t.designation);});
  test('[PPL] fixture figures, BASE and FY26 hashes unchanged; no list stored in the fixture',()=>{
    const s=seed();assert.equal(M.metrics(s).occupied,8);assert.equal(M.metrics(s).collections,2150000);assert.equal(M.drawer(s),1072500);assert.equal(s.mastersX,undefined);
    assert.equal(sha(strip(seed())),'70983afb19473bb217d3b6892b5e97a1e437b3859c7ecef2f206064b0bf81feb') /* ADM-int: d6df4a46… → 70983afb… = SCHEMA 14 → 15 only */ /* F6 setup: f8788cff… → d6df4a46… = SCHEMA 13 → 14 only */ /* LF-int (F3): 286e328b… → f8788cff… = SCHEMA 12 → 13 + compliance demo evidence moved to the documents mirror docObjectsX (LF-docdb); FY26 5b118f02… → 415be48f… same cause */ /* ACC-2: SCHEMA 11 → 12 only (new optional fields) */ /* ACC-1: SCHEMA 10 → 11 only (accounting chart / opening fields are optional and absent in the seed) */ /* SCC decisions wave (DEC-133): 4a8aad03… → aecae3b2… = the demo firm profile's fictional licence numbers / expiry dates only */ /* SCC port wave (DEC-116, tax + leave): 2c1782df… → 4a8aad03… = compliance + leave slices, SCHEMA 9 → 10 */ /* old → new: d885a20a… → add626d3… = SCHEMA 8 → 9 only (masters wave) */;
    assert.equal(sha(strip(M.buildHistory(M.baseFixture()))),'e124796477d1f35383df8f083501d5bc23c4df68e8947cc4f6cbe8fcae75e25b') /* ADM-int: FY26 history 961b04c2… → e1247964… = SCHEMA 14 → 15 only */ /* F6 setup: FY26 14d8a8f7… → 961b04c2… = SCHEMA 13 → 14 only */ /* LF-int (F3): 286e328b… → f8788cff… = SCHEMA 12 → 13 + compliance demo evidence moved to the documents mirror docObjectsX (LF-docdb); FY26 5b118f02… → 415be48f… same cause */ /* ACC-2: SCHEMA 11 → 12 only (new optional fields) */ /* ACC-1 integration (all seven lanes merged + the ITC / cash-over one-liners): the FY26 history locks its accounting months with accLiveVouchers snapshots, which now carry the re-pointed mapping (POS 4200, ITC 1310 / 1320, cash over 4950), party ledger ids (1000/TILL, 1030/<bank>, 1120/UD-…, 2200/<vendor>) and the Udhaar credit / company payment vouchers: f3601522… → e3a74ba3… */ /* ACC-1: SCHEMA 10 → 11 only */ /* SCC decisions wave (DEC-133): 197643a1… → 5dc19a44… = the demo firm profile's fictional licence fields only */ /* SCC port wave (DEC-116, tax + leave): bf689538… → 197643a1… = compliance + leave slices, SCHEMA 9 → 10 */ /* old → new: ba4a1d4e… → 35e63082… = SCHEMA 8 → 9 only */;});
  test('[PPL] payroll and the holiday calendar (DEC-115, SCC Mgmt holidays): August has no unworked holiday; September (no paid holiday) is unchanged with every holiday retired; October loses 2 total days',()=>{
    const s=seed(),run=x(s).runs[0];assert.equal(run.month,'2026-08');for(const l of run.lines)assert.equal('hol' in l.counts,false);
    // MA-11 old → new: holidays of the locked August month cannot be retired any more (Wave B's lock rule) — retire every other one.
    let none=s;for(const h of mstRows(s,'holidays').filter(r=>!r.code.startsWith('2026-08')))none=execute(none,'owner',{type:'MST_MASTER_RETIRE',master:'holidays',id:h.id});
    assert.deepEqual(hrComputeRun(s,'2026-09'),hrComputeRun(none,'2026-09'));
    // October: 2 and 20 Oct are seeded paid holidays → SCC total days 31 − 2 = 29 (31 once they are retired).
    assert.ok(hrComputeRun(s,'2026-10').lines.every(l=>l.days.total===29&&l.counts.mgmtHol===2));assert.ok(hrComputeRun(none,'2026-10').lines.every(l=>l.days.total===31&&!l.counts.mgmtHol));});
  test('[PPL] hub search finds the lists by label',()=>{
    for(const q of ['shift patterns','leave types','holiday calendar','designations'])assert.ok(mstSearch(q).length,q);});

  // ── Permissions ──
  test('[PPL] permissions: Owner and Tech admin change operational lists; Reception, Manager, Accountant, Supervisor are refused; holidays and pay fields Owner-only',()=>{
    const row={code:'S',name:'Split',start:'10:00',end:'20:00'};
    for(const u of ['reception','manager','accountant','supervisor','housekeeper'])assert.throws(()=>save(seed(),u,'shiftPatterns',row),code('FORBIDDEN'),u);
    for(const u of ['owner','techadmin'])assert.ok(mstByCode(save(seed(),u,'shiftPatterns',row),'shiftPatterns','S'),u);
    assert.throws(()=>save(seed(),'techadmin','holidays',{code:'2026-12-31',name:'New year eve',kind:'paid'}),code('FORBIDDEN'));
    assert.ok(mstByCode(save(seed(),'owner','holidays',{code:'2026-12-31',name:'New year eve',kind:'paid'}),'holidays','2026-12-31'));
    const clId=mstByCode(seed(),'leaveTypes','CL').id;
    assert.throws(()=>save(seed(),'techadmin','leaveTypes',{id:clId,code:'CL',name:'Casual leave',paid:true,yearlyQuota:3}),code('FORBIDDEN'));// the quota is a pay rule: Owner
    assert.throws(()=>save(seed(),'techadmin','designations',{code:'CHEF',name:'Chef',dept:'KIT',defaultPayRule:'monthly-basic'}),code('FORBIDDEN'));
    assert.ok(mstByCode(save(seed(),'techadmin','designations',{code:'CHEF',name:'Chef',dept:'KIT'}),'designations','CHEF'));
    const s=save(seed(),'owner','leaveTypes',{id:clId,code:'CL',name:'Casual leave',paid:true,yearlyQuota:3});
    const ren=save(s,'techadmin','leaveTypes',{id:clId,code:'CL',name:'Casual',paid:true,yearlyQuota:3});assert.equal(hrLeaveName(ren,'CL'),'Casual');});

  // ── Shift patterns ──
  test('[PPL] shift pattern validation: code 1–3 not OFF, HH:MM times, overnight needs night, break shorter than the shift, duplicate times',()=>{
    const bad=(row,r='MASTER_INVALID')=>assert.throws(()=>save(seed(),'owner','shiftPatterns',{name:'X',start:'10:00',end:'18:00',...row}),reason(r));
    bad({code:'OFF'});bad({code:'ABCD'});bad({code:'X1',start:'25:00'});bad({code:'X1',start:'9:00'});bad({code:'X1',start:'10:00',end:'10:00'});
    bad({code:'X1',start:'20:00',end:'04:00'});bad({code:'X1',breakMinutes:600,end:'12:00'});bad({code:'X1',start:'07:00',end:'15:00',name:'Dup'});bad({code:'M',name:'Another'},'MASTER_CODE_TAKEN');
    const ok=save(seed(),'owner','shiftPatterns',{code:'X1',name:'Late night',start:'20:00',end:'04:00',night:true,breakMinutes:45});
    assert.equal(hrShiftMinutes(mstByCode(ok,'shiftPatterns','X1')),480);});
  test('[PPL] late check-in follows the pattern: start 08:00 → 08:10 on time, 08:20 late; night patterns are never late; OFF is on time',()=>{
    let s=seed();assert.equal(hrCheckinStatus('2026-10-24T08:20','M',s),'L');
    const id=mstByCode(s,'shiftPatterns','M').id;
    s=save(s,'owner','shiftPatterns',{id,code:'M',name:'Morning',start:'08:00',end:'16:00',breakMinutes:30,night:false,weeklyOffRule:'fixed'});
    assert.equal(hrCheckinStatus('2026-10-24T08:10','M',s),'P');assert.equal(hrCheckinStatus('2026-10-24T08:20','M',s),'L');
    assert.equal(hrCheckinStatus('2026-10-24T03:00','N',s),'P');assert.equal(hrCheckinStatus('2026-10-24T08:20','OFF',s),'P');
    assert.equal(hrShiftInfo('M',s).start,'08:00');assert.equal(hrShiftInfo('M').start,'07:00');// without state: built-in table
    s=save(s,'owner','shiftPatterns',{code:'W',name:'Watch',start:'20:00',end:'06:00',night:true});assert.equal(hrCheckinStatus('2026-10-24T22:00','W',s),'P');});
  test('[PPL] self check-in uses the pattern (Ravi, morning pattern moved to 06:00: a 06:55 check-in is late)',()=>{
    let s=seed();const id=mstByCode(s,'shiftPatterns','M').id;
    s=save(s,'owner','shiftPatterns',{id,code:'M',name:'Morning',start:'05:00',end:'13:00',breakMinutes:30,night:false,weeklyOffRule:'fixed'});
    s.clock='2026-10-25T06:30';s.date='2026-10-25';
    const out=execute(s,'reception',{type:'HR_ATT_MARK',staffId:'HR-S02'});// Aarav, morning shift, Sunday is not his off day (Tuesday)
    assert.equal(x(out).att['2026-10-25|HR-S02'].status,'L');});
  test('[PPL] roster and staff default shift pick active patterns only; a retired pattern stays on records that have it; retiring a default shift in use is refused',()=>{
    let s=save(seed(),'owner','shiftPatterns',{code:'S',name:'Split',start:'10:00',end:'20:00',breakMinutes:60});
    s=execute(s,'manager',{type:'HR_ROSTER_SET',staffId:'HR-S05',date:'2026-10-26',shift:'S'});assert.equal(hrShiftFor(s,staffRow(s,'HR-S05'),'2026-10-26'),'S');
    assert.throws(()=>execute(s,'manager',{type:'HR_ROSTER_SET',staffId:'HR-S05',date:'2026-10-26',shift:'ZZ'}),code('VALIDATION'));
    assert.deepEqual(hrShiftPatterns(s,{active:true}).map(p=>p.code),['M','E','N','G','S']);
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'shiftPatterns',id:mstByCode(s,'shiftPatterns','M').id}),reason('MASTER_IN_USE'));
    const sid=mstByCode(s,'shiftPatterns','S').id;
    s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'shiftPatterns',id:sid});assert.equal(hrShiftInfo('S',s).label,'Split');// retired still resolves
    assert.throws(()=>execute(s,'manager',{type:'HR_ROSTER_SET',staffId:'HR-S06',date:'2026-10-27',shift:'S'}),code('VALIDATION'));
    const ravi=staffRow(s,'HR-S05');assert.throws(()=>execute(s,'owner',{type:'HR_STAFF_SAVE',id:ravi.id,name:ravi.name,dept:ravi.dept,designation:ravi.designation,defaultShift:'S',weeklyOff:5}),code('VALIDATION'));
    assert.throws(()=>execute(seed(),'owner',{type:'HR_STAFF_SAVE',name:'New Person',dept:'FO',designation:'Clerk',defaultShift:'Q',weeklyOff:1}),code('VALIDATION'));});
  test('[PPL] a staff member can be saved on a custom pattern and the roster / list shows it; in-use count names staff and roster cells',()=>{
    let s=save(seed(),'owner','shiftPatterns',{code:'S',name:'Split',start:'10:00',end:'20:00'});
    s=execute(s,'owner',{type:'HR_STAFF_SAVE',name:'Test Clerk',dept:'FO',designation:'Clerk',defaultShift:'S',weeklyOff:2});
    const st=x(s).staff.at(-1);assert.equal(st.defaultShift,'S');assert.equal(hrShiftFor(s,st,'2026-10-27'),'OFF');
    const spec=mstSpec('shiftPatterns'),row=mstByCode(s,'shiftPatterns','S'),u=spec.inUse(s,row);assert.equal(u.count,1);assert.match(u.where[0],/Test Clerk/);
    s=execute(s,'manager',{type:'HR_ROSTER_SET',staffId:'HR-S05',date:'2026-10-26',shift:'S'});assert.equal(spec.inUse(s,row).count,2);});
  test('[PPL] weekly-off rule "roster": a staff member on that pattern has no automatic weekly off (rostered offs still work)',()=>{
    let s=seed();const ravi=staffRow(s,'HR-S05');assert.equal(hrShiftFor(s,ravi,'2026-10-30'),'OFF');// Friday = his weekly off
    const id=mstByCode(s,'shiftPatterns','M').id;
    s=save(s,'owner','shiftPatterns',{id,code:'M',name:'Morning',start:'07:00',end:'15:00',breakMinutes:30,night:false,weeklyOffRule:'roster'});
    assert.equal(hrWeeklyOffAuto(s,'M'),false);assert.equal(hrShiftFor(s,ravi,'2026-10-30'),'M');
    s=execute(s,'manager',{type:'HR_ROSTER_SET',staffId:'HR-S05',date:'2026-10-30',shift:'OFF'});assert.equal(hrShiftFor(s,staffRow(s,'HR-S05'),'2026-10-30'),'OFF');
    // SCC leave port: leave days follow the hotel's week-off set, not the roster (3 days either way).
    assert.equal(hrLeaveDays(seed(),'2026-10-29','2026-10-31'),3);assert.equal(hrLeaveDays(save(seed(),'owner','shiftPatterns',{id,code:'M',name:'Morning',start:'07:00',end:'15:00',weeklyOffRule:'roster'}),'2026-10-29','2026-10-31'),3);});
  test('[PPL] audit and history: "Master Shift patterns: start 07:00 → 08:00"; reorder; idempotent repeat; a later read sees the saved row (cache follows the write)',()=>{
    let s=seed();const id=mstByCode(s,'shiftPatterns','M').id;
    s=save(s,'owner','shiftPatterns',{id,code:'M',name:'Morning',start:'08:00',end:'15:00',breakMinutes:30,night:false,weeklyOffRule:'fixed'},'k-shift');
    assert.equal(s.audit[0].detail,'Master Shift patterns: start 07:00 → 08:00');assert.deepEqual(mstHistory(s,'shiftPatterns',id).map(h=>[h.field,h.old,h.new]),[['start','07:00','08:00']]);
    assert.equal(hrShiftPattern(s,'M').start,'08:00');
    assert.equal(save(s,'owner','shiftPatterns',{id,code:'M',name:'Morning',start:'08:00',end:'15:00'},'k-shift'),s);
    const ids=['G','E','N','M'].map(c=>mstByCode(s,'shiftPatterns',c).id);s=execute(s,'owner',{type:'MST_MASTER_REORDER',master:'shiftPatterns',ids});assert.deepEqual(hrShiftPatterns(s).map(p=>p.code),['G','E','N','M']);});

  // ── Leave types ──
  // SCC leave port (DEC-116): the list is SCC's six categories + LOP — fixed; only LOP is unpaid; only CL / SL / EL carry a quota (any number ≥ 0,
  // fractions allowed, no maximum); no carry-forward and no encashment.
  test('[PPL] leave type validation (SCC fixed list): no other code, paid fixed (LOP unpaid), quota on CL / SL / EL only, fractions allowed, no retiring',()=>{
    const bad=row=>assert.throws(()=>save(seed(),'owner','leaveTypes',{name:'X',paid:true,...row}),reason('MASTER_INVALID'));
    bad({code:'BL'});bad({code:'UL',paid:false});
    const id=c=>mstByCode(seed(),'leaveTypes',c).id;
    assert.throws(()=>save(seed(),'owner','leaveTypes',{id:id('LOP'),code:'LOP',name:'Leave without pay',paid:true}),reason('MASTER_INVALID'));
    assert.throws(()=>save(seed(),'owner','leaveTypes',{id:id('ML'),code:'ML',name:'Maternity',paid:true,yearlyQuota:5}),reason('MASTER_INVALID'));
    assert.throws(()=>save(seed(),'owner','leaveTypes',{id:id('CL'),code:'CL',name:'Casual leave',paid:true,yearlyQuota:-1}),reason('MASTER_INVALID'));
    assert.equal(hrLeaveQuota(save(seed(),'owner','leaveTypes',{id:id('CL'),code:'CL',name:'Casual leave',paid:true,yearlyQuota:12.5}),'CL'),12.5);
    assert.equal(hrLeaveQuota(save(seed(),'owner','leaveTypes',{id:id('CL'),code:'CL',name:'Casual leave',paid:true,yearlyQuota:90}),'CL'),90);
    for(const c of ['LOP','CL','OTH'])assert.throws(()=>execute(seed(),'owner',{type:'MST_MASTER_RETIRE',master:'leaveTypes',id:id(c)}),e=>/fixed/.test(e.message),c);
    // Never deleted either, even when no record uses it (the server refuses DeleteLeaveType for SCC's codes too).
    for(const c of ['ML','CO','OTH'])assert.throws(()=>execute(seed(),'owner',{type:'MST_MASTER_DELETE',master:'leaveTypes',id:id(c)}),e=>/cannot be deleted/.test(e.message),c);
    assert.deepEqual(hrLeaveChoices(seed(),'daily').map(r=>r.code),['CL','SL','EL','ML','CO','OTH','LOP']);});
  test('[PPL] leave records use the list: names from the list, an unknown category refused, the quota decides the balance warning',()=>{
    const id=c=>mstByCode(seed(),'leaveTypes',c).id;
    let s=save(seed(),'owner','leaveTypes',{id:id('OTH'),code:'OTH',name:'Bereavement / other',paid:true});
    const req=(st,sid,type,from,to,extra={})=>execute(st,'housekeeper',{type:'HR_LEAVE_REQUEST',approver:'manager',staffId:sid,leaveType:type,from,to,reason:'Family matter',...extra});
    const n=req(s,'HR-S05','OTH','2026-11-02','2026-11-03');assert.equal(x(n).leaves.at(-1).type,'OTH');assert.equal(hrLeaveName(n,'OTH'),'Bereavement / other');assert.equal(hrLeaveBalance(n,'HR-S05','2026-11-02').length,3);
    assert.throws(()=>req(s,'HR-S05','XX','2026-11-02','2026-11-03'),code('VALIDATION'));
    s=save(s,'owner','leaveTypes',{id:id('CL'),code:'CL',name:'Casual leave',paid:true,yearlyQuota:1});
    assert.throws(()=>req(s,'HR-S05','CL','2026-11-02','2026-11-03'),/EXCEEDS the balance/);assert.ok(req(s,'HR-S05','CL','2026-11-02','2026-11-03',{confirmOverBalance:true}));
    assert.equal(hrLeaveBalance(s,'HR-S05','2026-10-24').find(b=>b.type==='CL').entitled,1);});
  test('[PPL] leave without pay counts as loss of pay in payroll day counts (absent); every other category is paid leave; any staff member may take any category (SCC)',()=>{
    const s=seed();
    const lv=execute(s,'manager',{type:'HR_LEAVE_REQUEST',approver:'owner',staffId:'HR-S10',leaveType:'CL',from:'2026-11-04',to:'2026-11-04',reason:'Trip'});assert.equal(x(lv).leaves.at(-1).type,'CL');
    let t=structuredClone(s);const l={id:'T-1',staffId:'HR-S05',date:'2026-10-27',type:'LOP',from:'2026-10-27',to:'2026-10-27',days:1,reason:'x',status:'Approved',requestedBy:'manager',requestedAt:'2026-10-24T08:00'};
    t.staffX.leaves.push(l);delete t.staffX.att['2026-10-27|HR-S05'];
    const c=hrCounts(t,staffRow(t,'HR-S05'),'2026-10',undefined);assert.equal(c.lopLeave,1);assert.equal(c.paidLeave,0);
    for(const type of ['CL','OTH','ML','CO']){const paid=structuredClone(t);paid.staffX.leaves.at(-1).type=type;assert.equal(hrCounts(paid,staffRow(paid,'HR-S05'),'2026-10').paidLeave,1,type);}});
  test('[PPL] no carry-forward (SCC: every financial year starts at the quota) and no encashment rule',()=>{
    const s=seed();assert.equal(hrLeaveBalance(s,'HR-S05','2027-05-10').find(b=>b.type==='CL').entitled,12);
    assert.ok(!mstSpec('leaveTypes').fields.some(f=>f.key==='carryForward'||f.key==='encashment'));assert.equal(M.hrElEncashment,undefined);
    const old=structuredClone(s);old.mastersX={rows:{leaveTypes:mstRows(s,'leaveTypes').map(r=>r.code==='CL'?{...r,carryForward:3,encashment:true}:r)},history:[]};
    assert.equal(hrLeaveBalance(old,'HR-S05','2027-05-10').find(b=>b.type==='CL').entitled,12,'an old list’s carry-forward is ignored');});
  test('[PPL] HR_LEAVE_POLICY_SET writes the CL / SL / EL entitlements through to the Leave types list (history per type); the policy then reads the list',()=>{
    let s=execute(seed(),'owner',{type:'HR_LEAVE_POLICY_SET',ent:{CL:10,SL:7,EL:20.5}});
    assert.deepEqual([hrLeaveQuota(s,'CL'),hrLeaveQuota(s,'SL'),hrLeaveQuota(s,'EL')],[10,7,20.5]);assert.deepEqual(hrLeavePolicy(s).ent,{CL:10,SL:7,EL:20.5});
    assert.deepEqual(mstHistory(s,'leaveTypes').map(h=>[h.code,h.field,h.old,h.new]).sort(),[['CL','yearlyQuota','12','10'],['EL','yearlyQuota','15','20.5']]);
    s=save(s,'owner','leaveTypes',{id:mstByCode(s,'leaveTypes','SL').id,code:'SL',name:'Sick leave',paid:true,yearlyQuota:9});assert.equal(hrLeavePolicy(s).ent.SL,9);
    assert.throws(()=>execute(seed(),'techadmin',{type:'HR_LEAVE_POLICY_SET',ent:{CL:10,SL:7,EL:20}}),code('FORBIDDEN'));});

  // ── Holidays ──
  const unmarked=(s,sid,date)=>{const t=structuredClone(s);delete t.staffX.att[date+'|'+sid];return t;};
  test('[PPL] holiday validation: real date, one per date (retired rows keep their date), kind required; FY derived from the date',()=>{
    const bad=(row,r)=>assert.throws(()=>save(seed(),'owner','holidays',{name:'X',kind:'paid',...row}),reason(r));
    bad({code:'2026-02-30'},'MASTER_INVALID');bad({code:'2026-1-5'},'MASTER_INVALID');bad({code:'2026-11-08',name:'Another'},'MASTER_CODE_TAKEN');
    assert.throws(()=>save(seed(),'owner','holidays',{code:'2026-12-31',name:'New year eve'}),reason('MASTER_INVALID'));
    assert.equal(hrHolidayFy('2027-01-26'),'FY 2026–27');assert.equal(hrHolidayFy('2026-08-15'),'FY 2026–27');assert.equal(hrHolidayFy('2027-04-01'),'FY 2027–28');});
  test('[PPL] a paid holiday without attendance is a paid day for monthly staff (no loss of pay), not for daily-wage staff (DEC-083); explicit absence stays absent',()=>{
    let s=seed();// 2026-10-27 is Tuesday: Ravi (off Fri) works; Imran (daily, off Tue) → use Wed 28 for him
    const ravi=staffRow(s,'HR-S05'),imran=staffRow(s,'HR-S10');
    const open=unmarked(unmarked(s,'HR-S05','2026-10-21'),'HR-S10','2026-10-21');// Wed 21 Oct (Dussehra seeded 20th, Tue): use own holiday
    let h=save(open,'owner','holidays',{code:'2026-10-21',name:'Local holiday',kind:'paid'});
    const before=hrCounts(open,staffRow(open,'HR-S05'),'2026-10','2026-10-23'),after=hrCounts(h,staffRow(h,'HR-S05'),'2026-10','2026-10-23');
    assert.deepEqual([before.unmarked,before.hol],[1,undefined]);assert.deepEqual([after.unmarked,after.hol],[0,1]);
    const ln=(st,r)=>hrComputeLine(staffRow(st,r),hrCounts(st,staffRow(st,r),'2026-10'),M.hrRatesFor(st,'2026-10'));
    assert.ok(ln(h,'HR-S05').earned>ln(open,'HR-S05').earned);/* Masters integration — DEC-083 (owner decision, Wave B) is binding: an unworked holiday is NOT paid to daily-wage staff (like a weekly off, CA A04-04 open). Old: one more payable day. */assert.equal(ln(h,'HR-S10').payableDays-ln(open,'HR-S10').payableDays,0);
    const abs=structuredClone(h);abs.staffX.att['2026-10-21|HR-S05']={status:'A',at:'2026-10-21T09:00',otMin:0,by:'manager'};assert.equal(hrCounts(abs,staffRow(abs,'HR-S05'),'2026-10','2026-10-23').A-hrCounts(h,staffRow(h,'HR-S05'),'2026-10','2026-10-23').A,1);
    assert.equal(hrDayStatus(save(s,'owner','holidays',{code:'2026-10-21',name:'Local holiday',kind:'paid'}),ravi,'2026-10-21').kind,'att');assert.equal(hrDayStatus(h,ravi,'2026-10-21').kind,'holiday');void imran;});
  test('[PPL] a weekly off on a holiday stays a weekly off; a restricted holiday and a retired holiday change nothing',()=>{
    let s=seed();const ravi=staffRow(s,'HR-S05');// Friday 30 Oct = Ravi's weekly off
    let h=save(unmarked(s,'HR-S05','2026-10-30'),'owner','holidays',{code:'2026-10-30',name:'Bank holiday',kind:'paid'});
    assert.equal(hrDayStatus(h,ravi,'2026-10-30').kind,'off');
    const r=save(unmarked(s,'HR-S05','2026-10-21'),'owner','holidays',{code:'2026-10-21',name:'Optional day',kind:'restricted'});assert.equal(hrCounts(r,staffRow(r,'HR-S05'),'2026-10','2026-10-23').unmarked,1);
    const p=save(unmarked(s,'HR-S05','2026-10-21'),'owner','holidays',{code:'2026-10-21',name:'Local',kind:'paid'});
    const ret=execute(p,'owner',{type:'MST_MASTER_RETIRE',master:'holidays',id:mstByCode(p,'holidays','2026-10-21').id});
    assert.equal(hrHolidayPaid(p,'2026-10-21'),true);assert.equal(hrHolidayPaid(ret,'2026-10-21'),false);assert.equal(hrCounts(ret,staffRow(ret,'HR-S05'),'2026-10','2026-10-23').unmarked,1);
    assert.equal(hrHolidayOn(seed(),'2026-09-14').kind,'restricted');assert.equal(hrHolidayPaid(seed(),'2026-09-14'),false);assert.ok(hrHolidayDates(seed()).has('2026-11-08'));});
  test('[PPL] SCC leave port: leave days do NOT skip paid holidays (SCC store holidays are blackout markers); leave on a holiday is recorded',()=>{
    const s=seed();assert.equal(hrLeaveDays(s,'2026-11-07','2026-11-09'),3);// 8 Nov Diwali (demo holiday) counts
    const st=execute(s,'housekeeper',{type:'HR_LEAVE_REQUEST',approver:'manager',staffId:'HR-S05',leaveType:'CL',from:'2026-11-07',to:'2026-11-09',reason:'Festival'});
    assert.deepEqual(x(st).leaves.filter(l=>l.staffId==='HR-S05'&&l.status==='Pending').map(l=>l.date),['2026-11-07','2026-11-08','2026-11-09']);
    assert.ok(execute(s,'housekeeper',{type:'HR_LEAVE_REQUEST',approver:'manager',staffId:'HR-S05',leaveType:'CL',from:'2026-11-08',to:'2026-11-08',half:'AM',reason:'Festival'}));});
  test('[PPL] holidays used by locked pay-runs: the date cannot change, the holiday cannot be deleted or retired (MA-11: Wave B lock rule)',()=>{
    const s=seed(),row=mstByCode(s,'holidays','2026-08-15'),u=mstSpec('holidays').inUse(s,row);assert.equal(u.count,1);
    assert.throws(()=>save(s,'owner','holidays',{id:row.id,code:'2026-08-16',name:'Independence Day',kind:'paid'}),reason('MASTER_IN_USE'));
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_DELETE',master:'holidays',id:row.id}),reason('MASTER_IN_USE'));
    // MA-11 old → new: retire allowed → refused while August 2026 is locked (same rule and words as HR_HOLIDAY_SAVE / _REMOVE).
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'holidays',id:row.id}),e=>/August 2026 payroll is locked — its holidays cannot change\./.test(e.message));
    assert.throws(()=>save(s,'owner','holidays',{id:row.id,code:row.code,name:'Renamed',kind:'paid'}),e=>/payroll is locked/.test(e.message));
    assert.throws(()=>save(s,'owner','holidays',{code:'2026-08-20',name:'New paid day',kind:'paid'}),e=>/payroll is locked/.test(e.message));
    assert.ok(mstByCode(save(s,'owner','holidays',{code:'2026-08-21',name:'Optional day',kind:'restricted'}),'holidays','2026-08-21'));
    const fresh=mstByCode(s,'holidays','2026-12-25');assert.equal(mstSpec('holidays').inUse(s,fresh).count,0);assert.ok(execute(s,'owner',{type:'MST_MASTER_DELETE',master:'holidays',id:fresh.id}));});
  test('[PPL] SCC tax port (DEC-116): compliance due dates no longer roll past paid holidays — SCC never moves a due date',()=>{
    const s=seed(),x=cmpSlice(s),v=cmpView(x,cmpFindCard(x,'ag5'),'August',2026,s.date);
    assert.ok(hrHolidayDates(s).has('2026-08-15'));assert.equal(v.due,'2026-08-15');// EPF July wages: 15 Aug is a paid holiday, the date stays
    assert.equal(cmpParseDue('15 Aug',2026),'2026-08-15');assert.equal(typeof M.cmpShiftDue,'undefined');});

  // ── Designations ──
  test('[PPL] designation seed codes are unique and valid; the staff form choices follow the department',()=>{
    const s=seed(),rows=mstRows(s,'designations');assert.equal(new Set(rows.map(r=>r.code)).size,rows.length);for(const r of rows)assert.match(r.code,/^[A-Z0-9-]{1,20}$/);
    assert.deepEqual(hrDesignationChoices(s,'KIT').map(r=>r.name).sort(),['Cook','Kitchen helper (daily wage)']);
    assert.deepEqual(rows.find(r=>r.name==='Kitchen helper (daily wage)').defaultPayRule,'daily');assert.deepEqual(hrPayRuleFields('daily'),{payType:'daily',structure:'B'});
    assert.equal(hrSeedDesignations(s).length,rows.length);});
  test('[PPL] designation validation: unknown department refused; Owner sets a pay rule; renaming keeps staff linked by code',()=>{
    assert.throws(()=>save(seed(),'owner','designations',{code:'CHEF',name:'Chef',dept:'ZZ'}),reason('MASTER_INVALID'));
    let s=save(seed(),'owner','designations',{code:'CHEF',name:'Chef',dept:'KIT',grade:'G3',defaultPayRule:'monthly-structured'});
    s=execute(s,'owner',{type:'HR_STAFF_SAVE',name:'Test Chef',dept:'KIT',designation:'ignored',designationCode:'CHEF',defaultShift:'M',weeklyOff:1});
    const chef=x(s).staff.at(-1);assert.deepEqual([chef.designation,chef.designationCode],['Chef','CHEF']);
    const row=mstByCode(s,'designations','CHEF'),u=mstSpec('designations').inUse(s,row);assert.equal(u.count,1);
    s=save(s,'owner','designations',{id:row.id,code:'CHEF',name:'Head chef',dept:'KIT',grade:'G3',defaultPayRule:'monthly-structured'});
    assert.equal(hrDesignationOf(s,staffRow(s,chef.id)).name,'Head chef');assert.equal(staffRow(s,chef.id).designation,'Chef');// printed name keeps the old text until the record is saved
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_DELETE',master:'designations',id:row.id}),reason('MASTER_IN_USE'));});
  test('[PPL] staff save with a designation code: department must match, retired designations are refused for new choices, free text still works',()=>{
    let s=save(seed(),'owner','designations',{code:'CHEF',name:'Chef',dept:'KIT'});
    assert.throws(()=>execute(s,'owner',{type:'HR_STAFF_SAVE',name:'Test Chef',dept:'FO',designation:'x',designationCode:'CHEF',defaultShift:'M',weeklyOff:1}),code('VALIDATION'));
    assert.throws(()=>execute(s,'owner',{type:'HR_STAFF_SAVE',name:'Test Chef',dept:'KIT',designation:'x',designationCode:'NOPE',defaultShift:'M',weeklyOff:1}),code('VALIDATION'));
    const r=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'designations',id:mstByCode(s,'designations','CHEF').id});
    assert.throws(()=>execute(r,'owner',{type:'HR_STAFF_SAVE',name:'Test Chef',dept:'KIT',designation:'x',designationCode:'CHEF',defaultShift:'M',weeklyOff:1}),code('INVALID_TRANSITION'));
    const free=execute(seed(),'owner',{type:'HR_STAFF_SAVE',name:'Free Text',dept:'FO',designation:'Night auditor',defaultShift:'N',weeklyOff:3});assert.equal(x(free).staff.at(-1).designation,'Night auditor');
    const sav=execute(seed(),'owner',{type:'HR_STAFF_SAVE',id:'HR-S09',name:'Suresh Pawar',dept:'KIT',designation:'Senior cook',defaultShift:'M',weeklyOff:3});assert.equal(staffRow(sav,'HR-S09').designation,'Senior cook');
    assert.match(sav.audit[0].detail,/designation Cook → Senior cook/);});

  // ── UI wiring, production mapping, sources ──
  test('[PPL] UI: staff form picks designation and default shift from the lists with "Manage list…" links; roster, leave policy and attendance use the lists',()=>{
    const m=src('src/features/staff/modals.tsx'),h=src('src/features/staff/HrDesk.tsx'),my=src('src/features/staff/MyHr.tsx'),lv=src('src/features/staff/Leave.tsx'),lm=src('src/features/staff/leaveModals.tsx');
    assert.match(m,/hrDesignationChoices\(s,f\.dept,f\.designationCode\)/);assert.match(m,/<MstLink spec="designations"\/>/);assert.match(m,/<MstLink spec="shiftPatterns"\/>/);assert.match(m,/hrShiftChoices\(s,f\.defaultShift\)/);
    assert.match(lm,/hrLeaveChoices\(s\)/);assert.match(lv,/<MstLink spec="leaveTypes"\/> <MstLink spec="holidays"\/>/);assert.match(h,/dayPill\(x\)/);/* masters integration: today's pill = Wave B dayPill, which names the paid holiday (old: hrHolidayOn(s,s.date)) */
    assert.match(my,/hrCheckinStatus\(s\.clock,shift!,s\)/);assert.match(my,/x\.kind==='holiday'\?'Holiday'/);});
  test('[PPL] production mapping: the four lists are in MST_SERVER with their server routes and commands; capabilities and read gates know the routes',()=>{
    const a=src('src/clients/sync/masters.ts');
    for(const n of ['SaveDesignation','RetireDesignation','ReactivateDesignation','ReorderDesignations','DeleteDesignation','SaveShiftPattern','SaveLeaveType','SaveHoliday','ReorderHolidays'])assert.ok(a.includes(n),n);
    for(const p of ['/hr/designations','/hr/shift-patterns','/hr/leave-types','/hr/holidays'])assert.ok(a.includes(p),p);
    const r=src('src/clients/sync/reads.ts');for(const p of ['GET /hr/designations','GET /hr/shift-patterns','GET /hr/leave-types','GET /hr/holidays'])assert.ok(r.includes(p),p);});
  test('[PPL] navigation unchanged: no new page; the lists appear in the Lists block of the "people" group (desktop and phone hubs)',()=>{
    assert.doesNotMatch(src('src/app/navmap.ts'),/designations|holidays/);assert.equal(mstSpecsOfGroup('people').length,4);assert.ok(HR_SHIFTS.some(h=>h.code==='OFF'));
    assert.match(src('src/features/admin/HotelSettings.tsx'),/<MstListsBlock key=\{g\} g=\{g\} q=\{q\}\/>/);});
}
