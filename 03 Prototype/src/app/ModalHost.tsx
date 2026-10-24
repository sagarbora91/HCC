import React from 'react';
import {X,AlertCircle} from 'lucide-react';
import {useHcc} from './context';
import {Empty} from '../components/ui';
import {RoomModal} from '../features/rooms/RoomModal';
import {NewReservation,CancelReservation,MoveStay} from '../features/reservations/modals';
import {BookingModal,PaymentModal,ChargeModal,CheckoutModal,InvoiceModal} from '../features/folio/modals';
import {ExpenseModal} from '../features/expenses/ExpenseModal';
import {CashModal} from '../features/closing/CashModal';
import {ScenarioModal,ResetModal,GuideModal} from './demo/DemoModals';
import {extraModals,extraWide,mobileModals} from './registry';
import {DemoSheet,UserMenuSheet} from './mobile/sheets';
const wide=['booking','invoice','guide','scenario',...extraWide];
export function ModalHost(){
  const {s,modal,closeModal,error,display,setDirty}=useHcc();
  if(!modal)return null;
  const needsBooking=['booking','payment','charge','discount','checkout','move','cancel','invoice'].includes(modal.type);
  const found=!needsBooking||s.bookings.some(b=>b.id===modal.id);
  const body=()=>{
    if(!found)return <Empty title="This record is unavailable"/>;
    const Mob=display.isMobile?mobileModals[modal.type]:undefined;if(Mob)return <Mob id={modal.id} room={modal.room}/>;
    const Extra=extraModals[modal.type];if(Extra)return <Extra id={modal.id} room={modal.room}/>;
    switch(modal.type){
      case 'room':return <RoomModal id={modal.id!}/>;
      case 'new':return <NewReservation room={modal.room}/>;
      case 'cancel':return <CancelReservation id={modal.id!}/>;
      case 'move':return <MoveStay id={modal.id!}/>;
      case 'booking':return <BookingModal id={modal.id!}/>;
      case 'payment':return <PaymentModal id={modal.id!}/>;
      case 'charge':return <ChargeModal id={modal.id!} discount={false}/>;
      case 'discount':return <ChargeModal id={modal.id!} discount/>;
      case 'checkout':return <CheckoutModal id={modal.id!}/>;
      case 'invoice':return <InvoiceModal id={modal.id!}/>;
      case 'expense':return <ExpenseModal/>;
      case 'cash':return <CashModal/>;
      case 'scenario':return <ScenarioModal/>;
      case 'reset':return <ResetModal/>;
      case 'guide':return <GuideModal/>;
      case 'demo':return <DemoSheet/>;
      case 'user-menu':return <UserMenuSheet/>;
      default:return <Empty title="This record is unavailable"/>;
    }
  };
  const trap=(e:React.KeyboardEvent<HTMLDivElement>)=>{
    if(e.key==='Escape')closeModal();
    if(e.key==='Tab'){const els=Array.from(e.currentTarget.querySelectorAll<HTMLElement>('button:not([disabled]),input:not([disabled]),select:not([disabled]),a[href],textarea'));const first=els[0],last=els[els.length-1];
      if(e.shiftKey&&document.activeElement===first){e.preventDefault();last?.focus();}else if(!e.shiftKey&&document.activeElement===last){e.preventDefault();first?.focus();}}
  };
  const sheet=display.isMobile;
  // Any typing inside a form marks the sheet as dirty; back/close then asks before discarding (AC-M03).
  const onInput=(e:React.FormEvent<HTMLDivElement>)=>{if((e.target as HTMLElement).closest('form'))setDirty(true);};
  return <div className={'modal-overlay'+(sheet?' m-sheet-overlay'+(display.framed?' framed':''):'')} onMouseDown={e=>{if(!sheet&&e.target===e.currentTarget)closeModal();}}>
    <div className={'modal '+(wide.includes(modal.type)?'wide':'')+(sheet?' m-sheet':'')} role="dialog" aria-modal="true" aria-label={modal.type==='booking'?s.bookings.find(b=>b.id===modal.id)?.guest:'Hotel Control Centre dialog'} onKeyDown={trap} onInput={onInput} onSubmit={()=>setDirty(false)}>
      {sheet?<div className="m-sheet-head"><button autoFocus className="m-icon" aria-label="Close" onClick={closeModal}><X size={22}/></button></div>
      :<button autoFocus className="modal-close icon-btn" aria-label="Close dialog" onClick={closeModal}><X size={21}/></button>}
      {body()}
      {error&&<div className="form-error" role="alert"><AlertCircle size={17}/>{error}</div>}
    </div></div>;
}
