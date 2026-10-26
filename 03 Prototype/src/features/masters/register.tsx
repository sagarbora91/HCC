// MA-0 · Masters UI registration: modal 'mst-row' (one row's form; desktop dialog, phone sheet). No page of its own — masters
// live inside Master configuration (page 'settings', "Lists" block of each group) and module pages link there with MstLink.
import type {FeatureRegistration} from '../../app/registry';
import {MasterSheet} from '../../mobile/masters/MasterSheet';
import {MstImportSheet,MstPasteSheet} from './MastersIo'; // MA-8

export const registration:FeatureRegistration={
  modals:{'mst-row':MasterSheet,'mst-import':MstImportSheet,'mst-paste':MstPasteSheet},
  mobile:{modals:{'mst-row':MasterSheet,'mst-import':MstImportSheet,'mst-paste':MstPasteSheet}},
};
