// MB-06 money (M09–M11, M15–M16) — phone screens. Registers mobile.pages / mobile.modals keyed by the desktop page / modal ID they replace on phones.
// M17 Approvals inbox belongs to the admin mobile stream. Commands are the existing desktop ones (see ./logic.ts).
import type {FeatureRegistration} from '../../app/registry';
import {MmFolios,MmFolioSheet,MmFolioMore} from './Folio';
import {MmPayment} from './Payment';
import {MmCheckout,MmInvoice} from './Checkout';
import {MmCashier,MmCountSheet} from './Cashier';
import {MmExpenses,MmExpenseSheet} from './Expenses';
export const registration:FeatureRegistration={
  // Phone-only sheets (mm- IDs) are also registered as regular modals so they open in any display mode.
  modals:{'mm-folio-more':MmFolioMore,'mm-count':MmCountSheet},
  mobile:{
    pages:{folios:MmFolios,cashier:MmCashier,expenses:MmExpenses},
    modals:{
      booking:p=><MmFolioSheet id={p.id}/>,   // M09 folio sheet
      payment:p=><MmPayment id={p.id}/>,      // M10
      checkout:p=><MmCheckout id={p.id}/>,    // M11
      invoice:p=><MmInvoice id={p.id}/>,      // M11 invoice preview + print (DEC-047)
      expense:MmExpenseSheet,                 // M16 Add expense
    },
  },
};
