// Missing-API regression: execute bundled application code in an isolated realm.
// This verifies application behavior, not a complete WebView or APK certification.
import {build} from 'esbuild';
import vm from 'node:vm';
const bundle=await build({entryPoints:['src/services/mock/index.ts'],bundle:true,write:false,platform:'browser',format:'iife',globalName:'HCC',target:'chrome83',logLevel:'error'});
const context=vm.createContext({console});
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
}
