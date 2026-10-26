// D5 (W4 E2E, S1): every seeded role boots the production runtime without a single read its grants do not cover, and still gets the
// data its home pages need. tests/_role-reads.json holds the REAL server's answer per role and route (written by
// 08 Production/tests/Hcc.Integration.Tests RoleReadMatrixTests, which fails when the server drifts from it):
//   1. sync/reads.ts READS agrees with the server for every role × route;
//   2. per role, a fake server answers 403 exactly where the real one does (and 404 for a route the file does not list): the runtime
//      sends no 403 read, no unknown read, has no load errors, fills the role's core slices and every page renders (desktop + phone).
// Grants are not changed (role matrix v2 D1–D7 are the owner's): Reception reads the front-desk room list (GET /rooms/status,
// PERM-RES-001) instead of the housekeeping board, the Cashier its folio list (GET /cashier/folios, PERM-FOL-001) instead of reservations.
import {readFileSync} from 'node:fs';
import {loadSync,loadPages,fakeServer,startRuntime,reply} from './_sync-harness.mjs';

const MATRIX=JSON.parse(readFileSync(new URL('./_role-reads.json',import.meta.url),'utf8'));
const BD='2026-10-24';
const USER_ID={OWN:1,MGR:2,REC:3,CSH:4,ACC:5,SUP:6,HKP:7,MNT:8,TAD:9};

const page=(rows,q={},key='rows')=>{const p=Number(q.page||1),n=Number(q.pageSize||100);return {page:p,pageSize:n,total:rows.length,[key]:rows.slice((p-1)*n,p*n)};};
const task=(id,roomId,roomNo,assigneeId,over={})=>({taskId:id,roomId,roomNo,kind:'checkout',status:'ToDo',assigneeId,checklist:[false,false,false],note:null,rejectReason:null,
  businessDate:BD,startedUtc:null,completedUtc:null,version:'V'+id,...over});
const row=(roomId,roomNo,over={})=>({roomId,roomNo,roomType:'Deluxe',hk:'Ready',status:'Ready',block:null,current:null,next:null,openTasks:[],roomVersion:'RV'+roomId,...over});
const resItem=(id,over={})=>({reservationId:id,publicNo:'HCC-'+id,version:'RV'+id,status:'InHouse',guestName:'Fictional Guest '+id,phone:null,roomId:7,roomNo:'105',roomTypeId:2,roomType:'Deluxe',
  arrival:'2026-10-23',departure:'2026-10-26',adults:2,children:0,source:'Direct',nightlyRatePaise:200000,holdUntil:null,groupBlockId:null,restrictionOverride:null,cancelReason:null,notes:null,
  company:null,guestId:null,registered:true,stayId:500+id,stayVersion:'SV'+id,stayStatus:'InHouse',checkedInDate:'2026-10-23',checkedOutDate:null,segments:[{roomNo:'105',from:'2026-10-23',to:null}],...over});
const folio=id=>({folioId:5000+id,reservationId:id,reservationStatus:'InHouse',businessDate:BD,version:'FV'+id,
  lines:[{chargeId:9001,kind:'room',serviceDate:'2026-10-23',label:'Room night · 2026-10-23',amountPaise:200000,taxBps:500,taxPaise:10000,reversesChargeId:null,reversed:false}],
  receipts:[],approvals:[],totals:{netPaise:200000,taxPaise:10000,totalPaise:210000,paidPaise:0,depositPaise:0,companyCreditPaise:0,balancePaise:210000}});
const shift={shiftId:41,tillId:3,businessDate:BD,status:'Open',openingFloatPaise:500000,expectedPaise:null,countedPaise:null,approvalId:null,version:'SHFv1'};
const order={workOrderId:31,no:'WO-31',kind:'repair',title:'AC not cooling',priority:'P2',status:'Open',roomId:7,roomNo:'105',assetId:null,area:null,assigneeId:USER_ID.MNT,vendorId:null,
  createdUtc:'2026-10-24T03:00:00.000Z',createdBy:6,startedUtc:null,doneUtc:null,verifiedUtc:null,holdReason:null,awaitingSpareId:null,note:null,returnReason:null,cancelReason:null,
  repairTaskId:null,planId:null,dueDate:null,downtimeFromUtc:null,downtimeToUtc:null,parts:[],version:'WOv31',doneBy:null,verifiedBy:null};

