// F8-RPT-P2-B · permission codes of matrix v2 that the prototype's v1 matrix has no row for (same grants as the server seeds 1101 / 2201): the company-credit
// report opens with PERM-GRP-001 and shows money with PERM-GRP-004 (Owner, Manager, Reception, Accountant, Cashier), the licence-expiry report with PERM-CMP-001
// (compliance.use: Owner + Accountant only, DEC-055). Filled into the engine's grant table from this lane's own file (the engine file is not edited).
import {RPT_V2_GRANTS} from '../mask';

RPT_V2_GRANTS['PERM-GRP-001']=['Owner','Manager','Reception','Accountant','Cashier'];
RPT_V2_GRANTS['PERM-GRP-004']=['Owner','Manager','Reception','Accountant','Cashier'];
RPT_V2_GRANTS['PERM-CMP-001']=['Owner','Accountant'];
