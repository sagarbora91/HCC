// F8 QW-ADV · Cashier step "Advances held": which bookings hold an advance or a refundable deposit, how old, which are red (QWQ-5).
// Read-only view. Every action opens an EXISTING dialog: 'refund' (REFUND_REQUEST), 'refund-execute' (REFUND_EXECUTE, never by the requester),
// 'payment' (deposit-return). Nothing is posted from here.
import React from 'react';
import {Download,RotateCcw} from 'lucide-react';
import {money,advHeld,advTotals,advCsv,ADV_RED_DAYS} from '../../services/mock';
import {useHcc} from '../../app/context';
import {Pill,Empty,dateLabel} from '../../components/ui';
import {usePaged,ShowMore} from '../../components/ShowMore';
import {saveCsv} from '../../app/native';

export function AdvancesHeld(){
  const {s,uid,open,booking,permission,busy}=useHcc();
  const rows=advHeld(s,uid),tot=advTotals(rows),pg=usePaged(rows,100);
  const who=(id:string)=>s.users.find(u=>u.id===id)?.name.split(' · ')[0]??id;
  if(!rows.length)return <Empty title="No advances or deposits held" body="Bookings that hold an advance beyond their bill, or a refundable deposit, appear here."/>;
  return <div className="adv-held">
    <div className="padded inline adv-head"><span><strong>{rows.length}</strong> booking{rows.length===1?'':'s'} · <strong>{money(tot.held)}</strong> held</span>
      {tot.red>0&&<Pill tone="red">{tot.red} red</Pill>}
      <span className="muted">Red: cancelled, no-show or departed with money held, or an advance older than {ADV_RED_DAYS} days.</span>
      <button className="btn" aria-label="Download advances held as CSV" onClick={()=>{void saveCsv(`advances-held_${s.date}.csv`,advCsv(rows));}}><Download size={14}/> CSV</button></div>
    <div className="table-wrap"><table><thead><tr><th>Booking</th><th>Status</th><th>Stay</th><th className="number">Advance</th><th className="number">Deposit</th><th className="number">Refunded</th><th className="number">Held</th><th className="number">Age</th><th>Folio</th><th>Actions</th></tr></thead><tbody>
      {pg.shown.map(r=><tr key={r.booking} className={r.red?'adv-red':undefined}>
        <td><button className="text-btn" onClick={()=>booking(r.booking)}>{r.guest} · {r.ref}</button></td>
        <td><Pill tone={r.red?'red':'neutral'}>{r.status}</Pill>{r.red&&<small className="f-why">{r.redReason}</small>}</td>
        <td>{dateLabel(r.arrival)} → {dateLabel(r.departure)}</td>
        <td className="number">{r.advance?money(r.advance):'—'}</td><td className="number">{r.deposit?money(r.deposit):'—'}</td>
        <td className="number">{r.refunded?money(r.refunded):'—'}</td><td className="number"><strong>{money(r.held)}</strong></td>
        <td className="number">{r.ageDays} d</td><td>{r.folios}</td>
        <td>{permission('payments')&&<div className="inline">
          {r.refundable.map(f=><button key={f.receiptId} className="text-btn" disabled={busy} onClick={()=>open({type:'refund',id:r.booking,room:f.receiptId})}><RotateCcw size={13}/> Request refund · {f.receiptId} ({money(f.remaining)})</button>)}
          {r.deposit>0&&<button className="text-btn" disabled={busy} onClick={()=>open({type:'payment',id:r.booking})}>Return deposit</button>}
          {r.pending.map(p=>p.status==='Approved'
            ?(p.requestedBy===uid?<small key={p.id} className="muted">{p.id} approved · another user executes it</small>:<button key={p.id} className="text-btn" disabled={busy} onClick={()=>open({type:'refund-execute',id:p.id})}>Execute refund {p.id} ({money(p.amount)})</button>)
            :<small key={p.id} className="muted">{p.id} · {money(p.amount)} awaiting approval ({who(p.requestedBy)})</small>)}
        </div>}</td></tr>)}
    </tbody></table><ShowMore p={pg}/></div>
  </div>;
}
