// MA-5 · Front-desk lists (plan Part 2 §8 M9, M11, M12, M14): booking sources, guest ID types, numbering series, booking policies on
// the MA-0 masters framework. Names prefixed [FDM]. DEC-MA-5: one tax-invoice series. Figures and the FY26 history stay unchanged.
import {readFileSync} from 'node:fs';
import {createHash} from 'node:crypto';
export function register({test,M,assert}){
  const {seed,execute,totals,mstRows,mstByCode,mstSpec,mstSpecsOfGroup,mstHistory,mstUsage,mstVisibleFields,mstReasonOf,MstError,
    fdmSourceOf,fdmSourceChoices,fdmOtaChannelNames,fdmSourceReport,fdmIdTypeChoices,fdmIdTypeProblem,fdmIdCheck,fdmIdType,fdmSeriesFor,fdmPreview,fdmAllocate,
    fdmFyLabel,fdmFyCode,fdmFormat,fdmIssued,fdmPolicyFor,fdmCancelCharge,fdmNoShowCharge,fdmAdvanceDue,fdmPolicyText,fdmCommission,fdmSpecs,FDM_GST_NOTE}=M;
  const reason=r=>e=>e instanceof MstError&&e.reason===r&&mstReasonOf(e)===r;
  const code=c=>e=>e.code===c;
  const msg=re=>e=>re.test(e.message);
  const save=(s,u,master,row,key)=>execute(s,u,{type:'MST_MASTER_SAVE',master,row,key});
  const src=f=>readFileSync(f,'utf8');
  const sha=o=>createHash('sha256').update(JSON.stringify(o)).digest('hex');
  const strip=s=>{const {mastersX,grants,frontdeskMasterX,...r}=s;return r;};
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  function stay(s=seed(),room='105'){
    s=execute(s,'reception',{type:'BOOK',guest:'MA-5 Test (fictional)',room,arrival:s.date,departure:'2026-10-26',people:1});
    const id=s.bookings.at(-1).id;
    s=execute(s,'reception',{type:'REGISTER',id});s=execute(s,'reception',{type:'CHECKIN',id});s=execute(s,'owner',{type:'POST_NIGHTS',id});
    s=execute(s,'cashier',{type:'PAYMENT',id,amount:totals(bk(s,id)).balance,method:'UPI',reference:'DEMO-MA5'});
    return [s,id];
  }
  const checkout=(s,id)=>execute(s,'reception',{type:'CHECKOUT',id});

  // ── Registry and seeds ──
  test('[FDM] Four masters in Master configuration › Front-desk policies: sources + ID types (masters), numbering + policies (Owner, money lists)',()=>{
    const specs=fdmSpecs();assert.deepEqual(specs.map(x=>x.id),['bookingSources','idTypes','numberSeries','policies']);
    assert.ok(specs.every(x=>x.group==='frontdesk'));
    assert.deepEqual(specs.map(x=>[x.permission,x.money]),[['masters',false],['masters',false],['settings',true],['settings',true]]);
    // Masters integration: MA-7's complaint / lost-and-found categories share the group, after these four (old: exactly these four).
    assert.deepEqual(mstSpecsOfGroup('frontdesk').map(x=>x.id).slice(0,4),['bookingSources','idTypes','numberSeries','policies']);
    assert.ok(mstSpec('bookingSources').fields.find(f=>f.key==='commissionBps').money);
    assert.match(mstSpec('numberSeries').help,/CA to confirm/);assert.match(FDM_GST_NOTE,/no gaps/);});
  test('[FDM] Seeds come from the fixture values; BASE stores nothing new',()=>{
    const s=seed();assert.equal(s.mastersX,undefined);assert.equal(s.frontdeskMasterX,undefined);
    assert.deepEqual(mstRows(s,'bookingSources').filter(r=>!r.auto).map(r=>r.name),['Walk-in','Phone','WhatsApp','Direct','Website','OTA','Agent','Corporate']);
    assert.deepEqual(mstRows(s,'idTypes').map(r=>r.name),[...M.guestIdTypes,'Passport + visa (demo, foreign national)']);
    assert.deepEqual(mstRows(s,'numberSeries').map(r=>[r.code,r.document,r.prefix,r.reset]),[['INV','INV','DEMO','FY'],['BOS','BOS','BOS','FY'],['CRN','CRN','DEMO-CN','FY'],['RCP','RCP','RCP','NEVER'],['KOT','KOT','KOT','NEVER'],['REG','REG','REG','NEVER']]);
    assert.deepEqual(mstRows(s,'policies').map(r=>[r.code,r.name,r.cancelChargeBps,r.noShowChargeBps,r.depositBps,r.sources.length]),[['DEFAULT','Default',0,0,0,0]]);});
  test('[FDM] Fixture figures, BASE and FY26 history hashes unchanged',()=>{
    const s=seed();assert.equal(M.metrics(s).occupied,8);assert.equal(M.metrics(s).collections,2150000);assert.equal(M.drawer(s),1072500);
    assert.equal(sha(strip(seed())),'add626d3e76aa9a5f2475049c8592c3b40207624c92cb44942e7215d08761d52') /* old → new: d885a20a… → add626d3… = SCHEMA 8 → 9 only (masters wave) */;
    assert.equal(sha(strip(M.buildHistory(M.baseFixture()))),'35e6308217fd38e9c5e3bc9137aa974eefee0930d3f49c8037853b8f76ad8319') /* old → new: ba4a1d4e… → 35e63082… = SCHEMA 8 → 9 only */;});

  // ── M9 booking sources ──
  test('[FDM] OTA channels and company accounts register their own source rows (auto, in use → retire not delete)',()=>{
    const s=seed(),auto=mstRows(s,'bookingSources').filter(r=>r.auto);
    assert.deepEqual(auto.map(r=>[r.code,r.segment,r.channel]),[['OTA-BOOKNEST','OTA','BookNest'],['OTA-STAYHUB','OTA','StayHub'],['OTA-TRIPKART','OTA','TripKart'],['OTA-ROOMRAAHI','OTA','RoomRaahi'],
      ['CORP-CO-1','CORPORATE','Deccan Pharma Distributors (fictional)'],['CORP-CO-2','CORPORATE','Godavari Agro Inputs (fictional)'],['CORP-CO-3','CORPORATE','Sahyadri Infra Projects (fictional)']]);
    assert.equal(auto[0].commissionBps,1800);const u=mstUsage(s,mstSpec('bookingSources'),auto[0]);assert.ok(u.count>=1);assert.match(u.where.join(' '),/Registered by OTA channel BookNest/);
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_DELETE',master:'bookingSources',id:auto[0].id}),reason('MASTER_IN_USE'));});
  test('[FDM] Every fixture and FY26 booking maps to a source row (OTA · manual → OTA, channel and company rows win)',()=>{
    const s=seed();
    assert.deepEqual([...new Set(s.bookings.map(b=>fdmSourceOf(s,b)?.code))].sort(),['CORPORATE','DIRECT','OTA','PHONE']);
    assert.equal(fdmSourceOf(s,{source:'OTA',otaChannel:'BookNest'}).code,'OTA-BOOKNEST');
    assert.equal(fdmSourceOf(s,{source:'OTA · StayHub'}).code,'OTA-STAYHUB');
    assert.equal(fdmSourceOf(s,{source:'OTA',otaChannel:'Demo Travel Hub'}).code,'OTA');
    assert.equal(fdmSourceOf(s,{source:'Corporate',company:{name:'Godavari Agro Inputs (fictional)'}}).code,'CORP-CO-2');
    assert.equal(fdmSourceOf(s,{source:'walk-in'}).code,'WALKIN');assert.equal(fdmSourceOf(s,{source:'Carrier pigeon'}),undefined);
    const fy=M.loadScenario('FY26');assert.equal(fy.bookings.filter(b=>!fdmSourceOf(fy,b)).length,0);});
  test('[FDM] Reports › bookings by source: segments add up, OTA commission from the channel rate, cancelled counted apart',()=>{
    const s=seed(),r=fdmSourceReport(s,'2026-10-01',s.date);
    assert.equal(r.total.bookings+r.rows.reduce((a,x)=>a+x.cancelled,0),s.bookings.filter(b=>b.arrival>='2026-10-01'&&b.arrival<=s.date).length);
    assert.equal(r.segments.reduce((a,g)=>a+g.roomRevenue,0),r.total.roomRevenue);assert.equal(r.segments.reduce((a,g)=>a+g.nights,0),r.total.nights);
    const b={source:'OTA',otaChannel:'BookNest',rate:400000,arrival:'2026-10-24',departure:'2026-10-26'};assert.equal(fdmCommission(s,b),144000); // 18 % of ₹8,000
    const fy=M.loadScenario('FY26'),f=fdmSourceReport(fy,'2026-04-01','2026-10-24');
    assert.ok(f.rows.some(x=>x.code==='OTA-BOOKNEST'&&x.commission>0));assert.ok(!f.rows.some(x=>x.code===''));});
  test('[FDM] Tech admin adds a source; the commission is Owner-only (DEC-058); Reception and Manager are refused',()=>{
    let s=save(seed(),'techadmin','bookingSources',{code:'agent-tg',name:'Travel Guru (agent)',segment:'AGENT'});
    const r=mstByCode(s,'bookingSources','AGENT-TG');assert.equal(r.segment,'AGENT');assert.equal(s.audit[0].detail,'Master Booking sources: added AGENT-TG · Travel Guru (agent)');
    assert.throws(()=>save(s,'techadmin','bookingSources',{...r,commissionBps:1000}),code('FORBIDDEN'));
    s=save(s,'owner','bookingSources',{...r,commissionBps:1000});assert.equal(mstByCode(s,'bookingSources','AGENT-TG').commissionBps,1000);assert.match(s.audit[0].detail,/commissionBps — → 10 %/);
    for(const u of ['reception','manager'])assert.throws(()=>save(seed(),u,'bookingSources',{code:'X',name:'X',segment:'DIRECT'}),code('FORBIDDEN'),u);
    assert.throws(()=>save(seed(),'owner','bookingSources',{code:'X',name:'X',segment:'NOPE'}),reason('MASTER_INVALID'));
    assert.throws(()=>save(seed(),'owner','bookingSources',{code:'X',name:'X',segment:'DIRECT',channel:'BookNest'}),reason('MASTER_INVALID'));});
  test('[FDM] Saving the list keeps the auto rows; a channel added later still registers itself',()=>{
    const s0=seed(),bn=mstByCode(s0,'bookingSources','OTA-BOOKNEST');
    let s=save(s0,'owner','bookingSources',{...bn,commissionBps:2000});
    assert.equal(mstByCode(s,'bookingSources','OTA-BOOKNEST').commissionBps,2000);assert.ok(Array.isArray(s.mastersX.rows.bookingSources));
    s=structuredClone(s);s.channelsX.channels.push({id:'yatra',name:'Yatra Demo',status:'Disconnected',commissionBps:1100,markupBps:0,mapping:{},stopSell:[],statusNote:'test'});
    const y=mstRows(s,'bookingSources').find(r=>r.channel==='Yatra Demo');assert.ok(y&&y.auto&&y.segment==='OTA');
    assert.equal(mstRows(s,'bookingSources').filter(r=>r.channel==='BookNest').length,1);});
  test('[FDM] Booking forms offer active, non-channel sources (+ the current value); retired sources still label old bookings',()=>{
    let s=seed();assert.deepEqual(fdmSourceChoices(s),['Walk-in','Phone','WhatsApp','Direct','Website','OTA','Agent','Corporate']);
    assert.ok(fdmOtaChannelNames(s,['Demo Travel Hub']).includes('BookNest'));
    const id=mstByCode(s,'bookingSources','PHONE').id;s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'bookingSources',id});
    assert.ok(!fdmSourceChoices(s).includes('Phone'));assert.ok(fdmSourceChoices(s,'Phone').includes('Phone'));
    const b=s.bookings.find(x=>x.source==='Phone');assert.equal(fdmSourceOf(s,b).code,'PHONE');assert.equal(fdmSourceOf(s,b).active,false);});
  test('[FDM] A source code in use cannot change (MASTER_IN_USE); an unused one can',()=>{
    const s=seed(),d=mstByCode(s,'bookingSources','DIRECT');
    assert.throws(()=>save(s,'owner','bookingSources',{...d,code:'DIR'}),reason('MASTER_IN_USE'));
    const w=mstByCode(s,'bookingSources','WHATSAPP');const t=save(s,'owner','bookingSources',{...w,code:'WA'});assert.ok(mstByCode(t,'bookingSources','WA'));});

  // ── M11 ID types ──
  test('[FDM] ID types: foreigner-only for foreign nationals, switched-off types not offered, the number check stays in guests.ts',()=>{
    let s=seed();
    assert.ok(!fdmIdTypeChoices(s,'Indian').some(x=>/visa/.test(x)));assert.ok(fdmIdTypeChoices(s,'British').some(x=>/visa/.test(x)));
    const pan=mstByCode(s,'idTypes','PAN');s=save(s,'techadmin','idTypes',{...pan,accepted:false});
    assert.ok(!fdmIdTypeChoices(s,'Indian').includes('PAN (demo)'));assert.ok(fdmIdTypeChoices(s,'Indian','PAN (demo)').includes('PAN (demo)'));
    assert.match(fdmIdTypeProblem(s,'PAN (demo)','Indian'),/not accepted/);assert.match(fdmIdTypeProblem(s,'Passport + visa (demo, foreign national)','Indian'),/foreign nationals only/);
    assert.equal(fdmIdTypeProblem(s,'Aadhaar (demo)','Indian'),'');assert.equal(fdmIdCheck('Aadhaar (demo)'),'Aadhaar: 12 digits with checksum');
    assert.equal(fdmIdType(s,'passport + visa (demo, foreign national)').needsPhoto,true);
    assert.equal(M.guestIdNumberError('Aadhaar (demo)','123'),'Aadhaar must be exactly 12 digits.');});
  test('[FDM] ID types in use (guest profiles) retire but do not delete; Reception may not change the list',()=>{
    const s=seed(),a=mstByCode(s,'idTypes','AADHAAR'),u=mstUsage(s,mstSpec('idTypes'),a);
    if(u.count>0){assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_DELETE',master:'idTypes',id:a.id}),reason('MASTER_IN_USE'));
      const t=execute(s,'techadmin',{type:'MST_MASTER_RETIRE',master:'idTypes',id:a.id});assert.ok(!fdmIdTypeChoices(t,'Indian').includes(a.name));}
    const v=mstByCode(s,'idTypes','VISA');const t=execute(s,'owner',{type:'MST_MASTER_DELETE',master:'idTypes',id:v.id});assert.equal(mstByCode(t,'idTypes','VISA'),undefined);
    assert.throws(()=>save(s,'reception','idTypes',{code:'RATION',name:'Ration card'}),code('FORBIDDEN'));});

  // ── M12 numbering series ──
  test('[FDM] FY helpers: 31 Mar → previous FY, 1 Apr → new FY; formats PREFIX/2627/0001 and PREFIX/00001',()=>{
    assert.equal(fdmFyLabel('2026-03-31'),'2025-26');assert.equal(fdmFyLabel('2026-04-01'),'2026-27');assert.equal(fdmFyCode('2027-01-15'),'2627');
    assert.equal(fdmFormat({prefix:'INV',padding:4,reset:'FY'},7,'2026-10-24'),'INV/2627/0007');assert.equal(fdmFormat({prefix:'RCP',padding:5,reset:'NEVER'},7,'2026-10-24'),'RCP/00007');
    assert.equal(fdmFormat({prefix:'DEMO',padding:4,reset:'FY'},7,'2026-10-24',true),'DEMO/2026-27/0007');});
  test('[FDM] Checkout and credit notes number through the seeded series: DEMO/2026-27/0001, DEMO-CN/2026-27/0001 (old counters, no new slice)',()=>{
    let [s,id]=stay();assert.equal(fdmPreview(s,'INV'),'DEMO/2026-27/0001');
    s=checkout(s,id);assert.equal(bk(s,id).invoiceNo,'DEMO/2026-27/0001');assert.equal(s.invoiceSeqC,1);assert.equal(s.frontdeskMasterX,undefined);
    assert.equal(fdmPreview(s,'INV'),'DEMO/2026-27/0002');assert.deepEqual(fdmIssued(s,mstByCode(s,'numberSeries','INV')).count,1);
    s=execute(s,'reception',{type:'CREDIT_NOTE',id,amount:10000,reason:'Goodwill (MA-5)'});s=execute(s,'owner',{type:'APPROVAL',id:s.approvals.at(-1).id,approve:true});
    assert.equal(bk(s,id).creditNotes[0].no,'DEMO-CN/2026-27/0001');assert.equal(s.creditNoteSeqC,1);});
  test('[FDM] FY26: the next invoice continues the history counter',()=>{
    const fy=M.loadScenario('FY26');const n=fy.invoiceSeqC;assert.ok(n>100);
    assert.equal(fdmPreview(fy,'INV'),'DEMO/2026-27/'+String(n+1).padStart(4,'0'));});
  test('[FDM] Numbering is Owner-only: Tech admin and Manager are refused; Housekeeper sees no policy money fields',()=>{
    const r=mstByCode(seed(),'numberSeries','RCP');
    for(const u of ['techadmin','manager'])assert.throws(()=>save(seed(),u,'numberSeries',{...r,name:'Receipts'}),code('FORBIDDEN'),u);
    const s=save(seed(),'owner','numberSeries',{...r,name:'Receipts'});assert.equal(s.audit[0].detail,'Master Numbering series: name Receipt → Receipts');
    assert.deepEqual(mstVisibleFields(seed(),'housekeeper',mstSpec('policies')).map(f=>f.key),['code','name','freeCancelHours','sources','sortOrder','active']);});
  test('[FDM] A live series never changes its numbering (no gaps); its name may change',()=>{
    let [s,id]=stay();s=checkout(s,id);const inv=mstByCode(s,'numberSeries','INV');
    assert.throws(()=>save(s,'owner','numberSeries',{...inv,prefix:'HCC'}),e=>reason('MASTER_INVALID')(e)&&/has issued 1 number/.test(e.message));
    assert.throws(()=>save(s,'owner','numberSeries',{...inv,start:5}),msg(/cannot change/));
    s=save(s,'owner','numberSeries',{...inv,name:'Tax invoice (hotel)'});assert.equal(mstByCode(s,'numberSeries','INV').name,'Tax invoice (hotel)');});
  test('[FDM] A new tax-invoice series starts from a date with no documents: not this FY once it has numbers, yes from 1 April',()=>{
    let [s,id]=stay();s=checkout(s,id);
    const row={code:'INV-2027',name:'Tax invoice 2027-28',document:'INV',prefix:'HCC',start:1,padding:5,reset:'FY',activeFrom:'2026-12-01'};
    assert.throws(()=>save(s,'owner','numberSeries',row),msg(/already has tax invoice numbers — the new series can start from 2027-04-01/));
    assert.throws(()=>save(s,'owner','numberSeries',{...row,activeFrom:'2025-04-01'}),msg(/today's business date/));
    s=save(s,'owner','numberSeries',{...row,activeFrom:'2027-04-01'});
    assert.equal(fdmSeriesFor(s,'INV').code,'INV');assert.equal(fdmSeriesFor(s,'INV','2027-04-01').code,'INV-2027');
    assert.equal(fdmPreview(s,'INV','2027-04-01'),'HCC/2728/00001');
    assert.throws(()=>save(s,'owner','numberSeries',{...row,code:'INV-X',name:'Another 2027-28',activeFrom:'2027-04-01'}),msg(/already starts tax invoice numbers on 2027-04-01/));});
  test('[FDM] GST documents restart yearly and fit 16 characters; prefix and date shapes are checked',()=>{
    const s=seed(),row={code:'BOS-2',name:'Bill of supply 2',document:'BOS',prefix:'BS',start:1,padding:4,reset:'FY',activeFrom:'2027-04-01'};
    assert.throws(()=>save(s,'owner','numberSeries',{...row,reset:'NEVER'}),msg(/restart every financial year/));
    assert.throws(()=>save(s,'owner','numberSeries',{...row,prefix:'LONGPRE',padding:6}),msg(/at most 16/));
    assert.throws(()=>save(s,'owner','numberSeries',{...row,prefix:'bs'}),msg(/capital letters/));
    assert.throws(()=>save(s,'owner','numberSeries',{...row,prefix:'ABCDEFG',padding:1}),msg(/at most 6 characters for GST/));
    assert.throws(()=>save(s,'owner','numberSeries',{...row,activeFrom:'01-04-2027'}),msg(/YYYY-MM-DD/));
    assert.ok(mstByCode(save(s,'owner','numberSeries',row),'numberSeries','BOS-2'));
    assert.ok(mstByCode(save(s,'owner','numberSeries',{code:'KOT-2',name:'KOT 2',document:'KOT',prefix:'K',start:1,padding:6,reset:'NEVER',activeFrom:'2026-11-01'}),'numberSeries','KOT-2'));});
  test('[FDM] A new series from today numbers the next checkout (start number, padding) and keeps its own counter',()=>{
    let s=seed();s=save(s,'owner','numberSeries',{code:'INV-NEW',name:'Tax invoice (new)',document:'INV',prefix:'HCC',start:101,padding:5,reset:'FY',activeFrom:s.date});
    let [t,id]=stay(s);t=checkout(t,id);assert.equal(bk(t,id).invoiceNo,'HCC/2627/00101');
    const k=Object.keys(t.frontdeskMasterX.counters);assert.equal(k.length,1);assert.deepEqual(t.frontdeskMasterX.counters[k[0]],{last:101,lastDate:t.date,count:1});
    const other='HCC-1041';t=execute(t,'owner',{type:'POST_NIGHTS',id:other});t=execute(t,'cashier',{type:'PAYMENT',id:other,amount:totals(bk(t,other)).balance,method:'UPI',reference:'DEMO-V'});
    t=checkout(t,other);assert.equal(bk(t,other).invoiceNo,'HCC/2627/00102');assert.equal(t.invoiceSeqC??0,0);
    assert.throws(()=>save(t,'owner','numberSeries',{...mstByCode(t,'numberSeries','INV-NEW'),padding:6}),msg(/has issued 2 numbers/));});
  test('[FDM] Receipt / KOT series without a yearly reset allocate PREFIX/00001, PREFIX/00002 across years',()=>{
    const s=structuredClone(seed());assert.equal(fdmAllocate(s,'RCP'),'RCP/00001');assert.equal(fdmAllocate(s,'RCP','2027-05-01'),'RCP/00002');
    assert.equal(fdmAllocate(s,'KOT'),'KOT/0001');assert.equal(Object.keys(s.frontdeskMasterX.counters).length,2);});
  test('[FDM] The series in force cannot be retired; a superseded or future one can; with none in force checkout is refused',()=>{
    let s=seed();const inv=mstByCode(s,'numberSeries','INV');
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'numberSeries',id:inv.id}),reason('MASTER_IN_USE'));
    s=save(s,'owner','numberSeries',{code:'INV-27',name:'Tax invoice FY 2027-28',document:'INV',prefix:'HCC',start:1,padding:5,reset:'FY',activeFrom:'2027-04-01'});
    const f=mstByCode(s,'numberSeries','INV-27');s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'numberSeries',id:f.id});assert.equal(mstByCode(s,'numberSeries','INV-27').active,false);
    s=execute(s,'owner',{type:'MST_MASTER_DELETE',master:'numberSeries',id:f.id});assert.equal(mstByCode(s,'numberSeries','INV-27'),undefined);
    const bare=structuredClone(seed());bare.mastersX={rows:{numberSeries:[]},history:[]};assert.throws(()=>fdmAllocate(bare,'INV'),e=>e.code==='INVALID_TRANSITION'&&/No numbering series is in force/.test(e.message));});

  // ── M14 policies ──
  test('[FDM] Default policy applies to every booking and changes nothing (no charges, no advance)',()=>{
    const s=seed(),b=s.bookings[0];assert.equal(fdmPolicyFor(s,b).code,'DEFAULT');
    assert.deepEqual([fdmCancelCharge(s,b).amount,fdmNoShowCharge(s,b),fdmAdvanceDue(s,b)],[0,0,0]);
    assert.equal(fdmPolicyText(fdmPolicyFor(s,b)),'Default: free cancellation · no-show no charge · advance not required.');});
  test('[FDM] A named OTA policy: 48 h free window, 100 % first night, 25 % advance; other sources keep Default',()=>{
    let s=save(seed(),'owner','policies',{code:'OTA-STD',name:'OTA standard',freeCancelHours:48,cancelChargeBps:10000,noShowChargeBps:10000,depositBps:2500,sources:['OTA','OTA-BOOKNEST']});
    const b={id:'X',source:'OTA',otaChannel:'BookNest',rate:300000,arrival:'2026-10-25',departure:'2026-10-27',status:'Confirmed'};
    assert.equal(fdmPolicyFor(s,b).code,'OTA-STD');assert.equal(fdmPolicyFor(s,{source:'Phone'}).code,'DEFAULT');
    const c=fdmCancelCharge(s,b);assert.equal(c.free,false);assert.equal(c.amount,300000);
    assert.equal(fdmCancelCharge(s,{...b,arrival:'2026-10-30',departure:'2026-11-01'}).amount,0);
    assert.equal(fdmNoShowCharge(s,b),300000);assert.equal(fdmAdvanceDue(s,b),150000);
    assert.match(fdmPolicyText(fdmPolicyFor(s,b)),/free cancellation until 48 h before arrival, then 100 % of the first night/);
    assert.equal(mstUsage(s,mstSpec('bookingSources'),mstByCode(s,'bookingSources','OTA')).where.some(w=>/Policies OTA standard/.test(w)),true);});
  test('[FDM] Policy rules: one default, a source follows one policy, Default cannot be retired, Owner only',()=>{
    let s=seed();
    assert.throws(()=>save(s,'owner','policies',{code:'P2',name:'Second default',freeCancelHours:0}),msg(/already the default policy/));
    s=save(s,'owner','policies',{code:'CORP',name:'Corporate',freeCancelHours:24,sources:['CORPORATE']});
    assert.throws(()=>save(s,'owner','policies',{code:'CORP2',name:'Corporate 2',freeCancelHours:12,sources:['CORPORATE']}),msg(/already follows Corporate/));
    assert.throws(()=>execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'policies',id:'pol-default'}),reason('MASTER_IN_USE'));
    for(const u of ['techadmin','manager'])assert.throws(()=>save(seed(),u,'policies',{code:'X',name:'X',freeCancelHours:1,sources:['PHONE']}),code('FORBIDDEN'),u);});

  // ── Framework behaviour, history, UI wiring ──
  test('[FDM] Idempotent repeat; history per changed field (who, old → new)',()=>{
    const s0=seed(),r=mstByCode(s0,'idTypes','OTHER');
    const s=save(s0,'techadmin','idTypes',{...r,needsPhoto:true,name:'Other ID (demo)'},'fdm:1');assert.equal(save(s,'techadmin','idTypes',{...r,needsPhoto:true,name:'Other ID (demo)'},'fdm:1'),s);
    const h=mstHistory(s,'idTypes',r.id);assert.deepEqual(h.map(x=>[x.field,x.old,x.new,x.by]).sort(),[['name','Other (demo)','Other ID (demo)','techadmin'],['needsPhoto','No','Yes','techadmin']]);});
  test('[FDM] Screens use the masters: booking forms, registration ID list, cancel note, confirmation policy, source report, numbering in checkout',()=>{
    assert.match(src('src/features/reservations/modals.tsx'),/fdmSourceChoices\(s,source\)/);assert.match(src('src/features/reservations/modals.tsx'),/<FdmCancelNote b=\{b\}\/>/);
    assert.match(src('src/mobile/frontdesk/NewBooking.tsx'),/fdmSourceChoices\(s,d\.source\)/);
    assert.match(src('src/features/guests/GuestForm.tsx'),/fdmIdTypeChoices\(s,d\.nationality,d\.idType\)/);
    assert.match(src('src/features/reservations/Confirmation.tsx'),/<FdmPolicyLine b=\{b\}\/>/);
    assert.match(src('src/features/reports/Reports.tsx'),/<FdmSourceReport from=\{from\} to=\{to\}\/>/);
    const f=src('src/services/mock/handlers/folio.ts');assert.match(f,/fdmAllocate\(s,'INV'\)/);assert.match(f,/fdmAllocate\(s,'CRN'\)/);
    assert.match(src('src/clients/sync/masters.ts'),/\.\.\.FDM_SERVER/);});
}
