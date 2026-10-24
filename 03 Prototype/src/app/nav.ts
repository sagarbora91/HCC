// Grouped navigation (Prototype Spec §1). Screens that arrive in M2/M3 are added here when built — never shown disabled.
import {LayoutDashboard,Grid2X2,CalendarDays,Receipt,Brush,Wallet,Sunset,ChartNoAxesCombined,ShieldCheck,Settings} from 'lucide-react';
import {Permission,Role} from '../services/mock';
/** p: one permission, or several (any-of) when different roles reach the page through different permissions. */
export type NavItem={id:string;label:string;icon:typeof LayoutDashboard;p:Permission|Permission[]};
const baseGroups:{label:string;items:NavItem[]}[]=[
  {label:'',items:[{id:'overview',label:'Overview',icon:LayoutDashboard,p:'reports'}]},
  {label:'FRONT DESK',items:[{id:'rooms',label:'Room board',icon:Grid2X2,p:'reservations'},{id:'reservations',label:'Reservations',icon:CalendarDays,p:'reservations'}]},
  {label:'CASH & BILLING',items:[{id:'folios',label:'Guests & folios',icon:Receipt,p:'billing'}]},
  {label:'OPERATIONS',items:[{id:'housekeeping',label:'Housekeeping',icon:Brush,p:'housekeeping'},{id:'expenses',label:'Expenses',icon:Wallet,p:'expenses'}]},
  {label:'CLOSING & REPORTS',items:[{id:'closing',label:'Day closing',icon:Sunset,p:'close'},{id:'reports',label:'Reports',icon:ChartNoAxesCombined,p:'reports'}]},
  {label:'ADMINISTRATION',items:[{id:'access',label:'Access control',icon:ShieldCheck,p:'admin'},{id:'settings',label:'Hotel configuration',icon:Settings,p:'admin'}]},
];
import {extraNav,extraLanding} from './registry';
/** Base groups plus stream nav items (app/registry.ts). */
export const navGroups=(()=>{const groups=baseGroups.map(g=>({label:g.label,items:[...g.items]}));
  for(const x of extraNav){let g=groups.find(g=>g.label===x.group);if(!g){g={label:x.group,items:[]};groups.splice(groups.length-1,0,g);}
    if(g.items.some(i=>i.id===x.item.id))continue;const at=x.after?g.items.findIndex(i=>i.id===x.after):-1;if(at>=0)g.items.splice(at+1,0,x.item);else g.items.push(x.item);}
  return groups;})();
export const navItems=navGroups.flatMap(g=>g.items);
/** Default landing page per role (Prototype Spec §1.1). Falls back to the first permitted page. */
const baseLanding:Record<Role,string>={Owner:'overview',Manager:'overview',Reception:'rooms',Cashier:'folios',Supervisor:'housekeeping',Housekeeper:'housekeeping',Accountant:'reports',Maintenance:'housekeeping','Tech admin':'access'};
export const landing:Record<Role,string>={...baseLanding,...extraLanding} as Record<Role,string>;
