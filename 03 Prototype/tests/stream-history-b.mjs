// Phase B tests: FY26 daily operations, closing records, cash continuity, reports over months, accounting, housekeeping history.
const FROM='2026-04-01',TO='2026-10-23';
const addDays=(d,n)=>new Date(Date.parse(d+'T12:00:00Z')+n*86400000).toISOString().slice(0,10);
const range=(a,b)=>{const out=[];for(let d=a;d<=b;d=addDays(d,1))out.push(d);return out;};

export function register({test,M,assert}){
  const {loadScenario,baseFixture,execute,closingSummary,drawer,reportPack,historyMonths,accLiveVouchers,accExceptions,accReconcile,accBalanced,accTotals,
    accTrialBalance,expenseCounts,metrics,totals,SHIFT_MANAGER_DIFF_LIMIT,EXPENSE_MANAGER_LIMIT}=M;
  const s=loadScenario('FY26'),days=range(FROM,TO),rec=d=>s.closings[d];
  const hist=s.bookings.filter(b=>b.id.startsWith('HCC-H'));

  test('[HIST-B] every history day is closed with exactly one closing record; 24 Oct stays open',()=>{
    assert.deepEqual(s.closedDates,days);assert.equal(Object.keys(s.closings).length,days.length);
    for(const d of days){assert.equal(rec(d).date,d);assert.equal(rec(d).closedBy,'manager');}
    assert.equal(s.closings['2026-10-24'],undefined);assert.equal(s.date,'2026-10-24');assert.equal(s.shiftOpen,true);assert.equal(s.shift,undefined);
  });
  test('[HIST-B] stored closing record equals the recomputed closingSummary for every day (full-state recompute)',()=>{
    for(const d of days){
      const c=rec(d),ap=s.approvals.find(a=>a.kind==='cash-difference'&&a.target===d);
      const bookings=s.bookings.map(b=>({...b,lines:b.lines.filter(l=>l.date<=d),receipts:b.receipts.filter(r=>r.date<=d)}));
      const ws0={...s,date:d,clock:d+'T20:00',openingCash:c.cash.opening,shiftOpen:true,shiftClose:undefined,shift:undefined,closeProgress:undefined,closings:undefined,bookings,expenses:s.expenses.filter(e=>e.date===d)};
      const expected=drawer(ws0);assert.equal(expected,c.cash.expected,d);
      const shift=ap?{date:d,status:'Closed',expected,counted:c.cash.counted,reason:ap.reason,submittedBy:'cashier',approval:ap.id,decidedBy:'manager'}:undefined;
      const re=closingSummary({...ws0,clock:d+'T23:30',shiftOpen:false,shiftClose:{expected,counted:c.cash.counted},shift},'manager');
      re.nightAudit={stays:re.occupancy.occupied,resumed:false};re.cash.banked=c.cash.banked;
      assert.deepEqual(re,c,'closing record '+d);
    }
  });
  test('[HIST-B] monthly totals equal the sum of the daily records and the report pack for the month',()=>{
    for(const [from,to] of [['2026-04-01','2026-04-30'],['2026-05-01','2026-05-31'],['2026-06-01','2026-06-30'],['2026-07-01','2026-07-31'],['2026-08-01','2026-08-31'],['2026-09-01','2026-09-30'],['2026-10-01','2026-10-23']]){
      const ds=range(from,to),p=reportPack(s,from,to),v=(t,id)=>p.tabs.find(x=>x.id===t).metrics.find(m=>m.id===id).value;
      const sum=f=>ds.reduce((a,d)=>a+f(rec(d)),0);
      assert.equal(v('revenue','net'),sum(c=>c.revenue.net),from);assert.equal(v('revenue','tax'),sum(c=>c.revenue.tax),from);
      assert.equal(v('receipts','net'),sum(c=>c.receipts.net),from);assert.equal(v('expenses','total'),sum(c=>c.expenses.total),from);
      assert.equal(v('occupancy','occupied'),sum(c=>c.occupancy.occupied),from);assert.equal(v('occupancy','sellable'),sum(c=>c.occupancy.sellable),from);
      assert.equal(v('occupancy','room-revenue'),sum(c=>c.occupancy.roomRevenue),from);
    }
  });
  test('[HIST-B] every report metric over the whole FY equals the sum of its drill-down rows',()=>{
    const p=reportPack(s,FROM,TO);
    for(const t of p.tabs)for(const m of t.metrics)assert.equal(m.rows.reduce((a,r)=>a+r.amount,0),m.value,t.id+'/'+m.id);
    const daily=p.tabs.find(t=>t.id==='daily').table;assert.equal(daily.rows.length,206);
    const cash=p.tabs.find(t=>t.id==='expenses').table;assert.equal(cash.rows.length,206);
    for(const r of cash.rows)assert.equal(r[1],'Closed');
  });
  test('[HIST-B] cash drawer continuity: opening = previous counted − banked; float ₹5,000; joins up with BASE on 24 Oct',()=>{
    assert.equal(rec(FROM).cash.opening,500000);
    for(let i=1;i<days.length;i++){const a=rec(days[i-1]).cash,b=rec(days[i]).cash;assert.equal(b.opening,a.counted-a.banked,days[i]);assert.ok(a.banked>=0&&a.banked===Math.max(0,a.counted-500000));}
    const last=rec(TO).cash;assert.equal(last.counted-last.banked,s.openingCash);assert.equal(s.openingCash,500000);
    for(const d of days)assert.ok(rec(d).cash.expected>=0,d);
  });
  test('[HIST-B] cash differences: ~8 % of days, ±₹10–₹200, each with a reason and an approved manager decision',()=>{
    const diffs=days.filter(d=>rec(d).cash.difference!==0);
    assert.ok(diffs.length>=6&&diffs.length<=30,'differences '+diffs.length);
    for(const d of days){const c=rec(d).cash,ap=s.approvals.find(a=>a.kind==='cash-difference'&&a.target===d);
      if(c.difference===0){assert.equal(ap,undefined);continue;}
      assert.ok(Math.abs(c.difference)>=1000&&Math.abs(c.difference)<=SHIFT_MANAGER_DIFF_LIMIT,d);assert.ok(ap&&ap.status==='Approved'&&ap.approver==='manager'&&ap.amount===Math.abs(c.difference)&&ap.reason,d);
      assert.equal(c.reason,ap.reason);assert.equal(c.approvedBy,'manager');assert.equal(ap.meta.counted,c.counted);assert.equal(ap.meta.expected,c.expected);
      assert.ok(rec(d).exceptions.some(x=>/Cash difference/.test(x)),d);}
    assert.equal(rec(TO).cash.difference,0);
  });
  test('[HIST-B] no posting on closed days: history is read-only except through the correction commands',()=>{
    for(const b of hist)for(const x of [...b.lines.map(l=>l.date),...b.receipts.map(r=>r.date)])assert.ok(s.closedDates.includes(x),b.id+' '+x);
    for(const e of s.expenses.filter(e=>e.id.startsWith('EXP-H')))assert.ok(s.closedDates.includes(e.date));
    const dep=hist.find(b=>b.status==='Departed');
    assert.throws(()=>execute(s,'cashier',{type:'PAYMENT',id:dep.id,amount:100,method:'UPI',reference:'X'}),/no longer open/);
    assert.throws(()=>execute(s,'owner',{type:'CHARGE',id:dep.id,label:'Late charge',amount:100}),/no longer open/);
  });
  test('[HIST-B] today (24 Oct) can run the normal close → next-day flow on top of FY26 history',()=>{
    let x=loadScenario('FY26');
    for(const b of x.bookings.filter(b=>b.status==='Confirmed')){x=execute(x,'owner',{type:'REGISTER',id:b.id});x=execute(x,'owner',{type:'CHECKIN',id:b.id});}
    x=execute(x,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});x=execute(x,'owner',{type:'NIGHT_POST'});
    const exp=drawer(x);x=execute(x,'owner',{type:'SHIFT_CLOSE',counted:exp});x=execute(x,'owner',{type:'DAY_CLOSE'});
    assert.equal(x.closings['2026-10-24'].cash.opening,500000);assert.equal(Object.keys(x.closings).length,207);
    x=execute(x,'owner',{type:'NEXT_DAY'});assert.equal(x.date,'2026-10-25');assert.equal(x.openingCash,exp);
  });
  test('[HIST-B] accounting: every day balanced, no blocking exceptions, vouchers reconcile with the reports and the cash count',()=>{
    let dr=0,cr=0;
    for(const d of days){
      const vs=accLiveVouchers(s,d);for(const v of vs)assert.ok(accBalanced(v),d+' '+v.no);
      assert.deepEqual(accExceptions(s,d).filter(e=>e.severity==='error'),[],d);
      for(const c of accReconcile(s,d))assert.ok(c.ok,`${d} ${c.id} report ${c.report} ledger ${c.ledger}`);
      const t=accTotals(vs);dr+=t.debit;cr+=t.credit;
    }
    assert.equal(dr,cr);assert.ok(dr>0);
    const all=days.flatMap(d=>accLiveVouchers(s,d)),tb=accTrialBalance(all);
    assert.equal(tb.reduce((a,r)=>a+r.debit,0),tb.reduce((a,r)=>a+r.credit,0));
  });
  test('[HIST-B] expenses: bills each month, weekly laundry, limits respected, rejected ones do not count, reversals offset',()=>{
    const ex=s.expenses.filter(e=>e.id.startsWith('EXP-H'));assert.ok(ex.length>400);
    for(const e of ex){assert.ok(e.date>=FROM&&e.date<=TO);if(e.status==='Posted'||e.status==='Reversed'){assert.equal(e.approver,Math.abs(e.amount)>EXPENSE_MANAGER_LIMIT?'owner':'manager',e.id);}}
    for(const m of ['04','05','06','07','08','09','10']){const mm=ex.filter(e=>e.date.slice(5,7)===m&&e.category==='Utilities'&&e.method==='Bank transfer');assert.ok(mm.length>=3,'bills in month '+m);}
    assert.ok(ex.filter(e=>e.vendor==='Fresh Linen Services').length>=28);
    const rej=ex.filter(e=>e.status==='Rejected');assert.ok(rej.length>=1);for(const e of rej){assert.ok(e.reason);assert.equal(expenseCounts(e),false);}
    const rv=ex.filter(e=>e.status==='Reversed');assert.ok(rv.length>=1);
    for(const e of rv){const r=ex.find(x=>x.id===e.reversedBy);assert.ok(r&&r.reversalOf===e.id&&r.amount===-e.amount&&r.date>e.date&&r.method===e.method);}
  });
  test('[HIST-B] discounts above the manager limit carry an approved approval; folios still balance',()=>{
    const discs=hist.flatMap(b=>b.lines.filter(l=>l.kind==='discount').map(l=>({b,l})));assert.ok(discs.length>10);
    for(const {b,l} of discs){assert.ok(l.amount<0);
      if(-l.amount>s.settings.discountLimit){const a=s.approvals.find(a=>a.id===l.approvalId);assert.ok(a&&a.status==='Approved'&&a.booking===b.id&&a.amount===-l.amount&&!a.kind,b.id);}
      else assert.equal(l.approvalId,undefined);}
    for(const b of hist.filter(b=>b.status==='Departed'))assert.equal(totals(b).balance,0,b.id);
  });
  test('[HIST-B] room moves keep both rooms free of overlaps (segments and booking room agree)',()=>{
    const moved=hist.filter(b=>b.segments);assert.ok(moved.length>=3,'moves '+moved.length);
    const spans=[];
    for(const b of s.bookings.filter(b=>['Departed','In house','Confirmed','Held'].includes(b.status)))
      for(const g of (b.segments??[{room:b.room,from:b.arrival,to:b.departure}]))spans.push({id:b.id,room:g.room,from:g.from,to:g.to??b.departure});
    const by=new Map();for(const x of spans){if(!by.has(x.room))by.set(x.room,[]);by.get(x.room).push(x);}
    for(const [room,l] of by){l.sort((a,b)=>a.from<b.from?-1:1);for(let i=1;i<l.length;i++)assert.ok(l[i-1].to<=l[i].from,`room ${room}: ${l[i-1].id} vs ${l[i].id}`);}
    for(const b of moved){assert.equal(b.room,b.segments.at(-1).room);assert.equal(b.segments[0].from,b.arrival);assert.equal(b.segments[0].to,b.segments[1].from);assert.match(b.notes,/moved from room/);}
  });
  test('[HIST-B] housekeeping history: 30 days of Done tasks, compact summary rows for every day, BASE tasks untouched',()=>{
    const base=baseFixture(),h=s.tasks.filter(t=>t.id.startsWith('task-H'));
    assert.ok(h.length>200);assert.deepEqual(s.tasks.filter(t=>!t.id.startsWith('task-H')),base.tasks);
    for(const t of h){assert.equal(t.status,'Done');assert.ok(t.date>=addDays(TO,-29)&&t.date<=TO);assert.ok(s.rooms.some(r=>r.id===t.room));assert.ok(s.bookings.some(b=>b.id===t.booking));assert.ok(['checkout','stayover'].includes(t.kind));}
    assert.equal(s.historyX.days.length,206);
    const dep=hist.filter(b=>b.status==='Departed');
    assert.equal(s.historyX.days.reduce((a,d)=>a+d.departures,0),dep.length);
    for(const d of s.historyX.days){assert.equal(d.cleans,d.departures);assert.equal(d.audits,d.cleans);}
    const lastCleans=s.historyX.days.filter(d=>d.date>=addDays(TO,-29)).reduce((a,d)=>a+d.cleans,0);
    assert.equal(h.filter(t=>t.kind==='checkout').length,lastCleans);
    assert.equal(h.filter(t=>t.kind==='stayover').length,s.historyX.days.filter(d=>d.date>=addDays(TO,-29)).reduce((a,d)=>a+d.stayovers,0));
  });
  test('[HIST-B] audit trail: one "Day closed" per day, every override event present, newest first, unique ids',()=>{
    const a=s.audit;assert.equal(a[0].action,'Scenario loaded');
    const h=a.filter(x=>x.id.startsWith('h-a-'));assert.equal(h.filter(x=>x.action==='Day closed').length,206);
    assert.equal(new Set(h.map(x=>x.id)).size,h.length);for(let i=1;i<h.length;i++)assert.ok(h[i-1].at>=h[i].at);
    assert.equal(h.filter(x=>x.action==='Cash difference approved').length,s.approvals.filter(x=>x.kind==='cash-difference').length);
    assert.equal(h.filter(x=>x.action==='Expense reversed').length,s.expenses.filter(e=>e.reversalOf&&e.id.startsWith('EXP-H')).length);
    assert.equal(h.filter(x=>x.action==='Company credit applied').length,hist.filter(b=>b.receipts.some(r=>r.kind==='company-credit')).length);
  });
  test('[HIST-B] history summary: months and FY-to-date equal the closing records and the FY report pack',()=>{
    const {months,total}=historyMonths(s),p=reportPack(s,FROM,TO),v=(t,id)=>p.tabs.find(x=>x.id===t).metrics.find(m=>m.id===id).value;
    assert.deepEqual(months.map(m=>m.month),['2026-04','2026-05','2026-06','2026-07','2026-08','2026-09','2026-10']);
    assert.equal(months.reduce((a,m)=>a+m.days,0),206);assert.equal(total.days,206);
    assert.equal(total.occupied,v('occupancy','occupied'));assert.equal(total.sellable,v('occupancy','sellable'));assert.equal(total.roomRevenue,v('occupancy','room-revenue'));
    assert.equal(total.netRevenue,v('revenue','net'));assert.equal(total.receipts,v('receipts','net'));assert.equal(total.expenses,v('expenses','total'));
    for(const k of ['occupied','sellable','roomRevenue','netRevenue','stays','cancellations','noShows','cleans','stayovers'])assert.equal(months.reduce((a,m)=>a+m[k],0),total[k],k);
    assert.equal(total.stays,hist.filter(b=>b.status==='Departed').length);
    const occ=total.occupied/total.sellable;assert.ok(occ>0.5&&occ<0.62);
    assert.equal(historyMonths(baseFixture()).months.length,0);
  });
  test('[HIST-B] budgets: FY generation ≤1.5 s and FY-to-date report ≤1 s',()=>{
    const runs=[];for(let i=0;i<3;i++){const t=performance.now();loadScenario('FY26');runs.push(performance.now()-t);}runs.sort((a,b)=>a-b);
    const t=performance.now();reportPack(s,FROM,TO);const pack=performance.now()-t;
    console.log(`  [HIST-B] generate ${runs[1].toFixed(0)} ms (median of 3), JSON ${(JSON.stringify(s).length/1e6).toFixed(2)} MB, FY report pack ${pack.toFixed(0)} ms`);
    assert.ok(runs[1]<=1500);assert.ok(pack<=1000);assert.ok(JSON.stringify(s).length<=12e6);
  });
  test('[HIST-B] 24 Oct figures still equal BASE with the full history in place',()=>{
    const b=baseFixture();assert.deepEqual(metrics(s),metrics(b));assert.equal(drawer(s),drawer(b));
  });
}
