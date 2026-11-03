// F6-H3 · Hall booking — UI registration: page 'hall-booking' (REVENUE & SALES), its modals and the phone page. Merged by app/registry.ts.
import {PartyPopper} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {HallPage} from './HallPage';
import {HallMobile} from './HallMobile';
import {HallEnquiryModal} from './ModalEnquiry';
import {HallQuoteModal} from './ModalQuote';
import {HallEventModal} from './ModalEvent';
import {HallPrintModal,HallVenueModal,HallMenuModal} from './ModalMisc';

export const registration:FeatureRegistration={
  pages:{'hall-booking':HallPage},
  nav:[{group:'REVENUE & SALES',item:{id:'hall-booking',label:'Hall booking',icon:PartyPopper,p:['reservations','reports','housekeeping','billing']},after:'groups'}],
  modals:{'hall-enquiry':HallEnquiryModal,'hall-quote':HallQuoteModal,'hall-event':HallEventModal,'hall-print':HallPrintModal,'hall-venue':HallVenueModal,'hall-menu':HallMenuModal},
  wideModals:['hall-quote','hall-event','hall-print','hall-venue','hall-menu','hall-enquiry'],
  mobile:{pages:{'hall-booking':HallMobile}},
};
