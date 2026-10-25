// Navigation map: the ten module categories, which category every page belongs to, and the search index built from it.
// Pure data + pure functions (no React state). Modules Home, the hamburger menu, the breadcrumb and the search palette all read this,
// so a page cannot be missing from one of them. A new page must be added to PAGE_CATEGORY (a test enforces it).
import {Store,Receipt,Wrench,HeartHandshake,Users,TrendingUp,Package,IdCard,ChartNoAxesCombined,ShieldCheck,LayoutDashboard} from 'lucide-react';
import type {Permission} from '../services/mock';

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
  rooms:'front',calendar:'front',reservations:'front',arrivals:'front',
  folios:'cash',cashier:'cash',cashbook:'cash',pos:'cash',expenses:'cash',
  housekeeping:'hkm',maintenance:'hkm',handover:'hkm',
  inhouse:'guestsvc','guest-services':'guestsvc','gsv-my-requests':'guestsvc',messages:'guestsvc',documents:'guestsvc',
  customers:'customers',
  yield:'revenue',groups:'revenue',channels:'revenue','booking-site':'revenue',
  inventory:'stock',
  staff:'people','my-hr':'people',
  closing:'accounts',reports:'accounts',accounting:'accounts',assistant:'accounts',compliance:'accounts',
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
  rooms:'Live room board with status and guests',calendar:'Date grid of room availability',reservations:'Search, create and manage bookings',arrivals:'Check guests in; look up a booking ID',
  folios:'Guest bills, payments and balances',cashier:'Shift cash, drawer and counts',cashbook:'Daily cash in and out with running balance',pos:'Restaurant and room-service checks',expenses:'Record and approve petty expenses',
  housekeeping:'Cleaning tasks and room inspection',maintenance:'Repairs, work orders and preventive upkeep',handover:'Shift checklists and handover notes',
  inhouse:'Guests staying now: extend, move, depart','guest-services':'Guest requests, complaints, lost & found','gsv-my-requests':'Requests assigned to you',messages:'Guest messages and templates',documents:'Scan and file IDs and bills',
  customers:'Search by name or mobile: full history and profile',
  yield:'Tariff, seasons and rate restrictions',groups:'Group blocks, companies and credit',channels:'OTA channels and commissions','booking-site':'Direct booking website',
  inventory:'Linen, amenities and supplies',
  staff:'Staff, attendance, leave and payroll','my-hr':'Your shifts, leave and pay',
  closing:'Close the business day',reports:'Revenue, occupancy and cash reports',accounting:'Ledger, vouchers and period locks',assistant:'Ask questions about your hotel data',compliance:'GST, TDS, PF, ESIC and PT due dates',
  access:'Users, roles and permissions',settings:'Hotel configuration and policies',approvals:'Pending approvals and the audit trail',backup:'Backup and restore data',roadmap:'What is planned next',
};

/** Extra words people type for a page (synonyms, hotel jargon, Hindi-English habits). The label and category are matched too. */
export const PAGE_KEYWORDS:Record<string,string[]>={
  overview:['home','dashboard','today','summary','kpi','occupancy'],
  rooms:['room board','rooms','availability','vacant','dirty','clean','blocked','status','floor'],
  calendar:['calendar','availability','tape chart','grid','dates'],
  reservations:['reservation','booking','bookings','book','stay','guest','search booking','cancel','confirm','hold'],
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
  accounting:['accounting','ledger','voucher','trial balance','tally','lock','period','journal'],
  assistant:['assistant','ask','question','ai','management assistant','insight'],
  compliance:['compliance','gst','gstr','tds','pf','epf','esic','pt','professional tax','due date','due dates','filing','return','ca pack','calendar'],
  access:['access','users','roles','permissions','rbac','login','disable user'],
  settings:['settings','configuration','config','tax','tariff','hotel','policy','display','device'],
  approvals:['approval','approvals','audit','audit log','pending','discount approval','history','who did'],
  backup:['backup','restore','recovery','export data'],
  roadmap:['roadmap','future','planned','modules','phases'],
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
