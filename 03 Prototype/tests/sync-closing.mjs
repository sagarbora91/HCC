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

export async function register({test,assert,M}){
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
  // Next day's cashier shift (DEC-035): after a Completed run the desk till (learned by the money area) gets OpenShift, float carried by the server.
  let deskOpen=false;
  const tillSrv=fakeServer({'GET /closing/business-date':()=>({businessDate:'2026-10-24',closed:false}),'GET /closing/records':()=>({items:[]}),
    'GET /cashier/desk':()=>({businessDate:deskOpen?'2026-10-25':'2026-10-24',tills:[{till:{tillId:3,code:'FRONT',name:'Front desk',version:'T1'},active:deskOpen?{shift:{shiftId:42,tillId:3,businessDate:'2026-10-25',
      status:'Open',openingFloatPaise:522000,expectedPaise:null,countedPaise:null,approvalId:null,version:'S42'},submittedBy:null,differenceReason:null,closedBy:null,approval:null}:null,expected:null,lastClosed:null}]}),
    'POST /commands/RunBusinessDayClose':({body})=>({runId:4,runRef:body.runId,businessDate:body.businessDate,status:'Completed',currentStep:'done',attempts:1,nextBusinessDate:'2026-10-25',snapshotId:4,steps:[]}),
    'POST /commands/OpenShift':({body})=>{deskOpen=true;return {shiftId:42,tillId:body.tillId,businessDate:'2026-10-25',status:'Open',openingFloatPaise:522000,expectedPaise:null,countedPaise:null,approvalId:null,version:'S42'};}});
  const trt=await startRuntime(S,tillSrv);tillSrv.reset();const opened=await trt.run({type:'DAY_CLOSE',key:'dayclose:t'});
  const os=tillSrv.commands('OpenShift');const tillCalls=tillSrv.calls.map(c=>`${c.method} ${c.path}`);
  const noPerm=fakeServer({'GET /me/capabilities':()=>capabilities({grants:capabilities().permissions.map(g=>[g.permissionId,'A']).filter(([p])=>p!=='PERM-SHF-001')}),
    'GET /cashier/desk':()=>({businessDate:'2026-10-24',tills:[{till:{tillId:3,code:'FRONT',name:'Front desk',version:'T1'},active:null,expected:null,lastClosed:null}]}),
    'POST /commands/RunBusinessDayClose':({body})=>({runId:5,runRef:body.runId,businessDate:body.businessDate,status:'Completed',currentStep:'done',attempts:1,nextBusinessDate:'2026-10-25',snapshotId:5,steps:[]}),
    'GET /closing/business-date':()=>({businessDate:'2026-10-24',closed:false})});
  const nrt=await startRuntime(S,noPerm);const np=await nrt.run({type:'DAY_CLOSE',key:'dayclose:n'});
  test('[W4-4 closing] A Completed close opens the next cashier shift: OpenShift {desk till, float carried} key <action>:shift, cash desk reloads; not without PERM-SHF-001 or a known till',()=>{
    assert.ok(opened.ok,opened.text);
    assert.deepEqual(os.map(c=>[c.key,c.body]),[['dayclose:t:shift',{tillId:3,openingFloatPaise:null}]]);
    assert.ok(tillCalls.indexOf('GET /cashier/desk')>tillCalls.indexOf('POST /commands/OpenShift'),tillCalls.join(','));
    assert.equal(trt.get().state.shiftOpen,true,'the reloaded desk shows the new open shift');
    assert.ok(np.ok,np.text);assert.equal(noPerm.commands('OpenShift').length,0);
    assert.equal(srv.commands('OpenShift').length,0,'first fake: no desk till learned → no OpenShift');
  });
  srv.down=true;const off=await startRuntime(S,srv,{cache:S.createMemoryCache()});void off;
  test('[W4-4 closing] The area declares what stays unmapped (corrections, POST_NIGHTS) with reasons',()=>{
    const u=S.AREAS.find(a=>a.id==='closing').unmapped;
    assert.deepEqual(Object.keys(u).sort(),['CORRECTION_ADJUST','CORRECTION_ADJUST_DECIDE','POST_NIGHTS']);
    assert.deepEqual(S.AREAS.find(a=>a.id==='closing').commands.map(c=>c.type).sort(),['CLOSE_RUN','DAY_CLOSE','DAY_RECLOSE','DAY_REOPEN','NEXT_DAY','NIGHT_POST']);
  });

  // DEC-077: reopens and replaced records ride on the records view (attached to their date); the open reopen of a day outside the
  // 62-day window is read on its own; DAY_REOPEN → ReopenBusinessDay, DAY_RECLOSE → RunBusinessDayClose for the reopened date.
  const OLD='2026-08-01';let openOld=false;
  const reopenOf=(id,date,open)=>({reopenId:id,businessDate:date,status:open?'Reopened':'Closed',reopenedBy:1,reopenedUtc:'2026-10-25T04:00:00Z',reason:'Wrong folio',priorSnapshotId:3,
    cashBookLocked:true,tallyReexport:!open,reclosedBy:open?null:2,reclosedUtc:open?null:'2026-10-25T05:00:00Z',snapshotId:open?null:4});
  const rsrv=fakeServer({
    'GET /closing/business-date':()=>({businessDate:'2026-10-25',closed:false,reopenedDate:openOld?OLD:null}),
    'GET /closing/records':({query})=>query.from===OLD?{from:OLD,to:OLD,items:[snap(OLD,9)],replaced:[],reopens:[reopenOf(5,OLD,true)]}
      :{from:query.from,to:query.to,items:[snap('2026-10-23',2),{...snap('2026-10-24',4),reopenId:1}],replaced:[{...snap('2026-10-24',3),supersededUtc:'2026-10-25T05:00:00Z'}],
        reopens:[reopenOf(1,'2026-10-24',false),...(openOld?[reopenOf(5,OLD,true)]:[])]},
    'POST /commands/ReopenBusinessDay':({body})=>{openOld=true;return {reopen:reopenOf(5,body.businessDate,true),message:`Business day ${body.businessDate} reopened.`};},
    'POST /commands/RunBusinessDayClose':({body})=>{openOld=false;return {runId:1,runRef:body.runId,businessDate:body.businessDate,status:'Completed',currentStep:'done',attempts:1,nextBusinessDate:null,snapshotId:10,steps:[]};},
  });
  const rrt=await startRuntime(S,rsrv);const r0=rrt.get().state;
  rsrv.reset();const badReopen=await rrt.run({type:'DAY_REOPEN',date:OLD,reason:'  '});const badCalls=rsrv.calls.length;
  const re=await rrt.run({type:'DAY_REOPEN',date:OLD,reason:' Wrong folio ',key:'reopen:1'});const reCmd=rsrv.commands('ReopenBusinessDay')[0];const r1=rrt.get().state;
  const oldReads=rsrv.gets('/closing/records').map(g=>g.query.from);
  const shownReopened=[P.renderPages(r1,'1',false),P.renderPages(r1,'1',true)];
  rsrv.reset();const rcl=await rrt.run({type:'DAY_RECLOSE',date:OLD,key:'reclose:1'});const rcCmd=rsrv.commands('RunBusinessDayClose')[0];const r2=rrt.get().state;
  test('[DEC-077 closing] Records carry reopens (who, when, cash-book lock, Tally) and replaced records on their date; a re-closed day shows no open reopen',()=>{
    const c=r0.closings['2026-10-24'];
    assert.equal(c.replaced.length,1);assert.equal(c.replaced[0].date,'2026-10-24');
    assert.deepEqual(c.reopens,[{id:'1',date:'2026-10-24',by:'1',at:'2026-10-25T09:30',reason:'Wrong folio',cashBookLocked:true,tallyReexport:true,reclosedBy:'2',reclosedAt:'2026-10-25T10:30'}]);
    assert.equal(M.closingReopened(r0),undefined);assert.equal(r0.closings['2026-10-23'].reopens,undefined);
  });
  test('[DEC-077 closing] DAY_REOPEN → ReopenBusinessDay {businessDate, reason} (reason precheck); the open reopen outside the window is read on its own; DAY_RECLOSE → RunBusinessDayClose for that date',()=>{
    assert.deepEqual([badReopen.ok,badReopen.reason,badCalls],[false,'precheck',0]);
    assert.ok(re.ok,re.text);assert.deepEqual([reCmd.key,reCmd.body],['reopen:1',{businessDate:OLD,reason:'Wrong folio'}]);
    assert.ok(oldReads.includes(OLD),'the old reopened day was read on its own: '+oldReads.join(','));
    assert.equal(M.closingReopened(r1)?.date,OLD);assert.equal(M.closingPreflight(r1).find(x=>x.id==='reopened').items.length,1);
    assert.deepEqual(shownReopened.map(x=>x.errors),[{},{}],'every desktop and phone page renders with a reopened day');
    assert.ok(rcl.ok,rcl.text);assert.deepEqual([rcCmd.key,rcCmd.body],['reclose:1',{businessDate:OLD,runId:'reclose-'+OLD}]);
    assert.equal(M.closingReopened(r2),undefined);
  });
}
