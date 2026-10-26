// Wave B1 · production client for cash leaving the drawer (DEC-073) and the shared till (DEC-078): the cashOutX loader (GET /cashier/transfers
// + /cashier/desk), each mapper's exact request (route, body, Idempotency-Key, expectedVersion), prechecks, the own-shift cash precheck with the
// shift's opener, and the screens rendering on server data. Fake routes are shaped like Hcc.Contracts Billing (CashOutContracts, CashDeskView).
import {build} from 'esbuild';
import {mkdir,writeFile} from 'node:fs/promises';
import {loadSync,fakeServer,startRuntime,capabilities,loadPages} from './_sync-harness.mjs';

const shift=(id,till,status,float,over={})=>({shiftId:id,tillId:till,businessDate:'2026-10-24',status,openingFloatPaise:float,expectedPaise:null,countedPaise:null,approvalId:null,version:'S'+id,...over});
const desk=()=>({businessDate:'2026-10-24',tills:[
  {till:{tillId:3,code:'FD-1',name:'Front desk',version:'T3'},active:{shift:shift(41,3,'Open',800000),submittedBy:null,differenceReason:null,closedBy:null,approval:null,
    openedBy:3,handoverCountPaise:790000,handoverDifferencePaise:-10000,handoverReason:'One ₹100 note short'},expected:{expectedPaise:750000},lastClosed:null},
  {till:{tillId:4,code:'BO-1',name:'Back office',version:'T4'},active:{shift:shift(42,4,'Open',100000),submittedBy:null,differenceReason:null,closedBy:null,approval:null,openedBy:2},
    expected:{expectedPaise:150000},lastClosed:null}]});
const item=(o)=>({transferId:0,kind:'banking',status:'Pending',businessDate:'2026-10-24',amountPaise:0,fromTillId:3,fromTillCode:'FD-1',toTillId:null,toTillCode:null,bank:null,
  slipNo:null,reason:null,recordedBy:3,recordedUtc:'2026-10-24T05:00:00.000Z',decidedBy:null,decidedUtc:null,decidedDate:null,decisionReason:null,slipStored:false,slipNote:null,
  approvalId:null,version:'V0',...o});
const transfers=()=>({businessDate:'2026-10-24',items:[
  item({transferId:13,kind:'drawing',status:'Posted',amountPaise:20000,reason:'Household',recordedBy:1,version:'D13'}),
  item({transferId:12,kind:'till',status:'Pending',amountPaise:30000,fromTillId:4,fromTillCode:'BO-1',toTillId:3,toTillCode:'FD-1',reason:'Change',recordedBy:2,approvalId:902,version:'A902'}),
  item({transferId:11,kind:'banking',status:'Pending',amountPaise:50000,bank:'SBI ••4321',slipNo:'DS-1001',slipStored:true,approvalId:901,version:'A901'}),
  item({transferId:10,kind:'banking',status:'Rejected',businessDate:'2026-10-23',amountPaise:40000,bank:'SBI ••4321',slipNo:'DS-0990',slipStored:false,
    slipNote:'Slip photo purged after 600 days.',decidedBy:1,decidedUtc:'2026-10-24T04:00:00.000Z',decidedDate:'2026-10-24',decisionReason:'Wrong slip',approvalId:900,version:'A900'})]});
const result=(id,kind,status)=>({transferId:id,kind,status,amountPaise:1,businessDate:'2026-10-24',fromTillId:3,toTillId:null,approvalId:null,version:'R'+id});

