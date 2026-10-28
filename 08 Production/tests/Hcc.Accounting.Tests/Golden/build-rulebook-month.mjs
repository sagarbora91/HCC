// Writes Golden/rulebook-month.json: the rulebook golden month (02 Planning/HCC_Accounting_Rulebook_v1.md §8, April 2026, fictional), the WHOLE of §8.2 (ACC-2),
// as the server's events plus the expectations worked out from the rulebook (shared with the prototype: 03 Prototype/tests/_rulebook-month.mjs).
// 19 steps have events today (existing event types; ACC-2 adds kinds / fields: receipt kinds `ota-credit` `deposit-apply` `deposit`, `tdsPaise` on a company payment,
// charge kind `food`, per-staff `advanceRecoveries` on the pay run); 7 steps (T11 T14a T14b T16 T17 T18 T19) wait for a server command / event of an ACC-2 lane and are
// listed in `waiting` with the intended event payloads in `acc2Events` (data only: the C# tests skip them with a reason).
// Run: node "08 Production/tests/Hcc.Accounting.Tests/Golden/build-rulebook-month.mjs"   (no build needed; money in paise)
import {writeFileSync} from 'node:fs';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {dirname,resolve} from 'node:path';
const here=dirname(fileURLToPath(import.meta.url));
const {R,OPENING,STEP,ALL,SERVER_EVENTS,SERVER_WAITING,RULEBOOK_TB_83,PNL_84,BS_85,GST_86,control,model}=await import(pathToFileURL(resolve(here,'../../../../03 Prototype/tests/_rulebook-month.mjs')).href);

// Rulebook party name → party id of the posting contract (§3): bank account row code, vendor row code, company id, OTA channel id, staff id, till code.
const PARTY={HDFC:'BA-HDFC','HDFC-loan':'LN-HDFC',Morning:'EV-MORNING',Kaveri:'EV-KAVERI',Deccan:'CMP-0007',Goibibo:'CHN-GOIBIBO','Staff-07':'STF-007'};
const partyOf=a=>{const [c,p]=a.split('/');return p?`${c}/${PARTY[p]}`:a;};
const line=x=>({account:control(x.a),party:x.a.includes('/')?PARTY[x.a.split('/')[1]]:null,ledger:partyOf(x.a),debitPaise:x.dr,creditPaise:x.cr});

