// Navigation map: the ten module categories, which category every page belongs to, and the search index built from it.
// Pure data + pure functions (no React state). Modules Home, the hamburger menu, the breadcrumb and the search palette all read this,
// so a page cannot be missing from one of them. A new page must be added to PAGE_CATEGORY (a test enforces it).
import {dgsCanView} from '../domain/streams/digest'; // F4-T2
import {rctCanView} from '../domain/streams/rpt-revcat'; // F4-T2
import {Store,Receipt,Wrench,HeartHandshake,Users,TrendingUp,Package,IdCard,ChartNoAxesCombined,ShieldCheck,LayoutDashboard} from 'lucide-react';
import type {Permission} from '../services/mock';
import type {State} from '../domain/types';
import {cfgPageOn} from '../domain/streams/config';
import {cmpHasAccess} from '../domain/streams/compliance';
import {rmdCanManage} from '../domain/streams/reminders'; // LF-ALERTS
import {trcCan} from '../domain/streams/tariffcard'; // F4-T1
import {hallCanOpen} from '../features/hall/access'; // F6-H3
import {rbacCanViewClosing,rbacSeesApprovals,rbacIsRestaurant,rbacSeesBooks,rbacIsRole,rbacTechAdminTemplates,RBAC_RESTAURANT_PAGES} from '../domain/streams/rbac';

export type CategoryId='front'|'cash'|'hkm'|'guestsvc'|'customers'|'revenue'|'stock'|'people'|'accounts'|'admin';
export type NavCategory={id:CategoryId;label:string;blurb:string;icon:typeof Store};
/** Display order of the tiles on Modules Home. */
export const NAV_CATEGORIES:NavCategory[]=[
  {id:'front',label:'Front Desk',blurb:'Rooms, bookings and check-in',icon:Store},
  {id:'cash',label:'Cash & Billing',blurb:'Folios, cashier, restaurant, expenses',icon:Receipt},
  {id:'hkm',label:'Housekeeping & Maintenance',blurb:'Room cleaning, repairs, shift checklists',icon:Wrench},
  {id:'guestsvc',label:'Guests & Services',blurb:'In-house guests, requests, messages',icon:HeartHandshake},
  {id:'customers',label:'Customers',blurb:'Customer profiles and full stay history',icon:Users},
  {id:'revenue',label:'Revenue & Sales',blurb:'Tariff, groups, OTA and direct booking',icon:TrendingUp},
  {id:'stock',label:'Stock & Purchasing',blurb:'Linen, amenities and supplies',icon:Package},
  {id:'people',label:'People & Payroll',blurb:'Staff, shifts, leave and pay',icon:IdCard},
  {id:'accounts',label:'Accounts & Reports',blurb:'Day closing, reports, accounting',icon:ChartNoAxesCombined},
  {id:'admin',label:'Admin & Setup',blurb:'Access, configuration, audit, backup',icon:ShieldCheck},
];
export const DASHBOARD={id:'overview',label:'Dashboard',icon:LayoutDashboard};
export const categoryOf=(id:CategoryId)=>NAV_CATEGORIES.find(c=>c.id===id)!;

/** Page ID (nav item ID) → category. `overview` is the pinned Dashboard tile and has no category. */
export const PAGE_CATEGORY:Record<string,CategoryId>={
  rooms:'front',calendar:'front',reservations:'front',guests:'front',arrivals:'front',keys:'front', // KEYS room keys
  folios:'cash',cashier:'cash',cashbook:'cash',pos:'cash',expenses:'cash',
  housekeeping:'hkm',maintenance:'hkm',handover:'hkm',
  inhouse:'guestsvc','guest-services':'guestsvc','gsv-my-requests':'guestsvc',messages:'guestsvc',documents:'guestsvc',
  customers:'customers',
  yield:'revenue',groups:'revenue',channels:'revenue','booking-site':'revenue',
  inventory:'stock',
  staff:'people','my-hr':'people',
  closing:'accounts',reports:'accounts',accounting:'accounts',assistant:'accounts',compliance:'accounts',
  syshealth:'admin',   // Wave C3 (A09-05): parked outbox events, retry, last backup — Owner + Tech admin
  'plans-extras':'accounts', // MA-2
  'legacy-import':'admin', // LG-UI: old Lodge software import — Owner (PERM-LGC-001)
  prefs:'admin', // CP-1 Appearance & display (every role, this device only)
  // LF lanes: your PAGE_CATEGORY line inside your own block (contract §11: 'desk-alerts':'front' = LF-ALERTS, 'counter-bills':'cash' = LF-UI).
  // LF-ALERTS
  'desk-alerts':'front',
  // LF-ALERTS end
  // LF-UI
  'counter-bills':'cash',
  // LF-UI end
  // F4 lanes (F4-CONTRACT): 'tariff-card':'revenue' = F4-T1, 'owner-digest':'accounts' = F4-T2.
  // F4-T1
  'tariff-card':'revenue',
  // F4-T1 end
  // F4-T2
  'owner-digest':'accounts',
  'revenue-category':'accounts',
  // F4-T2 end
  // F4-T3
  // F4-T3 end
  // F6-H3
  'hall-booking':'revenue',
  // F6-H3 end
  access:'admin',settings:'admin',approvals:'admin',backup:'admin',roadmap:'admin',
};

