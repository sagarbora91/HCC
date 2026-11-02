// LF-ui (lane lf-tests) · DEC-061 walk of the LF money features: housekeeping / maintenance (and the Restaurant role at the counter) never see or move hotel money. [LF-ROLES]
export function register({test,M,assert}){
  const {seed,execute,can,loadScenario,NAV,guestMoneyFilter}=M;
  const T='[LF-ROLES] ';
  const BLOCKED=['housekeeper','supervisor','maint'];
  test(T+'no billing / payments permission for floor staff, so Counter bills, Credit guests and the booking money buttons stay closed',()=>{
    const s=seed();for(const u of BLOCKED){assert.equal(can(s,u,'billing'),false,u+' billing');assert.equal(can(s,u,'payments'),false,u+' payments');}
    for(const u of ['owner','manager','reception','cashier'])assert.ok(can(s,u,'billing')||can(s,u,'payments'),u);
  });
  test(T+'every LF money command is refused to floor staff',()=>{
    const s=loadScenario('LF-PARITY'),b=s.bookings[0];
    const cmds=[{type:'STAY_RATE_CHANGE',booking:b.id,rate:100,scope:'all',reason:'x'},{type:'BILL_CORRECTION_REQUEST',booking:b.id,reason:'x',changes:[]},
      {type:'CTR_BILL_ISSUE',party:{name:'Test'},lines:[{label:'x',unit:100,gstCategory:'other'}],settlement:{mode:'paid',method:'Cash'}},{type:'CTR_BILL_VOID_REQUEST',id:s.counterX.bills[0].id,reason:'x'},
      {type:'GUEST_CREDIT_REQUEST',booking:b.id},{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:s.guests[0].id,limit:100},{type:'GUEST_RATE_SET',guestId:s.guests[0].id,roomType:'Classic',rate:100},
      {type:'GUEST_RATE_CLEAR',guestId:s.guests[0].id,roomType:'Classic'}];
    for(const u of BLOCKED)for(const c of cmds)assert.throws(()=>execute(s,u,c),e=>e.code==='FORBIDDEN'||/Access denied/.test(e.message),u+' '+c.type);
  });
  test(T+'agreed rate and credit account are Owner-only to change; non-Owner money roles cannot',()=>{
    const s=loadScenario('LF-PARITY'),g=s.guests[0];
    for(const u of ['manager','reception','cashier','accountant']){
      assert.throws(()=>execute(s,u,{type:'GUEST_RATE_SET',guestId:g.id,roomType:'Classic',rate:100}),e=>e.code==='FORBIDDEN',u);
      assert.throws(()=>execute(s,u,{type:'GUEST_CREDIT_ACCOUNT_SET',guestId:g.id,limit:100}),e=>e.code==='FORBIDDEN',u);}
  });
  test(T+'guest money filter hides the credit account and agreed rates from floor staff',()=>{
    const s=loadScenario('LF-PARITY'),g=s.guests[0];
    for(const u of BLOCKED){const f=guestMoneyFilter(s,u,g);assert.equal(f.creditAccount,undefined,u);assert.equal(f.agreedRates,undefined,u);}
    assert.ok(guestMoneyFilter(s,'owner',g).creditAccount);
  });
  test(T+'udhaar-backed counter bills and credit ageing are not reachable by floor staff (cashier shift permission is payments)',()=>{
    const s=loadScenario('LF-PARITY');for(const u of BLOCKED)assert.equal(can(s,u,'payments'),false,u);
    void NAV;
  });
}
