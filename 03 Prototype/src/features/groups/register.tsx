// Module 14 · Group & corporate — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import {Building} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {GroupsPage} from './Groups';
import {GroupsMobile,BlockDetail} from './Mobile';
import {CompanyModal,LimitModal,BlockModal,AllotModal,ReleaseModal,CancelBlockModal,RoomingModal,RoomingMobile,PrintModal} from './modals';
export const registration:FeatureRegistration={
  pages:{'groups':GroupsPage},
  nav:[
    {group:'REVENUE & SALES',item:{id:'groups',label:'Groups & corporate',icon:Building,p:'reservations'}}
  ],
  modals:{
    'grp-company':CompanyModal,   // id = company ID (omitted = new)
    'grp-limit':LimitModal,       // id = company ID
    'grp-block':BlockModal,       // new block
    'grp-allot':AllotModal,       // id = block ID
    'grp-release':ReleaseModal,   // id = block ID
    'grp-cancel':CancelBlockModal,// id = block ID
    'grp-rooming':RoomingModal,   // id = block ID
    'grp-detail':BlockDetail,     // id = block ID
    'grp-print':PrintModal,       // id = 'pickup' | 'folio:<block ID>'
  },
  wideModals:['grp-rooming','grp-print','grp-block','grp-detail'],
  mobile:{pages:{'groups':GroupsMobile},modals:{'grp-rooming':RoomingMobile}},
};
