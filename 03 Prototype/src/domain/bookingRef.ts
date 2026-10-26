// The reference a person sees for a booking. Prototype build: the booking id (HCC-1101 style). Production build: the server's public number
// (publicNo, e.g. HCC-1101) while `id` stays the internal key (the server's numeric reservation id). Display only, never use for lookups.
export const bookingRef=(b:{id:string;publicNo?:string}):string=>b.publicNo||b.id;
/** Same, for a record that only stores the booking id (messages, approvals, cash book rows): looks the booking up in the list. */
export const bookingRefOf=(bookings:readonly {id:string;publicNo?:string}[],id:string):string=>{const b=bookings.find(x=>x.id===id);return b?bookingRef(b):id;};