/** Renders the cash-out dialogs (both builds) over a state: catches runtime errors the page smoke render does not reach. */
async function renderDialogs(production){
  const dir='tests/.generated';await mkdir(dir,{recursive:true});
  const entry=`${dir}/cashout-dialogs-${production?'prod':'proto'}.tsx`;
  await writeFile(entry,`
import React from 'react';import {renderToStaticMarkup} from 'react-dom/server';
import {HccContext} from '../../src/app/context';
import {can,metrics} from '../../src/services/mock';
import {CashBankModal,TillTransferModal,OwnerDrawingModal,CashDecideModal,CashOutPanel,OpenShiftForm} from '../../src/features/closing/CashOut';
export function render(s:any,uid:string,mobile:boolean,ids:string[]){
  const noop=()=>{};const user=s.users.find((u:any)=>u.id===uid)??s.users[0];
  const ctx:any={s,uid,user,m:metrics(s),page:'cashier',go:noop,modal:{type:'x',key:'k1'},open:noop,closeModal:noop,booking:noop,error:'',busy:false,run:async()=>false,toast:noop,
    permission:(p:any)=>can(s,uid,p),act:()=>null,display:{isMobile:mobile,mode:mobile?'mobile':'desktop',setMode:noop},setDirty:noop};
  const out:Record<string,string>={};
  const els:[string,any][]=[['bank',<CashBankModal/>],['transfer',<TillTransferModal/>],['drawing',<OwnerDrawingModal/>],['panel',<CashOutPanel phone={mobile}/>],['open',<OpenShiftForm compact={mobile}/>],
    ...ids.map(id=>['decide:'+id,<CashDecideModal id={id}/>] as [string,any])];
  for(const [k,el] of els){try{out[k]=renderToStaticMarkup(<HccContext.Provider value={ctx}>{el}</HccContext.Provider>);}catch(e:any){out[k]='ERROR '+String(e?.message??e);}}
  return out;
}`);
  const outfile=`${dir}/cashout-dialogs-${production?'prod':'proto'}.mjs`;
  await build({entryPoints:[entry],bundle:true,platform:'node',format:'esm',outfile,logLevel:'error',jsx:'automatic',
    define:{'process.env.NODE_ENV':'"production"','process.env.HCC_BUILD':production?'"production"':'"prototype"'},
    banner:{js:"import {createRequire as __cxReq} from 'node:module';const require=__cxReq(import.meta.url);"}});
  return import(new URL('./.generated/'+outfile.split('/').pop(),import.meta.url).href);
}

