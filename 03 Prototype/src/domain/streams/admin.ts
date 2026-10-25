// Stream E · Access & configuration (owns USER, GRANT, SETTINGS) — pure domain helpers and selectors (re-exported by services/mock/index.ts for UI and tests).
// Fine-grained policy layer (BL-024, RBAC matrix v1, DEC-040), approvals inbox (BL-026) and configuration selectors (BL-027).
import type {State,Role,User,Booking,Approval,Audit,Permission} from '../types';
import {money} from '../money';

// ───────────────────────────── Policy model ─────────────────────────────
export type PolicyEffect='allow'|'deny'|'limit'|'request';
/** P = whole property · S = own cash shift · A = assigned records · O = own submissions (RBAC §4). */
export type PolicyScope='P'|'S'|'A'|'O';
/** One role-template cell. A missing cell = no grant ("—", default deny; an exception may add an allow). */
export type PolicyCell={effect:PolicyEffect;scope:PolicyScope;mode?:'view'|'masked'};
export type PolicyException={id:string;user:string;perm:string;effect:'allow'|'deny';reason:string;expires:string;by?:string;at?:string};
export type PolicyChange={version:number;at:string;actor:string;summary:string;before:string;after:string;reason:string};
export type HccPolicy={version:number;templates:Record<Role,Record<string,PolicyCell>>;limits:Record<Role,Record<string,number>>;exceptions:PolicyException[];history:PolicyChange[]};
export type AuthDecision={decision:'allow'|'deny'|'approval';reason:string;rule:string};
/** act: the caller performs the action (not just views) — view-only cells then deny. */
export type AuthContext={amount?:number;ownerId?:string;shiftUser?:string;assignee?:string;act?:boolean};

