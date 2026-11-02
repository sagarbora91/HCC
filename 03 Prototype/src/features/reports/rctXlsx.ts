// F4-T2 · Excel workbook of the revenue-by-category-and-tender report: the same rows as the screen (rpt-revcat.ts), money as ₹ cells.
import {buildXlsx} from '../../app/xlsx';
import type {XSheet,XCell} from '../../app/xlsx';
import type {RCTReport} from '../../domain/types';
import {RCT_A_HEAD,RCT_B_HEAD,rctARow,rctBRow} from '../../domain/streams/rpt-revcat';
const cell=(v:string|number,i:number,money:boolean):XCell=>typeof v==='number'&&money&&i>0?{paise:v}:v;
export function rctSheets(meta:{hotel:string;clock:string},r:RCTReport):XSheet[]{
  const a=(x:(string|number)[])=>x.map((v,i)=>i>=4&&i<=13?cell(v,i,true):i===3?v:v);
  return [
    {name:'About',widths:[26,60],rows:[['Item','Value'],['Report','Revenue by room category and tender · rpt.money.revenue-by-category-tender'],['Property',meta.hotel],['From',{date:r.from}],['To',{date:r.to}],
      ['Basis',r.basis==='stay'?'Stay (arrival date)':'Business date'],['Generated (simulated clock)',meta.clock],...(r.legacy?[['Source','Legacy data (old Lodge software)']]:[])]},
    {name:'A Revenue',widths:[26],rows:[RCT_A_HEAD,...[...r.a,r.aTotal].map(x=>a(rctARow(x)))]},
    {name:'B Collections',widths:[26],rows:[RCT_B_HEAD,...[...r.b,r.bTotal].map(x=>rctBRow(x).map((v,i)=>cell(v,i,true)))]},
    {name:'C Bridge',widths:[30,18],rows:[['Item','Amount'],['Opening due',{paise:r.c.openingDue}],['Gross charges',{paise:r.c.gross}],['Net received',{paise:r.c.netReceived}],['Advance movement',{paise:r.c.advanceMovement}],
      ['Closing due',{paise:r.c.closingDue}],['Opening advances held',{paise:r.c.openingAdvances}],['Closing advances held',{paise:r.c.closingAdvances}],['Difference (must be 0)',{paise:r.c.difference}]]},
  ];
}
export const rctXlsx=(meta:{hotel:string;clock:string},r:RCTReport)=>buildXlsx(rctSheets(meta,r));
