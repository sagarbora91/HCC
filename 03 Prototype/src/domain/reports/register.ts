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
import './meas-daily/flash';
import './meas-daily/regrets';
import './meas-daily/rate-variance';
import './meas-daily/room-discrepancy';
import './meas-daily/high-balance';
import './meas-daily/allowances-voids';
import './meas-daily/no-show-cancel';
import './meas-daily/inventory-exceptions';
import './meas-daily/cashier-variance';
// F12-RPT-DAILY end
// F12-RPT-REVENUE
import './meas-revenue/demand-calendar';
import './meas-revenue/pickup-pace';
import './meas-revenue/displacement';
import './meas-revenue/overbooking-wash';
import './meas-revenue/restriction-effect';
import './meas-revenue/rate-parity';
// F12-RPT-REVENUE end
// F12-RPT-GUEST
import './meas-guest/satisfaction';
import './meas-guest/reputation-index';
import './meas-guest/service-recovery';
import './meas-guest/desk-timing';
import './meas-guest/upsell';
import './meas-guest/repeat-loyalty';
// F12-RPT-GUEST end
// F12-RPT-HK
import './meas-hk/hk-productivity';
import './meas-hk/ooo-days';
import './meas-hk/ppm-compliance';
import './meas-hk/utilities-per-room';
import './meas-hk/linen-cost';
import './meas-hk/asset-life';
// F12-RPT-HK end
// F12-RPT-PEOPLE
import './meas-people/staff-productivity';
import './meas-people/attrition';
import './meas-people/agent-scorecard';
import './meas-people/training';
import './meas-people/staff-pulse';
// F12-RPT-PEOPLE end
// F12-STUDY-A
import './study-a/revenue-monthly';
import './study-a/year-comparison';
import './study-a/index-vs-base';
import './study-a/fy-deep-dive';
import './study-a/occupancy-history';
import './study-a/operations-patterns';
import './study-a/rooms-and-rates';
import './study-a/zero-rate-complimentary';
import './study-a/receivables-bad-debt';
// F12-STUDY-A end
// F12-STUDY-B
import './study-b/index';
// F12-STUDY-B end
// F12-STUDY-PACK
import './study-pack/management-pack';
// F12-STUDY-PACK end
// F12-DIGEST
// F12-DIGEST end
export {};
