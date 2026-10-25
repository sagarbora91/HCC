// Regenerates payroll-base.golden.json from the prototype BASE fixture (03 Prototype, MAIN checkout, read-only):
// 12 fictional staff, attendance for Aug + Sep 2026, leave, the open advance, the locked August run and the September run
// computed after it (ESIC contribution-period rule F-07, weekly off inside leave F-01). Run from 08 Production:
//   node tests/Hcc.Payroll.Tests/Golden/export-payroll-golden.mjs ["<path to 03 Prototype>"]
import {createRequire} from 'node:module';
import {writeFile,mkdtemp} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import path from 'node:path';
import {fileURLToPath,pathToFileURL} from 'node:url';
const here=path.dirname(fileURLToPath(import.meta.url));
const proto=process.argv[2]??'E:/Projects/HCC/Hotel Control Centre/03 Prototype';
const {build}=createRequire(path.join(proto,'package.json'))('esbuild');
const out=path.join(await mkdtemp(path.join(tmpdir(),'hcc-payroll-golden-')),'mock.mjs');
await build({entryPoints:[path.join(proto,'src/services/mock/index.ts')],bundle:true,platform:'node',format:'esm',outfile:out,logLevel:'error'});
const M=await import(pathToFileURL(out).href);
const s=M.seed(),x=M.hrSlice(s);
// The fixture drops the raw August marks after locking; regenerate them with the fixture's own deterministic pattern.
const SHIFT_START={M:'07:00',E:'15:00',N:'23:00',G:'09:00'};
const aug={};
x.staff.forEach((st,i)=>M.hrMonthDates('2026-08').forEach(d=>{
  if(M.hrShiftFor(s,st,d)==='OFF')return;
  const day=+d.slice(8,10)+(+d.slice(5,7))*31,h=(i*37+day*23)%101;
  const status=h<3?'A':h<6?'H':h<12?'L':'P';
  const ot=status==='P'&&h%13===0?120:status==='P'&&h%17===0?60:0;
  aug[d+'|'+st.id]={status,otMin:ot,at:d+'T'+(status==='L'?SHIFT_START[st.defaultShift].replace(':00',':25'):SHIFT_START[st.defaultShift])};
}));
const att=[];
for(const [k,v] of [...Object.entries(aug),...Object.entries(x.att)]){const [date,staffId]=k.split('|');if(date<'2026-10-01')att.push({date,staffId,status:v.status,otMin:v.otMin||0});}
att.sort((a,b)=>a.date.localeCompare(b.date)||a.staffId.localeCompare(b.staffId));
const line=l=>({staffId:l.staffId,counts:l.counts,latePenaltyHalf:l.latePenaltyHalf,payableDays:l.payableDays,earned:l.earned,basic:l.basic,hra:l.hra,washing:l.washing,special:l.special,
  ot:l.ot,gross:l.gross,pfWages:l.pfWages,pf:l.pf,pfEr:l.pfEr,esicWages:l.esicWages,esic:l.esic,esicEr:l.esicEr,pt:l.pt,advance:l.advance,deductions:l.deductions,net:l.net,ctc:l.ctc,esicCovered:l.esicCovered??null});
const augRun=x.runs.find(r=>r.month==='2026-08');
// Recompute August from the regenerated marks: must equal the fixture's locked run.
const s2=structuredClone(s);s2.staffX.runs=[];s2.staffX.advances.forEach(a=>{a.recovered=0;a.recoveries=[];});Object.assign(s2.staffX.att,Object.fromEntries(Object.entries(aug).map(([k,v])=>[k,{...v,by:'manager'}])));
const augCheck=M.hrComputeRun(s2,'2026-08');
if(JSON.stringify(augCheck.lines)!==JSON.stringify(augRun.lines))throw new Error('Regenerated August attendance does not reproduce the locked fixture run.');
const sep=M.hrComputeRun(s,'2026-09');
const tot=r=>M.hrRunTotals(r);
const golden={
  source:'03 Prototype BASE fixture (src/fixtures/ext/staff.ts) + src/domain/streams/staff.ts after the W05-B review fixes; fictional data',
  businessDate:s.date,
  rates:M.hrDefaultRates(),
  leavePolicy:M.hrDefaultLeavePolicy(),
  depts:x.depts,
  staff:x.staff.map(st=>({id:st.id,name:st.name,dept:st.dept,designation:st.designation,userId:st.userId??null,payType:st.payType,monthly:st.monthly,daily:st.daily,
    defaultShift:st.defaultShift,weeklyOff:st.weeklyOff,joined:st.joined,phone:st.phone,gender:st.gender??'M',bankName:st.bankName,accountNo:st.accountNo,ifsc:st.ifsc})),
  leaves:x.leaves.map(l=>({id:l.id,staffId:l.staffId,type:l.type,from:l.from,to:l.to,half:l.half??null,days:l.days,status:l.status})),
  advances:x.advances.map(a=>({id:a.id,staffId:a.staffId,amount:a.amount,installment:a.installment,date:a.date,fromMonth:a.fromMonth??null})),
  attendance:att,
  august:{lines:augRun.lines.map(line),totals:tot(augRun),warnings:augRun.warnings},
  september:{lines:sep.lines.map(line),totals:tot({lines:sep.lines}),warnings:sep.warnings},
};
await writeFile(path.join(here,'payroll-base.golden.json'),JSON.stringify(golden,null,1)+'\n');
console.log('August net',tot(augRun).net,'September net',tot({lines:sep.lines}).net,'attendance rows',att.length);
