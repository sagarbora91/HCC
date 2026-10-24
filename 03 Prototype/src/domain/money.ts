// Money is integer paise (DEC-023). Dates are YYYY-MM-DD strings; the clock is local Asia/Kolkata time.
export const money=(n:number)=>new Intl.NumberFormat('en-IN',{style:'currency',currency:'INR',minimumFractionDigits:n%100?2:0,maximumFractionDigits:n%100?2:0}).format(n/100);
/** "1 night" / "2 nights" — count with the right singular or plural word (T-04). */
export const plural=(n:number,word:string,many=word+'s')=>`${n} ${n===1?word:many}`;
export function addDays(d:string,n:number){return new Date(Date.parse(d+'T12:00:00Z')+n*86400000).toISOString().slice(0,10);}
export function nights(a:string,d:string){return Math.round((Date.parse(d)-Date.parse(a))/86400000);}
/** Advance a simulated clock value (YYYY-MM-DDTHH:mm) by whole minutes. Timezone-free arithmetic. */
export function addMinutes(clock:string,n:number){return new Date(Date.parse(clock+':00Z')+n*60000).toISOString().slice(0,16);}
export const OPENING_TIME='08:00';
