// LF-DESK · lane lf-desk (LF contract §8): A3 purpose of visit, A4 vehicle, A5 Form C, A6 check-out basis / due-out / late check-out. Names prefixed [LF-DESK].
import {readFileSync,existsSync} from 'node:fs';
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
import {execFileSync} from 'node:child_process';
await writeFile('tests/.generated/lf-desk-entry.ts',"export {customerIndex,customerSearch} from '../../src/domain/streams/customers';");
await build({entryPoints:['tests/.generated/lf-desk-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/lf-desk.mjs',logLevel:'error'});
const X=await import('./.generated/lf-desk.mjs');
export function register({test,M,assert}){
  const {seed,execute,totals,mstRows,mstByCode,cfgDef,cfgValueOn,stayDueOut,stayLateCheckout,deskVehicleNormalise,deskFormCValidate,deskForeignerRegister,deskVisaShown,
    deskVehicleMatches,available,deskEarlyGraceHours,deskHalfDayPct,deskPurposeProblem,stayEarlyCheckinWarning,gstForRoomNight,DESK_FORMC_CSV_COLUMNS,deskForeignerCsvRows}=M;
  const {customerIndex,customerSearch}=X;
  const bk=(s,id)=>s.bookings.find(b=>b.id===id);
  const set=(s,key,value)=>{const c=structuredClone(s);c.configX??={};c.configX.values??={};c.configX.values[key]=value;return c;};
  const at=(s,clock)=>{const c=structuredClone(s);c.clock=clock;return c;};
  const atd=(s,clock)=>{const c=at(s,clock);c.date=clock.slice(0,10);return c;}; // also moves the business date
  const FC={passportPlace:'Berlin',passportIssued:'2022-03-01',passportExpiry:'2032-02-28',visaNo:'V9988776',visaType:'Tourist',visaPlace:'Berlin',visaIssued:'2026-09-01',visaExpiry:'2027-03-01',
    arrivedFrom:'Frankfurt',arrivalInIndia:'2026-10-20',intendedStayDays:10,nextDestination:'Goa',employedInIndia:false,addressAbroad:'1 Example Str, Berlin (fictional)',sex:'F'};
  /** A confirmed, registered booking today in a ready room (Indian guest unless `foreign`). */
  function booked(s=seed(),opts={}){
    s=execute(s,'reception',{type:'BOOK',guest:'LF Desk Guest (fictional)',room:opts.room||'105',arrival:s.date,departure:opts.departure||'2026-10-26',people:1,...(opts.book||{})});
    const id=s.bookings.at(-1).id;
    const guest={name:'LF Desk Guest',phone:'9876500001',email:'',address:'Demo address (fictional)',nationality:opts.foreign?'German':'Indian',idType:opts.foreign?'Passport (demo)':'Aadhaar (demo)',
      idNumber:opts.foreign?'P1234567':'234567890124',preferences:'',notes:'',party:[],...(opts.foreign?{dob:'1990-05-05'}:{})};
    s=execute(s,'reception',{type:'REGISTER',id,guest});
    return [s,id];
  }
  const checkin=(s,id,extra={})=>execute(s,'reception',{type:'CHECKIN',id,...extra});

  // ── A3 purpose of visit ──
  test('[LF-DESK] A3 the Purposes of visit master: the 8 seeded purposes, Owner / Tech admin edit it, Reception cannot, a retired purpose is refused on BOOK',()=>{
    const s=seed();assert.deepEqual(mstRows(s,'purposes').map(r=>r.code),['BUSINESS','TOURISM','FAMILY','MEDICAL','EDUCATION','OFFICIAL','TRANSIT','OTHER']);
    assert.deepEqual(mstRows(s,'vehicleTypes').map(r=>r.name),['Car','Bike','Taxi','Bus','Other']);
    const t=execute(s,'techadmin',{type:'MST_MASTER_SAVE',master:'purposes',row:{code:'WEDDING',name:'Wedding'},key:'k1'});assert.ok(mstByCode(t,'purposes','WEDDING'));
    assert.throws(()=>execute(s,'reception',{type:'MST_MASTER_SAVE',master:'purposes',row:{code:'X',name:'X'}}),e=>e.code==='FORBIDDEN');
    const r=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'purposes',id:mstByCode(s,'purposes','TRANSIT').id});
    assert.throws(()=>execute(r,'reception',{type:'BOOK',guest:'Retired Purpose',room:'105',arrival:r.date,departure:'2026-10-26',people:1,purpose:'TRANSIT'}),/retired/);
    assert.throws(()=>execute(s,'reception',{type:'BOOK',guest:'Unknown Purpose',room:'105',arrival:s.date,departure:'2026-10-26',people:1,purpose:'NOPE'}),/not in Master configuration/);});
  test('[LF-DESK] A3 purpose is optional at BOOK, required at CHECKIN by the desk forms when frontdesk.purposeRequired is on (default on), free when off',()=>{
    assert.equal(cfgValueOn(seed(),'frontdesk.purposeRequired'),true);
    let [s,id]=booked();
    assert.throws(()=>checkin(s,id,{desk:true}),/Choose the purpose of visit/);
    const off=set(s,'frontdesk.purposeRequired',false);assert.equal(bk(checkin(off,id,{desk:true}),id).status,'In house');
    const c=checkin(s,id,{desk:true,purpose:'business'});assert.equal(bk(c,id).purpose,'BUSINESS');
    [s,id]=booked(seed(),{book:{purpose:'MEDICAL'}});assert.equal(bk(s,id).purpose,'MEDICAL');assert.equal(bk(checkin(s,id,{desk:true}),id).purpose,'MEDICAL','the booking purpose satisfies the check-in rule');
    assert.equal(deskPurposeProblem(seed(),'TOURISM'),undefined);});
  // ── A4 vehicle ──
  test('[LF-DESK] A4 vehicle number: upper-cased, 4–13 characters of A–Z 0–9 - space; type from the Vehicle types master; optional',()=>{
    assert.deepEqual(deskVehicleNormalise(' mh24 ab  1234 '),{ok:true,no:'MH24 AB 1234'});
    for(const bad of ['AB','','MH24/AB1234','A'.repeat(14),'MH 24*1'])assert.equal(deskVehicleNormalise(bad).ok,false,bad);
    let [s,id]=booked(seed(),{book:{vehicle:{no:'mh24ab1234',type:'car'}}});assert.deepEqual(bk(s,id).vehicle,{no:'MH24AB1234',type:'Car'});
    assert.throws(()=>booked(seed(),{book:{vehicle:{no:'MH24AB1234',type:'Spaceship'}}}),/Vehicle types/);
    assert.throws(()=>booked(seed(),{book:{vehicle:{no:'!!'}}}),/Vehicle number/);
    [s,id]=booked();s=checkin(s,id,{vehicle:{no:'KA01 XY 9'+'999',type:'Taxi'}});assert.equal(bk(s,id).vehicle.type,'Taxi');
    const b=bk(s,id);assert.ok(deskVehicleMatches(b,'ka01xy9999')&&deskVehicleMatches(b,'KA01-XY'));assert.ok(!deskVehicleMatches(b,'zz'));
    const hits=customerSearch(customerIndex(s),'KA01XY9999');assert.ok(hits.some(c=>c.stays.some(x=>x.id===id)),'Customers search finds the vehicle number');});
  // ── A5 Form C ──
  test('[LF-DESK] A5 Form C: a foreign guest cannot check in without it; passport ID type and date of birth needed; every field rule; Indian guests skip it',()=>{
    let [s,id]=booked(seed(),{foreign:true});
    assert.throws(()=>checkin(s,id,{purpose:'TOURISM'}),/Form C/);
    const bad=(patch,re)=>assert.throws(()=>checkin(s,id,{purpose:'TOURISM',formC:{...FC,...patch}}),re);
    bad({passportExpiry:'2026-10-24'},/passport expires on or before/);bad({visaExpiry:'2026-10-20'},/visa expires on or before/);bad({visaExpiry:'2026-08-01',visaIssued:'2026-09-01'},/visa expiry must be after/);
    bad({intendedStayDays:0},/at least 1 day/);bad({arrivalInIndia:'2026-11-01'},/cannot be after/);bad({visaNo:''},/visa number/);bad({sex:'?'},/sex/);bad({passportIssued:'26/10/2022'},/passport issue date/);
    bad({employedInIndia:true},/employer or business/);bad({addressAbroad:'Passport 234567890124 here'},/ID number|Store ID numbers/);
    const ok=checkin(s,id,{purpose:'TOURISM',formC:FC});assert.equal(bk(ok,id).status,'In house');assert.equal(bk(ok,id).formC.visaNo,'V9988776');
    assert.ok(!JSON.stringify(ok.audit[0]).includes('V9988776'),'the audit never carries the visa number');
    assert.ok(!JSON.stringify(ok.audit[0]).includes('P1234567'),'nor the passport number');
    const off=set(s,'frontdesk.formCRequired',false);assert.equal(bk(checkin(off,id,{purpose:'TOURISM'}),id).status,'In house','setting off');
    const [i,iid]=booked();assert.equal(bk(checkin(i,iid,{purpose:'TOURISM'}),iid).status,'In house');assert.equal(bk(checkin(i,iid,{purpose:'TOURISM'}),iid).formC,undefined);});
  test('[LF-DESK] A5 Form C needs the guest to hold a passport ID type and a date of birth',()=>{
    let [s,id]=booked(seed(),{foreign:true});const g=s.guests.find(x=>x.id===bk(s,id).guestId);
    const noDob=structuredClone(s);delete noDob.guests.find(x=>x.id===g.id).dob;assert.match(deskFormCValidate(noDob,bk(noDob,id),noDob.guests.find(x=>x.id===g.id),FC),/date of birth/);
    const notPass=structuredClone(s);notPass.guests.find(x=>x.id===g.id).idType='Driving licence (demo)';assert.match(deskFormCValidate(notPass,bk(notPass,id),notPass.guests.find(x=>x.id===g.id),FC),/Passport/);
    assert.equal(deskFormCValidate(s,bk(s,id),g,FC),undefined);});
  test('[LF-DESK] A5 STAY_FORM_C saves for an open stay; Indian guests are refused; the visa number is masked for roles without the ID-details right and in the register',()=>{
    let [s,id]=booked(seed(),{foreign:true});s=execute(s,'reception',{type:'STAY_FORM_C',id,formC:FC});assert.equal(bk(s,id).formC.arrivedFrom,'Frankfurt');
    assert.ok(!JSON.stringify(s.audit[0]).includes('V9988776'));
    const [i,iid]=booked();assert.throws(()=>execute(i,'reception',{type:'STAY_FORM_C',id:iid,formC:FC}),/Indian national/);
    assert.equal(deskVisaShown(s,'reception','V9988776'),'V9988776');assert.equal(deskVisaShown(s,'cashier','V9988776'),'V9988776'.length>4?'••••8776':'');
    assert.equal(deskVisaShown(s,'accountant','V9988776'),'••••8776');
    const reg=deskForeignerRegister(s,'reception','2026-10-01','2026-10-31');const row=reg.find(r=>r.booking===id);assert.ok(row);assert.equal(row.formC.visaNo,'V9988776');assert.equal(row.missing,false);
    const csv=deskForeignerCsvRows(reg);assert.equal(csv[0].length,DESK_FORMC_CSV_COLUMNS.length);
    const accountantReg=deskForeignerRegister(s,'accountant','2026-10-01','2026-10-31');assert.equal(accountantReg.find(r=>r.booking===id).formC.visaNo,'••••8776');
    assert.throws(()=>execute(s,'cashier',{type:'STAY_FORM_C',id,formC:FC}),e=>e.code==='FORBIDDEN');});
  // ── A6 check-out basis, due-out, late check-out ──
  test('[LF-DESK] A6 settings: five Front-desk settings with the contract defaults; the half-day percentage is a money setting (Owner only)',()=>{
    const s=seed();const d=k=>cfgDef(s,k);
    assert.equal(cfgValueOn(s,'frontdesk.defaultCheckoutBasis'),'fixed');assert.equal(cfgValueOn(s,'frontdesk.lateCheckoutGraceHours'),1);assert.equal(cfgValueOn(s,'frontdesk.earlyCheckinGraceHours'),1);
    assert.equal(deskHalfDayPct(s),50);assert.equal(d('frontdesk.halfDayPct').money,true);assert.equal(d('frontdesk.purposeRequired').money,false);assert.equal(d('frontdesk.halfDayPct').group,'frontdesk');
    assert.ok(d('frontdesk.halfDayPct').illustrative);});
  test('[LF-DESK] A6 due-out: fixed → departure date at the check-out time; 24h → check-in time + nights × 24 h; old bookings use arrival + check-in time',()=>{
    let [s,id]=booked(seed(),{departure:'2026-10-26'});
    assert.equal(stayDueOut(s,bk(s,id)),'2026-10-26T11:00');
    s=at(s,'2026-10-24T09:30');s=checkin(s,id,{purpose:'TOURISM',checkoutBasis:'24h'});assert.equal(bk(s,id).checkedInAt,'2026-10-24T09:30');assert.equal(stayDueOut(s,bk(s,id)),'2026-10-26T09:30');
    s=execute(s,'reception',{type:'STAY_CHECKOUT_BASIS',id,basis:'fixed',reason:'Guest asked'});assert.equal(stayDueOut(s,bk(s,id)),'2026-10-26T11:00');
    s=execute(s,'reception',{type:'MOVE',id,room:bk(s,id).room,departure:'2026-10-27'});s=execute(s,'manager',{type:'STAY_CHECKOUT_BASIS',id,basis:'24h',reason:'Guest asked'});assert.equal(stayDueOut(s,bk(s,id)),'2026-10-27T09:30');
    const old=structuredClone(s);delete bk(old,id).checkedInAt;assert.equal(stayDueOut(old,bk(old,id)),'2026-10-27T14:00','absent checkedInAt = arrival + check-in time');
    assert.throws(()=>execute(s,'reception',{type:'STAY_CHECKOUT_BASIS',id,basis:'24h',reason:'x'}),/reason|already/);
    assert.throws(()=>execute(s,'housekeeper',{type:'STAY_CHECKOUT_BASIS',id,basis:'fixed',reason:'Not allowed'}),e=>e.code==='FORBIDDEN');
    const def=set(seed(),'frontdesk.defaultCheckoutBasis','24h');const [d2,did]=booked(def);assert.equal(bk(d2,did).checkoutBasis,'24h');});
  test('[LF-DESK] A6 late check-out: refused past due-out + grace; half day = round(rate × 50 %) at the room slab; waived needs approve + reason; full night means extend',()=>{
    let [s,id]=booked(seed(),{room:'105',departure:'2026-10-25'});s=checkin(s,id,{purpose:'TOURISM'});s=execute(s,'owner',{type:'POST_NIGHTS',id});
    const b0=bk(s,id);const pay=x=>execute(x,'cashier',{type:'PAYMENT',id,amount:totals(bk(x,id)).balance,method:'UPI',reference:'DEMO-LF'});
    // before the due-out + grace: free
    const early=at(s,'2026-10-25T11:30');assert.equal(stayLateCheckout(early,bk(early,id)).late,false);
    // past: refused
    const late=at(s,'2026-10-25T13:15');const lc=stayLateCheckout(late,bk(late,id));assert.equal(lc.late,true);assert.equal(lc.hhmm,'2:15');
    assert.equal(lc.message,'Guest is past the due-out time by 2:15 — post the late check-out charge (half day) or extend the stay first.');
    assert.equal(lc.halfDay.amount,Math.round(b0.rate*50/100));assert.equal(lc.halfDay.taxBps,gstForRoomNight(late,'2026-10-25',b0.rate).bps);
    let l2=pay(late);assert.throws(()=>execute(l2,'reception',{type:'CHECKOUT',id}),/past the due-out time by 2:1\d/);
    // STAY_LATE_CHARGE half-day then settle and check out
    let h=execute(late,'reception',{type:'STAY_LATE_CHARGE',id,charge:'half-day'});const line=bk(h,id).lines.find(l=>l.label==='Late check-out · half day');
    assert.ok(line);assert.equal(line.amount,lc.halfDay.amount);assert.equal(line.kind,'room');assert.equal(line.taxBps,lc.halfDay.taxBps);
    assert.throws(()=>execute(h,'reception',{type:'STAY_LATE_CHARGE',id,charge:'half-day'}),/already decided/);
    h=pay(h);h=execute(h,'reception',{type:'CHECKOUT',id});assert.equal(bk(h,id).status,'Departed');
    // CHECKOUT with lateCharge in one go when the folio is already covered
    let c=pay(late);c=execute(c,'cashier',{type:'PAYMENT',id,amount:lc.halfDay.amount+Math.round(lc.halfDay.amount*lc.halfDay.taxBps/10000),method:'UPI',reference:'DEMO-LF2'});
    c=execute(c,'reception',{type:'CHECKOUT',id,lateCharge:'half-day'});assert.equal(bk(c,id).status,'Departed');assert.ok(bk(c,id).lines.some(l=>l.label==='Late check-out · half day'));
    // waived: approve permission + reason
    assert.throws(()=>execute(pay(late),'reception',{type:'CHECKOUT',id,lateCharge:'waived',lateReason:'Goodwill'}),e=>e.code==='FORBIDDEN');
    assert.throws(()=>execute(pay(late),'manager',{type:'CHECKOUT',id,lateCharge:'waived'}),/reason/);
    const w=execute(pay(late),'manager',{type:'CHECKOUT',id,lateCharge:'waived',lateReason:'Goodwill — flight delay'});assert.equal(bk(w,id).status,'Departed');
    assert.ok(w.audit[0].detail.includes('waived'));
    assert.throws(()=>execute(pay(late),'reception',{type:'CHECKOUT',id,lateCharge:'full-night'}),/extension/);
    // the grace setting
    const g3=set(late,'frontdesk.lateCheckoutGraceHours',3);assert.equal(stayLateCheckout(g3,bk(g3,id)).late,false);
    // half-day setting 0 → refused amount
    const pct=set(late,'frontdesk.halfDayPct',0);assert.throws(()=>execute(pct,'reception',{type:'STAY_LATE_CHARGE',id,charge:'half-day'}),/₹0/);});
  test('[LF-DESK] A6 a 24h stay frees the room after check-out (no hold for the departure night): checked out and cleaned, the room is bookable and checks in the same day',()=>{
    let [s,id]=booked(seed(),{room:'106',departure:'2026-10-25'});s=at(s,'2026-10-24T09:00');s=checkin(s,id,{purpose:'TOURISM',checkoutBasis:'24h'});s=execute(s,'owner',{type:'POST_NIGHTS',id});
    assert.equal(stayDueOut(s,bk(s,id)),'2026-10-25T09:00');
    // in house: the room is occupied tonight, free again from the departure date
    assert.equal(available(s,'106','2026-10-24','2026-10-25'),false);assert.equal(available(s,'106','2026-10-25','2026-10-26'),true,'the departure night is not held');
    s=at(s,'2026-10-25T08:30');s.rooms.find(r=>r.id==='106').hk=s.rooms.find(r=>r.id==='106').hk;s=execute(s,'cashier',{type:'PAYMENT',id,amount:totals(bk(s,id)).balance,method:'UPI',reference:'DEMO-24H'});
    // a new guest arrives that day and books the same room
    s=execute(s,'reception',{type:'BOOK',guest:'Same Day Guest (fictional)',room:'106',arrival:'2026-10-25',departure:'2026-10-26',people:1});const next=s.bookings.at(-1).id;
    s=execute(s,'reception',{type:'CHECKOUT',id});assert.equal(bk(s,id).status,'Departed');assert.equal(s.rooms.find(r=>r.id==='106').hk,'Dirty');
    s=execute(s,'reception',{type:'REGISTER',id:next});s=atd(s,'2026-10-25T10:00');
    try{execute(s,'reception',{type:'CHECKIN',id:next,purpose:'TOURISM'});assert.fail('no throw');}catch(e){assert.match(e.message,/vacant, ready/,'assigned only once cleaned: '+e.message);}
    s.rooms.find(r=>r.id==='106').hk='Ready';
    // the business date has not advanced in this fixture: arrival is today for the second guest
    assert.equal(bk(execute(s,'reception',{type:'CHECKIN',id:next,purpose:'TOURISM'}),next).status,'In house');});
  test('[LF-DESK] A6 early check-in shows a warning (no charge) on a fixed stay before the check-in time minus the grace',()=>{
    const [s,id]=booked();assert.equal(deskEarlyGraceHours(s),1);
    assert.match(stayEarlyCheckinWarning(at(s,'2026-10-24T09:00'),bk(s,id)),/No charge/);assert.equal(stayEarlyCheckinWarning(at(s,'2026-10-24T13:30'),bk(s,id)),undefined);
    const [t,tid]=booked(set(seed(),'frontdesk.defaultCheckoutBasis','24h'));assert.equal(stayEarlyCheckinWarning(at(t,'2026-10-24T09:00'),bk(t,tid)),undefined,'only fixed stays warn');});
  test('[LF-DESK] sources: registration card block, Form C print, foreigner register tab, In-house due-out, room tile due-out, import templates and extractor switches exist',()=>{
    const f=p=>readFileSync(p,'utf8');
    assert.match(f('src/features/guests/GuestProfile.tsx'),/Purpose of visit/);assert.match(f('src/features/guests/FormCPrint.tsx'),/Foreigner register/);
    assert.match(f('src/features/customers/Customers.tsx'),/foreigner-register/);assert.match(f('src/features/guests/InHouse.tsx'),/stayDueOut/);assert.match(f('src/features/rooms/RoomCard.tsx'),/stayDueOut/);
    for(const t of ['purposes','vehicle-types']){assert.ok(existsSync(`../08 Production/deployment/import-templates/templates/${t}.csv`));assert.ok(existsSync(`../08 Production/deployment/import-templates/samples/${t}.csv`));}
    assert.match(f('../tools/legacy/extract.py'),/--purposes/);assert.match(f('../tools/legacy/extract.py'),/--vehicle-types/);});
  test('[LF-DESK] extractor: --purposes / --vehicle-types write the import-template layout from names only (fictional fixture, no guest data)',()=>{
    let py;for(const c of ['python3','python']){try{execFileSync(c,['--version']);py=c;break;}catch{/* next */}}
    if(!py)return; // no Python on this machine: the hotel PC has it; the sources test above still covers the switches
    const out=execFileSync(py,['-I','../tools/legacy/extract.py','--purposes','--vehicle-types','--raw-dir','../tools/legacy/selftest-lf','--out',process.env.TMPDIR?process.env.TMPDIR+'/lf-extract-out':'/tmp/lf-extract-out'],{encoding:'utf8'});
    assert.match(out,/purposes\.csv: 4 rows/);assert.match(out,/vehicle-types\.csv: 5 rows/);
    const dir=process.env.TMPDIR?process.env.TMPDIR+'/lf-extract-out':'/tmp/lf-extract-out';
    assert.equal(readFileSync(dir+'/purposes.csv','utf8').split('\n')[0],'code,name,sortOrder,active');
    assert.match(readFileSync(dir+'/vehicle-types.csv','utf8'),/TEMPO-TRAVELLER,Tempo traveller,50,yes/);});
}
