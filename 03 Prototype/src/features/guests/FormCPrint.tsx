// LF-DESK · Form C print sheet (A5, Bureau of Immigration layout: hotel, guest, passport, visa, arrival, address, purpose, signature) and the
// Foreigner register tab of Customers. Printing uses printDocument (native hook); the visa number is masked for roles without the ID-details right.
import React,{useMemo,useState} from 'react';
import {Printer,Download,Hotel,FileText} from 'lucide-react';
import {printDocument,saveCsv,saveAndReport} from '../../app/native';
import {useHcc} from '../../app/context';
import {guestById,guestView,guestAccess,deskFormCView,deskForeignerRegister,deskForeignerCsvRows,DESK_FORMC_CSV_COLUMNS,deskPurposeLabel,addDays,plural,nights} from '../../services/mock';
import {csvMinimal} from '../../domain/csv';
import {Empty,Pill,shortDate,dateLabel} from '../../components/ui';
import {ScrollPanel,FitNote} from '../../components/fit';
import {bookingRef} from '../../domain/bookingRef';

/** Modal 'form-c-print': id = booking ID. */
export function FormCPrintModal({id}:{id?:string}){
  const {s,uid,booking,open}=useHcc();const b=s.bookings.find(x=>x.id===id);
  if(!b)return <Empty title="This record is unavailable"/>;
  const g=guestById(s,b.guestId),v=g?guestView(s,uid,g):undefined,acc=guestAccess(s,uid);
  if(!acc.idDetails)return <Empty title="Not permitted" body="The Form C shows ID details: it needs the ID-details right (Owner, Manager, Reception)."/>;
  if(!b.formC)return <><Empty title="No Form C on this stay" body="Foreign guests need a Form C at check-in. Enter it in the check-in step “Stay details”."/>
    <div className="modal-actions"><button className="btn" onClick={()=>open({type:'guest-checkin',id:b.id})}>Open check-in</button></div></>;
  const f=deskFormCView(s,uid,b.formC);
  const row=(k:string,val:React.ReactNode)=><tr><th scope="row">{k}</th><td>{val||'—'}</td></tr>;
  return <><div className="invoice ld-sheet gs-regcard" data-watermark="Demonstration — fictional">
    <div className="invoice-top"><Hotel size={34}/><div><h2>{s.settings.hotel}</h2><p>Form C · Arrival report of a foreigner (hotel / accommodation provider)</p></div></div>
    <div className="invoice-stamp">DEMONSTRATION — FICTIONAL · NOT A SUBMITTED FORM C</div>
    <h3 className="gs-sub">Guest</h3>
    <table><tbody>{row('Name',v?.name||b.guest)}{row('Sex',f.sex==='M'?'Male':f.sex==='F'?'Female':'Other')}{row('Date of birth',g?.dob?dateLabel(g.dob):'')}{row('Nationality',v?.nationality)}
      {row('Permanent address abroad',f.addressAbroad)}{row('Contact abroad',f.contactAbroad)}</tbody></table>
    <h3 className="gs-sub">Passport and visa</h3>
    <table><tbody>{row('Passport number',v?.idMasked)}{row('Passport place / date of issue',`${f.passportPlace} · ${dateLabel(f.passportIssued)}`)}{row('Passport valid until',dateLabel(f.passportExpiry))}
      {row('Visa number / type',`${f.visaNo} · ${f.visaType}`)}{row('Visa place / date of issue',`${f.visaPlace} · ${dateLabel(f.visaIssued)}`)}{row('Visa valid until',dateLabel(f.visaExpiry))}</tbody></table>
    <h3 className="gs-sub">Arrival and stay</h3>
    <table><tbody>{row('Arrived from',f.arrivedFrom)}{row('Date of arrival in India',dateLabel(f.arrivalInIndia))}{row('Arrival at this hotel',dateLabel(b.arrival)+' · room '+b.room)}
      {row('Intended stay in India',plural(f.intendedStayDays,'day'))}{row('Departure from this hotel',dateLabel(b.departure)+' ('+plural(nights(b.arrival,b.departure),'night')+')')}
      {row('Next destination',f.nextDestination)}{row('Purpose of visit',deskPurposeLabel(s,b.purpose))}
      {row('Employed in India',f.employedInIndia?'Yes · '+(f.employerOrBusiness??''):'No')}</tbody></table>
    <div className="ld-sign"><div>Signature of the guest</div><div>Signature of the hotel</div></div>
    <p className="footnote">All names and numbers are fictional. The visa number is shown in full only to roles with the ID-details right; this sheet is not stored as a document. {bookingRef(b)}</p></div>
    <div className="modal-actions no-print"><button className="btn" onClick={()=>booking(b.id)}>Back to booking</button><button className="btn primary" onClick={()=>{void printDocument();}}><Printer size={16}/> Print / save PDF</button></div></>;
}

