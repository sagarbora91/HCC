<!-- BEGIN serverNotTargetedTable -->
| Server command | Registered at | Used by other client code (outside the mappers) |
|---|---|---|
| `AddCustomerNote` | CustomersEndpoints.cs:35 | **no client code sends it** |
| `AddCustomerTag` | CustomersEndpoints.cs:31 | **no client code sends it** |
| `ApplyGroupRouting` | GroupsEndpoints.cs:43 | **no client code sends it** |
| `AttachGroupMember` | GroupsEndpoints.cs:39 | **no client code sends it** |
| `CaptureIdDocument` | DocumentsEndpoints.cs:35 | **no client code sends it** |
| `ChangeOwnCredential` | AccessEndpoints.cs:196 | **no client code sends it** |
| `CommitImportBatch` | ImportEndpoints.cs:31 | **no client code sends it** |
| `ConfirmIdCapture` | DocumentsEndpoints.cs:37 | **no client code sends it** |
| `CreateOutlet` | PosEndpoints.cs:34 | **no client code sends it** |
| `CreateTill` | PaymentsEndpoints.cs:14 | **no client code sends it** |
| `CreateWebDepositLink` | WebBookingEndpoints.cs:59 | **no client code sends it** |
| `DecideCustomerMerge` | CustomersEndpoints.cs:29 | **no client code sends it** |
| `DecideGroupAttrition` | GroupsEndpoints.cs:53 | **no client code sends it** |
| `DetachGroupMember` | GroupsEndpoints.cs:41 | **no client code sends it** |
| `EarlyDeparture` | StaysEndpoints.cs:45 | **no client code sends it** |
| `ImportChannelMessage` | OtaEndpoints.cs:36 | **no client code sends it** |
| `MarkWebDepositPaid` | WebBookingEndpoints.cs:61 | **no client code sends it** |
| `PostRoomNight` | BillingEndpoints.cs:33 | **no client code sends it** |
| `ProposeCustomerMerge` | CustomersEndpoints.cs:27 | **no client code sends it** |
| `PurgeIdCaptures` | DocumentsEndpoints.cs:51 | **no client code sends it** |
| `QueueSimulatedMessage` | OtaEndpoints.cs:37 | **no client code sends it** |
| `RecordGroupReceipt` | GroupsEndpoints.cs:49 | **no client code sends it** |
| `RejectIdCapture` | DocumentsEndpoints.cs:39 | **no client code sends it** |
| `RekeyGuestIds` | GuestIdEndpoints.cs:40 | **no client code sends it** |
| `ReleaseGroupBlock` | InventoryEndpoints.cs:53 | **no client code sends it** |
| `RemoveCustomerTag` | CustomersEndpoints.cs:33 | **no client code sends it** |
| `RequestGroupAttrition` | GroupsEndpoints.cs:51 | **no client code sends it** |
| `RerouteGroupCharge` | GroupsEndpoints.cs:45 | **no client code sends it** |
| `RevealGuestId` | GuestIdEndpoints.cs:35 | clients/sync/guestid.ts |
| `RevealIdCaptureNumber` | DocumentsEndpoints.cs:46 | **no client code sends it** |
| `SaveMenuItem` | PosEndpoints.cs:35 | **no client code sends it** |
| `SeedComplianceDefaults` | ComplianceEndpoints.cs:43 | **no client code sends it** |
| `SetCustomerConsent` | CustomersEndpoints.cs:39 | **no client code sends it** |
| `SetCustomerPreferences` | CustomersEndpoints.cs:37 | **no client code sends it** |
| `SetGroupRouting` | GroupsEndpoints.cs:37 | **no client code sends it** |
| `SetGstSetting` | TaxEndpoints.cs:33 | **no client code sends it** |
| `SetYieldForecast` | YieldEndpoints.cs:38 | **no client code sends it** |
| `SettleGroup` | GroupsEndpoints.cs:55 | **no client code sends it** |
| `StockSettingsSet` | StockEndpoints.cs:50 | **no client code sends it** |
| `TransferGroupMember` | GroupsEndpoints.cs:47 | **no client code sends it** |
| `UpdateGroup` | GroupsEndpoints.cs:35 | **no client code sends it** |
| `UploadImportBatch` | ImportEndpoints.cs:29 | **no client code sends it** |
| `ViewIdCaptureImage` | DocumentsEndpoints.cs:41 | **no client code sends it** |
| `WithdrawGstSetting` | TaxEndpoints.cs:35 | **no client code sends it** |

<!-- END serverNotTargetedTable -->

<!-- BEGIN summaryAreaTable -->
| Sync area (file) | Prototype commands | Mapped | Unmapped (reason in appendix B) | Absent (silent) | Extra production-only mappers |
|---|---:|---:|---|---|---|
| `accounting` | 15 | 15 | 0 | 0 | 0 |
| `admin` | 15 | 9 | 6: `GRANT`, `POLICY_DECIDE`, `POLICY_SET`, `SETTINGS`, `TARIFF_SCHEDULE`, `TAX_SCHEDULE` | 0 | 3: `USER_CREDENTIAL`, `DEVICE_REGISTER`, `DEVICE_REVOKE` |
| `cashier` | 0 | 0 | 0 | 0 | 1: `SHIFT_OPEN` |
| `channels` | 1 | 0 | 1: `CHN_WEB_REQUEST` | 0 | 0 |
| `channels/ota` | 9 | 9 | 0 | 0 | 0 |
| `channels/yield` | 10 | 10 | 0 | 0 | 0 |
| `closing` | 8 | 4 | 3: `CORRECTION_ADJUST`, `CORRECTION_ADJUST_DECIDE`, `POST_NIGHTS` | 1: `SHIFT_CLOSE` | 0 |
| `frontdesk` | 6 | 5 | 1: `GUEST_ID_VIEW` | 0 | 0 |
| `maintenance` | 26 | 26 | 0 | 0 | 1: `INV_ITEM_CREATE` |
| `money` | 22 | 17 | 4: `GRP_BLOCK_ALLOT`, `GRP_BLOCK_CANCEL`, `GRP_RELEASE`, `GRP_ROOMING` | 1: `GRP_SWEEP` | 1: `INVOICE_ISSUE` |
| `no sync area (assistant)` | 3 | 0 | 0 | 3: `AI_ASK`, `AI_CLEAR`, `AI_SETTINGS` | 0 |
| `pos` | 10 | 10 | 0 | 0 | 0 |
| `reservations` | 4 | 4 | 0 | 0 | 0 |
| `rooms` | 6 | 5 | 1: `BACKUP_SIMULATE` | 0 | 0 |
| `services/guest` | 15 | 15 | 0 | 0 | 0 |
| `services/handover` | 9 | 8 | 1: `HOV_CLOCK` | 0 | 0 |
| `services/messaging` | 10 | 7 | 3: `MSG_DOC_CONFIRM`, `MSG_DOC_DISCARD`, `MSG_DOC_EXTRACT` | 0 | 0 |
| `staff` | 20 | 20 | 0 | 0 | 1: `HR_DEPT_SAVE` |
| **Total** | **189** | **164** | **20** | **5** | **7** |

<!-- END summaryAreaTable -->

<!-- BEGIN summaryModuleTable -->
| Prototype module (`services/mock/handlers/*.ts`) | Commands | Mapped | Unmapped | Absent |
|---|---:|---:|---:|---:|
| accounting | 4 | 4 | 0 | 0 |
| admin | 15 | 9 | 6 | 0 |
| assistant | 3 | 0 | 0 | 3 |
| cashbook | 2 | 2 | 0 | 0 |
| channels | 10 | 9 | 1 | 0 |
| closing | 13 | 10 | 2 | 1 |
| commands.ts (core switch) | 1 | 0 | 1 | 0 |
| compliance | 7 | 7 | 0 | 0 |
| folio | 10 | 10 | 0 | 0 |
| groups | 9 | 4 | 4 | 1 |
| guests | 5 | 4 | 1 | 0 |
| handover | 9 | 8 | 1 | 0 |
| housekeeping | 6 | 5 | 1 | 0 |
| inventory | 10 | 10 | 0 | 0 |
| maintenance | 16 | 16 | 0 | 0 |
| messaging | 10 | 7 | 3 | 0 |
| pos | 10 | 10 | 0 | 0 |
| reservations | 4 | 4 | 0 | 0 |
| services | 15 | 15 | 0 | 0 |
| staff | 20 | 20 | 0 | 0 |
| yield | 10 | 10 | 0 | 0 |

<!-- END summaryModuleTable -->

