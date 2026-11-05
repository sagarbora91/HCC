// F12-CAP-PEOPLE · MEAS people captures: training (+ certificate-expiry reminder), exit reason, anonymous pulse. Names prefixed [F12-CAP-PEOPLE].
export function register({test,M,assert}){
  const {seed,execute}=M;
  const T='[F12-CAP-PEOPLE] ';
  const code=c=>e=>e.code===c;
  const staff=s=>M.hrSlice(s).staff;
  const off=s=>{const t=structuredClone(s);t.configX={...(t.configX??{}),modulesOff:[...(t.configX?.modulesOff??[]),'payroll']};return t;};
  const sid=s=>staff(s)[0].id;
  let n0=0;
  const training=(s,over={})=>({type:'MEAS_TRAINING_SAVE',key:'t-'+(++n0),staff:sid(s),course:'Fire safety',date:s.date,hours:4,certificateNo:'FS-1',validTo:M.addDays(s.date,20),mandatory:true,...over});
  const pulse=(s,over={})=>({type:'MEAS_PULSE_ADD',key:'p-'+(++n0),dept:M.hrSlice(s).depts[0].id,workload:3,respect:4,tools:3,payFairness:2,recommend:7,...over});

  test(T+'training: HR-001 gate (Owner and Manager only), saved with all fields',()=>{
    const s=seed();
    for(const u of ['reception','cashier','accountant','housekeeper','supervisor'])assert.throws(()=>execute(s,u,training(s)),code('FORBIDDEN'),u);
    const n=execute(s,'manager',training(s));
    const t=M.hrMeasSlice(n).training;assert.equal(t.length,1);
    assert.deepEqual({staff:t[0].staff,course:t[0].course,hours:t[0].hours,certificateNo:t[0].certificateNo,mandatory:t[0].mandatory},{staff:sid(s),course:'Fire safety',hours:4,certificateNo:'FS-1',mandatory:true});
    assert.match(t[0].id,/^TRN-/);
  });
  test(T+'training: validation (staff, course, future date, hours, valid-to before date) and idempotent on the client key',()=>{
    const s=seed();
    assert.throws(()=>execute(s,'owner',training(s,{staff:'NOPE'})),code('NOT_FOUND'));
    assert.throws(()=>execute(s,'owner',training(s,{course:' '})),code('VALIDATION'));
    assert.throws(()=>execute(s,'owner',training(s,{date:'2099-01-01'})),code('VALIDATION'));
    assert.throws(()=>execute(s,'owner',training(s,{hours:0})),code('VALIDATION'));
    assert.throws(()=>execute(s,'owner',training(s,{hours:'x'})),code('VALIDATION'));
    assert.throws(()=>execute(s,'owner',training(s,{validTo:'2020-01-01'})),code('VALIDATION'));
    const c=training(s);const a=execute(s,'owner',c),b=execute(a,'owner',c);
    assert.equal(M.hrMeasSlice(b).training.length,1);
  });
  test(T+'training: edit replaces the record; certificate-expiry reminder is created at 30 days lead, updated and removed with the date',()=>{
    const s0=seed();let s=execute(s0,'owner',training(s0));
    const id=M.hrMeasSlice(s).training[0].id,rid='RMD-MEAS-'+id;
    const r=s.remindersX.reminders.find(x=>x.id===rid);
    assert.ok(r);assert.equal(r.leadDays,30);assert.equal(r.date,M.hrMeasSlice(s).training[0].validTo);assert.equal(r.status,'open');
    s=execute(s,'owner',training(s,{id,course:'Fire safety 2',validTo:'2027-06-30'}));
    assert.equal(M.hrMeasSlice(s).training.length,1);assert.equal(M.hrMeasSlice(s).training[0].course,'Fire safety 2');
    assert.equal(s.remindersX.reminders.find(x=>x.id===rid).date,'2027-06-30');
    s=execute(s,'owner',training(s,{id,validTo:''}));
    assert.equal(s.remindersX.reminders.find(x=>x.id===rid),undefined);
    assert.throws(()=>execute(s,'owner',training(s,{id:'TRN-9999'})),code('NOT_FOUND'));
  });
  test(T+'expiry list: within 30 days or already expired, soonest first',()=>{
    let s=seed();
    s=execute(s,'owner',training(s,{validTo:M.addDays(s.date,10),course:'AA'}));
    s=execute(s,'owner',training(s,{validTo:M.addDays(s.date,200),course:'BB'}));
    s=execute(s,'owner',training(s,{validTo:s.date,course:'CC'}));
    assert.deepEqual(M.hrMeasTrainingExpiring(s).map(t=>t.course),['CC','AA']);
    assert.equal(M.hrMeasTrainingExpiring(s)[0].daysLeft,0);
    assert.deepEqual(M.hrMeasMandatoryLapsed(s).map(t=>t.course),[]);
  });
  test(T+'exit: HR-001 gate, reason list, dates, clear; staff record and payroll untouched',()=>{
    const s=seed(),id=sid(s),before=JSON.stringify(M.hrSlice(s));
    const c=k=>({type:'MEAS_EXIT_SET',staff:id,reason:'Resigned',exitDate:s.date,key:k});
    assert.throws(()=>execute(s,'reception',c('e1')),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{...c('e2'),reason:'Bored'}),code('VALIDATION'));
    assert.throws(()=>execute(s,'owner',{...c('e3'),exitDate:'nope'}),code('VALIDATION'));
    assert.throws(()=>execute(s,'owner',{...c('e4'),exitDate:'2099-01-01'}),code('VALIDATION'));
    assert.throws(()=>execute(s,'owner',{...c('e5'),staff:'NOPE'}),code('NOT_FOUND'));
    let n=execute(s,'manager',c('e6'));
    assert.deepEqual(M.hrMeasSlice(n).exit[id],{reason:'Resigned',exitDate:s.date});
    assert.equal(JSON.stringify(M.hrSlice(n)),before,'HR slice unchanged');
    n=execute(n,'owner',{type:'MEAS_EXIT_SET',staff:id,reason:'',key:'e7'});
    assert.equal(M.hrMeasSlice(n).exit[id],undefined);
  });
  test(T+'pulse: any signed-in user can answer; scores validated; quarter key is the FY quarter',()=>{
    const s=seed();
    for(const u of ['reception','housekeeper','cashier','owner'])execute(s,u,pulse(s));
    assert.throws(()=>execute(s,'reception',pulse(s,{dept:'zzz'})),code('VALIDATION'));
    assert.throws(()=>execute(s,'reception',pulse(s,{workload:0})),code('VALIDATION'));
    assert.throws(()=>execute(s,'reception',pulse(s,{respect:6})),code('VALIDATION'));
    assert.throws(()=>execute(s,'reception',pulse(s,{recommend:11})),code('VALIDATION'));
    assert.throws(()=>execute(s,'reception',pulse(s,{recommend:2.5})),code('VALIDATION'));
    assert.equal(M.hrMeasQuarter('2026-04-01'),'2026-27-Q1');assert.equal(M.hrMeasQuarter('2026-12-31'),'2026-27-Q3');
    assert.equal(M.hrMeasQuarter('2027-03-31'),'2026-27-Q4');assert.equal(M.hrMeasQuarter('2027-04-01'),'2027-28-Q1');
  });
  test(T+'pulse: NO staff id, user id or name is stored with an answer (record fields, whole slice)',()=>{
    const s=seed();
    const n=execute(s,'reception',pulse(s,{key:'client-key-1'}));
    const p=M.hrMeasSlice(n).pulse;assert.equal(p.length,1);
    assert.deepEqual(Object.keys(p[0]).sort(),['dept','id','payFairness','quarter','recommend','respect','tools','workload']);
    const slice=JSON.stringify(M.hrMeasSlice(n));
    for(const u of n.users){assert.ok(!slice.includes('"'+u.id+'"'),'user id '+u.id);assert.ok(!slice.includes(u.name),'user name '+u.name);}
    for(const t of staff(n)){assert.ok(!slice.includes('"'+t.id+'"'),'staff id '+t.id);assert.ok(!slice.includes(t.name),'staff name '+t.name);}
    assert.equal(p[0].dept,M.hrSlice(s).depts[0].id);
  });
  test(T+'MEAS_OFF: payroll module off refuses all three captures and keeps the data',()=>{
    let s=seed();s=execute(s,'owner',training(s));s=execute(s,'owner',pulse(s));
    const o=off(s);
    assert.throws(()=>execute(o,'owner',training(o)),code('MEAS_OFF'));
    assert.throws(()=>execute(o,'owner',{type:'MEAS_EXIT_SET',staff:sid(o),reason:'Resigned',exitDate:o.date,key:'x2'}),code('MEAS_OFF'));
    assert.throws(()=>execute(o,'reception',pulse(o)),code('MEAS_OFF'));
    assert.equal(M.hrMeasSlice(o).training.length,1);assert.equal(M.hrMeasSlice(o).pulse.length,1);
    assert.throws(()=>M.hrMeasGuard(o,M.HR_MEAS_KEYS.training),code('MEAS_OFF'));
    assert.doesNotThrow(()=>M.hrMeasGuard(s,M.HR_MEAS_KEYS.pulse));
  });
  test(T+'MEAS_OFF is checked before the permission (a switched-off capture refuses first)',()=>{
    const o=off(seed());
    assert.throws(()=>execute(o,'reception',training(o)),code('MEAS_OFF'));
  });
  test(T+'absent slice (old save) reads as empty; demo fixture rows are fictional and pulses carry no staff field',()=>{
    const s=seed();delete s.measX;assert.deepEqual(M.hrMeasSlice(s),{});assert.deepEqual(M.hrMeasTrainingExpiring(s),[]);
    for(const p of M.measPeopleDemoPulse(['d1']))assert.equal(Object.keys(p).includes('staff'),false);
    assert.equal(M.measPeopleDemoTraining(s,[sid(seed())]).length,3);
  });
}
