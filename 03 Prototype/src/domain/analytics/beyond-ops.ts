// F12 MEAS · meas-study-b · Operations & Housekeeping, F&B / Halls / Ancillary, Compliance & Risk workbook families.
// Day-level only: AnInput carries dates, not timestamps, so turnaround is measured in days between stays; the clock-time sheets use optional BStay.inTime / outTime.
// Server twin: Hcc.Reporting/Analytics/BeyondOps.cs.
import type {AnalyticsSelector} from './types';
import {addDays} from '../money';
import {ctxOf,fyParam,fyOf,fyStart,fyEnd,fyMonths,monthEnd,pct1,ratio,avg1,sec,seriesSec,finish,sum,dow,DOW,extrasOf,type Cells} from './beyond-lib';

export const housekeepingOperations:AnalyticsSelector=(inp,p)=>{
  const c=ctxOf(inp),fy=fyParam(c,p),from=fyStart(fy)<c.dataStart?c.dataStart:fyStart(fy),to=fyEnd(fy)>c.end?c.end:fyEnd(fy);
  const byRoom=new Map<string,typeof c.stays>();for(const s of c.stays)(byRoom.get(s.room)??byRoom.set(s.room,[]).get(s.room)!).push(s);
  const gaps=new Map<string,number[]>();
  for(const [,xs] of byRoom){xs.sort((a,b)=>a.from<b.from?-1:a.from>b.from?1:0);for(let i=1;i<xs.length;i++){const f=fyOf(xs[i].from),g=Math.round((Date.parse(xs[i].from)-Date.parse(xs[i-1].to))/864e5);if(g<0)continue;(gaps.get(f)??gaps.set(f,[]).get(f)!).push(g);}}
  const turn:Cells[]=c.fys.map(f=>{const g=gaps.get(f)??[],same=g.filter(x=>x===0).length;return [f,g.length,same,pct1(same,g.length),g.filter(x=>x===1).length,avg1(sum(g,x=>x),g.length)];});
  const idle:Cells[]=[];
  for(const r of inp.rooms){const sold=new Set(c.slept.filter(s=>s.room===r.id&&s.date>=from&&s.date<=to).map(s=>s.date));let run=0,best=0,unsold=0,inSvc=0;
    for(let d=from;d<=to;d=addDays(d,1)){if(r.inServiceFrom>d||(r.inServiceTo&&d>r.inServiceTo))continue;inSvc++;if(sold.has(d))run=0;else{run++;unsold++;if(run>best)best=run;}}
    if(inSvc)idle.push([r.id,c.rtype.get(r.id)??'',fy,inSvc,inSvc-unsold,unsold,best]);}
  const stdIn=String(p.stdIn??'12:00'),stdOut=String(p.stdOut??'11:00'),ss=c.stays.filter(s=>fyOf(s.from)===fy);
  const withIn=ss.filter(s=>s.inTime),withOut=ss.filter(s=>s.outTime);
  const inout:Cells[]=[[fy,ss.length,withIn.length,withIn.filter(s=>s.inTime!<stdIn).length,pct1(withIn.filter(s=>s.inTime!<stdIn).length,withIn.length),
    withOut.length,withOut.filter(s=>s.outTime!>stdOut).length,pct1(withOut.filter(s=>s.outTime!>stdOut).length,withOut.length)]];
  const arr=new Map<string,number>(),dep=new Map<string,number>();for(const s of ss){arr.set(s.from,(arr.get(s.from)??0)+1);dep.set(s.to,(dep.get(s.to)??0)+1);}
  const load:Cells[]=DOW.map((n,i)=>{let days=0,a=0,d2=0,ma=0,md=0;for(let d=from;d<=to;d=addDays(d,1))if(dow(d)===i){days++;const x=arr.get(d)??0,y=dep.get(d)??0;a+=x;d2+=y;if(x>ma)ma=x;if(y>md)md=y;}
    return [n,days,a,avg1(a,days),ma,d2,avg1(d2,days),md];});
  return finish('rpt.study.housekeeping-operations','Turnaround, idle streaks, check-in/out vs standard, daily load',p,[
    sec('turnaround','Turnaround',[['fy','FY','text'],['turnovers','Room turnovers','int'],['sameDay','Same-day turnovers','int'],['sameDayPct','Same-day %','pct'],['nextDay','Next-day turnovers','int'],['avgGap','Average gap (days)','int']],turn),
    sec('idle','Idle Streaks',[['room','Room','text'],['type','Type','text'],['fy','FY','text'],['inService','Days in service','int'],['soldDays','Nights sold','int'],['unsold','Unsold nights','int'],['longest','Longest unsold run (days)','int']],idle),
    sec('inout','Check-in-out vs Standard',[['fy','FY','text'],['stays','Stays','int'],['withIn','With check-in time','int'],['early','Before standard check-in','int'],['earlyPct','Early %','pct'],['withOut','With check-out time','int'],['late','After standard check-out','int'],['latePct','Late %','pct']],inout),
    sec('load','Daily Load',[['day','Night of week','text'],['days','Days','int'],['arrivals','Arrivals','int'],['avgArr','Avg arrivals / day','int'],['maxArr','Busiest arrival day','int'],['departures','Departures','int'],['avgDep','Avg departures / day','int'],['maxDep','Busiest departure day','int']],load),
    seriesSec('avg-arrivals','bar','Average arrivals per day by night of week',DOW,load.map(r=>r[3] as number|null))],c.stays.some(s=>s.legacy));
};

