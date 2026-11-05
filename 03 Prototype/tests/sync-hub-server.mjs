// AF2-HUB-SERVER (audit "Missed A"): the production Report library runs on the server — catalogue, run, files and export log mappers
// (clients/sync/reports-hub-run.ts), the loaders over a fake server, and a production-build render of the hub screens. Names prefixed [AF2-HUB].
import {build} from 'esbuild';
import {loadSync,fakeServer,startRuntime,reply} from './_sync-harness.mjs';

const SRV_DEF={id:'rpt.money.shift-report',title:'Shift report',area:'money',priority:'P1',status:'built',moneyFlag:true,moneyGate:'PERM-RPT-002',viewPerms:['PERM-RPT-002'],deskPerms:[],
  exportPerm:'PERM-RPT-003',dateClass:2,params:[{key:'range',label:'Dates',type:'range',dateClass:2,options:null,default:null,required:false},
    {key:'tender',label:'Tender',type:'enum',dateClass:null,options:['all','cash'],default:'all',required:false}],
  columns:[{key:'date',label:'Date',type:3,money:false,mask:0,computed:null},{key:'guest',label:'Guest',type:0,money:false,mask:1,computed:null},
    {key:'amount',label:'Amount',type:2,money:true,mask:0,computed:null},{key:'occ',label:'Occupancy',type:4,money:false,mask:0,computed:{expr:'pct(a,b)',deps:['amount']}}],
  dateBasis:'Business date',formats:[0,1,2,3,6],phone:1,documentLayout:false,legacyIds:['A12'],ownShift:true};
const SRV_RESULT={id:'rpt.money.shift-report',title:'Shift report',params:{values:{from:'2026-10-20',to:'2026-10-24',tender:'all'},from:'2026-10-20',to:'2026-10-24',legacy:false},
  columns:SRV_DEF.columns,rows:[{kind:0,cells:{date:'2026-10-24',guest:'J••• T•••',amount:123456,occ:49.4},group:'cash',drill:null},{kind:2,cells:{date:null,guest:'Total',amount:123456,occ:null},group:null,drill:null}],
  about:{runAt:'2026-10-24T04:30:00+00:00',user:'Hub Owner',dateBasis:'Business date',provisional:true,legacy:false,from:'2026-10-20',to:'2026-10-24'},removed:null,pageIndex:0,pageSize:0,totalRows:2,sections:null};
const LOG=[{id:12,at:'2026-10-24T04:31:00+00:00',principalId:4,reportId:'rpt.money.shift-report',paramsJson:'{"from":"2026-10-24"}',format:6,rows:2,fileName:null,provisional:true},
  {id:11,at:'2026-10-24T04:30:00+00:00',principalId:1,reportId:'rpt.money.shift-report',paramsJson:'{}',format:1,rows:2,fileName:'DEMO-rpt.money.shift-report.xlsx',provisional:false}];
const XLSX_B64=Buffer.from('PK\u0003\u0004demo').toString('base64');

