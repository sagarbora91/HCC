// F12 MEAS · meas-study-b · Finance & Cost, Sales (corporate accounts), Forecast & Benchmark, Owner dashboard workbook families.
// Inputs the workbooks left as yellow cells are parameters (fixedPerMonth, variablePerRn, commission, budgetGrowthPct, compOccPct, compAdr ...). Server twin: BeyondFinance.cs.
import type {AnalyticsSelector} from './types';
import {addDays} from '../money';
import {ctxOf,fyParam,fyOf,fyStart,fyEnd,fyMonths,monthEnd,monthsBetween,prevMonth,pct1,ratio,sec,seriesSec,finish,sum,norm,extrasOf,monthLabel,OTA_FREE,type Cells,type Ctx} from './beyond-lib';

export const financeCost:AnalyticsSelector=(inp,p)=>{
  const c=ctxOf(inp),fy=fyParam(c,p),ex=extrasOf(p),from=fyStart(fy),to=fyEnd(fy),bs=c.bills.filter(b=>b.fy===fy);
  const room=sum(bs,b=>b.rev),fnb=sum(bs,b=>b.b.fnbPaise),oth=sum(bs,b=>b.b.otherPaise),disc=sum(bs,b=>b.b.discountPaise),net=room+fnb+oth-disc;
  const exps=(ex.expenses??[]).filter(e=>e.date>=from&&e.date<=to),cats=[...new Set(exps.map(e=>e.category))].sort(),cost=sum(exps,e=>e.amountPaise);
  const pl:Cells[]=[['Rooms revenue',room],['Food & beverage',fnb],['Other revenue',oth],['Less discounts',-disc],['Net revenue (before GST)',net],...cats.map(k=>['Cost: '+k,sum(exps.filter(e=>e.category===k),e=>e.amountPaise)] as Cells),['Total cost recorded',cost],['Gross operating profit',net-cost]];
  const kroom=c.bills.filter(b=>b.fy===fy),cn=sum(kroom,b=>b.cn),adr=ratio(room,cn)??0,rooms=c.roomsOn(to>c.end?c.end:to).length,fixed=Number(p.fixedPerMonth??0),vr=Number(p.variablePerRn??0);
  const avail=c.capacity(from,to),sold=c.sleptIn(from,to);
  const be:Cells[]=[[fy,adr,rooms,fixed,vr,adr>vr?pct1(fixed,(adr-vr)*rooms*30):null,pct1(sold,avail)]];
  const paid:Cells[]=cats.map(k=>{const x=exps.filter(e=>e.category===k);return [k,x.length,sum(x,e=>e.amountPaise)];});
  const rc=inp.receipts.filter(r=>r.date>=from&&r.date<=to),methods=[...new Set(rc.map(r=>r.method))].sort();
  const cash:Cells[]=[];for(const m of fyMonths(c,fy))for(const k of methods){const x=rc.filter(r=>r.date.slice(0,7)===m&&r.method===k);if(x.length)cash.push([m,k,x.length,sum(x,r=>r.amountPaise)]);}
  const gst:Cells[]=fyMonths(c,fy).map(m=>{const x=c.bills.filter(b=>b.month===m),taxable=sum(x,b=>b.gross-b.b.discountPaise),g=sum(x,b=>b.b.gstPaise);return [m,taxable,g,pct1(g,taxable),sum(x,b=>b.b.totalPaise),sum(x,b=>b.b.paidPaise)];});
  return finish('rpt.study.finance-cost','USALI P&L, break-even, paid-outs, cash & bank, GST by slab',p,[
    sec('pl','USALI P&L',[['line','Line','text'],['amount','Amount','money',true]],pl),
    sec('breakeven','Break-even',[['fy','FY','text'],['adr','ADR','money',true],['rooms','Rooms','int'],['fixed','Fixed cost per month (input)','money',true],['variable','Variable cost per room-night (input)','money',true],['bePct','Break-even occupancy %','pct'],['occ','Actual occupancy %','pct']],be),
    sec('paidouts','Paid-outs',[['category','Category','text'],['count','Entries','int'],['amount','Amount','money',true]],paid),
    sec('cash','Cash & Bank',[['month','Month','text'],['method','Method','text'],['receipts','Receipts','int'],['amount','Amount','money',true]],cash),
    sec('gst','GST Reconciliation',[['month','Month','text'],['taxable','Taxable value','money',true],['gst','GST billed','money',true],['effective','Effective GST %','pct'],['billed','Total billed','money',true],['paid','Paid','money',true]],gst),
    seriesSec('billed-by-month','bar','Total billed by month',gst.map(r=>monthLabel(String(r[0]))),gst.map(r=>r[4] as number|null),true)],c.stays.some(s=>s.legacy));
};

