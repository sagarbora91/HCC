// Module 20 · Accounting — the rulebook golden month (the WHOLE of §8.2, ACC-2), scenario ACC-RULEBOOK. Fictional data only.
// Rulebook: 02 Planning/HCC_Accounting_Rulebook_v1.md §8 (April 2026: opening ₹77,40,000, transactions T1–T19, closing trial balance ₹84,27,500).
// This fixture drives April 2026 day by day with REAL commands — the hand-entered opening balances (ACC-1 contract §5: ACC_OPENING_SAVE /
// ACC_OPENING_APPROVE) with the HDFC term loan as a party row (ACC_LOAN_ADD, ACC-2 contract §2), room nights with an invoice discount, restaurant on
// folios, walk-in outlet bills, an OTA stay invoiced to the guest and moved to the OTA receivable at check-out (T4, `ota-credit`) with the OTA's net payout
// from its statement (T19, CHN_PAYOUT), the opening advance applied (T5a, DEPOSIT_APPLY), company credit and the company's bank payments net of TDS
// (T7b, GRP_COMPANY_PAYMENT {tds}), guest payments by cash / UPI / card, the May advance by UPI (T6, no GST), two vendor bills on credit, the vendor payment
// allocated oldest first (T11, EXP_VENDOR_PAY), electricity by bank, sundry cash expenses, the salary advance, the April pay run locked and paid (T13a–c:
// attendance by HR_ATT_MARK every day, HR_PAYROLL_RUN / _LOCK / _BANK_FILE on 1 May — a month can be paid only once it is complete), UPI and card
// settlements to the bank (T14a/b, manual vouchers), cash banked, a cash shortage, the Owner's drawing from the bank (T16), the loan EMI (T17) and the bank
// charges (T18) as guided-template manual vouchers — closing every business day exactly as the cashier does (night posting, exact cash count, day close,
// next day). Ends closed on 30 April, open on 1 May (payroll posted on 1 May). tests/stream-accounting-rulebook.mjs holds the expectations.
// ACC-2 command shapes are written against 02 Planning/agents/ACC2-POSTING-CONTRACT.md; where the contract leaves a field open the assumption is
// marked `ASSUMPTION` here and listed in 05 Delivery Evidence/Accounting/ACC2-TESTS-DONE.md.
// The command engine is injected (accRulebookBind, bound once in services/mock/rulebook-bind.ts): a fixture must not import the command
// registry, or the scenario worker would carry every handler.
import type {State,Booking,Command} from '../../domain/types';
import type {ScenarioInfo} from '../scenarios';
import {totals,drawer} from '../../domain/selectors';
import {mnyAccounts,mnyMethods} from '../../domain/streams/money-master';
import {hrRunTotals,type HRStaff} from '../../domain/streams/staff';

type Exec=(s:State,uid:string,cmd:Command)=>State;
let engine:Exec|undefined;
/** Called once by services/mock/rulebook-bind.ts with the real `execute`. */
export function accRulebookBind(execute:Exec):void{engine=execute;}

export const ACC_RULEBOOK='ACC-RULEBOOK';
/** The fictional parties of the month (rulebook §8.1). Codes are the master row codes; the party ledger id is `<control>/<code or id>` (contract §3). */
export const ACC_RB={
  asOf:'2026-04-01',
  company:{name:'Deccan Agro Ltd',gstin:'27ZZFIC0004F1Z2'},
  vendors:[{code:'EV-MORNING',name:'Morning Supplies'},{code:'EV-KAVERI',name:'Kaveri Textiles'}],
  bank:{code:'BA-HDFC',name:'HDFC'},
  /** The HDFC term loan: a party row under 2400 (ACC-2 contract §2, ACC_LOAN_ADD) — `2400/LN-HDFC`. */
  loan:{code:'LN-HDFC',name:'HDFC term loan',lender:'HDFC Bank (fictional)',accountLast4:'4321'},
  channel:{id:'goibibo',name:'Goibibo'},
  staff:'HR-S07', // "Staff-07" of the rulebook (existing staff row)
  /** The May stay whose advance is T6 (room 207, Deluxe ₹3,000, 10 nights from 10 May = ₹30,000 + 5 % = ₹31,500). */
  mayStay:{room:'207',arrival:'2026-05-10',departure:'2026-05-20'},
  /** The OTA stay of T4: room 101 (Classic ₹2,000), 7 → 17 April = ₹20,000 + 5 % GST. */
  otaStay:{room:'101',arrival:'2026-04-07',departure:'2026-04-17'},
};

const OWNER='owner',MANAGER='manager',ACCOUNTANT='accountant',HOUSEKEEPER='housekeeper',SUPERVISOR='supervisor';
const P=(rupees:number)=>Math.round(rupees*100);
const addDays=(d:string,n:number)=>{const x=new Date(d+'T00:00:00Z');x.setUTCDate(x.getUTCDate()+n);return x.toISOString().slice(0,10);};
const photo='data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';

