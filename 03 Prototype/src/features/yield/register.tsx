// Module 13 · Advanced tariff & yield — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import {TrendingUp} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {YieldPage} from './YieldPage';
import {YieldMobile} from './YieldMobile';
import {YldNightModal,YldPublishModal,YldEditModal} from './modals';
export const registration:FeatureRegistration={
  pages:{'yield':YieldPage},
  nav:[
    {group:'REVENUE & SALES',item:{id:'yield',label:'Tariff & yield',icon:TrendingUp,p:'reports'}}
  ],
  modals:{
    'yld-night':YldNightModal,     // id = date, room = room type
    'yld-publish':YldPublishModal, // Owner publishes; Manager proposes
    'yld-edit':YldEditModal,       // id = season|occ|restriction|plan|dow|guard, room = record ID (empty = new)
  },
  wideModals:['yld-publish'],
  mobile:{pages:{'yield':YieldMobile},modals:{}},
};
