// F7-R3 · A16 print layouts in the Lodge look with the HCC letterhead: the guest registration card and the day-end sheet (Day End Report A: movements;
// B/C: money, only for PERM-RPT-002). Pure: State → {title, body, css} (the shape printHtml() returns; native.ts printablePage wraps it). The ID number
// is always masked; no photos. Server twin: Hcc.Reporting/Reports/Guests/LodgeLayouts.cs (same labels).
import type {State,Booking} from '../../types';
import {HccError} from '../../errors';
import {money,nights} from '../../money';
import {reportPack} from '../../streams/closing';
import {deskPurposeLabel} from '../../streams/desk-fields';
import {rptHolds,maskValue} from '../mask';
import {bookingNo,checkInAtOf,checkOutAtOf,cityOfAddress,guestOfBooking,guestNameOf,idLast4Of,paxOf} from './shared';

export const LODGE_LETTERHEAD='Hotel Control Centre';
const esc=(t:unknown)=>String(t??'').replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
/** Lodge look: serif, boxed label/value cells, ruled sections. Colours are named, not hex (print is always light). */
export const LODGE_CSS='@page{size:A4 portrait}body{font-family:Georgia,"Times New Roman",serif;color:black}.lg-head{text-align:center;border-bottom:2px solid black;padding-bottom:6px;margin-bottom:10px}'+
  '.lg-head .brand{font-size:11pt;letter-spacing:.12em;text-transform:uppercase}.lg-head h1{font-size:18pt;margin:2px 0}.lg-head .sub{font-size:10pt}'+
  '.lg-grid{display:grid;grid-template-columns:1fr 1fr;border:1px solid black}.lg-cell{border:1px solid black;padding:4px 6px;font-size:10.5pt}.lg-cell b{display:block;font-size:8.5pt;text-transform:uppercase;letter-spacing:.05em}'+
  '.lg-sign{display:flex;justify-content:space-between;margin-top:46px;font-size:10pt}.lg-sign div{border-top:1px solid black;width:42%;text-align:center;padding-top:3px}'+
  '.lg-note{font-size:9pt;margin-top:10px}.lg-sec{font-weight:bold;margin:12px 0 4px;border-bottom:1px solid black}';
const head=(property:string,title:string,sub:string)=>`<div class="lg-head"><div class="brand">${esc(LODGE_LETTERHEAD)}</div><h1>${esc(property)}</h1><div class="sub">${esc(title)}${sub?' · '+esc(sub):''}</div></div>`;
const cell=(label:string,value:unknown,wide=false)=>`<div class="lg-cell"${wide?' style="grid-column:1/3"':''}><b>${esc(label)}</b>${esc(value)||'&nbsp;'}</div>`;
export type LodgeLayout={title:string;body:string;css:string;/** Label / value pairs in print order (golden and server parity). */pairs:[string,string][]};

/** Guest registration card of one stay (booking id or public number). Needs the check-in desk right (PERM-STY-001) or the operations reports right. */
export function registrationCardLayout(s:State,uid:string,bookingId:string):LodgeLayout{
  if(!rptHolds(s,uid,'PERM-STY-001')&&!rptHolds(s,uid,'PERM-RPT-001'))throw new HccError('FORBIDDEN','You cannot print registration cards.');
  const b:Booking|undefined=s.bookings.find(x=>x.id===bookingId||x.publicNo===bookingId);
  if(!b)throw new HccError('NOT_FOUND','No such booking.');
  const g=guestOfBooking(s,b);
  const pairs:[string,string][]=[['Registration no',bookingNo(b)],['Room',b.room],['Arrival',b.arrival],['Departure',b.departure],['Nights',String(nights(b.arrival,b.departure))],['Check-in',checkInAtOf(s,b)],
    ['Guest',guestNameOf(s,b)],['Nationality',g?.nationality??''],['City',cityOfAddress(g?.address)],['Phone',g?.phone??b.phone??''],['ID type',g?.idType??''],['ID number',idLast4Of(g)?maskValue('idNumber',idLast4Of(g)):''],
    ['Adults',String(b.people)],['Children',String(b.children??0)],['Pax',String(paxOf(b))],['Purpose of visit',deskPurposeLabel(s,b.purpose)],['Vehicle no',b.vehicle?.no??''],
    ['Coming from',b.formC?.arrivedFrom??''],['Going to',b.formC?.nextDestination??''],['Check-out',checkOutAtOf(s,b)],['Address',g?.address??'']];
  const body=head(s.settings.hotel,'Guest registration card',bookingNo(b))+`<div class="lg-grid">${pairs.map(([k,v])=>cell(k,v,k==='Address')).join('')}</div>`+
    `<div class="lg-sign"><div>Guest signature</div><div>Reception</div></div><p class="lg-note">I agree to the house rules and to settle my account at check-out. The ID number is masked on this card; no ID images are kept on it.</p>`;
  return {title:`Registration card ${bookingNo(b)}`,body,css:LODGE_CSS,pairs};
}

/** Day-end sheet of a business date: A movements always, B/C money only with PERM-RPT-002 (rule M). */
export function dayEndLayout(s:State,uid:string,date:string):LodgeLayout{
  if(!rptHolds(s,uid,'PERM-RPT-001'))throw new HccError('FORBIDDEN','You cannot print the day-end sheet.');
  const arr=s.bookings.filter(b=>b.arrival===date&&!['Cancelled','Expired','No show'].includes(b.status)).length,dep=s.bookings.filter(b=>b.departure===date&&['In house','Departed'].includes(b.status)).length;
  const inHouse=s.bookings.filter(b=>b.arrival<=date&&b.departure>date&&['In house','Departed'].includes(b.status)).length;
  const pairs:[string,string][]=[['Business date',date],['Arrivals',String(arr)],['Departures',String(dep)],['Rooms in house tonight',String(inHouse)]];
  if(rptHolds(s,uid,'PERM-RPT-002')){
    const p=reportPack(s,date,date),v=(tab:string,id:string)=>p.tabs.find(t=>t.id===tab)?.metrics.find(m=>m.id===id)?.value??0;
    pairs.push(['Net revenue',money(v('revenue','net'))],['GST',money(v('revenue','tax'))],['Receipts (net)',money(v('receipts','net'))],['Expenses',money(v('expenses','total'))]);
  }
  const body=head(s.settings.hotel,'Day end report',date)+`<div class="lg-sec">A · Movements</div><div class="lg-grid">${pairs.slice(0,4).map(([k,x])=>cell(k,x)).join('')}</div>`+
    (pairs.length>4?`<div class="lg-sec">B · Money</div><div class="lg-grid">${pairs.slice(4).map(([k,x])=>cell(k,x)).join('')}</div>`:'')+
    `<div class="lg-sign"><div>Prepared by</div><div>Checked by</div></div><p class="lg-note">Figures as at ${esc(s.clock)} (business clock). Same figures as the Closing report and the Arrivals and departures register.</p>`;
  return {title:`Day end report ${date}`,body,css:LODGE_CSS,pairs};
}
