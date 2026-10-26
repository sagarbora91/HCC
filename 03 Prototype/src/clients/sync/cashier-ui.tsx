// Production-only cashier pieces. Imported only from `process.env.HCC_BUILD==='production'` branches of the Cashier screens.
// (Wave B1: the "Open cashier shift" control moved to features/closing/CashOut.tsx OpenShiftForm — both builds, with the DEC-078 handover count.)
import React,{useEffect,useState} from 'react';
import {useHcc} from '../../app/context';
import {cxSlice} from '../../services/mock';
import {useSync} from './react';
import {getSyncRuntime} from './instance';
import {errorText} from '../errors';
import {cashBlockedReason} from './money';
import {serverId} from './map';

/** APK12-3 (production only): why this user cannot take a CASH tender now (hint text), or undefined. DEC-078: the desk shift's opener is on the
 *  wire, so own-shift (S) users are blocked exactly when the open shift is not theirs. The prototype build never blocks cash here. */
function useCashBlockProd():string|undefined{const {s}=useHcc();const snap=useSync();return cashBlockedReason(snap.me,s.shiftOpen,cxSlice(s).opener?.by);}
export const useCashBlock:()=>string|undefined=process.env.HCC_BUILD==='production'?useCashBlockProd:()=>undefined;

/** GET /cashier/transfers/{id}/slip (BankingSlipView, Hcc.Contracts Billing). */
type SrvSlip={transferId:number;contentType:string;imageBase64:string};
/** Wave B1 (DEC-073): the deposit-slip photo, fetched on demand (PERM-DRW-003: Manager, Owner, Accountant) and held only in this component. */
export function SlipPhotoProd({id}:{id:string}){
  const [v,setV]=useState<{src?:string;error?:string}>({});
  useEffect(()=>{let live=true;
    getSyncRuntime().transport.get<SrvSlip>(`/cashier/transfers/${serverId(id,'Bank deposit')}/slip`)
      .then(r=>{if(live)setV({src:`data:${r.contentType};base64,${r.imageBase64}`});},e=>{if(live)setV({error:errorText(e)});});
    return ()=>{live=false;};},[id]);
  if(v.error)return <p className="form-error" role="alert">{v.error}</p>;
  if(!v.src)return <p className="muted" role="status">Loading the slip photo…</p>;
  return <img src={v.src} alt="Deposit slip photo" style={{maxWidth:'100%',maxHeight:360,borderRadius:8}}/>;
}
