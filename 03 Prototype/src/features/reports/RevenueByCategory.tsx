// F4-T2 · Revenue by room category and tender: Block A revenue, Block B collections, Block C bridge. Roles: Owner, Accountant, Manager.
import React,{useMemo,useState} from 'react';
import {Download,FileSpreadsheet,Printer} from 'lucide-react';
import {money,rctCanView,rctReport,rctCsv} from '../../services/mock';
import type {RCTBasis,RCTBlockARow,RCTBlockBRow} from '../../domain/types';
import {useHcc} from '../../app/context';
import {Empty} from '../../components/ui';
import {saveCsv,saveBinaryFile,saveAndReport,printDocument} from '../../app/native';
import {XLSX_MIME} from '../../app/xlsx';
import {rctXlsx} from './rctXlsx';
import {FitPage,ScrollPanel} from '../../components/fit';

const RANGE=/^\d{4}-\d{2}-\d{2}$/;
export function RevenueByCategory(){
  const {s,uid,toast}=useHcc();
  const [from,setFrom]=useState(s.date.slice(0,8)+'01'),[to,setTo]=useState(s.date),[basis,setBasis]=useState<RCTBasis>('business-date'),[drill,setDrill]=useState<string|null>(null);
  const allowed=rctCanView(s,uid);
  const valid=RANGE.test(from)&&RANGE.test(to)&&from<=to&&Date.parse(to)-Date.parse(from)<=366*86400000;
  const r=useMemo(()=>allowed&&valid?rctReport(s,from,to,basis):null,[allowed,valid,s.bookings,s.rooms,s.posX,s.counterX,s.closings,s.date,from,to,basis]); // eslint-disable-line react-hooks/exhaustive-deps
  if(!allowed)return <FitPage title="Revenue by category and tender" sub="Owner, Accountant and Manager only"><Empty title="Not permitted" body="This report is for the Owner, Accountant and Manager."/></FitPage>;
  const name=`HCC-revenue-by-category-${from}_${to}`;
  const csv=()=>{if(r){void saveCsv(name+'.csv',rctCsv(r));toast('CSV saved · revenue by category and tender');}};
  const xl=()=>{if(!r)return;void saveAndReport(saveBinaryFile(name+'.xlsx',rctXlsx({hotel:s.settings.hotel,clock:s.clock},r),XLSX_MIME),toast,'Excel workbook saved','the Excel workbook');};
  const th=(h:string,i:number)=><th key={h} className={i?'number':''}>{h}</th>;
  const aCols:[string,(x:RCTBlockARow)=>string|number][]=[['Room-nights sold',x=>x.nightsSold],['Occ %',x=>x.occPct.toFixed(1)+'%'],['Room tariff',x=>money(x.roomTariff)],['Extra person',x=>money(x.extraPerson)],['Meal plans',x=>money(x.mealPlans)],['Other',x=>money(x.otherRoomCharges)],['Discounts',x=>money(x.discounts)],['Corrections',x=>money(x.corrections)],['Net (excl. GST)',x=>money(x.net)],['CGST',x=>money(x.cgst)],['SGST',x=>money(x.sgst)],['Gross (incl. GST)',x=>money(x.gross)],['ADR',x=>money(x.adr)]];
  const bCols:[string,(x:RCTBlockBRow)=>string|number][]=[['Cash',x=>money(x.cash)],['UPI',x=>money(x.upi)],['Card',x=>money(x.card)],['Bank',x=>money(x.bank)],['Company credit',x=>money(x.companyCredit)],['Udhaar',x=>money(x.udhaar)],['OTA',x=>money(x.ota)],['Other',x=>money(Object.values(x.otherTenders).reduce((a,b)=>a+b,0))],['Advances in',x=>money(x.advancesReceived)],['Advances adjusted',x=>money(x.advancesAdjusted)],['Advances refunded',x=>money(x.advancesRefunded)],['Net received',x=>money(x.netReceived)]];
  const c=r?.c;
  return <FitPage title="Revenue by category and tender" sub="Revenue by room type, collections by tender, and how they tie out" actions={<>
      <button className="btn" disabled={!r} onClick={csv}><Download size={15}/> CSV</button><button className="btn" disabled={!r} onClick={xl}><FileSpreadsheet size={15}/> Excel</button>
      <button className="btn" onClick={()=>void printDocument()}><Printer size={15}/> Print</button></>}
    toolbar={<div className="row" role="group" aria-label="Report range">
      <label>From <input type="date" value={from} max={s.date} onChange={e=>setFrom(e.target.value)}/></label><label>To <input type="date" value={to} max={s.date} onChange={e=>setTo(e.target.value)}/></label>
      <label>Basis <select value={basis} onChange={e=>setBasis(e.target.value as RCTBasis)}><option value="business-date">Business date (posting)</option><option value="stay">Stay (arrival date)</option></select></label></div>}>
    {!r?<Empty title="Choose a range" body="From must not be after To, and the range is at most 366 days."/>:<>
      {r.legacy&&<p className="muted">Legacy data (old Lodge software)</p>}
      <ScrollPanel title="A. Revenue by category" label="Revenue by room category" actions={<span className="muted">Headline {money(r.aTotal.net)} excl. GST · {money(r.aTotal.gross)} incl. GST</span>}>
        <div className="table-wrap"><table><thead><tr>{['Category',...aCols.map(c=>c[0])].map(th)}</tr></thead><tbody>
          {r.a.map(x=><tr key={x.key}><td>{x.label}{x.key==='hall'&&<small> after hall booking</small>}</td>{aCols.map(([h,f])=><td key={h} className="number">{f(x)}</td>)}</tr>)}
          <tr className="cb-close"><td><strong>{r.aTotal.label}</strong></td>{aCols.map(([h,f])=><td key={h} className="number"><strong>{f(r.aTotal)}</strong></td>)}</tr></tbody></table></div>
        <p className="muted">The folio total ties to Reports › Revenue & tax. Restaurant, counter and hall rows are separate outlets and are not in it.</p>
      </ScrollPanel>
      <ScrollPanel title="B. Collections by tender" label="Collections by tender" actions={<span className="muted">Net received {money(r.bTotal.netReceived)}</span>}>
        <div className="table-wrap"><table><thead><tr>{['Category',...bCols.map(c=>c[0])].map(th)}</tr></thead><tbody>
          {r.b.map(x=><tr key={x.key}><td>{x.label}</td>{bCols.map(([h,f])=><td key={h} className="number">{f(x)}</td>)}</tr>)}
          <tr className="cb-close"><td><strong>Total</strong></td>{bCols.map(([h,f])=><td key={h} className="number"><strong>{f(r.bTotal)}</strong></td>)}</tr></tbody></table></div>
      </ScrollPanel>
      <ScrollPanel title="C. Bridge" label="Bridge from charges to collections" actions={<span className="muted">{c!.difference===0?'Ties out':'Difference '+money(c!.difference)}</span>}>
        <div className="table-wrap"><table><tbody>
          {([['Opening due',c!.openingDue],['+ Gross charges (incl. GST)',c!.gross],['− Net received',c!.netReceived],['− Advances adjusted',r.bTotal.advancesAdjusted],['= Closing due',c!.closingDue],
            ['Advances held at start',c!.openingAdvances],['Advance movement',c!.advanceMovement],['Advances held at end',c!.closingAdvances],['Difference (must be 0)',c!.difference]] as [string,number][]).map(([k,v])=><tr key={k}><td>{k}</td><td className="number">{money(v)}</td></tr>)}
        </tbody></table></div>
        <button className="text-btn" onClick={()=>setDrill(drill?null:'src')}>{drill?'Hide':'Show'} source</button>
        {drill&&<p className="muted">Block A reads the folio lines in Reports › Revenue & tax; Block B reads the receipts in Reports › Receipts & deposits. Trial balance {r.tb.balanced?'balances':'does not balance'}; receivable ledgers {money(r.tb.receivable)}.</p>}
      </ScrollPanel></>}
  </FitPage>;
}
