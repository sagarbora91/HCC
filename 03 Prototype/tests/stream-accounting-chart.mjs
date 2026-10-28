// ACC-1 lane acc1-chart · ledger groups, the chart of accounts as an Owner-only master, the mapping re-points (posting contract
// 02 Planning/agents/ACC1-POSTING-CONTRACT.md §1, §2, §7, §8; rulebook §3.1–§3.4). Names prefixed [ACC1-CHART]. Fictional data only.
export function register({test,M,assert}){
  const D='2026-10-24',Y='2026-10-23';
  const reason=r=>e=>e?.reason===r;
  const code=c=>e=>e?.code===c;
  const up=(s,u,cmd)=>M.execute(s,u,{type:'ACC_LEDGER_UPSERT',...cmd});
  const grp=(s,u,cmd)=>M.execute(s,u,{type:'ACC_GROUP_UPSERT',...cmd});
  const retire=(s,u,c,r)=>M.execute(s,u,{type:'ACC_LEDGER_RETIRE',code:c,...(r?{reason:r}:{})});
  // The §2 table of the contract: code → [group, control].
  const TABLE={'1000':['cash',1],'1005':['cash'],'1010':['bank'],'1020':['bank'],'1030':['bank',1],'1100':['debtors'],'1110':['debtors',1],'1115':['debtors',1],
    '1120':['debtors',1],'1200':['loans-advances',1],'1210':['loans-advances',1],'1300':['duties-taxes'],'1310':['duties-taxes'],'1320':['duties-taxes'],'1330':['duties-taxes'],
    '1340':['duties-taxes'],'1350':['duties-taxes'],'1360':['loans-advances'],'1370':['loans-advances'],'1400':['suspense'],'1500':['fixed-assets'],'1510':['fixed-assets'],
    '1520':['fixed-assets'],'1530':['fixed-assets'],'1540':['fixed-assets'],'2000':['current-liabilities'],'2100':['duties-taxes'],'2110':['duties-taxes'],'2120':['duties-taxes'],
    '2130':['duties-taxes'],'2140':['duties-taxes'],'2150':['duties-taxes'],'2200':['creditors',1],'2300':['current-liabilities'],'2310':['duties-taxes'],'2320':['duties-taxes'],
    '2330':['duties-taxes'],'2340':['current-liabilities'],'2400':['secured-loans',1],'2410':['unsecured-loans'],'3000':['capital'],'3100':['capital'],'4000':['sales'],
    '4100':['sales'],'4200':['sales'],'4300':['sales'],'4400':['sales'],'4900':['sales'],'4910':['sales'],'4950':['indirect-income'],'4960':['indirect-income'],
    '5000':['direct-expenses'],'5010':['direct-expenses'],'5015':['direct-expenses'],'5020':['indirect-expenses'],'5030':['direct-expenses'],'5040':['indirect-expenses'],
    '5050':['indirect-expenses'],'5060':['indirect-expenses'],'5070':['indirect-expenses'],'5080':['indirect-expenses'],'5090':['indirect-expenses'],'5100':['indirect-expenses'],
    '5110':['indirect-expenses'],'5120':['indirect-expenses'],'5200':['indirect-expenses'],'5300':['indirect-expenses'],'5400':['indirect-expenses'],'5900':['indirect-expenses'],
    '9999':['suspense']};

  test('[ACC1-CHART] §1 groups: Tally’s 28 (15 primary, 13 sub-groups), fixed ids, legacy group derived (BS+Dr Asset, BS+Cr Liability, PL+Cr Income, PL+Dr Expense, suspense)',()=>{
    assert.equal(M.ACC_GROUPS.length,28);assert.equal(M.ACC_GROUPS.filter(g=>!g.parent).length,15);
    assert.equal(new Set(M.ACC_GROUPS.map(g=>g.id)).size,28);
    for(const g of M.ACC_GROUPS.filter(g=>g.parent)){const p=M.ACC_GROUPS.find(x=>x.id===g.parent);assert.ok(p&&!p.parent,g.id);assert.deepEqual([g.nature,g.side],[p.nature,p.side],g.id);}
    const s=M.seed();
    assert.deepEqual(['current-assets','bank','current-liabilities','duties-taxes','sales','indirect-expenses','suspense','capital','fixed-assets','indirect-income'].map(id=>M.accGroupLegacy(s,id)),
      ['Asset','Asset','Liability','Liability','Income','Expense','Suspense','Liability','Asset','Income']);
    assert.deepEqual(M.accGroupPath(s,'bank'),['Current Assets','Bank Accounts']);
    assert.equal(M.accGroups(s).length,28,'no custom groups in a fresh fixture');
  });

  test('[ACC1-CHART] §2 built-in ledgers: every code of the contract table with its group and control flag; system, active; legacy groups of existing codes keep working',()=>{
    assert.deepEqual(M.ACC_LEDGERS.map(l=>l.code),Object.keys(TABLE).sort());
    for(const l of M.ACC_LEDGERS){const [g,c]=TABLE[l.code];assert.equal(l.group,g,l.code);assert.equal(!!l.control,!!c,l.code);assert.equal(l.system,true);assert.equal(l.active,true);
      assert.ok(M.ACC_GROUPS.some(x=>x.id===l.group),l.code);}
    assert.deepEqual(M.ACC_LEDGERS.filter(l=>l.control).map(l=>l.code),['1000','1030','1110','1115','1120','1200','1210','2200','2400']);
    const legacy=Object.fromEntries(M.ACC_CHART.map(a=>[a.code,a.group]));
    assert.deepEqual([legacy['1000'],legacy['1110'],legacy['2110'],legacy['3000'],legacy['4000'],legacy['4900'],legacy['5100'],legacy['9999'],legacy['1400'],legacy['4950']],
      ['Asset','Asset','Liability','Liability','Income','Income','Expense','Suspense','Suspense','Income']);
    assert.ok(M.ACC_CHART.every(a=>a.groupId&&a.system));
    assert.equal(M.accLedgerName('4200'),'Food & beverage revenue');assert.equal(M.accLedgerName('5900'),'Cash short');assert.equal(M.accLedgerName('4950'),'Cash over');
    // contract §3: a party ledger id resolves to its control ledger; a non-control code never does.
    assert.equal(M.accAccount('1110/CMP-0007').code,'1110');assert.equal(M.accAccount('4000/X'),undefined);assert.equal(M.accAccount('7777'),undefined);
    const s=M.seed();
    assert.equal(M.accLedger(s,'2200/EV-MORNING').code,'2200');assert.equal(M.accLedgerNature(s,'2200'),'Cr');assert.equal(M.accLedgerNature(s,'1310'),'Cr','Duties & Taxes is a Cr group (input GST carries a Dr balance in it, as in Tally)');
    assert.equal(M.accLedgerNature(s,'5100'),'Dr');assert.equal(M.accLedgerNature(s,'7777'),undefined);
  });

  test('[ACC1-CHART] §2 mapping re-points: pos:sales 4200, line:attrition 4300, cash over 4950 (short stays 5900), ITC CGST half 1310 + SGST half 1320; every default is a live ledger',()=>{
    const m=M.ACC_DEFAULT_MAP;
    assert.deepEqual([m['pos:sales'],m['line:attrition'],m['closing:cash-difference'],m['closing:cash-over'],m['tax:gst-input'],m['tax:gst-input-sgst'],m['receipt:udhaar']],
      ['4200','4300','5900','4950','1310','1320','1120']);
    for(const [k,v] of Object.entries(m)){assert.ok(M.ACC_KEY_PATTERN.test(k),k);const a=M.accAccount(v);assert.ok(a&&a.code!=='9999',k+' → '+v);}
    assert.equal(M.accCashDiffKey(-5000),'closing:cash-difference');assert.equal(M.accCashDiffKey(5000),'closing:cash-over');
    assert.deepEqual(M.accItcSplit(9001),[['tax:gst-input',4501],['tax:gst-input-sgst',4500]],'half / half as output GST (gstSplit)');
    assert.deepEqual(M.accItcSplit(-9001),[['tax:gst-input',-4501],['tax:gst-input-sgst',-4500]],'a reversal mirrors it');
    // An outlet bill now credits 4200 (the fixture day's POS receipts, if any, plus a fresh one through the default).
    const s=M.seed();assert.equal(M.accMapping(s)['pos:sales'],'4200');
    const rows=M.accMapRows(s);for(const k of ['line:attrition','closing:cash-over','tax:gst-input-sgst'])assert.ok(rows.some(r=>r.key===k&&r.mapped&&r.defaultAccount===m[k]),k);
    assert.equal(rows.find(r=>r.key==='tax:gst-input-sgst').group,'GST input credit (ITC)');
  });

  test('[ACC1-CHART] locked snapshots are never re-pointed: a mapping change moves live vouchers only',()=>{
    let s=M.loadScenario('ACC');const before=JSON.stringify(M.accLockOf(s,Y).vouchers);
    s=M.execute(s,'owner',{type:'ACC_MAP_SET',source:'line:room',account:'4100'});
    assert.equal(JSON.stringify(M.accVouchers(s,Y)),before,'the snapshot is shown unchanged');
    assert.ok(M.accLiveVouchers(s,Y).some(v=>v.entries.some(e=>e.account==='4100'))&&!M.accLiveVouchers(s,Y).some(v=>v.entries.some(e=>e.account==='4000')));
  });

  test('[ACC1-CHART] §7 chart edits are Owner-only (FORBIDDEN for Manager, Accountant, Tech admin, Cashier, Reception, Housekeeper)',()=>{
    const s=M.seed();
    for(const u of ['manager','accountant','techadmin','cashier','reception','housekeeper']){
      assert.throws(()=>up(s,u,{create:true,code:'5130',name:'Guest amenities (fictional)',group:'indirect-expenses'}),code('FORBIDDEN'),u);
      assert.throws(()=>retire(s,u,'4400'),code('FORBIDDEN'),u);
      assert.throws(()=>grp(s,u,{name:'Banquet income',parent:'sales'}),code('FORBIDDEN'),u);
      assert.equal(M.accCanEditChart(s,u),false,u);}
    assert.equal(M.accCanEditChart(s,'owner'),true);
  });

  test('[ACC1-CHART] Owner adds a ledger: unused 4-digit code, any group, unique name and Tally name; history row; old saves read the built-ins',()=>{
    let s=M.seed();assert.deepEqual(M.accChart(s).map(l=>l.code),M.ACC_LEDGERS.map(l=>l.code),'no chart slice = the built-ins');
    s=up(s,'owner',{create:true,code:'5130',name:'Guest amenities (fictional)',group:'indirect-expenses',gst:'none',note:'Toiletries for rooms'});
    const l=M.accLedger(s,'5130');assert.deepEqual([l.name,l.group,l.system,l.active,l.control],['Guest amenities (fictional)','indirect-expenses',false,true,undefined]);
    assert.equal(M.accLedgerLegacyGroup(s,'5130'),'Expense');assert.equal(M.accLedgerNature(s,'5130'),'Dr');
    assert.deepEqual(M.accChartHistory(s).map(h=>[h.kind,h.code,h.field,h.actor]),[['ledger','5130','created','owner']]);
    assert.ok(M.accChart(s).findIndex(x=>x.code==='5130')>M.accChart(s).findIndex(x=>x.code==='5120'),'code order');
    assert.throws(()=>up(s,'owner',{create:true,code:'5130',name:'Other',group:'indirect-expenses'}),reason('MASTER_CODE_TAKEN'));
    assert.throws(()=>up(s,'owner',{create:true,code:'4000',name:'Other',group:'sales'}),reason('MASTER_CODE_TAKEN'),'a built-in code is taken');
    assert.throws(()=>up(s,'owner',{create:true,code:'51A0',name:'X',group:'sales'}),reason('MASTER_INVALID'));
    assert.throws(()=>up(s,'owner',{create:true,code:'5140',name:'',group:'sales'}),reason('MASTER_INVALID'));
    assert.throws(()=>up(s,'owner',{create:true,code:'5140',name:'X',group:'nope'}),reason('MASTER_INVALID'));
    assert.throws(()=>up(s,'owner',{create:true,code:'5140',name:'X',group:'sales',gst:'maybe'}),reason('MASTER_INVALID'));
    assert.throws(()=>up(s,'owner',{create:true,code:'5140',name:'room revenue',group:'sales'}),reason('MASTER_CODE_TAKEN'),'names are unique, case-insensitive');
    assert.throws(()=>up(s,'owner',{create:true,code:'5140',name:'Sundries',tallyName:'Room Revenue',group:'sales'}),reason('MASTER_CODE_TAKEN'),'Tally names too');
    assert.throws(()=>up(s,'owner',{code:'5140',name:'X',group:'sales'}),reason('MASTER_INVALID'),'editing an unknown code is refused (add it as new)');
    // An unused Owner ledger may move to any group; edits write one history row per field.
    s=up(s,'owner',{code:'5130',group:'direct-expenses',tallyName:'Guest Amenities'});
    assert.deepEqual(M.accChartHistory(s).slice(0,2).map(h=>[h.field,h.before,h.after]).sort(),[['group','indirect-expenses','direct-expenses'],['tallyName','','Guest Amenities']]);
    assert.equal(M.accLedgerTallyName(s,'5130'),'Guest Amenities');
    assert.throws(()=>up(s,'owner',{code:'5130',group:'direct-expenses'}),e=>e.reason==='MASTER_INVALID'&&/Nothing changed/.test(e.message));
    s=up(s,'owner',{code:'5130',group:'sales'});assert.equal(M.accLedgerLegacyGroup(s,'5130'),'Income','unused: nature may change');
  });

  test('[ACC1-CHART] built-ins: renamable, re-groupable within the same Tally nature only, code and control fixed; back to defaults drops the override',()=>{
    let s=M.seed();
    s=up(s,'owner',{code:'4100',name:'Laundry & other services'});
    assert.equal(M.accLedgerNameOf(s,'4100'),'Laundry & other services');assert.equal(M.accLedgerName('4100'),'Other services revenue','the static built-in is unchanged');
    s=up(s,'owner',{code:'4100',group:'indirect-income'});assert.equal(M.accLedger(s,'4100').group,'indirect-income');
    assert.throws(()=>up(s,'owner',{code:'4100',group:'indirect-expenses'}),e=>e.reason==='MASTER_INVALID'&&/Income group/.test(e.message));
    assert.throws(()=>up(s,'owner',{code:'1000',group:'suspense'}),reason('MASTER_INVALID'),'Asset → Suspense refused');
    assert.throws(()=>up(s,'owner',{code:'9999',group:'current-assets'}),reason('MASTER_INVALID'),'Suspense stays Suspense');
    s=up(s,'owner',{code:'1030',name:'Bank accounts (HDFC, SBI)',control:false});assert.equal(M.accLedger(s,'1030').control,true,'control cannot be switched off');
    assert.ok(M.accChartRows(s).find(r=>r.code==='4100').overridden);
    s=up(s,'owner',{code:'4100',name:'Other services revenue',group:'sales'});
    assert.ok(!M.accSlice(s).chart.ledgers.some(l=>l.code==='4100'),'identical to the built-in → no stored row');
    assert.equal(M.accChartRows(s).find(r=>r.code==='4100').overridden,false);
    assert.deepEqual(M.accChartProblems(s),[]);
  });

  test('[ACC1-CHART] retire: refused while mapped, with a balance, with a party balance, or for 9999 / 1400 (MASTER_IN_USE); reactivate through upsert',()=>{
    let s=M.seed();
    s=retire(s,'owner','4400','Banquets via the outlet (fictional)');
    assert.equal(M.accLedger(s,'4400').active,false);
    assert.deepEqual(M.accChartHistory(s)[0],{...M.accChartHistory(s)[0],kind:'ledger',code:'4400',field:'active',before:'active',after:'retired · Banquets via the outlet (fictional)'});
    assert.throws(()=>retire(s,'owner','4400'),reason('MASTER_RETIRED'));
    assert.throws(()=>retire(s,'owner','7777'),code('NOT_FOUND'));
    assert.throws(()=>up(s,'owner',{code:'4400',active:false}),reason('MASTER_INVALID'),'retire goes through ACC_LEDGER_RETIRE');
    s=up(s,'owner',{code:'4400',active:true});assert.equal(M.accLedger(s,'4400').active,true);
    assert.ok(!M.accSlice(s).chart.ledgers.some(l=>l.code==='4400'),'reactivated built-in = defaults again');
    assert.throws(()=>retire(s,'owner','4000'),e=>e.reason==='MASTER_IN_USE'&&/line:room/.test(e.message));
    assert.throws(()=>retire(s,'owner','1000'),e=>e.reason==='MASTER_IN_USE'&&/method:Cash/.test(e.message));
    for(const c of ['9999','1400'])assert.throws(()=>retire(s,'owner',c),e=>e.reason==='MASTER_IN_USE'&&/structural/.test(e.message),c);
    // A ledger with a balance: service charges mapped to 4400 on the business date, then the mapping reset → 4400 is unmapped but not zero.
    s=M.execute(s,'owner',{type:'ACC_MAP_SET',source:'line:service',account:'4400'});
    s=M.execute(s,'owner',{type:'CHARGE',id:'HCC-1041',label:'Banquet hall (fictional)',amount:50000});
    s=M.execute(s,'owner',{type:'ACC_MAP_RESET',source:'line:service'});
    assert.equal(M.accLedgerMappedBy(s,'4400').length,0);
    // the charge now posts to 4100 again (live), so lock-free 4400 is zero: retire works
    assert.equal(M.accLedgerBalances(s,'4400').balance,0);
    // a locked snapshot keeps 4400's credit: the balance blocks retiring
    let t=M.loadScenario('ACC');
    t={...t,accountingX:{...t.accountingX,locks:{...t.accountingX.locks,[Y]:{...t.accountingX.locks[Y],vouchers:t.accountingX.locks[Y].vouchers.map(v=>({...v,entries:v.entries.map(e=>e.account==='4000'?{...e,account:'4400'}:e)}))}}}};
    assert.ok(M.accLedgerBalances(t,'4400').balance<0);
    assert.throws(()=>retire(t,'owner','4400'),e=>e.reason==='MASTER_IN_USE'&&/balance of ₹[\d,]+ Cr/.test(e.message));
    // A party balance (contract §3 id <control>/<party>) blocks retiring its control.
    t=M.loadScenario('ACC');
    // ACC-2 flows: 1115 is now the default ledger of receipt:ota — map that key elsewhere so the party balance is what blocks.
    t={...t,accountingX:{...t.accountingX,mapping:{...(t.accountingX.mapping??{}),'receipt:ota':'4960'},locks:{...t.accountingX.locks,[Y]:{...t.accountingX.locks[Y],vouchers:[...t.accountingX.locks[Y].vouchers,
      {no:'JRN/20261023/900',date:Y,type:'Journal',narration:'OTA receivable (fictional)',source:{kind:'closing',id:'test-ota',sub:'test'},unmapped:[],
        entries:[{account:'1115/CHN-GOIBIBO',debit:120000,credit:0},{account:'4960',debit:0,credit:120000}]}]}}}};
    assert.deepEqual(M.accLedgerBalances(t,'1115'),{balance:120000,own:0,parties:{'CHN-GOIBIBO':120000}});
    assert.throws(()=>retire(t,'owner','1115'),e=>e.reason==='MASTER_IN_USE'&&/CHN-GOIBIBO ₹1,200 Dr/.test(e.message));
    assert.match(M.accLedgerRetireBlocker(M.seed(),'1115'),/receipt:ota/,'ACC-2 flows: mapped by default (receipt:ota)');
    const um=M.seed();um.accountingX={mapping:{'receipt:ota':'4960'}};
    assert.equal(M.accLedgerRetireBlocker(um,'1115'),'','no party balance, unmapped → retirable');
  });

  test('[ACC1-CHART] §1 custom sub-groups: g-<slug> under any group, nature inherited; Tally’s 28 fixed; re-parent within the same nature, no loops; ledgers move into them',()=>{
    let s=M.seed();
    s=grp(s,'owner',{name:'Banquet income',parent:'sales'});
    const g=M.accGroupOf(s,'g-banquet-income');assert.deepEqual([g.parent,g.nature,g.side,g.custom],['sales','Cr','PL',true]);
    assert.equal(M.accGroupLegacy(s,'g-banquet-income'),'Income');assert.deepEqual(M.accGroupPath(s,'g-banquet-income'),['Sales Accounts','Banquet income']);
    assert.throws(()=>grp(s,'owner',{name:'Banquet Income',parent:'sales'}),reason('MASTER_CODE_TAKEN'));
    assert.throws(()=>grp(s,'owner',{name:'Banquet halls',parent:'nope'}),reason('MASTER_INVALID'));
    assert.throws(()=>grp(s,'owner',{id:'sales',name:'Revenue'}),e=>e.reason==='MASTER_INVALID'&&/fixed/.test(e.message));
    s=grp(s,'owner',{name:'Weddings',parent:'g-banquet-income'});assert.equal(M.accGroupRoot(s,'g-weddings').id,'sales');
    assert.throws(()=>grp(s,'owner',{id:'g-banquet-income',parent:'g-weddings'}),e=>/under itself/.test(e.message));
    assert.throws(()=>grp(s,'owner',{id:'g-banquet-income',parent:'indirect-expenses'}),e=>/Income group/.test(e.message));
    s=grp(s,'owner',{id:'g-banquet-income',parent:'indirect-income',name:'Banquet & hall income'});
    assert.deepEqual(M.accGroupOf(s,'g-banquet-income').name,'Banquet & hall income');
    assert.deepEqual(M.accChartHistory(s).filter(h=>h.kind==='group').map(h=>h.field).sort(),['created','created','name','parent']);
    s=up(s,'owner',{code:'4400',group:'g-weddings'});assert.equal(M.accLedgerLegacyGroup(s,'4400'),'Income');
    assert.deepEqual(M.accChartRows(s).find(r=>r.code==='4400').path,['Indirect Incomes','Banquet & hall income','Weddings']);
    // A custom group under Suspense A/c keeps the Suspense nature.
    s=grp(s,'owner',{name:'Clearing (fictional)',parent:'suspense'});assert.equal(M.accGroupLegacy(s,'g-clearing-fictional'),'Suspense');
    assert.equal(M.accGroupSlug('  Rooms — Deluxe & Suites  '),'g-rooms-deluxe-suites');
    assert.deepEqual(M.accChartProblems(s),[]);
  });

  test('[ACC1-CHART] chart problems: a mapping to a retired or unknown ledger, a ledger in a missing group, a broken group, duplicate names',()=>{
    const s=M.seed();
    const bad={...s,accountingX:{mapping:{'line:service':'4400','expense:Laundry':'6100'},chart:{
      ledgers:[{code:'4400',name:'Banquet & events revenue',group:'sales',system:true,gst:'none',active:false},{code:'6000',name:'Room revenue',group:'g-gone',system:false,active:true}],
      groups:[{id:'g-orphan',name:'Orphan',parent:'g-missing',nature:'Dr',side:'PL',custom:true}],history:[]}}};
    const ids=M.accChartProblems(bad).map(p=>p.id).sort();
    assert.deepEqual(ids,['dup:6000','group:g-orphan','ledger-group:6000','map:expense:Laundry','map:line:service']);
    assert.ok(M.accChartProblems(bad).every(p=>p.severity==='error'));
    assert.deepEqual(M.accChartRows(s).find(r=>r.code==='4200').mappedBy,['line:food','pos:sales']); // ACC-2 parity: line:food → 4200
    assert.deepEqual(M.accChartRows(s).find(r=>r.code==='1310').mappedBy,['tax:gst-input']);
  });
}
