// Writes Golden/rulebook-month.json: the rulebook golden month (02 Planning/HCC_Accounting_Rulebook_v1.md §8, April 2026, fictional), ACC-1 subset,
// as the server's events plus the expectations worked out from the rulebook (shared with the prototype: 03 Prototype/tests/_rulebook-month.mjs).
// Run: node "08 Production/tests/Hcc.Accounting.Tests/Golden/build-rulebook-month.mjs"   (no build needed; money in paise)
import {writeFileSync} from 'node:fs';
import {fileURLToPath,pathToFileURL} from 'node:url';
import {dirname,resolve} from 'node:path';
const here=dirname(fileURLToPath(import.meta.url));
const {R,OPENING,STEP,ALL,DRIVEN,SERVER,BRIEF,ACC2,RULEBOOK_TB_83,control,model}=await import(pathToFileURL(resolve(here,'../../../../03 Prototype/tests/_rulebook-month.mjs')).href);

// Rulebook party name → party id of the posting contract (§3): bank account row code, vendor row code, company id, OTA channel id, staff id, till code.
const PARTY={HDFC:'BA-HDFC',Morning:'EV-MORNING',Kaveri:'EV-KAVERI',Deccan:'CMP-0007',Goibibo:'CHN-GOIBIBO','Staff-07':'STF-007'};
const partyOf=a=>{const [c,p]=a.split('/');return p?`${c}/${PARTY[p]}`:a;};
const line=x=>({account:control(x.a),party:x.a.includes('/')?PARTY[x.a.split('/')[1]]:null,ledger:partyOf(x.a),debitPaise:x.dr,creditPaise:x.cr});

// ── the events the server consumes (contract: Hcc.Contracts.Accounting), one per rulebook step, dated inside April 2026 ──
// T1/T2 are aggregates of the month's nights / restaurant lines (one charge each); the prototype posts them night by night.
const E=(step,date,type,payload)=>({step,date,type,payload});
const events=[
  E('T1','2026-04-04','ChargePosted',{folioId:1,chargeId:101,kind:'room',serviceDate:'2026-04-04',amountPaise:R(410000),taxPaise:R(20500)}),
  E('T2','2026-04-09','ChargePosted',{folioId:1,chargeId:102,kind:'service',serviceDate:'2026-04-09',amountPaise:R(100000),taxPaise:R(5000)}),
  E('T3','2026-04-12','PosBillSettled',{billId:201,kind:'sale',businessDate:'2026-04-12',outlet:'restaurant',tender:'Cash',totalPaise:R(31500),netPaise:R(30000),cgstPaise:R(750),sgstPaise:R(750),reference:'Outlet cash',mode:'outlet'}),
  E('T3','2026-04-13','PosBillSettled',{billId:202,kind:'sale',businessDate:'2026-04-13',outlet:'restaurant',tender:'UPI',totalPaise:R(31500),netPaise:R(30000),cgstPaise:R(750),sgstPaise:R(750),reference:'DEMO-UPI',mode:'outlet'}),
  E('T5a','2026-04-06','ReceiptRecorded',{folioId:1,receiptId:301,kind:'payment',tender:'Advance adjusted',amountPaise:R(10000),businessDate:'2026-04-06'}),
  E('T5b','2026-04-06','ReceiptRecorded',{folioId:1,receiptId:302,kind:'company-credit',tender:'Company credit',amountPaise:R(63000),businessDate:'2026-04-06'}),
  E('T5c','2026-04-11','ReceiptRecorded',{folioId:1,receiptId:303,kind:'payment',tender:'Cash',amountPaise:R(152000),businessDate:'2026-04-11'}),
  E('T5c','2026-04-11','ReceiptRecorded',{folioId:1,receiptId:304,kind:'payment',tender:'UPI',amountPaise:R(150000),businessDate:'2026-04-11'}),
  E('T5c','2026-04-11','ReceiptRecorded',{folioId:1,receiptId:305,kind:'payment',tender:'Card',amountPaise:R(150000),businessDate:'2026-04-11'}),
  E('T1','2026-04-11','ChargePosted',{folioId:1,chargeId:103,kind:'discount',serviceDate:'2026-04-11',amountPaise:-R(10000),taxPaise:-R(500)}),
  E('T7a','2026-04-10','CompanyPaymentRecorded',{companyId:7,paymentId:401,businessDate:'2026-04-10',tender:'Bank transfer',amountPaise:R(40000),reference:'NEFT-DEMO-0410',company:'Deccan Agro Ltd'}),
  E('T8','2026-04-05','ExpensePosted',{expenseId:501,businessDate:'2026-04-05',category:'Supplies',funding:'Vendor credit',amountPaise:R(59000),vendor:'Morning Supplies',reversalOf:null,gstPaise:R(9000),itc:'disallowed'}),
  E('T9','2026-04-12','ExpensePosted',{expenseId:502,businessDate:'2026-04-12',category:'Laundry',funding:'Vendor credit',amountPaise:R(42000),vendor:'Kaveri Textiles',reversalOf:null,gstPaise:R(2000),itc:'disallowed'}),
  E('T10','2026-04-15','ExpensePosted',{expenseId:503,businessDate:'2026-04-15',category:'Utilities',funding:'Bank transfer',amountPaise:R(45000),vendor:'State Electricity Board (fictional)',reversalOf:null}),
  E('T12','2026-04-14','ExpensePosted',{expenseId:504,businessDate:'2026-04-14',category:'Petty cash',funding:'Cash',amountPaise:R(8000),vendor:'',reversalOf:null}),
  E('T13a','2026-04-08','AdvanceGiven',{advanceId:601,staffId:7,amountPaise:R(5000),installmentPaise:R(5000),date:'2026-04-08',funding:'Cash'}),
  E('T13b','2026-04-30','PayrollPosted',{payRunId:701,period:'2026-04',businessDate:'2026-04-30',grossPaise:R(240000),employerPfPaise:R(18000),employerEsicPaise:R(6500),employeePfPaise:R(18000),
    employeeEsicPaise:R(1500),professionalTaxPaise:R(2000),advanceRecoveryPaise:R(5000),otherDeductionsPaise:0,netPayablePaise:R(213500),lockSeq:1}),
  E('T13c','2026-04-30','PayrollPaid',{payRunId:701,businessDate:'2026-04-30',funding:'Bank transfer',amountPaise:R(213500),reference:'NEFT-DEMO-PAY',lockSeq:1}),
  E('T15a','2026-04-25','CashBanked',{transferId:801,tillId:1,shiftId:1,businessDate:'2026-04-25',amountPaise:R(150000),bank:'HDFC',slipNo:'SLIP-20260425'}),
  E('T15b','2026-04-26','ShiftDifferenceDecided',{shiftId:2,approvalId:901,status:'Approved',businessDate:'2026-04-26',expectedPaise:R(50000),countedPaise:R(49800)}),
].sort((a,b)=>a.date<b.date?-1:a.date>b.date?1:0);

