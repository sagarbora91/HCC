// MA-8 · Import / export of masters (plan Part 2 §8 M30): masters JSON export / import, CSV text of a list, "Paste rows", the dry-run preview,
// permissions, atomicity and the header sync with the server import templates (08 Production/deployment/import-templates). Names prefixed [MIO].
import {readFileSync,readdirSync} from 'node:fs';
import {createHash} from 'node:crypto';
export async function register({test,M,assert}){
  const {seed,execute,masterSpecs,mstSpec,mstRows,mstByCode,mstHistory,mstExport,mstSetsFromExport,mstImportPreview,mstToCsv,mstRowsFromCsv,mstParseCsv,mstCsvColumns,mstIoFields,
    mstMoneyOn,mstMergeDated,mstImportOrder,MST_IO_WIRE,MST_IO_SKIP,MST_IO_FORMAT,HccError}=M;
  const sha=o=>createHash('sha256').update(JSON.stringify(o)).digest('hex');
  const strip=s=>{const {mastersX,grants,...r}=s;return r;};
  const base=seed();
  const IO_BASE=['code','name','sortOrder','active'];
  /** A row as files carry it: its io fields only; a dated amount reduced to the amount in force today (a new row keeps no past amounts). */
  const norm=(s,spec,r,dated='now')=>{const o={};for(const k of [...IO_BASE,...mstIoFields(spec).map(f=>f.key)]){
    if(r[k]===undefined||r[k]===''||r[k]===null||(Array.isArray(r[k])&&!r[k].length&&spec.fields.find(x=>x.key===k)?.type!=='datedMoney'))continue;const f=spec.fields.find(x=>x.key===k);
    o[k]=f?.type==='datedMoney'&&dated==='now'?mstMoneyOn(r[k],s.date):r[k];}return o;};
  /** Seed with every master's rows stored, then one master emptied (a second property that has none of it yet). */
  const emptied=id=>{const s=structuredClone(base);
    s.mastersX={rows:Object.fromEntries(masterSpecs().filter(x=>!x.store).map(x=>[x.id,mstRows(base,x.id).map(r=>({...r}))])),history:[]};
    const sp=mstSpec(id);if(sp.store)sp.store.write(s,[]);else s.mastersX.rows[id]=[];return s;};
  let seq=0;
  const importSets=(s,u,sets,key)=>execute(s,u,{type:'MST_IMPORT',sets,key:key??'mio:'+(++seq)+':'+sets.map(x=>x.master).join('+')});
  /** Masters that cannot be rebuilt from nothing in one step: their own rules need rows that only exist together (rooms ↔ room types), a series cannot start
   *  in the past, and a leave policy needs an unpaid type before a paid one. They are covered by the same-state round trip and by their own tests. */
  // holidays: a paid holiday of a locked pay month cannot be added (MA-11, DEC-MA-11c), so the fixture's August 2026 row cannot be rebuilt from an empty list; the same-state round trip still covers it.
  const NOT_FROM_EMPTY=['roomTypes','rooms','numberSeries','leaveTypes','holidays'];

  // ── Registry ──
  test('[MIO] CSV columns = code, name, the FieldSpec keys in order, sortOrder, active (no ID numbers, no per-day toggles); every master has a server file except room types and rooms',()=>{
    assert.deepEqual(mstCsvColumns(mstSpec('floors')),['code','name','sortOrder','active']);
    assert.deepEqual(mstCsvColumns(mstSpec('hkTaskTypes')),['code','name','appliesTo','standardMinutes','needsInspection','checklist','sortOrder','active']);
    assert.ok(!mstCsvColumns(mstSpec('expenseVendors')).includes('pan'));assert.ok(!mstCsvColumns(mstSpec('menuItems')).includes('soldOutToday'));
    const without=masterSpecs().map(x=>x.id).filter(id=>!MST_IO_WIRE[id]);assert.deepEqual(without.sort(),['roomTypes','rooms']);
    assert.equal(new Set(Object.values(MST_IO_WIRE)).size,Object.keys(MST_IO_WIRE).length);
    assert.deepEqual(mstImportOrder(['rooms','floors','roomTypes','amenities']).filter(x=>['rooms','floors','roomTypes'].includes(x)).indexOf('rooms'),2);});

  // ── JSON export ──
  test('[MIO] Owner export: every master, rows without ids, nothing secret (no PAN, no sold-out toggle, no id / retiredOn), format header, fixture untouched',()=>{
    const e=mstExport(base,'owner');
    assert.equal(e.format,MST_IO_FORMAT);assert.equal(e.version,1);assert.equal(e.businessDate,base.date);
    assert.deepEqual(Object.keys(e.masters).sort(),masterSpecs().map(x=>x.id).sort());
    const text=JSON.stringify({...e,excluded:[]});
    assert.ok(!/"id"/.test(text)&&!/"retiredOn"/.test(text)&&!/"pan"/.test(text)&&!/soldOutToday/.test(text));
    assert.ok(e.excluded.some(x=>x.startsWith('expenseVendors.pan')));
    assert.deepEqual(e.masters.floors.rows,[{code:'F1',name:'Floor 1',sortOrder:10,active:true},{code:'F2',name:'Floor 2',sortOrder:20,active:true},{code:'F3',name:'Floor 3',sortOrder:30,active:true}]);
    assert.equal(sha(strip(seed())),sha(strip(base)),'exporting changes nothing');});
  test('[MIO] Tech admin export leaves the Owner-only lists out and says why; Reception exports nothing',()=>{
    const t=mstExport(base,'techadmin');
    for(const sp of masterSpecs().filter(x=>x.money||x.permission==='settings'))assert.ok(!t.masters[sp.id]&&t.excluded.some(x=>x.startsWith(sp.id+' — ')),sp.id);
    assert.ok(t.masters.floors&&t.masters.hkZones);
    assert.deepEqual(Object.keys(mstExport(base,'reception').masters),[]);});
  test('[MIO] A role without money visibility gets no money fields even from an operational list it may edit (DEC-061)',()=>{
    const g=structuredClone(base);g.grants['Housekeeper']=[...(g.grants['Housekeeper']??[]),'masters'];
    const e=mstExport(g,'housekeeper');
    for(const sp of masterSpecs())for(const r of e.masters[sp.id]?.rows??[])for(const f of sp.fields.filter(x=>x.money||x.type==='money'||x.type==='datedMoney'))assert.equal(r[f.key],undefined,sp.id+'.'+f.key);});

  // ── JSON round trip ──
  for(const sp of masterSpecs().filter(x=>!NOT_FROM_EMPTY.includes(x.id))){
    test(`[MIO] round trip ${sp.id}: export → import into a property without the list → the same rows (code, name, order, active and every field)`,()=>{
      const e=mstExport(base,'owner'),want=mstRows(base,sp.id).map(r=>norm(base,sp,r));
      const s=emptied(sp.id);assert.ok(mstRows(s,sp.id).length<want.length||want.length===0);
      const sets=[{master:sp.id,rows:e.masters[sp.id].rows}];
      const rep=mstImportPreview(s,'owner',sets);assert.equal(rep.counts.errors,0,JSON.stringify(rep.items.filter(i=>i.action==='error').slice(0,2)));
      const t=importSets(s,'owner',sets);
      assert.deepEqual(mstRows(t,sp.id).map(r=>norm(t,sp,r)),want);
      assert.ok(mstHistory(t,sp.id).length>0||want.length===0,'history rows written like the list form');});
  }
  test('[MIO] same-state round trip: importing the own export plans every row of every master as unchanged and the command refuses "nothing to import"',()=>{
    const e=mstExport(base,'owner');
    for(const sp of masterSpecs()){
      const rep=mstImportPreview(base,'owner',[{master:sp.id,rows:e.masters[sp.id].rows}]);
      assert.deepEqual(rep.counts,{create:0,update:0,unchanged:e.masters[sp.id].rows.length,skipped:0,errors:0},sp.id);}
    assert.throws(()=>importSets(base,'owner',[{master:'floors',rows:e.masters.floors.rows}]),/Nothing to import: 3 unchanged/);});
  test('[MIO] the whole export in one file imports in dependency order (floors before rooms, outlets before tables) and the preview matches the result',()=>{
    const e=mstExport(base,'owner'),{sets,errors,skipped}=mstSetsFromExport(e);
    assert.deepEqual([errors,skipped],[[],[]]);assert.ok(sets.findIndex(x=>x.master==='floors')<sets.findIndex(x=>x.master==='rooms'));assert.ok(sets.findIndex(x=>x.master==='outlets')<sets.findIndex(x=>x.master==='posTables'));
    const changed=structuredClone(e);changed.masters.discountReasons.rows[0].name='Service recovery (changed)';changed.masters.amenities.rows.push({code:'POOL',name:'Swimming pool',showOnWebsite:true});
    const rep=mstImportPreview(base,'owner',mstSetsFromExport(changed).sets);
    assert.deepEqual([rep.counts.create,rep.counts.update,rep.counts.errors],[1,1,0]);
    const t=importSets(base,'owner',mstSetsFromExport(changed).sets);
    assert.equal(mstByCode(t,'discountReasons',changed.masters.discountReasons.rows[0].code).name,'Service recovery (changed)');assert.equal(mstByCode(t,'amenities','POOL').name,'Swimming pool');
    assert.match(t.audit[0].detail,/^Masters import · 1 added · 1 changed · /);assert.equal(t.audit[0].action,'MST IMPORT');
    assert.ok(mstHistory(t,'discountReasons').some(h=>h.field==='name'&&h.new==='Service recovery (changed)'));});
  test('[MIO] not an export file / wrong version / unknown list: clear problems, nothing guessed',()=>{
    assert.match(mstSetsFromExport({format:'other'}).errors[0],/not a masters export/);assert.match(mstSetsFromExport({format:MST_IO_FORMAT,version:2,masters:{}}).errors[0],/Unsupported export version 2/);
    const r=mstSetsFromExport({format:MST_IO_FORMAT,version:1,masters:{nope:{rows:[]},floors:{rows:[]}}});assert.deepEqual(r.skipped,[{master:'nope',reason:'Not in this registry'}]);assert.equal(r.sets.length,1);});

  // ── Rules of the import ──
  test('[MIO] a file never retires an active row; a retired row needs active:yes to change; a new row may arrive retired',()=>{
    const row=r=>({code:'F2',name:'First floor',...r});
    assert.equal(mstImportPreview(base,'owner',[{master:'floors',rows:[row({active:false})]}]).items[0].action,'error');
    let s=execute(base,'owner',{type:'MST_MASTER_RETIRE',master:'floors',id:'flr-2'});
    assert.equal(mstImportPreview(s,'owner',[{master:'floors',rows:[row({})]}]).items[0].action,'error');
    const rep=mstImportPreview(s,'owner',[{master:'floors',rows:[row({active:true,name:'Level one'})]}]);assert.equal(rep.items[0].action,'update');
    s=importSets(s,'owner',[{master:'floors',rows:[row({active:true,name:'Level one'})]}],'mio:react');assert.deepEqual([mstByCode(s,'floors','F2').active,mstByCode(s,'floors','F2').name],[true,'Level one']);
    s=importSets(s,'owner',[{master:'floors',rows:[{code:'F9',name:'Old annexe',active:false}]}],'mio:born-retired');assert.equal(mstByCode(s,'floors','F9').active,false);assert.equal(mstByCode(s,'floors','F9').retiredOn,s.date);});
  test('[MIO] all or nothing: one bad row refuses the whole command with the first problems and leaves the state as it was',()=>{
    const before=JSON.stringify(base);
    const sets=[{master:'floors',rows:[{code:'F8',name:'Good annexe'},{code:'bad code!',name:'Bad'},{code:'F1',name:''}]}];
    const rep=mstImportPreview(base,'owner',sets);assert.deepEqual([rep.counts.create,rep.counts.errors],[1,2]);
    assert.throws(()=>importSets(base,'owner',sets),e=>e instanceof HccError&&e.code==='VALIDATION'&&/2 rows refused — nothing was imported/.test(e.message));
    assert.equal(JSON.stringify(base),before);assert.equal(mstByCode(base,'floors','F8'),undefined);});
  test('[MIO] duplicate codes in a file, a missing code and unknown keys: reported per row; unknown keys are dropped',()=>{
    const rep=mstImportPreview(base,'owner',[{master:'floors',rows:[{code:'F8',name:'A',bogus:1,id:'x'},{code:'f8',name:'B'},{name:'No code'}]}]);
    assert.deepEqual(rep.items.map(i=>i.action),['create','error','error']);assert.match(rep.items[1].detail,/Duplicate/);
    const t=importSets(base,'owner',[{master:'floors',rows:[{code:'F8',name:'A',bogus:1,id:'x'}]}]);assert.equal(mstByCode(t,'floors','F8').bogus,undefined);assert.notEqual(mstByCode(t,'floors','F8').id,'x');});
  test('[MIO] permissions: Reception refused; Tech admin changes operational lists and skips the Owner-only ones with the reason; Owner does both',()=>{
    const sets=[{master:'floors',rows:[{code:'F8',name:'Annexe'}]},{master:'paymentMethods',rows:[{code:'WALLET',name:'Wallet',kind:'wallet',settlesTo:'BANK-1'}]}];
    assert.throws(()=>importSets(base,'reception',sets),e=>e.code==='FORBIDDEN');
    const rep=mstImportPreview(base,'techadmin',sets);assert.equal(rep.counts.create,1);assert.deepEqual(rep.skippedMasters.map(x=>x.master),['paymentMethods']);
    const t=importSets(base,'techadmin',sets);assert.ok(mstByCode(t,'floors','F8'));assert.equal(mstByCode(t,'paymentMethods','WALLET'),undefined);
    assert.equal(mstImportPreview(base,'owner',sets).counts.skipped,0);});
  test('[MIO] money fields of an operational list are Owner-only inside a file too: the Tech admin\'s row is skipped, the Owner\'s applies',()=>{
    const row={code:'LASSI',name:'Sweet lassi',category:'BEVERAGES',price:[{amount:9000,effectiveFrom:base.date}],gstClass:'food',veg:true,outlets:['RESTAURANT']};
    const rep=mstImportPreview(base,'techadmin',[{master:'menuItems',rows:[row]}]);assert.equal(rep.items[0].action,'skipped');
    const t=importSets(base,'owner',[{master:'menuItems',rows:[row]}]);assert.equal(mstMoneyOn(mstByCode(t,'menuItems','LASSI').price,t.date),9000);});
  test('[MIO] dated amounts: a new amount starts today, the old one stays in the list; scheduled future amounts follow a JSON file, a CSV keeps them',()=>{
    const e=mstExport(base,'owner'),ex=structuredClone(e.masters.extras.rows);
    const lau=ex.find(r=>r.code==='LAUNDRY');lau.price=[...lau.price.filter(x=>x.effectiveFrom<base.date),{amount:60000,effectiveFrom:base.date}];
    const t=importSets(base,'owner',[{master:'extras',rows:[lau]}]);
    assert.deepEqual(mstByCode(t,'extras','LAUNDRY').price,[{amount:50000,effectiveFrom:'2025-04-01'},{amount:60000,effectiveFrom:base.date}]);
    const fut=execute(t,'owner',{type:'MST_MASTER_SAVE',master:'extras',row:{id:mstByCode(t,'extras','LAUNDRY').id,price:[...mstByCode(t,'extras','LAUNDRY').price,{amount:70000,effectiveFrom:'2027-01-01'}]}});
    const csv=mstToCsv(fut,mstSpec('extras'),mstRows(fut,'extras').filter(r=>r.code==='LAUNDRY'));
    const parsed=mstRowsFromCsv(fut,mstSpec('extras'),csv.replace('600.00','650.00'));
    const u=importSets(fut,'owner',[{master:'extras',source:'csv',rows:parsed.rows.map(x=>x.row)}]);
    assert.deepEqual(mstByCode(u,'extras','LAUNDRY').price.map(x=>x.amount),[50000,65000,70000],'CSV: today\'s amount changes, the scheduled one stays');
    assert.deepEqual(mstMergeDated([{amount:1,effectiveFrom:'2025-01-01'}],[],'2026-10-24'),[{amount:1,effectiveFrom:'2025-01-01'}]);});

  // ── CSV ──
  test('[MIO] CSV round trip per master: mstToCsv → mstRowsFromCsv → import into a property without the list gives the same rows as the JSON route',()=>{
    for(const sp of masterSpecs().filter(x=>!NOT_FROM_EMPTY.includes(x.id))){
      const csv=mstToCsv(base,sp),res=mstRowsFromCsv(base,sp,csv);
      assert.deepEqual(res.errors,[],sp.id);assert.deepEqual(res.ignored,[],sp.id);
      assert.equal(csv.split('\r\n')[0],mstCsvColumns(sp).join(','),sp.id);
      const s=emptied(sp.id);
      const t=importSets(s,'owner',[{master:sp.id,source:'csv',rows:res.rows.map(x=>x.row)}],'mio:csv:'+sp.id);
      assert.deepEqual(mstRows(t,sp.id).map(r=>norm(t,sp,r)),mstRows(base,sp.id).map(r=>norm(base,sp,r)),sp.id);}});
  test('[MIO] CSV quoting: commas, quotes and line breaks in cells; BOM; semicolon or tab delimiter; blank lines skipped',()=>{
    const p=mstParseCsv('﻿code;name\r\nA;"Say ""hi"", ok"\r\n\r\nB;"two\nlines"\r\n');
    assert.deepEqual(p.headers,['code','name']);assert.deepEqual(p.rows.map(r=>r.cells),[['A','Say "hi", ok'],['B','two\nlines']]);
    assert.equal(mstParseCsv('code\tname\nA\tB\n').rows[0].cells[1],'B');assert.match(mstParseCsv('code,name\n"A,B').error,/never closed/);assert.match(mstParseCsv('  \n').error,/Paste the rows/);});
  test('[MIO] Paste rows: cells go through the FieldSpec — numbers, rupees, percent, yes/no, option by value or label, lists, dated amount, multi-line checklist',()=>{
    const csv='code,name,category,price,gst,unit,department,sortOrder\nspa,Spa visit,"misc",₹ 1,200.50,"other",each,frontdesk,5\n';
    const bad=mstRowsFromCsv(base,mstSpec('extras'),'code,name,category,price,gst,unit\nX,Bad,boat,abc,food,each\n');
    assert.deepEqual(bad.errors.map(e=>[e.line,e.column]),[[2,'category'],[2,'price']]);
    const ok=mstRowsFromCsv(base,mstSpec('extras'),'code,name,category,price,gst,unit,department,sortOrder\nspa,Spa visit,Misc,"₹ 1,200.50",Other,each,Frontdesk,5\n');
    assert.deepEqual(ok.errors,[]);const r=ok.rows[0].row;assert.deepEqual([r.code,r.category,r.gst,r.unit,r.department,r.sortOrder],['SPA','misc','other','each','frontdesk',5]);
    assert.deepEqual(r.price,[{amount:120050,effectiveFrom:base.date}]);assert.ok(csv);
    const disc=mstRowsFromCsv(base,mstSpec('discountReasons'),'code,name,maxPct,approvalAbovePct,roles\nrep,Repeat guest,"10%",5,Owner;manager\n').rows[0].row;
    assert.deepEqual([disc.maxPct,disc.approvalAbovePct,disc.roles],[1000,500,['Owner','Manager']]);
    const hk=mstRowsFromCsv(base,mstSpec('hkTaskTypes'),'code,name,appliesTo,standardMinutes,needsInspection,checklist\nQ,Quick,checkout;move,30,yes,Linen;Bathroom;Final check\n').rows[0].row;
    assert.deepEqual([hk.appliesTo,hk.standardMinutes,hk.needsInspection,hk.checklist],[['checkout','move'],30,true,'Linen\nBathroom\nFinal check']);
    const idt=mstRowsFromCsv(base,mstSpec('idTypes'),'code,name,accepted,needsPhoto\nX,Ration card,,\nY,Student card,no,\n').rows;assert.deepEqual(idt.map(x=>x.row.accepted),[true,false],'a blank accepted cell means the field default: yes');
    assert.equal(mstByCode(importSets(base,'owner',[{master:'idTypes',rows:[{code:'Z',name:'Pension book'}]}]),'idTypes','Z').accepted,true,'a new row without the column takes the default too');
    const z=mstRowsFromCsv(base,mstSpec('hkZones'),'code,name,floors,defaultAttendant\nZ,Zone,Floor 1;f2,'+(mstSpec('hkZones').fields.find(f=>f.key==='defaultAttendant').optionsFrom(base)[0]?.label??'')+'\n');
    assert.deepEqual(z.rows[0].row.floors,['F1','F2'],'a master pick by code or name');});
  test('[MIO] Paste rows problems: missing required column, duplicate / unknown columns, empty text, a PAN column, per-row cell errors with the line number',()=>{
    const sp=mstSpec('extras');
    assert.match(mstRowsFromCsv(base,sp,'code,name\nA,B\n').errors[0].message,/Required column “category” is missing/);
    const r=mstRowsFromCsv(base,sp,'code,name,category,gst,unit,colour,colour\nA,B,misc,other,each,red,blue\n');assert.deepEqual(r.ignored,['colour','colour']);
    assert.match(mstRowsFromCsv(base,sp,'code,name,category,gst,unit,code\nA,B,misc,other,each,A\n').errors[0].message,/appears twice/);
    assert.match(mstRowsFromCsv(base,sp,'').errors[0].message,/Paste the rows/);assert.match(mstRowsFromCsv(base,sp,'code,name,category,gst,unit\n').errors[0].message,/no rows/);
    assert.match(mstRowsFromCsv(base,mstSpec('expenseVendors'),'code,name,type,pan\nV,Vendor,supplier,ABCDE1234F\n').errors[0].message,/never carry ID numbers/);
    const e=mstRowsFromCsv(base,mstSpec('shiftPatterns'),'code,name,start,end,breakMinutes\nM,Morning,07:00,15:00,x\nN,Night,22:00,06:00,30\n');
    assert.deepEqual(e.errors.map(x=>[x.line,x.column]),[[2,'breakMinutes']]);assert.equal(e.rows.length,1);});

  // ── Header sync with the server import (08 Production) ──
  const TPL='../08 Production/deployment/import-templates';
  test('[MIO] header sync: the server template of every master has exactly the prototype CSV columns (pos-outlets adds the tables column)',()=>{
    const names=readdirSync(TPL+'/templates').filter(f=>f.endsWith('.csv'));
    for(const [id,wire] of Object.entries(MST_IO_WIRE)){
      assert.ok(names.includes(wire+'.csv'),`template for ${id}: ${wire}.csv`);
      const header=readFileSync(`${TPL}/templates/${wire}.csv`,'utf8').split(/\r?\n/)[0].split(',');
      const want=mstCsvColumns(mstSpec(id));if(id==='outlets')want.push('tables');
      assert.deepEqual(header,want,id+' ↔ '+wire);
      const sample=readFileSync(`${TPL}/samples/${wire}.csv`,'utf8').split(/\r?\n/)[0].split(',');assert.deepEqual(sample,want,'sample '+wire);}
    const legacy=['room-types','rooms','tariffs','seasons','staff','ledgers','opening-balances','till-cash','guests','departments','companies','suppliers','stock-items'];
    assert.deepEqual(names.map(f=>f.replace(/\.csv$/,'')).filter(w=>!Object.values(MST_IO_WIRE).includes(w)).sort(),legacy.sort(),'every other template is one of the older imports');});
  // ── V14-7: Paste rows for room types and rooms (demo build), rooms through the range-add rules ──
  test('[MIO] V14-7 every master offers Paste rows in the demo build: the server-file lists plus room types and rooms (MST_IO_LOCAL)',()=>{
    assert.deepEqual(M.MST_IO_LOCAL,['roomTypes','rooms']);
    assert.deepEqual(masterSpecs().filter(x=>!M.mstCanPaste(x.id)).map(x=>x.id),[]);});
  test('[MIO] V14-7 Paste rows adds rooms like range add: name optional (Room <n>), floor / type by code or name, Ready, rack rate of the type, added today',()=>{
    const res=mstRowsFromCsv(base,mstSpec('rooms'),'code,floor,type,smoking\n404,F3,Deluxe,no\n405,Floor 3,DELUXE,yes\n');
    assert.deepEqual(res.errors,[]);assert.deepEqual(res.rows.map(x=>[x.row.code,x.row.name,x.row.floor,x.row.type,x.row.smoking]),[['404','Room 404','F3','DELUXE',false],['405','Room 405','F3','DELUXE',true]]);
    const sets=[{master:'rooms',source:'csv',rows:res.rows.map(x=>x.row)}];
    assert.deepEqual(mstImportPreview(base,'owner',sets).counts,{create:2,update:0,unchanged:0,skipped:0,errors:0});
    const t=importSets(base,'techadmin',sets),r=t.rooms.find(x=>x.id==='405');
    assert.deepEqual([r.floor,r.type,r.hk,r.rate,r.smoking,r.addedOn,r.blocked],[3,'Deluxe','Ready',300000,true,base.date,false]);
    assert.equal(mstRows(t,'rooms').length,26);assert.ok(M.available(t,'404',base.date,M.addDays(base.date,2)),'bookable from today (V14-5)');
    assert.ok(mstHistory(t,'rooms').some(h=>h.code==='404'),'history like the form');});
  test('[MIO] V14-7 Paste rows refuses rooms the way range add does: number on past stays, retired type, floor not F<n>, unknown type',()=>{
    let s=structuredClone(base);const past=s.bookings.find(b=>b.status==='Departed')??s.bookings[0];s.bookings.push({...past,id:'HCC-T499',room:'499',status:'Departed'});
    s=execute(s,'owner',{type:'MST_MASTER_SAVE',master:'floors',row:{code:'ROOF',name:'Roof'}});
    s=execute(s,'owner',{type:'MST_MASTER_SAVE',master:'roomTypes',row:{code:'OLD',name:'Old type',maxAdults:2,rackRate:[{amount:100000,effectiveFrom:s.date}]}});
    s=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'roomTypes',id:mstByCode(s,'roomTypes','OLD').id});
    const rows=[{code:'499',floor:'F3',type:'DELUXE'},{code:'406',floor:'F3',type:'OLD'},{code:'407',floor:'ROOF',type:'DELUXE'},{code:'408',floor:'F3',type:'NOPE'},{code:'409',floor:'F3',type:'DELUXE'}];
    const rep=mstImportPreview(s,'owner',[{master:'rooms',rows}]);
    assert.deepEqual(rep.items.map(i=>[i.code,i.action]),[['499','error'],['406','error'],['407','error'],['408','error'],['409','create']]);
    assert.equal(rep.items[0].detail,M.rmsNumberTaken(s,'499'),'the same message as range add');
    assert.match(rep.items[1].detail,/retired/);assert.match(rep.items[2].detail,/F<number>/);assert.match(rep.items[3].detail,/not in Room types/);
    assert.throws(()=>importSets(s,'owner',[{master:'rooms',rows}]),/4 rows refused — nothing was imported/);
    const again=mstImportPreview(base,'owner',[{master:'rooms',rows:[{code:'101',floor:'F1',type:'CLASSIC'}]}]);assert.equal(again.items[0].action,'unchanged','a known number updates that room, never a second one');});
  test('[MIO] V14-7 Paste rows adds room types (the Owner sets the rack rate; the Tech admin\'s rack-rate row is skipped)',()=>{
    const res=mstRowsFromCsv(base,mstSpec('roomTypes'),'code,name,maxAdults,maxChildren,bedType,rackRate\nFAM,Family room,4,2,King,3500\n');assert.deepEqual(res.errors,[]);
    const sets=[{master:'roomTypes',source:'csv',rows:res.rows.map(x=>x.row)}];
    const t=importSets(base,'owner',sets),fam=mstByCode(t,'roomTypes','FAM');
    assert.deepEqual([fam.name,fam.maxAdults,fam.maxChildren,fam.bedType,M.rmsRackOn(t,'FAM',base.date)],['Family room',4,2,'King',350000]);
    assert.equal(mstImportPreview(base,'techadmin',sets).items[0].action,'skipped');});
  await registerUi({test,M,assert});
  test('[MIO] header sync: the server samples parse through the prototype FieldSpecs (columns known, no header problem) for every master',()=>{
    for(const [id,wire] of Object.entries(MST_IO_WIRE)){
      const sp=mstSpec(id),text=readFileSync(`${TPL}/samples/${wire}.csv`,'utf8');
      const res=mstRowsFromCsv(base,sp,text.replace(/,tables\r?\n/,'\n').split(/\r?\n/).map((l,i)=>i===0?l:l).join('\n'));
      assert.deepEqual(res.errors.filter(e=>e.line<=1),[],`${wire} header`);assert.ok(res.rows.length||res.errors.length,wire);}});
}

