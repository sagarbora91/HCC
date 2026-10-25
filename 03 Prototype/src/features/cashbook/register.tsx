// W05-D · Cash book — UI registration (desktop page, phone page, reopen modal). Merged by app/registry.ts.
import {BookOpen} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {CashBook,CashBookMobile,CbReopenModal} from './CashBook';
export const registration:FeatureRegistration={
  pages:{cashbook:CashBook},
  // Cashier, Manager, Owner, Accountant (all hold 'reports'); Reception does not.
  nav:[{group:'CASH & BILLING',item:{id:'cashbook',label:'Cash book',icon:BookOpen,p:'reports'},after:'cashier'}],
  modals:{'cb-reopen':CbReopenModal}, // id = business date
  mobile:{pages:{cashbook:CashBookMobile},modals:{}},
};
