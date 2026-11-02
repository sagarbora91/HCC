// LF-DESK · the front-desk fields of a stay (contract §8): purpose of visit (A3), vehicle (A4), check-out basis (A6) and, for a foreign guest, Form C (A5).
// Used by the check-in step 6 (desktop modal and phone sheet); the payload goes on the CHECKIN command (desk:true applies the Owner's "required" settings).
import React,{useState} from 'react';
import {useHcc} from '../../app/context';
import {Pill} from '../../components/ui';
import {mstRows,deskBasis,deskDefaultBasis,deskFormCNeeded,deskFormCValidate,deskIsForeign,deskPurposeRequired,deskVehicleNormalise,guestForBooking,stayDueOut,
  stayEarlyCheckinWarning,deskEarlyGraceHours,type Booking} from '../../services/mock';
import type {FormC} from '../../domain/types';
import {Info} from 'lucide-react';

const blankFormC=(b:Booking,fc?:FormC):FormC=>fc??{passportPlace:'',passportIssued:'',passportExpiry:'',visaNo:'',visaType:'Tourist',visaPlace:'',visaIssued:'',visaExpiry:'',
  arrivedFrom:'',arrivalInIndia:b.arrival,intendedStayDays:1,nextDestination:'',employedInIndia:false,addressAbroad:'',sex:'M'};

export type DeskDraft={purpose:string;vehicleNo:string;vehicleType:string;basis:'fixed'|'24h';formC:FormC};
export function useDeskFields(b:Booking|undefined){
  const {s}=useHcc();
  const [d,setD]=useState<DeskDraft>(()=>({purpose:b?.purpose??'',vehicleNo:b?.vehicle?.no??'',vehicleType:b?.vehicle?.type??'',basis:b?deskBasis(s,b):deskDefaultBasis(s),formC:blankFormC(b!,b?.formC)}));
  const g=b?guestForBooking(s,b):undefined;
  const foreign=!!b&&deskFormCNeeded(s,b);
  let problem:string|undefined;
  if(b){
    if(deskPurposeRequired(s)&&!d.purpose)problem='Choose the purpose of visit.';
    else if(d.vehicleNo.trim()){const v=deskVehicleNormalise(d.vehicleNo);if(!v.ok)problem=v.message;}
    if(!problem&&foreign)problem=deskFormCValidate(s,b,g,d.formC);
  }
  const payload=()=>({desk:true,...(d.purpose?{purpose:d.purpose}:{}),vehicle:{no:d.vehicleNo,type:d.vehicleType},checkoutBasis:d.basis,...(foreign||deskIsForeign(g)?{formC:{...d.formC,intendedStayDays:Number(d.formC.intendedStayDays)}}:{})});
  return {d,setD,problem,foreign,payload,node:b?<DeskFieldsForm b={b} d={d} setD={setD} problem={problem} foreign={foreign}/>:null};
}

function DeskFieldsForm({b,d,setD,problem,foreign}:{b:Booking;d:DeskDraft;setD:React.Dispatch<React.SetStateAction<DeskDraft>>;problem?:string;foreign:boolean}){
  const {s,uid}=useHcc();
  const purposes=mstRows(s,'purposes').filter(r=>r.active||r.code===d.purpose),types=mstRows(s,'vehicleTypes').filter(r=>r.active||r.name===d.vehicleType);
  const set=(p:Partial<DeskDraft>)=>setD(x=>({...x,...p}));
  const fc=d.formC,setFc=(p:Partial<FormC>)=>setD(x=>({...x,formC:{...x.formC,...p}}));
  const warn=stayEarlyCheckinWarning(s,b);
  const txt=(k:keyof FormC,label:string,type='text')=><label>{label}<input type={type} value={String(fc[k]??'')} onChange={e=>setFc({[k]:e.target.value} as Partial<FormC>)}/></label>;
  const due=stayDueOut({...s,clock:s.clock},{...b,checkoutBasis:d.basis,checkedInAt:b.checkedInAt??s.clock}).replace('T',' ');
  void uid;
  return <section className="ld-fields" aria-label="Stay details">
    <h3 className="gs-sub">Stay details</h3>
    <div className="form-row">
      <label>Purpose of visit{deskPurposeRequired(s)?' *':''}<select value={d.purpose} onChange={e=>set({purpose:e.target.value})}><option value="">Choose…</option>
        {purposes.map(r=><option key={r.code} value={r.code}>{r.name}</option>)}</select></label>
      <label>Check-out basis<select value={d.basis} onChange={e=>set({basis:e.target.value as 'fixed'|'24h'})}><option value="fixed">Fixed time ({s.settings.checkout} on departure)</option><option value="24h">24 hours from check-in</option></select>
        <small>Due out {due}</small></label>
    </div>
    <div className="form-row">
      <label>Vehicle number<input value={d.vehicleNo} placeholder="MH24 AB 1234" autoCapitalize="characters" onChange={e=>set({vehicleNo:e.target.value})}/></label>
      <label>Vehicle type<select value={d.vehicleType} onChange={e=>set({vehicleType:e.target.value})}><option value="">—</option>{types.map(r=><option key={r.code} value={r.name}>{r.name}</option>)}</select></label>
    </div>
    {warn&&<div className="info-note"><Info size={16}/>{warn} (Grace {deskEarlyGraceHours(s)} h.)</div>}
    {foreign&&<fieldset className="ld-formc"><legend>Form C · foreign guest <Pill tone="blue">Required</Pill></legend>
      <p className="muted">The passport number and date of birth come from the guest’s ID details. Dates as yyyy-mm-dd.</p>
      <div className="form-row">{txt('passportPlace','Passport place of issue')}{txt('passportIssued','Passport issue date','date')}{txt('passportExpiry','Passport expiry date','date')}</div>
      <div className="form-row">{txt('visaNo','Visa number')}{txt('visaType','Visa type')}{txt('visaPlace','Visa place of issue')}</div>
      <div className="form-row">{txt('visaIssued','Visa issue date','date')}{txt('visaExpiry','Visa expiry date','date')}{txt('arrivalInIndia','Date of arrival in India','date')}</div>
      <div className="form-row">{txt('arrivedFrom','Arrived from')}{txt('nextDestination','Next destination')}
        <label>Intended stay (days)<input type="number" min={1} value={fc.intendedStayDays} onChange={e=>setFc({intendedStayDays:Number(e.target.value)})}/></label></div>
      <div className="form-row"><label>Sex<select value={fc.sex} onChange={e=>setFc({sex:e.target.value as FormC['sex']})}><option value="M">Male</option><option value="F">Female</option><option value="X">Other</option></select></label>
        <label>Employed in India<select value={fc.employedInIndia?'yes':'no'} onChange={e=>setFc({employedInIndia:e.target.value==='yes'})}><option value="no">No</option><option value="yes">Yes</option></select></label>
        {fc.employedInIndia?txt('employerOrBusiness','Employer / business'):<span/>}</div>
      <div className="form-row">{txt('addressAbroad','Permanent address abroad')}{txt('contactAbroad','Contact abroad (optional)')}</div>
    </fieldset>}
    {problem&&<div className="gs-deny" role="alert">{problem}</div>}
  </section>;
}
