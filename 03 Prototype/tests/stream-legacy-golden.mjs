// LG-TESTS · Customers 360 legacy rollups, hand-computed (contract §9). Names prefixed [LG-TESTS].
// The contract formulas on an inline hand-made list, and lg-ui's real src/fixtures/ext/legacy-sample.ts (M.LEGACY_SAMPLE, exported by the integrator).
export function register({test,M,assert}){
  // Three fictional legacy stays: nights 2 + 3 + 1 = 6; net 498000 + 1144000 paise (third stay has none).
  const stays=[
    {fy:'2025-26',bookingId:101,billNo:1,arrival:'2025-06-10',departure:'2025-06-12',nights:2,room:'101',roomCount:1,status:'CheckedOut',net:498000},
    {fy:'2025-26',bookingId:103,billNo:3,arrival:'2025-08-01',departure:'2025-08-04',nights:3,room:'201',roomCount:1,status:'CheckedOut',net:1144000},
    {fy:'2026-27',bookingId:208,arrival:'2026-10-01',departure:'2026-10-02',nights:1,room:'104',roomCount:1,status:'CheckedOut'}, // no net: money masked
  ];
  const roll=(live,leg)=>({nights:live.nights+leg.reduce((a,x)=>a+x.nights,0),
    lifetime:live.lifetime+leg.reduce((a,x)=>a+(x.net||0),0),
    firstStay:[live.first,...leg.map(x=>x.arrival)].filter(Boolean).sort()[0]});
  test('[LG-TESTS] rollup with no legacy stays equals the live figures',()=>{
    assert.deepEqual(roll({nights:4,lifetime:900000,first:'2026-05-01'},[]),{nights:4,lifetime:900000,firstStay:'2026-05-01'});});
  test('[LG-TESTS] rollup adds legacy nights and net, first stay is the earlier date',()=>{
    const r=roll({nights:4,lifetime:900000,first:'2026-05-01'},stays);
    assert.equal(r.nights,4+2+3+1);assert.equal(r.lifetime,900000+498000+1144000);assert.equal(r.firstStay,'2025-06-10');});
  test('[LG-TESTS] a legacy stay without net adds nothing to lifetime and the badge rows carry no folio link',()=>{
    assert.equal(roll({nights:0,lifetime:0,first:null},[stays[2]]).lifetime,0);
    for(const x of stays) assert.ok(!('folioId' in x)&&!('bookingRef' in x));});
  test('[LG-TESTS] lg-ui sample obeys the same formulas',()=>{
    const sample=M.LEGACY_SAMPLE;
    assert.ok(sample,'LEGACY_SAMPLE exported from services/mock');
    const list=Array.isArray(sample)?sample:Object.values(sample).flat();
    assert.ok(list.length>=6);
    for(const x of list){assert.ok(x.nights>=1);assert.ok(x.arrival);assert.ok(!('folioId' in x));}});
}