export type AdminPerm={id:string;action:string;group:string;financial:boolean;module?:Permission;limit?:{unit:'paise'|'percent';label:string}};
const ROLE_ORDER:Role[]=['Owner','Manager','Reception','Cashier','Accountant','Supervisor','Housekeeper','Maintenance','Tech admin'];
// Columns: OWN MGR REC CSH ACC SUP HKP MNT TAD (RBAC matrix §5, verbatim).
const MATRIX:[string,string,string,boolean,Permission|undefined,string][]=[
  ['PERM-RES-001','reservation.view','Reservations',false,'reservations','A A A — — — — — —'],
  ['PERM-RES-002','reservation.create / hold / confirm','Reservations',false,'reservations','A A A — — — — — —'],
  ['PERM-RES-003','reservation.edit / change dates / move','Reservations',false,'reservations','A A A — — — — — —'],
  ['PERM-RES-004','reservation.cancel / no-show','Reservations',false,'reservations','A A A — — — — — —'],
  ['PERM-RATE-001','rate.override (vs tariff)','Reservations',true,'reservations','A L R — — — — — —'],
  ['PERM-GST-001','guest.view','Guests & stays',false,'checkin','A A A M — — — — —'],
  ['PERM-GST-002','guest.edit / register','Guests & stays',false,'checkin','A A A — — — — — —'],
  ['PERM-GST-003','guest.idDetails.view (field)','Guests & stays',false,'checkin','A A A — — — — — —'],
  ['PERM-STY-001','stay.checkIn','Guests & stays',false,'checkin','A A A — — — — — —'],
  ['PERM-STY-002','stay.move / extend','Guests & stays',false,'reservations','A A A — — — — — —'],
  ['PERM-STY-003','stay.checkOut','Guests & stays',false,'checkout','A A A — — — — — —'],
  ['PERM-FOL-001','folio.view','Folio',true,'billing','A A A A V — — — —'],
  ['PERM-FOL-002','folio.charge.post (service)','Folio',true,'billing','A A A A — — — — —'],
  ['PERM-FOL-003','folio.discount','Folio',true,'billing','A L L — — — — — —'],
  ['PERM-FOL-004','folio.invoice.issue / print','Folio',true,'billing','A A A A V — — — —'],
  ['PERM-FOL-005','folio.correction / credit note','Folio',true,'billing','A R R R R — — — —'],
  ['PERM-PAY-001','payment.record (own shift)','Payments',true,'payments','A A A·S A·S — — — — —'],
  ['PERM-PAY-002','deposit.record / return','Payments',true,'payments','A A A·S A·S — — — — —'],
  ['PERM-PAY-003','refund.request','Payments',true,'payments','A A R R — — — — —'],
  ['PERM-PAY-004','refund.execute (approved only)','Payments',true,'payments','A A A·S A·S — — — — —'],
  ['PERM-PAY-005','companyCredit.authorise','Payments',true,'checkout','A L — — — — — — —'],
  ['PERM-SHF-001','shift.open / count / submit','Cash shift',true,'close','A A A·S A·S — — — — —'],
  ['PERM-SHF-002','shift.difference.approve','Cash shift',true,'approve','A L — — A — — — —'],
  ['PERM-HK-001','hk.view board','Housekeeping',false,'housekeeping','A A — — — A A·A A·A —'],
  ['PERM-HK-002','hk.assign','Housekeeping',false,'inspect','A A — — — A — — —'],
  ['PERM-HK-003','hk.task.work (start/check/complete)','Housekeeping',false,'housekeeping','A A — — — A A·A — —'],
  ['PERM-HK-004','hk.inspect (pass/reject)','Housekeeping',false,'inspect','A A — — — A — — —'],
  ['PERM-ROOM-001','room.block.request','Housekeeping',false,'housekeeping','A A A — — A A·A A·A —'],
  ['PERM-ROOM-002','room.block.apply / unblock','Housekeeping',false,'inspect','A A — — — A — — —'],
  ['PERM-EXP-001','expense.submit','Expenses',true,'expenses','A A A·O A·O A·O — — — —'],
  ['PERM-EXP-002','expense.approve / reject','Expenses',true,'approve','A L — — A — — — —'],
  ['PERM-EXP-003','expense.post / reverse','Expenses',true,'expenses','A A — — A — — — —'],
  ['PERM-CLS-001','close.preflight.view','Closing',true,'close','A A — — V — — — —'],
  ['PERM-CLS-002','close.nightPost / closeDay / openNextDay','Closing',true,'close','A A — — — — — — —'],
  ['PERM-CLS-003','close.correction.request','Closing',true,'close','A A — — A — — — —'],
  ['PERM-CLS-004','close.correction.approve','Closing',true,'close','A — — — — — — — —'],
  ['PERM-APR-001','approval.decide (discount, rate, refund, credit)','Approvals',true,'approve','A L — — — — — — —'],
  ['PERM-RPT-001','report.operations','Reports',false,'reports','A A — — A — — — —'],
  ['PERM-RPT-002','report.finance','Reports',true,'reports','A A — V·S A — — — —'],
  ['PERM-RPT-003','report.export','Reports',true,'reports','A A — — A — — — —'],
  ['PERM-ADM-001','admin.users (create/enable/disable/assign)','Administration',false,'admin','A — — — — — — — A'],
  ['PERM-ADM-002','admin.roles (edit templates)','Administration',false,'admin','A — — — — — — — R'],
  ['PERM-ADM-003','admin.configuration','Administration',false,'admin','A — — — — — — — V'],
  ['PERM-ADM-004','audit.view','Administration',false,undefined,'A A — — A — — — —'],
  ['PERM-ADM-005','backup.view / run (simulated)','Administration',false,'admin','A — — — — — — — A'],
];
const LIMIT_META:Record<string,{unit:'paise'|'percent';label:string}>={
  'PERM-FOL-003':{unit:'paise',label:'Discount without approval (per request)'},
  'PERM-RATE-001':{unit:'percent',label:'Rate override below tariff'},
  'PERM-SHF-002':{unit:'paise',label:'Cash difference acceptance'},
  'PERM-PAY-005':{unit:'paise',label:'Company credit at checkout (per stay)'},
  'PERM-EXP-002':{unit:'paise',label:'Expense approval'},
  'PERM-APR-001':{unit:'paise',label:'Approval decision (discount / rate / refund / credit)'},
};
export const ADMIN_PERMS:AdminPerm[]=MATRIX.map(([id,action,group,financial,module])=>({id,action,group,financial,module,limit:LIMIT_META[id]}));
export const adminPerm=(id:string)=>ADMIN_PERMS.find(p=>p.id===id);
export const ADMIN_ROLE_ORDER=ROLE_ORDER;
/** Coarse module permissions that carry financial power (Tech admin may never hold them; plan §12). */
export const ADMIN_FINANCIAL_MODULES:Permission[]=['billing','payments','checkout','expenses','approve','close'];

