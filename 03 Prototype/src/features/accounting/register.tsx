// Module 20 · Accounting automation — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import {BookOpenCheck} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {AccountingPage} from './Accounting';
import {AccountingMobile} from './Mobile';
import {AccVoucherModal,AccLockModal,AccUnlockModal,AccMapModal,AccLedgerModal,AccGroupModal,AccOpeningLineModal} from './parts';
import {ManualModal,ManualDecideModal,ManualVoidModal} from './manual';
import {GstPayModal,StatRemitModal,MonthLockModal,MonthUnlockModal} from './gst';
import {TdsRemitModal,LoanModal} from './tds';
import {AssetPutModal,AssetDisposeModal,VendorReallocModal,TdsForm168Modal,GstAdvancesModal} from './carry-ui';
import {YearLockModal,YearRelockModal,YearReopenModal,BankIgnoreModal,BankCreateModal,BankSettleModal,BankLayoutModal} from './acc4-ui';
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
    // ACC-3 (acc3-ui)
    'acc-asset-put':p=><AssetPutModal id={p.id}/>,           // id = asset id (Owner, Accountant)
    'acc-asset-dispose':p=><AssetDisposeModal id={p.id}/>,   // id = asset id (Owner, Accountant)
    'exp-vendor-realloc':p=><VendorReallocModal id={p.id}/>, // id = vendor payment id (Owner, Manager, Accountant)
    'acc-tds-form168':p=><TdsForm168Modal id={p.id}/>,       // id = TDS remittance voucher id (Owner, Accountant)
    'acc-gst-advances':()=><GstAdvancesModal/>,              // Owner
    // ACC-4 (F5-U1)
    'acc-year-lock':p=><YearLockModal id={p.id}/>,           // id = FY (Owner)
    'acc-year-relock':p=><YearRelockModal id={p.id}/>,       // id = FY (Owner)
    'acc-year-reopen':p=><YearReopenModal id={p.id}/>,       // id = FY (Owner, reason)
    'acc-bank-ignore':p=><BankIgnoreModal id={p.id}/>,       // id = statement row (Owner, Accountant)
    'acc-bank-create':p=><BankCreateModal id={p.id}/>,       // id = statement row
    'acc-bank-settle':p=><BankSettleModal id={p.id}/>,       // id = statement row (B8)
    'acc-bank-layout':p=><BankLayoutModal id={p.id}/>,       // id = layout id | 'new' (Owner)
  },
  mobile:{pages:{'accounting':AccountingMobile},modals:{}},
};
