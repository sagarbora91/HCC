// ACC-1 acc1-tests · the rulebook golden month (02 Planning/HCC_Accounting_Rulebook_v1.md §8, April 2026, fictional) as DATA.
// One source for both suites: tests/stream-accounting-rulebook.mjs (prototype) and the generator of the server's
// Golden/rulebook-month.json (08 Production/tests/Hcc.Accounting.Tests/Golden/build-rulebook-month.mjs). Not a test file (leading underscore).
// Amounts in paise. Accounts: a ledger code, or `code/Party` with the party named as the rulebook names it
// (HDFC = the bank account, Deccan = Deccan Agro Ltd, Morning / Kaveri = the vendors, Goibibo = the OTA, Staff-07 = the staff member).
export const R=r=>Math.round(r*100);
const e=(a,dr,cr=0)=>({a,dr:R(dr),cr:R(cr)});

/** §8.1 opening balances, 1 April 2026. */
export const OPENING=[e('1000',20000),e('1030/HDFC',300000),e('1500',6000000),e('1510',800000),e('1520',500000),e('1530',80000),e('1110/Deccan',40000),
  e('2200/Morning',0,25000),e('2000',0,10000),e('2400',0,2000000),e('3100',0,5705000)];

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
  T17:[e('2400',40000),e('5090',15000),e('1030/HDFC',0,55000)],
  T18:[e('5080',590),e('1030/HDFC',0,590)],
  T19:[e('1030/HDFC',17360),e('5070',3540),e('1340',100),e('1115/Goibibo',0,21000)],
};
export const ALL=Object.keys(STEP);
/** Steps the PROTOTYPE drives and books in ACC-1 (scenario ACC-RULEBOOK). */
export const DRIVEN=['T1','T2','T3','T5a','T5b','T5c','T7a','T8','T9','T10','T12','T15a','T15b'];
/** Steps the SERVER books in ACC-1 from events: DRIVEN plus the salary advance and the pay run (the prototype posts no payroll voucher). */
export const SERVER=[...DRIVEN,'T13a','T13b','T13c'];
/** The brief's subset: SERVER plus T16 (Owner drawing from the bank — rulebook B6 "new", not an ACC-1 command). The ACC-1 target once T16 exists. */
export const BRIEF=[...SERVER,'T16'];
/** What ACC-2 adds to the brief's subset. */
export const ACC2=ALL.filter(k=>!BRIEF.includes(k));
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
