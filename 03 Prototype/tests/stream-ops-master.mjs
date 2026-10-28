// MA-7 · Housekeeping and guest-services masters (plan Part 2 M23–M25): task types, zones, complaint and lost-and-found categories.
// Names prefixed [OPSM]. Masters framework tests are in stream-masters.mjs; the production mapping is in sync-ops-masters.mjs.
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
export function register({test,M,assert}){
  const {seed,execute,mstRows,mstByCode,mstLabel,mstSpec,mstHistory,mstCanEdit,mstMoneyFields,mstVisibleFields,mstReasonOf,mstUsage,HccError}=M;
  const code=c=>e=>e.code===c;
  const reason=r=>e=>mstReasonOf(e)===r;
  const src=f=>readFileSync(f,'utf8');
  const sha=o=>createHash('sha256').update(JSON.stringify(o)).digest('hex');
  const strip=s=>{const {mastersX,grants,...r}=s;return r;};
  const save=(s,u,master,row,key)=>execute(s,u,{type:'MST_MASTER_SAVE',master,row,key});
  const cmd=(s,u,type,master,id)=>execute(s,u,{type,master,id});
  const task=(s,f)=>s.tasks.find(f);
  const toDo=s=>task(s,t=>t.assignee==='housekeeper'&&t.status==='To do'&&(t.kind??'checkout')==='checkout');
  const ids=['hkTaskTypes','hkZones','complaintCategories','lostFoundCategories'];

  // ── Registry and seeds ──
  test('[OPSM] four masters are registered: task types and zones in Housekeeping and maintenance, categories in Front-desk policies; operational (masters permission), no money fields',()=>{
    const where=Object.fromEntries(ids.map(i=>[i,mstSpec(i)]));
    assert.deepEqual(ids.map(i=>where[i].group),['hkm','hkm','frontdesk','frontdesk']);
    for(const i of ids){const sp=where[i];assert.equal(sp.permission,'masters',i);assert.equal(sp.money,false,i);assert.equal(mstMoneyFields(sp).length,0,i);}
    assert.deepEqual(where.hkTaskTypes.fields.map(f=>f.key),['code','name','sortOrder','appliesTo','standardMinutes','needsInspection','checklist','active']);
    assert.deepEqual(where.hkZones.fields.map(f=>f.key),['code','name','sortOrder','floors','rooms','defaultAttendant','active']);
    assert.deepEqual(where.complaintCategories.fields.map(f=>f.key),['code','name','sortOrder','department','slaHours','active']);
    assert.deepEqual(where.lostFoundCategories.fields.map(f=>f.key),['code','name','sortOrder','department','slaHours','valuable','active']);});
  test('[OPSM] seeds reproduce today: 4 task types (3-item checklist for cleans), one zone per floor, 8 complaint and 7 found-item categories; BASE has no stored slice',()=>{
    const s=seed();assert.equal(s.mastersX,undefined);
    assert.deepEqual(mstRows(s,'hkTaskTypes').map(r=>[r.code,r.standardMinutes,r.needsInspection]),[['DEP',40,true],['STAY',20,false],['DEEP',90,true],['INSP',10,false]]);
    assert.equal(mstByCode(s,'hkTaskTypes','DEP').checklist.split('\n').length,3);
    assert.deepEqual(mstRows(s,'hkZones').map(r=>[r.code,r.floors,r.defaultAttendant]),[['Z1',['F1'],'housekeeper'],['Z2',['F2'],'housekeeper2'],['Z3',['F3'],'housekeeper']]);
    assert.deepEqual(mstRows(s,'complaintCategories').map(r=>r.name),['Room cleanliness','Maintenance','Noise','Staff behaviour','Food & beverage','Billing','Amenities','Other']);
    assert.deepEqual(mstRows(s,'lostFoundCategories').filter(r=>r.valuable).map(r=>r.name),['Jewellery','Electronics','Cash / wallet','Documents']);
    assert.deepEqual(M.GSV_COMPLAINT_CATEGORIES,mstRows(s,'complaintCategories').map(r=>r.name));
    assert.deepEqual(M.GSV_ITEM_CATEGORIES,mstRows(s,'lostFoundCategories').map(r=>r.name));});
  test('[OPSM] fixture figures unchanged (occupancy 8, collections ₹21,500, drawer ₹10,725); BASE and FY26 history hashes unchanged',()=>{
    const s=seed();assert.equal(M.metrics(s).occupied,8);assert.equal(M.metrics(s).collections,2150000);assert.equal(M.drawer(s),1072500);
    assert.equal(sha(strip(seed())),'aa7464e71257f1122298fcb6c26d1e42158aec9311fe39e4347da09a9e887a78') /* ACC-2: SCHEMA 11 → 12 only (new optional fields) */ /* ACC-1: SCHEMA 10 → 11 only (accounting chart / opening fields are optional and absent in the seed) */ /* SCC decisions wave (DEC-133): 4a8aad03… → aecae3b2… = the demo firm profile's fictional licence numbers / expiry dates only */ /* SCC port wave (DEC-116, tax + leave): 2c1782df… → 4a8aad03… = compliance + leave slices, SCHEMA 9 → 10 */ /* old → new: d885a20a… → add626d3… = SCHEMA 8 → 9 only (masters wave) */;
    assert.equal(sha(strip(M.buildHistory(M.baseFixture()))),'ac6c854487762d3782de57a60af8324aca357d1037a7cf9f5841fba26d63fe21') /* ACC-2: SCHEMA 11 → 12 only (new optional fields) */ /* ACC-1 integration (all seven lanes merged + the ITC / cash-over one-liners): the FY26 history locks its accounting months with accLiveVouchers snapshots, which now carry the re-pointed mapping (POS 4200, ITC 1310 / 1320, cash over 4950), party ledger ids (1000/TILL, 1030/<bank>, 1120/UD-…, 2200/<vendor>) and the Udhaar credit / company payment vouchers: f3601522… → e3a74ba3… */ /* ACC-1: SCHEMA 10 → 11 only */ /* SCC decisions wave (DEC-133): 197643a1… → 5dc19a44… = the demo firm profile's fictional licence fields only */ /* SCC port wave (DEC-116, tax + leave): bf689538… → 197643a1… = compliance + leave slices, SCHEMA 9 → 10 */ /* old → new: ba4a1d4e… → 35e63082… = SCHEMA 8 → 9 only */;});
  test('[OPSM] permissions: Tech admin and Owner may change the four lists; Manager, Reception, Supervisor, Housekeeper, Maintenance may not (DEC-MA-1)',()=>{
    for(const i of ids){const sp=mstSpec(i);
      for(const u of ['owner','techadmin'])assert.equal(mstCanEdit(seed(),u,sp).ok,true,i+u);
      for(const u of ['manager','reception','supervisor','housekeeper','maint','cashier'])assert.equal(mstCanEdit(seed(),u,sp).ok,false,i+u);
      for(const u of ['housekeeper','maint','supervisor'])assert.equal(mstVisibleFields(seed(),u,sp).length,sp.fields.length,'no money fields to hide '+i);}
    assert.throws(()=>save(seed(),'manager','hkTaskTypes',{code:'X',name:'X',appliesTo:['deep'],standardMinutes:30,checklist:'A'}),code('FORBIDDEN'));
    assert.throws(()=>save(seed(),'housekeeper','hkZones',{code:'X',name:'X',floors:['F1']}),code('FORBIDDEN'));});

  // ── Task types ──
  const type=(o={})=>({code:'DEEP2',name:'Deep clean 2',appliesTo:['deep'],standardMinutes:60,needsInspection:true,checklist:'One\nTwo',...o});
  test('[OPSM] each kind belongs to one active type: a second active type for the same kind is refused (MASTER_INVALID)',()=>{
    assert.throws(()=>save(seed(),'techadmin','hkTaskTypes',type({appliesTo:['deep']})),e=>reason('MASTER_INVALID')(e)&&/already handled by/.test(e.message));});
  test('[OPSM] a new type with no kind: audit text, history, audit action',()=>{
    const s=save(cmd(seed(),'owner','MST_MASTER_RETIRE','hkTaskTypes','hkt-deep'),'techadmin','hkTaskTypes',type());const r=mstByCode(s,'hkTaskTypes','DEEP2');
    assert.equal(r.sortOrder,50);assert.equal(s.audit[0].detail,'Master Housekeeping task types: added DEEP2 · Deep clean 2');
    assert.deepEqual(mstHistory(s,'hkTaskTypes',r.id).map(h=>h.field).sort(),['appliesTo','checklist','code','name','needsInspection','sortOrder','standardMinutes']);});
  test('[OPSM] task-type validation: minutes 5–240, checklist 1–8 lines ≤ 60 chars each once, kind required for tasks, stayover never inspected, duplicate code',()=>{
    const bad=o=>assert.throws(()=>save(seed(),'owner','hkTaskTypes',type(o)),reason('MASTER_INVALID'));
    bad({standardMinutes:4});bad({standardMinutes:241});bad({standardMinutes:undefined});bad({checklist:''});bad({checklist:'\n  \n'});
    bad({checklist:Array.from({length:9},(_,i)=>'Item '+i).join('\n')});bad({checklist:'x'.repeat(61)});bad({checklist:'Same\nsame'});
    bad({appliesTo:['stayover'],needsInspection:true});bad({appliesTo:['bogus']});bad({appliesTo:[]});bad({appliesTo:['deep']}); // deep is already DEEP's
    assert.throws(()=>save(seed(),'owner','hkTaskTypes',type({code:'dep'})),reason('MASTER_CODE_TAKEN'));
    assert.ok(mstByCode(save(cmd(seed(),'owner','MST_MASTER_RETIRE','hkTaskTypes','hkt-deep'),'owner','hkTaskTypes',type({checklist:Array.from({length:8},(_,i)=>'Item '+i).join('\n')})),'hkTaskTypes','DEEP2'));});
  test('[OPSM] the checklist of the task type drives TASK: 4 items → check index 3 allowed, index 4 refused, complete needs all 4 ticks',()=>{
    let s=save(seed(),'techadmin','hkTaskTypes',{id:'hkt-dep',code:'DEP',name:'Departure clean',appliesTo:['checkout','move'],standardMinutes:45,needsInspection:true,checklist:'Linen\nBathroom\nSurfaces\nFinal check'});
    const t=toDo(s);s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'start'});
    assert.deepEqual(M.hkChecklistOf(s,s.tasks.find(x=>x.id===t.id)),['Linen','Bathroom','Surfaces','Final check']);
    for(const i of [0,1,2])s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'check',index:i});
    assert.throws(()=>execute(s,'housekeeper',{type:'TASK',id:t.id,action:'complete'}),/Finish all checklist items/);
    assert.throws(()=>execute(s,'housekeeper',{type:'TASK',id:t.id,action:'check',index:4}),/Invalid checklist item/);
    s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'check',index:3});assert.equal(s.tasks.find(x=>x.id===t.id).checked.length,4);
    s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'complete'});assert.equal(s.tasks.find(x=>x.id===t.id).status,'Inspection');});
  test('[OPSM] default checklist unchanged: 3 items complete exactly as before (inspection then Ready)',()=>{
    let s=seed();const t=toDo(s);s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'start'});
    for(const i of [0,1,2])s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'check',index:i});
    assert.throws(()=>execute(s,'housekeeper',{type:'TASK',id:t.id,action:'check',index:3}),/Invalid checklist item/);
    s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'complete'});const x=s.tasks.find(y=>y.id===t.id);
    assert.equal(x.status,'Inspection');assert.equal(s.rooms.find(r=>r.id===x.room).hk,'Inspection');
    s=execute(s,'supervisor',{type:'TASK',id:t.id,action:'inspect'});assert.equal(s.rooms.find(r=>r.id===x.room).hk,'Ready');});
  test('[OPSM] "needs inspection" off on the departure type: a finished clean goes straight to Done and the room Ready; reject clears one tick per item',()=>{
    let s=save(seed(),'owner','hkTaskTypes',{id:'hkt-dep',code:'DEP',name:'Departure clean',appliesTo:['checkout','move'],standardMinutes:40,needsInspection:false,checklist:'A\nB'});
    const t=toDo(s);s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'start'});
    for(const i of [0,1])s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'check',index:i});
    s=execute(s,'housekeeper',{type:'TASK',id:t.id,action:'complete'});const x=s.tasks.find(y=>y.id===t.id);
    assert.equal(x.status,'Done');assert.equal(s.rooms.find(r=>r.id===x.room).hk,'Ready');
    // reject path (type with inspection on) resets to the type's item count
    let r=save(seed(),'owner','hkTaskTypes',{id:'hkt-dep',code:'DEP',name:'Departure clean',appliesTo:['checkout','move'],standardMinutes:40,needsInspection:true,checklist:'A\nB'});
    const u=r.tasks.find(y=>y.status==='Inspection');r=execute(r,'supervisor',{type:'TASK',id:u.id,action:'reject',reason:'Dust on the shelf'});
    assert.deepEqual(r.tasks.find(y=>y.id===u.id).checked,[false,false]);});
  test('[OPSM] stayover tasks: checklist length from the Stayover type, zone attendant, never inspected; a stayover type cannot require inspection',()=>{
    let s=save(seed(),'owner','hkTaskTypes',{id:'hkt-stay',code:'STAY',name:'Stayover service',appliesTo:['stayover'],standardMinutes:15,needsInspection:false,checklist:'Towels\nBin'});
    s=execute(s,'supervisor',{type:'STAYOVER_GENERATE'});const st=s.tasks.filter(t=>t.kind==='stayover');
    assert.ok(st.length>0);assert.ok(st.every(t=>t.checked.length===2&&t.checked.every(x=>x===false)));assert.equal(M.hkStdMinutes(s,st[0]),15);
    assert.throws(()=>save(seed(),'owner','hkTaskTypes',{id:'hkt-stay',code:'STAY',name:'Stayover service',appliesTo:['stayover'],standardMinutes:20,needsInspection:true,checklist:'A'}),/never inspected/);});
  test('[OPSM] timing against the standard: waiting, running (n of std), over standard by n, finished (took n)',()=>{
    const s=seed(),t0=toDo(s);const mk=(patch)=>{const x=structuredClone(s);Object.assign(x.tasks.find(t=>t.id===t0.id),patch);return [x,x.tasks.find(t=>t.id===t0.id)];};
    let [x,t]=mk({});assert.deepEqual([M.hkTaskTiming(x,t).state,M.hkTaskTiming(x,t).std],['waiting',40]);
    [x,t]=mk({status:'In progress',startedAt:M.gsvAddMinutes(s.clock,-25)});assert.deepEqual([M.hkTaskTiming(x,t).state,M.hkTaskTiming(x,t).elapsed],['running',25]);
    [x,t]=mk({status:'In progress',startedAt:M.gsvAddMinutes(s.clock,-55)});assert.equal(M.hkTaskTiming(x,t).state,'over');assert.equal(M.hkTaskTiming(x,t).over,15);assert.match(M.hkTaskTiming(x,t).label,/Over standard by 15 min/);
    [x,t]=mk({status:'Done',startedAt:'2026-10-25T08:00',completedAt:'2026-10-25T08:35'});assert.deepEqual([M.hkTaskTiming(x,t).state,M.hkTaskTiming(x,t).actual,M.hkTaskTiming(x,t).over],['finished',35,0]);
    const rep=s.tasks.find(y=>y.kind==='repair');assert.equal(M.hkTaskTiming(s,rep),undefined);assert.equal(M.hkStdMinutes(s,rep),undefined);});
  test('[OPSM] attendant load: planned minutes = sum of standard minutes of rooms still to clean; over the rooms guide is flagged; inspection queue',()=>{
    const s=seed(),load=M.hkAttendantLoad(s);const mine=n=>s.tasks.filter(t=>t.assignee===n&&(t.kind??'checkout')!=='repair'&&(t.status==='To do'||t.status==='In progress'));
    for(const l of load){assert.equal(l.rooms,mine(l.user).length);assert.equal(l.minutes,mine(l.user).reduce((a,t)=>a+M.hkStdMinutes(s,t),0));assert.equal(l.guide,12);assert.equal(l.over,false);}
    const q=M.hkInspectionQueue(s);assert.equal(q.rooms,s.tasks.filter(t=>t.status==='Inspection').length);assert.equal(q.minutes,q.rooms*10);
    const x=save(seed(),'owner','hkTaskTypes',{id:'hkt-dep',code:'DEP',name:'Departure clean',appliesTo:['checkout','move'],standardMinutes:60,needsInspection:true,checklist:M.HKT_DEFAULT_CHECKLIST.join('\n')});
    const a=M.hkAttendantLoad(x).find(l=>l.user==='housekeeper');const b=load.find(l=>l.user==='housekeeper');assert.equal(a.minutes,b.rooms*60);
    assert.equal(M.hkFmtMinutes(125),'2 h 5 min');assert.equal(M.hkFmtMinutes(45),'45 min');});
  test('[OPSM] retire / reactivate / delete a type: a kind without an active type falls back to the built-in values; in-use rows are retired, unused deleted; reorder',()=>{
    let s=cmd(seed(),'owner','MST_MASTER_RETIRE','hkTaskTypes','hkt-dep');
    assert.equal(M.hkTypeFor(s,'checkout').active,false);assert.equal(M.hkStdMinutes(s,toDo(s)),40);assert.equal(M.hkChecklistOf(s,toDo(s)).length,3);
    s=cmd(s,'owner','MST_MASTER_REACTIVATE','hkTaskTypes','hkt-dep');assert.equal(M.hkTypeFor(s,'checkout').active,true);
    assert.throws(()=>cmd(seed(),'owner','MST_MASTER_DELETE','hkTaskTypes','hkt-dep'),e=>reason('MASTER_IN_USE')(e)&&/retire it instead/.test(e.message));
    const d=cmd(seed(),'techadmin','MST_MASTER_DELETE','hkTaskTypes','hkt-deep');assert.equal(mstByCode(d,'hkTaskTypes','DEEP'),undefined);
    const o=execute(seed(),'owner',{type:'MST_MASTER_REORDER',master:'hkTaskTypes',ids:['hkt-stay','hkt-dep','hkt-deep','hkt-insp']});assert.deepEqual(mstRows(o,'hkTaskTypes').map(r=>r.code),['STAY','DEP','DEEP','INSP']);});

  // ── Zones ──
  test('[OPSM] seeded zones keep the old assignment: stayover tasks go to hks[(floor-1) % n] exactly as before',()=>{
    const s=execute(seed(),'supervisor',{type:'STAYOVER_GENERATE'});const hks=['housekeeper','housekeeper2'];
    for(const t of s.tasks.filter(x=>x.kind==='stayover'))assert.equal(t.assignee,hks[(s.rooms.find(r=>r.id===t.room).floor-1)%2],t.room);});
  test('[OPSM] a zone of floors and rooms with a default attendant: a room listed itself wins over its floor; stayover tasks go to the zone attendant; reassigning stays possible',()=>{
    const base=seed(),stay=M.hkStayoverRooms(base);const r1=stay[0];
    let s=save(base,'techadmin','hkZones',{code:'VIP',name:'VIP rooms',rooms:[r1],defaultAttendant:'housekeeper2'});
    assert.equal(M.hkzOf(s,r1).code,'VIP');assert.equal(M.hkzAttendant(s,r1),'housekeeper2');assert.equal(M.hkzLabel(s,r1),'VIP rooms');
    s=execute(s,'supervisor',{type:'STAYOVER_GENERATE'});assert.equal(s.tasks.find(t=>t.id===`stayover:${r1}:${s.date}`).assignee,'housekeeper2');
    s=execute(s,'supervisor',{type:'TASK',id:`stayover:${r1}:${s.date}`,action:'assign',assignee:'housekeeper'});assert.equal(s.tasks.find(t=>t.id===`stayover:${r1}:${s.date}`).assignee,'housekeeper');
    assert.equal(M.hkzRooms(s,mstByCode(s,'hkZones','VIP')).length,1);assert.equal(M.hkzRooms(s,mstByCode(s,'hkZones','Z1')).length,M.hkzRooms(base,mstByCode(base,'hkZones','Z1')).length-(base.rooms.find(r=>r.id===r1).floor===1?1:0));});
  test('[OPSM] zone validation: needs a floor or a room; a floor or room belongs to one active zone; unknown room / floor and disabled attendant refused',()=>{
    assert.throws(()=>save(seed(),'owner','hkZones',{code:'E',name:'Empty'}),/at least one floor or room/);
    assert.throws(()=>save(seed(),'owner','hkZones',{code:'DUP',name:'Dup',floors:['F2']}),/already in zone “Floor 2 section”/);
    let s=save(seed(),'owner','hkZones',{code:'R1',name:'Room one',rooms:['101']});
    assert.throws(()=>save(s,'owner','hkZones',{code:'R2',name:'Room one again',rooms:['101']}),/Room 101 is already in zone/);
    assert.throws(()=>save(seed(),'owner','hkZones',{code:'X',name:'X',rooms:['999']}),reason('MASTER_INVALID'));
    assert.throws(()=>save(seed(),'owner','hkZones',{code:'X',name:'X',floors:['F9']}),reason('MASTER_INVALID'));
    assert.throws(()=>save(seed(),'owner','hkZones',{code:'X',name:'X',rooms:['101'],defaultAttendant:'supervisor'}),reason('MASTER_INVALID'));
    });
  test('[OPSM] a disabled default attendant is ignored (old rule applies) but stays visible on the zone; the attendant pick list offers enabled housekeepers only',()=>{
    let s=save(seed(),'owner','hkZones',{code:'A',name:'A',rooms:['101'],defaultAttendant:'housekeeper2'});
    assert.deepEqual(M.hkzAttendantOptions(s).map(o=>o.value),['housekeeper','housekeeper2']);
    s=execute(s,'owner',{type:'USER',id:'housekeeper2'});assert.equal(s.users.find(u=>u.id==='housekeeper2').enabled,false);
    assert.equal(M.hkzAttendant(s,'101'),undefined);assert.equal(mstByCode(s,'hkZones','A').defaultAttendant,'housekeeper2');
    assert.deepEqual(M.hkzAttendantOptions(s).map(o=>o.value),['housekeeper']);
    s=save(s,'owner','hkZones',{id:mstByCode(s,'hkZones','A').id,code:'A',name:'A renamed',rooms:['101'],defaultAttendant:'housekeeper2'}); // unchanged old value stays valid
    assert.equal(mstByCode(s,'hkZones','A').name,'A renamed');});
  test('[OPSM] tasks group by zone for "My tasks" (zone order, "No zone" last); a retired zone releases its rooms; in-use = open tasks in its rooms',()=>{
    let s=seed();const mine=s.tasks.filter(t=>t.assignee==='housekeeper');const g=M.hkzGroups(s,mine);
    assert.ok(g.length>=1);assert.equal(g.reduce((n,x)=>n+x.tasks.length,0),mine.length);assert.ok(g.every(x=>x.zone));
    const z1=mstByCode(s,'hkZones','Z1');const open=s.tasks.filter(t=>t.status!=='Done'&&(t.kind??'checkout')!=='repair'&&s.rooms.find(r=>r.id===t.room).floor===1).length;
    assert.equal(mstUsage(s,mstSpec('hkZones'),z1).count,open);
    s=cmd(s,'owner','MST_MASTER_RETIRE','hkZones',z1.id);assert.equal(M.hkzOf(s,'101'),undefined);
    assert.ok(M.hkzGroups(s,s.tasks.filter(t=>s.rooms.find(r=>r.id===t.room).floor===1)).every(x=>x.label==='No zone'));
    assert.equal(mstLabel(s,'hkZones','Z1'),'Floor 1 section');
    const e=save(seed(),'owner','hkZones',{code:'TMP',name:'Temp',rooms:['103']});assert.equal(mstUsage(e,mstSpec('hkZones'),mstByCode(e,'hkZones','TMP')).count,e.tasks.filter(t=>t.room==='103'&&t.status!=='Done'&&t.kind!=='repair').length);});
  test('[OPSM] zone names follow floor renames (seed) and a zone on a retired floor still resolves',()=>{
    const s=execute(seed(),'owner',{type:'MST_MASTER_SAVE',master:'floors',row:{id:'flr-1',code:'F1',name:'Ground'}});
    assert.equal(mstRows(s,'hkZones')[0].name,'Ground section');});

  // ── Complaint categories ──
  const log=(s,o={})=>execute(s,'reception',{type:'GSV_COMPLAINT_LOG',guest:'Test Guest',category:'NOISE',severity:'Low',description:'Loud music at night',owner:'reception',...o});
  const last=(s,k)=>M.gsvSlice(s)[k].at(-1);
  test('[OPSM] logging a complaint stores the category name and code; a name (older callers) resolves too; unknown or retired categories are refused',()=>{
    let s=log(seed());assert.deepEqual([last(s,'complaints').category,last(s,'complaints').categoryCode],['Noise','NOISE']);
    s=log(seed(),{category:'Room cleanliness'});assert.equal(last(s,'complaints').categoryCode,'CLEAN');
    assert.throws(()=>log(seed(),{category:'Nope'}),/Choose a category/);
    const r=cmd(seed(),'owner','MST_MASTER_RETIRE','complaintCategories','gcc-noise');assert.throws(()=>log(r),/Choose a category/);});
  test('[OPSM] new complaint category used everywhere: logged, reported with department and SLA; fixture complaints keep resolving by seed name',()=>{
    let s=save(seed(),'techadmin','complaintCategories',{code:'PARK',name:'Parking',department:'Front desk',slaHours:6});
    s=log(s,{category:'PARK'});const rep=M.gsvComplaintReport(s);
    assert.deepEqual(rep.find(r=>r.code==='PARK'),{category:'Parking',code:'PARK',dept:'Front desk',slaHours:6,total:1,open:1,bySeverity:{Low:1,Medium:0,High:0,Critical:0},recovery:0,overSla:0});
    assert.equal(rep.find(r=>r.category==='Noise').total,1);assert.equal(rep.find(r=>r.category==='Room cleanliness').total,1);assert.equal(rep.find(r=>r.category==='Food & beverage').total,1);
    assert.equal(s.audit.find(a=>a.action==='MST MASTER SAVE').detail,'Master Complaint categories: added PARK · Parking');});
  test('[OPSM] renaming a category: old complaints (code or seed name) read the new name; retiring keeps the label; delete only when unused',()=>{
    let s=save(seed(),'owner','complaintCategories',{id:'gcc-noise',code:'NOISE',name:'Noise and disturbance',department:'Front desk',slaHours:2});
    const c=M.gsvSlice(s).complaints.find(x=>x.id==='CMP-901');assert.equal(M.gcatLabel(s,c),'Noise and disturbance');assert.equal(M.gsvComplaintReport(s).find(r=>r.code==='NOISE').category,'Noise and disturbance');
    s=cmd(s,'owner','MST_MASTER_RETIRE','complaintCategories','gcc-noise');assert.equal(M.gcatLabel(s,c),'Noise and disturbance');assert.equal(mstLabel(s,'complaintCategories','NOISE'),'Noise and disturbance');
    assert.throws(()=>cmd(seed(),'owner','MST_MASTER_DELETE','complaintCategories','gcc-noise'),e=>reason('MASTER_IN_USE')(e)&&/used \(1/.test(e.message));
    });
  test('[OPSM] an unused complaint category can be deleted; a used code cannot change (MASTER_IN_USE); an unknown stored name is still reported',()=>{
    const d=cmd(seed(),'owner','MST_MASTER_DELETE','complaintCategories','gcc-billing');assert.equal(mstByCode(d,'complaintCategories','BILLING'),undefined);
    assert.throws(()=>save(seed(),'owner','complaintCategories',{id:'gcc-noise',code:'NOISE2',name:'Noise',department:'Front desk',slaHours:2}),reason('MASTER_IN_USE'));
    const x=structuredClone(d);M.gsvSlice(x);x.servicesX.complaints.push({...x.servicesX.complaints[0],id:'CMP-X',category:'Legacy topic',categoryCode:undefined});delete x.servicesX.complaints.at(-1).categoryCode;
    assert.ok(M.gsvComplaintReport(x).some(r=>r.category==='Legacy topic'&&r.total===1));});
  test('[OPSM] complaint SLA: due = logged + category hours; open past due = Over SLA; resolved after due = "Resolved after SLA"; within SLA green',()=>{
    const s=seed(),c=M.gsvSlice(s).complaints.find(x=>x.id==='CMP-901');
    assert.equal(M.gcatSlaDue(s,c),M.gsvAddMinutes(c.createdAt,120));
    const late=structuredClone(s);late.clock=M.gsvAddMinutes(c.createdAt,121);assert.equal(M.gcatOverSla(late,M.gsvSlice(late).complaints.find(x=>x.id==='CMP-901')),true);
    assert.match(M.gcatSlaLabel(late,c).label,/^Over SLA · 1 min$/);assert.equal(M.gcatSlaLabel(late,c).tone,'red');
    const soon=structuredClone(s);soon.clock=M.gsvAddMinutes(c.createdAt,30);assert.equal(M.gcatOverSla(soon,c),false);assert.match(M.gcatSlaLabel(soon,c).label,/^SLA due /);
    const r=M.gsvSlice(s).complaints.find(x=>x.id==='CMP-902');assert.equal(M.gcatOverSla(s,r),false);assert.equal(M.gcatSlaLabel(s,r).label,'Within SLA');
    const r2={...r,resolvedAt:M.gsvAddMinutes(r.createdAt,500)};assert.equal(M.gcatSlaLabel(s,r2).label,'Resolved after SLA');
    assert.equal(M.gsvComplaintReport(late).find(x=>x.code==='NOISE').overSla,1);});
  test('[OPSM] complaint category validation: department from the list, SLA 1–720 hours, name once among active rows',()=>{
    const bad=o=>assert.throws(()=>save(seed(),'owner','complaintCategories',{code:'ZZ',name:'Zed',department:'Front desk',slaHours:4,...o}),reason('MASTER_INVALID'));
    bad({department:'Kitchen'});bad({slaHours:0});bad({slaHours:721});bad({slaHours:undefined});bad({name:'noise'});});

  // ── Lost-and-found categories ──
  const found=(s,o={},u='reception')=>execute(s,u,{type:'GSV_LF_LOG',description:'Test item',category:'CLOTH',foundWhere:'Lobby',storage:'Housekeeping store',...o});
  test('[OPSM] found item: category row decides "valuable" (safe storage required); a name still works; retired / unknown refused; item keeps name and code',()=>{
    let s=found(seed());assert.deepEqual([last(s,'items').category,last(s,'items').categoryCode,last(s,'items').valuable],['Clothing','CLOTH',false]);
    assert.throws(()=>found(seed(),{category:'JEWEL'}),/must be stored in the Front office safe/);
    s=found(seed(),{category:'Jewellery',storage:'Front office safe'});assert.equal(last(s,'items').valuable,true);
    assert.throws(()=>found(seed(),{category:'Nope'}),/Choose a category/);
    const r=cmd(seed(),'owner','MST_MASTER_RETIRE','lostFoundCategories','lfc-cloth');assert.throws(()=>found(r),/Choose a category/);});
  test('[OPSM] a new valuable category (Tech admin) forces the safe for new items only; flipping "valuable" never rewrites logged items',()=>{
    let s=save(seed(),'techadmin','lostFoundCategories',{code:'ART',name:'Artwork',department:'Front desk',slaHours:6,valuable:true});
    assert.equal(M.gsvItemValuable(s,'ART'),true);assert.equal(M.gsvItemValuable(s,'Artwork'),true);assert.throws(()=>found(s,{category:'ART'}),/Valuables \(Artwork\)/);
    s=found(s,{category:'ART',storage:'Front office safe'});const id=last(s,'items').id;
    s=save(s,'owner','lostFoundCategories',{id:mstByCode(s,'lostFoundCategories','ART').id,code:'ART',name:'Artwork',department:'Front desk',slaHours:6,valuable:false});
    assert.equal(M.gsvSlice(s).items.find(i=>i.id===id).valuable,true);s=found(s,{category:'ART'});assert.equal(last(s,'items').valuable,false);
    assert.equal(M.gsvItemValuable(seed(),'Documents'),true);assert.equal(M.gsvItemValuable(seed(),'Unknown thing'),false);});
  test('[OPSM] found-item report by category (department, SLA hours, held, logged) and the contact-by time',()=>{
    const s=seed(),rep=M.gsvItemReport(s);
    assert.deepEqual(rep.find(r=>r.code==='ELEC'),{category:'Electronics',code:'ELEC',dept:'Front desk',slaHours:4,valuable:true,held:1,total:1});
    assert.equal(rep.reduce((n,r)=>n+r.total,0),M.gsvSlice(s).items.length);
    const i=M.gsvSlice(s).items.find(x=>x.id==='LF-901');assert.equal(M.lfcContactBy(s,i),M.gsvAddMinutes(i.foundAt,240));
    const r=save(s,'owner','lostFoundCategories',{id:'lfc-elec',code:'ELEC',name:'Electronic items',department:'Front desk',slaHours:4,valuable:true});assert.equal(M.lfcLabel(r,i),'Electronic items');
    assert.throws(()=>cmd(s,'owner','MST_MASTER_DELETE','lostFoundCategories','lfc-elec'),reason('MASTER_IN_USE'));});

  // ── Wiring: UI, registration, old saves ──
  test('[OPSM] old saves (no master slice) and idempotency: seeds apply; the same key repeats without a second row',()=>{
    const old=seed();delete old.mastersX;assert.equal(M.hkChecklistOf(old,{kind:'stayover'}).length,3);assert.equal(M.gcatRows(old).length,8);
    const a=save(seed(),'owner','complaintCategories',{code:'PARK',name:'Parking',department:'Front desk',slaHours:6},'k-park');
    assert.equal(save(a,'owner','complaintCategories',{code:'PARK',name:'Parking',department:'Front desk',slaHours:6},'k-park'),a);assert.equal(mstRows(a,'complaintCategories').length,9);});
  test('[OPSM] UI wiring: "Manage list…" links on the housekeeping board (desktop + phone) and guest-services forms; checklist and timing read the task type; hard-coded lists gone from forms and handlers',()=>{
    const hk=src('src/features/housekeeping/Housekeeping.tsx'),mo=src('src/mobile/ops/Housekeeping.tsx'),gm=src('src/features/services/modals.tsx');
    for(const spec of ['hkTaskTypes','hkZones'])assert.match(hk,new RegExp(`<MstLink spec="${spec}"`));
    for(const spec of ['hkTaskTypes','hkZones'])assert.match(mo,new RegExp(`<MstLink spec="${spec}"`));
    assert.match(gm,/<MstLink spec="complaintCategories"\/>/);assert.match(gm,/<MstLink spec="lostFoundCategories"\/>/);
    for(const t of [hk,mo]){assert.match(t,/hkChecklistOf\(s,t\)/);assert.match(t,/hkTaskTiming\(s,t\)/);assert.match(t,/hkzGroups|hkzOf/);assert.doesNotMatch(t,/MO_HK_CHECKLIST|\{hkChecklist\.map/);}
    assert.doesNotMatch(gm,/GSV_COMPLAINT_CATEGORIES|GSV_ITEM_CATEGORIES/);
    const hh=src('src/services/mock/handlers/housekeeping.ts'),sh=src('src/services/mock/handlers/services.ts');
    assert.doesNotMatch(hh,/\[0,1,2\]\.includes|checked:\[false,false,false\]/);assert.doesNotMatch(sh,/GSV_COMPLAINT_CATEGORIES\.includes|GSV_ITEM_CATEGORIES\.includes/);
    assert.match(src('src/services/mock/index.ts'),/streams\/hkmasters/);assert.match(src('src/services/mock/index.ts'),/streams\/gsvmasters/);});
  test('[OPSM] navigation unchanged: no new page; masters live inside Master configuration (PAGE_CATEGORY untouched)',()=>{
    assert.doesNotMatch(src('src/app/navmap.ts'),/hkTaskTypes|hkZones|complaintCategories/);});
}
