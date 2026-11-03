// F7-R1 · writes the cross-side report catalogue (RPT-ARCHITECTURE §2): 03 Prototype/src/domain/reports/catalogue.json and its copy in
// 08 Production/tests/Hcc.Reporting.Tests/catalogue.json (the server test compares it with ReportCatalogue). Run from 03 Prototype:
//   node tools/rpt-catalogue.mjs          write both files
//   node tools/rpt-catalogue.mjs --check  exit 1 when either file is stale
import {build} from 'esbuild';import {readFileSync,writeFileSync,existsSync} from 'node:fs';
await build({entryPoints:['src/services/mock/index.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/mock-catalogue.mjs',logLevel:'error'});
const M=await import('../tests/.generated/mock-catalogue.mjs');
const text=JSON.stringify(M.allReports().map(M.catalogueRecord),null,1)+'\n';
const files=['src/domain/reports/catalogue.json','../08 Production/tests/Hcc.Reporting.Tests/catalogue.json'];
if(process.argv.includes('--check')){
  const stale=files.filter(f=>!existsSync(f)||readFileSync(f,'utf8')!==text);
  if(stale.length){console.error('Stale catalogue: '+stale.join(', ')+' — run node tools/rpt-catalogue.mjs');process.exit(1);}
  console.log('Catalogue up to date ('+M.allReports().length+' reports).');
}else{for(const f of files)writeFileSync(f,text);console.log('Wrote',files.join(' + '));}
