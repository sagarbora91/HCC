// AF-VERIFY (wave 4): hostile client-side attempts against the audit-fix work: the counter mapper (BIL-01), the PAY-08 resend, the KEY_ / TRC_ / RPT_ mappers
// (OPS-01..03) and their role-gated reads, CFG_NET_SET. Names prefixed [AF-VERIFY V-xxx]; a failing test is a finding (see af-verify-DONE.md).
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {loadSync,fakeServer,startRuntime,capabilities,reply} from './_sync-harness.mjs';

await mkdir('tests/.generated',{recursive:true});
await writeFile('tests/.generated/af-verify-entry.ts',"export {lfmoney} from '../../src/clients/sync/lfmoney';\nexport {money} from '../../src/clients/sync/money';\nexport {HccClientError} from '../../src/clients/errors';\n");
await build({entryPoints:['tests/.generated/af-verify-entry.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/af-verify.mjs',logLevel:'error'});
const X=await import('./.generated/af-verify.mjs');

export async function register({test,assert}){
  const S=await loadSync();

  // ── V-CMAP: counter mapper ─────────────────────────────────────────────────────────────────────────────────────────────────
  const shift=()=>({shiftId:41,tillId:7,businessDate:'2026-10-24',status:'Open',openingFloatPaise:1072500,expectedPaise:null,countedPaise:null,approvalId:null,version:'SHFv1'});
  const desk=open=>({businessDate:'2026-10-24',tills:[{till:{tillId:7,code:'FRONT',name:'Front desk',version:'T1'},
    active:open?{shift:shift(),submittedBy:null,differenceReason:null,closedBy:null,approval:null}:null,expected:open?{expectedPaise:1222500}:null,lastClosed:null}]});
  const meta=(id,v)=>({updatedUtc:'2026-10-24T03:00:00.000Z',version:v+id,retiredOn:null,active:true,usageCount:0});
  const page=items=>({page:1,pageSize:200,total:items.length,items});
  const mk=(open,extra={})=>fakeServer({'GET /cashier/desk':()=>desk(open),'POST /commands/IssueCounterBill':()=>({billId:5,invoiceNo:'INV/2627/0005',version:'CB1'}),...extra});
  const lines=[{label:'Laundry',qty:1,unit:10000,gstCategory:'laundry'}];
  const party={name:'Walk-in (fictional)'};
  const issue=(rt,settlement,extra={})=>rt.run({type:'CTR_BILL_ISSUE',party,lines,settlement,key:'k-'+Math.random().toString(36).slice(2),...extra});

  const closed=mk(false),rtClosed=await startRuntime(S,closed);
  const rClosedCash=[await issue(rtClosed,{mode:'paid',method:'Cash'})];
  const rClosedEmpty=await issue(rtClosed,{mode:'paid',method:''});
  const rClosedUpi=await issue(rtClosed,{mode:'paid',method:'UPI',reference:'U1'});
  const rClosedUdhaar=await issue(rtClosed,{mode:'udhaar',method:'Cash'},{party:{name:'W',phone:'9820005555'}});
  const closedSent=closed.commands('IssueCounterBill').map(c=>c.body.settlement);

  const open=mk(true),rtOpen=await startRuntime(S,open);
  const rOpenLower=await issue(rtOpen,{mode:'paid',method:'cash'});          // not a master name: must not post a cash bill without a till
  const rOpenCompanyStr=await issue(rtOpen,{mode:'company',companyId:'12'});
  const rOpenCompanyBad=await issue(rtOpen,{mode:'company',companyId:'abc'});
  const rOpenCompanyNone=await issue(rtOpen,{mode:'company'});
  const sentOpen=open.commands('IssueCounterBill').map(c=>c.body.settlement);

  test('[AF-VERIFY V-CMAP-01] shift closed: Cash sends 0 requests; empty method too; UPI and Udhaar still go',()=>{
    for(const r of rClosedCash)assert.deepEqual([r.ok,r.reason],[false,'precheck'],JSON.stringify(r));
    assert.deepEqual([rClosedEmpty.ok,rClosedEmpty.reason],[false,'precheck']);
    assert.equal(rClosedUpi.ok,true,JSON.stringify(rClosedUpi));assert.equal(rClosedUdhaar.ok,true,JSON.stringify(rClosedUdhaar));
    assert.equal(closedSent.length,2,'only the UPI and the Udhaar bill reach the server');
    assert.equal(closedSent[0].tillId,undefined);assert.equal(closedSent[1].tillId,undefined);assert.equal(closedSent[1].method,'Cash');
  });
  test('[AF-VERIFY V-CMAP-02] open shift: a lower-case "cash" is either mapped to the till or refused locally - never sent without a till',()=>{
    const sentLower=sentOpen[0];
    assert.ok(!rOpenLower.ok||sentLower?.tillId===7,`cash (lower case) was sent without a till: ${JSON.stringify(sentLower)}`);
  });
  test('[AF-VERIFY V-CMAP-03] companyId from the <select> (a string) becomes a server number; a non-numeric or missing company never reaches the server',()=>{
    const company=sentOpen.find(s=>s.mode==='company');
    assert.ok(company,'company bill was sent');assert.strictEqual(company.companyId,12);
    assert.equal(rOpenCompanyBad.ok,false);
    assert.equal(sentOpen.filter(s=>s.mode==='company'&&s.companyId!==undefined).length,1,'only the valid company id was sent');
  });
  test('[AF-VERIFY V-CMAP-04] a company bill without a company is refused on the device (0 requests) - the server also refuses it',()=>{
    assert.equal(rOpenCompanyNone.ok,false,'sent to the server with no company id');
  });

  // ── V-PAY: the confirm resend of a split payment ───────────────────────────────────────────────────────────────────────────
  const pay=X.money.commands.find(c=>c.type==='PAYMENT');
  function fakeFolio(){
    const receipts=[],store=new Map();
    const api={async command(name,body,opts={}){
      const key=`${api.baseKey}${opts.step?':'+opts.step:''}`;
      if(store.has(key)){return store.get(key);}
      const dup=receipts.find(r=>r.kind===body.kind&&r.tender===body.tender&&r.amountPaise===body.amountPaise);
      if(dup&&!body.confirmDuplicate)throw new X.HccClientError('VALIDATION','A UPI payment was recorded on this folio 0 minutes ago. If this is a second, separate payment, confirm it.',{reason:'DUPLICATE_SUSPECTED',receiptId:dup.id});
      const rec={id:receipts.length+1,...body};receipts.push(rec);store.set(key,{receiptId:rec.id});return {receiptId:rec.id};}};
    return {receipts,api};
  }
  const ctxFor=(f,cmd)=>({cmd,state:{bookings:[{id:'1201',status:'InHouse'}],shiftOpen:true},ids:{server:(w,id)=>w==='folio'&&id==='1201'?5001:w==='till'?7:undefined,version:(w,id)=>w==='folio'?'FV1':undefined,learn(){},setVersion(){}},api:f.api,me:{permIds:[],userId:1}});
  const base={type:'PAYMENT',id:'1201',kind:'payment',method:'UPI',reference:'U1',amount:50000};
  // single tender: refused then confirmed = exactly two receipts (the lane's happy path, re-checked)
  const f1=fakeFolio();f1.api.baseKey='d1';await pay.send(ctxFor(f1,{...base}));
  f1.api.baseKey='d2';const refused1=await pay.send(ctxFor(f1,{...base})).then(()=>null,e=>e);
  f1.api.baseKey='d2';await pay.send(ctxFor(f1,{...base,confirmDuplicate:true}));
  // split of two equal UPI rows: row 2 is refused as a duplicate of row 1; the cashier ticks "second, separate payment"
  const f2=fakeFolio();f2.api.baseKey='s1';
  const split={...base,amount:100000,tenders:[{method:'UPI',amount:50000,reference:'A'},{method:'UPI',amount:50000,reference:'B'}]};
  const refused2=await pay.send(ctxFor(f2,split)).then(()=>null,e=>e);
  const after1=f2.receipts.length;
  await pay.send(ctxFor(f2,{...split,confirmDuplicate:true}));
  test('[AF-VERIFY V-PAY-C01] single tender: refused with details.reason, then the confirmed resend gives exactly two receipts',()=>{
    assert.equal(refused1?.details?.reason,'DUPLICATE_SUSPECTED');assert.equal(f1.receipts.length,2);
  });
  test('[AF-VERIFY V-PAY-C02] split tender: the refusal names the rows already recorded',()=>{
    assert.equal(refused2?.details?.reason,'DUPLICATE_SUSPECTED');assert.equal(after1,1,'row 1 recorded, row 2 refused');assert.match(refused2.message,/Tender rows 1 to 1 were already recorded/);
  });
  test('[AF-VERIFY V-PAY-C03] split tender: ticking "second, separate payment" must record row 2 only - never row 1 again (2 receipts, not 3)',()=>{
    assert.equal(f2.receipts.length,2,`after the confirmed resend the folio holds ${f2.receipts.length} receipts: ${JSON.stringify(f2.receipts.map(r=>[r.amountPaise,r.reference]))}`);
  });

  // ── V-OPS: KEY_ / TRC_ / RPT_ mappers ──────────────────────────────────────────────────────────────────────────────────────
  const opsRoutes=()=>({'GET /keys/log':()=>({rows:[],totals:{},total:0}),'GET /keys/locks':()=>({locks:[{roomId:7,roomNo:'105',hasLock:true,lockNo:5,doorId:null,doorName:'105',note:null,version:'AAAAAAAAB9E='}],encoders:[]}),
    'GET /keys/encoder':()=>({encoderId:null,pcName:null,online:false,adapter:null,detail:null,lastSeenUtc:null}),'GET /tariff-card':()=>({versions:[],weekendDaysDefault:[5,6]}),
    'GET /reports/favourites':()=>[],'GET /reports/schedules':()=>[],'GET /reports/runs':()=>[],
    'POST /commands/RequestKey':()=>({}),'POST /commands/CancelKey':()=>({}),'POST /commands/SaveKeyLock':()=>({}),'POST /commands/ProposeTariffCard':()=>({}),'POST /commands/PublishTariffCard':()=>({}),
    'POST /commands/SaveTariffCardDraft':()=>({}),'POST /commands/SetTariffCardWeekend':()=>({}),
    'POST /reports/favourites/:id':()=>({favourite:true}),'POST /reports/schedules':()=>({id:3}),'PUT /reports/schedules/:id':()=>({id:3}),'DELETE /reports/schedules/:id':()=>undefined,'POST /reports/:id/print-log':()=>({id:9})});
  const ops=fakeServer(opsRoutes()),rtOps=await startRuntime(S,ops);
  ops.reset();
  const hostile=[
    await rtOps.run({type:'KEY_REQUEST',booking:'abc',event:'issued',key:'o1'}),
    await rtOps.run({type:'KEY_REQUEST',booking:'-5',event:'issued',key:'o2'}),
    await rtOps.run({type:'KEY_CANCEL',booking:'0',key:'o3'}),
    await rtOps.run({type:'TRC_PUBLISH',id:'abc',key:'o4'}),
    await rtOps.run({type:'TRC_PROPOSE',id:'',key:'o5'}),
    await rtOps.run({type:'RPT_SCHEDULE_SAVE',reportId:'daily-sales',params:{},format:'pdf',cadence:{kind:'daily',time:'07:00'},recipients:['abc'],key:'o6'}),
    await rtOps.run({type:'RPT_SCHEDULE_DELETE',id:'x',key:'o7'}),
    await rtOps.run({type:'RPT_FAV_TOGGLE',reportId:'',key:'o8'}),
    await rtOps.run({type:'KEY_LOCK_SAVE',room:'999',hasLock:true,doorName:'x',lockNo:'1',note:'',key:'o9'}),
  ];
  const sentHostile=ops.calls.filter(c=>c.method!=='GET');
  test('[AF-VERIFY V-OPS-01] malformed ids (non-numeric, negative, zero, empty) are refused locally: 0 requests, no NaN on the wire',()=>{
    assert.deepEqual(hostile.map(r=>r.ok),hostile.map(()=>false),JSON.stringify(hostile.map(r=>[r.ok,r.reason,r.text])));
    assert.equal(sentHostile.length,0,JSON.stringify(sentHostile.map(c=>[c.method,c.path,c.body])));
  });
  ops.reset();
  const fav1=await rtOps.run({type:'RPT_FAV_TOGGLE',reportId:'daily sales/x',key:'fav-1'});
  const logRun=await rtOps.run({type:'RPT_LOG',reportId:'../../etc',params:{a:1},rows:NaN,format:'pdf',key:'log-1'});
  const sched=await rtOps.run({type:'RPT_SCHEDULE_SAVE',reportId:'daily-sales',params:{},format:'pdf',cadence:{kind:'weekly',day:'3',time:'07:00'},recipients:['4',5],enabled:true,key:'sch-1'});
  const sentPosts=ops.calls.filter(c=>c.method!=='GET');
  test('[AF-VERIFY V-OPS-02] raw-transport posts carry the command key as Idempotency-Key and path-encode the report id',()=>{
    assert.ok(fav1.ok,JSON.stringify(fav1));
    const fav=sentPosts.find(c=>c.path.startsWith('/reports/favourites/'));
    assert.ok(fav,'favourite toggle sent');assert.equal(fav.path,'/reports/favourites/daily%20sales%2Fx');
    assert.ok(fav.headers['Idempotency-Key']||fav.headers['idempotency-key'],'RPT_FAV_TOGGLE has no Idempotency-Key header: a retried toggle after a lost response flips the favourite back');
    assert.ok(!sentPosts.some(c=>/\.\./.test(c.path)&&!/%2E|%2F/i.test(c.path)),'a ../ report id must not become a path traversal');
  });
  test('[AF-VERIFY V-OPS-03] schedule recipients ids are numbers; weekly day is a number',()=>{
    assert.ok(sched.ok,JSON.stringify(sched));
    const body=sentPosts.find(c=>c.path==='/reports/schedules')?.body;
    assert.deepEqual(body.recipients,[4,5]);assert.strictEqual(body.day,3);
  });

  // role-gated reads: nothing the grants do not cover is requested
  const roleReads=async(grants,roles)=>{const s=fakeServer({...opsRoutes(),'GET /me/capabilities':()=>capabilities({userId:7,displayName:'Role',roles,grants})});await startRuntime(S,s);
    return ['/keys/log','/keys/locks','/keys/encoder','/tariff-card','/reports/favourites','/reports/schedules','/reports/runs'].filter(p=>s.gets(p).length>0);};
  const hk=await roleReads([['PERM-HK-001','A']],['HKP']);
  const keyOnly=await roleReads([['PERM-KEY-001','A']],['REC']);
  test('[AF-VERIFY V-OPS-04] role reads: housekeeping reads none of the keys / tariff / report-hub routes; a keys viewer reads log and encoder but not the lock mapping',()=>{
    assert.deepEqual(hk,[],JSON.stringify(hk));
    assert.ok(keyOnly.includes('/keys/log')&&keyOnly.includes('/keys/encoder'));assert.ok(!keyOnly.includes('/keys/locks'),'lock mapping needs PERM-KEY-004');
    assert.ok(!keyOnly.includes('/reports/schedules')&&!keyOnly.includes('/tariff-card'));
  });

  // ── V-NET: CFG_NET_SET ─────────────────────────────────────────────────────────────────────────────────────────────────────
  const netSrv=fakeServer({'GET /me/capabilities':()=>capabilities({grants:[['PERM-NET-001','A'],['PERM-ADM-001','A']]}),'POST /commands/SetNetSetting':()=>({status:'applied'})});
  const rtNet=await startRuntime(S,netSrv);netSrv.reset();
  const net=[
    await rtNet.run({type:'CFG_NET_SET',setting:'net.ota',value:'true',reason:'Enable OTA',key:'n1'}),
    await rtNet.run({type:'CFG_NET_SET',setting:'net.ota',value:1,reason:'Enable OTA',key:'n2'}),
    await rtNet.run({type:'CFG_NET_SET',setting:'net.ota',value:true,reason:'x'.repeat(121),key:'n3'}),
    await rtNet.run({type:'CFG_NET_SET',setting:'net.ota',value:true,reason:'   ',key:'n4'}),
    await rtNet.run({type:'CFG_NET_SET',setting:'net.nope',value:true,reason:'Enable OTA',key:'n5'}),
    await rtNet.run({type:'CFG_NET_SET',setting:'net.ota',value:undefined,reason:'Enable OTA',key:'n6'}),
  ];
  test('[AF-VERIFY V-NET-C01] non-boolean values, over-long / blank reasons and unknown keys never leave the device',()=>{
    assert.deepEqual(net.map(r=>r.ok),net.map(()=>false),JSON.stringify(net.map(r=>[r.ok,r.reason,r.text])));
    assert.equal(netSrv.commands('SetNetSetting').length,0);
  });
}