export const corporateAccounts:AnalyticsSelector=(inp,p)=>{
  const c=ctxOf(inp),fy=fyParam(c,p),key=(s?:string)=>norm(s).toUpperCase();
  const by=new Map<string,{name:string;fys:Set<string>;last:string;stays:number;net:number}>();
  const netOf=new Map<string,number>(),dueOf=new Map<string,number>();
  for(const b of inp.bills){netOf.set(b.stay,(netOf.get(b.stay)??0)+b.roomChargePaise+b.fnbPaise+b.otherPaise-b.discountPaise);dueOf.set(b.stay,(dueOf.get(b.stay)??0)+Math.max(0,b.totalPaise-b.paidPaise));}
  for(const s of c.stays){if(!s.company)continue;const k=key(s.company),o=by.get(k)??{name:norm(s.company),fys:new Set<string>(),last:s.from,stays:0,net:0};o.fys.add(fyOf(s.from));if(s.from>o.last)o.last=s.from;o.stays++;o.net+=netOf.get(s.id)??0;by.set(k,o);}
  const rows:Cells[]=[...by].map(([k,o]) => {const ss=c.stays.filter(s=>key(s.company)===k&&fyOf(s.from)===fy);return {k,o,ss};}).filter(x=>x.ss.length).sort((a,b)=>sum(b.ss,s=>netOf.get(s.id)??0)-sum(a.ss,s=>netOf.get(s.id)??0)||(a.k<b.k?-1:1))
    .map(({k,o,ss})=>[o.name,ss.length,sum(ss,s=>s.nights),sum(ss,s=>netOf.get(s.id)??0),ratio(sum(ss,s=>netOf.get(s.id)??0),sum(ss,s=>s.nights)),[...o.fys].sort()[0],o.last,o.fys.size,sum(c.stays.filter(s=>key(s.company)===k),s=>dueOf.get(s.id)??0)]);
  const cutoff=addDays(c.end,-365);
  const win:Cells[]=[...by.values()].filter(o=>o.last<cutoff).sort((a,b)=>b.net-a.net||(a.name<b.name?-1:1)).slice(0,Number(p.topN??50)).map(o=>[o.name,o.last,o.stays,o.net,o.fys.size]);
  const base:Cells[]=c.fys.map((f,i)=>{const act=(g:string)=>[...by.values()].filter(o=>o.fys.has(g)).map(o=>key(o.name)),a=act(f),prev=i?act(c.fys[i-1]):[],ever=c.fys.slice(0,i).flatMap(act);
    return [f,a.length,a.filter(x=>!ever.includes(x)).length,prev.filter(x=>!a.includes(x)).length];});
  return finish('rpt.study.corporate-accounts','Corporate accounts, win-back list, account base',p,[
    sec('accounts','Corporate Accounts',[['company','Company','text'],['stays','Stays (FY)','int'],['nights','Nights (FY)','int'],['net','Net billed (FY)','money',true],['adr','Net per night','money',true],['firstFy','First FY','text'],['lastStay','Last stay','date'],['years','Years active','int'],['unpaid','Unpaid on bills (lifetime)','money',true]],rows),
    sec('winback','Win-back list (no stay in the last 12 months)',[['company','Company','text'],['lastStay','Last stay','date'],['stays','Stays (lifetime)','int'],['net','Net billed (lifetime)','money',true],['years','Years active','int']],win),
    sec('base','Account base',[['fy','FY','text'],['active','Active companies','int'],['new','New companies','int'],['lost','Not back from last FY','int']],base),
    seriesSec('active-companies','bar','Active companies by FY',base.map(r=>String(r[0])),base.map(r=>r[1] as number|null))],c.stays.some(s=>s.legacy));
};

