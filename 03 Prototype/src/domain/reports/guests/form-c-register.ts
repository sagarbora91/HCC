// F7-R3 · rpt.guests.form-c-register — Form C register (foreign guests; catalogue A13, C4.CFormReport). Money flag n. Passport and visa numbers masked in every
// format (rule M of §1.6: a file never holds the full number). "Form C submitted" = the Form C is recorded on the stay (HCC keeps no FRRO submission date yet).
import type {RptColumn,RptRow} from '../types';
import {registerReport} from '../catalogue';
import {deskIsForeign,deskVisaMasked,deskPurposeLabel} from '../../streams/desk-fields';
import {guestOfBooking,guestNameOf,idLast4Of,rangeGuard} from './shared';

const T=(key:string,label:string,mask?:RptColumn['mask']):RptColumn=>({key,label,type:'text',money:false,...(mask?{mask}:{})});
const D=(key:string,label:string):RptColumn=>({key,label,type:'date',money:false});
export const FORMC_COLS:RptColumn[]=[D('checkIn','Check-in'),T('guest','Guest','guestName'),T('nationality','Nationality'),T('passportNo','Passport no','idNumber'),T('passportIssue','Passport issue date and place'),
  T('visa','Visa no and type'),D('visaExpiry','Visa expiry'),D('arrivedOn','Arrived in India on'),T('arrivedFrom','Arrived from'),T('nextDestination','Next destination'),T('purpose','Purpose'),T('room','Room'),
  D('checkOut','Check-out'),{key:'submitted',label:'Form C submitted',type:'bool',money:false},D('submittedOn','Submitted on')];

export const formCRegister=registerReport({
  id:'rpt.guests.form-c-register',title:'Form C register (foreign guests)',area:'guests',priority:'P1',status:'built',
  moneyFlag:false,viewPerms:['PERM-RPT-001'],deskPerms:['PERM-STY-001'],dateClass:'D-B',
  params:[{key:'from',label:'From',type:'date',dateClass:'D-B'},{key:'to',label:'To',type:'date',dateClass:'D-B'},{key:'submitted',label:'Submitted',type:'enum',options:['all','yes','no'],default:'all'}],
  columns:FORMC_COLS,
  dateBasis:'Foreign guests by check-in date (range up to 92 days); submitted = Form C recorded on the stay. Passport and visa numbers are masked in every format.',
  formats:['screen','xlsx','pdf'],phone:'summary',legacyIds:['A13','C4.CFormReport'],
  select:(s,_uid,p)=>{
    rangeGuard(p.from,p.to,92,s.date);
    const want=p.submitted??'all';
    const list=s.bookings.filter(b=>(b.status==='In house'||b.status==='Departed')&&b.arrival>=p.from!&&b.arrival<=p.to!&&(deskIsForeign(guestOfBooking(s,b))||b.formC))
      .filter(b=>want==='all'||(want==='yes')===!!b.formC).sort((a,c)=>a.arrival.localeCompare(c.arrival)||a.room.localeCompare(c.room));
    const todo=list.filter(b=>!b.formC).length;
    const rows:RptRow[]=[{kind:'subtotal',cells:{nationality:`To submit: ${todo}`}}];
    for(const b of list){const g=guestOfBooking(s,b),f=b.formC;
      rows.push({kind:'row',cells:{checkIn:b.arrival,guest:guestNameOf(s,b),nationality:g?.nationality??'',passportNo:idLast4Of(g),passportIssue:f?`${f.passportIssued} · ${f.passportPlace}`:'',
        visa:f?`${deskVisaMasked(f.visaNo)} · ${f.visaType}`:'',visaExpiry:f?.visaExpiry??null,arrivedOn:f?.arrivalInIndia??null,arrivedFrom:f?.arrivedFrom??'',nextDestination:f?.nextDestination??'',
        purpose:deskPurposeLabel(s,b.purpose),room:b.room,checkOut:b.status==='Departed'?b.departure:null,submitted:f?1:0,submittedOn:null}});}
    rows.push({kind:'total',cells:{nationality:`${list.length} foreign stay${list.length===1?'':'s'}`}});
    return {params:p,columns:FORMC_COLS,rows};
  },
});
