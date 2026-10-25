import React from 'react';
import {X,AlertCircle} from 'lucide-react';
import {useHcc} from './context';
import {Empty,trapTab} from '../components/ui';
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
  // WCAG 2.4.3: remember what had focus when the dialog opened (captured during render, before the dialog steals focus)
  // and give focus back when it closes, however it closes (Escape, button, back, successful submit).
  const opener=React.useRef<HTMLElement|null>(null);
  const dlg=React.useRef<HTMLDivElement>(null);
  const isOpen=!!modal;
  if(isOpen&&!opener.current){const a=typeof document!=='undefined'?document.activeElement as HTMLElement|null:null;if(a&&a!==document.body)opener.current=a;}
  React.useEffect(()=>{if(!isOpen&&opener.current){const el=opener.current;opener.current=null;if(el.isConnected&&typeof el.focus==='function')el.focus();}},[isOpen]);
  // Focus moves into the dialog: first form field, else the first action button, else the dialog itself. Name = dialog heading.
  const key=modal?modal.type+'|'+(modal.id||'')+'|'+(modal.room||''):'';
  React.useEffect(()=>{const d=dlg.current;if(!d)return;
    const h=d.querySelector<HTMLElement>('h1,h2,h3');
    if(h){if(!h.id)h.id='hcc-dialog-title';d.setAttribute('aria-labelledby',h.id);d.removeAttribute('aria-label');}
    const t=d.querySelector<HTMLElement>('form input:not([type="hidden"]):not([disabled]),form select:not([disabled]),form textarea:not([disabled])')
      ||d.querySelector<HTMLElement>('input:not([type="hidden"]):not([disabled]),select:not([disabled]),textarea:not([disabled])');
    (display.isMobile?d:(t||d)).focus(); // phone sheets: no on-screen keyboard pops up on open
  },[key]);
  // PB-1: Escape closes the top dialog from anywhere (focus may be on BODY after the search palette closed over it). Handlers that already
  // consumed the key (the dialog itself, palette, drawer, nested confirms) call preventDefault, so this only catches the stray case.
  const closeRef=React.useRef(closeModal);closeRef.current=closeModal;
  React.useEffect(()=>{if(!isOpen)return;
    const onKey=(e:KeyboardEvent)=>{if(e.key==='Escape'&&!e.defaultPrevented){e.preventDefault();closeRef.current();}}; // closeModal keeps the unsaved-changes guard
    document.addEventListener('keydown',onKey);return()=>document.removeEventListener('keydown',onKey);},[isOpen]);
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
    if(e.key==='Escape'){e.preventDefault();closeModal();return;} // closeModal keeps the unsaved-changes guard
    trapTab(e,e.currentTarget);
  };
  const sheet=display.isMobile;
  // Any typing inside a form marks the sheet as dirty; back/close then asks before discarding (AC-M03).
  const onInput=(e:React.FormEvent<HTMLDivElement>)=>{if((e.target as HTMLElement).closest('form'))setDirty(true);};
  return <div className={'modal-overlay'+(sheet?' m-sheet-overlay'+(display.framed?' framed':''):'')} onMouseDown={e=>{if(!sheet&&e.target===e.currentTarget)closeModal();}}>
    <div className={'modal '+(wide.includes(modal.type)?'wide':'')+(sheet?' m-sheet':'')} ref={dlg} tabIndex={-1} role="dialog" aria-modal="true" aria-label={modal.type==='booking'?s.bookings.find(b=>b.id===modal.id)?.guest:'Hotel Control Centre dialog'} onKeyDown={trap} onInput={onInput} onSubmitCapture={()=>setDirty(false)}>
      {sheet?<div className="m-sheet-head"><button className="m-icon" aria-label="Close" onClick={closeModal}><X size={22}/></button></div>
      :<button className="modal-close icon-btn" aria-label="Close dialog" onClick={closeModal}><X size={21}/></button>}
      {body()}
      {error&&<div className="form-error" role="alert"><AlertCircle size={17}/>{error}</div>}
    </div></div>;
}
