// F12-CAP-GUEST · registration of the guest-capture dialogs. NOT yet merged into app/registry.ts (outside this lane's row): see DONE "Integration needed".
import React from 'react';
import type {FeatureRegistration} from '../../app/registry';
import {MeasSurveyNew,MeasSurveyQr} from './SurveyForm';
import {MeasLoyaltyTag} from '../customers/Loyalty';

export const registration:FeatureRegistration={
  modals:{'meas-survey-new':MeasSurveyNew,'meas-survey-qr':MeasSurveyQr,'meas-loyalty':MeasLoyaltyTag},
};