export const fnbHallsAncillary:AnalyticsSelector=(inp,p)=>{
  const c=ctxOf(inp),fy=fyParam(c,p),ex=extrasOf(p);
  const mo:Cells[]=fyMonths(c,fy).map(m=>{const bs=c.bills.filter(b=>b.month===m),fb=sum(bs,b=>b.b.fnbPaise),rev=sum(bs,b=>b.rev);return [m,bs.length,fb,ratio(fb,bs.length),pct1(fb,rev)];});
  const byFy:Cells[]=c.fys.map(f=>{const bs=c.bills.filter(b=>b.fy===f),fb=sum(bs,b=>b.b.fnbPaise),rev=sum(bs,b=>b.rev);return [f,bs.length,bs.filter(b=>b.b.fnbPaise>0).length,fb,ratio(fb,bs.length),pct1(fb,rev)];});
  const cov:Cells[]=c.fys.map(f=>{const cp=c.stays.filter(s=>fyOf(s.from)===f&&(s.mealPlan??'').toUpperCase()==='CP');const n=sum(cp,s=>s.nights*(s.people??1));return [f,cp.length,n,sum(cp,s=>s.nights)];});
  const hall:Cells[]=c.fys.map(f=>{const h=(ex.hallEvents??[]).filter(x=>fyOf(x.date)===f);return [f,h.length,sum(h,x=>x.days),sum(h,x=>x.revenuePaise),ratio(sum(h,x=>x.revenuePaise),h.length)];});
  const anc:Cells[]=c.fys.map(f=>{const bs=c.bills.filter(b=>b.fy===f),o=sum(bs,b=>b.b.otherPaise);return [f,bs.length,o,ratio(o,bs.length),pct1(o,sum(bs,b=>b.rev))];});
  return finish('rpt.study.fnb-halls-ancillary','F&B on bills, breakfast covers, hall utilisation, ancillary',p,[
    sec('fnb-month','F&B on Bills by Month',[['month','Month','text'],['bills','Bills','int'],['fnb','F&B billed','money',true],['perBill','F&B per bill','money',true],['pctRoom','F&B % of room revenue','pct']],mo),
    sec('fnb-fy','F&B by FY',[['fy','FY','text'],['bills','Bills','int'],['withFnb','Bills with F&B','int'],['fnb','F&B billed','money',true],['perBill','F&B per bill','money',true],['pctRoom','F&B % of room revenue','pct']],byFy),
    sec('covers','Breakfast Covers',[['fy','FY','text'],['stays','CP stays','int'],['covers','Covers (people x nights)','int'],['nights','CP room-nights','int']],cov),
    sec('halls','Hall Utilisation',[['fy','FY','text'],['events','Events','int'],['days','Event days','int'],['revenue','Hall revenue','money',true],['perEvent','Revenue per event','money',true]],hall),
    sec('ancillary','Ancillary per Stay',[['fy','FY','text'],['bills','Bills','int'],['other','Other charges (extra bed, laundry, other)','money',true],['perBill','Other per bill','money',true],['pctRoom','Other % of room revenue','pct']],anc),
    seriesSec('fnb-per-bill','bar','F&B per bill by FY',byFy.map(r=>String(r[0])),byFy.map(r=>r[4] as number|null),true)],c.stays.some(s=>s.legacy));
};

