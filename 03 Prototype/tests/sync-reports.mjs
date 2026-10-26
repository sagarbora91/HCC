// W4 gap G3: server reports (sync/reports.ts). Reports screen, day pack, history over months and Overview/Home tiles read GET /reports/daily
// (fake fetch shaped like Hcc.Contracts.Reporting) — figures equal the server's; drill-down rows are never invented.
import {loadSync,loadPages,fakeServer,startRuntime,reply} from './_sync-harness.mjs';
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
const DIR=process.env.HCC_SYNC_TEST_DIR||'tests/.generated';

const till={tillCode:'FD-1',shifts:1,status:'Open',openingPaise:500000,cashInPaise:150000,cashOutPaise:0,expensesPaise:180000,restaurantPaise:0,expectedPaise:470000,
  countedPaise:null,differencePaise:0,countedBy:null,approvedBy:null,reason:null};
const dayRow=(date,n)=>({date,arrivals:2*n,departures:n,sellable:10,occupied:6+n,netRevenuePaise:1000000*n,taxPaise:50000*n,netReceiptsPaise:400000*n,cashExpensesPaise:10000*n});
/** A server report for from..to: day n has n x the base figures, so range sums are checkable. */
const report=(from,to)=>{
  const days=[];for(let d=new Date(from+'T00:00:00Z'),i=1;d<=new Date(to+'T00:00:00Z');d.setUTCDate(d.getUTCDate()+1),i++)days.push(dayRow(d.toISOString().slice(0,10),i));
  const sum=k=>days.reduce((a,d)=>a+d[k],0),sell=sum('sellable'),occ=sum('occupied'),net=sum('netRevenuePaise'),room=Math.round(net*0.9);
  return {from,to,occupancy:{sellableRoomNights:sell,occupiedRoomNights:occ,occupancyPct:(Math.round(occ/sell*1000)/10).toFixed(1)+'%',roomRevenuePaise:room,adrPaise:Math.round(room/occ),revParPaise:Math.round(room/sell)},
    revenue:{roomPaise:room,servicePaise:net-room,discountsPaise:0,correctionsPaise:0,netPaise:net,taxPaise:sum('taxPaise'),cgstPaise:sum('taxPaise')/2,sgstPaise:sum('taxPaise')/2,grossPaise:net+sum('taxPaise')},
    gst:[],receipts:{byTender:[{tender:'Cash',paymentsPaise:sum('netReceiptsPaise'),refundsPaise:0,netPaise:sum('netReceiptsPaise')}],netPaise:sum('netReceiptsPaise'),depositsInPaise:0,depositsOutPaise:0,companyCreditPaise:0},
    expenses:{cashPaise:sum('cashExpensesPaise'),reversalsPaise:0,totalPaise:sum('cashExpensesPaise'),note:''},
    drawer:{tills:[till],openingPaise:500000,cashInPaise:150000,cashOutPaise:0,expectedPaise:470000,countedPaise:null,differencePaise:0},days};
};
const view=(from,to)=>({report:report(from,to),dayStatus:report(from,to).days.map(d=>({date:d.date,status:d.date==='2026-10-24'?'Open':'Closed',matchesSnapshot:null})),businessDate:'2026-10-24',note:''});

