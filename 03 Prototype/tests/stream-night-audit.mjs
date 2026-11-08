// F8-QW-NA · night-audit lines: every line at its boundary, nothing new blocks, MEAS items switched off are skipped, HOVT-NA-PRE auto-ticks.
export function register({test,M,assert}){
  const {seed,execute,naChecklist,naAutoTicks,naTicksToSend,closingPreflight,measNightAuditLines}=M;
  const line=(s,k)=>naChecklist(s).find(l=>l.key===k);
  const blockIds=s=>closingPreflight(s).filter(p=>p.items.length).map(p=>p.id);

  test('[F8-QW-NA] arrivals line: a Confirmed arrival today is not ok, resolved when cancelled',()=>{
    let s=seed();const b=s.bookings.find(x=>x.status==='Confirmed'&&x.arrival<=s.date);
    if(b){assert.equal(line(s,'na.arrivals').ok,false);assert.equal(line(s,'na.arrivals').blocking,true);for(const x of s.bookings.filter(x=>x.status==='Confirmed'&&x.arrival<=s.date))x.status='Cancelled';}
    else assert.equal(line(s,'na.arrivals').ok,true);
    assert.equal(line(s,'na.arrivals').ok,true);});
  test('[F8-QW-NA] departures line: overdue In-house departure is not ok, ok after Departed',()=>{
    const s=seed();assert.equal(line(s,'na.departures').ok,true);
    const b=s.bookings.find(x=>x.status==='In house');b.departure=s.date;
    assert.equal(line(s,'na.departures').ok,false);b.status='Departed';assert.equal(line(s,'na.departures').ok,true);});
  test('[F8-QW-NA] room nights line follows the preflight nights check',()=>{
    const s=seed();const n=closingPreflight(s).find(p=>p.id==='nights').items.length;
    assert.equal(line(s,'na.nights').ok,n===0);
    const t=structuredClone(s);for(const b of t.bookings.filter(x=>x.status==='In house'&&x.arrival<=t.date&&x.departure>t.date))b.lines=b.lines.filter(l=>!(l.kind==='room'&&l.date===t.date));
    const m=closingPreflight(t).find(p=>p.id==='nights').items.length;assert.equal(line(t,'na.nights').ok,m===0);
    const done=execute(t,'manager',{type:'NIGHT_POST'});assert.equal(line(done,'na.nights').ok,true);});
  test('[F8-QW-NA] POS line mirrors the open restaurant checks',()=>{
    const s=seed();const n=closingPreflight(s).find(p=>p.id==='pos').items.length;
    assert.equal(line(s,'na.pos').ok,n===0);assert.equal(line(s,'na.pos').blocking,true);});
  test('[F8-QW-NA] cash line: open drawer is not ok, counted is ok',()=>{
    let s=seed();assert.equal(line(s,'na.cash').ok,false);
    s=execute(s,'owner',{type:'SHIFT_CLOSE',counted:M.drawer(s)});assert.equal(line(s,'na.cash').ok,true);});
  test('[F8-QW-NA] key cards line: an active card on a departed room is flagged; no slice reads ok',()=>{
    const s=seed();assert.equal(line(s,'na.keys').ok,true);
    const b=s.bookings.find(x=>x.status==='In house');b.status='Departed';b.departure=s.date;
    s.keysX={...(s.keysX||{}),cards:[{id:'K1',job:'J1',event:'issued',booking:b.id,room:b.room,from:s.date,to:s.date,by:'reception',at:s.clock,status:'active'}]};
    assert.equal(line(s,'na.keys').ok,false);assert.equal(line(s,'na.keys').blocking,false);
    s.keysX.cards[0].status='cancelled';assert.equal(line(s,'na.keys').ok,true);});
  test('[F8-QW-NA] backup line: boundary is a good backup on the business date',()=>{
    const s=seed();const d=s.date;
    s.sysHealthX={source:'demo',parked:[],lastOkBackupAt:d+'T02:00'};assert.equal(line(s,'na.backup').ok,true);
    s.sysHealthX={source:'demo',parked:[],lastOkBackupAt:'2026-10-23T23:59'};assert.equal(line(s,'na.backup').ok,false);
    s.sysHealthX={source:'demo',parked:[]};assert.equal(line(s,'na.backup').ok,false);assert.equal(line(s,'na.backup').blocking,false);});
  test('[F8-QW-NA] nothing new blocks: only existing preflight ids carry blocking, blockers unchanged',()=>{
    const s=seed();const before=JSON.stringify(blockIds(s));
    naChecklist(s);assert.equal(JSON.stringify(blockIds(s)),before);
    const ids=new Set(closingPreflight(s).map(p=>p.id));
    for(const l of naChecklist(s).filter(l=>l.blocking))assert.ok(ids.has(l.key.replace('na.',''))||['na.approvals','na.cash'].includes(l.key),l.key);
    for(const l of naChecklist(s).filter(l=>l.group==='measurement'))assert.equal(l.blocking,false);
    // with blockClose on, the lines still do not block the existing close
    const t=structuredClone(s);t.configX={...(t.configX||{}),values:{...(t.configX?.values||{}),'meas.daily.blockClose':true}};
    for(const l of naChecklist(t).filter(l=>l.group==='measurement'))assert.equal(l.blocking,false);});
  test('[F8-QW-NA] measurement group equals measNightAuditLines; a switched-off item is skipped',()=>{
    const s=seed();const m=naChecklist(s).filter(l=>l.group==='measurement');
    assert.deepEqual(m.map(l=>l.key),measNightAuditLines(s).map(l=>l.key));
    if(!m.length)return;
    const k=m[0].key;const t=structuredClone(s);t.configX={...(t.configX||{}),values:{...(t.configX?.values||{}),[k]:false}};
    const after=naChecklist(t).filter(l=>l.group==='measurement').map(l=>l.key);
    assert.equal(after.includes(k),measNightAuditLines(t).some(l=>l.key===k));});
  test('[F8-QW-NA] HOVT-NA-PRE auto-ticks: na1-na5 follow the lines, na6 stays manual; tick through HOV_ITEM with note auto',()=>{
    let s=seed();const a=naAutoTicks(s);assert.deepEqual(a.map(x=>x.item),['na1','na2','na3','na4','na5']);
    assert.equal(a.find(x=>x.item==='na4').ok,false);
    s=execute(s,'owner',{type:'SHIFT_CLOSE',counted:M.drawer(s)});
    assert.equal(naAutoTicks(s).find(x=>x.item==='na4').ok,true);
    const todo=naTicksToSend(s);assert.ok(!todo.includes('na6'));assert.ok(todo.includes('na4'));
    for(const item of todo)s=execute(s,'manager',{type:'HOV_ITEM',template:'HOVT-NA-PRE',item,done:true,note:'auto'});
    const r=M.hovRunFor(s,'HOVT-NA-PRE');const i=r.items.find(x=>x.id==='na4');assert.equal(i.done,true);assert.equal(i.note,'auto');
    assert.ok(!naTicksToSend(s).includes('na4'));
    assert.equal(r.items.find(x=>x.id==='na6').done,false);});
  test('[F8-QW-NA] BASE fixture templates are not edited (still 6 items in HOVT-NA-PRE)',()=>{
    const t=M.hovSlice(seed()).templates.find(x=>x.id==='HOVT-NA-PRE');assert.equal(t.items.length,6);});
}