/** 200 answers shaped like the .NET DTOs (enough for every loader; core data only where a role's home page needs it). */
function routes(role){
  const uid=USER_ID[role],wide=['OWN','MGR','SUP'].includes(role);
  const tasks=[task(21,7,'105',USER_ID.HKP),task(22,9,'207',USER_ID.HKP,{status:'Inspection'}),task(23,11,'305',USER_ID.MNT,{kind:'repair',checklist:[],note:'AC repair'})];
  const mine=wide?tasks:tasks.filter(t=>t.assigneeId===uid);
  const board=[row(7,'105',{hk:'Dirty',status:'Dirty'}),row(9,'207',{hk:'Inspection',status:'Inspection'}),row(11,'305',{status:'Blocked',block:{blockId:55,from:BD,to:'2026-10-27',reason:'AC repair',source:'manual',version:'BV55'}})];
  return {
    'GET /me/capabilities':null,
    'GET /closing/business-date':()=>({businessDate:BD,closed:false}),
    'GET /closing/records':()=>({items:[]}),
    'GET /reservations':()=>({businessDate:BD,total:2,page:1,pageSize:500,items:[resItem(1201),resItem(1202,{status:'Confirmed',roomId:9,roomNo:'207',arrival:BD,stayId:null,stayVersion:null,stayStatus:null,checkedInDate:null,registered:false,segments:[]})]}),
    'GET /reservations/:id':()=>reply(404,{code:'NOT_FOUND',message:'Reservation not found.'}),
    'GET /guests':()=>({total:0,page:1,pageSize:500,items:[]}),
    'GET /availability':()=>({businessDate:BD,arrival:BD,departure:'2026-10-25',nights:1,party:1,types:[],rooms:[{roomId:7,roomNo:'105',roomTypeId:2,roomType:'Deluxe',capacity:3,available:false,reasons:[],heldForBlock:null,nightlyRatePaise:200000}]}),
    'GET /rooms/board':({query})=>({businessDate:BD,...page(wide?board:board.filter(r=>mine.some(t=>t.roomId===r.roomId)),query),counts:{},assignedOnly:!wide}),
    'GET /rooms/status':()=>({asOf:BD,businessDate:BD,rooms:[
      {roomId:7,roomNo:'105',roomType:'Deluxe',blocked:false,blockReason:null,roomBlockId:null,occupied:true,hk:'Dirty',blockFrom:null,blockTo:null},
      {roomId:9,roomNo:'207',roomType:'Deluxe',blocked:false,blockReason:null,roomBlockId:null,occupied:false,hk:'Ready',blockFrom:null,blockTo:null},
      {roomId:11,roomNo:'305',roomType:'Suite',blocked:true,blockReason:'AC repair',roomBlockId:66,occupied:false,hk:'Ready',blockFrom:BD,blockTo:'2026-10-27'}]}),
    'GET /housekeeping/tasks':({query})=>({...page(mine,query),assignedOnly:!wide}),
    'GET /folios/by-reservation/:id':({params})=>params.id==='1201'?{reservationId:1201,stayId:501,stayVersion:'SV1201',folio:folio(1201)}:{reservationId:Number(params.id),stayId:null,stayVersion:null,folio:null},
    'GET /invoices/by-source/folio/:id':()=>reply(404,{code:'NOT_FOUND',message:'No invoice.'}),
    'GET /credit-note-requests':()=>[],
    'GET /cashier/desk':()=>({businessDate:BD,tills:[{till:{tillId:3,code:'FRONT',name:'Front desk',version:'T1'},active:{shift,submittedBy:null,differenceReason:null,closedBy:null,approval:null},expected:{expectedPaise:500000},lastClosed:null}]}),
    'GET /cashier/folios':()=>({businessDate:BD,items:[{reservationId:1201,publicNo:'HCC-1201',status:'InHouse',guestName:'Fictional Guest 1201',roomId:7,roomNo:'105',roomType:'Deluxe',
      arrival:'2026-10-23',departure:'2026-10-26',adults:2,children:0,source:'Direct',nightlyRatePaise:200000,stayId:501,folioId:6201,folioVersion:'FV1201',balancePaise:210000,companyName:null,companyGstin:null}]}),
    'GET /cashbook':()=>({from:'2026-07-24',to:BD,businessDate:BD,days:[]}),
    'GET /groups':()=>[],
    'GET /groups/:id/statement':()=>reply(404,{code:'NOT_FOUND',message:'No group.'}),
    'GET /admin/users':()=>({page:1,pageSize:100,total:1,items:[{userId:uid,loginName:'matrix.'+role.toLowerCase(),displayName:'Matrix '+role,enabled:true,roles:[{roleCode:role,validFromUtc:'2026-10-01T00:00:00.000Z',validUntilUtc:null}],
      credentialKind:'PASSWORD',hasPassword:true,hasPin:false,lockedUntilUtc:null,lastSignInUtc:null,version:'U1'}]}),
    'GET /admin/roles':()=>({roles:[],permissions:[],grants:[],version:'R1'}),
    'GET /admin/exceptions':()=>({items:[]}),
    'GET /admin/audit':()=>({page:1,pageSize:100,total:0,items:[]}),
    'GET /configuration/requests':()=>({items:[]}),
    'GET /configuration/history':()=>({page:1,pageSize:100,total:0,items:[]}),
    'GET /configuration/values':()=>({businessDate:BD,values:{hotel:'Sand Residency'},scheduled:[]}),
    'GET /payroll/staff':()=>[],
    'GET /payroll/board':()=>({businessDate:BD,from:BD,to:BD,allStaff:false,payroll:false,departments:[],leavePolicy:{cl:0,sl:0,el:0,defaultCap:0},roster:[],attendance:[],leaves:[],rateVersions:[],runs:[],advances:[],fnf:[]}),
    'GET /accounting/days':()=>[],
    'GET /accounting/trial-balance':()=>({date:'2026-08-23',cumulative:true,debitPaise:0,creditPaise:0,balanced:true,rows:[]}),
    'GET /accounting/mapping':()=>[],
    'GET /accounting/mapping/history':()=>[],
    'GET /accounting/locks/history':()=>[],
    'GET /expenses':({query})=>page(query.status==='Pending'?[{expenseId:71,ref:'EXP-71',vendor:'Fictional Stores',category:'Supplies',amountPaise:45000,method:'Cash',businessDate:BD,status:'Pending',
      submittedBy:uid,notes:null,receipt:null,reason:null,decidedBy:null,decidedUtc:null,reversalOf:null,reversedBy:null,version:'E71'}]:[],query,'items'),
    'GET /compliance/obligations':()=>[],
    'GET /compliance/calendar':()=>({asOf:BD,items:[],counts:{},settings:{reminderDays:7,version:'C1'},note:''}),
    'GET /compliance/ca-packs':()=>[],
    'GET /pos/outlets':()=>[],
    'GET /pos/menu':()=>[],
    'GET /pos/checks':()=>[],
    'GET /pos/checks/closed':()=>[],
    'GET /pos/checks/:id':()=>reply(404,{code:'NOT_FOUND',message:'No check.'}),
    'GET /pos/void-requests':()=>[],
    'GET /pos/in-house':()=>[],
    'GET /pos/till':()=>({tillId:3,shiftOpen:true}),
    'GET /maintenance/registers':()=>({assets:[],vendors:[],spares:[],plans:[]}),
    'GET /maintenance/work-orders':({query})=>page(role==='HKP'?[]:[order],query),
    'GET /maintenance/work-orders/:id':()=>({...order,history:[{at:'2026-10-24T03:00:00.000Z',by:6,action:'Created',note:null}]}),
    'GET /maintenance/amcs':()=>({amcs:[]}),
    'GET /stock/items':({query})=>page([],query),
    'GET /stock/laundry':({query})=>page([],query),
    'GET /stock/counts':({query})=>page([],query),
    'GET /stock/losses':({query})=>page([],query),
    'GET /stock/moves':({query})=>page([],query),
    'GET /stock/settings':()=>null,
    'GET /guest-services/requests':({query})=>page([],query,'items'),
    'GET /guest-services/complaints':({query})=>page([],query,'items'),
    'GET /guest-services/lost-items':({query})=>page([],query,'items'),
    'GET /guest-services/settings':()=>({slaMinutes:{},retentionDays:90,valuableRetentionDays:180,version:null}),
    'GET /guest-services/stays':()=>({items:[]}),
    'GET /guest-services/histories/:kind':()=>[],
    'GET /handover/checklists':()=>({date:BD,runs:[]}),
    'GET /handover/templates':()=>({templates:[]}),
    'GET /handover/notes':()=>({notes:[]}),
    'GET /messaging/messages':()=>({items:[],total:0}),
    'GET /messaging/templates':()=>({templates:[]}),
    'GET /messaging/rules':()=>[],
    'GET /messaging/consents':()=>({consents:[],guests:[]}),
    'GET /ota/channels':()=>[],
    'GET /ota/messages':()=>[],
    'GET /ota/pushes':()=>[],
    'GET /web-booking/requests':()=>({requests:[],pending:0}),
    'GET /web-booking/requests/:id':()=>reply(404,{code:'NOT_FOUND',message:'No request.'}),
    'GET /yield/calendar':()=>({cells:[]}),
    'GET /yield/config':()=>({plans:[],seasons:[],dow:[0,0,0,0,0,0,0],floorBps:0,ceilBps:0,roundToPaise:100,settingsVersion:'Y1',occupancyRules:[],restrictions:[],overrides:[],forecast:[],history:[]}),
    'GET /yield/publishes':()=>[],
    'GET /yield/proposals':()=>[],
  };
}