function parseCell(code:string):PolicyCell|undefined{
  const [k,scope]=code.split('·') as [string,PolicyScope|undefined];
  const sc:PolicyScope=scope||'P';
  switch(k){case 'A':return {effect:'allow',scope:sc};case 'L':return {effect:'limit',scope:sc};case 'R':return {effect:'request',scope:sc};
    case 'V':return {effect:'allow',scope:sc,mode:'view'};case 'M':return {effect:'allow',scope:sc,mode:'masked'};default:return undefined;}
}
/** RBAC matrix v1 baseline (version 1). Tech admin's financial cells are explicit denies (separation of duty). */
export function adminDefaultPolicy(at='2026-10-24T08:00'):HccPolicy{
  const templates={} as Record<Role,Record<string,PolicyCell>>;
  ROLE_ORDER.forEach(r=>templates[r]={});
  for(const [id,,,financial,,codes] of MATRIX){
    codes.split(' ').forEach((c,i)=>{const role=ROLE_ORDER[i];const cell=parseCell(c);
      if(cell)templates[role][id]=cell;else if(role==='Tech admin'&&financial)templates[role][id]={effect:'deny',scope:'P'};});
  }
  const limits={} as Record<Role,Record<string,number>>;ROLE_ORDER.forEach(r=>limits[r]={});
  // §6 demonstration defaults (paise; rate override in percent). Reception's discount limit follows Hotel configuration → discount threshold.
  limits.Manager={'PERM-FOL-003':100000,'PERM-RATE-001':15,'PERM-SHF-002':20000,'PERM-PAY-005':1000000,'PERM-EXP-002':500000,'PERM-APR-001':100000};
  return {version:1,templates,limits,exceptions:[],history:[{version:1,at,actor:'System',summary:'RBAC matrix v1 baseline loaded (Proposed demonstration templates)',before:'—',after:'9 role templates · 45 permissions',reason:'Fixture'}]};
}
export const adminPolicy=(s:State):HccPolicy=>s.policy??adminDefaultPolicy(s.clock);

// ───────────────────────────── Users ─────────────────────────────
export const adminUserExpired=(s:State,u:User)=>!!u.expiresOn&&s.date>u.expiresOn;
export const adminUserActive=(s:State,u:User)=>u.enabled&&!adminUserExpired(s,u);
export function adminUserStatus(s:State,u:User):'Enabled'|'Disabled'|'Expired'{return !u.enabled?'Disabled':adminUserExpired(s,u)?'Expired':'Enabled';}
/** Active (enabled, unexpired) owners other than `except`. */
export const adminOtherActiveOwners=(s:State,except:string)=>s.users.filter(u=>u.id!==except&&u.role==='Owner'&&adminUserActive(s,u));
export const adminExceptionActive=(s:State,e:PolicyException)=>!e.expires||s.date<=e.expires;

// ───────────────────────────── Evaluation (DEC-040) ─────────────────────────────
const fmtLimit=(perm:string,n:number)=>LIMIT_META[perm]?.unit==='percent'?n+'%':money(n);
/** The limit applying to a role for a limit-type cell. Reception's discount limit is the hotel discount threshold (SCR-18). */
export function adminLimitFor(s:State,role:Role,perm:string):number|undefined{
  const v=adminPolicy(s).limits[role]?.[perm];
  if(v!==undefined)return v;
  if(perm==='PERM-FOL-003')return s.settings.discountLimit;
  return undefined;
}
const scopeText:Record<PolicyScope,string>={P:'property-wide',S:'own cash shift only',A:'assigned records only',O:'own submissions only'};
export const adminScopeText=scopeText;
/**
 * Fine-grained authorisation over the RBAC matrix. Order: disabled/expired user → scope → explicit deny (role or
 * exception; deny wins) → no allow (default deny) → limit (above → approval) → allow. One role per user.
 * The coarse module gate (s.grants) is checked separately by commands (`can()`).
 */
