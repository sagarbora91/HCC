// MA-2 · Meal plans, extras catalogue, discount reasons (plan Part 2 §8 M6–M8; DEC-MA-3, DEC-MA-4) — domain/command tests. Prefix [CAT].
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
export function register({test,M,assert}){
  const {seed,execute,totals,mstRows,mstSpec,mstByCode,mstVisibleFields,mstReasonOf,catPlans,catPlan,catExtras,catExtra,catReasons,catChargeSplit,catPlanReport,
    catDiscountReport,catPlanFromChannel,catPlansForSite,catPlanQuote,catPlanNightLine,catReasonsFor,catReasonLimits,catChargePrefill,catReportCsv,catPlanOptions,
    gstForRoomNight,lineTax,HccError}=M;
  const src=f=>readFileSync(f,'utf8');
  const reason=r=>e=>mstReasonOf(e)===r;
  const code=c=>e=>e instanceof HccError&&e.code===c;
  const sha=o=>createHash('sha256').update(JSON.stringify(o)).digest('hex');
  const strip=s=>{const {mastersX,grants,...r}=s;return r;};
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  const save=(s,u,master,row)=>execute(s,u,{type:'MST_MASTER_SAVE',master,row});
  /** In-house stay in room 105 (Classic ₹2,000) from the business date to 26 Oct, optional plan and party size. */
  function stay(s=seed(),{plan,people=2,room='105',post=true}={}){
    s=execute(s,'reception',{type:'BOOK',guest:'Catalogue Test (fictional)',room,arrival:s.date,departure:'2026-10-26',people,...(plan?{plan}:{})});
    const id=s.bookings.at(-1).id;
    s=execute(s,'reception',{type:'REGISTER',id});s=execute(s,'reception',{type:'CHECKIN',id});
    if(post)s=execute(s,'owner',{type:'POST_NIGHTS',id});
    return [s,id];
  }

  // ── Registry, seeds, golden figures ──
  test('[CAT] three money masters registered: Meal plans (Rooms and rate plans), Extras and Discount reasons (Money and tax); Owner-only',()=>{
    for(const [id,g] of [['mealPlans','rooms'],['extras','money'],['discountReasons','money']]){const sp=mstSpec(id);
      assert.equal(sp.group,g,id);assert.equal(sp.money,true,id);assert.equal(sp.permission,'settings',id);}
    assert.deepEqual(mstSpec('mealPlans').fields.map(f=>f.key),['code','name','meals','addOn','gst','roomTypes','sortOrder','active']);
    assert.deepEqual(mstSpec('extras').fields.map(f=>f.key),['code','name','category','price','gst','unit','department','sortOrder','active']);
    assert.deepEqual(mstSpec('discountReasons').fields.map(f=>f.key),['code','name','maxPct','approvalAbovePct','roles','sortOrder','active']);});
  test('[CAT] seeds: EP/CP/MAP/AP, eight extras incl. Laundry ₹500 at the laundry class, five reasons; nothing stored in BASE',()=>{
    const s=seed();assert.equal(s.mastersX,undefined);
    assert.deepEqual(catPlans(s).map(p=>[p.code,p.meals.join(''),p.addOn[0].amount,p.gst]),[['EP','',0,'separate'],['CP','B',30000,'bundled'],['MAP','BD',75000,'separate'],['AP','BLD',120000,'separate']]);
    const l=catExtras(s).find(x=>x.code==='LAUNDRY');assert.equal(l.name,'Laundry');assert.equal(l.price[0].amount,50000);assert.equal(l.gst,'laundry');
    assert.equal(catExtras(s).length,8);assert.deepEqual(catReasons(s).map(r=>r.code),['SERVICE','CORP','REPEAT','LONGSTAY','MGMT']);});
  test('[CAT] golden figures unchanged: occupancy 8, collections ₹21,500, drawer ₹10,725; BASE / FY26 hashes as MA-0',()=>{
    const s=seed();assert.equal(M.metrics(s).occupied,8);assert.equal(M.metrics(s).collections,2150000);assert.equal(M.drawer(s),1072500);
    assert.equal(sha(strip(seed())),'237e220fd61ff6587816baf1c63a1502ec790ba1553758565d80f4ed12498be1') /* ACC-2: SCHEMA 11 → 12 only (new optional fields) */ /* ACC-1: SCHEMA 10 → 11 only (accounting chart / opening fields are optional and absent in the seed) */ /* SCC decisions wave (DEC-133): 4a8aad03… → aecae3b2… = the demo firm profile's fictional licence numbers / expiry dates only */ /* SCC port wave (DEC-116, tax + leave): 2c1782df… → 4a8aad03… = compliance + leave slices, SCHEMA 9 → 10 */ /* old → new: d885a20a… → add626d3… = SCHEMA 8 → 9 only (masters wave) */;
    assert.equal(sha(strip(M.buildHistory(M.baseFixture()))),'ba51a0eacfcdfab7835be9b16b0a8f0793aad052c1c9b99c0b80e511989ee2e8') /* ACC-2: SCHEMA 11 → 12 only (new optional fields) */ /* ACC-1 integration (all seven lanes merged + the ITC / cash-over one-liners): the FY26 history locks its accounting months with accLiveVouchers snapshots, which now carry the re-pointed mapping (POS 4200, ITC 1310 / 1320, cash over 4950), party ledger ids (1000/TILL, 1030/<bank>, 1120/UD-…, 2200/<vendor>) and the Udhaar credit / company payment vouchers: f3601522… → e3a74ba3… */ /* ACC-1: SCHEMA 10 → 11 only */ /* SCC decisions wave (DEC-133): 197643a1… → 5dc19a44… = the demo firm profile's fictional licence fields only */ /* SCC port wave (DEC-116, tax + leave): bf689538… → 197643a1… = compliance + leave slices, SCHEMA 9 → 10 */ /* old → new: ba4a1d4e… → 35e63082… = SCHEMA 8 → 9 only */;});

  // ── Extras catalogue in front of CHARGE (DEC-MA-4) ──
  test('[CAT] worked ₹4,265 GST journey through the catalogue: Laundry item ₹500 at 18 % gives the same folio as the free-text charge',()=>{
    let [s,id]=stay(seed(),{people:2});
    s=execute(s,'reception',{type:'CAT_CHARGE',id,extraId:'ext-laundry'});
    const l=bk(s,id).lines.at(-1);assert.deepEqual([l.label,l.amount,l.taxBps,l.extraId,l.kind],['Laundry',50000,1800,'ext-laundry','service']);
    s=execute(s,'reception',{type:'DISCOUNT',id,amount:50000,reason:'Service recovery'});s=execute(s,'manager',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    assert.deepEqual(totals(bk(s,id)),{net:400000,tax:26500,total:426500,paid:0,deposit:0,balance:426500});
    let [f,fid]=stay(seed(),{people:2});f=execute(f,'reception',{type:'CHARGE',id:fid,label:'Laundry',amount:50000});
    assert.equal(bk(f,fid).lines.at(-1).taxBps,1800);assert.equal(bk(f,fid).lines.at(-1).extraId,undefined);});
  test('[CAT] CAT_CHARGE: quantity multiplies the price and names the line; name, amount and GST class stay editable (no approval)',()=>{
    let [s,id]=stay();
    s=execute(s,'reception',{type:'CAT_CHARGE',id,extraId:'ext-pickup',qty:2});
    let l=bk(s,id).lines.at(-1);assert.deepEqual([l.label,l.amount,l.taxBps],['Airport pickup × 2',240000,1800]);
    assert.match(s.audit[0].detail,/catalogue PICKUP × 2$/);
    s=execute(s,'reception',{type:'CAT_CHARGE',id,extraId:'ext-minibar',label:'Minibar · 2 juices',amount:18000,gst:'food'});
    l=bk(s,id).lines.at(-1);assert.deepEqual([l.label,l.amount,l.taxBps,l.extraId],['Minibar · 2 juices',18000,500,'ext-minibar']);
    assert.match(s.audit[0].detail,/changed: name, price/);assert.equal(s.approvals.length,0);});
  test('[CAT] CAT_CHARGE refusals: unknown item NOT_FOUND, retired item MASTER_RETIRED, bad quantity / GST class VALIDATION, housekeeper FORBIDDEN',()=>{
    let [s,id]=stay();
    assert.throws(()=>execute(s,'reception',{type:'CAT_CHARGE',id,extraId:'nope'}),code('NOT_FOUND'));
    assert.throws(()=>execute(s,'reception',{type:'CAT_CHARGE',id,extraId:'ext-xbed',qty:0}),code('VALIDATION'));
    assert.throws(()=>execute(s,'reception',{type:'CAT_CHARGE',id,extraId:'ext-xbed',qty:1.5}),code('VALIDATION'));
    assert.throws(()=>execute(s,'reception',{type:'CAT_CHARGE',id,extraId:'ext-xbed',gst:'room'}),code('VALIDATION'));
    assert.throws(()=>execute(s,'housekeeper',{type:'CAT_CHARGE',id,extraId:'ext-xbed'}),code('FORBIDDEN'));
    s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'extras',id:'ext-xbed'});
    assert.throws(()=>execute(s,'reception',{type:'CAT_CHARGE',id,extraId:'ext-xbed'}),reason('MASTER_RETIRED'));});
  test('[CAT] catalogue vs free text report: catalogue lines by item, free text by label, plan lines apart; reversed lines excluded',()=>{
    let [s,id]=stay(seed(),{plan:'MAP'});
    s=execute(s,'reception',{type:'CAT_CHARGE',id,extraId:'ext-laundry'});
    s=execute(s,'reception',{type:'CAT_CHARGE',id,extraId:'ext-laundry',amount:30000});
    s=execute(s,'reception',{type:'CHARGE',id,label:'Flower bouquet',amount:70000});
    const r=catChargeSplit(s,s.date,'2026-10-31');
    assert.deepEqual([r.catalogue.count,r.catalogue.amount,r.catalogue.rows[0].label],[2,80000,'LAUNDRY · Laundry']);
    assert.deepEqual([r.freeText.count,r.freeText.amount,r.freeText.rows[0].label],[1,70000,'Flower bouquet']);
    assert.deepEqual(r.plans,{count:2,amount:300000});
    assert.match(catReportCsv(s,s.date,'2026-10-31'),/Free-text charges,Flower bouquet,1,700\.00/);});
  test('[CAT] a dated price change applies from its date; posted lines keep their amount and GST snapshot',()=>{
    let [s,id]=stay();s=execute(s,'reception',{type:'CAT_CHARGE',id,extraId:'ext-laundry'});
    const x=catExtra(s,'ext-laundry');
    s=save(s,'owner','extras',{...x,price:[...x.price,{amount:60000,effectiveFrom:'2026-10-25'}]});
    assert.equal(catChargePrefill(s,catExtra(s,'ext-laundry')).amount,50000);
    const t=structuredClone(s);t.date='2026-10-25';assert.equal(catChargePrefill(t,catExtra(t,'ext-laundry'),2).amount,120000);
    assert.equal(bk(s,id).lines.at(-1).amount,50000);
    assert.throws(()=>save(s,'owner','extras',{...catExtra(s,'ext-laundry'),price:[{amount:1,effectiveFrom:'2025-04-01'}]}),reason('MASTER_INVALID'));});
  test('[CAT] masters are Owner-only (DEC-058): Tech admin, Manager and Reception refused; housekeeping never sees prices (DEC-061)',()=>{
    for(const u of ['techadmin','manager','reception'])for(const m of ['extras','mealPlans','discountReasons'])
      assert.throws(()=>save(seed(),u,m,{code:'NEW',name:'New row'}),code('FORBIDDEN'),u+' '+m);
    const s=save(seed(),'owner','extras',{code:'IRON',name:'Ironing',category:'laundry',price:[{amount:10000,effectiveFrom:'2026-10-24'}],gst:'laundry',unit:'piece'});
    assert.equal(mstByCode(s,'extras','IRON').name,'Ironing');assert.match(s.audit[0].detail,/^Master Extras and services: added IRON/);
    for(const u of ['housekeeper','maint','supervisor']){assert.equal(mstVisibleFields(s,u,mstSpec('extras')).some(f=>f.key==='price'),false,u);
      assert.equal(mstVisibleFields(s,u,mstSpec('mealPlans')).some(f=>f.key==='addOn'),false,u);}});
  test('[CAT] an extra in use cannot be deleted (MASTER_IN_USE) — retire keeps old lines readable; an unused one can',()=>{
    let [s,id]=stay();s=execute(s,'reception',{type:'CAT_CHARGE',id,extraId:'ext-drop'});
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_DELETE',master:'extras',id:'ext-drop'}),reason('MASTER_IN_USE'));
    s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'extras',id:'ext-drop'});
    assert.equal(catChargeSplit(s,s.date,s.date).catalogue.rows[0].label,'DROP · Airport drop');
    s=execute(s,'owner',{type:'MST_MASTER_DELETE',master:'extras',id:'ext-hall'});assert.equal(catExtra(s,'ext-hall'),undefined);});
  test('[CAT] required fields and choices of an extra are validated (MASTER_INVALID): category, GST class, unit',()=>{
    assert.throws(()=>save(seed(),'owner','extras',{code:'X1',name:'Something'}),reason('MASTER_INVALID'));
    assert.throws(()=>save(seed(),'owner','extras',{code:'X1',name:'Something',category:'misc',gst:'room',unit:'each'}),reason('MASTER_INVALID'));
    assert.throws(()=>save(seed(),'owner','extras',{code:'LAUNDRY',name:'Laundry 2',category:'misc',gst:'other',unit:'each'}),reason('MASTER_CODE_TAKEN'));});

  // ── Meal plans on bookings and folio lines ──
  test('[CAT] BOOK carries a meal plan; unknown → MASTER_INVALID, retired → MASTER_RETIRED, room type not allowed → MASTER_INVALID',()=>{
    let [s,id]=stay(seed(),{plan:'cp',post:false});assert.equal(bk(s,id).plan,'CP');
    const book=(st,plan)=>execute(st,'reception',{type:'BOOK',guest:'Plan Test',room:'106',arrival:st.date,departure:'2026-10-26',people:1,plan});
    assert.throws(()=>book(seed(),'XX'),reason('MASTER_INVALID'));
    let r=execute(seed(),'owner',{type:'MST_MASTER_RETIRE',master:'mealPlans',id:'mpl-ap'});assert.throws(()=>book(r,'AP'),reason('MASTER_RETIRED'));
    // Masters integration: with MA-1's roomTypes master the multiselect stores the master code (Deluxe → DELUXE).
    const dlx=M.mstRows(seed(),'roomTypes').find(x=>x.name==='Deluxe')?.code??'Deluxe';
    r=save(seed(),'owner','mealPlans',{...catPlan(seed(),'MAP'),roomTypes:[dlx]});
    assert.throws(()=>book(r,'MAP'),e=>reason('MASTER_INVALID')(e)&&/not offered for Suite/.test(e.message));
    assert.deepEqual(catPlanOptions(r,'Suite').map(o=>o.value),['EP','CP','AP']);});
  test('[CAT] MAP (separate restaurant line): each room night posts one plan line = add-on × guests at the restaurant rate; repeat-safe',()=>{
    let [s,id]=stay(seed(),{plan:'MAP',people:2});
    const plan=bk(s,id).lines.filter(l=>l.planCode);
    assert.deepEqual(plan.map(l=>[l.id.split(':')[2],l.amount,l.taxBps,l.kind]),[['2026-10-24',150000,500,'service'],['2026-10-25',150000,500,'service']]);
    assert.ok(bk(s,id).lines.filter(l=>l.kind==='room').every(l=>l.taxBps===500));
    const again=execute(s,'owner',{type:'POST_NIGHTS',id});assert.equal(bk(again,id).lines.length,bk(s,id).lines.length);
    s=execute(s,'owner',{type:'NIGHT_POST'});assert.equal(bk(s,id).lines.filter(l=>l.planCode).length,2);});
  test('[CAT] bundled plan: plan line and the new room night take the room slab on room + meals (Illustrative — CA to confirm)',()=>{
    let s=save(seed(),'owner','mealPlans',{code:'GALA',name:'Gala package',meals:['B','D'],addOn:[{amount:200000,effectiveFrom:'2026-10-24'}],gst:'bundled'});
    let [t,id]=stay(s,{plan:'GALA',people:2,room:'106'}); // Suite ₹4,500 + 2 × ₹2,000 = ₹8,500 > ₹7,500 → 18 %
    const nights=bk(t,id).lines.filter(l=>l.kind==='room'),plan=bk(t,id).lines.filter(l=>l.planCode==='GALA');
    assert.ok(nights.every(l=>l.taxBps===1800));assert.ok(plan.every(l=>l.taxBps===1800&&l.amount===400000));
    assert.equal(gstForRoomNight(t,'2026-10-24',450000).bps,500);
    [t,id]=stay(seed(),{plan:'CP',people:2}); // Classic ₹2,000 + 2 × ₹300 = ₹2,600 → 5 %
    assert.ok(bk(t,id).lines.every(l=>l.taxBps===500));assert.equal(bk(t,id).lines.filter(l=>l.planCode==='CP').length,2);});
  test('[CAT] EP or no plan posts nothing extra; folio totals of plain stays unchanged',()=>{
    const [a,ia]=stay(seed(),{plan:'EP'}),[b,ib]=stay(seed());
    assert.equal(bk(a,ia).lines.length,2);assert.deepEqual(totals(bk(a,ia)),totals(bk(b,ib)));assert.equal(catPlanNightLine(a,bk(a,ia),a.date),undefined);});
  test('[CAT] CAT_PLAN_SET: change from the next night (posted nights keep theirs), clear, nothing-changed and permission refusals',()=>{
    let [s,id]=stay(seed(),{post:false});
    s=execute(s,'owner',{type:'NIGHT_POST'});
    s=execute(s,'cashier',{type:'CAT_PLAN_SET',id,plan:'AP'});assert.equal(bk(s,id).plan,'AP');assert.match(s.audit[0].detail,/No plan → All meals \(AP\) \(from the next room night posted\)/);
    s=execute(s,'owner',{type:'NIGHT_POST'});assert.equal(bk(s,id).lines.filter(l=>l.planCode).length,0);
    assert.throws(()=>execute(s,'reception',{type:'CAT_PLAN_SET',id,plan:'AP'}),/Nothing changed/);
    assert.throws(()=>execute(s,'housekeeper',{type:'CAT_PLAN_SET',id,plan:'CP'}),code('FORBIDDEN'));
    s=execute(s,'reception',{type:'CAT_PLAN_SET',id,plan:''});assert.equal(bk(s,id).plan,undefined);
    let [c,cid]=stay(seed(),{post:false});c=execute(c,'reception',{type:'CAT_PLAN_SET',id:cid,plan:'CP'});
    c=execute(c,'owner',{type:'POST_NIGHTS',id:cid});assert.equal(bk(c,cid).lines.filter(l=>l.planCode==='CP').length,2);
    assert.throws(()=>execute(seed(),'reception',{type:'CAT_PLAN_SET',id:'HCC-1041',plan:'ZZ'}),reason('MASTER_INVALID'));});
  test('[CAT] a plan in use cannot be deleted; retiring keeps its name on bookings and in the report',()=>{
    let [s,id]=stay(seed(),{plan:'MAP'});
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_DELETE',master:'mealPlans',id:'mpl-map'}),reason('MASTER_IN_USE'));
    s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'mealPlans',id:'mpl-map'});
    const r=catPlanReport(s,s.date,'2026-10-31').find(x=>x.code==='MAP');
    assert.deepEqual([r.label,r.bookings,r.roomNights,r.roomRevenue,r.planRevenue],['Breakfast and dinner (MAP)',1,2,400000,300000]);
    assert.equal(catPlanReport(s,s.date,'2026-10-31').length,1);});
  test('[CAT] report by plan on the fixture: in-house stays without a plan in one "No plan" row',()=>{
    const s=execute(seed(),'owner',{type:'NIGHT_POST'});const r=catPlanReport(s,'2026-10-23','2026-10-24');
    assert.deepEqual(r.map(x=>x.code),['—']);assert.equal(r[0].label,'No plan');assert.equal(r[0].bookings,8);assert.equal(r[0].roomNights,16);});
  test('[CAT] booking site and OTA hooks: plans offered per room type with the add-on; meal codes RO/BB/HB/FB/AI map to EP/CP/MAP/AP',()=>{
    const s=seed();
    assert.deepEqual(catPlansForSite(s,'Classic',s.date).map(p=>[p.code,p.perGuestNight]),[['EP',0],['CP',30000],['MAP',75000],['AP',120000]]);
    assert.deepEqual(['RO','bb','HB','FB','AI','XX',undefined].map(c=>catPlanFromChannel(s,c)),['EP','CP','MAP','AP','AP',undefined,undefined]);
    const r=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'mealPlans',id:'mpl-cp'});assert.equal(catPlanFromChannel(r,'BB'),undefined);
    assert.equal(catPlanQuote(s,'MAP',2,'2026-10-24','2026-10-27'),450000);assert.equal(catPlanQuote(s,'ZZ',2,'2026-10-24','2026-10-27'),0);});

  // ── Discount reasons feeding DISCOUNT ──
  test('[CAT] CAT_DISCOUNT within role limit and reason caps posts at once with the reason code; report by reason',()=>{
    let [s,id]=stay();s=execute(s,'reception',{type:'CAT_DISCOUNT',id,amount:20000,reasonCode:'service',note:'Late towels'});
    const l=bk(s,id).lines.at(-1);assert.deepEqual([l.kind,l.amount,l.discountReason,l.label],['discount',-20000,'SERVICE','Discount · Service recovery · Late towels']);
    s=execute(s,'reception',{type:'DISCOUNT',id,amount:5000,reason:'Goodwill'});
    assert.deepEqual(catDiscountReport(s,s.date,s.date).map(r=>[r.label,r.count,r.amount]),[['Service recovery',1,20000],['Free-text reason',1,5000]]);});
  test('[CAT] reason cap: above the maximum % is refused (VALIDATION); approval-above % turns a Manager discount into a request',()=>{
    let [s,id]=stay(); // net ₹4,000: SERVICE max 25 % = ₹1,000, approval above 10 % = ₹400
    assert.deepEqual(catReasonLimits(catReasons(s)[0],bk(s,id)),{net:400000,max:100000,approvalAbove:40000});
    assert.throws(()=>execute(s,'owner',{type:'CAT_DISCOUNT',id,amount:100001,reasonCode:'SERVICE'}),e=>e.code==='VALIDATION'&&/at most 25 %/.test(e.message));
    s=execute(s,'manager',{type:'CAT_DISCOUNT',id,amount:50000,reasonCode:'SERVICE'});
    const a=s.approvals.at(-1);assert.deepEqual([a.status,a.amount,a.meta.reasonCode,a.reason],['Pending',50000,'SERVICE','Service recovery']);
    assert.equal(bk(s,id).lines.filter(l=>l.kind==='discount').length,0);
    s=execute(s,'owner',{type:'APPROVAL',id:a.id,approve:true});
    const l=bk(s,id).lines.at(-1);assert.deepEqual([l.kind,l.amount,l.discountReason],['discount',-50000,'SERVICE']);
    let [o,oid]=stay();o=execute(o,'owner',{type:'CAT_DISCOUNT',id:oid,amount:50000,reasonCode:'SERVICE'});assert.equal(bk(o,oid).lines.at(-1).discountReason,'SERVICE');});
  test('[CAT] role limits still apply: Reception above ₹250 → approval request carrying the reason; approval keeps it on the line',()=>{
    let [s,id]=stay();s=execute(s,'reception',{type:'CAT_DISCOUNT',id,amount:30000,reasonCode:'CORP'});
    const a=s.approvals.at(-1);assert.equal(a.meta.reasonCode,'CORP');assert.equal(a.status,'Pending');
    s=execute(s,'manager',{type:'APPROVAL',id:a.id,approve:true});assert.equal(bk(s,id).lines.at(-1).discountReason,'CORP');});
  test('[CAT] reason roles and refusals: Reception cannot use a Manager-only reason; Cashier never; retired reason MASTER_RETIRED',()=>{
    let [s,id]=stay();
    assert.throws(()=>execute(s,'reception',{type:'CAT_DISCOUNT',id,amount:1000,reasonCode:'MGMT'}),e=>e.code==='FORBIDDEN'&&/Owner, Manager only/.test(e.message));
    assert.throws(()=>execute(s,'cashier',{type:'CAT_DISCOUNT',id,amount:1000,reasonCode:'SERVICE'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'reception',{type:'CAT_DISCOUNT',id,amount:1000,reasonCode:'NOPE'}),code('NOT_FOUND'));
    assert.deepEqual(catReasonsFor(s,'Reception').map(r=>r.code),['SERVICE','CORP','REPEAT']);
    s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'discountReasons',id:'dsr-repeat'});
    assert.throws(()=>execute(s,'reception',{type:'CAT_DISCOUNT',id,amount:1000,reasonCode:'REPEAT'}),reason('MASTER_RETIRED'));
    assert.throws(()=>save(s,'owner','discountReasons',{code:'BAD',name:'Bad',maxPct:1000,approvalAbovePct:1000}),reason('MASTER_INVALID'));});
  test('[CAT] idempotent catalogue charge and discount: the same key changes nothing twice',()=>{
    const [s,id]=stay();const a=execute(s,'reception',{type:'CAT_CHARGE',id,extraId:'ext-laundry',key:'cat-k1'});
    assert.equal(execute(a,'reception',{type:'CAT_CHARGE',id,extraId:'ext-laundry',key:'cat-k1'}),a);
    const b=execute(a,'reception',{type:'CAT_DISCOUNT',id,amount:1000,reasonCode:'SERVICE',key:'cat-k2'});assert.equal(execute(b,'reception',{type:'CAT_DISCOUNT',id,amount:1000,reasonCode:'SERVICE',key:'cat-k2'}),b);
    assert.equal(lineTax(bk(b,id).lines.at(-1)),-50);});

  // ── UI wiring and navigation (source checks) ──
  test('[CAT] UI: the charge and discount dialogs come from the catalogue (desktop and phone); booking action for the meal plan; report page',()=>{
    const reg=src('src/features/folio/catalogue.tsx');
    assert.match(reg,/'charge':CatChargeModal/);assert.match(reg,/'discount':CatDiscountModal/);assert.match(reg,/'cat-plan':CatPlanModal/);
    assert.match(reg,/bookingActions:CatPlanAction/);assert.match(reg,/Other \(describe\)/);assert.match(reg,/<MstLink spec="extras"/);
    assert.match(src('src/app/registry.ts'),/m_admin,(\w+,)*catalogue[,\]]/);assert.match(src('src/app/registry.ts'),/cashbook,masters,m_frontdesk/);
    const nm=src('src/app/navmap.ts');assert.match(nm,/'plans-extras':'accounts'/);assert.match(nm,/'plans-extras':\[/);
    assert.match(nm,/access:'admin',settings:'admin',approvals:'admin',backup:'admin',roadmap:'admin',\n\};/);});
}
