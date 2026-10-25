// SCHEMA 7 → 8 (Wave 0.5 integration merge): a version-7 save is migrated, never reset. Names prefixed [S8].
export function register({test,M,assert}){
  const v7Save=()=>{const s=structuredClone(M.seed());delete s.complianceX;delete s.cashbookX;delete s.configX;s.schema=7;return JSON.stringify(s);};
  test('[S8] SCHEMA is 8 and a fresh seed carries it',()=>{assert.equal(M.SCHEMA,8);assert.equal(M.seed().schema,8);});
  test('[S8] a v7 save without Wave 0.5 slices restores without reset and is upgraded to 8',()=>{
    const r=M.restoreState(v7Save());assert.equal(r.reset,false);assert.equal(r.state.schema,8);
    assert.equal(r.state.bookings.length,M.seed().bookings.length,'bookings kept');assert.equal(M.drawer(r.state),M.drawer(M.seed()),'drawer unchanged');});
  test('[S8] the v7 upgrade seeds the fictional compliance obligations (F14) and never overwrites existing ones',()=>{
    const r=M.restoreState(v7Save());const x=r.state.complianceX;assert.ok(x&&x.obligations.length>0,'obligations seeded');
    assert.equal(x.obligations.length,M.seed().complianceX.obligations.length);
    const kept=structuredClone(M.seed());kept.complianceX.obligations=kept.complianceX.obligations.slice(0,2);kept.schema=7;
    assert.equal(M.restoreState(JSON.stringify(kept)).state.complianceX.obligations.length,2,'existing slice untouched');});
  test('[S8] saves older than 7 or newer than 8 are reset, not migrated',()=>{
    for(const v of [6,9]){const s=structuredClone(M.seed());s.schema=v;assert.equal(M.restoreState(JSON.stringify(s)).reset,true,'schema '+v);}});
}
