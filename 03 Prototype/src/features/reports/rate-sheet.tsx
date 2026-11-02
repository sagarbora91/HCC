// F4-T1 · rpt.admin.rate-sheet (extended, F4-CONTRACT §1.7): A4 letterhead print of a tariff card version + CSV of the card and of the
// version history. Figures come from trcRateSheet — the same selector as the screen (print = screen). English only. PDF: the hook
// renderPdf?.('rate-sheet', model) is left for F7's renderer.
import React from 'react';
import type {State} from '../../domain/types';
import {money} from '../../domain/money';
import {gstPct} from '../../domain/gst';
import {trcRateSheet,trcSheetCsv,trcHistoryCsv,type TrcSheet} from '../../domain/streams/tariffcard';

/** F7 hook: a PDF renderer may register itself here; until then printing goes through printDocument (browser / Android print). */
export const rateSheetPdf:{renderPdf?:(kind:'rate-sheet',model:TrcSheet)=>Promise<void>}={};
const longDate=(d:string)=>d?new Date(d+'T12:00:00Z').toLocaleDateString('en-IN',{day:'numeric',month:'long',year:'numeric',timeZone:'UTC'}):'';
const groups=(sheet:TrcSheet)=>[...new Set(sheet.rows.map(r=>r.group))];

/** The printable document (pure: takes the model). */
export function RateSheetDoc({sheet}:{sheet:TrcSheet}){
  if(!sheet.version)return <div className="print-doc trc-sheet"><p>No published tariff card yet.</p></div>;
  return <div className="print-doc trc-sheet" data-version={sheet.version.id}>
    <header className="trc-sheet-head"><h2>{sheet.hotel}</h2><p className="trc-sheet-title">Tariff card · {sheet.version.label}</p>
      <p className="trc-sheet-valid">Valid from {longDate(sheet.validFrom)}</p><p className="trc-sheet-gst">{sheet.gstLine}</p></header>
    {groups(sheet).map(g=><section key={g} className="trc-sheet-group"><h3>{g}</h3>
      <table className="table trc-sheet-table"><thead><tr><th>Item</th><th>Nights</th><th className="num">Before GST</th><th className="num">GST</th><th className="num">Price incl. GST</th></tr></thead>
        <tbody>{sheet.rows.filter(r=>r.group===g).map(r=><tr key={r.id} data-row={r.id}><td>{r.title}{r.note?<small> · {r.note}</small>:null}</td><td>{r.daySet}</td>
          <td className="num">{r.excl===null?'—':money(r.excl)}</td><td className="num">{gstPct(r.bps)}</td><td className="num"><strong>{r.incl===null?'On request':money(r.incl)}</strong></td></tr>)}</tbody></table></section>)}
    <footer className="trc-sheet-foot"><p>Weekend nights: {sheet.weekend}. Prices per room per night unless stated; meal plans per guest per night.</p>
      {sheet.terms?<p className="trc-sheet-terms">{sheet.terms}</p>:null}<p className="muted">Card {sheet.version.id} · GST rates as per the GST master on the valid-from date.</p></footer>
  </div>;
}
export const rateSheetModel=(s:State,versionId?:string)=>trcRateSheet(s,versionId);
export const rateSheetCsv=(s:State,versionId?:string)=>trcSheetCsv(trcRateSheet(s,versionId));
export const rateSheetHistoryCsv=(s:State)=>trcHistoryCsv(s);
