// MA-6 · People lists in the production sync layer (src/clients/sync/masters.ts MST_SERVER) against a fake server shaped like the .NET DTOs
// (08 Production/src/Hcc.Contracts/PeopleLists/PeopleListsContracts.cs). Names prefixed [PPL-SYNC].
import {readFileSync} from 'node:fs';
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';
const readFileSource=f=>readFileSync(f,'utf8');
export async function register({test,M,assert}){
  const S=await loadSync();
  const mine={areas:S.AREAS.filter(a=>['core','masters'].includes(a.id))};
  const base=(id,idf,code,name,sortOrder,o={})=>({[idf]:id,code,name,sortOrder,active:true,retiredOn:null,used:2,updatedUtc:'2026-10-24T03:00:00.000Z',version:'V'+id,...o});
  const desig=[base(1,'designationId','COOK','Cook',10,{dept:'KIT',grade:null,defaultPayRule:'monthly-structured'}),base(2,'designationId','HELPER','Helper',20,{dept:'KIT',grade:'G1',defaultPayRule:null})];
  const shifts=[base(1,'shiftPatternId','M','Morning',10,{start:'07:00',end:'15:00',breakMinutes:30,night:false,weeklyOffRule:'fixed'}),
    base(2,'shiftPatternId','N','Night',20,{start:'23:00',end:'07:00',breakMinutes:30,night:true,weeklyOffRule:'roster',active:false,retiredOn:'2026-10-20'})];
  const leave=[base(1,'leaveTypeId','CL','Casual leave',10,{paid:true,yearlyQuota:12,carryForward:3,encashment:false}),base(2,'leaveTypeId','CO','Compensatory off',20,{paid:true,yearlyQuota:null,carryForward:0,encashment:false}),
    base(3,'leaveTypeId','LOP','Leave without pay',30,{paid:false,yearlyQuota:null,carryForward:0,encashment:false})];
  const hol=[base(1,'holidayId','2026-11-08','Diwali',10,{kind:'paid',fy:'FY 2026–27'}),base(2,'holidayId','2026-09-14','Ganesh Chaturthi',20,{kind:'restricted',fy:'FY 2026–27'})];
  const page=items=>({page:1,pageSize:200,total:items.length,items});
  const hist=(id,code,field,o,n)=>({historyId:id,rowId:1,code,field,oldValue:o,newValue:n,changedBy:1,changedByName:'owner',changedUtc:'2026-10-24T03:30:00.000Z'});
  const routes={'GET /hr/designations':()=>page(desig),'GET /hr/shift-patterns':()=>page(shifts),'GET /hr/leave-types':()=>page(leave),'GET /hr/holidays':()=>page(hol),
    'GET /hr/designations/history':()=>page([hist(9,'COOK','grade','—','G2')]),'GET /hr/shift-patterns/history':()=>page([hist(8,'M','start','06:30','07:00')]),
    'GET /hr/leave-types/history':()=>page([hist(7,'CL','yearlyQuota','10','12')]),'GET /hr/holidays/history':()=>page([hist(6,'2026-11-08','kind','restricted','paid')]),
    'GET /masters/floors':()=>page([]),'GET /masters/floors/history':()=>page([])};
  const cmds=['SaveDesignation','RetireDesignation','ReactivateDesignation','ReorderDesignations','DeleteDesignation','SaveShiftPattern','RetireShiftPattern','ReactivateShiftPattern','ReorderShiftPatterns','DeleteShiftPattern',
    'SaveLeaveType','RetireLeaveType','ReactivateLeaveType','ReorderLeaveTypes','DeleteLeaveType','SaveHoliday','RetireHoliday','ReactivateHoliday','ReorderHolidays','DeleteHoliday'];
  for(const n of cmds)routes['POST /commands/'+n]=()=>({});
  const srv=fakeServer(routes);
  const rt=await startRuntime(S,srv,mine);
  const st=rt.get().state,errors=rt.get().errors;

  test('[PPL-SYNC] Loaders fill mastersX.rows for the four lists with the prototype row shapes (ids = server ids, hidden / null fields left out, retired kept)',()=>{
    assert.deepEqual(srv.unrouted,[]);assert.deepEqual(errors,{});
    assert.deepEqual(st.mastersX.rows.designations,[{id:'1',code:'COOK',name:'Cook',sortOrder:10,active:true,dept:'KIT',defaultPayRule:'monthly-structured'},{id:'2',code:'HELPER',name:'Helper',sortOrder:20,active:true,dept:'KIT',grade:'G1'}]);
    assert.deepEqual(st.mastersX.rows.shiftPatterns,[{id:'1',code:'M',name:'Morning',sortOrder:10,active:true,start:'07:00',end:'15:00',breakMinutes:30,night:false,weeklyOffRule:'fixed'},
      {id:'2',code:'N',name:'Night',sortOrder:20,active:false,retiredOn:'2026-10-20',start:'23:00',end:'07:00',breakMinutes:30,night:true,weeklyOffRule:'roster'}]);
    assert.deepEqual(st.mastersX.rows.leaveTypes.map(r=>[r.code,r.paid,r.yearlyQuota,r.carryForward,r.encashment]),[['CL',true,12,3,false],['CO',true,undefined,0,false],['LOP',false,undefined,0,false]]);
    assert.deepEqual(st.mastersX.rows.holidays.map(r=>[r.code,r.name,r.kind]),[['2026-11-08','Diwali','paid'],['2026-09-14','Ganesh Chaturthi','restricted']]);
    assert.equal(rt.ids.version('designation','2'),'V2');assert.equal(rt.ids.version('shiftPattern','1'),'V1');assert.equal(rt.ids.version('leaveType','3'),'V3');assert.equal(rt.ids.version('holiday','2'),'V2');});
  test('[PPL-SYNC] The change history of every list loads into mastersX.history (field old → new, IST clock)',()=>{
    assert.deepEqual(st.mastersX.history.map(h=>[h.master,h.field,h.old,h.new]).sort(),[['designations','grade','—','G2'],['holidays','kind','restricted','paid'],['leaveTypes','yearlyQuota','10','12'],['shiftPatterns','start','06:30','07:00']].sort());
    assert.equal(st.mastersX.history[0].at.length,16);});
  test('[PPL-SYNC] The prototype selectors read the server rows: shift times, leave quotas and paid flags, holiday dates, designation names',()=>{
    assert.equal(M.hrShiftInfo('M',st).start,'07:00');assert.equal(M.hrShiftInfo('N',st).label,'Night');assert.equal(M.hrWeeklyOffAuto(st,'N'),false);assert.equal(M.hrWeeklyOffAuto(st,'M'),true);
    assert.equal(M.hrLeaveQuota(st,'CL'),12);assert.equal(M.hrLeaveCarryCap(st,'CL'),3);assert.equal(M.hrLeavePaid(st,'LOP'),false);assert.deepEqual(M.hrTrackedTypes(st),['CL']);
    assert.equal(M.hrHolidayPaid(st,'2026-11-08'),true);assert.equal(M.hrHolidayPaid(st,'2026-09-14'),false);assert.deepEqual([...M.hrHolidayDates(st)],['2026-11-08']);
    assert.deepEqual(M.mstOptions(st,'designations').map(o=>o.value),['COOK','HELPER']);assert.equal(M.mstLabel(st,'designations','COOK'),'Cook');});

  const seq=[];
  const go=async cmd=>{srv.reset();const r=await rt.run(cmd);seq.push({r,cmds:srv.calls.filter(c=>c.method==='POST'),gets:srv.gets().map(c=>c.path)});return seq.at(-1);};
  const dAdd=await go({type:'MST_MASTER_SAVE',master:'designations',row:{code:' chef ',name:' Chef ',dept:'KIT',grade:'G3',defaultPayRule:'monthly-basic'},key:'d-add'});
  const dEdit=await go({type:'MST_MASTER_SAVE',master:'designations',row:{id:'1',code:'COOK',name:'Senior cook',dept:'KIT'}});     // pay rule hidden from this role: not sent
  const sAdd=await go({type:'MST_MASTER_SAVE',master:'shiftPatterns',row:{code:'s',name:'Split',start:'10:00',end:'20:00',breakMinutes:60,night:false,weeklyOffRule:'roster'},key:'s-add'});
  const sEdit=await go({type:'MST_MASTER_SAVE',master:'shiftPatterns',row:{id:'1',code:'M',name:'Morning',start:'08:00',end:'16:00',breakMinutes:30,night:false,weeklyOffRule:'fixed'}});
  const lAdd=await go({type:'MST_MASTER_SAVE',master:'leaveTypes',row:{code:'bl',name:'Bereavement',paid:true,yearlyQuota:3,carryForward:0,encashment:false}});
  const lEdit=await go({type:'MST_MASTER_SAVE',master:'leaveTypes',row:{id:'3',code:'LOP',name:'Unpaid',paid:false}});
  const hAdd=await go({type:'MST_MASTER_SAVE',master:'holidays',row:{code:'2026-12-25',name:'Christmas',kind:'paid'}});
  const ret=await go({type:'MST_MASTER_RETIRE',master:'shiftPatterns',id:'1'});
  const back=await go({type:'MST_MASTER_REACTIVATE',master:'leaveTypes',id:'2'});
  const del=await go({type:'MST_MASTER_DELETE',master:'holidays',id:'2'});
  const ord=await go({type:'MST_MASTER_REORDER',master:'designations',ids:['2','1']});
  test('[PPL-SYNC] Save commands map to SaveDesignation / SaveShiftPattern / SaveLeaveType / SaveHoliday with the list fields, the dialog key and a reload',()=>{
    assert.ok(dAdd.r.ok);assert.deepEqual([dAdd.cmds[0].path,dAdd.cmds[0].key],['/commands/SaveDesignation','d-add']);
    assert.deepEqual(dAdd.cmds[0].body,{code:'CHEF',name:'Chef',dept:'KIT',grade:'G3',defaultPayRule:'monthly-basic'});assert.ok(dAdd.gets.includes('/hr/designations'));
    assert.deepEqual(sAdd.cmds[0].body,{code:'S',name:'Split',start:'10:00',end:'20:00',breakMinutes:60,night:false,weeklyOffRule:'roster'});assert.equal(sAdd.cmds[0].path,'/commands/SaveShiftPattern');
    assert.deepEqual(lAdd.cmds[0].body,{code:'BL',name:'Bereavement',paid:true,yearlyQuota:3,carryForward:0,encashment:false});assert.equal(lAdd.cmds[0].path,'/commands/SaveLeaveType');
    assert.deepEqual([hAdd.cmds[0].path,hAdd.cmds[0].body],['/commands/SaveHoliday',{code:'2026-12-25',name:'Christmas',kind:'paid'}]);});
  test('[PPL-SYNC] Edits send the id field and the loaded version; a designation edit without the (hidden) pay rule leaves it out; empty quota is sent as null',()=>{
    assert.deepEqual(dEdit.cmds[0].body,{code:'COOK',name:'Senior cook',designationId:1,expectedVersion:'V1',dept:'KIT',grade:null});
    assert.deepEqual(sEdit.cmds[0].body,{code:'M',name:'Morning',shiftPatternId:1,expectedVersion:'V1',start:'08:00',end:'16:00',breakMinutes:30,night:false,weeklyOffRule:'fixed'});
    assert.deepEqual(lEdit.cmds[0].body,{code:'LOP',name:'Unpaid',leaveTypeId:3,expectedVersion:'V3',paid:false,yearlyQuota:null,carryForward:0,encashment:false});});
  test('[PPL-SYNC] Retire / reactivate / delete / reorder map to the list commands with the right id field',()=>{
    assert.deepEqual([ret.cmds[0].path,ret.cmds[0].body],['/commands/RetireShiftPattern',{shiftPatternId:1,expectedVersion:'V1'}]);
    assert.deepEqual([back.cmds[0].path,back.cmds[0].body],['/commands/ReactivateLeaveType',{leaveTypeId:2,expectedVersion:'V2'}]);
    assert.deepEqual([del.cmds[0].path,del.cmds[0].body],['/commands/DeleteHoliday',{holidayId:2,expectedVersion:'V2'}]);
    assert.deepEqual([ord.cmds[0].path,ord.cmds[0].body],['/commands/ReorderDesignations',{designationIds:[2,1]}]);
    for(const x of [ret,back,del,ord])assert.ok(x.r.ok&&x.gets.includes('/hr/designations')&&x.gets.includes('/hr/holidays'));});
  test('[PPL-SYNC] Read gates and capabilities: the four lists are readable by every signed-in role, their history needs PERM-MST-001',()=>{
    const reads=readFileSource('src/clients/sync/reads.ts');
    for(const r of ['designations','shift-patterns','leave-types','holidays']){assert.match(reads,new RegExp(`'GET /hr/${r}':always`));assert.match(reads,new RegExp(`'GET /hr/${r}/history':one\\(wide\\('PERM-MST-001'\\)\\)`));}
    assert.equal(S.AREAS.find(a=>a.id==='masters').commands.length,6);}); // MA-8 old → new: 5 → 6 (MST_IMPORT)

  test('[PPL-SYNC] Staff: the designation code and any shift code come across; SaveStaff carries the designation code only when one was chosen',()=>{
    const row={staffId:7,name:'Asha',dept:'KIT',designation:'Head chef',defaultShift:'S1',weeklyOff:2,joined:'2025-04-01',exitedOn:null,active:true,linked:false,phoneMasked:'••••1234',version:'v1',
      payType:'monthly',monthlyPaise:1800000,dailyPaise:0,gender:'F',structure:null,pfApplicable:null,esicApplicable:null,bankName:null,accountMasked:null,designationCode:'CHEF'};
    const st1=S.staffFromServer(row);assert.deepEqual([st1.defaultShift,st1.designationCode],['S1','CHEF']);
    assert.equal(S.staffFromServer({...row,designationCode:null}).designationCode,undefined);
    const src=readFileSource('src/clients/sync/staff.ts');assert.match(src,/\.\.\.\(cmd\.designationCode\?\{designationCode:String\(cmd\.designationCode\)\}:\{\}\)/);assert.match(src,/\.\.\.\(c\.hol\?\{hol:c\.hol\}:\{\}\)/);});

  // Server master errors keep their reason; Reception loads the lists without the history reads.
  srv.on('POST /commands/SaveHoliday',()=>reply(403,{code:'FORBIDDEN',message:'Access denied.'}));
  const denied=await rt.run({type:'MST_MASTER_SAVE',master:'holidays',row:{code:'2026-12-31',name:'New year eve',kind:'paid'}});
  const rec=fakeServer({...routes,'GET /me/capabilities':()=>capabilities({roles:['REC'],grants:[['PERM-RES-001','A']]})});
  const rrt=await startRuntime(S,rec,mine);
  test('[PPL-SYNC] A refused save surfaces as a server error; Reception reads the lists but never asks for the history',()=>{
    assert.deepEqual([denied.ok,denied.reason],[false,'server']);
    assert.deepEqual(rec.gets().map(c=>c.path).filter(p=>p.startsWith('/hr')),['/hr/designations','/hr/shift-patterns','/hr/leave-types','/hr/holidays']);
    assert.equal(rrt.get().state.mastersX.rows.shiftPatterns.length,2);assert.deepEqual(rrt.get().state.mastersX.history,[]);assert.deepEqual(rrt.get().errors,{});});

  // Every page renders on the server state and on a prototype state with the lists saved.
  let proto=M.execute(M.seed(),'owner',{type:'MST_MASTER_SAVE',master:'shiftPatterns',row:{code:'S',name:'Split',start:'10:00',end:'20:00'}});
  proto=M.execute(proto,'owner',{type:'MST_MASTER_SAVE',master:'holidays',row:{code:'2026-10-25',name:'Local holiday',kind:'paid'}});
  const P=await loadPages();
  const shown=[P.renderPages(st,'1',false),P.renderPages(st,'1',true),P.renderPages(proto,'owner',false),P.renderPages(proto,'owner',true),P.renderPages(proto,'techadmin',false),P.renderPages(proto,'housekeeper',true)];
  test('[PPL-SYNC] Every desktop and phone page renders on the loaded server lists and on a state with a custom pattern and a holiday today',()=>{
    assert.deepEqual(shown.map(x=>x.errors),[{},{},{},{},{},{}]);assert.ok(shown.every(x=>x.rendered>5));});
}
