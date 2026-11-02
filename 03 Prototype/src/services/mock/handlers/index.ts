import type {Handler} from '../commands';
import {handlers as reservations} from './reservations';
import {handlers as guests} from './guests';
import {handlers as folio} from './folio';
import {handlers as housekeeping} from './housekeeping';
import {handlers as admin} from './admin';
import {handlers as closing} from './closing';
import {handlers as expenses} from './expenses'; // DEC-120: expenses with the SCC features
import {handlers as yieldMod} from './yield';
import {handlers as groups} from './groups';
import {handlers as channels} from './channels';
import {handlers as maintenance} from './maintenance';
import {handlers as inventory} from './inventory';
import {handlers as handover} from './handover';
import {handlers as services} from './services';
import {handlers as messaging} from './messaging';
import {handlers as pos} from './pos';
import {handlers as staff} from './staff';
import {handlers as accounting} from './accounting';
// ACC-1 lanes: add ONE import line for your handler file below this comment and spread it in the ACC1 object (append only).
import {handlers as accountingChart} from './accounting-chart'; // ACC-1 chart lane
import {handlers as accountingOpening} from './accounting-opening';
// ACC-1 end
// ACC-2 lanes: add ONE import line for your handler file below this comment and spread it in the ACC2 object (append only).
import {handlers as expensesVendor} from './expenses-vendor'; // acc2-purchase (contract §3.2, §3.5)
import {handlers as accountingManual} from './accounting-manual'; // ACC-2 acc2-manual lane
import {handlers as accountingTds} from './accounting-tds'; // ACC-2 tds lane
import {handlers as accountingGst} from './accounting-gst'; // ACC-2 gst lane
import {handlers as accountingFlows} from './accounting-flows'; // ACC-2 flows lane (DEPOSIT_APPLY, EXP_UD_PAY_REVERSE)
// ACC-2 end
// ACC-3 lanes: ONE import line below (append only).
import {handlers as accountingAssets} from './accounting-assets'; // ACC-3 acc3-carry (C1)
// ACC-3 end
import {handlers as assistant} from './assistant';
import {handlers as compliance} from './compliance';
import {handlers as cashbook} from './cashbook';
import {handlers as cashout} from './cashout';
import {handlers as masters} from './masters';
import {handlers as credentials} from './credentials'; // A02b-05 self-service password / PIN change
import {handlers as syshealth} from './syshealth';
import {handlers as roommasters} from './roommasters';
import {handlers as keys} from './keys'; // KEYS room keys
// LF lanes: ONE import line inside your own block (append only); spread it in the LF object below.
// LF-DOCDB
// LF-DOCDB end
// LF-RATE
import {handlers as stayRate} from './stay-rate';
// LF-RATE end
// LF-BILL
import {handlers as billCorrection} from './bill-correction';
// LF-BILL end
// LF-COUNTER
import {handlers as counter} from './counter';
// LF-COUNTER end
// LF-DESK
// LF-DESK end
// LF-ALERTS
// LF-ALERTS end
// LF-IMAGES
// LF-IMAGES end
import {handlers as catalogue} from './catalogue'; // MA-2 (last: wraps folio APPROVAL)
const ACC1:Record<string,Handler>={...accountingChart,...accountingOpening,}; // ACC-1 lanes: spread your handlers here, e.g. {...accountingChart}
const ACC2:Record<string,Handler>={...expensesVendor,...accountingManual,...accountingTds,...accountingGst,...accountingFlows,...accountingAssets,}; // ACC-2 lanes: spread your handlers here, e.g. {...accountingManual}
const LF:Record<string,Handler>={ // LF lanes: spread your handlers inside your own block, e.g. ...stayRate,
  // LF-DOCDB
  // LF-DOCDB end
  // LF-RATE
  ...stayRate,
  // LF-RATE end
  // LF-BILL
  ...billCorrection,
  // LF-BILL end
  // LF-COUNTER
  ...counter,
  // LF-COUNTER end
  // LF-DESK
  // LF-DESK end
  // LF-ALERTS
  // LF-ALERTS end
  // LF-IMAGES
  // LF-IMAGES end
};
export const registry:Record<string,Handler>={...ACC1,...ACC2,...reservations,...guests,...folio,...housekeeping,...admin,...closing,...expenses,...yieldMod,...groups,...channels,...maintenance,...inventory,...handover,...services,...messaging,...pos,...staff,...accounting,...assistant,...compliance,...cashbook,...cashout,...masters,...credentials,...syshealth,...roommasters,...keys,...LF,...catalogue};
