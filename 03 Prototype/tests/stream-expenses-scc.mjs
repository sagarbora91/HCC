// DEC-120 · SCC expense features in HCC's expense module (owner, 27 Oct 2026), with HCC's approval rules kept. SCC's own functions, copied
// below from E:\Projects\HCC\Migration data on 25 oct\HCC-Migration-2026-10-25\reference-inputs\SCC-modules\expense\index.html (scratchpad
// scc\expense.js; line numbers are expense.js lines, rupees as floats), are the oracle: HCC (integer paise) must give the same figures.
// Names prefixed [EXP-SCC].
import {build} from 'esbuild';
import {tillClear} from './_till.mjs';
await build({entryPoints:['src/app/slipPhoto.ts'],bundle:true,platform:'node',format:'esm',outfile:'tests/.generated/slipphoto-bill.mjs',logLevel:'error'});
const P=await import('./.generated/slipphoto-bill.mjs');

// ── SCC, verbatim apart from taking the ledger / budgets / today as arguments instead of localStorage and the real clock ──
const num=v=>{var n=parseFloat(v);return isNaN(n)?0:n;};
/* prefillGst (445-448): the GST part of a GST-inclusive bill */
function sccPrefill(a,r){if(a>0&&r>0)return Math.round(a*r/(100+r)*100)/100;return 0;}
/* renderBudget row (1282, 1287) */
function sccBudgetRow(bg,ac){bg=num(bg);var pct=bg?Math.round(ac/bg*100):0;return {pct,status:!bg?'no budget':pct>100?'over':pct>=85?'near limit':'ok'};}
/* budgetAlertFor (1297-1309) with today / budgets / ledger injected */
function sccBudgetAlertFor(cat,date,today,budgets,ledger){
  var m=(date||'').slice(0,7);
  if(m!==today.slice(0,7))return null;
  var bg=num((budgets[m]||{})[cat]);
  if(!(bg>0))return null;
  var ac=0;ledger.forEach(function(e){if(!e.void&&e.type==='expense'&&e.category===cat&&e.date.slice(0,7)===m)ac+=e.amount;});
  var pct=Math.round(ac/bg*100);
  if(pct<90)return null;
  var mon=['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'][+m.slice(5,7)-1]||m;
  return {msg:cat+' now '+pct+'% of '+mon+' budget',style:pct>=100?'err':'warn'};
}
/* prevYM (1321) + copyBudgets head selection (1329-1334) */
function p2(n){return (n<10?'0':'')+n;}
function prevYM(m){var y=+m.slice(0,4),mo=+m.slice(5,7)-1;if(mo<1){y--;mo=12;}return y+'-'+p2(mo);}
function sccCopyHeads(m,b,cats){var pm=prevYM(m),src=b[pm]||{},bm=b[m]||{};
  if(!cats.some(function(c){return num(src[c])>0;}))return 'No budgets found for '+pm;
  var heads=cats.filter(function(c){return num(src[c])>0&&!(num(bm[c])>0);});
  if(!heads.length)return 'Every head already has a budget for '+m;return heads;}
/* recDaysInMonth (705) + recurringDue (718-723) with today injected; posted = recPostedMap(mm) */
function recDaysInMonth(mm){return new Date(+mm.slice(0,4),+mm.slice(5,7),0).getDate();}
function sccRecurringDue(tpls,today,posted){var mm=today.slice(0,7),dNow=+today.slice(8,10),dim=recDaysInMonth(mm);
  return tpls.filter(function(t){return t.active&&!posted['rec:'+t.id+':'+mm]&&dNow>=Math.min(t.dueDay,dim);});}
/* normRecTpl due day (682) */
function sccDueDay(v){var d=Math.floor(num(v));if(!(d>=1&&d<=31))d=1;return d;}
/* renderPetty status (1213) */
function sccPettyStatus(float,spent){return (float-spent)<0.15*float&&float?'Replenish soon':'Healthy';}
/* Udhaar (1556-1560, 1587) with today injected */
function udPaid(r){return (r.settlements||[]).reduce(function(s,x){return s+num(x.amount);},0);}
function udOut(r){var o=num(r.amount)-udPaid(r);return o>0?o:0;}
function udDaysSince(d,today){var a=new Date(d+'T00:00:00'),b=new Date(today+'T00:00:00');return Math.round((b-a)/86400000);}
function udBucket(r,today){var d=udDaysSince(r.date,today);return d<=30?'0–30':(d<=60?'31–60':'60+');}
function udOverdue(r,today){return r.dueDate&&today>r.dueDate;}
function sccPay(r,a){var out=udOut(r);if(a>out+0.5){a=out;}return a;}
/* addReceivable mobile (1566, 1569) */
function sccMobile(v){var mob=(v||'').replace(/\D/g,'');return mob.length>=10?mob.slice(-10):'';}
/* readPhoto canvas size (463-464); a canvas keeps the whole pixels of a fractional width / height */
function sccPhotoSize(w,h){var sc=Math.min(1,1000/w);return [Math.trunc(w*sc),Math.trunc(h*sc)];}

const code=c=>e=>e?.code===c;
const says=(c,re)=>e=>e?.code===c&&re.test(e.message);

