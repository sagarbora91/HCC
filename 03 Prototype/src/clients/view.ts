// Screen helpers over the client DTOs: the availability list in the shape the booking dialogs render, and the
// availability → quote → create sequence shared by the desktop dialog and the phone wizard.
import {REASON_LABEL} from './types';
import type {AvailabilityResult,ReservationsClient,CreateReservationRequest,CommandOptions,Quote,CreateReservationResult} from './types';

export type AvailabilityRow={room:{id:string;type:string};capacity:number;ratePaise:number;occupiedNow:boolean;
  /** Reason labels, main one first ("Blocked", "Over capacity", …). */reasons:string[];/** Main reason as shown. */reason:string};
export type AvailabilityView={valid:boolean;party:number;nights:number;available:AvailabilityRow[];excluded:AvailabilityRow[]};

export function availabilityView(r:AvailabilityResult):AvailabilityView{
  const rows=r.rooms.map(x=>{const reasons=x.reasons.map(c=>REASON_LABEL[c]??c);
    return {room:{id:x.roomId,type:x.roomTypeId},capacity:x.capacity,ratePaise:x.nightlyRatePaise,occupiedNow:!!x.occupiedNow,reasons,
      reason:x.available?'Available':x.reasonText||reasons[0]||(r.valid?'Not available':'Choose valid dates'),ok:x.available};});
  const strip=({ok,...row}:typeof rows[number])=>{void ok;return row;};
  return {valid:r.valid,party:r.party,nights:r.nights,available:rows.filter(x=>x.ok).map(strip),excluded:rows.filter(x=>!x.ok).map(strip)};
}
export const EMPTY_AVAILABILITY:AvailabilityView={valid:false,party:0,nights:0,available:[],excluded:[]};

export type StayInput={arrival:string;departure:string;adults:number;children:number};
/** Quote the chosen room, then create the reservation from that quote (contracts §3: CreateReservation needs quoteId+version). */
export async function quoteAndCreate(c:ReservationsClient,stay:StayInput,req:Omit<CreateReservationRequest,'quoteId'|'quoteVersion'>,opts:CommandOptions):Promise<CreateReservationResult&{quote:Quote}>{
  const quote=await c.quoteStay({...stay,roomId:req.roomId},{idempotencyKey:(opts.idempotencyKey+':quote').slice(0,80)});
  const res=await c.createReservation({...req,quoteId:quote.quoteId,quoteVersion:quote.version},opts);
  return {...res,quote};
}
