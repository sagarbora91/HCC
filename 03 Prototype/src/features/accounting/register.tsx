// Module 20 · Accounting automation — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import {BookOpenCheck} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {AccountingPage} from './Accounting';
import {AccountingMobile} from './Mobile';
import {AccVoucherModal,AccLockModal,AccUnlockModal,AccMapModal,AccLedgerModal,AccGroupModal,AccOpeningLineModal} from './parts';
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
    'acc-ledger':AccLedgerModal,   // ACC-1 · id = ledger code | 'new' | 'new:<group>' (Owner edits)
    'acc-group':AccGroupModal,     // ACC-1 · id = custom group id | 'new' | 'new:<parent>' (Owner)
    'acc-opening-line':AccOpeningLineModal, // ACC-1 · id = ledger / party id | 'new' (Owner, Manager, Accountant)
  },
  mobile:{pages:{'accounting':AccountingMobile},modals:{}},
};
