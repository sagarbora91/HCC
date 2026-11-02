// LF-DESK · lane lf-desk (LF contract §8): A3 purpose of visit, A4 vehicle number / type, A5 Form C (foreign guests), A6 check-out basis and due-out.
// Pure helpers over State; the commands are in services/mock/handlers/desk.ts and the BOOK / CHECKIN / CHECKOUT blocks.
//   Settings (Master configuration › Front-desk policies): frontdesk.purposeRequired, frontdesk.formCRequired, frontdesk.defaultCheckoutBasis,
//   frontdesk.lateCheckoutGraceHours, frontdesk.earlyCheckinGraceHours, frontdesk.halfDayPct (money → Owner-only; illustrative, CA / Owner to confirm, OQ-5).
import type {State,Booking,FormC} from '../types';
import {addMinutes,addDays,nights} from '../money';
import {gstForRoomNight} from '../gst';
import {cfgValueOn} from './config';
import {mstByCode,mstRows} from './masters';
import {guestById,guestAccess,guestIdKind,guestLooksLikeIdNumber,type Guest} from './guests';

export const DESK_SETTING_KEYS={purposeRequired:'frontdesk.purposeRequired',formCRequired:'frontdesk.formCRequired',defaultBasis:'frontdesk.defaultCheckoutBasis',
  lateGrace:'frontdesk.lateCheckoutGraceHours',earlyGrace:'frontdesk.earlyCheckinGraceHours',halfDayPct:'frontdesk.halfDayPct'} as const;
const rd=<T,>(s:State,key:string,dflt:T):T=>{try{const v=cfgValueOn(s,key);return (v===undefined||v===null?dflt:v) as T;}catch{return dflt;}};
export const deskPurposeRequired=(s:State)=>rd<boolean>(s,DESK_SETTING_KEYS.purposeRequired,true)!==false;
export const deskFormCRequired=(s:State)=>rd<boolean>(s,DESK_SETTING_KEYS.formCRequired,true)!==false;
export const deskDefaultBasis=(s:State):'fixed'|'24h'=>rd<string>(s,DESK_SETTING_KEYS.defaultBasis,'fixed')==='24h'?'24h':'fixed';
export const deskLateGraceHours=(s:State)=>Math.max(0,Math.min(24,Number(rd(s,DESK_SETTING_KEYS.lateGrace,1))||0));
export const deskEarlyGraceHours=(s:State)=>Math.max(0,Math.min(24,Number(rd(s,DESK_SETTING_KEYS.earlyGrace,1))||0));
export const deskHalfDayPct=(s:State)=>{const v=Number(rd(s,DESK_SETTING_KEYS.halfDayPct,50));return Number.isFinite(v)?Math.max(0,Math.min(100,v)):50;};

/** The six Front-desk settings (registered in config.ts' builtins block). */
export const deskSettingDefs=[
  {key:DESK_SETTING_KEYS.purposeRequired,label:'Purpose of visit required at check-in',type:'boolean' as const,default:true,unit:'on / off',
    help:'Check-in asks for the purpose of visit (Master configuration › Purposes of visit). The old software required it.',affects:'Check-ins from now on; stays already in house are unchanged.'},
  {key:DESK_SETTING_KEYS.formCRequired,label:'Form C required for foreign guests',type:'boolean' as const,default:true,unit:'on / off',
    help:'A guest whose nationality is not Indian needs a passport ID, date of birth and the Form C details before check-in.',affects:'Check-ins from now on.'},
  {key:DESK_SETTING_KEYS.defaultBasis,label:'Default check-out basis',type:'choice' as const,options:['fixed','24h'],default:'fixed',unit:'fixed time / 24 hours',
    help:'fixed = the check-out time on the departure date; 24h = 24 hours per night from the check-in time. Each stay can be changed while in house.',affects:'Bookings made from now on.'},
  {key:DESK_SETTING_KEYS.lateGrace,label:'Late check-out grace',type:'integer' as const,min:0,max:24,default:1,suffix:'h',unit:'hours after the due-out time',
    help:'After the due-out time plus this grace, check-out asks for the late check-out charge.',affects:'Check-outs from now on.'},
  {key:DESK_SETTING_KEYS.earlyGrace,label:'Early check-in grace',type:'integer' as const,min:0,max:24,default:1,suffix:'h',unit:'hours before the check-in time',
    help:'Checking in earlier than this shows a warning (no charge).',affects:'Warnings at check-in from now on.'},
  {key:DESK_SETTING_KEYS.halfDayPct,label:'Half-day late check-out charge',type:'integer' as const,min:0,max:100,default:50,suffix:'%',money:true,unit:'% of the night rate',
    illustrative:true,help:'Illustrative — CA to confirm for a hotel. The late check-out line is this share of the stay’s room rate, at the room GST slab of that rate. ',affects:'Late check-out charges from now on; lines already posted are unchanged.'},
];

