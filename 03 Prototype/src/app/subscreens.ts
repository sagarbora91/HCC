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
  {page:'settings',id:'property',label:'Property and branding'},
  {page:'settings',id:'rooms',label:'Rooms and rate plans'},
  {page:'settings',id:'frontdesk',label:'Front-desk policies'},
  {page:'settings',id:'money',label:'Money and tax'},
  {page:'settings',id:'pos',label:'Restaurant and POS'},
  {page:'settings',id:'hkm',label:'Housekeeping and maintenance'},
  {page:'settings',id:'people',label:'People, payroll and leave'},
  {page:'settings',id:'compliance',label:'Compliance obligations'},
  {page:'settings',id:'modules',label:'Modules on/off'},
  {page:'settings',id:'users',label:'Users, roles and PINs'},
  {page:'settings',id:'messages',label:'Messages and documents'},
  {page:'settings',id:'data',label:'Data and system'},
  {page:'approvals',id:'approvals',label:'Approvals'},
  {page:'approvals',id:'expenses',label:'Pending expenses'},
  {page:'approvals',id:'audit',label:'Audit log'},
];
