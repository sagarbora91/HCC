// HCC A3 SQL Server spike runner. Creates a DISPOSABLE database on the local SQL Server Express instance, applies the draft
// schema, runs single-connection invariant tests and real two-connection concurrency tests through sqlcmd (Windows auth),
// writes evidence/sql-spike-results.json, then drops the database (pass --keep to inspect it).
// Touches no other database on the instance. No npm packages: driver/library selection is a separate A4 step.
// Usage: node "07 Integration Lab/sql-spike/run-spike.mjs" [--keep] [--server .\SQLEXPRESS]
import {spawn} from 'node:child_process';
import path from 'node:path';import fs from 'node:fs';import {fileURLToPath} from 'node:url';
const DIR=path.dirname(fileURLToPath(import.meta.url)),LAB=path.resolve(DIR,'..');
const arg=n=>{const i=process.argv.indexOf(n);return i>0?process.argv[i+1]:undefined;};
const SERVER=arg('--server')||'.\\SQLEXPRESS',KEEP=process.argv.includes('--keep');
const stamp=new Date().toISOString().replace(/\D/g,'').slice(0,12);
const DB=`HCC_Spike_A3_${stamp}`;
if(!/^HCC_Spike_A3_\d{12}$/.test(DB))throw new Error('unexpected database name');

function sql({query,file,db=DB,abort=true}){
  // -I: QUOTED_IDENTIFIER ON (sqlcmd defaults it OFF; filtered indexes require ON — same setting a driver uses by default).
  const args=['-S',SERVER,'-E','-C','-I','-d',db,'-W','-h','-1'];if(abort)args.push('-b');
  if(file)args.push('-i',file);else args.push('-Q',query);
  return new Promise(res=>{const t0=Date.now();const p=spawn('sqlcmd',args);let out='';
    p.stdout.on('data',d=>out+=d);p.stderr.on('data',d=>out+=d);
    p.on('close',code=>res({code,out:out.trim(),ms:Date.now()-t0}));});
}
const sleep=ms=>new Promise(r=>setTimeout(r,ms));
const results=[];const log=(id,ok,detail)=>{results.push({id,status:ok?'passed':'failed',detail});console.log(`${ok?'PASS':'FAIL'} ${id} ${detail}`);};

