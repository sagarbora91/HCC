// ADM-1 · Feature catalogue golden: writes 08 Production/database/feature-catalogue.json from the prototype's
// src/domain/feature-catalogue.ts (the single source). Run in 03 Prototype:
//   node tools/feature-catalogue.mjs --write     regenerate the golden
//   node tools/feature-catalogue.mjs             check only (exit 1 when stale)
// The prototype test tests/stream-features.mjs and the server test FeatureCatalogTests fail when the golden drifts from either side.
import {build} from 'esbuild';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {fileURLToPath,pathToFileURL} from 'node:url';
import path from 'node:path';

const proto=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
const root=path.resolve(proto,'..');
const out=path.join(root,'08 Production','database','feature-catalogue.json');
const gen=path.join(proto,'tests','.generated');
await mkdir(gen,{recursive:true});
const entry=path.join(gen,'feature-catalogue-entry.mjs');
await build({entryPoints:[path.join(proto,'src','domain','feature-catalogue.ts')],bundle:true,platform:'node',format:'esm',outfile:entry,logLevel:'error',absWorkingDir:proto});
const M=await import(pathToFileURL(entry).href); // f12-int: Windows paths need a file:// URL (Node 25)
const text=JSON.stringify({format:'hcc-feature-catalogue',version:1,modules:M.featureCatalogueJson()},null,2)+'\n';
if(process.argv.includes('--write')){await writeFile(out,text);console.log('Wrote',out);}
else{
  let cur='';try{cur=await readFile(out,'utf8');}catch{/* missing */}
  if(cur!==text){console.error('feature-catalogue.json is stale: run node tools/feature-catalogue.mjs --write');process.exit(1);}
  console.log('feature-catalogue.json is current');
}