/** Virtual pages (not nav items): Modules Home and one tile page per category. Always allowed once signed in. */
export const HOME_PAGE='home';
export const catPage=(id:CategoryId)=>'cat:'+id;
export const parseCatPage=(page:string):CategoryId|undefined=>{const c=NAV_CATEGORIES.find(x=>catPage(x.id)===page);return c?.id;};
export const pagesOfCategory=(id:CategoryId)=>Object.keys(PAGE_CATEGORY).filter(p=>PAGE_CATEGORY[p]===id);
/** Breadcrumb for a page: Home › Category › Page. Each part says where a click goes. */
export function crumbsFor(page:string,pageLabel:string):{label:string;page?:string}[]{
  if(page===HOME_PAGE)return [{label:'Modules Home'}];
  const home={label:'Modules Home',page:HOME_PAGE};
  const cid=parseCatPage(page);
  if(cid)return [home,{label:categoryOf(cid).label}];
  const cat=PAGE_CATEGORY[page];
  if(!cat)return [home,{label:page==='overview'?DASHBOARD.label:pageLabel}];
  const cl=categoryOf(cat).label;
  if(cl===pageLabel)return [home,{label:cl}]; // single-page category (e.g. Customers): no repeated crumb
  return [home,{label:cl,page:catPage(cat)},{label:pageLabel}];
}

/** One line under each page tile. */
export const PAGE_BLURB:Record<string,string>={
  overview:'Today at a glance: arrivals, occupancy, cash',
  rooms:'Live room board with status and guests',calendar:'Date grid of room availability',reservations:'Search, create and manage bookings',guests:'Front-desk guest list and guest profiles',arrivals:'Check guests in; look up a booking ID',
  folios:'Guest bills, payments and balances',cashier:'Shift cash, drawer and counts',cashbook:'Daily cash in and out with running balance',pos:'Restaurant and room-service checks',expenses:'Record and approve petty expenses',
  housekeeping:'Cleaning tasks and room inspection',maintenance:'Repairs, work orders and preventive upkeep',handover:'Shift checklists and handover notes',
  inhouse:'Guests staying now: extend, move, depart','guest-services':'Guest requests, complaints, lost & found','gsv-my-requests':'Requests assigned to you',messages:'Guest messages and templates',documents:'Scan and file IDs and bills',
  customers:'Search by name or mobile: full history and profile',
  yield:'Tariff, seasons and rate restrictions',groups:'Group blocks, companies and credit',channels:'OTA channels and commissions','booking-site':'Direct booking website',
  inventory:'Linen, amenities and supplies',
  staff:'Staff, attendance, leave and payroll','my-hr':'Your shifts, leave and pay',
  'owner-digest':'Yesterday in one page: occupancy, revenue, collections, held drafts','revenue-category':'Revenue by room type and collections by tender',closing:'Close the business day',reports:'Revenue, occupancy and cash reports',accounting:'Ledger, vouchers and period locks',assistant:'Ask questions about your hotel data',compliance:'GST, TDS, PF, ESIC and PT due dates',
  'plans-extras':'Catalogue vs free-text charges, meal plans, discount reasons',
  access:'Users, roles and permissions',settings:'All settings in one place: 12 groups, modules on/off',approvals:'Pending approvals and the audit trail',backup:'Backup and restore data',syshealth:'Stuck background work and the last backups',roadmap:'What is planned next',
  'legacy-import':'Load guests, history and opening balances from the old software',
  keys:'Room key cards: issue, re-issue, reactivate, cancel, key log and counts',
  prefs:'Text size, motion, start page, lists, messages (this device)',
  'desk-alerts':'Desk reminders with alert days and guests\' wake-up calls', // LF-ALERTS
  'counter-bills':'Bills for walk-in customers: paid now, Udhaar credit or company credit', // LF-UI
  'tariff-card':'Published room, meal and extra prices by valid-from date; rate sheet', // F4-T1
  // F6-H3
  'hall-booking':'Halls and lawn: enquiries, quotations with terms, calendar, events, event-day bill',
  // F6-H3 end
};