/** The Foreigner register tab (Customers › Foreigner register): every foreign stay in a date range with its Form C. */
export function ForeignerRegisterPanel(){
  const {s,uid,open,toast}=useHcc();
  const [from,setFrom]=useState(addDays(s.date,-30)),[to,setTo]=useState(s.date);
  const rows=useMemo(()=>deskForeignerRegister(s,uid,from,to),[s.bookings,s.guests,from,to,uid]); // eslint-disable-line react-hooks/exhaustive-deps
  const csv=()=>{const lines=[DESK_FORMC_CSV_COLUMNS,...deskForeignerCsvRows(rows)].map(r=>r.map(csvMinimal).join(','));
    void saveAndReport(saveCsv(`foreigner-register-${from}_${to}.csv`,lines.join('\r\n')),toast,`Foreigner register saved · ${rows.length} stay${rows.length===1?'':'s'}`,'the foreigner register');};
  return <ScrollPanel title="Foreigner register" label="Foreigner register" actions={<span className="inline ld-range">
      <label className="inline">From <input type="date" value={from} max={to} onChange={e=>setFrom(e.target.value)}/></label>
      <label className="inline">To <input type="date" value={to} min={from} onChange={e=>setTo(e.target.value)}/></label>
      <button className="btn" disabled={!rows.length} onClick={csv}><Download size={15}/> CSV</button></span>}
    footer={<FitNote text="Form C details per stay. Visa numbers are masked unless you hold the ID-details right. The portal upload file comes with the compliance module; this CSV follows the Form C columns. All records are fictional."/>}>
    {rows.length?<div className="table-wrap"><table><thead><tr><th>Guest / booking</th><th>Nationality</th><th>Stay</th><th>Passport</th><th>Visa</th><th>Form C</th><th></th></tr></thead><tbody>
      {rows.map(r=><tr key={r.booking}><td><strong>{r.guest}</strong><small>{r.booking} · room {r.room}</small></td><td>{r.nationality}</td>
        <td>{shortDate(r.arrival)} — {shortDate(r.departure)}<small>{r.status}</small></td><td>{r.passportNo||'—'}{r.formC&&<small>{r.formC.passportPlace} · to {shortDate(r.formC.passportExpiry)}</small>}</td>
        <td>{r.formC?<>{r.formC.visaNo}<small>{r.formC.visaType} · to {shortDate(r.formC.visaExpiry)}</small></>:'—'}</td>
        <td>{r.formC?<Pill tone="green">Recorded</Pill>:<Pill tone="orange">Missing</Pill>}</td>
        <td>{r.formC?<button className="text-btn" onClick={()=>open({type:'form-c-print',id:r.booking})}><FileText size={13}/> Form C</button>
          :r.status==='Confirmed'||r.status==='In house'?<button className="text-btn" onClick={()=>open({type:'guest-checkin',id:r.booking})}>Add</button>:null}</td></tr>)}
    </tbody></table></div>:<p className="muted padded">No foreign guests in this period. Stays of guests whose nationality is not Indian appear here, with their Form C.</p>}
  </ScrollPanel>;
}