// ── UI (server-side render of the sheets and the panel) ──
export async function registerUi({test,M,assert}){
  const {build}=await import('esbuild');const {mkdir,writeFile}=await import('node:fs/promises');
  const dir='tests/.generated';await mkdir(dir,{recursive:true});
  await writeFile(dir+'/mio-entry.tsx',`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics,roomStatus} from '../../src/services/mock';
import {MstPasteSheet,MstImportSheet,MstIoPanel} from '../../src/features/masters/MastersIo';
import {MasterList} from '../../src/features/masters/MasterList';
import {MasterListPhone} from '../../src/mobile/masters/MasterListPhone';
import {CfgDataPanel} from '../../src/features/admin/config/panels';
import {registration} from '../../src/features/masters/register';
import {MasterForm} from '../../src/features/masters/MasterForm';
import {MstRetireConfirm} from '../../src/features/masters/RetireConfirm';
import '../../src/features/roommasters/register'; // V14-7: the Rooms tools entry (range add, Renumber, Out of order, no-reorder note)
function ctx(s:any,uid:string,mobile:boolean):any{
  const user=s.users.find((u:any)=>u.id===uid);const noop=()=>{};
  return {s,uid,user,m:metrics(s),page:'settings',go:noop,modal:{type:'x',key:'k'},open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,permission:(p:any)=>can(s,uid,p),act:()=>null,
    search:'',setSearch:noop,filter:'All',setFilter:noop,tab:'Arrivals',setTab:noop,arrivals:[],departures:[],pending:0,taskList:[],roomStatus:(r:any)=>roomStatus(s,r),switchUser:noop,fault:'none',setFault:noop,
    loadScenario:noop,resetScenario:noop,display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop,openSearch:noop,openMenu:noop,signOut:noop};
}
export const html=(s:any,uid:string,what:string,mobile=false)=>renderToStaticMarkup(<HccContext.Provider value={ctx(s,uid,mobile)}>{
  what==='paste'?<MstPasteSheet id="floors"/>:what==='pasteVendors'?<MstPasteSheet id="expenseVendors"/>:what==='import'?<MstImportSheet/>:what==='panel'?<MstIoPanel/>:what==='data'?<CfgDataPanel/>:what==='list'?<MasterList spec="floors"/>:
  what==='listPhone'?<MasterListPhone spec="floors"/>:what==='listRooms'?<MasterList spec="rooms"/>:what==='listRoomsPhone'?<MasterListPhone spec="rooms"/>:
  what==='listTypes'?<MasterList spec="roomTypes"/>:what==='listTypesPhone'?<MasterListPhone spec="roomTypes"/>:what.startsWith('retire:')?<MstRetireConfirm id={what.slice(7)}/>:
  what.startsWith('form:')?<MasterForm specId={what.split(':')[1]} rowId={what.split(':')[2]} sheet={mobile}/>:null}</HccContext.Provider>);
export const reg=registration;
`);
  await build({entryPoints:[dir+'/mio-entry.tsx'],bundle:true,platform:'node',format:'esm',outfile:dir+'/mio-entry.mjs',logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"'},banner:{js:"import {createRequire as __r} from 'node:module';const require=__r(import.meta.url);"}});
  const U=await import(new URL('./.generated/mio-entry.mjs?'+Date.now(),import.meta.url).href);
  const base=M.seed();
  test('[MIO-UI] Data and system shows the masters export / import box with the number of lists; Reception cannot import',()=>{
    const o=U.html(base,'owner','data');assert.match(o,/Masters export and import/);assert.match(o,/Export masters \(28 lists\)/);assert.match(o,/Import masters/);
    const r=U.html(base,'reception','panel');assert.match(r,/You may not change any list/);assert.match(r,/disabled/);
    const t=U.html(base,'techadmin','panel');assert.ok(!/Export masters \(28 lists\)/.test(t),'Tech admin has fewer lists (money lists are the Owner\'s)');assert.match(t,/Export masters \(\d+ lists\)/);});
  test('[MIO-UI] Paste rows sheet: columns of the list, Copy CSV, textarea; Import sheet explains all-or-nothing and the retire rule',()=>{
    const p=U.html(base,'owner','paste');assert.match(p,/Paste rows/);assert.match(p,/code, name, sortOrder, active/);assert.match(p,/Copy this list as CSV/);assert.match(p,/<textarea/);
    const i=U.html(base,'owner','import');assert.match(i,/Import masters/);assert.match(i,/All or nothing/);assert.match(i,/never retires a row/);
    assert.match(U.html(base,'reception','paste'),/masters permission/);});
  test('[MIO-UI] MasterList (desktop and phone) offers Paste rows next to Add for every list incl. room types and rooms (V14-7); Reception sees neither',()=>{ // V14-7 old → new: rooms had no Paste rows
    assert.match(U.html(base,'owner','list'),/Paste rows/);assert.match(U.html(base,'owner','listPhone',true),/Paste rows/);
    for(const w of ['listRooms','listTypes'])assert.match(U.html(base,'owner',w),/Paste rows/,w);
    for(const w of ['listRoomsPhone','listTypesPhone'])assert.match(U.html(base,'owner',w,true),/Paste rows/,w);
    assert.ok(!/Paste rows/.test(U.html(base,'reception','list')));});
  test('[MIO-UI] V14-7 list tools uniform: room types have move arrows; rooms keep range add / Renumber / Out of order and say why they have no arrows (desktop + phone)',()=>{
    assert.match(U.html(base,'owner','listTypes'),/Move Deluxe up/);assert.match(U.html(base,'owner','listTypesPhone',true),/Move Deluxe up/);
    for(const [w,m] of [['listRooms',false],['listRoomsPhone',true]]){const h=U.html(base,'owner',w,m);
      assert.match(h,/Add rooms/,w);assert.match(h,/Renumber a room/,w);assert.match(h,/Set out of order/,w);
      assert.doesNotMatch(h,/Move Room 101 up/,w);assert.match(h,/Rooms are listed by floor and room number, so they have no manual order/,w);}
    assert.doesNotMatch(U.html(base,'owner','listTypes'),/no manual order/);});
  test('[MIO-UI] V14-6 Retire asks first in the app dialog (desktop + phone): "Retire Floor 1?", old records keep the name, Back + Retire; Reactivate runs at once',()=>{
    const src=f=>readFileSync(f,'utf8'),f=src('src/features/masters/MasterForm.tsx');
    assert.doesNotMatch(f,/MST_MASTER_RETIRE/,'the form no longer retires directly');assert.match(f,/open\(\{type:'mst-retire',id:`\$\{specId\}:\$\{row\.id\}\$\{sheet\?':sheet':''\}`\}\)/);
    assert.match(f,/status\('MST_MASTER_REACTIVATE'/,'reactivate needs no confirmation');
    for(const x of [f,src('src/features/masters/RetireConfirm.tsx')])assert.doesNotMatch(x,/window\.confirm|[^.\w]confirm\(/);
    for(const m of [false,true])assert.match(U.html(base,'owner','form:floors:flr-1',m),/Retire…/);
    const d=U.html(base,'owner','retire:floors:flr-1');
    assert.match(d,/Retire Floor 1\?/);assert.match(d,/Old records keep the name/);assert.match(d,/Used in 8/);assert.match(d,/>Back</);assert.match(d,/class="btn danger"/);assert.doesNotMatch(d,/disabled=""/);
    assert.match(U.html(base,'owner','retire:rooms:105:sheet'),/Room 105 leaves the room board, the calendar and New booking/);
    const live=U.html(base,'owner','retire:rooms:101');assert.match(live,/has bookings/);assert.match(live,/disabled=""/,'a room with a live booking cannot be retired');
    for(const where of [U.reg.modals,U.reg.mobile.modals])assert.equal(typeof where['mst-retire'],'function');});
  test('[MIO-UI] both modals are registered for desktop and phone (mst-import, mst-paste)',()=>{
    for(const where of [U.reg.modals,U.reg.mobile.modals]){assert.equal(typeof where['mst-import'],'function');assert.equal(typeof where['mst-paste'],'function');assert.equal(typeof where['mst-row'],'function');}});
}