/** Extra words people type for a page (synonyms, hotel jargon, Hindi-English habits). The label and category are matched too. */
export const PAGE_KEYWORDS:Record<string,string[]>={
  overview:['home','dashboard','today','summary','kpi','occupancy'],
  rooms:['room board','rooms','availability','vacant','dirty','clean','blocked','status','floor'],
  calendar:['calendar','availability','tape chart','grid','dates'],
  reservations:['reservation','booking','bookings','book','stay','guest','search booking','cancel','confirm','hold'],
  guests:['guest list','guest profile','guest profiles','guests','id proof','new guest'],
  arrivals:['check in','checkin','check-in','arrival','arrivals','walk in','register','registration','booking id','key'],
  folios:['folio','bill','invoice','payment','balance','checkout','check out','deposit','refund','company credit'],
  cashier:['cashier','cash','drawer','shift','count','float','till','difference'],
  cashbook:['cash book','cashbook','day book','daybook','rokad','ledger','cash in','cash out','running balance','lock day'],
  pos:['restaurant','room service','kot','menu','bill','food','kitchen','pos','table'],
  expenses:['expense','expenses','petty cash','vendor','bill','approve expense','receipt'],
  housekeeping:['housekeeping','cleaning','clean','inspect','inspection','tasks','room attendant','stayover','dirty'],
  maintenance:['maintenance','repair','work order','ac','geyser','amc','preventive','spare','technician'],
  handover:['checklist','checklists','handover','shift handover','opening','closing checklist','night audit'],
  inhouse:['in house','in-house','staying','current guests','extend','move room','departure'],
  'guest-services':['guest services','request','requests','complaint','complaints','lost and found','lost found','towels','taxi','wake up'],
  'gsv-my-requests':['guest requests','my requests','tasks from guests','towels','water'],
  messages:['message','messages','whatsapp','sms','email','notification','confirmation','template'],
  documents:['document','documents','scan','id card','ocr','capture','vendor bill','letter'],
  customers:['customer','customers','guest','guests','guest profile','profile','id','preferences','notes','history','stay history','lifetime','repeat guest','loyalty','mobile number','phone'],
  yield:['tariff','rate','rates','yield','season','pricing','restriction','publish','dynamic'],
  groups:['group','groups','corporate','company','block','allotment','credit limit','rooming list','wedding'],
  channels:['ota','channel','channels','booknest','stayhub','tripkart','commission','inbound','booking.com'],
  'booking-site':['website','direct booking','web booking','online','booking engine','site'],
  inventory:['linen','stock','laundry','amenity','amenities','towel','sheet','store','grn','count'],
  staff:['staff','payroll','salary','attendance','leave','roster','advance','payslip','employee'],
  'my-hr':['my shifts','my pay','my leave','payslip','attendance','roster'],
  closing:['day closing','day close','night audit','close day','end of day','eod','close'],
  reports:['report','reports','revenue','occupancy','adr','revpar','csv','export','print','daily'],
  accounting:['accounting','ledger','voucher','trial balance','tally','lock','period','journal','statement','day book','chart of accounts','year end','financial year','carry forward','bank reconciliation','bank statement','clearing','upi clearing','party','parties','opening balance','opening balances','sub-ledger','debtors','creditors','manual voucher','manual vouchers','journal entry','approval queue','profit and loss','p&l','balance sheet','outstanding','ageing','gst summary','cash ratio','tally ledger export','excel export','gst month','set-off','month lock','tds','deductee','fixed assets','asset register','add loan','loans','vendor payment','vendor ageing','ota payout','cash guard'],
  'plans-extras':['meal plan','meal plans','ep','cp','map','ap','breakfast','extras','extra bed','catalogue','free text','discount reason','laundry','package'],
  assistant:['assistant','ask','question','ai','management assistant','insight'],
  compliance:['compliance','gst','gstr','tds','pf','epf','esic','pt','professional tax','due date','due dates','filing','return','ca pack','calendar'],
  access:['access','users','roles','permissions','rbac','login','disable user'],
  settings:['settings','configuration','config','master configuration','masters','admin','setup','hotel configuration','tax','tariff','hotel','policy','display','device','modules on off','branding'],
  approvals:['approval','approvals','audit','audit log','pending','discount approval','history','who did'],
  backup:['backup','restore','recovery','export data'],
  syshealth:['system health','health','parked','stuck','retry','outbox','event log','alerts','errors','backup failed','last backup','restore drill'],
  keys:['key','keys','room key','key card','keycard','card','encoder','lock','lost card','reissue','re-issue','reactivate','recharge','extend key','manual key','chabi','chaabi','key log','door lock','bis'],
  'legacy-import':['legacy import','old software','lodge','old data','import history','dry run','migration','legacy','previous software'],
  'counter-bills':['counter bill','direct bill','walk-in','walk in','udhaar','credit note','bill'], // LF-UI
  'tariff-card':['tariff card','rate card','rate sheet','price list','tariff','weekend rate','extra person','gst inclusive'], // F4-T1
  // F6-H3
  'hall-booking':['hall','banquet','lawn','wedding','function','event','quotation','quote','enquiry','beo','function sheet','catering','plates','marriage','party','t&c','terms'],
  // F6-H3 end
  'desk-alerts':['reminder','reminders','alert','alerts','wake up','wake-up','wakeup','alarm','alert days','due date'], // LF-ALERTS
  roadmap:['roadmap','future','planned','modules','phases'],
  prefs:['appearance','display','text size','font','font size','bigger text','larger text','zoom','motion','animation','density','compact','start page','home page','page size','rows','toast','messages','preferences','my settings','theme'],
};

