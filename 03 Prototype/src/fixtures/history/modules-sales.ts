// FY history Phase C (part 3): module 23 restaurant & room service, 21–22 OTA channels and the direct booking site, 24–25 messages and
// document capture, 20 accounting month locks. Runs last: the accounting snapshots are taken from the final folios, receipts and expenses.
import type {State,Approval} from '../../domain/types';
import {addDays,nights} from '../../domain/money';
import {taxRateFor} from '../../domain/selectors';
import type {POSOrder,POSReceipt,POSLine} from '../../domain/streams/pos';
import type {CHNMessage,CHNPush,CHNWebRequest} from '../../domain/streams/channels';
import {MSG_DEFAULT_TEMPLATES,MSG_DEFAULT_RULES,msgTriggerEvents,msgRender,msgValues,msgRecipient,type MSGMessage,type MSGChannel,type MSGDoc} from '../../domain/streams/messaging';
import {accLiveVouchers,accTotals,type ACCLock,type ACCLockEvent} from '../../domain/streams/accounting';
import type {Rng} from './prng';
import {pad,type HistoryContext,type HistoryWindow} from './util';

export function addSalesHistory(s:State,rng:Rng,o:HistoryWindow,ctx:HistoryContext):void{
  posHistory(s,rng,o,ctx);
  channelsHistory(s,rng,o);
  messagingHistory(s,rng,o);
  accountingLocks(s,o);
}

function occupancyByDate(s:State,o:HistoryWindow){
  const occ=new Map<string,number>();
  for(const b of s.bookings)if(b.status==='Departed'||b.status==='In house')for(let d=b.arrival;d<b.departure&&d<=o.to;d=addDays(d,1))occ.set(d,(occ.get(d)??0)+1);
  return occ;
}

