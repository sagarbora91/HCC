// Stream B · Guests & check-in — UI registration. Pages/modals registered here are merged by app/registry.ts.
// A page or modal with an existing ID replaces the built-in one (only replace IDs your stream owns).
import React from 'react';
import {DoorOpen,BedDouble,Users} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import type {Booking} from '../../services/mock';
import {useHcc} from '../../app/context';
import {GuestsPage,GuestProfileModal,RegistrationCard} from './GuestProfile';
import {ArrivalsPage,CheckinModal,WalkinModal} from './CheckIn';
import {InHousePage,StayDetailModal,StayMoveModal} from './InHouse';
import {FrontDeskListModal} from './FrontDeskList';
import {FormCPrintModal} from './FormCPrint'; // LF-DESK

/** Booking dialog buttons: guided check-in, guest profile, stay details, registration card. */
function BookingActions({b}:{b:Booking}){
  const {open,permission,busy}=useHcc();
  return <>
    {b.status==='Confirmed'&&permission('checkin')&&<button className="btn" disabled={busy} onClick={()=>open({type:'guest-checkin',id:b.id})}>Guided check-in</button>}
    {b.guestId?<button className="btn subtle" onClick={()=>open({type:'guest-profile',id:b.guestId})}>Guest profile</button>
      :permission('checkin')&&['Confirmed','In house'].includes(b.status)&&<button className="btn subtle" onClick={()=>open({type:'guest-profile',id:'new',room:b.id})}>Create guest profile</button>}
    {['In house','Departed'].includes(b.status)&&<button className="btn subtle" onClick={()=>open({type:'stay-detail',id:b.id})}>Stay details</button>}
    {b.registered&&<button className="btn subtle" onClick={()=>open({type:'guest-regcard',id:b.id})}>Registration card</button>}
    {b.formC&&<button className="btn subtle" onClick={()=>open({type:'form-c-print',id:b.id})}>Form C</button>}{/* LF-DESK */}
  </>;
}

export const registration:FeatureRegistration={
  // Intended PERM IDs: arrivals PERM-STY-001 stay.checkIn; guests/in-house PERM-GST-001 guest.view (coarse 'billing' covers OWN/MGR/REC/CSH; cashier sees masked fields).
  pages:{arrivals:ArrivalsPage,guests:GuestsPage,inhouse:InHousePage},
  nav:[
    {group:'GUESTS & STAYS',item:{id:'arrivals',label:'Arrivals & check-in',icon:DoorOpen,p:'checkin'}},
    {group:'GUESTS & STAYS',item:{id:'inhouse',label:'In-house stays',icon:BedDouble,p:'billing'}},
    // FIT (owner decision, review): the front-desk guest list is linked in the Front desk group with Reservations' permission (Housekeeping / Maintenance never get it, DEC-058/061).
    {group:'FRONT DESK',item:{id:'guests',label:'Guests',icon:Users,p:'reservations'},after:'reservations'},
  ],
  modals:{'guest-profile':GuestProfileModal,'guest-regcard':RegistrationCard,'guest-checkin':CheckinModal,'guest-walkin':WalkinModal,'stay-detail':StayDetailModal,'stay-move':StayMoveModal,'front-list':FrontDeskListModal,'form-c-print':FormCPrintModal}, // front-list: printable in-house / arrivals list (DEC-081)
  wideModals:['guest-profile','guest-regcard','guest-checkin','guest-walkin','stay-detail','stay-move','front-list','form-c-print'],
  bookingActions:BookingActions,
};
