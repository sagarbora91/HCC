// MB-05 front desk (M01–M08) — phone screens. Registers mobile.pages / mobile.modals keyed by the desktop page / modal ID they replace on phones.
//   pages:  overview (M01) · rooms (M02) · calendar (M03) · reservations (M04) · arrivals (M06) · guests (M07) · inhouse (M08)
//   modals: new (M05 wizard) · guest-checkin / guest-walkin (M06 stepper) · guest-profile (M07) · stay-detail (M08) · mf-room (M02 room sheet, phone only)
import type {FeatureRegistration} from '../../app/registry';
import {MfHome} from './Home';
import {MfRooms,MfRoomSheet} from './Rooms';
import {MfCalendar} from './Calendar';
import {MfReservations} from './Reservations';
import {MfNewBooking} from './NewBooking';
import {MfArrivals,MfCheckin,MfWalkin} from './Arrivals';
import {MfGuests,MfGuestProfile} from './Guests';
import {MfInHouse,MfStayDetail} from './InHouse';
export const registration:FeatureRegistration={mobile:{
  pages:{overview:MfHome,rooms:MfRooms,calendar:MfCalendar,reservations:MfReservations,arrivals:MfArrivals,guests:MfGuests,inhouse:MfInHouse},
  modals:{new:MfNewBooking,'guest-checkin':MfCheckin,'guest-walkin':MfWalkin,'guest-profile':MfGuestProfile,'stay-detail':MfStayDetail,'mf-room':MfRoomSheet},
}};