/** The opening balances of rulebook §8.1 as ledger lines (paise). Party ledgers use the master codes / ids created by the fixture (the loan is `2400/LN-HDFC`). */
export function accRulebookOpeningLines(till:string,companyId:string):{account:string;debit:number;credit:number}[]{
  const d=(account:string,r:number)=>({account,debit:P(r),credit:0}),c=(account:string,r:number)=>({account,debit:0,credit:P(r)});
  return [d('1000/'+till,20000),d('1030/'+ACC_RB.bank.code,300000),d('1500',6000000),d('1510',800000),d('1520',500000),d('1530',80000),d('1110/'+companyId,40000),
    c('2200/'+ACC_RB.vendors[0].code,25000),c('2000',10000),c('2400/'+ACC_RB.loan.code,2000000),c('3100',5705000)];
}

/** Stays of the month: [room, nights, group]. Deccan Agro: 6 classic rooms × 5 nights = 30 nights (₹60,000). Guests: 55 classic + 50 deluxe + 20 suite nights. */
const DECCAN=['101','102','105','108','201','202'];
const GUESTS:{room:string;nights:number}[]=[
  {room:'205',nights:10},{room:'208',nights:10},{room:'301',nights:10},{room:'302',nights:10},{room:'305',nights:10},{room:'308',nights:5},
  {room:'103',nights:10},{room:'104',nights:10},{room:'107',nights:10},{room:'203',nights:10},{room:'204',nights:10},
  {room:'106',nights:10},{room:'206',nights:10},
];
/** Room-service check charged to a folio: net ₹5,000 = chicken curry ×6 + paneer butter masala ×4 + paneer tikka ×2 (460×6 + 380×4 + 360×2). */
const FOLIO_CHECK=[['m-curry',6],['m-paneer',4],['m-tikka',2]] as const;
/** Walk-in outlet check: net ₹6,000 = thali ×7 + curry ×5 + paneer butter masala ×2. */
const OUTLET_CHECK=[['m-thali',7],['m-curry',5],['m-paneer',2]] as const;

/** The staff of the April pay run (rulebook T13b): 10 men on ₹20,000 (PF on basic capped at ₹15,000, ESIC, PT ₹200) and 4 women on ₹10,000 (no PF / ESIC, PT-exempt
 *  up to ₹25,000): gross 2,40,000 · PF wages 1,50,000 (12 % + 12 %) · ESIC wages 2,00,000 (0.75 % + 3.25 %) · PT 2,000. Existing ids keep their login links. */
export const ACC_RB_PAYROLL={men:['HR-S02','HR-S05','HR-S07','HR-S08','HR-S09','HR-S10','HR-S11','HR-S12','HR-S13','HR-S14'],women:['HR-S01','HR-S03','HR-S04','HR-S06']};

/** What happens on which April day (T-numbers are the rulebook's). Every day also runs the night audit, the exact cash count, the day close and the next day. */
export const ACC_RB_PLAN:Record<string,string>={
  '2026-04-01':'HDFC term loan added (2400/LN-HDFC); opening entry saved by the Manager and approved by the Owner; the pay-run staff and the April statutory rates set; Deccan Agro (6 rooms × 5 nights) and 13 guest stays check in',
  '2026-04-01…30':'attendance P for every staff member, every day (HR_ATT_MARK)',
  '2026-04-02…10':'T2 room-service checks charged to folios (20 × ₹5,000 over days 1–9), T3 walk-in outlet checks (10 × ₹6,000 net, every second day, cash and UPI alternately)',
  '2026-04-05':'T8 Morning Supplies bill ₹59,000 on vendor credit (ITC disallowed); T17 loan EMI ₹55,000 from the bank (template loan-emi)',
  '2026-04-06':'T5b Deccan Agro checks out on company credit ₹63,000; T5a/T5c room 308: the opening advance applied with DEPOSIT_APPLY, the rest paid; room 101 cleaned',
  '2026-04-07':'T4 the Goibibo guest checks in to room 101 (10 nights, ₹20,000 + 5 % GST)',
  '2026-04-08':'T13a salary advance to HR-S07 ₹5,000 (cash)',
  '2026-04-10':'T7a Deccan Agro pays ₹40,000 by bank transfer',
  '2026-04-11':'T1 discount ₹10,000 (Owner); T5a/T5c the other 12 guests check out and pay: cash, UPI, card',
  '2026-04-12':'T9 Kaveri Textiles bill ₹42,000 on vendor credit (ITC disallowed)',
  '2026-04-14,18':'T12 sundry cash expenses 2 × ₹4,000',
  '2026-04-15':'T10 electricity ₹45,000 paid by bank',
  '2026-04-17':'T4 the Goibibo guest checks out: the folio ₹21,000 moves to the OTA receivable (ota-credit)',
  '2026-04-18':'T11 ₹60,000 paid to Morning Supplies by bank, allocated oldest first (₹25,000 opening, ₹35,000 of the ₹59,000 bill)',
  '2026-04-20':'T6 advance ₹31,500 by UPI for a May stay (no GST: the switch is off)',
  '2026-04-25':'T15a cash banked ₹1,50,000 (slip, photo, confirmed by the Owner)',
  '2026-04-26':'T15b cash short ₹200 accepted by the Owner',
  '2026-04-27':'T14a UPI settlements ₹2,13,000 reach the bank (Contra); T14b card settlement ₹1,50,000 less MDR ₹1,500 + GST ₹270 (Journal)',
  '2026-04-28':'T16 the Owner draws ₹50,000 from the bank (template owner-drawing-bank)',
  '2026-04-29':'T19 Goibibo payout from its statement: commission ₹3,000 + GST ₹540, TCS ₹100, net ₹17,360',
  '2026-04-30':'T7b Deccan Agro pays ₹57,000 + TDS ₹6,000; T18 bank charges ₹500 + GST ₹90 (template bank-charges)',
  '2026-05-01':'T13b/c the April pay run prepared (Accountant), locked (Owner) and the bank file generated — a month is paid once it is complete',
};

