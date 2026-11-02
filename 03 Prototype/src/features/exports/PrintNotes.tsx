// LF-IMAGES (B6, LF-CONTRACT §10.2): the notes and titles the Owner sets in Master configuration › Property and branding, printed on every document at PRINT time
// (never stored on the document): <PrintTitle/> above the lines, <PrintNotes/> below them. Every print template includes both, one line each:
// folio invoice, counter bill (lf-ui includes it), POS invoice, credit note and receipts. Pure text; blank settings print nothing.
import React from 'react';
import {useHcc} from '../../app/context';
import {printNotesFor,type PrintDoc} from '../../domain/streams/printnotes';
export type {PrintDoc,PrintNotesText} from '../../domain/streams/printnotes';
export {printNotesFor};

/** Title and subtitle above the document's lines. */
export function PrintTitle({doc}:{doc:PrintDoc}){
  const {s}=useHcc();const t=printNotesFor(s,doc);
  if(!t.title&&!t.subtitle)return null;
  return <div className="print-title">{t.title&&<strong>{t.title}</strong>}{t.subtitle&&<span>{t.subtitle}</span>}</div>;
}
/** Notes and footer below the document's lines (line breaks in the notes are kept). */
export function PrintNotes({doc}:{doc:PrintDoc}){
  const {s}=useHcc();const t=printNotesFor(s,doc);
  if(!t.notes&&!t.footer)return null;
  return <div className="print-notes">{t.notes&&<p className="print-notes-text">{t.notes}</p>}{t.footer&&<p className="print-notes-footer">{t.footer}</p>}</div>;
}
