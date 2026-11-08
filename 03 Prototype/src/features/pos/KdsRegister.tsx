// F8 QW-KDS · registration of the kitchen display page ('kds'). Merged by app/registry.ts inside the F8-QW-KDS block.
import {ChefHat} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {KdsPage} from './KdsPage';
export const registration:FeatureRegistration={
  pages:{'kds':KdsPage},
  nav:[{group:'CASH & BILLING',after:'pos',item:{id:'kds',label:'Kitchen display',icon:ChefHat,p:'billing'}}],
};
