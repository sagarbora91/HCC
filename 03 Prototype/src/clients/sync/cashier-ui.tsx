// Production "Open cashier shift" control (D4). Imported only from `process.env.HCC_BUILD==='production'` branches of the Cashier screens.
import React from 'react';
import {Wallet} from 'lucide-react';
import {money} from '../../services/mock';
import {useHcc} from '../../app/context';
import {useSync} from './react';
import {cashBlockedReason} from './money';

/** APK12-3 (production only): why this user cannot take a CASH tender now (hint text), or undefined. The prototype build never blocks cash. */
function useCashBlockProd():string|undefined{const {s}=useHcc();const snap=useSync();return cashBlockedReason(snap.me,s.shiftOpen);}
export const useCashBlock:()=>string|undefined=process.env.HCC_BUILD==='production'?useCashBlockProd:()=>undefined;

/** Shown while no shift is open on an open business day: the cashier starts the shift; the opening float is the last counted cash. */
export function OpenShiftPanel({compact}:{compact?:boolean}){
  const {s,run,busy,permission}=useHcc();
  if(!permission('payments'))return <p className="muted padded">Opening the cash shift needs the payments permission (cashier or reception).</p>;
  const float=s.shiftClose?.counted??s.openingCash;
  return <div className={compact?'mm-form':'padded'}>
    <p>No cash shift is open, so cash receipts are closed. Open the shift to start taking cash. The opening float is the last counted cash{float?<> ({money(float)})</>:null}, carried by the server; you do not type it.</p>
    <div className="modal-actions"><button type="button" className={compact?'btn primary full':'btn primary'} disabled={busy}
      onClick={()=>{void run({type:'SHIFT_OPEN'},'Cashier shift opened — cash receipts are accepted');}}><Wallet size={15}/> Open cashier shift</button></div>
  </div>;
}
