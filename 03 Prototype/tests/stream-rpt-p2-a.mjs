// [F8-RPT-P2-A] batch A guests + rooms on the report engine: reservations register, guest tariff, company business, user-wise activity, service lines,
// room-wise revenue, rooms left, allocation grid. Screen = Excel = print (threeWay), role matrix (rule V / M), date-class edges, provisional marker,
// CSV neutralisation, split-stay attribution (SF R8), goldens through tests/rpt-golden/accept.mjs.
import {threeWay} from './rpt-golden/harness.mjs';
export function register({test,M,assert}){
  const T='[F8-RPT-P2-A] ';
  const RR='rpt.guests.reservations-register',GT='rpt.guests.guest-tariff',CB='rpt.guests.company-business',UA='rpt.guests.userwise-activity',SL='rpt.guests.service-lines',
    RW='rpt.rooms.room-wise-revenue',RL='rpt.rooms.rooms-left',AG='rpt.rooms.allocation-grid';
  const ALL=[RR,GT,CB,UA,SL,RW,RL,AG];
  let f;const fy=()=>f??=M.loadScenario('FY26');
  const code=fn=>{try{fn();}catch(e){return e.code;}return 'none';};
  const run=(s,u,id,p={},o={})=>M.runReport(s,u,id,p,{all:true,...o});
  const rows=r=>r.rows.filter(x=>x.kind==='row'),sum=(r,k)=>rows(r).reduce((a,x)=>a+(x.cells[k]||0),0);

  test(T+'registered as built with the catalogue columns, money flags and gates',()=>{
    const want={[RR]:[12,true],[GT]:[10,true],[CB]:[6,true],[UA]:[7,false],[SL]:[11,true],[RW]:[7,true],[RL]:[7,false],[AG]:[34,false]};
    for(const [id,[n,money]] of Object.entries(want)){const d=M.reportDef(id);assert.ok(d,id);assert.equal(d.status,'built');assert.equal(d.columns.length,n,id);assert.equal(d.moneyFlag,money,id);assert.equal(!!d.moneyGate,money,id);}
    assert.deepEqual(M.reportDef(RR).columns.filter(c=>c.money).map(c=>c.key),['advance','quoted']);
    assert.deepEqual(M.reportDef(SL).columns.filter(c=>c.money).map(c=>c.key),['rate','amount','tax']);
    assert.ok(M.reportDef(UA).columns.find(c=>c.key==='guest').mask==='guestName');
  });

  test(T+'reservations register: arrival-date range, status / source / search filters, counts and totals; 92-day and look-ahead limits',()=>{
    const s=fy(),from='2026-10-20',to='2026-11-10',r=run(s,'owner',RR,{from,to}),d=rows(r);
    const want=s.bookings.filter(b=>b.arrival>=from&&b.arrival<=to);
    assert.equal(d.length,want.length);assert.ok(d.length>=5);
    assert.equal(r.rows.at(-1).kind,'total');assert.equal(r.rows.at(-1).cells.rooms,d.length);assert.equal(r.rows.at(-1).cells.advance,sum(r,'advance'));assert.equal(r.rows.at(-1).cells.quoted,sum(r,'quoted'));
    const b0=want[0];const q=run(s,'owner',RR,{from,to,search:b0.id});assert.ok(rows(q).some(x=>x.cells.booking===(b0.publicNo??b0.id)));
    assert.equal(rows(run(s,'owner',RR,{from,to,status:'cancelled'})).every(x=>x.cells.status==='Cancelled'),true);
    for(const x of d){const b=want.find(y=>(y.publicNo??y.id)===x.cells.booking);assert.equal(x.cells.advance,b.receipts.filter(r=>r.kind==='deposit').reduce((a,r)=>a+r.amount,0));}
    assert.equal(code(()=>run(s,'owner',RR,{from:'2026-10-01',to:'2027-01-05'})),'VALIDATION','92 days');
    assert.equal(code(()=>run(s,'owner',RR,{from:'2026-10-29',to:'2027-02-01'})),'VALIDATION','90 days ahead');
    assert.equal(code(()=>run(s,'owner',RR,{status:'maybe'})),'VALIDATION');
    threeWay(M,assert,r);
  });
  test(T+'reservations register: advance and quoted total are removed without the money gate; guest masked without billing / reservations',()=>{
    const s=fy();assert.ok(run(s,'owner',RR,{}).columns.some(c=>c.key==='quoted'));
    for(const u of ['reception','cashier','housekeeper','maint'])assert.equal(code(()=>run(s,u,RR)),u==='reception'?'FORBIDDEN':'FORBIDDEN',u);
    assert.equal(code(()=>run(s,'manager',RR)),'none');
  });

  test(T+'guest tariff: per stay net tariff = room lines less discount; rate source agreed / manual / standard; party required',()=>{
    const s=structuredClone(fy());
    const b=s.bookings.find(x=>(x.status==='Departed'||x.status==='In house')&&x.lines.some(l=>l.kind==='room'));
    const name=M.guestNameOf?.(s,b)??b.guest;
    s.guests.find(g=>g.id===b.guestId)&&(s.guests.find(g=>g.id===b.guestId).agreedRates=[{type:s.rooms.find(r=>r.id===b.room).type,rate:b.rate,setBy:'owner',at:s.clock}]);
    b.rateSource='agreed';b.lines.push({id:'t-disc',label:'Discount · test',amount:-10000,date:b.arrival,kind:'discount',taxBps:500});
    const r=run(s,'owner',GT,{party:name.slice(0,6),from:'2026-04-01',to:s.date}),x=rows(r).find(y=>y.cells.stay===(b.publicNo??b.id));
    assert.ok(x,'stay listed');
    const tariff=b.lines.filter(l=>l.kind==='room').reduce((a,l)=>a+l.amount,0);
    assert.equal(x.cells.discount,b.lines.filter(l=>l.kind==='discount').reduce((a,l)=>-(l.amount)+a,0)>0?-b.lines.filter(l=>l.kind==='discount').reduce((a,l)=>a+l.amount,0):0);
    assert.equal(x.cells.netTariff,tariff-x.cells.discount);assert.equal(x.cells.rateSource,'agreed');assert.equal(x.cells.agreed,b.rate);
    assert.equal(r.rows.at(-1).cells.netTariff,sum(r,'netTariff'));
    assert.equal(code(()=>run(s,'owner',GT,{})),'VALIDATION','party required');
    threeWay(M,assert,r);
    assert.equal(code(()=>run(s,'manager',GT,{party:'ab'})),'none');
    assert.equal(code(()=>run(s,'reception',GT,{party:'ab'})),'FORBIDDEN');
  });

  test(T+'company business: only the company account stays; a split stay counts the company folio only (R8); totals add up',()=>{
    const s=structuredClone(fy());
    const co=M.grpSlice(s).companies[0];assert.ok(co,'fixture has a company');
    const mk=(id,room,guest,account)=>({id,guest,email:'',people:1,room,arrival:'2026-10-20',departure:'2026-10-22',rate:300000,source:'Direct',status:'Departed',registered:true,version:1,notes:'',receipts:[],
      company:{name:co.name,account},lines:[{id:id+'n1',label:'Room night · 2026-10-20',amount:300000,date:'2026-10-20',kind:'room',taxBps:500},{id:id+'n2',label:'Room night · 2026-10-21',amount:300000,date:'2026-10-21',kind:'room',taxBps:500},
      {id:id+'x',label:'Laundry',amount:50000,date:'2026-10-21',kind:'service',taxBps:1800}]});
    const a=mk('TB-A','101','Test Alpha',co.id);s.bookings.push(a);
    const c=mk('TB-C','102','Test Gamma',co.id);
    c.folios=[{id:'A',payer:{kind:'guest',name:'Test Gamma'},routes:['extras'],openedAt:'',openedBy:''},{id:'B',payer:{kind:'company',name:co.name,ref:co.id},routes:['room'],openedAt:'',openedBy:''}];
    c.lines[2].folio='A';c.lines[0].folio='B';c.lines[1].folio='B';s.bookings.push(c);
    const noco=mk('TB-N','103','Test Nobody',undefined);delete noco.company;s.bookings.push(noco);
    const r=run(s,'owner',CB,{from:'2026-10-20',to:'2026-10-21',company:co.name}),x=rows(r).find(y=>y.cells.company===co.name);
    const base=run(fy(),'owner',CB,{from:'2026-10-20',to:'2026-10-21',company:co.name});const b0=rows(base).find(y=>y.cells.company===co.name);
    assert.equal(x.cells.tariff-(b0?.cells.tariff??0),1200000,'alpha 600000 + gamma company folio 600000');
    assert.equal(x.cells.other-(b0?.cells.other??0),50000,'only alpha laundry; gamma laundry is on the guest folio');
    assert.equal(x.cells.roomNights-(b0?.cells.roomNights??0),4);assert.equal(x.cells.guests-(b0?.cells.guests??0),2);
    assert.equal(x.cells.total,x.cells.tariff+x.cells.other);assert.equal(r.rows.at(-1).cells.total,sum(r,'total'));
    threeWay(M,assert,r);
    for(const u of ['reception','cashier','housekeeper'])assert.equal(code(()=>run(s,u,CB)),'FORBIDDEN',u);
  });

  test(T+'user-wise activity: check-ins and check-outs from the audit trail; the range binds both; subtotal per user; no money',()=>{
    let s=M.seed();s=M.execute(s,'owner',{type:'REGISTER',id:'HCC-1049'});s=M.execute(s,'owner',{type:'CHECKIN',id:'HCC-1049'});
    const r=run(s,'owner',UA,{from:s.date,to:s.date});
    assert.ok(rows(r).some(x=>x.cells.action==='Check-in'&&x.cells.booking==='HCC-1049'));
    const x=rows(r).find(y=>y.cells.booking==='HCC-1049');assert.equal(x.cells.room,s.bookings.find(b=>b.id==='HCC-1049').room);
    assert.ok(r.rows.some(y=>y.kind==='subtotal'&&/1 check-in/.test(y.cells.booking)));
    assert.equal(rows(run(s,'owner',UA,{from:s.date,to:s.date,action:'check-out'})).length,rows(r).filter(y=>y.cells.action==='Check-out').length);
    assert.equal(rows(run(s,'owner',UA,{from:'2026-10-01',to:'2026-10-02'})).length,0,'outside the range both actions disappear');
    assert.ok(!r.columns.some(c=>c.money));
    threeWay(M,assert,r);
    for(const u of ['reception','cashier','housekeeper','accountant'].filter(u=>u!=='accountant'))assert.equal(code(()=>run(s,u,UA)),'FORBIDDEN',u);
    assert.equal(code(()=>run(s,'manager',UA)),'none');
    assert.equal(code(()=>run(s,'owner',UA,{from:'2026-07-01',to:'2026-10-24'})),'VALIDATION');
  });

  test(T+'service lines: groups, subtotals, reversed lines left out, CSV neutralisation of a formula item',()=>{
    const s=structuredClone(fy());const b=s.bookings.find(x=>x.status==='In house');
    const d=b.arrival;
    b.lines.push({id:'sl1',label:'Laundry · shirts',amount:20000,date:d,kind:'service',taxBps:1800},{id:'sl2',label:'=HYPERLINK("x")',amount:10000,date:d,kind:'service',taxBps:1800},
      {id:'sl3',label:'Restaurant · bill 9 · 2 items',amount:50000,date:d,kind:'food',taxBps:500},{id:'sl4',label:'Spa visit',amount:30000,date:d,kind:'service',taxBps:1800},
      {id:'sl5',label:'Mini bar water',amount:5000,date:d,kind:'service',taxBps:500},{id:'sl5r',label:'Reversal',amount:-5000,date:d,kind:'adjustment',taxBps:500,reversalOf:'sl5'});
    const r=run(s,'owner',SL,{from:d,to:d}),x=id=>rows(r).find(y=>y.cells.item===id);
    assert.equal(x('Laundry · shirts').cells.group,'Laundry');assert.equal(x('Restaurant · bill 9 · 2 items').cells.group,'Food');assert.equal(x('Restaurant · bill 9 · 2 items').cells.source,'room-service POS');
    assert.equal(x('Spa visit').cells.group,'Other');assert.equal(x('Mini bar water'),undefined,'reversed line left out');
    assert.equal(x('Laundry · shirts').cells.tax,M.lineTax({amount:20000,taxBps:1800}));
    const subs=r.rows.filter(y=>y.kind==='subtotal');assert.equal(subs.reduce((a,y)=>a+y.cells.amount,0),r.rows.at(-1).cells.amount);assert.equal(r.rows.at(-1).cells.amount,sum(r,'amount'));
    assert.equal(rows(run(s,'owner',SL,{from:d,to:d,group:'laundry'})).every(y=>y.cells.group==='Laundry'),true);
    assert.equal(rows(run(s,'owner',SL,{from:d,to:d,item:'spa'})).length,1);
    assert.ok(M.toCsv(r).includes("'=HYPERLINK"),'formula neutralised in CSV');
    threeWay(M,assert,r);
    for(const u of ['reception','cashier','housekeeper','maint'])assert.equal(code(()=>run(s,u,SL)),'FORBIDDEN',u);
  });

  test(T+'room-wise revenue: totals tie to the room lines of the range; every grouping gives the same grand total; average = tariff / nights',()=>{
    const s=fy(),p={from:'2026-10-01',to:s.date};
    const room=run(s,'owner',RW,p),type=run(s,'owner',RW,{...p,groupBy:'room type'}),floor=run(s,'owner',RW,{...p,groupBy:'floor'});
    const tariff=s.bookings.flatMap(b=>b.lines).filter(l=>l.kind==='room'&&l.date>=p.from&&l.date<=p.to).reduce((a,l)=>a+l.amount,0);
    for(const r of [room,type,floor]){assert.equal(r.rows.at(-1).cells.tariff,tariff);assert.equal(r.rows.at(-1).cells.tariff,sum(r,'tariff'));assert.equal(r.rows.at(-1).cells.total,sum(r,'total'));}
    assert.equal(room.rows.at(-1).cells.nights,type.rows.at(-1).cells.nights);
    for(const x of rows(room))assert.equal(x.cells.total,x.cells.tariff+x.cells.extra+x.cells.other);
    const x=rows(room).find(y=>y.cells.nights>0);assert.equal(x.cells.avg,Math.round(x.cells.tariff/x.cells.nights));
    assert.ok(rows(type).length<=rows(room).length);
    threeWay(M,assert,room);threeWay(M,assert,type);
    assert.equal(code(()=>run(s,'owner',RW,{groupBy:'colour'})),'VALIDATION');
    assert.equal(code(()=>run(s,'owner',RW,{from:'2026-03-01',to:'2026-03-10'})),'none');assert.equal(run(s,'owner',RW,{from:'2026-03-01',to:'2026-03-10'}).about.legacy,true);
    assert.deepEqual(M.runReport(s,'owner',RW,p,{all:true}).columns.map(c=>c.key),M.reportDef(RW).columns.map(c=>c.key));
  });

  test(T+'rooms left: free = total - occupied - held - out of order; forward dates; 31-day and 90-day limits; no money',()=>{
    const s=fy(),from=s.date,to=M.addDays(s.date,6),r=run(s,'owner',RL,{from,to});
    for(const x of rows(r)){assert.equal(x.cells.free,Math.max(0,x.cells.total-x.cells.occupied-x.cells.held-x.cells.ooo),JSON.stringify(x.cells));assert.ok(x.cells.free>=0&&x.cells.occupied>=0);}
    const types=[...new Set(s.rooms.map(r=>r.type))];assert.equal(rows(r).length,7*types.length);
    const day0=rows(r).filter(x=>x.cells.date===from);assert.equal(day0.reduce((a,x)=>a+x.cells.total,0),s.rooms.filter(r=>r.status!=='retired').length);
    const t=types[0],one=rows(run(s,'owner',RL,{from,to:from,roomType:t}));assert.equal(one.length,1);
    assert.equal(one[0].cells.occupied,s.bookings.filter(b=>['Confirmed','In house'].includes(b.status)&&b.arrival<=from&&from<b.departure&&s.rooms.find(r=>r.id===b.room)?.type===t).length);
    assert.equal(code(()=>run(s,'owner',RL,{from,to:M.addDays(from,31)})),'VALIDATION');assert.equal(code(()=>run(s,'owner',RL,{from:M.addDays(from,80),to:M.addDays(from,95)})),'VALIDATION');
    assert.ok(!r.columns.some(c=>c.money));threeWay(M,assert,r);
    assert.equal(code(()=>run(s,'reception',RL)),'FORBIDDEN');
  });

  test(T+'allocation grid: one column per night, guest names, OOO, nights occupied = guest cells; 31 days; provisional on the open day',()=>{
    const s=fy(),to=s.date,from=M.addDays(to,-6),r=run(s,'owner',AG,{from,to});
    assert.equal(r.columns.length,2+7+1);assert.equal(r.columns[2].label,`${+from.slice(8)} ${['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'][+from.slice(5,7)-1]}`);
    for(const x of rows(r)){const n=r.columns.slice(2,9).filter(c=>x.cells[c.key]&&x.cells[c.key]!=='OOO').length;assert.equal(x.cells.nights,n,x.cells.room);}
    assert.equal(rows(r).length,s.rooms.filter(r=>r.status!=='retired').length);
    const occ=s.bookings.find(b=>b.status==='Departed'&&b.arrival>=from&&b.departure<=to);
    if(occ){const x=rows(r).find(y=>y.cells.room===occ.room);const i=M.nights(from,occ.arrival);assert.ok(x.cells['d'+String(i+1).padStart(2,'0')],'occupied cell');}
    assert.equal(r.about.provisional,!s.closedDates.includes(s.date));
    assert.equal(code(()=>run(s,'owner',AG,{from:M.addDays(to,-31),to})),'VALIDATION','32 days');
    assert.equal(code(()=>run(s,'owner',AG,{from,to:M.addDays(to,1)})),'VALIDATION','D-S: no future');
    threeWay(M,assert,r);
    assert.equal(rows(run(s,'owner',AG,{from,to,roomType:'nothing'})).length,0);
  });

  test(T+'role matrix: Housekeeping / Maintenance / Cashier / Restaurant refused on every report (DEC-061); Owner and Manager can open every one',()=>{
    const s=fy();
    for(const u of ['housekeeper','supervisor','maint','cashier'])for(const id of ALL)assert.equal(code(()=>run(s,u,id,id===GT?{party:'ab'}:{})),'FORBIDDEN',u+' '+id);
    for(const id of ALL)assert.equal(code(()=>run(s,'owner',id,id===GT?{party:'ab'}:{})),'none','owner '+id);
    for(const id of ALL)assert.ok(M.reportCatalogue(s,'owner').some(c=>c.id===id&&c.canExport),id);
  });
}
