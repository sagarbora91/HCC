// MA-0 · Masters framework + Floors (plan Part 2 §9) — domain/command tests. Names prefixed [MST].
// DEC-MA-1 (owner, 26 Oct 2026): the `masters` permission = Owner + Tech admin (not the Manager; grantable in Access control).
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
export function register({test,M,assert}){
  const {seed,execute,mstRows,mstByCode,mstLabel,mstOptions,mstFloorLabel,mstFloorGroups,mstFloorOf,mstHistory,mstSpec,masterSpecs,registerMaster,unregisterMaster,
    mstValidate,mstMove,mstMoneyOn,mstVisibleFields,mstCanEdit,mstSearch,mstReasonOf,MstError,MST_ERROR_CODES,HccError}=M;
  const reason=r=>e=>e instanceof MstError&&e.reason===r&&mstReasonOf(e)===r;
  const code=c=>e=>e.code===c;
  const save=(s,u,row,key)=>execute(s,u,{type:'MST_MASTER_SAVE',master:'floors',row,key});
  const src=f=>readFileSync(f,'utf8');
  const sha=o=>createHash('sha256').update(JSON.stringify(o)).digest('hex');
  const strip=s=>{const {mastersX,grants,...r}=s;return r;};
  // A money master and an operational master with a money field + a reference to floors (provider API exercise; removed at the end).
  const money={id:'__tPay',label:'Test payment methods',singular:'payment method',group:'money',prefix:'TPM',money:true,permission:'settings',
    fields:[{key:'code',label:'Code',type:'text',required:true},{key:'name',label:'Name',type:'text',required:true},{key:'surcharge',label:'Surcharge',type:'percent',max:500}],
    inUse:()=>({count:0,where:[]}),seed:()=>[{id:'tpm-1',code:'CASH',name:'Cash',sortOrder:10,active:true}]};
  const ops={id:'__tZone',label:'Test zones',singular:'zone',group:'hkm',prefix:'TZN',money:false,permission:'masters',
    fields:[{key:'code',label:'Code',type:'text',required:true},{key:'name',label:'Name',type:'text',required:true},{key:'floor',label:'Floor',type:'select',master:'floors'},
      {key:'rate',label:'Extra rate',type:'datedMoney'},{key:'kinds',label:'Kinds',type:'multiselect',options:[{value:'A',label:'A'},{value:'B',label:'B'}]}],
    inUse:(s,row)=>({count:s.rooms.filter(r=>'Z'+r.floor===row.code).length,where:['Rooms']})};
  registerMaster(money);registerMaster(ops);

  // ── Registry, seed, fixture ──
  test('[MST] Floors is registered in Rooms and rate plans; seeded from the fixture rooms (F1–F3, "Floor 1…3"); BASE has no stored slice',()=>{
    const s=seed(),f=mstSpec('floors');
    assert.equal(f.group,'rooms');assert.equal(f.permission,'masters');assert.equal(f.money,false);assert.deepEqual(f.fields.map(x=>x.key),['code','name','sortOrder','active']);
    assert.ok(masterSpecs().some(x=>x.id==='floors'));assert.equal(s.mastersX,undefined);
    assert.deepEqual(mstRows(s,'floors').map(r=>[r.code,r.name,r.sortOrder,r.active]),[['F1','Floor 1',10,true],['F2','Floor 2',20,true],['F3','Floor 3',30,true]]);});
  test('[MST] fixture figures unchanged (occupancy 8, collections ₹21,500, drawer ₹10,725)',()=>{
    const s=seed();assert.equal(M.metrics(s).occupied,8);assert.equal(M.metrics(s).collections,2150000);assert.equal(M.drawer(s),1072500);});
  test('[MST] BASE and FY26 history hashes unchanged (grants aside: DEC-MA-1 adds "masters" to Owner and Tech admin)',()=>{
    assert.equal(sha(strip(seed())),'237e220fd61ff6587816baf1c63a1502ec790ba1553758565d80f4ed12498be1') /* ACC-2: SCHEMA 11 → 12 only (new optional fields) */ /* ACC-1: SCHEMA 10 → 11 only (accounting chart / opening fields are optional and absent in the seed) */ /* SCC decisions wave (DEC-133): 4a8aad03… → aecae3b2… = the demo firm profile's fictional licence numbers / expiry dates only */ /* SCC port wave (DEC-116, tax + leave): 2c1782df… → 4a8aad03… = compliance + leave slices, SCHEMA 9 → 10 */ /* old → new: d885a20a… → add626d3… = SCHEMA 8 → 9 only (masters wave) */;
    assert.equal(sha(strip(M.buildHistory(M.baseFixture()))),'ba51a0eacfcdfab7835be9b16b0a8f0793aad052c1c9b99c0b80e511989ee2e8') /* ACC-2: SCHEMA 11 → 12 only (new optional fields) */ /* ACC-1 integration (all seven lanes merged + the ITC / cash-over one-liners): the FY26 history locks its accounting months with accLiveVouchers snapshots, which now carry the re-pointed mapping (POS 4200, ITC 1310 / 1320, cash over 4950), party ledger ids (1000/TILL, 1030/<bank>, 1120/UD-…, 2200/<vendor>) and the Udhaar credit / company payment vouchers: f3601522… → e3a74ba3… */ /* ACC-1: SCHEMA 10 → 11 only */ /* SCC decisions wave (DEC-133): 197643a1… → 5dc19a44… = the demo firm profile's fictional licence fields only */ /* SCC port wave (DEC-116, tax + leave): bf689538… → 197643a1… = compliance + leave slices, SCHEMA 9 → 10 */ /* old → new: ba4a1d4e… → 35e63082… = SCHEMA 8 → 9 only */;
    assert.equal(sha(M.loadScenario('FY26').bookings),'8b253c915389d5661c9489656586fe2bed7fccad7fe1e5e318e322838e05691c' /* ACC-2 integration re-pin: room-service folio lines kind food (acc2-parity) */);});
  test('[MST] permission list and grants: "masters" added; Owner + Tech admin hold it, Manager does not (DEC-MA-1)',()=>{
    // old → new: permissions 12 → 13 ('masters'); Tech admin ['admin'] → ['admin','masters']; Manager unchanged (all but admin, masters).
    const s=seed();assert.equal(M.permissions.length,13);assert.equal(M.permissions.at(-1),'masters');
    assert.equal(M.can(s,'owner','masters'),true);assert.equal(M.can(s,'techadmin','masters'),true);
    for(const u of ['manager','reception','cashier','supervisor','housekeeper','accountant','maint'])assert.equal(M.can(s,u,'masters'),false,u);
    assert.deepEqual(s.grants['Tech admin'],['admin','masters']);});

  // ── Save / edit / validation ──
  test('[MST] add a floor (Owner): stored, audit "Master Floors: added …", history row',()=>{
    const s=save(seed(),'owner',{code:'f4',name:'  Annexe '},'f4');const r=mstByCode(s,'floors','F4');
    assert.equal(r.name,'Annexe');assert.equal(r.sortOrder,40);assert.equal(r.active,true);assert.equal(mstRows(s,'floors').length,4);
    assert.equal(s.audit[0].detail,'Master Floors: added F4 · Annexe');assert.equal(s.audit[0].action,'MST MASTER SAVE');
    assert.deepEqual(mstHistory(s,'floors',r.id).map(h=>h.field).sort(),['code','name','sortOrder']);});
  test('[MST] rename a floor (Tech admin): audit "Master Floors: name Floor 1 → Ground floor"; board names follow',()=>{
    const s=save(seed(),'techadmin',{id:'flr-1',code:'F1',name:'Ground floor'});
    assert.equal(s.audit[0].detail,'Master Floors: name Floor 1 → Ground floor');
    const h=mstHistory(s,'floors','flr-1');assert.equal(h.length,1);assert.deepEqual([h[0].field,h[0].old,h[0].new,h[0].by],['name','Floor 1','Ground floor','techadmin']);
    assert.equal(mstFloorLabel(s,s.rooms.find(r=>r.id==='101')),'Ground floor');assert.equal(mstFloorGroups(s)[0].label,'Ground floor');});
  test('[MST] duplicate code → MASTER_CODE_TAKEN (case-insensitive, retired rows count)',()=>{
    assert.throws(()=>save(seed(),'owner',{code:'f2',name:'Second'}),e=>reason('MASTER_CODE_TAKEN')(e)&&e.code==='VALIDATION');
    let s=save(seed(),'owner',{code:'F9',name:'Roof'});const id=mstByCode(s,'floors','F9').id;s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'floors',id});
    assert.throws(()=>save(s,'owner',{code:'F9',name:'Roof 2'}),reason('MASTER_CODE_TAKEN'));});
  test('[MST] invalid rows → MASTER_INVALID (code pattern, empty / long name, duplicate active name, sort order)',()=>{
    const s=seed();
    assert.throws(()=>save(s,'owner',{code:'F 5',name:'X'}),reason('MASTER_INVALID'));
    assert.throws(()=>save(s,'owner',{code:'F5',name:''}),reason('MASTER_INVALID'));
    assert.throws(()=>save(s,'owner',{code:'F5',name:'x'.repeat(61)}),reason('MASTER_INVALID'));
    assert.throws(()=>save(s,'owner',{code:'F5',name:'floor 2'}),reason('MASTER_INVALID'));
    assert.throws(()=>save(s,'owner',{code:'F5',name:'Five',sortOrder:-1}),reason('MASTER_INVALID'));
    assert.throws(()=>save(s,'owner',{code:'ABCDEFGHIJKLMNOPQRSTU',name:'Long'}),reason('MASTER_INVALID'));
    const p=mstValidate(mstSpec('floors'),{id:'x',code:'',name:'',sortOrder:1,active:true},mstRows(s,'floors'));assert.equal(p.reason,'MASTER_INVALID');assert.equal(p.field,'code');});
  test('[MST] code is immutable once in use (MASTER_IN_USE); an unused row may change its code',()=>{
    assert.throws(()=>save(seed(),'owner',{id:'flr-2',code:'F2X',name:'Floor 2'}),e=>reason('MASTER_IN_USE')(e)&&e.code==='INVALID_TRANSITION');
    let s=save(seed(),'owner',{code:'ANNEXE',name:'Annexe'});const id=mstByCode(s,'floors','ANNEXE').id;
    s=save(s,'owner',{id,code:'ANX',name:'Annexe'});assert.ok(mstByCode(s,'floors','ANX'));assert.match(s.audit[0].detail,/code ANNEXE → ANX/);});
  test('[MST] nothing changed / unknown list / unknown row are refused with typed errors',()=>{
    assert.throws(()=>save(seed(),'owner',{id:'flr-1',code:'F1',name:'Floor 1'}),code('VALIDATION'));
    assert.throws(()=>execute(seed(),'owner',{type:'MST_MASTER_SAVE',master:'nope',row:{code:'A',name:'A'}}),code('VALIDATION'));
    assert.throws(()=>save(seed(),'owner',{id:'flr-77',code:'F7',name:'X'}),code('NOT_FOUND'));});

  // ── Retire / delete / reactivate / reorder ──
  test('[MST] retire a floor in use is allowed; its rooms keep the name; retired rows resolve and leave pick lists',()=>{
    let s=save(seed(),'owner',{id:'flr-3',code:'F3',name:'Top floor'});
    s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'floors',id:'flr-3'});
    const r=mstByCode(s,'floors','F3');assert.equal(r.active,false);assert.equal(r.retiredOn,s.date);
    assert.match(s.audit[0].detail,/^Master Floors: active yes → no \(F3 · Top floor retired; still used: 8/);
    assert.equal(mstFloorLabel(s,s.rooms.find(x=>x.id==='301')),'Top floor');assert.equal(mstLabel(s,'floors','F3'),'Top floor');assert.equal(mstFloorOf(s,{floor:3}).id,'flr-3');
    assert.equal(mstOptions(s,'floors').some(o=>o.value==='F3'),false);assert.deepEqual(mstOptions(s,'floors','F3').find(o=>o.value==='F3'),{value:'F3',label:'Top floor (retired)',retired:true});
    assert.equal(mstRows(s,'floors',{active:true}).length,2);assert.equal(mstFloorGroups(s).find(g=>g.floor===3).retired,true);});
  test('[MST] delete: refused while in use (MASTER_IN_USE, "retire it instead"); allowed for a never-used floor',()=>{
    assert.throws(()=>execute(seed(),'owner',{type:'MST_MASTER_DELETE',master:'floors',id:'flr-1'}),e=>reason('MASTER_IN_USE')(e)&&/retire it instead/.test(e.message));
    let s=save(seed(),'owner',{code:'F4',name:'Annexe'});const id=mstByCode(s,'floors','F4').id;
    s=execute(s,'techadmin',{type:'MST_MASTER_DELETE',master:'floors',id});assert.equal(mstByCode(s,'floors','F4'),undefined);assert.match(s.audit[0].detail,/deleted \(never used\)/);
    assert.equal(mstHistory(s,'floors',id)[0].field,'deleted');});
  test('[MST] a retired row cannot be edited (MASTER_RETIRED) until reactivated; double retire is refused',()=>{
    let s=execute(seed(),'owner',{type:'MST_MASTER_RETIRE',master:'floors',id:'flr-2'});
    assert.throws(()=>save(s,'owner',{id:'flr-2',code:'F2',name:'Two'}),e=>reason('MASTER_RETIRED')(e)&&e.code==='INVALID_TRANSITION');
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'floors',id:'flr-2'}),code('INVALID_TRANSITION'));
    s=execute(s,'owner',{type:'MST_MASTER_REACTIVATE',master:'floors',id:'flr-2'});const r=mstByCode(s,'floors','F2');assert.equal(r.active,true);assert.equal(r.retiredOn,undefined);
    s=save(s,'owner',{id:'flr-2',code:'F2',name:'Two'});assert.equal(mstLabel(s,'floors','F2'),'Two');
    assert.deepEqual(mstHistory(s,'floors','flr-2').map(h=>h.field),['name','active','active']);});
  test('[MST] reorder: a full permutation sets sortOrder 10, 20, …; board sections follow; bad lists refused',()=>{
    const s0=seed();const ids=mstMove(mstRows(s0,'floors'),'flr-3',-1);assert.deepEqual(ids,['flr-1','flr-3','flr-2']);
    const s=execute(s0,'owner',{type:'MST_MASTER_REORDER',master:'floors',ids:['flr-3','flr-1','flr-2']});
    assert.deepEqual(mstRows(s,'floors').map(r=>[r.code,r.sortOrder]),[['F3',10],['F1',20],['F2',30]]);
    assert.deepEqual(mstFloorGroups(s).map(g=>g.floor),[3,1,2]);assert.match(s.audit[0].detail,/^Master Floors: sortOrder changed for/);
    assert.equal(mstHistory(s,'floors').filter(h=>h.field==='sortOrder').length,3);
    assert.throws(()=>execute(s0,'owner',{type:'MST_MASTER_REORDER',master:'floors',ids:['flr-1','flr-2']}),code('VALIDATION'));
    assert.throws(()=>execute(s0,'owner',{type:'MST_MASTER_REORDER',master:'floors',ids:['flr-1','flr-2','flr-3']}),/did not change/);});

  // ── Permissions (DEC-MA-1, DEC-058, DEC-061) ──
  test('[MST] Reception, Manager, Cashier, Housekeeper, Accountant are denied; Tech admin and Owner may change floors',()=>{
    for(const u of ['reception','manager','cashier','housekeeper','accountant','supervisor','maint'])
      assert.throws(()=>save(seed(),u,{code:'F4',name:'Annexe'}),code('FORBIDDEN'),u);
    for(const u of ['owner','techadmin'])assert.ok(mstByCode(save(seed(),u,{code:'F4',name:'Annexe'}),'floors','F4'),u);
    assert.equal(mstCanEdit(seed(),'manager',mstSpec('floors')).ok,false);assert.equal(mstCanEdit(seed(),'techadmin',mstSpec('floors')).ok,true);});
  test('[MST] the masters permission is grantable: the Owner grants it to the Manager in Access control, then the Manager may change floors',()=>{
    let s=execute(seed(),'owner',{type:'GRANT',role:'Manager',permission:'masters'});assert.equal(M.can(s,'manager','masters'),true);
    s=save(s,'manager',{id:'flr-1',code:'F1',name:'Ground'});assert.equal(mstLabel(s,'floors','F1'),'Ground');
    s=execute(s,'owner',{type:'GRANT',role:'Manager',permission:'masters'});assert.throws(()=>save(s,'manager',{id:'flr-1',code:'F1',name:'Ground 2'}),code('FORBIDDEN'));});
  test('[MST] a money master is Owner-only (DEC-058): Tech admin and Manager refused, Owner allowed',()=>{
    for(const u of ['techadmin','manager','reception'])assert.throws(()=>execute(seed(),u,{type:'MST_MASTER_SAVE',master:'__tPay',row:{code:'UPI',name:'UPI'}}),code('FORBIDDEN'),u);
    const s=execute(seed(),'owner',{type:'MST_MASTER_SAVE',master:'__tPay',row:{code:'UPI',name:'UPI',surcharge:150}});
    assert.equal(mstByCode(s,'__tPay','UPI').surcharge,150);assert.equal(mstRows(s,'__tPay').length,2);});
  test('[MST] money fields inside an operational list: Tech admin edits names but not amounts; HK/maintenance/supervisor never see them (DEC-061)',()=>{
    let s=execute(seed(),'techadmin',{type:'MST_MASTER_SAVE',master:'__tZone',row:{code:'Z1',name:'Zone 1'}});
    assert.throws(()=>execute(s,'techadmin',{type:'MST_MASTER_SAVE',master:'__tZone',row:{code:'Z2',name:'Zone 2',rate:[{amount:10000,effectiveFrom:s.date}]}}),e=>e.code==='FORBIDDEN'&&/Owner/.test(e.message));
    s=execute(s,'owner',{type:'MST_MASTER_SAVE',master:'__tZone',row:{code:'Z2',name:'Zone 2',rate:[{amount:10000,effectiveFrom:s.date}]}});
    const spec=mstSpec('__tZone');
    for(const u of ['housekeeper','maint','supervisor'])assert.equal(mstVisibleFields(s,u,spec).some(f=>f.key==='rate'),false,u);
    for(const u of ['owner','techadmin','manager'])assert.equal(mstVisibleFields(s,u,spec).some(f=>f.key==='rate'),true,u);});

  // ── Field types, references, dated money ──
  test('[MST] select from another master: a retired floor cannot be newly chosen (MASTER_RETIRED) but stays valid on an existing row',()=>{
    let s=execute(seed(),'owner',{type:'MST_MASTER_SAVE',master:'__tZone',row:{code:'Z1',name:'Zone 1',floor:'F2',kinds:['A']}});
    const id=mstByCode(s,'__tZone','Z1').id;s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'floors',id:'flr-3'});
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_SAVE',master:'__tZone',row:{code:'Z2',name:'Zone 2',floor:'F3'}}),reason('MASTER_RETIRED'));
    s=execute(s,'owner',{type:'MST_MASTER_SAVE',master:'__tZone',row:{id,code:'Z1',name:'Zone 1',floor:'F2',kinds:['A','B']}});
    s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'floors',id:'flr-2'});
    s=execute(s,'owner',{type:'MST_MASTER_SAVE',master:'__tZone',row:{id,code:'Z1',name:'Zone one',floor:'F2'}}); // unchanged retired value is kept
    assert.equal(mstByCode(s,'__tZone','Z1').floor,'F2');
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_SAVE',master:'__tZone',row:{code:'Z3',name:'Zone 3',kinds:['C']}}),reason('MASTER_INVALID'));
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_SAVE',master:'__tZone',row:{code:'Z3',name:'Zone 3',floor:'F8'}}),reason('MASTER_INVALID'));});
  test('[MST] dated money: amounts in effect are immutable, a new amount from today or later is added; amount on a date',()=>{
    let s=execute(seed(),'owner',{type:'MST_MASTER_SAVE',master:'__tZone',row:{code:'Z1',name:'Zone 1',rate:[{amount:10000,effectiveFrom:'2026-10-24'}]}});
    const id=mstByCode(s,'__tZone','Z1').id;s=execute(s,'owner',{type:'NIGHT_POST'});
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_SAVE',master:'__tZone',row:{id,code:'Z1',name:'Zone 1',rate:[{amount:15000,effectiveFrom:'2026-10-20'}]}}),reason('MASTER_INVALID'));
    s=execute(s,'owner',{type:'MST_MASTER_SAVE',master:'__tZone',row:{id,code:'Z1',name:'Zone 1',rate:[{amount:10000,effectiveFrom:'2026-10-24'},{amount:12000,effectiveFrom:'2026-11-01'}]}});
    const v=mstByCode(s,'__tZone','Z1').rate;assert.equal(mstMoneyOn(v,'2026-10-25'),10000);assert.equal(mstMoneyOn(v,'2026-11-02'),12000);assert.equal(mstMoneyOn(v,'2026-10-01'),undefined);});

  // ── Idempotency, errors, old saves, search ──
  test('[MST] idempotent repeat: the same key returns the same state (no second row, no second history entry)',()=>{
    const a=save(seed(),'owner',{code:'F4',name:'Annexe'},'mst-k1');assert.equal(save(a,'owner',{code:'F4',name:'Annexe'},'mst-k1'),a);
    assert.equal(mstRows(a,'floors').length,4);});
  test('[MST] error codes: shared list, MstError is an HccError with the wire code and the master reason',()=>{
    assert.deepEqual([...MST_ERROR_CODES],['MASTER_CODE_TAKEN','MASTER_IN_USE','MASTER_RETIRED','MASTER_LOCKED_BY_BOOKINGS','MASTER_INVALID']);
    const e=new MstError('MASTER_LOCKED_BY_BOOKINGS','Locked');assert.ok(e instanceof HccError);assert.equal(e.code,'INVALID_TRANSITION');
    assert.equal(mstReasonOf({code:'VALIDATION',details:{reason:'MASTER_CODE_TAKEN'}}),'MASTER_CODE_TAKEN');assert.equal(mstReasonOf(new Error('x')),undefined);});
  test('[MST] old saves and new floors: no master slice → seed names; a room on a floor without a row shows "Floor n"',()=>{
    const s=save(seed(),'owner',{id:'flr-1',code:'F1',name:'Ground'});const x=structuredClone(s);x.rooms.push({...x.rooms[0],id:'401',floor:4});
    assert.equal(mstFloorLabel(x,x.rooms.at(-1)),'Floor 4');assert.deepEqual(mstFloorGroups(x).map(g=>g.label),['Ground','Floor 2','Floor 3','Floor 4']);
    const old=seed();delete old.mastersX;assert.equal(mstFloorLabel(old,2),'Floor 2');});
  test('[MST] Master configuration search finds masters by label ("floors", "list")',()=>{
    assert.ok(mstSearch('floors').some(x=>x.id==='floors'));assert.ok(mstSearch('lists').some(x=>x.id==='floors'));assert.deepEqual(mstSearch('zzz'),[]);});

  // ── UI wiring, navigation, production mapping (source checks; render checks are in tests/sync-masters.mjs) ──
  test('[MST] desktop + phone registration: modal mst-row on both; Lists block in both hubs; registry entry',()=>{
    const reg=src('src/features/masters/register.tsx');assert.match(reg,/modals:\{'mst-row':MasterSheet[,}]/);assert.match(reg,/mobile:\{modals:\{'mst-row':MasterSheet[,}]/); // MA-8 old → new: the two MA-8 modals (mst-import, mst-paste) follow mst-row
    assert.match(src('src/app/registry.ts'),/features\/masters\/register/);assert.match(src('src/app/registry.ts'),/cashbook,masters,m_frontdesk/);
    assert.match(src('src/features/admin/HotelSettings.tsx'),/<MstListsBlock key=\{g\} g=\{g\} q=\{q\}\/>/);
    assert.match(src('src/mobile/admin/Settings.tsx'),/<MstListsBlockPhone key=\{g\} g=\{g\} q=\{q\}\/>/);});
  test('[MST] floor names on the room board, housekeeping board and phone rooms, with "Manage list…" next to the floor filters',()=>{
    for(const f of ['src/features/rooms/RoomBoard.tsx','src/features/housekeeping/Housekeeping.tsx','src/mobile/frontdesk/Rooms.tsx']){
      const t=src(f);assert.match(t,/mstFloorGroups\(s\)/,f);assert.match(t,/<MstLink spec="floors"\/>/,f);assert.doesNotMatch(t,/'Floor '\+f|Floor \{f\}|\[1,2,3\]\.map/,f);}
    assert.match(src('src/features/rooms/RoomModal.tsx'),/mstFloorLabel\(s,r\)/);assert.match(src('src/mobile/ops/Housekeeping.tsx'),/mstFloorLabel\(s,room\)/);});
  test('[MST] navigation: no new page (PAGE_CATEGORY unchanged — masters live in settings); settings opens for admin or masters',()=>{
    const nm=src('src/app/navmap.ts');assert.doesNotMatch(nm,/masters:'/);assert.match(nm,/access:'admin',settings:'admin',approvals:'admin',backup:'admin',roadmap:'admin',\n\};/);
    assert.match(src('src/app/nav.ts'),/\{id:'settings',label:'Master configuration',icon:Settings,p:\['admin','masters'\]\}/);});
  test('[MST] production build mapping: sync area "masters" registered; MST_* commands mapped to the server Masters module',()=>{
    const reg=src('src/clients/sync/registry.ts'),a=src('src/clients/sync/masters.ts');
    assert.match(reg,/import \{masters\} from '\.\/masters';/);assert.match(reg,/\n  masters, \/\/ MA-0/);
    for(const c of ['MST_MASTER_SAVE','MST_MASTER_RETIRE','MST_MASTER_REACTIVATE','MST_MASTER_REORDER','MST_MASTER_DELETE'])assert.ok(a.includes(`'${c}'`),c);
    for(const n of ['SaveFloor','RetireFloor','ReactivateFloor','ReorderFloors','DeleteFloor'])assert.ok(a.includes(n),n);
    assert.match(src('src/clients/sync/capabilities.ts'),/masters:\{any:\['PERM-MST-001'\],modes:'AL'\}/);});
  test('[MST] provider API: register / replace / unregister a master',()=>{
    registerMaster({...money,label:'Payment methods (test)'});assert.equal(mstSpec('__tPay').label,'Payment methods (test)');
    unregisterMaster('__tPay');unregisterMaster('__tZone');unregisterMaster('floors');
    assert.equal(mstSpec('__tPay'),undefined);assert.equal(mstSpec('__tZone'),undefined);assert.ok(mstSpec('floors'));});
}
