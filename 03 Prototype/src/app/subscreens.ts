// FIT · sub-screen registry (DEC-196, contract §4). Every page tab made with FitTabs / useFitTab is listed here: the search palette shows
// each as "Page › Tab" and opens it with go(page,{tab:id}); tests/stream-fit.mjs checks FitTabs ids ↔ this list. Ids = kebab-case of the label.
// Module lanes append lines ONLY inside their own // FIT-<LANE> block, one sub-screen per line: {page:'<page id>',id:'<tab id>',label:'<Tab label>'},
import type {State} from '../domain/types';
import {hrCanManage,hrCanPay} from '../domain/streams/staff';
import {cfgModuleOn} from '../domain/streams/config';
import {chnCommissionHidden} from '../domain/streams/channels';
import {rbacPosSeesDaySales,rbacTechAdminTemplates,rbacCanTally} from '../domain/streams/rbac';
import {hovIsManager} from '../domain/streams/handover';
import {yldProposals} from '../domain/streams/yield';
import {keyCanConfigure} from '../domain/streams/keys';
import {guestAccess} from '../domain/streams/guests'; // LF-DESK
import {hallTabOpen} from '../features/hall/access'; // F6-H3
export type SubScreen={page:string;id:string;label:string;keywords?:string[]};
export const SUB_SCREENS:SubScreen[]=[
  // FIT-FRONT
  // LF-DESK
  {page:'customers',id:'customers',label:'Customers'},
  {page:'customers',id:'foreigner-register',label:'Foreigner register',keywords:['form c','foreign','immigration','passport','visa']},
  // LF-DESK end
  // FIT-FRONT-B: reservations, arrivals, inhouse, guests have no sub-screens (fill / split pages).
  // F1-INT: KEYS room keys (from main)
  {page:'keys',id:'log',label:'Key log'},
  {page:'keys',id:'locks',label:'Lock mapping'},
  {page:'keys',id:'encoder',label:'Encoder'},
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
  {page:'guest-services',id:'requests',label:'Requests'},
  {page:'guest-services',id:'complaints',label:'Complaints'},
  {page:'guest-services',id:'lost-found',label:'Lost & found',keywords:['lost and found','found items']},
  {page:'guest-services',id:'reports',label:'Reports'},
  {page:'messages',id:'outbox',label:'Outbox'},
  {page:'messages',id:'triggers',label:'Triggers'},
  {page:'messages',id:'templates',label:'Templates'},
  {page:'messages',id:'consent',label:'Consent'},
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
  // FIT2-CLOSING
  {page:'cashier',id:'expected-cash',label:'Expected cash',keywords:['drawer formula']},
  {page:'cashier',id:'count-and-result',label:'Count & result',keywords:['count the drawer','cash count','difference approval']},
  {page:'cashier',id:'cash-movements',label:'Cash movements'},
  {page:'cashier',id:'cash-out',label:'Cash out',keywords:['bank deposit','till transfer','owner drawing']},
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
  {page:'accounting',id:'year',label:'Year end',keywords:['financial year','carry forward','year lock','reopen year']},
  {page:'accounting',id:'bank',label:'Bank reconciliation',keywords:['bank statement','match','reconcile bank','utr']},
  {page:'accounting',id:'clearing',label:'Clearing',keywords:['upi clearing','card clearing','settlement','1010','1020']},
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
  // FIT-ADMIN-A
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
  {page:'settings',id:'internet',label:'Internet access',keywords:['internet','egress','online','offline','ota sync','whatsapp','master switch']},
  {page:'settings',id:'data',label:'Data and system'},
  {page:'approvals',id:'approvals',label:'Approvals'},
  {page:'approvals',id:'expenses',label:'Pending expenses'},
  {page:'approvals',id:'audit',label:'Audit log'},
  // FIT-ADMIN-B
  {page:'access',id:'modules',label:'Modules & features',keywords:['switch','sub-functions']},
  {page:'access',id:'roles',label:'Roles',keywords:['custom role','permissions','matrix','grid']},
  {page:'access',id:'users',label:'Users',keywords:['password','pin','devices','exceptions','effective access']},
  {page:'access',id:'audit',label:'Audit',keywords:['policy history','policy versions']},
  {page:'backup',id:'history',label:'Backup history'},
  {page:'backup',id:'restore',label:'Restore procedure'},
  {page:'prefs',id:'text',label:'Text size',keywords:['font','zoom']},
  {page:'prefs',id:'appearance',label:'Appearance',keywords:['theme','dark','density','motion']},
  {page:'prefs',id:'display',label:'Device & display',keywords:['phone layout','desktop layout']},
  {page:'prefs',id:'navigation',label:'Start & navigation',keywords:['start page','page size']},
  {page:'prefs',id:'alerts',label:'Alerts',keywords:['message duration']},
  // LF lanes (contract §11): one sub-screen per line inside your own block.
  // LF-DESK
  // LF-DESK end
  // LF-ALERTS
  {page:'desk-alerts',id:'today',label:'Today'},
  {page:'desk-alerts',id:'upcoming',label:'Upcoming'},
  {page:'desk-alerts',id:'done',label:'Done'},
  {page:'desk-alerts',id:'wake-ups',label:'Wake-ups'},
  // LF-ALERTS end
  // LF-UI
  {page:'counter-bills',id:'new-bill',label:'New bill'},
  {page:'counter-bills',id:'bills',label:'Bills'},
  {page:'cashier',id:'credit-guests',label:'Credit guests',keywords:['guest credit','udhaar','outstanding']},
  // LF-UI end
  // F7-R2
  {page:'report-hub',id:'reports',label:'Reports',keywords:['report library','catalogue','search reports','favourites']},
  {page:'report-hub',id:'scheduled',label:'Scheduled runs',keywords:['subscriptions','schedule','email report']},
  {page:'report-hub',id:'log',label:'Export log',keywords:['print log','who exported']},
  // F7-R2 end
  // F4-T1
  {page:'tariff-card',id:'rooms',label:'Rooms',keywords:['room prices','weekday','weekend','extra person']},
  {page:'tariff-card',id:'meals',label:'Meal plans & extras',keywords:['breakfast','cp','map','add-on']},
  {page:'tariff-card',id:'seasons',label:'Seasons & weekends',keywords:['valid from','season','weekend nights']},
  {page:'tariff-card',id:'halls',label:'Halls',keywords:['hall','banquet']},
  {page:'tariff-card',id:'history',label:'History',keywords:['versions','publish log']},
  {page:'tariff-card',id:'print',label:'Print',keywords:['rate sheet','print','csv']},
  // F4-T1 end
  // F4-T2
  {page:'revenue-category',id:'a',label:'A. Revenue by category',keywords:['room type revenue','occupancy']},
  {page:'revenue-category',id:'b',label:'B. Collections by tender',keywords:['cash','upi','card','tender']},
  {page:'revenue-category',id:'c',label:'C. Bridge',keywords:['tie out','advances']},
  // F4-T2 end
  // F4-T3
  // F4-T3 end
  // F6-H3
  {page:'hall-booking',id:'enquiries',label:'Enquiries',keywords:['follow up','lead','wedding enquiry']},
  {page:'hall-booking',id:'quotations',label:'Quotations',keywords:['quote','estimate','revise','send']},
  {page:'hall-booking',id:'calendar',label:'Calendar',keywords:['availability','clash','hold','date']},
  {page:'hall-booking',id:'events',label:'Events',keywords:['function sheet','beo','event day','plates','bill','advance','invoice']},
  {page:'hall-booking',id:'menus',label:'Menus',keywords:['per plate','package','halls master','hall rates']},
  {page:'hall-booking',id:'templates',label:'Templates',keywords:['terms','t&c','conditions','clauses']},
  // F6-H3 end
  // F8-SF-UI
  // F8-SF-UI end
  // F8-QW-ADV
  {page:'cashier',id:'advances-held',label:'Advances held',keywords:['advance','deposit','refund','held money']},
  // F8-QW-ADV end
  // F8-QW-PM
  // F8-QW-PM end
  // F8-QW-KDS
  // F8-QW-KDS end
];

