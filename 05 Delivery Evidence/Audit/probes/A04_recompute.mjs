// A04 — independent payroll recomputation (audit probe, read-only on the product).
// Run from the repo root:  $env:PATH="E:\Tools\nodejs;$env:PATH"; node "05 Delivery Evidence/Audit/probes/A04_recompute.mjs"
// Writes  05 Delivery Evidence/Audit/evidence/A04/recompute.json  (my figures + diff vs the prototype golden the server test asserts)
//         05 Delivery Evidence/Audit/evidence/A04/scenarios.json  (inputs for the kernel probe A04_kernel/Program.cs)
//
// This file deliberately does NOT import or copy product code. Rules are taken from the CA question pack (05 Delivery Evidence/
// CA_Question_Pack_2026-10-25.md, section 4, P1–P14) as an outsider would read them:
//   P1 basic 50 % of earned pay, HRA 80 % of basic, washing 10 % of basic, special = balance; PF 12 % EE / 13 % ER on basic capped ₹15,000
//   P4 ESIC 0.75 % / 3.25 % on earned pay less washing, overtime excluded, when pay ≤ ₹21,000
//   P5 coverage decided once per contribution period (Apr–Sep, Oct–Mar): earliest locked month, else contractual wage (daily × 26)
//   P6 rounding to the paisa (half away from zero) by default
//   P7 Maharashtra PT on earned pay excl. overtime; men nil ≤ 7,500 / 175 ≤ 10,000 / 200 above (300 in Feb, top slab only);
//      women nil ≤ 25,000 else 200 (300 Feb); charged whenever earned pay > 0
//   P8 overtime 1 × (monthly / days in month / 8 h), per minute; daily staff: daily / 8 h
//   P9 3 late marks = ½ day LOP; half-day = ½ day LOP; unmarked = absent; weekly offs paid
//   P11 advance: instalment (0 = full balance), never more than net pay
//   P12 daily-wage staff: leave without pay only
//   P14 F&F = final-month pay − PF/ESIC/PT − the WHOLE outstanding advance ± adjustments
// Money: BigInt paise; every component rounded once, half-up (amounts are non-negative).
import {readFileSync,writeFileSync,mkdirSync} from 'node:fs';
import path from 'node:path';

const ROOT=process.cwd();
const GOLDEN=path.join(ROOT,'08 Production/tests/Hcc.Payroll.Tests/Golden/payroll-base.golden.json');
const OUT=path.join(ROOT,'05 Delivery Evidence/Audit/evidence/A04');
mkdirSync(OUT,{recursive:true});
const g=JSON.parse(readFileSync(GOLDEN,'utf8'));

// ── exact helpers ──
const B=BigInt;
/** round(num/den) half-up for num ≥ 0, den > 0 */
const rdiv=(num,den)=>{num=B(num);den=B(den);if(num<0n||den<=0n)throw new Error('neg');return (2n*num+den)/(2n*den);};
const pct=(paise,percentTimes100)=>rdiv(B(paise)*B(percentTimes100),10000n);   // percentTimes100: 12 % → 1200
const N=x=>Number(x);
const rs=p=>(N(p)/100).toFixed(2);

// ── rates as the CA pack states them (₹ → paise) ──
const RATE={basic:5000,hraOfBasic:8000,washOfBasic:1000,pfEe:1200,pfEr:1300,pfCap:1500000n,esicEe:75,esicEr:325,esicCeil:2100000n,
  ptMaleNil:750000n,ptMaleMid:1000000n,ptMidAmt:17500n,ptStd:20000n,ptFeb:30000n,ptFemaleNil:2500000n,otTimes:1n,lateBlock:3};

// ── calendar ──
const daysIn=(y,m)=>new Date(Date.UTC(y,m,0)).getUTCDate();
const dow=d=>new Date(d+'T12:00:00Z').getUTCDay();
const datesOf=ym=>{const [y,m]=ym.split('-').map(Number);return Array.from({length:daysIn(y,m)},(_,i)=>`${ym}-${String(i+1).padStart(2,'0')}`);};

// ── attendance classification, my reading of P9/P12 ──
function counts(st,ym,att,leaves,exit){
  const c={days:datesOf(ym).length,present:0,late:0,half:0,absent:0,workedOff:0,weeklyOff:0,paidLeave:0,lopLeave:0,unmarked:0,notInService:0,otMin:0};
  for(const d of datesOf(ym)){
    if(d<st.joined||(exit&&d>exit)){c.notInService++;continue;}
    const a=att.get(d);
    const lv=leaves.filter(l=>l.staffId===st.id&&l.status==='Approved'&&l.from<=d&&l.to>=d);
    if(a){
      c.otMin+=a.otMin;
      if(a.status==='P')c.present++;else if(a.status==='L')c.late++;else if(a.status==='H')c.half++;else if(a.status==='OFF')c.workedOff++;else c.absent++;
      continue;
    }
    if(dow(d)===st.weeklyOff){c.weeklyOff++;continue;}
    if(lv.length){if(lv[0].type==='LOP')c.lopLeave++;else c.paidLeave++;continue;}   // fixture has no half-day leave in Aug/Sep/Oct for these two
    c.unmarked++;
  }
  return c;
}

