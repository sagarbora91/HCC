// Stream F · Cashier, closing & reports — UI registration. Pages/modals registered here are merged by app/registry.ts.
// A page or modal with an existing ID replaces the built-in one (only replace IDs your stream owns).
// The 'expenses', 'closing' and 'reports' pages and the 'cash'/'expense' modals are stream F files wired by App/ModalHost directly.
import {Banknote} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {Cashier} from './Cashier';
import {ClosingReportModal} from './ClosingReport';
import {ExpenseReasonModal} from '../expenses/ExpenseModal';
import {ReportPrintModal} from '../reports/Reports';
export const registration:FeatureRegistration={
  pages:{cashier:Cashier},
  // PERM-SHF-001 shift.open/count/submit (coarse: payments)
  nav:[{group:'CASH & BILLING',item:{id:'cashier',label:'Cashier shift',icon:Banknote,p:'payments'},after:'folios'}],
  modals:{
    'closing-report':ClosingReportModal, // id = business date
    'report-print':ReportPrintModal,     // id = report tab, room = 'from|to'
    'expense-reason':ExpenseReasonModal, // id = expense ID, room = 'reject' | 'reverse'
  },
  wideModals:['closing-report','report-print'],
};
