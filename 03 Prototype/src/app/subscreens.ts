// FIT · sub-screen registry (DEC-196, contract §4). Every page tab made with FitTabs / useFitTab is listed here: the search palette shows
// each as "Page › Tab" and opens it with go(page,{tab:id}); tests/stream-fit.mjs checks FitTabs ids ↔ this list. Ids = kebab-case of the label.
// Module lanes append lines ONLY inside their own // FIT-<LANE> block, one sub-screen per line: {page:'<page id>',id:'<tab id>',label:'<Tab label>'},
export type SubScreen={page:string;id:string;label:string;keywords?:string[]};
export const SUB_SCREENS:SubScreen[]=[
  // FIT-FRONT
  // FIT-CASH
  // FIT-OPS
  // FIT-GUEST
  {page:'guest-services',id:'requests',label:'Requests'},
  {page:'guest-services',id:'complaints',label:'Complaints'},
  {page:'guest-services',id:'lost-found',label:'Lost & found',keywords:['lost and found','found items']},
  {page:'guest-services',id:'reports',label:'Reports'},
  {page:'messages',id:'outbox',label:'Outbox'},
  {page:'messages',id:'triggers',label:'Triggers'},
  {page:'messages',id:'templates',label:'Templates'},
  {page:'messages',id:'consent',label:'Consent'},
  // FIT-REVENUE
  // FIT-PEOPLE
  // FIT-ADMIN
];