export function authorize(s:State,uid:string,permId:string,ctx:AuthContext={}):AuthDecision{
  const u=s.users.find(x=>x.id===uid);
  const p=adminPerm(permId);
  if(!u)return {decision:'deny',reason:'Denied: unknown user.',rule:'Unknown user'};
  if(!p)return {decision:'deny',reason:`Denied: ${permId} is not in the access matrix.`,rule:'Unknown permission'};
  const name=u.name.split(' · ')[0];
  if(!u.enabled)return {decision:'deny',reason:`Denied: ${name}'s account is disabled.`,rule:'PERM-SOD-005 · user disabled'};
  if(adminUserExpired(s,u))return {decision:'deny',reason:`Denied: ${name}'s access expired on ${u.expiresOn}.`,rule:'User assignment expired'};
  const pol=adminPolicy(s);
  const cell=pol.templates[u.role]?.[permId];
  const exc=pol.exceptions.filter(e=>e.user===uid&&e.perm===permId&&adminExceptionActive(s,e));
  const excDeny=exc.find(e=>e.effect==='deny'),excAllow=exc.find(e=>e.effect==='allow');
  // 2. Scope of the applicable grant (an allow exception is property-wide).
  const grant:{scope:PolicyScope}|undefined=excAllow?{scope:'P'}:cell&&cell.effect!=='deny'?cell:undefined;
  if(grant&&grant.scope!=='P'){
    const other=grant.scope==='S'?ctx.shiftUser:grant.scope==='A'?ctx.assignee:ctx.ownerId;
    if(other!==undefined&&other!==uid){
      const what=grant.scope==='S'?'Outside your own cash shift':grant.scope==='A'?'Outside your assigned rooms and tasks':'Only your own submissions';
      return {decision:'deny',reason:`Denied: ${what} — role ${u.role} has ${p.action} for ${scopeText[grant.scope]}.`,rule:`Scope ${grant.scope} · role ${u.role}`};
    }
  }
  // 3. Explicit deny wins.
  if(excDeny)return {decision:'deny',reason:`Denied: user exception “${excDeny.reason}” (until ${excDeny.expires}) denies ${p.action}. Deny overrides any allow.`,rule:`Exception ${excDeny.id} · deny`};
  if(cell?.effect==='deny')return {decision:'deny',reason:`Denied: role ${u.role} explicitly denies ${p.action}${u.role==='Tech admin'&&p.financial?' (technical and financial power are kept separate)':''}.`,rule:`Role ${u.role} · explicit deny`};
  // An allow exception grants property-wide allow (no limit).
  if(excAllow)return {decision:'allow',reason:`Allowed: user exception “${excAllow.reason}” (until ${excAllow.expires}) grants ${p.action}.`,rule:`Exception ${excAllow.id} · allow`};
  // 4. Default deny.
  if(!cell)return {decision:'deny',reason:`Denied: role ${u.role} has no grant for ${p.action}.`,rule:'Default deny · no grant'};
  const scoped=cell.scope==='P'?'':` (${scopeText[cell.scope]})`;
  if(cell.effect==='request')return {decision:'approval',reason:`Approval required: role ${u.role} may only request ${p.action}; another authorised user must approve.`,rule:`Role ${u.role} · request only`};
  // 5. Limits.
  if(cell.effect==='limit'){
    const lim=adminLimitFor(s,u.role,permId);
    if(lim===undefined)return {decision:'approval',reason:`Approval required: no limit is configured for role ${u.role}, so every ${p.action} routes for approval.`,rule:`Role ${u.role} · limit not set`};
    if(ctx.amount!==undefined&&ctx.amount>lim)return {decision:'approval',reason:`Approval required: ${fmtLimit(permId,ctx.amount)} is above the ${u.role} limit of ${fmtLimit(permId,lim)}.`,rule:`Limit · ${u.role} ${permId} ≤ ${fmtLimit(permId,lim)}`};
    return {decision:'allow',reason:ctx.amount!==undefined?`Allowed: ${fmtLimit(permId,ctx.amount)} is within the ${u.role} limit of ${fmtLimit(permId,lim)}.`:`Allowed up to ${fmtLimit(permId,lim)} for role ${u.role}; above that, approval is required.`,rule:`Limit · ${u.role} ${permId} ≤ ${fmtLimit(permId,lim)}`};
  }
  if(ctx.act&&cell.mode==='view')return {decision:'deny',reason:`Denied: role ${u.role} has view-only access to ${p.action}.`,rule:`Role ${u.role} · view only`};
  const mode=cell.mode==='view'?' — view only':cell.mode==='masked'?' — with masked fields':'';
  return {decision:'allow',reason:`Allowed: role ${u.role} grants ${p.action}${scoped}${mode}.`,rule:`Role ${u.role} · allow${cell.scope==='P'?'':' · scope '+cell.scope}`};
}
/** Effective access for one user: every PERM with its decision and deciding rule (SCR-16 tab 3, AC-035). */
export function adminEffectiveAccess(s:State,uid:string,ctx:AuthContext={}){
  const u=s.users.find(x=>x.id===uid);
  return ADMIN_PERMS.map(p=>({perm:p,...authorize(s,uid,p.id,p.limit?ctx:{}),module:p.module,moduleOn:!!u&&!!p.module&&(s.grants[u.role]||[]).includes(p.module)}));
}
/** Short matrix code for a cell, for the role-template grid. */
export function adminCellCode(c:PolicyCell|undefined){
  if(!c)return '—';
  const k=c.effect==='deny'?'D':c.effect==='limit'?'L':c.effect==='request'?'R':c.mode==='view'?'V':c.mode==='masked'?'M':'A';
  return c.scope==='P'?k:k+'·'+c.scope;
}
export function adminDescribeCell(c:PolicyCell|undefined){
  if(!c)return 'not granted';
  const e=c.effect==='allow'?(c.mode==='view'?'view only':c.mode==='masked'?'masked view':'allow'):c.effect;
  return e+(c.scope==='P'?'':' · '+c.scope);
}

