// Core area (W4-0): the hotel clock and a provisional business date, from GET /system/health.
// `clock` = server time in hotel-local "YYYY-MM-DDTHH:mm" (Asia/Kolkata). `date` = that clock's calendar date — PROVISIONAL:
// the real business date only changes at day close (DEC-042). W4-4 (closing) adds a `kind:'patch'` loader on 'date' from the
// server's business date (GET /closing/preflight `businessDate`, or a light system endpoint) — no change here needed.
import type {HealthResult} from '../types';
import type {SyncArea} from './types';
import {utcToClock} from './map';

export const core:SyncArea={
  id:'core',
  loaders:[
    {slice:'clock',async load(c){const h=await c.api.get<HealthResult>('/system/health');return utcToClock(h.serverTimeUtc)||c.current('clock');}},
    {slice:'date',needs:['clock'],async load(c){return c.current('clock').slice(0,10)||c.current('date');}},
  ],
  commands:[],
};
