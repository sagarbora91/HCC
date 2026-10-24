// Module 26 · AI management assistant — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import {Sparkles} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {AssistantPage,AssistantMobile} from './Assistant';
export const registration:FeatureRegistration={
  pages:{'assistant':AssistantPage},
  nav:[
    {group:'CLOSING & REPORTS',item:{id:'assistant',label:'Management assistant',icon:Sparkles,p:'reports'}}
  ],
  modals:{},
  mobile:{pages:{'assistant':AssistantMobile},modals:{}},
};
