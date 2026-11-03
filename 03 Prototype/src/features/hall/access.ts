// F6-H3 · Hall booking — who sees what (spec §16, contract §4). Pure, no React: imported by app/navmap.ts and app/subscreens.ts.
// The prototype checks roles; the server checks PERM-HAL-001…006 (migration 4345). Housekeeping / maintenance / supervisors get the calendar and
// the function sheet without prices (DEC-061).
import type {State,Role} from '../../domain/types';
import {HALL_ROLES,hallSeesMoney} from '../../domain/streams/hall';

export type HallTab='enquiries'|'quotations'|'calendar'|'events'|'menus'|'templates';
export const HALL_TAB_IDS:readonly HallTab[]=['enquiries','quotations','calendar','events','menus','templates'];
export const HALL_TAB_LABEL:Record<HallTab,string>={enquiries:'Enquiries',quotations:'Quotations',calendar:'Calendar',events:'Events',menus:'Menus',templates:'Templates'};
/** Tabs by role. Owner / Manager / Reception: all six (Templates are read-only below the Owner); Accountant: view only, no enquiries; Cashier and the
 *  operations roles: calendar and events (the Cashier takes advances and settles the bill, the others see the function sheet only). */
const TABS:Partial<Record<Role,HallTab[]>>={
  Owner:[...HALL_TAB_IDS],Manager:[...HALL_TAB_IDS],Reception:[...HALL_TAB_IDS],
  Accountant:['quotations','calendar','events','menus','templates'],
  Cashier:['calendar','events'],Supervisor:['calendar','events'],Housekeeper:['calendar','events'],Maintenance:['calendar','events'],
};
export const hallRoleOf=(s:State,uid:string):Role|undefined=>s.users.find(u=>u.id===uid)?.role;
export const hallCanOpen=(s:State,uid:string)=>{const r=hallRoleOf(s,uid);return !!r&&!!TABS[r];};
export const hallTabsFor=(s:State,uid:string):HallTab[]=>{const r=hallRoleOf(s,uid);return (r&&TABS[r])||[];};
export const hallTabOpen=(s:State,uid:string,tab:string)=>hallTabsFor(s,uid).includes(tab as HallTab);

export type HallUi={role?:Role;seesMoney:boolean;view:boolean;enquiry:boolean;quote:boolean;accept:boolean;advance:boolean;bill:boolean;cancel:boolean;refund:boolean;
  softHold:boolean;venues:boolean;menus:boolean;tnc:boolean;creditNote:boolean;readOnlyNote:string};
export function hallUi(s:State,uid:string):HallUi{
  const role=hallRoleOf(s,uid),has=(l:Role[])=>!!role&&l.includes(role);
  const money=!!role&&hallSeesMoney(role);
  return {role,seesMoney:money,view:hallCanOpen(s,uid),enquiry:has(HALL_ROLES.enquiry),quote:has(HALL_ROLES.quote),accept:has(HALL_ROLES.accept),advance:has(HALL_ROLES.advance),
    bill:has(HALL_ROLES.bill),cancel:has(HALL_ROLES.cancel),refund:has(HALL_ROLES.refund),softHold:has(HALL_ROLES.softHold),venues:has(HALL_ROLES.venues),menus:has(HALL_ROLES.menus),
    tnc:role==='Owner',creditNote:role==='Owner',
    readOnlyNote:!role||!TABS[role]?'Not available for this role.':!money?'Calendar and function sheet only: prices are not shown to this role.':has(HALL_ROLES.quote)?'':'View only.'};
}
