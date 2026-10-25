// Wave 0.5 · W05-C Compliance calendar — domain/command tests. Names prefixed [CMP]. Rules from SCC tax module; dates illustrative.
export function register({test,M,assert}){
  const {seed,execute,cmpSlice,cmpItems,cmpCounts,cmpDueDate,cmpDueThisWeek,cmpBadge,cmpHasAccess,cmpCanManage,cmpPeriods,cmpSettingDefs,cmpPackLines,aiAnswer,aiMatchIntent,aiAccess,aiSuggestions}=M;
  const code=c=>e=>e.code===c;
  const ob=(s,id)=>cmpSlice(s).obligations.find(o=>o.id===id);
  const find=(s,o,p)=>cmpItems(s).find(i=>i.key===`${o}:${p}`);

  test('[CMP] fixture: 7 obligations, 37 rows (6 months x 6 + GSTR-9); core figures unchanged',()=>{
    const s=seed(),x=cmpSlice(s);assert.equal(x.obligations.length,7);assert.equal(cmpItems(s).length,37);
    assert.equal(M.metrics(s).occupied,8);assert.equal(M.metrics(s).collections,2150000);assert.equal(M.drawer(s),1075000);});
  test('[CMP] counters on 24 Oct 2026: 1 overdue, 1 due soon, 1 upcoming, 32 filed, 2 not applicable',()=>{
    const c=cmpCounts(cmpItems(seed()));assert.deepEqual(c,{overdue:1,due:1,upcoming:1,filed:32,na:2,open:3});});
  test('[CMP] absent slice reads as empty (old saves)',()=>{const s=seed();delete s.complianceX;assert.equal(cmpItems(s).length,0);assert.equal(cmpSlice(s).settings.dueSoonDays,7);});
  test('[CMP] SCC due-date rules: TDS 7th (March 30 Apr), GSTR-1 11th, GSTR-3B 20th, EPF/ESIC 15th, PT last day, GSTR-9 31 Dec',()=>{
    const s=seed();
    assert.equal(cmpDueDate(ob(s,'CMPO-TDS'),'2026-09'),'2026-10-07');assert.equal(cmpDueDate(ob(s,'CMPO-TDS'),'2026-03'),'2026-04-30');
    assert.equal(cmpDueDate(ob(s,'CMPO-G1'),'2026-09'),'2026-10-11');assert.equal(cmpDueDate(ob(s,'CMPO-G3B'),'2026-09'),'2026-10-20');
    assert.equal(cmpDueDate(ob(s,'CMPO-EPF'),'2026-09'),'2026-10-15');assert.equal(cmpDueDate(ob(s,'CMPO-ESIC'),'2026-09'),'2026-10-15');
    assert.equal(cmpDueDate(ob(s,'CMPO-PT'),'2026-09'),'2026-10-31');assert.equal(cmpDueDate(ob(s,'CMPO-PT'),'2027-01'),'2027-02-28');assert.equal(cmpDueDate(ob(s,'CMPO-PT'),'2027-12'),'2028-01-31');
    assert.equal(cmpDueDate(ob(s,'CMPO-G9'),'2025-26'),'2026-12-31');});
  test('[CMP] only finished periods are tracked',()=>{
    const s=seed();assert.deepEqual(cmpPeriods(ob(s,'CMPO-EPF'),s.date),['2026-04','2026-05','2026-06','2026-07','2026-08','2026-09']);
    assert.deepEqual(cmpPeriods(ob(s,'CMPO-G9'),s.date),['2025-26']);});
  test('[CMP] status text: overdue by days, due in days, filed',()=>{
    const s=seed();assert.equal(M.cmpStatusText(find(s,'CMPO-G1','2026-09')),'Overdue by 13 days');
    assert.equal(M.cmpStatusText(find(s,'CMPO-PT','2026-09')),'Due in 7 days');assert.equal(M.cmpStatusText(find(s,'CMPO-G9','2025-26')),'Due in 68 days');
    assert.equal(find(s,'CMPO-TDS','2026-09').status,'Filed');});
  test('[CMP] access: Owner and Accountant only; Manager and Cashier are refused',()=>{
    const s=seed();for(const u of ['owner','accountant'])assert.equal(cmpHasAccess(s,u),true);
    for(const u of ['manager','cashier','reception','supervisor','housekeeper','maint','techadmin'])assert.equal(cmpHasAccess(s,u),false);
    assert.equal(cmpCanManage(s,'owner'),true);assert.equal(cmpCanManage(s,'accountant'),false);
    assert.throws(()=>execute(s,'manager',{type:'CMP_FILE',obligation:'CMPO-G1',period:'2026-09',reference:'ARN-12345'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'cashier',{type:'CMP_CA_PACK'}),code('FORBIDDEN'));});
  test('[CMP] mark filed records who, when and reference; late filing is noted; repeat key is idempotent',()=>{
    let s=seed();s=execute(s,'accountant',{type:'CMP_FILE',obligation:'CMPO-G1',period:'2026-09',reference:'ARN-AA2610-0042',key:'f1'});
    const i=find(s,'CMPO-G1','2026-09');assert.equal(i.status,'Filed');assert.equal(i.filing.by,'accountant');assert.equal(i.filing.at,s.clock);assert.equal(i.filing.reference,'ARN-AA2610-0042');
    assert.match(s.audit.at(-1).detail,/13 day\(s\) late/);
    assert.deepEqual(execute(s,'accountant',{type:'CMP_FILE',obligation:'CMPO-G1',period:'2026-09',reference:'ARN-AA2610-0042',key:'f1'}),s);
    assert.equal(cmpCounts(cmpItems(s)).overdue,0);});
  test('[CMP] mark filed validation: reference, unknown period, already filed, future period',()=>{
    const s=seed();
    assert.throws(()=>execute(s,'owner',{type:'CMP_FILE',obligation:'CMPO-G1',period:'2026-09',reference:'ab'}),/reference/);
    assert.throws(()=>execute(s,'owner',{type:'CMP_FILE',obligation:'CMPO-G1',period:'2026-09',reference:'<script>x</script>'}),/reference/);
    assert.throws(()=>execute(s,'owner',{type:'CMP_FILE',obligation:'CMPO-G1',period:'2026-10',reference:'ARN-1234'}),code('NOT_FOUND'));
    assert.throws(()=>execute(s,'owner',{type:'CMP_FILE',obligation:'CMPO-NOPE',period:'2026-09',reference:'ARN-1234'}),code('NOT_FOUND'));
    assert.throws(()=>execute(s,'owner',{type:'CMP_FILE',obligation:'CMPO-TDS',period:'2026-09',reference:'ARN-1234'}),code('INVALID_TRANSITION'));});
  test('[CMP] not applicable needs a reason; shows as Not applicable with the reason',()=>{
    let s=seed();assert.throws(()=>execute(s,'accountant',{type:'CMP_NA',obligation:'CMPO-PT',period:'2026-09',reason:'no'}),/reason/);
    s=execute(s,'accountant',{type:'CMP_NA',obligation:'CMPO-PT',period:'2026-09',reason:'Exempt this month (illustrative)'});
    const i=find(s,'CMPO-PT','2026-09');assert.equal(i.status,'Not applicable');assert.equal(i.filing.reason,'Exempt this month (illustrative)');
    assert.equal(cmpCounts(cmpItems(s)).na,3);});
  test('[CMP] reopen is Owner-only with a reason and keeps an audit line',()=>{
    let s=seed();assert.throws(()=>execute(s,'accountant',{type:'CMP_REOPEN',obligation:'CMPO-TDS',period:'2026-09',reason:'Wrong reference'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'CMP_REOPEN',obligation:'CMPO-TDS',period:'2026-09',reason:'x'}),/reason/);
    assert.throws(()=>execute(s,'owner',{type:'CMP_REOPEN',obligation:'CMPO-G1',period:'2026-09',reason:'Not filed yet at all'}),code('INVALID_TRANSITION'));
    s=execute(s,'owner',{type:'CMP_REOPEN',obligation:'CMPO-TDS',period:'2026-09',reason:'Wrong reference'});
    assert.equal(find(s,'CMPO-TDS','2026-09').status,'Overdue');assert.match(s.audit.at(-1).detail,/reopened/);});
  test('[CMP] due this week = overdue + inside the window; badge only for Owner and Accountant',()=>{
    const s=seed();assert.deepEqual(cmpDueThisWeek(s).map(i=>i.key),['CMPO-G1:2026-09','CMPO-PT:2026-09']);
    assert.equal(cmpBadge(s,'owner'),2);assert.equal(cmpBadge(s,'accountant'),2);assert.equal(cmpBadge(s,'manager'),0);assert.equal(cmpBadge(s,'reception'),0);
    const w=execute(s,'owner',{type:'CMP_SETTINGS_SET',dueSoonDays:80});assert.equal(cmpDueThisWeek(w).length,3);
    const n=execute(s,'owner',{type:'CMP_SETTINGS_SET',dueSoonDays:3});assert.deepEqual(cmpDueThisWeek(n).map(i=>i.key),['CMPO-G1:2026-09']);});
  test('[CMP] manage: new rule applies from its date only; old periods keep the old rule',()=>{
    let s=seed();assert.throws(()=>execute(s,'accountant',{type:'CMP_OBLIGATION_SAVE',id:'CMPO-EPF',name:'EPF contribution',rule:{type:'day',day:20}}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'CMP_OBLIGATION_SAVE',id:'CMPO-EPF',name:'EPF contribution',rule:{type:'day',day:31}}),/1–28/);
    assert.throws(()=>execute(s,'owner',{type:'CMP_OBLIGATION_SAVE',id:'CMPO-EPF',name:'EPF contribution',rule:{type:'day',day:20},effectiveFrom:'2026-10-01'}),/today or later/);
    s=execute(s,'owner',{type:'CMP_OBLIGATION_SAVE',id:'CMPO-EPF',name:'EPF contribution',form:'EPFO unified portal · ECR',owner:'Accountant',rule:{type:'day',day:20},effectiveFrom:'2026-11-01'});
    const o=ob(s,'CMPO-EPF');assert.equal(o.rules.length,2);assert.equal(cmpDueDate(o,'2026-09'),'2026-10-15');assert.equal(cmpDueDate(o,'2026-10'),'2026-11-15');assert.equal(cmpDueDate(o,'2026-11'),'2026-12-20');});
  test('[CMP] add obligation tracks from the last finished period (no fake backlog); duplicate name refused; toggle keeps history',()=>{
    let s=seed();s=execute(s,'owner',{type:'CMP_OBLIGATION_SAVE',kind:'PT',name:'PT — annex building (fictional)',form:'State PT portal',rule:{type:'month-end'}});
    const n=cmpSlice(s).obligations.at(-1);assert.equal(n.from,'2026-09');assert.equal(find(s,n.id,'2026-09').status,'Due soon');assert.equal(cmpItems(s).filter(i=>i.obligation.id===n.id).length,1);
    assert.throws(()=>execute(s,'owner',{type:'CMP_OBLIGATION_SAVE',kind:'PT',name:'pt — annex building (fictional)',rule:{type:'month-end'}}),/already exists/);
    assert.throws(()=>execute(s,'owner',{type:'CMP_OBLIGATION_SAVE',kind:'XYZ',name:'Something',rule:{type:'month-end'}}),/Choose a type/);
    const t=execute(s,'owner',{type:'CMP_OBLIGATION_TOGGLE',id:'CMPO-EPF'});assert.equal(cmpItems(t).some(i=>i.obligation.id==='CMPO-EPF'),false);
    assert.equal(cmpSlice(t).filings.filter(f=>f.obligation==='CMPO-EPF').length,6);
    assert.throws(()=>execute(t,'owner',{type:'CMP_FILE',obligation:'CMPO-EPF',period:'2026-09',reference:'ARN-1234'}),code('INVALID_TRANSITION'));});
  test('[CMP] CA pack: simulated, saved with counts and lines; idempotent; only 20 kept',()=>{
    let s=seed();s=execute(s,'accountant',{type:'CMP_CA_PACK',key:'p1'});const p=cmpSlice(s).packs[0];
    assert.equal(p.counts.overdue,1);assert.equal(p.toName,'Rao & Associates (fictional)');assert.ok(p.lines.some(l=>l.startsWith('OPEN · GSTR-1')));assert.match(s.audit.at(-1).detail,/simulated/);
    assert.deepEqual(execute(s,'accountant',{type:'CMP_CA_PACK',key:'p1'}),s);
    for(let n=0;n<25;n++)s=execute(s,'owner',{type:'CMP_CA_PACK',key:'pp'+n});assert.equal(cmpSlice(s).packs.length,20);
    assert.ok(cmpPackLines(seed()).length>=4);});
  test('[CMP] settings validation (CA e-mail, window)',()=>{
    const s=seed();assert.throws(()=>execute(s,'owner',{type:'CMP_SETTINGS_SET',caEmail:'not-an-email'}),/e-mail/);assert.throws(()=>execute(s,'owner',{type:'CMP_SETTINGS_SET',dueSoonDays:0}),/1–30/);
    assert.throws(()=>execute(s,'accountant',{type:'CMP_SETTINGS_SET',dueSoonDays:5}),code('FORBIDDEN'));
    const n=execute(s,'owner',{type:'CMP_SETTINGS_SET',caName:'Iyer & Co (fictional)',caEmail:'desk@example.test',dueSoonDays:10});assert.equal(cmpSlice(n).settings.dueSoonDays,10);});
  test('[CMP] cmpSettingDefs exposes CA, window and one dated rule per obligation for the hub',()=>{
    const d=cmpSettingDefs(seed());assert.equal(d.length,3+7);assert.ok(d.every(x=>x.category==='Compliance obligations'&&x.help));
    assert.equal(d.find(x=>x.key==='cmp.rule.CMPO-PT').value,'Last day of the next month');assert.match(d.find(x=>x.key==='cmp.rule.CMPO-TDS').value,/7th of the next month/);});
  test('[CMP] assistant: "what is due" lists compliance for Owner/Accountant, refused for others',()=>{
    const s=seed();assert.equal(aiMatchIntent('What compliance filings are due?'),'compliance-due');assert.equal(aiMatchIntent('is GST filed'),'compliance-due');
    const a=aiAnswer(s,'accountant','What is due this week?');assert.equal(a.intent,'compliance-due');assert.equal(a.figures[0].value,'1');assert.equal(a.items.length,2);assert.ok(a.links.some(l=>l.page==='compliance'));
    assert.equal(aiAnswer(s,'manager','What compliance filings are due?').restricted,true);
    assert.equal(aiAccess(s,'owner').compliance,true);assert.equal(aiAccess(s,'reception').compliance,false);
    assert.ok(aiSuggestions(s,'owner').some(t=>t.intent==='compliance-due'));assert.ok(!aiSuggestions(s,'manager').some(t=>t.intent==='compliance-due'));});
  test('[CMP] existing assistant intents unchanged by the new keywords',()=>{
    for(const [q,i] of [['Who has the biggest balances due','balances'],['Collections vs outstanding today','collections'],['What approvals are pending?','approvals'],['How is the cash drawer and any variance?','cash']])assert.equal(aiMatchIntent(q),i);});
}
