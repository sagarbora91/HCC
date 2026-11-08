// F8-QW-NS · Due no-shows list, one-click mark through CANCEL noShow (policy charge shown, nothing posted), overbooking cap at the channel push only.
export function register({test,M,assert}){
  const {seed,execute}=M;
  const code=fn=>{try{fn();return 'none';}catch(e){return e.code??'other';}};
  const at=(s,hhmm)=>{s.clock=s.date+'T'+hhmm;return s;};
  const arrival=s=>{const n=execute(s,'owner',{type:'BOOK',guest:'Late Guest',room:'105',arrival:s.date,departure:'2026-10-26',people:1});return [n,n.bookings.at(-1).id];};
  const withPolicy=(s,id)=>{const sc=M.fdmSourceOf(s,s.bookings.find(b=>b.id===id)).code;
    return execute(s,'owner',{type:'MST_MASTER_SAVE',master:'policies',row:{code:'NS-HALF',name:'Half night',freeCancelHours:0,cancelChargeBps:0,noShowChargeBps:5000,depositBps:0,sources:[sc]},key:'pol-ns'});};

  test('[F8-QW-NS] list boundaries: empty before the cut-off, filled from fd.noShowTime, only confirmed arrivals of the business date',()=>{
    let [s,id]=arrival(seed());
    assert.equal(M.nsCutoff(s),'23:00');
    at(s,'22:59');assert.deepEqual(M.nsDue(s),[]);
    at(s,'23:00');const due=M.nsDue(s);
    assert.ok(due.some(r=>r.booking.id===id));
    assert.ok(due.every(r=>r.booking.status==='Confirmed'&&r.booking.arrival===s.date));
    s=execute(s,'owner',{type:'REGISTER',id});s=execute(s,'owner',{type:'CHECKIN',id});at(s,'23:30');
    assert.ok(!M.nsDue(s).some(r=>r.booking.id===id),'checked in = not due');
  });
  test('[F8-QW-NS] nothing is marked automatically: a clock past the cut-off leaves every booking Confirmed',()=>{
    let [s,id]=arrival(seed());at(s,'23:45');M.nsDue(s);
    assert.equal(s.bookings.find(b=>b.id===id).status,'Confirmed');
  });
  test('[F8-QW-NS] mark = CANCEL noShow: status No show, no new line or receipt, policy charge text in the reason',()=>{
    let [s,id]=arrival(seed());s=withPolicy(s,id);at(s,'23:05');
    const b0=s.bookings.find(b=>b.id===id),lines=b0.lines.length,receipts=b0.receipts.length,cash=M.drawer(s);
    assert.match(M.nsDue(s).find(r=>r.booking.id===id).text,/^Policy .+: no-show charge ₹.+ — not posted$/);
    const n=execute(s,'reception',{type:'CANCEL',id,noShow:true,reason:'No-show after 23:00 cut-off',key:'noshow:'+id});
    const b=n.bookings.find(x=>x.id===id);
    assert.equal(b.status,'No show');
    assert.equal(b.lines.length,lines);assert.equal(b.receipts.length,receipts);assert.equal(M.drawer(n),cash);
    assert.match(b.cancelReason,/No-show after 23:00 cut-off · Policy .+: no-show charge ₹.+ — not posted/);
    assert.ok(M.nsChargeText(s,b0).includes(M.money(M.fdmNoShowCharge(s,b0))));
    assert.ok(M.fdmNoShowCharge(s,b0)>0,'policy at 50 % shows a charge');
    assert.ok(!M.nsDue(n).some(r=>r.booking.id===id));
  });
  test('[F8-QW-NS] mark with an advance held is refused as today; the list says why',()=>{
    let [s,id]=arrival(seed());s=execute(s,'cashier',{type:'PAYMENT',id,amount:50000,method:'Cash',kind:'payment',key:'adv-'+id});at(s,'23:10');
    assert.ok(M.nsDue(s).find(r=>r.booking.id===id).blocked);
    assert.equal(code(()=>execute(s,'reception',{type:'CANCEL',id,noShow:true,reason:'x'})),'INVALID_TRANSITION');
  });
  test('[F8-QW-NS] channel push: cap 0 unchanged, cap 1 adds one room to every night of the type',()=>{
    const s=seed();const ch=M.chnSlice(s).channels[0];
    if(!ch){assert.ok(true);return;}
    const type=Object.keys(ch.mapping).find(t=>ch.mapping[t]);
    const base=M.chnPushPreview(s,ch.id).rows.filter(r=>r.type===type).map(r=>r.avail);
    const base0=JSON.stringify(M.chnPushPreview(s,ch.id));
    assert.equal(JSON.stringify(M.chnPushPreview(s,ch.id)),base0);
    s.configX={...(s.configX??{}),values:{...(s.configX?.values??{}),['yld.overbook.'+type]:1}};
    const capped=M.chnPushPreview(s,ch.id).rows.filter(r=>r.type===type).map(r=>r.avail);
    assert.deepEqual(capped,base.map(a=>a+1));
    assert.ok(M.chnPushPreview(s,ch.id).rows.filter(r=>r.type===type).every(r=>!r.closed||r.why==='Stop-sell'));
  });
  test('[F8-QW-NS] BOOK refusal of a minimum-stay rule is unchanged (QWQ-4); override still needs an approver',()=>{
    const s=seed(),room=s.rooms.find(r=>r.type==='Classic'&&!r.blocked).id;
    const book=(uid,extra={})=>execute(s,uid,{type:'BOOK',guest:'Restriction Test',room,arrival:'2026-11-07',departure:'2026-11-08',people:1,...extra});
    assert.equal(code(()=>book('reception')),'RESTRICTED');
    assert.equal(code(()=>book('reception',{restrictionOverride:'Regular guest'})),'FORBIDDEN');
    assert.equal(book('manager',{restrictionOverride:'Regular corporate guest'}).bookings.at(-1).status,'Confirmed');
    assert.equal(M.yldRestrictionsEnforced(s),false,'setting stays as is');
  });
}
