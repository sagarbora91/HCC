// W05-E: .xlsx writer (stored ZIP + OOXML), Reports workbook, binary save path, Tally XML preview.
import {build} from 'esbuild';
import {inflateRawSync} from 'node:zlib';
import {spawnSync} from 'node:child_process';
import {writeFile} from 'node:fs/promises';
await build({entryPoints:['src/app/xlsx.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/xlsx.mjs',logLevel:'error'});
await build({entryPoints:['src/features/reports/exportXlsx.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/exportXlsx.mjs',logLevel:'error'});
await build({entryPoints:['src/app/native.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/native-x.mjs',logLevel:'error',external:['react']});
await build({entryPoints:['src/features/cashbook/exportXlsx.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/cb-xlsx.mjs',logLevel:'error'});
// Render harness for the cash book page (W05-7 difference line, W05-8 Excel button).
await writeFile('tests/.generated/cashbook-ui-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,cbDay} from '../../src/services/mock';
import {CashBookView,DayCard} from '../../src/features/cashbook/CashBook';
const ctx=(s:any,uid:string,mobile:boolean)=>({s,uid,user:s.users.find((u:any)=>u.id===uid),busy:false,run:async()=>true,open(){},closeModal(){},toast(){},go(){},setDirty(){},permission:(p:any)=>can(s,uid,p),display:{isMobile:mobile}} as any);
const wrap=(s:any,uid:string,mobile:boolean,el:any)=>renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid,mobile)}>{el}</HccContext.Provider>);
export const renderRange=(s:any,phone:boolean)=>wrap(s,'owner',phone,<CashBookView phone={phone}/>);
export const renderDay=(s:any,d:string,phone:boolean)=>wrap(s,'owner',phone,<DayCard d={cbDay(s,d)} phone={phone} onBack={()=>{}}/>);
`);
await build({entryPoints:['tests/.generated/cashbook-ui-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/cashbook-ui.mjs',logLevel:'error',jsx:'automatic',define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __cbReq} from 'node:module';const require=__cbReq(import.meta.url);"}});
const CBX=await import('./.generated/cb-xlsx.mjs'),CBUI=await import('./.generated/cashbook-ui.mjs');
const X=await import('./.generated/xlsx.mjs'),RX=await import('./.generated/exportXlsx.mjs'),N=await import('./.generated/native-x.mjs');
const dec=new TextDecoder();
/** Reads a ZIP through its central directory (what Excel does) and checks every CRC. */
function unzip(b){
  const dv=new DataView(b.buffer,b.byteOffset,b.byteLength);let e=b.length-22;
  if(dv.getUint32(e,true)!==0x06054b50)throw new Error('no end-of-central-directory');
  const n=dv.getUint16(e+10,true);let p=dv.getUint32(e+16,true);const out={};
  for(let i=0;i<n;i++){
    if(dv.getUint32(p,true)!==0x02014b50)throw new Error('bad central header');
    const method=dv.getUint16(p+10,true),crc=dv.getUint32(p+16,true),size=dv.getUint32(p+24,true),nl=dv.getUint16(p+28,true),el=dv.getUint16(p+30,true),cl=dv.getUint16(p+32,true),off=dv.getUint32(p+42,true);
    const name=dec.decode(b.subarray(p+46,p+46+nl));
    if(dv.getUint32(off,true)!==0x04034b50)throw new Error('bad local header');
    const ds=off+30+dv.getUint16(off+26,true)+dv.getUint16(off+28,true);
    let data=b.subarray(ds,ds+size);if(method!==0)data=inflateRawSync(data);
    if(X.crc32(data)!==crc)throw new Error('crc mismatch '+name);
    out[name]=dec.decode(data);p+=46+nl+el+cl;
  }
  return out;
}
const R={};
{const calls=[];const plugin=name=>new Proxy({},{get:(_,m)=>m==='then'?undefined:async a=>{calls.push([name,m,a]);if(m==='getUri')return {uri:'file:///d/'+a.path};return {};}});
  globalThis.Capacitor={isNativePlatform:()=>true,registerPlugin:plugin};N.resetNativeCache();
  R.bytes=X.buildXlsx([{name:'S',rows:[['a'],[{paise:100}]]}]);R.r=await N.saveBinaryFile('HCC-x.xlsx',R.bytes,X.XLSX_MIME);R.calls=calls;
  delete globalThis.Capacitor;N.resetNativeCache();}
// W05 review F6: a large workbook is written in base64 chunks (writeFile + appendFile); F11: a failing native save is reported as failed.
{const calls=[];const plugin=name=>new Proxy({},{get:(_,m)=>m==='then'?undefined:async a=>{calls.push([name,m,a]);if(m==='getUri')return {uri:'file:///d/'+a.path};return {};}});
  globalThis.Capacitor={isNativePlatform:()=>true,registerPlugin:plugin};N.resetNativeCache();
  R.big=new Uint8Array(N.NATIVE_CHUNK*2+5);for(let i=0;i<R.big.length;i++)R.big[i]=(i*31)&255;R.bigR=await N.saveBinaryFile('HCC-big.xlsx',R.big,X.XLSX_MIME);R.bigCalls=calls;
  delete globalThis.Capacitor;N.resetNativeCache();}
{const toasts=[];const plugin=name=>new Proxy({},{get:(_,m)=>m==='then'?undefined:async()=>{if(name==='Filesystem'&&m==='writeFile')throw new Error('disk full (simulated)');return {uri:'x'};}});
  globalThis.Capacitor={isNativePlatform:()=>true,registerPlugin:plugin};N.resetNativeCache();
  R.failR=await N.saveAndReport(N.saveBinaryFile('HCC-fail.xlsx',new Uint8Array([1,2,3]),X.XLSX_MIME),(t,e)=>toasts.push([t,e]),'Demo Excel workbook saved','the Excel workbook');
  R.failText=await N.saveAndReport(N.saveTextFile('HCC-fail.xml','<x/>','application/xml'),(t,e)=>toasts.push([t,e]),'Simulated Tally XML saved','the Tally XML');
  R.okR=await N.saveAndReport(Promise.resolve('downloaded'),(t,e)=>toasts.push([t,e]),'Demo Excel workbook saved','the Excel workbook');
  R.toasts=toasts;delete globalThis.Capacitor;N.resetNativeCache();}
const script=`const M=await import('./tests/.generated/mock.mjs'),RX=await import('./tests/.generated/exportXlsx.mjs');const s=M.loadScenario('FY26');
      const b=RX.reportXlsx({hotel:'x',scenario:'FY26',clock:s.clock},M.reportPack(s,'2026-04-01','2026-10-23'));const dv=new DataView(b.buffer,b.byteOffset,b.byteLength);
      console.log(JSON.stringify({len:b.length,eocd:dv.getUint32(b.length-22,true)===0x06054b50,files:dv.getUint16(b.length-12,true)}));`;
const R6=spawnSync(process.execPath,['--max-old-space-size=160','--input-type=module','-e',script],{encoding:'utf8',maxBuffer:1<<20});
export function register({test,M,assert}){
  const wb=()=>X.buildXlsx([{name:'Summary',rows:[['Item','Amount','Count','Day','Note'],['Room revenue',{paise:123456},{int:7},{date:'2026-10-24'},'A & B <ok>'],['Plain',42,1.5,null,'']]},{name:'Summary',rows:[['x']]}]);
  test('[XLSX] stored ZIP has the OOXML parts, valid CRCs and workbook XML',()=>{
    const z=unzip(wb());
    for(const k of ['[Content_Types].xml','_rels/.rels','xl/workbook.xml','xl/_rels/workbook.xml.rels','xl/styles.xml','xl/worksheets/sheet1.xml','xl/worksheets/sheet2.xml'])assert.ok(z[k],k);
    assert.match(z['xl/workbook.xml'],/<sheet name="Summary" sheetId="1"/);assert.match(z['xl/workbook.xml'],/<sheet name="Summary 2" sheetId="2"/);
    for(const k in z)assert.match(z[k],/^<\?xml/);
  });
  test('[XLSX] numbers are numbers (no text), money is ₹ rupees, dates are serials, text escaped',()=>{
    const s=unzip(wb())['xl/worksheets/sheet1.xml'];
    assert.match(s,/<c r="B2" s="2"><v>1234.56<\/v><\/c>/);assert.match(s,/<c r="C2" s="4"><v>7<\/v><\/c>/);
    assert.match(s,/<c r="D2" s="3"><v>46319<\/v><\/c>/);assert.match(s,/<c r="B3" s="4"><v>42<\/v><\/c>/);assert.match(s,/<c r="C3" s="0"><v>1.5<\/v><\/c>/);
    assert.ok(!/t="inlineStr"[^>]*><is><t[^>]*>1234/.test(s));assert.match(s,/A &amp; B &lt;ok&gt;/);assert.match(s,/<c r="A1" s="1" t="inlineStr">/);
    assert.match(unzip(wb())['xl/styles.xml'],/₹/);
  });
  test('[XLSX] sheet names are made Excel-safe and unique; column letters',()=>{
    assert.equal(X.sheetName('a/b:c?'+'x'.repeat(40),[]).length,31);assert.equal(X.colRef(0),'A');assert.equal(X.colRef(26),'AA');assert.equal(X.sheetName('A',['a']),'A 2');
  });
  test('[XLSX] Reports workbook: every tab, money cells equal the report figures',()=>{
    const s=M.seed(),pack=M.reportPack(s,s.date,s.date);
    const z=unzip(RX.reportXlsx({hotel:s.settings.hotel,scenario:s.scenario,clock:s.clock},pack));
    const wbx=z['xl/workbook.xml'];for(const t of pack.tabs)assert.ok(wbx.includes('name="'+t.label.replace(/&/g,'&amp;')+'"'),t.label);
    assert.match(wbx,/name="Source rows"/);
    const rev=pack.tabs.find(t=>t.id==='revenue'),net=rev.metrics.find(m=>m.id==='net').value;
    const idx=pack.tabs.findIndex(t=>t.id==='revenue')+2; // About is sheet 1
    assert.ok(z['xl/worksheets/sheet'+idx+'.xml'].includes('<v>'+net/100+'</v>'));
  });
  test('[XLSX] saveBinaryFile: native path writes base64 to the app cache exports/ then shares (A10-01)',()=>{
    const w=R.calls.find(c=>c[1]==='writeFile')[2];assert.equal(R.r,'shared');assert.equal(w.path,'exports/HCC-x.xlsx');assert.equal(w.directory,'CACHE');
    assert.deepEqual(new Uint8Array(Buffer.from(w.data,'base64')),R.bytes);assert.ok(R.calls.some(c=>c[0]==='Share'));
  });

  test('[TALLY] preview XML: envelope, vouchers from existing accounting vouchers, debit negative / credit positive, balanced, Simulated',()=>{
    const s=M.seed(),vs=M.accVouchers(s,s.date),p=M.accTallyXml(s,s.date,s.date);
    assert.equal(p.simulated,true);assert.equal(p.count,vs.length);assert.ok(p.count>0);assert.equal(p.debit,p.credit);
    assert.match(p.xml,/SIMULATED PREVIEW/);assert.match(p.xml,/<TALLYREQUEST>Import Data<\/TALLYREQUEST>/);assert.match(p.xml,/<REPORTNAME>Vouchers<\/REPORTNAME>/);
    assert.equal((p.xml.match(/<VOUCHER /g)||[]).length,p.count);assert.ok(p.xml.includes('<VOUCHERNUMBER>'+vs[0].no+'</VOUCHERNUMBER>'));
    const dr=[...p.xml.matchAll(/<ISDEEMEDPOSITIVE>Yes<\/ISDEEMEDPOSITIVE><AMOUNT>-([\d.]+)</g)].reduce((a,m)=>a+Math.round(parseFloat(m[1])*100),0);
    const cr=[...p.xml.matchAll(/<ISDEEMEDPOSITIVE>No<\/ISDEEMEDPOSITIVE><AMOUNT>([\d.]+)</g)].reduce((a,m)=>a+Math.round(parseFloat(m[1])*100),0);
    assert.equal(dr,p.debit);assert.equal(cr,p.credit);assert.match(p.xml,/<DATE>\d{8}<\/DATE>/);
  });
  test('[TALLY] XML escapes narration; bad or over-long ranges are refused',()=>{
    const s=M.seed();assert.ok(M.accTallyXml(s,'2026-10-25','2026-10-24').error);assert.ok(M.accTallyXml(s,'2026-01-01','2026-10-24').error);
    assert.ok(!M.accTallyXml(s,'2026-10-24','2026-10-24').xml.includes('& '));
  });
  test('[TALLY] F10: impossible calendar dates are refused (30 Feb, month 13, day 00)',()=>{
    const s=M.seed();for(const [a,b] of [['2026-02-30','2026-03-01'],['2026-10-01','2026-13-01'],['2026-10-00','2026-10-05'],['2026-09-31','2026-10-01']])assert.match(M.accTallyXml(s,a,b).error??'',/valid/,a+'..'+b);
    assert.equal(M.accTallyXml(s,'2026-10-20','2026-10-24').error,undefined);});
  test('[XLSX] F6: large native save is chunked (writeFile then appendFile) and reassembles byte-for-byte',()=>{
    const w=R.bigCalls.filter(c=>c[0]==='Filesystem'&&(c[1]==='writeFile'||c[1]==='appendFile'));assert.equal(R.bigR,'shared');
    assert.deepEqual(w.map(c=>c[1]),['writeFile','appendFile','appendFile']);assert.ok(w.every(c=>c[2].path==='exports/HCC-big.xlsx'&&c[2].directory==='CACHE'));
    assert.deepEqual(new Uint8Array(Buffer.concat(w.map(c=>Buffer.from(c[2].data,'base64')))),R.big);});
  test('[XLSX] F11: a failing save is reported as failed (error toast, never "saved"); success keeps the saved text',()=>{
    assert.equal(R.failR,'failed');assert.equal(R.failText,'failed');assert.equal(R.okR,'downloaded');
    const [f1,f2,ok]=R.toasts;assert.equal(f1[1],true);assert.equal(f2[1],true);assert.ok(!/\bsaved\b/i.test(f1[0]+f2[0]));assert.match(f1[0],/Could not save the Excel workbook/);
    assert.deepEqual(ok,['Demo Excel workbook saved',false]);});
    test('[XLSX] F6: full FY26 Reports workbook builds within a 160 MB heap (old writer needed ~450 MB) and is a valid ZIP',()=>{
      const r=R6;assert.equal(r.status,0,(r.stderr||'').split('\n').slice(0,3).join(' '));const o=JSON.parse(r.stdout.trim().split('\n').pop());
      assert.ok(o.len>10e6,'FY workbook is large');assert.equal(o.eocd,true);assert.ok(o.files>=10);});
  test('[XLSX] F8: NaN / Infinity never reach a cell; Excel-safe sheet names (apostrophes, History); floats ≤ 15 digits',()=>{
    const z=unzip(X.buildXlsx([{name:"'Quoted'",rows:[['h'],[NaN,{paise:NaN},Infinity,{int:NaN},0.1+0.2,{date:'bad'}]]},{name:'History',rows:[['x']]}]));
    const s1=z['xl/worksheets/sheet1.xml'];assert.ok(!/NaN|Infinity/.test(s1));assert.match(s1,/<v>0.3<\/v>/);assert.ok(!/0\.30000000000000004/.test(s1));
    assert.match(z['xl/workbook.xml'],/<sheet name="Quoted" sheetId="1"/);assert.match(z['xl/workbook.xml'],/<sheet name="History sheet" sheetId="2"/);
    assert.equal(X.sheetName("'a'"),'a');assert.equal(X.sheetName('x'.repeat(30)+"'"),'x'.repeat(30));});
  test('[XLSX] W05-8: cash book Excel = the cash book figures (money cells, over/short line, carried-forward closing)',()=>{
    const fy=M.loadScenario('FY26'),r=M.cbRange(fy,'2026-10-22','2026-10-23'),z=unzip(CBX.cbXlsx(fy,'2026-10-22','2026-10-23'));
    assert.match(z['xl/workbook.xml'],/name="About"/);assert.match(z['xl/workbook.xml'],/name="Days"/);assert.match(z['xl/workbook.xml'],/name="Entries"/);
    const about=z['xl/worksheets/sheet1.xml'],days=z['xl/worksheets/sheet2.xml'],ent=z['xl/worksheets/sheet3.xml'];
    assert.ok(about.includes('<v>'+r.closing/100+'</v>'));assert.ok(about.includes('<v>'+r.differences/100+'</v>'));assert.match(about,/Cash over\/short/);
    const d=r.days[0];assert.equal(d.difference,-3000);assert.ok(days.includes('s="2"><v>'+d.counted/100+'</v>'));assert.ok(days.includes('<v>'+d.difference/100+'</v>'));
    assert.match(ent,/Cash over\/short \(counted − expected\)/);assert.match(ent,/Carried forward to next day/);assert.ok(!/t="inlineStr"><is><t[^>]*>-?\d+\.\d\d</.test(days));});
  test('[CB] W05-7/W05-8 render: day view shows the over/short line and the carry; range shows the over/short total and an Excel button (desktop + phone)',()=>{
    const fy=M.loadScenario('FY26');
    for(const phone of [false,true]){const h=CBUI.renderDay(fy,'2026-10-22',phone);assert.match(h,/Cash over\/short/);assert.match(h,/Carried to next day<\/span><strong>₹5,000</);}
    const h=CBUI.renderRange(fy,false);assert.match(h,/Cash over\/short \(counted − expected\)/);assert.match(h,/Excel/);assert.match(h,/Over\/short/);
    assert.match(CBUI.renderRange(fy,true),/Excel/);});
  test('[XLSX] F9: Occupancy %, ADR and RevPAR are numbers (0.0% / ₹ formats) with the screen rounding, in the ratio rows and the daily table',()=>{
    const s=M.loadScenario('FY26'),pack=M.reportPack(s,'2026-06-01','2026-06-30'),z=unzip(RX.reportXlsx({hotel:'x',scenario:'FY26',clock:s.clock},pack));
    const sheets=RX.reportSheets({hotel:'x',scenario:'FY26',clock:s.clock},pack),ix=sheets.findIndex(x=>x.name==='Occupancy by date')+1,occIx=sheets.findIndex(x=>x.name==='Occupancy')+1;
    const tbl=z['xl/worksheets/sheet'+ix+'.xml'],tab=z['xl/worksheets/sheet'+occIx+'.xml'];
    assert.equal((tbl.match(/<c r="D\d+" s="5"><v>/g)||[]).length,30);assert.equal((tbl.match(/<c r="F\d+" s="2"><v>/g)||[]).length,30);assert.equal((tbl.match(/<c r="G\d+" s="2"><v>/g)||[]).length,30);
    assert.ok(!/<c r="D\d+" s="0" t="inlineStr"><is><t[^>]*>[\d.]+%/.test(tbl));
    const o=pack.tabs.find(t=>t.id==='occupancy'),r=o.ratios[0],pct=parseFloat(r.display)/100;
    assert.ok(tab.includes('s="5"><v>'+(+pct.toPrecision(15))+'</v>'),'occupancy % '+r.display);
    const adr=o.ratios[1];assert.ok(tab.includes('s="2"><v>'+Math.round(adr.value.num/adr.value.den)/100+'</v>'),'ADR '+adr.display);
    assert.match(z['xl/styles.xml'],/formatCode="0\.0%"/);});
}
