// ACC-1 acc1-tests · binds the real command engine to the ACC-RULEBOOK scenario fixture (fixtures/ext/accounting-rulebook.ts) so a
// fixture file never imports the command registry (the scenario worker would carry every handler). Imported once by the mock barrel.
import {execute} from './commands';
import {accRulebookBind} from '../../fixtures/ext/accounting-rulebook';
accRulebookBind(execute);
export {accRulebookBind,accRulebookMonth,accRulebookOpeningLines,ACC_RULEBOOK,ACC_RB,ACC_RB_PLAN} from '../../fixtures/ext/accounting-rulebook';
