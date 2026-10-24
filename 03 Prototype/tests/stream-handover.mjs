// Module 19 · Checklists & shift handover — domain/command tests. Names prefixed [HOV].
export function register({test,M,assert}){
  const {seed,execute,hovSlice,hovRunFor,hovProgress,hovMyRuns,hovIncoming,hovDashboard,hovNotesFor,hovOpenCarry,hovShiftAt,hovAt,hovSeesCash,hovDesksFor,hovOverdueList}=M;
  const code=c=>e=>e.code===c;
  const ack=s=>execute(s,'reception',{type:'HOV_NOTE_ACK',id:'HOVN-FRONT-2026-10-23-N',key:'ack-1'});
  const photoName='IMG_20261024_0810.jpg';
  function openingDone(s){
    s=ack(s);
    for(const i of ['fo1','fo4'])s=execute(s,'reception',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:i,done:true});
    s=execute(s,'reception',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:'fo6',photo:photoName});
    return execute(s,'reception',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:'fo6',done:true});
  }

  test('[HOV] fixture: 6 templates, 8 runs, 3 notes; core figures unchanged',()=>{
    const s=seed(),x=hovSlice(s);assert.equal(x.templates.length,6);assert.equal(x.runs.length,8);assert.equal(x.notes.length,3);
    assert.equal(M.metrics(s).occupied,8);assert.equal(M.metrics(s).collections,2150000);assert.equal(M.drawer(s),1020000);});
  test('[HOV] absent slice reads as empty (old saves)',()=>{const s=seed();delete s.handoverX;assert.deepEqual(hovSlice(s),{templates:[],runs:[],notes:[]});});
  test('[HOV] shift windows and night roll-over',()=>{
    assert.equal(hovShiftAt('2026-10-24T08:00'),'Morning');assert.equal(hovShiftAt('2026-10-24T15:00'),'Evening');assert.equal(hovShiftAt('2026-10-24T23:10'),'Night');assert.equal(hovShiftAt('2026-10-25T02:00'),'Night');
    assert.equal(hovAt('2026-10-24','Night','00:30'),'2026-10-25T00:30');assert.equal(hovAt('2026-10-24','Night','23:30'),'2026-10-24T23:30');});
  test('[HOV] opening run at 08:00: 2/7 done = 29 %, item due 07:30 overdue',()=>{
    const s=seed(),r=hovRunFor(s,'HOVT-FD-OPEN'),p=hovProgress(s,r);
    assert.equal(p.done,2);assert.equal(p.total,7);assert.equal(p.pct,29);assert.equal(p.overdue,1);assert.equal(p.mandatory,5);
    assert.equal(hovOverdueList(s).length,1);});
  test('[HOV] my checklists are filtered by role',()=>{
    const s=seed(),names=u=>hovMyRuns(s,u).map(r=>r.template).sort();
    assert.deepEqual(names('reception'),['HOVT-CASH-HO','HOVT-FD-CLOSE','HOVT-FD-OPEN','HOVT-NA-PRE']);
    assert.deepEqual(names('housekeeper'),['HOVT-HK-FLOOR']);
    assert.deepEqual(names('maint'),['HOVT-MNT-RND']);
    assert.deepEqual(names('cashier'),['HOVT-CASH-HO']);
    assert.equal(names('owner').length,6);assert.equal(names('accountant').length,0);assert.equal(names('techadmin').length,0);});
  test('[HOV] first tick on a not-started template creates the run with a snapshot',()=>{
    let s=seed();assert.equal(hovRunFor(s,'HOVT-MNT-RND'),undefined);
    s=execute(s,'maint',{type:'HOV_ITEM',template:'HOVT-MNT-RND',item:'mr1',done:true});
    const r=hovRunFor(s,'HOVT-MNT-RND');assert.equal(r.items.find(i=>i.id==='mr1').by,'maint');assert.equal(r.templateVersion,1);assert.equal(r.status,'Open');});
  test('[HOV] template edit bumps version; existing run keeps its items',()=>{
    let s=seed();const t=structuredClone(hovSlice(s).templates.find(t=>t.id==='HOVT-FD-OPEN'));
    t.items=t.items.slice(0,3);t.items.push({text:'New item added by manager',mandatory:true});
    s=execute(s,'manager',{type:'HOV_TEMPLATE_SAVE',template:t});
    const nt=hovSlice(s).templates.find(x=>x.id==='HOVT-FD-OPEN');assert.equal(nt.version,2);assert.equal(nt.items.length,4);
    assert.equal(hovRunFor(s,'HOVT-FD-OPEN').items.length,7);});
  test('[HOV] only Owner/Manager edit templates; validation',()=>{
    const s=seed(),t={name:'Pool deck check',desk:'Maintenance',shift:'Evening',roles:['Maintenance'],items:[{text:'Skim the pool',mandatory:true,due:'16:00'}]};
    assert.throws(()=>execute(s,'reception',{type:'HOV_TEMPLATE_SAVE',template:t}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'supervisor',{type:'HOV_TEMPLATE_TOGGLE',id:'HOVT-FD-OPEN'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'HOV_TEMPLATE_SAVE',template:{...t,roles:['Cashier']}}),/belong to the Maintenance desk/);
    assert.throws(()=>execute(s,'owner',{type:'HOV_TEMPLATE_SAVE',template:{...t,items:[{text:'Skim',due:'25:00'}]}}),/HH:mm/);
    assert.throws(()=>execute(s,'owner',{type:'HOV_TEMPLATE_SAVE',template:{...t,items:[]}}),/1–25 items/);
    assert.throws(()=>execute(s,'owner',{type:'HOV_TEMPLATE_SAVE',template:{...t,name:'Front desk opening'}}),/already exists/);
    const n=execute(s,'owner',{type:'HOV_TEMPLATE_SAVE',template:t});assert.equal(hovSlice(n).templates.length,7);
    assert.ok(hovMyRuns(n,'maint').some(r=>r.name==='Pool deck check'));});
  test('[HOV] deactivated template hides from today unless started; cannot start',()=>{
    let s=execute(seed(),'manager',{type:'HOV_TEMPLATE_TOGGLE',id:'HOVT-MNT-RND'});
    assert.ok(!hovMyRuns(s,'maint').length);
    assert.throws(()=>execute(s,'maint',{type:'HOV_ITEM',template:'HOVT-MNT-RND',item:'mr1',done:true}),/inactive/);});
  test('[HOV] wrong role cannot tick another role’s checklist',()=>{
    assert.throws(()=>execute(seed(),'housekeeper',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:'fo1',done:true}),code('FORBIDDEN'));
    assert.throws(()=>execute(seed(),'accountant',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:'fo1',done:true}),code('FORBIDDEN'));
    assert.throws(()=>execute(seed(),'cashier',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:'fo1',done:true}),code('FORBIDDEN'));});
  test('[HOV] photo item needs a photo file name; notes limited',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'reception',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:'fo6',done:true}),/photo file name/);
    assert.throws(()=>execute(s,'reception',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:'fo6',photo:'lobby.exe'}),/file name/);
    assert.throws(()=>execute(s,'reception',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:'fo7',note:'x'.repeat(201)}),/200/);
    s=execute(s,'reception',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:'fo6',photo:photoName,note:'Flowers replaced'});
    s=execute(s,'reception',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:'fo6',done:true});
    const i=hovRunFor(s,'HOVT-FD-OPEN').items.find(i=>i.id==='fo6');assert.equal(i.photoName,photoName);assert.equal(i.note,'Flowers replaced');assert.equal(i.done,true);});
  test('[HOV] submit blocked by mandatory items, then by unacknowledged incoming note',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'reception',{type:'HOV_RUN_SUBMIT',template:'HOVT-FD-OPEN'}),e=>e.code==='INVALID_TRANSITION'&&/3 mandatory items/.test(e.message));
    for(const i of ['fo1','fo4'])s=execute(s,'reception',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:i,done:true});
    s=execute(s,'reception',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:'fo6',photo:photoName});s=execute(s,'reception',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:'fo6',done:true});
    assert.throws(()=>execute(s,'reception',{type:'HOV_RUN_SUBMIT',template:'HOVT-FD-OPEN'}),/Acknowledge the incoming Front desk handover/);
    s=ack(s);s=execute(s,'reception',{type:'HOV_RUN_SUBMIT',template:'HOVT-FD-OPEN'});
    const r=hovRunFor(s,'HOVT-FD-OPEN');assert.equal(r.status,'Submitted');assert.equal(r.late,false);assert.equal(hovProgress(s,r).overdue,0);
    assert.throws(()=>execute(s,'reception',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:'fo7',done:true}),/submitted/);});
  test('[HOV] reopen: manager only, reason required',()=>{
    let s=execute(openingDone(seed()),'reception',{type:'HOV_RUN_SUBMIT',template:'HOVT-FD-OPEN'});
    assert.throws(()=>execute(s,'reception',{type:'HOV_RUN_REOPEN',template:'HOVT-FD-OPEN',reason:'Redo'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'manager',{type:'HOV_RUN_REOPEN',template:'HOVT-FD-OPEN',reason:''}),/reason/);
    s=execute(s,'manager',{type:'HOV_RUN_REOPEN',template:'HOVT-FD-OPEN',reason:'Lobby photo blurred'});
    assert.equal(hovRunFor(s,'HOVT-FD-OPEN').status,'Open');assert.equal(hovRunFor(s,'HOVT-FD-OPEN').reopenReason,'Lobby photo blurred');});
  test('[HOV] acknowledge: own note forbidden, other desk forbidden, idempotent key, once only',()=>{
    const s=seed();
    assert.throws(()=>execute(s,'manager',{type:'HOV_NOTE_ACK',id:'HOVN-FRONT-2026-10-23-N'}),e=>e.code==='FORBIDDEN'&&/own handover/.test(e.message));
    assert.throws(()=>execute(s,'housekeeper',{type:'HOV_NOTE_ACK',id:'HOVN-FRONT-2026-10-23-N'}),code('FORBIDDEN'));
    assert.equal(hovIncoming(s,'reception').length,1);assert.equal(hovIncoming(s,'manager').length,0);
    const a=ack(s);assert.deepEqual(ack(a),a);
    assert.equal(hovSlice(a).notes.find(n=>n.id==='HOVN-FRONT-2026-10-23-N').ackBy,'reception');assert.equal(hovIncoming(a,'reception').length,0);
    assert.throws(()=>execute(a,'cashier',{type:'HOV_NOTE_ACK',id:'HOVN-FRONT-2026-10-23-N'}),/already acknowledged/);});
  test('[HOV] handover note: desk membership, cash only front desk with payments, replace until ack',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'housekeeper',{type:'HOV_NOTE_SUBMIT',desk:'Front desk',issues:'Test note'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'supervisor',{type:'HOV_NOTE_SUBMIT',desk:'Housekeeping',issues:'Linen short',cash:{amount:1000}}),/only on the Front desk/);
    assert.throws(()=>execute(s,'reception',{type:'HOV_NOTE_SUBMIT',desk:'Front desk',issues:''}),/Nothing to report/);
    assert.throws(()=>execute(s,'reception',{type:'HOV_NOTE_SUBMIT',desk:'Front desk',nothing:true,carry:Array(9).fill('Carry item')}),/Up to 8/);
    s=execute(s,'cashier',{type:'HOV_NOTE_SUBMIT',desk:'Front desk',issues:'UPI settlement short by one entry',cash:{amount:1020000,note:'Drawer'},carry:['Recheck UPI DEMO-104']});
    const n=hovSlice(s).notes.at(-1);assert.equal(n.id,'HOVN-FRONT-2026-10-24-M');assert.equal(n.toShift,'Evening');assert.equal(n.cash.amount,1020000);assert.equal(n.carry.length,1);
    s=execute(s,'cashier',{type:'HOV_NOTE_SUBMIT',desk:'Front desk',issues:'Corrected note',carry:[]});
    assert.equal(hovSlice(s).notes.filter(x=>x.id===n.id).length,1);assert.equal(hovSlice(s).notes.find(x=>x.id===n.id).issues,'Corrected note');
    assert.throws(()=>execute(s,'reception',{type:'HOV_NOTE_SUBMIT',desk:'Front desk',issues:'Overwrite attempt'}),code('FORBIDDEN'));
    s=execute(s,'reception',{type:'HOV_NOTE_ACK',id:n.id});
    assert.throws(()=>execute(s,'cashier',{type:'HOV_NOTE_SUBMIT',desk:'Front desk',issues:'Too late'}),/already acknowledged/);});
  test('[HOV] housekeepers never see money or front-desk notes',()=>{
    const s=seed();assert.equal(hovSeesCash(s,'housekeeper'),false);assert.equal(hovSeesCash(s,'maint'),false);assert.equal(hovSeesCash(s,'cashier'),true);
    assert.deepEqual(hovDesksFor(s,'housekeeper'),['Housekeeping']);assert.deepEqual(hovDesksFor(s,'supervisor'),['Housekeeping','Maintenance']);
    assert.ok(hovNotesFor(s,'housekeeper').every(n=>!n.cash&&n.desk==='Housekeeping'));});
  test('[HOV] carry-forward items close once, by desk members',()=>{
    let s=seed();assert.equal(hovOpenCarry(s,'Maintenance').length,2);
    assert.throws(()=>execute(s,'reception',{type:'HOV_CARRY_DONE',note:'HOVN-MAINTENANCE-2026-10-23-M',item:'HOVN-MAINTENANCE-2026-10-23-M-c1'}),code('FORBIDDEN'));
    s=execute(s,'maint',{type:'HOV_CARRY_DONE',note:'HOVN-MAINTENANCE-2026-10-23-M',item:'HOVN-MAINTENANCE-2026-10-23-M-c1'});
    assert.equal(hovOpenCarry(s,'Maintenance').length,1);
    assert.throws(()=>execute(s,'maint',{type:'HOV_CARRY_DONE',note:'HOVN-MAINTENANCE-2026-10-23-M',item:'HOVN-MAINTENANCE-2026-10-23-M-c1'}),/already done/);});
  test('[HOV] demo clock: managers only, capped, drives overdue flags',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'reception',{type:'HOV_CLOCK',minutes:60}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'manager',{type:'HOV_CLOCK',minutes:5}),/15–120/);
    const before=hovProgress(s,hovRunFor(s,'HOVT-HK-FLOOR')).overdue;assert.equal(before,0);
    s=execute(s,'manager',{type:'HOV_CLOCK',minutes:120});assert.equal(s.clock,'2026-10-24T10:01');
    assert.equal(hovProgress(s,hovRunFor(s,'HOVT-HK-FLOOR')).overdue,2);// hk2 09:00, hk4 10:00
    for(let i=0;i<10;i++)s=execute(s,'manager',{type:'HOV_CLOCK',minutes:120});
    assert.equal(s.clock,'2026-10-25T06:11');
    assert.throws(()=>execute(s,'manager',{type:'HOV_CLOCK',minutes:60}),/06:59/);
    assert.equal(hovProgress(s,hovRunFor(s,'HOVT-FD-OPEN')).late,true);});
  test('[HOV] day closed rejects ticks and notes',()=>{
    const s=seed();s.closedDates.push(s.date);
    assert.throws(()=>execute(s,'reception',{type:'HOV_ITEM',template:'HOVT-FD-OPEN',item:'fo1',done:true}),code('DAY_CLOSED'));
    assert.throws(()=>execute(s,'reception',{type:'HOV_NOTE_SUBMIT',desk:'Front desk',nothing:true}),code('DAY_CLOSED'));});
  test('[HOV] dashboard completion by role and desk',()=>{
    const s=seed(),byDesk=Object.fromEntries(hovDashboard(s,'2026-10-24','desk').map(r=>[r.key,r]));
    assert.equal(byDesk['Front desk'].runs,4);assert.equal(byDesk['Front desk'].done,2);assert.equal(byDesk['Front desk'].items,25);assert.equal(byDesk['Front desk'].pct,8);assert.equal(byDesk['Front desk'].notStarted,3);
    assert.equal(byDesk.Housekeeping.pct,14);assert.equal(byDesk.Maintenance.notStarted,1);
    const y=Object.fromEntries(hovDashboard(s,'2026-10-23','role').map(r=>[r.key,r]));
    assert.equal(y.Maintenance.submitted,1);assert.equal(y.Reception.runs,4);assert.equal(y.Reception.submitted,3);assert.equal(y.Reception.late,1);});
  test('[HOV] revoked grants remove access (coarse permissions respected)',()=>{
    const s=seed();s.grants.Housekeeper=[];
    assert.throws(()=>execute(s,'housekeeper',{type:'HOV_ITEM',template:'HOVT-HK-FLOOR',item:'hk2',photo:photoName}),code('FORBIDDEN'));
    assert.equal(hovMyRuns(s,'housekeeper').length,0);});
}
