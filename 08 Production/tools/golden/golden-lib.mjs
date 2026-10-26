// Shared by every golden exporter (tools/golden/export-golden.mjs and tests/Hcc.*.Tests/Golden/export-*-golden.mjs).
//  - PROTO: the prototype the goldens come from = the 03 Prototype of THIS checkout (override: --proto <path>), so a worktree
//    regenerates from its own prototype and the drift check compares like with like (A08-04).
//  - bundleMock / bundleSource: bundle prototype sources with the prototype's own esbuild (JS API: no shell, nothing installed).
//  - writeGolden(dest, text): writes the golden file. With --check it writes nothing and fails (exit code 1) when the committed file
//    differs from what the current prototype produces, listing the first differing JSON paths. tools/golden/check-goldens.mjs runs
//    every exporter with --check, and 03 Prototype/tests/stream-goldens.mjs runs that check inside `npm test`, so the server goldens
//    cannot silently drift from the prototype again (A08-04 / A03a-12 / A03b-07).
import {createRequire} from 'node:module';
import path from 'node:path';import fs from 'node:fs';import {fileURLToPath,pathToFileURL} from 'node:url';
const HERE=path.dirname(fileURLToPath(import.meta.url));
export const PROD=path.resolve(HERE,'..','..'),ROOT=path.resolve(PROD,'..');
const arg=n=>{const i=process.argv.indexOf(n);return i>0?process.argv[i+1]:undefined;};
export const PROTO=path.resolve(arg('--proto')??path.join(ROOT,'03 Prototype'));
export const CHECK=process.argv.includes('--check');
if(!fs.existsSync(path.join(PROTO,'src','services','mock','index.ts')))throw new Error('Prototype sources not found in '+PROTO+' (pass --proto <path to 03 Prototype>)');
const esbuild=()=>{try{return createRequire(path.join(PROTO,'package.json'))('esbuild');}
  catch{throw new Error('esbuild not found under '+PROTO+'/node_modules; run npm install in 03 Prototype');}};
const OUT=path.join(PROD,'tools','golden','.generated');
const load=async(name,opts)=>{fs.mkdirSync(OUT,{recursive:true});const outfile=path.join(OUT,name+'.mjs');
  await esbuild().build({...opts,bundle:true,platform:'node',format:'esm',outfile,logLevel:'error'});
  return import(pathToFileURL(outfile).href);};
/** The prototype mock service (src/services/mock/index.ts) as an ES module. */
export const bundleMock=name=>load(name,{entryPoints:[path.join(PROTO,'src','services','mock','index.ts')]});
/** An ad-hoc entry module; imports are relative to 03 Prototype/src (e.g. `export * from './domain/streams/messaging';`). */
export const bundleSource=(name,contents)=>load(name,{stdin:{contents,resolveDir:path.join(PROTO,'src'),sourcefile:name+'-entry.ts',loader:'ts'}});

/** First differing JSON paths between two parsed values (committed vs regenerated). */
export function goldenDiff(a,b,p='$',out=[],max=25){
  if(out.length>=max)return out;
  if(typeof a!==typeof b||Array.isArray(a)!==Array.isArray(b)||a===null||b===null||typeof a!=='object'){
    if(JSON.stringify(a)!==JSON.stringify(b))out.push(`${p}: committed=${JSON.stringify(a)?.slice(0,120)} | prototype=${JSON.stringify(b)?.slice(0,120)}`);
    return out;
  }
  if(Array.isArray(a)&&a.length!==b.length)out.push(`${p}: array length committed=${a.length} prototype=${b.length}`);
  for(const k of new Set([...Object.keys(a),...Object.keys(b)]))goldenDiff(a[k],b[k],`${p}.${k}`,out,max);
  return out;
}

/** Writes the golden (normal run) or, with --check, compares it with the committed file. Returns true when written / up to date. */
export function writeGolden(dest,text){
  const rel=path.relative(ROOT,dest).split(path.sep).join('/');
  if(!CHECK){fs.mkdirSync(path.dirname(dest),{recursive:true});fs.writeFileSync(dest,text);return true;}
  const committed=fs.existsSync(dest)?fs.readFileSync(dest,'utf8').replace(/\r\n/g,'\n'):null;
  if(committed===text){console.log('golden up to date:',rel);return true;}
  let paths=[];try{paths=committed===null?['(file missing)']:goldenDiff(JSON.parse(committed),JSON.parse(text));}catch(e){paths=['(not JSON: '+e.message+')'];}
  console.error(`GOLDEN OUT OF DATE: ${rel} differs from what ${path.relative(ROOT,process.argv[1]).split(path.sep).join('/')} produces from ${path.relative(ROOT,PROTO)||'.'}.`);
  console.error('Regenerate it (run the exporter without --check), then update and re-run the server tests that read it. Never hand-edit golden numbers.');
  for(const l of paths.length?paths:['(same values, different formatting)'])console.error('  '+l);
  process.exitCode=1;return false;
}
