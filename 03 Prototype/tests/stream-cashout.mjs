// Wave B1 · cash leaving the drawer (DEC-073) and the shared till next morning (DEC-078) in the demo build: mock rules, drawer / cash book /
// accounting figures (worked out by hand: BASE drawer ₹10,725 = 1,072,500 paise), and the on-device slip-photo compression helper.
import {build} from 'esbuild';
import {tillClear} from './_till.mjs';
await build({entryPoints:['src/app/slipPhoto.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/slipphoto.mjs',logLevel:'error'});
const P=await import('./.generated/slipphoto.mjs');

const code=c=>e=>e?.code===c;
/** A data URL of a "JPEG" of exactly n bytes (what the compression helper hands the dialog). */
const jpeg=n=>P.slipDataUrl(new Uint8Array(n).fill(0xff));

// ── compression (async parts once, asserted below) ──
const fake=k=>{const calls=[];return {calls,enc:async(w,h,q)=>{calls.push([w,h,q]);return new Uint8Array(Math.round(w*h*q*k));}};};
const R={};
{
  const a=fake(0.3);R.big=await P.slipCompressPlan(4000,3000,a.enc);R.bigCalls=a.calls;
  const b=fake(0.3);R.small=await P.slipCompressPlan(600,400,b.enc);R.smallCalls=b.calls;
  const n=[];R.never=await P.slipCompressPlan(4000,3000,async(w,h,q)=>{n.push([w,h,q]);return new Uint8Array(150000);}).then(()=>null,e=>e);R.neverCalls=n;
  R.edge=await P.slipCompressPlan(1000,800,async()=>new Uint8Array(102400)).then(()=>null,e=>e);
  R.under=await P.slipCompressPlan(1000,800,async()=>new Uint8Array(102399));
  R.portrait=await P.slipCompressPlan(3000,4000,fake(0.3).enc);
}

export function register({test,M,assert}){
  const {seed,execute,drawer}=M;
  const ready=()=>{let s=seed();for(const b of s.bookings.filter(b=>b.status==='Confirmed')){s=execute(s,'owner',{type:'REGISTER',id:b.id});s=execute(s,'owner',{type:'CHECKIN',id:b.id});}
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});return execute(s,'owner',{type:'NIGHT_POST'});};
  const closeDay=s=>execute(tillClear(M,s),'owner',{type:'DAY_CLOSE'});
  const bank=(s,uid,amount,over={})=>execute(s,uid,{type:'CASH_BANK',amount,bank:'State Bank of India · current a/c ••4321 (fictional)',slip:'DS-1001',photo:jpeg(2048),...over});
  const last=s=>M.cxSlice(s).records.at(-1);

  test('[B1 compress] A 4000×3000 photo becomes a JPEG under 100 KB with the long edge ≤ 1,280 px (stepping quality, then size)',()=>{
    // fake JPEG size = w·h·q·0.3: 1280×960 never fits (q 0.32 → 117,965 B); 1024×768 at q 0.42 → 99,090 B (< 102,400).
    assert.deepEqual([R.big.width,R.big.height,R.big.quality,R.big.bytes.length],[1024,768,0.42,99090]);
    assert.ok(R.big.bytes.length<102400&&Math.max(R.big.width,R.big.height)<=1280);
    assert.equal(R.bigCalls.length,6+5,'six qualities at 1280, five at 1024');
    assert.deepEqual(R.bigCalls[0],[1280,960,0.82]);
    assert.deepEqual([R.portrait.width,R.portrait.height],[768,1024],'portrait keeps its aspect ratio');
    assert.deepEqual([R.small.width,R.small.height,R.small.quality,R.smallCalls.length],[600,400,0.82,1],'a small photo is never upscaled');
  });
  test('[B1 compress] A photo that cannot get under 100 KB is refused (18 tries: 3 sizes × 6 qualities); 102,400 B is refused, 102,399 B kept',()=>{
    assert.match(R.never.message,/cannot be made smaller than 100 KB/);assert.equal(R.neverCalls.length,18);
    assert.match(R.edge.message,/100 KB/);assert.equal(R.under.bytes.length,102399);
    assert.equal(P.SLIP_MAX_BYTES,102399);assert.equal(M.CX_SLIP_MAX_BYTES,102399);
    assert.equal(P.slipDataUrl(new Uint8Array([0xff,0xd8,0xff])),'data:image/jpeg;base64,/9j/');
    assert.equal(M.cxDataUrlBytes(jpeg(102399)),102399);assert.equal(M.cxDataUrlType(jpeg(10)),'image/jpeg');assert.equal(M.cxDataUrlType('data:image/gif;base64,R0lG'),undefined);
  });

  test('[B1 CASH_BANK] Cashier / Manager only, slip number + photo under 100 KB, nothing below zero; expected drops when recorded',()=>{
    const s=seed();assert.equal(drawer(s),1072500);
    for(const u of ['reception','owner','accountant','housekeeper'])assert.throws(()=>bank(s,u,100000),code('FORBIDDEN'),u);
    assert.throws(()=>bank(s,'cashier',1072501),e=>e.code==='INVALID_TRANSITION'&&/below zero/.test(e.message)); // ₹10,725.01 out of ₹10,725
    assert.throws(()=>bank(s,'cashier',100000,{photo:jpeg(102400)}),e=>e.code==='VALIDATION'&&/100 KB/.test(e.message));
    assert.throws(()=>bank(s,'cashier',100000,{photo:'data:image/gif;base64,R0lGODlh'}),code('VALIDATION'));
    assert.throws(()=>bank(s,'cashier',100000,{photo:''}),code('VALIDATION'));
    assert.throws(()=>bank(s,'cashier',100000,{slip:''}),code('VALIDATION'));
    assert.throws(()=>bank(s,'cashier',100000,{slip:'DS<1>'}),code('VALIDATION'));
    const a=bank(s,'cashier',600000,{photo:jpeg(102399)}),r=last(a);
    assert.deepEqual([r.kind,r.status,r.amount,r.actor,r.fromTill],['banking','Pending',600000,'cashier','DESK']);
    assert.equal(drawer(a),472500); // ₹10,725 − ₹6,000 = ₹4,725
    assert.equal(M.shiftExpected(a).transfers,-600000);assert.equal(M.shiftExpected(a).consistent,true);
  });

  test('[B1 CASH_BANK_DECIDE] A different Manager or Owner confirms; a rejection needs a reason and puts the cash back; decided once',()=>{
    let s=bank(seed(),'cashier',600000);const big=last(s).id;
    assert.throws(()=>execute(s,'cashier',{type:'CASH_BANK_DECIDE',id:big,confirm:true}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'reception',{type:'CASH_BANK_DECIDE',id:big,confirm:true}),code('FORBIDDEN'));
    s=execute(s,'manager',{type:'CASH_BANK_DECIDE',id:big,confirm:true});
    assert.deepEqual([last(s).status,last(s).decidedBy],['Confirmed','manager']);assert.equal(drawer(s),472500); // confirming moves no cash
    assert.throws(()=>execute(s,'owner',{type:'CASH_BANK_DECIDE',id:big,confirm:false,reason:'late'}),code('INVALID_TRANSITION'));
    s=bank(s,'manager',100000,{slip:'DS-1002'});const small=last(s).id;assert.equal(drawer(s),372500); // ₹4,725 − ₹1,000
    assert.throws(()=>execute(s,'manager',{type:'CASH_BANK_DECIDE',id:small,confirm:true}),e=>e.code==='FORBIDDEN'&&/maker-checker/.test(e.message));
    assert.throws(()=>execute(s,'owner',{type:'CASH_BANK_DECIDE',id:small,confirm:false}),code('VALIDATION'));
    s=execute(s,'owner',{type:'CASH_BANK_DECIDE',id:small,confirm:false,reason:'Slip shows ₹100'});
    assert.equal(last(s).status,'Rejected');assert.equal(drawer(s),472500); // ₹3,725 + ₹1,000 back
    assert.deepEqual(M.shiftMovements(s).filter(m=>m.kind==='banking').map(m=>m.amount),[-600000,-100000,100000]);
  });

  test('[B1 DEC-065] Deciding a deposit dated on a locked cash-book day is DAY_CLOSED until the Owner reopens it; a pending deposit does not block the close',()=>{
    let s=ready();s=bank(s,'cashier',100000);const id=last(s).id;
    s=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)});assert.equal(s.shiftClose.counted,972500); // ₹10,725 − ₹1,000
    s=closeDay(s);const d=s.date;assert.ok(s.closedDates.includes(d),'closed with the deposit still pending');
    s=execute(s,'owner',{type:'NEXT_DAY'});s=execute(s,'accountant',{type:'CB_LOCK',date:d});
    assert.throws(()=>execute(s,'manager',{type:'CASH_BANK_DECIDE',id,confirm:true}),e=>e.code==='DAY_CLOSED'&&e.message.includes(d));
    s=execute(s,'owner',{type:'CB_REOPEN',date:d,reason:'Confirm the deposit of '+d});
    s=execute(s,'manager',{type:'CASH_BANK_DECIDE',id,confirm:true});assert.equal(M.cxSlice(s).records.find(r=>r.id===id).status,'Confirmed');
  });

  test('[B1 TILL_TRANSFER] Giver drops now, the receiver accepts on their own shift (or sends it back); a pending transfer blocks DAY_CLOSE',()=>{
    let s=seed();
    s=execute(s,'cashier',{type:'TILL_TRANSFER',to:'BACK',amount:200000,reason:'Change for the back office'});const t1=last(s).id;
    assert.equal(drawer(s),872500); // ₹10,725 − ₹2,000
    const back=x=>M.cxSlice(x).tills.find(t=>t.code==='BACK').expected;assert.equal(back(s),1000000,'the receiver waits');
    assert.throws(()=>execute(s,'cashier',{type:'TILL_TRANSFER_DECIDE',id:t1,accept:true}),code('FORBIDDEN'));   // the giver
    assert.throws(()=>execute(s,'reception',{type:'TILL_TRANSFER_DECIDE',id:t1,accept:true}),code('FORBIDDEN')); // not the receiving shift's owner
    assert.throws(()=>execute(s,'housekeeper',{type:'TILL_TRANSFER_DECIDE',id:t1,accept:true}),code('FORBIDDEN'));
    s=execute(s,'manager',{type:'TILL_TRANSFER_DECIDE',id:t1,accept:true});
    assert.equal(last(s).status,'Accepted');assert.equal(back(s),1200000);assert.equal(drawer(s),872500);
    s=execute(s,'cashier',{type:'TILL_TRANSFER',to:'BACK',amount:50000});const t2=last(s).id;assert.equal(drawer(s),822500);
    assert.throws(()=>execute(s,'manager',{type:'TILL_TRANSFER_DECIDE',id:t2,accept:false}),code('VALIDATION'));
    s=execute(s,'manager',{type:'TILL_TRANSFER_DECIDE',id:t2,accept:false,reason:'Not needed'});assert.equal(drawer(s),872500); // returned
    // The other way: the Manager gives ₹300 from the back office; the desk shift's holder accepts it into the drawer.
    s=execute(s,'manager',{type:'TILL_TRANSFER',from:'BACK',to:'DESK',amount:30000});const t3=last(s).id;assert.equal(back(s),1170000);
    s=execute(s,'cashier',{type:'TILL_TRANSFER_DECIDE',id:t3,accept:true});assert.equal(drawer(s),902500); // ₹8,725 + ₹300
    assert.throws(()=>execute(s,'cashier',{type:'TILL_TRANSFER',to:'DESK',amount:1000}),code('VALIDATION'));
    assert.throws(()=>execute(s,'cashier',{type:'TILL_TRANSFER',to:'BACK',amount:902501}),code('INVALID_TRANSITION'));
    // preflight: a pending transfer is listed (and blocks), a pending deposit is not
    let p=bank(s,'cashier',1000);p=execute(p,'cashier',{type:'TILL_TRANSFER',to:'BACK',amount:1000});
    const items=M.closingPreflight(p).find(x=>x.id==='approvals').items.map(i=>i.label);
    assert.equal(items.filter(l=>l.startsWith('Till transfer awaiting the receiver')).length,1);assert.equal(items.filter(l=>/deposit/i.test(l)).length,0);
    let c=ready();c=execute(c,'cashier',{type:'TILL_TRANSFER',to:'BACK',amount:10000});const tc=last(c).id;
    c=execute(c,'cashier',{type:'SHIFT_SUBMIT',counted:drawer(c)});c=tillClear(M,c);
    assert.throws(()=>execute(c,'owner',{type:'DAY_CLOSE'}),e=>e.code==='INVALID_TRANSITION'&&/till transfer/.test(e.message));
    c=execute(c,'manager',{type:'TILL_TRANSFER_DECIDE',id:tc,accept:true});c=execute(c,'owner',{type:'DAY_CLOSE'});assert.ok(c.closedDates.includes(c.date));
  });

  test('[B1 OWNER_DRAWING] Owner only, reason required, nothing below zero; the drawer drops',()=>{
    const s=seed();
    for(const u of ['manager','cashier','reception','accountant'])assert.throws(()=>execute(s,u,{type:'OWNER_DRAWING',amount:150000,reason:'Household'}),code('FORBIDDEN'),u);
    assert.throws(()=>execute(s,'owner',{type:'OWNER_DRAWING',amount:150000,reason:' '}),code('VALIDATION'));
    assert.throws(()=>execute(s,'owner',{type:'OWNER_DRAWING',amount:1072501,reason:'Household'}),code('INVALID_TRANSITION'));
    const a=execute(s,'owner',{type:'OWNER_DRAWING',amount:150000,reason:'Household (fictional)'});
    assert.deepEqual([last(a).kind,last(a).status],['drawing','Posted']);assert.equal(drawer(a),922500); // ₹10,725 − ₹1,500
  });

  test('[B1] Cash book and accounting: every movement is a cash-book line, the day reconciles, vouchers balance on 1030 / 3000 / 1000 (no voucher for a transfer)',()=>{
    let s=seed();
    s=bank(s,'cashier',600000);const d1=last(s).id;s=execute(s,'manager',{type:'CASH_BANK_DECIDE',id:d1,confirm:true});
    s=bank(s,'cashier',100000,{slip:'DS-1002'});const d2=last(s).id;s=execute(s,'owner',{type:'CASH_BANK_DECIDE',id:d2,confirm:false,reason:'Wrong slip'});
    s=execute(s,'owner',{type:'OWNER_DRAWING',amount:150000,reason:'Household'});const dr=last(s).id;
    s=execute(s,'cashier',{type:'TILL_TRANSFER',to:'BACK',amount:200000});s=execute(s,'manager',{type:'TILL_TRANSFER_DECIDE',id:last(s).id,accept:true});
    // ₹10,725 − 6,000 − 1,000 + 1,000 − 1,500 − 2,000 = ₹1,225
    assert.equal(drawer(s),122500);const ex=M.shiftExpected(s);assert.equal(ex.transfers,-950000);assert.equal(ex.expected,122500);assert.ok(ex.consistent);
    const day=M.cbDay(s,s.date);
    assert.equal(day.closing,122500);assert.equal(day.deposited,600000);assert.ok(day.reconciles);
    const outs=day.entries.filter(e=>['banking','owner-drawing','transfer-out'].includes(e.kind)).map(e=>[e.kind,e.in,e.out]);
    assert.deepEqual(outs,[['banking',0,600000],['banking',0,100000],['banking',100000,0],['owner-drawing',0,150000],['transfer-out',0,200000]]);
    assert.equal(M.cbDay(seed(),seed().date).deposited,undefined,'no deposits: the field is absent (golden cash-book data unchanged)');
    const vs=M.accLiveVouchers(s,s.date),by=id=>vs.find(v=>v.source.id===id);
    const ent=v=>v.entries.map(e=>[e.account,e.debit,e.credit]);
    assert.deepEqual(ent(by('banking:'+d1)),[['1030/BANK-1',600000,0],['1000/TILL',0,600000]]);
    assert.deepEqual(ent(by('banking:'+d2+'/rejected')),[['1000/TILL',100000,0],['1030/BANK-1',0,100000]]);
    assert.deepEqual(ent(by('drawing:'+dr)),[['3000',150000,0],['1000/TILL',0,150000]]);assert.equal(by('drawing:'+dr).type,'Payment');
    assert.ok(vs.every(M.accBalanced));assert.ok(!vs.some(v=>/transfer/.test(v.source.id)));
    assert.equal(M.accAccount('3000').name,"Owner's drawings");assert.equal(M.ACC_DEFAULT_MAP['closing:bank-deposit'],'1030');
  });

  test('[B1 DEC-078] NEXT_DAY opens no shift; the next person opens their own with a handover count; Reception takes cash on her own shift, the Cashier is refused on it',()=>{
    let s=ready();s=execute(s,'cashier',{type:'SHIFT_SUBMIT',counted:drawer(s)});s=closeDay(s);s=execute(s,'owner',{type:'NEXT_DAY'});
    assert.deepEqual([s.date,s.shiftOpen,s.openingCash],['2026-10-25',false,1072500]);
    const inhouse=s.bookings.find(b=>b.status==='In house').id;
    const pay=(x,u,amount)=>execute(x,u,{type:'PAYMENT',id:inhouse,amount,method:'Cash',kind:'payment'});
    assert.throws(()=>pay(s,'reception',1000),e=>e.code==='INVALID_TRANSITION'&&/Open a cashier shift first/.test(e.message));
    assert.throws(()=>execute(s,'reception',{type:'SHIFT_OPEN'}),e=>e.code==='VALIDATION'&&/handover count/.test(e.message)); // the Cashier counted
    assert.throws(()=>execute(s,'reception',{type:'SHIFT_OPEN',handover:1062500}),e=>e.code==='VALIDATION'&&/reason/.test(e.message));
    const mine=execute(s,'reception',{type:'SHIFT_OPEN',handover:1062500,reason:'One ₹100 note short at handover'});
    assert.equal(mine.shiftOpen,true);assert.equal(mine.openingCash,1072500,'DEC-035: the float stays the counted cash');
    const {at,...opener}=M.cxSlice(mine).opener;assert.ok(at);
    assert.deepEqual(opener,{by:'reception',date:'2026-10-25',handover:1062500,difference:-10000,reason:'One ₹100 note short at handover'}); // ₹10,625 − ₹10,725
    let r=pay(mine,'reception',120000);assert.equal(drawer(r),1192500); // ₹10,725 + ₹1,200
    assert.throws(()=>pay(r,'cashier',1000),e=>e.code==='FORBIDDEN'&&/own open cashier shift/.test(e.message));
    r=pay(r,'manager',10000);assert.equal(drawer(r),1202500);
    assert.throws(()=>bank(r,'cashier',1000),code('FORBIDDEN'),'banking also only from your own shift');
    // the person who counted re-opens without a handover count
    const own=execute(s,'cashier',{type:'SHIFT_OPEN'});assert.equal(own.shiftOpen,true);assert.equal(M.cxSlice(own).opener.by,'cashier');
    assert.throws(()=>execute(mine,'cashier',{type:'SHIFT_OPEN',handover:1}),code('INVALID_TRANSITION'),'one open shift on the till');
  });
}
