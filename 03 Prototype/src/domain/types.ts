// Core prototype types. Pure data — no React, no storage.
/** 'Restaurant' = role matrix v2 D1 (DEC-079): the PIN-signed waiter role of the production server (RST); the demo fixture has no such user. */
export type Role='Owner'|'Manager'|'Reception'|'Cashier'|'Supervisor'|'Housekeeper'|'Accountant'|'Maintenance'|'Tech admin'|'Restaurant';
export type Permission='reservations'|'checkin'|'billing'|'payments'|'checkout'|'housekeeping'|'inspect'|'expenses'|'approve'|'close'|'reports'|'admin'|'masters';
/** 'masters' (MA-0, DEC-MA-1): change operational lists in Master configuration (floors …); Owner + Tech admin by default, grantable. */
export const permissions:Permission[]=['reservations','checkin','billing','payments','checkout','housekeeping','inspect','expenses','approve','close','reports','admin','masters'];
export const roles:Role[]=['Owner','Manager','Reception','Cashier','Supervisor','Housekeeper','Accountant','Maintenance','Tech admin'];
/** Roles that may only act on tasks assigned to them (scope "A" in the RBAC matrix). */
export const assignedScopeRoles:Role[]=['Housekeeper','Maintenance'];
export type User={id:string;name:string;role:Role;enabled:boolean;
  // ── User · stream A fields (add below this line) ──

  // ── User · stream B fields (add below this line) ──

  // ── User · stream C fields (add below this line) ──

  // ── User · stream D fields (add below this line) ──

  // ── User · stream E fields (add below this line) ──
  /** Last valid business date of this user's role assignment (inclusive). After it, the user is denied (DEC-040 step 1). */
  expiresOn?:string;
  /** Assignment metadata: who assigned the current role, and on which business date. */
  assignedBy?:string;assignedOn?:string;

  // ── User · stream F fields (add below this line) ──
};
export type Room={id:string;floor:number;type:string;rate:number;hk:'Ready'|'Dirty'|'Cleaning'|'Inspection';blocked:boolean;
  // ── Room · stream A fields (add below this line) ──

  // ── Room · stream B fields (add below this line) ──

  // ── Room · stream C fields (add below this line) ──

  // ── Room · stream D fields (add below this line) ──
  /** Block reason (REQ-ROOM-004). A block covers nights [blockFrom, blockTo); missing blockFrom = from now, missing blockTo = open-ended. */
  blockReason?:string; blockFrom?:string; blockTo?:string;

  // ── Room · stream E fields (add below this line) ──

  // ── Room · stream F fields (add below this line) ──

  // ── Room · MA-1 rooms master fields (domain/streams/roommasters.ts; all optional, absent in BASE) ──
  /** Amenity codes (amenities master) this room has on top of its room type. */
  features?:string[];
  /** Smoking room (default no). */ smoking?:boolean;
  /** Number of the connecting room, if any. */ connecting?:string;
  /** Phone extension. */ extension?:string;
  /** Free notes for staff. */ notes?:string;
  /** Master status: absent = in service; 'out-of-order' while `ooo` is set; 'retired' = no longer sold (kept for history). */
  status?:'out-of-order'|'retired';
  /** Out-of-order window: nights [from, to) (to absent = until cleared) and the reason. A room hold (registerRoomHold), not a block. */
  ooo?:{from:string;to?:string;reason:string};
  /** Business date the room was retired. */ retiredOn?:string;
  /** V14-5: business date the room was added through the rooms master (absent = an original room). Group holds take rooms that
   *  were there before (oldest first), so a room added today stays bookable while older free rooms can carry a block. */ addedOn?:string;
};
export type Line={id:string;label:string;amount:number;date:string;kind:'room'|'service'|'discount'|'adjustment';taxBps:number;
  // ── Line · stream C fields (add below this line) ──
  /** Original line ID this line reverses (correction, WF-FOLIO-002). Posted lines are never edited. */
  reversalOf?:string;
  /** Approval that authorised this correction/credit line. */
  approvalId?:string;

  // ── Line · stream F fields (add below this line) ──
  /** Closed business date this adjustment corrects (DEC-028). */
  correctionOf?:string;

  // ── Line · MA-2 catalogue fields (add below this line) ──
  /** Extras catalogue row id (CAT_CHARGE); absent on a free-text charge (DEC-MA-4: reports show catalogue vs free text). */
  extraId?:string;
  /** Meal plan code of a plan line posted with the room night (MA-2). */
  planCode?:string;
  /** Discount reason code (CAT_DISCOUNT); absent on a free-text reason. */
  discountReason?:string;
};
export type Receipt={id:string;amount:number;method:string;date:string;kind:'payment'|'deposit'|'deposit-return'|'refund'|'company-credit';reference:string;actor:string;
  // ── Receipt · stream A fields (add below this line) ──

  // ── Receipt · stream B fields (add below this line) ──

  // ── Receipt · stream C fields (add below this line) ──
  /** Original payment receipt ID this refund reverses (DEC-031). */
  refundOf?:string;
  /** Approval executed by this receipt (refund, company credit). */
  approvalId?:string;
  /** Mixed-tender group: receipts created by one PAYMENT command share this ID. */
  tenderGroup?:string;

  // ── Receipt · stream D fields (add below this line) ──

  // ── Receipt · stream E fields (add below this line) ──

  // ── Receipt · stream F fields (add below this line) ──
};
export type BookingStatus='Held'|'Expired'|'Confirmed'|'In house'|'Departed'|'Cancelled'|'No show';
export type Booking={id:string;guest:string;email:string;people:number;room:string;arrival:string;departure:string;rate:number;source:string;status:BookingStatus;registered:boolean;lines:Line[];receipts:Receipt[];version:number;notes:string;
  // ── Booking · stream A fields (add below this line) ──
  /** Production only: the server public booking number (e.g. HCC-1101) shown to people; `id` stays the internal key. See domain/bookingRef.ts. */
  publicNo?:string;
  /** Hold expiry (simulated clock value). A Held booking blocks inventory only while s.clock < holdUntil (DEC-038). */
  holdUntil?:string;
  /** Contact phone (fictional). Required in the booking form; optional in BOOK for backward compatibility. */
  phone?:string;
  /** Children in the party (counted with `people` against room capacity). */
  children?:number;
  /** Named OTA channel when source is OTA (REQ-BOOK-003). */
  otaChannel?:string;
  /** Reason recorded with CANCEL. */
  cancelReason?:string;

  // ── Booking · stream B fields (add below this line) ──
  /** Linked guest profile (State.guests). Set by REGISTER / GUEST_SAVE. */
  guestId?:string;
  /** Dated room segments of the stay [from, to); the open segment has no `to`. Started by CHECKIN, split by MOVE. */
  segments?:{room:string;from:string;to?:string}[];

  // ── Booking · stream C fields (add below this line) ──
  /** `account` (DEC-076) = the stay is billed to that company account (Groups & corporate › Companies): it counts in the company's credit
   *  exposure and may go on company credit. Without it the company is invoice details only (the guest pays). */
  company?:{name:string;gstin?:string;account?:string};
  /** Sample invoice number, allocated once at checkout (DEMO/2026-27/NNNN). */
  invoiceNo?:string;
  /** Sample credit notes issued after departure. */
  creditNotes?:{id:string;no:string;amount:number;reason:string;date:string;approvalId:string;lineId:string}[];

  // ── Booking · stream D fields (add below this line) ──

  // ── Booking · stream E fields (add below this line) ──

  // ── Booking · stream F fields (add below this line) ──

  // ── Booking · MA-2 catalogue fields (add below this line) ──
  /** Meal plan code (master mealPlans: EP / CP / MAP / AP / custom); absent = no plan. Plan lines post with each room night. */
  plan?:string;
};
export type Task={id:string;room:string;booking?:string;assignee:string;status:'To do'|'In progress'|'Inspection'|'Done';checked:boolean[];
  // ── Task · stream A fields (add below this line) ──

  // ── Task · stream B fields (add below this line) ──

  // ── Task · stream C fields (add below this line) ──

  // ── Task · stream D fields (add below this line) ──
  /** Task kind; undefined = checkout clean (M1 tasks). Stayover tasks do not change room readiness; repair tasks go to Maintenance. */
  kind?:'checkout'|'stayover'|'repair'|'move';
  /** Business date the task was created for (stayover idempotency, age). */
  date?:string;
  /** Repair description (repair tasks) or other note. */
  note?:string;
  /** Last inspection rejection reason (REQ-HK-003). */
  rejectReason?:string;
  /** Simulated-clock timestamps (REQ-HK-002). */
  createdAt?:string; startedAt?:string; completedAt?:string;

  // ── Task · stream E fields (add below this line) ──

  // ── Task · stream F fields (add below this line) ──
};
export type Expense={id:string;vendor:string;category:string;amount:number;method:string;date:string;status:'Draft'|'Pending'|'Posted'|'Rejected'|'Reversed';actor:string;
  // ── Expense · stream A fields (add below this line) ──

  // ── Expense · stream B fields (add below this line) ──

  // ── Expense · stream C fields (add below this line) ──

  // ── Expense · stream D fields (add below this line) ──

  // ── Expense · stream E fields (add below this line) ──

  // ── Expense · stream F fields (add below this line) ──
  notes?:string;
  /** Receipt placeholder: file name text only, no upload (REQ-EXP-004). */
  receipt?:string;
  /** Rejection or reversal reason. */
  reason?:string;
  approver?:string;decidedAt?:string;
  /** On a reversal record: the expense it reverses (negative amount, dated the reversal business date). */
  reversalOf?:string;
  /** On a reversed expense: its reversal record. */
  reversedBy?:string;
};
export type Approval={id:string;booking:string;amount:number;reason:string;status:'Pending'|'Approved'|'Rejected'|'Expired'|'Invalidated'|'Executed';actor:string;version:number;
  /** Request kind; undefined = discount (M1). Streams add e.g. 'refund','company-credit','cash-difference','expense','policy'. */
  kind?:string;
  /** Target record ID when not a booking (receipt, shift, expense, role…). */
  target?:string;
  approver?:string;decidedAt?:string;expiresAt?:string;meta?:Record<string,unknown>;
  // ── Approval · stream A fields (add below this line) ──

  // ── Approval · stream B fields (add below this line) ──

  // ── Approval · stream C fields (add below this line) ──

  // ── Approval · stream D fields (add below this line) ──

  // ── Approval · stream E fields (add below this line) ──

  // ── Approval · stream F fields (add below this line) ──
};
export type Audit={id:string;at:string;actor:string;action:string;detail:string};
export type Settings={hotel:string;
  /** Legacy synthetic tax (before DEC-069); ignored — GST comes from the dated GST master (domain/gst.ts). Old saves may still carry it. */
  taxBps?:number;
  discountLimit:number;inspection:boolean;checkin:string;checkout:string;
  // ── Settings · stream A fields (add below this line) ──

  // ── Settings · stream B fields (add below this line) ──

  // ── Settings · stream C fields (add below this line) ──

  // ── Settings · stream D fields (add below this line) ──

  // ── Settings · stream E fields (add below this line) ──
  /** Prefix for demonstration document numbers (SCR-18 Documents). */
  invoicePrefix?:string;
  /** Simulated hours before an approval request raised by stream E expires (RBAC §7 default 24). */
  approvalExpiryHours?:number;

  // ── Settings · stream F fields (add below this line) ──
};
export type State={
  /** Persisted-state schema version. Bump when the shape changes; older stored data is reset. */
  schema:number;
  /** Scenario this state was loaded from (fixture catalogue IDs). */
  scenario:string;
  /** Business date, YYYY-MM-DD. Changes only via day close → open next day (DEC-042). */
  date:string;
  /** Simulated hotel clock, local Asia/Kolkata time YYYY-MM-DDTHH:mm. Never the real clock (DEC-030). */
  clock:string;
  rooms:Room[];bookings:Booking[];users:User[];tasks:Task[];expenses:Expense[];approvals:Approval[];audit:Audit[];
  /** Coarse grants per role. Partial: the production-only Restaurant role (DEC-079 D1) has no demo entry; production fills the signed-in role. */
  grants:Partial<Record<Role,Permission[]>>;settings:Settings;
  openingCash:number;shiftOpen:boolean;shiftClose?:{expected:number;counted:number;/** Who counted the drawer (W05 review F1; absent in old saves). */countedBy?:string};
  closedDates:string[];
  /** Idempotency keys already executed. A repeated key returns the earlier result (no new effect). */
  processed:string[];
  sequence:number;

  // ── State · stream A fields (add below this line) ──

  // ── State · stream B fields (add below this line) ──
  /** Fictional guest profiles (type alias `Guest` in domain/streams/guests.ts). ID number stored in full by owner decision DEC-068 (`idNumber`, optional; older saves keep only `idLast4`) — always displayed masked, never in audit/exports/messages; never images. */
  guests?:{id:string;name:string;phone:string;email:string;address:string;nationality:string;
    /** ID document type, metadata only. */ idType:string;
    /** Last 4 characters of the ID number (derived from idNumber when present). */ idLast4:string;
    /** Full ID number (DEC-068, normalised upper-case, no spaces). Optional: profiles saved earlier have only idLast4. */ idNumber?:string;
    company?:string;gstin?:string;preferences:string;notes:string;
    party:{name:string;ageBand:string;relation:string}[];verified:boolean;
    /** seed = fixture, manual = GUEST_SAVE/REGISTER with details, auto = minimal demo profile from REGISTER {id}. */
    origin?:'seed'|'manual'|'auto';version?:number}[];

  // ── State · stream C fields (add below this line) ──
  /** Last allocated sample invoice number (default 0). */
  invoiceSeqC?:number;
  /** Last allocated sample credit-note number (default 0). */
  creditNoteSeqC?:number;

  // ── State · stream D fields (add below this line) ──

  // ── State · stream E fields (add below this line) ──
  /** Fine-grained access policy (RBAC matrix v1): role templates, limits, user exceptions, history. s.grants stays the module gate. */
  policy?:import('./streams/admin').HccPolicy;
  /** Effective-dated tariffs per room type. rateFor() uses the latest with effectiveFrom ≤ date, else room.rate. */
  tariffs?:{type:string;rate:number;effectiveFrom:string}[];
  /** Legacy synthetic tax schedule (before DEC-069); no longer read — GST rates are dated values in the GST master. */
  taxSchedule?:{bps:number;effectiveFrom:string}[];
  /** Configuration change history (SCR-18): one entry per changed key. */
  configHistory?:{id:string;key:string;before:string;after:string;at:string;actor:string;effectiveFrom:string;reason:string}[];

  // ── Modules 13–26 (one optional slice each; types live in domain/streams/<module>.ts) ──
  yieldX?:import('./streams/yield').YLDState;
  groupsX?:import('./streams/groups').GRPState;
  channelsX?:import('./streams/channels').CHNState;
  maintenanceX?:import('./streams/maintenance').MNTState;
  inventoryX?:import('./streams/inventory').INVState;
  handoverX?:import('./streams/handover').HOVState;
  servicesX?:import('./streams/services').GSVState;
  messagingX?:import('./streams/messaging').MSGState;
  posX?:import('./streams/pos').POSState;
  staffX?:import('./streams/staff').HRState;
  accountingX?:import('./streams/accounting').ACCState;
  assistantX?:import('./streams/assistant').AIState;
  complianceX?:import('./streams/compliance').CMPState;
  cashbookX?:import('./streams/cashbook').CBState;
  /** Wave B1 (DEC-073 / DEC-078): bank deposits, till transfers, Owner drawings, the other tills and the desk shift's opener. */
  cashOutX?:import('./streams/cashout').CXState;
  /** W05-A Master configuration: modules on/off, hub-stored and future-dated values, raw change log (revert). */
  configX?:import('./streams/config').CFGState;
  /** MA-0 masters framework: stored master rows per list + change history (absent = fixture seed). */
  mastersX?:import('./streams/masters').MSTState;
  /** MA-5 front-desk lists: numbering-series allocation counters (absent until a non-seeded series issues a number). */
  frontdeskMasterX?:import('./streams/frontdesk-master').FDMState;

  /** FY history: compact daily housekeeping summary for the generated history window (scenario FY26). */
  historyX?:import('./streams/history').HistoryX;

  // ── State · stream F fields (add below this line) ──
  /** Immutable closing record per business date (REQ-CLOSE-004). */
  closings?:Record<string,import('./streams/closing').ClosingRecord>;
  /** Resumable night-audit progress for the business date (REQ-CLOSE-005). */
  closeProgress?:import('./streams/closing').CloseProgress;
  /** Submitted cash count for the business date (WF-SHIFT-001). */
  shift?:import('./streams/closing').ShiftRecord;
};
export type Command={type:string;key?:string;[field:string]:any};
export const SCHEMA=9; // 9 = masters wave MA-1…MA-7 (new optional slices / fields; v8 saves migrate, masters seed lazily)
