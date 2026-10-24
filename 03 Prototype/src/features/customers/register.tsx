// Customers (Customer 360) — UI registration. The old "Guests" page now lives here as the Profile tab.
import {Contact} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {CustomersPage} from './Customers';

export const registration:FeatureRegistration={
  pages:{customers:CustomersPage},
  // 'billing' covers Owner/Manager/Reception/Cashier (same as the former Guests page); phone numbers are masked for cashiers.
  nav:[{group:'GUESTS & STAYS',item:{id:'customers',label:'Customers',icon:Contact,p:'billing'}}],
};
