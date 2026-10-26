// W4-11 area "channels" (sync/channels.ts): yield & tariffs, OTA channels, booking-website queue. Fake server shaped like the .NET DTOs
// (Hcc.Contracts Yield / Ota / WebBooking), the real runtime, requests asserted, slices compared with the prototype's own figures.
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities,ownErrors} from './_sync-harness.mjs';

const V=n=>'AAAAAAAAB'+n;
const calendar={from:'2026-10-24',nights:1,cells:[
  {roomTypeId:1,roomType:'Classic',date:'2026-10-24'},{roomTypeId:2,roomType:'Deluxe',date:'2026-10-24'},{roomTypeId:3,roomType:'Suite',date:'2026-10-24'}],
  summary:{averageBar7Paise:300000,highDemandNights:1,pendingNights:2,pendingProposals:1}};
const state={
  plans:[{planCode:'BAR',name:'Best available rate',adjBps:0,minNights:1,sellOnDays:null,dayUseFrom:null,dayUseTo:null,closeAtOcc:null,active:true,note:'ref',version:V('P1')},
    {planCode:'WKND',name:'Weekend stay',adjBps:-500,minNights:2,sellOnDays:[5,6,0],active:true,note:'wk',version:V('P2')},
    {planCode:'DAYUSE',name:'Day-use',adjBps:-4500,minNights:0,dayUseFrom:'10:00',dayUseTo:'17:00',closeAtOcc:90,active:true,note:'du',version:V('P3')}],
  seasons:[{seasonId:5,name:'Diwali',from:'2026-11-05',to:'2026-11-12',adjBps:2000,roomTypeIds:[2,3],priority:2,version:V('S5')}],
  dow:[0,0,0,0,0,1000,1000],floorBps:-2000,ceilBps:4000,roundToPaise:5000,settingsVersion:V('ST'),
  occupancyRules:[{ruleId:8,op:'>=',occ:80,adjBps:1000,version:V('O8')}],
  restrictions:[{restrictionId:9,code:'R9',roomTypeId:null,from:'2026-11-05',to:'2026-11-08',minStay:2,closedToArrival:false,note:'Diwali',version:V('R9')}],
  overrides:[{roomTypeId:2,night:'2026-10-30',ratePaise:350000,reason:'Event',actorId:1,setUtc:'2026-10-24T04:00:00.000Z'}],
  forecast:[{night:'2026-10-30',roomsSold:12}],
  history:[{historyId:1,atUtc:'2026-10-24T03:00:00.000Z',actorId:1,area:'Guardrails',before:'a',after:'b',reason:'r'}]};
const publishes=[{publishId:3,from:'2026-10-25',to:'2026-10-26',actorId:1,publishedUtc:'2026-10-24T04:10:00.000Z',reason:'',proposalId:4,
  rows:[{roomTypeId:2,night:'2026-10-25',beforePaise:200000,afterPaise:220000,closing:false},{roomTypeId:2,night:'2026-10-27',beforePaise:200000,afterPaise:200000,closing:true}]}];
const proposals=[{proposalId:4,status:'Pending',from:'2026-10-25',to:'2026-10-26',roomTypeIds:[2],changedNights:2,proposedBy:2,proposedUtc:'2026-10-24T04:00:00.000Z',
  expiresUtc:'2026-10-25T04:00:00.000Z',reason:'Weekend',decidedBy:null,decidedUtc:null,decisionNote:null,publishId:null,version:V('Q4')},
  {proposalId:5,status:'Stale',from:'2026-10-27',to:'2026-10-28',roomTypeIds:[1,2],changedNights:1,proposedBy:2,proposedUtc:'2026-10-24T04:00:00.000Z',
  expiresUtc:'2026-10-25T04:00:00.000Z',reason:'',decidedBy:null,decidedUtc:null,decisionNote:null,publishId:null,version:V('Q5')}];

