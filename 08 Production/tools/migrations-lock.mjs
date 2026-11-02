// A08-05 migration lock: 08 Production/database/migrations.lock.json records every migration (version, file name, SHA-256) in the
// order it was added. The checksum is exactly MigrationRunner's MigrationScript.Checksum: SHA-256 (upper-case hex) of the script text
// as the runner reads it (UTF-8, a byte-order mark dropped) with CRLF turned into LF. Hcc.Platform.Tests MigrationLockTests fails when
// (a) a locked script changed, (b) a new script is numbered below the highest locked version (it would run out of order on a database
// that already applied the higher one), (c) a script is not in the lock. Entries are appended, never edited or removed.
// Usage (Node 25, from "08 Production"; add --docs to every form for the HccDocuments stream, migrations-docs/):
//   node tools/migrations-lock.mjs                 check only; exit code 1 and the list of problems when the lock is out of step
//   node tools/migrations-lock.mjs --add           append every migration file that is not in the lock yet (refused below the highest version)
//   node tools/migrations-lock.mjs --add --accept-out-of-order "<reason>"
//                                                   merge of a parallel branch only: also append files numbered below the highest locked
//                                                   version and record them, with the reason, as accepted out-of-order history
import {createHash} from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';import {fileURLToPath} from 'node:url';

const PROD=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'..');
// LF-docdb: --docs selects the documents database stream (database/migrations-docs + migrations-docs.lock.json).
const DOCS=process.argv.slice(2).includes('--docs');
export const MIGRATIONS=path.join(PROD,'database',DOCS?'migrations-docs':'migrations');
export const LOCK=path.join(PROD,'database',DOCS?'migrations-docs.lock.json':'migrations.lock.json');
const NAME=/^(\d{4})_([a-z0-9_]+)\.sql$/;

/** MigrationScript.Checksum: UTF-8 without BOM, CRLF -> LF, SHA-256, upper-case hex. */
export function checksum(bytes){
  let b=Buffer.from(bytes);
  if(b.length>=3&&b[0]===0xef&&b[1]===0xbb&&b[2]===0xbf)b=b.subarray(3);
  return createHash('sha256').update(b.toString('utf8').replace(/\r\n/g,'\n'),'utf8').digest('hex').toUpperCase();
}

/** Every NNNN_name.sql in the migrations folder (a file name the runner would refuse is reported, not skipped). */
export function scripts(dir=MIGRATIONS){
  const out=[],bad=[];
  for(const file of fs.readdirSync(dir).filter(f=>f.toLowerCase().endsWith('.sql')).sort()){
    const m=NAME.exec(file);
    if(!m){bad.push(file);continue;}
    out.push({version:Number(m[1]),file,sha256:checksum(fs.readFileSync(path.join(dir,file)))});
  }
  return {scripts:out.sort((a,b)=>a.version-b.version),bad};
}

export function readLock(file=LOCK){
  if(!fs.existsSync(file))return {format:1,acceptedOutOfOrder:[],migrations:[]};
  return JSON.parse(fs.readFileSync(file,'utf8'));
}

const pad=v=>String(v).padStart(4,'0');

/** The same rules as MigrationLockTests: one message per problem, empty = in step. */
export function problems(lock,list){
  const out=[];
  const entries=lock.migrations??[],accepted=new Map((lock.acceptedOutOfOrder??[]).map(a=>[a.version,a]));
  const byVersion=new Map(list.map(s=>[s.version,s]));
  const seen=new Set();let high=0;
  for(const e of entries){
    if(seen.has(e.version))out.push(`${pad(e.version)} is in the lock twice.`);
    seen.add(e.version);
    const s=byVersion.get(e.version);
    if(!s)out.push(`(a) ${e.file} is in the lock but its file is gone (a locked migration is never removed or renamed).`);
    else if(s.file!==e.file)out.push(`(a) ${pad(e.version)} is locked as ${e.file} but the file is ${s.file} (a locked migration is never renamed).`);
    else if(s.sha256!==e.sha256)out.push(`(a) ${e.file} changed after it was locked (checksum drift on every database that applied it): add a new migration instead.`);
    if(e.version<high&&!accepted.has(e.version))out.push(`(b) ${e.file} comes after ${pad(high)} in the lock but is numbered below it, and is not accepted out-of-order history.`);
    high=Math.max(high,e.version);
  }
  for(const [v,a] of accepted){
    if(!seen.has(v))out.push(`Accepted out-of-order entry ${pad(v)} is not in the lock's migrations.`);
    if(!String(a.reason??'').trim())out.push(`Accepted out-of-order entry ${pad(v)} has no reason.`);
  }
  for(const s of list.filter(s=>!seen.has(s.version))){
    if(s.version<high)out.push(`(b) ${s.file} is numbered below the highest locked migration ${pad(high)}: on a database that already has ${pad(high)} it would run out of order. Renumber it above ${pad(high)}.`);
    out.push(`(c) ${s.file} is not in migrations.lock.json: run node tools/migrations-lock.mjs --add (from 08 Production).`);
  }
  return out;
}