const normalFys=(c:Ctx)=>c.fys.filter(f=>fyMonths(c,f).length===12&&!c.covid(fyStart(f))&&!c.covid(fyEnd(f))&&!(fyStart(f)<c.inp.covidWindow.from&&fyEnd(f)>c.inp.covidWindow.to));
export const forecastScenarios:AnalyticsSelector=(inp,p)=>{
  const c=ctxOf(inp),fy=fyParam(c,p),nf=normalFys(c),growth=Number(p.budgetGrowthPct??10);
  const mrev=(m:string)=>sum(c.bills.filter(b=>b.month===m),b=>b.rev);
  const calM=(k:number)=>nf.map(f=>monthsBetween(fyStart(f),fyEnd(f))[k]);
  const seas=Array.from({length:12},(_,k)=>{const ms=calM(k);return {rev:sum(ms,mrev),sold:sum(ms,m=>c.sleptIn(m+'-01',monthEnd(m))),avail:sum(ms,m=>c.capacity(m+'-01',monthEnd(m))),cn:sum(ms,m=>sum(c.bills.filter(b=>b.month===m),b=>b.cn))};});
  const T={rev:sum(seas,x=>x.rev),sold:sum(seas,x=>x.sold),avail:sum(seas,x=>x.avail),cn:sum(seas,x=>x.cn)};
  const MON=['Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec','Jan','Feb','Mar'];
  const si:Cells[]=seas.map((x,k)=>[MON[k],pct1(x.rev*12,T.rev),pct1(x.sold*T.avail,x.avail*T.sold),x.cn&&T.cn?pct1(ratio(x.rev,x.cn)!,ratio(T.rev,T.cn)!):null,nf.length]);
  const months=monthsBetween(fyStart(fy),fyEnd(fy)),lastEnd=c.end.slice(0,7),actualMs=months.filter(m=>m<=lastEnd);
  const ytd=sum(actualMs,mrev),seasActual=sum(actualMs,m=>seas[months.indexOf(m)].rev);
  const prev=(m:string)=>(+m.slice(0,4)-1)+m.slice(4);
  const fc:Cells[]=months.map((m,k)=>{const act=m<=lastEnd,ly=mrev(prev(m)),sea=seasActual>0?ratio(ytd*seas[k].rev,seasActual):null;
    return [m,act?'Actual':'Forecast',act?mrev(m):null,ly,sea,sea===null?ly:ratio(ly+sea,2)];});
  const from=fyStart(fy),to=fyEnd(fy)>c.end?c.end:fyEnd(fy),sold=c.sleptIn(from,to),avail=c.capacity(from,to),adr=ratio(sum(c.bills.filter(b=>b.fy===fy),b=>b.rev),sum(c.bills.filter(b=>b.fy===fy),b=>b.cn))??0;
  const rooms=c.roomsOn(to).length,yearAvail=rooms*365,occT=avail>0?Math.floor((sold*2000+avail)/(2*avail)):0,adrD=Number(p.adrDeltaPct??5);
  const scen:Cells[]=[[-5,0],[0,0],[0,adrD],[5,adrD]].map(([dp,da])=>{const o=Math.max(0,occT+dp*10),a=ratio(adr*(100+da),100)!;return [`Occupancy ${dp>=0?'+':''}${dp} pts, ADR ${da>=0?'+':''}${da}%`,o/10,a,ratio(yearAvail*o*a,1000)];});
  const bud:Cells[]=months.map(m=>{const b=ratio(mrev(prev(m))*(100+growth),100)!,a=m<=lastEnd?mrev(m):null;return [m,b,a,a===null?null:a-b,a===null||b<=0?null:a>=b?pct1(a-b,b):-(pct1(b-a,b) as number)];});
  const co=Number(p.compOccPct),ca=Number(p.compAdr),occ=pct1(sold,avail),mpi=co>0&&occ!==null?Math.floor((occ*2000+co)/(2*co))/10:null,ari=ca>0?pct1(adr,ca):null,rgi=mpi!==null&&ari!==null?Math.floor(mpi*ari*10/100+0.5)/10:null;
  const comp:Cells[]=[[fy,occ,co>0?co:null,mpi,adr,ca>0?ca:null,ari,rgi]];
  return finish('rpt.study.forecast-scenarios','Seasonality indices, forecast (three methods), scenarios, budget vs actual, comp-set index',p,[
    sec('seasonality','Seasonality Indices',[['month','Month','text'],['revIdx','Revenue index','pct'],['occIdx','Occupancy index','pct'],['adrIdx','ADR index','pct'],['years','Normal years used','int']],si),
    sec('forecast','Forecast',[['month','Month','text'],['kind','Actual / forecast','text'],['actual','Actual room revenue','money',true],['lastYear','Same month last year','money',true],['seasonal','Seasonal method','money',true],['blend','Blend','money',true]],fc),
    sec('scenarios','Scenarios',[['scenario','Scenario','text'],['occ','Occupancy %','pct'],['adr','ADR','money',true],['revenue','Room revenue for a full year','money',true]],scen),
    sec('budget','Budget vs Actual',[['month','Month','text'],['budget','Budget (last year + growth %)','money',true],['actual','Actual','money',true],['variance','Variance','money',true],['variancePct','Variance %','pct']],bud),
    sec('compset','Comp Set Index',[['fy','FY','text'],['occ','Our occupancy %','pct'],['compOcc','Comp-set occupancy % (input)','pct'],['mpi','MPI','pct'],['adr','Our ADR','money',true],['compAdr','Comp-set ADR (input)','money',true],['ari','ARI','pct'],['rgi','RGI','pct']],comp),
    seriesSec('seasonality','line','Revenue seasonality index',si.map(r=>String(r[0])),si.map(r=>r[1] as number|null),false,true)],c.stays.some(s=>s.legacy));
};

