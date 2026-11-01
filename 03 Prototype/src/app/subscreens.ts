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
  // FIT-CASH-B
  {page:'pos',id:'orders',label:'Orders'},
  {page:'pos',id:'kitchen',label:'Kitchen (KOT)',keywords:['kot','kitchen order ticket']},
  {page:'pos',id:'summary',label:'Day summary'},
  {page:'expenses',id:'register',label:'Register'},
  {page:'expenses',id:'recurring',label:'Recurring'},
  {page:'expenses',id:'petty',label:'Petty cash'},
  {page:'expenses',id:'budgets',label:'Budgets'},
  {page:'expenses',id:'udhaar',label:'Udhaar',keywords:['credit','who owes']},
  {page:'expenses',id:'vendors',label:'Vendor payments'},
  {page:'expenses',id:'month',label:'Month & tax',keywords:['tax feed','gst','itc']},
];
