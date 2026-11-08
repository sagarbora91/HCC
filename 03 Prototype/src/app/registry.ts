// Merges the six stream registrations (features/*/register.tsx) into pages, navigation, modals and booking actions.
import React from 'react';
import type {Booking} from '../services/mock';
import type {NavItem} from './nav';
import {registration as reservations} from '../features/reservations/register';
import {registration as guests} from '../features/guests/register';
import {registration as customers} from '../features/customers/register';
import {registration as folio} from '../features/folio/register';
import {registration as housekeeping} from '../features/housekeeping/register';
import {registration as admin} from '../features/admin/register';
import {registration as closing} from '../features/closing/register';
import {registration as yieldMod} from '../features/yield/register';
import {registration as groups} from '../features/groups/register';
import {registration as channels} from '../features/channels/register';
import {registration as maintenance} from '../features/maintenance/register';
import {registration as inventory} from '../features/inventory/register';
import {registration as handover} from '../features/handover/register';
import {registration as services} from '../features/services/register';
import {registration as messaging} from '../features/messaging/register';
import {registration as pos} from '../features/pos/register';
import {registration as staff} from '../features/staff/register';
import {registration as accounting} from '../features/accounting/register';
import {registration as assistant} from '../features/assistant/register';
import {registration as compliance} from '../features/compliance/register';
import {registration as cashbook} from '../features/cashbook/register';
import {registration as masters} from '../features/masters/register'; // MA-0 masters ('mst-row')
import {registration as roommasters} from '../features/roommasters/register'; // MA-1 rooms masters (tools, 'rms-extra')
import {registration as m_frontdesk} from '../mobile/frontdesk/register';
import {registration as m_money} from '../mobile/money/register';
import {registration as m_ops} from '../mobile/ops/register';
import {registration as m_admin} from '../mobile/admin/register';
import {registration as catalogue} from '../features/folio/catalogue'; // MA-2 charge/discount dialogs, meal plan, Plans & extras
import {registration as prefs} from '../features/prefs/register'; // CP-1 Appearance & display ('prefs')
import {registration as keysMod} from '../features/keys/register'; // KEYS room keys ('keys', 'key-panel')
// LF lanes: ONE import line inside your own block; add the registration name inside your block in `registrations` below.
// LF-ALERTS
import {registration as lfAlerts} from '../features/alerts/register';
// LF-ALERTS end
// LF-UI
import {registration as lfMoney} from '../features/lfmoney/register';
// LF-UI end
// F4 lanes: ONE import line inside your own block; the registration name inside your block in `registrations` below.
// F4-T1
import {registration as tariffCard} from '../features/tariffcard/register';
// F6-H3
import {registration as hallBooking} from '../features/hall/register';
// F6-H3 end
// F4-T1 end
// F4-T2
import {registration as f4Digest} from '../features/digest/register';
// F4-T2 end
// F4-T3
// F4-T3 end
// F7-R2
import {registration as f7Hub} from '../features/reports/HubRegister';
// F7-R2 end
// F12-INT (MEAS capture dialogs and revenue pages)
import {registration as measDeskUi} from '../features/reservations/TurnawayRegister';
import {registration as measGuestUi} from '../features/services/SurveyRegister';
import {registration as measCaptureUi} from '../features/reports/MeasCaptureRegister';
// F12-INT end
// F8 lanes: ONE import line inside your own block; the registration name inside your block in `registrations` below.
// F8-SF-UI
// F8-SF-UI end
// F8-QW-KDS
// F8-QW-KDS end
// F8-QW-ADV
// F8-QW-ADV end
// F8-QW-PM
// F8-QW-PM end
export type ModalProps={id?:string;room?:string};
export type FeatureRegistration={
  /** Page components by page ID (shown when the nav item with this ID is selected). */
  pages?:Record<string,React.ComponentType>;
  /** Nav items to add. group = existing group label (e.g. 'FRONT DESK') or a new one; after = item ID to insert after. */
  nav?:{group:string;item:NavItem;after?:string}[];
  /** Modal bodies by modal type, opened with open({type,id,room}). */
  modals?:Record<string,React.ComponentType<ModalProps>>;
  /** Wide modal types (830px). */
  wideModals?:string[];
  /** Extra buttons rendered in the booking/folio dialog action row. */
  bookingActions?:React.ComponentType<{b:Booking}>;
  /** Default landing page overrides per role name. */
  landing?:Record<string,string>;
  /** Phone versions (MB-05+): page / modal components that replace the desktop ones when the mobile shell is shown. */
  mobile?:{pages?:Record<string,React.ComponentType>;modals?:Record<string,React.ComponentType<ModalProps>>};
};
export const registrations:FeatureRegistration[]=[reservations,guests,customers,folio,housekeeping,admin,closing,
  yieldMod,groups,channels,maintenance,inventory,handover,services,messaging,pos,staff,accounting,assistant,compliance,
  cashbook,masters,m_frontdesk,m_money,m_ops,m_admin,roommasters,catalogue,prefs,keysMod,
  // LF-ALERTS
  lfAlerts,
  // LF-ALERTS end
  // LF-UI
  lfMoney,
  // LF-UI end
  // F4-T1
  tariffCard,
  // F4-T1 end
  // F4-T2
  f4Digest,
  // F4-T2 end
  // F4-T3
  // F4-T3 end
  // F7-R2
  f7Hub,
  // F7-R2 end
  // F6-H3
  hallBooking,
  // F6-H3 end
  // F12-INT
  measDeskUi,measGuestUi,measCaptureUi,
  // F12-INT end
  // F8-SF-UI
  // F8-SF-UI end
  // F8-QW-KDS
  // F8-QW-KDS end
  // F8-QW-ADV
  // F8-QW-ADV end
  // F8-QW-PM
  // F8-QW-PM end
];
export const mobilePages=Object.assign({},...registrations.map(r=>r.mobile?.pages||{})) as Record<string,React.ComponentType>;
export const mobileModals=Object.assign({},...registrations.map(r=>r.mobile?.modals||{})) as Record<string,React.ComponentType<ModalProps>>;
export const extraPages=Object.assign({},...registrations.map(r=>r.pages||{})) as Record<string,React.ComponentType>;
export const extraModals=Object.assign({},...registrations.map(r=>r.modals||{})) as Record<string,React.ComponentType<ModalProps>>;
export const extraWide=registrations.flatMap(r=>r.wideModals||[]);
export const bookingActionSlots=registrations.map(r=>r.bookingActions).filter(Boolean) as React.ComponentType<{b:Booking}>[];
export const extraNav=registrations.flatMap(r=>r.nav||[]);
export const extraLanding=Object.assign({},...registrations.map(r=>r.landing||{})) as Record<string,string>;