export async function register({test,assert}){
  const S=await loadSync();
  const srv=fakeServer({'GET /reports/catalogue':()=>[{definition:SRV_DEF,canExport:true,money:true},{definition:{...SRV_DEF,id:'rpt.rooms.room-status',title:'Room status',area:'rooms',moneyFlag:false,moneyGate:null,formats:['Screen','Xlsx']},canExport:false,money:false}],
    'GET /reports/export-log':()=>LOG,
    'GET /reports/:id':({params})=>params.id==='rpt.tax.gst-summary'?reply(403,{code:'FORBIDDEN',message:'You cannot open GST summary.'}):SRV_RESULT,
    'GET /reports/:id/export':()=>reply(200,XLSX_B64,{'Content-Disposition':'attachment; filename=DEMO-rpt.money.shift-report-2026-10-20-2026-10-24.xlsx'})});
  const rt=await startRuntime(S,srv,{areas:[S.core]});
  const api=rt.api;

  test('[AF2-HUB] catalogue entry → hub definition: numeric enums mapped (date class, column types, masks, formats without print, phone), rights kept',()=>{
    const d=S.hubDefFromServer({definition:SRV_DEF,canExport:true,money:true});
    assert.equal(d.dateClass,'D-B');assert.equal(d.params[0].dateClass,'D-B');assert.deepEqual(d.params[1].options,['all','cash']);assert.equal(d.params[1].default,'all');
    assert.deepEqual(d.columns.map(c=>c.type),['date','text','money','pct']);assert.equal(d.columns[1].mask,'guestName');assert.equal(d.columns[0].mask,undefined);
    assert.deepEqual(d.columns[3].computed,{expr:'pct(a,b)',deps:['amount']});
    assert.deepEqual(d.formats,['screen','xlsx','pdf','csv']);assert.equal(d.phone,'summary');assert.equal(d.ownShift,true);assert.deepEqual(d.legacyIds,['A12']);
    assert.equal(d.canExport,true);assert.equal(d.seesMoney,true);assert.equal(d.onServer,true);
    assert.throws(()=>d.select({},'1',{}),/hotel server/,'a server definition never runs on the device');
    const n=S.hubDefFromServer({definition:{...SRV_DEF,dateClass:'AsOf',phone:'No',formats:['Screen','Csv'],columns:[{key:'p',label:'P',type:'Percent',money:false,mask:'BankAccount'}]},canExport:false,money:false});
    assert.equal(n.dateClass,'D-ASOF');assert.equal(n.phone,'no');assert.deepEqual(n.formats,['screen','csv']);assert.equal(n.columns[0].type,'pct');assert.equal(n.columns[0].mask,'bankAccount');
  });
  test('[AF2-HUB] server result → RptResult: cells as sent (paise, percent, masked names), row kinds, bound params, about in hotel time',()=>{
    const r=S.hubResultFromServer(SRV_RESULT);
    assert.deepEqual(r.params,{from:'2026-10-20',to:'2026-10-24',tender:'all'});
    assert.deepEqual(r.rows.map(x=>x.kind),['row','total']);assert.equal(r.rows[0].cells.amount,123456);assert.equal(r.rows[0].cells.occ,49.4);assert.equal(r.rows[0].cells.guest,'J••• T•••');
    assert.equal(r.rows[0].group,'cash');assert.equal(r.rows[1].group,undefined);
    assert.equal(r.about.runAt,'2026-10-24 10:00');assert.equal(r.about.provisional,true);assert.equal(r.page,undefined,'all rows: no page');
    const sec=S.hubResultFromServer({...SRV_RESULT,removed:['amount'],sections:[{key:'a',title:'Revenue',columns:SRV_DEF.columns.slice(0,1),rows:[{kind:1,cells:{date:null}}]}]});
    assert.deepEqual(sec.removed,['amount']);assert.equal(sec.sections[0].title,'Revenue');assert.equal(sec.sections[0].rows[0].kind,'subtotal');
  });
  test('[AF2-HUB] export log rows → RptExportLogEntry (ids, user, format names, params, hotel time)',()=>{
    const l=S.hubExportLogFromServer(LOG);
    assert.deepEqual(l.map(e=>e.format),['print','xlsx']);assert.deepEqual(l.map(e=>e.by),['4','1']);assert.deepEqual(l[0].params,{from:'2026-10-24'});
    assert.equal(l[0].at,'2026-10-24T10:01');assert.equal(l[1].fileName,'DEMO-rpt.money.shift-report.xlsx');assert.equal(l[0].fileName,undefined);
  });
  test('[AF2-HUB] prototype-only reports are added as "Not on the server yet" (never runnable), server ids win',()=>{
    const server=[S.hubDefFromServer({definition:SRV_DEF,canExport:true,money:true})];
    const proto=[{...server[0],serverMirror:true,onServer:undefined},{id:'rpt.rooms.occupancy',title:'Occupancy',serverMirror:false,select:()=>({})},{id:'rpt.money.shift-report',serverMirror:false,select:()=>({})}];
    const m=S.mergeHubDefs(server,proto);
    assert.deepEqual(m.map(d=>[d.id,d.onServer]),[['rpt.money.shift-report',true],['rpt.rooms.occupancy',false]]);
    assert.equal(m[1].canExport,false);assert.throws(()=>m[1].select(),/hotel server/);
    assert.equal(S.NOT_ON_SERVER,'Not on the server yet');
  });
  test('[AF2-HUB] loaders: GET /reports/catalogue; GET /reports/{id} with every parameter and all=true (blank values left out); a 403 reaches the screen',async()=>{
    srv.reset();
    const defs=await S.loadHubCatalogue(api);
    assert.deepEqual(defs.map(d=>[d.id,d.canExport]),[['rpt.money.shift-report',true],['rpt.rooms.room-status',false]]);
    const r=await S.runHubReport(api,'rpt.money.shift-report',{from:'2026-10-20',to:'2026-10-24',tender:'all',note:''});
    const g=srv.gets('/reports/rpt.money.shift-report')[0];
    assert.deepEqual(g.query,{from:'2026-10-20',to:'2026-10-24',tender:'all',all:'true'});assert.equal(r.rows.length,2);
    await assert.rejects(()=>S.runHubReport(api,'rpt.tax.gst-summary',{}),e=>e.code==='FORBIDDEN'||/cannot open/.test(e.message));
    assert.equal(srv.calls.filter(c=>c.method!=='GET').length,0,'a run writes nothing (no print-log, no command)');
  });
  test('[AF2-HUB] files come from the server: GET /reports/{id}/export?format=…&encoding=base64, bytes decoded, server file name kept, no RPT_LOG post',async()=>{
    srv.reset();
    const f=await S.exportHubFile(api,'rpt.money.shift-report','xlsx',{from:'2026-10-20',to:'2026-10-24'},'fallback.xlsx');
    const g=srv.gets('/reports/rpt.money.shift-report/export')[0];
    assert.deepEqual(g.query,{from:'2026-10-20',to:'2026-10-24',format:'xlsx',encoding:'base64'});
    assert.equal(f.fileName,'DEMO-rpt.money.shift-report-2026-10-20-2026-10-24.xlsx');assert.equal(f.mime,'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet');
    assert.deepEqual([...f.bytes.slice(0,2)],[80,75]);
    assert.equal(srv.calls.filter(c=>c.method==='POST').length,0,'the server writes the export-log row itself');
    assert.deepEqual([...S.base64Bytes(' UEsD\nBA== ')],[80,75,3,4]);
    const log=await S.loadHubExportLog(api);assert.equal(log.length,2);
  });

  // Production-build render of the hub screens (react-dom/server; effects do not run, so nothing is fetched).
  await build({entryPoints:['tests/hub-server-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/hub-server.mjs',jsx:'automatic',
    external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"production"','process.env.NODE_ENV':'"production"'}});
  const H=await import('./.generated/hub-server.mjs');
  const {renderToStaticMarkup}=await import('react-dom/server');
  const React=(await import('react')).default;const h=React.createElement;
  const s=H.seed();const uid=s.users.find(u=>u.role==='Owner').id;
  const ctx=mobile=>({s,uid,user:s.users.find(u=>u.id===uid),page:'report-hub',go(){},open(){},toast(){},run:async()=>true,permission:p=>H.can(s,uid,p),
    display:{isMobile:mobile,framed:false,mode:'auto',setMode(){}},prefs:{prefs:{textScale:1},save(){},reset(){}}});
  const nav={desktop:true,page:'report-hub',tabKey:'',seq:1,setTabCrumb(){}};
  const render=(el,mobile=false)=>renderToStaticMarkup(h(H.HccContext.Provider,{value:ctx(mobile)},h(H.FitNavContext.Provider,{value:nav},el)));
  const serverDef=(over={})=>({...S.hubDefFromServer({definition:SRV_DEF,canExport:true,money:true}),...over});

  test('[AF2-HUB] production build: the hub, its phone version and the export log tab render (server data not fetched yet = empty, no throw)',()=>{
    const desk=render(h(H.ReportsHub));assert.ok(desk.includes('Reports'));
    assert.ok(!desk.includes('Collection detail'),'no report is listed from the device engine in production');
    render(h(H.ReportsHubMobile),true);
    assert.ok(render(h(H.ExportLogTab,{defs:[]})).includes('Nothing exported yet'));
  });
  test('[AF2-HUB] production build: a server report shows its parameters and the server export buttons only with the server export right; it is not run on the device',()=>{
    const on=render(h(H.ReportView,{def:serverDef()}));
    assert.ok(on.includes('Export Excel')&&on.includes('Export CSV')&&on.includes('Print'));assert.ok(on.includes('From')&&on.includes('Tender'));
    assert.ok(!on.includes('No rows'),'no device-side result');
    const off=render(h(H.ReportView,{def:serverDef({canExport:false})}));
    assert.ok(!off.includes('Export Excel')&&!off.includes('Export CSV')&&off.includes('Print'));
  });
  test('[AF2-HUB] production build: a prototype-only report is labelled "Not on the server yet" with no parameters, export or favourite button',()=>{
    const html=render(h(H.ReportView,{def:{...serverDef(),onServer:false,canExport:false}}));
    assert.ok(html.includes('Not on the server yet'));assert.ok(!html.includes('Export Excel')&&!html.includes('Print')&&!html.includes('Add to favourites'));
    assert.ok(!html.includes('From'),'no parameter panel');
  });
}
