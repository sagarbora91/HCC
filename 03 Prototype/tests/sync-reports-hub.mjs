// AF-SYNC-OPS (OPS-03 / EX-01 interim): RPT_* favourites, schedules and the device export log in the production sync layer (clients/sync/reports-hub.ts).
import {loadSync,fakeServer,startRuntime} from './_sync-harness.mjs';

const sched=(id,over={})=>({id,reportId:'rpt.daily-collections',params:{range:'yesterday'},format:'xlsx',cadence:'weekly',day:1,time:'07:00:00',ownerPrincipalId:1,recipients:[1,2],enabled:true,lastRun:null,...over});

export async function register({test,assert}){
  const S=await loadSync();
  const srv=fakeServer({'GET /reports/favourites':()=>['rpt.daily-collections','rpt.occupancy'],
    'GET /reports/schedules':()=>[sched(5),sched(6,{cadence:'daily',day:null,format:'csv',lastRun:'2026-10-24T01:30:00.000Z'})],
    'GET /reports/runs':()=>[{id:9,scheduleId:5,reportId:'rpt.daily-collections',paramsJson:'{"from":"2026-10-23"}',principalId:1,startedAt:'2026-10-24T01:30:00.000Z',finishedAt:'2026-10-24T01:30:05.000Z',status:'ok',rows:12,fileRef:'f/9',error:null}],
    'POST /reports/favourites/:id':({params})=>({favourite:params.id==='rpt.occupancy'}),
    'POST /reports/schedules':()=>({id:7}),'PUT /reports/schedules/:sid':({params})=>({id:Number(params.sid)}),'DELETE /reports/schedules/:sid':()=>undefined,
    'POST /reports/:id/print-log':()=>({id:31})});
  const rt=await startRuntime(S,srv);
  const rx=rt.get().state.rptX;
  const fav=await rt.run({type:'RPT_FAV_TOGGLE',reportId:'rpt.occupancy',key:'r1'});
  const cad={kind:'weekly',day:1,time:'07:00'};
  const create=await rt.run({type:'RPT_SCHEDULE_SAVE',reportId:'rpt.daily-collections',params:{range:'yesterday'},format:'xlsx',cadence:cad,recipients:['1','2'],enabled:true,key:'r2'});
  const update=await rt.run({type:'RPT_SCHEDULE_SAVE',id:'5',reportId:'rpt.daily-collections',params:{range:'yesterday'},format:'csv',cadence:{kind:'daily',time:'08:30'},recipients:['2'],enabled:false,key:'r3'});
  const del=await rt.run({type:'RPT_SCHEDULE_DELETE',id:'6',key:'r4'});
  const log=await rt.run({type:'RPT_LOG',reportId:'rpt.daily-collections',params:{from:'2026-10-23',to:'2026-10-24'},format:'xlsx',rows:12,provisional:true,key:'r5'});
  const due=await rt.run({type:'RPT_RUN_DUE',key:'r6'});
  const call=(m,p)=>srv.calls.filter(c=>c.method===m&&c.path===p);

  test('[AF-RPT] favourites and schedules load into rptX (own favourites, cadence and recipients mapped, runs with parsed params)',()=>{
    assert.deepEqual(rx.favourites['1'],['rpt.daily-collections','rpt.occupancy']);
    assert.deepEqual(rx.schedules.map(s=>s.id),['5','6']);
    assert.deepEqual(rx.schedules[0].cadence,{kind:'weekly',day:1,time:'07:00'});assert.deepEqual(rx.schedules[0].recipients,['1','2']);assert.equal(rx.schedules[0].owner,'1');
    assert.equal(rx.schedules[1].cadence.day,undefined);assert.equal(rx.schedules[1].format,'csv');assert.equal(rx.schedules[1].lastRun,'2026-10-24T07:00');
    assert.equal(rx.runs[0].scheduleId,'5');assert.deepEqual(rx.runs[0].params,{from:'2026-10-23'});assert.equal(rx.runs[0].rows,12);assert.equal(rx.runs[0].status,'ok');
  });
  test('[AF-RPT] RPT_FAV_TOGGLE → POST /reports/favourites/{id} (0 /commands/ calls)',()=>{
    assert.equal(fav.ok,true);assert.equal(call('POST','/reports/favourites/rpt.occupancy').length,1);
    assert.equal(srv.calls.filter(c=>c.path.startsWith('/commands/')).length,0);
  });
  test('[AF-RPT] RPT_SCHEDULE_SAVE new → POST, existing → PUT /reports/schedules/{sid}, body = ScheduleRequest',()=>{
    assert.equal(create.ok,true);assert.deepEqual(call('POST','/reports/schedules')[0].body,{reportId:'rpt.daily-collections',params:{range:'yesterday'},format:'xlsx',cadence:'weekly',day:1,time:'07:00',recipients:[1,2],enabled:true});
    assert.equal(update.ok,true);assert.deepEqual(call('PUT','/reports/schedules/5')[0].body,{reportId:'rpt.daily-collections',params:{range:'yesterday'},format:'csv',cadence:'daily',time:'08:30',recipients:[2],enabled:false});
  });
  test('[AF-RPT] RPT_SCHEDULE_DELETE → DELETE /reports/schedules/{sid}',()=>{
    assert.equal(del.ok,true);assert.equal(call('DELETE','/reports/schedules/6').length,1);
  });
  test('[AF-RPT] RPT_LOG xlsx → POST /reports/{id}/print-log with format "xlsx", rows and provisional; the log id is returned',()=>{
    assert.equal(log.ok,true);assert.equal(log.result,31);
    assert.deepEqual(call('POST','/reports/rpt.daily-collections/print-log')[0].body,{params:{from:'2026-10-23',to:'2026-10-24'},rows:12,provisional:true,format:'xlsx'});
  });
  test('[AF-RPT] RPT_RUN_DUE stays unmapped',()=>{
    assert.equal(due.ok,false);assert.equal(due.reason,'unmapped');
  });
}
