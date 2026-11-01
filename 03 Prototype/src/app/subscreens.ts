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
  {page:'yield',id:'rate-calendar',label:'Rate calendar',keywords:['suggested','bar','calendar']},
  {page:'yield',id:'rate-plans',label:'Rate plans',keywords:['quote','rate sheet']},
  {page:'yield',id:'rules',label:'Rules',keywords:['season','guardrails','occupancy tiers']},
  {page:'yield',id:'restrictions',label:'Restrictions',keywords:['minimum stay','closed to arrival']},
  {page:'yield',id:'history',label:'History',keywords:['publish log','change history']},
  {page:'yield',id:'publish-requests',label:'Publish requests',keywords:['proposals','approve']},
  {page:'groups',id:'blocks',label:'Blocks',keywords:['rooming list','allotment','group block']},
  {page:'groups',id:'companies',label:'Companies',keywords:['credit limit','corporate']},
  {page:'groups',id:'pickup-report',label:'Pickup report'},
  {page:'groups',id:'master-folio',label:'Master folio',keywords:['billing']},
  {page:'groups',id:'limit-requests',label:'Limit requests',keywords:['credit']},
  {page:'channels',id:'inbox',label:'Inbox',keywords:['ota messages','reservations']},
  {page:'channels',id:'channels',label:'Channels & mapping',keywords:['room mapping','stop-sell']},
  {page:'channels',id:'push',label:'Rate & availability push'},
  {page:'channels',id:'commission',label:'Commission report'},
  {page:'channels',id:'payouts',label:'Payouts'},
  {page:'booking-site',id:'bookings',label:'Bookings',keywords:['web requests']},
  {page:'booking-site',id:'preview',label:'Preview',keywords:['guest page']},
  // FIT-PEOPLE
  // FIT-ADMIN
];
