// Module 21–22 · OTA channels & direct booking — UI registration (desktop pages, nav, modals, mobile pages). Merged by app/registry.ts.
import {Globe,MousePointerClick} from 'lucide-react';
import type {FeatureRegistration} from '../../app/registry';
import {ChannelsPage} from './Channels';
import {ChannelsMobile} from './ChannelsMobile';
import {BookingSitePage,BookingSiteMobile} from './BookingSite';
import {MessageModal,RejectModal,ChannelModal,CommissionPrintModal,WebDeclineModal} from './modals';
export const registration:FeatureRegistration={
  pages:{'channels':ChannelsPage,'booking-site':BookingSitePage},
  nav:[
    {group:'REVENUE & SALES',item:{id:'channels',label:'OTA channels',icon:Globe,p:'reservations'}},
    {group:'REVENUE & SALES',item:{id:'booking-site',label:'Direct booking site',icon:MousePointerClick,p:'reservations'}}
  ],
  modals:{'chn-message':MessageModal,'chn-reject':RejectModal,'chn-channel':ChannelModal,'chn-commission-print':CommissionPrintModal,'chn-web-decline':WebDeclineModal},
  wideModals:['chn-commission-print'],
  mobile:{pages:{'channels':ChannelsMobile,'booking-site':BookingSiteMobile},modals:{}},
};
