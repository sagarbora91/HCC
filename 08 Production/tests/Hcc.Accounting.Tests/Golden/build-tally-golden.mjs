// ACC-3 acc3-tally · writes Golden/tally-april-2026.input.json (the April 2026 voucher / party / chart data of the ACC-RULEBOOK scenario, fictional),
// Golden/tally-april-2026.masters.xml and Golden/tally-april-2026.vouchers.xml (built by the PROTOTYPE selectors from that input).
// The server test (Tally/TallyGoldenTests.cs) builds the same two files from the same input.json with Hcc.Domain's VoucherExport and compares the bytes;
// the prototype test (tests/stream-accounting-tally.mjs) does the same with the prototype. Run after a deliberate change of either builder:
//   node "08 Production/tests/Hcc.Accounting.Tests/Golden/build-tally-golden.mjs"      (from the repo root; needs `npm install` in 03 Prototype)
import {writeFileSync,mkdirSync} from 'node:fs';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {dirname,resolve} from 'node:path';
const here=dirname(fileURLToPath(import.meta.url));
const proto=resolve(here,'../../../../03 Prototype');
const {build}=await import(pathToFileURL(resolve(proto,'node_modules/esbuild/lib/main.js')).href);
mkdirSync(resolve(proto,'tests/.generated'),{recursive:true});
await build({entryPoints:[resolve(proto,'src/services/mock/index.ts')],bundle:true,platform:'node',format:'esm',outfile:resolve(proto,'tests/.generated/mock-tally.mjs'),logLevel:'error'});
const M=await import(pathToFileURL(resolve(proto,'tests/.generated/mock-tally.mjs')).href);
const s=M.loadScenario('ACC-RULEBOOK');
const FROM='2026-04-01',TO='2026-04-30',IDENTITY='GOLDEN',COMPANY='Rulebook Hotels Pvt Ltd (fictional)';
const input=M.accTallyGoldenInput(s,FROM,TO,COMPANY,IDENTITY);
writeFileSync(resolve(here,'tally-april-2026.input.json'),JSON.stringify(input,null,1)+'\n');
const masters=M.tallyMastersXml(M.tallyBuildBook(input.chart,input.opening,input.parties,input.seen),input.company,input.fyStart);
const vouchers=M.tallyVouchersXml(input.stream,new Set(input.emit),M.tallyBuildBook(input.chart,input.opening,input.parties,input.seen),input.chart,input.company,input.identity);
writeFileSync(resolve(here,'tally-april-2026.masters.xml'),masters);
writeFileSync(resolve(here,'tally-april-2026.vouchers.xml'),vouchers);
console.log('written: input.json',input.emit.length,'vouchers,',Object.keys(input.parties).length,'parties; masters',masters.length,'chars; vouchers',vouchers.length,'chars');
