// Read gates (D5, W4 E2E): which server GETs the signed-in user may send, so a loader never fires a read the role does not hold
// (no 403 noise, no "N areas could not load"). Each rule repeats the server's check (Hcc.Application queries + AccessEvaluator):
//   • a VIEW permission (or an action's ":VIEW") is held in any mode (A/L/R/V/M); an action permission only in A/L/R;
//   • a check without a record ("wide") needs a property-scoped grant (P); "any" also accepts a scoped grant (S own shift,
//     A assigned, O own submissions) because the server filters the rows to what the user may see.
// tests/sync-roles.mjs checks every rule against the real server's answers per seeded role (tests/_role-reads.json, written by
// Hcc.Integration.Tests RoleReadMatrixTests) and boots each role against a fake server that answers 403 where the real one does.
// No grant is changed here: a read a role does not hold is simply not sent (role matrix v2 decisions D1–D7 stay with the owner).
import type {SyncMe} from './types';

type Me=Pick<SyncMe,'grants'>;
type Hold={perm:string;view?:boolean;scope?:'wide'|'any'};

/** The server's allow test for one permission (see the header). */
export function holdsGrant(me:Me|null|undefined,h:Hold):boolean{
  if(!me)return false;
  return me.grants.some(g=>g.permissionId===h.perm&&(h.view||'ALR'.includes(String(g.mode||'A').toUpperCase()))&&
    ((h.scope??'wide')==='any'||String(g.scope||'P').toUpperCase()==='P'));
}
const wide=(perm:string,view=false):Hold=>({perm,view,scope:'wide'});
const anyScope=(perm:string,view=false):Hold=>({perm,view,scope:'any'});
const one=(...hs:Hold[])=>(me:Me)=>hs.some(h=>holdsGrant(me,h));
const always=()=>true;

// Server permission per read (module *Permissions classes): RES-001 reservation.view, GST-001 guest.view, HK-001 board, FOL-001 folio.view,
// INV-001 invoice view, SHF-001 shift, RPT-001/002 reports, ADM-* admin, ACC-001 accounting, CMP-001 compliance, FOL-002 POS operate,
// PAY-001 POS settle, GST-002 guest services manage, APR-001 approvals, OTA-001 channels, WEB-001 booking website, GRP-001/004 groups.
const RES=one(wide('PERM-RES-001',true));
const BOARD=one(anyScope('PERM-HK-001',true));
const FINANCE=one(wide('PERM-RPT-002',true));
const STOCK=one(wide('PERM-RPT-002',true),anyScope('PERM-HK-003'));
const POS=one(wide('PERM-FOL-002'));
const GSV=one(wide('PERM-GST-002'));
const ACCOUNTING=one(wide('PERM-ACC-001',true));
const COMPLIANCE=one(wide('PERM-CMP-001'));
const ADMIN=one(wide('PERM-ADM-001'));
const OTA=one(wide('PERM-OTA-001'));
const WEB=one(wide('PERM-WEB-001'));
const YIELD=one(wide('PERM-RPT-001',true));