/** Audit before/after values are visible to Owner, Manager and Accountant; others see action + time only (RBAC §9). */
export function adminAuditDetailVisible(s:State,uid:string){
  const u=s.users.find(x=>x.id===uid);
  return !!u&&authorize(s,uid,'PERM-ADM-004').decision==='allow'&&['Owner','Manager','Accountant'].includes(u.role);
}

// ───────────────────────────── Approvals inbox (BL-026) ─────────────────────────────
export type AdminInboxStatus=Approval['status'];
export type AdminInboxRow={a:Approval;status:AdminInboxStatus;derived?:string;kind:string;target:string;command:string;requester:string};
/** Derives Expired (clock past expiresAt) and Invalidated (target version changed) for still-Pending requests. */
export function adminApprovalInbox(s:State):AdminInboxRow[]{
  const pol=adminPolicy(s);
  return s.approvals.map(a=>{
    let status:AdminInboxStatus=a.status,derived:string|undefined;
    if(a.status==='Pending'){
      if(a.expiresAt&&s.clock>a.expiresAt){status='Expired';derived=`Expired at ${a.expiresAt.replace('T',' ')} (simulated clock)`;}
      else if(a.kind==='policy'){if(a.version!==pol.version){status='Invalidated';derived=`Policy changed after this request (v${a.version} → v${pol.version})`;}}
      else if(!a.kind||a.kind==='discount'){const b=s.bookings.find(b=>b.id===a.booking);
        if(!b){status='Invalidated';derived='Booking no longer exists';}
        else if(b.version!==a.version){status='Invalidated';derived=`Folio changed after request (v${a.version} → v${b.version})`;}
        else if(!['Confirmed','In house'].includes(b.status)){status='Invalidated';derived='Booking is no longer open';}}
    }
    const requester=s.users.find(u=>u.id===a.actor)?.name.split(' · ')[0]||a.actor;
    return {a,status,derived,kind:a.kind||'discount',target:a.target||a.booking,command:String(a.meta?.command||'APPROVAL'),requester};
  });
}
export const ADMIN_INBOX_TABS:AdminInboxStatus[]=['Pending','Approved','Rejected','Expired','Invalidated','Executed'];
/** Why the current user may not decide a request (undefined = may decide). */
export function adminDecideBlock(s:State,uid:string,row:AdminInboxRow):string|undefined{
  const u=s.users.find(x=>x.id===uid);
  if(!u||!adminUserActive(s,u))return 'Your account is not active.';
  if(row.a.actor===uid)return 'You raised this request — another authorised user must decide it (PERM-SOD-001).';
  if(row.kind==='policy'){const d=authorize(s,uid,'PERM-ADM-002');if(d.decision!=='allow')return 'Only a user allowed to edit role templates (Owner) can decide policy requests. '+d.reason;}
  // W05-A Master configuration requests (CFG_DECIDE): decided by the setting's approver role or the Owner (DEC-058).
  else if(row.kind==='config'){const ap=String(row.a.meta?.approver||'Owner');if(u.role!=='Owner'&&!(ap==='Manager'&&u.role==='Manager'))return ap==='Manager'?'Only a Manager or the Owner can decide this configuration request.':'Only the Owner can decide this configuration request.';}
  // DEC-063 G4: the Accountant decides cash differences (no 'approve' grant needed). G3: corrections / credit notes and closed-date
  // corrections are approved by an Owner. G1: a Manager approves discounts only up to the Manager limit (APR-001 = L).
  else if(row.kind==='cash-difference'&&u.role==='Accountant')return undefined;
  else if(!(s.grants[u.role]||[]).includes('approve'))return `Role ${u.role} lacks the approve permission.`;
  else if(['correction','credit-note','closed-date-correction'].includes(row.kind)&&u.role!=='Owner')return 'Only an Owner can approve this request.';
  else if(row.kind==='discount'&&adminPolicy(s).templates[u.role]?.['PERM-APR-001']?.effect==='limit'){
    const lim=adminLimitFor(s,u.role,'PERM-APR-001')??0;if(row.a.amount>lim)return `Above the ${u.role} approval limit of ${money(lim)} — an Owner approves.`;}
  return undefined;
}
/** Why the current user may not decide a pending expense (undefined = may decide). DEC-063 G4: approve holders and the Accountant. */
export function adminExpenseBlock(s:State,uid:string,actor:string):string|undefined{
  const u=s.users.find(x=>x.id===uid);
  if(!u||!adminUserActive(s,u))return 'Your account is not active.';
  if(actor===uid)return 'You submitted this expense — another user must decide it.';
  if(u.role!=='Accountant'&&!(s.grants[u.role]||[]).includes('approve'))return `Role ${u.role} lacks the approve permission.`;
  return undefined;
}

