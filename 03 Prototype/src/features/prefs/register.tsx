// CP-1 · "Appearance & display" (page id 'prefs'): every role (any permission), category Admin & Setup (navmap.ts PAGE_CATEGORY).
// Desktop page + phone page. Never shows hotel data, so no permission row of its own (plan §4).
import {SlidersHorizontal} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {permissions} from '../../domain/types';
import {PrefsPage} from './PrefsPage';
import {MPrefsPage} from '../../mobile/prefs/PrefsPage';
export const registration:FeatureRegistration={
  pages:{prefs:PrefsPage},
  nav:[{group:'ADMINISTRATION',item:{id:'prefs',label:'Appearance & display',icon:SlidersHorizontal,p:[...permissions]}}],
  mobile:{pages:{prefs:MPrefsPage}},
};
