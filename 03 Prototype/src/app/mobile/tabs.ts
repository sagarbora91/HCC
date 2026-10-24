// MB-02 Bottom tabs per role (Mobile plan §3). Only pages the user may open are shown (same can() rules as desktop).
import {Home,Grid2X2,ClipboardCheck,ChartNoAxesCombined,Sunset,DoorOpen,CalendarDays,Receipt,Banknote,Wallet,Brush,SearchCheck,Wrench,ShieldCheck,ScrollText,ListChecks,Hammer} from 'lucide-react';
import type {Role} from '../../services/mock';
export type MobileTab={key:string;page:string;label:string;icon:typeof Home;badge?:'approvals'|'tasks'|'arrivals';focus?:string};
const T={
  home:{key:'home',page:'overview',label:'Home',icon:Home},
  rooms:{key:'rooms',page:'rooms',label:'Rooms',icon:Grid2X2},
  approvals:{key:'approvals',page:'approvals',label:'Approvals',icon:ClipboardCheck,badge:'approvals'},
  reports:{key:'reports',page:'reports',label:'Reports',icon:ChartNoAxesCombined},
  closing:{key:'closing',page:'closing',label:'Closing',icon:Sunset},
  arrivals:{key:'arrivals',page:'arrivals',label:'Arrivals',icon:DoorOpen,badge:'arrivals'},
  bookings:{key:'bookings',page:'reservations',label:'Bookings',icon:CalendarDays},
  folios:{key:'folios',page:'folios',label:'Folios',icon:Receipt},
  cashier:{key:'cashier',page:'cashier',label:'Cashier',icon:Banknote},
  expenses:{key:'expenses',page:'expenses',label:'Expenses',icon:Wallet},
  board:{key:'board',page:'housekeeping',label:'Board',icon:Brush,badge:'tasks'},
  inspect:{key:'inspect',page:'housekeeping',label:'Inspect',icon:SearchCheck,focus:'Inspection'},
  mytasks:{key:'mytasks',page:'housekeeping',label:'My tasks',icon:ListChecks,badge:'tasks'},
  hkrepair:{key:'hkrepair',page:'housekeeping',label:'Report repair',icon:Hammer,focus:'Report repair'},
  repairs:{key:'repairs',page:'housekeeping',label:'My repairs',icon:Wrench,badge:'tasks'},
  users:{key:'users',page:'access',label:'Users',icon:ShieldCheck},
  audit:{key:'audit',page:'approvals',label:'Audit',icon:ScrollText},
} satisfies Record<string,MobileTab>;
export const roleTabs:Record<Role,MobileTab[]>={
  Owner:[T.home,T.rooms,T.approvals,T.reports],
  Manager:[T.home,T.rooms,T.approvals,T.closing],
  Reception:[T.rooms,T.arrivals,T.bookings,T.folios],
  Cashier:[T.cashier,T.folios,T.expenses],
  Supervisor:[T.board,T.inspect,T.rooms],
  Housekeeper:[T.mytasks,T.hkrepair],
  Maintenance:[{key:'workorders',page:'maintenance',label:'Work orders',icon:Wrench},T.repairs,T.rooms],
  Accountant:[T.reports,T.expenses,T.approvals],
  'Tech admin':[T.users,T.audit],
};
/** Permitted tabs for a role (max 4 + More). */
export function tabsFor(role:Role,allowed:string[]):MobileTab[]{return (roleTabs[role]||[]).filter(t=>allowed.includes(t.page)).slice(0,4);}
/** MB-07: the tab bar tells the page which segment/mode to open on (e.g. "Inspect" → Inspect segment). App.onTab calls
 *  announceTab before navigating; a page reads takeTabFocus() on mount and listens for MOBILE_TAB_EVENT while mounted. */
export const MOBILE_TAB_EVENT='hcc-mobile-tab';
let pendingTab='';
export function announceTab(key:string){pendingTab=key;try{window.dispatchEvent(new CustomEvent(MOBILE_TAB_EVENT,{detail:key}));}catch{/* no window (tests) */}}
/** Returns the tab key just pressed (once), then clears it so later navigation (More, Back, "Go there") opens the default view. */
export function takeTabFocus(){const k=pendingTab;pendingTab='';return k;}