<!-- BEGIN appendixB -->
| Command | Prototype module | Status | Screens (D = desktop page ids, P = phone) | Sync area file | Server command(s) | Reload slices | Reason / note |
|---|---|---|---|---|---|---|---|
| `ACC_LOCK` | accounting | MAPPED | D:accounting; P:accounting | accounting.ts | `LockAccountingPeriod` | accountingX | precheck;  |
| `ACC_MAP_RESET` | accounting | MAPPED | D:accounting; P:accounting | accounting.ts | `ResetLedgerMapping` | accountingX |  |
| `ACC_MAP_SET` | accounting | MAPPED | D:accounting; P:accounting | accounting.ts | `SetLedgerMapping` | accountingX | precheck;  |
| `ACC_UNLOCK` | accounting | MAPPED | D:accounting; P:accounting | accounting.ts | `UnlockAccountingPeriod` | accountingX | precheck;  |
| `CMP_CA_PACK` | compliance | MAPPED | D:compliance; P:compliance | accounting.ts | `GenerateCaPack` | complianceX |  |
| `CMP_FILE` | compliance | MAPPED | D:compliance; P:compliance | accounting.ts | `MarkComplianceFiled` | complianceX | precheck;  |
| `CMP_NA` | compliance | MAPPED | D:compliance; P:compliance | accounting.ts | `MarkComplianceNotApplicable` | complianceX | precheck;  |
| `CMP_OBLIGATION_SAVE` | compliance | MAPPED | D:compliance; P:compliance | accounting.ts | `SaveComplianceObligation` | complianceX | precheck;  |
| `CMP_OBLIGATION_TOGGLE` | compliance | MAPPED | D:compliance; P:compliance | accounting.ts | `ToggleComplianceObligation` | complianceX |  |
| `CMP_REOPEN` | compliance | MAPPED | D:compliance; P:compliance | accounting.ts | `ReopenComplianceFiling` | complianceX | precheck;  |
| `CMP_SETTINGS_SET` | compliance | MAPPED | D:compliance,settings; P:compliance,settings | accounting.ts | `SaveComplianceSettings` | complianceX |  |
| `EXPENSE` | closing | MAPPED | D:expenses; P:expenses | accounting.ts | `SubmitExpense` | expenses | precheck;  |
| `EXPENSE_APPROVE` | closing | MAPPED | D:approvals,expenses; P:approvals,expenses | accounting.ts | `DecideExpense` | expenses | ALSO listed in admin.ts `unmapped` (stale note);  |
| `EXPENSE_REVERSE` | closing | MAPPED | D:expenses; P:approvals,expenses | accounting.ts | `ReverseExpense` | expenses | precheck;  |
| `EXPENSE_SUBMIT` | closing | MAPPED | D:expenses; P:expenses | accounting.ts | `SubmitExpenseDraft` | expenses |  |
| `CFG_DECIDE` | admin | MAPPED | D:approvals; P:approvals | admin.ts | `DecideSettingRequest` | configX, settings, configHistory, approvals |  |
| `CFG_IMPORT` | admin | MAPPED | D:settings; P:settings | admin.ts | `ImportConfiguration` | configX, settings, configHistory, approvals | precheck;  |
| `CFG_REVERT` | admin | MAPPED | D:settings; P:settings | admin.ts | `RevertSetting` | configX, settings, configHistory, approvals |  |
| `CFG_WITHDRAW` | admin | MAPPED | D:settings; P:settings | admin.ts | `WithdrawScheduledSetting` | configX, settings, configHistory, approvals |  |
| `GRANT` | admin | UNMAPPED | D:access | admin.ts | — | — | role templates are the server seed (0101 + module seeds); no command edits them yet — role matrix v2 (D1–D7) decides |
| `POLICY_DECIDE` | admin | UNMAPPED | D:approvals; P:approvals | admin.ts | — | — | no policy change requests on the server (Tech admin PERM-ADM-002 R has nothing to request yet) |
| `POLICY_SET` | admin | UNMAPPED | D:access | admin.ts | — | — | no server command edits role templates or role limits yet (same as GRANT) |
| `SETTING_SET` | admin | MAPPED | D:settings; P:settings | admin.ts | `SetSetting` | configX, settings, configHistory, approvals | precheck;  |
| `SETTINGS` | admin | UNMAPPED | — | admin.ts | — | — | legacy BL-027 command; no screen sends it — SETTING_SET per key instead |
| `TARIFF_SCHEDULE` | admin | UNMAPPED | D:settings; P:settings | admin.ts | — | — | dated tariffs are published by the Yield module (PublishYieldTariffs, W4-11); no plain schedule/withdraw command |
| `TAX_SCHEDULE` | admin | UNMAPPED | — | admin.ts | — | — | replaced by the dated GST master (DEC-069) — SETTING_SET on gst.* keys |
| `USER` | admin | MAPPED | D:access; P:access | admin.ts | `SetUserEnabled` | users, policy, grants | precheck;  |
| `USER_CREATE` | admin | MAPPED | D:access; P:access | admin.ts | `CreateUser` | users, policy, grants | precheck;  |
| `USER_EXCEPTION` | admin | MAPPED | D:access | admin.ts | `RevokeException` + `GrantException` | users, policy, grants | precheck;  |
| `USER_UPDATE` | admin | MAPPED | D:access; P:access | admin.ts | `AssignRole` + `RevokeRole` + `SetUserEnabled` | users, policy, grants | precheck;  |
| `CHN_WEB_REQUEST` | channels | UNMAPPED | D:booking-site; P:booking-site | channels.ts | — | — | public website form preview: the real guest submission is the anonymous POST /public/booking/requests (rate-limited, no staff session); staff only confirm/decline |
| `CHN_ACCEPT` | channels | MAPPED | D:channels; P:channels | channels/ota.ts | `AcceptChannelMessage` | channelsX, bookings |  |
| `CHN_CHANNEL_SAVE` | channels | MAPPED | D:channels; P:channels | channels/ota.ts | `SaveChannel` | channelsX | precheck;  |
| `CHN_CHANNEL_STATUS` | channels | MAPPED | D:channels; P:channels | channels/ota.ts | `SetChannelStatus` | channelsX |  |
| `CHN_PULL` | channels | MAPPED | D:channels; P:channels | channels/ota.ts | `PullChannelMessages` | channelsX |  |
| `CHN_PUSH` | channels | MAPPED | D:channels; P:channels | channels/ota.ts | `PushChannelRates` | channelsX |  |
| `CHN_REJECT` | channels | MAPPED | D:channels; P:channels | channels/ota.ts | `RejectChannelMessage` | channelsX | precheck;  |
| `CHN_STOP_SELL` | channels | MAPPED | D:channels; P:channels | channels/ota.ts | `SetChannelStopSell` | channelsX |  |
| `CHN_WEB_CONFIRM` | channels | MAPPED | D:booking-site; P:booking-site | channels/ota.ts | `ConfirmWebRequest` | channelsX, bookings |  |
| `CHN_WEB_DECLINE` | channels | MAPPED | D:booking-site; P:booking-site | channels/ota.ts | `DeclineWebRequest` | channelsX | precheck;  |
| `YLD_DECIDE` | yield | MAPPED | D:approvals,yield; P:approvals,yield | channels/yield.ts | `DecideYieldProposal` | yieldX, approvals |  |
| `YLD_DOW_SET` | yield | MAPPED | D:yield; P:yield | channels/yield.ts | `SetYieldDayOfWeek` | yieldX |  |
| `YLD_GUARD_SET` | yield | MAPPED | D:settings,yield; P:settings,yield | channels/yield.ts | `SetYieldGuardrails` | yieldX |  |
| `YLD_OCC_SAVE` | yield | MAPPED | D:yield; P:yield | channels/yield.ts | `SaveYieldOccupancyRule` | yieldX |  |
| `YLD_OVERRIDE` | yield | MAPPED | D:yield; P:yield | channels/yield.ts | `SetYieldOverride` | yieldX | precheck;  |
| `YLD_PLAN_SAVE` | yield | MAPPED | D:yield; P:yield | channels/yield.ts | `SaveYieldPlan` | yieldX |  |
| `YLD_PROPOSE` | yield | MAPPED | D:yield; P:yield | channels/yield.ts | `ProposeYieldPublish` | yieldX, approvals | precheck;  |
| `YLD_PUBLISH` | yield | MAPPED | D:yield; P:yield | channels/yield.ts | `PublishYieldTariffs` | yieldX, approvals | precheck;  |
| `YLD_RESTRICTION_SAVE` | yield | MAPPED | D:yield; P:yield | channels/yield.ts | `SaveYieldRestriction` | yieldX |  |
| `YLD_SEASON_SAVE` | yield | MAPPED | D:yield; P:yield | channels/yield.ts | `SaveYieldSeason` | yieldX |  |
| `CLOSE_RUN` | closing | MAPPED | D:closing; P:closing | (factory in closing).ts | `PostRoomNights` | bookings | precheck;  |
| `CORRECTION_ADJUST` | closing | UNMAPPED | D:closing | closing.ts | — | — | Server corrections (RequestAdjustment, action correction) reverse one whole posted charge line by id; the prototype posts a free-form signed amount for a closed date. Needs a charge picker (UI) and folio/charge ids from the money area (W4-3). |
| `CORRECTION_ADJUST_DECIDE` | closing | UNMAPPED | D:approvals,closing; P:approvals | closing.ts | — | — | DecideApproval needs the approval id + version from the approvals slice, which the money area (W4-3) owns; map it there together with SHIFT_DIFFERENCE_DECIDE. |
| `DAY_CLOSE` | closing | MAPPED | D:closing; P:closing | (factory in closing).ts | `RunBusinessDayClose` + `OpenShift` | date, closedDates, closings, bookings, clock | precheck;  |
| `NEXT_DAY` | closing | MAPPED | D:closing; P:closing | (factory in closing).ts | `RunBusinessDayClose` + `OpenShift` | date, closedDates, closings, bookings, clock | precheck;  |
| `NIGHT_POST` | closing | MAPPED | — | (factory in closing).ts | `PostRoomNights` | bookings | precheck;  |
| `POST_NIGHTS` | commands.ts (core switch) | UNMAPPED | — | closing.ts | — | — | M1 compatibility command with no UI button; CLOSE_RUN covers it. |
| `SHIFT_CLOSE` | closing | **ABSENT** | — | — | — | — | **ABSENT**: no mapper and no `unmapped` entry; the runtime refuses it with the same "Not available on the server yet" text. |
| `CHECKIN` | guests | MAPPED | D:arrivals; P:arrivals,calendar,overview,reservations,rooms; booking dialog | frontdesk.ts | `CheckIn` | bookings, guests |  |
| `CHECKOUT` | folio | MAPPED | D:arrivals,inhouse; P:arrivals,calendar,inhouse,overview,+2; booking dialog | frontdesk.ts | `CheckoutStay` + `IssueInvoice` | bookings |  |
| `GUEST_ID_VIEW` | guests | UNMAPPED | D:arrivals,customers,guests,inhouse; P:arrivals,calendar,guests,inhouse,+3; booking dialog | frontdesk / channels (listed by both) | — | — | [frontdesk] deliberately not a mapper: run() returns no result and the number must never enter state. Production uses guestid.ts / guestid-ui.tsx (GuestIdRevealButton calls RevealGuestId: PERM-GST-003, transient dialog, nothing stored) \|\| [channels] RevealGuestId returns the number in the command result but the screen reads it from the guest record (guestRevealId); needs a shared hook — left to the guests area (W4-1) |
| `GUEST_SAVE` | guests | MAPPED | D:arrivals,customers,guests,inhouse; P:arrivals,calendar,guests,inhouse,+3; booking dialog | frontdesk.ts | `RegisterGuest` + `SaveGuestProfile` | guests, bookings | precheck;  |
| `MOVE` | guests | MAPPED | D:arrivals,inhouse; P:arrivals,calendar,inhouse,overview,+2; booking dialog | frontdesk.ts | `AmendReservation` + `MoveRoom` + `ExtendStay` + `ShortenStay` | bookings | precheck;  |
| `REGISTER` | guests | MAPPED | D:arrivals; P:arrivals,calendar,overview,reservations,rooms; booking dialog | frontdesk.ts | `RegisterGuest` | bookings, guests | precheck;  |
| `INV_COUNT_POST` | inventory | MAPPED | D:inventory; P:inventory | maintenance.ts | `StockCountDecide` | inventoryX | precheck;  |
| `INV_COUNT_SUBMIT` | inventory | MAPPED | D:inventory; P:inventory | maintenance.ts | `StockCountSubmit` | inventoryX |  |
| `INV_GRN` | inventory | MAPPED | D:inventory; P:inventory | maintenance.ts | `StockReceiveGrn` | inventoryX | precheck;  |
| `INV_ISSUE` | inventory | MAPPED | D:inventory; P:inventory | maintenance.ts | `StockIssue` | inventoryX |  |
| `INV_ITEM_SET` | inventory | MAPPED | D:inventory | maintenance.ts | `StockItemSet` | inventoryX |  |
| `INV_LAUNDRY_RECEIVE` | inventory | MAPPED | D:inventory; P:inventory | maintenance.ts | `StockLaundryReceive` | inventoryX |  |
| `INV_LAUNDRY_SEND` | inventory | MAPPED | D:inventory; P:inventory | maintenance.ts | `StockLaundrySend` | inventoryX | precheck;  |
| `INV_LOSS` | inventory | MAPPED | D:inventory; P:inventory | maintenance.ts | `StockRecordLoss` | inventoryX | precheck;  |
| `INV_ROOM_CHANGE` | inventory | MAPPED | D:inventory; P:inventory | maintenance.ts | `StockRoomChange` | inventoryX |  |
| `INV_USE` | inventory | MAPPED | D:inventory; P:inventory | maintenance.ts | `StockUse` | inventoryX |  |
| `MNT_AMC_SAVE` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `SaveAmc` | maintenanceX | precheck;  |
| `MNT_ASSET_SAVE` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `SaveMaintenanceAsset` | maintenanceX | precheck;  |
| `MNT_PLAN_SAVE` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `SavePmPlan` | maintenanceX | precheck;  |
| `MNT_PM_GENERATE` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `GeneratePmWorkOrders` | maintenanceX |  |
| `MNT_SPARE_RECEIVE` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `ReceiveSpare` | maintenanceX | precheck;  |
| `MNT_VENDOR_SAVE` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `SaveMaintenanceVendor` | maintenanceX | precheck;  |
| `MNT_WO_ASSIGN` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `AssignWorkOrder` | maintenanceX |  |
| `MNT_WO_BLOCK` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `BlockWorkOrderRoom` | maintenanceX |  |
| `MNT_WO_CANCEL` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `CancelWorkOrder` | maintenanceX | precheck;  |
| `MNT_WO_COMPLETE` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `CompleteWorkOrder` | maintenanceX | precheck;  |
| `MNT_WO_CREATE` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `CreateWorkOrder` | maintenanceX | precheck;  |
| `MNT_WO_FROM_REPAIR` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `ConvertRepairToWorkOrder` | maintenanceX |  |
| `MNT_WO_HOLD` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `HoldWorkOrder` | maintenanceX | precheck;  |
| `MNT_WO_PART` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `AddWorkOrderPart` | maintenanceX | precheck;  |
| `MNT_WO_START` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `StartWorkOrder` | maintenanceX |  |
| `MNT_WO_VERIFY` | maintenance | MAPPED | D:maintenance; P:maintenance | maintenance.ts | `VerifyWorkOrder` | maintenanceX | precheck;  |
| `APPROVAL` | folio | MAPPED | D:approvals,folios; P:approvals,folios; booking dialog | money.ts | `DecideCreditNote` + `DecideApproval` | bookings, approvals |  |
| `CB_LOCK` | cashbook | MAPPED | D:cashbook; P:cashbook | money.ts | `LockCashBookDay` | cashbookX | precheck;  |
| `CB_REOPEN` | cashbook | MAPPED | D:cashbook; P:cashbook | money.ts | `ReopenCashBookDay` | cashbookX | precheck;  |
| `CHARGE` | folio | MAPPED | D:arrivals,inhouse; P:arrivals,calendar,inhouse,overview,+2; booking dialog | money.ts | `PostCharge` | bookings, approvals | precheck;  |
| `COMPANY_CREDIT_REQUEST` | folio | MAPPED | D:arrivals,inhouse; P:arrivals,calendar,inhouse,overview,+2; booking dialog | money.ts | `RequestAdjustment` | bookings, approvals | precheck;  |
| `CORRECTION_REQUEST` | folio | MAPPED | booking dialog | money.ts | `RequestAdjustment` | bookings, approvals | precheck;  |
| `CREDIT_NOTE` | folio | MAPPED | booking dialog | money.ts | `RequestCreditNote` | bookings, approvals | precheck;  |
| `DISCOUNT` | folio | MAPPED | D:arrivals,inhouse; P:arrivals,calendar,inhouse,overview,+2; booking dialog | money.ts | `RequestAdjustment` | bookings, approvals | precheck;  |
| `GRP_BLOCK_ALLOT` | groups | UNMAPPED | D:groups; P:groups | money.ts | — | — | no server command changes a block’s allotment (inventory backlog) |
| `GRP_BLOCK_CANCEL` | groups | UNMAPPED | D:groups; P:groups | money.ts | — | — | no server command cancels a group block |
| `GRP_BLOCK_CREATE` | groups | MAPPED | D:groups; P:groups | money.ts | `CreateGroupBlock` + `CreateGroup` | groupsX | precheck;  |
| `GRP_COMPANY_SAVE` | groups | MAPPED | D:groups; P:groups | money.ts | `SaveCompany` | groupsX | precheck;  |
| `GRP_LIMIT_DECIDE` | groups | MAPPED | D:groups; P:groups | money.ts | `DecideCompanyLimit` | groupsX |  |
| `GRP_LIMIT_SET` | groups | MAPPED | D:groups; P:groups | money.ts | `SetCompanyLimit` | groupsX | precheck;  |
| `GRP_RELEASE` | groups | UNMAPPED | D:groups; P:groups | money.ts | — | — | ReleaseGroupBlock needs the block version, which no server read returns yet; partial release has no server command |
| `GRP_ROOMING` | groups | UNMAPPED | D:groups; P:groups | money.ts | — | — | rooming-list pickup = one CreateReservation per row with the block — reservations area (W4-1) / backlog |
| `GRP_SWEEP` | groups | **ABSENT** | — | — | — | — | **ABSENT**: no mapper and no `unmapped` entry; the runtime refuses it with the same "Not available on the server yet" text. |
| `PAYMENT` | folio | MAPPED | D:arrivals,inhouse; P:arrivals,calendar,inhouse,overview,+2; booking dialog | money.ts | `RecordReceipt` | bookings, approvals | precheck;  |
| `REFUND_EXECUTE` | folio | MAPPED | D:folios; P:folios; booking dialog | money.ts | `ExecuteAdjustment` | bookings, approvals, shiftOpen, openingCash, shift, shiftClose | precheck;  |
| `REFUND_REQUEST` | folio | MAPPED | booking dialog | money.ts | `RequestAdjustment` | bookings, approvals | precheck;  |
| `SHIFT_DIFFERENCE_DECIDE` | closing | MAPPED | D:approvals,cashier; P:approvals,cashier | money.ts | `DecideApproval` | shiftOpen, openingCash, shift, shiftClose, approvals |  |
| `SHIFT_SUBMIT` | closing | MAPPED | D:cashier,closing; P:cashier,closing | money.ts | `SubmitShiftCount` | shiftOpen, openingCash, shift, shiftClose, approvals | precheck;  |
| `AI_ASK` | assistant | **ABSENT** | D:assistant; P:assistant | — | — | — | **ABSENT**: no mapper and no `unmapped` entry; the runtime refuses it with the same "Not available on the server yet" text. |
| `AI_CLEAR` | assistant | **ABSENT** | D:assistant | — | — | — | **ABSENT**: no mapper and no `unmapped` entry; the runtime refuses it with the same "Not available on the server yet" text. |
| `AI_SETTINGS` | assistant | **ABSENT** | D:settings; P:settings | — | — | — | **ABSENT**: no mapper and no `unmapped` entry; the runtime refuses it with the same "Not available on the server yet" text. |
| `POS_ITEM_ADD` | pos | MAPPED | D:pos; P:pos | pos.ts | `AddCheckItem` | posX | precheck;  |
| `POS_ITEM_QTY` | pos | MAPPED | D:pos; P:pos | pos.ts | `SetCheckItemQty` | posX | precheck;  |
| `POS_KOT_SEND` | pos | MAPPED | D:pos; P:pos | pos.ts | `SendKot` | posX |  |
| `POS_KOT_STATUS` | pos | MAPPED | D:pos; P:pos | pos.ts | `AdvanceKot` | posX |  |
| `POS_MENU_TOGGLE` | pos | MAPPED | D:pos,settings; P:pos,settings | pos.ts | `SetMenuItemAvailability` | posX |  |
| `POS_ORDER_OPEN` | pos | MAPPED | D:pos; P:pos | pos.ts | `OpenCheck` | posX | precheck;  |
| `POS_SETTLE` | pos | MAPPED | D:pos; P:pos | pos.ts | `SettleCheck` | posX | precheck;  |
| `POS_VOID_BILL` | pos | MAPPED | D:pos; P:pos | pos.ts | `VoidBill` | posX, approvals | precheck;  |
| `POS_VOID_DECIDE` | pos | MAPPED | D:approvals,pos; P:approvals,pos | pos.ts | `DecideVoid` | posX, approvals |  |
| `POS_VOID_ITEM` | pos | MAPPED | D:pos; P:pos | pos.ts | `VoidCheckItem` | posX, approvals | precheck;  |
| `BOOK` | reservations | MAPPED | D:arrivals,calendar,folios,housekeeping,+3; P:arrivals,calendar,overview,reservations,rooms; booking dialog | reservations.ts | `QuoteStay` + `CreateReservation` | bookings | precheck; typed-client result tracked;  |
| `CANCEL` | reservations | MAPPED | D:arrivals; P:arrivals,calendar,overview,reservations,rooms; booking dialog | reservations.ts | `MarkNoShow` + `CancelReservation` | bookings |  |
| `CHANGE_DATES` | reservations | MAPPED | D:arrivals,inhouse; P:arrivals,calendar,inhouse,overview,+2; booking dialog | reservations.ts | `AmendReservation` | bookings | precheck;  |
| `CONFIRM` | reservations | MAPPED | booking dialog | reservations.ts | `ConfirmHold` | bookings |  |
| `BACKUP_SIMULATE` | housekeeping | UNMAPPED | D:backup; P:backup | rooms.ts | — | — | demonstration only (audit-only simulated backup); real backups are run by the server deployment, not from the app |
| `BLOCK` | housekeeping | MAPPED | D:calendar,housekeeping,overview,rooms; P:rooms | rooms.ts | `UnblockRoom` + `BlockRoom` | rooms, tasks | precheck;  |
| `REPAIR_REQUEST` | housekeeping | MAPPED | D:calendar,housekeeping,overview,rooms; P:housekeeping,rooms | rooms.ts | `RequestRepair` | tasks, rooms | precheck;  |
| `STAYOVER_GENERATE` | housekeeping | MAPPED | D:housekeeping; P:housekeeping | rooms.ts | `GenerateStayoverTasks` | tasks |  |
| `TASK` | housekeeping | MAPPED | D:housekeeping; P:housekeeping | rooms.ts | `TransitionHousekeepingTask` | tasks, rooms | precheck;  |
| `UNBLOCK` | housekeeping | MAPPED | D:calendar,housekeeping,overview,rooms; P:housekeeping,rooms | rooms.ts | `UnblockRoom` | rooms, tasks | precheck;  |
| `GSV_COMPLAINT_FOLLOWUP` | services | MAPPED | D:guest-services; P:guest-services | services/guest.ts | `RecordComplaintFollowUp` | servicesX | precheck;  |
| `GSV_COMPLAINT_LOG` | services | MAPPED | D:guest-services; P:guest-services; booking dialog | services/guest.ts | `LogComplaint` | servicesX | precheck;  |
| `GSV_COMPLAINT_RECOVERY` | services | MAPPED | D:guest-services; P:guest-services | services/guest.ts | `OfferRecoveryDiscount` | servicesX, bookings | precheck;  |
| `GSV_COMPLAINT_RESOLVE` | services | MAPPED | D:guest-services; P:guest-services | services/guest.ts | `ResolveComplaint` | servicesX | precheck;  |
| `GSV_COMPLAINT_UPDATE` | services | MAPPED | D:guest-services; P:guest-services | services/guest.ts | `UpdateComplaint` | servicesX |  |
| `GSV_LF_CLAIM` | services | MAPPED | D:guest-services; P:guest-services | services/guest.ts | `ClaimLostItem` | servicesX | precheck;  |
| `GSV_LF_DISPOSE` | services | MAPPED | D:guest-services; P:guest-services | services/guest.ts | `DisposeLostItem` | servicesX |  |
| `GSV_LF_HANDOVER` | services | MAPPED | D:guest-services; P:guest-services | services/guest.ts | `HandOverLostItem` | servicesX |  |
| `GSV_LF_LOG` | services | MAPPED | D:gsv-my-requests,guest-services; P:gsv-my-requests,guest-services | services/guest.ts | `LogLostItem` | servicesX | precheck;  |
| `GSV_REQUEST_ASSIGN` | services | MAPPED | D:gsv-my-requests,guest-services; P:gsv-my-requests,guest-services | services/guest.ts | `AssignServiceRequest` | servicesX | precheck;  |
| `GSV_REQUEST_CANCEL` | services | MAPPED | D:gsv-my-requests,guest-services; P:gsv-my-requests,guest-services | services/guest.ts | `CancelServiceRequest` | servicesX | precheck;  |
| `GSV_REQUEST_COMPLETE` | services | MAPPED | D:gsv-my-requests,guest-services; P:gsv-my-requests,guest-services | services/guest.ts | `CompleteServiceRequest` | servicesX | precheck;  |
| `GSV_REQUEST_CREATE` | services | MAPPED | D:guest-services; P:guest-services; booking dialog | services/guest.ts | `LogServiceRequest` | servicesX | precheck;  |
| `GSV_REQUEST_START` | services | MAPPED | D:gsv-my-requests,guest-services; P:gsv-my-requests,guest-services | services/guest.ts | `StartServiceRequest` | servicesX |  |
| `GSV_SETTINGS` | services | MAPPED | D:settings; P:settings | services/guest.ts | `SaveGuestServiceSettings` | servicesX |  |
| `HOV_CARRY_DONE` | handover | MAPPED | D:handover; P:handover | services/handover.ts | `CompleteCarryItem` | handoverX |  |
| `HOV_CLOCK` | handover | UNMAPPED | D:handover; P:handover | services.ts | — | — | demo-only simulated clock; production uses the real property clock (the server has no such command by design) |
| `HOV_ITEM` | handover | MAPPED | D:handover; P:handover | services/handover.ts | `TickChecklistItem` | handoverX |  |
| `HOV_NOTE_ACK` | handover | MAPPED | D:handover; P:handover | services/handover.ts | `AcknowledgeHandoverNote` | handoverX |  |
| `HOV_NOTE_SUBMIT` | handover | MAPPED | D:handover; P:handover | services/handover.ts | `SubmitHandoverNote` | handoverX |  |
| `HOV_RUN_REOPEN` | handover | MAPPED | D:handover | services/handover.ts | `ReopenChecklistRun` | handoverX | precheck;  |
| `HOV_RUN_SUBMIT` | handover | MAPPED | D:handover; P:handover | services/handover.ts | `SubmitChecklistRun` | handoverX |  |
| `HOV_TEMPLATE_SAVE` | handover | MAPPED | D:handover; P:handover | services/handover.ts | `SaveHandoverTemplate` | handoverX | precheck;  |
| `HOV_TEMPLATE_TOGGLE` | handover | MAPPED | D:handover | services/handover.ts | `ToggleHandoverTemplate` | handoverX |  |
| `MSG_CONSENT` | messaging | MAPPED | D:messages; P:messages | services/messaging.ts | `SetMessagingConsent` | messagingX |  |
| `MSG_DOC_CONFIRM` | messaging | UNMAPPED | D:documents; P:documents | services / channels (listed by both) | — | — | [services] document capture: W4-11 (applies to guest profile / expense draft, other areas) \|\| [channels] maps to ConfirmIdCapture once captures exist (captureId + expectedVersion); depends on MSG_DOC_EXTRACT and the docs slice owner |
| `MSG_DOC_DISCARD` | messaging | UNMAPPED | D:documents; P:documents | services / channels (listed by both) | — | — | [services] document capture: W4-11 \|\| [channels] maps to RejectIdCapture once captures exist; same dependency |
| `MSG_DOC_EXTRACT` | messaging | UNMAPPED | D:documents; P:documents | services / channels (listed by both) | — | — | [services] document capture (simulated OCR of fictional samples) belongs to the ID/document capture area (W4-11 / Documents module); no messaging server command \|\| [channels] ID capture on the server needs the real image of a guest document (CaptureIdDocument: guestId + image); the prototype extracts from fictional samples — needs a capture screen (and messagingX.docs belongs to W4-10) |
| `MSG_RETRY` | messaging | MAPPED | D:messages; P:messages | services/messaging.ts | `RetryMessage` | messagingX |  |
| `MSG_RULE` | messaging | MAPPED | D:messages | services/messaging.ts | `UpdateMessageRule` | messagingX |  |
| `MSG_RUN_RULES` | messaging | MAPPED | D:messages; P:messages | services/messaging.ts | `RunMessagingTriggers` | messagingX |  |
| `MSG_SEND` | messaging | MAPPED | D:messages; P:messages | services/messaging.ts | `SendMessage` | messagingX | precheck;  |
| `MSG_SIMULATE` | messaging | MAPPED | D:messages; P:messages | services/messaging.ts | `SimulateMessageDelivery` | messagingX |  |
| `MSG_TEMPLATE` | messaging | MAPPED | D:messages | services/messaging.ts | `UpdateMessageTemplate` | messagingX |  |
| `HR_ADVANCE_ADD` | staff | MAPPED | D:staff; P:staff | staff.ts | `GiveAdvance`† | staffX |  |
| `HR_ATT_MARK` | staff | MAPPED | D:my-hr,staff; P:my-hr,staff | staff.ts | `MarkAttendance`† | staffX |  |
| `HR_FNF_CONFIRM` | staff | MAPPED | D:staff; P:staff | staff.ts | `ConfirmFnf`† | staffX |  |
| `HR_FNF_DISCARD` | staff | MAPPED | D:staff; P:staff | staff.ts | `DiscardFnf`† | staffX |  |
| `HR_FNF_SETTLE` | staff | MAPPED | D:staff; P:staff | staff.ts | `PrepareFnf`† | staffX |  |
| `HR_LEAVE_CANCEL` | staff | MAPPED | D:my-hr,staff; P:my-hr,staff | staff.ts | `CancelLeave`† | staffX |  |
| `HR_LEAVE_DECIDE` | staff | MAPPED | D:staff; P:staff | staff.ts | `DecideLeave`† | staffX |  |
| `HR_LEAVE_POLICY_SET` | staff | MAPPED | D:approvals,settings,staff; P:approvals,settings,staff | staff.ts | `SetLeavePolicy` + `SaveDepartment` | staffX |  |
| `HR_LEAVE_REQUEST` | staff | MAPPED | D:my-hr,staff; P:my-hr,staff | staff.ts | `RequestLeave`† | staffX |  |
| `HR_PAYROLL_BANK_FILE` | staff | MAPPED | D:staff; P:staff | staff.ts | `IssueBankFile`† | staffX |  |
| `HR_PAYROLL_LOCK` | staff | MAPPED | D:staff; P:staff | staff.ts | `LockPayRun`† | staffX |  |
| `HR_PAYROLL_RUN` | staff | MAPPED | D:staff; P:staff | staff.ts | `PreparePayRun`† | staffX |  |
| `HR_PAYROLL_UNLOCK` | staff | MAPPED | D:staff; P:staff | staff.ts | `UnlockPayRun`† | staffX |  |
| `HR_PAYSLIP_SEND` | staff | MAPPED | D:my-hr,staff; P:my-hr,staff | staff.ts | `SendPayslip`† | staffX |  |
| `HR_RATES_SET` | staff | MAPPED | — | staff.ts | `SetStatutoryRates`† | staffX |  |
| `HR_ROSTER_COPY` | staff | MAPPED | D:staff; P:staff | staff.ts | `CopyRosterWeek`† | staffX |  |
| `HR_ROSTER_SET` | staff | MAPPED | D:staff; P:staff | staff.ts | `SetRoster`† | staffX |  |
| `HR_STAFF_ACTIVE` | staff | MAPPED | D:staff; P:staff | staff.ts | `SetStaffActive`† | staffX |  |
| `HR_STAFF_SAVE` | staff | MAPPED | D:staff; P:staff | staff.ts | `SaveStaff` | staffX | precheck;  |
| `HR_STAT_RATES_SET` | staff | MAPPED | D:approvals,settings,staff; P:approvals,settings,staff | staff.ts | `SetStatutoryRates`† | staffX |  |

