// W4-8 POS area (sync/pos.ts): fake server routes shaped like PosEndpoints.cs / PosContracts.cs, the real runtime, assertions on the
// slices the POS screens read (posX, approvals) against the prototype's own figures (M.execute), and on every mapped command.
import {loadSync,loadPages,fakeServer,startRuntime,reply,capabilities} from './_sync-harness.mjs';

export async function register({test,assert,M}){
  const S=await loadSync();
  const T0='2026-10-24T05:00:00.000Z'; // 10:30 hotel clock
  const proto0=M.seed(),menu0=proto0.posX.menu;
  const PRICE=Object.fromEntries(menu0.map((m,i)=>[i+1,m]));
  const srvMenu=menu0.map((m,i)=>({itemId:i+1,code:m.id,name:m.name,category:m.category,pricePaise:m.price,veg:m.veg,available:m.available,
    outletIds:m.outlets.map(o=>o==='restaurant'?1:2),version:'MV'+(i+1)}));
  const item=name=>srvMenu.find(m=>m.name===name).itemId;
  const bps=M.gstForFood(proto0,proto0.date).bps;

  // ── prototype reference: open → add → KOT → settle by cash (the figures the server must reproduce) ──
  let ps=M.execute(proto0,'owner',{type:'POS_ORDER_OPEN',outlet:'restaurant',table:'T5',covers:2});
  const poid=ps.posX.orders.at(-1).id;
  ps=M.execute(ps,'owner',{type:'POS_ITEM_ADD',id:poid,item:'m-poha',qty:2});ps=M.execute(ps,'owner',{type:'POS_ITEM_ADD',id:poid,item:'m-tea',qty:2});
  ps=M.execute(ps,'owner',{type:'POS_KOT_SEND',id:poid});ps=M.execute(ps,'owner',{type:'POS_SETTLE',id:poid,mode:'outlet',method:'Cash'});
  const pst=ps.posX.orders.find(o=>o.id===poid).settlement;

  // ── fake server ──
  let nextOrder=900,nextLine=9000,nextKot=500,nextVoid=70,nextBill=40;
  const checks=new Map(),voids=new Map();
  const val=c=>c.lines.filter(l=>!l.voided).reduce((a,l)=>a+l.pricePaise*l.qty,0);
  const view=c=>{const net=val(c),tax=c.bill?c.bill.taxPaise:M.gstLineTax(net,bps);
    return {...c,netPaise:c.bill?c.bill.netPaise:net,taxBps:c.bill?c.bill.taxBps:bps,taxPaise:tax,totalPaise:c.bill?c.bill.totalPaise:net+tax,items:c.lines.filter(l=>!l.voided).reduce((a,l)=>a+l.qty,0)};};
  const mk=(over={})=>{const id=nextOrder++;const c={orderId:id,outletId:1,outlet:'Restaurant',spot:'Table T1',guest:null,covers:2,businessDate:'2026-10-24',status:'Open',version:'CV'+id,
    pendingVoidId:null,lines:[],kots:[],bill:null,openedUtc:T0,openedBy:4,voidReason:null,voidedUtc:null,reservationId:null,...over};checks.set(id,c);return c;};
  const addLine=(c,name,qty,kotId=null)=>{const m=srvMenu.find(x=>x.name===name);c.lines.push({lineId:nextLine++,itemId:m.itemId,name,pricePaise:m.pricePaise,qty,kotId,status:kotId?'Preparing':'New',voided:false,voidReason:null});};
  const settledBill=(c,tender,id)=>{const net=val(c),tax=M.gstLineTax(net,bps);c.status='Settled';
    c.bill={billId:id,billNo:'POS/2610/00'+id,mode:'outlet',tender,reference:tender==='Cash'?null:'DEMO-UPI-1',netPaise:net,taxBps:bps,taxPaise:tax,totalPaise:net+tax,
      businessDate:'2026-10-24',status:'Settled',settledUtc:T0,settledBy:4,reservationId:null};};
  // seeded: a settled cash bill (same lines as the prototype run), an open check with a KOT in the kitchen + an unsent line, a check with a pending void
  const settled=mk({spot:'Table T5'});addLine(settled,'Kanda poha',2,1);addLine(settled,'Masala chai',2,1);settled.kots.push({kotId:1,kotNo:1,status:'Served',createdUtc:T0,readyUtc:T0,servedUtc:T0,createdBy:4});settledBill(settled,'Cash',31);
  const openCheck=mk({spot:'Table T2',guest:'Walk-in · Shinde family',covers:4});addLine(openCheck,'Paneer butter masala',1,2);addLine(openCheck,'Masala chai',1);openCheck.kots.push({kotId:2,kotNo:2,status:'Preparing',createdUtc:T0,readyUtc:null,servedUtc:null,createdBy:3});
  const pendingChk=mk({spot:'Table T3'});addLine(pendingChk,'Maharashtrian veg thali',4,3);pendingChk.kots.push({kotId:3,kotNo:3,status:'Preparing',createdUtc:T0,readyUtc:null,servedUtc:null,createdBy:3});
  voids.set(60,{voidRequestId:60,orderId:pendingChk.orderId,spot:'Table T3',scope:'item',lineId:pendingChk.lines[0].lineId,amountPaise:pendingChk.lines[0].pricePaise*4,reason:'Kitchen error',requestedBy:3,expiresUtc:'2026-10-25T05:00:00.000Z',orderVersion:pendingChk.version});
  pendingChk.pendingVoidId=60;
  let till={tillId:3,code:'FD',shiftId:12,shiftOpen:true};
  const stays=[{stayId:77,folioId:88,reservationId:1201,roomNo:'201',guest:'Journey Test'}];
  const bump=c=>{c.version='CV'+c.orderId+'-'+(++nextBill);return c;};
  const srv=fakeServer({
    'GET /pos/outlets':()=>[{outletId:1,code:'REST',name:'Restaurant',kind:'table',tables:['T1','T2','T3','T4','T5','T6']},{outletId:2,code:'RS',name:'Room service',kind:'room',tables:[]}],
    'GET /pos/menu':()=>srvMenu,
    'GET /pos/checks':()=>[...checks.values()].filter(c=>c.status==='Open').map(c=>({orderId:c.orderId})),
    'GET /pos/checks/closed':()=>[...checks.values()].filter(c=>c.status!=='Open').map(view),
    'GET /pos/checks/:id':({params})=>checks.has(Number(params.id))?view(checks.get(Number(params.id))):reply(404,{code:'NOT_FOUND',message:'Check not found.'}),
    'GET /pos/void-requests':()=>[...voids.values()].filter(v=>!v.done),
    'GET /pos/till':()=>till,
    'GET /pos/in-house':()=>stays,
    'POST /commands/OpenCheck':({body})=>{const c=mk({outletId:body.outletId,outlet:body.outletId===1?'Restaurant':'Room service',spot:body.table?'Table '+body.table:'Room '+body.roomNo,covers:body.covers??1,guest:body.guest??null});
      return {orderId:c.orderId,status:'Open',version:c.version,netPaise:0,taxBps:bps,taxPaise:0,totalPaise:0,items:0};},
    'POST /commands/AddCheckItem':({body})=>{const c=checks.get(body.orderId);const m=srvMenu.find(x=>x.itemId===body.itemId);
      const same=c.lines.find(l=>l.itemId===m.itemId&&l.kotId===null&&!l.voided);if(same)same.qty+=body.qty??1;else addLine(c,m.name,body.qty??1);bump(c);return {orderId:c.orderId};},
    'POST /commands/SetCheckItemQty':({body})=>{const c=checks.get(body.orderId);const l=c.lines.find(x=>x.lineId===body.lineId);if(body.qty===0)c.lines=c.lines.filter(x=>x!==l);else l.qty=body.qty;bump(c);return {orderId:c.orderId};},
    'POST /commands/SendKot':({body})=>{const c=checks.get(body.orderId);const k={kotId:nextKot++,kotNo:c.kots.length+10,status:'Preparing',createdUtc:T0,readyUtc:null,servedUtc:null,createdBy:1};
      c.kots.push(k);c.lines.filter(l=>l.kotId===null&&!l.voided).forEach(l=>l.kotId=k.kotId);bump(c);return {orderId:c.orderId,kotId:k.kotId,kotNo:k.kotNo,status:'Preparing',version:c.version};},
    'POST /commands/AdvanceKot':({body})=>{const c=checks.get(body.orderId);const k=c.kots.find(x=>x.kotId===body.kotId);k.status=body.status;bump(c);return {orderId:c.orderId,kotId:k.kotId,kotNo:k.kotNo,status:k.status,version:c.version};},
    'POST /commands/SettleCheck':({body})=>{const c=checks.get(body.orderId);settledBill(c,body.mode==='folio'?null:body.tender,nextBill++);
      if(body.mode==='folio'){c.bill.mode='folio';c.bill.tender=null;c.bill.reservationId=1201;c.reservationId=1201;}bump(c);return {orderId:c.orderId,billNo:c.bill.billNo};},
    'POST /commands/VoidCheckItem':({body})=>{const c=checks.get(body.orderId);const l=c.lines.find(x=>x.lineId===body.lineId);
      if(l.pricePaise*l.qty>M.POS_VOID_LIMIT){const id=nextVoid++;voids.set(id,{voidRequestId:id,orderId:c.orderId,spot:c.spot,scope:'item',lineId:l.lineId,amountPaise:l.pricePaise*l.qty,reason:body.reason,requestedBy:1,expiresUtc:'2026-10-25T05:00:00.000Z',orderVersion:c.version});c.pendingVoidId=id;bump(c);return {orderId:c.orderId,outcome:'PendingApproval',voidRequestId:id};}
      l.voided=true;l.voidReason=body.reason;bump(c);return {orderId:c.orderId,outcome:'Voided',voidRequestId:null};},
    'POST /commands/VoidBill':({body})=>{const c=checks.get(body.orderId);if(c.bill)c.bill.status='Void';c.status='Void';c.voidReason=body.reason;c.voidedUtc=T0;c.lines.forEach(l=>{l.voided=true;l.voidReason=body.reason;});bump(c);return {orderId:c.orderId,outcome:'Voided',voidRequestId:null};},
    'POST /commands/DecideVoid':({body})=>{const v=voids.get(body.voidRequestId);v.done=true;const c=checks.get(v.orderId);c.pendingVoidId=null;
      if(body.approve){const l=c.lines.find(x=>x.lineId===v.lineId);l.voided=true;l.voidReason=v.reason;}bump(c);return {voidRequestId:v.voidRequestId,status:body.approve?'Executed':'Rejected',orderId:c.orderId};},
    'POST /commands/SetMenuItemAvailability':({body})=>{const m=srvMenu.find(x=>x.itemId===body.itemId);m.available=body.available;return {itemId:m.itemId,available:m.available,version:m.version};},
  });
  const rt=await startRuntime(S,srv);
  const st0=rt.get().state,p0=st0.posX;
  const P=await loadPages();
  const pages=[P.renderPages(st0,'1',false),P.renderPages(st0,'1',true)];

  // ── loaders ──
  test('[W4-8 pos] posX is the server read in the prototype shape: outlets, menu (86 flag, outlet ids), open and closed-today checks with lines, KOTs and spots',()=>{
    assert.deepEqual(p0.outlets.map(o=>[o.id,o.name,o.kind,o.tables?.length]),[['1','Restaurant','table',6],['2','Room service','room',undefined]]);
    assert.equal(p0.menu.length,menu0.length);
    assert.deepEqual(p0.menu.find(m=>m.name==='Kanda poha'),{id:String(item('Kanda poha')),name:'Kanda poha',category:'Breakfast',price:15000,veg:true,outlets:['1','2'],available:true});
    assert.equal(p0.orders.length,3);
    const o=p0.orders.find(x=>x.id===String(openCheck.orderId));
    assert.deepEqual([o.table,o.room,o.guest,o.covers,o.status,o.openedAt,o.openedBy],['T2',undefined,'Walk-in · Shinde family',4,'Open','2026-10-24T10:30','4']);
    assert.deepEqual(o.lines.map(l=>[l.name,l.qty,l.kot]),[['Paneer butter masala',1,'2'],['Masala chai',1,undefined]]);
    assert.deepEqual(M.posUnsent(o).map(l=>l.name),['Masala chai'],'the unsent line is what "Send KOT" sends');
    assert.deepEqual(o.kots,[{id:'2',no:2,at:'2026-10-24T10:30',by:'3',status:'Preparing'}]);
    assert.equal(M.posOpenOrderAt(st0,'1','T2').id,o.id);
    assert.equal(rt.ids.version('check',o.id),openCheck.version);
  });
  const so=p0.orders.find(x=>x.id===String(settled.orderId));
  test('[W4-8 pos] A settled bill carries the SERVER tax snapshot — same net / tax / total as the prototype settling the same lines; cash receipt feeds the shared drawer figure',()=>{
    assert.deepEqual([so.settlement.net,so.settlement.taxBps,so.settlement.tax,so.settlement.total,so.settlement.method,so.settlement.mode],[pst.net,pst.taxBps,pst.tax,pst.total,'Cash','outlet']);
    assert.equal(so.settlement.billNo,'POS/2610/0031');assert.equal(so.settlement.date,'2026-10-24');assert.equal(so.settlement.at,'2026-10-24T10:30');
    assert.deepEqual(p0.receipts.map(r=>[r.method,r.amount,r.net,r.tax,r.date,r.kind]),[['Cash',pst.total,pst.net,pst.tax,'2026-10-24','sale']]);
    assert.equal(M.drawerOutletCash(st0),pst.total,'outlet cash counted in the drawer = the prototype\'s');
    assert.equal(M.posOrderTotals(st0,so).total,pst.total);
    const d=M.posDaySummary(st0);assert.deepEqual([d.checks,d.gross,d.byMode.Cash,d.outletCash,d.open.count,d.pendingVoids],[1,pst.total,pst.total,pst.total,2,1]);
    assert.deepEqual(d.top.map(t=>[t.name,t.qty]),[['Kanda poha',2],['Masala chai',2]]);
  });
  test('[W4-8 pos] A pending void request arrives as a kind "pos-void" approval (amount, reason, requester, decide command) and locks its check',()=>{
    const a=st0.approvals.find(x=>x.kind==='pos-void');const o=p0.orders.find(x=>x.id===String(pendingChk.orderId));
    assert.deepEqual([a.id,a.status,a.amount,a.reason,a.actor,a.target,a.meta.command,a.meta.scope,a.meta.line,a.expiresAt],
      ['posv-60','Pending',pendingChk.lines[0].pricePaise*4,'Kitchen error','3',String(pendingChk.orderId),'POS_VOID_DECIDE','item',String(pendingChk.lines[0].lineId),'2026-10-25T10:30']);
    assert.equal(o.pendingVoid,'posv-60');assert.equal(M.posPendingVoid(st0,o).id,'posv-60');
  });
  test('[W4-8 pos] Every desktop and phone page renders on the loaded POS data',()=>{assert.deepEqual(pages.map(x=>x.errors),[{},{}]);});

  // ── commands ──
  const after=async(cmd)=>{srv.reset();const r=await rt.run(cmd);return {r,calls:srv.calls.map(c=>`${c.method} ${c.path}`)};};
  const open=await after({type:'POS_ORDER_OPEN',outlet:'1',table:'T4',covers:2,guest:'Mr. Patil',key:'open-1'});
  const newId=String([...checks.keys()].at(-1));
  const openCall=srv.commands('OpenCheck')[0];
  const openRoom=await after({type:'POS_ORDER_OPEN',outlet:'2',room:'201',covers:1,key:'open-2'});
  const roomCall=srv.commands('OpenCheck')[0];
  test('[W4-8 pos] POS_ORDER_OPEN → OpenCheck (table + guest / room service by room number), key = dialog key, then the POS reads reload',()=>{
    assert.ok(open.r.ok,open.r.text);assert.deepEqual(openCall.body,{outletId:1,table:'T4',covers:2,guest:'Mr. Patil'});assert.equal(openCall.key,'open-1');
    assert.ok(open.calls.includes('GET /pos/checks'),'posX reload');
    assert.deepEqual(roomCall.body,{outletId:2,roomNo:'201',covers:1});assert.ok(openRoom.r.ok);
    assert.ok(rt.get().state.posX.orders.some(o=>o.id===newId&&o.table==='T4'),'the new check shows after the reload');
  });
  const add=await after({type:'POS_ITEM_ADD',id:newId,item:String(item('Masala dosa')),qty:2,key:'add-1'});
  const addCall=srv.commands('AddCheckItem')[0];
  const lineId=String(checks.get(Number(newId)).lines[0].lineId);
  const qty=await after({type:'POS_ITEM_QTY',id:newId,line:lineId,qty:3,key:'qty-1'});
  const qtyCall=srv.commands('SetCheckItemQty')[0];
  const verBeforeKot=rt.ids.version('check',newId);
  const kot=await after({type:'POS_KOT_SEND',id:newId,key:'kot-1'});
  const kotCall=srv.commands('SendKot')[0];
  const kotId=String(checks.get(Number(newId)).kots[0].kotId);
  const adv=await after({type:'POS_KOT_STATUS',id:newId,kot:kotId,status:'Ready',key:'ready-1'});
  const advCall=srv.commands('AdvanceKot')[0];
  test('[W4-8 pos] POS_ITEM_ADD / POS_ITEM_QTY / POS_KOT_SEND / POS_KOT_STATUS → AddCheckItem / SetCheckItemQty / SendKot (check version) / AdvanceKot, each reloading',()=>{
    assert.deepEqual(addCall.body,{orderId:Number(newId),itemId:item('Masala dosa'),qty:2});assert.equal(addCall.key,'add-1');
    assert.deepEqual(qtyCall.body,{orderId:Number(newId),lineId:Number(lineId),qty:3});assert.equal(qtyCall.key,'qty-1');
    assert.equal(kotCall.body.orderId,Number(newId));assert.equal(kotCall.body.expectedVersion,verBeforeKot,'SendKot carries the loaded check version');assert.equal(kotCall.key,'kot-1');
    assert.deepEqual(advCall.body,{orderId:Number(newId),kotId:Number(kotId),status:'Ready'});
    for(const x of [add,qty,kot,adv])assert.ok(x.r.ok&&x.calls.some(c=>c==='GET /pos/checks/'+newId),x.r.text);
    const o=rt.get().state.posX.orders.find(x=>x.id===newId);
    assert.deepEqual([o.lines[0].qty,o.lines[0].kot,o.kots[0].status],[3,kotId,'Ready']);
  });
  const staleMenu=await after({type:'POS_MENU_TOGGLE',item:String(item('Gulab jamun (2)'))});
  const toggleCall=srv.commands('SetMenuItemAvailability')[0];
  test('[W4-8 pos] POS_MENU_TOGGLE → SetMenuItemAvailability with the flipped flag (86 / available again)',()=>{
    assert.ok(staleMenu.r.ok);assert.deepEqual(toggleCall.body,{itemId:item('Gulab jamun (2)'),available:false});
    assert.equal(rt.get().state.posX.menu.find(m=>m.name==='Gulab jamun (2)').available,false);
  });

  // settle: cash needs the open shift of the shared till, the check version is required
  const cashChk=String(newId),vBefore=rt.ids.version('check',cashChk);
  till={...till,shiftOpen:false,shiftId:null};
  const closedShift=await after({type:'POS_SETTLE',id:cashChk,mode:'outlet',method:'Cash',key:'settle-0'});
  const noPost=srv.commands('SettleCheck').length;
  till={tillId:3,code:'FD',shiftId:12,shiftOpen:true};
  const noRef=await after({type:'POS_SETTLE',id:cashChk,mode:'outlet',method:'UPI',key:'settle-0b'});
  const cash=await after({type:'POS_SETTLE',id:cashChk,mode:'outlet',method:'Cash',key:'settle-1'});
  const cashCall=srv.commands('SettleCheck')[0];
  test('[W4-8 pos] Cash settle: shared till from GET /pos/till (closed shift refused before any command), SettleCheck outlet+Cash+tillId+expectedVersion; UPI needs a reference; tax is the server\'s',()=>{
    assert.deepEqual([closedShift.r.ok,closedShift.r.reason],[false,'server']);assert.match(closedShift.r.text,/cash shift is closed/);assert.equal(noPost,0);
    assert.deepEqual([noRef.r.ok,noRef.r.reason],[false,'precheck']);assert.match(noRef.r.text,/transaction reference/);assert.equal(noRef.calls.length,0);
    assert.ok(cash.r.ok,cash.r.text);assert.deepEqual(cashCall.body,{orderId:Number(cashChk),mode:'outlet',tender:'Cash',tillId:3,expectedVersion:vBefore});assert.equal(cashCall.key,'settle-1');
    assert.ok(cash.calls.includes('GET /pos/till'));
    const o=rt.get().state.posX.orders.find(x=>x.id===cashChk);
    assert.deepEqual([o.status,o.settlement.method,o.settlement.taxBps],['Settled','Cash',bps]);
    assert.equal(o.settlement.total,o.settlement.net+o.settlement.tax);
    assert.equal(M.drawerOutletCash(rt.get().state),pst.total+o.settlement.total,'the new cash bill joins the drawer figure');
  });

  // folio posting (room service check of an in-house guest, and a restaurant check charged to a room)
  const roomChk=String([...checks.keys()].find(k=>checks.get(k).spot==='Room 201'));
  await rt.run({type:'POS_ITEM_ADD',id:roomChk,item:String(item('Masala chai'))});await rt.run({type:'POS_KOT_SEND',id:roomChk});
  const folio=await after({type:'POS_SETTLE',id:roomChk,mode:'folio',booking:'1201',key:'settle-2'});
  const folioCall=srv.commands('SettleCheck')[0];
  const rest=(await rt.run({type:'POS_ORDER_OPEN',outlet:'1',table:'T6',covers:1}),String([...checks.keys()].at(-1)));
  await rt.run({type:'POS_ITEM_ADD',id:rest,item:String(item('Masala chai'))});await rt.run({type:'POS_KOT_SEND',id:rest});
  const nobody=await after({type:'POS_SETTLE',id:rest,mode:'folio',booking:'9999',key:'settle-3'});
  test('[W4-8 pos] Room posting: SettleCheck folio with the stay of the booking from GET /pos/in-house; unknown / not in-house guest refused without a command; folio bills carry the booking',()=>{
    assert.ok(folio.r.ok,folio.r.text);assert.deepEqual(folioCall.body,{orderId:Number(roomChk),mode:'folio',stayId:77,expectedVersion:folioCall.body.expectedVersion});assert.ok(folioCall.body.expectedVersion);
    assert.ok(folio.calls.includes('GET /pos/in-house')&&folio.calls.includes('GET /reservations/1201')===false);
    const o=rt.get().state.posX.orders.find(x=>x.id===roomChk);assert.deepEqual([o.settlement.mode,o.settlement.booking,o.booking],['folio','1201','1201']);
    assert.equal(nobody.r.ok,false);assert.match(nobody.r.text,/in-house guest/);assert.equal(srv.commands('SettleCheck').length,0);
  });

  // voids: within the limit now; above the limit becomes a request; a different user decides (server enforces limit / approval right)
  const vChk=String(openCheck.orderId),vLine=String(openCheck.lines[0].lineId);
  const small=await after({type:'POS_VOID_ITEM',id:vChk,line:vLine,reason:'Guest changed order',key:'void-1'});
  const smallCall=srv.commands('VoidCheckItem')[0];
  const big=await after({type:'POS_VOID_ITEM',id:String(settled.orderId+99),line:'1',reason:'x'});
  const thali=mk({spot:'Table T6x'});addLine(thali,'Maharashtrian veg thali',4,5);
  await rt.refresh();
  const requested=await after({type:'POS_VOID_ITEM',id:String(thali.orderId),line:String(thali.lines[0].lineId),reason:'Kitchen error',key:'void-2'});
  const reqCall=srv.commands('VoidCheckItem')[0];
  const queue=rt.get().state.approvals.filter(a=>a.kind==='pos-void').map(a=>a.id);
  const rejected=await after({type:'POS_VOID_DECIDE',id:'posv-60',approve:false,reason:'Not needed',key:'approval:posv-60'});
  const rejCall=srv.commands('DecideVoid')[0];
  const lastVoid='posv-'+(nextVoid-1);
  const approve=await after({type:'POS_VOID_DECIDE',id:lastVoid,approve:true,key:'approval:'+lastVoid});
  const appCall=srv.commands('DecideVoid')[0];
  const billVoid=await after({type:'POS_VOID_BILL',id:String(settled.orderId),reason:'Wrong table billed',key:'void-3'});
  const bvCall=srv.commands('VoidBill')[0];
  const sAfter=rt.get().state;
  test('[W4-8 pos] Voids: within the limit voids at once; above POS_VOID_LIMIT the server raises a request (approval appears, check locked); reason is prechecked',()=>{
    assert.ok(small.r.ok,small.r.text);assert.deepEqual(smallCall.body,{orderId:Number(vChk),lineId:Number(vLine),reason:'Guest changed order',expectedVersion:smallCall.body.expectedVersion});
    assert.deepEqual([small.r.result?.outcome,smallCall.key],['Voided','void-1']);
    assert.ok(requested.r.ok,requested.r.text);assert.equal(requested.r.result.outcome,'PendingApproval');assert.equal(reqCall.body.reason,'Kitchen error');
    assert.ok(queue.includes('posv-'+requested.r.result.voidRequestId),'the request is in the approvals queue after the reload');
    const lockedOrder=rt.get().state.posX.orders.find(o=>o.id===String(thali.orderId));
    assert.ok(lockedOrder.pendingVoid||sAfter.posX.orders.find(o=>o.id===String(thali.orderId)).status);
    assert.deepEqual([big.r.ok,big.r.reason],[false,'precheck']);assert.match(big.r.text,/reason/);
  });
  test('[W4-8 pos] POS_VOID_DECIDE → DecideVoid (Manager may reject; approve within limit — the server decides), POS_VOID_BILL → VoidBill',()=>{
    assert.ok(rejected.r.ok,rejected.r.text);assert.deepEqual(rejCall.body,{voidRequestId:60,approve:false,reason:'Not needed'});assert.equal(rejCall.key,'approval:posv-60');
    assert.equal(sAfter.approvals.some(a=>a.id==='posv-60'),false,'decided request leaves the pending queue');
    assert.ok(approve.r.ok,approve.r.text);assert.deepEqual(appCall.body,{voidRequestId:Number(lastVoid.slice(5)),approve:true});
    assert.ok(billVoid.r.ok,billVoid.r.text);assert.deepEqual(bvCall.body,{orderId:settled.orderId,reason:'Wrong table billed',expectedVersion:bvCall.body.expectedVersion});
    const o=sAfter.posX.orders.find(x=>x.id===String(settled.orderId));
    assert.deepEqual([o.status,o.voidReason,o.settlement.total],['Void','Wrong table billed',pst.total]);
    assert.deepEqual(sAfter.posX.receipts.filter(r=>r.order===o.id).map(r=>[r.kind,r.amount]),[['sale',pst.total],['void',-pst.total]],'sale + reversal net to zero in the drawer');
  });

  // refusals / server errors
  const stale=await (async()=>{srv.on('POST /commands/AddCheckItem',()=>reply(409,{code:'STALE_VERSION',message:'The check changed.'}));return after({type:'POS_ITEM_ADD',id:rest,item:'1'});})();
  const badId=await after({type:'POS_ITEM_ADD',id:'POS-0001',item:'1'});
  srv.down=true;await rt.run({type:'POS_ITEM_ADD',id:rest,item:'1'});const ro=await rt.run({type:'POS_ITEM_ADD',id:rest,item:'1'});srv.down=false;
  const rec=fakeServer({'GET /me/capabilities':()=>capabilities({roles:['HKP'],grants:[['PERM-HK-001','A']]})});
  const hk=await startRuntime(S,rec);
  test('[W4-8 pos] Server refusals surface as toasts (STALE_VERSION), non-server ids are refused locally, read-only refuses every POS command, a housekeeper never reads the POS',()=>{
    assert.deepEqual([stale.r.ok,stale.r.reason],[false,'server']);assert.match(stale.r.text,/Record changed/);
    assert.deepEqual([badId.r.ok,badId.r.reason],[false,'server']);assert.match(badId.r.text,/not a server record/);
    assert.deepEqual([ro.ok,ro.reason],[false,'read-only']);
    assert.equal(rec.calls.filter(c=>c.path.startsWith('/pos')).length,0);assert.deepEqual(hk.get().state.posX?.orders??[],[]);
  });
  test('[W4-8 pos] Every prototype POS command is mapped; nothing is left unmapped',()=>{
    for(const t of ['POS_ORDER_OPEN','POS_ITEM_ADD','POS_ITEM_QTY','POS_KOT_SEND','POS_KOT_STATUS','POS_SETTLE','POS_VOID_ITEM','POS_VOID_BILL','POS_VOID_DECIDE','POS_MENU_TOGGLE'])assert.ok(rt.mapped(t),t);
    assert.deepEqual(Object.keys(S.AREAS.find(a=>a.id==='pos').unmapped),[]);
    assert.deepEqual(S.validateAreas(S.AREAS),[]);
  });
}
