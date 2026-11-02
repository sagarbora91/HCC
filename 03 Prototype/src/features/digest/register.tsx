// F4-T2 · UI registration: Owner digest page and the revenue-by-category report page.
import {Newspaper,PieChart} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {OwnerDigest} from './OwnerDigest';
import {RevenueByCategory} from '../reports/RevenueByCategory';
export const registration:FeatureRegistration={
  pages:{'owner-digest':OwnerDigest,'revenue-category':RevenueByCategory},
  nav:[{group:'CLOSING & REPORTS',item:{id:'owner-digest',label:'Owner digest',icon:Newspaper,p:'admin'},after:'reports'},
       {group:'CLOSING & REPORTS',item:{id:'revenue-category',label:'Revenue by category',icon:PieChart,p:'reports'},after:'reports'}],
};
