// Module 20 · Accounting automation — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import {BookOpenCheck} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {AccountingPage} from './Accounting';
import {AccountingMobile} from './Mobile';
import {AccVoucherModal,AccLockModal,AccUnlockModal,AccMapModal} from './parts';
export const registration:FeatureRegistration={
  pages:{'accounting':AccountingPage},
  nav:[
    {group:'CLOSING & REPORTS',item:{id:'accounting',label:'Accounting',icon:BookOpenCheck,p:'reports'}}
  ],
  modals:{
    'acc-voucher':AccVoucherModal, // id = "<date>|<voucher no>"
    'acc-lock':AccLockModal,       // id = business date
    'acc-unlock':AccUnlockModal,   // id = business date (owner)
    'acc-map':AccMapModal,         // id = mapping key (admin)
  },
  mobile:{pages:{'accounting':AccountingMobile},modals:{}},
};
