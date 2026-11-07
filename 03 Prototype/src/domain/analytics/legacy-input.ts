// F12 MEAS · meas-study-a · builds the single AnInput of every study selector from State (live HCC) plus the imported legacy history (`lgc`).
// Legacy rows come from a registered source (production: the server's lgc read; prototype: none, so a prototype run is live-only). Halls and
// duplicate stays never enter. Money = integer paise.
import type {State} from '../types';
import {lineTax} from '../gst';
import type {AnInput,AnStay,AnBill,AnReceipt,AnRoom} from './types';

export type LegacyAnRows={rooms?:AnRoom[];stays:AnStay[];bills:AnBill[];receipts:AnReceipt[]};
let source:(()=>LegacyAnRows)|undefined;
/** Production wiring / tests: where the lgc history comes from. */
export const setLegacyAnSource=(f:(()=>LegacyAnRows)|undefined)=>{source=f;};
export const LEGACY_UNTIL='2026-10-30';
export const COVID_WINDOW={from:'2020-03-01',to:'2021-03-31'};

const LIVE_OK=['In house','Departed'];
export function buildAnInput(s:State,legacy:LegacyAnRows|undefined=source?.()):AnInput{
  const rooms:AnRoom[]=s.rooms.map(r=>({id:r.id,type:r.type,inServiceFrom:'2000-01-01',...(r.type==='Suite'||r.id==='211'?{suite:true}:{})}));
  for(const r of legacy?.rooms??[])if(!rooms.some(x=>x.id===r.id))rooms.push(r);
  const stays:AnStay[]=[...(legacy?.stays??[])],bills:AnBill[]=[...(legacy?.bills??[])],receipts:AnReceipt[]=[...(legacy?.receipts??[])];
  for(const b of s.bookings){
    if(b.hallEventId)continue;
    const nights=Math.max(1,Math.round((Date.parse(b.departure)-Date.parse(b.arrival))/86400000));
    stays.push({id:b.id,guestKey:b.guestId??b.guest,room:b.room,from:b.arrival,to:b.departure,nights,ratePaise:b.rate,source:b.source,channel:b.otaChannel,company:b.company?.name,
      purpose:b.purpose,status:b.status,legacy:false});
    if(!LIVE_OK.includes(b.status)&&!b.lines.length)continue;
    const room=b.lines.filter(l=>l.kind==='room'),fnb=b.lines.filter(l=>l.kind==='food');
    const other=b.lines.filter(l=>l.kind==='service'||l.kind==='adjustment'),disc=b.lines.filter(l=>l.kind==='discount');
    const sum=(xs:{amount:number}[])=>xs.reduce((a,l)=>a+l.amount,0);
    const gst=b.lines.reduce((a,l)=>a+lineTax(l),0);
    const netAmt=sum(b.lines),paid=b.receipts.reduce((a,r)=>a+(r.kind==='payment'||r.kind==='deposit-apply'?r.amount:r.kind==='refund'?-r.amount:0),0);
    const credit=b.receipts.reduce((a,r)=>a+(r.kind==='company-credit'||r.kind==='ota-credit'||r.kind==='guest-credit'?r.amount:0),0);
    bills.push({id:'B-'+b.id,stay:b.id,date:b.status==='Departed'?b.departure:b.arrival,roomChargePaise:sum(room),fnbPaise:sum(fnb),otherPaise:sum(other),discountPaise:-sum(disc),
      gstPaise:gst,totalPaise:netAmt+gst,chargedNights:room.length,paidPaise:Math.min(paid+credit,netAmt+gst),legacy:false});
    for(const r of b.receipts)if(r.kind==='payment'||r.kind==='deposit-apply')receipts.push({id:r.id,bill:'B-'+b.id,date:r.date,method:r.method,amountPaise:r.amount,legacy:false});
  }
  return {rooms,stays,bills,receipts,asOf:s.date,legacyUntil:LEGACY_UNTIL,covidWindow:COVID_WINDOW};
}