export const ownerDashboard:AnalyticsSelector=(inp,p)=>{
  const c=ctxOf(inp),end=String(p.month??c.end.slice(0,7));let m=end;const ms:string[]=[];for(let i=0;i<12;i++){ms.unshift(m);m=prevMonth(m);}
  const firstSeen=new Map<string,string>();for(const s of c.stays){const o=firstSeen.get(s.guestKey);if(!o||s.from<o)firstSeen.set(s.guestKey,s.from);}
  const rows:Cells[]=ms.map(mo=>{const from=mo+'-01',to=monthEnd(mo),bs=c.bills.filter(b=>b.month===mo),avail=c.capacity(from,to),sold=c.sleptIn(from,to),rev=sum(bs,b=>b.rev),cn=sum(bs,b=>b.cn);
    const gs=bs.filter(b=>b.st),rep=gs.filter(b=>firstSeen.get(b.st!.guestKey)!<from),ota=sum(bs.filter(b=>b.channel!==OTA_FREE),b=>b.rev),billed=sum(bs,b=>b.b.totalPaise);
    return [mo,inp.legacyUntil>=to?'Legacy':inp.legacyUntil<from?'HCC live':'Legacy + live',pct1(sold,avail),ratio(rev,cn),ratio(rev,avail),rev,billed,sum(bs,b=>b.b.fnbPaise),bs.length,pct1(rep.length,gs.length),pct1(ota,rev),
      pct1(sum(bs,b=>b.b.discountPaise),sum(bs,b=>b.gross)),sum(inp.receipts.filter(r=>r.date.slice(0,7)===mo),r=>r.amountPaise),sum(bs,b=>Math.max(0,b.b.totalPaise-b.b.paidPaise))];});
  return finish('rpt.study.owner-dashboard','Owner 12-KPI monthly dashboard',p,[
    sec('dashboard','Owner Dashboard',[['month','Month','text'],['src','Source','text'],['occ','Occupancy %','pct'],['adr','ADR','money',true],['revpar','RevPAR','money',true],['roomRev','Room revenue','money',true],['billed','Total billed (incl. GST)','money',true],
      ['fnb','F&B billed','money',true],['bills','Bills','int'],['repeat','Repeat guests %','pct'],['otaShare','OTA share of room revenue','pct'],['discPct','Discount % of gross','pct'],['collected','Collections','money',true],['outstanding','Outstanding on the month\'s bills','money',true]],rows),
    seriesSec('occupancy','line','Occupancy % (12 months)',rows.map(r=>monthLabel(String(r[0]))),rows.map(r=>r[2] as number|null),false,true)],c.stays.some(s=>s.legacy));
};
void fyParam;
