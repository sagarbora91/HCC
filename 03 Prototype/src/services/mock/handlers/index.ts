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
import {handlers as accountingGst} from './accounting-gst'; // ACC-2 gst lane
// ACC-2 end
import {handlers as assistant} from './assistant';
import {handlers as compliance} from './compliance';
import {handlers as cashbook} from './cashbook';
import {handlers as cashout} from './cashout';
import {handlers as masters} from './masters';
import {handlers as credentials} from './credentials'; // A02b-05 self-service password / PIN change
import {handlers as syshealth} from './syshealth';
import {handlers as roommasters} from './roommasters';
import {handlers as catalogue} from './catalogue'; // MA-2 (last: wraps folio APPROVAL)
const ACC1:Record<string,Handler>={...accountingChart,...accountingOpening,}; // ACC-1 lanes: spread your handlers here, e.g. {...accountingChart}
const ACC2:Record<string,Handler>={...accountingGst,}; // ACC-2 lanes: spread your handlers here, e.g. {...accountingManual}
export const registry:Record<string,Handler>={...ACC1,...ACC2,...reservations,...guests,...folio,...housekeeping,...admin,...closing,...expenses,...yieldMod,...groups,...channels,...maintenance,...inventory,...handover,...services,...messaging,...pos,...staff,...accounting,...assistant,...compliance,...cashbook,...cashout,...masters,...credentials,...syshealth,...roommasters,...catalogue};