export async function register({test,assert,M}){
  const S=await loadSync();
  const srv=fakeServer({
    'GET /me/capabilities':()=>capabilities({userId:3,displayName:'Asha (Reception)',roles:['REC']}),
    'GET /cashier/desk':()=>desk(),
    'GET /cashier/transfers':()=>transfers(),
    'POST /commands/RecordCashBanking':()=>result(14,'banking','Pending'),
    'POST /commands/ConfirmCashBanking':()=>result(11,'banking','Confirmed'),
    'POST /commands/TransferTillCash':()=>result(15,'till','Pending'),
    'POST /commands/AcceptTillTransfer':()=>result(12,'till','Accepted'),
    'POST /commands/RecordOwnerDrawing':()=>result(16,'drawing','Posted'),
  });
  const rt=await startRuntime(S,srv);
  const st=rt.get().state,x=st.cashOutX;
  const transferGet=srv.gets('/cashier/transfers')[0];
  srv.reset();
  const photo='data:image/jpeg;base64,/9j/4AAQSkZJRg==';
  const bank=await rt.run({type:'CASH_BANK',amount:50000,bank:' SBI ••4321 ',slip:'DS-1002',photo,note:'Morning deposit',key:'cash-bank:k1'});
  const reloaded=srv.gets().map(c=>c.path);
  const confirm=await rt.run({type:'CASH_BANK_DECIDE',id:'11',confirm:true,key:'cash-decide:11:k2'});
  const reject=await rt.run({type:'CASH_BANK_DECIDE',id:'11',confirm:false,reason:'',key:'cash-decide:11:k3'});
  const send=await rt.run({type:'TILL_TRANSFER',from:'FD-1',to:'BO-1',amount:10000,reason:'Change',key:'till-transfer:k4'});
  const accept=await rt.run({type:'TILL_TRANSFER_DECIDE',id:'12',accept:true,key:'cash-decide:12:k5'});
  const draw=await rt.run({type:'OWNER_DRAWING',amount:20000,reason:'Household',key:'owner-drawing:k6'});
  const big=await rt.run({type:'CASH_BANK',amount:1000,bank:'SBI',slip:'DS-9',photo:'data:image/jpeg;base64,'+Buffer.alloc(102400,0xff).toString('base64')});
  const gif=await rt.run({type:'CASH_BANK',amount:1000,bank:'SBI',slip:'DS-9',photo:'data:image/gif;base64,R0lGODlh'});
  const cmd=n=>srv.commands(n);

  test('[B1 sync] cashOutX loads from GET /cashier/transfers (93 days) + /cashier/desk: my till is the desk, other tills with owner and expected cash, the opener and handover',()=>{
    assert.deepEqual(transferGet.query,{from:'2026-07-24',to:'2026-10-24'});
    assert.equal(x.desk,'FD-1');assert.equal(x.deskName,'Front desk');
    assert.deepEqual(x.tills,[{code:'BO-1',name:'Back office',shiftOpen:true,openedBy:'2',expected:150000}]);
    assert.deepEqual(x.opener,{by:'3',date:'2026-10-24',at:'',handover:790000,difference:-10000,reason:'One ₹100 note short'});
    assert.deepEqual(x.records.map(r=>[r.id,r.kind,r.status,r.amount]),[['10','banking','Rejected',40000],['11','banking','Pending',50000],['12','till','Pending',30000],['13','drawing','Posted',20000]]);
    const r10=x.records[0];
    assert.deepEqual([r10.fromTill,r10.slip,r10.slipStored,r10.slipNote,r10.decidedBy,r10.decidedDate,r10.decisionReason,r10.approvalId],
      ['FD-1','DS-0990',false,'Slip photo purged after 600 days.','1','2026-10-24','Wrong slip','900']);
    assert.deepEqual([x.records[2].fromTill,x.records[2].toTill,x.records[2].actor],['BO-1','FD-1','2']);
    // the client-derived drawer and cash book now see them: −₹500 deposit, +₹400 returned deposit (rejected on the 24th), −₹200 drawing
    assert.deepEqual(M.cxDeskMovements(st,'2026-10-24').map(m=>[m.kind,m.amount]),[['banking',40000],['banking',-50000],['owner-drawing',-20000]]);
  });
  test('[B1 sync] CASH_BANK → RecordCashBanking {desk till, paise, bank, slip, base64 photo, content type, note}; dialog key; the cash desk and cashOutX reload',()=>{
    assert.ok(bank.ok,bank.text);
    const c=cmd('RecordCashBanking')[0];
    assert.deepEqual(c.body,{tillId:3,amountPaise:50000,bank:'SBI ••4321',slipNo:'DS-1002',slipImageBase64:'/9j/4AAQSkZJRg==',slipContentType:'image/jpeg',note:'Morning deposit'});
    assert.equal(c.key,'cash-bank:k1');
    assert.ok(reloaded.includes('/cashier/desk')&&reloaded.includes('/cashier/transfers'),reloaded.join(','));
  });
  test('[B1 sync] CASH_BANK_DECIDE → ConfirmCashBanking with the request version; a rejection without a reason is refused before sending',()=>{
    assert.ok(confirm.ok,confirm.text);
    assert.deepEqual(cmd('ConfirmCashBanking').map(c=>[c.key,c.body]),[['cash-decide:11:k2',{transferId:11,confirm:true,reason:null,expectedVersion:'A901'}]]);
    assert.equal(reject.ok,false);assert.equal(reject.reason,'precheck');
  });
  test('[B1 sync] TILL_TRANSFER → TransferTillCash (till ids from codes); TILL_TRANSFER_DECIDE → AcceptTillTransfer (version); OWNER_DRAWING → RecordOwnerDrawing',()=>{
    assert.ok(send.ok&&accept.ok&&draw.ok,[send.text,accept.text,draw.text].join(' | '));
    assert.deepEqual(cmd('TransferTillCash')[0].body,{fromTillId:3,toTillId:4,amountPaise:10000,reason:'Change'});
    assert.deepEqual(cmd('AcceptTillTransfer')[0].body,{transferId:12,accept:true,reason:null,expectedVersion:'A902'});
    assert.deepEqual(cmd('RecordOwnerDrawing')[0].body,{tillId:3,amountPaise:20000,reason:'Household'});
    assert.equal(cmd('RecordOwnerDrawing')[0].key,'owner-drawing:k6');
  });
  test('[B1 sync] A slip photo of 100 KB or more, or not JPEG/PNG, is refused on the device (nothing sent)',()=>{
    assert.equal(big.ok,false);assert.equal(big.reason,'precheck');assert.match(big.text,/100 KB/);
    assert.equal(gif.ok,false);assert.equal(gif.reason,'precheck');
    assert.equal(cmd('RecordCashBanking').length,1);
  });
  test('[B1 sync DEC-078] Own-shift cash precheck uses the shift owner when the desk read gives it: my shift → allowed, someone else’s → blocked',()=>{
    const me=S.meFromCapabilities({...capabilities({userId:3,roles:['REC']}),permissions:[{permissionId:'PERM-PAY-001',mode:'A',scope:'S',limit:null,limitUnit:null},
      {permissionId:'PERM-SHF-001',mode:'A',scope:'S',limit:null,limitUnit:null}]});
    assert.equal(S.cashBlockedReason(me,true,'3'),undefined);
    assert.equal(S.cashBlockedReason(me,true,'2'),S.CASH_NEEDS_OWN_SHIFT);
    assert.equal(S.cashBlockedReason(me,false,'3'),S.CASH_NEEDS_OWN_SHIFT);
    assert.equal(S.cashBlockedReason(me,true),undefined,'opener unknown (older server): the v12 rule');
  });
  test('[B1 sync] The cashier area maps every cash-out command and lists nothing unmapped',()=>{
    const a=S.AREAS.find(z=>z.id==='cashier');
    assert.deepEqual(a.commands.map(c=>c.type).sort(),['CASH_BANK','CASH_BANK_DECIDE','OWNER_DRAWING','SHIFT_OPEN','TILL_TRANSFER','TILL_TRANSFER_DECIDE']);
    assert.deepEqual(a.unmapped,{});assert.deepEqual(a.loaders.map(l=>l.slice),['cashOutX']);
  });

  const P=await loadPages();const shown=[P.renderPages(st,'3',false),P.renderPages(st,'3',true)];
  const prod=await renderDialogs(true),proto=await renderDialogs(false);
  const prodHtml=[prod.render(st,'3',false,['10','11','12','13']),prod.render(st,'3',true,['10','11','12','13'])];
  let demo=M.seed();demo=M.execute(demo,'cashier',{type:'CASH_BANK',amount:100000,bank:'SBI',slip:'DS-1',photo:'data:image/jpeg;base64,/9j/AAAA'});
  const did=M.cxSlice(demo).records.at(-1).id;
  const protoHtml=[proto.render(demo,'manager',false,[did]),proto.render(demo,'manager',true,[did])];
  test('[B1 sync] Every desktop and phone page renders on the loaded server data; the cash-out dialogs render in both builds, desktop and phone',()=>{
    for(const r of shown)assert.deepEqual(r.errors,{});
    for(const h of [...prodHtml,...protoHtml])for(const [k,v] of Object.entries(h))assert.ok(!v.startsWith('ERROR'),k+': '+v);
    assert.match(prodHtml[0]['decide:10'],/Slip photo purged after 600 days/);
    assert.match(protoHtml[0]['bank'],/capture="environment"/);assert.match(protoHtml[0]['bank'],/accept="image\/\*"/);
    assert.match(protoHtml[0]['decide:'+did],/View slip photo/,'the Manager may view the slip');
    assert.match(prodHtml[0]['panel'],/Banking, transfers and drawings/);
  });
}
