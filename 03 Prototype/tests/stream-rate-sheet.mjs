// F4-T1 · rpt.admin.rate-sheet (extended): print = screen figures, GST line, valid-from line, blank child "On request", CSV of the card
// and of the version history. Names prefixed [RSHEET].
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/rsheet-entry.tsx',"export * from '../../src/features/reports/rate-sheet';\nexport {trcEditorRows} from '../../src/features/tariffcard/logic';");
  await build({entryPoints:['tests/.generated/rsheet-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/rsheet.mjs',logLevel:'error',jsx:'automatic',external:['react','react-dom','lucide-react']});
  const X={...await import('./.generated/rsheet.mjs'),renderToStaticMarkup:(await import('react-dom/server')).renderToStaticMarkup,createElement:(await import('react')).createElement};
  const {seed,execute}=M;
  const pub=()=>{let s=execute(seed(),'owner',{type:'SETTING_SET',setting:'gst.room.lowBps',value:1800,effectiveFrom:'2026-11-01',reason:'DEC-198'});
    s=execute(s,'owner',{type:'TRC_DRAFT_SAVE',label:'Card 2026-27',effectiveFrom:'2026-11-01',lines:[{kind:'room',roomType:'Deluxe',enteredIncl:200000},{kind:'room',roomType:'Suite',enteredIncl:350000},
      {kind:'occupancy',roomType:'Deluxe',occKind:'adult',rateExcl:42373},{kind:'occupancy',roomType:'Deluxe',occKind:'child',rateExcl:null,ageFrom:5,ageTo:12},{kind:'meal',mealPlan:'CP',rateExcl:8475}]});
    return execute(s,'owner',{type:'TRC_PUBLISH',id:'TRC-0001'});};
  const html=(s,id)=>X.renderToStaticMarkup(X.createElement(X.RateSheetDoc,{sheet:X.rateSheetModel(s,id)}));
  const rs=n=>'₹'+(n/100).toLocaleString('en-IN',{minimumFractionDigits:n%100?2:0,maximumFractionDigits:2});

  test('[RSHEET] print = screen: every inclusive figure of the editor grid is on the sheet',()=>{
    const s=pub(),v=s.tariffCardX.versions[0],h=html(s);
    for(const r of X.trcEditorRows(s,v.lines))if(r.incl!==null)assert.ok(h.includes(rs(r.incl)),`${r.title} ${rs(r.incl)}`);
    const m=X.rateSheetModel(s);assert.deepEqual(m.rows.map(r=>r.incl).sort(),X.trcEditorRows(s,v.lines).map(r=>r.incl).sort());
  });
  test('[RSHEET] GST line from the published room lines (18 % per DEC-198) and the valid-from line',()=>{
    const s=pub(),h=html(s);assert.match(h,/Prices include GST @ 18 %/);assert.match(h,/Valid from 1 November 2026/);assert.match(h,/On request/);assert.match(h,/Weekend nights: Fri, Sat, Sun/);
  });
  test('[RSHEET] no published card → a blank-safe sheet; terms line hidden when blank',()=>{
    const s=seed();assert.match(html(s),/No published tariff card yet/);assert.doesNotMatch(html(pub()),/trc-sheet-terms/);
  });
  test('[RSHEET] CSV of the card = sheet rows; history CSV lists every version',()=>{
    const s=pub();const csv=X.rateSheetCsv(s).trim().split('\r\n');assert.equal(csv.length,1+X.rateSheetModel(s).rows.length);
    assert.match(csv[0],/Price incl GST/);assert.ok(csv.some(l=>l.includes('2000.00')),'Deluxe 2,000');
    const h=X.rateSheetHistoryCsv(s).trim().split('\r\n');assert.equal(h.length,2);assert.match(h[1],/^TRC-0001,Card 2026-27,2026-11-01,published/);
  });
}
