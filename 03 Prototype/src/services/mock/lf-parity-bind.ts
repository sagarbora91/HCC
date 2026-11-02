// LF-ui (lf-tests) · binds the real command engine to the LF-PARITY scenario fixture (fixtures/ext/lf-parity.ts); imported once by the mock barrel.
import {execute} from './commands';
import {lfParityBind} from '../../fixtures/ext/lf-parity';
lfParityBind(execute);
export {lfParityBind,lfParityDay,LF_PARITY,FORM_C as LF_PARITY_FORM_C,LF_PARITY_LEGACY_BILLS,lfParityLegacyBills} from '../../fixtures/ext/lf-parity';
