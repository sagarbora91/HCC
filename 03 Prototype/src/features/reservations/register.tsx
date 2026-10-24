// Stream A · Calendar & reservations — UI registration. Pages/modals registered here are merged by app/registry.ts.
// A page or modal with an existing ID replaces the built-in one (only replace IDs your stream owns).
//   page  'calendar'      — SCR-03 (nav: FRONT DESK, after 'rooms', permission 'reservations')
//   modal 'new'           — new booking; props: room = preselected room, id = prefilled arrival date (YYYY-MM-DD)
//   modal 'cancel'        — cancel / no-show / cancel hold; id = booking ID
//   modal 'move'          — Move / change dates for Confirmed or Held (CHANGE_DATES); In-house falls back to the M1 MOVE dialog
//   modal 'confirmation'  — printable booking confirmation (wide); id = booking ID
import {CalendarRange} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {Calendar} from './Calendar';
import {NewReservation,CancelReservation,MoveReservation} from './modals';
import {BookingConfirmation,ResBookingActions} from './Confirmation';
import {Empty} from '../../components/ui';
export const registration:FeatureRegistration={
  pages:{calendar:Calendar},
  nav:[{group:'FRONT DESK',after:'rooms',item:{id:'calendar',label:'Calendar',icon:CalendarRange,p:'reservations'}}],
  modals:{
    new:({room,id})=><NewReservation room={room} id={id}/>,
    cancel:({id})=>id?<CancelReservation id={id}/>:<Empty title="This record is unavailable"/>,
    move:MoveReservation,
    confirmation:BookingConfirmation,
  },
  wideModals:['new','confirmation'],
  bookingActions:ResBookingActions,
};
