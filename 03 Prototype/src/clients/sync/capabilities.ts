// GET /me/capabilities → the prototype's can() permissions (W4-0). The server re-checks every request; this only drives
// menus and buttons. Grant modes (RBAC matrix): A allow · L allow within a limit · R request only (needs approval) ·
// V view only · M masked view. Seed: 08 Production/database/migrations/0101_access_rbac_seed.sql (+ module seeds).
import type {State,Permission,Role} from '../../services/mock';
import type {SrvCapabilities} from '../server';
import type {SyncMe} from './types';
import {protoUserId} from './map';

/** Prototype permission ← any of these server PERM ids, held in one of `modes`. */
export type PermissionRule={any:string[];modes:string};
export const PERMISSION_MAP:Record<Permission,PermissionRule>={
  reservations:{any:['PERM-RES-001','PERM-RES-002'],modes:'ALR'},
  checkin:{any:['PERM-STY-001','PERM-GST-002'],modes:'AL'},
  billing:{any:['PERM-FOL-002'],modes:'ALR'},
  payments:{any:['PERM-PAY-001'],modes:'AL'},
  checkout:{any:['PERM-STY-003'],modes:'AL'},
  housekeeping:{any:['PERM-HK-001'],modes:'ALV'},
  inspect:{any:['PERM-HK-004'],modes:'AL'},
  expenses:{any:['PERM-EXP-001'],modes:'AL'},
  approve:{any:['PERM-APR-001'],modes:'AL'},
  close:{any:['PERM-CLS-002'],modes:'AL'},
  reports:{any:['PERM-RPT-001','PERM-RPT-002'],modes:'ALV'},
  admin:{any:['PERM-ADM-001','PERM-ADM-002','PERM-ADM-003'],modes:'ALR'},
};
/** Known differences between the server seed and the prototype's coarse grants (documented, owner decision D1–D7 pending). */
export const CAPABILITY_GAPS:string[]=[
  'Accountant gets "expenses" (server PERM-EXP-001:A); the prototype Accountant has reports only.',
  'Cashier gets "reports" through PERM-RPT-002:V (finance view) — same as the prototype; Accountant/Manager/Owner through PERM-RPT-001.',
  '"approve" follows PERM-APR-001 only (Owner A, Manager L). Expense approval (PERM-EXP-002) and shift-difference approval (PERM-SHF-002) are re-checked by the server per command; Accountant does not get the prototype "approve" gate.',
  'Request-only grants (mode R, e.g. Reception PERM-FOL-005 credit note, PERM-PAY-003 refund) show the button; the server answers APPROVAL_REQUIRED when an approval is needed.',
  'Limits (mode L: Manager discount/rate BPS, approval amounts) are enforced by the server; the client shows the action and maps the refusal.',
  'Module PERM ids without a coarse prototype permission (PERM-ACC/CMP/CRM/DOC/GRP/HR/INV/OTA/SOD/WEB, PERM-HOV-*, PERM-MNT-*, PERM-POS*) do not change can(); module screens keep their existing gates. The fine-grained s.policy (HccPolicy) is not derived from the server yet (W4-5).',
];

/** Server role code → prototype role name (first role is the primary one shown in the header). */
export const ROLE_MAP:Record<string,Role>={OWN:'Owner',MGR:'Manager',REC:'Reception',CSH:'Cashier',SUP:'Supervisor',HKP:'Housekeeper',ACC:'Accountant',MNT:'Maintenance',TAD:'Tech admin'};

export function permissionsFromGrants(grants:{permissionId:string;mode:string}[]):Permission[]{
  return (Object.keys(PERMISSION_MAP) as Permission[]).filter(p=>{const r=PERMISSION_MAP[p];
    return grants.some(g=>r.any.includes(g.permissionId)&&r.modes.includes(String(g.mode||'A').toUpperCase()));});
}
export function meFromCapabilities(c:SrvCapabilities):SyncMe{
  const grants=c.permissions??[];
  const role=(c.roles??[]).map(r=>ROLE_MAP[r]).find(Boolean)??'Reception';
  return {userId:c.userId,uid:protoUserId(c.userId),displayName:c.displayName,role,roles:[...(c.roles??[])],permIds:[...new Set(grants.map(g=>g.permissionId))],
    permissions:permissionsFromGrants(grants),grants:grants.map(g=>({...g})),fieldMasks:{...(c.fieldMasks??{})}};
}
/** Puts the signed-in user into the assembled state so the unchanged can()/ctx.permission/ctx.user work: an enabled user record
 *  with the user's prototype role, and that role's grants = the mapped capabilities (applied last, over any loaded s.grants). */
export function overlayMe(s:State,me:SyncMe|null):State{
  if(!me)return s;
  const users=s.users.filter(u=>u.id!==me.uid);
  const prev=s.users.find(u=>u.id===me.uid);
  users.push({...(prev??{}),id:me.uid,name:me.displayName,role:me.role,enabled:true,expiresOn:undefined} as State['users'][number]);
  return {...s,users,grants:{...s.grants,[me.role]:[...me.permissions]}};
}