const channelsBody=[{channelId:1,code:'BKG',name:'Booking.test',status:'Connected',version:V('C1'),commissionBps:1800,markupBps:500,lastSync:'2026-10-24T04:00:00.000Z',statusNote:'ok',pushDue:false,pendingMessages:1,
  mapping:[{roomTypeId:2,roomType:'Deluxe',channelCode:'DLX',stopSell:true},{roomTypeId:1,roomType:'Classic',channelCode:'',stopSell:false}]}];
const msg=(id,status,over={})=>({messageId:id,version:V('M'+id),channelId:1,channel:'Booking.test',externalId:'x'+id,kind:'New',channelRef:'BK'+id,guest:'Anita',phone:null,email:'a@x.test',
  channelRoomCode:'DLX',roomTypeId:2,roomType:'Deluxe',arrival:'2026-10-26',departure:'2026-10-28',adults:2,children:0,amountPaise:400000,receivedAt:'2026-10-24T04:20:00.000Z',status,
  reservationId:null,publicNo:null,room:null,reason:null,...over});
const messages=[msg(11,'Pending'),msg(12,'Queued'),msg(13,'Rejected',{reason:'No'})];
const webSummary=[{requestId:21,requestNo:'WEB-21',status:'Pending'},{requestId:22,requestNo:'WEB-22',status:'Confirming'}];
const webDetail=(id,over={})=>({requestId:id,requestNo:'WEB-'+id,status:'Pending',guest:'Ravi',phone:'+91 90000 22222',email:'r@x.test',notes:null,roomTypeId:2,roomType:'Deluxe',arrival:'2026-10-26',departure:'2026-10-27',
  adults:2,children:0,netPaise:200000,taxPaise:10000,totalPaise:210000,quotedNights:[],createdAt:'2026-10-24T04:30:00.000Z',reservationId:null,reservationNo:null,reason:null,decidedBy:null,version:V('W'+id),depositLinks:[],...over});

