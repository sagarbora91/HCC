// W4-7 accounting, expenses & compliance (sync/accounting.ts): fake .NET API shaped like Hcc.Contracts.Accounting / Expenses /
// Compliance, built FROM the prototype's own figures (M.seed()), so the slices the runtime assembles must make the unchanged
// prototype selectors (accVouchers, accTrialBalance, accExceptions, cmpItems, …) return the same values as in the prototype.
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

const EXTRA=['PERM-ACC-001','PERM-ACC-002','PERM-ACC-003','PERM-ACC-004','PERM-ACC-005','PERM-ACC-006','PERM-CMP-001','PERM-CMP-002'];
const ownerCaps=()=>{const c=capabilities();c.permissions.push(...EXTRA.map(permissionId=>({permissionId,mode:'A',scope:'P',limit:null,limitUnit:null})));return c;};
const noBooking=v=>({...v,source:{kind:v.source.kind,id:v.source.id,sub:v.source.sub}});
const srvVoucher=v=>{const t=v.entries.reduce((a,e)=>({d:a.d+e.debit,c:a.c+e.credit}),{d:0,c:0});
  return {number:v.no,date:v.date,type:v.type,narration:v.narration,sourceKey:v.source.id,sourceKind:v.source.kind,sub:v.source.sub,debitPaise:t.d,creditPaise:t.c,balanced:t.d===t.c,
    entries:v.entries.map(e=>({account:e.account,ledger:'x',debitPaise:e.debit,creditPaise:e.credit})),unmapped:[...v.unmapped]};};
