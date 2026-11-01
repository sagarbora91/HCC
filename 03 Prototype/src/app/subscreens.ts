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
  {page:'access',id:'users',label:'Users'},
  {page:'access',id:'role-templates',label:'Role templates',keywords:['permissions','matrix']},
  {page:'access',id:'effective-access',label:'Effective access'},
  {page:'access',id:'exceptions',label:'Exceptions'},
  {page:'access',id:'policy-history',label:'Policy history'},
  {page:'access',id:'sign-in-devices',label:'Sign-in & devices',keywords:['password','pin','devices']}, // production build only; the demo build falls back to Users
  {page:'backup',id:'history',label:'Backup history'},
  {page:'backup',id:'restore',label:'Restore procedure'},
  {page:'prefs',id:'text',label:'Text size',keywords:['font','zoom']},
  {page:'prefs',id:'appearance',label:'Appearance',keywords:['theme','dark','density','motion']},
  {page:'prefs',id:'display',label:'Device & display',keywords:['phone layout','desktop layout']},
  {page:'prefs',id:'navigation',label:'Start & navigation',keywords:['start page','page size']},
  {page:'prefs',id:'alerts',label:'Alerts',keywords:['message duration']},
];