<!-- END appendixB -->

<!-- BEGIN prodOnlyTable -->
| Mapper type | Sync area file | Server command(s) | Reload | Dispatched from (UI source) |
|---|---|---|---|---|
| `INVOICE_ISSUE` | money.ts | `IssueInvoice` | bookings, approvals | features/folio/modals.tsx, mobile/money/Folio.tsx |
| `USER_CREDENTIAL` | admin.ts | `SetCredential` | users | clients/sync/credentials.ts |
| `DEVICE_REGISTER` | admin.ts | `RegisterDevice` |  | clients/sync/credentials.ts |
| `DEVICE_REVOKE` | admin.ts | `RevokeDevice` |  | clients/sync/credentials.ts |
| `HR_DEPT_SAVE` | staff.ts | `SaveDepartment` | staffX | features/staff/modals.tsx |
| `INV_ITEM_CREATE` | maintenance.ts | `StockItemCreate` | inventoryX | features/inventory/modals.tsx |
| `SHIFT_OPEN` | cashier.ts | `OpenShift` | shiftOpen, openingCash, shift, shiftClose, cashbookX | clients/sync/cashier-ui.tsx |

<!-- END prodOnlyTable -->

<!-- BEGIN desktopTable -->
| Screen id | Component (file) | Nav / permission | Own commands | Mapped | Unmapped | Absent | Booking dialog | Slices read that a loader fills | Slices read with NO loader (left as empty skeleton) | Notes |
|---|---|---|---:|---:|---|---|---|---|---|---|
| `access` | AccessControl (`features/admin/AccessControl.tsx`) | Access control — admin | 6 | 4 | 2: GRANT, POLICY_SET | 0 | no | `date`, `grants`, `settings`, `users` + `approvals`, `bookings` (via selectors) | — |  |
| `accounting` | AccountingPage (`features/accounting/Accounting.tsx`) | Accounting — reports | 4 | 4 | 0 | 0 | yes | `date`, `users` + `bookings`, `closedDates`, `expenses`, `openingCash`, `posX` (via selectors) | — |  |
| `approvals` | ApprovalsAudit (`features/admin/ApprovalsAudit.tsx`) | Approvals & audit — reports | 10 | 8 | 2: CORRECTION_ADJUST_DECIDE, POLICY_DECIDE | 0 | no | `audit`, `bookings`, `clock`, `expenses`, `users` + `approvals` (via selectors) | — | +9 dispatched dynamically (POLICY_DECIDE, CFG_DECIDE, SHIFT_DIFFERENCE_DECIDE, CORRECTION_ADJUST_DECIDE, APPROVAL, POS_VOID_DECIDE, YLD_DECIDE, HR_STAT_RATES_SET, HR_LEAVE_POLICY_SET) |
| `arrivals` | ArrivalsPage (`features/guests/CheckIn.tsx`) | Arrivals & check-in — checkin | 13 | 12 | 1: GUEST_ID_VIEW | 0 | yes | `bookings`, `date`, `guests`, `rooms`, `settings` + `approvals` (via selectors) | — |  |
| `assistant` | AssistantPage (`features/assistant/Assistant.tsx`) | Management assistant — reports | 2 | 0 | 0 | 2: AI_ASK, AI_CLEAR | no | — + `approvals`, `bookings`, `closedDates`, `expenses`, `openingCash`, `rooms`, `shiftOpen`, `tasks` (via selectors) | `assistantX` |  |
| `backup` | Backup (`features/housekeeping/Backup.tsx`) | Backup & recovery — admin | 1 | 0 | 1: BACKUP_SIMULATE | 0 | no | — + `audit` (via selectors) | — |  |
| `booking-site` | BookingSitePage (`features/channels/BookingSite.tsx`) | Direct booking site — reservations | 3 | 2 | 1: CHN_WEB_REQUEST | 0 | yes | `bookings`, `date`, `settings`, `users` + `channelsX`, `rooms` (via selectors) | — |  |
| `calendar` | Calendar (`features/reservations/Calendar.tsx`) | Calendar — reservations | 4 | 4 | 0 | 0 | yes | `bookings`, `date`, `rooms`, `tasks`, `users` | — | BOOK via typed client |
| `cashbook` | CashBook (`features/cashbook/CashBook.tsx`) | Cash book — reports | 2 | 2 | 0 | 0 | no | `clock`, `date`, `settings`, `users` + `bookings`, `closedDates`, `expenses`, `openingCash`, `posX`, `rooms` (via selectors) | — |  |
| `cashier` | Cashier (`features/closing/Cashier.tsx`) | Cashier shift — payments | 2 | 2 | 0 | 0 | yes | `approvals`, `date`, `users` + `bookings`, `closedDates`, `expenses`, `openingCash`, `shiftOpen` (via selectors) | — |  |
| `channels` | ChannelsPage (`features/channels/Channels.tsx`) | OTA channels — reservations | 7 | 7 | 0 | 0 | yes | `date`, `settings`, `users` + `bookings`, `channelsX`, `rooms` (via selectors) | — |  |
| `closing` | DayClosing (`features/closing/DayClosing.tsx`) | Day closing — [close,reports] | 6 | 4 | 2: CORRECTION_ADJUST, CORRECTION_ADJUST_DECIDE | 0 | yes | `approvals`, `bookings`, `clock`, `closedDates`, `date`, `settings`, `users` + `expenses`, `openingCash`, `shiftOpen` (via selectors) | — |  |
| `compliance` | CompliancePage (`features/compliance/CompliancePage.tsx`) | Compliance calendar — reports | 7 | 7 | 0 | 0 | no | `audit`, `clock`, `date`, `users` + `complianceX` (via selectors) | — |  |
| `customers` | CustomersPage (`features/customers/Customers.tsx`) | Customers — billing | 2 | 1 | 1: GUEST_ID_VIEW | 0 | yes | `bookings`, `date`, `guests`, `settings` + `servicesX` (via selectors) | — |  |
| `documents` | MsgDocumentsPage (`features/messaging/Documents.tsx`) | Document capture — checkin | 3 | 0 | 3: MSG_DOC_CONFIRM, MSG_DOC_DISCARD, MSG_DOC_EXTRACT | 0 | no | `guests` + `messagingX` (via selectors) | — |  |
| `expenses` | Expenses (`features/expenses/Expenses.tsx`) | Expenses — [expenses,reports] | 4 | 4 | 0 | 0 | yes | `approvals`, `bookings`, `date`, `expenses`, `shiftOpen`, `users` | — |  |
| `folios` | Folios (`features/folio/Folios.tsx`) | Guests & folios — [billing,reports] | 3 | 3 | 0 | 0 | yes | `approvals`, `bookings`, `date`, `rooms`, `users` | — | BOOK via typed client |
| `groups` | GroupsPage (`features/groups/Groups.tsx`) | Groups & corporate — reservations | 8 | 4 | 4: GRP_BLOCK_ALLOT, GRP_BLOCK_CANCEL, GRP_RELEASE, GRP_ROOMING | 0 | yes | `clock`, `date`, `rooms`, `settings`, `users` + `bookings`, `groupsX` (via selectors) | — |  |
| `gsv-my-requests` | GsvMyRequestsPage (`features/services/GuestServices.tsx`) | Guest requests — housekeeping | 5 | 5 | 0 | 0 | no | `clock`, `rooms`, `users` + `servicesX` (via selectors) | — |  |
| `guest-services` | GuestServicesPage (`features/services/GuestServices.tsx`) | Guest services — reservations | 14 | 14 | 0 | 0 | yes | `bookings`, `clock`, `rooms`, `settings`, `users` + `approvals`, `servicesX` (via selectors) | — |  |
| `guests` | GuestsPage (`features/guests/GuestProfile.tsx`) | (no nav item) | 2 | 1 | 1: GUEST_ID_VIEW | 0 | yes | `bookings`, `guests`, `settings` | — |  |
| `handover` | HandoverPage (`features/handover/HandoverPage.tsx`) | Checklists & handover — [housekeeping,payments] | 9 | 8 | 1: HOV_CLOCK | 0 | no | `audit`, `clock`, `closedDates`, `date`, `users` + `handoverX` (via selectors) | — |  |
| `home` | ModulesHome (`app/ModulesHome.tsx`) | home (modules grid) | 0 | 0 | 0 | 0 | no | `approvals`, `bookings`, `expenses` | — |  |
| `housekeeping` | Housekeeping (`features/housekeeping/Housekeeping.tsx`) | Housekeeping — housekeeping | 6 | 6 | 0 | 0 | yes | `bookings`, `clock`, `date`, `rooms`, `tasks`, `users` | — | BOOK via typed client |
| `inhouse` | InHousePage (`features/guests/InHouse.tsx`) | In-house stays — billing | 9 | 8 | 1: GUEST_ID_VIEW | 0 | yes | `bookings`, `date`, `guests`, `rooms`, `settings` + `approvals` (via selectors) | — |  |
| `inventory` | InventoryPage (`features/inventory/InventoryPage.tsx`) | Linen & stock — housekeeping | 11 | 11 | 0 | 0 | no | `clock`, `date`, `rooms`, `settings`, `users` + `bookings`, `inventoryX` (via selectors) | — |  |
| `maintenance` | MaintenancePage (`features/maintenance/Maintenance.tsx`) | Maintenance — housekeeping | 16 | 16 | 0 | 0 | no | `clock`, `date`, `rooms`, `settings`, `users` + `bookings`, `maintenanceX`, `tasks` (via selectors) | — |  |
| `messages` | MsgMessagesPage (`features/messaging/Messages.tsx`) | Messages — reservations | 7 | 7 | 0 | 0 | yes | `bookings`, `clock`, `guests`, `messagingX` | — |  |
| `my-hr` | MyHr (`features/staff/MyHr.tsx`) | My shifts & pay — [housekeeping,reservations,payments,reports,admin] | 4 | 4 | 0 | 0 | no | `clock`, `date`, `settings`, `users` + `closedDates`, `staffX` (via selectors) | — |  |
| `overview` | Overview (`features/overview/Overview.tsx`) | Overview — reports | 4 | 4 | 0 | 0 | yes | `bookings`, `date`, `rooms`, `settings`, `tasks`, `users` + `audit`, `complianceX`, `expenses`, `openingCash`, `posX` (via selectors) | — | BOOK via typed client |
| `pos` | PosPage (`features/pos/PosPage.tsx`) | Restaurant & room service — billing | 10 | 10 | 0 | 0 | yes | `bookings`, `clock`, `date`, `settings`, `users` + `approvals`, `closedDates`, `posX` (via selectors) | — |  |
| `reports` | Reports (`features/reports/Reports.tsx`) | Reports — reports | 0 | 0 | 0 | 0 | yes | `bookings`, `clock`, `closedDates`, `date`, `expenses`, `openingCash`, `posX`, `rooms`, `settings` | — |  |
| `reservations` | Reservations (`features/reservations/Reservations.tsx`) | Reservations — reservations | 1 | 1 | 0 | 0 | yes | `bookings`, `clock`, `date`, `rooms` | — | BOOK via typed client |
| `roadmap` | Roadmap (`features/housekeeping/Roadmap.tsx`) | Roadmap — reports | 0 | 0 | 0 | 0 | no | — | — |  |
| `rooms` | RoomBoard (`features/rooms/RoomBoard.tsx`) | Room board — reservations | 4 | 4 | 0 | 0 | yes | `bookings`, `date`, `rooms`, `tasks`, `users` | — | BOOK via typed client |
| `settings` | HotelSettings (`features/admin/HotelSettings.tsx`) | Master configuration — admin | 12 | 10 | 1: TARIFF_SCHEDULE | 1: AI_SETTINGS | no | `date`, `rooms`, `settings`, `users` + `approvals`, `bookings`, `configHistory`, `tasks` (via selectors) | `tariffs` | +7 dispatched dynamically (YLD_GUARD_SET, GSV_SETTINGS, POS_MENU_TOGGLE, AI_SETTINGS, HR_STAT_RATES_SET, HR_LEAVE_POLICY_SET, CMP_SETTINGS_SET) |
| `staff` | HrDesk (`features/staff/HrDesk.tsx`) | Staff & payroll — reports | 19 | 19 | 0 | 0 | no | `clock`, `date`, `settings`, `users` + `closedDates`, `staffX` (via selectors) | — |  |
| `yield` | YieldPage (`features/yield/YieldPage.tsx`) | Tariff & yield — reports | 10 | 10 | 0 | 0 | no | `closedDates`, `date`, `users` + `approvals`, `bookings`, `rooms`, `yieldX` (via selectors) | `tariffs` |  |