// ───────────────────────────── A4 vehicle ─────────────────────────────
const VEHICLE=/^[A-Z0-9][A-Z0-9 -]{2,11}[A-Z0-9]$/;
/** Upper-case, spaces collapsed; 4–13 characters of A–Z 0–9 - space. */
export function deskVehicleNormalise(raw:unknown):{ok:true;no:string}|{ok:false;message:string}{
  const no=String(raw??'').trim().toUpperCase().replace(/\s+/g,' ');
  if(!no)return {ok:false,message:'Enter the vehicle number.'};
  if(no.length<4||no.length>13||!VEHICLE.test(no))return {ok:false,message:'Vehicle number: 4–13 characters, letters, digits, hyphen or space (for example MH24 AB 1234).'};
  return {ok:true,no};
}
/** Search key of a vehicle number (spaces and hyphens ignored). */
export const deskVehicleKey=(v:unknown)=>String(v??'').toUpperCase().replace(/[\s-]/g,'');
export const deskVehicleMatches=(b:Pick<Booking,'vehicle'>,q:string)=>{const k=deskVehicleKey(q);return k.length>=3&&!!b.vehicle&&deskVehicleKey(b.vehicle.no).includes(k);};
/** A vehicle given by a command → the stored {no,type?}; throws nothing (returns the message). */
export function deskVehicleOf(s:State,raw:unknown):{ok:true;vehicle?:{no:string;type?:string}}|{ok:false;message:string}{
  if(raw===undefined||raw===null)return {ok:true};
  const o=raw as {no?:unknown;type?:unknown};
  if(!String(o.no??'').trim()&&!String(o.type??'').trim())return {ok:true};
  const n=deskVehicleNormalise(o.no);if(!n.ok)return n;
  const t=String(o.type??'').trim();
  if(t){const row=mstRows(s,'vehicleTypes').find(r=>r.name.toLowerCase()===t.toLowerCase()||r.code===t.toUpperCase());
    if(!row)return {ok:false,message:`“${t}” is not in Master configuration › Vehicle types.`};
    if(!row.active&&row.name!==t)return {ok:false,message:`${row.name} is retired — choose an active vehicle type.`};
    return {ok:true,vehicle:{no:n.no,type:row.name}};}
  return {ok:true,vehicle:{no:n.no}};
}

// ───────────────────────────── A3 purpose ─────────────────────────────
/** Message when `code` is not an active purpose of visit (undefined when fine or empty). */
export function deskPurposeProblem(s:State,code:unknown):string|undefined{
  const c=String(code??'').trim();if(!c)return undefined;
  const row=mstByCode(s,'purposes',c.toUpperCase());
  if(!row)return `“${c}” is not in Master configuration › Purposes of visit.`;
  return row.active?undefined:`${row.name} is retired — choose an active purpose of visit.`;
}
export const deskPurposeLabel=(s:State,code?:string)=>code?(mstByCode(s,'purposes',code)?.name??code):'';

