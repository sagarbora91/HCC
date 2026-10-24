// MB-05 front desk phone screens: pure helpers (agenda grouping, booking-wizard validation and BOOK command, attention list,
// room list, check-in step state) and the AC-M05 journey (reception books → registers → checks in using the wizard's command).
import {build} from 'esbuild';
import {writeFile} from 'node:fs/promises';
export async function register({test,M,assert}){
  await writeFile('tests/.generated/mf-entry.ts',"export * from '../../src/mobile/frontdesk/logic';");
  await build({entryPoints:['tests/.generated/mf-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/mf.mjs',logLevel:'error'});
  const X=await import('./.generated/mf.mjs');
  const can=(s,uid)=>p=>M.can(s,uid,p);

  test('[MF] Initials and room types',()=>{
    assert.equal(X.mfInitials('Ananya Rao'),'AR');assert.equal(X.mfInitials('Demo Guest (fictional)'),'DG');assert.equal(X.mfInitials('  '),'?');
    assert.deepEqual(X.mfRoomTypes(M.seed()),['Classic','Deluxe','Suite']);
    assert.deepEqual(X.mfWeek('2026-10-30',3),['2026-10-30','2026-10-31','2026-11-01']);
  });
  test('[MF] Agenda day groups rooms of one type with the occupying booking, free and blocked nights',()=>{
    const s=M.seed(),rows=X.mfAgendaDay(s,s.date,'Classic');
    assert.ok(rows.every(r=>r.room.type==='Classic'));assert.equal(rows.length,s.rooms.filter(r=>r.type==='Classic').length);
    assert.equal(rows.find(r=>r.room.id==='101').kind,'stay');assert.equal(rows.find(r=>r.room.id==='101').booking.id,'HCC-1041');assert.equal(rows.find(r=>r.room.id==='101').arrives,false);
    assert.equal(rows.find(r=>r.room.id==='308').kind,'block');assert.equal(rows.find(r=>r.room.id==='105').kind,'free');
    const deluxe=X.mfAgendaDay(s,s.date,'Deluxe');const r103=deluxe.find(r=>r.room.id==='103');assert.equal(r103.kind,'stay');assert.equal(r103.arrives,true);assert.equal(r103.status,'Confirmed');
    // Departure day is not a stay night (nights are [arrival, departure)).
    assert.equal(X.mfAgendaDay(s,'2026-10-26','Classic').find(r=>r.room.id==='101').kind,'free');
  });
  test('[MF] Agenda strip counts per night match the day rows',()=>{
    const s=M.seed(),strip=X.mfAgendaStrip(s,s.date,'Classic');assert.equal(strip.length,7);
    for(const d of strip){const rows=X.mfAgendaDay(s,d.date,'Classic');assert.equal(d.free+d.booked+d.blocked,rows.length);}
    assert.equal(strip[0].booked,s.bookings.filter(b=>b.status==='In house'&&s.rooms.find(r=>r.id===b.room).type==='Classic').length);
  });
  test('[MF] Wizard step 1 validates dates and party like the desktop dialog',()=>{
    const s=M.seed(),d=X.mfDraft(s);assert.equal(d.arrival,s.date);assert.equal(d.departure,'2026-10-26');
    assert.deepEqual(X.mfWizardErrors(s,0,d),{});
    assert.match(X.mfWizardErrors(s,0,{...d,arrival:'2026-10-20'}).dates,/before the current business date/);
    assert.match(X.mfWizardErrors(s,0,{...d,departure:d.arrival}).dates,/Departure must follow/);
    assert.match(X.mfWizardErrors(s,0,{...d,departure:'2026-12-31'}).dates,/30 nights/);
    assert.match(X.mfWizardErrors(s,0,{...d,type:'Classic',people:2,children:1}).party,/sleeps 2/);
    const pre=X.mfDraft(s,'105','2026-10-28');assert.equal(pre.room,'105');assert.equal(pre.type,'Classic');assert.equal(pre.arrival,'2026-10-28');assert.equal(pre.departure,'2026-10-29');
    assert.equal(X.mfDraft(s,undefined,'2026-01-01').arrival,s.date); // past prefill falls back to today
  });
  test('[MF] Wizard step 2: auto-assign, picked room, excluded room with a reason',()=>{
    const s=M.seed(),d={...X.mfDraft(s),type:'Classic'};
    const p=X.mfPick(s,d);assert.ok(p.chosen);assert.equal(p.chosen.id,p.available[0].room.id);assert.equal(p.auto,true);
    assert.deepEqual(X.mfWizardErrors(s,1,d),{});
    const bad={...d,room:'101'};assert.equal(X.mfPick(s,bad).chosen,undefined);assert.match(X.mfWizardErrors(s,1,bad).room,/Room 101 is not available: Occupied/);
    assert.ok(X.mfPick(s,d).excluded.some(x=>x.room.id==='308'&&/Blocked/.test(x.reason)));
  });
  test('[MF] Wizard step 3 guest validation (phone, OTA, GSTIN)',()=>{
    const s=M.seed(),d=X.mfDraft(s);const e=X.mfWizardErrors(s,2,d);assert.ok(e.guest&&e.phone);
    const ok={...d,guest:'Demo Guest',phone:'+91 90000 11111'};assert.deepEqual(X.mfWizardErrors(s,2,ok),{});
    assert.ok(X.mfWizardErrors(s,2,{...ok,source:'OTA'}).otaChannel);
    const g=X.mfWizardErrors(s,2,{...ok,gstin:'BAD'});assert.ok(g.gstin&&g.company);
    assert.deepEqual(X.mfWizardErrors(s,2,{...ok,company:'Demo Industries',gstin:'27aaaaa0000a1z5'}),{});
  });
  test('[MF] Wizard BOOK command creates the reservation (confirm and hold) and is idempotent',()=>{
    const s=M.seed(),d={...X.mfDraft(s,'105'),guest:'Wizard Guest',phone:'+91 90000 22222',company:'Demo Industries',gstin:'27AAAAA0000A1Z5'};
    assert.deepEqual(X.mfWizardErrors(s,3,d),{});
    const cmd=X.mfBookCommand(s,d,false,'mf-test-1');assert.equal(cmd.room,'105');
    const next=M.execute(s,'reception',cmd),b=next.bookings.at(-1);
    assert.equal(b.id,'HCC-'+(s.sequence+1));assert.equal(b.status,'Confirmed');assert.equal(b.room,'105');assert.equal(b.phone,'+91 90000 22222');assert.equal(b.company.gstin,'27AAAAA0000A1Z5');
    assert.deepEqual(M.execute(next,'reception',cmd),next);
    const held=M.execute(s,'reception',X.mfBookCommand(s,{...d,room:'',type:'Suite'},true,'mf-test-2'));assert.equal(held.bookings.at(-1).status,'Held');
    // Room taken meanwhile → review step reports it; BOOK is rejected without changing state.
    assert.match(X.mfWizardErrors(next,3,d).room,/not available/);
    assert.throws(()=>M.execute(next,'reception',X.mfBookCommand(s,d,false,'mf-test-3')),/not available/);
    assert.throws(()=>M.execute(s,'housekeeper',cmd),/Access denied/);
  });
  test('[MF] Reservation filter by status and search',()=>{
    const s=M.seed();assert.equal(X.mfFilterBookings(s,'','All').length,s.bookings.length);
    assert.equal(X.mfFilterBookings(s,'','Confirmed').length,2);assert.deepEqual(X.mfFilterBookings(s,'ishaan','All').map(b=>b.id),['HCC-1049']);
    assert.deepEqual(X.mfFilterBookings(s,'207','Confirmed').map(b=>b.id),['HCC-1050']);
  });
  test('[MF] Needs attention only lists permitted pages with counts',()=>{
    const s=M.seed();const pages=['rooms','arrivals','reservations','inhouse','housekeeping','expenses','approvals'];
    const all=X.mfAttention(s,can(s,'owner'),pages);assert.ok(all.every(a=>a.count>0));
    assert.equal(all.find(a=>a.id==='unregistered').count,2);assert.equal(all.find(a=>a.id==='blocked').count,1);assert.equal(all.find(a=>a.id==='blocked').filter,'Blocked');
    const recep=X.mfAttention(s,can(s,'reception'),['rooms','arrivals','reservations','inhouse','expenses']);assert.ok(!recep.some(a=>a.page==='housekeeping'||a.page==='approvals'));
  });
  test('[MF] Room list by floor and status; arrival lookup for the room sheet',()=>{
    const s=M.seed(),st=r=>M.roomStatus(s,r);
    assert.equal(X.mfRoomList(s,1,'All',st).length,8);assert.deepEqual(X.mfRoomList(s,0,'Blocked',st).map(r=>r.id),['308']);
    assert.equal(X.mfRoomList(s,0,'Occupied',st).length,M.metrics(s).occupied);
    assert.equal(X.mfArrivalFor(s,'103').id,'HCC-1049');assert.equal(X.mfArrivalFor(s,'105'),undefined);assert.equal(X.mfNextFor(s,'207').id,'HCC-1050');
  });
  test('[MF] AC-M05: wizard booking → register → check-in step state on the phone flow',()=>{
    let s=M.seed();const d={...X.mfDraft(s,'105'),guest:'Phone Journey',phone:'+91 90000 33333'};
    s=M.execute(s,'reception',X.mfBookCommand(s,d,false,'mf-ac-book'));const id=s.bookings.at(-1).id;
    let st=X.mfCheckinState(s,s.bookings.at(-1),false);assert.equal(st.start,2);assert.equal(st.reachable,2);assert.deepEqual(st.missing,['Verify guest','Tariff']);
    s=M.execute(s,'reception',{type:'REGISTER',id,guest:{name:'Phone Journey',phone:'+91 90000 33333',email:'',address:'1 Demo Road, Pune',nationality:'Indian',idType:'Passport (demo)',idLast4:'4K21',party:[]},key:'mf-ac-reg'});
    st=X.mfCheckinState(s,s.bookings.find(b=>b.id===id),false);assert.equal(st.start,4);assert.equal(st.reachable,4);assert.equal(st.elig.ok,true);
    st=X.mfCheckinState(s,s.bookings.find(b=>b.id===id),true);assert.deepEqual(st.missing,[]);assert.equal(st.reachable,6);
    s=M.execute(s,'reception',{type:'CHECKIN',id,key:'checkin:'+id});assert.equal(s.bookings.find(b=>b.id===id).status,'In house');
    assert.equal(X.mfCheckinState(s,s.bookings.find(b=>b.id===id),false).start,6);
    // Dirty room: eligibility fails at step 3 (check-in stepper shows alternatives).
    let t=M.seed();t.rooms.find(r=>r.id==='103').hk='Dirty';const b=t.bookings.find(b=>b.id==='HCC-1049');
    t=M.execute(t,'reception',{type:'REGISTER',id:b.id,key:'mf-reg-dirty'});const st2=X.mfCheckinState(t,t.bookings.find(x=>x.id===b.id),true);
    assert.equal(st2.elig.ok,false);assert.equal(st2.start,3);assert.ok(st2.missing.includes('Room'));
  });
}