export function register({test,M,assert}){
  const {execute,seed,drawer}=M;
  const D=seed().date;
  /** A posted expense of `category` for `amount` paise (submitted by Reception, approved by the Owner). */
  const posted=(s,amount,category='Supplies',extra={})=>{s=execute(s,'reception',{type:'EXPENSE',description:'Posted '+category+' '+amount+' '+s.sequence,vendor:'Test Vendor',category,amount,method:'UPI',...extra});
    return execute(s,'owner',{type:'EXPENSE_APPROVE',id:s.expenses.at(-1).id,approve:true});};
  /** The state with the Expense vendors list stored (the Owner changed it once), so new names are added as rows. */
  const storedVendors=()=>{const s=seed();const row=M.mnyVendors(s)[0];return execute(s,'owner',{type:'MST_MASTER_SAVE',master:'expenseVendors',row:{...row,contact:'98'}});};

  // ── 1. SCC's formulas, oracle-tested ──
  test('[EXP-SCC] GST prefill = SCC prefillGst on every grid point (₹0.01 … ₹10 lakh × 0.25 % … 100 %); 1180 @ 18 % → 180.00',()=>{
    assert.equal(M.expGstPrefill(118000,1800),18000);
    let n=0,diff=[];
    for(let a=1;a<=200000;a+=a<2000?1:a<20000?37:997)for(const bps of [25,100,250,300,500,1200,1800,2800,4000,10000]){
      const want=Math.round(sccPrefill(a/100,bps/100)*100),got=M.expGstPrefill(a,bps);n++;
      if(want!==got)diff.push([a,bps,want,got]);}
    for(const a of [99999999,100000000,123456789])for(const bps of [500,1800]){n++;const want=Math.round(sccPrefill(a/100,bps/100)*100),got=M.expGstPrefill(a,bps);if(want!==got)diff.push([a,bps,want,got]);}
    assert.ok(n>20000,'grid '+n);assert.deepEqual(diff,[],`${diff.length} of ${n} differ`);
    assert.equal(M.expGstPrefill(0,1800),0);assert.equal(M.expGstPrefill(118000,0),0);});
  test('[EXP-SCC] budget tab status = SCC renderBudget: round(actual ÷ budget × 100), "over" above 100, "near limit" from 85, no budget',()=>{
    const map={'no budget':'none','over':'over','near limit':'near','ok':'ok'};let n=0;
    for(const bg of [0,1,99,100,1000,4999,10000,333333])for(const ac of [0,1,50,84,84.4,84.5,85,89.5,90,99.4,99.5,100,100.4,100.5,101,150,10000].map(p=>Math.round(bg*p/100))){
      const w=sccBudgetRow(bg,ac),g=M.expBudgetRow('X',bg,ac);n++;
      assert.deepEqual([g.pct,g.status],[w.pct,map[w.status]],`budget ${bg} actual ${ac}`);}
    assert.ok(n>100);
    // SCC's 100 % edge (owner question): exactly 100 % is "near limit" on the tab but an error alert.
    assert.equal(M.expBudgetRow('X',10000,10000).status,'near');});
  test('[EXP-SCC] budget alert = SCC budgetAlertFor: business month only, ≥ 90 warning, ≥ 100 error, "<head> now <pct>% of <Mon> budget"',()=>{
    let s=seed();s=execute(s,'owner',{type:'EXP_BUDGET_SET',category:'Supplies',amount:100000});
    const cases=[];
    for(const amt of [10000,79999,9999,1,500,9500,500]){s=posted(s,amt);
      const ledger=s.expenses.filter(M.expenseCounts).map(e=>({type:'expense',void:false,category:e.category,date:e.date,amount:e.amount/100}));
      const w=sccBudgetAlertFor('Supplies',D,D,{[D.slice(0,7)]:{Supplies:1000}},ledger),g=M.expBudgetAlert(s,'Supplies',D);
      assert.deepEqual(g?{msg:g.msg,style:g.style}:null,w,'after '+amt);cases.push(g?.style??'none');}
    assert.deepEqual(cases,['none','warn','err','err','err','err','err']);
    assert.equal(M.expBudgetAlert(s,'Supplies','2026-09-30'),null,'only the business month');
    assert.equal(M.expBudgetAlert(s,'Laundry',D),null,'no budget → silent');
    assert.match(M.expBudgetAlert(s,'Supplies',D).msg,/^Supplies now \d+% of Oct budget$/);
    const proj=M.expBudgetAlert(execute(seed(),'owner',{type:'EXP_BUDGET_SET',category:'Supplies',amount:100000}),'Supplies',D,95000);
    assert.deepEqual(proj,{msg:'Supplies would be 95% of Oct budget once approved',style:'warn',pct:95});});
  test('[EXP-SCC] copy last month = SCC copyBudgets: only heads with a budget last month and none this month; SCC messages',()=>{
    const m='2026-10',cats=['Supplies','Laundry','Maintenance','Utilities','Petty cash'];
    for(const b of [{},{'2026-09':{Supplies:0}},{'2026-09':{Supplies:500,Laundry:200},'2026-10':{Supplies:100}},{'2026-09':{Supplies:500},'2026-10':{Supplies:100}},{'2026-09':{Laundry:1,Utilities:3},'2026-10':{Laundry:0}}]){
      const w=sccCopyHeads(m,b,cats),s={...seed(),expensesX:{budgets:b}},g=M.expBudgetCopyPlan(s,m);
      assert.deepEqual(g.message??g.heads,w,JSON.stringify(b));}
    assert.equal(M.expPrevMonth('2026-01'),'2025-12');});
  test('[EXP-SCC] recurring due = SCC recurringDue: due day capped to the month (rent on 31 → 28 Feb / 29 Feb 2028), posted / inactive skipped',()=>{
    const tpls=[{id:'A',name:'Rent',dueDay:31,active:true},{id:'B',name:'Power',dueDay:5,active:true},{id:'C',name:'Old',dueDay:1,active:false},{id:'D',name:'Wifi',dueDay:15,active:true}];
    for(const today of ['2027-02-27','2027-02-28','2028-02-28','2028-02-29','2026-10-04','2026-10-05','2026-10-30','2026-10-31','2026-04-30']){
      const posted={['rec:D:'+today.slice(0,7)]:1};
      const s={...seed(),date:today,expensesX:{recurring:tpls.map(t=>({...t,amount:100,category:'Supplies',method:'Cash',vendor:'',createdAt:'',createdBy:'owner'}))},
        expenses:[...seed().expenses,{id:'X1',vendor:'',category:'Supplies',amount:100,method:'Cash',date:today,status:'Draft',actor:'owner',sourceRef:'rec:D:'+today.slice(0,7)}]};
      assert.deepEqual(M.expRecurringDue(s).map(t=>t.id),sccRecurringDue(tpls,today,posted).map(t=>t.id),today);}
    for(const v of [0,1,15.7,31,31.2,32,-3,'x','7',null])assert.equal(M.expDueDay(v),sccDueDay(v),String(v));});
  test('[EXP-SCC] petty status = SCC renderPetty: "Replenish soon" below 15 % of the float (float > 0)',()=>{
    for(const float of [0,1000,500000,333333])for(const spent of [0,1,Math.floor(float*0.85),Math.ceil(float*0.85),float*0.85,float,float+1]){
      const f=float*100,sp=Math.round(spent*100);
      const s={...seed(),expensesX:{petty:f?[{id:'P1',amount:f,date:D,by:'owner',at:'',status:'Approved'}]:[]},
        expenses:sp?[{id:'E1',vendor:'',category:'Petty cash',amount:sp,method:'Cash',date:D,status:'Posted',actor:'reception',source:'petty'}]:[]};
      assert.equal(M.expPetty(s).status,sccPettyStatus(f/100,sp/100),`float ${float} spent ${spent}`);}});
  test('[EXP-SCC] Udhaar = SCC: outstanding floored at 0, buckets 0–30 / 31–60 / 60+, overdue, ₹0.50 overpay tolerance, mobile last 10 digits',()=>{
    const today='2026-10-24';
    for(const days of [0,1,30,31,59,60,61,200]){const date=M.addDays(today,-days);const r={amount:100000,settlements:[{amount:20000}],date,dueDate:M.addDays(date,30)};
      assert.equal(M.expUdBucket(r,today),udBucket(r,today),'days '+days);assert.equal(M.expUdOverdue(r,today),!!udOverdue(r,today),'overdue '+days);}
    // Amounts whose rupee values are exact binary fractions, so SCC's float subtraction is exact too (HCC is exact paise everywhere).
    for(const [amount,paid,pay] of [[100000,0,100000],[100000,0,100050],[100000,0,100075],[100000,99950,100],[100000,99950,125],[100000,99975,75],[100000,99975,100],[100000,100000,10],[100000,120000,10]]){
      const r={amount,settlements:paid?[{amount:paid}]:[]},rs={amount:amount/100,settlements:paid?[{amount:paid/100}]:[]};
      assert.equal(M.expUdOut(r),Math.round(udOut(rs)*100));
      if(M.expUdOut(r)>0)assert.equal(M.expUdPayment(r,pay).amount,Math.round(sccPay(rs,pay/100)*100),`${amount}/${paid}/${pay}`);}
    for(const v of ['98765 43210','+91 98765-43210','12345','0098765432109',''])assert.equal(M.expUdMobile(v),sccMobile(v),v);});
  test('[EXP-SCC] bill photo = SCC readPhoto: 4 MB limit, at most 1000 px wide (never enlarged), JPEG quality 0.7',()=>{
    for(const [w,h] of [[4000,3000],[3000,4000],[1000,700],[999,1500],[1001,2],[1333,777],[640,480],[1,1],[2500,2501],[4032,3024],[3024,4032]])
      assert.deepEqual(Object.values(P.billFit(w,h)),sccPhotoSize(w,h),`${w}x${h}`);
    assert.deepEqual(P.billFit(1001,1),{width:1000,height:1},'SCC would draw a 0 px high canvas (an empty photo); HCC keeps 1 px');
    assert.equal(P.BILL_QUALITY,0.7);assert.equal(P.BILL_MAX_INPUT_BYTES,4194304);
    assert.equal(P.billPickRefusal({size:4194304,type:'image/jpeg'}),'');assert.equal(P.billPickRefusal({size:4194305,type:'image/jpeg'}),'Photo too large (max 4MB)');});

  // ── 2. The expense entry: description, vendor, date, GST, photo, duplicates, amount (prototype handlers) ──
  test('[EXP-SCC] description is required to submit (a draft may wait); vendor optional; a new vendor joins the list; a retired vendor is refused',()=>{
    let s=storedVendors();
    assert.throws(()=>execute(s,'reception',{type:'EXPENSE',vendor:'X',category:'Supplies',amount:100,method:'UPI'}),says('VALIDATION',/Description is required/));
    s=execute(s,'reception',{type:'EXPENSE',description:'Tea for staff',category:'Supplies',amount:100,method:'UPI'});
    assert.deepEqual([s.expenses.at(-1).vendor,s.expenses.at(-1).description,s.expenses.at(-1).status],['','Tea for staff','Pending']);
    s=execute(s,'reception',{type:'EXPENSE',description:'Bulbs',vendor:'Brand New Electricals',category:'Maintenance',amount:900,method:'UPI'});
    const row=M.mnyVendors(s).find(v=>v.name==='Brand New Electricals');assert.ok(row&&row.active,'auto-added');
    assert.ok(s.mastersX.history.some(h=>h.master==='expenseVendors'&&h.rowId===row.id),'history entry');
    const n=M.mnyVendors(s).length;s=execute(s,'reception',{type:'EXPENSE',description:'Bulbs again',vendor:'brand new electricals',category:'Maintenance',amount:901,method:'UPI'});
    assert.equal(M.mnyVendors(s).length,n,'same name in other case is not added twice');assert.equal(s.expenses.at(-1).vendor,'Brand New Electricals');
    const r=execute(s,'owner',{type:'MST_MASTER_RETIRE',master:'expenseVendors',id:row.id});
    assert.throws(()=>execute(r,'reception',{type:'EXPENSE',description:'x',vendor:'Brand New Electricals',category:'Supplies',amount:100,method:'UPI'}),e=>M.mstReasonOf(e)==='MASTER_RETIRED');
    let d=execute(seed(),'reception',{type:'EXPENSE',vendor:'Draft Shop',category:'Supplies',amount:100,method:'UPI',draft:true});
    assert.throws(()=>execute(d,'reception',{type:'EXPENSE_SUBMIT',id:d.expenses.at(-1).id}),/Description is required/);});
  test('[EXP-SCC] chosen date: never after the business date, never a closed day (Owner-reopened day allowed, not for drawer cash), never a locked cash-book day',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'reception',{type:'EXPENSE',description:'x',category:'Supplies',amount:100,method:'UPI',date:M.addDays(D,1)}),says('VALIDATION',/after the business date/));
    assert.throws(()=>execute(s,'reception',{type:'EXPENSE',description:'x',category:'Supplies',amount:100,method:'UPI',date:'2026-10-23'}),says('DAY_CLOSED',/Owner must reopen it first/));
    assert.throws(()=>execute(s,'reception',{type:'EXPENSE',description:'x',category:'Supplies',amount:100,method:'UPI',date:'2026-10-2'}),says('VALIDATION',/valid expense date/));
    // Close 24 Oct, open 25 Oct, the Owner reopens 24 Oct: a UPI bill of the 24th may carry that date; drawer cash may not.
    let x=seed();for(const b of x.bookings.filter(b=>b.status==='Confirmed')){x=execute(x,'owner',{type:'REGISTER',id:b.id});x=execute(x,'owner',{type:'CHECKIN',id:b.id});}
    x=execute(x,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});x=execute(x,'owner',{type:'NIGHT_POST'});
    x=execute(x,'cashier',{type:'SHIFT_SUBMIT',counted:drawer(x)});x=tillClear(M,x);x=execute(x,'manager',{type:'DAY_CLOSE'});x=execute(x,'manager',{type:'NEXT_DAY'});
    assert.throws(()=>execute(x,'reception',{type:'EXPENSE',description:'Late bill',category:'Supplies',amount:500,method:'UPI',date:D}),code('DAY_CLOSED'));
    const x0=x;x=execute(x,'owner',{type:'DAY_REOPEN',date:D,reason:'Missed supplier bill'});
    const y=execute(x,'reception',{type:'EXPENSE',description:'Late bill',category:'Supplies',amount:500,method:'UPI',date:D});
    assert.equal(y.expenses.at(-1).date,D);
    assert.throws(()=>execute(x,'reception',{type:'EXPENSE',description:'Late cash',category:'Supplies',amount:500,method:'Cash',date:D}),says('VALIDATION',/today’s drawer/));
    // Approval keeps the chosen (reopened) date while it is open.
    const z=execute(y,'manager',{type:'EXPENSE_APPROVE',id:y.expenses.at(-1).id,approve:true});
    assert.equal(z.expenses.find(e=>e.id===y.expenses.at(-1).id).date,D);
    // A locked cash-book day stays locked when the Owner reopens the business day (DEC-065 / DEC-077).
    const locked=execute(execute(x0,'accountant',{type:'CB_LOCK',date:D}),'owner',{type:'DAY_REOPEN',date:D,reason:'Missed supplier bill'});
    assert.throws(()=>execute(locked,'reception',{type:'EXPENSE',description:'x',category:'Supplies',amount:100,method:'UPI',date:D}),code('DAY_CLOSED'));});
  test('[EXP-SCC] GST on the bill: kept only when > 0; GST more than the amount asks "Save anyway?"; GST paid on purchases counts approved bills only',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'reception',{type:'EXPENSE',description:'Paint',category:'Maintenance',amount:10000,method:'UPI',gstRateBps:1800,gstPaise:20000}),says('VALIDATION',/GST amount \(₹200\) is more than the entry amount \(₹100\)\. Save anyway\?/));
    s=execute(s,'reception',{type:'EXPENSE',description:'Paint',category:'Maintenance',amount:10000,method:'UPI',gstRateBps:1800,gstPaise:20000,confirmGst:true});
    s=execute(s,'reception',{type:'EXPENSE',description:'Cement',category:'Maintenance',amount:118000,method:'UPI',gstRateBps:1800,gstPaise:M.expGstPrefill(118000,1800)});
    s=execute(s,'reception',{type:'EXPENSE',description:'No GST',category:'Maintenance',amount:5000,method:'UPI',gstRateBps:0,gstPaise:0});
    assert.deepEqual([s.expenses.at(-1).gstRateBps,s.expenses.at(-1).gstPaise],[undefined,undefined]);
    assert.equal(M.expGstPaid(s,D.slice(0,7)),0,'pending bills are not counted');
    const ids=s.expenses.slice(-3).map(e=>e.id);for(const id of ids)s=execute(s,'owner',{type:'EXPENSE_APPROVE',id,approve:true});
    assert.equal(M.expGstPaid(s,D.slice(0,7)),38000);
    s=execute(s,'manager',{type:'EXPENSE_REVERSE',id:ids[1],reason:'Returned'});
    assert.equal(M.expGstPaid(s,D.slice(0,7)),20000,'a reversal carries −GST');
    assert.throws(()=>execute(seed(),'reception',{type:'EXPENSE',description:'x',category:'Supplies',amount:100,method:'UPI',gstRateBps:10001}),/GST rate/);});
  test('[EXP-SCC] duplicate = SCC key (same date + amount + description, any case): "Save anyway?" then saved; void / rejected do not count',()=>{
    let s=execute(seed(),'reception',{type:'EXPENSE',description:'Water cans',vendor:'Aqua',category:'Supplies',amount:30000,method:'UPI'});
    const msg=/A similar entry already exists for 2026-10-24 \(₹300 — water CANS\)\. Save anyway\?/;
    assert.throws(()=>execute(s,'cashier',{type:'EXPENSE',description:'water CANS',vendor:'Other vendor',category:'Supplies',amount:30000,method:'Cash'}),says('VALIDATION',msg));
    assert.equal(execute(s,'cashier',{type:'EXPENSE',description:'water CANS',category:'Supplies',amount:30000,method:'Cash',confirmDuplicate:true}).expenses.length,s.expenses.length+1);
    assert.equal(execute(s,'cashier',{type:'EXPENSE',description:'Water cans',category:'Supplies',amount:30001,method:'Cash'}).expenses.length,s.expenses.length+1,'other amount');
    const v=execute(s,'owner',{type:'EXP_VOID',id:s.expenses.at(-1).id,reason:'Duplicate entry'});
    assert.equal(M.expenseDuplicates(v,'water cans',30000,D).length,0,'void does not count');});
  test('[EXP-SCC] amount: up to ₹10 crore (server maximum; the prototype stopped at ₹10 lakh); bill photo over 4 MB refused',()=>{
    const s=execute(seed(),'reception',{type:'EXPENSE',description:'Renovation',category:'Maintenance',amount:10000000000,method:'Vendor credit'});
    assert.equal(s.expenses.at(-1).amount,10000000000);
    assert.throws(()=>execute(seed(),'reception',{type:'EXPENSE',description:'x',category:'Maintenance',amount:10000000001,method:'Vendor credit'}),/₹10,00,00,000/);
    const big='data:image/jpeg;base64,'+'A'.repeat(Math.ceil((4194304+1)/3)*4);
    assert.throws(()=>execute(seed(),'reception',{type:'EXPENSE',description:'x',category:'Supplies',amount:100,method:'UPI',billPhoto:big}),/Photo too large \(max 4MB\)/);
    const ok=execute(seed(),'reception',{type:'EXPENSE',description:'x',category:'Supplies',amount:100,method:'UPI',billPhoto:P.slipDataUrl(new Uint8Array(3000).fill(0xff))});
    assert.match(ok.expenses.at(-1).billPhoto,/^data:image\/jpeg;base64,/);
    assert.throws(()=>execute(seed(),'reception',{type:'EXPENSE',description:'x',category:'Supplies',amount:100,method:'UPI',billPhoto:'data:text/html;base64,AAAA'}),/JPEG or PNG/);});

  // ── 3. Edit and void through HCC's role and approval rules ──
  test('[EXP-SCC] edit: reason required, change log old → new, Draft / Pending only (approved = reversal + new entry), an editor never approves it',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'manager',{type:'EXP_EDIT',id:'EXP-102',amount:90000,category:'Supplies',method:'UPI',description:'Morning supplies'}),says('VALIDATION',/Reason required/));
    assert.throws(()=>execute(s,'cashier',{type:'EXP_EDIT',id:'EXP-102',amount:90000,category:'Supplies',method:'UPI',description:'x',reason:'r'}),code('FORBIDDEN'));
    s=execute(s,'manager',{type:'EXP_EDIT',id:'EXP-102',amount:90000,category:'Supplies',method:'UPI',description:'Morning supplies',gstRateBps:500,gstPaise:4286,reason:'Bill says ₹900'});
    const e=s.expenses.find(x=>x.id==='EXP-102');
    assert.deepEqual([e.amount,e.description,e.gstPaise,e.editors],[90000,'Morning supplies',4286,['manager']]);
    assert.deepEqual({...e.editLog.at(-1),at:'-'},{at:'-',by:'manager',old:{amount:85000,category:'Supplies',method:'UPI',description:'',gstPaise:0,gstRateBps:0},
      new:{amount:90000,category:'Supplies',method:'UPI',description:'Morning supplies',gstPaise:4286,gstRateBps:500},reason:'Bill says ₹900'});
    assert.throws(()=>execute(s,'manager',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true}),says('FORBIDDEN',/You changed this expense/));
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true});
    assert.throws(()=>execute(s,'owner',{type:'EXP_EDIT',id:'EXP-102',amount:1,category:'Supplies',method:'UPI',description:'x',reason:'r'}),says('INVALID_TRANSITION',/Approved entry is locked/));
    // own draft by its maker; somebody else's draft never
    let d=execute(seed(),'cashier',{type:'EXPENSE',vendor:'Shop',category:'Supplies',amount:100,method:'Cash',draft:true});const id=d.expenses.at(-1).id;
    assert.throws(()=>execute(d,'owner',{type:'EXP_EDIT',id,amount:200,category:'Supplies',method:'Cash',description:'x',reason:'r'}),says('FORBIDDEN',/own drafts/));
    d=execute(d,'cashier',{type:'EXP_EDIT',id,amount:200,category:'Supplies',method:'Cash',description:'Soap',reason:'Forgot the description'});
    d=execute(d,'cashier',{type:'EXPENSE_SUBMIT',id});assert.equal(d.expenses.at(-1).status,'Pending');});
  test('[EXP-SCC] void: one of SCC’s six reasons + note, kept for audit and out of every total; Owner / Manager / Accountant (never the Cashier), own draft by its maker',()=>{
    let s=seed();
    assert.deepEqual([...M.EXP_VOID_REASONS],['Cash shortage correction','Duplicate entry','Missed receipt','Wrong payment mode','Voucher correction','Other']);
    assert.throws(()=>execute(s,'manager',{type:'EXP_VOID',id:'EXP-102',reason:'Because'}),says('VALIDATION',/Choose a reason/));
    for(const u of ['cashier','reception'])assert.throws(()=>execute(s,u,{type:'EXP_VOID',id:'EXP-102',reason:'Duplicate entry'}),code('FORBIDDEN'),u);
    assert.throws(()=>execute(s,'manager',{type:'EXP_VOID',id:'EXP-101',reason:'Duplicate entry'}),says('INVALID_TRANSITION',/reverse it instead/));
    s=execute(s,'accountant',{type:'EXP_VOID',id:'EXP-102',reason:'Wrong payment mode',note:'Paid by the owner'});
    const e=s.expenses.find(x=>x.id==='EXP-102');assert.deepEqual([e.status,e.voidReason,e.voidedBy],['Void','Wrong payment mode — Paid by the owner','accountant']);
    assert.equal(M.closingPreflight?M.closingPreflight(s).find(i=>i.id==='expenses')?.items.length??0:0,0,'a void never holds up the day close');
    let d=execute(seed(),'cashier',{type:'EXPENSE',vendor:'Shop',category:'Supplies',amount:100,method:'Cash',draft:true});
    d=execute(d,'cashier',{type:'EXP_VOID',id:d.expenses.at(-1).id,reason:'Other'});assert.equal(d.expenses.at(-1).status,'Void');});

  // ── 4. Petty cash, budgets, recurring, Udhaar ──
  test('[EXP-SCC] petty cash: top-ups approved by a second person (Manager ≤ ₹5,000), disbursements are drawer-cash expenses counted once approved',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'cashier',{type:'EXP_PETTY_FLOAT',amount:0}),says('VALIDATION',/Enter amount/));
    s=execute(s,'cashier',{type:'EXP_PETTY_FLOAT',amount:1000000,note:'Monthly float'});const top=M.expSlice(s).petty.at(-1);
    assert.equal(M.expPetty(s).float,0,'not before approval');
    assert.throws(()=>execute(s,'cashier',{type:'EXP_PETTY_FLOAT_DECIDE',id:top.id,approve:true}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'manager',{type:'EXP_PETTY_FLOAT_DECIDE',id:top.id,approve:true}),says('FORBIDDEN',/Owner approval/));
    s=execute(s,'owner',{type:'EXP_PETTY_FLOAT_DECIDE',id:top.id,approve:true});assert.equal(M.expPetty(s).float,1000000);
    const before=drawer(s);
    s=execute(s,'cashier',{type:'EXP_PETTY_OUT',amount:860000,category:'Petty cash',for:'Tea and snacks'});
    const e=s.expenses.at(-1);assert.deepEqual([e.status,e.method,e.description,e.source],['Pending','Cash','Petty: Tea and snacks','petty']);
    assert.deepEqual([drawer(s),M.expPetty(s).disbursed,M.expPetty(s).pending,M.expPetty(s).status],[before,0,860000,'Healthy']);
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:e.id,approve:true});
    assert.deepEqual([drawer(s),M.expPetty(s).disbursed,M.expPetty(s).balance,M.expPetty(s).status],[before-860000,860000,140000,'Replenish soon']);
    assert.equal(execute(s,'cashier',{type:'EXP_PETTY_OUT',amount:100,category:'Petty cash'}).expenses.at(-1).description,'Petty: sundry');});
  // DEC-129 (SCC decisions wave): budgets are set and copied by the Owner only (was Manager / Accountant / Owner).
  test('[EXP-SCC] budgets: the Owner sets the business month, copies last month; the entry result carries the alert',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'cashier',{type:'EXP_BUDGET_SET',category:'Supplies',amount:100}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'EXP_BUDGET_COPY'}),says('VALIDATION',/No budgets found for 2026-09/));
    s={...s,expensesX:{budgets:{'2026-09':{Supplies:200000,Laundry:100000}}}};
    s=execute(s,'owner',{type:'EXP_BUDGET_SET',category:'laundry',amount:50000});
    s=execute(s,'owner',{type:'EXP_BUDGET_COPY'});
    assert.deepEqual(M.expSlice(s).budgets['2026-10'],{Laundry:50000,Supplies:200000});
    assert.throws(()=>execute(s,'owner',{type:'EXP_BUDGET_COPY'}),says('VALIDATION',/Every head already has a budget for 2026-10/));
    const rows=M.expBudgetRows(s,'2026-10',M.mnyCategoryNames(s));
    assert.deepEqual(rows.find(r=>r.category==='Laundry'),{category:'Laundry',budget:50000,actual:180000,pct:360,status:'over'});
    s=execute(s,'reception',{type:'EXPENSE',description:'Soap',category:'Supplies',amount:190000,method:'UPI'});
    assert.match(s.audit[0].detail,/Supplies would be 95% of Oct budget once approved/);
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:s.expenses.at(-1).id,approve:true});
    assert.match(s.audit[0].detail,/Supplies now 95% of Oct budget/);});
  // DEC-129: templates are kept by the Owner only (was Manager / Accountant / Owner); posting stays with every expense user.
  test('[EXP-SCC] recurring: templates by the Owner, same-name confirm, posting raises a DRAFT for approval, "post again" confirm',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'cashier',{type:'EXP_REC_SAVE',name:'Rent',amount:100,dueDay:1,category:'Utilities',method:'Bank transfer'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'EXP_REC_SAVE',name:'Rent',amount:100,dueDay:32,category:'Utilities',method:'Bank transfer'}),says('VALIDATION',/between 1 and 31/));
    assert.throws(()=>execute(s,'owner',{type:'EXP_REC_SAVE',name:' ',amount:100,dueDay:3,category:'Utilities',method:'Bank transfer'}),/Template name is required/);
    s=execute(s,'owner',{type:'EXP_REC_SAVE',name:'Shop rent',amount:2500000,dueDay:31,category:'Utilities',method:'Bank transfer',vendor:'Landlord (fictional)'});
    assert.throws(()=>execute(s,'owner',{type:'EXP_REC_SAVE',name:'shop RENT',amount:1,dueDay:1,category:'Utilities',method:'UPI'}),/A template named "shop RENT" already exists\. Add another\?/);
    const t=M.expSlice(s).recurring[0];
    assert.equal(M.expRecStatus(s,t).kind,'upcoming');assert.equal(M.expRecurringDue({...s,date:'2026-10-31'}).length,1);
    s=execute(s,'cashier',{type:'EXP_REC_POST',id:t.id,amount:2400000});
    const e=s.expenses.at(-1);
    assert.deepEqual([e.status,e.description,e.source,e.sourceRef,e.vendor,e.amount],['Draft','Shop rent — 2026-10','recurring','rec:'+t.id+':2026-10','Landlord (fictional)',2400000]);
    assert.equal(M.expRecStatus(s,t).kind,'posted');
    assert.throws(()=>execute(s,'cashier',{type:'EXP_REC_POST',id:t.id,amount:2400000}),/Already posted for 2026-10 \(1 entry · ₹24,000\)\. Confirming will post ANOTHER entry/);
    s=execute(s,'cashier',{type:'EXP_REC_POST',id:t.id,amount:100,again:true});assert.equal(M.expRecPosted(s,'2026-10')['rec:'+t.id+':2026-10'],2);
    s=execute(s,'cashier',{type:'EXP_VOID',id:s.expenses.at(-1).id,reason:'Duplicate entry'});assert.equal(M.expRecPosted(s,'2026-10')['rec:'+t.id+':2026-10'],1,'a void re-flags');
    s=execute(s,'owner',{type:'EXP_REC_TOGGLE',id:t.id});assert.throws(()=>execute(s,'cashier',{type:'EXP_REC_POST',id:t.id,amount:1}),/inactive/);
    s=execute(s,'owner',{type:'EXP_REC_DELETE',id:t.id});assert.equal(M.expSlice(s).recurring.length,0);assert.ok(s.expenses.some(x=>x.sourceRef==='rec:'+t.id+':2026-10'),'posted entries are kept');});
  // DEC-130 (SCC decisions wave): the credit itself never touches cash, but a CASH payment is now a desk receipt into the drawer (was: never).
  test('[EXP-SCC] Udhaar: recording a credit never touches cash; payments capped above outstanding + ₹0.50; write-off by Manager / Accountant / Owner; reminder names the hotel',()=>{
    let s=seed();const before=drawer(s);
    assert.throws(()=>execute(s,'cashier',{type:'EXP_UD_ADD',name:' ',amount:100}),/Customer name is required/);
    s=execute(s,'cashier',{type:'EXP_UD_ADD',name:'Ramesh (fictional)',mobile:'+91 98765 43210',amount:250000,note:'Banquet snacks'});
    assert.equal(drawer(s),before,'recording the credit adds nothing to the drawer');
    const r=M.expSlice(s).udhaar[0];assert.deepEqual([r.mobile,r.date,r.dueDate],['9876543210',D,'2026-11-23']);
    s=execute(s,'cashier',{type:'EXP_UD_PAY',id:r.id,amount:100000,mode:'Cash'});
    s=execute(s,'cashier',{type:'EXP_UD_PAY',id:r.id,amount:150050,mode:'UPI',reference:'UPI-DEMO-1'});
    assert.equal(M.expUdOut(M.expSlice(s).udhaar[0]),0);assert.equal(M.expUdPaid(M.expSlice(s).udhaar[0]),250050,'up to ₹0.50 over is taken as typed');
    let t=execute(seed(),'cashier',{type:'EXP_UD_ADD',name:'Suresh',amount:100000});const id=M.expSlice(t).udhaar[0].id;
    t=execute(t,'cashier',{type:'EXP_UD_PAY',id,amount:100051,mode:'Cash'});assert.equal(M.expUdPaid(M.expSlice(t).udhaar[0]),100000,'capped');
    assert.equal(drawer(s),before+100000,'DEC-130: the cash payment went into the drawer, the UPI one did not');
    let u=execute(seed(),'reception',{type:'EXP_UD_ADD',name:'Mohan',amount:50000,dueDate:'2026-10-20'});const uid=M.expSlice(u).udhaar[0].id;
    assert.equal(M.expUdOpen(u).total,50000);
    assert.equal(M.expUdReminder(u.settings.hotel,M.expSlice(u).udhaar[0]),`Namaste Mohan,\nA gentle reminder from ${u.settings.hotel} regarding your pending balance of ₹500 (due 2026-10-20).\nKindly clear it at your convenience. Thank you.`);
    assert.throws(()=>execute(u,'cashier',{type:'EXP_UD_VOID',id:uid}),code('FORBIDDEN'));
    u=execute(u,'manager',{type:'EXP_UD_VOID',id:uid,reason:'Written off'});assert.equal(M.expUdOpen(u).open.length,0);assert.ok(M.expSlice(u).udhaar[0].void,'kept for audit');});

  // ── 5. Month lock (SCC tax feed) and the SCC CSVs ──
  test('[EXP-SCC] month lock: Owner only, never a future month; writes into the locked month need the Owner + a reason and turn the feed stale; regenerate clears it',()=>{
    let s=seed();
    assert.throws(()=>execute(s,'manager',{type:'EXP_MONTH_LOCK',month:'2026-10'}),code('FORBIDDEN'));
    assert.throws(()=>execute(s,'owner',{type:'EXP_MONTH_LOCK',month:'2026-11'}),says('VALIDATION',/a future month cannot be locked/));
    s=execute(s,'owner',{type:'EXP_MONTH_LOCK',month:'2026-10'});
    const f=M.expSlice(s).months['2026-10'];assert.deepEqual([f.expense,f.count,f.gstPaid,f.byCategory],[180000,1,0,{Laundry:180000}]);
    assert.equal(M.expFeedStale(s,'2026-10'),false);
    const add={type:'EXPENSE',description:'Late bill',category:'Supplies',amount:500,method:'UPI'};
    assert.throws(()=>execute(s,'reception',add),says('FORBIDDEN',/Month 2026-10 is tax-locked — its tax feed was already generated\. Owner override required\./));
    assert.throws(()=>execute(s,'manager',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true}),code('FORBIDDEN'));
    assert.equal(execute(s,'manager',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:false}).expenses.find(e=>e.id==='EXP-102').status,'Rejected','a rejection posts nothing');
    assert.throws(()=>execute(s,'owner',add),says('VALIDATION',/override reason/));
    s=execute(s,'owner',{...add,override:'Bill found late'});
    assert.match(s.audit[0].detail,/locked-month override 2026-10: Bill found late/);
    assert.equal(M.expFeedStale(s,'2026-10'),true);
    assert.throws(()=>execute(s,'owner',{type:'EXP_VOID',id:s.expenses.at(-1).id,reason:'Other'}),/add a specific note for the override/);
    s=execute(s,'owner',{type:'EXP_MONTH_LOCK',month:'2026-10'});assert.equal(M.expFeedStale(s,'2026-10'),false,'regenerated');
    // derived stale: a record of the month posted after the feed
    const t=execute(execute(seed(),'owner',{type:'EXP_MONTH_LOCK',month:'2026-10'}),'owner',{type:'EXPENSE_APPROVE',id:'EXP-102',approve:true,override:'Approved late'});
    assert.equal(M.expFeedStale(t,'2026-10'),true);});
  test('[EXP-SCC] ledger CSV + month CSV: SCC columns (+ HCC Ref, Status), SCC file names, every record vs the month’s counted records',()=>{
    let s=seed();s=execute(s,'reception',{type:'EXPENSE',description:'Cement, "grade 53"',vendor:'=Hardware',category:'Maintenance',amount:118050,method:'UPI',gstRateBps:1800,gstPaise:18008});
    s=execute(s,'owner',{type:'EXPENSE_APPROVE',id:s.expenses.at(-1).id,approve:true});
    s=execute(s,'owner',{type:'EXP_VOID',id:'EXP-102',reason:'Duplicate entry',note:'entered twice'});
    const L=M.expLedgerCsv(s),lines=L.csv.split('\n');
    assert.equal(L.name,'ledger_2026-10-24.csv');
    assert.equal(lines[0],'Date,Firm,Type,Category,Mode,Vendor,Description,Amount,Source,Void,VoidReason,CreatedBy,Store,GSTAmt,GSTRate%,Ref,Status,ITC'); // DEC-135: + ITC
    assert.equal(lines.length,4);
    const h=s.settings.hotel;
    assert.ok(lines.includes(`2026-10-24,${h},expense,Supplies,UPI,Morning Supplies,Morning Supplies,850,manual,Y,Duplicate entry — entered twice,${s.users.find(u=>u.id==='reception').name},Unassigned,,,EXP-102,Void,`),lines.join('\n'));
    const cem=s.expenses.find(e=>e.category==='Maintenance');
    assert.ok(lines.includes(`2026-10-24,${h},expense,Maintenance,UPI,'=Hardware,"Cement, ""grade 53""",1180.5,manual,,,${s.users.find(u=>u.id==='reception').name},Unassigned,180.08,18,${cem.id},Posted,disallowed`),lines.join('\n'));
    const Mo=M.expMonthCsv(s,'2026-10'),ml=Mo.csv.split('\n');
    assert.equal(Mo.name,'expense_month_2026-10.csv');
    assert.equal(ml[0],'Date,Firm,Type,Category,Mode,Vendor,Description,Amount,Source,GSTAmt,GSTRate%,Ref,Status,ITC');
    assert.equal(ml.length,3,'the void and pending records are not in the month file');});
}