export async function register({test,assert}){
  const S=await loadSync();
  const srv=fakeServer({
    'GET /yield/calendar':()=>calendar,'GET /yield/config':()=>state,'GET /yield/publishes':()=>publishes,'GET /yield/proposals':()=>proposals,
    'GET /ota/channels':()=>channelsBody,'GET /ota/messages':()=>messages,'GET /ota/pushes':()=>[{pushId:7,channelId:1,channel:'Booking.test',requestedAt:'2026-10-24T04:05:00.000Z',rows:14,closed:2,summary:'14 rows',status:'Sent'}],
    'GET /web-booking/requests':()=>({requests:webSummary,pending:2}),'GET /web-booking/requests/:id':({params})=>webDetail(Number(params.id)),
  });
  const cmdRoutes=['SaveYieldPlan','SaveYieldSeason','SetYieldDayOfWeek','SaveYieldOccupancyRule','SetYieldGuardrails','SaveYieldRestriction','SetYieldOverride','PublishYieldTariffs','ProposeYieldPublish','DecideYieldProposal',
    'SetChannelStatus','SaveChannel','SetChannelStopSell','PushChannelRates','PullChannelMessages','AcceptChannelMessage','RejectChannelMessage','ConfirmWebRequest','DeclineWebRequest'];
  for(const n of cmdRoutes)srv.on('POST /commands/'+n,()=>({ok:true}));
  const rt=await startRuntime(S,srv);
  const st=rt.get().state;const errs=rt.get().errors;

  test('[W4-11 yield] yieldX is built from calendar/config/publishes in the prototype shape; ids and versions are learned',()=>{
    assert.deepEqual(ownErrors(errs,"channels"),{});
    const y=st.yieldX;
    assert.deepEqual(y.plans.map(p=>[p.id,p.code,p.adjBps,p.minNights,p.nights,p.dayUse]),[['BAR','BAR',0,1,undefined,undefined],['WKND','WKND',-500,2,[5,6,0],undefined],['DAYUSE','DAYUSE',-4500,0,undefined,{from:'10:00',to:'17:00',closeAtOcc:90}]]);
    assert.deepEqual(y.seasons,[{id:'5',name:'Diwali',from:'2026-11-05',to:'2026-11-12',adjBps:2000,types:['Deluxe','Suite'],priority:2}]);
    assert.deepEqual(y.dow,[0,0,0,0,0,1000,1000]);assert.deepEqual(y.guard,{floorBps:-2000,ceilBps:4000,roundTo:5000});
    assert.deepEqual(y.occ,[{id:'8',op:'>=',occ:80,adjBps:1000}]);
    assert.deepEqual(y.restrictions,[{id:'9',type:'*',from:'2026-11-05',to:'2026-11-08',minStay:2,cta:false,note:'Diwali'}]);
    assert.deepEqual(y.overrides,[{type:'Deluxe',date:'2026-10-30',rate:350000,reason:'Event',actor:'1',at:'2026-10-24T09:30'}]);
    assert.deepEqual(y.forecast,{'2026-10-30':12});
    assert.equal(y.history[0].area,'Guardrails');assert.equal(y.history[0].at,'2026-10-24T08:30');
    assert.deepEqual([y.publishes[0].id,y.publishes[0].approval,y.publishes[0].types,y.publishes[0].rows[1]],['3','4',['Deluxe'],{type:'Deluxe',date:'2026-10-27',before:200000,after:200000,closing:true}]);
    assert.deepEqual(y.owned,{'Deluxe|2026-10-25':220000,'Deluxe|2026-10-27':200000});
    assert.equal(rt.ids.server('roomType','Deluxe'),2);assert.equal(rt.ids.version('yieldPlan','WKND'),V('P2'));assert.equal(rt.ids.version('yieldSettings','x'),V('ST'));
  });
  test('[W4-11 yield] Proposals become kind "yield" approvals; the server\'s Stale shows as Stale, Pending stays Pending',()=>{
    const a=st.approvals;assert.deepEqual(a.map(x=>[x.id,x.kind,x.status,x.actor,x.target,x.expiresAt]),[['4','yield','Pending','2','2026-10-25→2026-10-26','2026-10-25T09:30'],['5','yield','Pending','2','2026-10-27→2026-10-28','2026-10-25T09:30']]);
    assert.equal(a[0].meta.sig,'[]');assert.equal(a[1].meta.sig,'stale');assert.match(a[0].meta.summary,/1 night rates?|2 night rates/);
  });

  const run=async(cmd)=>{srv.reset();const r=await rt.run(cmd);return {r,cmds:srv.calls.filter(c=>c.method==='POST'),gets:srv.calls.filter(c=>c.method==='GET').map(c=>c.path)};};
  const o={};
  o.plan=await run({type:'YLD_PLAN_SAVE',id:'WKND',adjBps:-700,minNights:2,active:true,reason:'test',key:'plan:1'});
  o.seasonNew=await run({type:'YLD_SEASON_SAVE',name:'Holi',from:'2027-03-01',to:'2027-03-05',adjBps:1500,types:['Suite'],priority:1,reason:'',key:'season:new'});
  o.seasonDel=await run({type:'YLD_SEASON_SAVE',id:'5',remove:true,reason:'over',key:'season:rm'});
  o.dow=await run({type:'YLD_DOW_SET',dow:[0,0,0,0,0,500,500],reason:'x',key:'dow:1'});
  o.occ=await run({type:'YLD_OCC_SAVE',id:'8',op:'>=',occ:85,adjBps:1200,key:'occ:1'});
  o.guard=await run({type:'YLD_GUARD_SET',floorBps:-1500,ceilBps:3000,roundTo:10000,key:'guard:1'});
  o.restr=await run({type:'YLD_RESTRICTION_SAVE',roomType:'Deluxe',from:'2026-12-20',to:'2026-12-31',minStay:3,cta:true,note:'NYE',key:'restr:1'});
  o.ovr=await run({type:'YLD_OVERRIDE',roomType:'Deluxe',date:'2026-10-31',rate:null,reason:'clear it',key:'ovr:1'});
  o.ovrNoReason=await run({type:'YLD_OVERRIDE',roomType:'Deluxe',date:'2026-10-31',rate:300000,reason:'',key:'ovr:2'});
  o.pub=await run({type:'YLD_PUBLISH',from:'2026-10-25',to:'2026-10-26',types:['Deluxe','Suite'],reason:'go',key:'pub:1'});
  o.pubNone=await run({type:'YLD_PUBLISH',from:'2026-10-25',to:'2026-10-26',types:[],key:'pub:2'});
  o.prop=await run({type:'YLD_PROPOSE',from:'2026-10-25',to:'2026-10-26',types:['Deluxe'],reason:'wk',key:'prop:1'});
  o.dec=await run({type:'YLD_DECIDE',id:'4',approve:true,key:'yld-decide:4'});

  test('[W4-11 yield] Each YLD_* command posts the right command, body, Idempotency-Key and expectedVersion, then reloads yield + proposals',()=>{
    const one=(x,name)=>{assert.ok(x.r.ok,x.r.text);assert.equal(x.cmds.length,1);assert.equal(x.cmds[0].path,'/commands/'+name);return x.cmds[0];};
    let c=one(o.plan,'SaveYieldPlan');assert.deepEqual(c.body,{planCode:'WKND',adjBps:-700,minNights:2,active:true,reason:'test',expectedVersion:V('P2')});assert.equal(c.key,'plan:1');
    c=one(o.seasonNew,'SaveYieldSeason');assert.deepEqual(c.body,{name:'Holi',from:'2027-03-01',to:'2027-03-05',adjBps:1500,roomTypeIds:[3],priority:1});
    c=one(o.seasonDel,'SaveYieldSeason');assert.deepEqual(c.body,{seasonId:5,remove:true,reason:'over',expectedVersion:V('S5')});
    c=one(o.dow,'SetYieldDayOfWeek');assert.deepEqual(c.body,{dow:[0,0,0,0,0,500,500],reason:'x',expectedVersion:V('ST')});
    c=one(o.occ,'SaveYieldOccupancyRule');assert.deepEqual(c.body,{ruleId:8,op:'>=',occ:85,adjBps:1200,expectedVersion:V('O8')});
    c=one(o.guard,'SetYieldGuardrails');assert.deepEqual(c.body,{floorBps:-1500,ceilBps:3000,roundToPaise:10000,expectedVersion:V('ST')});
    c=one(o.restr,'SaveYieldRestriction');assert.deepEqual(c.body,{roomTypeId:2,from:'2026-12-20',to:'2026-12-31',minStay:3,closedToArrival:true,note:'NYE'});
    c=one(o.ovr,'SetYieldOverride');assert.deepEqual(c.body,{roomTypeId:2,night:'2026-10-31',ratePaise:null,reason:'clear it'});assert.equal(c.key,'ovr:1');
    c=one(o.pub,'PublishYieldTariffs');assert.deepEqual(c.body,{from:'2026-10-25',to:'2026-10-26',roomTypeIds:[2,3],reason:'go'});
    c=one(o.prop,'ProposeYieldPublish');assert.deepEqual(c.body,{from:'2026-10-25',to:'2026-10-26',roomTypeIds:[2],reason:'wk'});
    c=one(o.dec,'DecideYieldProposal');assert.deepEqual(c.body,{proposalId:4,approve:true,expectedVersion:V('Q4')});assert.equal(c.key,'yld-decide:4');
    assert.ok(o.plan.gets.includes('/yield/config'));for(const x of [o.pub,o.dec])assert.ok(x.gets.includes('/yield/config')&&x.gets.includes('/yield/proposals'),x.gets.join(','));
    assert.deepEqual([o.ovrNoReason.r.ok,o.ovrNoReason.r.reason,o.ovrNoReason.cmds.length],[false,'precheck',0]);assert.match(o.ovrNoReason.r.text,/reason for the override/);
    assert.deepEqual([o.pubNone.r.reason,o.pubNone.cmds.length],['precheck',0]);
  });

  // ── OTA ──
  const ch=st.channelsX;
  test('[W4-11 channels] channelsX holds channels (mapping, stop-sell), inbox, simulated outbox, push log and web requests in the prototype shape',()=>{
    assert.deepEqual(ch.channels.map(c=>[c.id,c.name,c.status,c.commissionBps,c.markupBps,c.mapping,c.stopSell,c.lastSync,c.statusNote]),
      [['1','Booking.test','Connected',1800,500,{Deluxe:'DLX'},['Deluxe'],'2026-10-24T09:30','ok']]);
    assert.deepEqual(ch.messages.map(m=>[m.id,m.status,m.kind,m.ref,m.roomType,m.amount,m.receivedAt,m.reason]),[['11','Pending','New','BK11','Deluxe',400000,'2026-10-24T09:50',undefined],['13','Rejected','New','BK13','Deluxe',400000,'2026-10-24T09:50','No']]);
    assert.deepEqual(ch.outbox.map(m=>m.id),['12']);
    assert.deepEqual(ch.pushes.map(p=>[p.id,p.channel,p.rows,p.closed,p.at]),[['7','1',14,2,'2026-10-24T09:35']]);
    assert.deepEqual(ch.webRequests.map(w=>[w.id,w.status,w.guest,w.total,w.net,w.tax,w.booking]),[['21','Pending','Ravi',210000,200000,10000,''],['22','Pending','Ravi',210000,200000,10000,'']]);
    assert.equal(rt.ids.version('channel','1'),V('C1'));assert.equal(rt.ids.version('channelMessage','11'),V('M11'));assert.equal(rt.ids.version('webRequest','21'),V('W21'));
  });

  rt.ids.learn('room','105',7);
  const p={};
  p.status=await run({type:'CHN_CHANNEL_STATUS',channel:'1',action:'pause',key:'chs:1'});
  p.save=await run({type:'CHN_CHANNEL_SAVE',channel:'1',commissionBps:2000,markupBps:300,mapping:{Deluxe:'dlx',Classic:'',Suite:'st-1'},key:'chsave:1'});
  p.stop=await run({type:'CHN_STOP_SELL',channel:'ALL',roomType:'Deluxe',on:true,key:'stop:1'});
  p.push=await run({type:'CHN_PUSH',channel:'1',key:'push:1'});
  p.pull=await run({type:'CHN_PULL',key:'pull:1'});
  p.accept=await run({type:'CHN_ACCEPT',id:'11',room:'105',key:'acc:1'});
  p.acceptAuto=await run({type:'CHN_ACCEPT',id:'11',key:'acc:2'});
  p.acceptBadRoom=await run({type:'CHN_ACCEPT',id:'11',room:'999',key:'acc:3'});
  p.reject=await run({type:'CHN_REJECT',id:'11',reason:'Full house',key:'rej:1'});
  p.rejectNoReason=await run({type:'CHN_REJECT',id:'11',reason:'x',key:'rej:2'});
  p.confirm=await run({type:'CHN_WEB_CONFIRM',id:'21',key:'wc:1'});
  p.decline=await run({type:'CHN_WEB_DECLINE',id:'21',reason:'No rooms',key:'wd:1'});
  p.webReq=await run({type:'CHN_WEB_REQUEST',roomType:'Deluxe',key:'wr:1'});
  test('[W4-11 channels] Each CHN_* command posts the right command, body, key and expectedVersion; accept/confirm also reload bookings',()=>{
    const one=(x,name)=>{assert.ok(x.r.ok,x.r.text);assert.equal(x.cmds.length,1);assert.equal(x.cmds[0].path,'/commands/'+name);return x.cmds[0];};
    let c=one(p.status,'SetChannelStatus');assert.deepEqual(c.body,{channelId:1,action:'pause',expectedVersion:V('C1')});assert.equal(c.key,'chs:1');
    c=one(p.save,'SaveChannel');assert.deepEqual(c.body,{channelId:1,code:'BKG',name:'Booking.test',commissionBps:2000,markupBps:300,mapping:[{roomTypeId:2,channelCode:'DLX'},{roomTypeId:3,channelCode:'ST-1'}],expectedVersion:V('C1')});
    c=one(p.stop,'SetChannelStopSell');assert.deepEqual(c.body,{channelId:null,roomTypeId:2,on:true});
    c=one(p.push,'PushChannelRates');assert.deepEqual(c.body,{channelId:1});
    c=one(p.pull,'PullChannelMessages');assert.deepEqual(c.body,{});
    c=one(p.accept,'AcceptChannelMessage');assert.deepEqual(c.body,{messageId:11,roomId:7,expectedVersion:V('M11')});assert.equal(c.key,'acc:1');
    c=one(p.acceptAuto,'AcceptChannelMessage');assert.deepEqual(c.body,{messageId:11,expectedVersion:V('M11')});
    assert.equal(p.acceptBadRoom.r.ok,false);assert.equal(p.acceptBadRoom.cmds.length,0);
    c=one(p.reject,'RejectChannelMessage');assert.deepEqual(c.body,{messageId:11,reason:'Full house',expectedVersion:V('M11')});
    assert.deepEqual([p.rejectNoReason.r.reason,p.rejectNoReason.cmds.length],['precheck',0]);
    c=one(p.confirm,'ConfirmWebRequest');assert.deepEqual(c.body,{requestId:21});assert.equal(c.key,'wc:1');
    c=one(p.decline,'DeclineWebRequest');assert.deepEqual(c.body,{requestId:21,reason:'No rooms'});
    for(const x of [p.status,p.push])assert.ok(x.gets.includes('/ota/channels'),x.gets.join(','));
    assert.equal(p.webReq.r.ok,false);assert.match(p.webReq.r.text,/Not available on the server yet: CHN_WEB_REQUEST/);assert.equal(p.webReq.cmds.length,0);
  });

  const P=await loadPages();const shown=[P.renderPages(rt.get().state,'1',false),P.renderPages(rt.get().state,'1',true)];
  test('[W4-11] Every desktop and phone page renders on the loaded yield / channel data',()=>{assert.deepEqual(shown.map(x=>x.errors),[{},{}]);});

  test('[W4-11] unmapped list explains every prototype command left out; registry has no duplicates',()=>{
    const un=Object.keys(S.AREAS.find(a=>a.id==='channels').unmapped).sort();
    assert.deepEqual(un,['CHN_WEB_REQUEST','GUEST_ID_VIEW','MSG_DOC_CONFIRM','MSG_DOC_DISCARD','MSG_DOC_EXTRACT']);
    assert.deepEqual(S.validateAreas(S.AREAS),[]);
  });

  // Role gates: a cashier (no reports/reservations) loads neither slice, a 403 on a part leaves the rest.
  const cashier=fakeServer({'GET /me/capabilities':()=>capabilities({roles:['CSH'],grants:[['PERM-PAY-001','A']]})});
  const cs=await startRuntime(S,cashier);
  const part=fakeServer({'GET /ota/channels':()=>channelsBody,'GET /ota/messages':()=>reply(403,{code:'FORBIDDEN',message:'no'}),'GET /ota/pushes':()=>[],'GET /web-booking/requests':()=>reply(403,{code:'FORBIDDEN',message:'no'})});
  const ps=await startRuntime(S,part);
  test('[W4-11] A cashier never calls yield/OTA routes; missing OTA inbox / web permission leaves only those lists empty',()=>{
    assert.equal(cashier.calls.filter(c=>/yield|ota|web-booking/.test(c.path)).length,0);
    assert.equal(ps.get().state.channelsX.channels.length,1);assert.deepEqual(ps.get().state.channelsX.messages,[]);assert.deepEqual(ps.get().state.channelsX.webRequests,[]);
  });
}
