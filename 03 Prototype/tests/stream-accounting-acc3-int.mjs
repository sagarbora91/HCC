// ACC-3 integrator · items applied at integration (05 Delivery Evidence/Accounting/ACC3-DONE.md): Tally bill allocations read the explicit ACC-2
// vendor-payment allocations (acc3-ageing) before oldest-first; B11 refuses cash on a loan voucher only at or above ₹20,000 (ruling ACC3-R5).
// Names prefixed [ACC3-INT].
export function register({test,M,assert}){
  const T='[ACC3-INT] ';
  const book={groups:[],profitCarryNet:0,ledgers:[{id:'2200/V1',name:'Vendor One',group:'Sundry Creditors',billWise:true,openingNet:0,nature:'Cr',party:true},
    {id:'1030',name:'Bank',group:'Bank Accounts',billWise:false,openingNet:0,nature:'Dr',party:false},{id:'5000',name:'Purchases',group:'Purchase Accounts',billWise:false,openingNet:0,nature:'Dr',party:false}]};
  const bill=(no,id,amt)=>({no,date:'2026-04-0'+no.slice(-1),type:'Purchase',sub:'',sourceId:id,narration:'',entries:[{account:'5000',debit:amt,credit:0},{account:'2200/V1',debit:0,credit:amt}]});
  const pay=(alloc)=>({no:'PAY/1',date:'2026-04-05',type:'Payment',sub:'',sourceId:'vendor-payment:P1',narration:'',entries:[{account:'2200/V1',debit:50000,credit:0},{account:'1030',debit:0,credit:50000}],...(alloc?{alloc}:{})});
  test(T+'Tally bill allocations: an explicit vendor allocation pays its own bill first; without one, oldest-first as before',()=>{
    const a=new M.TallyBills(book);a.allocate(bill('PUR/1','E1',100000));a.allocate(bill('PUR/2','E2',50000));
    assert.deepEqual(a.allocate(pay())[0],[{name:'PUR/1',type:'Agst Ref',net:50000}],'oldest-first');
    const b=new M.TallyBills(book);b.allocate(bill('PUR/1','E1',100000));b.allocate(bill('PUR/2','E2',50000));
    assert.deepEqual(b.allocate(pay([{ref:'E2',amount:50000}]))[0],[{name:'PUR/2',type:'Agst Ref',net:50000}],'explicit allocation to the newer bill');
    const c=new M.TallyBills(book);c.allocate(bill('PUR/1','E1',100000));c.allocate(bill('PUR/2','E2',50000));
    assert.deepEqual(c.allocate(pay([{ref:'E2',amount:20000}]))[0],[{name:'PUR/2',type:'Agst Ref',net:20000},{name:'PUR/1',type:'Agst Ref',net:30000}],'the rest oldest-first');
  });
  test(T+'B11: cash of ₹20,000 or more on a loan voucher is refused, less passes (rulebook B11 beats the contract)',()=>{
    assert.equal(M.ACC_B11_CASH_LIMIT,2000000);
    assert.match(M.accLoanCashRefusal([{account:'2400',cash:false,amount:2000000},{account:'1000',cash:true,amount:2000000}]),/20,000/);
    assert.equal(M.accLoanCashRefusal([{account:'2400',cash:false,amount:1999999},{account:'1000',cash:true,amount:1999999}]),'');
  });
}