<!-- END desktopTable -->

<!-- BEGIN phoneTable -->
| Screen id | Component (file) | Nav / permission | Own commands | Mapped | Unmapped | Absent | Booking dialog | Slices read that a loader fills | Slices read with NO loader (left as empty skeleton) | Notes |
|---|---|---|---:|---:|---|---|---|---|---|---|
| `access` | MaAccess (`mobile/admin/Access.tsx`) | Access control — admin | 3 | 3 | 0 | 0 | no | `date`, `users` + `approvals`, `bookings` (via selectors) | — |  |
| `accounting` | AccountingMobile (`features/accounting/Mobile.tsx`) | Accounting — reports | 4 | 4 | 0 | 0 | yes | `date`, `users` + `bookings`, `closedDates`, `expenses`, `openingCash`, `posX` (via selectors) | — |  |
| `approvals` | MaApprovals (`mobile/admin/Approvals.tsx`) | Approvals & audit — reports | 11 | 9 | 2: CORRECTION_ADJUST_DECIDE, POLICY_DECIDE | 0 | no | `audit`, `bookings`, `clock`, `date`, `expenses`, `users` + `approvals` (via selectors) | — | +9 dispatched dynamically (POLICY_DECIDE, CFG_DECIDE, SHIFT_DIFFERENCE_DECIDE, CORRECTION_ADJUST_DECIDE, APPROVAL, POS_VOID_DECIDE, YLD_DECIDE, HR_STAT_RATES_SET, HR_LEAVE_POLICY_SET) |
| `arrivals` | MfArrivals (`mobile/frontdesk/Arrivals.tsx`) | Arrivals & check-in — checkin | 14 | 13 | 1: GUEST_ID_VIEW | 0 | yes | `approvals`, `bookings`, `date`, `guests`, `rooms`, `settings` | — |  |
| `assistant` | AssistantMobile (`features/assistant/Assistant.tsx`) | Management assistant — reports | 1 | 0 | 0 | 1: AI_ASK | no | — + `approvals`, `bookings`, `closedDates`, `expenses`, `openingCash`, `rooms`, `shiftOpen`, `tasks` (via selectors) | `assistantX` |  |
| `backup` | MaBackup (`mobile/admin/Backup.tsx`) | Backup & recovery — admin | 1 | 0 | 1: BACKUP_SIMULATE | 0 | no | — + `audit` (via selectors) | — |  |
| `booking-site` | BookingSiteMobile (`features/channels/BookingSite.tsx`) | Direct booking site — reservations | 3 | 2 | 1: CHN_WEB_REQUEST | 0 | yes | `bookings`, `date`, `settings`, `users` + `channelsX`, `rooms` (via selectors) | — |  |
| `calendar` | MfCalendar (`mobile/frontdesk/Calendar.tsx`) | Calendar — reservations | 14 | 13 | 1: GUEST_ID_VIEW | 0 | yes | `approvals`, `bookings`, `date`, `guests`, `rooms`, `settings` | — |  |
| `cashbook` | CashBookMobile (`features/cashbook/CashBook.tsx`) | Cash book — reports | 2 | 2 | 0 | 0 | no | `clock`, `date`, `settings`, `users` + `bookings`, `closedDates`, `expenses`, `openingCash`, `posX`, `rooms` (via selectors) | — |  |
| `cashier` | MmCashier (`mobile/money/Cashier.tsx`) | Cashier shift — payments | 2 | 2 | 0 | 0 | yes | `approvals`, `date`, `users` + `bookings`, `closedDates`, `expenses`, `openingCash`, `shiftOpen` (via selectors) | — |  |
| `channels` | ChannelsMobile (`features/channels/ChannelsMobile.tsx`) | OTA channels — reservations | 7 | 7 | 0 | 0 | yes | `date`, `settings` + `bookings`, `channelsX`, `rooms` (via selectors) | — |  |
| `closing` | MaClosing (`mobile/admin/Closing.tsx`) | Day closing — [close,reports] | 4 | 4 | 0 | 0 | yes | `clock`, `date`, `settings`, `users` + `approvals`, `bookings`, `closedDates`, `expenses`, `openingCash`, `shiftOpen` (via selectors) | — |  |
| `compliance` | ComplianceMobile (`features/compliance/CompliancePage.tsx`) | Compliance calendar — reports | 7 | 7 | 0 | 0 | no | `audit`, `clock`, `date`, `users` + `complianceX` (via selectors) | — |  |
| `documents` | MsgDocumentsMobile (`features/messaging/Documents.tsx`) | Document capture — checkin | 3 | 0 | 3: MSG_DOC_CONFIRM, MSG_DOC_DISCARD, MSG_DOC_EXTRACT | 0 | no | `guests` + `messagingX` (via selectors) | — |  |
| `expenses` | MmExpenses (`mobile/money/Expenses.tsx`) | Expenses — [expenses,reports] | 4 | 4 | 0 | 0 | no | `date`, `expenses`, `shiftOpen`, `users` | — |  |
| `folios` | MmFolios (`mobile/money/Folio.tsx`) | Guests & folios — [billing,reports] | 2 | 2 | 0 | 0 | yes | `approvals`, `bookings`, `date`, `users` | — |  |
| `groups` | GroupsMobile (`features/groups/Mobile.tsx`) | Groups & corporate — reservations | 8 | 4 | 4: GRP_BLOCK_ALLOT, GRP_BLOCK_CANCEL, GRP_RELEASE, GRP_ROOMING | 0 | yes | `clock`, `date`, `rooms`, `settings`, `users` + `bookings`, `groupsX` (via selectors) | — |  |
| `gsv-my-requests` | GsvMyRequestsMobile (`features/services/Mobile.tsx`) | Guest requests — housekeeping | 5 | 5 | 0 | 0 | no | `clock`, `rooms`, `users` + `servicesX` (via selectors) | — |  |
| `guest-services` | GuestServicesMobile (`features/services/Mobile.tsx`) | Guest services — reservations | 14 | 14 | 0 | 0 | yes | `bookings`, `clock`, `rooms`, `settings`, `users` + `approvals`, `servicesX` (via selectors) | — |  |
| `guests` | MfGuests (`mobile/frontdesk/Guests.tsx`) | (no nav item) | 2 | 1 | 1: GUEST_ID_VIEW | 0 | yes | `bookings`, `guests`, `settings` | — |  |
| `handover` | HandoverMobile (`features/handover/HandoverMobile.tsx`) | Checklists & handover — [housekeeping,payments] | 7 | 6 | 1: HOV_CLOCK | 0 | no | `audit`, `clock`, `closedDates`, `date`, `users` + `handoverX` (via selectors) | — |  |
| `housekeeping` | MoHousekeeping (`mobile/ops/Housekeeping.tsx`) | Housekeeping — housekeeping | 4 | 4 | 0 | 0 | no | `date`, `rooms`, `tasks`, `users` + `bookings` (via selectors) | — |  |
| `inhouse` | MfInHouse (`mobile/frontdesk/InHouse.tsx`) | In-house stays — billing | 10 | 9 | 1: GUEST_ID_VIEW | 0 | yes | `approvals`, `bookings`, `date`, `guests`, `rooms`, `settings` | — |  |
| `inventory` | InventoryMobile (`features/inventory/InventoryMobile.tsx`) | Linen & stock — housekeeping | 9 | 9 | 0 | 0 | no | `clock`, `date`, `rooms`, `settings`, `users` + `bookings`, `inventoryX` (via selectors) | — |  |
| `maintenance` | MaintenanceMobile (`features/maintenance/MaintenanceMobile.tsx`) | Maintenance — housekeeping | 16 | 16 | 0 | 0 | no | `clock`, `date`, `rooms`, `settings`, `users` + `bookings`, `maintenanceX`, `tasks` (via selectors) | — |  |
| `messages` | MsgMessagesMobile (`features/messaging/Messages.tsx`) | Messages — reservations | 5 | 5 | 0 | 0 | yes | `bookings`, `clock`, `guests`, `messagingX` | — |  |
| `my-hr` | MyHrMobile (`features/staff/MyHr.tsx`) | My shifts & pay — [housekeeping,reservations,payments,reports,admin] | 4 | 4 | 0 | 0 | no | `clock`, `date`, `settings`, `users` + `closedDates`, `staffX` (via selectors) | — |  |
| `overview` | MfHome (`mobile/frontdesk/Home.tsx`) | Overview — reports | 14 | 13 | 1: GUEST_ID_VIEW | 0 | yes | `approvals`, `bookings`, `date`, `expenses`, `guests`, `rooms`, `settings`, `tasks` + `audit`, `complianceX` (via selectors) | — |  |
| `pos` | PosMobile (`features/pos/PosMobile.tsx`) | Restaurant & room service — billing | 10 | 10 | 0 | 0 | yes | `bookings`, `clock`, `date`, `settings`, `users` + `approvals`, `posX` (via selectors) | — |  |
| `reports` | MaReports (`mobile/admin/Reports.tsx`) | Reports — reports | 0 | 0 | 0 | 0 | yes | `bookings`, `clock`, `closedDates`, `date`, `expenses`, `openingCash`, `posX`, `rooms`, `settings` | — |  |
| `reservations` | MfReservations (`mobile/frontdesk/Reservations.tsx`) | Reservations — reservations | 14 | 13 | 1: GUEST_ID_VIEW | 0 | yes | `approvals`, `bookings`, `date`, `guests`, `rooms`, `settings` | — |  |
| `roadmap` | MaRoadmap (`mobile/admin/Backup.tsx`) | Roadmap — reports | 0 | 0 | 0 | 0 | no | — | — |  |
| `rooms` | MfRooms (`mobile/frontdesk/Rooms.tsx`) | Room board — reservations | 17 | 16 | 1: GUEST_ID_VIEW | 0 | yes | `approvals`, `bookings`, `date`, `guests`, `rooms`, `settings`, `tasks`, `users` | — |  |
| `settings` | MaSettings (`mobile/admin/Settings.tsx`) | Master configuration — admin | 12 | 10 | 1: TARIFF_SCHEDULE | 1: AI_SETTINGS | no | `date`, `rooms`, `settings`, `users` + `approvals`, `bookings`, `configHistory`, `tasks` (via selectors) | `tariffs` | +7 dispatched dynamically (YLD_GUARD_SET, GSV_SETTINGS, POS_MENU_TOGGLE, AI_SETTINGS, HR_STAT_RATES_SET, HR_LEAVE_POLICY_SET, CMP_SETTINGS_SET) |
| `staff` | HrDeskMobile (`features/staff/HrDesk.tsx`) | Staff & payroll — reports | 19 | 19 | 0 | 0 | no | `clock`, `date`, `settings`, `users` + `closedDates`, `staffX` (via selectors) | — |  |
| `yield` | YieldMobile (`features/yield/YieldMobile.tsx`) | Tariff & yield — reports | 10 | 10 | 0 | 0 | no | `closedDates`, `date`, `users` + `approvals`, `bookings`, `rooms`, `yieldX` (via selectors) | `tariffs` |  |

