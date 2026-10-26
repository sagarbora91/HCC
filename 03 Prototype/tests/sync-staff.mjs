// W4-6 staff, payroll and leave (sync/staff.ts) on a fake .NET API. The fake board is built FROM the prototype's own state (BASE + the
// September draft prepared through M.execute), converted to the server DTO shapes (Hcc.Contracts.Payroll, ids numeric, money paise,
// times UTC). The loader must give back the prototype's figures (lines, rates, attendance, leave, advances) unchanged; every HR_* command
// must send the right server command, body, Idempotency-Key and expectedVersion, then reload staffX.
import {loadSync,loadPages,fakeServer,startRuntime,capabilities} from './_sync-harness.mjs';

const USERS=['owner','manager','reception','cashier','supervisor','housekeeper','housekeeper2','accountant','maint','techadmin'];
const uidNum=u=>u==null?null:USERS.indexOf(u)+1;
const mask=d=>{const x=String(d??'').replace(/\D/g,'');return x?'••••'+x.slice(-4):'(none)';};

/** Prototype state → the server's /payroll/staff + /payroll/board answers (as the owning payroll user, or as one staff member). */
function toServer(M,S,P,{self}={}){
  const x=P.staffX,sid=new Map(x.staff.map((st,i)=>[st.id,101+i])),rid=new Map(x.runs.map((r,i)=>[r.id,501+i])),lid=new Map(x.leaves.map((l,i)=>[l.id,301+i]));
  const utc=c=>S.clockToUtc(c);
  const rates=r=>({from:r.from,basicBps:r.basicBps,hraBps:r.hraBps,washBps:r.washBps,pfEeBps:r.pfEeBps,pfErBps:r.pfErBps,pfWageCap:r.pfWageCap,esicEeBps:r.esicEeBps,
    esicErBps:r.esicErBps,esicCeiling:r.esicCeiling,ptMaleExempt:r.ptMaleExempt,ptMaleMid:r.ptMaleMid,ptMaleMidAmt:r.ptMaleMidAmt,ptStd:r.ptStd,ptFeb:r.ptFeb,
    ptFemaleExempt:r.ptFemaleExempt,otBps:r.otBps,lateBlock:r.lateBlock,rounding:r.rounding,esicPeriod:!!r.esicPeriod});
  const line=l=>({staffId:sid.get(l.staffId),name:l.name,dept:l.dept,designation:l.designation,payType:l.payType,structure:l.structure,gender:l.gender,basePaise:l.base,
    counts:{ym:l.counts.ym,days:l.counts.days,p:l.counts.P,l:l.counts.L,h:l.counts.H,hl:l.counts.HL,a:l.counts.A,offw:l.counts.OFFW,off:l.counts.off,paidLeave:l.counts.paidLeave,
      lopLeave:l.counts.lopLeave,unmarked:l.counts.unmarked,na:l.counts.na,otMin:l.counts.otMin},latePenaltyHalf:l.latePenaltyHalf,payableDays:l.payableDays,
    earnedPaise:l.earned,basicPaise:l.basic,hraPaise:l.hra,washingPaise:l.washing,specialPaise:l.special,otPaise:l.ot,grossPaise:l.gross,pfWagesPaise:l.pfWages,pfPaise:l.pf,
    pfErPaise:l.pfEr,esicWagesPaise:l.esicWages,esicPaise:l.esic,esicErPaise:l.esicEr,ptPaise:l.pt,advancePaise:l.advance,deductionsPaise:l.deductions,netPaise:l.net,
    ctcPaise:l.ctc,esicCovered:l.esicCovered??null});
  const me=self?x.staff.find(st=>st.userId===self):undefined,pay=!self,vis=st=>!self||st.id===me?.id;
  const staff=x.staff.filter(vis).map(st=>{const showPay=pay||st.id===me?.id;return {staffId:sid.get(st.id),name:st.name,dept:st.dept,designation:st.designation,
    defaultShift:st.defaultShift,weeklyOff:st.weeklyOff,joined:st.joined,exitedOn:st.exitedOn??null,active:st.active,linked:!!st.userId,phoneMasked:mask(st.phone),
    version:'v-staff-'+sid.get(st.id),payType:showPay?st.payType:null,monthlyPaise:showPay?st.monthly:null,dailyPaise:showPay?st.daily:null,
    gender:pay?M.hrGenderOf(st):null,structure:pay?M.hrStructureOf(st):null,pfApplicable:pay?M.hrPfOn(st):null,esicApplicable:pay?M.hrEsicOn(st):null,
    bankName:pay?st.bankName??null:null,accountMasked:pay?mask(st.accountNo):null,linkedUserId:uidNum(st.userId)};});
  const visId=id=>vis(x.staff.find(st=>st.id===id));
  const runs=x.runs.filter(r=>pay||r.status==='Locked').map(r=>({runId:rid.get(r.id),month:r.month,status:r.status,rates:rates(r.rates),warnings:pay?r.warnings:[],
    lockSeq:r.status==='Locked'?1:0,version:'v-run-'+rid.get(r.id),preparedBy:uidNum(r.preparedBy),preparedUtc:utc(r.preparedAt),lockedBy:uidNum(r.lockedBy),
    lockedUtc:r.lockedAt?utc(r.lockedAt):null,lines:r.lines.filter(l=>pay||l.staffId===me?.id).map(line),sends:[],bankFiles:[],unlocks:[]})).filter(r=>r.lines.length);
  const board={businessDate:P.date,from:'2026-04-01',to:'2026-12-19',allStaff:pay,payroll:pay,departments:x.depts.map(d=>({code:d.id,name:d.name,leaveCap:x.leavePolicy?.deptCap?.[d.id]??null})),
    leavePolicy:{cl:x.leavePolicy.ent.CL,sl:x.leavePolicy.ent.SL,el:x.leavePolicy.ent.EL,defaultCap:x.leavePolicy.defaultCap},
    roster:Object.entries(x.roster).filter(([k])=>visId(k.split('|')[1])).map(([k,v])=>({staffId:sid.get(k.split('|')[1]),date:k.split('|')[0],shift:v})),
    attendance:Object.entries(x.att).filter(([k])=>visId(k.split('|')[1])).map(([k,a])=>({staffId:sid.get(k.split('|')[1]),date:k.split('|')[0],status:a.status,otMinutes:a.otMin,
      markedBy:uidNum(a.by),markedUtc:utc(a.at),note:a.note??null})),
    leaves:x.leaves.filter(l=>visId(l.staffId)).map(l=>({leaveId:lid.get(l.id),staffId:sid.get(l.staffId),leaveType:l.type,from:l.from,to:l.to,half:l.half??null,days:l.days,reason:l.reason,
      status:l.status,requestedBy:uidNum(l.requestedBy),requestedUtc:utc(l.requestedAt),decidedBy:uidNum(l.decidedBy),decidedUtc:l.decidedAt?utc(l.decidedAt):null,note:l.note??null,
      version:'v-leave-'+lid.get(l.id)})),
    rateVersions:pay?x.rateVersions.map(rates):[],runs,
    advances:pay?x.advances.map((a,i)=>({advanceId:401+i,staffId:sid.get(a.staffId),amountPaise:a.amount,installmentPaise:a.installment,recoveredPaise:a.recovered,date:a.date,
      fromMonth:a.fromMonth??a.date.slice(0,7),reason:a.reason,givenBy:uidNum(a.by),recoveries:a.recoveries.map(q=>({runId:rid.get(q.runId)??null,fnfId:null,amountPaise:q.amount}))})):[],
    fnf:[]};
  return {staff,board,sid,rid,lid};
}
/** Prototype HRState with its ids rewritten to the server ids (to compare with what the loader produced). */
function remap(x,{sid,rid,lid}){
  const s=id=>String(sid.get(id)),k=key=>{const [d,id]=key.split('|');return d+'|'+s(id);};
  const line=l=>({...l,staffId:s(l.staffId)});
  return {roster:Object.fromEntries(Object.entries(x.roster).map(([a,v])=>[k(a),v])),
    att:Object.fromEntries(Object.entries(x.att).map(([a,v])=>[k(a),{...v,by:String(uidNum(v.by))}])),
    leaves:x.leaves.map(l=>({...l,id:String(lid.get(l.id)),staffId:s(l.staffId),requestedBy:String(uidNum(l.requestedBy)),...(l.decidedBy?{decidedBy:String(uidNum(l.decidedBy))}:{})})),
    runs:x.runs.map(r=>({id:String(rid.get(r.id)),month:r.month,status:r.status,rates:r.rates,lines:r.lines.map(line),warnings:r.warnings,preparedAt:r.preparedAt})),
    advances:x.advances.map((a,i)=>({...a,id:String(401+i),staffId:s(a.staffId),by:String(uidNum(a.by)),fromMonth:a.fromMonth??a.date.slice(0,7),
      recoveries:a.recoveries.map(q=>({runId:String(rid.get(q.runId)),amount:q.amount}))}))};
}

