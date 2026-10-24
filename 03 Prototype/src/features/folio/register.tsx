// Stream C · Payments & folio — UI registration. Pages/modals registered here are merged by app/registry.ts.
// A page or modal with an existing ID replaces the built-in one (only replace IDs your stream owns).
// The owned dialogs booking/payment/charge/discount/checkout/invoice live in ./modals.tsx (imported by ModalHost).
import type {FeatureRegistration} from '../../app/registry';
import {Folios} from './Folios';
import {RefundRequestModal,RefundExecuteModal,CorrectionModal,CreditNoteModal} from './modals';
export const registration:FeatureRegistration={
  pages:{folios:Folios},
  modals:{
    'refund':RefundRequestModal,          // {id: bookingId, room: receiptId}
    'refund-execute':RefundExecuteModal,  // {id: approvalId}
    'correction':CorrectionModal,         // {id: bookingId, room: lineId}
    'credit-note':CreditNoteModal,        // {id: bookingId}
  },
};