// ── one payslip, my arithmetic ──
function payslip(st,ym,c,{advance=null,esicCovered=null,weeklyOffPaidForDaily=false}={}){
  const D=B(c.days),lateHalves=Math.floor(c.late/RATE.lateBlock);
  let earned,ot,payableHalves;
  if(st.payType==='monthly'){
    // P9: LOP = absent + unmarked + LOP leave + not-in-service, ½ per half-day, ½ per 3 late marks; weekly offs and paid leave are paid.
    const lopHalves=2*(c.absent+c.unmarked+c.lopLeave+c.notInService)+c.half+lateHalves;
    payableHalves=Math.max(0,2*c.days-lopHalves);
    earned=rdiv(B(st.monthly)*B(payableHalves),2n*D);
    ot=rdiv(B(st.monthly)*B(c.otMin)*RATE.otTimes,D*8n*60n);                          // P8
  }else{
    // P12: daily wage = days worked (present, late, worked off-day) + ½ per half-day − ½ per 3 lates; leave unpaid.
    // P9 says "weekly offs are paid" without carving out daily staff: variant flag.
    payableHalves=Math.max(0,2*(c.present+c.late+c.workedOff+(weeklyOffPaidForDaily?c.weeklyOff:0))+c.half-lateHalves);
    earned=rdiv(B(st.daily)*B(payableHalves),2n);
    ot=rdiv(B(st.daily)*B(c.otMin)*RATE.otTimes,8n*60n);
  }
  const structured=(st.structure??(st.payType==='daily'?'B':'S'))==='S';
  const basic=structured?pct(earned,RATE.basic):earned;
  const hra=structured?pct(basic,RATE.hraOfBasic):0n;
  const washing=structured?pct(basic,RATE.washOfBasic):0n;
  const special=earned-basic-hra-washing;
  const gross=earned+ot;
  const pfOn=st.pf??structured;                                                          // P3 default
  const pfWages=pfOn?(basic<RATE.pfCap?basic:RATE.pfCap):0n;
  const pf=pct(pfWages,RATE.pfEe),pfEr=pct(pfWages,RATE.pfEr);
  const contract=st.payType==='monthly'?B(st.monthly):B(st.daily)*26n;                   // P5
  const covered=esicCovered??(contract<=RATE.esicCeil);
  const esicWages=covered&&earned>0n?earned-washing:0n;                                  // P4: OT excluded, washing excluded
  const esic=pct(esicWages,RATE.esicEe),esicEr=pct(esicWages,RATE.esicEr);
  const feb=ym.endsWith('-02');
  let pt=0n;                                                                             // P7 on earned (no OT)
  if(earned>0n){
    if(st.gender==='F')pt=earned<=RATE.ptFemaleNil?0n:(feb?RATE.ptFeb:RATE.ptStd);
    else pt=earned<=RATE.ptMaleNil?0n:earned<=RATE.ptMaleMid?RATE.ptMidAmt:(feb?RATE.ptFeb:RATE.ptStd);
  }
  let adv=0n;
  if(advance){const due=advance.installment>0?(B(advance.installment)<B(advance.balance)?B(advance.installment):B(advance.balance)):B(advance.balance);
    const room=gross-pf-esic-pt;adv=due<room?due:(room>0n?room:0n);}                     // P11
  const deductions=pf+esic+pt+adv;
  return {payableDays:payableHalves/2,earned,basic,hra,washing,special,ot,gross,pfWages,pf,pfEr,esicWages,esic,esicEr,pt,advance:adv,deductions,net:gross-deductions,ctc:gross+pfEr+esicEr,esicCovered:covered};
}
const plain=o=>Object.fromEntries(Object.entries(o).map(([k,v])=>[k,typeof v==='bigint'?N(v):v]));

