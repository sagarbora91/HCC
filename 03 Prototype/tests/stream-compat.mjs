// Missing-API regression: execute bundled application code in an isolated realm.
// This verifies application behavior, not a complete WebView or APK certification.
import {build} from 'esbuild';
import vm from 'node:vm';
import {readFileSync,readdirSync,statSync} from 'node:fs';
const bundle=await build({entryPoints:['src/services/mock/index.ts'],bundle:true,write:false,platform:'browser',format:'iife',globalName:'HCC',target:'chrome83',logLevel:'error'});
const context=vm.createContext({console,TextEncoder}); // TextEncoder exists on Chrome 83 (used by the xlsx writer)
vm.runInContext(`
  globalThis.structuredClone=undefined;
  Array.prototype.at=undefined;
  String.prototype.replaceAll=undefined;
  const OriginalNumberFormat=Intl.NumberFormat;
  Intl.NumberFormat=function(locale,options){
    if(options?.style==='currency' && options.maximumFractionDigits<2 && options.minimumFractionDigits===undefined)
      throw new RangeError('Legacy currency minimum exceeds maximum');
    return new OriginalNumberFormat(locale,options);
  };
`,context);
vm.runInContext(bundle.outputFiles[0].text,context);
// W05 C/D/E exports (cash book Excel, Reports Excel) run in the same legacy realm.
const xb=await build({stdin:{contents:"export {cbXlsx} from './src/features/cashbook/exportXlsx';export {reportXlsx} from './src/features/reports/exportXlsx';",resolveDir:'.',loader:'ts'},bundle:true,write:false,platform:'browser',format:'iife',globalName:'HCCX',target:'chrome83',logLevel:'error'});
vm.runInContext(xb.outputFiles[0].text,context);
// Source scan: W05 cash book / compliance / export files must not use APIs missing on Chrome 83 (AC-01 regression class). Use domain/clone.ts for deep copies.
const W05_FILES=['src/domain/streams/cashbook.ts','src/domain/streams/compliance.ts','src/domain/streams/accounting.ts','src/domain/streams/assistant.ts','src/app/xlsx.ts','src/app/native.ts',
  'src/services/mock/handlers/cashbook.ts','src/services/mock/handlers/compliance.ts','src/features/cashbook/CashBook.tsx','src/features/cashbook/exportXlsx.ts','src/features/reports/exportXlsx.ts',
  'src/features/compliance','src/features/accounting/Accounting.tsx','src/fixtures/ext/compliance.ts'];
export function register({test,assert,M}){
  test('[COMPAT] JSON fallback matches native command results and rejected commands preserve input',()=>{
    const actual=vm.runInContext(`(()=>{
      const input=HCC.seed(),before=JSON.stringify(input);
      let denied=false;
      try{HCC.execute(input,'housekeeper',{type:'PAYMENT',id:'HCC-1041',amount:100,method:'Cash'});}catch(e){denied=e.code==='FORBIDDEN';}
      return {denied,unchanged:before===JSON.stringify(input),posted:JSON.stringify(HCC.execute(input,'owner',{type:'NIGHT_POST'}))};
    })()`,context);
    assert.equal(actual.denied,true);assert.equal(actual.unchanged,true);
    assert.equal(actual.posted,JSON.stringify(M.execute(M.seed(),'owner',{type:'NIGHT_POST'})));
  });
  test('[COMPAT] missing modern APIs: FY26 loads, commands isolate input and remain repeat-safe',()=>{
    const result=vm.runInContext(`(()=>{
      const history=HCC.loadScenario('FY26');
      const input=HCC.seed(),before=JSON.stringify(input);
      const next=HCC.execute(input,'owner',{type:'NIGHT_POST',key:'legacy-night'});
      const replay=HCC.execute(next,'owner',{type:'NIGHT_POST',key:'legacy-night'});
      const answer=HCC.execute(next,'owner',{type:'AI_ASK',question:'occupancy'});
      return {historyBookings:history.bookings.length,unchanged:before===JSON.stringify(input),
        separate:next!==input && next.bookings!==input.bookings,replayed:replay===next,
        audited:answer.audit[0].action==='AI ASK',drawer:HCC.drawer(input)};
    })()`,context);
    assert.ok(result.historyBookings>1000);
    assert.equal(result.unchanged,true);assert.equal(result.separate,true);
    assert.equal(result.replayed,true);assert.equal(result.audited,true);
    assert.equal(result.drawer,1075000);
  });
  test('[COMPAT] legacy currency options handle whole rupees, paise and negative amounts',()=>{
    const output=vm.runInContext(`[HCC.money(1075000),HCC.money(12345),HCC.money(-12345),HCC.money(0)]`,context);
    assert.deepEqual(Array.from(output),['₹10,750','₹123.45','-₹123.45','₹0']);
  });
  test('[COMPAT] W05 cash book, compliance and Excel exports work without .at / replaceAll / structuredClone (Chrome 83)',()=>{
    const r=vm.runInContext(`(()=>{
      let s=HCC.loadScenario('FY26');const ds=HCC.cbDates(s),rg=HCC.cbRange(s,ds[0],s.date);
      const csv=HCC.cbCsv(s,'2026-10-01',s.date),lock=HCC.cbCanLock(s,'owner','2026-06-10').ok;
      s=HCC.execute(s,'accountant',{type:'CB_LOCK',date:'2026-06-10'});
      const items=HCC.cmpItems(s).length,intent=HCC.aiMatchIntent('Is GSTR-3B filed?');
      s=HCC.execute(s,'owner',{type:'CMP_OBLIGATION_SAVE',id:'CMPO-TDS',name:'TDS deposit',rule:{type:'day',day:7,special:{month:3,day:30}}});
      const xl=HCCX.cbXlsx(s,'2026-10-01',s.date).length,rx=HCCX.reportXlsx({hotel:'x',scenario:'FY26',clock:s.clock},HCC.reportPack(s,'2026-10-01','2026-10-23')).length;
      return {days:rg.days.length,foots:rg.opening+rg.receipts-rg.payments+rg.differences+rg.gaps===rg.closing,csv:csv.length>1000,lock,locked:!!s.cashbookX.locks['2026-06-10'],items,intent,xl,rx};
    })()`,context);
    assert.ok(r.days>200);assert.equal(r.foots,true);assert.equal(r.csv,true);assert.equal(r.lock,true);assert.equal(r.locked,true);assert.ok(r.items>20);assert.equal(r.intent,'compliance-due');
    assert.ok(r.xl>5000);assert.ok(r.rx>5000);});
  test('[COMPAT] W05 source scan: no .at( / replaceAll( / raw structuredClone in the cash book, compliance and export files',()=>{
    const files=W05_FILES.flatMap(f=>statSync(f).isDirectory()?readdirSync(f).filter(x=>/\.tsx?$/.test(x)).map(x=>f+'/'+x):[f]);assert.ok(files.length>=15);
    const bad=files.flatMap(f=>readFileSync(f,'utf8').split('\n').map((l,i)=>[f+':'+(i+1),l]).filter(([,l])=>/\.at\(|\.replaceAll\(|\bstructuredClone\b/.test(l)).map(([w])=>w));
    assert.deepEqual(bad,[]);});
}