// ───────────────────────────── A5 Form C ─────────────────────────────
const ISO=/^\d{4}-\d{2}-\d{2}$/;
const validIso=(v:unknown)=>typeof v==='string'&&ISO.test(v)&&!Number.isNaN(Date.parse(v+'T00:00:00Z'))&&new Date(v+'T00:00:00Z').toISOString().slice(0,10)===v;
/** A guest is a foreign national when the nationality is filled in and is not Indian. */
export const deskIsForeign=(g?:Pick<Guest,'nationality'>)=>{const n=String(g?.nationality??'').trim().toLowerCase();return !!n&&n!=='indian'&&n!=='india';};
/** Form C is needed at check-in: the registered guest is foreign and the Owner setting is on. */
export const deskFormCNeeded=(s:State,b:Booking)=>deskFormCRequired(s)&&deskIsForeign(guestById(s,b.guestId));
export const FORMC_TEXT:(keyof FormC)[]=['passportPlace','visaNo','visaType','visaPlace','arrivedFrom','nextDestination','addressAbroad'];
/** First problem of a Form C draft for this stay (undefined = valid). The passport number itself is the guest's ID number. */
export function deskFormCValidate(s:State,b:Pick<Booking,'arrival'|'departure'>,g:Guest|undefined,fc:Partial<FormC>|undefined):string|undefined{
  if(!g)return 'Link the guest’s registration (with the passport) before the Form C.';
  if(guestIdKind(g.idType)!=='passport')return 'Form C needs the guest’s ID type to be Passport — update the guest profile.';
  if(!g.idNumber&&!g.idLast4)return 'Enter the passport number in the guest’s ID details.';
  if(!String(g.dob??'').trim())return 'Form C needs the guest’s date of birth — update the guest profile.';
  if(!fc)return 'Enter the Form C details (foreign guest).';
  const t=(k:keyof FormC,label:string,max=80)=>{const v=String(fc[k]??'').trim();return !v?`Form C: enter ${label}.`:v.length>max?`Form C: ${label} is too long (maximum ${max} characters).`:undefined;};
  for(const [k,l,m] of [['passportPlace','the place the passport was issued',60],['visaNo','the visa number',30],['visaType','the visa type',30],['visaPlace','the place the visa was issued',60],
    ['arrivedFrom','where the guest arrived from',60],['nextDestination','the next destination',60],['addressAbroad','the permanent address abroad',160]] as [keyof FormC,string,number][]){
    const m2=t(k,l,m);if(m2)return m2;}
  for(const [k,l] of [['passportIssued','the passport issue date'],['passportExpiry','the passport expiry date'],['visaIssued','the visa issue date'],['visaExpiry','the visa expiry date'],['arrivalInIndia','the date of arrival in India']] as [keyof FormC,string][])
    if(!validIso(fc[k]))return `Form C: ${l} must be a date (yyyy-mm-dd).`;
  const arr=b.arrival;
  if(fc.passportIssued!>=fc.passportExpiry!)return 'Form C: the passport expiry must be after its issue date.';
  if(fc.passportExpiry!<=arr)return 'Form C: the passport expires on or before the arrival date.';
  if(fc.visaIssued!>=fc.visaExpiry!)return 'Form C: the visa expiry must be after its issue date.';
  if(fc.visaExpiry!<=arr)return 'Form C: the visa expires on or before the arrival date.';
  if(fc.arrivalInIndia!>arr)return 'Form C: the arrival in India cannot be after this stay’s arrival.';
  const d=Number(fc.intendedStayDays);if(!Number.isInteger(d)||d<1||d>3650)return 'Form C: intended stay in India must be at least 1 day.';
  if(!['M','F','X'].includes(String(fc.sex)))return 'Form C: choose the sex of the guest.';
  if(typeof fc.employedInIndia!=='boolean')return 'Form C: say whether the guest is employed in India.';
  if(fc.employedInIndia&&!String(fc.employerOrBusiness??'').trim())return 'Form C: name the employer or business in India.';
  if(String(fc.employerOrBusiness??'').length>80||String(fc.contactAbroad??'').length>80)return 'Form C: employer / contact text is too long (maximum 80 characters).';
  for(const k of ['employerOrBusiness','contactAbroad','addressAbroad','nextDestination','arrivedFrom'] as (keyof FormC)[])
    if(guestLooksLikeIdNumber(fc[k]))return 'Form C: this looks like an ID number. Store ID numbers only in the ID section.';
  void s;return undefined;
}
/** Clean copy of the draft (trimmed text, number fields numeric). */
export function deskFormCClean(fc:FormC):FormC{
  const t=(v:unknown)=>String(v??'').trim();
  return {passportPlace:t(fc.passportPlace),passportIssued:t(fc.passportIssued),passportExpiry:t(fc.passportExpiry),visaNo:t(fc.visaNo).toUpperCase(),visaType:t(fc.visaType),
    visaPlace:t(fc.visaPlace),visaIssued:t(fc.visaIssued),visaExpiry:t(fc.visaExpiry),arrivedFrom:t(fc.arrivedFrom),arrivalInIndia:t(fc.arrivalInIndia),
    intendedStayDays:Number(fc.intendedStayDays),nextDestination:t(fc.nextDestination),employedInIndia:!!fc.employedInIndia,
    ...(t(fc.employerOrBusiness)?{employerOrBusiness:t(fc.employerOrBusiness)}:{}),addressAbroad:t(fc.addressAbroad),...(t(fc.contactAbroad)?{contactAbroad:t(fc.contactAbroad)}:{}),sex:fc.sex};
}
/** The visa number is personal data: last 4 for roles without the ID-details right (PERM-DOC-004 holders see it in full). */
export const deskVisaMasked=(v:string)=>v.length<=4?'••••':'••••'+v.slice(-4);
export const deskVisaShown=(s:State,uid:string,v:string)=>guestAccess(s,uid).idDetails?v:deskVisaMasked(v);
/** Form C as the viewer may see it. */
export const deskFormCView=(s:State,uid:string,fc:FormC):FormC=>({...fc,visaNo:deskVisaShown(s,uid,fc.visaNo)});

