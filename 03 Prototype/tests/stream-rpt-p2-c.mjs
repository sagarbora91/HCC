// F8-RPT-P2-C · batch c reports on the engine (prototype): asset register, leave balances, GRN register, sales by item, POS voids, scheduled runs,
// cancelled folio lines, rate sheet / publish log / change history. Every case builds its own facts on the BASE seed (clone + injected records), so the
// figures are known; each report also walks screen = Excel = print (threeWay), the role matrix, rule M, date-class edges and the provisional marker.
import {threeWay} from './rpt-golden/harness.mjs';
export function register({test,M,assert}){
  const T='[F8 RPT-P2-C] ';
  const AR='rpt.acc.asset-register',LB='rpt.people.leave-balances',GR='rpt.stock.grn-register',SI='rpt.rest.sales-by-item',PV='rpt.rest.voids',SR='rpt.admin.scheduled-runs',CL='rpt.admin.cancelled-lines',RS='rpt.admin.rate-sheet';
  const ALL=[AR,LB,GR,SI,PV,SR,CL,RS];
  const code=fn=>{try{fn();}catch(e){return e.code;}return 'none';};
  const run=(s,u,id,p={})=>M.runReport(s,u,id,p,{all:true});
  const body=r=>r.rows.filter(x=>x.kind==='row'),tot=r=>r.rows.find(x=>x.kind==='total');
  const base=()=>{const s=structuredClone(M.seed());return s;};
  const D1='2026-10-01',D2='2026-10-02';

  test(T+'registered as built; the money flag and gate of each report are as the catalogue says',()=>{
    for(const id of ALL){const d=M.reportDef(id);assert.ok(d,id);assert.equal(d.status,'built');}
    for(const id of [AR,GR,SI,PV,CL,RS])assert.equal(M.reportDef(id).moneyFlag,true,id);
    for(const id of [LB,SR])assert.equal(M.reportDef(id).moneyFlag,false,id);
    assert.equal(M.reportDef(AR).moneyGate,'PERM-ACC-001');assert.equal(M.reportDef(GR).moneyGate,'PERM-STK-004');assert.equal(M.reportDef(SI).moneyGate,'PERM-POS-004');
    assert.equal(M.reportDef(CL).moneyGate,'PERM-RPT-002');assert.equal(M.reportDef(RS).moneyGate,'PERM-YLD-001');
  });

  // ── asset register ──
  const assets=s=>{s.accountingX={...(s.accountingX??{}),assets:[
    {id:'A1',no:'FA-2026-002',ledger:'1510',name:'Lobby sofa',cost:4000000,date:'2026-06-01',vendor:'Shree Furniture',expense:'X2',itc:200000,putToUse:'2026-06-15'},
    {id:'A2',no:'FA-2026-001',ledger:'1520',name:'Boiler',cost:9000000,date:'2026-05-01',vendor:'Heatco',expense:'X1',itc:450000,putToUse:'2026-05-20',disposed:{date:'2026-09-30',amount:100000,voucher:'MV-9'}},
    {id:'A3',no:'FA-2026-003',ledger:'1510',name:'=cmd bed',cost:1000000,date:'2026-10-01',vendor:'',expense:'X3',itc:0},
  ]};return s;};
  test(T+'asset register: ordered by asset no, cost and ITC totals, put-to-use and disposal count only up to the date, later assets left out',()=>{
    const s=assets(base()),r=run(s,'owner',AR,{asOf:'2026-10-02'}),rows=body(r);
    assert.deepEqual(rows.map(x=>x.cells.no),['FA-2026-001','FA-2026-002','FA-2026-003']);
    assert.match(rows[0].cells.disposal,/Disposed 2026-09-30 · MV-9/);assert.equal(rows[1].cells.disposal,null);assert.equal(rows[2].cells.putToUse,null);
    assert.equal(tot(r).cells.cost,14000000);assert.equal(tot(r).cells.itc,650000);
    const early=body(run(s,'owner',AR,{asOf:'2026-05-25'}));assert.deepEqual(early.map(x=>x.cells.no),['FA-2026-001']);assert.equal(early[0].cells.disposal,null);assert.equal(early[0].cells.putToUse,'2026-05-20');
    assert.equal(code(()=>M.runReport(s,'owner',AR,{asOf:'2999-01-01'})),'VALIDATION');
    threeWay(M,assert,r);
  });
  test(T+'asset register: Owner, Manager and Accountant see it with money; Cashier, Reception, Housekeeping are refused; CSV formula cells are neutralised',()=>{
    const s=assets(base());
    for(const u of ['owner','accountant'])assert.ok(run(s,u,AR,{}).columns.some(c=>c.key==='cost'),u);
    for(const u of ['cashier','reception','housekeeper','supervisor','maint'])assert.equal(code(()=>M.runReport(s,u,AR,{})),'FORBIDDEN',u);
    assert.ok(M.toCsv(run(s,'owner',AR,{})).includes("'=cmd bed"));
  });

  // ── leave balances ──
  const leaveState=()=>{const s=base();const st=(M.hrSlice?M.hrSlice(s):s.staffX).staff;return {s,st};};
  test(T+'leave balances: entitlement less approved use up to the date; pending apart; half days count 0.5; previous FY and other staff ignored',()=>{
    const s=base();s.date='2027-04-05';s.staffX=s.staffX??{depts:[],staff:[],roster:{},att:{},leaves:[],advances:[],rateVersions:[],runs:[]};
    const mk=(id,name)=>({id,name,dept:'FO',designation:'Clerk',payType:'monthly',monthly:2000000,daily:0,defaultShift:'M',weeklyOff:0,joined:'2025-01-01',phone:'9000000000',active:true});
    s.staffX={...s.staffX,staff:[mk('ST-A','Asha'),mk('ST-B','Bimal')],leaves:[
      {id:'L1',staffId:'ST-A',date:'2026-06-01',type:'CL',from:'2026-06-01',to:'2026-06-01',days:1,reason:'',status:'Approved',requestedBy:'owner',requestedAt:'2026-05-30T09:00'},
      {id:'L2',staffId:'ST-A',date:'2026-06-02',type:'CL',from:'2026-06-02',to:'2026-06-02',days:0.5,half:'AM',reason:'',status:'Approved',requestedBy:'owner',requestedAt:'2026-05-30T09:00'},
      {id:'L3',staffId:'ST-A',date:'2026-12-01',type:'SL',from:'2026-12-01',to:'2026-12-01',days:1,reason:'',status:'Pending',requestedBy:'owner',requestedAt:'2026-10-01T09:00'},
      {id:'L4',staffId:'ST-A',date:'2026-12-02',type:'CL',from:'2026-12-02',to:'2026-12-02',days:1,reason:'',status:'Approved',requestedBy:'owner',requestedAt:'2026-10-01T09:00'},
      {id:'L5',staffId:'ST-A',date:'2026-03-10',type:'CL',from:'2026-03-10',to:'2026-03-10',days:1,reason:'',status:'Approved',requestedBy:'owner',requestedAt:'2026-03-01T09:00'},
      {id:'L6',staffId:'ST-B',date:'2026-07-01',type:'EL',from:'2026-07-01',to:'2026-07-01',days:1,reason:'',status:'Rejected',requestedBy:'owner',requestedAt:'2026-06-01T09:00'}]};
    const r=run(s,'owner',LB,{asOf:'2026-10-02'}),rows=body(r);
    assert.equal(rows.length,6);assert.ok(!r.columns.some(c=>c.money));
    const a=Object.fromEntries(rows.filter(x=>x.cells.employee==='Asha').map(x=>[x.cells.type,x.cells]));
    assert.equal(a['Casual Leave'].used,1.5);assert.equal(a['Casual Leave'].balance,a['Casual Leave'].entitlement-1.5);
    assert.equal(a['Sick Leave'].used,0);assert.equal(a['Sick Leave'].pending,1);assert.equal(a['Earned Leave'].pending,0);
    const b=rows.filter(x=>x.cells.employee==='Bimal');assert.ok(b.every(x=>x.cells.used===0&&x.cells.pending===0&&x.cells.balance===x.cells.entitlement));
    // the December approved day counts once the date reaches it
    assert.equal(run(s,'owner',LB,{asOf:'2026-12-31'}).rows.find(x=>x.cells.employee==='Asha'&&x.cells.type==='Casual Leave').cells.used,2.5);
    // the next financial year starts again at the quota
    assert.equal(run(s,'owner',LB,{asOf:'2027-04-01'}).rows.find(x=>x.cells.employee==='Asha'&&x.cells.type==='Casual Leave').cells.used,0);
    threeWay(M,assert,r);
  });
  test(T+'leave balances: Owner and Manager only; Accountant, Reception and floor roles are refused',()=>{
    const s=base();
    for(const u of ['owner','manager'])assert.doesNotThrow(()=>run(s,u,LB,{}),u);
    for(const u of ['reception','cashier','housekeeper','supervisor','maint'])assert.equal(code(()=>M.runReport(s,u,LB,{})),'FORBIDDEN',u);
  });

  // ── GRN register ──
  const grns=s=>{s.inventoryX={...M.invEmpty(),...(s.inventoryX??{}),grns:[
    {id:'GRN-2',date:D2,supplier:'Zenith Linen',ref:'ZL-77',lines:[{item:'ITM-X',qty:5,cost:12000}],actor:'owner'},
    {id:'GRN-1',date:D1,supplier:'Acme Supplies',ref:'AC-1',lines:[{item:'ITM-A',qty:10,cost:5000},{item:'ITM-B',qty:3,cost:20000}],actor:'owner'},
    {id:'GRN-3',date:'2026-07-01',supplier:'Acme Supplies',ref:'',lines:[{item:'ITM-A',qty:1,cost:5000}],actor:'owner'}]};return s;};
  test(T+'GRN register: line amounts, totals, supplier block, supplier filter, 92-day cap and date edges',()=>{
    const s=grns(base());s.date='2026-10-05';
    const r=run(s,'owner',GR,{from:D1,to:D2}),rows=body(r);
    assert.deepEqual(rows.map(x=>x.cells.grn),['GRN-1','GRN-1','GRN-2']);
    assert.equal(rows[0].cells.amount,50000);assert.equal(rows[1].cells.amount,60000);assert.equal(tot(r).cells.amount,170000);assert.equal(tot(r).cells.qty,18);
    const sup=r.sections[1].rows;assert.deepEqual(sup.filter(x=>x.kind==='row').map(x=>[x.cells.supplier,x.cells.grns,x.cells.amount]),[['Acme Supplies',1,110000],['Zenith Linen',1,60000]]);
    assert.equal(sup.find(x=>x.kind==='total').cells.amount,170000);
    assert.equal(body(run(s,'owner',GR,{from:D1,to:D2,supplier:'zenith'})).length,1);
    assert.equal(body(run(s,'owner',GR,{from:'2026-07-01',to:'2026-07-01'}))[0].cells.bill,null);
    assert.equal(code(()=>M.runReport(s,'owner',GR,{from:'2026-06-01',to:'2026-10-01'})),'VALIDATION');
    assert.equal(code(()=>M.runReport(s,'owner',GR,{from:D2,to:D1})),'VALIDATION');
    threeWay(M,assert,r);
  });
  test(T+'GRN register: Owner, Manager, Accountant only; floor roles and Reception refused',()=>{
    const s=grns(base());s.date='2026-10-05';
    for(const u of ['owner','manager','accountant'])assert.ok(run(s,u,GR,{from:D1,to:D2}).columns.some(c=>c.key==='rate'),u);
    for(const u of ['reception','cashier','housekeeper','supervisor','maint'])assert.equal(code(()=>M.runReport(s,u,GR,{from:D1,to:D2})),'FORBIDDEN',u);
  });

  // ── POS: sales by item, voids ──
  const line=(id,item,name,price,qty,x={})=>({id,item,name,price,qty,kot:'K1',...x});
  const order=(id,outlet,by,date,lines,x={})=>({id,outlet,table:'1',covers:2,date,openedAt:date+'T12:00',openedBy:by,status:'Settled',version:1,lines,kots:[],
    settlement:{mode:'outlet',billNo:'B-'+id,method:'Cash',net:0,taxBps:500,tax:0,total:0,date,at:date+'T13:00',by},...x});
  const pos=s=>{const P=s.posX??{outlets:[],menu:[],orders:[],receipts:[],kotSeq:{},billSeq:0};
    const outs=P.outlets.length>=2?P.outlets:[{id:'OUT-1',name:'Cafe',kind:'table'},{id:'OUT-2',name:'Bar',kind:'table'}],o1=outs[0].id,o2=outs[1].id;
    s.posX={...P,outlets:outs,menu:[],orders:[
      order('O1',o1,'cashier',D1,[line('l1','m-tea','Tea',5000,4),line('l2','m-cake','Cake',15000,2),line('l3','m-tea','Tea',5000,1,{voided:true,voidReason:'wrong item',voidBy:'cashier',voidDate:D1})]),
      order('O2',o1,'manager',D1,[line('l1','m-tea','Tea',5000,6)]),
      order('O3',o2,'manager',D2,[line('l1','m-beer','=Beer',30000,3)]),
      order('O4',o1,'cashier',D2,[line('l1','m-cake','Cake',15000,2,{voided:true,voidReason:'guest left',voidBy:'cashier',voidDate:D2,voidApproval:'APR-V1'})],{status:'Void',voidReason:'guest left',voidedBy:'cashier',voidedAt:D2+'T20:15',settlement:undefined})]};
    s.approvals=[...(s.approvals??[]),{id:'APR-V1',booking:'',amount:30000,reason:'guest left',status:'Executed',actor:'cashier',version:1,kind:'pos-void',approver:'manager',decidedAt:D2+'T20:16'}];return s;};
  test(T+'sales by item: quantity and value per item, categories by value, shares, voided lines out, outlet filter, edges',()=>{
    const s=pos(base());s.date='2026-10-05';
    const r=run(s,'owner',SI,{from:D1,to:D2}),rows=body(r);
    const tea=rows.find(x=>x.cells.item==='Tea');assert.equal(tea.cells.qty,10);assert.equal(tea.cells.value,50000);
    assert.equal(rows.find(x=>x.cells.item==='Cake').cells.value,30000);assert.equal(tot(r).cells.value,50000+30000+90000);
    assert.ok(Math.abs(rows.reduce((a,x)=>a+x.cells.share,0)-100)<=0.3);
    assert.equal(body(run(s,'owner',SI,{from:D1,to:D2,outlet:s.posX.outlets[1].name})).length,1);assert.equal(body(run(s,'owner',SI,{from:D1,to:D1,outlet:s.posX.outlets[1].id})).length,0);
    assert.equal(body(run(s,'owner',SI,{from:'2026-10-03',to:'2026-10-04'})).length,0);
    assert.ok(M.toCsv(r).includes("'=Beer"));
    threeWay(M,assert,r);
  });
  test(T+'sales by item: Cashier sees only own settled bills and cannot export; Restaurant, Reception and floor roles refused',()=>{
    const s=pos(base());s.date='2026-10-05';
    const c=run(s,'cashier',SI,{from:D1,to:D2});
    assert.equal(tot(c).cells.value,4*5000+2*15000);
    assert.ok(M.reportCatalogue(s,'cashier').some(x=>x.id===SI&&!x.canExport));assert.equal(code(()=>M.runReport(s,'cashier',SI,{from:D1,to:D2},{purpose:'export'})),'FORBIDDEN');
    for(const u of ['reception','housekeeper','supervisor','maint'])assert.equal(code(()=>M.runReport(s,u,SI,{})),'FORBIDDEN',u);
    const rest=s.users.find(u=>u.role==='Restaurant');if(rest)assert.equal(code(()=>M.runReport(s,rest.id,SI,{})),'FORBIDDEN','Restaurant');
  });
  test(T+'POS voids: one row per voided line with who and approver, oldest first, whole-check void carries its time, total, Cashier refused',()=>{
    const s=pos(base());s.date='2026-10-05';
    const r=run(s,'owner',PV,{from:D1,to:D2}),rows=body(r);
    assert.equal(rows.length,2);assert.equal(rows[0].cells.time,D1);assert.equal(rows[0].cells.item,'Tea');assert.equal(rows[0].cells.value,5000);assert.equal(rows[0].cells.approvedBy,null);
    assert.equal(rows[1].cells.time,D2+' 20:15');assert.equal(rows[1].cells.value,30000);assert.ok(rows[1].cells.approvedBy);assert.equal(tot(r).cells.value,35000);
    assert.equal(body(run(s,'owner',PV,{from:D2,to:D2})).length,1);
    for(const u of ['cashier','reception','housekeeper'])assert.equal(code(()=>M.runReport(s,u,PV,{})),'FORBIDDEN',u);
    assert.equal(code(()=>M.runReport(s,'owner',PV,{from:'2026-06-01',to:'2026-10-01'})),'VALIDATION');
    threeWay(M,assert,r);
  });

  // ── scheduled runs ──
  test(T+'scheduled runs: only runs started by a schedule, newest first, result and error shown, range edges, no money; Owner and Manager only',()=>{
    const s=base();s.date='2026-10-05';
    s.rptX={favourites:{},exportLog:[],schedules:[{id:'SCH1',reportId:GR,params:{},format:'xlsx',cadence:{kind:'daily',time:'06:00'},owner:'owner',recipients:['owner','accountant'],enabled:true}],
      runs:[{id:'R1',scheduleId:'SCH1',reportId:GR,params:{},by:'owner',startedAt:D1+'T06:00',status:'ok',rows:3},
        {id:'R2',scheduleId:'SCH1',reportId:GR,params:{},by:'owner',startedAt:D2+'T06:00',status:'failed',error:'mail refused'},
        {id:'R3',reportId:GR,params:{},by:'owner',startedAt:D2+'T09:00',status:'ok'}]};
    const r=run(s,'owner',SR,{from:D1,to:D2}),rows=body(r);
    assert.deepEqual(rows.map(x=>x.cells.result),['failed','ok']);assert.equal(rows[0].cells.error,'mail refused');assert.match(rows[0].cells.schedule,/daily 06:00/);assert.ok(rows[0].cells.recipients.includes(','));
    assert.equal(body(run(s,'owner',SR,{from:D2,to:D2})).length,1);assert.ok(!r.columns.some(c=>c.money));
    assert.doesNotThrow(()=>run(s,'manager',SR,{from:D1,to:D2}));
    for(const u of ['reception','cashier','housekeeper','maint'])assert.equal(code(()=>M.runReport(s,u,SR,{})),'FORBIDDEN',u);
    threeWay(M,assert,r);
  });

  // ── cancelled lines ──
  test(T+'cancelled folio lines: reversal lines with the cancelled item, amount, requester and reason; credit notes and other adjustments are not cancellations',()=>{
    const s=base();s.date='2026-10-05';const b=s.bookings[0];
    b.lines=[...b.lines,{id:'LN1',label:'Laundry',amount:35000,date:D1,kind:'service',taxBps:1800},
      {id:'RV1',label:'Reversal · Laundry · posted twice',amount:-35000,date:D2,kind:'adjustment',taxBps:1800,reversalOf:'LN1',approvalId:'APR-C1'},
      {id:'CN1',label:'Credit note CN-1 · goodwill',amount:-10000,date:D2,kind:'adjustment',taxBps:1800,approvalId:'APR-C2'}];
    s.approvals=[...(s.approvals??[]),{id:'APR-C1',booking:b.id,amount:35000,reason:'posted twice',status:'Executed',actor:'reception',version:1,kind:'correction',target:'LN1',approver:'owner'}];
    const r=run(s,'owner',CL,{from:D1,to:D2}),rows=body(r);
    assert.equal(rows.length,1);assert.equal(rows[0].cells.item,'Laundry');assert.equal(rows[0].cells.amount,35000);assert.equal(rows[0].cells.reason,'posted twice');assert.equal(rows[0].cells.booking,b.id);assert.ok(rows[0].cells.by);
    assert.equal(tot(r).cells.amount,35000);assert.equal(body(run(s,'owner',CL,{from:D1,to:D1})).length,0);
    for(const u of ['owner','manager','accountant'])assert.doesNotThrow(()=>run(s,u,CL,{}),u);
    for(const u of ['reception','cashier','housekeeper'])assert.equal(code(()=>M.runReport(s,u,CL,{})),'FORBIDDEN',u);
    threeWay(M,assert,r);
  });

  // ── rate sheet ──
  const yld=s=>{const y=s.yieldX??M.yldEmpty();s.yieldX={...y,publishes:[{id:'PUB1',at:D1+'T10:00',actor:'owner',from:'2026-10-10',to:'2026-10-12',types:['Deluxe'],reason:'festival',rows:[{type:'Deluxe',date:'2026-10-10',before:300000,after:350000},{type:'Deluxe',date:'2026-10-13',before:350000,after:300000,closing:true}]}],
    overrides:[{type:'Deluxe',date:'2026-10-11',rate:400000,reason:'event',actor:'manager',at:D2+'T11:00'}]};return s;};
  test(T+'rate sheet: three views - rate per room type, plan and night; publish log; change history with before / after and overrides',()=>{
    const s=yld(base());s.date='2026-10-05';
    const sheet=run(s,'owner',RS,{from:'2026-10-05',to:'2026-10-06'});assert.ok(body(sheet).length>0);assert.deepEqual(sheet.columns.map(c=>c.key),['roomType','plan','rate','night']);
    assert.ok(body(sheet).every(x=>Number.isInteger(x.cells.rate)&&x.cells.rate>0));
    const log=run(s,'owner',RS,{view:'publish log',from:'2026-10-05',to:'2026-10-20'}),lr=body(log);
    assert.equal(lr.length,1);assert.match(lr[0].cells.type,/2 night rates to 2026-10-12/);assert.equal(lr[0].cells.reason,'festival');assert.ok(!log.columns.some(c=>c.key==='before'));
    assert.equal(body(run(s,'owner',RS,{view:'publish log',from:'2026-10-13',to:'2026-10-20'})).length,0);
    const hist=run(s,'owner',RS,{view:'change history',from:'2026-10-05',to:'2026-10-20'}),hr=body(hist);
    assert.deepEqual(hr.map(x=>x.cells.type),['Published','Override','Published (closing row)']);assert.equal(hr[0].cells.before,300000);assert.equal(hr[0].cells.after,350000);assert.equal(hr[1].cells.before,null);assert.equal(hr[1].cells.after,400000);
    assert.equal(code(()=>M.runReport(s,'owner',RS,{from:'2026-10-05',to:'2027-01-30'})),'VALIDATION');
    assert.equal(code(()=>M.runReport(s,'owner',RS,{from:'2026-10-05',to:'2027-01-05'})),'VALIDATION');
    assert.equal(code(()=>M.runReport(s,'owner',RS,{view:'bogus'})),'VALIDATION');
    for(const r of [sheet,log,hist])threeWay(M,assert,r);
  });
  test(T+'rate sheet: Owner and Manager; Accountant, Reception and floor roles refused',()=>{
    const s=yld(base());s.date='2026-10-05';
    for(const u of ['owner','manager'])assert.doesNotThrow(()=>run(s,u,RS,{view:'publish log',from:'2026-10-05',to:'2026-10-06'}),u);
    for(const u of ['reception','cashier','housekeeper','supervisor','maint'])assert.equal(code(()=>M.runReport(s,u,RS,{})),'FORBIDDEN',u);
  });

  // ── provisional marker ──
  test(T+'provisional: a range that reaches the open business date is marked on the control reports, a closed past range is not',()=>{
    const s=pos(base());const open=run(s,'owner',PV,{from:s.date,to:s.date});assert.equal(open.about.provisional,true);
    const past=run(s,'owner',GR,{from:'2026-01-01',to:'2026-01-02'});assert.equal(past.about.provisional,false);
  });
}