export async function register({test,assert}){
  const S=await loadSync();
  const srv=fakeServer({'GET /reports/daily':({query})=>query.from>query.to?reply(400,{code:'VALIDATION',message:"'to' must be on or after 'from'."}):view(query.from,query.to||query.from),
    'GET /closing/business-date':()=>({businessDate:'2026-10-24',closed:false}),'GET /closing/records':()=>({items:[]})});
  const rt=await startRuntime(S,srv);srv.reset();
  const at=new Date('2026-10-24T05:00:00.000Z');
  const R=await S.loadServerReport(rt.api,'2026-10-22','2026-10-24',()=>at);
  const V=view('2026-10-22','2026-10-24').report;
  const met=(t,id)=>R.pack.tabs.find(x=>x.id===t).metrics.find(m=>m.id===id).value;
  test('[G3 reports] The Reports pack is the server report: one GET /reports/daily for the range, figures equal the server\'s, no invented source rows',()=>{
    assert.deepEqual(srv.gets('/reports/daily').map(c=>c.query),[{from:'2026-10-22',to:'2026-10-24'}]);
    assert.deepEqual([met('revenue','net'),met('revenue','tax'),met('revenue','gross'),met('receipts','net'),met('expenses','total'),met('occupancy','occupied'),met('occupancy','sellable'),met('daily','arrivals')],
      [V.revenue.netPaise,V.revenue.taxPaise,V.revenue.grossPaise,V.receipts.netPaise,V.expenses.totalPaise,V.occupancy.occupiedRoomNights,V.occupancy.sellableRoomNights,12]);
    assert.deepEqual(R.pack.tabs.find(t=>t.id==='occupancy').ratios[0].display,V.occupancy.occupancyPct);
    assert.equal(R.pack.tabs.find(t=>t.id==='daily').table.rows.length,3);
    assert.ok(R.pack.tabs.every(t=>t.metrics.every(m=>m.rows.length===0)),'drill-down rows are not available from the server report');
    assert.equal(R.asOfUtc,'2026-10-24T05:00:00.000Z');
  });

  const H=await S.loadServerHistory(rt.api,'2026-10-24',()=>at);
  test('[G3 reports] History over months: one server report per month of the financial year to date; month figures are the server\'s, the total is their sum',()=>{
    assert.deepEqual(H.months.map(m=>m.month),['2026-04','2026-05','2026-06','2026-07','2026-08','2026-09','2026-10']);
    const q=srv.gets('/reports/daily').slice(1).map(c=>[c.query.from,c.query.to]);
    assert.deepEqual([q[0],q[6]],[['2026-04-01','2026-04-30'],['2026-10-01','2026-10-24']]);
    const oct=view('2026-10-01','2026-10-24').report.occupancy;
    assert.deepEqual([H.months[6].occupied,H.months[6].occupancy,H.months[6].roomRevenue],[oct.occupiedRoomNights,oct.occupancyPct,oct.roomRevenuePaise]);
    assert.equal(H.total.occupied,H.months.reduce((a,m)=>a+m.occupied,0));
    assert.equal(H.total.roomRevenue,H.months.reduce((a,m)=>a+m.roomRevenue,0));
    assert.equal(H.from,'2026-04-01');
    const m2=S.fyMonths('2027-02-10');
    assert.deepEqual([m2[0].month,m2.at(-1).month,m2.at(-1).to],['2026-04','2027-02','2027-02-10']);
  });

  const d1=view('2026-10-24','2026-10-24').report;
  const T=S.todayFigures(await S.loadServerReport(rt.api,'2026-10-24','2026-10-24',()=>at));
  test('[G3 reports] Overview / Home tiles = the server\'s figures for the business date',()=>{
    assert.deepEqual([T.occupied,T.sellable,T.occupancy,T.collections,T.expenses,T.drawer],[d1.occupancy.occupiedRoomNights,d1.occupancy.sellableRoomNights,Math.round(d1.occupancy.occupiedRoomNights/d1.occupancy.sellableRoomNights*100),d1.receipts.netPaise,d1.expenses.totalPaise,470000]);
  });
  const rec=S.closingFromDailyView(view('2026-10-24','2026-10-24'),'2026-10-24');
  test('[G3 reports] Day pack record from the server report: revenue, receipts by tender, cash and occupancy equal the report',()=>{
    assert.deepEqual([rec.revenue.net,rec.revenue.tax,rec.receipts.net,rec.receipts.byTender.Cash,rec.cash.expected,rec.expenses.total,rec.occupancy.occupancy],
      [d1.revenue.netPaise,d1.revenue.taxPaise,d1.receipts.netPaise,d1.receipts.byTender[0].netPaise,470000,d1.expenses.totalPaise,d1.occupancy.occupancyPct]);
  });

  // Server unreachable and a refusal surface as errors (screens keep the last figures with their "as of" time).
  srv.down=true;const down=await S.loadServerReport(rt.api,'2026-10-24','2026-10-24').then(()=>'ok',e=>String(e.code??e.message));srv.down=false;
  const bad=await S.loadServerReport(rt.api,'2026-10-24','2026-10-23').then(()=>'ok',e=>String(e.message));
  test('[G3 reports] A down server and a refused range reject (never a client-side guess)',()=>{assert.notEqual(down,'ok');assert.ok(bad.includes('on or after'));});

  // Pages render (desktop + phone) on the production state; Reports and the day pack show a loading state until the server answers.
  const P=await loadPages();const shown=[P.renderPages(rt.get().state,'1',false),P.renderPages(rt.get().state,'1',true)];
  await mkdir(DIR,{recursive:true});
  await writeFile(DIR+'/g3-entry.tsx',`import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';import {ServerDayPackSheet} from '../../src/features/exports/ServerDayPack';
import {Reports} from '../../src/features/reports/Reports';import {MaReports} from '../../src/mobile/admin/Reports';
export const dayPack=(ctx:any)=>renderToStaticMarkup(<HccContext.Provider value={ctx}><ServerDayPackSheet date="2026-10-24" onClose={()=>{}}/></HccContext.Provider>);
export const reports=(ctx:any,phone:boolean)=>renderToStaticMarkup(<HccContext.Provider value={ctx}>{phone?<MaReports/>:<Reports/>}</HccContext.Provider>);`);
  await build({entryPoints:[DIR+'/g3-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:DIR+'/g3.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':'"production"'},banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
  const G=await import(new URL('../'+DIR+'/g3.mjs',import.meta.url).href);
  const s=rt.get().state,noop=()=>{};
  const ctx={s,uid:'1',user:s.users.find(u=>u.id==='1')??s.users[0],m:{},go:noop,open:noop,closeModal:noop,booking:noop,toast:noop,permission:()=>true,display:{isMobile:false}};
  const html=[G.dayPack(ctx),G.reports(ctx,false),G.reports({...ctx,display:{isMobile:true}},true)];
  test('[G3 reports] Every desktop and phone page renders; Reports (desktop + phone) and the day pack show a loading state until the server answers',()=>{
    assert.deepEqual(shown.map(x=>x.errors),[{},{}]);
    assert.ok(html[0].includes('Loading the day pack from the server'));
    assert.ok(html[1].includes('Loading the report'));
    assert.ok(html[2].includes('Loading the report'));
  });
}
