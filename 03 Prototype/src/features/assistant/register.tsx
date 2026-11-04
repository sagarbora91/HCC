// Module 26 · AI management assistant — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import {Sparkles} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {AssistantPage,AssistantMobile} from './Assistant';
export const registration:FeatureRegistration={
  // AF-SMALL: demo-only; compiled out of the production build (same pattern as ModalHost).
  pages:process.env.HCC_BUILD==='production'?{}:{'assistant':AssistantPage},
  nav:process.env.HCC_BUILD==='production'?[]:[
    {group:'CLOSING & REPORTS',item:{id:'assistant',label:'Management assistant',icon:Sparkles,p:'reports'}}
  ],
  modals:{},
  mobile:{pages:process.env.HCC_BUILD==='production'?{}:{'assistant':AssistantMobile},modals:{}},
};