// ── the events the server consumes (contract: Hcc.Contracts.Accounting), one per rulebook step, dated inside April 2026 ──
// T1/T2 are aggregates of the month's nights / restaurant lines (one charge each); the prototype posts them night by night.
const E=(step,date,type,payload)=>({step,date,type,payload});
const events=[
  E('T1','2026-04-04','ChargePosted',{folioId:1,chargeId:101,kind:'room',serviceDate:'2026-04-04',amountPaise:R(410000),taxPaise:R(20500)}),
  E('T2','2026-04-09','ChargePosted',{folioId:1,chargeId:102,kind:'food',serviceDate:'2026-04-09',amountPaise:R(100000),taxPaise:R(5000)}),
  E('T3','2026-04-12','PosBillSettled',{billId:201,kind:'sale',businessDate:'2026-04-12',outlet:'restaurant',tender:'Cash',totalPaise:R(31500),netPaise:R(30000),cgstPaise:R(750),sgstPaise:R(750),reference:'Outlet cash',mode:'outlet'}),
  E('T3','2026-04-13','PosBillSettled',{billId:202,kind:'sale',businessDate:'2026-04-13',outlet:'restaurant',tender:'UPI',totalPaise:R(31500),netPaise:R(30000),cgstPaise:R(750),sgstPaise:R(750),reference:'DEMO-UPI',mode:'outlet'}),
  // T4 (A13): ten nights of an OTA stay (₹20,000 + 5 % GST) invoiced to the guest; at check-out the folio moves to the OTA receivable (receipt kind ota-credit, flows lane)
  E('T4','2026-04-16','ChargePosted',{folioId:2,chargeId:111,kind:'room',serviceDate:'2026-04-16',amountPaise:R(20000),taxPaise:R(1000)}),
  E('T4','2026-04-17','ReceiptRecorded',{folioId:2,receiptId:310,kind:'ota-credit',tender:'OTA credit',amountPaise:R(21000),businessDate:'2026-04-17'}),
  // T5a (A6a): the opening advance applied at check-out (DEPOSIT_APPLY → receipt kind deposit-apply; flows lane)
  E('T5a','2026-04-06','ReceiptRecorded',{folioId:1,receiptId:301,kind:'deposit-apply',tender:'',amountPaise:R(10000),businessDate:'2026-04-06'}),
  E('T5b','2026-04-06','ReceiptRecorded',{folioId:1,receiptId:302,kind:'company-credit',tender:'Company credit',amountPaise:R(63000),businessDate:'2026-04-06'}),
  E('T5c','2026-04-11','ReceiptRecorded',{folioId:1,receiptId:303,kind:'payment',tender:'Cash',amountPaise:R(152000),businessDate:'2026-04-11'}),
  E('T5c','2026-04-11','ReceiptRecorded',{folioId:1,receiptId:304,kind:'payment',tender:'UPI',amountPaise:R(150000),businessDate:'2026-04-11'}),
  E('T5c','2026-04-11','ReceiptRecorded',{folioId:1,receiptId:305,kind:'payment',tender:'Card',amountPaise:R(150000),businessDate:'2026-04-11'}),
  E('T1','2026-04-11','ChargePosted',{folioId:1,chargeId:103,kind:'discount',serviceDate:'2026-04-11',amountPaise:-R(10000),taxPaise:-R(500)}),
  // T6 (A6): the May advance by UPI, GST switch off (DEC-142): Dr 1010 · Cr 2000
  E('T6','2026-04-20','ReceiptRecorded',{folioId:3,receiptId:320,kind:'deposit',tender:'UPI',amountPaise:R(31500),businessDate:'2026-04-20'}),
  E('T7a','2026-04-10','CompanyPaymentRecorded',{companyId:7,paymentId:401,businessDate:'2026-04-10',tender:'Bank transfer',amountPaise:R(40000),reference:'NEFT-DEMO-0410',company:'Deccan Agro Ltd'}),
  // T7b (A9): Deccan Agro pays the April bill net of TDS 10 % on ₹60,000 — amount is the money received, tdsPaise the TDS held back (tds lane): Dr tender · Dr 1360 · Cr 1110
  E('T7b','2026-04-30','CompanyPaymentRecorded',{companyId:7,paymentId:402,businessDate:'2026-04-30',tender:'Bank transfer',amountPaise:R(57000),tdsPaise:R(6000),reference:'NEFT-DEMO-0430',company:'Deccan Agro Ltd'}),
  E('T8','2026-04-05','ExpensePosted',{expenseId:501,businessDate:'2026-04-05',category:'Supplies',funding:'Vendor credit',amountPaise:R(59000),vendor:'Morning Supplies',reversalOf:null,gstPaise:R(9000),itc:'disallowed'}),
  E('T9','2026-04-12','ExpensePosted',{expenseId:502,businessDate:'2026-04-12',category:'Laundry',funding:'Vendor credit',amountPaise:R(42000),vendor:'Kaveri Textiles',reversalOf:null,gstPaise:R(2000),itc:'disallowed'}),
  E('T10','2026-04-15','ExpensePosted',{expenseId:503,businessDate:'2026-04-15',category:'Utilities',funding:'Bank transfer',amountPaise:R(45000),vendor:'State Electricity Board (fictional)',reversalOf:null}),
  E('T12','2026-04-14','ExpensePosted',{expenseId:504,businessDate:'2026-04-14',category:'Petty cash',funding:'Cash',amountPaise:R(8000),vendor:'',reversalOf:null}),
  E('T13a','2026-04-08','AdvanceGiven',{advanceId:601,staffId:7,amountPaise:R(5000),installmentPaise:R(5000),date:'2026-04-08',funding:'Cash'}),
  E('T13b','2026-04-30','PayrollPosted',{payRunId:701,period:'2026-04',businessDate:'2026-04-30',grossPaise:R(240000),employerPfPaise:R(18000),employerEsicPaise:R(6500),employeePfPaise:R(18000),
    employeeEsicPaise:R(1500),professionalTaxPaise:R(2000),advanceRecoveryPaise:R(5000),advanceRecoveries:[{staffId:7,paise:R(5000)}],otherDeductionsPaise:0,netPayablePaise:R(213500),lockSeq:1}),
  E('T13c','2026-04-30','PayrollPaid',{payRunId:701,businessDate:'2026-04-30',funding:'Bank transfer',amountPaise:R(213500),reference:'NEFT-DEMO-PAY',lockSeq:1}),
  E('T15a','2026-04-25','CashBanked',{transferId:801,tillId:1,shiftId:1,businessDate:'2026-04-25',amountPaise:R(150000),bank:'HDFC',slipNo:'SLIP-20260425'}),
  E('T15b','2026-04-26','ShiftDifferenceDecided',{shiftId:2,approvalId:901,status:'Approved',businessDate:'2026-04-26',expectedPaise:R(50000),countedPaise:R(49800)}),
].sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:0);

const closing=(set,withOpening=true)=>{const m=model(set,withOpening);return {control:m.ctl,party:Object.fromEntries(Object.entries(m.full).filter(([k])=>k.includes('/')).map(([k,v])=>[partyOf(k),v]))};};

