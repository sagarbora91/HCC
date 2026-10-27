// DEC-120 · production-only expense pieces: the bill photo is fetched on demand (GET /expenses/{id}/bill-photo — whoever may read the expense)
// and held only in this component, as the deposit-slip photo (cashier-ui.tsx SlipPhotoProd).
import React,{useEffect,useState} from 'react';
import {getSyncRuntime} from './instance';
import {errorText} from '../errors';
import {serverId} from './map';

/** GET /expenses/{id}/bill-photo (ExpenseBillPhotoView, Hcc.Contracts Expenses). */
type SrvBillPhoto={expenseId:number;contentType:string;imageBase64:string};
export function BillPhotoProd({id}:{id:string}){
  const [v,setV]=useState<{src?:string;error?:string}>({});
  useEffect(()=>{let live=true;
    getSyncRuntime().transport.get<SrvBillPhoto>(`/expenses/${serverId(id,'Expense')}/bill-photo`)
      .then(r=>{if(live)setV({src:`data:${r.contentType};base64,${r.imageBase64}`});},e=>{if(live)setV({error:errorText(e)});});
    return ()=>{live=false;};},[id]);
  if(v.error)return <p className="form-error" role="alert">{v.error}</p>;
  if(!v.src)return <p className="muted" role="status">Loading the bill photo…</p>;
  return <img className="exp-photo-full" src={v.src} alt="Bill photo"/>;
}