const closing=(set,withOpening=true)=>{const m=model(set,withOpening);return {control:m.ctl,party:Object.fromEntries(Object.entries(m.full).filter(([k])=>k.includes('/')).map(([k,v])=>[partyOf(k),v]))};};
// every driven step has its event(s); the sum of a step's events must reproduce the step's entries (the C# test checks this through the real mapper)
const json={
  generatedBy:'08 Production/tests/Hcc.Accounting.Tests/Golden/build-rulebook-month.mjs (lane acc1-tests); expectations are worked out from rulebook §8, never exported from the code under test',
  source:'02 Planning/HCC_Accounting_Rulebook_v1.md §8 (April 2026)',
  fictionalDataOnly:true,
  asOf:'2026-04-01',
  // The Owner's mapping for this month: a folio restaurant charge is a `service` line → F&B revenue (rulebook A3); an advance held since before HCC applied
  // to a folio is a receipt on the tender "Advance adjusted" mapped to 2000 (T5a; there is no apply-advance command).
  mappingOverrides:{'line:service':'4200','method:Advance adjusted':'2000'},
  parties:PARTY,
  opening:OPENING.map(line),
  steps:Object.fromEntries(ALL.map(k=>[k,STEP[k].map(line)])),
  driven:SERVER,
  // not in the server events: T16 (an Owner drawing from the BANK is rulebook B6, a manual voucher — not an ACC-1 command), and ACC-2:
  notDriven:{t16:['T16'],acc2:ACC2},
  events,
  expected:{
    // opening + the steps of `driven`, by control ledger and by party ledger (`code/partyId`), signed paise, Dr positive
    closing:closing(SERVER),
    // the same without the opening balances: what the event consumer alone produces (the SQL test; the opening arrives through the opening endpoint)
    flow:closing(SERVER,false),
    // the brief's subset (adds T16, the drawing from the bank) and the whole month (§8.3): the ACC-2 target
    briefSubset:closing(BRIEF),
    fullMonthControl:model(ALL).ctl,
    rulebookTrialBalance83:Object.fromEntries(Object.entries(RULEBOOK_TB_83).map(([k,v])=>[k,R(v)])),
  },
};
const out=resolve(here,'rulebook-month.json');
writeFileSync(out,JSON.stringify(json,null,1)+'\n');
console.log('wrote',out,'steps',ALL.length,'events',events.length);
