// F12-MEAS2-E · twin of tests/Hcc.Meas.Tests/Hk/AssetLifeSqlTests.cs: the FF&E reserve base counts a re-posted night once, at the new rate (N2),
// and a register asset's kind is its ledger (block) name (N5). Names prefixed [MEAS2-E].
export function register({test,M,assert}){
  const {seed,runReport}=M;
  const T='[MEAS2-E] ';
  const state=()=>{const s=seed();
    s.maintenanceX={assets:[{id:'A1',name:'AC 101',kind:'AC',area:'101',make:'',model:'',installedOn:'2020-01-01',status:'Working'}],vendors:[],amcs:[],spares:[],plans:[],orders:[]};
    s.measX={...(s.measX??{}),assetLife:{A1:{expectedLifeMonths:120,conditionGrade:'B',replacementCostPaise:4500000}}};
    // night 1 posted at 3,000 and re-posted at 2,000 the way stay-rate.ts writes it (reversal kind 'adjustment' with reversalOf), night 2 at 1,000
    s.bookings=[{...seed().bookings[0],lines:[
      {id:'N1',label:'Room night',amount:300000,date:'2026-06-01',kind:'room',taxBps:500},
      {id:'N1R',label:'Reversal · Room night · rate change',amount:-300000,date:'2026-06-01',kind:'adjustment',taxBps:500,reversalOf:'N1'},
      {id:'N1B',label:'Room night',amount:200000,date:'2026-06-01',kind:'room',taxBps:500},
      {id:'N2',label:'Room night',amount:100000,date:'2026-06-02',kind:'room',taxBps:500}]}];
    s.accountingX={...(s.accountingX??{}),assets:[
      {id:'FA1',no:'FA-2627-001',ledger:'1510',name:'Sofa set',cost:12000,date:'2026-06-10',vendor:'V',expense:'EX1',itc:0,putToUse:'2026-06-11'},
      {id:'FA2',no:'FA-2526-001',ledger:'1520',name:'Water pump',cost:5000,date:'2025-12-01',vendor:'V',expense:'EX2',itc:0,putToUse:'2025-12-02'}]};
    return s;};
  const al=(p={})=>runReport(state(),'owner','rpt.acc.asset-life',{asOf:'2026-10-24',...p},{all:true});
  const names=r=>r.rows.filter(x=>x.kind==='row').map(x=>x.cells.asset);

  test(T+'N2: the reserve base counts a re-posted night once, at the new rate',()=>{
    const tot=al().rows.find(x=>x.kind==='total');
    assert.equal(tot.cells.reserveAccrued,9000);                                    // 3 % × (2,000 + 1,000), not 3 % × 6,000
    assert.equal(tot.cells.capexSpent,12000);
    assert.equal(tot.group,'red');
  });
  test(T+'N5: a register asset\'s kind is its ledger name (same order and filter as the server)',()=>{
    assert.deepEqual(names(al()),['AC 101','FA-2627-001 Sofa set','FA-2526-001 Water pump']);
    assert.deepEqual(names(al({kind:'Plant & machinery'})),['FA-2526-001 Water pump']);
    assert.deepEqual(names(al({kind:'Fixed assets 1510'})),[]);
  });
}
