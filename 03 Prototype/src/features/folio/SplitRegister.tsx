// F8 SF-UI · registration of the split-folio dialogs. 'sf-split' = Split bill (desktop and phone, any display mode).
// The phone payment sheet (MmPayment, not in this lane's files) is wrapped so a payment opened for a folio shows THAT folio's balance and sends
// PAYMENT {folio}: the wrapper hands MmPayment a context whose state shows the stay as sfView(b,folio) and whose run() adds `folio` to PAYMENT.
import React from 'react';
import type {FeatureRegistration} from '../../app/registry';
import {HccContext,useHcc} from '../../app/context';
import {MmPayment} from '../../mobile/money/Payment';
import {SplitBillModal} from './Split';
import {sfFolioOfModal} from './SplitLogic';
import {sfView} from '../../services/mock';

export function SfMmPayment({id}:{id?:string}){
  const c=useHcc();
  const b=c.s.bookings.find(x=>x.id===id),f=sfFolioOfModal(b,c.modal?.room);
  if(!b||!f)return <MmPayment id={id}/>;
  const value={...c,s:{...c.s,bookings:c.s.bookings.map(x=>x.id===b.id?sfView(x,f):x)},run:((cmd,...rest)=>c.run(cmd.type==='PAYMENT'&&!cmd.folio?{...cmd,folio:f}:cmd,...rest)) as typeof c.run};
  return <HccContext.Provider value={value}><MmPayment id={id}/></HccContext.Provider>;
}
export const registration:FeatureRegistration={
  modals:{'sf-split':SplitBillModal},
  mobile:{modals:{payment:p=><SfMmPayment id={p.id}/>}},
};