// ── the steps that wait for a server command / event of an ACC-2 lane: the intended payloads (data only; the C# tests skip them with a reason) ──
const acc2Events=[
  {step:'T11',lane:'acc2-purchase',date:'2026-04-18',type:'VendorPaymentRecorded',reason:'exp.vendor_payment + SourceRules.VendorPayment (migrations 4230–4237)',
   payload:{paymentId:901,vendor:'Morning Supplies',vendorCode:'EV-MORNING',businessDate:'2026-04-18',tender:'Bank transfer',amountPaise:R(60000),reference:'NEFT-DEMO-0418',
     allocations:[{expenseId:null,opening:true,amountPaise:R(25000)},{expenseId:501,amountPaise:R(35000)}]}},
  {step:'T14a',lane:'acc2-manual',date:'2026-04-27',type:'ManualVoucherApproved',reason:'acc.manual_voucher + SourceRules.Manual (migrations 4238–4241)',
   payload:{voucherId:1,businessDate:'2026-04-27',voucherType:'Contra',template:'contra',narration:'UPI settlements to HDFC',lines:STEP.T14a.map(line)}},
  {step:'T14b',lane:'acc2-manual',date:'2026-04-27',type:'ManualVoucherApproved',reason:'as T14a (a Journal: the MDR line is not a cash / bank ledger)',
   payload:{voucherId:2,businessDate:'2026-04-27',voucherType:'Journal',template:'journal',narration:'Card settlement less MDR and GST',lines:STEP.T14b.map(line)}},
  {step:'T16',lane:'acc2-manual',date:'2026-04-28',type:'ManualVoucherApproved',reason:'as T14a',
   payload:{voucherId:3,businessDate:'2026-04-28',voucherType:'Payment',template:'owner-drawing-bank',narration:'Owner drawing from the bank',lines:STEP.T16.map(line)}},
  {step:'T17',lane:'acc2-manual',date:'2026-04-05',type:'ManualVoucherApproved',reason:'as T14a; the loan is a party row 2400/LN-HDFC',
   payload:{voucherId:4,businessDate:'2026-04-05',voucherType:'Payment',template:'loan-emi',narration:'HDFC term loan EMI',lines:STEP.T17.map(line)}},
  {step:'T18',lane:'acc2-manual',date:'2026-04-30',type:'ManualVoucherApproved',reason:'as T14a',
   payload:{voucherId:5,businessDate:'2026-04-30',voucherType:'Payment',template:'bank-charges',narration:'Bank charges 500 + GST 90',lines:STEP.T18.map(line)}},
  {step:'T19',lane:'acc2-flows',date:'2026-04-29',type:'OtaPayoutRecorded',reason:'ota.payout + SourceRules.OtaPayout (migrations 4250–4254)',
   payload:{payoutId:1,channel:'goibibo',businessDate:'2026-04-29',statementRef:'GOI-STMT-2026-04-A',grossPaise:R(21000),commissionPaise:R(3000),commissionGstPaise:R(540),tcsPaise:R(100),netPaise:R(17360),bank:'HDFC'}},
];
for(const w of SERVER_WAITING)if(!acc2Events.some(e=>e.step===w))throw new Error('no intended event for '+w);

// every driven step has its event(s); the sum of a step's events must reproduce the step's entries (the C# test checks this through the real mapper)
const json={
  generatedBy:'08 Production/tests/Hcc.Accounting.Tests/Golden/build-rulebook-month.mjs (lanes acc1-tests, acc2-tests); expectations are worked out from rulebook §8, never exported from the code under test',
  source:'02 Planning/HCC_Accounting_Rulebook_v1.md §8 (April 2026)',
  fictionalDataOnly:true,
  asOf:'2026-04-01',
  // The Owner's mapping for this month: none. ACC-2: a folio restaurant charge is a `food` line (default line:food → 4200, parity lane) and the opening advance is applied by
  // a receipt of kind deposit-apply (flows lane), so the ACC-1 overrides (line:service → 4200, method:Advance adjusted → 2000) are gone.
  mappingOverrides:{},
  parties:PARTY,
  opening:OPENING.map(line),
  steps:Object.fromEntries(ALL.map(k=>[k,STEP[k].map(line)])),
  driven:SERVER_EVENTS,
  // steps with no server command / event yet (ACC-2 lanes) and the intended payloads
  waiting:{steps:SERVER_WAITING,events:acc2Events},
  events,
  expected:{
    // opening + the steps of `driven`, by control ledger and by party ledger (`code/partyId`), signed paise, Dr positive
    closing:closing(SERVER_EVENTS),
    // the same without the opening balances: what the event consumer alone produces (the SQL test; the opening arrives through the opening endpoint)
    flow:closing(SERVER_EVENTS,false),
    // the whole month (§8.3): the target once every waiting step exists
    full:closing(ALL),
    fullMonthControl:model(ALL).ctl,
    rulebookTrialBalance83:Object.fromEntries(Object.entries(RULEBOOK_TB_83).map(([k,v])=>[k,R(v)])),
    // §8.4 profit and loss (P&L ledgers only), §8.5 balance-sheet totals, §8.6 GST for April
    profitAndLoss84:PNL_84,balanceSheet85:{owners:BS_85.owners,loan:BS_85.loan,creditors:BS_85.creditors,duties:BS_85.duties,advances:BS_85.advances,total:BS_85.total},
    gst86:GST_86,
  },
};
const out=resolve(here,'rulebook-month.json');
writeFileSync(out,JSON.stringify(json,null,1)+String.fromCharCode(10));
console.log('wrote',out,'steps',ALL.length,'driven',SERVER_EVENTS.length,'waiting',SERVER_WAITING.length,'events',events.length);
