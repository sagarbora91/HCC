// F4-T3 qw-audit · audit viewer filters, diff view, CSV, money mask. Names prefixed [AV].
export function register({test,M,assert}){
  const {seed,execute,avFilter,avDiff,avDiffFor,avCsv,avEntity,avEntityId,avEntities}=M;
  const withSetting=()=>{let s=seed();s=execute(s,'owner',{type:'SETTING_SET',setting:'gst.room.lowBps',value:1200,effectiveFrom:s.date,reason:'CA advice'});return s;};
  test('[AV] entity type and id come from action and detail; filters combine',()=>{
    const s=withSetting(),a=s.audit[0];
    assert.equal(avEntity(a),'SETTING');assert.equal(avEntityId(a),'gst.room.lowBps');
    assert.ok(avEntities(s).includes('SETTING'));
    assert.equal(avFilter(s,{entity:'SETTING'}).length,1);
    assert.equal(avFilter(s,{entity:'SETTING',entityId:'gst.room'}).length,1);
    assert.equal(avFilter(s,{entityId:'nothing-like-this'}).length,0);
    assert.equal(avFilter(s,{actor:a.actor,from:a.at.slice(0,10),to:a.at.slice(0,10)}).length,1);
    assert.equal(avFilter(s,{to:'2000-01-01'}).length,0);});
  test('[AV] diff lists changed keys; money is masked for roles without hotel money',()=>{
    const s=withSetting(),a=s.audit[0];
    assert.deepEqual(avDiff(a).map(c=>[c.before,c.after]),[['5%','12%']]);
    const m={detail:'Rate Deluxe · ₹1,500 → ₹1,800 · effective 2026-10-24'};
    assert.equal(avDiffFor(s,'owner',m)[0].after,'₹1,800');
    assert.equal(avDiffFor(s,'housekeeper',m)[0].after,'₹ ••');
    assert.deepEqual(avDiff({detail:'Demo initialized'}),[]);});
  test('[AV] CSV has a header, filtered rows and the formula guard',()=>{
    let s=withSetting();s.audit.push({id:'x1',at:s.clock,actor:'=cmd',action:'USER EDIT',detail:'User u1 · a → b'});
    const csv=avCsv(s,'owner',{entity:'USER'});const lines=csv.split('\r\n');
    assert.equal(lines[0],'Id,At,Actor,Entity,Entity id,Action,Detail');assert.equal(lines.length,2);assert.match(lines[1],/'=cmd/);
    assert.equal(avCsv(s,'owner',{}).split('\r\n').length,s.audit.length+1);});
}
