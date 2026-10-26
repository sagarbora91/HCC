// MA-1 · Rooms master: is a room out of the sellable inventory for nights [a, d) because of its MASTER status (retired, or out of
// order)? Pure and dependency-free so domain/selectors.ts (metrics), streams/yield.ts and streams/reservations.ts can read it without
// an import cycle. Availability itself follows through the room hold registered in streams/roommasters.ts (registerRoomHold).
import type {Room} from '../types';

/** Retired rooms are off from their retirement date (no date = always). */
export const rmsRetiredDuring=(r:Pick<Room,'status'|'retiredOn'>,_a:string,d:string)=>r.status==='retired'&&(!r.retiredOn||d>r.retiredOn);
/** Out-of-order window [from, to) overlaps [a, d). */
export const rmsOooDuring=(r:Pick<Room,'ooo'>,a:string,d:string)=>!!r.ooo&&a<(r.ooo.to??'9999-12-31')&&d>r.ooo.from;
/** Off the sellable inventory for any night in [a, d) (retired or out of order). Rooms without master fields are never off. */
export const rmsOffline=(r:Pick<Room,'status'|'retiredOn'|'ooo'>,a:string,d:string)=>rmsRetiredDuring(r,a,d)||rmsOooDuring(r,a,d);
/** Shown on boards and pick lists (retired rooms are hidden; they stay in history). */
export const rmsListed=(r:Pick<Room,'status'>)=>r.status!=='retired';
