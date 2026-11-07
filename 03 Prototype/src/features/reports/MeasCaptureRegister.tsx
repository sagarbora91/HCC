// F12 MEAS · f12-int: one registration for the measurement captures that had no host (MEAS-CAP-HK / CAP-PEOPLE / FORECAST DONE "Integration needed").
// Dialogs open from the search palette quick actions (navmap SEARCH_ACTIONS, filtered by measOn) and from the pages that host them; every
// dialog and command refuses MEAS_OFF on its own, so a switched-off capture never writes. The three revenue pages are FitPages in REVENUE & SALES.
import React from 'react';
import {CalendarDays,Binoculars,Scale} from 'lucide-react';
import type {FeatureRegistration,ModalProps} from '../../app/registry';
import {MeterReadingModal,MetersModal,MeterAssetLifeModal} from '../maintenance/MetersModal';
import {InspectOfflineModal,InspectDiscardModal} from '../housekeeping/InspectOffline';
import {MeasTrainingModal,MeasExitModal} from '../staff/Training';
import {MeasPulseModal} from '../staff/Pulse';
import {MeasEventsPage} from '../yield/EventsPage';
import {MeasRateShopPage} from '../yield/RateShopPage';
import {MeasDisplacementPage} from '../yield/DisplacementPage';

const Reading=({id}:ModalProps)=><MeterReadingModal meter={id}/>;
const AssetLife=({id}:ModalProps)=><MeterAssetLifeModal asset={id}/>;
const Offline=({id,room}:ModalProps)=><InspectOfflineModal room={room??id}/>;
const Training=({id}:ModalProps)=><MeasTrainingModal id={id}/>;
const Exit=({id}:ModalProps)=><MeasExitModal id={id}/>;
const modals:Record<string,React.ComponentType<ModalProps>>={
  'meas-reading':Reading,'meas-meters':MetersModal,'meas-assetlife':AssetLife,'meas-offline':Offline,'meas-discard':InspectDiscardModal,
  'meas-training':Training,'meas-exit':Exit,'meas-pulse':MeasPulseModal,
};
const pages={'meas-events':MeasEventsPage,'meas-rateshop':MeasRateShopPage,'meas-displacement':MeasDisplacementPage};

export const registration:FeatureRegistration={
  pages,
  nav:[
    {group:'REVENUE & SALES',item:{id:'meas-events',label:'Local events',icon:CalendarDays,p:'approve'},after:'yield'},
    {group:'REVENUE & SALES',item:{id:'meas-rateshop',label:'Rate shop',icon:Binoculars,p:'approve'},after:'meas-events'},
    {group:'REVENUE & SALES',item:{id:'meas-displacement',label:'Displacement',icon:Scale,p:'approve'},after:'meas-rateshop'},
  ],
  modals,
  mobile:{pages,modals},
};