export async function register({test,assert,M}){
  const S=await loadSync();
  // Only this area (+ core, reservations, frontdesk: bookings needs guests): other W4 areas load their own routes, which this fake server does not serve.
  const mine={areas:S.AREAS.filter(a=>['core','reservations','frontdesk','staff'].includes(a.id))};
  const P=M.execute(M.seed(),'accountant',{type:'HR_PAYROLL_RUN',month:'2026-09'});
  const srvData=toServer(M,S,P);
  const ok=name=>({body})=>({name,...body,version:'v-new'});
  const srv=fakeServer({
    'GET /payroll/staff':()=>srvData.staff,
    'GET /payroll/board':()=>srvData.board,
    'POST /commands/SaveStaff':({body})=>({staffId:body.staffId??113,name:body.name,version:'v2',notes:'saved'}),
    ...Object.fromEntries(['SetStaffActive','SetRoster','CopyRosterWeek','MarkAttendance','RequestLeave','DecideLeave','CancelLeave','SetLeavePolicy','SaveDepartment',
      'GiveAdvance','SetStatutoryRates','PreparePayRun','LockPayRun','UnlockPayRun','IssueBankFile','SendPayslip','PrepareFnf','ConfirmFnf','DiscardFnf'].map(n=>['POST /commands/'+n,ok(n)])),
  });
  const rt=await startRuntime(S,srv,mine);
  const st=rt.get().state,x=st.staffX;
  const want=remap(P.staffX,srvData);
  const sep=x.runs.find(r=>r.month==='2026-09');
  const fresh=M.hrComputeRun(st,'2026-09');
  test('[W4-6 staff] staffX loads from GET /payroll/staff + /payroll/board with the prototype’s own figures (stored lines, rates, attendance, leave, advances)',()=>{
    assert.deepEqual(srv.unrouted,[]);
    assert.equal(x.staff.length,12);assert.deepEqual(x.depts,P.staffX.depts);assert.deepEqual(x.leavePolicy,P.staffX.leavePolicy);
    assert.deepEqual(x.rateVersions,P.staffX.rateVersions);
    assert.deepEqual(x.runs.map(r=>({id:r.id,month:r.month,status:r.status,rates:r.rates,lines:r.lines,warnings:r.warnings,preparedAt:r.preparedAt})),want.runs);
    assert.deepEqual(x.roster,want.roster);assert.deepEqual(x.att,want.att);assert.deepEqual(x.leaves,want.leaves);assert.deepEqual(x.advances,want.advances);
    const ravi=x.staff.find(s=>s.name.startsWith('Ravi'));
    assert.equal(ravi.userId,'6');assert.match(ravi.phone,/^••••\d{4}$/);assert.match(ravi.accountNo,/^••••\d{4}$/,'masked fields stay masked');
    assert.equal(rt.ids.version('staff',ravi.id),'v-staff-'+ravi.id);assert.equal(rt.ids.version('run',sep.id),'v-run-'+sep.id);
  });
  test('[W4-6 staff] Server lines are what the payroll screen compares against: a client recalculation of the same data finds no change (no false “recalculate” warning)',()=>{
    assert.equal(JSON.stringify(fresh.lines),JSON.stringify(sep.lines));
    assert.equal(JSON.stringify(M.hrRatesFor(st,'2026-09')),JSON.stringify(sep.rates));
    assert.equal(M.hrRunTotals(sep).net,M.hrRunTotals(P.staffX.runs.find(r=>r.month==='2026-09')).net);
  });
  const P_=await loadPages();const shown=[P_.renderPages(st,'1',false),P_.renderPages(st,'1',true)];
  // A housekeeper: own record and own locked line only (DEC-061).
  const hkData=toServer(M,S,P,{self:'housekeeper'});
  const hkSrv=fakeServer({'GET /me/capabilities':()=>capabilities({userId:6,displayName:'Ravi',roles:['HKP'],grants:[['PERM-HK-001','A']]}),
    'GET /payroll/staff':()=>hkData.staff,'GET /payroll/board':()=>hkData.board,'POST /commands/MarkAttendance':ok('MarkAttendance')});
  const hk=await startRuntime(S,hkSrv,mine);const hs=hk.get().state;
  const slips=M.hrVisiblePayslips(hs,'6');
  const hkShown=[P_.renderPages(hs,'6',false),P_.renderPages(hs,'6',true)];
  hkSrv.reset();const checkin=await hk.run({type:'HR_ATT_MARK',staffId:String(hkData.sid.get('HR-S05')),key:'k-checkin'});
  test('[W4-6 staff] Every desktop and phone page renders on server data; the housekeeper sees only his own record and own locked payslip (DEC-061) and checks himself in',()=>{
    assert.deepEqual(shown.map(r=>r.errors),[{},{}]);assert.deepEqual(hkShown.map(r=>r.errors),[{},{}]);
    assert.equal(hs.staffX.staff.length,1);assert.equal(M.hrStaffOfUser(hs,'6').name.split(' ')[0],'Ravi');
    assert.deepEqual(slips.map(p=>[p.run.month,p.line.staffId]),[['2026-08',String(hkData.sid.get('HR-S05'))]]);
    assert.ok(hs.staffX.runs.every(r=>r.lines.length===1&&r.status==='Locked'));
    assert.ok(checkin.ok,checkin.text);
    assert.deepEqual(hkSrv.commands('MarkAttendance')[0].body,{staffId:hkData.sid.get('HR-S05')},'self check-in: no status, the server clock decides');
    assert.equal(hkSrv.commands('MarkAttendance')[0].key,'k-checkin');
    assert.ok(hkSrv.gets('/payroll/board').length>=1);
  });

  // ── Commands ──
  const ravi=String(srvData.sid.get('HR-S05')),meera=String(srvData.sid.get('HR-S01')),aug=String(srvData.rid.get('HR-PAY-1')),leave=String(srvData.lid.get('HR-LV-2'));
  const rv=x.staff.find(s=>s.id===ravi);
  const cases=[
    [{type:'HR_STAFF_SAVE',id:ravi,name:rv.name,dept:rv.dept,designation:rv.designation,userId:rv.userId,defaultShift:'E',weeklyOff:rv.weeklyOff,phone:rv.phone,gender:rv.gender,
      payType:'monthly',monthly:rv.monthly,daily:0,esicApplicable:true,bankName:rv.bankName,accountNo:rv.accountNo,ifsc:''},'SaveStaff',
      {staffId:+ravi,name:rv.name,dept:rv.dept,designation:rv.designation,defaultShift:'E',weeklyOff:rv.weeklyOff,linkUserId:6,gender:rv.gender,payType:'monthly',monthlyPaise:rv.monthly,
        dailyPaise:0,esicApplicable:true},'v-staff-'+ravi],
    [{type:'HR_STAFF_SAVE',name:'New Cook',dept:'KIT',designation:'Cook',defaultShift:'M',weeklyOff:2,phone:'90000 55555',gender:'M'},'SaveStaff',
      {staffId:null,name:'New Cook',dept:'KIT',designation:'Cook',defaultShift:'M',weeklyOff:2,linkUserId:null,phone:'90000 55555',gender:'M'},undefined],
    [{type:'HR_STAFF_ACTIVE',id:ravi,active:false},'SetStaffActive',{staffId:+ravi,active:false},'v-staff-'+ravi],
    [{type:'HR_ROSTER_SET',staffId:ravi,date:'2026-10-27',shift:'N'},'SetRoster',{staffId:+ravi,date:'2026-10-27',shift:'N'}],
    [{type:'HR_ROSTER_COPY',from:'2026-10-26'},'CopyRosterWeek',{week:'2026-10-26'}],
    [{type:'HR_ATT_MARK',staffId:ravi,status:'P',otMin:45,note:' Late checkout cover '},'MarkAttendance',{staffId:+ravi,status:'P',otMinutes:45,note:'Late checkout cover'}],
    [{type:'HR_LEAVE_REQUEST',staffId:ravi,leaveType:'CL',from:'2026-11-05',to:'2026-11-05',half:'AM',reason:'Clinic visit'},'RequestLeave',
      {staffId:+ravi,leaveType:'CL',from:'2026-11-05',to:null,half:'AM',reason:'Clinic visit'}],
    [{type:'HR_LEAVE_DECIDE',id:leave,approve:false,note:'Peak week'},'DecideLeave',{leaveId:+leave,approve:false,note:'Peak week'},'v-leave-'+leave],
    [{type:'HR_LEAVE_CANCEL',id:leave},'CancelLeave',{leaveId:+leave},'v-leave-'+leave],
    [{type:'HR_ADVANCE_ADD',staffId:meera,amount:500000,installment:100000,fromMonth:'2026-11',reason:'Medical'},'GiveAdvance',
      {staffId:+meera,amountPaise:500000,installmentPaise:100000,reason:'Medical',fromMonth:'2026-11'}],
    [{type:'HR_STAT_RATES_SET',from:'2026-11-01',pfEeBps:1200,esicPeriod:false,rounding:'rupee'},'SetStatutoryRates',{effectiveFrom:'2026-11-01',pfEeBps:1200,rounding:'rupee',esicPeriod:false}],
    [{type:'HR_PAYROLL_RUN',month:'2026-09'},'PreparePayRun',{month:'2026-09'}],
    [{type:'HR_PAYROLL_LOCK',id:sep.id},'LockPayRun',{runId:+sep.id},'v-run-'+sep.id],
    [{type:'HR_PAYROLL_UNLOCK',id:aug,reason:'Overtime correction',confirmBankFile:true},'UnlockPayRun',{runId:+aug,reason:'Overtime correction',confirmBankFile:true},'v-run-'+aug],
    [{type:'HR_PAYROLL_BANK_FILE',id:aug,confirmReissue:true},'IssueBankFile',{runId:+aug,confirmReissue:true}],
    [{type:'HR_PAYSLIP_SEND',runId:aug,staffId:ravi},'SendPayslip',{runId:+aug,staffId:+ravi}],
    [{type:'HR_FNF_SETTLE',staffId:ravi,adjustments:[{label:'Earned leave encashment',kind:'add',amount:250000}]},'PrepareFnf',
      {staffId:+ravi,adjustments:[{label:'Earned leave encashment',kind:'add',amountPaise:250000}]}],
  ];
  const sent=[];
  for(const [i,[cmd,name,body,ver]] of cases.entries()){srv.reset();const r=await rt.run({...cmd,key:'k-'+i});
    sent.push({r,name,body,ver,call:srv.commands(name)[0],reloaded:srv.gets('/payroll/board').length,key:'k-'+i});}
  // Leave policy: entitlements + the default limit, then one SaveDepartment per department whose own limit changed (step keys).
  srv.reset();const pol=await rt.run({type:'HR_LEAVE_POLICY_SET',ent:{CL:10,SL:7,EL:15},defaultCap:3,deptCap:{HK:1,FO:''},key:'k-pol'});
  const polCalls=srv.calls.filter(c=>c.method==='POST').map(c=>[c.path,c.key,c.body]);
  // F&F confirm / discard need a loaded settlement version.
  srvData.board.fnf=[{fnfId:701,staffId:+ravi,lastDay:'2026-10-24',month:'2026-10',salaryIncluded:true,line:null,rates:srvData.board.rateVersions[0],advanceOutstandingPaise:200000,
    adjustments:[{label:'Notice pay',kind:'ded',amountPaise:50000}],earningsPaise:0,recoveriesPaise:250000,netPaise:-250000,status:'Draft',preparedBy:8,
    preparedUtc:'2026-10-24T05:00:00.000Z',decidedBy:null,decidedUtc:null,version:'v-fnf-701'}];
  await rt.refresh();const f=rt.get().state.staffX.fnf;
  srv.reset();const conf=await rt.run({type:'HR_FNF_CONFIRM',id:'701',key:'k-fc'});const disc=await rt.run({type:'HR_FNF_DISCARD',id:'701',key:'k-fd'});
  const stale=await rt.run({type:'HR_LEAVE_CANCEL',id:'999',key:'k-stale'});
  test('[W4-6 staff] Each HR_* command sends its server command with the dialog key, the body in server terms and the loaded version, then reloads staffX',()=>{
    for(const s of sent){
      assert.ok(s.r.ok,`${s.name}: ${s.r.text}`);assert.ok(s.call,`${s.name} not sent`);
      assert.equal(s.call.key,s.key,s.name);
      const {expectedVersion,...b}=s.call.body;assert.deepEqual(b,s.body,s.name);assert.equal(expectedVersion,s.ver,s.name+' expectedVersion');
      assert.ok(s.reloaded>=1,s.name+' reloads staffX');
    }
  });
  test('[W4-6 staff] Masked phone / account number and an unloaded IFSC are never sent back; leave policy saves department limits as separate steps; F&F maker-checker uses the settlement version',()=>{
    const save=sent[0].call.body;assert.ok(!('phone' in save)&&!('accountNo' in save)&&!('ifsc' in save)&&!('bankName' in save),JSON.stringify(save));
    assert.ok(pol.ok,pol.text);
    assert.deepEqual(polCalls,[['/commands/SetLeavePolicy','k-pol',{cl:10,sl:7,el:15,defaultCap:3}],['/commands/SaveDepartment','k-pol:dept:HK',{code:'HK',name:'Housekeeping',leaveCap:1}]]);
    assert.deepEqual(f.map(z=>[z.id,z.name.split(' ')[0],z.status,z.net,z.by,z.adjustments[0].kind]),[['701','Ravi','Draft',-250000,'8','ded']]);
    assert.ok(conf.ok&&disc.ok);
    assert.deepEqual(srv.commands('ConfirmFnf').map(c=>[c.body,c.key]),[[{fnfId:701,expectedVersion:'v-fnf-701'},'k-fc']]);
    assert.deepEqual(srv.commands('DiscardFnf').map(c=>c.body),[{fnfId:701,expectedVersion:'v-fnf-701'}]);
    assert.deepEqual([stale.ok,stale.reason],[false,'server']);assert.match(stale.text,/Refresh and try again/);
    assert.deepEqual(S.AREAS.find(a=>a.id==='staff').unmapped,{});
    for(const t of ['HR_STAFF_SAVE','HR_STAFF_ACTIVE','HR_ROSTER_SET','HR_ROSTER_COPY','HR_ATT_MARK','HR_LEAVE_REQUEST','HR_LEAVE_DECIDE','HR_LEAVE_CANCEL','HR_LEAVE_POLICY_SET',
      'HR_ADVANCE_ADD','HR_STAT_RATES_SET','HR_RATES_SET','HR_PAYROLL_RUN','HR_PAYROLL_LOCK','HR_PAYROLL_UNLOCK','HR_PAYROLL_BANK_FILE','HR_PAYSLIP_SEND','HR_FNF_SETTLE',
      'HR_FNF_CONFIRM','HR_FNF_DISCARD'])assert.ok(rt.mapped(t),t);
  });
}