export type DeskForeignRow={booking:string;room:string;guest:string;nationality:string;arrival:string;departure:string;status:string;passportNo:string;dob:string;formC?:FormC;missing:boolean};
/** Foreigner register (Customers › Foreigner register): every stay with a foreign guest that arrives (or stayed) in the date range. */
export function deskForeignerRegister(s:State,uid:string,from:string,to:string):DeskForeignRow[]{
  const acc=guestAccess(s,uid).idDetails;
  return s.bookings.filter(b=>b.status!=='Cancelled'&&b.status!=='No show'&&b.arrival<=to&&b.departure>=from).map(b=>({b,g:guestById(s,b.guestId)})).filter(x=>x.g&&deskIsForeign(x.g)||x.b.formC)
    .sort((a,c)=>a.b.arrival.localeCompare(c.b.arrival)||a.b.room.localeCompare(c.b.room)).map(({b,g})=>({booking:b.id,room:b.room,guest:b.guest,nationality:g?.nationality??'',arrival:b.arrival,departure:b.departure,
      status:b.status,passportNo:acc?(g?.idNumber??''):(g?.idLast4?'••••'+g.idLast4:''),dob:g?.dob??'',...(b.formC?{formC:deskFormCView(s,uid,b.formC)}:{}),missing:!b.formC}));
}
/** CSV (Bureau of Immigration style column order; the exact FRRO upload file is W8's, OQ-4). Visa number masked per viewer. */
export const DESK_FORMC_CSV_COLUMNS=['Booking','Room','Guest name','Sex','Date of birth','Nationality','Passport no','Passport place of issue','Passport issue date','Passport expiry date',
  'Visa no','Visa type','Visa place of issue','Visa issue date','Visa expiry date','Arrived from','Date of arrival in India','Arrival at hotel','Intended stay (days)','Departure',
  'Next destination','Employed in India','Employer / business','Address abroad','Contact abroad'];
