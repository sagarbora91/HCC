// F4-T1 · Tariff card — UI registration (page tariff-card in REVENUE & SALES; phone page). Merged by app/registry.ts.
import {BookOpenText} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {TariffCardPage} from './TariffCardPage';
import {TariffCardMobile} from './TariffCardMobile';
export const registration:FeatureRegistration={
  pages:{'tariff-card':TariffCardPage},
  nav:[{group:'REVENUE & SALES',item:{id:'tariff-card',label:'Tariff card',icon:BookOpenText,p:['reports','approve','payments','checkin','reservations']},after:'yield'}],
  mobile:{pages:{'tariff-card':TariffCardMobile}},
};
