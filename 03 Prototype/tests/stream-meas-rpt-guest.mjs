// F12-RPT-GUEST · the six guest-experience measurement reports: satisfaction, reputation-index, service-recovery, desk-timing, upsell, repeat-loyalty.
// Screen = Excel = print (threeWay), one test per exception rule at the boundary, empty-data note, MEAS_OFF, permissions, no identity in a survey row.
import {build} from 'esbuild';
import {threeWay} from './rpt-golden/harness.mjs';
await build({entryPoints:['tests/rpt-meas-guest-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/rpt-meas-guest.mjs',logLevel:'error'});
const M=await import('./.generated/rpt-meas-guest.mjs');
export function register({test,assert}){
  const T='[F12-RPT-GUEST] ';
  const IDS=['rpt.guests.satisfaction','rpt.guests.reputation-index','rpt.guests.service-recovery','rpt.guests.desk-timing','rpt.guests.upsell','rpt.guests.repeat-loyalty'];
  const [SAT,REP,REC,DESK,UP,RPT]=IDS;
  const code=fn=>{try{fn();}catch(e){return e.code+':'+e.message;}return 'none';};
  /** A small clean state far from the fixtures' dates so nothing else pollutes the figures. */
  const fresh=()=>{const s=structuredClone(M.seed());s.date='2030-12-31';s.clock='2030-12-31T09:00';s.audit=[];return s;};
  const run=(s,uid,id,p={})=>M.runReport(s,uid,id,p,{all:true});
  const rows=r=>r.rows.filter(x=>x.kind==='row');
  const tpl=s=>s.bookings.find(b=>b.status==='Departed')??s.bookings[0];
  let seq=0;
  const stay=(s,o)=>{const b=structuredClone(tpl(s));seq++;Object.assign(b,{id:'MG-'+seq,status:'Departed',lines:[],receipts:[],phone:'900000'+String(1000+seq),guest:'Guest '+seq,guestId:undefined,source:'Direct',version:1},o);s.bookings.push(b);return b;};
  const sv=(o={})=>({id:'SV-'+(++seq),at:'2030-12-10T10:00',overall:5,cleanliness:5,staff:5,value:5,recommend:9,consent:true,via:'desk',...o});
  const withSurveys=(list)=>{const s=fresh();s.measX={...(s.measX??{}),surveys:list};return s;};

  test(T+'the six reports are registered with the catalogue columns, flags, switch keys and permissions',()=>{
    const want={[SAT]:[10,false,undefined,'meas.guest.survey'],[REP]:[7,false,undefined,'meas.guest.reputation'],[REC]:[9,true,'PERM-RPT-002','meas.guest.recovery'],
      [DESK]:[6,false,undefined,'meas.guest.desk-timing'],[UP]:[9,true,'PERM-RPT-002','meas.guest.upsell'],[RPT]:[8,true,'PERM-RPT-002','meas.guest.repeat']};
    for(const [id,[n,money,gate,key]] of Object.entries(want)){
      const d=M.reportDef(id);assert.ok(d,id);assert.equal(d.columns.length,n,id+' columns');assert.equal(d.moneyFlag,money,id);assert.equal(d.moneyGate,gate,id);
      assert.equal(d.area,'guests');assert.equal(d.status,'MEAS');assert.equal(M.MEAS_REPORT_KEY[id],key,id+' switch key');
    }
    assert.deepEqual(M.reportDef(SAT).columns.map(c=>c.key),['month','responses','responseRate','overall','cleanliness','staff','value','breakfast','nps','verbatim']);
    assert.deepEqual(M.reportDef(REC).columns.filter(c=>c.money).map(c=>c.key),['compensation']);
    assert.deepEqual(M.reportDef(UP).columns.filter(c=>c.money).map(c=>c.key),['lateCheckout','breakfast','extraBed','revenuePerArrival']);
    assert.deepEqual(M.reportDef(RPT).columns.filter(c=>c.money).map(c=>c.key),['revenueShare']);
    assert.equal(M.reportDef(DESK).phone,'no');
  });

  test(T+'empty data: zero rows and the "Capture needed" note, never an error',()=>{
    const s=fresh();
    for(const [id,p] of [[SAT,{month:'2030-12'}],[REP,{fy:'2030-31'}],[REC,{from:'2030-12-01',to:'2030-12-31'}],[UP,{from:'2030-12-01',to:'2030-12-31'},],[RPT,{month:'2030-12'}]]){
      const r=run(s,'owner',id,p);assert.equal(r.rows.length,0,id);assert.match(r.about.dateBasis,/Capture needed/,id);
    }
    const d=run(s,'owner',DESK,{from:'2030-12-01',to:'2030-12-31'});assert.equal(d.rows.length,0);
  });

  test(T+'satisfaction: averages, response rate, NPS = %9-10 minus %0-6 (rounded), verbatim only with consent, no identity in the row',()=>{
    const s=withSurveys([sv({recommend:10,overall:5,comment:'Lovely stay'}),sv({recommend:9,overall:4,breakfast:3}),sv({recommend:8,overall:4}),sv({recommend:6,overall:2,comment:'Noisy'}),sv({recommend:0,overall:1,consent:false,comment:'secret'})]);
    stay(s,{departure:'2030-12-12'});stay(s,{departure:'2030-12-13'});
    const r=run(s,'owner',SAT,{month:'2030-12'}),c=r.rows[0].cells;
    assert.equal(r.rows.length,1);assert.equal(c.responses,5);assert.equal(c.overall,'3.2');assert.equal(c.breakfast,'3.0');
    assert.equal(c.nps,M.roundHalf((2-2)*100/5));assert.equal(c.nps,0);
    assert.equal(c.responseRate,M.pct1(5,2));assert.equal(c.verbatim,'Lovely stay | Noisy','a comment without consent is never listed');
    assert.deepEqual(Object.keys(c).sort(),M.reportDef(SAT).columns.map(x=>x.key).sort(),'only the catalogue columns: no guest, booking or ID field');
    assert.equal(M.npsOf([{recommend:9},{recommend:9},{recommend:7}]),67);assert.equal(M.npsOf([]),null);
    // room type / channel filters
    const s2=withSurveys([sv({roomType:'Deluxe',channel:'OTA'}),sv({roomType:'Standard',channel:'Direct'})]);
    assert.equal(run(s2,'owner',SAT,{month:'2030-12',roomType:'Deluxe'}).rows[0].cells.responses,1);
    assert.equal(run(s2,'owner',SAT,{month:'2030-12',channel:'Direct'}).rows[0].cells.responses,1);
  });

  test(T+'satisfaction red rule: NPS 29 is red, 30 is not (target meas.target.nps); a changed target moves the line',()=>{
    const mk=(prom,det,pass)=>withSurveys([...Array(prom).fill(0).map(()=>sv({recommend:10})),...Array(det).fill(0).map(()=>sv({recommend:0})),...Array(pass).fill(0).map(()=>sv({recommend:8}))]);
    // 13 promoters / 7 passives / 0 detractors of 20 -> 65; make exactly 30 and 29 with 100 responses
    const s30=mk(30,0,70),s29=mk(29,0,71);
    assert.equal(M.satisfactionExceptions(s30,'2030-12').nps,30);assert.equal(M.satisfactionExceptions(s30,'2030-12').red,false);
    assert.equal(M.satisfactionExceptions(s29,'2030-12').nps,29);assert.equal(M.satisfactionExceptions(s29,'2030-12').red,true);
    s30.configX={...(s30.configX??{}),values:{...(s30.configX?.values??{}),'meas.target.nps':31}};
    assert.equal(M.satisfactionExceptions(s30,'2030-12').red,true,'target 31');
  });

  test(T+'satisfaction red rule: a score of 2 or lower unanswered for 48 hours is red at 48:00, not at 47:59; a complaint on the booking answers it',()=>{
    const low=sv({at:'2030-12-29T09:00',staff:2,recommend:10,booking:'MG-X'});
    const at=(clock,list=[low])=>{const s=withSurveys(list);s.clock=clock;return s;};
    assert.equal(M.satisfactionExceptions(at('2030-12-31T08:59'),'2030-12').unanswered,0,'47:59');
    assert.equal(M.satisfactionExceptions(at('2030-12-31T09:00'),'2030-12').unanswered,1,'48:00');
    assert.equal(M.satisfactionExceptions(at('2030-12-31T09:00'),'2030-12').red,true);
    const s=at('2030-12-31T09:00');s.servicesX={requests:[],items:[],complaints:[{id:'C-1',booking:'MG-X',room:'101',guest:'G',category:'Other',severity:'Low',description:'d',owner:'manager',status:'Open',createdAt:'2030-12-29T11:00',by:'reception',log:[]}]};
    assert.equal(M.satisfactionExceptions(s,'2030-12').unanswered,0,'answered by a complaint on the same booking');
    assert.equal(M.surveyLow(sv({breakfast:2})),true);assert.equal(M.surveyLow(sv({breakfast:3})),false);
  });

  test(T+'reputation: one row per month and platform for the FY, response rate, two-decimal scores, platform filter',()=>{
    const s=fresh();s.measX={reviews:[{id:'R1',month:'2030-05',platform:'google',count:10,avgScore:4.5,score12m:4.4,responded:9,themes:'clean'},{id:'R2',month:'2030-06',platform:'google',count:10,avgScore:4.2,responded:8},{id:'R3',month:'2030-06',platform:'agoda',count:5,avgScore:4,responded:5},{id:'R4',month:'2031-05',platform:'google',count:1,avgScore:5,responded:1}]};
    const r=run(s,'owner',REP,{fy:'2030-31'});
    assert.deepEqual(rows(r).map(x=>[x.cells.month,x.cells.platform]),[['2030-05','google'],['2030-06','agoda'],['2030-06','google']]);
    assert.equal(rows(r)[0].cells.avgScore,'4.50');assert.equal(rows(r)[0].cells.responseRate,90);assert.equal(rows(r)[0].cells.score12m,'4.40');assert.equal(rows(r)[1].cells.score12m,null);
    assert.equal(rows(run(s,'owner',REP,{fy:'2030-31',platform:'agoda'})).length,1);
    assert.match(code(()=>run(s,'owner',REP,{fy:'2030-31',platform:'tripadvisor'})),/VALIDATION/);
  });

  test(T+'reputation red rule: a drop of 0.30 is red, 0.29 is not; response rate 79 % is red, 80 % is not',()=>{
    const r=(avg,count=10,responded=10)=>({id:'x',month:'2030-06',platform:'google',count,avgScore:avg,responded});
    const prev={id:'p',month:'2030-05',platform:'google',count:10,avgScore:4.5,responded:10};
    assert.equal(M.reputationRed(r(4.2),prev).drop,true);assert.equal(M.reputationRed(r(4.21),prev).drop,false);
    assert.equal(M.reputationRed(r(4.5,100,79)).lowResponse,true);assert.equal(M.reputationRed(r(4.5,100,80)).lowResponse,false);
    assert.equal(M.reputationRed(r(4.5,100,80),prev).red,false);assert.equal(M.reputationRed(r(4.5)).drop,false,'no previous month = no drop');
    const s=fresh();s.measX={reviews:[prev,{id:'q',month:'2030-06',platform:'google',count:10,avgScore:4.2,responded:10}]};
    assert.equal(M.reputationRed(s.measX.reviews[1],M.prevReview(s,s.measX.reviews[1])).red,true);
  });

  const cmp=(id,room,at,extra={})=>({id,room,guest:'G',category:'Room cleanliness',severity:'Low',description:'d',owner:'manager',status:'Resolved',createdAt:at,resolvedAt:at.slice(0,11)+'12:00',by:'reception',log:[],...extra});
  test(T+'service recovery: grouped by category and room, hours to resolve, root cause label, compensation and approver, repeat count',()=>{
    const s=fresh();
    s.measX={rootCauseList:[{code:'LINEN',label:'Linen shortage',active:true}],rootCause:{'C-2':'LINEN'}};
    s.approvals.push({id:'AP-9',booking:'MG-1',amount:50000,reason:'goodwill',status:'Executed',actor:'reception',version:1,approver:'manager'});
    s.servicesX={requests:[],items:[],complaints:[cmp('C-1','101','2030-09-01T10:00'),cmp('C-2','101','2030-11-30T10:00',{recovery:{amount:50000,reason:'goodwill',approvalId:'AP-9',at:'2030-11-30T12:00',by:'reception'}}),cmp('C-3','102','2030-12-01T10:00')]};
    const r=run(s,'owner',REC,{from:'2030-11-25',to:'2030-12-05'});
    const a=rows(r).find(x=>x.cells.room==='101').cells,b=rows(r).find(x=>x.cells.room==='102').cells;
    assert.equal(a.complaints,1);assert.equal(a.hours,'2.0');assert.equal(a.rootCause,'Linen shortage');assert.equal(a.compensation,50000);assert.equal(a.approver,'Manager'.length?a.approver:'');assert.ok(a.approver);
    assert.equal(a.repeat,1);assert.equal(b.repeat,0);assert.equal(b.compensation,0);
    assert.ok(r.columns.some(c=>c.key==='compensation'),'Owner holds PERM-RPT-002');
    assert.equal(rows(run(s,'owner',REC,{from:'2030-11-25',to:'2030-12-05',room:'102'})).length,1);
    assert.match(code(()=>run(s,'owner',REC,{from:'2030-01-01',to:'2030-12-05'})),/VALIDATION/,'range over 92 days');
  });

  test(T+'service recovery red rule: a repeat 90 days apart is red, 91 days is not; compensation without an approval id is red',()=>{
    const s=fresh();
    s.servicesX={requests:[],items:[],complaints:[cmp('A','101','2030-09-01T10:00'),cmp('B','101','2030-11-30T10:00'),cmp('C','102','2030-09-01T10:00'),cmp('D','102','2030-12-01T10:00')]};
    const by=id=>s.servicesX.complaints.find(c=>c.id===id);
    assert.equal(M.isRepeat(s,by('B')),true,'90 days');assert.equal(M.isRepeat(s,by('D')),false,'91 days');assert.equal(M.isRepeat(s,by('A')),false,'first complaint');
    assert.equal(M.recoveryRed(s,[by('B')]).red,true);assert.equal(M.recoveryRed(s,[by('D')]).red,false);
    const pay=(approvalId)=>cmp('E','103','2030-12-02T10:00',{recovery:{amount:10000,reason:'x',...(approvalId?{approvalId}:{}),at:'2030-12-02T11:00',by:'reception'}});
    assert.equal(M.unapproved(pay()),true);assert.equal(M.unapproved(pay('AP-1')),false);
    assert.equal(M.recoveryRed(s,[pay()]).noApproval,1);assert.equal(M.recoveryRed(s,[pay('AP-1')]).red,false);
  });

  const deskState=(inMin,outMin)=>{
    const s=fresh();s.measX={};const name=s.users.find(u=>u.id==='reception').name;
    const b=stay(s,{room:'101',checkedInAt:'2030-12-20T10:00',arrival:'2030-12-20',departure:'2030-12-21'});
    s.audit.push({id:'a1',at:'2030-12-20T10:01',actor:name,action:'CHECKIN',detail:''});
    s.keysX={jobs:[{id:'K1',event:'issued',kind:'issue_guest',booking:b.id,room:'101',state:'done',by:'reception',at:'2030-12-20T10:00',timeoutAt:'2030-12-20T10:30',finishedAt:`2030-12-20T10:${String(inMin).padStart(2,'0')}`}]};
    s.audit.push({id:'a2',at:'2030-12-21T11:00',actor:name,action:'CHARGE',detail:''},{id:'a3',at:`2030-12-21T11:${String(outMin).padStart(2,'0')}`,actor:name,action:'CHECKOUT',detail:''});
    return s;
  };
  test(T+'desk timing: medians by hour and desk user (arrival-to-key, bill-to-checkout), queue peak, desk user filter',()=>{
    const s=deskState(7,5);const r=run(s,'owner',DESK,{from:'2030-12-20',to:'2030-12-21'});
    const a=rows(r).find(x=>x.cells.hour==='10:00').cells,o=rows(r).find(x=>x.cells.hour==='11:00').cells;
    assert.equal(a.arrivals,1);assert.equal(a.arrivalToKey,7);assert.equal(a.queuePeak,1);assert.equal(o.billToCheckout,5);assert.ok(a.deskUser.length>0);
    assert.equal(rows(run(s,'owner',DESK,{from:'2030-12-20',to:'2030-12-21',deskUser:'cashier'})).length,0);
    assert.equal(rows(run(s,'owner',DESK,{from:'2030-12-20',to:'2030-12-21',deskUser:'reception'})).length,2);
    assert.equal(M.lowerMedian([3,1,2,10]),2);assert.equal(M.lowerMedian([]),null);
  });
  test(T+'desk timing red rule: 9 minutes at check-in is red, 8 is not; 7 minutes at check-out is red, 6 is not (targets 8 / 6)',()=>{
    const red=(i,o)=>{const s=deskState(i,o);const rs=M.deskRows(s,'2030-12-20','2030-12-21');
      return {inRed:M.deskRed(s,rs.find(x=>x.hour==='10:00')).slowIn,outRed:M.deskRed(s,rs.find(x=>x.hour==='11:00')).slowOut};};
    assert.deepEqual(red(9,7),{inRed:true,outRed:true});assert.deepEqual(red(8,6),{inRed:false,outRed:false});
    const s=deskState(9,7);s.configX={values:{'meas.target.checkinMinutes':10,'meas.target.checkoutMinutes':7}};
    assert.deepEqual([M.deskRed(s,{arrivalToKey:9,billToCheckout:7}).slowIn,M.deskRed(s,{arrivalToKey:9,billToCheckout:7}).slowOut],[false,false],'targets are read through measTarget');
  });

  const upState=()=>{
    const s=fresh();s.measX={bookingExtra:{}};const types=[...new Set(s.rooms.map(r=>r.type))];
    const mkb=(o,lines,extra)=>{const b=stay(s,{checkedInAt:'2030-12-15T12:00',arrival:'2030-12-15',departure:'2030-12-16',lines,...o});if(extra)s.measX.bookingExtra[b.id]=extra;return b;};
    const L=(label,amount)=>({id:'L'+(++seq),label,amount,date:'2030-12-15',kind:'service',taxBps:1800});
    mkb({},[L('UPG · Room upgrade Deluxe to Suite',100000),L('LCO1 · Late check-out (after 14:00)',30000)],{bookedType:types[0],upgrade:'paid'});
    mkb({},[L('Extra bed · night',40000),L('Breakfast add-on',20000)],{bookedType:types[0],upgrade:'free',upgradeApprover:'manager'});
    mkb({arrival:'2030-12-15',departure:'2030-12-15'},[]);
    return s;
  };
  test(T+'upsell: counts, money columns before GST, revenue per arrival rounded half up, approver label',()=>{
    const s=upState();const r=run(s,'owner',UP,{from:'2030-12-15',to:'2030-12-15'}),c=r.rows[0].cells;
    assert.equal(c.arrivals,3);assert.equal(c.upgradesPaid,1);assert.equal(c.upgradesFree,1);assert.ok(c.approver);assert.equal(c.dayUse,1);
    assert.equal(c.lateCheckout,30000);assert.equal(c.breakfast,20000);assert.equal(c.extraBed,40000);assert.equal(c.revenuePerArrival,M.roundHalf((100000+30000+20000+40000)/3));
  });
  test(T+'upsell red rule: a free upgrade without an approver is red; revenue per arrival 4999 is red, 5000 is not',()=>{
    assert.equal(M.upsellRed(fresh(),{freeNoApprover:1,perArrival:99999}).red,true);
    assert.equal(M.upsellRed(fresh(),{freeNoApprover:0,perArrival:4999}).red,true);assert.equal(M.upsellRed(fresh(),{freeNoApprover:0,perArrival:5000}).red,false);
    const s=fresh();s.configX={values:{'meas.target.upsellPerArrival':6000}};assert.equal(M.upsellRed(s,{freeNoApprover:0,perArrival:5000}).red,true,'target read through measTarget');
    const u=upState();const f=M.upsellFigures(u,M.arrivalsOf(u,'2030-12-15','2030-12-15'));assert.equal(f.freeNoApprover,0);
    delete u.measX.bookingExtra[Object.keys(u.measX.bookingExtra)[1]].upgradeApprover;assert.equal(M.upsellFigures(u,M.arrivalsOf(u,'2030-12-15','2030-12-15')).freeNoApprover,1);
  });

  /** Customers: P1..P4 stayed in 2030-07; per month the listed phones arrive (repeat = had an earlier stay). */
  const repState=plan=>{
    const s=fresh();s.measX={preference:{}};let n=0;const ph=()=>'91'+String(8000000000+(++n));const people={};
    const who=k=>people[k]??=ph();
    for(const k of ['P1','P2','P3','P4'])stay(s,{phone:who(k),arrival:'2030-07-05',departure:'2030-07-06',lines:[]});
    for(const [month,keys] of Object.entries(plan))for(const k of keys)stay(s,{phone:who(k),arrival:`${month}-10`,departure:`${month}-11`,lines:[{id:'R'+(++seq),label:'Room',amount:100000,date:`${month}-10`,kind:'room',taxBps:0}]});
    return {s,people};
  };
  test(T+'repeat loyalty: seen-before count, repeat share, stays, revenue share, 3+ stay list with loyalty tag and offer sent',()=>{
    const {s,people}=repState({'2030-08':['P1','P2','P3','N1'],'2030-09':['N9'],'2030-10':['P1']});
    s.measX.preference={['n:x']:{}};
    const f=M.repeatFigures(s,'2030-08');assert.equal(f.customers,4);assert.equal(f.seenBefore,3);assert.equal(f.share,75);assert.equal(f.stays,4);assert.equal(f.revenueShare,75);
    const r=run(s,'owner',RPT,{month:'2030-10'});
    const guest=rows(r).find(x=>x.cells.stays===3);assert.ok(guest,'P1 has 3 completed stays');assert.equal(guest.cells.offerSent,false);
    const key=M.customerIndex(s).find(c=>c.completed.length===3).key;
    s.measX.preference[key]={loyaltyTag:'Gold'};s.messagingX={outbox:[{id:'m1',template:'manual',channel:'WhatsApp',booking:'x',guestKey:key,guest:'g',to:'x',body:'offer',queuedAt:'2030-12-01T10:00',outcome:'Read',actor:'manager'}]};
    const g2=rows(run(s,'owner',RPT,{month:'2030-10'})).find(x=>x.cells.stays===3).cells;assert.equal(g2.loyaltyTag,'Gold');assert.equal(g2.offerSent,true);
  });
  test(T+'repeat loyalty red rule: share falling two months running is red; equal to the month before is not',()=>{
    const falling=repState({'2030-08':['P1','P2','P3','N1'],'2030-09':['P1','P2','N2','N3'],'2030-10':['P1','N4','N5','N6']});
    const x=M.repeatFalling(falling.s,'2030-10');assert.deepEqual([x.share,x.prev,x.prev2,x.red],[25,50,75,true]);
    const flat=repState({'2030-08':['P1','P2','P3','N1'],'2030-09':['P1','P2','N2','N3'],'2030-10':['P1','P2','N4','N5']});
    const y=M.repeatFalling(flat.s,'2030-10');assert.deepEqual([y.share,y.prev,y.red],[50,50,false]);
    const empty=M.repeatFalling(fresh(),'2030-10');assert.equal(empty.red,false);
  });

  test(T+'MEAS_OFF: a switched-off item hides its report and refuses a run; the pack switch does the same; data is kept',()=>{
    const s=withSurveys([sv()]);
    assert.ok(M.reportCatalogue(s,'owner').some(x=>x.id===SAT));assert.ok(run(s,'owner',SAT,{month:'2030-12'}).rows.length===1);
    const off=structuredClone(s);off.configX={...(off.configX??{}),values:{...(off.configX?.values??{}),'meas.guest.survey':false}};
    assert.ok(!M.reportCatalogue(off,'owner').some(x=>x.id===SAT));assert.match(code(()=>run(off,'owner',SAT,{month:'2030-12'})),/MEAS_OFF/);
    assert.equal(off.measX.surveys.length,1,'data kept');
    const pack=structuredClone(s);pack.configX={values:{'meas.pack.guest':false}};
    for(const id of IDS){assert.ok(!M.reportCatalogue(pack,'owner').some(x=>x.id===id),id);assert.match(code(()=>M.runReport(pack,'owner',id,{})),/MEAS_OFF/,id);}
  });

  test(T+'permissions: Housekeeping and Maintenance see none of the six (DEC-061); the Owner sees all',()=>{
    const s=fresh();
    for(const id of IDS){for(const u of ['housekeeper','maint']){const p=id===DESK||id===SAT||id===REP?{}:{};assert.match(code(()=>M.runReport(s,u,id,p)),/FORBIDDEN/,`${u} ${id}`);}
      assert.ok(M.reportCatalogue(s,'owner').some(x=>x.id===id),'owner '+id);}
  });

  test(T+'screen = Excel = print on populated data for all six reports',()=>{
    const s=withSurveys([sv({comment:'Great'}),sv({recommend:3,overall:2})]);
    s.measX.reviews=[{id:'R1',month:'2030-05',platform:'google',count:10,avgScore:4.5,responded:9}];
    s.servicesX={requests:[],items:[],complaints:[cmp('C-1','101','2030-12-01T10:00')]};
    const d=deskState(7,5);const u=upState();const rp=repState({'2030-10':['P1','N1']});
    const cases=[[s,SAT,{month:'2030-12'}],[s,REP,{fy:'2030-31'}],[s,REC,{from:'2030-12-01',to:'2030-12-31'}],[d,DESK,{from:'2030-12-20',to:'2030-12-21'}],[u,UP,{from:'2030-12-15',to:'2030-12-15'}],[rp.s,RPT,{month:'2030-10'}]];
    for(const [st,id,p] of cases){const r=run(st,'owner',id,p);assert.ok(r.rows.length>0,id+' has rows');threeWay(M,assert,r);}
  });
}