const version=(await sql({db:'master',query:"SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS varchar(20))+' '+CAST(SERVERPROPERTY('Edition') AS varchar(60))"})).out;
console.log('Server:',version,'· database:',DB);
let r=await sql({db:'master',query:`CREATE DATABASE [${DB}]`});if(r.code)throw new Error(r.out);
try{
  r=await sql({file:path.join(DIR,'01-schema.sql')});
  log('S00',r.code===0,r.code?r.out:'draft schema + procedures applied');
  r=await sql({file:path.join(DIR,'02-tests.sql'),abort:false});
  for(const line of r.out.split(/\r?\n/)){const m=line.match(/^(PASS|FAIL) (T\d+) (.*)$/);if(m)log(m[2],m[1]==='PASS',m[3]);}
  for(let i=1;i<=10;i++){const id='T'+String(i).padStart(2,'0');if(!results.some(x=>x.id===id))log(id,false,'did not report (batch error): '+r.out.slice(0,300));}

  // C1 · two connections book overlapping nights of room 102. A holds its transaction open 3 s; B starts 0.7 s later.
  {const a=sql({query:"EXEC inv.usp_book 1, 2, '2026-11-01', '2026-11-03', N'Racer A', 'C1A', 1, '00:00:03.000'"});
   await sleep(700);const b=await sql({query:"EXEC inv.usp_book 1, 2, '2026-11-02', '2026-11-04', N'Racer B', 'C1B'"});const ra=await a;
   const nights=(await sql({query:"SET NOCOUNT ON; SELECT r.public_no+':'+CONVERT(char(10),n.night,23) FROM inv.room_night n JOIN inv.reservation r ON r.reservation_id=n.reservation_id WHERE n.room_id=2 ORDER BY n.night"})).out.split(/\s+/).join(' ');
   log('C1',ra.code===0&&b.code!==0&&/2627|PRIMARY KEY/.test(b.out)&&b.ms>=1500&&nights==='C1A:2026-11-01 C1A:2026-11-02',
     `A committed (${ra.ms} ms); B blocked ${b.ms} ms then failed with PK violation; room 102 nights = ${nights}`);}
  // C2 · A allocates room 201 then ROLLS BACK; B (waiting on the same keys) then succeeds.
  {const a=sql({query:"BEGIN TRAN; EXEC inv.usp_book 1, 3, '2026-11-01', '2026-11-03', N'Rollback A', 'C2A', 1, '00:00:02.000'; ROLLBACK;"});
   await sleep(700);const b=await sql({query:"EXEC inv.usp_book 1, 3, '2026-11-01', '2026-11-03', N'Winner B', 'C2B'"});const ra=await a;
   const owner=(await sql({query:"SET NOCOUNT ON; SELECT DISTINCT r.public_no FROM inv.room_night n JOIN inv.reservation r ON r.reservation_id=n.reservation_id WHERE n.room_id=3"})).out;
   log('C2',ra.code===0&&b.code===0&&owner==='C2B'&&b.ms>=1000,`A rolled back; B waited ${b.ms} ms then booked; room 201 owner = ${owner}`);}
  // C3 · two concurrent ₹750 refunds against one ₹1,000 receipt: exactly one succeeds.
  {await sql({query:"INSERT pay.receipt(folio_id,kind,method,amount_paise,business_date) SELECT f.folio_id,'payment','UPI',100000,'2026-10-24' FROM bill.folio f JOIN inv.reservation r ON r.reservation_id=f.reservation_id WHERE r.public_no='T03'"});
   const rid=(await sql({query:"SET NOCOUNT ON; SELECT MAX(receipt_id) FROM pay.receipt WHERE kind='payment'"})).out;
   const a=sql({query:`EXEC pay.usp_refund ${rid}, 75000, '2026-10-24', '00:00:03.000'`});await sleep(700);
   const b=await sql({query:`EXEC pay.usp_refund ${rid}, 75000, '2026-10-24'`});const ra=await a;
   const sum=(await sql({query:`SET NOCOUNT ON; SELECT ISNULL(SUM(amount_paise),0) FROM pay.receipt WHERE refund_of=${rid}`})).out;
   log('C3',ra.code===0&&b.code!==0&&/50010|remainder/.test(b.out)&&sum==='75000',`A refunded ₹750; B blocked ${b.ms} ms then rejected (remainder); total refunded = ${Number(sum)/100} ₹`);}
  // C4 · twelve parallel bookings for the same room-night: exactly one wins, no deadlock, no partial rows.
  {const runs=await Promise.all(Array.from({length:12},(_,i)=>sql({query:`EXEC inv.usp_book 1, 1, '2026-12-01', '2026-12-03', N'Rush ${i}', 'C4-${i}'`})));
   const won=runs.filter(x=>x.code===0).length,dead=runs.filter(x=>/1205|deadlock/i.test(x.out)).length;
   const rows=(await sql({query:"SET NOCOUNT ON; SELECT COUNT(*) FROM inv.reservation WHERE public_no LIKE 'C4-%'"})).out;
   log('C4',won===1&&dead===0&&rows==='1',`12 parallel connections → ${won} committed, ${12-won} rejected, ${dead} deadlocks, ${rows} reservation row(s)`);}
}finally{
  if(!KEEP){const d=await sql({db:'master',query:`ALTER DATABASE [${DB}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [${DB}];`});
    console.log(d.code===0?`Dropped ${DB}.`:`Could not drop ${DB}: ${d.out}`);}
  else console.log(`Kept ${DB} for inspection.`);
}
const failed=results.filter(x=>x.status==='failed').length;
fs.mkdirSync(path.join(LAB,'evidence'),{recursive:true});
fs.writeFileSync(path.join(LAB,'evidence/sql-spike-results.json'),JSON.stringify({run_at_utc:new Date().toISOString(),server:version,database:DB,dropped:!KEEP,
  client:'sqlcmd (ODBC 18, Windows authentication, -C trust server certificate — lab only)',totals:{tests:results.length,passed:results.length-failed,failed},results},null,1));
console.log(`\nSQL spike: ${results.length-failed}/${results.length} passed.`);
if(failed)process.exitCode=1;
