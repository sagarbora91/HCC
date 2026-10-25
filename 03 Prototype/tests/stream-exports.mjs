// W05-E: .xlsx writer (stored ZIP + OOXML), Reports workbook, binary save path, Tally XML preview.
import {build} from 'esbuild';
import {inflateRawSync} from 'node:zlib';
await build({entryPoints:['src/app/xlsx.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/xlsx.mjs',logLevel:'error'});
await build({entryPoints:['src/features/reports/exportXlsx.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/exportXlsx.mjs',logLevel:'error'});
await build({entryPoints:['src/app/native.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/native-x.mjs',logLevel:'error',external:['react']});
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
  test('[XLSX] saveBinaryFile: native path writes base64 to Documents/HCC then shares',()=>{
    const w=R.calls.find(c=>c[1]==='writeFile')[2];assert.equal(R.r,'shared');assert.equal(w.path,'HCC/HCC-x.xlsx');assert.equal(w.directory,'DOCUMENTS');
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
}
