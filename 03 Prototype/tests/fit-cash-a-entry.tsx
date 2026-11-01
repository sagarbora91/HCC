// FIT-CASH-A test entry (bundled by tests/stream-fit-cash-a.mjs for node): the three money pages of lane fit-cash-a and the pieces they share
// with the phone, rendered without a browser. App first, so the registry's module order is the app's (as tests/stream-fit.mjs does).
import '../src/app/App';
export {Folios} from '../src/features/folio/Folios';
export {Cashier} from '../src/features/closing/Cashier';
export {CashOutPanel} from '../src/features/closing/CashOut';
export {CashBook,CashBookView,DayCard} from '../src/features/cashbook/CashBook';
export {FitNavContext} from '../src/components/fit';
export {SUB_SCREENS} from '../src/app/subscreens';
export {HccContext} from '../src/app/context';
export {seed,can,metrics,cbDay,cbDates} from '../src/services/mock';
