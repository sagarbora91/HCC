// MA-1 · Rooms and room types masters (plan Part 2 §8 M2–M5): room types, rooms (backed by s.rooms), amenities, occupancy charges,
// range add / renumber / out of order, floor names in the Overview strip and room pickers. Names prefixed [RMS].
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  const {seed,execute,mstRows,mstByCode,mstById,mstSpec,mstHistory,mstReasonOf,MstError,available,metrics,rateFor,adminRoomTypes,changedSlices}=M;
  const reason=r=>e=>e instanceof MstError&&e.reason===r&&mstReasonOf(e)===r;
  const forbidden=e=>e.code==='FORBIDDEN';
  const save=(s,u,master,row,key)=>execute(s,u,{type:'MST_MASTER_SAVE',master,row,key});
  const add=(s,u,rooms,floor,roomType,key)=>execute(s,u,{type:'RMS_ROOMS_ADD',rooms,floor,roomType,key});
  const room=(s,id)=>s.rooms.find(r=>r.id===id);
  const src=f=>readFileSync(f,'utf8');
  const sha=o=>createHash('sha256').update(JSON.stringify(o)).digest('hex');
  const strip=s=>{const {mastersX,grants,...r}=s;return r;};
  const F4=s=>save(s,'owner','floors',{code:'F4',name:'Fourth floor'});
  const oooNights=(s,id,a,d)=>execute(s,'owner',{type:'RMS_ROOM_OOO',id,from:a,...(d?{to:d}:{}),reason:'AC compressor replaced'});

  // ── Registry, seeds, golden figures ──
  test('[RMS] four masters registered in Rooms and rate plans (room types, rooms, amenities = masters; occupancy charges = Owner money list)',()=>{
    const ids=['roomTypes','rooms','amenities','occupancyCharges'];
    for(const id of ids)assert.equal(mstSpec(id).group,'rooms',id);
    assert.deepEqual(ids.map(id=>[mstSpec(id).permission,mstSpec(id).money]),[['masters',false],['masters',false],['masters',false],['settings',true]]);
    assert.ok(mstSpec('roomTypes').store&&mstSpec('rooms').store,'room types and rooms use store adapters (no second copy)');});
  test('[RMS] room types seeded from the fixture rooms: codes, names = keys, order, occupancy = RES_CAPACITY, rack rate = room rate',()=>{
    const s=seed(),t=mstRows(s,'roomTypes');assert.equal(s.mastersX,undefined);
    assert.deepEqual(t.map(r=>[r.code,r.name,r.key,r.sortOrder]),[['CLASSIC','Classic','Classic',10],['DELUXE','Deluxe','Deluxe',20],['SUITE','Suite','Suite',30]]);
    assert.deepEqual(t.map(r=>r.maxAdults+r.maxChildren),[M.RES_CAPACITY.Classic,M.RES_CAPACITY.Deluxe,M.RES_CAPACITY.Suite]);
    assert.deepEqual(t.map(r=>r.rackRate),[[{amount:200000,effectiveFrom:'2000-01-01'}],[{amount:300000,effectiveFrom:'2000-01-01'}],[{amount:450000,effectiveFrom:'2000-01-01'}]]);
    assert.deepEqual(adminRoomTypes(s),['Classic','Deluxe','Suite']);assert.deepEqual(M.rmsRoomTypeKeys(s),['Classic','Deluxe','Suite']);});
  test('[RMS] rooms master reads s.rooms: 24 rows, number = code, floor F<n>, type code, retired none',()=>{
    const r=mstRows(seed(),'rooms');assert.equal(r.length,24);
    assert.deepEqual([r[0].id,r[0].code,r[0].name,r[0].floor,r[0].type,r[0].active],['101','101','Room 101','F1','CLASSIC',true]);
    assert.deepEqual(r.map(x=>x.code).slice(-2),['307','308']);assert.equal(mstById(seed(),'rooms','306').type,'SUITE');});
  test('[RMS] golden figures unchanged (occupancy 8, collections ₹21,500, drawer ₹10,725; BASE and FY26 hashes)',()=>{
    const s=seed();assert.equal(metrics(s).occupied,8);assert.equal(metrics(s).collections,2150000);assert.equal(M.drawer(s),1072500);
    assert.equal(sha(strip(s)),'d6df4a46fafac41b91f03ccc5643d9f3b48de4223a2ec356218f1f7c2210c366') /* ADM-2: f8788cff… → d6df4a46… = SCHEMA 13 → 14 only */  /* LF-int (F3): 286e328b… → f8788cff… = SCHEMA 12 → 13 + compliance demo evidence moved to the documents mirror docObjectsX (LF-docdb); FY26 5b118f02… → 415be48f… same cause */ /* ACC-2: SCHEMA 11 → 12 only (new optional fields) */ /* ACC-1: SCHEMA 10 → 11 only (accounting chart / opening fields are optional and absent in the seed) */ /* SCC decisions wave (DEC-133): 4a8aad03… → aecae3b2… = the demo firm profile's fictional licence numbers / expiry dates only */ /* SCC port wave (DEC-116, tax + leave): 2c1782df… → 4a8aad03… = compliance + leave slices, SCHEMA 9 → 10 */ /* old → new: d885a20a… → add626d3… = SCHEMA 8 → 9 only (masters wave) */;
    assert.equal(sha(strip(M.buildHistory(M.baseFixture()))),'961b04c2148b4430927b0fe4ac6466a0bd50ce67cbeca468bb569decce629f5a' /* ADM-2: 14d8a8f7… → 961b04c2… = SCHEMA 13 → 14 */ ) /* LF-int (F3): 286e328b… → f8788cff… = SCHEMA 12 → 13 + compliance demo evidence moved to the documents mirror docObjectsX (LF-docdb); FY26 5b118f02… → 415be48f… same cause */ /* ACC-2: SCHEMA 11 → 12 only (new optional fields) */ /* ACC-1 integration (all seven lanes merged + the ITC / cash-over one-liners): the FY26 history locks its accounting months with accLiveVouchers snapshots, which now carry the re-pointed mapping (POS 4200, ITC 1310 / 1320, cash over 4950), party ledger ids (1000/TILL, 1030/<bank>, 1120/UD-…, 2200/<vendor>) and the Udhaar credit / company payment vouchers: f3601522… → e3a74ba3… */ /* ACC-1: SCHEMA 10 → 11 only */ /* SCC decisions wave (DEC-133): 197643a1… → 5dc19a44… = the demo firm profile's fictional licence fields only */ /* SCC port wave (DEC-116, tax + leave): bf689538… → 197643a1… = compliance + leave slices, SCHEMA 9 → 10 */ /* old → new: ba4a1d4e… → 35e63082… = SCHEMA 8 → 9 only */;});
  test('[RMS] amenities and occupancy charges seeded (9 amenities; one charge row per type; Classic has no extra bed)',()=>{
    const s=seed();assert.equal(mstRows(s,'amenities').length,9);assert.equal(mstByCode(s,'amenities','WIFI').icon,'wifi');
    const o=mstRows(s,'occupancyCharges');assert.deepEqual(o.map(x=>[x.code,x.roomType]),[['X-CLASSIC','CLASSIC'],['X-DELUXE','DELUXE'],['X-SUITE','SUITE']]);
    assert.equal(o[0].extraBed,undefined);assert.deepEqual(o[1].extraBed,[{amount:100000,effectiveFrom:'2025-04-01'}]);});

  // ── Room types ──
  test('[RMS] rename a room type: every screen name follows, rooms / tariffs / history keep the key; audit + history',()=>{
    const s=save(seed(),'techadmin','roomTypes',{id:'rty-deluxe',name:'Deluxe King'});
    assert.equal(M.rmsTypeName(s,'Deluxe'),'Deluxe King');assert.equal(room(s,'103').type,'Deluxe');
    assert.equal(s.audit[0].detail,'Master Room types: name Deluxe → Deluxe King');
    assert.deepEqual(mstHistory(s,'roomTypes','rty-deluxe').map(h=>[h.field,h.old,h.new]),[['name','Deluxe','Deluxe King']]);
    assert.ok(changedSlices(seed(),s).includes('mastersX'));assert.deepEqual(s.rooms,seed().rooms,'a type rename does not change s.rooms');});
  test('[RMS] rack rate = the tariff schedule: a future amount (Owner) becomes a dated tariff and rateFor follows from that date',()=>{
    let s=seed();const base=[{amount:300000,effectiveFrom:'2000-01-01'}];
    s=save(s,'owner','roomTypes',{id:'rty-deluxe',rackRate:[...base,{amount:320000,effectiveFrom:'2026-11-01'}]});
    assert.deepEqual(s.tariffs,[{type:'Deluxe',rate:320000,effectiveFrom:'2026-11-01'}]);
    assert.equal(rateFor(s,room(s,'103'),'2026-10-31'),300000);assert.equal(rateFor(s,room(s,'103'),'2026-11-01'),320000);
    assert.deepEqual(mstByCode(s,'roomTypes','DELUXE').rackRate,[...base,{amount:320000,effectiveFrom:'2026-11-01'}]);
    s=execute(s,'owner',{type:'TARIFF_SCHEDULE',roomType:'Deluxe',rate:330000,effectiveFrom:'2026-12-01'});
    assert.equal(mstByCode(s,'roomTypes','DELUXE').rackRate.at(-1).amount,330000,'a tariff scheduled on the Tariffs screen shows in the master');});
  test('[RMS] rack rate is Owner-only (Tech admin FORBIDDEN) and amounts already in effect cannot change (MASTER_INVALID)',()=>{
    assert.throws(()=>save(seed(),'techadmin','roomTypes',{id:'rty-deluxe',rackRate:[{amount:300000,effectiveFrom:'2000-01-01'},{amount:310000,effectiveFrom:'2026-11-01'}]}),forbidden);
    assert.throws(()=>save(seed(),'owner','roomTypes',{id:'rty-deluxe',rackRate:[{amount:310000,effectiveFrom:'2000-01-01'}]}),reason('MASTER_INVALID'));});
  test('[RMS] add a new type (Owner, rack rate from today); not in Tariffs until it has rooms; duplicate code → MASTER_CODE_TAKEN',()=>{
    const s=save(seed(),'owner','roomTypes',{code:'exec',name:'Executive',maxAdults:2,maxChildren:1,rackRate:[{amount:380000,effectiveFrom:'2026-10-24'}]});
    const t=mstByCode(s,'roomTypes','EXEC');assert.equal(t.key,'EXEC');assert.equal(t.sortOrder,40);
    assert.deepEqual(adminRoomTypes(s),['Classic','Deluxe','Suite']);assert.equal(M.rmsRackOn(s,'EXEC','2026-10-24'),380000);
    assert.throws(()=>save(seed(),'owner','roomTypes',{code:'deluxe',name:'Deluxe 2',maxAdults:2}),reason('MASTER_CODE_TAKEN'));});
  test('[RMS] a type with rooms cannot be retired (MASTER_IN_USE) or deleted, nor its code changed; an unused type can be deleted',()=>{
    assert.throws(()=>execute(seed(),'owner',{type:'MST_MASTER_RETIRE',master:'roomTypes',id:'rty-suite'}),reason('MASTER_IN_USE'));
    assert.throws(()=>execute(seed(),'owner',{type:'MST_MASTER_DELETE',master:'roomTypes',id:'rty-suite'}),reason('MASTER_IN_USE'));
    assert.throws(()=>save(seed(),'owner','roomTypes',{id:'rty-suite',code:'STE'}),reason('MASTER_IN_USE'));
    let s=save(seed(),'owner','roomTypes',{code:'EXEC',name:'Executive',maxAdults:2});const id=mstByCode(s,'roomTypes','EXEC').id;
    s=execute(s,'owner',{type:'MST_MASTER_DELETE',master:'roomTypes',id});assert.equal(mstByCode(s,'roomTypes','EXEC'),undefined);});
  test('[RMS] max occupancy from the master drives the booking capacity check (seed = old figures; lowered Suite refuses 4)',()=>{
    const book=(s,people)=>execute(s,'owner',{type:'BOOK',guest:'Cap Test',room:'106',arrival:s.date,departure:'2026-10-26',people});
    assert.ok(book(seed(),4));
    const s=save(seed(),'techadmin','roomTypes',{id:'rty-suite',maxChildren:1});
    assert.throws(()=>book(s,4),/at most 3 guests/);assert.equal(M.rmsOverOccupancy(s,'Suite',3).length,0);});
  test('[RMS] reorder room types: Tariffs / group / channel lists follow the master order',()=>{
    const s=execute(seed(),'techadmin',{type:'MST_MASTER_REORDER',master:'roomTypes',ids:['rty-suite','rty-classic','rty-deluxe']});
    assert.deepEqual(adminRoomTypes(s),['Suite','Classic','Deluxe']);assert.deepEqual(M.rmsRoomTypeKeys(s),['Suite','Classic','Deluxe']);});

  // ── Rooms: range add ──
  test('[RMS] range add 401–405 on a new floor F4 (Tech admin): Ready rooms at the rack rate, bookable, board + audit + history',()=>{
    let s=F4(seed());s=add(s,'techadmin','401–405','F4','DELUXE','rng-1');
    assert.deepEqual(s.rooms.slice(-5).map(r=>[r.id,r.floor,r.type,r.rate,r.hk,r.blocked]),['401','402','403','404','405'].map(id=>[id,4,'Deluxe',300000,'Ready',false]));
    assert.equal(s.audit[0].detail,'Master Rooms: added 401–405 (5 rooms) on Fourth floor · Deluxe');
    const free=x=>x.rooms.filter(r=>r.type==='Deluxe'&&available(x,r.id,x.date,'2026-10-25')).length;assert.equal(free(s),free(seed())+5,'five more Deluxe rooms bookable tonight');assert.equal(mstHistory(s,'rooms','402')[0].field,'added');
    assert.equal(M.mstFloorGroups(s).at(-1).label,'Fourth floor');assert.equal(metrics(s).sellable,metrics(seed()).sellable+5);
    assert.deepEqual(add(s,'techadmin','401–405','F4','DELUXE','rng-1'),s,'idempotent repeat');});
  test('[RMS] range text: "101-103, 105", "201 to 203", leading zeros; errors for reversed, > 50, duplicates, bad numbers',()=>{
    const p=M.rmsParseRooms;assert.deepEqual(p('101-103, 105').numbers,['101','102','103','105']);assert.deepEqual(p('201 to 203').numbers,['201','202','203']);
    assert.deepEqual(p('008–010').numbers,['008','009','010']);assert.deepEqual(p('a12').numbers,['A12']);
    for(const bad of ['110-101','100-200','101, 101','10#1',''])assert.ok(p(bad).error,bad);});
  test('[RMS] range add refuses taken numbers (existing or on past stays), retired or unnumbered floors, retired types',()=>{
    assert.throws(()=>add(F4(seed()),'owner','308-310','F4','DELUXE'),reason('MASTER_CODE_TAKEN'));
    let s=save(seed(),'owner','floors',{code:'ANNEXE',name:'Annexe'});
    assert.throws(()=>add(s,'owner','901','ANNEXE','DELUXE'),reason('MASTER_INVALID'));
    s=execute(F4(seed()),'owner',{type:'MST_MASTER_RETIRE',master:'floors',id:mstByCode(F4(seed()),'floors','F4').id});
    assert.throws(()=>add(s,'owner','401','F4','DELUXE'),reason('MASTER_RETIRED'));
    s=save(seed(),'owner','roomTypes',{code:'EXEC',name:'Executive',maxAdults:2});
    assert.throws(()=>add(s,'owner','109','F1','EXEC'),reason('MASTER_INVALID'),'a type without a rack rate');});
  test('[RMS] permissions: Reception, Manager, Housekeeper FORBIDDEN on room tools; occupancy charges Owner only',()=>{
    for(const u of ['reception','manager','housekeeper','cashier'])assert.throws(()=>add(seed(),u,'109','F1','DELUXE'),forbidden,u);
    assert.throws(()=>save(seed(),'techadmin','occupancyCharges',{id:'occ-deluxe',childAgeTo:11}),forbidden);
    assert.equal(mstByCode(save(seed(),'owner','occupancyCharges',{id:'occ-deluxe',childAgeTo:11}),'occupancyCharges','X-DELUXE').childAgeTo,11);});

  // ── Rooms: edit, type change, renumber ──
  test('[RMS] edit a room through MST_MASTER_SAVE: smoking, extension, notes, amenities stored on the Room; history',()=>{
    const s=save(seed(),'techadmin','rooms',{id:'105',smoking:true,extension:'2105',notes:'Garden side',features:['AC'],connecting:'106'});
    assert.deepEqual((({smoking,extension,notes,features,connecting})=>({smoking,extension,notes,features,connecting}))(room(s,'105')),{smoking:true,extension:'2105',notes:'Garden side',features:['AC'],connecting:'106'});
    assert.ok(mstHistory(s,'rooms','105').some(h=>h.field==='extension'&&h.new==='2105'));
    assert.throws(()=>save(seed(),'owner','rooms',{id:'105',extension:'21a'}),reason('MASTER_INVALID'));
    assert.throws(()=>save(seed(),'owner','rooms',{id:'105',connecting:'999'}),reason('MASTER_INVALID'));});
  test('[RMS] change type / floor: free room 105 → Deluxe on F2 (rate follows the rack); room 103 with a booking → MASTER_LOCKED_BY_BOOKINGS',()=>{
    const s=save(seed(),'owner','rooms',{id:'105',type:'DELUXE',floor:'F2'});
    assert.deepEqual([room(s,'105').type,room(s,'105').rate,room(s,'105').floor],['Deluxe',300000,2]);
    assert.throws(()=>save(seed(),'owner','rooms',{id:'103',type:'SUITE'}),e=>reason('MASTER_LOCKED_BY_BOOKINGS')(e)&&/HCC-1049/.test(e.message));
    assert.throws(()=>save(seed(),'owner','rooms',{id:'105',type:'NOPE'}),reason('MASTER_INVALID'));});
  test('[RMS] renumber: free room 105 → 109 (open tasks follow); occupied room 101 locked; taken numbers refused; past stays keep the old number',()=>{
    let s=execute(seed(),'techadmin',{type:'RMS_ROOM_RENUMBER',id:'105',to:'109'});
    assert.ok(room(s,'109')&&!room(s,'105'));assert.equal(s.audit[0].detail,'Master Rooms: code 105 → 109 (renumbered; past stays keep room 105)');
    assert.throws(()=>execute(seed(),'owner',{type:'RMS_ROOM_RENUMBER',id:'101',to:'111'}),reason('MASTER_LOCKED_BY_BOOKINGS'));
    assert.throws(()=>execute(seed(),'owner',{type:'RMS_ROOM_RENUMBER',id:'105',to:'102'}),reason('MASTER_CODE_TAKEN'));
    s=execute(seed(),'owner',{type:'BOOK',guest:'Old Stay',room:'105',arrival:'2026-10-24',departure:'2026-10-25',people:1});const bid=s.bookings.at(-1).id;
    s=execute(s,'owner',{type:'CANCEL',id:bid,reason:'Plans changed'});s=execute(s,'owner',{type:'RMS_ROOM_RENUMBER',id:'105',to:'110'});
    assert.equal(s.bookings.find(b=>b.id===bid).room,'105');assert.match(M.rmsNumberTaken(s,'105'),/past stays/);
    const t=execute(seed(),'owner',{type:'RMS_ROOM_RENUMBER',id:'108',to:'118'});assert.ok(t.tasks.some(x=>x.room==='118'),'open housekeeping task follows');});
  test('[RMS] the generic code change of a used room is refused (MASTER_IN_USE): renumber goes through the tool',()=>{
    assert.throws(()=>save(seed(),'owner','rooms',{id:'101',code:'111'}),reason('MASTER_IN_USE'));});

  // ── Out of order (room hold) ──
  test('[RMS] out of order 105 for 25–27 Oct: not bookable those nights, bookable outside; board "Blocked"; calendar bar; sellable −1 on those nights',()=>{
    const s=oooNights(seed(),'105','2026-10-25','2026-10-27');
    assert.equal(available(s,'105','2026-10-25','2026-10-26'),false);assert.equal(available(s,'105','2026-10-24','2026-10-25'),true);assert.equal(available(s,'105','2026-10-27','2026-10-28'),true);
    assert.throws(()=>execute(s,'owner',{type:'BOOK',guest:'Xavier Test',room:'105',arrival:'2026-10-25',departure:'2026-10-26',people:1}),/not available/);
    assert.equal(M.roomHold(s,'105','2026-10-25','2026-10-26').label,'Out of order · AC compressor replaced');
    const cal=M.resCalendar(s,'2026-10-24',7);const row=cal.groups.flatMap(g=>g.rows).find(r=>r.room.id==='105');
    assert.ok(row.bars.some(b=>b.kind==='block'&&/Out of order/.test(b.label)));
    assert.equal(M.yldOcc(s,'2026-10-25').sellable,M.yldOcc(seed(),'2026-10-25').sellable-1,'yield counts follow');assert.equal(metrics(s).sellable,metrics(seed()).sellable,'today is not out of order');
    const t=oooNights(seed(),'105','2026-10-24');assert.equal(metrics(t).sellable,metrics(seed()).sellable-1);assert.equal(M.roomStatus(t,room(t,'105')),'Blocked');});
  test('[RMS] out of order over a live booking → MASTER_LOCKED_BY_BOOKINGS; past start / bad window / short reason refused; clear → bookable',()=>{
    assert.throws(()=>oooNights(seed(),'103','2026-10-25'),reason('MASTER_LOCKED_BY_BOOKINGS'));
    assert.throws(()=>oooNights(seed(),'105','2026-10-20'),reason('MASTER_INVALID'));
    assert.throws(()=>oooNights(seed(),'105','2026-10-26','2026-10-25'),reason('MASTER_INVALID'));
    assert.throws(()=>execute(seed(),'owner',{type:'RMS_ROOM_OOO',id:'105',from:'2026-10-25',reason:'x'}),reason('MASTER_INVALID'));
    let s=oooNights(seed(),'105','2026-10-25');assert.equal(room(s,'105').status,'out-of-order');assert.equal(M.rmsOooRooms(s).length,1);
    s=execute(s,'owner',{type:'RMS_ROOM_OOO_CLEAR',id:'105'});assert.equal(room(s,'105').status,undefined);assert.equal(available(s,'105','2026-10-25','2026-10-26'),true);
    assert.deepEqual(mstHistory(s,'rooms','105').map(h=>h.new),['in service','out of order 2026-10-25–open · AC compressor replaced']);});

  // ── Retire / reactivate / delete ──
  test('[RMS] retire a free room: hidden from boards and calendar, not bookable, not sellable, kept in history; reactivate restores it',()=>{
    let s=execute(seed(),'owner',{type:'MST_MASTER_RETIRE',master:'rooms',id:'105'});
    assert.deepEqual([room(s,'105').status,room(s,'105').retiredOn],['retired','2026-10-24']);
    assert.ok(!M.rmsBoardRooms(s).some(r=>r.id==='105'));assert.equal(available(s,'105','2026-10-25','2026-10-26'),false);
    assert.equal(metrics(s).sellable,metrics(seed()).sellable-1);assert.ok(!M.resCalendar(s,s.date,7).groups.flatMap(g=>g.rows).some(r=>r.room.id==='105'));
    assert.equal(mstByCode(s,'rooms','105').active,false);
    s=execute(s,'owner',{type:'MST_MASTER_REACTIVATE',master:'rooms',id:'105'});assert.equal(room(s,'105').status,undefined);assert.equal(available(s,'105','2026-10-25','2026-10-26'),true);});
  test('[RMS] retiring a room with a live booking is refused (MASTER_IN_USE); deleting a used room refused; a never-used new room can be deleted',()=>{
    assert.throws(()=>execute(seed(),'owner',{type:'MST_MASTER_RETIRE',master:'rooms',id:'101'}),reason('MASTER_IN_USE'));
    assert.throws(()=>execute(seed(),'owner',{type:'MST_MASTER_DELETE',master:'rooms',id:'101'}),reason('MASTER_IN_USE'));
    let s=add(seed(),'owner','109','F1','CLASSIC');s=execute(s,'owner',{type:'MST_MASTER_DELETE',master:'rooms',id:'109'});assert.equal(room(s,'109'),undefined);});

  // ── V14-5: rooms added today are bookable; retired / out-of-order rooms are excluded with their own reason (BASE and FY26) ──
  let fy26;const FY=()=>fy26??=M.buildHistory(M.baseFixture());
  const withNew=s=>add(F4(s),'owner','401-403','F4','DELUXE');
  const search=(s,a,d)=>M.resSearch(s,{arrival:a,departure:d,people:2});
  const excl=(r,id)=>r.excluded.find(x=>x.room.id===id);
  for(const [name,mk] of [['BASE',()=>seed()],['FY26',FY]]){
    test(`[RMS] V14-5 ${name}: rooms 401–403 added today are bookable from the business date; the open group block keeps holding original rooms, shown as "Group hold"`,()=>{
      const s=withNew(mk()),a=s.date,d=M.addDays(s.date,2);
      assert.equal(a,'2026-10-24');assert.deepEqual(['401','402','403'].map(id=>room(s,id).addedOn),['2026-10-24','2026-10-24','2026-10-24']);
      for(const id of ['401','402','403']){assert.equal(available(s,id,a,d),true,id);assert.equal(M.roomHold(s,id,a,d),undefined,id);}
      const r=search(s,a,d);assert.deepEqual(['401','402','403'].map(id=>r.available.some(x=>x.room.id===id)),[true,true,true]);
      const held=M.grpHolds(s).filter(h=>h.block==='GB-4');assert.equal(held.length,3,'GB-4 still holds its 3 Deluxe rooms');
      assert.deepEqual(held.map(h=>h.room).sort(),M.grpHolds(mk()).filter(h=>h.block==='GB-4').map(h=>h.room).sort(),'the same rooms as before the add');
      for(const h of held){const x=excl(r,h.room);assert.deepEqual([x.reasons,x.reason],[['Group hold'],'Group hold · GB-4 Sahyadri Infra site team'],h.room);}
      assert.ok(!r.excluded.some(x=>/another reservation/.test(x.reason)),'no unexplained "Booked · another reservation"');
      const t=execute(s,'reception',{type:'BOOK',guest:'Test Guest',phone:'+91 90000 00000',people:2,children:0,room:'401',arrival:a,departure:d,source:'Walk-in',key:'v145-'+name});
      assert.equal(t.bookings.find(b=>b.room==='401'&&b.arrival===a).status,'Confirmed');});
    test(`[RMS] V14-5 ${name}: a retired room is never bookable and is excluded with the reason "Retired"; a group block never holds it`,()=>{
      let s=withNew(mk());s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'rooms',id:'403'});
      for(const [a,d] of [[s.date,M.addDays(s.date,2)],['2026-12-01','2026-12-03']]){
        assert.equal(available(s,'403',a,d),false,a);const x=excl(search(s,a,d),'403');assert.deepEqual([x.reasons[0],x.reason],['Retired','Retired'],a);}
      assert.ok(!M.grpHolds(s).some(h=>h.room==='403'));
      const gb=M.grpHolds(s).filter(h=>h.block==='GB-4').map(h=>h.room),victim=gb[0];
      const t=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'rooms',id:victim});
      const after=M.grpHolds(t).filter(h=>h.block==='GB-4').map(h=>h.room);
      assert.equal(after.length,3,'the hold moves to another free Deluxe room');assert.ok(!after.includes(victim));
      assert.equal(excl(search(t,t.date,M.addDays(t.date,2)),victim).reason,'Retired');});
    test(`[RMS] V14-5 ${name}: an out-of-order room is excluded with "Out of order until <date>" (open-ended: "until further notice") and bookable after the window`,()=>{
      const s=withNew(mk()),a=s.date,d=M.addDays(s.date,2);
      let t=oooNights(s,'401',a,'2026-10-28');
      let x=excl(search(t,a,d),'401');assert.deepEqual([x.reasons,x.reason],[['Out of order'],'Out of order until 28 Oct 2026']);
      assert.equal(available(t,'401','2026-12-01','2026-12-03'),true);assert.ok(!M.grpHolds(t).some(h=>h.room==='401'));
      t=oooNights(s,'402',a);x=excl(search(t,'2026-12-01','2026-12-03'),'402');assert.equal(x.reason,'Out of order until further notice');});
  }

  // ── Amenities, booking site, extra charges ──
  test('[RMS] amenities: add one, used by a type → cannot delete; the booking-site copy shows bed + website amenities from the master',()=>{
    let s=save(seed(),'techadmin','amenities',{code:'POOL',name:'Pool access',icon:'waves',showOnWebsite:true});
    s=save(s,'techadmin','roomTypes',{id:'rty-suite',amenities:['LIVING','BATH','BFAST','POOL']});
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_DELETE',master:'amenities',id:mstByCode(s,'amenities','POOL').id}),reason('MASTER_IN_USE'));
    const c=M.rmsTypeCopy(s,'Suite');assert.deepEqual(c.features,['King bed','Living room','Bathtub','Breakfast for 2','Pool access']);assert.equal(c.sleeps,4);
    assert.deepEqual(M.rmsTypeCopy(seed(),'Classic').features,['Queen bed','Wi-Fi','Tea & coffee'],'same as the old fixed copy');});
  test('[RMS] occupancy charges: quote from the master; Classic extra bed refused; one active row per type; age band checked',()=>{
    const s=seed();const q=M.rmsExtraQuote(s,'HCC-1046','bed',1,2);
    assert.deepEqual(q,{label:'Extra bed × 1 · 2 nights · Suite (X-SUITE)',amount:200000,gst:'other',perNight:100000});
    assert.match(M.rmsExtraQuote(s,'HCC-1041','bed',1,1).error,/does not allow an extra bed/);
    assert.throws(()=>save(s,'owner','occupancyCharges',{code:'X2',name:'Second',roomType:'SUITE'}),reason('MASTER_INVALID'));
    assert.throws(()=>save(s,'owner','occupancyCharges',{id:'occ-suite',childAgeFrom:13,childAgeTo:12}),reason('MASTER_INVALID'));});
  test('[RMS] posting an extra uses the existing CHARGE: service line at 18 % (other services); the charge row is then in use',()=>{
    let s=seed();const q=M.rmsExtraQuote(s,'HCC-1046','adult',1,1);
    s=execute(s,'cashier',{type:'CHARGE',id:'HCC-1046',label:q.label,amount:q.amount,gst:q.gst});
    const l=s.bookings.find(b=>b.id==='HCC-1046').lines.at(-1);assert.deepEqual([l.amount,l.taxBps,l.kind],[80000,1800,'service']);
    assert.equal(M.mstUsage(s,mstSpec('occupancyCharges'),mstByCode(s,'occupancyCharges','X-SUITE')).count,1);});

  // ── UI wiring (source checks) + render ──
  test('[RMS] floor names (MA-0 known gaps): Overview strip and room pickers read the Floors master, no "Floor 0n" / "floor {r.floor}"',()=>{
    const ov=src('src/features/overview/Overview.tsx');assert.match(ov,/mstFloorGroups\(s\)\.map/);assert.doesNotMatch(ov,/\[1,2,3\]\.map|Floor \{f/);
    for(const f of ['src/features/channels/modals.tsx','src/features/guests/CheckIn.tsx','src/features/guests/InHouse.tsx','src/mobile/frontdesk/Arrivals.tsx'])
      assert.doesNotMatch(src(f),/floor \{r\.floor\}|Floor \{r\.floor\}/,f);
    const s=save(seed(),'owner','floors',{id:'flr-1',code:'F1',name:'Ground'});assert.equal(M.rmsRoomLine(s,room(s,'101')),'Classic · Ground');});
  test('[RMS] registration: Rooms tools (Add hidden, no reorder), modal rms-extra desktop + phone, booking-dialog slot; no new page; sync mapping',()=>{
    const reg=src('src/features/roommasters/register.tsx');assert.match(reg,/registerMasterTools\('rooms',\{Tools:RoomTools,hideAdd:true,noReorder:true,noReorderNote:'Rooms are listed by floor and room number[^']*'\}\)/); /* V14-7 old → new: noReorderNote added (visible reason) */
    assert.match(reg,/modals:\{'rms-extra':ExtraModal\}/);assert.match(reg,/mobile:\{modals:\{'rms-extra':ExtraModal\}\}/);assert.match(reg,/bookingActions:RmsBookingActions/);
    assert.match(src('src/app/registry.ts'),/m_admin,(\w+,)*roommasters[,\]]/);assert.doesNotMatch(src('src/app/navmap.ts'),/roommasters|rms-/);
    assert.match(src('src/features/masters/MasterList.tsx'),/ext\?\.Tools&&<ext\.Tools/);assert.match(src('src/mobile/masters/MasterListPhone.tsx'),/ext\?\.Tools&&<ext\.Tools specId=\{specId\} phone\/>/);});

  // Render the masters UI on desktop and phone (lists, forms, tools, extra dialog) for Owner, Tech admin and Housekeeper.
  const dir='tests/.generated';await mkdir(dir,{recursive:true});
  await writeFile(dir+'/rms-render-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics,roomStatus} from '../../src/services/mock';
import {navItems} from '../../src/app/nav';import '../../src/app/registry';void navItems;
import {MasterList} from '../../src/features/masters/MasterList';
import {MasterForm} from '../../src/features/masters/MasterForm';
import {MasterListPhone} from '../../src/mobile/masters/MasterListPhone';
import {ExtraModal} from '../../src/features/roommasters/ExtraModal';
import {Overview} from '../../src/features/overview/Overview';
import {RoomBoard} from '../../src/features/rooms/RoomBoard';
import {GuestPreview} from '../../src/features/channels/BookingSite';
function ctx(s:any,uid:string,mobile:boolean):any{const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid);
  return {s,uid,user,m:metrics(s),page:'settings',go:noop,modal:{type:'rms-extra',id:'HCC-1046',key:'k'},open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),act:()=>null,search:'',setSearch:noop,filter:'All',setFilter:noop,tab:'Arrivals',setTab:noop,arrivals:[],departures:[],pending:0,taskList:[],
    roomStatus:(r:any)=>roomStatus(s,r),switchUser:noop,fault:'none',setFault:noop,loadScenario:noop,resetScenario:noop,
    display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop,openSearch:noop,openMenu:noop,signOut:noop};}
export function render(s:any,uid:string){
  const out:Record<string,string>={},errors:Record<string,string>={};
  const els:Record<string,[boolean,()=>any]>={};
  for(const id of ['roomTypes','rooms','amenities','occupancyCharges']){els['list:'+id]=[false,()=><MasterList spec={id} select={id==='rooms'?'101':undefined}/>];els['phone:'+id]=[true,()=><MasterListPhone spec={id}/>];}
  els['form:type']=[false,()=><MasterForm specId="roomTypes" rowId="rty-deluxe"/>];els['form:room']=[false,()=><MasterForm specId="rooms" rowId="105"/>];
  els['form:occ']=[true,()=><MasterForm specId="occupancyCharges" rowId="occ-suite" sheet/>];els['extra']=[false,()=><ExtraModal id="HCC-1046"/>];
  els['overview']=[false,()=><Overview/>];els['board']=[false,()=><RoomBoard/>];els['site']=[false,()=><GuestPreview/>];
  for(const [k,[m,el]] of Object.entries(els)){try{out[k]=renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid,m)}>{el()}</HccContext.Provider>);}catch(e:any){errors[k]=String(e?.message??e).slice(0,300);}}
  return {out,errors};
}`);
  await build({entryPoints:[dir+'/rms-render-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:dir+'/rms-render.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __rReq} from 'node:module';const require=__rReq(import.meta.url);"}});
  const R=await import(new URL('./.generated/rms-render.mjs',import.meta.url).href);
  let st=save(seed(),'owner','floors',{id:'flr-1',code:'F1',name:'Ground'});st=oooNights(st,'105','2026-10-25');st=execute(st,'owner',{type:'MST_MASTER_RETIRE',master:'rooms',id:'108'});
  const own=R.render(st,'owner'),tech=R.render(st,'techadmin'),hk=R.render(st,'housekeeper');
  test('[RMS] the masters UI renders (desktop + phone lists, forms, tools, extra dialog, Overview, board, booking site) for Owner, Tech admin, Housekeeper',()=>{
    assert.deepEqual([own.errors,tech.errors,hk.errors],[{},{},{}]);
    assert.match(own.out['list:rooms'],/Add rooms/);assert.match(own.out['list:rooms'],/Renumber a room/);assert.match(own.out['list:rooms'],/Room 105/);
    assert.doesNotMatch(own.out['list:rooms'],/Add room</);assert.doesNotMatch(own.out['list:rooms'],/Move Room 101 up/);
    assert.match(own.out['phone:rooms'],/rms-phone/);assert.match(own.out['overview'],/Ground/);assert.doesNotMatch(own.out['overview'],/Floor 01/);
    assert.doesNotMatch(own.out['board'],/>108</,'retired room 108 hidden from the board');assert.match(own.out['extra'],/Extra person or bed/);
    assert.match(own.out['site'],/Separate living area/);});
  test('[RMS] money fields hidden from Housekeeper (DEC-061): no rack rate, no extra-charge amounts; tools read-only without masters',()=>{
    assert.doesNotMatch(hk.out['form:type'],/Rack rate/);assert.match(own.out['form:type'],/Rack rate/);
    assert.doesNotMatch(hk.out['form:occ'],/Extra adult per night/);assert.doesNotMatch(hk.out['list:rooms'],/Add rooms/);
    assert.match(tech.out['list:rooms'],/Add rooms/);assert.match(tech.out['form:occ'],/Owner changes this|Owner only|View only/);});
}