// ───────────────────────────── Audit log (SCR-17) ─────────────────────────────
export type AdminAuditFilter={actor?:string;action?:string;text?:string;from?:string;to?:string};
export function adminAuditFilter(s:State,f:AdminAuditFilter):Audit[]{
  const t=(f.text||'').trim().toLowerCase();
  return s.audit.filter(a=>(!f.actor||a.actor===f.actor)&&(!f.action||a.action===f.action)&&(!t||`${a.action} ${a.detail} ${a.actor}`.toLowerCase().includes(t))&&(!f.from||a.at.slice(0,10)>=f.from)&&(!f.to||a.at.slice(0,10)<=f.to));
}

// ───────────────────────────── Configuration (BL-027) ─────────────────────────────
export type AdminSettingKey='hotel'|'invoicePrefix'|'checkin'|'checkout'|'discountLimit'|'approvalExpiryHours'|'inspection';
export type AdminSettingDef={key:string;category:string;label:string;type:'text'|'time'|'money'|'boolean'|'integer'|'percent'|'fixed';unit:string;editable:boolean;approval:string;effective:string;help:string};
export const ADMIN_CONFIG_CATEGORIES=['Property','Rooms & tariffs','Stay timings','Finance & synthetic tax','Approvals/limits','Housekeeping','Shifts','Documents'] as const;
const OWNER='Owner (admin.configuration, PERM-ADM-003); Manager tells the Owner (DEC-058, DEC-063)';
export const ADMIN_SETTINGS:AdminSettingDef[]=[
  {key:'hotel',category:'Property',label:'Property display name',type:'text',unit:'text, ≤ 60 chars',editable:true,approval:OWNER,effective:'Immediately',help:'Shown in the header, sidebar and documents.'},
  {key:'rooms',category:'Property',label:'Rooms in inventory',type:'fixed',unit:'rooms',editable:false,approval:'Fixture (rooms are managed by housekeeping & rooms)',effective:'—',help:'Room count and types come from the demo fixture.'},
  {key:'checkin',category:'Stay timings',label:'Standard check-in time',type:'time',unit:'HH:mm (IST)',editable:true,approval:OWNER,effective:'Immediately; existing bookings keep their dates',help:'Affects arrivals expected today.'},
  {key:'checkout',category:'Stay timings',label:'Standard checkout time',type:'time',unit:'HH:mm (IST)',editable:true,approval:OWNER,effective:'Immediately; existing bookings keep their dates',help:'Affects in-house stays departing today.'},
  {key:'taxBps',category:'Finance & synthetic tax',label:'Synthetic demonstration tax',type:'percent',unit:'% (basis points stored)',editable:true,approval:OWNER,effective:'Effective-dated (tax schedule)',help:'Test data, not a validated GST rate. Posted lines keep their tax snapshot.'},
  {key:'currency',category:'Finance & synthetic tax',label:'Currency and money unit',type:'fixed',unit:'INR, integer paise',editable:false,approval:'Not configurable',effective:'—',help:'All money is stored as integer paise.'},
  {key:'discountLimit',category:'Approvals/limits',label:'Discount threshold without approval (Reception)',type:'money',unit:'₹ per request',editable:true,approval:OWNER,effective:'Immediately; pending requests are not re-evaluated',help:'Above this, another authorised user must approve (PERM-FOL-003).'},
  {key:'approvalExpiryHours',category:'Approvals/limits',label:'Approval request expiry',type:'integer',unit:'simulated hours',editable:true,approval:OWNER,effective:'New requests only',help:'Requests raised on this screen expire after this long (RBAC §7 default 24 h).'},
  {key:'managerLimits',category:'Approvals/limits',label:'Manager limits (discount, rate override, cash difference, credit, expenses)',type:'fixed',unit:'₹ / %',editable:false,approval:'Edited in Access control → Role templates (PERM-ADM-002)',effective:'Next command',help:'Role limits are part of the access policy, not hotel configuration.'},
  {key:'inspection',category:'Housekeeping',label:'Supervisor inspection after cleaning',type:'boolean',unit:'on / off',editable:true,approval:OWNER,effective:'Tasks completed after the change',help:'When off, a completed task makes the room Ready directly.'},
  {key:'openingCash',category:'Shifts',label:'Opening float for the current business day',type:'fixed',unit:'₹',editable:false,approval:'Set by day close → open next day',effective:'—',help:'Carried forward from the reconciled drawer (Financial Rulebook §4).'},
  {key:'shiftModel',category:'Shifts',label:'Cash shift model',type:'fixed',unit:'—',editable:false,approval:'Fixed in this prototype',effective:'—',help:'One cash shift per business day; cash receipts need an open shift.'},
  {key:'invoicePrefix',category:'Documents',label:'Sample invoice number prefix',type:'text',unit:'2–8 capitals, digits or hyphen',editable:true,approval:OWNER,effective:'Documents generated after the change',help:'Used on demonstration documents only.'},
  {key:'watermark',category:'Documents',label:'Demonstration watermark',type:'fixed',unit:'—',editable:false,approval:'Not configurable',effective:'—',help:'“Demonstration — not a tax invoice / fictional” is always printed.'},
];
export function adminSettingValue(s:State,key:string):string{
  const st=s.settings;
  switch(key){
    case 'hotel':return st.hotel;case 'checkin':return st.checkin;case 'checkout':return st.checkout;
    case 'taxBps':return (adminTaxScheduled(s,s.date)/100)+'%';case 'currency':return 'INR · paise';
    case 'discountLimit':return money(st.discountLimit);case 'approvalExpiryHours':return String(st.approvalExpiryHours??24)+' h';
    case 'managerLimits':{const l=adminPolicy(s).limits.Manager||{};return `Discount ${money(l['PERM-FOL-003']??0)} · rate ${l['PERM-RATE-001']??0}% · cash diff ${money(l['PERM-SHF-002']??0)} · credit ${money(l['PERM-PAY-005']??0)} · expense ${money(l['PERM-EXP-002']??0)}`;}
    case 'inspection':return st.inspection?'On':'Off';case 'openingCash':return money(s.openingCash);case 'shiftModel':return 'Single shift / day';
    case 'invoicePrefix':return st.invoicePrefix??'DEMO-INV';case 'watermark':return 'Always on';case 'rooms':return String(s.rooms.length);
    default:return '—';
  }
}
export const adminSettingHistory=(s:State,key:string)=>(s.configHistory||[]).filter(h=>h.key===key);

