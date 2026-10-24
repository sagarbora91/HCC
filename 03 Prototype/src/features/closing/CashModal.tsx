import React from 'react';
import {money,shiftExpected,shiftStatus} from '../../services/mock';
import {useHcc} from '../../app/context';
import {ShiftCountForm} from './ShiftCount';
/** Quick cash count from Day closing. The full shift view (movements, approval) is the 'cashier' page. */
export function CashModal(){
  const {s,closeModal,go}=useHcc();const ex=shiftExpected(s),st=shiftStatus(s);
  return <><div className="modal-heading"><h2>Count the cash drawer</h2><p>Expected cash: <strong>{money(ex.expected)}</strong> = opening float {money(ex.opening)} + cash in {money(ex.payments+ex.deposits)} − cash out {money(ex.refunds+ex.returns+ex.expenses)}.</p></div>
    {st==='Open'?<ShiftCountForm after={closeModal}/>:<p className="padded">{st==='Submitted'?'A count is awaiting difference approval.':'The shift is already closed.'}</p>}
    <div className="modal-actions"><button type="button" className="btn" onClick={closeModal}>Back</button><button type="button" className="btn subtle" onClick={()=>{closeModal();go('cashier');}}>Open cashier shift page</button></div></>;
}
