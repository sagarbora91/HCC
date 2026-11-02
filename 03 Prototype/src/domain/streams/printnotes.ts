// LF-IMAGES (B6, LF-CONTRACT §10.2): which of the Owner's branding texts print on which document. Pure; the print templates render the result
// (features/exports/PrintNotes.tsx) and the production print models mirror the same rule. Blank settings print nothing.
import type {State} from '../types';
import {cfgValueOn} from './config';

export type PrintDoc='invoice'|'counter-bill'|'pos-invoice'|'credit-note'|'receipt';
export type PrintNotesText={title:string;subtitle:string;notes:string;footer:string};

const txt=(s:State,key:string):string=>{const v=cfgValueOn(s,key);return typeof v==='string'?v.trim():'';};
/** The words printed on a document type: title / subtitle / notes on invoices, counter bills and POS tax invoices; the credit-note notes on a credit note;
 *  the receipt footer on a receipt (the invoice footer when the receipt footer is empty); the invoice footer everywhere else. */
export function printNotesFor(s:State,doc:PrintDoc):PrintNotesText{
  const invoiceLike=doc==='invoice'||doc==='counter-bill'||doc==='pos-invoice';
  const footer=doc==='receipt'?(txt(s,'brand.receiptFooter')||txt(s,'brand.invoiceFooter')):txt(s,'brand.invoiceFooter');
  return {title:invoiceLike?txt(s,'brand.invoiceTitle'):'',subtitle:invoiceLike?txt(s,'brand.invoiceSubtitle'):'',
    notes:doc==='credit-note'?txt(s,'brand.creditNoteNotes'):invoiceLike?txt(s,'brand.invoiceNotes'):'',footer};
}
