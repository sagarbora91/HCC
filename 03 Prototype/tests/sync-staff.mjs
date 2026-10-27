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
    ptFemaleExempt:r.ptFemaleExempt,otBps:r.otBps,lateBlock:r.lateBlock,rounding:r.rounding,esicPeriod:!!r.esicPeriod,daysOverride:r.daysOverride??0});
  const line=l=>({staffId:sid.get(l.staffId),name:l.name,dept:l.dept,designation:l.designation,payType:l.payType,structure:l.structure,gender:l.gender,basePaise:l.base,
    counts:{ym:l.counts.ym,days:l.counts.days,p:l.counts.P,l:l.counts.L,h:l.counts.H,hl:l.counts.HL,a:l.counts.A,offw:l.counts.OFFW,off:l.counts.off,paidLeave:l.counts.paidLeave,
      lopLeave:l.counts.lopLeave,unmarked:l.counts.unmarked,na:l.counts.na,otMin:l.counts.otMin,hol:l.counts.hol??0,mgmtHol:l.counts.mgmtHol??0,elapsed:l.counts.elapsed??null,nt:l.counts.NT??0},
    latePenaltyHalf:l.latePenaltyHalf,payableDays:l.payableDays,days:l.days??null,
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
    // SCC leave port (migration 4110): decimal entitlements, the week-off mask (0 = none), overrides and blackouts; one leave record per date.
    leavePolicy:{cl:x.leavePolicy.ent.CL,sl:x.leavePolicy.ent.SL,el:x.leavePolicy.ent.EL,weekOffMask:(x.leavePolicy.weekOff??[]).reduce((m,d)=>m|(1<<d),0),overrides:[],blackouts:[]},
    approvers:[{userId:1,name:'Sagar',role:'Owner'},{userId:2,name:'Meera',role:'Manager'}],agendas:[],
    roster:Object.entries(x.roster).filter(([k])=>visId(k.split('|')[1])).map(([k,v])=>({staffId:sid.get(k.split('|')[1]),date:k.split('|')[0],shift:v})),
    attendance:Object.entries(x.att).filter(([k])=>visId(k.split('|')[1])).map(([k,a])=>({staffId:sid.get(k.split('|')[1]),date:k.split('|')[0],status:a.status,otMinutes:a.otMin,
      markedBy:uidNum(a.by),markedUtc:utc(a.at),note:a.note??null})),
    leaves:x.leaves.filter(l=>visId(l.staffId)).map(l=>({leaveId:lid.get(l.id),staffId:sid.get(l.staffId),date:l.date,leaveType:l.type,from:l.from,to:l.to,half:l.half??null,days:l.days,
      reason:l.reason||null,approverId:uidNum(l.approver),approverName:l.approverName??null,status:l.status,requestedBy:uidNum(l.requestedBy),requestedUtc:utc(l.requestedAt),
      decidedBy:uidNum(l.decidedBy),decidedUtc:l.decidedAt?utc(l.decidedAt):null,note:l.note??null,cancelledBy:uidNum(l.cancelledBy),cancelledUtc:l.cancelledAt?utc(l.cancelledAt):null,
      legacy:!!l.legacy,sccId:l.sccId??null,decidedByName:l.decidedByName??null,version:'v-leave-'+lid.get(l.id)})),
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
    leaves:x.leaves.map(l=>({...l,id:String(lid.get(l.id)),staffId:s(l.staffId),requestedBy:String(uidNum(l.requestedBy)),...(l.decidedBy?{decidedBy:String(uidNum(l.decidedBy))}:{}),
      ...(l.approver?{approver:String(uidNum(l.approver))}:{})})),
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
      'SetLeaveEntOverride','AddLeaveBlackout','RemoveLeaveBlackout','AddLeaveAgenda','RemoveLeaveAgenda','ImportLeave','ClearLeave',
      'GiveAdvance','SetStatutoryRates','PreparePayRun','LockPayRun','UnlockPayRun','IssueBankFile','SendPayslip','PrepareFnf','ConfirmFnf','DiscardFnf',
      'CorrectAttendance','DecidePayrollRequest','WithdrawPayrollRequest','SavePaidHoliday','RemoveHoliday'].map(n=>['POST /commands/'+n,ok(n)])),
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
    // N3: joining date of a new staff member; department editor (code upper-cased, the loaded leave limit sent back unchanged).
    [{type:'HR_STAFF_SAVE',name:'Back-dated Clerk',dept:'FO',designation:'Clerk',defaultShift:'G',weeklyOff:0,phone:'',gender:'F',joined:'2026-09-01'},'SaveStaff',
      {staffId:null,name:'Back-dated Clerk',dept:'FO',designation:'Clerk',defaultShift:'G',weeklyOff:0,linkUserId:null,joined:'2026-09-01',phone:'',gender:'F'},undefined],
    [{type:'HR_DEPT_SAVE',code:'sec',name:' Security '},'SaveDepartment',{code:'SEC',name:'Security',leaveCap:null}],
    [{type:'HR_DEPT_SAVE',id:'HK',code:'HK',name:'Housekeeping team'},'SaveDepartment',{code:'HK',name:'Housekeeping team',leaveCap:P.staffX.leavePolicy.deptCap?.HK??null}],
    [{type:'HR_STAFF_ACTIVE',id:ravi,active:false},'SetStaffActive',{staffId:+ravi,active:false},'v-staff-'+ravi],
    [{type:'HR_ROSTER_SET',staffId:ravi,date:'2026-10-27',shift:'N'},'SetRoster',{staffId:+ravi,date:'2026-10-27',shift:'N'}],
    [{type:'HR_ROSTER_COPY',from:'2026-10-26'},'CopyRosterWeek',{week:'2026-10-26'}],
    [{type:'HR_ATT_MARK',staffId:ravi,status:'P',otMin:45,note:' Late checkout cover '},'MarkAttendance',{staffId:+ravi,status:'P',otMinutes:45,note:'Late checkout cover'}],
    [{type:'HR_LEAVE_REQUEST',staffId:ravi,approver:'2',leaveType:'CL',from:'2026-11-05',to:'2026-11-07',half:'AM',reason:'Clinic visit',confirmBlackout:true},'RequestLeave',
      {staffId:+ravi,leaveType:'CL',from:'2026-11-05',to:'2026-11-07',half:'AM',reason:'Clinic visit',approverId:2,confirmOverBalance:false,confirmBlackout:true}],
    [{type:'HR_LEAVE_REQUEST',staffId:ravi,approver:'1',leaveType:'OTH',from:'2026-11-09',reason:'  '},'RequestLeave',
      {staffId:+ravi,leaveType:'OTH',from:'2026-11-09',to:'2026-11-09',half:null,reason:null,approverId:1,confirmOverBalance:false,confirmBlackout:false}],
    [{type:'HR_LEAVE_DECIDE',id:leave,approve:false,note:'Peak week'},'DecideLeave',{leaveId:+leave,approve:false,note:'Peak week'},'v-leave-'+leave],
    // DEC-122: an approval over the quota carries the confirmation (the screen asked SCC's warning); a rejection never does
    [{type:'HR_LEAVE_DECIDE',id:leave,approve:true,confirmOverBalance:true},'DecideLeave',{leaveId:+leave,approve:true,confirmOverBalance:true},'v-leave-'+leave],
    [{type:'HR_LEAVE_DECIDE',id:leave,approve:false,confirmOverBalance:true},'DecideLeave',{leaveId:+leave,approve:false},'v-leave-'+leave],
    [{type:'HR_LEAVE_CANCEL',id:leave},'CancelLeave',{leaveId:+leave},'v-leave-'+leave],
    // SCC leave port: settings, ⚙ override (blank = the default → null), blackouts, agenda, import (validated here, merged on the server), clear.
    [{type:'HR_LEAVE_ENT_OVERRIDE',staffId:ravi,ent:{CL:'3.5',SL:'',EL:20}},'SetLeaveEntOverride',{staffId:+ravi,cl:3.5,sl:null,el:20}],
    [{type:'HR_LEAVE_BLACKOUT_ADD',from:'2026-12-20',to:'2026-12-25',dept:'HK',kind:'blackout',hard:true,label:' Christmas rush '},'AddLeaveBlackout',
      {from:'2026-12-20',to:'2026-12-25',dept:'HK',kind:'blackout',hard:true,label:'Christmas rush'}],
    [{type:'HR_LEAVE_BLACKOUT_REMOVE',id:'77'},'RemoveLeaveBlackout',{blackoutId:77}],
    [{type:'HR_AGENDA_ADD',date:'2026-11-02',title:' Briefing ',time:'09:30',agendaType:'meeting',description:''},'AddLeaveAgenda',{date:'2026-11-02',title:'Briefing',time:'09:30',type:'meeting',description:''}],
    [{type:'HR_AGENDA_REMOVE',id:'88'},'RemoveLeaveAgenda',{agendaId:88}],
    [{type:'HR_LEAVE_IMPORT',data:{leaves:{'2026-11-30':[{id:'a1',name:'Ravi Shinde',type:'full_day',category:'Casual Leave',approvedBy:'Meera',status:'pending'},
      {id:'a2',name:'Nobody',type:'full_day',approvedBy:'Meera'}]},agendas:{},weekOff:{days:[0]}}},'ImportLeave',
      {skipped:1,leaves:[{sccId:'a1',staffId:+ravi,date:'2026-11-30',half:null,leaveType:'CL',reason:null,approverId:2,approverName:null,from:'2026-11-30',to:'2026-11-30',status:'Pending',
        legacy:false,decidedByName:null,decidedAt:null}],agendas:[],weekOffMask:1}],
    [{type:'HR_LEAVE_CLEAR',confirm:'DELETE'},'ClearLeave',{confirm:'DELETE'}],
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
    // Wave B item B4: DEC-075 corrections (overtime only with a mark), DEC-083 holidays, DEC-084 the bank-change reason.
    [{type:'HR_ATT_CORRECT',staffId:ravi,date:'2026-09-22',mark:'P',otMin:30,reason:' Duty register '},'CorrectAttendance',
      {staffId:+ravi,date:'2026-09-22',mark:'P',reason:'Duty register',otMinutes:30}],
    [{type:'HR_ATT_CORRECT',staffId:ravi,date:'2026-09-16',mark:'CL',otMin:0,reason:'Leave form'},'CorrectAttendance',{staffId:+ravi,date:'2026-09-16',mark:'CL',reason:'Leave form'}],
    [{type:'HR_HOLIDAY_SAVE',date:'2026-11-14',name:' Diwali (fictional) '},'SavePaidHoliday',/* masters integration: old SaveHoliday */{date:'2026-11-14',name:'Diwali (fictional)'}],
    [{type:'HR_HOLIDAY_REMOVE',date:'2026-11-14'},'RemoveHoliday',{date:'2026-11-14'}],
    [{type:'HR_STAFF_SAVE',id:ravi,name:rv.name,dept:rv.dept,designation:rv.designation,userId:rv.userId,defaultShift:rv.defaultShift,weeklyOff:rv.weeklyOff,
      accountNo:'999999999999',bankReason:' New account (fictional) '},'SaveStaff',
      {staffId:+ravi,name:rv.name,dept:rv.dept,designation:rv.designation,defaultShift:rv.defaultShift,weeklyOff:rv.weeklyOff,linkUserId:6,accountNo:'999999999999',
        bankReason:'New account (fictional)'},'v-staff-'+ravi],
  ];
  const sent=[];
  for(const [i,[cmd,name,body,ver]] of cases.entries()){srv.reset();const r=await rt.run({...cmd,key:'k-'+i});
    sent.push({r,name,body,ver,call:srv.commands(name)[0],reloaded:srv.gets('/payroll/board').length,key:'k-'+i});}
  // N3 prechecks: no department chosen (none on a fresh server), a joining date after the business date, a bad department code.
  srv.reset();
  const noDept=await rt.run({type:'HR_STAFF_SAVE',name:'Nobody',dept:'',designation:'X',defaultShift:'G',weeklyOff:0,key:'k-nd'});
  const future=await rt.run({type:'HR_STAFF_SAVE',name:'Future Joiner',dept:'FO',designation:'X',defaultShift:'G',weeklyOff:0,joined:'2099-01-01',key:'k-fj'});
  const badCode=await rt.run({type:'HR_DEPT_SAVE',code:'front office',name:'Front office',key:'k-bc'});
  const precheckCalls=srv.commands('SaveStaff').length+srv.commands('SaveDepartment').length;
  // Leave policy: entitlements + the default limit, then one SaveDepartment per department whose own limit changed (step keys).
  srv.reset();const pol=await rt.run({type:'HR_LEAVE_POLICY_SET',ent:{CL:10,SL:7,EL:'15.5'},weekOff:[0,6],deptCap:{HK:1,FO:''},key:'k-pol'});
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
    // SCC: decimal entitlements, the week-off set as a mask (Sun + Sat = 65); a blank department cap keeps the old one (no call for FO).
    assert.deepEqual(polCalls,[['/commands/SetLeavePolicy','k-pol',{cl:10,sl:7,el:15.5,weekOffMask:65}],['/commands/SaveDepartment','k-pol:dept:HK',{code:'HK',name:'Housekeeping',leaveCap:1}]]);
    const noAppr=sent.find(z=>z.name==='RequestLeave');assert.ok(noAppr);
    assert.deepEqual(f.map(z=>[z.id,z.name.split(' ')[0],z.status,z.net,z.by,z.adjustments[0].kind]),[['701','Ravi','Draft',-250000,'8','ded']]);
    assert.ok(conf.ok&&disc.ok);
    assert.deepEqual(srv.commands('ConfirmFnf').map(c=>[c.body,c.key]),[[{fnfId:701,expectedVersion:'v-fnf-701'},'k-fc']]);
    assert.deepEqual(srv.commands('DiscardFnf').map(c=>c.body),[{fnfId:701,expectedVersion:'v-fnf-701'}]);
    assert.deepEqual([stale.ok,stale.reason],[false,'server']);assert.match(stale.text,/Refresh and try again/);
    assert.deepEqual(S.AREAS.find(a=>a.id==='staff').unmapped,{});
    assert.deepEqual([noDept.reason,future.reason,badCode.reason,precheckCalls],['precheck','precheck','precheck',0]);
    assert.match(noDept.text,/Choose a department/);assert.match(future.text,/cannot be after the business date/);assert.match(badCode.text,/2–10 capital letters or digits/);
    for(const t of ['HR_STAFF_SAVE','HR_STAFF_ACTIVE','HR_ROSTER_SET','HR_ROSTER_COPY','HR_ATT_MARK','HR_LEAVE_REQUEST','HR_LEAVE_DECIDE','HR_LEAVE_CANCEL','HR_LEAVE_POLICY_SET',
      'HR_ADVANCE_ADD','HR_STAT_RATES_SET','HR_RATES_SET','HR_PAYROLL_RUN','HR_PAYROLL_LOCK','HR_PAYROLL_UNLOCK','HR_PAYROLL_BANK_FILE','HR_PAYSLIP_SEND','HR_FNF_SETTLE',
      'HR_FNF_CONFIRM','HR_FNF_DISCARD','HR_DEPT_SAVE','HR_ATT_CORRECT','HR_REQUEST_DECIDE','HR_REQUEST_WITHDRAW','HR_HOLIDAY_SAVE','HR_HOLIDAY_REMOVE',
      'HR_LEAVE_ENT_OVERRIDE','HR_LEAVE_BLACKOUT_ADD','HR_LEAVE_BLACKOUT_REMOVE','HR_AGENDA_ADD','HR_AGENDA_REMOVE','HR_LEAVE_IMPORT','HR_LEAVE_CLEAR'])assert.ok(rt.mapped(t),t);
  });

  // ── Wave B item B4: holidays, requests, the correction window and a holiday count on a stored line come from the board ──
  srvData.board.holidays=[{date:'2026-11-14',name:'Diwali (fictional)'}];
  srvData.board.correctFrom='2026-09-01';srvData.board.correctTo='2026-10-23';
  const entry=(kind,status,otMinutes,leaveType)=>({kind,status,otMinutes,leaveType});
  srvData.board.requests=[
    {requestId:901,kind:'attendance',staffId:+ravi,date:'2026-09-22',before:entry('mark','A',0,null),after:entry('mark','P',30,null),bankBefore:null,bankAfter:null,payEffectPaise:50625,
      reason:'Duty register',status:'Pending',direct:false,requestedBy:2,requestedUtc:'2026-10-24T03:00:00.000Z',decidedBy:null,decidedUtc:null,note:null,version:'v-req-901'},
    {requestId:902,kind:'bank',staffId:+ravi,date:null,before:null,after:null,bankBefore:{bankName:'Demo Bank',accountMasked:'••••0001',ifsc:'DEMO0000101'},
      bankAfter:{bankName:'Demo Bank',accountMasked:'••••9999',ifsc:'DEMO0000999'},payEffectPaise:null,reason:'New account',status:'Pending',direct:false,requestedBy:8,
      requestedUtc:'2026-10-24T03:05:00.000Z',decidedBy:null,decidedUtc:null,note:null,version:'v-req-902'},
    {requestId:903,kind:'attendance',staffId:+ravi,date:'2026-09-03',before:entry('mark','P',0,null),after:entry('leave',null,0,'CL'),bankBefore:null,bankAfter:null,payEffectPaise:null,
      reason:'Form',status:'Approved',direct:true,requestedBy:2,requestedUtc:'2026-10-24T03:10:00.000Z',decidedBy:2,decidedUtc:'2026-10-24T03:10:00.000Z',note:null,version:'v-req-903'}];
  const sepSrv=srvData.board.runs.find(r=>r.month==='2026-09');sepSrv.lines[0].counts.hol=1;
  await rt.refresh();const wb=rt.get().state,wx=wb.staffX;
  const wbShown=[P_.renderPages(wb,'1',false),P_.renderPages(wb,'1',true),P_.renderPages(wb,'2',false),P_.renderPages(wb,'2',true)];
  srv.reset();const dec=await rt.run({type:'HR_REQUEST_DECIDE',id:'901',approve:false,note:' Not on duty ',key:'k-rd'});
  const wd=await rt.run({type:'HR_REQUEST_WITHDRAW',id:'902',key:'k-rw'});const ap=await rt.run({type:'HR_REQUEST_DECIDE',id:'902',approve:true,key:'k-ra'});
  const noReason=await rt.run({type:'HR_ATT_CORRECT',staffId:ravi,date:'2026-09-22',mark:'P',reason:'x',key:'k-nr'});
  test('[W4-6 staff · B4] holidays, requests (masked bank details, pay effect as the server sends it) and the server correction window load; decide / withdraw send the request version',()=>{
    assert.deepEqual(wx.holidays,[{date:'2026-11-14',name:'Diwali (fictional)'}]);
    assert.deepEqual(wx.correctWindow,{from:'2026-09-01',to:'2026-10-23'});assert.deepEqual(M.hrCorrectionWindow(wb),{from:'2026-09-01',to:'2026-10-23'});
    const [a,b,c]=wx.requests;
    assert.deepEqual({...a,requestedAt:''},{id:'901',kind:'attendance',staffId:ravi,date:'2026-09-22',before:{kind:'mark',status:'A',otMin:0},after:{kind:'mark',status:'P',otMin:30},
      payEffect:50625,reason:'Duty register',status:'Pending',requestedBy:'2',requestedAt:''});
    assert.match(a.requestedAt,/^2026-10-24T/);
    assert.deepEqual([b.kind,b.bankBefore.accountNo,b.bankAfter.accountNo,b.bankAfter.ifsc,b.payEffect],['bank','••••0001','••••9999','DEMO0000999',undefined]);
    assert.deepEqual([c.after,c.direct,c.decidedBy,c.status],[{kind:'leave',type:'CL'},true,'2','Approved']);
    assert.equal(rt.ids.version('hrreq','901'),'v-req-901');
    assert.equal(wx.runs.find(r=>r.month==='2026-09').lines[0].counts.hol,1);
    assert.deepEqual(wbShown.map(r=>r.errors),[{},{},{},{}]);
    assert.ok(dec.ok&&wd.ok&&ap.ok,[dec.text,wd.text,ap.text].join(' | '));
    assert.deepEqual(srv.commands('DecidePayrollRequest').map(q=>[q.body,q.key]),[[{requestId:901,approve:false,note:'Not on duty',expectedVersion:'v-req-901'},'k-rd'],
      [{requestId:902,approve:true,expectedVersion:'v-req-902'},'k-ra']]);
    assert.deepEqual(srv.commands('WithdrawPayrollRequest').map(q=>[q.body,q.key]),[[{requestId:902,expectedVersion:'v-req-902'},'k-rw']]);
    assert.deepEqual([noReason.ok,noReason.reason],[false,'precheck']);assert.equal(srv.commands('CorrectAttendance').length,0);
  });
}
