// W4-4 closing area (sync/closing.ts): business date patch, closing records + closed dates, CLOSE_RUN / DAY_CLOSE / NEXT_DAY mappers,
// server report → ReportPack, offline cache. Fake routes are shaped like the .NET DTOs (Hcc.Contracts/Closing, /Reporting).
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';
import {build} from 'esbuild';
const FIG=(process.env.HCC_SYNC_TEST_DIR||'tests/.generated')+'/closing-figures.mjs';

const till={tillCode:'FD-1',shifts:1,status:'Closed',openingPaise:500000,cashInPaise:150000,cashOutPaise:0,expensesPaise:180000,restaurantPaise:52500,expectedPaise:522500,
  countedPaise:522000,differencePaise:-500,countedBy:2,approvedBy:3,reason:'Short change'};
const figures=(d)=>({from:d,to:d,
  occupancy:{sellableRoomNights:10,occupiedRoomNights:8,occupancyPct:'80.0%',roomRevenuePaise:1600000,adrPaise:200000,revParPaise:160000},
  revenue:{roomPaise:1600000,servicePaise:50000,discountsPaise:-20000,correctionsPaise:0,netPaise:1630000,taxPaise:81500,cgstPaise:40750,sgstPaise:40750,grossPaise:1711500},
  gst:[],receipts:{byTender:[{tender:'Cash',paymentsPaise:150000,refundsPaise:0,netPaise:150000},{tender:'UPI',paymentsPaise:300000,refundsPaise:0,netPaise:300000}],
    netPaise:450000,depositsInPaise:50000,depositsOutPaise:0,companyCreditPaise:0},
  expenses:{cashPaise:180000,reversalsPaise:0,totalPaise:180000,note:''},
  drawer:{tills:[till],openingPaise:500000,cashInPaise:150000,cashOutPaise:0,expectedPaise:522500,countedPaise:522000,differencePaise:-500},
  days:[{date:d,arrivals:2,departures:1,sellable:10,occupied:8,netRevenuePaise:1630000,taxPaise:81500,netReceiptsPaise:450000,cashExpensesPaise:180000}]});
const snap=(d,id)=>({snapshotId:id,sha256:'AB'+id,record:{businessDate:d,closedBy:1,closedUtc:d+'T17:30:00.000Z',runId:id,attempts:2,nightAuditResumed:true,nightAuditStays:8,
  depositsHeldAtClosePaise:50000,exceptions:['Cash difference −₹5 on till FD-1 accepted'],figures:figures(d)}});

