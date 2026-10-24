// Original HCC reservation status map (no donor code). The SHAPE — a transition table plus allowed()/assert helpers —
// follows the idea of HAIP's reservation-state-machine.ts (Apache-2.0), but the statuses, commands and rules are HCC's
// own, documented from 03 Prototype handlers (reservations.ts, guests.ts, folio.ts). Used here as an executable
// specification: the conformance test checks the live mock against it.
import type {BookingStatus} from '../../../03 Prototype/src/domain/types';

export type ResCommand='CONFIRM'|'CANCEL'|'NO_SHOW'|'CHECKIN'|'CHECKOUT'|'CHANGE_DATES';

/** status → commands that may succeed (preconditions such as payment, readiness and dates still apply). */
export const HCC_RES_TRANSITIONS:Record<BookingStatus,Partial<Record<ResCommand,BookingStatus>>>={
  'Held':{CONFIRM:'Confirmed',CANCEL:'Cancelled',CHANGE_DATES:'Held'},
  'Expired':{},                       // a lapsed hold is swept to Expired; only a new booking can follow
  'Confirmed':{CANCEL:'Cancelled',NO_SHOW:'No show',CHECKIN:'In house',CHANGE_DATES:'Confirmed'},
  'In house':{CHECKOUT:'Departed'},   // room moves/extensions use MOVE and keep In house
  'Departed':{},'Cancelled':{},'No show':{},
};

/** Donor statuses that HCC models as derived views, not stored states. */
export const DONOR_STATUS_IN_HCC:Record<string,string>={
  pending:'Held (with a running hold expiry)',
  confirmed:'Confirmed',
  assigned:'Confirmed — HCC always assigns a room at booking',
  checked_in:'In house',
  stayover:'In house with departure > business date (derived)',
  due_out:'In house with departure = business date (derived)',
  checked_out:'Departed',
  no_show:'No show (only on/after arrival date)',
  cancelled:'Cancelled',
};

export const allowed=(from:BookingStatus,cmd:ResCommand)=>HCC_RES_TRANSITIONS[from][cmd];