/** What each role's home pages cannot do without (DEC-063: front desk = rooms + bookings, Cashier works from folios, …). */
const CORE={
  OWN:s=>[['rooms',s.rooms.length],['bookings',s.bookings.length],['tasks',s.tasks.length]],
  MGR:s=>[['rooms',s.rooms.length],['bookings',s.bookings.length],['tasks',s.tasks.length]],
  REC:s=>[['rooms',s.rooms.length],['bookings',s.bookings.length],['shiftOpen',s.shiftOpen?1:0]],
  CSH:s=>[['bookings',s.bookings.length],['folio lines',s.bookings[0]?.lines?.length??0],['shiftOpen',s.shiftOpen?1:0]],
  ACC:s=>[['expenses',s.expenses.length]],
  SUP:s=>[['rooms',s.rooms.length],['tasks',s.tasks.length]],
  HKP:s=>[['tasks',s.tasks.length],['rooms',s.rooms.length]],
  MNT:s=>[['work orders',s.maintenanceX?.orders?.length??0],['rooms',s.rooms.length]],
  TAD:s=>[['users',s.users.length]],
};

export async function register({test,assert}){
  const S=await loadSync();const P=await loadPages();
  const roles=Object.keys(MATRIX.roles);

  test('[D5] The fixture covers every seeded role and every listed route',()=>{
    assert.deepEqual(roles,['OWN','MGR','REC','CSH','ACC','SUP','HKP','MNT','TAD']);
    for(const r of roles)assert.deepEqual(Object.keys(MATRIX.roles[r].reads),Object.keys(MATRIX.routes),r);
    assert.deepEqual(Object.keys(S.READS).sort(),Object.keys(MATRIX.routes).sort(),'sync/reads.ts READS lists exactly the fixture routes');
  });
  test('[D5] READS repeats the server: every role × route allowed exactly where the real server does not answer 403',()=>{
    const bad=[];
    for(const r of roles){const me={grants:MATRIX.roles[r].permissions.map(([permissionId,mode,scope])=>({permissionId,mode,scope}))};
      for(const [route,status] of Object.entries(MATRIX.roles[r].reads))if(S.READS[route](me)!==(status!=='403'))bad.push(`${r} ${route}: server ${status}`);}
    assert.deepEqual(bad,[]);
  });

  const results={};
  for(const role of roles){
    const m=MATRIX.roles[role],uid=USER_ID[role];
    const caps={userId:uid,propertyId:1,displayName:'Matrix '+role,credentialKind:'PIN',roles:m.roles,
      permissions:m.permissions.map(([permissionId,mode,scope])=>({permissionId,mode,scope,limit:null,limitUnit:null})),fieldMasks:m.fieldMasks,
      session:{idleExpiresUtc:'2026-10-24T05:00:00.000Z',absoluteExpiresUtc:'2026-10-24T16:00:00.000Z',idleTimeoutSeconds:1800}};
    const forbidden=[];const table={};
    for(const [spec,h] of Object.entries(routes(role))){
      if(spec==='GET /me/capabilities'){table[spec]=()=>caps;continue;}
      table[spec]=m.reads[spec]==='403'?(call=>{forbidden.push(`${spec} (${call.url})`);return reply(403,{code:'FORBIDDEN',message:'Not permitted: No permission.'});}):h;
    }
    const srv=fakeServer(table);
    const rt=await startRuntime(S,srv);
    const snap=rt.get();
    const unknown=srv.calls.filter(c=>c.method==='GET'&&c.path!=='/system/health'&&!Object.keys(MATRIX.routes).some(p=>new RegExp('^'+p.slice(4).replace(/:[A-Za-z]+/g,'[^/]+')+'$').test(c.path))).map(c=>c.path);
    const uidProto=snap.me?.uid;
    results[role]={forbidden,unknown,errors:snap.errors,status:snap.status,core:CORE[role](snap.state),
      desk:P.renderPages(snap.state,uidProto),phone:P.renderPages(snap.state,uidProto,true),state:snap.state,unrouted:srv.unrouted};
  }

  for(const role of roles){
    const r=results[role];
    test(`[D5] ${role}: boots with zero 403 reads, no unknown read and no load errors`,()=>{
      assert.equal(r.status,'ready');
      assert.deepEqual(r.forbidden,[],`${role} sent reads its grants do not cover`);
      assert.deepEqual(r.unknown,[],`${role} sent a GET that tests/_role-reads.json does not list (add it to the route list and regenerate)`);
      assert.deepEqual(r.errors,{});
    });
    test(`[D5] ${role}: home data present and every desktop + phone page renders`,()=>{
      for(const [what,n] of r.core)assert.ok(n>0,`${role}: ${what} is empty`);
      assert.deepEqual(r.desk.errors,{});assert.deepEqual(r.phone.errors,{});
    });
  }

  test('[D5] Reception: room board from the front-desk status read (clean/dirty, block), never the housekeeping board',()=>{
    const s=results.REC.state;
    assert.deepEqual(s.rooms.map(x=>[x.id,x.hk,x.blocked,x.rate]),[['105','Dirty',false,200000],['207','Ready',false,0],['305','Ready',true,0]]);
    const b=s.rooms.find(x=>x.id==='305');assert.deepEqual([b.blockReason,b.blockFrom,b.blockTo],['AC repair',BD,'2026-10-27']);
    assert.deepEqual(s.tasks,[]);
    assert.deepEqual(s.bookings.map(x=>[x.id,x.status]),[['1201','In house'],['1202','Confirmed']]);
  });
  test('[D5] Cashier: bookings from the folio list with lines and balance from the folio read; no guest contact fields',()=>{
    const s=results.CSH.state,b=s.bookings[0];
    assert.deepEqual([s.bookings.length,b.id,b.guest,b.room,b.status,b.email,b.phone],[1,'1201','Fictional Guest 1201','105','In house','',undefined]);
    assert.equal(b.lines.length,1);assert.equal(s.rooms.length,0);
  });
  test('[D5] Housekeeping and Maintenance roles never receive money: no bookings, no cash desk',()=>{
    for(const r of ['SUP','HKP','MNT']){const s=results[r].state;assert.deepEqual([r,s.bookings.length,s.shiftOpen,s.openingCash],[r,0,false,0]);}
  });
}
