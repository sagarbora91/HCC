// Module 18 · Guest services, complaints, lost & found — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import React from 'react';
import {HeartHandshake,ConciergeBell} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import type {Booking} from '../../services/mock';
import {useHcc} from '../../app/context';
import {GuestServicesPage,GsvMyRequestsPage} from './GuestServices';
import {GuestServicesMobile,GsvMyRequestsMobile} from './Mobile';
import {GsvRequestNew,GsvRequestView,GsvComplaintNew,GsvComplaintView,GsvItemNew,GsvItemView,GsvPrint} from './modals';

/** Booking dialog buttons: log a guest request / complaint for this stay. */
function GsvBookingActions({b}:{b:Booking}){
  const {permission,open}=useHcc();
  if(!permission('reservations'))return null;
  return <>{b.status==='In house'&&<button className="btn" onClick={()=>open({type:'gsv-request-new',id:b.id})}>Guest request</button>}
    {(b.status==='In house'||b.status==='Departed')&&<button className="btn" onClick={()=>open({type:'gsv-complaint-new',id:b.id})}>Log complaint</button>}</>;
}

export const registration:FeatureRegistration={
  pages:{'guest-services':GuestServicesPage,'gsv-my-requests':GsvMyRequestsPage},
  nav:[
    {group:'FRONT DESK',item:{id:'guest-services',label:'Guest services',icon:HeartHandshake,p:'reservations'}},
    // Housekeepers / maintenance / supervisors: their assigned guest requests (no money).
    {group:'OPERATIONS',item:{id:'gsv-my-requests',label:'Guest requests',icon:ConciergeBell,p:'housekeeping'},after:'housekeeping'},
  ],
  modals:{
    'gsv-request-new':GsvRequestNew,   // id = booking ID (optional)
    'gsv-request':GsvRequestView,      // id = request ID
    'gsv-complaint-new':GsvComplaintNew,// id = booking ID (optional)
    'gsv-complaint':GsvComplaintView,  // id = complaint ID
    'gsv-item-new':GsvItemNew,
    'gsv-item':GsvItemView,            // id = item ID
    'gsv-print':GsvPrint,
  },
  wideModals:['gsv-print'],
  bookingActions:GsvBookingActions,
  mobile:{pages:{'guest-services':GuestServicesMobile,'gsv-my-requests':GsvMyRequestsMobile},modals:{}},
};
