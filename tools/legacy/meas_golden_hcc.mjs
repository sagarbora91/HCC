// F12 MEAS · legacy golden, HCC side in DRY mode (tools/meas-legacy-golden.ps1 -Dry). Runs on the prototype's FY26 fixture (fictional data only):
//   1. every ported study report that is registered (and the management pack) for the FY, as the Owner → results JSON (the same shape the server
//      API returns: id, fy, sections[{title, columns[{key,label,type}], rows[{kind,cells}]}]);
//   2. the raw facts the Python reference recomputes independently (room lines: room, night date, amount in paise; rooms in service).
// Usage (from any folder): node tools/legacy/meas_golden_hcc.mjs --fy 2025-26 --results <file> --facts <file>
import {createRequire} from 'node:module';import {writeFileSync,mkdirSync} from 'node:fs';import {dirname,resolve} from 'node:path';import {fileURLToPath,pathToFileURL} from 'node:url';
const here=dirname(fileURLToPath(import.meta.url));const proto=resolve(here,'../../03 Prototype');
const arg=k=>{const i=process.argv.indexOf('--'+k);return i>0?process.argv[i+1]:undefined;};
const fy=arg('fy')??'2026-27',resultsOut=arg('results'),factsOut=arg('facts');
if(!resultsOut||!factsOut){console.error('usage: --fy <yyyy-yy> --results <file> --facts <file>');process.exit(2);}
const {build}=createRequire(resolve(proto,'package.json'))('esbuild');
const bundle=resolve(proto,'tests/.generated/meas-golden-dry.mjs');
await build({entryPoints:[resolve(proto,'src/services/mock/index.ts')],bundle:true,platform:'node',format:'esm',outfile:bundle,logLevel:'error'});
const M=await import(pathToFileURL(bundle).href);
const s=M.loadScenario('FY26');
const ids=M.allReports().map(d=>d.id).filter(id=>id.startsWith('rpt.study.'));
const results=[];
for(const id of ids){
  try{const r=M.runReport(s,'owner',id,{fy},{all:true});
    results.push({id,fy,sections:M.rptTables(r).map(t=>({key:t.key,title:t.title??r.title,columns:t.columns.map(c=>({key:c.key,label:c.label,type:c.type})),rows:t.rows.map(x=>({kind:x.kind,cells:x.cells}))}))});}
  catch(e){results.push({id,fy,error:String(e?.message??e)});}
}
const from=fy.slice(0,4)+'-04-01',fyEnd=(+fy.slice(0,4)+1)+'-03-31',to=fyEnd>s.date&&from<=s.date?s.date:fyEnd; // the report binder stops a current FY at the business date
const lines=[];for(const b of s.bookings??[]){if(b.status==='Cancelled'||b.status==='Expired')continue;for(const l of b.lines??[])if(l.kind==='room'&&l.date>=from&&l.date<=to)lines.push([b.room,l.date,l.amount]);}
const rooms=(s.rooms??[]).filter(r=>!/hall/i.test(r.type)).length;
for(const f of [resultsOut,factsOut])mkdirSync(dirname(resolve(f)),{recursive:true});
writeFileSync(resultsOut,JSON.stringify({source:'prototype FY26 fixture (fictional)',fy,reports:results},null,1));
writeFileSync(factsOut,JSON.stringify({fy,from,to,rooms,lines}));
console.log(`HCC side (dry): ${results.length} study report(s) run for FY ${fy} (${results.filter(r=>r.error).length} failed); ${lines.length} room lines, ${rooms} rooms.`);