<!-- END phoneTable -->

<!-- BEGIN bookingDialogTable -->
| Platform | Commands reachable from the booking dialog (and the dialogs it opens) | Mapped | Unmapped | Absent |
|---|---|---:|---:|---:|
| desktop | `APPROVAL`, `BOOK`, `CANCEL`, `CHANGE_DATES`, `CHARGE`, `CHECKIN`, `CHECKOUT`, `COMPANY_CREDIT_REQUEST`, `CONFIRM`, `CORRECTION_REQUEST`, `CREDIT_NOTE`, `DISCOUNT`, `GSV_COMPLAINT_LOG`, `GSV_REQUEST_CREATE`, `GUEST_ID_VIEW` (unmapped), `GUEST_SAVE`, `INVOICE_ISSUE` (production-only mapper), `MOVE`, `PAYMENT`, `REFUND_EXECUTE`, `REFUND_REQUEST`, `REGISTER` | 21 | 1 | 0 |
| phone | `APPROVAL`, `BOOK`, `CANCEL`, `CHANGE_DATES`, `CHARGE`, `CHECKIN`, `CHECKOUT`, `COMPANY_CREDIT_REQUEST`, `CONFIRM`, `CORRECTION_REQUEST`, `CREDIT_NOTE`, `DISCOUNT`, `GSV_COMPLAINT_LOG`, `GSV_REQUEST_CREATE`, `GUEST_ID_VIEW` (unmapped), `GUEST_SAVE`, `INVOICE_ISSUE` (production-only mapper), `MOVE`, `PAYMENT`, `REFUND_EXECUTE`, `REFUND_REQUEST`, `REGISTER` | 21 | 1 | 0 |

