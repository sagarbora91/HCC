// FIT-GUEST test entry (bundled by tests/stream-fit-guest.mjs for node): the five converted pages + the shell context, rendered without a browser.
export {GuestServicesPage,GsvMyRequestsPage} from '../src/features/services/GuestServices';
export {MsgMessagesPage} from '../src/features/messaging/Messages';
export {MsgDocumentsPage} from '../src/features/messaging/Documents';
export {CustomersPage,CustomerPanel} from '../src/features/customers/Customers';
export {FitNavContext} from '../src/components/fit';
export {HccContext} from '../src/app/context';
export {SUB_SCREENS} from '../src/app/subscreens';
export {can,metrics,loadScenario,msgOutbox} from '../src/services/mock';
