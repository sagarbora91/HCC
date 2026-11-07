// F12-RPT-PEOPLE test entry (bundled by tests/stream-meas-rpt-people.mjs for node): the mock service plus the pure rules of the five people measurement reports.
export * from '../src/services/mock/index';
export {productivityRule,productivityRows,PRODUCTIVITY_COLS} from '../src/domain/reports/meas-people/staff-productivity';
export {attritionRed,attritionRows,ATTRITION_COLS} from '../src/domain/reports/meas-people/attrition';
export {certificateExpired,MANDATORY_COURSES,TRAINING_COLS} from '../src/domain/reports/meas-people/training';
export {enps,PULSE_MIN_ANSWERS,PULSE_COLS} from '../src/domain/reports/meas-people/staff-pulse';
export {scorecardRed,SCORECARD_COLS} from '../src/domain/reports/meas-people/agent-scorecard';
export {aboveUpperQuartile,nearestRank,pct1,shiftMinutes} from '../src/domain/reports/meas-people/shared';