/** One entry per line, so parallel branches that each append entries merge line by line. */
export function format(lock){
  const j=x=>JSON.stringify(x);
  const acc=(lock.acceptedOutOfOrder??[]).map(a=>`    {"version": ${a.version}, "file": ${j(a.file)}, "reason": ${j(a.reason)}}`);
  const mig=(lock.migrations??[]).map(e=>`    {"version": ${e.version}, "file": ${j(e.file)}, "sha256": ${j(e.sha256)}}`);
  return ['{',
    '  "format": 1,',
    `  "about": ${j('A08-05: every migration in the order it was added. sha256 = MigrationScript.Checksum (UTF-8 without BOM, CRLF -> LF, SHA-256, upper-case hex). Append only, through tools/migrations-lock.mjs --add; a new migration is numbered above the highest version here. Checked by Hcc.Platform.Tests MigrationLockTests.')},`,
    '  "acceptedOutOfOrder": ['+(acc.length?'\n'+acc.join(',\n')+'\n  ':'')+'],',
    '  "migrations": ['+(mig.length?'\n'+mig.join(',\n')+'\n  ':'')+']',
    '}',''].join('\n');
}

if(process.argv[1]&&path.resolve(process.argv[1])===fileURLToPath(import.meta.url)){
  const args=process.argv.slice(2),add=args.includes('--add'),i=args.indexOf('--accept-out-of-order');
  const reason=i>=0?String(args[i+1]??'').trim():null;
  if(i>=0&&!reason){console.error('--accept-out-of-order needs a reason (why these lower numbers are accepted).');process.exit(2);}
  const lock=readLock(),{scripts:list,bad}=scripts();
  if(bad.length){console.error('Migration file names must look like 0001_name.sql: '+bad.join(', '));process.exit(1);}
  if(!add){
    const p=problems(lock,list);
    for(const x of p)console.log(x);
    console.log(p.length?`${p.length} problem(s): migrations.lock.json is out of step.`:`migrations.lock.json is in step (${list.length} migrations).`);
    if(p.length)process.exitCode=1;
  }else{
    // Existing entries must still match before anything is appended.
    const locked=new Set((lock.migrations??[]).map(e=>e.version));
    const old=problems(lock,list.filter(s=>locked.has(s.version)));
    if(old.length){for(const x of old)console.error(x);console.error('Nothing appended: fix the locked entries first (add a new migration instead of editing an applied one).');process.exit(1);}
    const high=Math.max(0,...locked);
    const fresh=list.filter(s=>!locked.has(s.version));
    const low=fresh.filter(s=>s.version<high);
    if(low.length&&reason===null){
      for(const s of low)console.error(`${s.file} is numbered below the highest locked migration ${pad(high)}: renumber it above ${pad(high)}.`);
      console.error('Nothing appended. Only when merging a parallel branch whose migrations are already applied somewhere, rerun with --accept-out-of-order "<reason>".');
      process.exit(1);
    }
    lock.format=1;lock.acceptedOutOfOrder=lock.acceptedOutOfOrder??[];lock.migrations=lock.migrations??[];
    for(const s of fresh){
      lock.migrations.push({version:s.version,file:s.file,sha256:s.sha256});
      if(s.version<high)lock.acceptedOutOfOrder.push({version:s.version,file:s.file,reason});
    }
    fs.writeFileSync(LOCK,format(lock));
    console.log(fresh.length?`Appended ${fresh.length}: ${fresh.map(s=>s.file).join(', ')}`:'Nothing to append: every migration is locked.');
    const p=problems(lock,list);
    if(p.length){for(const x of p)console.error(x);process.exitCode=1;}
  }
}
