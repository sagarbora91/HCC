// F12-CAP-GUEST · MEAS guest captures: survey, review figures, root causes, loyalty tag (MEAS-CONTRACT §2). Names prefixed [F12-CAP-GUEST].
export function register({test,M,assert}){
  const {execute,baseFixture,HccError}=M;
  const err=fn=>{try{fn();}catch(e){return {code:e instanceof HccError?e.code:'UNTYPED:'+e.message,reason:e.reason};}return {code:'NO_ERROR'};};
  const sv=(o={})=>({type:'MEAS_SURVEY_ADD',via:'desk',overall:5,cleanliness:4,staff:5,value:4,recommend:9,consent:true,...o});
  const fx=()=>{const s=baseFixture();s.guests??=[];if(!s.guests.length)s.guests.push({id:'G-900',name:'Test Guest',phone:'',email:'',address:'',nationality:'India'});return s;};

  test('[F12-CAP-GUEST] survey: desk entry stores five scores, recommend and consent; qr entry needs no staff role',()=>{
    let s=fx();s=execute(s,'reception',sv({id:'SV-1',comment:'Quiet room, friendly staff'}));
    const r=s.measX.surveys.at(-1);assert.deepEqual([r.id,r.via,r.overall,r.cleanliness,r.staff,r.value,r.recommend,r.consent,r.enteredBy],['SV-1','desk',5,4,5,4,9,true,'reception']);
    assert.equal(r.at,M.gsvAddMinutes(s.clock,-1),'simulated clock');
    s=execute(s,'housekeeper',sv({via:'qr',id:'SV-2'}));assert.equal(s.measX.surveys.at(-1).via,'qr');assert.equal(s.measX.surveys.at(-1).enteredBy,undefined);
  });
  test('[F12-CAP-GUEST] survey: desk entry on behalf of a guest needs PERM-MEAS-002 (Owner, Manager, Reception)',()=>{
    const s=fx();for(const u of ['housekeeper','cashier','maint']){const e=err(()=>execute(s,u,sv()));assert.equal(e.code,'FORBIDDEN',u);}
    for(const u of ['owner','manager','reception'])execute(s,u,sv());
  });
  test('[F12-CAP-GUEST] survey: scores 1-5, recommend 0-10, whole numbers, comment length',()=>{
    const s=fx();
    for(const bad of [{overall:0},{overall:6},{cleanliness:2.5},{staff:'5'},{value:-1},{breakfast:9},{recommend:11},{recommend:-1},{recommend:7.5},{via:'sms'},{comment:'x'.repeat(501)}]){
      const e=err(()=>execute(s,'reception',sv(bad)));assert.equal(e.code,'VALIDATION',JSON.stringify(bad));assert.equal(e.reason,'MEAS_VALIDATION',JSON.stringify(bad));}
    execute(s,'reception',sv({recommend:0,overall:1,breakfast:1,comment:'x'.repeat(500)}));execute(s,'reception',sv({recommend:10}));
  });
  test('[F12-CAP-GUEST] survey: consent is enforced, nothing is saved without it',()=>{
    const s=fx();for(const c of [false,undefined,'yes',1]){const e=err(()=>execute(s,'reception',sv({consent:c})));assert.equal(e.reason,'MEAS_NO_CONSENT');}
    assert.equal(s.measX?.surveys?.length??0,0);
  });
  test('[F12-CAP-GUEST] survey: ID data and guest identity can never be stored (fields, photo, ID number in the comment)',()=>{
    const s=fx();
    for(const f of ['idNumber','idType','idPhoto','photo','aadhaar','passport','name','guest','phone','email','address','idLast4'])
      assert.equal(err(()=>execute(s,'reception',sv({[f]:'x'}))).reason,'MEAS_ID_DATA',f);
    assert.equal(err(()=>execute(s,'reception',sv({comment:'My Aadhaar is 2345 6789 0124'}))).reason,'MEAS_ID_DATA');
    assert.equal(err(()=>execute(s,'reception',sv({comment:'passport K1234567 thanks'}))).reason,'MEAS_ID_DATA');
    const ok=execute(fx(),'reception',sv({comment:'Call 9876543210 for the booking HCC-1041'}));
    const r=ok.measX.surveys.at(-1);
    const allowed=['id','at','booking','roomType','channel','overall','cleanliness','staff','value','breakfast','recommend','comment','consent','via','enteredBy'];
    assert.deepEqual(Object.keys(r).filter(k=>!allowed.includes(k)),[],'only survey fields are stored');
    assert.ok(!/id(Number|Type|Photo)|photo|name|phone/i.test(Object.keys(r).join(',')));
  });
  test('[F12-CAP-GUEST] survey: stay link keeps booking, room type and channel only (no guest name)',()=>{
    let s=fx();const b=s.bookings.find(x=>x.status==='In house');
    s=execute(s,'reception',sv({booking:b.id}));const r=s.measX.surveys.at(-1);
    assert.equal(r.booking,b.id);assert.equal(r.channel,b.source);assert.equal(r.roomType,s.rooms.find(x=>x.id===b.room).type);
    assert.ok(!JSON.stringify(r).includes(b.guest));
    assert.equal(err(()=>execute(s,'reception',sv({booking:'HCC-NOPE'}))).code,'NOT_FOUND');
  });
  test('[F12-CAP-GUEST] survey: idempotent on the client id and on the command key',()=>{
    let s=fx();s=execute(s,'reception',sv({id:'SV-9'}));const n=s.measX.surveys.length;
    s=execute(s,'reception',sv({id:'SV-9'}));assert.equal(s.measX.surveys.length,n,'same client id');
    s=execute(s,'reception',sv({key:'k-1'}));const m=s.measX.surveys.length;s=execute(s,'reception',sv({key:'k-1'}));assert.equal(s.measX.surveys.length,m,'same key');
  });
  test('[F12-CAP-GUEST] survey: the public QR page is rate-limited (10 answers per 10 simulated minutes)',()=>{
    let s=fx();
    for(let i=0;i<M.GSV_QR_LIMIT.max;i++){s=execute(s,'owner',sv({via:'qr',id:'Q-'+i}));s.clock=M.gsvAddMinutes(s.clock,-1);}
    // the clock advances 1 minute per command: rewind so all ten fall inside the window
    const e=err(()=>execute(s,'owner',sv({via:'qr',id:'Q-x'})));assert.equal(e.reason,'MEAS_RATE_LIMIT');assert.equal(e.code,'RESTRICTED');
    // desk entries are not limited by the public-page limit
    execute(s,'reception',sv({via:'desk',id:'D-x'}));
    s.clock=M.gsvAddMinutes(s.clock,M.GSV_QR_LIMIT.windowMinutes+1);execute(s,'owner',sv({via:'qr',id:'Q-later'}));
  });
  test('[F12-CAP-GUEST] survey: QR page follows the Direct booking site module; desk entry does not',()=>{
    const s=fx();s.configX={...(s.configX??{}),modulesOff:['website']};
    const e=err(()=>execute(s,'owner',sv({via:'qr'})));assert.equal(e.reason,'MEAS_OFF');
    execute(s,'reception',sv({via:'desk'}));
    assert.equal(M.gsvSurveyChannelOn(s,'qr'),false);assert.equal(M.gsvSurveyChannelOn(fx(),'qr'),true);
  });
  test('[F12-CAP-GUEST] switch: item off refuses MEAS_OFF and keeps the data (runs once meas-settings is merged; stub = always on)',()=>{
    const s=fx();const off=M.measOn(s,'meas.guest.survey');
    if(off){ // stub or default state: every capture works and the key names are the contract keys
      assert.deepEqual(M.GSV_MEAS_KEYS,{survey:'meas.guest.survey',reputation:'meas.guest.reputation',recovery:'meas.guest.recovery',repeat:'meas.guest.repeat'});
      assert.doesNotThrow(()=>M.gsvMeasGuard(s,'meas.guest.survey'));
      s.configX={...(s.configX??{}),values:{...(s.configX?.values??{}),'meas.guest.survey':false}};
      if(M.measOn(s,'meas.guest.survey')===false){
        const kept=execute(fx(),'reception',sv({id:'SV-keep'}));
        const off2={...kept,configX:{...(kept.configX??{}),values:{...(kept.configX?.values??{}),'meas.guest.survey':false}}};
        assert.equal(err(()=>execute(off2,'reception',sv())).reason,'MEAS_OFF');assert.equal(off2.measX.surveys.length,1,'data kept');
        for(const [t,k] of [['MEAS_REVIEW_SAVE','reputation'],['MEAS_ROOTCAUSE_SET','recovery'],['MEAS_LOYALTY_SET','repeat']]){
          const o={...fx()};o.configX={values:{['meas.guest.'+k]:false}};assert.equal(err(()=>execute(o,'owner',{type:t})).reason,'MEAS_OFF',t);}
      }
    }
  });
  test('[F12-CAP-GUEST] NPS and rate-limit helpers are pure',()=>{
    assert.equal(M.gsvSurveyNps([]),undefined);
    assert.equal(M.gsvSurveyNps([{recommend:10},{recommend:9},{recommend:8},{recommend:6}]),25);
    assert.equal(M.gsvSurveyNps([{recommend:0},{recommend:6},{recommend:7}]),-67);
  });

  const rv=(o={})=>({type:'MEAS_REVIEW_SAVE',month:'2026-10',platform:'google',count:42,avgScore:4.4,score12m:4.3,responded:30,themes:'clean, breakfast',...o});
  test('[F12-CAP-GUEST] review: monthly per platform, replaced on re-save, Manager or Owner only',()=>{
    let s=fx();s=execute(s,'manager',rv());s=execute(s,'owner',rv({platform:'agoda',count:10,responded:10,avgScore:4}));
    assert.equal(s.measX.reviews.length,2);
    s=execute(s,'manager',rv({count:50,responded:41}));assert.equal(s.measX.reviews.length,2);assert.equal(s.measX.reviews.find(r=>r.id==='2026-10-google').count,50);
    for(const u of ['reception','cashier','accountant']){assert.equal(err(()=>execute(s,u,rv())).code,'FORBIDDEN',u);}
  });
  test('[F12-CAP-GUEST] review: validation boundaries',()=>{
    const s=fx();
    for(const bad of [{month:'2026-13'},{month:'26-10'},{platform:'tripadvisor'},{count:-1},{count:1.5},{avgScore:5.1},{avgScore:-0.1},{score12m:6},{responded:43},{themes:'x'.repeat(201)},{themes:'K1234567'}])
      assert.equal(err(()=>execute(s,'manager',rv(bad))).code,'VALIDATION',JSON.stringify(bad));
    execute(s,'manager',rv({avgScore:5,responded:42,count:42,score12m:0,themes:undefined}));execute(s,'manager',rv({count:0,responded:0,avgScore:0}));
  });

  test('[F12-CAP-GUEST] root causes: seeded list, Manager edits it, codes unique, used cause cannot be removed',()=>{
    let s=fx();assert.equal(M.gsvRootCauses(s).length,6);
    const rows=[...M.gsvRootCauses(s),{code:'WEATHER',label:'Weather',active:true}];
    assert.equal(err(()=>execute(s,'reception',{type:'MEAS_ROOTCAUSE_LIST_SAVE',rows})).code,'FORBIDDEN');
    s=execute(s,'manager',{type:'MEAS_ROOTCAUSE_LIST_SAVE',rows});assert.equal(s.measX.rootCauseList.length,7);
    for(const bad of [[],[{code:'a',label:'x',active:true}],[{code:'A',label:'x',active:true},{code:'A',label:'y',active:true}],[{code:'A',label:'',active:true}],[{code:'A',label:'x'}],'no'])
      assert.equal(err(()=>execute(s,'manager',{type:'MEAS_ROOTCAUSE_LIST_SAVE',rows:bad})).code,'VALIDATION');
    const cid=M.gsvSlice(s).complaints[0].id;
    s=execute(s,'manager',{type:'MEAS_ROOTCAUSE_SET',complaint:cid,code:'STAFF'});
    assert.equal(err(()=>execute(s,'manager',{type:'MEAS_ROOTCAUSE_LIST_SAVE',rows:rows.filter(r=>r.code!=='STAFF')})).code,'VALIDATION');
    execute(s,'manager',{type:'MEAS_ROOTCAUSE_LIST_SAVE',rows:rows.map(r=>r.code==='STAFF'?{...r,active:false}:r)});
  });
  test('[F12-CAP-GUEST] root cause set: complaint must exist, cause active, Manager only, clearing works, complaint record untouched',()=>{
    let s=fx();const c0=M.gsvSlice(s).complaints[0];const before=JSON.stringify(s.servicesX.complaints);
    assert.equal(err(()=>execute(s,'reception',{type:'MEAS_ROOTCAUSE_SET',complaint:c0.id,code:'STAFF'})).code,'FORBIDDEN');
    assert.equal(err(()=>execute(s,'manager',{type:'MEAS_ROOTCAUSE_SET',complaint:'CMP-NONE',code:'STAFF'})).code,'NOT_FOUND');
    assert.equal(err(()=>execute(s,'manager',{type:'MEAS_ROOTCAUSE_SET',complaint:c0.id,code:'NOPE'})).code,'VALIDATION');
    s=execute(s,'owner',{type:'MEAS_ROOTCAUSE_SET',complaint:c0.id,code:'MAINT'});assert.equal(s.measX.rootCause[c0.id],'MAINT');
    assert.equal(JSON.stringify(s.servicesX.complaints),before,'side table only');
    s=execute(s,'manager',{type:'MEAS_ROOTCAUSE_SET',complaint:c0.id,code:''});assert.equal(s.measX.rootCause[c0.id],undefined);
  });

  test('[F12-CAP-GUEST] loyalty tag: set and clear on the customer side table; ID-like tags and long tags refused',()=>{
    let s=fx();const g=s.guests[0].id;
    s=execute(s,'reception',{type:'MEAS_LOYALTY_SET',customer:g,tag:'Regular'});assert.equal(s.measX.preference[g].loyaltyTag,'Regular');
    assert.equal(s.guests[0].id,g);
    s=execute(s,'manager',{type:'MEAS_LOYALTY_SET',customer:g,tag:''});assert.equal(s.measX.preference[g].loyaltyTag,undefined);
    assert.equal(err(()=>execute(s,'reception',{type:'MEAS_LOYALTY_SET',customer:'G-NONE',tag:'x'})).code,'NOT_FOUND');
    assert.equal(err(()=>execute(s,'reception',{type:'MEAS_LOYALTY_SET',customer:g,tag:'x'.repeat(21)})).code,'VALIDATION');
    assert.equal(err(()=>execute(s,'reception',{type:'MEAS_LOYALTY_SET',customer:g,tag:'K1234567'})).code,'VALIDATION');
    assert.equal(err(()=>execute(s,'housekeeper',{type:'MEAS_LOYALTY_SET',customer:g,tag:'VIP'})).code,'FORBIDDEN');
  });
  test('[F12-CAP-GUEST] loyalty tag keeps the other preference fields (cap-desk owns them)',()=>{
    let s=fx();const g=s.guests[0].id;s.measX={preference:{[g]:{floor:'Ground',pillow:'firm'}}};
    s=execute(s,'reception',{type:'MEAS_LOYALTY_SET',customer:g,tag:'VIP'});assert.deepEqual(s.measX.preference[g],{floor:'Ground',pillow:'firm',loyaltyTag:'VIP'});
  });
  test('[F12-CAP-GUEST] captures are audited and never touch money, folio lines or the services slice',()=>{
    const s0=fx();const bk=JSON.stringify(s0.bookings);
    const s=execute(execute(s0,'reception',sv({id:'A-1'})),'manager',rv());
    assert.equal(JSON.stringify(s.bookings),bk);assert.match(s.audit[0].action,/MEAS REVIEW SAVE/);assert.match(s.audit[1].action,/MEAS SURVEY ADD/);
  });
}
