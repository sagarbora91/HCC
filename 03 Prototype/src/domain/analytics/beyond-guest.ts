// F12 MEAS · meas-study-b · Guest workbook family (HCC_Guest_Report): repeat guests, frequency and value, repeat list, origin, nationality, meal plan, company guests.
// Customer = guestKey (HCC customer id; phone key for legacy rows). Server twin: Hcc.Reporting/Analytics/BeyondGuest.cs.
import type {AnalyticsSelector} from './types';
import {ctxOf,fyParam,fyOf,pct1,ratio,avg1,sec,seriesSec,finish,sum,norm,extrasOf,type Cells,type Ctx} from './beyond-lib';

/** Net billed per stay (room + F&B + other - discount, before GST). */
const netOf=(c:Ctx)=>{const m=new Map<string,number>();for(const b of c.inp.bills)m.set(b.stay,(m.get(b.stay)??0)+b.roomChargePaise+b.fnbPaise+b.otherPaise-b.discountPaise);return m;};
const firstFy=(c:Ctx)=>{const m=new Map<string,string>();for(const s of c.stays){const f=fyOf(s.from),o=m.get(s.guestKey);if(!o||f<o)m.set(s.guestKey,f);}return m;};

export const guestRepeatValue:AnalyticsSelector=(inp,p)=>{
  const c=ctxOf(inp),net=netOf(c),first=firstFy(c),top=Number(p.topN??50);
  const byFy:Cells[]=c.fys.map(f=>{const ss=c.stays.filter(s=>fyOf(s.from)===f),gs=[...new Set(ss.map(s=>s.guestKey))],rep=gs.filter(g=>first.get(g)!<f),rs=ss.filter(s=>rep.includes(s.guestKey));
    return [f,gs.length,rep.length,pct1(rep.length,gs.length),ss.length,rs.length,sum(ss,s=>net.get(s.id)??0),sum(rs,s=>net.get(s.id)??0),pct1(sum(rs,s=>net.get(s.id)??0),sum(ss,s=>net.get(s.id)??0))];});
  const per=new Map<string,{n:number;nights:number;rev:number;first:string;last:string;fys:Set<string>}>();
  for(const s of c.stays){const o=per.get(s.guestKey)??{n:0,nights:0,rev:0,first:s.from,last:s.from,fys:new Set<string>()};o.n++;o.nights+=s.nights;o.rev+=net.get(s.id)??0;
    if(s.from<o.first)o.first=s.from;if(s.from>o.last)o.last=s.from;o.fys.add(fyOf(s.from));per.set(s.guestKey,o);}
  const B:[string,number,number][]=[['1 stay',1,1],['2 stays',2,2],['3-4 stays',3,4],['5-9 stays',5,9],['10+ stays',10,1e9]];
  const freq:Cells[]=B.map(([n,lo,hi])=>{const g=[...per.values()].filter(x=>x.n>=lo&&x.n<=hi),stays=sum(g,x=>x.n);return [n,g.length,stays,sum(g,x=>x.nights),sum(g,x=>x.rev),ratio(sum(g,x=>x.rev),g.length),avg1(sum(g,x=>x.nights),g.length)];});
  const list:Cells[]=[...per].filter(([,x])=>x.n>=3).sort((a,b)=>b[1].n-a[1].n||b[1].rev-a[1].rev||(a[0]<b[0]?-1:1)).slice(0,top)
    .map(([k,x])=>[k,x.n,x.first,x.last,x.nights,x.rev,[...x.fys].sort().join(', ')]);
  return finish('rpt.study.guest-repeat-value','Repeat guests, frequency, lifetime value, repeat customer list',p,[
    sec('repeat','Repeat Guests by FY',[['fy','FY','text'],['guests','Guests','int'],['repeat','Repeat guests (seen in an earlier FY)','int'],['repeatPct','Repeat %','pct'],['stays','Stays','int'],['repeatStays','Stays by repeat guests','int'],
      ['net','Net billed','money',true],['repeatNet','Net billed to repeat guests','money',true],['repeatNetPct','Repeat share of net billed','pct']],byFy),
    sec('frequency','Guest Frequency & Value',[['bucket','Frequency','text'],['guests','Guests','int'],['stays','Stays','int'],['nights','Nights','int'],['net','Net billed (lifetime)','money',true],['perGuest','Net billed per guest','money',true],['avgNights','Avg nights per guest','int']],freq),
    sec('list','Repeat Customer List',[['guest','Customer','text',false,'guestName'],['stays','Stays','int'],['first','First stay','date'],['last','Last stay','date'],['nights','Nights','int'],['net','Net billed','money',true],['fys','Financial years','text']],list),
    seriesSec('repeat-pct','line','Repeat guests % by FY',byFy.map(r=>String(r[0])),byFy.map(r=>r[3] as number|null),false,true)],c.stays.some(s=>s.legacy));
};

