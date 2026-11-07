// F7 report registration (F7-CONTRACT §6). Each report lane adds ONE import line inside its own block; the engine (R1) calls these on load.
// F7-R1
import './money/revenue-by-category-tender'; // engine demo report (F7-CONTRACT §4)
// F7-R1 end
// F7-R3
import './guests/movement-register';
import './guests/guest-register';
import './guests/form-c-register';
import './guests/rate-changes';
import './rooms/room-status';
// F7-R3 end
// F7-R4
import './money/shift-report';
import './money/collection-detail';
import './money/credit-bills';
import './money/expense-register';
import './money/expenses-by-head';
// F7-R4 end
// F7-R5
import './tax/sac-summary';
import './tax/documents-issued';
import './tax/gst-by-bill';
import './tax/invoice-register';
import './tax/bill-corrections';
import './tax/gst-summary';
// F7-R5 end
// F7-R6
import './acc/trial-balance';
import './acc/statement';
import './acc/day-book';
import './acc/pl';
import './acc/balance-sheet';
import './acc/cash-ratio';
import './acc/ageing';
import './acc/tally-xml';
// F7-R6 end
// F7-R7
// F7-R7 end
// F7-R8
import './built/pack';
import './built/money';
import './built/rest';
import './built/stock';
import './built/admin';
// F7-R8 end
// F12-RPT-DAILY
// F12-RPT-DAILY end
// F12-RPT-REVENUE
// F12-RPT-REVENUE end
// F12-RPT-GUEST
// F12-RPT-GUEST end
// F12-RPT-HK
// F12-RPT-HK end
// F12-RPT-PEOPLE
import './meas-people/staff-productivity';
import './meas-people/attrition';
import './meas-people/agent-scorecard';
import './meas-people/training';
import './meas-people/staff-pulse';
// F12-RPT-PEOPLE end
// F12-STUDY-A
// F12-STUDY-A end
// F12-STUDY-B
// F12-STUDY-B end
// F12-STUDY-PACK
// F12-STUDY-PACK end
// F12-DIGEST
// F12-DIGEST end
export {};
