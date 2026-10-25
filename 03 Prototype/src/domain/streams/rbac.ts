// DEC-063 (25 Oct 2026) · RBAC rulings G1–G13: role rules the 12 coarse permissions cannot express on their own.
// Commands (services/mock) and screens both read these, so a hidden button is also a refused command.
// Source: 02 Planning/RBAC_Mismatch_Rulings_Proposed.md (accepted in full) · HCC_RBAC_Matrix_v1.md (revised 25 Oct 2026).
import type {State,Role} from '../types';
import {can} from '../selectors';
import {adminPolicy,adminLimitFor} from './admin';

const roleOf=(s:State,uid:string):Role|undefined=>s.users.find(u=>u.id===uid)?.role;
/** Enabled, unexpired user holding one of the roles. */
export function rbacIsRole(s:State,uid:string,...roles:Role[]){
  const u=s.users.find(x=>x.id===uid);
  return !!u&&u.enabled&&!(u.expiresOn&&s.date>u.expiresOn)&&roles.includes(u.role);
}
/** The Accountant: independent checker of cash (G4). */
export const rbacIsAccountant=(s:State,uid:string)=>rbacIsRole(s,uid,'Accountant');

// ── G1 · discounts (FOL-003, §6, APR-001) ──
/** Who may post or request a discount: billing plus front-desk or approval power (Owner, Manager, Reception — never the Cashier). */
export const rbacCanDiscount=(s:State,uid:string)=>can(s,uid,'billing')&&can(s,uid,['checkin','approve']);
/** Discount the user may post without approval (paise); undefined = no limit (template cell A, i.e. Owner). */
export function rbacDiscountLimit(s:State,uid:string):number|undefined{
  const role=roleOf(s,uid);if(!role)return 0;
  const cell=adminPolicy(s).templates[role]?.['PERM-FOL-003'];
  if(cell?.effect==='allow')return undefined;
  return adminLimitFor(s,role,'PERM-FOL-003')??s.settings.discountLimit;
}
/** Largest discount request the user may approve (paise); undefined = no limit (Owner). Manager ≤ ₹1,000 (APR-001 = L). */
export function rbacApprovalLimit(s:State,uid:string):number|undefined{
  const role=roleOf(s,uid);if(!role)return 0;
  const cell=adminPolicy(s).templates[role]?.['PERM-APR-001'];
  if(!cell||cell.effect==='allow')return undefined;
  return adminLimitFor(s,role,'PERM-APR-001')??0;
}

// ── G3 · corrections and credit notes (FOL-005): Reception, Cashier, Accountant, Manager ask; Owner approves ──
export const rbacCanRequestCorrection=(s:State,uid:string)=>can(s,uid,'billing')||rbacIsAccountant(s,uid);
/** Folio request kinds only an Owner decides. */
export const RBAC_OWNER_DECIDES=['correction','credit-note'];

// ── G4 · Accountant back-office checks ──
/** Folio list and folio sheet (read-only for the Accountant: FOL-001 = V). */
export const rbacCanViewFolios=(s:State,uid:string)=>can(s,uid,['billing','reservations'])||rbacIsAccountant(s,uid);
/** Day closing page (read-only for the Accountant: CLS-001 = V). */
export const rbacCanViewClosing=(s:State,uid:string)=>can(s,uid,'close')||rbacIsAccountant(s,uid);
/** Submit an own expense (EXP-001). Supervisor / Maintenance hand bills to Reception or Cashier (G6). */
export const rbacCanSubmitExpense=(s:State,uid:string)=>can(s,uid,'expenses')||rbacIsAccountant(s,uid);
/** Approve / reject and reverse expenses (EXP-002/003). Not the Cashier (G5). */
export const rbacCanDecideExpense=(s:State,uid:string)=>can(s,uid,'approve')||rbacIsAccountant(s,uid);
/** Accept or reject a cash difference (SHF-002; the counter never decides their own, SOD-004). */
export const rbacCanDecideCash=(s:State,uid:string)=>can(s,uid,'approve')||rbacIsAccountant(s,uid);
/** Request a closed-date correction (CLS-003); the Owner decides (CLS-004). */
export const rbacCanRequestClosedCorrection=(s:State,uid:string)=>can(s,uid,'close')||rbacIsAccountant(s,uid);

// ── G9 · Manager: no Access control / setting requests, but a last-backup warning (Owner + Manager) ──
export const rbacSeesBackupStatus=(s:State,uid:string)=>rbacIsRole(s,uid,'Owner','Manager');

// ── G10 · Approvals & audit: Owner, Manager, Accountant (Tech admin view at M4) ──
export const rbacSeesApprovals=(s:State,uid:string)=>rbacIsRole(s,uid,'Owner','Manager','Accountant');

// ── G11 · money settings of the Tech admin's reach: ledger mapping and company credit limit = Owner (admin + reports) ──
export const rbacMoneyAdmin=(s:State,uid:string)=>can(s,uid,'admin')&&can(s,uid,'reports');

// ── G12 · AMC contracts by maintenance roles with money (inspect + expenses: Owner, Manager) ──
export const rbacCanSaveAmc=(s:State,uid:string)=>can(s,uid,'inspect')&&can(s,uid,'expenses');

// ── G13 · exports: Tally tab Owner + Accountant; report export (RPT-003) Owner, Manager, Accountant ──
export const rbacCanTally=(s:State,uid:string)=>can(s,uid,'reports')&&rbacIsRole(s,uid,'Owner','Accountant');
/** Accounting voucher CSV / Excel export: same RPT-003 rule, Cashier cannot export (DEC-063 G13); Owner, Manager, Accountant can. */
export const rbacCanExportVouchers=(s:State,uid:string)=>can(s,uid,'reports')&&!rbacIsRole(s,uid,'Cashier');
// ── G4 · Day pack (printed day summary): follows the read-only Day closing view, deliberately including the Accountant ──
export const rbacCanDayPack=(s:State,uid:string)=>can(s,uid,'close')||rbacIsAccountant(s,uid);
export const rbacCanExportReports=(s:State,uid:string)=>can(s,uid,'reports')&&!rbacIsRole(s,uid,'Cashier');
