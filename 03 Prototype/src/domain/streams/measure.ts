// F12 MEAS · switches and targets (MEAS-CONTRACT §1). SETUP STUB: the signatures below are frozen; meas-settings replaces the bodies
// (registers the `measure` settings group, enforces module AND pack AND item) and may add exports, never rename these.
import type {State} from '../types';
import type {MeasPack} from './measure-types';

/** Every switchable item: key = `meas.<pack>.<item>`; `core` items cannot be switched off (contract §1.3). */
export type MeasItem={key:string;pack:MeasPack;label:string;report?:string;capture?:string;core?:boolean;module?:string};

/** True when the pack and the item are on (and, for items tied to an optional module, that module is on). Setup stub: everything on. */
export function measOn(_s:State,_key:string):boolean{return true;}
/** Target value (meas.target.*) as stored: percent as number, money in paise, integer as number. Setup stub: undefined. */
export function measTarget(_s:State,_key:string):number|undefined{return undefined;}