const USERS={owner:1,manager:2,reception:3,cashier:4,accountant:8};
const rule=(from,r)=>({effectiveFrom:from,type:r.type,text:'',day:r.day??null,month:r.month??null,specialMonth:r.special?.month??null,specialDay:r.special?.day??null});

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
  let opening=[];
  // ── expenses: the prototype's two seed expenses + whatever SubmitExpense adds ──
  const exps=new Map();let nextExp=103;
  const srvExp=(id,e,over={})=>({expenseId:id,ref:'EXP-'+id,vendor:e.vendor,category:e.category,amountPaise:e.amount,method:e.method,businessDate:e.date,status:e.status,
    submittedBy:USERS[e.actor]??1,notes:e.notes??null,receipt:e.receipt??null,reason:null,decidedBy:null,decidedUtc:null,reversalOf:null,reversedBy:null,version:'AAAAAAAAC'+id+'=',...over});
  P.expenses.forEach(e=>exps.set(Number(e.id.slice(4)),srvExp(Number(e.id.slice(4)),e)));
  exps.set(99,srvExp(99,{vendor:'Old draft',category:'Supplies',amount:5000,method:'Cash',date:'2026-07-01',status:'Draft',actor:'owner'}));
  // ── compliance: the prototype's obligations / filings / settings with numeric ids ──
  const C=M.cmpSlice(P);const obId=new Map(C.obligations.map((o,i)=>[o.id,i+1]));
  const obligations=C.obligations.map((o,i)=>({obligationId:i+1,kind:o.kind,name:o.name,form:o.form,frequency:o.freq,owner:o.owner,trackedFrom:o.from,active:o.active,note:o.note??null,
    version:'AAAAAAAAD'+(i+1)+'=',rules:o.rules.map(r=>rule(r.from,r.rule))}));
  const calendar={asOf:D,items:M.cmpItems(P,true).map(i=>({obligationId:obId.get(i.obligation.id),kind:i.obligation.kind,name:i.obligation.name,form:'',owner:'',period:i.period,periodLabel:i.periodLabel,
      dueDate:i.due,days:i.days,status:i.status,statusText:'',filing:i.filing?{status:i.filing.status,by:USERS[i.filing.by]??1,atUtc:'2026-05-06T05:30:00Z',reference:i.filing.reference??null,reason:i.filing.reason??null}:null})),
    counts:{},settings:{...C.settings,version:'AAAAAAAAE1='},note:''};
  const srv=fakeServer({
    'GET /me/capabilities':()=>ownerCaps(),
    'GET /accounting/days':({query})=>M.accDates(P).filter(d=>d>=query.from&&d<=query.to).sort().map(day),
    'GET /accounting/trial-balance':()=>({date:'2026-08-23',cumulative:true,debitPaise:0,creditPaise:0,balanced:true,rows:opening}),
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
    'GET /compliance/obligations':()=>obligations,
    'GET /compliance/calendar':()=>calendar,
    'GET /compliance/ca-packs':()=>[{packId:4,atUtc:'2026-10-20T06:30:00Z',by:8,to:C.settings.caEmail,toName:C.settings.caName,overdue:1,due:2,filed:3,notApplicable:0,lines:['Position as on …']}],
    'POST /commands/MarkComplianceFiled':({body})=>({obligationId:body.obligationId,period:body.period,status:'Filed',dueDate:D,daysLate:0,atUtc:'2026-10-24T04:00:00Z'}),
    'POST /commands/MarkComplianceNotApplicable':({body})=>({obligationId:body.obligationId,period:body.period,status:'Not applicable',dueDate:D,daysLate:0,atUtc:'2026-10-24T04:00:00Z'}),
    'POST /commands/ReopenComplianceFiling':({body})=>({obligationId:body.obligationId,period:body.period,wasStatus:'Filed'}),
    'POST /commands/SaveComplianceObligation':({body})=>({obligationId:body.id??31,version:'v',outcome:body.id?'Updated':'Added',trackedFrom:'2026-09',ruleChanged:false}),
    'POST /commands/ToggleComplianceObligation':({body})=>({obligationId:body.obligationId,active:false,version:'v'}),
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
  test('[W4-7 expenses] expenses = GET /expenses (window + every Draft / Pending) mapped to the prototype Expense; compliance from obligations + calendar',()=>{
    assert.deepEqual(bootGets.filter(p=>p.startsWith('/expenses')).sort(),['/expenses','/expenses?Draft','/expenses?Pending']);
    assert.deepEqual(s.expenses.map(e=>e.id),['99','101','102']);
    const strip=e=>[e.vendor,e.category,e.amount,e.method,e.date,e.status];
    assert.deepEqual(s.expenses.slice(1).map(strip),P.expenses.map(strip));
    assert.deepEqual([s.expenses[1].actor,s.expenses[2].actor],['2','3']);
    assert.equal(rt.ids.version('expense','102'),'AAAAAAAAC102=');
    const key=i=>`${i.obligation.name}|${i.period}|${i.due}|${i.status}`;
    assert.deepEqual(M.cmpItems(s).map(key),M.cmpItems(P).map(key),'same periods, due dates and statuses');
    assert.deepEqual(M.cmpCounts(M.cmpItems(s)),M.cmpCounts(M.cmpItems(P)));
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
  const proto=M.execute(M.seed(),'owner',{type:'EXPENSE',vendor:'Petty Stores',category:'Petty cash',method:'Cash',amount:42000,receipt:'bill.jpg',notes:'Tea',draft:false});
  await seq('exp',{type:'EXPENSE',vendor:' Petty Stores ',category:'Petty cash',method:'Cash',amount:42000,receipt:'bill.jpg',notes:'Tea',draft:false,key:'modal-7:submit'});
  const added=rt.get().state.expenses.find(e=>e.id==='103');
  await seq('expBad',{type:'EXPENSE',vendor:'X',category:'Supplies',method:'Cheque',amount:100});
  await seq('submit',{type:'EXPENSE_SUBMIT',id:'99',key:'expsubmit:99'});
  await seq('approveNoTill',{type:'EXPENSE_APPROVE',id:'103',approve:true,key:'expense:103'});
  rt.ids.learn('till','current',1);
  await seq('approve',{type:'EXPENSE_APPROVE',id:'103',approve:true,key:'expense:103'});
  await seq('reject',{type:'EXPENSE_APPROVE',id:'102',approve:false,reason:'Duplicate bill',key:'m:9'});
  await seq('reverse',{type:'EXPENSE_REVERSE',id:'103',reason:'Wrong vendor',key:'m:10'});
  const afterReverse=rt.get().state.expenses;
  await seq('file',{type:'CMP_FILE',obligation:'1',period:'2026-09',reference:'TDS-202609-1',key:'cmp:file:1'});
  await seq('na',{type:'CMP_NA',obligation:'2',period:'2026-09',reason:'Nil return month'});
  await seq('reopen',{type:'CMP_REOPEN',obligation:'1',period:'2026-04',reason:'Wrong challan'});
  await seq('obEdit',{type:'CMP_OBLIGATION_SAVE',id:'1',kind:'TDS',name:'TDS deposit',form:'Challan 281',owner:'Accountant',rule:{type:'day',day:7,special:{month:3,day:30}},effectiveFrom:'2026-11-01'});
  await seq('obAdd',{type:'CMP_OBLIGATION_SAVE',kind:'GSTR-9',name:'GSTR-9 annual',form:'GST portal',owner:'',rule:{type:'fixed',month:12,day:31},effectiveFrom:D});
  await seq('toggle',{type:'CMP_OBLIGATION_TOGGLE',id:'3'});
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
    const o=afterReverse.find(e=>e.id==='103'),r=afterReverse.find(e=>e.reversalOf==='103');
    assert.deepEqual([o.status,o.reversedBy,r.amount,r.status],['Reversed',r.id,-42000,'Posted']);
  });
  test('[W4-7 compliance] CMP_FILE / NA / REOPEN / OBLIGATION_SAVE (edit with version, add with kind) / TOGGLE / SETTINGS / CA_PACK → server commands, then complianceX reloads',()=>{
    for(const k of ['file','na','reopen','obEdit','obAdd','toggle','settings','pack'])assert.ok(res[k].r.ok,k+': '+res[k].r.text);
    assert.deepEqual(res.file.calls.slice(0,2),['POST /commands/MarkComplianceFiled','GET /compliance/obligations']);assert.equal(cmds('MarkComplianceFiled')[0].key,'cmp:file:1');
    assert.deepEqual(body('MarkComplianceFiled'),{obligationId:1,period:'2026-09',reference:'TDS-202609-1'});
    assert.deepEqual(body('MarkComplianceNotApplicable'),{obligationId:2,period:'2026-09',reason:'Nil return month'});
    assert.deepEqual(body('ReopenComplianceFiling'),{obligationId:1,period:'2026-04',reason:'Wrong challan'});
    assert.deepEqual(cmds('SaveComplianceObligation')[0].body,{id:1,name:'TDS deposit',form:'Challan 281',owner:'Accountant',rule:{type:'day',day:7,specialMonth:3,specialDay:30},
      effectiveFrom:'2026-11-01',expectedVersion:'AAAAAAAAD1='});
    assert.deepEqual(cmds('SaveComplianceObligation')[1].body,{id:null,name:'GSTR-9 annual',form:'GST portal',owner:'Accountant',rule:{type:'fixed',month:12,day:31},kind:'GSTR-9'});
    assert.deepEqual(body('ToggleComplianceObligation'),{obligationId:3,expectedVersion:'AAAAAAAAD3='});
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
    assert.deepEqual(ra.get().state.complianceX.obligations,[]);
    assert.equal(hk.gets().filter(c=>/^\/(accounting|expenses|compliance)/.test(c.path)).length,0);
    assert.deepEqual([rh.get().state.expenses,rh.get().state.accountingX?.server],[[],undefined]);
    const area=S.AREAS.find(a=>a.id==='accounting');assert.deepEqual(area.unmapped,{});
    assert.deepEqual(area.commands.map(c=>c.type).sort(),['ACC_LOCK','ACC_MAP_RESET','ACC_MAP_SET','ACC_UNLOCK','CMP_CA_PACK','CMP_FILE','CMP_NA','CMP_OBLIGATION_SAVE','CMP_OBLIGATION_TOGGLE',
      'CMP_REOPEN','CMP_SETTINGS_SET','EXPENSE','EXPENSE_APPROVE','EXPENSE_REVERSE','EXPENSE_SUBMIT']);
  });
  void reply;
}