// ───────────── Module 23 · Restaurant & room service ─────────────
function posHistory(s:State,rng:Rng,o:HistoryWindow,ctx:HistoryContext){
  const p=s.posX;if(!p)return;
  const occ=occupancyByDate(s,o);
  const menuR=p.menu.filter(m=>m.outlets.includes('restaurant'));
  const orders:POSOrder[]=[],receipts:POSReceipt[]=[],approvals:Approval[]=[];
  let on=0,rc=0,ap=0;
  const meals:[number,number,string[]][]=[[7*60+30,10*60,['Breakfast','Beverages']],[12*60+30,14*60+30,['Starters','Mains','Breads & rice','Beverages','Desserts']],[19*60+30,22*60,['Starters','Mains','Breads & rice','Beverages','Desserts']]];
  const hm=(m:number)=>pad(Math.floor(m/60),2)+':'+pad(m%60,2);
  for(let d=o.from;d<=o.to;d=addDays(d,1)){
    const checks=Math.round((occ.get(d)??0)*0.2)+rng.int(1,4);
    for(let k=0;k<checks;k++){
      const [from,to,cats]=rng.weighted([[meals[0],40],[meals[1],25],[meals[2],35]] as const);
      const t=rng.int(from,to),id='POS-HR'+pad(++on,5),pool=menuR.filter(m=>cats.includes(m.category));
      const picks=[...new Set([rng.pick(pool),rng.pick(pool),rng.pick(pool),rng.pick(pool)].slice(0,rng.int(2,4)))];
      const lines:POSLine[]=picks.map((m,i)=>({id:id+'-L'+(i+1),item:m.id,name:m.name,price:m.price,qty:rng.int(1,3),kot:id+'-K1'}));
      if(lines.length>1&&rng.chance(0.012)){const l=lines[lines.length-1];l.voided=true;l.voidReason='Wrong item entered';l.voidBy='cashier';l.voidDate=d;}
      const active=lines.filter(l=>!l.voided),net=active.reduce((a,l)=>a+l.price*l.qty,0),taxBps=taxRateFor(s,d),tax=Math.round(net*taxBps/10000);
      const method=rng.weighted([['Cash',40],['UPI',45],['Card',15]] as const),by=rng.chance(0.6)?'cashier':'reception';
      const rid='POSR-H'+pad(++rc,5);
      const o2:POSOrder={id,outlet:'restaurant',table:'T'+rng.int(1,8),covers:rng.int(1,5),date:d,openedAt:d+'T'+hm(t),openedBy:by,status:'Settled',version:4,lines,
        kots:[{id:id+'-K1',no:0,at:d+'T'+hm(t+1),by,status:'Served',readyAt:d+'T'+hm(t+12),servedAt:d+'T'+hm(t+15)}],
        settlement:{mode:'outlet',billNo:'',method,...(method==='Cash'?{}:{reference:`DEMO-${method.toUpperCase()}-POS-${pad(on)}`}),net,taxBps,tax,total:net+tax,date:d,at:d+'T'+hm(t+40),by,receiptId:rid}};
      orders.push(o2);
      receipts.push({id:rid,order:id,outlet:'restaurant',method,amount:net+tax,net,tax,taxBps,reference:method==='Cash'?'Outlet cash':o2.settlement!.reference!,date:d,actor:by,kind:'sale'});
    }
  }
  // A handful of whole bills voided the same day with a manager's approval (above the ₹500 limit).
  const big=orders.filter(x=>x.settlement!.total>=60000&&!x.lines.some(l=>l.voided));
  for(let k=0;k<6&&big.length;k++){
    const x=big.splice(rng.int(0,big.length-1),1)[0],st=x.settlement!,aid='APR-HP'+pad(++ap,3);
    approvals.push({id:aid,booking:'',amount:st.total,reason:rng.pick(['Wrong table billed','Duplicate bill','Guest left before service']),status:'Executed',actor:'cashier',version:x.version,kind:'pos-void',target:x.id,approver:'manager',decidedAt:x.date+'T'+hm(23*60-30),
      expiresAt:addDays(x.date,1)+'T21:00',meta:{command:'POS_VOID_DECIDE',scope:'bill',summary:'Void bill · Table '+x.table}});
    const a=approvals.at(-1)!;
    x.lines.forEach(l=>{l.voided=true;l.voidReason=a.reason;l.voidBy='cashier';l.voidDate=x.date;l.voidApproval=aid;});
    x.status='Void';x.voidReason=a.reason;x.voidedBy='cashier';x.voidedAt=x.date+'T'+hm(23*60-30);x.version++;
    receipts.push({id:'POSR-H'+pad(++rc,5),order:x.id,outlet:'restaurant',method:st.method!,amount:-st.total,net:-st.net,tax:-st.tax,taxBps:st.taxBps,reference:'Void of billNo',date:x.date,actor:'cashier',kind:'void'});
  }
  // Bill and KOT numbers follow the order of service (restaurant and room-service checks together).
  const all=[...orders,...ctx.roomOrders.map(r=>r.order)].sort((a,b)=>a.openedAt<b.openedAt?-1:a.openedAt>b.openedAt?1:a.id<b.id?-1:1);
  const lineOf=new Map(ctx.roomOrders.map(r=>[r.order.id,r.line]));
  const kotSeq:Record<string,number>={};let bill=2;
  const voidRef=new Map<string,string>();
  for(const x of all){
    const st=x.settlement!;bill++;st.billNo=`POS/${st.date.slice(2,4)}${st.date.slice(5,7)}/${pad(bill)}`;
    voidRef.set(x.id,st.billNo);
    const key=x.outlet+':'+x.date;kotSeq[key]=(kotSeq[key]??0)+1;x.kots[0].no=kotSeq[key];
    const l=lineOf.get(x.id);if(l){const n=x.lines.reduce((a,q)=>a+q.qty,0);l.label=`Room service · bill ${st.billNo} · ${n} item${n===1?'':'s'}`;}
  }
  receipts.forEach(r=>{if(r.kind==='void')r.reference='Void of '+voidRef.get(r.order);});
  receipts.sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:a.id<b.id?-1:1);
  p.orders=[...all,...p.orders];p.receipts=[...receipts,...p.receipts];
  p.billSeq=Math.max(p.billSeq,bill);
  for(const [k,v] of Object.entries(kotSeq))p.kotSeq[k]=Math.max(p.kotSeq[k]??0,v);
  s.approvals=[...approvals,...s.approvals];
}

