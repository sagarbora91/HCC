// MA-4 · POS masters (outlets, tables and counters, menu categories, menu items) on the MA-0 masters framework — domain/command tests.
// Names prefixed [POS-MST]. Plan Part 2 §8 F (M26–M28); masters permission = Owner + Tech admin (DEC-MA-1); money fields Owner-only (DEC-058).
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
export function register({test,M,assert}){
  const {seed,execute,mstRows,mstByCode,mstSpec,mstHistory,mstVisibleFields,mstReasonOf,HccError}=M;
  const P=s=>M.posSlice(s);
  const src=f=>readFileSync(f,'utf8');
  const sha=o=>createHash('sha256').update(JSON.stringify(o)).digest('hex');
  const strip=s=>{const {mastersX,grants,...r}=s;return r;};
  const reason=r=>e=>mstReasonOf(e)===r;
  const code=fn=>{try{fn();}catch(e){return e instanceof HccError?e.code:'UNTYPED:'+e.message;}return 'NO_ERROR';};
  const save=(s,u,master,row,key)=>execute(s,u,{type:'MST_MASTER_SAVE',master,row,key});
  const row=(s,master,c)=>mstByCode(s,master,c);
  const ord=(s,id)=>P(s).orders.find(o=>o.id===id);
  const last=s=>P(s).orders.at(-1);
  const next=s=>{const x=structuredClone(s);x.date='2026-10-25';x.clock='2026-10-25T08:00';return x;};
  /** Open a fresh table check at the restaurant with the given items (not sent). */
  const check=(s,tbl='T3',items=[['m-dal',1]],u='cashier',outlet='restaurant')=>{
    s=execute(s,u,{type:'POS_ORDER_OPEN',outlet,table:tbl,covers:2});const id=last(s).id;
    for(const [item,qty] of items)s=execute(s,u,{type:'POS_ITEM_ADD',id,item,qty});return [s,id];};
  const itemRow=(s,c)=>({...row(s,'menuItems',c)});

  // ── Registry and seed ──
  test('[POS-MST] four POS masters registered in "Restaurant and POS" with the masters permission; money fields marked',()=>{
    const ids=['outlets','posTables','menuCategories','menuItems'];
    for(const id of ids){const sp=mstSpec(id);assert.ok(sp,id);assert.equal(sp.group,'pos');assert.equal(sp.permission,'masters');assert.equal(sp.money,false);}
    assert.deepEqual(M.mstSpecsOfGroup('pos').map(x=>x.id),ids);
    const money=id=>mstSpec(id).fields.filter(f=>f.money||f.type==='datedMoney').map(f=>f.key);
    assert.deepEqual(money('outlets'),['gstProfile']);assert.deepEqual(money('menuItems'),['price','gstClass']);assert.deepEqual(money('posTables'),[]);});
  test('[POS-MST] fixture seed: outlets, tables, categories and items read from posX (no copy); codes derived from the fixture ids',()=>{
    const s=seed();
    assert.deepEqual(mstRows(s,'outlets').map(r=>[r.id,r.code,r.name,r.type,r.gstProfile,r.active]),[['restaurant','RESTAURANT','Restaurant','restaurant','hotel',true],['room-service','ROOM-SERVICE','Room service','room-service','hotel',true]]);
    assert.deepEqual(mstRows(s,'posTables').map(r=>r.code),['RESTAURANT-T1','RESTAURANT-T2','RESTAURANT-T3','RESTAURANT-T4','RESTAURANT-T5','RESTAURANT-T6','RESTAURANT-T7','RESTAURANT-T8']);
    assert.deepEqual(mstRows(s,'menuCategories').map(r=>r.code),['BEVERAGES','BREAKFAST','STARTERS','MAINS','BREADS-RICE','DESSERTS']);
    const items=mstRows(s,'menuItems');assert.equal(items.length,P(s).menu.length);
    const tea=row(s,'menuItems','TEA');assert.equal(tea.name,'Masala chai');assert.equal(tea.category,'BEVERAGES');assert.deepEqual(tea.price,[{amount:6000,effectiveFrom:'2017-07-01'}]);
    assert.deepEqual(tea.outlets,['RESTAURANT','ROOM-SERVICE']);assert.equal(tea.gstClass,'food');assert.equal(row(s,'menuItems','BIRYANI').soldOutToday,true);
    assert.equal(s.mastersX,undefined);assert.equal(P(s).outlets[0].code,undefined);});
  test('[POS-MST] BASE and FY26 hashes unchanged; fixture POS figures unchanged (drawer ₹10,725, day summary, open check preview at 5 %)',()=>{
    assert.equal(sha(strip(seed())),'286e328b4eb53ac99f3ae6aa0f2d7e568d186ae79488e10cd4ca754a55cbd91c') /* ACC-2: SCHEMA 11 → 12 only (new optional fields) */ /* ACC-1: SCHEMA 10 → 11 only (accounting chart / opening fields are optional and absent in the seed) */ /* SCC decisions wave (DEC-133): 4a8aad03… → aecae3b2… = the demo firm profile's fictional licence numbers / expiry dates only */ /* SCC port wave (DEC-116, tax + leave): 2c1782df… → 4a8aad03… = compliance + leave slices, SCHEMA 9 → 10 */ /* old → new: d885a20a… → add626d3… = SCHEMA 8 → 9 only (masters wave) */;
    assert.equal(sha(strip(M.buildHistory(M.baseFixture()))),'8aed04aaac441202b710112d66bb0eb69a6d911665d0c3307dc104712f76025f') /* ACC-2: SCHEMA 11 → 12 only (new optional fields) */ /* ACC-1 integration (all seven lanes merged + the ITC / cash-over one-liners): the FY26 history locks its accounting months with accLiveVouchers snapshots, which now carry the re-pointed mapping (POS 4200, ITC 1310 / 1320, cash over 4950), party ledger ids (1000/TILL, 1030/<bank>, 1120/UD-…, 2200/<vendor>) and the Udhaar credit / company payment vouchers: f3601522… → e3a74ba3… */ /* ACC-1: SCHEMA 10 → 11 only */ /* SCC decisions wave (DEC-133): 197643a1… → 5dc19a44… = the demo firm profile's fictional licence fields only */ /* SCC port wave (DEC-116, tax + leave): bf689538… → 197643a1… = compliance + leave slices, SCHEMA 9 → 10 */ /* old → new: ba4a1d4e… → 35e63082… = SCHEMA 8 → 9 only */;
    const s=seed();assert.equal(M.drawer(s),1072500);const d=M.posDaySummary(s);assert.deepEqual([d.checks,d.net,d.tax,d.gross,d.outletCash],[2,118000,5900,123900,52500]);
    const t=M.posOrderTotals(s,ord(s,'POS-F03'));assert.equal(t.taxBps,500);assert.equal(t.net,2*42000+38000+4*7000+2*12000);});

  // ── Outlets ──
  test('[POS-MST] add an outlet (Tech admin): bar with timings and KOT station → posX outlet (tables kind, no tables yet); audit text',()=>{
    const s=save(seed(),'techadmin','outlets',{code:'bar',name:'Rooftop bar',type:'bar',opens:'17:00',closes:'23:30',kotStation:'Bar'});
    const o=P(s).outlets.at(-1);assert.equal(o.name,'Rooftop bar');assert.equal(o.kind,'table');assert.deepEqual(o.tables,[]);assert.equal(o.code,'BAR');assert.equal(o.kotStation,'Bar');
    assert.equal(o.gstProfile,'hotel');assert.equal(M.posOutletHours(o),'17:00–23:30');assert.equal(s.audit[0].detail,'Master Outlets: added BAR · Rooftop bar');
    assert.deepEqual(M.posOutletList(s).map(x=>x.name),['Restaurant','Room service','Rooftop bar']);});
  test('[POS-MST] outlet GST profile is Owner-only (Tech admin FORBIDDEN); Owner fixes 18 % → open check preview and settlement at 18 %',()=>{
    let s=seed();const r=row(s,'outlets','RESTAURANT');
    assert.equal(code(()=>save(s,'techadmin','outlets',{...r,gstProfile:'specified'})),'FORBIDDEN');
    s=save(s,'owner','outlets',{...r,gstProfile:'specified'});
    assert.equal(M.posOrderTotals(s,ord(s,'POS-F03')).taxBps,1800);
    let [x,id]=check(s,'T5',[['m-dal',1]]);x=execute(x,'cashier',{type:'POS_KOT_SEND',id});x=execute(x,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'UPI',reference:'DEMO-1'});
    assert.deepEqual([ord(x,id).settlement.taxBps,ord(x,id).settlement.tax,ord(x,id).settlement.total],[1800,5040,33040]);
    assert.equal(mstHistory(s,'outlets','restaurant')[0].field,'gstProfile');
    for(const u of ['housekeeper','maint','supervisor'])assert.equal(mstVisibleFields(s,u,mstSpec('outlets')).some(f=>f.key==='gstProfile'),false,u);});
  test('[POS-MST] Manager, Reception, Cashier cannot change POS masters (DEC-MA-1); Owner and Tech admin can',()=>{
    for(const u of ['manager','reception','cashier'])assert.equal(code(()=>save(seed(),u,'menuCategories',{code:'SOUPS',name:'Soups'})),'FORBIDDEN',u);
    for(const u of ['owner','techadmin'])assert.ok(row(save(seed(),u,'menuCategories',{code:'SOUPS',name:'Soups'}),'menuCategories','SOUPS'),u);});
  test('[POS-MST] retire an outlet with open checks → MASTER_IN_USE; without open checks it retires, leaves the POS and refuses new checks',()=>{
    let s=seed();const rs=row(s,'outlets','ROOM-SERVICE');
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'outlets',id:rs.id}),reason('MASTER_IN_USE'));
    s=execute(s,'cashier',{type:'POS_VOID_BILL',id:'POS-F04',reason:'Guest cancelled'});
    s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'outlets',id:rs.id});
    assert.equal(P(s).outlets.find(o=>o.id==='room-service').active,false);assert.equal(P(s).outlets.find(o=>o.id==='room-service').retiredOn,s.date);
    assert.deepEqual(M.posOutletList(s).map(o=>o.id),['restaurant']);
    assert.equal(code(()=>execute(s,'reception',{type:'POS_ORDER_OPEN',outlet:'room-service',room:'201'})),'INVALID_TRANSITION');
    assert.equal(M.posOutlet(s,'room-service').name,'Room service'); // old checks keep the outlet name
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_DELETE',master:'outlets',id:rs.id}),reason('MASTER_IN_USE'));});
  test('[POS-MST] outlet rules: duplicate code, code immutable once used, HH:MM timings, table ↔ room-service switch refused once used',()=>{
    const s=seed(),r=row(s,'outlets','RESTAURANT');
    assert.throws(()=>save(s,'owner','outlets',{code:'RESTAURANT',name:'Second'}),reason('MASTER_CODE_TAKEN'));
    assert.throws(()=>save(s,'owner','outlets',{...r,code:'REST'}),reason('MASTER_IN_USE'));
    assert.throws(()=>save(s,'owner','outlets',{code:'CAFE',name:'Cafe',type:'restaurant',opens:'7am'}),reason('MASTER_INVALID'));
    assert.throws(()=>save(s,'owner','outlets',{...r,type:'room-service'}),e=>reason('MASTER_INVALID')(e)&&/cannot be switched/.test(e.message));
    const x=save(s,'owner','outlets',{code:'BQ',name:'Banquet hall',type:'banquet'});const bq=row(x,'outlets','BQ');
    const y=save(x,'owner','outlets',{...bq,type:'room-service'});assert.equal(P(y).outlets.find(o=>o.id===bq.id).kind,'room');
    const z=execute(x,'owner',{type:'MST_MASTER_DELETE',master:'outlets',id:bq.id});assert.equal(P(z).outlets.length,2);});

  // ── Tables and counters ──
  test('[POS-MST] add a table with seats and section (Tech admin): POS spots and check opening use it; legacy tables list kept in step',()=>{
    let s=save(seed(),'techadmin','posTables',{code:'REST-T9',name:'Restaurant T9',outlet:'RESTAURANT',tableNo:'T9',seats:4,section:'Garden'});
    const ts=M.posTablesOf(s,'restaurant');assert.equal(ts.length,9);assert.deepEqual([ts.at(-1).tableNo,ts.at(-1).seats,ts.at(-1).section],['T9',4,'Garden']);
    assert.equal(M.posTableLabel(ts.at(-1)),'Table T9 · 4 seats · Garden');assert.deepEqual(P(s).outlets[0].tables.at(-1),'T9');
    s=execute(s,'cashier',{type:'POS_ORDER_OPEN',outlet:'restaurant',table:'T9',covers:4});assert.equal(last(s).table,'T9');
    assert.equal(code(()=>execute(s,'cashier',{type:'POS_ORDER_OPEN',outlet:'restaurant',table:'T10'})),'VALIDATION');});
  test('[POS-MST] retire a table: refused with an open check (T2), allowed on a free table (T5) which then leaves the POS',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'posTables',id:row(s,'posTables','RESTAURANT-T2').id}),reason('MASTER_IN_USE'));
    s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'posTables',id:row(s,'posTables','RESTAURANT-T5').id});
    assert.ok(!M.posTablesOf(s,'restaurant').some(t=>t.tableNo==='T5'));assert.ok(!P(s).outlets[0].tables.includes('T5'));
    assert.equal(code(()=>execute(s,'cashier',{type:'POS_ORDER_OPEN',outlet:'restaurant',table:'T5'})),'VALIDATION');
    assert.equal(row(s,'posTables','RESTAURANT-T5').active,false);});
  test('[POS-MST] table rules: duplicate number per outlet, no tables on room service, number fixed once used; counters',()=>{
    const s=seed();
    assert.throws(()=>save(s,'owner','posTables',{code:'X-T1',name:'Another T1',outlet:'RESTAURANT',tableNo:'T1'}),e=>reason('MASTER_INVALID')(e)&&/already has an active T1/.test(e.message));
    assert.throws(()=>save(s,'owner','posTables',{code:'RS-1',name:'RS table',outlet:'ROOM-SERVICE',tableNo:'1'}),e=>/room service/.test(e.message));
    const t1=row(s,'posTables','RESTAURANT-T1');
    assert.throws(()=>save(s,'owner','posTables',{...t1,tableNo:'T01'}),e=>/cannot change/.test(e.message));
    const c=save(s,'owner','posTables',{code:'REST-C1',name:'Takeaway counter',outlet:'RESTAURANT',tableNo:'C1',counter:true});
    const ct=M.posTablesOf(c,'restaurant').find(t=>t.tableNo==='C1');assert.equal(ct.counter,true);assert.equal(M.posTableLabel(ct),'Counter C1');});

  // ── Categories ──
  test('[POS-MST] menu categories: rename and reorder change the POS category chips; in use cannot be deleted; unused can',()=>{
    let s=seed();const bev=row(s,'menuCategories','BEVERAGES');
    s=save(s,'techadmin','menuCategories',{...bev,name:'Drinks'});
    assert.equal(M.posCategories(s,'restaurant')[0],'Drinks');
    const ids=mstRows(s,'menuCategories').map(r=>r.id);s=execute(s,'owner',{type:'MST_MASTER_REORDER',master:'menuCategories',ids:[...ids.slice(1),ids[0]]});
    assert.equal(M.posCategories(s,'restaurant').at(-1),'Drinks');assert.equal(M.posMenuFor(s,'restaurant').at(-1).category,'Beverages');
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_DELETE',master:'menuCategories',id:bev.id}),reason('MASTER_IN_USE'));
    s=save(s,'owner','menuCategories',{code:'SOUPS',name:'Soups'});s=execute(s,'owner',{type:'MST_MASTER_DELETE',master:'menuCategories',id:row(s,'menuCategories','SOUPS').id});
    assert.equal(row(s,'menuCategories','SOUPS'),undefined);});

  // ── Menu items ──
  test('[POS-MST] add a menu item (Owner) with a price from today: on the outlet menu in its category; added to a check at that price',()=>{
    let s=save(seed(),'owner','menuItems',{code:'MANCHOW',name:'Manchow soup',category:'STARTERS',price:[{amount:19000,effectiveFrom:'2026-10-24'}],veg:true,outlets:['RESTAURANT'],shortCode:'MS'});
    const m=P(s).menu.at(-1);assert.deepEqual([m.name,m.category,m.categoryCode,m.price,m.outlets,m.available],['Manchow soup','Starters','STARTERS',19000,['restaurant'],true]);
    assert.ok(M.posMenuFor(s,'restaurant').some(x=>x.id===m.id));assert.ok(!M.posMenuFor(s,'room-service').some(x=>x.id===m.id));
    const [x,id]=check(s,'T6',[[m.id,2]]);assert.deepEqual([ord(x,id).lines[0].price,ord(x,id).lines[0].qty],[19000,2]);});
  test('[POS-MST] item price and GST class are Owner-only: Tech admin adds an unpriced item (not sellable) but cannot price it',()=>{
    let s=save(seed(),'techadmin','menuItems',{code:'SPECIAL',name:'Chef special',category:'MAINS',outlets:['RESTAURANT']});
    const m=P(s).menu.at(-1);assert.equal(M.posItemPrice(s,m),undefined);
    const [x,id]=check(s,'T6',[]);assert.throws(()=>execute(x,'cashier',{type:'POS_ITEM_ADD',id,item:m.id}),/no price yet/);
    const r=row(s,'menuItems','SPECIAL');
    assert.equal(code(()=>save(s,'techadmin','menuItems',{...r,price:[{amount:50000,effectiveFrom:s.date}]})),'FORBIDDEN');
    assert.equal(code(()=>save(s,'techadmin','menuItems',{...r,gstClass:'other'})),'FORBIDDEN');
    s=save(s,'owner','menuItems',{...r,price:[{amount:50000,effectiveFrom:s.date}]});assert.equal(M.posItemPrice(s,P(s).menu.at(-1)),50000);});
  test('[POS-MST] dated price: a change from tomorrow keeps today’s price; the old amount in effect cannot be edited; history + audit',()=>{
    let s=seed();const tea=itemRow(s,'TEA');
    assert.throws(()=>save(s,'owner','menuItems',{...tea,price:[{amount:7000,effectiveFrom:'2017-07-01'}]}),reason('MASTER_INVALID'));
    s=save(s,'owner','menuItems',{...tea,price:[...tea.price,{amount:7000,effectiveFrom:'2026-10-25'}]});
    const m=P(s).menu.find(x=>x.id==='m-tea');assert.equal(M.posItemPrice(s,m),6000);assert.equal(M.posItemPrice(next(s),m),7000);
    assert.match(s.audit[0].detail,/^Master Menu items: price ₹60 \(dated change ahead\) → |^Master Menu items: price /);
    assert.equal(mstHistory(s,'menuItems','m-tea')[0].field,'price');
    const [x,id]=check(next(s),'T6',[['m-tea',1]]);assert.equal(ord(x,id).lines[0].price,7000);
    const [y,id2]=check(s,'T6',[['m-tea',1]]);assert.equal(ord(y,id2).lines[0].price,6000);});
  test('[POS-MST] "sold out today" (master switch, Tech admin): refused on checks today, available again at the next business date',()=>{
    let s=seed();s=save(s,'techadmin','menuItems',{...itemRow(s,'PANEER'),soldOutToday:true});
    const m=P(s).menu.find(x=>x.id==='m-paneer');assert.deepEqual([m.available,m.soldOutOn],[false,'2026-10-24']);assert.equal(M.posSoldOut(s,m),true);
    const [x,id]=check(s,'T6',[]);assert.equal(code(()=>execute(x,'cashier',{type:'POS_ITEM_ADD',id,item:'m-paneer'})),'NOT_AVAILABLE');
    const t=next(s);assert.equal(M.posSoldOut(t,m),false);assert.equal(row(t,'menuItems','PANEER').soldOutToday,false);
    const [y,id2]=check(t,'T6',[['m-paneer',1]]);assert.equal(ord(y,id2).lines.length,1);});
  test('[POS-MST] POS_MENU_TOGGLE (billing) = the same "sold out today" switch; the legacy 86 (biryani) switches back on',()=>{
    let s=execute(seed(),'cashier',{type:'POS_MENU_TOGGLE',item:'m-tea'});
    const m=()=>P(s).menu.find(x=>x.id==='m-tea');assert.deepEqual([m().available,m().soldOutOn],[false,'2026-10-24']);assert.equal(row(s,'menuItems','TEA').soldOutToday,true);
    assert.equal(s.audit[0].detail,'Masala chai · sold out today (86)');
    s=execute(s,'cashier',{type:'POS_MENU_TOGGLE',item:'m-tea'});assert.deepEqual([m().available,m().soldOutOn],[true,undefined]);
    s=execute(s,'cashier',{type:'POS_MENU_TOGGLE',item:'m-biryani'});assert.equal(P(s).menu.find(x=>x.id==='m-biryani').available,true);
    // A sold-out switch from an earlier day has lapsed: the toggle marks it sold out again for today.
    const t=next(execute(seed(),'cashier',{type:'POS_MENU_TOGGLE',item:'m-tea'}));const u=execute(t,'cashier',{type:'POS_MENU_TOGGLE',item:'m-tea'});
    assert.deepEqual([P(u).menu.find(x=>x.id==='m-tea').available,P(u).menu.find(x=>x.id==='m-tea').soldOutOn],[false,'2026-10-25']);});
  test('[POS-MST] retire a menu item: off the POS menu, refused on checks; old check lines keep the name; in use → cannot delete',()=>{
    let s=seed();const poha=itemRow(s,'POHA');
    s=execute(s,'techadmin',{type:'MST_MASTER_RETIRE',master:'menuItems',id:poha.id});
    assert.ok(!M.posMenuFor(s,'restaurant').some(m=>m.id==='m-poha'));
    const [x,id]=check(s,'T6',[]);assert.equal(code(()=>execute(x,'cashier',{type:'POS_ITEM_ADD',id,item:'m-poha'})),'INVALID_TRANSITION');
    assert.equal(ord(s,'POS-F01').lines[0].name,'Kanda poha');
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_DELETE',master:'menuItems',id:poha.id}),reason('MASTER_IN_USE'));
    assert.throws(()=>save(s,'owner','menuItems',{...poha,name:'Poha'}),reason('MASTER_RETIRED'));});
  test('[POS-MST] item outlets: a retired outlet cannot be newly chosen (MASTER_RETIRED); removing an outlet takes the item off its menu',()=>{
    let s=save(seed(),'owner','outlets',{code:'BQ',name:'Banquet hall',type:'banquet'});
    s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'outlets',id:row(s,'outlets','BQ').id});
    assert.throws(()=>save(s,'owner','menuItems',{...itemRow(s,'DAL'),outlets:['RESTAURANT','BQ']}),reason('MASTER_RETIRED'));
    s=save(s,'owner','menuItems',{...itemRow(s,'DAL'),outlets:['RESTAURANT']});
    assert.ok(!M.posMenuFor(s,'room-service').some(m=>m.id==='m-dal'));assert.ok(M.posMenuFor(s,'restaurant').some(m=>m.id==='m-dal'));});

  // ── GST class ──
  test('[POS-MST] GST class: a banquet-hall item bills at the GST master’s banquet rate (18 %); food and banquet cannot share a check',()=>{
    let s=save(seed(),'owner','menuItems',{code:'HALL',name:'Hall hire (2 h)',category:'MAINS',price:[{amount:500000,effectiveFrom:'2026-10-24'}],gstClass:'banquet',outlets:['RESTAURANT']});
    const hall=P(s).menu.at(-1).id;
    const [a,ida]=check(s,'T6',[['m-dal',1]]);assert.throws(()=>execute(a,'cashier',{type:'POS_ITEM_ADD',id:ida,item:hall}),/separate check/);
    let [b,id]=check(s,'T7',[[hall,1]]);assert.equal(ord(b,id).lines[0].gst,'banquet');
    assert.throws(()=>execute(b,'cashier',{type:'POS_ITEM_ADD',id,item:'m-dal'}),/separate check/);
    assert.equal(M.posOrderTotals(b,ord(b,id)).taxBps,1800);
    b=execute(b,'cashier',{type:'POS_KOT_SEND',id});b=execute(b,'cashier',{type:'POS_SETTLE',id,mode:'outlet',method:'Card',reference:'DEMO-C1'});
    assert.deepEqual([ord(b,id).settlement.taxBps,ord(b,id).settlement.tax],[1800,90000]);});

  // ── KOT by station ──
  test('[POS-MST] KOT by station: tandoor items and drinks on separate KOTs (one number each); kitchen board filters by station',()=>{
    let s=seed();
    s=save(s,'owner','menuItems',{...itemRow(s,'NAAN'),station:'Tandoor'});s=save(s,'owner','menuItems',{...itemRow(s,'LIME'),station:'Bar'});
    assert.deepEqual(M.posStations(s),['Kitchen','Bar','Tandoor']);
    let [x,id]=check(s,'T6',[['m-dal',1],['m-naan',2],['m-lime',1]]);x=execute(x,'cashier',{type:'POS_KOT_SEND',id});
    const ks=ord(x,id).kots;assert.deepEqual(ks.map(k=>[k.no,k.station]),[[4,'Kitchen'],[5,'Tandoor'],[6,'Bar']]);
    assert.equal(P(x).kotSeq['restaurant:2026-10-24'],6);
    const o=ord(x,id);assert.equal(M.posKotLabel(x,o,ks[1]),'RE-KOT 5 · Tandoor');assert.deepEqual(o.lines.map(l=>o.kots.find(k=>k.id===l.kot).station),['Kitchen','Tandoor','Bar']);
    assert.deepEqual(M.posKitchenAt(x,'Tandoor').map(k=>k.kot.no),[5]);assert.ok(M.posKitchenAt(x,'Kitchen').some(k=>k.kot.id==='KOT-F03')); // old KOTs count as Kitchen
    assert.match(x.audit[0].detail,/KOT 4 \(Kitchen\), KOT 5 \(Tandoor\), KOT 6 \(Bar\)/);});
  test('[POS-MST] no stations configured (fixture): one KOT per send, no station field — KOT numbers and labels as before',()=>{
    let [s,id]=check(seed(),'T6',[['m-dal',1],['m-naan',2]]);s=execute(s,'cashier',{type:'POS_KOT_SEND',id});
    const k=ord(s,id).kots;assert.equal(k.length,1);assert.equal(k[0].no,4);assert.equal('station' in k[0],false);assert.equal(M.posKotLabel(s,ord(s,id),k[0]),'RE-KOT 4');
    assert.deepEqual(M.posStations(s),[]);
    // An outlet KOT station alone labels the tickets.
    let t=save(seed(),'owner','outlets',{...row(seed(),'outlets','RESTAURANT'),kotStation:'Main kitchen'});
    [t,id]=check(t,'T6',[['m-dal',1]]);t=execute(t,'cashier',{type:'POS_KOT_SEND',id});assert.equal(ord(t,id).kots[0].station,'Main kitchen');});

  // ── Idempotency, UI wiring, production mapping ──
  test('[POS-MST] idempotent repeat of a master save (same key → same state, one history row)',()=>{
    const a=save(seed(),'owner','menuCategories',{code:'SOUPS',name:'Soups'},'pm-k1');assert.equal(save(a,'owner','menuCategories',{code:'SOUPS',name:'Soups'},'pm-k1'),a);
    assert.equal(mstHistory(a,'menuCategories').filter(h=>h.code==='SOUPS'&&h.field==='name').length,1);});
  test('[POS-MST] POS screens pick from the masters (desktop + phone) and link to the lists ("Manage list…"); no new page',()=>{
    const parts=src('src/features/pos/parts.tsx'),page=src('src/features/pos/PosPage.tsx'),phone=src('src/features/pos/PosMobile.tsx');
    assert.match(parts,/posTablesOf\(s,outlet\)/);assert.match(parts,/posItemPrice\(s,m\)/);assert.match(parts,/posSoldOut\(s,m\)/);
    for(const t of [page,phone]){assert.match(t,/posOutletList\(s\)/);assert.match(t,/PosMasterLinks/);assert.match(t,/posKitchenAt\(s,station\)/);}
    for(const id of ['outlets','posTables','menuCategories','menuItems'])assert.match(page,new RegExp(`<MstLink spec="${id}"/>`));
    assert.doesNotMatch(src('src/app/navmap.ts'),/menuItems|posTables/);});
  test('[POS-MST] production build mapping: the four lists in MST_SERVER with their server commands; posX loader reads master fields',()=>{
    const pm=src('src/clients/sync/pos-masters.ts'),m=src('src/clients/sync/masters.ts'),p=src('src/clients/sync/pos.ts');
    assert.match(m,/\.\.\.POS_MST_SERVER, \/\/ MA-4/);
    for(const n of ['SaveOutlet','RetireOutlet','ReactivateOutlet','ReorderOutlets','DeleteOutlet','SaveOutletTable','RetireOutletTable','ReorderOutletTables','DeleteOutletTable',
      'SaveMenuCategory','RetireMenuCategory','ReorderMenuCategories','DeleteMenuCategory','SaveMenuItem','RetireMenuItem','ReactivateMenuItem','ReorderMenuItems','DeleteMenuItem'])assert.ok(pm.includes(`'${n}'`),n);
    assert.match(p,/requires:\['billing','masters'\]/);assert.match(p,/tableRows/);});
}
