// ACC-1 acc1-tests · ACC-2 acc2-tests · the rulebook golden month (02 Planning/HCC_Accounting_Rulebook_v1.md §8, April 2026, fictional) as DATA.
// One source for both suites: tests/stream-accounting-rulebook.mjs (prototype) and the generator of the server's
// Golden/rulebook-month.json (08 Production/tests/Hcc.Accounting.Tests/Golden/build-rulebook-month.mjs). Not a test file (leading underscore).
// Amounts in paise. Accounts: a ledger code, or `code/Party` with the party named as the rulebook names it
// (HDFC = the bank account, HDFC-loan = the HDFC term loan under 2400, Deccan = Deccan Agro Ltd, Morning / Kaveri = the vendors, Goibibo = the OTA,
// Staff-07 = the staff member). ACC-2: the whole of §8.2 is driven in the prototype (DRIVEN = ALL); the server books SERVER_EVENTS from events today and
// waits for SERVER_WAITING (a server command / event that the ACC-2 lanes add).
export const R=r=>Math.round(r*100);
const e=(a,dr,cr=0)=>({a,dr:R(dr),cr:R(cr)});

/** §8.1 opening balances, 1 April 2026. */
export const OPENING=[e('1000',20000),e('1030/HDFC',300000),e('1500',6000000),e('1510',800000),e('1520',500000),e('1530',80000),e('1110/Deccan',40000),
  e('2200/Morning',0,25000),e('2000',0,10000),e('2400/HDFC-loan',0,2000000),e('3100',0,5705000)];

/** §8.2 transactions T1…T19, entry by entry (the rulebook's own lines; T13b splits employer PF / ESIC on two 5110 lines). */
export const STEP={
  T1:[e('1100',420000),e('4900',10000),e('4000',0,410000),e('2110',0,10000),e('2120',0,10000)],
  T2:[e('1100',105000),e('4200',0,100000),e('2110',0,2500),e('2120',0,2500)],
  T3:[e('1000',31500),e('1010',31500),e('4200',0,60000),e('2110',0,1500),e('2120',0,1500)],
  T4:[e('1115/Goibibo',21000),e('4000',0,20000),e('2110',0,500),e('2120',0,500)],
  T5a:[e('2000',10000),e('1100',0,10000)],
  T5b:[e('1110/Deccan',63000),e('1100',0,63000)],
  T5c:[e('1000',152000),e('1010',150000),e('1020',150000),e('1100',0,452000)],
  T6:[e('1010',31500),e('2000',0,31500)],
  T7a:[e('1030/HDFC',40000),e('1110/Deccan',0,40000)],
  T7b:[e('1030/HDFC',57000),e('1360',6000),e('1110/Deccan',0,63000)],
  T8:[e('5000',59000),e('2200/Morning',0,59000)],
  T9:[e('5010',42000),e('2200/Kaveri',0,42000)],
  T10:[e('5030',45000),e('1030/HDFC',0,45000)],
  T11:[e('2200/Morning',60000),e('1030/HDFC',0,60000)],
  T12:[e('5040',8000),e('1000',0,8000)],
  T13a:[e('1200/Staff-07',5000),e('1000',0,5000)],
  T13b:[e('5100',240000),e('5110',18000),e('5110',6500),e('2300',0,213500),e('2310',0,36000),e('2320',0,8000),e('2330',0,2000),e('1200/Staff-07',0,5000)],
  T13c:[e('2300',213500),e('1030/HDFC',0,213500)],
  T14a:[e('1030/HDFC',213000),e('1010',0,213000)],
  T14b:[e('1030/HDFC',148230),e('5080',1770),e('1020',0,150000)],
  T15a:[e('1030/HDFC',150000),e('1000',0,150000)],
  T15b:[e('5900',200),e('1000',0,200)],
  T16:[e('3000',50000),e('1030/HDFC',0,50000)],
  T17:[e('2400/HDFC-loan',40000),e('5090',15000),e('1030/HDFC',0,55000)],
  T18:[e('5080',590),e('1030/HDFC',0,590)],
  T19:[e('1030/HDFC',17360),e('5070',3540),e('1340',100),e('1115/Goibibo',0,21000)],
};
export const ALL=Object.keys(STEP);
/** ACC-2: the PROTOTYPE drives the whole month through real commands (scenario ACC-RULEBOOK): all 26 steps. */
export const DRIVEN=[...ALL];
/** The server books these from events/commands of the lanes' contract (ChargePosted, ReceiptRecorded incl. kinds `ota-credit`, `deposit-apply`, `deposit`,
 *  CompanyPaymentRecorded with `tdsPaise`, ExpensePosted, AdvanceGiven, PayrollPosted, PayrollPaid, CashBanked, ShiftDifferenceDecided). */