export type SearchEntry={
  id:string;kind:'page'|'action';label:string;
  /** Page the entry opens (action entries open this page first). */
  page:string;
  /** Modal opened after navigating (action entries only). */
  modal?:{type:string;id?:string;room?:string};
  category:CategoryId|'dashboard';
  /** Breadcrumb shown under the result: category › page › (action). */
  path:string[];
  keywords:string[];
  /** Permission needed beyond the page itself (actions). */
  needs?:Permission;
};
/** Actions that jump straight into a form. Only dialogs that exist today (verified by the nav tests). */
export const SEARCH_ACTIONS:{id:string;label:string;page:string;modal:{type:string};needs:Permission;keywords:string[]}[]=[
  {id:'act-new-reservation',label:'New reservation',page:'reservations',modal:{type:'new'},needs:'reservations',keywords:['new booking','add booking','book a room','create reservation','new stay']},
  {id:'act-walkin',label:'Walk-in guest',page:'arrivals',modal:{type:'guest-walkin'},needs:'checkin',keywords:['walk in','walkin','new guest arriving','register walk in']},
  {id:'act-add-expense',label:'Add expense',page:'expenses',modal:{type:'expense'},needs:'expenses',keywords:['new expense','petty cash','record expense','vendor bill']},
];

/** Index of every page (from the nav items the app actually registers) plus the quick actions. */
export function buildSearchEntries(items:{id:string;label:string}[]):SearchEntry[]{
  const pages:SearchEntry[]=items.map(n=>{
    const cat=PAGE_CATEGORY[n.id],c=cat?categoryOf(cat):undefined;
    return {id:'page-'+n.id,kind:'page' as const,label:n.label,page:n.id,category:cat??'dashboard',path:c?[c.label,n.label]:[DASHBOARD.label],keywords:PAGE_KEYWORDS[n.id]??[]};
  });
  const have=new Set(items.map(n=>n.id));
  const actions:SearchEntry[]=SEARCH_ACTIONS.filter(a=>have.has(a.page)).map(a=>{
    const cat=PAGE_CATEGORY[a.page],c=categoryOf(cat),label=items.find(n=>n.id===a.page)!.label;
    return {id:a.id,kind:'action' as const,label:a.label,page:a.page,modal:a.modal,category:cat,path:[c.label,label,a.label],keywords:a.keywords,needs:a.needs};
  });
  return [...pages,...actions];
}

