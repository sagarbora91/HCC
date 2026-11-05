// F12 MEAS · cap-desk fixtures (fictional). Used lazily: the slice s.measX stays absent until the first capture, so seed / FY26 pins do not move.
import type {MeasAllowanceReason,MeasTurnaway} from '../../domain/streams/measure-types';

/** Default allowance reasons (extends the discount-reasons master; Owner edits through MEAS_ALLOWANCE_REASON_SAVE). */
export const MEAS_ALLOWANCE_DEFAULTS:MeasAllowanceReason[]=[
  {code:'SERVICE-RECOVERY',label:'Service recovery',active:true},
  {code:'LOYALTY',label:'Repeat guest courtesy',active:true},
  {code:'COMPANY-RATE',label:'Company agreed rate',active:true},
  {code:'MAINTENANCE',label:'Room fault or noise',active:true},
  {code:'OTHER',label:'Other (write the reason)',active:true},
];

/** Demo turn-aways (fictional) for screenshots and tests that want existing rows; never added to the base seed. */
export const MEAS_TURNAWAY_DEMO:MeasTurnaway[]=[
  {id:'TRN-DEMO-1',at:'2026-10-24T11:20',by:'reception',reason:'full',arrival:'2026-10-31',nights:2,roomType:'Classic',rooms:1,quotedRate:200000,channel:'Walk-in',leadDays:7},
  {id:'TRN-DEMO-2',at:'2026-10-24T15:05',by:'reception',reason:'price',arrival:'2026-10-26',nights:1,roomType:'Deluxe',rooms:1,quotedRate:325000,channel:'Phone',leadDays:2,note:'Wanted Rs 2,500'},
];
