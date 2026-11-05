// F12 MEAS · cap-desk UI registration (dialogs + booking-dialog action). Merged by app/registry.ts (integrator adds one import + `measDesk` to `registrations`).
import React from 'react';
import type {FeatureRegistration,ModalProps} from '../../app/registry';
import type {Booking} from '../../services/mock';
import {TurnawayModal} from './TurnawayModal';
import {UpsellModal,UpsellButton} from '../folio/UpsellModal';
import {PreferencesModal} from '../customers/PreferencesModal';

const Slot=({b}:{b:Booking})=><UpsellButton b={b}/>;
export const registration:FeatureRegistration={
  modals:{
    'meas-turnaway':TurnawayModal,
    'meas-upsell':UpsellModal as React.ComponentType<ModalProps>,
    'meas-prefs':PreferencesModal as React.ComponentType<ModalProps>,
  },
  bookingActions:Slot,
  mobile:{modals:{'meas-turnaway':TurnawayModal,'meas-upsell':UpsellModal as React.ComponentType<ModalProps>,'meas-prefs':PreferencesModal as React.ComponentType<ModalProps>}},
};
