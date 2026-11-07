// F12 MEAS · meas-rpt-daily — the nine daily-control reports: ids, switches (MEAS_OFF), money removal, empty data, one boundary test per exception rule,
// night-audit lines, the optional close block, screen = Excel = PDF text on the FY26 fixture. Names prefixed [F12-RPT-DAILY].
import {build} from 'esbuild';
import {threeWay} from './rpt-golden/harness.mjs';
// The pure exception functions are not part of the mock index (no F12-RPT-DAILY block there): bundle the lane's own entry point.
await build({entryPoints:['src/domain/reports/meas-daily/index.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/meas-daily.mjs',logLevel:'error'});
const D=await import('./.generated/meas-daily.mjs');

const IDS={
  'rpt.guests.manager-flash':'meas.daily.flash','rpt.guests.regrets-denials':'meas.daily.regrets','rpt.guests.rate-variance':'meas.daily.rate-variance',
  'rpt.rooms.room-discrepancy':'meas.daily.discrepancy','rpt.money.high-balance':'meas.daily.high-balance','rpt.money.allowances-voids':'meas.daily.adjustments',
  'rpt.guests.no-show-cancel':'meas.daily.no-show','rpt.rooms.inventory-exceptions':'meas.daily.inventory','rpt.money.cashier-variance':'meas.daily.cashier'};
const CORE=['meas.daily.flash','meas.daily.inventory','meas.daily.high-balance']; // high-balance is core while company credit (groups) is on, as in the seed

export function register({test,M,assert}){
  const T='[F12-RPT-DAILY] ';
  const off=(s,key)=>{const c=structuredClone(s);c.configX={...(c.configX??{}),values:{...(c.configX?.values??{}),[key]:false}};return c;};
  const setVal=(s,key,v)=>{const c=structuredClone(s);c.configX={...(c.configX??{}),values:{...(c.configX?.values??{}),[key]:v}};return c;};
  const code=fn=>{try{fn();}catch(e){return e.code??('UNTYPED:'+e.message);}return 'NO_ERROR';};
  const addDays=(d,n)=>M.addDays(d,n);
  const inhouse=s=>s.bookings.filter(b=>b.status==='In house');
  const exc=rows=>rows.map(r=>r.cells.exception);

  test(T+'all nine reports are registered with the MEAS status, a trailing Exception column and the right switch',()=>{
    const s=M.seed();
    for(const [id,key] of Object.entries(IDS)){
      const d=M.reportDef(id);assert.ok(d,id+' registered');assert.equal(d.status,'MEAS',id);
      assert.equal(d.columns.at(-1).key,'exception',id+' last column');
      assert.equal(M.MEAS_REPORT_KEY?.[id]??key,key,id+' key');
      assert.ok(M.reportCatalogue(s,'owner').some(x=>x.id===id),id+' in the owner catalogue');
    }
  });
  test(T+'a switched-off item hides its report and refuses MEAS_OFF; the core items stay on',()=>{
    const s=M.seed();
    for(const [id,key] of Object.entries(IDS)){
      const o=off(s,key);
      if(CORE.includes(key)){assert.ok(M.reportCatalogue(o,'owner').some(x=>x.id===id),id+' core stays');continue;}
      assert.ok(!M.reportCatalogue(o,'owner').some(x=>x.id===id),id+' hidden');
      assert.equal(code(()=>M.runReport(o,'owner',id,{})),'MEAS_OFF',id);
    }
    const pack=setVal(s,'meas.pack.daily',false);
    assert.equal(code(()=>M.runReport(pack,'owner','rpt.money.cashier-variance',{})),'MEAS_OFF');
  });
  test(T+'every report runs on the base fixture; empty captures show the Capture-needed note and zero rows, never an error',()=>{
    const s=M.seed();
    for(const id of Object.keys(IDS)){const r=M.runReport(s,'owner',id,{},{all:true});assert.ok(Array.isArray(r.rows),id);}
    const r=M.runReport(s,'owner','rpt.guests.regrets-denials',{from:'2026-01-01',to:'2026-01-31'},{all:true});
    assert.equal(r.rows.length,0);assert.match(M.reportDef('rpt.guests.regrets-denials').dateBasis,/Capture needed/);
  });
  test(T+'DEC-061: Housekeeping and Maintenance never receive a money column',()=>{
    const s=M.seed();
    for(const role of ['Housekeeper','Maintenance']){
      const u=s.users.find(x=>x.role===role);if(!u)continue;
      for(const id of Object.keys(IDS)){
        let r;try{r=M.runReport(s,u.id,id,{},{all:true});}catch(e){assert.ok(['FORBIDDEN','VALIDATION'].includes(e.code),id+' '+role+' '+e.code);continue;}
        assert.ok(!r.columns.some(c=>c.money),`${id} shows money to ${role}`);
      }
    }
  });
  test(T+'screen = Excel = PDF text for all nine reports on the FY26 fixture',()=>{
    const s=M.loadScenario('FY26');
    for(const id of Object.keys(IDS)){threeWay(M,assert,M.runReport(s,'owner',id,{},{all:true}));}
  });

  // ── 1.1 flash ──
  test(T+'flash: a KPI exactly 10 % below target is not red, one unit below is red; no target never red',()=>{
    assert.equal(D.flashRed(90,100),false);assert.equal(D.flashRed(89,100),true);assert.equal(D.flashRed(0,undefined),false);assert.equal(D.flashRed(0,null),false);
    const s=M.seed();const rows=D.flashRows(s,s.date,'budget');
    assert.equal(rows.length,12);assert.equal(rows[0].cells.kpi,'Occupancy %');
    const occ=rows[0].cells.pctActual,target=D.flashKpis(s,s.date)[0].target;
    assert.equal(rows[0].cells.exception!=='',occ*100<target*90);
  });
  test(T+'flash: occupancy target comes from meas.target.occupancy',()=>{
    const s=setVal(M.seed(),'meas.target.occupancy',99);
    assert.equal(D.flashKpis(s,s.date)[0].target,99);
  });

  // ── 1.2 regrets ──
  const turn=(n,arrival,reason='price')=>Array.from({length:n},(_,i)=>({id:'T'+i,at:'2026-10-20T10:00',by:'reception',reason,arrival,nights:1,roomType:'Classic',rooms:1,quotedRate:200000,channel:'Walk-in',leadDays:5}));
  test(T+'regrets: 4 price regrets on a night under 60 % are red, 3 are not',()=>{
    const s=structuredClone(M.seed());const night='2027-02-10';
    s.measX={turnaway:turn(4,night)};assert.ok(exc(D.regretsRows(s,{})).every(x=>x!==''),'4 red');
    s.measX={turnaway:turn(3,night)};assert.ok(exc(D.regretsRows(s,{})).every(x=>x===''),'3 not red');
    s.measX={turnaway:turn(4,night,'dates')};assert.ok(exc(D.regretsRows(s,{})).every(x=>x===''),'other reasons never red');
  });
  test(T+'regrets: a night at 60 % occupancy or more is not red even with 4 price regrets',()=>{
    const s=structuredClone(M.seed());const night=s.date;
    s.rooms=s.rooms.filter(r=>s.bookings.some(b=>(b.status==='In house'||b.status==='Confirmed')&&b.room===r.id&&b.arrival<=night&&night<b.departure));
    const o=D.occOn(s,night);assert.ok(o.sell>0&&o.occ*100>=60*o.sell,'occupancy at least 60 %');
    s.measX={turnaway:turn(4,night)};assert.ok(exc(D.regretsRows(s,{})).every(x=>x===''));
  });

  // ── 1.3 rate variance ──
  test(T+'rate variance: a rate at the agreed floor passes, one paisa below is red; a change without a reason is red',()=>{
    assert.equal(D.rateBelowFloor(10000,8500,1500),false);assert.equal(D.rateBelowFloor(10000,8499,1500),true);
    const s=structuredClone(M.seed());const b=s.bookings[0];
    const mk=(i,newRate,reason)=>({id:'C'+i,booking:b.id,room:b.room,date:s.date,at:s.clock,by:'manager',scope:'next',reason,oldRate:10000,newRate,oldPeople:1,newPeople:1,oldChildren:0,newChildren:0,effectiveFrom:s.date,reposted:[],tariff:10000,agreedRate:10000,thresholdBps:1500});
    s.stayRateX={changes:[mk(1,8500,'Loyal guest'),mk(2,8499,'Loyal guest'),mk(3,9500,'')]};
    const rows=D.rateVarianceRows(s,{from:s.date,to:s.date});
    assert.deepEqual(exc(rows).map(x=>x!==''),[false,true,true]);
    assert.equal(rows[0].cells.difference,-1500);assert.equal(rows[0].cells.differencePct,-15);
  });

  // ── 1.4 room discrepancy ──
  test(T+'room discrepancy: a sold-but-vacant mismatch of exactly 2 hours is not red, 2 hours 1 minute is red',()=>{
    const s=structuredClone(M.seed());
    const free=s.rooms.find(r=>!s.bookings.some(x=>x.status==='In house'&&x.room===r.id));const b=s.bookings.find(x=>x.status==='Confirmed');
    b.room=free.id;b.arrival=s.date;b.departure=addDays(s.date,2);
    const hhmm=s.settings.checkin,[h,m]=hhmm.split(':').map(Number);
    const at=min=>{const t=h*60+m+min;return `${s.date}T${String(Math.floor(t/60)).padStart(2,'0')}:${String(t%60).padStart(2,'0')}`;};
    const pick=min=>D.discrepancyRows(s,at(min)).find(r=>r.cells.room===free.id);
    assert.equal(pick(120).cells.exception,'');assert.notEqual(pick(121).cells.exception,'');
    assert.equal(pick(120).cells.kind,'sold-but-vacant');assert.equal(pick(121).cells.ageHours,2);
  });
  test(T+'room discrepancy: an in-house stay in an out-of-order room is OOO-with-stay',()=>{
    const s=structuredClone(M.seed());const b=inhouse(s)[0];const r=s.rooms.find(x=>x.id===b.room);
    r.ooo={from:addDays(s.date,-1),reason:'AC fault'};
    const row=D.discrepancyRows(s).find(x=>x.cells.room===b.room);assert.equal(row.cells.kind,'OOO-with-stay');
  });

  // ── 1.5 high balance ──
  test(T+'high balance: walk-in balance exactly at the threshold is not red, one paisa above is red',()=>{
    const s=structuredClone(M.seed());const b=inhouse(s)[0];
    const set=a=>{b.company=undefined;b.arrival=s.date;b.receipts=[];b.lines=[{id:'L1',label:'Room',amount:a,date:s.date,kind:'service',taxBps:0}];return D.highBalanceRows(s,500000).find(x=>x.cells.room===b.room);};
    assert.equal(set(500000).cells.exception,'');assert.notEqual(set(500001).cells.exception,'');
    assert.equal(D.highBalanceThreshold(s),500000);assert.equal(D.highBalanceThreshold(s,'1,000'),100000);
  });
  test(T+'high balance: any balance on a stay older than 2 nights is red; 2 nights is not',()=>{
    const s=structuredClone(M.seed());const b=inhouse(s)[0];
    const set=n=>{b.company=undefined;b.arrival=addDays(s.date,-n);b.receipts=[];b.lines=[{id:'L1',label:'Room',amount:100,date:s.date,kind:'service',taxBps:0}];return D.highBalanceRows(s,500000).find(x=>x.cells.room===b.room);};
    assert.equal(set(2).cells.exception,'');assert.notEqual(set(3).cells.exception,'');
  });

  // ── 1.6 allowances ──
  test(T+'allowances: a discount without an approver is red, with an approver and reason it is not',()=>{
    const s=structuredClone(M.seed());const b=s.bookings[0];
    b.lines=[...b.lines,{id:'D1',label:'Discount · loyal guest',amount:-1000,date:s.date,kind:'discount',taxBps:0},{id:'D2',label:'Discount · ',amount:-500,date:s.date,kind:'discount',taxBps:0}];
    s.approvals=[...s.approvals,{id:'AP1',booking:b.id,amount:1000,reason:'loyal guest',status:'Executed',actor:'manager',version:1,approver:'owner'}];
    const rows=D.allowanceRows(s,{from:s.date,to:s.date}).filter(r=>r.cells.bill===(b.publicNo??b.id));
    const d1=rows.find(r=>r.cells.amount===1000),d2=rows.find(r=>r.cells.amount===500);
    assert.doesNotMatch(d1.cells.exception,/No approver|No reason/);
    assert.match(d2.cells.exception,/No reason/);assert.match(d2.cells.exception,/No approver/);
  });

  // ── 1.7 no-show ──
  test(T+'no-show: rate of 5 % is not red, above 5 % is red; a waiver without an approver is red',()=>{
    assert.equal(D.noShowRateRed(5,100),false);assert.equal(D.noShowRateRed(6,100),true);assert.equal(D.noShowRateRed(0,0),false);
    const s=structuredClone(M.seed());const b=s.bookings.find(x=>x.status==='Confirmed');
    b.status='No show';b.lines=[...b.lines,{id:'R1',label:'No-show retention',amount:50000,date:b.arrival,kind:'service',taxBps:0},{id:'W1',label:'Retention waived · guest call',amount:-50000,date:b.arrival,kind:'discount',taxBps:0}];
    const row=D.noShowRows(s,{from:b.arrival,to:b.arrival}).find(r=>r.cells.booking===(b.publicNo??b.id));
    assert.equal(row.cells.retention,50000);assert.equal(row.cells.waived,50000);assert.match(row.cells.exception,/without an approver/);
  });

  // ── 1.8 inventory ──
  test(T+'inventory: out of order for 7 days is not red, 8 days without a work order is red, a work order clears it',()=>{
    const s=structuredClone(M.seed());const r=s.rooms[0];
    const run=(days,wo)=>{r.ooo={from:addDays(s.date,-days),reason:'Renovation'};s.measX={offline:{[r.id]:{reason:'OOO',...(wo?{workOrder:wo}:{})}}};return D.inventoryRows(s,s.date,s.date).find(x=>x.cells.room===r.id);};
    assert.equal(run(7).cells.exception,'');assert.notEqual(run(8).cells.exception,'');assert.equal(run(8,'WO-1').cells.exception,'');
  });
  test(T+'inventory: a complimentary stay without an approver is red (marker pending DEC-266)',()=>{
    const s=structuredClone(M.seed());const b=inhouse(s)[0];b.complimentary={reason:'Owner guest'};
    const row=D.inventoryRows(s,s.date,s.date).find(x=>x.cells.kind==='complimentary'&&x.cells.room===b.room);
    assert.match(row.cells.exception,/without an approver/);
    b.complimentary={reason:'Owner guest',approver:'owner'};
    assert.equal(D.inventoryRows(s,s.date,s.date).find(x=>x.cells.kind==='complimentary'&&x.cells.room===b.room).cells.exception,'');
  });

  // ── 1.9 cashier ──
  const closing=(date,diff,by='cashier')=>({date,closedAt:date+'T23:00',closedBy:by,cash:{opening:100000,expected:500000,counted:500000+diff,difference:diff,countedBy:by},expenses:{cash:2000,nonCash:0,vendorCredit:0,total:2000}});
  test(T+'cashier: a variance of exactly Rs 100 is not red, Rs 100.01 is red; the third variance of the month is red',()=>{
    const s=structuredClone(M.seed());
    s.closings={'2026-09-01':closing('2026-09-01',10000),'2026-09-02':closing('2026-09-02',10001),'2026-09-03':closing('2026-09-03',-100),'2026-09-04':closing('2026-09-04',0)};
    const rows=D.cashierRows(s,{from:'2026-09-01',to:'2026-09-30'});
    assert.deepEqual(exc(rows).map(x=>x!==''),[false,true,true,false]);
    assert.match(rows[2].cells.exception,/number 3/);assert.equal(rows[2].cells.mtdVariance,10000+10001-100);
    const other=D.cashierRows(s,{from:'2026-09-01',to:'2026-09-30',cashier:'nobody'});assert.equal(other.length,0);
  });
  test(T+'cashier: the limit follows meas.target.cashVariance',()=>{
    const s=setVal(structuredClone(M.seed()),'meas.target.cashVariance',500);
    s.closings={'2026-09-01':closing('2026-09-01',501)};
    assert.notEqual(D.cashierRows(s,{}).at(0).cells.exception,'');
  });

  // ── night audit and the close block ──
  test(T+'night audit lines skip an item that is off, never fail it',()=>{
    const s=M.seed();const keys=D.measNightAuditLines(s).map(l=>l.key);
    assert.ok(keys.includes('meas.daily.flash')&&keys.includes('meas.daily.inventory'),'core lines present');
    const o=off(s,'meas.daily.discrepancy');assert.ok(!D.measNightAuditLines(o).some(l=>l.key==='meas.daily.discrepancy'));
  });
  test(T+'meas.daily.blockClose: off never blocks; on blocks only while a line has an exception',()=>{
    const s=structuredClone(M.seed());
    const b=inhouse(s)[0];b.company=undefined;b.receipts=[];b.lines=[{id:'L1',label:'Room',amount:900000,date:s.date,kind:'service',taxBps:0}];
    assert.equal(D.measBlockClose(s).blocked,false);
    const on=setVal(s,'meas.daily.blockClose',true);
    const r=D.measBlockClose(on);assert.equal(r.blocked,true);assert.ok(r.reasons.some(x=>/High balance/.test(x)));
    b.lines=[];assert.equal(D.measBlockClose(setVal(s,'meas.daily.blockClose',true)).reasons.some(x=>/High balance/.test(x)),false);
    assert.equal(D.measBlockClose(setVal(off(s,'meas.daily.high-balance'),'meas.daily.blockClose',true)).reasons.some(x=>/High balance/.test(x)),false);
  });

  // ── F12-MEAS2-A · daily money follow-up (twins of Hcc.Meas.Tests MeasDailyRulesTests / MeasDailySqlTests Meas2A_*) ──
  const A2='[F12-MEAS2-A] ';
  test(A2+'M5: flash ADR divides by charged nights (room lines above zero); a re-posted night counts its new line only',()=>{
    const s=structuredClone(M.seed());const d=s.date;
    for(const b of s.bookings)b.lines=b.lines.filter(l=>!(l.kind==='room'&&l.date===d));
    const ih=inhouse(s);assert.ok(ih.length>=4,'four in-house stays');
    const night=(id,amount)=>({id,label:`Room night · ${d}`,amount,date:d,kind:'room',taxBps:0});
    ih[0].lines.push(night('M5a',200000));
    ih[1].lines.push(night('M5o',180000),{id:'M5r',label:`Reversal · Room night · ${d} · rate change`,amount:-180000,date:d,kind:'adjustment',taxBps:0,reversalOf:'M5o'},night('M5b',200000));
    ih[2].lines.push(night('M5c',200000));ih[3].lines.push(night('M5z',0));
    const adr=D.flashKpis(s,d).find(k=>k.kpi==='ADR');
    assert.equal(adr.actual,200000);                                                  // 6,000 / 3, not / rooms slept and not counting the reversed 1,800
    assert.equal(D.flashRows(s,d,'budget').find(r=>r.cells.kpi==='ADR').cells.exception,'');
    for(const b of s.bookings)b.lines=b.lines.filter(l=>!(l.kind==='room'&&l.date===d));
    assert.equal(D.flashKpis(s,d).find(k=>k.kpi==='ADR').actual,null);              // no charged night → blank, never 0 and red
    assert.match(M.reportDef('rpt.guests.manager-flash').dateBasis,/charged nights \(nights with a room charge above zero\)/);
  });
  test(A2+'L8: last year is the same calendar date; 29 Feb compares with 28 Feb',()=>{
    assert.equal(D.sameDateLastYear('2028-03-01'),'2027-03-01');assert.equal(D.sameDateLastYear('2028-02-29'),'2027-02-28');
    assert.equal(D.sameDateLastYear('2026-11-07'),'2025-11-07');
    assert.match(M.reportDef('rpt.guests.manager-flash').dateBasis,/same calendar date one year earlier/);
  });
  test(A2+'L2: flash money division is integer half away from zero (server FixedCodeEngine.DivRound)',()=>{
    assert.deepEqual([D.divRound(5,2),D.divRound(-5,2),D.divRound(7,3),D.divRound(1,0),D.divRound(1,3)],[3,-3,2,0,0]);
    const big=2**40;assert.equal(D.divRound(big,3),Math.floor(big/3));assert.equal(D.divRound(big*2+1,2),big+1);
  });
  test(A2+'L3: the walk-in threshold text keeps paise (500050 → 5,000.50)',()=>{
    assert.equal(D.rupeesText(500050),'5,000.50');assert.equal(D.rupeesText(500000),'5,000');assert.equal(D.rupeesText(100000000),'10,00,000');
    const s=structuredClone(M.seed());const b=inhouse(s)[0];b.company=undefined;b.arrival=s.date;b.receipts=[];b.lines=[{id:'L1',label:'Room',amount:500051,date:s.date,kind:'service',taxBps:0}];
    assert.equal(D.highBalanceRows(s,500050).find(x=>x.cells.room===b.room).cells.exception,'Walk-in balance above 5,000.50 rupees');
  });
  test(A2+'N4: OOO-with-stay starts at the later of the block start and the check-in; an offline mark alone starts at the check-in',()=>{
    const s=structuredClone(M.seed());const b=inhouse(s)[0];const r=s.rooms.find(x=>x.id===b.room);
    b.checkedInAt=`${addDays(s.date,-1)}T10:00`;
    s.measX={offline:{[r.id]:{reason:'OOO'}}};delete r.ooo;
    assert.equal(D.discrepancyRows(s).find(x=>x.cells.room===b.room).cells.since,`${addDays(s.date,-1)} 10:00`); // not today's midnight
    r.ooo={from:s.date,reason:'AC fault'};
    assert.equal(D.discrepancyRows(s).find(x=>x.cells.room===b.room).cells.since,`${s.date} 00:00`);
    r.ooo={from:addDays(s.date,-3),reason:'AC fault'};
    assert.equal(D.discrepancyRows(s).find(x=>x.cells.room===b.room).cells.since,`${addDays(s.date,-1)} 10:00`);
  });
  test(A2+'N3: rate re-posts are kind rate-change with the change approver; an Owner change is its own approval; only unapproved entries are red',()=>{
    const s=structuredClone(M.seed());const d=s.date;const bs=s.bookings.slice(0,4);
    const owner=s.users.find(u=>u.role==='Owner').id,manager=s.users.find(u=>u.role==='Manager').id;
    const rev=(b,id,amount)=>{b.lines.push({id:id+'o',label:`Room night · ${d}`,amount,date:d,kind:'room',taxBps:0},{id,label:`Reversal · Room night · ${d} · rate change`,amount:-amount,date:d,kind:'adjustment',taxBps:0,reversalOf:id+'o'});};
    const ch=(b,id,by,approvedBy,reason)=>({id:'STR-'+id,booking:b.id,room:b.room,date:d,at:s.clock,by,scope:'all',reason,oldRate:1,newRate:2,oldPeople:1,newPeople:1,oldChildren:0,newChildren:0,effectiveFrom:d,reposted:[{reversal:id,line:id+'n',night:d}],...(approvedBy?{approvalId:'AP-'+id,approvedBy}:{})});
    rev(bs[0],'NR1',180000);rev(bs[1],'NR2',220000);rev(bs[2],'NR3',150000);
    bs[3].lines.push({id:'NV0',label:'Laundry',amount:5000,date:d,kind:'service',taxBps:0},{id:'NV1',label:'Reversal · Laundry · guest dispute',amount:-5000,date:d,kind:'adjustment',taxBps:0,reversalOf:'NV0'});
    s.stayRateX={changes:[ch(bs[0],'NR1',manager,owner,'Corporate rate agreed'),ch(bs[1],'NR2',owner,undefined,'Owner guest rate'),ch(bs[2],'NR3',manager,undefined,'Long stay')]};
    const rows=D.allowanceRows(s,{from:d,to:d});
    const rate=rows.filter(r=>r.cells.kind==='rate-change');assert.equal(rate.length,3);
    const by=no=>rate.find(r=>r.cells.bill===(bs[no].publicNo??bs[no].id)).cells;
    assert.deepEqual([by(0).reasonCode,by(0).exception,by(0).amount],['Corporate rate agreed','',180000]);assert.notEqual(by(0).approver,'');
    assert.deepEqual([by(1).reasonCode,by(1).exception],['Owner guest rate','']);assert.equal(by(1).approver,by(1).user);
    assert.deepEqual([by(2).reasonCode,by(2).approver,by(2).exception],['Long stay','','No approver']);
    const v=rows.find(r=>r.cells.kind==='void'&&r.cells.bill===(bs[3].publicNo??bs[3].id)&&r.cells.amount===5000);assert.equal(v.cells.amount,5000);assert.match(v.cells.exception,/No approver/);
    assert.ok(D.ALLOWANCE_KINDS.includes('rate-change'));
    assert.equal(D.allowanceRows(s,{from:d,to:d,kind:'rate-change'}).length,3);
  });
}
