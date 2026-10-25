// DEC-065 · short explanation of locked cash-book days, shown in Day closing / Corrections (desktop and phone) and the Cash book day card.
import React from 'react';
import {Lock} from 'lucide-react';
import {cbLockedDates} from '../../services/mock';
import {useHcc} from '../../app/context';
import {dateLabel} from '../../components/ui';

export function CashLockNote({date}:{date?:string}){
  const {s,go}=useHcc();
  const all=cbLockedDates(s),list=date?all.filter(d=>d===date):all;
  if(!list.length)return null;
  return <div className="info-note cb-lock-note" role="status"><Lock size={16}/><span>
    <strong>{list.length===1?`Cash book ${dateLabel(list[0])} is locked.`:`${list.length} cash book days are locked.`}</strong> Corrections, expense reversals, refunds, deposit returns and bill voids that touch {list.length===1?'that day':'those days'} are blocked until the Owner reopens {list.length===1?'it':'them'} with a reason in the <button type="button" className="text-btn" onClick={()=>go('cashbook')}>Cash book</button>.
    {!date&&list.length>1&&<> Locked: {list.slice(-6).map(dateLabel).join(', ')}{list.length>6?', …':''}.</>}</span></div>;
}
