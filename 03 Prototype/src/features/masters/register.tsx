// MA-0 · Masters UI registration: modal 'mst-row' (one row's form; desktop dialog, phone sheet); V14-6 'mst-retire' (retire confirmation). No page of its own — masters
// live inside Master configuration (page 'settings', "Lists" block of each group) and module pages link there with MstLink.
import type {FeatureRegistration} from '../../app/registry';
import {MasterSheet} from '../../mobile/masters/MasterSheet';
import {MstImportSheet,MstPasteSheet} from './MastersIo'; // MA-8
import {MstRetireConfirm} from './RetireConfirm'; // V14-6

export const registration:FeatureRegistration={
  modals:{'mst-row':MasterSheet,'mst-import':MstImportSheet,'mst-paste':MstPasteSheet,'mst-retire':MstRetireConfirm},
  mobile:{modals:{'mst-row':MasterSheet,'mst-import':MstImportSheet,'mst-paste':MstPasteSheet,'mst-retire':MstRetireConfirm}},
};