export const guestOriginPlan:AnalyticsSelector=(inp,p)=>{
  const c=ctxOf(inp),fy=fyParam(c,p),ss=c.stays.filter(s=>fyOf(s.from)===fy);
  const count=(xs:string[])=>{const m=new Map<string,number>();for(const x of xs)m.set(x,(m.get(x)??0)+1);return [...m].sort((a,b)=>b[1]-a[1]||(a[0]<b[0]?-1:1));};
  const st=count(ss.map(s=>norm(s.state)||'Unknown')),origin:Cells[]=st.map(([k,n])=>[fy,k,n,pct1(n,ss.length)]);
  const nat=count(ss.map(s=>norm(s.nationality)||'Not recorded')),isIn=(n:string)=>/^(india|indian|ind)$/i.test(n),
    natRows:Cells[]=nat.map(([k,n])=>[fy,k,isIn(k)||k==='Not recorded'?'No':'Yes',n,pct1(n,ss.length)]);
  const plans=['EP','CP','MAP','AP'],plan:Cells[]=c.fys.map(f=>{const x=c.stays.filter(s=>fyOf(s.from)===f);const n=(pl:string)=>x.filter(s=>(s.mealPlan??'').toUpperCase()===pl).length,known=sum(plans,pl=>n(pl));
    return [f,x.length,...plans.map(n),x.length-known,pct1(n('CP'),x.length)];});
  const co:Cells[]=c.fys.map(f=>{const x=c.stays.filter(s=>fyOf(s.from)===f&&s.company),names=new Set(x.map(s=>norm(s.company).toUpperCase()));
    const all=c.stays.filter(s=>fyOf(s.from)===f);return [f,x.length,names.size,sum(x,s=>s.nights),pct1(x.length,all.length)];});
  return finish('rpt.study.guest-origin-plan','Guest origin, nationality, meal plan, company guests',p,[
    sec('origin','Guest Origin',[['fy','FY','text'],['state','State','text'],['stays','Stays','int'],['share','Share of stays','pct']],origin),
    sec('nationality','Nationality & Form C',[['fy','FY','text'],['nationality','Nationality','text'],['formC','Form C needed','text'],['stays','Stays','int'],['share','Share of stays','pct']],natRows),
    sec('plan','Meal Plan Mix',[['fy','FY','text'],['stays','Stays','int'],['ep','EP (room only)','int'],['cp','CP (with breakfast)','int'],['map','MAP','int'],['ap','AP','int'],['none','No plan recorded','int'],['cpPct','CP share','pct']],plan),
    sec('company','Company Guests',[['fy','FY','text'],['stays','Company stays','int'],['companies','Companies','int'],['nights','Nights','int'],['share','Share of stays','pct']],co),
    seriesSec('cp-share','bar','CP share of stays by FY',plan.map(r=>String(r[0])),plan.map(r=>r[7] as number|null),false,true)],c.stays.some(s=>s.legacy));
};
void extrasOf;
