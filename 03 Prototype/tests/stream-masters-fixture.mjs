// MA-9 · Fixture and history alignment of the masters wave. Names prefixed [MFX].
// One seed module (src/fixtures/ext/masters-seed.ts) feeds every master; BASE is never edited and FY26 = BASE + history, so no figure moves.
// (a) old constants gone or marked LEGACY · (b) every historical record resolves to a master row · (c) seed rows: unique codes and sort
// orders, nothing retired · (d) golden figures and the BASE / FY26 hashes · (e) the demo-data switch.
import {readFileSync,readdirSync,statSync} from 'node:fs';
import {join} from 'node:path';
import {createHash} from 'node:crypto';
export function register({test,M,assert}){
  const {seed,loadScenario,buildHistory,baseFixture,metrics,drawer,masterSpecs,mstRows,mstByCode,mstSpec,mstValidate,mstFloorOf,mastersSeed,mastersSeedIds,MASTERS_SEED,
    MASTER_FIXTURE_BACKED,MASTERS_SEED_OPTIONS,setMastersSeedOptions,mnyMethod,mnyCategory,mnyVendor,fdmSourceOf,rmsTypeRow,rmsRoomTypeKeys,adminRoomTypes,hrDesignationOf,
    hrShiftPattern,hrLeaveTypeRow,hkTypeOfTask,hkTaskKind,gcatResolve,lfcResolve,posOutletIdOf,posItemCategoryCode,posTableRows}=M;
  const sha=o=>createHash('sha256').update(JSON.stringify(o)).digest('hex');
  const strip=s=>{const {mastersX,grants,...r}=s;return r;};
  const src=f=>readFileSync(f,'utf8');
  const walk=(d,out=[])=>{for(const f of readdirSync(d)){const p=join(d,f);if(statSync(p).isDirectory())walk(p,out);else if(/\.(ts|tsx)$/.test(f))out.push(p);}return out;};
  const SRC=walk('src');
  const fy=(()=>{let x;return ()=>x??=loadScenario('FY26');})();
  const states=()=>[['BASE',seed()],['FY26',fy()]];
  const lines=f=>src(f).split(/\r?\n/);
  /** The definition line of `export const NAME` / `export function NAME` and the three lines above it. */
  const defBlock=(file,name)=>{const l=lines(file),i=l.findIndex(x=>new RegExp(`^export (const|function) ${name}\\b`).test(x));return i<0?undefined:l.slice(Math.max(0,i-3),i+1).join('\n');};

  // ── (a) old constants gone or marked LEGACY ──
  test('[MFX] old constants are marked LEGACY where they remain: EXPENSE_CATEGORIES / EXPENSE_METHODS / TENDERS (closing), FOLIO_TENDERS, POS_TENDERS, adminRoomTypes',()=>{
    for(const [file,name] of [['src/domain/streams/closing.ts','EXPENSE_CATEGORIES'],['src/domain/streams/closing.ts','EXPENSE_METHODS'],['src/domain/streams/closing.ts','TENDERS'],
      ['src/domain/streams/folio.ts','FOLIO_TENDERS'],['src/domain/streams/pos.ts','POS_TENDERS'],['src/domain/streams/admin.ts','adminRoomTypes']]){
      const b=defBlock(file,name);if(b===undefined)continue; // gone is fine
      assert.match(b,/LEGACY/,`${name} in ${file} must be gone or marked LEGACY:\n${b}`);}});
  test('[MFX] no code reads the legacy payment / category constants (they are the seed values only); the pick lists come from the money masters',()=>{
    const bad=[];
    for(const f of SRC){const t=src(f);
      for(const m of t.matchAll(/(^|[^\w'"])(EXPENSE_CATEGORIES|EXPENSE_METHODS|FOLIO_TENDERS|POS_TENDERS|TENDERS)\b/gm)){
        const lineStart=t.lastIndexOf('\n',m.index)+1,line=t.slice(lineStart,t.indexOf('\n',m.index));
        if(/^\s*(\/\/|\/\*|\*)/.test(line)||/^export const (EXPENSE_CATEGORIES|EXPENSE_METHODS|FOLIO_TENDERS|POS_TENDERS|TENDERS)\b/.test(line))continue;
        bad.push(`${f}: ${line.trim().slice(0,100)}`);}}
    assert.deepEqual(bad,[]);});
  test('[MFX] adminRoomTypes (LEGACY derivation) agrees with the room-types master selector on BASE and FY26',()=>{
    for(const [n,s] of states())assert.deepEqual([...adminRoomTypes(s)].sort(),[...rmsRoomTypeKeys(s)].sort(),n);
    // And once the room-types master is stored the order follows it in both.
    const s=M.execute(seed(),'owner',{type:'MST_MASTER_REORDER',master:'roomTypes',ids:['rty-suite','rty-deluxe','rty-classic']});
    assert.deepEqual(adminRoomTypes(s),['Suite','Deluxe','Classic']);assert.deepEqual(rmsRoomTypeKeys(s),['Suite','Deluxe','Classic']);});
  test('[MFX] every spec seed hook reads the one seed module (no per-agent seed rows left in the domain)',()=>{
    const bad=[];
    for(const f of SRC.filter(x=>x.replace(/\\/g,'/').startsWith('src/domain/'))){
      for(const [i,line] of lines(f).entries()){const m=/(^|[\s,{])seed:(.*)$/.exec(line);if(!m||/^\s*(\/\/|\*|\/\*)/.test(line))continue;
        const v=m[2].trim();
        if(/^(s=>|\(\)=>)?\(?\s*(mastersSeed\(|mstSeedFloors|posSeedCategories|hkzSeed|catSeed\w+\(\))/.test(v)||/^mstSeedFloors|^posSeedCategories|^hkzSeed/.test(v))continue;
        if(/^seed\??:\(s:State\)=>T\[\]/.test(v)||/^\(s:State\)=>T\[\]/.test(v))continue; // the framework's own type
        bad.push(`${f}:${i+1}: ${line.trim().slice(0,110)}`);}}
    assert.deepEqual(bad,[]);});
  test('[MFX] seed row literals live only in masters-seed.ts (payment methods, categories, sources, series, meal plans … are not redefined elsewhere)',()=>{
    const ids=["id:'pm-cash'","id:'ba-till'","id:'ec-supplies'","id:'bsrc-walkin'","id:'nsr-inv'","id:'idt-aadhaar'","id:'pol-default'","id:'mpl-ep'","id:'ext-laundry'","id:'dsr-service'",
      "id:'hkt-dep'","id:'amn-'","id:'shp-'","id:'lvt-'","id:'hol-'","id:'gcc-'","id:'lfc-'","id:'dsg-'","id:'ev-'","id:'mcat-'","id:'hkz-'","id:'rty-'","id:'occ-'","id:'flr-'"];
    const where=[];
    for(const f of SRC){if(f.replace(/\\/g,'/').endsWith('fixtures/ext/masters-seed.ts'))continue;const t=src(f);for(const id of ids)if(t.includes(id))where.push(`${f} ${id}`);}
    assert.deepEqual(where,[]);
    assert.ok(src('src/fixtures/ext/masters-seed.ts').includes("id:'pm-cash'"));});

  // ── (c) seed rows ──
  test('[MFX] every registered master is seeded or fixture-backed (none forgotten); seeded ids are all registered',()=>{
    const ids=masterSpecs().map(x=>x.id),seeded=new Set(mastersSeedIds()),backed=new Set(Object.keys(MASTER_FIXTURE_BACKED));
    assert.deepEqual(ids.filter(i=>!seeded.has(i)&&!backed.has(i)),[]);
    assert.deepEqual([...seeded].filter(i=>!ids.includes(i)),[]);
    assert.deepEqual([...backed].filter(i=>!ids.includes(i)),[]);
    assert.equal(ids.length,seeded.size+backed.size);});
  test('[MFX] seed rows of every master (BASE and FY26): unique ids, codes (case-insensitive) and sort orders; ascending; all active, none retired',()=>{
    for(const [n,s] of states())for(const id of mastersSeedIds()){
      const rows=mastersSeed(id,s),tag=`${n} ${id}`;
      for(const r of rows){assert.equal(r.active,true,`${tag} ${r.code} must be active`);assert.equal(r.retiredOn,undefined,`${tag} ${r.code} retiredOn`);}
      for(const k of ['id','code','sortOrder']){const v=rows.map(r=>k==='code'?String(r.code).toUpperCase():r[k]);assert.equal(new Set(v).size,v.length,`${tag}: duplicate ${k}: ${v.join(',')}`);}
      assert.deepEqual(rows.map(r=>r.sortOrder),[...rows.map(r=>r.sortOrder)].sort((a,b)=>a-b),`${tag} ascending sort order`);
      assert.ok(rows.every(r=>/^[A-Z0-9-]{1,20}$/.test(r.code)||id==='holidays'&&/^\d{4}-\d{2}-\d{2}$/.test(r.code)),`${tag} code pattern`);
      assert.ok(rows.every(r=>r.name&&r.name.length<=60),`${tag} names`);}});
  test('[MFX] the spec rows before any save ARE the seed (mstRows = mastersSeed) and each seed row passes its own spec validation',()=>{
    for(const [n,s] of states())for(const id of mastersSeedIds()){
      const rows=mastersSeed(id,s);assert.deepEqual(mstRows(s,id).map(r=>r.id).filter(i=>rows.some(x=>x.id===i)).sort(),rows.map(r=>r.id).sort(),`${n} ${id}`);
      const sp=mstSpec(id);
      for(const r of rows){const p=mstValidate(sp,r,rows,s,r);assert.equal(p,undefined,`${n} ${id} ${r.code}: ${p&&p.message}`);}}
    assert.equal(seed().mastersX,undefined);});
  test('[MFX] no master reports a seed row as used by a record it cannot resolve: in-use counts are numbers for every seed row',()=>{
    for(const [n,s] of states())for(const sp of masterSpecs())for(const r of mstRows(s,sp.id)){const u=sp.inUse(s,r);assert.ok(Number.isInteger(u.count)&&u.count>=0,`${n} ${sp.id} ${r.code}`);}});

  // ── (b) every historical record resolves to a master row ──
  test('[MFX] rooms: every floor and type resolves (FY26 = BASE rooms)',()=>{
    for(const [n,s] of states())for(const r of s.rooms){
      assert.ok(mstFloorOf(s,r),`${n} room ${r.id} floor ${r.floor}`);assert.ok(rmsTypeRow(s,r.type),`${n} room ${r.id} type ${r.type}`);
      for(const f of r.features??[])assert.ok(mstByCode(s,'amenities',f),`${n} room ${r.id} feature ${f}`);}
    for(const [n,s] of states())assert.deepEqual(mstRows(s,'rooms').map(r=>r.code).sort(),s.rooms.map(r=>r.id).sort(),n);});
  test('[MFX] money: every folio receipt, POS receipt and expense method resolves to a payment method; categories and vendors to their masters; refunds too',()=>{
    for(const [n,s] of states()){
      const miss=new Set();
      for(const b of s.bookings)for(const r of b.receipts)if(!mnyMethod(s,r.method))miss.add('receipt '+r.method);
      for(const r of s.posX?.receipts??[])if(!mnyMethod(s,r.method))miss.add('pos receipt '+r.method);
      for(const e of s.expenses){if(!mnyMethod(s,e.method))miss.add('expense method '+e.method);if(!mnyCategory(s,e.category))miss.add('category '+e.category);
        if(e.vendor&&!mnyVendor(s,e.vendor))miss.add('vendor '+e.vendor);}
      assert.deepEqual([...miss],[],n);}});
  test('[MFX] bookings: every source resolves to a booking-source row; plans, catalogue lines and discount reasons (when present) to theirs',()=>{
    for(const [n,s] of states()){
      const miss=new Set();
      for(const b of s.bookings){if(!fdmSourceOf(s,b))miss.add('source '+b.source);
        if(b.plan&&!mstByCode(s,'mealPlans',b.plan))miss.add('plan '+b.plan);
        for(const l of b.lines){if(l.extraId&&!mstRows(s,'extras').some(x=>x.id===l.extraId))miss.add('extra '+l.extraId);
          if(l.discountReason&&!mstByCode(s,'discountReasons',l.discountReason))miss.add('reason '+l.discountReason);
          if(l.planCode&&!mstByCode(s,'mealPlans',l.planCode))miss.add('planCode '+l.planCode);}}
      assert.deepEqual([...miss],[],n);}});
  test('[MFX] guests: every ID type on a guest profile resolves to an ID-type row',()=>{
    for(const [n,s] of states()){const names=new Set(mstRows(s,'idTypes').map(r=>r.name)),miss=new Set();
      for(const g of s.guests??[])if(g.idType&&!names.has(g.idType))miss.add(g.idType);
      assert.deepEqual([...miss],[],n);}});
  test('[MFX] people: staff designations, default shifts, roster shifts and leave types resolve to their masters',()=>{
    for(const [n,s] of states()){const miss=new Set(),st=s.staffX;
      for(const x of st.staff){if(!hrDesignationOf(s,x))miss.add('designation '+x.designation);if(x.defaultShift&&!hrShiftPattern(s,x.defaultShift))miss.add('default shift '+x.defaultShift);}
      for(const [k,v] of Object.entries(st.roster??{}))if(typeof v==='string'&&!hrShiftPattern(s,v))miss.add('roster '+k+' '+v);
      for(const l of st.leaves??[])if(!hrLeaveTypeRow(s,l.type))miss.add('leave '+l.type);
      assert.deepEqual([...miss],[],n);}});
  test('[MFX] housekeeping and guest services: every non-repair task has a task type; complaint and found-item categories resolve',()=>{
    for(const [n,s] of states()){const miss=new Set();
      for(const t of s.tasks){const k=hkTaskKind(t);if(k!=='repair'&&!hkTypeOfTask(s,t))miss.add('task kind '+k);}
      for(const c of s.servicesX.complaints)if(!gcatResolve(s,c.categoryCode??c.category,false))miss.add('complaint '+c.category);
      for(const i of s.servicesX.items)if(!lfcResolve(s,i.categoryCode??i.category,false))miss.add('found item '+i.category);
      assert.deepEqual([...miss],[],n);}});
  test('[MFX] POS: every order outlet, table and every menu item category resolves; every menu item has a category row',()=>{
    for(const [n,s] of states()){const miss=new Set(),outlets=new Map((s.posX.outlets).map(o=>[o.id,o]));
      for(const o of s.posX.orders){const out=outlets.get(o.outlet);if(!out){miss.add('outlet '+o.outlet);continue;}
        if(o.table&&out.kind==='table'&&!posTableRows(out).some(t=>t.tableNo===o.table))miss.add(`table ${o.outlet}/${o.table}`);}
      for(const o of s.posX.outlets)if(posOutletIdOf(s,mstRows(s,'outlets').find(r=>r.id===o.id)?.code)!==o.id)miss.add('outlet master '+o.id);
      for(const m of s.posX.menu)if(!mstByCode(s,'menuCategories',posItemCategoryCode(s,m)))miss.add('category '+m.category);
      assert.deepEqual([...miss],[],n);}});
  test('[MFX] every master in-use count is consistent with the records: a seeded payment method used by a record is never reported unused',()=>{
    const s=fy(),sp=mstSpec('paymentMethods');
    const used=new Set([...s.bookings.flatMap(b=>b.receipts.map(r=>r.method)),...(s.posX.receipts??[]).map(r=>r.method),...s.expenses.map(e=>e.method)]);
    for(const r of mstRows(s,'paymentMethods')){const n=sp.inUse(s,r).count;assert.equal(n>0,[...used].some(u=>mnyMethod(s,u)?.id===r.id),r.code);}});

  // ── (d) golden figures, BASE untouched, FY26 determinism ──
  test('[MFX] golden figures unchanged: occupancy 8, collections ₹21,500 (2150000), drawer ₹10,725 (1072500)',()=>{
    const s=seed();assert.equal(metrics(s).occupied,8);assert.equal(metrics(s).collections,2150000);assert.equal(drawer(s),1072500);});
  test('[MFX] BASE is untouched by the seed module: reading every master seed and spec changes nothing; BASE hash and FY26 history / scenario hashes are the pinned ones',()=>{
    const s=seed(),before=sha(s);
    for(const id of mastersSeedIds())mastersSeed(id,s);for(const sp of masterSpecs()){for(const r of mstRows(s,sp.id))sp.inUse(s,r);}
    assert.equal(sha(s),before);assert.equal(s.mastersX,undefined);
    assert.equal(sha(strip(seed())),'a2cbb5cf1a5240c5e12d58f77d566ba20ffbdd2353d1955e7ab4bf3a29be503c');
    assert.equal(sha(strip(buildHistory(baseFixture()))),'1b16471e6ebf47ab0e4661bb3b4f46adaa24888514a89e79cbeca510200dd2ad');
    assert.equal(sha(loadScenario('FY26').bookings),'66e8195233df0bb433ef76dfe58c83e215f93473a90b5bbf8b2c08613d5dbfce');});
  test('[MFX] FY26 = BASE + history: same FY26 whole-state hash as tests/stream-perf-store.mjs; no stored masters slice; BASE rooms / users identical inside FY26',()=>{
    const f=loadScenario('FY26');
    assert.equal(sha(f),'0e29ff52e666358f297c83782f44f7b337496dfa64c2ff70be3cb91085186b9c');
    assert.equal(f.mastersX,undefined);assert.equal(sha(f.rooms),sha(seed().rooms));assert.equal(sha(f.users),sha(seed().users));});
  test('[MFX] FY26 history is deterministic (two builds identical) and the seed reads do not depend on it being built first',()=>{
    assert.equal(sha(buildHistory(baseFixture())),sha(buildHistory(baseFixture())));
    for(const id of mastersSeedIds()){const have=mstRows(fy(),id).map(r=>r.code);for(const r of mastersSeed(id,fy()))assert.ok(have.includes(r.code),id+' '+r.code);}});

  // ── (e) demo data ──
  test('[MFX] demo data is marked and behind ONE switch: the example holiday calendar (8 rows, demo:true); off = none, as DEC-083 and the servers',()=>{
    assert.equal(MASTERS_SEED_OPTIONS.demoHolidays,true);
    const s=seed(),rows=mstRows(s,'holidays');assert.equal(rows.length,8);assert.ok(rows.every(r=>r.demo===true));
    // no other master seeds rows marked demo
    for(const id of mastersSeedIds())if(id!=='holidays')assert.ok(mastersSeed(id,s).every(r=>r.demo===undefined),id);
    const prev=setMastersSeedOptions({demoHolidays:false});
    try{assert.deepEqual(mstRows(seed(),'holidays'),[]);assert.deepEqual(mastersSeed('holidays',seed()),[]);}
    finally{setMastersSeedOptions(prev);}
    assert.equal(mstRows(seed(),'holidays').length,8);});
  test('[MFX] the demo switch is documented in the seed module and the people master (one switch, not two)',()=>{
    const t=src('src/fixtures/ext/masters-seed.ts');assert.match(t,/DEMO DATA SWITCH/);assert.match(t,/export const MASTERS_SEED_OPTIONS/);
    const hits=SRC.filter(f=>/demoHolidays/.test(src(f))).map(f=>f.replace(/\\/g,'/')).sort();
    assert.deepEqual(hits,['src/fixtures/ext/masters-seed.ts']);});
  test('[MFX] seed values that the servers also seed keep the same codes: payment methods, bank accounts, expense categories, number series, task types, leave types, shifts',()=>{
    const s=seed(),codes=id=>mstRows(s,id).map(r=>r.code);
    assert.deepEqual(codes('paymentMethods'),['CASH','UPI','CARD','BANK','VCREDIT','COMPANY']);
    assert.deepEqual(codes('bankAccounts'),['TILL','BANK-1']);
    assert.deepEqual(codes('expenseCategories'),['SUPPLIES','LAUNDRY','MAINT','UTIL','PETTY']);
    assert.deepEqual(codes('numberSeries'),['INV','BOS','CRN','RCP','KOT','REG','RST']); // MA-11 (DEC-MA-11d): old list without RST → RST is a seeded row on both sides
    assert.deepEqual(codes('hkTaskTypes'),['DEP','STAY','DEEP','INSP']);
    assert.deepEqual(codes('leaveTypes'),['CL','SL','EL','ML','CO','OTH','LOP']); // SCC leave port (DEC-116): SCC's six in SCC's order + LOP, the same list as the server's SeedLeaveTypes
    assert.deepEqual(codes('shiftPatterns'),['M','E','N','G']);
    assert.deepEqual(codes('floors'),['F1','F2','F3']);});
}
