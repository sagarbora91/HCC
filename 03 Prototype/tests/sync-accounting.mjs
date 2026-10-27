// W4-7 accounting, expenses & compliance (sync/accounting.ts): fake .NET API shaped like Hcc.Contracts.Accounting / Expenses /
// Compliance, built FROM the prototype's own figures (M.seed()), so the slices the runtime assembles must make the unchanged
// prototype selectors (accVouchers, accTrialBalance, accExceptions, cmpMonthPage, cmpActionCenter, …) return the same values as in the prototype.
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

const EXTRA=['PERM-ACC-001','PERM-ACC-002','PERM-ACC-003','PERM-ACC-004','PERM-ACC-005','PERM-ACC-006','PERM-CMP-001','PERM-CMP-002'];
const ownerCaps=()=>{const c=capabilities();c.permissions.push(...EXTRA.map(permissionId=>({permissionId,mode:'A',scope:'P',limit:null,limitUnit:null})));return c;};
const noBooking=v=>({...v,source:{kind:v.source.kind,id:v.source.id,sub:v.source.sub}});
const srvVoucher=v=>{const t=v.entries.reduce((a,e)=>({d:a.d+e.debit,c:a.c+e.credit}),{d:0,c:0});
  return {number:v.no,date:v.date,type:v.type,narration:v.narration,sourceKey:v.source.id,sourceKind:v.source.kind,sub:v.source.sub,debitPaise:t.d,creditPaise:t.c,balanced:t.d===t.c,
    entries:v.entries.map(e=>({account:e.account,ledger:'x',debitPaise:e.debit,creditPaise:e.credit})),unmapped:[...v.unmapped]};};
const USERS={owner:1,manager:2,reception:3,cashier:4,accountant:8};

