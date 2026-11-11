// FX-VIEWER: paging and the PDF button of the report viewer in production mode. Names prefixed [FX-VIEWER].
import {build} from 'esbuild';
import {readFileSync} from 'node:fs';
import {loadSync,fakeServer,startRuntime,reply} from './_sync-harness.mjs';

const bundle=async(mode)=>{const out='tests/.generated/fx-viewer-'+mode+'.mjs';
  await build({entryPoints:['tests/fx-viewer-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:out,jsx:'automatic',external:['react','react-dom','lucide-react'],logLevel:'error',
    define:{'process.env.HCC_BUILD':JSON.stringify(mode),'process.env.NODE_ENV':'"production"'}});
  return import('./.generated/fx-viewer-'+mode+'.mjs?'+Date.now());};

const cols=[{key:'name',label:'Name',type:'text'},{key:'amt',label:'Amount',type:'money',money:true}];
const mkRows=n=>[...Array.from({length:n},(_,i)=>({kind:'row',cells:{name:'Row '+(i+1),amt:100*(i+1)}})),{kind:'total',cells:{name:'GRAND TOTAL',amt:100*n*(n+1)/2}}];
const result=n=>({id:'rpt.x',title:'X',params:{},columns:cols,rows:mkRows(n),about:{runAt:'',user:'u',dateBasis:'b',provisional:false,legacy:false}});
const def=(o={})=>({id:'rpt.money.shift-report',title:'Shift report',area:'money',priority:'P1',status:'built',moneyFlag:false,viewPerms:[],params:[],columns:cols,dateBasis:'b',
  formats:['screen','xlsx','csv','pdf'],phone:'yes',canExport:true,onServer:true,select:()=>null,...o});
const count=(html,re)=>(html.match(re)||[]).length;

export async function register({test,assert}){
  const P=await bundle('production');const D=await bundle('prototype');
  const {renderToStaticMarkup}=await import('react-dom/server');
  const React=(await import('react')).default;const h=React.createElement;
  const s=P.seed();const uid=s.users.find(u=>u.role==='Owner').id;
  const ctx=mobile=>({s,uid,user:s.users.find(u=>u.id===uid),page:'report-hub',go(){},open(){},toast(){},run:async()=>true,permission:p=>P.can(s,uid,p),
    display:{isMobile:mobile,framed:false,mode:'auto',setMode(){}},prefs:{prefs:{textScale:1},save(){},reset(){}}});
  const nav={desktop:true,page:'report-hub',tabKey:'',seq:1,setTabCrumb(){}};
  const render=(M,el,mobile=false)=>renderToStaticMarkup(h(M.HccContext.Provider,{value:ctx(mobile)},h(M.FitNavContext.Provider,{value:nav},el)));

  test('[FX-VIEWER] splitTail keeps only the closing total / subtotal rows apart from the detail rows',()=>{
    const {body,tail}=P.splitTail(mkRows(3));assert.equal(body.length,3);assert.equal(tail.length,1);assert.equal(tail[0].cells.name,'GRAND TOTAL');
    assert.deepEqual(P.splitTail([{kind:'row',cells:{}},{kind:'subtotal',cells:{}},{kind:'row',cells:{}}]).tail,[],'a middle subtotal stays inside the paged rows');
    assert.equal(P.splitTail([]).body.length,0);
  });
  test('[FX-VIEWER] desktop: 250 server rows show 100 at a time with "Show more"; the grand total row is always visible and is the total of ALL rows',()=>{
    const html=render(P,h(P.ResultTable,{result:result(250)}));
    assert.equal(count(html,/Row \d+</g),100,'one page of 100 detail rows');
    assert.ok(html.includes('Showing 100 of 250 rows')&&html.includes('Show 100 more'));
    assert.ok(html.includes('GRAND TOTAL'),'total row not hidden behind Show more');
    assert.ok(html.includes('₹31,375<'),'the total is the whole-report figure (3,137,500 paise = sum of all 250 rows), not the page sum');
    assert.ok(!html.includes('Row 101<'));
  });
  test('[FX-VIEWER] a short report shows every row and its total with no Show more button',()=>{
    const html=render(P,h(P.ResultTable,{result:result(40)}));assert.equal(count(html,/Row \d+</g),40);assert.ok(html.includes('GRAND TOTAL')&&!html.includes('Show '));
  });
  test('[FX-VIEWER] phone: cards page 50 at a time, the total card stays',()=>{
    const html=render(P,h(P.ResultCards,{result:result(120)}),true);
    assert.equal(count(html,/Row \d+</g),50);assert.ok(html.includes('Showing 50 of 120 rows')&&html.includes('Show 50 more'));assert.ok(html.includes('GRAND TOTAL')&&html.includes('rpt-card total'));
  });
  test('[FX-VIEWER] production: Export PDF appears with the other server exports, only with the export right and when the report offers pdf',()=>{
    const on=render(P,h(P.ExportBar,{def:def(),result:result(3),raw:{}}));
    assert.ok(on.includes('Export PDF')&&on.includes('Export Excel')&&on.includes('Export CSV')&&on.includes('Print'));
    assert.ok(!render(P,h(P.ExportBar,{def:def({canExport:false}),result:result(3),raw:{}})).includes('Export PDF'),'no export right, no PDF');
    assert.ok(!render(P,h(P.ExportBar,{def:def({formats:['screen','xlsx','csv']}),result:result(3),raw:{}})).includes('Export PDF'),'report without pdf');
    assert.ok(render(P,h(P.ExportBar,{def:def(),result:null,raw:{}})).includes('disabled'),'disabled until a result is on screen');
  });
  test('[FX-VIEWER] the demo build has no PDF button (no client-side PDF writer) and its paging is unchanged',()=>{
    assert.ok(!render(D,h(D.ExportBar,{def:def(),result:result(3),raw:{}})).includes('Export PDF'));
    assert.equal(count(render(D,h(D.ResultTable,{result:result(250)})),/Row \d+</g),100);
  });
  test('[FX-VIEWER] the PDF click goes through the server file loader (format pdf) and the shared save hook',()=>{
    const src=readFileSync('src/features/reports/ReportView.tsx','utf8');
    assert.ok(/serverFile\('pdf','PDF file'\)/.test(src)&&/fetchHubFile\(def\.id,format,raw,fallback\)/.test(src)&&/saveBinaryFile\(f\.fileName,f\.bytes,f\.mime\)/.test(src));
  });

  // The loader the button calls, over a fake server.
  const S=await loadSync();
  const PDF_B64=Buffer.from('%PDF-1.4 demo').toString('base64');
  const srv=fakeServer({'GET /reports/:id/export':()=>reply(200,PDF_B64,{'Content-Disposition':'attachment; filename=HCC-shift.pdf'})});
  const rt=await startRuntime(S,srv,{areas:[S.core]});
  test('[FX-VIEWER] exportHubFile pdf: GET /reports/{id}/export?format=pdf&encoding=base64, a PDF back, mime application/pdf, server file name, no POST (the server writes the export log)',async()=>{
    srv.reset();const f=await S.exportHubFile(rt.api,'rpt.money.shift-report','pdf',{from:'2026-10-20'},'fallback.pdf');
    const g=srv.gets('/reports/rpt.money.shift-report/export')[0];assert.deepEqual(g.query,{from:'2026-10-20',format:'pdf',encoding:'base64'});
    assert.equal(f.mime,'application/pdf');assert.equal(f.fileName,'HCC-shift.pdf');assert.equal(Buffer.from(f.bytes).toString('latin1').slice(0,5),'%PDF-');
    assert.equal(srv.calls.filter(c=>c.method==='POST').length,0);
  });
}
