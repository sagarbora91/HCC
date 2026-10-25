// MB-08 phone admin / closing / reports (M17–M23): the phone helpers re-shape desktop selectors — figures must be identical.
import {tillClear} from './_till.mjs';
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/mobile-admin-entry.ts',"export * from '../../src/mobile/admin/logic';");
  await build({entryPoints:['tests/.generated/mobile-admin-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/mobile-admin.mjs',logLevel:'error'});
  const X=await import('./.generated/mobile-admin.mjs');
  const {execute,loadScenario,seed,drawer}=M;
  const code=(fn)=>{try{fn();}catch(e){return e.code||'UNTYPED';}return 'NO_ERROR';};
  /** A busy S0 day: booking, payments, expense, interrupted + resumed night audit, cash submitted, closed. */
  function busyDay(){
    let s=loadScenario('S0');const R=(u,c)=>{s=execute(s,u,c);};
    R('reception',{type:'BOOK',guest:'Meera Kulkarni (fictional)',room:'105',arrival:s.date,departure:'2026-10-26',people:2,source:'Phone',key:'ma-book'});
    const id=s.bookings.at(-1).id;
    R('reception',{type:'PAYMENT',id,amount:120000,method:'UPI',kind:'payment',reference:'DEMO-UPI-77',key:'ma-adv'});
    R('reception',{type:'REGISTER',id});R('reception',{type:'CHECKIN',id});
    R('cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'deposit',key:'ma-dep'});
    R('manager',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});
    return s;
  }

  test('[MA] Report chips map 1:1 to desktop report tabs; KPI and drill-down totals equal desktop reportPack',()=>{
    let s=busyDay();s=execute(s,'manager',{type:'CLOSE_RUN'});
    assert.deepEqual(X.MA_REPORT_CHIPS.map(c=>c.id),['daily','revenue','receipts','expenses','occupancy']);
    for(const [from,to] of [[s.date,s.date],['2026-10-22',s.date]]){
      const desk=M.reportPack(s,from,to),phone=X.maReportPack(s,'owner',from,to);
      for(const c of X.MA_REPORT_CHIPS){const dt=desk.tabs.find(t=>t.id===c.id),pt=phone.tabs.find(t=>t.id===c.id);
        assert.ok(dt&&pt,c.id);
        assert.deepEqual(pt.metrics.map(m=>[m.id,m.value,m.rows.length]),dt.metrics.map(m=>[m.id,m.value,m.rows.length]));
        assert.deepEqual(pt.ratios,dt.ratios);
        for(const m of pt.metrics)assert.equal(X.maDrillTotal(m),m.value,`${c.id}/${m.id} rows ≠ figure`);}
    }
    // Known fixture figures survive (collections ₹21,500 before today's activity).
    assert.equal(M.metrics(seed()).collections,2150000);
  });
  test('[MA] Restricted scope masks guest names on phone but never changes a figure',()=>{
    const s=busyDay();
    const role=s.users.find(u=>X.maMaskReports(s,u.id)&&M.can(s,u.id,'reports'));
    const plain=X.maScopedPack(M.reportPack(s,s.date,s.date),false),masked=X.maScopedPack(M.reportPack(s,s.date,s.date),true);
    for(const [i,t] of masked.tabs.entries())for(const [j,m] of t.metrics.entries()){assert.equal(m.value,plain.tabs[i].metrics[j].value);
      for(const r of m.rows)if(r.guest)assert.match(r.guest,/^(\S•••)( \S•••)*$/);}
    assert.equal(X.maMaskName('Meera Kulkarni'),'M••• K•••');
    if(role)assert.equal(JSON.stringify(X.maReportPack(s,role.id,s.date,s.date)).includes('Meera'),false);
    assert.equal(X.maMaskReports(s,'owner'),false);
  });
  test('[MA] Date range rules match desktop: future, reversed and > 62 days refused',()=>{
    const d='2026-10-24';
    assert.equal(X.maRangeCheck(d,d,d).ok,true);
    assert.equal(X.maRangeCheck('2026-10-25','2026-10-25',d).ok,false);
    assert.equal(X.maRangeCheck('2026-10-24','2026-10-20',d).ok,false);
    assert.equal(X.maRangeCheck('2026-08-24',d,d).ok,true);assert.equal(X.maRangeCheck('2026-08-24',d,d).span,62);
    assert.match(X.maRangeCheck('2026-08-23',d,d).error,/62 days/);
    assert.equal(X.maRangeCheck('bad',d,d).ok,false);
    assert.equal(X.maReportPack(seed(),'owner','2026-10-25','2026-10-25'),null);
    assert.deepEqual(X.maRangePresets(d).map(p=>[p.from,p.to]),[[d,d],['2026-10-22',d],['2026-10-18',d]]);
  });

  test('[MA] Closing stepper: blockers are tappable rows that point at the record or page to fix',()=>{
    const s=loadScenario('S0'),md=X.maClosingModel(s);
    assert.deepEqual(md.steps.map(x=>x.id),['preflight','nights','cash','close','summary','next']);
    assert.equal(md.closed,false);assert.equal(md.resume,undefined);
    const pf=M.closingPreflight(s).filter(c=>c.id!=='nights'&&c.id!=='shift');
    const items=pf.reduce((a,c)=>a+c.items.length,0);
    assert.equal(md.steps[0].blockers.length,items);
    for(const b of md.steps.flatMap(x=>x.blockers))assert.ok(b.booking||b.page||b.step,'blocker without a target: '+b.label);
    assert.equal(md.steps[0].state,items?'blocked':'done');
    assert.equal(md.steps[3].state,md.blocking?'waiting':'current');
    assert.equal(md.steps[3].blockers.length,M.closingPreflight(s).filter(c=>c.items.length).length);
    assert.equal(md.steps[1].blockers.length,md.toPost);
    assert.equal(md.steps[5].state,'waiting');
  });
  test('[MA] Closing stepper shows resume after an interrupted night audit; resume posts the rest once',()=>{
    let s=busyDay();const before=X.maClosingModel(s);
    s=execute(s,'manager',{type:'CLOSE_RUN',interruptAfter:3});
    const md=X.maClosingModel(s);
    assert.deepEqual(md.resume,{posted:3,total:before.toPost,remaining:before.toPost-3});
    assert.equal(md.steps[1].state,'blocked');assert.match(md.steps[1].summary,/Interrupted after 3 of/);
    s=execute(s,'manager',{type:'CLOSE_RUN'});
    const after=X.maClosingModel(s);assert.equal(after.resume,undefined);assert.equal(after.steps[1].state,'done');assert.equal(after.toPost,0);
  });
  test('[MA] AC-M08 Manager closes the day following the phone stepper; summary equals desktop closing record',()=>{
    let s=busyDay();
    s=execute(s,'manager',{type:'CLOSE_RUN',interruptAfter:2});s=execute(s,'manager',{type:'CLOSE_RUN'});
    s=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)});s=tillClear(M,s);
    let md=X.maClosingModel(s);
    assert.equal(md.steps[2].state,'done');assert.equal(md.steps[3].state,'current',JSON.stringify(md.steps[3]));assert.equal(md.focus,3);
    const preview=M.closingSummary(s);
    s=execute(s,'manager',{type:'DAY_CLOSE',key:'dayclose:'+s.date});
    md=X.maClosingModel(s);
    assert.equal(md.closed,true);assert.ok(md.record);assert.equal(md.steps[4].state,'done');assert.equal(md.steps[5].state,'current');assert.equal(md.focus,5);
    assert.equal(md.record.revenue.net,preview.revenue.net);assert.equal(md.record.receipts.net,preview.receipts.net);
    // Report "Daily operations" net revenue for the closed date equals the closing record.
    const daily=X.maReportPack(s,'manager',s.date,s.date).tabs.find(t=>t.id==='daily');
    assert.equal(daily.metrics.find(m=>m.id==='net-revenue').value,md.record.revenue.net);
    assert.equal(daily.metrics.find(m=>m.id==='net-receipts').value,md.record.receipts.net);
    assert.equal(code(()=>execute(s,'manager',{type:'DAY_CLOSE',key:'dayclose-rerun'})),'DAY_CLOSED');
    const d=s.date;s=execute(s,'manager',{type:'NEXT_DAY',key:'nextday:'+d});
    assert.equal(s.date,'2026-10-25');assert.equal(X.maClosingModel(s).closed,false);
  });

  test('[MA] Approval cards: own request disabled with explanation; another manager may approve or reject with reason',()=>{
    let s=seed();
    s=execute(s,'owner',{type:'BOOK',guest:'Card Test (fictional)',room:'105',arrival:s.date,departure:'2026-10-26',people:2});
    const id=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'reception',{type:'DISCOUNT',id,amount:50000,reason:'Service recovery'});
    const row=()=>M.adminApprovalInbox(s).find(r=>r.a.booking===id&&r.a.status==='Pending');
    const own=X.maApprovalActions(s,'reception',row());
    assert.equal(own.own,true);assert.match(own.approveBlock,/raised this request/);assert.ok(own.rejectBlock);
    const mgr=X.maApprovalActions(s,'manager',row());
    assert.equal(mgr.approveBlock,undefined);assert.equal(mgr.rejectBlock,undefined);assert.equal(mgr.live,true);
    const hk=X.maApprovalActions(s,'housekeeper',row());assert.ok(hk.approveBlock);
    const r=row();
    const next=execute(s,'manager',{type:r.command,id:r.a.id,approve:false,reason:'Offer late checkout instead',key:'ma-rej'});
    assert.equal(next.approvals.find(a=>a.id===r.a.id).status,'Rejected');
    assert.deepEqual(execute(next,'manager',{type:r.command,id:r.a.id,approve:false,reason:'again',key:'ma-rej'}),next); // idempotent
    assert.equal(X.maApprovalActions(next,'manager',M.adminApprovalInbox(next).find(x=>x.a.id===r.a.id)).approveBlock,'Already decided');
    // Expired: approve refused with a reason, reject still offered.
    const old={...s,clock:'2026-10-27T09:00'};const er=M.adminApprovalInbox(old).find(x=>x.a.id===r.a.id);
    if(er.status==='Expired'){const ex=X.maApprovalActions(old,'manager',er);assert.match(ex.approveBlock,/Expired/);assert.equal(ex.rejectBlock,undefined);}
  });
  test('[MA] Expense cards: submitter cannot decide own expense; housekeeper never can',()=>{
    let s=execute(seed(),'reception',{type:'EXPENSE',vendor:'Ma Test Vendor',category:'Supplies',amount:10000,method:'UPI'});
    let e=s.expenses.at(-1);
    if(e.status==='Draft'){s=execute(s,'reception',{type:'EXPENSE_SUBMIT',id:e.id});e=s.expenses.at(-1);}
    assert.equal(e.status,'Pending');
    assert.match(X.maExpenseBlock(s,'reception',e),/You submitted/);
    assert.equal(X.maExpenseBlock(s,'manager',e),undefined);
    assert.match(X.maExpenseBlock(s,'housekeeper',e),/lacks the approve permission/);
  });
  test('[MA] Audit filter sheet: count of active filters and options derive from the log',()=>{
    const s=busyDay();
    assert.equal(X.maAuditFilterCount({}),0);assert.equal(X.maAuditFilterCount({actor:'x',text:'  ',from:'2026-10-24'}),2);
    const o=X.maAuditOptions(s);assert.ok(o.actors.length>0&&o.actions.length>0);
    assert.deepEqual(o.actions,[...o.actions].sort());
    const f={action:o.actions[0]};assert.ok(M.adminAuditFilter(s,f).every(a=>a.action===o.actions[0]));
  });
  test('[MA] Users list: own account and last active owner protected; enable/disable via USER',()=>{
    const s=seed();
    assert.match(X.maUserBlock(s,'owner',s.users.find(u=>u.id==='owner')),/own access/);
    const rec=s.users.find(u=>u.id==='reception');assert.equal(X.maUserBlock(s,'owner',rec),undefined);
    assert.ok(X.maUserBlock(s,'reception',rec)); // no admin permission
    const next=execute(s,'owner',{type:'USER',id:'reception',key:'ma-user'});
    assert.equal(next.users.find(u=>u.id==='reception').enabled,false);
    const others=s.users.filter(u=>u.role==='Owner'&&u.id!=='owner'&&u.enabled);
    if(!others.length){const admin=s.users.find(u=>u.id!=='owner'&&M.authorize(s,u.id,'PERM-ADM-001').decision==='allow');
      if(admin)assert.match(X.maUserBlock(s,admin.id,s.users.find(u=>u.id==='owner')),/Last active owner/);}
  });
  test('[MA] Roadmap areas 13–26 show the current prototype build; external ones say simulated',()=>{
    assert.equal(M.HK_ROADMAP.length,14);
    for(const r of M.HK_ROADMAP)assert.equal(r.current,'Built in prototype (simulated integrations where external)');
    for(const no of ['21','22','24','25','26'])assert.match(M.HK_ROADMAP.find(r=>r.no===no).disposition,/[Ss]imulated/);
  });
}