// ── fixture inputs (golden = the prototype BASE fixture, exported by the product's own script) ──
const staff=Object.fromEntries(g.staff.map(s=>[s.id,s]));
const attOf=(id,ym)=>new Map(g.attendance.filter(a=>a.staffId===id&&a.date.startsWith(ym)).map(a=>[a.date,a]));
// October 2026 marks are not in the golden file: regenerate them with the fixture's own deterministic data generator
// (03 Prototype/src/fixtures/ext/staff.ts genAttendance — a DATA generator, not a payroll rule), days 1–23, plus the 24 Oct self check-in.
function octAtt(id){
  const i=g.staff.findIndex(s=>s.id===id),st=g.staff[i],m=new Map();
  for(const d of datesOf('2026-10').filter(d=>d<'2026-10-24')){
    if(dow(d)===st.weeklyOff)continue;
    const day=+d.slice(8,10)+(+d.slice(5,7))*31,h=(i*37+day*23)%101;
    const status=h<3?'A':h<6?'H':h<12?'L':'P';
    const ot=status==='P'&&h%13===0?120:status==='P'&&h%17===0?60:0;
    m.set(d,{date:d,status,otMin:ot});
  }
  if(id==='HR-S05')m.set('2026-10-24',{date:'2026-10-24',status:'P',otMin:0});
  return m;
}

const results={probe:'A04_recompute.mjs',node:process.version,golden:path.relative(ROOT,GOLDEN),cases:[]};
const FIELDS=['payableDays','earned','basic','hra','washing','special','ot','gross','pfWages','pf','pfEr','esicWages','esic','esicEr','pt','advance','deductions','net','ctc','esicCovered'];
function diff(mine,other){const d={};for(const f of FIELDS){const a=mine[f],b=other[f];if(a!==b&&!(a==null&&b==null))d[f]={mine:a,golden:b};}return d;}

// Case 1–2: Ravi Shinde (HR-S05) monthly ₹13,500, male, structured; advance ₹3,000 @ ₹1,000/month from Aug 2026.
// Case 3–4: Imran Shaikh (HR-S10) daily ₹600, male, basic only (PF off by default), joined 1 Jun 2026.
let raviBalance=300000;
for(const ym of ['2026-08','2026-09']){
  for(const id of ['HR-S05','HR-S10']){
    const st=staff[id],c=counts(st,ym,attOf(id,ym),g.leaves,null);
    const adv=id==='HR-S05'?{installment:100000,balance:raviBalance}:null;
    const mine=plain(payslip(st,ym,c,{advance:adv}));
    const golden=g[ym==='2026-08'?'august':'september'].lines.find(l=>l.staffId===id);
    const gl={...golden};
    results.cases.push({case:`${id} ${ym}`,staff:st.name,payType:st.payType,base:st.payType==='monthly'?st.monthly:st.daily,myCounts:c,goldenCounts:golden.counts,
      mine,diffVsGolden:diff(mine,gl),...(st.payType==='daily'?{variantWeeklyOffsPaid:plain(payslip(st,ym,c,{weeklyOffPaidForDaily:true}))}:{})});
    if(id==='HR-S05')raviBalance-=mine.advance;
  }
}

// Case 5: F&F — Ravi exits on the business date 24 Oct 2026 (SetStaffActive sets ExitedOn = business date). August locked (₹1,000
// recovered), September not locked → outstanding ₹2,000; the WHOLE balance is recovered (P14), not the ₹1,000 instalment.
// Adjustments: + EL encashment (suggested: 15 EL left × ₹13,500 / 31 days, P13), − uniform ₹500.
{
  const st=staff['HR-S05'],exit='2026-10-24',ym='2026-10';
  const c=counts(st,ym,octAtt('HR-S05'),g.leaves,exit);
  const line=payslip(st,ym,c,{});                     // F&F line: no instalment; whole advance recovered separately
  const outstanding=300000n-100000n;
  const elEnc=rdiv(B(st.monthly)*15n,31n);
  const adds=elEnc,deds=50000n;
  const earnings=line.gross+adds,recoveries=line.pf+line.esic+line.pt+outstanding+deds;
  results.cases.push({case:'HR-S05 F&F exit 2026-10-24',myCounts:c,line:plain(line),outstandingAdvance:N(outstanding),elEncashment:N(elEnc),
    adjustments:[{label:'EL encashment',kind:'add',amount:N(elEnc)},{label:'Uniform',kind:'ded',amount:N(deds)}],
    earnings:N(earnings),recoveries:N(recoveries),net:N(earnings-recoveries)});
}

