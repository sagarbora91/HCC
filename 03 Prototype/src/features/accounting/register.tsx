// Module 20 · Accounting automation — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import {BookOpenCheck} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {AccountingPage} from './Accounting';
import {AccountingMobile} from './Mobile';
import {AccVoucherModal,AccLockModal,AccUnlockModal,AccMapModal,AccLedgerModal,AccGroupModal,AccOpeningLineModal} from './parts';
import {ManualModal,ManualDecideModal,ManualVoidModal} from './manual';
import {GstPayModal,StatRemitModal,MonthLockModal,MonthUnlockModal} from './gst';
import {TdsRemitModal,LoanModal} from './tds';
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
    // ACC-2 (acc2-ui)
    'acc-manual':p=><ManualModal id={p.id}/>,               // id = voucher id | 'new' | 'new:<template>' (Owner, Accountant enter; Owner approves)
    'acc-manual-decide':p=><ManualDecideModal id={p.id}/>,  // id = voucher id (Owner rejects with a reason)
    'acc-manual-void':p=><ManualVoidModal id={p.id}/>,      // id = voucher id (Owner, reason)
    'acc-gst-pay':p=><GstPayModal id={p.id} room={p.room}/>,// id = YYYY-MM, room = 'rcm'
    'acc-stat-remit':p=><StatRemitModal id={p.id}/>,        // id = YYYY-MM (PF / ESIC / PT)
    'acc-month-lock':p=><MonthLockModal id={p.id}/>,        // id = YYYY-MM (Owner)
    'acc-month-unlock':p=><MonthUnlockModal id={p.id}/>,    // id = YYYY-MM (Owner, reason)
    'acc-tds-remit':p=><TdsRemitModal id={p.id}/>,          // id = YYYY-MM
    'acc-loan':()=><LoanModal/>,                            // Owner: Add loan under 2400
  },
  mobile:{pages:{'accounting':AccountingMobile},modals:{}},
};
