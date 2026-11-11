// Customers (Customer 360) — UI registration. The old "Guests" page now lives here as the Profile tab.
import {Contact} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {CustomersPage} from './Customers';
import {MergeMobile} from './MergeMobile'; // FX-MERGE-UI

export const registration:FeatureRegistration={
  pages:{customers:CustomersPage},
  // 'billing' covers Owner/Manager/Reception/Cashier (same as the former Guests page); phone numbers are masked for cashiers.
  mobile:{modals:{'customer-merge':MergeMobile}}, // FX-MERGE-UI: phone sheet opened from the Customers page
  nav:[{group:'GUESTS & STAYS',item:{id:'customers',label:'Customers',icon:Contact,p:'billing'}}],
};