<!-- END bookingDialogTable -->

<!-- BEGIN loadersTable -->
| Sync area | Slice | Kind | Requires (prototype permission) | Required reads (D5) | Needs |
|---|---|---|---|---|---|
| core | `clock` | own | — | — | — |
| core | `date` | own | — | — | clock |
| reservations | `bookings` | own | reservations, checkin, checkout, billing, payments | — | date, guests |
| frontdesk | `guests` | own | reservations, checkin, checkout, billing | `GET /guests` | — |
| rooms | `rooms` | own | housekeeping, reservations | — | — |
| rooms | `tasks` | own | housekeeping | `GET /housekeeping/tasks` | — |
| money | `bookings` | patch | billing, payments, checkout, reservations, reports | — | — |
| money | `approvals` | patch | — | — | bookings, shift |
| money | `shiftOpen` | own | payments, approve, reports, close | `GET /cashier/desk` | — |
| money | `openingCash` | own | payments, approve, reports, close | `GET /cashier/desk` | — |
| money | `shift` | own | payments, approve, reports, close | `GET /cashier/desk` | — |
| money | `shiftClose` | own | payments, approve, reports, close | `GET /cashier/desk` | — |
| money | `cashbookX` | own | reports | `GET /cashbook` | date |
| money | `groupsX` | own | reservations | `GET /groups` | — |
| closing | `date` | patch | — | — | — |
| closing | `closings` | own | reports | `GET /closing/records` | date |
| closing | `closedDates` | own | — | — | closings |
| admin | `users` | own | admin | `GET /admin/users` | date |
| admin | `policy` | own | — | — | — |
| admin | `grants` | own | admin | — | policy |
| admin | `approvals` | own | admin, approve | `GET /configuration/requests` | users |
| admin | `audit` | own | — | — | — |
| admin | `configX` | own | — | — | — |
| admin | `configHistory` | own | admin | — | — |
| admin | `settings` | own | — | — | configX |
| staff | `staffX` | own | — | — | — |
| accounting | `accountingX` | own | reports | `GET /accounting/days`, `GET /accounting/trial-balance`, `GET /accounting/mapping`, `GET /accounting/mapping/history`, `GET /accounting/locks/history` | date |
| accounting | `expenses` | own | — | `GET /expenses` | date |
| accounting | `complianceX` | own | reports | `GET /compliance/obligations`, `GET /compliance/calendar`, `GET /compliance/ca-packs` | — |
| pos | `posX` | own | billing | `GET /pos/outlets`, `GET /pos/menu`, `GET /pos/checks`, `GET /pos/checks/closed`, `GET /pos/checks/:id` | date |
| pos | `approvals` | patch | billing | `GET /pos/void-requests` | — |
| maintenance | `maintenanceX` | own | housekeeping | `GET /maintenance/registers`, `GET /maintenance/work-orders`, `GET /maintenance/work-orders/:id` | — |
| maintenance | `inventoryX` | own | housekeeping, reports, expenses | `GET /stock/items`, `GET /stock/laundry`, `GET /stock/counts`, `GET /stock/losses`, `GET /stock/moves`, `GET /stock/settings` | — |
| services | `servicesX` | own | reservations, checkin, housekeeping, inspect | `GET /guest-services/requests` | — |
| services | `messagingX` | own | reservations, checkin | `GET /messaging/messages`, `GET /messaging/templates`, `GET /messaging/rules`, `GET /messaging/consents` | — |
| services | `handoverX` | own | housekeeping, payments, approve | `GET /handover/checklists` | — |
| channels | `yieldX` | own | reports | `GET /yield/calendar`, `GET /yield/config`, `GET /yield/publishes` | — |
| channels | `approvals` | patch | reports | `GET /yield/proposals` | — |
| channels | `channelsX` | own | reservations | `GET /ota/channels` | — |
| directory | `users` | patch | — | `GET /me/directory` | — |

<!-- END loadersTable -->

