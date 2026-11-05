// F12 MEAS · meas-cap-hk in the production sync layer (MEAS-CONTRACT §2, server mirror schema `meas`: task_times, room_offline, meter, meter_reading,
// asset_life, discard; migration 4815). The tables and the server-side rules (Hcc.Operations/Meas/HkRules.cs) exist; the HTTP endpoints and the
// loaders are the integrator's step once meas-settings has merged the gate, so the five capture commands are listed as gaps with the reason
// instead of being sent to a route that does not exist yet. TASK is already mapped by the housekeeping area; the times are recorded server-side.
import type {SyncArea} from './types';

const PENDING='Server endpoint pending: table and rules are in migration 4815 / Hcc.Operations Meas/HkRules; wire after meas-settings merges the MeasurementGate.';
export const measHk:SyncArea={
  id:'meas-hk',
  loaders:[],
  commands:[],
  unmapped:{
    MEAS_OFFLINE_SET:PENDING,
    MEAS_METER_SAVE:PENDING,
    MEAS_READING_ADD:PENDING,
    MEAS_ASSETLIFE_SET:PENDING,
    MEAS_DISCARD_ADD:PENDING,
  },
};
