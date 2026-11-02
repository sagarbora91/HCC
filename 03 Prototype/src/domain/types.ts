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
export type Line={id:string;label:string;amount:number;date:string;kind:'room'|'service'|'food'|'discount'|'adjustment';taxBps:number; // ACC-2: 'food' = F&B charged to the folio → 4200 (contract §8)
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
export type Receipt={id:string;amount:number;method:string;date:string;kind:'payment'|'deposit'|'deposit-return'|'refund'|'company-credit'|'deposit-apply'|'ota-credit'|'guest-credit';reference:string;actor:string; // ACC-2 flows (contract §4): deposit applied at check-out (A6a), folio balance to the OTA (A13)
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

  // ── Receipt · ACC-2 flows (contract §4; coordinator ruling 28 Oct: DEPOSIT_APPLY source) ──
  /** A `deposit-apply` receipt that applies an advance carried in the opening balance (no deposit receipt on the folio, rulebook T5a). */
  depositSource?:'opening';
  // ── Receipt · LF (contract §7 B2): kind 'guest-credit' = folio balance moved to the guest's Udhaar account at check-out ──
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
  // ── Booking · LF (SCHEMA 13, contract §2–§4, §8; declared by the setup commit, lanes implement) ──
  /** A3: purpose-of-visit master code (frontdesk master `purposes`). */
  purpose?:string;
  /** A4: vehicle number (upper-case, 4–13 of A–Z 0–9 - space) and vehicle-type master label. */
  vehicle?:{no:string;type?:string};
  /** A5: Form C for a foreign guest (per stay). */
  formC?:FormC;
  /** A6: due-out basis; absent = the setting `frontdesk.defaultCheckoutBasis` (default 'fixed'). */
  checkoutBasis?:'fixed'|'24h';
  /** A6: simulated clock at CHECKIN (absent on old bookings = arrival + settings.checkin). */
  checkedInAt?:string;
  /** B1: where the rate came from ('agreed' = the guest's agreed rate set by the Owner). */
  rateSource?:'tariff'|'agreed';
  /** A2: earlier invoice revisions of this stay; `invoiceNo` is always the current one. */
  invoiceRevisions?:{no:string;date:string;supersedes:string}[];
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
export type Expense={id:string;vendor:string;category:string;amount:number;method:string;date:string;status:'Draft'|'Pending'|'Posted'|'Rejected'|'Reversed'|'Void';actor:string;
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
  // ── Expense · ACC-2 purchase fields (contract §3; optional — older records have none) ──
  /** inter = IGST on the bill (vendor outside Maharashtra): allowed ITC goes to 1330. */ gstKind?:'intra'|'inter';
  /** Reverse charge (category flag at posting): no vendor GST; HCC posts the RCM GST to 2140 at rcmBps. */ rcm?:boolean;rcmBps?:number;
  /** Fixed-asset purchase (category flag): the debit goes to the category's asset ledger; assetNo = register row. */ capital?:boolean;assetNo?:string;
  /** TDS held back on this bill (DEC-146): the vendor is credited net; tdsSection = rate-table row. */ tdsPaise?:number;tdsSection?:string;
  /** Cash guard B10 tripped (cash above ₹10,000 to one person in a day): the deduction is lost for income tax. */ cashDeductionLost?:boolean;
  // ── Expense · SCC features (DEC-120; optional — records saved before have none; helpers in domain/streams/expenses-scc.ts) ──
  /** What the expense is for (SCC: required; HCC: required once submitted for approval). Older records show the vendor. */
  description?:string;
  /** GST on the bill (SCC P1-17): rate in basis points (1800 = 18 %) and amount in paise, kept only when > 0. A reversal record carries −GST. */
  gstRateBps?:number;gstPaise?:number;
  /** DEC-135: input tax credit of the bill's GST — 'allowed' books the GST to the GST input-credit ledger, 'disallowed' leaves it in the
   *  expense cost. Kept only with GST on the bill; absent on older records = disallowed (restore migration sets it). A reversal copies it. */
  itc?:'allowed'|'disallowed';
  /** Bill photo (SCC: picked file ≤ 4 MB, scaled to 1000 px wide, JPEG 0.7): a data URL in the demo build; production keeps it on the server. */
  billPhoto?:string;
  /** LF §1.10 (SCHEMA 13): ref into `s.docObjectsX`; replaces `billPhoto` after the v12 → v13 move. */
  billPhotoRef?:string;
  /** Production: the server holds a bill photo for this expense (fetched on demand). */
  hasBillPhoto?:boolean;
  /** Origin: a petty-cash disbursement or a recurring template post (absent = entered by hand). sourceRef 'rec:<template>:<YYYY-MM>' / 'pty-…'. */
  source?:'petty'|'recurring';sourceRef?:string;
  /** Void (SCC): one of the six reasons plus an optional note ("reason — note"); the record is kept for audit. */
  voidReason?:string;voidedBy?:string;voidedAt?:string;
  /** Edit log (SCC: when, who, old → new, reason). */
  editLog?:import('./streams/expenses-scc').ExpEditLog[];
  /** Users other than the submitter who changed it: they may not approve it (nobody approves their own). */
  editors?:string[];
  /** Simulated clock when the record was made (SCC createdAt; the month feed's stale rule). */
  createdAt?:string;
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
    /** W1a LG (DEC-162 Q4): date of birth yyyy-mm-dd, optional (no SCHEMA bump). */ dob?:string;
    /** W1a LG: the old "Lodge" program's GuestID that produced this profile (set by the legacy importer only). */ legacyGuestId?:number;
    company?:string;gstin?:string;preferences:string;notes:string;
    /** DEC-193: every accompanying guest has their own ID (idType, idLast4, idNumber? — optional fields, no SCHEMA bump; the full number is
     *  shown masked and revealed only through the audited GUEST_ID_VIEW {seq}). Required for Adult / Senior (GUEST_PARTY_ID_REQUIRED_BANDS). */
    party:{name:string;ageBand:string;relation:string;idType?:string;idLast4?:string;idNumber?:string}[];verified:boolean;
    /** LF B1 (§3): agreed rate per room type; sent to money roles only (DEC-MA-11e). */
    agreedRates?:GuestAgreedRate[];
    /** LF B2 (§7): Owner-only money field (DEC-058); customerKey = the Udhaar party id. */
    creditAccount?:GuestCreditAccount;
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
  /** A02b-05 demo: users who changed their starting PIN / password (digest only, never the secret; absent = demoPin() / demoPassword()). */
  credentialsX?:import('./credentials').DemoCredentials;

  // ── Modules 13–26 (one optional slice each; types live in domain/streams/<module>.ts) ──
  yieldX?:import('./streams/yield').YLDState;
  groupsX?:import('./streams/groups').GRPState;
  channelsX?:import('./streams/channels').CHNState;
  maintenanceX?:import('./streams/maintenance').MNTState;
  inventoryX?:import('./streams/inventory').INVState;
  handoverX?:import('./streams/handover').HOVState;
  /** KEYS room keys: lock mapping overrides, encoder state, job log, card ledger (optional; absent in old saves, no SCHEMA bump). */
  keysX?:import('./streams/keys').KEYSlice;
  servicesX?:import('./streams/services').GSVState;
  messagingX?:import('./streams/messaging').MSGState;
  posX?:import('./streams/pos').POSState;
  staffX?:import('./streams/staff').HRState;
  accountingX?:import('./streams/accounting').ACCState;
  assistantX?:import('./streams/assistant').AIState;
  complianceX?:import('./streams/compliance').CMPState;
  /** SCC tax port (DEC-116): compliance evidence file contents (data URLs) by file id — its own slice so the calendar never re-saves them. */
  complianceFilesX?:Record<string,string>;
  cashbookX?:import('./streams/cashbook').CBState;
  /** Wave B1 (DEC-073 / DEC-078): bank deposits, till transfers, Owner drawings, the other tills and the desk shift's opener. */
  cashOutX?:import('./streams/cashout').CXState;
  /** W05-A Master configuration: modules on/off, hub-stored and future-dated values, raw change log (revert). */
  configX?:import('./streams/config').CFGState;
  /** MA-0 masters framework: stored master rows per list + change history (absent = fixture seed). */
  mastersX?:import('./streams/masters').MSTState;
  /** Wave C3 System health (A09-05): the server's parked outbox events and backup results, or the demo's remaining parked events (absent = demo seed). */
  sysHealthX?:import('./streams/syshealth').SYSState;
  /** MA-5 front-desk lists: numbering-series allocation counters (absent until a non-seeded series issues a number). */
  frontdeskMasterX?:import('./streams/frontdesk-master').FDMState;
  /** DEC-120 SCC expense features: monthly budgets, recurring templates, petty-cash float top-ups, the Udhaar credit register, month locks. */
  expensesX?:import('./streams/expenses-scc').EXPState;

  /** FY history: compact daily housekeeping summary for the generated history window (scenario FY26). */
  historyX?:import('./streams/history').HistoryX;

  // ── State · LF slices (SCHEMA 13, contract §11; types below in this file, lanes implement in their stream files) ──
  stayRateX?:STRState;
  billCorrectionX?:BLCState;
  counterX?:CTRState;
  remindersX?:RMDState;
  /** LF §1.10: every image / file of the demo keyed by ref (its own IndexedDB record). */
  docObjectsX?:DOBState;
  // F5-E1
  /** F5-E1 egress gate mirror (EGRESS-CONTRACT §6): last net.* change + the mock outbound outbox (held / pending / sent). Values live in configX.values['net.*']. */
  egressX?:import('./streams/egress').NETState;
  // F5-E1 end

  // ── State · stream F fields (add below this line) ──
  /** Immutable closing record per business date (REQ-CLOSE-004). */
  closings?:Record<string,import('./streams/closing').ClosingRecord>;
  /** Resumable night-audit progress for the business date (REQ-CLOSE-005). */
  closeProgress?:import('./streams/closing').CloseProgress;
  /** Submitted cash count for the business date (WF-SHIFT-001). */
  shift?:import('./streams/closing').ShiftRecord;
};
export type Command={type:string;key?:string;[field:string]:any};
export const SCHEMA=13; // 13 = LF legacy parity (docObjectsX image slice — v12 saves move billPhoto / complianceFilesX into it; new optional LF slices and fields). 12 = ACC-2 (manual vouchers, purchases / vendor payments, TDS, GST month end, OTA payouts; new optional fields only — v11 saves need nothing). 11 = ACC-1 (accounting chart as a master, party ledgers, opening balances; new optional fields on accountingX — v10 saves need nothing). 10 = SCC port wave (leave one record per date, SCC compliance slice, SCC expense registers; v9 saves migrate by shape). 9 = masters wave MA-1…MA-7 (new optional slices / fields; v8 saves migrate, masters seed lazily)

// ════ LF (wave claude/lf, contract 02 Planning/agents/LF-CONTRACT.md §11) — declared by the setup commit; lanes import (or re-export) these, never redeclare ════
/** §1.10 */
export type DOBKind='id-image'|'guest-photo'|'expense-bill'|'banking-slip'|'compliance-evidence'|'legacy-image';
export type DOBObject={ref:string;kind:DOBKind;owner:string;contentType:string;bytes:number;dataUrl:string;at:string;by:string};
/** State slice `s.docObjectsX` (schema 13): every image / file of the demo, keyed by ref; saved only when it changed (its own IndexedDB record). */
export type DOBState=Record<string,DOBObject>;
/** §1.11 (lf-images implements compressPhoto / PHOTO_SLIP / PHOTO_DOC in app/slipPhoto.ts). */
export type PhotoProfile={maxBytes:number;edges:number[];qualities:number[];label:string};
/** §2 A1 */
export type STRRateChange={id:string;booking:string;room:string;date:string;at:string;by:string;approvalId?:string;approvedBy?:string;
  scope:'next'|'today-next'|'all';reason:string;oldRate:number;newRate:number;oldPeople:number;newPeople:number;oldChildren:number;newChildren:number;
  effectiveFrom:string;reposted:{reversal:string;line:string;night:string}[]};
export type STRState={changes:STRRateChange[]};     // s.stayRateX
/** §3 B1 */
export type GuestAgreedRate={type:string;rate:number;plan?:string;validTo?:string;note?:string;setBy:string;at:string};
/** §4 A2 */
export type BLCChange=
  |{kind:'room-rate';night:string;oldRate:number;newRate:number}
  |{kind:'remove-line';lineId:string}
  |{kind:'add-line';label:string;amount:number;gstCategory:string;date:string;extraId?:string};
export type BLCCorrection={id:string;booking:string;oldInvoiceNo:string;creditNoteNo?:string;newInvoiceNo?:string;changes:BLCChange[];
  reason:string;requestedBy:string;at:string;approvalId:string;approvedBy?:string;decidedAt?:string;status:'Pending'|'Issued'|'Rejected'|'Expired';
  oldTotal:number;newTotal:number;delta:number;settlement?:'refund-due'|'balance-due'|'none'};
export type BLCState={corrections:BLCCorrection[]};   // s.billCorrectionX
/** §6 A7 */
export type CTRLine={id:string;label:string;extraId?:string;menuItemId?:string;qty:number;unit:number;amount:number;gstCategory:string;taxBps:number;sac:string};
export type CTRBill={id:string;no:string;date:string;at:string;by:string;party:{name:string;phone?:string;gstin?:string;address?:string;guestId?:string;companyId?:string};
  lines:CTRLine[];discount?:{amount:number;reason:string};net:number;tax:number;cgst:number;sgst:number;total:number;remark?:string;
  settlement:{mode:'paid'|'udhaar'|'company';method?:string;reference?:string;receiptId?:string;udhaarId?:string;companyId?:string};
  status:'Issued'|'Credited';creditNoteNo?:string;creditNoteReason?:string;voidApprovalId?:string};
export type CTRState={bills:CTRBill[]};   // s.counterX
/** §7 B2 */
export type GuestCreditAccount={limit:number;active:boolean;customerKey:string;setBy:string;at:string;note?:string};
/** §8.3 A5 — the passport NUMBER is the guest's ID number (DEC-068 store); never duplicated here. */
export type FormC={passportPlace:string;passportIssued:string;passportExpiry:string;
  visaNo:string;visaType:string;visaPlace:string;visaIssued:string;visaExpiry:string;
  arrivedFrom:string;arrivalInIndia:string;intendedStayDays:number;nextDestination:string;
  employedInIndia:boolean;employerOrBusiness?:string;addressAbroad:string;contactAbroad?:string;sex:'M'|'F'|'X'};
/** §9.2 B4 */
export type RMDReminder={id:string;subject:string;date:string;time?:string;leadDays:number;role:Role|'all';note?:string;repeat:'none'|'monthly'|'yearly';
  status:'open'|'done'|'dismissed';by:string;at:string;doneBy?:string;doneAt?:string;nextDate?:string};
export type RMDState={reminders:RMDReminder[];leadDaysDefault:number};   // s.remindersX
/** §1.9 System health databases block (SYSState.databases). */
export type SYSDatabase={name:string;kind:'main'|'documents';schemaVersion:string;reachable:boolean;dataBytes:number;logBytes:number;limitBytes:number;pct:number;
  objectCount?:number;contentBytes?:number;danglingRefs?:number;orphanObjects?:number;lastBackup?:string;lastDrill?:string};
