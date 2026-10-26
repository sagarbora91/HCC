// W4-10 sync area "services": guest services (requests, complaints, lost & found), messaging (templates, consent, simulated outbox)
// and shift handover (checklists, desk notes, carry-forward). Fake server shaped like the .NET DTOs (Hcc.Contracts GuestServices /
// Messaging / Handover); the real runtime; assertions on slices, requests, keys, versions and reloads.
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

const ver=n=>'AAAAAAAA'+String(n).padStart(3,'0')+'=';
const utc=(h,m)=>`2026-10-24T${String(h).padStart(2,'0')}:${String(m).padStart(2,'0')}:00.000Z`; // 04:30Z = 10:00 IST
const stays=[{stayId:501,reservationId:1201,guestId:77,roomNo:'105',guestName:'Journey Test',status:'InHouse'},{stayId:490,reservationId:1190,guestId:70,roomNo:'204',guestName:'Earlier Guest',status:'CheckedOut'}];
const request=(id,over={})=>({requestId:id,stayId:501,roomNo:'105',guestName:'Journey Test',requestType:'Extra towels',detail:'Two',dept:'Housekeeping',assigneeId:null,priority:'Normal',status:'Open',
  createdUtc:utc(4,0),dueUtc:utc(4,20),scheduledUtc:null,startedUtc:null,doneUtc:null,guestNote:null,cancelReason:null,sla:{tone:'blue',label:'Due in 20 min',minutesLeft:20},version:ver(id),...over});
const complaint=(id,over={})=>({complaintId:id,stayId:501,roomNo:'105',guestName:'Journey Test',category:'Noise',severity:'High',description:'Loud corridor',ownerId:1,status:'Open',rootCause:null,
  recoveryAction:null,followUpOn:null,followUpDue:false,followUp:null,createdUtc:utc(3,0),resolvedUtc:null,closedUtc:null,recovery:null,recoveryStatus:'None',version:ver(100+id),...over});
const item=(id,over={})=>({itemId:id,description:'Gold ring',category:'Jewellery',valuable:true,foundWhere:'Room 204',roomNo:'204',stayId:490,foundUtc:utc(2,0),foundDate:'2026-10-24',foundBy:2,
  storage:'Front office safe',status:'Stored',disposeOn:'2027-04-22',disposable:false,claim:null,handedOverTo:null,disposalMethod:null,version:ver(200+id),...over});
const hist=[{recordId:11,entries:[{atUtc:utc(4,0),by:2,what:'Logged · assigned to Asha'}]}];
const tpl=(id,over={})=>({templateId:id,name:'Front desk morning',desk:'Front desk',shift:'Morning',roles:['REC','CSH'],items:[{key:'i1',text:'Check keys',mandatory:true,due:'09:00',photo:false,link:null},{key:'i2',text:'Count float',mandatory:false,due:null,photo:true,link:null}],
  active:true,templateVersion:3,version:ver(300+id),...over});
const run=(over={})=>({runId:900,templateId:7,templateVersion:3,name:'Front desk morning',desk:'Front desk',shift:'Morning',roles:['REC','CSH'],date:'2026-10-24',status:'Open',started:true,startedBy:2,
  submittedUtc:null,submittedBy:null,reopenReason:null,items:[{key:'i1',text:'Check keys',mandatory:true,due:'09:00',photo:false,link:null,done:true,doneBy:2,doneUtc:utc(3,40),note:'ok',photoName:null,overdue:false},
  {key:'i2',text:'Count float',mandatory:false,due:null,photo:true,link:null,done:false,doneBy:null,doneUtc:null,note:null,photoName:null,overdue:false}],progress:{total:2,done:1,pct:50,mandatory:1,mandatoryDone:1,overdue:0,late:false},...over});
const note=(over={})=>({noteId:60,ref:'HOVN-FRONT-2026-10-24-M',desk:'Front desk',date:'2026-10-24',shift:'Morning',toShift:'Evening',authorId:2,authorName:'Asha',writtenUtc:utc(9,0),issues:'AC in 105',nothing:false,
  keys:'Spare at desk',guests:'Wake-up 05:00',cash:{amountPaise:500000,note:'float'},carry:[{carryId:81,text:'Call plumber',done:false,doneBy:null,doneUtc:null},{carryId:82,text:'Restock',done:true,doneBy:3,doneUtc:utc(9,30)}],
  status:'Submitted',ackBy:null,ackUtc:null,ackRemark:null,version:ver(60),...over});