export async function register({test,assert,M}){
  const S=await loadSync();
  const P=M.seed();const D=P.date,Y=M.accDates(P)[1];
  const LOCK={lockId:5,lockedUtc:'2026-10-24T03:00:00',lockedBy:8,version:'AAAAAAAAB+E='};
  // ── accounting: the server's evaluation = the prototype's derivation for every active date; Y is locked on the server ──
  const day=(d)=>{const vs=M.accVouchers(P,d);const t=M.accTotals(vs);const ex=M.accExceptions(P,d);
    return {date:d,status:d===Y?'Locked':M.accDayStatus(P,d),provisional:d!==Y,voucherCount:vs.length,debitPaise:t.debit,creditPaise:t.credit,balanced:t.debit===t.credit,
      errors:ex.filter(x=>x.severity==='error').length,warnings:0,lock:d===Y?LOCK:null,vouchers:vs.map(srvVoucher),
      exceptions:ex.map(x=>({id:x.id,severity:x.severity,kind:x.kind,title:x.title,detail:x.detail,voucher:x.voucher??null,source:x.source??null,key:x.key??null})),
      reconciliation:M.accReconcile(P,d).map(c=>({id:c.id,label:c.label,reportPaise:c.report,ledgerPaise:c.ledger,ok:c.ok,note:c.note}))};};
  const mapRows=M.accMapRows(P).map(r=>({mapKey:r.key,group:r.group,value:r.value,account:r.key==='expense:Laundry'?'5050':r.account||null,defaultAccount:r.defaultAccount??null,
    changed:r.key==='expense:Laundry'?true:r.changed,uses:r.uses,mapped:r.mapped}));
  let opening=[],tbExtra={};
  // ── expenses: the prototype's two seed expenses + whatever SubmitExpense adds ──
  const exps=new Map();let nextExp=103;
  const srvExp=(id,e,over={})=>({expenseId:id,ref:'EXP-'+id,vendor:e.vendor,category:e.category,amountPaise:e.amount,method:e.method,businessDate:e.date,status:e.status,
    submittedBy:USERS[e.actor]??1,notes:e.notes??null,receipt:e.receipt??null,reason:null,decidedBy:null,decidedUtc:null,reversalOf:null,reversedBy:null,version:'AAAAAAAAC'+id+'=',...over});
  P.expenses.forEach(e=>exps.set(Number(e.id.slice(4)),srvExp(Number(e.id.slice(4)),e)));
  exps.set(99,srvExp(99,{vendor:'Old draft',category:'Supplies',amount:5000,method:'Cash',date:'2026-07-01',status:'Draft',actor:'owner'}));
  // ── compliance (SCC tax module in full, DEC-116): the prototype's profile, FY, records and evidence list as the server's GET /compliance/state ──
  const C=M.cmpSlice(P),U=id=>USERS[id]??1,T='2026-05-06T05:30:00Z';
  const custom12={customId:12,id:'cust-12',cat:'lab',name:'Fire NOC renewal',freq:'Annual',dueDay:31,months:['November'],form:'Fire dept',who:'',what:'',why:'',penalty:'',
    responsible:'',reviewer:'',risk:null,buffer:null,checklist:[],workflow:[],evidence:[],updatedBy:1,updatedUtc:T,version:'AAAAAAAAG12='};
  const cmpState={asOf:D,profile:{...C.profile,updatedBy:1,updatedUtc:'2026-04-01T03:30:00Z',version:'AAAAAAAAP1='},fyStart:C.fyStart,
    records:Object.entries(C.records).flatMap(([fy,b])=>Object.entries(b).map(([itemId,r])=>({fyStart:Number(fy),itemId,done:!!r.done,na:!!r.na,filedOn:r.filedOn??null,notes:r.notes??null,
      evidenceRef:r.evidenceRef??null,reviewerNote:r.reviewerNote??null,dueDate:r.dueDate??null,doneBy:r.by?U(r.by):null,doneUtc:r.by?T:null,naBy:r.naBy?U(r.naBy):null,naUtc:r.naBy?T:null,legacy:r.legacy??null}))),
    custom:[custom12],archived:['cust-12'],archivedCats:[],
    evidence:C.files.map((f,i)=>({evidenceId:i+1,fyStart:f.fy,itemId:f.itemId,fileName:f.name,contentType:f.type,sizeBytes:f.size,addedBy:8,addedUtc:'2026-10-06T05:35:00Z'})),
    reopened:[{fyStart:2026,itemId:'oc4',part:'done',wasDone:true,wasNa:false,filedOn:'2026-10-10',notes:'',evidenceRef:'ARN-1',reviewerNote:'',dueDate:'2026-10-11',recordedBy:8,recordedUtc:T,reopenedBy:1,reopenedUtc:T,reason:'Wrong ARN'}],
    settings:{...C.settings,version:'AAAAAAAAE1='}};
  const srv=fakeServer({
    'GET /me/capabilities':()=>ownerCaps(),
    // Masters integration (MA-6): the server's holiday calendar = the prototype seed (compliance due dates no longer move: SCC, DEC-116).
    'GET /hr/holidays':()=>{const items=M.hrHolidays(P).map((h,i)=>({holidayId:i+1,code:h.code,name:h.name,sortOrder:h.sortOrder,active:h.active,retiredOn:null,kind:h.kind,version:'H'+(i+1)}));
      return {page:1,pageSize:200,total:items.length,items};},
    'GET /accounting/days':({query})=>M.accDates(P).filter(d=>d>=query.from&&d<=query.to).sort().map(day),
    'GET /accounting/trial-balance':()=>({date:'2026-08-23',cumulative:true,debitPaise:0,creditPaise:0,balanced:true,rows:opening,...tbExtra}),
    'GET /accounting/mapping':()=>mapRows,
    'GET /accounting/mapping/history':()=>[{changeId:3,atUtc:'2026-10-23T10:00:00Z',actor:1,mapKey:'expense:Laundry',before:'5010',after:'5050'}],
    'GET /accounting/locks/history':()=>[{lockId:5,businessDate:Y,action:'lock',atUtc:'2026-10-24T03:00:00',actor:8,reason:null}],
    'POST /commands/LockAccountingPeriod':({body})=>({businessDate:body.businessDate,lockId:6,status:'Locked',voucherCount:1,debitPaise:1,creditPaise:1,version:'v'}),
    'POST /commands/UnlockAccountingPeriod':({body})=>({businessDate:body.businessDate,lockId:5,status:'Closed',voucherCount:1,debitPaise:1,creditPaise:1,version:'v'}),
    'POST /commands/SetLedgerMapping':({body})=>({mapKey:body.mapKey,before:'5010',after:body.accountCode,isDefault:false}),
    'POST /commands/ResetLedgerMapping':({body})=>({mapKey:body.mapKey,before:'5050',after:'5010',isDefault:true}),
    'GET /expenses':({query})=>{const all=[...exps.values()].filter(e=>query.status?e.status===query.status:e.businessDate>=query.from&&e.businessDate<=query.to);
      return {items:all,page:1,pageSize:100,total:all.length,ownOnly:false};},
    'POST /commands/SubmitExpense':({body})=>{const id=nextExp++;const e=srvExp(id,{vendor:body.vendor,category:body.category,amount:body.amountPaise,method:body.method,date:D,
      status:body.draft?'Draft':'Pending',actor:'owner',notes:body.notes,receipt:body.receipt});exps.set(id,e);return {expense:e,duplicateOf:[],cashMovementId:null};},
    'POST /commands/SubmitExpenseDraft':({body})=>{const e={...exps.get(body.expenseId),status:'Pending',version:'AAAAAAAAF1='};exps.set(body.expenseId,e);return {expense:e,duplicateOf:[],cashMovementId:null};},
    'POST /commands/DecideExpense':({body})=>{const e={...exps.get(body.expenseId),status:body.approve?'Posted':'Rejected',decidedBy:1,decidedUtc:'2026-10-24T04:00:00Z'};exps.set(body.expenseId,e);return {expense:e,duplicateOf:[],cashMovementId:7};},
    'POST /commands/ReverseExpense':({body})=>{const o=exps.get(body.expenseId);const r=srvExp(nextExp++,{...o,vendor:o.vendor,category:o.category,amount:-o.amountPaise,method:o.method,date:D,status:'Posted',actor:'owner'},{reversalOf:body.expenseId,reason:body.reason});
      exps.set(r.expenseId,r);exps.set(body.expenseId,{...o,status:'Reversed',reversedBy:r.expenseId,reason:body.reason});return {original:exps.get(body.expenseId),reversal:r,cashMovementId:null};},
    'GET /compliance/state':()=>cmpState,
    'GET /compliance/ca-packs':()=>[{packId:4,atUtc:'2026-10-20T06:30:00Z',by:8,to:C.settings.caEmail,toName:C.settings.caName,overdue:1,due:2,filed:3,notApplicable:0,lines:['Position as on …']}],
    'POST /commands/MarkComplianceDone':({body})=>({fyStart:body.fyStart,itemId:body.itemId,dueDate:'2026-10-11',daysLate:13}),
    'POST /commands/MarkComplianceNotApplicable':({body})=>({fyStart:body.fyStart,itemId:body.itemId}),
    'POST /commands/ReopenComplianceItem':({body})=>({fyStart:body.fyStart,itemId:body.itemId,part:'done'}),
    'POST /commands/SetComplianceFy':({body})=>({fyStart:body.fyStart}),
    'POST /commands/SaveComplianceProfile':()=>({version:'v'}),
    'POST /commands/ToggleComplianceArchive':({body})=>({key:body.key,archived:true}),
    'POST /commands/ToggleComplianceCategoryArchive':({body})=>({cat:body.cat,archived:true}),
    'POST /commands/SaveCustomObligation':({body})=>({customId:body.customId??13,id:'cust-'+(body.customId??13),version:'v'}),
    'POST /commands/DeleteCustomObligation':({body})=>({customId:body.customId,evidenceRemoved:0}),
    'POST /commands/AddComplianceEvidence':({body})=>({evidenceId:7,sizeBytes:body.base64.length}),
    'POST /commands/DeleteComplianceEvidence':({body})=>({evidenceId:body.evidenceId}),
    'POST /commands/SaveComplianceSettings':({body})=>({caName:body.caName??'',caEmail:body.caEmail??'',dueSoonDays:body.dueSoonDays??7,version:'v'}),
    'POST /commands/GenerateCaPack':()=>({packId:5,to:'x',toName:'y',overdue:0,open:0,lines:1,atUtc:'2026-10-24T04:00:00Z'}),
  });
  const rt=await startRuntime(S,srv);const s=rt.get().state;
  const bootGets=srv.gets().map(c=>c.path+(c.query.status?'?'+c.query.status:''));
  const Pg=await loadPages();const shown=[Pg.renderPages(s,'1',false),Pg.renderPages(s,'1',true)];

  test('[W4-7 accounting] accountingX = the server days: vouchers, exceptions, reconciliation, status, trial balance equal the prototype’s own derivation',()=>{
    assert.ok(bootGets.includes('/accounting/days'),bootGets.join(','));
    const q=srv.gets('/accounting/days')[0].query;assert.deepEqual([q.from,q.to],['2026-08-24',D],'62-day window ending on the business date');
    assert.equal(srv.gets('/accounting/trial-balance')[0].query.date,'2026-08-23');
    assert.deepEqual(M.accDates(s),M.accDates(P));
    for(const d of M.accDates(P)){
      assert.deepEqual(M.accVouchers(s,d),M.accVouchers(P,d).map(noBooking),d);
      assert.deepEqual(M.accReconcile(s,d),M.accReconcile(P,d),d);
      assert.deepEqual(M.accExceptions(s,d),M.accExceptions(P,d),d);
    }
    assert.deepEqual(M.accTrialBalance(M.accVouchersUpTo(s,D)),M.accTrialBalance(M.accVouchersUpTo(P,D)));
    assert.equal(M.accVouchers(s,D).length,13);
    assert.deepEqual(M.accTotals(M.accVouchers(s,D)),M.accTotals(M.accVouchers(P,D)));
    assert.equal(M.accDayStatus(s,D),M.accDayStatus(P,D));
    assert.ok(M.accVouchers(s,D).some(v=>v.entries.some(e=>e.account==='2110'))&&M.accVouchers(s,D).some(v=>v.entries.some(e=>e.account==='2120')),'CGST 2110 / SGST 2120 ledgers');
  });
  test('[W4-7 accounting] Locked day: lock record from the server (IST clock, user id, version kept), lock blocker texts, mapping overrides + history',()=>{
    assert.equal(M.accDayStatus(s,Y),'Locked');
    const l=M.accLockOf(s,Y);assert.deepEqual([l.lockedAt,l.lockedBy,l.count],['2026-10-24T08:30','8',M.accVouchers(P,Y).length]);
    assert.equal(rt.ids.version('accLock',Y),'AAAAAAAAB+E=');
    assert.equal(M.accLockBlocker(s,Y),'This date is already locked.');
    assert.equal(M.accLockBlocker(s,D),M.accDayStatus(P,D)==='Open'?'Close the business day before locking it.':M.accLockBlocker(P,D));
    assert.equal(M.accMapping(s)['expense:Laundry'],'5050');assert.deepEqual(Object.keys(s.accountingX.mapping),['expense:Laundry']);
    assert.equal(M.accMapRows(s).find(r=>r.key==='expense:Laundry').changed,true);
    assert.deepEqual(s.accountingX.mapHistory,[{id:'3',at:'2026-10-23T15:30',actor:'1',key:'expense:Laundry',before:'5010',after:'5050'}]);
    assert.deepEqual(s.accountingX.lockHistory,[{id:'5:lock',at:'2026-10-24T08:30',actor:'8',date:Y,action:'lock'}]);
    const tally=M.accTallyXml(s,Y,D);assert.equal(tally.count,M.accTallyXml(P,Y,D).count);assert.equal(tally.debit,M.accTallyXml(P,Y,D).debit);
  });
  test('[W4-7 expenses] expenses = GET /expenses (window + every Draft / Pending) mapped to the prototype Expense; compliance from GET /compliance/state',()=>{
    assert.deepEqual(bootGets.filter(p=>p.startsWith('/expenses')).sort(),['/expenses','/expenses?Draft','/expenses?Pending']);
    assert.deepEqual(s.expenses.map(e=>e.id),['99','101','102']);
    const strip=e=>[e.vendor,e.category,e.amount,e.method,e.date,e.status];
    assert.deepEqual(s.expenses.slice(1).map(strip),P.expenses.map(strip));
    assert.deepEqual([s.expenses[1].actor,s.expenses[2].actor],['2','3']);
    assert.equal(rt.ids.version('expense','102'),'AAAAAAAAC102=');
    // SCC cards computed on the server data = the prototype's: every month page (stats, done / NA), Action Center, dashboard, CSV register
    for(const m of M.CMP_MONTHS){const a=M.cmpMonthPage(s,m),b=M.cmpMonthPage(P,m);assert.deepEqual(a.stats,b.stats,m);assert.deepEqual(a.all.map(v=>[v.card.id,v.isDone,v.isNA,v.due]),b.all.map(v=>[v.card.id,v.isDone,v.isNA,v.due]),m);}
    assert.deepEqual(M.cmpActionCenter(s).map(r=>[r.itemId,r.days]),M.cmpActionCenter(P).map(r=>[r.itemId,r.days]));
    assert.deepEqual(M.cmpQuarter(s,'Q3').q.score,M.cmpQuarter(P,'Q3').q.score);assert.deepEqual(M.cmpCsvRows(s),M.cmpCsvRows(P));
    assert.deepEqual(M.cmpRecord(s.complianceX,2026,'oc1'),{...M.cmpRecord(P.complianceX,2026,'oc1'),by:'8',at:'2026-05-06T11:00'});
    assert.deepEqual(M.cmpRecord(s.complianceX,2026,'my6'),{...M.cmpRecord(P.complianceX,2026,'my6'),naBy:'8',naAt:'2026-05-06T11:00'});
    assert.deepEqual([s.complianceX.profile.name,s.complianceX.profile.gstType,s.complianceX.profile.updatedBy],[C.profile.name,'regular','1']);
    assert.deepEqual(s.complianceX.custom.map(c=>[c.id,c.dueDay,c.months]),[['cust-12',31,['November']]]);assert.deepEqual(s.complianceX.archived,['cust-12']);
    assert.deepEqual(s.complianceX.files.map(f=>[f.id,f.itemId,f.name,f.size]),[['1','oc1',C.files[0].name,C.files[0].size]]);
    assert.deepEqual(s.complianceX.reopened.map(r=>[r.itemId,r.part,r.record.evidenceRef,r.reopenReason]),[['oc4','done','ARN-1','Wrong ARN']]);
    assert.deepEqual([rt.ids.version('cmpProfile','1'),rt.ids.version('cmpCustom','cust-12')],['AAAAAAAAP1=','AAAAAAAAG12=']);
    assert.deepEqual(s.complianceX.settings,C.settings);
    assert.deepEqual(s.complianceX.packs.map(p=>[p.id,p.at,p.by,p.counts.filed]),[['4','2026-10-20T12:00','8',3]]);
    assert.deepEqual(shown.map(x=>x.errors),[{},{}],'every desktop and phone page renders on the server data');
    assert.ok(shown[0].rendered>20&&shown[1].rendered>10);
  });

  // ── commands ──
  const res={},log=[];const seq=async(name,cmd)=>{srv.reset();res[name]={r:await rt.run(cmd),calls:srv.calls.map(c=>`${c.method} ${c.path}`)};log.push(...srv.calls);};
  const cmds=n=>log.filter(c=>c.method==='POST'&&c.path==='/commands/'+n);
  await seq('lock',{type:'ACC_LOCK',date:'2026-10-23',key:'acc:lock:1'});
  await seq('unlockBad',{type:'ACC_UNLOCK',date:Y,reason:'x'});
  await seq('unlock',{type:'ACC_UNLOCK',date:Y,reason:'Late laundry bill'});
  await seq('mapSet',{type:'ACC_MAP_SET',source:'expense:Laundry',account:'5040'});
  await seq('mapReset',{type:'ACC_MAP_RESET',source:'expense:Laundry'});
  // DEC-077: the Tally download goes to the server (it records the export and carries days flagged for re-export); the server file is saved.
  let tallyQuery;
  srv.on('GET /accounting/export/tally',({query})=>{tallyQuery=query;return {from:query.from,to:query.to,dates:2,voucherCount:3,debitPaise:100000,creditPaise:100000,simulated:true,
    fileName:`HCC-tally-SIMULATED-${query.from}_${query.to}.xml`,xml:'<ENVELOPE/>',reexportDates:['2026-10-22'],provisionalDates:[],exportId:9};});
  await seq('tally',{type:'ACC_TALLY_EXPORT',from:Y,to:Y,key:'tally:1'});
  await seq('tallyBad',{type:'ACC_TALLY_EXPORT',from:Y,to:'2026-01-01'});
  // A03b-05: events still pending → the server marks the unlocked dates provisional and says how many; a day exported before voucher ids.
  srv.on('GET /accounting/export/tally',({query})=>({from:query.from,to:query.to,dates:1,voucherCount:2,debitPaise:5000,creditPaise:5000,simulated:true,
    fileName:`HCC-tally-SIMULATED-${query.from}_${query.to}.xml`,xml:'<ENVELOPE/>',reexportDates:[],provisionalDates:[D],exportId:10,pendingEvents:2,legacyDates:['2026-10-01']}));
  await seq('tallyPending',{type:'ACC_TALLY_EXPORT',from:D,to:D,key:'tally:2'});
  const proto=M.execute(M.seed(),'owner',{type:'EXPENSE',vendor:'Petty Stores',category:'Petty cash',method:'Cash',amount:42000,receipt:'bill.jpg',notes:'Tea',draft:false});
  await seq('exp',{type:'EXPENSE',vendor:' Petty Stores ',category:'Petty cash',method:'Cash',amount:42000,receipt:'bill.jpg',notes:'Tea',draft:false,key:'modal-7:submit'});
  const added=rt.get().state.expenses.find(e=>e.id==='103');
  await seq('expBad',{type:'EXPENSE',vendor:'X',category:'Supplies',method:'Cheque',amount:100});
  await seq('submit',{type:'EXPENSE_SUBMIT',id:'99',key:'expsubmit:99'});
  await seq('approveNoTill',{type:'EXPENSE_APPROVE',id:'103',approve:true,key:'expense:103'});
  // Till handshake: the money area's cash-desk read learns the till with the open shift as ids 'till','current'.
  srv.on('GET /cashier/desk',()=>({businessDate:'2026-10-24',tills:[{till:{tillId:1,code:'FRONT',name:'Front desk',version:'T1'},active:{shift:{shiftId:9,tillId:1,businessDate:'2026-10-24',
    status:'Open',openingFloatPaise:500000,expectedPaise:null,countedPaise:null,approvalId:null,version:'S9'},submittedBy:null,differenceReason:null,closedBy:null,approval:null},expected:null,lastClosed:null}]}));
  await rt.refresh();const tillLearned=rt.ids.server('till','current');
  await seq('approve',{type:'EXPENSE_APPROVE',id:'103',approve:true,key:'expense:103'});
  await seq('reject',{type:'EXPENSE_APPROVE',id:'102',approve:false,reason:'Duplicate bill',key:'m:9'});
  await seq('reverse',{type:'EXPENSE_REVERSE',id:'103',reason:'Wrong vendor',key:'m:10'});
  const afterReverse=rt.get().state.expenses;
  await seq('done',{type:'CMP_DONE',fy:2026,itemId:'oc4',filedOn:'2026-10-23',notes:'Ack 7',evidenceRef:'ARN-7',reviewerNote:'Owner',key:'cmp:done:1'});
  await seq('na',{type:'CMP_NA',itemId:'oc2'});
  await seq('reopen',{type:'CMP_REOPEN',fy:2026,itemId:'oc1',reason:'Wrong challan'});
  await seq('reopenBad',{type:'CMP_REOPEN',fy:2026,itemId:'oc1',reason:'x'});
  await seq('fy',{type:'CMP_FY_SET',fy:2027});
  await seq('profile',{type:'CMP_PROFILE_SET',name:'HCC Demo Residency',pan:'aahfh2604k',gstin:'27AAHFH2604K1Z9',entity:'partnership',gstType:'regular',hasEmployees:true,qrmp:false,notes:''});
  await seq('arch',{type:'CMP_ARCHIVE_KEY',obligationKey:'GSTR-1',key:'cmp:arch:1'});
  await seq('cat',{type:'CMP_ARCHIVE_CAT',cat:'comp'});
  await seq('custAdd',{type:'CMP_CUSTOM_SAVE',name:'Lift licence',cat:'lab',freq:'Annual',dueDay:0,months:['March'],checklist:'Inspect\nPay',buffer:''});
  await seq('custEdit',{type:'CMP_CUSTOM_SAVE',id:'cust-12',name:'Fire NOC renewal',cat:'lab',freq:'Annual',dueDay:30,months:['November'],risk:'High',buffer:10});
  await seq('custDel',{type:'CMP_CUSTOM_DELETE',id:'cust-12'});
  await seq('evAdd',{type:'CMP_EVIDENCE_ADD',fy:2026,itemId:'oc4',name:'arn.pdf',dataUrl:'data:application/pdf;base64,JVBERg=='});
  await seq('evDel',{type:'CMP_EVIDENCE_DELETE',id:'1'});
  await seq('settings',{type:'CMP_SETTINGS_SET',caName:'Rao & Co',caEmail:'ca@example.test',dueSoonDays:10});
  await seq('pack',{type:'CMP_CA_PACK',key:'cmp:pack:1'});
  const body=n=>cmds(n).at(-1)?.body;
  test('[W4-7 accounting] ACC_LOCK / ACC_UNLOCK (reason, loaded lock version) / ACC_MAP_SET / ACC_MAP_RESET → server commands, then accountingX reloads',()=>{
    assert.ok(res.lock.r.ok,res.lock.r.text);assert.deepEqual(res.lock.calls.slice(0,2),['POST /commands/LockAccountingPeriod','GET /accounting/days']);
    assert.ok(res.lock.calls.includes('GET /accounting/mapping')&&!res.lock.calls.some(c=>c.startsWith('GET /expenses')),'only accountingX reloads');
    assert.deepEqual([res.unlockBad.r.reason,res.unlockBad.calls.length],['precheck',0]);
    assert.ok(res.unlock.r.ok);assert.deepEqual(cmds('UnlockAccountingPeriod')[0].body,{businessDate:Y,reason:'Late laundry bill',expectedVersion:'AAAAAAAAB+E='});
    assert.equal(cmds('LockAccountingPeriod')[0].key,'acc:lock:1');assert.deepEqual(cmds('LockAccountingPeriod')[0].body,{businessDate:'2026-10-23'});
    assert.deepEqual(cmds('SetLedgerMapping')[0].body,{mapKey:'expense:Laundry',accountCode:'5040'});
    assert.deepEqual(cmds('ResetLedgerMapping')[0].body,{mapKey:'expense:Laundry'});
    assert.ok(cmds('SetLedgerMapping')[0].key&&cmds('SetLedgerMapping')[0].key!==cmds('ResetLedgerMapping')[0].key);
  });
  test('[DEC-077] ACC_TALLY_EXPORT → GET /accounting/export/tally {from,to}: the server records the export and returns re-export days; a bad range is refused locally',()=>{
    assert.ok(res.tally.r.ok,res.tally.r.text);
    assert.deepEqual(res.tally.calls,['GET /accounting/export/tally'],'no reload: the export changes no loaded slice');
    assert.deepEqual(tallyQuery,{from:Y,to:Y});
    assert.deepEqual([res.tally.r.result.count,res.tally.r.result.reexport],[3,['2026-10-22']]);
    // No browser download here (node): the device could not save, which is reported as a warning (the server still recorded the export).
    assert.equal(res.tally.r.result.saved,'failed');assert.match(res.tally.r.warning,/could not save the XML/);
    assert.deepEqual([res.tallyBad.r.ok,res.tallyBad.r.reason,res.tallyBad.calls.length],[false,'precheck',0]);
  });
  test('[A03b-05] ACC_TALLY_EXPORT: pending accounting events and days exported before voucher ids reach the Accountant as a warning',()=>{
    const r=res.tallyPending.r;assert.ok(r.ok,r.text);
    assert.deepEqual([r.result.pending,r.result.provisional,r.result.legacy],[2,[D],['2026-10-01']]);
    assert.match(r.warning,/Provisional: 2 accounting events were not yet in the vouchers, so 2026-10-24 may be incomplete/);
    assert.match(r.warning,/2026-10-01 was exported before voucher IDs: if that earlier file was imported, delete those HCC vouchers in Tally/);
    assert.match(res.tally.r.warning,/could not save the XML\. Try the download again\.$/,'no pending events: no extra note');
  });
  test('[W4-7 expenses] EXPENSE (same record as the prototype) / SUBMIT / APPROVE (till for cash) / reject / REVERSE → server commands with key + loaded version, then expenses reload',()=>{
    assert.ok(res.exp.r.ok,res.exp.r.text);assert.deepEqual(res.exp.calls[0],'POST /commands/SubmitExpense');assert.ok(res.exp.calls.includes('GET /expenses'));
    assert.equal(cmds('SubmitExpense')[0].key,'modal-7:submit');
    assert.deepEqual(cmds('SubmitExpense')[0].body,{vendor:'Petty Stores',category:'Petty cash',amountPaise:42000,method:'Cash',draft:false,notes:'Tea',receipt:'bill.jpg'});
    const pe=proto.expenses.at(-1);
    assert.deepEqual([added.vendor,added.category,added.amount,added.method,added.date,added.status,added.notes,added.receipt],[pe.vendor,pe.category,pe.amount,pe.method,pe.date,pe.status,pe.notes,pe.receipt]);
    assert.deepEqual([res.expBad.r.reason,res.expBad.calls.length],['precheck',0]);assert.match(res.expBad.r.text,/funding source/);
    assert.deepEqual(cmds('SubmitExpenseDraft')[0].body,{expenseId:99,expectedVersion:'AAAAAAAAC99='});assert.equal(cmds('SubmitExpenseDraft')[0].key,'expsubmit:99');
    assert.equal(res.approveNoTill.r.ok,false);assert.match(res.approveNoTill.r.text,/till/);assert.equal(res.approveNoTill.calls.filter(c=>c.startsWith('POST')).length,0);
    assert.deepEqual(cmds('DecideExpense')[0].body,{expenseId:103,expectedVersion:'AAAAAAAAC103=',approve:true,tillId:1});assert.equal(cmds('DecideExpense')[0].key,'expense:103');
    assert.deepEqual(cmds('DecideExpense')[1].body,{expenseId:102,expectedVersion:'AAAAAAAAC102=',approve:false,reason:'Duplicate bill'},'UPI: no till');
    assert.deepEqual(cmds('ReverseExpense')[0].body,{expenseId:103,expectedVersion:'AAAAAAAAC103=',reason:'Wrong vendor',tillId:1});
    assert.equal(tillLearned,1,'till from the money area’s GET /cashier/desk (open shift)');
    assert.ok(res.approve.calls.includes('GET /cashier/desk')&&res.reverse.calls.includes('GET /cashier/desk'),'cash approve / reverse reload the cash desk (CASH_SLICES)');
    assert.ok(!res.reject.calls.includes('GET /cashier/desk'),'a rejection moves no cash');
    const o=afterReverse.find(e=>e.id==='103'),r=afterReverse.find(e=>e.reversalOf==='103');
    assert.deepEqual([o.status,o.reversedBy,r.amount,r.status],['Reversed',r.id,-42000,'Posted']);
  });
  test('[W4-7 compliance] SCC commands → MarkComplianceDone / NotApplicable / ReopenComplianceItem / SetComplianceFy / SaveComplianceProfile / archive / custom / evidence / settings / CA pack, then complianceX reloads',()=>{
    for(const k of ['done','na','reopen','fy','profile','arch','cat','custAdd','custEdit','custDel','evAdd','evDel','settings','pack'])assert.ok(res[k].r.ok,k+': '+res[k].r.text);
    assert.deepEqual(res.done.calls.slice(0,2),['POST /commands/MarkComplianceDone','GET /compliance/state']);assert.equal(cmds('MarkComplianceDone')[0].key,'cmp:done:1');
    assert.deepEqual(body('MarkComplianceDone'),{fyStart:2026,itemId:'oc4',filedOn:'2026-10-23',notes:'Ack 7',evidenceRef:'ARN-7',reviewerNote:'Owner'});
    assert.deepEqual(body('MarkComplianceNotApplicable'),{fyStart:2026,itemId:'oc2'},'the selected FY when the command carries none');
    assert.deepEqual(body('ReopenComplianceItem'),{fyStart:2026,itemId:'oc1',reason:'Wrong challan'});assert.deepEqual([res.reopenBad.r.reason,res.reopenBad.calls.length],['precheck',0]);
    assert.deepEqual(body('SetComplianceFy'),{fyStart:2027});
    assert.deepEqual(body('SaveComplianceProfile'),{name:'HCC Demo Residency',pan:'aahfh2604k',gstin:'27AAHFH2604K1Z9',entity:'partnership',gstType:'regular',hasEmployees:true,qrmp:false,notes:'',expectedVersion:'AAAAAAAAP1='});
    assert.deepEqual([body('ToggleComplianceArchive'),cmds('ToggleComplianceArchive')[0].key],[{key:'GSTR-1'},'cmp:arch:1']);assert.deepEqual(body('ToggleComplianceCategoryArchive'),{cat:'comp'});
    assert.deepEqual(cmds('SaveCustomObligation')[0].body,{cat:'lab',name:'Lift licence',freq:'Annual',dueDay:0,months:['March'],form:'',who:'',what:'',why:'',penalty:'',responsible:'',reviewer:'',
      risk:null,buffer:null,checklist:['Inspect','Pay'],workflow:[],evidence:[]});assert.deepEqual(res.custAdd.r.result,{id:'cust-13'});
    assert.deepEqual(cmds('SaveCustomObligation')[1].body,{customId:12,expectedVersion:'AAAAAAAAG12=',cat:'lab',name:'Fire NOC renewal',freq:'Annual',dueDay:30,months:['November'],form:'',who:'',what:'',
      why:'',penalty:'',responsible:'',reviewer:'',risk:'High',buffer:10,checklist:[],workflow:[],evidence:[]});
    assert.deepEqual(body('DeleteCustomObligation'),{customId:12});
    assert.deepEqual(body('AddComplianceEvidence'),{fyStart:2026,itemId:'oc4',fileName:'arn.pdf',contentType:'application/pdf',base64:'JVBERg=='});assert.deepEqual(body('DeleteComplianceEvidence'),{evidenceId:1});
    assert.deepEqual(body('SaveComplianceSettings'),{caName:'Rao & Co',caEmail:'ca@example.test',dueSoonDays:10,expectedVersion:'AAAAAAAAE1='});
    assert.deepEqual([body('GenerateCaPack'),cmds('GenerateCaPack')[0].key,res.pack.r.result],[{},'cmp:pack:1',{id:'5'}]);
  });

  // ── opening balances, permissions ──
  opening=[{account:'1030',name:'Bank account',group:'Asset',debitPaise:500000,creditPaise:0,balancePaise:500000},{account:'4000',name:'Room revenue',group:'Income',debitPaise:0,creditPaise:500000,balancePaise:-500000}];
  await rt.refresh();const s2=rt.get().state;
  const tb=M.accTrialBalance(M.accVouchersUpTo(s2,D)),tbP=M.accTrialBalance(M.accVouchersUpTo(P,D));
  const row=(t,a)=>t.find(r=>r.account===a)??{debit:0,credit:0};
  const acc=fakeServer({'GET /me/capabilities':()=>capabilities({roles:['ACC'],grants:[['PERM-RPT-001','A'],['PERM-EXP-001','A'],['PERM-ACC-001','A']]}),
    'GET /accounting/days':()=>[],'GET /accounting/trial-balance':()=>({rows:[]}),'GET /accounting/mapping':()=>[],'GET /accounting/mapping/history':()=>[],'GET /accounting/locks/history':()=>[],
    'GET /expenses':()=>({items:[],page:1,pageSize:100,total:0,ownOnly:true})});
  const ra=await startRuntime(S,acc);
  const hk=fakeServer({'GET /me/capabilities':()=>capabilities({roles:['HKP'],grants:[['PERM-HK-001','A']]})});
  const rh=await startRuntime(S,hk);
  test('[W4-7] Cumulative trial balance adds the server’s opening balances; an Accountant without PERM-CMP loads no compliance; a housekeeper loads nothing; nothing unmapped',()=>{
    assert.deepEqual([row(tb,'1030').debit-row(tbP,'1030').debit,row(tb,'4000').credit-row(tbP,'4000').credit],[500000,500000]);
    assert.equal(tb.reduce((a,r)=>a+r.debit-r.credit,0),0,'still balanced');
    assert.deepEqual(M.accTrialBalance(M.accVouchers(s2,D)),M.accTrialBalance(M.accVouchers(P,D)),'day movement has no opening');
    assert.equal(acc.gets().filter(c=>c.path.startsWith('/compliance')).length,0);assert.ok(acc.gets('/accounting/days').length===1&&acc.gets('/expenses').length===3);
    assert.deepEqual([ra.get().state.complianceX.records,ra.get().state.complianceX.profile],[{},undefined]);
    assert.equal(hk.gets().filter(c=>/^\/(accounting|expenses|compliance)/.test(c.path)).length,0);
    assert.deepEqual([rh.get().state.expenses,rh.get().state.accountingX?.server],[[],undefined]);
    const area=S.AREAS.find(a=>a.id==='accounting');assert.deepEqual(area.unmapped,{});
    assert.deepEqual(area.commands.map(c=>c.type).sort(),['ACC_LOCK','ACC_MAP_RESET','ACC_MAP_SET','ACC_TALLY_EXPORT','ACC_UNLOCK','CMP_ARCHIVE_CAT','CMP_ARCHIVE_KEY','CMP_CA_PACK','CMP_CUSTOM_DELETE',
      'CMP_CUSTOM_SAVE','CMP_DONE','CMP_EVIDENCE_ADD','CMP_EVIDENCE_DELETE','CMP_FY_SET','CMP_NA','CMP_PROFILE_SET','CMP_REOPEN','CMP_SETTINGS_SET','EXPENSE','EXPENSE_APPROVE','EXPENSE_REVERSE','EXPENSE_SUBMIT']);
  });
  // N5: the imported opening trial balance dated inside the window counts from its own date (names / groups of imported ledgers kept).
  const imp=[{account:'1000',name:'Cash in hand',group:'Asset',debitPaise:2500000,creditPaise:0,balancePaise:2500000},
    {account:'FURN01',name:'Furniture and fixtures',group:'Asset',debitPaise:120000000,creditPaise:0,balancePaise:120000000},
    {account:'CAP01',name:"Owner's capital",group:'Equity',debitPaise:0,creditPaise:122500000,balancePaise:-122500000}];
  const asOf=M.addDays(D,-3);
  tbExtra={openingAsOf:asOf,openingIncluded:false,opening:imp};await rt.refresh();const s3=rt.get().state;
  const tbImp=M.accTrialBalance(M.accVouchersUpTo(s3,D),s3.accountingX.server.accounts),tbBefore=M.accTrialBalance(M.accVouchersUpTo(s3,M.addDays(D,-4)),s3.accountingX.server.accounts);
  const tbBase=M.accTrialBalance(M.accVouchersUpTo(s2,D));
  // already inside the balance before the window: not added twice
  tbExtra={openingAsOf:'2026-01-01',openingIncluded:true,opening:imp};await rt.refresh();const s4=rt.get().state;
  tbExtra={};
  test('[N5 accounting] Imported opening balances (W4-13) join the cumulative trial balance from their own date, keep their ledger names / groups and stay balanced',()=>{
    assert.deepEqual(row(tbImp,'1000').debit-row(tbBase,'1000').debit,2500000);
    assert.deepEqual(tbImp.find(r=>r.account==='CAP01'),{account:'CAP01',name:"Owner's capital",group:'Equity',debit:0,credit:122500000,balance:-122500000});
    assert.equal(tbImp.find(r=>r.account==='FURN01').group,'Asset');
    assert.equal(tbImp.reduce((a,r)=>a+r.debit-r.credit,0),0,'balanced');
    assert.ok(!tbBefore.some(r=>r.account==='CAP01'),'not before its date');
    assert.equal(s3.accountingX.server.opening.filter(v=>v.source.sub==='opening-import').length,1);
    assert.equal(s4.accountingX.server.opening.filter(v=>v.source.sub==='opening-import').length,0);
    assert.deepEqual(M.accTrialBalance(M.accVouchers(s3,D)),M.accTrialBalance(M.accVouchers(P,D)),'day movement has no opening');
  });
  void reply;
}