export async function register({test,assert}){
  const S=await loadSync();
  await build({entryPoints:['src/clients/sync/closing/figures.ts'],bundle:true,platform:'node',format:'esm',outfile:FIG,logLevel:'error'});
  const F=await import(new URL('../'+FIG,import.meta.url).href);
  let bd={businessDate:'2026-10-24',closed:false},recordItems=[snap('2026-10-22',1),snap('2026-10-23',2)];
  const srv=fakeServer({
    'GET /closing/business-date':()=>bd,
    'GET /closing/records':({query})=>({from:query.from,to:query.to,items:recordItems}),
    'POST /commands/PostRoomNights':({body})=>({businessDate:body.businessDate,stays:3,postedNow:3,alreadyPosted:0}),
    'POST /commands/RunBusinessDayClose':({body})=>{bd={businessDate:'2026-10-25',closed:false};recordItems=[...recordItems,snap(body.businessDate,3)];
      return {runId:3,runRef:body.runId,businessDate:body.businessDate,status:'Completed',currentStep:'done',attempts:1,nextBusinessDate:'2026-10-25',snapshotId:3,steps:[]};},
  });
  const rt=await startRuntime(S,srv);
  const s0=rt.get().state;
  test('[W4-4 closing] The business date patches the provisional clock date; closings carry the last 62 days of records mapped to the prototype ClosingRecord; closedDates = record dates',()=>{
    assert.equal(s0.date,'2026-10-24');
    const q=srv.gets('/closing/records')[0].query;assert.deepEqual([q.from,q.to],['2026-08-24','2026-10-24']);
    assert.deepEqual(Object.keys(s0.closings),['2026-10-22','2026-10-23']);
    const c=s0.closings['2026-10-23'];
    assert.deepEqual([c.date,c.closedAt,c.closedBy],['2026-10-23','2026-10-23T23:00'.replace('23:00','23:00'),'1']);
    assert.deepEqual(c.revenue,{room:1600000,service:50000,discounts:-20000,adjustments:0,net:1630000,tax:81500});
    assert.deepEqual(c.receipts,{byTender:{Cash:150000,UPI:300000},net:450000,companyCredit:0});
    assert.deepEqual(c.deposits,{received:50000,returned:0,heldAtClose:50000});
    assert.deepEqual([c.expenses.cash,c.expenses.total],[180000,180000]);
    assert.deepEqual([c.occupancy.sellable,c.occupancy.occupied,c.occupancy.occupancy,c.occupancy.adr,c.occupancy.revpar],[10,8,'80.0%','₹2,000','₹1,600']);
    assert.deepEqual([c.cash.opening,c.cash.expected,c.cash.counted,c.cash.difference,c.cash.reason,c.cash.approvedBy,c.cash.countedBy,c.cash.restaurant],[500000,522500,522000,-500,'Short change','3','2',52500]);
    assert.deepEqual([c.nightAudit,c.exceptions.length],[{stays:8,resumed:true},1]);
    assert.deepEqual(s0.closedDates,['2026-10-22','2026-10-23']);
  });

  // The closing summary of the prototype's own screen renders from the server record; pages render on it.
  const P=await loadPages();const shown=[P.renderPages(rt.get().state,'1',false),P.renderPages(rt.get().state,'1',true)];
  test('[W4-4 closing] Every desktop and phone page renders on the loaded closing data',()=>{assert.deepEqual(shown.map(x=>x.errors),[{},{}]);});

  srv.reset();
  const nights=await rt.run({type:'CLOSE_RUN',key:'cr:1'});const nightCalls=srv.calls.map(c=>`${c.method} ${c.path}`);
  const pn=srv.commands('PostRoomNights')[0];
  const demo=await rt.run({type:'CLOSE_RUN',interruptAfter:2});
  srv.reset();
  const post=await rt.run({type:'NIGHT_POST',key:'np:1'});
  test('[W4-4 closing] CLOSE_RUN → PostRoomNights {businessDate} with the dialog key, then reloads bookings; the interrupt demo is refused locally; NIGHT_POST maps the same way',()=>{
    assert.ok(nights.ok,nights.text);assert.deepEqual(nights.result,{businessDate:'2026-10-24',stays:3,postedNow:3,alreadyPosted:0});
    assert.deepEqual([pn.key,pn.body],['cr:1',{businessDate:'2026-10-24'}]);
    assert.deepEqual(nightCalls,['POST /commands/PostRoomNights','GET /reservations'],'bookings reload = the W4-1 reservation list');
    assert.deepEqual([demo.ok,demo.reason],[false,'precheck']);assert.match(demo.text,/interrupted night-audit demo/);
    assert.ok(post.ok);assert.equal(srv.commands('PostRoomNights')[0].key,'np:1');
  });

  srv.reset();
  const closed=await rt.run({type:'DAY_CLOSE',key:'dayclose:2026-10-24'});const after=rt.get().state;
  const rc=srv.commands('RunBusinessDayClose')[0];
  const reloaded=srv.calls.filter(c=>c.method==='GET').map(c=>c.path);
  test('[W4-4 closing] DAY_CLOSE → RunBusinessDayClose {businessDate, runId} (one run also opens the next day); date, closedDates and closings reload',()=>{
    assert.ok(closed.ok,closed.text);assert.equal(closed.result.status,'Completed');
    assert.deepEqual([rc.key,rc.body],['dayclose:2026-10-24',{businessDate:'2026-10-24',runId:'close-2026-10-24'}]);
    for(const p of ['/closing/business-date','/closing/records'])assert.ok(reloaded.includes(p),p+' reloaded');
    assert.equal(after.date,'2026-10-25');assert.deepEqual(after.closedDates,['2026-10-22','2026-10-23','2026-10-24']);
    assert.equal(after.closings['2026-10-24'].nightAudit.stays,8);
  });

  // Closed, next day not opened yet: closedDates includes the current date; NEXT_DAY resumes the run.
  bd={businessDate:'2026-10-25',closed:true};await rt.refresh();
  const st=rt.get().state;srv.reset();
  const next=await rt.run({type:'NEXT_DAY',key:'nextday:2026-10-25'});
  test('[W4-4 closing] A closed current date shows as closed (isDayClosed); NEXT_DAY resumes via RunBusinessDayClose for that date',()=>{
    assert.ok(st.closedDates.includes('2026-10-25'));
    assert.ok(next.ok);assert.deepEqual(srv.commands('RunBusinessDayClose')[0].body,{businessDate:'2026-10-25',runId:'close-2026-10-25'});
  });

  // A server refusal (blocked close) surfaces as the command error; nothing changes locally.
  srv.on('POST /commands/RunBusinessDayClose',()=>reply(409,{code:'INVALID_TRANSITION',message:'Day close is blocked: arrivals not resolved.'}));
  const blocked=await rt.run({type:'DAY_CLOSE',key:'dayclose:x'});
  // Users without report permission: no records read; the business date still loads.
  const recep=fakeServer({'GET /me/capabilities':()=>capabilities({roles:['REC'],grants:[['PERM-RES-001','A'],['PERM-STY-001','A']]}),
    'GET /closing/business-date':()=>({businessDate:'2026-10-24',closed:true})});
  const rr=await startRuntime(S,recep);
  test('[W4-4 closing] A blocked close is reported, not applied; a receptionist loads the business date and closed flag but never reads closing records',()=>{
    assert.deepEqual([blocked.ok,blocked.reason],[false,'server']);assert.match(blocked.text,/blocked/i);
    assert.equal(recep.calls.filter(c=>c.path==='/closing/records').length,0);
    assert.equal(rr.get().state.date,'2026-10-24');assert.deepEqual(rr.get().state.closedDates,['2026-10-24']);assert.deepEqual(Object.keys(rr.get().state.closings??{}),[]);
  });

  // Server report → ReportPack (figures). Read-only cache and unmapped list.
  const view={report:figures('2026-10-23'),dayStatus:[{date:'2026-10-23',status:'Closed',matchesSnapshot:true}],businessDate:'2026-10-24',note:''};
  const pack=F.reportPackFromServer(view);
  const tab=id=>pack.tabs.find(t=>t.id===id),met=(t,id)=>tab(t).metrics.find(x=>x.id===id).value;
  test('[W4-4 closing] reportPackFromServer carries the Reports tabs and metric ids with the server figures',()=>{
    assert.deepEqual(pack.tabs.map(t=>t.id),['daily','revenue','receipts','expenses','occupancy']);
    assert.deepEqual([met('revenue','net'),met('revenue','tax'),met('receipts','net'),met('receipts','tender:UPI'),met('expenses','total'),met('occupancy','occupied'),met('daily','arrivals')],
      [1630000,81500,450000,300000,180000,8,2]);
    assert.deepEqual(tab('occupancy').ratios.map(r=>r.display),['80.0%','₹2,000','₹1,600']);
    assert.deepEqual(tab('daily').table.rows[0],['2026-10-23','Closed',2,1,8,1630000,450000,180000]);
  });
  srv.down=true;const off=await startRuntime(S,srv,{cache:S.createMemoryCache()});void off;
  test('[W4-4 closing] The area declares what stays unmapped (corrections, POST_NIGHTS) with reasons',()=>{
    const u=S.AREAS.find(a=>a.id==='closing').unmapped;
    assert.deepEqual(Object.keys(u).sort(),['CORRECTION_ADJUST','CORRECTION_ADJUST_DECIDE','POST_NIGHTS']);
    assert.deepEqual(S.AREAS.find(a=>a.id==='closing').commands.map(c=>c.type).sort(),['CLOSE_RUN','DAY_CLOSE','NEXT_DAY','NIGHT_POST']);
  });
}
