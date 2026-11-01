// Room keys — UI registration: page 'keys' (nav under FRONT DESK), the 'key-panel' dialog and the booking-dialog button. Merged by app/registry.ts.
import {KeyRound} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {KeysPage} from './KeysPage';
import {KeyPanelModal,KeyBookingAction} from './KeyPanel';

export const registration:FeatureRegistration={
  pages:{keys:KeysPage},
  nav:[{group:'FRONT DESK',item:{id:'keys',label:'Room keys',icon:KeyRound,p:['checkin','checkout']},after:'arrivals'}],
  modals:{'key-panel':KeyPanelModal},   // id = booking ID
  wideModals:['key-panel'],
  bookingActions:KeyBookingAction,
};