/** Route pattern (as in tests/_role-reads.json) → may the user send it. */
export const READS={
  'GET /me/capabilities':always,
  'GET /closing/business-date':always,
  'GET /closing/records':FINANCE,
  'GET /reservations':RES,
  'GET /reservations/:id':RES,
  'GET /guests':one(wide('PERM-GST-001',true)),
  'GET /availability':RES,
  'GET /rooms/board':BOARD,
  'GET /rooms/status':RES,
  'GET /housekeeping/tasks':BOARD,
  'GET /folios/by-reservation/:id':one(wide('PERM-FOL-001',true)),
  'GET /invoices/by-source/folio/:id':one(wide('PERM-INV-001',true)),
  'GET /credit-note-requests':one(wide('PERM-INV-001',true)),
  'GET /cashier/desk':one(anyScope('PERM-SHF-001')),
  'GET /cashier/folios':one(wide('PERM-FOL-001',true)),
  'GET /cashbook':FINANCE,
  'GET /groups':one(wide('PERM-GRP-001',true)),
  'GET /groups/companies':one(wide('PERM-GRP-001',true)),
  'GET /groups/:id/statement':one(wide('PERM-GRP-004',true)),
  'GET /admin/users':ADMIN,
  'GET /admin/roles':ADMIN,
  'GET /admin/exceptions':ADMIN,
  'GET /admin/audit':one(wide('PERM-ADM-004',true)),
  // ADM-003:VIEW. /me/capabilities lists no V/M cell of an ACTION permission (e.g. Tech admin ADM-003 V), so the Tech admin's view is
  // recognised by ADM-001 (only the Owner and the Tech admin hold either; matrix v1 §5).
  'GET /configuration/requests':one(wide('PERM-ADM-003',true),wide('PERM-ADM-001'),wide('PERM-APR-001')),
  'GET /configuration/history':one(wide('PERM-ADM-003',true),wide('PERM-ADM-001')),
  'GET /configuration/values':always,
  'GET /payroll/staff':always,
  'GET /payroll/board':always,
  'GET /accounting/days':ACCOUNTING,
  'GET /accounting/trial-balance':ACCOUNTING,
  'GET /accounting/mapping':ACCOUNTING,
  'GET /accounting/mapping/history':ACCOUNTING,
  'GET /accounting/locks/history':ACCOUNTING,
  'GET /expenses':one(anyScope('PERM-EXP-001'),anyScope('PERM-EXP-002'),anyScope('PERM-EXP-003')),
  'GET /compliance/obligations':COMPLIANCE,
  'GET /compliance/calendar':COMPLIANCE,
  'GET /compliance/ca-packs':COMPLIANCE,
  'GET /pos/outlets':POS,
  'GET /pos/menu':POS,
  'GET /pos/checks':POS,
  'GET /pos/checks/closed':POS,
  'GET /pos/checks/:id':POS,
  'GET /pos/void-requests':POS,
  'GET /pos/in-house':POS,
  'GET /pos/till':one(anyScope('PERM-PAY-001')),
  'GET /maintenance/registers':BOARD,
  'GET /maintenance/work-orders':BOARD,
  'GET /maintenance/work-orders/:id':BOARD,
  'GET /maintenance/amcs':one(wide('PERM-HK-004')),
  'GET /stock/items':STOCK,
  'GET /stock/laundry':STOCK,
  'GET /stock/counts':STOCK,
  'GET /stock/losses':STOCK,
  'GET /stock/moves':STOCK,
  'GET /stock/settings':STOCK,
  'GET /guest-services/requests':one(wide('PERM-GST-002'),anyScope('PERM-HK-002'),anyScope('PERM-HK-003'),anyScope('PERM-ROOM-001')),
  'GET /guest-services/complaints':GSV,
  'GET /guest-services/lost-items':one(wide('PERM-GST-002'),anyScope('PERM-HK-002'),anyScope('PERM-HK-003'),anyScope('PERM-ROOM-001')),
  'GET /guest-services/settings':one(wide('PERM-GST-002'),wide('PERM-APR-001')),
  'GET /guest-services/stays':GSV,
  'GET /guest-services/histories/:kind':GSV,
  'GET /handover/checklists':one(anyScope('PERM-HK-001',true),anyScope('PERM-PAY-001'),wide('PERM-APR-001')),
  'GET /handover/templates':one(wide('PERM-APR-001')),
  'GET /handover/notes':one(anyScope('PERM-HK-001',true),anyScope('PERM-PAY-001'),wide('PERM-APR-001')),
  'GET /messaging/messages':RES,
  'GET /messaging/templates':RES,
  'GET /messaging/rules':RES,
  'GET /messaging/consents':RES,
  'GET /ota/channels':OTA,
  'GET /ota/messages':OTA,
  'GET /ota/pushes':OTA,
  'GET /web-booking/requests':WEB,
  'GET /web-booking/requests/:id':WEB,
  'GET /yield/calendar':YIELD,
  'GET /yield/config':YIELD,
  'GET /yield/publishes':YIELD,
  'GET /yield/proposals':YIELD,
} satisfies Record<string,(me:Me)=>boolean>;
export type ReadRoute=keyof typeof READS;

/** True when the user may send every given read (a missing user may send none). */
export function mayRead(me:Me|null|undefined,...routes:ReadRoute[]):boolean{
  return !!me&&routes.every(r=>READS[r](me));
}
