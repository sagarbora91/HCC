// F8 QW-PM · PM calendar, AMC cards, CCTV / DG template button. Names prefixed [F8-QW-PM].
export function register({test,M,assert}){
  const {execute,baseFixture,HccError}=M;
  const code=(fn)=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const flat=(s,m)=>M.pmcMonth(s,m).flatMap(d=>d.items);
  const monthOf=(s)=>s.date.slice(0,7);

  test('[F8-QW-PM] Occurrences per month match mntPmDue and each plan everyDays',()=>{
    const s=baseFixture(),m=monthOf(s),items=flat(s,m);
    // every generate-able occurrence in the month appears, as Due or Overdue
    for(const d of M.mntPmDue(s).filter(d=>d.due.startsWith(m))){
      const o=items.find(i=>i.planId===d.plan.id&&i.due===d.due);assert.ok(o,`${d.plan.id}@${d.due} missing`);assert.ok(o.state==='Due'||o.state==='Overdue');assert.equal(o.workOrder,undefined);
    }
    // future occurrences step by everyDays from nextDue
    for(const p of M.mntSlice(s).plans.filter(p=>p.active)){
      const dates=items.filter(i=>i.planId===p.id&&!i.workOrder).map(i=>i.due).sort();
      for(let i=1;i<dates.length;i++)assert.equal(Math.round((Date.parse(dates[i])-Date.parse(dates[i-1]))/86400000),p.everyDays);
      assert.ok(dates.every(d=>d>=p.nextDue));
    }
    // days sorted, items inside the month
    const days=M.pmcMonth(s,m);assert.deepEqual(days.map(d=>d.date),[...days.map(d=>d.date)].sort());assert.ok(days.every(d=>d.date.startsWith(m)));
    // a month far away projects occurrences only for active plans, none done
    const far=flat(s,M.pmcShiftMonth(m,6));assert.ok(far.every(i=>i.state==='Scheduled'));
    assert.deepEqual(M.pmcShiftMonth('2026-12',1),'2027-01');assert.deepEqual(M.pmcShiftMonth('2026-01',-1),'2025-12');
  });
  test('[F8-QW-PM] Existing work orders show their state; generate turns Due into Done-or-open with an order link',()=>{
    let s=baseFixture();const m=monthOf(s);
    s=execute(s,'supervisor',{type:'MNT_PM_GENERATE'});
    const items=flat(s,m),generated=M.mntSlice(s).orders.filter(o=>o.kind==='Preventive'&&o.status==='Open');
    for(const w of generated.filter(w=>w.dueDate.startsWith(m)))assert.equal(items.find(i=>i.workOrder===w.id)?.due,w.dueDate);
    assert.equal(items.filter(i=>!i.workOrder&&(i.state==='Due'||i.state==='Overdue')).length,0);
    const done=M.mntSlice(s).orders.filter(o=>o.kind==='Preventive'&&o.dueDate?.startsWith(m)&&(o.status==='Verified'||o.status==='Done'));
    for(const w of done)assert.equal(items.find(i=>i.workOrder===w.id)?.state,'Done');
    s=execute(s,'supervisor',{type:'MNT_WO_CANCEL',id:generated[0].id,reason:'Not needed'});
    assert.ok(!flat(s,m).some(i=>i.workOrder===generated[0].id));
  });
  test('[F8-QW-PM] Overdue boundary: the business date itself is Due, the day before is Overdue (as mntPlanState)',()=>{
    const s=baseFixture();s.maintenanceX={...s.maintenanceX,orders:[],plans:[{id:'PM-T',assetId:s.maintenanceX.assets[0].id,title:'Boundary test',everyDays:30,nextDue:M.addDays(s.date,-1),active:true}]};
    const st=(due)=>M.pmcState(s,due,null);
    assert.equal(st(M.addDays(s.date,-1)),'Overdue');assert.equal(st(s.date),'Due');assert.equal(st(M.addDays(s.date,3)),'Due');assert.equal(st(M.addDays(s.date,4)),'Scheduled');
    assert.equal(M.mntPlanState(s,s.maintenanceX.plans[0]),'Overdue');
    const o=flat(s,monthOf(s)).find(i=>i.due===M.addDays(s.date,-1));
    if(M.addDays(s.date,-1).startsWith(monthOf(s)))assert.equal(o.state,'Overdue');
    assert.equal(M.pmcState(s,M.addDays(s.date,-9),{done:true}),'Done');
  });
  test('[F8-QW-PM] AMC cards: Active / Expiring at 30 days / Expired, soonest end first',()=>{
    const s=baseFixture();const a=s.maintenanceX.amcs[0];
    s.maintenanceX={...s.maintenanceX,amcs:[
      {...a,id:'A1',from:M.addDays(s.date,-100),to:M.addDays(s.date,31)},{...a,id:'A2',from:M.addDays(s.date,-100),to:M.addDays(s.date,30)},
      {...a,id:'A3',from:M.addDays(s.date,-100),to:M.addDays(s.date,-1)},{...a,id:'A4',from:M.addDays(s.date,1),to:M.addDays(s.date,300)}]};
    const c=M.pmcAmcCards(s);
    assert.deepEqual(c.map(x=>x.id+':'+x.status),['A3:Expired','A2:Expiring','A1:Active','A4:Not started']);
    assert.equal(c.find(x=>x.id==='A2').daysLeft,30);assert.equal(c.find(x=>x.id==='A3').daysLeft,-1);
    assert.equal(M.pmcAmcCards(baseFixture()).length,3);
  });
  test('[F8-QW-PM] No money key anywhere in what Maintenance is shown',()=>{
    const s=baseFixture();const bad=/value|cost|amount|price|paise|rupee|\bgst\b/i;
    const keys=(o,acc=new Set())=>{if(o&&typeof o==='object')for(const [k,v] of Object.entries(o)){acc.add(k);keys(v,acc);}return acc;};
    for(const k of keys([M.pmcMonth(s,monthOf(s)),M.pmcMonth(s,M.pmcShiftMonth(monthOf(s),1)),M.pmcAmcCards(s)]))assert.ok(!bad.test(k),'money-like key '+k);
    // the register link carries number and status only, even when the asset matches a register row
    s.accX={...(s.accX??{})};
    for(const i of flat(s,monthOf(s)))if(i.register)assert.deepEqual(Object.keys(i.register).sort(),['no','status']);
  });
  test('[F8-QW-PM] CCTV / DG template: three shifts, QWQ-6 items, Manager only, saved once, idempotent',()=>{
    let s=baseFixture();
    const t=M.pmcTemplates();assert.deepEqual(t.map(x=>x.shift),['Morning','Evening','Night']);
    for(const x of t){assert.equal(x.desk,'Maintenance');assert.equal(x.items.length,5);assert.ok(x.name.startsWith('CCTV and DG shift check'));}
    assert.deepEqual(t[0].items.map(i=>i.text),['CCTV recorder running and all cameras visible','DG diesel level noted','DG weekly test run done','Battery / UPS status checked','Fire panel normal']);
    assert.equal(M.pmcMissingTemplates(s).length,3);
    assert.equal(code(()=>execute(s,'maint',{type:'HOV_TEMPLATE_SAVE',template:t[0],key:'k0'})),'FORBIDDEN');
    const before=M.hovSlice(s).templates.length;
    for(const x of t)s=execute(s,'manager',{type:'HOV_TEMPLATE_SAVE',template:x,key:'qwpm:tpl:'+x.name});
    assert.equal(M.hovSlice(s).templates.length,before+3);assert.equal(M.pmcMissingTemplates(s).length,0);
    // replay (same key) and a second press by name do not add a copy
    for(const x of t){const again=(()=>{try{return execute(s,'manager',{type:'HOV_TEMPLATE_SAVE',template:x,key:'qwpm:tpl:'+x.name});}catch(e){return s;}})();s=again;}
    assert.equal(M.hovSlice(s).templates.length,before+3);
  });
}
