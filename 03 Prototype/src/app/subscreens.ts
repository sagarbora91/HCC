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
  // FIT-PEOPLE-B
  {page:'accounting',id:'vouchers',label:'Vouchers',keywords:['voucher list','day vouchers']},
  {page:'accounting',id:'manual',label:'Manual vouchers',keywords:['journal','contra','loan']},
  {page:'accounting',id:'gst',label:'GST month',keywords:['gst set-off','month lock','gstr']},
  {page:'accounting',id:'tds',label:'TDS',keywords:['tds challan','deduction']},
  {page:'accounting',id:'assets',label:'Assets',keywords:['asset register','depreciation']},
  {page:'accounting',id:'tb',label:'Trial balance',keywords:['tb','six column']},
  {page:'accounting',id:'statements',label:'Statements',keywords:['ledger statement','party statement']},
  {page:'accounting',id:'pl',label:'P&L',keywords:['profit and loss','income statement']},
  {page:'accounting',id:'bs',label:'Balance sheet'},
  {page:'accounting',id:'ageing',label:'Outstanding',keywords:['ageing','receivables','payables']},
  {page:'accounting',id:'gstsum',label:'GST summary'},
  {page:'accounting',id:'cashratio',label:'Cash ratio'},
  {page:'accounting',id:'exports',label:'Exports',keywords:['report export']},
  {page:'accounting',id:'daybook',label:'Day book'},
  {page:'accounting',id:'chart',label:'Chart of accounts',keywords:['ledgers','groups']},
  {page:'accounting',id:'parties',label:'Parties',keywords:['party ledgers','debtors','creditors']},
  {page:'accounting',id:'opening',label:'Opening balances'},
  {page:'accounting',id:'exceptions',label:'Exceptions',keywords:['suspense','unmapped']},
  {page:'accounting',id:'recon',label:'Reconciliation'},
  {page:'accounting',id:'mapping',label:'Mapping',keywords:['ledger mapping']},
  {page:'accounting',id:'tally',label:'Tally export',keywords:['tally xml']},
  {page:'plans-extras',id:'service-charges',label:'Service charges',keywords:['catalogue','free text']},
  {page:'plans-extras',id:'stays-by-meal-plan',label:'Stays by meal plan',keywords:['meal plan']},
  {page:'plans-extras',id:'discounts-by-reason',label:'Discounts by reason',keywords:['discount reasons']},
];
