// Group block rooms are shown blocked and protected in shared availability (owner decision, 24 Oct 2026).
export function register({test,M,assert}){
  const {seed,execute,available,availableRaw,grpHolds,roomStatus,resCalendar}=M;
  test('[GRP-HOLD] unpicked block rooms are held as specific rooms and not bookable by others',()=>{
    const s=seed();const h=grpHolds(s).filter(h=>h.block==='GB-4');
    assert.equal(h.length,3);for(const x of h){assert.equal(availableRaw(s,x.room,x.a,x.d),true);assert.equal(available(s,x.room,x.a,x.d),false);}
    assert.throws(()=>execute(s,'reception',{type:'BOOK',guest:'Walk In',room:h[0].room,arrival:'2026-10-25',departure:'2026-10-26',people:1}),/not available/);
  });
  test('[GRP-HOLD] calendar shows held nights as blocked with the group label; room board shows Blocked on the night',()=>{
    const s=seed();const room=grpHolds(s).find(h=>h.block==='GB-4').room;
    const cal=resCalendar(s,'2026-10-24',7);const row=cal.groups.flatMap(g=>g.rows).find(r=>r.room.id===room);
    assert.equal(row.cells[0].kind,'free');assert.equal(row.cells[1].kind,'block');
    assert.ok(row.bars.some(b=>b.kind==='block'&&/Group hold · GB-4/.test(b.label)));
    const next={...s,date:'2026-10-25',clock:'2026-10-25T08:00'};assert.equal(roomStatus(next,next.rooms.find(r=>r.id===room)),'Blocked');
  });
  test('[GRP-HOLD] the block itself can pick up its held rooms; holds shrink as rooms are picked up',()=>{
    let s=seed();const before=grpHolds(s).filter(h=>h.block==='GB-4').length;
    s=execute(s,'reception',{type:'GRP_ROOMING',block:'GB-4',rows:[{guest:'Group Guest One',type:'Deluxe',people:2}],key:'gh-1'});
    const b=s.bookings.at(-1);assert.equal(b.arrival,'2026-10-25');
    assert.equal(grpHolds(s).filter(h=>h.block==='GB-4').length,before-1);
    assert.ok(!grpHolds(s).some(h=>h.room===b.room&&h.a<b.departure&&h.d>b.arrival),'picked room no longer held');
  });
  test('[GRP-HOLD] releasing a block frees its rooms',()=>{
    let s=seed();const room=grpHolds(s).find(h=>h.block==='GB-2').room;
    s=execute(s,'owner',{type:'GRP_RELEASE',id:'GB-2'});
    assert.ok(!grpHolds(s).some(h=>h.block==='GB-2'));assert.equal(available(s,room,'2026-10-30','2026-11-01'),true);
  });
}
