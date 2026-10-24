// Module 23 · Restaurant POS & room service — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import {UtensilsCrossed} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {PosPage} from './PosPage';
import {PosMobile} from './PosMobile';
import {PosOpenModal,PosSettleModal,PosVoidModal,PosPrintModal,PosReportModal,PosCartSheet} from './modals';
export const registration:FeatureRegistration={
  pages:{'pos':PosPage},
  nav:[
    {group:'CASH & BILLING',item:{id:'pos',label:'Restaurant & room service',icon:UtensilsCrossed,p:'billing'}}
  ],
  modals:{
    'pos-open':PosOpenModal,     // id = outlet, room = table / room number
    'pos-settle':PosSettleModal, // id = check
    'pos-void':PosVoidModal,     // id = check, room = line ID | 'bill'
    'pos-print':PosPrintModal,   // id = check, room = KOT ID | 'bill'
    'pos-report':PosReportModal, // outlet day summary
    'pos-cart':PosCartSheet,     // id = check (phone cart sheet; also usable on desktop)
  },
  wideModals:['pos-report'],
  mobile:{pages:{'pos':PosMobile},modals:{}},
};