function need(f:Exec|undefined):Exec{if(!f)throw new Error('ACC-RULEBOOK needs the command engine (services/mock/rulebook-bind.ts binds it).');return f;}

/**
 * Drives April 2026 on `s0` (any state: it is cleared of operations, dated 1 April and run forward). Returns the state after 30 April is
 * closed, 1 May is open and the April pay run is locked and paid. Every change goes through `exec` (the real command engine); only master-data setup that
 * has no command is written directly (the OTA channel row, the company's opening outstanding, the pay-run staff roster).
 */
export function accRulebookMonth(s0:State,exec:Exec=need(engine)):State{
  let s=clearOperations(s0);
  const run=(uid:string,cmd:Command)=>{s=exec(s,uid,cmd);return s;};
  // ── master data: bank account, vendors, company, OTA channel, the HDFC loan ──
  const till=mnyAccounts(s).find(a=>a.kind==='till')!.code;
  run(OWNER,{type:'MST_MASTER_SAVE',master:'bankAccounts',row:{code:ACC_RB.bank.code,name:ACC_RB.bank.name,kind:'bank',bank:'HDFC Bank (fictional)',accountLast4:'4321',ifsc:'DEMO0001234'}});
  const bankMethod=mnyMethods(s).find(r=>r.name==='Bank transfer')!;
  run(OWNER,{type:'MST_MASTER_SAVE',master:'paymentMethods',row:{...bankMethod,settlesTo:ACC_RB.bank.code}});
  for(const v of ACC_RB.vendors)run(OWNER,{type:'MST_MASTER_SAVE',master:'expenseVendors',row:{code:v.code,name:v.name,type:v.name.startsWith('Kaveri')?'laundry':'supplier',tds:false}});
  run(OWNER,{type:'GRP_COMPANY_SAVE',name:ACC_RB.company.name,gstin:ACC_RB.company.gstin,contact:'Anita Kulkarni (fictional)',phone:'+91 90000 00044',email:'accounts@deccanagro.example.test',city:'Latur',
    rates:{Classic:200000},creditLimit:P(1000000)});
  const company=s.groupsX!.companies.find(c=>c.name===ACC_RB.company.name)!;
  company.openingOutstanding=P(40000); // the ₹40,000 owed before HCC (rulebook §8.1)
  s.channelsX!.channels.push({id:ACC_RB.channel.id,name:ACC_RB.channel.name,status:'Connected',commissionBps:1500,markupBps:0,mapping:{},stopSell:[],statusNote:'Simulated connection'});
  // ACC-2 contract §2: the HDFC term loan is an Owner-added party row under 2400 (kind 'loan', lender, last four digits) — the opening and the EMI name it.
  // ASSUMPTION: payload {code, name, lender, accountLast4, secured} (the contract names the command ACC_LOAN_ADD and the row fields, not the payload).
  run(OWNER,{type:'ACC_LOAN_ADD',code:ACC_RB.loan.code,name:ACC_RB.loan.name,lender:ACC_RB.loan.lender,accountLast4:ACC_RB.loan.accountLast4,secured:true});
  // ── the opening entry (ACC-1 contract §5): entered by the Manager, approved by the Owner ──
  run(MANAGER,{type:'ACC_OPENING_SAVE',asOf:ACC_RB.asOf,lines:accRulebookOpeningLines(till,company.id),note:'Rulebook §8.1 opening balances, 1 April 2026 (fictional)'});
  run(OWNER,{type:'ACC_OPENING_APPROVE'});
  // ACC-2 (parity lane, contract §8): a restaurant / room-service charge on a folio is a `food` line → 4200 by default, so the ACC-1 Owner override
  // `line:service → 4200` is gone. This month has no other service line (no laundry, no banquet).
  // ── the pay-run staff (T13b) and the April statutory rates: PF employer 12 % (the SCC default is 13 %) ──
  setupPayrollStaff(s);
  run(OWNER,{type:'HR_STAT_RATES_SET',from:'2026-04-01',pfErBps:1200});
  // ── 1 April: arrivals ──
  const stays:{id:string;room:string;nights:number;company?:boolean}[]=[];
  const book=(room:string,nights:number,guest:string,onCompany:boolean)=>{
    run(MANAGER,{type:'BOOK',guest,room,arrival:s.date,departure:addDays(s.date,nights),people:2,phone:'+91 90000 12345',source:'Direct',...(onCompany?{company:{account:company.id}}:{})});
    const b=s.bookings.at(-1)!;
    run(MANAGER,{type:'REGISTER',id:b.id});run(MANAGER,{type:'CHECKIN',id:b.id});
    stays.push({id:b.id,room,nights,...(onCompany?{company:true}:{})});
  };
  DECCAN.forEach((room,i)=>book(room,5,`Deccan Agro delegate ${i+1} (fictional)`,true));
  GUESTS.forEach((g,i)=>book(g.room,g.nights,`Guest ${String.fromCharCode(65+i)} (fictional)`,false));
  const expectRate:Record<string,number>={Classic:200000,Deluxe:300000,Suite:450000};
  for(const b of s.bookings){const type=s.rooms.find(r=>r.id===b.room)!.type;if(b.rate!==expectRate[type])throw new Error(`ACC-RULEBOOK: ${b.room} rate ${b.rate} is not the ${type} base rate ${expectRate[type]}`);}
  let otaId='';

  // restaurant checks charged to folios (T2): 20 × ₹5,000 over the stay, spread over four rooms
  const folioRooms=['103','104','106','206'],checks:{day:number;room:string}[]=[];
  for(let i=0;i<20;i++)checks.push({day:1+(i%9),room:folioRooms[i%4]});
  // walk-in outlet checks (T3): 5 by cash, 5 by UPI, ₹6,000 net each
  const outlet:{day:number;method:'Cash'|'UPI'}[]=[];
  for(let i=0;i<10;i++)outlet.push({day:2+i*2,method:i%2?'UPI':'Cash'});
  let checkNo=0;
  const pos=(uid:string,open:Record<string,any>,items:readonly (readonly [string,number])[],settle:Record<string,any>)=>{
    run(uid,{type:'POS_ORDER_OPEN',...open});const o=s.posX!.orders.at(-1)!;
    for(const [item,qty] of items)run(uid,{type:'POS_ITEM_ADD',id:o.id,item,qty});
    run(uid,{type:'POS_KOT_SEND',id:o.id});run(uid,{type:'POS_SETTLE',id:o.id,...settle});
  };

  const bank=(amount:number)=>{
    run(MANAGER,{type:'CASH_BANK',amount,bank:ACC_RB.bank.name,slip:'SLIP-'+s.date.replace(/-/g,''),photo});
    run(OWNER,{type:'CASH_BANK_DECIDE',id:s.cashOutX!.records.at(-1)!.id,confirm:true});
  };
  const expense=(e:Record<string,any>)=>{run(MANAGER,{...e,type:'EXPENSE'});run(OWNER,{type:'EXPENSE_APPROVE',id:s.expenses.at(-1)!.id,approve:true});};
  /** A guided-template manual voucher (ACC-2 contract §2) entered by the Owner (approved at once). The command's own `type` is its name, so the voucher
   *  type (Journal / Contra / Payment / Receipt) cannot travel as `type`. ASSUMPTION: the key is not fixed by the contract — it is sent as `voucherType`
   *  and, for a handler that reads another name, also as `vtype` / `kind` (one helper: change it here only). */
  const manual=(voucherType:'Journal'|'Contra'|'Payment'|'Receipt',template:string,narration:string,reference:string,lines:{account:string;debit:number;credit:number}[])=>
    run(OWNER,{type:'ACC_MANUAL_SAVE',date:s.date,voucherType,vtype:voucherType,kind:voucherType,narration,reference,lines,template});
  /** Cleans a vacated room back to Ready (housekeeper does the checklist, the supervisor inspects). */
  const clean=(room:string)=>{
    const t=s.tasks.find(t=>t.room===room&&t.status==='To do');if(!t)throw new Error('ACC-RULEBOOK: no housekeeping task for room '+room);
    run(HOUSEKEEPER,{type:'TASK',id:t.id,action:'start'});
    for(let i=0;i<3;i++)run(HOUSEKEEPER,{type:'TASK',id:t.id,action:'check',index:i});
    run(HOUSEKEEPER,{type:'TASK',id:t.id,action:'complete'});run(SUPERVISOR,{type:'TASK',id:t.id,action:'inspect'});
  };

  // guest folios are settled at check-out: cash, UPI, card up to the month's totals (T5c). The ₹10,000 advance held since before HCC is applied first (T5a).
  const pools:{method:string;left:number}[]=[{method:'Cash',left:P(152000)},{method:'UPI',left:P(150000)},{method:'Card',left:P(150000)}];
  let payNo=0;
  const settleGuest=(b:Booking)=>{
    let owe=totals(s.bookings.find(x=>x.id===b.id)!).balance;const tenders:{method:string;amount:number;reference:string}[]=[];
    for(const p of pools){if(owe<=0)break;const take=Math.min(owe,p.left);if(take<=0)continue;p.left-=take;owe-=take;
      tenders.push({method:p.method,amount:take,reference:p.method==='Cash'?'':`DEMO-${p.method.toUpperCase()}-${++payNo}`});}
    if(owe!==0)throw new Error('ACC-RULEBOOK: guest tenders do not cover the folio of '+b.id);
    const amount=tenders.reduce((a,t)=>a+t.amount,0);
    if(amount>0)run(MANAGER,{type:'PAYMENT',id:b.id,amount,kind:'payment',...(tenders.length>1?{tenders}:{method:tenders[0].method,reference:tenders[0].reference})});
    run(MANAGER,{type:'CHECKOUT',id:b.id});
  };
  const staffIds=[...ACC_RB_PAYROLL.men,...ACC_RB_PAYROLL.women];

  for(let day=1;day<=30;day++){
    const date=s.date;
    if(date!==addDays(ACC_RB.asOf,day-1))throw new Error('ACC-RULEBOOK: business date drifted to '+date);
    // attendance: everybody present on every working day; Sunday is everybody's weekly off and is not marked (SCC pay days: 26 present + the 4 extra days of a full
    // attendance = 30 salary days, no overtime — the April pay run pays exactly the monthly salary)
    if(new Date(date+'T12:00:00Z').getUTCDay()!==0)for(const staffId of staffIds)run(OWNER,{type:'HR_ATT_MARK',staffId,status:'P'});
    // restaurant checks on folios and walk-in checks of the day
    for(const c of checks.filter(c=>c.day===day))pos(MANAGER,{outlet:'room-service',room:c.room,covers:2},FOLIO_CHECK,{mode:'folio'});
    for(const o of outlet.filter(o=>o.day===day))pos(MANAGER,{outlet:'restaurant',table:'T'+(1+(checkNo++%8)),covers:2},OUTLET_CHECK,
      {mode:'outlet',method:o.method,reference:o.method==='Cash'?'':'DEMO-UPI-OUTLET-'+checkNo});
    if(day===5){
      expense({vendor:'Morning Supplies',category:'Supplies',amount:P(59000),method:'Vendor credit',description:'Consumables, invoice MS/26-27/118 (fictional)',gstRateBps:1800,gstPaise:P(9000),itc:'disallowed'});
      // T17 loan EMI ₹55,000 from the bank: principal 40,000 against the loan, interest 15,000 (template loan-emi).
      manual('Payment','loan-emi','HDFC term loan EMI, April (fictional)','EMI-DEMO-0405',
        [{account:'2400/'+ACC_RB.loan.code,debit:P(40000),credit:0},{account:'5090',debit:P(15000),credit:0},{account:'1030/'+ACC_RB.bank.code,debit:0,credit:P(55000)}]);
    }
    if(day===6){
      for(const st of stays.filter(x=>x.company)){
        run(MANAGER,{type:'COMPANY_CREDIT_REQUEST',id:st.id,reason:'Deccan Agro account'});
        run(OWNER,{type:'APPROVAL',id:s.approvals.at(-1)!.id,approve:true});
        run(MANAGER,{type:'CHECKOUT',id:st.id});
      }
      // T5a (contract §4, A6a): the ₹10,000 advance held since before HCC is applied to room 308's folio — no deposit receipt exists in HCC for it
      // (booked before HCC: no advance-GST line), so the command is DEPOSIT_APPLY {booking, amount, source:'opening'} (coordinator ruling 4: the advance comes from 2000 in the opening balance; Manager / Owner).
      const b308=stays.find(x=>x.room==='308')!;
      run(MANAGER,{type:'DEPOSIT_APPLY',booking:b308.id,amount:P(10000),source:'opening'});
      settleGuest(s.bookings.find(b=>b.id===b308.id)!);
      clean(ACC_RB.otaStay.room);
    }
    if(day===7){
      // T4 (contract §4, A13): an OTA stay invoiced to the guest, paid by the OTA; the channel row is the connected Goibibo.
      run(MANAGER,{type:'BOOK',guest:'Goibibo guest (fictional)',room:ACC_RB.otaStay.room,arrival:ACC_RB.otaStay.arrival,departure:ACC_RB.otaStay.departure,people:2,
        phone:'+91 90000 12345',source:'OTA',otaChannel:ACC_RB.channel.name});
      otaId=s.bookings.at(-1)!.id;
      run(MANAGER,{type:'REGISTER',id:otaId});run(MANAGER,{type:'CHECKIN',id:otaId});
    }
    // T13a salary advance ₹5,000 to Staff-07 in cash. ASSUMPTION: the funding travels as `funding` (the server's AdvanceGiven field) and `method`.
    if(day===8)run(OWNER,{type:'HR_ADVANCE_ADD',staffId:ACC_RB.staff,amount:P(5000),installment:P(5000),reason:'Salary advance (fictional)',funding:'Cash',method:'Cash'});
    if(day===10)run(OWNER,{type:'GRP_COMPANY_PAYMENT',company:company.id,amount:P(40000),method:'Bank transfer',reference:'NEFT-DEMO-0410'});
    if(day===11){
      const disc=s.bookings.find(b=>b.room==='206')!;
      run(OWNER,{type:'DISCOUNT',id:disc.id,amount:P(10000),reason:'Service recovery (fictional)'});
      for(const g of GUESTS.filter(g=>g.nights===10))settleGuest(s.bookings.find(b=>b.room===g.room)!);
    }
    if(day===12)expense({vendor:'Kaveri Textiles',category:'Laundry',amount:P(42000),method:'Vendor credit',description:'Linen, invoice KT/118 (fictional)',gstRateBps:500,gstPaise:P(2000),itc:'disallowed'});
    if(day===14)expense({vendor:'',category:'Petty cash',amount:P(4000),method:'Cash',description:'Sundry cash expenses (1 of 2, fictional)'});
    if(day===15)expense({vendor:'State Electricity Board (fictional)',category:'Utilities',amount:P(45000),method:'Bank transfer',description:'Electricity, March bill (exempt supply)'});
    // T4 check-out: the folio balance ₹21,000 moves to the OTA receivable (receipt kind `ota-credit`, contract §4).
    // ASSUMPTION: the contract names the receipt kind and the rule (booking source is a connected channel) but no command — CHECKOUT with `otaCredit:true`
    // is written here, in one place; change this line to the flows lane's command.
    if(day===17)run(MANAGER,{type:'CHECKOUT',id:otaId,otaCredit:true});
    if(day===18){
      expense({vendor:'',category:'Petty cash',amount:P(4000),method:'Cash',description:'Sundry cash expenses (2 of 2, fictional)'});
      // T11 (contract §3.2, C3): ₹60,000 to Morning Supplies by bank, allocated oldest first by default (the opening ₹25,000, then ₹35,000 of the ₹59,000 bill).
      run(OWNER,{type:'EXP_VENDOR_PAY',vendor:'Morning Supplies',amount:P(60000),method:'Bank transfer',reference:'NEFT-DEMO-0418',date:s.date});
    }
    if(day===20){
      // T6 (contract §4, A6, DEC-142): advance by UPI for a May stay; the switch `gstOnAdvances` is off (default) → no GST line.
      run(MANAGER,{type:'BOOK',guest:'May guest (fictional)',room:ACC_RB.mayStay.room,arrival:ACC_RB.mayStay.arrival,departure:ACC_RB.mayStay.departure,people:2,phone:'+91 90000 12345',source:'Direct'});
      run(MANAGER,{type:'PAYMENT',id:s.bookings.at(-1)!.id,amount:P(31500),kind:'deposit',method:'UPI',reference:'DEMO-UPI-ADVANCE-0420'});
    }
    if(day===25)bank(P(150000));
    if(day===27){
      // T14a / T14b (B8): settlements reach the bank. UPI: a Contra between two bank-group ledgers. Card: the MDR line (5080) is not a cash / bank ledger, so
      // the voucher is a Journal (contract §2 guards: a Contra only moves between cash / bank ledgers).
      manual('Contra','contra','UPI settlements to HDFC, April (fictional)','UPI-SETTLE-APR',
        [{account:'1030/'+ACC_RB.bank.code,debit:P(213000),credit:0},{account:'1010',debit:0,credit:P(213000)}]);
      manual('Journal','journal','Card settlement less MDR 1 % and GST 18 % (fictional)','CARD-SETTLE-APR',
        [{account:'1030/'+ACC_RB.bank.code,debit:P(148230),credit:0},{account:'5080',debit:P(1770),credit:0},{account:'1020',debit:0,credit:P(150000)}]);
    }
    if(day===28)manual('Payment','owner-drawing-bank','Owner drawing from the bank (fictional)','DRAW-DEMO-0428',
      [{account:'3000',debit:P(50000),credit:0},{account:'1030/'+ACC_RB.bank.code,debit:0,credit:P(50000)}]);
    if(day===29){
      // T19 (contract §4, A13, DEC-150): Goibibo pays net from its statement — gross 21,000 = net 17,360 + commission 3,000 + GST 540 + TCS 100.
      run(OWNER,{type:'CHN_PAYOUT',channel:ACC_RB.channel.id,date:s.date,statementRef:'GOI-STMT-2026-04-A',gross:P(21000),commission:P(3000),commissionGst:P(540),tcs:P(100),
        net:P(17360),bank:ACC_RB.bank.code,bookings:[otaId]});
    }
    if(day===30){
      // T7b (contract §4, A9): Deccan Agro pays the April bill net of TDS 10 % on ₹60,000 — ₹57,000 by bank and ₹6,000 held back (Dr 1360).
      run(OWNER,{type:'GRP_COMPANY_PAYMENT',company:company.id,amount:P(57000),tds:P(6000),method:'Bank transfer',reference:'NEFT-DEMO-0430'});
      manual('Payment','bank-charges','Bank charges ₹500 + GST ₹90, April (fictional)','BANKCHG-DEMO-0430',
        [{account:'5080',debit:P(590),credit:0},{account:'1030/'+ACC_RB.bank.code,debit:0,credit:P(590)}]);
    }
    // day end: night audit, exact cash count (a ₹200 shortage on 26 April, accepted by the Owner), day close, next day
    run(MANAGER,{type:'NIGHT_POST'});
    if(day===26){
      run(MANAGER,{type:'SHIFT_SUBMIT',counted:counted(s)-P(200),reason:'Short by ₹200 (fictional)'});
      run(OWNER,{type:'SHIFT_DIFFERENCE_DECIDE',id:s.approvals.at(-1)!.id,approve:true});
    }else run(MANAGER,{type:'SHIFT_CLOSE',counted:counted(s)});
    if((s.posX?.orders??[]).some(o=>o.status==='Open'))throw new Error('ACC-RULEBOOK: an outlet check is still open at day end');
    run(OWNER,{type:'DAY_CLOSE'});
    run(OWNER,{type:'NEXT_DAY'});
    run(MANAGER,{type:'SHIFT_OPEN',handover:s.openingCash});
  }
  // ── 1 May: T13b / T13c — the April pay run (a month is paid once it is complete). Prepared by the Accountant, locked by the Owner (the maker never locks). ──
  run(ACCOUNTANT,{type:'HR_PAYROLL_RUN',month:'2026-04'});
  const pay=s.staffX!.runs.find(r=>r.month==='2026-04')!,t=hrRunTotals(pay);
  const want={staff:14,gross:P(240000),pf:P(18000),pfEr:P(18000),esic:P(1500),esicEr:P(6500),pt:P(2000),advance:P(5000),net:P(213500)};
  for(const [k,v] of Object.entries(want))if((t as Record<string,number>)[k]!==v)throw new Error(`ACC-RULEBOOK: pay run ${k} is ${(t as Record<string,number>)[k]}, the rulebook says ${v}`);
  run(OWNER,{type:'HR_PAYROLL_LOCK',id:pay.id});
  run(OWNER,{type:'HR_PAYROLL_BANK_FILE',id:pay.id});
  return s;
}