// ───────────── Module 21–22 · OTA channels and the direct booking site ─────────────
function channelsHistory(s:State,rng:Rng,o:HistoryWindow){
  const x=s.channelsX;if(!x)return;
  const msgs:CHNMessage[]=[],web:CHNWebRequest[]=[];let mn=0,wn=0;
  const prefix:Record<string,string>={booknest:'BN',stayhub:'SH',tripkart:'TK'};
  const typeOf=(room:string)=>s.rooms.find(r=>r.id===room)!.type;
  for(const b of s.bookings){
    if(!b.id.startsWith('HCC-H'))continue;
    const n=nights(b.arrival,b.departure);
    if(b.otaChannel&&(b.status==='Departed'||b.status==='Cancelled'||b.status==='No show')){
      const c=x.channels.find(c=>c.name===b.otaChannel);if(!c)continue;
      const lead=rng.int(0,20),rec=addDays(b.arrival,-lead)<o.from?o.from:addDays(b.arrival,-lead),ref=`${prefix[c.id]??'OT'}-${70000+(++mn)}`;
      const base:Omit<CHNMessage,'id'|'kind'|'status'|'receivedAt'>={channel:c.id,ref,guest:b.guest,phone:b.phone,email:b.email,roomType:typeOf(b.room),arrival:b.arrival,departure:b.departure,adults:Math.max(1,b.people-(b.children??0)),children:b.children??0,
        amount:Math.round(b.rate*n*(10000+c.markupBps)/10000)};
      const at=rec+'T'+pad(rng.int(6,22),2)+':'+pad(rng.int(0,59),2);
      msgs.push({...base,id:'CHM-H'+pad(mn),kind:'New',receivedAt:at,status:'Applied',booking:b.id,room:b.room,decidedBy:'reception',decidedAt:at.slice(0,13)+':59'});
      if(b.status!=='Departed'){
        const cat=addDays(b.arrival,-rng.int(0,2))<rec?rec:addDays(b.arrival,-rng.int(0,2));
        msgs.push({...base,id:'CHM-H'+pad(mn)+'C',kind:'Cancel',amount:0,receivedAt:cat+'T10:00',status:'Applied',booking:b.id,room:b.room,decidedBy:'reception',decidedAt:cat+'T10:30'});
      }
    }
    if(b.source==='Website'){
      const net=b.rate*n,tax=Math.round(net*(b.lines[0]?.taxBps??1000)/10000),created=addDays(b.arrival,-rng.int(1,15))<o.from?o.from:addDays(b.arrival,-rng.int(1,15));
      web.push({id:'WEB-H'+pad(++wn),booking:b.id,guest:b.guest,phone:b.phone??'',email:b.email,roomType:typeOf(b.room),arrival:b.arrival,departure:b.departure,adults:Math.max(1,b.people-(b.children??0)),children:b.children??0,net,tax,total:net+tax,
        createdAt:created+'T'+pad(rng.int(8,22),2)+':00',status:b.status==='Departed'?'Confirmed':'Declined',decidedBy:'reception',...(b.status==='Departed'?{}:{reason:'Payment not completed within the hold period'}),notes:'Direct website request (simulated)'});
    }
  }
  // Requests the front desk rejected because no room of the type was free when the message arrived.
  const days=nights(o.from,o.to);
  for(let k=0;k<18;k++){
    const c=rng.pick(x.channels.filter(c=>prefix[c.id])),d=addDays(o.from,rng.int(3,days-5)),t=rng.pick(['Classic','Deluxe','Suite']),arr=addDays(d,rng.int(2,10));
    msgs.push({id:'CHM-H'+pad(++mn)+'R',channel:c.id,kind:'New',ref:`${prefix[c.id]}-${70000+mn}`,guest:'Guest request '+mn,roomType:t,arrival:arr,departure:addDays(arr,rng.int(1,3)),adults:2,children:0,amount:0,receivedAt:d+'T'+pad(rng.int(6,22),2)+':15',
      status:'Rejected',reason:'No room of this type was available for those dates',decidedBy:'reception',decidedAt:d+'T'+pad(rng.int(6,22),2)+':45'});
  }
  msgs.sort((a,b)=>a.receivedAt<b.receivedAt?-1:a.receivedAt>b.receivedAt?1:a.id<b.id?-1:1);
  web.sort((a,b)=>a.createdAt<b.createdAt?-1:1);
  // Weekly rate and availability push to every connected channel.
  const pushes:CHNPush[]=[];let pn=0;
  for(let d=o.from;d<=o.to;d=addDays(d,1)){
    if(new Date(d+'T12:00:00Z').getUTCDay()!==1)continue;
    for(const c of x.channels.filter(c=>prefix[c.id]))pushes.push({id:'CHP-H'+pad(++pn),at:d+'T09:00',actor:'manager',channel:c.id,rows:21,closed:rng.int(0,3),summary:'21 rate/availability rows pushed (weekly review)'});
  }
  x.messages=[...msgs,...x.messages];x.pushes=[...pushes,...x.pushes];x.webRequests=[...web,...x.webRequests];
}

