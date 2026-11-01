// FIT · sub-screen registry (DEC-196, contract §4). Every page tab made with FitTabs / useFitTab is listed here: the search palette shows
// each as "Page › Tab" and opens it with go(page,{tab:id}); tests/stream-fit.mjs checks FitTabs ids ↔ this list. Ids = kebab-case of the label.
// Module lanes append lines ONLY inside their own // FIT-<LANE> block, one sub-screen per line: {page:'<page id>',id:'<tab id>',label:'<Tab label>'},
export type SubScreen={page:string;id:string;label:string;keywords?:string[]};
export const SUB_SCREENS:SubScreen[]=[
  // FIT-FRONT
  // FIT-CASH
  // FIT-OPS
  // FIT-GUEST
  // FIT-REVENUE
  // FIT-PEOPLE
  // FIT-ADMIN
  // FIT-PEOPLE-A
  {page:'staff',id:'people',label:'Staff',keywords:['staff master','employees']},
  {page:'staff',id:'roster',label:'Roster',keywords:['shifts','weekly roster']},
  {page:'staff',id:'attendance',label:'Attendance',keywords:['attendance register','past days']},
  {page:'staff',id:'leave',label:'Leave',keywords:['leave calendar','leave approvals','holidays']},
  {page:'staff',id:'payroll',label:'Payroll',keywords:['pay-run','payslips','salary']},
  {page:'staff',id:'advances',label:'Advances',keywords:['salary advance']},
  {page:'staff',id:'fnf',label:'Full and final',keywords:['settlement','exit']},
  {page:'staff',id:'settings',label:'Statutory rates',keywords:['pf','esic','professional tax']},
  {page:'closing',id:'preflight',label:'Preflight',keywords:['closing checks']},
  {page:'closing',id:'post-room-nights',label:'Post room nights',keywords:['night audit']},
  {page:'closing',id:'cash-shift',label:'Cash shift'},
  {page:'closing',id:'close-business-date',label:'Close business date',keywords:['close day']},
  {page:'closing',id:'closing-summary',label:'Closing summary',keywords:['earlier closing records']},
  {page:'closing',id:'open-next-business-day',label:'Open next business day',keywords:['next day']},
  {page:'closing',id:'reopen-and-corrections',label:'Reopen & corrections',keywords:['closed-date correction','reopen a closed day']},
  {page:'compliance',id:'calendar',label:'Calendar'},
  {page:'compliance',id:'dashboard',label:'Dashboard',keywords:['compliance score']},
];
