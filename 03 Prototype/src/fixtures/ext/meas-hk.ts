// F12 MEAS · housekeeping / engineering capture fixtures (fictional data). Nothing here is part of the base seed or the FY26 history:
// the seed is untouched (no pin changes); tests and demos call these to fill s.measX on top of a loaded state.
import type {State} from '../../domain/types';
import type {MeasMeterReading} from '../../domain/streams/measure-types';
import {addDays} from '../../domain/money';

/** Seven daily readings per default meter ending on the business date (monotonic, fictional consumption). */
export function measHkDemoReadings(s:State):MeasMeterReading[]{
  const base:Record<string,[number,number]>={'MTR-ELEC':[48210,310],'MTR-WATER':[2290,9],'MTR-LPG':[41,0],'MTR-DG':[612,2]};
  const out:MeasMeterReading[]=[];
  for(const [meter,[start,step]] of Object.entries(base))
    for(let i=0;i<7;i++)out.push({id:`MR-demo-${meter}-${i}`,meter,date:addDays(s.date,i-6),value:start+step*i,by:'maint'});
  return out;
}
/** A copy of `s` with the demo readings in measX. */
export const measHkDemoState=(s:State):State=>({...s,measX:{...(s.measX??{}),readings:measHkDemoReadings(s)}});
