// SCHEMA 7 → 8 (Wave 0.5 integration merge): a version-7 save is migrated, never reset. Names prefixed [S8].
// Masters wave: SCHEMA 8 → 9; SCC port wave: 9 → 10. v7, v8 and v9 saves all migrate to the current SCHEMA.
export function register({test,M,assert}){
  const v7Save=()=>{const s=structuredClone(M.seed());delete s.complianceX;delete s.cashbookX;delete s.configX;s.schema=7;return JSON.stringify(s);};
  test('[S8] SCHEMA is 13 (LF legacy parity) and a fresh seed carries it',()=>{assert.equal(M.SCHEMA,13);assert.equal(M.seed().schema,13);});
  test('[S8] a v7 save without Wave 0.5 slices restores without reset and is upgraded to the current SCHEMA',()=>{
    const r=M.restoreState(v7Save());assert.equal(r.reset,false);assert.equal(r.state.schema,M.SCHEMA);
    assert.equal(r.state.bookings.length,M.seed().bookings.length,'bookings kept');assert.equal(M.drawer(r.state),M.drawer(M.seed()),'drawer unchanged');});
  test('[S8] the v7 upgrade seeds the fictional compliance calendar (F14; SCC shape since DEC-116) and never overwrites an existing one',()=>{
    const r=M.restoreState(v7Save());const x=r.state.complianceX;assert.ok(x&&Object.keys(x.records['2026']).length>0,'records seeded');
    assert.equal(Object.keys(x.records['2026']).length,Object.keys(M.seed().complianceX.records['2026']).length);
    const kept=structuredClone(M.seed());kept.complianceX.records={'2026':{oc1:kept.complianceX.records['2026'].oc1}};kept.schema=7;
    assert.deepEqual(Object.keys(M.restoreState(JSON.stringify(kept)).state.complianceX.records['2026']),['oc1'],'existing slice untouched');});
  test('[S8] saves older than 7 or newer than SCHEMA are reset, not migrated',()=>{
    for(const v of [6,M.SCHEMA+1]){const s=structuredClone(M.seed());s.schema=v;assert.equal(M.restoreState(JSON.stringify(s)).reset,true,'schema '+v);}});
  test('[S9] a v8 save (before the masters wave) restores without reset, keeps its data and reads the masters from their seeds',()=>{
    const v8=structuredClone(M.seed());v8.schema=8;if(v8.mastersX){v8.mastersX={rows:{floors:v8.mastersX.rows?.floors??[]},history:[]};}
    const r=M.restoreState(JSON.stringify(v8));assert.equal(r.reset,false);assert.equal(r.state.schema,M.SCHEMA);
    assert.equal(r.state.bookings.length,M.seed().bookings.length);assert.equal(M.drawer(r.state),M.drawer(M.seed()));
    for(const id of ['roomTypes','paymentMethods','mealPlans','bookingSources','designations','hkTaskTypes'])assert.ok(M.mstRows(r.state,id).length>0,id);});
  test('[S10] a v9 save (before the SCC port wave) restores without reset, is marked migrated so it is saved once, and keeps its data',()=>{
    const v9=structuredClone(M.seed());v9.schema=9;const r=M.inspectSaved(JSON.stringify(v9));
    assert.equal(r.reset,false);assert.equal(r.migrated,true);assert.equal(r.state.schema,M.SCHEMA);
    assert.deepEqual(r.state,M.seed(),'a current-shape v9 save is kept as it is (only the schema number moves)');
    assert.equal(M.inspectSaved(JSON.stringify(r.state)).migrated,false,'saved once, then no further migration');});
}