export function deskForeignerCsvRows(rows:DeskForeignRow[]):string[][]{
  return rows.map(r=>{const f=r.formC;return [r.booking,r.room,r.guest,f?.sex??'',r.dob,r.nationality,r.passportNo,f?.passportPlace??'',f?.passportIssued??'',f?.passportExpiry??'',
    f?.visaNo??'',f?.visaType??'',f?.visaPlace??'',f?.visaIssued??'',f?.visaExpiry??'',f?.arrivedFrom??'',f?.arrivalInIndia??'',r.arrival,f?String(f.intendedStayDays):'',r.departure,
    f?.nextDestination??'',f?(f.employedInIndia?'Yes':'No'):'',f?.employerOrBusiness??'',f?.addressAbroad??'',f?.contactAbroad??''];});
}

// ───────────────────────────── A6 check-out basis, due-out, late check-out ─────────────────────────────
export const deskBasis=(s:State,b:Pick<Booking,'checkoutBasis'>):'fixed'|'24h'=>b.checkoutBasis??deskDefaultBasis(s);
/** The moment the guest is due out (YYYY-MM-DDTHH:mm). fixed → departure date at the check-out time; 24h → check-in time + 24 h per night. */
export function stayDueOut(s:State,b:Booking):string{
  if(deskBasis(s,b)==='24h'){
    const from=b.checkedInAt??`${b.arrival}T${s.settings.checkin}`;
    return addMinutes(from,nights(b.arrival,b.departure)*24*60);
  }
  return `${b.departure}T${s.settings.checkout}`;
}
export const deskDueLabel=(due:string)=>due.slice(11,16);
export type LateCheckout={late:boolean;dueOut:string;minutesPast:number;hhmm:string;message:string;halfDay:{amount:number;taxBps:number;label:string};fullNight:boolean};
/** Is `clock` past the due-out time plus the grace? The half-day amount = round(rate × halfDayPct / 100) at the room slab of that rate (DEC-069). */
export function stayLateCheckout(s:State,b:Booking,clock=s.clock):LateCheckout{
  const dueOut=stayDueOut(s,b),limit=addMinutes(dueOut,deskLateGraceHours(s)*60);
  const past=Math.max(0,Math.round((Date.parse(clock+':00Z')-Date.parse(dueOut+':00Z'))/60000));
  const late=clock>limit;
  const hhmm=`${Math.floor(past/60)}:${String(past%60).padStart(2,'0')}`;
  const amount=Math.round(b.rate*deskHalfDayPct(s)/100);
  const date=clock.slice(0,10);
  return {late,dueOut,minutesPast:past,hhmm,fullNight:true,
    message:`Guest is past the due-out time by ${hhmm} — post the late check-out charge (half day) or extend the stay first.`,
    halfDay:{amount,taxBps:gstForRoomNight(s,date,b.rate).bps,label:'Late check-out · half day'}};
}
/** Warning (not a charge) when an arrival checks in earlier than the check-in time minus the grace on a fixed stay. */
export function stayEarlyCheckinWarning(s:State,b:Booking,clock=s.clock):string|undefined{
  if(deskBasis(s,b)!=='fixed')return undefined;
  const open=addMinutes(`${clock.slice(0,10)}T${s.settings.checkin}`,-deskEarlyGraceHours(s)*60);
  return clock<open?`Early check-in: the check-in time is ${s.settings.checkin}. No charge is made automatically — add an extra if the guest should pay.`:undefined;
}
/** Id of the late check-out half-day line of a stay. */
export const deskLateLineId=(bookingId:string)=>`late-half:${bookingId}`;
export const deskLateWaivedId=(bookingId:string)=>`late-waived:${bookingId}`;
export const deskLateDecided=(b:Booking)=>b.lines.some(l=>l.id===deskLateLineId(b.id)||l.id===deskLateWaivedId(b.id));
void addDays;
