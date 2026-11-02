// LF-UI · registration: page 'counter-bills' (Alt+B), the money dialogs and the booking-dialog actions.
import {Receipt} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {CounterBillsPage} from './CounterBills';
import {StayRateModal,BillCorrectModal,AgreedRateModal,CreditAccountModal} from './modals';
import {LfBookingActions} from './GuestMoney';

export const registration:FeatureRegistration={
  pages:{'counter-bills':CounterBillsPage},
  nav:[{group:'CASH & BILLING',item:{id:'counter-bills',label:'Counter bills',icon:Receipt,p:['billing','payments']},after:'folios'}],
  modals:{'stay-rate':StayRateModal,'bill-correct':BillCorrectModal,'agreed-rate':AgreedRateModal,'credit-account':CreditAccountModal},
  wideModals:['bill-correct'],
  bookingActions:LfBookingActions,
};