export const SERVER_EVENTS=ALL.filter(k=>!['T11','T14a','T14b','T16','T17','T18','T19'].includes(k));
/** Steps whose server command / event does not exist yet (ACC-2 lanes): T11 vendor payment (acc2-purchase, `VendorPaymentRecorded`), T14a/b T16 T17 T18
 *  manual vouchers (acc2-manual, `ManualVoucherApproved`), T19 OTA payout (acc2-flows, `OtaPayoutRecorded`). The C# tests skip them with this reason. */
export const SERVER_WAITING=ALL.filter(k=>!SERVER_EVENTS.includes(k));
/** ACC-1 history (kept for the self-checks): what ACC-1 drove in the prototype / booked on the server. */
export const ACC1_DRIVEN=['T1','T2','T3','T5a','T5b','T5c','T7a','T8','T9','T10','T12','T15a','T15b'];
export const ACC1_SERVER=[...ACC1_DRIVEN,'T13a','T13b','T13c'];
/** §8.3 closing trial balance, literally (control level; rupees; Dr positive, Cr negative). */
export const RULEBOOK_TB_83={'1000':40300,'1030':501500,'1360':6000,'1340':100,'1500':6000000,'1510':800000,'1520':500000,'1530':80000,'3000':50000,'4900':10000,
  '5000':59000,'5010':42000,'5030':45000,'5040':8000,'5100':240000,'5110':24500,'5080':2360,'5900':200,'5090':15000,'5070':3540,
  '2200':-66000,'2000':-31500,'2110':-14500,'2120':-14500,'2310':-36000,'2320':-8000,'2330':-2000,'2400':-1960000,'3100':-5705000,'4000':-430000,'4200':-160000};

export const control=a=>a.split('/')[0];
/** Balances (paise, Dr positive) of the opening plus the given steps: by full account (`code/Party`) and by control code; zero balances left out. */
export function model(steps,withOpening=true){
  const full={},ctl={};
  for(const x of [...(withOpening?OPENING:[]),...steps.flatMap(k=>STEP[k])]){
    full[x.a]=(full[x.a]??0)+x.dr-x.cr;ctl[control(x.a)]=(ctl[control(x.a)]??0)+x.dr-x.cr;
  }
  const strip=o=>Object.fromEntries(Object.entries(o).filter(([,v])=>v!==0));
  return {full:strip(full),ctl:strip(ctl)};
}
export const sum=(xs,k)=>xs.reduce((a,x)=>a+x[k],0);

/** §8.4 profit & loss, April 2026 (paise): the P&L ledgers only (4xxx and 5xxx). Revenue from operations 5,80,000 … profit 1,40,400. */
export const PNL_84={revenue:R(580000),consumption:R(101000),employee:R(264500),finance:R(17360),other:R(56740),profit:R(140400)};
/** §8.5 balance sheet totals, 30 April 2026 (paise): both sides ₹79,27,900 (the six-column TB check: closing Dr/Cr of the balance-sheet ledgers + profit). */
export const BS_85={owners:R(5795400),loan:R(1960000),creditors:R(66000),duties:R(75000),advances:R(31500),total:R(7927900),
  assets:{fixed:R(7380000),cash:R(40300),bank:R(501500),tds:R(6000),gstCash:R(100)}};
/** §8.6 GST for April: output 14,500 + 14,500, ITC nil, TCS credit 100 → challan 28,900 (paid in May). */
export const GST_86={cgst:R(14500),sgst:R(14500),igst:0,itc:0,tcs:R(100),challan:R(28900),cashLedgerAfterSetoff:R(29000)};