const norm=(t:string)=>t.toLowerCase().replace(/[^a-z0-9 ]+/g,' ').replace(/\s+/g,' ').trim();
export type SearchHit={entry:SearchEntry;score:number};
/** Every typed word must match somewhere (label, category, page, keywords). Label word-starts rank highest; actions rank just under their page. */
export function searchEntries(entries:SearchEntry[],query:string):SearchHit[]{
  const terms=norm(query).split(' ').filter(Boolean);
  if(!terms.length)return [];
  const hits:SearchHit[]=[];
  for(const e of entries){
    const label=norm(e.label),path=norm(e.path.join(' ')),kws=e.keywords.map(norm);
    let score=0,ok=true;
    for(const t of terms){
      let best=0;
      if(label.split(' ').some(w=>w.startsWith(t)))best=100;
      else if(label.includes(t))best=70;
      for(const k of kws){if(k.split(' ').some(w=>w.startsWith(t)))best=Math.max(best,60);else if(k.includes(t))best=Math.max(best,30);}
      if(path.includes(t))best=Math.max(best,40);
      if(!best){ok=false;break;}
      score+=best;
    }
    if(!ok)continue;
    if(label===norm(query))score+=50;
    if(e.kind==='action')score-=1;
    hits.push({entry:e,score});
  }
  return hits.sort((a,b)=>b.score-a.score||a.entry.path.join('›').localeCompare(b.entry.path.join('›')));
}

/** Entries a user may open: the page must be allowed, and an action also needs its own permission. */
export function visibleEntries(entries:SearchEntry[],allowedPages:Set<string>,can:(p:Permission)=>boolean):SearchEntry[]{
  return entries.filter(e=>allowedPages.has(e.page)&&(!e.needs||can(e.needs)));
}

/** W05-A · DEC-057 Modules on/off: the one filter every navigation surface applies (Modules Home and category tiles, hamburger menu,
 * search palette, phone tabs via App's allowed pages). Pages of a switched-off module disappear; core pages always stay. */
export function navFilterModules<T extends {id:string}>(s:State,items:readonly T[],uid?:string):T[]{return items.filter(i=>cfgPageOn(s,i.id)&&(uid===undefined||navRoleOk(s,uid,i.id)));}
/** Pages that permissions alone cannot express. W05-1 · DEC-055: Compliance calendar = Owner + Accountant only (Manager/Cashier also hold
 * 'reports'). DEC-063: Day closing = close holders + the Accountant read-only, not the Cashier (G4/G8); Approvals & audit = Owner,
 * Manager, Accountant, not the Cashier (G10). DEC-079: Accounting (the books) not for the Cashier; D1: the Restaurant role sees the POS and
 * its own shifts & pay only. */
export function navRoleOk(s:State,uid:string,page:string):boolean{
  if(rbacIsRestaurant(s,uid))return RBAC_RESTAURANT_PAGES.includes(page);
  if(page==='hall-booking')return hallCanOpen(s,uid); // F6-H3: every signed-in staff role but the Tech admin (the calendar shows no prices to housekeeping / maintenance, DEC-061)
  if(page==='tariff-card')return trcCan(s,uid,'view'); // F4-T1: PERM-TRF-001 (not housekeeping / maintenance, DEC-061)
  // F4-T2
  if(page==='owner-digest')return dgsCanView(s,uid);      // PERM-DGS-001: Owner only
  if(page==='revenue-category')return rctCanView(s,uid);  // PERM-RPT-001 + 002: Owner, Manager, Accountant (DEC-202 #1)
  // F4-T2 end
  if(page==='desk-alerts')return rmdCanManage(s,uid); // LF-ALERTS: PERM-RMD-001 = Owner, Manager, Reception, Accountant
  if(page==='compliance')return cmpHasAccess(s,uid);
  if(page==='closing')return rbacCanViewClosing(s,uid);
  if(page==='approvals')return rbacSeesApprovals(s,uid);
  if(page==='accounting')return rbacSeesBooks(s,uid);   // DEC-079: not the Cashier (collections only, Reports)
  // DEC-079 D4: the Tech admin reaches Messages only for its templates, in production (the demo baseline stays matrix v1, D6).
  if(page==='messages'&&rbacIsRole(s,uid,'Tech admin'))return rbacTechAdminTemplates(s,uid);
  if(page==='legacy-import')return !rbacIsRole(s,uid,'Tech admin');   // LG: the importer is Owner-only (PERM-LGC-001); the page itself checks the Owner role
  return true;
}
