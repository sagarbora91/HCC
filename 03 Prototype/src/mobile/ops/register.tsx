// MB-07/09/10 housekeeping, maintenance, demo sheet, manifest (M12–M14, M24) — phone screens. Registers mobile.pages / mobile.modals keyed by the desktop page / modal ID they replace on phones.
// Page 'housekeeping' → MoHousekeeping (role-aware). Modals: 'mo-assign' (id = task ID, phone-only picker), phone overrides of 'hk-reject' (task ID) and 'room-repair' (room ID).
// The demo sheet (M24) lives in app/mobile/sheets.tsx; the manifest/icons (MB-10) in build.mjs + ./manifest.mjs.
import type {FeatureRegistration} from '../../app/registry';
import {MoHousekeeping} from './Housekeeping';
import {MoAssignSheet,MoRejectSheet,MoRepairSheet} from './sheets';
export const registration:FeatureRegistration={mobile:{pages:{housekeeping:MoHousekeeping},modals:{'mo-assign':MoAssignSheet,'hk-reject':MoRejectSheet,'room-repair':MoRepairSheet}}};