// Edge scenarios (counts given directly, so only the money arithmetic is compared with the kernel).
const mon=(id,monthly,gender='M',extra={})=>({id,name:id,payType:'monthly',monthly,daily:0,gender,weeklyOff:0,joined:'2025-04-01',...extra});
const full=(ym,o={})=>{const [y,m]=ym.split('-').map(Number);const d=daysIn(y,m);return {days:d,present:d-(o.absent??0)-(o.late??0)-(o.half??0),late:0,half:0,absent:0,workedOff:0,weeklyOff:0,paidLeave:0,lopLeave:0,unmarked:0,notInService:0,otMin:0,...o};};
const edges=[
  ['E1 Feb PT top slab, man ₹18,000',mon('E1',1800000),'2027-02',full('2027-02')],
  ['E2 Feb PT middle slab, man ₹9,000 (no Feb bump)',mon('E2',900000),'2027-02',full('2027-02')],
  ['E3 PT woman ₹25,000 (nil)',mon('E3',2500000,'F'),'2026-09',full('2026-09')],
  ['E4 PT woman ₹25,000.01 (₹200)',mon('E4',2500001,'F'),'2026-09',full('2026-09')],
  ['E5 ESIC ceiling ₹21,000 exactly (covered)',mon('E5',2100000),'2026-09',full('2026-09')],
  ['E6 ESIC ₹22,000 with 2 LOP days (period rule: contract wage > ceiling → not covered)',mon('E6',2200000),'2026-09',full('2026-09',{absent:2})],
  ['E7 ESIC raised to ₹23,000 in Dec after October locked covered (covered to March)',mon('E7',2300000),'2026-12',full('2026-12'),{esicCovered:true}],
  ['E8 Overtime 600 min on ₹12,000, 30-day month',mon('E8',1200000),'2026-09',full('2026-09',{otMin:600})],
  ['E9 Three late marks = ½ day',mon('E9',3000000),'2026-09',full('2026-09',{late:3})],
  ['E10 Advance instalment ₹5,000 larger than net (₹10,000, 25 days absent)',mon('E10',1000000),'2026-09',full('2026-09',{absent:25}),{advance:{installment:500000,balance:500000}}],
  ['E11 Advance instalment 0 = full balance ₹4,000',mon('E11',1500000),'2026-09',full('2026-09'),{advance:{installment:0,balance:400000}}],
  ['E12 Daily ₹850 (×26 = ₹22,100 > ceiling → not covered although month earns ₹17,000)',{id:'E12',name:'E12',payType:'daily',monthly:0,daily:85000,gender:'M',weeklyOff:0,joined:'2025-04-01'},'2026-09',{...full('2026-09'),present:20}],
  ['E13 PF cap: basic ₹20,000 (₹40,000 pay) → PF on ₹15,000',mon('E13',4000000),'2026-09',full('2026-09')],
  ['E14 Part-month joiner from 21 Sep, man ₹9,000 (earned ₹3,000 → PT nil)',mon('E14',900000,'M',{joined:'2026-09-21'}),'2026-09',{...full('2026-09'),present:10,notInService:20}],
];
results.edges=edges.map(([label,st,ym,c,opt])=>({label,st,ym,counts:c,opt:opt??{},mine:plain(payslip(st,ym,c,opt??{}))}));

writeFileSync(path.join(OUT,'recompute.json'),JSON.stringify(results,null,1)+'\n');
// scenarios for the kernel probe (same inputs, kernel arithmetic)
writeFileSync(path.join(OUT,'scenarios.json'),JSON.stringify({
  fnf:{staffCode:'HR-S05',exit:'2026-10-24',octAttendance:[...octAtt('HR-S05').values()],advance:{amount:300000,installment:100000,recovered:100000,date:'2026-08-10'},
    adjustments:results.cases.at(-1).adjustments},
  edges:results.edges.map(e=>({label:e.label,payType:e.st.payType,monthly:e.st.monthly,daily:e.st.daily,gender:e.st.gender,joined:e.st.joined,ym:e.ym,counts:e.counts,
    advance:e.opt.advance??null,esicCovered:e.opt.esicCovered??null})),
},null,1)+'\n');

// console summary
for(const c of results.cases){
  if(c.mine)console.log(c.case.padEnd(18),'earned',rs(c.mine.earned),'gross',rs(c.mine.gross),'PF',rs(c.mine.pf),'ESIC',rs(c.mine.esic),'PT',rs(c.mine.pt),'adv',rs(c.mine.advance),'net',rs(c.mine.net),
    Object.keys(c.diffVsGolden).length?'DIFF '+JSON.stringify(c.diffVsGolden):'= golden');
  else console.log(c.case,'earnings',rs(c.earnings),'recoveries',rs(c.recoveries),'net',rs(c.net));
}
for(const e of results.edges)console.log(e.label.slice(0,60).padEnd(60),'earned',rs(e.mine.earned),'PF',rs(e.mine.pf),'ESIC',rs(e.mine.esic),'PT',rs(e.mine.pt),'adv',rs(e.mine.advance),'net',rs(e.mine.net),'cov',e.mine.esicCovered);