/** The cash the cashier counts at close (the expected drawer). */
const counted=(s:State)=>drawer(s);

/** The pay-run staff of the month (T13b): ten men on ₹20,000 with PF / ESIC, four women on ₹10,000 without (see ACC_RB_PAYROLL). Master-data setup written directly
 *  (the staff master has a command, but a roster of this size, statutory flags and salaries are set in one place). The base fixture's HR history (dated October) is
 *  cleared by clearOperations. */
function setupPayrollStaff(s:State):void{
  const x=s.staffX!,tpl=x.staff.find(t=>t.id==='HR-S11')!;
  for(const id of [...ACC_RB_PAYROLL.men,...ACC_RB_PAYROLL.women]){
    const woman=ACC_RB_PAYROLL.women.includes(id);
    let st:HRStaff|undefined=x.staff.find(t=>t.id===id);
    if(!st){const n:HRStaff={...JSON.parse(JSON.stringify(tpl)),id,name:`Staff ${id.slice(-2)} (fictional)`};delete n.userId;x.staff.push(n);st=n;}
    st.active=true;delete st.exitedOn;st.joined='2025-04-01';st.payType='monthly';st.monthly=woman?P(10000):P(20000);st.daily=0;
    st.weeklyOff=0;st.structure='B';st.pfApplicable=!woman;st.esicApplicable=!woman;st.gender=woman?'F':'M';
  }
}

