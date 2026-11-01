// FIT · sub-screen registry (DEC-196, contract §4). Every page tab made with FitTabs / useFitTab is listed here: the search palette shows
// each as "Page › Tab" and opens it with go(page,{tab:id}); tests/stream-fit.mjs checks FitTabs ids ↔ this list. Ids = kebab-case of the label.
// Module lanes append lines ONLY inside their own // FIT-<LANE> block, one sub-screen per line: {page:'<page id>',id:'<tab id>',label:'<Tab label>'},
export type SubScreen={page:string;id:string;label:string;keywords?:string[]};
export const SUB_SCREENS:SubScreen[]=[
  // FIT-FRONT
  // FIT-FRONT-B: reservations, arrivals, inhouse, guests have no sub-screens (fill / split pages).
  // FIT-CASH
  // FIT-OPS
  {page:'maintenance',id:'orders',label:'Work orders'},
  {page:'maintenance',id:'pm',label:'Preventive'},
  {page:'maintenance',id:'assets',label:'Assets'},
  {page:'maintenance',id:'spares',label:'Spares'},
  {page:'maintenance',id:'vendors',label:'Vendors & AMC'},
  {page:'maintenance',id:'reports',label:'Reports'},
  {page:'handover',id:'checklists',label:'My checklists'},
  {page:'handover',id:'handover',label:'Handover'},
  {page:'handover',id:'dashboard',label:'Dashboard'},
  {page:'handover',id:'templates',label:'Templates'},
  {page:'inventory',id:'stock',label:'Stock'},
  {page:'inventory',id:'laundry',label:'Laundry'},
  {page:'inventory',id:'counts',label:'Counts'},
  {page:'inventory',id:'losses',label:'Losses'},
  {page:'inventory',id:'consumption',label:'Consumption'},
  {page:'inventory',id:'par-minimums',label:'Par & minimums'},
  {page:'inventory',id:'movements',label:'Movements'},
  // FIT-GUEST
  // FIT-REVENUE
  // FIT-PEOPLE
  // FIT-ADMIN
  // FIT-FRONT-A
  {page:'overview',id:'arrivals',label:'Arrivals'},
  {page:'overview',id:'departures',label:'Departures'},
  {page:'overview',id:'in-house',label:'In house'},
  {page:'rooms',id:'all',label:'All floors'},
  // FIT-CASH-A
  {page:'folios',id:'folios',label:'Folios',keywords:['folio list','balances']},
  {page:'folios',id:'folio-requests',label:'Folio requests',keywords:['refund','company credit','correction','credit note','discount approval']},
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