// ───────────── Module 24–25 · Messages and document capture ─────────────
function messagingHistory(s:State,rng:Rng,o:HistoryWindow){
  const x=s.messagingX;if(!x)return;
  const from60=addDays(o.to,-59),outbox:MSGMessage[]=[];let n=0;
  const tpl=(id:string)=>MSG_DEFAULT_TEMPLATES.find(t=>t.id===id)!;
  const outcome=()=>rng.weighted([['Read',70],['Delivered',25],['Failed',5]] as const);
  const add=(b:State['bookings'][number],template:MSGMessage['template'],channel:MSGChannel,queuedAt:string,rule:string)=>{
    const t=tpl(template as string),v=msgValues(s,b);
    const m:MSGMessage={id:'MSG-H'+pad(++n,5),template,channel,booking:b.id,guestKey:'B:'+b.id,guest:b.guest,to:msgRecipient(s,b,channel).masked,body:msgRender(t.body,v).text,queuedAt,outcome:outcome(),actor:'reception',rule};
    if(channel==='Email')m.subject=msgRender(t.subject,v).text;
    outbox.push(m);
  };
  for(const b of s.bookings){
    if(!b.id.startsWith('HCC-H')||b.status!=='Departed'||b.arrival<from60)continue;
    const conf=addDays(b.arrival,-rng.int(0,10));
    add(b,'booking-confirmation','WhatsApp',(conf<from60?from60:conf)+'T'+pad(rng.int(8,21),2)+':'+pad(rng.int(0,59),2),'R1');
    for(const r of b.receipts.filter(r=>r.kind==='payment'&&r.date<=o.to))add(b,'payment-receipt','SMS',(r.date<from60?from60:r.date)+'T'+pad(rng.int(9,21),2)+':'+pad(rng.int(0,59),2),'R4');
    add(b,'checkout-invoice','Email',b.departure+'T11:30','R5');
  }
  outbox.sort((a,b)=>a.queuedAt<b.queuedAt?-1:a.queuedAt>b.queuedAt?1:a.id<b.id?-1:1);
  x.outbox=[...outbox,...(x.outbox??[])];
  // Every trigger of a history stay counts as handled, so only today's real triggers show as pending.
  const fired=new Set(x.fired??[]);
  for(const t of msgTriggerEvents(s,MSG_DEFAULT_RULES))if(t.booking.id.startsWith('HCC-H'))fired.add(t.key);
  x.fired=[...fired];
  // A few captured documents applied to real records (vendor bills → posted expenses, ID cards → departed guests) and two discarded.
  const docs:MSGDoc[]=[];
  const exps=s.expenses.filter(e=>e.id.startsWith('EXP-H')&&e.status==='Posted'&&!e.reversalOf&&(e.category==='Supplies'||e.category==='Maintenance')&&e.date>=addDays(o.to,-90));
  const pick=<T,>(xs:T[])=>xs.splice(rng.int(0,xs.length-1),1)[0];
  for(let k=0;k<8&&exps.length;k++){
    const e=pick(exps),at=e.date+'T'+pad(rng.int(9,19),2)+':10';
    docs.push({id:'DOC-H'+pad(docs.length+1,2),sample:'S-BILL-1',kind:'vendor-bill',title:'Vendor bill · '+e.vendor,status:'Applied',actor:'reception',at,appliedTo:e.id,appliedAt:at.slice(0,11)+'19:40',
      fields:[{key:'vendor',label:'Vendor',value:e.vendor,confidence:0.94},{key:'billNo',label:'Bill no.',value:'B/'+pad(rng.int(100,9999)),confidence:0.88},{key:'billDate',label:'Bill date',value:e.date,confidence:0.92},
        {key:'amount',label:'Amount (₹)',value:(e.amount/100).toFixed(2),confidence:0.9},{key:'category',label:'Category',value:e.category,confidence:0.85}]});
  }
  const stays=s.bookings.filter(b=>b.id.startsWith('HCC-H')&&b.status==='Departed'&&b.arrival>=addDays(o.to,-90));
  for(let k=0;k<4&&stays.length;k++){
    const b=pick(stays),at=b.arrival+'T15:20',last4=pad(rng.int(0,9999));
    docs.push({id:'DOC-H'+pad(docs.length+1,2),sample:'S-ID-1',kind:'id-card',title:'ID card · '+b.guest,status:'Applied',actor:'reception',at,appliedTo:b.id,appliedAt:at.slice(0,11)+'15:25',
      fields:[{key:'name',label:'Name',value:b.guest,confidence:0.95},{key:'idType',label:'ID type',value:'Aadhaar (demo)',confidence:0.95},{key:'idMasked',label:'ID number (masked)',value:'XXXX XXXX '+last4,confidence:0.93,locked:true},
        {key:'idLast4',label:'ID last 4',value:last4,confidence:0.93},{key:'city',label:'City',value:'Latur',confidence:0.88}]});
  }
  for(let k=0;k<2;k++){const e=exps.length?pick(exps):undefined;const at=addDays(o.to,-rng.int(5,40))+'T11:00';
    docs.push({id:'DOC-H'+pad(docs.length+1,2),sample:'S-BILL-2',kind:'vendor-bill',title:'Vendor bill (blurred scan)',status:'Discarded',actor:'reception',at,reason:'Photo too blurry; asked the vendor for a copy',
      fields:[{key:'vendor',label:'Vendor',value:e?.vendor??'Unknown',confidence:0.5},{key:'amount',label:'Amount (₹)',value:'0.00',confidence:0.4}]});}
  docs.sort((a,b)=>a.at<b.at?-1:1);docs.forEach((d,i)=>{d.id='DOC-H'+pad(i+1,2);});
  x.docs=[...docs,...(x.docs??[])];
}

// ───────────── Module 20 · Accounting: every day of April–September locked ─────────────
function accountingLocks(s:State,o:HistoryWindow){
  const locks:Record<string,ACCLock>={},events:ACCLockEvent[]=[];let n=0;
  const lastLocked='2026-09-30';
  for(let d=o.from;d<=lastLocked&&d<=o.to;d=addDays(d,1)){
    const vouchers=accLiveVouchers(s,d),t=accTotals(vouchers);
    const monthEnd=addDays(d.slice(0,8)+'01',32).slice(0,8)+'01';
    const at=addDays(monthEnd,4)+'T17:00'; // locked in the first week of the next month
    locks[d]={date:d,lockedAt:at,lockedBy:'accountant',vouchers,debit:t.debit,credit:t.credit,count:vouchers.length};
    events.push({id:'ACCL-H'+pad(++n),at,actor:'accountant',date:d,action:'lock'});
  }
  const cur=s.accountingX??{};
  s.accountingX={...cur,locks:{...locks,...(cur.locks??{})},lockHistory:[...events,...(cur.lockHistory??[])]};
}
