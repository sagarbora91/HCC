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
  // FIT-CASH-A
  {page:'folios',id:'folios',label:'Folios',keywords:['folio list','balances']},
  {page:'folios',id:'folio-requests',label:'Folio requests',keywords:['refund','company credit','correction','credit note','discount approval']},
];