/** The base fixture cleared of operations (as scenario S1), a 20,000 float in the drawer, dated 1 April 2026. */
function clearOperations(s0:State):State{
  const s=JSON.parse(JSON.stringify(s0)) as State;
  s.bookings=[];s.tasks=[];s.expenses=[];s.approvals=[];s.closedDates=[];s.closings={};s.processed=[];
  s.rooms.forEach(r=>{r.hk='Ready';r.blocked=false;delete r.blockFrom;delete r.blockTo;});
  if(s.posX){s.posX.orders=[];s.posX.receipts=[];}
  // HR history of the base fixture (an October pay run, advances, attendance) is not part of April.
  if(s.staffX){const h=s.staffX;h.runs=[];h.advances=[];h.att={};h.leaves=[];h.roster={};h.fnf=[];h.requests=[];h.agendas=[];}
  s.date=ACC_RB.asOf;s.clock=ACC_RB.asOf+'T08:00';s.openingCash=P(20000);s.shiftOpen=true;s.shiftClose=undefined;s.shift=undefined;s.closeProgress=undefined;
  return s;
}

export function extendBase(_s:State):void{}
export const scenarioPatches:Record<string,(s:State)=>void>={
  [ACC_RULEBOOK]:s=>{Object.assign(s,accRulebookMonth(s));},
};
export const scenarioInfo:ScenarioInfo[]=[
  {id:ACC_RULEBOOK,name:'Accounting rulebook month (April 2026)',purpose:'The rulebook golden month, the whole of §8.2: hand-entered opening balances (₹77,40,000) with the HDFC loan as a party, 20 stays (one from Goibibo), restaurant and walk-in bills, company credit and payments net of TDS, the Goibibo payout, the opening advance applied, a May advance, vendor bills and a vendor payment, electricity, the April pay run, UPI and card settlements, the Owner\'s drawing, the loan EMI and bank charges — April 2026 closed day by day, opened on 1 May. Fictional.',available:true},
];