/** Review S3: sub-screens a role's page never shows (the page falls back to its default tab) are not listed in the search palette for that
 *  role. Each rule is the page's own tab condition (HrDesk tabs, Accounting tally guard, expSectionsFor, chnTabsFor, PosPage sales, Messages
 *  templatesOnly, useHovTabs, YieldPage publish tab, AccessControl production tab). A sub-screen without a rule is open to every role that can
 *  open the page. Key: '<page>:<tab id>'. */
const books=(s:State,uid:string)=>['Owner','Manager','Accountant'].includes(s.users.find(x=>x.id===uid)?.role??''); // = accounting/shared seesVendorBooks
const hrDesk=(s:State,uid:string)=>hrCanManage(s,uid)||hrCanPay(s,uid);
const hrPay=(s:State,uid:string)=>hrCanPay(s,uid)&&cfgModuleOn(s,'payroll');
const notTemplatesOnly=(s:State,uid:string)=>!rbacTechAdminTemplates(s,uid);
export const SUB_SCREEN_GATES:Record<string,(s:State,uid:string)=>boolean>={
  'staff:people':hrDesk,'staff:roster':hrDesk,'staff:attendance':hrDesk,'staff:leave':hrDesk,
  'staff:payroll':hrPay,'staff:advances':hrPay,'staff:fnf':hrPay,'staff:settings':hrPay,
  'accounting:tally':rbacCanTally,
  'accounting:year':(s,uid)=>['Owner','Accountant'].includes(s.users.find(x=>x.id===uid)?.role??''),'accounting:bank':(s,uid)=>['Owner','Accountant'].includes(s.users.find(x=>x.id===uid)?.role??''), // F5-U1 PERM-ACC-014 / 016
  'accounting:clearing':books,'settings:internet':(s,uid)=>['Owner','Manager'].includes(s.users.find(x=>x.id===uid)?.role??''), // F5-U1 (EGRESS-CONTRACT §6)
  'expenses:vendors':books,
  'channels:commission':s=>!chnCommissionHidden(s),'channels:payouts':books,
  'pos:summary':rbacPosSeesDaySales,
  'messages:outbox':notTemplatesOnly,'messages:consent':notTemplatesOnly,
  'handover:dashboard':hovIsManager,'handover:templates':hovIsManager,
  'customers:foreigner-register':(s,uid)=>guestAccess(s,uid).idDetails, // LF-DESK
  'keys:locks':(s,uid)=>keyCanConfigure(s,uid),'keys:encoder':(s,uid)=>keyCanConfigure(s,uid),
  // F6-H3
  'hall-booking:enquiries':(s,uid)=>hallTabOpen(s,uid,'enquiries'),'hall-booking:quotations':(s,uid)=>hallTabOpen(s,uid,'quotations'),
  'hall-booking:menus':(s,uid)=>hallTabOpen(s,uid,'menus'),'hall-booking:templates':(s,uid)=>hallTabOpen(s,uid,'templates'),
  // F6-H3 end
  'yield:publish-requests':s=>yldProposals(s).length>0,
  'access:sign-in-devices':()=>process.env.HCC_BUILD==='production',
};
/** May this role see sub-screen `tab` of `page` (palette filter; the page's own permission is checked separately). */
export const subScreenOpen=(s:State,uid:string,page:string,tab:string)=>SUB_SCREEN_GATES[page+':'+tab]?.(s,uid)??true;
