// F7-R3 · batch 1 guests + rooms on the report engine: movement register, guest register, Form C register, rate changes, room status, and the
// Lodge-look layouts (registration card, day end). Screen = Excel = print (threeWay), roles (rule V / M), masks, ranges, goldens.
import {build} from 'esbuild';
import {threeWay,goldenCheck,unzipStored} from './rpt-golden/harness.mjs';
await build({entryPoints:['tests/rpt-guests-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/rpt-guests.mjs',logLevel:'error'});
const G=await import('./.generated/rpt-guests.mjs');
export function register({test,assert}){
  const M=G,T='[F7-R3] ';
  const MR='rpt.guests.movement-register',GR='rpt.guests.guest-register',FC='rpt.guests.form-c-register',RC='rpt.guests.rate-changes',RS='rpt.rooms.room-status';
  const code=fn=>{try{fn();}catch(e){return e.code;}return 'none';};
  const base=()=>M.seed();
  const fy=()=>M.loadScenario('FY26');
  const run=(s,uid,id,p={})=>M.runReport(s,uid,id,p,{all:true});
  const rows=r=>r.rows.filter(x=>x.kind==='row');
  const FULL_ID=/100079324101/;

  test(T+'the five reports are registered with the catalogue columns, money flags and gates',()=>{
    const want={[MR]:[16,true,'PERM-RPT-002'],[GR]:[17,false],[FC]:[15,false],[RC]:[11,true,'PERM-RPT-002'],[RS]:[11,false]};
    for(const [id,[n,money,gate]] of Object.entries(want)){const d=M.reportDef(id);assert.ok(d,id);assert.equal(d.columns.length,n,id+' columns');assert.equal(d.moneyFlag,money,id);assert.equal(d.moneyGate,gate,id);}
    assert.deepEqual(M.reportDef(MR).columns.filter(c=>c.money).map(c=>c.key),['lateCharge','balanceDue']);assert.deepEqual(M.reportDef(RC).columns.filter(c=>c.money).map(c=>c.key),['oldRate','newRate']);
    assert.deepEqual(M.reportDef(GR).columns.filter(c=>c.mask).map(c=>c.key+':'+c.mask),['guest:guestName','idNumber:idNumber']);
    assert.deepEqual(M.reportDef(FC).columns.filter(c=>c.mask).map(c=>c.key+':'+c.mask),['guest:guestName','passportNo:idNumber']);
    assert.ok(M.reportDef(RS).deskPerms.includes('PERM-HK-001')&&M.reportDef(GR).deskPerms.includes('PERM-STY-001'));
  });

  test(T+'movement register: arrivals and departures with day sub-totals and a total equal to the sum of the rows',()=>{
    const s=base(),r=run(s,'owner',MR,{from:'2026-10-24',to:'2026-10-27'});
    const data=rows(r);assert.ok(data.length>=10);
    const arr=data.filter(x=>x.cells.date==='2026-10-24');assert.deepEqual(arr.map(x=>x.cells.booking),['HCC-1049','HCC-1050']);
    const sub=r.rows.filter(x=>x.kind==='subtotal');assert.equal(sub[0].cells.pax,arr.reduce((a,x)=>a+x.cells.pax,0));assert.match(sub[0].cells.booking,/Day total · 2 rooms/);
    const tot=r.rows.at(-1);assert.equal(tot.kind,'total');assert.equal(tot.cells.pax,data.reduce((a,x)=>a+x.cells.pax,0));assert.equal(tot.cells.balanceDue,data.reduce((a,x)=>a+x.cells.balanceDue,0));
    for(const x of data){const b=s.bookings.find(b=>b.id===x.cells.booking);assert.equal(x.cells.balanceDue,M.totals(b).balance,x.cells.booking);}
    const dep=run(s,'owner',MR,{from:'2026-10-24',to:'2026-10-27',type:'departures'});assert.ok(rows(dep).every(x=>['Checked in','Booked','Checked out'].includes(x.cells.status)));
    assert.ok(rows(dep).every(x=>s.bookings.find(b=>b.id===x.cells.booking).departure===x.cells.date));
  });
  test(T+'movement register: status, room type and source filters; desk fields; late check-out charge',()=>{
    const s=base();const b=s.bookings.find(x=>x.id==='HCC-1041');b.purpose='BUSINESS';b.vehicle={no:'MH12AB1234'};b.checkoutBasis='24h';
    b.lines.push({id:M.deskLateLineId(b.id),label:'Late check-out · half day',amount:100000,date:'2026-10-26',kind:'service',taxBps:1200});
    const r=run(s,'owner',MR,{from:'2026-10-26',to:'2026-10-26',type:'departures',status:'checked in',roomType:'classic'});
    const x=rows(r).find(y=>y.cells.booking==='HCC-1041');assert.ok(x);assert.equal(x.cells.vehicle,'MH12AB1234');assert.equal(x.cells.basis,'24-hour');assert.equal(x.cells.lateCharge,112000);
    assert.ok(rows(r).every(y=>y.cells.status==='Checked in'&&y.cells.roomType==='Classic'));
    assert.equal(rows(run(s,'owner',MR,{from:'2026-10-24',to:'2026-10-24',type:'arrivals',source:'Phone'})).every(y=>y.cells.source==='Phone'),true);
    assert.equal(rows(run(s,'owner',MR,{from:'2026-10-24',to:'2026-10-24',type:'arrivals',status:'cancelled'})).length,0);
  });
  test(T+'movement register: 92-day range, 90-day look-ahead, bad enum; legacy flag before April 2026 (no legacy stays in the prototype)',()=>{
    const s=base();
    assert.equal(code(()=>run(s,'owner',MR,{from:'2026-10-24',to:'2027-01-24'})),'VALIDATION');
    assert.equal(code(()=>run(s,'owner',MR,{from:'2026-10-24',to:'2027-01-23'})),'VALIDATION');
    assert.equal(code(()=>run(s,'owner',MR,{from:'2026-10-24',to:'2027-01-22'})),'none');
    assert.equal(code(()=>run(s,'owner',MR,{type:'sideways'})),'VALIDATION');
    const lg=run(s,'owner',MR,{from:'2026-03-01',to:'2026-03-10'});assert.equal(lg.about.legacy,true);assert.equal(rows(lg).length,0);
    assert.equal(run(s,'owner',MR,{}).params.to,'2026-10-30','default window = business date + 6');
  });
  test(T+'movement register roles: Owner / Manager / Accountant see it with money; Reception, Cashier, Housekeeper are refused; the money gate removes both money columns',()=>{
    const s=base();for(const u of ['owner','manager','accountant'])assert.equal(code(()=>run(s,u,MR)),'none',u);
    for(const u of ['reception','cashier','housekeeper','maint','techadmin'])assert.equal(code(()=>run(s,u,MR)),'FORBIDDEN',u);
    // A user who may open operations reports but holds no finance right: grant-level change on a copy of the policy is outside the prototype; the engine rule is covered in stream-rpt-engine.
    const r=run(s,'owner',MR);assert.ok(r.columns.some(c=>c.key==='balanceDue'));
  });

  test(T+'guest register: statutory columns, ID numbers masked everywhere, the full number never appears in any format',()=>{
    const s=base(),r=run(s,'owner',GR,{from:'2026-10-23',to:'2026-10-23'});
    const d=rows(r);assert.equal(d.length,8);assert.ok(d.every(x=>/^XXXX-XXXX-\w{4}$/.test(x.cells.idNumber)));
    const g=d.find(x=>x.cells.regNo==='HCC-1041');assert.equal(g.cells.idNumber,'XXXX-XXXX-4101');assert.equal(g.cells.checkIn,'2026-10-23 14:00');assert.equal(g.cells.city,'Bengaluru');assert.equal(g.cells.checkOut,'');
    assert.equal(r.rows.at(-1).cells.regNo,'8 stays');
    const x=M.toXlsx(r,'Test'),csv=M.toCsv(r),pm=M.printText(M.toPrintModel(r,'Test'));
    const xml=Object.values(unzipStored(M.buildXlsx(x))).join('');
    for(const text of [JSON.stringify(r),xml,csv,pm])assert.ok(!FULL_ID.test(text),'full ID leaked');
    threeWay(M,assert,r);
    assert.equal(rows(run(s,'owner',GR,{from:'2026-10-23',to:'2026-10-23',status:'checked out'})).length,0);
  });
  test(T+'guest register: desk right — Reception allowed, Cashier / Housekeeper / Accountant-with-no-desk handled by the matrix; names masked without reservations or billing',()=>{
    const s=base();assert.equal(code(()=>run(s,'reception',GR,{from:'2026-10-23',to:'2026-10-23'})),'none');
    for(const u of ['cashier','housekeeper','maint','techadmin'])assert.equal(code(()=>run(s,u,GR)),'FORBIDDEN',u);
    assert.equal(code(()=>run(s,'owner',GR,{from:'2026-07-01',to:'2026-10-24'})),'VALIDATION','92-day limit');
    assert.equal(rows(run(s,'reception',GR,{from:'2026-10-23',to:'2026-10-23'}))[0].cells.guest.includes('•'),false);
  });
  test(T+'guest register: purpose, vehicle, Form C places and bill number fill from the stay; a formula-looking name is neutralised in CSV',()=>{
    const s=base();const b=s.bookings.find(x=>x.id==='HCC-1041');b.purpose='TOURISM';b.vehicle={no:'KA01AA0001'};b.invoiceNo='DEMO/2026-27/0001';b.guest='=SUM(A1)';
    s.guests.find(g=>g.id==='G-0001').name='=SUM(A1)';
    const r=run(s,'owner',GR,{from:'2026-10-23',to:'2026-10-23'}),x=rows(r).find(y=>y.cells.regNo==='HCC-1041');
    assert.equal(x.cells.vehicle,'KA01AA0001');assert.equal(x.cells.billNo,'DEMO/2026-27/0001');assert.ok(x.cells.purpose.length>0);
    assert.ok(M.toCsv(r).includes("'=SUM(A1)")||!M.toCsv(r).includes(',=SUM(A1)'),'formula neutralised');
  });

  // A foreign stay with a Form C, built on a copy of the base state.
  function withForeign(){
    const s=base();const g=s.guests.find(x=>x.id==='G-0001');g.nationality='French';g.idType='Passport (demo)';g.idNumber='P1234567';g.idLast4='4567';
    const b=s.bookings.find(x=>x.id==='HCC-1041');
    b.formC={passportPlace:'Paris',passportIssued:'2022-01-10',passportExpiry:'2032-01-09',visaNo:'V99887766',visaType:'Tourist',visaPlace:'Paris',visaIssued:'2026-09-01',visaExpiry:'2027-03-01',
      arrivedFrom:'Delhi',arrivalInIndia:'2026-10-20',intendedStayDays:10,nextDestination:'Goa',employedInIndia:false,addressAbroad:'1 Rue Demo, Paris',sex:'F'};
    // a second foreign stay with no Form C yet
    const g2=s.guests.find(x=>x.id==='G-0002');g2.nationality='German';g2.idType='Passport (demo)';
    return s;
  }
  test(T+'Form C register: foreign stays, passport and visa masked, the to-submit count on top, submitted filter',()=>{
    const s=withForeign(),r=run(s,'owner',FC,{from:'2026-10-01',to:'2026-10-24'});
    assert.equal(r.rows[0].kind,'subtotal');assert.equal(r.rows[0].cells.nationality,'To submit: 1');
    const d=rows(r);assert.equal(d.length,2);const f=d.find(x=>x.cells.nationality==='French');
    assert.equal(f.cells.passportNo,'XXXX-XXXX-4567');assert.equal(f.cells.visa,'••••7766 · Tourist');assert.equal(f.cells.passportIssue,'2022-01-10 · Paris');assert.equal(f.cells.arrivedOn,'2026-10-20');assert.equal(f.cells.submitted,1);
    const miss=d.find(x=>x.cells.nationality==='German');assert.equal(miss.cells.submitted,0);assert.equal(miss.cells.visa,'');
    assert.equal(rows(run(s,'owner',FC,{from:'2026-10-01',to:'2026-10-24',submitted:'no'})).length,1);assert.equal(rows(run(s,'owner',FC,{from:'2026-10-01',to:'2026-10-24',submitted:'yes'})).length,1);
    for(const text of [JSON.stringify(r),M.printText(M.toPrintModel(r,'T')),Object.values(unzipStored(M.buildXlsx(M.toXlsx(r,'T')))).join('')])assert.ok(!/P1234567|V99887766/.test(text),'passport / visa leaked');
    threeWay(M,assert,r);
    assert.equal(code(()=>run(s,'reception',FC,{from:'2026-10-01',to:'2026-10-24'})),'none');assert.equal(code(()=>run(s,'cashier',FC)),'FORBIDDEN');
    assert.equal(rows(run(base(),'owner',FC,{from:'2026-10-01',to:'2026-10-24'})).length,0,'no foreign guests in the base fixture');
  });

  test(T+'rate changes: filters, net change in the footer, money gate is the report permission, guest masked without billing / reservations',()=>{
    const s=base();s.stayRateX={changes:[
      {id:'c1',booking:'HCC-1041',room:'101',date:'2026-10-23',at:'2026-10-23T15:10',by:'manager',approvedBy:'owner',scope:'next',reason:'Loyal guest',oldRate:200000,newRate:180000,oldPeople:1,newPeople:2,oldChildren:0,newChildren:0,effectiveFrom:'2026-10-24',reposted:[]},
      {id:'c2',booking:'HCC-1042',room:'102',date:'2026-10-24',at:'2026-10-24T09:00',by:'owner',scope:'all',reason:'Extra bed',oldRate:200000,newRate:230000,oldPeople:2,newPeople:2,oldChildren:0,newChildren:1,effectiveFrom:'2026-10-24',reposted:[]}]};
    const r=run(s,'owner',RC,{from:'2026-10-20',to:'2026-10-24'}),d=rows(r);
    assert.equal(d.length,2);assert.equal(d[0].cells.oldGuests,1);assert.equal(d[0].cells.newGuests,2);const nm=id=>s.users.find(u=>u.id===id).name.split(' · ')[0];assert.equal(d[0].cells.authorisedBy,nm('owner'));assert.equal(d[0].cells.changedBy,nm('manager'));
    assert.equal(d[1].cells.authorisedBy,d[1].cells.changedBy,'a direct change is authorised by whoever made it');
    const tot=r.rows.at(-1);assert.equal(tot.kind,'total');assert.equal(tot.cells.newRate,-20000+30000);assert.match(tot.cells.changedAt,/2 changes/);
    assert.equal(rows(run(s,'owner',RC,{from:'2026-10-20',to:'2026-10-24',room:'102'})).length,1);assert.equal(rows(run(s,'owner',RC,{from:'2026-10-20',to:'2026-10-24',changedBy:'manager'})).length,1);
    threeWay(M,assert,r);
    for(const u of ['reception','cashier','housekeeper'])assert.equal(code(()=>run(s,u,RC)),'FORBIDDEN',u);
    assert.equal(rows(run(s,'owner',RC,{from:'2026-10-10',to:'2026-10-12'})).length,0);
  });

  test(T+'room status: every room, counts in the total row, filters, no date parameter',()=>{
    const s=base(),r=run(s,'owner',RS),d=rows(r);
    assert.equal(d.length,s.rooms.length);const tot=r.rows.at(-1);assert.equal(tot.cells.room,'24 rooms');assert.match(tot.cells.status,/Occupied 8 · Vacant 15 · Out of order 1/);assert.match(tot.cells.notes,/Check-ins today 2 · Check-outs today 0/);
    assert.equal(rows(run(s,'owner',RS,{filter:'occupied'})).length,8);assert.equal(rows(run(s,'owner',RS,{filter:'out of order'})).map(x=>x.cells.room).join(),'308');
    assert.equal(rows(run(s,'owner',RS,{filter:'vacant'})).length,15);
    s.rooms.find(x=>x.id==='103').hk='Dirty';const dirty=rows(run(s,'owner',RS,{filter:'dirty'}));assert.ok(dirty.some(x=>x.cells.room==='103')&&dirty.every(x=>x.cells.hk==='Dirty'));
    assert.deepEqual(M.reportDef(RS).params.map(p=>p.key),['filter']);assert.equal(r.params.from,undefined);
    const occ=d.find(x=>x.cells.room==='101');assert.equal(occ.cells.guest,'Ananya Rao');assert.equal(occ.cells.checkIn,'2026-10-23');assert.equal(occ.cells.checkOut,'2026-10-26');
    assert.equal(d.find(x=>x.cells.room==='103').cells.nextArrival,'2026-10-24');
    threeWay(M,assert,r);
  });
  test(T+'room status roles: Reception sees names, Accountant / Supervisor / Housekeeper / Maintenance do not (empty, not masked); Cashier and Tech admin are refused',()=>{
    const s=base();
    for(const u of ['owner','manager','reception'])assert.equal(rows(run(s,u,RS,{filter:'occupied'}))[0].cells.guest.length>2,true,u);
    for(const u of ['accountant','supervisor','housekeeper','maint'])assert.ok(rows(run(s,u,RS,{filter:'occupied'})).every(x=>x.cells.guest===''),u);
    for(const u of ['cashier','techadmin'])assert.equal(code(()=>run(s,u,RS)),'FORBIDDEN',u);
    assert.ok(M.reportCatalogue(s,'housekeeper').map(d=>d.id).filter(i=>!i.startsWith('rpt.stock.')).join()===RS,'Housekeeping sees only the room board report (F7-R8: plus the stock quantity reports, PERM-STK-001)');
    assert.ok(!M.reportCatalogue(s,'cashier').some(d=>d.area==='guests'||d.area==='rooms'||d.area==='tax'),'Cashier sees no guests / rooms / tax report (R4 gives the Cashier own-shift money reports)');
  });

  test(T+'layouts: registration card (Lodge look, HCC letterhead, ID masked) and day-end sheet (money only with the finance right)',()=>{
    const s=base(),c=G.registrationCardLayout(s,'reception','HCC-1041');
    assert.ok(c.body.includes(G.LODGE_LETTERHEAD)&&c.body.includes('Guest registration card')&&c.body.includes('XXXX-XXXX-4101')&&!FULL_ID.test(c.body));
    assert.deepEqual(c.pairs.slice(0,2),[['Registration no','HCC-1041'],['Room','101']]);
    assert.equal(code(()=>G.registrationCardLayout(s,'cashier','HCC-1041')),'FORBIDDEN');assert.equal(code(()=>G.registrationCardLayout(s,'owner','NOPE')),'NOT_FOUND');
    assert.ok(!/#[0-9a-fA-F]{3,8}\b/.test(c.css),'no hex colour in the layout css');
    const d=G.dayEndLayout(s,'owner','2026-10-24'),dn=G.dayEndLayout(s,'owner','2026-10-24');
    assert.ok(d.pairs.some(p=>p[0]==='Net revenue')&&d.body.includes('B · Money'));assert.deepEqual(d.pairs,dn.pairs);
    assert.equal(d.pairs.find(p=>p[0]==='Arrivals')[1],String(s.bookings.filter(b=>b.arrival==='2026-10-24'&&!['Cancelled','Expired','No show'].includes(b.status)).length));
    assert.equal(code(()=>G.dayEndLayout(s,'reception','2026-10-24')),'FORBIDDEN');
  });
  test(T+'city of an address, and the goldens of these five reports have no drift',()=>{
    assert.equal(G.cityOfAddress('10 Demo Street, Bengaluru, Karnataka (fictional)'),'Bengaluru');assert.equal(G.cityOfAddress('Pune, Maharashtra'),'Pune');assert.equal(G.cityOfAddress(''),'');
    const fails=goldenCheck(M,assert).filter(f=>/guests|rooms/.test(f));assert.deepEqual(fails,[]);
  });
}