const msg=(id,over={})=>({messageId:id,id:'MSG-'+id,reservationId:1201,bookingNo:'HCC-1201',guestName:'Journey Test',templateCode:'checkin-welcome',channel:'WhatsApp',to:'••••••1111',subject:null,body:'Welcome',
  status:'Sent',blockedReason:null,queuedUtc:utc(4,10),timeline:[{status:'Queued',atUtc:utc(4,10)},{status:'Sent',atUtc:utc(4,11)}],ruleId:'R3',retryOf:null,retriedBy:null,smsSegments:null,simulated:true,
  outcome:'Read',boostMinutes:0,guestId:77,...over});

export async function register({test,assert,M}){
  const S=await loadSync();
  // Only this area (+ core, reservations, frontdesk: bookings needs guests): other W4 areas load their own routes, which this fake server does not serve.
  const mine={areas:S.AREAS.filter(a=>['core','reservations','frontdesk','services'].includes(a.id))};
  const page=(items)=>({page:1,pageSize:100,total:items.length,items,ownOnly:false});
  const names=['LogServiceRequest','AssignServiceRequest','StartServiceRequest','CompleteServiceRequest','CancelServiceRequest','LogComplaint','UpdateComplaint','ResolveComplaint','RecordComplaintFollowUp',
    'OfferRecoveryDiscount','LogLostItem','ClaimLostItem','HandOverLostItem','DisposeLostItem','SaveGuestServiceSettings','SendMessage','RunMessagingTriggers','SimulateMessageDelivery','RetryMessage',
    'SetMessagingConsent','UpdateMessageTemplate','UpdateMessageRule','SaveHandoverTemplate','ToggleHandoverTemplate','TickChecklistItem','SubmitChecklistRun','ReopenChecklistRun','SubmitHandoverNote',
    'AcknowledgeHandoverNote','CompleteCarryItem'];
  const routes={
    'GET /guest-services/requests':()=>page([request(11,{assigneeId:5,status:'Assigned'})]),
    'GET /guest-services/complaints':()=>page([complaint(21,{recovery:{amountPaise:50000,reason:'Service recovery · 21',approvalId:null,chargeId:9001,atUtc:utc(4,5),by:1},recoveryStatus:'Posted',
      followUpOn:'2026-10-25',followUp:{outcome:'Ok',satisfied:true,atUtc:utc(4,6),by:1}})]),
    'GET /guest-services/lost-items':()=>page([item(31),item(32,{status:'Handed over',handedOverTo:'Owner Ring',claim:{name:'A',phone:'9000011111',idType:'Passport (fictional)',idNote:'last4 1234',matchNote:'ring',atUtc:utc(3,0),by:1}})]),
    'GET /guest-services/settings':()=>({slaMinutes:{'Extra towels':20,Taxi:30},retentionDays:90,valuableRetentionDays:180,version:ver(400)}),
    'GET /guest-services/stays':()=>({items:stays}),
    'GET /guest-services/histories/request':()=>hist,
    'GET /guest-services/histories/complaint':()=>[{recordId:21,entries:[{atUtc:utc(3,0),by:1,what:'Logged'}]}],
    'GET /guest-services/histories/item':()=>[{recordId:32,entries:[{atUtc:utc(3,0),by:1,what:'Logged'},{atUtc:utc(5,0),by:1,what:'Handed over to Owner Ring'}]}],
    'GET /messaging/messages':()=>({items:[msg(6,{templateCode:null,ruleId:null,channel:'SMS',status:'Failed',outcome:'Failed',timeline:[{status:'Queued',atUtc:utc(4,12)},{status:'Failed',atUtc:utc(4,14)}],queuedUtc:utc(4,12)}),msg(5)],total:2,page:1,pageSize:100}),
    'GET /messaging/templates':()=>({templates:[{code:'checkin-welcome',name:'Check-in welcome',channel:'WhatsApp',subject:'Welcome to {hotel}',body:'Welcome to {hotel}, {firstName}!',version:ver(500)}],placeholders:['guest']}),
    'GET /messaging/rules':()=>[{ruleId:'R3',event:'checked-in',templateCode:'checkin-welcome',channel:'WhatsApp',enabled:true,version:ver(600)}],
    'GET /messaging/consents':()=>({consents:[{guestId:77,whatsApp:true,sms:false,email:true,optOut:false,updatedUtc:utc(4,0),version:ver(700)}],guests:[{reservationId:1201,guestId:77}]}),
    'GET /handover/checklists':()=>({date:'2026-10-24',currentShift:'Morning',runs:[run(),{...run({runId:null,started:false,templateId:8,name:'Housekeeping evening',desk:'Housekeeping',shift:'Evening',roles:['HKP'],items:[]})}]}),
    'GET /handover/templates':()=>({templates:[tpl(7),tpl(9,{name:'Old list',active:false,roles:['MNT']})]}),
    'GET /handover/notes':()=>({notes:[note()],incomingNoteIds:[60]}),
  };
  for(const n of names)routes['POST /commands/'+n]=()=>({});
  const srv=fakeServer(routes);
  const rt=await startRuntime(S,srv,mine);
  const st=rt.get().state;
  const bootErrors=rt.get().errors;

  test('[W4-10] Loaders fill servicesX / messagingX / handoverX with prototype-shaped data (ids, IST clock, booking = reservation id)',()=>{
    assert.deepEqual(srv.unrouted,[],srv.unrouted.join(','));
    const x=st.servicesX,r=x.requests[0];
    assert.deepEqual([r.id,r.booking,r.room,r.guest,r.type,r.dept,r.assignee,r.status,r.priority,r.createdAt,r.dueAt,r.by],['11','1201','105','Journey Test','Extra towels','Housekeeping','5','Assigned','Normal','2026-10-24T09:30','2026-10-24T09:50','2']);
    assert.deepEqual(r.log,[{at:'2026-10-24T09:30',by:'2',what:'Logged · assigned to Asha'}]);
    const c=x.complaints[0];
    assert.deepEqual([c.id,c.booking,c.owner,c.severity,c.followUpOn,c.recovery.amount,c.recovery.lineId,c.followUp.satisfied],['21','1201','1','High','2026-10-25',50000,'9001',true]);
    const [i1,i2]=x.items;
    assert.deepEqual([i1.id,i1.valuable,i1.booking,i1.foundAt,i1.storage,i2.status,i2.handover.to,i2.handover.at,i2.claim.idNote],['31',true,'1190','2026-10-24T07:30','Front office safe','Handed over','Owner Ring','2026-10-24T10:30','last4 1234']);
    assert.deepEqual([x.settings.retentionDays,x.settings.valuableRetentionDays,x.settings.sla['Extra towels']],[90,180,20]);
    const m=st.messagingX;
    assert.deepEqual(m.outbox.map(o=>o.id),['MSG-5','MSG-6'],'oldest first (the screens reverse)');
    assert.deepEqual([m.outbox[0].to,m.outbox[0].outcome,m.outbox[0].guestKey,m.outbox[0].booking,m.outbox[0].queuedAt,m.outbox[0].rule],['••••••1111','Read','77','1201','2026-10-24T09:40','R3']);
    assert.deepEqual([m.outbox[1].template,m.outbox[1].outcome,m.outbox[1].channel],['manual','Failed','SMS']);
    assert.deepEqual(m.fired,['R3:1201']);
    assert.deepEqual(m.consent['77'],{whatsapp:true,sms:false,email:true,optOut:false,updatedAt:'2026-10-24T09:30'});
    assert.deepEqual(m.consent['B:1201'],m.consent['77'],'aliased by booking for registered guests');
    assert.deepEqual([m.templates[0].id,m.rules[0].id,m.rules[0].event],['checkin-welcome','R3','checked-in']);
    assert.deepEqual(m.docs,[]);
    const h=st.handoverX;
    assert.deepEqual(h.templates.map(t=>[t.id,t.active,t.roles.join('/')]).sort(),[['7',true,'Reception/Cashier'],['8',true,'Housekeeper'],['9',false,'Maintenance']]);
    assert.equal(h.runs.length,1,'only started runs');
    const rn=h.runs[0];
    assert.deepEqual([rn.id,rn.template,rn.status,rn.date,rn.items[0].done,rn.items[0].by,rn.items[0].at,rn.items[0].note,rn.items[1].photo],['HOVR-7-2026-10-24','7','Open','2026-10-24',true,'2','2026-10-24T09:10','ok',true]);
    const n=h.notes[0];
    assert.deepEqual([n.id,n.author,n.cash,n.carry.map(k=>k.id+':'+k.done),n.status,n.at],['HOVN-FRONT-2026-10-24-M','2',{amount:500000,note:'float'},['HOVN-FRONT-2026-10-24-M-c1:false','HOVN-FRONT-2026-10-24-M-c2:true'],'Submitted','2026-10-24T14:30']);
    assert.deepEqual(bootErrors,{});
  });

  const P=await loadPages();const shown=[P.renderPages(st,'1',false),P.renderPages(st,'1',true)];
  test('[W4-10] Every desktop and phone page renders on the loaded server data',()=>{assert.deepEqual(shown.map(x=>x.errors),[{},{}]);});

  // Prototype parity: the same request through execute() has the same fields as the mapped one.
  const proto=(()=>{let s=M.seed();s=M.execute(s,'owner',{type:'BOOK',guest:'Parity',room:'105',arrival:s.date,departure:'2026-10-26',people:1});const id=s.bookings.at(-1).id;
    s=M.execute(s,'owner',{type:'REGISTER',id});s=M.execute(s,'owner',{type:'CHECKIN',id});return M.execute(s,'owner',{type:'GSV_REQUEST_CREATE',id,requestType:'Extra towels',detail:'Two'}).servicesX.requests.at(-1);})();
  test('[W4-10] A mapped request has the same fields as the prototype request',()=>{
    const keys=o=>Object.keys(o).sort().filter(k=>!['scheduledFor','startedAt','doneAt','note','cancelReason','assignee'].includes(k));
    assert.deepEqual(keys(st.servicesX.requests[0]),keys(proto));assert.equal(proto.dept,st.servicesX.requests[0].dept);assert.equal(proto.dueAt.length,st.servicesX.requests[0].dueAt.length);
  });

  // Commands: path, body, key, expectedVersion, reload.
  const cases=[
    [{type:'GSV_REQUEST_CREATE',id:'1201',requestType:'Wake-up call',detail:'',dept:'Front desk',priority:'Urgent',assignee:'2',time:'05:00',key:'k-create'},'LogServiceRequest',
      {stayId:501,requestType:'Wake-up call',dept:'Front desk',priority:'Urgent',time:'05:00',assigneeId:2},undefined,'/guest-services/requests'],
    [{type:'GSV_REQUEST_ASSIGN',id:'11',assignee:'6',key:'k-assign'},'AssignServiceRequest',{requestId:11,assigneeId:6,expectedVersion:ver(11)},ver(11),'/guest-services/requests'],
    [{type:'GSV_REQUEST_START',id:'11',key:'k-start'},'StartServiceRequest',{requestId:11,expectedVersion:ver(11)},ver(11),'/guest-services/requests'],
    [{type:'GSV_REQUEST_COMPLETE',id:'11',note:'Towels delivered',key:'k-complete'},'CompleteServiceRequest',{requestId:11,note:'Towels delivered',expectedVersion:ver(11)},ver(11),'/guest-services/requests'],
    [{type:'GSV_REQUEST_CANCEL',id:'11',reason:'Guest left',key:'k-cancel'},'CancelServiceRequest',{requestId:11,reason:'Guest left',expectedVersion:ver(11)},ver(11),'/guest-services/requests'],
    [{type:'GSV_COMPLAINT_LOG',booking:'1201',guest:'Journey Test',category:'Noise',severity:'High',description:'Loud corridor',owner:'1',key:'k-clog'},'LogComplaint',
      {stayId:501,guestName:'Journey Test',category:'Noise',severity:'High',description:'Loud corridor',ownerId:1},undefined,'/guest-services/complaints'],
    [{type:'GSV_COMPLAINT_UPDATE',id:'21',owner:'3',key:'k-cu'},'UpdateComplaint',{complaintId:21,ownerId:3,expectedVersion:ver(121)},ver(121),'/guest-services/complaints'],
    [{type:'GSV_COMPLAINT_RESOLVE',id:'21',rootCause:'Late party',recoveryAction:'Apology',key:'k-cr'},'ResolveComplaint',{complaintId:21,rootCause:'Late party',recoveryAction:'Apology',expectedVersion:ver(121)},ver(121),'/guest-services/complaints'],
    [{type:'GSV_COMPLAINT_RECOVERY',id:'21',amount:50000,key:'k-rec'},'OfferRecoveryDiscount',{complaintId:21,amountPaise:50000,expectedVersion:ver(121)},ver(121),'/guest-services/complaints'],
    [{type:'GSV_COMPLAINT_FOLLOWUP',id:'21',outcome:'Happy',satisfied:true,key:'k-fu'},'RecordComplaintFollowUp',{complaintId:21,outcome:'Happy',satisfied:true,expectedVersion:ver(121)},ver(121),'/guest-services/complaints'],
    [{type:'GSV_LF_LOG',description:'Blue scarf',category:'Clothing',foundWhere:'Lobby',room:'105',storage:'Housekeeping store',foundBy:'2',key:'k-lf'},'LogLostItem',
      {description:'Blue scarf',category:'Clothing',foundWhere:'Lobby',storage:'Housekeeping store',roomNo:'105',foundBy:2},undefined,'/guest-services/lost-items'],
    [{type:'GSV_LF_CLAIM',id:'31',name:'Asha Rao',phone:'9000011111',idType:'Passport (fictional)',idNote:'last4 1234',matchNote:'Gold ring',key:'k-claim'},'ClaimLostItem',
      {itemId:31,name:'Asha Rao',phone:'9000011111',idType:'Passport (fictional)',idNote:'last4 1234',matchNote:'Gold ring',expectedVersion:ver(231)},ver(231),'/guest-services/lost-items'],
    [{type:'GSV_LF_HANDOVER',id:'31',to:'Asha',note:'',key:'k-ho'},'HandOverLostItem',{itemId:31,to:'Asha',expectedVersion:ver(231)},ver(231),'/guest-services/lost-items'],
    [{type:'GSV_LF_DISPOSE',id:'31',method:'Donated',note:'',key:'k-disp'},'DisposeLostItem',{itemId:31,method:'Donated',expectedVersion:ver(231)},ver(231),'/guest-services/lost-items'],
    [{type:'GSV_SETTINGS',sla:{Taxi:25},retentionDays:60,valuableRetentionDays:200,key:'k-set'},'SaveGuestServiceSettings',{slaMinutes:{Taxi:25},retentionDays:60,valuableRetentionDays:200,expectedVersion:ver(400)},ver(400),'/guest-services/requests'],
    [{type:'MSG_SEND',booking:'1201',channel:'WhatsApp',body:'Hello {firstName}',template:'checkin-welcome',key:'k-send'},'SendMessage',{reservationId:1201,channel:'WhatsApp',body:'Hello {firstName}',templateCode:'checkin-welcome'},undefined,'/messaging/messages'],
    [{type:'MSG_RUN_RULES',only:['R3:1201'],key:'k-run'},'RunMessagingTriggers',{},undefined,'/messaging/messages'],
    [{type:'MSG_SIMULATE',id:'MSG-5',key:'k-sim'},'SimulateMessageDelivery',{messageId:5},undefined,'/messaging/messages'],
    [{type:'MSG_SIMULATE',key:'k-sim-all'},'SimulateMessageDelivery',{},undefined,'/messaging/messages'],
    [{type:'MSG_RETRY',id:'MSG-6',key:'k-retry'},'RetryMessage',{messageId:6},undefined,'/messaging/messages'],
    [{type:'MSG_CONSENT',guestKey:'B:1201',whatsapp:false,sms:true,email:true,optOut:false,key:'k-cons'},'SetMessagingConsent',{guestId:77,whatsApp:false,sms:true,email:true,optOut:false},undefined,'/messaging/consents'],
    [{type:'MSG_CONSENT',guestKey:'77',optOut:true,key:'k-cons2'},'SetMessagingConsent',{guestId:77,optOut:true},undefined,'/messaging/consents'],
    [{type:'MSG_TEMPLATE',id:'checkin-welcome',body:'Welcome {firstName} to {hotel}',subject:'Hi {hotel}',channel:'SMS',key:'k-tpl'},'UpdateMessageTemplate',
      {code:'checkin-welcome',body:'Welcome {firstName} to {hotel}',subject:'Hi {hotel}',channel:'SMS',expectedVersion:ver(500)},ver(500),'/messaging/templates'],
    [{type:'MSG_RULE',id:'R3',enabled:false,key:'k-rule'},'UpdateMessageRule',{ruleId:'R3',enabled:false,expectedVersion:ver(600)},ver(600),'/messaging/rules'],
    [{type:'HOV_TEMPLATE_SAVE',template:{id:'7',name:'Front desk morning',desk:'Front desk',shift:'Morning',roles:['Reception'],items:[{id:'i1',text:'Check keys',mandatory:true,due:'09:00'}]},key:'k-ts'},'SaveHandoverTemplate',
      {name:'Front desk morning',desk:'Front desk',shift:'Morning',roles:['REC'],items:[{text:'Check keys',mandatory:true,due:'09:00',photo:false}],templateId:7,expectedVersion:ver(307)},ver(307),'/handover/checklists'],
    [{type:'HOV_TEMPLATE_SAVE',template:{name:'New list',desk:'Maintenance',shift:'Night',roles:['Maintenance'],items:[{text:'Check boiler'}]},key:'k-tn'},'SaveHandoverTemplate',
      {name:'New list',desk:'Maintenance',shift:'Night',roles:['MNT'],items:[{text:'Check boiler',mandatory:false,photo:false}]},undefined,'/handover/checklists'],
    [{type:'HOV_TEMPLATE_TOGGLE',id:'9',key:'k-tt'},'ToggleHandoverTemplate',{templateId:9,expectedVersion:ver(309)},ver(309),'/handover/templates'],
    [{type:'HOV_ITEM',template:'7',item:'i2',done:true,note:'n',photo:'p.jpg',key:'k-item'},'TickChecklistItem',{templateId:7,item:'i2',done:true,note:'n',photoName:'p.jpg'},undefined,'/handover/checklists'],
    [{type:'HOV_RUN_SUBMIT',template:'7',key:'k-rs'},'SubmitChecklistRun',{templateId:7},undefined,'/handover/checklists'],
    [{type:'HOV_RUN_REOPEN',template:'7',date:'2026-10-24',reason:'Missed item',key:'k-rr'},'ReopenChecklistRun',{templateId:7,reason:'Missed item',date:'2026-10-24'},undefined,'/handover/checklists'],
    [{type:'HOV_NOTE_SUBMIT',desk:'Front desk',shift:'Morning',issues:'AC',keys:'k',guests:'g',nothing:false,cash:{amount:500000,note:'float'},carry:['Call plumber',' '],key:'k-ns'},'SubmitHandoverNote',
      {desk:'Front desk',shift:'Morning',issues:'AC',nothing:false,keys:'k',guests:'g',cash:{amountPaise:500000,note:'float'},carry:['Call plumber']},undefined,'/handover/notes'],
    [{type:'HOV_NOTE_ACK',id:'HOVN-FRONT-2026-10-24-M',remark:'Seen',key:'k-ack'},'AcknowledgeHandoverNote',{noteId:60,remark:'Seen'},undefined,'/handover/notes'],
    [{type:'HOV_CARRY_DONE',note:'HOVN-FRONT-2026-10-24-M',item:'HOVN-FRONT-2026-10-24-M-c1',key:'k-carry'},'CompleteCarryItem',{noteId:60,carryId:81},undefined,'/handover/notes'],
  ];
  const results=[];
  for(const [cmd,name,body,version,reloadPath] of cases){
    srv.reset();const r=await rt.run(cmd);
    const sent=srv.commands(name);
    results.push({cmd,name,body,r,call:sent[0],count:sent.length,reloaded:srv.gets(reloadPath).length});
  }
  test('[W4-10] Each mapped command sends the right route, body, Idempotency-Key and expectedVersion, then reloads its slice',()=>{
    for(const x of results){
      const tag=`${x.cmd.type} → ${x.name}`;
      assert.ok(x.r.ok,tag+': '+x.r.text);assert.equal(x.count,1,tag);
      assert.equal(x.call.key,x.cmd.key,tag+' key');
      const {expectedVersion,...rest}=x.call.body;const {expectedVersion:ev,...want}=x.body;
      assert.deepEqual(rest,JSON.parse(JSON.stringify(want)),tag+' body');
      assert.equal(expectedVersion,ev,tag+' expectedVersion');
      assert.ok(x.reloaded>=1,tag+' reload');
    }
  });

  srv.reset();
  const refusals=[await rt.run({type:'GSV_REQUEST_COMPLETE',id:'11',note:'x'}),await rt.run({type:'GSV_REQUEST_CREATE',id:'9999',requestType:'Taxi'}),await rt.run({type:'MSG_CONSENT',guestKey:'B:5555',optOut:true}),
    await rt.run({type:'HOV_CLOCK',minutes:30}),await rt.run({type:'MSG_DOC_EXTRACT',sample:'x'}),await rt.run({type:'HOV_NOTE_ACK',id:'HOVN-NONE'})];
  const refusedCalls=srv.calls.filter(c=>c.method==='POST').length;
  test('[W4-10] Prechecks and unknown records are refused without a request; unmapped commands are listed and refused',()=>{
    assert.deepEqual(refusals.map(r=>r.ok),[false,false,false,false,false,false]);
    assert.equal(refusals[0].reason,'precheck');assert.match(refusals[1].text,/in-house guests/);assert.match(refusals[2].text,/register the guest/);
    assert.equal(refusals[3].reason,'unmapped');assert.equal(refusals[4].reason,'unmapped');assert.match(refusals[5].text,/not found/i);
    assert.equal(refusedCalls,0);
    const un=Object.keys(S.AREAS.find(a=>a.id==='services').unmapped).sort();
    assert.deepEqual(un,['HOV_CLOCK','MSG_DOC_CONFIRM','MSG_DOC_DISCARD','MSG_DOC_EXTRACT']);
  });

  // Housekeeper: only own requests, no complaints/history/stays/settings (403 tolerated), handover for HKP only.
  const hkSrv=fakeServer({...routes,
    'GET /me/capabilities':()=>capabilities({roles:['HKP'],grants:[['PERM-HK-001','A'],['PERM-HK-003','A']]}),
    'GET /guest-services/requests':()=>({...page([request(12,{assigneeId:1})]),ownOnly:true}),
    'GET /guest-services/complaints':()=>reply(403,{code:'FORBIDDEN',message:'Access denied.'}),
    'GET /guest-services/stays':()=>reply(403,{code:'FORBIDDEN',message:'Access denied.'}),
    'GET /guest-services/settings':()=>reply(403,{code:'FORBIDDEN',message:'Access denied.'}),
    'GET /guest-services/histories/request':()=>reply(403,{code:'FORBIDDEN',message:'Access denied.'}),
    'GET /guest-services/histories/complaint':()=>reply(403,{code:'FORBIDDEN',message:'Access denied.'}),
    'GET /guest-services/histories/item':()=>reply(403,{code:'FORBIDDEN',message:'Access denied.'}),
    'GET /guest-services/lost-items':()=>({...page([item(40,{claim:null})]),ownOnly:true}),
    'GET /handover/templates':()=>reply(403,{code:'FORBIDDEN',message:'Access denied.'}),
    'GET /handover/notes':()=>({notes:[note({desk:'Housekeeping',ref:'HOVN-HOUSEKEEPING-2026-10-24-M',cash:{amountPaise:null,note:null}})],incomingNoteIds:[]})});
  const hk=await startRuntime(S,hkSrv,mine);const hs=hk.get();
  test('[W4-10] A housekeeper loads only what the server lets them see: own requests, no complaints, no messaging reads, masked cash stays absent',()=>{
    assert.deepEqual(hs.state.servicesX.requests.map(r=>[r.id,r.booking,r.by]),[['12','','']]);
    assert.deepEqual(hs.state.servicesX.complaints,[]);assert.equal(hs.state.servicesX.settings,undefined);
    assert.equal(hkSrv.calls.filter(c=>c.path.startsWith('/messaging')).length,0);
    assert.equal(hs.state.handoverX.notes[0].cash,undefined);
    assert.deepEqual(Object.keys(hs.errors??{}),[]);
  });

  // Offline restart: cached slices visible, commands refused.
  const cache=S.createMemoryCache();const a=await startRuntime(S,srv,{cache,...mine});void a;
  srv.down=true;const off=await startRuntime(S,srv,{cache,...mine});const ro=await off.run({type:'MSG_RETRY',id:'MSG-6'});
  test('[W4-10] Offline restart shows the cached outbox read-only and refuses commands',()=>{
    assert.equal(off.get().readOnly,true);assert.deepEqual(off.get().state.messagingX.outbox.map(m=>m.id),['MSG-5','MSG-6']);assert.equal(ro.reason,'read-only');
  });
}
