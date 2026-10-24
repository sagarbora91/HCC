| Command | File | Coarse req() | authorize() | Custom gate | Intended PERM | Day-closed guard | Amount check |
|---|---|---|---|---|---|---|---|
| GRANT | commands.ts | admin | — | yes | — | no | — |
| POST_NIGHTS | commands.ts | billing | — | yes | — | yes | — |
| SHIFT_CLOSE | commands.ts | close | — | yes | — | yes | yes |
| ACC_LOCK | handlers/accounting.ts | reports | — | yes | — | no | — |
| ACC_MAP_RESET | handlers/accounting.ts | admin | — | — | — | no | — |
| ACC_MAP_SET | handlers/accounting.ts | admin | — | — | — | no | — |
| ACC_UNLOCK | handlers/accounting.ts | reports, admin | — | — | — | no | — |
| POLICY_DECIDE | handlers/admin.ts | admin | PERM-ADM-002, PERM-ADM-003 | yes | PERM-ADM-002, PERM-ADM-003 | no | — |
| SETTING_SET | handlers/admin.ts | admin | PERM-ADM-003 | — | — | no | yes |
| SETTINGS | handlers/admin.ts | admin | PERM-ADM-003 | — | — | no | yes |
| TARIFF_SCHEDULE | handlers/admin.ts | admin | PERM-ADM-003 | — | — | no | yes |
| TAX_SCHEDULE | handlers/admin.ts | admin | PERM-ADM-003 | — | — | no | yes |
| USER | handlers/admin.ts | admin | PERM-ADM-001 | yes | — | no | — |
| USER_CREATE | handlers/admin.ts | admin | PERM-ADM-001 | yes | — | no | — |
| USER_UPDATE | handlers/admin.ts | admin | PERM-ADM-001 | yes | — | no | — |
| AI_ASK | handlers/assistant.ts | **none** | — | any enabled user (by design) | — | no | — |
| AI_CLEAR | handlers/assistant.ts | **none** | — | any enabled user (by design) | — | no | — |
| AI_SETTINGS | handlers/assistant.ts | approve | — | — | — | no | yes |
| CHN_ACCEPT | handlers/channels.ts | reservations | — | — | — | yes | — |
| CHN_CHANNEL_SAVE | handlers/channels.ts | approve | — | — | — | no | yes |
| CHN_CHANNEL_STATUS | handlers/channels.ts | approve | — | — | — | no | — |
| CHN_PULL | handlers/channels.ts | reservations | — | — | — | no | — |
| CHN_PUSH | handlers/channels.ts | reservations | — | — | — | no | — |
| CHN_REJECT | handlers/channels.ts | reservations | — | — | — | yes | — |
| CHN_STOP_SELL | handlers/channels.ts | reservations | — | — | — | no | — |
| CHN_WEB_CONFIRM | handlers/channels.ts | reservations | — | — | — | no | — |
| CHN_WEB_DECLINE | handlers/channels.ts | reservations | — | — | — | yes | — |
| CHN_WEB_REQUEST | handlers/channels.ts | reservations | — | — | — | yes | — |
| CLOSE_RUN | handlers/closing.ts | close | — | — | PERM-SHF-001 | yes | yes |
| CORRECTION_ADJUST | handlers/closing.ts | close | — | — | PERM-CLS-004 | yes | yes |
| CORRECTION_ADJUST_DECIDE | handlers/closing.ts | approve | — | yes | — | yes | — |
| DAY_CLOSE | handlers/closing.ts | close | — | — | PERM-CLS-002 | yes | — |
| EXPENSE | handlers/closing.ts | expenses | — | — | — | yes | yes |
| EXPENSE_APPROVE | handlers/closing.ts | approve | — | yes | PERM-EXP-003 | yes | — |
| EXPENSE_REVERSE | handlers/closing.ts | approve | — | — | PERM-CLS-002 | yes | — |
| EXPENSE_SUBMIT | handlers/closing.ts | expenses | — | yes | PERM-EXP-002, PERM-EXP-003 | yes | — |
| NEXT_DAY | handlers/closing.ts | close | — | — | PERM-CLS-003 | no | — |
| NIGHT_POST | handlers/closing.ts | close | — | — | PERM-CLS-002 | yes | — |
| SHIFT_DIFFERENCE_DECIDE | handlers/closing.ts | approve | — | yes | PERM-EXP-001 | yes | — |
| SHIFT_SUBMIT | handlers/closing.ts | payments | — | — | PERM-SHF-002, PERM-SOD-001 | yes | yes |
| APPROVAL | handlers/folio.ts | approve | — | yes | PERM-APR-001 | yes | — |
| CHARGE | handlers/folio.ts | billing | — | — | PERM-FOL-002 | yes | yes |
| CHECKOUT | handlers/folio.ts | checkout | — | — | PERM-STY-003 | yes | — |
| COMPANY_CREDIT_REQUEST | handlers/folio.ts | checkout | — | — | PERM-STY-003 | yes | — |
| CORRECTION_REQUEST | handlers/folio.ts | billing | — | — | PERM-FOL-005 | yes | — |
| CREDIT_NOTE | handlers/folio.ts | billing | — | — | PERM-FOL-005 | yes | yes |
| DISCOUNT | handlers/folio.ts | billing | — | — | PERM-FOL-003, PERM-APR-001 | yes | yes |
| PAYMENT | handlers/folio.ts | payments | — | — | PERM-PAY-001 | yes | yes |
| REFUND_EXECUTE | handlers/folio.ts | payments | — | yes | PERM-PAY-004 | yes | — |
| REFUND_REQUEST | handlers/folio.ts | payments | — | — | PERM-PAY-003 | yes | yes |
| GRP_BLOCK_ALLOT | handlers/groups.ts | reservations | — | — | — | yes | yes |
| GRP_BLOCK_CANCEL | handlers/groups.ts | reservations | — | — | — | yes | — |
| GRP_BLOCK_CREATE | handlers/groups.ts | reservations | — | — | — | yes | yes |
| GRP_COMPANY_SAVE | handlers/groups.ts | reservations | — | yes | — | yes | — |
| GRP_LIMIT_DECIDE | handlers/groups.ts | approve | — | yes | — | yes | — |
| GRP_LIMIT_SET | handlers/groups.ts | reservations | — | yes | — | yes | — |
| GRP_RELEASE | handlers/groups.ts | reservations | — | — | — | yes | yes |
| GRP_ROOMING | handlers/groups.ts | reservations, approve | — | — | — | yes | — |
| GRP_SWEEP | handlers/groups.ts | reservations | — | — | — | no | — |
| CHECKIN | handlers/guests.ts | checkin | — | — | PERM-STY-002 | yes | — |
| GUEST_SAVE | handlers/guests.ts | checkin | — | — | — | no | — |
| MOVE | handlers/guests.ts | reservations | — | — | — | yes | — |
| REGISTER | handlers/guests.ts | checkin | — | — | PERM-STY-001 | no | — |
| HOV_CARRY_DONE | handlers/handover.ts | **none** | — | yes | — | no | — |
| HOV_CLOCK | handlers/handover.ts | close | — | — | — | no | yes |
| HOV_ITEM | handlers/handover.ts | **none** | — | yes | — | no | — |
| HOV_NOTE_ACK | handlers/handover.ts | **none** | — | yes | — | no | — |
| HOV_NOTE_SUBMIT | handlers/handover.ts | payments | — | yes | — | no | yes |
| HOV_RUN_REOPEN | handlers/handover.ts | approve | — | — | — | no | — |
| HOV_RUN_SUBMIT | handlers/handover.ts | **none** | — | yes | — | no | — |
| HOV_TEMPLATE_SAVE | handlers/handover.ts | approve | — | — | — | no | — |
| HOV_TEMPLATE_TOGGLE | handlers/handover.ts | approve | — | — | — | no | — |
| BACKUP_SIMULATE | handlers/housekeeping.ts | admin | — | — | PERM-ADM-005 | no | — |
| BLOCK | handlers/housekeeping.ts | inspect | — | — | PERM-ROOM-002 | no | — |
| REPAIR_REQUEST | handlers/housekeeping.ts | **none** | — | yes | — | no | — |
| STAYOVER_GENERATE | handlers/housekeeping.ts | inspect | — | — | PERM-HK-002 | yes | — |
| TASK | handlers/housekeeping.ts | housekeeping, inspect | — | yes | PERM-HK-001, PERM-HK-002, PERM-ROOM-002, PERM-HK-004 | no | — |
| UNBLOCK | handlers/housekeeping.ts | inspect | — | — | PERM-ROOM-002, PERM-ROOM-001 | no | — |
| INV_COUNT_POST | handlers/inventory.ts | inspect | — | yes | — | yes | — |
| INV_COUNT_SUBMIT | handlers/inventory.ts | housekeeping | — | — | — | yes | yes |
| INV_GRN | handlers/inventory.ts | housekeeping, inspect | — | yes | — | yes | yes |
| INV_ISSUE | handlers/inventory.ts | housekeeping | — | — | — | yes | — |
| INV_ITEM_SET | handlers/inventory.ts | inspect | — | — | — | yes | yes |
| INV_LAUNDRY_RECEIVE | handlers/inventory.ts | housekeeping | — | — | — | yes | yes |
| INV_LAUNDRY_SEND | handlers/inventory.ts | housekeeping, inspect | — | yes | — | yes | — |
| INV_LOSS | handlers/inventory.ts | housekeeping | — | — | — | yes | — |
| INV_ROOM_CHANGE | handlers/inventory.ts | housekeeping | — | — | — | yes | — |
| INV_USE | handlers/inventory.ts | housekeeping | — | — | — | yes | — |
| MNT_AMC_SAVE | handlers/maintenance.ts | expenses | — | — | — | no | yes |
| MNT_ASSET_SAVE | handlers/maintenance.ts | inspect | — | — | — | no | — |
| MNT_PLAN_SAVE | handlers/maintenance.ts | inspect | — | — | — | no | yes |
| MNT_PM_GENERATE | handlers/maintenance.ts | inspect | — | — | — | yes | — |
| MNT_SPARE_RECEIVE | handlers/maintenance.ts | inspect | — | — | — | no | yes |
| MNT_VENDOR_SAVE | handlers/maintenance.ts | inspect | — | — | — | no | — |
| MNT_WO_ASSIGN | handlers/maintenance.ts | inspect | — | — | — | no | — |
| MNT_WO_BLOCK | handlers/maintenance.ts | inspect | — | — | — | no | — |
| MNT_WO_CANCEL | handlers/maintenance.ts | inspect | — | — | — | no | — |
| MNT_WO_COMPLETE | handlers/maintenance.ts | housekeeping | — | yes | — | no | — |
| MNT_WO_CREATE | handlers/maintenance.ts | housekeeping, inspect | — | yes | — | yes | — |
| MNT_WO_FROM_REPAIR | handlers/maintenance.ts | housekeeping | — | yes | — | no | — |
| MNT_WO_HOLD | handlers/maintenance.ts | housekeeping | — | yes | — | no | — |
| MNT_WO_PART | handlers/maintenance.ts | housekeeping | — | yes | — | no | yes |
| MNT_WO_START | handlers/maintenance.ts | housekeeping | — | yes | — | no | — |
| MNT_WO_VERIFY | handlers/maintenance.ts | inspect | — | yes | — | no | — |
| MSG_CONSENT | handlers/messaging.ts | checkin | — | yes | — | no | — |
| MSG_DOC_CONFIRM | handlers/messaging.ts | checkin | — | — | — | no | yes |
| MSG_DOC_DISCARD | handlers/messaging.ts | checkin | — | — | — | no | — |
| MSG_DOC_EXTRACT | handlers/messaging.ts | checkin | — | — | — | no | — |
| MSG_RETRY | handlers/messaging.ts | reservations | — | — | — | no | — |
| MSG_RULE | handlers/messaging.ts | approve | — | — | — | no | — |
| MSG_RUN_RULES | handlers/messaging.ts | reservations | — | — | — | no | — |
| MSG_SEND | handlers/messaging.ts | reservations | — | — | — | no | — |
| MSG_SIMULATE | handlers/messaging.ts | reservations | — | — | — | no | — |
| MSG_TEMPLATE | handlers/messaging.ts | approve | — | — | — | no | — |
| POS_ITEM_ADD | handlers/pos.ts | billing | — | — | — | yes | yes |
| POS_ITEM_QTY | handlers/pos.ts | billing | — | — | — | yes | yes |
| POS_KOT_SEND | handlers/pos.ts | billing | — | — | — | yes | — |
| POS_KOT_STATUS | handlers/pos.ts | billing | — | — | — | yes | — |
| POS_MENU_TOGGLE | handlers/pos.ts | billing | — | — | — | yes | — |
| POS_ORDER_OPEN | handlers/pos.ts | billing | — | — | — | yes | yes |
| POS_SETTLE | handlers/pos.ts | folio, billing, payments | — | — | — | yes | — |
| POS_VOID_BILL | handlers/pos.ts | billing, payments | — | — | — | yes | — |
| POS_VOID_DECIDE | handlers/pos.ts | approve | — | yes | — | yes | — |
| POS_VOID_ITEM | handlers/pos.ts | billing | — | — | — | yes | — |
| BOOK | handlers/reservations.ts | reservations, approve | — | — | — | yes | yes |
| CANCEL | handlers/reservations.ts | reservations | — | — | — | yes | — |
| CHANGE_DATES | handlers/reservations.ts | reservations, approve | — | — | — | yes | — |
| CONFIRM | handlers/reservations.ts | reservations | — | — | — | yes | — |
| GSV_COMPLAINT_FOLLOWUP | handlers/services.ts | reservations, approve | — | — | — | no | — |
| GSV_COMPLAINT_LOG | handlers/services.ts | reservations | — | yes | — | yes | — |
| GSV_COMPLAINT_RECOVERY | handlers/services.ts | **none** | — | yes | — | no | — |
| GSV_COMPLAINT_RESOLVE | handlers/services.ts | reservations | — | — | — | no | — |
| GSV_COMPLAINT_UPDATE | handlers/services.ts | reservations | — | yes | — | no | — |
| GSV_LF_CLAIM | handlers/services.ts | reservations | — | — | — | no | — |
| GSV_LF_DISPOSE | handlers/services.ts | reservations, approve | — | — | — | no | — |
| GSV_LF_HANDOVER | handlers/services.ts | reservations | — | — | — | no | — |
| GSV_LF_LOG | handlers/services.ts | housekeeping | — | yes | — | yes | — |
| GSV_REQUEST_ASSIGN | handlers/services.ts | reservations | — | yes | — | no | — |
| GSV_REQUEST_CANCEL | handlers/services.ts | reservations | — | — | — | no | — |
| GSV_REQUEST_COMPLETE | handlers/services.ts | housekeeping | — | yes | — | no | — |
| GSV_REQUEST_CREATE | handlers/services.ts | reservations | — | — | — | yes | — |
| GSV_REQUEST_START | handlers/services.ts | housekeeping | — | yes | — | no | — |
| GSV_SETTINGS | handlers/services.ts | approve | — | — | — | no | yes |
| HR_ADVANCE_ADD | handlers/staff.ts | **none** | — | yes | — | no | yes |
| HR_ATT_MARK | handlers/staff.ts | **none** | — | yes | — | yes | yes |
| HR_LEAVE_CANCEL | handlers/staff.ts | **none** | — | yes | — | no | — |
| HR_LEAVE_DECIDE | handlers/staff.ts | **none** | — | yes | — | no | — |
| HR_LEAVE_REQUEST | handlers/staff.ts | **none** | — | yes | — | no | — |
| HR_PAYROLL_LOCK | handlers/staff.ts | **none** | — | yes | — | no | — |
| HR_PAYROLL_RUN | handlers/staff.ts | **none** | — | yes | — | no | — |
| HR_RATES_SET | handlers/staff.ts | **none** | — | yes | — | no | yes |
| HR_ROSTER_COPY | handlers/staff.ts | **none** | — | yes | — | no | — |
| HR_ROSTER_SET | handlers/staff.ts | **none** | — | yes | — | no | — |
| HR_STAFF_ACTIVE | handlers/staff.ts | **none** | — | yes | — | no | — |
| HR_STAFF_SAVE | handlers/staff.ts | **none** | — | yes | — | no | yes |
| YLD_DECIDE | handlers/yield.ts | reports | — | yes | — | no | — |
| YLD_DOW_SET | handlers/yield.ts | reports | — | yes | — | no | — |
| YLD_GUARD_SET | handlers/yield.ts | reports | — | yes | — | no | — |
| YLD_OCC_SAVE | handlers/yield.ts | reports | — | yes | — | no | yes |
| YLD_OVERRIDE | handlers/yield.ts | reports | — | yes | — | no | yes |
| YLD_PLAN_SAVE | handlers/yield.ts | reports | — | yes | — | no | yes |
| YLD_PROPOSE | handlers/yield.ts | reports, approve | — | — | — | no | — |
| YLD_PUBLISH | handlers/yield.ts | reports | — | yes | — | no | — |
| YLD_RESTRICTION_SAVE | handlers/yield.ts | reports | — | yes | — | no | yes |
| YLD_SEASON_SAVE | handlers/yield.ts | reports | — | yes | — | no | yes |