/** Latest dated tariff for a room type with effectiveFrom ≤ date (undefined = none scheduled). */
export function adminTariffOn(s:State,type:string,date:string){
  return (s.tariffs||[]).filter(t=>t.type===type&&t.effectiveFrom<=date).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1).slice(-1)[0];
}
export function adminTaxScheduled(s:State,date:string){
  const e=(s.taxSchedule||[]).filter(t=>t.effectiveFrom<=date).sort((a,b)=>a.effectiveFrom<b.effectiveFrom?-1:1).slice(-1)[0];
  return e?e.bps:s.settings.taxBps;
}
export const adminRoomTypes=(s:State)=>[...new Set(s.rooms.map(r=>r.type))];
/** Base (fixture) tariff for a room type: the room rate of the first room of that type. */
export const adminBaseRate=(s:State,type:string)=>s.rooms.find(r=>r.type===type)?.rate??0;

export type AdminImpact={openStays:number;postedLines:number;stays:{id:string;guest:string;room:string;note:string}[];notes:string[];warning?:string};
const isOpen=(b:Booking)=>b.status==='In house'||b.status==='Confirmed';
/** Impact preview shown before a configuration save (SCR-18). Pure; never changes state. */
export function adminImpactPreview(s:State,change:
  {kind:'tariff';type:string;rate:number;effectiveFrom:string}|{kind:'tax';bps:number;effectiveFrom:string}|
  {kind:'times';checkin:string;checkout:string}|{kind:'discount';limit:number}|{kind:'inspection';on:boolean}):AdminImpact{
  if(change.kind==='tariff'){
    const rooms=new Set(s.rooms.filter(r=>r.type===change.type).map(r=>r.id));
    const list=s.bookings.filter(b=>isOpen(b)&&rooms.has(b.room)&&b.departure>change.effectiveFrom);
    const posted=list.reduce((n,b)=>n+b.lines.filter(l=>l.kind==='room').length,0);
    return {openStays:list.length,postedLines:posted,stays:list.map(b=>({id:b.id,guest:b.guest,room:b.room,note:`keeps agreed ${money(b.rate)}/night`})),
      notes:[`${list.length} open stay${list.length===1?'':'s'} in ${change.type} rooms overlap ${change.effectiveFrom} — existing bookings keep their agreed rates`,`${posted} posted room line${posted===1?'':'s'} unchanged`,`New ${change.type} bookings arriving on or after ${change.effectiveFrom} are priced at ${money(change.rate)}/night`]};
  }
  if(change.kind==='tax'){
    const list=s.bookings.filter(b=>isOpen(b)&&b.departure>change.effectiveFrom);
    const posted=s.bookings.reduce((n,b)=>n+b.lines.length,0);
    return {openStays:list.length,postedLines:posted,stays:list.map(b=>({id:b.id,guest:b.guest,room:b.room,note:'nights and charges dated on/after the effective date use the new rate'})),
      notes:[`${list.length} open stay${list.length===1?'':'s'} will have new lines at ${change.bps/100}% from ${change.effectiveFrom}`,`${posted} posted line${posted===1?'':'s'} keep their tax snapshot (unchanged)`,'Synthetic demonstration tax — not a GST rate']};
  }
  if(change.kind==='times'){
    const out=change.checkout!==s.settings.checkout,inn=change.checkin!==s.settings.checkin;
    const dep=out?s.bookings.filter(b=>b.status==='In house'&&b.departure===s.date):[];
    const arr=inn?s.bookings.filter(b=>b.status==='Confirmed'&&b.arrival===s.date):[];
    const stays=[...dep.map(b=>({id:b.id,guest:b.guest,room:b.room,note:`departing today · checkout ${s.settings.checkout} → ${change.checkout}`})),...arr.map(b=>({id:b.id,guest:b.guest,room:b.room,note:`arriving today · check-in ${s.settings.checkin} → ${change.checkin}`}))];
    return {openStays:stays.length,postedLines:0,stays,notes:[stays.length?`${stays.length} open stay${stays.length===1?'':'s'} affected today`:'No open stays are affected today (no arrivals or departures due)','Booking dates and posted charges are unchanged','Inform affected guests of the new time'],
      warning:stays.length?`${stays.length} open stay${stays.length===1?'':'s'} affected today: ${stays.map(x=>x.guest+' (room '+x.room+')').join(', ')}`:undefined};
  }
  if(change.kind==='discount'){
    const pend=s.approvals.filter(a=>a.status==='Pending'&&(!a.kind||a.kind==='discount'));
    const within=pend.filter(a=>a.amount<=change.limit);
    return {openStays:0,postedLines:0,stays:pend.map(a=>({id:a.id,guest:a.booking,room:'',note:`pending ${money(a.amount)} — still needs a decision`})),
      notes:[`Discounts up to ${money(change.limit)} will post without approval from the next request`,`${pend.length} pending request${pend.length===1?'':'s'} keep their approval route${within.length?` (${within.length} would now be within the threshold)`:''}`,'Posted discounts are unchanged']};
  }
  const insp=s.tasks.filter(t=>t.status==='Inspection');
  return {openStays:0,postedLines:0,stays:insp.map(t=>({id:t.id,guest:'',room:t.room,note:'awaiting inspection — stays in inspection'})),
    notes:[change.on?'Completed cleaning will wait for supervisor inspection':'Completed cleaning makes the room Ready directly',`${insp.length} task${insp.length===1?'':'s'} already awaiting inspection are unaffected`]};
}