// ───────────────────────────── ACC-3 (lane acc3-tests): April 2026 report EXPECTATIONS ─────────────────────────────
// Hand-computed from rulebook §8.1–§8.6 (never exported from the code under test). Paise, signed as the contract shapes of
// 02 Planning/agents/ACC3-REPORTS-CONTRACT.md §2–§3 (income positive on the P&L). tests/stream-accounting-golden3.mjs and
// 08 Production/tests/Hcc.Accounting.Tests/Golden/rulebook-reports.json carry the same figures. Expectations only: no code reads them but the tests.
// The pay run is locked on 1 May in the prototype scenario (a month is paid once complete), so every "April" report is read to 1 May (nothing else posts on 1 May).
const RP=(r:number)=>Math.round(r*100);
export const ACC_RB_REPORTS={
  from:'2026-04-01',to:'2026-04-30',readTo:'2026-05-01',fy:'2026-27',
  /** §8.4 profit & loss. Lines by ledger code (revenue and discounts signed: income positive). */
  pl:{
    revenue:{lines:{'4000':RP(430000),'4200':RP(160000),'4900':-RP(10000)} as Record<string,number>,total:RP(580000)},
    otherIncome:{lines:{} as Record<string,number>,total:0},
    consumption:{lines:{'5000':RP(59000),'5010':RP(42000)} as Record<string,number>,total:RP(101000)},
    employee:{lines:{'5100':RP(240000),'5110':RP(24500)} as Record<string,number>,total:RP(264500)},
    finance:{lines:{'5080':RP(2360),'5090':RP(15000)} as Record<string,number>,total:RP(17360)},
    depreciation:{lines:{} as Record<string,number>,total:0},   // depreciation is a year-end entry (§8.7)
    other:{lines:{'5030':RP(45000),'5040':RP(8000),'5070':RP(3540),'5900':RP(200)} as Record<string,number>,total:RP(56740)},
    expensesTotal:RP(439600),profitBeforeTax:RP(140400),
  },
  /** §8.5 balance sheet at 30 April (fixed assets at WDV: depreciation is a year-end entry). Block totals follow contract §2 (Duties & Taxes in current liabilities). */
  balanceSheet:{
    ownersFunds:{total:RP(5795400),lines:{'3100':RP(5705000),'3000':-RP(50000)} as Record<string,number>,profit:RP(140400)},
    nonCurrentLiabilities:{total:RP(1960000),lines:{'2400':RP(1960000)} as Record<string,number>},
    currentLiabilities:{total:RP(172500),lines:{'2200':RP(66000),'2000':RP(31500),'2110':RP(14500),'2120':RP(14500),'2310':RP(36000),'2320':RP(8000),'2330':RP(2000)} as Record<string,number>},
    liabilitiesTotal:RP(7927900),
    nonCurrentAssets:{total:RP(7380000),lines:{'1500':RP(6000000),'1510':RP(800000),'1520':RP(500000),'1530':RP(80000)} as Record<string,number>},
    currentAssets:{total:RP(547900),lines:{'1000':RP(40300),'1030':RP(501500),'1360':RP(6000),'1340':RP(100)} as Record<string,number>},
    assetsTotal:RP(7927900),suspense:0,openingDifference:0,
  },
  /** §7.6 ageing. Deccan Agro's April bill (63,000 = 60,000 + GST 3,000, dated 6 April) is paid in full on 30 April (T7b), so at 30 April nothing is receivable:
   *  the ₹60,000 + GST in 0–30 of the brief is the position on 28 April (the Goibibo folio of 21,000, dated 17 April, is then also open). */
  ageing:{
    receivable30Apr:{rows:{} as Record<string,{open:number}>,open:0},
    receivable28Apr:{rows:{'1110':{party:'Deccan Agro Ltd',open:RP(63000),b0_30:RP(63000),days:22,ref:'bill'},'1115':{party:'Goibibo',open:RP(21000),b0_30:RP(21000),days:11}} as Record<string,any>,open:RP(84000),b0_30:RP(84000)},
    /** Payables at 30 April: Morning 24,000 (the 5 April bill, 35,000 paid after the 25,000 opening: oldest first), Kaveri 42,000 (12 April). */
    payable30Apr:{rows:{'EV-MORNING':{name:'Morning Supplies',open:RP(24000),days:25},'EV-KAVERI':{name:'Kaveri Textiles',open:RP(42000),days:18}} as Record<string,any>,open:RP(66000),b0_30:RP(66000)},
    /** The same open bills later: bucket by days since the bill date (5 and 12 April). */
    payableLater:[
      {asOf:'2026-05-31',morning:{days:56,bucket:'b31_60'},kaveri:{days:49,bucket:'b31_60'}},
      {asOf:'2026-06-30',morning:{days:86,bucket:'b61_90'},kaveri:{days:79,bucket:'b61_90'}},
      {asOf:'2026-07-15',morning:{days:101,bucket:'b90'},kaveri:{days:94,bucket:'b90'}},
    ],
  },
  /** §8.6 GST for April. Taxable: rooms 4,00,000 (T1) + 20,000 (T4) + restaurant 1,00,000 (T2) + 60,000 (T3) = 5,80,000, all at 5 %; B2B = Deccan Agro 60,000. */
  gst:{month:'2026-04',output:{cgst:RP(14500),sgst:RP(14500),igst:0,total:RP(29000)},rate5Taxable:RP(580000),rate18Taxable:0,b2bTaxable:RP(60000),b2csTaxable:RP(520000),
    advances:0,creditNotes:0,itc:{cgst:0,sgst:0,igst:0},rcm:0,tcsCredit:RP(100),cashLedgerBalance:RP(100),challan:RP(28900),payable:RP(29000)},
  /** §7.9 cash ratio (classification stated in the DONE file): cash receipts = Receipt vouchers' cash legs on 1000 (T3 31,500 + T5c 1,52,000 = 1,83,500) of all Receipt
   *  vouchers (T3 63,000 · T5c 4,52,000 · T6 31,500 · T7a 40,000 · T7b 63,000 · T19 21,000 = 6,70,500); cash payments = 13,000 (T12 8,000 + T13a 5,000) of all Payment vouchers
   *  (T10 45,000 · T11 60,000 · T12 8,000 · T13a 5,000 · T13c 2,13,500 · T16 50,000 · T17 55,000 · T18 590 = 4,37,090). Percent to two decimals; bps = percent × 100. */
  cashRatio:{cashReceipts:RP(183500),totalReceipts:RP(670500),cashPayments:RP(13000),totalPayments:RP(437090),receiptsPct:27.37,paymentsPct:2.97,flagged:true},
};
