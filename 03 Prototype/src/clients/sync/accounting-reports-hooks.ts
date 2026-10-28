// ACC-2 · lane acc2-parity: React hooks for the production report readers (./accounting-reports.ts). Imported only from
// `process.env.HCC_BUILD==='production'` branches of the accounting screens (the UI wiring is in ACC2-PARITY-DONE.md, Integration needed).
// Same fetch rule as the server reports (reportsHooks.ts useServerFetch): refetch on a key change or the runtime's data stamp, keep the last
// result of the same key while reloading or offline. Gate: PERM-ACC-001 through the trial-balance read rule (useMayRead).
import {useServerFetch,useMayRead,type Fetched} from './reportsHooks';
import {loadAccStatement,loadAccTrialBalance6,loadAccDayBook} from './accounting-reports';
import type {ACCStatement,ACCTB6,ACCDayBook} from '../../domain/streams/accounting-reports';

/** May the signed-in user read the accounting reports (PERM-ACC-001)? */
export const useMayReadAccReports=():boolean=>useMayRead('GET /accounting/trial-balance');
export const useAccStatement=(account:string,from:string,to:string,enabled=true):Fetched<ACCStatement>=>
  useServerFetch(enabled&&account&&from&&to?`acc-st|${account}|${from}|${to}`:null,api=>loadAccStatement(api,account,from,to));
export const useAccTrialBalance6=(from:string,to:string,detail:boolean,enabled=true):Fetched<ACCTB6>=>
  useServerFetch(enabled&&from&&to?`acc-tb6|${from}|${to}|${detail?1:0}`:null,api=>loadAccTrialBalance6(api,from,to,detail));
export const useAccDayBook=(from:string,to:string,enabled=true):Fetched<ACCDayBook>=>
  useServerFetch(enabled&&from&&to?`acc-db|${from}|${to}`:null,api=>loadAccDayBook(api,from,to));
