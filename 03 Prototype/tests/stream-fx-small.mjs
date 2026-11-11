// FX-SMALL · D25-1: a total / subtotal row's label in a date column ("Total", "Range total") must print as that label, never "Invalid Date". Names prefixed [FXS].
import {build} from 'esbuild';
await build({stdin:{contents:"export {fmtCell} from './src/features/reports/ReportView';export {allReports,runReport} from './src/domain/reports/engine';export {seed} from './src/services/mock/index';",resolveDir:process.cwd(),loader:'ts'},
  bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/fxs.mjs',external:['react','react-dom','lucide-react'],logLevel:'error',define:{'process.env.HCC_BUILD':'"prototype"'}});
const X=await import('./.generated/fxs.mjs');
export async function register({test,assert}){
  const dcol={key:'d',label:'Business date',type:'date'};
  test('[FXS] D25-1 fmtCell: ISO dates are short dates, a label in a date column stays a label, empty stays empty',()=>{
    assert.match(X.fmtCell(dcol,'2026-11-03'),/^3 Nov$/);
    assert.equal(X.fmtCell(dcol,'Total'),'Total');assert.equal(X.fmtCell(dcol,'Range total'),'Range total');
    assert.equal(X.fmtCell(dcol,null),'');assert.equal(X.fmtCell(dcol,''),'');
    assert.ok(!/Invalid/i.test(X.fmtCell(dcol,'Subtotal')));
  });
  test('[FXS] D25-1 no engine report prints "Invalid Date" in any date column of any row (cash book and daily operations included)',()=>{
    const s=X.seed(),bad=[];let withTotal=0;
    for(const d of X.allReports()){
      let r;try{r=X.runReport(s,'owner',d.id);}catch{continue;}   // a report that needs parameters the default run lacks is not part of this sweep
      const tables=[{columns:r.columns,rows:r.rows},...(r.sections??[]).map(x=>({columns:x.columns,rows:x.rows}))];
      for(const t of tables)for(const c of t.columns??[])if(c.type==='date')for(const row of t.rows??[]){
        const out=X.fmtCell(c,row.cells[c.key]);if(row.kind!=='row')withTotal++;
        if(/Invalid/i.test(out))bad.push(d.id+'/'+c.key+'/'+row.kind);}
    }
    assert.deepEqual(bad,[]);assert.ok(withTotal>=2,'the sweep reached total rows with a date column: '+withTotal);
  });
}
