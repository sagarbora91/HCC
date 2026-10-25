// Public surface of the mock service — used by the UI and by tests/run.mjs.
export * from '../../domain/index';
export {execute} from './commands';
export * from './faults';
export * from './persistence';
export {baseFixture,BUSINESS_DATE} from '../../fixtures/base';
export {scenarios,loadScenario,DEFAULT_SCENARIO} from '../../fixtures/scenarios';
export type {ScenarioInfo} from '../../fixtures/scenarios';
/** Back-compat alias used by earlier tests: the base fixture. */
export {baseFixture as seed} from '../../fixtures/base';
// Stream domain modules (pure helpers/selectors).
export * from '../../domain/streams/reservations';
export * from '../../domain/streams/guests';
export * from '../../domain/streams/folio';
export * from '../../domain/streams/housekeeping';
export * from '../../domain/streams/admin';
export * from '../../domain/streams/closing';
export * from '../../domain/streams/yield';
export * from '../../domain/streams/groups';
export * from '../../domain/streams/channels';
export * from '../../domain/streams/maintenance';
export * from '../../domain/streams/inventory';
export * from '../../domain/streams/handover';
export * from '../../domain/streams/services';
export * from '../../domain/streams/messaging';
export * from '../../domain/streams/pos';
export * from '../../domain/streams/staff';
export * from '../../domain/streams/accounting';
export * from '../../domain/streams/assistant';
export * from '../../domain/streams/compliance';
export {buildHistory,HISTORY_DEFAULTS} from '../../fixtures/history/generate';
export * from '../../domain/streams/history';