const RISKS:[string,number,number,string,string][]=[
  ['Revenue leakage through free-text rate edits and zero edits',4,3,'Rate card, reason codes, approver for cuts > 10% and for complimentary stays, audit log','Open'],
  ['Walk-in credit becoming bad debt',4,4,'No check-out with balance; credit only against company master with limit; monthly ageing','Open'],
  ['Cash handling and unreferenced receipts',3,3,'Receipt always linked to a bill; daily cash count vs HCC; UPI / bank import','Open'],
  ['GST slab or 0% bills mis-stated in returns',3,4,'Tax code per line; monthly GST reconciliation; CA review of 0% bills','Open'],
  ['Guest ID not captured',3,4,'Mandatory ID at check-in; police register export','Open'],
  ['Rooms out of order unrecorded',4,2,'Daily room-status log; maintenance tickets','Open'],
  ['OTA dependence and commission cost',3,3,'Channel master with commission; direct-booking offer; enquiry log','Open'],
  ['Single-point data loss',3,5,'HCC server with backups; legacy files archived read-only','Mitigated by HCC'],
  ['Licence lapse (fire NOC, FSSAI, lodging licence)',2,5,'Licence calendar with 60 / 30-day alerts','Open'],
  ['Foreign guest without Form C',1,4,'Nationality list + Form C task','Open'],
  ['Key / access control (physical keys)',3,3,'KEYS module (key-card) with issue / return log','In progress'],
  ['Staff advances unreconciled (paid-outs)',3,2,'Payroll module; advances ledger per employee','Open']];
export const complianceRisk:AnalyticsSelector=(inp,p)=>{
  const c=ctxOf(inp),fy=fyParam(c,p),ex=extrasOf(p),ss=c.stays.filter(s=>fyOf(s.from)===fy);
  const cap:Cells[]=c.fys.map(f=>{const x=c.stays.filter(s=>fyOf(s.from)===f),id=x.filter(s=>s.idCaptured).length,ph=x.filter(s=>s.phoneCaptured).length,pu=x.filter(s=>s.purpose).length;
    return [f,x.length,id,pct1(id,x.length),ph,pct1(ph,x.length),pu,pct1(pu,x.length)];});
  const foreign=ss.filter(s=>s.nationality&&!/^(india|indian|ind)$/i.test(s.nationality)).length;
  const audit:Cells[]=(ex.audit??[]).slice(0,Number(p.topN??20)).map(a=>[a.action,a.count]);
  const disc=c.bills.filter(b=>b.fy===fy&&b.b.discountPaise>0).length,zero=c.bills.filter(b=>b.fy===fy&&b.b.totalPaise===0).length;
  const risk:Cells[]=RISKS.map(r=>[r[0],r[1],r[2],r[1]*r[2],r[3],r[4]]);
  const lic:Cells[]=(ex.licences??[]).map(l=>[l.name,l.validTo??null,l.validTo?Math.round((Date.parse(l.validTo)-Date.parse(c.end))/864e5):null]);
  return finish('rpt.study.compliance-risk','Capture rates, audit-trail counts, risk register, licence calendar',p,[
    sec('capture','Capture Rates',[['fy','FY','text'],['stays','Stays','int'],['id','With ID record','int'],['idPct','ID %','pct'],['phone','With phone','int'],['phonePct','Phone %','pct'],['purpose','With purpose of visit','int'],['purposePct','Purpose %','pct']],cap),
    sec('audit','Audit Trail',[['action','Action recorded','text'],['count','Count','int']],[...audit,['Discounted bills (selected FY)',disc],['Zero-value bills (selected FY)',zero],['Foreign-national stays (selected FY, Form C)',foreign]]),
    sec('risk','Risk Register',[['risk','Risk','text'],['likelihood','Likelihood (1-5)','int'],['impact','Impact (1-5)','int'],['rating','Rating','int'],['control','Control in HCC','text'],['status','Status','text']],risk),
    sec('licences','Licence Calendar',[['name','Licence','text'],['validTo','Valid to','date'],['days','Days left','int']],lic),
    seriesSec('id-capture','line','ID capture % by FY',cap.map(r=>String(r[0])),cap.map(r=>r[3] as number|null),false,true)],c.stays.some(s=>s.legacy));
};
void monthEnd;
